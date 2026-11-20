using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

/// <summary>The engine state AGERC's menu refresh reads when a native menu opens (AGE:IAGEService).</summary>
public readonly record struct VmMenuState(bool MessageSkip, bool AutoMessage, bool ReadTextSkip, bool HiddenWindow,
                                          bool TextWait, bool YieldHandler, bool SystemMenuEnabled);

/// <summary>The interpreter's side of the shell's message commands (AGE:IAGEService): the state a menu refresh reads,
/// which the VM composes from each owner (engine layering plan §3.2, addition 4), and the hide request, which a wait
/// takes or refuses at once. The toggles and the 設定 values are requests to the shell-services native
/// (<see cref="ShellServices"/>). <see cref="VirtualMachine"/> implements it; the shell model's tests fake it.</summary>
public interface IVmMenuServices
{
    VmMenuState MenuState { get; }
    bool RequestMenuHideWindow();
}

/// <summary>
/// The AGE:IAGEService calls behind AGERC's native menu commands (docs/engine-re.md, "Native menu bar"). The menu runs
/// on the front end's thread and queues its toggles at the shell-services native; the interpreter applies them at its
/// next safe point (an instruction boundary, or the input service of a text wait), followed by CALLBACK_SETTING.BIN as
/// AGERC's command handler launches it.
/// </summary>
public sealed partial class VirtualMachine : IVmMenuServices
{
    /// <summary>What AGERC's refresh reads. The front end reads it through the waits' Invoke, on the engine
    /// thread.</summary>
    public VmMenuState MenuState => new(
        MessageSkip: _skip.MessageSkipEnabled,
        AutoMessage: _skip.AutoMessageEnabled,
        ReadTextSkip: _saves.Profile.ReadMessageSkipEnabled,
        HiddenWindow: _hiddenWindowWaiting,
        TextWait: _advTextWaitPolling,
        YieldHandler: _cur?.CoroutineYieldHandlerA is int handler && handler != -1,
        SystemMenuEnabled: _shell.SystemMenuActionsEnabled != 0);

    /// <summary>The message settings as the 設定 dialog reads them (agerc_settings_message_page_proc): message:
    /// MessageSpeed (the per-glyph delay, also ctx+0x14e9c), message:MesWinAlpha and the skip and auto native's
    /// message:AutoMessageTime0/1. The front end reads them through the waits' Invoke, on the engine thread.</summary>
    public MessageSettings MessageSettings => new(
        _text.MessageGlyphDelayMilliseconds, _text.MessageWindowAlphaSetting, _skip.AutoMessageTime0Ms,
        _skip.AutoMessageTime1Ms, _skip.AutoMessagePitch(0) ?? 0, _skip.AutoMessagePitch(1) ?? 0,
        _skip.AutoMessageOption);

    /// <summary>Menu 0x9c64 (IAGEService +0x8c, iage_service_request_hide_window@0x45f700). Native ORs bit 0x80000000
    /// into the service trigger mask while the window is hidden (any trigger ends that wait), and otherwise bit 0x20,
    /// logical action 5, which a polling text wait takes as Space's hide request. False when neither wait takes it;
    /// the menu offers the command only in those two states.</summary>
    public bool RequestMenuHideWindow()
        => _hiddenWindowWaiting
            ? TryLatchAdvCoroutineYieldTrigger(unchecked((int)0x80000000))
            : TryRequestAdvWaitCoroutineYield();

    /// <summary>Apply the shell native's queued menu commands on the VM thread, then queue CALLBACK_SETTING.BIN (each
    /// AGERC toggle command, and the 設定 dialog's OK, launches it after the service calls). A screen-mode switch has
    /// the native queue CALLBACK_WINDOW.BIN instead.</summary>
    private void ApplyMenuToggles()
    {
        if (!_shell.HasMenuRequests) return;
        foreach (ShellMenuRequest request in _shell.TakeMenuRequests())
        {
            switch (request.Kind)
            {
                case ShellMenuRequestKind.MessageSkipToggle: _skip.ToggleMessageSkipFromMenu(); break;
                case ShellMenuRequestKind.AutoMessageToggle: ToggleAutoMessageFromMenu(); break;
                case ShellMenuRequestKind.ReadMessageSkipToggle: ToggleReadMessageSkipFromMenu(); break;
                case ShellMenuRequestKind.MessageSettings: ApplyMessageSettings(request.Settings); break;
            }
            _shell.RequestSettingCallback();
        }
    }

    /// <summary>adv_toggle_auto_mode (SkipAndAuto.ToggleAutoMessageFromMenu). Enabling Auto with the skip service
    /// enabled clears run-state 0x80000000, so a polling text wait ends at once and the next page comes without
    /// input.</summary>
    private void ToggleAutoMessageFromMenu()
    {
        if (_skip.ToggleAutoMessageFromMenu() && _advTextWaitPolling) _waits.EndInputWait();
    }

    /// <summary>The 設定 dialog's OK: MessageSpeed as op 0x1b5 sets it (IAGEService +0x10 writes ctx+0x14e9c),
    /// MesWinAlpha as op 0x141 and the Auto delays as op 0x1b9.</summary>
    private void ApplyMessageSettings(MessageSettings settings)
    {
        _text.SetMessageGlyphDelayMilliseconds(settings.MessageSpeed);
        _text.SetMessageWindowAlphaSetting(settings.MesWinAlpha);
        _skip.SetAutoMessageTime(0, settings.AutoMessageTime0);
        _skip.SetAutoMessageTime(1, settings.AutoMessageTime1);
        if (!settings.CommitsAutoPitch) return;
        // The ｵｰﾄﾒｯｾｰｼﾞ page's per-character times and its check, as ops 0x2e7 and 0x2e8 set them.
        _skip.SetAutoMessagePitch(0, settings.AutoMessagePitch0);
        _skip.SetAutoMessagePitch(1, settings.AutoMessagePitch1);
        _skip.AutoMessageOption = settings.AutoMessageOption;
    }

    /// <summary>0x406cd0: toggle message:ReadTextSkip, then (at a text wait) re-check the current message against
    /// ReadTextDB. A read message starts fast-forward; turning the setting off clears adv_read_skip_state.</summary>
    private void ToggleReadMessageSkipFromMenu()
    {
        _saves.Profile.ReadMessageSkipEnabled = !_saves.Profile.ReadMessageSkipEnabled;
        if (!_advTextWaitPolling) return;
        ApplyReadTextSkip();   // with the setting off, it clears adv_read_skip_state
    }

    /// <summary>Run a queued CALLBACK_SETTING.BIN or CALLBACK_WINDOW.BIN from a text wait's input service, nested in
    /// the wait as native's launch queue runs it from the ADV tick. True when one ran.</summary>
    private bool ServiceWaitSettingCallback()
    {
        ApplyMenuToggles();
        if (_tick.StatementBlockOpen || _shell.TakeCallbackScript() is not { } callback) return false;
        bool waitPolling = _advTextWaitPolling;
        _advTextWaitPolling = false;
        _waitCallbacks++;   // Lua scripting spec §13: no reload inside the wait
        try
        {
            switch (RunCallbackScript(callback))
            {
                case FrameOutcome.RootReload: throw new RootReloadRequestedException();
                case FrameOutcome.ExitRequested: throw new ProcessExitRequestedException();
                case FrameOutcome.Halted: throw new VmHaltException();
            }
        }
        finally
        {
            _waitCallbacks--;
            _advTextWaitPolling = waitPolling;
        }
        RearmAfterWaitCallback();
        return true;
    }
}

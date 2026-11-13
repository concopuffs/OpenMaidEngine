using System.Collections.Concurrent;

namespace Ome.Engine.Vm;

/// <summary>The engine state AGERC's menu refresh reads when a native menu opens (AGE:IAGEService).</summary>
public readonly record struct VmMenuState(bool MessageSkip, bool AutoMessage, bool ReadTextSkip, bool HiddenWindow,
                                          bool TextWait, bool YieldHandler, bool SystemMenuEnabled);

/// <summary>The engine-owned message settings the 設定 dialog's ﾒｯｾｰｼﾞ page shows and its OK writes
/// (agerc_settings_message_page_proc, agerc_settings_apply): message:MessageSpeed (the per-glyph delay, also
/// ctx+0x14e9c), message:MesWinAlpha and message:AutoMessageTime0/1.</summary>
public readonly record struct VmMessageSettings(int MessageSpeed, int MesWinAlpha, long AutoMessageTime0,
                                                long AutoMessageTime1);

/// <summary>The VM's side of the shell's message commands (AGE:IAGEService): the state a menu refresh reads and the
/// requests the commands make. <see cref="VirtualMachine"/> implements it; the shell model's tests fake it.</summary>
public interface IVmMenuServices
{
    VmMenuState MenuState { get; }
    void RequestMenuMessageSkipToggle();
    void RequestMenuAutoMessageToggle();
    void RequestMenuReadMessageSkipToggle();
    bool RequestMenuHideWindow();
}

/// <summary>
/// The AGE:IAGEService calls behind AGERC's native menu commands (docs/engine-re.md, "Native menu bar"). The menu runs
/// on the frontend thread; the toggles queue here and apply at the VM's next safe point (an instruction boundary, or
/// the input service of a text wait), followed by CALLBACK_SETTING.BIN as AGERC's command handler launches it.
/// </summary>
public sealed partial class VirtualMachine : IVmMenuServices
{
    private enum MenuToggle { MessageSkip, AutoMessage, ReadMessageSkip, MessageSettings, ScreenModeSwitched }

    private readonly record struct MenuRequest(MenuToggle Kind, VmMessageSettings Settings = default);

    private readonly ConcurrentQueue<MenuRequest> _menuToggles = new();
    private volatile bool _menuTogglesPending;

    /// <summary>What AGERC's refresh reads. Called on the frontend thread while the VM is held for an open menu.</summary>
    public VmMenuState MenuState => new(
        MessageSkip: _skip.MessageSkipEnabled,
        AutoMessage: _skip.AutoMessageEnabled,
        ReadTextSkip: _sharedProfile.ReadMessageSkipEnabled,
        HiddenWindow: _hiddenWindowWaiting,
        TextWait: _advTextWaitPolling,
        YieldHandler: _cur?.CoroutineYieldHandlerA is int handler && handler != -1,
        SystemMenuEnabled: _systemMenuActionsEnabled != 0);

    /// <summary>The message settings as the 設定 dialog reads them. Called on the frontend thread while the VM is held
    /// for the open dialog.</summary>
    public VmMessageSettings MessageSettings => new(
        _messageGlyphDelayMilliseconds, _messageWindowAlphaSetting, _skip.AutoMessageTime0Ms, _skip.AutoMessageTime1Ms);

    /// <summary>The 設定 dialog's OK (agerc_settings_apply, then IAGEService +0x20 launching CALLBACK_SETTING.BIN):
    /// MessageSpeed as op 0x1b5 sets it (IAGEService +0x10 writes ctx+0x14e9c), MesWinAlpha as op 0x141 and the Auto
    /// delays as op 0x1b9. Applied at the VM's next safe point, followed by CALLBACK_SETTING.BIN.</summary>
    public void RequestMessageSettings(VmMessageSettings settings)
        => QueueMenuRequest(new MenuRequest(MenuToggle.MessageSettings, settings));

    /// <summary>Menu 0x9c63 (IAGEService +0x88, adv_toggle_skip_mode@0x406c20).</summary>
    public void RequestMenuMessageSkipToggle() => QueueMenuToggle(MenuToggle.MessageSkip);

    /// <summary>Menu 0x9c65 (IAGEService +0x84, adv_toggle_auto_mode@0x406b70).</summary>
    public void RequestMenuAutoMessageToggle() => QueueMenuToggle(MenuToggle.AutoMessage);

    /// <summary>Menu 0x9c6f (IAGEService +0xd8 at 0x406cd0): toggle message:ReadTextSkip and refresh read skip.</summary>
    public void RequestMenuReadMessageSkipToggle() => QueueMenuToggle(MenuToggle.ReadMessageSkip);

    /// <summary>Menu 0x9c64 (IAGEService +0x8c, iage_service_request_hide_window@0x45f700). Native ORs bit 0x80000000
    /// into the service trigger mask while the window is hidden (any trigger ends that wait), and otherwise bit 0x20,
    /// logical action 5, which a polling text wait takes as Space's hide request. False when neither wait takes it;
    /// the menu offers the command only in those two states.</summary>
    public bool RequestMenuHideWindow()
        => _hiddenWindowWaiting
            ? TryLatchAdvCoroutineYieldTrigger(unchecked((int)0x80000000))
            : TryRequestAdvWaitCoroutineYield();

    private void QueueMenuToggle(MenuToggle toggle) => QueueMenuRequest(new MenuRequest(toggle));

    private void QueueMenuRequest(MenuRequest request)
    {
        _menuToggles.Enqueue(request);
        _menuTogglesPending = true;
        _host.WakeInputCallbackService();
    }

    /// <summary>Apply queued menu toggles on the VM thread, then queue CALLBACK_SETTING.BIN (each AGERC toggle command,
    /// and the 設定 dialog's OK, launches it after the service calls). A screen-mode switch queues CALLBACK_WINDOW.BIN
    /// instead.</summary>
    private void ApplyMenuToggles()
    {
        if (!_menuTogglesPending) return;
        _menuTogglesPending = false;
        while (_menuToggles.TryDequeue(out MenuRequest request))
        {
            switch (request.Kind)
            {
                case MenuToggle.MessageSkip: _skip.ToggleMessageSkipFromMenu(); break;
                case MenuToggle.AutoMessage: ToggleAutoMessageFromMenu(); break;
                case MenuToggle.ReadMessageSkip: ToggleReadMessageSkipFromMenu(); break;
                case MenuToggle.MessageSettings: ApplyMessageSettings(request.Settings); break;
                case MenuToggle.ScreenModeSwitched:
                    _windowCallbackPending = true;
                    continue;
            }
            _settingCallbackPending = true;
        }
    }

    /// <summary>adv_toggle_auto_mode (SkipAndAuto.ToggleAutoMessageFromMenu). Enabling Auto with the skip service
    /// enabled clears run-state 0x80000000, so a polling text wait ends at once and the next page comes without
    /// input.</summary>
    private void ToggleAutoMessageFromMenu()
    {
        if (_skip.ToggleAutoMessageFromMenu() && _advTextWaitPolling) _host.EndInputWait();
    }

    private void ApplyMessageSettings(VmMessageSettings settings)
    {
        _messageGlyphDelayMilliseconds = System.Math.Max(0, settings.MessageSpeed);
        _host.SetMessageGlyphDelayMilliseconds(_messageGlyphDelayMilliseconds);
        _messageWindowAlphaSetting = settings.MesWinAlpha;
        _host.SetMessageWindowAlphaSetting(_messageWindowAlphaSetting);
        _skip.SetAutoMessageTime(0, settings.AutoMessageTime0);
        _skip.SetAutoMessageTime(1, settings.AutoMessageTime1);
    }

    /// <summary>0x406cd0: toggle message:ReadTextSkip, then (at a text wait) re-check the current message against
    /// ReadTextDB. A read message starts fast-forward; turning the setting off clears adv_read_skip_state.</summary>
    private void ToggleReadMessageSkipFromMenu()
    {
        _sharedProfile.ReadMessageSkipEnabled = !_sharedProfile.ReadMessageSkipEnabled;
        if (!_advTextWaitPolling) return;
        ApplyReadTextSkip();   // with the setting off, it clears adv_read_skip_state
    }

    /// <summary>Run a queued CALLBACK_SETTING.BIN or CALLBACK_WINDOW.BIN from a text wait's input service, nested in
    /// the wait as native's launch queue runs it from the ADV tick. True when one ran.</summary>
    private bool ServiceWaitSettingCallback()
    {
        ApplyMenuToggles();
        if (_statementBlockActive || TakePendingCallbackScript() is not { } callback) return false;
        bool waitPolling = _advTextWaitPolling;
        _advTextWaitPolling = false;
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
            _advTextWaitPolling = waitPolling;
        }
        RearmAfterWaitCallback();
        return true;
    }
}

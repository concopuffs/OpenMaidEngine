using System.Collections.Concurrent;

namespace Age.Engine.Vm;

/// <summary>The engine state AGERC's menu refresh reads when a native menu opens (AGE:IAGEService).</summary>
public readonly record struct VmMenuState(bool MessageSkip, bool AutoMessage, bool ReadTextSkip, bool HiddenWindow,
                                          bool TextWait, bool YieldHandler, bool SystemMenuEnabled);

/// <summary>
/// The AGE:IAGEService calls behind AGERC's native menu commands (docs/engine-re.md, "Native menu bar"). The menu runs
/// on the frontend thread; the toggles queue here and apply at the VM's next safe point (an instruction boundary, or
/// the input service of a text wait), followed by CALLBACK_SETTING.BIN as AGERC's command handler launches it.
/// </summary>
public sealed partial class VirtualMachine
{
    private enum MenuToggle { MessageSkip, AutoMessage, ReadMessageSkip }

    private readonly ConcurrentQueue<MenuToggle> _menuToggles = new();
    private volatile bool _menuTogglesPending;
    // ctx+0x55100 adv_skip_service_enabled: scene_context_init_reset sets it, op 0x19b clears it, op 0x19c sets it.
    // The menu's Skip and Auto toggles change the active fast-forward state only while it is set.
    private bool _advSkipServiceEnabled = true;

    /// <summary>What AGERC's refresh reads. Called on the frontend thread while the VM is held for an open menu.</summary>
    public VmMenuState MenuState => new(
        MessageSkip: _messageSkipEnabled,
        AutoMessage: _autoMessageEnabled,
        ReadTextSkip: _sharedProfile.ReadMessageSkipEnabled,
        HiddenWindow: _hiddenWindowWaiting,
        TextWait: _advTextWaitPolling,
        YieldHandler: _cur?.CoroutineYieldHandlerA is int handler && handler != -1,
        SystemMenuEnabled: _systemMenuActionsEnabled != 0);

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

    private void QueueMenuToggle(MenuToggle toggle)
    {
        _menuToggles.Enqueue(toggle);
        _menuTogglesPending = true;
        _host.WakeInputCallbackService();
    }

    /// <summary>Apply queued menu toggles on the VM thread, then queue CALLBACK_SETTING.BIN (each AGERC toggle command
    /// launches it after the service call).</summary>
    private void ApplyMenuToggles()
    {
        if (!_menuTogglesPending) return;
        _menuTogglesPending = false;
        while (_menuToggles.TryDequeue(out MenuToggle toggle))
        {
            switch (toggle)
            {
                case MenuToggle.MessageSkip: ToggleMessageSkipFromMenu(); break;
                case MenuToggle.AutoMessage: ToggleAutoMessageFromMenu(); break;
                case MenuToggle.ReadMessageSkip: ToggleReadMessageSkipFromMenu(); break;
            }
            _settingCallbackPending = true;
        }
    }

    /// <summary>adv_toggle_skip_mode: flip persistent Skip (enabling clears Auto unless set:CoexistMesSkip). With the
    /// skip service enabled, enabling starts fast-forward at once: it ends a text wait, completes a reveal and stops
    /// the wait indicator. Disabling clears only the active flag (ctx+0x13dc) and leaves run-state 0x08000000, so
    /// fast-forward runs on until op 0xfa or 0x19c recomputes it at the next page.</summary>
    private void ToggleMessageSkipFromMenu()
    {
        if (!_messageSkipEnabled)
        {
            if (!_o.CoexistMessageSkip) _autoMessageEnabled = false;
            _messageSkipEnabled = true;
        }
        else
        {
            _messageSkipEnabled = false;
        }
        if (!_advSkipServiceEnabled || !_messageSkipEnabled) return;
        _messageSkipServiceActive = true;
        _host.SetMessageSkipActive(true);
    }

    /// <summary>adv_toggle_auto_mode: flip Auto (enabling clears Skip unless set:CoexistMesSkip). Native arms its sleep
    /// timer with AutoMessageTime1 unless a voice is pending; the port's text wait arms its auto timer the same way
    /// when it first sees Auto enabled. With the skip service enabled, enabling also clears run-state 0x80000000, so a
    /// polling text wait ends at once and the next page comes without input. It sets ctx+0x13dc (the injected skip
    /// action) from the persistent Skip but leaves run-state 0x08000000, which the port's active fast-forward flag
    /// stands for, to the next page's op 0xfa.</summary>
    private void ToggleAutoMessageFromMenu()
    {
        if (_autoMessageEnabled)
        {
            _autoMessageEnabled = false;
            return;
        }
        _autoMessageEnabled = true;
        if (!_o.CoexistMessageSkip) _messageSkipEnabled = false;
        if (_advSkipServiceEnabled && _advTextWaitPolling) _host.EndInputWait();
    }

    /// <summary>0x406cd0: toggle message:ReadTextSkip, then (at a text wait) re-check the current message against
    /// ReadTextDB. A read message starts fast-forward; turning the setting off clears adv_read_skip_state.</summary>
    private void ToggleReadMessageSkipFromMenu()
    {
        _sharedProfile.ReadMessageSkipEnabled = !_sharedProfile.ReadMessageSkipEnabled;
        if (!_advTextWaitPolling) return;
        if (!_sharedProfile.ReadMessageSkipEnabled)
        {
            _advReadSkipState = false;
            return;
        }
        RefreshAdvReadSkipState();
    }

    /// <summary>Run the queued CALLBACK_SETTING.BIN from a text wait's input service, nested in the wait as native's
    /// launch queue runs it from the ADV tick. True when it ran.</summary>
    private bool ServiceWaitSettingCallback()
    {
        ApplyMenuToggles();
        if (!_settingCallbackPending || _statementBlockActive) return false;
        _settingCallbackPending = false;
        bool waitPolling = _advTextWaitPolling;
        _advTextWaitPolling = false;
        try
        {
            switch (RunSettingCallback())
            {
                case FrameOutcome.RootReload: throw new RootReloadRequestedException();
                case FrameOutcome.ExitRequested: throw new ProcessExitRequestedException();
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

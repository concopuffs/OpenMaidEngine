using Age.Engine.Diagnostics;
using Age.Engine.Model;

namespace Age.Engine.Vm;

/// <summary>
/// Native set:CancelMesSkipOnClick (SYS4INI CANCELMESSKIPONCLICK). adv_interpreter_tick@0x410fb0 runs only while
/// ADV fast-forward (run-state 0x08000000) is active, and with a nonzero setting it runs a press/release state
/// machine (ctx+0x6da80) on logical action 4 (bit 0x10) before dispatching the next op:
/// <list type="bullet">
/// <item>bit set: the bit is removed from the tick's mask, and state 1 becomes 2;</item>
/// <item>bit clear in state 2: state 0, input_flush_action_triggers, run-state 0x08000000 and both all-message Skip
/// fields (ctx+0x13dc, ctx+0x550fc) cleared, message:ReadTextSkip set to 0; with value 2 CALLBACK_SETTING.BIN is
/// queued through script_launch_queue_enqueue@0x40f820 and the tick returns without dispatching;</item>
/// <item>bit clear otherwise: state 1.</item>
/// </list>
/// A nonzero setting also withholds the tick's hotspot hover/click dispatch during fast-forward, so the frontend
/// routes an action-4 click to <see cref="TryLatchMessageSkipCancelClick"/> instead of hotspots or page advance.
/// The native launch queue suspends the current frame and runs the script in a reserved frame, refusing while a
/// statement block is open (the block's close dispatches it); the port runs it nested at the first instruction
/// boundary outside a statement block.
/// </summary>
public sealed partial class VirtualMachine
{
    private const string SettingCallbackScript = "CALLBACK_SETTING.BIN";

    private int _skipClickCancelState;        // native ctx+0x6da80
    private int _skipClickCancelTriggers;     // latched action-4 click triggers since the last tick
    private bool _settingCallbackPending;     // CALLBACK_SETTING.BIN queued by a mode-2 cancel or a menu toggle

    private bool AdvFastForwardActive => _messageSkipServiceActive || _host.IsMessageSkipActive;

    /// <summary>True while a completed action-4 click would cancel fast-forward instead of reaching hotspots.</summary>
    public bool IsMessageSkipClickCancelArmed => _o.CancelMessageSkipOnClick != 0 && AdvFastForwardActive;

    /// <summary>Latch an action-4 click trigger for the click-cancel state machine. False when the setting is off
    /// or fast-forward is inactive, in which case the click keeps its ordinary routing.</summary>
    public bool TryLatchMessageSkipCancelClick()
    {
        if (!IsMessageSkipClickCancelArmed) return false;
        Interlocked.Increment(ref _skipClickCancelTriggers);
        _host.WakeInputCallbackService();
        return true;
    }

    /// <summary>The services native's tick runs before dispatching an op: the click-cancel machine (with
    /// CANCELMESSKIPONCLICK set), queued native-menu toggles, then any queued CALLBACK_SETTING.BIN launch. Non-null
    /// only when the callback ends the enclosing frame.</summary>
    private FrameOutcome? ServiceInstructionBoundary()
    {
        if (_o.CancelMessageSkipOnClick != 0) ServiceMessageSkipClickCancel();
        ApplyMenuToggles();
        if (!_settingCallbackPending || _statementBlockActive) return null;
        _settingCallbackPending = false;
        return RunSettingCallback();
    }

    /// <summary>One adv_interpreter_tick pass of the click-cancel machine.</summary>
    private void ServiceMessageSkipClickCancel()
    {
        if (AdvFastForwardActive)
        {
            bool clicked = Interlocked.Exchange(ref _skipClickCancelTriggers, 0) != 0;
            bool held = (InputBindings.PollActionMask() & 0x10) != 0;
            if (held || clicked)
            {
                if (_skipClickCancelState == 1) _skipClickCancelState = 2;
            }
            else if (_skipClickCancelState == 2)
            {
                CancelMessageSkipFromClick();
            }
            else
            {
                _skipClickCancelState = 1;
            }
        }
        else
        {
            Interlocked.Exchange(ref _skipClickCancelTriggers, 0);
        }
    }

    private void CancelMessageSkipFromClick()
    {
        _skipClickCancelState = 0;
        Interlocked.Exchange(ref _queuedInputCallbackMask, 0);   // input_flush_action_triggers
        TakeHeldTriggers();
        _messageSkipEnabled = false;
        _messageSkipServiceActive = false;
        _sharedProfile.ReadMessageSkipEnabled = false;
        _advReadSkipState = false;
        _host.SetMessageSkipActive(false);
        if (_o.CancelMessageSkipOnClick == 2) _settingCallbackPending = true;
    }

    private FrameOutcome? RunSettingCallback()
    {
        Script? callback = _provider?.GetByName(SettingCallbackScript);
        if (callback == null) return null;
        if (_depth >= _o.CallDepthCap)
        {
            HaltReason ??= "call-depth-exceeded";
            return FrameOutcome.Halted;
        }
        int entry = callback.IndexByOffset.TryGetValue(0, out int ci) ? ci : 0;
        CallScriptDispatches++;
        _sink.Emit(TraceEvent.CallScript(callback.PackedId, callback.Name));
        FrameOutcome outcome = RunFrame(new ExecFrame(callback, entry), FrameCause.CallScript, callback.PackedId);
        return outcome switch
        {
            FrameOutcome.Halted => FrameOutcome.Halted,
            FrameOutcome.RootReload => FrameOutcome.RootReload,
            FrameOutcome.ExitRequested => FrameOutcome.ExitRequested,
            _ => null,
        };
    }
}

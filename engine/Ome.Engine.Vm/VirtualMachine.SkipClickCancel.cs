using Ome.Engine.Diagnostics;
using Ome.Engine.Model;

namespace Ome.Engine.Vm;

/// <summary>
/// The interpreter's part of native set:CancelMesSkipOnClick (SYS4INI CANCELMESSKIPONCLICK). The press/release machine
/// on logical action 4 is the skip and auto native's (SkipAndAuto.ClickCancel.cs); adv_interpreter_tick@0x410fb0 runs
/// it before dispatching the next op, and a cancel also flushes the input triggers, sets message:ReadTextSkip to 0 and,
/// with value 2, queues CALLBACK_SETTING.BIN through script_launch_queue_enqueue@0x40f820. The native launch queue
/// suspends the current frame and runs the script in a reserved frame, refusing while a statement block is open (the
/// block's close dispatches it); the port runs it nested at the first instruction boundary outside a statement block.
/// </summary>
public sealed partial class VirtualMachine
{
    private const string SettingCallbackScript = "CALLBACK_SETTING.BIN";

    private bool _settingCallbackPending;     // CALLBACK_SETTING.BIN queued by a mode-2 cancel or a menu toggle

    private bool AdvFastForwardActive => _skip.FastForwardActive;

    /// <summary>True while a completed action-4 click would cancel fast-forward instead of reaching hotspots.</summary>
    public bool IsMessageSkipClickCancelArmed => _skip.ClickCancelArmed;

    /// <summary>Latch an action-4 click trigger for the click-cancel state machine. False when the setting is off
    /// or fast-forward is inactive, in which case the click keeps its ordinary routing.</summary>
    public bool TryLatchMessageSkipCancelClick()
    {
        if (!_skip.TryLatchCancelClick()) return false;
        _host.WakeInputCallbackService();
        return true;
    }

    /// <summary>The services native's tick runs before dispatching an op: the click-cancel machine (with
    /// CANCELMESSKIPONCLICK set), queued native-menu toggles, then any queued CALLBACK_SETTING.BIN or
    /// CALLBACK_WINDOW.BIN launch. Non-null only when the callback ends the enclosing frame.</summary>
    private FrameOutcome? ServiceInstructionBoundary()
    {
        if (_skip.ServiceClickCancel(InputBindings)) CancelMessageSkipFromClick();
        ApplyMenuToggles();
        if (_statementBlockActive || TakePendingCallbackScript() is not { } callback) return null;
        return RunCallbackScript(callback);
    }

    private void CancelMessageSkipFromClick()
    {
        Interlocked.Exchange(ref _queuedInputCallbackMask, 0);   // input_flush_action_triggers
        TakeHeldTriggers();
        _sharedProfile.ReadMessageSkipEnabled = false;
        _skip.CancelMessageSkipFromClick();
        if (_skip.Settings.CancelMessageSkipOnClick == 2) _settingCallbackPending = true;
    }

    private FrameOutcome? RunCallbackScript(string name)
    {
        Script? callback = _provider?.GetByName(name);
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

using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0xfa: end fast-forward unless action 6 is held, then wait for input
    private StepResult OpRefreshAdvSkipInputState(Instruction ins, int pc)
    {
        RefreshAdvSkipInputState();
        if (AdvFastForwardActive) return StepResult.Next;
        // With fast-forward off, the op sets run-state 0x80000000: the ADV input poll a text wait runs, with no
        // page. Kamidori's SHOWALLMAP holds the world map on it until a click. Persistent Skip's injected action
        // 6 ends it at the first poll and starts fast-forward again.
        if (_o.HaltAtWaitForInput) { HaltReason ??= "wait-for-input"; return StepResult.Halt; }
        if (_messageSkipInjected) SetScriptFastForward(true);
        WaitAtAdvInputPoll(0, textPage: false);
        return StepResult.Next;
    }

    // 0x88: persistent all-message fast-forward service state
    private StepResult OpSetMessageSkip(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0x88_set_message_skip@0x41f130 writes ctx+0x13dc and ctx+0x550fc alike. Enabling sets no run-state
        // bit: the next polling text wait starts fast-forward from the injected action 6, so SC0000's Skip button
        // still sleeps its 100 ms and the page passes once the callback returns into op 0x72. Disabling clears
        // run-state 0x8000000.
        _messageSkipEnabled = Read(a[0]) != 0;
        _messageSkipInjected = _messageSkipEnabled;
        if (!_messageSkipEnabled)
        {
            SetScriptFastForward(false);
            EndPhysicalFastForward();
        }
        return StepResult.Next;
    }

    // 0x19a: persistent state used by the SO001 active overlay
    private StepResult OpGetMessageSkipEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _messageSkipEnabled ? 1 : 0); return StepResult.Next;
    }

    // 0x19b: preserve the toggle while leaving ADV presentation
    private StepResult OpSuspendAdvSkipService(Instruction ins, int pc)
    {
        _advSkipServiceEnabled = false;
        _messageSkipInjected = false;   // ctx+0x13dc; the toggle (ctx+0x550fc) survives
        _messageSkipServiceActive = false;
        _host.SetMessageSkipActive(false);
        EndPhysicalFastForward();   // it clears run-state 0x8000000, however started
        return StepResult.Next;
    }

    // 0x19c: recompute active fast-forward on ADV entry
    private StepResult OpResumeAdvSkipService(Instruction ins, int pc)
    {
        _advSkipServiceEnabled = true;
        // ctx+0x13dc follows the toggle; a read-skip-only resume leaves it as it was.
        if (_messageSkipEnabled) _messageSkipInjected = true;
        else if (!_advReadSkipState) _messageSkipInjected = false;
        _messageSkipServiceActive =
            _messageSkipEnabled || _advReadSkipState || _host.IsAdvReadSkipActive;
        _host.SetMessageSkipActive(_messageSkipServiceActive);
        EndPhysicalFastForward();   // it rebuilds run-state 0x8000000 from persistent Skip and read-skip only
        return StepResult.Next;
    }

    // 0x1c7: persistent Skip or host-supplied Ctrl fast-forward
    private StepResult OpGetMessageSkip(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Native persistent state and the independently polled physical action-6 channel both
        // re-arm the transient run-state bit consumed by this query.
        Write(a[0], _messageSkipServiceActive || _host.IsMessageSkipActive ? 1 : 0); return StepResult.Next;
    }

    // 0x1cc: per-message read/click skip service state
    private StepResult OpGetAdvReadSkipState(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _advReadSkipState || _host.IsAdvReadSkipActive ? 1 : 0); return StepResult.Next;
    }

    // 0x1ca: engine setting message:ReadTextSkip
    private StepResult OpSetReadMessageSkip(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0x1ca_set_read_message_skip@0x41f880 only writes the setting; the next page reads it.
        _sharedProfile.ReadMessageSkipEnabled = Read(a[0]) != 0;
        return StepResult.Next;
    }

    // 0x1cb
    private StepResult OpGetReadMessageSkip(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _sharedProfile.ReadMessageSkipEnabled ? 1 : 0);
        return StepResult.Next;
    }

    // 0x1b6: VM service state used by the ADV redraw callback
    private StepResult OpGetAutoMessage(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _autoMessageEnabled ? 1 : 0); return StepResult.Next;
    }

    // 0x1b7
    private StepResult OpSetAutoMessage(Instruction ins, int pc)
    {
        var a = ins.Args;
        _autoMessageEnabled = Read(a[0]) != 0; return StepResult.Next;
    }

    // 0x1b8 (selector 0=post-voice Time0, 1=unvoiced Time1, out)
    private StepResult OpGetAutoMessageTime(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[1], Read(a[0]) == 0 ? _autoMessageTime0Ms : _autoMessageTime1Ms); return StepResult.Next;
    }

    // 0x1b9 (selector, milliseconds)
    private StepResult OpSetAutoMessageTime(Instruction ins, int pc)
    {
        var a = ins.Args;
        SetAutoMessageTime((int)Read(a[0]), Read(a[1]));
        return StepResult.Next;
    }

    // 0x1bc resets native per-message voice/queued-voice state
    private StepResult OpResetMessageVoiceState(Instruction ins, int pc)
    {
        // Native stops channel 12, drops the Skip-queued voice and clears voice_mute_next_flags.
        _host.StopVoice();
        _voiceMuteNextFlags = 0;
        _autoVoicePending = false; return StepResult.Next;
    }

    // 0x101 clears transient input/run bits, not op 0x88 state
    private StepResult OpResetMessageSkipInput(Instruction ins, int pc)
    {
        // Native also writes the CancelMesSkipOnClick press/release state ctx+0x6da80 = 0, and polls the input
        // latch, discarding it.
        _skipClickCancelState = 0;
        Interlocked.Exchange(ref _skipClickCancelTriggers, 0);
        TakeHeldTriggers();
        // It clears run-state 0x8000000 (0x4170ce), however it started, and sets ctx+0x6da74, so a held action 6
        // starts no reveal's fast-forward until a reveal runs with it released. Persistent Skip and read-skip start
        // it again at the next text wait, or when the callback that ran this op returns into op 0x72.
        SetScriptFastForward(false);
        EndPhysicalFastForward();
        lock (_physicalFastForwardLock) _revealPhysicalSkipSuppressed = true;
        return StepResult.Next;
    }

    /// <summary>The entry of op_0xfa_refresh_adv_skip_input_state (/v2 0x416de0, 4.33G 0x417580). It polls the held
    /// mask, which carries no Skip injection, so without action 6 held it clears run-state 0x08000000 even on a read
    /// message. With fast-forward then off it discards the input latch, as op 0x72 does.</summary>
    private void RefreshAdvSkipInputState()
    {
        ReconcilePhysicalFastForward();
        if (!PhysicalSkipActionHeld) SetScriptFastForward(false);
        DiscardHeldTriggersUnlessFastForward();
    }
}

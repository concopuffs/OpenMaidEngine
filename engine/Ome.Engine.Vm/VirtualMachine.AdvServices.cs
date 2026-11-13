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
        if (_skip.MessageSkipInjected) _skip.SetScriptFastForward(true);
        WaitAtAdvInputPoll(0, textPage: false);
        return StepResult.Next;
    }

    // 0x88: persistent all-message fast-forward service state
    private StepResult OpSetMessageSkip(Instruction ins, int pc)
    {
        var a = ins.Args;
        _skip.SetMessageSkip(Read(a[0]) != 0);
        return StepResult.Next;
    }

    // 0x19a: persistent state used by the SO001 active overlay
    private StepResult OpGetMessageSkipEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _skip.MessageSkipEnabled ? 1 : 0); return StepResult.Next;
    }

    // 0x19b: preserve the toggle while leaving ADV presentation
    private StepResult OpSuspendAdvSkipService(Instruction ins, int pc)
    {
        _skip.SuspendAdvSkipService();
        return StepResult.Next;
    }

    // 0x19c: recompute active fast-forward on ADV entry
    private StepResult OpResumeAdvSkipService(Instruction ins, int pc)
    {
        _skip.ResumeAdvSkipService();
        return StepResult.Next;
    }

    // 0x1c7: persistent Skip, read skip or held Ctrl: run-state 0x08000000
    private StepResult OpGetMessageSkip(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Native persistent state and the independently polled physical action-6 channel both re-arm the transient
        // run-state bit consumed by this query.
        Write(a[0], AdvFastForwardActive ? 1 : 0); return StepResult.Next;
    }

    // 0x1cc: per-message read/click skip service state
    private StepResult OpGetAdvReadSkipState(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _skip.ReadSkipActive ? 1 : 0); return StepResult.Next;
    }

    // 0x1ca: engine setting message:ReadTextSkip
    private StepResult OpSetReadMessageSkip(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0x1ca_set_read_message_skip@0x41f880 only writes the setting; the next page reads it.
        _saves.Profile.ReadMessageSkipEnabled = Read(a[0]) != 0;
        return StepResult.Next;
    }

    // 0x1cb
    private StepResult OpGetReadMessageSkip(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _saves.Profile.ReadMessageSkipEnabled ? 1 : 0);
        return StepResult.Next;
    }

    // 0x1b6: VM service state used by the ADV redraw callback
    private StepResult OpGetAutoMessage(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _skip.AutoMessageEnabled ? 1 : 0); return StepResult.Next;
    }

    // 0x1b7
    private StepResult OpSetAutoMessage(Instruction ins, int pc)
    {
        var a = ins.Args;
        _skip.SetAutoMessage(Read(a[0]) != 0); return StepResult.Next;
    }

    // 0x1b8 (selector 0=post-voice Time0, 1=unvoiced Time1, out)
    private StepResult OpGetAutoMessageTime(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[1], Read(a[0]) == 0 ? _skip.AutoMessageTime0Ms : _skip.AutoMessageTime1Ms); return StepResult.Next;
    }

    // 0x1b9 (selector, milliseconds)
    private StepResult OpSetAutoMessageTime(Instruction ins, int pc)
    {
        var a = ins.Args;
        _skip.SetAutoMessageTime((int)Read(a[0]), Read(a[1]));
        return StepResult.Next;
    }

    // 0x1bc resets native per-message voice/queued-voice state
    private StepResult OpResetMessageVoiceState(Instruction ins, int pc)
    {
        // Native stops channel 12, drops the Skip-queued voice and clears voice_mute_next_flags.
        _audio.ResetMessageVoiceState();
        _skip.ClearPendingVoice(); return StepResult.Next;
    }

    // 0x101 clears transient input/run bits, not op 0x88 state
    private StepResult OpResetMessageSkipInput(Instruction ins, int pc)
    {
        // Native polls the input latch and discards it, and resets the skip input (SkipAndAuto.ResetMessageSkipInput:
        // the click machine, run-state 0x8000000 however it started, and ctx+0x6da74).
        TakeHeldTriggers();
        _skip.ResetMessageSkipInput();
        return StepResult.Next;
    }

    /// <summary>The entry of op_0xfa_refresh_adv_skip_input_state (/v2 0x416de0, 4.33G 0x417580). It polls the held
    /// mask, which carries no Skip injection, so without action 6 held it clears run-state 0x08000000 even on a read
    /// message. With fast-forward then off it discards the input latch, as op 0x72 does.</summary>
    private void RefreshAdvSkipInputState()
    {
        _skip.RefreshAdvSkipInputState(InputBindings);
        DiscardHeldTriggersUnlessFastForward();
    }
}

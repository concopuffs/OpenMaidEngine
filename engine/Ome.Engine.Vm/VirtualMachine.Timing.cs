using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    private readonly record struct TimedCallback(long DeadlineMs, int PrimaryOffset, int CatchUpOffset);

    // The op-0xd3..0xd5 timed sequence is engine state (entries +0x5f694, guard +0x5f6b8), shared by every frame.
    private readonly List<TimedCallback> _timedCallbacks = new();
    private int _timedCallbackCursor;
    private long _timedCallbackStartedAtMs;
    private int _timedCallbackAbortOffset = -1;
    private Script? _timedCallbackGuard;

    private void ResetTimedCallbackSequence()
    {
        _timedCallbacks.Clear();
        _timedCallbackCursor = 0;
        _timedCallbackStartedAtMs = 0;
        _timedCallbackAbortOffset = -1;
        _timedCallbackGuard = null;
    }

    private StepResult OpGetMonotonicTimeMs(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], unchecked((int)_host.InputClockMilliseconds)); return StepResult.Next;
    }

    // 0xc8 (duration) — pause the host duration ms; headless hosts no-op (parity). Frame pacing.
    private StepResult OpSleep(Instruction ins, int pc)
    {
        var a = ins.Args;
        // sleep_op_0xc8@0x420ec0 arms nothing while ADV fast-forward (run-state 0x08000000) is active.
        if (AdvFastForwardActive) return StepResult.Next;
        _host.Sleep(Read(a[0])); return StepResult.Next;
    }

    // 0xd3: clear the engine-wide relative schedule
    private StepResult OpBeginTimedCallbackSequence(Instruction ins, int pc)
    {
        ResetTimedCallbackSequence();
        return StepResult.Next;
    }

    // 0xd4: (interval ms, count, on-time PC, catch-up PC)
    private StepResult OpAppendRelativeTimedCallbacks(Instruction ins, int pc)
    {
        var a = ins.Args;
        long intervalMs = Read(a[0]);
        int count = System.Math.Max(0, unchecked((int)Read(a[1])));
        if (count > MaxScriptRunLength - _timedCallbacks.Count)
        {
            HaltReason ??= $"timed-callback-count-too-large:{count}";
            return StepResult.Halt;
        }
        int primaryOffset = unchecked((int)Read(a[2]));
        int catchUpOffset = unchecked((int)Read(a[3]));
        long deadlineMs = _timedCallbacks.Count == 0 ? 0 : _timedCallbacks[^1].DeadlineMs;
        for (int i = 0; i < count; i++)
        {
            deadlineMs += intervalMs;
            _timedCallbacks.Add(new TimedCallback(deadlineMs, primaryOffset, catchUpOffset));
        }
        return StepResult.Next;
    }

    // 0xd5: dispatch each scheduled local callback, resuming here after ret
    private StepResult OpRunTimedCallbackSequence(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0xd5_handler@0x421090 starts the sequence when its cursor is 0: it records the abort pc and the
        // running script as the guard (+0x5f6b8) and starts the clock.
        if (_timedCallbackCursor == 0)
        {
            TakeHeldTriggers();   // input_flush_action_triggers
            _timedCallbackStartedAtMs = _host.InputClockMilliseconds;
            _timedCallbackAbortOffset = unchecked((int)Read(a[0]));
            _timedCallbackGuard = _cur.Script;
        }
        // Native op 0xd5 stops at last_index rather than count. The final entry is a
        // look-ahead sentinel: it supplies the next deadline for the preceding event but
        // is not itself dispatched.
        if (_timedCallbackCursor >= _timedCallbacks.Count - 1) return StepResult.Next;
        if (TryAbortTimedCallbackSequence(out int abortTarget)) return StepResult.Jump(abortTarget);

        var callback = _timedCallbacks[_timedCallbackCursor];
        long elapsedMs = _host.InputClockMilliseconds - _timedCallbackStartedAtMs;
        if (elapsedMs < callback.DeadlineMs)
        {
            _host.WaitForTimedCallbackDeadline(callback.DeadlineMs - elapsedMs);
            elapsedMs = _host.InputClockMilliseconds - _timedCallbackStartedAtMs;
            if (TryAbortTimedCallbackSequence(out abortTarget)) return StepResult.Jump(abortTarget);
        }

        bool fellBehind = _timedCallbackCursor + 1 < _timedCallbacks.Count
            && _timedCallbacks[_timedCallbackCursor + 1].DeadlineMs < elapsedMs;
        int targetOffset = fellBehind ? callback.CatchUpOffset : callback.PrimaryOffset;
        // timed_callback_sequence_tick@0x408170: the schedule is engine-wide, so a frame that runs op 0xd5
        // while another script's sequence is under way dispatches that script's entry. When the guard is
        // another script, AGE raises Depth before the cursor moves; op 0xd5 recorded length 0, so 再試行 and
        // 無視 both run it again and the error repeats until 中止.
        if (targetOffset != -1 && _timedCallbackGuard is { } guard && !IsSameScript(guard, _cur.Script))
            return StepResult.Jump(RaiseInstructionScriptError(DepthError(guard), pc, ignoreSkipsInstruction: false));
        _timedCallbackCursor++;
        if (targetOffset < 0 || !_cur.Script.IndexByOffset.TryGetValue(targetOffset, out int target))
            return StepResult.Jump(pc);
        _cur.CallStack.Add(pc);
        return StepResult.Jump(target);
    }

    /// <summary>timed_callback_sequence_tick@0x408170 polls the input latch on every tick of run-state 0x40, which op
    /// 0xd5 sets until its last entry. Any trigger moves the frame to the abort pc (op 0xd5's operand, recorded at
    /// cursor 0) and clears 0x40; with no abort pc (-1: 16 of Himegari's 17 sequences, 14 of Kamidori's 15) the trigger
    /// is dropped. The cursor stays. The port polls at each op 0xd5 step, before and after the wait for the next entry
    /// (the literal entry intervals in both games' scripts are 33 ms at most).</summary>
    private bool TryAbortTimedCallbackSequence(out int target)
    {
        target = -1;
        if (TakeHeldTriggers() == 0 || _timedCallbackAbortOffset == -1) return false;
        return _cur.Script.IndexByOffset.TryGetValue(_timedCallbackAbortOffset, out target);
    }
}

using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    private readonly record struct TimedCallback(long DeadlineMs, int PrimaryOffset, int CatchUpOffset);


    private void ResetTimedCallbackSequence()
    {
        _scene.TimedCallbacks.Clear();
        _scene.TimedCallbackCursor = 0;
        _scene.TimedCallbackStartedAtMs = 0;
        _scene.TimedCallbackAbortOffset = -1;
        _scene.TimedCallbackGuard = null;
    }

    private StepResult OpGetMonotonicTimeMs(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], unchecked((int)_waits.InputClockMilliseconds)); return StepResult.Next;
    }

    // 0xc8 (duration) — the waits sleep duration ms; headless stand-ins return at once (parity). Frame pacing.
    private StepResult OpSleep(Instruction ins, int pc)
    {
        var a = ins.Args;
        // sleep_op_0xc8@0x420ec0 arms nothing while ADV fast-forward (run-state 0x08000000) is active.
        if (AdvFastForwardActive) return StepResult.Next;
        _waits.Sleep(Read(a[0])); return StepResult.Next;
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
        if (count > MaxScriptRunLength - _scene.TimedCallbacks.Count)
        {
            HaltReason ??= $"timed-callback-count-too-large:{count}";
            return StepResult.Halt;
        }
        int primaryOffset = unchecked((int)Read(a[2]));
        int catchUpOffset = unchecked((int)Read(a[3]));
        long deadlineMs = _scene.TimedCallbacks.Count == 0 ? 0 : _scene.TimedCallbacks[^1].DeadlineMs;
        for (int i = 0; i < count; i++)
        {
            deadlineMs += intervalMs;
            _scene.TimedCallbacks.Add(new TimedCallback(deadlineMs, primaryOffset, catchUpOffset));
        }
        return StepResult.Next;
    }

    // 0xd5: dispatch each scheduled local callback, resuming here after ret
    private StepResult OpRunTimedCallbackSequence(Instruction ins, int pc)
    {
        var a = ins.Args;
        // op_0xd5_handler@0x421090 starts the sequence when its cursor is 0: it records the abort pc and the
        // running script as the guard (+0x5f6b8) and starts the clock.
        if (_scene.TimedCallbackCursor == 0)
        {
            TakeHeldTriggers();   // input_flush_action_triggers
            _scene.TimedCallbackStartedAtMs = _waits.InputClockMilliseconds;
            _scene.TimedCallbackAbortOffset = unchecked((int)Read(a[0]));
            _scene.TimedCallbackGuard = _cur.Script;
        }
        // Native op 0xd5 stops at last_index rather than count. The final entry is a
        // look-ahead sentinel: it supplies the next deadline for the preceding event but
        // is not itself dispatched.
        if (_scene.TimedCallbackCursor >= _scene.TimedCallbacks.Count - 1) return StepResult.Next;
        if (TryAbortTimedCallbackSequence(out int abortTarget)) return StepResult.Jump(abortTarget);

        var callback = _scene.TimedCallbacks[_scene.TimedCallbackCursor];
        long elapsedMs = _waits.InputClockMilliseconds - _scene.TimedCallbackStartedAtMs;
        if (elapsedMs < callback.DeadlineMs)
        {
            _waits.WaitForTimedCallbackDeadline(callback.DeadlineMs - elapsedMs);
            elapsedMs = _waits.InputClockMilliseconds - _scene.TimedCallbackStartedAtMs;
            if (TryAbortTimedCallbackSequence(out abortTarget)) return StepResult.Jump(abortTarget);
        }

        bool fellBehind = _scene.TimedCallbackCursor + 1 < _scene.TimedCallbacks.Count
            && _scene.TimedCallbacks[_scene.TimedCallbackCursor + 1].DeadlineMs < elapsedMs;
        int targetOffset = fellBehind ? callback.CatchUpOffset : callback.PrimaryOffset;
        // timed_callback_sequence_tick@0x408170: the schedule is engine-wide, so a frame that runs op 0xd5
        // while another script's sequence is under way dispatches that script's entry. When the guard is
        // another script, AGE raises Depth before the cursor moves; op 0xd5 recorded length 0, so 再試行 and
        // 無視 both run it again and the error repeats until 中止.
        if (targetOffset != -1 && _scene.TimedCallbackGuard is { } guard && !IsSameScript(guard, _cur.Script))
            return StepResult.Jump(RaiseInstructionScriptError(DepthError(guard), pc, ignoreSkipsInstruction: false));
        _scene.TimedCallbackCursor++;
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
        if (TakeHeldTriggers() == 0 || _scene.TimedCallbackAbortOffset == -1) return false;
        return _cur.Script.IndexByOffset.TryGetValue(_scene.TimedCallbackAbortOffset, out target);
    }
}

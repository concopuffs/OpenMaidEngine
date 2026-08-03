using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepTiming(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "get-monotonic-time-ms":
                Write(a[0], unchecked((int)_host.InputClockMilliseconds)); return pc + 1;
            case "sleep":   // 0xc8 (duration) — pause the host duration ms; headless hosts no-op (parity). Frame pacing.
                _host.Sleep(Read(a[0])); return pc + 1;
            case "u00425960":
            case "begin-timed-callback-sequence": // 0xd3: clear the frame-local relative schedule
                _cur.TimedCallbacks.Clear();
                _cur.TimedCallbackCursor = 0;
                _cur.TimedCallbackStartedAtMs = null;
                _cur.TimedCallbackAbortOffset = -1;
                return pc + 1;
            case "u004266F0":
            case "append-relative-timed-callbacks": // 0xd4: (interval ms, count, on-time PC, catch-up PC)
            {
                long intervalMs = Read(a[0]);
                int count = System.Math.Max(0, unchecked((int)Read(a[1])));
                int primaryOffset = unchecked((int)Read(a[2]));
                int catchUpOffset = unchecked((int)Read(a[3]));
                long deadlineMs = _cur.TimedCallbacks.Count == 0 ? 0 : _cur.TimedCallbacks[^1].DeadlineMs;
                for (int i = 0; i < count; i++)
                {
                    deadlineMs += intervalMs;
                    _cur.TimedCallbacks.Add(new ExecFrame.TimedCallback(
                        deadlineMs, primaryOffset, catchUpOffset));
                }
                return pc + 1;
            }
            case "u004262C0":
            case "run-timed-callback-sequence": // 0xd5: dispatch each scheduled local callback, resuming here after ret
            {
                if (_cur.TimedCallbackStartedAtMs == null)
                {
                    _cur.TimedCallbackStartedAtMs = _host.InputClockMilliseconds;
                    _cur.TimedCallbackAbortOffset = unchecked((int)Read(a[0]));
                }
                // Native op 0xd5 stops at last_index rather than count. The final entry is a
                // look-ahead sentinel: it supplies the next deadline for the preceding event but
                // is not itself dispatched.
                if (_cur.TimedCallbackCursor >= _cur.TimedCallbacks.Count - 1)
                {
                    _cur.TimedCallbackStartedAtMs = null;
                    return pc + 1;
                }

                var callback = _cur.TimedCallbacks[_cur.TimedCallbackCursor];
                long elapsedMs = _host.InputClockMilliseconds - _cur.TimedCallbackStartedAtMs.Value;
                if (elapsedMs < callback.DeadlineMs)
                {
                    _host.WaitForTimedCallbackDeadline(callback.DeadlineMs - elapsedMs);
                    elapsedMs = _host.InputClockMilliseconds - _cur.TimedCallbackStartedAtMs.Value;
                }

                bool fellBehind = _cur.TimedCallbackCursor + 1 < _cur.TimedCallbacks.Count
                    && _cur.TimedCallbacks[_cur.TimedCallbackCursor + 1].DeadlineMs < elapsedMs;
                int targetOffset = fellBehind ? callback.CatchUpOffset : callback.PrimaryOffset;
                _cur.TimedCallbackCursor++;
                if (targetOffset < 0 || !_cur.Script.IndexByOffset.TryGetValue(targetOffset, out int target))
                    return pc;
                _cur.CallStack.Add(pc);
                return target;
            }
            default:
                throw new InvalidOperationException($"Non-timing opcode routed to timing handler: {label}");
        }
    }
}

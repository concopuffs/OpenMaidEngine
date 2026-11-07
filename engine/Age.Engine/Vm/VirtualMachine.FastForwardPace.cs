namespace Age.Engine.Vm;

/// <summary>
/// Native ADV fast-forward pace. While run-state 0x08000000 is set, engine_main_tick_with_exception_policy@0x411840
/// dispatches exactly one op per main-loop iteration (adv_interpreter_tick) and then does fixed per-iteration work:
/// gfx_request_force_complete_and_reset_anim_clock, sfx_clear_scheduled_starts, a 1000-slot movie-surface service
/// pass, Sleep(0) and PeekMessage, plus gfx_render_frame when the frame is dirty. Fast-forward is therefore paced
/// by a per-op cost rather than by sleeps or presentation (op 0xc8 sleep is a no-op under fast-forward).
///
/// The port reproduces it with a wall-clock deadline: op n of a fast-forward run may not start before
/// start + n × <see cref="VmOptions.FastForwardOpMicroseconds"/>. Time the VM already spends blocked in host
/// services (synchronized 0x20c presentation, for example) counts toward the budget, up to
/// <see cref="FastForwardCreditCapMs"/>, so the total approaches native's ops × cost. Time and waits come from the
/// VM's <see cref="IPaceClock"/> (constructor <c>paceClock</c>; a Stopwatch and Thread.Sleep by default).
/// </summary>
public sealed partial class VirtualMachine
{
    private const long FastForwardCreditCapMs = 100;

    private readonly IPaceClock _paceClock;
    private bool _fastForwardPacing;
    private long _fastForwardOps;
    private double _fastForwardStartMs;

    private void PaceFastForwardOp()
    {
        if (_o.FastForwardOpMicroseconds <= 0) return;
        if (!AdvFastForwardActive)
        {
            _fastForwardPacing = false;
            return;
        }
        double now = _paceClock.NowMs;
        if (!_fastForwardPacing)
        {
            _fastForwardPacing = true;
            _fastForwardOps = 0;
            _fastForwardStartMs = now;
        }
        _fastForwardOps++;
        double target = _fastForwardStartMs + _fastForwardOps * (_o.FastForwardOpMicroseconds / 1000.0);
        double credit = now - target;
        if (credit > FastForwardCreditCapMs)
        {
            // A long blocking service (a modal movie, say) must not bank enough credit to run unpaced later.
            _fastForwardStartMs += credit - FastForwardCreditCapMs;
            return;
        }
        // Sleep in whole milliseconds once at least one is owed; the deadline carries the remainder.
        if (credit <= -1.0) _paceClock.Sleep((int)(-credit));
    }
}

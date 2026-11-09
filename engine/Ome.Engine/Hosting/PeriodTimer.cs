namespace Ome.Engine.Hosting;

/// <summary>Native AGE's polled period timer: one of the 32 0x1c-byte timers at <c>ctx+0x5f304</c>, armed by
/// <c>sleep_timer_arm@0x44cff0</c> and polled by <c>period_timer_poll_elapsed_periods@0x44d0f0</c> on
/// <c>timeGetTime</c> milliseconds. The ADV glyph reveal uses the one at <c>ctx+0x5f630</c>
/// (docs/engine-re.md, "The reveal clock").</summary>
/// <remarks>A poll succeeds once the time since the arm passes <c>index × period</c>, and then moves the index past
/// every boundary crossed, so a late poll drops the boundaries it missed instead of catching up. A constructed timer
/// has period 0 and never elapses.</remarks>
public sealed class PeriodTimer
{
    private long _startMs;
    private long _periodMs;
    private long _index = 1;

    public long PeriodMs => _periodMs;

    /// <summary>sleep_timer_arm: restart at <paramref name="nowMs"/> with the next boundary one period ahead. A zero
    /// period becomes 1 ms.</summary>
    public void Arm(long nowMs, int periodMs)
    {
        _index = 1;
        _startMs = nowMs;
        _periodMs = periodMs == 0 ? 1 : periodMs;
    }

    /// <summary>period_timer_poll_elapsed_periods: -1 before the next boundary; otherwise the boundaries crossed past
    /// it (0 when only that one), with the index moved past them.</summary>
    public int Poll(long nowMs)
    {
        if (_periodMs <= 0) return -1;
        long elapsed = nowMs - _startMs;
        long previous = _index;
        if (previous * _periodMs - elapsed >= 0) return -1;
        long crossed = elapsed / _periodMs;
        _index = crossed + 1;
        return (int)(crossed - previous);
    }

    /// <summary>The first millisecond at which <see cref="Poll"/> succeeds, or null for a timer that never
    /// elapses.</summary>
    public long? NextDueMs => _periodMs <= 0 ? null : _startMs + _index * _periodMs + 1;
}

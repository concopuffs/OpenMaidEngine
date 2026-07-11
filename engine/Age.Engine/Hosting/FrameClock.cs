namespace Age.Engine.Hosting;

/// <summary>Host-owned virtual clock. Godot advances it from real elapsed time; presentation-service waits,
/// sleeps, and retained graphics consume this timebase. Ordinary opcode bursts are not clock-throttled.
/// Fractional milliseconds are retained so diagnostic
/// slow motion does not stall on high-refresh displays.</summary>
public sealed class FrameClock
{
    private long _nowMs;
    private double _fractionalMs;

    /// <summary>Monotonic virtual time in milliseconds (scaled by Speed).</summary>
    public long NowMs => System.Threading.Interlocked.Read(ref _nowMs);

    /// <summary>Speed multiplier. 1.0 = normal. A lower diagnostic value slows sleeps and retained
    /// presentation clocks; ordinary opcode bursts still run to the next service boundary.</summary>
    public double Speed = 1.0;

    /// <summary>Legacy calibration retained for the isolated WallClockOpPacer tests. Production Godot no
    /// longer uses an opcode-rate limiter; native presentation tracing disproved this as scheduler state.</summary>
    public double OpsPerSecond = 200.0;

    /// <summary>Advance the clock by one rendered frame's real delta (seconds), scaled by Speed.</summary>
    public void Advance(double realDeltaSeconds)
    {
        double scaled = realDeltaSeconds * 1000.0 * Speed + _fractionalMs;
        long whole = (long)System.Math.Floor(scaled);
        _fractionalMs = scaled - whole;
        if (whole > 0) System.Threading.Interlocked.Add(ref _nowMs, whole);
    }
}

/// <summary>Legacy isolated rate-limiter utility. It is not used by production hosts; ordinary native opcode
/// execution is burst-fast between explicit presentation/sleep/input service boundaries.</summary>
public sealed class WallClockOpPacer
{
    private readonly FrameClock _clock;
    private bool _started;
    private long _epochMs;
    private long _completed;

    public WallClockOpPacer(FrameClock clock) => _clock = clock;

    public void OpcodeCompleted()
    {
        if (!_started)
        {
            _started = true;
            _epochMs = _clock.NowMs;
            _completed = 0;
        }
        _completed++;
    }

    /// <summary>Whether the next opcode may execute at the clock's current time.</summary>
    public bool CanRunNext
    {
        get
        {
            if (!_started) return true;
            long elapsed = System.Math.Max(0, _clock.NowMs - _epochMs);
            long allowance = 1 + (long)System.Math.Floor(elapsed * _clock.OpsPerSecond / 1000.0);
            return _completed < allowance;
        }
    }

    public void Reset()
    {
        _started = false;
        _epochMs = 0;
        _completed = 0;
    }
}

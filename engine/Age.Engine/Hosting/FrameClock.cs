namespace Age.Engine.Hosting;

/// <summary>Host-owned virtual clock. Godot advances it from real elapsed time; VM pacing, sleeps, and
/// retained graphics all consume this same timebase. Fractional milliseconds are retained so diagnostic
/// slow motion does not stall on high-refresh displays.</summary>
public sealed class FrameClock
{
    private long _nowMs;
    private double _fractionalMs;

    /// <summary>Monotonic virtual time in milliseconds (scaled by Speed).</summary>
    public long NowMs => System.Threading.Interlocked.Read(ref _nowMs);

    /// <summary>Speed multiplier. 1.0 = normal. A lower diagnostic value slows VM progress, sleeps, and
    /// graphics together; a future ADV-scoped Ctrl hook can drive the same seam.</summary>
    public double Speed = 1.0;

    /// <summary>Native normal-playback interpreter cadence. The old 1,800 figure counted calls to
    /// vm_operand_fetch, not completed opcodes. A live 1,890 ms transform section executes about 407
    /// port opcodes. A normal-speed replay at 215/s retained the object for 1,798 ms; 200/s reaches
    /// the native 1,890 ms endpoint before the same teardown path.</summary>
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

/// <summary>Pure wall-clock opcode rate limiter. The VM thread records completed opcodes and waits whenever
/// it has consumed the allowance earned from <see cref=FrameClock.NowMs/>. Reset after a blocking wait so
/// parked time never turns into a catch-up burst.</summary>
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

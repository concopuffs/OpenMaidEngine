using System.Diagnostics;

namespace Ome.Engine.Hosting;

/// <summary>Host-owned virtual clock. Presentation-service waits, sleeps, and retained graphics consume this
/// timebase; ordinary opcode bursts are not clock-throttled.
/// <para>A real-time clock (<see cref="StartRealTime"/>, the Godot host's) reads the high-resolution timer on
/// every <see cref="NowMs"/>, as native AGE's tick reads <c>timeGetTime</c>. A service wait can then end when its
/// effect does, not at the next rendered frame (docs/engine-re.md, "Service waits end on the engine clock").
/// <see cref="Advance"/> does nothing on it.</para>
/// <para>A frame-stepped clock (<c>new FrameClock()</c>, used by tests) moves only through <see cref="Advance"/>.
/// It keeps fractional milliseconds so diagnostic slow motion does not stall on high-refresh displays.</para>
/// </summary>
public sealed class FrameClock
{
    private readonly bool _realTime;
    private readonly object _speedLock = new();
    private long _nowMs;            // frame-stepped: the time; real-time: the latest value returned (monotonic floor)
    private double _fractionalMs;
    private double _speed = 1.0;
    private double _anchorMs;       // real-time: virtual time at _anchorTicks, rebased when Speed changes
    private long _anchorTicks;

    public FrameClock() { }

    private FrameClock(bool realTime)
    {
        _realTime = realTime;
        _anchorTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>A clock that runs on the high-resolution timer between frames, starting at 0.</summary>
    public static FrameClock StartRealTime() => new(realTime: true);

    public bool IsRealTime => _realTime;

    /// <summary>Monotonic virtual time in milliseconds (scaled by Speed).</summary>
    public long NowMs
    {
        get
        {
            if (!_realTime) return Interlocked.Read(ref _nowMs);
            long sample;
            lock (_speedLock) sample = (long)Math.Floor(VirtualMsAt(Stopwatch.GetTimestamp()));
            long previous;
            do
            {
                previous = Interlocked.Read(ref _nowMs);
                if (sample <= previous) return previous;
            } while (Interlocked.CompareExchange(ref _nowMs, sample, previous) != previous);
            return sample;
        }
    }

    /// <summary>Speed multiplier. 1.0 = normal. A lower diagnostic value slows sleeps and retained
    /// presentation clocks; ordinary opcode bursts still run to the next service boundary.</summary>
    public double Speed
    {
        get { lock (_speedLock) return _speed; }
        set
        {
            lock (_speedLock)
            {
                if (_realTime)
                {
                    long ticks = Stopwatch.GetTimestamp();
                    _anchorMs = VirtualMsAt(ticks);
                    _anchorTicks = ticks;
                }
                _speed = value;
            }
        }
    }

    /// <summary>Legacy calibration retained for the isolated WallClockOpPacer tests. Production Godot no
    /// longer uses an opcode-rate limiter; native presentation tracing disproved this as scheduler state.</summary>
    public double OpsPerSecond = 200.0;

    /// <summary>Real milliseconds until the clock reaches <paramref name="deadlineMs"/>, rounded up and clamped to
    /// [0, <paramref name="maximum"/>]. A frame-stepped clock moves only with frames, so it answers
    /// <paramref name="maximum"/> while the deadline is ahead.</summary>
    public int RealMillisecondsUntil(long deadlineMs, int maximum)
    {
        long remaining = deadlineMs - NowMs;
        if (remaining <= 0) return 0;
        if (!_realTime) return maximum;
        double real = remaining / Math.Max(0.05, Speed);
        return (int)Math.Clamp(Math.Ceiling(real), 1, maximum);
    }

    /// <summary>Advance a frame-stepped clock by one rendered frame's real delta (seconds), scaled by Speed.
    /// A real-time clock ignores it.</summary>
    public void Advance(double realDeltaSeconds)
    {
        if (_realTime) return;
        double scaled = realDeltaSeconds * 1000.0 * Speed + _fractionalMs;
        long whole = (long)Math.Floor(scaled);
        _fractionalMs = scaled - whole;
        if (whole > 0) Interlocked.Add(ref _nowMs, whole);
    }

    private double VirtualMsAt(long ticks) => _anchorMs + (ticks - _anchorTicks) * 1000.0 / Stopwatch.Frequency * _speed;
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

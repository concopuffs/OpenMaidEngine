namespace Ome.Engine.Devices;

/// <summary>
/// The time source and wait that ADV fast-forward pacing uses (<c>Ome.Engine.Natives.SkipAndAuto</c>). The system
/// clock reads a Stopwatch and blocks the calling thread with Thread.Sleep. A test passes a clock that moves only when
/// pacing sleeps (or when it chooses), so it can check the pacing schedule exactly instead of measuring wall time.
/// </summary>
public interface IPaceClock
{
    /// <summary>Monotonic milliseconds since an arbitrary origin.</summary>
    double NowMs { get; }

    /// <summary>Block the calling thread for <paramref name="milliseconds"/> (Thread.Sleep: at least that long).</summary>
    void Sleep(int milliseconds);
}

using System.Diagnostics;

namespace Age.Engine.Vm;

/// <summary>
/// The time source and wait that ADV fast-forward pacing uses (VirtualMachine.FastForwardPace.cs). The default reads
/// a Stopwatch and blocks the VM thread with Thread.Sleep. A test passes a clock that moves only when pacing sleeps
/// (or when it chooses), so it can check the pacing schedule exactly instead of measuring wall time.
/// </summary>
public interface IPaceClock
{
    /// <summary>Monotonic milliseconds since an arbitrary origin.</summary>
    double NowMs { get; }

    /// <summary>Block the VM thread for <paramref name="milliseconds"/> (Thread.Sleep: at least that long).</summary>
    void Sleep(int milliseconds);
}

/// <summary>The real pace clock: a Stopwatch started when the VM is built, and Thread.Sleep.</summary>
public sealed class StopwatchPaceClock : IPaceClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public double NowMs => _watch.Elapsed.TotalMilliseconds;

    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
}

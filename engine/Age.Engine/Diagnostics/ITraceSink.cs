namespace Age.Engine.Diagnostics;

/// <summary>The engine's diagnostics seam. The VM emits typed <see cref="TraceEvent"/>s here; any
/// consumer (CLI, Godot, tests) supplies a sink instead of reimplementing IHost. Observe-only:
/// a sink never reads/writes VM state or influences control flow (that guarantees trace parity).</summary>
public interface ITraceSink
{
    /// <summary>Cheap gate: when false the VM skips constructing per-instruction Step events, keeping
    /// the hot path (a corpus sweep is ~1.46M instructions) free. Rare events emit regardless.</summary>
    bool TracingSteps { get; }
    void Emit(in TraceEvent e);
}

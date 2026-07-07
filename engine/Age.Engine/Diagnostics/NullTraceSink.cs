namespace Age.Engine.Diagnostics;

/// <summary>The inert default: no step tracing, empty Emit. Supplying this (or null) to the VM
/// guarantees byte-identical behavior.</summary>
public sealed class NullTraceSink : ITraceSink
{
    public static readonly NullTraceSink Instance = new();
    private NullTraceSink() { }
    public bool TracingSteps => false;
    public void Emit(in TraceEvent e) { }
}

namespace Age.Engine.Diagnostics;

/// <summary>Fans one event stream out to several sinks, so e.g. a live text trace and an aggregating
/// histogram (or Godot's call-script queue) can run together. TracingSteps is true if ANY child needs
/// steps, so the VM's cheap gate stays correct.</summary>
public sealed class CompositeTraceSink : ITraceSink
{
    private readonly ITraceSink[] _sinks;
    public CompositeTraceSink(params ITraceSink[] sinks) => _sinks = sinks;

    public bool TracingSteps
    {
        get { foreach (var s in _sinks) if (s.TracingSteps) return true; return false; }
    }

    public void Emit(in TraceEvent e) { foreach (var s in _sinks) s.Emit(in e); }
}

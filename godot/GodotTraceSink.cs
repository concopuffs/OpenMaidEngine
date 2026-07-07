using System.Collections.Concurrent;
using Age.Engine.Diagnostics;

// Frontend-side trace consumer. Runs on the VM background thread, so it just queues the dispatched
// call-script ids; the main thread drains them (Godot drops GD.Print from background threads). This
// replaces the old IHost.CallScript -> GodotAdvHost.Dispatched hack: subroutine visibility is now an
// engine fact delivered over the trace seam.
public sealed class GodotTraceSink : ITraceSink
{
    public bool TracingSteps => false;
    public readonly ConcurrentQueue<long> CallScripts = new();
    public void Emit(in TraceEvent e)
    {
        if (e.Kind == TraceEventKind.CallScript) CallScripts.Enqueue(e.Id);
    }
}

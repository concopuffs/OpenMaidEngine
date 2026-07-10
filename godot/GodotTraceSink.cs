using System.Collections.Concurrent;
using System.Collections.Generic;
using Age.Engine.Diagnostics;

// Frontend-side trace consumer. Runs on the VM background thread, so it just queues the dispatched
// call-script ids; the main thread drains them (Godot drops GD.Print from background threads). This
// replaces the old IHost.CallScript -> GodotAdvHost.Dispatched hack: subroutine visibility is now an
// engine fact delivered over the trace seam.
public sealed class GodotTraceSink : ITraceSink
{
    private readonly GodotTimelineLog? _timeline;
    private readonly Stack<string> _scripts = new();
    public GodotTraceSink(GodotTimelineLog? timeline = null) => _timeline = timeline;
    public bool TracingSteps => _timeline != null;
    public readonly ConcurrentQueue<long> CallScripts = new();
    public void Emit(in TraceEvent e)
    {
        if (e.Kind == TraceEventKind.CallScript) CallScripts.Enqueue(e.Id);
        if (_timeline == null) return;
        if (e.Kind == TraceEventKind.FrameEnter && e.Name != null) _scripts.Push(e.Name);
        else if (e.Kind == TraceEventKind.FrameExit && _scripts.Count > 0) _scripts.Pop();
        else if (e.Kind == TraceEventKind.Step && e.Ins != null)
            _timeline.Step(_scripts.Count > 0 ? _scripts.Peek() : "<unknown>", e.Ins.Offset, e.Opcode, e.Depth);
        else if (e.Kind == TraceEventKind.Halt)
            _timeline.State("halted", new() { ["reason"] = e.Text, ["steps"] = e.Steps });
    }
}

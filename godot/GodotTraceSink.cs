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
    private readonly PageLocatorState _locator;
    private readonly Stack<string> _scripts = new();
    public GodotTraceSink(PageLocatorState locator, GodotTimelineLog? timeline = null)
    { _locator = locator; _timeline = timeline; }
    // The page locator needs the exact script/offset even when the heavier timeline log is disabled.
    public bool TracingSteps => true;
    public readonly ConcurrentQueue<long> CallScripts = new();
    public void Emit(in TraceEvent e)
    {
        if (e.Kind == TraceEventKind.CallScript) CallScripts.Enqueue(e.Id);
        if (e.Kind == TraceEventKind.FrameEnter && e.Name != null)
        {
            _scripts.Push(e.Name);
            PublishCallStack();
        }
        else if (e.Kind == TraceEventKind.FrameExit && _scripts.Count > 0)
        {
            _scripts.Pop();
            PublishCallStack();
        }
        else if (e.Kind == TraceEventKind.Step && e.Ins != null)
        {
            string script = _scripts.Count > 0 ? _scripts.Peek() : "<unknown>";
            _locator.Step(script, e.Ins.Offset);
            _timeline?.Step(script, e.Ins.Offset, e.Opcode, e.Depth);
        }
        else if (e.Kind == TraceEventKind.Halt)
            _timeline?.State("halted", new() { ["reason"] = e.Text, ["steps"] = e.Steps });
    }

    private void PublishCallStack()
    {
        var stack = _scripts.ToArray();
        System.Array.Reverse(stack);
        _locator.CallStack(stack);
    }
}

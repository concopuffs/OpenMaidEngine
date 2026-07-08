using System.Collections.Generic;
namespace Age.Engine.Diagnostics;

/// <summary>Base for sinks that need to know the currently-executing script. Tracks the frame stack
/// from FrameEnter/FrameExit (which emit regardless of the TracingSteps gate) so a subclass can key
/// per-instruction facts by the REAL running script — including inside nested call-script frames.
/// This is the "which script is offset 0x1c3 actually in?" fix: a bare Step event only carries pc +
/// depth, not the script name.</summary>
public abstract class TraceSinkBase : ITraceSink
{
    private readonly Stack<string> _frames = new();

    /// <summary>Name of the innermost frame currently executing ("?" before the first FrameEnter).</summary>
    protected string CurrentScript => _frames.Count > 0 ? _frames.Peek() : "?";

    public abstract bool TracingSteps { get; }

    public void Emit(in TraceEvent e)
    {
        // Push before dispatch so the FrameEnter itself is attributed to the entered frame; pop after
        // dispatch so the FrameExit is still attributed to the exiting frame.
        if (e.Kind == TraceEventKind.FrameEnter) _frames.Push(e.Name ?? "?");
        OnEvent(e);
        if (e.Kind == TraceEventKind.FrameExit && _frames.Count > 0) _frames.Pop();
    }

    protected abstract void OnEvent(in TraceEvent e);
}

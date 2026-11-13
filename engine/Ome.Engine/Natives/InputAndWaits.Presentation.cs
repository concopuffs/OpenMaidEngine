using Ome.Engine.Hosting;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

// The presentation waits: a burst's state reaches the screen only at a presentation, sleep, input or effect wait, as
// native publishes retained mutations only at its present and service boundaries. A frame turn presents only where the
// burst is suspended (or before any script runs): the interpreter's script frames count a burst, and each wait that
// lets the screen see its state suspends it while it is parked. Everything here is the engine thread's.
public sealed partial class InputAndWaits
{
    private int _scriptDepth;          // the interpreter's script frames
    private bool _burstSuspended;      // a wait has suspended the burst: a turn may present
    private readonly bool _synchronizeExplicitPresentation;
    private long _explicitPresentationRequestGeneration;
    private long _consumedPresentationRequestGeneration;
    private long _lastSynchronizedPresentationTimestamp;
    private static readonly long FastForwardPresentationInterval = System.Diagnostics.Stopwatch.Frequency / 20;
    private bool _presentationWithheldByStatementBlock;   // brackets the timeline's withheld stretches

    /// <summary>A frame turn may present: no script runs, or a wait has suspended its burst.</summary>
    private bool Presentable => _scriptDepth == 0 || _burstSuspended;

    /// <summary>The interpreter enters a script's frame: a burst is running, and --timeline-log records it. The VM's
    /// frames are the script stack; the waits keep no copy (engine layering plan §3.3).</summary>
    public void EnterScriptContext(string scriptName)
    {
        _scriptDepth++;
        _timeline?.Event("script-context-enter", new() { ["scene"] = SceneOf(scriptName) });
    }

    /// <summary>The interpreter leaves a script's frame.</summary>
    public void ExitScriptContext(string scriptName)
    {
        _timeline?.Event("script-context-exit", new() { ["scene"] = SceneOf(scriptName) });
        if (_scriptDepth <= 0) throw new System.InvalidOperationException("No script burst is active.");
        _scriptDepth--;
    }

    private static string SceneOf(string scriptName)
        => System.IO.Path.GetFileNameWithoutExtension(scriptName).ToUpperInvariant();

    // A wait parks: the screen may see the burst's state until it resumes. False when there is no burst to suspend.
    private bool SuspendScriptForPresentation()
    {
        if (_scriptDepth <= 0 || _burstSuspended) return false;
        _burstSuspended = true;
        return true;
    }

    private void ResumeScriptAfterPresentation(bool suspended)
    {
        if (suspended) _burstSuspended = false;
    }

    private long RequestPresentation()
    {
        long requested = ++_explicitPresentationRequestGeneration;
        _graphics.RequestPresentation();
        return requested;
    }

    private void RequestSynchronizedPresentation()
    {
        long requested = RequestPresentation();
        if (!_synchronizeExplicitPresentation) return;
        // Native presents without waiting for the display, and under ADV fast-forward it passes a text page in about
        // 2.4 ms. The port presents in the front end's frame turn, once a display frame, so waiting at every page and
        // op 0x20c would cost a frame each. Fast-forward waits for a frame at most every 50 ms; the boundaries between
        // publish at the next turn (docs/engine-re.md, "Fast-forward pace").
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_skip.FastForwardActive && now - _lastSynchronizedPresentationTimestamp < FastForwardPresentationInterval)
            return;
        while (_consumedPresentationRequestGeneration < requested && !Stopping)
            WaitAndTick(_frameSignal, 50);
        _lastSynchronizedPresentationTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>Op 0x222 (the backbuffer): native ends its D3D scene and calls Present before returning, so the exact
    /// retained state is shown before the burst can change or release the capture surface.</summary>
    public void PresentObjectRange(long firstHandle, long count)
    {
        if (!_graphics.PresentObjectRange(firstHandle, count)) return;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
    }

    /// <summary>Op 0x20c: present the frame now.</summary>
    public void PresentFrame()
    {
        if (!_graphics.PresentFrame()) return;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
    }

    /// <summary>
    /// In a frame turn: why this frame must be drawn again, if at all. Native retained-object writes are not
    /// front-buffer writes, so explicit and service-boundary dirtiness publishes once, and the frame continues only while
    /// the sampled retained scene can change. The wait indicator advances its retained source cell here, so it counts
    /// only when the frame changes.
    /// </summary>
    private HostPresentationReason ConsumePresentationReasons()
    {
        GfxState gfx = _graphics.State;
        _text.UpdateWaitIndicators(gfx);
        bool screenTransitionActive = _graphics.ScreenTransitionPending;
        // Native engine_main_tick skips its own gfx_render_frame while a statement block (op 0x1f4) is open,
        // unless run-state 0x400 parks the interpreter on a transition. Explicit present-frame (op 0x20c)
        // still renders. Scripts that keep a block open across a wait (ADDEXP, MES) call present-frame
        // themselves. Leave the retained-state reasons pending so the next explicit present publishes them.
        bool withheld = _tick.StatementBlockOpen
            && !_graphics.PresentationRequested
            && !screenTransitionActive && !_transitionWaiting;
        if (withheld != _presentationWithheldByStatementBlock)
        {
            _presentationWithheldByStatementBlock = withheld;
            _timeline?.Event("statement-block-presentation", new() { ["withheld"] = withheld });
        }
        if (withheld) return HostPresentationReason.None;
        var reasons = HostPresentationReason.None;
        if (_graphics.TakePresentationRequest())
        {
            reasons |= HostPresentationReason.HostRequest;
            _consumedPresentationRequestGeneration = _explicitPresentationRequestGeneration;
        }
        if (screenTransitionActive) reasons |= HostPresentationReason.ScreenTransition;
        GfxPresentationReason gfxReasons = gfx.ConsumePresentationReasons(_clock.NowMs);
        if ((gfxReasons & GfxPresentationReason.RetainedMutation) != 0)
            reasons |= HostPresentationReason.RetainedMutation;
        if ((gfxReasons & GfxPresentationReason.ContinuousChannel) != 0)
            reasons |= HostPresentationReason.ContinuousChannel;
        if ((gfxReasons & GfxPresentationReason.DiscreteSourceCell) != 0)
            reasons |= HostPresentationReason.DiscreteSourceCell;
        return reasons;
    }
}

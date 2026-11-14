using System.Threading;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

// The effect waits (run-state 0x400): op 0x21c parks until the queued 0x223 transitions and the movies that hold the
// service finish, and the legacy screen transitions (ops 0x21, 0x22, 0x24, 0x25) park while the engine composites an
// endpoint. Their state is the graphics native's (Natives/Graphics.Transitions.cs); what parks is here.
public sealed partial class InputAndWaits
{
    private bool _foregroundTransitionWaitBypassed;

    /// <summary>When the effect wait in progress started on the engine clock, or -1 (the debug auto-clicker reads
    /// it).</summary>
    public long TransitionStartedAtMs => Interlocked.Read(ref _scene.TransitionStartedAtMs);

    /// <summary>A triggered action while an effect service parks the VM (<see cref="IsTransitionWaiting"/>). With
    /// system:EffectSkipOnClick 1 the service polls native's input latch every tick
    /// (engine_main_tick_with_exception_policy@0x411840). The poll takes every trigger, and action 4 forces the
    /// effect's endpoint. A run-state-0x400 effect does not poll while service-flags bit 0 is set. False when the
    /// service does not poll: the trigger stays in the latch (VirtualMachine.HoldTriggers) for the next service that
    /// does.</summary>
    public bool TryPollEffectTriggers(int actionMask, bool effectSkipOnClick)
    {
        if (!effectSkipOnClick || !_scene.TransitionWaiting) return false;
        bool polls;
        if (_scene.ForegroundGfx is { } foreground) polls = (foreground.AnimationServiceFlags & 1) == 0;
        else polls = _graphics.ScreenTransitionPending;
        if (!polls) return false;
        _timeline?.State("effect-input-polled", new() { ["actions"] = actionMask });
        if ((actionMask & (1 << InputBindings.AdvanceAction)) != 0) TryForceActiveTransition("advance");
        return true;
    }

    private bool TryForceActiveTransition(string source)
    {
        if (_scene.TransitionWaiting && _scene.ForegroundGfx != null)
        {
            bool accepted = _scene.ForegroundGfx.TryCompleteClickSkippableTimedPresentation(
                _clock.NowMs, out int completed);
            if (accepted)
            {
                _foregroundTransitionWaitBypassed = true;
                _timeline?.State("transition-skip-accepted", new()
                {
                    ["endpoints_completed"] = completed,
                    ["source"] = source,
                    ["wait_bypassed"] = true,
                });
                _frameSignal.Set();
                return true;
            }
        }
        if (_scene.TransitionWaiting && _graphics.ForceScreenTransition())
        {
            _timeline?.State("screen-transition-forced-complete", new()
            {
                ["source"] = source,
            });
            _frameSignal.Set();
            return true;
        }
        return false;
    }

    /// <summary>Op 0x21c: normal playback parks until a queued 0x223 transition completes. The read/message-skip branch
    /// reaches op 0x20c instead and presents the completed endpoint at once.</summary>
    public void WaitForForegroundTransition(GfxState gfx)
    {
        _foregroundTransitionWaitBypassed = false;
        long startedAtMs = _clock.NowMs;
        int started = gfx.StartForegroundTransitions(startedAtMs);
        gfx.SeedOneShotChannels(startedAtMs);   // native renders on the service's first tick
        long serviceFlags = gfx.AnimationServiceFlags;
        long animClockMs = gfx.AnimClockDurationTicks;
        bool hasActivePresentation =
            gfx.PollEffectService(startedAtMs) | _movies.HoldsServiceWait;
        if (hasActivePresentation)
        {
            _scene.ForegroundGfx = gfx;
            Interlocked.Exchange(ref _scene.TransitionStartedAtMs, _clock.NowMs);
            _scene.TransitionWaiting = true;
            _timeline?.State("transition-start", new()
            {
                ["count"] = started, ["service_flags"] = serviceFlags, ["anim_clock_ms"] = animClockMs,
            });
            // Under ADV fast-forward native's main tick requests a forced render after every op it dispatches,
            // 0x21c included, so this wait starts forced. Service-flags bit 1 suppresses the request; bit 0 does
            // not (it rejects only the EffectSkipOnClick click). Whatever the force leaves running (a movie, a
            // detached object) still holds the wait.
            if (_skip.FastForwardActive)
            {
                int completed = gfx.ForceCompleteTimedPresentationForFastForward(startedAtMs);
                _timeline?.State("transition-fast-forward-forced", new() { ["endpoints_completed"] = completed });
            }
        }
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
            if (!hasActivePresentation) return;
            int lastBucket = -1;
            while (!_foregroundTransitionWaitBypassed
                   && (gfx.PollEffectService(_clock.NowMs) | _movies.HoldsServiceWait)
                   && !Stopping)
            {
                // Native's run-state-0x400 service polls every tick (Sleep(0)), so the wait ends within a
                // millisecond of its effect. On the real-time clock this wait polls as often; a frame-stepped
                // clock moves only with frames.
                WaitAndTick(_frameSignal, _clock.IsRealTime ? 1 : 50);
                if (_timeline == null) continue;
                var active = gfx.SnapshotForegroundTransitions(_clock.NowMs);
                var directional = gfx.SnapshotDirectionalBlurRangeTransitions(_clock.NowMs);
                var radial = gfx.SnapshotRadialBlurRangeTransitions(_clock.NowMs);
                double progress = active.Count != 0 ? active[0].Progress
                    : directional.Count != 0 ? directional[0].Progress
                    : radial.Count != 0 ? radial[0].Progress : 1.0;
                bool forced = active.Count != 0 ? active[0].Forced
                    : directional.Count != 0 ? directional[0].Forced
                    : radial.Count != 0 && radial[0].Forced;
                int bucket = (int)System.Math.Floor(progress * 10);
                if (bucket != lastBucket)
                {
                    lastBucket = bucket;
                    _timeline.State("transition-progress", new()
                    {
                        ["progress"] = progress,
                        ["forced"] = forced,
                    });
                }
            }
            // Native renders the terminal sample in the tick that ends the wait and runs the script on in that
            // tick. A render commits every channel it samples past its end, so sample once here on the engine thread:
            // the next burst starts from the committed endpoints (or the forced ones), and the next compositor
            // frame presents them or that burst's result. Waiting for that frame would hold the VM for a frame
            // and its render (docs/engine-re.md, "Service waits end on the engine clock").
            _graphics.CommitTerminalSample();
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _scene.TransitionWaiting = false;
        _foregroundTransitionWaitBypassed = false;
        Interlocked.Exchange(ref _scene.TransitionStartedAtMs, -1);
        _scene.ForegroundGfx = null;
        _timeline?.State("running", new() { ["transition_complete"] = true });
    }

    /// <summary>Ops 0x21, 0x22, 0x24 and 0x25 (the legacy screen transitions): scripts render whole frames into numbered
    /// surfaces, then block while the engine alpha-composites an endpoint. The engine thread blocks while the screen
    /// transition runs, then until the compositor has drawn its endpoint.</summary>
    public void WaitForScreenTransition(ScreenTransitionStart transition)
    {
        long start = transition.StartMs;
        long duration = transition.DurationMs;
        _scene.ForegroundGfx = null;
        Interlocked.Exchange(ref _scene.TransitionStartedAtMs, start);
        _scene.TransitionWaiting = true;
        _timeline?.State("screen-transition-start", transition.Detail);
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
            while (!Stopping && !_graphics.ScreenTransitionDue(_clock.NowMs))
                WaitAndTick(_frameSignal, _clock.RealMillisecondsUntil(start + duration, 50));
            // Publish the exact target endpoint before the following surface releases/root reload.
            _graphics.ForceScreenTransition();
            RequestSynchronizedPresentation();
            // Do not let the VM release both source surfaces (or immediately reload SYSTEM4) until the
            // main thread has actually published the terminal target frame.
            while (!Stopping && _graphics.ScreenTransitionPending)
                WaitAndTick(_frameSignal, 50);
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _scene.TransitionWaiting = false;
        Interlocked.Exchange(ref _scene.TransitionStartedAtMs, -1);
        _timeline?.State("running", new() { ["screen_transition_complete"] = true });
    }
}

using System;
using System.Threading;
using Ome.Engine.Hosting;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

// The text waits: op 0x72's page wait and op 0xfa's poll (run-state 0x80000000), the reveal (0x20000000) and the
// hidden-window wait (0x4000000). The interpreter's callbacks run inside a wait: each time the wait wakes it hands the
// interpreter its turn (serviceInputCallback, engine layering spec §5.4).
public sealed partial class InputAndWaits
{
    /// <summary>
    /// Op 0x72's text wait for the page (the interpreter has begun its wait on the page's layout), or op 0xfa's poll
    /// with no page (<paramref name="textPage"/> false: no page break or locator page, no wait indicator, the text
    /// stays). It runs the interpreter's callbacks (<paramref name="serviceInputCallback"/>, true while another is ready) until a click,
    /// fast-forward or Auto ends it. Callback completion itself is not native graphics dirtiness: a callback's retained
    /// writes publish through GfxState's mutation generation and its surface writes through the graphics native, so
    /// FIELD's 50 ms hover callback does not redraw an idle frame.
    /// </summary>
    public void WaitAtAdvInputPoll(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState,
                                   bool textPage)
    {
        if (textPage)
        {
            int page = _pages + 1;
            _locator.WaitReached(page);
            _pages = page;
            PostPageNotice();
        }
        _scene.Waiting = true;
        _timeline?.State(textPage ? "input-wait" : "input-poll", new() { ["page"] = _pages });
        var autoTimer = new AdvAutoAdvanceTimer(unvoicedDeadlineDue: !textPage);
        bool autoAdvanced = false;
        bool messageSkipped = false;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            // Publish the completed pre-wait burst once. Callback scripts temporarily reacquire the
            // write side below, so each hover/click update is likewise exposed only after it returns.
            RequestSynchronizedPresentation();
            while (!Stopping)
            {
                // A native modal loop (an open menu or dialog) stops the engine tick, so the wait neither services
                // input nor lets Auto time out until it closes.
                if (!_frontendModalRelease.IsSet)
                {
                    WaitModal(_frontendModalRelease.WaitHandle, 50);
                    continue;
                }
                while (true)
                {
                    bool markerWasEnabled = _text.WaitIndicatorEnabled;
                    if (scriptSuspended) ResumeScriptAfterPresentation(true);
                    bool keepServicing;
                    try
                    {
                        keepServicing = serviceInputCallback();
                    }
                    finally
                    {
                        if (scriptSuspended && !SuspendScriptForPresentation())
                            throw new InvalidOperationException("Input callback did not retain the script burst.");
                    }
                    // The native callback returns through the shared ADV redraw/wait path, whose op 0x72
                    // re-arms a marker stopped by nested HISTORY. Our blocking wait keeps the parent wait
                    // parked, so restore that parent-owned state at the equivalent callback boundary.
                    _text.RestoreWaitIndicator(markerWasEnabled);
                    if (!keepServicing) break;
                }
                if (_skip.FastForwardActive)
                {
                    messageSkipped = true;
                    break;
                }
                if (autoTimer.Poll(autoWaitState(), _audio.IsVoicePlaying, _clock.NowMs))
                {
                    autoAdvanced = true;
                    break;
                }
                if (_gate.Wait(0)) break;
                WaitAnyAndTick(new[] { _gate.AvailableWaitHandle, _inputCallbackSignal, _frameSignal });
                if (_gate.Wait(0)) break;
            }
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
            // Also when a callback ends the run with a native script error (the exception leaves this wait).
            _scene.Waiting = false;
        }
        _graphics.RequestPresentation();
        _timeline?.State("running", new()
        {
            ["input"] = messageSkipped ? "message-skip" : autoAdvanced ? "auto" : "user",
        });
        if (textPage) PostPageNotice();
    }

    /// <summary>
    /// The engine thread's advance (a routed click or advance key, or <see cref="SignalInput"/>): it cancels the modal
    /// movie, finishes the reveal, forces a transition that a click may skip, or ends the text wait, in that order. A
    /// transition click is consumed by the foreground lifecycle; it never pre-arms or advances the following stable
    /// input wait.
    /// </summary>
    private void Advance()
    {
        if (_scene.ModalMovieWaiting)
        {
            _scene.ModalMovieCancelled = true;
            _timeline?.State("modal-movie-cancel", new());
            _frameSignal.Set();
            return;
        }
        if (_text.FinishRevealOnClick())
        {
            // The text native sets ctx+0x55108, so the page's later reveals publish at once too.
            _frameSignal.Set();
            return;
        }
        if (TryForceActiveTransition("advance")) return;
        if (_scene.Waiting && _gate.CurrentCount == 0) _gate.Release();
    }

    /// <summary>Input arrived that a wait's callbacks may take: wake the wait so it hands the interpreter its
    /// turn.</summary>
    public void WakeInputCallbackService() => _inputCallbackSignal.Set();

    /// <summary>A service call ended the polling text wait (native run-state 0x80000000 cleared): the wait returns as a
    /// click would make it. Called on the engine thread from the wait's input callback.</summary>
    public void EndInputWait()
    {
        if (_scene.Waiting && _gate.CurrentCount == 0) _gate.Release();
    }

    /// <summary>
    /// Native run-state 0x4000000, the hidden-window wait (engine_main_tick_with_exception_policy@0x411ec8): the page
    /// stays hidden and frames keep presenting while the engine thread waits for any triggered action. The front end
    /// latches triggers into the VM, which wakes this wait; <paramref name="triggered"/> consumes the trigger.
    /// </summary>
    public void WaitForServiceTrigger(Func<bool> triggered)
    {
        _timeline?.State("hidden-window-wait", new());
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
            while (!Stopping && !triggered())
                WaitAnyAndTick(new WaitHandle[] { _inputCallbackSignal, _frameSignal }, 50);
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _timeline?.State("running", new() { ["hidden_window_triggered"] = true });
    }

    /// <summary>
    /// The reveal wait (run-state 0x20000000): the engine thread services the text native's reveal at each engine tick
    /// until its last glyph is out, a click or fast-forward finishes it, or the engine stops. Presentation is requested,
    /// not awaited: a frame wait would make the poll late and drop boundaries.
    /// </summary>
    public void WaitForTextReveal(GfxState gfx)
    {
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            _graphics.RequestPresentation();
            while (!Stopping && _text.ServiceReveal(gfx) is int waitMs)
                WaitAndTick(_frameSignal, waitMs);
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _timeline?.State("running", new() { ["text_reveal_complete"] = true });
    }
}

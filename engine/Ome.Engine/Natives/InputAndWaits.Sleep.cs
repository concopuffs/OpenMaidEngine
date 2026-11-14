using System.Threading;

namespace Ome.Engine.Natives;

// The timed waits: op 0xc8's sleep (run-state 1) and op 0xd5's wait for the next timed callback (0x40), and the hold
// at each opcode boundary while the front end runs a modal loop.
public sealed partial class InputAndWaits
{
    // Native runs AGERC's 終了 dialog as a modal Win32 dialog inside the window procedure, and a menu's modal loop the
    // same way, so the engine main loop dispatches no opcodes while either is open. The front end holds the VM at its
    // next opcode boundary. Holds nest (a menu command opens the 終了 dialog before the menu loop ends), so the VM runs
    // again only when the last one is released. The count is the front end's thread's; the event hands the hold over.
    private readonly ManualResetEventSlim _frontendModalRelease = new(true);
    private int _frontendModalHolds;

    /// <summary>--sleep-scale: a debug multiplier for explicit op-0xc8 holds only.</summary>
    public double SleepScale { get; set; } = 1.0;

    /// <summary>The front end opens (<paramref name="held"/> true) or closes a modal loop: the options panel, the 終了
    /// dialog, an open menu. Front end's thread only.</summary>
    public void SetFrontendModalHold(bool held)
    {
        if (held)
        {
            if (_frontendModalHolds++ == 0) _frontendModalRelease.Reset();
            return;
        }
        if (_frontendModalHolds == 0) return;
        if (--_frontendModalHolds == 0) _frontendModalRelease.Set();
    }

    /// <summary>Each opcode boundary: answer the front end's frame turn and pump its messages
    /// (InputAndWaits.Turns.cs), and block, running no tick, while a front-end modal loop is open.</summary>
    public void FrameYield()
    {
        if (!ServeFrameTurn()) PumpMessages();
        if (_frontendModalRelease.IsSet) return;
        while (!Stopping && !WaitModal(_frontendModalRelease.WaitHandle, 50)) { }
    }

    /// <summary>Wait on the engine clock (not Thread.Sleep), so the driver's speed scales sleeps together with the
    /// retained presentation clocks. The front end advances the clock and pulses each frame.</summary>
    public static long NormalizeSleepMilliseconds(long duration, double scale)
        => (long)System.Math.Clamp(duration * scale, 1, 60_000);

    /// <summary>sleep_op_0xc8@0x420ec0 calls kernel32 Sleep for an operand under 10 ms and returns; only a longer sleep
    /// arms the main-loop timer. Either way native renders a dirty frame at the next loop iteration without waiting for
    /// the display.</summary>
    public const long NativeInlineSleepLimitMs = 10;

    /// <summary>What a sleep asks of the compositor.</summary>
    public enum SleepPresentation { None, Immediate, Synchronized }

    /// <summary>What a sleep asks of the compositor. The sleep sets no run-state 0x400, so inside an open statement
    /// block engine_main_tick renders nothing while it runs: Himegari's control-strip callbacks sleep 100 ms inside a
    /// block and native shows their pressed icon only at the close (docs/engine-re.md, "Statement blocks").</summary>
    public static SleepPresentation SleepPresentationFor(long duration, bool statementBlockActive)
        => statementBlockActive ? SleepPresentation.None
           : duration < NativeInlineSleepLimitMs ? SleepPresentation.Immediate
           : SleepPresentation.Synchronized;

    /// <summary>Op 0xc8: block the engine thread for <paramref name="duration"/> milliseconds on the engine clock while
    /// the compositor keeps presenting the retained state. The native op arms a timer its main loop polls; blocking the
    /// engine thread is the same given the port's threads (docs/engine-re.md, the sleep section).</summary>
    public void Sleep(long duration)
    {
        // Native sleep_timer_arm clamps the duration to at least 1 ms. In menu poll loops, sleep(0)
        // therefore yields to the next engine tick instead of becoming a free-running no-op.
        long ms = NormalizeSleepMilliseconds(duration, SleepScale);
        long deadline = _clock.NowMs + ms;
        _timeline?.State("sleep", new() { ["duration_ms"] = ms, ["deadline_ms"] = deadline });
        _scene.Sleeping = true;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            // A poll loop (`get-input-type`, `sleep 1`) wakes about once a millisecond on native, so its 50 ms mouse
            // callback fires every 50 ms. Waiting here for the compositor rounded each short sleep up to a display
            // frame: Kamidori FIELD's callback ran every 50 or 67 ms, and its 100-call help overlay came late
            // (docs/engine-re.md, "Short sleeps do not wait for the display").
            switch (SleepPresentationFor(duration, _tick.StatementBlockOpen))
            {
                case SleepPresentation.Immediate: RequestPresentation(); break;
                case SleepPresentation.Synchronized: RequestSynchronizedPresentation(); break;
            }
            while (_clock.NowMs < deadline)
            {
                if (Stopping) break;
                // Native: once ADV fast-forward starts, the main loop dispatches ops ahead of the armed sleep timer.
                if (_skip.FastForwardActive) break;
                WaitAndTick(_frameSignal, _clock.RealMillisecondsUntil(deadline, 50));
            }
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _scene.Sleeping = false;
        _timeline?.State("running", new() { ["sleep_complete"] = true });
    }

    /// <summary>Op 0xd5's wait for the next timed callback (run-state 0x40): it advances the timer and the message
    /// pump but keeps the presentation barrier held, as native suppresses ordinary retained rendering until the
    /// callback's explicit 0x222.</summary>
    public void WaitForTimedCallbackDeadline(long duration)
    {
        long ms = NormalizeSleepMilliseconds(duration, 1.0);
        long deadline = _clock.NowMs + ms;
        _timeline?.State("timed-callback-wait",
            new() { ["duration_ms"] = ms, ["deadline_ms"] = deadline });
        while (_clock.NowMs < deadline && !Stopping)
            WaitAndTick(_frameSignal, _clock.RealMillisecondsUntil(deadline, 50));
        _timeline?.State("running", new() { ["timed_callback_due"] = true });
    }
}

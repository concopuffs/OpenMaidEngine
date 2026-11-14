using Ome.Engine.Model;

namespace Ome.Engine.Natives;

/// <summary>
/// The input and waits native as the interpreter calls it (engine layering spec §5.2, "Input and waits"): every wait
/// the handlers block in, the dialogs they wait on, the interpreter's frames, the scene reset's part in the waits, and
/// the AGE cursor. <see cref="InputAndWaits"/> implements it; a tool or a test that runs scripts with no front end gives
/// a stand-in (<see cref="Hosting.HeadlessWaits"/>, the tests' recording fakes) whose waits return at once.
/// </summary>
/// <remarks>The members with a body are what a stand-in does by default: it has no input, no clock of its own and no
/// dialogs. It names no device type (spec §3.3).</remarks>
public interface IInputAndWaits
{
    // ---- routing: the interpreter the front end's input reaches, and the stop and return-to-title requests ----

    /// <summary>Route the front end's input to <paramref name="interpreter"/>'s hotspots and callbacks.</summary>
    void RouteInputTo(IInterpreterInput interpreter) { }

    /// <summary>The stop and return-to-title requests the waits end on, which the interpreter reads too; null for a
    /// stand-in, whose interpreter keeps its own.</summary>
    StopAndReload? StopAndReload => null;

    // ---- warnings and dialogs ----

    /// <summary>Report a recoverable runtime discrepancy while allowing script execution to continue.</summary>
    void ReportWarning(string message) => System.Console.Error.WriteLine(message);

    /// <summary>Present a modal diagnostic and return only after the user dismisses it.</summary>
    void ShowDiagnosticMessage(DiagnosticMessage message)
        => System.Console.Error.WriteLine($"{message.Caption}: {message.Text}");

    /// <summary>Present an interpreter error with 中止/再試行/無視 and return the answer once the user picks one.
    /// A stand-in without a modal box shows the text and aborts.</summary>
    ScriptErrorChoice ShowScriptError(DiagnosticMessage message)
    {
        ShowDiagnosticMessage(message);
        return ScriptErrorChoice.Abort;
    }

    /// <summary>Present AGERC's modal full-width editor. Cancel preserves CurrentText.</summary>
    FullwidthTextEditResult EditFullwidthString(FullwidthTextEditRequest request)
        => new(false, request.CurrentText);

    // ---- the interpreter's frames, sleeps and the scene reset ----

    // The VM enters and leaves a script's frame: presentation waits for the outermost frame, and --timeline-log
    // records them.
    void EnterScriptContext(string scriptName) { }
    void ExitScriptContext(string scriptName) { }

    void Sleep(long duration);

    // Native op-0xd5 pacing sleeps inside run-state 0x40 without publishing retained gfx state.
    // An interactive native keeps this distinct from presentation-capable script op-0xc8 sleep.
    void WaitForTimedCallbackDeadline(long duration) => Sleep(duration);

    void FrameYield();

    // Native op 0x9 resets the scene's waits before reloading root script resource 0. The movie native has stopped
    // the movies, and the surfaces stay; the VM releases them afterwards when set:AutoFreeTex asks.
    void ResetSceneContext() { }

    // A front-end return-to-title request (VirtualMachine.RequestRootReload) for waits that do not supply
    // StopAndReload: end every blocking wait now so the VM reaches an opcode boundary. Waits stay interrupted until
    // the reset calls ResetSceneContext. Waits that supply it are woken by the request on that copy.
    void InterruptForRootReload() { }

    // ---- the modal movie ----

    // Native op 0x20f uses a universal packed id and parks script execution until the movie reaches EOF or the player
    // cancels it. The decoder stays asynchronous; the wait keeps the front end's frames coming.
    void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags) { }
    // The modal movie service polls native's input latch on its first tick, and a held action 4 stops the movie: the
    // next PlayModalMovieToSurface returns as cancelled at once (VirtualMachine.InputLatch.cs).
    void CancelNextModalMovie() { }

    // ---- presentation and screen transitions ----
    // Each GfxState passed is the graphics native's state. The input and waits native has its own reference to it; a
    // stand-in, which has none, reaches the state through the argument.

    void PresentObjectRange(GfxState gfx, long firstHandle, long count) { }

    // Normal playback reaches op 0x21c and parks until a queued 0x223 transition completes. The
    // read/message-skip branch reaches op 0x20c and presents the completed endpoint immediately.
    void WaitForForegroundTransition(GfxState gfx) { }
    void PresentFrame(GfxState gfx) { }

    // Legacy SYS4 screen-transition family (ops 0x21, 0x22, 0x24, and 0x25): scripts render complete
    // frames into numbered surfaces, then block while the engine alpha-composites an endpoint.
    // Native bypasses the timed service when ADV fast-forward is already active at opcode dispatch.
    void FadeSurfaceWithBlack(
        GfxState gfx, int surface, long intervalArgument, SurfaceBlackFadeDirection direction,
        bool forceEndpoint = false) { }
    void FadeSurfaceToWhite(
        GfxState gfx, int surface, long intervalArgument, bool forceEndpoint = false) { }
    void CrossfadeSurfaces(
        GfxState gfx, int sourceSurface, int targetSurface, long intervalArgument,
        bool forceEndpoint = false) { }
    void RevealSurfaceWithPattern(
        GfxState gfx, SurfacePatternTransitionRequest request, bool forceEndpoint = false) { }

    // ---- text and input waits ----

    /// <summary>
    /// Run-state 0x20000000's wait: block the engine thread while <paramref name="text"/> reveals the run it just
    /// armed, servicing <see cref="TextManager.ServiceReveal"/> at each engine tick until the run is out, a click or
    /// fast-forward finishes it, or the engine stops. A stand-in with no clock publishes the run at once.
    /// </summary>
    void WaitForTextReveal(GfxState gfx, TextManager text)
    {
        text.CompleteReveal();
        while (text.ServiceReveal(gfx) != null) { }
    }

    // Op 0x72's page wait. The interpreter has begun the page's wait on its layout (TextManager.BeginPageWait).
    void WaitForInput();

    // An interactive native services script callbacks on the engine thread while the enclosing ADV page remains
    // parked. The callback returns true while another queued input callback is ready to run.
    void WaitForInput(Func<bool> serviceInputCallback)
    {
        while (serviceInputCallback()) { }
        WaitForInput();
    }

    void WaitForInput(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
        => WaitForInput(serviceInputCallback);

    // Op 0xfa's wait: native run-state 0x80000000, the input poll of a text wait, with no page. There is no wait
    // indicator or page break, the text stays, and Auto has no timer of its own: native polls the shared timer that the
    // last page or sleep armed, which has normally expired, so an unvoiced Auto ends the wait at once. A stand-in
    // without a page model treats it as a text wait.
    void WaitForAdvInputPoll(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
        => WaitForInput(serviceInputCallback, autoWaitState);

    void WakeInputCallbackService() { }
    // Native clearing run-state 0x80000000 from a service call: the polling text wait returns as if the page were
    // advanced. Called on the engine thread from the wait's input callback.
    void EndInputWait() { }
    // A wait's callback ended and the waiting frame's hotspots are armed again (stand-ins count them).
    void InputCallbackCompleted() { }

    // Native run-state 0x4000000, the hidden-window wait: the engine keeps presenting frames and polls the triggered
    // actions until any arrives. Return once triggered() is true (it consumes the trigger) or the engine stops;
    // WakeInputCallbackService signals new input. A stand-in has no input, so it returns at once, as if a trigger had
    // arrived.
    void WaitForServiceTrigger(Func<bool> triggered) { }

    // Generic AGE input-callback services (ops 0xcc/0xcd, 0xfb/0xff/0x100, 0x108).
    // The input clock is the engine clock the frame turns run on.
    long InputClockMilliseconds => Environment.TickCount64;
    int JoystickButtonCount => 0;

    // ---- the AGE cursor (ops 0x86, 0x87 and 0x10a) ----

    void SetCursorResource(long resourceId) { }
    void ClearCursorResource() { }
    void WarpCursor(int virtualX, int virtualY) { }
}

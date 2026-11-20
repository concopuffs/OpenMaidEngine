using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

/// <summary>
/// The waits as the interpreter calls them while a Lua handler's <c>maid.vm.call</c> runs (Lua scripting spec §10):
/// each member that waits refuses before it reaches the waits native, so a headless run, whose waits return at once,
/// fails the same way as the port; every other member passes through. Every member of <see cref="IInputAndWaits"/> is on
/// <see cref="Waits"/> or <see cref="NotWaits"/>, and is implemented here, so none reaches the inner waits through a
/// default body (<c>ModCallWaitsTests</c>). The interpreter uses it only when the stack ships Lua.
/// </summary>
internal sealed class ModCallWaits(IInputAndWaits inner, VirtualMachine vm) : IInputAndWaits
{
    /// <summary>The members that block the engine thread: the text waits, the sleeps, the timed-callback wait, the
    /// transitions, the text reveal, the modal movie, the hidden-window wait and the dialogs.</summary>
    internal static readonly string[] Waits =
    [
        nameof(ShowDiagnosticMessage), nameof(ShowScriptError), nameof(EditFullwidthString),
        nameof(Sleep), nameof(WaitForTimedCallbackDeadline), nameof(PlayModalMovieToSurface),
        nameof(WaitForForegroundTransition), nameof(FadeSurfaceWithBlack), nameof(FadeSurfaceToWhite),
        nameof(CrossfadeSurfaces), nameof(RevealSurfaceWithPattern), nameof(MosaicSurfaces), nameof(WaitForTextReveal),
        nameof(WaitForInput), nameof(WaitForAdvInputPoll), nameof(WaitForServiceTrigger),
    ];

    /// <summary>The members that do not wait. A present and the front end's modal hold (<see cref="FrameYield"/>) may
    /// block, but run no script and take no save (spec §10).</summary>
    internal static readonly string[] NotWaits =
    [
        nameof(RouteInputTo), nameof(StopAndReload), nameof(ReportWarning),
        nameof(EnterScriptContext), nameof(ExitScriptContext), nameof(FrameYield), nameof(ResetSceneContext),
        nameof(InterruptForRootReload), nameof(CancelNextModalMovie), nameof(PresentObjectRange), nameof(PresentFrame),
        nameof(WakeInputCallbackService), nameof(EndInputWait), nameof(InputCallbackCompleted),
        nameof(InputClockMilliseconds), nameof(JoystickButtonCount),
        nameof(SetCursorResource), nameof(ClearCursorResource), nameof(WarpCursor),
    ];

    private void Refuse(string member) => vm.RefuseWaitInModCall(member);

    // ---- routing ----
    public void RouteInputTo(IInterpreterInput interpreter) => inner.RouteInputTo(interpreter);
    public StopAndReload? StopAndReload => inner.StopAndReload;

    // ---- warnings and dialogs ----
    public void ReportWarning(string message) => inner.ReportWarning(message);

    public void ShowDiagnosticMessage(DiagnosticMessage message)
    {
        Refuse(nameof(ShowDiagnosticMessage));
        inner.ShowDiagnosticMessage(message);
    }

    public ScriptErrorChoice ShowScriptError(DiagnosticMessage message)
    {
        Refuse(nameof(ShowScriptError));
        return inner.ShowScriptError(message);
    }

    public FullwidthTextEditResult EditFullwidthString(FullwidthTextEditRequest request)
    {
        Refuse(nameof(EditFullwidthString));
        return inner.EditFullwidthString(request);
    }

    // ---- frames, sleeps and the scene reset ----
    public void EnterScriptContext(string scriptName) => inner.EnterScriptContext(scriptName);
    public void ExitScriptContext(string scriptName) => inner.ExitScriptContext(scriptName);

    public void Sleep(long duration)
    {
        Refuse(nameof(Sleep));
        inner.Sleep(duration);
    }

    public void WaitForTimedCallbackDeadline(long duration)
    {
        Refuse(nameof(WaitForTimedCallbackDeadline));
        inner.WaitForTimedCallbackDeadline(duration);
    }

    public void FrameYield() => inner.FrameYield();
    public void ResetSceneContext() => inner.ResetSceneContext();
    public void InterruptForRootReload() => inner.InterruptForRootReload();

    // ---- the modal movie ----
    public void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags)
    {
        Refuse(nameof(PlayModalMovieToSurface));
        inner.PlayModalMovieToSurface(resourceId, surfaceSlot, movieFlags);
    }

    public void CancelNextModalMovie() => inner.CancelNextModalMovie();

    // ---- presentation and screen transitions ----
    public void PresentObjectRange(GfxState gfx, long firstHandle, long count) => inner.PresentObjectRange(gfx, firstHandle, count);

    public void WaitForForegroundTransition(GfxState gfx)
    {
        Refuse(nameof(WaitForForegroundTransition));
        inner.WaitForForegroundTransition(gfx);
    }

    public void PresentFrame(GfxState gfx) => inner.PresentFrame(gfx);

    public void FadeSurfaceWithBlack(GfxState gfx, int surface, long intervalArgument, SurfaceBlackFadeDirection direction,
                                     bool forceEndpoint = false)
    {
        Refuse(nameof(FadeSurfaceWithBlack));
        inner.FadeSurfaceWithBlack(gfx, surface, intervalArgument, direction, forceEndpoint);
    }

    public void FadeSurfaceToWhite(GfxState gfx, int surface, long intervalArgument, bool forceEndpoint = false)
    {
        Refuse(nameof(FadeSurfaceToWhite));
        inner.FadeSurfaceToWhite(gfx, surface, intervalArgument, forceEndpoint);
    }

    public void CrossfadeSurfaces(GfxState gfx, int sourceSurface, int targetSurface, long intervalArgument,
                                  bool forceEndpoint = false)
    {
        Refuse(nameof(CrossfadeSurfaces));
        inner.CrossfadeSurfaces(gfx, sourceSurface, targetSurface, intervalArgument, forceEndpoint);
    }

    public void RevealSurfaceWithPattern(GfxState gfx, SurfacePatternTransitionRequest request, bool forceEndpoint = false)
    {
        Refuse(nameof(RevealSurfaceWithPattern));
        inner.RevealSurfaceWithPattern(gfx, request, forceEndpoint);
    }

    public void MosaicSurfaces(GfxState gfx, int sourceSurface, int targetSurface, long intervalMilliseconds,
                               bool forceEndpoint = false)
    {
        Refuse(nameof(MosaicSurfaces));
        inner.MosaicSurfaces(gfx, sourceSurface, targetSurface, intervalMilliseconds, forceEndpoint);
    }

    // ---- text and input waits ----
    public void WaitForTextReveal(GfxState gfx, TextManager text)
    {
        Refuse(nameof(WaitForTextReveal));
        inner.WaitForTextReveal(gfx, text);
    }

    public void WaitForInput()
    {
        Refuse(nameof(WaitForInput));
        inner.WaitForInput();
    }

    public void WaitForInput(Func<bool> serviceInputCallback)
    {
        Refuse(nameof(WaitForInput));
        inner.WaitForInput(serviceInputCallback);
    }

    public void WaitForInput(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
    {
        Refuse(nameof(WaitForInput));
        inner.WaitForInput(serviceInputCallback, autoWaitState);
    }

    public void WaitForAdvInputPoll(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
    {
        Refuse(nameof(WaitForAdvInputPoll));
        inner.WaitForAdvInputPoll(serviceInputCallback, autoWaitState);
    }

    public void WakeInputCallbackService() => inner.WakeInputCallbackService();
    public void EndInputWait() => inner.EndInputWait();
    public void InputCallbackCompleted() => inner.InputCallbackCompleted();

    public void WaitForServiceTrigger(Func<bool> triggered)
    {
        Refuse(nameof(WaitForServiceTrigger));
        inner.WaitForServiceTrigger(triggered);
    }

    public long InputClockMilliseconds => inner.InputClockMilliseconds;
    public int JoystickButtonCount => inner.JoystickButtonCount;

    // ---- the AGE cursor ----
    public void SetCursorResource(long resourceId) => inner.SetCursorResource(resourceId);
    public void ClearCursorResource() => inner.ClearCursorResource();
    public void WarpCursor(int virtualX, int virtualY) => inner.WarpCursor(virtualX, virtualY);
}

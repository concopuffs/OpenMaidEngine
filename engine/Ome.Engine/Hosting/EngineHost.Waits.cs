using System;
using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Hosting;

// The waits the VM asks the host for are the input and waits native's (Natives/InputAndWaits*.cs): each member here
// calls it, until milestone 12 has the VM call the native itself. Nothing here blocks (InputAndWaitsTests).
public sealed partial class EngineHost
{
    // ---- dialogs ----
    public void ShowDiagnosticMessage(DiagnosticMessage message) => _waits.ShowDiagnosticMessage(message);
    public ScriptErrorChoice ShowScriptError(DiagnosticMessage message) => _waits.ShowScriptError(message);
    public FullwidthTextEditResult EditFullwidthString(FullwidthTextEditRequest request)
        => _waits.EditFullwidthString(request);

    // ---- the interpreter's frames, sleeps, the modal hold, the scene reset ----
    public void EnterScriptContext(string scriptName) => _waits.EnterScriptContext(scriptName);
    public void ExitScriptContext(string scriptName) => _waits.ExitScriptContext(scriptName);
    public void Sleep(long duration) => _waits.Sleep(duration);
    public void WaitForTimedCallbackDeadline(long duration) => _waits.WaitForTimedCallbackDeadline(duration);
    public void FrameYield() => _waits.FrameYield();
    public void ResetSceneContext() => _waits.ResetScene();
    public void InterruptForRootReload() => _waits.InterruptForRootReload();

    // ---- the modal movie ----
    public void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags)
        => _waits.PlayModalMovieToSurface(resourceId, surfaceSlot, movieFlags);
    public void CancelNextModalMovie() => _waits.CancelNextModalMovie();

    // ---- presentation and effects ----
    public void PresentObjectRange(GfxState gfx, long firstHandle, long count) => _waits.PresentObjectRange(firstHandle, count);
    public void WaitForForegroundTransition(GfxState gfx) => _waits.WaitForForegroundTransition(gfx);
    public void PresentFrame(GfxState gfx) => _waits.PresentFrame();

    public void FadeSurfaceWithBlack(
        GfxState gfx, int surface, long intervalArgument, SurfaceBlackFadeDirection direction,
        bool forceEndpoint = false)
        => _waits.WaitForScreenTransition(
            _graphics.StartSurfaceBlackFade(surface, intervalArgument, direction, forceEndpoint));

    public void CrossfadeSurfaces(
        GfxState gfx, int sourceSurface, int targetSurface, long intervalArgument,
        bool forceEndpoint = false)
        => _waits.WaitForScreenTransition(
            _graphics.StartSurfaceCrossfade(sourceSurface, targetSurface, intervalArgument, forceEndpoint));

    public void FadeSurfaceToWhite(
        GfxState gfx, int surface, long intervalArgument, bool forceEndpoint = false)
        => _waits.WaitForScreenTransition(_graphics.StartSurfaceWhiteFade(surface, intervalArgument, forceEndpoint));

    public void RevealSurfaceWithPattern(
        GfxState gfx, SurfacePatternTransitionRequest request, bool forceEndpoint = false)
        => _waits.WaitForScreenTransition(_graphics.StartSurfacePatternReveal(request, forceEndpoint));

    // ---- text and input waits ----
    public void WaitForTextReveal(GfxState gfx, TextManager text) => _waits.WaitForTextReveal(gfx);

    public void WaitForInput() => WaitForInput(0);

    public void WaitForInput(int layoutSlot)
        => WaitForInput(layoutSlot, static () => false, static () => default);

    public void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        => WaitForInput(layoutSlot, serviceInputCallback, static () => default);

    public void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
        => _waits.WaitAtAdvInputPoll(layoutSlot, serviceInputCallback, autoWaitState, textPage: true);

    // Op 0xfa: the same poll with no page (no page break or locator page, no wait indicator, the text stays).
    public void WaitForAdvInputPoll(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
        => _waits.WaitAtAdvInputPoll(0, serviceInputCallback, autoWaitState, textPage: false);

    public void WakeInputCallbackService() => _waits.WakeInputCallbackService();
    public void EndInputWait() => _waits.EndInputWait();
    public void WaitForServiceTrigger(Func<bool> triggered) => _waits.WaitForServiceTrigger(triggered);
    public long InputClockMilliseconds => _clock.NowMs;
}

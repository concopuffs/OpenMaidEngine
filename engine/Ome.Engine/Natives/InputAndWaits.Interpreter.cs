using System;
using Ome.Engine.Devices;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

// The native as the interpreter calls it (IInputAndWaits): the members whose shape is the interpreter's map to the
// waits in the other files, the legacy screen transitions start in the graphics native and wait here, and the AGE
// cursor goes to the front end's cursor device.
public sealed partial class InputAndWaits : IInputAndWaits
{
    private readonly ResourceMap? _cursors;   // the CUR records ops 0x86 name
    private readonly ICursorDevice? _cursor;

    void IInputAndWaits.ResetSceneContext() => ResetScene();
    long IInputAndWaits.InputClockMilliseconds => _clock.NowMs;

    // ---- presentation and the legacy screen transitions (ops 0x21, 0x22, 0x24 and 0x25) ----

    void IInputAndWaits.PresentObjectRange(GfxState gfx, long firstHandle, long count)
        => PresentObjectRange(firstHandle, count);
    void IInputAndWaits.PresentFrame(GfxState gfx) => PresentFrame();

    public void FadeSurfaceWithBlack(
        GfxState gfx, int surface, long intervalArgument, SurfaceBlackFadeDirection direction,
        bool forceEndpoint = false)
        => WaitForScreenTransition(_graphics.StartSurfaceBlackFade(surface, intervalArgument, direction, forceEndpoint));

    public void CrossfadeSurfaces(
        GfxState gfx, int sourceSurface, int targetSurface, long intervalArgument, bool forceEndpoint = false)
        => WaitForScreenTransition(
            _graphics.StartSurfaceCrossfade(sourceSurface, targetSurface, intervalArgument, forceEndpoint));

    public void FadeSurfaceToWhite(GfxState gfx, int surface, long intervalArgument, bool forceEndpoint = false)
        => WaitForScreenTransition(_graphics.StartSurfaceWhiteFade(surface, intervalArgument, forceEndpoint));

    public void RevealSurfaceWithPattern(
        GfxState gfx, SurfacePatternTransitionRequest request, bool forceEndpoint = false)
        => WaitForScreenTransition(_graphics.StartSurfacePatternReveal(request, forceEndpoint));

    // ---- text and input waits ----

    void IInputAndWaits.WaitForTextReveal(GfxState gfx, TextManager text) => WaitForTextReveal(gfx);

    void IInputAndWaits.WaitForInput() => WaitAtAdvInputPoll(0, static () => false, static () => default, textPage: true);

    void IInputAndWaits.WaitForInput(int layoutSlot)
        => WaitAtAdvInputPoll(layoutSlot, static () => false, static () => default, textPage: true);

    void IInputAndWaits.WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        => WaitAtAdvInputPoll(layoutSlot, serviceInputCallback, static () => default, textPage: true);

    void IInputAndWaits.WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                                     Func<AdvAutoWaitState> autoWaitState)
        => WaitAtAdvInputPoll(layoutSlot, serviceInputCallback, autoWaitState, textPage: true);

    // Op 0xfa: the same poll with no page (no page break or locator page, no wait indicator, the text stays).
    void IInputAndWaits.WaitForAdvInputPoll(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
        => WaitAtAdvInputPoll(0, serviceInputCallback, autoWaitState, textPage: false);

    // ---- the AGE cursor (ops 0x86, 0x87 and 0x10a) ----

    public void SetCursorResource(long resourceId)
    {
        AssetEntry? asset = _cursors?.ResolveCursor(resourceId);
        if (asset == null) return;
        try
        {
            CursorImage cursor = _cursors!.DecodeCursor(asset);
            _cursor?.SetCursor(cursor.Image, cursor.HotspotX, cursor.HotspotY);
            _timeline?.Event("cursor-set", new() { ["resource"] = resourceId, ["asset"] = asset.Name });
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"cursor decode {asset.Name}: {ex.Message}");
        }
    }

    public void ClearCursorResource()
    {
        _cursor?.ClearCursor();
        _timeline?.Event("cursor-clear", new());
    }

    public void WarpCursor(int virtualX, int virtualY)
    {
        _cursor?.WarpCursor(virtualX, virtualY);
        _timeline?.Event("cursor-warp", new() { ["x"] = virtualX, ["y"] = virtualY });
    }
}

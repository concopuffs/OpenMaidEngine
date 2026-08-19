using Age.Engine.Model;

namespace Age.Engine.Hosting;

public interface IGraphicsHost
{
    void FillSurfaceRect(SurfaceRectFill fill) { }
    void CopySurfaceRect(SurfaceRectCopy copy) { }
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

    void CreateTexture(int slot, int width, int height);

    /// <summary>Return a stable RGBA snapshot of one numbered surface, or null when unavailable.</summary>
    RgbaImage? CaptureSurfacePixels(int slot) => null;

    /// <summary>Replace one numbered surface from decoded RGBA pixels. False means unsupported.</summary>
    bool ReplaceSurfacePixels(int slot, RgbaImage image) => false;

    void SetTexture(long resourceId, int slot);
    void SetTexture(long resourceId, int slot, long colorKey) => SetTexture(resourceId, slot);
    void ReleaseSurface(int slot) { }

    /// <summary>Clear the selected target's pixels; -1 denotes the main backbuffer.</summary>
    void ClearRenderTarget(int surfaceSlot) { }

    void ReleaseSurfaceRange(int firstSlot, int count)
    {
        for (int slot = firstSlot; slot < firstSlot + count; slot++) ReleaseSurface(slot);
    }

    void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY);
    (int Width, int Height) GetTextureSize(int slot);
}

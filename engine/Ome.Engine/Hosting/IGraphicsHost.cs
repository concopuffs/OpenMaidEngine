using Ome.Engine.Model;

namespace Ome.Engine.Hosting;

/// <summary>
/// The presentation waits the graphics ops block on. The ops' state is the graphics native's
/// (<see cref="Natives.Graphics"/>): a handler calls the native, and these members only present and wait until
/// milestone 11 moves the waits (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.3). Each
/// <see cref="GfxState"/> passed is the native's state.
/// </summary>
public interface IGraphicsHost
{
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
}

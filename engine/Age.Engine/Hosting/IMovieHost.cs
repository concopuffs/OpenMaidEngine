using Age.Engine.Model;

namespace Age.Engine.Hosting;

public interface IMovieHost
{
    // Native op 0x236 binds a movie decoder to an existing retained texture surface.
    // Playback is non-modal: the VM advances to the following instruction while the host publishes frames.
    /// <returns>The initialized movie graph's stop position in truncated integer milliseconds, or null
    /// when the host could not obtain usable timing metadata. Native op 0x23f queries this state
    /// immediately after 0x236 returns.</returns>
    long? PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask) => null;

    // Native op 0x241 uses the same graph/surface lifecycle as 0x236, but seeks the graph before
    // playback configuration. Hosts without positioned decoding may fall back to ordinary playback.
    long? PlayMovieToSurfaceAtPosition(
        long resourceId, int surfaceSlot, long movieFlags, long syncMask, long positionMs)
        => PlayMovieToSurface(resourceId, surfaceSlot, movieFlags, syncMask);

    // Native op 0x24d captures a retained range into the scratch surface, then publishes each
    // decoded movie frame's green channel as an exact packed-alpha mask. A nonvisual host completes
    // the lifecycle immediately so a following 0x21c can never strand script execution.
    void PlayMovieMaskTransition(GfxState gfx, MovieMaskTransitionRequest request)
    {
        gfx.QueueMovieMaskTransition(request);
        gfx.CompleteMovieMaskTransition(request.SurfaceSlot);
    }

    bool IsMovieSurfaceActive(int surfaceSlot) => false;

    // Native op 0x20f uses a universal packed id and parks script execution until the movie
    // reaches EOF or the player cancels it. The decoder remains asynchronous; the interactive host
    // owns the modal wait so its render loop can continue publishing frames.
    void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags) { }
}

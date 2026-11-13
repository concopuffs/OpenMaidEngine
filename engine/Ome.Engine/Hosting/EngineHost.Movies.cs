using Ome.Engine.Sys4;

namespace Ome.Engine.Hosting;

// The movies are the movie native's (Natives/Movies*.cs). Op 0x20f's modal wait and its three fields stay here with
// the other waits until milestone 11.
public sealed partial class EngineHost
{
    public EngineHostDiagnosticSnapshot CaptureDiagnosticSnapshot()
    {
        bool screenTransitionActive = _graphics.ScreenTransitionPending;
        return new EngineHostDiagnosticSnapshot(
            IsWaiting,
            IsTransitionWaiting,
            IsSleeping,
            _text.IsRevealing,
            _modalMovieWaiting,
            _text.IsAdvPagePresentationSuspended,
            _skip.FastForwardActive,
            screenTransitionActive,
            TransitionStartedAtMs,
            _movies.SurfaceDiagnostics(),
            _movies.CompletedPlaybackIds());
    }

    public void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags)
    {
        bool cancelAtStart = _modalMovieCancelPending;
        _modalMovieCancelPending = false;
        AssetEntry? asset = _movies.ResolveModal(resourceId);
        if (asset == null) return;

        _modalMovieCancelled = cancelAtStart;
        _modalMovieWaiting = true;
        bool scriptSuspended = false;
        try
        {
            if (!_movies.StartModal(asset, resourceId, surfaceSlot, movieFlags, out long playbackId)) return;
            _timeline?.State("modal-movie-wait", new()
            {
                ["resource"] = resourceId, ["playback"] = playbackId,
                ["surface"] = surfaceSlot, ["file"] = asset.Name,
            });
            scriptSuspended = SuspendScriptForPresentation();
            RequestSynchronizedPresentation();
            while (!_stopping && !_modalMovieCancelled)
            {
                if (!_movies.IsSurfaceActive(surfaceSlot)) break;
                WaitAndTick(_frameSignal, 50);
            }

            // Cancellation is a completed modal presentation from the script's perspective. The
            // wrapper's following surface-release opcode performs the ordinary decoder teardown.
            if (_modalMovieCancelled)
                _movies.MarkCompleted(playbackId);
            _timeline?.State("running", new()
            {
                ["modal_movie_complete"] = !_modalMovieCancelled,
                ["modal_movie_cancelled"] = _modalMovieCancelled,
            });
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
            _modalMovieWaiting = false;
            _modalMovieCancelled = false;
        }
    }
}

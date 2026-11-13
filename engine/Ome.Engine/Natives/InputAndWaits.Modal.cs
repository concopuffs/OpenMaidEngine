using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

// The modal waits: the engine thread blocks, running nothing, while the shell native's dialog is open (native's
// message boxes and AGERC's dialogs freeze its main loop); and op 0x20f's modal movie (run-state 0x2000), which runs the
// tick, since native services its main loop during one.
public sealed partial class InputAndWaits
{
    private volatile bool _modalMovieWaiting;
    private volatile bool _modalMovieCancelled;
    private volatile bool _modalMovieCancelPending;   // CancelNextModalMovie: a held action 4 stops the next movie

    /// <summary>Op 0x20f's modal movie parks the engine thread.</summary>
    public bool IsModalMovieWaiting => _modalMovieWaiting;

    /// <summary>Op 0x1b4's box: the engine thread blocks until it is dismissed.</summary>
    public void ShowDiagnosticMessage(DiagnosticMessage message)
    {
        if (Stopping) return;
        _timeline?.Event("diagnostic-message", new()
        {
            ["caption"] = message.Caption,
            ["text"] = message.Text,
        });
        DiagnosticBox box = _shell.OpenDiagnostic(message);
        while (!Stopping && !WaitModal(box.Closed, 50)) { }
    }

    /// <summary>The interpreter error box (中止/再試行/無視): the engine thread blocks until the dialog answers. A stop
    /// aborts; a root reload (タイトルに戻る) retries, so the VM reaches its next opcode boundary and reloads.</summary>
    public ScriptErrorChoice ShowScriptError(DiagnosticMessage message)
    {
        if (_stopAndReload.StopRequested) return ScriptErrorChoice.Abort;
        _timeline?.Event("script-error", new()
        {
            ["caption"] = message.Caption,
            ["text"] = message.Text,
        });
        ScriptErrorBox box = _shell.OpenScriptError(message);
        while (!Stopping && !WaitModal(box.Closed, 50)) { }
        if (_stopAndReload.StopRequested) return ScriptErrorChoice.Abort;
        if (_stopAndReload.ReloadRequested) return ScriptErrorChoice.Retry;
        return box.Choice;
    }

    /// <summary>AGERC's full-width editor: the engine thread blocks until it answers. Not accepted, with the current
    /// text, unless answered.</summary>
    public FullwidthTextEditResult EditFullwidthString(FullwidthTextEditRequest request)
    {
        if (Stopping) return new(false, request.CurrentText);
        _timeline?.Event("fullwidth-text-edit", new()
        {
            ["current"] = request.CurrentText,
            ["initial"] = request.InitialText,
        });
        TextEntry entry = _shell.OpenTextEntry(request);
        while (!Stopping && !WaitModal(entry.Closed, 50)) { }
        return entry.Result;
    }

    /// <summary>The modal movie service polls native's input latch on its first tick, and a held action 4 stops the
    /// movie: the next <see cref="PlayModalMovieToSurface"/> returns as cancelled at once.</summary>
    public void CancelNextModalMovie() => _modalMovieCancelPending = true;

    /// <summary>Op 0x20f: play a movie to <paramref name="surfaceSlot"/> and park the engine thread until it reaches
    /// its end or the player cancels it. The decoder stays asynchronous; the front end keeps publishing frames.</summary>
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
            while (!Stopping && !_modalMovieCancelled)
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

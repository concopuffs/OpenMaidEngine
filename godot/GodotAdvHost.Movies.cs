using System;
using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;

public sealed partial class GodotAdvHost
{
    private readonly MovieSurfaceRegistry _movieSurfaces = new();
    private sealed record MovieMaskPlayback(
        GfxState Gfx, MovieMaskTransitionRequest Request, RgbaImage Captured);
    private readonly object _movieMaskLock = new();
    private readonly Dictionary<long, MovieMaskPlayback> _movieMasksByPlayback = new();
    private readonly Dictionary<int, long> _movieMaskPlaybackBySurface = new();

    public bool IsMovieSurfaceBound(int surfaceSlot) => _movieSurfaces.IsBound(surfaceSlot);

    public long? PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask)
    {
        string scene = CurrentScene;
        var asset = _res.ResolveMovie(resourceId);
        if (asset == null) { Godot.GD.Print($"movie unresolved {scene}:0x{resourceId:x}"); return null; }
        StartMovie(asset, resourceId, surfaceSlot, movieFlags, syncMask, modal: false,
                   initialPositionMs: 0, startDelayMs: 0, presentationDurationMs: null,
                   movieMask: null, out long? stopTimeMs, out _);
        return stopTimeMs ?? 0;
    }

    public long? PlayMovieToSurfaceAtPosition(
        long resourceId, int surfaceSlot, long movieFlags, long syncMask, long positionMs)
    {
        string scene = CurrentScene;
        var asset = _res.ResolveMovie(resourceId);
        if (asset == null) { Godot.GD.Print($"movie unresolved {scene}:0x{resourceId:x}"); return null; }
        StartMovie(asset, resourceId, surfaceSlot, movieFlags, syncMask, modal: false,
                   initialPositionMs: positionMs, startDelayMs: 0, presentationDurationMs: null,
                   movieMask: null, out long? stopTimeMs, out _);
        return stopTimeMs ?? 0;
    }

    public void PlayMovieMaskTransition(GfxState gfx, MovieMaskTransitionRequest request)
    {
        gfx.QueueMovieMaskTransition(request);
        RgbaImage captured = CaptureRetainedRange(
            gfx, request.SurfaceSlot, request.SourceRangeStart, request.SourceRangeCount);
        byte initialFill = request.Mode == 1 ? (byte)0 : (byte)255;
        PublishMaskedCapture(
            request.SurfaceSlot, captured,
            CreateFilledMask(request.Width, request.Height, initialFill), request);

        var asset = _res.ResolveMovie(request.ResourceId);
        if (asset == null)
        {
            Godot.GD.Print($"movie mask unresolved {CurrentScene}:0x{request.ResourceId:x}");
            PublishMovieMaskTerminal(new MovieMaskPlayback(gfx, request, captured));
            return;
        }

        var mask = new MovieMaskPlayback(gfx, request, captured);
        bool started = StartMovie(
            asset, request.ResourceId, request.SurfaceSlot, movieFlags: 6, syncMask: 0,
            modal: false, initialPositionMs: 0,
            startDelayMs: request.StartDelayMs,
            presentationDurationMs: request.DurationMs,
            movieMask: mask, out long? stopTimeMs, out _);
        gfx.SetMovieStopTime(request.SurfaceSlot, stopTimeMs ?? 0);
        if (!started) PublishMovieMaskTerminal(mask);
    }

    public bool IsMovieSurfaceActive(int surfaceSlot)
        => _movieSurfaces.IsActive(surfaceSlot);

    public void SetMoviePlaybackRate(int surfaceSlot, double rate)
    {
        if (!_movieSurfaces.TryGetBinding(surfaceSlot, out MovieSurfaceBinding binding)) return;
        _main.QueueMoviePlaybackRate(binding.PlaybackId, rate);
        _timeline?.Event("movie-rate", new()
        {
            ["surface"] = surfaceSlot,
            ["playback"] = binding.PlaybackId,
            ["rate"] = rate,
        });
    }

    public GodotHostDiagnosticSnapshot CaptureDiagnosticSnapshot()
    {
        IReadOnlyList<MovieSurfaceDiagnostic> movies = _movieSurfaces.Snapshot();
        IReadOnlyList<long> completed = _movieSurfaces.CompletedPlaybackIds();
        bool screenTransitionActive;
        lock (_screenTransitionLock) screenTransitionActive = _screenTransition != null;
        return new GodotHostDiagnosticSnapshot(
            CurrentScene,
            IsWaiting,
            IsTransitionWaiting,
            IsSleeping,
            IsTextRevealing,
            _modalMovieWaiting,
            _advPagePresentationSuspended,
            _messageSkipActive,
            screenTransitionActive,
            TransitionStartedAtMs,
            movies,
            completed);
    }

    public void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags)
    {
        var asset = _res.ResolveMovie(resourceId);
        if (asset == null)
        {
            Godot.GD.Print($"modal movie unresolved packed:0x{resourceId:x}");
            return;
        }

        _modalMovieCancelled = false;
        _modalMovieWaiting = true;
        bool scriptSuspended = false;
        try
        {
            if (!StartMovie(asset, resourceId, surfaceSlot, movieFlags, 0, modal: true,
                            initialPositionMs: 0, startDelayMs: 0,
                            presentationDurationMs: null, movieMask: null,
                            out _, out long playbackId)) return;
            _timeline?.State("modal-movie-wait", new()
            {
                ["resource"] = resourceId, ["playback"] = playbackId,
                ["surface"] = surfaceSlot, ["file"] = asset.Name,
            });
            scriptSuspended = SuspendScriptForPresentation();
            RequestSynchronizedPresentation();
            while (!_stopping && !_modalMovieCancelled)
            {
                if (!_movieSurfaces.IsActive(surfaceSlot)) break;
                _frameSignal.WaitOne(50);
            }

            // Cancellation is a completed modal presentation from the script's perspective. The
            // wrapper's following surface-release opcode performs the ordinary decoder teardown.
            if (_modalMovieCancelled)
                _movieSurfaces.Complete(playbackId);
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

    private bool StartMovie(AssetEntry asset, long resourceId, int surfaceSlot, long movieFlags,
                            long syncMask, bool modal, long initialPositionMs, long startDelayMs,
                            long? presentationDurationMs, MovieMaskPlayback? movieMask,
                            out long? stopTimeMs, out long playbackId)
    {
        stopTimeMs = null;
        // A playback is a surface-owned instance, not the shared resource id. BTL can schedule the same
        // asset on multiple surfaces; replacing one binding must not erase another binding's completion.
        MovieSurfaceBinding binding = _movieSurfaces.Begin(surfaceSlot, resourceId, out var replaced);
        playbackId = binding.PlaybackId;
        if (replaced is { } prior)
        {
            AbandonMovieMaskPlayback(prior.PlaybackId);
            ForgetMovieMaskSurface(prior.SurfaceSlot, prior.PlaybackId);
            _main.CallDeferred("StopMovie", prior.PlaybackId);
        }
        if (movieMask != null)
            lock (_movieMaskLock)
            {
                _movieMasksByPlayback[playbackId] = movieMask;
                _movieMaskPlaybackBySurface[surfaceSlot] = playbackId;
            }
        lock (_imageLock)
        {
            if (movieMask == null) _surfaceImages.Remove(surfaceSlot);
            _surfaceColorKeys.Remove(surfaceSlot);
        }
        // Movie surfaces inherit the selected game's primary size until a decoded frame supplies content.
        _slotDims[surfaceSlot] = movieMask == null
            ? (_screenWidth, _screenHeight)
            : (movieMask.Captured.Width, movieMask.Captured.Height);
        try
        {
            var movie = _res.ReadMovie(asset);
            _timeline?.Event("movie-start", new()
            {
                ["resource"] = resourceId, ["playback"] = playbackId,
                ["surface"] = surfaceSlot, ["file"] = movie.Name,
                ["flags"] = movieFlags, ["sync_mask"] = syncMask, ["modal"] = modal,
                ["initial_position_ms"] = Math.Max(0, initialPositionMs),
                ["start_delay_ms"] = Math.Max(0, startDelayMs),
                ["presentation_duration_ms"] = presentationDurationMs,
                ["movie_mask"] = movieMask != null,
            });
            bool started = _main.TryPlayMovie(
                movie.Bytes, movie.Name, playbackId, resourceId, asset.PackedId, movieFlags,
                initialPositionMs, startDelayMs, presentationDurationMs, out stopTimeMs);
            if (!started)
            {
                stopTimeMs = 0;
                NotifyMovieCompleted(playbackId);
            }
            return started;
        }
        catch (System.Exception e)
        {
            AbandonMovieMaskPlayback(playbackId);
            _movieSurfaces.Abandon(playbackId, out _);
            _slotDims.Remove(surfaceSlot);
            stopTimeMs = 0;
            Godot.GD.Print($"movie read failed {asset.Name}: {e.Message}");
            return false;
        }
    }

    public void PublishMovieFrame(long playbackId, string name, int assetId, RgbaImage frame)
    {
        MovieMaskPlayback? maskPlayback;
        lock (_movieMaskLock) _movieMasksByPlayback.TryGetValue(playbackId, out maskPlayback);
        if (maskPlayback != null)
        {
            var request = maskPlayback.Request;
            byte initialFill = request.Mode == 1 ? (byte)0 : (byte)255;
            byte[] mask = MovieMaskSurface.ExtractGreen(
                frame, request.Width, request.Height, initialFill);
            PublishMaskedCapture(request.SurfaceSlot, maskPlayback.Captured, mask, request);
            return;
        }
        if (_movieSurfaces.PublishFrame(playbackId, frame, name, assetId))
            System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
    }

    public void NotifyMovieCompleted(long playbackId)
    {
        MovieMaskPlayback? maskPlayback = RemoveMovieMaskPlayback(playbackId);
        if (maskPlayback != null) PublishMovieMaskTerminal(maskPlayback);
        if (!_movieSurfaces.Complete(playbackId)) return;
        _timeline?.Event("movie-complete", new() { ["playback"] = playbackId });
        _frameSignal.Set();
    }

    private bool HasActiveMoviePresentation()
        => _movieSurfaces.HasActivePlayback;

    private RgbaImage CaptureRetainedRange(
        GfxState gfx, int targetSlot, long firstHandle, int count)
    {
        var dimensions = _slotDims.GetValueOrDefault(
            targetSlot, (W: _screenWidth, H: _screenHeight));
        int width = System.Math.Max(0, dimensions.W);
        int height = System.Math.Max(0, dimensions.H);
        var captured = new RgbaImage(width, height, new byte[checked(width * height * 4)]);
        IReadOnlyList<RenderObject> visible = gfx.SnapshotVisibleObjects(_clock.NowMs);
        RetainedSurfaceRasterizer.CompositeRange(
            captured, visible, firstHandle, System.Math.Max(0, count),
            item =>
            {
                var raw = gfx.TryGet(item.Handle);
                var resolved = raw != null
                    ? ResolveSurfaceTexture(raw.SourceSlot, item.SurfaceResId)
                    : ResolveResIdTexture(item.SurfaceResId);
                return resolved == null
                    ? null
                    : RgbaSurfaceOps.WithColorKey(resolved.Value.Image, item.ColorKey);
            });
        return captured;
    }

    private static byte[] CreateFilledMask(int width, int height, byte fill)
    {
        var mask = new byte[checked(System.Math.Max(0, width) * System.Math.Max(0, height))];
        if (fill != 0) System.Array.Fill(mask, fill);
        return mask;
    }

    private void PublishMaskedCapture(
        int surfaceSlot, RgbaImage captured, byte[] mask, MovieMaskTransitionRequest request)
    {
        RgbaImage image = MovieMaskSurface.Apply(
            captured, mask, System.Math.Max(0, request.Width),
            System.Math.Max(0, request.Height), request.X, request.Y);
        lock (_imageLock) _surfaceImages[surfaceSlot] = image;
        _slotDims[surfaceSlot] = (image.Width, image.Height);
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        _frameSignal.Set();
    }

    private void PublishMovieMaskTerminal(MovieMaskPlayback playback)
    {
        byte terminalFill = playback.Request.Mode == 1 ? (byte)255 : (byte)0;
        PublishMaskedCapture(
            playback.Request.SurfaceSlot, playback.Captured,
            CreateFilledMask(playback.Request.Width, playback.Request.Height, terminalFill),
            playback.Request);
        playback.Gfx.CompleteMovieMaskTransition(playback.Request.SurfaceSlot);
    }

    private MovieMaskPlayback? RemoveMovieMaskPlayback(long playbackId)
    {
        lock (_movieMaskLock)
        {
            if (!_movieMasksByPlayback.Remove(playbackId, out var playback)) return null;
            return playback;
        }
    }

    private void AbandonMovieMaskPlayback(long playbackId)
    {
        MovieMaskPlayback? playback = RemoveMovieMaskPlayback(playbackId);
        if (playback != null)
        {
            ForgetMovieMaskSurface(playback.Request.SurfaceSlot, playbackId);
            playback.Gfx.CompleteMovieMaskTransition(playback.Request.SurfaceSlot);
        }
    }

    private void ForgetMovieMaskSurface(int surfaceSlot, long playbackId)
    {
        lock (_movieMaskLock)
            if (_movieMaskPlaybackBySurface.GetValueOrDefault(surfaceSlot) == playbackId)
                _movieMaskPlaybackBySurface.Remove(surfaceSlot);
    }
}

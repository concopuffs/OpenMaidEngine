using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;

public sealed partial class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _rootScene;
    private readonly object _imageLock = new();
    private readonly Dictionary<int, RgbaImage?> _images = new();        // packed catalog id -> decoded pixels
    // Mutable AGE surfaces are published by replacing immutable RgbaImage snapshots, so the compositor
    // can safely finish reading an old frame while the VM prepares a copied-rectangle update.
    private readonly Dictionary<int, RgbaImage> _surfaceImages = new();
    private readonly Dictionary<int, long> _surfaceColorKeys = new();
    private readonly Dictionary<int, long> _surfaceResources = new();    // surface slot -> packed catalog id
    private readonly MovieSurfaceRegistry _movieSurfaces = new();
    private sealed record MovieMaskPlayback(
        GfxState Gfx, MovieMaskTransitionRequest Request, RgbaImage Captured);
    private readonly object _movieMaskLock = new();
    private readonly Dictionary<long, MovieMaskPlayback> _movieMasksByPlayback = new();
    private readonly Dictionary<int, long> _movieMaskPlaybackBySurface = new();
    private readonly string?[] _sfxNames = new string?[10];              // SC0000 native channel subset
    // slot -> dimensions of the currently allocated surface. Slot 0 begins as the selected game's
    // logical canvas, but op 0x1fa releases it like any other slot; subsequent queries return 0x0.
    private readonly Dictionary<int, (int W, int H)> _slotDims = new();
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly Age.Engine.Hosting.FrameClock _clock;
    private readonly GodotTimelineLog? _timeline;
    private readonly PageLocatorState _locator;
    private int _voiceBgmDuckControl;
    private (AudioPayload Audio, int PlaybackVariant)? _queuedSkippedVoice;
    private readonly object _scheduledVoiceLock = new();
    private (AudioPayload Audio, int PlaybackVariant, uint DelayMs, uint? StartMs)? _scheduledVoice;
    public GodotAdvHost(Main main, ResourceMap res, string scene, Age.Engine.Hosting.FrameClock clock,
                        PageLocatorState locator, Sys4LogicalCanvas logicalCanvas,
                        IGlyphMaskRasterizer surfaceTextRasterizer,
                        GodotTimelineLog? timeline = null,
                        bool synchronizeExplicitPresentation = true,
                        int surfaceTextMaskCacheCapacity = 2048)
    {
        _main = main; _res = res; _rootScene = scene; _clock = clock;
        _locator = locator; _timeline = timeline;
        _screenWidth = logicalCanvas.Width;
        _screenHeight = logicalCanvas.Height;
        _slotDims[0] = (_screenWidth, _screenHeight);
        _synchronizeExplicitPresentation = synchronizeExplicitPresentation;
        ArgumentNullException.ThrowIfNull(surfaceTextRasterizer);
        _surfaceTextBackendInfo =
            (surfaceTextRasterizer as IIdentifiedGlyphMaskRasterizer)?.BackendInfo
            ?? throw new ArgumentException(
                "Gameplay glyph rasterizers must identify their policy.",
                nameof(surfaceTextRasterizer));
        _surfaceTextMaskCache = new CachedGlyphMaskRasterizer(
            surfaceTextRasterizer, capacity: surfaceTextMaskCacheCapacity);
        _surfaceTextPixelRenderer =
            new ImmediateSurfaceTextRenderer(
                _surfaceTextMaskCache, _surfaceTextBackendInfo.Policy);
        _retainedGlyphLayoutEngine =
            new RetainedGlyphLayoutEngine(_surfaceTextMaskCache);
    }

    public Sys4LogicalCanvas LogicalCanvas => new(_screenWidth, _screenHeight);
    public void ReportWarning(string message) => System.Console.Error.WriteLine(message);

    public void FillSurfaceRect(SurfaceRectFill fill)
    {
        RgbaImage? destination = ResolveSurfacePixels(fill.SurfaceSlot);
        if (destination == null && _slotDims.TryGetValue(fill.SurfaceSlot, out var dimensions)
            && dimensions.W >= 0 && dimensions.H >= 0)
            destination = new RgbaImage(dimensions.W, dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
        if (destination != null)
        {
            var updated = new RgbaImage(destination.Width, destination.Height,
                (byte[])destination.Pixels.Clone());
            if (RgbaSurfaceOps.FillRect(updated, fill.X, fill.Y, fill.Width, fill.Height,
                                        unchecked((byte)fill.Alpha), fill.Rgb))
            {
                lock (_imageLock) _surfaceImages[fill.SurfaceSlot] = updated;
                System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
            }
        }
        _timeline?.Event("surface-fill", new()
        {
            ["surface"] = fill.SurfaceSlot, ["x"] = fill.X, ["y"] = fill.Y,
            ["w"] = fill.Width, ["h"] = fill.Height, ["alpha"] = fill.Alpha, ["rgb"] = fill.Rgb,
        });
    }

    public bool TraceOps;   // --gfx-log: print set-texture/create-texture slot assignments (diagnose slot collisions)

    public void CreateTexture(int slot, int width, int height)
    {
        lock (_textLock) _surfaceResources.Remove(slot);
        int safeWidth = System.Math.Max(0, width);
        int safeHeight = System.Math.Max(0, height);
        lock (_imageLock)
        {
            _surfaceImages[slot] = new RgbaImage(safeWidth, safeHeight,
                new byte[checked(safeWidth * safeHeight * 4)]);
            _surfaceColorKeys.Remove(slot);
        }
        _slotDims[slot] = (safeWidth, safeHeight);
        if (TraceOps) Godot.GD.Print($"[op] create-texture slot={slot} {width}x{height}");
    }

    public void SetTexture(long resourceId, int slot) => SetTexture(resourceId, slot, -1);

    public void SetTexture(long resourceId, int slot, long colorKey)
    {
        lock (_imageLock)
        {
            _surfaceImages.Remove(slot);
            _surfaceColorKeys[slot] = colorKey;
        }
        lock (_textLock)
        {
            _surfaceResources[slot] = resourceId;
        }
        var asset = _res.ResolveTexture(resourceId);
        var image = asset != null ? Decode(asset) : null;
        _slotDims[slot] = image != null ? (image.Width, image.Height) : (0, 0);
        if (TraceOps) Godot.GD.Print($"[op] set-texture slot={slot} resId=0x{resourceId:x} -> {(asset?.Name ?? "<none>")}");
    }

    // AGF is decoded synchronously on the VM thread so geometry queried immediately afterward sees real dims.
    public (int Width, int Height) GetTextureSize(int slot)
        => _slotDims.TryGetValue(slot, out var d) ? (d.W, d.H) : (0, 0);

    public RgbaImage? CaptureSurfacePixels(int slot)
    {
        RgbaImage? image = ResolveSurfacePixels(slot);
        return image == null
            ? null
            : new RgbaImage(image.Width, image.Height, (byte[])image.Pixels.Clone());
    }

    public bool ReplaceSurfacePixels(int slot, RgbaImage image)
    {
        if (image.Width <= 0 || image.Height <= 0
            || image.Pixels.Length != checked(image.Width * image.Height * 4))
            return false;
        lock (_imageLock)
        {
            _surfaceImages[slot] =
                new RgbaImage(image.Width, image.Height, (byte[])image.Pixels.Clone());
            _surfaceColorKeys.Remove(slot);
        }
        lock (_textLock)
        {
            _surfaceResources.Remove(slot);
            foreach (int layoutSlot in _retainedTextLayouts
                         .Where(pair => pair.Value.Binding.SourceSurfaceSlot == slot)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _retainedTextLayouts.Remove(layoutSlot);
                _retainedHistoryLayouts.Remove(layoutSlot);
            }
        }
        _slotDims[slot] = (image.Width, image.Height);
        return true;
    }

    // Retained render model: draw-texture updates GfxState (object -> surface bind); Main._Process composites
    // the visible objects each frame in ascending-handle order. No immediate blit here.
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }

    public void CopySurfaceRect(SurfaceRectCopy copy)
    {
        RgbaImage? source = ResolveSurfacePixels(copy.SourceSurface);
        RgbaImage? destination = ResolveSurfacePixels(copy.DestinationSurface);
        if (destination == null && _slotDims.TryGetValue(copy.DestinationSurface, out var dimensions)
            && dimensions.W >= 0 && dimensions.H >= 0)
            destination = new RgbaImage(dimensions.W, dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
        if (source == null || destination == null)
        {
            ReportWarning($"surface copy unresolved source={copy.SourceSurface} destination={copy.DestinationSurface}");
            return;
        }

        var updated = new RgbaImage(destination.Width, destination.Height, (byte[])destination.Pixels.Clone());
        if (RgbaSurfaceOps.CopyRect(source, updated, copy.SourceX, copy.SourceY, copy.Width, copy.Height,
                                   copy.DestinationX, copy.DestinationY))
        {
            lock (_imageLock) _surfaceImages[copy.DestinationSurface] = updated;
            System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        }
        _timeline?.Event("surface-copy", new()
        {
            ["source"] = copy.SourceSurface, ["source_x"] = copy.SourceX, ["source_y"] = copy.SourceY,
            ["w"] = copy.Width, ["h"] = copy.Height, ["destination"] = copy.DestinationSurface,
            ["destination_x"] = copy.DestinationX, ["destination_y"] = copy.DestinationY,
        });
    }

    private RgbaImage? ResolveSurfacePixels(int slot)
    {
        lock (_imageLock)
            if (_surfaceImages.TryGetValue(slot, out var mutable)) return mutable;
        long resourceId;
        lock (_textLock)
            if (!_surfaceResources.TryGetValue(slot, out resourceId)) return null;
        var resolved = ResolveResIdTexture(resourceId);
        if (resolved == null) return null;
        long colorKey;
        lock (_imageLock) colorKey = _surfaceColorKeys.GetValueOrDefault(slot, -1);
        return RgbaSurfaceOps.WithColorKey(resolved.Value.Image, colorKey);
    }

    /// <summary>Resolve a gfx surface through universal packed addressing and decode it from the
    /// loose-first asset store.</summary>
    public (RgbaImage Image, string Name, int AssetId, bool IsDynamic)? ResolveResIdTexture(long resId)
    {
        if (_movieSurfaces.TryResolveResource(resId, out var movie) && movie != null)
            return (movie.Image, movie.Name, movie.AssetId, true);
        // Movie payloads use the same .AGF extension as still images. Do not misclassify the MPEG program
        // stream before its first frame or during the cleanup frame after its surface binding is detached.
        // Packed catalog identity is immutable, so a resource which entered the typed movie path remains
        // a movie even when it has no live playback.
        if (_movieSurfaces.IsKnownMovieResource(resId)) return null;
        var asset = _res.ResolveTexture(resId);
        var image = asset != null ? Decode(asset) : null;
        return asset != null && image != null ? (image, asset.Name, asset.PackedId, false) : null;
    }

    public (RgbaImage Image, string Name, int AssetId, bool IsDynamic)? ResolveSurfaceTexture(
        int surfaceSlot, long fallbackResourceId)
    {
        lock (_movieMaskLock)
            if (_movieMaskPlaybackBySurface.ContainsKey(surfaceSlot))
                lock (_imageLock)
                    if (_surfaceImages.TryGetValue(surfaceSlot, out var masked))
                        return (masked, $"<movie-mask:{surfaceSlot}>",
                            int.MinValue + surfaceSlot, true);
        if (_movieSurfaces.TryResolveSurface(surfaceSlot, out var movie) && movie != null)
            return (movie.Image, movie.Name, movie.AssetId, true);
        if (_movieSurfaces.IsBound(surfaceSlot)) return null;
        lock (_imageLock)
            if (_surfaceImages.TryGetValue(surfaceSlot, out var surface))
                return (surface, $"<surface:{surfaceSlot}>", int.MinValue + surfaceSlot, true);
        return fallbackResourceId != 0 ? ResolveResIdTexture(fallbackResourceId) : null;
    }

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

    public void ReleaseSurface(int slot)
    {
        lock (_screenTransitionLock) _renderTargetSnapshots.Remove(slot);
        MovieSurfaceRelease movieRelease = _movieSurfaces.ReleaseIfCompleted(slot);
        if (movieRelease.Kind == MovieSurfaceReleaseKind.Active)
            return; // Static surface setup before 0x21c must not evict an active movie playback.
        lock (_imageLock)
        {
            _surfaceImages.Remove(slot);
            _surfaceColorKeys.Remove(slot);
        }
        lock (_textLock)
        {
            _surfaceResources.Remove(slot);
            foreach (int layoutSlot in _retainedTextLayouts
                         .Where(pair => pair.Value.Binding.SourceSurfaceSlot == slot)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _retainedTextLayouts.Remove(layoutSlot);
                _retainedHistoryLayouts.Remove(layoutSlot);
            }
        }
        _slotDims.Remove(slot);
        if (movieRelease.Kind == MovieSurfaceReleaseKind.Released)
        {
            var binding = movieRelease.Binding;
            AbandonMovieMaskPlayback(binding.PlaybackId);
            ForgetMovieMaskSurface(binding.SurfaceSlot, binding.PlaybackId);
            _timeline?.Event("movie-stop", new()
            {
                ["resource"] = binding.ResourceId,
                ["playback"] = binding.PlaybackId,
                ["surface"] = slot,
            });
            _main.CallDeferred("StopMovie", binding.PlaybackId);
        }
    }

    public void ClearRenderTarget(int surfaceSlot)
    {
        if (surfaceSlot < 0)
        {
            lock (_backbufferRangeLock) _backbufferClearPending = true;
        }
        else
        {
            lock (_imageLock)
            {
                if (_surfaceImages.TryGetValue(surfaceSlot, out var image))
                    System.Array.Clear(image.Pixels);
                else if (_slotDims.TryGetValue(surfaceSlot, out var dimensions)
                         && dimensions.W > 0 && dimensions.H > 0)
                    _surfaceImages[surfaceSlot] = new RgbaImage(
                        dimensions.W, dimensions.H,
                        new byte[checked(dimensions.W * dimensions.H * 4)]);
            }
        }
        _timeline?.Event("render-target-clear", new() { ["surface"] = surfaceSlot });
    }

    private void PublishObjectRangeToSurface(
        GfxState gfx, long firstHandle, long count,
        IReadOnlyList<RenderObject>? sampled = null)
    {
        int targetSlot = gfx.CurrentRenderTargetSlot;
        if (targetSlot < 0 || !_slotDims.TryGetValue(targetSlot, out var dimensions)
            || dimensions.W <= 0 || dimensions.H <= 0)
            return;

        RgbaImage destination;
        lock (_imageLock)
            destination = _surfaceImages.TryGetValue(targetSlot, out var current)
                ? new RgbaImage(current.Width, current.Height, (byte[])current.Pixels.Clone())
                : new RgbaImage(dimensions.W, dimensions.H,
                    new byte[checked(dimensions.W * dimensions.H * 4)]);

        IReadOnlyList<RenderObject> visible = sampled ?? gfx.SnapshotVisibleObjects(_clock.NowMs);
        int rendered = RetainedSurfaceRasterizer.CompositeRange(
            destination, visible, firstHandle, count,
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

        lock (_imageLock) _surfaceImages[targetSlot] = destination;
        IReadOnlyList<RenderObject> retained = visible
            .Where(item => item.Handle >= firstHandle && item.Handle - firstHandle < count)
            .ToArray();
        lock (_screenTransitionLock) _renderTargetSnapshots[targetSlot] = retained;
        _timeline?.Event("render-target-publish", new()
        {
            ["surface"] = targetSlot,
            ["first"] = firstHandle,
            ["count"] = count,
            ["objects"] = retained.Count,
            ["rendered"] = rendered,
        });
    }

    public void ReleaseSurfaceRange(int firstSlot, int count)
    {
        IReadOnlyList<MovieSurfaceBinding> stoppedMovies = _movieSurfaces.ReleaseRange(firstSlot, count);
        int end = checked(firstSlot + count);
        lock (_screenTransitionLock)
            for (int slot = firstSlot; slot < end; slot++) _renderTargetSnapshots.Remove(slot);
        lock (_imageLock)
        {
            for (int slot = firstSlot; slot < end; slot++)
            {
                _surfaceImages.Remove(slot);
                _surfaceColorKeys.Remove(slot);
                _slotDims.Remove(slot);
            }
        }
        lock (_textLock)
        {
            for (int slot = firstSlot; slot < end; slot++)
            {
                _surfaceResources.Remove(slot);
            }
            foreach (int layoutSlot in _retainedTextLayouts
                         .Where(pair =>
                             pair.Value.Binding.SourceSurfaceSlot >= firstSlot
                             && pair.Value.Binding.SourceSurfaceSlot < end)
                         .Select(pair => pair.Key)
                         .ToArray())
                _retainedTextLayouts.Remove(layoutSlot);
            _retainedHistoryLayouts.RemoveWhere(layoutSlot =>
                !_retainedTextLayouts.ContainsKey(layoutSlot));
        }
        foreach (MovieSurfaceBinding binding in stoppedMovies)
        {
            AbandonMovieMaskPlayback(binding.PlaybackId);
            ForgetMovieMaskSurface(binding.SurfaceSlot, binding.PlaybackId);
            _timeline?.Event("movie-stop", new()
            {
                ["resource"] = binding.ResourceId,
                ["playback"] = binding.PlaybackId,
                ["range_release"] = true,
            });
            _main.CallDeferred("StopMovie", binding.PlaybackId);
        }
        _timeline?.Event("surface-range-release", new() { ["first"] = firstSlot, ["count"] = count });
    }

    // Main-thread decoder handoff. Replacing the newest frame mirrors the native texture renderer's
    // sample callback: the retained object keeps its surface binding while only the surface pixels change.
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

    private RgbaImage? Decode(AssetEntry asset)
    {
        lock (_imageLock)
        {
            if (_images.TryGetValue(asset.PackedId, out var cached)) return cached;
            try { return _images[asset.PackedId] = _res.DecodeTexture(asset); }
            catch (System.Exception e)
            {
                Godot.GD.Print($"AGF decode failed {asset.Name}: {e.Message}");
                _images[asset.PackedId] = null;
                return null;
            }
        }
    }

    // ---- audio ops (OGG plays natively in Godot) ----
    // BGM is addressed by direct name (BGM{id:D3}.OGG); voice uses the universal packed catalog.
    public void PlayBgm(long id)
        => DispatchBgm(id, 1, false);

    public void RestartBgm(long id, int startMode)
        => DispatchBgm(id, startMode, true);

    private void DispatchBgm(long id, int startMode, bool forceRestart)
    {
        var asset = _res.ResolveBgm(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("bgm", new()
        {
            ["id"] = id,
            ["file"] = audio?.Name,
            ["start_mode"] = startMode,
            ["force_restart"] = forceRestart,
        });
        if (audio == null) return;
        if (forceRestart)
            _main.CallDeferred("RestartBgm", audio.Bytes, audio.Name, startMode);
        else
            _main.CallDeferred("PlayBgm", audio.Bytes, audio.Name);
    }

    public void StopBgm()
    {
        _timeline?.Event("bgm-stop", new());
        _main.CallDeferred("StopBgm");
    }

    public void PlayVoice(long id) => PlayVoice(id, 0);

    public void PlayVoice(long id, int playbackVariant)
    {
        var asset = _res.ResolveVoice(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("voice", new() { ["id"] = id, ["file"] = audio?.Name,
            ["playback_variant"] = playbackVariant });
        if (audio == null) return;
        bool queuedForSkip;
        bool firstQueued = false;
        lock (_messageSkipLock)
        {
            queuedForSkip = _messageSkipActive;
            if (queuedForSkip)
            {
                firstQueued = _queuedSkippedVoice == null;
                _queuedSkippedVoice = (audio, playbackVariant);
            }
        }
        if (queuedForSkip)
        {
            if (firstQueued) _main.CallDeferred("StopVoiceForMessageSkip");
            return;
        }
        DispatchVoice(audio, playbackVariant);
    }

    private void DispatchVoice(AudioPayload audio, int playbackVariant)
    {
        int generation = _main.QueueVoicePlayback();
        bool duckBgm = (System.Threading.Volatile.Read(ref _voiceBgmDuckControl) & 1) == 0;
        // Godot's stream player has no matching AGE start-mode control. Retain the native
        // variant through dispatch/timeline so that distinction is not erased at the VM seam.
        _main.CallDeferred("PlayVoice", audio.Bytes, audio.Name, generation, duckBgm, 50);
    }

    public void SetVoiceBgmDuckControl(long flags)
    {
        System.Threading.Volatile.Write(ref _voiceBgmDuckControl, unchecked((int)flags));
        _timeline?.State("voice-bgm-duck-control", new() { ["flags"] = flags });
    }

    public void ScheduleVoicePlayback(long id, int playbackVariant, long delayMs)
    {
        var asset = _res.ResolveVoice(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("voice-scheduled", new()
        {
            ["id"] = id, ["file"] = audio?.Name, ["playback_variant"] = playbackVariant,
            ["delay_ms"] = unchecked((uint)delayMs),
        });
        lock (_scheduledVoiceLock)
            _scheduledVoice = audio == null
                ? null
                : (audio, playbackVariant, unchecked((uint)delayMs), null);
    }

    public void LoadSoundEffect(long resourceId, int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        var asset = _res.ResolveSoundEffect(resourceId);
        var audio = asset != null ? LoadAudio(asset) : null;
        _sfxNames[channel] = audio?.Name;
        _timeline?.Event("sfx-load", new() { ["resource"] = resourceId, ["channel"] = channel,
            ["file"] = audio?.Name });
        if (audio != null) _main.CallDeferred("LoadSoundEffect", audio.Bytes, audio.Name, channel);
    }

    public void StartSoundEffect(int channel) => StartSoundEffect(channel, 0);

    public void StartSoundEffect(int channel, int startMode)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        _timeline?.Event("sfx-start", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["file"] = _sfxNames[channel] });
        _main.CallDeferred("StartSoundEffect", channel, startMode);
    }

    public void ScheduleSoundEffectStart(int channel, int startMode, long delayMs)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        long ms = System.Math.Clamp(delayMs, 0, 60_000);
        double realSeconds = ms / 1000.0 / System.Math.Max(0.05, _clock.Speed);
        _timeline?.Event("sfx-start-scheduled", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["delay_ms"] = ms, ["file"] = _sfxNames[channel] });
        _main.CallDeferred("ScheduleSoundEffectStart", channel, startMode, realSeconds);
    }

    public void ReleaseSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        _timeline?.Event("sfx-release", new() { ["channel"] = channel,
            ["file"] = _sfxNames[channel] });
        _sfxNames[channel] = null;
        _main.CallDeferred("ReleaseSoundEffect", channel);
    }

    private AudioPayload? LoadAudio(AssetEntry asset)
    {
        try { return _res.ReadAudio(asset); }
        catch (System.Exception e)
        {
            Godot.GD.Print($"audio read failed {asset.Name}: {e.Message}");
            return null;
        }
    }

    public void FadeBgm(int targetPercent, long durationMs)
    {
        long ms = System.Math.Clamp(durationMs, 0, 60_000);
        double realSeconds = ms / 1000.0 / System.Math.Max(0.05, _clock.Speed);
        _timeline?.State("bgm-fade", new() { ["target_percent"] = targetPercent, ["duration_ms"] = ms });
        _main.CallDeferred("FadeBgm", targetPercent, realSeconds);
        long deadline = _clock.NowMs + ms;
        IsSleeping = true;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            // Native parks the interpreter in its audio service while the main render loop continues.
            // Publish scene changes accumulated before the fade (notably GAMESTART -> SC0000's black
            // frame), then leave presentation ownership with the compositor for the timed wait.
            RequestSynchronizedPresentation();
            while (_clock.NowMs < deadline && !_stopping) _frameSignal.WaitOne(50);
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
            IsSleeping = false;
        }
        _timeline?.State("running", new() { ["bgm_fade_complete"] = true });
    }

    public void ApplyAudioVolume(int category, int basisPoints)
    {
        _timeline?.State("audio-volume", new()
        {
            ["category"] = category,
            ["basis_points"] = basisPoints,
        });
        _main.CallDeferred("ApplyAudioVolume", category, basisPoints);
    }

    public void ApplyAudioRouteEnabled(int category, bool enabled)
    {
        _timeline?.State("audio-route", new()
        {
            ["category"] = category,
            ["enabled"] = enabled,
        });
        _main.CallDeferred("ApplyAudioRouteEnabled", category, enabled);
    }
}

public sealed record GodotHostDiagnosticSnapshot(
    string CurrentScene, bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);

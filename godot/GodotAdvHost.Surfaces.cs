using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;

public sealed partial class GodotAdvHost
{
    private readonly object _imageLock = new();
    private readonly Dictionary<int, RgbaImage?> _images = new();        // packed catalog id -> decoded pixels
    // Mutable AGE surfaces are published by replacing immutable RgbaImage snapshots, so the compositor
    // can safely finish reading an old frame while the VM prepares a copied-rectangle update.
    private readonly Dictionary<int, RgbaImage> _surfaceImages = new();
    private readonly Dictionary<int, long> _surfaceColorKeys = new();
    private readonly Dictionary<int, long> _surfaceResources = new();    // surface slot -> packed catalog id
    // slot -> dimensions of the currently allocated surface. Slot 0 begins as the selected game's
    // logical canvas, but op 0x1fa releases it like any other slot; subsequent queries return 0x0.
    private readonly Dictionary<int, (int W, int H)> _slotDims = new();

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
        bool copied = copy.Width == copy.DestinationWidth && copy.Height == copy.DestinationHeight
            ? RgbaSurfaceOps.CopyRect(source, updated, copy.SourceX, copy.SourceY,
                                      copy.Width, copy.Height, copy.DestinationX, copy.DestinationY)
            : RgbaSurfaceOps.ScaleCopyRect(source, updated, copy.SourceX, copy.SourceY,
                                           copy.Width, copy.Height, copy.DestinationX, copy.DestinationY,
                                           copy.DestinationWidth, copy.DestinationHeight);
        if (copied)
        {
            lock (_imageLock) _surfaceImages[copy.DestinationSurface] = updated;
            System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        }
        _timeline?.Event("surface-copy", new()
        {
            ["source"] = copy.SourceSurface, ["source_x"] = copy.SourceX, ["source_y"] = copy.SourceY,
            ["w"] = copy.Width, ["h"] = copy.Height,
            ["source_w"] = copy.Width, ["source_h"] = copy.Height,
            ["destination"] = copy.DestinationSurface,
            ["destination_x"] = copy.DestinationX, ["destination_y"] = copy.DestinationY,
            ["destination_w"] = copy.DestinationWidth,
            ["destination_h"] = copy.DestinationHeight,
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
}

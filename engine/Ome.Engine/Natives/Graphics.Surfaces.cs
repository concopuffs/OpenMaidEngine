using System.Collections.Concurrent;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>A texture a surface or a resource resolves to: its pixels, a name for the logs, its asset id, and whether
/// its pixels are replaced while it is shown (a script-drawn surface, a movie).</summary>
public readonly record struct SurfaceTexture(RgbaImage Image, string Name, int AssetId, bool IsDynamic);

// The surface table (native's 1,000 surface records) and its pixels. Each op that native runs as one call is one
// method here, doing the record (GfxState) and the pixels in native's order.
public sealed partial class Graphics
{
    private readonly Dictionary<int, RgbaImage?> _images = new();        // packed catalog id -> decoded pixels
    // Mutable AGE surfaces are published by replacing immutable RgbaImage snapshots, so a renderer can finish reading
    // an old frame while the VM prepares a copied-rectangle update.
    private readonly Dictionary<int, RgbaImage> _surfaceImages = new();
    private readonly Dictionary<int, long> _surfaceColorKeys = new();
    private readonly Dictionary<int, long> _surfaceResources = new();    // surface slot -> packed catalog id
    // slot -> dimensions of the currently allocated surface. Slot 0 begins as the selected game's logical canvas, but
    // op 0x1fa releases it like any other slot; later queries return 0x0.
    private readonly ConcurrentDictionary<int, (int W, int H)> _slotDims = new();

    /// <summary><c>--gfx-log</c>: print set-texture/create-texture slot assignments.</summary>
    public bool TraceOps { get; set; }

    /// <summary>The CLI's <c>gfx</c> oracle: one line per surface op (create, load, release, size, draw).</summary>
    public Action<string>? OpTrace { get; set; }

    // ---- the ops ----

    /// <summary>Op 0x1f8: release the slot, then allocate a blank surface of the given size there.</summary>
    public void CreateSurface(int slot, int width, int height)
    {
        ReleaseSurfacePixels(slot);
        State.CreateSurface(slot);
        CreateSurfacePixels(slot, width, height);
        OpTrace?.Invoke($"create-texture slot={slot} {width}x{height}");
    }

    /// <summary>Ops 0x1f9 and 0x249: release the slot, then load a resource into it with a colour key. Op 0x249
    /// constructs native's mode-1 surface subclass (<paramref name="rawTextureMode"/>); the pixels are the same.</summary>
    public void LoadSurface(int slot, long resourceId, long colorKey, bool rawTextureMode = false)
    {
        ReleaseSurfacePixels(slot);
        State.SetSurface(slot, resourceId, colorKey, rawTextureMode);
        LoadSurfacePixels(resourceId, slot, colorKey);
    }

    /// <summary>Op 0x1fb: bind object <paramref name="handle"/> to the slot's surface with a source rectangle and a
    /// position. Nothing draws now: the frame draws the retained objects.</summary>
    public void DrawTexture(long handle, int slot, int srcX, int srcY, int width, int height, int dstX, int dstY)
    {
        State.BindDraw(handle, slot, srcX, srcY, width, height, dstX, dstY);
        if (OpTrace is { } trace)
        {
            long resourceId;
            bool loaded;
            loaded = _surfaceResources.TryGetValue(slot, out resourceId);
            string file = loaded ? _res?.ResolveTexture(resourceId)?.Name ?? "<none>" : "<none>";
            trace($"draw-texture slot={slot} src=({srcX},{srcY} {width}x{height}) dst=({dstX},{dstY}) file={file}");
        }
    }

    /// <summary>A numbered load's restore loop reopens a flagged record through gfx_surface_load_asset, without a
    /// release first.</summary>
    public void ReloadSurface(int slot, long resourceId, long colorKey)
    {
        State.SetSurface(slot, resourceId, colorKey);
        LoadSurfacePixels(resourceId, slot, colorKey);
    }

    /// <summary>Op 0x1fa: release one surface.</summary>
    public void ReleaseSurface(int slot)
    {
        ReleaseSurfacePixels(slot);
        State.ClearSurface(slot);
    }

    /// <summary>The release that ops 0x1f8, 0x1f9 and 0x249 run before they allocate.</summary>
    private void ReleaseSurfacePixels(int slot)
    {
        ReleaseSlotPixels(slot);
        OpTrace?.Invoke($"release-surface slot={slot}");
    }

    /// <summary>The release worker over a range (op 0x23d's fixed [42,1000), and the AutoFreeTex releases over all
    /// 1,000 slots).</summary>
    public void ReleaseSurfaceRange(int firstSlot, int count)
    {
        State.ReleaseSurfaceRange(firstSlot, count);
        ReleaseSurfacePixelRange(firstSlot, count);
        OpTrace?.Invoke($"release-surface-range first={firstSlot} count={count}");
    }

    /// <summary>Op 0x208: the size of the slot's surface, 0 × 0 when none.</summary>
    public (int Width, int Height) SurfaceSize(int slot)
    {
        var size = _slotDims.TryGetValue(slot, out var d) ? (d.W, d.H) : (0, 0);
        OpTrace?.Invoke($"get-tex-size slot={slot} -> {size.Item1}x{size.Item2}");
        return size;
    }

    /// <summary>Op 0x20b: a clipped alpha/RGB fill of a surface.</summary>
    public void FillSurfaceRect(SurfaceRectFill fill)
    {
        State.MarkSurfaceContentWritten(fill.SurfaceSlot);
        RgbaImage? destination = SurfacePixels(fill.SurfaceSlot);
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
                _surfaceImages[fill.SurfaceSlot] = updated;
                RequestPresentation();
            }
        }
        _timeline?.Event("surface-fill", new()
        {
            ["surface"] = fill.SurfaceSlot, ["x"] = fill.X, ["y"] = fill.Y,
            ["w"] = fill.Width, ["h"] = fill.Height, ["alpha"] = fill.Alpha, ["rgb"] = fill.Rgb,
        });
    }

    /// <summary>Ops 0x207 and 0x32: a paired-clipped (0x207) or scaled (0x32) copy between surfaces.</summary>
    public void CopySurfaceRect(SurfaceRectCopy copy)
    {
        State.MarkSurfaceContentWritten(copy.DestinationSurface);
        RgbaImage? source = SurfacePixels(copy.SourceSurface);
        RgbaImage? destination = SurfacePixels(copy.DestinationSurface);
        if (destination == null && _slotDims.TryGetValue(copy.DestinationSurface, out var dimensions)
            && dimensions.W >= 0 && dimensions.H >= 0)
            destination = new RgbaImage(dimensions.W, dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
        if (source == null || destination == null)
        {
            System.Console.Error.WriteLine(
                $"surface copy unresolved source={copy.SourceSurface} destination={copy.DestinationSurface}");
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
            _surfaceImages[copy.DestinationSurface] = updated;
            RequestPresentation();
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

    /// <summary>Op 0x20e: clear the selected render target's pixels; the backbuffer (-1) clears at its next
    /// publication.</summary>
    public void ClearRenderTarget()
    {
        int surfaceSlot = State.CurrentRenderTargetSlot;
        if (surfaceSlot < 0)
        {
            _scene.BackbufferClearPending = true;
        }
        else
        {
            // A fresh buffer rather than an in-place clear, as every other surface write: the GPU texture cache
            // sees a changed dynamic surface by its pixel array's identity, and the front end may be drawing a frame
            // that holds the published one.
            if (_surfaceImages.TryGetValue(surfaceSlot, out var image))
                _surfaceImages[surfaceSlot] = new RgbaImage(image.Width, image.Height, new byte[image.Pixels.Length]);
            else if (_slotDims.TryGetValue(surfaceSlot, out var dimensions)
                     && dimensions.W > 0 && dimensions.H > 0)
                _surfaceImages[surfaceSlot] = new RgbaImage(
                    dimensions.W, dimensions.H,
                    new byte[checked(dimensions.W * dimensions.H * 4)]);
        }
        _timeline?.Event("render-target-clear", new() { ["surface"] = surfaceSlot });
    }

    // ---- pixels ----

    private void CreateSurfacePixels(int slot, int width, int height)
    {
        _surfaceResources.Remove(slot);
        int safeWidth = System.Math.Max(0, width);
        int safeHeight = System.Math.Max(0, height);
        _surfaceImages[slot] = new RgbaImage(safeWidth, safeHeight,
            new byte[checked(safeWidth * safeHeight * 4)]);
        _surfaceColorKeys.Remove(slot);
        _slotDims[slot] = (safeWidth, safeHeight);
        if (TraceOps) _print($"[op] create-texture slot={slot} {width}x{height}");
    }

    // The catalog image is decoded synchronously on the VM thread, so a size queried at once sees its real size.
    private void LoadSurfacePixels(long resourceId, int slot, long colorKey)
    {
        _surfaceImages.Remove(slot);
        _surfaceColorKeys[slot] = colorKey;
        _surfaceResources[slot] = resourceId;
        var asset = _res?.ResolveTexture(resourceId);
        var image = asset != null ? Decode(asset) : null;
        _slotDims[slot] = image != null ? (image.Width, image.Height) : (0, 0);
        if (TraceOps) _print($"[op] set-texture slot={slot} resId=0x{resourceId:x} -> {(asset?.Name ?? "<none>")}");
        OpTrace?.Invoke($"set-texture slot={slot} res=0x{resourceId:x} -> {(asset?.Name ?? "<unresolved>")}"
                        + (image == null ? " [NO AGF]" : ""));
    }

    private void ReleaseSlotPixels(int slot)
    {
        _renderTargetSnapshots.Remove(slot);
        // Native deletes the slot's movie object first, finished or not.
        foreach (ISurfaceListener listener in _listeners) listener.SurfaceReleasing(slot);
        _surfaceImages.Remove(slot);
        _surfaceColorKeys.Remove(slot);
        _surfaceResources.Remove(slot);
        _slotDims.TryRemove(slot, out _);
        foreach (ISurfaceListener listener in _listeners) listener.SurfaceReleased(slot);
    }

    private void ReleaseSurfacePixelRange(int firstSlot, int count)
    {
        foreach (ISurfaceListener listener in _listeners) listener.SurfaceRangeReleasing(firstSlot, count);
        int end = checked(firstSlot + count);
        for (int slot = firstSlot; slot < end; slot++) _renderTargetSnapshots.Remove(slot);
        for (int slot = firstSlot; slot < end; slot++)
        {
            _surfaceImages.Remove(slot);
            _surfaceColorKeys.Remove(slot);
            _slotDims.TryRemove(slot, out _);
        }
        for (int slot = firstSlot; slot < end; slot++) _surfaceResources.Remove(slot);
        foreach (ISurfaceListener listener in _listeners) listener.SurfaceRangeReleased(firstSlot, count);
        _timeline?.Event("surface-range-release", new() { ["first"] = firstSlot, ["count"] = count });
    }

    /// <summary>A stable copy of a surface's pixels, or null when it has none (op 0x1ae's thumbnail).</summary>
    public RgbaImage? CaptureSurfacePixels(int slot)
    {
        RgbaImage? image = SurfacePixels(slot);
        return image == null
            ? null
            : new RgbaImage(image.Width, image.Height, (byte[])image.Pixels.Clone());
    }

    /// <summary>Replace a surface's pixels from outside (op 0x1af's thumbnail): the surface forgets its resource and
    /// colour key, and the layouts retained on it go.</summary>
    public bool ReplaceSurfacePixels(int slot, RgbaImage image)
    {
        if (image.Width <= 0 || image.Height <= 0
            || image.Pixels.Length != checked(image.Width * image.Height * 4))
            return false;
        _surfaceImages[slot] =
            new RgbaImage(image.Width, image.Height, (byte[])image.Pixels.Clone());
        _surfaceColorKeys.Remove(slot);
        _surfaceResources.Remove(slot);
        foreach (ISurfaceListener listener in _listeners) listener.SurfaceReplaced(slot);
        _slotDims[slot] = (image.Width, image.Height);
        return true;
    }

    /// <summary>A surface's current pixels: its own, or its resource's with the colour key applied; null when it has
    /// neither.</summary>
    public RgbaImage? SurfacePixels(int slot)
    {
        if (_surfaceImages.TryGetValue(slot, out var mutable)) return mutable;
        long resourceId;
        if (!_surfaceResources.TryGetValue(slot, out resourceId)) return null;
        var resolved = ResolveResIdTexture(resourceId);
        if (resolved == null) return null;
        long colorKey;
        colorKey = _surfaceColorKeys.GetValueOrDefault(slot, -1);
        return RgbaSurfaceOps.WithColorKey(resolved.Value.Image, colorKey);
    }

    /// <summary>Publish new pixels for a surface (text drawn into it, a movie mask's capture). The surface keeps its
    /// resource and colour key; <paramref name="updateSize"/> also records the image's size as the surface's.</summary>
    public void PublishSurfacePixels(int slot, RgbaImage image, bool updateSize = true)
    {
        _surfaceImages[slot] = image;
        if (updateSize) _slotDims[slot] = (image.Width, image.Height);
    }

    /// <summary>Take a surface's own pixels away (a movie starting on it keeps them until its first sample).</summary>
    public RgbaImage? TakeSurfacePixels(int slot)
    {
        return _surfaceImages.Remove(slot, out RgbaImage? image) ? image : null;
    }

    /// <summary>The surface draws without a colour key (a movie bound to it).</summary>
    public void ForgetSurfaceColorKey(int slot)
    {
        _surfaceColorKeys.Remove(slot);
    }

    /// <summary>Record a surface's size without pixels (a movie's before its first frame).</summary>
    public void SetSurfaceSize(int slot, int width, int height) => _slotDims[slot] = (width, height);

    /// <summary>Forget a surface's size (a movie that failed to start).</summary>
    public void ForgetSurfaceSize(int slot) => _slotDims.TryRemove(slot, out _);

    /// <summary>The surface's size, if it has one.</summary>
    public bool TryGetSurfaceSize(int slot, out (int W, int H) size) => _slotDims.TryGetValue(slot, out size);

    // ---- texture resolution ----

    /// <summary>Resolve a resource through universal packed addressing and decode it from the loose-first asset
    /// store; a movie resource resolves to its playing frame, or to nothing.</summary>
    public SurfaceTexture? ResolveResIdTexture(long resId)
    {
        if (_movies.TryResolveResource(resId, out var movie) && movie != null)
            return new SurfaceTexture(movie.Image, movie.Name, movie.AssetId, true);
        // Movie payloads use the same .AGF extension as still images. Do not misclassify the MPEG program stream
        // before its first frame or during the cleanup frame after its surface binding is detached. Packed catalog
        // identity is immutable, so a resource which entered the typed movie path remains a movie even when it has
        // no live playback.
        if (_movies.IsKnownMovieResource(resId)) return null;
        var asset = _res?.ResolveTexture(resId);
        var image = asset != null ? Decode(asset) : null;
        return asset != null && image != null ? new SurfaceTexture(image, asset.Name, asset.PackedId, false) : null;
    }

    /// <summary>What a surface shows: a movie mask's capture, a movie's frame, its own pixels, or its resource.
    /// A movie bound without a frame shows nothing.</summary>
    public SurfaceTexture? ResolveSurfaceTexture(int surfaceSlot, long fallbackResourceId)
    {
        if (_movies.IsMaskedSurface(surfaceSlot))
            if (_surfaceImages.TryGetValue(surfaceSlot, out var masked))
                return new SurfaceTexture(masked, $"<movie-mask:{surfaceSlot}>", int.MinValue + surfaceSlot, true);
        if (_movies.TryResolveSurface(surfaceSlot, out var movie) && movie != null)
            return new SurfaceTexture(movie.Image, movie.Name, movie.AssetId, true);
        if (_movies.IsBound(surfaceSlot)) return null;
        if (_surfaceImages.TryGetValue(surfaceSlot, out var surface))
            return new SurfaceTexture(surface, $"<surface:{surfaceSlot}>", int.MinValue + surfaceSlot, true);
        return fallbackResourceId != 0 ? ResolveResIdTexture(fallbackResourceId) : null;
    }

    /// <summary>The packed resource id surface <paramref name="slot"/> loaded, or null when it loaded none (released,
    /// created blank, or given pixels from outside).</summary>
    public long? SurfaceResource(int slot)
    {
        return _surfaceResources.TryGetValue(slot, out long resourceId) ? resourceId : null;
    }

    /// <summary>The slots holding a surface (pixels or a loaded resource), for the timeline.</summary>
    public int[] LiveSurfaceSlots()
    {
        var slots = new SortedSet<int>();
        slots.UnionWith(_surfaceImages.Keys);
        slots.UnionWith(_surfaceResources.Keys);
        return slots.ToArray();
    }

    /// <summary>Drops the decoded images, so each is read again when next drawn: after a language switch, its
    /// language assets apply from the next load (localization spec §12).</summary>
    public void ForgetDecodedImages()
    {
        _images.Clear();
    }

    private RgbaImage? Decode(AssetEntry asset)
    {
        if (_images.TryGetValue(asset.PackedId, out var cached)) return cached;
        try { return _images[asset.PackedId] = _res!.DecodeTexture(asset); }
        catch (System.Exception e)
        {
            _print($"AGF decode failed {asset.Name}: {e.Message}");
            _images[asset.PackedId] = null;
            return null;
        }
    }
}

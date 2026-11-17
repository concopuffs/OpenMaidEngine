using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Hosting;

/// <summary>
/// The reference renderer (engine layering spec §6.3, part 2): draws a frame description into one canvas-sized RGBA
/// buffer that the front end shows. It defines what each frame item means, and the GPU renderer must match it within
/// the stated tolerance. Decoded catalog pixels, with their colour key baked in, are cached by identity, since this
/// runs every frame it draws; a dynamic surface's are never cached.
/// </summary>
/// <remarks>The front end draws with it on its thread: a screen transition, a diagnostic log, a frame the GPU renderer
/// declines, or a run with <c>--render-backend software</c>. Normal play draws on the GPU. A render-target publication
/// draws with its rules, uncached (<see cref="DrawItems(RgbaImage, IReadOnlyList{FrameItem})"/>).</remarks>
public sealed class ReferenceRenderer
{
    private readonly int _width;
    private readonly int _height;
    // One managed target for the entire frame. Items draw into it in place; only the finished frame crosses to the
    // front end, which avoids a read-back per item.
    private readonly byte[] _pixels;
    private byte[] _transitionSourcePixels = [];
    // A static asset's pixels with its colour key baked in. Source: the decoded pixels the entry was prepared from,
    // held weakly so the cache does not keep an image the graphics native has let go. A static asset's decoded image is
    // one instance until the native drops it (past its budget, or a language switch), so another instance under the
    // same key is that file decoded again, or the newer one.
    private sealed record CachedPixels(int Width, int Height, byte[] Rgba, WeakReference<byte[]> Source)
    {
        public bool IsFrom(byte[] pixels) => Source.TryGetTarget(out byte[]? source) && ReferenceEquals(source, pixels);
    }

    /// <summary>The bytes of colour-keyed pixels the renderer keeps between frames.</summary>
    public const long PreparedPixelBudgetBytes = 64L << 20;

    private readonly ByteBudgetCache<(int AssetId, long Key), CachedPixels> _pixelCache = new(PreparedPixelBudgetBytes);

    public ReferenceRenderer(Sys4LogicalCanvas canvas)
    {
        _width = canvas.Width;
        _height = canvas.Height;
        _pixels = new byte[canvas.RgbaByteCount];
    }

    /// <summary>The drawn frame: <c>Width * Height</c> RGBA8 pixels, rows top down. Valid after <see cref="Draw"/>
    /// until the next call.</summary>
    public byte[] Pixels => _pixels;
    public int Width => _width;
    public int Height => _height;

    /// <summary><c>--perf-log</c>'s per-frame counters.</summary>
    public PerformanceFrameLog? Perf { get; init; }

    /// <summary>Drops the prepared pixels of every catalog image: after a language switch, its language assets apply
    /// (localization spec §12).</summary>
    public void ForgetPreparedPixels() => _pixelCache.Clear();

    /// <summary>Draw <paramref name="frame"/> into <see cref="Pixels"/>. A frame that starts clean clears to black and
    /// fills its background colour; a whole-screen transition draws its source, then blends its flattened target
    /// over it.</summary>
    public void Draw(FrameDescription frame)
    {
        _pixelCache.BeginFrame();
        long phase = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        FrameScreenTransition? screen = frame.ScreenTransition;
        if (screen != null || !frame.PreserveExistingPixels)
        {
            System.Array.Clear(_pixels);
            if (frame.BackgroundRgb != 0)
                FillQuad(0, 0, _width, _height, frame.BackgroundRgb, 1f);
        }
        Perf?.RecordClear(PerformanceFrameLog.Timestamp() - phase);
        long allocated = Perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        if (screen != null)
        {
            DrawItems(screen.Source);
            if (screen.Alpha != 0) DrawFlattenedTransitionTarget(screen);
        }
        else DrawItems(frame.Items);
        Perf?.RecordCompositeAllocation(PerformanceFrameLog.AllocatedBytes() - allocated);
        _pixelCache.Trim();
    }

    /// <summary>Draw <paramref name="items"/> into <paramref name="destination"/> by this renderer's rules, with nothing
    /// cached: a render-target publication.</summary>
    public static void DrawItems(RgbaImage destination, IReadOnlyList<FrameItem> items)
    {
        for (int index = 0; index < items.Count; index++)
        {
            FrameItem item = items[index];
            if (item.Kind == FrameItemKind.Fill)
            {
                SoftwareAffineRasterizer.FillRgba(
                    destination.Pixels, destination.Width, destination.Height,
                    item.Width, item.Height, item.Matrix, item.Tint, item.Opacity);
                continue;
            }
            if (item.Width <= 0 || item.Height <= 0) continue;
            RgbaImage source = RgbaSurfaceOps.WithColorKey(item.Pixels!, item.ColorKey);
            int width = System.Math.Min(item.Width, source.Width - item.SourceX);
            int height = System.Math.Min(item.Height, source.Height - item.SourceY);
            if (width <= 0 || height <= 0) continue;
            SoftwareAffineRasterizer.BlitRgba(
                destination.Pixels, destination.Width, destination.Height,
                source.Pixels, source.Width, source.Height,
                item.SourceX, item.SourceY, width, height, item.Matrix,
                item.Tint, item.TintStrength / 255f, item.Opacity, item.MultiplyTint, item.Blend);
        }
    }

    // Native screen_transition_tick mode 4: the target surface, flattened over the overlay colour, blends over the
    // source already in the buffer at the transition's alpha.
    private void DrawFlattenedTransitionTarget(FrameScreenTransition screen)
    {
        if (_transitionSourcePixels.Length != _pixels.Length)
            _transitionSourcePixels = new byte[_pixels.Length];
        System.Buffer.BlockCopy(_pixels, 0, _transitionSourcePixels, 0, _pixels.Length);
        System.Array.Clear(_pixels);
        FillQuad(0, 0, _width, _height, screen.OverlayRgb, 1f);
        DrawItems(screen.Target);
        if (screen.Alpha < 256)
            LegacyScreenTransitionComposite.BlendTargetOverSource(_pixels, _transitionSourcePixels, screen.Alpha);
    }

    private void DrawItems(List<FrameItem> items)
    {
        for (int index = 0; index < items.Count; index++)
        {
            FrameItem item = items[index];
            if (item.Kind == FrameItemKind.Fill)
                FillAffineQuad(item.Width, item.Height, item.Matrix, item.Tint, item.Opacity);
            else
                BlitLayer(item);
        }
    }

    // Blit one texture item's rectangle. Static source pixels with a colour key are cached per (assetId, colorKey): on
    // first use, texels matching the key are made transparent (native bakes the key at load — engine-re.md §Blend). Mode 0 uses
    // the tint strength to move texel RGB toward the tint. Mode 1 uses packed RGB modulation and SRCALPHA/ONE additive
    // composition; black source pixels therefore contribute nothing.
    private void BlitLayer(in FrameItem item)
    {
        // Native gfx_object_blit_d3d9 clips the explicit source rectangle and returns without drawing when
        // right<=left or bottom<=top. FIELD deliberately creates zero-area prototype objects from SO005;
        // expanding those dimensions to the full texture leaks the entire spritesheet onto the map.
        if (item.Width <= 0 || item.Height <= 0) return;
        long sourcePrepStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long sourcePrepAllocated = Perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        RgbaImage decoded = item.Pixels!;
        int sourceWidth, sourceHeight;
        byte[] sourcePixels;
        if (item.Dynamic || !BlendMath.HasColorKey(item.ColorKey))
        {
            // Pixels without a key are drawn as decoded, so nothing is kept for them. Decoder samples replace the
            // pixels of one retained surface: never enter them in the static cache, and clone only when applying a
            // key so the decoder-owned newest-frame buffer remains untouched.
            sourceWidth = decoded.Width;
            sourceHeight = decoded.Height;
            sourcePixels = decoded.Pixels;
            if (BlendMath.HasColorKey(item.ColorKey))
            {
                sourcePixels = (byte[])sourcePixels.Clone();
                BakeColorKey(sourcePixels, item.ColorKey);
            }
        }
        else
        {
            var cacheKey = (item.AssetId, item.ColorKey);
            if (!_pixelCache.TryGet(cacheKey, out var cached) || !cached.IsFrom(decoded.Pixels))
            {
                byte[] pixels = (byte[])decoded.Pixels.Clone();
                BakeColorKey(pixels, item.ColorKey);
                cached = new CachedPixels(decoded.Width, decoded.Height, pixels, new WeakReference<byte[]>(decoded.Pixels));
                _pixelCache.Set(cacheKey, cached, pixels.LongLength);
            }
            sourceWidth = cached.Width;
            sourceHeight = cached.Height;
            sourcePixels = cached.Rgba;
        }

        int width = System.Math.Min(item.Width, sourceWidth - item.SourceX);
        int height = System.Math.Min(item.Height, sourceHeight - item.SourceY);
        Perf?.RecordSourcePrep(PerformanceFrameLog.Timestamp() - sourcePrepStarted);
        Perf?.RecordSourcePrepAllocation(PerformanceFrameLog.AllocatedBytes() - sourcePrepAllocated);
        if (width <= 0 || height <= 0) return;
        long rasterStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        SoftwareAffineRasterizer.BlitRgba(
            _pixels, _width, _height, sourcePixels, sourceWidth, sourceHeight,
            item.SourceX, item.SourceY, width, height, item.Matrix, item.Tint, item.TintStrength / 255f,
            item.Opacity, item.MultiplyTint, item.Blend);
        Perf?.RecordRaster(width, height, item.Matrix, _width, _height, item.Dynamic, item.Blend,
                           PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    private void FillAffineQuad(int width, int height, Affine2D localToDest, long tint, float alpha)
    {
        long rasterStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        SoftwareAffineRasterizer.FillRgba(_pixels, _width, _height, width, height, localToDest, tint, alpha);
        Perf?.RecordRaster(width, height, localToDest, _width, _height, false, BlendKind.Alpha,
                           PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    // Alpha-blend a solid tint (0xRRGGBB) rectangle over the buffer: the background and the transition overlay.
    private void FillQuad(int dstX, int dstY, int w, int h, long tint, float alpha)
    {
        int ia = (int)(System.Math.Clamp(alpha, 0f, 1f) * 255);
        if (ia == 0) return;
        long rasterStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        int tr = (int)((tint >> 16) & 0xff), tg = (int)((tint >> 8) & 0xff), tb = (int)(tint & 0xff);
        byte[] dst = _pixels;
        int dw = _width, dh = _height;
        int x0 = System.Math.Max(0, -dstX), x1 = System.Math.Min(w, dw - dstX);
        int y0 = System.Math.Max(0, -dstY), y1 = System.Math.Min(h, dh - dstY);
        if (x1 <= x0 || y1 <= y0) return;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int dxp = dstX + x, dyp = dstY + y;
                int di = (dyp * dw + dxp) * 4;
                dst[di]     = (byte)((tr * ia + dst[di]     * (255 - ia)) / 255);
                dst[di + 1] = (byte)((tg * ia + dst[di + 1] * (255 - ia)) / 255);
                dst[di + 2] = (byte)((tb * ia + dst[di + 2] * (255 - ia)) / 255);
                dst[di + 3] = (byte)System.Math.Min(255, dst[di + 3] + ia);
            }
        Perf?.RecordFillLayer();
        Perf?.RecordRaster(w, h, new Affine2D(1, 0, 0, 1, dstX, dstY),
                           _width, _height, false, BlendKind.Alpha,
                           PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    // Make colour-key-matching texels transparent (native bakes the colour key at surface load).
    private static void BakeColorKey(byte[] px, long colorKey)
    {
        for (int i = 0; i < px.Length; i += 4)
            if (BlendMath.ColorKeyMatches(px[i], px[i + 1], px[i + 2], colorKey))
                px[i + 3] = 0;
    }
}

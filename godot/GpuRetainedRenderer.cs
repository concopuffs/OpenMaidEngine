using System;
using System.Collections.Generic;
using Godot;
using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Model;

/// <summary>
/// The GPU renderer (engine layering spec §6.3, part 3): draws frame descriptions with pooled Sprite2D canvas items.
/// Static decoded/color-key variants are uploaded once; the canvas items keep their GPU resources between
/// presentation boundaries. The reference renderer (Ome.Engine.Hosting.ReferenceRenderer) defines each item, and
/// this must match it within the stated tolerance. Objects sample as native's D3D9 sprite does (docs/engine-re.md,
/// "Object sampling"): bilinear for any transform other than an integer translation, clamped to the texture's edge
/// rather than the source region.
/// </summary>
internal sealed class GpuRetainedRenderer : IDisposable, IFrameRenderer
{
    internal readonly record struct FrameStats(int DrawItems, int TextureUploads, long TextureUploadTicks);

    private sealed class CachedTexture
    {
        public required ImageTexture Texture;
        public required int Width;
        public required int Height;
        // The pixels the upload came from, held weakly so the cache does not keep an image the graphics native has
        // let go. A static asset's decoded image is one instance until the native drops it (past its budget, or a
        // language switch), so another instance under the same key is that file decoded again, or the newer one.
        public required WeakReference<byte[]> SourcePixels;

        public bool IsFrom(byte[] pixels) => SourcePixels.TryGetTarget(out byte[]? source) && ReferenceEquals(source, pixels);
    }

    /// <summary>The bytes of uploaded textures kept between frames. Past it, those no frame has drawn lately go: a
    /// released surface's, a finished movie's, an asset off the screen, which is uploaded again when next drawn.</summary>
    public const long TextureBudgetBytes = 256L << 20;

    /// <summary>The tint materials kept between frames (each counts as one): an effect that steps a tint makes one per
    /// step.</summary>
    public const long MaterialBudget = 256;

    private readonly Node2D _stage;
    private readonly List<Sprite2D> _items = new(1024);
    private readonly ByteBudgetCache<(int AssetId, long ColorKey, long DynamicKey), CachedTexture> _textures =
        new(TextureBudgetBytes, (_, cached) => cached.Texture.Dispose());
    private readonly ByteBudgetCache<(long Tint, int Strength, BlendKind Blend), ShaderMaterial> _lerpMaterials =
        new(MaterialBudget, (_, material) => material.Dispose());
    private readonly CanvasItemMaterial _alphaMaterial = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Mix };
    private readonly CanvasItemMaterial _additiveMaterial = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private readonly ImageTexture _whiteTexture;
    private readonly Shader _lerpShader;
    private readonly Shader _additiveLerpShader;
    private PerformanceFrameLog? _perf;
    private int _used;
    private int _textureUploads;
    private long _textureUploadTicks;

    public bool Visible
    {
        get => _stage.Visible;
        set => _stage.Visible = value;
    }

    /// <summary><c>--perf-log</c>'s per-frame counters for the frames this draws.</summary>
    public PerformanceFrameLog? Perf { get; set; }

    /// <summary>
    /// Draw <paramref name="frame"/>, or decline it untouched: a whole-screen transition (it needs the flattened
    /// whole-screen target) goes to the reference renderer.
    /// </summary>
    public bool TryDraw(FrameDescription frame)
    {
        if (frame.ScreenTransition != null) return false;
        List<FrameItem> items = frame.Items;
        // A polygon item (AGE 4.38D's polygon objects) is not drawn here yet, so the reference renderer draws a
        // frame that holds one (docs/superpowers/specs/2026-11-19-polygon-objects-design.md, milestone 3).
        for (int index = 0; index < items.Count; index++)
            if (items[index].Kind == FrameItemKind.Polygon) return false;
        if (!frame.AppendLayers)
        {
            // An appended frame adds to the one before it, whose sprites still show that frame's textures.
            _textures.BeginFrame();
            _lerpMaterials.BeginFrame();
        }

        BeginFrame(frame.AppendLayers, Perf);
        // A frame that starts clean shows its background colour under the items, as the reference clears to it.
        if (!frame.PreserveExistingPixels && frame.BackgroundRgb != 0)
        {
            var screen = new Affine2D(1, 0, 0, 1, 0, 0);
            Perf?.RecordFillLayer();
            if (DrawFill(frame.Width, frame.Height, screen, frame.BackgroundRgb, 1f))
                Perf?.RecordGpuLayer(frame.Width, frame.Height, screen, frame.Width, frame.Height,
                                     dynamic: false, BlendKind.Alpha);
        }
        for (int index = 0; index < items.Count; index++)
        {
            FrameItem item = items[index];
            if (item.Kind == FrameItemKind.Fill)
            {
                if (DrawFill(item.Width, item.Height, item.Matrix, item.Tint, item.Opacity))
                    Perf?.RecordGpuLayer(item.Width, item.Height, item.Matrix, frame.Width, frame.Height,
                                         dynamic: false, BlendKind.Alpha);
            }
            else if (DrawTexture(item.Pixels!, item.AssetId, item.ColorKey,
                         item.SourceX, item.SourceY, item.Width, item.Height, item.Matrix, item.Tint,
                         item.TintStrength, item.Opacity, item.MultiplyTint, item.Dynamic, item.DynamicKey,
                         item.Blend))
                Perf?.RecordGpuLayer(item.Width, item.Height, item.Matrix, frame.Width, frame.Height,
                                     item.Dynamic, item.Blend);
        }
        FrameStats stats = EndFrame();
        Perf?.RecordGpu(stats.DrawItems, stats.TextureUploads, stats.TextureUploadTicks);
        _textures.Trim();
        _lerpMaterials.Trim();
        return true;
    }

    public GpuRetainedRenderer(Node parent)
    {
        _stage = new Node2D { Name = "GpuRetainedStage", Visible = false };
        parent.AddChild(_stage);

        var white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        white.SetData(1, 1, false, Image.Format.Rgba8, new byte[] { 255, 255, 255, 255 });
        _whiteTexture = ImageTexture.CreateFromImage(white);

        _lerpShader = new Shader { Code = LerpShaderCode("blend_mix") };
        _additiveLerpShader = new Shader { Code = LerpShaderCode("blend_add") };
    }

    // A LERP tint moves texel RGB toward the tint by the strength, then the item's opacity scales alpha and the item
    // blends source-over or, additive, SRCALPHA/ONE: the reference renderer's order (SoftwareAffineRasterizer).
    private static string LerpShaderCode(string blendMode) => $$"""
        shader_type canvas_item;
        render_mode {{blendMode}}, unshaded;
        uniform vec3 ome_tint = vec3(1.0);
        uniform float ome_strength = 0.0;
        void fragment() {
            vec4 source = texture(TEXTURE, UV);
            source.rgb = mix(source.rgb, ome_tint, ome_strength);
            COLOR = source * COLOR;
        }
        """;

    public void BeginFrame(bool preserveExistingLayers, PerformanceFrameLog? perf = null)
    {
        _perf = perf;
        if (!preserveExistingLayers) _used = 0;
        _textureUploads = 0;
        _textureUploadTicks = 0;
    }

    public bool DrawTexture(RgbaImage source, int assetId, long colorKey,
                            int srcX, int srcY, int width, int height,
                            Affine2D localToDest, long tint, int tintStrength,
                            float opacity, bool multiplyTint, bool dynamic, long dynamicKey, BlendKind blend)
    {
        if (width <= 0 || height <= 0 || opacity <= 0) return false;
        int clippedWidth = Math.Min(width, source.Width - srcX);
        int clippedHeight = Math.Min(height, source.Height - srcY);
        if (srcX < 0 || srcY < 0 || clippedWidth <= 0 || clippedHeight <= 0) return false;

        long started = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        var texture = ResolveTexture(source, assetId, colorKey, dynamic, dynamicKey);
        _perf?.RecordGpuTextureLookup(PerformanceFrameLog.Timestamp() - started,
            PerformanceFrameLog.AllocatedBytes() - allocated);
        started = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        allocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        var item = NextItem();
        item.Texture = texture;
        item.RegionEnabled = true;
        item.RegionRect = new Rect2(srcX, srcY, clippedWidth, clippedHeight);
        item.TextureFilter = SoftwareAffineRasterizer.IsIntegerTranslation(localToDest)
            ? CanvasItem.TextureFilterEnum.Nearest
            : CanvasItem.TextureFilterEnum.Linear;
        item.Transform = ToGodot(localToDest);
        item.Modulate = Modulation(tint, opacity, multiplyTint);
        item.Material = ResolveMaterial(tint, tintStrength, multiplyTint, blend);
        item.Visible = true;
        _perf?.RecordGpuSpriteUpdate(PerformanceFrameLog.Timestamp() - started,
            PerformanceFrameLog.AllocatedBytes() - allocated);
        return true;
    }

    public bool DrawFill(int width, int height, Affine2D localToDest, long tint, float opacity)
    {
        if (width <= 0 || height <= 0 || opacity <= 0) return false;
        long started = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        var item = NextItem();
        item.Texture = _whiteTexture;
        item.RegionEnabled = false;
        item.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        item.Transform = ToGodot(new Affine2D(
            localToDest.XX * width, localToDest.XY * width,
            localToDest.YX * height, localToDest.YY * height,
            localToDest.TX, localToDest.TY));
        item.Modulate = Modulation(tint, opacity, multiplyTint: true);
        item.Material = _alphaMaterial;
        item.Visible = true;
        _perf?.RecordGpuSpriteUpdate(PerformanceFrameLog.Timestamp() - started,
            PerformanceFrameLog.AllocatedBytes() - allocated);
        return true;
    }

    public FrameStats EndFrame()
    {
        for (int i = _used; i < _items.Count; i++) _items[i].Visible = false;
        return new FrameStats(_used, _textureUploads, _textureUploadTicks);
    }

    private Sprite2D NextItem()
    {
        if (_used == _items.Count)
        {
            var item = new Sprite2D
            {
                Centered = false,
                // D3D9's CLAMP addressing reads the texels just outside a source region, so do not clip there.
                RegionFilterClipEnabled = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                Visible = false,
            };
            _stage.AddChild(item);
            _items.Add(item);
        }
        var result = _items[_used];
        // Children are created in retained handle order, which is enough to preserve AGE z-order. Keep
        // their absolute Godot Z at the stage level so Main's later dialogue/wait controls remain above
        // the complete AGE canvas instead of being covered by a high-numbered full-screen sprite.
        result.ZIndex = 0;
        _used++;
        return result;
    }

    // Each upload's Image is disposed at once: the texture holds its own copy of the pixels, and a movie uploads one
    // per frame, so leaving them to the GC lets native image memory pile up.
    private Texture2D ResolveTexture(RgbaImage source, int assetId, long colorKey, bool dynamic, long dynamicKey)
    {
        // One movie resource may play concurrently on multiple surfaces. Static assets share one upload;
        // dynamic surfaces use their surface/playback identity so one frame cannot overwrite another. Either uploads
        // again when its pixels are another array: a dynamic surface's next sample, or a static asset decoded again
        // after a language switch (so a frame lowered before the switch cannot leave the old file cached).
        var key = (assetId, colorKey, dynamic ? dynamicKey : 0);
        if (!_textures.TryGet(key, out var cached))
        {
            byte[] pixels = PreparePixels(source.Pixels, colorKey);
            using var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, pixels);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            var texture = ImageTexture.CreateFromImage(image);
            _textureUploadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            _textureUploads++;
            cached = new CachedTexture
            {
                Texture = texture,
                Width = source.Width,
                Height = source.Height,
                SourcePixels = new WeakReference<byte[]>(source.Pixels),
            };
            _textures.Set(key, cached, TextureBytes(source));
        }
        else if (!cached.IsFrom(source.Pixels) || cached.Width != source.Width || cached.Height != source.Height)
        {
            byte[] pixels = PreparePixels(source.Pixels, colorKey);
            using var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, pixels);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            if (cached.Width == source.Width && cached.Height == source.Height)
                cached.Texture.Update(image);
            else
            {
                // A sprite that still shows the old texture keeps its own reference to it.
                ImageTexture old = cached.Texture;
                cached.Texture = ImageTexture.CreateFromImage(image);
                old.Dispose();
                cached.Width = source.Width;
                cached.Height = source.Height;
                _textures.Set(key, cached, TextureBytes(source));
            }
            _textureUploadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            _textureUploads++;
            cached.SourcePixels = new WeakReference<byte[]>(source.Pixels);
        }
        return cached.Texture;
    }

    private static long TextureBytes(RgbaImage source) => (long)source.Width * source.Height * 4;

    private static byte[] PreparePixels(byte[] source, long colorKey)
    {
        if (!BlendMath.HasColorKey(colorKey)) return source;
        byte[] pixels = (byte[])source.Clone();
        for (int i = 0; i < pixels.Length; i += 4)
            if (BlendMath.ColorKeyMatches(pixels[i], pixels[i + 1], pixels[i + 2], colorKey))
                pixels[i + 3] = 0;
        return pixels;
    }

    private Material ResolveMaterial(long tint, int tintStrength, bool multiplyTint, BlendKind blend)
    {
        if (!multiplyTint && tintStrength > 0)
        {
            var key = (tint & 0x00ff_ffff, tintStrength, blend);
            if (!_lerpMaterials.TryGet(key, out var material))
            {
                material = new ShaderMaterial
                {
                    Shader = blend == BlendKind.Additive ? _additiveLerpShader : _lerpShader,
                };
                material.SetShaderParameter("ome_tint", new Vector3(
                    ((tint >> 16) & 0xff) / 255f,
                    ((tint >> 8) & 0xff) / 255f,
                    (tint & 0xff) / 255f));
                material.SetShaderParameter("ome_strength", tintStrength / 255f);
                _lerpMaterials.Set(key, material, 1);
            }
            return material;
        }
        return blend == BlendKind.Additive ? _additiveMaterial : _alphaMaterial;
    }

    private static Color Modulation(long tint, float opacity, bool multiplyTint)
    {
        float r = 1, g = 1, b = 1;
        if (multiplyTint)
        {
            r = ((tint >> 16) & 0xff) / 255f;
            g = ((tint >> 8) & 0xff) / 255f;
            b = (tint & 0xff) / 255f;
        }
        return new Color(r, g, b, Math.Clamp(opacity, 0, 1));
    }

    private static Godot.Transform2D ToGodot(Affine2D value) => new(
        new Vector2((float)value.XX, (float)value.XY),
        new Vector2((float)value.YX, (float)value.YY),
        new Vector2((float)value.TX, (float)value.TY));

    /// <summary>Drops the uploads of static assets, so the next frame uploads what the asset store serves now: a
    /// language switch changes which file an asset id stands for (localization spec §12). A sprite that still shows
    /// one keeps its own reference until it is drawn again.</summary>
    public void ForgetStaticTextures() => _textures.RemoveWhere(key => key.DynamicKey == 0);

    public void Dispose()
    {
        _stage.Visible = false;
        foreach (var item in _items)
        {
            item.Texture = null;
            item.Material = null;
        }
        _whiteTexture.Dispose();
        _alphaMaterial.Dispose();
        _additiveMaterial.Dispose();
        _lerpMaterials.Clear();
        _textures.Clear();
        _lerpShader.Dispose();
        _additiveLerpShader.Dispose();
        _items.Clear();
    }
}

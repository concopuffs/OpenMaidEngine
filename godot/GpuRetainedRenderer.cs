using System;
using System.Collections.Generic;
using Godot;
using Age.Engine.Model;

/// <summary>
/// Godot-native presentation of AGE's sampled retained objects. Static decoded/color-key variants are
/// uploaded once; pooled Sprite2D canvas items retain their GPU resources between presentation boundaries.
/// GfxState remains the backend-neutral source of truth and the software compositor remains the oracle.
/// </summary>
internal sealed class GpuRetainedRenderer : IDisposable
{
    internal readonly record struct FrameStats(int DrawItems, int TextureUploads, long TextureUploadTicks);

    private sealed class CachedTexture
    {
        public required ImageTexture Texture;
        public required int Width;
        public required int Height;
        public byte[]? LastPixels;
    }

    private readonly Node2D _stage;
    private readonly List<Sprite2D> _items = new(1024);
    private readonly Dictionary<(int AssetId, long ColorKey, long DynamicKey), CachedTexture> _textures = new();
    private readonly Dictionary<(long Tint, int Strength, BlendKind Blend), ShaderMaterial> _lerpMaterials = new();
    private readonly CanvasItemMaterial _alphaMaterial = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Mix };
    private readonly CanvasItemMaterial _additiveMaterial = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private readonly ImageTexture _whiteTexture;
    private readonly Shader _lerpShader;
    private int _used;
    private int _textureUploads;
    private long _textureUploadTicks;

    public bool Visible
    {
        get => _stage.Visible;
        set => _stage.Visible = value;
    }

    public GpuRetainedRenderer(Node parent)
    {
        _stage = new Node2D { Name = "GpuRetainedStage", Visible = false };
        parent.AddChild(_stage);

        var white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        white.SetData(1, 1, false, Image.Format.Rgba8, new byte[] { 255, 255, 255, 255 });
        _whiteTexture = ImageTexture.CreateFromImage(white);

        _lerpShader = new Shader
        {
            Code = """
                shader_type canvas_item;
                render_mode blend_mix, unshaded;
                uniform vec3 age_tint = vec3(1.0);
                uniform float age_strength = 0.0;
                void fragment() {
                    vec4 source = texture(TEXTURE, UV);
                    source.rgb = mix(source.rgb, age_tint, age_strength);
                    COLOR = source * COLOR;
                }
                """
        };
    }

    public void BeginFrame(bool preserveExistingLayers)
    {
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

        var texture = ResolveTexture(source, assetId, colorKey, dynamic, dynamicKey);
        var item = NextItem();
        item.Texture = texture;
        item.RegionEnabled = true;
        item.RegionRect = new Rect2(srcX, srcY, clippedWidth, clippedHeight);
        item.Transform = ToGodot(localToDest);
        item.Modulate = Modulation(tint, opacity, multiplyTint);
        item.Material = ResolveMaterial(tint, tintStrength, multiplyTint, blend);
        item.Visible = true;
        return true;
    }

    public bool DrawFill(int width, int height, Affine2D localToDest, long tint, float opacity)
    {
        if (width <= 0 || height <= 0 || opacity <= 0) return false;
        var item = NextItem();
        item.Texture = _whiteTexture;
        item.RegionEnabled = false;
        item.Transform = ToGodot(new Affine2D(
            localToDest.XX * width, localToDest.XY * width,
            localToDest.YX * height, localToDest.YY * height,
            localToDest.TX, localToDest.TY));
        item.Modulate = Modulation(tint, opacity, multiplyTint: true);
        item.Material = _alphaMaterial;
        item.Visible = true;
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
                RegionFilterClipEnabled = true,
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

    private Texture2D ResolveTexture(RgbaImage source, int assetId, long colorKey, bool dynamic, long dynamicKey)
    {
        // One movie resource may play concurrently on multiple surfaces. Static assets share one upload;
        // dynamic surfaces use their surface/playback identity so one frame cannot overwrite another.
        var key = (assetId, colorKey, dynamic ? dynamicKey : 0);
        if (!_textures.TryGetValue(key, out var cached))
        {
            byte[] pixels = PreparePixels(source.Pixels, colorKey);
            var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, pixels);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            var texture = ImageTexture.CreateFromImage(image);
            _textureUploadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            _textureUploads++;
            cached = new CachedTexture
            {
                Texture = texture,
                Width = source.Width,
                Height = source.Height,
                LastPixels = dynamic ? source.Pixels : null,
            };
            _textures.Add(key, cached);
        }
        else if (dynamic && (!ReferenceEquals(cached.LastPixels, source.Pixels) ||
                             cached.Width != source.Width || cached.Height != source.Height))
        {
            byte[] pixels = PreparePixels(source.Pixels, colorKey);
            var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, pixels);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            if (cached.Width == source.Width && cached.Height == source.Height)
                cached.Texture.Update(image);
            else
            {
                cached.Texture = ImageTexture.CreateFromImage(image);
                cached.Width = source.Width;
                cached.Height = source.Height;
            }
            _textureUploadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            _textureUploads++;
            cached.LastPixels = source.Pixels;
        }
        return cached.Texture;
    }

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
            if (!_lerpMaterials.TryGetValue(key, out var material))
            {
                material = new ShaderMaterial { Shader = _lerpShader };
                material.SetShaderParameter("age_tint", new Vector3(
                    ((tint >> 16) & 0xff) / 255f,
                    ((tint >> 8) & 0xff) / 255f,
                    (tint & 0xff) / 255f));
                material.SetShaderParameter("age_strength", tintStrength / 255f);
                // LERP-tint objects observed so far use source-over. If an additive LERP mode appears,
                // keep the frame on the software oracle until it has a dedicated shader blend variant.
                _lerpMaterials.Add(key, material);
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
        foreach (var material in _lerpMaterials.Values) material.Dispose();
        foreach (var texture in _textures.Values) texture.Texture.Dispose();
        _lerpShader.Dispose();
        _items.Clear();
        _textures.Clear();
        _lerpMaterials.Clear();
    }
}

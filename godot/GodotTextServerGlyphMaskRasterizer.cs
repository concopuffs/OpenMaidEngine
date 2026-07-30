using System;
using Age.Engine.Text;
using Godot;

/// <summary>
/// Portable, explicitly non-pixel-exact glyph masks obtained from Godot's TextServer atlas.
/// AGE still owns layout, effects, blending, retained ordering, and surface lifetime.
/// </summary>
public sealed class GodotTextServerGlyphMaskRasterizer :
    IIdentifiedGlyphMaskRasterizer, IDisposable
{
    private readonly record struct FontKey(string RequestedFace, int Weight);

    private readonly object _gate = new();
    private readonly PortableTextRenderingPolicy _policy;
    private readonly TextServer _textServer;
    private readonly BoundedLruCache<FontKey, SystemFont> _fonts;
    private bool _disposed;

    public GodotTextServerGlyphMaskRasterizer(PortableTextRenderingPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _textServer = TextServerManager.GetPrimaryInterface()
            ?? throw new InvalidOperationException("Godot has no primary TextServer interface.");
        _fonts = new BoundedLruCache<FontKey, SystemFont>(
            policy.FontCacheCapacity, font => font.Dispose());
        BackendInfo = new GlyphRasterizerBackendInfo(
            "portable-godot-textserver",
            "Godot TextServer system fonts",
            GlyphRasterPolicy.PortableUnicode,
            NativePixelExact: false,
            $"policy={policy.Id}; raster={policy.Raster.Antialiasing}/" +
            $"{policy.Raster.Hinting}/{policy.Raster.SubpixelPositioning}; " +
            "Unicode system-font substitution; not GDI pixel-exact");
    }

    public GlyphRasterizerBackendInfo BackendInfo { get; }

    public GlyphMask Rasterize(GlyphRasterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Policy != GlyphRasterPolicy.PortableUnicode)
            throw new ArgumentException(
                "Godot TextServer accepts only portable Unicode glyph requests.",
                nameof(request));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SystemFont font = ResolveFont(request);
            // Asking the high-level Font for the character primes both the selected system face
            // and any system fallback RID before we inspect the TextServer cache directly.
            _ = font.GetCharSize(request.UnicodeScalar, request.PixelHeight);
            Rid rid = FindGlyphRid(font, request);
            long glyph = _textServer.FontGetGlyphIndex(
                rid, request.PixelHeight, request.UnicodeScalar, 0);
            if (glyph == 0 && request.UnicodeScalar != 0)
                throw MissingGlyph(request);

            var sizeKey = new Vector2I(request.PixelHeight, 0);
            _textServer.FontRenderGlyph(rid, sizeKey, glyph);
            Vector2 glyphSize = _textServer.FontGetGlyphSize(rid, sizeKey, glyph);
            Vector2 glyphOffset = _textServer.FontGetGlyphOffset(rid, sizeKey, glyph);
            Vector2 advance = _textServer.FontGetGlyphAdvance(
                rid, request.PixelHeight, glyph);
            Rect2 uv = _textServer.FontGetGlyphUVRect(rid, sizeKey, glyph);

            int width = Math.Max(0, Mathf.RoundToInt(glyphSize.X));
            int height = Math.Max(0, Mathf.RoundToInt(glyphSize.Y));
            byte[] coverage = width == 0 || height == 0
                ? []
                : ExtractCoverage(rid, sizeKey, glyph, uv, width, height);
            int advanceX = Mathf.RoundToInt(advance.X);
            return new GlyphMask(
                width,
                height,
                width,
                Mathf.RoundToInt(glyphOffset.X),
                -Mathf.RoundToInt(glyphOffset.Y),
                advanceX,
                0,
                Math.Max(width, advanceX),
                request.PixelHeight,
                coverage);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _fonts.Clear();
        }
    }

    private SystemFont ResolveFont(GlyphRasterRequest request)
    {
        PortableFontRasterPolicy raster = _policy.Raster;
        int weight = request.Weight >= raster.BoldThreshold
            ? raster.BoldWeight
            : raster.RegularWeight;
        var key = new FontKey(request.FontFace, weight);
        if (_fonts.TryGetValue(key, out SystemFont? cached)) return cached;

        var font = new SystemFont
        {
            FontNames = _policy.ResolveFamilies(request.FontFace),
            FontWeight = weight,
            AllowSystemFallback = raster.AllowSystemFallback,
            Antialiasing = raster.Antialiasing switch
            {
                "none" => TextServer.FontAntialiasing.None,
                "lcd" => TextServer.FontAntialiasing.Lcd,
                _ => TextServer.FontAntialiasing.Gray,
            },
            Hinting = raster.Hinting switch
            {
                "none" => TextServer.Hinting.None,
                "light" => TextServer.Hinting.Light,
                _ => TextServer.Hinting.Normal,
            },
            SubpixelPositioning = raster.SubpixelPositioning switch
            {
                "auto" => TextServer.SubpixelPositioning.Auto,
                "one-half" => TextServer.SubpixelPositioning.OneHalf,
                "one-quarter" => TextServer.SubpixelPositioning.OneQuarter,
                _ => TextServer.SubpixelPositioning.Disabled,
            },
            MultichannelSignedDistanceField =
                raster.MultichannelSignedDistanceField,
        };
        _fonts.Set(key, font);
        return font;
    }

    private Rid FindGlyphRid(SystemFont font, GlyphRasterRequest request)
    {
        foreach (Rid rid in font.GetRids())
            if (_textServer.FontHasChar(rid, request.UnicodeScalar))
                return rid;
        throw MissingGlyph(request);
    }

    private byte[] ExtractCoverage(
        Rid rid,
        Vector2I sizeKey,
        long glyph,
        Rect2 uv,
        int width,
        int height)
    {
        long textureIndex = _textServer.FontGetGlyphTextureIdx(rid, sizeKey, glyph);
        Image atlas = _textServer.FontGetTextureImage(rid, sizeKey, textureIndex)
            ?? throw new InvalidOperationException("TextServer returned no glyph atlas image.");
        int left = Mathf.RoundToInt(uv.Position.X);
        int top = Mathf.RoundToInt(uv.Position.Y);
        var result = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color pixel = atlas.GetPixel(left + x, top + y);
            float mask = atlas.GetFormat() switch
            {
                Image.Format.L8 => pixel.R,
                Image.Format.Rgb8 => Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)),
                _ => pixel.A,
            };
            result[y * width + x] =
                (byte)Math.Clamp(Mathf.RoundToInt(mask * 16f), 0, 16);
        }
        return result;
    }

    private static Exception MissingGlyph(GlyphRasterRequest request)
        => new InvalidOperationException(
            $"Portable font policy could not resolve U+{request.UnicodeScalar:X4} " +
            $"for authored face '{request.FontFace}'.");
}

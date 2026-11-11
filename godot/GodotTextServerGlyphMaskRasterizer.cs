using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ome.Engine.Text;
using Godot;

/// <summary>
/// Portable, explicitly non-pixel-exact glyph masks obtained from Godot's TextServer atlas.
/// AGE still owns layout, effects, blending, retained ordering, and surface lifetime.
/// </summary>
public sealed class GodotTextServerGlyphMaskRasterizer :
    IIdentifiedGlyphMaskRasterizer, IDisposable
{
    private readonly record struct FontKey(string RequestedFace, int Weight, bool Antialias);

    private readonly object _gate = new();
    private readonly PortableTextRenderingPolicy _policy;
    private readonly TextServer _textServer;
    private readonly BoundedLruCache<FontKey, Font> _fonts;
    private readonly Dictionary<string, byte[]?> _fontData = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, IReadOnlyList<string>?>? _languageFonts;
    private bool _disposed;

    public GodotTextServerGlyphMaskRasterizer(PortableTextRenderingPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _textServer = TextServerManager.GetPrimaryInterface()
            ?? throw new InvalidOperationException("Godot has no primary TextServer interface.");
        _fonts = new BoundedLruCache<FontKey, Font>(policy.FontCacheCapacity, DisposeFont);
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

    /// <summary>
    /// Puts a language's fonts (localization spec §7.4) in front of the policy's substitutes: for a face the language
    /// maps, its bundled files (absolute paths) first, then its family names, then the policy's families. Null
    /// removes them. The fonts made so far are dropped; the caller clears the glyph masks cached above this
    /// rasterizer. A bundled file draws at its own weight.
    /// </summary>
    public void UseLanguageFonts(Func<string, IReadOnlyList<string>?>? fontsFor)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _languageFonts = fontsFor;
            _fonts.Clear();
            _fontData.Clear();
        }
    }

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
            Font font = ResolveFont(request);
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
                : ExtractCoverage(rid, sizeKey, glyph, uv, width, height, request.Antialias);
            int advanceX = Mathf.RoundToInt(advance.X);
            double ascent = _textServer.FontGetAscent(rid, request.PixelHeight);
            double descent = _textServer.FontGetDescent(rid, request.PixelHeight);
            return new GlyphMask(
                width,
                height,
                width,
                Mathf.RoundToInt(glyphOffset.X),
                -Mathf.RoundToInt(glyphOffset.Y),
                advanceX,
                0,
                Math.Max(width, advanceX),
                // GDI's cell is the font's whole height (GetTextExtentPoint32A gives tmHeight, ascent plus descent), and
                // a retained glyph shows only its cell: an em-high cell would cut off descenders.
                Math.Max(request.PixelHeight, (int)Math.Round(ascent + descent)),
                coverage,
                // AGE places the baseline at the cell top plus the font's ascent (GDI tmAscent).
                (int)Math.Round(ascent),
                monochrome: !request.Antialias);
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

    private Font ResolveFont(GlyphRasterRequest request)
    {
        PortableFontRasterPolicy raster = _policy.Raster;
        int weight = request.Weight >= raster.BoldThreshold
            ? raster.BoldWeight
            : raster.RegularWeight;
        var key = new FontKey(request.FontFace, weight, request.Antialias);
        if (_fonts.TryGetValue(key, out Font? cached)) return cached;

        // UseAntiFont 0 asks for aliased glyphs whatever the policy's antialiasing.
        TextServer.FontAntialiasing antialiasing = !request.Antialias ? TextServer.FontAntialiasing.None
            : raster.Antialiasing switch
            {
                "none" => TextServer.FontAntialiasing.None,
                "lcd" => TextServer.FontAntialiasing.Lcd,
                _ => TextServer.FontAntialiasing.Gray,
            };
        TextServer.Hinting hinting = raster.Hinting switch
        {
            "none" => TextServer.Hinting.None,
            "light" => TextServer.Hinting.Light,
            _ => TextServer.Hinting.Normal,
        };
        TextServer.SubpixelPositioning subpixel = raster.SubpixelPositioning switch
        {
            "auto" => TextServer.SubpixelPositioning.Auto,
            "one-half" => TextServer.SubpixelPositioning.OneHalf,
            "one-quarter" => TextServer.SubpixelPositioning.OneQuarter,
            _ => TextServer.SubpixelPositioning.Disabled,
        };
        var bundled = new List<string>();
        var families = new List<string>();
        foreach (string use in _languageFonts?.Invoke(request.FontFace) ?? [])
            (Path.IsPathFullyQualified(use) ? bundled : families).Add(use);
        families.AddRange(_policy.ResolveFamilies(request.FontFace));
        var system = new SystemFont
        {
            FontNames = families.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            FontWeight = weight,
            AllowSystemFallback = raster.AllowSystemFallback,
            Antialiasing = antialiasing,
            Hinting = hinting,
            SubpixelPositioning = subpixel,
            MultichannelSignedDistanceField =
                raster.MultichannelSignedDistanceField,
        };
        var files = new List<FontFile>();
        foreach (string path in bundled)
            if (FontData(path) is { } data)
                files.Add(new FontFile
                {
                    Data = data,
                    Antialiasing = antialiasing,
                    Hinting = hinting,
                    SubpixelPositioning = subpixel,
                    MultichannelSignedDistanceField = raster.MultichannelSignedDistanceField,
                });
        Font font = system;
        if (files.Count > 0)
        {
            var fallbacks = new Godot.Collections.Array<Font>();
            foreach (FontFile file in files.Skip(1)) fallbacks.Add(file);
            fallbacks.Add(system);
            files[0].Fallbacks = fallbacks;
            font = files[0];
        }
        _fonts.Set(key, font);
        return font;
    }

    /// <summary>A bundled font file's bytes, read once; null (with a warning) when it cannot be read.</summary>
    private byte[]? FontData(string path)
    {
        if (_fontData.TryGetValue(path, out byte[]? known)) return known;
        byte[]? data = null;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[text] font {path}: {error.Message}");
        }
        return _fontData[path] = data;
    }

    private static void DisposeFont(Font font)
    {
        foreach (Font fallback in font.Fallbacks) fallback.Dispose();
        font.Dispose();
    }

    private Rid FindGlyphRid(Font font, GlyphRasterRequest request)
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
        int height,
        bool antialias)
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
            result[y * width + x] = antialias
                ? (byte)Math.Clamp(Mathf.RoundToInt(mask * 16f), 0, 16)
                : (byte)(mask >= 0.5f ? 16 : 0);
        }
        return result;
    }

    private static Exception MissingGlyph(GlyphRasterRequest request)
        => new InvalidOperationException(
            $"Portable font policy could not resolve U+{request.UnicodeScalar:X4} " +
            $"for authored face '{request.FontFace}'.");
}

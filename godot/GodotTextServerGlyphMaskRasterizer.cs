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
/// <remarks>
/// A face the game names is drawn from its own file where the installed fonts have it
/// (<see cref="InstalledFontIndex"/>), at the weight GDI would pick; a system font behind it supplies a face the index
/// lacks and the system's fallback. With the file known, the glyph drawn at the face's own width becomes what GDI
/// draws for the request (<see cref="GdiGlyphRealization"/>): the width scaled against the face's average width, a
/// monospaced face's whole cells, GDI's ascent and height, and <see cref="SimulatedBold"/> for a bold request that a
/// face with no bold style answers (ＭＳ ゴシック). (TextServer cannot scale a face per request: a linked variation
/// shares its face's transform, so <see cref="GlyphMaskNarrowing"/> scales the mask.) As a metrics probe
/// (<see cref="IFontMetricsProbe"/>) it reports GDI's metrics for the face that draws, so aspect mode's calibration
/// (<see cref="AspectFontRealization"/>) comes out as GDI's.
/// </remarks>
public sealed class GodotTextServerGlyphMaskRasterizer :
    IIdentifiedGlyphMaskRasterizer, ILanguageFontRasterizer, IGameFontRasterizer, IFontMetricsProbe, IDisposable
{
    /// <summary>A resolved chain's key; <paramref name="LanguageFonts"/> is false for the reference glyph, which a
    /// language's fonts do not replace.</summary>
    private readonly record struct FontKey(string RequestedFace, int Weight, bool Antialias, bool LanguageFonts);

    /// <summary>One font of a resolved chain: its own text server RIDs (taken before any fallback is attached), the
    /// face file it draws (null for the system font, whose file is not known), that face's weight, and whether it
    /// stands in for a requested face the system lacks (a substitute, not the face itself or a language's font).
    /// </summary>
    private sealed record ChainMember(Font Font, Rid[] OwnRids, OpenTypeFace? Face, int Weight, bool StandsIn = false);

    /// <summary>A requested face's fonts, first to last fallback; <see cref="Root"/> carries the rest as fallbacks.
    /// </summary>
    private sealed record ResolvedFont(Font Root, IReadOnlyList<ChainMember> Members);

    private static readonly System.Text.Encoding Cp932 = CreateCp932();

    private readonly object _gate = new();
    private readonly PortableTextRenderingPolicy _policy;
    private readonly TextServer _textServer;
    private readonly Func<InstalledFontIndex> _installedFonts;
    private readonly BoundedLruCache<FontKey, ResolvedFont> _fonts;
    private readonly Dictionary<string, byte[]?> _fontData = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OpenTypeFace?> _bundledFaces = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, IReadOnlyList<string>?>? _languageFonts;
    // The font files a game ships in its catalog, by the face each provides (IGameFontRasterizer).
    private readonly Dictionary<string, (byte[] Data, OpenTypeFace? Face)> _gameFonts =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <param name="policy">The substitution list and raster settings.</param>
    /// <param name="installedFonts">The installed fonts the requested faces are found in, read when first needed;
    /// without it every face draws through the system font, its widths scaled against half its height.</param>
    public GodotTextServerGlyphMaskRasterizer(PortableTextRenderingPolicy policy,
                                              Func<InstalledFontIndex>? installedFonts = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _installedFonts = installedFonts ?? (() => InstalledFontIndex.Empty);
        _textServer = TextServerManager.GetPrimaryInterface()
            ?? throw new InvalidOperationException("Godot has no primary TextServer interface.");
        _fonts = new BoundedLruCache<FontKey, ResolvedFont>(policy.FontCacheCapacity, DisposeFont);
        BackendInfo = new GlyphRasterizerBackendInfo(
            "portable-godot-textserver",
            "Godot TextServer system fonts",
            GlyphRasterPolicy.PortableUnicode,
            NativePixelExact: false,
            $"policy={policy.Id}; raster={policy.Raster.Antialiasing}/" +
            $"{policy.Raster.Hinting}/{policy.Raster.SubpixelPositioning}; " +
            "Unicode system-font substitution; GDI metrics from font files; not GDI pixel-exact");
    }

    public GlyphRasterizerBackendInfo BackendInfo { get; }

    /// <summary>
    /// Puts a language's fonts (localization spec §7.4) in front of the policy's substitutes: for a face the language
    /// maps, its bundled files (absolute paths) first, then its family names, then the policy's families. Null
    /// removes them. The fonts made so far are dropped; the caller clears the glyph masks cached above this
    /// rasterizer. A bundled file has only its own weight: a bold request it answers regular is simulated bold.
    /// </summary>
    public void UseLanguageFonts(Func<string, IReadOnlyList<string>?>? fontsFor)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _languageFonts = fontsFor;
            _fonts.Clear();
            _fontData.Clear();
            _bundledFaces.Clear();
        }
    }

    /// <summary>
    /// Puts a game's font file in front of everything else for the face it provides: a request for
    /// <paramref name="faceName"/> draws from the file first, then from the policy's substitutes for characters the
    /// file lacks. A file this cannot read as a font is still given to TextServer, with a warning.
    /// </summary>
    public void AddGameFont(string faceName, byte[] fontFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(faceName);
        ArgumentNullException.ThrowIfNull(fontFile);
        OpenTypeFace? face = null;
        try
        {
            face = OpenTypeFace.ReadData(fontFile, $"catalog font {faceName}")[0];
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
        {
            GD.PushWarning($"[text] game font {faceName}: {error.Message}");
        }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _gameFonts[faceName] = (fontFile, face);
            _fonts.Clear();
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
            return Realize(request, languageFonts: true).Mask;
        }
    }

    /// <summary>The GGO_METRICS and TEXTMETRIC values aspect mode's calibration reads (<see cref="IFontMetricsProbe"/>),
    /// GDI's for the face that draws the request (<see cref="GdiGlyphRealization.Measure"/>).</summary>
    public FontProbeMetrics Measure(string faceName, int pixelHeight, int width, int weight, ushort code)
        => MeasureLocked(faceName, pixelHeight, width, weight, code, languageFonts: true);

    /// <summary>The reference glyph in ＭＳ ゴシック as installed (or its substitutes), whatever a language's fonts put
    /// in its place (<see cref="IFontMetricsProbe.MeasureReference"/>).</summary>
    public FontProbeMetrics MeasureReference(int pixelHeight, int width, int weight, ushort code)
        => MeasureLocked(AspectFontRealization.ReferenceFace, pixelHeight, width, weight, code, languageFonts: false);

    /// <summary>The face a language's font puts in place of <paramref name="faceName"/>
    /// (<see cref="IFontMetricsProbe.Replacement"/>): the first of the language's fonts for it that this rasterizer can
    /// draw, a bundled file or an installed family, named and pitched as GDI's font list would show it. Null without
    /// one.</summary>
    public EnumeratedFontFamily? Replacement(string faceName)
    {
        ArgumentNullException.ThrowIfNull(faceName);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (string use in _languageFonts?.Invoke(faceName) ?? [])
            {
                if (Path.IsPathFullyQualified(use))
                {
                    if (BundledFace(use) is { } bundled && GdiFontRules.Families(bundled) is [var listed, ..])
                        return new EnumeratedFontFamily(listed.Family, GdiFontRules.PitchAndFamily(bundled));
                }
                else if (_installedFonts().Family(use) is { } family)
                    return new EnumeratedFontFamily(family.Name, family.PitchAndFamily);
            }
            return null;
        }
    }

    private FontProbeMetrics MeasureLocked(string faceName, int pixelHeight, int width, int weight, ushort code,
                                           bool languageFonts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(faceName);
        byte[] bytes = code <= 0xff ? [(byte)code] : [(byte)(code >> 8), (byte)code];
        string text = Cp932.GetString(bytes);
        var request = new GlyphRasterRequest(faceName, pixelHeight, width, weight, char.ConvertToUtf32(text, 0), code,
                                             GlyphRasterPolicy.PortableUnicode, antialias: true);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            (GlyphMask mask, OpenTypeFace? face, bool simulated) = Realize(request, languageFonts);
            int glyph = face?.GlyphIndex(request.UnicodeScalar) ?? 0;
            return GdiGlyphRealization.Measure(mask, face, request, simulated, glyph);
        }
    }

    /// <summary>What GDI draws for the request (<see cref="GdiGlyphRealization.Draw"/>), the face file that drew it
    /// when known, and whether the glyph is simulated bold: a bold request a face lighter than semibold answers.
    /// </summary>
    private (GlyphMask Mask, OpenTypeFace? Face, bool SimulatedBold) Realize(GlyphRasterRequest request,
                                                                             bool languageFonts)
    {
        (GlyphMask natural, bool drawnRegular, OpenTypeFace? face) = RasterizeNatural(request, languageFonts);
        bool simulated = drawnRegular && request.Weight >= _policy.Raster.BoldThreshold;
        return (GdiGlyphRealization.Draw(natural, face, request, simulated), face, simulated);
    }

    /// <summary>The glyph at the face's own width, whether its face drew it regular (lighter than semibold and not
    /// emboldened), and the face file that drew it when known.</summary>
    private (GlyphMask Mask, bool DrawnRegular, OpenTypeFace? Face) RasterizeNatural(GlyphRasterRequest request,
                                                                                     bool languageFonts)
    {
        ResolvedFont resolved = ResolveFont(request, languageFonts);
        Font font = resolved.Root;
        // Asking the high-level Font for the character primes both the selected system face
        // and any system fallback RID before we inspect the TextServer cache directly.
        _ = font.GetCharSize(request.UnicodeScalar, request.PixelHeight);
        // A control character no face draws (a tab in a script's string) takes a space's room and draws nothing,
        // as the layout counts it. So does a script string's raw unit (Sys4TextEncoding), a code with no Unicode
        // character: a single byte a space's room, two bytes an ideographic space's. So does a character of Shift-JIS's
        // user-defined area (0xF040 to 0xF9FC, U+E000 up) that no face of the chain has, as GDI draws the face's
        // default glyph: ExtendedFaceGlyphRasterizer asks the text's own face for one only for its ascent and a ruby's
        // cell, and draws the glyph from the build's extended face.
        bool raw = Ome.Engine.Sys4.Sys4TextEncoding.TryGetRawCode(request.UnicodeScalar, out int rawCode);
        bool userDefined = ExtendedFaceGlyphRasterizer.IsExtended(request.Cp932Code)
                           && !HasGlyph(font, request.UnicodeScalar);
        if (raw || userDefined || request.UnicodeScalar is < 0x20 or 0x7f && !HasGlyph(font, request.UnicodeScalar))
        {
            (int scalar, ushort code) = raw && rawCode > 0xff || userDefined
                ? ('\u3000', (ushort)0x8140)
                : (' ', (ushort)0x20);
            (GlyphMask space, bool spaceDrawnRegular, OpenTypeFace? spaceFace) = RasterizeNatural(
                new GlyphRasterRequest(request.FontFace, request.PixelHeight, request.RequestedWidth, request.Weight,
                                       scalar, code, request.Policy, request.Antialias),
                languageFonts);
            return (Blank(space), spaceDrawnRegular, spaceFace);
        }
        Rid rid = FindGlyphRid(font, request);
        ChainMember? member = resolved.Members.FirstOrDefault(candidate => candidate.OwnRids.Contains(rid));
        OpenTypeFace? face = member?.Face;
        bool drawnRegular = face != null
            ? member!.Weight < 600
            : _textServer.FontGetWeight(rid) < 600 && _textServer.FontGetEmbolden(rid) == 0;
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
        int originX = Mathf.RoundToInt(glyphOffset.X), originY = -Mathf.RoundToInt(glyphOffset.Y);
        // The atlas keeps a blank margin around each glyph; GDI's mask is the black box, which aspect mode's placement
        // measures (an opening bracket's box ends at the reference's), so the margin goes.
        (coverage, width, height, originX, originY) = Trim(coverage, width, height, originX, originY);
        int advanceX = Mathf.RoundToInt(advance.X);
        double ascent = _textServer.FontGetAscent(rid, request.PixelHeight);
        double descent = _textServer.FontGetDescent(rid, request.PixelHeight);
        return (new GlyphMask(
            width,
            height,
            width,
            originX,
            originY,
            advanceX,
            0,
            Math.Max(width, advanceX),
            // GDI's cell is the font's whole height (GetTextExtentPoint32A gives tmHeight, ascent plus descent), and
            // a retained glyph shows only its cell: an em-high cell would cut off descenders. With the face's file
            // known, GdiGlyphRealization puts GDI's own height and ascent in their place.
            Math.Max(request.PixelHeight, (int)Math.Round(ascent + descent)),
            coverage,
            // AGE places the baseline at the cell top plus the font's ascent (GDI tmAscent).
            (int)Math.Round(ascent),
            monochrome: !request.Antialias), drawnRegular, member is { StandsIn: true } ? null : face);
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

    /// <summary>The coverage cut to its inked rows and columns, the origin moved with it; a glyph with no ink keeps
    /// none.</summary>
    private static (byte[] Coverage, int Width, int Height, int OriginX, int OriginY) Trim(
        byte[] coverage, int width, int height, int originX, int originY)
    {
        int left = width, right = -1, top = height, bottom = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (coverage[y * width + x] != 0)
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        if (right < 0) return ([], 0, 0, originX, originY);
        int trimmedWidth = right - left + 1, trimmedHeight = bottom - top + 1;
        if (trimmedWidth == width && trimmedHeight == height) return (coverage, width, height, originX, originY);
        var trimmed = new byte[trimmedWidth * trimmedHeight];
        for (int y = 0; y < trimmedHeight; y++)
            Array.Copy(coverage, (top + y) * width + left, trimmed, y * trimmedWidth, trimmedWidth);
        return (trimmed, trimmedWidth, trimmedHeight, originX + left, originY - top);
    }

    private bool HasGlyph(Font font, int scalar)
    {
        foreach (Rid rid in font.GetRids())
            if (_textServer.FontHasChar(rid, scalar))
                return true;
        return false;
    }

    /// <summary><paramref name="space"/>'s room with no ink.</summary>
    private static GlyphMask Blank(GlyphMask space)
        => new(0, 0, 0, space.OriginX, space.OriginY, space.CellAdvanceX, space.CellAdvanceY, space.CellWidth,
               space.CellHeight, [], space.Ascent, space.Monochrome);

    // A code with no Unicode character decodes to its raw scalar (Sys4TextEncoding), which draws blank.
    private static System.Text.Encoding CreateCp932() => Ome.Engine.Sys4.Sys4TextEncoding.Replacing;

    /// <summary>
    /// The fonts a requested face draws with, first to last: the language's bundled files for it; then the first of
    /// the language's family names and the policy's families (the requested face, then its substitutes) that the
    /// installed fonts have, from its own file at the weight GDI would pick; then a system font over all those names,
    /// for a face the index lacks and the system's fallback for characters none of them has. Without
    /// <paramref name="languageFonts"/> the language's fonts are left out.
    /// </summary>
    private ResolvedFont ResolveFont(GlyphRasterRequest request, bool languageFonts)
    {
        PortableFontRasterPolicy raster = _policy.Raster;
        int weight = request.Weight >= raster.BoldThreshold
            ? raster.BoldWeight
            : raster.RegularWeight;
        var key = new FontKey(request.FontFace, weight, request.Antialias, languageFonts);
        if (_fonts.TryGetValue(key, out ResolvedFont? cached)) return cached;

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
        if (languageFonts)
            foreach (string use in _languageFonts?.Invoke(request.FontFace) ?? [])
                (Path.IsPathFullyQualified(use) ? bundled : families).Add(use);
        var languageFamilies = new HashSet<string>(families, StringComparer.OrdinalIgnoreCase);
        families.AddRange(_policy.ResolveFamilies(request.FontFace));
        string[] names = families.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        FontFile FileFont(byte[] data, int faceIndex)
        {
            var file = new FontFile
            {
                Data = data,
                Antialiasing = antialiasing,
                Hinting = hinting,
                SubpixelPositioning = subpixel,
                MultichannelSignedDistanceField = raster.MultichannelSignedDistanceField,
                // GDI's antialiased glyphs are the outlines, never a face's embedded bitmaps (ＭＳ ゴシック has
                // thinner, smaller ones up to 22 pixels).
                DisableEmbeddedBitmaps = true,
            };
            if (faceIndex != 0) file.SetFaceIndex(0, faceIndex);
            return file;
        }

        var members = new List<ChainMember>();
        if (_gameFonts.TryGetValue(request.FontFace, out (byte[] Data, OpenTypeFace? Face) game))
        {
            FontFile file = FileFont(game.Data, 0);
            members.Add(new ChainMember(file, OwnRids(file), game.Face, game.Face?.WeightClass ?? 400));
        }
        foreach (string path in bundled)
            if (FontData(path) is { } data)
            {
                OpenTypeFace? face = BundledFace(path);
                FontFile file = FileFont(data, 0);
                members.Add(new ChainMember(file, OwnRids(file), face, face?.WeightClass ?? 400));
            }
        InstalledFontIndex installed = _installedFonts();
        OpenTypeFace? requested = installed.Resolve(request.FontFace, weight)?.Face;
        foreach (string name in names)
        {
            if (installed.Resolve(name, weight) is not { } choice) continue;
            // A substitute draws in the room of the face it stands in for, which this system lacks: GDI's rules are
            // that face's, not the substitute's own (GdiGlyphRealization, as for a face whose file is unknown).
            bool standsIn = !languageFamilies.Contains(name) && !ReferenceEquals(choice.Face, requested);
            if (FontData(choice.Face.Source) is { } data)
            {
                FontFile file = FileFont(data, choice.Face.Index);
                Font font = file;
                if (choice.Instance != null)
                {
                    var coordinates = new Godot.Collections.Dictionary();
                    foreach ((string axis, float value) in choice.Instance.Coordinates) coordinates[axis] = value;
                    font = new FontVariation { BaseFont = file, VariationOpentype = coordinates };
                }
                members.Add(new ChainMember(font, OwnRids(font), choice.Face, choice.Weight, standsIn));
            }
            break;
        }
        var system = new SystemFont
        {
            FontNames = names,
            FontWeight = weight,
            AllowSystemFallback = raster.AllowSystemFallback,
            Antialiasing = antialiasing,
            Hinting = hinting,
            SubpixelPositioning = subpixel,
            MultichannelSignedDistanceField =
                raster.MultichannelSignedDistanceField,
        };
        members.Add(new ChainMember(system, [], null, weight));
        if (members.Count > 1)
        {
            var fallbacks = new Godot.Collections.Array<Font>();
            foreach (ChainMember member in members.Skip(1)) fallbacks.Add(member.Font);
            members[0].Font.Fallbacks = fallbacks;
        }
        var resolved = new ResolvedFont(members[0].Font, members);
        _fonts.Set(key, resolved);
        GD.Print($"[text] {request.FontFace} at weight {weight}{(languageFonts ? "" : " (reference)")}: " +
                 string.Join(" > ", members.Select(member => member.Face is { } face
                     ? $"{Path.GetFileName(face.Source)}#{face.Index} weight {member.Weight}" +
                       (member.StandsIn ? " standing in" : "")
                     : "system font")));
        return resolved;
    }

    /// <summary>A font's own text server RIDs; read before fallbacks are attached, they are its file's alone.</summary>
    private static Rid[] OwnRids(Font font) => [.. font.GetRids()];

    /// <summary>The first face of a bundled font file, read once; null (with a warning) when it is not a font this
    /// reads, which TextServer may still draw.</summary>
    private OpenTypeFace? BundledFace(string path)
    {
        if (_bundledFaces.TryGetValue(path, out OpenTypeFace? known)) return known;
        OpenTypeFace? face = null;
        if (FontData(path) is { } data)
            try
            {
                face = OpenTypeFace.ReadData(data, path)[0];
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
            {
                GD.PushWarning($"[text] font {path}: {error.Message}");
            }
        return _bundledFaces[path] = face;
    }

    /// <summary>A font file's bytes, read once; null (with a warning) when it cannot be read.</summary>
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

    private static void DisposeFont(ResolvedFont resolved)
    {
        foreach (ChainMember member in resolved.Members)
        {
            if (member.Font is FontVariation { BaseFont: { } baseFont }) baseFont.Dispose();
            member.Font.Dispose();
        }
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

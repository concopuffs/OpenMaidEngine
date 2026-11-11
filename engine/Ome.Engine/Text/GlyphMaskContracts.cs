using System.Text;

namespace Ome.Engine.Text;

/// <summary>Which rasterizer family a request targets. <see cref="NativeCp932Gray4"/> is AGE's GDI path; despite
/// its name it covers both of AGE's glyph formats, which <see cref="GlyphRasterRequest.Antialias"/> selects.</summary>
public enum GlyphRasterPolicy
{
    NativeCp932Gray4,
    PortableUnicode,
}

/// <summary>
/// Backend-neutral description of one requested glyph. Native-compatible requests preserve both
/// the Unicode scalar used by authored code and the original CP932 code consumed by AGE/GDI.
/// <see cref="Antialias"/> is AGE's text-manager+0x544 (message:UseAntiFont): true asks for GGO_GRAY4_BITMAP
/// coverage, false for GGO_BITMAP's aliased glyphs.
/// </summary>
public sealed record GlyphRasterRequest
{
    public GlyphRasterRequest(
        string fontFace,
        int pixelHeight,
        int requestedWidth,
        int weight,
        int unicodeScalar,
        ushort? cp932Code,
        GlyphRasterPolicy policy,
        bool antialias = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFace);
        if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        if (weight is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(weight));
        if (!Rune.IsValid(unicodeScalar)) throw new ArgumentOutOfRangeException(nameof(unicodeScalar));
        if (policy == GlyphRasterPolicy.NativeCp932Gray4 && cp932Code == null)
            throw new ArgumentException("Native CP932 glyph requests require the original code.", nameof(cp932Code));

        FontFace = fontFace;
        PixelHeight = pixelHeight;
        RequestedWidth = requestedWidth;
        Weight = weight;
        UnicodeScalar = unicodeScalar;
        Cp932Code = cp932Code;
        Policy = policy;
        Antialias = antialias;
    }

    public string FontFace { get; }
    public int PixelHeight { get; }
    public int RequestedWidth { get; }
    public int Weight { get; }
    public int UnicodeScalar { get; }
    public ushort? Cp932Code { get; }
    public GlyphRasterPolicy Policy { get; }
    public bool Antialias { get; }

    private readonly int _widthPercent = 100;

    /// <summary>The glyph's width in percent of the font's own, 1 to 100: presented text an entry fits to its source
    /// text's extent (localization spec §7.2, <c>fit = "shrink"</c>). The backends draw every glyph at the font's
    /// width; <see cref="CachedGlyphMaskRasterizer"/> narrows the mask (<see cref="GlyphMaskNarrowing"/>).</summary>
    public int WidthPercent
    {
        get => _widthPercent;
        init => _widthPercent = value is >= 1 and <= 100
            ? value
            : throw new ArgumentOutOfRangeException(nameof(WidthPercent), value, "A glyph's width is 1 to 100 percent.");
    }
}

/// <summary>
/// One glyph mask plus GDI-compatible placement and cell metrics. Coverage values use AGE's normalized
/// 0..16 range, regardless of which platform rasterizer produced them. A <see cref="Monochrome"/> mask
/// (an aliased GGO_BITMAP glyph) holds only 0 and 16.
/// </summary>
public sealed class GlyphMask
{
    private readonly byte[] _coverage;

    public GlyphMask(
        int width,
        int height,
        int stride,
        int originX,
        int originY,
        int cellAdvanceX,
        int cellAdvanceY,
        int cellWidth,
        int cellHeight,
        ReadOnlySpan<byte> coverage,
        int? ascent = null,
        bool monochrome = false)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (stride < width) throw new ArgumentOutOfRangeException(nameof(stride));
        if (cellWidth < 0) throw new ArgumentOutOfRangeException(nameof(cellWidth));
        if (cellHeight < 0) throw new ArgumentOutOfRangeException(nameof(cellHeight));
        int required = checked(stride * height);
        if (coverage.Length != required)
            throw new ArgumentException($"Glyph coverage has {coverage.Length} bytes; expected {required}.",
                                        nameof(coverage));
        foreach (byte value in coverage)
            if (value > 16)
                throw new ArgumentException("Glyph coverage values must be in AGE's 0..16 range.",
                                            nameof(coverage));
            else if (monochrome && value is not (0 or 16))
                throw new ArgumentException("A monochrome glyph's coverage is 0 or 16.", nameof(coverage));

        Width = width;
        Height = height;
        Stride = stride;
        OriginX = originX;
        OriginY = originY;
        CellAdvanceX = cellAdvanceX;
        CellAdvanceY = cellAdvanceY;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Ascent = ascent;
        Monochrome = monochrome;
        _coverage = coverage.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public int OriginX { get; }
    public int OriginY { get; }
    public int CellAdvanceX { get; }
    public int CellAdvanceY { get; }
    public int CellWidth { get; }
    public int CellHeight { get; }

    /// <summary>
    /// The font's ascent (GDI <c>tmAscent</c>) at this size. AGE draws every glyph with its baseline at
    /// the cell top plus this value. Null when the source has no font metrics; callers then fall back to
    /// the pixel height.
    /// </summary>
    public int? Ascent { get; }

    /// <summary>
    /// An aliased glyph (GGO_BITMAP, AntiFont off): every coverage value is 0 or 16. AGE keeps such a glyph as
    /// packed bits, which changes what its mode-2 second pass draws (<see cref="AgeGlyphMaskCompositor"/>).
    /// </summary>
    public bool Monochrome { get; }

    public ReadOnlyMemory<byte> Coverage => _coverage;
}

public interface IGlyphMaskRasterizer
{
    GlyphMask Rasterize(GlyphRasterRequest request);
}

public sealed record GlyphRasterizerBackendInfo(
    string Id,
    string DisplayName,
    GlyphRasterPolicy Policy,
    bool NativePixelExact,
    string Detail);

public interface IIdentifiedGlyphMaskRasterizer : IGlyphMaskRasterizer
{
    GlyphRasterizerBackendInfo BackendInfo { get; }
}

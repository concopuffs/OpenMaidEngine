using System.Text;

namespace Age.Engine.Text;

public enum GlyphRasterPolicy
{
    NativeCp932Gray4,
    PortableUnicode,
}

/// <summary>
/// Backend-neutral description of one requested glyph. Native-compatible requests preserve both
/// the Unicode scalar used by authored code and the original CP932 code consumed by AGE/GDI.
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
        GlyphRasterPolicy policy)
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
    }

    public string FontFace { get; }
    public int PixelHeight { get; }
    public int RequestedWidth { get; }
    public int Weight { get; }
    public int UnicodeScalar { get; }
    public ushort? Cp932Code { get; }
    public GlyphRasterPolicy Policy { get; }
}

/// <summary>
/// One grayscale glyph mask plus GDI-compatible placement and cell metrics. Coverage values use
/// AGE's normalized 0..16 range, regardless of which platform rasterizer produced them.
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
        ReadOnlySpan<byte> coverage)
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

        Width = width;
        Height = height;
        Stride = stride;
        OriginX = originX;
        OriginY = originY;
        CellAdvanceX = cellAdvanceX;
        CellAdvanceY = cellAdvanceY;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
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
    public ReadOnlyMemory<byte> Coverage => _coverage;
}

public interface IGlyphMaskRasterizer
{
    GlyphMask Rasterize(GlyphRasterRequest request);
}

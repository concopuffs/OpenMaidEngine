using System.Text;
using Age.Engine.Model;
using Age.Engine.Sys4;

namespace Age.Engine.Text;

public readonly record struct ImmediateSurfaceTextResult(
    int GlyphCount,
    int CursorX,
    int CursorY);

/// <summary>
/// Converts one string into backend-policy glyph requests and composites it directly into an AGE
/// RGBA surface. Request construction completes before any pixels change, so an unsupported
/// character cannot leave a partially drawn string behind.
/// </summary>
public sealed class ImmediateSurfaceTextRenderer
{
    public const string DefaultFontFace = "ＭＳ 明朝";

    private static readonly Encoding Cp932 = CreateCp932();
    private readonly IGlyphMaskRasterizer _rasterizer;
    private readonly GlyphRasterPolicy _policy;

    public ImmediateSurfaceTextRenderer(
        IGlyphMaskRasterizer rasterizer,
        GlyphRasterPolicy policy = GlyphRasterPolicy.NativeCp932Gray4)
    {
        _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
        _policy = policy;
    }

    public ImmediateSurfaceTextResult Render(
        RgbaImage destination,
        int x,
        int y,
        string text,
        AdvTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<GlyphRasterRequest> requests = CreateRequests(text, style, _policy);
        int cursorX = x;
        int cursorY = y;
        foreach (GlyphRasterRequest request in requests)
        {
            GlyphMask mask = _rasterizer.Rasterize(request);
            AgeGlyphMaskCompositor.DrawGlyph(
                destination, mask, cursorX, cursorY, request.PixelHeight, style);
            cursorX = checked(cursorX + mask.CellAdvanceX);
            cursorY = checked(cursorY + mask.CellAdvanceY);
        }
        return new ImmediateSurfaceTextResult(requests.Count, cursorX, cursorY);
    }

    public static IReadOnlyList<GlyphRasterRequest> CreateRequests(
        string text,
        AdvTextStyle style,
        GlyphRasterPolicy policy = GlyphRasterPolicy.NativeCp932Gray4)
    {
        ArgumentNullException.ThrowIfNull(text);
        int requestedHeight = style.PrimaryFontSize > 0 ? style.PrimaryFontSize : 24;
        int pixelHeight = NativePixelHeight(requestedHeight);
        int requestedWidth = -(pixelHeight / 2);
        int weight = style.Bold ? 700 : 0;
        string fontFace = string.IsNullOrWhiteSpace(style.FontFace)
            ? DefaultFontFace
            : style.FontFace;
        var requests = new List<GlyphRasterRequest>(text.Length);

        foreach (Rune rune in text.EnumerateRunes())
        {
            ushort? cp932Code = null;
            try
            {
                byte[] encoded = Cp932.GetBytes(rune.ToString());
                if (encoded.Length is < 1 or > 2)
                    throw new ArgumentException(
                        $"U+{rune.Value:X4} encoded to unsupported CP932 length {encoded.Length}.",
                        nameof(text));
                cp932Code = encoded.Length == 1
                    ? encoded[0]
                    : (ushort)((encoded[0] << 8) | encoded[1]);
            }
            catch (EncoderFallbackException error)
            {
                if (policy == GlyphRasterPolicy.NativeCp932Gray4)
                    throw new ArgumentException(
                        $"U+{rune.Value:X4} is not representable in native CP932 text.",
                        nameof(text), error);
            }
            requests.Add(new GlyphRasterRequest(
                fontFace, pixelHeight, requestedWidth, weight,
                rune.Value, cp932Code, policy));
        }
        return requests;
    }

    public static int NativePixelHeight(int requestedHeight)
    {
        if (requestedHeight <= 0) return 24;
        if (requestedHeight is 32 or 33) return 31;
        return (requestedHeight & 1) != 0 ? requestedHeight - 1 : requestedHeight;
    }

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}

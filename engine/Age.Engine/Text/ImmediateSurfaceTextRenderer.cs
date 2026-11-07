using System.Text;
using Age.Engine.Model;

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
    private readonly GlyphCoverageAlpha _coverageAlpha;

    public ImmediateSurfaceTextRenderer(
        IGlyphMaskRasterizer rasterizer,
        GlyphRasterPolicy policy = GlyphRasterPolicy.NativeCp932Gray4,
        GlyphCoverageAlpha coverageAlpha = GlyphCoverageAlpha.Sixteenths)
    {
        _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
        _policy = policy;
        _coverageAlpha = coverageAlpha;
    }

    /// <param name="antialias">message:UseAntiFont (text manager +0x544). draw_string_to_surface@0x450150 calls the
    /// raster workers directly, so an aliased string keeps its position.</param>
    /// <param name="realization">The installed-font list and metrics behind SYS4433's aspect-mode font widths; without
    /// one every font is half its height wide.</param>
    /// <param name="blankExtentMode">SYS4INI <c>BLANKEXTENTMODE</c> = 1: in aspect mode a blank advances by its own extent
    /// in the reference font rather than by the reference cell.</param>
    public ImmediateSurfaceTextResult Render(
        RgbaImage destination,
        int x,
        int y,
        string text,
        AdvTextStyle style,
        bool antialias = true,
        AspectFontRealization? realization = null,
        bool blankExtentMode = false)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<GlyphRasterRequest> requests = CreateRequests(text, style, _policy, antialias, realization);
        if (style.AspectMode != 0)
            return RenderAspect(destination, x, y, requests, style, antialias, blankExtentMode);
        int cursorX = x;
        int cursorY = y;
        foreach (GlyphRasterRequest request in requests)
        {
            GlyphMask mask = _rasterizer.Rasterize(request);
            AgeGlyphMaskCompositor.DrawGlyph(
                destination, mask, cursorX, cursorY,
                AgeGlyphMaskCompositor.CellTopToBaseline(mask, request.PixelHeight), style, _coverageAlpha);
            cursorX = checked(cursorX + mask.CellAdvanceX);
            cursorY = checked(cursorY + mask.CellAdvanceY);
        }
        return new ImmediateSurfaceTextResult(requests.Count, cursorX, cursorY);
    }

    /// <summary>
    /// SYS4433's aspect-mode surface text (text_draw_string_to_surface_sys4433@0x44f8a0 and its glyph-string draws;
    /// docs/engine-re.md, "Aspect-mode surface text"). Mode 1 moves y by the reference ＭＳ ゴシック's ascent minus the
    /// primary font's, so every glyph's baseline is <c>y + ref.tmAscent</c> (other modes keep the primary font's). Each
    /// glyph draws at the pen, with no in-cell placement. The two draws advance differently:
    /// <list type="bullet">
    /// <item>without an effect (text_draw_glyph_string_plain@0x45e4e0) a glyph advances by its own cell, and a blank
    /// (a space, 0x8140 or a control byte) by the reference 激's cell, halved for one byte;</item>
    /// <item>with an outline or shadow (text_draw_glyph_string_effect@0x460250) a glyph advances by
    /// <c>trunc(ref.gmCellIncX / (one byte ? 2.0 : 1.0) + 0.5)</c>, and a blank by the font size, halved for one
    /// byte.</item>
    /// </list>
    /// Under <c>BLANKEXTENTMODE</c> a space or 0x8140 advances instead by its extent in the reference font
    /// (text_measure_blank_extent@0x404da0); the effect draw does so only in aspect mode 1. AGE measures a control
    /// byte's extent too; here a control byte keeps the fallback advance.
    /// </summary>
    private ImmediateSurfaceTextResult RenderAspect(
        RgbaImage destination, int x, int y, IReadOnlyList<GlyphRasterRequest> requests, AdvTextStyle style,
        bool antialias, bool blankExtentMode)
    {
        if (requests.Count == 0) return new ImmediateSurfaceTextResult(0, x, y);
        AdvTextStyle referenceStyle = style with { FontFace = AspectFontRealization.ReferenceFace };
        GlyphMask reference = _rasterizer.Rasterize(CreateRequests("激", referenceStyle, _policy, antialias)[0]);
        GlyphRasterRequest first = requests[0];
        int pixelHeight = first.PixelHeight;
        int baseline;
        if (style.AspectMode == 1)
            baseline = AgeGlyphMaskCompositor.CellTopToBaseline(reference, pixelHeight);
        else
        {
            // GetTextMetrics on the primary font, whatever single-byte font a glyph is drawn in; its width does not
            // change its ascent.
            GlyphRasterRequest primary = CreateRequests("激", style, _policy, antialias)[0];
            baseline = AgeGlyphMaskCompositor.CellTopToBaseline(_rasterizer.Rasterize(primary), pixelHeight);
        }

        bool effect = style.RenderMode != 0;
        int cursorX = x;
        foreach (GlyphRasterRequest request in requests)
        {
            bool singleByte = request.Cp932Code <= 0xff;
            if (IsBlank(request.Cp932Code))
            {
                int advance = effect
                    ? pixelHeight / (singleByte ? 2 : 1)
                    : reference.CellAdvanceX / (singleByte ? 2 : 1);
                if (blankExtentMode && request.Cp932Code is 0x20 or 0x8140 && (!effect || style.AspectMode == 1))
                    advance = _rasterizer.Rasterize(
                        CreateRequests(char.ConvertFromUtf32(request.UnicodeScalar), referenceStyle, _policy, antialias)[0])
                        .CellWidth;
                cursorX = checked(cursorX + advance);
                continue;
            }
            GlyphMask mask = _rasterizer.Rasterize(request);
            AgeGlyphMaskCompositor.DrawGlyph(destination, mask, cursorX, y, baseline, style, _coverageAlpha);
            cursorX = checked(cursorX + (effect ? EffectAdvance(reference, singleByte) : mask.CellAdvanceX));
        }
        return new ImmediateSurfaceTextResult(requests.Count, cursorX, y);
    }

    /// <summary>text_draw_glyph_string_effect's aspect-mode advance outside the ADV layout: the reference 激's
    /// <c>gmCellIncX</c>, halved for one byte, rounded half up (<c>_ftol</c> of <c>cell / 2.0f + 0.5</c>).</summary>
    private static int EffectAdvance(GlyphMask reference, bool singleByte) =>
        (int)(reference.CellAdvanceX / (singleByte ? 2.0f : 1.0f) + 0.5);

    /// <summary>The characters AGE's glyph-string draws skip as blanks: a space, the full-width space and the control
    /// bytes.</summary>
    private static bool IsBlank(ushort? code) => code is < 0x21 or 0x8140;

    public static IReadOnlyList<GlyphRasterRequest> CreateRequests(
        string text,
        AdvTextStyle style,
        GlyphRasterPolicy policy = GlyphRasterPolicy.NativeCp932Gray4,
        bool antialias = true,
        AspectFontRealization? realization = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        int requestedHeight = style.PrimaryFontSize > 0 ? style.PrimaryFontSize : 24;
        int pixelHeight = NativePixelHeight(requestedHeight);
        int weight = style.Bold ? 700 : 0;
        string fontFace = string.IsNullOrWhiteSpace(style.FontFace)
            ? DefaultFontFace
            : style.FontFace;
        int requestedWidth = realization?.RequestedWidth(fontFace, pixelHeight, weight, style.AspectMode)
                             ?? -(pixelHeight / 2);
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
            // Outside ruby, aspect mode draws a single-byte glyph in a font calibrated against that character
            // (text_create_calibrated_single_byte_font@0x451b70). Ruby requests come without a realization.
            int width = realization != null && cp932Code is { } code && code <= 0xff && !IsBlank(code)
                ? realization.SingleByteWidth(fontFace, pixelHeight, weight, code, style.AspectMode)
                : requestedWidth;
            requests.Add(new GlyphRasterRequest(
                fontFace, pixelHeight, width, weight,
                rune.Value, cp932Code, policy, antialias));
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

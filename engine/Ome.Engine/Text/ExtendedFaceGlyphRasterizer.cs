namespace Ome.Engine.Text;

/// <summary>
/// Draws the characters of Shift-JIS's user-defined area, codes 0xF040 to 0xF9FC, with one face whatever face the
/// text is in, as AGE 4.38D's glyph drawers do (docs/engine-re.md, "Kamidori against Arterial, triaged"): there
/// text_draw_glyph_string_plain@0x4692f0 and text_draw_glyph_string_effect@0x46b1d0 select, for such a code, the
/// font text_rebuild_primary_font_resources@0x456800 makes from the primary font's LOGFONT with the face name
/// <c>AGE Extend</c>. The font has the primary font's height and weight. Its width is <c>-height / 2</c>: aspect
/// mode calibrates only a face of the installed-font list (<see cref="AspectFontRealization"/>), and a font AGE
/// registers for its own process is not in it.
/// <para>The glyph and its box are the extended font's; the ascent is the text's own font's. Native's callers read
/// <c>tmAscent</c> with the primary font selected and pass it to the drawer, which selects the extended font only
/// around the glyph's outline, so the baseline and aspect mode's raise are the primary font's
/// (adv_text_append_horizontal_glyph_records@0x466000).</para>
/// <para>It wraps the rasterizer below the glyph cache, so every text path gets the face: live text, ruby, surface
/// strings, History and the redraw. A build names the face in <see cref="TextRules.ExtendedGlyphFace"/>.</para>
/// <para>A ruby is drawn through the same drawers with the ruby font selected, and the extended font is still the
/// primary font's: a ruby character of the range is drawn at the primary font's height and weight
/// (<see cref="GlyphRasterRequest.ExtendedFont"/>), with the ruby font's ascent, and its cell is the ruby font's,
/// which adv_text_draw_ruby_over_row@0x460760 measures with GetTextExtentPoint32A.</para>
/// </summary>
public sealed class ExtendedFaceGlyphRasterizer : IGlyphMaskRasterizer
{
    /// <summary>The first and last code drawn with the face.</summary>
    public const ushort FirstCode = 0xf040, LastCode = 0xf9fc;

    private readonly IGlyphMaskRasterizer _inner;
    private readonly string _face;

    public ExtendedFaceGlyphRasterizer(IGlyphMaskRasterizer inner, string face)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentException.ThrowIfNullOrWhiteSpace(face);
        _face = face;
    }

    /// <summary>Whether native draws a glyph with this CP932 code in the extended face: the test is
    /// <c>(ushort)(code + 0xfc0) &lt; 0x9bd</c>.</summary>
    public static bool IsExtended(ushort? code) => code is >= FirstCode and <= LastCode;

    public GlyphMask Rasterize(GlyphRasterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsExtended(request.Cp932Code)) return _inner.Rasterize(request);
        ExtendedGlyphFont font = request.ExtendedFont ?? new ExtendedGlyphFont(request.PixelHeight, request.Weight);
        GlyphMask glyph = _inner.Rasterize(new GlyphRasterRequest(
            _face, font.PixelHeight, -(font.PixelHeight / 2), font.Weight, request.UnicodeScalar,
            request.Cp932Code, request.Policy, request.Antialias)
        {
            WidthPercent = request.WidthPercent,
        });
        // The text's own font at this size: any of its glyphs carries its ascent, and a ruby's cell is its.
        GlyphMask own = _inner.Rasterize(request);
        bool rubyCell = request.ExtendedFont != null;
        int cellWidth = rubyCell ? own.CellWidth : glyph.CellWidth;
        int cellHeight = rubyCell ? own.CellHeight : glyph.CellHeight;
        return own.Ascent == glyph.Ascent && cellWidth == glyph.CellWidth && cellHeight == glyph.CellHeight
            ? glyph
            : new GlyphMask(glyph.Width, glyph.Height, glyph.Stride, glyph.OriginX, glyph.OriginY, glyph.CellAdvanceX,
                            glyph.CellAdvanceY, cellWidth, cellHeight, glyph.Coverage.Span, own.Ascent,
                            glyph.Monochrome);
    }
}

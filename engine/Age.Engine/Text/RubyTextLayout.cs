using System.Text;
using Age.Engine.Model;

namespace Age.Engine.Text;

/// <summary>
/// AGE's furigana (op 0x196, docs/engine-re.md "Furigana"). adv_text_build_glyph_records@0x4576c0 draws the base word
/// as ordinary glyphs and then hands each drawn row its share of the ruby, which adv_text_draw_ruby_over_row@0x456b40
/// spreads over the row with the ruby font. History (text_history_render_records@0x4526c0) spreads a ruby the same way
/// over the record drawn before it.
/// </summary>
public static class RubyTextLayout
{
    /// <summary>adv_text_manager_initialize copies the initial primary LOGFONT into the ruby one (+0x508), then
    /// sets the face to MS Gothic (0x577f60). Op 0x1a5 changes only the primary face, so the ruby stays MS Gothic at
    /// the default weight.</summary>
    public const string FontFace = "ＭＳ ゴシック";

    /// <summary>The ruby LOGFONT's initial height (lfHeight -8, lfWidth -4).</summary>
    public const int DefaultSize = 8;

    private static readonly Encoding Cp932 = CreateCp932();

    /// <summary>text_set_ruby_font_size@0x415c40 adjusts the size as the primary font's does: odd sizes and 32 lose
    /// one pixel, and 33 becomes 31.</summary>
    public static int PixelHeight(int rubyFontSize)
        => rubyFontSize <= 0 ? DefaultSize : ImmediateSurfaceTextRenderer.NativePixelHeight(rubyFontSize);

    public static IReadOnlyList<GlyphRasterRequest> CreateRequests(
        string ruby, AdvTextStyle style, GlyphRasterPolicy policy, bool antialias)
        => ImmediateSurfaceTextRenderer.CreateRequests(
            ruby,
            style with
            {
                PrimaryFontSize = style.RubyFontSize > 0 ? style.RubyFontSize : DefaultSize,
                Bold = false,
                FontFace = FontFace,
            },
            policy,
            antialias);

    /// <summary>
    /// Split a ruby over the base text's drawn rows as adv_text_build_glyph_records does at each horizontal wrap:
    /// a row before the last takes <c>((rubyLeft * rowBytes) / baseLeft / 2) * 2</c> bytes of the ruby, where
    /// <c>rubyLeft</c> and <c>baseLeft</c> are the CP932 bytes still undrawn from the row's start. The last row takes
    /// the rest.
    /// </summary>
    public static IReadOnlyList<string> SplitAcrossRows(string baseText, IReadOnlyList<AdvTextRow> rows, string ruby)
    {
        ArgumentNullException.ThrowIfNull(baseText);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(ruby);
        var shares = new List<string>(rows.Count);
        if (rows.Count == 0) return shares;
        int[] glyphBytes = baseText.EnumerateRunes().Select(rune => Cp932.GetByteCount(rune.ToString())).ToArray();
        byte[] rubyBytes = Cp932.GetBytes(ruby);
        int rubyAt = 0;
        for (int index = 0; index < rows.Count; index++)
        {
            int rubyLeft = rubyBytes.Length - rubyAt;
            int take = rubyLeft;
            if (index < rows.Count - 1)
            {
                AdvTextRow row = rows[index];
                int rowBytes = SumBytes(glyphBytes, row.FirstGlyph, row.GlyphCount);
                int baseLeft = SumBytes(glyphBytes, row.FirstGlyph, glyphBytes.Length - row.FirstGlyph);
                if (baseLeft > 0) take = rubyLeft * rowBytes / baseLeft / 2 * 2;
            }
            shares.Add(Cp932.GetString(rubyBytes, rubyAt, take));
            rubyAt += take;
        }
        return shares;
    }

    /// <summary>
    /// Draw a ruby over a base of <paramref name="baseWidth"/> pixels whose first glyph's cell starts at
    /// (<paramref name="baseLeft"/>, <paramref name="baseTop"/>), as adv_text_draw_ruby_over_row@0x456b40 does:
    /// <list type="bullet">
    /// <item>With the ruby narrower than the base, an equal gap of <c>(base - ruby) / (n + 1)</c> goes before, between
    /// and after its n characters (n is the CP932 length over two). Otherwise the ruby is centred and overhangs both
    /// sides by half the difference.</item>
    /// <item>The cells start at <c>baseTop + lfHeight + 1</c>, lfHeight being minus the ruby pixel height, and a mode-3
    /// effect raises them by the effect's y offset.</item>
    /// <item>Each character is drawn plain (no effect) in the text color, and its record's right/bottom are the
    /// character's own extent.</item>
    /// </list>
    /// The first record's chain flag is 0 and every later one's is 1, so the first ruby character publishes alone and
    /// the rest publish together in the next tick (adv_text_publish_next_glyph@0x451220).
    /// </summary>
    public static IReadOnlyList<AdvRetainedGlyphRecord> Draw(
        RgbaImage destination,
        IGlyphMaskRasterizer rasterizer,
        int baseLeft,
        int baseTop,
        int baseWidth,
        string ruby,
        AdvTextStyle style,
        GlyphRasterPolicy policy,
        bool antialias)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentNullException.ThrowIfNull(ruby);
        IReadOnlyList<GlyphRasterRequest> requests = CreateRequests(ruby, style, policy, antialias);
        var masks = new GlyphMask[requests.Count];
        int rubyWidth = 0;
        for (int index = 0; index < requests.Count; index++)
        {
            masks[index] = rasterizer.Rasterize(requests[index]);
            rubyWidth = checked(rubyWidth + masks[index].CellWidth);
        }

        int difference = baseWidth - rubyWidth;
        int gap = 0, overhang = 0;
        if (difference > 0)
            gap = (int)((uint)difference / (uint)(Cp932.GetByteCount(ruby) / 2 + 1));
        else
            overhang = -(difference / 2);
        int x = checked(baseLeft - overhang + gap);
        int y = checked(baseTop - PixelHeight(style.RubyFontSize) + 1);
        if (style.RenderMode == 3) y = checked(y - style.EffectOffsetY);

        AdvTextStyle plain = style with { RenderMode = 0 };
        var records = new List<AdvRetainedGlyphRecord>(requests.Count);
        for (int index = 0; index < requests.Count; index++)
        {
            GlyphMask mask = masks[index];
            int right = checked(x + mask.CellWidth);
            AgeGlyphMaskCompositor.DrawGlyph(
                destination, mask, x, y,
                AgeGlyphMaskCompositor.CellTopToBaseline(mask, requests[index].PixelHeight), plain);
            records.Add(new AdvRetainedGlyphRecord(index == 0 ? 0 : 1, x, y, right, checked(y + mask.CellHeight)));
            x = checked(right + gap);
        }
        return records;
    }

    private static int SumBytes(int[] glyphBytes, int first, int count)
    {
        int total = 0;
        for (int index = Math.Max(0, first); index < Math.Min(glyphBytes.Length, first + count); index++)
            total += glyphBytes[index];
        return total;
    }

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}

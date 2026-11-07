namespace Age.Engine.Text;

/// <summary>
/// Where SYS4433's aspect mode (op 0x2db) draws a proportional face's glyph inside its fixed reference-width cell
/// (docs/engine-re.md, "Aspect-mode glyph placement"). The glyph draws (text_draw_glyph_string_effect@0x460250 at
/// 0x460918 and text_draw_glyph_string_plain@0x45e4e0 at 0x45e815) move the glyph right from the pen when the aspect
/// mode is on, the text manager's aspect-layout flag (+0x35598) is set, the glyph is not ruby, and the primary face is
/// in the installed-font list with its fixed-pitch bit clear (<see cref="AspectFontRealization.PlacesGlyphsInCell"/>).
/// The ADV append, History and the retained redraw set that flag; immediate surface text does not.
/// </summary>
public static class AspectGlyphPlacement
{
    // The two class strings the text manager's constructor (0x4588d0) copies to +0x31484 and +0x31504, byte for byte.
    // text_classify_aspect_glyph@0x45c560 looks a double-byte glyph's lead and trail bytes up in each with strstr.
    // Class 0: closing brackets and 、。，． (the stray 0x20 after 」 is in the image).
    private static readonly byte[] KeepsPen =
    [
        0x81, 0x76, 0x01, 0x20, 0x81, 0x78, 0x01, 0x81, 0x6a, 0x01, 0x81, 0x6e, 0x01, 0x81, 0x6c, 0x01, 0x81, 0x72,
        0x01, 0x81, 0x74, 0x01, 0x81, 0x7a, 0x01, 0x81, 0x70, 0x01, 0x81, 0x41, 0x01, 0x81, 0x42, 0x01, 0x81, 0x43,
        0x01, 0x81, 0x44, 0x01,
    ];

    // Class 1: opening brackets.
    private static readonly byte[] RightAligns =
    [
        0x81, 0x75, 0x01, 0x81, 0x77, 0x01, 0x81, 0x69, 0x01, 0x81, 0x6d, 0x01, 0x81, 0x6b, 0x01, 0x81, 0x71, 0x01,
        0x81, 0x73, 0x01, 0x81, 0x79, 0x01, 0x81, 0x6f,
    ];

    /// <summary>text_classify_aspect_glyph@0x45c560: 0 for a closing bracket or 、。，．, 1 for an opening bracket, -1
    /// for any other glyph and every single-byte one.</summary>
    public static int Classify(ushort code)
    {
        if (code <= 0xff) return -1;
        ReadOnlySpan<byte> pair = [(byte)(code >> 8), (byte)code];
        if (KeepsPen.AsSpan().IndexOf(pair) >= 0) return 0;
        if (RightAligns.AsSpan().IndexOf(pair) >= 0) return 1;
        return -1;
    }

    /// <summary>
    /// How far right of the pen the glyph's origin goes. Mode 1 keeps the pen for class 0, puts the black box's right
    /// edge at the reference 激's black-box width for class 1, and otherwise centres the black box on half the font
    /// size (a quarter for a single-byte glyph): <c>trunc(size / 2 + 0.5) - trunc(gmBlackBoxX * 0.5 + 0.5) -
    /// gmptGlyphOrigin.x</c>. Any other nonzero mode always centres. AGE also multiplies the size and the reference
    /// width by a display scale (+0x40c->+0x4e0, 1.0 unless +0x488 is clear), which the port does not have.
    /// </summary>
    public static int OffsetX(GlyphMask glyph, GlyphMask reference, int fontSize, ushort? code, int aspectMode)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(reference);
        int classification = aspectMode == 1 && code is { } value ? Classify(value) : -1;
        if (classification == 0) return 0;
        if (classification == 1) return (int)(reference.Width + 0.5) - glyph.OriginX - glyph.Width;
        float centre = fontSize / (code is null or <= 0xff ? 4.0f : 2.0f);
        return (int)(centre + 0.5) - (int)(glyph.Width * 0.5 + 0.5) - glyph.OriginX;
    }
}

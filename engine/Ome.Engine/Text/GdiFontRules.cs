using System.Text;

namespace Ome.Engine.Text;

/// <summary>The part of GDI's <c>TEXTMETRIC</c> that AGE reads, for one font request, in pixels.</summary>
public readonly record struct GdiTextMetrics(int Ascent, int Descent, int AverageCharWidth)
{
    /// <summary><c>tmHeight</c>, the ascent plus the descent.</summary>
    public int Height => Ascent + Descent;
}

/// <summary>
/// What GDI derives from a font file, on the Japanese-locale Windows AGE runs under (system code page 932), for a
/// request with <c>lfHeight</c> = -height, so the height is the em in pixels. Each rule was fitted against GDI on
/// Windows 10 over every installed Japanese face and checked by the Windows oracle tests
/// (<c>WindowsGdiFontRulesTests</c>; docs/engine-re.md, "What GDI derives from a font file"):
/// <list type="bullet">
/// <item>The font list names a static face by its family (name 1) in Japanese, else in US English; a variable face
/// by each named instance (<see cref="Families"/>). It lists under Shift-JIS a face whose code page ranges include
/// code page 932.</item>
/// <item>A face is fixed-pitch when its PANOSE proportion is monospaced (9), or, with TrueType outlines, when
/// <c>post.isFixedPitch</c> is set; the family bits come from the PANOSE family type, serif style and proportion, by
/// one rule for TrueType outlines and another for CFF (<see cref="PitchAndFamily"/>).</item>
/// <item><c>tmAveCharWidth</c> is <c>xAvgCharWidth</c> scaled to the height, rounded half up; a request's width
/// replaces it, and simulated bold adds a pixel. The ascent and descent are the <c>VDMX</c> record at the height,
/// else <c>usWinAscent</c> and <c>usWinDescent</c> scaled and rounded half up.</item>
/// <item>A request's width scales the face horizontally by <c>|width|</c> over its average width at the height,
/// unrounded (<see cref="HorizontalScale"/>). A monospaced face's advances are whole multiples of the average width.
/// </item>
/// <item><c>GetGlyphOutline</c>'s metrics come from the embedded bitmap where the face has a strike at the height
/// and the request is not scaled (<see cref="MeasuredBitmap"/>): the bitmap's inked box and advance.</item>
/// </list>
/// </summary>
public static class GdiFontRules
{
    public const ushort Japanese = 0x0411;
    public const ushort UnitedStatesEnglish = 0x0409;

    /// <summary>Windows' replacement for a CP932 byte sequence it cannot decode (katakana middle dot).</summary>
    public const char Cp932Replacement = '・';

    private const uint Cp932CodePage = 1u << 17;
    private const byte FixedPitch = 1, VariablePitch = 2;
    private const byte Roman = 0x10, Swiss = 0x20, Modern = 0x30, Script = 0x40, Decorative = 0x50;
    private static readonly Encoding Cp932 = CreateCp932();
    private static readonly string[] RegularStyles = ["Regular", "Bold", "Italic", "Bold Italic"];

    /// <summary>
    /// The families GDI lists <paramref name="face"/> under, with the named instance each stands for. A static face
    /// is listed under its family. A variable face is listed once per named instance: Regular, Bold, Italic and
    /// Bold Italic under the typographic family (name 16, else 1), any other under that family and the instance's
    /// subfamily name ("Noto Sans JP Thin"). Names are Japanese where the face has them, else US English.
    /// </summary>
    public static IReadOnlyList<(string Family, OpenTypeNamedInstance? Instance)> Families(OpenTypeFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (face.NamedInstances.Count == 0)
            return face.Name(1, Japanese, UnitedStatesEnglish) is { } family ? [(family, null)] : [];
        var families = new List<(string, OpenTypeNamedInstance?)>();
        foreach (OpenTypeNamedInstance instance in face.NamedInstances)
            if (InstanceFamily(face, instance, Japanese, UnitedStatesEnglish) is { } family)
                families.Add((family, instance));
        return families;
    }

    /// <summary>Every name GDI finds <paramref name="face"/>'s family by, in any language: its family (name 1) and
    /// typographic family (16), or for a variable face its instances' families (<see cref="Families"/>).</summary>
    public static IEnumerable<(string Name, OpenTypeNamedInstance? Instance)> FamilyNames(OpenTypeFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (face.NamedInstances.Count == 0)
        {
            foreach (OpenTypeName name in face.Names)
                if (name.NameId is 1 or 16) yield return (name.Value, null);
            yield break;
        }
        var languages = face.Names.Where(name => name.NameId is 1 or 16).Select(name => name.LanguageId).Distinct();
        foreach (ushort language in languages)
            foreach (OpenTypeNamedInstance instance in face.NamedInstances)
                if (InstanceFamily(face, instance, language) is { } family) yield return (family, instance);
    }

    /// <summary>GDI's fixed-pitch bit: a monospaced PANOSE proportion, or, for a face with TrueType outlines,
    /// <c>post.isFixedPitch</c>. GDI ignores <c>post.isFixedPitch</c> in a CFF face.</summary>
    public static bool IsFixedPitch(OpenTypeFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return IsMonospaced(face) || (face.PostFixedPitch && !face.HasCffOutlines);
    }

    /// <summary>A monospaced PANOSE proportion (9): GDI gives such a face's glyphs advances that are whole multiples of
    /// its average width (<see cref="MonospacedAdvance"/>). BIZ UDゴシック, fixed-pitch by <c>post</c> alone, keeps
    /// its own advances.</summary>
    public static bool IsMonospaced(OpenTypeFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return face.Panose[3] == 9;
    }

    /// <summary>The <c>lfPitchAndFamily</c> GDI's Shift-JIS font list gives the face: FIXED_PITCH or VARIABLE_PITCH,
    /// and a family from the PANOSE family type (byte 0), serif style (byte 1) and proportion (byte 3). FF_SCRIPT for
    /// the script type (3). Otherwise, with TrueType outlines: FF_ROMAN for a serif style of 2 to 10, FF_MODERN for 11
    /// to 15, FF_DONTCARE for any other. With CFF outlines: FF_DECORATIVE for the decorative and pictorial types (4,
    /// 5), FF_MODERN for a monospaced proportion, then FF_ROMAN for a serif style of 2 to 10, FF_SWISS for 11 to 15,
    /// FF_MODERN for any other. AGE reads only the pitch bit.</summary>
    public static byte PitchAndFamily(OpenTypeFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        byte type = face.Panose[0], serif = face.Panose[1];
        byte family = type == 3 ? Script
            : !face.HasCffOutlines ? serif switch { >= 2 and <= 10 => Roman, >= 11 and <= 15 => Modern, _ => (byte)0 }
            : type is 4 or 5 ? Decorative
            : IsMonospaced(face) ? Modern
            : serif switch { >= 2 and <= 10 => Roman, >= 11 and <= 15 => Swiss, _ => Modern };
        return (byte)(family | (IsFixedPitch(face) ? FixedPitch : VariablePitch));
    }

    /// <summary>Whether GDI lists the face under SHIFTJIS_CHARSET: its code page ranges (<c>OS/2</c> version 1 and
    /// later) include code page 932. Of the 24 faces on the test machine that declare no code pages, GDI listed one
    /// (AquilineTwo, a decorative Latin face with no Japanese glyphs) for a reason its tables do not show; this rule
    /// leaves all of them out.</summary>
    public static bool ListsUnderShiftJis(OpenTypeFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return face.HasOs2 && face.Os2Version >= 1 && (face.CodePageRange1 & Cp932CodePage) != 0;
    }

    /// <summary><c>tmAveCharWidth</c> of the face at its own width: <c>xAvgCharWidth</c> scaled to the height,
    /// rounded half up; half the height for a face that records no average.</summary>
    public static int AverageCharWidth(OpenTypeFace face, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (face.AverageCharWidth <= 0) return pixelHeight / 2;
        return RoundHalfUp((double)face.AverageCharWidth * pixelHeight / face.UnitsPerEm);
    }

    /// <summary><c>GetTextMetrics</c> for the request {height, <paramref name="width"/>}; a nonzero width is the
    /// average width GDI reports, and <paramref name="simulatedBold"/> adds a pixel to it. A TrueType face rounds its
    /// ascent and descent; a CFF face rounds its descent and its whole height, and the ascent is what remains.
    /// </summary>
    public static GdiTextMetrics TextMetrics(OpenTypeFace face, int pixelHeight, int width, bool simulatedBold)
    {
        ArgumentNullException.ThrowIfNull(face);
        int average = (width != 0 ? Math.Abs(width) : AverageCharWidth(face, pixelHeight)) + (simulatedBold ? 1 : 0);
        if (face.VerticalDeviceMetrics(pixelHeight) is { } device)
            return new GdiTextMetrics(device.YMax, -device.YMin, average);
        double scale = (double)pixelHeight / face.UnitsPerEm;
        bool windows = face.HasOs2 && face.WindowsAscent + face.WindowsDescent > 0;
        int ascent = windows ? face.WindowsAscent : face.HorizontalAscender;
        int descent = windows ? face.WindowsDescent : -face.HorizontalDescender;
        int descentPixels = RoundHalfUp(descent * scale);
        int ascentPixels = face.HasCffOutlines ? RoundHalfUp((ascent + descent) * scale) - descentPixels
                                               : RoundHalfUp(ascent * scale);
        return new GdiTextMetrics(ascentPixels, descentPixels, average);
    }

    /// <summary>How much a request's width scales the face horizontally: <c>|width|</c> over the face's average width
    /// at the height before rounding (<c>xAvgCharWidth * height / unitsPerEm</c>), 1 for width 0. ＭＳ ゴシック at 21
    /// pixels averages 10.5, so its <c>-height / 2</c> of -10 draws at 10/10.5; メイリオ at 22 pixels averages 20.6, so -11
    /// draws at about half its own width.</summary>
    public static double HorizontalScale(OpenTypeFace face, int pixelHeight, int width)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (width == 0) return 1;
        if (face.AverageCharWidth <= 0) return (double)Math.Abs(width) / Math.Max(1, pixelHeight / 2);
        // Compared in font units so that an exact width (ＭＳ ゴシック's -11 at 22) scales by exactly 1.
        long requested = (long)Math.Abs(width) * face.UnitsPerEm, average = (long)face.AverageCharWidth * pixelHeight;
        return requested == average ? 1 : (double)requested / average;
    }

    /// <summary>A monospaced face's advance for a glyph whose advance at the face's own width is
    /// <paramref name="naturalAdvance"/>: the number of average widths it spans (1 or 2) times the request's average
    /// width, so a full-width ＭＳ ゴシック glyph at 21 pixels advances 22, not 21. Null for any other face, or a glyph
    /// that does not advance. Simulated bold's extra pixel comes on top.</summary>
    public static int? MonospacedAdvance(OpenTypeFace face, int pixelHeight, int width, int naturalAdvance)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (!IsMonospaced(face) || naturalAdvance <= 0) return null;
        int natural = AverageCharWidth(face, pixelHeight);
        if (natural <= 0) return null;
        int cells = Math.Max(1, (int)Math.Round((double)naturalAdvance / natural, MidpointRounding.AwayFromZero));
        return cells * (width != 0 ? Math.Abs(width) : natural);
    }

    /// <summary>The embedded bitmap <c>GetGlyphOutline(GGO_METRICS)</c> measures <paramref name="glyph"/> by: the
    /// face's strike at the height when the request does not scale the face. Its black box is the bitmap's inked box
    /// and its origin the bearing plus the ink's offset; null when GDI measures the outline.</summary>
    public static EmbeddedBitmapGlyph? MeasuredBitmap(OpenTypeFace face, int glyph, int pixelHeight, int width)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (HorizontalScale(face, pixelHeight, width) != 1 || !face.HasBitmapStrike(pixelHeight)) return null;
        return face.BitmapGlyph(pixelHeight, glyph);
    }

    /// <summary>The character GDI's ANSI calls take CP932 <paramref name="code"/> for: a code above 0xff is a lead and
    /// a trail byte. A sequence CP932 does not define becomes U+30FB, as Windows decodes it.</summary>
    public static int Cp932Character(ushort code)
    {
        byte[] bytes = code > 0xff ? [(byte)(code >> 8), (byte)code] : [(byte)code];
        string text = Cp932.GetString(bytes);
        return text.Length == 0 ? Cp932Replacement : char.ConvertToUtf32(text, 0);
    }

    /// <summary>
    /// The glyph GDI draws CP932 <paramref name="code"/> with in the face: the character map's glyph for its character
    /// (<see cref="Cp932Character"/>), else the face's default character's, else <c>.notdef</c>.
    /// </summary>
    public static int GlyphFor(OpenTypeFace face, ushort code)
    {
        ArgumentNullException.ThrowIfNull(face);
        int glyph = face.GlyphIndex(Cp932Character(code));
        if (glyph == 0 && face.DefaultChar != 0) glyph = face.GlyphIndex(face.DefaultChar);
        return glyph;
    }

    /// <summary>
    /// SYS4433's VALIDFONTCHECKER probe (<see cref="IFontFamilySource.HasGlyph"/>): GGO_METRICS at <c>lfHeight</c> -64
    /// reports a black box of 2 pixels or more in either direction. A glyph whose outline spans at least one pixel at
    /// 64 pixels per em passes; an empty glyph (a blank <c>.notdef</c>) does not.
    /// </summary>
    public static bool HasCheckerGlyph(OpenTypeFace face, ushort code)
    {
        ArgumentNullException.ThrowIfNull(face);
        return face.HasInk(GlyphFor(face, code), Math.Max(1, face.UnitsPerEm / 64));
    }

    private static string? InstanceFamily(OpenTypeFace face, OpenTypeNamedInstance instance, params ushort[] languages)
    {
        string? family = face.Name(16, languages) ?? face.Name(1, languages);
        if (family == null) return null;
        // Whether the instance is one of the four styles a family holds is read from its English name.
        string? english = face.Name(instance.SubfamilyNameId, UnitedStatesEnglish);
        string? style = face.Name(instance.SubfamilyNameId, languages);
        return english == null || style == null || RegularStyles.Contains(english, StringComparer.OrdinalIgnoreCase)
            ? family
            : $"{family} {style}";
    }

    private static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ReplacementFallback,
                                    new DecoderReplacementFallback(Cp932Replacement.ToString()));
    }
}

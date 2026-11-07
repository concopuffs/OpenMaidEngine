using System.Text;

namespace Age.Engine.Text;

/// <summary>One family that SYS4433's EnumFontFamiliesExA callback (0x455010) kept: a face name and its
/// <c>lfPitchAndFamily</c>.</summary>
public readonly record struct EnumeratedFontFamily(string FaceName, byte PitchAndFamily);

/// <summary>The system queries behind SYS4433's installed-font list (docs/engine-re.md, "Font family list").</summary>
public interface IFontFamilySource
{
    /// <summary>EnumFontFamiliesExA over SHIFTJIS_CHARSET (lfOutPrecision 8, empty face) on a display IC, in GDI's
    /// order. The native callback keeps a family when its FontType is not exactly RASTER_FONTTYPE (1), its
    /// <c>lfCharSet</c> is SHIFTJIS_CHARSET and its name does not start with '@'.</summary>
    IReadOnlyList<EnumeratedFontFamily> EnumerateShiftJisFamilies();

    /// <summary>The value names of <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink</c>, as
    /// the 32-bit AGE reads them.</summary>
    IReadOnlyList<string> SystemLinkFaceNames();

    /// <summary>True unless GetGlyphOutlineA(GGO_METRICS) gives the code a black box under 2x2 pixels in the face at
    /// lfHeight -64, lfWidth -32 and DEFAULT_CHARSET.</summary>
    bool HasGlyph(string faceName, ushort code);
}

/// <summary>
/// SYS4433's installed-font list, which ops 0x2dc (count), 0x2dd (name) and 0x2de (find) read. The startup reader
/// (0x415a20) builds it once with text_build_font_family_list@0x458040:
/// <list type="number">
/// <item>The Shift-JIS families GDI enumerates (<see cref="IFontFamilySource.EnumerateShiftJisFamilies"/>).</item>
/// <item>Every FontLink SystemLink value name except ten Japanese families (<see cref="KeptSystemLinkFaces"/>) is
/// removed: those are Latin faces with linked Japanese fallbacks.</item>
/// <item>With <c>set:UseProportionalFont</c> 0, IPA Pゴシック, IPA P明朝 and every family whose pitch is not
/// FIXED_PITCH are removed.</item>
/// <item>A family that lacks any <c>VALIDFONTCHECKER</c> character is removed (<see cref="CheckerCode"/>).</item>
/// </list>
/// Removal keeps the order of the rest. Each family keeps its <c>lfPitchAndFamily</c>, which the aspect-mode font
/// calibration reads (<see cref="AspectFontRealization"/>).
/// </summary>
public static class FontFamilyCatalog
{
    /// <summary>The SystemLink value names the list keeps (0x470bc0's table at 0x57ef7c..0x57eff4).</summary>
    public static readonly IReadOnlyList<string> KeptSystemLinkFaces =
    [
        "Meiryo", "Meiryo Bold", "Meiryo UI", "Meiryo UI Bold", "MS Gothic", "MS PGothic", "MS PGothic MC",
        "MS UI Gothic", "MS Mincho", "MS PMincho",
    ];

    /// <summary>The proportional IPA faces removed by name when proportional fonts are off (0x57d124, 0x57d118).</summary>
    public static readonly IReadOnlyList<string> ProportionalIpaFaces = ["IPA Pゴシック", "IPA P明朝"];

    private const byte FixedPitch = 1;
    private static readonly Encoding Cp932 = CreateCp932();

    public static IReadOnlyList<EnumeratedFontFamily> Build(
        IFontFamilySource source, bool useProportionalFont, IEnumerable<string> validFontCheckers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(validFontCheckers);
        var families = source.EnumerateShiftJisFamilies().ToList();
        foreach (string linked in source.SystemLinkFaceNames())
        {
            if (KeptSystemLinkFaces.Contains(linked, StringComparer.Ordinal)) continue;
            families.RemoveAll(family => string.Equals(family.FaceName, linked, StringComparison.Ordinal));
        }
        if (!useProportionalFont)
            families.RemoveAll(family => ProportionalIpaFaces.Contains(family.FaceName, StringComparer.Ordinal)
                                         || (family.PitchAndFamily & FixedPitch) == 0);
        ushort[] codes = validFontCheckers.Select(CheckerCode).ToArray();
        if (codes.Length > 0)
            families.RemoveAll(family => codes.Any(code => !source.HasGlyph(family.FaceName, code)));
        return families.ToArray();
    }

    /// <summary>
    /// The glyph code text_set_valid_font_checkers@0x40b060 stores for one VALIDFONTCHECKER value: a one-byte value
    /// is its byte; otherwise the first two CP932 bytes combine as <c>b0 * 256 + (signed)b1</c> in 16 bits. The
    /// second byte is sign-extended, so a trail byte of 0x80 or more lowers the lead byte by one: 悪 (0x88AB) becomes
    /// 0x87AB and 洒 (0x9FAD) becomes 0x9EAD, and GDI is asked for those codes.
    /// </summary>
    public static ushort CheckerCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Cp932.GetBytes(value);
        if (bytes.Length == 1) return bytes[0];
        int lead = bytes.Length > 0 ? bytes[0] : 0;
        int trail = bytes.Length > 1 ? (sbyte)bytes[1] : 0;
        return unchecked((ushort)(lead * 256 + trail));
    }

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}

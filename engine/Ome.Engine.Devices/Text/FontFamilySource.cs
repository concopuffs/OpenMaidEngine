namespace Ome.Engine.Text;

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

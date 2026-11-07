namespace Age.Engine.Text;

/// <summary>GDI measurements of one CP932 glyph: GetTextMetricsA's <c>tmAveCharWidth</c> and GetGlyphOutlineA
/// (GGO_METRICS)'s <c>gmBlackBoxX</c> and <c>gmCellIncX</c>.</summary>
public readonly record struct FontProbeMetrics(int AverageCharWidth, uint BlackBoxX, short CellIncrementX);

/// <summary>Measures glyphs the way SYS4433's font calibration does.</summary>
public interface IFontMetricsProbe
{
    /// <summary>Measure <paramref name="code"/> in the LOGFONT {lfHeight -<paramref name="pixelHeight"/>, lfWidth
    /// <paramref name="width"/>, lfWeight <paramref name="weight"/>, DEFAULT_CHARSET, face} on a display IC.</summary>
    FontProbeMetrics Measure(string faceName, int pixelHeight, int width, int weight, ushort code);
}

/// <summary>
/// The primary font's <c>lfWidth</c> under SYS4433's text aspect mode (op 0x2db; docs/engine-re.md, "Aspect-mode font
/// calibration"). text_rebuild_primary_font_resources@0x452320 rebuilds the font whenever its face, size, weight or
/// the mode changes, and text_measure_glyph_with_aspect_mode@0x452080 picks its width:
/// <list type="bullet">
/// <item>With the mode off, or a face that is fixed-pitch in the installed-font list (<see cref="FontFamilyCatalog"/>)
/// and not IPA Pゴシック or IPA P明朝, or a face missing from the list, the width is <c>-height / 2</c>, as for any
/// AGE font.</item>
/// <item>Otherwise mode 1 calibrates it from 激 (text_calibrate_mode1_glyph_font@0x451340): with <c>ref</c> the
/// reference glyph (ＭＳ ゴシック at the size, half width and the weight) and <c>nat</c> the face's glyph at its natural
/// width (lfWidth 0), the width is <c>ref.gmBlackBoxX / nat.gmCellIncX * -nat.tmAveCharWidth</c> when the reference
/// black box is no wider, and <c>nat.gmBlackBoxX / ref.gmCellIncX * -nat.tmAveCharWidth</c> otherwise, truncated.
/// Kamidori's メイリオ bold comes out at -18 for both 20 and 22 pixels.</item>
/// </list>
/// Not modelled: mode 2 (text_calibrate_mode2_glyph_font@0x451910), which no shipped game selects; the
/// <c>display:AspectMode</c> setting, which calibrates every face when nonzero; and the separate single-byte font
/// (text_create_calibrated_single_byte_font@0x451b70).
/// </summary>
public sealed class AspectFontRealization
{
    /// <summary>CP932 激, the glyph both the reference metrics and the calibration measure.</summary>
    public const ushort ReferenceCode = 0x8c83;

    /// <summary>The reference face (text_measure_reference_gothic_glyph@0x44ff60).</summary>
    public const string ReferenceFace = "ＭＳ ゴシック";

    private readonly Dictionary<string, byte> _pitchByFace = new(StringComparer.Ordinal);
    private readonly IFontMetricsProbe _probe;
    private readonly Dictionary<(string Face, int Height, int Weight), int> _calibrated = new();
    private readonly object _gate = new();

    public AspectFontRealization(IReadOnlyList<EnumeratedFontFamily> installedFamilies, IFontMetricsProbe probe)
    {
        ArgumentNullException.ThrowIfNull(installedFamilies);
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        // text_find_font_family_index@0x423a50 returns the first exact match.
        foreach (EnumeratedFontFamily family in installedFamilies)
            _pitchByFace.TryAdd(family.FaceName, family.PitchAndFamily);
    }

    /// <summary>The <c>lfWidth</c> AGE creates the primary font with.</summary>
    public int RequestedWidth(string faceName, int pixelHeight, int weight, int aspectMode)
    {
        ArgumentNullException.ThrowIfNull(faceName);
        int uncalibrated = -(pixelHeight / 2);
        if (aspectMode != 1 || !Calibrates(faceName)) return uncalibrated;
        lock (_gate)
        {
            if (!_calibrated.TryGetValue((faceName, pixelHeight, weight), out int width))
            {
                width = CalibrateMode1(faceName, pixelHeight, weight) ?? uncalibrated;
                _calibrated.Add((faceName, pixelHeight, weight), width);
            }
            return width;
        }
    }

    /// <summary>Whether aspect mode places this face's glyphs inside their cell (<see cref="AspectGlyphPlacement"/>):
    /// the face is in the installed-font list and its fixed-pitch bit is clear. Unlike the calibration, the IPA
    /// proportional faces get no exception.</summary>
    public bool PlacesGlyphsInCell(string faceName)
    {
        ArgumentNullException.ThrowIfNull(faceName);
        return TryGetPitch(faceName, out byte pitch) && (pitch & 1) == 0;
    }

    private bool Calibrates(string faceName)
        => TryGetPitch(faceName, out byte pitch)
           && ((pitch & 1) == 0 || FontFamilyCatalog.ProportionalIpaFaces.Contains(faceName, StringComparer.Ordinal));

    // text_find_font_family_index@0x423a50 skips one leading '@'.
    private bool TryGetPitch(string faceName, out byte pitch)
        => _pitchByFace.TryGetValue(faceName.StartsWith('@') ? faceName[1..] : faceName, out pitch);

    private int? CalibrateMode1(string faceName, int pixelHeight, int weight)
    {
        FontProbeMetrics reference = _probe.Measure(ReferenceFace, pixelHeight, -(pixelHeight / 2), weight, ReferenceCode);
        FontProbeMetrics natural = _probe.Measure(faceName, pixelHeight, 0, weight, ReferenceCode);
        double width = reference.BlackBoxX <= natural.BlackBoxX
            ? (double)reference.BlackBoxX / natural.CellIncrementX * -natural.AverageCharWidth
            : (double)natural.BlackBoxX / reference.CellIncrementX * -natural.AverageCharWidth;
        return double.IsFinite(width) ? (int)width : null;
    }
}

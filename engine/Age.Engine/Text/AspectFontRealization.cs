namespace Age.Engine.Text;

/// <summary>GDI measurements of one CP932 glyph: GetTextMetricsA's <c>tmAveCharWidth</c> and <c>tmHeight</c>, and
/// GetGlyphOutlineA (GGO_METRICS)'s <c>gmBlackBoxX</c> and <c>gmCellIncX</c>.</summary>
public readonly record struct FontProbeMetrics(int AverageCharWidth, uint BlackBoxX, short CellIncrementX,
                                               int Height = 0);

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
/// A single-byte glyph is drawn in a font calibrated against itself (<see cref="SingleByteWidth"/>).
/// Not modelled: mode 2 (text_calibrate_mode2_glyph_font@0x451910), which no shipped game selects; and the
/// <c>display:AspectMode</c> setting, which calibrates every face when nonzero.
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
    private readonly Dictionary<(string Face, int Height, int Weight, ushort Code), int> _singleByte = new();
    private readonly Dictionary<(int Height, int Weight), int> _referenceBlackBox = new();
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

    /// <summary>
    /// The <c>lfWidth</c> of the font AGE draws single-byte glyph <paramref name="code"/> with in aspect mode, outside
    /// ruby. text_create_calibrated_single_byte_font@0x451b70 calibrates the primary font against that very character
    /// (text_calibrate_mode1_glyph_font@0x451340's single-byte branch). With <c>nat</c> the face at its natural width
    /// and <c>ref</c> the reference 激:
    /// <list type="bullet">
    /// <item><c>font2</c> is the face at character height <c>trunc(size * size / nat.tmHeight)</c>, a cell about
    /// <c>size</c> tall, at its natural width;</item>
    /// <item>the width is <c>-font2.tmAveCharWidth</c>, unless the glyph's black box in <c>font2</c> is wider than
    /// half of 激's: then <c>trunc((float)(ref.gmBlackBoxX * 0.5) / font2.gmBlackBoxX * -font2.tmAveCharWidth)</c>.</item>
    /// </list>
    /// The font keeps the height. The same faces as <see cref="RequestedWidth"/>'s are calibrated; any other gets
    /// <c>-height / 2</c>.
    /// </summary>
    public int SingleByteWidth(string faceName, int pixelHeight, int weight, ushort code, int aspectMode)
    {
        ArgumentNullException.ThrowIfNull(faceName);
        if (code > 0xff) throw new ArgumentOutOfRangeException(nameof(code), "Not a single-byte CP932 code.");
        int uncalibrated = -(pixelHeight / 2);
        if (aspectMode != 1 || !Calibrates(faceName)) return uncalibrated;
        lock (_gate)
        {
            var key = (faceName, pixelHeight, weight, code);
            if (!_singleByte.TryGetValue(key, out int width))
            {
                width = CalibrateSingleByteMode1(faceName, pixelHeight, weight, code) ?? uncalibrated;
                _singleByte.Add(key, width);
            }
            return width;
        }
    }

    /// <summary>
    /// The reference 激's <c>gmBlackBoxX</c> at <c>+0x313d8</c>, which an opening bracket's black box ends at
    /// (<see cref="AspectGlyphPlacement"/>). text_rebuild_reference_gothic_metrics@0x44fe00 measures it with
    /// GGO_METRICS in ＭＳ ゴシック at the font size, half width and the primary weight, at DEFAULT_QUALITY. At the sizes
    /// where the face has an embedded bitmap that is the bitmap's box: 19 pixels for bold at 22, where an antialiased
    /// raster's is 21.
    /// </summary>
    public int ReferenceBlackBoxWidth(int pixelHeight, int weight)
    {
        lock (_gate)
        {
            if (!_referenceBlackBox.TryGetValue((pixelHeight, weight), out int width))
            {
                width = (int)_probe.Measure(ReferenceFace, pixelHeight, -(pixelHeight / 2), weight, ReferenceCode).BlackBoxX;
                _referenceBlackBox.Add((pixelHeight, weight), width);
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

    private int? CalibrateSingleByteMode1(string faceName, int pixelHeight, int weight, ushort code)
    {
        FontProbeMetrics natural = _probe.Measure(faceName, pixelHeight, 0, weight, code);
        if (natural.Height <= 0) return null;
        // lfHeight = trunc(size / nat.tmHeight * -size): a negative (character) height.
        int smallHeight = (int)((double)pixelHeight / natural.Height * pixelHeight);
        if (smallHeight <= 0) return null;
        FontProbeMetrics small = _probe.Measure(faceName, smallHeight, 0, weight, code);
        FontProbeMetrics reference = _probe.Measure(ReferenceFace, pixelHeight, -(pixelHeight / 2), weight, ReferenceCode);
        if (reference.BlackBoxX >> 1 >= small.BlackBoxX) return -small.AverageCharWidth;
        // The half black box is stored as a float before the division.
        double width = (float)(reference.BlackBoxX * 0.5) / (double)small.BlackBoxX * -small.AverageCharWidth;
        return double.IsFinite(width) ? (int)width : null;
    }

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

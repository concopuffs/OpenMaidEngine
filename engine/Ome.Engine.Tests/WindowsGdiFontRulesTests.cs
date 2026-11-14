using Ome.Engine.Text;
using Ome.Engine.Text.Windows;

/// <summary>
/// The portable font rules (<see cref="GdiFontRules"/>, <see cref="OpenTypeFontFamilySource"/>) held to GDI over the
/// fonts installed on this machine: the rules were fitted this way (docs/engine-re.md, "What GDI derives from a font
/// file"), and these tests keep them honest wherever they run on Windows. Elsewhere they pass without checking.
/// Variable faces are left out of the metrics: the rules read the default instance's tables.
/// </summary>
public class WindowsGdiFontRulesTests
{
    private static readonly string[] Checkers = ["悪", "ｱ", "洒", "A", "﨑", "　"];
    private static readonly int[] Heights = [8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 21, 22, 24, 26, 28, 32, 40, 48];

    private static readonly Lazy<InstalledFontIndex> Installed =
        new(() => InstalledFontIndex.Scan(SystemFontFiles.Enumerate(SystemFontFiles.Directories())));

    private static bool OnWindows => OperatingSystem.IsWindows() && WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out _);

    [Fact]
    public void ThePortableFontListIsGdisSaveFacesThatDeclareNoCodePages()
    {
        if (!OnWindows || !OperatingSystem.IsWindows()) return;
        var portable = new OpenTypeFontFamilySource(Installed.Value);
        using var gdi = new WindowsGdiFontFamilySource();
        IReadOnlyList<EnumeratedFontFamily> listed = gdi.EnumerateShiftJisFamilies();
        IReadOnlyList<EnumeratedFontFamily> ours = portable.EnumerateShiftJisFamilies();
        var ourPitch = ours.GroupBy(family => family.FaceName, StringComparer.Ordinal)
                           .ToDictionary(group => group.Key, group => group.First().PitchAndFamily, StringComparer.Ordinal);

        // GDI also lists the FontLink faces (their linked Japanese fonts answer Shift-JIS), which AGE removes, and now
        // and then a face that declares no code pages (AquilineTwo on the machine the rules were fitted on), for a
        // reason its tables do not show.
        var linked = gdi.SystemLinkFaceNames().ToHashSet(StringComparer.Ordinal);
        foreach (EnumeratedFontFamily family in listed.Where(family => !ourPitch.ContainsKey(family.FaceName)
                                                                      && !linked.Contains(family.FaceName)))
            Assert.True(Installed.Value.Family(family.FaceName) is { } installed
                        && installed.Faces.All(face => face.Face.CodePageRange1 == 0 && face.Face.CodePageRange2 == 0),
                        $"GDI lists {family.FaceName}, which the portable list leaves out.");
        Assert.Empty(ourPitch.Keys.Except(listed.Select(family => family.FaceName), StringComparer.Ordinal));
        var common = listed.Where(family => ourPitch.ContainsKey(family.FaceName)).ToList();
        Assert.All(common, family => Assert.True(family.PitchAndFamily == ourPitch[family.FaceName],
                                                 $"{family.FaceName}: GDI {family.PitchAndFamily}, " +
                                                 $"portable {ourPitch[family.FaceName]}"));
        foreach (string checker in Checkers)
        {
            ushort code = FontFamilyCatalog.CheckerCode(checker);
            foreach (EnumeratedFontFamily family in common)
                Assert.True(gdi.HasGlyph(family.FaceName, code) == portable.HasGlyph(family.FaceName, code),
                            $"{family.FaceName} checker {checker} (0x{code:x4})");
        }
        var catalog = FontFamilyCatalog.Build(gdi, true, Checkers).Select(family => family.FaceName)
                                       .Where(ourPitch.ContainsKey).ToHashSet(StringComparer.Ordinal);
        Assert.True(catalog.SetEquals(FontFamilyCatalog.Build(portable, true, Checkers).Select(family => family.FaceName)));
    }

    [Fact]
    public void TheMetricRulesAreGdisForTheInstalledJapaneseFaces()
    {
        if (!OnWindows || !OperatingSystem.IsWindows()) return;
        using var gdi = new WindowsGdiGlyphMaskRasterizer();
        using var source = new WindowsGdiFontFamilySource();
        var mismatches = new List<string>();
        int rows = 0;
        foreach (InstalledFontFamily family in StaticFamilies(source))
        foreach (int weight in new[] { 400, 700 })
        {
            InstalledFace face = Installed.Value.Resolve(family.Name, weight)!;
            bool simulated = weight >= 600 && face.Weight < 600;
            int glyph = face.Face.GlyphIndex('激');
            foreach (int height in Heights)
            foreach (int width in new[] { 0, -(height / 2) })
            {
                rows++;
                string at = $"{family.Name} {height} px weight {weight} width {width}";
                FontProbeMetrics measured = gdi.Measure(family.Name, height, width, weight, 0x8c83);
                GdiTextMetrics rules = GdiFontRules.TextMetrics(face.Face, height, width, simulated);
                GlyphMask mask = gdi.Rasterize(new GlyphRasterRequest(family.Name, height, width, weight, '激', 0x8c83,
                                                                      GlyphRasterPolicy.NativeCp932Gray4));
                if (measured.AverageCharWidth != rules.AverageCharWidth)
                    mismatches.Add($"{at}: tmAveCharWidth GDI {measured.AverageCharWidth}, rule {rules.AverageCharWidth}");
                if (measured.Height != rules.Height) mismatches.Add($"{at}: tmHeight GDI {measured.Height}, rule {rules.Height}");
                if (mask.Ascent != rules.Ascent) mismatches.Add($"{at}: tmAscent GDI {mask.Ascent}, rule {rules.Ascent}");
                if (glyph == 0) continue;
                FontProbeMetrics natural = gdi.Measure(family.Name, height, 0, 400, 0x8c83);
                if (GdiFontRules.MonospacedAdvance(face.Face, height, width, natural.CellIncrementX) is { } advance
                    && advance + (simulated ? 1 : 0) != measured.CellIncrementX)
                    mismatches.Add($"{at}: advance GDI {measured.CellIncrementX}, rule {advance + (simulated ? 1 : 0)}");
                if (GdiFontRules.MeasuredBitmap(face.Face, glyph, height, width) is { } bitmap
                    && bitmap.InkWidth + (simulated ? 1 : 0) != measured.BlackBoxX)
                    mismatches.Add($"{at}: bitmap black box GDI {measured.BlackBoxX}, rule {bitmap.InkWidth + (simulated ? 1 : 0)}");
            }
        }

        // A Windows with ＭＳ ゴシック installed always has rows to compare.
        Assert.True(rows > 0 || Installed.Value.Family("ＭＳ ゴシック") == null, "No installed face was compared.");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {rows} requests differ:\n" +
                                            string.Join("\n", mismatches.Take(20)));
    }

    /// <summary>A width scales a face that is not monospaced by its unrounded average: the advance is the natural one
    /// times <see cref="GdiFontRules.HorizontalScale"/>, rounded. GDI's hinting makes about one in a hundred a pixel
    /// wider (6 of 624 on the machine the rule was fitted on): exact advances just under a half, as 20.499 for Meiryo
    /// UI at 22 pixels and -11, and Meiryo UI at 12 pixels and -6. That stays a known gap of the portable rasterizer.
    /// </summary>
    [Fact]
    public void AWidthScalesAFaceThatIsNotMonospacedByItsUnroundedAverage()
    {
        if (!OnWindows || !OperatingSystem.IsWindows()) return;
        using var gdi = new WindowsGdiGlyphMaskRasterizer();
        using var source = new WindowsGdiFontFamilySource();
        var mismatches = new List<string>();
        int rows = 0;
        foreach (InstalledFontFamily family in StaticFamilies(source))
        {
            OpenTypeFace face = Installed.Value.Resolve(family.Name, 400)!.Face;
            if (GdiFontRules.IsMonospaced(face) || face.GlyphIndex('激') == 0) continue;
            foreach (int height in new[] { 12, 16, 20, 22, 26, 32 })
            {
                FontProbeMetrics natural = gdi.Measure(family.Name, height, 0, 400, 0x8c83);
                foreach (int width in new[] { height / 3, height / 2, height * 2 / 3, height })
                {
                    rows++;
                    int expected = (int)Math.Round(natural.CellIncrementX * GdiFontRules.HorizontalScale(face, height, -width),
                                                   MidpointRounding.AwayFromZero);
                    int measured = gdi.Measure(family.Name, height, -width, 400, 0x8c83).CellIncrementX;
                    if (measured == expected) continue;
                    string at = $"{family.Name} {height} px width {-width}: GDI {measured}, rule {expected}";
                    Assert.True(measured == expected + 1, at);
                    mismatches.Add(at);
                }
            }
        }

        Assert.True(mismatches.Count * 50 <= rows, $"{mismatches.Count} of {rows} advances are a pixel wider:\n" +
                                            string.Join("\n", mismatches.Take(20)));
    }

    /// <summary>The families of AGE's list on this machine, as installed, whose faces are all static.</summary>
    private static IEnumerable<InstalledFontFamily> StaticFamilies(IFontFamilySource source)
        => FontFamilyCatalog.Build(source, true, [])
                            .Select(family => Installed.Value.Family(family.FaceName))
                            .OfType<InstalledFontFamily>()
                            .Where(family => family.Faces.All(face => face.Instance == null))
                            .DistinctBy(family => family.Name);
}

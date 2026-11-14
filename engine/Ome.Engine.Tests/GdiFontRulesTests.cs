using Ome.Engine.Text;

/// <summary>What GDI derives from a font file (<see cref="GdiFontRules"/>), on synthesized faces whose tables are
/// known; <c>WindowsGdiFontRulesTests</c> holds the same rules to GDI over the installed fonts.</summary>
public class GdiFontRulesTests
{
    private const ushort Japanese = GdiFontRules.Japanese;

    [Fact]
    public void AStaticFaceIsListedUnderItsJapaneseFamilyElseItsEnglishOne()
    {
        OpenTypeFace both = new TestFont().Name(1, "MS Gothic").Name(1, "ＭＳ ゴシック", Japanese).Read();
        OpenTypeFace english = new TestFont().Name(1, "Arial").Name(1, "Arial Deutsch", 0x0407).Read();
        OpenTypeFace typographic = new TestFont().Name(1, "Yu Gothic Light").Name(16, "Yu Gothic").Read();

        Assert.Equal(new (string, OpenTypeNamedInstance?)[] { ("ＭＳ ゴシック", null) }, GdiFontRules.Families(both));
        Assert.Equal("Arial", Assert.Single(GdiFontRules.Families(english)).Family);
        // A static face goes by its family name; the typographic family is only another name it is found by.
        Assert.Equal("Yu Gothic Light", Assert.Single(GdiFontRules.Families(typographic)).Family);
        Assert.Equal(new[] { "Yu Gothic Light", "Yu Gothic" },
                     GdiFontRules.FamilyNames(typographic).Select(name => name.Name));
        Assert.Empty(GdiFontRules.Families(new TestFont().Name(2, "Regular").Read()));
    }

    [Fact]
    public void AVariableFaceIsListedOncePerNamedInstance()
    {
        var font = new TestFont()
            .Name(1, "Noto Sans JP Thin").Name(16, "Noto Sans JP").Name(16, "ノト", Japanese)
            .Name(256, "Thin").Name(256, "極細", Japanese).Name(257, "Regular").Name(258, "Bold").Name(258, "太字", Japanese);
        font.Axes.Add("wght");
        font.Instances.Add((256, [100]));
        font.Instances.Add((257, [400]));
        font.Instances.Add((258, [700]));
        OpenTypeFace face = font.Read();

        // The four styles a family holds go under the typographic family; any other instance gets its own family.
        // Whether an instance is one of the four is read from its English name.
        Assert.Equal(new[] { "ノト 極細", "ノト", "ノト" }, GdiFontRules.Families(face).Select(family => family.Family));
        Assert.Same(face.NamedInstances[2], GdiFontRules.Families(face)[2].Instance);
        Assert.Equal(new[] { "Noto Sans JP Thin", "Noto Sans JP", "Noto Sans JP", "ノト 極細", "ノト", "ノト" },
                     GdiFontRules.FamilyNames(face).Select(name => name.Name));
    }

    [Theory]
    [InlineData(false, 0, false, 0x02)]      // proportional, no PANOSE family
    [InlineData(true, 0, false, 0x01)]       // post.isFixedPitch alone (BIZ UDゴシック): fixed, its own advances
    [InlineData(false, 9, true, 0x01)]       // a monospaced PANOSE proportion alone (IPAゴシック)
    [InlineData(true, 9, true, 0x01)]
    public void TheFixedPitchBitIsPostsOrAMonospacedPanose(bool post, byte proportion, bool monospaced,
                                                          byte pitchAndFamily)
    {
        var font = new TestFont { PostFixedPitch = post, Panose = [0, 0, 0, proportion, 0, 0, 0, 0, 0, 0] };
        OpenTypeFace face = font.Read();

        Assert.Equal(post || proportion == 9, GdiFontRules.IsFixedPitch(face));
        Assert.Equal(monospaced, GdiFontRules.IsMonospaced(face));
        Assert.Equal(pitchAndFamily, GdiFontRules.PitchAndFamily(face));
    }

    // The two family rules were read from GDI over probe faces that differ only in these PANOSE bytes and their
    // outlines (docs/engine-re.md, "What GDI derives from a font file").
    [Theory]
    [InlineData(2, 11, 0, 0x32)]    // sans-serif styles (11 to 15): FF_MODERN
    [InlineData(2, 13, 0, 0x32)]
    [InlineData(2, 14, 0, 0x32)]
    [InlineData(2, 15, 0, 0x32)]
    [InlineData(2, 2, 0, 0x12)]     // serif styles (2 to 10): FF_ROMAN
    [InlineData(2, 10, 9, 0x11)]
    [InlineData(2, 1, 0, 0x02)]     // no fit, or past 15: FF_DONTCARE
    [InlineData(2, 16, 0, 0x02)]
    [InlineData(4, 11, 0, 0x32)]    // the decorative type changes nothing (HG創英角ﾎﾟｯﾌﾟ体)
    [InlineData(3, 11, 0, 0x42)]    // the script type: FF_SCRIPT
    [InlineData(3, 2, 9, 0x41)]
    public void ATrueTypeFacesFamilyComesFromItsPanoseSerifStyle(byte type, byte serifStyle, byte proportion,
                                                                 byte pitchAndFamily)
    {
        OpenTypeFace face = new TestFont { Panose = [type, serifStyle, 0, proportion, 0, 0, 0, 0, 0, 0] }.Read();

        Assert.Equal(pitchAndFamily, GdiFontRules.PitchAndFamily(face));
    }

    [Theory]
    [InlineData(2, 11, 0, 0x22)]    // sans-serif styles (11 to 15): FF_SWISS (Noto Sans CJK)
    [InlineData(2, 15, 0, 0x22)]
    [InlineData(2, 2, 0, 0x12)]     // serif styles (2 to 10): FF_ROMAN (Noto Serif CJK)
    [InlineData(2, 0, 0, 0x32)]     // any other: FF_MODERN
    [InlineData(2, 16, 0, 0x32)]
    [InlineData(2, 2, 9, 0x31)]     // a monospaced proportion: FF_MODERN whatever the serif style
    [InlineData(6, 11, 0, 0x22)]
    [InlineData(3, 2, 9, 0x41)]     // the script type: FF_SCRIPT
    [InlineData(4, 11, 0, 0x52)]    // the decorative and pictorial types: FF_DECORATIVE
    [InlineData(5, 0, 9, 0x51)]
    public void ACffFacesFamilyComesFromItsPanoseTypeProportionAndSerifStyle(byte type, byte serifStyle,
                                                                            byte proportion, byte pitchAndFamily)
    {
        OpenTypeFace face = new TestFont
        {
            Panose = [type, serifStyle, 0, proportion, 0, 0, 0, 0, 0, 0],
            CffTable = TestFont.Cff([14]),
        }.Read("test.otf");

        Assert.Equal(pitchAndFamily, GdiFontRules.PitchAndFamily(face));
    }

    [Fact]
    public void ACffFaceIsNotFixedPitchByItsPostTable()
    {
        OpenTypeFace face = new TestFont { PostFixedPitch = true, CffTable = TestFont.Cff([14]) }.Read("test.otf");

        Assert.False(GdiFontRules.IsFixedPitch(face));
        Assert.Equal(0x32, GdiFontRules.PitchAndFamily(face));
    }

    [Theory]
    [InlineData(true, 1, 1u << 17, true)]
    [InlineData(true, 4, (1u << 17) | 1, true)]
    [InlineData(true, 4, 1u, false)]                // Latin 1 only
    [InlineData(true, 0, 1u << 17, false)]          // version 0 has no code page ranges
    [InlineData(false, 4, 1u << 17, false)]         // no OS/2 table
    public void AFaceIsListedUnderShiftJisWhenItsCodePagesInclude932(bool os2, int version, uint codePages,
                                                                     bool listed)
    {
        OpenTypeFace face = new TestFont { HasOs2 = os2, Os2Version = (ushort)version, CodePageRange1 = codePages }.Read();

        Assert.Equal(listed, GdiFontRules.ListsUnderShiftJis(face));
    }

    [Theory]
    [InlineData(1000, 525, 22, 12)]     // 11.55
    [InlineData(1000, 500, 21, 11)]     // 10.5 rounds up
    [InlineData(256, 128, 21, 11)]
    [InlineData(2048, 1918, 22, 21)]    // 20.6: メイリオ
    [InlineData(1000, 0, 22, 11)]       // no average recorded: half the height
    public void TheAverageWidthIsXAvgCharWidthScaledAndRoundedHalfUp(int unitsPerEm, int average, int height,
                                                                   int expected)
    {
        OpenTypeFace face = new TestFont { UnitsPerEm = (ushort)unitsPerEm, AverageCharWidth = (short)average }.Read();

        Assert.Equal(expected, GdiFontRules.AverageCharWidth(face, height));
        Assert.Equal(expected, GdiFontRules.TextMetrics(face, height, 0, simulatedBold: false).AverageCharWidth);
        Assert.Equal(expected + 1, GdiFontRules.TextMetrics(face, height, 0, simulatedBold: true).AverageCharWidth);
    }

    [Fact]
    public void ARequestedWidthIsTheAverageWidthGdiReports()
    {
        OpenTypeFace face = new TestFont { UnitsPerEm = 2048, AverageCharWidth = 1918 }.Read();

        Assert.Equal(11, GdiFontRules.TextMetrics(face, 22, -11, simulatedBold: false).AverageCharWidth);
        Assert.Equal(19, GdiFontRules.TextMetrics(face, 22, -18, simulatedBold: true).AverageCharWidth);
    }

    [Fact]
    public void TheAscentAndDescentAreTheWindowsMetricsScaledAndRoundedHalfUp()
    {
        OpenTypeFace face = new TestFont { WindowsAscent = 900, WindowsDescent = 100, Ascender = 800, Descender = -300 }
            .Read();

        Assert.Equal(new GdiTextMetrics(14, 2, 8), GdiFontRules.TextMetrics(face, 15, -8, false));   // 13.5, 1.5
        Assert.Equal(22, GdiFontRules.TextMetrics(face, 22, 0, false).Height);
    }

    [Theory]
    [InlineData(8, 9, 10)]      // 9.28 + 2.30: a TrueType face rounds the ascent, a CFF face the whole height
    [InlineData(10, 12, 11)]    // 11.6 + 2.88
    [InlineData(19, 22, 23)]    // 22.04 + 5.47
    [InlineData(32, 37, 37)]
    public void ACffFaceRoundsItsDescentAndItsHeight(int height, int trueTypeAscent, int cffAscent)
    {
        // Noto Sans CJK's Windows metrics: GDI gave these ascents for its CFF faces.
        var font = new TestFont { WindowsAscent = 1160, WindowsDescent = 288 };
        int descent = (int)Math.Floor(288.0 * height / 1000 + 0.5);

        Assert.Equal(new GdiTextMetrics(trueTypeAscent, descent, 5), GdiFontRules.TextMetrics(font.Read(), height, 5, false));
        font.CffTable = TestFont.Cff([14]);
        Assert.Equal(new GdiTextMetrics(cffAscent, descent, 5),
                     GdiFontRules.TextMetrics(font.Read("test.otf"), height, 5, false));
    }

    [Fact]
    public void AFaceWithoutWindowsMetricsUsesItsHorizontalHeader()
    {
        OpenTypeFace none = new TestFont { HasOs2 = false, Ascender = 800, Descender = -200 }.Read();
        OpenTypeFace zero = new TestFont { WindowsAscent = 0, WindowsDescent = 0, Ascender = 750, Descender = -250 }
            .Read();

        Assert.Equal((8, 2), (GdiFontRules.TextMetrics(none, 10, 0, false).Ascent,
                              GdiFontRules.TextMetrics(none, 10, 0, false).Descent));
        Assert.Equal((8, 3), (GdiFontRules.TextMetrics(zero, 10, 0, false).Ascent,
                              GdiFontRules.TextMetrics(zero, 10, 0, false).Descent));   // 7.5, 2.5
    }

    [Fact]
    public void TheDeviceMetricsRecordForTheHeightComesFirst()
    {
        // Meiryo UI at 12 pixels: VDMX gives 12 and 3 where the Windows metrics would give 13 and 4.
        var font = new TestFont { UnitsPerEm = 2048, WindowsAscent = 2171, WindowsDescent = 901 };
        font.DeviceRatios.Add(new TestDeviceRatio(1, 1, 1, [(12, 12, -3)]));
        OpenTypeFace face = font.Read();

        Assert.Equal((12, 3), (GdiFontRules.TextMetrics(face, 12, 0, false).Ascent,
                               GdiFontRules.TextMetrics(face, 12, 0, false).Descent));
        Assert.Equal((14, 6), (GdiFontRules.TextMetrics(face, 13, 0, false).Ascent,
                               GdiFontRules.TextMetrics(face, 13, 0, false).Descent));   // 13.78, 5.72
    }

    [Fact]
    public void ARequestedWidthScalesTheFaceByItsUnroundedAverage()
    {
        OpenTypeFace gothic = new TestFont { UnitsPerEm = 256, AverageCharWidth = 128 }.Read();
        OpenTypeFace meiryo = new TestFont { UnitsPerEm = 2048, AverageCharWidth = 1918 }.Read();
        OpenTypeFace unrecorded = new TestFont { AverageCharWidth = 0 }.Read();

        Assert.Equal(1, GdiFontRules.HorizontalScale(gothic, 22, -11));
        Assert.Equal(1, GdiFontRules.HorizontalScale(gothic, 22, 0));
        Assert.Equal(10 / 10.5, GdiFontRules.HorizontalScale(gothic, 21, -10), 12);
        Assert.Equal(11 / (1918 * 22 / 2048.0), GdiFontRules.HorizontalScale(meiryo, 22, -11), 12);
        Assert.Equal(18 / 11.0, GdiFontRules.HorizontalScale(unrecorded, 22, 18), 12);
    }

    [Fact]
    public void AMonospacedFaceAdvancesByWholeAverageWidths()
    {
        var monospaced = new TestFont { UnitsPerEm = 256, AverageCharWidth = 128, Panose = [2, 11, 6, 9, 0, 0, 0, 0, 0, 0] }
            .Read();
        OpenTypeFace proportional = new TestFont { UnitsPerEm = 256, AverageCharWidth = 128 }.Read();

        // At 21 pixels the average is 11 (10.5 rounded up): a full-width glyph spans two.
        Assert.Equal(22, GdiFontRules.MonospacedAdvance(monospaced, 21, 0, 21));
        Assert.Equal(20, GdiFontRules.MonospacedAdvance(monospaced, 21, -10, 21));
        Assert.Equal(10, GdiFontRules.MonospacedAdvance(monospaced, 21, -10, 11));
        Assert.Equal(10, GdiFontRules.MonospacedAdvance(monospaced, 21, -10, 3));   // at least one
        Assert.Null(GdiFontRules.MonospacedAdvance(monospaced, 21, -10, 0));
        Assert.Null(GdiFontRules.MonospacedAdvance(proportional, 21, -10, 21));
    }

    [Fact]
    public void AnUnscaledRequestIsMeasuredByTheEmbeddedBitmap()
    {
        var font = new TestFont { UnitsPerEm = 256, AverageCharWidth = 128 };
        int glyph = font.AddGlyph(TestGlyph.Box(0, 0, 200, 200));
        font.Strikes.Add(new TestStrike(22, 1, 1, 1, [new TestBitmap(glyph, 1, 18, 22, ".###.", "#...#")]));
        OpenTypeFace face = font.Read();

        Assert.Equal(5, GdiFontRules.MeasuredBitmap(face, glyph, 22, -11)?.InkWidth);
        Assert.Equal(5, GdiFontRules.MeasuredBitmap(face, glyph, 22, 0)?.InkWidth);
        Assert.Null(GdiFontRules.MeasuredBitmap(face, glyph, 22, -10));   // scaled: the outline
        Assert.Null(GdiFontRules.MeasuredBitmap(face, glyph, 21, 0));     // no strike at 21
    }

    [Theory]
    [InlineData(0x41, 'A')]
    [InlineData(0x82a0, 'あ')]
    [InlineData(0x8c83, '激')]
    [InlineData(0xb1, 'ｱ')]
    [InlineData(0x8540, '・')]   // undefined in CP932
    public void ACp932CodeIsTheCharacterWindowsDecodesItTo(int code, char expected)
    {
        Assert.Equal(expected, GdiFontRules.Cp932Character((ushort)code));
    }

    [Fact]
    public void AMissingCharacterIsDrawnWithTheDefaultCharacter()
    {
        var font = new TestFont { DefaultChar = 'B' };
        int a = font.AddGlyph(TestGlyph.Box(0, 0, 400, 700));
        int b = font.AddGlyph(TestGlyph.Box(0, 0, 400, 700));
        font.Map(('A', a), ('B', b));
        OpenTypeFace face = font.Read();
        OpenTypeFace noDefault = new TestFont().Map(('A', 1)).Read();

        Assert.Equal(a, GdiFontRules.GlyphFor(face, 0x41));
        Assert.Equal(b, GdiFontRules.GlyphFor(face, 0x8c83));
        Assert.Equal(0, GdiFontRules.GlyphFor(noDefault, 0x8c83));
    }

    [Fact]
    public void TheCheckerProbePassesAGlyphThatSpansAPixelAtSixtyFourPixels()
    {
        var font = new TestFont();
        int ink = font.AddGlyph(TestGlyph.Box(0, 0, 400, 700));
        int speck = font.AddGlyph(TestGlyph.Box(0, 0, 14, 14));   // under 1000 / 64 units
        int blank = font.AddGlyph(null);
        font.Map(('A', ink), ('.', speck), (' ', blank));
        OpenTypeFace face = font.Read();
        var withDefault = new TestFont { DefaultChar = 'A' };
        withDefault.AddGlyph(TestGlyph.Box(0, 0, 400, 700));
        withDefault.Map(('A', 1));

        Assert.True(GdiFontRules.HasCheckerGlyph(face, 0x41));
        Assert.False(GdiFontRules.HasCheckerGlyph(face, 0x2e));
        Assert.False(GdiFontRules.HasCheckerGlyph(face, 0x20));
        Assert.False(GdiFontRules.HasCheckerGlyph(face, 0x88ab));                 // missing: a blank .notdef
        Assert.True(GdiFontRules.HasCheckerGlyph(withDefault.Read(), 0x88ab));    // missing: the default character
    }
}

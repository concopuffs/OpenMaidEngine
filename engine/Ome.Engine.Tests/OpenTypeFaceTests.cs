using Ome.Engine.Text;

/// <summary>The portable sfnt reader (<see cref="OpenTypeFace"/>) on synthesized faces (<see cref="TestFont"/>).</summary>
public class OpenTypeFaceTests
{
    [Fact]
    public void TheSmallTablesAreReadWithTheFace()
    {
        var font = new TestFont
        {
            UnitsPerEm = 2048, MacStyle = 2, Ascender = 1800, Descender = -500, Os2Version = 3,
            AverageCharWidth = 1918, WeightClass = 700, Selection = 0x20, WindowsAscent = 2171, WindowsDescent = 901,
            CodePageRange1 = 0x0002_0001, CodePageRange2 = 7, DefaultChar = 0x30fb, PostFixedPitch = true,
            Panose = [2, 11, 6, 4, 3, 5, 4, 4, 2, 4],
        };
        font.AddGlyph(TestGlyph.Box(0, 0, 100, 100));

        OpenTypeFace face = font.Read("meiryo.ttc");

        Assert.Equal(("meiryo.ttc", 0, 2048, (ushort)2), (face.Source, face.Index, face.UnitsPerEm, face.MacStyle));
        Assert.Equal(((short)1800, (short)-500), (face.HorizontalAscender, face.HorizontalDescender));
        Assert.True(face.HasOs2);
        Assert.Equal((3, (short)1918, 700, (ushort)0x20), (face.Os2Version, face.AverageCharWidth, face.WeightClass,
                                                           face.Selection));
        Assert.Equal(new byte[] { 2, 11, 6, 4, 3, 5, 4, 4, 2, 4 }, face.Panose);
        Assert.Equal(((ushort)2171, (ushort)901), (face.WindowsAscent, face.WindowsDescent));
        Assert.Equal((0x0002_0001u, 7u, (ushort)0x30fb), (face.CodePageRange1, face.CodePageRange2, face.DefaultChar));
        Assert.True(face.PostFixedPitch);
        Assert.Equal(2, face.GlyphCount);
        Assert.True(face.IsItalic);   // macStyle bit 1
        Assert.False(face.IsVariable);
    }

    [Fact]
    public void AnOlderOs2TableHasNoCodePagesOrDefaultCharacter()
    {
        OpenTypeFace version0 = new TestFont { Os2Version = 0, DefaultChar = 0x41 }.Read();
        OpenTypeFace version1 = new TestFont { Os2Version = 1, DefaultChar = 0x41 }.Read();
        OpenTypeFace none = new TestFont { HasOs2 = false }.Read();

        Assert.Equal((0u, (ushort)0), (version0.CodePageRange1, version0.DefaultChar));
        Assert.Equal((1u << 17, (ushort)0), (version1.CodePageRange1, version1.DefaultChar));
        Assert.Equal((false, 400, (short)0), (none.HasOs2, none.WeightClass, none.AverageCharWidth));
    }

    [Fact]
    public void OnlyTheWindowsNamesGdiReadsAreKept()
    {
        var font = new TestFont()
            .Name(1, "ＭＳ ゴシック", GdiFontRules.Japanese)
            .Name(1, "MS Gothic")
            .Name(2, "Regular")
            .Name(3, "unique id")
            .Name(16, "Typographic");
        font.Names.Add((1, 0, 0, 1, "Mac Name"));

        OpenTypeFace face = font.Read();

        Assert.Equal(new OpenTypeName[]
        {
            new(1, GdiFontRules.Japanese, "ＭＳ ゴシック"), new(1, GdiFontRules.UnitedStatesEnglish, "MS Gothic"),
            new(2, GdiFontRules.UnitedStatesEnglish, "Regular"), new(16, GdiFontRules.UnitedStatesEnglish, "Typographic"),
        }, face.Names);
        Assert.Equal("MS Gothic", face.Name(1, GdiFontRules.UnitedStatesEnglish, GdiFontRules.Japanese));
        Assert.Equal("ＭＳ ゴシック", face.Name(1, 0x0407));   // no German name: the first in any language
        Assert.Null(face.Name(17));
    }

    [Fact]
    public void TheCharacterMapGdiReadsIsTheFullRepertoireFirst()
    {
        var font = new TestFont();
        font.CharacterMaps.Add(new TestCmap(3, 1, 4, new Dictionary<int, int> { ['A'] = 1 }));
        font.CharacterMaps.Add(new TestCmap(3, 10, 12, new Dictionary<int, int> { ['A'] = 2, [0x20b9f] = 3 }));
        font.CharacterMaps.Add(new TestCmap(0, 3, 4, new Dictionary<int, int> { ['A'] = 4 }));

        OpenTypeFace face = font.Read();

        Assert.Equal(2, face.GlyphIndex('A'));
        Assert.Equal(3, face.GlyphIndex(0x20b9f));   // 𠮟, beyond the BMP
        Assert.Equal(0, face.GlyphIndex('B'));
        Assert.Equal(0, face.GlyphIndex(-1));
    }

    [Theory]
    [InlineData(0, 3, 4, 7)]     // Unicode before Windows BMP
    [InlineData(3, 1, 4, 5)]     // Windows BMP
    public void AUnicodeMapComesBeforeTheWindowsBmpOne(ushort platform, ushort encoding, int format, int expected)
    {
        var font = new TestFont();
        font.CharacterMaps.Add(new TestCmap(3, 1, 4, new Dictionary<int, int> { ['A'] = 5 }));
        if (platform == 0) font.CharacterMaps.Add(new TestCmap(platform, encoding, format,
                                                               new Dictionary<int, int> { ['A'] = 7 }));

        Assert.Equal(expected, font.Read().GlyphIndex('A'));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(6, false)]
    [InlineData(12, false)]
    public void EachCharacterMapFormatIsRead(int format, bool rangeOffsets)
    {
        var map = new Dictionary<int, int> { [' '] = 1, ['A'] = 2, ['B'] = 3, ['D'] = 9, [0xc0] = 4 };
        // 激 and ｱ: deltas that wrap (a glyph index array that long would not fit format 4's length).
        if (format == 12 || format == 4 && !rangeOffsets) (map[0x6fc0], map[0xff71]) = (5, 6);
        var font = new TestFont();
        font.CharacterMaps.Add(new TestCmap(3, 1, format, map, rangeOffsets));

        OpenTypeFace face = font.Read();

        foreach ((int scalar, int glyph) in map) Assert.Equal(glyph, face.GlyphIndex(scalar));
        Assert.Equal(0, face.GlyphIndex('C'));
        Assert.Equal(0, face.GlyphIndex(0xfffe));
    }

    [Fact]
    public void ASymbolMapAnswersTheLowBytesFromThePrivateUseRange()
    {
        var font = new TestFont();
        font.CharacterMaps.Add(new TestCmap(3, 0, 4, new Dictionary<int, int> { [0xf041] = 3, [0x42] = 4 }));

        OpenTypeFace face = font.Read();

        Assert.Equal(3, face.GlyphIndex('A'));
        Assert.Equal(4, face.GlyphIndex('B'));
        Assert.Equal(0, face.GlyphIndex(0x141));
    }

    [Fact]
    public void TheDeviceMetricsAreTheFirstRatioThatTakesASquareDevice()
    {
        var font = new TestFont();
        font.DeviceRatios.Add(new TestDeviceRatio(2, 1, 1, [(12, 99, -99)]));    // 2:1, not square
        font.DeviceRatios.Add(new TestDeviceRatio(1, 1, 1, [(12, 12, -3), (13, 13, -4)]));
        font.DeviceRatios.Add(new TestDeviceRatio(0, 0, 0, [(12, 50, -50), (14, 15, -4)]));

        OpenTypeFace face = font.Read();

        Assert.Equal((12, -3), face.VerticalDeviceMetrics(12));
        Assert.Equal((13, -4), face.VerticalDeviceMetrics(13));
        Assert.Null(face.VerticalDeviceMetrics(14));   // only the first square ratio is read
        Assert.Null(new TestFont().Read().VerticalDeviceMetrics(12));
    }

    public static TheoryData<int, int, int> BitmapFormats => new()
    {
        { 1, 1, 1 }, { 1, 2, 1 }, { 3, 6, 1 }, { 3, 7, 1 }, { 4, 1, 1 }, { 4, 2, 1 }, { 1, 2, 8 }, { 3, 6, 4 },
        { 2, 5, 1 }, { 5, 5, 1 }, { 5, 5, 2 },
    };

    [Theory]
    [MemberData(nameof(BitmapFormats))]
    public void AStrikesGlyphReportsItsBoxAndItsInk(int indexFormat, int imageFormat, int bitDepth)
    {
        // Image format 5 keeps one size for every glyph of the subtable.
        bool constant = imageFormat == 5;
        TestBitmap[] bitmaps =
        [
            new(3, 1, 9, 11, "........", "..#.....", "..##....", "..###...", "........"),
            new(constant ? 4 : 6, -1, 8, 12, constant
                ? ["#.......", "........", "........", "........", ".......#"]
                : ["....", ".##.", "...."]),
        ];
        if (indexFormat == 2) bitmaps[1] = bitmaps[1] with { Glyph = 4 };
        var font = new TestFont();
        for (int glyph = 1; glyph <= 6; glyph++) font.AddGlyph(TestGlyph.Box(0, 0, 100, 100));
        font.Strikes.Add(new TestStrike(16, bitDepth, indexFormat, imageFormat, bitmaps));

        OpenTypeFace face = font.Read();

        Assert.True(face.HasBitmapStrike(16));
        Assert.False(face.HasBitmapStrike(15));
        Assert.Equal(new EmbeddedBitmapGlyph(8, 5, 1, 9, 11, 2, 1, 3, 3), face.BitmapGlyph(16, 3));
        EmbeddedBitmapGlyph? second = face.BitmapGlyph(16, bitmaps[1].Glyph);
        Assert.Equal(constant
                         ? new EmbeddedBitmapGlyph(8, 5, 1, 9, 11, 0, 0, 8, 5)        // the subtable's metrics
                         : new EmbeddedBitmapGlyph(4, 3, -1, 8, 12, 1, 1, 2, 1), second);
        Assert.Null(face.BitmapGlyph(16, 2));    // outside the subtable
        Assert.Null(face.BitmapGlyph(15, 3));    // no such strike
        if (indexFormat is 1 or 3) Assert.Null(face.BitmapGlyph(16, 5));   // in range with no image of its own
    }

    [Fact]
    public void ABlankBitmapHasNoInkAndACompositeIsAllInk()
    {
        var font = new TestFont();
        for (int glyph = 1; glyph <= 2; glyph++) font.AddGlyph(TestGlyph.Box(0, 0, 100, 100));
        font.Strikes.Add(new TestStrike(12, 1, 1, 1, [new TestBitmap(1, 0, 6, 6, "...", "...")]));
        font.Strikes.Add(new TestStrike(13, 1, 1, 8, [new TestBitmap(2, 0, 6, 7, "....", "....")]));

        OpenTypeFace face = font.Read();

        Assert.Equal(new EmbeddedBitmapGlyph(3, 2, 0, 6, 6, 0, 0, 0, 0), face.BitmapGlyph(12, 1));
        Assert.Equal(new EmbeddedBitmapGlyph(4, 2, 0, 6, 7, 0, 0, 4, 2), face.BitmapGlyph(13, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGlyfOutlineHasInkWhenItsBoxSpansTheMinimum(bool shortLoca)
    {
        var font = new TestFont { ShortLoca = shortLoca };
        int box = font.AddGlyph(TestGlyph.Box(10, -5, 410, 700));
        int thin = font.AddGlyph(TestGlyph.Box(0, 0, 14, 14));
        int contourless = font.AddGlyph(new TestGlyph(0, 0, 0, 500, 500));
        int composite = font.AddGlyph(new TestGlyph(-1, 0, 0, 300, 20));

        OpenTypeFace face = font.Read();

        Assert.False(face.HasInk(0, 15));             // an empty .notdef
        Assert.True(face.HasInk(box, 15));
        Assert.False(face.HasInk(thin, 15));
        Assert.True(face.HasInk(thin, 14));
        Assert.False(face.HasInk(contourless, 15));
        Assert.True(face.HasInk(composite, 15));
        Assert.False(face.HasInk(99, 15));            // beyond loca
    }

    [Fact]
    public void ACffCharstringHasInkWhenItDraws()
    {
        byte[] endchar = [14];
        byte[] line = [149, 149, 21, 189, 139, 5, 14];                  // 10 10 rmoveto 50 0 rlineto endchar
        byte[] moveOnly = [239, 139, 139, 21, 14];                      // width, then 0 0 rmoveto endchar
        byte[] hinted = [139, 189, 1, 19, 0x05, 14];                    // 0 50 hstem, a hintmask whose byte is 5
        byte[] subroutine = [139, 10];                                  // 0 callsubr
        byte[] flex = [139, 139, 139, 139, 139, 139, 139, 139, 139, 139, 139, 139, 139, 12, 35];
        var font = new TestFont { CffTable = TestFont.Cff(endchar, line, moveOnly, hinted, subroutine, flex) };

        OpenTypeFace face = font.Read("test.otf");

        Assert.Equal(new[] { false, true, false, false, true, true },
                     Enumerable.Range(0, 6).Select(glyph => face.HasInk(glyph, 15)));
        Assert.False(face.HasInk(6, 15));
        Assert.True(face.HasCffOutlines);
        Assert.False(new TestFont().Read().HasCffOutlines);   // glyf outlines
    }

    [Fact]
    public void AVariableFacesNamedInstancesAndTheirNamesAreRead()
    {
        var font = new TestFont().Name(1, "Noto Sans JP").Name(256, "Thin").Name(257, "Bold").Name(258, "Unused");
        font.Axes.AddRange(["wght", "wdth"]);
        font.Instances.Add((256, [100, 100]));
        font.Instances.Add((257, [700, 87.5f]));

        OpenTypeFace face = font.Read();

        Assert.True(face.IsVariable);
        Assert.Equal(2, face.NamedInstances.Count);
        Assert.Equal((ushort)257, face.NamedInstances[1].SubfamilyNameId);
        IReadOnlyDictionary<string, float> bold = face.NamedInstances[1].Coordinates;
        Assert.Equal((2, 700f, 87.5f), (bold.Count, bold["wght"], bold["wdth"]));
        Assert.Equal(new[] { "Noto Sans JP", "Thin", "Bold" }, face.Names.Select(name => name.Value));
    }

    [Fact]
    public void ACollectionsFacesAreReadInOrderFromAFile()
    {
        TestFont first = new TestFont { WeightClass = 400 }.Name(1, "First").Map(('A', 1));
        first.AddGlyph(TestGlyph.Box(0, 0, 100, 100));
        TestFont second = new TestFont { WeightClass = 700 }.Name(1, "Second").Map(('A', 2));
        string path = Path.Combine(Path.GetTempPath(), $"ome-font-{Guid.NewGuid():N}.ttc");
        File.WriteAllBytes(path, TestFont.Collection(first, second));
        try
        {
            IReadOnlyList<OpenTypeFace> faces = OpenTypeFace.ReadFile(path);

            Assert.Equal(new[] { 0, 1 }, faces.Select(face => face.Index));
            Assert.All(faces, face => Assert.Equal(path, face.Source));
            Assert.Equal(new[] { "First", "Second" }, faces.Select(face => face.Name(1)));
            Assert.Equal(new[] { 400, 700 }, faces.Select(face => face.WeightClass));
            // The tables read later come from the same file.
            Assert.Equal(new[] { 1, 2 }, faces.Select(face => face.GlyphIndex('A')));
            Assert.True(faces[0].HasInk(1, 15));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileThatIsNotAFontIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => OpenTypeFace.ReadData("not a font at all"u8.ToArray(), "x.ttf"));
        Assert.Throws<InvalidDataException>(() => OpenTypeFace.ReadData([0, 1, 0, 0, 0, 0], "short.ttf"));
    }
}

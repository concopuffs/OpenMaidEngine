using Ome.Engine.Text;

/// <summary>The installed fonts read from their files (<see cref="InstalledFontIndex"/>) and the installed-font list
/// AGE builds from them where GDI does not give it (<see cref="OpenTypeFontFamilySource"/>).</summary>
public class InstalledFontIndexTests
{
    private static TestFont Face(string english, string? japanese = null, ushort weight = 400, bool italic = false,
                                 uint codePages = 1u << 17)
    {
        var font = new TestFont { WeightClass = weight, Selection = italic ? (ushort)1 : (ushort)0x40, CodePageRange1 = codePages };
        font.Name(1, english);
        if (japanese != null) font.Name(1, japanese, GdiFontRules.Japanese);
        return font;
    }

    [Fact]
    public void FacesAreGroupedIntoFamiliesInTheOrderTheyAreFound()
    {
        InstalledFontIndex index = InstalledFontIndex.FromFaces([
            Face("Meiryo", "メイリオ").Read("meiryo.ttc"),
            Face("MS Gothic", "ＭＳ ゴシック").Read("msgothic.ttc"),
            Face("Meiryo", "メイリオ", weight: 700).Read("meiryob.ttc"),
        ]);

        Assert.Equal(new[] { "メイリオ", "ＭＳ ゴシック" }, index.Families.Select(family => family.Name));
        Assert.Equal(new[] { 400, 700 }, index.Families[0].Faces.Select(face => face.Weight));
        // Any of a family's names finds it, in any case, and a vertical face's '@' is ignored.
        Assert.Same(index.Families[0], index.Family("Meiryo"));
        Assert.Same(index.Families[0], index.Family("meiryo"));
        Assert.Same(index.Families[1], index.Family("@ＭＳ ゴシック"));
        Assert.Null(index.Family("Not Installed"));
    }

    [Fact]
    public void AFamilyResolvesToTheFaceNearestTheWeightInTheAskedSlant()
    {
        InstalledFontIndex index = InstalledFontIndex.FromFaces([
            Face("Test Sans", weight: 300).Read("light.ttf"),
            Face("Test Sans", weight: 400).Read("regular.ttf"),
            Face("Test Sans", weight: 700).Read("bold.ttf"),
            Face("Test Sans", weight: 400, italic: true).Read("italic.ttf"),
            Face("Upright Only").Read("upright.ttf"),
        ]);

        Assert.Equal("regular.ttf", index.Resolve("Test Sans", 400)?.Face.Source);
        Assert.Equal("bold.ttf", index.Resolve("Test Sans", 600)?.Face.Source);
        Assert.Equal("light.ttf", index.Resolve("Test Sans", 100)?.Face.Source);
        Assert.Equal("italic.ttf", index.Resolve("Test Sans", 700, italic: true)?.Face.Source);
        Assert.Equal("upright.ttf", index.Resolve("Upright Only", 400, italic: true)?.Face.Source);
        Assert.Null(index.Resolve("Missing", 400));
        Assert.Equal("regular.ttf", index.Family("Test Sans")!.Regular.Face.Source);
    }

    [Fact]
    public void AVariableFacesInstancesAreFacesOfTheirFamilies()
    {
        TestFont font = new TestFont().Name(1, "Noto Sans JP").Name(256, "Thin").Name(257, "Regular").Name(258, "Bold");
        font.Axes.Add("wght");
        font.Instances.Add((256, [100]));
        font.Instances.Add((257, [400]));
        font.Instances.Add((258, [700]));
        InstalledFontIndex index = InstalledFontIndex.FromFaces([font.Read("NotoSansJP-VF.ttf")]);

        Assert.Equal(new[] { "Noto Sans JP Thin", "Noto Sans JP" }, index.Families.Select(family => family.Name));
        InstalledFace? bold = index.Resolve("Noto Sans JP", 700);
        Assert.Equal((700, (ushort)258), (bold?.Weight, bold?.Instance?.SubfamilyNameId));
        Assert.Equal(400, index.Family("Noto Sans JP")!.Regular.Weight);
    }

    [Fact]
    public void TheListHasTheShiftJisFamiliesWithGdisPitch()
    {
        TestFont gothic = Face("MS Gothic", "ＭＳ ゴシック");
        gothic.Panose = [2, 11, 6, 9, 7, 2, 5, 8, 2, 4];
        InstalledFontIndex index = InstalledFontIndex.FromFaces([
            gothic.Read(),
            Face("Meiryo", "メイリオ").Read(),
            Face("Arial", codePages: 1).Read(),
        ]);
        var source = new OpenTypeFontFamilySource(index);

        Assert.Equal(new EnumeratedFontFamily[] { new("ＭＳ ゴシック", 0x31), new("メイリオ", 0x02) },
                     source.EnumerateShiftJisFamilies());
    }

    [Fact]
    public void TheCheckerProbeReadsTheFamilysRegularFace()
    {
        // 悪's checker code is 0x87AB (FontFamilyCatalog.CheckerCode), which CP932 does not define: GDI is asked for ・.
        TestFont withKanji = Face("With Kanji");
        withKanji.Map(('・', withKanji.AddGlyph(TestGlyph.Box(0, 0, 900, 900))));
        TestFont latin = Face("Latin");
        latin.Map(('A', latin.AddGlyph(TestGlyph.Box(0, 0, 500, 700))));
        var source = new OpenTypeFontFamilySource(InstalledFontIndex.FromFaces([withKanji.Read(), latin.Read()]));
        ushort aku = FontFamilyCatalog.CheckerCode("悪");

        Assert.True(source.HasGlyph("With Kanji", aku));
        Assert.False(source.HasGlyph("Latin", aku));
        Assert.True(source.HasGlyph("Not Listed", aku));
        Assert.Equal(new[] { "With Kanji" },
                     FontFamilyCatalog.Build(source, useProportionalFont: true, ["悪"]).Select(family => family.FaceName));
    }

    [Fact]
    public void AScanReadsEachFontFileOnceAndReportsTheOnesItCannotRead()
    {
        string directory = Directory.CreateTempSubdirectory("ome-fonts-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            string collection = Path.Combine(directory, "b.ttc");
            File.WriteAllBytes(collection, TestFont.Collection(Face("Second Family"), Face("Third Family")));
            File.WriteAllBytes(Path.Combine(directory, "nested", "a.TTF"), Face("First Family").Build());
            File.WriteAllBytes(Path.Combine(directory, "broken.otf"), "not a font"u8.ToArray());
            File.WriteAllText(Path.Combine(directory, "readme.txt"), "fonts");
            var warnings = new List<string>();

            List<string> files = SystemFontFiles.Enumerate([directory, Path.Combine(directory, "missing")]).ToList();
            InstalledFontIndex index = InstalledFontIndex.Scan(files.Append(collection), warnings.Add);

            Assert.Equal(new[] { "b.ttc", "broken.otf", "a.TTF" }, files.Select(Path.GetFileName));
            Assert.Equal(new[] { "Second Family", "Third Family", "First Family" },
                         index.Families.Select(family => family.Name));
            Assert.Equal(1, index.Family("Third Family")!.Regular.Face.Index);
            Assert.Contains("broken.otf", Assert.Single(warnings));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheFontDirectoriesAreThePlatforms()
    {
        IReadOnlyList<string> directories = SystemFontFiles.Directories();

        if (OperatingSystem.IsWindows())
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"),
                         directories[0]);
        else if (!OperatingSystem.IsMacOS())
            Assert.Contains(directories, directory => directory.EndsWith("/share/fonts", StringComparison.Ordinal));
        Assert.Equal(directories.Count, directories.Distinct().Count());
    }
}

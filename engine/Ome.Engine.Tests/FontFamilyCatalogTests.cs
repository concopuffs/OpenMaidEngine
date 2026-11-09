using Ome.Engine.Text;

/// <summary>SYS4433's installed-font list (docs/engine-re.md, "Font family list"), from a synthesized font system.</summary>
public class FontFamilyCatalogTests
{
    private const byte Fixed = 1;
    private const byte Variable = 2;

    private sealed class FakeFontSystem(
        IReadOnlyList<EnumeratedFontFamily> families,
        IReadOnlyList<string> systemLink,
        IReadOnlyDictionary<string, ushort[]>? missingGlyphs = null) : IFontFamilySource
    {
        public List<(string Face, ushort Code)> Probes { get; } = new();

        public IReadOnlyList<EnumeratedFontFamily> EnumerateShiftJisFamilies() => families;
        public IReadOnlyList<string> SystemLinkFaceNames() => systemLink;

        public bool HasGlyph(string faceName, ushort code)
        {
            Probes.Add((faceName, code));
            return missingGlyphs == null || !missingGlyphs.TryGetValue(faceName, out ushort[]? codes)
                                         || !codes.Contains(code);
        }
    }

    private static string[] Names(IReadOnlyList<EnumeratedFontFamily> families)
        => families.Select(family => family.FaceName).ToArray();

    [Fact]
    public void SystemLinkFacesLeaveTheListExceptTheTenJapaneseFamilies()
    {
        var fonts = new FakeFontSystem(
            [new("Segoe UI", Variable), new("MS Gothic", Fixed), new("ＭＳ ゴシック", Fixed),
             new("Tahoma", Variable), new("Meiryo UI", Variable), new("Tahoma", Variable)],
            ["Tahoma", "Segoe UI", "MS Gothic", "Meiryo UI"]);

        Assert.Equal(new[] { "MS Gothic", "ＭＳ ゴシック", "Meiryo UI" },
            Names(FontFamilyCatalog.Build(fonts, useProportionalFont: true, [])));
    }

    [Fact]
    public void WithoutProportionalFontsOnlyFixedPitchFamiliesRemain()
    {
        var fonts = new FakeFontSystem(
            [new("ＭＳ ゴシック", Fixed), new("ＭＳ Ｐゴシック", Variable), new("IPA Pゴシック", Fixed),
             new("IPA P明朝", Fixed), new("IPAゴシック", Fixed), new("メイリオ", Variable)],
            []);

        Assert.Equal(new[] { "ＭＳ ゴシック", "IPAゴシック" }, Names(FontFamilyCatalog.Build(fonts, false, [])));
        Assert.Equal(6, FontFamilyCatalog.Build(fonts, true, []).Count);
    }

    [Fact]
    public void AFamilyMissingAnyCheckerCharacterLeavesTheList()
    {
        var fonts = new FakeFontSystem(
            [new("ＭＳ 明朝", Fixed), new("Latin Only", Fixed), new("No Kana", Fixed)],
            [],
            new Dictionary<string, ushort[]> { ["Latin Only"] = [0x87ab, 0x9ead], ["No Kana"] = [0xb1] });

        IReadOnlyList<string> list = Names(FontFamilyCatalog.Build(fonts, true, ["悪", "ｱ", "洒"]));

        Assert.Equal(new[] { "ＭＳ 明朝" }, list);
        // Every family is asked for every checker code, in VALIDFONTCHECKER order.
        Assert.Equal(new ushort[] { 0x87ab, 0xb1, 0x9ead }, fonts.Probes.Where(p => p.Face == "ＭＳ 明朝").Select(p => p.Code));
    }

    [Fact]
    public void FamiliesKeepTheirPitchAndFamily()
    {
        var fonts = new FakeFontSystem([new("メイリオ", 34), new("ＭＳ ゴシック", 49)], []);

        Assert.Equal(new EnumeratedFontFamily[] { new("メイリオ", 34), new("ＭＳ ゴシック", 49) },
            FontFamilyCatalog.Build(fonts, true, []));
    }

    [Fact]
    public void NoCheckersProbeNothing()
    {
        var fonts = new FakeFontSystem([new("ＭＳ 明朝", Fixed)], []);

        FontFamilyCatalog.Build(fonts, true, []);

        Assert.Empty(fonts.Probes);
    }

    [Theory]
    // A trail byte of 0x80 or more is sign-extended, lowering the lead byte by one.
    [InlineData("悪", 0x87ab)]   // CP932 88 AB
    [InlineData("洒", 0x9ead)]   // CP932 9F AD
    [InlineData("あ", 0x81a0)]   // CP932 82 A0
    [InlineData("ア", 0x8341)]   // CP932 83 41: a trail byte below 0x80 is kept
    [InlineData("ｱ", 0x00b1)]
    [InlineData("A", 0x0041)]
    public void CheckerCodesCombineTheBytesAsAgeDoes(string value, int code)
        => Assert.Equal((ushort)code, FontFamilyCatalog.CheckerCode(value));
}

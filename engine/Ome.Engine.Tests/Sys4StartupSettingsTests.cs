using System.Buffers.Binary;
using System.Text;
using Ome.Engine.Sys4;

public class Sys4StartupSettingsTests
{
    [Fact]
    [Trait("Category", "Workspace")]
    public void InstalledCatalogExposesOrderedStartupSettingsAndLogicalCanvas()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);

        Assert.Equal(36, catalog.StartupSettings.Count);
        Assert.True(catalog.StartupSettings.TryGetValue("SCREENX", out string? width));
        Assert.True(catalog.StartupSettings.TryGetValue("screeny", out string? height));
        Assert.Equal("800", width);
        Assert.Equal("600", height);
        Assert.Equal(new Sys4LogicalCanvas(800, 600), catalog.LogicalCanvas);
        Assert.True(catalog.StartupSettings.ClickOnUp); // Himegari: CLICKONUP=1
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData(" 2 ", true)]
    [InlineData("yes", false)]
    public void ClickOnUpIsAnyNonzeroInteger(string? value, bool expected)
    {
        var pairs = value == null ? Array.Empty<(string, string)>() : new[] { ("ClickOnUp", value) };
        Assert.Equal(expected, Sys4AssetCatalog.Parse(BuildCatalog(pairs)).StartupSettings.ClickOnUp);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("3", 3)]   // Himegari: the 設定 dialog's ｻｳﾝﾄﾞ page is dialog 102, without sound:Music's radios
    [InlineData("x", 0)]
    public void NoSetMusicDefaultsToZero(string? value, int expected)
    {
        var pairs = value == null ? Array.Empty<(string, string)>() : new[] { ("NoSetMusic", value) };
        Assert.Equal(expected, Sys4AssetCatalog.Parse(BuildCatalog(pairs)).StartupSettings.NoSetMusic);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("1", 1)]   // Kamidori
    [InlineData("x", 0)]
    public void BlankExtentModeDefaultsToZero(string? value, int expected)
    {
        var pairs = value == null ? Array.Empty<(string, string)>() : new[] { ("BlankExtentMode", value) };
        Assert.Equal(expected, Sys4AssetCatalog.Parse(BuildCatalog(pairs)).StartupSettings.BlankExtentMode);
    }

    [Fact]
    public void Sys4433FontAndRubyKeysUseTheirRegisteredDefaults()
    {
        Sys4StartupSettings missing = Sys4AssetCatalog.Parse(BuildCatalog()).StartupSettings;
        Assert.Equal((0, false, 0), (missing.DrawMode, missing.UseProportionalFont, missing.RubyShiftMode));
        Assert.Empty(missing.ValidFontCheckers);

        // Kamidori's trailer: VALIDFONTCHECKER repeats, and each pair adds a character in file order.
        Sys4StartupSettings kamidori = Sys4AssetCatalog.Parse(BuildCatalog(
            ("DRAWMODE", "1"), ("USEPROPORTIONALFONT", "1"), ("VALIDFONTCHECKER", "悪"),
            ("VALIDFONTCHECKER", "ｱ"), ("validfontchecker", "洒"), ("RUBYSHIFTMODE", "1"))).StartupSettings;
        Assert.Equal((1, true, 1), (kamidori.DrawMode, kamidori.UseProportionalFont, kamidori.RubyShiftMode));
        Assert.Equal(new[] { "悪", "ｱ", "洒" }, kamidori.ValidFontCheckers);
    }

    [Theory]
    [InlineData(null, null, null, 1, 0)]     // registered defaults: DrawMode 0 is in CreateObject 1
    [InlineData("1", "2", null, 2, 0)]       // Himegari
    [InlineData("1", "2", "3", 2, 3)]        // Kamidori
    [InlineData("1", "1", "3", 3, 3)]        // a mask without DrawMode's bit becomes 3
    [InlineData("0", "x", null, 1, 0)]
    public void CreateObjectHoldsTheDrawModesBit(
        string? drawMode, string? createObject, string? autoFreeTex, int expectedCreateObject, int expectedAutoFreeTex)
    {
        var pairs = new List<(string, string)>();
        if (drawMode != null) pairs.Add(("DRAWMODE", drawMode));
        if (createObject != null) pairs.Add(("CREATEOBJECT", createObject));
        if (autoFreeTex != null) pairs.Add(("AUTOFREETEX", autoFreeTex));

        Sys4StartupSettings settings = Sys4AssetCatalog.Parse(BuildCatalog(pairs.ToArray())).StartupSettings;

        Assert.Equal((expectedCreateObject, expectedAutoFreeTex), (settings.CreateObject, settings.AutoFreeTex));
    }

    [Theory]
    [InlineData(null, 1)]   // registered default
    [InlineData("2", 2)]    // Kamidori
    [InlineData("x", 1)]
    public void DependMovieSoundDefaultsToTheMusicRoute(string? value, int expected)
    {
        var pairs = value == null ? Array.Empty<(string, string)>() : new[] { ("DEPENDMOVIESOUND", value) };
        Assert.Equal(expected, Sys4AssetCatalog.Parse(BuildCatalog(pairs)).StartupSettings.DependMovieSound);
    }

    [Fact]
    public void SyntheticCatalogPreservesUnknownKeysAndUsesLastCaseInsensitiveValue()
    {
        var catalog = Sys4AssetCatalog.Parse(BuildCatalog(
            ("SCREENX", "320"),
            ("FutureProfileKey", "preserved"),
            ("screenx", "1024"),
            ("SCREENY", "576")));

        Assert.Equal(4, catalog.StartupSettings.Count);
        Assert.Equal("preserved", catalog.StartupSettings.GetValueOrDefault("futureprofilekey"));
        Assert.Equal("1024", catalog.StartupSettings.GetValueOrDefault("SCREENX"));
        Assert.Equal(new Sys4LogicalCanvas(1024, 576), catalog.LogicalCanvas);
        Assert.Equal("SCREENX", catalog.StartupSettings.Pairs[0].Key);
        Assert.Equal("screenx", catalog.StartupSettings.Pairs[2].Key);
    }

    [Theory]
    [InlineData(null, null, 640, 480)]
    [InlineData("1024", null, 1024, 480)]
    [InlineData(null, "576", 640, 576)]
    [InlineData("not-a-number", "0", 640, 480)]
    [InlineData("-1", " 768 ", 640, 768)]
    public void MissingOrInvalidDimensionsFallBackIndependently(
        string? width, string? height, int expectedWidth, int expectedHeight)
    {
        var pairs = new List<(string Key, string Value)>();
        if (width != null) pairs.Add(("SCREENX", width));
        if (height != null) pairs.Add(("SCREENY", height));

        var catalog = Sys4AssetCatalog.Parse(BuildCatalog(pairs.ToArray()));

        Assert.Equal(new Sys4LogicalCanvas(expectedWidth, expectedHeight), catalog.LogicalCanvas);
    }

    [Fact]
    public void AbsentSettingsTrailerUsesAgeFallback()
    {
        var catalog = Sys4AssetCatalog.Parse(BuildCatalog(includeTrailer: false));

        Assert.Empty(catalog.StartupSettings.Pairs);
        Assert.Equal(new Sys4LogicalCanvas(640, 480), catalog.LogicalCanvas);
    }

    [Theory]
    [InlineData("16385", "480")]
    [InlineData("16384", "16384")]
    public void UnsafeCanvasSizesAreRejected(string width, string height)
    {
        Assert.Throws<InvalidDataException>(() =>
            Sys4AssetCatalog.Parse(BuildCatalog(("SCREENX", width), ("SCREENY", height))));
    }

    [Fact]
    public void MalformedSettingsTrailerIsRejected()
    {
        byte[] truncated = BuildCatalog(("SCREENX", "1024"));
        Array.Resize(ref truncated, truncated.Length - 1);
        Assert.Throws<InvalidDataException>(() => Sys4AssetCatalog.Parse(truncated));

        byte[] expanded = BuildExpanded([("SCREENX", "1024")]);
        int trailer = 4 + 256 + 4 + 80;
        // Declare one extra string byte; the parser must not silently absorb or ignore malformed data.
        BinaryPrimitives.WriteUInt32LittleEndian(expanded.AsSpan(trailer + 4, 4),
                                                  ReadU32(expanded, trailer + 4) + 1);
        Assert.Throws<InvalidDataException>(() =>
            Sys4AssetCatalog.Parse(WrapCatalog(expanded), "malformed-settings"));

        byte[] missingNul = BuildExpanded([("SCREENX", "1024")]);
        missingNul[^1] = (byte)'x';
        Assert.Throws<InvalidDataException>(() =>
            Sys4AssetCatalog.Parse(WrapCatalog(missingNul), "unterminated-settings"));

        byte[] countMismatch = BuildExpanded([("SCREENX", "1024")]);
        BinaryPrimitives.WriteUInt32LittleEndian(countMismatch.AsSpan(trailer + 8, 4), 2);
        Assert.Throws<InvalidDataException>(() =>
            Sys4AssetCatalog.Parse(WrapCatalog(countMismatch), "miscounted-settings"));
    }

    internal static byte[] BuildCatalog(
        params (string Key, string Value)[] pairs)
        => WrapCatalog(BuildExpanded(pairs));

    internal static byte[] BuildCatalog(bool includeTrailer, string fileName = "@")
        => WrapCatalog(BuildExpanded(Array.Empty<(string, string)>(), includeTrailer, [fileName]));

    /// <summary>A catalog listing <paramref name="fileNames"/>, each an empty entry of DATA.ALF, under the catalog
    /// revision <paramref name="magic"/> and <paramref name="title"/> (a game's identity).</summary>
    internal static byte[] BuildCatalog(IReadOnlyList<string> fileNames, string magic = "S4IC422 ", string title = "Synthetic")
        => WrapCatalog(BuildExpanded(Array.Empty<(string, string)>(), includeTrailer: true, fileNames), magic, title);

    /// <summary>A catalog with <paramref name="fileNames"/> and the startup settings <paramref name="pairs"/>, under a
    /// game's identity: an install's SYS4INI.BIN as the launcher recognizes it.</summary>
    internal static byte[] BuildCatalog(IReadOnlyList<string> fileNames, (string Key, string Value)[] pairs, string magic,
                                        string title)
        => WrapCatalog(BuildExpanded(pairs, includeTrailer: true, fileNames), magic, title);

    private static byte[] BuildExpanded(
        (string Key, string Value)[] pairs, bool includeTrailer = true, IReadOnlyList<string>? fileNames = null)
        => SyntheticCatalog.Expanded(pairs, includeTrailer, fileNames);

    private static byte[] WrapCatalog(byte[] expanded, string magic = "S4IC422 ", string title = "Synthetic")
        => SyntheticCatalog.Wrap(expanded, magic, title);

    private static uint ReadU32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
}

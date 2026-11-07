using System.Buffers.Binary;
using System.Text;
using Age.Engine.Sys4;

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
        => WrapCatalog(BuildExpanded(Array.Empty<(string, string)>(), includeTrailer, fileName));

    private static byte[] BuildExpanded(
        (string Key, string Value)[] pairs, bool includeTrailer = true, string fileName = "@")
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp932 = Encoding.GetEncoding(932);
        var blob = new List<byte>();
        AddU32(blob, 1);
        AddFixedString(blob, "DATA.ALF", 256, cp932);
        AddU32(blob, 1);
        AddFixedString(blob, fileName, 64, cp932);
        AddU32(blob, 0);
        AddU32(blob, 0);
        AddU32(blob, 0);
        AddU32(blob, 0);
        if (!includeTrailer) return blob.ToArray();

        AddU32(blob, 0); // VM metadata bytes
        var strings = new List<byte>();
        foreach (var pair in pairs)
        {
            AddCString(strings, pair.Key, cp932);
            AddCString(strings, pair.Value, cp932);
        }
        AddU32(blob, checked((uint)strings.Count));
        if (strings.Count == 0) return blob.ToArray();
        AddU32(blob, checked((uint)pairs.Length));
        blob.AddRange(strings);
        return blob.ToArray();
    }

    private static byte[] WrapCatalog(byte[] expanded)
    {
        byte[] packed = LiteralLzss(expanded);
        var catalog = new byte[0x138 + packed.Length];
        Encoding.ASCII.GetBytes("S4IC422 ").CopyTo(catalog, 0);
        Encoding.ASCII.GetBytes("Synthetic").CopyTo(catalog, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x12c, 4),
                                                  checked((uint)expanded.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x134, 4),
                                                  checked((uint)packed.Length));
        packed.CopyTo(catalog, 0x138);
        return catalog;
    }

    private static byte[] LiteralLzss(byte[] source)
    {
        var packed = new List<byte>();
        for (int offset = 0; offset < source.Length;)
        {
            int count = Math.Min(8, source.Length - offset);
            packed.Add((byte)((1 << count) - 1));
            for (int i = 0; i < count; i++) packed.Add(source[offset++]);
        }
        return packed.ToArray();
    }

    private static void AddFixedString(List<byte> target, string value, int size, Encoding encoding)
    {
        byte[] bytes = encoding.GetBytes(value);
        if (bytes.Length >= size) throw new ArgumentException("test string is too long");
        target.AddRange(bytes);
        target.AddRange(new byte[size - bytes.Length]);
    }

    private static void AddCString(List<byte> target, string value, Encoding encoding)
    {
        target.AddRange(encoding.GetBytes(value));
        target.Add(0);
    }

    private static void AddU32(List<byte> target, uint value)
    {
        int offset = target.Count;
        target.AddRange(new byte[4]);
        BinaryPrimitives.WriteUInt32LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(target).Slice(offset, 4), value);
    }

    private static uint ReadU32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
}

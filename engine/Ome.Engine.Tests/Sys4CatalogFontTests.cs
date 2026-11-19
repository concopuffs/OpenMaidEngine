using Ome.Engine.Sys4;

/// <summary>
/// The font list that follows the settings pairs in a catalog since AGE 4.38D (docs/sys4-format-notes.md, "The font
/// list"; native engine_load_sys4ini_catalog@0x414290 of 創刻のアテリアル's build): a byte count, then records of a
/// catalog file id and a NUL-terminated face name until that many bytes are read.
/// </summary>
public sealed class Sys4CatalogFontTests
{
    private static Sys4AssetCatalog Parse(byte[] afterSettings, params (string Key, string Value)[] pairs)
        => Sys4AssetCatalog.Parse(SyntheticCatalog.Wrap(
            [.. SyntheticCatalog.Expanded(pairs, includeTrailer: true, ["A.BIN", "FONT.TTF"]), .. afterSettings]));

    private static byte[] Record(int fileId, string faceName)
        => [.. BitConverter.GetBytes(fileId), .. System.Text.Encoding.ASCII.GetBytes(faceName), 0];

    private static byte[] List(params byte[][] records)
    {
        byte[] body = records.SelectMany(record => record).ToArray();
        return [.. BitConverter.GetBytes(body.Length), .. body];
    }

    [Fact]
    public void ACatalogWithoutTheListHasNoFonts()
    {
        Assert.Empty(Parse([], ("SCREENX", "800")).Fonts);
        Assert.Empty(Sys4AssetCatalog.Parse(SyntheticCatalog.Build(["A.BIN"])).Fonts);
    }

    [Fact]
    public void RecordsAreReadUntilTheListsBytesAreUsed()
    {
        // 創刻のアテリアル's list is the one record: file 37 (AGE-EXTEND.TTF) as "AGE Extend", 15 bytes.
        byte[] arterial = List(Record(37, "AGE Extend"));
        Assert.Equal(new byte[] { 0x0f, 0, 0, 0, 0x25, 0, 0, 0 }, arterial[..8]);
        Assert.Equal(new[] { new Sys4CatalogFont(37, "AGE Extend") }, Parse(arterial, ("SCREENX", "1024")).Fonts);

        Sys4AssetCatalog two = Parse(List(Record(1, "First Face"), Record(7, "Second")), ("SCREENX", "1024"));
        Assert.Equal(new[] { new Sys4CatalogFont(1, "First Face"), new Sys4CatalogFont(7, "Second") }, two.Fonts);
        Assert.Equal("1024", two.StartupSettings.GetValueOrDefault("SCREENX"));
    }

    [Fact]
    public void AListFollowsASettingsBlockWithoutStringsToo()
    {
        // Native steps over a settings count below 1 and reads the font list next.
        Assert.Equal(new[] { new Sys4CatalogFont(3, "Face") }, Parse(List(Record(3, "Face"))).Fonts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ACountBelowOneMeansNoFonts(int count)
        => Assert.Empty(Parse(BitConverter.GetBytes(count), ("SCREENX", "800")).Fonts);

    [Fact]
    public void AListThatDoesNotFitOrBytesAfterItAreRefused()
    {
        byte[] list = List(Record(37, "AGE Extend"));
        var pairs = new[] { ("SCREENX", "800") };

        // The count runs past the catalog.
        Assert.Contains("font list length", Assert.Throws<InvalidDataException>(
            () => Parse([.. BitConverter.GetBytes(list.Length), .. list[4..^1]], pairs)).Message);
        // The last record has no room for its file id.
        Assert.Contains("truncated", Assert.Throws<InvalidDataException>(
            () => Parse([.. BitConverter.GetBytes(list.Length - 4 + 2), .. list[4..], 9, 9], pairs)).Message);
        // A face name without its NUL inside the list.
        Assert.Contains("not NUL-terminated", Assert.Throws<InvalidDataException>(
            () => Parse([.. BitConverter.GetBytes(list.Length - 5), .. list[4..^1]], pairs)).Message);
        // Bytes after the list are a block nobody has read: the engine's reader stops there.
        Assert.Contains("trailing bytes", Assert.Throws<InvalidDataException>(
            () => Parse([.. list, 1, 2, 3], pairs)).Message);
        Assert.Contains("trailing bytes", Assert.Throws<InvalidDataException>(
            () => Parse([0, 0, 0, 0, 1], pairs)).Message);
    }
}

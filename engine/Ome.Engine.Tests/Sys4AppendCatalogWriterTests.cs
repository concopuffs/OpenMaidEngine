using System;
using System.IO;
using System.Linq;
using Ome.Engine.Sys4;
using Xunit;

/// <summary>Generated S4AC append catalogs read back through the runtime parser and mount beside a real base catalog
/// (spec §9.3: a native mod's new scripts).</summary>
public class Sys4AppendCatalogWriterTests
{
    private static readonly Sys4AppendCatalogWriter.File[] Files =
    [
        new("$2$OMSAUTO.BIN", Enumerable.Range(0, 300).Select(i => (byte)i).ToArray()),
        new("$2$OMSHELLO.BIN", [1, 2, 3, 4, 5, 6, 7, 8]),
    ];

    [Fact]
    public void AGeneratedCatalogParsesWithItsSelectorRecordsAndArchiveRanges()
    {
        var (catalogBytes, archive) = Sys4AppendCatalogWriter.Write("S4AC422 ", "姫狩りダンジョンマイスター", 2,
            "APPEND02.ALF", Files);
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Parse(catalogBytes, "APPEND02.AAI");
        Assert.Equal("S4AC422", catalog.Magic.TrimEnd());
        Assert.Equal("姫狩りダンジョンマイスター", catalog.Title);
        Assert.Equal(2, catalog.PackId);
        Assert.Equal(["APPEND02.ALF"], catalog.Archives);
        Assert.Equal([0x02000000, 0x02000001], catalog.Files.Select(file => file.PackedId));
        Assert.Equal(["$2$OMSAUTO.BIN", "$2$OMSHELLO.BIN"], catalog.Files.Select(file => file.Name));
        Assert.Equal(Files[1].Bytes, archive.AsSpan((int)catalog.Files[1].Offset, (int)catalog.Files[1].Size).ToArray());
        Assert.Equal(308, archive.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x80)]
    public void SelectorsOutsideTheNativeMountTableAreRejected(int selector)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            Sys4AppendCatalogWriter.Write("S4AC422 ", "t", selector, "APPEND02.ALF", Files));

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    public void AGeneratedCatalogMountsBesideTheInstalledBaseCatalog()
    {
        string install = Path.Combine(Paths.Workspace, "Himegari_Game");
        string scratch = Directory.CreateTempSubdirectory("ome-append-").FullName;
        try
        {
            System.IO.File.Copy(Path.Combine(install, "SYS4INI.BIN"), Path.Combine(scratch, "SYS4INI.BIN"));
            Sys4AssetCatalog baseCatalog = Sys4AssetCatalog.Parse(System.IO.File.ReadAllBytes(Path.Combine(scratch, "SYS4INI.BIN")));
            var (catalogBytes, archive) = Sys4AppendCatalogWriter.Write("S4AC422 ", baseCatalog.Title, 2, "APPEND02.ALF", Files);
            System.IO.File.WriteAllBytes(Path.Combine(scratch, "APPEND02.AAI"), catalogBytes);
            System.IO.File.WriteAllBytes(Path.Combine(scratch, "APPEND02.ALF"), archive);

            Sys4AssetCatalog mounted = Sys4AssetCatalog.Load(Path.Combine(scratch, "SYS4INI.BIN"));
            AssetEntry hello = Assert.IsType<AssetEntry>(mounted.ResolvePacked(0x02000001));
            Assert.Equal("$2$OMSHELLO.BIN", hello.Name);
            var store = new Sys4AssetStore(mounted, scratch, scratch);
            Assert.Equal(Files[1].Bytes, store.ReadAll(hello));
            Assert.Equal(Files[0].Bytes, store.ReadAll(mounted.ResolvePacked(0x02000000)!));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}

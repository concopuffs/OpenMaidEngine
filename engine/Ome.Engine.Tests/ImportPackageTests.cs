using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Linking;
using Ome.Mods.Localization.Import;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;
using Ome.Script.Abi;
using Xunit;

/// <summary><c>omp text import-scripts --package</c> (localization spec §12, §18.1): the replaced images written as
/// language assets, and the manifest that makes the output folder a package.</summary>
public sealed class ImportPackageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ome-import-package-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A 1×1 24-bit bottom-up BMP of one colour, as the Kamidori translation stores under AGF names.</summary>
    private static byte[] Bmp(byte r, byte g, byte b)
    {
        const int pixelOffset = 14 + 40;
        var file = new byte[pixelOffset + 4];
        "BM"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), pixelOffset);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), 1);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(34), 4);
        (file[pixelOffset], file[pixelOffset + 1], file[pixelOffset + 2]) = (b, g, r);
        return file;
    }

    private string WriteImages(AssetCounts counts)
    {
        string folder = Path.Combine(_root, "assets", "en");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "OLD.png"), [1]);       // an earlier run's image
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");   // not an image: kept
        byte[] photo = Bmp(0, 0, 255);
        ImportPackage.WriteAssets(["AE000AA.AGF", "$1$SW0118.AGF", "PHOTO.BMP", "BROKEN.AGF"],
            name => name switch
            {
                "AE000AA.AGF" => Bmp(255, 0, 0),
                "$1$SW0118.AGF" => Bmp(0, 255, 0),
                "PHOTO.BMP" => photo,
                _ => "xyz"u8.ToArray(),
            }, folder, counts);
        return folder;
    }

    [Fact]
    public void AnAgfBecomesAPngAndAnyOtherImageIsCopied()
    {
        var counts = new AssetCounts();
        string folder = WriteImages(counts);

        Assert.Equal(["$1$SW0118.png", "AE000AA.png", "PHOTO.BMP", "notes.txt"],
                     Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        RgbaImage red = PngDecoder.Decode(File.ReadAllBytes(Path.Combine(folder, "AE000AA.png")));
        Assert.Equal((1, 1), (red.Width, red.Height));
        Assert.Equal([255, 0, 0, 255], red.Pixels);
        Assert.Equal(Bmp(0, 0, 255), File.ReadAllBytes(Path.Combine(folder, "PHOTO.BMP")));
        Assert.Equal(3, counts.Written);
        Assert.StartsWith("BROKEN.AGF: ", Assert.Single(counts.NotWritten));
    }

    [Fact]
    public void TheGlyphDelayScaleIsTheSourcesCharactersOverTheTranslations()
    {
        Assert.Equal(0.5, ImportPackage.GlyphDelayScale(
        [
            TextEntry.Literal("あいう", "abcdef"),
            TextEntry.Literal("えお", "えお"),            // untranslated: not counted
            TextEntry.Literal("かきくけ", "abcdefgh"),
        ]));
        Assert.Equal(1, ImportPackage.GlyphDelayScale([TextEntry.Literal("えお", "えお")]));
    }

    [Fact]
    public void TheOutputFolderOpensAsAPackageWithTheLanguageAndItsAssets()
    {
        WriteImages(new AssetCounts());
        File.Delete(Path.Combine(_root, "assets", "en", "notes.txt"));
        File.WriteAllText(Path.Combine(_root, ManifestReader.FileName), ImportPackage.Manifest("kamidori", "en",
            ImportPackage.LanguageName("en"), "Kamidori_Translated", 0.48, ["Imported for the test.", "Local test data."]));

        using ModPackage package = ModPackage.Open(_root);
        ModManifest manifest = package.Manifest;
        Assert.Equal(("local.kamidori-en", "English (Kamidori_Translated, imported)", "kamidori", "native", 2),
                     (manifest.Id, manifest.Name, manifest.Profile, manifest.Target, manifest.OmpVersion));
        var language = Assert.Single(manifest.Languages);
        Assert.Equal(("en", "English", 0.48), (language.Tag, language.Name, language.GlyphDelayScale));
        Assert.Equal(["$1$SW0118.AGF", "AE000AA.AGF", "PHOTO.BMP"],
                     package.Layout.LanguageAssets.Select(file => file.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ALanguageTheSystemDoesNotListIsNamedByItsTag()
        => Assert.Equal("x-unknown-tag", ImportPackage.LanguageName("x-unknown-tag"));

    // An append record (a $n$ name the install's *.AAI catalog lists) is a base asset, as AGE and the port's store
    // open it by that name: a mod may override it, and a language asset may replace it.
    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    public void AnAppendRecordIsABaseAsset()
    {
        File.Copy(Paths.Sys4Ini, Path.Combine(_root, "SYS4INI.BIN"));
        Sys4AssetCatalog installed = Sys4AssetCatalog.Parse(File.ReadAllBytes(Paths.Sys4Ini));
        var (catalogBytes, archive) = Sys4AppendCatalogWriter.Write("S4AC422 ", installed.Title, 2, "APPEND02.ALF",
            [new Sys4AppendCatalogWriter.File("$2$OMSIMAGE.AGF", Bmp(1, 2, 3))]);
        File.WriteAllBytes(Path.Combine(_root, "APPEND02.AAI"), catalogBytes);
        File.WriteAllBytes(Path.Combine(_root, "APPEND02.ALF"), archive);

        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(Path.Combine(_root, "SYS4INI.BIN"));
        ModLinkBase linkBase = ModLinkBase.ForInstall("himegari", Path.Combine(_root, "SYS4INI.BIN"), catalog,
            new Sys4AssetStore(catalog, _root, _root), OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422"),
            OpcodeContracts.Load(Paths.OpcodesJson));

        Assert.True(linkBase.HasAsset("$2$OMSIMAGE.AGF"));
        Assert.True(linkBase.HasAsset("$2$omsimage.agf"));
        Assert.False(linkBase.HasAsset("$2$OMSOTHER.AGF"));
        Assert.False(linkBase.HasAsset("OMSIMAGE.AGF"));
    }
}

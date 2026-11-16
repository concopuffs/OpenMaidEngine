using System.Text.RegularExpressions;
using Ome.Engine.Sys4;
using Ome.Mods.Linking;
using Ome.Script.Abi;

/// <summary>Launcher spec §5.9, the first item: the engine, <c>Ome.Cli</c>'s <c>omp</c> commands and the launcher link
/// from one base (<see cref="ModLinkBase.ForGame"/>) over the opcode metadata embedded in <c>Ome.Engine</c>.</summary>
public sealed class SharedLinkBaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-linkbase-" + Guid.NewGuid().ToString("N"));

    public SharedLinkBaseTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheEmbeddedMetadataIsTheBuiltFile()
    {
        using Stream embedded = OpcodeTableJson.OpenEmbedded();
        using var copy = new MemoryStream();
        embedded.CopyTo(copy);
        Assert.Equal(File.ReadAllBytes(Paths.OpcodesJson), copy.ToArray());
    }

    /// <summary>The scripts a profile's OMS modules run (<c>run NAME</c>), as catalog names: a synthetic install
    /// lists them so the modules' idioms build.</summary>
    internal static string[] ScriptsTheModulesRun(string profile) => SyntheticCatalog.ScriptsTheModulesRun(profile);

    [Theory]
    [InlineData("himegari", "SYS4422")]
    [InlineData("kamidori", "SYS4433")]
    public void TheSharedBaseHasTheToolchainStampOfTheRepositorysFiles(string profile, string abiId)
    {
        // The host linked from its embedded copy and Ome.Cli from the repository's files; both now use ForGame, and
        // it stamps a link as the repository's files do, so neither mounts an entry the other would not.
        string[] scripts = ScriptsTheModulesRun(profile);
        Assert.NotEmpty(scripts);
        string sys4Ini = Path.Combine(_root, "SYS4INI.BIN");
        File.WriteAllBytes(sys4Ini, Sys4StartupSettingsTests.BuildCatalog(scripts));
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var store = new Sys4AssetStore(catalog, _root, _root);

        ModLinkBase shared = ModLinkBase.ForGame(profile, abiId, sys4Ini, catalog, store, out bool repositoryToolchain);
        ModLinkBase fromFiles = ModLinkBase.ForInstall(profile, sys4Ini, catalog, store,
            OpcodeTableJson.Load(Paths.OpcodesJson, abiId), OpcodeContracts.Load(Paths.OpcodesJson),
            Paths.GlobalsJson(profile), Paths.OmsModules(profile), Paths.PatchPointsJson(profile));

        Assert.True(repositoryToolchain);
        Assert.Equal(abiId, shared.Table.AbiId);
        Assert.Equal(fromFiles.ToolchainStamp, shared.ToolchainStamp);
        Assert.Equal(fromFiles.Modules.Count, shared.Modules.Count);
    }

    [Fact]
    public void ModulesWithoutTheirGeneratedGlobalsAreAnInvalidBaseThatSaysWhatToBuild()
    {
        // Launcher plan §9.5: a program inside a fresh checkout links with the repository's modules, and build/ has no
        // globals until they are built. Every linker reports an InvalidDataException; the compile error was a crash.
        string sys4Ini = Path.Combine(_root, "SYS4INI.BIN");
        File.WriteAllBytes(sys4Ini, Sys4StartupSettingsTests.BuildCatalog(ScriptsTheModulesRun("himegari")));
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        string missing = Path.Combine(_root, "build", "games", "himegari", "globals.json");

        var error = Assert.Throws<InvalidDataException>(() => ModLinkBase.ForInstall(
            "himegari", sys4Ini, catalog, new Sys4AssetStore(catalog, _root, _root),
            OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422"), OpcodeContracts.Load(Paths.OpcodesJson),
            missing, Paths.OmsModules("himegari"), Paths.PatchPointsJson("himegari")));

        Assert.StartsWith($"the OMS modules in {Paths.OmsModules("himegari")} do not build: ", error.Message);
        Assert.Contains("unknown module 'himegari.globals'", error.Message);
        Assert.EndsWith($"they use the generated {missing}, which is missing: run tools/globals_build.py --game himegari "
                        + "--build", error.Message);
    }
}

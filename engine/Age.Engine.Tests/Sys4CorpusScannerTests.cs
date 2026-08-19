using Age.Engine.Model;
using Age.Engine.Sys4;
using Xunit;

public class Sys4CorpusScannerTests
{
    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [Fact]
    public void InstalledHimegariCatalogCorpusDecodesThroughRuntimeStore()
        => AssertInstalledCorpus("himegari", "Himegari_Game", "SYS4422");

    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [Fact]
    public void InstalledKamidoriCatalogCorpusDecodesThroughRuntimeStore()
        => AssertInstalledCorpus("kamidori", "Kamidori", "SYS4433");

    private static void AssertInstalledCorpus(
        string profileId, string directoryName, string abiId)
    {
        string gameRoot = Environment.GetEnvironmentVariable("AGE_GAME_ROOT")
            ?? Path.Combine(Paths.Workspace, directoryName);
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"{profileId} install not found at {gameRoot}");

        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, abiId);
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var provider = new Sys4ScriptProvider(
            table, catalog, new Sys4AssetStore(catalog, gameRoot, gameRoot));

        Sys4CorpusScanResult scan = Sys4CorpusScanner.Scan(catalog, provider);

        Assert.True(scan.ScriptCount > 0);
        Assert.Equal(scan.ScriptCount, scan.BaseScriptCount + scan.AppendScriptCount);
        Assert.True(scan.InstructionCount > 0);
        Assert.True(scan.OpcodeOccurrences.Count > 0);
        Assert.Empty(scan.Failures);
        if (profileId == "kamidori")
        {
            Assert.True(scan.AppendScriptCount > 0);
            Assert.True(scan.OpcodeOccurrences.ContainsKey(0x1be));
        }
    }
}

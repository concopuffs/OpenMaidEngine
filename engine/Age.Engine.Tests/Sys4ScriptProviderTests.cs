using Age.Engine.Sys4;
using Age.Engine.Diagnostics;
using Xunit;

public class Sys4ScriptProviderTests
{
    [Fact]
    public void ResolvesKnownIdsToTheirScripts()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(table);

        var additem = provider.GetById(0x1ab);   // ADDITEM.BIN
        var mes = provider.GetById(0x2ae7);      // MES.BIN
        Assert.NotNull(additem);
        Assert.NotNull(mes);
        Assert.True(additem!.Instructions.Count > 0);
        Assert.Same(additem, provider.GetById(0x1ab));   // cached: same instance
        Assert.Null(provider.GetById(long.MaxValue));    // unknown id
    }

    [Fact]
    public void HighByteSelectsAppendPackWithoutReplacingBaseNames()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(table);
        var append = provider.Catalog.AppendPacks[1];
        var entry = append.Files.Single(e => e.Name == "$1$SC1260.BIN");
        long packedId = 0x01000000L | (uint)entry.RawIndex;

        var script = provider.GetById(packedId);
        Assert.NotNull(script);
        Assert.True(script!.Instructions.Count > 0);
        Assert.Null(provider.GetByName("$1$SC1260.BIN"));
    }

    [Fact]
    public void RootScriptLoadingUsesTheSameAssetStoreAndLoosePrecedence()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(table);
        var patched = provider.RequireByName("FIELD.BIN");
        var directLoose = Sys4Loader.Load(Path.Combine(Paths.GameDir, "FIELD.BIN"), table);

        Assert.Equal(directLoose.Instructions.Count, patched.Instructions.Count);
        Assert.Same(patched, provider.RequireByName("field.bin"));
        Assert.Null(provider.GetByName("../FIELD.BIN"));
        Assert.Equal(481, provider.ScriptNames.Count);
    }

    [Fact]
    public void DebugCatalogPreservesPackedIdentityAcrossBaseAndAppendPacks()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var entries = DebugSceneCatalog.Build(catalog);

        Assert.Equal(catalog.EnumerateScripts().Count, entries.Count);
        Assert.Equal(481, entries.Count(entry => entry.PackId == 0));
        Assert.Contains(entries, entry => entry.Name == "$1$SC1260.BIN"
                                          && entry.PackedId == (0x01000000L | (uint)entry.RawIndex)
                                          && entry.Kind == DebugScriptKind.Scenario);
        Assert.All(entries, entry => Assert.Same(
            catalog.ResolvePacked(entry.PackedId),
            catalog.AppendPacks.GetValueOrDefault(entry.PackId, catalog).ResolveRaw(entry.RawIndex)));
        Assert.Equal(entries.Count, entries.Select(entry => entry.PackedId).Distinct().Count());
    }

    [Fact]
    public void DebugCatalogFiltersByProfileCategoryNameAndPackedId()
    {
        var entries = DebugSceneCatalog.Build(Sys4AssetCatalog.Load(Paths.Sys4Ini));

        var scenarios = DebugSceneCatalog.Filter(entries, DebugScriptFilter.Scenario, "");
        Assert.NotEmpty(scenarios);
        Assert.All(scenarios, entry => Assert.Equal(DebugScriptKind.Scenario, entry.Kind));
        Assert.Contains(scenarios, entry => entry.Name.Equals("SC0000.BIN", StringComparison.OrdinalIgnoreCase));

        var debug = DebugSceneCatalog.Filter(entries, DebugScriptFilter.Debug, "DEBUG.BIN");
        var exact = Assert.Single(debug);
        Assert.Equal("DEBUG.BIN", exact.Name);
        Assert.True(exact.Launchable);
        Assert.Equal(exact, Assert.Single(DebugSceneCatalog.Filter(entries, DebugScriptFilter.All,
            $"0x{exact.PackedId:x}")));
        Assert.Equal(exact, Assert.Single(DebugSceneCatalog.Filter(entries, DebugScriptFilter.All,
            exact.PackedId.ToString())));

        Assert.False(entries.Single(entry => entry.Name == "SYSTEM4.BIN").Launchable);
        Assert.False(entries.Single(entry => entry.Name == "TITLE.BIN").Launchable);
    }
}

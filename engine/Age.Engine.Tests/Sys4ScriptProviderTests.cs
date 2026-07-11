using Age.Engine.Sys4;
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
}

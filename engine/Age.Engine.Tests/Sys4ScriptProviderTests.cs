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
}

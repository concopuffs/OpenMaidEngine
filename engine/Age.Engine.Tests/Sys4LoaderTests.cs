using Age.Engine.Sys4;
using Xunit;

public class Sys4LoaderTests
{
    [Fact]
    [Trait("Category", "Workspace")]
    public void ParsesMenuBinLikeSys4load()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var s = Sys4Loader.Load(Path.Combine(Paths.Data1, "MENU.BIN"), table);
        // header F0..F5 from `sys4load MENU.BIN --summary`
        Assert.Equal(0x17, s.Header.LocalInt1);
        Assert.Equal(1, s.Header.LocalFloats);
        Assert.Equal(1, s.Header.LocalStrings1);
        Assert.Equal(2, s.Header.LocalInt2);
        Assert.Equal(1, s.Header.Unknown);
        Assert.Equal(1, s.Header.LocalStrings2);
        Assert.Equal(148, s.Instructions.Count);   // sys4load: 148 instructions
        Assert.Equal(3, s.Strings.Count);           // sys4load: 3 inline strings
        Assert.All(s.Instructions, ins => Assert.True(s.IndexByOffset.ContainsKey(ins.Offset)));
    }
}

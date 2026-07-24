using Age.Engine.Sys4;
using Xunit;

public class OpcodeTableTests
{
    [Fact]
    public void LoadsObservedOpcodesPlusMappedUnusedAbiEntries()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        Assert.Equal(249, t.Count); // 248 corpus-observed + unused persistence ABI opcode 0x19f
        Assert.True(t.TryGet(0x55, out var label, out var argc));
        Assert.Equal("mov", label);
        Assert.Equal(2, argc);
        Assert.Equal("u0041BEB0", t.Label(0x90));
        Assert.Equal(7, t.Argc(0x90));
        Assert.Equal(2, t.Argc(0x19f));
        Assert.Equal(-1, t.Argc(0x9999)); // absent -> -1
    }
}

using Age.Engine.Model;
using Age.Engine.Sys4;
using Xunit;

public class OpcodeTableTests
{
    [Fact]
    public void LoadsCompleteAgeCatalog()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        Assert.Equal(548, t.Count); // 248 Himegari-observed + 300 broader AGE compatibility stubs
        Assert.True(t.TryGet(0x55, out var label, out var argc));
        Assert.Equal("mov", label);
        Assert.Equal(2, argc);
        Assert.Equal("u0041BEB0", t.Label(0x90));
        Assert.Equal(7, t.Argc(0x90));
        Assert.Equal(2, t.Argc(0x19f));
        Assert.Equal(2, t.Argc(0x1be)); // Kamidori probe blocker; catalog-only in Himegari
        Assert.Equal(-1, t.Argc(0x9999)); // absent -> -1
    }

    [Fact]
    public void CatalogOnlyOpcodeDoesNotTruncateFollowingInstructions()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(t, "CROSS_GAME_STUB", new List<(int, Operand[])>
        {
            (0x1be, new[] { new Operand(0, 11), new Operand(0, 22) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());

        Assert.Equal(2, script.Instructions.Count);
        Assert.Equal(0x1be, script.Instructions[0].Opcode);
        Assert.Equal(2, script.Instructions[0].Args.Count);
        Assert.Equal(0x2, script.Instructions[1].Opcode);
    }
}

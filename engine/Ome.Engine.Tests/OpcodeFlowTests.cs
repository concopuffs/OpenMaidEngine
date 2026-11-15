using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// The two control facts of each implemented opcode (Lua scripting spec §7.1): vm-map/opcodes.toml's <c>waits</c> and
/// <c>leaves</c>, derived from the port's handlers by tools/opcode_flow.py, loaded with the opcode table, and checked
/// against every instruction the tests run (<see cref="VirtualMachine.CheckSwitch"/>, on in this project).
/// </summary>
public class OpcodeFlowTests
{
    private static readonly OpcodeTable Catalog = OpcodeTableJson.Load(Paths.OpcodesJson);

    private static OpcodeDefinition Named(string name)
        => Catalog.Entries.Single(definition => definition.CanonicalLabel == name);

    [Theory]
    [MemberData(nameof(OpcodeHandlerTableTests.GameAbis), MemberType = typeof(OpcodeHandlerTableTests))]
    public void EveryImplementedOpcodeHasItsFactsAndNoOtherDoes(string abi)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, abi);
        string[] wrong = table.Entries
            .Where(definition => VirtualMachine.Implements(table, definition.Opcode) != (definition.Waits != null))
            .Select(definition => $"0x{definition.Opcode:x} {definition.CanonicalLabel}")
            .ToArray();
        Assert.Empty(wrong);
    }

    [Fact]
    public void TheFactsAreTheHandlers()
    {
        Assert.Equal((OpcodeWaits.Never, OpcodeLeaves.Jumps), (Named("jcc").Waits, Named("jcc").Leaves));
        Assert.Equal(OpcodeLeaves.Ends, Named("exit").Leaves);
        Assert.Equal((OpcodeWaits.Never, OpcodeLeaves.Falls), (Named("mov").Waits, Named("mov").Leaves));
        // The ADV wait runs the menu's callbacks inside it, and one of them can return to title.
        OpcodeDefinition wait = Named("wait-for-input");
        Assert.Equal(OpcodeWaits.May, wait.Waits);
        Assert.Equal(OpcodeLeaves.Falls | OpcodeLeaves.Calls | OpcodeLeaves.Ends, wait.Leaves);
        Assert.Equal(OpcodeWaits.Always, Named("show-and-clear-diagnostic").Waits);
        Assert.True(Named("call-script").Leaves.HasFlag(OpcodeLeaves.Calls));
        Assert.True(Named("save-numbered-slot").Leaves.HasFlag(OpcodeLeaves.Saves));
        Assert.True(Named("load-numbered-slot-and-resume").Leaves.HasFlag(OpcodeLeaves.Loads));
        Assert.True(Named("continue-save-load-stack-restore").Leaves.HasFlag(OpcodeLeaves.Restores));
        // An opcode the port does not implement has none.
        Assert.True(Catalog.TryGetDefinition(0x4, out OpcodeDefinition unimplemented));
        Assert.Equal((null, OpcodeLeaves.None), (unimplemented.Waits, unimplemented.Leaves));
    }

    [Fact]
    public void TheTestsCheckEveryInstruction()
        => Assert.True(AppContext.TryGetSwitch(VirtualMachine.CheckSwitch, out bool on) && on);

    [Fact]
    public void AnInstructionThatLeavesAsItsFactsDenyStopsTheRun()
    {
        // exit ends its frame; a table that says it only falls through is wrong, and the check names the opcode.
        OpcodeDefinition exit = Named("exit");
        var table = new OpcodeTable(Catalog.Entries.ToDictionary(
            definition => definition.Opcode,
            definition => definition.Opcode == exit.Opcode ? definition with { Leaves = OpcodeLeaves.Falls } : definition));
        var vm = new VirtualMachine(Asm(table, "FLOW", (uint)exit.Opcode), table, new HeadlessWaits(), null, null, null);
        var error = Assert.Throws<OpcodeFlowException>(() => vm.Run());
        Assert.Contains($"FLOW@0x0: op 0x{exit.Opcode:x} exit left by Ends", error.Message);
    }

    private static Script Asm(OpcodeTable t, string name, params uint[] body)
    {
        var bytes = new byte[0x3C + body.Length * 4];
        System.Text.Encoding.ASCII.GetBytes("SYS4422 ").CopyTo(bytes, 0);
        BitConverter.GetBytes(body.Length).CopyTo(bytes, 8 + 8 * 4);
        for (int i = 0; i < body.Length; i++) BitConverter.GetBytes(body[i]).CopyTo(bytes, 0x3C + i * 4);
        return Sys4Loader.Parse(bytes, t, name);
    }
}

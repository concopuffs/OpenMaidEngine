using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class PointerAndBlockCopyOpsTests
{
    private const int Immediate = 0, InlineString = 2, GlobalInt = 3, GlobalString = 5,
                      LocalInt = 9, LocalPointer = 12, LocalStringPointer = 14;

    [Fact]
    public void TakeAddressAndCopyDwords_PreserveLocalAndGlobalAddressDomains()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int move = table.ByLabel("mov")!.Value;
        int lookup = table.ByLabel("lookup-array")!.Value;
        var script = ScriptAssembler.Assemble(table, "POINTER_BLOCK_COPY", new List<(int, Operand[])>
        {
            (move, new[] { new Operand(LocalInt, 0), new Operand(Immediate, 11) }),
            (move, new[] { new Operand(LocalInt, 1), new Operand(Immediate, 22) }),
            (move, new[] { new Operand(LocalInt, 2), new Operand(Immediate, 33) }),
            (0x63, new[] { new Operand(LocalPointer, 0), new Operand(LocalInt, 0) }),
            (0x63, new[] { new Operand(LocalPointer, 1), new Operand(GlobalInt, 0x700) }),
            (0x1b0, new[]
            {
                new Operand(LocalPointer, 0), new Operand(LocalPointer, 1), new Operand(Immediate, 3),
            }),

            // A pointer source is aliased to its target, rather than to the pointer slot itself.
            (lookup, new[]
            {
                new Operand(LocalPointer, 2), new Operand(GlobalInt, 0x710), new Operand(Immediate, 2),
            }),
            (0x63, new[] { new Operand(LocalPointer, 3), new Operand(LocalPointer, 2) }),
            (0x1b0, new[]
            {
                new Operand(LocalPointer, 3), new Operand(GlobalInt, 0x720), new Operand(Immediate, 2),
            }),

            // Direct operands resolve as the starts of consecutive cell spans.
            (0x1b0, new[]
            {
                new Operand(GlobalInt, 0x700), new Operand(LocalInt, 10), new Operand(Immediate, 3),
            }),
            (move, new[] { new Operand(GlobalInt, 0x730), new Operand(LocalInt, 10) }),
            (move, new[] { new Operand(GlobalInt, 0x731), new Operand(LocalInt, 11) }),
            (move, new[] { new Operand(GlobalInt, 0x732), new Operand(LocalInt, 12) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());
        vm.Globals[0x712] = 44;
        vm.Globals[0x713] = 55;

        vm.Run();

        Assert.Equal(new long[] { 11, 22, 33 }, new[]
        {
            vm.Globals[0x700], vm.Globals[0x701], vm.Globals[0x702],
        });
        Assert.Equal(new long[] { 44, 55 }, new[] { vm.Globals[0x720], vm.Globals[0x721] });
        Assert.Equal(new long[] { 11, 22, 33 }, new[]
        {
            vm.Globals[0x730], vm.Globals[0x731], vm.Globals[0x732],
        });
    }

    [Fact]
    public void TakeAddress_SupportsTheNativeStringPointerDestination()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int move = table.ByLabel("mov")!.Value;
        var script = ScriptAssembler.Assemble(table, "STRING_ADDRESS", new List<(int, Operand[])>
        {
            (0x63, new[] { new Operand(LocalStringPointer, 0), new Operand(GlobalString, 0x740) }),
            (move, new[] { new Operand(LocalStringPointer, 0), new Operand(InlineString, 0) }),
            (0x2, Array.Empty<Operand>()),
        }, new[] { "aliased" });
        var vm = new VirtualMachine(script, table, new RecordingHost());
        vm.GlobalStrings[0x740] = "before";

        vm.Run();

        Assert.Equal("aliased", vm.GlobalStrings[0x740]);
    }

    [Fact]
    public void FillIntArray_WritesResolvedPointerAndDirectSpansAndIgnoresNonpositiveCounts()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        int move = table.ByLabel("mov")!.Value;
        var script = ScriptAssembler.Assemble(table, "FILL-INT-ARRAY", new List<(int, Operand[])>
        {
            (0x63, new[] { new Operand(LocalPointer, 0), new Operand(GlobalInt, 0x800) }),
            (0x2d8, new[]
            {
                new Operand(LocalPointer, 0), new Operand(Immediate, -7), new Operand(Immediate, 3),
            }),
            (0x2d8, new[]
            {
                new Operand(LocalInt, 4), new Operand(Immediate, 9), new Operand(Immediate, 2),
            }),
            (move, new[] { new Operand(GlobalInt, 0x820), new Operand(LocalInt, 4) }),
            (move, new[] { new Operand(GlobalInt, 0x821), new Operand(LocalInt, 5) }),
            (0x2d8, new[]
            {
                new Operand(GlobalInt, 0x810), new Operand(Immediate, 5), new Operand(Immediate, 0),
            }),
            (0x2d8, new[]
            {
                new Operand(GlobalInt, 0x811), new Operand(Immediate, 6), new Operand(Immediate, -1),
            }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(
            script, table, new RecordingHost(),
            compatibility: new VmCompatibilityContext("kamidori", "SYS4433"));
        vm.Globals[0x810] = 44;
        vm.Globals[0x811] = 55;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Null(vm.CompatibilityFailure);
        Assert.Equal(new long[] { -7, -7, -7 },
            new[] { vm.Globals[0x800], vm.Globals[0x801], vm.Globals[0x802] });
        Assert.Equal(new long[] { 9, 9 }, new[] { vm.Globals[0x820], vm.Globals[0x821] });
        Assert.Equal(44, vm.Globals[0x810]);
        Assert.Equal(55, vm.Globals[0x811]);
    }
}

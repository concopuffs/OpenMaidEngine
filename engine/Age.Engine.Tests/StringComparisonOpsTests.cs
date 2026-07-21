using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class StringComparisonOpsTests
{
    private const int Immediate = 0, InlineString = 2, GlobalInt = 3, GlobalString = 5,
                      GlobalStringPointer = 8, LocalInt = 9, LocalString = 11,
                      LocalStringPointer = 14;

    [Fact]
    public void StringEquals_HandlesLiteralGlobalLocalAndPointerOperands()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int lookup = table.ByLabel("lookup-array")!.Value;
        int move = table.ByLabel("mov")!.Value;
        var script = ScriptAssembler.Assemble(table, "STRING_EQUALS", new List<(int, Operand[])>
        {
            (move, new[] { new Operand(LocalString, 0), new Operand(InlineString, 0) }),
            (lookup, new[]
            {
                new Operand(LocalStringPointer, 0), new Operand(GlobalString, 0x300),
                new Operand(Immediate, 2),
            }),
            (0x194, new[]
            {
                new Operand(LocalInt, 0), new Operand(InlineString, 0), new Operand(InlineString, 1),
            }),
            (0x194, new[]
            {
                new Operand(LocalInt, 1), new Operand(GlobalString, 0x301), new Operand(InlineString, 0),
            }),
            (0x194, new[]
            {
                new Operand(LocalInt, 2), new Operand(LocalString, 0), new Operand(InlineString, 0),
            }),
            (0x194, new[]
            {
                new Operand(LocalInt, 3), new Operand(LocalStringPointer, 0), new Operand(InlineString, 0),
            }),
            (0x194, new[]
            {
                new Operand(LocalInt, 4), new Operand(GlobalStringPointer, 0x400),
                new Operand(InlineString, 0),
            }),
            (move, new[] { new Operand(GlobalInt, 0x500), new Operand(LocalInt, 0) }),
            (move, new[] { new Operand(GlobalInt, 0x501), new Operand(LocalInt, 1) }),
            (move, new[] { new Operand(GlobalInt, 0x502), new Operand(LocalInt, 2) }),
            (move, new[] { new Operand(GlobalInt, 0x503), new Operand(LocalInt, 3) }),
            (move, new[] { new Operand(GlobalInt, 0x504), new Operand(LocalInt, 4) }),
            (0x2, Array.Empty<Operand>()),
        }, new[] { "姫狩り", "姫狩り", "姫狩り違い" });
        var vm = new VirtualMachine(script, table, new RecordingHost());
        vm.GlobalStrings[0x301] = "姫狩り違い";
        vm.GlobalStrings[0x302] = "姫狩り";
        vm.Globals[0x400] = 0x302;

        vm.Run();

        Assert.Equal(new long[] { 1, 0, 1, 1, 1 }, new[]
        {
            vm.Globals[0x500], vm.Globals[0x501], vm.Globals[0x502],
            vm.Globals[0x503], vm.Globals[0x504],
        });
    }

    [Fact]
    public void StringEquals_DrivesAGameStartShapedConditionalBranch()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "GAMESTART_STRING_BRANCH", new List<(int, Operand[])>
        {
            // These offsets mirror the release idiom: compare INPUTNAME with a literal, then jcc.
            (0x194, new[]
            {
                new Operand(LocalInt, 0), new Operand(GlobalString, 0x52d), new Operand(InlineString, 0),
            }),
            (0xa0, new[]
            {
                new Operand(LocalInt, 0), new Operand(Immediate, 19),
                new Operand(Immediate, unchecked((long)0xffffffff)),
            }),
            (0x55, new[] { new Operand(GlobalInt, 0x600), new Operand(Immediate, 99) }),
            (0x2, Array.Empty<Operand>()),
        }, new[] { "?" });
        var vm = new VirtualMachine(script, table, new RecordingHost());
        vm.GlobalStrings[0x52d] = "?";
        vm.Globals[0x600] = 1;

        vm.Run();

        Assert.Equal(1, vm.Globals[0x600]);
    }
}

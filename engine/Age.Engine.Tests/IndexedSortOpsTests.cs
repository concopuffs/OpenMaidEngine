using System;
using System.Collections.Generic;
using System.Linq;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class IndexedSortOpsTests
{
    private const int Immediate = 0, GlobalInt = 3, LocalInt = 9;

    [Fact]
    public void SortIndicesByKeySum_IsStableAndUsesSignedInt32Overflow()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int move = table.ByLabel("mov")!.Value;
        var ops = new List<(int, Operand[])>();

        long[] secondaryKeys = { -1, 1, -1, -2, 1 };
        for (int i = 0; i < secondaryKeys.Length; i++)
            ops.Add((move, new[] { new Operand(LocalInt, 20 + i), new Operand(Immediate, secondaryKeys[i]) }));

        ops.Add((0x12f, new[]
        {
            new Operand(LocalInt, 0),
            new Operand(GlobalInt, 0x100),
            new Operand(LocalInt, 20),
            new Operand(Immediate, secondaryKeys.Length),
        }));
        for (int i = 0; i < secondaryKeys.Length; i++)
            ops.Add((move, new[] { new Operand(GlobalInt, 0x200 + i), new Operand(LocalInt, i) }));
        ops.Add((0x2, Array.Empty<Operand>()));

        var vm = new VirtualMachine(
            ScriptAssembler.Assemble(table, "INDEX_SORT", ops, Array.Empty<string>()),
            table, new RecordingHost());
        long[] primaryKeys = { 4, -2, 4, 1, int.MaxValue };
        for (int i = 0; i < primaryKeys.Length; i++) vm.Globals[0x100 + i] = primaryKeys[i];
        vm.Globals[0x104] = 0;
        vm.ExternalGlobals[0x104] = int.MaxValue; // addressed global reads honor the host-owned overlay

        vm.Run();

        // Sums are 3, -1, 3, -1, and int.MinValue after native signed 32-bit overflow.
        // Equal -1 and 3 pairs retain their original relative order.
        Assert.Equal(new long[] { 4, 1, 3, 0, 2 },
            Enumerable.Range(0, 5).Select(i => vm.Globals[0x200 + i]).ToArray());
    }

    [Fact]
    public void SortIndicesByKeySum_WritesInitialIndexWhenCountIsZero()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int move = table.ByLabel("mov")!.Value;
        var script = ScriptAssembler.Assemble(table, "EMPTY_INDEX_SORT", new List<(int, Operand[])>
        {
            (move, new[] { new Operand(LocalInt, 0), new Operand(Immediate, 99) }),
            (0x12f, new[]
            {
                new Operand(LocalInt, 0), new Operand(LocalInt, 10),
                new Operand(LocalInt, 20), new Operand(Immediate, 0),
            }),
            (move, new[] { new Operand(GlobalInt, 0x200), new Operand(LocalInt, 0) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Equal(0, vm.Globals[0x200]);
    }
}

using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class PointInPolygonOpsTests
{
    private const int Immediate = 0, GlobalInt = 3, LocalInt = 9, LocalPointer = 12;

    [Fact]
    public void PointInPolygon_MatchesAlternateFillAndHalfOpenGdiEdges()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int move = table.ByLabel("mov")!.Value;
        var operations = new List<(int, Operand[])>();
        int[] xs = { 0, 10, 10, 0 };
        int[] ys = { 0, 0, 10, 10 };
        for (int index = 0; index < xs.Length; index++)
        {
            operations.Add((move, new[] { L(100 + index), I(xs[index]) }));
            operations.Add((move, new[] { L(200 + index), I(ys[index]) }));
        }
        operations.Add((0x63, new[] { P(0), L(100) }));
        operations.Add((0x63, new[] { P(1), L(200) }));

        AddProbe(operations, 0x300, 5, 5);   // interior
        AddProbe(operations, 0x301, 0, 5);   // left edge is included by GDI's half-open fill
        AddProbe(operations, 0x302, 10, 5);  // right edge is excluded
        AddProbe(operations, 0x303, 5, 10);  // bottom edge is excluded
        AddProbe(operations, 0x304, -1, 5);  // exterior
        operations.Add((0x2, Array.Empty<Operand>()));

        var vm = new VirtualMachine(
            ScriptAssembler.Assemble(table, "POINT_IN_POLYGON", operations, Array.Empty<string>()),
            table, new RecordingHost());

        vm.Run();

        Assert.Equal(new long[] { 1, 1, 0, 0, 0 },
            new[] { vm.Globals[0x300], vm.Globals[0x301], vm.Globals[0x302],
                    vm.Globals[0x303], vm.Globals[0x304] });
    }

    private static void AddProbe(List<(int, Operand[])> operations, int output, int x, int y)
        => operations.Add((0x147, new[] { G(output), I(x), I(y), P(0), P(1), I(4) }));

    private static Operand I(long value) => new(Immediate, value);
    private static Operand G(int address) => new(GlobalInt, address);
    private static Operand L(int address) => new(LocalInt, address);
    private static Operand P(int address) => new(LocalPointer, address);
}

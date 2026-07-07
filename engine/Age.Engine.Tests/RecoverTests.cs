using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class RecoverTests
{
    private static long G(VirtualMachine vm, int k) => vm.Globals.TryGetValue(k, out var v) ? v : 0;

    [Fact]
    public void RecoverUnitTestPasses()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Path.Combine(Paths.Data1, "RECOVER.BIN"), table);
        // Full call-script handling on, but the subroutines are doubled by a no-op `exit` script:
        // this test isolates RECOVER's ISA semantics (pointer/lvalue, 2D-stride, loops) from the real
        // subroutines' game-state dependencies. The real subroutines are covered elsewhere.
        var noop = ScriptAssembler.Assemble(table, "NOOP",
            new List<(int, Age.Engine.Model.Operand[])> { (0x2, System.Array.Empty<Age.Engine.Model.Operand>()) },
            System.Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new CaptureHost(), null, new AnyProvider(noop));

        int unit = 0;
        vm.Globals[0x152616] = unit;
        int A = 0x4e11b, B = 0x4e085, C = 0x52383, E = 0x52f3b, F = 0x5295f, FL = 0xaacb4;
        for (int k = 0; k < 3; k++) vm.Globals[A + unit * 14 + (11 + k)] = 100 + k;
        vm.Globals[C + unit * 30 + 5] = 7; vm.Globals[FL + 5] = 1; vm.Globals[E + unit * 30 + 5] = 42;
        vm.Globals[C + unit * 30 + 6] = 0; vm.Globals[FL + 6] = 1; vm.Globals[E + unit * 30 + 6] = 99;
        vm.Globals[C + unit * 30 + 7] = 3; vm.Globals[FL + 7] = 0; vm.Globals[E + unit * 30 + 7] = 88;
        vm.Run();

        Assert.Equal(100, G(vm, B + unit * 3 + 0));
        Assert.Equal(101, G(vm, B + unit * 3 + 1));
        Assert.Equal(102, G(vm, B + unit * 3 + 2));
        Assert.Equal(42, G(vm, C + unit * 30 + 5));
        Assert.Equal(-1, G(vm, F + unit * 30 + 5));
        Assert.Equal(0, G(vm, C + unit * 30 + 6));
        Assert.Equal(3, G(vm, C + unit * 30 + 7));
    }
}

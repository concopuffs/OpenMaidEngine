using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class IntegerQueueOpsTests
{
    private const int Immediate = 0, GlobalInt = 3, LocalInt = 9;

    private static Operand I(long value) => new(Immediate, value);
    private static Operand G(int address) => new(GlobalInt, address);
    private static Operand L(int address) => new(LocalInt, address);

    [Fact]
    public void IntegerQueues_AreIndependentFifosAndResetDiscardsPendingValues()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "INT_QUEUE", new List<(int, Operand[])>
        {
            (0x132, new[] { I(0) }),
            (0x132, new[] { I(1) }),
            (0x133, new[] { I(0), I(11) }),
            (0x133, new[] { I(0), I(-22) }),
            (0x133, new[] { I(1), I(33) }),

            (0x134, new[] { I(0), L(0), L(1) }),
            (0x55, new[] { G(0x100), L(0) }),
            (0x55, new[] { G(0x101), L(1) }),
            (0x134, new[] { I(0), L(0), L(1) }),
            (0x55, new[] { G(0x102), L(0) }),
            (0x55, new[] { G(0x103), L(1) }),
            (0x134, new[] { I(1), L(0), L(1) }),
            (0x55, new[] { G(0x104), L(0) }),
            (0x55, new[] { G(0x105), L(1) }),

            (0x55, new[] { L(1), I(777) }),
            (0x134, new[] { I(0), L(0), L(1) }),
            (0x55, new[] { G(0x106), L(0) }),
            (0x55, new[] { G(0x107), L(1) }),
            (0x133, new[] { I(1), I(44) }),
            (0x132, new[] { I(1) }),
            (0x134, new[] { I(1), L(0), L(1) }),
            (0x55, new[] { G(0x108), L(0) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(11, vm.Globals[0x101]);
        Assert.Equal(1, vm.Globals[0x102]);
        Assert.Equal(-22, vm.Globals[0x103]);
        Assert.Equal(1, vm.Globals[0x104]);
        Assert.Equal(33, vm.Globals[0x105]);
        Assert.Equal(0, vm.Globals[0x106]);
        Assert.Equal(777, vm.Globals[0x107]);
        Assert.Equal(0, vm.Globals[0x108]);
    }

    [Fact]
    public void PreloadedScriptSlot_RestartsCodeAndRetainsItsLocalBankAcrossCalls()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var root = ScriptAssembler.Assemble(table, "PRELOADED_ROOT", new List<(int, Operand[])>
        {
            (0x6, new[] { I(0x500), I(0x1f) }),
            (0x8, new[] { I(0x1f) }),
            (0x8, new[] { I(0x1f) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var worker = ScriptAssembler.Assemble(table, "PRELOADED_WORKER", new List<(int, Operand[])>
        {
            (0x50, new[] { L(0), L(0), I(1) }),
            (0x55, new[] { G(0x120), L(0) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(root, table, new RecordingHost(),
            provider: new MapProvider(new Dictionary<long, Script> { [0x500] = worker }));

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(2, vm.Globals[0x120]);
        Assert.Equal(2, vm.CallScriptDispatches);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void RegisteredRealMvseek_ExpandsMovementCostsThroughTheSystem4ServiceAbi()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var mvseek = Sys4Loader.Load(Paths.Scripts()["MVSEEK.BIN"], table);
        var root = ScriptAssembler.Assemble(table, "SYSTEM4_SERVICE_BRIDGE", new List<(int, Operand[])>
        {
            (0x6, new[] { I(0x3381), I(0x1f) }),
            (0x8, new[] { I(0x1f) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(root, table, new RecordingHost(),
            provider: new MapProvider(new Dictionary<long, Script> { [0x3381] = mvseek }));
        SeedSearchGrid(vm, movementPoints: 3);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(3, vm.Globals[MovementCell(6, 5)]);
        Assert.Equal(3, vm.Globals[MovementCell(4, 5)]);
        Assert.Equal(3, vm.Globals[MovementCell(5, 6)]);
        Assert.Equal(3, vm.Globals[MovementCell(5, 4)]);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void RealMvseek_ExpandsMovementCostsBeyondTheOrigin()
    {
        var vm = RealSearchVm("MVSEEK.BIN");
        SeedSearchGrid(vm, movementPoints: 3);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(3, vm.Globals[MovementCell(6, 5)]);
        Assert.Equal(3, vm.Globals[MovementCell(4, 5)]);
        Assert.Equal(3, vm.Globals[MovementCell(5, 6)]);
        Assert.Equal(3, vm.Globals[MovementCell(5, 4)]);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void RealAtseek_ExpandsAttackDistancesBeyondTheOrigin()
    {
        var vm = RealSearchVm("ATSEEK.BIN");
        SeedSearchGrid(vm, movementPoints: 0);
        vm.Globals[0xcc9f3] = 2;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[AttackCell(6, 5)]);
        Assert.Equal(1, vm.Globals[AttackCell(4, 5)]);
        Assert.Equal(1, vm.Globals[AttackCell(5, 6)]);
        Assert.Equal(1, vm.Globals[AttackCell(5, 4)]);
    }

    private static VirtualMachine RealSearchVm(string name)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()[name], table);
        return new VirtualMachine(script, table, new RecordingHost());
    }

    private static void SeedSearchGrid(VirtualMachine vm, int movementPoints)
    {
        const int entity = 0;
        const int x = 5, y = 5;
        vm.Globals[0x66713] = entity;
        vm.Globals[0x5231f + entity] = x;
        vm.Globals[0x52351 + entity] = y;
        vm.Globals[0x4e11b + entity * 14 + 10] = movementPoints;

        vm.Globals[0xccbcf + 1] = 1;
        vm.Globals[0xccbcf + 2] = -1;
        vm.Globals[0xccbcf + 3] = 0;
        vm.Globals[0xccbcf + 4] = 0;
        vm.Globals[0xccbd4 + 1] = 0;
        vm.Globals[0xccbd4 + 2] = 0;
        vm.Globals[0xccbd4 + 3] = 1;
        vm.Globals[0xccbd4 + 4] = -1;

        // Terrain id 1 is passable. MVSEEK checks destination centers; ATSEEK checks the
        // fine-grid edge between the origin and each logical neighbor.
        vm.Globals[0xe6ae0 + 1] = 1;
        SetTerrain(vm, x * 2, y * 2);
        SetTerrain(vm, (x + 1) * 2, y * 2);
        SetTerrain(vm, (x - 1) * 2, y * 2);
        SetTerrain(vm, x * 2, (y + 1) * 2);
        SetTerrain(vm, x * 2, (y - 1) * 2);
        SetTerrain(vm, x * 2 + 1, y * 2);
        SetTerrain(vm, x * 2 - 1, y * 2);
        SetTerrain(vm, x * 2, y * 2 + 1);
        SetTerrain(vm, x * 2, y * 2 - 1);
    }

    private static void SetTerrain(VirtualMachine vm, int x, int y)
        => vm.Globals[0x341ab + y * 53 + x] = 1;

    private static int MovementCell(int x, int y) => 0xaba96 + y * 27 + x;
    private static int AttackCell(int x, int y) => 0xb8d86 + y * 27 + x;
}

using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class IntegerStackOpsTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
    private static Operand L(long address) => new(9, address);
    private static (int, Operand[]) Exit() => (0x2, Array.Empty<Operand>());

    [Fact]
    public void IntegerStacksAreIndependentLifosAndResetDiscardsPendingValues()
    {
        Script script = ScriptAssembler.Assemble(Table, "INT_STACK",
        [
            (0x138, [I(0), I(11)]),
            (0x138, [I(0), I(-22)]),
            (0x138, [I(1), I(33)]),

            (0x139, [I(0), L(0), L(1)]),
            (0x55, [G(0x100), L(0)]),
            (0x55, [G(0x101), L(1)]),
            (0x139, [I(0), L(0), L(1)]),
            (0x55, [G(0x102), L(0)]),
            (0x55, [G(0x103), L(1)]),
            (0x139, [I(1), L(0), L(1)]),
            (0x55, [G(0x104), L(0)]),
            (0x55, [G(0x105), L(1)]),

            (0x55, [L(1), I(777)]),
            (0x139, [I(0), L(0), L(1)]),
            (0x55, [G(0x106), L(0)]),
            (0x55, [G(0x107), L(1)]),
            (0x138, [I(1), I(44)]),
            (0x137, [I(1)]),
            (0x139, [I(1), L(0), L(1)]),
            (0x55, [G(0x108), L(0)]),
            Exit(),
        ], []);
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(-22, vm.Globals[0x101]);
        Assert.Equal(1, vm.Globals[0x102]);
        Assert.Equal(11, vm.Globals[0x103]);
        Assert.Equal(1, vm.Globals[0x104]);
        Assert.Equal(33, vm.Globals[0x105]);
        Assert.Equal(0, vm.Globals[0x106]);
        Assert.Equal(777, vm.Globals[0x107]);
        Assert.Equal(0, vm.Globals[0x108]);
    }

    [Fact]
    public void HandlerAddressableSlotTenIsSafeIndependentState()
    {
        Script script = ScriptAssembler.Assemble(Table, "INT_STACK_SLOT_TEN",
        [
            (0x138, [I(0), I(10)]),
            (0x138, [I(10), I(1010)]),
            (0x139, [I(10), G(0x100), G(0x101)]),
            (0x139, [I(0), G(0x102), G(0x103)]),
            Exit(),
        ], []);
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(1010, vm.Globals[0x101]);
        Assert.Equal(1, vm.Globals[0x102]);
        Assert.Equal(10, vm.Globals[0x103]);
    }

    [Fact]
    public void RootSceneReloadReconstructsEmptyStacks()
    {
        Script reloadedSystem4 = ScriptAssembler.Assemble(Table, "SYSTEM4.BIN",
        [
            (0x55, [L(1), I(777)]),
            (0x139, [I(0), L(0), L(1)]),
            (0x55, [G(0x100), L(0)]),
            (0x55, [G(0x101), L(1)]),
            Exit(),
        ], []);
        Script initial = ScriptAssembler.Assemble(Table, "INITIAL",
        [
            (0x138, [I(0), I(123)]),
            (0x9, Array.Empty<Operand>()),
        ], []);
        var provider = new MapProvider(new Dictionary<long, Script> { [0] = reloadedSystem4 });
        var host = new RecordingHost();
        var vm = new VirtualMachine(initial, Table, host, provider: provider);

        vm.Run();

        Assert.Equal(0, vm.Globals[0x100]);
        Assert.Equal(777, vm.Globals[0x101]);
        Assert.Equal(1, host.SceneContextResets);
    }
}

using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class SystemMenuShowDelayOpcodeTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
    private static (int, Operand[]) Exit() => (0x2, Array.Empty<Operand>());

    [Fact]
    public void System4DelayRoundTripsThroughThePairedGetter()
    {
        Script system4 = ScriptAssembler.Assemble(Table, "SYSTEM4.BIN",
        [
            (0x149, [I(1000)]),
            (0x148, [G(0x100)]),
            Exit(),
        ], []);
        var vm = new VirtualMachine(system4, Table, new RecordingHost());

        Assert.Equal(0u, vm.SystemMenuShowDelayMilliseconds);

        vm.Run();

        Assert.Equal(1000u, vm.SystemMenuShowDelayMilliseconds);
        Assert.Equal(1000, vm.Globals[0x100]);
    }

    [Fact]
    public void SetterAndGetterPreserveTheCompleteNativeDword()
    {
        Script system4 = ScriptAssembler.Assemble(Table, "SYSTEM4.BIN",
        [
            (0x149, [I(0x80000001L)]),
            (0x148, [G(0x100)]),
            Exit(),
        ], []);
        var vm = new VirtualMachine(system4, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(0x80000001u, vm.SystemMenuShowDelayMilliseconds);
        Assert.Equal(unchecked((int)0x80000001u), vm.Globals[0x100]);
    }

    [Fact]
    public void RootSceneReloadRestoresTheZeroDefault()
    {
        Script reloadedSystem4 = ScriptAssembler.Assemble(Table, "SYSTEM4.BIN",
        [
            (0x148, [G(0x100)]),
            Exit(),
        ], []);
        Script initial = ScriptAssembler.Assemble(Table, "INITIAL",
        [
            (0x149, [I(1000)]),
            (0x9, Array.Empty<Operand>()),
        ], []);
        var provider = new MapProvider(new Dictionary<long, Script> { [0] = reloadedSystem4 });
        var host = new RecordingHost();
        var vm = new VirtualMachine(initial, Table, host, provider: provider);

        vm.Run();

        Assert.Equal(0u, vm.SystemMenuShowDelayMilliseconds);
        Assert.Equal(0, vm.Globals[0x100]);
        Assert.Equal(1, host.SceneContextResets);
    }
}

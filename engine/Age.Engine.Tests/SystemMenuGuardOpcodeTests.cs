using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class SystemMenuGuardOpcodeTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static (int, Operand[]) Exit() => (0x2, Array.Empty<Operand>());

    [Fact]
    public void SetterDefaultsEnabledAndReplacesTheCompleteNativeDword()
    {
        Script config = ScriptAssembler.Assemble(Table, "CONFIG.BIN",
        [
            (0x142, [I(0)]),
            (0x142, [I(-7)]),
            Exit(),
        ], []);
        var vm = new VirtualMachine(config, Table, new RecordingHost());

        Assert.Equal(1, vm.SystemMenuActionsEnabled);

        vm.Run();

        Assert.Equal(-7, vm.SystemMenuActionsEnabled);
    }

    [Fact]
    public void ConfigEntryAndExitPairRestoresNativeMenuActions()
    {
        Script config = ScriptAssembler.Assemble(Table, "CONFIG.BIN",
        [
            (0x142, [I(0)]),
            (0x142, [I(1)]),
            Exit(),
        ], []);
        var vm = new VirtualMachine(config, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(1, vm.SystemMenuActionsEnabled);
    }

    [Fact]
    public void RootSceneReloadRestoresTheNativeDefault()
    {
        Script system4 = ScriptAssembler.Assemble(Table, "SYSTEM4.BIN",
        [
            Exit(),
        ], []);
        Script config = ScriptAssembler.Assemble(Table, "CONFIG.BIN",
        [
            (0x142, [I(0)]),
            (0x9, Array.Empty<Operand>()),
        ], []);
        var provider = new MapProvider(new Dictionary<long, Script> { [0] = system4 });
        var host = new RecordingHost();
        var vm = new VirtualMachine(config, Table, host, provider: provider);

        vm.Run();

        Assert.Equal(1, vm.SystemMenuActionsEnabled);
        Assert.Equal(1, host.SceneContextResets);
    }
}

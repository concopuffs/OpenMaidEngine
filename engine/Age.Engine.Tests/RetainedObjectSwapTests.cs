using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class RetainedObjectSwapTests
{
    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int address) => new(3, address);
    private static Operand I(long value) => new(0, value);
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void Op0x214_SwapsCompleteRecordsWithoutChangingHandles()
    {
        var table = Table();
        var scene = ScriptAssembler.Assemble(table, "SWAP_GFX", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), I(100)}), (0x55, new[]{G(2), I(200)}),
            (0x214, new[]{G(1), G(2)}), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingHost());
        vm.Gfx.BindDraw(100, 3, 1, 2, 30, 40, 10, 20);
        vm.Gfx.SetCurrentRotation(100, (1, 0, 0), 30);
        vm.Gfx.TryGet(100)!.NativePersistenceRecord = [0x11, 0x12];
        vm.Gfx.BindDraw(200, 7, 5, 6, 70, 80, 50, 60);
        vm.Gfx.SetCurrentRotation(200, (0, 1, 0), 90);
        vm.Gfx.TryGet(200)!.NativePersistenceRecord = [0x21, 0x22];

        vm.Run();

        Assert.Equal(7, vm.Gfx.TryGet(100)!.SourceSlot);
        Assert.Equal((50L, 60L, 0L), vm.Gfx.TryGet(100)!.V24);
        Assert.Equal((0.0, 1.0, 0.0, 90.0), vm.Gfx.TryGet(100)!.RotationCurrent);
        Assert.Equal(new byte[]{0x21, 0x22}, vm.Gfx.TryGet(100)!.NativePersistenceRecord);
        Assert.Equal(3, vm.Gfx.TryGet(200)!.SourceSlot);
        Assert.Equal((10L, 20L, 0L), vm.Gfx.TryGet(200)!.V24);
        Assert.Equal((1.0, 0.0, 0.0, 30.0), vm.Gfx.TryGet(200)!.RotationCurrent);
        Assert.Equal(new byte[]{0x11, 0x12}, vm.Gfx.TryGet(200)!.NativePersistenceRecord);
    }

    [Fact]
    public void Op0x214_MovesSoleRecordToMissingHandle()
    {
        var table = Table();
        var scene = ScriptAssembler.Assemble(table, "MOVE_GFX", new List<(int, Operand[])>
        {
            (0x214, new[]{G(1), G(2)}), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingHost());
        vm.Globals[1] = 100;
        vm.Globals[2] = 200;
        vm.Gfx.BindDraw(100, 3, 1, 2, 30, 40, 10, 20);

        vm.Run();

        Assert.Null(vm.Gfx.TryGet(100));
        Assert.Equal(3, vm.Gfx.TryGet(200)!.SourceSlot);
        var rendered = vm.Gfx.SnapshotVisibleObjects();
        Assert.Single(rendered);
        Assert.Equal(200, rendered[0].Handle);
    }

    [Fact]
    public void Op0x214_WithTwoMissingHandles_RemainsEmpty()
    {
        var table = Table();
        var scene = ScriptAssembler.Assemble(table, "SWAP_MISSING_GFX", new List<(int, Operand[])>
        {
            (0x214, new[]{I(100), I(200)}), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingHost());

        vm.Run();

        Assert.Null(vm.Gfx.TryGet(100));
        Assert.Null(vm.Gfx.TryGet(200));
        Assert.Empty(vm.Gfx.SnapshotVisibleObjects());
    }
}

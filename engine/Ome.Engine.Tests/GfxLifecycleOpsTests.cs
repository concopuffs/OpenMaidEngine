using System.Collections.Generic;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

public class GfxLifecycleOpsTests
{
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int address) => new(3, address);
    private static Operand I(long value) => new(0, value);
    private static (int, Operand[]) Mov(int address, long value) => (0x55, new[] { G(address), I(value) });
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void ClearRetainedObjectsPreservesSurfaceResourcesForLaterRebind()
    {
        var table = T();
        var scene = ScriptAssembler.Assemble(table, "GFX-LIFECYCLE", new List<(int, Operand[])>
        {
            Mov(1, 0x40), Mov(2, 7), Mov(3, 0x33),
            (0x1f9, new[] { G(3), G(2), I(0) }),
            (0x1fb, new[] { G(1), G(2), I(0), I(0), I(16), I(16), I(0), I(0) }),
            (0x1f6, System.Array.Empty<Operand>()),
            (0x1fb, new[] { G(1), G(2), I(0), I(0), I(16), I(16), I(0), I(0) }),
            Exit(),
        }, System.Array.Empty<string>());

        var vm = new VirtualMachine(scene, table, new RecordingWaits());
        vm.Run();

        var visible = Assert.Single(vm.Gfx.SnapshotVisibleObjects());
        Assert.Equal(0x40, visible.Handle);
        Assert.Equal(0x33, visible.SurfaceResId);
    }

    [Fact]
    public void RenderTargetSelectionAndClearReachTheSurfaceAndTheBackbuffer()
    {
        var table = T();
        var host = new RecordingWaits();
        var scene = ScriptAssembler.Assemble(table, "GFX-TARGET", new List<(int, Operand[])>
        {
            (0x20d, new[] { I(7) }),
            (0x20e, System.Array.Empty<Operand>()),
            (0x20d, new[] { I(1000) }),
            (0x20e, System.Array.Empty<Operand>()),
            Exit(),
        }, System.Array.Empty<string>());

        var vm = new VirtualMachine(scene, table, host);
        vm.Graphics.PublishSurfacePixels(7, GraphicsTestSupport.Image(2, 2, (x, y) => [9, 9, 9, 255]));
        Assert.True(vm.Graphics.PresentFrame());   // the backbuffer now keeps its pixels
        vm.Run();

        Assert.Equal(new byte[4], vm.Graphics.PixelAt(7, 1, 1));
        Assert.Equal(-1, vm.Gfx.CurrentRenderTargetSlot);
        // The backbuffer's clear lands at its next publication, once.
        var objects = new List<RenderObject>();
        Assert.False(vm.Graphics.SnapshotBackbufferObjects(0, objects).PreserveExistingPixels);
        Assert.True(vm.Graphics.SnapshotBackbufferObjects(0, objects).PreserveExistingPixels);
    }

    [Fact]
    public void OpEightyRetainsItsLatestSelectionAsTheCurrentLayout()
    {
        var table = T();
        var scene = ScriptAssembler.Assemble(table, "GFX-DEFAULT-SLOT", new List<(int, Operand[])>
        {
            (0x80, new[] { I(7) }),
            (0x80, new[] { I(8) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingWaits());

        vm.Run();

        Assert.Equal(8, vm.TextHistory.CurrentLayoutSlot);
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void BulkReleaseDropsOnlyTransientSurfaceRange()
    {
        var table = T();
        var host = new RecordingWaits();
        var scene = ScriptAssembler.Assemble(table, "GFX-RELEASE", new List<(int, Operand[])>
        {
            Mov(1, 1), Mov(2, 42), Mov(3, 0x11), Mov(4, 0x22),
            (0x1f9, new[] { G(3), G(1), I(0) }),
            (0x1f9, new[] { G(4), G(2), I(0) }),
            (0x1fb, new[] { I(0x100), G(1), I(0), I(0), I(16), I(16), I(0), I(0) }),
            (0x1fb, new[] { I(0x101), G(2), I(0), I(0), I(16), I(16), I(0), I(0) }),
            (0x23d, System.Array.Empty<Operand>()),
            Exit(),
        }, System.Array.Empty<string>());

        var vm = new VirtualMachine(scene, table, host);
        vm.Run();

        var visible = vm.Gfx.SnapshotVisibleObjects();
        Assert.Equal(0x11, Assert.Single(visible, item => item.Handle == 0x100).SurfaceResId);
        Assert.Equal(0, Assert.Single(visible, item => item.Handle == 0x101).SurfaceResId);
        Assert.Equal(0x11, vm.Graphics.SurfaceResource(1));
        Assert.Null(vm.Graphics.SurfaceResource(42));
    }
}

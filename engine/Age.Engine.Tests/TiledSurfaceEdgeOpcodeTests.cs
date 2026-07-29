using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class TiledSurfaceEdgeOpcodeTests
{
    private static Operand I(long value) => new(0, value);

    [Fact]
    public void OpcodeRetainsSystem4EdgeWhileModeOneSurfaceUsesPortableBackend()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scene = ScriptAssembler.Assemble(table, "TILED-SURFACE", new List<(int, Operand[])>
        {
            (0x248, new[] { I(128) }),
            (0x249, new[] { I(0x32da), I(0x3e), I(0) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(scene, table, host);

        vm.Run();

        Assert.Equal(128, vm.Gfx.TiledSurfaceEdgeLength);
        Assert.Equal((0x32daL, 0x3e), Assert.Single(host.Textures));
        var surface = Assert.Single(vm.Gfx.CapturePersistenceSnapshot().Surfaces);
        Assert.Equal((0x3e, 0x32daL), (surface.Slot, surface.ResourceId));
    }

    [Fact]
    public void SettingReplacesSignedDwordAndSurvivesSceneContextReset()
    {
        var gfx = new GfxState();
        Assert.Equal(0, gfx.TiledSurfaceEdgeLength);

        gfx.SetTiledSurfaceEdgeLength(-1);
        Assert.Equal(-1, gfx.TiledSurfaceEdgeLength);

        gfx.SetTiledSurfaceEdgeLength(128);
        gfx.ResetSceneContext();
        Assert.Equal(128, gfx.TiledSurfaceEdgeLength);
    }
}

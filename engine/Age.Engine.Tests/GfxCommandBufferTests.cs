using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class GfxCommandBufferTests
{
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int addr) => new(3, addr);   // global-int
    private static Operand I(long v) => new(0, v);        // immediate

    // op ctor helpers (0x217 set-geom3, 0x218 get-geom3, 0x215 query, 0x55 mov, 0x2 exit)
    private static (int, Operand[]) SetGeom3(int handle, int a, int b, int c) => (0x217, new[] { G(handle), G(a), G(b), G(c) });
    private static (int, Operand[]) GetGeom3(int handle, int a, int b, int c) => (0x218, new[] { G(handle), G(a), G(b), G(c) });
    private static (int, Operand[]) Query(int outAddr, int handle) => (0x215, new[] { G(outAddr), G(handle) });
    private static (int, Operand[]) MovGI(int dst, long v) => (0x55, new[] { G(dst), I(v) });
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void SetThenGetGeom3RoundTripsThroughTheObject()
    {
        var t = T();
        // g[1]=handle; set V18 from g[2,3,4]=(10,20,30); read V18 back into g[5,6,7]; then exit.
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 10), MovGI(3, 20), MovGI(4, 30),
            SetGeom3(1, 2, 3, 4),
            GetGeom3(1, 5, 6, 7),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(10, vm.Globals[5]);
        Assert.Equal(20, vm.Globals[6]);
        Assert.Equal(30, vm.Globals[7]);
    }

    [Fact]
    public void QueryReturnsDistinctSlotsPerHandle_NotZero()
    {
        var t = T();
        // create two objects via set-geom, then query each into g[10], g[11].
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 0x2000), MovGI(3, 0),
            SetGeom3(1, 3, 3, 3), SetGeom3(2, 3, 3, 3),
            Query(10, 1), Query(11, 2), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.NotEqual(0, vm.Globals[10]);              // not collapsed to slot 0
        Assert.NotEqual(vm.Globals[10], vm.Globals[11]); // distinct slots => no collapse
    }
}

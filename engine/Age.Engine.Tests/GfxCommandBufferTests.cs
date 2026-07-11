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

    private static (int, Operand[]) Register(int handle) => (0x1a2, new[] { G(handle) });

    [Fact]
    public void QueryReturnsMinusOneUntilDrawBound_ThenSourceSlot()
    {
        // Op 0x215 returns obj+4 from the retained gfx object. Geometry creates the object but leaves it unbound;
        // op 0x1a2's descriptor registry is unrelated. Draw-texture binds the source slot returned by the query.
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0xcb2a), MovGI(2, 6), MovGI(3, 0), MovGI(4, 200),
            SetGeom3(1, 3, 3, 3),
            Register(1),
            Query(10, 1),
            (0x1fb, new[] { G(1), G(2), I(0), I(0), G(4), G(4), G(3), G(3) }),
            Query(11, 1), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(-1, vm.Globals[10]);
        Assert.Equal(6, vm.Globals[11]);
    }

    private static (int, Operand[]) BlitColor(int h, int x, int y, int alpha, int color)
        => (0x202, new[] { G(h), G(x), G(y), G(alpha), G(color) });

    [Fact]
    public void BlitColorStoresPackedArgbOnTheObject()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 0), MovGI(3, 0), MovGI(4, 0x80), MovGI(5, 0x112233),
            BlitColor(1, 2, 3, 4, 5), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.Equal(0x80_112233L, o.OneShotColorTarget);
        Assert.True(o.OneShotColorEnabled);
    }

    [Fact]
    public void TwoObjectsKeepIndependentGeometry_NoDrift()
    {
        var t = T();
        // The drift's essence: two different objects must NOT share geometry (pre-fix they collapsed to
        // slot 0 and cross-contaminated). Set V24 on A, then on B, then read both back — each intact.
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 0x2000),
            MovGI(3, 100), MovGI(4, 500), MovGI(5, 0),
            (0x219, new[] { G(1), G(3), G(4), G(5) }),          // set V24 on A = (100,500,0)
            MovGI(3, 300), MovGI(4, 100),
            (0x219, new[] { G(2), G(3), G(4), G(5) }),          // set V24 on B = (300,100,0)
            (0x21a, new[] { G(1), G(10), G(11), G(12) }),       // read A back
            (0x21a, new[] { G(2), G(20), G(21), G(22) }),       // read B back
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal((100L, 500L), (vm.Globals[10], vm.Globals[11]));   // A intact
        Assert.Equal((300L, 100L), (vm.Globals[20], vm.Globals[21]));   // B intact, no cross-contamination
    }

    private static (int, Operand[]) DrawTex(int handle, int slot, int w, int h, int dx, int dy)
        => (0x1fb, new[] { G(handle), G(slot), I(0), I(0), G(w), G(h), G(dx), G(dy) });

    private static (int, Operand[]) SetTex(int resId, int slot) => (0x1f9, new[] { G(resId), G(slot), I(0) });

    [Fact]
    public void SetThenDrawTextureMakesAVisibleObjectFromTheSurface()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0xA), MovGI(2, 4), MovGI(7, 0x25), MovGI(3, 800), MovGI(4, 600), MovGI(5, 0), MovGI(6, 0),
            SetTex(7, 2),                  // load resId 0x25 into surface slot 4
            DrawTex(1, 2, 3, 4, 5, 6),     // object 0xA draws surface 4 at (0,0)
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var vis = vm.Gfx.SnapshotVisibleObjects();
        Assert.Single(vis);
        Assert.Equal(0xA, vis[0].Handle);
        Assert.Equal(0x25, vis[0].SurfaceResId);          // resolved from the object's live source slot
        Assert.Equal((800, 600, 0, 0), (vis[0].W, vis[0].H, vis[0].DstX, vis[0].DstY));
    }
}

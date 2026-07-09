using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class AnimChannelTests
{
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int addr) => new(3, addr);
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void Op0x22f_SetsPosition_Op0x228_QueriesItBack()
    {
        var t = T();
        // g1=handle, g3=x=50, g4=y=60, g5=z=0; op 0x22f sets V24; op 0x228 reads it into g11/g12/g13.
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), new Operand(0,0x1000)}), (0x55, new[]{G(2), new Operand(0,0)}),
            (0x55, new[]{G(3), new Operand(0,50)}), (0x55, new[]{G(4), new Operand(0,60)}), (0x55, new[]{G(5), new Operand(0,0)}),
            (0x22f, new[]{G(1), G(2), G(3), G(4), G(5)}),      // set position
            (0x228, new[]{G(10), G(1), G(11), G(12), G(13)}),  // query position -> g11,g12,g13
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(50, vm.Globals[11]);
        Assert.Equal(60, vm.Globals[12]);
        Assert.Equal(0, vm.Globals[10]);   // success flag
    }

    [Fact]
    public void Op0x232_SetsColorAnim()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), new Operand(0,0x1000)}), (0x55, new[]{G(2), new Operand(0,1000)}),
            (0x55, new[]{G(3), new Operand(0,0x80)}), (0x55, new[]{G(4), new Operand(0,0xFF0000)}),
            (0x232, new[]{G(1), G(2), G(3), G(4)}), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.True(o.ColorAnim);
        Assert.Equal(1000, o.ColorPeriod);
        Assert.Equal(GfxState.PackColor(0x80, 0xFF0000), o.ColorTarget);
    }

    [Fact]
    public void Op0x239_SetsSpritesheetGridCell()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), new Operand(0,0x1000)}), (0x55, new[]{G(2), new Operand(0,0)}), (0x55, new[]{G(3), new Operand(0,0)}),
            (0x55, new[]{G(4), new Operand(0,4)}), (0x55, new[]{G(5), new Operand(0,1)}), (0x55, new[]{G(6), new Operand(0,2)}),
            (0x239, new[]{G(1), G(2), G(3), G(4), G(5), G(6)}), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.True(o.SrcAnim);
        Assert.Equal(4, o.SrcGridW);
        Assert.Equal(2, o.SrcCell);
    }

    private static GfxState VisibleObj(long handle)
    {
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(handle, 1, 0, 0, 256, 64, 100, 100);   // 256x64 sheet at base pos (100,100)
        return g;
    }

    [Fact]
    public void SetSrcRect_StoresGridCellPeriod()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRect(0x100, gridW: 4, gridH: 1, cell: 2, period: 800);
        var o = g.TryGet(0x100)!;
        Assert.Equal(4, o.SrcGridW);
        Assert.Equal(2, o.SrcCell);
        Assert.Equal(800, o.SrcPeriod);
        Assert.True(o.SrcAnim);
        Assert.Equal(-1, o.SrcStart);   // uninitialized until first interpolated frame
    }

    [Fact]
    public void SetSrcRect_ClampsGridToAtLeastOne()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRect(0x100, gridW: 0, gridH: 0, cell: 0, period: 0);
        var o = g.TryGet(0x100)!;
        Assert.Equal(1, o.SrcGridW);
        Assert.Equal(1, o.SrcGridH);
    }

    [Fact]
    public void SetColorAnim_StoresPeriodAndTarget()
    {
        var g = VisibleObj(0x100);
        long target = GfxState.PackColor(0x80, 0xFF0000);   // half-alpha red glow
        g.SetColorAnim(0x100, period: 1000, target: target);
        var o = g.TryGet(0x100)!;
        Assert.Equal(1000, o.ColorPeriod);
        Assert.Equal(target, o.ColorTarget);
        Assert.True(o.ColorAnim);
    }
}

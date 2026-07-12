using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class AnimChannelTests
{
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int addr) => new(3, addr);
    private static Operand I(long value) => new(0, value);
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void Op0x228_QueriesTranslationTarget_NotBasePosition()
    {
        var t = T();
        // AE001H has draw/base V24=(360,20), independent of its op-0x220 translation target (40,-20).
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), I(0xcf3a)}), (0x55, new[]{G(2), I(0)}),
            (0x55, new[]{G(3), I(360)}), (0x55, new[]{G(4), I(20)}), (0x55, new[]{G(5), I(0)}),
            (0x22f, new[]{G(1), G(2), G(3), G(4), G(5)}),
            (0x55, new[]{G(6), I(300)}), (0x55, new[]{G(7), I(40)}),
            (0x55, new[]{G(8), I(20)}), (0x51, new[]{G(8), I(0), G(8)}),
            (0x220, new[]{G(1), G(2), G(6), G(7), G(8), G(5)}),
            (0x228, new[]{G(10), G(1), G(11), G(12), G(13)}),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(40, vm.Globals[11]);
        Assert.Equal(-20, vm.Globals[12]);
        Assert.Equal(0, vm.Globals[13]);
        Assert.Equal(0, vm.Globals[10]);   // success flag
        Assert.Equal((360L, 20L, 0L), vm.Gfx.TryGet(0xcf3a)!.V24);
    }

    [Fact]
    public void Op0x228_MissingObject_SetsFailureAndPreservesOutputs()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "GFX_MISSING", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), I(0xdead)}), (0x55, new[]{G(11), I(7)}),
            (0x55, new[]{G(12), I(8)}), (0x55, new[]{G(13), I(9)}),
            (0x228, new[]{G(10), G(1), G(11), G(12), G(13)}), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(1, vm.Globals[10]);
        Assert.Equal((7L, 8L, 9L), (vm.Globals[11], vm.Globals[12], vm.Globals[13]));
    }

    [Fact]
    public void Ae001hThreeLegSequence_BuildsEachTargetFromPreviousTranslationTarget()
    {
        var t = T();
        var ops = new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), I(0xcf3a)}), (0x55, new[]{G(2), I(0)}), (0x55, new[]{G(3), I(300)}),
            (0x55, new[]{G(4), I(360)}), (0x55, new[]{G(5), I(20)}), (0x55, new[]{G(6), I(0)}),
            (0x55, new[]{G(40), I(20)}), (0x51, new[]{G(40), I(0), G(40)}),
            (0x55, new[]{G(41), I(60)}), (0x51, new[]{G(41), I(0), G(41)}),
            (0x22f, new[]{G(1), G(2), G(4), G(5), G(6)}),

            (0x228, new[]{G(10), G(1), G(11), G(12), G(13)}),
            (0x50, new[]{G(11), G(11), I(40)}), (0x50, new[]{G(12), G(12), G(40)}),
            (0x220, new[]{G(1), G(2), G(3), G(11), G(12), G(13)}),

            (0x228, new[]{G(20), G(1), G(21), G(22), G(23)}),
            (0x50, new[]{G(21), G(21), I(10)}), (0x50, new[]{G(22), G(22), G(41)}),
            (0x220, new[]{G(1), G(2), G(3), G(21), G(22), G(23)}),

            (0x228, new[]{G(30), G(1), G(31), G(32), G(33)}),
            (0x50, new[]{G(31), G(31), I(80)}), (0x50, new[]{G(32), G(32), G(40)}),
            (0x220, new[]{G(1), G(2), G(3), G(31), G(32), G(33)}),
            Exit(),
        };
        var vm = new VirtualMachine(ScriptAssembler.Assemble(t, "AE001H_TRAVEL", ops,
            System.Array.Empty<string>()), t, new RecordingHost());
        vm.Run();

        Assert.Equal((40L, -20L, 0L), (vm.Globals[11], vm.Globals[12], vm.Globals[13]));
        Assert.Equal((50L, -80L, 0L), (vm.Globals[21], vm.Globals[22], vm.Globals[23]));
        Assert.Equal((130L, -100L, 0L), (vm.Globals[31], vm.Globals[32], vm.Globals[33]));
        Assert.Equal((130.0, -100.0, 0.0), vm.Gfx.TryGet(0xcf3a)!.TranslationTarget);
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
    public void Op0x232_NegativeRgbPreservesNativeDefaultStaticColor()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "AE001H_COLOR", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), I(0xcb8e)}), (0x55, new[]{G(2), I(1200)}),
            (0x55, new[]{G(3), I(224)}), (0x55, new[]{G(4), I(1)}),
            (0x51, new[]{G(4), I(0), G(4)}),
            (0x232, new[]{G(1), G(2), G(3), G(4)}), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0xcb8e)!;
        Assert.Equal(0xffffffff, o.Color);
        Assert.Equal(0xe0ffffff, o.ColorTarget);
        Assert.Equal(0, o.StaticColorMode);
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
        Assert.Equal(4, o.SrcFrameCount);
        Assert.Equal(1, o.SrcColumns);
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
        g.SetSrcRect(0x100, frameCount: 4, columns: 1, cell: 2, period: 800);
        var o = g.TryGet(0x100)!;
        Assert.Equal(4, o.SrcFrameCount);
        Assert.Equal(2, o.SrcCell);
        Assert.Equal(800, o.SrcPeriod);
        Assert.True(o.SrcAnim);
        Assert.Equal(-1, o.SrcStart);   // uninitialized until first interpolated frame
    }

    [Fact]
    public void SetSrcRect_ClampsGridToAtLeastOne()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRect(0x100, frameCount: 0, columns: 0, cell: 0, period: 0);
        var o = g.TryGet(0x100)!;
        Assert.Equal(1, o.SrcFrameCount);
        Assert.Equal(1, o.SrcColumns);
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

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
    public void Op0x228_QueriesCurrentTranslation_NotTargetOrBasePosition()
    {
        var t = T();
        // V24=(360,20), current translation=(12,34,5), and delayed target=(40,-20,0) are independent.
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), I(0xcf3a)}), (0x55, new[]{G(2), I(0)}),
            (0x55, new[]{G(3), I(360)}), (0x55, new[]{G(4), I(20)}), (0x55, new[]{G(5), I(0)}),
            (0x1fb, new[]{G(1), I(0), I(0), I(0), I(1), I(1), G(3), G(4)}),
            (0x55, new[]{G(14), I(12)}), (0x55, new[]{G(15), I(34)}), (0x55, new[]{G(16), I(5)}),
            (0x1ff, new[]{G(1), G(14), G(15), G(16)}),
            (0x55, new[]{G(6), I(300)}), (0x55, new[]{G(7), I(40)}),
            (0x55, new[]{G(8), I(20)}), (0x51, new[]{G(8), I(0), G(8)}),
            (0x220, new[]{G(1), G(2), G(6), G(7), G(8), G(5)}),
            (0x228, new[]{G(10), G(1), G(11), G(12), G(13)}),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(12, vm.Globals[11]);
        Assert.Equal(34, vm.Globals[12]);
        Assert.Equal(5, vm.Globals[13]);
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
    public void Op0x228_ReusedHandleReadsResetCurrentInsteadOfPreviousTarget()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "REUSED_TRANSLATION", new List<(int, Operand[])>
        {
            (0x55, new[]{G(1), I(0xcb20)}), (0x55, new[]{G(2), I(0)}),
            // SC0000 rebinds the handle, resets only current translation, then queries current.
            (0x1fb, new[]{G(1), I(4), I(0), I(0), I(800), I(500), I(0), I(-500)}),
            (0x1ff, new[]{G(1), I(0), I(0), I(0)}),
            (0x228, new[]{G(10), G(1), G(11), G(12), G(13)}),
            (0x50, new[]{G(22), G(12), I(600)}),
            (0x220, new[]{G(1), G(2), I(500), G(11), G(22), G(13)}),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Gfx.BindDraw(0xcb20, 4, 0, 0, 800, 500, 0, 50);
        vm.Gfx.SetCurrentTranslation(0xcb20, (0, 600, 0));
        vm.Gfx.SetTranslationChannel(0xcb20, 0, 0, (0, 600, 0));
        vm.Run();

        Assert.Equal((0L, 0L, 0L), (vm.Globals[11], vm.Globals[12], vm.Globals[13]));
        Assert.Equal((0.0, 600.0, 0.0), vm.Gfx.TryGet(0xcb20)!.TranslationTarget);
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

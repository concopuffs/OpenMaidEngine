using System;
using System.Collections.Generic;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// Opcodes 創刻のアテリアル's scripts use that had no handler (vm-map/opcodes.toml): the float comparisons 0x2df to
/// 0x2e4 and the square root 0x1cd, the voice query 0x1c4, the object transform reset 0x1fc with its range forms 0x253,
/// 0x254 and 0x256, and 0x225, which erases queued transition commands.
/// </summary>
public class Age438ComputeAndTransformOpsTests
{
    private const int Imm = 0, Float = 1, GlobalInt = 3, GlobalFloat = 4;
    private const int Exit = 0x2;

    private static Operand I(long value) => new(Imm, value);
    private static Operand F(float value) => new(Float, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    private static Operand G(int address) => new(GlobalInt, address);
    private static Operand GF(int address) => new(GlobalFloat, address);

    // abi null is the registry's whole table: the game's own table holds only the opcodes its scripts use.
    private static VirtualMachine Build(string? abi, params (int, Operand[])[] instructions)
    {
        OpcodeTable table = abi == null ? OpcodeTableJson.Load(Paths.OpcodesJson) : OpcodeTableJson.Load(Paths.OpcodesJson, abi);
        var ops = new List<(int, Operand[])>(instructions) { (Exit, Array.Empty<Operand>()) };
        return new VirtualMachine(ScriptAssembler.Assemble(table, "OPS.BIN", ops, []), table, new RecordingWaits(),
                                  new VmOptions());
    }

    private static VirtualMachine Run(string? abi, params (int, Operand[])[] instructions)
    {
        VirtualMachine vm = Build(abi, instructions);
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);
        return vm;
    }

    // ---- floats ----

    [Theory]
    [InlineData(0x2df, 1.5f, 1.5f, 1)]
    [InlineData(0x2df, 1.5f, 1.25f, 0)]
    [InlineData(0x2e0, 1.5f, 1.25f, 1)]
    [InlineData(0x2e0, 1.5f, 1.5f, 0)]
    [InlineData(0x2e1, 1.25f, 1.5f, 1)]
    [InlineData(0x2e1, 1.5f, 1.5f, 0)]
    [InlineData(0x2e2, 1.5f, 1.5f, 1)]
    [InlineData(0x2e2, 1.75f, 1.5f, 0)]
    [InlineData(0x2e3, 1.75f, 1.5f, 1)]
    [InlineData(0x2e3, 1.5f, 1.5f, 0)]
    [InlineData(0x2e4, 1.5f, 1.5f, 1)]
    [InlineData(0x2e4, 1.25f, 1.5f, 0)]
    // A NaN is equal to nothing, different from everything, and neither less nor greater.
    [InlineData(0x2df, float.NaN, float.NaN, 0)]
    [InlineData(0x2e0, float.NaN, 1f, 1)]
    [InlineData(0x2e1, float.NaN, 1f, 0)]
    [InlineData(0x2e2, 1f, float.NaN, 0)]
    [InlineData(0x2e3, float.NaN, 1f, 0)]
    [InlineData(0x2e4, float.NaN, float.NaN, 0)]
    public void TheFloatComparisonsWriteOneOrZero(int opcode, float a, float b, int expected)
    {
        // The default table: scripts of the game use only 0x2e1 and 0x2e3, so its own table lacks the rest.
        VirtualMachine vm = Run(null, (opcode, [G(0x300), F(a), F(b)]));

        Assert.Equal(expected, vm.Globals.GetValueOrDefault(0x300));
    }

    [Fact]
    public void AComparisonConvertsAnIntegerOperand()
    {
        // SC scripts compare a float cell with an integer: the float accessor converts it.
        VirtualMachine vm = Run("SYS4438",
            (0x2d5, [GF(1), F(2.5f)]),
            (0x2e3, [G(0x300), GF(1), I(2)]),
            (0x2e3, [G(0x301), GF(1), I(3)]),
            (0x2e1, [G(0x302), GF(1), GF(1)]));

        Assert.Equal((1L, 0L, 0L),
            (vm.Globals[0x300], vm.Globals.GetValueOrDefault(0x301), vm.Globals.GetValueOrDefault(0x302)));
    }

    [Fact]
    public void TheSquareRootIsAFloatAndTruncatesIntoAnIntegerCell()
    {
        VirtualMachine floats = Run("SYS4438", (0x1cd, [GF(1), F(6.25f)]), (0x1cd, [GF(2), F(-1f)]));
        Assert.Equal(2.5f, BitConverter.Int32BitsToSingle(unchecked((int)floats.GlobalFloats[1])));
        Assert.True(float.IsNaN(BitConverter.Int32BitsToSingle(unchecked((int)floats.GlobalFloats[2]))));

        // An integer operand is converted, and an integer destination takes the root truncated.
        VirtualMachine integers = Run(null, (0x1cd, [G(0x300), I(17)]));
        Assert.Equal(4L, integers.Globals[0x300]);
    }

    [Fact]
    public void TheVoiceQueryIsZeroWhileNothingPlays()
    {
        VirtualMachine vm = Run(null, (0x55, [G(0x300), I(7)]), (0x1c4, [G(0x300)]));

        Assert.Equal(0L, vm.Globals.GetValueOrDefault(0x300, -1));
    }

    // ---- object transforms ----

    [Fact]
    public void ResetReturnsAnObjectsCurrentTransformToIdentity()
    {
        VirtualMachine vm = Run(null,
            (0x1fd, [I(500), I(210), I(240), I(100)]),
            (0x1fe, [I(500), I(0), I(0), I(1), I(30)]),
            (0x1ff, [I(500), I(12), I(-7), I(3)]),
            (0x1fc, [I(500)]),
            (0x1fc, [I(900)]));        // the handle is created, as native's get-or-create does

        var o = vm.Gfx.TryGet(500)!;
        Assert.False(o.TransformMarked);
        Assert.Equal((1.0, 1.0, 1.0), o.ScaleCurrent);
        Assert.Equal((0.0, 0.0, 0.0), o.TranslationCurrent);
        // The axis stays; the angle of the identity is 0.
        Assert.Equal((0.0, 0.0, 1.0, 0.0), o.RotationCurrent);
        Assert.NotNull(vm.Gfx.TryGet(900));
    }

    [Fact]
    public void TheRangeFormsTouchOnlyTheObjectsThatExist()
    {
        VirtualMachine vm = Run(null,
            (0x1ff, [I(100), I(1), I(1), I(1)]),
            (0x1ff, [I(102), I(2), I(2), I(2)]),
            (0x1ff, [I(105), I(5), I(5), I(5)]),
            (0x256, [I(100), I(5), F(40f), F(-8f), F(0.5f)]),      // handles 100 to 104: 100 and 102 exist
            (0x253, [I(105), I(1)]));                              // handle 105

        Assert.Equal((40.0, -8.0, 0.5), vm.Gfx.TryGet(100)!.TranslationCurrent);
        Assert.Equal((40.0, -8.0, 0.5), vm.Gfx.TryGet(102)!.TranslationCurrent);
        Assert.True(vm.Gfx.TryGet(102)!.TransformMarked);
        Assert.Equal((0.0, 0.0, 0.0), vm.Gfx.TryGet(105)!.TranslationCurrent);
        Assert.False(vm.Gfx.TryGet(105)!.TransformMarked);
        // No object was made for 101, 103 or 104.
        Assert.Null(vm.Gfx.TryGet(101));
        Assert.Null(vm.Gfx.TryGet(103));
        Assert.Null(vm.Gfx.TryGet(104));
    }

    [Fact]
    public void TheRangeScaleDividesItsPercentages()
    {
        var gfx = new GfxState();
        gfx.GetOrCreate(10);
        gfx.GetOrCreate(11);
        gfx.GetOrCreate(20);

        gfx.SetCurrentScaleRange(10, 2, (150, 50, 100));
        gfx.SetCurrentScaleRange(20, 0, (300, 300, 300));          // an empty range
        gfx.SetCurrentScaleRange(20, -4, (300, 300, 300));

        Assert.Equal((1.5, 0.5, 1.0), gfx.TryGet(10)!.ScaleCurrent);
        Assert.Equal((1.5, 0.5, 1.0), gfx.TryGet(11)!.ScaleCurrent);
        Assert.Equal((1.0, 1.0, 1.0), gfx.TryGet(20)!.ScaleCurrent);
    }

    // ---- queued transition commands ----

    private static GfxState WithFourCommands()
    {
        var gfx = new GfxState();
        gfx.QueueSurfaceAlphaTransition(100, 2, 3, 1, 4, 1, 0, 500);
        gfx.QueuePatternedSurfaceTransition(101, 2, 3, 1, 4, 1, 8, 16, 0, 500);
        gfx.QueueRadialBlurRangeTransition(102, 14, 70_010, 23, 100, 512, 204, 0, 512, 204, 0, 500);
        gfx.QueueDirectionalBlurRangeTransition(103, 14, 70_010, 23, 10, 0, 20, 90, 0, 500);
        return gfx;
    }

    private static (int Alpha, int Patterned, int Radial, int Directional) Commands(GfxState gfx)
        => (gfx.SnapshotForegroundTransitions(0).Count, gfx.SnapshotPatternedSurfaceTransitions(0).Count,
            gfx.SnapshotRadialBlurRangeTransitions(0).Count, gfx.SnapshotDirectionalBlurRangeTransitions(0).Count);

    [Fact]
    public void ACountAboveOneErasesTheCommandsOfTheRange()
    {
        GfxState gfx = WithFourCommands();
        Assert.Equal((1, 1, 1, 1), Commands(gfx));

        gfx.EraseTransitionCommands(101, 2);        // 101 and 102

        Assert.Equal((1, 0, 0, 1), Commands(gfx));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    public void AnyOtherCountErasesTheOneCommand(int count)
    {
        GfxState gfx = WithFourCommands();

        gfx.EraseTransitionCommands(103, count);
        gfx.EraseTransitionCommands(50, count);     // no command has this handle

        Assert.Equal((1, 1, 1, 0), Commands(gfx));
    }

    [Fact]
    public void TheOpcodeErasesThroughTheGraphicsState()
    {
        VirtualMachine vm = Build("SYS4438", (0x225, [I(101), I(3)]));
        vm.Gfx.QueueSurfaceAlphaTransition(100, 2, 3, 1, 4, 1, 0, 500);
        vm.Gfx.QueuePatternedSurfaceTransition(101, 2, 3, 1, 4, 1, 8, 16, 0, 500);
        vm.Gfx.QueueRadialBlurRangeTransition(102, 14, 70_010, 23, 100, 512, 204, 0, 512, 204, 0, 500);
        vm.Gfx.QueueDirectionalBlurRangeTransition(103, 14, 70_010, 23, 10, 0, 20, 90, 0, 500);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((1, 0, 0, 0), Commands(vm.Gfx));
    }
}

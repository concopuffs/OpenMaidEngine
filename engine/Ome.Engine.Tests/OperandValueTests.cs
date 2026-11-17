using System;
using System.Collections.Generic;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>Native's operand accessors (docs/engine-re.md, "VM value, array and call semantics relevant to
/// compilation"): the integer read truncates a float form and parses a string with atol, the integer write stores
/// (float)value into a float cell, the types those accessors throw on halt the script, and SYS4433's transform family
/// reads and writes the operands its registry contract names as floats (backlog "Float operands").</summary>
public class OperandValueTests
{
    private const int Imm = 0, Float = 1, Str = 2, GlobalInt = 3, GlobalFloat = 4, GlobalString = 5, GlobalStringRef = 8;
    private const int Exit = 0x2, Add = 0x50, Mov = 0x55, SetString = 0x192;
    private const int SetScaleCurrent = 0x1fd, SetTranslationCurrent = 0x1ff, SetPosition = 0x219;
    private const int QueryTranslationCurrent = 0x228;

    private static Operand F(float value) => new(Float, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    private static Operand G(int address) => new(GlobalInt, address);
    private static Operand GF(int address) => new(GlobalFloat, address);
    private static Operand I(long value) => new(Imm, value);
    private static long Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private static VirtualMachine Run(string abiId, List<(int, Operand[])> ops, string[]? strings = null,
                                      Action<VirtualMachine>? seed = null)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, abiId);
        ops.Add((Exit, Array.Empty<Operand>()));
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "VALUES.BIN", ops, strings ?? []), table,
                                    new RecordingWaits());
        seed?.Invoke(vm);
        vm.Run();
        return vm;
    }

    [Fact]
    public void TheIntegerReadTruncatesAFloatTowardZeroAndParsesAStringAsAtolDoes()
    {
        var vm = Run("SYS4422",
        [
            (Add, [G(1), F(37.9f), I(0)]),
            (Add, [G(2), F(-37.9f), I(0)]),
            // __ftol2_sse's integer indefinite for NaN and for a value outside int32.
            (Add, [G(3), F(float.NaN), I(0)]),
            (Add, [G(4), F(3e9f), I(0)]),
            (Add, [G(5), new Operand(Str, 0), I(0)]),
            (Add, [G(6), new Operand(Str, 1), I(0)]),
            (Add, [G(7), new Operand(Str, 2), I(0)]),
            (SetString, [new Operand(GlobalString, 1), new Operand(Str, 3)]),
            (Add, [G(8), new Operand(GlobalString, 1), I(0)]),
            (Mov, [G(9), F(2.5f)]),
            (Mov, [G(10), GF(4)]),
        ],
        ["  -42abc", "１２", "99999999999", "7 seas"],
        vm => vm.GlobalFloats[4] = Bits(-2.75f));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(37, vm.Globals[1]);
        Assert.Equal(-37, vm.Globals[2]);
        Assert.Equal(int.MinValue, vm.Globals[3]);
        Assert.Equal(int.MinValue, vm.Globals[4]);
        Assert.Equal(-42, vm.Globals[5]);
        Assert.Equal(0, vm.Globals[6]);              // atol reads ASCII digits only
        Assert.Equal(int.MaxValue, vm.Globals[7]);   // strtol clamps
        Assert.Equal(7, vm.Globals[8]);
        Assert.Equal(2, vm.Globals[9]);
        Assert.Equal(-2, vm.Globals[10]);
    }

    [Fact]
    public void AnIntegerStoredInAFloatCellIsThatNumberAsAFloat()
    {
        var vm = Run("SYS4422", [(Mov, [GF(3), I(5)]), (Add, [GF(4), I(-7), I(0)])]);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(Bits(5f), vm.GlobalFloats[3]);
        Assert.Equal(Bits(-7f), vm.GlobalFloats[4]);
    }

    [Fact]
    public void AnOperandTypeTheIntegerAccessorsRejectHaltsTheScript()
    {
        var read = Run("SYS4422", [(Add, [G(1), new Operand(GlobalStringRef, 0), I(0)]), (Mov, [G(2), I(1)])]);
        Assert.Equal("operand-type-rejected:read:type 0x8", read.HaltReason);
        Assert.False(read.Globals.ContainsKey(2));

        var write = Run("SYS4422", [(Mov, [I(5), I(1)]), (Mov, [G(2), I(1)])]);
        Assert.Equal("operand-type-rejected:write:type 0x0", write.HaltReason);
        Assert.False(write.Globals.ContainsKey(2));
    }

    // Kamidori's ALLMAP@0x5d9 and SAVE@0xcad pass 37.5 for op 0x1fd's x and y scale.
    [Theory]
    [InlineData("SYS4433", 0.375)]
    [InlineData("SYS4422", 0.37)]
    public void SetScaleCurrentReadsItsScaleAsTheBuildsContractSays(string abiId, double expected)
    {
        var vm = Run(abiId, [(SetScaleCurrent, [I(0x1000), F(37.5f), F(37.5f), I(100)])]);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((expected, expected, 1.0), vm.Gfx.TryGet(0x1000)!.ScaleCurrent);
    }

    [Fact]
    public void Sys4433ReadsAndWritesTheTranslationAsFloats()
    {
        var vm = Run("SYS4433",
        [
            (SetTranslationCurrent, [I(0x1000), F(12.5f), F(-3.25f), I(4)]),
            (QueryTranslationCurrent, [G(1), I(0x1000), GF(1), G(2), GF(3)]),
        ]);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((12.5, -3.25, 4.0), vm.Gfx.TryGet(0x1000)!.TranslationCurrent);
        Assert.Equal(0, vm.Globals[1]);
        Assert.Equal(Bits(12.5f), vm.GlobalFloats[1]);
        Assert.Equal(-3, vm.Globals[2]);   // an integer destination of the float write truncates
        Assert.Equal(Bits(4f), vm.GlobalFloats[3]);
    }

    [Fact]
    public void Sys4422ReadsAndWritesTheTranslationAsIntegers()
    {
        var vm = Run("SYS4422",
        [
            (SetTranslationCurrent, [I(0x1000), F(12.5f), F(-3.25f), I(4)]),
            (QueryTranslationCurrent, [G(1), I(0x1000), GF(1), G(2), GF(3)]),
        ]);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((12.0, -3.0, 4.0), vm.Gfx.TryGet(0x1000)!.TranslationCurrent);
        Assert.Equal(Bits(12f), vm.GlobalFloats[1]);
        Assert.Equal(-3, vm.Globals[2]);
    }

    // Native's position (obj+0x24) is a float32 the draw matrix uses as it is, so a fraction is not truncated.
    [Fact]
    public void ASys4433PositionKeepsItsFractionWhereTheObjectIsDrawn()
    {
        var vm = Run("SYS4433", [(SetPosition, [I(0x1000), F(12.5f), F(-3.25f), I(0)])],
                     seed: vm =>
                     {
                         vm.Gfx.SetSurface(4, 0x25, 0);
                         vm.Gfx.BindDraw(0x1000, 4, 0, 0, 8, 8, 0, 0);
                     });

        Assert.Equal("exit", vm.HaltReason);
        RenderObject drawn = Assert.Single(vm.Gfx.SnapshotVisibleObjects(), o => o.Handle == 0x1000);
        Assert.Equal((12.5, -3.25), (drawn.DstX, drawn.DstY));
    }

    [Fact]
    public void TheOpcodeTableCarriesEachAbisFloatOperands()
    {
        Assert.True(OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433").TryGetDefinition(SetScaleCurrent, out var sys4433));
        Assert.Equal(new HashSet<int> { 1, 2, 3 }, sys4433.FloatOperands);
        Assert.True(OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422").TryGetDefinition(SetScaleCurrent, out var sys4422));
        Assert.Empty(sys4422.FloatOperands);
    }
}

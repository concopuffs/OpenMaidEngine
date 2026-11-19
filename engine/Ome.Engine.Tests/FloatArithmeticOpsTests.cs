using System;
using System.Collections.Generic;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// The float arithmetic opcodes 0x2d0 to 0x2d3, 0x2d5 and 0x2d7 (vm-map/opcodes.toml; read in 創刻のアテリアル's AGE
/// 4.38D image, where its scripts use them at 33,164 sites). Native fetches each operand through the float accessor,
/// which leaves an integer exact, computes on the x87 stack and rounds to a float once, at the store.
/// </summary>
public class FloatArithmeticOpsTests
{
    private const int Imm = 0, Float = 1, GlobalInt = 3, GlobalFloat = 4, LocalFloatPointer = 0xd;
    private const int Exit = 0x2;
    private const int AddFloat = 0x2d0, SubFloat = 0x2d1, MulFloat = 0x2d2, DivFloat = 0x2d3, MovFloat = 0x2d5, FloatToInt = 0x2d7;

    private static Operand F(float value) => new(Float, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    private static Operand G(int address) => new(GlobalInt, address);
    private static Operand GF(int address) => new(GlobalFloat, address);
    private static Operand I(long value) => new(Imm, value);

    private static VirtualMachine Run(params (int, Operand[])[] instructions)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4438");
        var ops = new List<(int, Operand[])>(instructions) { (Exit, Array.Empty<Operand>()) };
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "FLOATS.BIN", ops, []), table, new RecordingWaits());
        vm.Run();
        return vm;
    }

    private static float FloatAt(VirtualMachine vm, int address)
        => BitConverter.Int32BitsToSingle(unchecked((int)vm.GlobalFloats.GetValueOrDefault(address)));

    [Fact]
    public void TheFourOperationsComputeInFloats()
    {
        VirtualMachine vm = Run(
            (AddFloat, [GF(1), F(1.5f), F(2.25f)]),
            (SubFloat, [GF(2), F(1.5f), F(2.25f)]),
            (MulFloat, [GF(3), F(1.5f), F(-2.0f)]),
            (DivFloat, [GF(4), F(1.0f), F(3.0f)]),
            // An operand may be a cell an earlier instruction wrote, or an integer.
            (AddFloat, [GF(5), GF(1), I(2)]),
            (MulFloat, [GF(6), I(3), GF(4)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((3.75f, -0.75f, -3.0f, 1.0f / 3.0f), (FloatAt(vm, 1), FloatAt(vm, 2), FloatAt(vm, 3), FloatAt(vm, 4)));
        Assert.Equal((5.75f, 3.0f * (1.0f / 3.0f)), (FloatAt(vm, 5), FloatAt(vm, 6)));
    }

    [Fact]
    public void AnIntegerOperandIsExactUntilTheStore()
    {
        // 16,777,217 has no float of its own. Native's float accessor leaves the integer exact on the x87 stack, so
        // adding 1 gives 16,777,218; rounding the integer to a float first would give 16,777,216.
        VirtualMachine vm = Run(
            (AddFloat, [GF(1), I(16_777_217), F(1.0f)]),
            (SubFloat, [GF(2), I(16_777_217), I(16_777_216)]),
            (MovFloat, [GF(3), I(16_777_217)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((16_777_218f, 1.0f, 16_777_216f), (FloatAt(vm, 1), FloatAt(vm, 2), FloatAt(vm, 3)));
    }

    [Fact]
    public void ADivisorOfZeroIsNotTested()
    {
        VirtualMachine vm = Run(
            (DivFloat, [GF(1), F(1.0f), F(0.0f)]),
            (DivFloat, [GF(2), F(-1.0f), I(0)]),
            (DivFloat, [GF(3), F(0.0f), F(0.0f)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((float.PositiveInfinity, float.NegativeInfinity), (FloatAt(vm, 1), FloatAt(vm, 2)));
        Assert.True(float.IsNaN(FloatAt(vm, 3)));
    }

    [Fact]
    public void AFloatIsMovedAsItIsAndTruncatedIntoAnInteger()
    {
        VirtualMachine vm = Run(
            (MovFloat, [GF(1), F(-2.9f)]),
            (MovFloat, [GF(2), GF(1)]),
            // The float accessor's store truncates into an integer cell, and 0x2d7 converts with __ftol2_sse.
            (MovFloat, [G(3), F(-2.9f)]),
            (FloatToInt, [G(4), F(-2.9f)]),
            (FloatToInt, [G(5), GF(1)]),
            (FloatToInt, [G(6), F(37.9f)]),
            (FloatToInt, [G(7), F(float.NaN)]),
            (FloatToInt, [G(8), F(3e9f)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((-2.9f, -2.9f), (FloatAt(vm, 1), FloatAt(vm, 2)));
        Assert.Equal((-2L, -2L, -2L, 37L), (vm.Globals[3], vm.Globals[4], vm.Globals[5], vm.Globals[6]));
        Assert.Equal(((long)int.MinValue, (long)int.MinValue), (vm.Globals[7], vm.Globals[8]));
    }

    [Fact]
    public void AFloatPointerOperandStopsTheRunUntilThePortHasOne()
    {
        // 創刻のアテリアル passes local float pointers (type 0xd) to these opcodes. The port's float accessors have no
        // such cell yet (docs/backlog.md, "Onboard 創刻のアテリアル"), so the run stops instead of computing a wrong value.
        VirtualMachine vm = Run((MovFloat, [new Operand(LocalFloatPointer, 0), F(1.0f)]));

        Assert.NotEqual("exit", vm.HaltReason);
    }
}

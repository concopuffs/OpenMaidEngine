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
    private const int Imm = 0, Float = 1, GlobalInt = 3, GlobalFloat = 4, LocalFloat = 0xa, LocalFloatPointer = 0xd;
    private const int Exit = 0x2, Add = 0x50, LookupArray = 0x61, TakeAddress = 0x63, LookupArray2d = 0x12c, CopyDwords = 0x1b0;
    private const int AddFloat = 0x2d0, SubFloat = 0x2d1, MulFloat = 0x2d2, DivFloat = 0x2d3, MovFloat = 0x2d5, FloatToInt = 0x2d7;

    private static Operand F(float value) => new(Float, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    private static Operand G(int address) => new(GlobalInt, address);
    private static Operand GF(int address) => new(GlobalFloat, address);
    private static Operand LF(int index) => new(LocalFloat, index);
    private static Operand FP(int index) => new(LocalFloatPointer, index);
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
    public void AFloatReferenceReadsAndWritesTheCellItNames()
    {
        // 創刻のアテリアル binds local float references (type 0xd) with lookup-array, lookup-array-2d and take-address,
        // and its float opcodes and op 0x320 read and write through them (docs/engine-re.md, "Float cells as arrays,
        // and float references").
        VirtualMachine vm = Run(
            (MovFloat, [GF(0x12), F(2.5f)]),
            (LookupArray, [FP(0), GF(0x10), I(2)]),          // -> global float 0x12
            (AddFloat, [GF(1), FP(0), F(1.0f)]),             // read through it
            (MovFloat, [FP(0), F(8.0f)]),                    // write through it
            (LookupArray2d, [FP(1), GF(0x20), I(2), I(3), I(1)]),   // -> global float 0x20 + 2 * 3 + 1
            (MovFloat, [FP(1), F(4.0f)]),
            (TakeAddress, [FP(2), FP(1)]),                   // a reference copies what another names
            (MulFloat, [FP(2), FP(2), I(3)]),
            (LookupArray, [FP(3), LF(4), I(1)]),             // -> local float 5
            (MovFloat, [FP(3), F(0.5f)]),
            (AddFloat, [GF(2), LF(5), FP(3)]),
            (FloatToInt, [G(0x300), FP(2)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((3.5f, 8.0f, 12.0f, 1.0f), (FloatAt(vm, 1), FloatAt(vm, 0x12), FloatAt(vm, 0x27), FloatAt(vm, 2)));
        Assert.Equal(12L, vm.Globals[0x300]);
    }

    [Fact]
    public void TheIntegerAccessorsRefuseAFloatReference()
    {
        // vm_operand_fetch and vm_operand_write have no case for type 0xd in AGE 4.38D: the script ends with an error.
        VirtualMachine read = Run(
            (LookupArray, [FP(0), GF(0x10), I(0)]),
            (Add, [G(0x300), FP(0), I(1)]));
        VirtualMachine write = Run(
            (LookupArray, [FP(0), GF(0x10), I(0)]),
            (Add, [FP(0), I(1), I(2)]));

        Assert.Equal("operand-type-rejected:read:type 0xd", read.HaltReason);
        Assert.Equal("operand-type-rejected:write:type 0xd", write.HaltReason);
    }

    [Fact]
    public void AFloatReferenceThatNamesNoFloatCellStopsTheRun()
    {
        // Native reads through a null pointer for one never bound, and reads an integer cell's bytes as a float for
        // one bound to an integer array. The port has neither, and stops instead of computing a wrong value.
        VirtualMachine unbound = Run((MovFloat, [GF(1), FP(0)]));
        VirtualMachine integer = Run(
            (LookupArray, [FP(0), G(0x300), I(0)]),
            (MovFloat, [GF(1), FP(0)]));

        Assert.Equal("float-reference-unbound:local 0x0", unbound.HaltReason);
        Assert.Equal("float-reference-names:Global 0x300", integer.HaltReason);
    }

    [Fact]
    public void AFloatCellIsAnArrayBaseForAnIntegerReference()
    {
        // vm_operand_resolve_address gives a float cell's address in AGE 4.38D, so an integer reference bound to a
        // float array copies its bits (copy-dwords, op 0x1b0).
        VirtualMachine vm = Run(
            (MovFloat, [GF(0x10), F(1.5f)]),
            (MovFloat, [GF(0x11), F(-2.0f)]),
            (CopyDwords, [GF(0x10), GF(0x40), I(2)]));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((1.5f, -2.0f), (FloatAt(vm, 0x40), FloatAt(vm, 0x41)));
    }
}

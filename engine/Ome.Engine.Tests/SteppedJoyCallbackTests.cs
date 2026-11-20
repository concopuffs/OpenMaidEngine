using System;
using System.Collections.Generic;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// AGE's stepped input callbacks (ops 0x102 to 0x105, native's SetKeyJumpEx family), the horizontal wheel (op 0x2e5,
/// AGE 4.38D) and the key state (op 0x10e): vm-map/opcodes.toml.
/// </summary>
public class SteppedJoyCallbackTests
{
    private const int Register = 0x102, SetSteps = 0x103, Dispatch = 0x104, SetRepeatMask = 0x105, Poll = 0xff;
    private const int Exit = 0x2, Return = 0x5, Add = 0x50;
    private const int CountA = 0x180, CountB = 0x181, CountC = 0x182;

    private static Operand I(long value) => new(0, value);
    private static Operand G(int address) => new(3, address);

    /// <summary>A script: <paramref name="body"/>, an exit, then three callbacks A, B and C that each add 1 to their
    /// own global and return. <paramref name="body"/> is given the callbacks' code offsets.</summary>
    private static VirtualMachine Build(Func<int, int, int, (int, Operand[])[]> body, RecordingWaits? waits = null)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // The body's length does not depend on the offsets, so lay it out once with placeholders.
        int Length((int, Operand[])[] instructions)
        {
            int dwords = 0;
            foreach ((_, Operand[] args) in instructions) dwords += 1 + 2 * args.Length;
            return dwords;
        }
        int a = Length(body(0, 0, 0)) + 1;      // after the exit
        int callback = 1 + 2 * 3 + 1;           // an add of three operands and a return
        var ops = new List<(int, Operand[])>(body(a, a + callback, a + 2 * callback))
        {
            (Exit, Array.Empty<Operand>()),
            (Add, [G(CountA), G(CountA), I(1)]), (Return, Array.Empty<Operand>()),
            (Add, [G(CountB), G(CountB), I(1)]), (Return, Array.Empty<Operand>()),
            (Add, [G(CountC), G(CountC), I(1)]), (Return, Array.Empty<Operand>()),
        };
        return new VirtualMachine(ScriptAssembler.Assemble(table, "STEPS.BIN", ops, []), table,
                                  waits ?? new RecordingWaits());
    }

    private static (long A, long B, long C) Counts(VirtualMachine vm)
        => (vm.Globals.GetValueOrDefault(CountA), vm.Globals.GetValueOrDefault(CountB),
            vm.Globals.GetValueOrDefault(CountC));

    private static (int, Operand[]) Op(int opcode, params Operand[] args) => (opcode, args);

    [Fact]
    public void AHeldActionCyclesThroughItsSteps()
    {
        VirtualMachine vm = Build((a, b, c) =>
        [
            Op(Register, I(0), I(0), I(a)), Op(Register, I(0), I(1), I(b)), Op(SetSteps, I(2)),
            Op(Poll), Op(Dispatch), Op(Poll), Op(Dispatch), Op(Poll), Op(Dispatch),
        ]);
        vm.UpdateInputCallbackState(0, true);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((2L, 1L, 0L), Counts(vm));     // A, B, then A again
    }

    [Fact]
    public void ADifferentRowStartsAgainAtStepZero()
    {
        // Action 0 is queued once: the first poll sees it, the later ones see nothing and take the idle row, which
        // is the action count's (7).
        VirtualMachine vm = Build((a, b, c) =>
        [
            Op(Register, I(0), I(0), I(c)),
            Op(Register, I(7), I(0), I(a)), Op(Register, I(7), I(1), I(b)), Op(SetSteps, I(2)),
            Op(Poll), Op(Dispatch), Op(Poll), Op(Dispatch), Op(Poll), Op(Dispatch),
        ]);
        vm.QueueInputCallback(0);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        // C at step 0; the idle row then starts at step 0 (A), not at the step the action left (1), and goes on to B.
        Assert.Equal((1L, 1L, 1L), Counts(vm));
    }

    [Theory]
    [InlineData(0b10, 1, 1)]    // action 1 returns to the dispatch, which then takes action 0 too
    [InlineData(0, 1, 0)]       // without the mask the highest action alone is dispatched
    public void AnActionOfTheRepeatMaskLetsTheLowerActionsRunToo(int mask, long a, long b)
    {
        VirtualMachine vm = Build((ta, tb, tc) =>
        [
            Op(Register, I(1), I(0), I(ta)), Op(Register, I(0), I(0), I(tb)), Op(SetSteps, I(1)),
            Op(SetRepeatMask, I(mask)), Op(Poll), Op(Dispatch),
        ]);
        vm.UpdateInputCallbackState(0, true);
        vm.UpdateInputCallbackState(1, true);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((a, b, 0L), Counts(vm));
    }

    [Fact]
    public void AStepWithNoTargetCallsNothingAndStillAdvances()
    {
        VirtualMachine vm = Build((a, b, c) =>
        [
            Op(Register, I(0), I(1), I(b)), Op(SetSteps, I(2)),
            Op(Poll), Op(Dispatch), Op(Poll), Op(Dispatch),
        ]);
        vm.UpdateInputCallbackState(0, true);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((0L, 1L, 0L), Counts(vm));     // step 0 has no target; step 1 calls B
    }

    [Fact]
    public void WithNoStepCountTheRunHaltsWhereNativeDividesByZero()
    {
        VirtualMachine vm = Build((a, b, c) => [Op(Register, I(0), I(0), I(a)), Op(Poll), Op(Dispatch)]);
        vm.UpdateInputCallbackState(0, true);

        vm.Run();

        Assert.StartsWith("division-by-zero:", vm.HaltReason);
        Assert.Equal((0L, 0L, 0L), Counts(vm));
    }

    // ---- the horizontal wheel ----

    private static VirtualMachine Wheel(int up = -1, int down = -1)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var ops = new List<(int, Operand[])> { Op(0x2e5, G(0x300)), Op(0x2e5, G(0x301)), (Exit, Array.Empty<Operand>()) };
        return new VirtualMachine(ScriptAssembler.Assemble(table, "HWHEEL.BIN", ops, []), table, new RecordingWaits(),
                                  new VmOptions(HWheelKeyUpAction: up, HWheelKeyDownAction: down));
    }

    [Fact]
    public void TheHorizontalWheelAccumulatesUntilTheOpcodeTakesIt()
    {
        VirtualMachine vm = Wheel();
        Assert.Equal(MouseWheelOutcome.Accumulated, vm.ApplyMouseHorizontalWheel(120));
        Assert.Equal(MouseWheelOutcome.Accumulated, vm.ApplyMouseHorizontalWheel(240));
        Assert.Equal(MouseWheelOutcome.Accumulated, vm.ApplyMouseHorizontalWheel(-120));
        Assert.Equal(MouseWheelOutcome.Dropped, vm.ApplyMouseHorizontalWheel(0));

        vm.Run();

        // The first read takes the sum and clears it.
        Assert.Equal((240L, 0L), (vm.Globals[0x300], vm.Globals.GetValueOrDefault(0x301, -1)));
    }

    [Fact]
    public void TheCatalogsWheelActionsAreReadFromItsSettings()
    {
        Sys4StartupSettings none = Sys4StartupSettings.FromPairs([]);
        Sys4StartupSettings set = Sys4StartupSettings.FromPairs(
            [new("HWHEELKEYUP", "9"), new("HWHEELKEYDOWN", "8")]);

        Assert.Equal((-1, -1), (none.HWheelKeyUpAction, none.HWheelKeyDownAction));
        Assert.Equal((9, 8), (set.HWheelKeyUpAction, set.HWheelKeyDownAction));
    }

    // ---- the key state ----

    [Fact]
    public void TheKeyStateIsNegativeWhileTheKeyIsDownAndOddOnceAfterAPress()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var ops = new List<(int, Operand[])>
        {
            Op(0x10e, G(0x300), I(0x1e)),       // A, pressed and held: bit 15, and bit 0 for the press
            Op(0x10e, G(0x301), I(0x1e)),       // A again: the first call took bit 0
            Op(0x10e, G(0x302), I(0x30)),       // B, pressed and let go before the call: bit 0 alone
            Op(0x10e, G(0x303), I(0x30)),       // B again
            Op(0x10e, G(0x304), I(0xff)),       // a scan code with no virtual key
            (Exit, Array.Empty<Operand>()),
        };
        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "KEYS.BIN", ops, []), table, new RecordingWaits());
        vm.UpdateKeyboardVirtualKeyState(0x41, true);
        vm.UpdateKeyboardVirtualKeyState(0x42, true);
        vm.UpdateKeyboardVirtualKeyState(0x42, false);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((-32767L, -32768L, 1L, 0L, 0L),
            (vm.Globals[0x300], vm.Globals[0x301], vm.Globals[0x302], vm.Globals.GetValueOrDefault(0x303, -1),
             vm.Globals.GetValueOrDefault(0x304, -1)));
    }
}

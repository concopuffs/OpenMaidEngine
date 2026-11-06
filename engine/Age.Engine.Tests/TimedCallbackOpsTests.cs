using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class TimedCallbackOpsTests
{
    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(T_IMM, value);
    private static Operand G(int address) => new(T_GINT, address);

    private sealed class ClockHost(long overshootMs = 0) : RecordingHost
    {
        private long _now;
        public override long InputClockMilliseconds => _now;

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            _now += duration + overshootMs;
        }
    }

    private sealed class NonPresentingClockHost : RecordingHost
    {
        private long _now;
        public override long InputClockMilliseconds => _now;

        public override void Sleep(long duration)
            => throw new InvalidOperationException("Timed callback pacing used presentation-capable sleep.");

        public override void WaitForTimedCallbackDeadline(long duration)
        {
            TimedCallbackWaitDurations.Add(duration);
            _now += duration;
        }
    }

    private static Script BuildTwoEventSequence()
        => ScriptAssembler.Assemble(Table, "TIMED_CALLBACKS",
            new List<(int, Operand[])>
            {
                (0xd3, Array.Empty<Operand>()),                  // 0
                (0xd4, new[] { I(10), I(3), I(14), I(22) }),   // 1: two events + look-ahead sentinel
                (0xd5, new[] { I(-1) }),                        // 10
                (0x2, Array.Empty<Operand>()),                   // 13
                (0x50, new[] { G(0x100), G(0x100), I(1) }),    // 14: on-time callback
                (0x5, Array.Empty<Operand>()),                   // 21: ret
                (0x50, new[] { G(0x101), G(0x101), I(1) }),    // 22: catch-up callback
                (0x5, Array.Empty<Operand>()),                   // 29: ret
            }, Array.Empty<string>());

    [Fact]
    public void RelativeSequenceRunsEveryPrimaryCallbackAtItsDeadline()
    {
        var host = new ClockHost();
        var vm = new VirtualMachine(BuildTwoEventSequence(), Table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(2, vm.Globals[0x100]);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x101));
        Assert.Equal(new long[] { 10, 10 }, host.SleptDurations);
        Assert.Equal(new long[] { 10, 10 }, host.TimedCallbackWaitDurations);
    }

    [Fact]
    public void SequenceUsesCatchUpTargetWhenTheFollowingDeadlineHasPassed()
    {
        var host = new ClockHost(overshootMs: 15);
        var vm = new VirtualMachine(BuildTwoEventSequence(), Table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(1, vm.Globals[0x101]);
        Assert.Equal(new long[] { 10 }, host.SleptDurations);
        Assert.Equal(new long[] { 10 }, host.TimedCallbackWaitDurations);
    }

    [Fact]
    public void SequencePacingDoesNotUsePresentationCapableScriptSleep()
    {
        var host = new NonPresentingClockHost();
        var vm = new VirtualMachine(BuildTwoEventSequence(), Table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new long[] { 10, 10 }, host.TimedCallbackWaitDurations);
        Assert.Empty(host.SleptDurations);
    }

    /// <summary>A click arrives while the sequence waits for its first entry: the frontend holds it in the input latch,
    /// as nothing polls but timed_callback_sequence_tick.</summary>
    private sealed class ClickDuringFirstWaitHost : RecordingHost
    {
        private long _now;
        public VirtualMachine? Vm;
        public override long InputClockMilliseconds => _now;

        public override void WaitForTimedCallbackDeadline(long duration)
        {
            if (TimedCallbackWaitDurations.Count == 0) Vm!.HoldTriggers(1 << VirtualMachine.AdvanceAction);
            TimedCallbackWaitDurations.Add(duration);
            _now += duration;
        }
    }

    private static Script BuildSequenceWithAbort(long abortOffset)
        => ScriptAssembler.Assemble(Table, "TIMED_CALLBACK_ABORT",
            new List<(int, Operand[])>
            {
                (0xd3, Array.Empty<Operand>()),                  // 0
                (0xd4, new[] { I(10), I(3), I(14), I(22) }),   // 1: two events + look-ahead sentinel
                (0xd5, new[] { I(abortOffset) }),               // 10
                (0x2, Array.Empty<Operand>()),                   // 13
                (0x50, new[] { G(0x100), G(0x100), I(1) }),    // 14: on-time callback
                (0x5, Array.Empty<Operand>()),                   // 21: ret
                (0x50, new[] { G(0x101), G(0x101), I(1) }),    // 22: catch-up callback
                (0x5, Array.Empty<Operand>()),                   // 29: ret
                (0x50, new[] { G(0x102), G(0x102), I(1) }),    // 30: the abort target
                (0x2, Array.Empty<Operand>()),                   // 37
            }, Array.Empty<string>());

    [Fact]
    public void ATriggerDuringASequenceWithoutAnAbortPcIsTakenAndDropped()
    {
        var host = new ClickDuringFirstWaitHost();
        var vm = new VirtualMachine(BuildSequenceWithAbort(-1), Table, host);
        host.Vm = vm;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(2, vm.Globals[0x100]);   // both entries still run
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x102));
        Assert.Equal(0, vm.HeldTriggers);     // the sequence's poll took the click: no later reveal sees it
    }

    [Fact]
    public void ATriggerDuringASequenceMovesTheFrameToItsAbortPc()
    {
        var host = new ClickDuringFirstWaitHost();
        var vm = new VirtualMachine(BuildSequenceWithAbort(30), Table, host);
        host.Vm = vm;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x100));   // no entry ran
        Assert.Equal(1, vm.Globals[0x102]);
        Assert.Equal(new long[] { 10 }, host.TimedCallbackWaitDurations);
        Assert.Equal(0, vm.HeldTriggers);
    }

    [Fact]
    public void ATriggerHeldBeforeTheSequenceStartsIsFlushed()
    {
        var host = new ClockHost();
        var vm = new VirtualMachine(BuildSequenceWithAbort(30), Table, host);
        vm.HoldTriggers(1 << VirtualMachine.AdvanceAction);   // op 0xd5 at cursor 0 runs input_flush_action_triggers

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(2, vm.Globals[0x100]);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x102));
    }
}

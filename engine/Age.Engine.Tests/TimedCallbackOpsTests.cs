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
    }
}

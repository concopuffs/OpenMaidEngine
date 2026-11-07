using System;
using System.Collections.Generic;
using System.Linq;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

/// <summary>Native ADV fast-forward pace: one op per main-loop iteration at a fixed cost, and op 0xc8 sleep arms
/// nothing while run-state 0x08000000 is set (sleep_op_0xc8@0x420ec0). Pacing runs on a virtual
/// <see cref="IPaceClock"/> here: time moves only when pacing sleeps or a test says a host service blocked, so the
/// schedule is checked exactly instead of timed.</summary>
public class FastForwardPaceTests
{
    /// <summary>A pace clock whose time moves only by pacing's sleeps and by an optional blocking service.</summary>
    private sealed class VirtualPaceClock : IPaceClock
    {
        private double _now;

        /// <summary>A host service that blocks for <c>Ms</c> just before pacing's read number <c>Read</c> (1-based).
        /// </summary>
        public (int Read, double Ms)? Block { get; init; }

        /// <summary>How often pacing read the time: once per paced op.</summary>
        public int Reads { get; private set; }

        public List<int> Sleeps { get; } = new();

        public double NowMs
        {
            get
            {
                Reads++;
                if (Block is { } block && block.Read == Reads) _now += block.Ms;
                return _now;
            }
        }

        public void Sleep(int milliseconds)
        {
            Sleeps.Add(milliseconds);
            _now += milliseconds;
        }
    }

    private static readonly (int, Operand[]) SkipOn = (0x88, new[] { I(1) });
    private static readonly (int, Operand[]) SkipOff = (0x88, new[] { I(0) });

    private static IEnumerable<(int, Operand[])> Probes(int count, int first = 0) =>
        Enumerable.Range(first, count).Select(i => (0x55, new[] { G(0x200 + i), I(i) }));

    private static Script Assemble(OpcodeTable table, string name, IEnumerable<(int, Operand[])> ops) =>
        ScriptAssembler.Assemble(table, name, ops.Append((0x2, Array.Empty<Operand>())).ToList(),
                                 Array.Empty<string>());

    private static Script Build(OpcodeTable table, string name, bool skip, int probes, params (int, Operand[])[] extra)
    {
        var ops = new List<(int, Operand[])>();
        if (skip) ops.Add(SkipOn);
        ops.AddRange(extra);
        ops.AddRange(Probes(probes));
        return Assemble(table, name, ops);
    }

    private static VirtualPaceClock Run(OpcodeTable table, Script script, int opMicroseconds,
                                        (int Read, double Ms)? block = null)
    {
        var clock = new VirtualPaceClock { Block = block };
        new VirtualMachine(script, table, new RecordingHost(), new VmOptions(FastForwardOpMicroseconds: opMicroseconds),
                           paceClock: clock).Run();
        return clock;
    }

    [Fact]
    public void SleepIsSkippedWhileFastForwardIsActive()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var skipped = new RecordingHost();
        new VirtualMachine(Build(table, "SLEEP_SKIP", skip: true, 0, (0xc8, new[] { I(1000) })), table, skipped).Run();
        Assert.Empty(skipped.SleptDurations);

        var physical = new RecordingHost { MessageSkip = true };
        new VirtualMachine(Build(table, "SLEEP_CTRL", skip: false, 0, (0xc8, new[] { I(1000) })), table, physical).Run();
        Assert.Empty(physical.SleptDurations);

        var normal = new RecordingHost();
        new VirtualMachine(Build(table, "SLEEP_NORMAL", skip: false, 0, (0xc8, new[] { I(1000) })), table, normal).Run();
        Assert.Equal(new long[] { 1000 }, normal.SleptDurations);
    }

    [Fact]
    public void FastForwardOpsArePacedAtTheConfiguredCost()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        VirtualPaceClock clock = Run(table, Build(table, "PACE", skip: true, 60), 2000);

        // 0x88, 60 probes and the closing 0x2 are paced, each after it runs. With no other time passing, each owes
        // its whole 2 ms.
        Assert.Equal(62, clock.Reads);
        Assert.Equal(Enumerable.Repeat(2, 62), clock.Sleeps);
    }

    [Fact]
    public void SubMillisecondCostsSleepWholeMillisecondsAndCarryTheRemainder()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        VirtualPaceClock clock = Run(table, Build(table, "PACE_NATIVE", skip: true, 2000),
                                     VmOptions.NativeFastForwardOpMicroseconds);

        // 2002 ops at 374 µs owe 748.748 ms. Pacing sleeps 1 ms whenever at least 1 ms is owed, so it ends less than
        // 1 ms behind the schedule and never ahead of it.
        Assert.Equal(2002, clock.Reads);
        Assert.All(clock.Sleeps, sleep => Assert.Equal(1, sleep));
        Assert.Equal(2002 * VmOptions.NativeFastForwardOpMicroseconds / 1000, clock.Sleeps.Sum());
    }

    [Fact]
    public void PacingAppliesOnlyDuringFastForwardAndOnlyWhenConfigured()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        VirtualPaceClock unskipped = Run(table, Build(table, "NO_SKIP", skip: false, 60), 2000);
        Assert.Equal(0, unskipped.Reads);
        Assert.Empty(unskipped.Sleeps);

        VirtualPaceClock unconfigured = Run(table, Build(table, "UNPACED", skip: true, 60), 0);
        Assert.Equal(0, unconfigured.Reads);
        Assert.Empty(unconfigured.Sleeps);
    }

    [Fact]
    public void TimeOutsideFastForwardIsNotBankedAsCredit()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // Fast-forward for 0x88 and 20 probes, off for 20 probes, then on again for 0x88, 20 probes and 0x2.
        Script script = Assemble(table, "PACE_RESUME",
            new[] { SkipOn }.Concat(Probes(20)).Append(SkipOff).Concat(Probes(20, 20)).Append(SkipOn)
                .Concat(Probes(20, 40)));
        // The unpaced stretch takes 500 ms, which pacing first sees on the resumed run's first read.
        VirtualPaceClock clock = Run(table, script, 2000, block: (22, 500));

        // The resumed run starts a new schedule, so its ops owe their 2 ms each instead of spending the 500 ms.
        Assert.Equal(43, clock.Reads);
        Assert.Equal(Enumerable.Repeat(2, 43), clock.Sleeps);
    }

    [Fact]
    public void ABlockingServiceBanksAtMostTheCreditCap()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // A host service blocks 500 ms before the 11th paced op (a modal movie, say).
        VirtualPaceClock clock = Run(table, Build(table, "PACE_BLOCK", skip: true, 200), 2000, block: (11, 500));

        // The 10 ops before it sleep 2 ms each. The block covers its own op and banks 100 ms, the cap, so the next
        // 50 ops at 2 ms run without sleeping; then every op sleeps 2 ms again. Banking the whole 500 ms would have
        // let every remaining op run unpaced.
        Assert.Equal(202, clock.Reads);
        Assert.Equal(Enumerable.Repeat(2, 202 - 1 - 50), clock.Sleeps);
    }

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
}

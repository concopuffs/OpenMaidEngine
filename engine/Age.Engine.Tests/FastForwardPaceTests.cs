using System;
using System.Collections.Generic;
using System.Diagnostics;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

/// <summary>Native ADV fast-forward pace: one op per main-loop iteration at a fixed cost, and op 0xc8 sleep arms
/// nothing while run-state 0x08000000 is set (sleep_op_0xc8@0x420ec0).</summary>
public class FastForwardPaceTests
{
    private static Script Build(OpcodeTable table, string name, bool skip, int probes, params (int, Operand[])[] extra)
    {
        var ops = new List<(int, Operand[])>();
        if (skip) ops.Add((0x88, new[] { I(1) }));
        ops.AddRange(extra);
        for (int i = 0; i < probes; i++) ops.Add((0x55, new[] { G(0x200 + i), I(i) }));
        ops.Add((0x2, Array.Empty<Operand>()));
        return ScriptAssembler.Assemble(table, name, ops, Array.Empty<string>());
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
        var script = Build(table, "PACE", skip: true, 60);
        var watch = Stopwatch.StartNew();
        new VirtualMachine(script, table, new RecordingHost(), new VmOptions(FastForwardOpMicroseconds: 2000)).Run();
        watch.Stop();
        // 61 paced ops at 2 ms: at least ~120 ms (whole-millisecond sleeps may land a little early or late).
        Assert.InRange(watch.ElapsedMilliseconds, 110, 2000);
    }

    [Fact]
    public void PacingAppliesOnlyDuringFastForwardAndOnlyWhenConfigured()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var normal = Stopwatch.StartNew();
        new VirtualMachine(Build(table, "NO_SKIP", skip: false, 60), table, new RecordingHost(),
            new VmOptions(FastForwardOpMicroseconds: 2000)).Run();
        normal.Stop();
        Assert.True(normal.ElapsedMilliseconds < 100, $"unskipped run took {normal.ElapsedMilliseconds} ms");

        var unpaced = Stopwatch.StartNew();
        new VirtualMachine(Build(table, "UNPACED", skip: true, 60), table, new RecordingHost()).Run();
        unpaced.Stop();
        Assert.True(unpaced.ElapsedMilliseconds < 100, $"unpaced run took {unpaced.ElapsedMilliseconds} ms");
    }

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
}

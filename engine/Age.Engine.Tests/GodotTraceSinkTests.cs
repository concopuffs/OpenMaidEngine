using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Age.Engine.Sys4;

public class GodotTraceSinkTests
{
    [Fact]
    public void SnapshotRetainsCurrentStackAndBoundedRecentSteps()
    {
        using var locator = new PageLocatorState("SYSTEM4", null);
        var sink = new GodotTraceSink(locator);
        sink.Emit(TraceEvent.FrameEnter("SYSTEM4.BIN", 0, FrameCause.TopScene));
        sink.Emit(TraceEvent.FrameEnter("BTL.BIN", 1, FrameCause.CallScript, 0x2b10));

        for (int index = 0; index < 140; index++)
        {
            var instruction = new Instruction(0x2400 + index, 0x100 + index,
                                              Array.Empty<Operand>());
            sink.Emit(TraceEvent.Step(index, instruction, 1));
        }

        GodotTraceSnapshot snapshot = sink.Snapshot();

        Assert.Equal("BTL.BIN", snapshot.CurrentScript);
        Assert.Equal(0x2400 + 139, snapshot.CurrentOffset);
        Assert.Equal(0x100 + 139, snapshot.CurrentOpcode);
        Assert.Equal(new[] { "SYSTEM4.BIN", "BTL.BIN" }, snapshot.CallStack);
        Assert.Equal(128, snapshot.RecentSteps.Count);
        Assert.Equal(0x2400 + 12, snapshot.RecentSteps[0].Offset);
        Assert.Equal(0x2400 + 139, snapshot.RecentSteps[^1].Offset);
    }

    [Fact]
    public void HaltSnapshotRetainsDeepestStackBeforeFramesUnwind()
    {
        using var locator = new PageLocatorState("SYSTEM4", null);
        var sink = new GodotTraceSink(locator);
        sink.Emit(TraceEvent.FrameEnter("SYSTEM4.BIN", 0, FrameCause.TopScene));
        sink.Emit(TraceEvent.FrameEnter("FIELD.BIN", 1, FrameCause.CallScript, 0x337d));
        sink.Emit(TraceEvent.FrameEnter("SC0600.BIN", 2, FrameCause.CallScript, 0x1202));
        sink.Emit(TraceEvent.Step(10,
            new Instruction(0x3403, 0x8f, new[] { new Operand(0, 0x6f28) }), 2));

        sink.Emit(TraceEvent.FrameExit("SC0600.BIN", 2, "Halted"));
        sink.Emit(TraceEvent.FrameExit("FIELD.BIN", 1, "Halted"));
        sink.Emit(TraceEvent.FrameExit("SYSTEM4.BIN", 0, "Halted"));

        GodotTraceSnapshot snapshot = Assert.IsType<GodotTraceSnapshot>(sink.HaltSnapshot);
        Assert.Equal("SC0600.BIN", snapshot.CurrentScript);
        Assert.Equal(0x3403, snapshot.CurrentOffset);
        Assert.Equal(new[] { "SYSTEM4.BIN", "FIELD.BIN", "SC0600.BIN" }, snapshot.CallStack);
    }

    [Fact]
    public void StepLimitReportShowsCoordinateFramesHotSitesAndTail()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var recent = new[]
        {
            new GodotTraceStepSnapshot("FIELD.BIN", 0x100, 0x55, 1),
            new GodotTraceStepSnapshot("FIELD.BIN", 0x107, 0x8e, 1),
            new GodotTraceStepSnapshot("FIELD.BIN", 0x100, 0x55, 1),
        };
        var snapshot = new GodotTraceSnapshot(
            "FIELD.BIN", 0x100, 0x55, 1,
            new[] { "SYSTEM4.BIN", "FIELD.BIN" }, recent);

        string report = StepLimitDiagnosticFormatter.Format(snapshot, table, topSites: 2, tailSteps: 2);

        Assert.Contains("[step-limit] last: FIELD@0x100 op=0x055 mov depth=1", report);
        Assert.Contains("[step-limit] frames: SYSTEM4 > FIELD", report);
        Assert.Contains("2x FIELD@0x100 op=0x055 mov", report);
        Assert.DoesNotContain("SYSTEM4@0x", report);
        Assert.EndsWith("[step-limit]   FIELD@0x100 op=0x055 mov", report);
    }
}

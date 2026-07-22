using Age.Engine.Diagnostics;
using Age.Engine.Model;

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
}

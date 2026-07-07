using System.IO;
using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Xunit;

public class TraceSinkTests
{
    [Fact]
    public void FactoriesSetKindAndFields()
    {
        var ins = new Instruction(0x40, 0x55, new[] { new Operand(3, 0x10), new Operand(0, 7) });
        var step = TraceEvent.Step(0x40, ins, 2);
        Assert.Equal(TraceEventKind.Step, step.Kind);
        Assert.Equal(0x55, step.Opcode);
        Assert.Same(ins, step.Ins);
        Assert.Equal(2, step.Depth);

        var cs = TraceEvent.CallScript(0x1ab, "ADDITEM");
        Assert.Equal(TraceEventKind.CallScript, cs.Kind);
        Assert.Equal(0x1abL, cs.Id);
        Assert.Equal("ADDITEM", cs.Name);
    }

    [Fact]
    public void NullSinkIsInertAndNotTracingSteps()
    {
        Assert.False(NullTraceSink.Instance.TracingSteps);
        NullTraceSink.Instance.Emit(TraceEvent.Halt("x", 1));   // must not throw
    }

    [Fact]
    public void TextSinkFormatsEachKind()
    {
        var sw = new StringWriter();
        var sink = new TextTraceSink(sw, table: null, includeSteps: true);
        sink.Emit(TraceEvent.FrameEnter("SC0000", 1, FrameCause.TopScene));
        sink.Emit(TraceEvent.CallScript(0x1ab, "ADDITEM"));
        sink.Emit(TraceEvent.Halt("exit", 27994));
        var outp = sw.ToString();
        Assert.Contains("» SC0000 (enter, TopScene)", outp);
        Assert.Contains("call-script 0x1ab =ADDITEM (resolved)", outp);
        Assert.Contains("halt: exit @ 27994 steps", outp);
    }
}

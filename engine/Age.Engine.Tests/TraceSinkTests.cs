using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class TraceSinkTests
{
    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);

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

    [Fact]
    public void VmEmitsFrameCallScriptAndHaltEvents()
    {
        var t = Table();
        // callee: exit.  caller: call-script 5 ; exit.
        var callee = ScriptAssembler.Assemble(t, "CALLEE",
            new List<(int, Operand[])> { (0x2, Array.Empty<Operand>()) }, Array.Empty<string>());
        var caller = ScriptAssembler.Assemble(t, "CALLER",
            new List<(int, Operand[])> { (0x3, new[] { new Operand(0, 5) }), (0x2, Array.Empty<Operand>()) },
            Array.Empty<string>());
        var sink = new RecordingTraceSink();
        var vm = new VirtualMachine(caller, t, new RecordingHost(), null,
                                    new MapProvider(new() { [5] = callee }), sink);
        vm.Run();

        var kinds = sink.Events.Select(e => e.Kind).ToList();
        Assert.Equal(TraceEventKind.FrameEnter, kinds[0]);                 // caller enters first
        Assert.Equal(TraceEventKind.Halt, kinds[^1]);                      // halt is last
        Assert.Equal(2, sink.Events.Count(e => e.Kind == TraceEventKind.FrameEnter));  // caller + callee
        Assert.Equal(2, sink.Events.Count(e => e.Kind == TraceEventKind.FrameExit));
        Assert.Contains(5L, sink.CallScriptIds);
        Assert.Equal(1, vm.CallScriptDispatches);
    }

    [Fact]
    public void StepEventsGatedByTracingSteps()
    {
        var t = Table();
        // mov g[0x10]=7 ; exit  => 2 executed instructions.
        var body = new List<(int, Operand[])>
        {
            (0x55, new[] { new Operand(3, 0x10), new Operand(0, 7) }),
            (0x2, Array.Empty<Operand>()),
        };
        var s = ScriptAssembler.Assemble(t, "S", body, Array.Empty<string>());

        var off = new RecordingTraceSink { TracingSteps = false };
        new VirtualMachine(s, t, new RecordingHost(), null, null, off).Run();
        Assert.Empty(off.Events.Where(e => e.Kind == TraceEventKind.Step));

        var on = new RecordingTraceSink { TracingSteps = true };
        new VirtualMachine(s, t, new RecordingHost(), null, null, on).Run();
        Assert.Equal(2, on.Events.Count(e => e.Kind == TraceEventKind.Step));
    }
}

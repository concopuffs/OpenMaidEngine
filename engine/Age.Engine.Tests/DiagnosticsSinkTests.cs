using System;
using System.Collections.Generic;
using System.IO;
using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class DiagnosticsSinkTests
{
    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);

    [Fact]
    public void OpcodeTable_ByLabel_ResolvesMnemonicToOpcode()
    {
        var t = Table();
        Assert.Equal(0xc8, t.ByLabel("sleep"));
        Assert.Equal(0x55, t.ByLabel("mov"));
        Assert.Null(t.ByLabel("no-such-op"));
    }

    [Fact]
    public void Histogram_CountsOps_AndAttributesCallSitesToTheRightScript()
    {
        var t = Table();
        // callee SUB: mov g[0x20]=99 ; exit.   caller MAIN: mov g[0x10]=1 ; call-script 5 ; exit.
        var sub = ScriptAssembler.Assemble(t, "SUB",
            new List<(int, Operand[])> { (0x55, new[] { new Operand(3, 0x20), new Operand(0, 99) }),
                                         (0x2, Array.Empty<Operand>()) }, Array.Empty<string>());
        var main = ScriptAssembler.Assemble(t, "MAIN",
            new List<(int, Operand[])> { (0x55, new[] { new Operand(3, 0x10), new Operand(0, 1) }),
                                         (0x3, new[] { new Operand(0, 5) }),
                                         (0x2, Array.Empty<Operand>()) }, Array.Empty<string>());

        var hist = new HistogramTraceSink();
        new VirtualMachine(main, t, new RecordingHost(), null, new MapProvider(new() { [5] = sub }), hist).Run();

        Assert.Equal(2, hist.OpCount(0x55));   // one mov in each frame
        Assert.Equal(2, hist.OpCount(0x2));    // one exit in each frame

        var sw = new StringWriter();
        hist.WriteReport(sw, t);
        var report = sw.ToString();
        // Same pc (0) in two scripts must be two distinct call-sites, each tagged with its script + arg0.
        Assert.Contains("MAIN:0x0  mov  arg0=16", report);   // 0x10
        Assert.Contains("SUB:0x0  mov  arg0=32", report);    // 0x20
    }

    [Fact]
    public void TextSink_OpFilter_ShowsOnlyFilteredOps_TaggedWithScript()
    {
        var t = Table();
        var scene = ScriptAssembler.Assemble(t, "F",
            new List<(int, Operand[])> { (0x55, new[] { new Operand(3, 0x10), new Operand(0, 1) }),  // mov
                                         (0xc8, new[] { new Operand(0, 200) }),                        // sleep
                                         (0x2, Array.Empty<Operand>()) }, Array.Empty<string>());
        var sw = new StringWriter();
        var filter = new HashSet<int> { 0xc8 };
        var sink = new TextTraceSink(sw, t, includeSteps: false, opFilter: filter);
        new VirtualMachine(scene, t, new RecordingHost(), null, null, sink).Run();

        var outp = sw.ToString();
        Assert.Contains("F:0001 sleep", outp);   // filtered op appears, tagged with its script + pc
        Assert.DoesNotContain(" mov ", outp);    // non-filtered op is suppressed
    }

    [Fact]
    public void Composite_FansOut_AndTracingStepsIsAnyChild()
    {
        var a = new RecordingTraceSink { TracingSteps = false };
        var b = new RecordingTraceSink { TracingSteps = true };
        var c = new CompositeTraceSink(a, b);
        Assert.True(c.TracingSteps);   // any child needs steps

        c.Emit(TraceEvent.Halt("x", 3));
        Assert.Single(a.Events);
        Assert.Single(b.Events);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

/// <summary>The VM side of the differential offset-path oracle: JsonOffsetTraceSink records every executed
/// instruction offset (word index) of one target script, in order, excluding call-script subroutines.</summary>
public class JsonOffsetTraceSinkTests
{
    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);

    [Fact]
    public void RecordsExecutedOffsetsInOrder()
    {
        var t = Table();
        // mov g[0x10]=7 (op 0x55, argc2 -> 5 words) ; exit (op 0x2, argc0) => offsets [0, 5].
        var body = new List<(int, Operand[])>
        {
            (0x55, new[] { new Operand(3, 0x10), new Operand(0, 7) }),
            (0x2, Array.Empty<Operand>()),
        };
        var s = ScriptAssembler.Assemble(t, "S.BIN", body, Array.Empty<string>());
        var sink = new JsonOffsetTraceSink("S.BIN");
        new VirtualMachine(s, t, new RecordingHost(), null, null, sink).Run();
        Assert.Equal(new[] { 0, 5 }, sink.Offsets);
    }

    [Fact]
    public void FiltersToTargetFrameExcludingCallScriptSubroutine()
    {
        var t = Table();
        // callee: mov ; exit.   caller: call-script 5 (op 0x3, argc1 -> 3 words) ; exit  => caller offsets [0, 3].
        var callee = ScriptAssembler.Assemble(t, "CALLEE.BIN",
            new List<(int, Operand[])>
            {
                (0x55, new[] { new Operand(3, 0x11), new Operand(0, 1) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var caller = ScriptAssembler.Assemble(t, "CALLER.BIN",
            new List<(int, Operand[])> { (0x3, new[] { new Operand(0, 5) }), (0x2, Array.Empty<Operand>()) },
            Array.Empty<string>());

        var sink = new JsonOffsetTraceSink("CALLER.BIN");
        var vm = new VirtualMachine(caller, t, new RecordingHost(), null,
                                    new MapProvider(new() { [5] = callee }), sink);
        vm.Run();

        Assert.Equal(1, vm.CallScriptDispatches);                  // the subroutine really ran
        Assert.Equal(new[] { 0, 3 }, sink.Offsets);                // ...but its offsets are excluded
    }
}

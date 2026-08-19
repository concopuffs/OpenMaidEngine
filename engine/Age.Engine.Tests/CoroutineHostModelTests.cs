using System.Collections.Generic;
using System.Linq;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class CoroutineHostModelTests
{
    private const int T_IMM = 0, T_STR = 2, T_GINT = 3, T_LINT = 9;
    private const int SceneEntryGate = 0xaba5c;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly VmCompatibilityContext HimegariCompatibility =
        new("himegari", "AGE-catalog", SceneEntryCoroutineGateAddress: SceneEntryGate);

    private static int Op(string label) => Table.ByLabel(label)!.Value;
    private static Instruction Ins(int offset, int opcode, params Operand[] args) => new(offset, opcode, args);

    private static Script Script(params Instruction[] instructions) => new()
    {
        Name = "COROUTINE-TEST",
        Header = new ScriptHeader(0, 0, 0, 0, 0, 0),
        Instructions = instructions,
        IndexByOffset = instructions.Select((ins, i) => (ins.Offset, i)).ToDictionary(x => x.Offset, x => x.i),
        Strings = new Dictionary<int, string> { [0x1000] = "LABEL", [0x1001] = "J" },
    };

    [Fact]
    public void AdvLabeledYieldRunsSetupExactlyOnceThenUsesStructuralTerminal()
    {
        const int output = 0x6be, terminal = 0x6c3, setupCount = 0x7000, slot = 0x7001, observed = 0x7002;
        var script = Script(
            Ins(0x00, Op("eq"), new(T_LINT, 0), new(T_GINT, SceneEntryGate), new(T_IMM, 1)),
            Ins(0x07, Op("jcc"), new(T_LINT, 0), new(T_IMM, 0x20), new(T_IMM, 0x80)),
            Ins(0x20, 0x140, new(T_GINT, output), new(T_STR, 0x1000), new(T_STR, 0x1001), new(T_GINT, output)),
            Ins(0x29, Op("mov"), new(T_GINT, terminal), new(T_IMM, 0x77)),
            Ins(0x2e, Op("eq"), new(T_LINT, 1), new(T_GINT, terminal), new(T_GINT, output)),
            Ins(0x35, Op("jcc"), new(T_LINT, 1), new(T_IMM, 0x60), new(T_IMM, 0x40)),
            Ins(0x40, Op("add"), new(T_GINT, setupCount), new(T_GINT, setupCount), new(T_IMM, 1)),
            Ins(0x47, Op("mov"), new(T_GINT, slot), new(T_IMM, 4)),
            Ins(0x4c, Op("jmp"), new Operand(T_IMM, 0x20)),
            Ins(0x60, Op("mov"), new(T_GINT, SceneEntryGate), new(T_IMM, 0)),
            Ins(0x65, Op("jmp"), new Operand(T_IMM, 0x80)),
            Ins(0x80, Op("mov"), new(T_GINT, observed), new(T_GINT, slot)),
            Ins(0x85, Op("exit")));

        var sink = new RecordingTraceSink { TracingSteps = true };
        var vm = new VirtualMachine(
            script, Table, new CaptureHost(), sink: sink,
            compatibility: HimegariCompatibility);
        vm.Globals[output] = 0x77; // stale value from a previous scene: already equal to this scene's terminal
        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[setupCount]);
        Assert.Equal(4, vm.Globals[observed]);
        Assert.Equal(0, vm.Globals[SceneEntryGate]);
        Assert.Equal(0x77, vm.Globals[output]);
        Assert.Equal(2, sink.Events.Count(e => e.Kind == TraceEventKind.Step && e.Opcode == 0x140));
    }

    [Fact]
    public void NonAdvLabeledServiceIsLeftStubbedAndDoesNotInjectSceneEntryGate()
    {
        var script = new Script
        {
            Name = "NON-ADV-140",
            Header = new ScriptHeader(0, 0, 0, 0, 0, 0),
            Instructions = new[]
            {
                Ins(0, 0x140, new(T_GINT, 0x699), new(T_STR, 0x2000), new(T_STR, 0x2001), new(T_GINT, 0x699)),
                Ins(9, Op("exit")),
            },
            IndexByOffset = new Dictionary<int, int> { [0] = 0, [9] = 1 },
            Strings = new Dictionary<int, string> { [0x2000] = "BIN", [0x2001] = "SC????.BIN" },
        };
        var sink = new RecordingTraceSink { TracingSteps = true };
        var vm = new VirtualMachine(script, Table, new CaptureHost(), sink: sink);
        vm.Globals[0x699] = 123;
        vm.Run();

        Assert.Equal(123, vm.Globals[0x699]);
        Assert.False(vm.Globals.ContainsKey(SceneEntryGate));
        Assert.Contains(sink.Events, e => e.Kind == TraceEventKind.Stub && e.Opcode == 0x140);
    }

    [Fact]
    public void CoroutineHandlerOpsAreConsumedByTheHostSchedulerModel()
    {
        var script = Script(
            Ins(0, 0x7b, new(T_IMM, 0x30), new(T_IMM, 0x40)),
            Ins(5, 0x7c),
            Ins(6, Op("exit")));
        var sink = new RecordingTraceSink { TracingSteps = true };
        var vm = new VirtualMachine(script, Table, new CaptureHost(), sink: sink);
        vm.Run();

        Assert.DoesNotContain(sink.Events, e => e.Kind == TraceEventKind.Stub && (e.Opcode == 0x7b || e.Opcode == 0x7c));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Sc0000EntryRunsSetupAndFillsDistinctTextureSlots()
    {
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], Table);
        var sink = new RecordingTraceSink { TracingSteps = true };
        var vm = new VirtualMachine(script, Table, new CaptureHost(),
            new VmOptions(HaltAtWaitForInput: true), sink: sink,
            compatibility: HimegariCompatibility);
        vm.Run();

        Assert.Equal("wait-for-input", vm.HaltReason);
        Assert.Equal(0, vm.Globals[SceneEntryGate]);
        Assert.Equal(new long[] { 4, 5, 6, 7, 8, 9, 10, 11 },
            Enumerable.Range(0, 8).Select(i => vm.Globals[0x3239 + i * 3]).ToArray());
        Assert.Equal(2, sink.Events.Count(e => e.Kind == TraceEventKind.Step && e.Opcode == 0x140));
    }
}

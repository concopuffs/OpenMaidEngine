using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class RootReloadTests
{
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);

    [Fact]
    public void ExitScriptDiscardsEveryCallerAndReloadsRawScriptZero()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var system4 = ScriptAssembler.Assemble(table, "SYSTEM4", new List<(int, Operand[])>
        {
            (0x1b6, new[] { G(0x220) }),
            (0x19a, new[] { G(0x221) }),
            (0x55, new[] { G(0x202), I(0x99) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var deepest = ScriptAssembler.Assemble(table, "DEEPEST", new List<(int, Operand[])>
        {
            (0x55, new[] { G(0x203), I(3) }),
            (0x9, Array.Empty<Operand>()),
            (0x55, new[] { G(0x204), I(4) }),
        }, Array.Empty<string>());
        var child = ScriptAssembler.Assemble(table, "CHILD", new List<(int, Operand[])>
        {
            (0x3, new[] { I(2) }),
            (0x55, new[] { G(0x205), I(5) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var initial = ScriptAssembler.Assemble(table, "INITIAL", new List<(int, Operand[])>
        {
            (0x1b7, new[] { I(1) }),
            (0x88, new[] { I(1) }),
            (0x55, new[] { G(0x200), I(1) }),
            (0x3, new[] { I(1) }),
            (0x55, new[] { G(0x201), I(2) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var provider = new MapProvider(new Dictionary<long, Script>
        {
            [0] = system4,
            [1] = child,
            [2] = deepest,
        });
        var host = new RecordingHost();
        var trace = new RecordingTraceSink();
        var vm = new VirtualMachine(initial, table, host, provider: provider, sink: trace);
        vm.Globals[0x2ff] = 0x1234;
        vm.Gfx.SetSurface(5, 0x33, 0);
        vm.Gfx.BindDraw(0x100, 5, 0, 0, 1, 1, 0, 0);
        vm.TextHistory.DefineLayout(1, 10, 10, 0, 0);
        vm.TextHistory.AppendText(1, 0, "preserved", AdvTextStyle.Default);
        vm.TextHistory.SetRecordingEnabled(false);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0x99, vm.Globals[0x202]);
        Assert.False(vm.Globals.ContainsKey(0x201));
        Assert.False(vm.Globals.ContainsKey(0x204));
        Assert.False(vm.Globals.ContainsKey(0x205));
        Assert.Equal(0, vm.Globals[0x220]);
        Assert.Equal(0, vm.Globals[0x221]);
        Assert.Empty(vm.Gfx.SnapshotVisibleObjects());
        Assert.Single(vm.TextHistory.Records, r => r.Text == "preserved");
        Assert.False(vm.TextHistory.RecordingSuppressed);
        Assert.Equal(1, host.SceneContextResets);
        Assert.False(host.MessageSkip);

        var rootEnter = Assert.Single(trace.Events,
            e => e.Kind == TraceEventKind.FrameEnter && e.Name == "SYSTEM4");
        Assert.Equal(FrameCause.RootReload, rootEnter.Cause);
        Assert.Equal(3, trace.Events.Count(e =>
            e.Kind == TraceEventKind.FrameExit && e.Text == "RootReload"));
    }

    [Theory]
    [InlineData(2, 0, false)]   // Himegari: no AUTOFREETEX
    [InlineData(2, 3, true)]    // Kamidori
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]   // bit 1 releases at a numbered load only
    [InlineData(1, 3, false)]   // without the object backend's bit
    public void ExitScriptReleasesSurfacesOnlyWhenAutoFreeTexAsks(int createObject, int autoFreeTex, bool released)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var initial = ScriptAssembler.Assemble(table, "INITIAL", new List<(int, Operand[])>
        {
            (0x9, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var system4 = ScriptAssembler.Assemble(table, "SYSTEM4", new List<(int, Operand[])>
        {
            (0x216, new[] { G(0x211), I(5) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(initial, table, host,
            new VmOptions(CreateObject: createObject, AutoFreeTex: autoFreeTex),
            provider: new MapProvider(new Dictionary<long, Script> { [0] = system4 }));
        vm.Gfx.SetSurface(5, 0x23, 0);
        vm.Gfx.CreateSurface(21);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(-1, vm.Globals[0x211]);   // the reset clears the record either way
        Assert.Equal(1, host.SceneContextResets);
        Assert.Equal(released ? new[] { (0, 1000) } : Array.Empty<(int, int)>(), host.ReleasedSurfaceRanges);
        Assert.Equal(
            released ? Array.Empty<int>() : new[] { 5, 21 },
            vm.Gfx.CapturePersistenceSnapshot().Surfaces.Select(item => item.Slot));
    }

    /// <summary>op_0x9_reset_scene_and_reload_root zeroes every global bank before resource 0 runs. A Kamidori
    /// game over exits with FIELD's flag 0x428c still set; native's INIT2 then reads zero and draws no field objects
    /// over the title.</summary>
    [Fact]
    public void ExitScriptZeroesEveryGlobalBankBeforeTheRootRuns()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var initial = ScriptAssembler.Assemble(table, "INITIAL", new List<(int, Operand[])>
        {
            (0x55, new[] { G(0x428c), I(1) }),
            (0x9, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var system4 = ScriptAssembler.Assemble(table, "SYSTEM4", new List<(int, Operand[])>
        {
            (0x55, new[] { G(0x300), G(0x428c) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(initial, table, new RecordingHost(),
            provider: new MapProvider(new Dictionary<long, Script> { [0] = system4 }));
        vm.Globals[0x2ff] = 0x1234;
        vm.GlobalFloats[1] = BitConverter.SingleToInt32Bits(2.5f);
        vm.GlobalStrings[2] = "残留";
        vm.GlobalPointers[3] = 0x2ff;
        vm.GlobalStringPointers[4] = 2;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new KeyValuePair<int, long>(0x300, 0), Assert.Single(vm.Globals));
        Assert.Empty(vm.GlobalFloats);
        Assert.Empty(vm.GlobalStrings);
        Assert.Empty(vm.GlobalPointers);
        Assert.Empty(vm.GlobalStringPointers);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void HimegariRawScriptZeroIsSystem4()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scripts = Sys4ScriptProvider.Load(table);

        Assert.Equal("SYSTEM4.BIN", scripts.GetById(0)?.Name);
    }

    [Fact]
    public void ExitScriptResetsSceneBeforeAnUnresolvedRootLoadFails()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "NO_ROOT", new List<(int, Operand[])>
        {
            (0x9, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Gfx.SetSurface(5, 0x33, 0);
        vm.Gfx.BindDraw(0x100, 5, 0, 0, 1, 1, 0, 0);

        vm.Run();

        Assert.Equal("root-reload-unresolved:0x0", vm.HaltReason);
        Assert.Equal(1, host.SceneContextResets);
        Assert.Empty(vm.Gfx.SnapshotVisibleObjects());
    }
}

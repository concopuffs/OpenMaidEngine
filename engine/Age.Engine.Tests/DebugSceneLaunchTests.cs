using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class DebugSceneLaunchTests
{
    private sealed class ReachedDebugMapPage8ContinuationException : Exception { }

    private sealed class LaunchDebugMapAndStopAfterPage8Sink : ITraceSink
    {
        public VirtualMachine Vm = null!;
        public bool TracingSteps => true;

        public void Emit(in TraceEvent e)
        {
            if (e.Kind == TraceEventKind.FrameEnter
                && e.Name?.Equals("TITLE.BIN", StringComparison.OrdinalIgnoreCase) == true)
            {
                DebugFrameSnapshot frame = Assert.IsType<DebugFrameSnapshot>(Vm.DebugFrame);
                Assert.True(Vm.TryRequestDebugFrameReturn(frame.FrameId, new Dictionary<int, long>
                {
                    [0] = 1,
                    [0xaba5c] = -1,
                    [0x62ccf] = 0,
                    [0x699] = 0x338c,
                }));
            }

            if (e.Kind == TraceEventKind.Step
                && e.Ins?.Offset == 0x5b25
                && Vm.DebugFrame?.CurrentScript.Equals(
                    "SC0270.BIN", StringComparison.OrdinalIgnoreCase) == true)
                throw new ReachedDebugMapPage8ContinuationException();
        }
    }

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
    private static (int, Operand[]) Call(Operand id) => (0x3, new[] { id });
    private static (int, Operand[]) Mov(int address, long value) => (0x55, new[] { G(address), I(value) });
    private static (int, Operand[]) Wait() => (0x72, new[] { I(0) });
    private static (int, Operand[]) Sleep() => (0xc8, new[] { I(1) });
    private static (int, Operand[]) Exit() => (0x2, Array.Empty<Operand>());

    [Fact]
    [Trait("Category", "Workspace")]
    public void RealDebugMapLaunchContinuesPastSc0270Page8Wait()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scripts = Sys4ScriptProvider.Load(table);
        var sink = new LaunchDebugMapAndStopAfterPage8Sink();
        var vm = new VirtualMachine(
            scripts.RequireByName("SYSTEM4.BIN"), table, new CaptureHost(),
            new VmOptions(MaxSteps: 1_000_000), scripts, sink);
        sink.Vm = vm;

        Exception? exception = Record.Exception(() => vm.Run());

        Assert.IsType<ReachedDebugMapPage8ContinuationException>(exception);
        Assert.Null(vm.HaltReason);
    }

    [Fact]
    public async Task ParkedTitleFrameReturnsToCoordinatorWhichDispatchesSelectedScript()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var root = ScriptAssembler.Assemble(table, "SYSTEM4.BIN", new List<(int, Operand[])>
        {
            Call(I(1)), Call(G(0x699)), Mov(0x7102, 1), Exit(),
        }, Array.Empty<string>());
        var title = ScriptAssembler.Assemble(table, "TITLE.BIN", new List<(int, Operand[])>
        {
            Wait(), Mov(0x7100, 1), Exit(),
        }, Array.Empty<string>());
        var selected = ScriptAssembler.Assemble(table, "DEBUG.BIN", new List<(int, Operand[])>
        {
            Mov(0x7101, 1), Exit(),
        }, Array.Empty<string>());
        var host = new BlockingWaitHost();
        var trace = new RecordingTraceSink();
        var vm = new VirtualMachine(root, table, host, provider: new MapProvider(new()
        {
            [1] = title,
            [2] = selected,
        }), sink: trace);

        Assert.False(vm.TryRequestDebugFrameReturn(0, new Dictionary<int, long>()));
        Task run = Task.Run(() => vm.Run());
        await host.WaitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var parked = Assert.IsType<DebugFrameSnapshot>(vm.DebugFrame);
        Assert.Equal("TITLE.BIN", parked.CurrentScript);
        Assert.Equal(new[] { "SYSTEM4.BIN", "TITLE.BIN" }, parked.CallStack);
        Assert.True(vm.TryRequestDebugFrameReturn(parked.FrameId, new Dictionary<int, long>
        {
            [0] = 1,
            [0xaba5c] = -1,
            [0x62ccf] = 0,
            [0x699] = 2,
        }));
        Assert.False(vm.TryRequestDebugFrameReturn(parked.FrameId, new Dictionary<int, long>()));
        host.SignalInput();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x7100));
        Assert.Equal(1, vm.Globals[0x7101]);
        Assert.Equal(1, vm.Globals[0x7102]);
        Assert.Null(vm.DebugFrame);
        Assert.Contains(trace.Events, e => e.Kind == TraceEventKind.FrameExit
                                           && e.Name == "TITLE.BIN" && e.Text == "DebugReturned");
        Assert.Contains(trace.Events, e => e.Kind == TraceEventKind.FrameEnter
                                           && e.Name == "DEBUG.BIN" && e.Cause == FrameCause.CallScript);
    }

    [Fact]
    public async Task SelectedScriptExitScriptStillPerformsWholeStackRootReload()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var initialRoot = ScriptAssembler.Assemble(table, "SYSTEM4.BIN", new List<(int, Operand[])>
        {
            Call(I(1)), Call(G(0x699)), Mov(0x7200, 1), Exit(),
        }, Array.Empty<string>());
        var title = ScriptAssembler.Assemble(table, "TITLE.BIN", new List<(int, Operand[])>
        {
            Wait(), Exit(),
        }, Array.Empty<string>());
        var selected = ScriptAssembler.Assemble(table, "DEBUG.BIN", new List<(int, Operand[])>
        {
            (0x9, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var reloadedRoot = ScriptAssembler.Assemble(table, "SYSTEM4.BIN", new List<(int, Operand[])>
        {
            Mov(0x7201, 1), Exit(),
        }, Array.Empty<string>());
        var host = new BlockingWaitHost();
        var trace = new RecordingTraceSink();
        var vm = new VirtualMachine(initialRoot, table, host, provider: new MapProvider(new()
        {
            [0] = reloadedRoot,
            [1] = title,
            [2] = selected,
        }), sink: trace);

        Task run = Task.Run(() => vm.Run());
        await host.WaitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var frame = Assert.IsType<DebugFrameSnapshot>(vm.DebugFrame);
        Assert.True(vm.TryRequestDebugFrameReturn(frame.FrameId,
            new Dictionary<int, long> { [0x699] = 2 }));
        host.SignalInput();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x7200));
        Assert.Equal(1, vm.Globals[0x7201]);
        Assert.Equal(1, host.SceneContextResets);
        Assert.Contains(trace.Events, e => e.Kind == TraceEventKind.FrameEnter
                                           && e.Name == "SYSTEM4.BIN" && e.Cause == FrameCause.RootReload);
    }

    [Fact]
    public async Task PollingTitleReturnsAtOpcodeBoundaryWithoutAdvInputWait()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var root = ScriptAssembler.Assemble(table, "SYSTEM4.BIN", new List<(int, Operand[])>
        {
            Call(I(1)), Call(G(0x699)), Mov(0x7302, 1), Exit(),
        }, Array.Empty<string>());
        var title = ScriptAssembler.Assemble(table, "TITLE.BIN", new List<(int, Operand[])>
        {
            Sleep(), Mov(0x7300, 1), Exit(),
        }, Array.Empty<string>());
        var selected = ScriptAssembler.Assemble(table, "DEBUG.BIN", new List<(int, Operand[])>
        {
            Mov(0x7301, 1), Exit(),
        }, Array.Empty<string>());
        var host = new BlockingSleepHost();
        var vm = new VirtualMachine(root, table, host, provider: new MapProvider(new()
        {
            [1] = title,
            [2] = selected,
        }));

        Task run = Task.Run(() => vm.Run());
        await host.SleepEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var frame = Assert.IsType<DebugFrameSnapshot>(vm.DebugFrame);
        Assert.Equal(new[] { "SYSTEM4.BIN", "TITLE.BIN" }, frame.CallStack);
        Assert.True(vm.TryRequestDebugFrameReturn(frame.FrameId,
            new Dictionary<int, long> { [0x699] = 2 }));
        host.CompleteSleep();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x7300));
        Assert.Equal(1, vm.Globals[0x7301]);
        Assert.Equal(1, vm.Globals[0x7302]);
    }

    private sealed class BlockingWaitHost : RecordingHost
    {
        private readonly SemaphoreSlim _gate = new(0, 1);
        public TaskCompletionSource WaitEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                                          Func<AdvAutoWaitState> autoWaitState)
        {
            Waits++;
            WaitEntered.TrySetResult();
            if (!_gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("test input wait timed out");
        }

        public void SignalInput() => _gate.Release();
    }

    private sealed class BlockingSleepHost : RecordingHost
    {
        private readonly SemaphoreSlim _gate = new(0, 1);
        public TaskCompletionSource SleepEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Sleep(long duration)
        {
            SleptDurations.Add(duration);
            SleepEntered.TrySetResult();
            if (!_gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("test sleep timed out");
        }

        public void CompleteSleep() => _gate.Release();
    }
}

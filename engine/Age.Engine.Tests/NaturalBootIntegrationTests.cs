using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Age.Engine.Profiles;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

[Trait("Category", "Workspace")]
public class NaturalBootIntegrationTests
{
    private sealed class ReachedSc0000Exception : Exception { }
    private sealed class ReachedKamidoriAdvPageException(
        string scriptName, string text, int visibleObjectCount, int audioRequestCount) : Exception
    {
        public string ScriptName { get; } = scriptName;
        public string Text { get; } = text;
        public int VisibleObjectCount { get; } = visibleObjectCount;
        public int AudioRequestCount { get; } = audioRequestCount;
    }
    private sealed class ReachedChmenuRosterException : Exception { }

    private sealed record NaturalBootResult(
        OpcodeTable Table, Sys4ScriptProvider Scripts, VirtualMachine Vm,
        NewGameInputHost Host, StopAtSc0000Sink Sink);

    private sealed class StopAtSc0000Sink : ITraceSink
    {
        public readonly List<string> Entered = new();
        public bool SawStringEqualsStub;
        public bool SawUnitDataCopyStub;
        public Action<string>? OnEnter;
        public bool TracingSteps => true;

        public void Emit(in TraceEvent e)
        {
            if (e.Kind == TraceEventKind.Stub && e.Opcode == 0x194)
                SawStringEqualsStub = true;
            if (e.Kind == TraceEventKind.Stub && e.Opcode is 0x63 or 0x1b0)
                SawUnitDataCopyStub = true;
            if (e.Kind == TraceEventKind.FrameEnter && e.Name != null)
            {
                Entered.Add(e.Name);
                OnEnter?.Invoke(e.Name);
                if (e.Name.Equals("SC0000.BIN", StringComparison.OrdinalIgnoreCase))
                    throw new ReachedSc0000Exception();
            }
        }
    }

    private sealed class StopAfterChmenuRosterSink : ITraceSink
    {
        public VirtualMachine Vm = null!;
        public bool SawIndexSort;
        public bool SawIndexSortFallback;
        public bool TracingSteps => true;

        public void Emit(in TraceEvent e)
        {
            if (e.Kind == TraceEventKind.Stub && e.Opcode == 0x12f)
                SawIndexSortFallback = true;
            if (e.Kind != TraceEventKind.Step || e.Ins == null) return;
            if (e.Opcode == 0x12f) SawIndexSort = true;

            // The step event is emitted before execution. At CHMENU@0x1dbd, the first sort has
            // completed and 0x1db8 has copied the chosen roster slot back to G[0x6718].
            if (e.Ins.Offset == 0x1dbd
                && string.Equals(Vm.DebugFrame?.CurrentScript, "CHMENU.BIN",
                                 StringComparison.OrdinalIgnoreCase))
                throw new ReachedChmenuRosterException();
        }
    }

    private sealed class KamidoriNaturalBootSink : ITraceSink
    {
        public readonly List<string> Entered = new();
        public Action<string>? OnEnter;
        public bool TracingSteps => true;

        public void Emit(in TraceEvent e)
        {
            if (e.Kind != TraceEventKind.FrameEnter || e.Name == null) return;
            Entered.Add(e.Name);
            OnEnter?.Invoke(e.Name);
        }
    }

    private sealed class NewGameInputHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        private readonly int _titlePointerX;
        private readonly int _titlePointerY;
        private long _now;
        public int TitlePollSleeps;
        private bool _inGameStart;
        private bool _stopAtKamidoriAdvPage;
        private int _kamidoriAdvLineBaseline;
        private int _kamidoriAdvAudioBaseline;
        private int _gameStartPollSleeps;
        public override long InputClockMilliseconds => _now;

        public NewGameInputHost(int titlePointerX = 400, int titlePointerY = 300)
        {
            _titlePointerX = titlePointerX;
            _titlePointerY = titlePointerY;
        }

        public void BeginGameStart()
        {
            _inGameStart = true;
            _gameStartPollSleeps = 0;
            Vm.UpdateMouseButtonState(0x1, false);
            Vm.UpdateInputCallbackState(4, false);
            Vm.QueueInputCallback(10);
        }

        public void StopAtKamidoriAdvPage()
        {
            _stopAtKamidoriAdvPage = true;
            _kamidoriAdvLineBaseline = Lines.Count;
            _kamidoriAdvAudioBaseline = AudioRequestCount;
        }

        private int AudioRequestCount => BgmTracks.Count + BgmRestartRequests.Count
            + Voices.Count + SfxLoads.Count;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            if (_stopAtKamidoriAdvPage && Lines.Count > _kamidoriAdvLineBaseline)
                throw new ReachedKamidoriAdvPageException(
                    "SC0000.BIN", Lines[^1].Text,
                    Vm.Gfx.SnapshotVisibleObjects().Count,
                    AudioRequestCount - _kamidoriAdvAudioBaseline);
            base.WaitForInput(layoutSlot, serviceInputCallback);
        }

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            _now += Math.Max(1, duration);
            if (duration > 1) return;

            // TITLE's first menu entry is Game Start. Hold the pointer over its native 800x600
            // rectangle, then provide one complete primary-button edge to its raw input callback.
            int polls = _inGameStart ? ++_gameStartPollSleeps : ++TitlePollSleeps;
            int cycle = polls % 200;
            if (_inGameStart && polls <= 200) return;
            if (cycle == 1)
            {
                int input = _inGameStart && polls <= 400 ? 0 : 4;
                if (!_inGameStart)
                {
                    Vm.UpdatePointer(_titlePointerX, _titlePointerY);
                    Vm.UpdateMouseButtonState(0x1, true);
                }
                Vm.UpdateInputCallbackState(input, true);
            }
            // TITLE polls its registered mouse callback every 50 ms and activates on the release edge.
            // Keep the button down through one callback, then release it before the next.
            else if (cycle == 120)
            {
                int input = _inGameStart && polls <= 400 ? 0 : 4;
                if (!_inGameStart) Vm.UpdateMouseButtonState(0x1, false);
                Vm.UpdateInputCallbackState(input, false);
                Vm.QueueInputCallback(10); // native/main-thread release callback unlocks the menu input gate
            }
        }
    }

    private static NaturalBootResult RunNaturalNewGameToSc0000()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
        var scripts = Sys4ScriptProvider.Load(table);
        var host = new NewGameInputHost();
        var sink = new StopAtSc0000Sink();
        GameProfileManifest profile = GameProfileRegistry.BuiltIn.Find("himegari")!;
        var vm = new VirtualMachine(scripts.RequireByName("SYSTEM4.BIN"), table, host,
            new VmOptions(MaxSteps: 5_000_000), scripts, sink,
            compatibility: new VmCompatibilityContext(
                profile.Id, profile.EngineAbiId,
                SceneEntryCoroutineGateAddress: profile.Runtime.SceneEntryCoroutineGateAddress));
        host.Vm = vm;
        sink.OnEnter = name =>
        {
            if (name.Equals("GAMESTART.BIN", StringComparison.OrdinalIgnoreCase)) host.BeginGameStart();
        };

        var exception = Record.Exception(() => vm.Run());
        Assert.True(exception is ReachedSc0000Exception,
            $"halt={vm.HaltReason}; title_sleeps={host.TitlePollSleeps}; entered={string.Join(",", sink.Entered)}");
        return new NaturalBootResult(table, scripts, vm, host, sink);
    }

    [Fact]
    public void System4Root_NewGameSelectionNaturallyCallsSc0000()
    {
        var boot = RunNaturalNewGameToSc0000();
        var vm = boot.Vm;
        var host = boot.Host;
        var sink = boot.Sink;

        Assert.Equal(new[]
        {
            "SYSTEM4.BIN", "INITCONFIG.BIN", "INIT2.BIN",
        }, sink.Entered.Take(3));
        int logo = sink.Entered.IndexOf("LOGO.BIN");
        int opening = sink.Entered.IndexOf("OP.BIN");
        int init = sink.Entered.IndexOf("INIT.BIN");
        int title = sink.Entered.IndexOf("TITLE.BIN");
        Assert.True(logo >= 0 && logo < opening && opening < init && init < title,
            $"entered={string.Join(",", sink.Entered)}");
        Assert.Equal(new[] { (0x335fL, 42, 4L), (0x3364L, 42, 4L) }, host.ModalMovies);
        Assert.Contains("TITLE.BIN", sink.Entered);
        Assert.Contains("GAMESTART.BIN", sink.Entered);
        Assert.Contains("UNITECH.BIN", sink.Entered);
        Assert.Contains("CALCARR.BIN", sink.Entered);
        Assert.Equal("SC0000.BIN", sink.Entered[^1]);
        int baseBtanInit2 = sink.Entered.IndexOf("BTANINIT2.BIN");
        int appendAutorun = sink.Entered.IndexOf("$1$AUTORUN.BIN");
        int appendEbInit = sink.Entered.IndexOf("$1$EBINIT.BIN");
        int tune = sink.Entered.IndexOf("TUNE.BIN");
        Assert.Equal(new[] { 1 }, boot.Scripts.MountedAppendSelectors);
        Assert.True(baseBtanInit2 >= 0 && baseBtanInit2 < appendAutorun
            && appendAutorun < appendEbInit && appendEbInit < tune,
            $"entered={string.Join(",", sink.Entered)}");
        Assert.Equal(40, vm.Globals.GetValueOrDefault(0x7a37e + 81));       // append unit starting level
        Assert.Equal(0x0100002d, vm.Globals.GetValueOrDefault(0x6fb86 + 81)); // packed battle sprite
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0));
        Assert.Equal(0x22, vm.Globals.GetValueOrDefault(0x699));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x6c1));
        Assert.False(sink.SawStringEqualsStub);
        Assert.False(sink.SawUnitDataCopyStub);
    }

    [Fact]
    [Trait("Profile", "kamidori")]
    public void KamidoriSystem4Root_NewGameSelectionNaturallyCallsFirstAdvScene()
    {
        string gameRoot = Path.Combine(Paths.Workspace, "Kamidori");
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");

        GameProfileManifest profile = GameProfileRegistry.BuiltIn.Find("kamidori")!;
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, profile.EngineAbiId);
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var scripts = new Sys4ScriptProvider(
            table, catalog, new Sys4AssetStore(catalog, gameRoot, gameRoot));
        // Kamidori's 1024x576 title menu begins at x=606; its first row spans y=286..340.
        var host = new NewGameInputHost(700, 300);
        var sink = new KamidoriNaturalBootSink();
        var vm = new VirtualMachine(
            scripts.RequireByName(profile.NaturalBootScript), table, host,
            new VmOptions(MaxSteps: 5_000_000), scripts, sink,
            compatibility: new VmCompatibilityContext(
                profile.Id, profile.EngineAbiId,
                SceneEntryCoroutineGateAddress: profile.Runtime.SceneEntryCoroutineGateAddress));
        host.Vm = vm;
        sink.OnEnter = name =>
        {
            if (name.Equals("GAMESTART.BIN", StringComparison.OrdinalIgnoreCase)) host.BeginGameStart();
            if (name.Equals("SC0000.BIN", StringComparison.OrdinalIgnoreCase)) host.StopAtKamidoriAdvPage();
        };

        Exception? exception = Record.Exception(() => vm.Run());
        Assert.True(exception is ReachedKamidoriAdvPageException,
            $"exception={exception?.GetType().Name ?? "none"}; halt={vm.HaltReason}; "
            + $"title_sleeps={host.TitlePollSleeps}; entered={string.Join(",", sink.Entered)}");
        var reachedAdv = Assert.IsType<ReachedKamidoriAdvPageException>(exception);

        Assert.Null(vm.CompatibilityFailure);
        Assert.Contains("TITLE.BIN", sink.Entered);
        Assert.Contains("GAMESTART.BIN", sink.Entered);
        Assert.Equal("SC0000.BIN", reachedAdv.ScriptName);
        Assert.NotEmpty(reachedAdv.Text);
        Assert.True(reachedAdv.VisibleObjectCount > 0);
        Assert.True(reachedAdv.AudioRequestCount > 0);
        Assert.Equal(reachedAdv.ScriptName, sink.Entered[^1]);
    }

    [Fact]
    public void NaturalNewGamePartyStateSurvivesRealChmenuRosterSort()
    {
        var boot = RunNaturalNewGameToSc0000();
        Assert.Equal(2, boot.Vm.Globals.GetValueOrDefault(0x6718));
        Assert.Equal(0x13, boot.Vm.Globals.GetValueOrDefault(0x673c + 2));
        Assert.Equal(2, boot.Vm.Globals.GetValueOrDefault(0x67a0 + 2));

        var sink = new StopAfterChmenuRosterSink();
        var menuVm = new VirtualMachine(boot.Scripts.RequireByName("CHMENU.BIN"), boot.Table,
            new RecordingHost(), new VmOptions(MaxSteps: 5_000_000), boot.Scripts, sink);
        sink.Vm = menuVm;
        foreach (var (address, value) in boot.Vm.Globals) menuVm.Globals[address] = value;
        foreach (var (address, value) in boot.Vm.ExternalGlobals) menuVm.ExternalGlobals[address] = value;
        foreach (var (address, value) in boot.Vm.GlobalStrings) menuVm.GlobalStrings[address] = value;

        var exception = Record.Exception(() => menuVm.Run());

        Assert.IsType<ReachedChmenuRosterException>(exception);
        Assert.True(sink.SawIndexSort);
        Assert.False(sink.SawIndexSortFallback);
        Assert.Equal(2, menuVm.Globals.GetValueOrDefault(0x6718));
    }
}

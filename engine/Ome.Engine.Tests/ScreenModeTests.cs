using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Engine.Natives;
using Ome.Engine.Devices;

/// <summary>
/// The VM's part of AGE's screen mode (docs/engine-re.md, "Screen mode"): op 0x2cf asks the shell-services native for
/// a mode (op_0x2cf_handler@0x425370 passes operand 0 or 1 to display_toggle_screen_mode_and_menu_bar@0x412df0), which
/// switches at the front end's next frame, and a switch runs CALLBACK_WINDOW.BIN as the native switch launches it.
/// </summary>
public class ScreenModeTests
{
    private const int Probe = 0x200, Done = 0x300, WindowCallbackRan = 0x310, SettingCallbackRan = 0x311;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);

    /// <summary>The shell native's window: records each mode it shows.</summary>
    private sealed class RecordingWindow : IWindowDevice
    {
        public readonly List<bool> Shown = new();
        public void ShowScreenMode(bool windowed) => Shown.Add(windowed);
    }

    private sealed class ScreenModeHost : RecordingWaits
    {
        public VirtualMachine? Vm;
        public readonly Dictionary<int, Action<VirtualMachine>> AtYield = new();
        public Action<VirtualMachine>? AtWait;
        public bool ServiceFrames;   // run the front end's frame service at each yield
        public int EndedWaits;
        private int _yields;

        public override void FrameYield()
        {
            _yields++;
            if (Vm != null && AtYield.TryGetValue(_yields, out var act)) act(Vm);
            if (ServiceFrames) Vm?.Shell.ServiceFrame();
        }

        public override void WaitForInput(Func<bool> serviceInputCallback)
        {
            if (Vm != null) AtWait?.Invoke(Vm);
            base.WaitForInput(serviceInputCallback);
        }

        public override void EndInputWait() => EndedWaits++;
    }

    private static Script Build(string name, params (int Op, Operand[] Operands)[] body)
        => ScriptAssembler.Assemble(Table, name, [.. body, (0x55, new[] { G(Done), I(1) }), (0x2, Array.Empty<Operand>())],
                                    Array.Empty<string>());

    private static (int, Operand[])[] Probes(int count)
        => Enumerable.Range(0, count).Select(i => (0x1c7, new[] { G(Probe + i) })).ToArray();

    private static Script Callback(string name, int flag) => ScriptAssembler.Assemble(Table, name,
        new List<(int, Operand[])> { (0x55, new[] { G(flag), I(1) }), (0x2, Array.Empty<Operand>()) },
        Array.Empty<string>());

    private static (VirtualMachine Vm, ScreenModeHost Host) Start(Script script, RecordingWindow? window = null)
    {
        var host = new ScreenModeHost();
        var shell = new ShellServices(window: window);
        shell.WakeWaitsWith(((IInputAndWaits)host).WakeInputCallbackService);
        var provider = new MapProvider(new Dictionary<long, Script>(),
            new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase)
            {
                ["CALLBACK_WINDOW.BIN"] = Callback("CALLBACK_WINDOW", WindowCallbackRan),
                ["CALLBACK_SETTING.BIN"] = Callback("CALLBACK_SETTING", SettingCallbackRan),
            });
        var vm = new VirtualMachine(script, Table, host, new VmOptions(), provider, shell: shell);
        host.Vm = vm;
        return (vm, host);
    }

    [Fact]
    public void Op0x2cfAsksForAWindowOrFullscreenAndIgnoresOtherValues()
    {
        var window = new RecordingWindow();
        var (vm, host) = Start(Build("SCREEN_MODE_OP",
            (0x2cf, new[] { I(1) }), (0x1c7, new[] { G(Probe) }),        // already a window: nothing
            (0x2cf, new[] { I(0) }), (0x1c7, new[] { G(Probe + 1) }),    // fullscreen
            (0x2cf, new[] { I(2) }), (0x1c7, new[] { G(Probe + 2) }),    // ignored
            (0x2cf, new[] { I(-1) }), (0x1c7, new[] { G(Probe + 3) }),   // ignored
            (0x2cf, new[] { I(1) }), (0x1c7, new[] { G(Probe + 4) })), window);
        host.ServiceFrames = true;

        vm.Run();

        Assert.Equal([false, true], window.Shown);
        Assert.True(vm.Shell.Windowed);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Done));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(WindowCallbackRan));   // each switch ran CALLBACK_WINDOW.BIN
    }

    [Fact]
    public void Op0x2cfOnlyAsksTheFrontEndSwitchesAtItsNextFrame()
    {
        var window = new RecordingWindow();
        var (vm, _) = Start(Build("SCREEN_MODE_ASK", (0x2cf, new[] { I(0) })), window);

        vm.Run();

        // No frame ran: the mode, the window and CALLBACK_WINDOW.BIN wait for the front end.
        Assert.True(vm.Shell.Windowed);
        Assert.Empty(window.Shown);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(WindowCallbackRan));
        vm.Shell.ServiceFrame();
        Assert.False(vm.Shell.Windowed);
        Assert.Equal([false], window.Shown);
    }

    [Fact]
    public void ASwitchRunsCallbackWindowAtTheNextInstructionBoundary()
    {
        var (vm, host) = Start(Build("SCREEN_MODE_SWITCH", Probes(6)));
        host.AtYield[3] = v => v.Shell.SwitchScreenMode(windowed: false, "the test");

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(WindowCallbackRan));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(SettingCallbackRan));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Done));   // the callback returned to the scene
    }

    [Fact]
    public void ASwitchDuringATextWaitRunsCallbackWindowInsideTheWait()
    {
        var (vm, host) = Start(Build("SCREEN_MODE_WAIT", (0x72, new[] { I(0) })));
        host.AtWait = v => v.Shell.SwitchScreenMode(windowed: false, "the test");

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(WindowCallbackRan));
        Assert.Equal(0, host.EndedWaits);   // the page still waits for its click
    }

    [Fact]
    public void ASwitchAndAMenuToggleRunBothCallbacks()
    {
        var (vm, host) = Start(Build("SCREEN_MODE_BOTH", Probes(6)));
        host.AtYield[2] = v =>
        {
            v.Shell.RequestMessageSkipToggle();
            v.Shell.SwitchScreenMode(windowed: false, "the test");
        };

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(SettingCallbackRan));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(WindowCallbackRan));
    }

    [Fact]
    public void WithoutASwitchCallbackWindowDoesNotRun()
    {
        var (vm, _) = Start(Build("SCREEN_MODE_NONE", Probes(3)));

        vm.Run();

        Assert.Equal(0, vm.Globals.GetValueOrDefault(WindowCallbackRan));
    }
}

using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>
/// The VM's part of AGE's screen mode (docs/engine-re.md, "Screen mode"): op 0x2cf asks the frontend for a mode
/// (op_0x2cf_handler@0x425370 passes operand 0 or 1 to display_toggle_screen_mode_and_menu_bar@0x412df0), and a
/// switch the frontend performed runs CALLBACK_WINDOW.BIN as the native switch launches it.
/// </summary>
public class ScreenModeTests
{
    private const int Probe = 0x200, Done = 0x300, WindowCallbackRan = 0x310, SettingCallbackRan = 0x311;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);

    private sealed class ScreenModeHost : RecordingHost, Ome.Engine.Hosting.IHost   // re-implemented for RequestScreenMode
    {
        public VirtualMachine? Vm;
        public readonly List<bool> Requests = new();
        public readonly Dictionary<int, Action<VirtualMachine>> AtYield = new();
        public Action<VirtualMachine>? AtWait;
        public int EndedWaits;
        private int _yields;

        public void RequestScreenMode(bool windowed) => Requests.Add(windowed);

        public override void FrameYield()
        {
            _yields++;
            if (Vm != null && AtYield.TryGetValue(_yields, out var act)) act(Vm);
        }

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            if (Vm != null) AtWait?.Invoke(Vm);
            base.WaitForInput(layoutSlot, serviceInputCallback);
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

    private static (VirtualMachine Vm, ScreenModeHost Host) Start(Script script)
    {
        var host = new ScreenModeHost();
        var provider = new MapProvider(new Dictionary<long, Script>(),
            new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase)
            {
                ["CALLBACK_WINDOW.BIN"] = Callback("CALLBACK_WINDOW", WindowCallbackRan),
                ["CALLBACK_SETTING.BIN"] = Callback("CALLBACK_SETTING", SettingCallbackRan),
            });
        var vm = new VirtualMachine(script, Table, host, new VmOptions(), provider);
        host.Vm = vm;
        return (vm, host);
    }

    [Fact]
    public void Op0x2cfAsksTheHostForAWindowOrFullscreenAndIgnoresOtherValues()
    {
        var (vm, host) = Start(Build("SCREEN_MODE_OP",
            (0x2cf, new[] { I(1) }), (0x2cf, new[] { I(0) }), (0x2cf, new[] { I(2) }), (0x2cf, new[] { I(-1) })));

        vm.Run();

        Assert.Equal([true, false], host.Requests);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Done));
        // The op only asks: CALLBACK_WINDOW.BIN waits for the frontend's switch.
        Assert.Equal(0, vm.Globals.GetValueOrDefault(WindowCallbackRan));
    }

    [Fact]
    public void ASwitchRunsCallbackWindowAtTheNextInstructionBoundary()
    {
        var (vm, host) = Start(Build("SCREEN_MODE_SWITCH", Probes(6)));
        host.AtYield[3] = v => v.RequestWindowCallback();

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(WindowCallbackRan));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(SettingCallbackRan));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Done));   // the callback returned to the scene
    }

    [Fact]
    public void ASwitchDuringATextWaitRunsCallbackWindowInsideTheWait()
    {
        var (vm, host) = Start(Build("SCREEN_MODE_WAIT", (0x72, new[] { I(0) })));
        host.AtWait = v => v.RequestWindowCallback();

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
            v.RequestMenuMessageSkipToggle();
            v.RequestWindowCallback();
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

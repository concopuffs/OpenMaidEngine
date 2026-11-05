using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

/// <summary>
/// The AGE:IAGEService calls behind AGERC's ﾒｯｾｰｼﾞ menu (docs/engine-re.md, "Native menu bar"): Skip
/// (adv_toggle_skip_mode@0x406c20), Auto (adv_toggle_auto_mode@0x406b70) and read-message Skip (0x406cd0). The
/// frontend queues them; the VM applies them at its next instruction boundary or text-wait service, then runs
/// CALLBACK_SETTING.BIN as AGERC's command handler does.
/// </summary>
public class MenuServicesTests
{
    private const int Probe = 0x200, SkipOut = 0x300, AutoOut = 0x301, ReadSkipBefore = 0x302, ReadSkipAfter = 0x303;
    private const int CallbackRan = 0x311;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);

    /// <summary>Runs an action at chosen instruction boundaries or when the text wait starts.</summary>
    private sealed class MenuHost : RecordingHost
    {
        public VirtualMachine? Vm;
        public readonly Dictionary<int, Action<VirtualMachine>> AtYield = new();
        public Action<VirtualMachine>? AtWait;
        public int EndedWaits;
        private int _yields;

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
    {
        var ops = new List<(int, Operand[])>(body)
        {
            (0x19a, new[] { G(SkipOut) }),
            (0x1b6, new[] { G(AutoOut) }),
            (0x2, Array.Empty<Operand>()),
        };
        return ScriptAssembler.Assemble(Table, name, ops, Array.Empty<string>());
    }

    private static (int, Operand[])[] Probes(int first, int count)
        => Enumerable.Range(first, count).Select(i => (0x1c7, new[] { G(Probe + i) })).ToArray();

    private static MapProvider SettingCallback()
    {
        Script callback = ScriptAssembler.Assemble(Table, "CALLBACK_SETTING", new List<(int, Operand[])>
        {
            (0x55, new[] { G(CallbackRan), I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        return new MapProvider(new Dictionary<long, Script>(),
            new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase) { ["CALLBACK_SETTING.BIN"] = callback });
    }

    private static (VirtualMachine Vm, MenuHost Host) Start(Script script, VmOptions? options = null)
    {
        var host = new MenuHost();
        var vm = new VirtualMachine(script, Table, host, options ?? new VmOptions(), SettingCallback());
        host.Vm = vm;
        return (vm, host);
    }

    [Fact]
    public void SkipToggleStartsFastForwardClearsAutoAndRunsTheSettingCallback()
    {
        var (vm, host) = Start(Build("MENU_SKIP", [(0x1b7, new[] { I(1) }), .. Probes(0, 6)]));
        host.AtYield[3] = v => v.RequestMenuMessageSkipToggle();

        vm.Run();

        Assert.Equal(0, vm.Globals.GetValueOrDefault(Probe));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + 5));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(SkipOut));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(AutoOut));   // set:CoexistMesSkip 0
        Assert.True(host.MessageSkip);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(CallbackRan));
        Assert.True(vm.MenuState.MessageSkip);
        Assert.False(vm.MenuState.AutoMessage);
    }

    [Fact]
    public void CoexistMesSkipKeepsAutoOnWithSkip()
    {
        var (vm, host) = Start(Build("MENU_COEXIST", [(0x1b7, new[] { I(1) }), .. Probes(0, 4)]),
                               new VmOptions(CoexistMessageSkip: true));
        host.AtYield[3] = v => v.RequestMenuMessageSkipToggle();

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(SkipOut));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(AutoOut));
    }

    [Fact]
    public void SkipToggleWhileTheSkipServiceIsSuspendedWaitsForTheResume()
    {
        // Op 0x19b clears ctx+0x55100: the toggle sets only the persistent Skip until op 0x19c recomputes.
        var (vm, host) = Start(Build("MENU_SUSPENDED",
            [(0x19b, Array.Empty<Operand>()), .. Probes(0, 4), (0x19c, Array.Empty<Operand>()), .. Probes(4, 1)]));
        host.AtYield[3] = v => v.RequestMenuMessageSkipToggle();

        vm.Run();

        Assert.Equal(0, vm.Globals.GetValueOrDefault(Probe + 3));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + 4));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(SkipOut));
    }

    [Fact]
    public void SkipToggleOffKeepsFastForwardUntilThePageRecomputesIt()
    {
        // Native clears the persistent flag and ctx+0x13dc but not run-state 0x08000000; op 0xfa recomputes it.
        var (vm, host) = Start(Build("MENU_SKIP_OFF",
            [(0x88, new[] { I(1) }), .. Probes(0, 4), (0xfa, Array.Empty<Operand>()), .. Probes(4, 1)]));
        host.AtYield[3] = v => v.RequestMenuMessageSkipToggle();

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + 3));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(Probe + 4));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(SkipOut));
    }

    [Fact]
    public void AutoToggleAtATextWaitClearsSkipAndEndsTheWait()
    {
        var (vm, host) = Start(Build("MENU_AUTO", (0x88, new[] { I(1) }), (0x72, new[] { I(0) })));
        host.AtWait = v => v.RequestMenuAutoMessageToggle();

        vm.Run();

        Assert.Equal(1, host.EndedWaits);   // run-state 0x80000000 cleared: the page goes on without input
        Assert.Equal(1, vm.Globals.GetValueOrDefault(AutoOut));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(SkipOut));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(CallbackRan));
    }

    [Fact]
    public void AutoToggleWithTheSkipServiceSuspendedLeavesTheWait()
    {
        var (vm, host) = Start(Build("MENU_AUTO_SUSPENDED", (0x19b, Array.Empty<Operand>()), (0x72, new[] { I(0) })));
        host.AtWait = v => v.RequestMenuAutoMessageToggle();

        vm.Run();

        Assert.Equal(0, host.EndedWaits);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(AutoOut));
    }

    [Fact]
    public void AutoToggleTurnsAutoOffWithoutTouchingTheWait()
    {
        var (vm, host) = Start(Build("MENU_AUTO_OFF", (0x1b7, new[] { I(1) }), (0x72, new[] { I(0) })));
        host.AtWait = v => v.RequestMenuAutoMessageToggle();

        vm.Run();

        Assert.Equal(0, host.EndedWaits);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(AutoOut));
    }

    [Fact]
    public void ReadSkipToggleFlipsTheSetting()
    {
        var (vm, host) = Start(Build("MENU_READ_SKIP",
            [(0x1cb, new[] { G(ReadSkipBefore) }), .. Probes(0, 3), (0x1cb, new[] { G(ReadSkipAfter) })]));
        host.AtYield[2] = v => v.RequestMenuReadMessageSkipToggle();

        vm.Run();

        Assert.NotEqual(vm.Globals.GetValueOrDefault(ReadSkipBefore), vm.Globals.GetValueOrDefault(ReadSkipAfter));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(CallbackRan));
    }

    [Fact]
    public void WithoutAMenuCommandTheSettingCallbackDoesNotRun()
    {
        var (vm, _) = Start(Build("MENU_NONE", Probes(0, 3)));

        vm.Run();

        Assert.Equal(0, vm.Globals.GetValueOrDefault(CallbackRan));
    }

    [Fact]
    public void MessageSettingsSeedFromTheHostAndTheDialogsOkAppliesThemAtTheNextBoundary()
    {
        // The 設定 dialog reads the four values (VirtualMachine.MessageSettings) and its OK writes them back the way
        // ops 0x1b5, 0x141 and 0x1b9 do, then runs CALLBACK_SETTING.BIN. Ops 0x7f, 0x131 and 0x1b8 read them.
        (int, Operand[])[] Read(int at) =>
        [
            (0x7f, new[] { G(at) }), (0x131, new[] { G(at + 1) }), (0x1b8, new[] { I(0), G(at + 2) }),
            (0x1b8, new[] { I(1), G(at + 3) }),
        ];
        var host = new MenuHost
        {
            MessageWindowAlphaSetting = 8, AutoMessageTime0Milliseconds = 700, AutoMessageTime1Milliseconds = 2400,
        };
        var vm = new VirtualMachine(Build("MENU_MESSAGE_SETTINGS", [.. Read(0x400), .. Probes(0, 3), .. Read(0x410)]),
                                    Table, host, new VmOptions(), SettingCallback());
        host.Vm = vm;
        Assert.Equal(new VmMessageSettings(50, 8, 700, 2400), vm.MessageSettings);
        host.AtYield[5] = v => v.RequestMessageSettings(new VmMessageSettings(20, 3, 1500, 4000));   // after the reads

        vm.Run();

        Assert.Equal([50, 8, 700, 2400], Enumerable.Range(0x400, 4).Select(g => vm.Globals.GetValueOrDefault(g)));
        Assert.Equal([20, 3, 1500, 4000], Enumerable.Range(0x410, 4).Select(g => vm.Globals.GetValueOrDefault(g)));
        Assert.Equal((20, 3, 1500L, 4000L), (host.MessageGlyphDelayMilliseconds, host.MessageWindowAlphaSetting,
                                             host.AutoMessageTime0Milliseconds, host.AutoMessageTime1Milliseconds));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(CallbackRan));
    }

    [Fact]
    public void AutoMessageTimeOpcodeWritesTheSettingThroughTheHost()
    {
        var (vm, host) = Start(Build("AUTO_TIME_WRITE",
            (0x1b9, new[] { I(0), I(900) }), (0x1b9, new[] { I(1), I(3000) }), (0x1b9, new[] { I(2), I(1) })));

        vm.Run();

        Assert.Equal((900L, 3000L), (host.AutoMessageTime0Milliseconds, host.AutoMessageTime1Milliseconds));
        Assert.Equal(new VmMessageSettings(50, 0, 900, 3000), vm.MessageSettings);
    }

    [Fact]
    public void StartupSettingReadsCoexistMesSkip()
    {
        Assert.True(Sys4StartupSettings.FromPairs(new[]
            { new KeyValuePair<string, string>("COEXISTMESSKIP", "1") }).CoexistMessageSkip);
        Assert.False(Sys4StartupSettings.Empty.CoexistMessageSkip);
    }
}

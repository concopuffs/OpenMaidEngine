using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>
/// Fast-forward from logical action 6 (docs/engine-re.md, "Physical fast-forward"). Native sets run-state 0x08000000
/// from it only at a polling text wait (a key-down) or at a reveal tick (held, unless op 0x101 suppressed it), and the
/// ADV lifecycle ops clear it whatever the key holds. Driven on native 2026-11-07: Kamidori with Ctrl held from SC0000's
/// first page stops at SC2000@0xaf5, after FIELD's op 0x101 and SC2000's op 0x19b. Backspace is action 6 in the
/// engine's default bindings.
/// </summary>
public class PhysicalFastForwardTests
{
    private const int Backspace = 0x08;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(int address) => new(3, address);
    private static Operand S(int index) => new(2, index);

    private sealed class FastForwardHost : RecordingHost
    {
        public VirtualMachine? Vm;
        public Action<VirtualMachine, int>? AtWait;     // the wait's ordinal, from 0
        public Action<VirtualMachine, int>? AtReveal;   // inside the host's reveal, after its first tick
        public Action<VirtualMachine>? AtSleep;
        public readonly List<bool> FastForwardAtReveal = new();
        private int _waits, _reveals;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            AtWait?.Invoke(Vm!, _waits++);
            base.WaitForInput(layoutSlot, serviceInputCallback);
        }

        public override AdvRetainedTextRunResult? ShowText(
            GfxState gfx, AdvTextLayoutPresentationBinding binding, AdvLiveTextRun run, int glyphDelayMilliseconds)
        {
            FastForwardAtReveal.Add(PhysicalMessageSkip);
            AtReveal?.Invoke(Vm!, _reveals++);
            return base.ShowText(gfx, binding, run, glyphDelayMilliseconds);
        }

        public override void Sleep(long duration)
        {
            AtSleep?.Invoke(Vm!);
            base.Sleep(duration);
        }
    }

    private static (VirtualMachine Vm, FastForwardHost Host) Start(params (int Op, Operand[] Operands)[] body)
    {
        var script = ScriptAssembler.Assemble(Table, "PHYSICAL_FF", [.. body, (0x2, Array.Empty<Operand>())], ["a", "b"]);
        var host = new FastForwardHost();
        var vm = new VirtualMachine(script, Table, host);
        host.Vm = vm;
        return (vm, host);
    }

    private static readonly (int, Operand[])[] TextLayout =
    [
        (0x70, new[] { I(1), I(640), I(160), I(0), I(430) }),
        (0x71, new[] { I(1) }),
    ];

    private static void Press(VirtualMachine vm) => vm.UpdateKeyboardVirtualKeyState(Backspace, true);
    private static void Release(VirtualMachine vm) => vm.UpdateKeyboardVirtualKeyState(Backspace, false);

    [Fact]
    public void AKeyDownAtAPollingTextWaitStartsFastForwardUntilTheKeyIsReleased()
    {
        var (vm, host) = Start((0x72, new[] { I(0) }));
        host.AtWait = (v, _) => Press(v);

        vm.Run();

        Assert.True(host.PhysicalMessageSkip);
        Release(vm);
        Assert.False(host.PhysicalMessageSkip);
    }

    [Fact]
    public void AKeyHeldSinceBeforeTheWaitStartsNothing()
    {
        // The wait polls the triggered mask, and op 0x72's entry discards what the latch held.
        var (vm, host) = Start((0x72, new[] { I(0) }));
        Press(vm);

        vm.Run();

        Assert.False(host.PhysicalMessageSkip);
        Assert.Equal(1, host.Waits);
    }

    [Fact]
    public void AKeyDownOutsideAWaitOrRevealStartsNothing()
    {
        var (vm, host) = Start((0xc8, new[] { I(10) }), (0x72, new[] { I(0) }));
        host.AtSleep = Press;

        vm.Run();

        Assert.False(host.PhysicalMessageSkip);
    }

    [Fact]
    public void AKeyHeldAtARevealStartsFastForward()
    {
        var (vm, host) = Start([.. TextLayout, (0x6e, new[] { I(1), S(0) }), (0x72, new[] { I(0) })]);
        Press(vm);

        vm.Run();

        Assert.Equal(new[] { true }, host.FastForwardAtReveal);   // before the reveal publishes a glyph
        Assert.True(host.PhysicalMessageSkip);
    }

    [Fact]
    public void AKeyDownDuringARevealStartsFastForward()
    {
        var (vm, host) = Start([.. TextLayout, (0x6e, new[] { I(1), S(0) }), (0x72, new[] { I(0) })]);
        host.AtReveal = (v, _) => Press(v);

        vm.Run();

        Assert.True(host.PhysicalMessageSkip);
    }

    [Fact]
    public void WithoutAGlyphDelayNoRevealRunsAndAHeldKeyStartsNothing()
    {
        var (vm, host) = Start([.. TextLayout, (0x1b5, new[] { I(0) }), (0x6e, new[] { I(1), S(0) }),
                                (0x72, new[] { I(0) })]);
        Press(vm);

        vm.Run();

        Assert.False(host.PhysicalMessageSkip);
    }

    [Fact]
    public void Op0x101SuppressesTheRevealStartUntilARevealRunsWithTheKeyReleased()
    {
        // ctx+0x6da74: op 0x101 sets it; a reveal tick without action 6 held clears it.
        var (vm, host) = Start([(0x101, Array.Empty<Operand>()), .. TextLayout,
                                (0x6e, new[] { I(1), S(0) }),   // held and suppressed: an ordinary reveal
                                (0xc8, new[] { I(10) }),        // the key goes up
                                (0x6e, new[] { I(1), S(1) })]); // released: the suppression lifts; then pressed
        Press(vm);
        host.AtSleep = Release;
        host.AtReveal = (v, n) => { if (n == 1) Press(v); };

        vm.Run();

        Assert.Equal(new[] { false, false }, host.FastForwardAtReveal);
        Assert.True(host.PhysicalMessageSkip);
    }

    [Fact]
    public void AKeyReleasedAndPressedAgainDuringASuppressedRevealStartsFastForward()
    {
        var (vm, host) = Start([(0x101, Array.Empty<Operand>()), .. TextLayout, (0x6e, new[] { I(1), S(0) })]);
        Press(vm);
        host.AtReveal = (v, _) =>
        {
            Release(v);
            Press(v);
        };

        vm.Run();

        Assert.Equal(new[] { false }, host.FastForwardAtReveal);
        Assert.True(host.PhysicalMessageSkip);
    }

    [Theory]
    [InlineData(0x19b, -1)]
    [InlineData(0x19c, -1)]
    [InlineData(0x101, -1)]
    [InlineData(0x88, 0)]
    public void LifecycleOpsEndFastForwardAndAHeldKeyDoesNotRestartItAtTheNextWait(int op, int argument)
    {
        Operand[] operands = argument < 0 ? Array.Empty<Operand>() : new[] { I(argument) };
        var (vm, host) = Start((0x72, new[] { I(0) }), (op, operands), (0x72, new[] { I(0) }),
                               (0x1c7, new[] { G(0x140) }));
        host.AtWait = (v, n) => { if (n == 0) Press(v); };

        vm.Run();

        Assert.True((vm.InputBindings.PollActionMask() & 0x40) != 0);   // still held
        Assert.False(host.PhysicalMessageSkip);
        Assert.Equal(2, host.Waits);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x140));
    }

    [Fact]
    public void Op0xfaKeepsFastForwardWhileTheKeyIsHeld()
    {
        var (vm, host) = Start((0x72, new[] { I(0) }), (0xfa, Array.Empty<Operand>()), (0x1c7, new[] { G(0x140) }));
        host.AtWait = (v, _) => Press(v);

        vm.Run();

        Assert.True(host.PhysicalMessageSkip);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x140));
    }

    [Fact]
    public void ReleasingTheKeyDoesNotClearPersistentSkip()
    {
        // Persistent Skip on, then an ADV entry starts fast-forward from it.
        var (vm, host) = Start((0x88, new[] { I(1) }), (0x19c, Array.Empty<Operand>()));
        vm.Run();

        Press(vm);
        Release(vm);

        Assert.True(vm.MessageSkipEnabled);
        Assert.False(host.PhysicalMessageSkip);
        Assert.True(host.MessageSkip);
    }

    [Theory]
    [InlineData(0x11)] // SYSTEM4 LeftCtrl binding
    [InlineData(0x43)] // SYSTEM4 C binding
    [Trait("Category", "Workspace")]
    public void System4BindsCtrlAndCToFastForward(int virtualKey)
    {
        var (vm, host) = Start((0x72, new[] { I(0) }));
        InputBindingBootstrap.Apply(Sys4ScriptProvider.Load(Table).RequireByName("SYSTEM4.BIN"), vm.InputBindings);
        host.AtWait = (v, _) => v.UpdateKeyboardVirtualKeyState(virtualKey, true);

        vm.Run();

        Assert.True(host.PhysicalMessageSkip);
        vm.UpdateKeyboardVirtualKeyState(virtualKey, false);
        Assert.False(host.PhysicalMessageSkip);
    }
}

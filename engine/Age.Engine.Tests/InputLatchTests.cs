using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

/// <summary>
/// Native AGE's input latch (docs/engine-re.md, "Effect skipping and the input latch"). Godot holds a trigger in it
/// (VirtualMachine.HoldTriggers) while an effect service parks the VM without polling. That is the default, with
/// system:EffectSkipOnClick 0. The next native poll takes the trigger, and the flush points discard it. Driven on
/// native 2026-11-05:
/// <list type="bullet">
/// <item>a click during SC0000's page 13→14 fade finished the page 14 reveal;</item>
/// <item>a click during the hide control's fade-out restored the window;</item>
/// <item>a click during the fade-in after a Space hide advanced the page.</item>
/// </list>
/// </summary>
public class InputLatchTests
{
    private const int Click = 1 << VirtualMachine.AdvanceAction, Space = 1 << VirtualMachine.AdvWaitCoroutineYieldAction;
    private const int RightClick = 1 << 7;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(int address) => new(3, address);
    private static Operand S(int index) => new(2, index);

    private sealed class LatchHost : RecordingHost
    {
        public VirtualMachine? Vm;
        public Action<VirtualMachine>? AtWait;          // a text wait starts (after op 0x72's entry), before it polls
        public Func<VirtualMachine, bool>? HoldWhen;    // checked after each instruction until it holds once
        public int? HeldAtWait;
        public int EndedWaits;
        public bool RequestYieldAtWait;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            HeldAtWait ??= Vm!.HeldTriggers;
            AtWait?.Invoke(Vm!);
            if (RequestYieldAtWait)
            {
                RequestYieldAtWait = false;
                Assert.True(Vm!.TryRequestAdvWaitCoroutineYield());
            }
            base.WaitForInput(layoutSlot, serviceInputCallback);
        }

        public override void FrameYield()
        {
            if (HoldWhen != null && HoldWhen(Vm!))
            {
                HoldWhen = null;
                Vm!.HoldTriggers(Click);   // what Godot does for a click during an effect here
            }
        }

        public override void EndInputWait() => EndedWaits++;
    }

    private static (VirtualMachine Vm, LatchHost Host) Start(Script script, int held = 0)
    {
        var host = new LatchHost();
        var vm = new VirtualMachine(script, Table, host);
        host.Vm = vm;
        vm.HoldTriggers(held);
        return (vm, host);
    }

    private static Script Assemble(string name, params (int Op, Operand[] Operands)[] body)
        => ScriptAssembler.Assemble(Table, name, [.. body, (0x2, Array.Empty<Operand>())], ["a", "b"]);

    private static readonly (int, Operand[])[] TextLayout =
    [
        (0x70, new[] { I(1), I(640), I(160), I(0), I(430) }),
        (0x71, new[] { I(1) }),
    ];

    [Fact]
    public void AHeldClickFinishesTheNextRevealOnly()
    {
        // adv_text_reveal_service_tick@0x406fa0 takes the whole latch on its first tick; action 4 finishes the reveal
        // (and sets ctx+0x55108, which the host keeps until the page's op 0x72).
        var (vm, host) = Start(Assemble("REVEAL", [.. TextLayout, (0x6e, new[] { I(0), S(0) }), (0x6e, new[] { I(0), S(1) })]),
                               Click);

        vm.Run();

        Assert.Equal(1, host.TextRevealFinishes);
        Assert.Equal(0, vm.HeldTriggers);
    }

    [Fact]
    public void WhileRevealsAreFinishedTheRevealLeavesTheLatch()
    {
        // With ctx+0x55108 set, the reveal service publishes without polling, so a click held for the page's next run
        // stays latched (op 0x72's entry would discard it).
        var (vm, host) = Start(Assemble("REVEAL_RUNS", [.. TextLayout, (0x6e, new[] { I(0), S(0) }), (0x6e, new[] { I(0), S(1) })]),
                               Click);
        host.HoldWhen = _ => host.TextRevealFinishes == 1;

        vm.Run();

        Assert.Equal(1, host.TextRevealFinishes);
        Assert.Equal(Click, vm.HeldTriggers);
    }

    [Fact]
    public void TheRevealTakesOtherHeldTriggersWithoutFinishing()
    {
        var (vm, host) = Start(Assemble("REVEAL_RIGHT", [.. TextLayout, (0x6e, new[] { I(0), S(0) })]), RightClick);

        vm.Run();

        Assert.Equal(0, host.TextRevealFinishes);
        Assert.Equal(0, vm.HeldTriggers);
    }

    [Fact]
    public void WithoutAGlyphDelayNoRevealRunsAndTheLatchStays()
    {
        var (vm, host) = Start(Assemble("NO_REVEAL", [.. TextLayout, (0x1b5, new[] { I(0) }), (0x6e, new[] { I(0), S(0) })]),
                               Click);

        vm.Run();

        Assert.Equal(0, host.TextRevealFinishes);
        Assert.Equal(Click, vm.HeldTriggers);
    }

    [Fact]
    public void TheTextWaitDiscardsTheLatchOnEntry()
    {
        var (vm, host) = Start(Assemble("WAIT", (0x72, new[] { I(0) })), Click);

        vm.Run();

        Assert.Equal(0, host.HeldAtWait);
        Assert.Equal(0, host.EndedWaits);   // the held click does not advance the page
    }

    [Fact]
    public void UnderFastForwardTheTextWaitNeitherDiscardsNorPollsTheLatch()
    {
        var (vm, host) = Start(Assemble("WAIT_FF", (0x88, new[] { I(1) }), (0x72, new[] { I(0) })), Click);

        vm.Run();

        Assert.Equal(Click, host.HeldAtWait);
        Assert.Equal(0, host.EndedWaits);
        Assert.Equal(Click, vm.HeldTriggers);
    }

    [Theory]
    [InlineData(0xfa)]    // op_0xfa_refresh_adv_skip_input_state polls and discards
    [InlineData(0x101)]   // op_0x101_reset_message_skip_input
    public void ServiceOpsDiscardTheLatch(int op)
    {
        var (vm, _) = Start(Assemble("SERVICE", (op, Array.Empty<Operand>())), Click);

        vm.Run();

        Assert.Equal(0, vm.HeldTriggers);
    }

    [Fact]
    public void ALegacyFadeFlushesTheLatchAsItStarts()
    {
        var (vm, _) = Start(Assemble("FADE", (0x21, new[] { I(0), I(16) })), Click);

        vm.Run();

        Assert.Equal(0, vm.HeldTriggers);
    }

    [Fact]
    public void AHeldClickCancelsTheNextModalMovie()
    {
        // The run-state-0x2000 service polls the latch on its first tick, whatever EffectSkipOnClick says.
        var (vm, host) = Start(Assemble("MOVIE", (0x20f, new[] { I(0x33), I(0), I(0) })), Click);

        vm.Run();

        Assert.Equal(1, host.ModalMovieCancels);
        Assert.Single(host.ModalMovies);
        Assert.Equal(0, vm.HeldTriggers);
    }

    // 0: handlers; 5: yield; 6: page continues; 12: handler A; 18: handler B.
    private static Script ControlYield()
        => ScriptAssembler.Assemble(Table, "SC", new List<(int, Operand[])>
        {
            (0x7b, new[] { I(12), I(18) }),   // 0
            (0x199, Array.Empty<Operand>()),  // 5: the control's callback yields
            (0x55, new[] { G(3), I(1) }),     // 6
            (0x2, Array.Empty<Operand>()),    // 11
            (0x55, new[] { G(1), I(1) }),     // 12: handler A
            (0x7c, Array.Empty<Operand>()),   // 17
            (0x55, new[] { G(2), I(1) }),     // 18: handler B
            (0x7c, Array.Empty<Operand>()),   // 23
        }, Array.Empty<string>());

    [Fact]
    public void AClickHeldBeforeTheYieldEndsTheHiddenWait()
    {
        // Op 0x199 clears only the service trigger mask: a click an effect in the control's callback held before the
        // yield is still latched when the hidden-window wait polls.
        var (vm, host) = Start(ControlYield(), Click);

        vm.Run();

        Assert.Equal(1, host.ServiceTriggerWaits);
        Assert.True(host.LastServiceTriggerWaitTriggered);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(2));   // handler B ran
        Assert.Equal(0, vm.HeldTriggers);
    }

    // 0: handlers; 5: text wait; 8: page continues; 14: handler A; 20: handler B.
    private static Script TextWaitYield()
        => ScriptAssembler.Assemble(Table, "SC", new List<(int, Operand[])>
        {
            (0x7b, new[] { I(14), I(20) }),   // 0
            (0x72, new[] { I(0) }),           // 5: text wait
            (0x55, new[] { G(3), I(1) }),     // 8
            (0x2, Array.Empty<Operand>()),    // 13
            (0x55, new[] { G(1), I(1) }),     // 14: handler A
            (0x7c, Array.Empty<Operand>()),   // 19
            (0x55, new[] { G(2), I(1) }),     // 20: handler B
            (0x7c, Array.Empty<Operand>()),   // 25
        }, Array.Empty<string>());

    [Fact]
    public void AClickHeldThroughHandlerBAdvancesTheWaitSpaceParked()
    {
        // Op 0x7c returns to the parked wait without re-running op 0x72, so the wait's poll takes the click handler B's
        // fade-in held and advances the page (native: SC0000@0x8a9 -> 0x8d8 with EffectSkipOnClick 0).
        var (vm, host) = Start(TextWaitYield());
        host.RequestYieldAtWait = true;
        host.ServiceTriggerWaitStarted = () => Assert.True(vm.TryLatchAdvCoroutineYieldTrigger(Space));
        host.HoldWhen = v => v.Globals.GetValueOrDefault(2) == 1;   // inside handler B

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(2));
        Assert.Equal(1, host.EndedWaits);
        Assert.Equal(0, vm.HeldTriggers);
    }

    [Fact]
    public void AClickHeldInAHotspotCallbackIsDiscardedWhenItReturnsToTheWait()
    {
        // The callback's ret re-runs the waiting op 0x72, whose entry discards the latch: the page stays.
        const int activateTarget = 20;   // 0x90 (15) + 0x94 (1) + 0x72 (3) + 0x2 (1)
        var script = ScriptAssembler.Assemble(Table, "HOTSPOT", new List<(int, Operand[])>
        {
            (0x90, new[] { I(10), I(20), I(20), I(20), I(-1), I(-1), I(activateTarget) }),
            (0x94, Array.Empty<Operand>()),
            (0x72, new[] { I(0) }),
            (0x2, Array.Empty<Operand>()),
            (0x55, new[] { G(1), I(1) }),     // 20: the callback
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var (vm, host) = Start(script);
        host.AtWait = v =>
        {
            v.UpdatePointer(30, 40);
            Assert.True(v.TryActivatePointer(30, 40));
        };
        host.HoldWhen = v => v.Globals.GetValueOrDefault(1) == 1;

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(1));
        Assert.Equal(0, host.EndedWaits);
        Assert.Equal(0, vm.HeldTriggers);
    }
}

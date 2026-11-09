using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>
/// The ADV coroutine yield's hidden-window wait (docs/engine-re.md, "The other guards", coroutine yield). Op 0x199, or
/// logical action 5 at a polling text wait, enters the frame's op-0x7b handler A. When handler A ends at op 0x7c without
/// a second op 0x199 (Himegari: adv_hide_window_enabled G[0x62425] is 0 in retail), op 0x7c restores a run state that
/// still holds 0x4000000: the engine waits at the resume pc until any trigger, then enters handler B, whose op 0x7c
/// returns to the page. Each script below marks handler A in G[1], handler B in G[2] and the page's continuation in
/// G[3].
/// </summary>
public class HiddenWindowWaitTests
{
    private const int T_IMM = 0, T_GINT = 3;
    private const int Click = 1 << 4, Space = 1 << 5;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(T_IMM, value);
    private static Operand G(int address) => new(T_GINT, address);

    private sealed class YieldHost : RecordingHost
    {
        public VirtualMachine? Vm;
        public bool RequestYieldAtWait;
        public bool? YieldRequested;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            if (RequestYieldAtWait)
            {
                RequestYieldAtWait = false;
                YieldRequested = Vm!.TryRequestAdvWaitCoroutineYield();
            }
            base.WaitForInput(layoutSlot, serviceInputCallback);
        }
    }

    // 0: handlers; 5: yield; 6: page continues; 12: handler A; 18: handler B.
    private static Script ControlYield(long handlerA = 12, long handlerB = 18, bool handlerAYieldsAgain = false)
    {
        var code = new List<(int, Operand[])>
        {
            (0x7b, new[] { I(handlerA), I(handlerB) }),   // 0
            (0x199, Array.Empty<Operand>()),              // 5: the control's callback yields
            (0x55, new[] { G(3), I(1) }),                 // 6
            (0x2, Array.Empty<Operand>()),                // 11
            (0x55, new[] { G(1), I(1) }),                 // 12: handler A
            (handlerAYieldsAgain ? 0x199 : 0x7c, Array.Empty<Operand>()), // 17
            (0x55, new[] { G(2), I(1) }),                 // 18: handler B
            (0x7c, Array.Empty<Operand>()),               // 23
        };
        return ScriptAssembler.Assemble(Table, "SC", code, Array.Empty<string>());
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

    private static void AssertRanHandlersAndPage(VirtualMachine vm)
    {
        Assert.Equal(1, vm.Globals.GetValueOrDefault(1));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(2));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(3));
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void HandlerAEndingAtTheResumeWaitsHiddenUntilATriggerThenRunsHandlerB()
    {
        var host = new RecordingHost();
        var vm = new VirtualMachine(ControlYield(), Table, host);
        VmFrameLocation? waitingAt = null;
        bool latched = false;
        host.ServiceTriggerWaitStarted = () =>
        {
            Assert.Equal(1, vm.Globals.GetValueOrDefault(1));
            Assert.Equal(0, vm.Globals.GetValueOrDefault(2));
            Assert.Equal([true], host.AdvPagePresentationSuspended); // the page stays hidden through the wait
            waitingAt = vm.CaptureFrameLocations()[^1];
            latched = vm.TryLatchAdvCoroutineYieldTrigger(Click);
        };

        vm.Run();

        AssertRanHandlersAndPage(vm);
        Assert.Equal(1, host.ServiceTriggerWaits);
        Assert.True(latched);
        Assert.True(host.LastServiceTriggerWaitTriggered);
        Assert.Equal(6, waitingAt!.ResumeOffset); // native's frame stands on the resume pc
        Assert.Equal([true, false], host.AdvPagePresentationSuspended);
    }

    [Fact]
    public void HandlerAYieldingAgainLeavesForHandlerBWithoutTheHiddenWait()
    {
        // The HIDEWIN.BIN path (Kamidori, or G[0x62425] set): handler A's second op 0x199 enters handler B.
        var host = new RecordingHost();
        var vm = new VirtualMachine(ControlYield(handlerAYieldsAgain: true), Table, host);

        vm.Run();

        AssertRanHandlersAndPage(vm);
        Assert.Equal(0, host.ServiceTriggerWaits);
        Assert.Equal([true, false], host.AdvPagePresentationSuspended);
    }

    [Fact]
    public void HiddenWaitWithoutHandlerBReturnsToThePageOnTheTrigger()
    {
        var host = new RecordingHost();
        var vm = new VirtualMachine(ControlYield(handlerB: -1), Table, host);

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(1));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(2));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(3));
        Assert.Equal(1, host.ServiceTriggerWaits);
        Assert.Equal([true, false], host.AdvPagePresentationSuspended);
    }

    [Fact]
    public void YieldWithNoHandlerADoesNothingEvenWithHandlerB()
    {
        // op_0x199_yield_adv_coroutine@0x416440 takes handler B only while a yield is entered.
        var host = new RecordingHost();
        var vm = new VirtualMachine(ControlYield(handlerA: -1), Table, host);

        vm.Run();

        Assert.Equal(0, vm.Globals.GetValueOrDefault(1));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(2));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(3));
        Assert.Empty(host.AdvPagePresentationSuspended);
    }

    [Fact]
    public void TheSkipStateEndsTheHiddenWaitWithoutInput()
    {
        // input_poll_triggered_action_mask injects action 6 while the persistent Skip state (ctx+0x13dc) is set.
        var host = new RecordingHost();
        var vm = new VirtualMachine(SkipThenControlYield(), Table, host);

        vm.Run();

        AssertRanHandlersAndPage(vm);
        Assert.Equal(1, host.ServiceTriggerWaits);
        Assert.True(host.LastServiceTriggerWaitTriggered);
    }

    // 0: skip on; 3: handlers; 8: yield; 9: page continues; 15: handler A; 21: handler B.
    private static Script SkipThenControlYield()
        => ScriptAssembler.Assemble(Table, "SC", new List<(int, Operand[])>
        {
            (0x88, new[] { I(1) }),           // 0
            (0x7b, new[] { I(15), I(21) }),   // 3
            (0x199, Array.Empty<Operand>()),  // 8
            (0x55, new[] { G(3), I(1) }),     // 9
            (0x2, Array.Empty<Operand>()),    // 14
            (0x55, new[] { G(1), I(1) }),     // 15: handler A
            (0x7c, Array.Empty<Operand>()),   // 20
            (0x55, new[] { G(2), I(1) }),     // 21: handler B
            (0x7c, Array.Empty<Operand>()),   // 26
        }, Array.Empty<string>());

    [Fact]
    public void ControlYieldWheelAccumulatesInsteadOfEndingTheWait()
    {
        // The run state is 0x4000000 alone, so age_main_window_proc accumulates the delta for op 0x10d.
        var host = new RecordingHost();
        var vm = new VirtualMachine(ControlYield(), Table, host);
        MouseWheelOutcome? outcome = null;
        host.ServiceTriggerWaitStarted = () => outcome = vm.ApplyMouseWheel(-120, false, false);

        vm.Run();

        Assert.Equal(MouseWheelOutcome.Accumulated, outcome);
        Assert.False(host.LastServiceTriggerWaitTriggered);
    }

    [Fact]
    public void SpaceAtATextWaitHidesThePageAndTheTriggerReturnsToTheSameWait()
    {
        var host = new YieldHost { RequestYieldAtWait = true };
        var vm = new VirtualMachine(TextWaitYield(), Table, host);
        host.Vm = vm;
        VmFrameLocation? waitingAt = null;
        bool latched = false;
        host.ServiceTriggerWaitStarted = () =>
        {
            waitingAt = vm.CaptureFrameLocations()[^1];
            latched = vm.TryLatchAdvCoroutineYieldTrigger(Space);
        };

        vm.Run();

        Assert.True(host.YieldRequested);
        AssertRanHandlersAndPage(vm);
        Assert.Equal(1, host.Waits);              // the page's wait parked once: the yield did not advance it
        Assert.Equal(1, host.ServiceTriggerWaits);
        Assert.True(latched);
        Assert.Equal(8, waitingAt!.ResumeOffset); // the wait's own location, as at any text wait
        Assert.Equal([true, false], host.AdvPagePresentationSuspended);
    }

    [Fact]
    public void TextWaitYieldWheelEndsTheHiddenWait()
    {
        // The saved run state keeps the wait's 0x80000000, so the window proc raises WheelKeyDown's action.
        var host = new YieldHost { RequestYieldAtWait = true };
        var vm = new VirtualMachine(TextWaitYield(), Table, host);
        host.Vm = vm;
        MouseWheelOutcome? outcome = null;
        host.ServiceTriggerWaitStarted = () => outcome = vm.ApplyMouseWheel(-120, false, false);

        vm.Run();

        Assert.Equal(MouseWheelOutcome.Activated, outcome);
        Assert.True(host.LastServiceTriggerWaitTriggered);
        AssertRanHandlersAndPage(vm);
    }

    [Fact]
    public void ActionFiveOutsideATextWaitIsNotAYield()
    {
        var host = new RecordingHost();
        var vm = new VirtualMachine(ControlYield(), Table, host);

        Assert.False(vm.TryRequestAdvWaitCoroutineYield());
        Assert.False(vm.TryLatchAdvCoroutineYieldTrigger(Space)); // no yield under way
    }
}

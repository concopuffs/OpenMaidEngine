using System;
using System.Collections.Generic;
using Ome.Engine.Diagnostics;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>The native wheel routing (age_main_window_proc@0x486320, adv_input_service_poll@0x411230, the reveal
/// service at 0x406fa0; docs/engine-re.md "Mouse wheel at an ADV text wait").</summary>
public class MouseWheelInputTests
{
    private const int CallbackTarget = 34;

    // An off-screen record bound to WHEELKEYUP's action 8, as every Himegari scene binds its History record; the wait
    // then reads op 0x10d, and the callback marks G[0x110].
    private static Script BoundWheelScript(OpcodeTable table) => ScriptAssembler.Assemble(table, "WHEEL",
        new List<(int, Operand[])>
        {
            (0x90, new[] { I(-1000), I(-1008), I(1), I(1), I(-1), I(-1), I(CallbackTarget) }), // 0
            (0x97, new[] { I(-1000), I(-1008), I(1), I(1), I(8) }),                             // 15
            (0x94, Array.Empty<Operand>()),                                                     // 26
            (0x72, new[] { I(1) }),                                                             // 27
            (0x10d, new[] { G(0x120) }),                                                        // 30
            (0x2, Array.Empty<Operand>()),                                                      // 33
            (0x55, new[] { G(0x110), I(1) }),                                                   // 34
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());

    private sealed class WheelAtWaitHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public bool PollingAtWait, PollingAfterCallback;
        public MouseWheelOutcome Down, DownWithAdvance, Up;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Waits++;
            PollingAtWait = Vm.IsAdvTextWaitPolling;
            Down = Vm.ApplyMouseWheel(-120, advanceMessageOnWheel: false, textRevealing: false);
            DownWithAdvance = Vm.ApplyMouseWheel(-120, advanceMessageOnWheel: true, textRevealing: false);
            Up = Vm.ApplyMouseWheel(120, advanceMessageOnWheel: false, textRevealing: false);
            while (serviceInputCallback()) { }
            PollingAfterCallback = Vm.IsAdvTextWaitPolling;
        }
    }

    private sealed class PollingProbeSink : ITraceSink
    {
        public VirtualMachine Vm = null!;
        public bool? PollingInCallback;
        public bool TracingSteps => true;

        public void Emit(in TraceEvent e)
        {
            if (e.Kind == TraceEventKind.Step && e.Ins!.Offset == CallbackTarget) PollingInCallback = Vm.IsAdvTextWaitPolling;
        }
    }

    [Fact]
    public void WheelAtAPollingWait_RaisesTheBoundActionAndNeverReachesTheCounter()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var host = new WheelAtWaitHost();
        var sink = new PollingProbeSink();
        var vm = new VirtualMachine(BoundWheelScript(table), table, host,
            new VmOptions(WheelKeyUpAction: 8, WheelKeyDownAction: 9), sink: sink);
        host.Vm = vm;
        sink.Vm = vm;

        Assert.False(vm.IsAdvTextWaitPolling);
        vm.Run();

        Assert.True(host.PollingAtWait);
        Assert.Equal(MouseWheelOutcome.Dropped, host.Down);           // action 9 is unbound
        Assert.Equal(MouseWheelOutcome.Advance, host.DownWithAdvance); // AdvanceMesOnWheel=1
        Assert.Equal(MouseWheelOutcome.Activated, host.Up);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x110));
        Assert.False(sink.PollingInCallback);                         // the callback feeds op 0x10d instead
        Assert.True(host.PollingAfterCallback);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x120));         // nothing reached the counter
        Assert.False(vm.IsAdvTextWaitPolling);
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void WheelUpOnTheDefaultAction_DoesNotRunAHistoryBindingOnAction8()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var host = new WheelAtWaitHost();
        var vm = new VirtualMachine(BoundWheelScript(table), table, host); // registered defaults 3/1
        host.Vm = vm;

        vm.Run();

        Assert.Equal(MouseWheelOutcome.Dropped, host.Up);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x110));
    }

    [Fact]
    public void WheelOutsideAWait_AccumulatesForTheCounter_AndARevealWithAdvanceConsumesIt()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "COUNTER", new List<(int, Operand[])>
        {
            (0x10d, new[] { G(0x120) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        // The reveal service reads and clears the counter only with AdvanceMesOnWheel=1, and finishes the reveal only
        // when the total it read is negative.
        Assert.Equal(MouseWheelOutcome.Accumulated, vm.ApplyMouseWheel(-120, false, textRevealing: true));
        Assert.Equal(MouseWheelOutcome.Dropped, vm.ApplyMouseWheel(120, true, textRevealing: true)); // -120 + 120
        Assert.Equal(MouseWheelOutcome.Advance, vm.ApplyMouseWheel(-120, true, textRevealing: true));
        Assert.Equal(MouseWheelOutcome.Dropped, vm.ApplyMouseWheel(120, true, textRevealing: true));
        Assert.Equal(MouseWheelOutcome.Accumulated, vm.ApplyMouseWheel(240, true, textRevealing: false));
        Assert.Equal(MouseWheelOutcome.Accumulated, vm.ApplyMouseWheel(120, false, textRevealing: false));

        vm.Run();

        Assert.Equal(360, vm.Globals.GetValueOrDefault(0x120));
    }

    [Fact]
    public void WheelKeysComeFromTheSettingsTrailerWithTheRegisteredDefaults()
    {
        var himegari = Sys4StartupSettings.FromPairs(new[]
        {
            new KeyValuePair<string, string>("WHEELKEYUP", "8"),
            new KeyValuePair<string, string>("WHEELKEYDOWN", "9"),
        });
        Assert.Equal((8, 9), (himegari.WheelKeyUpAction, himegari.WheelKeyDownAction));
        Assert.Equal((3, 1), (Sys4StartupSettings.Empty.WheelKeyUpAction, Sys4StartupSettings.Empty.WheelKeyDownAction));

        VmOptions options = GodotVmOptions.Create(selftest: false, ignoreExitRequests: false, himegari);
        Assert.Equal((8, 9), (options.WheelKeyUpAction, options.WheelKeyDownAction));
    }

    private sealed class StopAtHistoryException : Exception { }

    private sealed class WheelUpAtFirstWaitHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public MouseWheelOutcome Up;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Up = Vm.ApplyMouseWheel(120, advanceMessageOnWheel: false, textRevealing: false);
            while (serviceInputCallback()) { }
        }
    }

    private sealed class StopAtHistorySink : ITraceSink
    {
        public bool TracingSteps => false;

        public void Emit(in TraceEvent e)
        {
            if (e.Kind == TraceEventKind.FrameEnter && string.Equals(e.Name, "HISTORY.BIN", StringComparison.OrdinalIgnoreCase))
                throw new StopAtHistoryException();
        }
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Sc0000WheelUpAtTheFirstWait_EntersTheRealHistory()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scripts = Sys4ScriptProvider.Load(table);
        var scene = scripts.RequireByName("SC0000.BIN");
        var host = new WheelUpAtFirstWaitHost();
        var vm = new VirtualMachine(scene, table, host,
            GodotVmOptions.Create(selftest: true, ignoreExitRequests: false,
                Sys4AssetCatalog.Load(Paths.Sys4Ini).StartupSettings), scripts,
            new StopAtHistorySink());
        host.Vm = vm;
        vm.Globals[0x6c1] = 1;

        Assert.Throws<StopAtHistoryException>(() => vm.Run());

        Assert.Equal(MouseWheelOutcome.Activated, host.Up);
    }

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
}

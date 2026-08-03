using System;
using System.Collections.Generic;
using System.Linq;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class HotspotInputTests
{
    private sealed class StopAtFirstWaitException : Exception { }

    private sealed class InteractiveHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public bool ClickConsumed;
        public bool SecondClickConsumed;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Waits++;

            // Width/height are added to x/y, so the native rectangle includes (30,40).
            Vm.UpdatePointer(30, 40);
            while (serviceInputCallback()) { }
            Assert.Equal(1, Vm.Globals.GetValueOrDefault(0x100));

            Vm.UpdatePointer(31, 40);
            while (serviceInputCallback()) { }
            Assert.Equal(1, Vm.Globals.GetValueOrDefault(0x101));

            Vm.UpdatePointer(30, 40);
            while (serviceInputCallback()) { }
            ClickConsumed = Vm.TryActivatePointer(30, 40);
            while (serviceInputCallback()) { }
            SecondClickConsumed = Vm.TryActivatePointer(30, 40);
            while (serviceInputCallback()) { }
        }
    }

    private sealed class BoundActionHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public bool Consumed;
        public bool DuplicateConsumed;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Waits++;
            Assert.False(Vm.TryActivateInputActions(1 << 6));
            Consumed = Vm.TryActivateInputActions(1 << 7);
            DuplicateConsumed = Vm.TryActivateInputActions(1 << 7);
            while (serviceInputCallback()) { }
        }
    }

    private sealed class Sc0000MenuHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public bool MenuActionConsumed;
        public bool ParentHotspotsRearmed;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            MenuActionConsumed = Vm.TryActivateInputActions(1 << 7);
            while (serviceInputCallback()) { }
            // Consuming the same binding again without advancing the page proves the parent registry
            // was restored after MENU returned. Do not service it: this test ends at that boundary.
            ParentHotspotsRearmed = Vm.TryActivateInputActions(1 << 7);
            throw new StopAtFirstWaitException();
        }
    }

    private sealed class ReturnMenuOnEntrySink : Age.Engine.Diagnostics.ITraceSink
    {
        public VirtualMachine Vm = null!;
        public bool MenuEntered;
        private bool _returnRequested;
        public bool TracingSteps => true;

        public void Emit(in Age.Engine.Diagnostics.TraceEvent e)
        {
            if (e.Kind == Age.Engine.Diagnostics.TraceEventKind.FrameEnter
                && string.Equals(e.Name, "MENU.BIN", StringComparison.OrdinalIgnoreCase))
            {
                MenuEntered = true;
                return;
            }
            // Request the controlled return when MENU reaches its slot selector. Step events are emitted
            // before execution, so op 0x80 still runs and the request is consumed at its next boundary.
            if (!MenuEntered || _returnRequested
                || e.Kind != Age.Engine.Diagnostics.TraceEventKind.Step || e.Opcode != 0x80) return;
            var frame = Assert.IsType<DebugFrameSnapshot>(Vm.DebugFrame);
            Assert.True(Vm.TryRequestDebugFrameReturn(frame.FrameId, new Dictionary<int, long>()));
            _returnRequested = true;
        }
    }

    private sealed class Sc0000HoverHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public bool SawHistoryHover;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Vm.UpdatePointer(684, 572);
            while (serviceInputCallback()) { }
            SawHistoryHover = Vm.Globals.GetValueOrDefault(0x6c9) == 1;
            throw new StopAtFirstWaitException();
        }
    }

    private sealed class Sc0000MessageSkipHost : RecordingHost
    {
        public VirtualMachine Vm = null!;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                                          Func<AdvAutoWaitState> autoWaitState)
        {
            Vm.UpdatePointer(728, 572);
            while (serviceInputCallback()) { }
            Assert.True(Vm.TryActivatePointer(728, 572));
            while (serviceInputCallback()) { }
            throw new StopAtFirstWaitException();
        }
    }

    private sealed class AutoStateHost : RecordingHost
    {
        public AdvAutoWaitState State;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                                          Func<AdvAutoWaitState> autoWaitState)
        {
            while (serviceInputCallback()) { }
            State = autoWaitState();
            Waits++;
        }
    }

    private sealed class MessageSkipCadenceHost : RecordingHost
    {
        public int ActiveSkipYields;
        public override void FrameYield()
        {
            if (MessageSkip) ActiveSkipYields++;
        }
    }

    private sealed class Sc0000HideWindowHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        private long _now;
        public int HideLoopSleeps;
        public bool HideReturned;
        public override long InputClockMilliseconds => _now;

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            _now += System.Math.Max(16, duration);
            if (duration <= 1 && ++HideLoopSleeps == 2)
            {
                Vm.UpdateMouseButtonState(0x1, false); // release the x=772 activation click
                Vm.QueueInputCallback(10);             // common release callback arms HIDEWIN input
            }
            else if (duration <= 1 && HideLoopSleeps == 3)
            {
                Vm.UpdateMouseButtonState(0x1, true);  // next primary click is generic action index 4
                Vm.QueueInputCallback(4);
            }
            else if (duration <= 1 && HideLoopSleeps == 4)
            {
                Vm.UpdateMouseButtonState(0x1, false);
                Vm.QueueInputCallback(10);
            }
        }

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Vm.UpdatePointer(772, 572);
            while (serviceInputCallback()) { }
            Vm.UpdateMouseButtonState(0x1, true);
            Assert.True(Vm.TryActivatePointer(772, 572));
            while (serviceInputCallback()) { }
            HideReturned = true;
            throw new StopAtFirstWaitException();
        }
    }

    [Fact]
    public void ArmedHotspot_DispatchesHoverAndConsumesActivationWithoutAdvancingPage()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // Main code ends at dword 20. Activation also exercises an ordinary local call beneath the
        // VM's temporary callback return sentinel.
        const int enterTarget = 20, leaveTarget = 26, activateTarget = 32;
        var script = ScriptAssembler.Assemble(table, "HOTSPOT", new List<(int, Operand[])>
        {
            (0x90, new[] { I(10), I(20), I(20), I(20), I(enterTarget), I(leaveTarget), I(activateTarget) }),
            (0x94, Array.Empty<Operand>()),
            (0x72, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
            (0x55, new[] { G(0x100), I(1) }),
            (0x5, Array.Empty<Operand>()),
            (0x55, new[] { G(0x101), I(1) }),
            (0x5, Array.Empty<Operand>()),
            (0x8f, new[] { I(36) }),
            (0x5, Array.Empty<Operand>()),
            (0x1b7, new[] { I(1) }),
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new InteractiveHost();
        var vm = new VirtualMachine(script, table, host);
        host.Vm = vm;

        vm.Run();

        Assert.True(host.ClickConsumed);
        Assert.True(host.SecondClickConsumed);
        Assert.True(vm.AutoMessageEnabled);
        Assert.Equal(1, host.Waits);
        Assert.Equal(7, host.InputCallbackFrames);
        Assert.Equal("exit", vm.HaltReason);
        Assert.False(vm.TryActivatePointer(30, 40)); // the script frame has exited, so no hotspot remains active
    }

    [Fact]
    public void ArmedHotspot_DispatchesBoundLogicalActionThroughOrdinaryActivationCallback()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        const int activateTarget = 31;
        var script = ScriptAssembler.Assemble(table, "HOTSPOT_ACTION", new List<(int, Operand[])>
        {
            (0x90, new[] { I(10), I(20), I(20), I(20), I(-1), I(-1), I(activateTarget) }),
            (0x97, new[] { I(10), I(20), I(20), I(20), I(7) }),
            (0x94, Array.Empty<Operand>()),
            (0x72, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
            (0x55, new[] { G(0x110), I(1) }),
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new BoundActionHost();
        var vm = new VirtualMachine(script, table, host);
        host.Vm = vm;

        vm.Run();

        Assert.True(host.Consumed);
        Assert.False(host.DuplicateConsumed);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x110));
        Assert.Equal(1, host.Waits);
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Sc0000AdvChromeBootstrap_VisitsAllFiveVisibleButtonRegistrations()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var trace = new RecordingTraceSink { TracingSteps = true };
        var vm = new VirtualMachine(script, table, new RecordingHost(),
            new VmOptions(HaltAtWaitForInput: true), sink: trace);
        vm.Globals[0x6c1] = 1; // inherited SYSTEM4 adv_chrome_enabled state seeded by Godot Main

        vm.Run();

        Assert.Equal(new[] { 0x94, 0xa3, 0xb2, 0xc1, 0xd0 }, trace.Events
            .Where(e => e.Kind == Age.Engine.Diagnostics.TraceEventKind.Step
                     && e.Opcode == 0x90 && e.Ins!.Offset < 0xdf)
            .Select(e => e.Ins!.Offset).ToArray());
        Assert.Equal("wait-for-input", vm.HaltReason);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Sc0000FirstWait_DispatchesRealHistoryHoverCallback()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new Sc0000HoverHost();
        var vm = new VirtualMachine(script, table, host, new VmOptions(MaxSteps: 1_000_000));
        host.Vm = vm;
        vm.Globals[0x6c1] = 1;

        Assert.Throws<StopAtFirstWaitException>(() => vm.Run());

        Assert.True(host.InputCallbackFrames > 0);
        Assert.True(host.SawHistoryHover);
    }

    [Fact]
    public void AutoMessageOpcodes_RoundTripVmServiceState()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "AUTO", new List<(int, Operand[])>
        {
            (0x1b7, new[] { I(1) }),
            (0x1b6, new[] { G(0x120) }),
            (0x1b7, new[] { I(0) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x120));
        Assert.False(vm.AutoMessageEnabled);
    }

    [Fact]
    public void AutoMessageTimeOpcodes_ConfigureWaitSchedulerState()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "AUTO_TIMES", new List<(int, Operand[])>
        {
            (0x1b9, new[] { I(0), I(750) }),
            (0x1b9, new[] { I(1), I(2250) }),
            (0x1b8, new[] { I(0), G(0x130) }),
            (0x1b8, new[] { I(1), G(0x131) }),
            (0x1b7, new[] { I(1) }),
            (0x72, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new AutoStateHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(750, vm.Globals.GetValueOrDefault(0x130));
        Assert.Equal(2250, vm.Globals.GetValueOrDefault(0x131));
        Assert.Equal(new AdvAutoWaitState(true, false, 750, 2250), host.State);
        Assert.Equal(1, host.Waits);
    }

    [Fact]
    public void VoicePlayback_MarksAutoWaitUntilBlockMarkResetsIt()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var voiced = ScriptAssembler.Assemble(table, "AUTO_VOICE", new List<(int, Operand[])>
        {
            (0xc4, new[] { I(12) }),
            (0x72, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var reset = ScriptAssembler.Assemble(table, "AUTO_VOICE_RESET", new List<(int, Operand[])>
        {
            (0xc4, new[] { I(12) }),
            (0x1bc, Array.Empty<Operand>()),
            (0x72, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var voicedHost = new AutoStateHost();
        var resetHost = new AutoStateHost();

        new VirtualMachine(voiced, table, voicedHost).Run();
        new VirtualMachine(reset, table, resetHost).Run();

        Assert.True(voicedHost.State.VoicePending);
        Assert.False(resetHost.State.VoicePending);
    }

    [Fact]
    public void MessageSkipOpcodes_RetainStateAcrossTransientResetUntilExplicitlyDisabled()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MESSAGE_SKIP", new List<(int, Operand[])>
        {
            (0x88, new[] { I(1) }),
            (0x19a, new[] { G(0x140) }),
            (0x1c7, new[] { G(0x141) }),
            (0x101, Array.Empty<Operand>()),
            (0x1c7, new[] { G(0x142) }),
            (0x88, new[] { I(0) }),
            (0x19a, new[] { G(0x143) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x140));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x141));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x142));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x143));
        Assert.False(vm.MessageSkipEnabled);
        Assert.Equal(new[] { true, false }, host.MessageSkipChanges);
    }

    [Fact]
    public void AdvSkipService_SuspendsAndRestoresPersistentMessageSkip()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MESSAGE_SKIP_LIFECYCLE", new List<(int, Operand[])>
        {
            (0x88, new[] { I(1) }),
            (0x19b, Array.Empty<Operand>()),
            (0x19a, new[] { G(0x144) }),
            (0x1c7, new[] { G(0x145) }),
            (0x19c, Array.Empty<Operand>()),
            (0x1c7, new[] { G(0x146) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x144));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x145));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x146));
        Assert.True(vm.MessageSkipEnabled);
        Assert.Equal(new[] { true, false, true }, host.MessageSkipChanges);
    }

    [Fact]
    public void AdvSkipService_RestoreIncludesLiveReadSkipChannel()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "READ_SKIP_LIFECYCLE", new List<(int, Operand[])>
        {
            (0x19b, Array.Empty<Operand>()),
            (0x19c, Array.Empty<Operand>()),
            (0x1c7, new[] { G(0x147) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost { AdvReadSkip = true };
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x147));
        Assert.False(vm.MessageSkipEnabled);
        Assert.Equal(new[] { false, true }, host.MessageSkipChanges);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Sc0000MessageSkipButton_EnablesPersistentServiceState()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new Sc0000MessageSkipHost();
        var vm = new VirtualMachine(script, table, host, new VmOptions(MaxSteps: 1_000_000));
        host.Vm = vm;
        vm.Globals[0x6c1] = 1;

        Assert.Throws<StopAtFirstWaitException>(() => vm.Run());

        Assert.True(vm.MessageSkipEnabled);
        Assert.True(host.MessageSkip);
        Assert.Contains(true, host.MessageSkipChanges);
    }

    [Fact]
    public void CursorOpcodes_ForwardResourceAndClearToHost()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "CURSOR", new List<(int, Operand[])>
        {
            (0x86, new[] { I(0x3318) }),
            (0x87, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(new long[] { 0x3318 }, host.CursorResources);
        Assert.Equal(1, host.CursorClearCount);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Sc0000HideWindowButton_RunsRealHidewinAndReturnsToAdvWait()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scene = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var hide = Sys4Loader.Load(Paths.Scripts()["HIDEWIN.BIN"], table);
        var provider = new MapProvider(new Dictionary<long, Script> { [0x20] = hide });
        var trace = new RecordingTraceSink { TracingSteps = true };
        var host = new Sc0000HideWindowHost();
        var vm = new VirtualMachine(scene, table, host, new VmOptions(MaxSteps: 1_000_000), provider, trace);
        host.Vm = vm;
        InputBindingBootstrap.Apply(
            Sys4Loader.Load(Paths.Scripts()["SYSTEM4.BIN"], table), vm.InputBindings);
        vm.Globals[0x6c1] = 1;
        vm.Globals[0x62425] = 1; // inherited native ADV scheduler state, mirrored by Godot Main

        Assert.Throws<StopAtFirstWaitException>(() => vm.Run());

        Assert.True(host.HideLoopSleeps >= 4,
            $"hide sleeps={host.HideLoopSleeps}; halt={vm.HaltReason}; frames={string.Join(',', trace.Events.Where(e => e.Kind == Age.Engine.Diagnostics.TraceEventKind.FrameEnter).Select(e => e.Name))}; tail={string.Join(',', trace.Events.Where(e => e.Kind == Age.Engine.Diagnostics.TraceEventKind.Step).TakeLast(30).Select(e => $"{e.Ins!.Offset:x}:{e.Opcode:x}"))}");
        Assert.Contains(trace.Events, e => e.Kind == Age.Engine.Diagnostics.TraceEventKind.FrameEnter
                                        && e.Name?.EndsWith("HIDEWIN.BIN", StringComparison.OrdinalIgnoreCase) == true);
        Assert.True(host.HideReturned);
        Assert.Equal(new[] { true, false }, host.AdvPagePresentationSuspended);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Sc0000ActionSeven_EntersRealMenuAndRestoresParentAdvHotspots()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var scripts = Sys4ScriptProvider.Load(table);
        var scene = scripts.RequireByName("SC0000.BIN");
        var host = new Sc0000MenuHost();
        var sink = new ReturnMenuOnEntrySink();
        var vm = new VirtualMachine(scene, table, host, new VmOptions(MaxSteps: 1_000_000), scripts, sink);
        host.Vm = vm;
        sink.Vm = vm;
        InputBindingBootstrap.Apply(scripts.RequireByName("SYSTEM4.BIN"), vm.InputBindings);
        vm.Globals[0x6c1] = 1;
        vm.Globals[0x62425] = 1;

        Assert.Throws<StopAtFirstWaitException>(() => vm.Run());

        Assert.True(host.MenuActionConsumed);
        Assert.True(sink.MenuEntered);
        Assert.Equal(8, vm.Gfx.DefaultObjectSlot); // MENU's entry selector ran before the controlled return.
        Assert.True(host.ParentHotspotsRearmed);
    }

    [Fact]
    public void MessageSkipState_ReachesHostBeforeFollowingOpcodeCadenceYields()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MESSAGE_SKIP_CADENCE", new List<(int, Operand[])>
        {
            (0x88, new[] { I(1) }),
            (0x55, new[] { G(0x150), I(1) }),
            (0x55, new[] { G(0x151), I(1) }),
            (0x88, new[] { I(0) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new MessageSkipCadenceHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(3, host.ActiveSkipYields);
    }

    [Fact]
    public void AdvCoroutineYield_RunsHandlerAThenHandlerBAndResumesAfterOpcode()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        const int handlerA = 17, handlerB = 23;
        var script = ScriptAssembler.Assemble(table, "ADV_COROUTINE", new List<(int, Operand[])>
        {
            (0x7b, new[] { I(handlerA), I(handlerB) }),
            (0x55, new[] { G(0x160), I(1) }),
            (0x199, Array.Empty<Operand>()),
            (0x55, new[] { G(0x163), I(1) }),
            (0x2, Array.Empty<Operand>()),
            (0x55, new[] { G(0x161), I(1) }),
            (0x199, Array.Empty<Operand>()),
            (0x55, new[] { G(0x162), I(1) }),
            (0x7c, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x160));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x161));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x162));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x163));
        Assert.Equal(new[] { true, false }, host.AdvPagePresentationSuspended);
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void BitSetAndReset_UseBitIndicesRatherThanLiteralMasks()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "BIT_INDEX", new List<(int, Operand[])>
        {
            (0x135, new[] { G(0x168), I(1) }),
            (0x135, new[] { G(0x168), I(4) }),
            (0x136, new[] { G(0x168), I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Equal(0x10, vm.Globals.GetValueOrDefault(0x168));
    }

    [Fact]
    public void BitSet_RejectsNativeOutOfRangeIndex()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "BIT_RANGE", new List<(int, Operand[])>
        {
            (0x135, new[] { G(0x169), I(32) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Equal("bit-index-out-of-range:32", vm.HaltReason);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x169));
    }

    [Fact]
    public void MouseCallback_UsesLivePointerAndButtonState()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        const int callback = 7;
        var script = ScriptAssembler.Assemble(table, "MOUSE_CALLBACK", new List<(int, Operand[])>
        {
            (0xcc, new[] { I(0), I(callback) }),
            (0xcd, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
            (0x109, new[] { G(0x170), G(0x171) }),
            (0x108, new[] { G(0x172) }),
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());
        vm.UpdatePointer(321, 456);
        vm.UpdateMouseButtonState(0x1, true);

        vm.Run();

        Assert.Equal(321, vm.Globals.GetValueOrDefault(0x170));
        Assert.Equal(456, vm.Globals.GetValueOrDefault(0x171));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x172));
    }

    [Fact]
    public void JoyCallbackTable_DispatchesHeldInput()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        const int callback = 8;
        var script = ScriptAssembler.Assemble(table, "JOY_CALLBACK", new List<(int, Operand[])>
        {
            (0xfb, new[] { I(0), I(callback) }),
            (0xff, Array.Empty<Operand>()),
            (0x100, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
            (0x55, new[] { G(0x180), I(1) }),
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());
        vm.UpdateInputCallbackState(0, true);

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x180));
    }

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
}

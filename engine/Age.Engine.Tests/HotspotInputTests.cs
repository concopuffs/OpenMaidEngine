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

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
}

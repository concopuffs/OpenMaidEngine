using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

/// <summary>Native CancelMesSkipOnClick (adv_interpreter_tick@0x410fb0): during active fast-forward a completed
/// action-4 click cancels all-message Skip and ReadTextSkip; mode 2 also launches CALLBACK_SETTING.BIN outside any
/// open statement block.</summary>
public class MessageSkipClickCancelTests
{
    private const int Probe = 0x200;

    /// <summary>Drives physical mouse slot 0 (action 4 by default) at chosen instruction boundaries.</summary>
    private sealed class ClickHost : RecordingHost
    {
        public VirtualMachine? Vm;
        public readonly Dictionary<int, Action<VirtualMachine>> AtYield = new();
        private int _yields;
        public override void FrameYield()
        {
            _yields++;
            if (Vm != null && AtYield.TryGetValue(_yields, out var act)) act(Vm);
        }
    }

    private static Script BuildProbeScript(OpcodeTable table, string name, int probes, bool block = false)
    {
        var ops = new List<(int, Operand[])>
        {
            (0x1ca, new[] { I(1) }),   // message:ReadTextSkip on
            (0x88, new[] { I(1) }),    // all-message Skip on
            (0x19c, Array.Empty<Operand>()),   // ADV entry: fast-forward starts from it
        };
        if (block) ops.Add((0x1f4, Array.Empty<Operand>()));
        for (int i = 0; i < probes; i++) ops.Add((0x1c7, new[] { G(Probe + i) }));
        if (block) ops.Add((0x1f5, Array.Empty<Operand>()));
        ops.Add((0x19a, new[] { G(0x300) }));
        ops.Add((0x1cb, new[] { G(0x301) }));
        ops.Add((0x2, Array.Empty<Operand>()));
        return ScriptAssembler.Assemble(table, name, ops, Array.Empty<string>());
    }

    private static Script BuildSettingCallback(OpcodeTable table)
        => ScriptAssembler.Assemble(table, "CALLBACK_SETTING", new List<(int, Operand[])>
        {
            (0x1c7, new[] { G(0x310) }),     // fast-forward state seen by the callback
            (0x55, new[] { G(0x311), I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());

    [Fact]
    public void PressThenReleaseDuringSkipCancelsSkipAndReadSkip()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = BuildProbeScript(table, "CANCEL_SKIP", 8);
        var host = new ClickHost();
        var vm = new VirtualMachine(script, table, host, new VmOptions(CancelMessageSkipOnClick: 1));
        host.Vm = vm;
        // Yield n follows instruction n-1. Fast-forward is on from boundary 3 (state 1); the press is held at
        // boundary 4 (state 2) and released before boundary 6, which cancels before probe 3 runs.
        host.AtYield[4] = v => v.UpdatePhysicalMouseButtonState(0, true);
        host.AtYield[6] = v => v.UpdatePhysicalMouseButtonState(0, false);

        vm.Run();

        for (int i = 0; i < 3; i++) Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + i));
        for (int i = 3; i < 8; i++) Assert.Equal(0, vm.Globals.GetValueOrDefault(Probe + i));
        Assert.False(vm.MessageSkipEnabled);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x300));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x301));
        Assert.False(host.MessageSkip);
    }

    [Fact]
    public void LatchedClickTriggerCancelsWithoutAHeldButton()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = BuildProbeScript(table, "CANCEL_SKIP_TRIGGER", 6);
        var host = new ClickHost();
        var vm = new VirtualMachine(script, table, host, new VmOptions(CancelMessageSkipOnClick: 1));
        host.Vm = vm;
        bool latched = false;
        host.AtYield[4] = v => latched = v.TryLatchMessageSkipCancelClick();

        vm.Run();

        Assert.True(latched);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + 0));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + 1));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(Probe + 2));
        Assert.False(vm.MessageSkipEnabled);
    }

    [Fact]
    public void SettingOffLeavesClicksToOrdinaryRouting()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = BuildProbeScript(table, "NO_CANCEL", 6);
        var host = new ClickHost();
        var vm = new VirtualMachine(script, table, host);
        host.Vm = vm;
        bool latched = true;
        host.AtYield[4] = v => { latched = v.TryLatchMessageSkipCancelClick(); v.UpdatePhysicalMouseButtonState(0, true); };
        host.AtYield[6] = v => v.UpdatePhysicalMouseButtonState(0, false);

        vm.Run();

        Assert.False(latched);
        for (int i = 0; i < 6; i++) Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + i));
        Assert.True(vm.MessageSkipEnabled);
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x301));
    }

    [Fact]
    public void ResetMessageSkipInputClearsThePressState()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "CANCEL_RESET", new List<(int, Operand[])>
        {
            (0x88, new[] { I(1) }),
            (0x19c, Array.Empty<Operand>()),
            (0x1c7, new[] { G(Probe) }),
            (0x1c7, new[] { G(Probe + 1) }),   // press held at the next boundary: state 2
            (0x101, Array.Empty<Operand>()),   // native ctx+0x6da80 = 0; it also ends fast-forward
            (0x19c, Array.Empty<Operand>()),   // fast-forward starts again from persistent Skip
            (0x1c7, new[] { G(Probe + 2) }),   // released here: state 0 only re-arms to 1
            (0x1c7, new[] { G(Probe + 3) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new ClickHost();
        var vm = new VirtualMachine(script, table, host, new VmOptions(CancelMessageSkipOnClick: 1));
        host.Vm = vm;
        host.AtYield[3] = v => v.UpdatePhysicalMouseButtonState(0, true);
        // Released once fast-forward runs again, where state 2 would have cancelled.
        host.AtYield[6] = v => v.UpdatePhysicalMouseButtonState(0, false);

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + 1));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + 2));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(Probe + 3));
        Assert.True(vm.MessageSkipEnabled);
    }

    [Fact]
    public void ModeTwoRunsSettingCallbackAfterTheStatementBlockCloses()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = BuildProbeScript(table, "CANCEL_SKIP_CALLBACK", 6, block: true);
        var callback = BuildSettingCallback(table);
        var provider = new MapProvider(new Dictionary<long, Script>(),
            new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase) { ["CALLBACK_SETTING.BIN"] = callback });
        var host = new ClickHost();
        var vm = new VirtualMachine(script, table, host, new VmOptions(CancelMessageSkipOnClick: 2), provider);
        host.Vm = vm;
        // Instructions: 0x1ca, 0x88, 0x19c, 0x1f4, six probes, 0x1f5, ...; cancel lands inside the block.
        host.AtYield[5] = v => v.UpdatePhysicalMouseButtonState(0, true);
        host.AtYield[7] = v => v.UpdatePhysicalMouseButtonState(0, false);
        int probesBeforeCallback = -1;
        host.AtYield[11] = v => probesBeforeCallback = (int)v.Globals.GetValueOrDefault(0x311);

        vm.Run();

        Assert.Equal(0, probesBeforeCallback);                    // deferred past the open block
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x311));     // ran after the close
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x310));     // fast-forward already off
        Assert.Equal(0, vm.Globals.GetValueOrDefault(Probe + 5));
        Assert.False(vm.MessageSkipEnabled);
    }

    [Fact]
    public void StartupSettingReadsCancelMesSkipOnClick()
    {
        Assert.Equal(2, Sys4StartupSettings.FromPairs(new[]
            { new KeyValuePair<string, string>("CANCELMESSKIPONCLICK", "2") }).CancelMessageSkipOnClick);
        Assert.Equal(0, Sys4StartupSettings.Empty.CancelMessageSkipOnClick);
        Assert.Equal(0, Sys4StartupSettings.FromPairs(new[]
            { new KeyValuePair<string, string>("CANCELMESSKIPONCLICK", "x") }).CancelMessageSkipOnClick);
    }

    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);
}

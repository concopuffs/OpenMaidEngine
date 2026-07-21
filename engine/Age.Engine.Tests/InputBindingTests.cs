using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class InputBindingTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);

    [Fact]
    public void NativeDefaultsExposeSevenKeyboardActionsAndMouseActionFour()
    {
        var bindings = new InputBindings();

        Assert.Equal(7, bindings.ActionCount);
        Assert.Equal(0, bindings.KeyboardAction(0x26)); // Up
        Assert.Equal(1, bindings.KeyboardAction(0x27)); // Right
        Assert.Equal(2, bindings.KeyboardAction(0x28)); // Down
        Assert.Equal(3, bindings.KeyboardAction(0x25)); // Left
        Assert.Equal(4, bindings.KeyboardAction(0x0d)); // Enter
        Assert.Equal(5, bindings.KeyboardAction(0x20)); // Space
        Assert.Equal(6, bindings.KeyboardAction(0x08)); // Backspace
        Assert.Equal(4, bindings.MouseAction(0));
        Assert.Equal(4, bindings.MouseAction(1));
    }

    [Fact]
    public void ConfigurationOpcodesMutateTheSharedPhysicalBindingService()
    {
        var script = ScriptAssembler.Assemble(Table, "INPUT_CONFIG", new List<(int, Operand[])>
        {
            (0xfe, new[] { I(10) }),
            (0x107, new[] { I(0), I(3) }),
            (0x10b, new[] { I(3), I(1) }),
            (0x10c, new[] { I(4), I(0x2c) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(10, vm.InputBindings.ActionCount);
        Assert.Equal(4, vm.InputBindings.KeyboardAction(0x5a));
        Assert.Equal(7, vm.InputBindings.MouseAction(1));
        Assert.NotEqual(0, vm.InputBindings.JoystickButtonActionMask(3) & (1 << 4));
    }

    [Fact]
    public void System4BootstrapReplaysAllSixteenInputConfigurationCalls()
    {
        var scripts = Sys4ScriptProvider.Load(Table);
        var bindings = new InputBindings();

        Assert.Equal(16, InputBindingBootstrap.Apply(
            scripts.RequireByName("SYSTEM4.BIN"), bindings));

        Assert.Equal(10, bindings.ActionCount);
        Assert.Equal(4, bindings.KeyboardAction(0x5a)); // Z plus retained Enter
        Assert.Equal(4, bindings.KeyboardAction(0x0d));
        Assert.Equal(5, bindings.KeyboardAction(0x20));
        Assert.Equal(6, bindings.KeyboardAction(0x08)); // retained Backspace
        Assert.Equal(6, bindings.KeyboardAction(0x11)); // Ctrl
        Assert.Equal(7, bindings.KeyboardAction(0x58)); // X
        Assert.Equal(8, bindings.KeyboardAction(0x21)); // PageUp
        Assert.Equal(9, bindings.KeyboardAction(0x22)); // PageDown
        Assert.Equal(7, bindings.MouseAction(1));
    }

    [Theory]
    [InlineData(0x08)] // retained native Backspace default
    [InlineData(0x11)] // SYSTEM4 LeftCtrl binding
    [InlineData(0x43)] // SYSTEM4 C binding
    public void LogicalActionSixDrivesHeldAdvFastForward(int virtualKey)
    {
        var script = ScriptAssembler.Assemble(Table, "ADV_FAST_FORWARD", new List<(int, Operand[])>
        {
            (0x19c, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, Table, host);
        InputBindingBootstrap.Apply(
            Sys4ScriptProvider.Load(Table).RequireByName("SYSTEM4.BIN"), vm.InputBindings);

        vm.UpdateKeyboardVirtualKeyState(virtualKey, true);
        Assert.False(host.PhysicalMessageSkip); // ADV lifecycle is not active yet.

        vm.Run();

        Assert.True(host.PhysicalMessageSkip);
        Assert.True(host.MessageSkip);

        vm.UpdateKeyboardVirtualKeyState(virtualKey, false);

        Assert.False(host.PhysicalMessageSkip);
        Assert.False(host.MessageSkip);
    }

    [Fact]
    public void ReleasingPhysicalFastForwardDoesNotClearPersistentSkip()
    {
        var script = ScriptAssembler.Assemble(Table, "ADV_PERSISTENT_AND_HELD_SKIP", new List<(int, Operand[])>
        {
            (0x19c, Array.Empty<Operand>()),
            (0x88, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, Table, host);
        InputBindingBootstrap.Apply(
            Sys4ScriptProvider.Load(Table).RequireByName("SYSTEM4.BIN"), vm.InputBindings);
        vm.Run();

        vm.UpdateKeyboardVirtualKeyState(0x11, true);
        vm.UpdateKeyboardVirtualKeyState(0x11, false);

        Assert.True(vm.MessageSkipEnabled);
        Assert.False(host.PhysicalMessageSkip);
        Assert.True(host.MessageSkip);
    }

    [Fact]
    public void EmptyPollDispatchesTheCallbackAtActionCount()
    {
        var script = ScriptAssembler.Assemble(Table, "INPUT_IDLE", new List<(int, Operand[])>
        {
            (0xfe, new[] { I(2) }),              // offsets 0..2
            (0xfb, new[] { I(2), I(11) }),       // offsets 3..7
            (0xff, Array.Empty<Operand>()),       // offset 8
            (0x100, Array.Empty<Operand>()),      // offset 9
            (0x2, Array.Empty<Operand>()),        // offset 10
            (0x55, new[] { G(0x700), I(1) }),    // offset 11
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x700));
    }

    [Fact]
    public void HeldActionsDispatchSimultaneouslyButOnlyBelowConfiguredCount()
    {
        var script = ScriptAssembler.Assemble(Table, "INPUT_MULTI", new List<(int, Operand[])>
        {
            (0xfe, new[] { I(2) }),               // offsets 0..2
            (0xfb, new[] { I(0), I(16) }),        // offsets 3..7
            (0xfb, new[] { I(1), I(22) }),        // offsets 8..12
            (0xff, Array.Empty<Operand>()),        // offset 13
            (0x100, Array.Empty<Operand>()),       // offset 14
            (0x2, Array.Empty<Operand>()),         // offset 15
            (0x55, new[] { G(0x701), I(1) }),     // offset 16
            (0x5, Array.Empty<Operand>()),         // offset 21
            (0x55, new[] { G(0x702), I(1) }),     // offset 22
            (0x5, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost());
        vm.UpdateInputCallbackState(0, true);
        vm.UpdateInputCallbackState(1, true);
        vm.UpdateInputCallbackState(3, true); // bit survives polling but is outside count and is ignored

        vm.Run();

        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x701));
        Assert.Equal(1, vm.Globals.GetValueOrDefault(0x702));
    }

    [Theory]
    [InlineData(0xfe, "input-action-count-out-of-range:32")]
    [InlineData(0x10c, "keyboard-action-out-of-range:32")]
    public void NativeValidatedActionOperandsRejectThirtyTwo(int opcode, string expected)
    {
        Operand[] args = opcode == 0xfe ? new[] { I(32) } : new[] { I(32), I(0x2c) };
        var script = ScriptAssembler.Assemble(Table, "INPUT_RANGE", new List<(int, Operand[])>
        {
            (opcode, args),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(expected, vm.HaltReason);
    }
}

using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class ExitRequestTests
{
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);

    [Fact]
    public void ThrowExitRequestDoesNotFallThroughToFollowingBytecode()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "TITLE.BIN", new List<(int, Operand[])>
        {
            (0x1, Array.Empty<Operand>()),
            (0x55, new[] { G(0x7000), I(1) }),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Equal("exit-request", vm.HaltReason);
        Assert.False(vm.Globals.ContainsKey(0x7000));
    }

    [Fact]
    public void ThrowExitRequestEscapesNestedScriptFrames()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var root = ScriptAssembler.Assemble(table, "SYSTEM4.BIN", new List<(int, Operand[])>
        {
            (0x3, new[] { I(1) }),
            (0x55, new[] { G(0x7001), I(1) }),
        }, Array.Empty<string>());
        var child = ScriptAssembler.Assemble(table, "TITLE.BIN", new List<(int, Operand[])>
        {
            (0x1, Array.Empty<Operand>()),
            (0x55, new[] { G(0x7002), I(1) }),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(root, table, new RecordingHost(), provider: new MapProvider(new()
        {
            [1] = child,
        }));

        vm.Run();

        Assert.Equal("exit-request", vm.HaltReason);
        Assert.False(vm.Globals.ContainsKey(0x7001));
        Assert.False(vm.Globals.ContainsKey(0x7002));
    }

    [Fact]
    public void DebugIgnoreExitRequestFallsThroughToFollowingBytecode()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "TITLE.BIN", new List<(int, Operand[])>
        {
            (0x1, Array.Empty<Operand>()),
            (0x55, new[] { G(0x7000), I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost(),
            new VmOptions(IgnoreExitRequests: true));

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[0x7000]);
    }
}

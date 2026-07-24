using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class AppendAutorunTests
{
    private static Operand I(long value) => new(0, value);
    private static Operand G(long address) => new(3, address);

    private sealed class MountedProvider : IScriptProvider
    {
        private readonly IReadOnlyDictionary<long, Script> _scripts;

        public MountedProvider(IReadOnlyList<int> selectors, IReadOnlyDictionary<long, Script> scripts)
        {
            MountedAppendSelectors = selectors;
            _scripts = scripts;
        }

        public IReadOnlyList<int> MountedAppendSelectors { get; }
        public List<long> Requests { get; } = new();

        public Script? GetById(long id)
        {
            Requests.Add(id);
            return _scripts.GetValueOrDefault(id);
        }
    }

    [Fact]
    public void RunMountedAppendAutoruns_ExecutesRecordZeroSeriallyBySelectorThenResumesCaller()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var first = ScriptAssembler.Assemble(table, "APPEND_ONE_AUTORUN", new List<(int, Operand[])>
        {
            (0x55, new[] { G(0x100), I(7) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var second = ScriptAssembler.Assemble(table, "APPEND_TWO_AUTORUN", new List<(int, Operand[])>
        {
            (0x50, new[] { G(0x100), G(0x100), I(5) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var root = ScriptAssembler.Assemble(table, "ROOT", new List<(int, Operand[])>
        {
            (0x143, Array.Empty<Operand>()),
            (0x55, new[] { G(0x101), G(0x100) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var provider = new MountedProvider(new[] { 2, 1, 2 }, new Dictionary<long, Script>
        {
            [0x01000000] = first,
            [0x02000000] = second,
        });
        var vm = new VirtualMachine(root, table, new RecordingHost(), provider: provider);

        vm.Run();

        Assert.Equal(new[] { 0x01000000L, 0x02000000L }, provider.Requests);
        Assert.Equal(12, vm.Globals.GetValueOrDefault(0x100));
        Assert.Equal(12, vm.Globals.GetValueOrDefault(0x101));
        Assert.Equal(2, vm.CallScriptDispatches);
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void RunMountedAppendAutoruns_HaltsWhenMountedRecordZeroIsNotAScript()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var root = ScriptAssembler.Assemble(table, "ROOT", new List<(int, Operand[])>
        {
            (0x143, Array.Empty<Operand>()),
            (0x55, new[] { G(0x100), I(1) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var provider = new MountedProvider(new[] { 1 }, new Dictionary<long, Script>());
        var vm = new VirtualMachine(root, table, new RecordingHost(), provider: provider);

        vm.Run();

        Assert.Equal(new[] { 0x01000000L }, provider.Requests);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x100));
        Assert.Equal("append-autorun-unresolved:0x1000000", vm.HaltReason);
    }
}

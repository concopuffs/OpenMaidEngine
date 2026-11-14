using Ome.Engine.Model;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// The handler table (VirtualMachine.Handlers.cs) is the only list of implemented opcodes
/// (docs/superpowers/specs/2026-11-12-engine-layering-design.md §4 and acceptance 3).
/// </summary>
public class OpcodeHandlerTableTests
{
    private static readonly OpcodeTable Catalog = OpcodeTableJson.Load(Paths.OpcodesJson);

    public static TheoryData<string> GameAbis()
    {
        var abis = new TheoryData<string>();
        foreach (string abi in GameProfileRegistry.BuiltIn.Profiles.Select(profile => profile.EngineAbiId).Distinct())
            abis.Add(abi);
        return abis;
    }

    [Theory]
    [MemberData(nameof(GameAbis))]
    public void EveryOpcodeOfAGamesTableHasAHandler(string abi)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, abi);
        Assert.True(table.Count > 200, $"{abi} has only {table.Count} opcodes");
        string[] missing = table.Entries
            .Where(definition => !VirtualMachine.Implements(table, definition.Opcode))
            .Select(definition => $"0x{definition.Opcode:x} {definition.CanonicalLabel}")
            .ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryHandlerIsBoundToAnOpcodesName()
    {
        var names = Catalog.Entries.Select(definition => definition.CanonicalLabel).ToHashSet(StringComparer.Ordinal);
        Assert.True(VirtualMachine.HandlerNames.Count > 250);
        Assert.Empty(VirtualMachine.HandlerNames.Where(name => !names.Contains(name)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NoTwoOpcodesShareAName()
    {
        // A handler is bound by name, so two opcodes with one name would run one handler.
        string[] shared = Catalog.Entries
            .GroupBy(definition => definition.CanonicalLabel, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(d => $"0x{d.Opcode:x}"))}")
            .ToArray();
        Assert.Empty(shared);
    }

    [Fact]
    public void AnOpcodeWithoutAHandlerHaltsWithADiagnostic()
    {
        // 0x4 is in the catalog and has no handler.
        Assert.True(Catalog.TryGetDefinition(0x4, out _));
        Assert.False(VirtualMachine.Implements(Catalog, 0x4));
        Script script = ScriptAssembler.Assemble(Catalog, "UNSUPPORTED.BIN", new[]
        {
            (0x4, Enumerable.Repeat(new Operand(0, 0), Catalog.Argc(0x4)).ToArray()),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Catalog, new RecordingWaits());

        vm.Run();

        Assert.NotNull(vm.CompatibilityFailure);
        Assert.Equal(0x4, vm.CompatibilityFailure!.Opcode);
    }
}

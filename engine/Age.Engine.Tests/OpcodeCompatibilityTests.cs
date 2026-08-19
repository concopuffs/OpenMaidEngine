using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class OpcodeCompatibilityTests
{
    [Fact]
    public void RuntimeCoverageIsIndependentFromAbiMembershipAndCorpusObservation()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");

        Assert.True(table.TryGetDefinition(0x1be, out OpcodeDefinition definition));
        Assert.False(definition.IsObservedBy("himegari"));
        Assert.True(definition.IsObservedBy("kamidori"));
        Assert.False(OpcodeRuntimeCoverage.IsImplemented(0x1be));
        Assert.Equal("SYS4433", table.AbiId);
        IReadOnlySet<int> himegari = table.Observations["himegari"].Opcodes;
        IReadOnlySet<int> kamidori = table.Observations["kamidori"].Opcodes;
        Assert.Equal(248, himegari.Count);
        Assert.Equal(269, kamidori.Count);
        Assert.Equal(242, himegari.Intersect(kamidori).Count());
        Assert.Equal(275, himegari.Union(kamidori).Count());
        Assert.All(
            table.Entries.Where(entry => entry.IsObservedBy("himegari")),
            entry => Assert.True(
                OpcodeRuntimeCoverage.IsImplemented(entry.Opcode),
                $"Himegari-observed opcode 0x{entry.Opcode:x} would enter unsupported fallback"));
    }

    [Fact]
    public void OpcodeAbsentFromSelectedAbiProducesStructuredDecodeFailure()
    {
        var table = new OpcodeTable(
            new Dictionary<int, (string Label, int Argc)> { [0x2] = ("exit", 0) },
            "TEST-ABI");
        byte[] bytes = ScriptAssembler.AssembleBytes(
            new[] { (0x1be, Array.Empty<Operand>()) }, Array.Empty<string>());

        var error = Assert.Throws<Sys4OpcodeDecodeException>(
            () => Sys4Loader.Parse(bytes, table, "MISSING.BIN", 0x1234));

        Assert.Equal(Sys4OpcodeDecodeFailure.OpcodeAbsentFromAbi, error.Failure);
        Assert.Equal("TEST-ABI", error.EngineAbiId);
        Assert.Equal("SYS4422", error.ScriptRevision);
        Assert.Equal("MISSING.BIN", error.ScriptName);
        Assert.Equal(0x1234u, error.PackedScriptId);
        Assert.Equal(0, error.Offset);
        Assert.Equal(0x1be, error.Opcode);
    }

    [Fact]
    public void NormalModeHaltsAndProbeModeTracesThenAdvancesAtUnsupportedOpcode()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Script script = ScriptAssembler.Assemble(table, "FRONTIER.BIN", new[]
        {
            (0x1be, new[] { new Operand(0, 11), new Operand(0, 22) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());

        var normalTrace = new RecordingTraceSink();
        var normal = new VirtualMachine(
            script, table, new RecordingHost(), sink: normalTrace,
            compatibility: new VmCompatibilityContext("kamidori", "SYS4433"));
        normal.Run();

        UnsupportedOpcodeDiagnostic failure = Assert.IsType<UnsupportedOpcodeDiagnostic>(
            normal.CompatibilityFailure);
        Assert.Equal("kamidori", failure.ProfileId);
        Assert.Equal("SYS4433", failure.EngineAbiId);
        Assert.Equal("SYS4422", failure.ScriptRevision);
        Assert.Equal("FRONTIER.BIN", failure.ScriptName);
        Assert.Equal(0, failure.Offset);
        Assert.Equal(0x1be, failure.Opcode);
        Assert.Equal("u0041D9D0", failure.CanonicalLabel);
        Assert.Equal(new long[] { 11, 22 }, failure.Operands.Select(value => value.Value));
        Assert.Contains(normalTrace.Events,
            item => item.Kind == TraceEventKind.UnsupportedOpcode
                    && item.CompatibilityDiagnostic == failure);

        var probeTrace = new RecordingTraceSink();
        var probe = new VirtualMachine(
            script, table, new RecordingHost(), sink: probeTrace,
            compatibility: new VmCompatibilityContext("kamidori", "SYS4433", ProbeMode: true));
        probe.Run();

        Assert.Null(probe.CompatibilityFailure);
        Assert.Equal("exit", probe.HaltReason);
        Assert.Contains(probeTrace.Events,
            item => item.Kind == TraceEventKind.UnsupportedOpcode
                    && item.CompatibilityDiagnostic?.Opcode == 0x1be);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void KamidoriTitleStopsAtFirstKnownUnsupportedFrontier()
    {
        string gameRoot = Path.Combine(Paths.Workspace, "Kamidori");
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");

        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var scripts = new Sys4ScriptProvider(
            table, catalog, new Sys4AssetStore(catalog, gameRoot, gameRoot));
        Script title = scripts.RequireByName("TITLE.BIN");
        var vm = new VirtualMachine(
            title, table, new CaptureHost(), new VmOptions(MaxSteps: 100_000), scripts,
            compatibility: new VmCompatibilityContext("kamidori", "SYS4433"));

        vm.Run();

        UnsupportedOpcodeDiagnostic failure = Assert.IsType<UnsupportedOpcodeDiagnostic>(
            vm.CompatibilityFailure);
        Assert.Equal("SYS4433", title.EngineRevision);
        Assert.Equal("TITLE.BIN", failure.ScriptName);
        Assert.Equal(0xd7, failure.Offset);
        Assert.Equal(0x1be, failure.Opcode);
    }
}

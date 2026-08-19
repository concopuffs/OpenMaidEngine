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
        OpcodeTable himegariTable = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");

        Assert.True(table.TryGetDefinition(0x1be, out OpcodeDefinition definition));
        Assert.False(definition.IsObservedBy("himegari"));
        Assert.True(definition.IsObservedBy("kamidori"));
        Assert.True(OpcodeRuntimeCoverage.IsImplemented(0x1be));
        Assert.Equal("SYS4433", table.AbiId);
        IReadOnlySet<int> himegari = table.Observations["himegari"].Opcodes;
        IReadOnlySet<int> kamidori = table.Observations["kamidori"].Opcodes;
        Assert.Equal(248, himegari.Count);
        Assert.Equal(269, kamidori.Count);
        Assert.Equal(242, himegari.Intersect(kamidori).Count());
        Assert.Equal(275, himegari.Union(kamidori).Count());
        Assert.All(
            himegariTable.Entries,
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
    public void NewlyImplementedCrossRevisionOpcodeRunsWithoutCompatibilityFallback()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Script script = ScriptAssembler.Assemble(table, "FRONTIER.BIN", new[]
        {
            (0x1be, new[] { new Operand(3, 0x100), new Operand(0, 11) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());

        var normalTrace = new RecordingTraceSink();
        var host = new RecordingHost();
        host.PlayingSoundChannels.Add(11);
        var normal = new VirtualMachine(
            script, table, host, sink: normalTrace,
            compatibility: new VmCompatibilityContext("kamidori", "SYS4433"));
        normal.Run();

        Assert.Null(normal.CompatibilityFailure);
        Assert.Equal("exit", normal.HaltReason);
        Assert.Equal(1, normal.Globals[0x100]);
        Assert.DoesNotContain(normalTrace.Events,
            item => item.Kind == TraceEventKind.UnsupportedOpcode);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void KamidoriTitleEntersSupportedIdleLoop()
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

        Assert.Null(vm.CompatibilityFailure);
        Assert.Equal("SYS4433", title.EngineRevision);
        Assert.Equal("STEP-LIMIT", vm.HaltReason);
        Assert.Equal(100_000, vm.Steps);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void KamidoriDrawchpExtendedNumericStyleFrontierExecutesWithNativeSpacing()
    {
        string gameRoot = Path.Combine(Paths.Workspace, "Kamidori");
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");

        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var scripts = new Sys4ScriptProvider(
            table, catalog, new Sys4AssetStore(catalog, gameRoot, gameRoot));
        Script drawchp = scripts.RequireByName("DRAWCHP.BIN");
        Instruction frontier = Assert.Single(
            drawchp.Instructions.Where(instruction => instruction.Offset == 0x626));
        Assert.Equal(0x2da, frontier.Opcode);
        Assert.Equal(
            new long[] { 0, 0x40, 0x39f, 0x2fe, 0x15, 0x1d, 1, 1 },
            frontier.Args.Select(argument => argument.Value));

        var probe = ScriptAssembler.Assemble(table, "DRAWCHP-0X2DA-FRONTIER",
            new[]
            {
                (frontier.Opcode, frontier.Args.ToArray()),
                (0x23b, new[]
                {
                    new Operand(0, 500), new Operand(0, 0), new Operand(0, 7),
                    new Operand(0, 10), new Operand(0, 20), new Operand(0, 1),
                    new Operand(0, 0),
                }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var vm = new VirtualMachine(
            probe, table, new RecordingHost(),
            compatibility: new VmCompatibilityContext("kamidori", "SYS4433"));

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Null(vm.CompatibilityFailure);
        RenderObject visible = Assert.Single(vm.Gfx.SnapshotVisibleObjects());
        Assert.Equal((500L, 0x39f + 7 * (0x15 + 1), 10),
            (visible.Handle, visible.SrcX, visible.DstX));
        Assert.True(OpcodeRuntimeCoverage.IsImplemented(0x2da));
        Assert.True(table.TryGetDefinition(0x2da, out OpcodeDefinition definition));
        Assert.Equal("register-extended-numeric-glyph-style", definition.SemanticName);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void KamidoriArrayFillContractIsAnchoredInCombatAndEnemySetupScripts()
    {
        string gameRoot = Path.Combine(Paths.Workspace, "Kamidori");
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");

        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var scripts = new Sys4ScriptProvider(
            table, catalog, new Sys4AssetStore(catalog, gameRoot, gameRoot));
        Instruction calcdmg = Assert.Single(
            scripts.RequireByName("CALCDMG.BIN").Instructions,
            instruction => instruction.Offset == 0x2ff);
        Instruction seten = Assert.Single(
            scripts.RequireByName("SETEN.BIN").Instructions,
            instruction => instruction.Offset == 0xb4);

        Assert.Equal(0x2d8, calcdmg.Opcode);
        Assert.Equal(new[]
        {
            new Operand(12, 0), new Operand(0, 1), new Operand(0, 17),
        }, calcdmg.Args);
        Assert.Equal(0x2d8, seten.Opcode);
        Assert.True(table.TryGetDefinition(0x2d8, out OpcodeDefinition definition));
        Assert.Equal("fill-int-array", definition.SemanticName);
        Assert.True(OpcodeRuntimeCoverage.IsImplemented(0x2d8));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void KamidoriPatternRevealCorpusUsesTheRecoveredNativeModes()
    {
        string gameRoot = Path.Combine(Paths.Workspace, "Kamidori");
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");

        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var scripts = new Sys4ScriptProvider(
            table, catalog, new Sys4AssetStore(catalog, gameRoot, gameRoot));
        List<Instruction> calls = catalog.EnumerateScripts()
            .Select(entry => scripts.GetById(entry.PackedId))
            .Where(script => script != null)
            .SelectMany(script => script!.Instructions)
            .Where(instruction => instruction.Opcode is 0x27 or 0x28)
            .ToList();

        Instruction striped = Assert.Single(calls, instruction => instruction.Opcode == 0x27);
        Assert.Equal(new long[] { 2, 40, 64, 3 },
            striped.Args.Select(argument => argument.Value));
        List<Instruction> staggered = calls.Where(instruction => instruction.Opcode == 0x28).ToList();
        Assert.Equal(192, staggered.Count);
        Assert.All(staggered, instruction =>
        {
            long[] operands = instruction.Args.Select(argument => argument.Value).ToArray();
            Assert.Equal(2, operands[0]);
            Assert.Contains(operands[1], new long[] { 10, 15 });
            Assert.Equal(64, operands[2]);
            Assert.InRange(operands[3], 0, 3);
        });
        Assert.Equal(new long[] { 0, 1, 2, 3 }, staggered
            .Select(instruction => instruction.Args[3].Value).Distinct().OrderBy(value => value));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void KamidoriDirectionalBlurCorpusUsesTheRecoveredTenOperandContract()
    {
        string gameRoot = Path.Combine(Paths.Workspace, "Kamidori");
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");

        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        var scripts = new Sys4ScriptProvider(
            table, catalog, new Sys4AssetStore(catalog, gameRoot, gameRoot));
        List<Instruction> calls = catalog.EnumerateScripts()
            .Select(entry => scripts.GetById(entry.PackedId))
            .Where(script => script != null)
            .SelectMany(script => script!.Instructions)
            .Where(instruction => instruction.Opcode == 0x250)
            .ToList();

        Assert.Equal(34, calls.Count);
        Assert.All(calls, instruction =>
            Assert.Equal(new[] { 9, 3, 3, 0, 9, 9, 9, 9, 0, 0 },
                instruction.Args.Select(argument => argument.Type)));
        Assert.Equal(11, catalog.EnumerateScripts()
            .Select(entry => scripts.GetById(entry.PackedId))
            .Where(script => script?.Instructions.Any(instruction => instruction.Opcode == 0x250) == true)
            .Count());
    }
}

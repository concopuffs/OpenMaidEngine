using System.Buffers.Binary;
using Ome.Engine.Model;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>
/// <see cref="InstallIntake"/>, the first look at an install that has no profile yet (docs/adding-a-game.md, step 1),
/// over synthetic installs: a catalog revision no profile names, whose trailer holds a block the reader does not
/// know, and scripts of a revision no ABI snapshot covers. The installed games check the counts against the
/// registry's observations.
/// </summary>
public sealed class InstallIntakeTests : IDisposable
{
    private const string Revision = "S4IC499 ", Title = "Synthetic Third", ScriptAbi = "SYS4499";
    /// <summary>An opcode of the catalog that no handler binds, and one the port runs. The first test asserts both
    /// premises, so a handler for the first one fails it plainly.</summary>
    private const int Handled = 0x5;

    private static readonly OpcodeTable Catalog = OpcodeTableJson.LoadEmbedded();

    /// <summary>The first opcode of the catalog that takes operands, has no handler and is in no game's scripts.</summary>
    private static readonly int Unhandled = Catalog.Entries.Select(definition => definition.Opcode).Order()
        .First(opcode => Catalog.Argc(opcode) > 0 && !VirtualMachine.Implements(Catalog, opcode)
                         && Catalog.Observations.Values.All(observation => !observation.Opcodes.Contains(opcode)));
    private static readonly HashSet<int> OperandTypes = [0x0, 0x2, 0x3];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-intake-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Install(string name, byte[] catalog, params (string Name, byte[] Bytes)[] files)
    {
        string install = Path.Combine(_root, name);
        Directory.CreateDirectory(install);
        File.WriteAllBytes(Path.Combine(install, "SYS4INI.BIN"), catalog);
        foreach ((string file, byte[] bytes) in files) File.WriteAllBytes(Path.Combine(install, file), bytes);
        return install;
    }

    private static byte[] Script(params (int Op, Operand[] Args)[] instructions)
        => ScriptAssembler.AssembleBytes(instructions, [], ScriptAbi);

    private static (int, Operand[]) Instruction(int opcode)
        => (opcode, Enumerable.Repeat(new Operand(0, 0), Catalog.Argc(opcode)).ToArray());

    private static InstallIntakeReport Intake(string install, params (string Name, string Root)[] compare)
        => InstallIntake.Run(install, Catalog, opcode => VirtualMachine.Implements(Catalog, opcode), OperandTypes,
                             GameProfileRegistry.BuiltIn, compare);

    /// <summary>The font list of AGE 4.38D's catalogs: one record, file 1 as the face "AGE Extend".</summary>
    private static readonly byte[] FontList = [0x0f, 0, 0, 0, 1, 0, 0, 0, .. "AGE Extend\0"u8];

    /// <summary>A catalog whose trailer goes on after the settings pairs, as a later catalog revision's does.</summary>
    private static byte[] CatalogWithTrailer(byte[] afterSettings, IReadOnlyList<string> files,
                                             params (string Key, string Value)[] pairs)
        => SyntheticCatalog.Wrap([.. SyntheticCatalog.Expanded(pairs, includeTrailer: true, files), .. afterSettings], Revision, Title);

    [Fact]
    public void AnInstallWithNoProfileIsReadWholeAndWhatIsNewIsListed()
    {
        Assert.False(VirtualMachine.Implements(Catalog, Unhandled));
        Assert.True(VirtualMachine.Implements(Catalog, Handled));
        byte[] unknown = [0x04, 0, 0, 0, .. "NEW\0"u8];
        byte[] catalog = CatalogWithTrailer([.. FontList, .. unknown], ["A.BIN", "FONT.TTF", "PIC.AGF"],
                                            ("SCREENX", "1024"), ("NEWKEY", "1"));
        string install = Install("third", catalog,
            ("A.BIN", Script(Instruction(Handled), Instruction(Unhandled), Instruction(Unhandled))), ("AGE.EXE", [1, 2, 3]));
        string other = Install("known", SyntheticCatalog.Build(["B.BIN", "PIC.AGF"], [("SCREENX", "800")], "S4IC422 ", "Known"));

        // The engine's own reader refuses the block, so a game is never run past bytes nobody has reversed.
        Assert.Contains("trailing bytes", Assert.Throws<InvalidDataException>(() => Sys4AssetCatalog.Parse(catalog)).Message);

        InstallIntakeReport report = Intake(install, ("known", other), ("itself", install), ("absent", Path.Combine(_root, "none")));

        Assert.Equal(("S4IC499", Title, null), (report.CatalogRevision, report.Title, report.MatchingProfile));
        Assert.Equal(("AGE.EXE", 3L), (report.Executable!.Name, report.Executable.Size));
        Assert.Null(report.CatalogProblem);
        Assert.Equal((3, 0), (report.Entries, report.Placeholders));
        Assert.Equal(new IntakeFont(1, "FONT.TTF", "AGE Extend"), Assert.Single(report.Fonts));
        Assert.Equal("8 bytes follow the font list", report.TrailerProblem);
        Assert.Equal((Convert.ToHexString(unknown).ToLowerInvariant(), "....NEW."),
                     (report.UnreadTrailerHex, report.UnreadTrailerText));
        Assert.Equal(new[] { "SCREENX", "NEWKEY" }, report.Settings.Select(setting => setting.Key));

        Assert.Equal(new[] { "known" }, report.ComparedWith.Select(compared => compared.Name));
        Assert.Equal(new[] { "NEWKEY" }, report.SettingsKeysNoComparedInstallHas);
        Assert.Equal(new[] { ".TTF" }, report.FileTypesNoComparedInstallHas);

        Assert.Equal(("catalog", 1, 3L), (report.ScriptSource, report.Scripts, report.Instructions));
        Assert.Equal(new Dictionary<string, int> { [ScriptAbi] = 1 }, report.ScriptRevisions);
        Assert.Empty(report.Failures);
        IntakeOpcode missing = Assert.Single(report.WithoutHandler);
        Assert.Equal(($"0x{Unhandled:x}", 2L, 1), (missing.Opcode, missing.Occurrences, missing.ScriptCount));
        Assert.Equal(new IntakeScriptCount("A.BIN", 2), Assert.Single(missing.TopScripts));
        Assert.Contains($"0x{Unhandled:x}", report.UsedByNoProfile);
        Assert.All(report.Checks, check => Assert.True(check.Passed, check.Detail));
        Assert.True(report.SameGeneration);
    }

    [Fact]
    public void AnOpcodeOutsideTheCatalogOrAnUnknownOperandTypeFailsItsCheck()
    {
        const int outside = 0x7ff0;
        Assert.False(Catalog.TryGetDefinition(outside, out _));
        string install = Install("other-generation", SyntheticCatalog.Build(["A.BIN", "B.BIN"], Revision, Title),
            ("A.BIN", Script((outside, []))),
            ("B.BIN", Script((Handled, []), (Handled, []))));
        // A script that decodes, with an operand type tag the registry does not name.
        string tagged = Install("tagged", SyntheticCatalog.Build(["C.BIN"], Revision, Title),
            ("C.BIN", Script((Unhandled, Enumerable.Repeat(new Operand(0x7777, 0), Catalog.Argc(Unhandled)).ToArray()))));

        InstallIntakeReport report = Intake(install);
        Assert.Equal("A.BIN", Assert.Single(report.Failures).Script);
        Assert.False(report.Checks.Single(check => check.Name == "whole-catalog decode").Passed);
        Assert.False(report.SameGeneration);

        InstallIntakeReport taggedReport = Intake(tagged);
        Assert.Equal(new[] { "0x7777" }, taggedReport.UnknownOperandTypes);
        Assert.False(taggedReport.Checks.Single(check => check.Name == "operand types").Passed);
        Assert.False(taggedReport.SameGeneration);
    }

    [Fact]
    public void ACatalogThatDoesNotReadLeavesTheLooseScripts()
    {
        byte[] catalog = SyntheticCatalog.Build(["A.BIN"], Revision, Title);
        BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(0x134, 4), 0x00ffffff);   // a packed size past the file
        string install = Install("unread", catalog, ("A.BIN", Script(Instruction(Handled))), ("NOTES.BIN", [0, 1, 2, 3]));

        InstallIntakeReport report = Intake(install);

        Assert.NotNull(report.CatalogProblem);
        Assert.Equal(("loose files", 1, 1L), (report.ScriptSource, report.Scripts, report.Instructions));
        Assert.False(report.Checks.Single(check => check.Name == "catalog signature").Passed);
        Assert.True(report.Checks.Single(check => check.Name == "whole-catalog decode").Passed);
        Assert.False(report.SameGeneration);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void InstalledHimegariReadsAsItsObservationRecordsIt() => AssertInstalled("himegari");

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void InstalledKamidoriReadsAsItsObservationRecordsIt() => AssertInstalled("kamidori");

    /// <summary>A game still being added: its scripts use opcodes the port has no handler for yet, and the intake
    /// lists them.</summary>
    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "arterial")]
    public void InstalledArterialReadsAsItsObservationRecordsIt() => AssertInstalled("arterial", complete: false);

    /// <summary>A supported game's install: the intake names its profile, finds nothing new, and counts what the
    /// registry's observation counts (vm-map/opcodes.toml), though it decodes with the whole catalog.</summary>
    private static void AssertInstalled(string game, bool complete = true)
    {
        string install = Paths.GameInstall(game);
        Assert.True(File.Exists(Path.Combine(install, "SYS4INI.BIN")), $"{game} install not found at {install}");
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        OpcodeCorpusObservation observation = table.Observations[game];

        InstallIntakeReport report = InstallIntake.Run(install, table, opcode => VirtualMachine.Implements(table, opcode),
                                                       new HashSet<int>(), GameProfileRegistry.BuiltIn, []);

        Assert.Equal(game, report.MatchingProfile);
        Assert.Null(report.CatalogProblem);
        Assert.Null(report.TrailerProblem);
        Assert.Empty(report.Failures);
        if (complete) Assert.Empty(report.WithoutHandler);
        else Assert.NotEmpty(report.WithoutHandler);
        Assert.Equal((observation.ScriptCount, observation.InstructionCount), (report.Scripts, report.Instructions));
        Assert.Equal(observation.Opcodes.Order().Select(opcode => $"0x{opcode:x}"), report.OpcodeOccurrences.Keys);
        Assert.Equal(GameProfileRegistry.BuiltIn.Find(game)!.Persistence.BankDimensions.ToArray(), report.SavedPrefix);
    }
}

using System.Text;
using Age.Engine.Persistence;
using Age.Engine.Profiles;
using Age.Engine.Sys4;

public sealed class GameProfileSelectionTests
{
    private static readonly GameCatalogIdentity HimegariIdentity =
        new("S4IC422", "姫狩りダンジョンマイスター");
    private static readonly GameCatalogIdentity KamidoriIdentity =
        new("S4IC433", "神採りアルケミーマイスター");

    [Fact]
    public void BuiltInManifestsExposeGameIdentityAndEngineAbiWithoutContentPaths()
    {
        GameProfileRegistry registry = GameProfileRegistry.BuiltIn;

        GameProfileManifest himegari = Assert.IsType<GameProfileManifest>(registry.Find("himegari"));
        Assert.Equal("SYS4", himegari.SysFrontendId);
        Assert.Equal("SYS4422", himegari.EngineAbiId);
        Assert.Equal(["SYS4422"], himegari.ScriptRevisions);
        Assert.Equal("SYSTEM4.BIN", himegari.NaturalBootScript);
        Assert.Equal("himegari", himegari.PersistenceNamespace);
        Assert.True(himegari.PersistenceWritesEnabled);
        Assert.Equal(310, himegari.Persistence.ExpectedPackedSaveVersion);
        Assert.Equal(0x4a343234u, himegari.Persistence.SharedCompatibilityId);
        Assert.Equal(0x42323234u, himegari.Persistence.NumberedCompatibilityId);
        Assert.Equal([402459, 1, 789, 1, 1, 1], himegari.Persistence.BankDimensions.ToArray());
        Assert.Equal(0x2d4, himegari.Persistence.NumberedGfxRecordSize);
        Assert.Equal("res://config/japanese-text-rendering.json",
            himegari.MetadataReferences["portableTextRendering"]);
        Assert.Equal([new ProfileCellSeed(0x62425, 1)], himegari.Runtime.ExternalGlobalSeeds);
        Assert.Equal(0xaba5c, himegari.Runtime.SceneEntryCoroutineGateAddress);
        DirectSceneDiagnosticPolicy himegariDiagnostics =
            Assert.IsType<DirectSceneDiagnosticPolicy>(himegari.DirectSceneDiagnostics);
        Assert.Equal("SYSTEM4.BIN", himegariDiagnostics.SystemScript);
        Assert.Equal(["INITCONFIG.BIN", "INIT2.BIN", "INIT.BIN"],
            himegariDiagnostics.DataBootstrapScripts);
        Assert.Equal([
            "SKINIT.BIN", "ITINIT.BIN", "EBINIT.BIN", "CGINIT.BIN", "MPINIT.BIN",
            "AFINIT.BIN", "CCINIT.BIN", "STINIT.BIN", "STINIT2.BIN",
        ], himegariDiagnostics.DataTableBootstrapScripts);
        Assert.Equal([new ProfileCellSeed(0x6c1, 1)], himegariDiagnostics.GlobalSeeds);
        DebugSceneLaunchPolicy debugLaunch =
            Assert.IsType<DebugSceneLaunchPolicy>(himegari.DebugSceneLaunch);
        Assert.Equal("SYSTEM4.BIN", debugLaunch.RootScript);
        Assert.Equal("TITLE.BIN", debugLaunch.CoordinatorScript);
        Assert.Equal(0x699, debugLaunch.PackedScriptIdAddress);
        Assert.True(himegari.Matches(HimegariIdentity));

        GameProfileManifest kamidori = Assert.IsType<GameProfileManifest>(registry.Find("kamidori"));
        Assert.Equal("SYS4", kamidori.SysFrontendId);
        Assert.Equal("SYS4433", kamidori.EngineAbiId);
        Assert.Equal(["SYS4433"], kamidori.ScriptRevisions);
        Assert.Equal("SYSTEM4.BIN", kamidori.NaturalBootScript);
        Assert.Equal("kamidori", kamidori.PersistenceNamespace);
        Assert.False(kamidori.PersistenceWritesEnabled);
        Assert.Equal(320, kamidori.Persistence.ExpectedPackedSaveVersion);
        Assert.Equal(0x46333334u, kamidori.Persistence.SharedCompatibilityId);
        Assert.Equal(0x46333334u, kamidori.Persistence.NumberedCompatibilityId);
        Assert.Equal([1037327, 1, 802, 1, 1, 1], kamidori.Persistence.BankDimensions.ToArray());
        Assert.Equal(0x2e4, kamidori.Persistence.NumberedGfxRecordSize);
        Assert.Contains("0x2e4", kamidori.Persistence.ReadOnlyReason);
        Assert.Empty(kamidori.Runtime.ExternalGlobalSeeds);
        Assert.Null(kamidori.Runtime.SceneEntryCoroutineGateAddress);
        Assert.Null(kamidori.DirectSceneDiagnostics);
        Assert.Null(kamidori.DebugSceneLaunch);
        Assert.True(kamidori.Matches(KamidoriIdentity));
    }

    [Fact]
    public void NormalKamidoriRuntimePolicyInjectsNoHimegariState()
    {
        GameProfileManifest kamidori = Assert.IsType<GameProfileManifest>(
            GameProfileRegistry.BuiltIn.Find("kamidori"));
        var externalGlobals = new Dictionary<int, long>();

        kamidori.Runtime.ApplyExternalGlobals(externalGlobals);

        Assert.Empty(externalGlobals);
        Assert.False(externalGlobals.ContainsKey(0x62425));
        Assert.Null(kamidori.DirectSceneDiagnostics);
        Assert.Null(kamidori.DebugSceneLaunch);
        Assert.False(kamidori.PersistenceWritesEnabled);
    }

    [Theory]
    [InlineData("S4IC422", "姫狩りダンジョンマイスター", "himegari", true)]
    [InlineData("S4IC433", "神採りアルケミーマイスター", "kamidori", false)]
    public void AutomaticSelectionRequiresOneExactIdentityMatch(
        string revision, string title, string expectedProfile, bool writesEnabled)
    {
        SelectedGameProfile selected = GameProfileSelection.Resolve(
            [], new GameCatalogIdentity(revision, title));

        Assert.Equal(expectedProfile, selected.Profile.Id);
        Assert.Equal(GameProfileSelectionSource.Automatic, selected.Source);
        Assert.True(selected.IdentityMatched);
        Assert.False(selected.ProbeMode);
        Assert.Equal(writesEnabled, selected.PersistenceWritesEnabled);
    }

    [Fact]
    public void ExplicitCrossPairContinuesWithRequestedProfileAndRecordsMismatch()
    {
        SelectedGameProfile selected = GameProfileSelection.Resolve(
            ["--profile", "kamidori"], HimegariIdentity);

        Assert.Equal("kamidori", selected.Profile.Id);
        Assert.Equal(GameProfileSelectionSource.Explicit, selected.Source);
        Assert.False(selected.IdentityMatched);
        Assert.False(selected.ProbeMode);
        Assert.True(selected.IsReadOnly);
        Assert.Contains("requested=kamidori", selected.MismatchDiagnostic);
        Assert.Contains("detected=S4IC422 / 姫狩りダンジョンマイスター", selected.MismatchDiagnostic);
    }

    [Fact]
    public void ExplicitProfileUsesLastValueAndMatchesIdsCaseInsensitively()
    {
        SelectedGameProfile selected = GameProfileSelection.Resolve(
            ["--profile", "himegari", "--profile", "KAMIDORI"], KamidoriIdentity);

        Assert.Equal("kamidori", selected.Profile.Id);
        Assert.True(selected.IdentityMatched);
        Assert.Equal(GameProfileSelectionSource.Explicit, selected.Source);
    }

    [Fact]
    public void ProbeModeRequiresAnExplicitAbiAndSuppressesWrites()
    {
        var missingProfile = Assert.Throws<ArgumentException>(
            () => GameProfileSelection.Resolve(["--probe"], HimegariIdentity));
        Assert.Contains("--probe requires --profile <id>", missingProfile.Message);

        SelectedGameProfile selected = GameProfileSelection.Resolve(
            ["--profile", "himegari", "--probe"], HimegariIdentity);
        Assert.True(selected.IdentityMatched);
        Assert.True(selected.ProbeMode);
        Assert.True(selected.IsReadOnly);
        Assert.Equal("probe mode", selected.ReadOnlyReason);
    }

    [Fact]
    public void UnknownAutomaticIdentityFailsBeforeRuntimeConstruction()
    {
        var error = Assert.Throws<ArgumentException>(() => GameProfileSelection.Resolve(
            [], new GameCatalogIdentity("S4IC999", "Unknown Game")));

        Assert.Contains("no profile matches S4IC999 / Unknown Game", error.Message);
        Assert.Contains("--profile <id>", error.Message);
    }

    [Fact]
    public void AmbiguousAutomaticIdentityRequiresAnExplicitChoice()
    {
        var registry = new GameProfileRegistry([
            SyntheticProfile("first", HimegariIdentity),
            SyntheticProfile("second", HimegariIdentity),
        ]);

        var error = Assert.Throws<ArgumentException>(
            () => GameProfileSelection.Resolve([], HimegariIdentity, registry));

        Assert.Contains("multiple profiles match", error.Message);
        Assert.Contains("first, second", error.Message);
    }

    [Theory]
    [InlineData("--profile")]
    [InlineData("--profile", "--probe")]
    public void MissingExplicitProfileIdIsRejected(params string[] arguments)
    {
        var error = Assert.Throws<ArgumentException>(
            () => GameProfileSelection.Resolve(arguments, HimegariIdentity));

        Assert.Contains("--profile requires a profile id", error.Message);
    }

    [Fact]
    public void UnknownExplicitProfileIdIsRejectedWithoutChangingTheRequest()
    {
        var error = Assert.Throws<ArgumentException>(() => GameProfileSelection.Resolve(
            ["--profile", "not-a-profile"], HimegariIdentity));

        Assert.Contains("unknown profile 'not-a-profile'", error.Message);
        Assert.Contains("himegari, kamidori", error.Message);
    }

    [Fact]
    public void IdentityHeaderReaderNormalizesPaddingAndDecodesCp932Title()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "age-profile-tests", Guid.NewGuid().ToString("N"), "SYS4INI.BIN");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var header = new byte[264];
            Encoding.ASCII.GetBytes("S4IC422 ").CopyTo(header, 0);
            Encoding.GetEncoding(932).GetBytes("姫狩りダンジョンマイスター").CopyTo(header, 8);
            File.WriteAllBytes(path, header);

            Assert.Equal(HimegariIdentity, GameCatalogIdentity.ReadSys4IniHeader(path));
        }
        finally
        {
            string directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [Trait("Category", "Workspace")]
    [InlineData("Himegari_Game", "himegari")]
    [InlineData("Kamidori", "kamidori")]
    public void InstalledCatalogAutoSelectsExpectedProfile(string directory, string expectedProfile)
    {
        string path = Path.Combine(Paths.Workspace, directory, "SYS4INI.BIN");
        Assert.True(File.Exists(path), $"installed-data profile gate requires {path}");

        GameCatalogIdentity identity = GameCatalogIdentity.ReadSys4IniHeader(path);
        SelectedGameProfile selected = GameProfileSelection.Resolve([], identity);

        Assert.Equal(expectedProfile, selected.Profile.Id);
        Assert.True(selected.IdentityMatched);
    }

    private static GameProfileManifest SyntheticProfile(string id, GameCatalogIdentity identity)
        => new(
            id,
            id,
            [identity],
            ["SYS4000"],
            "SYS4",
            "SYS4000",
            "SYSTEM4.BIN",
            metadataReferences: null,
            new GamePersistencePolicy(
                id,
                writesEnabled: false,
                readOnlyReason: "synthetic test profile",
                NativeSaveMagic.S4SD,
                sharedCompatibilityId: 1,
                numberedCompatibilityId: 1,
                gameId: id,
                expectedPackedSaveVersion: 300,
                new NativeSaveBankDimensions(1, 1, 1, 1, 1, 1),
                numberedGfxRecordSize: NativeNumberedSaveState.GfxRecordSize));
}

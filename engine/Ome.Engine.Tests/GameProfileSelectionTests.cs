using System.Text;
using Ome.Engine.Persistence;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;

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
        Assert.Equal("4.24J", himegari.EngineBuildId);
        Assert.Equal("SYS4422", himegari.EngineAbiId);
        Assert.Equal(["SYS4422"], himegari.ScriptRevisions);
        Assert.Equal("SYSTEM4.BIN", himegari.NaturalBootScript);
        Assert.Equal("himegari", himegari.PersistenceNamespace);
        Assert.True(himegari.PersistenceWritesEnabled);
        Assert.Equal(310, himegari.Persistence.ExpectedPackedSaveVersion);
        Assert.Equal("姫狩りダンジョンマイスター", himegari.Persistence.GameId);
        Assert.Equal(["蟋ｫ迢ｩ繧翫ム繝ｳ繧ｸ繝ｧ繝ｳ繝槭う繧ｹ繧ｿ繝ｼ"], himegari.Persistence.LegacyGameIds);
        Assert.Equal([402459, 1, 789, 1, 1, 1], himegari.Persistence.BankDimensions.ToArray());
        Assert.Equal("res://config/japanese-text-rendering.json",
            himegari.MetadataReferences["portableTextRendering"]);
        DirectSceneDiagnosticPolicy himegariDiagnostics =
            Assert.IsType<DirectSceneDiagnosticPolicy>(himegari.DirectSceneDiagnostics);
        Assert.Equal(["INITCONFIG.BIN", "INIT2.BIN", "INIT.BIN"],
            himegariDiagnostics.DataBootstrapScripts);
        Assert.Equal([
            "SKINIT.BIN", "ITINIT.BIN", "EBINIT.BIN", "CGINIT.BIN", "MPINIT.BIN",
            "AFINIT.BIN", "CCINIT.BIN", "STINIT.BIN", "STINIT2.BIN",
        ], himegariDiagnostics.DataTableBootstrapScripts);
        DebugSceneLaunchPolicy debugLaunch =
            Assert.IsType<DebugSceneLaunchPolicy>(himegari.DebugSceneLaunch);
        Assert.Equal("SYSTEM4.BIN", debugLaunch.RootScript);
        Assert.Equal("TITLE.BIN", debugLaunch.CoordinatorScript);
        Assert.Equal(0x699, debugLaunch.PackedScriptIdAddress);
        Assert.Equal([new ProfileCellSeed(0x0, 1), new ProfileCellSeed(0xaba5c, -1), new ProfileCellSeed(0x62ccf, 0)],
            debugLaunch.CoordinatorWrites);   // TITLE's ＡＤＶデバッグ writes (TITLE@0x61b)
        Assert.True(himegari.Matches(HimegariIdentity));

        GameProfileManifest kamidori = Assert.IsType<GameProfileManifest>(registry.Find("kamidori"));
        Assert.Equal("SYS4", kamidori.SysFrontendId);
        Assert.Equal("4.33G", kamidori.EngineBuildId);
        Assert.Equal("SYS4433", kamidori.EngineAbiId);
        Assert.Equal(["SYS4433"], kamidori.ScriptRevisions);
        Assert.Equal("SYSTEM4.BIN", kamidori.NaturalBootScript);
        Assert.Equal("kamidori", kamidori.PersistenceNamespace);
        Assert.True(kamidori.PersistenceWritesEnabled);
        Assert.Equal(320, kamidori.Persistence.ExpectedPackedSaveVersion);
        Assert.Equal("神採りアルケミーマイスター", kamidori.Persistence.GameId);
        Assert.Empty(kamidori.Persistence.LegacyGameIds);
        Assert.Equal([1037327, 1, 802, 1, 1, 1], kamidori.Persistence.BankDimensions.ToArray());
        Assert.Null(kamidori.Persistence.ReadOnlyReason);
        Assert.Null(kamidori.DirectSceneDiagnostics);
        Assert.Null(kamidori.DebugSceneLaunch);
        Assert.True(kamidori.Matches(KamidoriIdentity));
    }

    /// <summary>創刻のアテリアル is at P3 and M2 (docs/adding-a-game.md): the engine names its install, reads all
    /// of it, has a handler for every opcode its scripts use and saves; its build is declared from 4.33G; script
    /// mods and text packages work for it, and its data tables are decoded and named. Its base mod is not built
    /// yet.</summary>
    [Fact]
    public void ArterialIsCompleteForPlayAndHasItsBaseMod()
    {
        GameProfileManifest arterial = Assert.IsType<GameProfileManifest>(GameProfileRegistry.BuiltIn.Find("arterial"));

        Assert.Equal(("SYS4", "4.38D", "SYS4438", "SYSTEM4.BIN"),
                     (arterial.SysFrontendId, arterial.EngineBuildId, arterial.EngineAbiId, arterial.NaturalBootScript));
        Assert.Equal(["SYS4438"], arterial.ScriptRevisions);
        Assert.True(arterial.Matches(new GameCatalogIdentity("S4IC438", "創刻のアテリアル")));
        Assert.Equal(new GameSupport(PlayLevel.Complete, ModdingLevel.BaseMod), arterial.Support);
        Assert.True(arterial.Support.IsPlayable);
        Assert.Equal(("P3", "M3"), (arterial.Support.PlayId, arterial.Support.ModdingId));
        Assert.Equal("none", new GameSupport(PlayLevel.Complete, ModdingLevel.None).ModdingId);
        // The launcher's art for it comes from the publisher's site, as the other two games' does.
        Assert.Equal("https://www.eukleia.co.jp/eushully/eu13/banner/banner_eu13_a.jpg", arterial.Launcher!.Banner!.Url.ToString());
        Assert.Equal("https://www.eukleia.co.jp/eushully/eu13/image/eu13_package.jpg", arterial.Launcher.Cover!.Url.ToString());
        // A game below P3 is still being added: the run says so, and the launcher does not offer it.
        Assert.False(new GameSupport(PlayLevel.Boots, ModdingLevel.None).IsPlayable);
        Assert.Equal(GameSupport.Supported, GameProfileRegistry.BuiltIn.Find("himegari")!.Support);
        Assert.Equal(GameSupport.Supported, GameProfileRegistry.BuiltIn.Find("kamidori")!.Support);
        Assert.Throws<InvalidOperationException>(() => GameSupport.Parse("P0", "none"));
        Assert.Throws<InvalidOperationException>(() => GameSupport.Parse("P3", null));
        // Its saves are on since a native save was read and loaded both ways (2026-11-19).
        Assert.True(arterial.PersistenceWritesEnabled);
        Assert.Null(arterial.Persistence.ReadOnlyReason);
        Assert.Equal(320, arterial.Persistence.ExpectedPackedSaveVersion);
        Assert.Equal([260993, 1, 231, 1, 1, 1], arterial.Persistence.BankDimensions.ToArray());
        Assert.Null(arterial.DirectSceneDiagnostics);
        Assert.Null(arterial.DebugSceneLaunch);
        Assert.Equal("SYS4438", Ome.Engine.Vm.EngineBuilds.ForProfile(arterial).AbiId);
    }

    [Fact]
    public void NormalKamidoriProfileInjectsNoHimegariState()
    {
        // A natural boot takes no profile state at all; only direct-scene and debug launches have any.
        GameProfileManifest kamidori = Assert.IsType<GameProfileManifest>(
            GameProfileRegistry.BuiltIn.Find("kamidori"));

        Assert.Null(kamidori.DirectSceneDiagnostics);
        Assert.Null(kamidori.DebugSceneLaunch);
        Assert.True(kamidori.PersistenceWritesEnabled);
    }

    [Theory]
    [InlineData("S4IC422", "姫狩りダンジョンマイスター", "himegari", true)]
    [InlineData("S4IC433", "神採りアルケミーマイスター", "kamidori", true)]
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
            ["--game", "kamidori"], HimegariIdentity);

        Assert.Equal("kamidori", selected.Profile.Id);
        Assert.Equal(GameProfileSelectionSource.Explicit, selected.Source);
        Assert.False(selected.IdentityMatched);
        Assert.False(selected.ProbeMode);
        Assert.True(selected.IsReadOnly);
        Assert.Equal("catalog/game identity mismatch", selected.ReadOnlyReason);
        Assert.StartsWith("explicit game mismatch: requested=kamidori", selected.MismatchDiagnostic);
        Assert.Contains("detected=S4IC422 / 姫狩りダンジョンマイスター", selected.MismatchDiagnostic);
        Assert.EndsWith("continuing with the requested game read-only", selected.MismatchDiagnostic);
    }

    [Fact]
    public void ExplicitGameUsesLastValueAndMatchesIdsCaseInsensitively()
    {
        SelectedGameProfile selected = GameProfileSelection.Resolve(
            ["--game", "himegari", "--game", "KAMIDORI"], KamidoriIdentity);

        Assert.Equal("kamidori", selected.Profile.Id);
        Assert.True(selected.IdentityMatched);
        Assert.Equal(GameProfileSelectionSource.Explicit, selected.Source);
    }

    /// <summary>Launcher spec §5.8: <c>--profile</c> is the same option as <c>--game</c>, so the last of either wins.</summary>
    [Theory]
    [InlineData(new[] { "--profile", "kamidori" }, "kamidori")]
    [InlineData(new[] { "--game", "himegari", "--profile", "kamidori" }, "kamidori")]
    [InlineData(new[] { "--profile", "himegari", "--game", "kamidori" }, "kamidori")]
    [InlineData(new[] { "--game", "kamidori", "--mod", "x", "--profile", "himegari" }, "himegari")]
    public void ProfileIsAnAliasOfGameAndTheLastOfEitherWins(string[] arguments, string expected)
    {
        Assert.Equal(expected, GameProfileSelection.RequestedGameId(arguments));
        Assert.Equal(expected, GameProfileSelection.Resolve(arguments, KamidoriIdentity).Profile.Id);
    }

    [Fact]
    public void NoGameOptionRequestsNothing()
        => Assert.Null(GameProfileSelection.RequestedGameId(["--data-root", "x", "--probe"]));

    [Fact]
    public void ProbeModeRequiresAnExplicitAbiAndSuppressesWrites()
    {
        var missingProfile = Assert.Throws<ArgumentException>(
            () => GameProfileSelection.Resolve(["--probe"], HimegariIdentity));
        Assert.Contains("--probe requires --game <id>", missingProfile.Message);

        SelectedGameProfile selected = GameProfileSelection.Resolve(
            ["--game", "himegari", "--probe"], HimegariIdentity);
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

        Assert.Contains("no game matches S4IC999 / Unknown Game", error.Message);
        Assert.Contains("pass --game <id> to choose a game explicitly", error.Message);
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

        Assert.Contains("multiple games match", error.Message);
        Assert.Contains("first, second", error.Message);
        Assert.EndsWith("pass --game <id>", error.Message);
    }

    [Theory]
    [InlineData("--game", "--game")]
    [InlineData("--game", "--game", "--probe")]
    [InlineData("--profile", "--profile")]
    [InlineData("--profile", "--game", "himegari", "--profile")]
    public void MissingExplicitGameIdIsRejected(string option, params string[] arguments)
    {
        var error = Assert.Throws<ArgumentException>(
            () => GameProfileSelection.Resolve(arguments, HimegariIdentity));

        Assert.Equal($"{option} requires a game id", error.Message);
    }

    [Fact]
    public void UnknownExplicitProfileIdIsRejectedWithoutChangingTheRequest()
    {
        var error = Assert.Throws<ArgumentException>(() => GameProfileSelection.Resolve(
            ["--game", "not-a-game"], HimegariIdentity));

        Assert.Contains("unknown game 'not-a-game'; available games: ", error.Message);
    }

    [Fact]
    public void IdentityHeaderReaderNormalizesPaddingAndDecodesCp932Title()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "ome-profile-tests", Guid.NewGuid().ToString("N"), "SYS4INI.BIN");
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
    [InlineData("himegari")]
    [InlineData("kamidori")]
    [InlineData("arterial")]
    public void InstalledCatalogAutoSelectsExpectedProfile(string expectedProfile)
    {
        string path = Path.Combine(Paths.GameInstall(expectedProfile), "SYS4INI.BIN");
        Assert.True(File.Exists(path), $"installed-data profile gate requires {path}");

        GameCatalogIdentity identity = GameCatalogIdentity.ReadSys4IniHeader(path);
        SelectedGameProfile selected = GameProfileSelection.Resolve([], identity);

        Assert.Equal(expectedProfile, selected.Profile.Id);
        Assert.True(selected.IdentityMatched);
    }

    /// <summary>The numbered-save bank prefix is SYS4INI header data (+0xf0..+0x104), which
    /// engine_load_sys4ini_and_mount_append_catalogs reads into ctx+0xa0bb8; the profile copy must match.</summary>
    [Theory]
    [Trait("Category", "Workspace")]
    [InlineData("himegari")]
    [InlineData("kamidori")]
    [InlineData("arterial")]
    public void InstalledSys4IniHeaderDeclaresProfileSaveBankDimensions(string game)
    {
        string path = Path.Combine(Paths.GameInstall(game), "SYS4INI.BIN");
        Assert.True(File.Exists(path), $"installed-data profile gate requires {path}");
        byte[] header = File.ReadAllBytes(path).AsSpan(0, 0x12c).ToArray();
        int[] savedPrefix = Enumerable.Range(0, 6)
            .Select(k => BitConverter.ToInt32(header, 0xf0 + 4 * k)).ToArray();
        int[] fullBanks = Enumerable.Range(0, 6)
            .Select(k => BitConverter.ToInt32(header, 0x114 + 4 * k)).ToArray();

        SelectedGameProfile selected = GameProfileSelection.Resolve(
            [], GameCatalogIdentity.ReadSys4IniHeader(path));

        Assert.Equal(selected.Profile.Persistence.BankDimensions.ToArray(), savedPrefix);
        Assert.All(Enumerable.Range(0, 6), k => Assert.True(fullBanks[k] >= savedPrefix[k]));
    }

    private static GameProfileManifest SyntheticProfile(string id, GameCatalogIdentity identity)
        => new(
            id,
            id,
            [identity],
            ["SYS4000"],
            "SYS4",
            "0.00A",
            "SYS4000",
            "SYSTEM4.BIN",
            metadataReferences: null,
            new GamePersistencePolicy(
                id,
                writesEnabled: false,
                readOnlyReason: "synthetic test profile",
                NativeSaveMagic.S4SD,
                gameId: id,
                expectedPackedSaveVersion: 300,
                new NativeSaveBankDimensions(1, 1, 1, 1, 1, 1)));
}

using System.Text;
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
        Assert.True(himegari.Matches(HimegariIdentity));

        GameProfileManifest kamidori = Assert.IsType<GameProfileManifest>(registry.Find("kamidori"));
        Assert.Equal("SYS4", kamidori.SysFrontendId);
        Assert.Equal("SYS4433", kamidori.EngineAbiId);
        Assert.Equal(["SYS4433"], kamidori.ScriptRevisions);
        Assert.Equal("SYSTEM4.BIN", kamidori.NaturalBootScript);
        Assert.Equal("kamidori", kamidori.PersistenceNamespace);
        Assert.False(kamidori.PersistenceWritesEnabled);
        Assert.True(kamidori.Matches(KamidoriIdentity));
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
            persistenceNamespace: id,
            persistenceWritesEnabled: false);
}

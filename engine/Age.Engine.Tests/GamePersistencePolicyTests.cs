using Age.Engine.Persistence;
using Age.Engine.Profiles;
using Age.Engine.Sys4;

public sealed class GamePersistencePolicyTests
{
    private static readonly GameCatalogIdentity HimegariIdentity =
        new("S4IC422", "姫狩りダンジョンマイスター");
    private static readonly GameCatalogIdentity KamidoriIdentity =
        new("S4IC433", "神採りアルケミーマイスター");

    [Fact]
    public void ProfilePolicyResolvesSys4IniVersionAndNamespacedPaths()
    {
        Sys4StartupSettings settings = Settings(
            "310", "Eushully\\姫狩りダンジョンマイスター");
        SelectedGameProfile selected = GameProfileSelection.Resolve([], HimegariIdentity);
        string userRoot = Path.Combine(Path.GetTempPath(), "age-profile-policy", "user-root");

        ResolvedGamePersistence resolved = ResolvedGamePersistence.Resolve(
            selected, settings, userRoot);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(userRoot, "games", "himegari")),
            resolved.ProfileRoot);
        Assert.Equal(Path.Combine(resolved.ProfileRoot, "SAVE"), resolved.NativePaths.SaveDirectory);
        Assert.Equal(
            Path.Combine(resolved.ProfileRoot, Sys4RegIniStore.FileName),
            resolved.NativePaths.Sys4RegIniPath);
        Assert.Equal(Path.Combine(resolved.ProfileRoot, "diagnostics"), resolved.DiagnosticsDirectory);
        Assert.Equal(3, resolved.NativeIdentity.SaveVersion1);
        Assert.Equal(10, resolved.NativeIdentity.SaveVersion2);
        Assert.Equal([402459, 1, 789, 1, 1, 1], resolved.NativeIdentity.BankDimensions!.ToArray());
        Assert.True(resolved.WritesEnabled);

        Sys4StartupSettings wrongVersion = Settings(
            "320", "Eushully\\姫狩りダンジョンマイスター");
        ResolvedGamePersistence mismatched = ResolvedGamePersistence.Resolve(
            selected, wrongVersion, userRoot);
        Assert.False(mismatched.WritesEnabled);
        Assert.Null(mismatched.CreateNativeDatStore());
        Assert.Contains("does not match profile value 310", mismatched.ReadOnlyReason);
    }

    [Fact]
    public void ExplicitMismatchContinuesReadOnlyWithoutTreatingDetectedVersionAsRequestedProfileData()
    {
        SelectedGameProfile selected = GameProfileSelection.Resolve(
            ["--profile", "himegari"], KamidoriIdentity);

        ResolvedGamePersistence resolved = ResolvedGamePersistence.Resolve(
            selected,
            Settings("320", "Eushully\\神採りアルケミーマイスター"),
            Path.Combine(Path.GetTempPath(), "age-profile-policy", "mismatch"));

        Assert.False(resolved.WritesEnabled);
        Assert.Null(resolved.CreateNativeDatStore());
        Assert.Equal(3, resolved.NativeIdentity.SaveVersion1);
        Assert.Equal(10, resolved.NativeIdentity.SaveVersion2);
        Assert.Equal("catalog/profile identity mismatch", resolved.ReadOnlyReason);
    }

    [Fact]
    public void ProfileRootsIsolateOpenSaveCopyAndDeleteOperations()
    {
        string userRoot = Path.Combine(
            Path.GetTempPath(), "age-profile-isolation", Guid.NewGuid().ToString("N"));
        try
        {
            ResolvedGamePersistence himegari = ResolvedGamePersistence.Resolve(
                GameProfileSelection.Resolve([], HimegariIdentity),
                Settings("310", "Eushully\\姫狩りダンジョンマイスター"),
                userRoot);
            ResolvedGamePersistence kamidori = ResolvedGamePersistence.Resolve(
                GameProfileSelection.Resolve([], KamidoriIdentity),
                Settings("320", "Eushully\\神採りアルケミーマイスター"),
                userRoot);

            Assert.NotEqual(himegari.ProfileRoot, kamidori.ProfileRoot);
            Assert.True(himegari.WritesEnabled);
            Assert.False(kamidori.WritesEnabled);
            Assert.Null(kamidori.CreateNativeDatStore());

            var himegariStore = new DirectoryNativeDatStore(
                himegari.NativePaths.SaveDirectory, himegari.NativeIdentity);
            var kamidoriStore = new DirectoryNativeDatStore(
                kamidori.NativePaths.SaveDirectory, kamidori.NativeIdentity);
            NativeSystemTime timestamp = NativeSystemTime.FromLocalDateTime(DateTime.Now);
            himegariStore.SaveShared([1, 2, 3, 4], timestamp, 0);
            himegariStore.SaveNumbered(1, [5, 6, 7, 8], timestamp, 0);

            Assert.NotNull(himegariStore.LoadShared());
            Assert.NotNull(himegariStore.LoadNumbered(1));
            Assert.Null(kamidoriStore.LoadShared());
            Assert.Null(kamidoriStore.LoadNumbered(1));

            kamidoriStore.SaveNumbered(2, [9, 10, 11, 12], timestamp, 0);
            Assert.Equal(2, himegariStore.CopyNumberedPair(1, 3));
            Assert.Equal(2, himegariStore.DeleteNumberedPair(1));

            Assert.Null(himegariStore.LoadNumbered(1));
            Assert.NotNull(himegariStore.LoadNumbered(3));
            Assert.NotNull(kamidoriStore.LoadNumbered(2));
            Assert.Null(kamidoriStore.LoadNumbered(3));
        }
        finally
        {
            if (Directory.Exists(userRoot)) Directory.Delete(userRoot, recursive: true);
        }
    }

    private static Sys4StartupSettings Settings(string saveVersion, string productPath)
        => Sys4AssetCatalog.Parse(Sys4StartupSettingsTests.BuildCatalog(
            ("SAVEVERSION", saveVersion),
            ("REGFILEPATH", productPath),
            ("SAVEPATH", productPath + "\\SAVE"))).StartupSettings;
}

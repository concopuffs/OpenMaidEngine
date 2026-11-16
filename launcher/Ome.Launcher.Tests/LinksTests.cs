using System.Net;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Mods.Launch;

/// <summary>
/// The pages under the profile list (plan §6.11): Games (each supported game with its installs and its art, downloaded
/// and removed at the player's word, and the publisher's page), Launcher settings (each written to
/// <c>launcher.toml</c>, the language and theme shown at once), and About (the version, the engine, the licences, the
/// repository). The art comes from a handler in the process; no request leaves it.
/// </summary>
public sealed class LinksTests : IDisposable
{
    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    /// <summary>Two Kamidori profiles on one install; no Himegari profile.</summary>
    private string TwoKamidori()
    {
        string install = _home.Install("kamidori");
        _home.Profile("kamidori-english", "kamidori", install, "Kamidori (English)");
        _home.Profile("kamidori-ja", "kamidori", install, "神採り (日本語)");
        return install;
    }

    private void Settings(Func<LauncherSettings, LauncherSettings> change)
        => _home.Session().Save(change(LauncherSettingsStore.Load(_home.Home).Settings));

    private LauncherSettings Saved => LauncherSettingsStore.Load(_home.Home).Settings;

    private async Task<(MainViewModel Model, RecordingHost Host)> Open(ArtOptions? art = null)
    {
        var model = new MainViewModel(_home.Session(), art ?? new ArtOptions(Handler: new Served()));
        var host = new RecordingHost();
        model.Host = host;
        await model.StartAsync();
        return (model, host);
    }

    /// <summary>A real PNG of that size, so the window's own decoder takes it.</summary>
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888,
                                               AlphaFormat.Premul);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>The built-in registry with Kamidori's images at <c>art.test</c>, under the synthetic files' hashes.</summary>
    private static GameProfileRegistry Registry(byte[] banner, byte[] cover)
    {
        GameProfileManifest kamidori = GameProfileRegistry.BuiltIn.Find("kamidori")!;
        var art = new GameLauncherArt("https://art.test/", new GameArtSource("https://art.test/banner.png", Sha(banner)),
                                      new GameArtSource("https://art.test/cover.png", Sha(cover)));
        return new GameProfileRegistry(GameProfileRegistry.BuiltIn.Profiles.Where(game => game.Id != "kamidori").Append(
            new GameProfileManifest(kamidori.Id, kamidori.DisplayTitle, kamidori.CatalogIdentities, kamidori.ScriptRevisions,
                                    kamidori.SysFrontendId, kamidori.EngineBuildId, kamidori.EngineAbiId,
                                    kamidori.NaturalBootScript, kamidori.MetadataReferences, kamidori.Persistence,
                                    kamidori.DirectSceneDiagnostics, kamidori.DebugSceneLaunch, art)));
    }

    [Fact]
    public void TheGamesPageListsEachGameWithItsInstallsAndDownloadsAndRemovesItsArt() => Headless.Run(async () =>
    {
        string install = TwoKamidori();
        Settings(settings => settings with { Art = ArtConsent.Never });
        byte[] banner = Png(40, 8), cover = Png(32, 45);
        var served = new Served { ["/banner.png"] = banner, ["/cover.png"] = cover };
        (MainViewModel model, RecordingHost host) = await Open(new ArtOptions(Registry(banner, cover), served));

        model.Games.Execute(null);
        GamesViewModel games = Assert.IsType<GamesViewModel>(model.Dialog);
        Assert.Equal(GameProfileRegistry.BuiltIn.Profiles.Select(game => game.Id), games.Games.Select(game => game.Id));
        GameEntryViewModel kamidori = games.Games.Single(game => game.Id == "kamidori");
        GameEntryViewModel himegari = games.Games.Single(game => game.Id == "himegari");

        // The known installs are the profiles' (spec §8.2): one install, both its profiles named.
        string listed = Assert.Single(kamidori.Installs);
        Assert.Contains(install, listed);
        Assert.Contains("Kamidori (English)", listed);
        Assert.Contains("神採り (日本語)", listed);
        Assert.False(himegari.HasInstalls);

        // Nothing was downloaded with `never`; the player's word downloads both images, and the list shows them.
        Assert.Equal(0, served.Requests);
        Assert.True(kamidori.Banner!.IsCard);
        Assert.Contains("art.test", kamidori.DownloadText);
        Assert.Equal("Banner: not downloaded · Cover: not downloaded", kamidori.ArtText);
        Assert.True(kamidori.Download.CanExecute(null));
        Assert.False(kamidori.Remove.CanExecute(null));
        await kamidori.DownloadAsync();
        Assert.Equal(2, served.Requests);
        Assert.Equal("The art is downloaded.", kamidori.Status);
        Assert.False(kamidori.StatusBad);
        Assert.Equal("Banner: downloaded · Cover: downloaded", kamidori.ArtText);
        Assert.False(kamidori.Banner!.IsCard);
        Assert.False(model.Groups.Single(group => group.Game == "kamidori").Banner!.IsCard);
        Assert.False(model.Page!.Cover!.IsCard);
        Assert.False(kamidori.Download.CanExecute(null));

        // Removing it brings the cards back, and a failed download says what failed.
        kamidori.Remove.Execute(null);
        Assert.False(Directory.Exists(_home.Home.ArtCacheDirectory("kamidori")));
        Assert.Equal("The downloaded art is removed.", kamidori.Status);
        Assert.True(kamidori.Banner!.IsCard);
        Assert.True(model.Groups.Single(group => group.Game == "kamidori").Banner!.IsCard);
        served.Remove("/cover.png");
        await kamidori.DownloadAsync();
        Assert.True(kamidori.StatusBad);
        Assert.Contains("Cover: the server answered 404", kamidori.Status);
        Assert.Equal("Banner: downloaded · Cover: not downloaded", kamidori.ArtText);

        kamidori.OpenSite.Execute(null);
        Assert.Contains("Open https://art.test/", host.Calls);
        Assert.Equal("https://www.eukleia.co.jp/eushully/", himegari.SiteText);
        games.Close.Execute(null);
        Assert.False(model.HasDialog);
        model.Dispose();
    });

    [Fact]
    public void LauncherSettingsAreWrittenToLauncherTomlAndTheLanguageAndThemeShowAtOnce() => Headless.Run(async () =>
    {
        TwoKamidori();
        Settings(settings => settings with { Art = ArtConsent.Never, LastProfile = "kamidori-english" });
        var served = new Served();
        (MainViewModel model, RecordingHost host) = await Open(new ArtOptions(Handler: served));
        ProfileRowViewModel other = model.Rows.Single(row => row.Id == "kamidori-ja");
        ProfileState checkedState = other.State;
        Assert.NotEqual(ProfileState.Checking, checkedState);

        model.LauncherSettings.Execute(null);
        LauncherSettingsViewModel settings = Assert.IsType<LauncherSettingsViewModel>(model.Dialog);
        Assert.Equal(["", "en", "ja"], settings.Languages.Select(choice => choice.Value));
        Assert.Equal("The system's (English)", settings.SelectedLanguage!.Label);

        // The language: written, and the window, its list and this page read it at once. The rows stay what the checks
        // made them, rather than going back to "Checking…".
        settings.SelectedLanguage = settings.Languages.Single(choice => choice.Value == "ja");
        Assert.Equal("ja", Saved.Language);
        Assert.Equal("ja", model.S.Language);
        Assert.Equal("ランチャーの設定", settings.S["lsettings.title"]);
        Assert.Same(other, model.Rows.Single(row => row.Id == "kamidori-ja"));
        Assert.Equal(checkedState, other.State);
        Assert.Equal("ja", other.S.Language);
        Assert.Equal("ja", model.Page!.S.Language);
        settings.SelectedLanguage = settings.Languages[0];
        Assert.Null(Saved.Language);
        Assert.Equal("en", model.S.Language);

        // The theme is applied as it is written.
        settings.SelectedTheme = settings.Themes.Single(choice => choice.Value == "Dark");
        Assert.Equal(LauncherTheme.Dark, Saved.Theme);
        Assert.Contains("Theme Dark", host.Calls);

        settings.SelectedWhileRunning = settings.WhileRunningChoices.Single(choice => choice.Value == "Minimize");
        Assert.Equal(WhileRunning.Minimize, Saved.WhileRunning);

        // `always` downloads what is missing at once, as the window's question does.
        settings.SelectedArt = settings.ArtChoices.Single(choice => choice.Value == "Always");
        Assert.Equal(ArtConsent.Always, Saved.Art);
        await Headless.Until(() => served.Requests == 2, "the missing Kamidori images are requested");

        // A new profile's OME.INI starts from these.
        settings.SelectedNewLanguage = settings.NewLanguages.Single(choice => choice.Value == "ja");
        settings.ToggleNewFps.Execute(null);
        settings.ToggleNewLocator.Execute(null);
        Assert.Equal(new PortOptions("ja", 0, 0, true, true), Saved.NewProfile);
        Assert.True(settings.NewFps && settings.NewLocator);
        ProfileEntry made = _home.Profile("fresh", "kamidori", _home.Install("kamidori", "second"), "Fresh");
        Assert.Equal(new PortOptions("ja", 0, 0, true, true),
                     PortOptions.Parse(File.ReadAllText(Path.Combine(made.Root, PortOptions.FileName))));
        model.Dispose();
    });

    [Fact]
    public void ASettingThatCannotBeWrittenIsShownAndChangesNothing() => Headless.Run(async () =>
    {
        TwoKamidori();
        Settings(settings => settings with { Art = ArtConsent.Never });
        (MainViewModel model, RecordingHost host) = await Open();
        // A folder where launcher.toml goes: the write's last step, the move over it, fails.
        File.Delete(_home.Home.SettingsPath);
        Directory.CreateDirectory(_home.Home.SettingsPath);

        model.LauncherSettings.Execute(null);
        var settings = (LauncherSettingsViewModel)model.Dialog!;
        settings.SelectedTheme = settings.Themes.Single(choice => choice.Value == "Dark");
        Assert.True(settings.HasError);
        Assert.Equal("System", settings.SelectedTheme!.Value);
        Assert.Equal(LauncherTheme.System, model.Session.Settings.Theme);
        Assert.DoesNotContain(host.Calls, call => call.StartsWith("Theme"));
        settings.SelectedLanguage = settings.Languages.Single(choice => choice.Value == "ja");
        Assert.Equal("en", model.S.Language);
        Assert.Equal("", settings.SelectedLanguage!.Value);
        model.Dispose();
    });

    [Fact]
    public void AboutShowsTheVersionTheEngineTheLicencesAndTheRepository() => Headless.Run(async () =>
    {
        TwoKamidori();
        Settings(settings => settings with { Art = ArtConsent.Never });
        string folder = Path.Combine(_home.Root, "launcher");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "LICENSE"), "licence");
        (MainViewModel model, RecordingHost host) = await Open();

        model.About.Execute(null);
        AboutViewModel about = Assert.IsType<AboutViewModel>(model.Dialog);
        Assert.Equal($"Open Maid Engine launcher {Ome.Launcher.App.Version}", about.VersionText);
        Assert.Equal(Path.Combine(_home.Root, "engine", "OME"), about.EngineText);
        Assert.True(about.EngineMissing);
        // The notices are not beside this launcher: About says so, and offers only the licence it has.
        Assert.True(about.LicencesMissing);
        Assert.Equal(["LICENSE", "THIRD_PARTY_NOTICES.md"], about.Licences.Select(licence => licence.Name));
        Assert.False(about.Licences[1].Open.CanExecute(null));
        about.Licences[0].Open.Execute(null);
        Assert.Contains($"Open {Path.Combine(folder, "LICENSE")}", host.Calls);
        about.OpenRepository.Execute(null);
        Assert.Contains("Open https://github.com/concopuffs/OpenMaidEngine", host.Calls);
        about.Close.Execute(null);

        File.WriteAllText(Path.Combine(folder, "THIRD_PARTY_NOTICES.md"), "notices");
        model.About.Execute(null);
        Assert.False(((AboutViewModel)model.Dialog!).LicencesMissing);
        model.Dispose();
    });

    [Fact]
    public void TheVersionIsTheOneTheBuildStampedAsTheEngineShowsIts()
    {
        // Directory.Build.targets stamps OmeVersion (git describe) into both programs (plan §9.1); a checkout always
        // has a commit to describe, so the stamp is there, and the launcher reads it by the engine's rule.
        string? stamped = typeof(Ome.Launcher.App).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == EngineIdentity.VersionMetadataKey)?.Value;
        Assert.False(string.IsNullOrWhiteSpace(stamped));
        Assert.Equal(EngineIdentity.DisplayVersion(stamped), Ome.Launcher.App.Version);
        Assert.NotEqual("unknown", Ome.Launcher.App.Version);
        Assert.DoesNotContain("1.0.0", Ome.Launcher.App.Version);
    }

    [Fact]
    public void TheLicencesAreBesideTheBuiltLauncher()
    {
        string folder = Path.GetDirectoryName(TestHome.Launcher)!;
        Assert.True(File.Exists(Path.Combine(folder, "LICENSE")), folder);
        Assert.True(File.Exists(Path.Combine(folder, "THIRD_PARTY_NOTICES.md")), folder);
    }

    [Fact]
    public void AReloadKeepsAFailureThePlayerDismissed() => Headless.Run(async () =>
    {
        string install = _home.Install("kamidori");
        ProfileEntry profile = _home.Profile("kamidori", "kamidori", install, "Kamidori");
        Settings(settings => settings with { Art = ArtConsent.Never });
        var crashed = new LaunchResult(true, 5, null, null, ["the last line"],
                                       ProfileChecks.Run(profile, _home.CheckOptions));
        var model = new MainViewModel(_home.Session(WindowReason.StartFailed, profile: profile, launch: crashed),
                                      new ArtOptions(Handler: new Served()));
        await model.StartAsync();
        ProfileRowViewModel row = model.Rows.Single();
        Assert.NotNull(row.Failure);
        model.Page!.DismissFailure.Execute(null);
        model.Reload();
        Assert.Same(row, model.Rows.Single());
        Assert.Null(row.Failure);
        model.Dispose();
    });

    /// <summary>Serves the paths it holds and answers 404 to the rest, counting every request.</summary>
    private sealed class Served : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public int Requests { get; private set; }
        public byte[] this[string path]
        {
            set => _files[path] = value;
        }

        public void Remove(string path) => _files.Remove(path);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests++;
            return Task.FromResult(_files.TryGetValue(request.RequestUri!.AbsolutePath, out byte[]? body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

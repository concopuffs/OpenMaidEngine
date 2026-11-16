using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Mods.Launch;

/// <summary>
/// The Settings tab (launcher spec §8.4, plan §6.6) on synthetic installs: the language (the shell's and a package's),
/// the start mode with the note a forced window brings, the window sizes over the install's canvas, the port's
/// switches, each written at once as the engine writes the files; the start mode waiting for the install; and the page
/// locked while the game runs.
/// </summary>
public sealed class SettingsTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly string _install;
    private readonly ProfileEntry _profile;

    public SettingsTests()
    {
        _install = _home.Install("kamidori");
        _profile = _home.Profile("kamidori", "kamidori", _install, "Kamidori");
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { Art = ArtConsent.Never });
    }

    public void Dispose() => _home.Dispose();

    private async Task<(MainViewModel Model, RecordingHost Host)> Open((int, int)? usable = null)
    {
        var model = new MainViewModel(_home.Session(), new ArtOptions(Handler: new HttpClientHandler()));
        var host = new RecordingHost { Usable = usable };
        model.Host = host;
        await model.StartAsync();
        model.Page!.ShowTab.Execute(ProfileTab.Settings);
        return (model, host);
    }

    private ProfileSettings Saved => ProfileSettings.Load(_profile.Root, Sys4StartupSettings.Empty);
    private string Registry => Path.Combine(_profile.Root, "SYS4REG.INI");

    [Fact]
    public void EachChangeIsWrittenAtOnceAsTheEngineWritesIt() => Headless.Run(async () =>
    {
        var (model, _) = await Open(usable: (1900, 900));
        using (model)
        {
            SettingsViewModel settings = model.Page!.Settings;
            Assert.True(settings.CanWrite && settings.CanWriteDisplay);
            Assert.Equal(["ja", "en"], settings.Languages.Select(choice => choice.Tag));
            Assert.Equal(Saved.Port.Language, settings.SelectedLanguage!.Tag);

            settings.SelectedLanguage = settings.Languages.Single(choice => choice.Tag != Saved.Port.Language);
            string chosen = settings.SelectedLanguage!.Tag;
            await Headless.Until(() => settings.SelectedLanguage?.Tag == chosen, "the language written");
            Assert.Equal(chosen, Saved.Port.Language);
            Assert.Equal(ProfilePageViewModel.LanguageName(model.Page.S, chosen), model.Page.LanguageText);

            // The synthetic install's canvas is the engine's default 640 x 480; 1280 x 960 and its bar do not fit 900
            // rows.
            Assert.Equal(["640 × 480 (the game's size)", "800 × 600 (125 %)", "960 × 720 (150 %)", "1120 × 840 (175 %)"],
                         settings.Sizes.Select(size => size.Label));
            Assert.True(settings.SelectedSize!.Canvas);
            settings.SelectedSize = settings.Sizes[1];
            await Headless.Until(() => settings.SelectedSize?.Width == 800, "the size written");
            Assert.Equal((800, 600), (Saved.Port.WindowWidth, Saved.Port.WindowHeight));
            Assert.Equal("800 × 600 (125 %)", settings.SelectedSize!.Label);
            settings.SelectedSize = settings.Sizes[0];
            await Headless.Until(() => settings.SelectedSize?.Canvas == true, "the game's size written");
            Assert.True(Saved.Port.CanvasWindowSize);

            Assert.True(settings.Fullscreen);
            Assert.False(File.Exists(Registry));
            await settings.SaveAsync(windowed: true);
            Assert.True(settings.Windowed);
            Assert.Contains("ScreenMode=1", File.ReadAllText(Registry));
            Assert.Equal("In a window, at the game's size", model.Page.Overview.Display);

            settings.ToggleFps.Execute(null);
            await Headless.Until(() => settings.FpsCounter, "the FPS counter written");
            settings.ToggleLocator.Execute(null);
            await Headless.Until(() => settings.LocatorHud, "the locator written");
            Assert.True(Saved.Port.FpsCounter && Saved.Port.LocatorHud);
            Assert.False(settings.ForceScreen);
            Assert.False(settings.HasResult);

            // Changes made in a row, before the first is written, all land: each waits its turn and changes the file
            // as it then is.
            settings.ToggleFps.Execute(null);
            settings.ToggleLocator.Execute(null);
            await settings.SaveAsync(port => port with { Language = "en" });
            Assert.False(settings.HasResult);
            Assert.Equal((false, false, "en"), (Saved.Port.FpsCounter, Saved.Port.LocatorHud, Saved.Port.Language));
        }
    });

    [Fact]
    public void AForcedWindowLeavesTheStartFreeAndSaysWhatItDoes() => Headless.Run(async () =>
    {
        File.WriteAllText(Registry, "[display]\r\nScreenMode=0\r\nForceScreen=1\r\n");
        var (model, _) = await Open();
        using (model)
        {
            SettingsViewModel settings = model.Page!.Settings;
            Assert.True(settings.ForceScreen);
            Assert.True(settings.Fullscreen);
            Assert.True(settings.StartWindowed.CanExecute(null));
            await settings.SaveAsync(windowed: true);
            Assert.True(settings.Windowed);
            Assert.Contains("ForceScreen=1", File.ReadAllText(Registry));
            // No screen to measure: every size is offered.
            Assert.Equal(5, settings.Sizes.Count);
        }
    });

    [Fact]
    public void ALanguageAPackageDefinesIsOffered() => Headless.Run(async () =>
    {
        _home.Package("kamidori", "test.french", target: "extended",
                      tables: "[[language]]\ntag = \"fr\"\nname = \"Français\"\n");
        ProfileStore.Update(_profile, document => Library.Enable(document with { Mode = ProfileMode.Extended }, "test.french",
                                                                 Library.Index(document)).Document, _home.Home);
        var (model, _) = await Open();
        using (model)
        {
            SettingsViewModel settings = model.Page!.Settings;
            Assert.Equal(("fr", "Français"), (settings.Languages[^1].Tag, settings.Languages[^1].Label));
            settings.SelectedLanguage = settings.Languages[^1];
            await Headless.Until(() => settings.SelectedLanguage?.Tag == "fr", "the language written");
            Assert.Equal("fr", Saved.Port.Language);
            Assert.Equal("fr", model.Page.LanguageText);
        }
    });

    [Fact]
    public void TheStartWaitsForTheInstallAndTheRunningGameLocksThePage() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            SettingsViewModel settings = model.Page!.Settings;
            Assert.True(RunLock.TryAcquire(_profile.Root, markStart: true, out RunLock? engine, out _));
            using (engine)
            {
                await model.RefreshRunningAsync();
                Assert.True(settings.IsLocked);
                Assert.False(settings.CanWrite);
                Assert.False(settings.ToggleFps.CanExecute(null));
                Assert.False(settings.StartWindowed.CanExecute(null));
                await settings.SaveAsync(port => port with { FpsCounter = true });
                Assert.False(Saved.Port.FpsCounter);
            }
        }

        File.Delete(Path.Combine(_install, Installs.CatalogFile));
        (model, _) = await Open();
        using (model)
        {
            SettingsViewModel settings = model.Page!.Settings;
            Assert.True(settings.CanWrite);
            Assert.True(settings.DisplayWaitsForInstall);
            Assert.False(settings.StartWindowed.CanExecute(null));
            Assert.Equal(["The game's size"], settings.Sizes.Select(size => size.Label));
            Assert.Equal("Unknown", model.Page.Overview.Display);
            await settings.SaveAsync(windowed: true);
            Assert.False(File.Exists(Registry));
            // The port's own options need no install.
            settings.ToggleFps.Execute(null);
            await Headless.Until(() => settings.FpsCounter, "the FPS counter written");
            Assert.True(Saved.Port.FpsCounter);
        }
    });
}

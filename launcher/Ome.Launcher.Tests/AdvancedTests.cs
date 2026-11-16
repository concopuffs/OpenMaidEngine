using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Mods.Launch;

/// <summary>
/// The Advanced tab (launcher spec §8.4, plan §6.8) on a synthetic install: the overlays chosen from the install's
/// folders of files the catalog names, with the BMP switch they bring; the engine's startup options written to
/// <c>profile.toml</c>; the diagnostics folder and the summary on the clipboard; and the page locked while the game runs.
/// </summary>
public sealed class AdvancedTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly string _install;
    private readonly ProfileEntry _profile;

    public AdvancedTests()
    {
        _install = Path.Combine(_home.Root, "installs", "kamidori");
        Directory.CreateDirectory(_install);
        TestHome.Catalog(_install, "kamidori", extra: "TITLE.AGF");
        Directory.CreateDirectory(Path.Combine(_install, "patch"));
        File.WriteAllBytes(Path.Combine(_install, "patch", "TITLE.AGF"), "BM\0\0"u8.ToArray());
        File.WriteAllText(Path.Combine(_install, "patch", "MENU.BIN"), "");
        Directory.CreateDirectory(Path.Combine(_install, "extras"));
        File.WriteAllText(Path.Combine(_install, "extras", "HISTORY.BIN"), "");
        Directory.CreateDirectory(Path.Combine(_install, "notes"));
        File.WriteAllText(Path.Combine(_install, "notes", "readme.txt"), "");
        _profile = _home.Profile("kamidori", "kamidori", _install, "Kamidori");
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { Art = ArtConsent.Never });
    }

    public void Dispose() => _home.Dispose();

    private async Task<(MainViewModel Model, RecordingHost Host)> Open()
    {
        var model = new MainViewModel(_home.Session(), new ArtOptions(Handler: new HttpClientHandler()));
        var host = new RecordingHost();
        model.Host = host;
        await model.StartAsync();
        model.Page!.ShowTab.Execute(ProfileTab.Advanced);
        return (model, host);
    }

    private ProfileDocument Document => ProfileDocuments.Read(_profile.Root)!.Document;
    private string Patch => Path.Combine(_install, "patch");
    private string Extras => Path.Combine(_install, "extras");

    [Fact]
    public void OverlaysAreChosenFromTheInstallsFoldersAndBringTheBmpSwitch() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            AdvancedViewModel advanced = model.Page!.Advanced;
            Assert.True(advanced.CanWrite);
            Assert.Equal([("extras", false, "1 file the game names"), ("patch", false, "2 files the game names · BMP files under AGF names")],
                         advanced.Overlays.Select(row => (row.Name, row.On, row.Note)));
            Assert.False(advanced.AllowBmpAsAgf);

            await advanced.ToggleOverlayAsync(Patch);
            Assert.Equal([Patch], Document.Install.Overlays);
            Assert.True(Document.Install.AllowBmpAsAgf);
            Assert.True(advanced.AllowBmpAsAgf);
            Assert.False(advanced.BmpNeeded);

            advanced.Overlays.Single(row => row.Name == "extras").Toggle.Execute(null);
            await Headless.Until(() => advanced.Overlays.All(row => row.On), "the second overlay");
            Assert.Equal([Patch, Extras], Document.Install.Overlays);
            Assert.Equal([("patch", true), ("extras", true)], advanced.Overlays.Select(row => (row.Name, row.On)));

            // The switch off with a BMP folder in use says what the engine will do.
            advanced.ToggleBmp.Execute(null);
            await Headless.Until(() => !advanced.AllowBmpAsAgf, "the switch off");
            Assert.False(Document.Install.AllowBmpAsAgf);
            Assert.True(advanced.BmpNeeded);

            await advanced.ToggleOverlayAsync(Patch);
            Assert.Equal([Extras], Document.Install.Overlays);
            Assert.False(advanced.BmpNeeded);
            Assert.Equal([("extras", true), ("patch", false)], advanced.Overlays.Select(row => (row.Name, row.On)));

            // An overlay the profile names that is no longer there fails the install's check, and the notice leads
            // here, where it stays listed to be turned off.
            Directory.Delete(Extras, recursive: true);
            await model.Page.CheckAsync();
            Assert.Equal("An overlay folder this profile uses is gone.", model.Page.NoticeText);
            Assert.Equal("Show on the Advanced page", model.Page.NoticeAction);
            Assert.False(model.Page.Overview.ShowLocate);
            Assert.Equal([("extras", true, "The folder is gone"), ("patch", false, "2 files the game names · BMP files under AGF names")],
                         advanced.Overlays.Select(row => (row.Name, row.On, row.Note)));
            await advanced.ToggleOverlayAsync(Extras);
            Assert.Empty(Document.Install.Overlays);
            Assert.False(model.Page.HasNotice);
        }
    });

    [Fact]
    public void TheEnginesOptionsAreWrittenToTheProfileFile() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            AdvancedViewModel advanced = model.Page!.Advanced;
            Assert.Equal(("gpu", "auto", "continue", "stop"),
                         (advanced.Renderer!.Value, advanced.TextBackend!.Value, advanced.LuaErrors!.Value, advanced.OnFailure!.Value));
            Assert.Equal(new ProfileLaunch(null, null, null), Document.Launch);

            // Each change is written in turn; the page shows the file once its write is done.
            advanced.Renderer = advanced.Renderers[1];
            advanced.TextBackend = advanced.TextBackends.Single(choice => choice.Value == "portable");
            advanced.LuaErrors = advanced.LuaErrorModes[1];
            advanced.OnFailure = advanced.FailureModes[1];
            advanced.ToggleDirect3D12.Execute(null);
            await Headless.Until(() => advanced.Direct3D12, "Direct3D 12");
            Assert.True(advanced.SoftwareRenderer);
            Assert.Equal(("software", "portable", "halt", "Start without it"),
                         (advanced.Renderer!.Value, advanced.TextBackend!.Value, advanced.LuaErrors!.Value, advanced.OnFailure!.Label));
            Assert.False(advanced.HasResult);
            Assert.Equal(ProfileLauncherTable.Direct3D12, Document.Launcher.GraphicsApi);

            // The engine reads the same file.
            ProfileFile file = ProfileFile.TryRead(_profile.Root)!;
            Assert.Equal(new ProfileLaunch("software", "portable", "halt"), file.Launch);
            Assert.True(file.Mods!.SkipFailed);
        }
    });

    [Fact]
    public void TheDiagnosticsOpenAndTheSummaryIsCopied() => Headless.Run(async () =>
    {
        var (model, host) = await Open();
        using (model)
        {
            AdvancedViewModel advanced = model.Page!.Advanced;
            advanced.OpenDiagnostics.Execute(null);
            string diagnostics = Path.Combine(_profile.Root, "diagnostics");
            Assert.True(Directory.Exists(diagnostics));
            Assert.Contains($"Open {diagnostics}", host.Calls);

            File.WriteAllText(Path.Combine(diagnostics, "engine.log"), $"opened {Path.Combine(Patch, "TITLE.AGF")}\n");
            await advanced.CopySummaryAsync();
            Assert.Equal("The summary is on the clipboard.", advanced.Result);
            Assert.Contains($"Launcher: {Ome.Launcher.App.Version}", host.Clipboard);
            Assert.Contains("Profile: Kamidori [kamidori]", host.Clipboard);
            Assert.Contains("  Install: Passed", host.Clipboard);
            Assert.Contains($"  opened {Path.Combine("<install>", "patch", "TITLE.AGF")}", host.Clipboard);
        }
    });

    [Fact]
    public void TheRunningGameLocksThePage() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            AdvancedViewModel advanced = model.Page!.Advanced;
            Assert.True(RunLock.TryAcquire(_profile.Root, markStart: true, out RunLock? engine, out _));
            using (engine)
            {
                await model.RefreshRunningAsync();
                Assert.True(advanced.IsLocked);
                Assert.False(advanced.CanWrite);
                Assert.False(advanced.ToggleBmp.CanExecute(null));
                Assert.False(advanced.ToggleDirect3D12.CanExecute(null));
                Assert.All(advanced.Overlays, row => Assert.False(row.Toggle.CanExecute(null)));
                advanced.Renderer = advanced.Renderers[1];
                await advanced.ToggleOverlayAsync(Patch);
                Assert.Equal(new ProfileLaunch(null, null, null), Document.Launch);
                Assert.Empty(Document.Install.Overlays);
                // Looking stays open.
                Assert.True(advanced.OpenDiagnostics.CanExecute(null));
                Assert.True(advanced.CopySummary.CanExecute(null));
            }
        }
    });
}

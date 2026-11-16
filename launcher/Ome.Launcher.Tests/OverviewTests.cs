using Ome.Engine.Profiles;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Mods.Launch;

/// <summary>
/// The Overview tab (launcher spec §8.4, plan §6.4) on synthetic installs: what it shows of the game and the profile,
/// the mods in load order, the checks in the window's words, and its actions: locate the install, accept the install as
/// it is now, and the mode switch, which asks first and, down to Compatibility, turns off what that mode refuses.
/// </summary>
public sealed class OverviewTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly string _install;
    private readonly ProfileEntry _profile;

    public OverviewTests()
    {
        _install = _home.Install("kamidori");
        _profile = _home.Profile("kamidori", "kamidori", _install, "Kamidori");
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { Art = ArtConsent.Never });
    }

    public void Dispose() => _home.Dispose();

    private async Task<(MainViewModel Model, RecordingHost Host)> Open(string language = "en")
    {
        var model = new MainViewModel(_home.Session(language: language), new ArtOptions(Handler: new HttpClientHandler()));
        var host = new RecordingHost();
        model.Host = host;
        await model.StartAsync();
        return (model, host);
    }

    private ProfileDocument Document => ProfileDocuments.Read(_profile.Root)!.Document;

    [Fact]
    public void TheOverviewShowsTheGameTheProfileAndTheChecks() => Headless.Run(async () =>
    {
        var (model, host) = await Open();
        using (model)
        {
            OverviewViewModel overview = model.Page!.Overview;
            GameProfileManifest kamidori = GameProfileRegistry.BuiltIn.Find("kamidori")!;
            Assert.Equal(kamidori.CatalogIdentities[0].Title, overview.OriginalTitle);
            Assert.Equal(kamidori.DisplayTitle, overview.OtherTitle);
            Assert.StartsWith(kamidori.CatalogIdentities[0].CatalogRevision + " · sha256:", overview.Catalog);
            Assert.EndsWith("…", overview.Catalog);
            Assert.StartsWith("sha256:", overview.Fingerprint);
            Assert.Equal(_install, overview.InstallPath);
            Assert.Equal("None", overview.Overlays);
            Assert.Equal("Compatibility", overview.ModeName);
            Assert.Equal("Switch to Extended…", overview.SwitchText);
            Assert.Equal("Fullscreen", overview.Display);
            Assert.Equal("No saves yet", overview.Saves);
            Assert.Equal(_profile.Root, overview.Folder);
            Assert.True(overview.HasNoMods);
            Assert.Equal(["The install folder holds Kamidori Alchemy Meister",
                          "The catalog is the one this profile accepted",
                          "The profile folder can be written",
                          "Every enabled mod is found, and the mode admits it",
                          "The mods link",
                          "No game is running on this profile"],
                         overview.CheckLines.Select(line => line.Text));
            Assert.All(overview.CheckLines, line => Assert.Equal("✓", line.Mark));
            Assert.False(overview.ShowLocate);
            Assert.False(overview.ShowAccept);

            if (kamidori.Launcher?.Site is { } site)
            {
                Assert.StartsWith(site.Host, overview.Publisher);
                overview.OpenPublisher.Execute(null);
                Assert.Equal($"Open {site.AbsoluteUri}", host.Calls[^1]);
            }
            overview.OpenFolder.Execute(null);
            Assert.Equal($"Open {_profile.Root}", host.Calls[^1]);

            overview.ManageMods.Execute(null);
            Assert.Equal(ProfileTab.Mods, model.Page.Tab);
        }

        File.WriteAllText(Path.Combine(_profile.Root, "OME.INI"), "[display]\nWindowWidth=1280\nWindowHeight=720\n");
        File.WriteAllText(Path.Combine(_profile.Root, "SYS4REG.INI"), "[display]\r\nScreenMode=1\r\n");
        var (again, _) = await Open("ja");
        using (again)
        {
            Assert.Equal("ウィンドウ、1280 × 720", again.Page!.Overview.Display);
            Assert.Equal("インストール先に Kamidori Alchemy Meister があります", again.Page.Overview.CheckLines[0].Text);
        }
    });

    [Fact]
    public void AMissingInstallIsLocatedAndAFolderOfAnotherGameIsRefused() => Headless.Run(async () =>
    {
        string moved = _home.Install("kamidori", "kamidori-moved");
        string himegari = _home.Install("himegari");
        File.Delete(Path.Combine(_install, Installs.CatalogFile));
        var (model, host) = await Open();
        using (model)
        {
            OverviewViewModel overview = model.Page!.Overview;
            Assert.True(overview.ShowLocate);
            CheckLine install = overview.CheckLines[0];
            Assert.Equal(("✕", "The install folder does not hold Kamidori Alchemy Meister"), (install.Mark, install.Text));
            Assert.True(install.HasDetail);
            Assert.Equal("The catalog was not compared: there is no install", overview.CheckLines[1].Text);
            Assert.True(overview.CheckLines[1].IsNotRun);
            Assert.False(overview.CheckLines[1].HasDetail);
            Assert.Equal("The mods were not linked: there is no install", overview.CheckLines[4].Text);
            Assert.Equal("Known once the install is found", overview.Saves);

            host.Folder = himegari;
            await overview.LocateAsync();
            Assert.True(overview.ResultBad);
            Assert.StartsWith($"{himegari} does not hold Kamidori Alchemy Meister.", overview.Result);
            Assert.Equal(_install, Document.Install.Root);

            host.Folder = null;
            await overview.LocateAsync();
            Assert.Equal(_install, Document.Install.Root);

            host.Folder = moved;
            await overview.LocateAsync();
            Assert.False(overview.ResultBad);
            Assert.Equal($"The profile now uses the install in {moved}.", overview.Result);
            Assert.Equal(moved, Document.Install.Root);
            Assert.Equal(moved, model.Page.InstallPath);
            Assert.Equal(ProfileState.Ready, model.Page.State);
            Assert.False(overview.ShowLocate);
        }
    });

    [Fact]
    public void AChangedCatalogIsAcceptedAsItIsNow() => Headless.Run(async () =>
    {
        string accepted = Document.Install.Fingerprint!;
        TestHome.Catalog(_install, "kamidori", extra: "EXTRA.BIN");
        var (model, _) = await Open();
        using (model)
        {
            OverviewViewModel overview = model.Page!.Overview;
            Assert.Equal(ProfileState.Warning, model.Page.State);
            Assert.True(overview.ShowAccept);
            Assert.Equal(("!", "The catalog changed since this profile accepted it"),
                         (overview.CheckLines[1].Mark, overview.CheckLines[1].Text));

            await overview.AcceptAsync();
            Assert.Equal("The profile accepts the install as it is now.", overview.Result);
            Assert.NotEqual(accepted, Document.Install.Fingerprint);
            Assert.Equal(overview.Fingerprint, Document.Install.Fingerprint);
            Assert.Equal(ProfileState.Ready, model.Page.State);
            Assert.False(overview.ShowAccept);
        }
    });

    [Fact]
    public void TheModeSwitchAsksFirstAndCompatibilityTurnsOffWhatItRefuses() => Headless.Run(async () =>
    {
        _home.Package("kamidori", "test.plain");
        _home.Package("kamidori", "test.lua", target: "extended");
        var (model, _) = await Open();
        using (model)
        {
            OverviewViewModel overview = model.Page!.Overview;

            // Cancelled: nothing changes.
            Task cancelled = overview.SwitchModeAsync();
            await Headless.Until(() => model.HasConfirm, "the question");
            Assert.Equal("Switch to Extended?", model.Confirm!.Title);
            model.Confirm.No.Execute(null);
            await cancelled;
            Assert.False(model.HasConfirm);
            Assert.Equal(ProfileMode.Native, Document.Mode);

            Task up = overview.SwitchModeAsync();
            await Headless.Until(() => model.HasConfirm, "the question");
            model.Confirm!.Yes.Execute(null);
            await up;
            Assert.Equal(ProfileMode.Extended, Document.Mode);
            Assert.Equal("This profile is now in Extended mode.", overview.Result);
            Assert.Equal("Switch to Compatibility…", overview.SwitchText);
            Assert.Equal("Extended", model.Page.ModeText);

            // Both packages enabled, as the Mods page will (§6.5); the Overview lists them in load order.
            ProfileStore.Update(model.Page.Entry, document =>
            {
                var folders = Library.Index(document);
                return Library.Enable(Library.Enable(document, "test.plain", folders).Document, "test.lua", folders).Document;
            }, _home.Home);
            model.Page.Row.Entry = model.Page.Entry with { Loaded = ProfileDocuments.Read(_profile.Root) };
            await model.Page.CheckAsync();
            Assert.Equal([("test.plain", "1.2.0"), ("test.lua", "1.2.0")],
                         overview.Mods.Select(mod => (mod.Name, mod.Version)));

            Task down = overview.SwitchModeAsync();
            await Headless.Until(() => model.HasConfirm, "the question");
            Assert.Equal("Switch to Compatibility?", model.Confirm!.Title);
            Assert.Equal(["test.lua"], model.Confirm.Items);
            Assert.True(model.Confirm.Danger);
            model.Confirm.Yes.Execute(null);
            await down;
            Assert.Equal(ProfileMode.Native, Document.Mode);
            Assert.Equal(["test.plain"], Document.Mods!.Enabled);
            Assert.Equal("This profile is now in Compatibility mode. Turned off: test.lua.", overview.Result);
            Assert.Equal(ProfileState.Ready, model.Page.State);
            Assert.Equal([("test.plain", "1.2.0")], overview.Mods.Select(mod => (mod.Name, mod.Version)));
        }
    });

    [Fact]
    public void AWriteTheGamesLockRefusesChangesNothing() => Headless.Run(async () =>
    {
        var (model, _) = await Open();
        using (model)
        {
            OverviewViewModel overview = model.Page!.Overview;
            // An engine started elsewhere while the page was open: the page has not seen it yet.
            Assert.True(RunLock.TryAcquire(_profile.Root, markStart: true, out RunLock? engine, out _));
            using (engine)
            {
                Task up = overview.SwitchModeAsync();
                await Headless.Until(() => model.HasConfirm, "the question");
                model.Confirm!.Yes.Execute(null);
                await up;
            }
            Assert.True(overview.ResultBad);
            Assert.StartsWith("The profile was not changed.", overview.Result);
            Assert.Equal(ProfileMode.Native, Document.Mode);
        }
    });
}

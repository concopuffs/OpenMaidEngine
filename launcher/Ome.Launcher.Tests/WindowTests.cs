using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Ome.Launcher;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Launcher.Views;
using Ome.Mods.Launch;

/// <summary>The window drawn headless (launcher plan §6.1-§6.3): both themes take the mock's palette, the text is set in
/// the bundled Noto Sans JP, and each state of the frame draws with no warning. The frames are written to
/// <c>build/launcher-shots/</c>.</summary>
public sealed class WindowTests : IDisposable
{
    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public void TheWindowDrawsInBothThemesWithTheMocksPaletteAndTheLaunchersFont()
    {
        Headless.Run(() =>
        {
            foreach ((ThemeVariant theme, string name, uint side, uint win) in new[]
                     {
                         (ThemeVariant.Light, "light", 0xeeeeebu, 0xf7f7f5u),
                         (ThemeVariant.Dark, "dark", 0x1f2023u, 0x18191bu),
                     })
            {
                var window = new MainWindow { DataContext = new MainViewModel(_home.Session(language: "ja")) };
                var frame = Headless.Render(window, theme, $"6.1-frame-{name}");
                Assert.Equal(side, Headless.Pixel(frame, 150, 600));
                Assert.Equal(win, Headless.Pixel(frame, 1150, 730));

                TextBlock heading = window.GetLogicalDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("heading"));
                Assert.Equal("プロファイル", heading.Text);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(heading.FontFamily), out var face));
                Assert.Equal("Noto Sans JP", face!.FamilyName);
                window.Close();
            }
        });
    }

    /// <summary>The mock's list: two Kamidori profiles and one Himegari profile.</summary>
    private Dictionary<string, ProfileEntry> Three()
    {
        string kamidori = _home.Install("kamidori");
        string himegari = _home.Install("himegari");
        return new[]
        {
            _home.Profile("kamidori-english", "kamidori", kamidori, "Kamidori (English)"),
            _home.Profile("kamidori-ja", "kamidori", kamidori, "神採り (日本語)"),
            _home.Profile("himegari", "himegari", himegari, "Himegari"),
        }.ToDictionary(profile => profile.Id);
    }

    private void Select(string id)
        => _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { LastProfile = id, Art = ArtConsent.Never });

    /// <summary>Draws the window over a model whose first work is done, in each theme, as <c>&lt;task&gt;-&lt;name&gt;-&lt;theme&gt;</c>.
    /// <paramref name="session"/> makes the model's session (by default a requested window in
    /// <paramref name="language"/>); <paramref name="before"/> runs once the window shows, before it is drawn.</summary>
    private void Draw(string name, string language = "en", Action<MainViewModel>? check = null, string task = "6.2",
                      Func<LauncherSession>? session = null, Func<MainViewModel, Task>? before = null,
                      Func<MainViewModel, Task>? after = null, params ThemeVariant[] themes)
    {
        Headless.Run(async () =>
        {
            foreach (ThemeVariant theme in themes.Length == 0 ? [ThemeVariant.Light, ThemeVariant.Dark] : themes)
            {
                var model = new MainViewModel(session?.Invoke() ?? _home.Session(language: language),
                                              new ArtOptions(Handler: new Refuse()));
                await model.StartAsync();
                var window = new MainWindow { DataContext = model };
                if (before != null) await before(model);
                Headless.Render(window, theme, $"{task}-{name}-{(theme == ThemeVariant.Dark ? "dark" : "light")}");
                check?.Invoke(model);
                if (after != null) await after(model);
                window.Close();
            }
        });
    }

    [Fact]
    public void TheListAndAReadyProfilesHeaderDraw()
    {
        Three();
        Select("kamidori-english");
        Draw("ready", check: model => Assert.Equal(ProfileState.Ready, model.Page!.State));
        Draw("ready-ja", "ja", themes: ThemeVariant.Dark);
    }

    [Fact]
    public void AMissingInstallsNoticeAndARunningProfileDraw()
    {
        Three();
        Select("himegari");
        File.Delete(Path.Combine(_home.Root, "installs", "himegari", Installs.CatalogFile));
        Draw("install-missing", check: model => Assert.True(model.Page!.NoticeBad), themes: ThemeVariant.Light);

        Select("kamidori-ja");
        Assert.True(RunLock.TryAcquire(Path.Combine(_home.Home.ProfilesDirectory, "kamidori-ja"), markStart: true,
                                       out RunLock? engine, out _));
        using (engine) Draw("running", check: model => Assert.True(model.Page!.IsRunning));
    }

    [Fact]
    public void AFailedStartAndAProfileThisLauncherRunsDraw()
    {
        ProfileEntry english = Three()["kamidori-english"];
        Select("kamidori-english");
        string log = Path.Combine(english.Root, EngineLaunch.DiagnosticsFolder, EngineLaunch.LogFile);
        string[] tail =
        [
            "[launcher] OME --rendering-driver d3d12 -- --data-root " + english.Root,
            "OME: game kamidori, profile root " + english.Root,
            "OME: the mods link (2 packages)",
            "ERROR: the scene cannot be loaded: res://synthetic/missing.tscn",
            "   at: load (core/io/resource_loader.cpp:290)",
            "[launcher] the engine exited with 3",
        ];
        Headless.Run(() => Assert.Equal(ProfileState.Ready, ProfilePageStateOf(english)));
        Draw("failed-start", task: "6.3",
             session: () => _home.Session(WindowReason.StartFailed, profile: english,
                                          launch: new LaunchResult(true, 3, null, log, tail,
                                                                   ProfileChecks.Run(english, _home.CheckOptions))),
             check: model =>
             {
                 Assert.True(model.Page!.HasFailure);
                 Assert.Equal("kamidori-english", model.Selected!.Id);
             });

        string signal = Path.Combine(_home.Root, "let-the-engine-exit");
        Task<LaunchFailure?>? play = null;
        Draw("running-here", task: "6.3", session: () => _home.Session(engine: TestHome.StubEngine("--wait-for", signal)),
             before: async model =>
             {
                 if (File.Exists(signal)) File.Delete(signal);
                 play = model.PlayAsync(model.Selected!);
                 await Headless.Until(() => model.Selected!.RunningHere, "the engine to start");
             },
             check: model =>
             {
                 Assert.True(model.Page!.IsRunning);
                 Assert.True(model.Page.IsLocked);
             },
             after: async _ =>
             {
                 File.WriteAllText(signal, "");
                 Assert.Null(await play!);
             });
    }

    [Fact]
    public void TheOverviewDraws()
    {
        Dictionary<string, ProfileEntry> profiles = Three();
        Select("kamidori-english");
        _home.Package("kamidori", "community.font-fix");
        _home.Package("kamidori", "community.random-enemies", target: "extended");
        ProfileStore.Update(profiles["kamidori-english"], document =>
        {
            var folders = Library.Index(document);
            document = Library.Enable(document with { Mode = Ome.Mods.Launch.ProfileMode.Extended }, "community.font-fix",
                                      folders).Document;
            return Library.Enable(document, "community.random-enemies", folders).Document;
        }, _home.Home);
        Draw("overview", task: "6.4", check: model => Assert.Equal(2, model.Page!.Overview.Mods.Count));
        Draw("overview-ja", "ja", task: "6.4", themes: ThemeVariant.Dark);

        Task? asked = null;
        Draw("switch-question", task: "6.4", themes: ThemeVariant.Light,
             before: async model =>
             {
                 asked = model.Page!.Overview.SwitchModeAsync();
                 await Headless.Until(() => model.HasConfirm, "the question");
             },
             check: model => Assert.Equal(["community.random-enemies"], model.Confirm!.Items),
             after: async model =>
             {
                 model.Confirm!.No.Execute(null);
                 await asked!;
             });

        string install = profiles["kamidori-english"].Loaded!.Document.Install.Root;
        TestHome.Catalog(install, "kamidori", extra: "EXTRA.BIN");
        Draw("fingerprint", task: "6.4", themes: ThemeVariant.Light,
             check: model => Assert.True(model.Page!.Overview.ShowAccept));
        File.Delete(Path.Combine(install, Ome.Launcher.Core.Installs.CatalogFile));
        Draw("install-missing", task: "6.4", themes: ThemeVariant.Dark,
             check: model => Assert.True(model.Page!.Overview.ShowLocate));
    }

    private ProfileState ProfilePageStateOf(ProfileEntry profile)
        => ProfileRowViewModel.StateOf(ProfileChecks.Run(profile, _home.CheckOptions));

    [Fact]
    public void TheFirstRunCardAndTheArtQuestionDraw()
    {
        Draw("empty", themes: ThemeVariant.Light);

        Three();
        Headless.Run(() =>
        {
            var model = new MainViewModel(_home.Session(), new ArtOptions(Handler: new Refuse()));
            Assert.True(model.AskArt);
            var window = new MainWindow { DataContext = model };
            Headless.Render(window, ThemeVariant.Light, "6.2-art-question-light");
            window.Close();
        });
    }

    /// <summary>Fails any request: a render never downloads.</summary>
    private sealed class Refuse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            => throw new InvalidOperationException($"no request was expected: {request.RequestUri}");
    }
}

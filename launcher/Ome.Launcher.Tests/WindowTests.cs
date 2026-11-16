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
        Headless.Run(async () =>
        {
            foreach ((ThemeVariant theme, string name, uint side, uint win) in new[]
                     {
                         (ThemeVariant.Light, "light", 0xeeeeebu, 0xf7f7f5u),
                         (ThemeVariant.Dark, "dark", 0x1f2023u, 0x18191bu),
                     })
            {
                // With no profile the wizard opens over the window; the frame's own colours are measured without it.
                var model = new MainViewModel(_home.Session(language: "ja"));
                await model.StartAsync();
                model.Wizard!.Cancel.Execute(null);
                var window = new MainWindow { DataContext = model };
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
    /// <summary>Three profiles over two installs; <paramref name="kamidoriSettings"/> are the Kamidori install's startup
    /// settings.</summary>
    private Dictionary<string, ProfileEntry> Three((string Key, string Value)[]? kamidoriSettings = null)
    {
        string kamidori = _home.Install("kamidori", settings: kamidoriSettings);
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
    public void TheModsTabDraws()
    {
        Dictionary<string, ProfileEntry> profiles = Three();
        Select("kamidori-english");
        _home.Package("kamidori", "community.font-fix");
        _home.Package("kamidori", "community.random-enemies", target: "extended");
        _home.Package("kamidori", "community.big-font", tables: "[[conflicts]]\nid = \"community.font-fix\"\nreason = \"both set the font\"\n");
        string readme = _home.Package("kamidori", "community.uses-base",
                                      tables: "[[requires]]\nid = \"kamidori-base\"\nversion = \">=1.0\"\n");
        File.WriteAllText(Path.Combine(readme, "README.md"), "# Uses base\n\nNames cells through the base mod's layout.\n");
        ProfileStore.Update(profiles["kamidori-english"], document =>
        {
            var folders = Library.Index(document);
            document = document with { Mode = Ome.Mods.Launch.ProfileMode.Extended };
            foreach (string id in new[] { "community.font-fix", "community.uses-base", "community.big-font" })
                document = Library.Enable(document, id, folders).Document;
            return document;
        }, _home.Home);
        Task Mods(MainViewModel model)
        {
            model.Page!.ShowTab.Execute(ProfileTab.Mods);
            return Task.CompletedTask;
        }
        Draw("mods", task: "6.5", before: Mods,
             check: model =>
             {
                 ModsViewModel mods = model.Page!.Mods;
                 // The base mod sits where the stack loads it: after the ranked packages that are ready, before the one
                 // that requires it. The disabled package keeps its place at the end.
                 Assert.Equal(["community.font-fix", "community.big-font", "kamidori-base", "community.uses-base",
                               "community.random-enemies"], mods.Rows.Select(row => row.Id));
                 Assert.True(mods.Rows[2].IsEngine);
                 Assert.Equal(["Conflict", "Conflict"], mods.Rows.Take(2).Select(row => row.Chip));
                 Assert.Contains(mods.Report, line => line.Text
                     == "[community.big-font, community.font-fix] declares a conflict with community.font-fix: both set the font");
             });
        Draw("mods-ja", "ja", task: "6.5", before: Mods, themes: ThemeVariant.Dark);
        Draw("mods-readme", task: "6.5", themes: ThemeVariant.Light,
             before: async model =>
             {
                 await Mods(model);
                 model.Page!.Mods.Select("community.uses-base");
                 await model.Page.Mods.ReadmeAsync();
             },
             check: model => Assert.True(model.Page!.Mods.HasReadme));
    }

    [Fact]
    public void TheSettingsTabDraws()
    {
        Dictionary<string, ProfileEntry> profiles = Three();
        Select("kamidori-english");
        string root = profiles["kamidori-english"].Root;
        File.WriteAllText(Path.Combine(root, "SYS4REG.INI"), "[display]\r\nScreenMode=1\r\nForceScreen=1\r\n");
        File.WriteAllText(Path.Combine(root, "OME.INI"), "[ui]\nLanguage=en\n[display]\nWindowWidth=800\nWindowHeight=600\n[debug]\nLocatorHud=1\n");
        Task Settings(MainViewModel model)
        {
            model.Page!.ShowTab.Execute(ProfileTab.Settings);
            return Task.CompletedTask;
        }
        Draw("settings", task: "6.6", before: Settings,
             check: model =>
             {
                 SettingsViewModel settings = model.Page!.Settings;
                 Assert.True(settings.Windowed && settings.ForceScreen && settings.LocatorHud);
                 Assert.Equal("800 × 600 (125 %)", settings.SelectedSize!.Label);
                 Assert.Equal("en", settings.SelectedLanguage!.Tag);
             });
        Draw("settings-ja", "ja", task: "6.6", before: Settings, themes: ThemeVariant.Dark);
    }

    [Fact]
    public void TheSavesTabDraws()
    {
        Dictionary<string, ProfileEntry> profiles = Three([("SAVEPATH", "SAVE"), ("REGFILEPATH", "")]);
        Select("kamidori-english");
        string saves = Path.Combine(profiles["kamidori-english"].Root, "SAVE");
        Directory.CreateDirectory(saves);
        foreach (string file in new[] { "SAVE01.DAT", "SAVE01.STH", "SAVE02.DAT", "SAVE02.STH", "SAVE.DAT" })
            File.WriteAllText(Path.Combine(saves, file), file);
        string native = Path.Combine(_home.Root, "installs", "kamidori", "SAVE");
        Directory.CreateDirectory(native);
        File.WriteAllText(Path.Combine(native, "SAVE01.DAT"), "native");
        Task Saves(MainViewModel model)
        {
            model.Page!.ShowTab.Execute(ProfileTab.Saves);
            return Task.CompletedTask;
        }
        Draw("saves", task: "6.7", before: Saves,
             check: model =>
             {
                 SavesViewModel page = model.Page!.Saves;
                 Assert.StartsWith("2 saves · last ", page.Contents);
                 Assert.Equal(["神採り (日本語)"], page.Sources.Select(choice => choice.Label));
                 Assert.True(page.CopyFromNative.CanExecute(null));
             });
        Draw("saves-ja", "ja", task: "6.7", before: Saves, themes: ThemeVariant.Dark);
    }

    [Fact]
    public void TheAdvancedTabDraws()
    {
        string install = Path.Combine(_home.Root, "installs", "kamidori");
        Directory.CreateDirectory(Path.Combine(install, "patch"));
        TestHome.Catalog(install, "kamidori", extra: "TITLE.AGF");
        File.WriteAllBytes(Path.Combine(install, "patch", "TITLE.AGF"), "BM  "u8.ToArray());
        File.WriteAllText(Path.Combine(install, "patch", "MENU.BIN"), "");
        Directory.CreateDirectory(Path.Combine(install, "extras"));
        File.WriteAllText(Path.Combine(install, "extras", "HISTORY.BIN"), "");
        ProfileEntry profile = _home.Profile("kamidori-english", "kamidori", install, "Kamidori (English)");
        ProfileStore.Update(profile, document => document with
        {
            Install = document.Install with { Overlays = [Path.Combine(install, "patch")], AllowBmpAsAgf = true },
            Launch = document.Launch with { LuaErrors = "halt" },
        }, _home.Home);
        _home.Profile("himegari", "himegari", _home.Install("himegari"), "Himegari");
        Select("kamidori-english");
        Task Advanced(MainViewModel model)
        {
            model.Page!.ShowTab.Execute(ProfileTab.Advanced);
            return Task.CompletedTask;
        }
        Draw("advanced", task: "6.8", before: Advanced,
             check: model =>
             {
                 AdvancedViewModel page = model.Page!.Advanced;
                 Assert.Equal([("patch", true), ("extras", false)], page.Overlays.Select(row => (row.Name, row.On)));
                 Assert.Equal("halt", page.LuaErrors!.Value);
             });
        Draw("advanced-ja", "ja", task: "6.8", before: Advanced, themes: ThemeVariant.Dark);
    }

    [Fact]
    public void TheWizardDraws()
    {
        Dictionary<string, ProfileEntry> profiles = Three([("SAVEPATH", "SAVE"), ("REGFILEPATH", "")]);
        string install = Path.Combine(_home.Root, "installs", "kamidori");
        Directory.CreateDirectory(Path.Combine(install, "patch"));
        File.WriteAllText(Path.Combine(install, "patch", "MENU.BIN"), "");
        Directory.CreateDirectory(Path.Combine(install, "SAVE"));
        File.WriteAllText(Path.Combine(install, "SAVE", "SAVE01.DAT"), "native");
        Select("kamidori-english");
        async Task Game(MainViewModel model)
        {
            model.NewProfile.Execute(null);
            WizardViewModel wizard = model.Wizard!;
            wizard.Known.Single(known => known.Root == install).Pick.Execute(null);
            await Headless.Until(() => wizard.CanContinue, "the install recognized");
        }
        async Task Profile(MainViewModel model)
        {
            await Game(model);
            model.Wizard!.Next.Execute(null);
            model.Wizard.ToggleAdvanced.Execute(null);
        }
        Draw("wizard-game", task: "6.9", before: Game,
             check: model => Assert.Equal(["patch"], model.Wizard!.Overlays.Select(row => row.Name)));
        Draw("wizard-profile", task: "6.9", before: Profile,
             check: model => Assert.True(model.Wizard!.HasNativeSaves && model.Wizard.HasCopySources));
        Draw("wizard-profile-ja", "ja", task: "6.9", before: Profile, themes: ThemeVariant.Dark);
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
        Draw("empty", themes: ThemeVariant.Light, check: model => Assert.True(model.HasWizard));
        Draw("empty-card", themes: ThemeVariant.Light, before: model =>
        {
            model.Wizard!.Cancel.Execute(null);
            return Task.CompletedTask;
        });

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

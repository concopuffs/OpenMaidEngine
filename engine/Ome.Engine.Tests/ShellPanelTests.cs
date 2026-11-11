using Ome.Engine.Frontend.Shell;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>
/// The options panel's model (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §5.3-§5.6, §7.1): the layout
/// the panel draws, the draft with 設定's commit model and op 0x142's read-only state, the value readouts, and the port
/// options in OME.INI with their command-line flags. Everything runs on a recording settings host and synthetic keys.
/// </summary>
public class ShellPanelTests
{
    private static readonly ShellStrings Ja = ShellStrings.Japanese;

    private static Sys4StartupSettings Settings(params (string Key, string Value)[] pairs) =>
        Sys4StartupSettings.FromPairs(pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));

    /// <summary>A profile whose ENABLEANTIFONT binds message:UseAntiFont, so every option shows.</summary>
    private static readonly Sys4StartupSettings AntiFont = Settings(("ENABLEANTIFONT", "1"));

    /// <summary>The engine side as a fake: a registry, live volumes and routes, and a log of every call.</summary>
    private sealed class Host : IShellSettingsHost
    {
        public readonly List<string> Calls = [];
        public readonly Dictionary<(string, string), int> Registry = [];
        public readonly int[] Volumes = [10000, 8000, 6050, 10000];
        public readonly bool[] Routes = [true, true, true, false];
        public VmMessageSettings MessageSettings { get; set; } = new(5, 8, 500, 2000);

        public int LoadInt(string section, string key, int defaultValue) =>
            Registry.TryGetValue((section, key), out int value) ? value : defaultValue;

        public void SaveInt(string section, string key, int value)
        {
            Registry[(section, key)] = value;
            Calls.Add($"save {section}:{key}={value}");
        }

        public void SetTextAntialias(bool enabled) => Calls.Add($"antialias {enabled}");
        public int Volume(int category) => Volumes[category];

        public void ApplyVolume(int category, int basisPoints)
        {
            Volumes[category] = basisPoints;
            Calls.Add($"live volume {category}={basisPoints}");
        }

        public void SaveVolume(int category, int basisPoints) => Calls.Add($"save volume {category}={basisPoints}");
        public bool RouteEnabled(int category) => Routes[category];

        public void SetRouteEnabled(int category, bool enabled)
        {
            Routes[category] = enabled;
            Calls.Add($"route {category}={enabled}");
        }

        public void ApplyMessageSettings(VmMessageSettings settings) =>
            Calls.Add($"message {settings.MessageSpeed} {settings.MesWinAlpha} {settings.AutoMessageTime0} " +
                      $"{settings.AutoMessageTime1}; CALLBACK_SETTING");
    }

    // ---- the layout ----

    [Fact]
    public void EveryOptionHasOneRowInItsOwnSection()
    {
        var rows = new List<(string Option, ShellSection Section)>();
        foreach (ShellPanelSection section in ShellPanel.Sections)
            foreach (ShellPanelItem item in section.Groups.SelectMany(g => g.Items))
            {
                if (item is ShellPanelSlider slider)
                {
                    rows.Add((slider.Option, section.Section));
                    if (slider.Switch != null) rows.Add((slider.Switch, section.Section));
                }
                else if (item is ShellPanelSwitch toggle) rows.Add((toggle.Option, section.Section));
                else if (item is ShellPanelChoice choice) rows.Add((choice.Option, section.Section));
            }

        Assert.Equal(rows.Count, rows.Select(r => r.Option).Distinct().Count());
        Assert.Equal(ShellOptions.All.Select(o => o.Name).Concat(ShellPanel.PortSwitches).Concat(ShellPanel.PortChoices)
                         .Order(),
                     rows.Select(r => r.Option).Order());
        foreach (ShellOption option in ShellOptions.All)
            Assert.Equal(option.Section, rows.Single(r => r.Option == option.Name).Section);
        Assert.Equal(Enum.GetValues<ShellSection>(), ShellPanel.Sections.Select(s => s.Section));
        Assert.Equal([ShellSection.Message, ShellSection.Sound, ShellSection.Other],
                     ShellPanel.Sections.Where(s => s.ScriptShared).Select(s => s.Section));
    }

    [Theory]
    [InlineData("message-speed", 5, 95)]      // 設定's 0x414 at 100 - v: 遅い on the left
    [InlineData("window-opacity", 0, 16)]     // 0x415 at 16 - v: 薄い on the left
    [InlineData("auto-wait-voice", 1200, 1200)]
    [InlineData("bgm-volume", 7500, 7500)]
    public void SlidersRunAs設定sDo(string name, int value, int position)
    {
        ShellOption option = ShellOptions.Find(name)!;
        ShellPanelSlider slider = ShellPanel.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Items)
            .OfType<ShellPanelSlider>().Single(s => s.Option == name);

        Assert.Equal(position, ShellPanel.Position(option, slider, value));
        Assert.Equal(value, ShellPanel.ValueAt(option, slider, position));
    }

    [Theory]
    [InlineData(ShellValueFormat.Milliseconds, 0, "ﾉｰｳｴｲﾄ")]
    [InlineData(ShellValueFormat.Milliseconds, 5, "5 ms")]
    [InlineData(ShellValueFormat.Opacity, 8, "50%")]       // 設定's edit: (16 - v) * 100 / 16
    [InlineData(ShellValueFormat.Opacity, 0, "100%")]
    [InlineData(ShellValueFormat.Seconds, 500, "0.5秒")]
    [InlineData(ShellValueFormat.Seconds, 10000, "10.0秒")]
    [InlineData(ShellValueFormat.BasisPoints, 6000, "60%")]
    [InlineData(ShellValueFormat.Percent, 50, "50%")]
    public void ValuesReadNaturally(ShellValueFormat format, int value, string text) =>
        Assert.Equal(text, ShellPanel.Format(Ja, format, value));

    [Fact]
    public void WindowSizesAreTheCanvasScaledToFitThenTheSavedOne()
    {
        Assert.Equal([(800, 600), (1000, 750), (1200, 900)],
                     ShellPanel.WindowSizes(800, 600, (1900, 1000)));
        Assert.Equal([(800, 600)], ShellPanel.WindowSizes(800, 600, (640, 480)));   // 100 % always
        Assert.Equal([(800, 600), (1000, 750), (1280, 720)],
                     ShellPanel.WindowSizes(800, 600, (1100, 800), saved: (1280, 720)));
        // 150 % is 900 rows of game and 30 of menu bar: 920 rows hold only 125 % (750 + 25).
        Assert.Equal([(800, 600), (1000, 750)], ShellPanel.WindowSizes(800, 600, (1900, 920), barHeight: 20));
        Assert.Equal("800 × 600 (ｹﾞｰﾑの画面)", ShellPanel.WindowSizeText(Ja, 800, 600, canvas: true));
    }

    // ---- the draft ----

    [Fact]
    public void TheDraftOpensOnTheEngineValuesSnappedToTheControls()
    {
        var host = new Host { MessageSettings = new VmMessageSettings(150, 8, 1250, 2000) };
        host.Registry[("message", "UseAntiFont")] = 0;
        host.Registry[("sound", "MusicFadeOnVoicePlayingVolume")] = 30;

        var draft = new ShellOptionsDraft(host, AntiFont, readOnly: false);

        Assert.Equal(100, draft.Value("message-speed"));    // the trackbar clamps 150 to its range
        Assert.Equal(1200, draft.Value("auto-wait-voice"));  // a position is 100 ms
        Assert.Equal(0, draft.Value("smooth-text"));
        Assert.Equal(1, draft.Value("lower-bgm-on-voice"));  // the registered default
        Assert.Equal(30, draft.Value("lower-bgm-level"));
        Assert.Equal(6000, draft.Value("se-volume"));
        Assert.Equal(0, draft.Value("voice"));
        Assert.False(draft.HasChanges);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public void SoundAppliesLiveAndCancelPutsItBack()
    {
        var host = new Host();
        var draft = new ShellOptionsDraft(host, AntiFont, readOnly: false);

        Assert.True(draft.Set("se-volume", 2345));
        Assert.True(draft.Set("bgm", 0));
        Assert.True(draft.Set("effect-skip", 1));   // on OK: nothing yet
        Assert.Equal(["live volume 2=2300", "route 1=False"], host.Calls);
        Assert.True(draft.HasChanges);

        host.Calls.Clear();
        draft.Cancel();

        // ｷｬﾝｾﾙ restores the routes, then the volumes, at their opening live values (6050, not the snapped 6000).
        Assert.Equal(["route 1=True", "route 2=True", "route 3=False",
                      "live volume 0=10000", "live volume 1=8000", "live volume 2=6050", "live volume 3=10000"],
                     host.Calls);
        Assert.False(draft.HasChanges);
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("save"));
    }

    [Fact]
    public void CommitAppliesInAgercSettingsApplysOrderThenTheCallbackOnce()
    {
        var host = new Host();
        var draft = new ShellOptionsDraft(host, AntiFont, readOnly: false);
        draft.Set("message-speed", 20);
        draft.Set("wheel-advance", 1);
        host.Calls.Clear();

        Assert.True(draft.Commit());

        Assert.Equal(
        [
            "antialias True", "save message:AdvanceMesOnWheel=1",
            "route 1=True", "route 2=True", "route 3=False", "save sound:MusicFadeOnVoicePlaying=1",
            "save volume 0=10000", "save volume 1=8000", "save volume 2=6000", "save volume 3=10000",
            "save sound:MusicFadeOnVoicePlayingVolume=50", "save system:EffectSkipOnClick=0",
            "message 20 8 500 2000; CALLBACK_SETTING",
        ], host.Calls);
        Assert.False(draft.HasChanges);   // the committed values are the new opening ones
    }

    [Fact]
    public void WithoutEnableAntiFontTheSmoothTextOptionNeitherShowsNorApplies()
    {
        var host = new Host();
        var draft = new ShellOptionsDraft(host, Settings(), readOnly: false);

        Assert.DoesNotContain(draft.Visible, o => o.Name == "smooth-text");
        Assert.False(draft.Set("smooth-text", 0));
        draft.Commit();
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("antialias"));
    }

    [Fact]
    public void WhileOp142IsOffTheDraftIsReadOnly()
    {
        var host = new Host();
        var draft = new ShellOptionsDraft(host, AntiFont, readOnly: true);

        Assert.True(draft.ReadOnly);
        Assert.False(draft.Set("bgm-volume", 0));
        Assert.False(draft.Set("message-speed", 50));
        Assert.False(draft.Commit());
        draft.Cancel();

        Assert.Empty(host.Calls);   // no live change, no registry write, no CALLBACK_SETTING
        Assert.Equal(8000, draft.Value("bgm-volume"));
    }

    // ---- port options ----

    [Fact]
    public void OmeIniReadsEachValueInItsDomain()
    {
        Assert.Equal(PortOptions.Defaults, PortOptions.Parse(""));
        Assert.Equal(new PortOptions("en", 1600, 1200, true, false),
                     PortOptions.Parse("[UI]\nlanguage = EN\n[display]\nWindowWidth=1600\nWindowHeight=1200\n" +
                                       "[debug]\nFpsCounter=1\nLocatorHud=0\n"));
        // Any well-formed tag is kept, normalized; a malformed one, a half-given size and junk read as the defaults.
        Assert.Equal(PortOptions.Defaults with { Language = "pt-br" }, PortOptions.Parse("[ui]\nLanguage=pt_BR\n"));
        Assert.Equal(PortOptions.Defaults,
                     PortOptions.Parse("[ui]\nLanguage=French\n[display]\nWindowWidth=1600\n[debug]\nFpsCounter=yes\n"));
        Assert.Equal(PortOptions.Defaults, PortOptions.Parse("[display]\nWindowWidth=-5\nWindowHeight=99999\n"));
    }

    [Fact]
    public void OmeIniKeepsKeysItDoesNotKnow()
    {
        string written = new PortOptions("en", 0, 0, false, true)
            .Serialize("[ui]\nLanguage=ja\nTheme=dark\n[future]\nKey=1\n");

        Assert.Equal("[ui]\nLanguage=en\nTheme=dark\n[future]\nKey=1\n[display]\nWindowWidth=0\nWindowHeight=0\n" +
                     "[debug]\nFpsCounter=0\nLocatorHud=1\n", written);
        Assert.Equal(new PortOptions("en", 0, 0, false, true), PortOptions.Parse(written));
    }

    [Fact]
    public void OmeIniLivesBesideASandboxsSavesElseInTheUserFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ome-ini-{Guid.NewGuid():N}");
        try
        {
            OmeIniStore store = OmeIniStore.For(Path.Combine(root, "user"), Path.Combine(root, "sandbox"));
            Assert.Equal(Path.Combine(root, "sandbox", "OME.INI"), store.FilePath);
            Assert.Equal(Path.Combine(root, "user", "OME.INI"), OmeIniStore.For(Path.Combine(root, "user"), null).FilePath);
            Assert.Equal(PortOptions.Defaults, store.Load());   // no file yet

            store.Save(new PortOptions("en", 1024, 768, false, false));

            Assert.Equal(new PortOptions("en", 1024, 768, false, false), store.Load());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FlagsWinForTheirRunAndNeverReachTheFile()
    {
        var saved = new PortOptions("ja", 1200, 900, false, false);

        PortOptionsRun run = PortOptionsRun.Resolve(saved, ["--language", "EN", "--fps-counter", "--window-width", "640"]);

        Assert.Equal(new PortOptions("en", 1200, 900, true, false), run.Effective);
        Assert.Equal(PortOptionFlags.Language | PortOptionFlags.FpsCounter | PortOptionFlags.WindowSize, run.Overridden);
        // The panel opened on the run's values; the player changes only the locator. The flags' values stay out.
        PortOptions edited = run.Effective with { LocatorHud = true };
        Assert.Equal(saved with { LocatorHud = true }, run.SavedAfter(run.Effective, edited));
        // Changing a flagged option writes the player's choice.
        Assert.Equal(saved with { Language = "ja" }, run.SavedAfter(run.Effective, run.Effective with { Language = "ja" }));
        Assert.Equal("x-pseudo", PortOptionsRun.Resolve(saved, ["--language", "x-pseudo"]).Effective.Language);
        Assert.Throws<ArgumentException>(() => PortOptionsRun.Resolve(saved, ["--language", "French"]));
        Assert.Throws<ArgumentException>(() => PortOptionsRun.Resolve(saved, ["--language"]));
    }

    [Fact]
    public void TheSavedWindowSizeAppliesUnlessAFlagGivesOne()
    {
        var canvas = new Sys4LogicalCanvas(800, 600);
        var saved = new PortOptions("ja", 1200, 900, false, false);

        Assert.Equal((1200, 900, false), Size(WindowLaunchOptions.Resolve([], canvas, saved)));
        Assert.Equal((1600, 900, true), Size(WindowLaunchOptions.Resolve(["--window-width", "1600"], canvas, saved)));
        Assert.Equal((800, 600, false), Size(WindowLaunchOptions.Resolve([], canvas, PortOptions.Defaults)));

        static (int, int, bool) Size(WindowLaunchOptions options) => (options.Width, options.Height, options.IsOverridden);
    }

    [Fact]
    public void DiagnosticsCopyWithEnglishLabels()
    {
        var diagnostics = new ShellDiagnostics([("diag.os", "Windows 10"), ("diag.gpu", "Example GPU")]);

        Assert.Equal("OS: Windows 10\nGraphics: Example GPU\n", diagnostics.ClipboardText());
    }
}

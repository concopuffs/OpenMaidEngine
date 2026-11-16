using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Launcher.Views;
using Ome.Mods.Launch;

/// <summary>The window drawn headless (launcher plan §6.1, §6.2): both themes take the mock's palette, the text is set in
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
    private void Three()
    {
        string kamidori = _home.Install("kamidori");
        string himegari = _home.Install("himegari");
        _home.Profile("kamidori-english", "kamidori", kamidori, "Kamidori (English)");
        _home.Profile("kamidori-ja", "kamidori", kamidori, "神採り (日本語)");
        _home.Profile("himegari", "himegari", himegari, "Himegari");
    }

    private void Select(string id)
        => _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { LastProfile = id, Art = ArtConsent.Never });

    /// <summary>Draws the window over a model whose first work is done, in each theme.</summary>
    private void Draw(string name, string language = "en", Action<MainViewModel>? check = null, params ThemeVariant[] themes)
    {
        Headless.Run(async () =>
        {
            foreach (ThemeVariant theme in themes.Length == 0 ? [ThemeVariant.Light, ThemeVariant.Dark] : themes)
            {
                var model = new MainViewModel(_home.Session(language: language), new ArtOptions(Handler: new Refuse()));
                await model.StartAsync();
                var window = new MainWindow { DataContext = model };
                Headless.Render(window, theme, $"6.2-{name}-{(theme == ThemeVariant.Dark ? "dark" : "light")}");
                check?.Invoke(model);
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

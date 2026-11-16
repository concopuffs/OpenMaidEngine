using System.Net;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Mods.Launch;

/// <summary>
/// The window's frame (launcher plan §6.2): the profile list grouped by game, each profile checked, the selection kept,
/// the header's state, notice and last played, and the art question. On synthetic installs in a temporary home; no
/// request leaves the process. Each test runs on the headless session's UI thread, since the view models make Avalonia
/// brushes and bitmaps, and Avalonia's UI thread is the first thread that touches it.
/// </summary>
public sealed class FrameTests : IDisposable
{
    private readonly TestHome _home = new();

    public void Dispose() => _home.Dispose();

    /// <summary>Two Kamidori profiles and one Himegari profile, as the mock lists them.</summary>
    private (ProfileEntry KamidoriEnglish, ProfileEntry KamidoriJapanese, ProfileEntry Himegari) Three()
    {
        string kamidori = _home.Install("kamidori");
        string himegari = _home.Install("himegari");
        return (_home.Profile("kamidori-english", "kamidori", kamidori, "Kamidori (English)"),
                _home.Profile("kamidori-ja", "kamidori", kamidori, "神採り (日本語)"),
                _home.Profile("himegari", "himegari", himegari, "Himegari"));
    }

    private MainViewModel Model(string language = "en", ArtOptions? art = null)
        => new(_home.Session(language: language), art ?? new ArtOptions(Handler: new NoRequests()));

    [Fact]
    public void TheListGroupsProfilesUnderTheirGamesBannerAndChecksEach() => Headless.Run(async () =>
    {
        Three();
        using MainViewModel model = Model();
        await model.StartAsync();

        Assert.Equal(["himegari", "kamidori"], model.Groups.Select(group => group.Game));
        Assert.Equal(["Kamidori (English)", "神採り (日本語)"], model.Groups[1].Profiles.Select(row => row.Name));
        Assert.All(model.Groups, group => Assert.True(group.Banner is { IsCard: true, IsBanner: true }));
        Assert.All(model.Rows, row => Assert.Equal(ProfileState.Ready, row.State));
        Assert.All(model.Rows, row => Assert.Equal("Compatibility · no mods", row.Sub));
        Assert.Equal("himegari", model.Selected!.Id);
        Assert.True(model.Selected.IsSelected);
        Assert.Equal("Ready", model.Page!.StatusText);
        Assert.Equal("姫狩りダンジョンマイスター · Himegari Dungeon Meister", model.Page.Titles);
        Assert.Equal("日本語", model.Page.LanguageText);
        Assert.False(model.Page.HasNotice);
    });

    [Fact]
    public void ASelectionIsKeptAsTheLastProfileAndTheWindowOpensAtIt() => Headless.Run(async () =>
    {
        Three();
        using (MainViewModel model = Model())
        {
            await model.SelectAsync(model.Rows.Single(row => row.Id == "kamidori-ja"));
            Assert.Equal("kamidori-ja", LauncherSettingsStore.Load(_home.Home).Settings.LastProfile);
            Assert.Equal(ProfileState.Ready, model.Selected!.State);
        }
        using MainViewModel again = Model();
        Assert.Equal("kamidori-ja", again.Selected!.Id);
    });

    [Fact]
    public void AMissingInstallIsAProblemWhoseNoticeLeadsToTheOverview() => Headless.Run(async () =>
    {
        var (_, _, himegari) = Three();
        File.Delete(Path.Combine(himegari.Loaded!.Document.Install.Root, Installs.CatalogFile));
        using MainViewModel model = Model();
        await model.StartAsync();

        ProfilePageViewModel page = model.Page!;
        Assert.Equal(ProfileState.Problem, page.Row.State);
        Assert.True(page.Row.IsBad);
        Assert.Equal("1 problem", page.StatusText);
        Assert.True(page.NoticeBad);
        Assert.Equal("The install was not found, or it does not hold this profile's game.", page.NoticeText);
        Assert.Contains("locate the install", page.NoticeDetail);
        Assert.False(page.HasNoticeAction, "the Overview is open, so the notice needs no link to it");

        page.Tab = ProfileTab.Mods;
        Assert.True(page.HasNoticeAction);
        Assert.Equal("Show on the Overview page", page.NoticeAction);
        page.NoticeCommand.Execute(null);
        Assert.Equal(ProfileTab.Overview, page.Tab);
        Assert.All(model.Rows.Where(row => row.Id != "himegari"), row => Assert.Equal(ProfileState.Ready, row.State));
    });

    [Fact]
    public void ARunningProfileShowsRunningAndSinceWhenAndItsPlayIsReplaced() => Headless.Run(async () =>
    {
        var (english, _, _) = Three();
        Assert.True(RunLock.TryAcquire(english.Root, markStart: true, out RunLock? engine, out _));
        using (engine)
        {
            _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { LastProfile = english.Id });
            using MainViewModel model = Model();
            await model.StartAsync();

            ProfilePageViewModel page = model.Page!;
            Assert.Equal(ProfileState.Running, page.State);
            Assert.True(page.Row.IsRun);
            Assert.True(page.IsRunning);
            Assert.Equal("Running", page.StatusText);
            Assert.True(page.NoticeRun);
            Assert.StartsWith("The game is running this profile since ", page.NoticeText);
            Assert.StartsWith("Last played ", page.LastPlayed);
        }
    });

    [Fact]
    public void AProfileTheLauncherMadeIsNeverPlayedUntilTheEngineStartsIt() => Headless.Run(async () =>
    {
        var (english, _, _) = Three();
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { LastProfile = english.Id });
        using (MainViewModel model = Model())
            Assert.Equal("Never played", model.Page!.LastPlayed);

        Assert.True(RunLock.TryAcquire(english.Root, markStart: true, out RunLock? engine, out _));
        engine!.Dispose();
        using MainViewModel after = Model("ja");
        Assert.StartsWith("最終プレイ ", after.Page!.LastPlayed);
    });

    [Fact]
    public void TheArtQuestionIsAskedWhenAnImageIsMissingAndItsAnswerIsKept() => Headless.Run(async () =>
    {
        Three();
        var never = new NoRequests();
        using (MainViewModel model = Model(art: new ArtOptions(Handler: never)))
        {
            Assert.True(model.AskArt);
            Assert.Contains("www.eukleia.co.jp", model.ArtQuestion);
            await model.AnswerArtAsync(ArtConsent.Never);
            Assert.False(model.AskArt);
        }
        Assert.Equal(0, never.Requests);
        Assert.Equal(ArtConsent.Never, LauncherSettingsStore.Load(_home.Home).Settings.Art);
        using (MainViewModel model = Model())
            Assert.False(model.AskArt);

        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { Art = ArtConsent.Ask });
        var missing = new NoRequests(HttpStatusCode.NotFound);
        using (MainViewModel model = Model(art: new ArtOptions(Handler: missing)))
        {
            await model.AnswerArtAsync(ArtConsent.Always);
            Assert.All(model.Groups, group => Assert.True(group.Banner!.IsCard));
        }
        Assert.Equal(4, missing.Requests);
        Assert.Equal(ArtConsent.Always, LauncherSettingsStore.Load(_home.Home).Settings.Art);
    });

    [Fact]
    public void WithNoProfileTheWizardOpensOverTheFirstRunCard() => Headless.Run(async () =>
    {
        using MainViewModel model = Model();
        Assert.True(model.HasNoProfiles);
        Assert.False(model.HasPage);
        Assert.Empty(model.Groups);
        Assert.False(model.AskArt, "no game is listed, so no art is missing");
        // The start runs once, however often it is asked for: the window asks again when it opens.
        Task started = model.StartAsync();
        Assert.Same(started, model.StartAsync());
        await started;
        Assert.True(model.HasWizard && model.Wizard!.OnGame);
        model.Wizard.Cancel.Execute(null);
        Assert.False(model.HasWizard);
        Assert.True(model.HasNoProfiles && model.NewProfile.CanExecute(null));
    });

    /// <summary>A handler that answers every request with <paramref name="status"/> and counts them; with none given,
    /// a request fails the test.</summary>
    private sealed class NoRequests(HttpStatusCode? status = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests++;
            if (status == null) throw new InvalidOperationException($"no request was expected: {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(status.Value));
        }
    }
}

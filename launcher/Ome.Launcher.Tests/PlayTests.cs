using System.Text;
using Ome.Launcher.Core;
using Ome.Launcher.ViewModels;
using Ome.Mods.Launch;

/// <summary>
/// Play, running and a failed start (launcher spec §8.5, plan §6.3), with the stub engine on a synthetic Kamidori install:
/// the profile runs until the engine exits, the window follows <c>while_running</c> and the launcher ends after the last
/// engine, a failed start comes forward with the end of the log and the checks again, and the window opens at the launch
/// that made it open (spec §8.6). A recording host stands in for the window.
/// </summary>
public sealed class PlayTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ProfileEntry _profile;
    private readonly string _signal;

    public PlayTests()
    {
        _profile = _home.Profile("kamidori", "kamidori", _home.Install("kamidori"), "Kamidori");
        _signal = Path.Combine(_home.Root, "let-the-engine-exit");
    }

    public void Dispose() => _home.Dispose();

    private void While(WhileRunning mode)
        => _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { WhileRunning = mode, Art = ArtConsent.Never });

    private (MainViewModel Model, Host Host) Model(EngineProgram engine, WindowReason reason = WindowReason.Requested,
                                                   LaunchResult? launch = null)
    {
        var model = new MainViewModel(_home.Session(reason, profile: launch == null ? null : _profile, launch: launch,
                                                    engine: engine), new ArtOptions(Handler: new HttpClientHandler()));
        var host = new Host();
        model.Host = host;
        return (model, host);
    }

    private string Log => File.ReadAllText(Path.Combine(_profile.Root, EngineLaunch.DiagnosticsFolder, EngineLaunch.LogFile),
                                           Encoding.UTF8);

    [Fact]
    public void PlayStartsTheEngineAndThePageIsLockedUntilItExits() => Headless.Run(async () =>
    {
        While(WhileRunning.Stay);
        var (model, host) = Model(TestHome.StubEngine("--wait-for", _signal));
        using (model)
        {
            await model.StartAsync();
            ProfilePageViewModel page = model.Page!;
            Assert.True(page.CanPlay);
            Assert.True(page.Play.CanExecute(null));
            page.Tab = ProfileTab.Mods;

            Task<LaunchFailure?> play = model.PlayAsync(page.Row);
            await Headless.Until(() => page.Row.RunningHere, "the engine to start");
            Assert.True(page.IsRunning);
            Assert.True(page.IsLocked);
            Assert.StartsWith("The game is running this profile since ", page.NoticeText);
            Assert.True(page.NoticeRun);
            Assert.Equal(ProfileTab.Overview, page.Tab);
            Assert.False(page.ShowTab.CanExecute(ProfileTab.Mods));
            Assert.True(page.ShowTab.CanExecute(ProfileTab.Overview));
            Assert.False(page.Play.CanExecute(null));
            Assert.True(model.HasRunningEngine);
            Assert.False(model.CloseRequested(), "the window does not close while the engine it started runs");
            Assert.Equal(["Hide"], host.Calls);

            File.WriteAllText(_signal, "");
            Assert.Null(await play);
            Assert.False(model.HasRunningEngine);
            Assert.Equal(ProfileState.Ready, page.Row.State);
            Assert.True(page.IsUnlocked);
            Assert.Equal(["Hide", "Quit"], host.Calls);
            Assert.Contains($"stub out: {_profile.Root}", Log);
        }
    });

    [Fact]
    public void WhileRunningMinimizesOrHidesTheWindowAndAHiddenLauncherEndsAfterItsEngine() => Headless.Run(async () =>
    {
        While(WhileRunning.Minimize);
        var (minimized, minimizedHost) = Model(TestHome.StubEngine());
        using (minimized)
        {
            await minimized.StartAsync();
            Assert.Null(await minimized.PlayAsync(minimized.Selected!));
            Assert.Equal(["Minimize"], minimizedHost.Calls);
        }

        While(WhileRunning.Close);
        var (closed, closedHost) = Model(TestHome.StubEngine());
        using (closed)
        {
            await closed.StartAsync();
            Assert.Null(await closed.PlayAsync(closed.Selected!));
            Assert.Equal(["Hide", "Quit"], closedHost.Calls);
        }
    });

    [Fact]
    public void AFailedStartComesForwardWithTheLogsEndAndTheChecksRunAgain() => Headless.Run(async () =>
    {
        While(WhileRunning.Close);
        string catalog = Path.Combine(_profile.Loaded!.Document.Install.Root, Installs.CatalogFile);
        var (model, host) = Model(TestHome.StubEngine("--exit-code", "3", "--delete-file", catalog));
        using (model)
        {
            await model.StartAsync();
            ProfilePageViewModel page = model.Page!;
            LaunchFailure? failure = await model.PlayAsync(page.Row);

            Assert.Equal(3, failure!.ExitCode);
            Assert.Equal(["Hide", "Restore"], host.Calls);
            Assert.True(page.HasFailure);
            Assert.Equal("The game stopped with exit code 3.", page.FailureText);
            Assert.Contains("stub out: --exit-code", page.FailureLog);
            Assert.EndsWith("engine.log:", page.FailureLogHeading);
            page.OpenLog.Execute(null);
            Assert.Equal($"Open {failure.LogPath}", host.Calls[^1]);

            // The checks ran again: the engine deleted the catalog, so the install now fails, shown where it is fixed.
            Assert.Equal(ProfileState.Problem, page.State);
            Assert.Equal("The install was not found, or it does not hold this profile's game.", page.NoticeText);
            Assert.False(page.CanPlay);

            page.DismissFailure.Execute(null);
            Assert.False(page.HasFailure);
        }
    });

    [Fact]
    public void AStartTheChecksRefuseShowsTheCheckNotAFailedStart() => Headless.Run(async () =>
    {
        While(WhileRunning.Close);
        var (model, host) = Model(TestHome.StubEngine());
        using (model)
        {
            await model.StartAsync();
            ProfilePageViewModel page = model.Page!;
            File.Delete(Path.Combine(_profile.Loaded!.Document.Install.Root, Installs.CatalogFile));

            Assert.Null(await model.PlayAsync(page.Row));
            Assert.Empty(host.Calls);
            Assert.False(page.HasFailure);
            Assert.Equal(ProfileState.Problem, page.State);
            Assert.Equal("The install was not found, or it does not hold this profile's game.", page.NoticeText);
            Assert.False(File.Exists(Path.Combine(_profile.Root, EngineLaunch.DiagnosticsFolder, EngineLaunch.LogFile)));
        }
    });

    [Fact]
    public void AStartThatCannotBeginSaysWhy() => Headless.Run(async () =>
    {
        While(WhileRunning.Stay);
        var (model, _) = Model(new EngineProgram(Path.Combine(_home.Root, "no-engine-here", "OME"), []));
        using (model)
        {
            await model.StartAsync();
            ProfilePageViewModel page = model.Page!;
            LaunchFailure? failure = await model.PlayAsync(page.Row);
            Assert.False(failure!.Started);
            Assert.Equal("The game was not started.", page.FailureText);
            Assert.Contains("cannot be started", page.FailureReason);
            Assert.Equal(ProfileState.Ready, page.State);
        }
    });

    /// <summary>The window as each outcome of §5.12 opens it: <c>StartFailed</c> at the failed start, <c>CheckFailed</c>
    /// at the page's notice, a refusal the checks do not explain at that refusal, and <c>Review</c> at the root to
    /// review; <c>Requested</c> opens at the last profile (<c>FrameTests</c>).</summary>
    [Fact]
    public void TheWindowOpensAtTheLaunchThatMadeItOpen() => Headless.Run(async () =>
    {
        string install = _profile.Loaded!.Document.Install.Root;
        _home.Profile("another", "kamidori", install, "Another");
        While(WhileRunning.Stay);
        _home.Session().Save(LauncherSettingsStore.Load(_home.Home).Settings with { LastProfile = "another" });

        ProfileCheckResult passed = ProfileChecks.Run(_profile, _home.CheckOptions);
        var crashed = new LaunchResult(true, 5, null, Path.Combine(_profile.Root, "diagnostics", "engine.log"),
                                       ["the last line"], passed);
        var (model, _) = Model(TestHome.StubEngine(), WindowReason.StartFailed, crashed);
        using (model)
        {
            await model.StartAsync();
            Assert.Equal("kamidori", model.Selected!.Id);
            Assert.Equal("The game stopped with exit code 5.", model.Page!.FailureText);
            Assert.Equal("the last line", model.Page.FailureLog);
        }

        var unexplained = new LaunchResult(false, null, "a link was found at saves", null, [], passed);
        var (linked, _) = Model(TestHome.StubEngine(), WindowReason.CheckFailed, unexplained);
        using (linked)
        {
            await linked.StartAsync();   // its checks read the catalog: done before the catalog is deleted below
            Assert.Equal("kamidori", linked.Selected!.Id);
            Assert.Equal("The game was not started.", linked.Page!.FailureText);
            Assert.Equal("a link was found at saves", linked.Page.FailureReason);
            Assert.False(linked.Page.HasFailureLog);
        }

        string catalog = Path.Combine(install, Installs.CatalogFile);
        byte[] kept = File.ReadAllBytes(catalog);
        File.Delete(catalog);
        ProfileCheckResult failed = ProfileChecks.Run(_profile, _home.CheckOptions);
        var refused = new LaunchResult(false, null, "a check failed: Install: gone", null, [], failed);
        var (notice, _) = Model(TestHome.StubEngine(), WindowReason.CheckFailed, refused);
        using (notice)
        {
            await notice.StartAsync();
            Assert.Equal("kamidori", notice.Selected!.Id);
            Assert.False(notice.Page!.HasFailure);
            Assert.Equal("The install was not found, or it does not hold this profile's game.", notice.Page.NoticeText);
        }
        File.WriteAllBytes(catalog, kept);

        var otherHome = new OmeHome(Path.Combine(_home.Root, "other-home"), OmeHomeKind.Explicit);
        string root = _home.Profile("shared", "kamidori", install, "Shared", otherHome).Root;
        LauncherSettings settings = LauncherSettingsStore.Load(_home.Home).Settings;
        ProfileEntry elsewhere = ProfileStore.Open(root, root, settings, () => MachineState.Load(_home.CheckOptions.MachineFolder!));
        Assert.True(elsewhere.NeedsReview);
        using var review = new MainViewModel(_home.Session(WindowReason.Review, profile: elsewhere,
                                                           engine: TestHome.StubEngine()),
                                             new ArtOptions(Handler: new HttpClientHandler()));
        await review.StartAsync();
        Assert.Equal(ProfileState.Review, review.Selected!.State);
        Assert.Equal(root, review.Selected.Entry.Root);
        Assert.Equal("This profile was made outside this launcher. Review it before the launcher opens its files.",
                     review.Page!.NoticeText);
        Assert.False(review.Page.CanPlay);
    });

    [Fact]
    public void AnEngineStartedElsewhereShowsAsRunningWhenTheWindowComesForward() => Headless.Run(async () =>
    {
        ProfileEntry other = _home.Profile("other", "kamidori", _profile.Loaded!.Document.Install.Root, "Other");
        While(WhileRunning.Stay);
        var (model, _) = Model(TestHome.StubEngine());
        using (model)
        {
            await model.StartAsync();
            ProfileRowViewModel row = model.Rows.Single(row => row.Id == other.Id);
            Assert.Equal(ProfileState.Ready, row.State);

            Assert.True(RunLock.TryAcquire(other.Root, markStart: true, out RunLock? engine, out _));
            using (engine)
            {
                await model.RefreshRunningAsync();
                Assert.Equal(ProfileState.Running, row.State);
            }
            await model.RefreshRunningAsync();
            Assert.Equal(ProfileState.Ready, row.State);
        }
    });

    /// <summary>Records what the view models asked of the window.</summary>
    private sealed class Host : IWindowHost
    {
        public List<string> Calls { get; } = [];
        public void Minimize() => Calls.Add("Minimize");
        public void Hide() => Calls.Add("Hide");
        public void Restore() => Calls.Add("Restore");
        public void Quit() => Calls.Add("Quit");
        public void Open(string path) => Calls.Add($"Open {path}");
    }
}

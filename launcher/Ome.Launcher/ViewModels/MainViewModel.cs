using Ome.Engine.Profiles;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

/// <summary>A game's heading in the profile list: its banner once, then its profiles (launcher spec §8.7, §9). Profiles
/// whose game is not known (an unreadable <c>profile.toml</c>) form a last group with no banner.</summary>
public sealed class GameGroupViewModel(string? game, string title, IReadOnlyList<ProfileRowViewModel> profiles) : ViewModel
{
    private ArtViewModel? _banner;

    public string? Game { get; } = game;
    public string Title { get; } = title;
    public IReadOnlyList<ProfileRowViewModel> Profiles { get; } = profiles;

    public ArtViewModel? Banner
    {
        get => _banner;
        set
        {
            Set(ref _banner, value);
            Changed(nameof(HasBanner));
            Changed(nameof(HasNoBanner));
        }
    }

    public bool HasBanner => _banner != null;
    public bool HasNoBanner => _banner == null;
}

/// <summary>
/// The main window (launcher spec §9): the profile list, grouped by game under each game's banner, beside the selected
/// profile's page. The selection is kept as <c>launcher.toml</c>'s <c>last_profile</c>. When the window starts
/// (<see cref="StartAsync"/>) every profile is checked off the UI thread, so each dot shows its state, and the art is
/// downloaded when the player said <c>always</c>; with <c>ask</c> and an image missing, the window asks once, naming the
/// hosts (spec §8.7). Play (<see cref="PlayAsync"/>) starts the engine on a worker thread; the launcher's process stays
/// while any engine it started runs, and the window stays, minimizes or hides meanwhile by <c>while_running</c>
/// (spec §8.5).
/// </summary>
public sealed class MainViewModel : ViewModel, IDisposable
{
    private readonly ArtStore _art;
    private IReadOnlyList<GameGroupViewModel> _groups = [];
    private ProfileRowViewModel? _selected;
    private ProfilePageViewModel? _page;
    private IReadOnlyList<string> _artHosts = [];
    private bool _askArt;
    private int _running;
    private bool _quitWhenIdle;
    private ConfirmViewModel? _confirm;
    private WizardViewModel? _wizard;
    private Task? _started;
    private readonly SemaphoreSlim _lockGate = new(1, 1);

    public MainViewModel(LauncherSession session, ArtOptions? artOptions = null)
    {
        Session = session;
        artOptions ??= new ArtOptions();
        _art = new ArtStore(session.Home, artOptions with { Decoder = artOptions.Decoder ?? ArtViewModel.Probe });
        NewProfile = new Command(OpenWizard, () => _wizard == null);
        AddExisting = new Command(() => { }, () => false);
        Games = new Command(() => { }, () => false);
        LauncherSettings = new Command(() => { }, () => false);
        About = new Command(() => { }, () => false);
        ArtAlways = new Command(() => _ = AnswerArtAsync(ArtConsent.Always));
        ArtNever = new Command(() => _ = AnswerArtAsync(ArtConsent.Never));
        Reload();
    }

    public LauncherSession Session { get; }

    /// <summary>The window and the desktop, as the view models need them; null until the window shows.</summary>
    public IWindowHost? Host { get; set; }

    /// <summary>The launcher's strings, bound as <c>S[key]</c>; a new language tells every binding at once.</summary>
    public LauncherStrings S => Session.Strings;

    public IReadOnlyList<GameGroupViewModel> Groups
    {
        get => _groups;
        private set
        {
            Set(ref _groups, value);
            Changed(nameof(HasProfiles));
            Changed(nameof(HasNoProfiles));
        }
    }

    public IEnumerable<ProfileRowViewModel> Rows => _groups.SelectMany(group => group.Profiles);
    public bool HasProfiles => Rows.Any();
    public bool HasNoProfiles => !HasProfiles;

    public ProfileRowViewModel? Selected => _selected;

    public ProfilePageViewModel? Page
    {
        get => _page;
        private set
        {
            Set(ref _page, value);
            Changed(nameof(HasPage));
        }
    }

    public bool HasPage => _page != null;

    /// <summary>The last check run a selection started, for the tests to wait on.</summary>
    public Task Checking { get; private set; } = Task.CompletedTask;

    public Command NewProfile { get; }

    /// <summary>The new-profile wizard while it is open (spec §8.3).</summary>
    public WizardViewModel? Wizard
    {
        get => _wizard;
        private set
        {
            Set(ref _wizard, value);
            Changed(nameof(HasWizard));
            NewProfile.Refresh();
        }
    }
    public bool HasWizard => _wizard != null;

    /// <summary>Opens the wizard at its first step.</summary>
    public void OpenWizard()
    {
        if (_wizard == null) Wizard = new WizardViewModel(this);
    }

    /// <summary>Closes <paramref name="wizard"/>, by Cancel or once it made the profile.</summary>
    public void CloseWizard(WizardViewModel wizard)
    {
        if (_wizard == wizard) Wizard = null;
    }
    public Command AddExisting { get; }
    public Command Games { get; }
    public Command LauncherSettings { get; }
    public Command About { get; }

    /// <summary>Reads the profile list again and selects <paramref name="select"/>, else the profile the window opened
    /// for, else the last one, else the first.</summary>
    public void Reload(string? select = null)
    {
        List<ProfileEntry> entries = [.. ProfileStore.List(Session.Home, Session.Settings, Session.Machine)];
        if (Session.Profile is { } opened
            && !entries.Any(entry => OmeHome.PathsEqual(entry.Root, opened.Root, Session.Environment.Platform)))
            entries.Add(opened);

        var rows = entries.Select(entry => new ProfileRowViewModel(entry, S, row => _ = SelectAsync(row))).ToList();
        // The launch that opened the window (spec §8.6): its failure is shown on its profile's page.
        if (Session.Launch is { } launch && LaunchFailure.Of(launch) is { } failed && Session.Profile is { } launched)
            foreach (ProfileRowViewModel launchedRow in rows.Where(row => OmeHome.PathsEqual(
                         row.Entry.Root, launched.Root, Session.Environment.Platform)))
                launchedRow.Failure = failed;
        var groups = rows.GroupBy(row => row.Game)
            .Select(group => new GameGroupViewModel(group.Key, GameTitle(group.Key),
                                                    [.. group.OrderBy(row => row.Name, StringComparer.CurrentCulture)]))
            .OrderBy(group => group.Game == null)
            .ThenBy(group => group.Title, StringComparer.CurrentCulture)
            .ToList();
        foreach (GameGroupViewModel group in groups)
            group.Banner = group.Game == null ? null : ArtFor(group.Game, ArtKind.Banner);
        Groups = groups;

        string? wanted = select ?? Session.Profile?.Id ?? Session.Settings.LastProfile;
        ProfileRowViewModel? row = Rows.FirstOrDefault(row => row.Id == wanted) ?? Rows.FirstOrDefault();
        Show(row, save: false);

        _artHosts = Session.Settings.Art == ArtConsent.Ask ? _art.MissingHosts(GamesListed) : [];
        AskArt = _artHosts.Count > 0;
    }

    /// <summary>Selects <paramref name="row"/>, keeps it as the last profile, and checks it.</summary>
    public Task SelectAsync(ProfileRowViewModel row)
    {
        if (row == _selected) return Checking;
        Show(row, save: true);
        return Checking;
    }

    private void Show(ProfileRowViewModel? row, bool save)
    {
        foreach (ProfileRowViewModel each in Rows) each.IsSelected = each == row;
        _selected = row;
        Changed(nameof(Selected));
        _page?.Detach();
        Page = row == null ? null : new ProfilePageViewModel(this, row);
        if (save && row != null && Session.Settings.LastProfile != row.Id)
            Session.Save(Session.Settings with { LastProfile = row.Id });
        Checking = _page?.CheckAsync() ?? Task.CompletedTask;
    }

    /// <summary>The window's first work: the selected profile's checks, then every other profile's, one at a time, so
    /// each dot shows its state; then the automatic downloads of <c>always</c>. It runs once: a second call (the window
    /// opening after a caller started the model) gets the first call's task, so no check pass runs twice or outlives
    /// the one awaited.</summary>
    public Task StartAsync() => _started ??= StartOnceAsync();

    private async Task StartOnceAsync()
    {
        await Checking;
        foreach (ProfileRowViewModel row in Rows.ToList())
        {
            if (row == _selected || !row.Entry.Usable || row.Entry.NeedsReview) continue;
            ProfileEntry entry = row.Entry;
            CheckOptions options = Session.CheckOptions;
            try
            {
                row.State = ProfileRowViewModel.StateOf(await Task.Run(() => ProfileChecks.Run(entry, options)));
            }
            catch (Exception error) when (ProfilePageViewModel.IsCheckFailure(error))
            {
                row.State = ProfileState.Problem;
            }
        }
        await DownloadArtAsync(Session.Settings.Art);
        // With no profile yet, the wizard's first step is the window (spec §8.7); Cancel leaves the first-run card.
        if (!HasProfiles) OpenWizard();
    }

    // ---- questions ----

    /// <summary>The question the window asks over its scrim, or null.</summary>
    public ConfirmViewModel? Confirm
    {
        get => _confirm;
        private set
        {
            Set(ref _confirm, value);
            Changed(nameof(HasConfirm));
        }
    }
    public bool HasConfirm => _confirm != null;

    /// <summary>Asks <paramref name="question"/> and returns the player's answer.</summary>
    public async Task<bool> ConfirmAsync(ConfirmViewModel question)
    {
        Confirm = question;
        try
        {
            return await question.Answer;
        }
        finally
        {
            if (Confirm == question) Confirm = null;
        }
    }

    /// <summary>The publisher's page of <paramref name="game"/> (spec §8.4 Overview), from the game's manifest.</summary>
    public Uri? Site(string game) => _art.Site(game);

    // ---- Play (spec §8.5) ----

    /// <summary>Whether an engine this launcher started still runs: the launcher's process stays until none does.</summary>
    public bool HasRunningEngine => _running > 0;

    /// <summary>Starts <paramref name="row"/>'s profile: the checks run again and the engine starts, on a worker thread,
    /// with the engine of this launcher run (<see cref="LauncherSession.Engine"/>). Once it runs, the window follows
    /// <c>while_running</c>. When it exits the checks run again; a failed start brings the window forward with the end
    /// of the log, and the last engine's normal exit ends a launcher whose window closed.</summary>
    public async Task<LaunchFailure?> PlayAsync(ProfileRowViewModel row, IReadOnlyList<string>? runArguments = null)
    {
        row.Launching = true;
        row.Failure = null;
        // Launching locks the page, so nothing new is written; a write already under way finishes first, and the start
        // runs on the profile as written.
        await OneAtATime(() => Task.FromResult(true));
        SynchronizationContext? ui = SynchronizationContext.Current;
        ProfileEntry entry = row.Entry;
        CheckOptions options = Session.CheckOptions;
        LaunchFailure? failure;
        ProfileCheckResult? checks = null;
        bool refused = true;
        try
        {
            EngineCommand command = EngineCommands.For(Session.Engine, entry, runArguments ?? [],
                                                       Session.Environment.Platform);
            LaunchResult result = await Task.Run(() => EngineLaunch.Run(entry, command, options, _ =>
            {
                // Posted to the UI thread before the exit's continuation, so it runs first.
                if (ui == null) Started(row);
                else ui.Post(_ => Started(row), null);
            }));
            failure = LaunchFailure.Of(result);
            checks = result.Checks;
            refused = !result.Started;
        }
        catch (Exception error) when (ProfilePageViewModel.IsCheckFailure(error))
        {
            failure = new LaunchFailure(false, null, error.Message, null, []);
        }
        finally
        {
            row.Launching = false;
        }

        if (row.RunningHere)
        {
            row.RunningHere = false;
            _running--;
        }
        if (failure != null)
        {
            // A failed start comes forward with the end of the log and the checks run again (spec §8.5).
            row.Failure = failure;
            _quitWhenIdle = false;
            Host?.Restore();
            if (row != _selected) Show(row, save: true);
        }
        // A failed or refused start's checks ran again (spec §8.5): shown as they are, each where it is fixed.
        if (checks != null && (failure != null || refused))
        {
            if (row == _selected) _page!.Show(checks);
            else row.State = ProfileRowViewModel.StateOf(checks);
            return failure;
        }
        await Recheck(row);
        if (failure == null && _quitWhenIdle && _running == 0) Host?.Quit();
        return failure;
    }

    private void Started(ProfileRowViewModel row)
    {
        row.Launching = false;
        row.StartedAt = DateTime.UtcNow;
        row.RunningHere = true;
        row.State = ProfileState.Running;
        _running++;
        switch (Session.Settings.WhileRunning)
        {
            case WhileRunning.Minimize:
                Host?.Minimize();
                break;
            case WhileRunning.Close:
                _quitWhenIdle = true;
                Host?.Hide();
                break;
        }
    }

    /// <summary>The window asks to close: it may, unless an engine this launcher started runs. Then it hides, and the
    /// launcher ends once the last engine exits (spec §8.5: the process stays while an engine it started runs).</summary>
    public bool CloseRequested()
    {
        if (!HasRunningEngine) return true;
        _quitWhenIdle = true;
        Host?.Hide();
        return false;
    }

    /// <summary>The window came forward: a profile an engine started elsewhere holds shows as running, and one whose
    /// engine has gone is checked again (spec §8.5).</summary>
    public async Task RefreshRunningAsync()
    {
        foreach (ProfileRowViewModel row in Rows.ToList())
        {
            if (!row.Entry.Usable || row.Entry.NeedsReview || row.RunningHere || row.Launching) continue;
            bool held = await OneAtATime(() => Task.FromResult(RunLock.IsHeld(row.Entry.Root, out _)));
            if (held != (row.State == ProfileState.Running)) await Recheck(row);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> when no other work that opens a profile's <c>run.lock</c> runs: a write under the
    /// lock and its check run, a page's check run, the running probe. They exclude each other because the probe
    /// (<see cref="RunLock.IsHeld"/>) opens the lock file: a write taking the lock during a probe is refused as if the
    /// game ran, and a probe during a write sees a game that is not there.
    /// </summary>
    public async Task<T> OneAtATime<T>(Func<Task<T>> work)
    {
        await _lockGate.WaitAsync();
        try
        {
            return await work();
        }
        finally
        {
            _lockGate.Release();
        }
    }

    private async Task Recheck(ProfileRowViewModel row)
    {
        if (row == _selected && _page != null)
        {
            await _page.CheckAsync();
            return;
        }
        if (!row.Entry.Usable || row.Entry.NeedsReview) return;
        ProfileEntry entry = row.Entry;
        CheckOptions options = Session.CheckOptions;
        try
        {
            ProfileCheckResult checks = await Task.Run(() => ProfileChecks.Run(entry, options));
            row.State = row.RunningHere ? ProfileState.Running : ProfileRowViewModel.StateOf(checks);
        }
        catch (Exception error) when (ProfilePageViewModel.IsCheckFailure(error))
        {
            row.State = ProfileState.Problem;
        }
    }

    // ---- art ----

    private IEnumerable<string> GamesListed => Rows.Select(row => row.Game).OfType<string>().Distinct();

    /// <summary>The image for <paramref name="game"/>, or null when the game is not known.</summary>
    public ArtViewModel? ArtFor(string? game, ArtKind kind, ProfileEntry? profile = null)
        => game == null ? null : new ArtViewModel(_art.Load(game, kind, profile));

    /// <summary>Whether the window asks whether to download art (spec §8.7): <c>ask</c>, and an image of a listed game
    /// is missing.</summary>
    public bool AskArt
    {
        get => _askArt;
        private set => Set(ref _askArt, value);
    }

    /// <summary>The question's text, naming the hosts a download contacts.</summary>
    public string ArtQuestion => S.Format("art.text", string.Join(", ", _artHosts));

    public Command ArtAlways { get; }
    public Command ArtNever { get; }

    /// <summary>Keeps the player's answer in <c>launcher.toml</c>, and downloads the missing images on <c>always</c>.</summary>
    public async Task AnswerArtAsync(ArtConsent answer)
    {
        AskArt = false;
        Session.Save(Session.Settings with { Art = answer });
        await DownloadArtAsync(answer);
    }

    private async Task DownloadArtAsync(ArtConsent consent)
    {
        IReadOnlyList<ArtDownload> downloads = await _art.DownloadMissingAsync(GamesListed, consent);
        if (!downloads.Any(download => download.Status == ArtDownloadStatus.Stored)) return;
        foreach (GameGroupViewModel group in _groups.Where(group => group.Game != null))
            group.Banner = ArtFor(group.Game, ArtKind.Banner);
        _page?.RefreshArt();
    }

    // ---- the window ----

    /// <summary>Keeps where the window was in <c>launcher.toml</c>.</summary>
    public void SavePlacement(WindowPlacement placement)
    {
        if (Session.Settings.Window != placement) Session.Save(Session.Settings with { Window = placement });
    }

    private static string GameTitle(string? game)
        => game == null ? "" : GameProfileRegistry.BuiltIn.Find(game)?.DisplayTitle ?? game;

    public void Dispose() => _art.Dispose();
}

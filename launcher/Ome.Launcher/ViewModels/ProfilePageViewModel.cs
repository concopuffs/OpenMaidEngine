using System.ComponentModel;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

/// <summary>The tabs of a profile's page (launcher spec §8.4), in the mock's order.</summary>
public enum ProfileTab
{
    Overview,
    Mods,
    Settings,
    Saves,
    Advanced,
}

/// <summary>
/// A profile's page (launcher spec §8.4, §9): the header (cover, name, titles, chips, install path, Play, last
/// played), the notice of a failed check with the action that fixes it or the page that does, and the tabs. Its checks
/// run off the UI thread when the page opens (<see cref="CheckAsync"/>).
/// </summary>
public sealed class ProfilePageViewModel : ViewModel
{
    private readonly MainViewModel _main;
    private ProfileCheckResult? _checks;
    private string? _checkFailure;
    private ProfileTab _tab;

    public ProfilePageViewModel(MainViewModel main, ProfileRowViewModel row)
    {
        _main = main;
        Row = row;
        Cover = main.ArtFor(row.Game, Core.ArtKind.Cover, row.Entry);
        ReloadPort();
        Play = new Command(() => _ = _main.PlayAsync(Row), () => CanPlay);
        Duplicate = new Command(() => _main.ShowDialog(new DuplicateViewModel(_main, this)),
                                () => Entry.Usable && !Entry.NeedsReview && IsUnlocked);
        Shortcut = new Command(() => _main.ShowDialog(new ShortcutViewModel(_main, Entry)),
                               () => Entry.Loaded != null && !Entry.NeedsReview);
        Rename = new Command(() => _main.ShowDialog(new RenameViewModel(_main, this)),
                             () => Entry.Loaded != null && !Entry.NeedsReview && IsUnlocked);
        Delete = new Command(() => _ = DeleteAsync(), () => !Entry.NeedsReview && IsUnlocked);
        NoticeCommand = new Command(RunNoticeAction, () => Entry.NeedsReview || NoticeTarget != null);
        ShowTab = new Command(tab => Tab = (ProfileTab)tab!);
        Overview = new OverviewViewModel(this);
        Mods = new ModsViewModel(this);
        Settings = new SettingsViewModel(this);
        Saves = new SavesViewModel(this);
        Advanced = new AdvancedViewModel(this);
        OpenLog = new Command(() => { if (Row.Failure?.LogPath is { } log) _main.Host?.Open(log); },
                              () => Row.Failure?.LogPath != null);
        DismissFailure = new Command(() => Row.Failure = null);
        row.PropertyChanged += RowChanged;
    }

    public LauncherStrings S => _main.S;
    public MainViewModel Main => _main;
    public ProfileRowViewModel Row { get; }
    public OverviewViewModel Overview { get; }
    public ModsViewModel Mods { get; }
    public SettingsViewModel Settings { get; }
    public SavesViewModel Saves { get; }
    public AdvancedViewModel Advanced { get; }
    public ProfileEntry Entry => Row.Entry;
    private ProfileDocument? Document => Entry.Loaded?.Document;
    public ArtViewModel? Cover { get; private set; }
    public PortOptions? Port { get; private set; }

    /// <summary>Reads the profile's <c>OME.INI</c> again, after the Settings page wrote it.</summary>
    public void ReloadPort()
    {
        Port = Row.Entry.Usable && !Row.Entry.NeedsReview
            ? new OmeIniStore(Path.Combine(Row.Entry.Root, PortOptions.FileName)).Load()
            : null;
        Changed(nameof(Port));
        Changed(nameof(LanguageText));
    }

    /// <summary>The install's catalog once the checks found the profile's game there. A missing overlay fails the
    /// install's check but not this: the catalog, its startup settings and the overlay candidates stay known.</summary>
    public Sys4AssetCatalog? Catalog
        => _checks?.Install is { Kind: InstallKind.Supported, Problem: null, Catalog: { } catalog, Game: { } game }
           && game.Id == Entry.Loaded?.Document.Game
            ? catalog
            : null;

    public ProfileCheckResult? Checks
    {
        get => _checks;
        private set
        {
            _checks = value;
            ChangedAll();
            Overview.Refresh();
            Mods.Refresh();
            Settings.Refresh();
            Saves.Refresh();
            Advanced.Refresh();
        }
    }

    public string Name => Entry.DisplayName;

    /// <summary>The game's titles, the original first, as the mock's header line.</summary>
    public string Titles
    {
        get
        {
            GameProfileManifest? game = Document == null ? null : GameProfileRegistry.BuiltIn.Find(Document.Game);
            if (game == null) return Document?.Game ?? "";
            string? original = game.CatalogIdentities.FirstOrDefault()?.Title;
            return original == null || original == game.DisplayTitle
                ? game.DisplayTitle
                : S.Format("header.titles", original, game.DisplayTitle);
        }
    }

    public string InstallPath => Entry.NeedsReview || Document == null ? Entry.Root : Document.Install.Root;

    // ---- chips ----

    public ProfileState State => Row.State;
    public string StatusText => State switch
    {
        ProfileState.Checking => S["status.checking"],
        ProfileState.Running => S["status.running"],
        ProfileState.Review => S["status.review"],
        ProfileState.Unreadable => S["status.unreadable"],
        ProfileState.Problem => Math.Max(1, Count(CheckStatus.Failed)) is var failed && failed == 1
            ? S["status.problem"]
            : S.Format("status.problems", failed),
        ProfileState.Warning => Count(CheckStatus.Warning) is var warnings && warnings == 1
            ? S["status.warning"]
            : S.Format("status.warnings", warnings),
        _ => S["status.ready"],
    };
    public bool StatusOk => State == ProfileState.Ready;
    public bool StatusWarn => State is ProfileState.Warning or ProfileState.Review;
    public bool StatusBad => State is ProfileState.Problem or ProfileState.Unreadable;
    public bool StatusRun => State == ProfileState.Running;
    public bool HasDocument => Document != null && !Entry.NeedsReview;
    public string ModeText => Document == null ? "" : ProfileRowViewModel.ModeName(S, Document.Mode);
    public string ModsText => ProfileRowViewModel.ModCount(S, ProfileRowViewModel.EnabledCount(Entry));
    public string LanguageText => Port == null ? "" : LanguageName(S, Port.Language);

    /// <summary>The launcher's name for a language tag: its own table's, else the tag.</summary>
    public static string LanguageName(LauncherStrings strings, string tag)
        => strings.Has($"language.{tag}") ? strings[$"language.{tag}"] : tag;

    // ---- Play, last played, the links ----

    public Command Play { get; }
    public Command Duplicate { get; }
    public Command Shortcut { get; }

    /// <summary>Under More (spec §8.4): the name, never the id.</summary>
    public Command Rename { get; }

    /// <summary>Under More: the profile's folder goes, with everything in it, once the player says so.</summary>
    public Command Delete { get; }

    /// <summary>Asks, then deletes the profile's folder (spec §4.2: a profile is the unit of removal) and forgets it.
    /// Core refuses while the game runs; the refusal is told.</summary>
    public async Task DeleteAsync()
    {
        ProfileEntry entry = Entry;
        int? saves = null;
        if (Catalog is { } catalog)
            try
            {
                saves = ProfileSaves.Count(ProfileStore.SaveDirectory(catalog.StartupSettings, entry.Root)).Saves;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
            }
        string text = S["confirm.delete.text"] + (saves is { } count
            ? " " + S.Format(count == 1 ? "confirm.delete.save" : "confirm.delete.saves", count)
            : "");
        if (!await _main.ConfirmAsync(new ConfirmViewModel(
                S.Format("confirm.delete.title", entry.DisplayName), text, S["confirm.delete.yes"], S["confirm.cancel"],
                [entry.Root], S["confirm.delete.items"], danger: true)))
            return;
        try
        {
            OmeHome home = _main.Session.Home;
            LauncherSettings settings = _main.Session.Settings;
            settings = await _main.OneAtATime(() => Task.Run(() => ProfileStore.Delete(home, settings, entry)));
            _main.Session.Save(settings);
        }
        catch (Exception error) when (error is ProfileBusyException or IOException or UnauthorizedAccessException)
        {
            await _main.TellAsync(S["delete.failed"], error.Message);
            return;
        }
        await _main.Dropped(entry.Root);
    }
    public bool IsRunning => State == ProfileState.Running;
    public bool IsNotRunning => !IsRunning;

    /// <summary>Play starts the profile when its checks passed and nothing runs it (spec §8.5).</summary>
    public bool CanPlay => _checks is { CanPlay: true } && !Row.Launching && !IsRunning && !Entry.NeedsReview;

    /// <summary>"Play", or "Starting…" while the checks run again before the engine starts.</summary>
    public string PlayText => Row.Launching ? S["header.starting"] : S["header.play"];

    /// <summary>While the game runs (or starts), nothing that writes one of the profile's files can be used: the Mods
    /// page, the mode's switch, Settings, Advanced and copying saves in (spec §8.4). Every tab can still be looked at,
    /// as in the mock.</summary>
    public bool IsLocked => IsRunning || Row.Launching;
    public bool IsUnlocked => !IsLocked;

    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        ChangedAll();
        Overview.Refresh();
        Mods.Refresh();
        Settings.Refresh();
        Saves.Refresh();
        Advanced.Refresh();
        Play.Refresh();
        OpenLog.Refresh();
        Duplicate.Refresh();
        Shortcut.Refresh();
        Rename.Refresh();
        Delete.Refresh();
        NoticeCommand.Refresh();
    }

    /// <summary>Writes a change to the profile's files off the UI thread (<paramref name="write"/> holds the profile's
    /// lock), keeps the profile as written (a write that returns null left <c>profile.toml</c> as it was), reads its
    /// <c>OME.INI</c> again, and checks it again. A refused write (the game holds the lock, a file cannot be written)
    /// changes nothing and is reported to <paramref name="failed"/>, by default on the Overview. The write and its
    /// check run take their turn with the window's other uses of the lock (<see cref="MainViewModel.OneAtATime"/>).</summary>
    public Task<bool> WriteAsync(Func<ProfileEntry, LoadedProfile?> write, Action<string>? failed = null)
        => _main.OneAtATime(async () =>
        {
            ProfileEntry entry = Entry;
            try
            {
                if (await Task.Run(() => write(entry)) is { } loaded) Row.Entry = entry with { Loaded = loaded };
                ReloadPort();
            }
            catch (Exception error) when (error is ProfileBusyException or ProfileFileException || IsCheckFailure(error))
            {
                (failed ?? Overview.WriteFailed)(error.Message);
                return false;
            }
            await CheckNowAsync();
            return true;
        });

    /// <summary>Stops listening to the row: the page is no longer shown.</summary>
    public void Detach() => Row.PropertyChanged -= RowChanged;

    // ---- a failed start (spec §8.5) ----

    public bool HasFailure => Row.Failure != null;

    /// <summary>What happened: the start was refused, the engine refused (exit code 2), or the game stopped with an
    /// exit code other than 0.</summary>
    public string FailureText => Row.Failure switch
    {
        null => "",
        { Started: false } => S["failure.not-started"],
        { ExitCode: 2 } => S["failure.refused"],
        { ExitCode: var code } => S.Format("failure.exit", code),
    };

    /// <summary>Why a start was refused, in Core's words.</summary>
    public string? FailureReason => Row.Failure is { Started: false, Refusal: { } refusal } ? refusal : null;
    public bool HasFailureReason => FailureReason != null;

    /// <summary>The end of <c>diagnostics/engine.log</c>.</summary>
    public string FailureLog => string.Join(Environment.NewLine, Row.Failure?.LogTail ?? []);
    public bool HasFailureLog => Row.Failure is { LogTail.Count: > 0 };
    public string FailureLogHeading => S.Format("failure.log", Row.Failure?.LogPath ?? "");
    public Command OpenLog { get; }
    public Command DismissFailure { get; }

    public string LastPlayed => RunLock.LastStart(Entry.Root) is { } start
        ? S.Format("header.last-played", start.ToLocalTime().ToString(S["date.short"], S.Culture))
        : S["header.never-played"];

    // ---- the notice ----

    /// <summary>What the notice shows: running first, then the first failed check, then the first warning; the
    /// review and an unreadable profile before any check.</summary>
    private (string Text, string? Detail, string Class, ProfileTab? Target)? Notice
    {
        get
        {
            if (Entry.NeedsReview) return (S["notice.review"], null, "warn", null);
            if (!Entry.Usable) return (S["notice.unreadable"], Entry.Problem, "bad", null);
            if (_checkFailure != null) return (S["notice.checks-failed"], _checkFailure, "bad", null);
            if (Row.RunningHere || _checks is { Running: true })
            {
                // An engine this launcher started runs from when the launcher saw it start; one started elsewhere
                // from the time it marked on run.lock.
                DateTime? start = Row.RunningHere ? Row.StartedAt : RunLock.LastStart(Entry.Root);
                string text = start is { } since
                    ? S.Format("notice.running", since.ToLocalTime().ToString(S["time.short"], S.Culture))
                    : S["notice.running-unknown"];
                return (text, null, "run", null);
            }
            if (_checks is not { } checks) return null;
            ProfileCheck? check = checks.Checks.FirstOrDefault(check => check.Status == CheckStatus.Failed)
                                  ?? checks.Checks.FirstOrDefault(check => check.Status == CheckStatus.Warning);
            if (check == null) return null;
            string cls = check.Status == CheckStatus.Failed ? "bad" : "warn";
            return check.Kind switch
            {
                ProfileCheckKind.Install when checks.MissingOverlays.Count > 0
                    => (S["notice.overlay"], check.Message, cls, ProfileTab.Advanced),
                ProfileCheckKind.Install => (S["notice.install"], check.Message, cls, ProfileTab.Overview),
                ProfileCheckKind.Fingerprint => (S["notice.fingerprint"], check.Message, cls, ProfileTab.Overview),
                ProfileCheckKind.Root => (S["notice.root"], check.Message, cls, null),
                ProfileCheckKind.Packages => (S["notice.packages"], check.Message, cls, ProfileTab.Mods),
                ProfileCheckKind.Link when check.Status == CheckStatus.Warning
                    => (S["notice.link-skip"], check.Message, cls, ProfileTab.Mods),
                ProfileCheckKind.Link => (S["notice.link"], check.Message, cls, ProfileTab.Mods),
                _ => (check.Message, null, cls, null),
            };
        }
    }

    public bool HasNotice => Notice != null;
    public string NoticeText => Notice?.Text ?? "";
    public string? NoticeDetail => Notice?.Detail;
    public bool HasNoticeDetail => !string.IsNullOrEmpty(NoticeDetail);
    public bool NoticeBad => Notice?.Class == "bad";
    public bool NoticeWarn => Notice?.Class == "warn";
    public bool NoticeRun => Notice?.Class == "run";
    private ProfileTab? NoticeTarget => Notice?.Target;
    public bool HasNoticeAction => Entry.NeedsReview || NoticeTarget != null && NoticeTarget != Tab;
    public string NoticeAction => Entry.NeedsReview ? S["action.review"]
        : NoticeTarget == ProfileTab.Mods ? S["action.mods"]
        : NoticeTarget == ProfileTab.Advanced ? S["action.advanced"]
        : S["action.overview"];
    public Command NoticeCommand { get; }

    private void RunNoticeAction()
    {
        if (Entry.NeedsReview) _ = _main.OpenReviewAsync(Entry);
        else if (NoticeTarget is { } target) Tab = target;
    }

    // ---- the tabs ----

    public ProfileTab Tab
    {
        get => _tab;
        set
        {
            if (!Set(ref _tab, value)) return;
            Changed(nameof(HasNoticeAction));
            Changed(nameof(OnOverview));
            Changed(nameof(OnMods));
            Changed(nameof(OnSettings));
            Changed(nameof(OnSaves));
            Changed(nameof(OnAdvanced));
        }
    }

    /// <summary>Shows the tab its parameter names.</summary>
    public Command ShowTab { get; }

    public bool OnOverview => _tab == ProfileTab.Overview;
    public bool OnMods => _tab == ProfileTab.Mods;
    public bool OnSettings => _tab == ProfileTab.Settings;
    public bool OnSaves => _tab == ProfileTab.Saves;
    public bool OnAdvanced => _tab == ProfileTab.Advanced;

    /// <summary>The Mods tab's count: the failed checks the Mods page fixes.</summary>
    public int ModsProblems => _checks?.Checks.Count(check => check.Kind is ProfileCheckKind.Packages or ProfileCheckKind.Link
                                                              && check.Status == CheckStatus.Failed) ?? 0;
    public bool HasModsProblems => ModsProblems > 0;

    // ---- the checks ----

    private int Count(CheckStatus status)
        => _checks?.Checks.Count(check => check.Status == status && check.Kind != ProfileCheckKind.Lock) ?? 0;

    /// <summary>Runs the checks off the UI thread and shows their result (launcher spec §8.5). A profile under review or
    /// one that is not usable is not checked. Checks that cannot run (this machine's state is unreadable) show why.</summary>
    public Task CheckAsync() => _main.OneAtATime(async () =>
    {
        await CheckNowAsync();
        return true;
    });

    /// <summary>The check run itself, for a caller that already has its turn.</summary>
    private async Task CheckNowAsync()
    {
        if (!Entry.Usable || Entry.NeedsReview) return;
        ProfileEntry entry = Entry;
        CheckOptions options = _main.Session.CheckOptions;
        try
        {
            ProfileCheckResult result = await Task.Run(() => ProfileChecks.Run(entry, options));
            Show(result);
        }
        catch (Exception error) when (IsCheckFailure(error))
        {
            _checkFailure = error.Message;
            Row.State = ProfileState.Problem;
            Checks = null;
        }
    }

    /// <summary>Shows a check run's result: the row's state (running while an engine this launcher started runs it) and
    /// the header.</summary>
    public void Show(ProfileCheckResult result)
    {
        _checkFailure = null;
        Row.State = Row.RunningHere ? ProfileState.Running : ProfileRowViewModel.StateOf(result);
        Checks = result;
        Play.Refresh();
    }

    /// <summary>The failures a check run reports rather than throws: a file that cannot be read.</summary>
    public static bool IsCheckFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidDataException or ProfileNeedsReviewException
            or ArgumentException;

    /// <summary>Draws the cover again, after art was downloaded.</summary>
    public void RefreshArt()
    {
        Cover = _main.ArtFor(Row.Game, Core.ArtKind.Cover, Entry);
        Changed(nameof(Cover));
    }
}

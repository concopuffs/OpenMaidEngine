using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
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
        Port = row.Entry.Usable && !row.Entry.NeedsReview
            ? new OmeIniStore(Path.Combine(row.Entry.Root, PortOptions.FileName)).Load()
            : null;
        Play = new Command(() => { }, () => false);
        Duplicate = new Command(() => { }, () => false);
        Shortcut = new Command(() => { }, () => false);
        More = new Command(() => { }, () => false);
        NoticeCommand = new Command(RunNoticeAction, () => NoticeTarget != null);
        ShowTab = new Command(tab => Tab = (ProfileTab)tab!);
    }

    public LauncherStrings S => _main.S;
    public ProfileRowViewModel Row { get; }
    public ProfileEntry Entry => Row.Entry;
    private ProfileDocument? Document => Entry.Loaded?.Document;
    public ArtViewModel? Cover { get; private set; }
    public PortOptions? Port { get; }

    public ProfileCheckResult? Checks
    {
        get => _checks;
        private set
        {
            _checks = value;
            ChangedAll();
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
    public Command More { get; }
    public bool IsRunning => State == ProfileState.Running;
    public bool IsNotRunning => !IsRunning;

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
            if (_checks is not { } checks) return null;
            if (checks.Running)
            {
                string text = RunLock.LastStart(Entry.Root) is { } start
                    ? S.Format("notice.running", start.ToLocalTime().ToString(S["time.short"], S.Culture))
                    : S["notice.running-unknown"];
                return (text, null, "run", null);
            }
            ProfileCheck? check = checks.Checks.FirstOrDefault(check => check.Status == CheckStatus.Failed)
                                  ?? checks.Checks.FirstOrDefault(check => check.Status == CheckStatus.Warning);
            if (check == null) return null;
            string cls = check.Status == CheckStatus.Failed ? "bad" : "warn";
            return check.Kind switch
            {
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
    public bool HasNoticeAction => NoticeTarget != null && NoticeTarget != Tab;
    public string NoticeAction => NoticeTarget == ProfileTab.Mods ? S["action.mods"] : S["action.overview"];
    public Command NoticeCommand { get; }

    private void RunNoticeAction()
    {
        if (NoticeTarget is { } target) Tab = target;
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
    public async Task CheckAsync()
    {
        if (!Entry.Usable || Entry.NeedsReview) return;
        ProfileEntry entry = Entry;
        CheckOptions options = _main.Session.CheckOptions;
        try
        {
            ProfileCheckResult result = await Task.Run(() => ProfileChecks.Run(entry, options));
            _checkFailure = null;
            Row.State = ProfileRowViewModel.StateOf(result);
            Checks = result;
        }
        catch (Exception error) when (IsCheckFailure(error))
        {
            _checkFailure = error.Message;
            Row.State = ProfileState.Problem;
            Checks = null;
        }
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

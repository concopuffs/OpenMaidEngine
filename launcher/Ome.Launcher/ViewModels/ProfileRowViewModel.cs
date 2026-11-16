using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

/// <summary>A launch that did not start, or whose engine exited with a code other than 0 (launcher spec §8.5), as the
/// failed-start view shows it: why it was refused, or the exit code with the end of the log.</summary>
public sealed record LaunchFailure(bool Started, int? ExitCode, string? Refusal, string? LogPath,
                                   IReadOnlyList<string> LogTail)
{
    /// <summary>The failure in <paramref name="result"/> that the failed-start view shows, or null: when the engine
    /// started and exited with 0, and when a check refused the start, since the page's notice shows that check where it
    /// is fixed (plan §6.3). A refusal the checks do not explain (a link found just before the start, an engine that
    /// cannot be started) is shown.</summary>
    public static LaunchFailure? Of(LaunchResult result)
        => !result.Failed || result is { Started: false, Checks.CanPlay: false } ? null
            : new(result.Started, result.ExitCode, result.Refusal, result.LogPath, result.LogTail);
}

/// <summary>A profile's state as the list's dot and the header's chip show it (launcher spec §9).</summary>
public enum ProfileState
{
    Checking,
    Ready,
    Warning,
    Problem,
    Running,
    Review,
    Unreadable,
}

/// <summary>One row of the profile list (the mock's <c>.prow</c>): the state dot, the name, the mode and the mod
/// count.</summary>
public sealed class ProfileRowViewModel : ViewModel
{
    private readonly Action<ProfileRowViewModel> _select;
    private ProfileState _state;
    private bool _isSelected;
    private bool _launching;
    private bool _runningHere;
    private LaunchFailure? _failure;
    private ProfileEntry _entry;

    public ProfileRowViewModel(ProfileEntry entry, LauncherStrings strings, Action<ProfileRowViewModel> select)
    {
        _entry = entry;
        S = strings;
        _select = select;
        _state = entry.NeedsReview ? ProfileState.Review
            : entry.Usable ? ProfileState.Checking
            : ProfileState.Unreadable;
        Select = new Command(() => _select(this));
    }

    /// <summary>The list was read again and still holds this root: the row takes the profile as read now and the
    /// launcher's strings, and keeps what the checks and a running game gave it. A profile that became usable (a review
    /// accepted) is checked again, and one that needs review or cannot be read says so.</summary>
    public void Renew(ProfileEntry entry, LauncherStrings strings)
    {
        _entry = entry;
        S = strings;
        if (entry.NeedsReview) _state = ProfileState.Review;
        else if (!entry.Usable) _state = ProfileState.Unreadable;
        else if (_state is ProfileState.Review or ProfileState.Unreadable) _state = ProfileState.Checking;
        ChangedAll();
    }

    /// <summary>The profile as last read or written: a page's change replaces it.</summary>
    public ProfileEntry Entry
    {
        get => _entry;
        set
        {
            _entry = value;
            ChangedAll();
        }
    }
    public LauncherStrings S { get; private set; }
    public string Id => Entry.Id;
    public string Name => Entry.DisplayName;
    public string? Game => Entry.Loaded?.Document.Game;
    public Command Select { get; }

    /// <summary>The mode and the mod count, or why the profile is not usable.</summary>
    public string Sub => _state switch
    {
        ProfileState.Review => S["row.review"],
        ProfileState.Unreadable => S["row.unreadable"],
        _ => S.Format("row.sub", ModeName(S, Entry.Loaded!.Document.Mode), ModCount(S, EnabledCount(Entry))),
    };

    public ProfileState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            Changed(nameof(Sub));
            Changed(nameof(IsWarn));
            Changed(nameof(IsBad));
            Changed(nameof(IsRun));
            Changed(nameof(IsChecking));
        }
    }

    public bool IsWarn => _state is ProfileState.Warning or ProfileState.Review;
    public bool IsBad => _state is ProfileState.Problem or ProfileState.Unreadable;
    public bool IsRun => _state == ProfileState.Running;
    public bool IsChecking => _state == ProfileState.Checking;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>Play was pressed and the engine has not started yet: the checks run again first.</summary>
    public bool Launching
    {
        get => _launching;
        set => Set(ref _launching, value);
    }

    /// <summary>An engine this launcher started runs the profile, since <see cref="StartedAt"/>.</summary>
    public bool RunningHere
    {
        get => _runningHere;
        set => Set(ref _runningHere, value);
    }

    /// <summary>When this launcher saw the engine it started begin to run (UTC).</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>The last launch that did not start or that failed (spec §8.5), until the player dismisses it or starts
    /// the profile again.</summary>
    public LaunchFailure? Failure
    {
        get => _failure;
        set => Set(ref _failure, value);
    }

    /// <summary>The state the checks give: running, a failed check, a warning, or ready.</summary>
    public static ProfileState StateOf(ProfileCheckResult checks)
    {
        if (checks.Running) return ProfileState.Running;
        if (checks.Checks.Any(check => check.Status == CheckStatus.Failed)) return ProfileState.Problem;
        if (checks.Checks.Any(check => check.Status == CheckStatus.Warning)) return ProfileState.Warning;
        return ProfileState.Ready;
    }

    public static string ModeName(LauncherStrings strings, ProfileMode mode)
        => strings[mode == ProfileMode.Native ? "mode.native" : "mode.extended"];

    public static string ModCount(LauncherStrings strings, int count) => count switch
    {
        0 => strings["mods.none"],
        1 => strings["mods.one"],
        _ => strings.Format("mods.many", count),
    };

    public static int EnabledCount(ProfileEntry entry) => entry.Loaded?.Document.Mods?.Enabled.Count() ?? 0;
}

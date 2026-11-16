using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

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

    public ProfileRowViewModel(ProfileEntry entry, LauncherStrings strings, Action<ProfileRowViewModel> select)
    {
        Entry = entry;
        S = strings;
        _select = select;
        _state = entry.NeedsReview ? ProfileState.Review
            : entry.Usable ? ProfileState.Checking
            : ProfileState.Unreadable;
        Select = new Command(() => _select(this));
    }

    public ProfileEntry Entry { get; }
    public LauncherStrings S { get; }
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

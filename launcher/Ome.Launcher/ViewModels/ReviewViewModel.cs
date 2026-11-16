using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

/// <summary>A path the review shows as text: what names it, the path, and whether it is on another computer and
/// approved.</summary>
public sealed record ReviewPathRow(string Label, string Path, string? Note, bool NeedsApproval);

/// <summary>
/// The review of a root the launcher did not create (launcher spec §12, plan §6.10), from
/// <see cref="ProfileTrust.Review"/>: its name, game, mode, the paths its file names (a network location marked, with
/// whether this machine approved it), its enabled mods, and a link, which stops it. The launcher opens none of the
/// paths before the player accepts. Accept approves the network locations it shows and records the root; Decline
/// forgets the root and leaves its folder; Later closes the dialog, and the page keeps its notice.
/// </summary>
public sealed class ReviewViewModel : ViewModel
{
    private readonly MainViewModel _main;
    private readonly ProfileEntry _profile;
    private RootReview? _review;
    private bool _busy;
    private string? _error;

    public ReviewViewModel(MainViewModel main, ProfileEntry profile)
    {
        _main = main;
        _profile = profile;
        Accept = new Command(() => _ = AcceptAsync(), () => !_busy && _review is { CanAccept: true });
        Decline = new Command(() => _ = DeclineAsync(), () => !_busy && _review != null);
        Later = new Command(() => _main.CloseDialog(this), () => !_busy);
    }

    public LauncherStrings S => _main.S;
    public string Title => S.Format("review.title", _profile.DisplayName);

    /// <summary>Reads the review: the file's text and the walk for links, off the UI thread.</summary>
    public async Task LoadAsync()
    {
        ProfileEntry profile = _profile;
        Func<MachineState> machine = _main.Session.Machine;
        _review = await Task.Run(() => ProfileTrust.Review(profile, machine));
        ChangedAll();
        Accept.Refresh();
        Decline.Refresh();
    }

    public bool Loaded => _review != null;

    /// <summary>A root on another computer that this machine has not approved is not read: accepting approves it, and
    /// the review then shows the profile.</summary>
    public bool Unread => _review is { Read: false, Problem: null };
    public bool Read => _review is { Read: true };
    public string Intro => _review switch
    {
        { Read: false, Problem: null } => S.Format("review.unread", _review.Root),
        _ => S["review.intro"],
    };

    public string NameText => _review?.Name ?? _profile.Id;
    public string GameText => _review?.GameTitle ?? _review?.Game ?? "";
    public string ModeText => _review?.Mode switch
    {
        "native" => S["mode.native"],
        "extended" => S["mode.extended"],
        { } other => other,
        null => "",
    };

    public IReadOnlyList<ReviewPathRow> Paths => (_review?.Paths ?? []).Select(path => new ReviewPathRow(
        S[$"review.key.{path.Key}"], path.Path,
        path.Network ? S[path.Approved ? "review.network-approved" : "review.network"] : null,
        path.Network && !path.Approved)).ToList();

    public string ModsText => _review switch
    {
        null => "",
        { OwnModsFolder: true } => S["review.own-mods"],
        { EnabledMods.Count: 0 } => S["review.no-mods"],
        { } review => string.Join(", ", review.EnabledMods),
    };

    /// <summary>A link in the root, or a root that cannot be checked: the profile cannot be accepted, and why.</summary>
    public string? Stop => _review?.Link ?? _review?.Problem;
    public bool HasStop => Stop != null;

    public string? Error
    {
        get => _error;
        private set
        {
            Set(ref _error, value);
            Changed(nameof(HasError));
        }
    }
    public bool HasError => _error != null;

    public Command Accept { get; }
    public Command Decline { get; }
    public Command Later { get; }

    private void Busy(bool busy)
    {
        _busy = busy;
        Accept.Refresh();
        Decline.Refresh();
        Later.Refresh();
    }

    /// <summary>Accepts the review and opens the profile's page; a root that was not read yet is reviewed again, now
    /// that its location is approved.</summary>
    public async Task AcceptAsync()
    {
        if (_review is not { CanAccept: true } review) return;
        Busy(true);
        try
        {
            LauncherSettings settings = ProfileTrust.Accept(_main.Session.Settings, review, _main.Session.Machine);
            _main.Session.Save(settings with { LastProfile = _profile.Id });
            _main.CloseDialog(this);
            await _main.Reload(select: _profile.Id);
            if (_main.Selected is { Entry.NeedsReview: true } again && again.Id == _profile.Id)
                await _main.OpenReviewAsync(again.Entry);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = error.Message;
        }
        finally
        {
            Busy(false);
        }
    }

    /// <summary>Forgets the root; its folder stays as it is.</summary>
    public Task DeclineAsync()
    {
        try
        {
            _main.Session.Save(ProfileTrust.Decline(_main.Session.Settings, _profile));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Error = error.Message;
            return Task.CompletedTask;
        }
        _main.CloseDialog(this);
        return _main.Dropped(_profile.Root);
    }
}

using Ome.Engine.Profiles;
using Ome.Launcher.Core;

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
/// hosts (spec §8.7).
/// </summary>
public sealed class MainViewModel : ViewModel, IDisposable
{
    private readonly ArtStore _art;
    private IReadOnlyList<GameGroupViewModel> _groups = [];
    private ProfileRowViewModel? _selected;
    private ProfilePageViewModel? _page;
    private IReadOnlyList<string> _artHosts = [];
    private bool _askArt;

    public MainViewModel(LauncherSession session, ArtOptions? artOptions = null)
    {
        Session = session;
        artOptions ??= new ArtOptions();
        _art = new ArtStore(session.Home, artOptions with { Decoder = artOptions.Decoder ?? ArtViewModel.Probe });
        NewProfile = new Command(() => { }, () => false);
        AddExisting = new Command(() => { }, () => false);
        Games = new Command(() => { }, () => false);
        LauncherSettings = new Command(() => { }, () => false);
        About = new Command(() => { }, () => false);
        ArtAlways = new Command(() => _ = AnswerArtAsync(ArtConsent.Always));
        ArtNever = new Command(() => _ = AnswerArtAsync(ArtConsent.Never));
        Reload();
    }

    public LauncherSession Session { get; }

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
        Page = row == null ? null : new ProfilePageViewModel(this, row);
        if (save && row != null && Session.Settings.LastProfile != row.Id)
            Session.Save(Session.Settings with { LastProfile = row.Id });
        Checking = _page?.CheckAsync() ?? Task.CompletedTask;
    }

    /// <summary>The window's first work: the selected profile's checks, then every other profile's, one at a time, so
    /// each dot shows its state; then the automatic downloads of <c>always</c>.</summary>
    public async Task StartAsync()
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

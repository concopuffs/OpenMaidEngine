using Ome.Engine.Profiles;
using Ome.Launcher.Core;

namespace Ome.Launcher.ViewModels;

/// <summary>One supported game on the Games page: its titles, its banner, the installs its profiles use, its art, and
/// the publisher's page.</summary>
public sealed class GameEntryViewModel : ViewModel
{
    private readonly MainViewModel _main;
    private ArtViewModel? _banner;
    private string? _status;
    private bool _statusBad;
    private bool _busy;

    public GameEntryViewModel(MainViewModel main, GameProfileManifest game, IReadOnlyList<string> installs)
    {
        _main = main;
        Id = game.Id;
        Title = game.DisplayTitle;
        Subtitle = string.Join(" · ", game.CatalogIdentities.Select(identity => identity.Title.Trim())
                                          .Where(title => title.Length > 0 && title != game.DisplayTitle).Distinct());
        Installs = installs;
        Site = main.Site(game.Id);
        Download = new Command(() => _ = DownloadAsync(), () => !_busy && Kinds.Any(Missing));
        Remove = new Command(RemoveArt, () => !_busy && Kinds.Any(Downloaded));
        OpenSite = new Command(() => { if (Site != null) _main.Host?.Open(Site.ToString()); }, () => Site != null);
        Refresh();
    }

    public LauncherStrings S => _main.S;
    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool HasSubtitle => Subtitle.Length > 0;
    public IReadOnlyList<string> Installs { get; }
    public bool HasInstalls => Installs.Count > 0;
    public Uri? Site { get; }
    public string SiteText => Site?.ToString() ?? "";
    public bool HasSite => Site != null;

    public ArtViewModel? Banner
    {
        get => _banner;
        private set => Set(ref _banner, value);
    }

    private static readonly ArtKind[] Kinds = [ArtKind.Banner, ArtKind.Cover];
    private bool Offered(ArtKind kind) => _main.Art.Source(Id, kind) != null;
    private bool Missing(ArtKind kind) => _main.Art.IsMissing(Id, kind);
    private bool Downloaded(ArtKind kind) => Offered(kind) && File.Exists(_main.Art.CachePath(Id, kind));

    /// <summary>Each image's state: downloaded, not downloaded, or shown from the player's own file.</summary>
    public string ArtText => string.Join(" · ", Kinds.Select(kind => S.Format(
        !Offered(kind) ? "games.art-none" : Downloaded(kind) ? "games.art-downloaded"
        : Missing(kind) ? "games.art-missing" : "games.art-own",
        S[kind == ArtKind.Banner ? "games.banner" : "games.cover"])));

    /// <summary>The host a download contacts, named before the player asks for one (spec §8.7).</summary>
    public string HostsText => string.Join(", ", Kinds.Select(kind => _main.Art.Source(Id, kind)?.Url.Host)
                                                     .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase));
    public string DownloadText => S.Format("games.download-text", HostsText);

    public Command Download { get; }
    public Command Remove { get; }
    public Command OpenSite { get; }

    public string? Status
    {
        get => _status;
        private set
        {
            Set(ref _status, value);
            Changed(nameof(HasStatus));
        }
    }
    public bool HasStatus => _status != null;
    public bool StatusBad
    {
        get => _statusBad;
        private set => Set(ref _statusBad, value);
    }

    private void Refresh()
    {
        Banner = _main.ArtFor(Id, ArtKind.Banner);
        Changed(nameof(ArtText));
        Download.Refresh();
        Remove.Refresh();
    }

    /// <summary>Downloads the missing images: the player asked, which is the consent (spec §8.7).</summary>
    public async Task DownloadAsync()
    {
        _busy = true;
        Download.Refresh();
        Remove.Refresh();
        var failed = new List<string>();
        try
        {
            foreach (ArtKind kind in Kinds.Where(Missing))
            {
                ArtDownload result = await _main.Art.DownloadAsync(Id, kind);
                if (result.Status == ArtDownloadStatus.Failed)
                    failed.Add($"{S[kind == ArtKind.Banner ? "games.banner" : "games.cover"]}: {result.Problem}");
            }
        }
        finally
        {
            _busy = false;
        }
        StatusBad = failed.Count > 0;
        Status = failed.Count > 0 ? S["games.download-failed"] + " " + string.Join("; ", failed) : S["games.downloaded"];
        _main.RefreshArt();
        Refresh();
    }

    private void RemoveArt()
    {
        try
        {
            _main.Art.RemoveDownloaded(Id);
            StatusBad = false;
            Status = S["games.removed"];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusBad = true;
            Status = error.Message;
        }
        _main.RefreshArt();
        Refresh();
    }
}

/// <summary>
/// The Games page (launcher spec §8.2, §8.7, plan §6.11): the supported games from the manifests, each with its art and
/// the installs known for it (the install roots of the profiles; there is no separate list), "download the art" and
/// "remove the downloaded art", which the player may do at any time, and the publisher's page.
/// </summary>
public sealed class GamesViewModel : ViewModel
{
    private readonly MainViewModel _main;

    public GamesViewModel(MainViewModel main)
    {
        _main = main;
        GameProfileRegistry registry = main.Session.CheckOptions.Registry ?? GameProfileRegistry.BuiltIn;
        List<ProfileEntry> profiles = main.Rows.Select(row => row.Entry).Where(entry => entry.Usable && !entry.NeedsReview)
            .ToList();
        IReadOnlyList<KnownInstall> known = Installs.Known(profiles);
        // The games a player can play: one below P3 is still being added (docs/adding-a-game.md, section 1).
        Games = registry.Profiles.Where(game => game.Support.IsPlayable).Select(game => new GameEntryViewModel(main, game, known
                .Where(install => install.Game == game.Id)
                .Select(install => S.Format("games.install", install.Root, string.Join(", ", install.Profiles.Select(
                    id => main.Rows.FirstOrDefault(row => row.Id == id)?.Name ?? id))))
                .ToList()))
            .ToList();
        Close = new Command(() => _main.CloseDialog(this));
    }

    public LauncherStrings S => _main.S;
    public IReadOnlyList<GameEntryViewModel> Games { get; }
    public Command Close { get; }
}

using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

/// <summary>One line of the Overview's checks (launcher spec §8.5): its mark, the check in the window's language, and
/// Core's message where the check did not pass.</summary>
public sealed record CheckLine(ProfileCheckKind Kind, CheckStatus Status, string Text, string? Detail)
{
    public string Mark => Status switch
    {
        CheckStatus.Passed => "✓",
        CheckStatus.Warning => "!",
        CheckStatus.Failed => Kind == ProfileCheckKind.Lock ? "●" : "✕",
        _ => "–",
    };
    public bool IsOk => Status == CheckStatus.Passed;
    public bool IsWarn => Status == CheckStatus.Warning;
    public bool IsBad => Status == CheckStatus.Failed && Kind != ProfileCheckKind.Lock;
    public bool IsRun => Status == CheckStatus.Failed && Kind == ProfileCheckKind.Lock;
    public bool IsNotRun => Status == CheckStatus.NotRun;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>A package of the Overview's list, in load order.</summary>
public sealed record ModLine(int Number, string Name, string Version);

/// <summary>
/// The Overview tab (launcher spec §8.4, plan §6.4): the game (titles, version, catalog, install, overlays, the
/// publisher's page), the profile (mode with its switch, language, display, saves, folder), the enabled mods in load
/// order, and the checks. Its actions fix what the checks found: locate the install, accept the install as it is now.
/// The mode switch writes the mode, or to Compatibility is <see cref="Library.ToCompatibility"/>, which turns off the
/// packages that mode refuses. Every write holds the profile's lock (<see cref="ProfileStore.Update"/>) and none is
/// offered while the game runs.
/// </summary>
public sealed class OverviewViewModel : ViewModel
{
    private readonly ProfilePageViewModel _page;
    private string? _result;
    private bool _resultBad;

    public OverviewViewModel(ProfilePageViewModel page)
    {
        _page = page;
        OpenInstall = new Command(() => _page.Main.Host?.Open(InstallPath), () => Directory.Exists(InstallPath));
        OpenFolder = new Command(() => _page.Main.Host?.Open(_page.Entry.Root));
        OpenPublisher = new Command(() => { if (Site is { } site) _page.Main.Host?.Open(site.AbsoluteUri); },
                                    () => Site != null);
        SwitchMode = new Command(() => _ = SwitchModeAsync(), () => CanWrite);
        Locate = new Command(() => _ = LocateAsync(), () => CanWrite);
        Accept = new Command(() => _ = AcceptAsync(), () => CanWrite);
        ManageMods = new Command(() => _page.Tab = ProfileTab.Mods);
    }

    public LauncherStrings S => _page.S;
    private ProfileCheckResult? Checks => _page.Checks;
    private ProfileDocument? Document => _page.Entry.Loaded?.Document;
    private GameProfileManifest? Game => Document == null ? null : GameProfileRegistry.BuiltIn.Find(Document.Game);
    private InstallRecognition? Install => Checks?.Install;
    private bool InstallFound => Checks?[ProfileCheckKind.Install].Status == CheckStatus.Passed;
    private Sys4StartupSettings? Startup => InstallFound ? Install?.Catalog?.StartupSettings : null;

    /// <summary>A change can be written: the profile is readable, under no review, and not running or starting.</summary>
    private bool CanWrite => Document != null && !_page.Entry.NeedsReview && _page.IsUnlocked;

    /// <summary>Everything is read again: the checks ran, or the profile changed.</summary>
    public void Refresh()
    {
        ChangedAll();
        OpenInstall.Refresh();
        OpenPublisher.Refresh();
        SwitchMode.Refresh();
        Locate.Refresh();
        Accept.Refresh();
    }

    // ---- the game ----

    public string OriginalTitle => Game?.CatalogIdentities.FirstOrDefault()?.Title ?? Game?.DisplayTitle ?? Document?.Game ?? "";
    public string OtherTitle => Game != null && Game.DisplayTitle != OriginalTitle ? Game.DisplayTitle : "";
    public bool HasOtherTitle => OtherTitle.Length > 0;

    /// <summary>The version 情報 shows (<c>GameVersionRule</c>), once the install is found.</summary>
    public string Version => InstallFound && Install?.GameVersion is { Length: > 0 } version ? version : S["overview.unknown"];

    /// <summary>The catalog's revision and its fingerprint, shortened (the whole one is <see cref="Fingerprint"/>).</summary>
    public string Catalog => InstallFound && Install?.Identity is { } identity
        ? S.Format("overview.catalog-line", identity.CatalogRevision, Short(Install.Fingerprint))
        : S["overview.unknown"];
    public string Fingerprint => Install?.Fingerprint ?? "";

    private static string Short(string? fingerprint)
    {
        if (fingerprint == null) return "";
        int colon = fingerprint.IndexOf(':');
        return fingerprint.Length - colon > 13 ? fingerprint[..(colon + 13)] + "…" : fingerprint;
    }

    public string InstallPath => Document?.Install.Root ?? "";
    public Command OpenInstall { get; }

    public string Overlays => Document is { Install.Overlays: { Count: > 0 } overlays }
        ? string.Join(Environment.NewLine, overlays)
        : S["overview.no-overlays"];

    private Uri? Site => Document == null ? null : _page.Main.Site(Document.Game);
    public string Publisher => Site is { } site ? site.Host + site.AbsolutePath.TrimEnd('/') + " ↗" : S["overview.unknown"];
    public Command OpenPublisher { get; }

    // ---- the profile ----

    public string ModeName => Document == null ? "" : ProfileRowViewModel.ModeName(S, Document.Mode);
    public string ModeDescription => Document?.Mode == ProfileMode.Extended ? S["mode.extended-text"] : S["mode.native-text"];
    private ProfileMode OtherMode => Document?.Mode == ProfileMode.Extended ? ProfileMode.Native : ProfileMode.Extended;
    public string SwitchText => S.Format("overview.switch-to", ProfileRowViewModel.ModeName(S, OtherMode));
    public bool ShowSwitch => _page.IsUnlocked;
    public bool ShowSwitchLocked => _page.IsLocked;
    public Command SwitchMode { get; }

    public string Language => _page.LanguageText;

    public string Display
    {
        get
        {
            ProfileSettings? settings = SettingsOrNull();
            if (settings == null) return S["overview.unknown"];
            if (!settings.Windowed) return S["display.fullscreen"];
            return settings.Port.CanvasWindowSize
                ? S["display.window-canvas"]
                : S.Format("display.window", settings.Port.WindowWidth, settings.Port.WindowHeight);
        }
    }

    private ProfileSettings? SettingsOrNull()
    {
        if (Document == null || _page.Entry.NeedsReview) return null;
        try
        {
            return ProfileSettings.Load(_page.Entry.Root, Startup ?? Sys4StartupSettings.Empty);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>How many saves the profile holds and when the last was written; the save folder is the game's
    /// <c>SAVEPATH</c>, so it is known once the install is found.</summary>
    public string Saves
    {
        get
        {
            if (Startup is not { } startup) return S["saves.unknown"];
            SaveCounts counts;
            try
            {
                counts = ProfileSaves.Count(ProfileSaves.Directory(startup, _page.Entry.Root));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return S["overview.unknown"];
            }
            if (counts.Saves == 0 || counts.LastWrittenUtc is not { } last) return S["saves.none"];
            string when = last.ToLocalTime().ToString(S["date.short"], S.Culture);
            return counts.Saves == 1 ? S.Format("saves.one", when) : S.Format("saves.many", counts.Saves, when);
        }
    }

    public string Folder => _page.Entry.Root;
    public Command OpenFolder { get; }

    // ---- the mods ----

    /// <summary>The enabled packages in the order they load: the link's resolved order once it ran, else the
    /// profile's.</summary>
    public IReadOnlyList<ModLine> Mods
    {
        get
        {
            IReadOnlyList<string> order = Checks?.Link?.Order
                                          ?? Document?.Mods?.Enabled.ToList()
                                          ?? [];
            var packages = Checks?.Library?.Packages.ToDictionary(package => package.Id) ?? [];
            return order.Select((id, index) => packages.TryGetValue(id, out LibraryPackage? package)
                                                   && package.Entry is { } entry
                                                   ? new ModLine(index + 1, entry.Manifest.Name, entry.Version.ToString())
                                                   : new ModLine(index + 1, id, "")).ToList();
        }
    }
    public bool HasMods => Mods.Count > 0;
    public bool HasNoMods => !HasMods;
    public Command ManageMods { get; }

    // ---- the checks ----

    public IReadOnlyList<CheckLine> CheckLines => Checks?.Checks.Select(Line).ToList() ?? [];
    public bool HasChecks => Checks != null;

    private CheckLine Line(ProfileCheck check)
    {
        string game = Game?.DisplayTitle ?? Document?.Game ?? "";
        string key = $"check.{check.Kind.ToString().ToLowerInvariant()}.{check.Status.ToString().ToLowerInvariant()}";
        string text = check is { Kind: ProfileCheckKind.Link, Status: CheckStatus.Warning } && Checks?.Link is { } link
            ? S.Format(key, string.Join(", ", link.Dropped))
            : S.Has(key) ? S.Format(key, game) : check.Message;
        // A check that did not run because the install is missing says so in the window's words; Core's message is
        // the detail of the others that did not pass.
        bool noInstall = check.Status == CheckStatus.NotRun && !InstallFound;
        if (noInstall && S.Has(key + "-install")) text = S[key + "-install"];
        string? detail = check.Status is CheckStatus.Passed || noInstall ? null : check.Message;
        return new CheckLine(check.Kind, check.Status, text, detail);
    }

    // ---- what fixes a check ----

    /// <summary>"Locate the install…": the install check failed.</summary>
    public bool ShowLocate => Checks?[ProfileCheckKind.Install].Status == CheckStatus.Failed && !_page.IsLocked;
    public Command Locate { get; }

    /// <summary>"Accept the install as it is now": the fingerprint check warns.</summary>
    public bool ShowAccept => Checks?[ProfileCheckKind.Fingerprint].Status == CheckStatus.Warning && !_page.IsLocked;
    public Command Accept { get; }

    /// <summary>What the last action did, or why it could not.</summary>
    public string? Result
    {
        get => _result;
        private set
        {
            Set(ref _result, value);
            Changed(nameof(HasResult));
        }
    }
    public bool HasResult => !string.IsNullOrEmpty(_result);
    public bool ResultBad
    {
        get => _resultBad;
        private set => Set(ref _resultBad, value);
    }

    private void Say(string text, bool bad = false)
    {
        ResultBad = bad;
        Result = text;
    }

    /// <summary>Points the profile at another folder of its game (spec §8.5). A folder that does not hold the game is
    /// refused, with why.</summary>
    public async Task LocateAsync()
    {
        if (!CanWrite || Document is not { } document || _page.Main.Host is not { } host) return;
        string game = Game?.DisplayTitle ?? document.Game;
        if (await host.PickFolderAsync(S.Format("overview.locate-title", game)) is not { } picked) return;
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picked));
        CheckOptions options = _page.Main.Session.CheckOptions;
        InstallRecognition found = await Task.Run(() => Installs.Recognize(
            folder, options.Registry ?? GameProfileRegistry.BuiltIn, options.MachineRegistry));
        if (found is not { Kind: InstallKind.Supported, Problem: null } || found.Game!.Id != document.Game)
        {
            Say(S.Format("overview.locate-wrong", folder, game)
                + (found.Problem is { } problem ? " " + problem : ""), bad: true);
            return;
        }
        if (await _page.WriteAsync(entry => ProfileStore.Update(entry, current => current with
            {
                Install = current.Install with { Root = folder },
            }, _page.Main.Session.Home)))
            Say(S.Format("overview.located", folder));
    }

    /// <summary>Records the install's fingerprint as the one the profile accepts
    /// (<see cref="ProfileChecks.AcceptInstall"/>).</summary>
    public async Task AcceptAsync()
    {
        if (!CanWrite || Install?.Fingerprint is not { } fingerprint) return;
        if (await _page.WriteAsync(entry => ProfileChecks.AcceptInstall(entry, fingerprint, _page.Main.Session.Home)))
            Say(S["overview.accepted"]);
    }

    /// <summary>Switches the mode (spec §6) after the player confirms: up to Extended it writes the mode; down to
    /// Compatibility it warns about Lua mod state and names the packages that mode turns off.</summary>
    public async Task SwitchModeAsync()
    {
        if (!CanWrite || Document is not { } document) return;
        OmeHome home = _page.Main.Session.Home;
        if (document.Mode == ProfileMode.Native)
        {
            if (!await _page.Main.ConfirmAsync(new ConfirmViewModel(
                    S["confirm.extended.title"], S["confirm.extended.text"], S["confirm.extended.yes"], S["confirm.cancel"])))
                return;
            if (await _page.WriteAsync(entry => ProfileStore.Update(entry, current => current with
                {
                    Mode = ProfileMode.Extended,
                }, home)))
                Say(S.Format("overview.switched", ProfileRowViewModel.ModeName(S, ProfileMode.Extended)));
            return;
        }

        LibraryChange preview;
        try
        {
            preview = await Task.Run(() => Library.ToCompatibility(document, Library.Index(document)));
        }
        catch (Exception error) when (ProfilePageViewModel.IsCheckFailure(error))
        {
            Say(S["overview.write-failed"] + " " + error.Message, bad: true);
            return;
        }
        if (!await _page.Main.ConfirmAsync(new ConfirmViewModel(
                S["confirm.native.title"], S["confirm.native.text"], S["confirm.native.yes"], S["confirm.cancel"],
                preview.Changed, S["confirm.native.off"], danger: preview.Changed.Count > 0)))
            return;
        IReadOnlyList<string> turnedOff = [];
        if (await _page.WriteAsync(entry => ProfileStore.Update(entry, current =>
            {
                // Under the lock, from the document as it is now.
                LibraryChange change = Library.ToCompatibility(current, Library.Index(current));
                turnedOff = change.Changed;
                return change.Document;
            }, home)))
            Say(turnedOff.Count == 0
                ? S.Format("overview.switched", ProfileRowViewModel.ModeName(S, ProfileMode.Native))
                : S.Format("overview.switched-off", string.Join(", ", turnedOff)));
    }

    /// <summary>A failed write, as the page reports it.</summary>
    internal void WriteFailed(string message) => Say(S["overview.write-failed"] + " " + message, bad: true);
}

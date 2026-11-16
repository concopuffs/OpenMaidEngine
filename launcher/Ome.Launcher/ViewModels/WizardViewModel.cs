using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

/// <summary>What a new profile starts from (launcher spec §8.3).</summary>
public enum WizardStart { Nothing, Copy, BareEngine, OriginalSaves }

/// <summary>An install a profile already uses, offered in the wizard's first step.</summary>
public sealed record KnownInstallChoice(string Root, string Label, string Profiles, Command Pick);

/// <summary>A subfolder of the chosen install offered as an overlay; on by default, as the mock shows.</summary>
public sealed class WizardOverlayViewModel : ViewModel
{
    private bool _on = true;

    public WizardOverlayViewModel(OverlayCandidate candidate, string name, string label, string note, Action changed)
    {
        Candidate = candidate;
        Name = name;
        Label = label;
        Note = note;
        Toggle = new Command(() =>
        {
            On = !On;
            changed();
        });
    }

    public OverlayCandidate Candidate { get; }
    public string Name { get; }
    public string Label { get; }
    public string Note { get; }
    public Command Toggle { get; }

    public bool On
    {
        get => _on;
        private set => Set(ref _on, value);
    }
}

/// <summary>A library folder of the new profile, with its Remove.</summary>
public sealed record WizardFolder(string Path, Command Remove);

/// <summary>
/// The new-profile wizard (launcher spec §8.3, plan §6.9). Step 1 chooses the install: browse, a folder dropped on the
/// window, or an install a profile already uses; it shows what <see cref="Installs.Recognize"/> found, with the
/// overlay candidates, and a folder that is not a supported game says why and cannot continue. Step 2 names the profile,
/// chooses its mode and what it starts from (nothing, a copy of another profile of the game with or without its saves,
/// the bare engine's data, or the original game's saves), and under Advanced its location and library folders. Create
/// makes the profile with Core (<see cref="ProfileStore"/>, <see cref="Adoption"/>, <see cref="ProfileSaves"/>) and opens
/// its page.
/// </summary>
public sealed class WizardViewModel : ViewModel
{
    private readonly MainViewModel _main;
    private int _step = 1;
    private bool _busy;
    private string? _error;

    // Step 1.
    private InstallRecognition? _found;
    private IReadOnlyList<WizardOverlayViewModel> _overlays = [];
    private bool _allowBmp;
    private ArtViewModel? _banner;

    // Step 2.
    private string _name = "";
    private string? _defaultName;
    private bool _extended;
    private WizardStart _start = WizardStart.Nothing;
    private IReadOnlyList<SaveProfileChoice> _copySources = [];
    private SaveProfileChoice? _copySource;
    private bool _copySaves = true;
    private BareEngineData? _bare;
    private AdoptionPlan? _plan;
    private string _bareCounts = "";
    private int _nativeSaves;
    private bool _advancedOpen;
    private string? _location;
    private IReadOnlyList<WizardFolder> _folders = [];

    public WizardViewModel(MainViewModel main)
    {
        _main = main;
        Known = Installs.Known(main.Rows.Select(row => row.Entry).Where(entry => entry.Usable && !entry.NeedsReview))
            .Select(known => new KnownInstallChoice(
                known.Root, GameTitle(known.Game),
                string.Join(", ", known.Profiles.Select(id => main.Rows.FirstOrDefault(row => row.Id == id)?.Name ?? id)),
                                                    new Command(() => _ = ChooseAsync(known.Root), () => !Busy)))
            .ToList();
        Browse = new Command(() => _ = BrowseAsync(), () => !Busy);
        Next = new Command(GoToProfile, () => CanContinue);
        Back = new Command(() => Step = 1, () => !Busy);
        Cancel = new Command(() => _main.CloseWizard(this), () => !Busy);
        ToggleBmp = new Command(() => AllowBmp = !AllowBmp);
        ChooseCompatibility = new Command(() => Extended = false, () => !StartForcesExtended);
        ChooseExtended = new Command(() => Extended = true);
        StartNothing = new Command(() => Start = WizardStart.Nothing);
        StartCopy = new Command(() => Start = WizardStart.Copy, () => HasCopySources);
        StartBare = new Command(() => Start = WizardStart.BareEngine, () => HasBareData);
        StartNative = new Command(() => Start = WizardStart.OriginalSaves, () => HasNativeSaves);
        ToggleCopySaves = new Command(() => CopySaves = !CopySaves);
        ToggleAdvanced = new Command(() => AdvancedOpen = !AdvancedOpen);
        ChooseLocation = new Command(() => _ = ChooseLocationAsync(), () => !Busy);
        DefaultLocation = new Command(() => Location = null, () => _location != null);
        AddFolder = new Command(() => _ = AddFolderAsync(), () => !Busy);
        Create = new Command(() => _ = CreateAsync(), () => CanCreate);
    }

    public LauncherStrings S => _main.S;
    private OmeHome Home => _main.Session.Home;
    private GameProfileRegistry Registry => _main.Session.CheckOptions.Registry ?? GameProfileRegistry.BuiltIn;

    private string GameTitle(string game) => Registry.Find(game)?.DisplayTitle ?? game;

    public int Step
    {
        get => _step;
        private set
        {
            Set(ref _step, value);
            Changed(nameof(OnGame));
            Changed(nameof(OnProfile));
        }
    }
    public bool OnGame => _step == 1;
    public bool OnProfile => _step == 2;

    public bool Busy
    {
        get => _busy;
        private set
        {
            Set(ref _busy, value);
            RefreshCommands();
        }
    }

    /// <summary>Why the last Create failed, in Core's words.</summary>
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

    private void RefreshCommands()
    {
        foreach (Command command in new[]
                 {
                     Browse, Next, Back, Cancel, ChooseCompatibility, StartCopy, StartBare, StartNative, ChooseLocation,
                     DefaultLocation, AddFolder, Create,
                 })
            command.Refresh();
        foreach (KnownInstallChoice known in Known) known.Pick.Refresh();
    }

    // ---- step 1: the game ----

    public IReadOnlyList<KnownInstallChoice> Known { get; }
    public bool HasKnown => Known.Count > 0;
    public Command Browse { get; }

    private async Task BrowseAsync()
    {
        if (_main.Host is not { } host) return;
        if (await host.PickFolderAsync(S["wizard.browse-title"]) is { } folder) await ChooseAsync(folder);
    }

    /// <summary>Recognizes <paramref name="folder"/> (browsed, dropped or known) and lists its overlay
    /// candidates.</summary>
    public async Task ChooseAsync(string folder)
    {
        if (Busy) return;
        Busy = true;
        try
        {
            string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            MachineRegistryReader? machine = _main.Session.CheckOptions.MachineRegistry;
            var (found, candidates) = await Task.Run(() =>
            {
                InstallRecognition recognition = Installs.Recognize(path, Registry, machine);
                IReadOnlyList<OverlayCandidate> overlays = recognition is
                    { Kind: InstallKind.Supported, Problem: null, Catalog: { } catalog }
                    ? Installs.OverlayCandidates(recognition.Root, catalog)
                    : [];
                return (recognition, overlays);
            });
            _found = found;
            Overlays = candidates.Select(candidate => new WizardOverlayViewModel(
                    candidate, Path.GetFileName(candidate.Path),
                    S.Format("wizard.overlay", Path.GetFileName(candidate.Path)),
                    S.Format(candidate.Files == 1 ? "advanced.overlay-file" : "advanced.overlay-files", candidate.Files)
                    + (candidate.BmpAsAgf ? " · " + S["advanced.overlay-bmp"] : ""), OverlaysChanged))
                .ToList();
            _allowBmp = candidates.Any(candidate => candidate.BmpAsAgf);
            Banner = found.Game is { } game ? _main.ArtFor(game.Id, ArtKind.Banner) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _found = new InstallRecognition(folder, InstallKind.NotAnInstall, error.Message);
            Overlays = [];
            Banner = null;
        }
        finally
        {
            Busy = false;
        }
        ChangedAll();
        RefreshCommands();
    }

    public bool HasFound => _found != null;
    public bool CanContinue => !Busy && _found is { Kind: InstallKind.Supported, Problem: null, Game: not null };
    public bool Refused => _found != null && !CanContinue && !Busy;

    public string FoundTitle => _found?.Game?.DisplayTitle ?? _found?.Identity?.Title ?? "";
    public string FoundSub => _found switch
    {
        { Kind: InstallKind.Supported, Problem: null } found => string.Join(" · ", new[]
        {
            found.Titles.Count > 1 ? found.Titles[1] : null,
            found.GameVersion is { Length: > 0 } version ? S.Format("wizard.version", version) : null,
            S["wizard.supported"],
        }.Where(part => part != null)),
        _ => "",
    };
    public string FoundPath => _found == null ? ""
        : _found.Identity is { } identity
            ? S.Format("wizard.path-catalog", _found.Root, identity.CatalogRevision.Trim())
            : _found.Root;

    /// <summary>Why the folder cannot continue: a game this engine does not support, by its revision and title, or not
    /// an install at all, in Core's words.</summary>
    public string RefusalText => _found switch
    {
        null => "",
        { Kind: InstallKind.Supported, Problem: null } => "",
        { Kind: InstallKind.Unsupported, Identity: { } identity } =>
            S.Format("wizard.unsupported", identity.CatalogRevision.Trim(), identity.Title),
        { Problem: { } problem } => S["wizard.not-install"] + " " + problem,
        _ => S["wizard.not-install"],
    };

    public ArtViewModel? Banner
    {
        get => _banner;
        private set
        {
            Set(ref _banner, value);
            Changed(nameof(HasBanner));
        }
    }
    public bool HasBanner => _banner != null;

    public IReadOnlyList<WizardOverlayViewModel> Overlays
    {
        get => _overlays;
        private set
        {
            Set(ref _overlays, value);
            Changed(nameof(HasOverlays));
        }
    }
    public bool HasOverlays => _overlays.Count > 0;

    /// <summary>A folder with BMP files under AGF names turns the switch on with it (spec §8.2).</summary>
    private void OverlaysChanged()
    {
        if (_overlays.Any(row => row.On && row.Candidate.BmpAsAgf)) AllowBmp = true;
        Changed(nameof(HasBmpCandidates));
    }

    public bool AllowBmp
    {
        get => _allowBmp;
        private set => Set(ref _allowBmp, value);
    }
    public Command ToggleBmp { get; }
    public bool HasBmpCandidates => _overlays.Any(row => row.Candidate.BmpAsAgf);

    public Command Next { get; }
    public Command Back { get; }
    public Command Cancel { get; }

    private InstallRecognition Found => _found!;
    private GameProfileManifest Game => _found!.Game!;
    private Sys4StartupSettings Startup => _found!.Catalog!.StartupSettings;

    private void GoToProfile()
    {
        if (!CanContinue) return;
        // The name follows the game's title until the player writes one of their own.
        if (_name.Trim().Length == 0 || _name == _defaultName) Name = Game.DisplayTitle;
        _defaultName = Game.DisplayTitle;
        CopySources = _main.Rows.Where(row => row.Entry.Usable && !row.Entry.NeedsReview
                                              && row.Entry.Loaded?.Document.Game == Game.Id)
            .Select(row => new SaveProfileChoice(row.Name, row.Entry)).ToList();
        _copySource = _copySources.FirstOrDefault();
        _bare = BareEngineData.For(Game, _main.Session.MachineFolder);
        _bareCounts = CountBare(_bare);
        _nativeSaves = NativeSaves();
        _folders = [];
        StartFolders();
        if (Start == WizardStart.Copy && !HasCopySources || Start == WizardStart.BareEngine && !HasBareData
            || Start == WizardStart.OriginalSaves && !HasNativeSaves)
            Start = WizardStart.Nothing;
        Error = null;
        Step = 2;
        ChangedAll();
        RefreshCommands();
    }

    // ---- step 2: the profile ----

    public string Name
    {
        get => _name;
        set
        {
            Set(ref _name, value ?? "");
            Changed(nameof(Id));
            Changed(nameof(LocationText));
            Create.Refresh();
        }
    }

    /// <summary>The id the profile gets: made from its name, free in OME home.</summary>
    public string Id => _found?.Game is { } game
        ? ProfileStore.SuggestId(_name.Trim().Length == 0 ? null : _name, game.Id, Home, _main.Session.Settings)
        : "";

    public bool Extended
    {
        get => _extended || StartForcesExtended;
        private set
        {
            Set(ref _extended, value);
            Changed(nameof(Compatibility));
        }
    }
    public bool Compatibility => !Extended;
    public Command ChooseCompatibility { get; }
    public Command ChooseExtended { get; }

    public WizardStart Start
    {
        get => _start;
        private set
        {
            Set(ref _start, value);
            foreach (string name in new[]
                     {
                         nameof(FromNothing), nameof(FromCopy), nameof(FromBare), nameof(FromNative), nameof(Extended),
                         nameof(Compatibility), nameof(StartForcesExtended), nameof(ExtendedNote), nameof(HasPlanNotes),
                         nameof(PlanNotes),
                     })
                Changed(name);
            if (value == WizardStart.Copy && _copySource?.Entry.Loaded?.Document.Mode == ProfileMode.Extended)
                Extended = true;
            StartFolders();
            if (value == WizardStart.BareEngine) _ = PlanAsync();
            ChooseCompatibility.Refresh();
        }
    }
    public bool FromNothing => _start == WizardStart.Nothing;
    public bool FromCopy => _start == WizardStart.Copy;
    public bool FromBare => _start == WizardStart.BareEngine;
    public bool FromNative => _start == WizardStart.OriginalSaves;
    public Command StartNothing { get; }
    public Command StartCopy { get; }
    public Command StartBare { get; }
    public Command StartNative { get; }

    public IReadOnlyList<SaveProfileChoice> CopySources
    {
        get => _copySources;
        private set
        {
            Set(ref _copySources, value);
            Changed(nameof(HasCopySources));
        }
    }
    public bool HasCopySources => _copySources.Count > 0;
    public string CopyNote => HasCopySources ? "" : S["wizard.start-copy-none"];
    public SaveProfileChoice? CopySource
    {
        get => _copySource;
        set
        {
            if (value == null) return;
            Set(ref _copySource, value);
            if (FromCopy && value.Entry.Loaded?.Document.Mode == ProfileMode.Extended) Extended = true;
            StartFolders();
        }
    }
    public bool CopySaves
    {
        get => _copySaves;
        private set => Set(ref _copySaves, value);
    }
    public Command ToggleCopySaves { get; }

    /// <summary>The bare engine's data for this game, offered when its folder is there.</summary>
    public bool HasBareData => _bare?.Exists == true;
    public string BareCounts => _bareCounts;
    public string BareNote => HasBareData ? _bareCounts : S["wizard.start-bare-none"];

    private string CountBare(BareEngineData data)
    {
        if (!data.Exists) return "";
        int saves = 0, mods = 0;
        try
        {
            saves = ProfileSaves.Count(data.Saves(Startup).SaveDirectory).Saves;
            mods = ModLaunch.Candidates(data.ModsDirectory).Count();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }
        return S.Format("wizard.bare-counts", saves, mods);
    }

    /// <summary>The adoption's plan once the bare engine's data is chosen: it says when the stack needs
    /// Extended.</summary>
    private async Task PlanAsync()
    {
        if (_bare is not { } data || !_bare.Exists || _found == null) return;
        ProfileDocument document = Document();
        AdoptionPlan plan = await Task.Run(() => Adoption.Plan(data, document));
        _plan = plan;
        foreach (string name in new[]
                 {
                     nameof(StartForcesExtended), nameof(ExtendedNote), nameof(Extended), nameof(Compatibility),
                     nameof(HasPlanNotes), nameof(PlanNotes),
                 })
            Changed(name);
        ChooseCompatibility.Refresh();
    }

    /// <summary>The stack the bare engine ran needs Extended, so the profile starts in it, and the step says
    /// why.</summary>
    public bool StartForcesExtended => FromBare && _plan is { NeedsExtended.Count: > 0 };
    public string ExtendedNote => StartForcesExtended
        ? S.Format("wizard.bare-extended", string.Join("; ", _plan!.NeedsExtended.Select(pair => $"{pair.Key}: {pair.Value}")))
        : "";
    public bool HasPlanNotes => FromBare && _plan is { Notes.Count: > 0 };
    public IReadOnlyList<string> PlanNotes => FromBare ? _plan?.Notes ?? [] : [];

    private int NativeSaves()
    {
        try
        {
            string folder = SaveSource.Native(Startup, Found.Root).SaveDirectory;
            return Directory.Exists(folder) ? ProfileSaves.Count(folder).Saves : 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return 0;
        }
    }
    public bool HasNativeSaves => _nativeSaves > 0;
    public string NativeCounts => S.Format(_nativeSaves == 1 ? "wizard.native-one" : "wizard.native-many", _nativeSaves);
    public string NativeNote => HasNativeSaves ? NativeCounts : S["wizard.start-native-none"];

    // ---- Advanced: the location and the library folders ----

    public bool AdvancedOpen
    {
        get => _advancedOpen;
        private set
        {
            Set(ref _advancedOpen, value);
            Changed(nameof(AdvancedLabel));
        }
    }
    public string AdvancedLabel => S["wizard.advanced"] + (_advancedOpen ? " ▾" : " ▸");
    public Command ToggleAdvanced { get; }

    /// <summary>A folder elsewhere for the profile, or null for <c>profiles/&lt;id&gt;</c> in OME home.</summary>
    public string? Location
    {
        get => _location;
        private set
        {
            Set(ref _location, value);
            Changed(nameof(LocationText));
            DefaultLocation.Refresh();
        }
    }
    public string LocationText => _location ?? Path.Combine(Home.ProfilesDirectory, Id);
    public Command ChooseLocation { get; }
    public Command DefaultLocation { get; }

    private async Task ChooseLocationAsync()
    {
        if (_main.Host is { } host && await host.PickFolderAsync(S["wizard.location-title"]) is { } folder)
            Location = folder;
    }

    /// <summary>The library folders a start begins with: a copy's are its source's, whose enabled mods come from them;
    /// any other start's are OME home's folders for the game (spec §8.3).</summary>
    private void StartFolders()
    {
        if (_found?.Game is not { } game) return;
        IReadOnlyList<string> folders = FromCopy && _copySource?.Entry.Loaded?.Document.Mods is { } mods
            ? mods.Folders
            : ProfileDocument.LibraryFolders(Home, game.Id);
        Folders = folders.Select(FolderRow).ToList();
    }

    public IReadOnlyList<WizardFolder> Folders
    {
        get => _folders;
        private set => Set(ref _folders, value);
    }
    public Command AddFolder { get; }

    private WizardFolder FolderRow(string path)
        => new(path, new Command(() => Folders = _folders.Where(folder => folder.Path != path).ToList()));

    private async Task AddFolderAsync()
    {
        if (_main.Host is not { } host || await host.PickFolderAsync(S["wizard.folder-title"]) is not { } folder) return;
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (_folders.Any(each => OmeHome.PathsEqual(each.Path, path, _main.Session.Environment.Platform))) return;
        Folders = [.. _folders, FolderRow(path)];
    }

    // ---- Create ----

    public bool CanCreate => OnProfile && !Busy && _found?.Game != null && _name.Trim().Length > 0
                             && (!FromCopy || _copySource != null);
    public Command Create { get; }

    /// <summary>The wizard's profile document: the install with its chosen overlays, the name, the mode and the
    /// library folders.</summary>
    private ProfileDocument Document()
    {
        ProfileDocument document = ProfileDocument.New(
            _name.Trim(), Game.Id, Found.Root, Found.Fingerprint, Home,
            _overlays.Where(row => row.On).Select(row => row.Candidate.Path).ToList(), _allowBmp);
        return document with
        {
            Mode = Extended ? ProfileMode.Extended : ProfileMode.Native,
            Mods = document.Mods! with { Folders = _folders.Select(folder => folder.Path).ToList() },
        };
    }

    /// <summary>Makes the profile, then opens its page. A refusal (a taken or non-empty location, the source running, a
    /// clash of selectors) is shown in the wizard, and nothing is left behind.</summary>
    public async Task CreateAsync()
    {
        if (!CanCreate) return;
        Busy = true;
        Error = null;
        try
        {
            ProfileDocument document = Document();
            string id = Id, name = _name.Trim();
            string? elsewhere = _location;
            LauncherSettings settings = _main.Session.Settings;
            WizardStart start = _start;
            SaveProfileChoice? copy = _copySource;
            bool copySaves = _copySaves;
            BareEngineData? bare = _bare;
            Sys4StartupSettings startup = Startup;
            string install = Found.Root;
            ProfileChange change = await Task.Run(() =>
            {
                switch (start)
                {
                    case WizardStart.Copy:
                    {
                        ProfileEntry source = copy!.Entry;
                        ProfileChange copied = ProfileStore.Duplicate(Home, settings, source, id, name,
                            copySaves ? null : ProfileStore.SaveDirectory(startup, source.Root), elsewhere);
                        // The copy keeps its mods and options; the install and the mode are the wizard's.
                        ProfileStore.Update(copied.Profile, current => current with
                        {
                            Mode = document.Mode,
                            Install = document.Install,
                            Mods = current.Mods! with { Folders = document.Mods!.Folders },
                        }, Home);
                        return copied;
                    }
                    case WizardStart.BareEngine:
                        return Adoption.Adopt(Home, settings, id, bare!, Adoption.Plan(bare!, document), startup, elsewhere);
                    case WizardStart.OriginalSaves:
                        return ProfileStore.Create(Home, settings, id, document, elsewhere, root =>
                            ProfileSaves.CopyInLocked(root, startup, SaveSource.Native(startup, install),
                                                      withSettings: true, replace: false));
                    default:
                        return ProfileStore.Create(Home, settings, id, document, elsewhere);
                }
            });
            if (!ReferenceEquals(change.Settings, settings)) _main.Session.Save(change.Settings);
            _main.CloseWizard(this);
            _main.Reload(select: change.Profile.Id);
            _main.Session.Save(_main.Session.Settings with { LastProfile = change.Profile.Id });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                          or ProfileBusyException or ProfileFileException or SaveCopyRefusedException
                                          or InvalidDataException)
        {
            Error = S["wizard.create-failed"] + " " + error.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}

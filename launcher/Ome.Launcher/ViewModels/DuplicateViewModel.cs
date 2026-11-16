using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher.ViewModels;

/// <summary>
/// Duplicate (launcher spec §8.4, plan §6.10): a copy of the profile under a new name and id, with or without its saves
/// (the saves and <c>mods/selectors.toml</c> go together, in Core), in <c>profiles/&lt;id&gt;</c> or a new or empty
/// folder the player picks. Leaving the saves out needs the save folder, which the game's startup settings give: the
/// dialog reads them from the page as its checks find the install (a page just opened is still checking), and with the
/// install not found the saves always come along. Core refuses while the profile runs.
/// </summary>
public sealed class DuplicateViewModel : ViewModel
{
    private readonly MainViewModel _main;
    private readonly ProfilePageViewModel _page;
    private readonly ProfileEntry _source;
    private string _name;
    private bool _withSaves = true;
    private string? _location;
    private bool _busy;
    private string? _error;

    public DuplicateViewModel(MainViewModel main, ProfilePageViewModel page)
    {
        _main = main;
        _page = page;
        _source = page.Entry;
        _name = main.S.Format("duplicate.name", _source.DisplayName);
        ToggleSaves = new Command(() => WithSaves = !WithSaves, () => CanLeaveSaves);
        ChooseLocation = new Command(() => _ = ChooseLocationAsync(), () => !Busy);
        DefaultLocation = new Command(() => Location = null, () => _location != null);
        Create = new Command(() => _ = CreateAsync(), () => CanCreate);
        Cancel = new Command(Close, () => !Busy);
        _page.PropertyChanged += PageChanged;
    }

    /// <summary>The page's checks found (or lost) the install: whether the saves can stay behind follows.</summary>
    private void PageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Changed(nameof(CanLeaveSaves));
        Changed(nameof(CannotLeaveSaves));
        Changed(nameof(WithSaves));
        ToggleSaves.Refresh();
    }

    private void Close()
    {
        _page.PropertyChanged -= PageChanged;
        _main.CloseDialog(this);
    }

    private Sys4StartupSettings? Startup => _page.Catalog?.StartupSettings;

    public LauncherStrings S => _main.S;
    private OmeHome Home => _main.Session.Home;
    public string Title => S.Format("duplicate.title", _source.DisplayName);

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

    /// <summary>The copy's id, made from its name and free in OME home.</summary>
    public string Id => ProfileStore.SuggestId(_name.Trim().Length == 0 ? null : _name,
                                               _source.Loaded?.Document.Game ?? "profile", Home, _main.Session.Settings);

    public bool WithSaves
    {
        get => _withSaves || !CanLeaveSaves;
        private set => Set(ref _withSaves, value);
    }
    public bool CanLeaveSaves => Startup != null;
    public bool CannotLeaveSaves => !CanLeaveSaves;
    public Command ToggleSaves { get; }

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

    public bool Busy
    {
        get => _busy;
        private set
        {
            Set(ref _busy, value);
            Create.Refresh();
            Cancel.Refresh();
            ChooseLocation.Refresh();
        }
    }

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

    public bool CanCreate => !Busy && _name.Trim().Length > 0;
    public Command Create { get; }
    public Command Cancel { get; }

    /// <summary>Copies the profile, then opens the copy's page. A refusal stays in the dialog.</summary>
    public async Task CreateAsync()
    {
        if (!CanCreate) return;
        Busy = true;
        Error = null;
        try
        {
            string id = Id, name = _name.Trim();
            string? elsewhere = _location;
            string? leaveOut = WithSaves ? null : ProfileStore.SaveDirectory(Startup!, _source.Root);
            LauncherSettings settings = _main.Session.Settings;
            ProfileEntry source = _source;
            // Core takes the source's lock, so the copy waits its turn with the window's other uses of it.
            ProfileChange change = await _main.OneAtATime(() => Task.Run(
                () => ProfileStore.Duplicate(Home, settings, source, id, name, leaveOut, elsewhere)));
            Close();
            _main.Session.Save(change.Settings with { LastProfile = change.Profile.Id });
            _main.Reload(select: change.Profile.Id);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                          or ProfileBusyException or ProfileFileException)
        {
            Error = S["duplicate.failed"] + " " + error.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}

/// <summary>Rename (spec §8.4, under More): the profile's name, never its id (spec §4.1). An empty name shows the
/// profile by its id. Refused while the game runs, as every write of <c>profile.toml</c> is.</summary>
public sealed class RenameViewModel : ViewModel
{
    private readonly MainViewModel _main;
    private readonly ProfilePageViewModel _page;
    private string _name;
    private string? _error;
    private bool _busy;

    public RenameViewModel(MainViewModel main, ProfilePageViewModel page)
    {
        _main = main;
        _page = page;
        _name = page.Entry.Loaded?.Document.Name ?? "";
        Save = new Command(() => _ = SaveAsync(), () => !_busy);
        Cancel = new Command(() => _main.CloseDialog(this), () => !_busy);
    }

    public LauncherStrings S => _main.S;
    public string Title => S.Format("rename.title", _page.Entry.DisplayName);
    public string IdText => S.Format("rename.id", _page.Entry.Id);

    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? "");
    }

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

    public Command Save { get; }
    public Command Cancel { get; }

    public async Task SaveAsync()
    {
        _busy = true;
        Save.Refresh();
        Cancel.Refresh();
        Error = null;
        string name = _name;
        OmeHome home = _main.Session.Home;
        bool saved = await _page.WriteAsync(entry =>
        {
            ProfileStore.Rename(entry, name, home);
            return ProfileDocuments.Read(entry.Root);
        }, failed: message => Error = S["rename.failed"] + " " + message);
        _busy = false;
        Save.Refresh();
        Cancel.Refresh();
        if (!saved) return;
        _main.CloseDialog(this);
        // The list is ordered by name within each game.
        _main.Reload(select: _page.Entry.Id);
    }
}

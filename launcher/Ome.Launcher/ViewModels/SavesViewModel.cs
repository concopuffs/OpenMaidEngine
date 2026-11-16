using Ome.Engine.Sys4;
using Ome.Launcher.Core;

namespace Ome.Launcher.ViewModels;

/// <summary>Another profile of the same game, as a source of saves.</summary>
public sealed record SaveProfileChoice(string Label, ProfileEntry Entry);

/// <summary>
/// The Saves tab (launcher spec §8.4, plan §6.7): the profile's save folder, how many saves it holds and when the last
/// was written, and copying saves in from another profile of the game or from where the original game keeps its saves
/// (<see cref="ProfileSaves.CopyIn"/>, which holds both profiles' locks). The saves a copy would replace are confirmed
/// first; a clash of selectors is refused with the ids named. The profile's own <c>SYS4REG.INI</c> stays: only a new
/// profile takes the original game's settings with its saves (spec §8.3). The launcher copies nothing out.
/// </summary>
public sealed class SavesViewModel : ViewModel
{
    private readonly ProfilePageViewModel _page;
    private IReadOnlyList<SaveProfileChoice> _sources = [];
    private SaveProfileChoice? _source;
    private string? _result;
    private bool _resultBad;
    private IReadOnlyList<string> _details = [];
    private bool _nativeHasSaves;

    public SavesViewModel(ProfilePageViewModel page)
    {
        _page = page;
        OpenFolder = new Command(() =>
        {
            if (Folder is not { } folder) return;
            Directory.CreateDirectory(folder);
            _page.Main.Host?.Open(folder);
        }, () => Folder != null);
        OpenNative = new Command(() =>
        {
            if (NativeFolder is { } folder && Directory.Exists(folder)) _page.Main.Host?.Open(folder);
        }, () => NativeFolder != null && Directory.Exists(NativeFolder));
        CopyFromProfile = new Command(() =>
        {
            if (_source is { } source) _ = CopyAsync(SaveSource.FromProfile(source.Entry.Root, Startup!), source.Label);
        }, () => CanWrite && _source != null);
        CopyFromNative = new Command(() =>
        {
            if (NativeSource is { } native) _ = CopyAsync(native, S["saves.original"]);
        }, () => CanWrite && _nativeHasSaves);
    }

    public LauncherStrings S => _page.S;
    private Sys4StartupSettings? Startup
        => _page.Catalog?.StartupSettings;

    /// <summary>The save folder follows the game's <c>SAVEPATH</c>, so it is known once the install is found.</summary>
    public bool Known => Startup != null && _page.Entry.Loaded?.Document != null && !_page.Entry.NeedsReview;
    public bool Unknown => !Known;
    public bool CanWrite => Known && _page.IsUnlocked;
    public bool IsLocked => _page.IsLocked;

    public void Refresh()
    {
        string? game = _page.Entry.Loaded?.Document.Game;
        Sources = Known
            ? _page.Main.Rows.Where(row => row.Entry != _page.Entry && row.Entry.Usable && !row.Entry.NeedsReview
                                           && row.Entry.Loaded?.Document.Game == game)
                .Select(row => new SaveProfileChoice(row.Name, row.Entry)).ToList()
            : [];
        _source = _sources.FirstOrDefault(choice => choice.Entry.Root == _source?.Entry.Root) ?? _sources.FirstOrDefault();
        _nativeHasSaves = NativeHasSaves();
        ChangedAll();
        foreach (Command command in new[] { OpenFolder, OpenNative, CopyFromProfile, CopyFromNative }) command.Refresh();
    }

    // ---- the profile's saves ----

    public string? Folder => Known ? ProfileSaves.Directory(Startup!, _page.Entry.Root) : null;
    public string FolderText => Folder ?? S["saves.unknown"];

    /// <summary>The count and the last write, as the Overview says them.</summary>
    public string Contents => _page.Overview.Saves;
    public Command OpenFolder { get; }

    // ---- bringing saves in ----

    public IReadOnlyList<SaveProfileChoice> Sources
    {
        get => _sources;
        private set => Set(ref _sources, value);
    }
    public bool HasSources => _sources.Count > 0;
    public bool HasNoSources => Known && !HasSources;

    public SaveProfileChoice? Source
    {
        get => _source;
        set
        {
            if (value == null) return;
            Set(ref _source, value);
            CopyFromProfile.Refresh();
        }
    }
    public Command CopyFromProfile { get; }

    /// <summary>Where native AGE keeps this game's saves (<see cref="SaveSource.Native"/>): often outside the install
    /// (Himegari's are under <c>%LOCALAPPDATA%</c>).</summary>
    private SaveSource? NativeSource
        => Known && _page.Entry.Loaded?.Document.Install.Root is { } install ? SaveSource.Native(Startup!, install) : null;
    public string? NativeFolder => NativeSource?.SaveDirectory;
    public string NativeFolderText => NativeFolder ?? S["saves.unknown"];

    /// <summary>Whether the original game's folder holds anything to copy: a numbered save, or only the game's
    /// shared <c>SAVE.DAT</c> (what has been seen, the unlocked pictures), which is worth bringing too.</summary>
    private bool NativeHasSaves()
    {
        try
        {
            return NativeFolder is { } folder && Directory.Exists(folder)
                   && Directory.EnumerateFiles(folder).Any();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
    public bool HasNoNativeSaves => Known && !_nativeHasSaves;
    public Command CopyFromNative { get; }
    public Command OpenNative { get; }

    // ---- the outcome ----

    public string? Result
    {
        get => _result;
        private set
        {
            Set(ref _result, value);
            Changed(nameof(HasResult));
        }
    }
    public bool HasResult => _result != null;
    public bool ResultBad
    {
        get => _resultBad;
        private set => Set(ref _resultBad, value);
    }

    /// <summary>The lines a refusal gives: each selector that clashes, with both ids.</summary>
    public IReadOnlyList<string> Details
    {
        get => _details;
        private set
        {
            Set(ref _details, value);
            Changed(nameof(HasDetails));
        }
    }
    public bool HasDetails => _details.Count > 0;

    private void Say(string text, bool bad, IReadOnlyList<string>? details = null)
    {
        ResultBad = bad;
        Details = details ?? [];
        Result = text;
    }

    /// <summary>Copies <paramref name="source"/>'s saves in, after confirming the saves it would replace. Refused
    /// copies (a selector clash, a source profile the game runs) write nothing and say why.</summary>
    public async Task CopyAsync(SaveSource source, string from)
    {
        if (!CanWrite || Startup is not { } startup) return;
        string target = ProfileSaves.Directory(startup, _page.Entry.Root);
        IReadOnlyList<string> replaced;
        try
        {
            replaced = await Task.Run(() => ProfileSaves.WouldReplace(source, target));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Say(S["saves.copy-failed"] + " " + error.Message, bad: true);
            return;
        }
        if (replaced.Count > 0 && !await _page.Main.ConfirmAsync(new ConfirmViewModel(
                S.Format("confirm.replace-saves.title", replaced.Count), S.Format("confirm.replace-saves.text", from),
                S["confirm.replace-saves.yes"], S["confirm.cancel"], replaced, S["confirm.replace-saves.items"],
                danger: true)))
            return;

        SaveCopyResult? copied = null;
        SaveCopyRefusedException? refused = null;
        bool written = await _page.WriteAsync(entry =>
        {
            try
            {
                copied = ProfileSaves.CopyIn(entry.Root, startup, source, withSettings: false, replace: replaced.Count > 0);
            }
            catch (SaveCopyRefusedException error)
            {
                refused = error;
            }
            return null;
        }, message => Say(S["saves.copy-failed"] + " " + message, bad: true));
        if (!written) return;
        if (refused != null)
            Say(S["saves.copy-failed"] + " " + refused.Message, bad: true, refused.Details);
        else if (copied!.Copied.Count == 0)
            Say(S.Format("saves.copied-none", from), bad: false);
        else
            Say(S.Format("saves.copied", from, string.Join(", ", copied.Copied))
                + (copied.Replaced.Count > 0 ? " " + S.Format("saves.replaced", string.Join(", ", copied.Replaced)) : ""),
                bad: false);
        Refresh();
    }
}

using Ome.Launcher.Core;

namespace Ome.Launcher.ViewModels;

/// <summary>
/// The Shortcut dialog (launcher spec §8.4, plan §6.10). Its two forms come from <see cref="EngineCommands.Shortcut"/>,
/// the builder Play and <c>--print-command</c> use, so the dialog cannot disagree with them: through the launcher (the
/// default), or the engine directly. For the chosen form it shows the target, the arguments, the working folder and the
/// whole command line, each with Copy, and "Create a shortcut" writes a <c>.lnk</c> or <c>.desktop</c> file
/// (<see cref="ShortcutFile"/>) on the desktop or in a folder the player picks; a file already there is replaced only
/// when the player says so.
/// </summary>
public sealed class ShortcutViewModel : ViewModel
{
    private readonly MainViewModel _main;
    private readonly ShortcutForms _forms;
    private readonly string _name;
    private bool _direct;
    private bool _busy;
    private string? _status;
    private bool _statusBad;

    public ShortcutViewModel(MainViewModel main, ProfileEntry profile)
    {
        _main = main;
        _name = profile.DisplayName;
        Platform = main.Session.Environment.Platform;
        _forms = EngineCommands.Shortcut(main.Session.LauncherProgram, main.Session.Home, profile, main.Session.Engine,
                                       Platform);
        ChooseLauncher = new Command(() => Direct = false);
        ChooseDirect = new Command(() => Direct = true);
        CopyTarget = new Command(() => _ = CopyAsync(Target));
        CopyArguments = new Command(() => _ = CopyAsync(Arguments));
        CopyWorking = new Command(() => _ = CopyAsync(WorkingFolder));
        CopyLine = new Command(() => _ = CopyAsync(CommandLine));
        CreateOnDesktop = new Command(() => _ = CreateAsync(_main.Session.DesktopFolder), () => CanCreate);
        CreateIn = new Command(() => _ = CreateInAsync(), () => CanCreate);
        Close = new Command(() => _main.CloseDialog(this), () => !_busy);
    }

    public LauncherStrings S => _main.S;
    public OmePlatform Platform { get; }
    public string Title => S.Format("shortcut.title", _name);

    /// <summary>The engine directly, or (false, the default) through the launcher.</summary>
    public bool Direct
    {
        get => _direct;
        private set
        {
            Set(ref _direct, value);
            Status = null;
            ChangedAll();
        }
    }
    public bool ThroughLauncher => !_direct;
    public Command ChooseLauncher { get; }
    public Command ChooseDirect { get; }

    public EngineCommand Command => _direct ? _forms.Direct : _forms.ThroughLauncher;
    public string FormText => S[_direct ? "shortcut.direct-text" : "shortcut.launcher-text"];

    public string Target => Command.Program;
    public string Arguments => ShortcutFile.Arguments(Command, Platform);
    public string WorkingFolder => Command.WorkingDirectory;
    public string CommandLine => ShortcutFile.CommandLine(Command, Platform);

    public Command CopyTarget { get; }
    public Command CopyArguments { get; }
    public Command CopyWorking { get; }
    public Command CopyLine { get; }

    private async Task CopyAsync(string text)
    {
        if (_main.Host is not { } host) return;
        await host.CopyTextAsync(text);
        StatusBad = false;
        Status = S["shortcut.copied"];
    }

    /// <summary>Windows and Linux have a shortcut file; elsewhere the fields are what there is.</summary>
    public bool CanCreate => ShortcutFile.Supported(Platform) && !_busy;
    public bool CannotCreate => !ShortcutFile.Supported(Platform);
    public Command CreateOnDesktop { get; }
    public Command CreateIn { get; }
    public Command Close { get; }

    /// <summary>What the last copy or shortcut did, or why it failed.</summary>
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

    private async Task CreateInAsync()
    {
        if (_main.Host is { } host && await host.PickFolderAsync(S["shortcut.folder-title"]) is { } folder)
            await CreateAsync(folder);
    }

    /// <summary>Writes the chosen form's shortcut in <paramref name="folder"/>, named after the profile.</summary>
    public async Task CreateAsync(string folder)
    {
        if (!CanCreate) return;
        string path = ShortcutFile.PathFor(folder, _name, Platform);
        if (File.Exists(path) && !await _main.ConfirmAsync(new ConfirmViewModel(
                S["confirm.replace-shortcut.title"], S.Format("confirm.replace-shortcut.text", path),
                S["confirm.replace-shortcut.yes"], S["confirm.cancel"], danger: true)))
            return;
        _busy = true;
        Refresh();
        try
        {
            EngineCommand command = Command;
            OmePlatform platform = Platform;
            string name = _name;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(folder);
                ShortcutFile.Write(path, name, command, platform);
            });
            StatusBad = false;
            Status = S.Format("shortcut.created", path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                          or System.Runtime.InteropServices.COMException)
        {
            StatusBad = true;
            Status = S["shortcut.failed"] + " " + error.Message;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        CreateOnDesktop.Refresh();
        CreateIn.Refresh();
        Close.Refresh();
        Changed(nameof(CanCreate));
    }
}

using System.Globalization;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;

namespace Ome.Launcher.ViewModels;

/// <summary>A language the Settings page offers: its tag and its name.</summary>
public sealed record LanguageChoice(string Tag, string Label);

/// <summary>A window size the Settings page offers; the game's canvas is stored as 0 by 0 (the port's "the game's
/// size").</summary>
public sealed record SizeChoice(int Width, int Height, bool Canvas, string Label);

/// <summary>
/// The Settings tab (launcher spec §8.4, plan §6.6): the profile's <c>OME.INI</c> options and its start mode in
/// <c>SYS4REG.INI</c>, edited directly (<see cref="ProfileSettings"/>). Each change is written at once, under the
/// profile's lock, as the engine writes the files.
/// <list type="bullet">
/// <item>The language: the shell's languages, then those the enabled packages define (<see cref="Library.Languages"/>),
/// as the port's options panel lists them (<see cref="ShellPanel.LanguageChoices"/>).</item>
/// <item>The start: fullscreen or a window. When <c>display:ForceScreen</c> is 1 the choice stays free, and a note says
/// the game keeps its window once in one (the user's answer to plan §8 question 4). It is written only with the
/// install's <c>SYS4INI</c> settings, so it waits for the install.</item>
/// <item>The window size: the port's list over the install's canvas (<see cref="ProfileSettings.WindowSizes"/>).</item>
/// <item>The FPS counter and the page locator.</item>
/// </list>
/// </summary>
public sealed class SettingsViewModel : ViewModel
{
    private readonly ProfilePageViewModel _page;
    private ProfileSettings? _settings;
    private IReadOnlyList<LanguageChoice> _languages = [];
    private IReadOnlyList<SizeChoice> _sizes = [];
    private bool _refreshing;
    private string? _result;

    public SettingsViewModel(ProfilePageViewModel page)
    {
        _page = page;
        StartFullscreen = new Command(() => _ = SaveAsync(windowed: false), () => CanWriteDisplay);
        StartWindowed = new Command(() => _ = SaveAsync(windowed: true), () => CanWriteDisplay);
        ToggleFps = new Command(() => _ = SaveAsync(port => port with { FpsCounter = !port.FpsCounter }), () => CanWrite);
        ToggleLocator = new Command(() => _ = SaveAsync(port => port with { LocatorHud = !port.LocatorHud }),
                                    () => CanWrite);
    }

    public LauncherStrings S => _page.S;
    private ProfileCheckResult? Checks => _page.Checks;
    private Sys4StartupSettings? Startup
        => _page.Catalog?.StartupSettings;

    /// <summary>The profile's files can be read: it is not under review and its document reads.</summary>
    public bool HasSettings => _settings != null;
    public bool HasNoSettings => !HasSettings;

    /// <summary>A change can be written: the files read, and the game does not run the profile (spec §8.4).</summary>
    public bool CanWrite => HasSettings && _page.IsUnlocked;
    public bool IsLocked => _page.IsLocked;

    /// <summary>The start mode is written with the install's settings, so it also waits for the install.</summary>
    public bool CanWriteDisplay => CanWrite && Startup != null;
    public bool DisplayWaitsForInstall => HasSettings && Startup == null;

    /// <summary>Reads the files again and rebuilds the lists; called when the page's checks ran or the lock
    /// changed.</summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            _settings = Load();
            Languages = BuildLanguages();
            Sizes = BuildSizes();
            ChangedAll();
            foreach (Command command in new[] { StartFullscreen, StartWindowed, ToggleFps, ToggleLocator }) command.Refresh();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private ProfileSettings? Load()
    {
        if (_page.Entry.Loaded?.Document == null || _page.Entry.NeedsReview) return null;
        try
        {
            return ProfileSettings.Load(_page.Entry.Root, Startup ?? Sys4StartupSettings.Empty);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---- the language ----

    public IReadOnlyList<LanguageChoice> Languages
    {
        get => _languages;
        private set => Set(ref _languages, value);
    }

    /// <summary>The port's list (the shell's languages, the packages', then the saved tag when neither has it), each
    /// shell language named by the launcher's own table.</summary>
    private IReadOnlyList<LanguageChoice> BuildLanguages()
    {
        if (_settings == null) return [];
        IReadOnlyList<(string Tag, string? Name)> stack = Checks?.Library is { } view ? Library.Languages(view) : [];
        return ShellPanel.LanguageChoices(ShellStrings.Load(S.Language), stack, _settings.Port.Language)
            .Select(choice => new LanguageChoice(choice.Tag, S.Has($"language.{choice.Tag}") ? S[$"language.{choice.Tag}"]
                                                                                              : choice.Label))
            .ToList();
    }

    public LanguageChoice? SelectedLanguage
    {
        get => _languages.FirstOrDefault(choice => choice.Tag == _settings?.Port.Language);
        set
        {
            if (_refreshing || value == null || _settings == null || value.Tag == _settings.Port.Language) return;
            _ = SaveAsync(port => port with { Language = value.Tag });
        }
    }

    // ---- the display ----

    public bool Windowed => _settings?.Windowed == true;
    public bool Fullscreen => _settings?.Windowed == false;
    public bool ForceScreen => _settings?.ForceScreen == true;
    public Command StartFullscreen { get; }
    public Command StartWindowed { get; }

    public IReadOnlyList<SizeChoice> Sizes
    {
        get => _sizes;
        private set => Set(ref _sizes, value);
    }

    /// <summary>The port's sizes over the install's canvas, labelled with their scale; without the install, only the
    /// saved size.</summary>
    private IReadOnlyList<SizeChoice> BuildSizes()
    {
        if (_settings == null) return [];
        PortOptions port = _settings.Port;
        if (Startup == null || _page.Catalog?.LogicalCanvas is not { } canvas)
            return [port.CanvasWindowSize
                        ? new SizeChoice(0, 0, true, S["settings.size-game"])
                        : new SizeChoice(port.WindowWidth, port.WindowHeight, false,
                                         S.Format("settings.size", port.WindowWidth, port.WindowHeight))];
        return ProfileSettings.WindowSizes(canvas, port, _page.Main.Host?.UsableArea())
            .Select(size =>
            {
                bool isCanvas = size == (canvas.Width, canvas.Height);
                string scale = (size.Width * 100.0 / canvas.Width).ToString("0.#", CultureInfo.InvariantCulture);
                bool scaled = size.Width * canvas.Height == size.Height * canvas.Width;
                string label = isCanvas ? S.Format("settings.size-canvas", size.Width, size.Height)
                    : scaled ? S.Format("settings.size-scaled", size.Width, size.Height, scale)
                    : S.Format("settings.size", size.Width, size.Height);
                return new SizeChoice(size.Width, size.Height, isCanvas, label);
            })
            .ToList();
    }

    public SizeChoice? SelectedSize
    {
        get
        {
            if (_settings == null) return null;
            PortOptions port = _settings.Port;
            return port.CanvasWindowSize
                ? _sizes.FirstOrDefault(size => size.Canvas)
                : _sizes.FirstOrDefault(size => !size.Canvas && (size.Width, size.Height) == (port.WindowWidth, port.WindowHeight))
                  ?? _sizes.FirstOrDefault(size => (size.Width, size.Height) == (port.WindowWidth, port.WindowHeight));
        }
        set
        {
            if (_refreshing || value == null || _settings == null || value == SelectedSize) return;
            _ = SaveAsync(port => value.Canvas ? port with { WindowWidth = 0, WindowHeight = 0 }
                                               : port with { WindowWidth = value.Width, WindowHeight = value.Height });
        }
    }

    // ---- the port's switches ----

    public bool FpsCounter => _settings?.Port.FpsCounter == true;
    public bool LocatorHud => _settings?.Port.LocatorHud == true;
    public Command ToggleFps { get; }
    public Command ToggleLocator { get; }

    // ---- writing ----

    /// <summary>Why the last change was not written; null after one that was.</summary>
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

    /// <summary>Changes the options by <paramref name="port"/> and writes the start mode (each when given) under the
    /// profile's lock, then the page reads the files again. The change applies to the file as it is when its turn
    /// comes, so changes made in a row all land. A refused write says why, and the page shows the files as they
    /// are.</summary>
    public async Task SaveAsync(Func<PortOptions, PortOptions>? port = null, bool? windowed = null)
    {
        if (!CanWrite || (windowed != null && !CanWriteDisplay)) return;
        Sys4StartupSettings? startup = Startup;
        bool written = await _page.WriteAsync(entry =>
        {
            ProfileSettings.Save(entry.Root, port, windowed, startup);
            return null;
        }, message => Result = S["overview.write-failed"] + " " + message);
        if (written) Result = null;
        Refresh();
    }
}

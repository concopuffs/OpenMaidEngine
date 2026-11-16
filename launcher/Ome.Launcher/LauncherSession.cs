using System.Globalization;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

namespace Ome.Launcher;

/// <summary>
/// What one run of the window holds (launcher plan §6.1): OME home and its <c>launcher.toml</c>, the engine its Play
/// starts, where the machine's folders are, the strings, and why the window opened, with the profile and launch that
/// made it open.
/// </summary>
public sealed class LauncherSession
{
    private LauncherSettingsFile _settings;

    public LauncherSession(WindowStart start, WindowReason reason, string launcherProgram, LauncherEnvironment environment,
                           CheckOptions? checkOptions = null, CultureInfo? osCulture = null,
                           string? desktopFolder = null)
    {
        Home = start.Home;
        Engine = start.Engine;
        Reason = reason;
        Profile = start.Profile;
        Launch = start.Launch;
        LauncherProgram = launcherProgram;
        Environment = environment;
        CheckOptions = checkOptions ?? new CheckOptions();
        MachineFolder = CheckOptions.MachineFolder ?? MachineState.DefaultFolder();
        DesktopFolder = desktopFolder
                        ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
        _settings = LauncherSettingsStore.Load(Home);
        OsCulture = osCulture ?? CultureInfo.CurrentUICulture;
        Strings = LauncherStrings.Load(LauncherStrings.Choose(Settings.Language, OsCulture));
    }

    public OmeHome Home { get; }
    public EngineProgram Engine { get; }
    public WindowReason Reason { get; }
    public ProfileEntry? Profile { get; }
    public LaunchResult? Launch { get; }
    public string LauncherProgram { get; }
    public LauncherEnvironment Environment { get; }
    public CheckOptions CheckOptions { get; }
    public string MachineFolder { get; }

    /// <summary>Where "Create a shortcut" puts one by default: the player's desktop (spec §8.4).</summary>
    public string DesktopFolder { get; }
    public CultureInfo OsCulture { get; }
    public LauncherStrings Strings { get; private set; }

    public LauncherSettings Settings => _settings.Settings;

    /// <summary>What was wrong with <c>launcher.toml</c> when it was read.</summary>
    public IReadOnlyList<string> SettingsWarnings => _settings.Warnings;

    public MachineState Machine() => MachineState.Load(MachineFolder);

    /// <summary>Writes <paramref name="settings"/> to <c>launcher.toml</c> and holds them; the strings follow the
    /// language. A file from another launcher version is not written over, and the settings hold for this run only.</summary>
    public void Save(LauncherSettings settings)
    {
        if (_settings.Writable) LauncherSettingsStore.Save(Home, _settings, settings);
        _settings = _settings with { Settings = settings, Unreadable = false };
        Strings = LauncherStrings.Load(LauncherStrings.Choose(settings.Language, OsCulture));
    }
}

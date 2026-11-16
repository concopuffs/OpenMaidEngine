namespace Ome.Launcher.Core;

/// <summary>Which rule chose an OME home (launcher spec §4.1).</summary>
public enum OmeHomeKind
{
    /// <summary>The operating system's per-user data folder.</summary>
    Default,
    /// <summary>A <c>data/</c> folder beside the launcher.</summary>
    Portable,
    /// <summary><c>--home DIR</c>.</summary>
    Explicit,
}

/// <summary>What the launcher reads of its process's environment, so the location rules can be tested for every
/// operating system on any one.</summary>
/// <param name="Platform">The operating system the rules are for.</param>
/// <param name="Variable">An environment variable's value, or null.</param>
/// <param name="ApplicationData">The per-user roaming application data folder (<c>%APPDATA%</c> on Windows).</param>
/// <param name="UserProfile">The user's home folder.</param>
public sealed record LauncherEnvironment(OmePlatform Platform, Func<string, string?> Variable, string ApplicationData,
                                         string UserProfile)
{
    /// <summary>This process's environment. <c>%APPDATA%</c> is the variable when it is set, as Godot reads it
    /// (<c>MachineState.DefaultFolder</c>), so one harness moves the engine's folders and the launcher's together.</summary>
    public static LauncherEnvironment Current { get; } = new(
        OperatingSystem.IsWindows() ? OmePlatform.Windows : OperatingSystem.IsMacOS() ? OmePlatform.MacOS : OmePlatform.Linux,
        Environment.GetEnvironmentVariable,
        Environment.GetEnvironmentVariable("APPDATA") is { Length: > 0 } appData && Path.IsPathRooted(appData)
            ? appData
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}

/// <summary>The operating systems whose rules differ for the launcher.</summary>
public enum OmePlatform
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>
/// OME home (launcher spec §4.1): the folder that holds the launcher's settings, the profiles in <c>profiles/</c>,
/// the shared mod library in <c>mods/</c>, the player's art in <c>art/</c> and the downloaded art in <c>cache/</c>.
/// <c>--home DIR</c> names it; otherwise a <c>data/</c> folder beside the launcher is it (portable); otherwise it is
/// <c>OpenMaidEngine</c> in the operating system's per-user data folder.
/// </summary>
public sealed record OmeHome(string Root, OmeHomeKind Kind)
{
    public const string FolderName = "OpenMaidEngine";
    public const string PortableFolder = "data";
    public const string SettingsFile = "launcher.toml";
    public const string ProfilesFolder = "profiles", ModsFolder = "mods", CommonModsFolder = "common",
                        ArtFolder = "art", CacheFolder = "cache";

    /// <summary><c>launcher.toml</c>, the launcher's own settings (spec §8.8).</summary>
    public string SettingsPath => Path.Combine(Root, SettingsFile);

    /// <summary>The folder that holds the profile roots the launcher made.</summary>
    public string ProfilesDirectory => Path.Combine(Root, ProfilesFolder);

    /// <summary>The library folder of one game's packages (spec §7).</summary>
    public string ModsDirectory(string game) => Path.Combine(Root, ModsFolder, game);

    /// <summary>The library folder of the packages with no <c>[game]</c> table.</summary>
    public string CommonModsDirectory => Path.Combine(Root, ModsFolder, CommonModsFolder);

    /// <summary>The player's own art for a game (spec §8.7).</summary>
    public string ArtDirectory(string game) => Path.Combine(Root, ArtFolder, game);

    /// <summary>The art the launcher downloaded for a game; safe to delete.</summary>
    public string ArtCacheDirectory(string game) => Path.Combine(Root, CacheFolder, ArtFolder, game);

    /// <summary>
    /// The OME home of one launcher run. <paramref name="explicitHome"/> is <c>--home</c>'s value, resolved against
    /// <paramref name="workingDirectory"/>; <paramref name="launcherDirectory"/> is the folder the launcher's program is
    /// in, where a portable <c>data/</c> folder would be.
    /// </summary>
    public static OmeHome Resolve(string? explicitHome, string launcherDirectory, string workingDirectory,
                                  LauncherEnvironment environment)
    {
        if (explicitHome != null)
        {
            if (explicitHome.Length == 0) throw new ArgumentException("--home needs a folder");
            return new OmeHome(Normalize(Path.GetFullPath(explicitHome, workingDirectory)), OmeHomeKind.Explicit);
        }
        return Implied(launcherDirectory, environment);
    }

    /// <summary>The home a launcher finds with no <c>--home</c>: portable when <c>data/</c> is beside it, else the
    /// default.</summary>
    public static OmeHome Implied(string launcherDirectory, LauncherEnvironment environment)
    {
        string portable = Path.Combine(Path.GetFullPath(launcherDirectory), PortableFolder);
        return Directory.Exists(portable)
            ? new OmeHome(Normalize(portable), OmeHomeKind.Portable)
            : new OmeHome(DefaultRoot(environment), OmeHomeKind.Default);
    }

    /// <summary>The default home: <c>%APPDATA%\OpenMaidEngine</c> on Windows, <c>$XDG_DATA_HOME/OpenMaidEngine</c> on
    /// Linux (<c>~/.local/share</c> when the variable is unset, empty or relative, as the XDG rules say), and
    /// <c>~/Library/Application Support/OpenMaidEngine</c> on macOS.</summary>
    public static string DefaultRoot(LauncherEnvironment environment)
    {
        string data = environment.Platform switch
        {
            OmePlatform.Windows => environment.ApplicationData,
            OmePlatform.MacOS => Path.Combine(environment.UserProfile, "Library", "Application Support"),
            _ => environment.Variable("XDG_DATA_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(environment.UserProfile, ".local", "share"),
        };
        if (string.IsNullOrEmpty(data)) throw new InvalidOperationException("the user's data folder is unknown");
        // Not GetFullPath: the folder is absolute already, and a test of another system's rule runs on this one.
        return Path.Combine(data, FolderName);
    }

    /// <summary>Whether a command that starts this launcher must name the home: when it is not the one the launcher
    /// finds by itself (the Shortcut dialog's first form, spec §8.4).</summary>
    public bool NeedsHomeArgument(string launcherDirectory, LauncherEnvironment environment)
        => !PathsEqual(Root, Implied(launcherDirectory, environment).Root, environment.Platform);

    /// <summary>Two paths as the operating system compares them: case-insensitively on Windows and macOS.</summary>
    public static bool PathsEqual(string first, string second, OmePlatform platform)
        => string.Equals(Normalize(first), Normalize(second),
            platform == OmePlatform.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

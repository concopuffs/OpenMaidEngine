using System.Globalization;
using Age.Engine.Sys4;

namespace Age.Engine.Persistence;

/// <summary>
/// Resolves the two native AGE persistence locations from one SYS4INI profile. Native mode follows
/// USEAPPDATAFOLDER plus the independent SAVEPATH and REGFILEPATH values. A profile override replaces
/// the resolved REGFILEPATH directory while preserving SAVEPATH's relative tail beneath it.
/// </summary>
public readonly record struct Sys4PersistencePaths(
    string SaveDirectory,
    string Sys4RegIniPath)
{
    public static Sys4PersistencePaths ResolveNative(
        Sys4StartupSettings startupSettings,
        string gameRoot,
        string? localApplicationDataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(startupSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);

        bool useAppData = int.TryParse(
            startupSettings.GetValueOrDefault("USEAPPDATAFOLDER"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int useAppDataValue)
            && useAppDataValue != 0;
        string root = useAppData
            ? localApplicationDataRoot
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : gameRoot;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidDataException("Native AGE persistence root is unavailable.");

        string saveDirectory = CombineWindowsRelativePath(
            root, startupSettings.GetValueOrDefault("SAVEPATH"), "SAVEPATH");
        string settingsDirectory = CombineWindowsRelativePath(
            root, startupSettings.GetValueOrDefault("REGFILEPATH"), "REGFILEPATH");
        return new Sys4PersistencePaths(
            saveDirectory,
            Path.Combine(settingsDirectory, Sys4RegIniStore.FileName));
    }

    public static Sys4PersistencePaths ResolveProfileOverride(
        Sys4StartupSettings startupSettings,
        string profileRoot)
    {
        ArgumentNullException.ThrowIfNull(startupSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoot);

        IReadOnlyList<string> settingsComponents = ParseWindowsRelativePath(
            startupSettings.GetValueOrDefault("REGFILEPATH"), "REGFILEPATH");
        IReadOnlyList<string> saveComponents = ParseWindowsRelativePath(
            startupSettings.GetValueOrDefault("SAVEPATH"), "SAVEPATH");
        if (settingsComponents.Count > saveComponents.Count
            || !settingsComponents
                .Select((component, index) => string.Equals(
                    component, saveComponents[index], StringComparison.OrdinalIgnoreCase))
                .All(matches => matches))
        {
            throw new InvalidDataException(
                "SAVEPATH is not beneath REGFILEPATH; one profile-root override cannot safely " +
                "represent both native AGE persistence locations.");
        }

        string root = Path.GetFullPath(profileRoot);
        string saveDirectory = saveComponents
            .Skip(settingsComponents.Count)
            .Aggregate(root, Path.Combine);
        return new Sys4PersistencePaths(
            saveDirectory,
            Path.Combine(root, Sys4RegIniStore.FileName));
    }

    private static string CombineWindowsRelativePath(
        string root,
        string? relative,
        string settingName)
        => ParseWindowsRelativePath(relative, settingName)
            .Aggregate(Path.GetFullPath(root), Path.Combine);

    private static IReadOnlyList<string> ParseWindowsRelativePath(
        string? relative,
        string settingName)
    {
        string[] components = (relative ?? "").Split(
            ['\\', '/'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string component in components)
        {
            if (component is "." or ".." || component.Contains(':'))
                throw new InvalidDataException(
                    $"{settingName} contains unsafe component '{component}'.");
        }
        return components;
    }
}

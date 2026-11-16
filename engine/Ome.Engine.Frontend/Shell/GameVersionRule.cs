using System.Security;
using Ome.Engine.Sys4;

namespace Ome.Engine.Frontend.Shell;

/// <summary>Reads one string value under <c>HKEY_LOCAL_MACHINE</c> in the registry's 32-bit view, or null when there is
/// none.</summary>
public delegate string? MachineRegistryReader(string subKey, string name);

/// <summary>
/// <c>set:GameVersion</c>, the game version 情報 shows (docs/engine-re.md), and the launcher with it (launcher spec
/// §5.9, §8.2). With <c>VERREGPOS</c>, <c>registration_read_install_display_version@0x46ae80</c> reads the installer's
/// <c>DisplayVersion</c> from <c>HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\InstallShield_{GUID}</c> (a
/// 32-bit process, so the WOW6432Node view), and "1.00.0000" when there is none; without it, SYS4INI
/// <c>GAMEVERSION</c>. Off Windows there is no registry, so the installer default.
/// </summary>
public static class GameVersionRule
{
    public const string InstallerDefault = "1.00.0000";
    public const string DisplayVersion = "DisplayVersion";

    /// <param name="registry">The registry read; the machine's own (<see cref="ReadMachine32"/>) when null.</param>
    public static string Of(Sys4StartupSettings settings, MachineRegistryReader? registry = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string guid = settings.GetValueOrDefault("VERREGPOS") ?? "";
        if (guid.Length == 0) return settings.GetValueOrDefault("GAMEVERSION") ?? "";
        return (registry ?? ReadMachine32)(UninstallKey(guid), DisplayVersion) ?? InstallerDefault;
    }

    /// <summary>The installer's uninstall key for <c>VERREGPOS</c>'s GUID.</summary>
    public static string UninstallKey(string guid)
        => $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\InstallShield_{{{guid}}}";

    /// <summary>This machine's registry, in the 32-bit view; null off Windows and when the key cannot be read.</summary>
    public static string? ReadMachine32(string subKey, string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                         Microsoft.Win32.RegistryView.Registry32);
            using var key = machine.OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace Ome.Launcher.Core;

/// <summary>A Windows shortcut as <see cref="ShortcutFile.ReadWindowsLink"/> reads it back.</summary>
public sealed record WindowsLink(string Target, string Arguments, string WorkingDirectory, string Description);

/// <summary>
/// The Shortcut dialog's text and files (launcher spec §8.4). A command is shown as its target, its arguments and its
/// working folder, and as one command line quoted for the platform's own parser: the rules of
/// <c>CommandLineToArgvW</c> on Windows, a POSIX shell's elsewhere. "Create a shortcut" writes a <c>.lnk</c> on Windows
/// (through the shell's <c>IShellLinkW</c>) and a <c>.desktop</c> file on Linux (the Desktop Entry Specification's
/// <c>Exec</c> quoting). Every path is absolute, as the command is.
/// </summary>
public static class ShortcutFile
{
    /// <summary>Whether this platform has a shortcut file the launcher writes.</summary>
    public static bool Supported(OmePlatform platform) => platform is OmePlatform.Windows or OmePlatform.Linux;

    /// <summary>The arguments as one string, each quoted for the platform (the field a Windows shortcut's properties
    /// and other programs' "add a game" forms ask for).</summary>
    public static string Arguments(EngineCommand command, OmePlatform platform)
        => string.Join(" ", command.Arguments.Select(argument => Quote(argument, platform)));

    /// <summary>The whole command line: the environment the command sets (<c>env NAME=VALUE</c>, which only Linux
    /// uses), the program and its arguments.</summary>
    public static string CommandLine(EngineCommand command, OmePlatform platform)
    {
        var parts = new List<string>();
        if (command.Environment.Count > 0)
        {
            if (platform == OmePlatform.Windows)
                throw new ArgumentException("a Windows command line cannot set environment variables");
            parts.Add("env");
            parts.AddRange(command.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                               .Select(pair => Quote($"{pair.Key}={pair.Value}", platform)));
        }
        parts.Add(Quote(command.Program, platform));
        parts.AddRange(command.Arguments.Select(argument => Quote(argument, platform)));
        return string.Join(" ", parts);
    }

    /// <summary>One argument quoted for the platform. Windows: as <c>CommandLineToArgvW</c> splits it (backslashes are
    /// doubled only before a quote or the closing quote). Elsewhere: a POSIX shell word, in single quotes when it holds
    /// anything but the safe characters.</summary>
    public static string Quote(string argument, OmePlatform platform)
    {
        if (platform == OmePlatform.Windows)
        {
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"', '\n', '\v']) < 0) return argument;
            var quoted = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                quoted.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0;
                quoted.Append(c);
            }
            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }
        if (argument.Length > 0 && argument.All(c => char.IsAsciiLetterOrDigit(c) || "_@%+=:,./-".Contains(c)))
            return argument;
        return "'" + argument.Replace("'", "'\\''") + "'";
    }

    /// <summary>Where a shortcut named <paramref name="name"/> goes in <paramref name="folder"/>: the name with the
    /// characters no file name may hold replaced, and the platform's extension.</summary>
    public static string PathFor(string folder, string name, OmePlatform platform)
    {
        var clean = new StringBuilder();
        foreach (char c in name)
            clean.Append(char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c);
        string stem = clean.ToString().Trim().TrimEnd('.', ' ');
        if (stem.Length == 0) stem = "OME profile";
        if (IsReservedWindowsName(stem)) stem += "_";
        return Path.Combine(folder, stem + (platform == OmePlatform.Windows ? ".lnk" : ".desktop"));
    }

    private static bool IsReservedWindowsName(string stem)
    {
        string upper = stem.Split('.')[0].ToUpperInvariant();
        return upper is "CON" or "PRN" or "AUX" or "NUL"
               || upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal)
                                        || upper.StartsWith("LPT", StringComparison.Ordinal))
                                    && upper[3] is >= '1' and <= '9';
    }

    /// <summary>Writes the shortcut for <paramref name="command"/> at <paramref name="path"/>, replacing a file
    /// there.</summary>
    public static void Write(string path, string name, EngineCommand command, OmePlatform platform)
    {
        switch (platform)
        {
            case OmePlatform.Windows when OperatingSystem.IsWindows():
                WriteWindowsLink(path, name, command);
                break;
            case OmePlatform.Windows:
                throw new PlatformNotSupportedException("a Windows shortcut is written on Windows");
            case OmePlatform.Linux:
                File.WriteAllText(path, DesktopEntry(name, command), new UTF8Encoding(false));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                               | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                break;
            default:
                throw new PlatformNotSupportedException($"the launcher writes no shortcut file on {platform}");
        }
    }

    /// <summary>The <c>.desktop</c> file for <paramref name="command"/>: an application entry whose <c>Exec</c> runs
    /// the command (with <c>env</c> for its environment) and whose <c>Path</c> is its working folder.</summary>
    public static string DesktopEntry(string name, EngineCommand command)
    {
        var words = new List<string>();
        if (command.Environment.Count > 0)
        {
            words.Add("env");
            words.AddRange(command.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                               .Select(pair => $"{pair.Key}={pair.Value}"));
        }
        words.Add(command.Program);
        words.AddRange(command.Arguments);
        string exec = string.Join(" ", words.Select(ExecWord)).Replace("%", "%%");
        return "[Desktop Entry]\n"
               + "Type=Application\n"
               + "Version=1.5\n"
               + $"Name={EntryValue(name)}\n"
               + $"Exec={EntryValue(exec)}\n"
               + $"Path={EntryValue(command.WorkingDirectory)}\n"
               + "Terminal=false\n"
               + "Categories=Game;\n";
    }

    /// <summary>One word of <c>Exec</c>: in double quotes when it holds a reserved character, with <c>"</c>, <c>`</c>,
    /// <c>$</c> and <c>\</c> escaped inside them.</summary>
    private static string ExecWord(string word)
    {
        if (word.Length > 0 && word.IndexOfAny([' ', '\t', '"', '\'', '\\', '>', '<', '~', '|', '&', ';', '$', '*', '?',
                                                '#', '(', ')', '`']) < 0)
            return word;
        var quoted = new StringBuilder("\"");
        foreach (char c in word)
        {
            if (c is '"' or '`' or '$' or '\\') quoted.Append('\\');
            quoted.Append(c);
        }
        return quoted.Append('"').ToString();
    }

    /// <summary>A key file's string value: a backslash is escaped, and a line break cannot be held.</summary>
    private static string EntryValue(string value)
    {
        if (value.IndexOfAny(['\n', '\r']) >= 0)
            throw new ArgumentException($"a desktop entry's value cannot hold a line break: {value}");
        return value.Replace("\\", "\\\\");
    }

    [SupportedOSPlatform("windows")]
    private static void WriteWindowsLink(string path, string name, EngineCommand command)
    {
        if (command.Environment.Count > 0)
            throw new ArgumentException("a Windows shortcut cannot set environment variables");
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(command.Program);
            link.SetArguments(Arguments(command, OmePlatform.Windows));
            link.SetWorkingDirectory(command.WorkingDirectory);
            link.SetDescription(name);
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Reads a <c>.lnk</c> back: its target, arguments, working folder and description.</summary>
    [SupportedOSPlatform("windows")]
    public static WindowsLink ReadWindowsLink(string path)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var working = new StringBuilder(32768);
            var description = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */);
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(working, working.Capacity);
            link.GetDescription(description, description.Capacity);
            return new WindowsLink(target.ToString(), arguments.ToString(), working.ToString(), description.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath,
                             out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}

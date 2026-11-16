using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// The engine started with no game (launcher spec §5.6): with no engine argument at all, no <c>SYS4INI.BIN</c> in the
/// executable's folder or the current directory, and the launcher beside the executable, the engine starts the
/// launcher and exits, so double-clicking either program works. With any argument the engine reports its error as
/// before: a mistyped command must show its own error.
/// </summary>
public static class LauncherHandOver
{
    public const string LauncherName = "OME-Launcher";

    /// <summary>The launcher to start in place of the engine, or null to start the engine as usual.</summary>
    /// <param name="userArgs">The engine arguments (Godot's user arguments, after <c>--</c>).</param>
    /// <param name="windows">Whether the launcher's file name carries <c>.exe</c>.</param>
    public static string? Decide(IReadOnlyList<string> userArgs, string? executablePath, string workingDirectory,
                                 bool windows)
    {
        ArgumentNullException.ThrowIfNull(userArgs);
        if (userArgs.Count > 0 || string.IsNullOrWhiteSpace(executablePath)) return null;
        string cwd = Path.GetFullPath(workingDirectory);
        string? folder = Path.GetDirectoryName(Path.GetFullPath(executablePath, cwd));
        if (folder == null) return null;
        if (File.Exists(Path.Combine(folder, "SYS4INI.BIN")) || File.Exists(Path.Combine(cwd, "SYS4INI.BIN")))
            return null;
        string launcher = Path.Combine(folder, LauncherName + (windows ? ".exe" : ""));
        return File.Exists(launcher) ? launcher : null;
    }
}

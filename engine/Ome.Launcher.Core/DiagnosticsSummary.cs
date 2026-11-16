using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Ome.Mods.Launch;
using Ome.Mods.Linking;

namespace Ome.Launcher.Core;

/// <summary>
/// The text the Advanced page's "Copy a summary" puts on the clipboard (launcher spec §8.4, plan §6.8), for a player to
/// paste into a report: the launcher's version, the system, the profile's game, install, mode, mods and startup
/// options, its checks, and the end of <c>diagnostics/engine.log</c>. It reads nothing beyond the profile and its
/// install. Paths are written from known roots: the profile's as <c>&lt;profile&gt;</c>, the install's as
/// <c>&lt;install&gt;</c>, OME home's as <c>&lt;OME home&gt;</c>, and the player's home folder as <c>~</c>, so the
/// summary does not carry the account name; the two roots are given once at the top with the home folder as <c>~</c>.
/// It is in English, as the engine's own reports are, and is made only when the player asks (spec §12).
/// </summary>
public static class DiagnosticsSummary
{
    /// <summary>The summary of <paramref name="profile"/> with its <paramref name="checks"/> (none when they have not
    /// run). <paramref name="userHome"/> is the player's home folder; the system's when null.</summary>
    public static string Build(ProfileEntry profile, ProfileCheckResult? checks, string launcherVersion, OmeHome home,
                               string? userHome = null)
    {
        userHome ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ProfileDocument? document = profile.Loaded?.Document;
        string? install = document?.Install.Root;
        var roots = new List<(string Root, string Name)> { (profile.Root, "<profile>") };
        if (install != null) roots.Add((install, "<install>"));
        roots.Add((home.Root, "<OME home>"));
        var homeOnly = new List<(string Root, string Name)>();
        if (!string.IsNullOrEmpty(userHome))
        {
            roots.Add((userHome, "~"));
            homeOnly.Add((userHome, "~"));
        }
        string Masked(string text) => Mask(text, roots);
        string Top(string path) => Mask(path, homeOnly);

        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append('\n');

        Line("Open Maid Engine launcher: diagnostics summary");
        Line($"Launcher: {launcherVersion}");
        Line($"System: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), "
             + RuntimeInformation.FrameworkDescription);
        Line($"Profile: {profile.DisplayName} [{profile.Id}] at {Top(profile.Root)}"
             + (profile.Elsewhere ? " (outside OME home)" : ""));
        if (profile.Problem != null) Line($"Problem: {Masked(profile.Problem)}");
        if (document == null)
        {
            Line("The profile file was not read.");
        }
        else
        {
            InstallRecognition? found = checks?.Install;
            string titles = found?.Titles is { Count: > 0 } names ? " (" + string.Join(" / ", names) + ")" : "";
            Line($"Game: {document.Game}{titles}"
                 + (found?.GameVersion is { } version ? $", game version {version}" : "")
                 + (found?.Identity is { } identity ? $", catalog {identity.CatalogRevision.Trim()}" : ""));
            Line($"Install: {Top(document.Install.Root)}");
            Line($"  fingerprint {document.Install.Fingerprint ?? "(none)"}"
                 + (found?.Fingerprint is { } now && now != document.Install.Fingerprint ? $", now {now}" : ""));
            Line("  overlays " + (document.Install.Overlays.Count == 0
                     ? "(none)"
                     : string.Join(", ", document.Install.Overlays.Select(Masked)))
                 + $", allow_bmp_as_agf = {(document.Install.AllowBmpAsAgf ? "true" : "false")}");
            Line($"Mode: {(document.Mode == ProfileMode.Native ? "native" : "extended")}");
            if (document.Mods is { } mods)
            {
                Line($"Mods: on_failure = {(mods.SkipFailed ? "skip" : "stop")}");
                Line("  folders " + (mods.Folders.Count == 0 ? "(none)" : string.Join(", ", mods.Folders.Select(Masked))));
                Line("  order " + (mods.Order.Count == 0
                         ? "(none)"
                         : string.Join(", ", mods.Order.Select(entry => (entry.Enabled ? "" : "-") + entry.Id))));
            }
            else
            {
                Line("Mods: (no [mods] table)");
            }
            ProfileLaunch launch = document.Launch;
            Line($"Launch: renderer = {launch.Renderer ?? "(default)"}, text_backend = {launch.TextBackend ?? "(default)"}, "
                 + $"lua_errors = {launch.LuaErrors ?? "(default)"}, graphics_api = {document.Launcher.GraphicsApi}");
        }
        foreach (string warning in profile.Loaded?.Warnings ?? []) Line($"Warning: {Masked(warning)}");

        Line();
        if (checks == null)
        {
            Line("Checks: not run");
        }
        else
        {
            Line("Checks:");
            foreach (ProfileCheck check in checks.Checks)
                Line($"  {check.Kind}: {check.Status}" + (check.Message.Length > 0 ? " - " + Masked(check.Message) : ""));
            if (checks.Link is { } link)
            {
                if (link.Order.Count > 0) Line("  resolved order " + string.Join(", ", link.Order));
                foreach (OrderMove move in link.Moves)
                    Line($"  moved: {move.Id} loads after {string.Join(", ", move.Overtaken)} ("
                         + Masked(string.Join("; ", move.Edges)) + ")");
                if (link.Dropped.Count > 0) Line("  a skipped link drops " + string.Join(", ", link.Dropped));
            }
        }

        Line();
        IReadOnlyList<string>? tail = LogTail(profile.Root);
        if (tail == null)
        {
            Line($"{EngineLaunch.LogFile}: none yet");
        }
        else
        {
            Line($"{EngineLaunch.LogFile} (the last {tail.Count} lines):");
            foreach (string line in tail) Line("  " + Masked(line));
        }
        return text.ToString();
    }

    /// <summary>The last <see cref="EngineLaunch.TailLines"/> lines of the profile's <c>diagnostics/engine.log</c>, or
    /// null when there is none. A running engine may be writing it, so it is read shared.</summary>
    public static IReadOnlyList<string>? LogTail(string profileRoot)
    {
        string path = Path.Combine(profileRoot, EngineLaunch.DiagnosticsFolder, EngineLaunch.LogFile);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tail = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                tail.Enqueue(line);
                if (tail.Count > EngineLaunch.TailLines) tail.Dequeue();
            }
            return tail.ToList();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [$"(the log could not be read: {error.Message})"];
        }
    }

    /// <summary>Writes each root in <paramref name="text"/> as its name, the longest root first: in either slash,
    /// without regard to case on Windows, where paths are not case-sensitive, and only where the root's last name ends
    /// (so the home folder <c>C:\Users\ann</c> is not found in <c>C:\Users\anna</c>).</summary>
    private static string Mask(string text, IEnumerable<(string Root, string Name)> roots)
    {
        RegexOptions options = RegexOptions.CultureInvariant | (OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : 0);
        foreach (var (root, name) in roots
                     .Select(pair => (Root: Path.TrimEndingDirectorySeparator(Path.GetFullPath(pair.Root)), pair.Name))
                     .Where(pair => pair.Root.Length > 3)   // never a drive's or the file system's own root
                     .OrderByDescending(pair => pair.Root.Length))
        {
            string pattern = string.Join(@"[\\/]", root.Split('\\', '/').Select(Regex.Escape)) + NameGoesOn;
            text = Regex.Replace(text, pattern, name.Replace("$", "$$"), options);
        }
        return text;
    }

    /// <summary>After a root, a character that would carry its last name on: a letter, a digit, or one of the marks a
    /// file name holds.</summary>
    private const string NameGoesOn = @"(?![\p{L}\p{N}_.\-~$#@!&+=%'(\[{])";
}

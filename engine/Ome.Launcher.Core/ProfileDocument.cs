using System.Text;
using Ome.Mods.Launch;
using Ome.Mods.Localization;
using Ome.Mods.Manifest;
using Tomlyn;
using Tomlyn.Model;

namespace Ome.Launcher.Core;

/// <summary><c>profile.toml</c>'s <c>[launcher]</c> table (launcher spec §4.3), which the engine does not read: the
/// profile's own cover (a file inside the profile root, spec §8.7, §12; a full path here, written relative to the root),
/// the graphics API Godot starts with (§8.5), and the player's notes.</summary>
public sealed record ProfileLauncherTable(string? Cover = null, string GraphicsApi = ProfileLauncherTable.DefaultApi,
                                          string Notes = "")
{
    public const string DefaultApi = "default", Direct3D12 = "d3d12";
    public static readonly IReadOnlyList<string> GraphicsApis = [DefaultApi, Direct3D12];
    public static ProfileLauncherTable Defaults { get; } = new();
}

/// <summary>Everything <c>profile.toml</c> says, as the launcher edits it: the engine's part in the engine's own
/// records (<see cref="ProfileFile"/>), with full paths, and the launcher's table.</summary>
public sealed record ProfileDocument(string? Name, string Game, ProfileMode Mode, ProfileInstall Install,
                                     ProfileMods? Mods, ProfileLaunch Launch, ProfileLauncherTable Launcher)
{
    public static ProfileDocument From(ProfileFile file, ProfileLauncherTable launcher)
        => new(file.Name, file.Game, file.Mode, file.Install, file.Mods, file.Launch, launcher);

    /// <summary>A new profile of <paramref name="game"/> on <paramref name="installRoot"/>: Compatibility mode
    /// (spec §6), no mod list and the default startup options.</summary>
    public static ProfileDocument New(string? name, string game, string installRoot, string? fingerprint,
                                      IReadOnlyList<string>? overlays = null, bool allowBmpAsAgf = false)
        => new(name, game, ProfileMode.Native,
               new ProfileInstall(Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot)), fingerprint,
                                  overlays ?? [], allowBmpAsAgf),
               null, new ProfileLaunch(null, null, null), ProfileLauncherTable.Defaults);
}

/// <summary>A profile root's <c>profile.toml</c> as the launcher read it: the engine's reading, the whole document,
/// and what was wrong with the launcher's table.</summary>
public sealed record LoadedProfile(string Root, ProfileFile File, ProfileDocument Document, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads and writes <c>profile.toml</c> (launcher spec §4.3). The engine's part is read by the engine's own reader,
/// so the launcher sees what the engine will; the <c>[launcher]</c> table is read here. The writer is the launcher's,
/// since the engine never writes the file, and every file it writes reads back through the engine's reader to the same
/// values. Paths are written relative to the profile root when both are inside OME home (spec §4.3), and overlays
/// relative to the install root when they are inside it.
/// </summary>
public static class ProfileDocuments
{
    /// <summary>Reads <paramref name="root"/>'s profile file once, with the engine's size limit; null when it has
    /// none. Throws <see cref="ProfileFileException"/> for a file the engine would refuse.</summary>
    public static LoadedProfile? Read(string root)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string path = Path.Combine(full, ProfileFile.FileName);
        if (!File.Exists(path)) return null;
        long length = new FileInfo(path).Length;
        if (length > ProfileFile.MaxBytes)
            throw new ProfileFileException(path, [$"the file is {length} bytes; at most {ProfileFile.MaxBytes} are read"]);
        string text = File.ReadAllText(path);
        ProfileFile file = ProfileFile.Parse(text, full, path);
        var warnings = new List<string>();
        ProfileLauncherTable launcher = ReadLauncherTable(text, full, warnings);
        return new LoadedProfile(full, file, ProfileDocument.From(file, launcher), warnings);
    }

    /// <summary>The <c>[launcher]</c> table of a file the engine's reader accepted. A key of the wrong type, an unknown
    /// key and a cover outside the profile root are warnings, and the key keeps its default.</summary>
    public static ProfileLauncherTable ReadLauncherTable(string text, string profileRoot, List<string> warnings)
    {
        TomlTable table = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        if (!table.TryGetValue("launcher", out object? value)) return ProfileLauncherTable.Defaults;
        if (value is not TomlTable launcherTable)
        {
            warnings.Add($"{ProfileFile.FileName}: launcher: expected a table; the launcher's settings are not used");
            return ProfileLauncherTable.Defaults;
        }
        var problems = new List<string>();
        var reader = new TableReader(launcherTable, "launcher.", problems);
        string? written = reader.String("cover");
        string? cover = written == null ? null : CoverPath(written, profileRoot);
        if (written != null && cover == null)
            problems.Add($"launcher.cover: '{written}' is not a file inside the profile root; it is not used");
        string? api = reader.String("graphics_api");
        if (api != null && !ProfileLauncherTable.GraphicsApis.Contains(api))
        {
            problems.Add($"launcher.graphics_api: '{api}' is not one of "
                         + string.Join(", ", ProfileLauncherTable.GraphicsApis.Select(name => $"\"{name}\"")));
            api = null;
        }
        string notes = reader.String("notes") ?? "";
        reader.RejectUnknown();
        warnings.AddRange(problems.Select(problem => $"{ProfileFile.FileName}: {problem}"));
        return new ProfileLauncherTable(cover, api ?? ProfileLauncherTable.DefaultApi, notes);
    }

    /// <summary>The full path of a cover written as <paramref name="cover"/>, or null when it is not inside
    /// <paramref name="profileRoot"/> (spec §12).</summary>
    public static string? CoverPath(string cover, string profileRoot)
    {
        try
        {
            if (cover.Length == 0) return null;
            string full = Path.GetFullPath(cover, profileRoot);
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileRoot));
            return HomePaths.IsInside(full, root) && !HomePaths.IsInside(root, full) ? full : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>The file's text for <paramref name="document"/> in <paramref name="profileRoot"/>.</summary>
    public static string Serialize(ProfileDocument document, string profileRoot, OmeHome home)
    {
        string root = Path.GetFullPath(profileRoot);
        var text = new StringBuilder();
        text.Append("# An Open Maid Engine profile (launcher spec §4.3). The launcher writes this file; the engine only\n")
            .Append("# reads it, and a key it does not know outside [launcher] stops it.\n");
        text.Append("ome_profile = ").Append(ProfileFile.CurrentVersion).Append('\n');
        if (document.Name != null) Key(text, "name", document.Name);
        Key(text, "game", document.Game);
        Key(text, "mode", document.Mode == ProfileMode.Native ? ModeCheck.Native : ModeCheck.Extended);

        ProfileInstall install = document.Install;
        text.Append("\n[install]\n");
        Key(text, "root", HomePaths.ForFile(install.Root, root, home));
        if (install.Fingerprint != null) Key(text, "fingerprint", install.Fingerprint);
        if (install.Overlays.Count > 0)
            Strings(text, "overlays", install.Overlays.Select(overlay => OverlayText(overlay, install.Root)));
        text.Append("allow_bmp_as_agf = ").Append(install.AllowBmpAsAgf ? "true" : "false").Append('\n');

        if (document.Mods is { } mods)
        {
            text.Append("\n[mods]\n");
            Strings(text, "folders", mods.Folders.Select(folder => HomePaths.ForFile(folder, root, home)));
            Strings(text, "order", mods.Order.Select(entry => entry.Enabled ? entry.Id : "-" + entry.Id));
            Key(text, "on_failure", mods.SkipFailed ? "skip" : "stop");
        }

        ProfileLaunch launch = document.Launch;
        if (launch.Renderer != null || launch.TextBackend != null || launch.LuaErrors != null)
        {
            text.Append("\n[launch]\n");
            if (launch.Renderer != null) Key(text, "renderer", launch.Renderer);
            if (launch.TextBackend != null) Key(text, "text_backend", launch.TextBackend);
            if (launch.LuaErrors != null) Key(text, "lua_errors", launch.LuaErrors);
        }

        ProfileLauncherTable launcher = document.Launcher;
        text.Append("\n[launcher]                           # the launcher's own; the engine does not read this table\n");
        if (launcher.Cover != null)
            Key(text, "cover", CoverPath(launcher.Cover, root) is { } cover
                ? Path.GetRelativePath(root, cover).Replace('\\', '/')
                : throw new ArgumentException($"the cover {launcher.Cover} is not inside the profile root {root}"));
        Key(text, "graphics_api", launcher.GraphicsApi);
        if (launcher.Notes.Length > 0) Key(text, "notes", launcher.Notes);

        // The engine must read back what was meant: a writer that drifted from the reader fails here, not at a start.
        ProfileFile check = ProfileFile.Parse(text.ToString(), root);
        if (!SameAsEngineReads(document, check))
            throw new InvalidOperationException("profile.toml would not read back as written");
        return text.ToString();
    }

    /// <summary>Writes the file through a temporary file. The caller holds the profile's lock (spec §4.4).</summary>
    public static void Write(ProfileDocument document, string profileRoot, OmeHome home)
    {
        string path = Path.Combine(Path.GetFullPath(profileRoot), ProfileFile.FileName);
        string text = Serialize(document, profileRoot, home);
        Directory.CreateDirectory(profileRoot);
        string temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Whether the engine's reading of a file equals the document's engine part.</summary>
    public static bool SameAsEngineReads(ProfileDocument document, ProfileFile file)
        => document.Name == file.Name && document.Game == file.Game && document.Mode == file.Mode
           && SamePath(document.Install.Root, file.Install.Root)
           && document.Install.Fingerprint == file.Install.Fingerprint
           && document.Install.Overlays.Count == file.Install.Overlays.Count
           && document.Install.Overlays.Zip(file.Install.Overlays).All(pair => SamePath(pair.First, pair.Second))
           && document.Install.AllowBmpAsAgf == file.Install.AllowBmpAsAgf
           && (document.Mods == null) == (file.Mods == null)
           && (document.Mods == null || SameMods(document.Mods, file.Mods!))
           && document.Launch == file.Launch;

    private static bool SameMods(ProfileMods first, ProfileMods second)
        => first.SkipFailed == second.SkipFailed && first.Order.SequenceEqual(second.Order)
           && first.Folders.Count == second.Folders.Count
           && first.Folders.Zip(second.Folders).All(pair => SamePath(pair.First, pair.Second));

    private static bool SamePath(string first, string second)
        => OmeHome.PathsEqual(first, second, LauncherEnvironment.Current.Platform);

    private static string OverlayText(string overlay, string installRoot)
        => HomePaths.IsInside(overlay, installRoot)
            ? Path.GetRelativePath(Path.GetFullPath(installRoot), Path.GetFullPath(overlay)).Replace('\\', '/')
            : Path.GetFullPath(overlay);

    private static void Key(StringBuilder text, string key, string value)
        => text.Append(key).Append(" = ").Append(TextCatalogFileWriter.Quote(value)).Append('\n');

    private static void Strings(StringBuilder text, string key, IEnumerable<string> values)
        => text.Append(key).Append(" = [").Append(string.Join(", ", values.Select(TextCatalogFileWriter.Quote))).Append("]\n");
}

using Ome.Mods.Manifest;
using Tomlyn;
using Tomlyn.Model;

namespace Ome.Mods.Launch;

/// <summary>A <c>profile.toml</c> that does not parse or validate: every problem found, each prefixed with its key
/// path, as a package manifest's are.</summary>
public sealed class ProfileFileException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public ProfileFileException(string origin, IReadOnlyList<string> problems)
        : base($"{origin}: " + string.Join("; ", problems))
        => Problems = problems;
}

/// <summary>The package target a profile admits (launcher spec §6): <c>native</c> or <c>extended</c>.</summary>
public enum ProfileMode
{
    Native,
    Extended,
}

/// <summary><c>[install]</c>: the game's install, its fingerprint when the profile last accepted it, and its
/// asset options. <see cref="Root"/> and <see cref="Overlays"/> are full paths.</summary>
public sealed record ProfileInstall(string Root, string? Fingerprint, IReadOnlyList<string> Overlays, bool AllowBmpAsAgf);

/// <summary>One entry of <c>[mods] order</c>: a package id, enabled, or disabled with its place kept (<c>-ID</c>).</summary>
public sealed record ProfileModEntry(string Id, bool Enabled);

/// <summary><c>[mods]</c>: the folders the profile's packages come from (full paths, each once), the order that
/// enables them, and what a failed link does.</summary>
public sealed record ProfileMods(IReadOnlyList<string> Folders, IReadOnlyList<ProfileModEntry> Order, bool SkipFailed)
{
    /// <summary>The ids <see cref="Order"/> enables, in order.</summary>
    public IEnumerable<string> Enabled => Order.Where(entry => entry.Enabled).Select(entry => entry.Id);
}

/// <summary><c>[launch]</c>: the startup options the engine reads, each null when the file leaves it out.</summary>
public sealed record ProfileLaunch(string? Renderer, string? TextBackend, string? LuaErrors);

/// <summary>
/// A profile root's <c>profile.toml</c> (launcher spec §4.3): what the profile is. The launcher writes it and the engine
/// only reads it. The keys are fixed and typed; outside <c>[launcher]</c>, the launcher's own table, an unknown key is
/// an error, as in a package manifest. A format version other than <see cref="CurrentVersion"/> stops with the
/// version this engine reads. Paths are absolute or relative to the profile root; <c>[install] overlays</c> are
/// relative to the install root, as <c>--overlay-root</c> is.
/// </summary>
public sealed record ProfileFile(
    string Path,
    string ProfileRoot,
    string? Name,
    string Game,
    ProfileMode Mode,
    ProfileInstall Install,
    ProfileMods? Mods,
    ProfileLaunch Launch)
{
    public const string FileName = "profile.toml";
    public const int CurrentVersion = 1;
    /// <summary>Longest file the reader accepts.</summary>
    public const int MaxBytes = 1 << 20;

    public static readonly IReadOnlyList<string> Renderers = ["gpu", "software"];
    public static readonly IReadOnlyList<string> TextBackends = ["auto", "gdi", "portable"];
    public static readonly IReadOnlyList<string> LuaErrorModes = ["continue", "halt"];
    public static readonly IReadOnlyList<string> FailureModes = ["stop", "skip"];

    public string ModeName => Mode == ProfileMode.Native ? "native" : "extended";

    /// <summary>The profile file of a profile root, or null when it has none.</summary>
    public static ProfileFile? TryRead(string profileRoot)
    {
        string root = System.IO.Path.GetFullPath(profileRoot);
        string path = System.IO.Path.Combine(root, FileName);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length > MaxBytes)
            throw new ProfileFileException(path, [$"the file is {info.Length} bytes; at most {MaxBytes} are read"]);
        return Parse(File.ReadAllText(path), root, path);
    }

    public static ProfileFile Parse(string text, string profileRoot, string origin = FileName)
    {
        string root = System.IO.Path.GetFullPath(profileRoot);
        TomlTable table;
        try
        {
            table = TomlDocument.Parse(text)
                    ?? throw new ProfileFileException(origin, ["the file is empty"]);
        }
        catch (TomlException error)
        {
            throw new ProfileFileException(origin, [$"not valid TOML: {error.Message}"]);
        }

        var reader = new TableReader(table, "", []);
        int? version = reader.Integer("ome_profile", required: true);
        // A newer format may add keys, so an unknown version stops with that one problem.
        if (reader.Problems.Count > 0)
            throw new ProfileFileException(origin, reader.Problems);
        if (version != CurrentVersion)
            throw new ProfileFileException(origin,
                [$"ome_profile: format version {version} is not supported (this engine reads version {CurrentVersion})"]);

        string? name = reader.String("name");
        string game = reader.String("game", required: true) ?? "";
        if (game.Length > 0 && !ManifestReader.IsValidId(game))
            reader.Problems.Add($"game: '{game}' is not a game id");
        string? modeText = reader.String("mode", required: true);
        ProfileMode mode = ProfileMode.Native;
        if (modeText is "extended") mode = ProfileMode.Extended;
        else if (modeText is not (null or "native"))
            reader.Problems.Add($"mode: '{modeText}' is not a mode; a profile is \"native\" or \"extended\"");

        ProfileInstall install = new(root, null, [], false);
        if (reader.Table("install", required: true) is { } installTable)
        {
            string installRoot = installTable.String("root", required: true) ?? "";
            string full = installRoot.Length > 0 ? FullPath(installTable, "root", installRoot, root) ?? root : root;
            string? fingerprint = installTable.String("fingerprint");
            if (fingerprint != null && !ManifestReader.IsValidFingerprint(fingerprint))
                installTable.Problems.Add($"install.fingerprint: '{fingerprint}' is not a sha256:<64 lowercase hex> fingerprint");
            var overlays = new List<string>();
            foreach (string overlay in installTable.Strings("overlays"))
                if (FullPath(installTable, "overlays", overlay, full) is { } resolved && !Contains(overlays, resolved))
                    overlays.Add(resolved);
            bool allowBmp = installTable.Boolean("allow_bmp_as_agf") ?? false;
            installTable.RejectUnknown();
            install = new ProfileInstall(full, fingerprint, overlays, allowBmp);
        }

        ProfileMods? mods = null;
        if (reader.Table("mods") is { } modsTable)
        {
            var folders = new List<string>();
            foreach (string folder in modsTable.Strings("folders"))
                if (FullPath(modsTable, "folders", folder, root) is { } resolved && !Contains(folders, resolved))
                    folders.Add(resolved);
            var order = new List<ProfileModEntry>();
            foreach (string entry in modsTable.Strings("order"))
            {
                bool enabled = !entry.StartsWith('-');
                string id = enabled ? entry : entry[1..];
                if (!ManifestReader.IsValidId(id))
                    modsTable.Problems.Add($"mods.order: '{entry}' is not a mod id (or -ID for a disabled one)");
                else if (order.Any(other => other.Id == id))
                    modsTable.Problems.Add($"mods.order: '{id}' is listed more than once");
                else order.Add(new ProfileModEntry(id, enabled));
            }
            string onFailure = OneOf(modsTable, "mods.", "on_failure", FailureModes) ?? "stop";
            modsTable.RejectUnknown();
            mods = new ProfileMods(folders, order, onFailure == "skip");
        }

        ProfileLaunch launch = new(null, null, null);
        if (reader.Table("launch") is { } launchTable)
        {
            launch = new ProfileLaunch(
                OneOf(launchTable, "launch.", "renderer", Renderers),
                OneOf(launchTable, "launch.", "text_backend", TextBackends),
                OneOf(launchTable, "launch.", "lua_errors", LuaErrorModes));
            launchTable.RejectUnknown();
        }

        // The launcher's own table: the engine reads none of it, so any content is accepted.
        reader.Table("launcher");
        reader.RejectUnknown();
        if (reader.Problems.Count > 0)
            throw new ProfileFileException(origin, reader.Problems);
        return new ProfileFile(origin, root, name, game, mode, install, mods, launch);
    }

    private static string? OneOf(TableReader table, string prefix, string key, IReadOnlyList<string> values)
    {
        string? value = table.String(key);
        if (value == null || values.Contains(value)) return value;
        table.Problems.Add($"{prefix}{key}: '{value}' is not one of {string.Join(", ", values.Select(v => $"\"{v}\""))}");
        return null;
    }

    private static string? FullPath(TableReader table, string key, string path, string basePath)
    {
        try
        {
            if (path.Length == 0) throw new ArgumentException("an empty path");
            return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path, basePath));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            table.Problems.Add($"{table.Path}{key}: '{path}' is not a usable path ({error.Message})");
            return null;
        }
    }

    private static bool Contains(IEnumerable<string> paths, string candidate)
        => paths.Any(path => string.Equals(path, candidate, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal));
}

using System.Text;
using Ome.Mods.Localization;
using Ome.Mods.Manifest;
using Tomlyn;
using Tomlyn.Model;

namespace Ome.Mods.Launch;

/// <summary>
/// This machine's own state, kept outside OME home and every data root (launcher spec §5.3, §12): the network
/// locations its player approved, and the random value that makes a link cache entry usable only on the machine
/// that wrote it. It lives in <c>machine.toml</c> in Godot's user folder for this project
/// (<see cref="DefaultFolder"/>), so a profile root or a whole OME home from someone else brings none of it.
/// </summary>
public sealed class MachineState
{
    public const string FileName = "machine.toml";
    public const int CurrentVersion = 1;
    /// <summary><c>application/config/name</c> in <c>godot/project.godot</c>: Godot names the user folder after it.</summary>
    public const string GodotProjectName = "Open Maid Engine";

    public string Folder { get; }
    public string Path => System.IO.Path.Combine(Folder, FileName);
    /// <summary>The approved network locations: a path is approved when it is one of them or lies under one.</summary>
    public IReadOnlyList<string> ApprovedNetworkLocations { get; }
    /// <summary>The link cache's machine value (<c>cache_value</c>): random, made once by the first engine or launcher
    /// that needs it. Null until then.</summary>
    public string? CacheValue { get; }

    private MachineState(string folder, IReadOnlyList<string> approved, string? cacheValue)
    {
        Folder = folder;
        ApprovedNetworkLocations = approved;
        CacheValue = cacheValue;
    }

    /// <summary>
    /// Godot's user folder for this project, computed by Godot's rule for a project with no custom user folder, for
    /// the programs that run without Godot (<c>Ome.Cli</c>, the launcher). The host passes the folder Godot reports
    /// instead, and its self-test checks that the two agree.
    /// </summary>
    public static string DefaultFolder()
    {
        string data;
        string godot = "Godot";
        if (OperatingSystem.IsWindows())
            data = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        else if (OperatingSystem.IsMacOS())
            data = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                          "Library", "Application Support");
        else
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            data = !string.IsNullOrEmpty(xdg) && System.IO.Path.IsPathRooted(xdg)
                ? xdg
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            godot = "godot";
        }
        return System.IO.Path.Combine(data, godot, "app_userdata", GodotProjectName);
    }

    /// <summary>The state kept in <paramref name="folder"/>; empty when it has no <c>machine.toml</c>.</summary>
    public static MachineState Load(string folder)
    {
        string root = System.IO.Path.GetFullPath(folder);
        string path = System.IO.Path.Combine(root, FileName);
        if (!File.Exists(path)) return new MachineState(root, [], null);
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path)) ?? new TomlTable();
        }
        catch (TomlException error)
        {
            throw new InvalidDataException($"{path}: not valid TOML: {error.Message}");
        }
        var reader = new TableReader(table, "", []);
        int? version = reader.Integer("ome_machine", required: true);
        if (reader.Problems.Count == 0 && version != CurrentVersion)
            throw new InvalidDataException(
                $"{path}: ome_machine: format version {version} is not supported (this engine reads version {CurrentVersion})");
        string? cacheValue = reader.String("cache_value");
        if (cacheValue != null && !IsCacheValue(cacheValue))
            reader.Problems.Add("cache_value: expected 64 lowercase hexadecimal digits");
        var approved = new List<string>();
        if (reader.Table("network") is { } network)
        {
            foreach (string location in network.Strings("approved"))
                if (RootSafety.Normalize(location) is { } normalized && !RootSafety.ContainsPath(approved, normalized))
                    approved.Add(normalized);
            network.RejectUnknown();
        }
        reader.RejectUnknown();
        if (reader.Problems.Count > 0)
            throw new InvalidDataException($"{path}: " + string.Join("; ", reader.Problems));
        return new MachineState(root, approved, cacheValue);
    }

    private static bool IsCacheValue(string text) => text.Length == 64 && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// The machine value for the link cache key (launcher spec §5.3), made and kept on first use. A state that cannot be
    /// read or written gives a value for this run only, with the reason in <paramref name="warning"/>: the run's cache
    /// entries then miss at the next start, which a cache allows. Two first starts at once may both make one; the last
    /// write is kept, and the other's entries miss once.
    /// </summary>
    public static string CacheValueFor(string folder, out string? warning)
    {
        warning = null;
        MachineState state;
        try
        {
            state = Load(folder);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            warning = $"this machine's state cannot be read, so this run's link cache entries are not reused: {error.Message}";
            return NewCacheValue();
        }
        if (state.CacheValue is { } value) return value;
        var made = new MachineState(state.Folder, state.ApprovedNetworkLocations, NewCacheValue());
        try
        {
            made.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warning = $"{made.Path} cannot be written, so this run's link cache entries are not reused: {error.Message}";
        }
        return made.CacheValue!;
    }

    private static string NewCacheValue()
        => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public bool IsApproved(string path)
    {
        string? normalized = RootSafety.Normalize(path);
        return normalized != null && ApprovedNetworkLocations.Any(location => RootSafety.IsSameOrUnder(normalized, location));
    }

    /// <summary>Approves <paramref name="location"/> on this machine and writes the file: the launcher's choice when
    /// the player picks a network folder or accepts a review that names one (launcher spec §12).</summary>
    public MachineState Approve(string location)
    {
        string normalized = RootSafety.Normalize(location)
            ?? throw new ArgumentException($"'{location}' is not a usable path", nameof(location));
        if (RootSafety.ContainsPath(ApprovedNetworkLocations, normalized)) return this;
        var state = new MachineState(Folder, [.. ApprovedNetworkLocations, normalized], CacheValue);
        state.Save();
        return state;
    }

    private void Save()
    {
        var text = new StringBuilder()
            .Append("# This machine's own state (launcher spec §12). It is not part of any profile: do not copy it.\n")
            .Append("ome_machine = ").Append(CurrentVersion).Append('\n');
        if (CacheValue != null) text.Append("cache_value = ").Append(TextCatalogFileWriter.Quote(CacheValue)).Append('\n');
        if (ApprovedNetworkLocations.Count > 0)
        {
            text.Append("\n[network]\napproved = [\n");
            foreach (string location in ApprovedNetworkLocations)
                text.Append("  ").Append(TextCatalogFileWriter.Quote(location)).Append(",\n");
            text.Append("]\n");
        }
        Directory.CreateDirectory(Folder);
        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, text.ToString());
        File.Move(temporary, Path, overwrite: true);
    }
}

using Ome.Mods.Manifest;

namespace Ome.Mods.Packages;

/// <summary>One candidate as its manifest describes it, read without opening or hashing the rest of the package
/// (launcher spec §5.3, §5.9).</summary>
/// <param name="Game">The game the package targets (<c>[game].profile</c>); null for one that targets every game.</param>
/// <param name="ShipsLua">Whether the package ships Lua: a <c>[lua]</c> table, hooks, or <c>lua/</c> or <c>events/</c>
/// files.</param>
/// <param name="Manifest">The whole manifest, for what the launcher shows of a package: its name, authors and
/// description, its revisions and conflicts.</param>
public sealed record PackageIndexEntry(string Path, string Id, SemanticVersion Version, string? Game, string? Target,
                                       IReadOnlyList<ModRequirement> Requires, bool ShipsLua, ModManifest Manifest)
{
    /// <summary>Whether the package can run on <paramref name="game"/>.</summary>
    public bool IsFor(string game) => Game == null || Game == game;

    /// <summary>Whether the package runs on the install whose <c>SYS4INI.BIN</c> has <paramref name="fingerprint"/>:
    /// <c>[game].revisions</c> empty admits any.</summary>
    public bool Supports(string fingerprint)
        => Manifest.Revisions.Count == 0 || Manifest.Revisions.Contains(fingerprint, StringComparer.Ordinal);
}

/// <summary>A candidate of a folder that was not indexed, by its file name, or a folder that could not be read.</summary>
public sealed record PackageIndexNote(string Name, string Message);

/// <summary>A folder's index: an entry for every candidate whose manifest reads, a note for every other.</summary>
public sealed record PackageFolderIndex(string Folder, IReadOnlyList<PackageIndexEntry> Entries,
                                        IReadOnlyList<PackageIndexNote> Notes)
{
    /// <summary>The ids found more than once in the folder, each with its copies (launcher spec §7: one version of an
    /// id per folder).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PackageIndexEntry>> Duplicates
        => Entries.GroupBy(entry => entry.Id, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PackageIndexEntry>)group.ToList(), StringComparer.Ordinal);
}

/// <summary>Reads a candidate's manifest only, under the package reader's path and size limits.</summary>
public static class PackageIndex
{
    /// <summary>The candidate's index entry. A folder is walked and an <c>.omp</c>'s central directory read; of the
    /// content, only <c>manifest.toml</c> is. Throws what <see cref="ModPackage.Open"/> throws for an unreadable
    /// manifest.</summary>
    public static PackageIndexEntry Read(string path, PackageLimits? limits = null)
    {
        using IPackageContainer container = ModPackage.OpenContainer(path, limits);
        ModManifest manifest = ModPackage.ReadManifest(container);
        bool shipsLua = manifest.Lua != null || manifest.Hooks.Count > 0
                        || container.Entries.Any(entry => entry.Path.StartsWith(ModLayout.LuaFolder + "/", StringComparison.Ordinal)
                                                          || entry.Path.StartsWith(ModLayout.EventsFolder + "/", StringComparison.Ordinal));
        return new PackageIndexEntry(container.Location, manifest.Id, manifest.Version, manifest.Profile, manifest.Target,
                                     manifest.Requires, shipsLua, manifest);
    }

    /// <summary>The candidates of a mods folder: each <c>.omp</c> file and each folder with a <c>manifest.toml</c>,
    /// skipping dot-entries such as <c>.cache</c>.</summary>
    public static IReadOnlyList<string> Candidates(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var files = Directory.EnumerateFiles(folder, "*" + ModPackage.PackageExtension)
            .Where(path => !System.IO.Path.GetFileName(path).StartsWith('.'));
        var folders = Directory.EnumerateDirectories(folder)
            .Where(path => !System.IO.Path.GetFileName(path).StartsWith('.')
                           && File.Exists(System.IO.Path.Combine(path, ManifestReader.FileName)));
        return files.Concat(folders).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every candidate of <paramref name="folder"/> by its manifest alone. A candidate that does not read is
    /// a note, so a broken file in a shared folder stops nothing that leaves it off; so is a folder that does not exist
    /// or cannot be read.</summary>
    public static PackageFolderIndex ReadFolder(string folder, PackageLimits? limits = null)
    {
        var entries = new List<PackageIndexEntry>();
        var notes = new List<PackageIndexNote>();
        IReadOnlyList<string> candidates;
        try
        {
            if (!Directory.Exists(folder))
            {
                notes.Add(new PackageIndexNote(System.IO.Path.GetFileName(folder), $"the mod folder {folder} does not exist"));
                return new PackageFolderIndex(folder, entries, notes);
            }
            candidates = Candidates(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            notes.Add(new PackageIndexNote(System.IO.Path.GetFileName(folder),
                                           $"the mod folder {folder} cannot be read: {error.Message}"));
            return new PackageFolderIndex(folder, entries, notes);
        }
        foreach (string candidate in candidates)
            try
            {
                entries.Add(Read(candidate, limits));
            }
            catch (Exception error) when (error is ModPackageException or ModManifestException or IOException
                                              or InvalidDataException or UnauthorizedAccessException)
            {
                notes.Add(new PackageIndexNote(System.IO.Path.GetFileName(candidate), $"{candidate} is left out: {error.Message}"));
            }
        return new PackageFolderIndex(folder, entries, notes);
    }
}

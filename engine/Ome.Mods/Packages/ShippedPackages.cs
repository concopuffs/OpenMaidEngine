using System.Reflection;
using Ome.Engine.Modding;
using Ome.Mods.Linking;

namespace Ome.Mods.Packages;

/// <summary>A package held in memory: one the engine ships inside its own build (<see cref="ShippedPackages"/>).</summary>
public sealed class MemoryPackageContainer : IPackageContainer
{
    private readonly Dictionary<string, (PackageEntry Entry, byte[] Bytes)> _files;

    public string Location { get; }
    public IReadOnlyList<PackageEntry> Entries { get; }

    public MemoryPackageContainer(string location, IEnumerable<(string Path, byte[] Bytes)> files, PackageLimits? limits = null)
    {
        Location = location;
        var contents = files.ToList();
        var problems = new List<string>();
        Entries = PackageListing.Validate(location, contents.Select(file => (file.Path, (long)file.Bytes.Length)),
                                          limits ?? PackageLimits.Default, problems);
        if (problems.Count > 0) throw new ModPackageException(location, problems);
        var bytes = contents.ToDictionary(file => PackagePath.TryNormalize(file.Path, out string path, out _) ? path : file.Path,
                                          file => file.Bytes, StringComparer.OrdinalIgnoreCase);
        _files = Entries.ToDictionary(entry => entry.Path, entry => (entry, bytes[entry.Path]), StringComparer.OrdinalIgnoreCase);
    }

    public PackageEntry? Find(string path) => _files.TryGetValue(path, out var file) ? file.Entry : null;

    public Stream Open(PackageEntry entry)
    {
        if (!_files.TryGetValue(entry.Path, out var file) || file.Entry.Path != entry.Path)
            throw new FileNotFoundException($"{entry.Path} is not in {Location}", entry.Path);
        return new ExactLengthStream(new MemoryStream(file.Bytes, writable: false), entry.Length, $"{Location}/{entry.Path}");
    }

    public void Dispose() { }
}

/// <summary>
/// The packages the engine ships inside its own build: the base mods of <c>mods/</c> (Lua scripting spec §9.1),
/// embedded in this assembly, so an exported build carries them. A base mod joins a stack only when an enabled mod
/// requires it; an installed package of the same id is used instead.
/// </summary>
public static class ShippedPackages
{
    private const string ResourcePrefix = "Ome.Mods.Shipped/";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<(string Path, byte[] Bytes)>>> Contents = new(Read);

    /// <summary>Where a shipped package's diagnostics say it is.</summary>
    public static string LocationOf(string folder) => $"<engine>/mods/{folder}";

    /// <summary>The folders of the shipped packages, each named by its package id.</summary>
    public static IReadOnlyList<string> Ids => Contents.Value.Keys.ToList();

    /// <summary>Opens a shipped package, or null when the engine ships none of that id.</summary>
    public static ModPackage? Open(string id)
        => Contents.Value.TryGetValue(id, out var files)
            ? ModPackage.Load(new MemoryPackageContainer(LocationOf(id), files)) : null;

    /// <summary>The layout of a profile's shipped base mod, <c>&lt;profile&gt;-base</c>, which the console's accessors read
    /// when no mod mounts it (Lua scripting spec §13); null when the engine ships none.</summary>
    public static BaseModLayout? BaseLayoutOf(string profile)
    {
        string id = $"{profile}-base";
        return Open(id)?.BaseLayout is { } layout ? new BaseModLayout(id, layout) : null;
    }

    /// <summary>
    /// The shipped packages that <paramref name="packages"/> require and do not hold, and those they in turn require,
    /// each noted in the report. An installed package with a shipped package's id is noted as used instead.
    /// </summary>
    public static IReadOnlyList<ModPackage> Required(IReadOnlyList<ModPackage> packages, ModLinkReport report)
    {
        var held = packages.Select(package => package.Id).ToHashSet(StringComparer.Ordinal);
        foreach (ModPackage installed in packages.Where(package => Contents.Value.ContainsKey(package.Id)))
            report.Add(ModReportClass.Note, installed.Id, null,
                $"{installed.Location} is used instead of the engine's own {installed.Id}");
        var added = new List<ModPackage>();
        var pending = new Queue<ModPackage>(packages);
        while (pending.Count > 0)
        {
            ModPackage package = pending.Dequeue();
            foreach (string id in package.Manifest.Requires.Select(requirement => requirement.Id))
            {
                if (held.Contains(id) || !Contents.Value.ContainsKey(id)) continue;
                held.Add(id);
                ModPackage shipped = Open(id)!;
                report.Add(ModReportClass.Note, id, null,
                    $"the engine's own {id} {shipped.Manifest.Version} is mounted: {package.Id} requires it");
                added.Add(shipped);
                pending.Enqueue(shipped);
            }
        }
        return added;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<(string Path, byte[] Bytes)>> Read()
    {
        Assembly assembly = typeof(ShippedPackages).Assembly;
        var folders = new SortedDictionary<string, List<(string, byte[])>>(StringComparer.Ordinal);
        foreach (string resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            string path = resource[ResourcePrefix.Length..].Replace('\\', '/');
            int slash = path.IndexOf('/');
            if (slash <= 0) continue;
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            string folder = path[..slash];
            if (!folders.TryGetValue(folder, out var files)) folders[folder] = files = [];
            files.Add((path[(slash + 1)..], buffer.ToArray()));
        }
        return folders.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(string, byte[])>)pair.Value, StringComparer.Ordinal);
    }
}

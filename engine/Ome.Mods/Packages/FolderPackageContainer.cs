namespace Ome.Mods.Packages;

/// <summary>
/// An unpacked package folder (spec §3.1), for development. Symbolic links and junctions are refused, so the tree
/// cannot reach outside the folder.
/// </summary>
public sealed class FolderPackageContainer : IPackageContainer
{
    private readonly string _root;
    private readonly Dictionary<string, PackageEntry> _entries;

    public string Location => _root;
    public IReadOnlyList<PackageEntry> Entries { get; }

    private FolderPackageContainer(string root, IReadOnlyList<PackageEntry> entries)
    {
        _root = root;
        Entries = entries;
        _entries = entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
    }

    public static FolderPackageContainer Open(string path, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;
        string root = Path.GetFullPath(path);
        if (!Directory.Exists(root)) throw new ModPackageException(root, "not a directory");
        var problems = new List<string>();
        var files = new List<(string, long)>();
        Walk(new DirectoryInfo(root), "", files, problems, limits.MaxEntries + 1);
        var listed = PackageListing.Validate(root, files, limits, problems);
        if (problems.Count > 0) throw new ModPackageException(root, problems);
        return new FolderPackageContainer(root, listed);
    }

    private static void Walk(DirectoryInfo directory, string prefix, List<(string, long)> files, List<string> problems,
                             int stopAfter)
    {
        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (files.Count >= stopAfter) return;
            string relative = prefix + item.Name;
            if (item.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                problems.Add($"'{relative}' is a link or junction");
                continue;
            }
            if (item is DirectoryInfo child) Walk(child, relative + "/", files, problems, stopAfter);
            else if (item is FileInfo file) files.Add((relative, file.Length));
        }
    }

    public PackageEntry? Find(string path) => _entries.GetValueOrDefault(path);

    public Stream Open(PackageEntry entry)
    {
        if (!_entries.TryGetValue(entry.Path, out PackageEntry? listed) || listed.Path != entry.Path)
            throw new FileNotFoundException($"{entry.Path} is not in {Location}", entry.Path);
        string file = Path.Combine(_root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                                    FileOptions.SequentialScan);
        return new ExactLengthStream(stream, entry.Length, $"{Location}/{entry.Path}");
    }

    public void Dispose() { }
}

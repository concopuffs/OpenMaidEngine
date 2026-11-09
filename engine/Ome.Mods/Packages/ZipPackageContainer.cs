using System.IO.Compression;

namespace Ome.Mods.Packages;

/// <summary>
/// A <c>.omp</c> package (spec §3.1): a ZIP archive whose entries are stored or deflated. The listing is checked
/// against the declared sizes, and every read is checked again against them, since the headers are untrusted.
/// </summary>
public sealed class ZipPackageContainer : IPackageContainer
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _byPath;
    private readonly Dictionary<string, PackageEntry> _entries;

    public string Location { get; }
    public IReadOnlyList<PackageEntry> Entries { get; }

    private ZipPackageContainer(string location, ZipArchive archive, IReadOnlyList<PackageEntry> entries,
                                Dictionary<string, ZipArchiveEntry> byPath)
    {
        Location = location;
        _archive = archive;
        Entries = entries;
        _byPath = byPath;
        _entries = entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
    }

    public static ZipPackageContainer Open(string path, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;
        string location = Path.GetFullPath(path);
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(location);
        }
        catch (InvalidDataException error)
        {
            throw new ModPackageException(location, $"not a readable ZIP archive: {error.Message}");
        }
        try
        {
            var problems = new List<string>();
            if (archive.Entries.Count > limits.MaxEntries)
                throw new ModPackageException(location, $"more than {limits.MaxEntries} entries");
            var files = new List<ZipArchiveEntry>();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                bool directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                if (directory)
                {
                    if (entry.Length != 0) problems.Add($"'{entry.FullName}': directory entry with data");
                    continue;
                }
                if (entry.Length > limits.RatioFloorBytes
                    && (entry.CompressedLength == 0 || entry.Length / entry.CompressedLength > limits.MaxCompressionRatio))
                {
                    problems.Add($"'{entry.FullName}' expands {entry.Length} bytes from {entry.CompressedLength}, "
                                 + $"over the {limits.MaxCompressionRatio}:1 ratio limit");
                    continue;
                }
                files.Add(entry);
            }
            var listed = PackageListing.Validate(location, files.Select(entry => (entry.FullName, entry.Length)),
                                                 limits, problems);
            if (problems.Count > 0) throw new ModPackageException(location, problems);
            var byPath = files.ToDictionary(entry => entry.FullName.Replace('\\', '/'), StringComparer.Ordinal);
            return new ZipPackageContainer(location, archive, listed, byPath);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    public PackageEntry? Find(string path) => _entries.GetValueOrDefault(path);

    public Stream Open(PackageEntry entry)
    {
        if (!_byPath.TryGetValue(entry.Path, out ZipArchiveEntry? zipEntry))
            throw new FileNotFoundException($"{entry.Path} is not in {Location}", entry.Path);
        return new ExactLengthStream(zipEntry.Open(), zipEntry.Length, $"{Location}!{entry.Path}");
    }

    public void Dispose() => _archive.Dispose();
}

namespace Age.Mods.Packages;

/// <summary>A package problem: the package it was found in and why it was rejected.</summary>
public sealed class ModPackageException : Exception
{
    public string Package { get; }
    public IReadOnlyList<string> Problems { get; }

    public ModPackageException(string package, IReadOnlyList<string> problems)
        : base($"{package}: " + string.Join("; ", problems))
    {
        Package = package;
        Problems = problems;
    }

    public ModPackageException(string package, string problem) : this(package, [problem]) { }
}

/// <summary>
/// Size bounds for untrusted packages (spec §11). The defaults are the spec's provisional limits; tests pass smaller
/// ones. The ratio check applies only to entries that expand past <see cref="RatioFloorBytes"/>, since small,
/// highly regular files compress far beyond any bomb threshold without being one.
/// </summary>
public sealed record PackageLimits(
    int MaxEntries = 65_536,
    long MaxEntryBytes = 1L << 30,
    long MaxTotalBytes = 8L << 30,
    int MaxCompressionRatio = 200,
    long RatioFloorBytes = 1L << 20)
{
    public static PackageLimits Default { get; } = new();
}

/// <summary>One file of a package: its normalized path and expanded length.</summary>
public sealed record PackageEntry(string Path, long Length);

/// <summary>
/// The read-only file tree of a package (spec §3.1): a <c>.omp</c> ZIP archive or an unpacked folder. Entries are
/// sorted by ordinal path, so both forms of one mod list the same tree.
/// </summary>
public interface IPackageContainer : IDisposable
{
    /// <summary>Where the package lives, for diagnostics.</summary>
    string Location { get; }
    IReadOnlyList<PackageEntry> Entries { get; }
    PackageEntry? Find(string path);
    /// <summary>Opens an entry. The stream fails if the payload differs from the listed length.</summary>
    Stream Open(PackageEntry entry);
}

/// <summary>Shared listing checks: path safety, case-insensitive collisions, and the entry and size limits.</summary>
internal static class PackageListing
{
    public static IReadOnlyList<PackageEntry> Validate(
        string location, IEnumerable<(string Raw, long Length)> files, PackageLimits limits, List<string> problems)
    {
        var entries = new List<PackageEntry>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (raw, length) in files)
        {
            if (entries.Count >= limits.MaxEntries)
            {
                problems.Add($"more than {limits.MaxEntries} entries");
                break;
            }
            if (!PackagePath.TryNormalize(raw, out string path, out string? reason))
            {
                problems.Add($"'{raw}': {reason}");
                continue;
            }
            if (seen.TryGetValue(path, out string? other))
            {
                problems.Add(other == path ? $"'{path}' appears twice" : $"'{path}' collides with '{other}'");
                continue;
            }
            seen.Add(path, path);
            if (length > limits.MaxEntryBytes)
            {
                problems.Add($"'{path}' expands to {length} bytes, over the {limits.MaxEntryBytes}-byte entry limit");
                continue;
            }
            total += length;
            entries.Add(new PackageEntry(path, length));
        }
        if (total > limits.MaxTotalBytes)
            problems.Add($"the package expands to {total} bytes, over the {limits.MaxTotalBytes}-byte limit");

        // A file may not share a name with a directory ("a" and "a/b"), compared case-insensitively.
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            for (int slash = entry.Path.IndexOf('/'); slash >= 0; slash = entry.Path.IndexOf('/', slash + 1))
                directories.Add(entry.Path[..slash]);
        foreach (var entry in entries.Where(entry => directories.Contains(entry.Path)))
            problems.Add($"'{entry.Path}' is both a file and a directory");

        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return entries;
    }
}

/// <summary>A read-only stream that fails when its source yields more or fewer bytes than declared.</summary>
internal sealed class ExactLengthStream : Stream
{
    private readonly Stream _inner;
    private readonly long _length;
    private readonly string _name;
    private long _position;

    public ExactLengthStream(Stream inner, long length, string name)
    {
        _inner = inner;
        _length = length;
        _name = name;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0) return 0;
        // Ask for one byte past the declared end, so an overlong payload is caught rather than truncated.
        long remaining = _length - _position;
        int request = (int)Math.Min(buffer.Length, remaining + 1);
        int read = _inner.Read(buffer[..request]);
        _position += read;
        if (_position > _length)
            throw new InvalidDataException($"{_name}: payload is longer than its declared {_length} bytes");
        if (read == 0 && _position < _length)
            throw new InvalidDataException($"{_name}: payload ends at {_position} of its declared {_length} bytes");
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}

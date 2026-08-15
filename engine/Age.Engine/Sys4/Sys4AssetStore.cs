namespace Age.Engine.Sys4;

/// <summary>Read-only byte seam after catalog resolution.</summary>
public interface IAssetStore
{
    Stream Open(AssetEntry entry);
    byte[] ReadAll(AssetEntry entry);
}

/// <summary>Reports only successful catalog opens, matching AGE's profile unlock marker timing.</summary>
public sealed class CatalogTrackingAssetStore : IAssetStore
{
    private readonly IAssetStore _inner;
    private readonly Action<AssetEntry> _opened;

    public CatalogTrackingAssetStore(IAssetStore inner, Action<AssetEntry> opened)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _opened = opened ?? throw new ArgumentNullException(nameof(opened));
    }

    public Stream Open(AssetEntry entry)
    {
        Stream stream = _inner.Open(entry);
        _opened(entry);
        return stream;
    }

    public byte[] ReadAll(AssetEntry entry)
    {
        using Stream stream = Open(entry);
        if (stream.Length > int.MaxValue)
            throw new InvalidDataException($"{entry.Name}: payload is too large");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
}

/// <summary>Native base-game precedence: exact-basename loose roots first, indexed ALF range second.</summary>
public sealed class Sys4AssetStore : IAssetStore
{
    private readonly string _archiveRoot;
    private readonly string[] _looseRoots;

    public Sys4AssetCatalog Catalog { get; }

    public Sys4AssetStore(Sys4AssetCatalog catalog, string archiveRoot, params string[] looseRoots)
    {
        Catalog = catalog;
        _archiveRoot = Path.GetFullPath(archiveRoot);
        _looseRoots = looseRoots.Select(Path.GetFullPath).ToArray();
    }

    public Stream Open(AssetEntry entry)
    {
        ValidateBasename(entry.Name, "asset");
        if (entry.IsPlaceholder) throw new FileNotFoundException("SYS4INI placeholder has no payload", entry.Name);

        foreach (string root in _looseRoots)
        {
            string candidate = Path.GetFullPath(Path.Combine(root, entry.Name));
            if (!IsDirectChild(root, candidate)) throw new InvalidDataException($"unsafe asset name: {entry.Name}");
            try
            {
                return new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                                      64 * 1024, FileOptions.SequentialScan);
            }
            // Native falls through on any INVALID_HANDLE_VALUE result from the loose CreateFileA.
            // Keep malformed payloads authoritative once open; only open failures reach the archive.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        ValidateBasename(entry.Archive, "archive");
        string archivePath = Path.GetFullPath(Path.Combine(_archiveRoot, entry.Archive));
        if (!IsDirectChild(_archiveRoot, archivePath))
            throw new InvalidDataException($"unsafe archive name: {entry.Archive}");
        var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                  64 * 1024, FileOptions.SequentialScan);
        try
        {
            if (entry.Offset < 0 || entry.Size < 0 || entry.Offset > file.Length
                || entry.Size > file.Length - entry.Offset)
                throw new InvalidDataException($"{entry.Name}: ALF range {entry.Offset}+{entry.Size} exceeds {entry.Archive} ({file.Length})");
            return new BoundedReadStream(file, entry.Offset, entry.Size);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public byte[] ReadAll(AssetEntry entry)
    {
        using Stream stream = Open(entry);
        if (stream.Length > int.MaxValue) throw new InvalidDataException($"{entry.Name}: payload is too large");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void ValidateBasename(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value)
            || value.Contains('/') || value.Contains('\\') || value is "." or "..")
            throw new InvalidDataException($"unsafe {kind} name: {value}");
    }

    private static bool IsDirectChild(string root, string child)
        => string.Equals(Path.GetDirectoryName(child)?.TrimEnd(Path.DirectorySeparatorChar),
                         root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private sealed class BoundedReadStream : Stream
    {
        private readonly FileStream _file;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public BoundedReadStream(FileStream file, long start, long length)
        {
            _file = file; _start = start; _length = length;
            _file.Position = start;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int wanted = (int)Math.Min(buffer.Length, _length - _position);
            if (wanted <= 0) return 0;
            int read = _file.Read(buffer[..wanted]);
            _position += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(_length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0 || target > _length) throw new IOException("seek outside asset range");
            _file.Position = _start + target;
            return _position = target;
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _file.Dispose(); base.Dispose(disposing); }
    }
}

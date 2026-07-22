using System.Buffers.Binary;
using System.Text;

namespace Age.Engine.Sys4;

/// <summary>One raw SYS4INI file record. Placeholder records remain addressable by
/// <see cref="RawIndex"/> but are excluded from scene and name views.</summary>
public sealed record AssetEntry(
    string Name,
    string Archive,
    long Offset,
    long Size,
    int RawIndex = -1,
    int ArchiveId = -1,
    int FileNumber = -1,
    bool IsPlaceholder = false,
    int PackId = 0)
{
    /// <summary>The exact packed SYS4INI/AAI id AGE uses to address this record.</summary>
    public int PackedId => checked((PackId << 24) | RawIndex);
}

/// <summary>A real catalog entry paired with the packed resource id AGE uses at runtime.</summary>
public sealed record PackedAssetEntry(long PackedId, AssetEntry Asset);

/// <summary>Runtime parser and lookup views for a base S4IC SYS4INI catalog and its S4AC append mounts.</summary>
public sealed class Sys4AssetCatalog
{
    private const int ArchiveNameSize = 256;
    private const int RecordSize = 80;

    private readonly Dictionary<string, AssetEntry> _byName;
    private readonly Dictionary<string, (int Start, int End)> _sceneRanges;
    private readonly Dictionary<int, Sys4AssetCatalog> _appendPacks = new();

    public string Magic { get; }
    public string Title { get; }
    public int PackId { get; }
    public IReadOnlyList<string> Archives { get; }
    public IReadOnlyList<AssetEntry> RawSlots { get; }
    public IReadOnlyList<AssetEntry> Files { get; }
    public IReadOnlyDictionary<int, Sys4AssetCatalog> AppendPacks => _appendPacks;

    private Sys4AssetCatalog(string magic, string title, int packId,
                             List<string> archives, List<AssetEntry> rawSlots)
    {
        Magic = magic;
        Title = title;
        PackId = packId;
        Archives = archives;
        RawSlots = rawSlots;
        Files = rawSlots.Where(r => !r.IsPlaceholder).ToArray();
        _byName = Files.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        _sceneRanges = BuildSceneRanges(Files);
    }

    public static Sys4AssetCatalog Load(string path)
    {
        var catalog = Parse(File.ReadAllBytes(path), Path.GetFileName(path));
        if (!catalog.Magic.StartsWith("S4IC", StringComparison.Ordinal)) return catalog;

        string? root = Path.GetDirectoryName(Path.GetFullPath(path));
        if (root == null) return catalog;
        foreach (string appendPath in Directory.EnumerateFiles(root, "*.AAI"))
            catalog.MountAppend(Parse(File.ReadAllBytes(appendPath), Path.GetFileName(appendPath)));
        return catalog;
    }

    public static Sys4AssetCatalog Parse(byte[] data, string name = "SYS4INI.BIN")
    {
        bool isBase = data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual("S4IC"u8);
        bool isAppend = data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual("S4AC"u8);
        if (!isBase && !isAppend)
            throw new InvalidDataException($"{name}: expected an S4IC or S4AC catalog");

        int expandedSizeOffset = isAppend ? 0x110 : 0x12c;
        int packedSizeOffset = isAppend ? 0x114 : 0x134;
        if (data.Length < packedSizeOffset + 4)
            throw new InvalidDataException($"{name}: catalog header is truncated");

        uint expandedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(expandedSizeOffset, 4));
        uint packedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(packedSizeOffset, 4));
        if (packedSize > data.Length - (packedSizeOffset + 4))
            throw new InvalidDataException($"{name}: packed directory is truncated");
        if (expandedSize == 0 || expandedSize > int.MaxValue)
            throw new InvalidDataException($"{name}: invalid expanded size {expandedSize}");

        var blob = LzssDecoder.Decode(data.AsSpan(packedSizeOffset + 4, checked((int)packedSize)),
                                      checked((int)expandedSize), name);
        int p = 0;
        uint ReadU32()
        {
            if (p > blob.Length - 4) throw new InvalidDataException($"{name}: directory is truncated");
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(p, 4));
            p += 4;
            return value;
        }

        uint archiveCount = ReadU32();
        if (archiveCount is 0 or >= 0x1000 || archiveCount > (blob.Length - p) / ArchiveNameSize)
            throw new InvalidDataException($"{name}: invalid archive count {archiveCount}");
        var archives = new List<string>(checked((int)archiveCount));
        for (int i = 0; i < archiveCount; i++, p += ArchiveNameSize)
            archives.Add(ReadCString(blob.AsSpan(p, ArchiveNameSize)));

        uint fileCount = ReadU32();
        if (fileCount is 0 or >= 0x400000 || fileCount > (blob.Length - p) / RecordSize)
            throw new InvalidDataException($"{name}: invalid file count {fileCount}");
        int packId = isAppend
            ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x108, 4))) : 0;
        // AGE uses SAR 24 before indexing its mount table, so selectors with the sign bit set do
        // not address slots 0x80..0xff. Reject them instead of inventing unsigned behavior.
        if (isAppend && packId is not (> 0 and < 0x80))
            throw new InvalidDataException($"{name}: invalid append pack selector {packId}");

        var slots = new List<AssetEntry>(checked((int)fileCount));
        for (int i = 0; i < fileCount; i++, p += RecordSize)
        {
            var row = blob.AsSpan(p, RecordSize);
            string fileName = ReadCString(row[..64]);
            int archiveId = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(64, 4)));
            int fileNumber = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(68, 4)));
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(72, 4));
            long size = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(76, 4));
            string archive = archiveId >= 0 && archiveId < archives.Count ? archives[archiveId] : "";
            bool placeholder = fileName is "" or "@";
            slots.Add(new AssetEntry(fileName, archive, offset, size, i, archiveId, fileNumber,
                                     placeholder, packId));
        }

        string magic = ReadCString(data.AsSpan(0, Math.Min(8, data.Length)));
        string title = ReadCString(data.AsSpan(8, Math.Min(256, data.Length - 8)));
        return new Sys4AssetCatalog(magic, title, packId, archives, slots);
    }

    /// <summary>Mount an append catalog by its native header selector. A later mount of the same
    /// selector replaces the earlier one, matching AGE's FindNextFile/store loop.</summary>
    public void MountAppend(Sys4AssetCatalog append)
    {
        if (PackId != 0) throw new InvalidOperationException("append catalogs cannot own append mounts");
        if (!append.Magic.StartsWith("S4AC", StringComparison.Ordinal) || append.PackId == 0)
            throw new InvalidDataException("mounted catalog is not an S4AC append pack");
        if (!string.Equals(Title, append.Title, StringComparison.Ordinal))
            throw new InvalidDataException($"append title mismatch: {append.Title}");
        _appendPacks[append.PackId] = append;
    }

    /// <summary>Universal raw-id lookup. Placeholder slots are returned, not collapsed.</summary>
    public AssetEntry? ResolveRaw(long rawId)
        => rawId >= 0 && rawId < RawSlots.Count ? RawSlots[(int)rawId] : null;

    /// <summary>Native packed-id lookup: zero high byte selects SYS4INI; otherwise the high byte
    /// selects an S4AC mount and the low 24 bits index that pack.</summary>
    public AssetEntry? ResolvePacked(long id)
    {
        if (id < 0 || id > uint.MaxValue) return null;
        int pack = (int)((id >> 24) & 0xff);
        if (pack >= 0x80) return null;
        long index = id & 0xffffff;
        return pack == 0 ? ResolveRaw(index)
            : _appendPacks.TryGetValue(pack, out var append) ? append.ResolveRaw(index) : null;
    }

    /// <summary>Case-insensitive exact-name lookup over real records.</summary>
    public AssetEntry? ResolveName(string name)
        => _byName.TryGetValue(Path.GetFileName(name), out var entry) && Path.GetFileName(name) == name
            ? entry : null;

    /// <summary>Resolve within the owning scene section; ids cannot spill into the next section.</summary>
    public AssetEntry? ResolveScene(string scene, long localId)
    {
        string key = Path.GetFileNameWithoutExtension(scene);
        if (!_sceneRanges.TryGetValue(key, out var range)) return null;
        long pos = range.Start + localId;
        return localId >= 0 && pos <= range.End ? Files[(int)pos] : null;
    }

    public IReadOnlyList<string> ScriptNames => Files
        .Where(f => f.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
        .Select(f => f.Name.ToUpperInvariant()).ToArray();

    /// <summary>Enumerate every script in native packed-id order, including mounted append packs.
    /// Placeholder slots and non-script assets are excluded without collapsing raw indices.</summary>
    public IReadOnlyList<PackedAssetEntry> EnumerateScripts()
    {
        var scripts = new List<PackedAssetEntry>();
        AddScripts(this, scripts);
        foreach (var append in _appendPacks.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            AddScripts(append, scripts);
        return scripts;
    }

    private static void AddScripts(Sys4AssetCatalog catalog, List<PackedAssetEntry> scripts)
    {
        long selector = (long)catalog.PackId << 24;
        foreach (var entry in catalog.Files)
            if (entry.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
                scripts.Add(new PackedAssetEntry(selector | (uint)entry.RawIndex, entry));
    }

    private static Dictionary<string, (int Start, int End)> BuildSceneRanges(IReadOnlyList<AssetEntry> files)
    {
        var ranges = new Dictionary<string, (int Start, int End)>(StringComparer.OrdinalIgnoreCase);
        int start = 0;
        for (int i = 1; i <= files.Count; i++)
        {
            bool end = i == files.Count || files[i].FileNumber <= files[i - 1].FileNumber;
            if (!end) continue;
            for (int k = start; k < i; k++)
                if (files[k].Name.Length == 10 && files[k].Name.StartsWith("SC", StringComparison.OrdinalIgnoreCase)
                    && files[k].Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
                    && files[k].Name.AsSpan(2, 4).ToString().All(char.IsDigit))
                {
                    ranges[Path.GetFileNameWithoutExtension(files[k].Name)] = (start, i - 1);
                    break;
                }
            start = i;
        }
        return ranges;
    }

    private static string ReadCString(ReadOnlySpan<byte> bytes)
    {
        int zero = bytes.IndexOf((byte)0);
        if (zero >= 0) bytes = bytes[..zero];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932).GetString(bytes);
    }

}

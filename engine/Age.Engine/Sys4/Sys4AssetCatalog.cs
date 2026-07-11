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
    bool IsPlaceholder = false);

/// <summary>Runtime parser and lookup views for a base S4IC SYS4INI catalog.</summary>
public sealed class Sys4AssetCatalog
{
    private const int PackedSizeOffset = 0x134;
    private const int ExpandedSizeOffset = 0x12c;
    private const int ArchiveNameSize = 256;
    private const int RecordSize = 80;

    private readonly Dictionary<string, AssetEntry> _byName;
    private readonly Dictionary<string, (int Start, int End)> _sceneRanges;

    public string Magic { get; }
    public IReadOnlyList<string> Archives { get; }
    public IReadOnlyList<AssetEntry> RawSlots { get; }
    public IReadOnlyList<AssetEntry> Files { get; }

    private Sys4AssetCatalog(string magic, List<string> archives, List<AssetEntry> rawSlots)
    {
        Magic = magic;
        Archives = archives;
        RawSlots = rawSlots;
        Files = rawSlots.Where(r => !r.IsPlaceholder).ToArray();
        _byName = Files.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        _sceneRanges = BuildSceneRanges(Files);
    }

    public static Sys4AssetCatalog Load(string path) => Parse(File.ReadAllBytes(path), Path.GetFileName(path));

    public static Sys4AssetCatalog Parse(byte[] data, string name = "SYS4INI.BIN")
    {
        if (data.Length < PackedSizeOffset + 4 || !data.AsSpan(0, 4).SequenceEqual("S4IC"u8))
            throw new InvalidDataException($"{name}: expected an S4IC catalog");

        uint expandedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(ExpandedSizeOffset, 4));
        uint packedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(PackedSizeOffset, 4));
        if (packedSize > data.Length - (PackedSizeOffset + 4))
            throw new InvalidDataException($"{name}: packed directory is truncated");
        if (expandedSize == 0 || expandedSize > int.MaxValue)
            throw new InvalidDataException($"{name}: invalid expanded size {expandedSize}");

        var blob = DecompressLzss(data.AsSpan(PackedSizeOffset + 4, checked((int)packedSize)),
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
            slots.Add(new AssetEntry(fileName, archive, offset, size, i, archiveId, fileNumber, placeholder));
        }

        string magic = ReadCString(data.AsSpan(0, Math.Min(8, data.Length)));
        return new Sys4AssetCatalog(magic, archives, slots);
    }

    /// <summary>Universal raw-id lookup. Placeholder slots are returned, not collapsed.</summary>
    public AssetEntry? ResolveRaw(long rawId)
        => rawId >= 0 && rawId < RawSlots.Count ? RawSlots[(int)rawId] : null;

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

    private static byte[] DecompressLzss(ReadOnlySpan<byte> source, int expectedSize, string name)
    {
        var frame = new byte[0x1000];
        int framePos = 0xfee, input = 0, output = 0;
        var result = new byte[expectedSize];
        while (output < expectedSize)
        {
            if (input >= source.Length) throw new InvalidDataException($"{name}: LZSS stream ended early");
            int control = source[input++];
            for (int bit = 1; bit <= 0x80 && output < expectedSize; bit <<= 1)
            {
                if ((control & bit) != 0)
                {
                    if (input >= source.Length) throw new InvalidDataException($"{name}: truncated LZSS literal");
                    byte value = source[input++];
                    result[output++] = value;
                    frame[framePos] = value;
                    framePos = (framePos + 1) & 0xfff;
                }
                else
                {
                    if (input > source.Length - 2) throw new InvalidDataException($"{name}: truncated LZSS back-reference");
                    int lo = source[input++], hi = source[input++];
                    int readPos = ((hi & 0xf0) << 4) | lo;
                    int length = 3 + (hi & 0x0f);
                    for (int j = 0; j < length && output < expectedSize; j++)
                    {
                        byte value = frame[readPos++ & 0xfff];
                        result[output++] = value;
                        frame[framePos] = value;
                        framePos = (framePos + 1) & 0xfff;
                    }
                }
            }
        }
        return result;
    }
}

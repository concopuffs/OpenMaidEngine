using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace Age.Engine.Persistence;

/// <summary>One native RT.DAT script record and its per-message read flags.</summary>
public sealed record ReadTextScriptRecord(uint ScriptId, IReadOnlyList<uint> Flags);

/// <summary>
/// Logical contents of AGE's shared RT.DAT. Records are keyed by the raw packed SYS4/AAI script id.
/// </summary>
public sealed class ReadTextDatabaseSnapshot
{
    public IReadOnlyDictionary<uint, IReadOnlyList<uint>> Records { get; }

    public ReadTextDatabaseSnapshot(IEnumerable<ReadTextScriptRecord>? records = null)
    {
        var values = new Dictionary<uint, IReadOnlyList<uint>>();
        foreach (ReadTextScriptRecord record in records ?? Array.Empty<ReadTextScriptRecord>())
        {
            if (!values.TryAdd(record.ScriptId, Array.AsReadOnly(record.Flags.ToArray())))
                throw new ArgumentException(
                    $"ReadTextDB repeats script id 0x{record.ScriptId:x8}.", nameof(records));
        }
        Records = new ReadOnlyDictionary<uint, IReadOnlyList<uint>>(values);
    }
}

/// <summary>
/// Codec for AGE's native S3RT file: a fixed 0x114-byte identity header, all 12-byte script
/// records, then each record's DWORD flag array. The record's third word is an ignored serialized
/// process pointer; portable writers emit zero and native AGE replaces it while loading.
/// </summary>
public static class ReadTextDatabaseCodec
{
    public const int HeaderSize = 0x114;
    public const int RecordSize = 0x0c;
    public const int VersionMajor = 1;
    public const int VersionMinor = 0;
    private const uint Magic = 0x54523353; // S3RT
    private static readonly Encoding ShiftJis = CreateShiftJis();

    public static byte[] Encode(ReadTextDatabaseSnapshot snapshot, NativeSaveIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(identity);

        KeyValuePair<uint, IReadOnlyList<uint>>[] records =
            snapshot.Records.OrderBy(record => record.Key).ToArray();
        long length = HeaderSize + checked((long)records.Length * RecordSize);
        foreach (var record in records)
            length = checked(length + (long)record.Value.Count * sizeof(uint));
        if (length > int.MaxValue)
            throw new InvalidDataException("RT.DAT is too large.");

        byte[] result = new byte[(int)length];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), identity.CompatibilityId);
        WriteGameId(result.AsSpan(8, 0x100), identity.GameId);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x108, 4), VersionMajor);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x10c, 4), VersionMinor);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x110, 4), checked((uint)records.Length));

        int position = HeaderSize;
        foreach (var record in records)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(position, 4), record.Key);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(position + 4, 4), checked((uint)record.Value.Count));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(position + 8, 4), 0);
            position += RecordSize;
        }
        foreach (var record in records)
            foreach (uint flag in record.Value)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(position, 4), flag);
                position += sizeof(uint);
            }
        return result;
    }

    public static ReadTextDatabaseSnapshot Decode(
        ReadOnlySpan<byte> source,
        NativeSaveIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (source.Length < HeaderSize)
            throw new InvalidDataException("RT.DAT header is truncated.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(source) != Magic)
            throw new InvalidDataException("RT.DAT magic is not S3RT.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) != identity.CompatibilityId)
            throw new InvalidDataException("RT.DAT compatibility id mismatch.");
        if (!StringComparer.Ordinal.Equals(ReadGameId(source.Slice(8, 0x100)), identity.GameId))
            throw new InvalidDataException("RT.DAT game id mismatch.");
        uint major = BinaryPrimitives.ReadUInt32LittleEndian(source[0x108..]);
        uint minor = BinaryPrimitives.ReadUInt32LittleEndian(source[0x10c..]);
        if (major != VersionMajor || minor != VersionMinor)
            throw new InvalidDataException(
                $"RT.DAT version mismatch: expected {VersionMajor}.{VersionMinor}, got {major}.{minor}.");

        uint rawCount = BinaryPrimitives.ReadUInt32LittleEndian(source[0x110..]);
        if (rawCount > int.MaxValue || rawCount > (uint)((source.Length - HeaderSize) / RecordSize))
            throw new InvalidDataException("RT.DAT script-record count is too large.");
        int count = (int)rawCount;
        int flagsPosition = checked(HeaderSize + count * RecordSize);
        var records = new List<ReadTextScriptRecord>(count);
        var seen = new HashSet<uint>();
        for (int i = 0; i < count; i++)
        {
            int recordPosition = HeaderSize + i * RecordSize;
            uint scriptId = BinaryPrimitives.ReadUInt32LittleEndian(source[recordPosition..]);
            uint rawMessageCount =
                BinaryPrimitives.ReadUInt32LittleEndian(source[(recordPosition + 4)..]);
            if (!seen.Add(scriptId))
                throw new InvalidDataException($"RT.DAT repeats script id 0x{scriptId:x8}.");
            if (rawMessageCount > int.MaxValue
                || rawMessageCount > (uint)((source.Length - flagsPosition) / sizeof(uint)))
                throw new InvalidDataException(
                    $"RT.DAT script 0x{scriptId:x8} flag array is truncated.");

            int messageCount = (int)rawMessageCount;
            var flags = new uint[messageCount];
            for (int message = 0; message < messageCount; message++)
            {
                flags[message] = BinaryPrimitives.ReadUInt32LittleEndian(source[flagsPosition..]);
                flagsPosition += sizeof(uint);
            }
            records.Add(new ReadTextScriptRecord(scriptId, flags));
        }
        if (flagsPosition != source.Length)
            throw new InvalidDataException(
                $"RT.DAT has {source.Length - flagsPosition} unexpected trailing byte(s).");
        return new ReadTextDatabaseSnapshot(records);
    }

    private static void WriteGameId(Span<byte> destination, string gameId)
    {
        destination.Clear();
        byte[] encoded = ShiftJis.GetBytes(TruncateAtNul(gameId));
        if (encoded.Length >= destination.Length)
            throw new InvalidDataException("RT.DAT game id does not fit its 256-byte field.");
        encoded.CopyTo(destination);
    }

    private static string ReadGameId(ReadOnlySpan<byte> source)
    {
        int terminator = source.IndexOf((byte)0);
        if (terminator < 0)
            throw new InvalidDataException("RT.DAT game id is not NUL-terminated.");
        try
        {
            return ShiftJis.GetString(source[..terminator]);
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("RT.DAT game id is not valid CP932.", error);
        }
    }

    private static string TruncateAtNul(string value)
    {
        int nul = value.IndexOf('\0');
        return nul < 0 ? value : value[..nul];
    }

    private static Encoding CreateShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}

/// <summary>
/// Profile-lifetime ReadTextDB with AGE's queue-at-advance and commit-at-op-0x71 lifecycle.
/// </summary>
public sealed class ReadTextDatabase
{
    private readonly Dictionary<uint, uint[]> _records = new();
    private readonly List<(uint ScriptId, int MessageIndex, int MessageCount)> _pending = new();

    public int RecordCount => _records.Count;
    public int PendingCount => _pending.Count;

    public bool IsMessageRead(uint scriptId, int messageIndex)
        => messageIndex >= 0
           && _records.TryGetValue(scriptId, out uint[]? flags)
           && messageIndex < flags.Length
           && flags[messageIndex] != 0;

    public void QueueMessage(uint scriptId, int messageIndex, int messageCount)
        => _pending.Add((scriptId, messageIndex, messageCount));

    public void CommitPending()
    {
        foreach (var pending in _pending)
        {
            if (pending.MessageIndex < 0
                || pending.MessageCount <= 0
                || pending.MessageIndex >= pending.MessageCount)
                continue;

            if (!_records.TryGetValue(pending.ScriptId, out uint[]? flags))
            {
                flags = new uint[pending.MessageCount];
                _records.Add(pending.ScriptId, flags);
            }
            else if (pending.MessageIndex >= flags.Length)
            {
                var grown = new uint[pending.MessageCount];
                Array.Copy(flags, grown, Math.Min(flags.Length, grown.Length));
                flags = grown;
                _records[pending.ScriptId] = flags;
            }
            flags[pending.MessageIndex] = 1;
        }
        _pending.Clear();
    }

    public ReadTextDatabaseSnapshot Snapshot()
        => new(_records.Select(record =>
            new ReadTextScriptRecord(record.Key, Array.AsReadOnly(record.Value.ToArray()))));

    public void Replace(ReadTextDatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _records.Clear();
        foreach (var record in snapshot.Records)
            _records.Add(record.Key, record.Value.ToArray());
        _pending.Clear();
    }

    public bool Load(INativeDatStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        ReadTextDatabaseSnapshot? snapshot = store.LoadReadText();
        if (snapshot is null)
        {
            Clear();
            return false;
        }
        Replace(snapshot);
        return true;
    }

    public void Save(INativeDatStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.SaveReadText(Snapshot());
    }

    public void Clear()
    {
        _records.Clear();
        _pending.Clear();
    }
}

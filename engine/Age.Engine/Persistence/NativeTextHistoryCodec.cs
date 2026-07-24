using System.Buffers.Binary;
using System.Text;
using Age.Engine.Model;
using Age.Engine.Sys4;

namespace Age.Engine.Persistence;

/// <summary>The non-container LZSS tail appended to a layout-3 numbered save.</summary>
public static class NativeTextHistoryCodec
{
    private const int HeaderSize = 12;
    private const int RecordDwords = 11;
    private static readonly Encoding NativeEncoding = CreateNativeEncoding();

    public static byte[] Encode(AdvTextHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        byte[] logical = EncodeLogical(history);
        byte[] stored = LzssEncoder.EncodeOrVerbatim(logical);
        byte[] result = new byte[HeaderSize + stored.Length];
        WriteInt(result, 0, logical.Length);
        WriteInt(result, 4, logical.Length);
        WriteInt(result, 8, stored.Length);
        stored.CopyTo(result, HeaderSize);
        return result;
    }

    public static void DecodeInto(ReadOnlySpan<byte> source, AdvTextHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (source.Length < HeaderSize) throw new InvalidDataException("Native text-history tail is truncated.");
        int logicalLength = ReadNonNegative(source, 0, "logical length");
        int duplicateLength = ReadNonNegative(source, 4, "duplicate logical length");
        int storedLength = ReadNonNegative(source, 8, "stored length");
        if (duplicateLength != logicalLength)
            throw new InvalidDataException("Native text-history length fields disagree.");
        if (source.Length < HeaderSize + storedLength)
            throw new InvalidDataException("Native text-history compressed stream is truncated.");
        ReadOnlySpan<byte> stored = source.Slice(HeaderSize, storedLength);
        byte[] logical = storedLength == logicalLength
            ? stored.ToArray()
            : LzssDecoder.Decode(stored, logicalLength, "native text history");
        DecodeLogical(logical, history);
    }

    private static byte[] EncodeLogical(AdvTextHistory history)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(history.Entries.Count);
        foreach (AdvTextHistoryEntry entry in history.Entries)
        {
            writer.Write(entry.LayoutSlot);
            writer.Write(entry.FirstRecordIndex);
        }
        writer.Write(history.Records.Count);
        foreach (AdvTextHistoryRecord record in history.Records)
        {
            writer.Write(record.Layout.Slot);
            writer.Write(record.Layout.OriginX);
            writer.Write(record.Layout.OriginY);
            writer.Write(record.Layout.Width);
            writer.Write(record.Layout.Height);
            writer.Write(unchecked((int)record.Value));
            writer.Write(unchecked((int)record.AuxValue));
            writer.Write(record.Style.PrimaryFontSize);
            writer.Write(unchecked((int)record.Style.TextColor));
            writer.Write(record.Layout.CursorY);
            writer.Write(unchecked((int)record.Flags));
        }

        using var strings = new MemoryStream();
        foreach (AdvTextHistoryRecord record in history.Records)
        {
            strings.Write(NativeEncoding.GetBytes(record.Text ?? ""));
            strings.WriteByte(0);
        }
        while ((strings.Length & 3) != 0) strings.WriteByte(0);
        byte[] blob = strings.ToArray();
        writer.Write(blob.Length / 4);
        for (int i = 0; i < blob.Length; i += 4)
            writer.Write(~BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(i, 4)));
        return stream.ToArray();
    }

    private static void DecodeLogical(ReadOnlySpan<byte> source, AdvTextHistory history)
    {
        int at = 0;
        int entryCount = ReadCount(source, ref at, "entry");
        var entries = new AdvTextHistoryEntry[entryCount];
        for (int i = 0; i < entryCount; i++)
            entries[i] = new AdvTextHistoryEntry(ReadNext(source, ref at), ReadNext(source, ref at));

        int recordCount = ReadCount(source, ref at, "record");
        var raw = new int[recordCount, RecordDwords];
        for (int i = 0; i < recordCount; i++)
            for (int dword = 0; dword < RecordDwords; dword++)
                raw[i, dword] = ReadNext(source, ref at);

        int stringDwords = ReadCount(source, ref at, "string blob dword");
        int stringBytes = checked(stringDwords * 4);
        if (at > source.Length - stringBytes)
            throw new InvalidDataException("Native text-history string blob is truncated.");
        byte[] blob = source.Slice(at, stringBytes).ToArray();
        for (int i = 0; i < blob.Length; i += 4)
        {
            uint value = ~BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(i, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(i, 4), value);
        }
        string[] texts = DecodeStrings(blob, recordCount);
        var records = new AdvTextHistoryRecord[recordCount];
        for (int i = 0; i < recordCount; i++)
        {
            var flags = unchecked((AdvTextHistoryRecordFlags)(uint)raw[i, 10]);
            AdvTextHistoryRecordKind kind =
                flags.HasFlag(AdvTextHistoryRecordFlags.VoicePair) ? AdvTextHistoryRecordKind.Voice :
                flags.HasFlag(AdvTextHistoryRecordFlags.TypedMetadata) ? AdvTextHistoryRecordKind.Metadata :
                AdvTextHistoryRecordKind.Text;
            var layout = new AdvTextLayoutSnapshot(
                raw[i, 0], raw[i, 3], raw[i, 4], raw[i, 1], raw[i, 2],
                0, raw[i, 9], raw[i, 3], raw[i, 4]);
            var style = AdvTextStyle.Default with
            {
                PrimaryFontSize = raw[i, 7],
                TextColor = unchecked((uint)raw[i, 8]),
            };
            records[i] = new AdvTextHistoryRecord(
                kind, flags, layout, style, texts[i], raw[i, 5], raw[i, 6], -1);
        }
        history.RestorePersistenceSnapshot(entries, records);
    }

    private static string[] DecodeStrings(ReadOnlySpan<byte> blob, int count)
    {
        var result = new string[count];
        int at = 0;
        for (int i = 0; i < count; i++)
        {
            if (at >= blob.Length)
                throw new InvalidDataException("Native text-history string blob ended early.");
            int end = blob[at..].IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException("Native text-history string blob ended early.");
            result[i] = NativeEncoding.GetString(blob.Slice(at, end));
            at += end + 1;
        }
        return result;
    }

    private static int ReadCount(ReadOnlySpan<byte> source, ref int at, string name)
    {
        int value = ReadNext(source, ref at);
        if (value < 0) throw new InvalidDataException($"Native text-history {name} count is negative.");
        return value;
    }

    private static int ReadNext(ReadOnlySpan<byte> source, ref int at)
    {
        if (at > source.Length - 4) throw new InvalidDataException("Native text-history logical data is truncated.");
        int value = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(at, 4));
        at += 4;
        return value;
    }

    private static int ReadNonNegative(ReadOnlySpan<byte> source, int offset, string name)
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
        if (value < 0) throw new InvalidDataException($"Native text-history {name} is negative.");
        return value;
    }

    private static void WriteInt(Span<byte> destination, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), value);

    private static Encoding CreateNativeEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}

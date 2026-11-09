using System.Buffers.Binary;
using System.Text;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Persistence;

/// <summary>
/// The non-container LZSS tail appended to a layout-3 numbered save. Each record is the first 11 dwords of
/// AGE's 0x48-byte retained record (text_history_serialize@0x451d00): slot, rect left/top/right/bottom,
/// then for text the BGR primary and effect colors (text manager +0x54c/+0x550), text manager +0x554,
/// render mode (+0x558) and primary pixel height (text_history_append_text_record@0x456000); metadata and
/// voice records carry only value/aux at dwords 5/6. Dword 10 is the flags.
/// </summary>
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
            writer.Write(record.Rect.Left);
            writer.Write(record.Rect.Top);
            writer.Write(record.Rect.Right);
            writer.Write(record.Rect.Bottom);
            if (record.Kind == AdvTextHistoryRecordKind.Text)
            {
                bool lineBreak = record.Flags.HasFlag(AdvTextHistoryRecordFlags.LineBreak);
                writer.Write(SwapRedBlue(record.Style.TextColor));
                writer.Write(SwapRedBlue(record.Style.EffectColor));
                writer.Write(record.TextManager554);
                writer.Write(record.Style.RenderMode);
                writer.Write(lineBreak ? 0 : PrimaryPixelHeight(record.Style));
            }
            else
            {
                writer.Write(unchecked((int)record.Value));
                writer.Write(unchecked((int)record.AuxValue));
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
            }
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
            var layout = new AdvTextLayoutSnapshot(raw[i, 0], 0, 0, 0, 0, 0, 0, 0, 0);
            var rect = new AdvTextHistoryRect(raw[i, 1], raw[i, 2], raw[i, 3], raw[i, 4]);
            bool text = kind == AdvTextHistoryRecordKind.Text;
            var style = text
                ? AdvTextStyle.Default with
                {
                    TextColor = SwapRedBlue(raw[i, 5]),
                    EffectColor = SwapRedBlue(raw[i, 6]),
                    RenderMode = raw[i, 8],
                    PrimaryFontSize = raw[i, 9],
                }
                : AdvTextStyle.Default;
            records[i] = new AdvTextHistoryRecord(
                kind, flags, layout, style, texts[i], text ? 0 : raw[i, 5], text ? 0 : raw[i, 6], -1)
            {
                Rect = rect,
                TextManager554 = text ? raw[i, 7] : 0,
            };
        }
        history.RestorePersistenceSnapshot(entries, records);
    }

    // Script colors are RGB; the text manager keeps GDI COLORREFs (BGR), as ops 0x76/0x77 convert them.
    private static int SwapRedBlue(long color)
    {
        uint value = unchecked((uint)color);
        return unchecked((int)((value & 0xff) << 16 | value & 0xff00 | (value >> 16) & 0xff));
    }

    // text_history_append_text_record's size argument is -LOGFONT.lfHeight (text manager +0x4cc).
    private static int PrimaryPixelHeight(AdvTextStyle style)
        => Ome.Engine.Text.ImmediateSurfaceTextRenderer.NativePixelHeight(style.PrimaryFontSize);

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

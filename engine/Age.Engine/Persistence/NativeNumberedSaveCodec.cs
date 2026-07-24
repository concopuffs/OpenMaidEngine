using System.Buffers.Binary;
using System.Text;

namespace Age.Engine.Persistence;

public sealed record NativeSavedScriptFrame(
    int ParentContext,
    uint ScriptId,
    IReadOnlyList<int> ReturnIndices,
    int ResumeIndex,
    int CallTargetIndex);

public sealed record NativeSavedGfxObject(long Handle, byte[] Record);

public sealed record NativeNumberedSaveState(
    int SavedFrameOwner,
    int BgmTrackId,
    IReadOnlyList<int> SoundEffectResourceIds,
    byte[] ResourceRecords,
    byte[] SurfaceRecords,
    IReadOnlyList<NativeSavedScriptFrame> Frames,
    IReadOnlyList<int> IntegerGlobals,
    IReadOnlyList<int> FloatGlobals,
    IReadOnlyList<string> StringGlobals,
    IReadOnlyList<int> PointerGlobals,
    IReadOnlyList<int> PointerStrings,
    IReadOnlyList<int> LocalPointerScratch,
    IReadOnlyList<NativeSavedGfxObject> GfxObjects,
    long RangeTransformFirst,
    int RangeTransformCount,
    byte[] RangeTransformRecord)
{
    public const int SoundEffectChannelCount = 10;
    public const int ResourceRecordsSize = 300 * 4;
    public const int SurfaceRecordsSize = 20_000;
    public const int GfxRecordSize = 0x2d4;
}

/// <summary>AGE SaveVersion1=3 numbered-save logical payload.</summary>
public static class NativeNumberedSaveCodec
{
    private const int FixedPrefixSize = 0x5304;
    private const int FrameSize = 0x414;
    private const int FixedSuffixSize = 0x414;
    private const int FrameReturnCapacity = 256;
    private const int GfxAllocationDwords = 0x2d8;
    private const int GfxConstantDwords = 0x2e1;
    private static readonly Encoding NativeEncoding = CreateNativeEncoding();

    public static byte[] Encode(NativeNumberedSaveState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);

        int cutoff = state.Frames.Count - 1;
        byte[] strings = EncodeStrings(state.StringGlobals);
        int stringDwords = strings.Length / 4;
        int bankDwords = checked(
            state.IntegerGlobals.Count + state.FloatGlobals.Count + state.StringGlobals.Count
            + state.PointerGlobals.Count + state.PointerStrings.Count + state.LocalPointerScratch.Count);
        int totalDwords = checked(
            cutoff * 0x105 + 0x53ea + bankDwords + stringDwords
            + GfxConstantDwords + state.GfxObjects.Count * GfxAllocationDwords);
        byte[] payload = new byte[checked((totalDwords - 2) * 4)];

        WriteInt(payload, 0, cutoff);
        WriteInt(payload, 4, state.SavedFrameOwner);
        WriteInt(payload, 8, state.BgmTrackId);
        WriteIntList(payload, 0x0c, state.SoundEffectResourceIds);
        state.ResourceRecords.CopyTo(payload, 0x34);
        state.SurfaceRecords.CopyTo(payload, 0x4e4);

        for (int i = 0; i < state.Frames.Count; i++)
            WriteFrame(payload, FixedPrefixSize + i * FrameSize, state.Frames[i], i == cutoff);

        int at = checked(0x5718 + cutoff * FrameSize);
        int[] counts =
        [
            state.IntegerGlobals.Count, state.FloatGlobals.Count, state.StringGlobals.Count,
            state.PointerGlobals.Count, state.PointerStrings.Count, state.LocalPointerScratch.Count,
        ];
        WriteIntList(payload, at, counts);
        at += 24;
        WriteIntList(payload, at, state.IntegerGlobals);
        at += state.IntegerGlobals.Count * 4;
        WriteIntList(payload, at, state.FloatGlobals);
        at += state.FloatGlobals.Count * 4;
        WriteInt(payload, at, stringDwords);
        at += 4;
        strings.CopyTo(payload, at);
        at += strings.Length;
        WriteIntList(payload, at, state.PointerGlobals);
        at += state.PointerGlobals.Count * 4;
        WriteIntList(payload, at, state.PointerStrings);
        at += state.PointerStrings.Count * 4;
        WriteIntList(payload, at, state.LocalPointerScratch);
        at += state.LocalPointerScratch.Count * 4;

        WriteInt(payload, at, NativeNumberedSaveState.GfxRecordSize);
        WriteInt(payload, at + 4, state.GfxObjects.Count);
        at += 8;
        foreach (NativeSavedGfxObject gfx in state.GfxObjects)
        {
            WriteInt(payload, at, unchecked((int)gfx.Handle));
            gfx.Record.CopyTo(payload, at + 4);
            at += 4 + NativeNumberedSaveState.GfxRecordSize;
        }
        WriteInt(payload, at, unchecked((int)state.RangeTransformFirst));
        WriteInt(payload, at + 4, state.RangeTransformCount);
        state.RangeTransformRecord.CopyTo(payload, at + 8);
        return payload;
    }

    public static NativeNumberedSaveState Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 0x5718)
            throw new InvalidDataException("Numbered-save layout 3 payload is truncated.");
        int cutoff = ReadNonNegative(payload, 0, "saved frame cutoff");
        int fixedBytes = checked(0x5718 + cutoff * FrameSize);
        Require(payload, 0, fixedBytes, "numbered-save fixed state");

        int[] soundEffects = ReadInts(
            payload, 0x0c, NativeNumberedSaveState.SoundEffectChannelCount);
        byte[] resources = payload.Slice(0x34, NativeNumberedSaveState.ResourceRecordsSize).ToArray();
        byte[] surfaces = payload.Slice(0x4e4, NativeNumberedSaveState.SurfaceRecordsSize).ToArray();
        var frames = new NativeSavedScriptFrame[cutoff + 1];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = ReadFrame(payload, FixedPrefixSize + i * FrameSize);

        int at = fixedBytes;
        int[] counts = ReadInts(payload, at, 6);
        if (counts.Any(count => count < 0))
            throw new InvalidDataException("Numbered-save layout 3 contains a negative bank count.");
        at += 24;
        int[] integers = ReadInts(payload, at, counts[0]);
        at = checked(at + counts[0] * 4);
        int[] floats = ReadInts(payload, at, counts[1]);
        at = checked(at + counts[1] * 4);
        int stringDwords = ReadNonNegative(payload, at, "string blob length");
        at += 4;
        int stringBytes = checked(stringDwords * 4);
        Require(payload, at, stringBytes, "numbered-save string blob");
        string[] strings = DecodeStrings(payload.Slice(at, stringBytes), counts[2]);
        at += stringBytes;
        int[] pointers = ReadInts(payload, at, counts[3]);
        at = checked(at + counts[3] * 4);
        int[] pointerStrings = ReadInts(payload, at, counts[4]);
        at = checked(at + counts[4] * 4);
        int[] localPointerScratch = ReadInts(payload, at, counts[5]);
        at = checked(at + counts[5] * 4);

        int gfxRecordSize = ReadNonNegative(payload, at, "gfx record size");
        int gfxCount = ReadNonNegative(payload, at + 4, "gfx object count");
        if (gfxRecordSize != NativeNumberedSaveState.GfxRecordSize)
            throw new InvalidDataException($"Unsupported native gfx record size 0x{gfxRecordSize:x}.");
        at += 8;
        var objects = new NativeSavedGfxObject[gfxCount];
        for (int i = 0; i < objects.Length; i++)
        {
            Require(payload, at, 4 + gfxRecordSize, "numbered-save gfx object");
            long handle = ReadInt(payload, at);
            objects[i] = new NativeSavedGfxObject(handle, payload.Slice(at + 4, gfxRecordSize).ToArray());
            at += 4 + gfxRecordSize;
        }
        Require(payload, at, 8 + gfxRecordSize, "numbered-save range transform");
        long rangeFirst = ReadInt(payload, at);
        int rangeCount = ReadInt(payload, at + 4);
        byte[] rangeRecord = payload.Slice(at + 8, gfxRecordSize).ToArray();

        return new NativeNumberedSaveState(
            ReadInt(payload, 4), ReadInt(payload, 8), soundEffects, resources, surfaces, frames,
            integers, floats, strings, pointers, pointerStrings, localPointerScratch, objects,
            rangeFirst, rangeCount, rangeRecord);
    }

    public static NativeNumberedSaveState Empty(IReadOnlyList<NativeSavedScriptFrame> frames)
        => new(
            0, 0, new int[NativeNumberedSaveState.SoundEffectChannelCount],
            new byte[NativeNumberedSaveState.ResourceRecordsSize],
            new byte[NativeNumberedSaveState.SurfaceRecordsSize],
            frames, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<string>(),
            Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
            Array.Empty<NativeSavedGfxObject>(), 0, 0,
            new byte[NativeNumberedSaveState.GfxRecordSize]);

    private static void WriteFrame(Span<byte> payload, int offset, NativeSavedScriptFrame frame, bool terminal)
    {
        if (frame.ReturnIndices.Count > FrameReturnCapacity)
            throw new InvalidDataException("A native saved frame cannot hold more than 256 return entries.");
        WriteInt(payload, offset, frame.ParentContext);
        WriteInt(payload, offset + 4, unchecked((int)frame.ScriptId));
        WriteInt(payload, offset + 8, frame.ReturnIndices.Count);
        for (int i = 0; i < frame.ReturnIndices.Count; i++)
            WriteInt(payload, offset + 12 + i * 4, frame.ReturnIndices[i]);
        WriteInt(payload, offset + 0x40c, frame.ResumeIndex);
        WriteInt(payload, offset + 0x410, terminal ? -1 : frame.CallTargetIndex);
    }

    private static NativeSavedScriptFrame ReadFrame(ReadOnlySpan<byte> payload, int offset)
    {
        int count = ReadNonNegative(payload, offset + 8, "frame return count");
        if (count > FrameReturnCapacity)
            throw new InvalidDataException("Numbered-save frame return count exceeds 256.");
        return new NativeSavedScriptFrame(
            ReadInt(payload, offset), unchecked((uint)ReadInt(payload, offset + 4)),
            ReadInts(payload, offset + 12, count),
            ReadInt(payload, offset + 0x40c), ReadInt(payload, offset + 0x410));
    }

    private static byte[] EncodeStrings(IReadOnlyList<string> strings)
    {
        using var stream = new MemoryStream();
        foreach (string text in strings)
        {
            byte[] encoded = NativeEncoding.GetBytes(text ?? "");
            stream.Write(encoded);
            stream.WriteByte(0);
        }
        while ((stream.Length & 3) != 0) stream.WriteByte(0);
        return stream.ToArray();
    }

    private static string[] DecodeStrings(ReadOnlySpan<byte> blob, int count)
    {
        var result = new string[count];
        int at = 0;
        for (int i = 0; i < count; i++)
        {
            if (at >= blob.Length)
                throw new InvalidDataException("Numbered-save string blob ended before its declared string count.");
            int end = blob[at..].IndexOf((byte)0);
            if (end < 0)
                throw new InvalidDataException("Numbered-save string blob ended before its declared string count.");
            result[i] = NativeEncoding.GetString(blob.Slice(at, end));
            at += end + 1;
        }
        return result;
    }

    private static void ValidateState(NativeNumberedSaveState state)
    {
        if (state.Frames.Count == 0) throw new InvalidDataException("A numbered save requires at least one frame.");
        if (state.SoundEffectResourceIds.Count != NativeNumberedSaveState.SoundEffectChannelCount)
            throw new InvalidDataException("Numbered-save sound-effect channel count must be 10.");
        if (state.ResourceRecords.Length != NativeNumberedSaveState.ResourceRecordsSize)
            throw new InvalidDataException("Numbered-save resource table must be 1,200 bytes.");
        if (state.SurfaceRecords.Length != NativeNumberedSaveState.SurfaceRecordsSize)
            throw new InvalidDataException("Numbered-save surface table must be 20,000 bytes.");
        if (state.RangeTransformRecord.Length != NativeNumberedSaveState.GfxRecordSize
            || state.GfxObjects.Any(item => item.Record.Length != NativeNumberedSaveState.GfxRecordSize))
            throw new InvalidDataException("Numbered-save gfx records must be 0x2d4 bytes.");
    }

    private static int[] ReadInts(ReadOnlySpan<byte> source, int offset, int count)
    {
        int bytes = checked(count * 4);
        Require(source, offset, bytes, "numbered-save integer array");
        var values = new int[count];
        for (int i = 0; i < count; i++) values[i] = ReadInt(source, offset + i * 4);
        return values;
    }

    private static void WriteIntList(Span<byte> destination, int offset, IReadOnlyList<int> values)
    {
        for (int i = 0; i < values.Count; i++) WriteInt(destination, offset + i * 4, values[i]);
    }

    private static int ReadNonNegative(ReadOnlySpan<byte> source, int offset, string name)
    {
        int value = ReadInt(source, offset);
        if (value < 0) throw new InvalidDataException($"Numbered-save {name} is negative.");
        return value;
    }

    private static int ReadInt(ReadOnlySpan<byte> source, int offset)
    {
        Require(source, offset, 4, "numbered-save dword");
        return BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
    }

    private static void WriteInt(Span<byte> destination, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), value);

    private static void Require(ReadOnlySpan<byte> source, int offset, int count, string name)
    {
        if (offset < 0 || count < 0 || offset > source.Length - count)
            throw new InvalidDataException($"{name} is truncated.");
    }

    private static Encoding CreateNativeEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}

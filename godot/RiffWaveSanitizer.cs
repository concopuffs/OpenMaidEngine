using System;
using System.Buffers.Binary;

/// <summary>Godot-specific WAV input adapter. Godot assumes RIFF INFO strings are UTF-8, while AGE's
/// Japanese assets commonly store them as CP932. Playback does not consume these tags, so remove only
/// INFO metadata while preserving every functional RIFF chunk byte-for-byte.</summary>
internal static class RiffWaveSanitizer
{
    public static byte[] RemoveInfoMetadata(byte[] wavBytes)
    {
        ReadOnlySpan<byte> input = wavBytes;
        if (input.Length < 12 || !HasId(input, 0, "RIFF") || !HasId(input, 8, "WAVE"))
            return wavBytes;

        ulong declaredEnd64 = 8UL + BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(4, 4));
        if (declaredEnd64 < 12 || declaredEnd64 > (ulong)input.Length || declaredEnd64 > int.MaxValue)
            return wavBytes;
        int declaredEnd = (int)declaredEnd64;

        int cursor = 12;
        int removedBytes = 0;
        while (cursor < declaredEnd)
        {
            if (!TryGetChunk(input, cursor, declaredEnd, out int chunkBytes, out bool isInfoList))
                return wavBytes;
            if (isInfoList) removedBytes = checked(removedBytes + chunkBytes);
            cursor += chunkBytes;
        }
        if (removedBytes == 0) return wavBytes;

        var output = new byte[checked(wavBytes.Length - removedBytes)];
        input[..12].CopyTo(output);
        cursor = 12;
        int destination = 12;
        while (cursor < declaredEnd)
        {
            _ = TryGetChunk(input, cursor, declaredEnd, out int chunkBytes, out bool isInfoList);
            if (!isInfoList)
            {
                input.Slice(cursor, chunkBytes).CopyTo(output.AsSpan(destination));
                destination += chunkBytes;
            }
            cursor += chunkBytes;
        }

        int newDeclaredEnd = declaredEnd - removedBytes;
        input[declaredEnd..].CopyTo(output.AsSpan(newDeclaredEnd));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4),
            checked((uint)(newDeclaredEnd - 8)));
        return output;
    }

    private static bool TryGetChunk(ReadOnlySpan<byte> input, int offset, int declaredEnd,
                                    out int chunkBytes, out bool isInfoList)
    {
        chunkBytes = 0;
        isInfoList = false;
        if (offset > declaredEnd - 8) return false;
        uint payloadBytes = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset + 4, 4));
        ulong total64 = 8UL + payloadBytes + (payloadBytes & 1U);
        if (total64 > int.MaxValue || total64 > (ulong)(declaredEnd - offset)) return false;
        chunkBytes = (int)total64;
        isInfoList = payloadBytes >= 4 && HasId(input, offset, "LIST")
                    && HasId(input, offset + 8, "INFO");
        return true;
    }

    private static bool HasId(ReadOnlySpan<byte> bytes, int offset, string id)
        => offset >= 0 && offset <= bytes.Length - 4
           && bytes[offset] == id[0] && bytes[offset + 1] == id[1]
           && bytes[offset + 2] == id[2] && bytes[offset + 3] == id[3];
}

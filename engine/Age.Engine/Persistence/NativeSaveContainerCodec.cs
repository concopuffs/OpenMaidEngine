using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Age.Engine.Sys4;

namespace Age.Engine.Persistence;

public enum NativeSaveMagic
{
    S3SD,
    S4SD,
}

public readonly record struct NativeSystemTime(
    ushort Year,
    ushort Month,
    ushort DayOfWeek,
    ushort Day,
    ushort Hour,
    ushort Minute,
    ushort Second,
    ushort Milliseconds)
{
    public static NativeSystemTime FromLocalDateTime(DateTime value)
    {
        DateTime local = value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
        return new NativeSystemTime(
            (ushort)local.Year, (ushort)local.Month, (ushort)local.DayOfWeek, (ushort)local.Day,
            (ushort)local.Hour, (ushort)local.Minute, (ushort)local.Second, (ushort)local.Millisecond);
    }
}

public sealed record NativeSaveMetadata(
    NativeSaveMagic Magic,
    uint CompatibilityId,
    string GameId,
    NativeSystemTime Timestamp,
    uint AccumulatedPlaySeconds,
    int SaveVersion1,
    int SaveVersion2);

public sealed record NativeSaveDocument(
    NativeSaveMetadata Metadata,
    byte[] Payload,
    int BytesConsumed);

public sealed record NativeSaveEncodingOptions(uint XorSeed, ushort Multiplier)
{
    public static NativeSaveEncodingOptions Random()
    {
        Span<byte> random = stackalloc byte[6];
        RandomNumberGenerator.Fill(random);
        uint seed = BinaryPrimitives.ReadUInt32LittleEndian(random);
        ushort multiplier = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(random[4..]) | 1);
        return new NativeSaveEncodingOptions(seed, multiplier);
    }
}

/// <summary>
/// AGE's common S3SD/S4SD wrapper used by shared SAVE.DAT and numbered SAVE##.DAT files.
/// Payload serializers own the bytes inside this wrapper; this class owns the native header,
/// integrity checks, optional LZSS layer, and reversible DWORD transform.
/// </summary>
public static class NativeSaveContainerCodec
{
    public const int HeaderSize = 0x124;
    public const int CodecFrameSize = 0x14;
    public const int FixedPrefixSize = HeaderSize + CodecFrameSize;

    private const uint SeedIncrement = 0x0b0b0b0b;
    private const ushort MultiplierIncrement = 0x0b02;
    private const uint MsbPolynomial = 0x04c11db7;
    private const uint ReflectedPolynomial = 0xedb88320;
    private static readonly Encoding ShiftJis = CreateShiftJis();

    public static byte[] Encode(
        ReadOnlySpan<byte> payload,
        NativeSaveMetadata metadata,
        NativeSaveEncodingOptions? options = null)
    {
        if ((payload.Length & 3) != 0)
            throw new ArgumentException("Native save payload length must be DWORD-aligned.", nameof(payload));
        if (metadata.SaveVersion2 < 0)
            throw new ArgumentOutOfRangeException(nameof(metadata), "SaveVersion2 cannot be negative.");

        options ??= NativeSaveEncodingOptions.Random();
        ValidateMultiplier(options.Multiplier);

        byte[] checkedLogical = new byte[payload.Length + 8];
        payload.CopyTo(checkedLogical.AsSpan(8));
        WriteLogicalChecksums(checkedLogical);

        byte[] transformInput = metadata.SaveVersion2 >= 2
            ? BuildCompressedWrapper(checkedLogical)
            : checkedLogical;
        byte[] encoded = ExpandTransform(transformInput, options.XorSeed, options.Multiplier);

        byte[] result = new byte[checked(HeaderSize + CodecFrameSize + encoded.Length)];
        WriteHeader(result.AsSpan(0, HeaderSize), metadata);
        Span<byte> frame = result.AsSpan(HeaderSize, CodecFrameSize);
        BinaryPrimitives.WriteUInt32LittleEndian(frame, checked((uint)(encoded.Length / 4)));
        BinaryPrimitives.WriteUInt32LittleEndian(frame[4..], Crc32Msb(encoded));
        BinaryPrimitives.WriteUInt32LittleEndian(frame[8..], Crc32Reflected(encoded));
        BinaryPrimitives.WriteUInt32LittleEndian(frame[12..], options.XorSeed);
        BinaryPrimitives.WriteUInt32LittleEndian(frame[16..], options.Multiplier);
        encoded.CopyTo(result.AsSpan(FixedPrefixSize));
        return result;
    }

    public static NativeSaveDocument Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < FixedPrefixSize)
            throw new InvalidDataException("Native save container is shorter than its fixed header.");

        NativeSaveMetadata metadata = ReadMetadata(source);
        ReadOnlySpan<byte> frame = source.Slice(HeaderSize, CodecFrameSize);
        uint encodedDwordCount = BinaryPrimitives.ReadUInt32LittleEndian(frame);
        if ((encodedDwordCount & 1) != 0)
            throw new InvalidDataException("Native save encoded DWORD count is not even.");
        if (encodedDwordCount > int.MaxValue / 4)
            throw new InvalidDataException("Native save encoded DWORD count is too large.");
        int encodedLength = (int)encodedDwordCount * 4;
        if (encodedLength > source.Length - FixedPrefixSize)
            throw new InvalidDataException("Native save encoded payload is truncated.");

        ReadOnlySpan<byte> encoded = source.Slice(FixedPrefixSize, encodedLength);
        uint expectedMsb = BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]);
        uint expectedReflected = BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]);
        if (Crc32Msb(encoded) != expectedMsb || Crc32Reflected(encoded) != expectedReflected)
            throw new InvalidDataException("Native save encoded payload checksum mismatch.");

        uint seed = BinaryPrimitives.ReadUInt32LittleEndian(frame[12..]);
        uint storedMultiplier = BinaryPrimitives.ReadUInt32LittleEndian(frame[16..]);
        if ((storedMultiplier & 0xffff0000) != 0)
            throw new InvalidDataException("Native save multiplier contains unexpected high bits.");
        ushort multiplier = (ushort)storedMultiplier;
        ValidateMultiplier(multiplier);

        byte[] transformInput = InverseTransform(encoded, seed, multiplier);
        byte[] checkedLogical = metadata.SaveVersion2 >= 2
            ? DecodeCompressedWrapper(transformInput)
            : transformInput;
        if (checkedLogical.Length < 8 || (checkedLogical.Length & 3) != 0)
            throw new InvalidDataException("Native save logical payload has an invalid length.");

        ReadOnlySpan<byte> payload = checkedLogical.AsSpan(8);
        uint logicalMsb = BinaryPrimitives.ReadUInt32LittleEndian(checkedLogical);
        uint logicalReflected = BinaryPrimitives.ReadUInt32LittleEndian(checkedLogical.AsSpan(4));
        if (Crc32Msb(payload) != logicalMsb || Crc32Reflected(payload) != logicalReflected)
            throw new InvalidDataException("Native save logical payload checksum mismatch.");

        return new NativeSaveDocument(
            metadata,
            payload.ToArray(),
            checked(FixedPrefixSize + encodedLength));
    }

    /// <summary>Read only the fixed native header, matching metadata-query opcode 0x1a0.</summary>
    public static NativeSaveMetadata ReadMetadata(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
            throw new InvalidDataException("Native save container is shorter than its metadata header.");
        return ReadHeader(source[..HeaderSize]);
    }

    public static uint Crc32Msb(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= (uint)value << 24;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 0x80000000) != 0 ? crc << 1 ^ MsbPolynomial : crc << 1;
        }
        return ~crc;
    }

    public static uint Crc32Reflected(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? crc >> 1 ^ ReflectedPolynomial : crc >> 1;
        }
        return ~crc;
    }

    private static void WriteHeader(Span<byte> header, NativeSaveMetadata metadata)
    {
        header.Clear();
        WriteMagic(header, metadata.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], metadata.CompatibilityId);

        if (metadata.GameId.Contains('\0'))
            throw new ArgumentException("Native save game id cannot contain NUL.", nameof(metadata));
        byte[] gameId = ShiftJis.GetBytes(metadata.GameId);
        if (gameId.Length >= 0x100)
            throw new ArgumentException("Native save game id must fit in 255 Shift-JIS bytes.", nameof(metadata));
        gameId.CopyTo(header[8..]);

        Span<byte> systemTime = header[0x108..0x118];
        BinaryPrimitives.WriteUInt16LittleEndian(systemTime, metadata.Timestamp.Year);
        BinaryPrimitives.WriteUInt16LittleEndian(systemTime[2..], metadata.Timestamp.Month);
        BinaryPrimitives.WriteUInt16LittleEndian(systemTime[4..], metadata.Timestamp.DayOfWeek);
        BinaryPrimitives.WriteUInt16LittleEndian(systemTime[6..], metadata.Timestamp.Day);
        BinaryPrimitives.WriteUInt16LittleEndian(systemTime[8..], metadata.Timestamp.Hour);
        BinaryPrimitives.WriteUInt16LittleEndian(systemTime[10..], metadata.Timestamp.Minute);
        BinaryPrimitives.WriteUInt16LittleEndian(systemTime[12..], metadata.Timestamp.Second);
        BinaryPrimitives.WriteUInt16LittleEndian(systemTime[14..], metadata.Timestamp.Milliseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x118..], metadata.AccumulatedPlaySeconds);
        BinaryPrimitives.WriteInt32LittleEndian(header[0x11c..], metadata.SaveVersion1);
        BinaryPrimitives.WriteInt32LittleEndian(header[0x120..], metadata.SaveVersion2);
    }

    private static NativeSaveMetadata ReadHeader(ReadOnlySpan<byte> header)
    {
        NativeSaveMagic magic = ReadMagic(header);
        uint compatibilityId = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        ReadOnlySpan<byte> gameIdBytes = header.Slice(8, 0x100);
        int terminator = gameIdBytes.IndexOf((byte)0);
        if (terminator < 0)
            throw new InvalidDataException("Native save game id is not NUL-terminated.");
        string gameId;
        try
        {
            gameId = ShiftJis.GetString(gameIdBytes[..terminator]);
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("Native save game id is not valid Shift-JIS.", error);
        }

        ReadOnlySpan<byte> systemTime = header[0x108..0x118];
        var timestamp = new NativeSystemTime(
            BinaryPrimitives.ReadUInt16LittleEndian(systemTime),
            BinaryPrimitives.ReadUInt16LittleEndian(systemTime[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(systemTime[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(systemTime[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(systemTime[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(systemTime[10..]),
            BinaryPrimitives.ReadUInt16LittleEndian(systemTime[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(systemTime[14..]));

        return new NativeSaveMetadata(
            magic,
            compatibilityId,
            gameId,
            timestamp,
            BinaryPrimitives.ReadUInt32LittleEndian(header[0x118..]),
            BinaryPrimitives.ReadInt32LittleEndian(header[0x11c..]),
            BinaryPrimitives.ReadInt32LittleEndian(header[0x120..]));
    }

    private static byte[] BuildCompressedWrapper(ReadOnlySpan<byte> checkedLogical)
    {
        byte[] stored = LzssEncoder.EncodeOrVerbatim(checkedLogical);
        int nativeDwordCount = checked(stored.Length / 4 + 0x0d);
        byte[] wrapper = new byte[checked(nativeDwordCount * 4)];
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper, checked((uint)checkedLogical.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper.AsSpan(4), checked((uint)checkedLogical.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper.AsSpan(8), checked((uint)stored.Length));
        stored.CopyTo(wrapper.AsSpan(12));
        return wrapper;
    }

    private static byte[] DecodeCompressedWrapper(ReadOnlySpan<byte> wrapper)
    {
        if (wrapper.Length < 12)
            throw new InvalidDataException("Native save compressed wrapper is truncated.");
        uint originalLength = BinaryPrimitives.ReadUInt32LittleEndian(wrapper);
        uint consumedLength = BinaryPrimitives.ReadUInt32LittleEndian(wrapper[4..]);
        uint storedLength = BinaryPrimitives.ReadUInt32LittleEndian(wrapper[8..]);
        if (originalLength != consumedLength)
            throw new InvalidDataException("Native save LZSS wrapper length fields disagree.");
        if (originalLength > int.MaxValue || storedLength > int.MaxValue ||
            storedLength > (uint)(wrapper.Length - 12))
            throw new InvalidDataException("Native save LZSS wrapper length is invalid.");

        ReadOnlySpan<byte> stored = wrapper.Slice(12, (int)storedLength);
        return storedLength == originalLength
            ? stored.ToArray()
            : LzssDecoder.Decode(stored, (int)originalLength, "native save LZSS");
    }

    private static byte[] ExpandTransform(ReadOnlySpan<byte> source, uint seed, ushort multiplier)
    {
        if ((source.Length & 3) != 0)
            throw new InvalidDataException("Native save transform input is not DWORD-aligned.");
        byte[] encoded = new byte[checked(source.Length * 2)];
        for (int input = 0, output = 0; input < source.Length; input += 4, output += 8)
        {
            uint mixed = BinaryPrimitives.ReadUInt32LittleEndian(source[input..]) ^ seed;
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(output), (mixed >> 16) * multiplier);
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(output + 4), (mixed & 0xffff) * multiplier);
            seed = unchecked(seed + SeedIncrement);
            multiplier = unchecked((ushort)(multiplier + MultiplierIncrement));
        }
        return encoded;
    }

    private static byte[] InverseTransform(ReadOnlySpan<byte> encoded, uint seed, ushort multiplier)
    {
        if ((encoded.Length & 7) != 0)
            throw new InvalidDataException("Native save encoded transform length is invalid.");
        byte[] source = new byte[encoded.Length / 2];
        for (int input = 0, output = 0; input < encoded.Length; input += 8, output += 4)
        {
            uint highProduct = BinaryPrimitives.ReadUInt32LittleEndian(encoded[input..]);
            uint lowProduct = BinaryPrimitives.ReadUInt32LittleEndian(encoded[(input + 4)..]);
            if (highProduct % multiplier != 0 || lowProduct % multiplier != 0)
                throw new InvalidDataException("Native save transform product is not exactly divisible.");
            uint high = highProduct / multiplier;
            uint low = lowProduct / multiplier;
            if (high > ushort.MaxValue || low > ushort.MaxValue)
                throw new InvalidDataException("Native save transform quotient exceeds 16 bits.");

            uint mixed = high << 16 | low;
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(output), mixed ^ seed);
            seed = unchecked(seed + SeedIncrement);
            multiplier = unchecked((ushort)(multiplier + MultiplierIncrement));
        }
        return source;
    }

    private static void WriteLogicalChecksums(Span<byte> checkedLogical)
    {
        ReadOnlySpan<byte> payload = checkedLogical[8..];
        BinaryPrimitives.WriteUInt32LittleEndian(checkedLogical, Crc32Msb(payload));
        BinaryPrimitives.WriteUInt32LittleEndian(checkedLogical[4..], Crc32Reflected(payload));
    }

    private static void WriteMagic(Span<byte> destination, NativeSaveMagic magic)
    {
        destination[0] = (byte)'S';
        destination[1] = magic == NativeSaveMagic.S3SD ? (byte)'3' : (byte)'4';
        destination[2] = (byte)'S';
        destination[3] = (byte)'D';
    }

    private static NativeSaveMagic ReadMagic(ReadOnlySpan<byte> source)
    {
        if (source[0] != 'S' || source[2] != 'S' || source[3] != 'D')
            throw new InvalidDataException("Native save magic is invalid.");
        return source[1] switch
        {
            (byte)'3' => NativeSaveMagic.S3SD,
            (byte)'4' => NativeSaveMagic.S4SD,
            _ => throw new InvalidDataException("Native save generation is unsupported."),
        };
    }

    private static void ValidateMultiplier(ushort multiplier)
    {
        if (multiplier == 0 || (multiplier & 1) == 0)
            throw new InvalidDataException("Native save multiplier must be a nonzero odd value.");
    }

    private static Encoding CreateShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Age.Engine.Persistence;

/// <summary>
/// Typed logical contents of AGE's shared SAVE.DAT payload. The catalog and extended arrays are
/// intentionally opaque: the selected-cell service owns only the integer and string maps, while
/// native import/export must preserve the other engine-owned sections losslessly.
/// </summary>
public sealed class SharedProfilePayload
{
    public IReadOnlyList<uint> CatalogCompatibilityValues { get; }
    public IReadOnlyDictionary<int, uint> IntegerCells { get; }
    public IReadOnlyDictionary<int, string> StringCells { get; }
    public IReadOnlyList<uint> ExtendedSelectorCounts { get; }
    public IReadOnlyList<uint> ExtendedValues { get; }
    public IReadOnlyList<uint> ReservedTail { get; }

    public SharedProfilePayload(
        IEnumerable<uint>? catalogCompatibilityValues = null,
        IEnumerable<KeyValuePair<int, uint>>? integerCells = null,
        IEnumerable<KeyValuePair<int, string>>? stringCells = null,
        IEnumerable<uint>? extendedSelectorCounts = null,
        IEnumerable<uint>? extendedValues = null,
        IEnumerable<uint>? reservedTail = null)
    {
        uint[] selectors = extendedSelectorCounts?.ToArray() ?? Array.Empty<uint>();
        if (selectors.Length is not 0 and not SharedProfilePayloadCodec.ExtendedSelectorCount)
            throw new ArgumentException(
                $"Extended selector table must contain exactly {SharedProfilePayloadCodec.ExtendedSelectorCount} values.",
                nameof(extendedSelectorCounts));

        uint[] tail = reservedTail?.ToArray() ?? new uint[SharedProfilePayloadCodec.ReservedTailDwordCount];
        if (tail.Length != SharedProfilePayloadCodec.ReservedTailDwordCount)
            throw new ArgumentException(
                $"Reserved tail must contain exactly {SharedProfilePayloadCodec.ReservedTailDwordCount} DWORDs.",
                nameof(reservedTail));

        CatalogCompatibilityValues = Array.AsReadOnly(
            catalogCompatibilityValues?.ToArray() ?? Array.Empty<uint>());
        IntegerCells = new ReadOnlyDictionary<int, uint>(
            integerCells?.ToDictionary() ?? new Dictionary<int, uint>());
        StringCells = new ReadOnlyDictionary<int, string>(
            stringCells?.ToDictionary() ?? new Dictionary<int, string>());
        ExtendedSelectorCounts = Array.AsReadOnly(selectors);
        ExtendedValues = Array.AsReadOnly(extendedValues?.ToArray() ?? Array.Empty<uint>());
        ReservedTail = Array.AsReadOnly(tail);
    }
}

/// <summary>
/// Codec for the logical payload inside shared SAVE.DAT's common S3SD/S4SD container.
/// Keys use a raw one-byte AGE type tag followed by eight lowercase ASCII hex digits.
/// </summary>
public static class SharedProfilePayloadCodec
{
    public const int ExtendedSelectorCount = 0x100;
    // Native writer emits one explicit zero terminator and its historical allocation formula
    // leaves eight additional DWORDs at the end of the logical payload.
    public const int ReservedTailDwordCount = 9;

    private const byte IntegerTypeTag = 0x03;
    private const byte StringTypeTag = 0x05;
    private const int FixedIntegerEntrySize = 0x10;
    private const int FixedKeyFieldSize = 0x0c;
    private static readonly Encoding ShiftJis = CreateShiftJis();

    public static byte[] Encode(SharedProfilePayload payload, NativeSaveMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(metadata);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        WriteDwordArray(writer, payload.CatalogCompatibilityValues);

        KeyValuePair<int, uint>[] integers = payload.IntegerCells
            .OrderBy(entry => entry.Key)
            .ToArray();
        writer.Write(checked((uint)integers.Length));
        Span<byte> entryBuffer = stackalloc byte[FixedIntegerEntrySize];
        foreach (var (address, value) in integers)
        {
            entryBuffer.Clear();
            WriteTypedKey(entryBuffer[..FixedKeyFieldSize], IntegerTypeTag, address);
            BinaryPrimitives.WriteUInt32LittleEndian(entryBuffer[FixedKeyFieldSize..], value);
            writer.Write(entryBuffer);
        }

        KeyValuePair<int, string>[] strings = payload.StringCells
            .OrderBy(entry => entry.Key)
            .ToArray();
        writer.Write(checked((uint)strings.Length));
        byte[] stringBlob = BuildStringBlob(strings);
        writer.Write(checked((uint)(stringBlob.Length / 4)));
        writer.Write(stringBlob);

        if (HasExtendedSections(metadata))
        {
            IReadOnlyList<uint> selectors = payload.ExtendedSelectorCounts.Count == 0
                ? new uint[ExtendedSelectorCount]
                : payload.ExtendedSelectorCounts;
            foreach (uint value in selectors) writer.Write(value);
            WriteDwordArray(writer, payload.ExtendedValues);
        }
        else if (payload.ExtendedSelectorCounts.Count != 0 || payload.ExtendedValues.Count != 0)
        {
            throw new InvalidDataException(
                $"Shared profile version {metadata.SaveVersion1}.{metadata.SaveVersion2} " +
                "cannot encode version-3.10 extended sections.");
        }

        foreach (uint value in payload.ReservedTail) writer.Write(value);
        writer.Flush();
        return stream.ToArray();
    }

    public static SharedProfilePayload Decode(ReadOnlySpan<byte> source, NativeSaveMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var reader = new PayloadReader(source);

        uint[] catalog = reader.ReadDwordArray("catalog compatibility");

        int integerCount = reader.ReadCount("integer entry", FixedIntegerEntrySize);
        var integers = new Dictionary<int, uint>(integerCount);
        for (int i = 0; i < integerCount; i++)
        {
            ReadOnlySpan<byte> entry = reader.ReadBytes(FixedIntegerEntrySize, "integer entry");
            int address = ReadTypedKey(entry[..FixedKeyFieldSize], IntegerTypeTag);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(entry[FixedKeyFieldSize..]);
            if (!integers.TryAdd(address, value))
                throw new InvalidDataException($"Shared SAVE.DAT repeats integer cell 0x{address:x8}.");
        }

        int stringCount = reader.ReadCount("string entry", minimumBytesPerEntry: 2);
        uint stringBlobDwords = reader.ReadUInt32("string blob DWORD count");
        if (stringBlobDwords > int.MaxValue / 4)
            throw new InvalidDataException("Shared SAVE.DAT string blob is too large.");
        int stringBlobLength = checked((int)stringBlobDwords * 4);
        ReadOnlySpan<byte> stringBlob = reader.ReadBytes(stringBlobLength, "string blob");
        var strings = new Dictionary<int, string>(stringCount);
        int stringPosition = 0;
        for (int i = 0; i < stringCount; i++)
        {
            ReadOnlySpan<byte> key = ReadCString(stringBlob, ref stringPosition, "string key");
            int address = ReadTypedKey(key, StringTypeTag);
            ReadOnlySpan<byte> value = ReadCString(stringBlob, ref stringPosition, "string value");
            string decoded;
            try
            {
                decoded = ShiftJis.GetString(value);
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException(
                    $"Shared SAVE.DAT string cell 0x{address:x8} is not valid CP932.", error);
            }
            if (!strings.TryAdd(address, decoded))
                throw new InvalidDataException($"Shared SAVE.DAT repeats string cell 0x{address:x8}.");
        }
        if (stringBlob[stringPosition..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("Shared SAVE.DAT string blob has nonzero padding.");

        uint[] selectors = Array.Empty<uint>();
        uint[] extended = Array.Empty<uint>();
        if (HasExtendedSections(metadata))
        {
            selectors = reader.ReadDwords(ExtendedSelectorCount, "extended selector table");
            extended = reader.ReadDwordArray("extended");
        }

        uint[] tail = reader.ReadDwords(ReservedTailDwordCount, "reserved tail");
        if (!reader.AtEnd)
            throw new InvalidDataException(
                $"Shared SAVE.DAT has {reader.Remaining} unexpected trailing byte(s).");

        return new SharedProfilePayload(catalog, integers, strings, selectors, extended, tail);
    }

    private static byte[] BuildStringBlob(IReadOnlyList<KeyValuePair<int, string>> entries)
    {
        using var stream = new MemoryStream();
        Span<byte> key = stackalloc byte[10];
        foreach (var (address, rawValue) in entries)
        {
            WriteTypedKey(key, StringTypeTag, address);
            stream.Write(key);

            string value = TruncateAtNul(rawValue ?? string.Empty);
            byte[] encodedValue = ShiftJis.GetBytes(value);
            stream.Write(encodedValue);
            stream.WriteByte(0);
        }

        // Native stores a DWORD count and rounds up with `(byte_length / 4) + 1`, which deliberately
        // adds a whole zero DWORD when the strings already end on a DWORD boundary.
        int paddedLength = checked((int)((stream.Length / 4 + 1) * 4));
        stream.SetLength(paddedLength);
        return stream.ToArray();
    }

    private static void WriteTypedKey(Span<byte> destination, byte typeTag, int address)
    {
        if (address < 0)
            throw new InvalidDataException("Shared profile cell addresses cannot be negative.");
        if (destination.Length < 10)
            throw new ArgumentException("Typed-key destination is too short.", nameof(destination));

        destination[0] = typeTag;
        bool written = address.TryFormat(
            destination[1..9], out int charsWritten, "x8", CultureInfo.InvariantCulture);
        if (!written || charsWritten != 8)
            throw new InvalidDataException($"Shared profile cell address 0x{address:x} does not fit its key.");
        destination[9] = 0;
    }

    private static int ReadTypedKey(ReadOnlySpan<byte> source, byte expectedTypeTag)
    {
        bool hasTerminator = source.Length >= 10;
        if (source.Length < 9 || source[0] != expectedTypeTag
            || hasTerminator && source[9] != 0)
            throw new InvalidDataException(
                $"Shared SAVE.DAT key does not have type tag 0x{expectedTypeTag:x2} and eight hex digits.");
        uint address = 0;
        for (int i = 1; i <= 8; i++)
        {
            int digit = source[i] switch
            {
                >= (byte)'0' and <= (byte)'9' => source[i] - '0',
                >= (byte)'a' and <= (byte)'f' => source[i] - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => source[i] - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
                throw new InvalidDataException("Shared SAVE.DAT typed key contains a non-hex digit.");
            address = address * 16 + (uint)digit;
        }
        if (address > int.MaxValue)
            throw new InvalidDataException("Shared SAVE.DAT cell address exceeds the VM global-bank range.");
        return (int)address;
    }

    private static ReadOnlySpan<byte> ReadCString(
        ReadOnlySpan<byte> source, ref int position, string field)
    {
        if ((uint)position > (uint)source.Length)
            throw new InvalidDataException($"Shared SAVE.DAT {field} starts outside its string blob.");
        int relativeEnd = source[position..].IndexOf((byte)0);
        if (relativeEnd < 0)
            throw new InvalidDataException($"Shared SAVE.DAT {field} is not NUL-terminated.");
        ReadOnlySpan<byte> value = source.Slice(position, relativeEnd);
        position = checked(position + relativeEnd + 1);
        return value;
    }

    private static void WriteDwordArray(BinaryWriter writer, IReadOnlyList<uint> values)
    {
        writer.Write(checked((uint)values.Count));
        foreach (uint value in values) writer.Write(value);
    }

    private static bool HasExtendedSections(NativeSaveMetadata metadata)
        => metadata.SaveVersion1 > 3
           || metadata.SaveVersion1 == 3 && metadata.SaveVersion2 >= 10;

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

    private ref struct PayloadReader
    {
        private readonly ReadOnlySpan<byte> _source;
        private int _position;

        internal PayloadReader(ReadOnlySpan<byte> source)
        {
            _source = source;
            _position = 0;
        }

        internal bool AtEnd => _position == _source.Length;
        internal int Remaining => _source.Length - _position;

        internal uint ReadUInt32(string field)
        {
            ReadOnlySpan<byte> bytes = ReadBytes(4, field);
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        internal int ReadCount(string field, int minimumBytesPerEntry)
        {
            uint count = ReadUInt32(field + " count");
            if (count > int.MaxValue
                || minimumBytesPerEntry > 0 && count > (uint)(Remaining / minimumBytesPerEntry))
                throw new InvalidDataException($"Shared SAVE.DAT {field} count is too large.");
            return (int)count;
        }

        internal uint[] ReadDwordArray(string field)
        {
            int count = ReadCount(field, 4);
            return ReadDwords(count, field);
        }

        internal uint[] ReadDwords(int count, string field)
        {
            if (count < 0 || count > Remaining / 4)
                throw new InvalidDataException($"Shared SAVE.DAT {field} is truncated.");
            var values = new uint[count];
            for (int i = 0; i < count; i++) values[i] = ReadUInt32(field);
            return values;
        }

        internal ReadOnlySpan<byte> ReadBytes(int count, string field)
        {
            if (count < 0 || count > Remaining)
                throw new InvalidDataException($"Shared SAVE.DAT {field} is truncated.");
            ReadOnlySpan<byte> result = _source.Slice(_position, count);
            _position += count;
            return result;
        }
    }
}

/// <summary>
/// Profile-lifetime selected cells plus the opaque native sections required to round-trip SAVE.DAT.
/// VM opcodes mutate this object; explicit Load/Save calls own filesystem lifecycle.
/// </summary>
public sealed class SharedProfile
{
    private uint[] _catalogCompatibilityValues = Array.Empty<uint>();
    private readonly Dictionary<int, uint> _integerCells = new();
    private readonly Dictionary<int, string> _stringCells = new();
    private uint[] _extendedSelectorCounts = Array.Empty<uint>();
    private uint[] _extendedValues = Array.Empty<uint>();
    private uint[] _reservedTail = new uint[SharedProfilePayloadCodec.ReservedTailDwordCount];

    public IReadOnlyDictionary<int, uint> IntegerCells => _integerCells;
    public IReadOnlyDictionary<int, string> StringCells => _stringCells;

    public void StoreInteger(int address, long value)
    {
        ValidateAddress(address);
        _integerCells[address] = unchecked((uint)value);
    }

    public int LoadInteger(int address)
    {
        ValidateAddress(address);
        return _integerCells.TryGetValue(address, out uint value) ? unchecked((int)value) : 0;
    }

    public void StoreString(int address, string value)
    {
        ValidateAddress(address);
        ArgumentNullException.ThrowIfNull(value);
        int nul = value.IndexOf('\0');
        _stringCells[address] = nul < 0 ? value : value[..nul];
    }

    public string LoadString(int address)
    {
        ValidateAddress(address);
        return _stringCells.TryGetValue(address, out string? value) ? value : string.Empty;
    }

    public bool Load(INativeDatStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        NativeSaveDocument? document = store.LoadShared();
        if (document is null)
        {
            Clear();
            return false;
        }

        Replace(SharedProfilePayloadCodec.Decode(document.Payload, document.Metadata));
        return true;
    }

    public void Save(
        INativeDatStore store,
        NativeSystemTime timestamp,
        uint accumulatedPlaySeconds)
    {
        ArgumentNullException.ThrowIfNull(store);
        NativeSaveMetadata metadata = store.Identity.CreateMetadata(timestamp, accumulatedPlaySeconds);
        byte[] payload = SharedProfilePayloadCodec.Encode(Snapshot(), metadata);
        store.SaveShared(payload, timestamp, accumulatedPlaySeconds);
    }

    public SharedProfilePayload Snapshot()
        => new(
            _catalogCompatibilityValues,
            _integerCells,
            _stringCells,
            _extendedSelectorCounts,
            _extendedValues,
            _reservedTail);

    public void Replace(SharedProfilePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _catalogCompatibilityValues = payload.CatalogCompatibilityValues.ToArray();
        _integerCells.Clear();
        foreach (var entry in payload.IntegerCells) _integerCells.Add(entry.Key, entry.Value);
        _stringCells.Clear();
        foreach (var entry in payload.StringCells) _stringCells.Add(entry.Key, entry.Value);
        _extendedSelectorCounts = payload.ExtendedSelectorCounts.ToArray();
        _extendedValues = payload.ExtendedValues.ToArray();
        _reservedTail = payload.ReservedTail.ToArray();
    }

    public void Clear()
    {
        _catalogCompatibilityValues = Array.Empty<uint>();
        _integerCells.Clear();
        _stringCells.Clear();
        _extendedSelectorCounts = Array.Empty<uint>();
        _extendedValues = Array.Empty<uint>();
        _reservedTail = new uint[SharedProfilePayloadCodec.ReservedTailDwordCount];
    }

    private static void ValidateAddress(int address)
    {
        if (address < 0)
            throw new ArgumentOutOfRangeException(nameof(address), "Shared profile cell address cannot be negative.");
    }
}

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Age.Engine.Persistence;

/// <summary>
/// Typed logical contents of AGE's shared SAVE.DAT payload. CatalogCompatibilityValues retains the
/// historical codec-facing name but carries AGE's encrypted base resource-unlock table; the selector
/// and extended arrays carry the parallel append-catalog tables.
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
/// Profile-lifetime selected cells, catalog resource-unlock markers, and independent RT.DAT read
/// history. VM opcodes and successful catalog opens mutate this object; explicit Load/Save calls own
/// both filesystem lifecycles.
/// </summary>
public sealed class SharedProfile
{
    private const uint CatalogPrivateExponentMask = 0x87912345;
    private const uint CanonicalCatalogPrivateExponent = 127;
    private const uint CanonicalCatalogPublicExponent = 14_478_031;
    private const uint CanonicalCatalogModulus = 1_838_797_217;

    private uint[] _catalogCompatibilityValues = Array.Empty<uint>();
    private readonly Dictionary<int, uint> _integerCells = new();
    private readonly Dictionary<int, string> _stringCells = new();
    private uint[] _extendedSelectorCounts = Array.Empty<uint>();
    private uint[] _extendedValues = Array.Empty<uint>();
    private uint[] _reservedTail = new uint[SharedProfilePayloadCodec.ReservedTailDwordCount];
    private readonly object _catalogUnlockLock = new();
    private readonly HashSet<uint> _unlockedCatalogResources = new();
    private int _baseCatalogSlotCount;
    private readonly int[] _appendCatalogSlotCounts = new int[SharedProfilePayloadCodec.ExtendedSelectorCount];
    private bool _catalogUnlocksDirty;
    private uint _accumulatedPlaySeconds;

    public IReadOnlyDictionary<int, uint> IntegerCells => _integerCells;
    public IReadOnlyDictionary<int, string> StringCells => _stringCells;
    public uint AccumulatedPlaySeconds => _accumulatedPlaySeconds;
    public ReadTextDatabase ReadText { get; } = new();
    /// <summary>The engine setting manipulated by opcodes 0x1ca/0x1cb.</summary>
    public bool ReadMessageSkipEnabled { get; set; }

    /// <summary>
    /// Set the mounted catalog geometry used when newly opened resources must be written back to
    /// SAVE.DAT. Existing imported unlocks survive when catalogs grow; absent new slots begin locked.
    /// </summary>
    public void ConfigureCatalogUnlockSlots(
        int baseSlotCount,
        IEnumerable<KeyValuePair<int, int>>? appendSlotCounts = null)
    {
        if (baseSlotCount < 0)
            throw new ArgumentOutOfRangeException(nameof(baseSlotCount));

        var configuredAppendCounts = new int[SharedProfilePayloadCodec.ExtendedSelectorCount];
        if (appendSlotCounts != null)
        {
            foreach ((int selector, int count) in appendSlotCounts)
            {
                if (selector is <= 0 or >= 0x80)
                    throw new ArgumentOutOfRangeException(
                        nameof(appendSlotCounts), "Append catalog selectors must be in 1..127.");
                if (count < 0 || count > 0x1000000)
                    throw new ArgumentOutOfRangeException(
                        nameof(appendSlotCounts), "Append catalog slot counts must fit the packed 24-bit index.");
                configuredAppendCounts[selector] = count;
            }
        }

        lock (_catalogUnlockLock)
        {
            bool changed = _baseCatalogSlotCount != baseSlotCount
                           || !_appendCatalogSlotCounts.SequenceEqual(configuredAppendCounts);
            _baseCatalogSlotCount = baseSlotCount;
            configuredAppendCounts.CopyTo(_appendCatalogSlotCounts, 0);
            _catalogUnlocksDirty |= changed;
        }
    }

    /// <summary>AGE marks a resource unlocked only after its catalog entry opens successfully.</summary>
    public void MarkCatalogResourceOpened(long packedId)
    {
        if (packedId < 0 || packedId > uint.MaxValue) return;
        uint id = unchecked((uint)packedId);
        int selector = (int)(id >> 24);
        int index = (int)(id & 0x00ff_ffff);
        if (selector >= 0x80) return;

        lock (_catalogUnlockLock)
        {
            if (selector == 0)
                _baseCatalogSlotCount = Math.Max(_baseCatalogSlotCount, checked(index + 1));
            else
                _appendCatalogSlotCounts[selector] =
                    Math.Max(_appendCatalogSlotCounts[selector], checked(index + 1));
            if (_unlockedCatalogResources.Add(id)) _catalogUnlocksDirty = true;
        }
    }

    /// <summary>Native opcode 0x19d's profile-wide resource-seen predicate.</summary>
    public bool IsCatalogResourceUnlocked(long packedId)
    {
        if (packedId < 0 || packedId > uint.MaxValue) return false;
        uint id = unchecked((uint)packedId);
        if ((id >> 24) >= 0x80) return false;
        lock (_catalogUnlockLock) return _unlockedCatalogResources.Contains(id);
    }

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
            ClearSharedPayload();
            _accumulatedPlaySeconds = 0;
        }
        else
        {
            Replace(SharedProfilePayloadCodec.Decode(document.Payload, document.Metadata));
            _accumulatedPlaySeconds = document.Metadata.AccumulatedPlaySeconds;
        }
        bool readTextLoaded = ReadText.Load(store);
        return document is not null || readTextLoaded;
    }

    public void Save(
        INativeDatStore store,
        NativeSystemTime timestamp,
        uint accumulatedPlaySeconds)
    {
        ArgumentNullException.ThrowIfNull(store);
        NativeSaveMetadata metadata = store.Identity.CreateMetadata(timestamp, accumulatedPlaySeconds);
        SharedProfilePayload snapshot = Snapshot();
        byte[] payload = SharedProfilePayloadCodec.Encode(snapshot, metadata);
        store.SaveShared(payload, timestamp, accumulatedPlaySeconds);
        lock (_catalogUnlockLock)
        {
            _catalogCompatibilityValues = snapshot.CatalogCompatibilityValues.ToArray();
            _extendedSelectorCounts = snapshot.ExtendedSelectorCounts.ToArray();
            _extendedValues = snapshot.ExtendedValues.ToArray();
            _catalogUnlocksDirty = false;
        }
        _accumulatedPlaySeconds = accumulatedPlaySeconds;
        ReadText.Save(store);
    }

    public SharedProfilePayload Snapshot()
    {
        uint[] catalog;
        uint[] selectors;
        uint[] extended;
        lock (_catalogUnlockLock)
        {
            if (_catalogUnlocksDirty)
                (catalog, selectors, extended) = EncodeCatalogUnlockSections();
            else
            {
                catalog = _catalogCompatibilityValues.ToArray();
                selectors = _extendedSelectorCounts.ToArray();
                extended = _extendedValues.ToArray();
            }
        }
        return new SharedProfilePayload(
            catalog,
            _integerCells,
            _stringCells,
            selectors,
            extended,
            _reservedTail);
    }

    public void Replace(SharedProfilePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        lock (_catalogUnlockLock)
        {
            _catalogCompatibilityValues = payload.CatalogCompatibilityValues.ToArray();
            _extendedSelectorCounts = payload.ExtendedSelectorCounts.ToArray();
            _extendedValues = payload.ExtendedValues.ToArray();
            DecodeCatalogUnlockSections();
            _catalogUnlocksDirty = false;
        }
        _integerCells.Clear();
        foreach (var entry in payload.IntegerCells) _integerCells.Add(entry.Key, entry.Value);
        _stringCells.Clear();
        foreach (var entry in payload.StringCells) _stringCells.Add(entry.Key, entry.Value);
        _reservedTail = payload.ReservedTail.ToArray();
    }

    public void Clear()
    {
        ClearSharedPayload();
        ReadText.Clear();
        ReadMessageSkipEnabled = false;
        _accumulatedPlaySeconds = 0;
    }

    private void ClearSharedPayload()
    {
        lock (_catalogUnlockLock)
        {
            _catalogCompatibilityValues = Array.Empty<uint>();
            _extendedSelectorCounts = Array.Empty<uint>();
            _extendedValues = Array.Empty<uint>();
            _unlockedCatalogResources.Clear();
            _baseCatalogSlotCount = 0;
            Array.Clear(_appendCatalogSlotCounts);
            _catalogUnlocksDirty = false;
        }
        _integerCells.Clear();
        _stringCells.Clear();
        _reservedTail = new uint[SharedProfilePayloadCodec.ReservedTailDwordCount];
    }

    private void DecodeCatalogUnlockSections()
    {
        _unlockedCatalogResources.Clear();
        _baseCatalogSlotCount = Math.Max(0, _catalogCompatibilityValues.Length - 2);
        Array.Clear(_appendCatalogSlotCounts);

        DecodeCatalogTable(_catalogCompatibilityValues, _baseCatalogSlotCount, 0, 0);

        int encodedOffset = 0;
        int availableExtendedSlots = Math.Max(0, _extendedValues.Length - 2);
        for (int selector = 0; selector < _extendedSelectorCounts.Length
                                   && selector < _appendCatalogSlotCounts.Length; selector++)
        {
            uint rawCount = _extendedSelectorCounts[selector];
            int count = rawCount > int.MaxValue ? 0 : unchecked((int)rawCount);
            _appendCatalogSlotCounts[selector] = count;
            int readable = Math.Min(count, Math.Max(0, availableExtendedSlots - encodedOffset));
            DecodeCatalogTable(_extendedValues, readable, encodedOffset, selector);
            encodedOffset = checked(encodedOffset + readable);
            if (readable != count) break;
        }
    }

    private void DecodeCatalogTable(
        IReadOnlyList<uint> encoded,
        int count,
        int encodedOffset,
        int selector)
    {
        if (encoded.Count < 2 || encoded[1] == 0) return;
        uint exponent = encoded[0] ^ CatalogPrivateExponentMask;
        uint modulus = encoded[1];
        for (int index = 0; index < count && encodedOffset + index + 2 < encoded.Count; index++)
        {
            uint cipher = encoded[encodedOffset + index + 2];
            if (cipher == 0) continue;
            uint plain = ModularPow(cipher, exponent, modulus);
            if (unchecked((ushort)plain) != CatalogUnlockStamp(index)) continue;
            _unlockedCatalogResources.Add(unchecked(((uint)selector << 24) | (uint)index));
        }
    }

    private (uint[] Catalog, uint[] Selectors, uint[] Extended) EncodeCatalogUnlockSections()
    {
        uint[] catalog = NewEncodedCatalogTable(_baseCatalogSlotCount);
        uint[] selectors = new uint[SharedProfilePayloadCodec.ExtendedSelectorCount];
        int extendedSlotCount = 0;
        for (int selector = 0; selector < selectors.Length; selector++)
        {
            int count = _appendCatalogSlotCounts[selector];
            selectors[selector] = checked((uint)count);
            extendedSlotCount = checked(extendedSlotCount + count);
        }
        uint[] extended = NewEncodedCatalogTable(extendedSlotCount);

        int[] selectorOffsets = new int[selectors.Length];
        int offset = 0;
        for (int selector = 0; selector < selectors.Length; selector++)
        {
            selectorOffsets[selector] = offset;
            offset = checked(offset + _appendCatalogSlotCounts[selector]);
        }

        foreach (uint id in _unlockedCatalogResources)
        {
            int selector = (int)(id >> 24);
            int index = (int)(id & 0x00ff_ffff);
            uint cipher = ModularPow(
                CatalogUnlockStamp(index),
                CanonicalCatalogPublicExponent,
                CanonicalCatalogModulus);
            if (selector == 0)
            {
                if (index < _baseCatalogSlotCount) catalog[index + 2] = cipher;
            }
            else if ((uint)selector < (uint)_appendCatalogSlotCounts.Length
                     && index < _appendCatalogSlotCounts[selector])
            {
                extended[selectorOffsets[selector] + index + 2] = cipher;
            }
        }
        return (catalog, selectors, extended);
    }

    private static uint[] NewEncodedCatalogTable(int slotCount)
    {
        var result = new uint[checked(slotCount + 2)];
        result[0] = CanonicalCatalogPrivateExponent ^ CatalogPrivateExponentMask;
        result[1] = CanonicalCatalogModulus;
        return result;
    }

    private static ushort CatalogUnlockStamp(int index)
        => unchecked((ushort)((uint)index * 0x053d6f99u + 0xb0b0b0b0u));

    private static uint ModularPow(uint value, uint exponent, uint modulus)
    {
        if (modulus == 0) return 0;
        ulong result = 1;
        ulong factor = value % modulus;
        for (int bit = 0; bit < 32; bit++)
        {
            if ((exponent & (1u << bit)) != 0)
                result = result * factor % modulus;
            factor = factor * factor % modulus;
        }
        return unchecked((uint)result);
    }

    private static void ValidateAddress(int address)
    {
        if (address < 0)
            throw new ArgumentOutOfRangeException(nameof(address), "Shared profile cell address cannot be negative.");
    }
}

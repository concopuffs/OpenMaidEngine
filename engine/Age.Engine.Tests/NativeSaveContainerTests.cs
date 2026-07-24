using System.Buffers.Binary;
using System.Text;
using Age.Engine.Persistence;
using Age.Engine.Sys4;

public class NativeSaveContainerTests
{
    private static readonly NativeSystemTime Timestamp =
        new(2026, 7, 5, 24, 13, 42, 17, 321);

    [Fact]
    public void NativeCrcVariantsMatchIndependentCheckVectors()
    {
        byte[] input = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xfc891918u, NativeSaveContainerCodec.Crc32Msb(input));
        Assert.Equal(0xcbf43926u, NativeSaveContainerCodec.Crc32Reflected(input));
    }

    [Fact]
    public void LzssEncoderRoundTripsTheNativeRingDialect()
    {
        byte[] input = Encoding.ASCII.GetBytes(string.Concat(
            Enumerable.Repeat("HIMEGARI-HIMEGARI-0000000000000000-", 80)));
        byte[] encoded = LzssEncoder.EncodeOrVerbatim(input);

        Assert.True(encoded.Length < input.Length);
        Assert.Equal(input, LzssDecoder.Decode(encoded, input.Length, "save test"));

        byte[] incompressible = [1, 9, 2, 8, 3, 7, 4, 6];
        Assert.Equal(incompressible, LzssEncoder.EncodeOrVerbatim(incompressible));
    }

    [Fact]
    public void VersionTwoContainerWritesNativeHeaderFrameAndRoundTrips()
    {
        byte[] payload = Enumerable.Range(0, 256)
            .SelectMany(i => BitConverter.GetBytes(i % 7))
            .ToArray();
        var metadata = new NativeSaveMetadata(
            NativeSaveMagic.S4SD, 0x10203040, "姫狩りDM", Timestamp, 54321, 3, 2);
        var options = new NativeSaveEncodingOptions(0x78563412, 0x1357);

        byte[] file = NativeSaveContainerCodec.Encode(payload, metadata, options);

        Assert.Equal("S4SD", Encoding.ASCII.GetString(file, 0, 4));
        Assert.Equal(0x10203040u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)));
        Assert.Equal((ushort)2026, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x108)));
        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x10a)));
        Assert.Equal((ushort)24, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x10e)));
        Assert.Equal(54321u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x118)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(0x11c)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(0x120)));
        Assert.Equal(0x78563412u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x130)));
        Assert.Equal(0x1357u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x134)));

        byte[] checkedLogical = new byte[payload.Length + 8];
        payload.CopyTo(checkedLogical.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(
            checkedLogical, NativeSaveContainerCodec.Crc32Msb(payload));
        BinaryPrimitives.WriteUInt32LittleEndian(
            checkedLogical.AsSpan(4), NativeSaveContainerCodec.Crc32Reflected(payload));
        byte[] stored = LzssEncoder.EncodeOrVerbatim(checkedLogical);
        int expectedTransformDwords = stored.Length / 4 + 0x0d;
        Assert.Equal(
            checked((uint)(expectedTransformDwords * 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x124)));

        NativeSaveDocument decoded = NativeSaveContainerCodec.Decode(file);
        Assert.Equal(metadata, decoded.Metadata);
        Assert.Equal(payload, decoded.Payload);
        Assert.Equal(file.Length, decoded.BytesConsumed);
        Assert.Equal(metadata, NativeSaveContainerCodec.ReadMetadata(file.AsSpan(0, 0x124)));
    }

    [Fact]
    public void LegacyUncompressedContainerRoundTrips()
    {
        byte[] payload = Enumerable.Range(0, 64).Select(i => (byte)(i * 37)).ToArray();
        var metadata = new NativeSaveMetadata(
            NativeSaveMagic.S3SD, 77, "legacy", Timestamp, 9, 1, 1);

        byte[] file = NativeSaveContainerCodec.Encode(
            payload, metadata, new NativeSaveEncodingOptions(0xabcdef01, 3));
        NativeSaveDocument decoded = NativeSaveContainerCodec.Decode(file);

        Assert.Equal(metadata, decoded.Metadata);
        Assert.Equal(payload, decoded.Payload);
        Assert.Equal((uint)((payload.Length + 8) / 4 * 2),
            BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x124)));

        uint mixed = NativeSaveContainerCodec.Crc32Msb(payload) ^ 0xabcdef01;
        Assert.Equal((mixed >> 16) * 3,
            BinaryPrimitives.ReadUInt32LittleEndian(
                file.AsSpan(NativeSaveContainerCodec.FixedPrefixSize)));
        Assert.Equal((mixed & 0xffff) * 3,
            BinaryPrimitives.ReadUInt32LittleEndian(
                file.AsSpan(NativeSaveContainerCodec.FixedPrefixSize + 4)));
    }

    [Fact]
    public void EncodedChecksumAndExactDivisionRejectCorruption()
    {
        byte[] payload = Enumerable.Range(0, 16).SelectMany(BitConverter.GetBytes).ToArray();
        var metadata = new NativeSaveMetadata(
            NativeSaveMagic.S4SD, 1, "test", Timestamp, 0, 1, 1);
        byte[] file = NativeSaveContainerCodec.Encode(
            payload, metadata, new NativeSaveEncodingOptions(0x12345678, 3));

        byte[] crcFailure = file.ToArray();
        crcFailure[^1] ^= 0x80;
        Assert.Contains("checksum", Assert.Throws<InvalidDataException>(
            () => NativeSaveContainerCodec.Decode(crcFailure)).Message);

        byte[] divisionFailure = file.ToArray();
        int encodedOffset = NativeSaveContainerCodec.FixedPrefixSize;
        uint firstProduct = BinaryPrimitives.ReadUInt32LittleEndian(divisionFailure.AsSpan(encodedOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(divisionFailure.AsSpan(encodedOffset), firstProduct + 1);
        ReadOnlySpan<byte> encoded = divisionFailure.AsSpan(encodedOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            divisionFailure.AsSpan(0x128), NativeSaveContainerCodec.Crc32Msb(encoded));
        BinaryPrimitives.WriteUInt32LittleEndian(
            divisionFailure.AsSpan(0x12c), NativeSaveContainerCodec.Crc32Reflected(encoded));

        Assert.Contains("divisible", Assert.Throws<InvalidDataException>(
            () => NativeSaveContainerCodec.Decode(divisionFailure)).Message);
    }

    [Fact]
    public void IdentityUsesNativeLayoutTwoVersionCompatibility()
    {
        var identity = new NativeSaveIdentity(NativeSaveMagic.S4SD, 9, "game", 2, 10);
        identity.Validate(identity.CreateMetadata(Timestamp, 0) with { SaveVersion2 = 20 });

        Assert.Throws<InvalidDataException>(() =>
            identity.Validate(identity.CreateMetadata(Timestamp, 0) with { SaveVersion1 = 3 }));
        Assert.Throws<InvalidDataException>(() =>
            identity.Validate(identity.CreateMetadata(Timestamp, 0) with { GameId = "other" }));
    }

    [Fact]
    public void DirectoryStoreKeepsSharedBackupFallbackAndDirectNumberedFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "age-native-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            var identity = new NativeSaveIdentity(NativeSaveMagic.S4SD, 123, "himegari-test", 3, 2);
            var store = new DirectoryNativeDatStore(root, identity);
            byte[] first = Enumerable.Repeat((byte)0x11, 64).ToArray();
            byte[] second = Enumerable.Repeat((byte)0x22, 64).ToArray();
            byte[] numbered = Enumerable.Repeat((byte)0x33, 64).ToArray();

            store.SaveShared(first, Timestamp, 10);
            Assert.True(File.Exists(Path.Combine(root, "SAVE.DAT")));
            Assert.False(File.Exists(Path.Combine(root, "$$SAVE.DAT")));
            Assert.False(File.Exists(Path.Combine(root, "SAVE.BAK")));

            store.SaveShared(second, Timestamp, 20);
            Assert.Equal(second, store.LoadShared()!.Payload);
            Assert.True(File.Exists(Path.Combine(root, "SAVE.BAK")));

            string primary = Path.Combine(root, "SAVE.DAT");
            byte[] corrupt = File.ReadAllBytes(primary);
            corrupt[^1] ^= 1;
            File.WriteAllBytes(primary, corrupt);
            Assert.Equal(first, store.LoadShared()!.Payload);

            store.SaveNumbered(4, numbered, Timestamp, 30);
            Assert.True(File.Exists(Path.Combine(root, "SAVE04.DAT")));
            Assert.Equal(numbered, store.LoadNumbered(4)!.Payload);
            Assert.Null(store.LoadNumbered(5));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

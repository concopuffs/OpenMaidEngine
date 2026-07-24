using System.Buffers.Binary;
using System.Text;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class ReadTextDatabaseTests
{
    private static readonly NativeSaveIdentity Identity =
        new(NativeSaveMagic.S4SD, 0x4a343234, "姫狩りダンジョンマイスター", 3, 10);
    private static readonly NativeSystemTime Timestamp =
        new(2026, 7, 5, 24, 13, 42, 17, 321);

    [Fact]
    public void S3rtCodecWritesNativeHeaderRecordTableAndFlagArrays()
    {
        var snapshot = new ReadTextDatabaseSnapshot(
        [
            new ReadTextScriptRecord(0x22, new uint[] { 1, 0, 1 }),
            new ReadTextScriptRecord(0x01000005, new uint[] { 0, 7 }),
        ]);

        byte[] encoded = ReadTextDatabaseCodec.Encode(snapshot, Identity);

        Assert.Equal(ReadTextDatabaseCodec.HeaderSize + 2 * 12 + 5 * 4, encoded.Length);
        Assert.Equal("S3RT", Encoding.ASCII.GetString(encoded, 0, 4));
        Assert.Equal(Identity.CompatibilityId,
            BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0x108)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0x10c)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0x110)));
        Assert.Equal(0x22u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0x114)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0x118)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0x11c)));

        // Native AGE serializes its live heap pointer in word three. The loader ignores that value,
        // allocates a new array, and overwrites it, so import must tolerate a nonzero original pointer.
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(0x11c), 0x06dbdb78);
        ReadTextDatabaseSnapshot decoded = ReadTextDatabaseCodec.Decode(encoded, Identity);

        Assert.Equal(new uint[] { 1, 0, 1 }, decoded.Records[0x22]);
        Assert.Equal(new uint[] { 0, 7 }, decoded.Records[0x01000005]);
    }

    [Fact]
    public void S3rtCodecRejectsIdentityMismatchTruncationAndTrailingData()
    {
        byte[] encoded = ReadTextDatabaseCodec.Encode(
            new ReadTextDatabaseSnapshot(
                [new ReadTextScriptRecord(7, new uint[] { 1, 0 })]),
            Identity);

        var wrongIdentity = Identity with { CompatibilityId = 1 };
        Assert.Contains("compatibility", Assert.Throws<InvalidDataException>(
            () => ReadTextDatabaseCodec.Decode(encoded, wrongIdentity)).Message);

        Assert.Contains("truncated", Assert.Throws<InvalidDataException>(
            () => ReadTextDatabaseCodec.Decode(encoded.AsSpan(0, encoded.Length - 4), Identity)).Message);

        byte[] trailing = [.. encoded, 0, 0, 0, 0];
        Assert.Contains("trailing", Assert.Throws<InvalidDataException>(
            () => ReadTextDatabaseCodec.Decode(trailing, Identity)).Message);
    }

    [Fact]
    public void InstalledNativeRtDatRoundTripsItsLogicalRecordsWhenPresent()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Eushully", "姫狩りダンジョンマイスター", "SAVE", "RT.DAT");
        if (!File.Exists(path)) return;

        ReadTextDatabaseSnapshot imported =
            ReadTextDatabaseCodec.Decode(File.ReadAllBytes(path), Identity);
        ReadTextDatabaseSnapshot roundTripped = ReadTextDatabaseCodec.Decode(
            ReadTextDatabaseCodec.Encode(imported, Identity), Identity);
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        Script sc0000 = Sys4ScriptProvider.Load(table).RequireByName("SC0000.BIN");

        Assert.NotEmpty(imported.Records);
        Assert.Equal(0x22u, sc0000.PackedId);
        Assert.Equal(sc0000.ReadMessageOffsets.Count, imported.Records[sc0000.PackedId].Count);
        Assert.Equal(imported.Records.Keys.Order(), roundTripped.Records.Keys.Order());
        foreach (var record in imported.Records)
            Assert.Equal(record.Value, roundTripped.Records[record.Key]);
    }

    [Fact]
    public void ReadTextDatabaseQueuesThenCommitsWithNativeGrowthRules()
    {
        var database = new ReadTextDatabase();

        database.QueueMessage(5, 1, 2);
        Assert.False(database.IsMessageRead(5, 1));
        Assert.Equal(1, database.PendingCount);
        database.CommitPending();
        Assert.True(database.IsMessageRead(5, 1));

        database.QueueMessage(5, 3, 4);
        database.QueueMessage(5, -1, 4);
        database.CommitPending();

        Assert.True(database.IsMessageRead(5, 1));
        Assert.True(database.IsMessageRead(5, 3));
        Assert.False(database.IsMessageRead(5, 2));
        Assert.Equal(4, database.Snapshot().Records[5].Count);
    }

    [Fact]
    public void SharedProfilePersistsSaveDatAndReadTextDatThroughPairedTransactions()
    {
        string root = Path.Combine(Path.GetTempPath(), "age-read-text-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            var profile = new SharedProfile();
            var selectors = new uint[SharedProfilePayloadCodec.ExtendedSelectorCount];
            profile.Replace(new SharedProfilePayload(extendedSelectorCounts: selectors));
            profile.StoreInteger(0x123, 9);
            profile.ReadText.QueueMessage(0x22, 2, 4);
            profile.ReadText.CommitPending();

            profile.Save(store, Timestamp, 10);

            Assert.True(File.Exists(Path.Combine(root, DirectoryNativeDatStore.SharedFileName)));
            Assert.True(File.Exists(Path.Combine(root, DirectoryNativeDatStore.ReadTextFileName)));
            var loaded = new SharedProfile();
            Assert.True(loaded.Load(store));
            Assert.Equal(9, loaded.LoadInteger(0x123));
            Assert.True(loaded.ReadText.IsMessageRead(0x22, 2));

            loaded.ReadText.QueueMessage(0x22, 3, 4);
            loaded.ReadText.CommitPending();
            loaded.Save(store, Timestamp, 20);
            Assert.True(File.Exists(Path.Combine(root, DirectoryNativeDatStore.ReadTextBackupFileName)));
            Assert.True(store.LoadReadText()!.Records[0x22][3] != 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AdvOpcodesUseT1CoordinatesAndShareReadStateAcrossFreshVms()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(int value) => new(0, value);
        static Operand S(int value) => new(2, value);
        static Operand G(int value) => new(3, value);
        Script script = ScriptAssembler.Assemble(table, "READ_TEXT_TEST",
        [
            (0x1ca, new[] { I(1) }),
            (0x1cb, new[] { G(0x100) }),
            (0x71, new[] { I(1) }),
            (0x6e, new[] { I(1), S(0) }),
            (0x1cc, new[] { G(0x101) }),
            (0x72, new[] { I(1) }),
            (0x71, new[] { I(1) }),
            (0x6e, new[] { I(1), S(1) }),
            (0x1cc, new[] { G(0x102) }),
            (0x72, new[] { I(1) }),
            (0x71, new[] { I(1) }),
            (0x2, Array.Empty<Operand>()),
        ], ["first", "second"]);
        var profile = new SharedProfile();

        var first = new VirtualMachine(
            script, table, new RecordingHost(), sharedProfile: profile);
        first.Run();

        Assert.Equal(1, first.Globals[0x100]);
        Assert.Equal(0, first.Globals[0x101]);
        Assert.Equal(0, first.Globals[0x102]);
        Assert.Equal(3, script.ReadMessageOffsets.Count);
        Assert.True(profile.ReadText.IsMessageRead(script.PackedId, 0));
        Assert.True(profile.ReadText.IsMessageRead(script.PackedId, 1));

        var secondHost = new RecordingHost();
        var second = new VirtualMachine(
            script, table, secondHost, sharedProfile: profile);
        second.Run();

        Assert.Equal(1, second.Globals[0x101]);
        Assert.Equal(1, second.Globals[0x102]);
        Assert.Contains(true, secondHost.MessageSkipChanges);
    }
}

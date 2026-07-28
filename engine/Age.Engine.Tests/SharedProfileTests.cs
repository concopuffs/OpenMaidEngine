using System.Buffers.Binary;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class SharedProfileTests
{
    private const int Immediate = 0, InlineString = 2, GlobalInt = 3, GlobalString = 5,
                      LocalPointer = 12, LocalStringPointer = 14;
    private static readonly NativeSystemTime Timestamp =
        new(2026, 7, 5, 24, 13, 42, 17, 321);

    [Fact]
    public void SharedPayloadRoundTripsNativeTypedKeysAndOpaqueSections()
    {
        var selectors = new uint[SharedProfilePayloadCodec.ExtendedSelectorCount];
        selectors[1] = 81;
        var payload = new SharedProfilePayload(
            catalogCompatibilityValues: [0x11223344, 0xaabbccdd],
            integerCells: new Dictionary<int, uint> { [0x1234] = 0xffffffff },
            stringCells: new Dictionary<int, string> { [0x5678] = "姫狩り\0ignored" },
            extendedSelectorCounts: selectors,
            extendedValues: [7, 8, 9]);
        var metadata = new NativeSaveMetadata(
            NativeSaveMagic.S4SD, 123, "himegari-test", Timestamp, 42, 3, 10);

        byte[] logical = SharedProfilePayloadCodec.Encode(payload, metadata);

        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(logical));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(logical.AsSpan(12)));
        Assert.Equal(0x03, logical[16]);
        Assert.Equal("00001234", System.Text.Encoding.ASCII.GetString(logical, 17, 8));
        Assert.Equal(0, logical[25]);
        Assert.Equal(0xffffffffu, BinaryPrimitives.ReadUInt32LittleEndian(logical.AsSpan(28)));

        byte[] file = NativeSaveContainerCodec.Encode(
            logical, metadata, new NativeSaveEncodingOptions(0x12345678, 3));
        NativeSaveDocument document = NativeSaveContainerCodec.Decode(file);
        SharedProfilePayload decoded =
            SharedProfilePayloadCodec.Decode(document.Payload, document.Metadata);

        Assert.Equal(payload.CatalogCompatibilityValues, decoded.CatalogCompatibilityValues);
        Assert.Equal(0xffffffffu, decoded.IntegerCells[0x1234]);
        Assert.Equal("姫狩り", decoded.StringCells[0x5678]);
        Assert.Equal(81u, decoded.ExtendedSelectorCounts[1]);
        Assert.Equal(new uint[] { 7, 8, 9 }, decoded.ExtendedValues);
        Assert.Equal(9, decoded.ReservedTail.Count);
        Assert.All(decoded.ReservedTail, value => Assert.Equal(0u, value));
    }

    [Fact]
    public void SharedPayloadRejectsMalformedTypedKeysAndTrailingData()
    {
        var metadata = new NativeSaveMetadata(
            NativeSaveMagic.S4SD, 1, "test", Timestamp, 0, 1, 1);
        byte[] logical = SharedProfilePayloadCodec.Encode(
            new SharedProfilePayload(integerCells: new Dictionary<int, uint> { [1] = 2 }),
            metadata);

        byte[] badType = logical.ToArray();
        badType[8] = 0x05;
        Assert.Contains("type tag", Assert.Throws<InvalidDataException>(
            () => SharedProfilePayloadCodec.Decode(badType, metadata)).Message);

        byte[] trailing = [.. logical, 0, 0, 0, 0];
        Assert.Contains("trailing", Assert.Throws<InvalidDataException>(
            () => SharedProfilePayloadCodec.Decode(trailing, metadata)).Message);
    }

    [Fact]
    public void SharedProfilePersistsSelectedCellsThroughNativeDirectoryStore()
    {
        string root = Path.Combine(Path.GetTempPath(), "age-shared-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var identity = new NativeSaveIdentity(
                NativeSaveMagic.S4SD, 123, "himegari-test", 3, 10);
            var store = new DirectoryNativeDatStore(root, identity);
            var profile = new SharedProfile();
            var selectors = new uint[SharedProfilePayloadCodec.ExtendedSelectorCount];
            selectors[1] = 81;
            profile.Replace(new SharedProfilePayload(
                catalogCompatibilityValues: [10, 20, 30],
                extendedSelectorCounts: selectors,
                extendedValues: [99]));
            profile.StoreInteger(0x100, -7);
            profile.StoreString(0x200, "リリィ");

            profile.Save(store, Timestamp, 456);

            var loaded = new SharedProfile();
            Assert.True(loaded.Load(store));
            Assert.Equal(-7, loaded.LoadInteger(0x100));
            Assert.Equal("リリィ", loaded.LoadString(0x200));
            Assert.Equal(0, loaded.LoadInteger(0x101));
            Assert.Equal(string.Empty, loaded.LoadString(0x201));
            Assert.Equal(new uint[] { 10, 20, 30 },
                loaded.Snapshot().CatalogCompatibilityValues);
            Assert.Equal(81u, loaded.Snapshot().ExtendedSelectorCounts[1]);
            Assert.Equal(456u, store.LoadShared()!.Metadata.AccumulatedPlaySeconds);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectedCellOpcodesUseResolvedGlobalAddressesAndNativeMissDefaults()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int move = table.ByLabel("mov")!.Value;
        int lookup = table.ByLabel("lookup-array")!.Value;
        Script script = ScriptAssembler.Assemble(table, "SHARED_PROFILE_OPS", new List<(int, Operand[])>
        {
            (move, [new Operand(GlobalInt, 0x700), new Operand(Immediate, 123)]),
            (0x1a2, [new Operand(GlobalInt, 0x700)]),
            (move, [new Operand(GlobalInt, 0x700), new Operand(Immediate, 0)]),
            (0x1a3, [new Operand(GlobalInt, 0x700)]),
            (0x1a3, [new Operand(GlobalInt, 0x701)]),

            (lookup, [
                new Operand(LocalPointer, 0), new Operand(GlobalInt, 0x710), new Operand(Immediate, 2),
            ]),
            (move, [new Operand(LocalPointer, 0), new Operand(Immediate, 77)]),
            (0x1a2, [new Operand(LocalPointer, 0)]),
            (move, [new Operand(LocalPointer, 0), new Operand(Immediate, 0)]),
            (0x1a3, [new Operand(LocalPointer, 0)]),

            (move, [new Operand(GlobalString, 0x800), new Operand(InlineString, 0)]),
            (0x1a9, [new Operand(GlobalString, 0x800)]),
            (move, [new Operand(GlobalString, 0x800), new Operand(InlineString, 1)]),
            (0x1aa, [new Operand(GlobalString, 0x800)]),
            (0x1aa, [new Operand(GlobalString, 0x801)]),

            (0x63, [new Operand(LocalStringPointer, 0), new Operand(GlobalString, 0x810)]),
            (move, [new Operand(LocalStringPointer, 0), new Operand(InlineString, 2)]),
            (0x1a9, [new Operand(LocalStringPointer, 0)]),
            (move, [new Operand(LocalStringPointer, 0), new Operand(InlineString, 1)]),
            (0x1aa, [new Operand(LocalStringPointer, 0)]),
            (0x2, []),
        }, ["リリィ", "changed", "使い魔"]);
        var profile = new SharedProfile();
        var vm = new VirtualMachine(
            script, table, new RecordingHost(), sharedProfile: profile);
        vm.Globals[0x701] = 999;
        vm.GlobalStrings[0x801] = "not empty";

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(123, vm.Globals[0x700]);
        Assert.Equal(0, vm.Globals[0x701]);
        Assert.Equal(77, vm.Globals[0x712]);
        Assert.Equal("リリィ", vm.GlobalStrings[0x800]);
        Assert.Equal(string.Empty, vm.GlobalStrings[0x801]);
        Assert.Equal("使い魔", vm.GlobalStrings[0x810]);
        Assert.Equal(123, profile.LoadInteger(0x700));
        Assert.Equal(77, profile.LoadInteger(0x712));
        Assert.Equal("リリィ", profile.LoadString(0x800));
        Assert.Equal("使い魔", profile.LoadString(0x810));
    }

    [Fact]
    public void GameSessionCarriesSharedProfileAcrossFreshSceneVmsButJsonDoesNotAliasIt()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        int move = table.ByLabel("mov")!.Value;
        Script store = ScriptAssembler.Assemble(table, "PROFILE_STORE", new List<(int, Operand[])>
        {
            (move, [new Operand(GlobalInt, 0x900), new Operand(Immediate, 42)]),
            (0x1a2, [new Operand(GlobalInt, 0x900)]),
            (0x2, []),
        }, []);
        Script load = ScriptAssembler.Assemble(table, "PROFILE_LOAD", new List<(int, Operand[])>
        {
            (move, [new Operand(GlobalInt, 0x900), new Operand(Immediate, 0)]),
            (0x1a3, [new Operand(GlobalInt, 0x900)]),
            (0x2, []),
        }, []);
        var session = new GameSession();

        session.RunScene(store, table, new RecordingHost());
        session.RunScene(load, table, new RecordingHost());

        Assert.Equal(42, session.Globals[0x900]);
        Assert.Equal(42, session.SharedProfile.LoadInteger(0x900));
        GameSession jsonClone = GameSession.FromJson(session.ToJson());
        Assert.Equal(42, jsonClone.Globals[0x900]);
        Assert.Equal(0, jsonClone.SharedProfile.LoadInteger(0x900));
    }

    [Fact]
    public void CatalogUnlockMarkersRoundTripBaseAndAppendResources()
    {
        var profile = new SharedProfile();
        profile.ConfigureCatalogUnlockSlots(
            6000,
            new Dictionary<int, int> { [1] = 32 });
        profile.MarkCatalogResourceOpened(5407);
        profile.MarkCatalogResourceOpened(0x01000011);

        SharedProfilePayload encoded = profile.Snapshot();

        Assert.Equal(6002, encoded.CatalogCompatibilityValues.Count);
        Assert.Equal(0x8791233au, encoded.CatalogCompatibilityValues[0]);
        Assert.Equal(0x6868a8e1u, encoded.CatalogCompatibilityValues[5407 + 2]);
        Assert.Equal(32u, encoded.ExtendedSelectorCounts[1]);
        Assert.Equal(34, encoded.ExtendedValues.Count);
        Assert.Equal(0x42d5ee4eu, encoded.ExtendedValues[17 + 2]);

        var reloaded = new SharedProfile();
        reloaded.Replace(encoded);

        Assert.True(reloaded.IsCatalogResourceUnlocked(5407));
        Assert.True(reloaded.IsCatalogResourceUnlocked(0x01000011));
        Assert.False(reloaded.IsCatalogResourceUnlocked(5406));
        Assert.False(reloaded.IsCatalogResourceUnlocked(0x01000012));
        Assert.False(reloaded.IsCatalogResourceUnlocked(0x80000000));
    }

    [Fact]
    public void CatalogUnlockOpcodeReturnsImportedProfilePredicate()
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        Script script = ScriptAssembler.Assemble(table, "CATALOG_UNLOCK_QUERY", new List<(int, Operand[])>
        {
            (0x19d, [new Operand(GlobalInt, 0x900), new Operand(Immediate, 5407)]),
            (0x19d, [new Operand(GlobalInt, 0x901), new Operand(Immediate, 5406)]),
            (0x19d, [new Operand(GlobalInt, 0x902), new Operand(Immediate, 0x01000011)]),
            (0x2, []),
        }, []);
        var profile = new SharedProfile();
        profile.ConfigureCatalogUnlockSlots(6000, new Dictionary<int, int> { [1] = 32 });
        profile.MarkCatalogResourceOpened(5407);
        profile.MarkCatalogResourceOpened(0x01000011);
        var vm = new VirtualMachine(
            script, table, new RecordingHost(), sharedProfile: profile);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[0x900]);
        Assert.Equal(0, vm.Globals[0x901]);
        Assert.Equal(1, vm.Globals[0x902]);
    }

    [Fact]
    public void TrackingStoreMarksOnlySuccessfulCatalogOpens()
    {
        var profile = new SharedProfile();
        profile.ConfigureCatalogUnlockSlots(8);
        var tracker = new CatalogTrackingAssetStore(
            new SelectiveAssetStore(),
            entry => profile.MarkCatalogResourceOpened(entry.PackedId));
        var available = new AssetEntry("A.BIN", "DATA1.ALF", 0, 1, RawIndex: 3);
        var missing = new AssetEntry("MISSING.BIN", "DATA1.ALF", 0, 1, RawIndex: 4);

        Assert.Equal(new byte[] { 42 }, tracker.ReadAll(available));
        Assert.Throws<FileNotFoundException>(() => tracker.ReadAll(missing));

        Assert.True(profile.IsCatalogResourceUnlocked(3));
        Assert.False(profile.IsCatalogResourceUnlocked(4));
    }

    [Fact]
    public void InstalledSharedProfileUnlocksKnownCgAndHSceneResourcesWhenPresent()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Eushully", "姫狩りダンジョンマイスター", "SAVE");
        if (!File.Exists(Path.Combine(root, "SAVE.DAT"))) return;

        var store = new DirectoryNativeDatStore(
            root,
            new NativeSaveIdentity(
                NativeSaveMagic.S4SD, 0x4a343234, "姫狩りダンジョンマイスター", 3, 10));
        var profile = new SharedProfile();

        Assert.True(profile.Load(store));
        Assert.True(profile.IsCatalogResourceUnlocked(0x3324)); // ED.AGF

        using var cgDocument = System.Text.Json.JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(Paths.Build, "data", "CGINIT.json")));
        uint[] cgIds = cgDocument.RootElement.GetProperty("records").EnumerateArray()
            .Select(record => record.GetProperty("gallery_image_asset_id").GetUInt32())
            .ToArray();
        Assert.Equal(851, cgIds.Length);
        Assert.All(cgIds, id => Assert.True(
            profile.IsCatalogResourceUnlocked(id), $"CG resource 0x{id:x} should be unlocked"));

        using var hDocument = System.Text.Json.JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(Paths.Build, "data", "SPINIT.json")));
        uint[] hSceneIds = hDocument.RootElement.GetProperty("records").EnumerateArray()
            .SelectMany(page => page.GetProperty("script_resource_ids").EnumerateArray())
            .Select(id => id.GetUInt32())
            .Where(id => id != 0)
            .ToArray();
        Assert.Equal(118, hSceneIds.Length);
        Assert.All(hSceneIds, id => Assert.True(
            profile.IsCatalogResourceUnlocked(id), $"H-scene resource 0x{id:x} should be unlocked"));

        int newlyOpenedId = Enumerable.Range(0, 13_208)
            .First(id => !profile.IsCatalogResourceUnlocked(id));
        profile.MarkCatalogResourceOpened(newlyOpenedId);
        string roundTripRoot = Path.Combine(
            Path.GetTempPath(), "age-installed-profile-unlocks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var roundTripStore = new DirectoryNativeDatStore(roundTripRoot, store.Identity);
            profile.Save(roundTripStore, Timestamp, profile.AccumulatedPlaySeconds);
            var reloaded = new SharedProfile();

            Assert.True(reloaded.Load(roundTripStore));
            Assert.True(reloaded.IsCatalogResourceUnlocked(newlyOpenedId));
            Assert.All(cgIds, id => Assert.True(reloaded.IsCatalogResourceUnlocked(id)));
            Assert.All(hSceneIds, id => Assert.True(reloaded.IsCatalogResourceUnlocked(id)));
        }
        finally
        {
            if (Directory.Exists(roundTripRoot)) Directory.Delete(roundTripRoot, recursive: true);
        }
    }

    private sealed class SelectiveAssetStore : IAssetStore
    {
        public Stream Open(AssetEntry entry)
        {
            if (entry.Name == "MISSING.BIN") throw new FileNotFoundException();
            return new MemoryStream([42], writable: false);
        }

        public byte[] ReadAll(AssetEntry entry)
        {
            using Stream stream = Open(entry);
            var result = new byte[stream.Length];
            stream.ReadExactly(result);
            return result;
        }
    }
}

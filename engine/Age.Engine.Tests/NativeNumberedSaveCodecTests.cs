using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using System.Buffers.Binary;

public class NativeNumberedSaveCodecTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public NativeNumberedSaveCodecTests(Xunit.Abstractions.ITestOutputHelper output)
        => _output = output;

    [Theory]
    [InlineData(0x2d4, 0xb54)]
    [InlineData(0x2e4, 0xb94)]
    public void LayoutThreeRoundTripsNativeBanksFramesAndGfxRecords(int recordSize, int nativeEntryStride)
    {
        var frames = new[]
        {
            new NativeSavedScriptFrame(-1, 0, new[] { 2, 4 }, 8, 7),
            new NativeSavedScriptFrame(0, 0x3389, Array.Empty<int>(), 3, -1),
        };
        NativeNumberedSaveState state = NativeNumberedSaveCodec.Empty(frames, recordSize) with
        {
            SavedFrameOwner = 7,
            BgmTrackId = 9,
            SoundEffectResourceIds = Enumerable.Range(10, 10).ToArray(),
            IntegerGlobals = new[] { 12, -3, 0x12345678 },
            FloatGlobals = new[] { BitConverter.SingleToInt32Bits(1.25f) },
            StringGlobals = new[] { "姫狩り", "", "save" },
            PointerGlobals = new[] { 2 },
            PointerStrings = new[] { 0 },
            LocalPointerScratch = new[] { -1 },
            GfxObjects =
            [
                new NativeSavedGfxObject(0xcf08,
                    Enumerable.Range(0, recordSize)
                        .Select(i => unchecked((byte)i)).ToArray()),
                new NativeSavedGfxObject(0xcf09,
                    Enumerable.Repeat((byte)0xa5, recordSize).ToArray()),
            ],
            RangeTransformFirst = 100,
            RangeTransformCount = 4,
            RangeTransformRecord = Enumerable.Repeat((byte)0x5a,
                recordSize).ToArray(),
        };

        byte[] encoded = NativeNumberedSaveCodec.Encode(state);
        NativeNumberedSaveState decoded = NativeNumberedSaveCodec.Decode(encoded);

        Assert.Equal(state.SavedFrameOwner, decoded.SavedFrameOwner);
        Assert.Equal(state.BgmTrackId, decoded.BgmTrackId);
        Assert.Equal(state.SoundEffectResourceIds, decoded.SoundEffectResourceIds);
        Assert.Equal(state.Frames[0].ParentContext, decoded.Frames[0].ParentContext);
        Assert.Equal(state.Frames[0].ScriptId, decoded.Frames[0].ScriptId);
        Assert.Equal(state.Frames[0].ReturnIndices, decoded.Frames[0].ReturnIndices);
        Assert.Equal(state.Frames[0].ResumeIndex, decoded.Frames[0].ResumeIndex);
        Assert.Equal(state.Frames[0].CallTargetIndex, decoded.Frames[0].CallTargetIndex);
        Assert.Equal(-1, decoded.Frames[1].CallTargetIndex);
        Assert.Equal(state.IntegerGlobals, decoded.IntegerGlobals);
        Assert.Equal(state.FloatGlobals, decoded.FloatGlobals);
        Assert.Equal(state.StringGlobals, decoded.StringGlobals);
        Assert.Equal(state.PointerGlobals, decoded.PointerGlobals);
        Assert.Equal(state.GfxObjects[0].Handle, decoded.GfxObjects[0].Handle);
        Assert.Equal(state.GfxObjects[0].Record, decoded.GfxObjects[0].Record);
        Assert.Equal(state.GfxObjects[1].Handle, decoded.GfxObjects[1].Handle);
        Assert.Equal(state.GfxObjects[1].Record, decoded.GfxObjects[1].Record);
        Assert.False(decoded.LegacyTightGfxLayout);
        Assert.Equal(state.RangeTransformRecord, decoded.RangeTransformRecord);

        Assert.Equal(recordSize, decoded.GraphicsRecordSize);
        int objectsAt = FindGfxObjects(encoded, 2, 0xcf08, recordSize);
        Assert.Equal(0xcf08, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(objectsAt)));
        Assert.Equal(0xcf09, BinaryPrimitives.ReadInt32LittleEndian(
            encoded.AsSpan(objectsAt + nativeEntryStride)));
        Assert.Throws<InvalidDataException>(() => NativeNumberedSaveCodec.Decode(
            encoded, recordSize == 0x2d4 ? 0x2e4 : 0x2d4));
        Assert.Throws<InvalidDataException>(() => NativeNumberedSaveCodec.Decode(
            encoded.AsSpan(0, objectsAt + nativeEntryStride + 8)));
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(objectsAt - 8), 0x2f4);
        Assert.Throws<InvalidDataException>(() => NativeNumberedSaveCodec.Decode(encoded));
    }

    [Fact]
    public void HistoryTailRoundTripsLogicalEntriesRecordsAndCp932Text()
    {
        var history = new AdvTextHistory();
        history.DefineLayout(1, 400, 120, 75, 340);
        history.SetCursor(1, 8, 12);
        history.AppendText(1, 0x123, "セーブ履歴", AdvTextStyle.Default with
        {
            PrimaryFontSize = 24,
            TextColor = 0xff112233,
        });
        history.AppendMetadata(42, 7, AdvTextStyle.Default);

        byte[] encoded = NativeTextHistoryCodec.Encode(history);
        var restored = new AdvTextHistory();
        NativeTextHistoryCodec.DecodeInto(encoded, restored);

        Assert.Equal(history.Entries, restored.Entries);
        Assert.Equal(2, restored.Records.Count);
        Assert.Equal("セーブ履歴", restored.Records[0].Text);
        Assert.Equal(42, restored.Records[1].Value);
        Assert.Equal(7, restored.Records[1].AuxValue);
        Assert.Equal(AdvTextHistoryRecordKind.Metadata, restored.Records[1].Kind);
    }

    [Fact]
    public void HistoryTailRestorePreservesInitializedLiveLayoutBindings()
    {
        var savedHistory = new AdvTextHistory();
        savedHistory.DefineLayout(1, 400, 120, 75, 340);
        savedHistory.AppendText(1, 0x123, "保存履歴", AdvTextStyle.Default);

        var liveHistory = new AdvTextHistory();
        liveHistory.DefineLayout(3, 320, 90, 20, 400);
        liveHistory.SetResetCursor(3, 45, 42);
        liveHistory.SetBounds(3, 300, 80);
        liveHistory.SetWaitIndicatorObjectHandle(3, 0x1234);
        liveHistory.SetTextObjectRange(3, 0x2000, 500);

        NativeTextHistoryCodec.DecodeInto(NativeTextHistoryCodec.Encode(savedHistory), liveHistory);

        Assert.Equal(savedHistory.Entries, liveHistory.Entries);
        Assert.Equal(savedHistory.Records.Select(record => record.Text),
                     liveHistory.Records.Select(record => record.Text));
        Assert.Equal(3, liveHistory.CurrentLayoutSlot);
        Assert.Equal(
            new AdvTextLayoutSnapshot(3, 320, 90, 20, 400, 0, 0, 300, 80),
            liveHistory.GetLayoutSnapshot(3));
        Assert.Equal(
            new AdvTextLayoutPresentationBinding(3, 0x17, 0x2000, 500, 0x1234, 45, 42),
            liveHistory.GetPresentationBinding(3));
    }

    [Fact]
    public void DirectoryStorePreservesHistoryTailAfterNativeContainer()
    {
        string root = Path.Combine(Path.GetTempPath(), "age-numbered-tail-" + Guid.NewGuid().ToString("N"));
        try
        {
            var identity = new NativeSaveIdentity(
                NativeSaveMagic.S4SD, 1, "test", 3, 10, NumberedCompatibilityId: 2);
            var store = new DirectoryNativeDatStore(root, identity);
            byte[] payload = NativeNumberedSaveCodec.Encode(NativeNumberedSaveCodec.Empty(
                [new NativeSavedScriptFrame(-1, 0, Array.Empty<int>(), -1, -1)]));
            byte[] tail = [1, 2, 3, 4, 5];

            store.SaveNumberedFile(
                3, payload, tail,
                new NativeSystemTime(2026, 7, 5, 24, 12, 30, 0, 0), 99);
            NativeNumberedSaveFile loaded = store.LoadNumberedFile(3)!;

            Assert.Equal(payload, loaded.Document.Payload);
            Assert.Equal(tail, loaded.HistoryTail);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstalledHimegariLayoutThreeAndHistoryTailDecodeWhenPresent()
    {
        string eushullyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eushully");
        if (!Directory.Exists(eushullyRoot)) return;
        string? path = Directory.EnumerateFiles(
                eushullyRoot, "SAVE00.DAT", SearchOption.AllDirectories)
            .FirstOrDefault(candidate =>
            {
                try
                {
                    NativeSaveMetadata metadata = NativeSaveContainerCodec.ReadMetadata(
                        File.ReadAllBytes(candidate));
                    return metadata.CompatibilityId == 0x42323234
                        && metadata.SaveVersion1 == 3 && metadata.SaveVersion2 == 10;
                }
                catch (Exception error) when (
                    error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    return false;
                }
            });
        if (path == null) return;

        byte[] source = File.ReadAllBytes(path);
        NativeSaveDocument document = NativeSaveContainerCodec.Decode(source);
        NativeNumberedSaveState state = NativeNumberedSaveCodec.Decode(document.Payload);
        var history = new AdvTextHistory();
        NativeTextHistoryCodec.DecodeInto(source.AsSpan(document.BytesConsumed), history);

        Assert.NotEmpty(state.Frames);
        Assert.Equal(402459, state.IntegerGlobals.Count);
        Assert.Equal(789, state.StringGlobals.Count);
        Assert.Equal(NativeNumberedSaveState.GfxRecordSize, state.RangeTransformRecord.Length);
        ReadOnlySpan<byte> systemChoiceAtlas = state.SurfaceRecords.AsSpan(15 * 20, 20);
        Assert.Equal(0x3383, BinaryPrimitives.ReadInt32LittleEndian(systemChoiceAtlas));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(systemChoiceAtlas[8..]));
        Assert.All(
            Enumerable.Range(0, 1000),
            slot => Assert.Equal(
                0,
                BinaryPrimitives.ReadInt32LittleEndian(
                    state.SurfaceRecords.AsSpan(slot * 20 + 8))));
    }

    [Fact]
    public void InstalledKamidoriLayoutThreeRoundTripsBanksGfxAndHistoryWhenPresent()
    {
        string eushullyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eushully");
        if (!Directory.Exists(eushullyRoot)) return;
        string? path = Directory.EnumerateFiles(
                eushullyRoot, "SAVE00.DAT", SearchOption.AllDirectories)
            .FirstOrDefault(candidate =>
            {
                try
                {
                    NativeSaveMetadata metadata = NativeSaveContainerCodec.ReadMetadata(
                        File.ReadAllBytes(candidate));
                    return metadata.CompatibilityId == 0x46333334
                        && metadata.SaveVersion1 == 3 && metadata.SaveVersion2 == 20;
                }
                catch (Exception error) when (
                    error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    return false;
                }
            });
        if (path == null) return;

        byte[] source = File.ReadAllBytes(path);
        NativeSaveDocument document = NativeSaveContainerCodec.Decode(source);
        int cutoff = BinaryPrimitives.ReadInt32LittleEndian(document.Payload);
        int banksAt = checked(0x5718 + cutoff * 0x414);
        int[] counts = Enumerable.Range(0, 6)
            .Select(index => BinaryPrimitives.ReadInt32LittleEndian(
                document.Payload.AsSpan(banksAt + index * 4)))
            .ToArray();

        Assert.Equal([1037327, 1, 802, 1, 1, 1], counts);

        int at = banksAt + 24 + counts[0] * 4 + counts[1] * 4;
        int stringDwords = BinaryPrimitives.ReadInt32LittleEndian(document.Payload.AsSpan(at));
        at += 4 + stringDwords * 4
            + counts[3] * 4 + counts[4] * 4 + counts[5] * 4;
        Assert.Equal(0x2e4, BinaryPrimitives.ReadInt32LittleEndian(document.Payload.AsSpan(at)));
        // SYS4433 writer @0040d1e0 copies 0xb9 DWORDs but advances the DWORD cursor
        // by 0x2e5 per handle+record; loader @0040faa0 mirrors this sparse layout.
        int objectCount = BinaryPrimitives.ReadInt32LittleEndian(document.Payload.AsSpan(at + 4));
        Assert.True(objectCount > 0);
        int objectsAt = at + 8;
        int rangeAt = checked(objectsAt + objectCount * 0xb94);
        Assert.True(rangeAt + 8 + 0x2e4 <= document.Payload.Length);
        int[] handles = Enumerable.Range(0, objectCount)
            .Select(index => BinaryPrimitives.ReadInt32LittleEndian(
                document.Payload.AsSpan(objectsAt + index * 0xb94)))
            .ToArray();
        Assert.Equal(handles.OrderBy(handle => handle).Distinct(), handles);

        int expectedDwords = checked(cutoff * 0x105 + 0x53ea + counts.Sum()
            + stringDwords + 0x2f1 + objectCount * 0x2e8);
        Assert.Equal((expectedDwords - 2) * 4, document.Payload.Length);
        int nonzeroRotationCaches = 0;
        foreach (int index in Enumerable.Range(0, objectCount + 1))
        {
            int recordAt = index < objectCount ? objectsAt + index * 0xb94 + 4 : rangeAt + 8;
            ReadOnlySpan<byte> tail = document.Payload.AsSpan(recordAt + 0x2d4, 16);
            if (tail.ContainsAnyExcept((byte)0)) nonzeroRotationCaches++;
            for (int component = 0; component < 4; component++)
                Assert.True(float.IsFinite(BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(tail[(component * 4)..]))));
        }
        var history = new AdvTextHistory();
        NativeTextHistoryCodec.DecodeInto(source.AsSpan(document.BytesConsumed), history);
        _output.WriteLine($"Kamidori 3.20: payload={document.Payload.Length}, cutoff={cutoff}, "
            + $"objects={objectCount}, handles={handles[0]}..{handles[^1]}, stride=0xb94, "
            + $"nonzero rotation caches={nonzeroRotationCaches}, "
            + $"range first={BinaryPrimitives.ReadInt32LittleEndian(document.Payload.AsSpan(rangeAt))}, "
            + $"range count={BinaryPrimitives.ReadInt32LittleEndian(document.Payload.AsSpan(rangeAt + 4))}, "
            + $"history bytes={source.Length - document.BytesConsumed}");
        var identity = Age.Engine.Profiles.GameProfileRegistry.BuiltIn.Find("kamidori")!
            .Persistence.CreateExpectedNativeIdentity();
        Assert.Equal(document.Metadata.GameId, identity.GameId);
        identity.Validate(document.Metadata, numbered: true);
        NativeNumberedSaveState decoded = NativeNumberedSaveCodec.Decode(document.Payload, 0x2e4);
        NativeNumberedSaveState roundTrip = NativeNumberedSaveCodec.Decode(
            NativeNumberedSaveCodec.Encode(decoded), 0x2e4);
        Assert.Equal(decoded.IntegerGlobals, roundTrip.IntegerGlobals);
        Assert.Equal(decoded.FloatGlobals, roundTrip.FloatGlobals);
        Assert.Equal(decoded.StringGlobals, roundTrip.StringGlobals);
        Assert.Equal(decoded.PointerGlobals, roundTrip.PointerGlobals);
        Assert.Equal(decoded.PointerStrings, roundTrip.PointerStrings);
        Assert.Equal(decoded.RangeTransformRecord, roundTrip.RangeTransformRecord);
        Assert.Equal(handles, roundTrip.GfxObjects.Select(item => (int)item.Handle));
        for (int index = 0; index < objectCount; index++)
            Assert.Equal(decoded.GfxObjects[index].Record, roundTrip.GfxObjects[index].Record);

        // Exercise semantic import and fresh capture through the ordinary shared opcodes too.
        // All writes target an isolated copy, never the installed native save directory.
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "age-kamidori-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            File.WriteAllBytes(Path.Combine(temporaryRoot, "SAVE00.DAT"), source);
            var store = new DirectoryNativeDatStore(temporaryRoot, identity);
            // The catalog exposes the shared data-only import handler; SYS4433's shipped
            // dispatch omits 0x19f, so full native-script continuation is checked separately.
            OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
            Script script = ScriptAssembler.Assemble(table, "SAVE_ROUNDTRIP.BIN",
            [
                (0x19f, [new Operand(3, 0x110000), new Operand(0, 0)]),
                (0x19e, [new Operand(3, 0x110001), new Operand(0, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []);
            var vm = new VirtualMachine(script, table, new RecordingHost(), nativeDatStore: store);
            vm.Run();
            Assert.Equal(0, vm.Globals[0x110000]);
            Assert.Equal(0, vm.Globals[0x110001]);
            NativeNumberedSaveState captured = NativeNumberedSaveCodec.Decode(
                store.LoadNumberedFile(1)!.Document.Payload, 0x2e4);
            Assert.Equal(decoded.IntegerGlobals, captured.IntegerGlobals);
            Assert.Equal(decoded.FloatGlobals, captured.FloatGlobals);
            Assert.Equal(decoded.StringGlobals, captured.StringGlobals);
            Assert.Equal(decoded.RangeTransformFirst, captured.RangeTransformFirst);
            Assert.Equal(decoded.RangeTransformCount, captured.RangeTransformCount);
            Assert.Equal(decoded.RangeTransformRecord[0x2d4..], captured.RangeTransformRecord[0x2d4..]);
            Assert.Equal(objectCount, captured.GfxObjects.Count);
            for (int index = 0; index < objectCount; index++)
            {
                Assert.Equal(decoded.GfxObjects[index].Handle, captured.GfxObjects[index].Handle);
                byte[] original = decoded.GfxObjects[index].Record;
                byte[] restored = captured.GfxObjects[index].Record;
                Assert.Equal(original[4..0x30], restored[4..0x30]);
                Assert.Equal(original[0x68..0x6c], restored[0x68..0x6c]);
                Assert.Equal(original[0x2d4..], restored[0x2d4..]);
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static int FindGfxObjects(byte[] payload, int count, int firstHandle,
        int recordSize = NativeGfxSaveLayout.BaseRecordSize)
    {
        for (int offset = 0; offset <= payload.Length - 12; offset += 4)
            if (BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset)) ==
                    recordSize
                && BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4)) == count
                && BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 8)) == firstHandle)
                return offset + 8;
        throw new Xunit.Sdk.XunitException("Encoded gfx table was not found.");
    }
}

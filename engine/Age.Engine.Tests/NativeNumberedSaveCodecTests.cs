using Age.Engine.Model;
using Age.Engine.Persistence;
using System.Buffers.Binary;

public class NativeNumberedSaveCodecTests
{
    [Fact]
    public void LayoutThreeRoundTripsNativeBanksFramesAndGfxRecords()
    {
        var frames = new[]
        {
            new NativeSavedScriptFrame(-1, 0, new[] { 2, 4 }, 8, 7),
            new NativeSavedScriptFrame(0, 0x3389, Array.Empty<int>(), 3, -1),
        };
        NativeNumberedSaveState state = NativeNumberedSaveCodec.Empty(frames) with
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
                    Enumerable.Range(0, NativeNumberedSaveState.GfxRecordSize)
                        .Select(i => unchecked((byte)i)).ToArray()),
                new NativeSavedGfxObject(0xcf09,
                    Enumerable.Repeat((byte)0xa5, NativeNumberedSaveState.GfxRecordSize).ToArray()),
            ],
            RangeTransformFirst = 100,
            RangeTransformCount = 4,
            RangeTransformRecord = Enumerable.Repeat((byte)0x5a,
                NativeNumberedSaveState.GfxRecordSize).ToArray(),
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

        const int nativeEntryStride = (1 + NativeNumberedSaveState.GfxRecordSize) * 4;
        int objectsAt = FindGfxObjects(encoded, 2, 0xcf08);
        Assert.Equal(0xcf08, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(objectsAt)));
        Assert.Equal(0xcf09, BinaryPrimitives.ReadInt32LittleEndian(
            encoded.AsSpan(objectsAt + nativeEntryStride)));
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

    private static int FindGfxObjects(byte[] payload, int count, int firstHandle)
    {
        for (int offset = 0; offset <= payload.Length - 12; offset += 4)
            if (BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset)) ==
                    NativeNumberedSaveState.GfxRecordSize
                && BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4)) == count
                && BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 8)) == firstHandle)
                return offset + 8;
        throw new Xunit.Sdk.XunitException("Encoded gfx table was not found.");
    }
}

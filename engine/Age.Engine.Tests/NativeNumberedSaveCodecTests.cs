using Age.Engine.Model;
using Age.Engine.Persistence;

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
            EngineState = 9,
            StateWords = Enumerable.Range(10, 10).ToArray(),
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
            ],
            RangeTransformFirst = 100,
            RangeTransformCount = 4,
            RangeTransformRecord = Enumerable.Repeat((byte)0x5a,
                NativeNumberedSaveState.GfxRecordSize).ToArray(),
        };

        byte[] encoded = NativeNumberedSaveCodec.Encode(state);
        NativeNumberedSaveState decoded = NativeNumberedSaveCodec.Decode(encoded);

        Assert.Equal(state.SavedFrameOwner, decoded.SavedFrameOwner);
        Assert.Equal(state.EngineState, decoded.EngineState);
        Assert.Equal(state.StateWords, decoded.StateWords);
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
        Assert.Equal(state.RangeTransformRecord, decoded.RangeTransformRecord);
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
    }
}

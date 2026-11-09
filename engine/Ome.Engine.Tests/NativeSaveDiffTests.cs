using Ome.Engine.Persistence;
using System.Buffers.Binary;

public class NativeSaveDiffTests
{
    private const int GfxRecordSize = 0x2e4;

    private static readonly NativeSaveMetadata Metadata = new(
        NativeSaveMagic.S4SD, 1, "test", new NativeSystemTime(2026, 11, 0, 8, 12, 0, 0, 0), 100, 3, 10);

    private static NativeNumberedSaveState BaseState()
        => NativeNumberedSaveCodec.Empty(
               [new NativeSavedScriptFrame(-1, 0x10, new[] { 4 }, 8, -1)], GfxRecordSize) with
           {
               BgmTrackId = 5,
               IntegerGlobals = new[] { 0, 1, 2, 3 },
               StringGlobals = new[] { "a", "b" },
               GfxObjects = [new NativeSavedGfxObject(0xc382, new byte[GfxRecordSize])],
           };

    // Each container gets its own random transform seed, so the files differ even for equal payloads.
    private static NativeSaveDocument Container(NativeNumberedSaveState state, NativeSaveMetadata? metadata = null)
        => NativeSaveContainerCodec.Decode(
            NativeSaveContainerCodec.Encode(NativeNumberedSaveCodec.Encode(state), metadata ?? Metadata));

    [Fact]
    public void EqualPayloadsMatchWhateverTheirEncoding()
    {
        NativeSaveComparison comparison = NativeSaveDiff.Compare(Container(BaseState()), Container(BaseState()));

        Assert.True(comparison.Numbered);
        Assert.Empty(comparison.Differences);
    }

    [Fact]
    public void NumberedSavesDifferBySectionKeyAndField()
    {
        NativeNumberedSaveState a = BaseState();
        byte[] surfaces = (byte[])a.SurfaceRecords.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(surfaces.AsSpan(0x20 * 20 + 16), 1);   // slot 0x20's created flag
        byte[] gfx = new byte[GfxRecordSize];
        BinaryPrimitives.WriteInt32LittleEndian(gfx.AsSpan(0x58), 400);
        NativeNumberedSaveState b = a with
        {
            BgmTrackId = 6,
            SurfaceRecords = surfaces,
            IntegerGlobals = new[] { 0, 1, 0x320a, 3 },
            StringGlobals = new[] { "a", "c" },
            GfxObjects = [new NativeSavedGfxObject(0xc382, gfx), new NativeSavedGfxObject(0xc44b, gfx)],
        };

        NativeSaveComparison comparison = NativeSaveDiff.Compare(
            Container(a), Container(b, Metadata with { AccumulatedPlaySeconds = 160 }));

        Assert.True(comparison.Numbered);
        Assert.Equal(
            new[]
            {
                new NativeSaveDifference(NativeSaveDiff.Metadata, 0, "play-seconds", "100", "160"),
                new NativeSaveDifference(NativeSaveDiff.Header, 0, "bgm", "5", "6"),
                new NativeSaveDifference(NativeSaveDiff.SurfaceRecords, 0x20, "created", "0", "1"),
                new NativeSaveDifference(NativeSaveDiff.IntegerGlobals, 2, "", "2", "12810 (0x320a)"),
                new NativeSaveDifference(NativeSaveDiff.StringGlobals, 1, "", "\"b\"", "\"c\""),
                new NativeSaveDifference(NativeSaveDiff.GfxObjects, 0xc382, "+0x58", "0", "400 (0x190)"),
                new NativeSaveDifference(NativeSaveDiff.GfxObjects, 0xc44b, "present", "no", "yes"),
            },
            comparison.Differences);
    }

    [Fact]
    public void PayloadsThatAreNotNumberedSavesCompareByDword()
    {
        byte[] a = new byte[16], b = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), -2);
        NativeSaveDocument left = NativeSaveContainerCodec.Decode(NativeSaveContainerCodec.Encode(a, Metadata));
        NativeSaveDocument right = NativeSaveContainerCodec.Decode(NativeSaveContainerCodec.Encode(b, Metadata));

        NativeSaveComparison comparison = NativeSaveDiff.Compare(left, right);

        Assert.False(comparison.Numbered);
        Assert.NotNull(comparison.NumberedDecodeError);
        Assert.Equal(
            new[] { new NativeSaveDifference(NativeSaveDiff.Payload, 0, "+0x8", "0", "-2") },
            comparison.Differences);
    }
}

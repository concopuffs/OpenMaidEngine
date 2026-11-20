using System.Linq;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Profiles;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// What a save file says about itself and how it is set against a profile (NativeSaveInfo; <c>Ome.Cli save-info</c>,
/// docs/adding-a-game.md, step 7), and the polygons a load clears.
/// </summary>
public class NativeSaveInfoTests
{
    private const int GfxRecordSize = 0x2e4;

    private static NativeSaveIdentity Arterial()
    {
        GameProfileManifest profile = GameProfileRegistry.BuiltIn.Find("arterial")!;
        return profile.Persistence.CreateExpectedNativeIdentity(EngineBuilds.ForProfile(profile).Rules.Saves);
    }

    private static byte[] File(NativeSaveIdentity identity, NativeNumberedSaveState state, byte[]? tail = null)
    {
        NativeSaveMetadata metadata = identity.CreateMetadata(new NativeSystemTime(2026, 11, 4, 19, 21, 2, 20, 0), 84);
        byte[] container = NativeSaveContainerCodec.Encode(NativeNumberedSaveCodec.Encode(state), metadata);
        return tail == null ? container : container.Concat(tail).ToArray();
    }

    private static NativeNumberedSaveState State(NativeSaveIdentity identity)
    {
        int[] counts = identity.BankDimensions!.ToArray();
        return NativeNumberedSaveCodec.Empty([new NativeSavedScriptFrame(-1, 0, [], 8, 7)], GfxRecordSize) with
        {
            IntegerGlobals = new int[counts[0]],
            FloatGlobals = new int[counts[1]],
            StringGlobals = Enumerable.Repeat("", counts[2]).ToArray(),
            PointerGlobals = new int[counts[3]],
            FloatPointers = new int[counts[4]],
            StringPointers = new int[counts[5]],
            GfxObjects = [new NativeSavedGfxObject(0xabe0, new byte[GfxRecordSize])],
        };
    }

    [Fact]
    public void ASaveOfTheGameAgreesWithItsProfileAndSurvivesTheCodec()
    {
        NativeSaveIdentity identity = Arterial();

        NativeSaveInfo info = NativeSaveInfo.Read(File(identity, State(identity), tail: new byte[13]));

        Assert.Equal("438D", info.VersionStamp);
        Assert.Equal((3, 20), (info.Metadata.SaveVersion1, info.Metadata.SaveVersion2));
        Assert.Equal(13, info.TailBytes);
        Assert.Equal([260993, 1, 231, 1, 1, 1], info.BankCounts);
        Assert.Equal(GfxRecordSize, info.Numbered!.GraphicsRecordSize);
        Assert.True(info.ReencodedSectionsAgree);
        Assert.True(info.ReencodedBytesAgree);
        Assert.Empty(info.MismatchesWith(identity));
    }

    [Fact]
    public void ASaveOfAnotherGameOrBuildSaysWhereItDiffers()
    {
        NativeSaveIdentity arterial = Arterial();
        GameProfileManifest kamidori = GameProfileRegistry.BuiltIn.Find("kamidori")!;
        NativeSaveIdentity other = kamidori.Persistence.CreateExpectedNativeIdentity(
            EngineBuilds.ForProfile(kamidori).Rules.Saves);

        NativeSaveInfo info = NativeSaveInfo.Read(File(arterial, State(arterial)));
        string[] mismatches = info.MismatchesWith(other).ToArray();

        Assert.Contains(mismatches, text => text.Contains("the game id is '創刻のアテリアル'"));
        Assert.Contains(mismatches, text => text.Contains("the version stamp is \"438D\"") && text.Contains("\"433G\""));
        Assert.Contains(mismatches, text => text.Contains("the bank counts are 260993, 1, 231, 1, 1, 1"));
        Assert.Equal(3, mismatches.Length);     // the save version and the record size are the same in both
    }

    [Fact]
    public void APayloadThatIsNotANumberedSaveIsReportedAsSuch()
    {
        NativeSaveIdentity identity = Arterial();
        NativeSaveMetadata metadata = identity.CreateMetadata(new NativeSystemTime(2026, 11, 4, 19, 21, 2, 20, 0), 0);

        NativeSaveInfo info = NativeSaveInfo.Read(NativeSaveContainerCodec.Encode(new byte[64], metadata));

        Assert.Null(info.Numbered);
        Assert.NotNull(info.NumberedDecodeError);
        Assert.Null(info.BankCounts);
        Assert.Empty(info.MismatchesWith(identity));    // the header alone agrees
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void ALoadClearsThePolygonsWhereTheCatalogSaysSo(bool clearPolygons, int left)
    {
        // save_data_deserialize_and_begin_restore@0x40fe90 (AGE 4.38D) calls gfx_polygon_map_clear under
        // set:CreateObject bit 1; a save holds no polygon.
        Graphics graphics = Graphics.Headless();
        graphics.State.CreatePolygon(10,
        [
            new GfxState.PolygonVertex(0, 0, 0, 0xff000000, 0, 0),
            new GfxState.PolygonVertex(4, 0, 0, 0xff000000, 1, 0),
            new GfxState.PolygonVertex(0, 4, 0, 0xff000000, 0, 1),
        ], 0);

        graphics.ReadSaveRecords(graphics.State.CapturePersistenceSnapshot(), releaseAllSurfaces: false, clearPolygons);

        Assert.Equal(left, graphics.State.PolygonCount);
    }
}

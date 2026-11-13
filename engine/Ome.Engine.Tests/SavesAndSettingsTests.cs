using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;

/// <summary>
/// The save and settings native (Ome.Engine/Natives/SavesAndSettings*.cs; engine layering milestone 9) on its own:
/// the playtime clock, the results without a save store, and the shutdown flush's set:NoSaveDat. Numbered saves with a
/// store run through the VM (NumberedSaveVmTests, SaveUiIntegrationTests).
/// </summary>
public class SavesAndSettingsTests
{
    [Fact]
    public void ThePlaytimeCountsFromConstructionAndTheProfilesTimeOnlyLandsPending()
    {
        var clock = new ManualPlayClock { NowMs = 5_000 };
        var profile = new SharedProfile();
        var saves = new SavesAndSettings(profile, playClock: clock);
        clock.NowMs += 12_000;

        Assert.Equal(12u, saves.AccumulatedPlaySeconds);

        saves.CommitLoadedPlayTime();   // the pending value (the profile's, 0) becomes the base, counting from now
        clock.NowMs += 3_000;
        Assert.Equal(3u, saves.AccumulatedPlaySeconds);

        saves.ResetScene();   // op 0x9
        clock.NowMs += 1_000;
        Assert.Equal(1u, saves.AccumulatedPlaySeconds);
    }

    [Fact]
    public void WithoutAStoreEveryNumberedOperationFailsAsNativeReportsIt()
    {
        var saves = new SavesAndSettings();
        var records = new NumberedSaveRecords(
            new ScriptMemory(), Audio.Silent(SkipAndAuto.WithSystemClock()), Graphics.Headless(), new AdvTextHistory(),
            () => [], () => [], releaseSurfacesOnLoad: false);

        Assert.Equal(1, saves.SaveNumbered(0, records));
        Assert.Equal(NumberedLoad.Missing, saves.LoadNumbered(0, restoreHistory: true, records));
        Assert.Null(saves.RestoringFrames);
        Assert.Equal(1, saves.QueryNumbered(0, out NativeSaveMetadata? metadata));
        Assert.Null(metadata);
        Assert.Equal(2, saves.DeleteNumbered(0));
        Assert.Equal(2, saves.CopyNumbered(0, 1));
    }

    [Fact]
    public void TheShutdownFlushIsSuppressedByNoSaveDatAndRunsOnce()
    {
        var suppressed = new SavesAndSettings(noSaveDat: true);
        var unavailable = new SavesAndSettings();

        Assert.Equal(SharedProfileShutdownFlushOutcome.Suppressed, suppressed.FlushSharedProfileOnShutdown().Outcome);
        Assert.Equal(SharedProfileShutdownFlushOutcome.AlreadyHandled,
                     suppressed.FlushSharedProfileOnShutdown().Outcome);
        Assert.Equal(SharedProfileShutdownFlushOutcome.StoreUnavailable,
                     unavailable.FlushSharedProfileOnShutdown().Outcome);
    }
}

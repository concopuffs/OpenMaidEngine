using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Xunit;

/// <summary>
/// The audio native over a recording device and a stepped clock, without Godot (engine layering spec §5.2 and §6.2,
/// milestone 6): the BGM envelope, the voice duck and its release, delayed starts, the voice that fast-forward holds
/// back, the mixer routes and a numbered load.
/// </summary>
public class AudioNativeTests
{
    private static string Volume(int basisPoints)
        => $"volume 11 {RecordingAudioDevice.Db(NativeAudioVolume.ToDecibels(basisPoints))}";

    [Fact]
    public void AFadeMovesInWholeStepsAndCommitsItsTarget()
    {
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(5000, 500);   // under a second: 10 steps of 10 points
        rig.Device.Commands.Clear();

        rig.Advance(49);
        Assert.Empty(rig.Device.CommandsStartingWith("volume 11"));   // the first step is not due yet
        rig.Advance(1);
        Assert.Equal([Volume(9500)], rig.Device.CommandsStartingWith("volume 11"));
        rig.Advance(450);
        Assert.Equal(Volume(5000), rig.Device.CommandsStartingWith("volume 11")[^1]);
        Assert.Equal(5, rig.Audio.CurrentBgmTrack);   // a target above zero keeps the track

        rig.Device.Commands.Clear();
        rig.Advance(1000);
        Assert.Empty(rig.Device.Commands);   // the fade is over: the tick sends nothing
    }

    [Fact]
    public void ThePauseToggleHoldsThePlayingTrackAndAStartOrAStopClearsIt()
    {
        // Op 0xc1 (bgm object +0x410): the backend holds channel 11 where it is, and plays it on.
        var rig = new AudioRig();
        rig.Audio.ToggleBgmPause();                     // nothing plays: the flag flips and the device hears nothing
        Assert.True(rig.Audio.BgmPaused);
        Assert.Empty(rig.Device.CommandsStartingWith("pause"));
        rig.Audio.ToggleBgmPause();

        rig.Audio.PlayBgm(5);
        rig.Audio.ToggleBgmPause();
        rig.Audio.ToggleBgmPause();
        rig.Audio.ToggleBgmPause();
        Assert.Equal(["pause 11", "resume 11", "pause 11"],
            rig.Device.Commands.Where(command => command is "pause 11" or "resume 11"));

        rig.Audio.PlayBgm(5);                           // the identical request keeps the stream, and the hold
        Assert.True(rig.Audio.BgmPaused);
        rig.Audio.PlayBgm(6);                           // another track starts unheld
        Assert.False(rig.Audio.BgmPaused);

        rig.Audio.ToggleBgmPause();
        rig.Audio.StopBgm();                            // the release clears it
        Assert.False(rig.Audio.BgmPaused);
    }

    [Fact]
    public void AFadeArmedAgainPartWayStartsFromTheCommittedLevel()
    {
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(0, 1000);
        rig.Advance(500);
        Assert.Equal(Volume(5000), rig.Device.CommandsStartingWith("volume 11")[^1]);

        rig.Audio.FadeBgm(2000, 1000);   // from 10000, the level last committed, not the 5000 the channel plays at
        rig.Advance(500);
        Assert.Equal(Volume(6000), rig.Device.CommandsStartingWith("volume 11")[^1]);
    }

    [Fact]
    public void ADuckDropsToTheSettingAndItsReleaseFadesToFullOverASecond()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ome-audio-{Guid.NewGuid():N}", "SYS4REG.INI");
        try
        {
            var settings = new Sys4RegIniStore(path, defaultMusicRouteValue: 2);
            settings.SaveInt("sound", "MusicFadeOnVoicePlayingVolume", 0);   // 0 reads as 100 basis points
            var rig = new AudioRig(settings: settings);
            rig.Audio.PlayBgm(5);
            rig.Device.Commands.Clear();

            rig.Audio.PlayVoice(10);
            Assert.Equal([Volume(100)], rig.Device.CommandsStartingWith("volume 11"));
            rig.Audio.ArmVoiceBgmDuckRelease();
            rig.Device.End(Audio.VoiceChannel);
            rig.Advance(0);   // the end is reported and the release starts: 100 steps of one point
            rig.Advance(10);
            Assert.Equal(Volume(NativeAudioVolume.InterpolateFade(100, 10000, 1)),
                         rig.Device.CommandsStartingWith("volume 11")[^1]);
            rig.Advance(990);
            Assert.Equal(Volume(10000), rig.Device.CommandsStartingWith("volume 11")[^1]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void TheDuckReleaseWaitsUntilTheVoiceEndsAndTheReleaseIsArmed()
    {
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Audio.PlayVoice(10);
        rig.Device.Commands.Clear();

        rig.Device.End(Audio.VoiceChannel);
        rig.Advance(2000);
        Assert.Empty(rig.Device.Commands);   // ended, but not armed: BGM stays ducked between lines

        rig.Audio.ArmVoiceBgmDuckRelease();
        rig.Advance(0);
        rig.Advance(1000);
        Assert.Equal(Volume(10000), rig.Device.CommandsStartingWith("volume 11")[^1]);
    }

    [Fact]
    public void AVoiceDoesNotDuckWhileAFadeRuns()
    {
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(8000, 1000);
        rig.Device.Commands.Clear();

        rig.Audio.PlayVoice(10);
        Assert.Empty(rig.Device.CommandsStartingWith("volume 11"));
        rig.Audio.ArmVoiceBgmDuckRelease();
        rig.Device.End(Audio.VoiceChannel);
        rig.Advance(3000);
        Assert.Equal(Volume(8000), rig.Device.CommandsStartingWith("volume 11")[^1]);   // no duck, so no release to full
    }

    [Fact]
    public void ControlBitZeroSuppressesTheDuckAndTheOneShotBitClearsTheMaskAtTheNextArm()
    {
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Device.Commands.Clear();

        rig.Audio.SetVoiceBgmDuckControl(0x10001);
        rig.Audio.PlayVoice(10);
        Assert.Empty(rig.Device.CommandsStartingWith("volume 11"));   // bit 0: no duck
        rig.Audio.ArmVoiceBgmDuckRelease();                             // 0x10000: the mask clears here

        rig.Audio.PlayVoice(11);
        Assert.Equal([Volume(5000)], rig.Device.CommandsStartingWith("volume 11"));
    }

    [Fact]
    public void DelayedStartsWaitWhileAStatementBlockIsOpen()
    {
        var rig = new AudioRig();
        rig.Audio.LoadSoundEffect(40, 2);
        rig.Audio.ScheduleSoundEffectStart(2, 0, 100);
        rig.Audio.ScheduleVoicePlayback(10, 0, 100);
        rig.Tick.OpenStatementBlock();
        rig.Advance(500);
        Assert.Empty(rig.Device.CommandsStartingWith("play"));

        rig.Tick.CloseStatementBlock();
        rig.Advance(0);   // the sound effect was due; the voice's delay starts counting now
        Assert.Equal(["play 2 sfx:40 once 0.0dB"], rig.Device.CommandsStartingWith("play"));
        rig.Advance(99);
        Assert.Empty(rig.Device.CommandsStartingWith("play 12"));
        rig.Advance(1);
        Assert.Equal([(10L, false)], rig.Device.Voices);
    }

    [Fact]
    public void OneDelayedStartWaitsPerChannelAndALoadCancelsIt()
    {
        var rig = new AudioRig();
        rig.Audio.LoadSoundEffect(40, 2);
        rig.Audio.ScheduleSoundEffectStart(2, 0, 100);
        rig.Audio.ScheduleSoundEffectStart(2, 1, 300);   // replaces the first
        rig.Advance(200);
        Assert.Empty(rig.Device.CommandsStartingWith("play"));
        rig.Advance(100);
        Assert.Equal(["play 2 sfx:40 loop 0.0dB"], rig.Device.CommandsStartingWith("play"));

        rig.Audio.ScheduleSoundEffectStart(2, 0, 100);
        rig.Audio.LoadSoundEffect(41, 2);
        rig.Advance(500);
        Assert.Single(rig.Device.CommandsStartingWith("play"));
    }

    [Fact]
    public void FastForwardHoldsTheLastVoiceBackAndPlaysItWhenItEnds()
    {
        var rig = new AudioRig();
        rig.Audio.PlayVoice(9);
        rig.Device.Commands.Clear();
        rig.Device.Plays.Clear();

        rig.Skip.SetScriptFastForward(true);
        rig.Audio.PlayVoice(10);
        rig.Audio.PlayVoice(11);
        Assert.Equal(["stop 12"], rig.Device.Commands);   // the first held voice stops the one that played
        Assert.False(rig.Audio.IsVoicePlaying);

        rig.Skip.SetScriptFastForward(false);
        Assert.Equal([(11L, false)], rig.Device.Voices);   // the last one, once
        Assert.True(rig.Audio.IsVoicePlaying);
    }

    [Fact]
    public void ResetMessageVoiceStateDropsTheHeldVoice()
    {
        var rig = new AudioRig();
        rig.Skip.SetScriptFastForward(true);
        rig.Audio.PlayVoice(10);
        rig.Audio.ResetMessageVoiceState();
        rig.Skip.SetScriptFastForward(false);
        Assert.Empty(rig.Device.Voices);
    }

    [Fact]
    public void AnEndReportOfAnEarlierPlayLeavesTheChannelPlaying()
    {
        var rig = new AudioRig();
        rig.Audio.PlayVoice(10);
        rig.Device.End(Audio.VoiceChannel);   // reported, but not yet taken
        rig.Audio.PlayVoice(11);
        rig.Tick.Service();
        Assert.True(rig.Audio.IsVoicePlaying);   // the report was of voice 10

        rig.Device.End(Audio.VoiceChannel);
        rig.Tick.Service();
        Assert.False(rig.Audio.IsVoicePlaying);
    }

    [Fact]
    public void TheMusicRouteStopsTheBgmAndTurningItOnStartsTheRetainedTrackAgainAtFullLevel()
    {
        // sound_set_music_route_enabled: bgm_stop_release, then bgm_force_play_track(0, +0x414) on the new route.
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(5000, 0);
        rig.Audio.ToggleBgmPause();
        rig.Device.Commands.Clear();

        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.Music, false));
        Assert.Equal(["stop 11", "bus-mute Music True"], rig.Device.Commands);
        Assert.False(rig.Audio.IsChannelPlaying(Audio.BgmChannel));
        Assert.False(rig.Audio.BgmPaused);

        rig.Device.Commands.Clear();
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.Music, true));
        Assert.Equal(["load 11 Music bgm:5", "play 11 bgm:5 loop 0.0dB", "bus-mute Music False"], rig.Device.Commands);
        Assert.True(rig.Audio.IsChannelPlaying(Audio.BgmChannel));
    }

    [Fact]
    public void APlayOfTheRetainedTrackKeepsItsStreamUnlessTheBuildAlsoComparesTheStartMode()
    {
        var rig = new AudioRig();
        rig.Audio.RestartBgm(5, 0);                     // op 0xb9: once
        rig.Device.Commands.Clear();
        rig.Audio.PlayBgm(5);                           // 4.24J: the track alone decides, and the stream goes on
        Assert.Empty(rig.Device.Commands);
        // The play left the start mode at 1, which the Music route restarts the track with.
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.Music, false));
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.Music, true));
        Assert.Equal("play 11 bgm:5 loop 0.0dB", rig.Device.CommandsStartingWith("play")[^1]);

        var later = new AudioRig();
        later.Audio.RestartBgm(5, 0);
        later.Device.Commands.Clear();
        later.Audio.PlayBgm(5, comparesStartMode: true);   // 4.33G and 4.38D: the start mode differs, so it starts again
        Assert.Equal(["load 11 Music bgm:5", "play 11 bgm:5 loop 0.0dB"], later.Device.Commands);
        later.Device.Commands.Clear();
        later.Audio.PlayBgm(5, comparesStartMode: true);   // now both match
        Assert.Empty(later.Device.Commands);
    }

    [Fact]
    public void PlayingTrackZeroStartsTheRetainedTrackAgainAndWithNoneReleasesTheSource()
    {
        // bgm_play_track with track 0, as Arterial's DEBUGADV2 plays it.
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Device.Commands.Clear();
        rig.Audio.PlayBgm(0);
        Assert.Equal(["load 11 Music bgm:5", "play 11 bgm:5 loop 0.0dB"], rig.Device.Commands);
        Assert.Equal(5, rig.Audio.CurrentBgmTrack);

        rig.Audio.StopBgm();
        rig.Device.Commands.Clear();
        rig.Audio.PlayBgm(0);
        Assert.Equal(["unload 11"], rig.Device.Commands);
        Assert.Equal(0, rig.Audio.CurrentBgmTrack);
    }

    [Fact]
    public void ARootReloadZeroesTheRetainedTrackPauseAndStartModeAndLeavesTheStream()
    {
        // scene_context_init_reset zeroes the BGM object's +0x40c, +0x410 and +0x414 and calls no backend.
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Audio.ToggleBgmPause();
        rig.Device.Commands.Clear();
        rig.Audio.ResetScene();
        Assert.Empty(rig.Device.Commands);              // the stream stays, held
        Assert.Equal(0, rig.Audio.CurrentBgmTrack);
        Assert.False(rig.Audio.BgmPaused);
        rig.Audio.ToggleBgmPause();                     // so the next op 0xc1 holds it again
        Assert.Equal(["pause 11"], rig.Device.Commands);

        rig.Device.Commands.Clear();
        rig.Audio.PlayBgm(5);                           // a title's play of the track that plays starts it again
        Assert.Equal(["load 11 Music bgm:5", "play 11 bgm:5 loop 0.0dB"], rig.Device.Commands);

        rig.Audio.ResetScene();
        rig.Device.Commands.Clear();
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.Music, false));
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.Music, true));
        // No retained track to start again: the source goes at each change, as bgm_stop_release runs at each.
        Assert.Equal(["unload 11", "bus-mute Music True", "unload 11", "bus-mute Music False"], rig.Device.Commands);
        Assert.False(rig.Audio.IsChannelPlaying(Audio.BgmChannel));
    }

    [Fact]
    public void WithTheSoundEffectRouteOffNothingStartsAndADelayedStartFallingDueIsUsedUp()
    {
        // sfx_channel_start tests the route before the manager, and sfx_tick_delayed_starts clears a request first.
        var rig = new AudioRig();
        rig.Audio.LoadSoundEffect(40, 2);
        rig.Audio.ScheduleSoundEffectStart(2, 0, 100);
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.SoundEffect, false));
        rig.Audio.StartSoundEffect(2, 1);
        Assert.False(rig.Audio.IsChannelPlaying(2));
        rig.Advance(500);
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.SoundEffect, true));
        rig.Advance(500);
        Assert.Empty(rig.Device.CommandsStartingWith("play"));
    }

    [Fact]
    public void ADelayedStartOutlivesTheSoundEffectRouteTurnedOffAndOnBeforeItFallsDue()
    {
        // sound_set_sfx_route_enabled only stops channels 0..9.
        var rig = new AudioRig();
        rig.Audio.LoadSoundEffect(40, 2);
        rig.Audio.ScheduleSoundEffectStart(2, 0, 100);
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.SoundEffect, false));
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.SoundEffect, true));
        rig.Advance(500);
        Assert.Equal(["play 2 sfx:40 once 0.0dB"], rig.Device.CommandsStartingWith("play"));
    }

    [Fact]
    public void ChannelElevenPlaysItsTrackAgainFromItsStartAlsoAfterTheBgmStops()
    {
        // The DirectSound backend's BGM stop only stops channel 11's buffer, which keeps the track.
        var rig = new AudioRig();
        Assert.False(rig.Audio.ChannelHoldsSound(Audio.BgmChannel));
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(5000, 0);
        rig.Device.Commands.Clear();

        rig.Audio.StartSoundEffect(Audio.BgmChannel, 0);
        string half = RecordingAudioDevice.Db(NativeAudioVolume.ToDecibels(5000));
        Assert.Equal([$"play 11 bgm:5 once {half}"], rig.Device.Commands);
        // The Music route starts the retained track again with the retained start mode, not the op's, at full level.
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.Music, false));
        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.Music, true));
        Assert.Equal("play 11 bgm:5 loop 0.0dB", rig.Device.CommandsStartingWith("play")[^1]);

        rig.Audio.StopBgm();
        rig.Device.Commands.Clear();
        rig.Audio.StartSoundEffect(Audio.BgmChannel, 1);
        Assert.Equal(["load 11 Music bgm:5", "play 11 bgm:5 loop 0.0dB"], rig.Device.Commands);
        Assert.True(rig.Audio.IsChannelPlaying(Audio.BgmChannel));
        Assert.Equal(0, rig.Audio.CurrentBgmTrack);
    }

    [Fact]
    public void ChannelTwelvePlaysTheLastVoiceAgainAtItsLevelWithTheOpsStartModeAsItsVariant()
    {
        var rig = new AudioRig();
        rig.Audio.StartSoundEffect(Audio.VoiceChannel, 0);   // no voice yet
        Assert.Empty(rig.Device.Commands);
        rig.Audio.MuteNextVoice();
        rig.Audio.PlayVoice(10);
        rig.Audio.ResetMessageVoiceState();
        rig.Device.Commands.Clear();

        rig.Audio.StartSoundEffect(Audio.VoiceChannel, 1);

        Assert.Equal([$"play 12 voice:10 once {RecordingAudioDevice.Db(NativeAudioVolume.ToDecibels(0))}"],
                     rig.Device.Commands);
        Assert.True(rig.Audio.IsVoicePlaying);
        Assert.Equal(1, rig.Audio.VoicePlaybackVariant);
    }

    [Fact]
    public void ChannelTenHoldsNothingAndTheSoundEffectRouteGatesElevenAndTwelve()
    {
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Audio.PlayVoice(10);
        rig.Device.Commands.Clear();

        Assert.False(rig.Audio.ChannelHoldsSound(10));
        rig.Audio.StartSoundEffect(10, 0);
        Assert.Empty(rig.Device.Commands);

        Assert.True(rig.Audio.TrySetRouteEnabled((int)AudioMixerCategory.SoundEffect, false));
        rig.Audio.StartSoundEffect(Audio.BgmChannel, 0);
        rig.Audio.StartSoundEffect(Audio.VoiceChannel, 0);
        Assert.Empty(rig.Device.CommandsStartingWith("play"));
    }

    [Fact]
    public void AVolumeIsStoredAndAppliedToItsBusAndAPreviewIsOnlyApplied()
    {
        var rig = new AudioRig();
        Assert.True(rig.Audio.TrySetVolume((int)AudioMixerCategory.Voice, 2500));
        rig.Audio.PreviewVolume((int)AudioMixerCategory.Voice, 7500);
        Assert.Equal([$"bus-volume Voice {RecordingAudioDevice.Db(NativeAudioVolume.ToDecibels(2500))}",
                      $"bus-volume Voice {RecordingAudioDevice.Db(NativeAudioVolume.ToDecibels(7500))}"],
                     rig.Device.Commands);
        Assert.True(rig.Audio.TryGetVolume((int)AudioMixerCategory.Voice, out int stored));
        Assert.Equal(2500, stored);
    }

    [Fact]
    public void ARestoreEndsTheFadeStartsTheSavedTrackAndReloadsEachChannel()
    {
        var rig = new AudioRig();
        rig.Audio.PlayBgm(5);
        rig.Audio.FadeBgm(0, 1000);
        rig.Audio.LoadSoundEffect(40, 1);
        rig.Device.Commands.Clear();

        rig.Audio.RestoreSaved(7, [0, 0, 41, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Contains("load 11 Music bgm:7", rig.Device.Commands);
        Assert.Contains("load 2 SoundEffect sfx:41", rig.Device.Commands);
        Assert.False(rig.Device.IsLoaded(1));
        Assert.Equal([0L, 0, 41, 0, 0, 0, 0, 0, 0, 0], rig.Audio.SoundEffectResourceIds);
        rig.Advance(2000);
        Assert.Equal(7, rig.Audio.CurrentBgmTrack);   // the fade to zero ended before the restore: it cannot clear 7

        rig.Audio.RestoreSaved(0, new long[10]);
        Assert.False(rig.Device.IsLoaded(Audio.BgmChannel));
        Assert.Equal(0, rig.Audio.CurrentBgmTrack);
    }

    [Fact]
    public void AMissingTrackLeavesTheOnePlayingAndAnEmptyCatalogSendsNothing()
    {
        var rig = new AudioRig(assets: new TestAudioAssets(9));
        rig.Audio.PlayBgm(5);
        rig.Device.Commands.Clear();
        rig.Audio.PlayBgm(9);
        Assert.Empty(rig.Device.Commands);
        Assert.True(rig.Audio.IsChannelPlaying(Audio.BgmChannel));

        var empty = new AudioRig(assets: new ResourceAudioAssets(HeadlessNativesTests.EmptyResources()));
        empty.Audio.PlayBgm(0x1234);
        empty.Audio.LoadSoundEffect(0x1234, 2);
        empty.Audio.StartSoundEffect(2, 0);
        empty.Audio.PlayVoice(0x1234);
        Assert.Empty(empty.Device.Commands);
    }

    [Fact]
    public void ASilentDeviceEndsEveryPlayAtOnce()
    {
        var audio = new Audio(new SilentAudioDevice(), new TestAudioAssets(), new FrameClock(),
                              SkipAndAuto.WithSystemClock(), new AudioMixerSettings());
        var tick = new EngineTick(audio);
        audio.PlayVoice(10);
        Assert.True(audio.IsVoicePlaying);   // the native's answer until the report is taken
        tick.Service();
        Assert.False(audio.IsVoicePlaying);
    }

    [Fact]
    public void ARootReloadDropsTheDelayedStartsAndTheMuteRequest()
    {
        var rig = new AudioRig();
        rig.Audio.LoadSoundEffect(40, 2);
        rig.Audio.ScheduleSoundEffectStart(2, 0, 100);
        rig.Audio.ScheduleVoicePlayback(10, 0, 0);
        rig.Audio.MuteNextVoice();
        rig.Audio.ResetScene();
        rig.Advance(0);
        rig.Advance(500);
        Assert.Empty(rig.Device.CommandsStartingWith("play"));
        Assert.True(rig.Device.IsLoaded(2));   // what a channel holds stays

        rig.Audio.PlayVoice(11);
        Assert.Equal([(11L, false)], rig.Device.Voices);
    }
}

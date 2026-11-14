using System;
using System.Collections.Generic;
using System.Reflection;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

// The BGM and sound effect ops through the audio native (Ome.Engine.Natives.Audio) over a recording device.
public class SfxOpsTests
{
    [Fact]
    public void ReassertingCurrentBgmPreservesPlaybackUntilTrackChangesOrStops()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "BGM",
            new List<(int, Operand[])>
            {
                (0xbf, new[] { new Operand(0, 24) }),
                (0xbf, new[] { new Operand(0, 24) }), // FIELD action cleanup: keep current playback position
                (0xbf, new[] { new Operand(0, 25) }),
                (0xbf, new[] { new Operand(0, 25) }),
                (0xc2, new[] { new Operand(0, 0), new Operand(0, 0) }),
                (0xbf, new[] { new Operand(0, 25) }), // fade-to-zero cleared the current track
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var rig = new AudioRig();
        var vm = rig.Vm(script, table, new RecordingWaits());

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal([24L, 25L, 25L], rig.Device.BgmTracks);
        // The zero-length fade to zero released the source at once.
        Assert.Equal(["load 11 Music bgm:24", "play 11 bgm:24 loop 0.0dB", "load 11 Music bgm:25",
                      "play 11 bgm:25 loop 0.0dB", "unload 11", "load 11 Music bgm:25", "play 11 bgm:25 loop 0.0dB"],
                     rig.Device.Commands);
    }

    // Native op 0xc2 sets run-state 0x200 and returns; the engine tick runs the fade while ops keep dispatching.
    // SC0000 fades the title music out over 3000 ms at 0x7c1 and plays BGM005 at 0x7fa, which ends the fade at once.
    [Fact]
    public void BgmFadeRunsInTheBackgroundUntilTheNextBgmOpEndsIt()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        const int trackDuringFade = 0x100;
        const int trackAfterPlay = 0x101;
        var script = ScriptAssembler.Assemble(table, "BGM-FADE-BACKGROUND",
            new List<(int, Operand[])>
            {
                (0xbf, new[] { new Operand(0, 5) }),
                (0xc2, new[] { new Operand(0, 0), new Operand(0, 3000) }),
                (0xc8, new[] { new Operand(0, 100) }),
                (0xc0, new[] { new Operand(3, trackDuringFade) }),  // the fade has not ended: the track is retained
                (0xbf, new[] { new Operand(0, 5) }),                  // ends the fade, which clears the track first
                (0xc0, new[] { new Operand(3, trackAfterPlay) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var rig = new AudioRig();
        var host = new RecordingWaits();
        var vm = rig.Vm(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal([100L], host.SleptDurations);
        Assert.Equal(5, vm.Globals[trackDuringFade]);
        // 100 ms of a 3000 ms fade from full: progress 3, so the level is 9700 basis points when op 0xbf ends it.
        Assert.Contains($"volume 11 {RecordingAudioDevice.Db(NativeAudioVolume.ToDecibels(9700))}", rig.Device.Commands);
        Assert.Equal([5L, 5L], rig.Device.BgmTracks);   // the same id starts again: the faded source was released
        Assert.Equal(5, vm.Globals[trackAfterPlay]);
    }

    [Fact]
    public void BgmFadeToZeroThatRanItsCourseClearsTheRetainedTrack()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        const int trackAfterFade = 0x100;
        var script = ScriptAssembler.Assemble(table, "BGM-FADE-ENDED",
            new List<(int, Operand[])>
            {
                (0xbf, new[] { new Operand(0, 5) }),
                (0xc2, new[] { new Operand(0, 0), new Operand(0, 3000) }),
                (0xc8, new[] { new Operand(0, 3000) }),
                (0xc0, new[] { new Operand(3, trackAfterFade) }),
                (0xbf, new[] { new Operand(0, 5) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var rig = new AudioRig();
        var vm = rig.Vm(script, table, new RecordingWaits());

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0, vm.Globals[trackAfterFade]);
        Assert.Equal([5L, 5L], rig.Device.BgmTracks);
        Assert.Single(rig.Device.CommandsStartingWith("unload 11"));   // the tick released it at the fade's end
    }

    // Given no audio native the VM runs on a silent one, whose clock stands still (the CLI's run, play, sweep, trace
    // and oms-rebuild, GameSession): a timed fade settles at once, as a headless host's did before the audio native.
    [Fact]
    public void WithoutAnAudioNativeATimedFadeToZeroSettlesAtOnce()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        const int trackAfterFade = 0x100;
        var script = ScriptAssembler.Assemble(table, "BGM-FADE-HEADLESS",
            new List<(int, Operand[])>
            {
                (0xbf, new[] { new Operand(0, 5) }),
                (0xc2, new[] { new Operand(0, 0), new Operand(0, 3000) }),
                (0xc0, new[] { new Operand(3, trackAfterFade) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingWaits());

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0, vm.Globals[trackAfterFade]);
        const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        object audio = typeof(VirtualMachine).GetField("_audio", Instance)!.GetValue(vm)!;
        Assert.False((bool)typeof(Audio).GetField("_timedWork", Instance)!.GetValue(audio)!);   // nothing left to run
    }

    [Fact]
    public void ForcedBgmTrioRestartsWithNativeModesReusesCurrentAndStops()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        const int currentAfterStop = 0x100;
        const int currentAfterRestart = 0x101;
        var script = ScriptAssembler.Assemble(table, "BGM-FORCE",
            new List<(int, Operand[])>
            {
                (0xbf, new[] { new Operand(0, 24) }),
                (0xb7, new[] { new Operand(0, 24) }), // same track still restarts
                (0xb7, new[] { new Operand(0, 0) }),  // zero aliases retained track
                (0xb9, new[] { new Operand(0, 0) }),  // retained track, one-shot mode
                (0xb9, new[] { new Operand(0, 25) }), // nonzero replaces retained track
                (0xb8, Array.Empty<Operand>()),
                (0xc0, new[] { new Operand(3, currentAfterStop) }),
                (0xb7, new[] { new Operand(0, 0) }),  // no retained track: idempotent stop
                (0xbf, new[] { new Operand(0, 25) }),
                (0xc0, new[] { new Operand(3, currentAfterRestart) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var rig = new AudioRig();
        var vm = rig.Vm(script, table, new RecordingWaits());

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(
            [("bgm:24", true), ("bgm:24", true), ("bgm:24", true), ("bgm:24", false), ("bgm:25", false),
             ("bgm:25", true)],
            rig.Device.Plays.Where(play => play.Channel == Audio.BgmChannel).Select(play => (play.Clip, play.Loop)));
        Assert.Equal(2, rig.Device.CommandsStartingWith("unload 11").Count);
        Assert.Equal(0, vm.Globals[currentAfterStop]);
        Assert.Equal(25, vm.Globals[currentAfterRestart]);
    }

    [Fact]
    public void Sc0000AudioLifecycleDelayAndDuckControlReachTheDevice()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "SFX",
            new List<(int, Operand[])>
            {
                (0xb4, new[] { new Operand(0, 0x28), new Operand(0, 0) }),
                (0xb5, new[] { new Operand(0, 0) }),
                (0xb4, new[] { new Operand(0, 0x2aea), new Operand(0, 9) }),
                (0xba, new[] { new Operand(0, 9) }),
                (0xb4, new[] { new Operand(0, 0x2b00), new Operand(0, 4) }),
                (0x1cf, new[] { new Operand(0, 1) }),
                (0x2bf, new[] { new Operand(0, 4), new Operand(0, 0), new Operand(0, 100) }),
                (0xb6, new[] { new Operand(0, 0) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var rig = new AudioRig();
        var vm = rig.Vm(script, table, new RecordingWaits());

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(["load 0 SoundEffect sfx:40", "play 0 sfx:40 once 0.0dB", "load 9 SoundEffect sfx:10986",
                      "play 9 sfx:10986 loop 0.0dB", "load 4 SoundEffect sfx:11008", "unload 0"],
                     rig.Device.Commands);
        Assert.Equal([0L, 0, 0, 0, 0x2b00, 0, 0, 0, 0, 0x2aea], rig.Audio.SoundEffectResourceIds);   // channel 0 was released
        Assert.False(rig.Audio.IsChannelPlaying(0));

        rig.Advance(99);
        Assert.Empty(rig.Device.PlayedOn(4));   // the delayed start waits 100 ms
        rig.Advance(1);
        Assert.Equal(["sfx:11008"], rig.Device.PlayedOn(4));
    }

    [Fact]
    public void SoundChannelPlayingQueryAnswersFromTheNative()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        const int bgmResult = 0x100;
        const int unavailableResult = 0x101;
        const int endedResult = 0x102;
        var script = ScriptAssembler.Assemble(table, "SOUND-PLAYING",
            new List<(int, Operand[])>
            {
                (0xbf, new[] { new Operand(0, 7) }),
                (0x1be, new[] { new Operand(3, bgmResult), new Operand(0, 11) }),   // at once, before the device
                (0x1be, new[] { new Operand(3, unavailableResult), new Operand(0, 10) }),
                (0xc8, new[] { new Operand(0, 20) }),
                (0x1be, new[] { new Operand(3, endedResult), new Operand(0, 11) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var rig = new AudioRig();
        var host = new EndingHost(rig.Device, Audio.BgmChannel);
        var vm = new VirtualMachine(script, table, host, build: EngineBuilds.Age433G,
                                    skipAndAuto: rig.Skip, audio: rig.Audio, tick: rig.Tick);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[bgmResult]);
        Assert.Equal(0, vm.Globals[unavailableResult]);
        Assert.Equal(0, vm.Globals[endedResult]);   // the device reported the track ran out during the sleep
        Assert.Null(vm.CompatibilityFailure);
    }

    private sealed class EndingHost(RecordingAudioDevice device, int channel) : RecordingWaits
    {
        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            device.End(channel);
        }
    }

    [Fact]
    public void ActiveAdvSkipForcesBgmFadeEndpointButOrdinaryDispatchKeepsAuthoredDelay()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "BGM-FADE-SKIP",
            new List<(int, Operand[])>
            {
                (0xbf, new[] { new Operand(0, 5) }),
                (0xc2, new[] { new Operand(0, 0), new Operand(0, 3000) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var persistentSkipScript = ScriptAssembler.Assemble(table, "BGM-FADE-PERSISTENT-SKIP",
            new List<(int, Operand[])>
            {
                (0xbf, new[] { new Operand(0, 5) }),
                (0x88, new[] { new Operand(0, 1) }),
                (0x19c, Array.Empty<Operand>()),   // ADV entry starts fast-forward from persistent Skip
                (0xc2, new[] { new Operand(0, 0), new Operand(0, 3000) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var ordinary = new AudioRig();
        var physicalSkip = new AudioRig(skip: TestSkipAndAuto.HoldingFastForward());
        var persistentSkip = new AudioRig();

        ordinary.Vm(script, table, new RecordingWaits()).Run();
        physicalSkip.Vm(script, table, new RecordingWaits()).Run();
        persistentSkip.Vm(persistentSkipScript, table, new RecordingWaits()).Run();

        Assert.DoesNotContain("unload 11", ordinary.Device.Commands);   // still fading
        Assert.Contains("unload 11", physicalSkip.Device.Commands);    // the endpoint at once
        Assert.Contains("unload 11", persistentSkip.Device.Commands);
    }
}

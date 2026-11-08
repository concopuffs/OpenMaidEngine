using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

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
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal([24L, 25L, 25L], host.BgmTracks);
        Assert.Equal((0, 0L), Assert.Single(host.BgmFades));
        Assert.Equal(0, host.BgmStops);   // the host releases the source its zero-target fade reaches
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
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal([100L], host.SleptDurations);
        Assert.Equal((0, 3000L), Assert.Single(host.BgmFades));
        Assert.Equal(1, host.BgmFadeCompletions);
        Assert.Equal(5, vm.Globals[trackDuringFade]);
        Assert.Equal([5L, 5L], host.BgmTracks);   // the same id starts again: the faded source was released
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
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0, vm.Globals[trackAfterFade]);
        Assert.Equal(0, host.BgmFadeCompletions);   // nothing was left to end
        Assert.Equal([5L, 5L], host.BgmTracks);
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
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal([24L, 25L], host.BgmTracks);
        Assert.Equal([(24L, 1), (24L, 1), (24L, 0), (25L, 0)], host.BgmRestartRequests);
        Assert.Equal(2, host.BgmStops);
        Assert.Equal(0, vm.Globals[currentAfterStop]);
        Assert.Equal(25, vm.Globals[currentAfterRestart]);
    }

    [Fact]
    public void Sc0000AudioLifecycleDelayAndDuckControlReachHost()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "SFX",
            new List<(int, Operand[])>
            {
                (0xb4, new[] { new Operand(0, 0x28), new Operand(0, 0) }),
                (0xb5, new[] { new Operand(0, 0) }),
                (0xb4, new[] { new Operand(0, 0x2aea), new Operand(0, 9) }),
                (0xba, new[] { new Operand(0, 9) }),
                (0x1cf, new[] { new Operand(0, 1) }),
                (0x2bf, new[] { new Operand(0, 4), new Operand(0, 0), new Operand(0, 100) }),
                (0xb6, new[] { new Operand(0, 0) }),
                (0xc2, new[] { new Operand(0, 25), new Operand(0, 3000) }),
                (0xd9, Array.Empty<Operand>()),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal([(0x28L, 0), (0x2aeaL, 9)], host.SfxLoads);
        Assert.Equal([0, 9], host.SfxStarts);
        Assert.Equal([(0, 0), (9, 1)], host.SfxStartRequests);
        Assert.Equal(1, Assert.Single(host.VoiceBgmDuckControls));
        Assert.Equal((4, 0, 100L), Assert.Single(host.ScheduledSfxStarts));
        Assert.Equal(0, Assert.Single(host.SfxReleases));
        Assert.Equal((25, 3000L), Assert.Single(host.BgmFades));
    }

    [Fact]
    public void SoundChannelPlayingQueryWritesNormalizedHostState()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        const int bgmResult = 0x100;
        const int unavailableResult = 0x101;
        var script = ScriptAssembler.Assemble(table, "SOUND-PLAYING",
            new List<(int, Operand[])>
            {
                (0x1be, new[] { new Operand(3, bgmResult), new Operand(0, 11) }),
                (0x1be, new[] { new Operand(3, unavailableResult), new Operand(0, 10) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        host.PlayingSoundChannels.Add(11);
        var vm = new VirtualMachine(script, table, host,
            compatibility: new("kamidori", "SYS4433"));

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[bgmResult]);
        Assert.Equal(0, vm.Globals[unavailableResult]);
        Assert.Null(vm.CompatibilityFailure);
    }

    [Fact]
    public void ActiveAdvSkipForcesBgmFadeEndpointButOrdinaryDispatchKeepsAuthoredDelay()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "BGM-FADE-SKIP",
            new List<(int, Operand[])>
            {
                (0xc2, new[] { new Operand(0, 0), new Operand(0, 3000) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var persistentSkipScript = ScriptAssembler.Assemble(table, "BGM-FADE-PERSISTENT-SKIP",
            new List<(int, Operand[])>
            {
                (0x88, new[] { new Operand(0, 1) }),
                (0xc2, new[] { new Operand(0, 0), new Operand(0, 3000) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var ordinaryHost = new RecordingHost();
        var physicalSkipHost = new RecordingHost { MessageSkip = true };
        var persistentSkipHost = new RecordingHost();

        new VirtualMachine(script, table, ordinaryHost).Run();
        new VirtualMachine(script, table, physicalSkipHost).Run();
        new VirtualMachine(persistentSkipScript, table, persistentSkipHost).Run();

        Assert.Equal((0, 3000L), Assert.Single(ordinaryHost.BgmFades));
        Assert.Equal((0, 0L), Assert.Single(physicalSkipHost.BgmFades));
        Assert.Equal((0, 0L), Assert.Single(persistentSkipHost.BgmFades));
    }
}

using System;
using System.Collections.Generic;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

// Ops 0x1d5 (mute-next-voice), 0x1bc's voice stop, 0x1bf (arm-voice-duck-release), 0x1f4/0x1f5
// (statement blocks) and 0x21b/0x247 (tick presentation); docs/engine-re.md has the native decodes.
public class VoiceStatementOpsTests
{
    private static readonly Operand[] None = Array.Empty<Operand>();

    private static (VirtualMachine Vm, AudioRig Rig) Run(params (int, Operand[])[] ops)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var list = new List<(int, Operand[])>(ops) { (0x2, None) };
        var script = ScriptAssembler.Assemble(table, "VOICE", list, Array.Empty<string>());
        var rig = new AudioRig();
        var vm = rig.Vm(script, table, new RecordingWaits());
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);
        return (vm, rig);
    }

    private static Operand Imm(long value) => new(0, value);

    [Fact]
    public void MuteNextVoiceSilencesOnlyTheFollowingVoice()
    {
        var (_, rig) = Run(
            (0x1d5, None), (0xc4, new[] { Imm(10) }),   // muted: the request is promoted to 0x10001
            (0xc4, new[] { Imm(11) }),                  // a promoted request belongs to the earlier voice
            (0x1d5, None), (0x1d5, None), (0xc4, new[] { Imm(12) }),
            (0xc4, new[] { Imm(13) }));

        Assert.Equal([(10L, true), (11L, false), (12L, true), (13L, false)], rig.Device.Voices);
    }

    [Fact]
    public void ResetMessageVoiceStateStopsTheVoiceAndClearsAMuteRequest()
    {
        var (_, rig) = Run(
            (0x1d5, None), (0x1bc, None), (0xc4, new[] { Imm(10) }),
            (0x1d5, None), (0xc4, new[] { Imm(11) }), (0x1bc, None), (0xc4, new[] { Imm(12) }));

        Assert.Equal([(10L, false), (11L, true), (12L, false)], rig.Device.Voices);
        Assert.Equal(2, rig.Device.CommandsStartingWith("stop 12").Count);
        Assert.True(rig.Audio.IsVoicePlaying);
    }

    [Fact]
    public void HistoryVoiceIsAudibleAndConsumesAMuteRequest()
    {
        var (_, rig) = Run((0x1d5, None), (0x1bd, new[] { Imm(10) }), (0xc4, new[] { Imm(11) }));

        Assert.Equal([(10L, false), (11L, false)], rig.Device.Voices);
        Assert.Equal(0, rig.Audio.VoicePlaybackVariant);
        Assert.Equal(1, Run((0x1bd, new[] { Imm(10) })).Rig.Audio.VoicePlaybackVariant);
    }

    [Fact]
    public void ArmVoiceDuckReleaseFadesTheBgmBackOnceTheVoiceEnds()
    {
        var (_, rig) = Run((0xbf, new[] { Imm(5) }), (0xc4, new[] { Imm(10) }), (0x1bf, None));
        string ducked = $"volume 11 {RecordingAudioDevice.Db(NativeAudioVolume.ToDecibels(5000))}";
        Assert.Equal([ducked], rig.Device.CommandsStartingWith("volume 11"));   // sound:MusicFadeOnVoicePlayingVolume 50

        rig.Advance(500);
        Assert.Single(rig.Device.CommandsStartingWith("volume 11"));   // the voice still plays: the duck holds
        rig.Device.End(12);
        rig.Advance(0);   // the end is reported: the release starts
        rig.Advance(1000);
        Assert.Equal($"volume 11 {RecordingAudioDevice.Db(0)}", rig.Device.CommandsStartingWith("volume 11")[^1]);
    }

    [Fact]
    public void OnlyTheOutermostStatementBlockCloses()
    {
        (int, Operand[]) open = (0x1f4, None), close = (0x1f5, None);

        Assert.True(Run(open, open, close).Rig.Tick.StatementBlockOpen);
        Assert.False(Run(open, open, close, close).Rig.Tick.StatementBlockOpen);
        Assert.True(Run(open, close, close, open).Rig.Tick.StatementBlockOpen);   // an unmatched close is ignored
        Assert.False(Run(open, close, close, open, close).Rig.Tick.StatementBlockOpen);
    }

    [Fact]
    public void TickPresentationIsStoredAndQueried()
    {
        var (vm, _) = Run(
            (0x247, new[] { new Operand(3, 0x100) }),
            (0x21b, new[] { Imm(5) }), (0x247, new[] { new Operand(3, 0x101) }),
            (0x21b, new[] { Imm(0) }), (0x247, new[] { new Operand(3, 0x102) }));

        Assert.Equal(0, vm.Globals[0x100]);
        Assert.Equal(1, vm.Globals[0x101]);
        Assert.Equal(0, vm.Globals[0x102]);
    }
}

using System;
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

// Ops 0x1d5 (mute-next-voice), 0x1bc's voice stop, 0x1bf (arm-voice-duck-release), 0x1f4/0x1f5
// (statement blocks) and 0x21b/0x247 (tick presentation); docs/engine-re.md has the native decodes.
public class VoiceStatementOpsTests
{
    private static readonly Operand[] None = Array.Empty<Operand>();

    private static (VirtualMachine Vm, RecordingHost Host) Run(params (int, Operand[])[] ops)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var list = new List<(int, Operand[])>(ops) { (0x2, None) };
        var script = ScriptAssembler.Assemble(table, "VOICE", list, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);
        return (vm, host);
    }

    private static Operand Imm(long value) => new(0, value);

    [Fact]
    public void MuteNextVoiceSilencesOnlyTheFollowingVoice()
    {
        var (_, host) = Run(
            (0x1d5, None), (0xc4, new[] { Imm(10) }),   // muted: the request is promoted to 0x10001
            (0xc4, new[] { Imm(11) }),                  // a promoted request belongs to the earlier voice
            (0x1d5, None), (0x1d5, None), (0xc4, new[] { Imm(12) }),
            (0xc4, new[] { Imm(13) }));

        Assert.Equal([(10L, 0, true), (11L, 0, false), (12L, 0, true), (13L, 0, false)], host.VoicePlaybacks);
    }

    [Fact]
    public void ResetMessageVoiceStateStopsTheVoiceAndClearsAMuteRequest()
    {
        var (_, host) = Run(
            (0x1d5, None), (0x1bc, None), (0xc4, new[] { Imm(10) }),
            (0x1d5, None), (0xc4, new[] { Imm(11) }), (0x1bc, None), (0xc4, new[] { Imm(12) }));

        Assert.Equal([(10L, 0, false), (11L, 0, true), (12L, 0, false)], host.VoicePlaybacks);
        Assert.Equal(2, host.VoiceStops);
    }

    [Fact]
    public void HistoryVoiceIsAudibleAndConsumesAMuteRequest()
    {
        var (_, host) = Run((0x1d5, None), (0x1bd, new[] { Imm(10) }), (0xc4, new[] { Imm(11) }));

        Assert.Equal([(10L, 1, false), (11L, 0, false)], host.VoicePlaybacks);
    }

    [Fact]
    public void ArmVoiceDuckReleaseReachesTheHost()
    {
        var (_, host) = Run((0xc4, new[] { Imm(10) }), (0x1bf, None), (0x1bf, None));

        Assert.Equal(2, host.VoiceBgmDuckReleaseArms);
    }

    [Fact]
    public void OnlyTheOutermostStatementBlockReachesTheHost()
    {
        var (_, host) = Run(
            (0x1f4, None), (0x1f4, None), (0x1f5, None), (0x1f5, None),
            (0x1f5, None),                              // an unmatched close is ignored, as natively
            (0x1f4, None), (0x1f5, None));

        Assert.Equal([true, false, true, false], host.StatementBlockChanges);
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

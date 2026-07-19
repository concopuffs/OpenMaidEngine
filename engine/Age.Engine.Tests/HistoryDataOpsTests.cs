using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class HistoryDataOpsTests
{
    private const int T_IMM = 0, T_GINT = 3, T_LINT = 9;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(T_IMM, value);
    private static Operand G(int address) => new(T_GINT, address);
    private static Operand L(int address) => new(T_LINT, address);

    private sealed class HistoryVoiceHost : RecordingHost
    {
        public AdvAutoWaitState AutoState;

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                                          Func<AdvAutoWaitState> autoWaitState)
        {
            AutoState = autoWaitState();
            Waits++;
        }
    }

    [Fact]
    public void CopyInlineIntArrayWritesConsecutiveSignedDwords()
    {
        const int arrayOffset = 20;
        var body = new uint[arrayOffset + 5];
        body[arrayOffset] = 4;
        body[arrayOffset + 1] = 0;
        body[arrayOffset + 2] = 5;
        body[arrayOffset + 3] = 0xffff_ff74;
        body[arrayOffset + 4] = 538;
        var instructions = new[]
        {
            new Instruction(0, 0x64, new[] { L(4), I(arrayOffset) }),
            new Instruction(5, 0x55, new[] { G(0x100), L(4) }),
            new Instruction(10, 0x55, new[] { G(0x101), L(5) }),
            new Instruction(15, 0x55, new[] { G(0x102), L(6) }),
            new Instruction(20, 0x55, new[] { G(0x103), L(7) }),
            new Instruction(25, 0x2, Array.Empty<Operand>()),
        };
        var script = new Script
        {
            Name = "INLINE_ARRAY",
            Header = new ScriptHeader(0, 0, 0, 0, 0, 0),
            Instructions = instructions,
            IndexByOffset = instructions.Select((ins, index) => (ins.Offset, index))
                .ToDictionary(pair => pair.Offset, pair => pair.index),
            Strings = new Dictionary<int, string>(),
            BodyDwords = body,
        };

        var vm = new VirtualMachine(script, Table, new RecordingHost());
        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new long[] { 0, 5, -140, 538 },
            new[] { vm.Globals[0x100], vm.Globals[0x101], vm.Globals[0x102], vm.Globals[0x103] });
    }

    [Fact]
    public void LoaderRetainsRealHistoryFooterArraysForRuntimeCopy()
    {
        var history = Sys4Loader.Load(Paths.Scripts()["HISTORY.BIN"], Table);

        Assert.Equal(4u, history.BodyDwords[0x13d4]);
        Assert.Equal(new uint[] { 0, 5, 0, 538 }, history.BodyDwords.Skip(0x13d5).Take(4));
        Assert.Equal(14u, history.BodyDwords[0x1424]);
        Assert.Equal(0xffff_ff74u, history.BodyDwords[0x1424 + 10]);
    }

    [Theory]
    [InlineData(3, 30)]
    [InlineData(5, 50)]
    [InlineData(9, -1)]
    public void ValueSwitchJumpsToMatchingCaseOrDefault(int selector, int expected)
    {
        var script = ScriptAssembler.Assemble(Table, "VALUE_SWITCH",
            new List<(int, Operand[])>
            {
                (0xa1, Array.Empty<Operand>()),                         // 0
                (0xa2, new[] { I(3), I(16) }),                         // 1
                (0xa2, new[] { I(5), I(24) }),                         // 6
                (0xa3, new[] { G(0x100), I(32) }),                     // 11
                (0x55, new[] { G(0x101), I(30) }),                     // 16
                (0x8c, new[] { I(37) }),                              // 21
                (0x55, new[] { G(0x101), I(50) }),                     // 24
                (0x8c, new[] { I(37) }),                              // 29
                (0x55, new[] { G(0x101), I(-1) }),                     // 32
                (0x2, Array.Empty<Operand>()),                         // 37
            }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost());
        vm.Globals[0x100] = selector;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(expected, unchecked((int)vm.Globals[0x101]));
    }

    [Fact]
    public void HistoryOpcodesStepFromLatestBoundaryAndQueryOneGroup()
    {
        var history = new AdvTextHistory();
        history.ResetLayout(1);
        history.AppendMetadata(111, 1, AdvTextStyle.Default);
        history.AppendText(0, 10, "older", AdvTextStyle.Default);
        history.ResetLayout(2);
        history.AppendMetadata(222, 2, AdvTextStyle.Default);
        history.AppendVoice(77, 9, AdvTextStyle.Default);
        history.AppendText(0, 11, "target", AdvTextStyle.Default);
        history.ResetLayout(3);
        history.AppendText(0, 12, "latest", AdvTextStyle.Default);

        var script = ScriptAssembler.Assemble(Table, "HISTORY_QUERY",
            new List<(int, Operand[])>
            {
                (0x51, new[] { L(10), I(0), I(1) }),                  // delta = -1
                (0x1d0, new[] { L(0), L(1), L(10) }),
                (0x55, new[] { G(0x100), L(0) }),
                (0x55, new[] { G(0x101), L(1) }),
                (0x1d3, new[] { L(2), L(3), I(1), L(1), I(2) }),
                (0x55, new[] { G(0x102), L(2) }),
                (0x55, new[] { G(0x103), L(3) }),
                (0x1d4, new[] { L(4), L(5), I(1), L(1) }),
                (0x55, new[] { G(0x104), L(4) }),
                (0x55, new[] { G(0x105), L(5) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost(), textHistory: history);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(2, vm.Globals[0x100]);
        Assert.Equal(2, vm.Globals[0x101]);
        Assert.Equal(1, vm.Globals[0x102]);
        Assert.Equal(222, vm.Globals[0x103]);
        Assert.Equal(77, vm.Globals[0x104]);
        Assert.Equal(9, vm.Globals[0x105]);
    }

    [Fact]
    public void StepSkipsFilteredAndDuplicateLogicalEntries()
    {
        var history = new AdvTextHistory();
        history.ResetLayout(1);
        history.AppendText(0, 1, "visible", AdvTextStyle.Default);
        history.ResetLayout(2);
        history.ResetLayout(2); // duplicate first-record index, as native structural calls may produce
        history.AppendText(0, 2, "filtered", AdvTextStyle.Default,
            AdvTextHistoryRecordFlags.NavigationFiltered);
        history.ResetLayout(3);
        history.AppendText(0, 3, "latest", AdvTextStyle.Default);

        Assert.True(history.TryStepGroup(-1, out var prior));
        Assert.Equal(new AdvTextHistoryEntry(1, 0), prior);
        Assert.False(history.TryStepGroup(-2, out var boundary));
        Assert.Equal(new AdvTextHistoryEntry(-1, -1), boundary);
    }

    [Fact]
    public void MissingHistoryQueriesUseNativeOutputDefaults()
    {
        var history = new AdvTextHistory();
        history.ResetLayout(1);
        history.AppendText(0, 1, "plain", AdvTextStyle.Default);

        Assert.False(history.TryFindMetadata(0, 7, out long metadata));
        Assert.Equal(0, metadata);
        Assert.False(history.TryFindVoicePair(0, out long voiceId, out long voiceArgument));
        Assert.Equal((-1L, -1L), (voiceId, voiceArgument));
    }

    [Fact]
    public void PlayHistoryVoiceUsesNativeVariantAndAutoServices()
    {
        var history = new AdvTextHistory();
        var script = ScriptAssembler.Assemble(Table, "HISTORY_VOICE_REPLAY",
            new List<(int, Operand[])>
            {
                (0x1bd, new[] { I(77) }),
                (0x72, new[] { I(1) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new HistoryVoiceHost();
        var vm = new VirtualMachine(script, Table, host, textHistory: history);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new long[] { 77 }, host.Voices);
        Assert.Equal(new[] { (77L, 1) }, host.VoiceRequests);
        Assert.True(host.AutoState.VoicePending);
        var voice = Assert.Single(history.Records);
        Assert.Equal(AdvTextHistoryRecordKind.Voice, voice.Kind);
        Assert.Equal((77L, 1L), (voice.Value, voice.AuxValue));
    }

    [Fact]
    public void SuppressedHistoryReplayDoesNotRecordItself()
    {
        var history = new AdvTextHistory();
        history.SetRecordingEnabled(false);
        var script = ScriptAssembler.Assemble(Table, "HISTORY_VOICE_REPLAY_SUPPRESSED",
            new List<(int, Operand[])>
            {
                (0x1bd, new[] { I(88) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, Table, host, textHistory: history);

        vm.Run();

        Assert.Equal(new[] { (88L, 1) }, host.VoiceRequests);
        Assert.Empty(history.Records);
    }
}

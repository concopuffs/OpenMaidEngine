using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class AdvTextHistoryTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand S(int index) => new(2, index);

    [Fact]
    public void OrdinaryAdvOpsBuildGroupedStyledMetadataVoiceAndTextRecords()
    {
        var script = ScriptAssembler.Assemble(Table, "HISTORY_WRITE",
            new List<(int, Operand[])>
            {
                (0x70, new[] { I(1), I(640), I(160), I(80), I(430) }),
                (0x75, new[] { I(24) }),
                (0x76, new[] { I(0xf0e0d0) }),
                (0x7a, new[] { I(0), I(12), I(34) }),
                (0x1d2, new[] { I(1), I(123) }),
                (0xc4, new[] { I(77) }),
                (0x6e, new[] { I(0), S(0) }),
                (0x71, new[] { I(1) }),
                (0x6e, new[] { I(0), S(1) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "first", "second" });

        var vm = new VirtualMachine(script, Table, new RecordingHost());
        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new[]
        {
            new AdvTextHistoryEntry(1, 0),
            new AdvTextHistoryEntry(1, 3),
        }, vm.TextHistory.Entries);

        Assert.Collection(vm.TextHistory.Records,
            metadata =>
            {
                Assert.Equal(AdvTextHistoryRecordKind.Metadata, metadata.Kind);
                Assert.Equal(AdvTextHistoryRecordFlags.TypedMetadata | AdvTextHistoryRecordFlags.GroupStart,
                             metadata.Flags);
                Assert.Equal(123, metadata.Value);
                Assert.Equal(1, metadata.AuxValue);
                Assert.Equal((24, 0xf0e0d0L), (metadata.Style.PrimaryFontSize, metadata.Style.TextColor));
                Assert.Equal(new AdvTextLayoutSnapshot(1, 640, 160, 80, 430, 12, 34), metadata.Layout);
            },
            voice =>
            {
                Assert.Equal(AdvTextHistoryRecordKind.Voice, voice.Kind);
                Assert.Equal(AdvTextHistoryRecordFlags.VoicePair, voice.Flags);
                Assert.Equal((77L, 0L), (voice.Value, voice.AuxValue));
            },
            text =>
            {
                Assert.Equal(AdvTextHistoryRecordKind.Text, text.Kind);
                Assert.Equal("first", text.Text);
                Assert.Equal(AdvTextHistoryRecordFlags.None, text.Flags);
            },
            text =>
            {
                Assert.Equal(AdvTextHistoryRecordKind.Text, text.Kind);
                Assert.Equal("second", text.Text);
                Assert.Equal(AdvTextHistoryRecordFlags.GroupStart, text.Flags);
                Assert.Equal((0, 0), (text.Layout.CursorX, text.Layout.CursorY));
            });
    }

    [Fact]
    public void RecordingSuppressionBlocksEveryWriterUntilReenabled()
    {
        var script = ScriptAssembler.Assemble(Table, "HISTORY_SUPPRESS",
            new List<(int, Operand[])>
            {
                (0x1bb, new[] { I(0) }),
                (0x70, new[] { I(2), I(500), I(100), I(10), I(20) }),
                (0x1d2, new[] { I(2), I(9) }),
                (0xc4, new[] { I(88) }),
                (0x6e, new[] { I(0), S(0) }),
                (0x1bb, new[] { I(1) }),
                (0x71, new[] { I(2) }),
                (0x6e, new[] { I(0), S(1) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "hidden", "retained" });

        var vm = new VirtualMachine(script, Table, new RecordingHost());
        vm.Run();

        Assert.False(vm.TextHistory.RecordingSuppressed);
        Assert.Equal(new AdvTextHistoryEntry(2, 0), Assert.Single(vm.TextHistory.Entries));
        var record = Assert.Single(vm.TextHistory.Records);
        Assert.Equal("retained", record.Text);
        Assert.True(record.Flags.HasFlag(AdvTextHistoryRecordFlags.GroupStart));
    }

    [Fact]
    public void ClearDropsRecordsIndexAndPendingGroupStartButRetainsLayoutDefinition()
    {
        var history = new AdvTextHistory();
        history.DefineLayout(3, 320, 90, 20, 400);
        history.AppendText(0, 10, "old", AdvTextStyle.Default);

        history.Clear();
        history.AppendText(0, 11, "new", AdvTextStyle.Default);

        Assert.Empty(history.Entries);
        var record = Assert.Single(history.Records);
        Assert.Equal("new", record.Text);
        Assert.False(record.Flags.HasFlag(AdvTextHistoryRecordFlags.GroupStart));
        Assert.Equal(new AdvTextLayoutSnapshot(3, 320, 90, 20, 400, 0, 0), record.Layout);
    }

    [Fact]
    public void GameSessionSharesOneLiveHistoryAcrossSceneVms()
    {
        Script Scene(string name, string text) => ScriptAssembler.Assemble(Table, name,
            new List<(int, Operand[])>
            {
                (0x71, new[] { I(1) }),
                (0x6e, new[] { I(0), S(0) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { text });

        var session = new GameSession();
        session.RunScene(Scene("FIRST", "one"), Table, new RecordingHost());
        session.RunScene(Scene("SECOND", "two"), Table, new RecordingHost());

        Assert.Equal(new[] { "one", "two" }, session.TextHistory.Records.Select(r => r.Text));
        Assert.Equal(new[]
        {
            new AdvTextHistoryEntry(1, 0),
            new AdvTextHistoryEntry(1, 1),
        }, session.TextHistory.Entries);
    }

    [Fact]
    public void RealSc0000FirstPagePopulatesTheRetainedBacklogBeforeItsWait()
    {
        var scripts = Sys4ScriptProvider.Load(Table);
        var scene = scripts.RequireByName("SC0000.BIN");
        var vm = new VirtualMachine(scene, Table, new RecordingHost(),
            new VmOptions(MaxSteps: 1_000_000, HaltAtWaitForInput: true), scripts);

        vm.Run();

        Assert.Equal("wait-for-input", vm.HaltReason);
        Assert.NotEmpty(vm.TextHistory.Entries);
        var text = Assert.Single(vm.TextHistory.Records.Where(r => r.Kind == AdvTextHistoryRecordKind.Text));
        Assert.Equal(0x14963, text.SourceOffset);
        Assert.NotEmpty(text.Text);
        Assert.True(text.Flags.HasFlag(AdvTextHistoryRecordFlags.GroupStart));
    }
}

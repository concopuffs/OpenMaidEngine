using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class TextRedrawOpsTests
{
    private const int Immediate = 0, GlobalInt = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(Immediate, value);
    private static Operand G(int address) => new(GlobalInt, address);

    [Fact]
    public void QueryThenRedrawRestoresTheNewestRetainedAdvGroup()
    {
        var history = new AdvTextHistory();
        history.DefineLayout(1, 640, 160, 80, 430);
        history.AppendText(1, 0x100, "older", AdvTextStyle.Default);
        history.ResetLayout(1);
        history.AppendText(1, 0x200, "restored", AdvTextStyle.Default with
        {
            TextColor = 0x112233,
            EffectColor = 0x445566,
        });

        var script = ScriptAssembler.Assemble(Table, "CONFIG_REDRAW",
        [
            (0x83, [G(0x10), G(0x11), G(0x12)]),
            (0x82, [G(0x11), G(0x12), I(2), I(0xffffff), I(0x123456)]),
            (0x2, Array.Empty<Operand>()),
        ], []);
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, Table, host, textHistory: history);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0, vm.Globals[0x10]);
        Assert.Equal(1, vm.Globals[0x11]);
        Assert.Equal(1, vm.Globals[0x12]);
        // A redraw is not a History presentation, which History's exit would erase.
        Assert.Empty(host.HistoryRenders);
        var (binding, redraw) = Assert.Single(host.Redraws);
        Assert.Equal((1, 1, 1, 2), (binding.LayoutSlot, redraw.LayoutSlot, redraw.FirstRecordIndex, redraw.Flags));
        AdvTextRedrawRecord record = Assert.Single(redraw.Records);
        Assert.Equal(("restored", 0xffffffL, 0x123456L),
            (record.Text, record.Style.TextColor, record.Style.EffectColor));
    }

    [Fact]
    public void ARedrawKeepsEachRecordsColorsAndHeightInTheCurrentStyle()
    {
        // text_redraw_retained_group@0x4553c0 restores each record's colors and pixel height; face, weight, effect and
        // render mode are the text manager's current ones, and a ruby record draws in mode 0.
        var history = new AdvTextHistory();
        history.DefineLayout(1, 640, 160, 80, 430);
        AdvTextStyle recorded = AdvTextStyle.Default with
        {
            PrimaryFontSize = 24, TextColor = 0x112233, EffectColor = 0x445566, RenderMode = 3, FontFace = "old",
        };
        history.AppendText(1, 0, "first", recorded,
            rows: [new AdvTextRow(0, 5, 0, 0, 120, 24, WrapsAfter: true)],
            ruby: [new AdvRubyGlyph(0, "る", new AdvRetainedGlyphRecord(1, 3, -8, 11, 0))]);
        history.AppendText(1, 0, "filtered", recorded, AdvTextHistoryRecordFlags.NavigationFiltered);
        AdvTextStyle current = AdvTextStyle.Default with
        {
            PrimaryFontSize = 20, TextColor = 0xffffff, EffectColor = 0, RenderMode = 1, FontFace = "new", Bold = true,
        };

        Assert.True(history.TryBuildRedrawBatch(1, 0, 0, 0, 0, current, out AdvTextRedrawBatch batch));

        // The line break draws nothing, and the navigation-filtered record ends the walk without flag 1.
        Assert.Equal(
            new[]
            {
                ("first", false, 24, 0x112233L, 0x445566L, 1, "new", true, new AdvTextHistoryRect(0, 0, 120, 24)),
                ("る", true, 8, 0x112233L, 0x445566L, 0, "new", true, new AdvTextHistoryRect(3, -8, 11, 0)),
            },
            batch.Records.Select(record => (record.Text, record.IsRuby, record.Style.PrimaryFontSize,
                record.Style.TextColor, record.Style.EffectColor, record.Style.RenderMode, record.Style.FontFace,
                record.Style.Bold, record.Rect)));

        Assert.True(history.TryBuildRedrawBatch(1, 0, 1, 0, 0, current, out AdvTextRedrawBatch all));
        Assert.Equal(new[] { "first", "る", "filtered" }, all.Records.Select(record => record.Text));
    }

    [Fact]
    public void QueryWithoutRetainedEntriesReturnsNativeSentinels()
    {
        var script = ScriptAssembler.Assemble(Table, "EMPTY_REDRAW",
        [
            (0x83, [G(0x10), G(0x11), G(0x12)]),
            (0x2, Array.Empty<Operand>()),
        ], []);
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(0, vm.Globals[0x10]);
        Assert.Equal(-1, vm.Globals[0x11]);
        Assert.Equal(-1, vm.Globals[0x12]);
    }
}

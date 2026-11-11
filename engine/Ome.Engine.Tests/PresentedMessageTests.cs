using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Localization;
using Ome.Script.Abi;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>The VM's side of localization milestone 2: messages (gate 4, spec §6.2), the record of string stores and
/// the lookup by origin (§5.2, §6.4), and the length opcodes (gate 6, §5.3).</summary>
public class PresentedMessageTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);

    private static Operand I(long value) => new(0, value);
    private static Operand S(int index) => new(2, index);
    private static Operand G(int cell) => new(3, cell);
    private static Operand GS(int cell) => new(5, cell);

    private static TextPresenter Presenter(TextMissLog? misses, params TextEntry[] entries)
        => new(new TextLanguage("en", [TextCatalog.Build("en", entries)]), misses: misses,
               messages: new ScriptMessageIndex(Table, Contracts));

    private static (RecordingHost Host, VirtualMachine Vm) Run(TextPresenter? presenter,
        IReadOnlyList<(int, Operand[])> code, IReadOnlyList<string> strings, bool records = true)
    {
        Ome.Engine.Model.Script script = ScriptAssembler.Assemble(Table, "SC0100", code, strings);
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, Table, host) { Presenter = presenter, RecordsStringStores = records };
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);
        return (host, vm);
    }

    /// <summary>あの, 黒石 with its ruby, a line break, を拾った。: one message (spec §6.2's example).</summary>
    private static readonly string[] Sentence = ["あの", "黒石", "くろいし", "を拾った。"];

    private static List<(int, Operand[])> SentenceCode(bool branchIntoLastRun = false)
    {
        var code = new List<(int, Operand[])>
        {
            (0x6e, [I(0), S(0)]),              // 0x0
            (0x196, [I(0), S(1), S(2)]),       // 0x5
            (0x6f, [I(0)]),                    // 0xc
            (0x6e, [I(0), S(3)]),              // 0xf
            (0x2, []),
        };
        if (branchIntoLastRun) code.Add((0x8c, [I(0xf)]));   // never runs; makes 0xf a branch target
        return code;
    }

    [Fact]
    public void ASentenceSplitByFuriganaPresentsAsOne()
    {
        var misses = new TextMissLog();
        var (host, vm) = Run(Presenter(misses, TextEntry.Literal("あの黒石を拾った。", "I picked up that black stone.")),
            SentenceCode(), Sentence);

        var drawn = Assert.Single(host.LiveTextRuns).Run;
        Assert.Equal(("あの", "I picked up that black stone."), (drawn.Text, drawn.Presented?.Text));
        // Every run still reaches history, the page map and the oracle as source text.
        Assert.Equal(new[] { "あの", "黒石", "を拾った。" }, host.Lines.Select(line => line.Text));
        Assert.Equal(new[] { "あの", "黒石", "を拾った。" }, vm.Emitted.Select(emitted => emitted.Text));
        Assert.Equal(new[] { "あの", "黒石", "", "を拾った。" }, vm.TextHistory.Records.Select(record => record.Text));
        Assert.Equal(0, misses.Count);
        // The line break inside the message moves no cursor: the sentence draws its own breaks.
        Assert.Empty(host.TextCursors);
    }

    [Fact]
    public void ABranchTargetInsideTheRunsSplitsTheMessage()
    {
        var (host, _) = Run(Presenter(null,
                TextEntry.Literal("あの黒石を拾った。", "I picked up that black stone."),
                TextEntry.Literal("あの黒石", "That black stone"),
                TextEntry.Literal("を拾った。", " was picked up.")),
            SentenceCode(branchIntoLastRun: true), Sentence);

        Assert.Equal(new[] { "That black stone", " was picked up." },
                     host.LiveTextRuns.Select(run => run.Run.Presented?.Text));
    }

    [Fact]
    public void AMissFallsBackRunByRunAndLogsTheMessage()
    {
        var misses = new TextMissLog();
        var (host, _) = Run(Presenter(misses, TextEntry.Literal("黒石", "black stone")), SentenceCode(), Sentence);

        Assert.Equal(new[] { null, "black stone", null }, host.LiveTextRuns.Select(run => run.Run.Presented?.Text));
        Assert.Null(host.LiveTextRuns[1].Run.Ruby);   // a presented run keeps no ruby
        var written = new StringWriter();
        misses.WriteJsonLines(written);
        Assert.Contains("\"source\":\"あの黒石を拾った。\"", written.ToString());
        Assert.Equal(1, misses.Count);
        Assert.Single(host.TextCursors);   // the break moves the cursor: no message was presented
    }

    [Fact]
    public void AMessageTheHostCutIsReportedOnceWithItsRows()
    {
        // The host draws the presented sentence and cuts two rows of it (spec §7.3).
        var misses = new TextMissLog();
        Ome.Engine.Model.Script script = ScriptAssembler.Assemble(Table, "SC0100", SentenceCode(), Sentence);
        var host = new RecordingHost
        {
            OnRetainedText = run => run.Presented == null ? null
                : new AdvRetainedTextRunResult(run.Layout.Slot, 0, 4, 40, 0) { CutRows = 2 },
        };
        var vm = new VirtualMachine(script, Table, host)
        {
            Presenter = Presenter(misses, TextEntry.Literal("あの黒石を拾った。", "I picked up that black stone.")),
        };
        vm.Run();

        var written = new StringWriter();
        misses.WriteJsonLines(written);
        Assert.Equal("{\"source\":\"あの黒石を拾った。\",\"script\":\"SC0100\",\"at\":\"0x0\",\"count\":1,\"rows_cut\":2}",
                     written.ToString().Trim());
        Assert.Equal((0, 1), (misses.Count, misses.CutCount));
    }

    [Fact]
    public void AMessageTheHostNarrowedToFitIsReportedWithItsWidth()
    {
        // The host narrows the presented sentence to 91 % so that it is not cut (spec §7.3).
        var misses = new TextMissLog();
        Ome.Engine.Model.Script script = ScriptAssembler.Assemble(Table, "SC0100", SentenceCode(), Sentence);
        var host = new RecordingHost
        {
            OnRetainedText = run => run.Presented == null ? null
                : new AdvRetainedTextRunResult(run.Layout.Slot, 0, 4, 40, 0) { NarrowedToFitPercent = 91 },
        };
        var vm = new VirtualMachine(script, Table, host)
        {
            Presenter = Presenter(misses, TextEntry.Literal("あの黒石を拾った。", "I picked up that black stone.")),
        };
        vm.Run();

        var written = new StringWriter();
        misses.WriteJsonLines(written);
        Assert.Equal("{\"source\":\"あの黒石を拾った。\",\"script\":\"SC0100\",\"at\":\"0x0\",\"count\":1,\"width_percent\":91}",
                     written.ToString().Trim());
        Assert.Equal((0, 1, 0), (misses.Count, misses.NarrowedCount, misses.CutCount));
    }

    // ---- the record of stores -------------------------------------------------------------------------------------

    private static readonly TextEntry[] StoneEntries =
    [
        TextEntry.Literal("黒石", "the stone the table stored") with { Script = "SC0100", At = 0 },
        TextEntry.Literal("黒石", "Black Stone"),
        TextEntry.Literal("黒", "Black"),
        TextEntry.Literal("黒", "a stale origin") with { Script = "SC0100", At = 0 },
    ];

    private static List<(int, Operand[])> StoreAndDraw(params (int, Operand[])[] between)
    {
        var code = new List<(int, Operand[])> { (0x192, [GS(5), S(0)]) };   // set-string at 0x0
        code.AddRange(between);
        code.Add((0x204, [I(13), I(1), I(1), GS(5)]));
        code.Add((0x204, [I(13), I(2), I(2), GS(6)]));
        code.Add((0x2, []));
        return code;
    }

    [Fact]
    public void AStringDrawnFromACellIsScopedToTheInstructionThatStoredIt()
    {
        var (host, _) = Run(Presenter(null, StoneEntries), StoreAndDraw((0x55, [GS(6), GS(5)])), ["黒石"]);

        // The mov copied a recorded cell, so G6 keeps the set-string's origin too.
        Assert.Equal(new[] { "the stone the table stored", "the stone the table stored" },
                     host.SurfaceStrings.Select(drawn => drawn.Text));
    }

    [Fact]
    public void WithoutRecordsTheDrawingSiteScopesTheString()
    {
        var (host, _) = Run(Presenter(null, StoneEntries), StoreAndDraw((0x55, [GS(6), GS(5)])), ["黒石"],
                            records: false);

        Assert.Equal(new[] { "Black Stone", "Black Stone" }, host.SurfaceStrings.Select(drawn => drawn.Text));
    }

    [Fact]
    public void AWriteThatChangesTheTextCancelsTheRecord()
    {
        // cp932-substring keeps 黒 of 黒石 and records nothing: the cell no longer holds the recorded text.
        var (host, _) = Run(Presenter(null, StoneEntries),
            StoreAndDraw((0x2c8, [GS(5), GS(5), I(0), I(1)]), (0x55, [GS(6), GS(5)])), ["黒石"]);

        Assert.Equal(new[] { "Black", "Black" }, host.SurfaceStrings.Select(drawn => drawn.Text));
    }

    // ---- measurement (gate 6) -------------------------------------------------------------------------------------

    private static int[] Lengths(TextPresenter? presenter)
    {
        var (_, vm) = Run(presenter, new List<(int, Operand[])>
        {
            (0x192, [GS(5), S(0)]),      // the speaker's name
            (0x1a6, [G(0x4f8), GS(5)]),  // halve-strlen
            (0x2c5, [G(0x4f9), GS(5)]),  // strlen
            (0x2c6, [G(0x4fa), GS(5)]),  // cp932-character-length
            (0x2, []),
        }, ["アリア"]);
        return [(int)vm.Globals[0x4f8], (int)vm.Globals[0x4f9], (int)vm.Globals[0x4fa]];
    }

    [Fact]
    public void TheLengthOpcodesMeasureThePresentedName()
    {
        Assert.Equal(new[] { 3, 6, 3 }, Lengths(null));
        Assert.Equal(new[] { 4, 8, 8 }, Lengths(Presenter(null, TextEntry.Literal("アリア", "Illumina"))));
    }

    [Fact]
    public void AnEntrysCellsOverrideTheCountAndSourceMeasurementOptsOut()
    {
        Assert.Equal(new[] { 5, 10, 8 },
                     Lengths(Presenter(null, TextEntry.Literal("アリア", "Illumina") with { Cells = 10 })));
        Assert.Equal(new[] { 3, 6, 3 },
                     Lengths(Presenter(null, TextEntry.Literal("アリア", "Illumina") with { Measure = TextMeasure.Source })));
        var sourceLanguage = new TextPresenter(new TextLanguage("en",
            [TextCatalog.Build("en", [TextEntry.Literal("アリア", "Illumina")])]) { Measure = TextMeasure.Source });
        Assert.Equal(new[] { 3, 6, 3 }, Lengths(sourceLanguage));
    }

    [Fact]
    public void AMissAndThePseudoLanguageMeasureAsNative()
    {
        Assert.Equal(new[] { 3, 6, 3 }, Lengths(Presenter(null, TextEntry.Literal("イリス", "Iris"))));
        Assert.Equal(new[] { 3, 6, 3 }, Lengths(new TextPresenter(TextLanguage.Pseudo, [new PseudoLanguageFilter()])));
    }
}

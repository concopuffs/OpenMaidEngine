using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Localization;
using Ome.Script.Abi;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Localization milestone 3 (spec §8): the source layout pass, the two cursors, and history presented again
/// (gate 7).</summary>
public class PresentedHistoryTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);

    private static Operand I(long value) => new(0, value);
    private static Operand S(int index) => new(2, index);

    private static TextPresenter Presenter(string tag, params TextEntry[] entries)
        => new(new TextLanguage(tag, [TextCatalog.Build(tag, entries)]), messages: new ScriptMessageIndex(Table, Contracts));

    private static TextPresenter English(params TextEntry[] extra) => Presenter("en",
    [
        TextEntry.Literal("黒石", "Black Stone"),
        TextEntry.Literal("あの黒石を拾った。", "I picked up that black stone."),
        .. extra,
    ]);

    /// <summary>A host that lays text out on a grid: drawn glyphs are 10 pixels wide, source glyphs 20, and a
    /// presented run wraps once. Every run that draws something else also returns its source layout.</summary>
    private static RecordingHost GridHost() => new()
    {
        OnRetainedText = run =>
        {
            string drawn = run.Presented?.Text ?? run.Text;
            int x = run.Layout.CursorX, y = run.Layout.CursorY;
            (int endX, int endY) = run.Presented != null ? (50, y + 30) : (x + 10 * drawn.Length, y);
            var result = new AdvRetainedTextRunResult(run.Layout.Slot, 0, drawn.Length, endX, endY)
            {
                Rows = [new AdvTextRow(0, drawn.Length, x, y, x + 10 * drawn.Length, y + 20, false)],
            };
            return run.Presented == null && run.SourceCursor == null ? result : result with { Source = SourceOf(run) };
        },
        OnHiddenText = SourceOf,
    };

    private static AdvTextSourceLayout SourceOf(AdvLiveTextRun run)
    {
        (int x, int y) = run.SourceCursor ?? (run.Layout.CursorX, run.Layout.CursorY);
        int right = x + 20 * run.Text.Length;
        return new AdvTextSourceLayout([new AdvTextRow(0, run.Text.Length, x, y, right, y + 20, false)], [], right, y);
    }

    private static (RecordingHost Host, VirtualMachine Vm, Ome.Engine.Model.Script Script) Run(
        TextPresenter? presenter, IReadOnlyList<(int, Operand[])> code, IReadOnlyList<string> strings)
    {
        Ome.Engine.Model.Script script = ScriptAssembler.Assemble(Table, "SC0100", code, strings);
        var host = GridHost();
        var vm = new VirtualMachine(script, Table, host) { Presenter = presenter };
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);
        return (host, vm, script);
    }

    // ---- the source layout pass and the two cursors -----------------------------------------------------------------

    [Fact]
    public void HistoryRecordsTheSourceRowsWhileRunsDrawFromThePresentedCursor()
    {
        var (host, vm, _) = Run(English(), [
            (0x70, [I(1), I(640), I(160), I(0), I(400)]),
            (0x6e, [I(0), S(0)]),
            (0x6e, [I(0), S(1)]),
            (0x2, []),
        ], ["黒石", "未訳"]);

        // The miss draws after the presented text, and its source layout follows the source text.
        var second = host.LiveTextRuns[1].Run;
        Assert.Equal((50, 30), (second.Layout.CursorX, second.Layout.CursorY));
        Assert.Equal((40, 0), second.SourceCursor);
        Assert.Equal(new[] { new AdvTextHistoryRect(0, 0, 40, 20), new AdvTextHistoryRect(40, 0, 80, 20) },
                     vm.TextHistory.Records.Select(record => record.Rect));
        Assert.Equal((80, 0), (vm.TextHistory.GetLayoutSnapshot(1).CursorX, vm.TextHistory.GetLayoutSnapshot(1).CursorY));
        Assert.Equal((70, 30), (vm.TextHistory.GetDrawSnapshot(1).CursorX, vm.TextHistory.GetDrawSnapshot(1).CursorY));
    }

    [Fact]
    public void ALineBreakMovesBothCursorsAndTheIndicatorFollowsThePresentedOne()
    {
        var (host, vm, _) = Run(English(), [
            (0x70, [I(1), I(640), I(160), I(0), I(400)]),
            (0x6e, [I(0), S(0)]),
            (0x6f, [I(0)]),
            (0x2, []),
        ], ["黒石"]);

        // Default style: a 24-pixel font and 6 pixels of spacing. The presented run ended a row lower.
        Assert.Equal((1, 0, 60), host.TextCursors[^1]);
        Assert.Equal(30, vm.TextHistory.GetLayoutSnapshot(1).CursorY);
    }

    [Fact]
    public void ThePresentedMessagesLaterRunsRecordSourceRowsFromTheSourceCursor()
    {
        var (host, vm, _) = Run(English(), [
            (0x70, [I(1), I(640), I(160), I(0), I(400)]),
            (0x6e, [I(0), S(0)]),
            (0x196, [I(0), S(1), S(2)]),
            (0x6f, [I(0)]),
            (0x6e, [I(0), S(3)]),
            (0x2, []),
        ], ["あの", "黒石", "くろいし", "を拾った。"]);

        Assert.Equal("I picked up that black stone.", Assert.Single(host.LiveTextRuns).Run.Presented?.Text);
        Assert.Equal(new[] { "くろいし", null }, host.HiddenTextRuns.Select(run => run.SourceRuby));
        // あの (40 px), 黒石 after it; the break inside the message moves only the source cursor; を拾った。 from there.
        Assert.Equal(new[]
        {
            new AdvTextHistoryRect(0, 0, 40, 20), new AdvTextHistoryRect(40, 0, 80, 20), default,
            new AdvTextHistoryRect(0, 30, 100, 50),
        }, vm.TextHistory.Records.Select(record => record.Rect));
        Assert.Equal((100, 30), (vm.TextHistory.GetLayoutSnapshot(1).CursorX, vm.TextHistory.GetLayoutSnapshot(1).CursorY));
        Assert.Equal((50, 30), (vm.TextHistory.GetDrawSnapshot(1).CursorX, vm.TextHistory.GetDrawSnapshot(1).CursorY));
    }

    // ---- history presented again (gate 7) ---------------------------------------------------------------------------

    /// <summary>The page: a furigana sentence, a line break, a presented run, a break, a miss; then layout 2 for the
    /// backlog. The second part, from <c>render</c>, draws record 0's group into layout 2 and redraws it in place.</summary>
    private static readonly List<(int, Operand[])> Page =
    [
        (0x70, [I(1), I(640), I(160), I(0), I(400)]),
        (0x6e, [I(0), S(0)]),
        (0x196, [I(0), S(1), S(2)]),
        (0x6e, [I(0), S(3)]),
        (0x6f, [I(0)]),
        (0x6e, [I(1), S(1)]),                        // its own message: another layout operand
        (0x6f, [I(0)]),
        (0x6e, [I(0), S(4)]),
        (0x70, [I(2), I(640), I(400), I(0), I(0)]),
        (0x2, []),
        (0x1d1, [I(2), I(0), I(0), I(0), I(0)]),     // render (instruction 10)
        (0x82, [I(1), I(0), I(0), I(0), I(0)]),
        (0x2, []),
    ];

    private static readonly string[] PageStrings = ["あの", "黒石", "くろいし", "を拾った。", "未訳"];

    private const string PresentedPage = "I picked up that black stone.\nBlack Stone\n未訳";

    private static void Render(VirtualMachine vm, Ome.Engine.Model.Script script)
    {
        vm.Run(script.Instructions[10].Offset);
        Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void TheBacklogShowsThePresentedTextOfThisSession()
    {
        var (host, vm, script) = Run(English(), Page, PageStrings);

        Render(vm, script);

        Assert.Equal(PresentedPage, Assert.Single(host.HistoryRenders).Presented?.Text);
        Assert.Equal(PresentedPage, Assert.Single(host.Redraws).Batch.Presented?.Text);
    }

    [Fact]
    public void AfterALoadTheBacklogMatchesTheRecordsTextPreferringGlobalEntries()
    {
        // A site entry for 黒石 elsewhere loses to the global one: a loaded record keeps no site.
        var (host, vm, script) = Run(English(TextEntry.Literal("黒石", "a stone of SC0200") with { Script = "SC0200", At = 4 }),
                                     Page, PageStrings);
        vm.TextHistory.RestorePersistenceSnapshot(vm.TextHistory.Entries.ToArray(), vm.TextHistory.Records.ToArray());

        Render(vm, script);

        Assert.Equal(PresentedPage, Assert.Single(host.HistoryRenders).Presented?.Text);
    }

    [Fact]
    public void TheBacklogChangesWithTheLanguage()
    {
        var (host, vm, script) = Run(English(), Page, PageStrings);

        vm.Presenter = Presenter("fr", TextEntry.Literal("黒石", "la pierre noire"),
                                 TextEntry.Literal("未訳", "non traduit"));
        Render(vm, script);
        vm.Presenter = null;
        Render(vm, script);

        // French has no sentence: the records match by their text, and its word is found inside the sentence.
        Assert.Equal(new[] { "あのla pierre noireを拾った。\nla pierre noire\nnon traduit", null },
                     host.HistoryRenders.Select(batch => batch.Presented?.Text));
    }

    [Fact]
    public void AMissInTheSameLanguageShowsItsSourceAndAGroupWithNothingPresentedDrawsAsRecorded()
    {
        var (host, vm, script) = Run(Presenter("en", TextEntry.Literal("別", "other")), Page, PageStrings);

        Render(vm, script);

        Assert.Null(Assert.Single(host.HistoryRenders).Presented);
    }

    [Fact]
    public void TheHeadlessHostSavesTheSameHistoryWithALanguage()
    {
        // Gate 2 (spec §8): the headless host draws nothing, so a language changes none of its saved history.
        byte[] Saved(TextPresenter? presenter)
        {
            Ome.Engine.Model.Script script = ScriptAssembler.Assemble(Table, "SC0100", Page, PageStrings);
            var vm = new VirtualMachine(script, Table, new RecordingHost()) { Presenter = presenter };
            vm.Run();
            Assert.Equal("exit", vm.HaltReason);
            return Ome.Engine.Persistence.NativeTextHistoryCodec.Encode(vm.TextHistory);
        }

        Assert.Equal(Saved(null), Saved(English()));
    }

    [Fact]
    public void ThePseudoLanguagePresentsTheBacklogAfterALoad()
    {
        var (host, vm, script) = Run(new TextPresenter(TextLanguage.Pseudo, [new PseudoLanguageFilter()]),
                                     Page, PageStrings);
        vm.TextHistory.RestorePersistenceSnapshot(vm.TextHistory.Entries.ToArray(), vm.TextHistory.Records.ToArray());

        Render(vm, script);

        // Each record is bracketed by the filter; the two breaks stay.
        string presented = Assert.Single(host.HistoryRenders).Presented!.Text;
        Assert.Equal(3, presented.Split('\n').Length);
        Assert.NotEqual("あの黒石を拾った。\n黒石\n未訳", presented);
    }
}

/// <summary>Spec §8: splitting a loaded group's records into spans that entries match.</summary>
public class TextHistorySplitTests
{
    private static PresentedText? Lookup(string text) => text switch
    {
        "あの黒石を拾った。" => new PresentedText("I picked up that black stone.", LineBreakMode.Words,
                                             TextEntry.Literal(text, "I picked up that black stone.")),
        "黒石" => new PresentedText("Black Stone", LineBreakMode.Words, TextEntry.Literal(text, "Black Stone")),
        "を拾った。" => new PresentedText("(filtered)", LineBreakMode.Words, null),   // a filter's text is no match
        _ => null,
    };

    [Fact]
    public void TheSplitLeavesTheLeastTextUnmatchedWithTheFewestSpans()
    {
        var spans = TextHistorySplit.Split(["未訳", "あの", "黒石", "を拾った。", "黒石"], Lookup);

        Assert.Equal(new (int, int, string?)[]
        {
            (0, 1, null), (1, 3, "I picked up that black stone."), (4, 1, "Black Stone"),
        }, spans.Select(span => (span.First, span.Count, span.Presented?.Text)));
    }

    [Fact]
    public void AnUnmatchedRecordStandsAlone()
    {
        var spans = TextHistorySplit.Split(["を拾った。", "未訳"], Lookup);

        Assert.Equal(new (int, int, string?)[] { (0, 1, null), (1, 1, null) },
                     spans.Select(span => (span.First, span.Count, span.Presented?.Text)));
    }

    [Fact]
    public void AnUnscopedLookupPrefersTheGlobalEntry()
    {
        var catalog = TextCatalog.Build("en",
        [
            TextEntry.Literal("黒石", "global"),
            TextEntry.Literal("黒石", "script") with { Script = "SC0100" },
            TextEntry.Literal("白石", "site") with { Script = "SC0100", At = 4 },
            TextEntry.Literal("白石", "script") with { Script = "SC0200" },
        ]);

        Assert.True(catalog.TryGetUnscoped("黒石", out TextEntry black));
        Assert.True(catalog.TryGetUnscoped("白石", out TextEntry white));
        Assert.Equal(("global", "script"), (black.TextTemplate.Literal, white.TextTemplate.Literal));
    }
}

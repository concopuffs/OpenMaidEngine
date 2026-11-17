using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
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

    private static (RecordingWaits Host, VirtualMachine Vm) Run(TextPresenter? presenter,
        IReadOnlyList<(int, Operand[])> code, IReadOnlyList<string> strings, bool records = true)
    {
        Ome.Engine.Model.Script script = ScriptAssembler.Assemble(Table, "SC0100", code, strings);
        var host = new RecordingWaits();
        var vm = new VirtualMachine(script, Table, host) { Text = { Presenter = presenter }, RecordsStringStores = records };
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

    /// <summary>The sentence drawn by a text native into a window 100 pixels wide and <paramref name="height"/> tall,
    /// with source glyphs 20 pixels wide and presented ones 10: the translation needs four rows (I picked | up that |
    /// black | stone.), the source two. Returns the misses and the message's site.</summary>
    private static (TextMissLog Misses, int At) DrawSentence(int height, TextOverflow overflow)
    {
        var misses = new TextMissLog();
        Ome.Engine.Model.Script script = ScriptAssembler.Assemble(Table, "SC0100",
            [(0x70, [I(1), I(100), I(height), I(0), I(400)]), (0x213, [I(1), I(100), I(64)]), .. SentenceCode()],
            Sentence);
        var vm = TestText.Vm(script, Table, new RecordingWaits(), TestText.Glyphs(TestText.Fixed(20, 10), TestText.Unicode(10)));
        var language = new TextLanguage("en",
            [TextCatalog.Build("en", [TextEntry.Literal("あの黒石を拾った。", "I picked up that black stone.")])])
        {
            Overflow = overflow,
        };
        vm.Text.Presenter = new TextPresenter(language, misses: misses, messages: new ScriptMessageIndex(Table, Contracts));
        vm.Text.PresentsText = true;
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);
        return (misses, script.Instructions[2].Offset);
    }

    [Fact]
    public void AMessageCutToItsWindowIsReportedOnceWithItsRows()
    {
        // Two of the four rows fit 60 pixels (a row needs its top plus the 24-pixel font): a language that cuts
        // overflow draws two and reports two (spec §7.3).
        var (misses, at) = DrawSentence(height: 60, TextOverflow.Cut);

        var written = new StringWriter();
        misses.WriteJsonLines(written);
        Assert.Equal($"{{\"source\":\"あの黒石を拾った。\",\"script\":\"SC0100\",\"at\":\"0x{at:x}\",\"count\":1,\"rows_cut\":2}}",
                     written.ToString().Trim());
        Assert.Equal((0, 1), (misses.Count, misses.CutCount));
    }

    [Fact]
    public void AMessageNarrowedToFitItsWindowIsReportedWithItsWidth()
    {
        // Three rows fit 90 pixels: the language's default narrows the glyphs until the sentence needs three
        // (I picked up | that black | stone.), so nothing is cut (spec §7.3). At 94 % a glyph advances 9 pixels (9.4
        // rounded), so I picked up takes 99; at 95 % it advances 10 again.
        var (misses, at) = DrawSentence(height: 90, TextOverflow.Shrink);

        var written = new StringWriter();
        misses.WriteJsonLines(written);
        Assert.Equal($"{{\"source\":\"あの黒石を拾った。\",\"script\":\"SC0100\",\"at\":\"0x{at:x}\",\"count\":1,\"width_percent\":94}}",
                     written.ToString().Trim());
        Assert.Equal((0, 1, 0), (misses.Count, misses.NarrowedCount, misses.CutCount));
    }

    // ---- one layout (spec §7.4) ------------------------------------------------------------------------------------

    /// <summary>The left of each glyph object a SYS4433 page in aspect mode 1 draws: <paramref name="text"/>, presented
    /// as <paramref name="presented"/> when it is given, with glyphs 13 pixels wide for a single-byte CP932 code and 30
    /// for a double-byte one.</summary>
    private static double[] AspectLefts(string text, string? presented)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Ome.Engine.Model.Script script = ScriptAssembler.Assemble(table, "SC0100",
            [(0x70, [I(1), I(400), I(160), I(0), I(400)]), (0x213, [I(1), I(100), I(64)]), (0x2db, [I(1)]),
             (0x6e, [I(0), S(0)]), (0x2, [])], [text]);
        var vm = TestText.Vm(script, table, new RecordingWaits(),
            TestText.Glyphs(TestText.Fixed(30, 13), TestText.Unicode(30, 13)), EngineBuilds.Age433G);
        if (presented != null)
        {
            vm.Text.Presenter = new TextPresenter(
                new TextLanguage("en", [TextCatalog.Build("en", [TextEntry.Literal(text, presented)])]),
                messages: new ScriptMessageIndex(table, Contracts));
            vm.Text.PresentsText = true;
        }
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);
        return [.. vm.Gfx.SnapshotVisibleObjects().Where(glyph => glyph.Handle >= 100).Select(glyph => glyph.DstX)];
    }

    [Fact]
    public void InAspectModePresentedTextIsLaidOutAsTheSameTextInTheSourceWouldBe()
    {
        // The reference 激 is 30 wide, so a single-byte character takes a 15-pixel cell and a double-byte one 30,
        // whatever its glyph's own advance; Ⅰ is double-byte in CP932. The same text drawn as source and as a
        // translation lands on the same cells.
        double[] source = AspectLefts("Wil Ⅰ", presented: null);
        Assert.Equal([0.0, 15.0, 30.0, 45.0, 60.0], source.Select(left => left - source[0]));
        Assert.Equal(source, AspectLefts("ウィル", presented: "Wil Ⅰ"));
    }

    /// <summary>A presented rasterizer that measures: glyphs 13 pixels wide for a single-byte CP932 code and 30
    /// otherwise, every request recorded, and metrics whose average width is the width asked for (or half the height)
    /// over a font twice as tall as its size, so the single-byte calibration narrows.</summary>
    private sealed class MeasuringGlyphs : IIdentifiedGlyphMaskRasterizer, Ome.Engine.Text.IFontMetricsProbe
    {
        private readonly TestText.GridGlyphs _glyphs = TestText.Unicode(30, 13);
        public readonly List<GlyphRasterRequest> Requests = [];
        public GlyphRasterizerBackendInfo BackendInfo => _glyphs.BackendInfo;

        public GlyphMask Rasterize(GlyphRasterRequest request)
        {
            Requests.Add(request);
            return _glyphs.Rasterize(request);
        }

        public Ome.Engine.Text.FontProbeMetrics Measure(string faceName, int pixelHeight, int width, int weight,
                                                        ushort code)
            => new(width != 0 ? System.Math.Abs(width) : pixelHeight / 2, BlackBoxX: 8, CellIncrementX: 16,
                   Height: 2 * pixelHeight);
    }

    [Fact]
    public void PresentedTextIsCalibratedWithItsOwnRasterizersMetrics()
    {
        // The installed list says the face is proportional, so aspect mode calibrates it (spec §7.4): the source
        // rasterizer does not measure, the presented one does, and presented glyphs ask for the width its metrics give.
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Ome.Engine.Model.Script script = ScriptAssembler.Assemble(table, "SC0100",
            [(0x70, [I(1), I(400), I(160), I(0), I(400)]), (0x213, [I(1), I(100), I(64)]), (0x2db, [I(1)]),
             (0x6e, [I(0), S(0)]), (0x2, [])], ["ウィル"]);
        var presented = new MeasuringGlyphs();
        EnumeratedFontFamily[] families = [new(ImmediateSurfaceTextRenderer.DefaultFontFace, 0)];
        var vm = TestText.Vm(script, table, new RecordingWaits(),
            new Ome.Engine.Natives.TextDevices(TestText.Fixed(30, 13), GlyphCoverageAlpha.Sixteenths, Presented: () => presented,
                            FontFamilies: () => families),
            EngineBuilds.Age433G);
        vm.Text.Presenter = new TextPresenter(
            new TextLanguage("en", [TextCatalog.Build("en", [TextEntry.Literal("ウィル", "Wil")])]),
            messages: new ScriptMessageIndex(table, Contracts));
        vm.Text.PresentsText = true;

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        GlyphRasterRequest w = presented.Requests.First(request => request.UnicodeScalar == 'W');
        int calibrated = new Ome.Engine.Text.AspectFontRealization(families, presented)
            .SingleByteWidth(w.FontFace, w.PixelHeight, w.Weight, 'W', aspectMode: 1);
        Assert.NotEqual(-(w.PixelHeight / 2), calibrated);
        Assert.Equal(calibrated, w.RequestedWidth);
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
        // halve-strlen truncates, as native's does: Kamidori sizes the "Wil" plate for one cell.
        Assert.Equal(new[] { 1, 3, 3 }, Lengths(Presenter(null, TextEntry.Literal("アリア", "Wil"))));
        // CP932's double-byte symbols count two half cells, as native counts their bytes.
        Assert.Equal(new[] { 4, 8, 7 }, Lengths(Presenter(null, TextEntry.Literal("アリア", "Cover Ⅰ"))));
    }

    [Fact]
    public void SourceMeasurementOptsOut()
    {
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

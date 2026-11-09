using Ome.Engine.Model;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Ome.Engine.Vm;

/// <summary>Op 0x196 furigana (docs/engine-re.md, "Furigana"), with synthesized glyph metrics.</summary>
public class RubyTextLayoutTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(0, value);
    private static Operand S(int index) => new(2, index);

    /// <summary>Full-width cells as wide as the pixel height, half-width cells half that.</summary>
    private sealed class SquareRasterizer : IGlyphMaskRasterizer
    {
        public List<GlyphRasterRequest> Requests { get; } = new();

        public GlyphMask Rasterize(GlyphRasterRequest request)
        {
            Requests.Add(request);
            int width = request.Cp932Code <= 0xff ? request.PixelHeight / 2 : request.PixelHeight;
            return new GlyphMask(
                width: 1, height: 1, stride: 1,
                originX: 0, originY: 1,
                cellAdvanceX: width, cellAdvanceY: 0,
                cellWidth: width, cellHeight: request.PixelHeight,
                coverage: [16]);
        }
    }

    private static RgbaImage Image(int width, int height) => new(width, height, new byte[width * height * 4]);

    private static AdvTextStyle Style(int renderMode = 3) => AdvTextStyle.Default with
    {
        PrimaryFontSize = 24,
        RubyFontSize = 8,
        TextColor = 0xffffff,
        EffectColor = 0x606060,
        RenderMode = renderMode,
        EffectOffsetX = 1,
        EffectOffsetY = 1,
    };

    [Fact]
    public void ARubyNarrowerThanItsBaseIsSpreadWithEqualGapsAboveIt()
    {
        var rasterizer = new SquareRasterizer();

        // 篝火 is 48 pixels and かがりび 32: (48 - 32) / (4 + 1) = 3 pixels before, between and after.
        IReadOnlyList<AdvRetainedGlyphRecord> records = RubyTextLayout.Draw(
            Image(200, 80), rasterizer, 10, 40, 48, "かがりび", Style(), GlyphRasterPolicy.NativeCp932Gray4, true,
            RubyLayoutRules.Sys4422);

        // Mode 3: top + lfHeight (-8) + 1 - effect y (1).
        Assert.Equal(new AdvRetainedGlyphRecord[]
        {
            new(0, 13, 32, 21, 40),
            new(1, 24, 32, 32, 40),
            new(1, 35, 32, 43, 40),
            new(1, 46, 32, 54, 40),
        }, records);
        Assert.All(rasterizer.Requests, request =>
        {
            Assert.Equal(RubyTextLayout.FontFace, request.FontFace);
            Assert.Equal(8, request.PixelHeight);
            Assert.Equal(-4, request.RequestedWidth);
            Assert.Equal(0, request.Weight);
        });
    }

    [Fact]
    public void ARubyWiderThanItsBaseIsCentredAndOverhangsBothSides()
    {
        // A 21-pixel base under a 32-pixel ruby: -(-11 / 2) = 5 pixels of overhang on the left, and no gap.
        IReadOnlyList<AdvRetainedGlyphRecord> records = RubyTextLayout.Draw(
            Image(200, 80), new SquareRasterizer(), 100, 40, 21, "いちにさ", Style(renderMode: 1),
            GlyphRasterPolicy.NativeCp932Gray4, true, RubyLayoutRules.Sys4422);

        // Not mode 3: top + lfHeight + 1.
        Assert.Equal(new[] { 95, 103, 111, 119 }, records.Select(record => record.Left));
        Assert.All(records, record => Assert.Equal(33, record.Top));
    }

    [Fact]
    public void ARubyIsDrawnPlainInTheTextColor()
    {
        RgbaImage destination = Image(80, 60);

        RubyTextLayout.Draw(destination, new SquareRasterizer(), 10, 40, 48, "か", Style(),
            GlyphRasterPolicy.NativeCp932Gray4, true, RubyLayoutRules.Sys4422);

        // One mask pixel at the ruby baseline (cell top 32 + ascent 8 - origin 1) with no outline around it.
        int Alpha(int x, int y) => destination.Pixels[(y * 80 + x) * 4 + 3];
        Assert.NotEqual(0, Alpha(30, 39));
        Assert.Equal(0, Alpha(31, 40));
        Assert.Equal(0, Alpha(29, 38));
    }

    [Fact]
    public void Sys4433DividesTheGapByHalfTheBytesPlusThreeAndHalvesItAfterASingleByteCharacter()
    {
        // ｱか is 3 bytes and 12 pixels over a 48-pixel base: (48 - 12) / ((3 + 3) >> 1) = 12 (SYS4422 divides by
        // 3 / 2 + 1 = 2). ｱ is followed by 6 pixels, half the gap.
        IReadOnlyList<AdvRetainedGlyphRecord> records = RubyTextLayout.Draw(
            Image(200, 80), new SquareRasterizer(), 10, 40, 48, "ｱか", Style(),
            GlyphRasterPolicy.NativeCp932Gray4, true, RubyLayoutRules.Sys4433(rubyShiftMode: 1, drawMode: 1));

        // Kamidori (RubyShiftMode 1): top + lfHeight, and mode 3 raises nothing. Every record chains.
        Assert.Equal(new AdvRetainedGlyphRecord[] { new(1, 22, 32, 26, 40), new(1, 32, 32, 40, 40) }, records);
    }

    [Fact]
    public void Sys4433DrawsAControlCharacterAsASpace()
    {
        var rasterizer = new SquareRasterizer();

        RubyTextLayout.Draw(Image(200, 80), rasterizer, 10, 40, 48, "か	か", Style(),
            GlyphRasterPolicy.NativeCp932Gray4, true, RubyLayoutRules.Sys4433(rubyShiftMode: 1, drawMode: 1));

        Assert.Equal(new[] { 'か', ' ', 'か' }, rasterizer.Requests.Select(request => (char)request.UnicodeScalar));
    }

    [Theory]
    // SYS4422: one pixel below top + lfHeight, raised by the mode-3 effect offset, live and in History.
    [InlineData("SYS4422", 0, 1, false, 32)]
    [InlineData("SYS4422", 0, 1, true, 32)]
    // SYS4433: the pixel only off DrawMode 1 with RubyShiftMode 0, never in History; the raise only with
    // RubyShiftMode 0.
    [InlineData("SYS4433", 0, 0, false, 32)]
    [InlineData("SYS4433", 0, 1, false, 31)]
    [InlineData("SYS4433", 0, 0, true, 31)]
    [InlineData("SYS4433", 1, 0, false, 32)]
    [InlineData("SYS4433", 1, 1, true, 32)]
    public void RubyTopFollowsTheRevisionsShiftRules(
        string engineAbiId, int rubyShiftMode, int drawMode, bool history, int top)
    {
        Sys4StartupSettings settings = Sys4StartupSettings.FromPairs(
        [
            new("RUBYSHIFTMODE", rubyShiftMode.ToString()),
            new("DRAWMODE", drawMode.ToString()),
        ]);

        IReadOnlyList<AdvRetainedGlyphRecord> records = RubyTextLayout.Draw(
            Image(200, 80), new SquareRasterizer(), 10, 40, 48, "か", Style(renderMode: 3),
            GlyphRasterPolicy.NativeCp932Gray4, true, RubyLayoutRules.For(engineAbiId, settings), history);

        Assert.Equal(top, Assert.Single(records).Top);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(32, 31)]
    [InlineData(33, 31)]
    public void RubyPixelHeightFollowsTextSetRubyFontSize(int size, int pixelHeight)
        => Assert.Equal(pixelHeight, RubyTextLayout.PixelHeight(size));

    [Fact]
    public void AWrappedBaseSharesItsRubyByBytesPerRow()
    {
        // 一二三 | 四: the first row has 6 of 8 base bytes, so it takes ((12 * 6) / 8 / 2) * 2 = 8 ruby bytes.
        IReadOnlyList<string> shares = RubyTextLayout.SplitAcrossRows(
            "一二三四",
            [new AdvTextRow(0, 3, 0, 0, 72, 24, true), new AdvTextRow(3, 1, 0, 30, 24, 54, false)],
            "いちにさんし");

        Assert.Equal(new[] { "いちにさ", "んし" }, shares);
    }

    [Fact]
    public void ChainFlagsFollowARubyAcrossRunsAndGroupItsPublication()
    {
        var presentation = new RetainedAdvTextLayoutPresentation(
            new AdvTextLayoutPresentationBinding(1, 21, 55000, 500, -1, 0, 0));
        AdvRetainedGlyphRecord Cell(int left) => new(0, left, 0, left + 24, 24);

        // 篝火 + かがりび, then の如く.
        int furigana = presentation.AppendRun(
            [Cell(0), Cell(24), Cell(3), Cell(14), Cell(25), Cell(36)],
            [false, false, true, true, true, true], 0, 430);
        int following = presentation.AppendRun([Cell(48), Cell(72), Cell(96)], [false, false, false], 0, 430);

        Assert.Equal(new[] { 0, 0, 0, 1, 1, 1, 1, 0, 0 },
            presentation.Glyphs.Select(glyph => glyph.Record.PublicationChainFlag));
        // 篝 | 火 | か | がりび, as native's 0.4 s at 100 ms; then の如 | く.
        Assert.Equal(4, presentation.PublicationTicks(furigana, 6));
        Assert.Equal(new[] { 1, 2, 3, 6 },
            Enumerable.Range(1, 4).Select(ticks => presentation.PublicationTarget(furigana, 6, ticks)));
        Assert.Equal(2, presentation.PublicationTicks(following, 3));
        Assert.Equal(8, presentation.PublicationTarget(following, 3, 1));
    }

    [Fact]
    public void Sys4433ChainsEveryRubyRecordSoTheWholeRubyPublishesInOneTick()
    {
        var presentation = new RetainedAdvTextLayoutPresentation(
            new AdvTextLayoutPresentationBinding(1, 21, 55000, 500, -1, 0, 0));
        AdvRetainedGlyphRecord Cell(int left) => new(0, left, 0, left + 24, 24);

        int furigana = presentation.AppendRun(
            [Cell(0), Cell(24), Cell(3), Cell(14), Cell(25), Cell(36)],
            [false, false, true, true, true, true], 0, 430, chainEveryRubyRecord: true);
        int following = presentation.AppendRun(
            [Cell(48), Cell(72), Cell(96)], [false, false, false], 0, 430, chainEveryRubyRecord: true);

        Assert.Equal(new[] { 0, 0, 1, 1, 1, 1, 0, 0, 0 },
            presentation.Glyphs.Select(glyph => glyph.Record.PublicationChainFlag));
        // 篝 | 火 | かがりび; then の | 如 | く.
        Assert.Equal(new[] { 1, 2, 6 },
            Enumerable.Range(1, 3).Select(ticks => presentation.PublicationTarget(furigana, 6, ticks)));
        Assert.Equal(3, presentation.PublicationTicks(following, 3));
    }

    [Fact]
    public void Sys4433OpcodeFuriganaArmsTheGlyphTimer()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var script = ScriptAssembler.Assemble(table, "KAMIDORI-FURIGANA",
            new List<(int, Operand[])>
            {
                (0x70, new[] { I(1), I(640), I(160), I(0), I(430) }),
                (0x196, new[] { I(0), S(0), S(1) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "叛乱", "はんらん" });
        var runs = new List<AdvLiveTextRun>();
        var host = new RecordingHost { OnRetainedText = run => { runs.Add(run); return null; } };

        new VirtualMachine(script, table, host, compatibility: new("kamidori", "SYS4433")).Run();

        Assert.True(Assert.Single(runs).ArmsGlyphTimer);
    }

    [Fact]
    public void OpcodeFuriganaShowsItsBaseWithTheRubyAndRecordsEachRubyCharacter()
    {
        var script = ScriptAssembler.Assemble(Table, "FURIGANA",
            new List<(int, Operand[])>
            {
                (0x70, new[] { I(1), I(640), I(160), I(0), I(430) }),
                (0x75, new[] { I(24) }),
                (0x6e, new[] { I(0), S(0) }),
                (0x196, new[] { I(0), S(1), S(2) }),
                (0x2, Array.Empty<Operand>()),
            }, new[] { "今にも", "篝火", "かがりび" });
        var runs = new List<AdvLiveTextRun>();
        var host = new RecordingHost
        {
            OnRetainedText = run =>
            {
                runs.Add(run);
                if (run.Ruby == null)
                    return new AdvRetainedTextRunResult(run.Layout.Slot, 0, 3, 72, 0)
                    {
                        Rows = [new AdvTextRow(0, 3, 0, 0, 72, 24, false)],
                    };
                return new AdvRetainedTextRunResult(run.Layout.Slot, 3, 6, 120, 0)
                {
                    Rows = [new AdvTextRow(0, 2, 72, 0, 120, 24, false)],
                    Ruby =
                    [
                        new AdvRubyGlyph(0, "か", new(0, 75, -8, 83, 0)),
                        new AdvRubyGlyph(0, "が", new(1, 86, -8, 94, 0)),
                    ],
                };
            },
        };
        var vm = new VirtualMachine(script, Table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new string?[] { null, "かがりび" }, runs.Select(run => run.Ruby));
        // Op 0x6e arms the glyph timer; op 0x196 polls the one it left.
        Assert.Equal(new[] { true, false }, runs.Select(run => run.ArmsGlyphTimer));
        Assert.Equal(new[] { "今にも", "篝火" }, vm.Emitted.Select(line => line.Text));
        Assert.Equal(
            new (string, AdvTextHistoryRecordFlags, AdvTextHistoryRect, int, int)[]
            {
                ("今にも", AdvTextHistoryRecordFlags.GroupStart, new(0, 0, 72, 24), 24, 0),
                ("篝火", AdvTextHistoryRecordFlags.None, new(72, 0, 120, 24), 24, 0),
                ("か", AdvTextHistoryRecordFlags.Ruby, new(75, -8, 83, 0), 8, 0),
                ("が", AdvTextHistoryRecordFlags.Ruby, new(86, -8, 94, 0), 8, 0),
            },
            vm.TextHistory.Records.Select(record =>
                (record.Text, record.Flags, record.Rect, record.Style.PrimaryFontSize, record.Style.RenderMode)));

        // History draws the ruby over the record before it; op 0x82 draws each ruby record at its own rectangle.
        Assert.True(vm.TextHistory.TryBuildHistoryRenderBatch(1, 0, 0, 0, 0, Style(), out AdvTextHistoryRenderBatch batch));
        Assert.Equal("今にも篝火", batch.Text);
        Assert.Equal(new[] { new AdvTextRubyAnnotation(3, 2, "かが") }, batch.Ruby);
        Assert.True(vm.TextHistory.TryBuildRedrawBatch(1, 0, 0, 0, 0, Style(), out AdvTextRedrawBatch redraw));
        Assert.Equal(
            new[] { ("今にも", false), ("篝火", false), ("か", true), ("が", true) },
            redraw.Records.Select(record => (record.Text, record.IsRuby)));
        Assert.Equal(new AdvTextHistoryRect(86, -8, 94, 0), redraw.Records[3].Rect);

        // A numbered save keeps the ruby records: flags 4, the ruby pixel height and render mode 0.
        var restored = new AdvTextHistory();
        NativeTextHistoryCodec.DecodeInto(NativeTextHistoryCodec.Encode(vm.TextHistory), restored);
        AdvTextHistoryRecord ruby = restored.Records[2];
        Assert.Equal(("か", AdvTextHistoryRecordFlags.Ruby, 8, 0),
            (ruby.Text, ruby.Flags, ruby.Style.PrimaryFontSize, ruby.Style.RenderMode));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARedrawDrawsThePageAgainAtItsRecords(bool sys4433)
    {
        // text_redraw_retained_group@0x451530 draws each record at its rectangle, ruby included, so a redraw in the
        // page's own style gives back the live page's pixels.
        RubyLayoutRules rules = sys4433 ? RubyLayoutRules.Sys4433(1, 1) : RubyLayoutRules.Sys4422;
        var engine = new RetainedGlyphLayoutEngine(new SquareRasterizer());
        var history = new AdvTextHistory();
        history.DefineLayout(1, 200, 90, 0, 0);
        RgbaImage live = Image(200, 90);
        AdvTextStyle style = Style();
        int x = 10, y = 30;
        foreach ((string text, string? ruby) in new[] { ("今にも", null), ("篝火の灯り", "かがりびのあかり") })
        {
            GlyphTextLayoutResult drawn = engine.Render(
                live, new GlyphTextLayoutOptions(x, y, 10, 130, 90, WrapHorizontally: true, style),
                ImmediateSurfaceTextRenderer.CreateRequests(text, style, GlyphRasterPolicy.NativeCp932Gray4, true));
            IReadOnlyList<AdvRubyGlyph> glyphs = ruby == null ? []
                : engine.RenderRuby(live, text, drawn.Rows, ruby, style, GlyphRasterPolicy.NativeCp932Gray4, true, rules);
            history.AppendText(1, 0, text, style, rows: drawn.Rows, ruby: glyphs);
            (x, y) = (drawn.CursorX, drawn.CursorY);
        }

        Assert.True(history.TryBuildRedrawBatch(1, 0, 0, 0, 0, style, out AdvTextRedrawBatch batch));
        RgbaImage redrawn = Image(200, 90);
        IReadOnlyList<(AdvRetainedGlyphRecord Cell, bool IsRuby)> cells = engine.RenderRedraw(
            redrawn, batch.Records, GlyphRasterPolicy.NativeCp932Gray4, true, rules);

        Assert.Single(history.Records, record => record.Flags.HasFlag(AdvTextHistoryRecordFlags.LineBreak));
        Assert.Equal(8, cells.Count(cell => cell.IsRuby));
        Assert.Contains(live.Pixels, value => value != 0);
        Assert.Equal(live.Pixels, redrawn.Pixels);
    }
}

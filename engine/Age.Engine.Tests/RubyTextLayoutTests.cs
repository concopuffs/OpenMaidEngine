using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Text;
using Age.Engine.Vm;

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
            Image(200, 80), rasterizer, 10, 40, 48, "かがりび", Style(), GlyphRasterPolicy.NativeCp932Gray4, true);

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
            GlyphRasterPolicy.NativeCp932Gray4, true);

        // Not mode 3: top + lfHeight + 1.
        Assert.Equal(new[] { 95, 103, 111, 119 }, records.Select(record => record.Left));
        Assert.All(records, record => Assert.Equal(33, record.Top));
    }

    [Fact]
    public void ARubyIsDrawnPlainInTheTextColor()
    {
        RgbaImage destination = Image(80, 60);

        RubyTextLayout.Draw(destination, new SquareRasterizer(), 10, 40, 48, "か", Style(),
            GlyphRasterPolicy.NativeCp932Gray4, true);

        // One mask pixel at the ruby baseline (cell top 32 + ascent 8 - origin 1) with no outline around it.
        int Alpha(int x, int y) => destination.Pixels[(y * 80 + x) * 4 + 3];
        Assert.NotEqual(0, Alpha(30, 39));
        Assert.Equal(0, Alpha(31, 40));
        Assert.Equal(0, Alpha(29, 38));
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

        // History draws the ruby over the record before it; op 0x82's reflow leaves it out.
        Assert.True(vm.TextHistory.TryBuildHistoryRenderBatch(1, 0, 0, 0, 0, Style(), out AdvTextHistoryRenderBatch batch));
        Assert.Equal("今にも篝火", batch.Text);
        Assert.Equal(new[] { new AdvTextRubyAnnotation(3, 2, "かが") }, batch.Ruby);
        Assert.True(vm.TextHistory.TryBuildRenderBatch(1, 0, 0, 0, 0, out AdvTextHistoryRenderBatch redraw));
        Assert.Equal("今にも篝火", redraw.Text);

        // A numbered save keeps the ruby records: flags 4, the ruby pixel height and render mode 0.
        var restored = new AdvTextHistory();
        NativeTextHistoryCodec.DecodeInto(NativeTextHistoryCodec.Encode(vm.TextHistory), restored);
        AdvTextHistoryRecord ruby = restored.Records[2];
        Assert.Equal(("か", AdvTextHistoryRecordFlags.Ruby, 8, 0),
            (ruby.Text, ruby.Flags, ruby.Style.PrimaryFontSize, ruby.Style.RenderMode));
    }
}

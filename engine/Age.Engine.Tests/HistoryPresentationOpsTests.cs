using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class HistoryPresentationOpsTests
{
    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(T_IMM, value);
    private static Operand G(int address) => new(T_GINT, address);

    // A retained group as the ADV window recorded it: metadata, a name row, a line break, then a line that
    // wrapped once. Its records carry the ADV window's 24 px style; a second group follows.
    private static AdvTextHistory RecordedHistory()
    {
        var history = new AdvTextHistory();
        var adv = new AdvTextStyle(24, 8, false, 0xffffff, 0x606060, 3, 1, 1, 9, "ＭＳ 明朝");
        history.DefineLayout(1, 640, 160, 80, 430);
        history.AppendMetadata(9, 2, AdvTextStyle.Default);
        history.AppendText(0, 10, "魔王", adv, rows: [new AdvTextRow(0, 2, 0, 0, 48, 24, false)]);
        history.EndLine(0, adv);
        history.AppendText(0, 11, "「あいう」", adv, rows:
        [
            new AdvTextRow(0, 3, 0, 32, 72, 56, WrapsAfter: true),
            new AdvTextRow(3, 2, 24, 64, 72, 88, WrapsAfter: false),
        ]);
        history.ResetLayout(1);
        history.AppendText(0, 12, "next group", AdvTextStyle.Default);
        history.SetRecordingEnabled(false);
        history.DefineLayout(4, 600, 150, 0, 0);
        history.SetRecordingEnabled(true);
        return history;
    }

    private static RecordingHost RunHistoryRender(AdvTextHistory history, Operand[] renderOperands)
    {
        var script = ScriptAssembler.Assemble(Table, "HISTORY_RENDER",
            new List<(int, Operand[])>
            {
                (0x198, new[] { I(4), I(65), I(150) }),
                (0x7a, new[] { I(4), I(45), I(42) }),
                // HISTORY.BIN's style before it renders (0xf78..0xfa0).
                (0x75, new[] { I(0x16) }),
                (0x8b, new[] { I(8) }),
                (0x78, new[] { I(3) }),
                (0x76, new[] { I(0xffffff) }),
                (0x77, new[] { I(0x333e52) }),
                (0x1d1, renderOperands),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        new VirtualMachine(script, Table, host, textHistory: history).Run();
        return host;
    }

    [Fact]
    public void RenderHistoryDrawsOneGroupInTheCurrentStyleBreakingOnlyAtRecordedLineBreaks()
    {
        var host = RunHistoryRender(RecordedHistory(), new[] { I(4), I(0), I(0), I(0), I(0) });

        var render = Assert.Single(host.HistoryRenders);
        Assert.Equal((4, 0, "魔王「あいう」"), (render.LayoutSlot, render.FirstRecordIndex, render.Text));
        Assert.Equal(new AdvTextLayoutSnapshot(4, 600, 150, 65, 150, 45, 42, 600, 150), render.Layout);
        // Native text_history_render_records@0x4526c0 draws with the text manager's current font and colors,
        // not the 24 px / 0x606060 / Mincho the records retain.
        Assert.Equal((22, 0xffffffL, 0x333e52L, 3, 8, ""),
            (render.Style.PrimaryFontSize, render.Style.TextColor, render.Style.EffectColor,
             render.Style.RenderMode, render.Style.LineSpacing, render.Style.FontFace));
        // The explicit line break after the name and the ADV window's wrap after 「あい.
        Assert.Equal(new[] { 2, 5 }, render.RecordedLineBreaks);
        var retained = Assert.Single(host.RetainedHistoryRenders);
        Assert.Equal(4, retained.Binding.LayoutSlot);
        Assert.Equal(0x18, retained.Binding.SourceSurfaceSlot);
        Assert.Same(render, retained.Batch);
    }

    [Fact]
    public void RenderHistoryFlag2OverridesOnlyTheCurrentColors()
    {
        var host = RunHistoryRender(RecordedHistory(), new[] { I(4), I(0), I(2), I(0x112233), I(0x445566) });

        var render = Assert.Single(host.HistoryRenders);
        Assert.Equal((22, 0x112233L, 0x445566L, 3),
            (render.Style.PrimaryFontSize, render.Style.TextColor, render.Style.EffectColor,
             render.Style.RenderMode));
    }

    [Fact]
    public void HistorySupportingPresentationOpsReachTheHostWithNativeShaping()
    {
        var script = ScriptAssembler.Assemble(Table, "HISTORY_PRESENT_SUPPORT",
            new List<(int, Operand[])>
            {
                (0x131, new[] { G(0x100) }),
                (0x20b, new[] { I(0xc1), I(0), I(30), I(600), I(30), I(999), I(0x123456) }),
                (0x222, new[] { I(0), I(60000) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost { MessageWindowAlphaSetting = 6 };
        var vm = new VirtualMachine(script, Table, host);

        vm.Run();

        Assert.Equal(6, vm.Globals[0x100]);
        Assert.Equal(new SurfaceRectFill(0xc1, 0, 30, 600, 30, 255, 0x123456),
            Assert.Single(host.SurfaceFills));
        Assert.Equal((0L, 60000L), Assert.Single(host.PresentedRanges));
    }

    [Fact]
    public void MessageWindowAlphaSetterImmediatelyFeedsThePairedGetter()
    {
        var script = ScriptAssembler.Assemble(Table, "MESSAGE_WINDOW_ALPHA",
            new List<(int, Operand[])>
            {
                (0x141, new[] { I(7) }),
                (0x131, new[] { G(0x100) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, Table, host);

        vm.Run();

        Assert.Equal(7, host.MessageWindowAlphaSetting);
        Assert.Equal(7, vm.Globals[0x100]);
    }

    [Fact]
    public void ResetLayoutClearsItsPreviouslyBoundHostPresentation()
    {
        var script = ScriptAssembler.Assemble(Table, "HISTORY_CLEAR_RENDER",
            new List<(int, Operand[])>
            {
                (0x71, new[] { I(5) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, Table, host).Run();

        Assert.Equal(5, Assert.Single(host.ClearedTextLayouts));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void RealHistoryScriptBuildsVisibleRowsFromARealSc0000Page()
    {
        var scripts = Sys4ScriptProvider.Load(Table);
        var session = new GameSession();
        session.RunScene(scripts.RequireByName("SC0000.BIN"), Table, new RecordingHost(),
            new VmOptions(MaxSteps: 1_000_000, HaltAtWaitForInput: true), scripts);
        var host = new RecordingHost();

        var result = session.RunScene(scripts.RequireByName("HISTORY.BIN"), Table, host,
            new VmOptions(MaxSteps: 2_000_000, HaltAtWaitForInput: true), scripts);

        Assert.NotEqual("step-cap", result.Halt);
        Assert.Contains(host.HistoryRenders, render => render.Text.Length > 0);
        // HISTORY's own 22 px style, although SC0000 recorded its rows at 24 px.
        Assert.All(host.HistoryRenders.Where(render => render.Text.Length > 0),
            render => Assert.Equal((22, 8, 0x333e52L),
                (render.Style.PrimaryFontSize, render.Style.LineSpacing, render.Style.EffectColor)));
        Assert.Contains(host.PresentedRanges, range => range == (0L, 60000L));
        Assert.Contains(false, host.WaitIndicatorEnabledChanges);
    }
}

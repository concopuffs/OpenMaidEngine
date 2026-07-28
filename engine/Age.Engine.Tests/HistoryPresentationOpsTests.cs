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

    [Fact]
    public void RenderHistoryUsesTheSelectedTargetLayoutAndOneRetainedGroup()
    {
        var history = new AdvTextHistory();
        history.DefineLayout(1, 640, 160, 80, 430);
        history.AppendMetadata(9, 2, AdvTextStyle.Default);
        history.AppendText(0, 10, "retained dialogue", new AdvTextStyle(24, 8, false,
            0xffffff, 0x606060, 3, 1, 1, 9));
        history.ResetLayout(1);
        history.AppendText(0, 11, "next group", AdvTextStyle.Default);
        history.SetRecordingEnabled(false);
        history.DefineLayout(4, 600, 150, 0, 0);
        history.SetRecordingEnabled(true);

        var script = ScriptAssembler.Assemble(Table, "HISTORY_RENDER",
            new List<(int, Operand[])>
            {
                (0x198, new[] { I(4), I(65), I(150) }),
                (0x7a, new[] { I(4), I(45), I(42) }),
                (0x8b, new[] { I(8) }),
                (0x1d1, new[] { I(4), I(0), I(0), I(0), I(0) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, Table, host, textHistory: history);

        vm.Run();

        var render = Assert.Single(host.HistoryRenders);
        Assert.Equal((4, 0, "retained dialogue"),
            (render.LayoutSlot, render.FirstRecordIndex, render.Text));
        Assert.Equal(new AdvTextLayoutSnapshot(4, 600, 150, 65, 150, 45, 42, 600, 150), render.Layout);
        Assert.Equal((24, 0xffffffL, 0x606060L, 8),
            (render.Style.PrimaryFontSize, render.Style.TextColor, render.Style.EffectColor,
             render.Style.LineSpacing));
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
        Assert.All(host.HistoryRenders.Where(render => render.Text.Length > 0),
            render => Assert.Equal(8, render.Style.LineSpacing));
        Assert.Contains(host.PresentedRanges, range => range == (0L, 60000L));
        Assert.Contains(false, host.WaitIndicatorEnabledChanges);
    }
}

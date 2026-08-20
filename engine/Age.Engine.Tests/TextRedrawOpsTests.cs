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
        var redraw = Assert.Single(host.HistoryRenders);
        Assert.Equal((1, 1, "restored"),
            (redraw.LayoutSlot, redraw.FirstRecordIndex, redraw.Text));
        Assert.Equal((0xffffffL, 0x123456L),
            (redraw.Style.TextColor, redraw.Style.EffectColor));
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

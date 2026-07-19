using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class HistoryInteractionOpsTests
{
    private const int T_IMM = 0, T_GINT = 3, T_LINT = 9;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(T_IMM, value);
    private static Operand G(int address) => new(T_GINT, address);
    private static Operand L(int address) => new(T_LINT, address);

    private sealed class StopAfterHistoryReturnsException : Exception { }

    private sealed class Sc0000HistoryCloseHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        private long _now;
        private int _modalSleeps;
        public bool HistoryReturned;
        public bool SawRenderedText;
        public override long InputClockMilliseconds => _now;

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            _now += System.Math.Max(16, duration);
            if (!Vm.IsRawInputCallbackActive) return;
            _modalSleeps++;
            if (_modalSleeps == 1)
            {
                Vm.UpdatePointer(790, 570); // HISTORY candidate 8: visible bottom-right close region
                Vm.UpdateMouseButtonState(0x1, true);
                Vm.QueueInputCallback(4);
            }
            else if (_modalSleeps == 3)
            {
                Vm.UpdateMouseButtonState(0x1, false);
                Vm.QueueInputCallback(10);
            }
        }

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Waits++;
            Vm.UpdatePointer(684, 572);
            while (serviceInputCallback()) { }
            Assert.True(Vm.TryActivatePointer(684, 572));
            while (serviceInputCallback()) { }
            HistoryReturned = !Vm.IsRawInputCallbackActive && !Vm.TextHistory.RecordingSuppressed;
            SawRenderedText = HistoryRenders.Any(render => render.Text.Length > 0);
            throw new StopAfterHistoryReturnsException();
        }
    }

    [Fact]
    public void FindHitRectangleScansAfterTheIncomingIndexWithInclusiveEdges()
    {
        var ops = new List<(int, Operand[])>();
        void Set(int address, long value) => ops.Add((0x55, new[] { L(address), I(value) }));

        for (int i = 0; i < 4; i++) Set(i, 0); // point-sized reference rectangle
        long[][] rectangles =
        {
            new long[] { 0, 10, 0, 10 },
            new long[] { 0, 20, 0, 20 },
            new long[] { 0, 20, 0, 20 },
        };
        for (int rectangle = 0; rectangle < rectangles.Length; rectangle++)
            for (int field = 0; field < 4; field++) Set(100 + rectangle * 4 + field, rectangles[rectangle][field]);
        foreach (var (address, value) in new[]
                 {
                     (200, 100L), (201, 200L), (202, 300L),
                     (210, 100L), (211, 200L), (212, 300L),
                 }) Set(address, value);
        Set(50, 0); // skip candidate 0 and begin at candidate 1
        ops.Add((0x12e, new[] { L(50), L(0), I(220), I(220), L(100), L(200), L(210), I(3) }));
        ops.Add((0x55, new[] { G(0x100), L(50) }));
        Set(50, 1);
        ops.Add((0x12e, new[] { L(50), L(0), I(321), I(320), L(100), L(200), L(210), I(3) }));
        ops.Add((0x55, new[] { G(0x101), L(50) }));
        ops.Add((0x2, Array.Empty<Operand>()));
        var script = ScriptAssembler.Assemble(Table, "HIT_RECT", ops, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(1, vm.Globals[0x100]); // (220,220) is on candidate 1's inclusive edge
        Assert.Equal(-1, vm.Globals[0x101]);
    }

    [Fact]
    public void RealSc0000HistoryButtonRendersAndClosesWithoutAdvancingThePageWait()
    {
        var scripts = Sys4ScriptProvider.Load(Table);
        var host = new Sc0000HistoryCloseHost();
        var vm = new VirtualMachine(scripts.RequireByName("SC0000.BIN"), Table, host,
            new VmOptions(MaxSteps: 2_000_000), scripts);
        host.Vm = vm;
        vm.Globals[0x6c1] = 1;

        Assert.Throws<StopAfterHistoryReturnsException>(() => vm.Run());

        Assert.True(host.SawRenderedText);
        Assert.True(host.HistoryReturned);
        Assert.Equal(1, host.Waits); // the enclosing ADV page was never released or re-entered
    }
}

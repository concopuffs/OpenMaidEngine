using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class HistoryInteractionOpsTests
{
    private const int T_IMM = 0, T_GINT = 3, T_LINT = 9, T_LPTR = 12;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(T_IMM, value);
    private static Operand G(int address) => new(T_GINT, address);
    private static Operand L(int address) => new(T_LINT, address);
    private static Operand P(int address) => new(T_LPTR, address);

    private sealed class StopAfterHistoryReturnsException : Exception { }
    private sealed class StopAfterHistoryVoiceException : Exception { }
    private sealed class StopAfterHistoryWheelException : Exception { }

    private static void SeedSystem4Services(Sys4ScriptProvider scripts, VirtualMachine vm)
    {
        var systemScript = scripts.RequireByName("SYSTEM4.BIN");
        Assert.Equal(9, AdvTextLayoutBootstrap.ApplyLeadingDefinitionsAndResets(
            systemScript, Table, vm.TextHistory));
        Assert.Equal(16, InputBindingBootstrap.Apply(systemScript, vm.InputBindings));
    }

    private sealed class Sc0000HistoryCloseHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        private readonly int _openAtWait;
        private long _now;
        private int _modalSleeps;
        public bool HistoryReturned;
        public bool SawRenderedText;
        public IReadOnlyList<RenderObject> FirstHistoryFrame = Array.Empty<RenderObject>();
        public override long InputClockMilliseconds => _now;

        public Sc0000HistoryCloseHost(int openAtWait = 1) => _openAtWait = openAtWait;

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            _now += System.Math.Max(16, duration);
            if (!Vm.IsRawInputCallbackActive) return;
            _modalSleeps++;
            if (_modalSleeps == 1)
            {
                FirstHistoryFrame = Vm.Gfx.SnapshotVisibleObjects(_now);
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
            if (Waits < _openAtWait) return;
            Vm.UpdatePointer(684, 572);
            while (serviceInputCallback()) { }
            Assert.True(Vm.TryActivatePointer(684, 572));
            while (serviceInputCallback()) { }
            HistoryReturned = !Vm.IsRawInputCallbackActive && !Vm.TextHistory.RecordingSuppressed;
            SawRenderedText = HistoryRenders.Any(render => render.Text.Length > 0);
            throw new StopAfterHistoryReturnsException();
        }
    }

    private sealed class Sc0000HistoryVoiceHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        private long _now;
        private int _modalSleeps;
        private bool _pressedVoiceRow;
        public AdvTextHistoryRenderBatch? ClickedBatch;
        public override long InputClockMilliseconds => _now;

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            _now += System.Math.Max(16, duration);
            if (!Vm.IsRawInputCallbackActive) return;
            _modalSleeps++;
            if (!_pressedVoiceRow)
            {
                ClickedBatch = HistoryRenders.LastOrDefault(batch => batch.Layout.OriginY < 600
                    && Vm.TextHistory.TryFindVoicePair(batch.FirstRecordIndex, out _, out _));
                if (ClickedBatch == null) return;
                _pressedVoiceRow = true;
                Vm.UpdatePointer(200, ClickedBatch.Layout.OriginY + 50);
                Vm.UpdateMouseButtonState(0x1, true);
                Vm.QueueInputCallback(4);
                return;
            }
            if (_modalSleeps == 3)
            {
                Vm.UpdateMouseButtonState(0x1, false);
                Vm.QueueInputCallback(10);
            }
            if (VoiceRequests.Count > 0) throw new StopAfterHistoryVoiceException();
        }

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Waits++;
            bool hasRecentVoice = Vm.TextHistory.Entries.TakeLast(5)
                .Any(entry => Vm.TextHistory.TryFindVoicePair(entry.FirstRecordIndex, out _, out _));
            if (!hasRecentVoice) return;
            Voices.Clear();
            VoiceRequests.Clear();
            Vm.UpdatePointer(684, 572);
            while (serviceInputCallback()) { }
            Assert.True(Vm.TryActivatePointer(684, 572));
            while (serviceInputCallback()) { }
        }
    }

    private sealed class Sc0000HistoryWheelHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        private readonly Dictionary<int, int> _before = new();
        private long _now;
        private bool _wheelQueued;
        public bool HistoryRowsChanged;
        public override long InputClockMilliseconds => _now;

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            _now += System.Math.Max(16, duration);
            if (!Vm.IsRawInputCallbackActive || ActiveHistoryRenders.Count == 0) return;
            if (!_wheelQueued)
            {
                foreach (var (slot, batch) in ActiveHistoryRenders)
                    _before[slot] = batch.FirstRecordIndex;
                _wheelQueued = true;
                Vm.QueueMouseWheelDelta(120); // native wheel-up sign: move toward older retained rows
                return;
            }
            HistoryRowsChanged = ActiveHistoryRenders.Any(pair =>
                !_before.TryGetValue(pair.Key, out int first) || first != pair.Value.FirstRecordIndex);
            if (HistoryRowsChanged) throw new StopAfterHistoryWheelException();
        }

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Waits++;
            if (Waits < 8) return;
            Vm.UpdatePointer(684, 572);
            while (serviceInputCallback()) { }
            Assert.True(Vm.TryActivatePointer(684, 572));
            while (serviceInputCallback()) { }
        }
    }

    [Fact]
    public void MouseWheelDeltaAccumulatesAndIsClearedByOpcode10d()
    {
        var script = ScriptAssembler.Assemble(Table, "WHEEL_DELTA",
            new List<(int, Operand[])>
            {
                (0x10d, new[] { G(0x100) }),
                (0x10d, new[] { G(0x101) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost());
        vm.QueueMouseWheelDelta(120);
        vm.QueueMouseWheelDelta(-360);

        vm.Run();

        Assert.Equal(-240, vm.Globals[0x100]);
        Assert.Equal(0, vm.Globals[0x101]);
    }

    [Fact]
    public void RealHistoryWheelUpNavigatesToOlderRetainedRows()
    {
        var scripts = Sys4ScriptProvider.Load(Table);
        var host = new Sc0000HistoryWheelHost();
        var vm = new VirtualMachine(scripts.RequireByName("SC0000.BIN"), Table, host,
            new VmOptions(MaxSteps: 2_000_000), scripts);
        host.Vm = vm;
        SeedSystem4Services(scripts, vm);
        vm.Globals[0x6c1] = 1;

        Assert.Throws<StopAfterHistoryWheelException>(() => vm.Run());

        Assert.True(host.HistoryRowsChanged);
        Assert.Equal(8, host.Waits); // the eighth enclosing ADV wait was not released or re-entered
    }

    [Fact]
    public void RealHistoryRendersMultipleRowsAfterSeveralSc0000Messages()
    {
        var scripts = Sys4ScriptProvider.Load(Table);
        var host = new Sc0000HistoryCloseHost(openAtWait: 6);
        var vm = new VirtualMachine(scripts.RequireByName("SC0000.BIN"), Table, host,
            new VmOptions(MaxSteps: 2_000_000), scripts);
        host.Vm = vm;
        SeedSystem4Services(scripts, vm);
        vm.Globals[0x6c1] = 1;

        Assert.Throws<StopAfterHistoryReturnsException>(() => vm.Run());

        var visibleRows = host.HistoryRenders
            .Where(render => render.Text.Length > 0)
            .GroupBy(render => render.LayoutSlot)
            .Select(group => group.Last())
            .ToArray();
        Assert.True(visibleRows.Length >= 2,
            $"Expected multiple retained History rows after six messages, got {visibleRows.Length}: "
            + string.Join(" | ", visibleRows.Select(render => render.Text)));
        Assert.All(visibleRows, render =>
        {
            Assert.Equal(650, render.Layout.Width);
            Assert.Equal(150, render.Layout.Height);
        });
    }

    [Fact]
    public void LookupArrayPreservesLocalStorageForLocalBases()
    {
        var script = ScriptAssembler.Assemble(Table, "LOCAL_LOOKUP",
            new List<(int, Operand[])>
            {
                (0x55, new[] { L(10), I(768) }),
                (0x55, new[] { L(11), I(121) }),
                (0x61, new[] { P(0), L(10), I(1) }),
                (0x55, new[] { G(0x100), P(0) }),
                (0x55, new[] { P(0), I(179) }),
                (0x55, new[] { G(0x101), L(11) }),
                (0x2, Array.Empty<Operand>()),
            }, Array.Empty<string>());
        var vm = new VirtualMachine(script, Table, new RecordingHost());

        vm.Run();

        Assert.Equal(121, vm.Globals[0x100]);
        Assert.Equal(179, vm.Globals[0x101]);
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
        SeedSystem4Services(scripts, vm);
        vm.Globals[0x6c1] = 1;

        Assert.Throws<StopAfterHistoryReturnsException>(() => vm.Run());

        Assert.True(host.SawRenderedText);
        Assert.True(host.HistoryReturned);
        Assert.Equal(1, host.Waits); // the enclosing ADV page was never released or re-entered
        Assert.Equal(1, host.HistoryPresentationEnds);
        Assert.Empty(host.ActiveHistoryRenders);
        Assert.Contains(false, host.WaitIndicatorEnabledChanges);
        Assert.Contains(1, host.PublishedAdvTextLayouts);

        var historyButtons = host.FirstHistoryFrame
            .Where(render => render.Handle >= 0xd2fa && render.Handle <= 0xd300)
            .OrderBy(render => render.Handle)
            .ToArray();
        Assert.Equal(new[]
        {
            (0xd2faL, 768, 121), (0xd2fbL, 768, 179), (0xd2fcL, 768, 237),
            (0xd2fdL, 768, 295), (0xd2feL, 768, 353), (0xd2ffL, 768, 411),
            (0xd300L, 768, 549),
        }, historyButtons.Select(render => (render.Handle, render.DstX, render.DstY)).ToArray());

        var visibleRows = host.HistoryRenders
            .Where(render => render.Text.Length > 0)
            .GroupBy(render => render.LayoutSlot)
            .Select(group => group.Last())
            .ToArray();
        Assert.NotEmpty(visibleRows);
        Assert.All(visibleRows, render => Assert.Equal(65, render.Layout.OriginX));
    }

    [Fact]
    public void RealHistoryVoicedRowDispatchesItsRetainedVoicePair()
    {
        var scripts = Sys4ScriptProvider.Load(Table);
        var host = new Sc0000HistoryVoiceHost();
        var vm = new VirtualMachine(scripts.RequireByName("SC0000.BIN"), Table, host,
            new VmOptions(MaxSteps: 2_000_000), scripts);
        host.Vm = vm;
        SeedSystem4Services(scripts, vm);
        vm.Globals[0x6c1] = 1;

        Assert.Throws<StopAfterHistoryVoiceException>(() => vm.Run());

        var batch = Assert.IsType<AdvTextHistoryRenderBatch>(host.ClickedBatch);
        Assert.True(vm.TextHistory.TryFindVoicePair(batch.FirstRecordIndex,
            out long expectedVoice, out long expectedVariant));
        Assert.Equal((0x24L, 0L), (expectedVoice, expectedVariant));
        Assert.Equal(new[] { (expectedVoice, checked((int)expectedVariant)) }, host.VoiceRequests);

        var resources = ResourceMap.Load();
        var voice = Assert.IsType<AssetEntry>(resources.ResolveVoice(expectedVoice));
        Assert.Equal("MAN999.OGG", voice.Name);
        Assert.NotEmpty(resources.ReadAudio(voice).Bytes);
    }
}

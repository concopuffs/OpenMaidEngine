using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using System.Buffers.Binary;

public class NumberedSaveVmTests
{
    private const int Immediate = 0;
    private const int GlobalInt = 3;
    private const int LocalInt = 9;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly NativeSaveIdentity Identity =
        new(NativeSaveMagic.S4SD, 0x4a343234, "numbered-vm-test", 3, 10, 0x42323234,
            new NativeSaveBankDimensions(402459, 1, 789, 1, 1, 1));

    [Fact]
    public void SaveOpcodeWritesLayoutThreeStateHistoryAndRetainedGfx()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            Script script = WithPackedId(ScriptAssembler.Assemble(Table, "SAVE_TEST.BIN",
            [
                (0xbf, [new Operand(Immediate, 24)]),
                (0xc0, [new Operand(GlobalInt, 0x21)]),
                (0xb4, [new Operand(Immediate, 0x3321), new Operand(Immediate, 1)]),
                (0x1ad, Array.Empty<Operand>()),
                (0x19e, [new Operand(GlobalInt, 0x20), new Operand(Immediate, 2)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x77);
            var history = new AdvTextHistory();
            history.DefineLayout(1, 400, 120, 75, 340);
            history.AppendText(1, 0x40, "保存", AdvTextStyle.Default);
            var vm = new VirtualMachine(
                script, Table, new RecordingHost(), textHistory: history, nativeDatStore: store);
            vm.Globals[0x123] = 456;
            vm.GlobalFloats[0] = BitConverter.SingleToInt32Bits(2.5f);
            vm.GlobalStrings[4] = "姫狩り";
            vm.Gfx.SetSurface(3, 0x1234, 0xff00ff);
            vm.Gfx.SetSurface(5, 0x5678, 0);
            vm.Gfx.SetSurfaceReloadOnRestore(5, true);
            vm.Gfx.CreateSurface(4);
            vm.Gfx.SetSurface(7, 0x7777, 0);
            vm.Gfx.SetSurfaceReloadOnRestore(7, true);
            vm.Gfx.ReleaseSurfaceRange(7, 1);
            vm.Gfx.BindDraw(100, 3, 1, 2, 30, 40, 50, 60);
            vm.Gfx.SetCurrentTranslation(100, (7, 8, 9));
            vm.Gfx.SetRotationChannel(100, 0, 500, (0, 0, 1), 90);

            vm.Run();

            Assert.Equal(0, vm.Globals[0x20]);
            Assert.Equal(24, vm.Globals[0x21]);
            NativeNumberedSaveFile file = store.LoadNumberedFile(2)!;
            NativeNumberedSaveState state = NativeNumberedSaveCodec.Decode(file.Document.Payload);
            Assert.Equal(0x6241b, state.IntegerGlobals.Count);
            Assert.Equal(456, state.IntegerGlobals[0x123]);
            Assert.Equal(24, state.BgmTrackId);
            Assert.Equal(0x3321, state.SoundEffectResourceIds[1]);
            Assert.Equal("姫狩り", state.StringGlobals[4]);
            Assert.Equal(0x77u, state.Frames.Single().ScriptId);
            NativeSavedGfxObject savedObject =
                Assert.Single(state.GfxObjects, item => item.Handle == 100);
            Assert.Equal(0, ReadGfxField(savedObject.Record, 0x34));
            Assert.Equal(-1, ReadGfxField(savedObject.Record, 0x60));
            Assert.Equal(0, ReadGfxField(savedObject.Record, 0x238));
            Assert.Equal(-1, ReadGfxField(savedObject.Record, 0x240));
            foreach (int matrixOffset in new[] { 0x6c, 0xac, 0xec, 0x16c, 0x1ac })
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(1), ReadGfxField(savedObject.Record, matrixOffset));
                Assert.Equal(BitConverter.SingleToInt32Bits(1), ReadGfxField(savedObject.Record, matrixOffset + 0x14));
                Assert.Equal(BitConverter.SingleToInt32Bits(1), ReadGfxField(savedObject.Record, matrixOffset + 0x28));
                Assert.Equal(BitConverter.SingleToInt32Bits(1), ReadGfxField(savedObject.Record, matrixOffset + 0x3c));
            }
            Assert.Equal(BitConverter.SingleToInt32Bits(1), ReadGfxField(savedObject.Record, 0x168));
            Assert.Equal(BitConverter.SingleToInt32Bits(7), ReadGfxField(savedObject.Record, 0x19c));
            Assert.Equal(BitConverter.SingleToInt32Bits(8), ReadGfxField(savedObject.Record, 0x1a0));
            Assert.Equal(BitConverter.SingleToInt32Bits(9), ReadGfxField(savedObject.Record, 0x1a4));
            Assert.Equal(BitConverter.SingleToInt32Bits(1), ReadGfxField(savedObject.Record, 0x130));
            Assert.Equal(BitConverter.SingleToInt32Bits(-1), ReadGfxField(savedObject.Record, 0x13c));
            Assert.Equal(0x1234, ReadSurfaceField(state, 3, 0));
            Assert.Equal(0, ReadSurfaceField(state, 3, 8));
            Assert.Equal(-1, ReadSurfaceField(state, 4, 0));
            Assert.Equal(1, ReadSurfaceField(state, 4, 0x10));
            Assert.Equal(0x5678, ReadSurfaceField(state, 5, 0));
            Assert.Equal(1, ReadSurfaceField(state, 5, 8));
            Assert.Equal(-1, ReadSurfaceField(state, 6, 0));
            Assert.Equal(-1, ReadSurfaceField(state, 7, 0));
            Assert.Equal(1, ReadSurfaceField(state, 7, 8));
            var restoredHistory = new AdvTextHistory();
            NativeTextHistoryCodec.DecodeInto(file.HistoryTail, restoredHistory);
            Assert.Equal("保存", restoredHistory.Records.Single().Text);
            Assert.NotNull(store.LoadShared());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FullLoadRestoresBanksThenResumesSavedScriptThroughOpcodeAe()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            Script resumed = WithTables(WithPackedId(ScriptAssembler.Assemble(Table, "RESUMED.BIN",
            [
                (Table.ByLabel("mov")!.Value,
                    [new Operand(GlobalInt, 0x510), new Operand(Immediate, 11)]),
                (0xae, Array.Empty<Operand>()),
                (0x3, [new Operand(Immediate, 0x89)]),
                (Table.ByLabel("mov")!.Value,
                    [new Operand(GlobalInt, 0x500), new Operand(GlobalInt, 0x123)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x88), scriptCallOffsets: [6]);
            Script child = WithPackedId(ScriptAssembler.Assemble(Table, "CHILD.BIN",
            [
                (Table.ByLabel("mov")!.Value,
                    [new Operand(GlobalInt, 0x511), new Operand(Immediate, 22)]),
                (0xae, Array.Empty<Operand>()),
                (Table.ByLabel("mov")!.Value,
                    [new Operand(GlobalInt, 0x501), new Operand(GlobalInt, 0x123)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x89);
            Script loader = WithPackedId(ScriptAssembler.Assemble(Table, "LOADER.BIN",
            [
                (0x1a1, [new Operand(LocalInt, 0), new Operand(Immediate, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x99);
            Script callback = WithPackedId(ScriptAssembler.Assemble(Table, "CALLBACK_LOAD.BIN",
            [
                (Table.ByLabel("mov")!.Value,
                    [new Operand(GlobalInt, 0x502), new Operand(Immediate, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x90);
            NativeNumberedSaveState state = NativeNumberedSaveCodec.Empty(
            [
                new NativeSavedScriptFrame(-1, 0x88, Array.Empty<int>(), -1, 0),
                new NativeSavedScriptFrame(0, 0x89, Array.Empty<int>(), -1, -1),
            ]) with
            {
                BgmTrackId = 24,
                SoundEffectResourceIds =
                [
                    0, 0x3321, 0x2aea, 0, 0,
                    0, 0, 0, 0, 0,
                ],
                IntegerGlobals = DenseIntBank(0x124, (0x123, 456)),
                FloatGlobals = [BitConverter.SingleToInt32Bits(3.5f)],
                StringGlobals = ["復帰"],
                PointerGlobals = [0x123],
                PointerStrings = [0],
                LocalPointerScratch = [0],
                SurfaceRecords = NativeSurfaceRecords(3, 0x1234, 0xff00ff),
                GfxObjects = [new NativeSavedGfxObject(100, NativeGfxRecord(3))],
            };
            var history = new AdvTextHistory();
            history.DefineLayout(1, 100, 50, 0, 0);
            history.AppendText(1, 0, "履歴復帰", AdvTextStyle.Default);
            store.SaveNumberedFile(
                1, NativeNumberedSaveCodec.Encode(state), NativeTextHistoryCodec.Encode(history),
                NativeSystemTime.FromLocalDateTime(DateTime.Now), 123);
            var liveHistory = new AdvTextHistory();
            var trace = new RecordingTraceSink();
            var host = new RecordingHost();
            var vm = new VirtualMachine(
                loader, Table, host, provider: new MapProvider(
                    new()
                    {
                        [0x88] = resumed,
                        [0x89] = child,
                    },
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CALLBACK_LOAD.BIN"] = callback,
                    }),
                sink: trace, textHistory: liveHistory, nativeDatStore: store);
            vm.Globals[0x10] = 999;
            vm.Globals[0x123] = 999;
            vm.Globals[0x124] = 777;
            vm.GlobalStrings[0] = "stale";
            vm.GlobalStrings[1] = "static-unit-name";
            vm.Gfx.SetSurface(15, 0x3383, 0);

            vm.Run();

            Assert.False(vm.Globals.ContainsKey(0x10));
            Assert.Equal(456, vm.Globals[0x123]);
            Assert.Equal(777, vm.Globals[0x124]);
            Assert.Equal(456, vm.Globals[0x500]);
            Assert.Equal(456, vm.Globals[0x501]);
            Assert.Equal(11, vm.Globals[0x510]);
            Assert.Equal(22, vm.Globals[0x511]);
            Assert.Equal(1, vm.Globals[0x502]);
            Assert.Equal("復帰", vm.GlobalStrings[0]);
            Assert.Equal(0x123, vm.GlobalPointers[0]);
            Assert.Equal("static-unit-name", vm.GlobalStrings[1]);
            Assert.Equal([24L], host.BgmTracks);
            Assert.Equal(
                [(0x3321L, 1), (0x2aeaL, 2)],
                host.SfxLoads);
            Assert.Equal(Enumerable.Range(0, 10), host.SfxReleases);
            Assert.Equal("履歴復帰", liveHistory.Records.Single().Text);
            Assert.Equal(3, vm.Gfx.QuerySlot(100));
            Assert.Contains(
                vm.Gfx.CapturePersistenceSnapshot().Surfaces,
                item => item.Slot == 15 && item.ResourceId == 0x3383);
            Assert.Contains(host.Textures, item => item == (0x1234, 3));
            Assert.DoesNotContain(host.Textures, item => item.Slot == 15);
            Assert.Empty(host.ReleasedSurfaceRanges);
            RenderObject restoredObject = Assert.Single(vm.Gfx.SnapshotVisibleObjects());
            Assert.Equal(0x1234, restoredObject.SurfaceResId);
            Assert.Equal((50, 60), (restoredObject.DstX, restoredObject.DstY));
            Assert.Contains(trace.Events, item =>
                item.Kind == Age.Engine.Diagnostics.TraceEventKind.FrameEnter
                && item.Name == "CHILD.BIN"
                && item.Cause == Age.Engine.Diagnostics.FrameCause.SaveRestore);
            Assert.DoesNotContain(trace.Events, item =>
                item.Kind == Age.Engine.Diagnostics.TraceEventKind.FrameEnter
                && item.Name == "CHILD.BIN"
                && item.Cause == Age.Engine.Diagnostics.FrameCause.CallScript);
            Assert.Equal("exit", vm.HaltReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FullLoadReestablishesSaveBoundaryBeforeNestedSaveUiWritesAnotherSlot()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            int callScript = Table.ByLabel("call-script")!.Value;
            Script resumed = WithTables(WithPackedId(ScriptAssembler.Assemble(Table, "RESUMED.BIN",
            [
                (0xae, Array.Empty<Operand>()),
                (callScript, [new Operand(Immediate, 0x89)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x88), scriptCallOffsets: [1]);
            Script child = WithPackedId(ScriptAssembler.Assemble(Table, "CHILD.BIN",
            [
                (0xae, Array.Empty<Operand>()),
                (callScript, [new Operand(Immediate, 0x91)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x89);
            Script saveUi = WithPackedId(ScriptAssembler.Assemble(Table, "SAVE_UI.BIN",
            [
                (0x19e, [new Operand(LocalInt, 0), new Operand(Immediate, 2)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x91);
            Script loader = WithPackedId(ScriptAssembler.Assemble(Table, "LOADER.BIN",
            [
                (0x1a1, [new Operand(LocalInt, 0), new Operand(Immediate, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x99);
            byte[] opaqueRecord = NativeGfxRecord(3);
            BinaryPrimitives.WriteInt32LittleEndian(opaqueRecord.AsSpan(0x68), 0x12345678);
            NativeNumberedSaveState source = NativeNumberedSaveCodec.Empty(
            [
                new NativeSavedScriptFrame(-1, 0x88, [], 8, 0),
                new NativeSavedScriptFrame(0, 0x89, [], -1, -1),
            ]) with
            {
                GfxObjects = [new NativeSavedGfxObject(100, opaqueRecord)],
            };
            store.SaveNumberedFile(
                1, NativeNumberedSaveCodec.Encode(source), [],
                NativeSystemTime.FromLocalDateTime(DateTime.Now), 0);
            var vm = new VirtualMachine(
                loader, Table, new RecordingHost(),
                provider: new MapProvider(new()
                {
                    [0x88] = resumed,
                    [0x89] = child,
                    [0x91] = saveUi,
                }),
                nativeDatStore: store);

            vm.Run();

            NativeNumberedSaveFile? resaved = store.LoadNumberedFile(2);
            Assert.NotNull(resaved);
            NativeNumberedSaveState state =
                NativeNumberedSaveCodec.Decode(resaved!.Document.Payload);
            Assert.Equal([0x88u, 0x89u], state.Frames.Select(frame => frame.ScriptId));
            Assert.Equal(8, state.Frames[0].ResumeIndex);
            Assert.Equal(0, state.Frames[0].CallTargetIndex);
            Assert.Equal(
                0x12345678,
                ReadGfxField(Assert.Single(state.GfxObjects).Record, 0x68));

            Script reloader = WithPackedId(ScriptAssembler.Assemble(Table, "RELOADER.BIN",
            [
                (0x1a1, [new Operand(LocalInt, 0), new Operand(Immediate, 2)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x9a);
            var trace = new RecordingTraceSink();
            var roundTrippedVm = new VirtualMachine(
                reloader, Table, new RecordingHost(),
                provider: new MapProvider(new()
                {
                    [0x88] = resumed,
                    [0x89] = child,
                    [0x91] = saveUi,
                }),
                sink: trace,
                nativeDatStore: store);

            roundTrippedVm.Run();

            Assert.DoesNotContain(trace.Events, item =>
                item.Kind == Age.Engine.Diagnostics.TraceEventKind.FrameEnter
                && item.Name == "CHILD.BIN"
                && item.Cause == Age.Engine.Diagnostics.FrameCause.CallScript);
            Assert.Equal("exit", roundTrippedVm.HaltReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoredChildRootReloadDiscardsSyntheticAncestorChain()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            int callScript = Table.ByLabel("call-script")!.Value;
            int move = Table.ByLabel("mov")!.Value;
            Script resumed = WithTables(WithPackedId(ScriptAssembler.Assemble(Table, "RESUMED.BIN",
            [
                (0xae, Array.Empty<Operand>()),
                (callScript, [new Operand(Immediate, 0x89)]),
                (move, [new Operand(GlobalInt, 0x600), new Operand(Immediate, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x88), scriptCallOffsets: [1]);
            Script child = WithPackedId(ScriptAssembler.Assemble(Table, "CHILD_RELOAD.BIN",
            [
                (0xae, Array.Empty<Operand>()),
                (0x9, Array.Empty<Operand>()),
            ], []), 0x89);
            Script reloaded = WithPackedId(ScriptAssembler.Assemble(Table, "RELOADED.BIN",
            [
                (move, [new Operand(GlobalInt, 0x601), new Operand(Immediate, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0);
            Script loader = WithPackedId(ScriptAssembler.Assemble(Table, "LOADER.BIN",
            [
                (0x1a1, [new Operand(LocalInt, 0), new Operand(Immediate, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x99);
            NativeNumberedSaveState source = NativeNumberedSaveCodec.Empty(
            [
                new NativeSavedScriptFrame(-1, 0x88, [], -1, 0),
                new NativeSavedScriptFrame(0, 0x89, [], -1, -1),
            ]);
            store.SaveNumberedFile(
                1, NativeNumberedSaveCodec.Encode(source), [],
                NativeSystemTime.FromLocalDateTime(DateTime.Now), 0);
            var vm = new VirtualMachine(
                loader, Table, new RecordingHost(),
                provider: new MapProvider(new()
                {
                    [0] = reloaded,
                    [0x88] = resumed,
                    [0x89] = child,
                }),
                nativeDatStore: store);

            vm.Run();

            Assert.False(vm.Globals.ContainsKey(0x600));
            Assert.Equal(1, vm.Globals[0x601]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DataOnlyLoadReleasesAllSurfacesWhenBothNativeSettingsEnableIt()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            Script loader = ScriptAssembler.Assemble(Table, "LOAD_DATA_ONLY.BIN",
            [
                (0x19f, [new Operand(LocalInt, 0), new Operand(Immediate, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []);
            NativeNumberedSaveState state = NativeNumberedSaveCodec.Empty(
                [new NativeSavedScriptFrame(-1, 0, [], -1, -1)]) with
            {
                SurfaceRecords = NativeSurfaceRecords(3, 0x1234, 0xff00ff),
            };
            store.SaveNumberedFile(
                1, NativeNumberedSaveCodec.Encode(state), [],
                NativeSystemTime.FromLocalDateTime(DateTime.Now), 0);
            var host = new RecordingHost();
            var vm = new VirtualMachine(
                loader, Table, host,
                new VmOptions(CreateObject: true, AutoFreeTextures: true),
                nativeDatStore: store);
            vm.Gfx.SetSurface(15, 0x3383, 0);

            vm.Run();

            Assert.Contains((0, 1000), host.ReleasedSurfaceRanges);
            Assert.DoesNotContain(
                vm.Gfx.CapturePersistenceSnapshot().Surfaces,
                item => item.Slot == 15);
            Assert.Contains(
                vm.Gfx.CapturePersistenceSnapshot().Surfaces,
                item => item.Slot == 3
                    && item.ResourceId == 0x1234
                    && item.ReloadOnRestore);
            Assert.Contains(host.Textures, item => item == (0x1234, 3));
            Assert.Equal("exit", vm.HaltReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScriptEntryClearsReloadPolicyWithoutReleasingTheSurface()
    {
        Script script = ScriptAssembler.Assemble(Table, "ENTRY.BIN",
        [
            (0x259, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
        ], []);
        var vm = new VirtualMachine(script, Table, new RecordingHost());
        vm.Gfx.SetSurface(15, 0x3383, 0);
        vm.Gfx.SetSurfaceReloadOnRestore(15, true);

        vm.Run();

        GfxSurfacePersistenceState surface = Assert.Single(
            vm.Gfx.CapturePersistenceSnapshot().Surfaces);
        Assert.Equal(15, surface.Slot);
        Assert.Equal(0x3383, surface.ResourceId);
        Assert.False(surface.ReloadOnRestore);
    }

    [Fact]
    public void SurfacePersistenceDeclarationSetsAndClearsReloadBit()
    {
        Script script = ScriptAssembler.Assemble(Table, "SURFACE_POLICY.BIN",
        [
            (0x258, [new Operand(Immediate, 15), new Operand(Immediate, 1)]),
            (0x258, [new Operand(Immediate, 16), new Operand(Immediate, 3)]),
            (0x258, [new Operand(Immediate, 15), new Operand(Immediate, 0)]),
            (0x2, Array.Empty<Operand>()),
        ], []);
        var vm = new VirtualMachine(script, Table, new RecordingHost());
        vm.Gfx.SetSurface(15, 0x3383, 0);
        vm.Gfx.SetSurface(16, 0x3384, 0);

        vm.Run();

        GfxSurfacePersistenceState[] surfaces =
            vm.Gfx.CapturePersistenceSnapshot().Surfaces.ToArray();
        Assert.False(Assert.Single(surfaces, item => item.Slot == 15).ReloadOnRestore);
        Assert.True(Assert.Single(surfaces, item => item.Slot == 16).ReloadOnRestore);
    }

    private static Script WithPackedId(Script source, uint packedId)
        => new()
        {
            Name = source.Name,
            PackedId = packedId,
            Header = source.Header,
            Instructions = source.Instructions,
            IndexByOffset = source.IndexByOffset,
            Strings = source.Strings,
            BodyDwords = source.BodyDwords,
            ReadMessageOffsets = source.ReadMessageOffsets,
            ScriptCallOffsets = source.ScriptCallOffsets,
            LocalCallOffsets = source.LocalCallOffsets,
        };

    private static Script WithTables(
        Script source,
        IReadOnlyList<int>? scriptCallOffsets = null,
        IReadOnlyList<int>? localCallOffsets = null)
        => new()
        {
            Name = source.Name,
            PackedId = source.PackedId,
            Header = source.Header,
            Instructions = source.Instructions,
            IndexByOffset = source.IndexByOffset,
            Strings = source.Strings,
            BodyDwords = source.BodyDwords,
            ReadMessageOffsets = source.ReadMessageOffsets,
            ScriptCallOffsets = scriptCallOffsets ?? source.ScriptCallOffsets,
            LocalCallOffsets = localCallOffsets ?? source.LocalCallOffsets,
        };

    private static int[] DenseIntBank(int count, params (int Index, int Value)[] values)
    {
        var result = new int[count];
        foreach (var (index, value) in values) result[index] = value;
        return result;
    }

    private static byte[] NativeSurfaceRecords(int slot, int resourceId, int colorKey)
    {
        byte[] result = new byte[NativeNumberedSaveState.SurfaceRecordsSize];
        for (int index = 0; index < 1000; index++)
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(index * 20), -1);
        int at = slot * 20;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), resourceId);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at + 4), unchecked((int)(0xff000000u | (uint)colorKey)));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at + 8), 1);
        return result;
    }

    private static int ReadSurfaceField(NativeNumberedSaveState state, int slot, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(
            state.SurfaceRecords.AsSpan(slot * 20 + offset));

    private static byte[] NativeGfxRecord(int sourceSlot)
    {
        byte[] result = new byte[NativeNumberedSaveState.GfxRecordSize];
        void Write(int offset, int value)
            => BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset), value);
        Write(0, 1);
        Write(4, sourceSlot);
        Write(8, 1);
        Write(0x0c, 2);
        Write(0x10, 31);
        Write(0x14, 42);
        Write(0x24, 50);
        Write(0x28, 60);
        Write(0x60, -1);
        Write(0x64, -1);
        foreach (int matrixOffset in new[] { 0x6c, 0xac, 0xec, 0x12c, 0x16c, 0x1ac })
        {
            Write(matrixOffset, BitConverter.SingleToInt32Bits(1));
            Write(matrixOffset + 0x14, BitConverter.SingleToInt32Bits(1));
            Write(matrixOffset + 0x28, BitConverter.SingleToInt32Bits(1));
            Write(matrixOffset + 0x3c, BitConverter.SingleToInt32Bits(1));
        }
        Write(0x240, -1);
        return result;
    }

    private static int ReadGfxField(byte[] record, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(offset));

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "age-save-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

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
        new(NativeSaveMagic.S4SD, 0x4a343234, "numbered-vm-test", 3, 10, 0x42323234);

    [Fact]
    public void SaveOpcodeWritesLayoutThreeStateHistoryAndRetainedGfx()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            Script script = WithPackedId(ScriptAssembler.Assemble(Table, "SAVE_TEST.BIN",
            [
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
            vm.Gfx.BindDraw(100, 3, 1, 2, 30, 40, 50, 60);

            vm.Run();

            Assert.Equal(0, vm.Globals[0x20]);
            NativeNumberedSaveFile file = store.LoadNumberedFile(2)!;
            NativeNumberedSaveState state = NativeNumberedSaveCodec.Decode(file.Document.Payload);
            Assert.Equal(0x6241b, state.IntegerGlobals.Count);
            Assert.Equal(456, state.IntegerGlobals[0x123]);
            Assert.Equal("姫狩り", state.StringGlobals[4]);
            Assert.Equal(0x77u, state.Frames.Single().ScriptId);
            Assert.Contains(state.GfxObjects, item => item.Handle == 100);
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
                (0xae, Array.Empty<Operand>()),
                (0x3, [new Operand(Immediate, 0x89)]),
                (Table.ByLabel("mov")!.Value,
                    [new Operand(GlobalInt, 0x500), new Operand(GlobalInt, 0x123)]),
                (0x2, Array.Empty<Operand>()),
            ], []), 0x88), scriptCallOffsets: [1]);
            Script child = WithPackedId(ScriptAssembler.Assemble(Table, "CHILD.BIN",
            [
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
            var vm = new VirtualMachine(
                loader, Table, new RecordingHost(), provider: new MapProvider(
                    new()
                    {
                        [0x88] = resumed,
                        [0x89] = child,
                    },
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CALLBACK_LOAD.BIN"] = callback,
                    }),
                textHistory: liveHistory, nativeDatStore: store);
            vm.Globals[0x123] = 999;

            vm.Run();

            Assert.Equal(456, vm.Globals[0x123]);
            Assert.Equal(456, vm.Globals[0x500]);
            Assert.Equal(456, vm.Globals[0x501]);
            Assert.Equal(1, vm.Globals[0x502]);
            Assert.Equal("復帰", vm.GlobalStrings[0]);
            Assert.Equal(0x123, vm.GlobalPointers[0]);
            Assert.Equal("履歴復帰", liveHistory.Records.Single().Text);
            Assert.Equal(3, vm.Gfx.QuerySlot(100));
            RenderObject restoredObject = Assert.Single(vm.Gfx.SnapshotVisibleObjects());
            Assert.Equal(0x1234, restoredObject.SurfaceResId);
            Assert.Equal((50, 60), (restoredObject.DstX, restoredObject.DstY));
            Assert.Equal("exit", vm.HaltReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        int at = slot * 20;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), resourceId);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at + 4), unchecked((int)(0xff000000u | (uint)colorKey)));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at + 8), 1);
        return result;
    }

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
        Write(0x238, 1);
        Write(0x23c, 1);
        return result;
    }

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "age-save-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

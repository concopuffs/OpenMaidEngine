using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class SaveUiIntegrationTests
{
    private sealed class MenuReadyException : Exception;
    private sealed class InstalledResumeReachedException : Exception;

    private sealed class StopAtFirstMenuPollHost : RecordingHost
    {
        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            throw new MenuReadyException();
        }
    }

    private sealed class ClickFirstSlotHost : RecordingHost
    {
        private long _now;
        private bool _pressed;
        private bool _released;
        public VirtualMachine Vm { get; set; } = null!;
        public override long InputClockMilliseconds => _now;

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            _now += Math.Max(1, duration);
            if (!_pressed)
            {
                Vm.UpdatePointer(400, 70);
                Vm.UpdateMouseButtonState(0x1, true);
                _pressed = true;
            }
            if (!_released && _now >= 32)
            {
                Vm.UpdateMouseButtonState(0x1, false);
                _released = true;
            }
        }
    }

    private sealed class SaveUiProvider(
        Sys4ScriptProvider native,
        Script? resumed,
        Script loadCallback) : IScriptProvider
    {
        public Script? GetById(long id)
            => resumed != null && id == resumed.PackedId ? resumed : native.GetById(id);

        public Script? GetByName(string name)
            => name.Equals("CALLBACK_LOAD.BIN", StringComparison.OrdinalIgnoreCase)
                ? loadCallback
                : native.GetByName(name);

        public IReadOnlyList<int> MountedAppendSelectors => native.MountedAppendSelectors;
    }

    private sealed class StopAtInstalledResumeSink : ITraceSink
    {
        public bool TracingSteps => false;

        public void Emit(in TraceEvent item)
        {
            if (item.Kind == TraceEventKind.FrameEnter
                && item.Cause == FrameCause.SaveRestore
                && !string.Equals(item.Name, "CALLBACK_LOAD.BIN",
                    StringComparison.OrdinalIgnoreCase))
                throw new InstalledResumeReachedException();
        }
    }

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly NativeSaveIdentity Identity =
        new(NativeSaveMagic.S4SD, 0x4a343234, "姫狩りダンジョンマイスター",
            3, 10, 0x42323234);

    [Fact]
    public void RealSaveBinListsNativeSlotMetadataAndThumbnail()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            store.SaveNumbered(
                0, [1, 2, 3, 4],
                new NativeSystemTime(2026, 7, 5, 24, 13, 42, 17, 321),
                7_445);
            var pixels = new byte[112 * 84 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0x12;
                pixels[i + 1] = 0x34;
                pixels[i + 2] = 0x56;
                pixels[i + 3] = 0xff;
            }
            store.SaveNumberedThumbnail(
                0, NumberedThumbnailCodec.Encode(new(112, 84, pixels)));

            var scripts = Sys4ScriptProvider.Load(Table);
            var host = new StopAtFirstMenuPollHost();
            var vm = new VirtualMachine(
                scripts.RequireByName("SAVE.BIN"), Table, host,
                new VmOptions(MaxSteps: 500_000), scripts,
                nativeDatStore: store);

            Assert.Throws<MenuReadyException>(() => vm.Run());

            RgbaImage thumbnail = Assert.Single(host.SurfacePixels.Values);
            Assert.Equal((112, 84), (thumbnail.Width, thumbnail.Height));
            Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0xff }, thumbnail.Pixels[..4]);
            Assert.Contains(host.SurfaceStrings, item => item.Text == "2026");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "07");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "24");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "13");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RealSaveBinClickLoadsNativeSlotAndEntersRestoreRendezvous()
    {
        const uint resumedId = 0x00fefefe;
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            Script resumed = WithPackedId(ScriptAssembler.Assemble(Table, "SAVE_UI_RESUME.BIN",
            [
                (0xae, Array.Empty<Operand>()),
                (Table.ByLabel("mov")!.Value,
                    [new Operand(3, 0x500), new Operand(0, 1)]),
                (0x2, Array.Empty<Operand>()),
            ], []), resumedId);
            Script loadCallback = WithPackedId(ScriptAssembler.Assemble(
                Table, "CALLBACK_LOAD.BIN",
                [(0x2, Array.Empty<Operand>())], []), 0x00fefefd);
            NativeNumberedSaveState state = NativeNumberedSaveCodec.Empty(
                [new NativeSavedScriptFrame(-1, resumedId, [], -1, -1)]);
            store.SaveNumberedFile(
                0, NativeNumberedSaveCodec.Encode(state), [],
                new NativeSystemTime(2026, 7, 5, 24, 13, 42, 17, 321),
                7_445);

            var nativeScripts = Sys4ScriptProvider.Load(Table);
            var provider = new SaveUiProvider(nativeScripts, resumed, loadCallback);
            var host = new ClickFirstSlotHost();
            var trace = new RecordingTraceSink();
            var vm = new VirtualMachine(
                nativeScripts.RequireByName("SAVE.BIN"), Table, host,
                new VmOptions(MaxSteps: 1_000_000), provider, trace,
                nativeDatStore: store);
            host.Vm = vm;
            vm.Globals[0x6241b] = 1; // LOAD mode
            vm.Globals[0x696] = 0;   // caller allows direct load without the optional confirmation

            vm.Run();

            string entered = string.Join(",", trace.Events
                .Where(item => item.Kind == Age.Engine.Diagnostics.TraceEventKind.FrameEnter)
                .Select(item => item.Name));
            Assert.True(vm.Globals.GetValueOrDefault(0x500) == 1,
                $"halt={vm.HaltReason}; steps={vm.Steps}; sleeps={host.SleptDurations.Count}; entered={entered}");
            Assert.Equal("exit", vm.HaltReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstalledSave00DecodesAndResolvesThroughRealSaveBinWhenPresent()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Eushully", "姫狩りダンジョンマイスター", "SAVE");
        if (!File.Exists(Path.Combine(root, "SAVE00.DAT"))) return;

        var nativeScripts = Sys4ScriptProvider.Load(Table);
        Script loadCallback = WithPackedId(ScriptAssembler.Assemble(
            Table, "CALLBACK_LOAD.BIN",
            [(0x2, Array.Empty<Operand>())], []), 0x00fefefd);
        var provider = new SaveUiProvider(nativeScripts, null, loadCallback);
        var host = new ClickFirstSlotHost();
        var vm = new VirtualMachine(
            nativeScripts.RequireByName("SAVE.BIN"), Table, host,
            new VmOptions(MaxSteps: 1_000_000), provider,
            new StopAtInstalledResumeSink(),
            nativeDatStore: new DirectoryNativeDatStore(root, Identity));
        host.Vm = vm;
        vm.Globals[0x6241b] = 1;
        vm.Globals[0x696] = 0;

        Assert.Throws<InstalledResumeReachedException>(() => vm.Run());
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

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "age-save-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

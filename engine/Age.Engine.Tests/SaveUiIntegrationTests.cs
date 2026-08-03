using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;

[Trait("Category", "Workspace")]
public class SaveUiIntegrationTests
{
    private sealed class MenuReadyException : Exception;
    private sealed class InstalledResumeReachedException : Exception;
    private sealed class InstalledGameplayPollReachedException : Exception;
    private sealed class InstalledRootReloadReachedException(string message) : Exception(message);

    private sealed class StopAtFirstMenuPollHost : RecordingHost
    {
        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            throw new MenuReadyException();
        }
    }

    private class ClickFirstSlotHost : RecordingHost
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

    private sealed class InstalledContinuationState(int expectedRestoreFrames)
    {
        public int RestoreFrameCount;
        public bool TerminalRestoreEntered => RestoreFrameCount >= expectedRestoreFrames;
        public readonly List<string> Frames = new();
    }

    private sealed class ContinueInstalledLoadHost(InstalledContinuationState state) : ClickFirstSlotHost
    {
        public override void WaitForInput(
            int layoutSlot, Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
        {
            if (state.TerminalRestoreEntered) throw new InstalledGameplayPollReachedException();
            base.WaitForInput(layoutSlot, serviceInputCallback, autoWaitState);
        }

        public override void Sleep(long duration)
        {
            base.Sleep(duration);
            if (state.TerminalRestoreEntered) throw new InstalledGameplayPollReachedException();
        }
    }

    private sealed class InstalledContinuationSink(InstalledContinuationState state) : ITraceSink
    {
        public bool TracingSteps => false;

        public void Emit(in TraceEvent item)
        {
            if (item.Kind == TraceEventKind.FrameEnter)
            {
                state.Frames.Add($"+{item.Name}:{item.Cause}");
                if (item.Cause == FrameCause.SaveRestore
                    && !string.Equals(item.Name, "CALLBACK_LOAD.BIN",
                        StringComparison.OrdinalIgnoreCase))
                    state.RestoreFrameCount++;
                if (item.Cause == FrameCause.RootReload)
                    throw new InstalledRootReloadReachedException(
                        string.Join(", ", state.Frames));
            }
            else if (item.Kind == TraceEventKind.FrameExit)
            {
                state.Frames.Add($"-{item.Name}:{item.Text}");
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
            var authoredProfile = new SharedProfile();
            authoredProfile.StoreString(0x000, "序章 封印、そして");
            authoredProfile.StoreString(0x0d2, "base part-time job");
            authoredProfile.StoreString(0x1a4, "Lily");
            authoredProfile.StoreInteger(0x0d7, 80);
            authoredProfile.StoreInteger(0x27b, 98);
            authoredProfile.StoreInteger(0x34d, 69);
            authoredProfile.StoreInteger(0x41f, 0);
            authoredProfile.StoreInteger(0x4f1, 0b1011);
            authoredProfile.Save(
                store, new NativeSystemTime(2026, 7, 5, 24, 13, 42, 17, 321), 7_445);
            var loadedProfile = new SharedProfile();
            Assert.True(loadedProfile.Load(store));

            var scripts = Sys4ScriptProvider.Load(Table);
            var host = new StopAtFirstMenuPollHost();
            var vm = new VirtualMachine(
                scripts.RequireByName("SAVE.BIN"), Table, host,
                new VmOptions(MaxSteps: 500_000), scripts,
                sharedProfile: loadedProfile,
                nativeDatStore: store);

            Assert.Throws<MenuReadyException>(() => vm.Run());

            RgbaImage thumbnail = Assert.Single(host.SurfacePixels.Values);
            Assert.Equal((112, 84), (thumbnail.Width, thumbnail.Height));
            Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0xff }, thumbnail.Pixels[..4]);
            Assert.Contains(host.SurfaceStrings, item => item.Text == "2026");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "07");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "24");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "13");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "序章 封印、そして");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "base part-time job");
            Assert.Contains(host.SurfaceStrings, item => item.Text == "Lily");
            var objects = vm.Gfx.SnapshotVisibleObjects().ToDictionary(item => item.Handle);
            Assert.Equal((0, 96), (objects[0x13137].SrcX, objects[0x13138].SrcX));   // level 80
            Assert.Equal((96, 108), (objects[0x13146].SrcX, objects[0x13147].SrcX)); // growth 98
            Assert.Equal((108, 72), (objects[0x13155].SrcX, objects[0x13156].SrcX)); // personality 69
            Assert.Equal(3, host.TextureDraws.Count(
                item => item.Slot == 193 && item.Width == 17 && item.Height == 17));
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
        var store = new DirectoryNativeDatStore(root, Identity);
        var sharedProfile = new SharedProfile();
        Assert.True(sharedProfile.Load(store));
        var vm = new VirtualMachine(
            nativeScripts.RequireByName("SAVE.BIN"), Table, host,
            new VmOptions(MaxSteps: 1_000_000), provider,
            new StopAtInstalledResumeSink(),
            sharedProfile: sharedProfile,
            nativeDatStore: store);
        host.Vm = vm;
        vm.Globals[0x4] = unchecked((uint)sharedProfile.LoadInteger(0x5c3));
        vm.Globals[0x6241b] = 1;
        vm.Globals[0x696] = 0;

        Assert.Throws<InstalledResumeReachedException>(() => vm.Run());
    }

    [Fact]
    public void InstalledSave00ContinuesToRestoredGameplayPollWhenPresent()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Eushully", "姫狩りダンジョンマイスター", "SAVE");
        if (!File.Exists(Path.Combine(root, "SAVE00.DAT"))) return;

        var scripts = Sys4ScriptProvider.Load(Table);
        var store = new DirectoryNativeDatStore(root, Identity);
        NativeNumberedSaveState numbered = NativeNumberedSaveCodec.Decode(
            store.LoadNumberedFile(0)!.Document.Payload);
        var state = new InstalledContinuationState(numbered.Frames.Count);
        var host = new ContinueInstalledLoadHost(state);
        var sharedProfile = new SharedProfile();
        Assert.True(sharedProfile.Load(store));
        var vm = new VirtualMachine(
            scripts.RequireByName("SAVE.BIN"), Table, host,
            new VmOptions(MaxSteps: 1_000_000), scripts,
            new InstalledContinuationSink(state),
            sharedProfile: sharedProfile,
            nativeDatStore: store);
        host.Vm = vm;
        vm.Globals[0x4] = unchecked((uint)sharedProfile.LoadInteger(0x5c3));
        vm.Globals[0x6241b] = 1;
        vm.Globals[0x696] = 0;

        Exception? outcome = Record.Exception(() => vm.Run());
        Assert.True(outcome is InstalledGameplayPollReachedException,
            $"outcome={outcome?.GetType().Name ?? "<none>"}:{outcome?.Message}; " +
            $"halt={vm.HaltReason}; steps={vm.Steps}; waits={host.Waits}; " +
            $"movies={string.Join(",", host.ModalMovies.Select(item => item.Resource))}; " +
            $"saved={string.Join(", ", numbered.Frames.Select((frame, index) =>
                $"{index}:0x{frame.ScriptId:x}/resume={frame.ResumeIndex}/call={frame.CallTargetIndex}"))}; " +
            $"frames={string.Join(", ", state.Frames)}");
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

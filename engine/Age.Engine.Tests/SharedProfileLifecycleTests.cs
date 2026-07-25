using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class SharedProfileLifecycleTests
{
    private static readonly NativeSaveIdentity Identity =
        new(NativeSaveMagic.S4SD, 0x4a343234, "himegari-test", 3, 10, 0x42323234);
    private static readonly NativeSystemTime Timestamp =
        new(2026, 7, 5, 24, 13, 42, 17, 321);
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);

    [Fact]
    public void ShutdownFlushPersistsSharedCellsAndReadFlagsWithoutNumberedSave()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            var profile = new SharedProfile();
            profile.StoreInteger(0x123, 77);
            profile.StoreString(0x456, "終了保存");
            profile.ReadText.QueueMessage(0x22, 3, 5);
            profile.ReadText.CommitPending();
            var vm = NewVm(profile, store);

            vm.Run();
            SharedProfileShutdownFlushResult result = vm.FlushSharedProfileOnShutdown();

            Assert.Equal(SharedProfileShutdownFlushOutcome.Saved, result.Outcome);
            Assert.True(File.Exists(Path.Combine(root, DirectoryNativeDatStore.SharedFileName)));
            Assert.True(File.Exists(Path.Combine(root, DirectoryNativeDatStore.ReadTextFileName)));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(root),
                path =>
                {
                    string name = Path.GetFileName(path);
                    return name.Length == 10
                           && name.StartsWith("SAVE", StringComparison.Ordinal)
                           && char.IsAsciiDigit(name[4])
                           && char.IsAsciiDigit(name[5])
                           && name.EndsWith(".DAT", StringComparison.Ordinal);
                });

            var restarted = new SharedProfile();
            Assert.True(restarted.Load(store));
            Assert.Equal(77, restarted.LoadInteger(0x123));
            Assert.Equal("終了保存", restarted.LoadString(0x456));
            Assert.True(restarted.ReadText.IsMessageRead(0x22, 3));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NoSaveDatSuppressesOnlyShutdownFlush()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            var profile = new SharedProfile();
            profile.StoreInteger(1, 2);
            var vm = NewVm(profile, store, new VmOptions(NoSaveDat: true));

            SharedProfileShutdownFlushResult result = vm.FlushSharedProfileOnShutdown();

            Assert.Equal(SharedProfileShutdownFlushOutcome.Suppressed, result.Outcome);
            Assert.False(File.Exists(Path.Combine(root, DirectoryNativeDatStore.SharedFileName)));
            Assert.False(File.Exists(Path.Combine(root, DirectoryNativeDatStore.ReadTextFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NoSaveDatDoesNotSuppressSuccessfulNumberedSaveSharedFlush()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            var profile = new SharedProfile();
            profile.StoreInteger(1, 2);
            Script script = ScriptAssembler.Assemble(
                Table, "NUMBERED_WITH_NOSAVEDAT.BIN",
                [
                    (0x1ad, Array.Empty<Operand>()),
                    (0x19e, [new Operand(3, 0), new Operand(0, 0)]),
                    (0x2, Array.Empty<Operand>()),
                ],
                []);
            var vm = new VirtualMachine(
                script, Table, new RecordingHost(), new VmOptions(NoSaveDat: true),
                sharedProfile: profile, nativeDatStore: store);

            vm.Run();

            Assert.Equal(0, vm.Globals[0]);
            Assert.NotNull(store.LoadNumberedFile(0));
            Assert.NotNull(store.LoadShared());
            Assert.NotNull(store.LoadReadText());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepeatedShutdownFlushDoesNotRotateBackupsAgain()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            var profile = new SharedProfile();
            profile.StoreInteger(1, 2);
            var vm = NewVm(profile, store);

            SharedProfileShutdownFlushResult first = vm.FlushSharedProfileOnShutdown();
            SharedProfileShutdownFlushResult second = vm.FlushSharedProfileOnShutdown();

            Assert.Equal(SharedProfileShutdownFlushOutcome.Saved, first.Outcome);
            Assert.Equal(SharedProfileShutdownFlushOutcome.AlreadyHandled, second.Outcome);
            Assert.False(File.Exists(Path.Combine(root, DirectoryNativeDatStore.SharedBackupFileName)));
            Assert.False(File.Exists(Path.Combine(root, DirectoryNativeDatStore.ReadTextBackupFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShutdownFlushPreservesLoadedAccumulatedPlaytimeBaseline()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            var original = new SharedProfile();
            original.StoreInteger(1, 2);
            original.Save(store, Timestamp, 777);
            var loaded = new SharedProfile();
            Assert.True(loaded.Load(store));
            var vm = NewVm(loaded, store);

            SharedProfileShutdownFlushResult result = vm.FlushSharedProfileOnShutdown();

            Assert.Equal(SharedProfileShutdownFlushOutcome.Saved, result.Outcome);
            Assert.True(store.LoadShared()!.Metadata.AccumulatedPlaySeconds >= 777);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShutdownFlushReportsIoFailureWithoutThrowingOrRetrying()
    {
        string parent = NewTemporaryDirectory();
        string fileAsRoot = Path.Combine(parent, "not-a-directory");
        File.WriteAllText(fileAsRoot, "occupied");
        try
        {
            var store = new DirectoryNativeDatStore(fileAsRoot, Identity);
            var profile = new SharedProfile();
            profile.StoreInteger(1, 2);
            var vm = NewVm(profile, store);

            SharedProfileShutdownFlushResult first = vm.FlushSharedProfileOnShutdown();
            SharedProfileShutdownFlushResult second = vm.FlushSharedProfileOnShutdown();

            Assert.Equal(SharedProfileShutdownFlushOutcome.Failed, first.Outcome);
            Assert.NotNull(first.Error);
            Assert.NotEmpty(first.Error);
            Assert.Equal(SharedProfileShutdownFlushOutcome.AlreadyHandled, second.Outcome);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static VirtualMachine NewVm(
        SharedProfile profile,
        INativeDatStore store,
        VmOptions? options = null)
    {
        Script script = ScriptAssembler.Assemble(
            Table, "SHARED_SHUTDOWN.BIN", [(0x2, Array.Empty<Operand>())], []);
        return new VirtualMachine(
            script, Table, new RecordingHost(), options,
            sharedProfile: profile, nativeDatStore: store);
    }

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "age-shared-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

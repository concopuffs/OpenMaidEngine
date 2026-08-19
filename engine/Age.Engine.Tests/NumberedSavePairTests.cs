using System.Buffers.Binary;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public class NumberedSavePairTests
{
    private const int Immediate = 0;
    private const int GlobalInt = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly NativeSystemTime Timestamp =
        new(2026, 7, 5, 24, 13, 42, 17, 321);
    private static readonly NativeSaveIdentity Identity =
        new(NativeSaveMagic.S4SD, 0x4a343234, "numbered-test", 3, 10, 0x42323234,
            new NativeSaveBankDimensions(402459, 1, 789, 1, 1, 1));

    [Fact]
    public void ThumbnailCodecWritesNativeBottomUpBmpAndRoundTrips()
    {
        var image = new RgbaImage(2, 2,
        [
            255, 0, 0, 255,       0, 255, 0, 255,
            0, 0, 255, 255,       255, 255, 255, 255,
        ]);

        byte[] encoded = NumberedThumbnailCodec.Encode(image);

        Assert.Equal((byte)'B', encoded[0]);
        Assert.Equal((byte)'M', encoded[1]);
        Assert.Equal(56u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(2)));
        Assert.Equal(70, encoded.Length);
        Assert.Equal(54u, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(10)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(18)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(22)));
        Assert.Equal((ushort)24, BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(28)));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 255 }, encoded[54..60]);

        RgbaImage decoded = NumberedThumbnailCodec.Decode(encoded);
        Assert.Equal(image.Width, decoded.Width);
        Assert.Equal(image.Height, decoded.Height);
        Assert.Equal(image.Pixels, decoded.Pixels);
    }

    [Fact]
    public void InstalledHimegariThumbnailMatchesNativeBmpDialectWhenPresent()
    {
        string eushullyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eushully");
        if (!Directory.Exists(eushullyRoot)) return;

        string? dataPath = Directory.EnumerateFiles(
                eushullyRoot, "SAVE00.DAT", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    byte[] header = new byte[NativeSaveContainerCodec.HeaderSize];
                    stream.ReadExactly(header);
                    NativeSaveMetadata metadata = NativeSaveContainerCodec.ReadMetadata(header);
                    return metadata.CompatibilityId == 0x42323234
                        && metadata.SaveVersion1 == 3 && metadata.SaveVersion2 == 10;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException
                                            or UnauthorizedAccessException)
                {
                    return false;
                }
            });
        if (dataPath == null) return;
        string thumbnailPath = Path.ChangeExtension(dataPath, ".STH");
        if (!File.Exists(thumbnailPath)) return;

        byte[] native = File.ReadAllBytes(thumbnailPath);
        RgbaImage decoded = NumberedThumbnailCodec.Decode(native);

        Assert.Equal(112, decoded.Width);
        Assert.Equal(84, decoded.Height);
        Assert.Equal(28278, native.Length);
        Assert.Equal(28264u, BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(2)));
        Assert.Equal(54u, BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(10)));
        Assert.Equal(native.Length, NumberedThumbnailCodec.Encode(decoded).Length);
    }

    [Fact]
    public void NumberedMetadataUsesItsOwnIdentityAndDoesNotDecodePayload()
    {
        string root = NewTempRoot();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            store.SaveNumbered(4, [1, 2, 3, 4], Timestamp, 54321);
            string path = Path.Combine(root, "SAVE04.DAT");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.SetLength(NativeSaveContainerCodec.HeaderSize);

            NativeSaveMetadata metadata = store.QueryNumberedMetadata(4)!;

            Assert.Equal(0x42323234u, metadata.CompatibilityId);
            Assert.Equal(Timestamp, metadata.Timestamp);
            Assert.Equal(54321u, metadata.AccumulatedPlaySeconds);
            Assert.Throws<InvalidDataException>(() => store.LoadNumbered(4));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PairOperationsAttemptBothMembersAndPreserveNativeStatusPrecedence()
    {
        string root = NewTempRoot();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);

            store.SaveNumbered(1, [1, 2, 3, 4], Timestamp, 1);
            Assert.Equal(2, store.CopyNumberedPair(1, 2));
            Assert.NotNull(store.LoadNumbered(2));
            Assert.Null(store.LoadNumberedThumbnail(2));

            store.SaveNumberedThumbnail(3, [7, 8, 9]);
            Assert.Equal(1, store.CopyNumberedPair(3, 4));
            Assert.Null(store.LoadNumbered(4));
            Assert.Equal(new byte[] { 7, 8, 9 }, store.LoadNumberedThumbnail(4));

            Assert.Equal(2, store.DeleteNumberedPair(1));
            Assert.Equal(1, store.DeleteNumberedPair(3));
            Assert.Equal(2, store.DeleteNumberedPair(99));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VmMetadataThumbnailCopyAndDeleteOpcodesUseTheNativePairStore()
    {
        string root = NewTempRoot();
        try
        {
            var store = new DirectoryNativeDatStore(root, Identity);
            store.SaveNumbered(4, [1, 2, 3, 4], Timestamp, 54321);
            var host = new RecordingHost();
            host.SurfacePixels[7] = new RgbaImage(1, 1, [10, 20, 30, 255]);
            Script script = ScriptAssembler.Assemble(Table, "NUMBERED_SAVE_OPS",
            [
                (0x1a0, [
                    G(100), I(4), G(101), G(102), G(103), G(104), G(105), G(106), G(107),
                ]),
                (0x1ae, [G(110), I(4), I(7)]),
                (0x1af, [G(111), I(4), I(8)]),
                (0x1ac, [G(112), I(4), I(5)]),
                (0x1ab, [G(113), I(5)]),
                (0x2, []),
            ], []);
            var vm = new VirtualMachine(script, Table, host, nativeDatStore: store);

            vm.Run();

            Assert.Equal(0, vm.Globals[100]);
            Assert.Equal(2026, vm.Globals[101]);
            Assert.Equal(7, vm.Globals[102]);
            Assert.Equal(24, vm.Globals[103]);
            Assert.Equal(13, vm.Globals[104]);
            Assert.Equal(42, vm.Globals[105]);
            Assert.Equal(17, vm.Globals[106]);
            Assert.Equal(54321, vm.Globals[107]);
            Assert.Equal(0, vm.Globals[110]);
            Assert.Equal(0, vm.Globals[111]);
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, host.SurfacePixels[8].Pixels);
            Assert.Equal(0, vm.Globals[112]);
            Assert.Equal(0, vm.Globals[113]);
            Assert.Null(store.LoadNumbered(5));
            Assert.Null(store.LoadNumberedThumbnail(5));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveResumeMarkerSurvivesNestedReturnUntilItsOwningFrameUnwinds()
    {
        int callScript = Table.ByLabel("call-script")!.Value;
        Script child = ScriptAssembler.Assemble(Table, "CHILD",
        [
            (0x1a8, []),
            (0x2, []),
        ], []);
        Script root = ScriptAssembler.Assemble(Table, "ROOT",
        [
            (0x1ad, []),
            (callScript, [I(7)]),
            (0x2, []),
        ], []);
        var provider = new MapProvider(new Dictionary<long, Script> { [7] = child });
        var host = new MarkerObservingHost();
        var vm = new VirtualMachine(root, Table, host, provider: provider);
        host.Vm = vm;

        vm.Run();

        Assert.NotEmpty(host.ObservedDepths);
        Assert.All(host.ObservedDepths, depth => Assert.Equal(0, depth));
        Assert.Null(vm.SaveResumeFrameDepth);
    }

    [Fact]
    public void ChildMarkerIsClearedWhenTheChildFrameUnwinds()
    {
        int callScript = Table.ByLabel("call-script")!.Value;
        Script child = ScriptAssembler.Assemble(Table, "CHILD",
        [
            (0x1ad, []),
            (0x2, []),
        ], []);
        Script root = ScriptAssembler.Assemble(Table, "ROOT",
        [
            (callScript, [I(7)]),
            (0x1a8, []),
            (0x2, []),
        ], []);
        var provider = new MapProvider(new Dictionary<long, Script> { [7] = child });
        var host = new MarkerObservingHost();
        var vm = new VirtualMachine(root, Table, host, provider: provider);
        host.Vm = vm;

        vm.Run();

        Assert.Contains(1, host.ObservedDepths);
        int marked = host.ObservedDepths.FindLastIndex(depth => depth == 1);
        Assert.Contains(host.ObservedDepths.Skip(marked + 1), depth => depth == null);
        Assert.Null(vm.SaveResumeFrameDepth);
    }

    private static Operand I(long value) => new(Immediate, value);
    private static Operand G(long value) => new(GlobalInt, value);

    private static string NewTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "age-numbered-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class MarkerObservingHost : RecordingHost
    {
        public VirtualMachine Vm { get; set; } = null!;
        public List<int?> ObservedDepths { get; } = new();
        public override void FrameYield() => ObservedDepths.Add(Vm.SaveResumeFrameDepth);
    }
}

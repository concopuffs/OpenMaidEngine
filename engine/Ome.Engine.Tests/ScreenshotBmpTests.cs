using System.Buffers.Binary;
using Ome.Engine.Model;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>AGE's screenshot BMPs (engine_save_screenshot_bmp@0x407ce0), their folder setting and op 0x10f's F1 gate
/// (docs/engine-re.md, "Native menu bar").</summary>
public sealed class ScreenshotBmpTests : IDisposable
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-screenshot-" + Guid.NewGuid().ToString("N"));

    public ScreenshotBmpTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static RgbaImage Frame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4] = (byte)(i * 7);
            pixels[i * 4 + 1] = (byte)(i * 13);
            pixels[i * 4 + 2] = (byte)(i * 29);
            pixels[i * 4 + 3] = 255;
        }
        return new RgbaImage(width, height, pixels);
    }

    [Fact]
    public void SavesTheFirstMissingNameOnTheDesktopWithOp0x1aesWriter()
    {
        string desktop = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(desktop);
        File.WriteAllBytes(Path.Combine(desktop, "CG000000.BMP"), [1]);
        File.WriteAllBytes(Path.Combine(desktop, "CG000001.BMP"), [1]);
        var store = new ScreenshotBmpStore(() => desktop);
        RgbaImage frame = Frame(5, 3);

        string? first = store.Save(frame, "");
        string? second = store.Save(frame, "");

        Assert.Equal(Path.Combine(desktop, "CG000002.BMP"), first);
        Assert.Equal(Path.Combine(desktop, "CG000003.BMP"), second);
        Assert.Equal(4, store.Counter);   // every probe advances the counter
        byte[] written = File.ReadAllBytes(first!);
        Assert.Equal(NumberedThumbnailCodec.Encode(frame), written);
        // Native's 800x600 BMP: 1,440,054 bytes, bfSize 1,440,040 (the 14-byte file header left out), offset 54.
        int rowStride = (5 * 3 + 3) & ~3;
        Assert.Equal(54 + rowStride * 3, written.Length);
        Assert.Equal((uint)(40 + rowStride * 3), BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(2)));
        Assert.Equal(24, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(28)));
    }

    [Fact]
    public void SaveBmpPathReplacesTheDesktopAndTheCounterIsShared()
    {
        string desktop = Path.Combine(_root, "Desktop");
        string chosen = Path.Combine(_root, "Shots");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(chosen);
        var store = new ScreenshotBmpStore(() => desktop);

        Assert.Equal(Path.Combine(desktop, "CG000000.BMP"), store.Save(Frame(2, 2), ""));
        Assert.Equal(Path.Combine(chosen, "CG000001.BMP"), store.Save(Frame(2, 2), chosen));
        Assert.Equal(chosen, store.TargetDirectory(chosen));
        Assert.Equal(desktop, store.TargetDirectory(""));
    }

    [Fact]
    public void AFailedWriteReturnsNullAndTheCounterStopsAtItsBound()
    {
        var store = new ScreenshotBmpStore(() => Path.Combine(_root, "missing", "folder"));
        Assert.Null(store.Save(Frame(1, 1), ""));
        Assert.Equal(1, store.Counter);

        string desktop = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(desktop);
        var nearlyFull = new ScreenshotBmpStore(() => desktop, ScreenshotBmpStore.NameLimit - 2);
        Assert.Equal(Path.Combine(desktop, "CG999998.BMP"), nearlyFull.Save(Frame(1, 1), ""));
        // Native increments before it writes and gives up once the counter reaches 1,000,000, so CG999999 is never
        // written.
        Assert.Null(nearlyFull.Save(Frame(1, 1), ""));
        Assert.False(File.Exists(Path.Combine(desktop, "CG999999.BMP")));
    }

    [Fact]
    public void SaveBmpPathIsAStringSettingInTheSystemSection()
    {
        string path = Path.Combine(_root, Sys4RegIniStore.FileName);
        var store = new Sys4RegIniStore(path, 0);
        Assert.Equal("", store.LoadString("system", "SaveBMPPath", "fallback"));   // AGE binds it with ""

        store.SaveString("system", "SaveBMPPath", @"C:\Users\someone\Pictures\姫狩り");

        var reloaded = new Sys4RegIniStore(path, 0);
        Assert.Equal(@"C:\Users\someone\Pictures\姫狩り", reloaded.LoadString("system", "SaveBMPPath", ""));
        string text = File.ReadAllText(path, System.Text.CodePagesEncodingProvider.Instance.GetEncoding(932)!);
        Assert.Contains("[system]\r\nSaveBMPPath=C:\\Users\\someone\\Pictures\\姫狩り\r\nUseMMX=1", text);
        Assert.Throws<ArgumentException>(() => store.SaveString("system", "SaveBMPPath", "a\nb"));
    }

    [Fact]
    public void Op0x10fStoresTheSystemKeyMaskAndSceneResetClearsIt()
    {
        Script system4 = ScriptAssembler.Assemble(Table, "SYSTEM4.BIN", [(0x2, Array.Empty<Operand>())], []);
        Script scene = ScriptAssembler.Assemble(Table, "SC0000.BIN",
        [
            (0x10f, [new Operand(0, 0x201)]),
            (0x2, Array.Empty<Operand>()),
        ], []);
        var vm = new VirtualMachine(scene, Table, new RecordingHost());
        Assert.Equal(0, vm.SystemKeyDisableMask);
        vm.Run();
        Assert.Equal(0x201, vm.SystemKeyDisableMask);

        Script reloading = ScriptAssembler.Assemble(Table, "SC0000.BIN",
        [
            (0x10f, [new Operand(0, 1)]),
            (0x9, Array.Empty<Operand>()),
        ], []);
        var provider = new MapProvider(new Dictionary<long, Script> { [0] = system4 });
        var reset = new VirtualMachine(reloading, Table, new RecordingHost(), provider: provider);
        reset.Run();
        Assert.Equal(0, reset.SystemKeyDisableMask);
    }
}

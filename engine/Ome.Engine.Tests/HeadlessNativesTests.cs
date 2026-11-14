using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// The natives run without Godot (docs/superpowers/specs/2026-11-12-engine-layering-design.md, acceptance 5): over
/// devices that do nothing, with no game files and no glyphs. The helpers build the input and waits native over the
/// other natives for the tests that drive it.
/// </summary>
public class HeadlessNativesTests
{
    private static readonly Sys4LogicalCanvas Canvas = new(16, 12);

    /// <summary>Shows nothing; keeps the names of the calls it was given, in order.</summary>
    internal sealed class RecordingCursor : ICursorDevice
    {
        public List<string> Calls { get; } = [];
        private void Note(string name) { lock (Calls) Calls.Add(name); }

        public void SetCursor(RgbaImage image, int hotspotX, int hotspotY) => Note(nameof(SetCursor));
        public void ClearCursor() => Note(nameof(ClearCursor));
        public void WarpCursor(int x, int y) => Note(nameof(WarpCursor));
    }

    private sealed class NoGlyphs : IIdentifiedGlyphMaskRasterizer
    {
        public GlyphRasterizerBackendInfo BackendInfo { get; } =
            new("none", "No glyphs", GlyphRasterPolicy.NativeCp932Gray4, false, "draws nothing");
        public GlyphMask Rasterize(GlyphRasterRequest request) => new(0, 0, 0, 0, 0, 0, 0, 0, 0, [], 0);
    }

    private sealed class NoAssets : IAssetStore
    {
        public Stream Open(AssetEntry entry) => throw new FileNotFoundException(entry.Name);
        public byte[] ReadAll(AssetEntry entry) => throw new FileNotFoundException(entry.Name);
    }

    /// <summary>A catalog with no sounds, over a store with no files.</summary>
    internal static ResourceMap EmptyResources()
        => new(Sys4AssetCatalog.Parse(Sys4StartupSettingsTests.BuildCatalog()), new NoAssets());

    /// <summary>The natives a headless run uses, as composition builds them for a front end.</summary>
    internal sealed record NativeSet(
        SkipAndAuto Skip, Audio Audio, EngineTick Tick, Graphics Graphics, Movies Movies, SavesAndSettings Saves,
        ShellServices Shell, TextManager Text, InputAndWaits Waits)
    {
        /// <summary>A VM over these natives, as composition builds the front end's.</summary>
        public VirtualMachine Vm(Script script, OpcodeTable table, VmOptions? options = null,
                                 IScriptProvider? provider = null)
            => new(script, table, Waits, options, provider, saves: Saves, shell: Shell, skipAndAuto: Skip,
                   audio: Audio, tick: Tick, graphics: Graphics, movies: Movies, text: Text);
    }

    /// <summary>The natives over devices that do nothing, any of which a test may give. Without an
    /// <see cref="AudioRig"/> the audio native is a silent one. A presentation request returns before a frame takes it
    /// unless <paramref name="synchronizeExplicitPresentation"/>, since a test has no frames unless it runs them.</summary>
    internal static NativeSet Natives(
        PageLocatorState locator, FrameClock clock,
        SkipAndAuto? skipAndAuto = null, AudioRig? audio = null, Graphics? graphics = null, Movies? movies = null,
        SavesAndSettings? saves = null, ShellServices? shell = null, TextManager? text = null,
        ICursorDevice? cursor = null, bool synchronizeExplicitPresentation = false)
    {
        SkipAndAuto skip = audio?.Skip ?? skipAndAuto ?? SkipAndAuto.WithSystemClock();
        Audio native = audio?.Audio ?? Audio.Silent(skip);
        EngineTick tick = audio?.Tick ?? new EngineTick(native);
        ResourceMap resources = EmptyResources();
        Graphics surfaces = graphics ?? new Graphics(resources, clock, Canvas);
        movies ??= Movies.Headless(surfaces);
        saves ??= new SavesAndSettings();
        shell ??= ShellServices.Headless();
        text ??= new TextManager(surfaces, skip, clock, new TextDevices(new NoGlyphs(), GlyphCoverageAlpha.Sixteenths),
                                 RubyLayoutRules.FixedOffsets, settings: saves.Settings, locator: locator);
        var waits = new InputAndWaits(skip, native, tick, surfaces, movies, shell, text, clock, locator,
                                      synchronizeExplicitPresentation: synchronizeExplicitPresentation,
                                      cursors: resources, cursor: cursor);
        return new NativeSet(skip, native, tick, surfaces, movies, saves, shell, text, waits);
    }

    /// <summary>The input and waits native over the natives it runs with (<see cref="Natives"/>).</summary>
    internal static InputAndWaits Waits(
        PageLocatorState locator, FrameClock clock,
        SkipAndAuto? skipAndAuto = null, AudioRig? audio = null, Graphics? graphics = null, Movies? movies = null,
        SavesAndSettings? saves = null, ShellServices? shell = null, TextManager? text = null,
        ICursorDevice? cursor = null, bool synchronizeExplicitPresentation = false)
        => Natives(locator, clock, skipAndAuto, audio, graphics, movies, saves, shell, text, cursor,
                   synchronizeExplicitPresentation).Waits;

    [Fact]
    public void AHeadlessRunProducesFramesWithTheReferenceRenderer()
    {
        // A script draws a red 4x3 surface at (5, 6) over a blue-grey background and waits for a click. The VM runs on an
        // engine thread over the natives a front end gets; this thread is the front end: each frame it takes a frame
        // turn and draws the description the turn gives with the reference renderer, and it clicks once the drawn frame
        // shows the surface.
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        static Operand I(long value) => new(0, value);
        Script script = ScriptAssembler.Assemble(table, "HEADLESS", new List<(int, Operand[])>
        {
            (0x25a, [I(0x204060)]),                                        // set-display-background-color
            (0x1f8, [I(1), I(4), I(3), I(0)]),                              // create-texture 1, 4x3
            (0x20b, [I(1), I(0), I(0), I(4), I(3), I(255), I(0xff0000)]),   // fill-surface-rect, opaque red
            (0x1fb, [I(0x10), I(1), I(0), I(0), I(4), I(3), I(5), I(6)]),   // draw-texture: object 0x10 at (5, 6)
            (0x72, [I(0)]),                                                 // wait-for-input
            (0x2, []),
        }, []);
        var clock = new FrameClock();
        using var locator = new PageLocatorState("HEADLESS", null);
        NativeSet natives = Natives(locator, clock, synchronizeExplicitPresentation: true);
        VirtualMachine vm = natives.Vm(script, table);
        InputAndWaits waits = natives.Waits;
        var renderer = new ReferenceRenderer(Canvas);

        Task engine = Task.Run(() => waits.RunEngine(() => vm.Run()));
        int drawn = 0;
        bool clicked = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!engine.IsCompleted && DateTime.UtcNow < deadline)
        {
            clock.Advance(0.016);
            FrameDescription? frame = waits.RunFrameTurn(null, null, null, 4, out _);
            if (frame != null)
            {
                renderer.Draw(frame);
                drawn++;
            }
            if (!clicked && waits.IsWaiting && drawn > 0 && Pixel(renderer, 5, 6)[0] == 255)
            {
                waits.SignalInput();
                clicked = true;
            }
            Thread.Sleep(1);
        }

        Assert.True(engine.Wait(TimeSpan.FromSeconds(10)), "the engine thread did not end");
        Assert.True(clicked, "no drawn frame showed the surface at the wait");
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, waits.Pages);
        byte[] background = [0x20, 0x40, 0x60, 255], red = [255, 0, 0, 255];
        Assert.Equal(background, Pixel(renderer, 0, 0));
        Assert.Equal(red, Pixel(renderer, 5, 6));
        Assert.Equal(red, Pixel(renderer, 8, 8));
        Assert.Equal(background, Pixel(renderer, 9, 6));
    }

    [Fact]
    public void TheCursorReachesTheDeviceInTheOrderSent()
    {
        var cursor = new RecordingCursor();
        using var locator = new PageLocatorState("SC0000", null);
        IInputAndWaits waits = Waits(locator, new FrameClock(), cursor: cursor);

        waits.WarpCursor(3, 4);
        waits.ClearCursorResource();
        waits.SetCursorResource(0x1234);   // no CUR record in the catalog: nothing is shown
        waits.WarpCursor(5, 6);

        Assert.Equal(["WarpCursor", "ClearCursor", "WarpCursor"], cursor.Calls);
    }

    [Fact]
    public void AScenesResetIsAPageNotice()
    {
        using var locator = new PageLocatorState("SC0000", null);
        InputAndWaits waits = Waits(locator, new FrameClock());
        int before = waits.PageNotices;

        ((IInputAndWaits)waits).ResetSceneContext();

        Assert.Equal(before + 1, waits.PageNotices);
        Assert.Equal(0, waits.Pages);
    }

    [Fact]
    public void TheDiagnosticSnapshotReadsFastForwardFromTheSkipAndAutoNative()
    {
        using var locator = new PageLocatorState("SC0000", null);
        SkipAndAuto skipAndAuto = SkipAndAuto.WithSystemClock();
        InputAndWaits waits = Waits(locator, new FrameClock(), skipAndAuto);

        Assert.False(waits.CaptureDiagnosticSnapshot().IsMessageSkipActive);
        skipAndAuto.SetPhysicalFastForward(true);
        Assert.True(waits.CaptureDiagnosticSnapshot().IsMessageSkipActive);
        skipAndAuto.SetPhysicalFastForward(false);
        Assert.False(waits.CaptureDiagnosticSnapshot().IsMessageSkipActive);
    }

    [Fact]
    public void TheReferenceRendererDrawsTheLoweredFrame()
    {
        var clock = new FrameClock();
        var graphics = new Graphics(EmptyResources(), clock, Canvas);
        GfxState gfx = graphics.State;
        gfx.SetDisplayBackgroundColor(0x204060);
        graphics.CreateSurface(1, 4, 3);
        graphics.FillSurfaceRect(new SurfaceRectFill(1, 0, 0, 4, 3, 255, 0xff0000));
        graphics.DrawTexture(0x10, 1, 0, 0, 4, 3, 5, 6);   // object 0x10 draws surface 1's 4x3 at (5, 6)

        var frame = graphics.LowerFrame(clock.NowMs);
        var compositor = new ReferenceRenderer(Canvas);
        compositor.Draw(frame);

        Assert.Equal(0x204060, frame.BackgroundRgb);
        Assert.Single(frame.Items);
        Assert.Equal(Canvas.RgbaByteCount, compositor.Pixels.Length);
        byte[] background = [0x20, 0x40, 0x60, 255], red = [255, 0, 0, 255];
        Assert.Equal(background, Pixel(compositor, 0, 0));
        Assert.Equal(red, Pixel(compositor, 5, 6));
        Assert.Equal(red, Pixel(compositor, 8, 8));
        Assert.Equal(background, Pixel(compositor, 9, 6));
        Assert.Equal(background, Pixel(compositor, 5, 9));
    }

    internal static byte[] Pixel(ReferenceRenderer compositor, int x, int y)
        => compositor.Pixels.AsSpan((y * compositor.Width + x) * 4, 4).ToArray();
}

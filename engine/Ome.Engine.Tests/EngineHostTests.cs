using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Xunit;

/// <summary>
/// The lifted host (docs/superpowers/specs/2026-11-12-engine-layering-design.md, milestone 4) runs without Godot: over
/// a front end that does nothing, with no game files and no glyphs.
/// </summary>
public class EngineHostTests
{
    private static readonly Sys4LogicalCanvas Canvas = new(16, 12);

    /// <summary>Does nothing; keeps the names of the calls it was given, in order.</summary>
    private sealed class NothingFrontEnd : IFrontEnd
    {
        public List<string> Calls { get; } = [];
        private void Note(string name) { lock (Calls) Calls.Add(name); }

        public bool TryPlayMovie(byte[] mpegBytes, string assetName, long playbackId, long resourceId, int assetId,
                                 long movieFlags, long initialPositionMs, long startDelayMs,
                                 long? presentationDurationMs, out long? stopTimeMs)
        {
            Note(nameof(TryPlayMovie));
            stopTimeMs = null;
            return false;
        }
        public void QueueMoviePlaybackRate(long playbackId, double rate) => Note(nameof(QueueMoviePlaybackRate));
        public void StopMovie(long playbackId) => Note(nameof(StopMovie));
        public void PageBreak() => Note(nameof(PageBreak));
        public void ClearPage() => Note(nameof(ClearPage));
        public void SetAgeCursor(byte[] rgba, int width, int height, int hotspotX, int hotspotY) => Note(nameof(SetAgeCursor));
        public void ClearAgeCursor() => Note(nameof(ClearAgeCursor));
        public void WarpAgeCursor(int virtualX, int virtualY) => Note(nameof(WarpAgeCursor));
        public void ShowAgeDiagnostic(DiagnosticMessage message) => Note(nameof(ShowAgeDiagnostic));
        public void ShowAgeScriptError(DiagnosticMessage message) => Note(nameof(ShowAgeScriptError));
        public void ShowAgeFullwidthTextEditor(string initialText) => Note(nameof(ShowAgeFullwidthTextEditor));
        public void RequestScreenModeFromScript(bool windowed) => Note(nameof(RequestScreenModeFromScript));
        public void Print(string message) { }
        public void Warn(string message) { }
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

    /// <summary>The lifted host over a front end, with the natives it runs with. Without an <see cref="AudioRig"/> the
    /// audio native is a silent one.</summary>
    internal static EngineHost Host(IFrontEnd frontEnd, PageLocatorState locator, FrameClock clock,
                                    SkipAndAuto? skipAndAuto = null, AudioRig? audio = null)
    {
        SkipAndAuto skip = audio?.Skip ?? skipAndAuto ?? SkipAndAuto.WithSystemClock();
        Audio native = audio?.Audio ?? Audio.Silent(skip);
        return new(frontEnd, skip, native, audio?.Tick ?? new EngineTick(native), EmptyResources(),
                   "SC0000", clock, locator, Canvas, new NoGlyphs(), GlyphCoverageAlpha.Sixteenths,
                   synchronizeExplicitPresentation: false)
        {
            RubyRules = RubyLayoutRules.FixedOffsets,
        };
    }

    internal static IFrontEnd NothingAtAll() => new NothingFrontEnd();

    [Fact]
    public void TheHostsCommandsReachTheFrontEndInTheOrderSent()
    {
        var frontEnd = new NothingFrontEnd();
        using var locator = new PageLocatorState("SC0000", null);
        EngineHost host = Host(frontEnd, locator, new FrameClock());

        host.WarpCursor(3, 4);
        host.ClearCursorResource();
        host.RequestScreenMode(windowed: true);
        host.ResetSceneContext();

        Assert.Equal(["WarpAgeCursor", "ClearAgeCursor", "RequestScreenModeFromScript", "ClearPage"], frontEnd.Calls);
    }

    [Fact]
    public void TheHostReadsFastForwardFromTheSkipAndAutoNative()
    {
        var frontEnd = new NothingFrontEnd();
        using var locator = new PageLocatorState("SC0000", null);
        SkipAndAuto skipAndAuto = SkipAndAuto.WithSystemClock();
        EngineHost host = Host(frontEnd, locator, new FrameClock(), skipAndAuto);

        Assert.False(host.CaptureDiagnosticSnapshot().IsMessageSkipActive);
        skipAndAuto.SetPhysicalFastForward(true);
        Assert.True(host.CaptureDiagnosticSnapshot().IsMessageSkipActive);
        skipAndAuto.SetPhysicalFastForward(false);
        Assert.False(host.CaptureDiagnosticSnapshot().IsMessageSkipActive);
        Assert.Empty(frontEnd.Calls);
    }

    [Fact]
    public void TheSoftwareCompositorRendersAFrame()
    {
        var frontEnd = new NothingFrontEnd();
        using var locator = new PageLocatorState("SC0000", null);
        var clock = new FrameClock();
        EngineHost host = Host(frontEnd, locator, clock);
        var gfx = new GfxState();
        gfx.SetDisplayBackgroundColor(0x204060);
        host.CreateTexture(1, 4, 3);
        host.FillSurfaceRect(new SurfaceRectFill(1, 0, 0, 4, 3, 255, 0xff0000));
        gfx.BindDraw(0x10, 1, 0, 0, 4, 3, 5, 6);   // object 0x10 draws surface 1's 4x3 at (5, 6)

        var compositor = new SoftwareCompositor(host, gfx, clock, Canvas);
        compositor.Compose(sampledVisible: null, preserveExistingPixels: false, page: 0);

        Assert.Equal(Canvas.RgbaByteCount, compositor.Pixels.Length);
        byte[] background = [0x20, 0x40, 0x60, 255], red = [255, 0, 0, 255];
        Assert.Equal(background, Pixel(compositor, 0, 0));
        Assert.Equal(red, Pixel(compositor, 5, 6));
        Assert.Equal(red, Pixel(compositor, 8, 8));
        Assert.Equal(background, Pixel(compositor, 9, 6));
        Assert.Equal(background, Pixel(compositor, 5, 9));
        Assert.Empty(frontEnd.Calls);
    }

    private static byte[] Pixel(SoftwareCompositor compositor, int x, int y)
        => compositor.Pixels.AsSpan((y * compositor.Width + x) * 4, 4).ToArray();
}

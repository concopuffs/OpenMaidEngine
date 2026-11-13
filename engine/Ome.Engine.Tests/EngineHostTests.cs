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

        public void PlayBgm(byte[] oggBytes, string assetName) => Note(nameof(PlayBgm));
        public void RestartBgm(byte[] oggBytes, string assetName, int startMode) => Note(nameof(RestartBgm));
        public void StopBgm() => Note(nameof(StopBgm));
        public void FadeBgm(int targetBasisPoints, int steps, double realDurationSeconds) => Note(nameof(FadeBgm));
        public void CompleteBgmFade() => Note(nameof(CompleteBgmFade));
        public int QueueVoicePlayback() { Note(nameof(QueueVoicePlayback)); return 1; }
        public void PlayVoice(byte[] oggBytes, string assetName, int generation, bool duckBgm, int duckTargetPercent,
                              bool muted) => Note(nameof(PlayVoice));
        public void StopVoiceForMessageSkip() => Note(nameof(StopVoiceForMessageSkip));
        public void ArmVoiceBgmDuckRelease() => Note(nameof(ArmVoiceBgmDuckRelease));
        public bool IsVoicePlaybackActive => false;
        public void LoadSoundEffect(byte[] wavBytes, string assetName, int channel) => Note(nameof(LoadSoundEffect));
        public void StartSoundEffect(int channel, int startMode) => Note(nameof(StartSoundEffect));
        public void ScheduleSoundEffectStart(int channel, int startMode, double realDelaySeconds)
            => Note(nameof(ScheduleSoundEffectStart));
        public void CancelScheduledSoundEffectStarts() => Note(nameof(CancelScheduledSoundEffectStarts));
        public void ReleaseSoundEffect(int channel) => Note(nameof(ReleaseSoundEffect));
        public bool IsSoundChannelPlaying(int channel) { Note(nameof(IsSoundChannelPlaying)); return false; }
        public void ApplyAudioVolume(int category, int basisPoints) => Note(nameof(ApplyAudioVolume));
        public void ApplyAudioRouteEnabled(int category, bool enabled) => Note(nameof(ApplyAudioRouteEnabled));
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

    private static EngineHost Host(IFrontEnd frontEnd, PageLocatorState locator, FrameClock clock,
                                   SkipAndAuto? skipAndAuto = null)
        => new(frontEnd, skipAndAuto ?? SkipAndAuto.WithSystemClock(), new ResourceMap(Sys4AssetCatalog.Parse(Sys4StartupSettingsTests.BuildCatalog()), new NoAssets()),
               "SC0000", clock, locator, Canvas, new NoGlyphs(), GlyphCoverageAlpha.Sixteenths,
               synchronizeExplicitPresentation: false)
        {
            RubyRules = RubyLayoutRules.FixedOffsets,
        };

    [Fact]
    public void TheHostsCommandsReachTheFrontEndInTheOrderSent()
    {
        var frontEnd = new NothingFrontEnd();
        using var locator = new PageLocatorState("SC0000", null);
        EngineHost host = Host(frontEnd, locator, new FrameClock());

        host.StopBgm();
        host.ApplyAudioVolume(0, 5000);
        host.ApplyAudioRouteEnabled(1, false);
        host.FadeBgm(0, 500);
        host.CompleteBgmFade();
        host.StopVoice();
        Assert.False(host.IsSoundChannelPlaying(3));

        Assert.Equal(
            ["StopBgm", "ApplyAudioVolume", "ApplyAudioRouteEnabled", "FadeBgm", "CompleteBgmFade",
             "StopVoiceForMessageSkip", "IsSoundChannelPlaying"],
            frontEnd.Calls);
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
        Assert.Empty(frontEnd.Calls);   // no voice was held back, so ending fast-forward plays nothing
    }

    [Fact]
    public void AnAssetTheCatalogLacksSendsNothing()
    {
        var frontEnd = new NothingFrontEnd();
        using var locator = new PageLocatorState("SC0000", null);
        EngineHost host = Host(frontEnd, locator, new FrameClock());

        host.PlayBgm(0x1234);
        host.LoadSoundEffect(0x1234, 2);
        host.StartSoundEffect(2);

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

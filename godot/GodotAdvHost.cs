using System;
using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;

public sealed partial class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _rootScene;
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly Age.Engine.Hosting.FrameClock _clock;
    private readonly GodotTimelineLog? _timeline;
    private readonly PageLocatorState _locator;
    public GodotAdvHost(Main main, ResourceMap res, string scene, Age.Engine.Hosting.FrameClock clock,
                        PageLocatorState locator, Sys4LogicalCanvas logicalCanvas,
                        IGlyphMaskRasterizer surfaceTextRasterizer,
                        GodotTimelineLog? timeline = null,
                        bool synchronizeExplicitPresentation = true,
                        int surfaceTextMaskCacheCapacity = 2048)
    {
        _main = main; _res = res; _rootScene = scene; _clock = clock;
        _locator = locator; _timeline = timeline;
        _screenWidth = logicalCanvas.Width;
        _screenHeight = logicalCanvas.Height;
        _slotDims[0] = (_screenWidth, _screenHeight);
        _synchronizeExplicitPresentation = synchronizeExplicitPresentation;
        ArgumentNullException.ThrowIfNull(surfaceTextRasterizer);
        _surfaceTextBackendInfo =
            (surfaceTextRasterizer as IIdentifiedGlyphMaskRasterizer)?.BackendInfo
            ?? throw new ArgumentException(
                "Gameplay glyph rasterizers must identify their policy.",
                nameof(surfaceTextRasterizer));
        _surfaceTextMaskCache = new CachedGlyphMaskRasterizer(
            surfaceTextRasterizer, capacity: surfaceTextMaskCacheCapacity);
        _surfaceTextPixelRenderer =
            new ImmediateSurfaceTextRenderer(
                _surfaceTextMaskCache, _surfaceTextBackendInfo.Policy);
        _retainedGlyphLayoutEngine =
            new RetainedGlyphLayoutEngine(_surfaceTextMaskCache);
    }

    public Sys4LogicalCanvas LogicalCanvas => new(_screenWidth, _screenHeight);
    public void ReportWarning(string message) => System.Console.Error.WriteLine(message);
}

public sealed record GodotHostDiagnosticSnapshot(
    string CurrentScene, bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);

using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly Lazy<EnumeratedFontFamily[]> _fontFamilies;
    private readonly Lazy<AspectFontRealization?> _fontRealization;

    /// <summary>The engine revision's furigana rules (op 0x196); Himegari's unless the profile selects another.</summary>
    public RubyLayoutRules RubyRules { get; init; } = RubyLayoutRules.Sys4422;

    /// <summary>Builds the installed-font list SYS4433's ops 0x2dc-0x2de read, in the engine's order, on first use.
    /// Without one the list is Godot's system fonts; Windows runs supply AGE's own (<see cref="FontFamilyCatalog"/>).
    /// </summary>
    public Func<IReadOnlyList<EnumeratedFontFamily>>? FontFamilySource { get; init; }

    /// <summary>Measures glyphs for the aspect-mode font calibration (<see cref="AspectFontRealization"/>); without
    /// one every font is created at half its height in width.</summary>
    public IFontMetricsProbe? FontMetricsProbe { get; init; }

    /// <summary>SYS4INI <c>BLANKEXTENTMODE</c> = 1: an aspect-mode blank in surface text advances by its own extent in
    /// the reference font (<see cref="ImmediateSurfaceTextRenderer"/>).</summary>
    public bool BlankExtentMode { get; init; }
    public GodotAdvHost(Main main, ResourceMap res, string scene, Age.Engine.Hosting.FrameClock clock,
                        PageLocatorState locator, Sys4LogicalCanvas logicalCanvas,
                        IGlyphMaskRasterizer surfaceTextRasterizer,
                        GodotTimelineLog? timeline = null,
                        bool synchronizeExplicitPresentation = true,
                        int surfaceTextMaskCacheCapacity = 2048,
                        GlyphCoverageAlpha coverageAlpha = GlyphCoverageAlpha.Sixteenths)
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
                _surfaceTextMaskCache, _surfaceTextBackendInfo.Policy, coverageAlpha);
        _retainedGlyphLayoutEngine =
            new RetainedGlyphLayoutEngine(_surfaceTextMaskCache, coverageAlpha);
        _fontFamilies = new Lazy<EnumeratedFontFamily[]>(() => FontFamilySource != null
            ? FontFamilySource().Where(family => !string.IsNullOrWhiteSpace(family.FaceName)).ToArray()
            : Godot.OS.GetSystemFonts()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new EnumeratedFontFamily(name, 0))
                .ToArray());
        _fontRealization = new Lazy<AspectFontRealization?>(() => FontMetricsProbe != null
            ? new AspectFontRealization(_fontFamilies.Value, FontMetricsProbe)
            : null);
    }

    public Sys4LogicalCanvas LogicalCanvas => new(_screenWidth, _screenHeight);
    public void ReportWarning(string message) => System.Console.Error.WriteLine(message);
    public int FindFontFamilyIndex(string faceName)
    {
        string ordinaryFace = faceName.StartsWith('@') ? faceName[1..] : faceName;
        // text_find_font_family_index@0x423a50 compares bytes exactly.
        return Array.FindIndex(_fontFamilies.Value,
            installed => installed.FaceName.Equals(ordinaryFace, StringComparison.Ordinal));
    }
    public int FontFamilyCount => _fontFamilies.Value.Length == 0 ? -1 : _fontFamilies.Value.Length;
    public string GetFontFamilyName(int index)
        => (uint)index < (uint)_fontFamilies.Value.Length ? _fontFamilies.Value[index].FaceName : "";

    /// <summary>The primary font's aspect-mode width rule, once the installed-font list exists.</summary>
    private AspectFontRealization? FontRealization => _fontRealization.Value;
}

public sealed record GodotHostDiagnosticSnapshot(
    string CurrentScene, bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);

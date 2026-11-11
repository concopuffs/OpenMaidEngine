using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Engine.Text;

public sealed partial class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _rootScene;
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly Ome.Engine.Hosting.FrameClock _clock;
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

    /// <summary>Creates the Unicode rasterizer that draws while a language is active (localization spec §7.4), on first
    /// use. Without one, or when the gameplay rasterizer is already portable, the gameplay rasterizer draws.</summary>
    public Func<IGlyphMaskRasterizer>? PresentedTextRasterizer { get; init; }

    /// <summary>Glyph masks the presented-text rasterizer's cache keeps.</summary>
    public int PresentedTextMaskCacheCapacity { get; init; } = 2048;
    public GodotAdvHost(Main main, ResourceMap res, string scene, Ome.Engine.Hosting.FrameClock clock,
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
        _sourceText = new TextPipeline(_surfaceTextBackendInfo, _surfaceTextMaskCache, coverageAlpha);
        _presentedText = new Lazy<TextPipeline>(() =>
        {
            if (_surfaceTextBackendInfo.Policy == GlyphRasterPolicy.PortableUnicode
                || PresentedTextRasterizer?.Invoke() is not { } rasterizer)
                return _sourceText;
            GlyphRasterizerBackendInfo info = (rasterizer as IIdentifiedGlyphMaskRasterizer)?.BackendInfo
                ?? throw new InvalidOperationException("The presented-text rasterizer must identify its policy.");
            if (info.Policy != GlyphRasterPolicy.PortableUnicode)
                throw new InvalidOperationException("The presented-text rasterizer must take Unicode requests.");
            return new TextPipeline(info, new CachedGlyphMaskRasterizer(rasterizer, PresentedTextMaskCacheCapacity),
                                    coverageAlpha);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
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

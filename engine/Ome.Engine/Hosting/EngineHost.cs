using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Ome.Engine.Diagnostics;

namespace Ome.Engine.Hosting;

public sealed partial class EngineHost : IHost, ISurfacePixels, IFastForwardListener
{
    private readonly IFrontEnd _frontEnd;
    private readonly SkipAndAuto _skip;
    private readonly Audio _audio;        // the auto timer reads channel 12
    private readonly EngineTick _tick;    // every non-modal wait runs it (EngineHost.Waits.cs)
    private readonly ResourceMap _res;
    private readonly string _rootScene;
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly Ome.Engine.Hosting.FrameClock _clock;
    private readonly TimelineLog? _timeline;
    private readonly PageLocatorState _locator;
    private readonly Lazy<EnumeratedFontFamily[]> _fontFamilies;
    private readonly Lazy<AspectFontRealization?> _fontRealization;

    /// <summary>The engine build's furigana rules for this install's settings (TextRules.Ruby).</summary>
    public required RubyLayoutRules RubyRules { get; init; }

    /// <summary>Builds the installed-font list SYS4433's ops 0x2dc-0x2de read, in the engine's order, on first use.
    /// Without one the list is empty. The Godot front end supplies its system fonts, and AGE's own on Windows runs
    /// (<see cref="FontFamilyCatalog"/>).
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
    /// <param name="skipAndAuto">The skip and auto native the VM runs with: the host reads fast-forward from it, keeps
    /// the Auto delays in it for SYS4REG.INI, and keeps what a fast-forward change does here until those natives
    /// exist.</param>
    /// <param name="audio">The audio native the VM runs with: ADV Auto waits for its voice.</param>
    /// <param name="tick">The engine tick the VM runs with: every wait that is not modal runs it each time it
    /// wakes, and presentation waits while its statement block is open.</param>
    public EngineHost(IFrontEnd frontEnd, SkipAndAuto skipAndAuto, Audio audio, EngineTick tick, ResourceMap res,
                        string scene,
                        Ome.Engine.Hosting.FrameClock clock,
                        PageLocatorState locator, Sys4LogicalCanvas logicalCanvas,
                        IGlyphMaskRasterizer surfaceTextRasterizer,
                        GlyphCoverageAlpha coverageAlpha,
                        TimelineLog? timeline = null,
                        bool synchronizeExplicitPresentation = true,
                        int surfaceTextMaskCacheCapacity = 2048)
    {
        _frontEnd = frontEnd; _res = res; _rootScene = scene; _clock = clock;
        _skip = skipAndAuto ?? throw new ArgumentNullException(nameof(skipAndAuto));
        _skip.Listen(this);
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
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
        _surfaceTextRasterizer = surfaceTextRasterizer;
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
            _presentedTextRasterizer = rasterizer;
            (rasterizer as ILanguageFontRasterizer)?.UseLanguageFonts(_presentedFonts);
            return new TextPipeline(info, new CachedGlyphMaskRasterizer(rasterizer, PresentedTextMaskCacheCapacity),
                                    coverageAlpha);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        _fontFamilies = new Lazy<EnumeratedFontFamily[]>(() => FontFamilySource != null
            ? FontFamilySource().Where(family => !string.IsNullOrWhiteSpace(family.FaceName)).ToArray()
            : []);
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

public sealed record EngineHostDiagnosticSnapshot(
    string CurrentScene, bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);

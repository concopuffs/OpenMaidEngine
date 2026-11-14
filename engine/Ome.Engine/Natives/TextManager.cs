using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Persistence;
using Ome.Engine.Text;

namespace Ome.Engine.Natives;

/// <summary>
/// What the text manager draws with: the gameplay glyph rasterizer, and what a front end may add (docs/superpowers/specs/
/// 2026-11-12-engine-layering-design.md §6.1, "Glyph rasterizer, font list"). Without them the text manager draws
/// nothing (<see cref="TextManager.Headless"/>).
/// </summary>
/// <param name="Gameplay">The gameplay rasterizer (--text-backend). It must identify its policy.</param>
/// <param name="CoverageAlpha">How a glyph's coverage becomes alpha (the build's <c>TextRules.GlyphCoverage</c>).</param>
/// <param name="GameplayCacheCapacity">Glyph masks the gameplay rasterizer's cache keeps.</param>
/// <param name="Presented">Creates the Unicode rasterizer that draws while a language is active (localization spec
/// §7.4), on first use. Without one, or when the gameplay rasterizer is already portable, the gameplay rasterizer
/// draws.</param>
/// <param name="PresentedCacheCapacity">Glyph masks the presented-text rasterizer's cache keeps.</param>
/// <param name="FontFamilies">Builds the installed-font list SYS4433's ops 0x2dc-0x2de read, in the engine's order, on
/// first use. Without one the list is empty.</param>
/// <param name="FontMetrics">Measures glyphs for the aspect-mode font calibration (<see cref="AspectFontRealization"/>);
/// without one every font is created at half its height in width.</param>
public sealed record TextDevices(
    IGlyphMaskRasterizer Gameplay,
    GlyphCoverageAlpha CoverageAlpha,
    int GameplayCacheCapacity = 2048,
    Func<IGlyphMaskRasterizer>? Presented = null,
    int PresentedCacheCapacity = 2048,
    Func<IReadOnlyList<EnumeratedFontFamily>>? FontFamilies = null,
    IFontMetricsProbe? FontMetrics = null);

/// <summary>
/// The text native (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.2; milestone 10): native AGE's text
/// manager (ctx+0x14940). It owns the ADV layouts and their history (<see cref="History"/>), the style, the retained
/// glyphs and cursors, the reveal and its glyph timer, the wait indicator, the page's suspension under a hide-window
/// callback, op 0x204's surface strings, the History groups it draws, the message settings (MessageSpeed, MesWinAlpha,
/// UseAntiFont), the installed-font list and the localization presenter. Its partials: <c>TextManager.Layout.cs</c>
/// (laying runs out and drawing them), <c>TextManager.Reveal.cs</c> (showing a run and its reveal),
/// <c>TextManager.WaitIndicator.cs</c> (the indicator and the page), <c>TextManager.Presentation.cs</c> (the presenter
/// and what it reads).
/// </summary>
/// <remarks>
/// <para>The reveal and the page wait block the engine thread in the input and waits native
/// (<c>InputAndWaits.TextWait.cs</c>): the native waits, and the text manager says when to stop.</para>
/// <para>Engine thread only (spec §5.1, rule 4): it lays out and draws, the waits advance the wait indicator in each
/// frame turn (<see cref="UpdateWaitIndicators"/>), and a routed click finishes a reveal
/// (<see cref="FinishRevealOnClick"/>).</para>
/// </remarks>
public sealed partial class TextManager : ISurfaceListener
{
    private readonly Graphics _graphics;
    private readonly SkipAndAuto _skip;
    private readonly FrameClock _clock;
    private readonly PageLocatorState? _locator;
    private readonly TimelineLog? _timeline;
    private readonly RubyLayoutRules _rubyRules;
    private readonly bool _blankExtentMode;
    private Func<bool> _inputWaiting = static () => false;
    private ITextListener[] _listeners = [];

    /// <summary>One rasterizer with its glyph cache and the two renderers over it.</summary>
    private sealed class TextPipeline(GlyphRasterizerBackendInfo info, CachedGlyphMaskRasterizer cache,
                                      GlyphCoverageAlpha coverageAlpha)
    {
        public GlyphRasterizerBackendInfo Info { get; } = info;
        public GlyphRasterPolicy Policy => Info.Policy;
        public CachedGlyphMaskRasterizer Cache { get; } = cache;
        public ImmediateSurfaceTextRenderer Pixels { get; } = new(cache, info.Policy, coverageAlpha);
        public RetainedGlyphLayoutEngine Layout { get; } = new(cache, coverageAlpha);
    }

    // The gameplay rasterizer (--text-backend), and the Unicode one every run uses while a language is active
    // (localization spec §7.4): an untranslated string then draws with the same faces as the translated ones beside it.
    // Null without devices: the text manager then lays out and draws nothing.
    private readonly IGlyphMaskRasterizer? _surfaceTextRasterizer;
    private readonly CachedGlyphMaskRasterizer? _surfaceTextMaskCache;
    private readonly GlyphRasterizerBackendInfo? _surfaceTextBackendInfo;
    private readonly TextPipeline? _sourceText;
    private readonly Lazy<TextPipeline>? _presentedText;
    private IGlyphMaskRasterizer? _presentedTextRasterizer;
    private Func<string, IReadOnlyList<string>?>? _presentedFonts;
    private volatile bool _presentsText;

    private readonly Lazy<EnumeratedFontFamily[]> _fontFamilies;
    private readonly Lazy<AspectFontRealization?> _fontRealization;

    private int _messageGlyphDelayMilliseconds = Sys4RegIniStore.DefaultMessageSpeed;
    private volatile int _messageWindowAlphaSetting = Sys4RegIniStore.DefaultMesWinAlpha;
    // Text manager +0x544 (message:UseAntiFont): GGO_GRAY4_BITMAP glyphs when set, GGO_BITMAP when clear. Both games
    // enable it; a text manager without a settings file keeps that.
    private volatile bool _textAntialias = true;

    /// <summary>The shown text, for tools, <c>GameSession</c> and the self-test: each run's source offset, text and
    /// script, hidden runs too. The engine thread appends to it.</summary>
    public List<(int Offset, string Text, string Script)> Emitted { get; } = new();

    /// <summary>The ADV layouts and the backlog (ops 0x70, 0x71, 0x79, 0x7a, 0x1c1, 0x198, 0x212, 0x213 and History's
    /// ops): what each layout draws from, and what was shown. A numbered save writes its tail
    /// (<see cref="WriteHistoryTail"/>).</summary>
    public AdvTextHistory History { get; }

    /// <summary>The text manager's style (the font, size, colour, effect and aspect ops): what a run, a draw-string and
    /// a History group draw in. Op 0x9 resets it.</summary>
    public AdvTextStyle Style { get => _scene.Style; set => _scene.Style = value; }

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5).
    private sealed class Scene
    {
        public AdvTextStyle Style = AdvTextStyle.Default;
        public readonly HashSet<int> RetainedHistoryLayouts = new();
        public readonly Dictionary<int, RetainedAdvTextLayoutPresentation> RetainedTextLayouts = new();
        // The layout of the last run shown: releasing or resetting it stops that run's reveal.
        public int ActiveRunLayout;
        // Each layout's pending record (left, top): AGE draws into the layout's last 20-byte record and pushes the next one
        // at the cursor, which op 0x7a, op 0x6f and a reset move. The follow-last-glyph wait indicator anchors here.
        public readonly Dictionary<int, (int X, int Y)> LayoutCursors = new();
        public bool AdvTextForceComplete;
        // ctx+0x55108: a click (or a held action 4) finished a reveal; every later reveal publishes at once until op 0x72.
        public bool RevealsFinishedUntilWait;
        public RevealState? Reveal;
        public volatile bool IsRevealing;
        public readonly Dictionary<int, AdvWaitIndicatorConfig> WaitIndicators = new();
        public readonly Dictionary<int, (AdvTextLayoutPresentationBinding Binding, AdvTextLayoutSnapshot Layout)> WaitIndicatorBindings = new();
        public readonly Dictionary<int, RetainedAdvWaitIndicatorPresentation> RetainedWaitIndicators = new();
        public int ActiveWaitLayout;
        public bool WaitIndicatorEnabled;
        public int SuspendedRetainedTextLayoutSlot;
        public volatile bool AdvPagePresentationSuspended;
        // The string stores (§5.2): op 0x9 zeroes the string bank, so every record goes with it.
        public readonly Dictionary<int, StringStore> StringStores = new();
        // Op 0x1b1's mode, text-manager+0x56c (SYS4433 +0x570): the reset sets it to 0, fixed layout-origin anchoring.
        public bool WaitIndicatorFollowLastGlyph;
    }

    private Scene _scene = new();

    /// <param name="graphics">The graphics native: text draws into its surfaces, and the text manager hears its
    /// surface releases (the layouts retained on a surface go with its pixels).</param>
    /// <param name="skip">The skip and auto native: fast-forward publishes a run at once.</param>
    /// <param name="clock">The engine's frame clock, which the glyph timer and the wait indicator's frames read.</param>
    /// <param name="devices">The rasterizers and the font list; null for a text manager that draws nothing.</param>
    /// <param name="ruby">The engine build's furigana rules for this install's settings (<c>TextRules.Ruby</c>).</param>
    /// <param name="blankExtentMode">SYS4INI <c>BLANKEXTENTMODE</c> = 1: an aspect-mode blank in surface text advances
    /// by its own extent in the reference font (<see cref="ImmediateSurfaceTextRenderer"/>).</param>
    /// <param name="settings">SYS4REG.INI: MessageSpeed, MesWinAlpha and UseAntiFont start from its [message] keys,
    /// native's defaults when absent.</param>
    /// <param name="locator">The page map, which records each run's text and geometry.</param>
    /// <param name="history">The layouts and backlog, when they outlive this native (a <c>GameSession</c> shares one
    /// across its scenes); a new one otherwise.</param>
    public TextManager(Graphics graphics, SkipAndAuto skip, FrameClock clock, TextDevices? devices,
                       RubyLayoutRules ruby, bool blankExtentMode = false, Sys4RegIniStore? settings = null,
                       PageLocatorState? locator = null, TimelineLog? timeline = null, AdvTextHistory? history = null)
    {
        History = history ?? new AdvTextHistory();
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _skip = skip ?? throw new ArgumentNullException(nameof(skip));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _rubyRules = ruby ?? throw new ArgumentNullException(nameof(ruby));
        _blankExtentMode = blankExtentMode;
        _locator = locator;
        _timeline = timeline;
        _graphics.Listen(this);
        if (settings != null) ReadSettings(settings);

        if (devices != null)
        {
            ArgumentNullException.ThrowIfNull(devices.Gameplay);
            GlyphRasterizerBackendInfo backend = (devices.Gameplay as IIdentifiedGlyphMaskRasterizer)?.BackendInfo
                ?? throw new ArgumentException("Gameplay glyph rasterizers must identify their policy.",
                                               nameof(devices));
            _surfaceTextBackendInfo = backend;
            _surfaceTextRasterizer = devices.Gameplay;
            _surfaceTextMaskCache = new CachedGlyphMaskRasterizer(devices.Gameplay, capacity: devices.GameplayCacheCapacity);
            TextPipeline source = new(backend, _surfaceTextMaskCache, devices.CoverageAlpha);
            _sourceText = source;
            _presentedText = new Lazy<TextPipeline>(() =>
            {
                if (backend.Policy == GlyphRasterPolicy.PortableUnicode
                    || devices.Presented?.Invoke() is not { } rasterizer)
                    return source;
                GlyphRasterizerBackendInfo info = (rasterizer as IIdentifiedGlyphMaskRasterizer)?.BackendInfo
                    ?? throw new InvalidOperationException("The presented-text rasterizer must identify its policy.");
                if (info.Policy != GlyphRasterPolicy.PortableUnicode)
                    throw new InvalidOperationException("The presented-text rasterizer must take Unicode requests.");
                _presentedTextRasterizer = rasterizer;
                (rasterizer as ILanguageFontRasterizer)?.UseLanguageFonts(_presentedFonts);
                return new TextPipeline(info, new CachedGlyphMaskRasterizer(rasterizer, devices.PresentedCacheCapacity),
                                        devices.CoverageAlpha);
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }
        Func<IReadOnlyList<EnumeratedFontFamily>>? familySource = devices?.FontFamilies;
        IFontMetricsProbe? metrics = devices?.FontMetrics;
        _fontFamilies = new Lazy<EnumeratedFontFamily[]>(() => familySource != null
            ? familySource().Where(family => !string.IsNullOrWhiteSpace(family.FaceName)).ToArray()
            : []);
        _fontRealization = new Lazy<AspectFontRealization?>(() => metrics != null
            ? new AspectFontRealization(_fontFamilies.Value, metrics)
            : null);
    }

    /// <summary>A text manager that draws nothing, for a VM given none and for headless runs: it keeps every layout,
    /// cursor, setting and indicator, and records the shown text, as a host without text pixels did.</summary>
    public static TextManager Headless(Graphics graphics, SkipAndAuto skip, FrameClock? clock = null,
                                       AdvTextHistory? history = null)
        => new(graphics, skip, clock ?? new FrameClock(), devices: null, RubyLayoutRules.FixedOffsets, history: history);

    /// <summary>Add a listener that hears what the text native does (<see cref="ITextListener"/>).</summary>
    public void Listen(ITextListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (Array.IndexOf(_listeners, listener) < 0) _listeners = [.. _listeners, listener];
    }

    private void Tell(Action<ITextListener> call)
    {
        foreach (ITextListener listener in _listeners) call(listener);
    }

    /// <summary>Where the text manager learns whether the engine thread is in an input wait: the wait indicator shows
    /// only then, and the page a hide-window callback suspends is the waiting one: the input and waits native's.
    /// </summary>
    public void ReadInputWaitFrom(Func<bool> waiting) => _inputWaiting = waiting ?? (static () => false);

    private bool InputWaiting => _inputWaiting();

    // ---- what draws ----

    /// <summary>Whether the text manager draws text pixels (false without devices).</summary>
    public bool UsesSurfaceTextPixels => _sourceText != null;

    public GlyphRasterizerBackendInfo? SurfaceTextBackendInfo => _surfaceTextBackendInfo;

    public (int Count, int Capacity, long Hits, long Misses) SurfaceTextMaskCacheStats
        => _surfaceTextMaskCache is { } cache ? (cache.Count, cache.Capacity, cache.Hits, cache.Misses) : (0, 0, 0, 0);

    /// <summary>Whether a language other than the source is active, with a catalog or the pseudo-language: every run
    /// then draws with the Unicode rasterizer. False keeps the gameplay rasterizer (gate 1).</summary>
    public bool PresentsText
    {
        get => _presentsText;
        set
        {
            if (_presentsText == value) return;
            _presentsText = value;
            _timeline?.Event("text-presentation", new() { ["active"] = value });
        }
    }

    private TextPipeline ActiveText => _presentsText ? _presentedText!.Value : _sourceText!;

    /// <summary>
    /// The language's fonts (localization spec §7.4) for the rasterizer presented text draws with: put in front of
    /// the portable policy's substitutes, for every run while the language is active. Null (the source language, or a
    /// language without font tables) leaves the policy alone. Switching drops the fonts and glyph masks made so far;
    /// text already drawn stays.
    /// </summary>
    public void UsePresentedFonts(Ome.Engine.Localization.TextLanguage? language)
    {
        Func<string, IReadOnlyList<string>?>? fonts = language is { Fonts.Count: > 0 } ? language.FontsFor : null;
        if (fonts == null && _presentedFonts == null) return;
        _presentedFonts = fonts;
        if (_presentedText == null) return;
        // The presented pipeline is the gameplay one when that rasterizer is already portable.
        if (_presentedText.IsValueCreated && _presentedTextRasterizer is ILanguageFontRasterizer presented)
        {
            presented.UseLanguageFonts(fonts);
            _presentedText.Value.Cache.Clear();
        }
        else if (_surfaceTextBackendInfo!.Policy == GlyphRasterPolicy.PortableUnicode
                 && _surfaceTextRasterizer is ILanguageFontRasterizer gameplay)
        {
            gameplay.UseLanguageFonts(fonts);
            _surfaceTextMaskCache!.Clear();
        }
    }

    /// <summary>The aspect-mode font list for <paramref name="text"/>'s rasterizer: the gameplay rasterizer's, or none
    /// for the Unicode one, as on a portable run.</summary>
    private AspectFontRealization? RealizationFor(TextPipeline text)
        => ReferenceEquals(text, _sourceText) ? _fontRealization.Value : null;

    /// <summary>A presented run's style: aspect mode's fixed reference cell does not fit other scripts, so presented
    /// glyphs advance by their own width (spec §7.4).</summary>
    private static AdvTextStyle PresentedStyle(AdvTextStyle style) => style with { AspectMode = 0 };

    // ---- the installed fonts (SYS4433 ops 0x2dc-0x2de) ----

    /// <summary>Op 0x2de: a stable non-negative index of an installed family, or -1.
    /// text_find_font_family_index@0x423a50 compares bytes exactly; a vertical face's '@' is dropped.</summary>
    public int FindFontFamilyIndex(string faceName)
    {
        string ordinaryFace = faceName.StartsWith('@') ? faceName[1..] : faceName;
        return Array.FindIndex(_fontFamilies.Value,
            installed => installed.FaceName.Equals(ordinaryFace, StringComparison.Ordinal));
    }

    /// <summary>Op 0x2dc: the installed-family list's size, -1 for an empty list.</summary>
    public int FontFamilyCount => _fontFamilies.Value.Length == 0 ? -1 : _fontFamilies.Value.Length;

    /// <summary>Op 0x2dd: the indexed family, or an empty string for an index outside the list.</summary>
    public string GetFontFamilyName(int index)
        => (uint)index < (uint)_fontFamilies.Value.Length ? _fontFamilies.Value[index].FaceName : "";

    // ---- the message settings ----

    /// <summary>MessageSpeed, MesWinAlpha and UseAntiFont from SYS4REG.INI's [message], native defaults when absent.
    /// AGE writes them back when it quits (<see cref="WriteSettings"/>).</summary>
    private void ReadSettings(Sys4RegIniStore store)
    {
        int speed = store.LoadMessageInt(Sys4RegIniStore.MessageSpeedKey, Sys4RegIniStore.DefaultMessageSpeed);
        _messageGlyphDelayMilliseconds = Math.Max(0, speed);
        _messageWindowAlphaSetting = store.LoadMessageInt(Sys4RegIniStore.MesWinAlphaKey,
                                                          Sys4RegIniStore.DefaultMesWinAlpha);
        _textAntialias = store.TextAntialiasEnabled;
    }

    /// <summary>The shutdown write of MessageSpeed and MesWinAlpha into SYS4REG.INI (UseAntiFont is the shell's to
    /// write, when its option changes).</summary>
    public void WriteSettings(Sys4RegIniStore store)
    {
        store.SaveMessageInt(Sys4RegIniStore.MessageSpeedKey, MessageGlyphDelayMilliseconds);
        store.SaveMessageInt(Sys4RegIniStore.MesWinAlphaKey, _messageWindowAlphaSetting);
    }

    public int MessageGlyphDelayMilliseconds
    {
        get
        {
            return _messageGlyphDelayMilliseconds;
        }
    }

    public void SetMessageGlyphDelayMilliseconds(int milliseconds)
    {
        int delay = Math.Max(0, milliseconds);
        _messageGlyphDelayMilliseconds = delay;
        _timeline?.Event("message-glyph-delay", new() { ["delay_ms"] = delay });
    }

    public int MessageWindowAlphaSetting => _messageWindowAlphaSetting;

    public void SetMessageWindowAlphaSetting(int value)
    {
        _messageWindowAlphaSetting = value;
        _timeline?.Event("message-window-alpha", new() { ["value"] = value });
    }

    /// <summary>Whether glyphs are antialiased (text manager +0x544, message:UseAntiFont).</summary>
    public bool TextAntialias => _textAntialias;

    /// <summary>
    /// text_set_antialias_enabled@0x4137e0, which IAGEService +0x68 calls after writing message:UseAntiFont: later
    /// text rasterizes in the new format. AGE also empties its glyph caches; text already drawn is not redrawn.
    /// The port's glyph requests carry the flag, so its cache keeps the two formats apart instead.
    /// </summary>
    public void SetTextAntialias(bool enabled)
    {
        if (_textAntialias == enabled) return;
        _textAntialias = enabled;
        _timeline?.Event("text-antialias", new() { ["enabled"] = enabled });
    }

    // ---- op 0x9 ----

    /// <summary>Op 0x9 (scene_context_init_reset): every layout's retained glyphs, cursors and wait indicator, the
    /// reveal, the page's suspension and the string stores go (op 0x9 zeroes the string bank), the style starts over and
    /// History records again. The backlog, the settings, the current layout, the glyph timer and the fonts stay.
    /// </summary>
    public void ResetScene()
    {
        _scene = new();
        History.SetRecordingEnabled(true);
    }

    // ---- a numbered save's records (spec §5.1, rule 6) ----

    /// <summary>The history tail a numbered save writes beside its payload (op 0x19e).</summary>
    public byte[] WriteHistoryTail() => NativeTextHistoryCodec.Encode(History);

    /// <summary>A full load's history tail (op 0x1a1), decoded before anything changed: the backlog it holds, less its
    /// last group, which the resumed page records again; none empties the backlog.</summary>
    public void ReadHistoryTail(NativeTextHistoryCodec.Decoded? tail)
    {
        if (tail == null)
        {
            History.Clear();
            return;
        }
        History.RestorePersistenceSnapshot(tail.Entries, tail.Records);
        History.DropLastGroup();
    }

    // ---- the surfaces: the layouts retained on a surface go with its pixels ----

    void ISurfaceListener.SurfaceReleasing(int slot) { }

    void ISurfaceListener.SurfaceReleased(int slot) => DropTextLayoutsOn(slot);

    void ISurfaceListener.SurfaceRangeReleasing(int firstSlot, int count) { }

    void ISurfaceListener.SurfaceRangeReleased(int firstSlot, int count)
    {
        int end = checked(firstSlot + count);
        foreach (int layoutSlot in _scene.RetainedTextLayouts
                     .Where(pair =>
                         pair.Value.Binding.SourceSurfaceSlot >= firstSlot
                         && pair.Value.Binding.SourceSurfaceSlot < end)
                     .Select(pair => pair.Key)
                     .ToArray())
            _scene.RetainedTextLayouts.Remove(layoutSlot);
        _scene.RetainedHistoryLayouts.RemoveWhere(layoutSlot =>
            !_scene.RetainedTextLayouts.ContainsKey(layoutSlot));
    }

    void ISurfaceListener.SurfaceReplaced(int slot) => DropTextLayoutsOn(slot);

    private void DropTextLayoutsOn(int slot)
    {
        foreach (int layoutSlot in _scene.RetainedTextLayouts
                     .Where(pair => pair.Value.Binding.SourceSurfaceSlot == slot)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _scene.RetainedTextLayouts.Remove(layoutSlot);
            _scene.RetainedHistoryLayouts.Remove(layoutSlot);
        }
    }
}

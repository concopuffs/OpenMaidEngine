using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Persistence;
using Ome.Engine.Text;

public sealed partial class GodotAdvHost
{
    private readonly object _textLock = new();
    private readonly CachedGlyphMaskRasterizer _surfaceTextMaskCache;
    private readonly GlyphRasterizerBackendInfo _surfaceTextBackendInfo;

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
    private readonly TextPipeline _sourceText;
    private readonly Lazy<TextPipeline> _presentedText;
    private volatile bool _presentsText;

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

    private TextPipeline ActiveText => _presentsText ? _presentedText.Value : _sourceText;

    private readonly IGlyphMaskRasterizer _surfaceTextRasterizer;
    private IGlyphMaskRasterizer? _presentedTextRasterizer;
    private Func<string, IReadOnlyList<string>?>? _presentedFonts;

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
        // The presented pipeline is the gameplay one when that rasterizer is already portable.
        if (_presentedText.IsValueCreated && _presentedTextRasterizer is GodotTextServerGlyphMaskRasterizer presented)
        {
            presented.UseLanguageFonts(fonts);
            _presentedText.Value.Cache.Clear();
        }
        else if (_surfaceTextBackendInfo.Policy == GlyphRasterPolicy.PortableUnicode
                 && _surfaceTextRasterizer is GodotTextServerGlyphMaskRasterizer gameplay)
        {
            gameplay.UseLanguageFonts(fonts);
            _surfaceTextMaskCache.Clear();
        }
    }

    /// <summary>The aspect-mode font list for <paramref name="text"/>'s rasterizer: the gameplay rasterizer's, or none
    /// for the Unicode one, as on a portable run.</summary>
    private AspectFontRealization? RealizationFor(TextPipeline text)
        => ReferenceEquals(text, _sourceText) ? FontRealization : null;

    /// <summary>A presented run's style: aspect mode's fixed reference cell does not fit other scripts, so presented
    /// glyphs advance by their own width (spec §7.4).</summary>
    private static AdvTextStyle PresentedStyle(AdvTextStyle style) => style with { AspectMode = 0 };
    private readonly HashSet<int> _retainedHistoryLayouts = new();
    private sealed class LiveTextState
    {
        public required AdvLiveTextRun Run;
        public int FirstGlyphIndex;
        public int GlyphCount;
    }
    private readonly List<LiveTextState> _liveText = new();
    private readonly Dictionary<int, RetainedAdvTextLayoutPresentation>
        _retainedTextLayouts = new();
    private LiveTextState? _activeLiveText;
    private int _suspendedRetainedTextLayoutSlot;
    private string _advText = "";
    // Each layout's pending record (left, top): AGE draws into the layout's last 20-byte record and pushes the next one
    // at the cursor, which op 0x7a, op 0x6f and a reset move. The follow-last-glyph wait indicator anchors here.
    private readonly Dictionary<int, (int X, int Y)> _layoutCursors = new();
    private int _currentAdvLayout = 1; // SYSTEM4's ordinary SC0000 ADV layout
    // ctx+0x5f630, the reveal's glyph timer: op 0x6e arms it, op 0x196 keeps polling the previous arm.
    private readonly PeriodTimer _glyphTimer = new();
    private int _messageGlyphDelayMilliseconds = Sys4RegIniStore.DefaultMessageSpeed;
    private volatile int _messageWindowAlphaSetting = Sys4RegIniStore.DefaultMesWinAlpha;
    private long _autoMessageTime0Milliseconds = Sys4RegIniStore.DefaultAutoMessageTime0;
    private long _autoMessageTime1Milliseconds = Sys4RegIniStore.DefaultAutoMessageTime1;
    private Sys4RegIniStore? _messageSettingsStore;
    // Text manager +0x544 (message:UseAntiFont): GGO_GRAY4_BITMAP glyphs when set, GGO_BITMAP when clear. Both games
    // enable it; hosts without a settings file keep that.
    private volatile bool _textAntialias = true;
    private bool _advTextForceComplete;
    // ctx+0x55108: a click (or a held action 4) finished a reveal; every later reveal publishes at once until op 0x72.
    private bool _revealsFinishedUntilWait;
    private readonly Dictionary<int, AdvWaitIndicatorConfig> _waitIndicators = new();
    private bool _waitIndicatorFollowLastGlyph;
    private readonly Dictionary<int, (
        AdvTextLayoutPresentationBinding Binding,
        AdvTextLayoutSnapshot Layout)> _waitIndicatorBindings = new();
    private readonly Dictionary<int, RetainedAdvWaitIndicatorPresentation>
        _retainedWaitIndicators = new();
    private int _activeWaitLayout;
    private long _waitIndicatorStartedMs;
    private bool _waitIndicatorEnabled;
    public volatile bool IsTextRevealing;
    public readonly List<(int Offset, string Text)> Captured = new();

    public bool UsesSurfaceTextPixels => true;
    public GlyphRasterizerBackendInfo SurfaceTextBackendInfo => _surfaceTextBackendInfo;
    public (int Count, int Capacity, long Hits, long Misses) SurfaceTextMaskCacheStats
        => (
            _surfaceTextMaskCache.Count,
            _surfaceTextMaskCache.Capacity,
            _surfaceTextMaskCache.Hits,
            _surfaceTextMaskCache.Misses);

    public int MessageGlyphDelayMilliseconds
    {
        get
        {
            lock (_textLock) return _messageGlyphDelayMilliseconds;
        }
    }

    public void SetMessageGlyphDelayMilliseconds(int milliseconds)
    {
        lock (_textLock) _messageGlyphDelayMilliseconds = System.Math.Max(0, milliseconds);
        _timeline?.Event("message-glyph-delay", new() { ["delay_ms"] = milliseconds });
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

    public long AutoMessageTime0Milliseconds => Interlocked.Read(ref _autoMessageTime0Milliseconds);
    public long AutoMessageTime1Milliseconds => Interlocked.Read(ref _autoMessageTime1Milliseconds);

    public void SetAutoMessageTime(int selector, long milliseconds)
    {
        if (selector == 0) Interlocked.Exchange(ref _autoMessageTime0Milliseconds, milliseconds);
        else if (selector == 1) Interlocked.Exchange(ref _autoMessageTime1Milliseconds, milliseconds);
    }

    /// <summary>
    /// Seed MessageSpeed, MesWinAlpha and AutoMessageTime0/1 from SYS4REG.INI [message] (native defaults when
    /// absent). Changes are written back by <see cref="FlushMessageSettings"/> at shutdown, with ReadTextSkip, as AGE
    /// rewrites SYS4REG.INI when it quits; MAMES/ITMES/SKMES/INFOMES briefly set the delay to 0 per description, so no
    /// per-op write. Call before the VM is constructed: it reads them once.
    /// </summary>
    public void UseMessageSettingsStore(Sys4RegIniStore store)
    {
        _messageSettingsStore = store;
        int speed = store.LoadMessageInt(Sys4RegIniStore.MessageSpeedKey, Sys4RegIniStore.DefaultMessageSpeed);
        lock (_textLock)
        {
            _messageGlyphDelayMilliseconds = System.Math.Max(0, speed);
        }
        _messageWindowAlphaSetting = store.LoadMessageInt(
            Sys4RegIniStore.MesWinAlphaKey, Sys4RegIniStore.DefaultMesWinAlpha);
        SetAutoMessageTime(0, store.LoadMessageInt(
            Sys4RegIniStore.AutoMessageTime0Key, Sys4RegIniStore.DefaultAutoMessageTime0));
        SetAutoMessageTime(1, store.LoadMessageInt(
            Sys4RegIniStore.AutoMessageTime1Key, Sys4RegIniStore.DefaultAutoMessageTime1));
        _textAntialias = store.TextAntialiasEnabled;
    }

    /// <param name="readTextSkip">message:ReadTextSkip, which the shared profile carried while the game ran.</param>
    public void FlushMessageSettings(bool readTextSkip)
    {
        // Unconditional, like AGE on quit: the write also leaves every key AGE binds (Sys4RegIniStore).
        if (_messageSettingsStore == null) return;
        try
        {
            _messageSettingsStore.SaveMessageInt(Sys4RegIniStore.MessageSpeedKey, MessageGlyphDelayMilliseconds);
            _messageSettingsStore.SaveMessageInt(Sys4RegIniStore.MesWinAlphaKey, _messageWindowAlphaSetting);
            // The registry keeps a 32-bit value: op 0x1b9's operand wraps as AGE's int does.
            _messageSettingsStore.SaveMessageInt(Sys4RegIniStore.AutoMessageTime0Key,
                                                 unchecked((int)AutoMessageTime0Milliseconds));
            _messageSettingsStore.SaveMessageInt(Sys4RegIniStore.AutoMessageTime1Key,
                                                 unchecked((int)AutoMessageTime1Milliseconds));
            _messageSettingsStore.SaveReadTextSkip(readTextSkip);
        }
        catch (Exception error) when (
            error is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            Godot.GD.PushWarning($"[settings] message settings save failed: {error.Message}");
        }
    }

    public void ShowText(int offset, string text)
        => throw new NotSupportedException(
            "Godot gameplay text requires a retained layout binding.");

    public void ShowText(AdvLiveTextRun run, int glyphDelayMilliseconds)
        => throw new NotSupportedException(
            "Godot gameplay text requires a retained layout binding.");

    public AdvTextSourceLayout? LayoutHiddenTextRun(AdvTextLayoutPresentationBinding binding, AdvLiveTextRun run)
    {
        Captured.Add((run.SourceOffset, run.Text));
        _locator.Text(run.SourceOffset, run.Text);
        if (run.Layout.Width <= 0 || run.Layout.Height <= 0) return null;
        AdvTextSourceLayout source = LayoutSourceText(binding, run, out IReadOnlyList<AdvRetainedGlyphRecord> glyphs);
        if (binding.LayoutSlot <= 0 || binding.LayoutSlot != run.Layout.Slot || binding.SourceSurfaceSlot < 0
            || binding.FirstObjectHandle < 0 || binding.ObjectCapacity <= 0)
            return source;
        lock (_textLock)
        {
            if (!_retainedTextLayouts.TryGetValue(binding.LayoutSlot, out RetainedAdvTextLayoutPresentation? presentation))
            {
                presentation = new RetainedAdvTextLayoutPresentation(binding);
                _retainedTextLayouts.Add(binding.LayoutSlot, presentation);
            }
            // Drawn by the message's first run; a save binds what the source-language run would have drawn.
            if (presentation.Binding == binding)
                presentation.AppendSourceOnly(glyphs, run.Layout.OriginX, run.Layout.OriginY);
        }
        return source;
    }

    public IReadOnlyList<AdvTextSourceObjects> SourceTextObjects
    {
        get
        {
            lock (_textLock)
                return _retainedTextLayouts.Values
                    .Where(presentation => presentation.SourceDiffers)
                    .Select(presentation => new AdvTextSourceObjects(
                        presentation.Binding.FirstObjectHandle, presentation.Binding.ObjectCapacity,
                        presentation.Binding.SourceSurfaceSlot, presentation.PublishedSourceGlyphs()))
                    .ToArray();
        }
    }

    /// <summary>
    /// The source layout pass (localization spec §8): <paramref name="run"/>'s source text and ruby laid out with the
    /// gameplay rasterizer from the source cursor, as the run would draw with no language selected, without drawing.
    /// History records its rows, so a save holds what the source-language run would have recorded, and
    /// <paramref name="glyphs"/> are its records in drawing order, which a save binds in place of the drawn glyphs.
    /// </summary>
    private AdvTextSourceLayout LayoutSourceText(AdvTextLayoutPresentationBinding binding, AdvLiveTextRun run,
                                                 out IReadOnlyList<AdvRetainedGlyphRecord> glyphs)
    {
        TextPipeline text = _sourceText;
        AspectFontRealization? realization = RealizationFor(text);
        (int x, int y) = run.SourceCursor ?? (run.Layout.CursorX, run.Layout.CursorY);
        GlyphTextLayoutResult laid = text.Layout.Render(
            null,
            new GlyphTextLayoutOptions(
                x, y, binding.ResetCursorX, run.Layout.Right, run.Layout.Bottom, WrapHorizontally: true, run.Style,
                FontRealization: realization),
            ImmediateSurfaceTextRenderer.CreateRequests(run.Text, run.Style, text.Policy, _textAntialias, realization));
        IReadOnlyList<AdvRubyGlyph> ruby = !string.IsNullOrEmpty(run.SourceRuby) && laid.Rows.Count > 0
            ? text.Layout.RenderRuby(
                null, run.Text, laid.Rows, run.SourceRuby, run.Style, text.Policy, _textAntialias, RubyRules)
            : [];
        glyphs = OrderedGlyphs(laid, ruby, byRow: !string.IsNullOrEmpty(run.SourceRuby) && laid.Rows.Count > 0)
            .Select(item => item.Record).ToArray();
        return new AdvTextSourceLayout(laid.Rows, ruby, laid.CursorX, laid.CursorY);
    }

    /// <summary>A run's records in AGE's drawing order: with ruby (<paramref name="byRow"/>), op 0x196's ruby follows
    /// each base row.</summary>
    private static IEnumerable<(AdvRetainedGlyphRecord Record, bool IsRuby)> OrderedGlyphs(
        GlyphTextLayoutResult rendered, IReadOnlyList<AdvRubyGlyph> ruby, bool byRow)
    {
        if (!byRow)
        {
            foreach (AdvRetainedGlyphRecord record in rendered.Records) yield return (record, false);
            yield break;
        }
        for (int row = 0; row < rendered.Rows.Count; row++)
        {
            AdvTextRow drawn = rendered.Rows[row];
            for (int glyph = drawn.FirstGlyph; glyph < drawn.FirstGlyph + drawn.GlyphCount; glyph++)
                yield return (rendered.Records[glyph], false);
            foreach (AdvRubyGlyph character in ruby)
                if (character.Row == row) yield return (character.Record, true);
        }
    }

    public AdvRetainedTextRunResult? ShowText(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvLiveTextRun run,
        int glyphDelayMilliseconds)
    {
        if (binding.LayoutSlot != run.Layout.Slot
            || binding.FirstObjectHandle < 0
            || binding.ObjectCapacity <= 0)
            throw new InvalidOperationException(
                $"ADV layout {run.Layout.Slot} has no retained presentation binding.");
        PrepareRetainedTextRun(
            gfx, binding, run,
            out RetainedAdvTextLayoutPresentation? presentation,
            out AdvRetainedTextRunResult result);

        Captured.Add((run.SourceOffset, run.Text));
        _locator.Text(run.SourceOffset, run.Text);
        int delay = System.Math.Max(0, glyphDelayMilliseconds);
        int revealGlyphCount = System.Math.Max(
            0, System.Math.Min(
                result.GlyphCount,
                presentation.PublishableGlyphCount - result.FirstGlyphIndex));
        // A tick publishes one record, or a chained group after a ruby (adv_text_publish_next_glyph@0x451220).
        int revealTicks = presentation.PublicationTicks(result.FirstGlyphIndex, revealGlyphCount);
        var state = new LiveTextState
        {
            Run = run,
            FirstGlyphIndex = result.FirstGlyphIndex,
            GlyphCount = revealGlyphCount,
        };
        lock (_textLock)
        {
            _liveText.Add(state);
            _activeLiveText = state;
            _advText = run.Text;
            _currentAdvLayout = run.Layout.Slot;
            // Read skip takes the blocking builder (no timer); a finished-until-wait reveal still arms it.
            bool incremental = revealGlyphCount > 0 && delay > 0 && !_messageSkipActive;
            if (incremental && run.ArmsGlyphTimer) _glyphTimer.Arm(_clock.NowMs, delay);
            _advTextForceComplete = _messageSkipActive || _revealsFinishedUntilWait;
            IsTextRevealing = incremental && !_advTextForceComplete;
        }
        _timeline?.State("text-reveal", new()
        {
            ["offset"] = $"0x{run.SourceOffset:x}",
            ["layout"] = run.Layout.Slot,
            ["x"] = run.Layout.OriginX + run.Layout.CursorX,
            ["y"] = run.Layout.OriginY + run.Layout.CursorY,
            ["glyphs"] = revealGlyphCount,
            ["records"] = result.GlyphCount,
            ["delay_ms"] = delay,
            ["presentation"] = "retained-glyphs",
        });

        if (!IsTextRevealing)
        {
            presentation.PublishThrough(gfx, checked(result.FirstGlyphIndex + revealGlyphCount));
            Interlocked.Exchange(ref _presentRequested, 1);
            _timeline?.State("running", new() { ["text_reveal_complete"] = true });
            return result;
        }

        // adv_text_reveal_service_tick@0x406fa0 (draw mode 1): every engine tick polls the glyph timer and publishes one
        // tick when it succeeds, so the first tick also waits a full delay and a late poll drops missed boundaries.
        // Presentation is requested, not awaited: a frame wait would make the poll late and drop boundaries.
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            Interlocked.Exchange(ref _presentRequested, 1);
            int ticks = 0;
            while (IsTextRevealing && !_stopping)
            {
                long? due;
                lock (_textLock)
                {
                    if (_advTextForceComplete) ticks = revealTicks;
                    else if (_glyphTimer.Poll(_clock.NowMs) >= 0) ticks++;
                    if (ticks >= revealTicks) IsTextRevealing = false;
                    due = _glyphTimer.NextDueMs;
                }
                int before = presentation.PublishedGlyphCount;
                presentation.PublishThrough(
                    gfx, presentation.PublicationTarget(result.FirstGlyphIndex, revealGlyphCount, ticks));
                if (presentation.PublishedGlyphCount != before)
                    Interlocked.Exchange(ref _presentRequested, 1);
                if (IsTextRevealing)
                    _frameSignal.WaitOne(due is long deadline ? _clock.RealMillisecondsUntil(deadline, 50) : 50);
            }
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _timeline?.State("running", new() { ["text_reveal_complete"] = true });
        return result;
    }

    /// <param name="recordedLineBreaks">History's retained rows (op 0x1d1): break only at these glyph
    /// indices, never at the layout's edges. Native text_history_render_records@0x4526c0 checks no bounds;
    /// the surface clips what falls outside.</param>
    /// <param name="historyRuby">History's ruby runs (op 0x1d1), each drawn over its base glyphs.</param>
    /// <param name="history">Op 0x1d1's group: no bottom bound, as native checks none, and no source layout pass.</param>
    private void PrepareRetainedTextRun(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvLiveTextRun run,
        out RetainedAdvTextLayoutPresentation presentation,
        out AdvRetainedTextRunResult result,
        IReadOnlyList<int>? recordedLineBreaks = null,
        IReadOnlyList<AdvTextRubyAnnotation>? historyRuby = null,
        bool history = false)
    {
        presentation = null!;
        result = default;
        if (run.Layout.Width <= 0 || run.Layout.Height <= 0)
            throw new InvalidOperationException(
                $"ADV layout {run.Layout.Slot} has invalid dimensions " +
                $"{run.Layout.Width}x{run.Layout.Height}.");

        RgbaImage destination = ResolveSurfacePixels(binding.SourceSurfaceSlot)
            ?? new RgbaImage(
                run.Layout.Width,
                run.Layout.Height,
                new byte[checked(run.Layout.Width * run.Layout.Height * 4)]);
        if (destination.Width != run.Layout.Width
            || destination.Height != run.Layout.Height)
            throw new InvalidOperationException(
                $"ADV layout {run.Layout.Slot} surface {binding.SourceSurfaceSlot} is " +
                $"{destination.Width}x{destination.Height}; expected " +
                $"{run.Layout.Width}x{run.Layout.Height}.");

        var updated = new RgbaImage(
            destination.Width, destination.Height, (byte[])destination.Pixels.Clone());
        // A presented run always draws with the Unicode rasterizer, even if the language changed meanwhile.
        TextPipeline text = run.Presented != null ? _presentedText.Value : ActiveText;
        string drawnText = run.Presented?.Text ?? run.Text;
        AdvTextStyle style = run.Presented != null ? PresentedStyle(run.Style) : run.Style;
        AspectFontRealization? realization = run.Presented != null ? null : RealizationFor(text);
        IReadOnlyList<GlyphRasterRequest> requests =
            ImmediateSurfaceTextRenderer.CreateRequests(drawnText, style, text.Policy, _textAntialias, realization);
        IReadOnlyList<int>? breaks = recordedLineBreaks;
        int rightBound = recordedLineBreaks == null ? run.Layout.Right : int.MaxValue;
        bool wrap = recordedLineBreaks == null;
        if (run.Presented is { } presented && recordedLineBreaks == null)
        {
            // Presented text breaks where the presenter allows (localization spec §7.1), planned before drawing.
            PresentedTextPlan plan = PresentedTextLayout.Plan(
                drawnText, Ome.Engine.Localization.PresentedLineBreaks.Opportunities(drawnText, presented.LineBreak), requests,
                text.Cache, run.Layout.CursorX, binding.ResetCursorX, run.Layout.Right);
            requests = plan.Glyphs;
            breaks = plan.BreaksBefore;
            rightBound = int.MaxValue;
            wrap = false;
        }
        GlyphTextLayoutResult rendered = text.Layout.Render(
            updated,
            new GlyphTextLayoutOptions(
                run.Layout.CursorX,
                run.Layout.CursorY,
                binding.ResetCursorX,
                rightBound,
                history ? int.MaxValue : run.Layout.Bottom,
                WrapHorizontally: wrap,
                style,
                breaks,
                realization,
                OverflowAtFontHeight: run.Presented != null),
            requests);

        // Op 0x196's ruby follows each base row in AGE's drawing order; History draws its ruby runs after the text.
        IReadOnlyList<AdvRubyGlyph> ruby = [];
        var ordered = new List<AdvRetainedGlyphRecord>(rendered.Records.Count);
        var orderedIsRuby = new List<bool>(rendered.Records.Count);
        void Add(AdvRetainedGlyphRecord record, bool isRuby)
        {
            ordered.Add(record);
            orderedIsRuby.Add(isRuby);
        }
        if (!string.IsNullOrEmpty(run.Ruby) && rendered.Rows.Count > 0)
        {
            ruby = text.Layout.RenderRuby(
                updated, run.Text, rendered.Rows, run.Ruby, run.Style,
                text.Policy, _textAntialias, RubyRules);
            foreach ((AdvRetainedGlyphRecord record, bool isRuby) in OrderedGlyphs(rendered, ruby, byRow: true))
                Add(record, isRuby);
        }
        else
        {
            foreach (AdvRetainedGlyphRecord record in rendered.Records) Add(record, false);
            foreach (AdvTextRubyAnnotation annotation in historyRuby ?? [])
            {
                int firstBase = annotation.BaseFirstGlyph;
                int lastBase = firstBase + annotation.BaseGlyphCount - 1;
                if (annotation.BaseGlyphCount <= 0 || lastBase >= rendered.Records.Count) continue;
                AdvRetainedGlyphRecord left = rendered.Records[firstBase], right = rendered.Records[lastBase];
                foreach (AdvRetainedGlyphRecord record in text.Layout.RenderRubyOver(
                             updated, left.Left, left.Top, right.Right - left.Left, annotation.Text, run.Style,
                             text.Policy, _textAntialias, RubyRules, history: true))
                    Add(record, true);
            }
        }

        // What history records and a save binds (spec §8): the drawn rows when the run drew its source text with the
        // gameplay rasterizer from the source cursor, else a separate source layout pass.
        IReadOnlyList<AdvRetainedGlyphRecord>? sourceGlyphs = null;
        AdvTextSourceLayout? source = !history
                                      && (run.Presented != null || run.SourceCursor != null
                                          || !ReferenceEquals(text, _sourceText))
            ? LayoutSourceText(binding, run, out sourceGlyphs)
            : null;

        _timeline?.Event("adv-text-layout-result", new()
        {
            ["layout"] = binding.LayoutSlot,
            ["text"] = run.Text,
            ["presented"] = run.Presented?.Text,
            ["backend"] = text.Info.Id,
            ["cursor_x"] = run.Layout.CursorX,
            ["cursor_y"] = run.Layout.CursorY,
            ["right"] = run.Layout.Right,
            ["bottom"] = run.Layout.Bottom,
            ["font"] = run.Style.FontFace,
            ["font_size"] = run.Style.PrimaryFontSize,
            ["line_spacing"] = run.Style.LineSpacing,
            ["requested_glyphs"] = requests.Count,
            ["rendered_glyphs"] = rendered.ConsumedGlyphs,
            ["ruby"] = run.Ruby ?? "",
            ["ruby_glyphs"] = orderedIsRuby.Count(isRuby => isRuby),
            ["wrapped_lines"] = rendered.WrappedLines,
            ["vertical_overflow"] = rendered.StoppedOnVerticalOverflow,
            ["result_cursor_x"] = rendered.CursorX,
            ["result_cursor_y"] = rendered.CursorY,
            ["source_cursor_x"] = source?.CursorX,
            ["source_cursor_y"] = source?.CursorY,
        });

        lock (_textLock)
        {
            if (!_retainedTextLayouts.TryGetValue(
                    binding.LayoutSlot, out presentation!))
            {
                presentation = new RetainedAdvTextLayoutPresentation(binding);
                _retainedTextLayouts.Add(binding.LayoutSlot, presentation);
            }
            else if (presentation.Binding != binding)
                throw new InvalidOperationException(
                    $"ADV layout {binding.LayoutSlot} changed its retained binding without reset.");
            int first = presentation.AppendRun(
                ordered,
                orderedIsRuby,
                run.Layout.OriginX,
                run.Layout.OriginY,
                RubyRules.ChainEveryRubyRecord,
                sourceGlyphs);
            _layoutCursors[binding.LayoutSlot] = (rendered.CursorX, rendered.CursorY);
            RebuildRetainedWaitIndicator(binding.LayoutSlot);
            result = new AdvRetainedTextRunResult(
                binding.LayoutSlot,
                first,
                ordered.Count,
                rendered.CursorX,
                rendered.CursorY) { Rows = rendered.Rows, Ruby = ruby, Source = source };
        }

        lock (_imageLock)
            _surfaceImages[binding.SourceSurfaceSlot] = updated;
        _slotDims[binding.SourceSurfaceSlot] = (updated.Width, updated.Height);
        gfx.CreateSurface(binding.SourceSurfaceSlot);
    }

    public void SetAdvTextCursor(int layoutSlot, int x, int y)
    {
        lock (_textLock)
        {
            if (layoutSlot != 0) _currentAdvLayout = layoutSlot;
            int slot = layoutSlot != 0 ? layoutSlot : _currentAdvLayout;
            _layoutCursors[slot] = (x, y);
            RebuildRetainedWaitIndicator(slot);
        }
        _timeline?.Event("text-cursor", new() { ["slot"] = layoutSlot, ["x"] = x, ["y"] = y });
    }

    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text)
        => DrawStringToSurface(surfaceSlot, x, y, text, AdvTextStyle.Default);

    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text, AdvTextStyle style)
        => DrawString(surfaceSlot, x, y, text, style, presented: null);

    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text, AdvTextStyle style,
                                    Ome.Engine.Localization.PresentedText presented)
        => DrawString(surfaceSlot, x, y, text, style, presented);

    private void DrawString(int surfaceSlot, int x, int y, string text, AdvTextStyle style,
                            Ome.Engine.Localization.PresentedText? presented)
    {
        TextPipeline pipeline = presented != null ? _presentedText.Value : ActiveText;
        DrawStringPixels(surfaceSlot, x, y, text, style, pipeline, presented);
        _timeline?.Event("draw-string", new()
        {
            ["surface"] = surfaceSlot,
            ["x"] = x,
            ["y"] = y,
            ["text"] = text,
            ["presented"] = presented?.Text,
            ["presentation"] = "rgba-glyph-mask",
            ["backend"] = pipeline.Info.Id,
        });
    }

    /// <summary>The gap between the rows of a presented <c>draw-string</c> when its entry sets no <c>row_gap</c>
    /// (localization spec §7.1).</summary>
    private const int PresentedRowGap = 2;

    private void DrawStringPixels(
        int surfaceSlot, int x, int y, string text, AdvTextStyle style, TextPipeline pipeline,
        Ome.Engine.Localization.PresentedText? presented)
    {
        RgbaImage? destination = ResolveSurfacePixels(surfaceSlot);
        if (destination == null
            && _slotDims.TryGetValue(surfaceSlot, out var dimensions)
            && dimensions.W > 0
            && dimensions.H > 0)
            destination = new RgbaImage(
                dimensions.W, dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
        if (destination == null || destination.Width <= 0 || destination.Height <= 0)
            throw new InvalidOperationException(
                $"Surface {surfaceSlot} has no rasterizable pixel allocation.");

        var updated = new RgbaImage(
            destination.Width, destination.Height, (byte[])destination.Pixels.Clone());
        if (presented == null)
            pipeline.Pixels.Render(updated, x, y, text, style, _textAntialias, RealizationFor(pipeline), BlankExtentMode);
        else
        {
            // Each "\n" of the presented text starts a row at the same x, lower by the font's height and a gap.
            AdvTextStyle presentedStyle = PresentedStyle(style);
            int height = style.PrimaryFontSize > 0 ? style.PrimaryFontSize : 24;
            int step = height + (presented.Entry?.RowGap ?? PresentedRowGap);
            string[] rows = presented.Text.Split('\n');
            for (int row = 0; row < rows.Length; row++)
            {
                string drawn = new(rows[row].Where(c => c >= ' ').ToArray());
                if (drawn.Length > 0)
                    pipeline.Pixels.Render(updated, x, checked(y + row * step), drawn, presentedStyle, _textAntialias);
            }
        }

        lock (_imageLock) _surfaceImages[surfaceSlot] = updated;
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
    }

    /// <summary>
    /// Layout that owns the ordinary retained ADV page. Nested callback scripts such as HISTORY can select
    /// and mutate other layouts while the parent wait remains parked; those transient selections must not
    /// move the parent page when its retained range is restored.
    /// </summary>
    public int AdvPageLayoutSlot
    {
        get
        {
            lock (_textLock)
                return IsWaiting && _activeWaitLayout != 0 ? _activeWaitLayout : _currentAdvLayout;
        }
    }

    public void ClearRenderedAdvTextLayout(int layoutSlot)
    {
        lock (_textLock)
        {
            int slot = layoutSlot == 0 ? _currentAdvLayout : layoutSlot;
            if (_retainedTextLayouts.ContainsKey(slot)) return;
            _liveText.RemoveAll(state => state.Run.Layout.Slot == slot);
            if (_activeLiveText?.Run.Layout.Slot == slot)
            {
                _activeLiveText = null;
                _advText = "";
                _advTextForceComplete = false;
                IsTextRevealing = false;
            }
        }
    }

    public void ResetRenderedAdvTextLayout(
        GfxState gfx, AdvTextLayoutPresentationBinding binding)
    {
        lock (_textLock)
        {
            _retainedHistoryLayouts.Remove(binding.LayoutSlot);
            _retainedTextLayouts.Remove(binding.LayoutSlot);
            _liveText.RemoveAll(
                state => state.Run.Layout.Slot == binding.LayoutSlot);
            if (_activeLiveText?.Run.Layout.Slot == binding.LayoutSlot)
            {
                _activeLiveText = null;
                _advText = "";
                _advTextForceComplete = false;
                IsTextRevealing = false;
            }
            if (_suspendedRetainedTextLayoutSlot == binding.LayoutSlot)
                _suspendedRetainedTextLayoutSlot = 0;
        }

        if (_slotDims.TryGetValue(
                binding.SourceSurfaceSlot, out var dimensions)
            && dimensions.W > 0
            && dimensions.H > 0)
        {
            lock (_imageLock)
                _surfaceImages[binding.SourceSurfaceSlot] = new RgbaImage(
                    dimensions.W,
                    dimensions.H,
                    new byte[checked(dimensions.W * dimensions.H * 4)]);
        }
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("adv-text-layout-reset", new()
        {
            ["layout"] = binding.LayoutSlot,
            ["surface"] = binding.SourceSurfaceSlot,
            ["first_handle"] = binding.FirstObjectHandle,
            ["capacity"] = binding.ObjectCapacity,
        });
    }

    public bool RenderTextHistory(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvTextHistoryRenderBatch batch)
    {
        // A presented group (localization spec §8) wraps in the target layout as live presented text does.
        var run = new AdvLiveTextRun(
            batch.FirstRecordIndex,
            batch.Layout,
            batch.Style,
            batch.Text,
            Array.Empty<string>()) { Presented = batch.Presented };
        if (binding.LayoutSlot != batch.LayoutSlot
            || binding.FirstObjectHandle < 0
            || binding.ObjectCapacity <= 0)
            throw new InvalidOperationException(
                $"History layout {batch.LayoutSlot} has no retained presentation binding.");
        PrepareRetainedTextRun(
            gfx, binding, run,
            out RetainedAdvTextLayoutPresentation? presentation,
            out AdvRetainedTextRunResult result,
            batch.Presented == null ? batch.RecordedLineBreaks : null,
            batch.Presented == null ? batch.Ruby : null,
            history: true);

        presentation.PublishThrough(
            gfx,
            checked(result.FirstGlyphIndex + result.GlyphCount));
        lock (_textLock)
        {
            _retainedHistoryLayouts.Add(batch.LayoutSlot);
        }
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("history-render", new()
        {
            ["layout"] = batch.LayoutSlot,
            ["record"] = batch.FirstRecordIndex,
            ["x"] = batch.Layout.OriginX + batch.Layout.CursorX,
            ["y"] = batch.Layout.OriginY + batch.Layout.CursorY,
            ["text"] = batch.Text,
            ["presented"] = batch.Presented?.Text,
            ["glyphs"] = result.GlyphCount,
            ["presentation"] = "retained-glyphs",
        });
        return true;
    }

    /// <summary>
    /// Op 0x82 (text_redraw_retained_group@0x451530; SYS4433 @0x4553c0): release the layout's objects, clear its surface
    /// and draw each record at its rectangle. Unless flag 4 is set, the published glyphs are bound again over the new
    /// pixels; native binds one object per record instead, which covers the same cells. The glyph list stays, so the
    /// wait indicator keeps its anchor and later text continues the page. This is not a History presentation, which
    /// History's exit would erase.
    /// </summary>
    public void RedrawRetainedTextGroup(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvTextRedrawBatch batch)
    {
        if (binding.LayoutSlot != batch.LayoutSlot
            || binding.FirstObjectHandle < 0
            || binding.ObjectCapacity <= 0)
            return;
        (int width, int height) = _slotDims.TryGetValue(binding.SourceSurfaceSlot, out var dimensions)
                                  && dimensions.W > 0 && dimensions.H > 0
            ? (dimensions.W, dimensions.H)
            : (batch.Layout.Width, batch.Layout.Height);
        if (width <= 0 || height <= 0) return;

        var updated = new RgbaImage(width, height, new byte[checked(width * height * 4)]);
        TextPipeline text = batch.Presented != null ? _presentedText.Value : ActiveText;
        IReadOnlyList<(AdvRetainedGlyphRecord Cell, bool IsRuby)> cells = batch.Presented is { } presented
            ? RedrawPresented(updated, binding, batch, presented, text)
            : text.Layout.RenderRedraw(
                updated, batch.Records, text.Policy, _textAntialias, RubyRules, RealizationFor(text));

        bool bind = (batch.Flags & 4) == 0;
        RetainedAdvTextLayoutPresentation? presentation;
        lock (_textLock)
        {
            // A layout with no live text yet takes the drawn cells as its glyphs.
            if (!_retainedTextLayouts.TryGetValue(binding.LayoutSlot, out presentation) && bind)
            {
                presentation = new RetainedAdvTextLayoutPresentation(binding);
                _retainedTextLayouts.Add(binding.LayoutSlot, presentation);
                presentation.AppendRun(
                    cells.Select(cell => cell.Cell).ToArray(),
                    cells.Select(cell => cell.IsRuby).ToArray(),
                    batch.Layout.OriginX,
                    batch.Layout.OriginY,
                    RubyRules.ChainEveryRubyRecord);
                RebuildRetainedWaitIndicator(binding.LayoutSlot);
            }
        }

        lock (_imageLock)
            _surfaceImages[binding.SourceSurfaceSlot] = updated;
        _slotDims[binding.SourceSurfaceSlot] = (width, height);
        gfx.CreateSurface(binding.SourceSurfaceSlot);
        gfx.EraseRange(binding.FirstObjectHandle, binding.ObjectCapacity);
        if (bind && presentation != null)
        {
            if (presentation.PublishedGlyphCount > 0) presentation.Republish(gfx);
            else presentation.PublishThrough(gfx, presentation.Glyphs.Count);
        }
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("text-redraw", new()
        {
            ["layout"] = batch.LayoutSlot,
            ["record"] = batch.FirstRecordIndex,
            ["flags"] = batch.Flags,
            ["records"] = batch.Records.Count,
            ["cells"] = cells.Count,
        });
    }

    /// <summary>Op 0x82 for a presented group (localization spec §8): the presented text from the first text record's
    /// top-left in its style, wrapped in the layout as live presented text is.</summary>
    private IReadOnlyList<(AdvRetainedGlyphRecord Cell, bool IsRuby)> RedrawPresented(
        RgbaImage destination, AdvTextLayoutPresentationBinding binding, AdvTextRedrawBatch batch,
        Ome.Engine.Localization.PresentedText presented, TextPipeline text)
    {
        AdvTextRedrawRecord first = batch.Records.First(record => !record.IsRuby);
        AdvTextStyle style = PresentedStyle(first.Style);
        PresentedTextPlan plan = PresentedTextLayout.Plan(
            presented.Text, Ome.Engine.Localization.PresentedLineBreaks.Opportunities(presented.Text, presented.LineBreak),
            ImmediateSurfaceTextRenderer.CreateRequests(presented.Text, style, text.Policy, _textAntialias, null),
            text.Cache, first.Rect.Left, binding.ResetCursorX, batch.Layout.Right);
        GlyphTextLayoutResult drawn = text.Layout.Render(
            destination,
            new GlyphTextLayoutOptions(
                first.Rect.Left, first.Rect.Top, binding.ResetCursorX, int.MaxValue, batch.Layout.Bottom,
                WrapHorizontally: false, style, plan.BreaksBefore, OverflowAtFontHeight: true),
            plan.Glyphs);
        return drawn.Records.Select(record => (record, false)).ToArray();
    }

    public void EndTextHistoryPresentation(GfxState gfx)
    {
        RetainedAdvTextLayoutPresentation[] retained;
        int[] surfaceSlots;
        lock (_textLock)
        {
            retained = _retainedHistoryLayouts
                .Select(slot => _retainedTextLayouts.GetValueOrDefault(slot))
                .Where(presentation => presentation != null)
                .Cast<RetainedAdvTextLayoutPresentation>()
                .ToArray();
            surfaceSlots = retained
                .Select(presentation => presentation.Binding.SourceSurfaceSlot)
                .Distinct()
                .ToArray();
            foreach (int slot in _retainedHistoryLayouts)
                _retainedTextLayouts.Remove(slot);
            _retainedHistoryLayouts.Clear();
        }

        foreach (RetainedAdvTextLayoutPresentation presentation in retained)
            presentation.ErasePublished(gfx);
        foreach (int surfaceSlot in surfaceSlots)
            ClearAllocatedSurfacePixels(surfaceSlot);
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("history-presentation-end", new()
        {
            ["retained_layouts"] = retained.Length,
        });
    }

    private void ClearAllocatedSurfacePixels(int surfaceSlot)
    {
        if (!_slotDims.TryGetValue(surfaceSlot, out var dimensions)
            || dimensions.W <= 0
            || dimensions.H <= 0)
            return;
        lock (_imageLock)
            _surfaceImages[surfaceSlot] = new RgbaImage(
                dimensions.W,
                dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
    }

    public int MessageWindowAlphaSetting => _messageWindowAlphaSetting;

    public void SetMessageWindowAlphaSetting(int value)
    {
        _messageWindowAlphaSetting = value;
        _timeline?.Event("message-window-alpha", new() { ["value"] = value });
    }

    public void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config)
    {
        lock (_textLock)
        {
            _waitIndicators[config.LayoutSlot] = config;
            RebuildRetainedWaitIndicator(config.LayoutSlot);
        }
        _timeline?.Event("wait-indicator-config", new()
        {
            ["layout"] = config.LayoutSlot, ["x"] = config.X, ["y"] = config.Y,
            ["surface"] = config.SurfaceSlot, ["cell_w"] = config.CellWidth,
            ["cell_h"] = config.CellHeight, ["atlas_columns"] = config.AtlasColumns,
            ["terminal_frame"] = config.TerminalFrame,
            ["period_ms"] = config.FramePeriodMs,
        });
    }

    public void SetAdvWaitIndicatorFollowLastGlyph(bool enabled)
    {
        lock (_textLock)
        {
            _waitIndicatorFollowLastGlyph = enabled;
            foreach (int layoutSlot in _waitIndicators.Keys.ToArray())
                RebuildRetainedWaitIndicator(layoutSlot);
        }
        _timeline?.Event("wait-indicator-follow-last-glyph", new() { ["enabled"] = enabled });
    }

    public void BindAdvWaitIndicator(
        AdvTextLayoutPresentationBinding binding,
        AdvTextLayoutSnapshot layout)
    {
        lock (_textLock)
        {
            _waitIndicatorBindings[binding.LayoutSlot] = (binding, layout);
            RebuildRetainedWaitIndicator(binding.LayoutSlot);
        }
        _timeline?.Event("wait-indicator-bind", new()
        {
            ["layout"] = binding.LayoutSlot,
            ["handle"] = binding.WaitIndicatorObjectHandle,
            ["origin_x"] = layout.OriginX,
            ["origin_y"] = layout.OriginY,
        });
    }

    private void RebuildRetainedWaitIndicator(int layoutSlot)
    {
        if (!_waitIndicators.TryGetValue(layoutSlot, out AdvWaitIndicatorConfig config)
            || !_waitIndicatorBindings.TryGetValue(
                layoutSlot,
                out (AdvTextLayoutPresentationBinding Binding, AdvTextLayoutSnapshot Layout) retained)
            || retained.Binding.WaitIndicatorObjectHandle < 0)
        {
            _retainedWaitIndicators.Remove(layoutSlot);
            return;
        }

        // SYS4433's indicator draw (0x452730) in follow mode adds the layout's last 20-byte record's left/top, which is
        // the pending record at the cursor, just past the last glyph.
        int anchorX = 0;
        int anchorY = 0;
        if (_waitIndicatorFollowLastGlyph && _layoutCursors.TryGetValue(layoutSlot, out var cursor))
            (anchorX, anchorY) = cursor;
        _retainedWaitIndicators[layoutSlot] =
            new RetainedAdvWaitIndicatorPresentation(
                config,
                retained.Binding,
                retained.Layout.OriginX,
                retained.Layout.OriginY,
                anchorX,
                anchorY);
    }

    public void SetAdvWaitIndicatorEnabled(bool enabled)
    {
        lock (_textLock)
        {
            if (enabled && !_waitIndicatorEnabled) _waitIndicatorStartedMs = _clock.NowMs;
            _waitIndicatorEnabled = enabled;
        }
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("wait-indicator-enabled", new() { ["enabled"] = enabled });
    }

    public void PublishAdvTextLayout(int layoutSlot)
    {
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("adv-text-layout-publish", new() { ["layout"] = layoutSlot });
    }

    public void PublishAdvTextLayout(
        GfxState gfx, AdvTextLayoutPresentationBinding binding)
    {
        lock (_textLock)
        {
            if (_retainedTextLayouts.TryGetValue(
                    binding.LayoutSlot, out RetainedAdvTextLayoutPresentation? presentation)
                && presentation.Binding == binding)
                presentation.Republish(gfx);
            if (_retainedWaitIndicators.TryGetValue(
                    binding.LayoutSlot,
                    out RetainedAdvWaitIndicatorPresentation? indicator))
                indicator.Republish(
                    gfx,
                    _clock.NowMs - _waitIndicatorStartedMs,
                    WaitIndicatorShouldBeVisible(binding.LayoutSlot));
        }
        PublishAdvTextLayout(binding.LayoutSlot);
    }

    private bool WaitIndicatorShouldBeVisible(int layoutSlot)
        => IsWaiting
           && !_advPagePresentationSuspended
           && _waitIndicatorEnabled
           && _activeWaitLayout == layoutSlot;

    private void UpdateRetainedWaitIndicator(GfxState gfx)
    {
        lock (_textLock)
        {
            foreach ((int layoutSlot, RetainedAdvWaitIndicatorPresentation presentation)
                     in _retainedWaitIndicators)
                presentation.Update(
                    gfx,
                    _clock.NowMs - _waitIndicatorStartedMs,
                    WaitIndicatorShouldBeVisible(layoutSlot));
        }
    }

}

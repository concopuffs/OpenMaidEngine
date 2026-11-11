namespace Ome.Engine.Model;

[Flags]
public enum AdvTextHistoryRecordFlags : uint
{
    None = 0,
    GroupStart = 0x00000001,
    NavigationFiltered = 0x00000002,
    /// <summary>One ruby character drawn over the record before it (adv_text_draw_ruby_over_row@0x456b40 appends one
    /// per character, flags | 4). History draws a run of these with the ruby font.</summary>
    Ruby = 0x00000004,
    /// <summary>An empty record that moves the redraw cursor to the next line: op 0x6f and every
    /// horizontal wrap (adv_text_append_line_break_horizontal@0x456fd0).</summary>
    LineBreak = 0x00000008,
    TypedMetadata = 0x20000000,
    VoicePair = 0x40000000,
}

public enum AdvTextHistoryRecordKind
{
    Text,
    Metadata,
    Voice,
}

/// <summary>The text raster state copied into each native retained text record.</summary>
public readonly record struct AdvTextStyle(
    int PrimaryFontSize,
    int RubyFontSize,
    bool Bold,
    long TextColor,
    long EffectColor,
    int RenderMode,
    int EffectOffsetX,
    int EffectOffsetY,
    int LineSpacing,
    string FontFace)
{
    /// <summary>
    /// SYS4433 glyph aspect/metric mode selected by opcode 0x2db. Mode zero is the native default;
    /// Kamidori selects mode one during SYSTEM4 startup.
    /// </summary>
    public int AspectMode { get; init; }

    public static AdvTextStyle Default => new(0, 0, false, 0, 0, 0, 0, 0, 6, "");
}

/// <summary>A stable snapshot of the layout state associated with a retained record.</summary>
public readonly record struct AdvTextLayoutSnapshot(
    int Slot,
    int Width,
    int Height,
    int OriginX,
    int OriginY,
    int CursorX,
    int CursorY,
    int Right,
    int Bottom);

/// <summary>
/// One live show-text run with the layout and raster state captured when the opcode executes.
/// Unlike <see cref="AdvTextHistoryRecord"/>, this presentation record exists even while History
/// backlog recording is suppressed.
/// </summary>
public sealed record AdvLiveTextRun(
    int SourceOffset,
    AdvTextLayoutSnapshot Layout,
    AdvTextStyle Style,
    string Text,
    IReadOnlyList<string> ScriptStack)
{
    /// <summary>Op 0x196's ruby, drawn over <see cref="Text"/> (null for show-text).</summary>
    public string? Ruby { get; init; }

    /// <summary>Whether a timed reveal of this run restarts the glyph timer (<c>ctx+0x5f630</c>). Op 0x6e arms it;
    /// op 0x196 does not and keeps the previous run's phase (docs/engine-re.md, "The reveal clock").</summary>
    public bool ArmsGlyphTimer { get; init; } = true;

    /// <summary>What the presenter gives for <see cref="Text"/> (localization spec §5.2), or null to draw the source
    /// text. The host draws this, wrapped at its break opportunities, with no ruby; history, the page map and saves
    /// keep <see cref="Text"/>.</summary>
    public Localization.PresentedText? Presented { get; init; }

    public bool BelongsToScript(string scriptName)
    {
        foreach (string frame in ScriptStack)
            if (string.Equals(frame, scriptName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

/// <summary>
/// One semantic counterpart of AGE's 0x48-byte retained text record. Metadata uses
/// <see cref="Value"/> plus <see cref="AuxValue"/> as value/type; voice uses them as id/argument.
/// </summary>
public sealed record AdvTextHistoryRecord(
    AdvTextHistoryRecordKind Kind,
    AdvTextHistoryRecordFlags Flags,
    AdvTextLayoutSnapshot Layout,
    AdvTextStyle Style,
    string Text,
    long Value,
    long AuxValue,
    int SourceOffset)
{
    /// <summary>A text row's drawn rectangle edges (record +0x04..+0x10); zero for line breaks,
    /// metadata and voice records.</summary>
    public AdvTextHistoryRect Rect { get; init; }

    /// <summary>Text record +0x1c, copied from text manager +0x554 (unidentified; zero in every observed
    /// save). Kept so a native save's records round-trip.</summary>
    public int TextManager554 { get; init; }
}

/// <summary>Rectangle edges (not width/height) of one recorded text row.</summary>
public readonly record struct AdvTextHistoryRect(int Left, int Top, int Right, int Bottom);

/// <summary>AGE's logical 8-byte history index entry.</summary>
public readonly record struct AdvTextHistoryEntry(int LayoutSlot, int FirstRecordIndex);

/// <summary>The host-facing result of rendering one retained history group into a target ADV layout.</summary>
public sealed record AdvTextHistoryRenderBatch(
    int LayoutSlot,
    int FirstRecordIndex,
    int Flags,
    AdvTextLayoutSnapshot Layout,
    string Text,
    AdvTextStyle Style)
{
    /// <summary>
    /// Set for op 0x1d1: the rows break only here, never at the layout's right edge. Each entry is a glyph
    /// (Unicode scalar) index into <see cref="Text"/> where a retained line-break record starts a new row at
    /// the layout's reset x, <see cref="Style"/>'s font height plus line spacing lower. Null wraps at the
    /// layout's edges as live text does.
    /// </summary>
    public IReadOnlyList<int>? RecordedLineBreaks { get; init; }

    /// <summary>The group's ruby runs (op 0x1d1 only), each over the text record drawn before it.</summary>
    public IReadOnlyList<AdvTextRubyAnnotation> Ruby { get; init; } = [];
}

/// <summary>A ruby drawn over glyphs <see cref="BaseFirstGlyph"/>..+<see cref="BaseGlyphCount"/> of a History
/// batch's text (text_history_render_records@0x4526c0: consecutive flag-4 records over the preceding record).</summary>
public readonly record struct AdvTextRubyAnnotation(int BaseFirstGlyph, int BaseGlyphCount, string Text);

/// <summary>One retained record that op 0x82 draws again: <see cref="Text"/> from the cell at <see cref="Rect"/>'s
/// top-left, in <see cref="Style"/>. A ruby record is one ruby character, drawn plain with the ruby font.</summary>
public readonly record struct AdvTextRedrawRecord(string Text, AdvTextHistoryRect Rect, AdvTextStyle Style, bool IsRuby);

/// <summary>Op 0x82's redraw of one retained group into its own layout (text_redraw_retained_group@0x451530; SYS4433
/// @0x4553c0). The host releases the layout's objects, clears its surface, draws <see cref="Records"/> and, unless
/// flag 4 is set, binds the objects again.</summary>
public sealed record AdvTextRedrawBatch(
    int LayoutSlot,
    int FirstRecordIndex,
    int Flags,
    AdvTextLayoutSnapshot Layout,
    IReadOnlyList<AdvTextRedrawRecord> Records);

/// <summary>
/// Engine-owned retained ADV backlog. Native numbered saves serialize this same model; live HISTORY.BIN
/// reads it directly, so restored history is immediately available to the script UI.
/// </summary>
public sealed class AdvTextHistory
{
    private sealed class LayoutState
    {
        public int Width;
        public int Height;
        public int OriginX;
        public int OriginY;
        public int ResetCursorX;
        public int ResetCursorY;
        public int CursorX;
        public int CursorY;
        public int Right;
        public int Bottom;
        public long WaitIndicatorObjectHandle = -1;
        public long TextObjectRangeFirst = -1;
        public long TextObjectRangeCount;
    }

    private readonly List<AdvTextHistoryRecord> _records = new();
    private readonly List<AdvTextHistoryEntry> _entries = new();
    private readonly Dictionary<int, LayoutState> _layouts = new();
    private readonly HashSet<int> _pendingGroupStarts = new();
    private int _navigationAnchorIndex = -1;

    public IReadOnlyList<AdvTextHistoryRecord> Records => _records;
    public IReadOnlyList<AdvTextHistoryEntry> Entries => _entries;
    public bool RecordingSuppressed { get; private set; }
    public int CurrentLayoutSlot { get; private set; }

    public void SetRecordingEnabled(bool enabled) => RecordingSuppressed = !enabled;

    public void DefineLayout(int requestedSlot, int width, int height, int originX, int originY)
    {
        int slot = SelectLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.Width = width;
        layout.Height = height;
        layout.OriginX = originX;
        layout.OriginY = originY;
        layout.Right = width;
        layout.Bottom = height;
        AppendBoundary(slot);
    }

    public void ResetLayout(int requestedSlot)
    {
        int slot = SelectLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.CursorX = layout.ResetCursorX;
        layout.CursorY = layout.ResetCursorY;
        AppendBoundary(slot);
    }

    public void SetResetCursor(int requestedSlot, int x, int y)
    {
        int slot = ResolveLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.ResetCursorX = x;
        layout.ResetCursorY = y;
    }

    public void SetBounds(int requestedSlot, int right, int bottom)
    {
        int slot = ResolveLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.Right = right;
        layout.Bottom = bottom;
    }

    public void SetCursor(int requestedSlot, int x, int y)
    {
        int slot = ResolveLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.CursorX = x;
        layout.CursorY = y;
    }

    public void SetLayoutOrigin(int requestedSlot, int x, int y)
    {
        int slot = ResolveLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.OriginX = x;
        layout.OriginY = y;
    }

    public void SetWaitIndicatorObjectHandle(int requestedSlot, long handle)
    {
        int slot = ResolveLayout(requestedSlot);
        GetOrCreateLayout(slot).WaitIndicatorObjectHandle = handle;
    }

    public void SetTextObjectRange(int requestedSlot, long firstHandle, long count)
    {
        int slot = ResolveLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        layout.TextObjectRangeFirst = firstHandle;
        layout.TextObjectRangeCount = count;
    }

    public AdvTextLayoutPresentationBinding GetPresentationBinding(int requestedSlot)
    {
        int slot = ResolveLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        return new AdvTextLayoutPresentationBinding(
            slot, checked(slot + 0x14), layout.TextObjectRangeFirst,
            layout.TextObjectRangeCount, layout.WaitIndicatorObjectHandle,
            layout.ResetCursorX, layout.ResetCursorY);
    }

    /// <summary>
    /// Resolve layouts whose complete native retained-glyph handle interval is covered by one op-0x1f7
    /// erase. Hosts use this notification to discard fallback metadata or preserve exact transient records
    /// for a following native op-0x20a republication, according to the active presentation path.
    /// </summary>
    public IReadOnlyList<int> LayoutsCoveredByTextObjectErase(long firstHandle, long count)
    {
        long eraseCount = count > 1 ? count : 1;
        var slots = new List<int>();
        foreach (var pair in _layouts)
        {
            var layout = pair.Value;
            if (RangeCovers(firstHandle, eraseCount,
                            layout.TextObjectRangeFirst, layout.TextObjectRangeCount))
                slots.Add(pair.Key);
        }
        return slots;
    }

    private static bool RangeCovers(long first, long count, long targetFirst, long targetCount)
    {
        if (count <= 0 || targetCount <= 0 || targetFirst < first) return false;
        ulong offset = (ulong)(targetFirst - first);
        ulong extent = (ulong)count;
        return offset <= extent && (ulong)targetCount <= extent - offset;
    }

    /// <summary>
    /// Advance to the next horizontal text line. Native resets x to the layout's configured reset cursor
    /// and adds the primary font's pixel height plus the current extra line spacing to y.
    /// </summary>
    /// <summary>Op 0x6f: a line break record, and the cursor to the start of the next line. Without
    /// <paramref name="moveCursor"/> only the record is written: the break is inside a presented message, which
    /// draws its own breaks (localization spec §6.2).</summary>
    public void EndLine(int requestedSlot, AdvTextStyle style, bool moveCursor = true)
    {
        int slot = ResolveLayout(requestedSlot);
        var layout = GetOrCreateLayout(slot);
        int fontHeight = style.PrimaryFontSize > 0 ? style.PrimaryFontSize : 24;
        if (moveCursor)
        {
            layout.CursorX = layout.ResetCursorX;
            layout.CursorY += fontHeight + style.LineSpacing;
        }
        AppendLineBreak(slot, style);
    }

    private void AppendLineBreak(int slot, AdvTextStyle style)
        => AppendRecord(slot, AdvTextHistoryRecordKind.Text, AdvTextHistoryRecordFlags.LineBreak,
                        style, "", 0, 0, -1);

    public AdvTextLayoutSnapshot GetLayoutSnapshot(int requestedSlot)
        => SnapshotLayout(ResolveLayout(requestedSlot));

    /// <summary>
    /// Record one show-text run the way adv_text_build_glyph_records@0x4576c0 does: one record per drawn
    /// row with the row's rectangle, and a line-break record after every horizontal wrap. Without
    /// <paramref name="rows"/> (a host that did not lay the text out) the run is one row at the cursor.
    /// An op-0x196 run's <paramref name="ruby"/> characters follow their row's record, one record each
    /// (adv_text_draw_ruby_over_row@0x456b40: flags | 4, the character's rectangle, the ruby pixel height, render mode
    /// 0), before the row's line break.
    /// </summary>
    public void AppendText(int requestedSlot, int sourceOffset, string text, AdvTextStyle style,
                           AdvTextHistoryRecordFlags flags = AdvTextHistoryRecordFlags.None,
                           IReadOnlyList<AdvTextRow>? rows = null,
                           IReadOnlyList<AdvRubyGlyph>? ruby = null)
    {
        int slot = ResolveLayout(requestedSlot);
        if (rows is not { Count: > 0 })
        {
            var layout = GetOrCreateLayout(slot);
            int height = style.PrimaryFontSize > 0 ? style.PrimaryFontSize : 24;
            AppendRecord(slot, AdvTextHistoryRecordKind.Text, flags, style, text, 0, 0, sourceOffset,
                new AdvTextHistoryRect(layout.CursorX, layout.CursorY, layout.CursorX, layout.CursorY + height));
            return;
        }

        // Rows count glyphs, one per Unicode scalar; map them back to UTF-16 positions.
        var starts = new List<int>(text.Length + 1);
        for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) starts.Add(i);
        starts.Add(text.Length);
        int Position(int glyph) => starts[Math.Clamp(glyph, 0, starts.Count - 1)];
        AdvTextStyle rubyStyle = style with
        {
            PrimaryFontSize = style.RubyFontSize > 0 ? style.RubyFontSize : Text.RubyTextLayout.DefaultSize,
            RenderMode = 0,
        };
        for (int index = 0; index < rows.Count; index++)
        {
            AdvTextRow row = rows[index];
            int from = Position(row.FirstGlyph), to = Position(row.FirstGlyph + row.GlyphCount);
            AppendRecord(slot, AdvTextHistoryRecordKind.Text, flags, style, text[from..to], 0, 0, sourceOffset,
                new AdvTextHistoryRect(row.Left, row.Top, row.Right, row.Bottom));
            foreach (AdvRubyGlyph glyph in ruby ?? [])
                if (glyph.Row == index)
                    AppendRecord(slot, AdvTextHistoryRecordKind.Text, flags | AdvTextHistoryRecordFlags.Ruby,
                        rubyStyle, glyph.Text, 0, 0, sourceOffset,
                        new AdvTextHistoryRect(glyph.Record.Left, glyph.Record.Top, glyph.Record.Right,
                                               glyph.Record.Bottom));
            if (row.WrapsAfter) AppendLineBreak(slot, style);
        }
    }

    public void AppendMetadata(long value, long metadataType, AdvTextStyle style)
        => AppendRecord(CurrentLayoutSlot, AdvTextHistoryRecordKind.Metadata,
                        AdvTextHistoryRecordFlags.TypedMetadata, style, "", value, metadataType, -1);

    public void AppendVoice(long voiceId, long voiceArgument, AdvTextStyle style)
        => AppendRecord(CurrentLayoutSlot, AdvTextHistoryRecordKind.Voice,
                        AdvTextHistoryRecordFlags.VoicePair, style, "", voiceId, voiceArgument, -1);

    /// <summary>Clear the retained records and logical index while keeping reusable layout definitions.</summary>
    public void Clear()
    {
        _records.Clear();
        _entries.Clear();
        _pendingGroupStarts.Clear();
        _navigationAnchorIndex = -1;
    }

    /// <summary>Replace retained history from AGE's numbered-save history tail.</summary>
    public void RestorePersistenceSnapshot(
        IReadOnlyList<AdvTextHistoryEntry> entries,
        IReadOnlyList<AdvTextHistoryRecord> records)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(records);
        _records.Clear();
        _records.AddRange(records);
        _entries.Clear();
        _entries.AddRange(entries);
        _pendingGroupStarts.Clear();
        // Native text_history_deserialize clears and reconstructs only the history entry/record vectors.
        // Live layout definitions, selected layout, surfaces, and retained-object bindings remain owned by
        // the initialized text manager and are rebuilt by the script restore path as needed.
        _navigationAnchorIndex = entries.Count - 1;
    }

    /// <summary>
    /// Resolve a logical entry relative to AGE's latest-boundary navigation anchor. Repeated calls do not
    /// mutate the anchor; HISTORY.BIN supplies cumulative deltas while counting and paging backward.
    /// </summary>
    public bool TryStepGroup(int delta, out AdvTextHistoryEntry entry)
    {
        entry = new AdvTextHistoryEntry(-1, -1);
        if ((uint)_navigationAnchorIndex >= (uint)_entries.Count) return false;

        int index = _navigationAnchorIndex;
        if (delta < 0)
        {
            for (int remaining = -delta; remaining > 0; remaining--)
            {
                int firstRecord = _entries[index].FirstRecordIndex;
                do
                {
                    if (firstRecord == 0 || index < 1) return false;
                    index--;
                }
                while (IsNavigationFiltered(_entries[index])
                       || _entries[index].FirstRecordIndex == firstRecord);
            }
        }
        else
        {
            for (int remaining = delta; remaining > 0; remaining--)
            {
                int firstRecord = _entries[index].FirstRecordIndex;
                do
                {
                    index++;
                    if (index >= _entries.Count) return false;
                    // Native treats the last entry's record offset as the forward sentinel.
                    if (_entries[index].FirstRecordIndex == _entries[^1].FirstRecordIndex) return false;
                }
                while (IsNavigationFiltered(_entries[index])
                       || _entries[index].FirstRecordIndex == firstRecord);
            }
        }

        entry = _entries[index];
        return true;
    }

    /// <summary>
    /// Return the retained entry selected by AGE's ordinary ADV redraw service. The portable runtime does
    /// not currently drive the native direct-history key walker, so its current redraw entry remains the
    /// newest navigation anchor. This is the exact state used by the shipped CONFIG1--CONFIG5 restore path.
    /// </summary>
    public bool TryGetCurrentRedrawEntry(out AdvTextHistoryEntry entry)
    {
        if ((uint)_navigationAnchorIndex < (uint)_entries.Count)
        {
            entry = _entries[_navigationAnchorIndex];
            return true;
        }

        entry = new AdvTextHistoryEntry(-1, -1);
        return false;
    }

    public bool TryFindMetadata(int firstRecordIndex, long metadataType, out long value)
    {
        value = 0;
        bool found = false;
        foreach (var record in EnumerateGroup(firstRecordIndex))
        {
            if (!record.Flags.HasFlag(AdvTextHistoryRecordFlags.TypedMetadata)
                || record.AuxValue != metadataType) continue;
            value = record.Value;
            found = true;
        }
        return found;
    }

    public bool TryFindVoicePair(int firstRecordIndex, out long voiceId, out long voiceArgument)
    {
        voiceId = -1;
        voiceArgument = -1;
        bool found = false;
        foreach (var record in EnumerateGroup(firstRecordIndex))
        {
            if (!record.Flags.HasFlag(AdvTextHistoryRecordFlags.VoicePair)) continue;
            voiceId = record.Value;
            voiceArgument = record.AuxValue;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// Build native op 0x1d1's History rendering of one retained group
    /// (text_history_render_records@0x4526c0). The record supplies only its text and flags: every row draws
    /// with the text manager's current font, colors, effect and leading (<paramref name="currentStyle"/>,
    /// which HISTORY.BIN sets to 22 px before rendering), and flag 2 swaps in the override colors for the
    /// call. Rows start at the target layout's cursor and break only at retained line-break records.
    /// Metadata and voice records are skipped; HISTORY passes flags and colors as zero. A run of ruby (flag 4)
    /// records becomes an annotation over the text record drawn before it, which the host draws with the ruby font.
    /// </summary>
    public bool TryBuildHistoryRenderBatch(int requestedLayoutSlot, int firstRecordIndex, int flags,
                                           long overrideTextColor, long overrideEffectColor,
                                           AdvTextStyle currentStyle, out AdvTextHistoryRenderBatch batch)
    {
        batch = null!;
        if ((uint)firstRecordIndex >= (uint)_records.Count) return false;

        int slot = ResolveLayout(requestedLayoutSlot);
        var text = new System.Text.StringBuilder();
        var lineBreaks = new List<int>();
        var ruby = new List<AdvTextRubyAnnotation>();
        var pendingRuby = new System.Text.StringBuilder();
        int glyphs = 0, baseFirst = -1, baseCount = 0;
        void FlushRuby()
        {
            if (pendingRuby.Length > 0 && baseFirst >= 0)
                ruby.Add(new AdvTextRubyAnnotation(baseFirst, baseCount, pendingRuby.ToString()));
            pendingRuby.Clear();
        }
        foreach (var record in EnumerateGroup(firstRecordIndex))
        {
            if (record.Flags.HasFlag(AdvTextHistoryRecordFlags.NavigationFiltered) && (flags & 1) == 0)
                break;
            if (record.Kind != AdvTextHistoryRecordKind.Text) continue;
            if (record.Flags.HasFlag(AdvTextHistoryRecordFlags.Ruby))
            {
                pendingRuby.Append(record.Text);
                continue;
            }
            FlushRuby();
            if (record.Flags.HasFlag(AdvTextHistoryRecordFlags.LineBreak)) lineBreaks.Add(glyphs);
            int count = 0;
            foreach (System.Text.Rune _ in record.Text.EnumerateRunes()) count++;
            text.Append(record.Text);
            baseFirst = glyphs;
            baseCount = count;
            glyphs += count;
        }
        FlushRuby();

        AdvTextStyle style = (flags & 2) != 0
            ? currentStyle with { TextColor = overrideTextColor, EffectColor = overrideEffectColor }
            : currentStyle;
        batch = new AdvTextHistoryRenderBatch(slot, firstRecordIndex, flags, SnapshotLayout(slot),
                                              text.ToString(), style)
        {
            RecordedLineBreaks = lineBreaks,
            Ruby = ruby,
        };
        return true;
    }

    /// <summary>
    /// Build op 0x82's redraw of one retained group (text_redraw_retained_group@0x451530; SYS4433 @0x4553c0). Native
    /// walks the group from <paramref name="firstRecordIndex"/>, stopping at a navigation-filtered record unless flag 1
    /// is set, and draws each text record at its rectangle. A record keeps its colors (flag 2 puts the operands in their
    /// place) and its pixel height. Face, weight, effect offsets and render mode are the text manager's current
    /// (<paramref name="currentStyle"/>), so the redraw a CONFIG setting change runs restyles the page in place. A ruby
    /// record draws with the current ruby font in render mode 0. Line-break, metadata and voice records draw nothing;
    /// flag 8's voice replay is not modelled.
    /// </summary>
    public bool TryBuildRedrawBatch(int requestedLayoutSlot, int firstRecordIndex, int flags,
                                    long overrideTextColor, long overrideEffectColor, AdvTextStyle currentStyle,
                                    out AdvTextRedrawBatch batch)
    {
        batch = null!;
        if ((uint)firstRecordIndex >= (uint)_records.Count) return false;

        int slot = ResolveLayout(requestedLayoutSlot);
        var records = new List<AdvTextRedrawRecord>();
        foreach (var record in EnumerateGroup(firstRecordIndex))
        {
            if (record.Flags.HasFlag(AdvTextHistoryRecordFlags.NavigationFiltered) && (flags & 1) == 0)
                break;
            if (record.Kind != AdvTextHistoryRecordKind.Text || record.Text.Length == 0) continue;
            bool ruby = record.Flags.HasFlag(AdvTextHistoryRecordFlags.Ruby);
            AdvTextStyle style = currentStyle with
            {
                TextColor = (flags & 2) != 0 ? overrideTextColor : record.Style.TextColor,
                EffectColor = (flags & 2) != 0 ? overrideEffectColor : record.Style.EffectColor,
                PrimaryFontSize = record.Style.PrimaryFontSize,
                RenderMode = ruby ? 0 : currentStyle.RenderMode,
            };
            records.Add(new AdvTextRedrawRecord(record.Text, record.Rect, style, ruby));
        }
        batch = new AdvTextRedrawBatch(slot, firstRecordIndex, flags, SnapshotLayout(slot), records);
        return true;
    }

    private int SelectLayout(int requestedSlot)
    {
        int slot = requestedSlot == 0 ? CurrentLayoutSlot : requestedSlot;
        CurrentLayoutSlot = slot;
        return slot;
    }

    private int ResolveLayout(int requestedSlot) => requestedSlot == 0 ? CurrentLayoutSlot : requestedSlot;

    private LayoutState GetOrCreateLayout(int slot)
    {
        if (!_layouts.TryGetValue(slot, out var layout))
        {
            layout = new LayoutState();
            _layouts.Add(slot, layout);
        }
        return layout;
    }

    private AdvTextLayoutSnapshot SnapshotLayout(int slot)
    {
        var layout = GetOrCreateLayout(slot);
        return new AdvTextLayoutSnapshot(slot, layout.Width, layout.Height,
            layout.OriginX, layout.OriginY, layout.CursorX, layout.CursorY,
            layout.Right, layout.Bottom);
    }

    private void AppendBoundary(int slot)
    {
        if (RecordingSuppressed) return;
        _entries.Add(new AdvTextHistoryEntry(slot, _records.Count));
        _pendingGroupStarts.Add(slot);
        _navigationAnchorIndex = _entries.Count - 1;
    }

    private bool IsNavigationFiltered(AdvTextHistoryEntry entry)
        => (uint)entry.FirstRecordIndex < (uint)_records.Count
           && _records[entry.FirstRecordIndex].Flags.HasFlag(AdvTextHistoryRecordFlags.NavigationFiltered);

    private IEnumerable<AdvTextHistoryRecord> EnumerateGroup(int firstRecordIndex)
    {
        if ((uint)firstRecordIndex >= (uint)_records.Count) yield break;
        for (int i = firstRecordIndex; i < _records.Count; i++)
        {
            if (i > firstRecordIndex
                && _records[i].Flags.HasFlag(AdvTextHistoryRecordFlags.GroupStart)) yield break;
            yield return _records[i];
        }
    }

    private void AppendRecord(int slot, AdvTextHistoryRecordKind kind, AdvTextHistoryRecordFlags flags,
                              AdvTextStyle style, string text, long value, long auxValue, int sourceOffset,
                              AdvTextHistoryRect rect = default)
    {
        if (RecordingSuppressed) return;
        if (_pendingGroupStarts.Remove(slot)) flags |= AdvTextHistoryRecordFlags.GroupStart;

        var layout = GetOrCreateLayout(slot);
        var snapshot = new AdvTextLayoutSnapshot(slot, layout.Width, layout.Height,
            layout.OriginX, layout.OriginY, layout.CursorX, layout.CursorY,
            layout.Right, layout.Bottom);
        _records.Add(new AdvTextHistoryRecord(kind, flags, snapshot, style, text, value, auxValue, sourceOffset)
        {
            Rect = rect,
        });
    }
}

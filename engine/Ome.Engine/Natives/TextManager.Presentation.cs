using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

// The localization presenter (localization spec §4, §5.2) and what it reads: the string stores that scope a drawn string
// to the instruction that stored it, and the History groups presented again in the selected language (§8). The handlers
// ask the text native; they do not hold the presenter (engine layering spec §8, "Localization").
public sealed partial class TextManager
{
    private TextPresenter? _presenter;

    /// <summary>
    /// The selected language's presenter, or null to draw source text: the source language, or a language with no
    /// catalog (gate 1). The handlers ask the text native at each presentation site; everything the engine keeps (cells,
    /// history, saves, the page map) stays source text. The shell replaces it when the language changes; the next run
    /// uses the new one.
    /// </summary>
    public TextPresenter? Presenter
    {
        get => _presenter;
        set => _presenter = value;
    }

    /// <summary>Whether a presenter is selected.</summary>
    public bool Presents => _presenter != null;

    /// <summary>The presented form of <paramref name="text"/> drawn at <paramref name="site"/>, scoped by the
    /// instruction that stored it (<paramref name="origin"/>), or null to draw the source.</summary>
    public PresentedText? Present(string text, TextSite site, TextSite? origin = null, bool logMiss = true)
        => _presenter?.Present(text, site, origin, logMiss);

    /// <summary>The message of several show-text runs that the instruction at <paramref name="instructionIndex"/> of
    /// <paramref name="script"/> belongs to (§6.2), with its index among the message's runs.</summary>
    public bool TryGetMessageRun(Script script, int instructionIndex, out TextMessage message, out int run)
    {
        message = null!;
        run = -1;
        return _presenter?.Messages?.MessagesOf(script) is { } messages
               && messages.TryGetRun(instructionIndex, out message, out run);
    }

    /// <summary>What a length opcode returns for the presented form of <paramref name="text"/> (§5.3), or null for the
    /// native result: no presenter, no entry, or a language or entry that measures source.</summary>
    public int? Measure(string text, TextSite site, TextSite? origin, TextLengthKind kind)
        => _presenter?.Measure(text, site, origin, kind);

    /// <summary>The reveal's delay per glyph for a run: a presented run's is scaled by its language's
    /// <c>glyph_delay_scale</c> (§7.5), and never reaches zero, so a reveal stays a reveal.</summary>
    private int RevealDelay(PresentedText? presented, TextPresenter? presenter)
    {
        int delay = MessageGlyphDelayMilliseconds;
        if (presented == null || presenter == null || delay <= 0) return delay;
        double scale = presenter.Language.GlyphDelayScale;
        return scale == 1.0 ? delay : Math.Max(1, (int)Math.Round(delay * scale));
    }

    // ---- the string stores (§5.2) ----

    /// <summary>The instruction that stored a global string cell's text, and that text.</summary>
    private readonly record struct StringStore(TextSite Origin, string Text);

    private bool _recordsStringStores;

    /// <summary>
    /// Whether every write to a global string cell records its instruction and text (§5.2), so a string drawn far from
    /// where a table stored it is still scoped to the store. On whenever the mounted stack defines a language, selected
    /// or not, so a language switch during play is exact for the boot tables; off with no language in the stack
    /// (gate 1). The records are not saved; op 0x9 zeroes the string bank, and the scene reset drops them with it.
    /// </summary>
    public bool RecordsStringStores
    {
        get => _recordsStringStores;
        set => _recordsStringStores = value;
    }

    /// <summary>The instruction that stored global string cell <paramref name="cell"/>, while the cell still holds
    /// <paramref name="text"/>, the text that instruction wrote; null otherwise, and the record is dropped once another
    /// write changed the cell.</summary>
    public TextSite? OriginOfCell(int cell, string text)
    {
        if (!_scene.StringStores.TryGetValue(cell, out StringStore store)) return null;
        if (string.Equals(text, store.Text, StringComparison.Ordinal)) return store.Origin;
        _scene.StringStores.Remove(cell);
        return null;
    }

    /// <summary>Global string cell <paramref name="cell"/> now holds <paramref name="text"/>, stored by the instruction
    /// at <paramref name="origin"/> (or by the one whose record a copy took over). Nothing while
    /// <see cref="RecordsStringStores"/> is off.</summary>
    public void RecordStringStore(int cell, TextSite origin, string text)
    {
        if (_recordsStringStores) _scene.StringStores[cell] = new StringStore(origin, text);
    }

    // ---- History's groups (ops 0x1d1 and 0x82) ----

    /// <summary>Op 0x1d1: one History group drawn into a target layout and bound, in the text manager's current style,
    /// presented again in the selected language. Flag 4 draws nothing.</summary>
    public void RenderHistoryGroup(GfxState gfx, int layoutSlot, int firstRecordIndex, int flags,
                                   long overrideTextColor, long overrideEffectColor)
    {
        if ((flags & 4) != 0 || !History.TryBuildHistoryRenderBatch(
                layoutSlot, firstRecordIndex, flags, overrideTextColor, overrideEffectColor, Style,
                out AdvTextHistoryRenderBatch batch))
            return;
        if (PresentHistoryGroup(batch.FirstRecordIndex, flags) is { } presented)
            batch = batch with { Presented = presented };
        RenderTextHistory(gfx, History.GetPresentationBinding(batch.LayoutSlot), batch);
    }

    /// <summary>Op 0x82: an ordinary ADV group drawn again at its records' rectangles, presented again in the selected
    /// language when it has text.</summary>
    public void RedrawGroup(GfxState gfx, int layoutSlot, int firstRecordIndex, int flags,
                            long overrideTextColor, long overrideEffectColor)
    {
        if (!History.TryBuildRedrawBatch(
                layoutSlot, firstRecordIndex, flags, overrideTextColor, overrideEffectColor, Style,
                out AdvTextRedrawBatch redraw))
            return;
        if (redraw.Records.Any(record => !record.IsRuby)
            && PresentHistoryGroup(redraw.FirstRecordIndex, flags) is { } presented)
            redraw = redraw with { Presented = presented };
        RedrawRetainedTextGroup(gfx, History.GetPresentationBinding(redraw.LayoutSlot), redraw);
    }

    /// <summary>
    /// A history group presented again for op 0x1d1 or op 0x82 (§8), or null to draw its records as they are. A run or
    /// message this session resolved is presented again from its lookup, so the backlog follows the selected language;
    /// one that missed in the selected language shows its source. Records with no lookup (restored from a save, or
    /// recorded with no language selected), and lookups made in another language that now miss, are matched by their
    /// text against entries of every scope (<see cref="TextHistorySplit"/>). Line-break records outside a matched span
    /// become "\n"; ruby is dropped. Null when nothing in the group is presented.
    /// </summary>
    private PresentedText? PresentHistoryGroup(int firstRecordIndex, int flags)
    {
        if (_presenter is not { } presenter) return null;
        IReadOnlyList<AdvTextHistoryRecord> records = History.Records;
        int end = History.GroupEnd(firstRecordIndex, flags);
        var text = new StringBuilder();
        var unresolved = new List<AdvTextHistoryRecord>();
        bool presentedAny = false;

        void AppendSource(AdvTextHistoryRecord record)
            => text.Append(record.Flags.HasFlag(AdvTextHistoryRecordFlags.LineBreak) ? "\n" : record.Text);

        // Match a stretch of unresolved records by their text; breaks inside one matched span are the entry's own.
        void MatchUnresolved()
        {
            if (unresolved.Count == 0) return;
            var texts = new List<string>();
            foreach (AdvTextHistoryRecord record in unresolved)
                if (!record.Flags.HasFlag(AdvTextHistoryRecordFlags.LineBreak)) texts.Add(record.Text);
            IReadOnlyList<TextHistorySplit.Span> spans = TextHistorySplit.Split(texts, presenter.PresentUnscoped);
            var spanOf = new int[texts.Count];
            for (int span = 0; span < spans.Count; span++)
                for (int at = spans[span].First; at < spans[span].First + spans[span].Count; at++) spanOf[at] = span;
            int next = 0;
            foreach (AdvTextHistoryRecord record in unresolved)
            {
                if (record.Flags.HasFlag(AdvTextHistoryRecordFlags.LineBreak))
                {
                    bool inside = next > 0 && next < texts.Count && spanOf[next - 1] == spanOf[next]
                                  && spans[spanOf[next]].Presented != null;
                    if (!inside) text.Append('\n');
                    continue;
                }
                TextHistorySplit.Span owner = spans[spanOf[next]];
                if (owner.Presented is { } matched)
                {
                    if (owner.First == next)
                    {
                        text.Append(matched.Text);
                        presentedAny = true;
                    }
                }
                else if (presenter.PresentUnscoped(record.Text) is { } filtered)
                {
                    // An unmatched record has no entry, so this is a filter's text (the pseudo-language's, say).
                    text.Append(filtered.Text);
                    presentedAny = true;
                }
                else
                    text.Append(record.Text);
                next++;
            }
            unresolved.Clear();
        }

        for (int index = firstRecordIndex; index < end; index++)
        {
            if (History.TryGetLookupSpan(index, out AdvTextLookupSpan span) && span.EndRecord <= end)
            {
                PresentedText? again = presenter.Present(span.Lookup.Source, span.Lookup.Site, span.Lookup.Origin,
                                                         logMiss: false);
                if (again != null || span.Language == presenter.Language.Tag)
                {
                    MatchUnresolved();
                    if (again != null)
                    {
                        text.Append(again.Text);
                        presentedAny = true;
                    }
                    else
                        for (int record = index; record < span.EndRecord; record++)
                            if (IsPresentableRecord(records[record])) AppendSource(records[record]);
                    index = span.EndRecord - 1;
                    continue;
                }
            }
            if (IsPresentableRecord(records[index])) unresolved.Add(records[index]);
        }
        MatchUnresolved();
        return presentedAny ? new PresentedText(text.ToString(), presenter.Language.LineBreak, null) : null;
    }

    /// <summary>A text record with text, or a line-break record. Ruby, metadata and voice records have no place in a
    /// presented group.</summary>
    private static bool IsPresentableRecord(AdvTextHistoryRecord record)
        => record.Kind == AdvTextHistoryRecordKind.Text
           && !record.Flags.HasFlag(AdvTextHistoryRecordFlags.Ruby)
           && (record.Flags.HasFlag(AdvTextHistoryRecordFlags.LineBreak) || record.Text.Length > 0);
}

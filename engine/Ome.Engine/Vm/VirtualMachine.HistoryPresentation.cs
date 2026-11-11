using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    /// <summary>
    /// A history group presented again for op 0x1d1 or op 0x82 (localization spec §8), or null to draw its records as
    /// they are. A run or message this session resolved is presented again from its lookup, so the backlog follows the
    /// selected language; one that missed in the selected language shows its source. Records with no lookup (restored
    /// from a save, or recorded with no language selected), and lookups made in another language that now miss, are
    /// matched by their text against entries of every scope (<see cref="TextHistorySplit"/>). Line-break records
    /// outside a matched span become "\n"; ruby is dropped. Null when nothing in the group is presented.
    /// </summary>
    private PresentedText? PresentHistoryGroup(int firstRecordIndex, int flags)
    {
        if (_presenter is not { } presenter) return null;
        IReadOnlyList<AdvTextHistoryRecord> records = TextHistory.Records;
        int end = TextHistory.GroupEnd(firstRecordIndex, flags);
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
            if (TextHistory.TryGetLookupSpan(index, out AdvTextLookupSpan span) && span.EndRecord <= end)
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

using System.Text;

namespace Ome.Engine.Localization;

/// <summary>
/// Splits the text records of a history group into consecutive spans that catalog entries match (localization spec
/// §8), for records the presenter did not resolve in this session: a save keeps no script or site, so a loaded group is
/// matched by its text alone. A message always starts at a record boundary, so only boundaries are tried. The split
/// leaves the least text unmatched, and at equal cost has the fewest spans.
/// </summary>
public static class TextHistorySplit
{
    /// <summary>The most records one span joins; a message's rows and runs stay well under it.</summary>
    public const int MaxRecordsPerSpan = 64;

    /// <summary><see cref="Count"/> records from <see cref="First"/>. <see cref="Presented"/> is the entry's text for a
    /// matched span, and null for one unmatched record.</summary>
    public readonly record struct Span(int First, int Count, PresentedText? Presented);

    /// <summary>Split <paramref name="records"/>; a joined span matches when <paramref name="present"/> gives it an
    /// entry's text.</summary>
    public static IReadOnlyList<Span> Split(IReadOnlyList<string> records, Func<string, PresentedText?> present)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(present);
        int count = records.Count;
        // For the first k records: the least unmatched text, the fewest spans at that cost, and the last span.
        var cost = new int[count + 1];
        var spans = new int[count + 1];
        var from = new int[count + 1];
        var matched = new PresentedText?[count + 1];
        var joined = new StringBuilder();
        for (int end = 1; end <= count; end++)
        {
            (cost[end], spans[end], from[end]) = (cost[end - 1] + records[end - 1].Length, spans[end - 1] + 1, end - 1);
            joined.Clear();
            for (int start = end - 1; start >= Math.Max(0, end - MaxRecordsPerSpan); start--)
            {
                joined.Insert(0, records[start]);
                // A match from here cannot beat the best split so far: skip the lookup.
                if (cost[start] > cost[end] || (cost[start] == cost[end] && spans[start] + 1 >= spans[end])) continue;
                if (present(joined.ToString()) is not { Entry: not null } presented) continue;
                (cost[end], spans[end], from[end], matched[end]) = (cost[start], spans[start] + 1, start, presented);
            }
        }
        var result = new List<Span>();
        for (int end = count; end > 0; end = from[end])
            result.Add(new Span(from[end], end - from[end], matched[end]));
        result.Reverse();
        return result;
    }
}

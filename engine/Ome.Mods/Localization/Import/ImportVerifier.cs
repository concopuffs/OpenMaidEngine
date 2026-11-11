using Ome.Engine.Localization;

namespace Ome.Mods.Localization.Import;

/// <summary>
/// <c>import-scripts --verify</c> (localization spec §18.1): presents every compared message and literal through a
/// catalog and compares the result with the translated install's text, ignoring line breaks and runs of spaces. A
/// mismatch means two different translations ended up under one key.
/// </summary>
public static class ImportVerifier
{
    public static VerifyCounts Verify(TextCatalog catalog, IReadOnlyList<TextOccurrence> occurrences)
    {
        var counts = new VerifyCounts();
        var patterns = new Dictionary<(string?, int?, string), TextEntry>();
        foreach (TextEntry entry in catalog.Patterns) patterns[(entry.Script, entry.At, entry.Source)] = entry;

        foreach (TextOccurrence occurrence in occurrences)
        {
            string presented;
            switch (occurrence.Kind)
            {
                case OccurrenceKind.Message:
                case OccurrenceKind.DrawLiteral:
                    presented = catalog.TryResolveExact(occurrence.Key, occurrence.Script, occurrence.Offset,
                        out TextEntry exact) ? exact.TextTemplate.Literal : occurrence.Key;
                    if (occurrence.Kind == OccurrenceKind.Message) counts.CheckedMessages++;
                    else counts.CheckedDrawLiterals++;
                    break;
                case OccurrenceKind.StoredLiteral:
                    // Drawn somewhere else, so only a global entry can apply to it.
                    presented = catalog.TryResolveExact(occurrence.Key, null, null, out TextEntry stored)
                        ? stored.TextTemplate.Literal : occurrence.Key;
                    counts.CheckedStoredLiterals++;
                    if (Same(presented, occurrence.Translation)) continue;
                    if (occurrence.Translation == occurrence.Key)
                        counts.StoredLiteralsTranslatedAnyway.Add(Mismatch(occurrence, presented));
                    else counts.StoredLiteralConflicts.Add(Mismatch(occurrence, presented));
                    continue;
                case OccurrenceKind.Pattern:
                    // Matching values against a pattern is the presenter's (milestone 2); here the entry that
                    // the message's own key selects must carry the translation.
                    presented = (patterns.GetValueOrDefault((occurrence.Script, occurrence.Offset, occurrence.Key))
                                 ?? patterns.GetValueOrDefault((occurrence.Script, null, occurrence.Key))
                                 ?? patterns.GetValueOrDefault((null, null, occurrence.Key)))?.Text
                                ?? occurrence.Key;
                    counts.CheckedPatterns++;
                    break;
                default:
                    continue;
            }
            if (!Same(presented, occurrence.Translation)) counts.Mismatches.Add(Mismatch(occurrence, presented));
        }
        return counts;
    }

    /// <summary>Equal once every run of whitespace, line breaks included, counts as one space.</summary>
    public static bool Same(string a, string b) => Normalize(a) == Normalize(b);

    private static string Normalize(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static ReportedMismatch Mismatch(TextOccurrence occurrence, string presented)
        => new(occurrence.Script, occurrence.Offset, occurrence.Kind.ToString(), occurrence.Key,
            occurrence.Translation, presented);
}

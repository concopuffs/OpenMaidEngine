using Ome.Engine.Localization;

namespace Ome.Mods.Localization.Import;

/// <summary>
/// <c>import-scripts --verify</c> (localization spec §18.1): presents every compared message and literal through a
/// catalog and compares the result with the translated install's text as the importer read it: wraps joined, and
/// deliberate breaks kept as <c>\n</c>. A mismatch means two different translations ended up under one key.
/// A pattern is also checked by value (R4 of <c>docs/localization-import-test.md</c>'s review): its source is
/// filled with sample values and presented by the port's own <see cref="TextPresenter"/> (spec §6.3, §6.4), and the
/// result must be the translation filled with the same values.
/// </summary>
public static class ImportVerifier
{
    public static VerifyCounts Verify(TextCatalog catalog, IReadOnlyList<TextOccurrence> occurrences)
    {
        var counts = new VerifyCounts();
        var patterns = new Dictionary<(string?, int?, string), TextEntry>();
        foreach (TextEntry entry in catalog.Patterns) patterns[(entry.Script, entry.At, entry.Source)] = entry;
        var presenter = new TextPresenter(new TextLanguage(catalog.Language, [catalog]));

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
                case OccurrenceKind.FrameLiteral:
                    // A stored literal is presented with its storing instruction as its origin (review R2), so it is
                    // checked there; the drawing site's entries, which can also apply, are not known here. Nothing
                    // records a store into a frame local, so only a global entry applies to that one.
                    bool recorded = occurrence.Kind == OccurrenceKind.StoredLiteral;
                    presented = catalog.TryResolveExact(occurrence.Key, recorded ? occurrence.Script : null,
                        recorded ? occurrence.Offset : null, out TextEntry stored)
                        ? stored.TextTemplate.Literal : occurrence.Key;
                    counts.CheckedStoredLiterals++;
                    if (Same(presented, occurrence.Translation)) continue;
                    if (occurrence.Translation == occurrence.Key)
                        counts.StoredLiteralsTranslatedAnyway.Add(Mismatch(occurrence, presented));
                    else if (recorded) counts.Mismatches.Add(Mismatch(occurrence, presented));
                    else counts.StoredLiteralConflicts.Add(Mismatch(occurrence, presented));
                    continue;
                case OccurrenceKind.Composed
                    when TextTemplate.Parse(occurrence.Key, allowKinds: true) is { HasPlaceholders: false } fixedText:
                    // Literals only: the composed string is always the same, so its entry is an exact one.
                    presented = catalog.TryResolveExact(fixedText.Literal, occurrence.Script, occurrence.Offset,
                        out TextEntry composed) ? composed.Text : occurrence.Key;
                    counts.CheckedComposed++;
                    break;
                case OccurrenceKind.Pattern:
                case OccurrenceKind.Composed:
                    // The entry the occurrence's own key selects must carry the translation, and the catalog's
                    // patterns must present the source filled with sample values as the translation.
                    presented = (patterns.GetValueOrDefault((occurrence.Script, occurrence.Offset, occurrence.Key))
                                 ?? patterns.GetValueOrDefault((occurrence.Script, null, occurrence.Key))
                                 ?? patterns.GetValueOrDefault((null, null, occurrence.Key)))?.Text
                                ?? occurrence.Key;
                    if (occurrence.Kind == OccurrenceKind.Pattern) counts.CheckedPatterns++;
                    else counts.CheckedComposed++;
                    if (Same(presented, occurrence.Translation)
                        && !BySample(presenter, occurrence, out string sampled, out string expected))
                    {
                        counts.Mismatches.Add(new ReportedMismatch(occurrence.Script, occurrence.Offset,
                            occurrence.Kind + " by sample", occurrence.Key, expected, sampled));
                        continue;
                    }
                    break;
                default:
                    continue;
            }
            if (!Same(presented, occurrence.Translation)) counts.Mismatches.Add(Mismatch(occurrence, presented));
        }
        return counts;
    }

    /// <summary>Equal as written. Both sides carry the importer's reading of the line breaks, so nothing is
    /// ignored.</summary>
    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static ReportedMismatch Mismatch(TextOccurrence occurrence, string presented)
        => new(occurrence.Script, occurrence.Offset, occurrence.Kind.ToString(), occurrence.Key,
            occurrence.Translation, presented);

    // ---- by sample -----------------------------------------------------------------------------------------------

    private static bool BySample(TextPresenter presenter, TextOccurrence occurrence, out string presented,
                                 out string expected)
    {
        string drawn = Fill(occurrence.Key, occurrence.Key, kinds: true);
        presented = presenter.Present(drawn, new TextSite(occurrence.Script, occurrence.Offset))?.Text ?? drawn;
        expected = Fill(occurrence.Translation, occurrence.Key, kinds: false);
        return Same(presented, expected);
    }

    /// <summary>A template with each placeholder replaced by a sample value: a number for <c>num</c>, otherwise a
    /// marked name. The kinds come from <paramref name="source"/>.</summary>
    private static string Fill(string template, string source, bool kinds)
    {
        TextTemplate sourceTemplate = TextTemplate.Parse(source, allowKinds: true);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int k = 0; k < sourceTemplate.Placeholders.Count; k++)
        {
            PlaceholderPart placeholder = sourceTemplate.Placeholders[k];
            values[placeholder.Name] = placeholder.Kind == PlaceholderKind.Number ? (12 + k).ToString() : $"«{k + 1}»";
        }
        return TextTemplate.Parse(template, allowKinds: kinds).Render(values);
    }
}

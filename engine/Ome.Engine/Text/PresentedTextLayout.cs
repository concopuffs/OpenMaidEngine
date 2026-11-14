namespace Ome.Engine.Text;

/// <summary>The glyphs of presented text to draw, and the glyph indices (into <see cref="Glyphs"/>) before which a
/// row starts: <see cref="GlyphTextLayoutOptions.LineBreaksBefore"/>.</summary>
public sealed record PresentedTextPlan(IReadOnlyList<GlyphRasterRequest> Glyphs, IReadOnlyList<int> BreaksBefore);

/// <summary>
/// Word wrap for presented text (localization spec §7.1). Source text wraps wherever a glyph crosses the right bound;
/// presented text breaks at the last opportunity the presenter marked before the bound, drops the spaces at that
/// break, and breaks at every <c>\n</c>. A word longer than a row breaks at the bound, as source text does. A run that
/// begins in the middle of a row (after a name, or after an earlier run) may also break before its first glyph: its
/// start is the boundary of a word. Every glyph takes the room the layout that draws it gives it
/// (<see cref="RetainedGlyphLayoutEngine.Room"/>), aspect mode's cells included, so the plan is drawn by
/// <see cref="RetainedGlyphLayoutEngine.Render"/> with these breaks and no horizontal wrap of its own.
/// </summary>
public static class PresentedTextLayout
{
    /// <param name="text">The presented text; <paramref name="requests"/> holds one request per code point of it.</param>
    /// <param name="opportunities">Per UTF-16 index, whether a row may start there
    /// (<see cref="Localization.PresentedLineBreaks.Opportunities"/>).</param>
    /// <param name="cursorX">Where the run starts; a row other than the first starts at
    /// <paramref name="lineOriginX"/>.</param>
    public static PresentedTextPlan Plan(string text, bool[] opportunities, IReadOnlyList<GlyphRasterRequest> requests,
                                         GlyphRoom room, int cursorX, int lineOriginX, int rightBound)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(opportunities);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(room);
        int count = requests.Count;
        var charIndex = new int[count];
        for (int i = 0, glyph = 0; i < text.Length && glyph < count; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            charIndex[glyph++] = i;

        var dropped = new bool[count];
        var advance = new int[count];
        var breaks = new List<int>();
        int x = cursorX, rowStart = 0, lastOpportunity = -1;
        // The run's start is an opportunity in its first row when the run begins after the row's origin.
        bool startOpens = cursorX > lineOriginX;

        bool IsSpace(int glyph) => PresentedTextPlacement.IsSpace(requests[glyph].UnicodeScalar);
        bool Opens(int glyph) => glyph > rowStart ? opportunities[charIndex[glyph]]
            : glyph == 0 && startOpens && breaks.Count == 0;
        void DropSpacesBefore(int glyph)
        {
            for (int j = glyph - 1; j >= rowStart && (dropped[j] || IsSpace(j)); j--) dropped[j] = true;
        }

        for (int g = 0; g < count; g++)
        {
            if (requests[g].UnicodeScalar == '\n')
            {
                dropped[g] = true;
                DropSpacesBefore(g);
                breaks.Add(g + 1);
                x = lineOriginX;
                rowStart = g + 1;
                lastOpportunity = -1;
                continue;
            }
            if (Opens(g)) lastOpportunity = g;
            (int glyphAdvance, int width) = room.Of(requests[g]);
            advance[g] = glyphAdvance;
            while (!IsSpace(g) && x + width > rightBound)
            {
                // The last opportunity in the row, else the bound itself.
                int k = lastOpportunity >= rowStart && lastOpportunity >= 0 ? lastOpportunity : g;
                if (k == rowStart && !(k == 0 && startOpens && breaks.Count == 0)) break;
                DropSpacesBefore(k);
                breaks.Add(k);
                rowStart = k;
                for (int j = k; j < g && IsSpace(j); j++) dropped[j] = true;
                x = lineOriginX;
                lastOpportunity = -1;
                for (int j = k; j < g; j++)
                {
                    if (!dropped[j]) x += advance[j];
                    if (j > rowStart && opportunities[charIndex[j]]) lastOpportunity = j;
                }
                if (g > rowStart && opportunities[charIndex[g]]) lastOpportunity = g;
            }
            x += advance[g];
        }

        var glyphs = new List<GlyphRasterRequest>(count);
        var keptBefore = new int[count + 1];
        for (int g = 0; g < count; g++)
        {
            keptBefore[g] = glyphs.Count;
            if (!dropped[g]) glyphs.Add(requests[g]);
        }
        keptBefore[count] = glyphs.Count;
        return new PresentedTextPlan(glyphs, breaks.Select(index => keptBefore[index]).ToArray());
    }
}

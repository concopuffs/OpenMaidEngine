namespace Ome.Engine.Localization;

/// <summary>Two patterns of equal rank that one string can match (localization spec §6.4).</summary>
public sealed record AmbiguousPatterns(TextEntry First, TextEntry Second);

/// <summary>
/// Finds ambiguous patterns (localization spec §6.4: "two patterns of equal rank that can match one string fail
/// <c>omp text check</c>"). Equal rank is one scope and as many literal characters. Whether some string matches both
/// is decided exactly, by searching the pair of patterns' automata for a common accepted string: a literal
/// character, any character (<c>{name}</c>), a digit or a minus sign (<c>{name:num}</c>) are the only steps, so the
/// search is over at most the product of the two patterns' lengths.
/// </summary>
public static class TextPatternOverlap
{
    /// <summary>Every pair of equal-rank patterns in <paramref name="catalog"/> that one string can match, in load
    /// order.</summary>
    public static IReadOnlyList<AmbiguousPatterns> Find(TextCatalog catalog)
    {
        var found = new List<AmbiguousPatterns>();
        foreach (var rank in catalog.Patterns.GroupBy(entry => (entry.Script, entry.At,
                     entry.SourceTemplate.LiteralLength)))
        {
            TextEntry[] entries = rank.ToArray();
            for (int a = 0; a < entries.Length; a++)
                for (int b = a + 1; b < entries.Length; b++)
                    if (CanMatchOneString(entries[a].SourceTemplate, entries[b].SourceTemplate))
                        found.Add(new AmbiguousPatterns(entries[a], entries[b]));
        }
        return found;
    }

    /// <summary>Whether some string matches both <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static bool CanMatchOneString(TextTemplate a, TextTemplate b)
    {
        Step[] x = Steps(a), y = Steps(b);
        var seen = new HashSet<(int, int, int, int)>();
        var pending = new Stack<(int X, int XState, int Y, int YState)>();
        void Visit(int xi, int xs, int yi, int ys)
        {
            // A finished {name} or {name:num} may stop, so its next step is reachable without a character.
            if (xi < x.Length && Done(x[xi], xs)) Visit(xi + 1, 0, yi, ys);
            if (yi < y.Length && Done(y[yi], ys)) Visit(xi, xs, yi + 1, 0);
            if (seen.Add((xi, xs, yi, ys))) pending.Push((xi, xs, yi, ys));
        }
        Visit(0, 0, 0, 0);
        while (pending.Count > 0)
        {
            var (xi, xs, yi, ys) = pending.Pop();
            if (xi == x.Length && yi == y.Length) return true;
            if (xi == x.Length || yi == y.Length) continue;
            foreach (var (xClass, xNext) in Moves(x[xi], xs))
                foreach (var (yClass, yNext) in Moves(y[yi], ys))
                    if (Intersects(xClass, x[xi].Char, yClass, y[yi].Char))
                        Visit(xNext < 0 ? xi + 1 : xi, Math.Max(xNext, 0), yNext < 0 ? yi + 1 : yi, Math.Max(yNext, 0));
        }
        return false;
    }

    private enum StepKind { Char, Any, Number }

    private enum CharClass { Exact, Any, Digit, Minus }

    private readonly record struct Step(StepKind Kind, char Char);

    private static Step[] Steps(TextTemplate template)
    {
        var steps = new List<Step>();
        foreach (TemplatePart part in template.Parts)
            switch (part)
            {
                case LiteralPart literal:
                    steps.AddRange(literal.Text.Select(c => new Step(StepKind.Char, c)));
                    break;
                case PlaceholderPart placeholder:
                    steps.Add(new Step(placeholder.Kind == PlaceholderKind.Number ? StepKind.Number : StepKind.Any, '\0'));
                    break;
            }
        return steps.ToArray();
    }

    /// <summary>Whether a step in state <paramref name="state"/> has consumed enough to end: <c>{name}</c> after
    /// one character (state 1), <c>{name:num}</c> after a digit (state 2).</summary>
    private static bool Done(Step step, int state) => step.Kind switch
    {
        StepKind.Any => state == 1,
        StepKind.Number => state == 2,
        _ => false,
    };

    /// <summary>The characters a step accepts in a state, and the state after (-1: the step is complete).</summary>
    private static IEnumerable<(CharClass, int)> Moves(Step step, int state)
    {
        switch (step.Kind)
        {
            case StepKind.Char:
                yield return (CharClass.Exact, -1);
                break;
            case StepKind.Any:
                yield return (CharClass.Any, 1);
                break;
            default:
                if (state == 0) yield return (CharClass.Minus, 1);
                yield return (CharClass.Digit, 2);
                break;
        }
    }

    private static bool Intersects(CharClass a, char aChar, CharClass b, char bChar)
    {
        if (a == CharClass.Any || b == CharClass.Any) return true;
        if (a == CharClass.Exact && b == CharClass.Exact) return aChar == bChar;
        if (a == CharClass.Exact) return Accepts(b, aChar);
        if (b == CharClass.Exact) return Accepts(a, bChar);
        return a == b;
    }

    private static bool Accepts(CharClass set, char c) => set switch
    {
        CharClass.Digit => TextPatternMatcher.IsDigit(c),
        CharClass.Minus => TextPatternMatcher.IsMinus(c),
        _ => true,
    };
}

namespace Ome.Engine.Localization;

/// <summary>
/// Matches a pattern entry's source against a whole string (localization spec §6.3). <c>{name}</c> matches any
/// non-empty text and <c>{name:num}</c> digits, half or full width, with an optional minus sign in front; each
/// placeholder takes as little as it can, the earlier ones first, as a lazy regular expression would. There are no
/// regular expressions: the matcher marks every position the rest of the pattern can match from, right to left,
/// then takes the shortest capture at each placeholder, so its cost is linear in the text for a given pattern and a
/// package cannot make it slow.
/// </summary>
public static class TextPatternMatcher
{
    /// <summary>Whether <paramref name="pattern"/> matches all of <paramref name="text"/>; on a match,
    /// <paramref name="captures"/> holds each placeholder's text.</summary>
    public static bool TryMatch(TextTemplate pattern, string text, out Dictionary<string, string> captures)
    {
        captures = null!;
        IReadOnlyList<TemplatePart> parts = pattern.Parts;
        int n = text.Length;
        if (n < pattern.LiteralLength + pattern.Placeholders.Count) return false;
        // Anchored literals reject most strings before any table is built.
        if (parts[0] is LiteralPart first && !text.StartsWith(first.Text, StringComparison.Ordinal)) return false;
        if (parts[^1] is LiteralPart last && !text.EndsWith(last.Text, StringComparison.Ordinal)) return false;
        // can[k][i]: parts k.. match text[i..].
        var can = new bool[parts.Count + 1][];
        can[parts.Count] = new bool[n + 1];
        can[parts.Count][n] = true;
        int[]? digitRunEnd = null;
        for (int k = parts.Count - 1; k >= 0; k--)
        {
            bool[] next = can[k + 1], here = new bool[n + 1];
            switch (parts[k])
            {
                case LiteralPart literal:
                {
                    int length = literal.Text.Length;
                    foreach (int at in Occurrences(text, literal.Text))
                        here[at] = next[at + length];
                    if (length == 0) Array.Copy(next, here, n + 1);
                    break;
                }
                case PlaceholderPart { Kind: PlaceholderKind.Any }:
                {
                    bool later = false;   // some j > i with next[j]
                    for (int i = n; i >= 0; i--)
                    {
                        here[i] = later;
                        later |= next[i];
                    }
                    break;
                }
                default:
                {
                    digitRunEnd ??= DigitRunEnds(text);
                    var reachable = new int[n + 2];   // reachable[j]: positions below j where next holds
                    for (int j = 0; j <= n; j++) reachable[j + 1] = reachable[j] + (next[j] ? 1 : 0);
                    for (int i = 0; i < n; i++)
                    {
                        int digits = IsMinus(text[i]) ? i + 1 : i;
                        int end = digits < n ? digitRunEnd[digits] : digits;
                        // A capture ends anywhere in (digits, end].
                        here[i] = end > digits && reachable[end + 1] - reachable[digits + 1] > 0;
                    }
                    break;
                }
            }
            can[k] = here;
        }
        if (!can[0][0]) return false;

        captures = new Dictionary<string, string>(StringComparer.Ordinal);
        int position = 0;
        for (int k = 0; k < parts.Count; k++)
        {
            bool[] next = can[k + 1];
            switch (parts[k])
            {
                case LiteralPart literal:
                    position += literal.Text.Length;
                    break;
                case PlaceholderPart placeholder:
                {
                    int from = placeholder.Kind == PlaceholderKind.Number && IsMinus(text[position])
                        ? position + 1
                        : position;
                    int end = from + 1;
                    while (!next[end]) end++;
                    captures[placeholder.Name] = text[position..end];
                    position = end;
                    break;
                }
            }
        }
        return true;
    }

    /// <summary>Whether <paramref name="text"/> is nothing but a number as <c>{name:num}</c> matches one.</summary>
    public static bool IsNumber(string text)
    {
        int start = text.Length > 0 && IsMinus(text[0]) ? 1 : 0;
        if (start == text.Length) return false;
        for (int k = start; k < text.Length; k++)
            if (!IsDigit(text[k])) return false;
        return true;
    }

    /// <summary>A number with its full-width digits and minus sign made ASCII (spec §6.3, <c>digits = "ascii"</c>).
    /// Any other character is kept.</summary>
    public static string AsciiDigits(string text)
    {
        if (!text.Any(c => c is >= '０' and <= '９' or '－')) return text;
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (int k = 0; k < source.Length; k++)
                span[k] = source[k] switch
                {
                    >= '０' and <= '９' => (char)('0' + source[k] - '０'),
                    '－' => '-',
                    _ => source[k],
                };
        });
    }

    public static bool IsDigit(char c) => c is >= '0' and <= '9' or >= '０' and <= '９';

    /// <summary>A minus sign as the scripts write one: ASCII, or the full-width form that native widening gives
    /// ('－', CP932 0x817C).</summary>
    public static bool IsMinus(char c) => c is '-' or '－';

    /// <summary>For each position, where the run of digits that starts there ends.</summary>
    private static int[] DigitRunEnds(string text)
    {
        var ends = new int[text.Length + 1];
        ends[text.Length] = text.Length;
        for (int i = text.Length - 1; i >= 0; i--) ends[i] = IsDigit(text[i]) ? ends[i + 1] : i;
        return ends;
    }

    /// <summary>Every position where <paramref name="literal"/> occurs in <paramref name="text"/>, overlapping
    /// ones included, found in one pass (Knuth–Morris–Pratt).</summary>
    private static IEnumerable<int> Occurrences(string text, string literal)
    {
        if (literal.Length == 0 || literal.Length > text.Length) yield break;
        var fallback = new int[literal.Length];
        for (int k = 1, length = 0; k < literal.Length;)
        {
            if (literal[k] == literal[length]) fallback[k++] = ++length;
            else if (length > 0) length = fallback[length - 1];
            else fallback[k++] = 0;
        }
        for (int i = 0, matched = 0; i < text.Length;)
        {
            if (text[i] == literal[matched])
            {
                i++;
                if (++matched == literal.Length)
                {
                    yield return i - matched;
                    matched = fallback[matched - 1];
                }
            }
            else if (matched > 0) matched = fallback[matched - 1];
            else i++;
        }
    }
}

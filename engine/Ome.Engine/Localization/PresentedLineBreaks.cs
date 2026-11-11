namespace Ome.Engine.Localization;

/// <summary>How presented text may break (localization spec §7.1, §9.2 <c>line_break</c>).</summary>
public enum LineBreakMode
{
    /// <summary>After a space or a hyphen, and between any two characters next to CJK text.</summary>
    Words,
    /// <summary>Between any two characters.</summary>
    Anywhere,
}

/// <summary>
/// Where presented text may break (localization spec §7.1). Source text keeps the native rule, a wrap wherever a
/// glyph crosses the bound except before <c>、</c>, <c>。</c> or <c>」</c>; presented text breaks only where this
/// allows, and never before closing punctuation or after opening punctuation. A <c>\n</c> always breaks, and is
/// not an opportunity here: the layout engine handles it.
/// </summary>
public static class PresentedLineBreaks
{
    /// <summary>For each index <c>i</c>, whether a row may end before <c>text[i]</c>. Index 0 is always false. A break
    /// after spaces is placed before the first non-space after them, and the layout drops those spaces.</summary>
    public static bool[] Opportunities(string text, LineBreakMode mode)
    {
        var allowed = new bool[text.Length];
        for (int i = 1; i < text.Length; i++)
        {
            char before = text[i - 1], at = text[i];
            if (char.IsLowSurrogate(at) || at == '\n' || before == '\n' || IsSpace(at)) continue;
            if (IsClosing(at) || IsOpening(before)) continue;
            allowed[i] = mode == LineBreakMode.Anywhere
                         || IsSpace(before)
                         || (IsHyphen(before) && i >= 2 && char.IsLetter(text[i - 2]) && char.IsLetter(at))
                         || IsCjk(CodePointBefore(text, i)) || IsCjk(CodePointAt(text, i));
        }
        return allowed;
    }

    /// <summary>Spaces a break drops: ASCII space, tab and the ideographic space.</summary>
    public static bool IsSpace(char c) => c is ' ' or '\t' or '\u3000';

    private static bool IsHyphen(char c) => c is '-' or '\u2010' or '\u2013';

    /// <summary>Characters a row must not start with: closing brackets and quotes, sentence punctuation, and the
    /// Japanese small kana, iteration marks and long-vowel mark (kinsoku).</summary>
    private static bool IsClosing(char c) => ClosingSet.Contains(c);

    /// <summary>Characters a row must not end with: opening brackets and quotes.</summary>
    private static bool IsOpening(char c) => OpeningSet.Contains(c);

    private const string ClosingSet = ")]}>,.!?:;%»’”、。，．・：；？！」』）】〕〉》｝］〟…‥ー々ゝゞヽヾぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ";
    private const string OpeningSet = "([{<«‘“「『（【〔〈《｛［〝";

    /// <summary>Wide scripts that break between any two characters: kana, CJK ideographs and symbols, Hangul and the
    /// full-width forms.</summary>
    public static bool IsCjk(int codePoint)
        => codePoint is >= 0x1100 and <= 0x11FF
            or >= 0x2E80 and <= 0x303F
            or >= 0x3040 and <= 0x31FF
            or >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xA960 and <= 0xA97F
            or >= 0xAC00 and <= 0xD7AF
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x20000 and <= 0x3FFFF;

    private static int CodePointBefore(string text, int index)
        => index >= 2 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2])
            ? char.ConvertToUtf32(text[index - 2], text[index - 1])
            : text[index - 1];

    private static int CodePointAt(string text, int index)
        => index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1])
            ? char.ConvertToUtf32(text[index], text[index + 1])
            : text[index];
}

namespace Ome.Engine.Localization;

/// <summary>
/// The pseudo-language <c>x-pseudo</c> (localization spec §14): every string is presented a third longer and in
/// brackets, with no catalog. A string drawn without the brackets bypassed the boundary, and a bracket cut off shows
/// a layout that cannot take longer text.
/// </summary>
public sealed class PseudoLanguageFilter : ITextFilter
{
    public string? Present(in TextFilterInput input) => Lengthen(input.CatalogText ?? input.Source);

    public static string Lengthen(string text)
    {
        int glyphs = 0;
        foreach (char c in text)
            if (!char.IsLowSurrogate(c) && c != '\n') glyphs++;
        return "[" + text + new string('~', Math.Max(1, (glyphs + 2) / 3)) + "]";
    }
}

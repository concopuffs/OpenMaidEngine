using System.Text.RegularExpressions;

namespace Ome.Engine.Localization;

/// <summary>
/// BCP 47 language tags as the localization design writes them (spec §9.1): lower case, such as <c>en</c>,
/// <c>pt-br</c> or <c>zh-hant</c>. <see cref="Pseudo"/> is the pseudo-language (§14), and <see cref="Source"/> the
/// language the supported games' scripts are written in, which means "no presenter" (§10).
/// </summary>
public static partial class LanguageTag
{
    /// <summary>The source language of the supported games: selecting it switches the presenter off.</summary>
    public const string Source = "ja";

    /// <summary>Presents every string lengthened and bracketed, with no catalog (spec §14).</summary>
    public const string Pseudo = "x-pseudo";

    /// <summary>The tag in the form catalogs and folders use: trimmed, lower case, <c>_</c> read as <c>-</c>.</summary>
    public static string Normalize(string tag) => tag.Trim().Replace('_', '-').ToLowerInvariant();

    /// <summary>Whether <paramref name="tag"/> (after <see cref="Normalize"/>) is a well-formed tag: a 2- or 3-letter
    /// language, then subtags of 1 to 8 letters or digits; or a private-use tag such as <see cref="Pseudo"/>.</summary>
    public static bool IsWellFormed(string tag) => WellFormed().IsMatch(Normalize(tag));

    /// <summary>The tag's parents, nearest first: <c>pt-br</c> gives <c>pt</c>, and <c>zh-hant-tw</c> gives
    /// <c>zh-hant</c> then <c>zh</c>. The default fallback chain (spec §6.4, §9.2) is the tag, its parents, then
    /// source.</summary>
    public static IReadOnlyList<string> Parents(string tag)
    {
        string normalized = Normalize(tag);
        if (normalized.StartsWith("x-", StringComparison.Ordinal)) return [];
        var parents = new List<string>();
        for (int dash = normalized.LastIndexOf('-'); dash > 0; dash = normalized.LastIndexOf('-', dash - 1))
            parents.Add(normalized[..dash]);
        return parents;
    }

    [GeneratedRegex("^(?:[a-z]{2,3}(?:-[a-z0-9]{1,8})*|x(?:-[a-z0-9]{1,8})+)$")]
    private static partial Regex WellFormed();
}

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ome.Engine.Localization;
using Ome.Engine.Model;

namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// The shell's keyed text (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §6): one table per language,
/// embedded in this assembly as <c>Shell/Strings/&lt;language&gt;.json</c>. The keys name what a string is for, not
/// its text. Japanese keeps AGERC's wording, half-width katakana included, and takes AGE's own boxes' wording from
/// <see cref="AgeBoxText"/>; English is our own. A key a table lacks falls back to Japanese.
/// <para>Packages supply more (localization spec §11): their <c>[[engine]]</c> entries for a language go in front of
/// the embedded table for it, along the language's fallback chain, then the embedded Japanese
/// (<see cref="Load(string?, IReadOnlyList{string}, IReadOnlyList{EngineTextLayer}, ICollection{string}?)"/>).</para>
/// </summary>
public sealed partial class ShellStrings
{
    public const string FallbackLanguage = "ja";

    /// <summary>The languages with an embedded table, the fallback first.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["ja", "en"];

    /// <summary>The Japanese table: what the driver reports, whatever the player's language (spec §8).</summary>
    public static ShellStrings Japanese { get; } = new(FallbackLanguage, JapaneseTable(), null);

    private readonly IReadOnlyDictionary<string, string> _table;
    private readonly ShellStrings? _fallback;

    public ShellStrings(string language, IReadOnlyDictionary<string, string> table, ShellStrings? fallback)
    {
        Language = language;
        _table = table;
        _fallback = fallback;
    }

    public string Language { get; }

    /// <summary>This table's own keys, without the fallback's.</summary>
    public IEnumerable<string> Keys => _table.Keys;

    /// <summary>The table for <paramref name="language"/> (case ignored), or the Japanese one when there is none.</summary>
    public static ShellStrings Load(string? language)
    {
        string wanted = (language ?? "").Trim().ToLowerInvariant();
        if (wanted == FallbackLanguage || ReadTable(wanted) is not { } table) return Japanese;
        return new ShellStrings(wanted, table, Japanese);
    }

    /// <summary>
    /// The tables for a language with the stack's engine text (localization spec §11): for each language of
    /// <paramref name="lookup"/> (the language, then its fallbacks), the entries <paramref name="layers"/> holds for
    /// it, then its embedded table; then the embedded Japanese. An entry whose key the engine does not have, or whose
    /// text does not take the Japanese text's arguments, is left out, with a line in <paramref name="problems"/>.
    /// </summary>
    public static ShellStrings Load(string? language, IReadOnlyList<string> lookup, IReadOnlyList<EngineTextLayer> layers,
                                    ICollection<string>? problems = null)
    {
        string wanted = (language ?? "").Trim().ToLowerInvariant();
        ShellStrings strings = Japanese;
        foreach (string tag in (lookup.Count > 0 ? lookup : [wanted]).Reverse())
        {
            if (tag != FallbackLanguage && ReadTable(tag) is { } embedded) strings = new ShellStrings(tag, embedded, strings);
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (EngineTextLayer layer in layers.Where(layer => layer.Language == tag))
                foreach (var (key, value) in layer.Entries)
                {
                    if (EntryProblem(key, value.Text) is { } problem) problems?.Add($"{value.Origin}: {problem}");
                    else entries[key] = value.Text;
                }
            if (entries.Count > 0) strings = new ShellStrings(tag, entries, strings);
        }
        return strings;
    }

    /// <summary>The embedded table for <paramref name="language"/> (Japanese with AGE's boxes), or null when the
    /// engine has none.</summary>
    public static IReadOnlyDictionary<string, string>? Embedded(string language)
        => language == FallbackLanguage ? Japanese._table : ReadTable(language);

    /// <summary>Why a package may not set <paramref name="key"/> to <paramref name="text"/>, or null when it may: the
    /// engine has no such key, or the text names an argument the Japanese does not take or does not format.</summary>
    public static string? EntryProblem(string key, string text)
    {
        if (!Japanese.TryGet(key, out string? japanese)) return $"the engine has no text '{key}'";
        int[] takes = Arguments(japanese) ?? [];
        if (takes.Length == 0) return null;
        if (Arguments(text) is not { } uses)
            return $"'{key}' does not format: write a literal brace as {{{{ or }}}}";
        if (uses.Except(takes).ToArray() is { Length: > 0 } extra)
            return $"'{key}' takes {string.Join(", ", takes.Select(n => $"{{{n}}}"))}, not "
                   + string.Join(", ", extra.Select(n => $"{{{n}}}"));
        return null;
    }

    /// <summary>The text for <paramref name="key"/>: this table's, else the fallback's, else the key itself, so a
    /// missing string shows where it belongs.</summary>
    public string this[string key] => TryGet(key, out string? text) ? text : key;

    public bool TryGet(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        if (_table.TryGetValue(key, out text)) return true;
        if (_fallback != null) return _fallback.TryGet(key, out text);
        text = null;
        return false;
    }

    /// <summary>The argument indices a format string names, or null when it does not format (a lone brace).</summary>
    private static int[]? Arguments(string text)
    {
        string bare = text.Replace("{{", "").Replace("}}", "");
        var uses = new SortedSet<int>();
        string rest = FormatItem().Replace(bare, match =>
        {
            uses.Add(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            return "";
        });
        return rest.Contains('{') || rest.Contains('}') ? null : [.. uses];
    }

    [GeneratedRegex(@"\{(\d{1,2})(?:,-?\d+)?(?::[^{}]*)?\}")]
    private static partial Regex FormatItem();

    private static Dictionary<string, string> JapaneseTable()
    {
        Dictionary<string, string> table = ReadTable(FallbackLanguage)!;
        foreach (var (key, text) in AgeBoxText.Japanese) table.Add(key, text);     // one home: not also in ja.json
        return table;
    }

    private static Dictionary<string, string>? ReadTable(string language)
    {
        if (!Languages.Contains(language)) return null;
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"Ome.Engine.Frontend.Shell.Strings.{language}.json");
        return stream == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }
}

using System.Reflection;
using System.Text.Json;

namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// The shell's keyed text (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §6): one table per language,
/// embedded in this assembly as <c>Shell/Strings/&lt;language&gt;.json</c>. The keys name what a string is for, not
/// its text. Japanese keeps AGERC's wording, half-width katakana included; English is our own. A key a table lacks
/// falls back to Japanese.
/// </summary>
public sealed class ShellStrings
{
    public const string FallbackLanguage = "ja";

    /// <summary>The languages with a table, the fallback first.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["ja", "en"];

    /// <summary>The Japanese table: what the driver reports, whatever the player's language (spec §8).</summary>
    public static ShellStrings Japanese { get; } = new(FallbackLanguage, ReadTable(FallbackLanguage)!, null);

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

    private static Dictionary<string, string>? ReadTable(string language)
    {
        if (!Languages.Contains(language)) return null;
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"Ome.Engine.Frontend.Shell.Strings.{language}.json");
        return stream == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }
}

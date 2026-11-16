using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ome.Launcher;

/// <summary>
/// The launcher's own text (launcher spec §8.8): keyed tables, <c>en</c> and <c>ja</c>, embedded from
/// <c>Strings/&lt;tag&gt;.json</c> as the shell keeps its own (<c>ShellStrings</c>). A key the chosen table lacks reads from
/// English, and a key neither has reads as itself, so a missing string shows where it belongs. A text formats with
/// <c>{0}</c>, <c>{1}</c>, ….
/// </summary>
public sealed partial class LauncherStrings
{
    public const string Fallback = "en";

    public static IReadOnlyList<string> Languages { get; } = ["en", "ja"];

    private readonly IReadOnlyDictionary<string, string> _table;
    private readonly LauncherStrings? _fallback;

    private LauncherStrings(string language, IReadOnlyDictionary<string, string> table, LauncherStrings? fallback)
    {
        Language = language;
        _table = table;
        _fallback = fallback;
    }

    public static LauncherStrings English { get; } = new(Fallback, Table(Fallback)!, null);

    public string Language { get; }

    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    public string this[string key]
        => _table.TryGetValue(key, out string? text) ? text : _fallback is { } fallback ? fallback[key] : key;

    /// <summary>Whether this table or English has <paramref name="key"/>.</summary>
    public bool Has(string key) => _table.ContainsKey(key) || (_fallback?.Has(key) ?? false);

    /// <summary><paramref name="key"/>'s text with its arguments filled in.</summary>
    public string Format(string key, params object?[] values) => string.Format(Culture, this[key], values);

    /// <summary>The language the launcher starts in: <c>launcher.toml</c>'s when there is a table for it, else the OS
    /// language's (or one of its parents') when there is, else English.</summary>
    public static string Choose(string? setting, CultureInfo os)
    {
        if (setting != null && Languages.Contains(setting)) return setting;
        for (CultureInfo culture = os; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
            if (Languages.Contains(culture.TwoLetterISOLanguageName)) return culture.TwoLetterISOLanguageName;
        return Fallback;
    }

    public static LauncherStrings Load(string language)
        => language != Fallback && Table(language) is { } table ? new LauncherStrings(language, table, English) : English;

    /// <summary>A language's embedded table, or null when the launcher has none.</summary>
    public static IReadOnlyDictionary<string, string>? Table(string language)
    {
        if (!Languages.Contains(language)) return null;
        using Stream? stream = typeof(LauncherStrings).Assembly
            .GetManifestResourceStream($"Ome.Launcher.Strings.{language}.json");
        return stream == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }

    /// <summary>The argument numbers a text formats, in order, or null when it does not format (a stray brace).</summary>
    public static int[]? Arguments(string text)
    {
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, text, new object?[10]);
        }
        catch (FormatException)
        {
            return null;
        }
        return FormatItem().Matches(text.Replace("{{", "").Replace("}}", ""))
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
    }

    [GeneratedRegex(@"\{(\d{1,2})(?:,-?\d+)?(?::[^{}]*)?\}")]
    private static partial Regex FormatItem();
}

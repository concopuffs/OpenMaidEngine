using Ome.Engine.Localization;

namespace Ome.Mods.Localization;

/// <summary>An <c>[[engine]]</c> entry: a keyed string the port draws itself (localization spec §11).</summary>
public sealed record EngineTextEntry(string Key, string Text, string Origin = "");

/// <summary>
/// One catalog file under a package's <c>text/&lt;language&gt;/</c> (localization spec §9.3). <see cref="Script"/>
/// is the default scope of its entries; each entry carries its effective scope. <see cref="Fingerprint"/> is the base
/// script's, required when an entry uses <c>at</c>.
/// </summary>
public sealed record TextCatalogFile(
    string? Script,
    string? Fingerprint,
    IReadOnlyList<TextEntry> Entries,
    IReadOnlyList<EngineTextEntry> EngineEntries);

/// <summary>
/// A <c>[[language]]</c> table (localization spec §9.2): a language's name, fallback chain and settings. Null keeps
/// the default: the tag's parents as the fallback, <c>words</c>, <c>presented</c> and <c>keep</c>. Packages declare
/// it in their manifest from milestone 4; a text root declares it in <c>languages.toml</c>
/// (<see cref="TextRootLoader.LanguagesFile"/>).
/// </summary>
public sealed record LanguageSettings(string Tag)
{
    public string? Name { get; init; }
    public IReadOnlyList<string>? Fallback { get; init; }
    public LineBreakMode? LineBreak { get; init; }
    public TextMeasure? Measure { get; init; }
    public DigitsMode? Digits { get; init; }
    public string Origin { get; init; } = "";
}

/// <summary>A catalog file that does not parse or validate: every problem found, each with its file and line.</summary>
public sealed class TextCatalogException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public TextCatalogException(IReadOnlyList<string> problems)
        : base(string.Join(Environment.NewLine, problems))
        => Problems = problems;
}

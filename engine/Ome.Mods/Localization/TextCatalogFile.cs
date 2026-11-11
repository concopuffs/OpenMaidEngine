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

/// <summary>A catalog file that does not parse or validate: every problem found, each with its file and line.</summary>
public sealed class TextCatalogException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public TextCatalogException(IReadOnlyList<string> problems)
        : base(string.Join(Environment.NewLine, problems))
        => Problems = problems;
}

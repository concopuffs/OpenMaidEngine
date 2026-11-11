using Ome.Engine.Localization;

namespace Ome.Mods.Localization;

/// <summary>An <c>[[engine]]</c> entry: a keyed string the port draws itself (localization spec §11). Without
/// <see cref="Text"/> it is a template, which the loader skips: <c>omp text extract</c> writes one for each key the
/// engine has no table for in the language, with the engine's own wording in <see cref="Note"/>.</summary>
public sealed record EngineTextEntry(string Key, string? Text, string Origin = "", string? Note = null);

/// <summary>
/// One catalog file under a package's <c>text/&lt;language&gt;/</c> (localization spec §9.3). <see cref="Script"/>
/// is the default scope of its entries; each entry carries its effective scope. <see cref="Fingerprint"/> is the base
/// script's, required when an entry uses <c>at</c>. <see cref="Entries"/> are the entries the loader applies;
/// <see cref="Items"/>, which only the reader fills, are all the file's <c>[[text]]</c> tables in order, templates and
/// obsolete entries included.
/// </summary>
public sealed record TextCatalogFile(
    string? Script,
    string? Fingerprint,
    IReadOnlyList<TextEntry> Entries,
    IReadOnlyList<EngineTextEntry> EngineEntries)
{
    public IReadOnlyList<TextCatalogItem> Items { get; init; } = [];

    /// <summary>The file's entries as items: <see cref="Items"/> when the reader filled them, else
    /// <see cref="Entries"/>, each with its text.</summary>
    public IReadOnlyList<TextCatalogItem> AllItems
        => Items.Count > 0 ? Items : Entries.Select(entry => new TextCatalogItem(entry, true, false)).ToList();
}

/// <summary>
/// A <c>[[text]]</c> table as its file holds it. The loader applies it only when it has <c>text</c> and is not
/// marked <c>obsolete</c>. <c>omp text extract</c> writes entries without text as templates for the translator, and
/// marks obsolete those whose source is no longer in the scripts; both stay in the file. An entry without text
/// carries its source as <see cref="TextEntry.Text"/>.
/// </summary>
public sealed record TextCatalogItem(TextEntry Entry, bool HasText, bool Obsolete)
{
    public bool Applies => HasText && !Obsolete;
}

/// <summary>
/// A <c>[[language.font]]</c> table (localization spec §7.4, §9.2): for the authored faces <see cref="Requested"/>
/// names (<c>*</c> for every face), the fonts to use, in order. An entry under <c>fonts/</c> is a file the package
/// bundles; once loaded it is an absolute path (<see cref="IsBundled"/>). Any other entry is a family name.
/// </summary>
public sealed record LanguageFont(IReadOnlyList<string> Requested, IReadOnlyList<string> Use)
{
    public const string FontsFolder = "fonts/";
    public const string AnyFace = "*";

    /// <summary>The font file extensions a package may bundle.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".ttf", ".otf", ".ttc", ".woff", ".woff2"];

    /// <summary>Whether a <c>use</c> entry names a bundled file: a path under <c>fonts/</c> as written, or the
    /// absolute path the loader resolved it to.</summary>
    public static bool IsBundled(string use)
        => use.StartsWith(FontsFolder, StringComparison.OrdinalIgnoreCase) || Path.IsPathFullyQualified(use);

    /// <summary>Whether <paramref name="face"/> is one this table maps.</summary>
    public bool Applies(string face)
        => Requested.Any(requested => requested == AnyFace || requested.Equals(face, StringComparison.OrdinalIgnoreCase));

    /// <summary>The table with each bundled entry resolved by <paramref name="resolve"/> (a package-relative
    /// <c>fonts/NAME</c> to an absolute path).</summary>
    public LanguageFont Resolve(Func<string, string> resolve)
        => this with { Use = Use.Select(use => use.StartsWith(FontsFolder, StringComparison.OrdinalIgnoreCase)
                                            ? resolve(use) : use).ToArray() };
}

/// <summary>
/// A <c>[[language]]</c> table (localization spec §9.2): a language's name, fallback chain and settings. Null keeps
/// the default: the tag's parents as the fallback, <c>words</c>, <c>presented</c>, <c>keep</c>, a glyph delay
/// scale of 1, a fit floor of 0.75, <c>shrink</c> on overflow and the system's fonts. Packages declare it in their manifest (format version 2); a text root declares
/// it in <c>languages.toml</c> (<see cref="TextRootLoader.LanguagesFile"/>).
/// </summary>
public sealed record LanguageSettings(string Tag)
{
    public string? Name { get; init; }
    public IReadOnlyList<string>? Fallback { get; init; }
    public LineBreakMode? LineBreak { get; init; }
    public TextMeasure? Measure { get; init; }
    public DigitsMode? Digits { get; init; }
    /// <summary>The factor on the reveal's glyph delay for presented runs (§7.5).</summary>
    public double? GlyphDelayScale { get; init; }
    /// <summary>The floor for <c>fit = "shrink"</c> (§7.2) and for narrowing a run that would be cut.</summary>
    public double? MinFitScale { get; init; }
    /// <summary>What a run that needs more rows than its window has does (§7.3): narrowed or cut.</summary>
    public TextOverflow? Overflow { get; init; }
    /// <summary>The font tables, in order (§7.4); the first that applies to a face wins.</summary>
    public IReadOnlyList<LanguageFont>? Fonts { get; init; }
    public string Origin { get; init; } = "";

    /// <summary>These settings with each key <paramref name="later"/> sets taken from it (§9.2: the later package
    /// wins per key), and the keys it replaced.</summary>
    public (LanguageSettings Merged, IReadOnlyList<string> Replaced) Merge(LanguageSettings later)
    {
        var replaced = new List<string>();
        T? Pick<T>(T? earlier, T? next, string key)
        {
            if (next is null) return earlier;
            if (earlier is not null) replaced.Add(key);
            return next;
        }
        var merged = this with
        {
            Name = Pick(Name, later.Name, "name"),
            Fallback = Pick(Fallback, later.Fallback, "fallback"),
            LineBreak = Pick(LineBreak, later.LineBreak, "line_break"),
            Measure = Pick(Measure, later.Measure, "measure"),
            Digits = Pick(Digits, later.Digits, "digits"),
            GlyphDelayScale = Pick(GlyphDelayScale, later.GlyphDelayScale, "glyph_delay_scale"),
            MinFitScale = Pick(MinFitScale, later.MinFitScale, "min_fit_scale"),
            Overflow = Pick(Overflow, later.Overflow, "overflow"),
            Fonts = Pick(Fonts, later.Fonts, "font"),
            Origin = later.Origin,
        };
        return (merged, replaced);
    }
}

/// <summary>A catalog file that does not parse or validate: every problem found, each with its file and line.</summary>
public sealed class TextCatalogException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public TextCatalogException(IReadOnlyList<string> problems)
        : base(string.Join(Environment.NewLine, problems))
        => Problems = problems;
}

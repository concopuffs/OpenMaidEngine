namespace Ome.Engine.Localization;

/// <summary>Where the presented text sits against the source text's extent (localization spec §7.2).</summary>
public enum TextAlign { Start, Center, End }

/// <summary>Whether presented text wider than the source text is narrowed (localization spec §7.2).</summary>
public enum TextFit { None, Shrink }

/// <summary>What the length opcodes measure (localization spec §5.3).</summary>
public enum TextMeasure { Presented, Source }

/// <summary>Limits on catalog data, which comes from untrusted packages (localization spec §16).</summary>
public static class TextCatalogLimits
{
    /// <summary>The longest <c>source</c>, <c>text</c> or <c>note</c>, in UTF-16 code units.</summary>
    public const int MaxStringLength = 16 * 1024;
    /// <summary>The most entries one language may hold after merging.</summary>
    public const int MaxEntriesPerLanguage = 1_000_000;
    /// <summary>The largest <c>cells</c> value.</summary>
    public const int MaxCells = 4096;
}

/// <summary>
/// One catalog entry (localization spec §9.3): a source text or pattern, the presented text, and its scope. The
/// scope is global when <see cref="Script"/> is null, a script when only <see cref="Script"/> is set, and one
/// instruction when <see cref="At"/> is set too. <see cref="Source"/> and <see cref="Text"/> keep the written form
/// (braces doubled); the parsed templates are <see cref="SourceTemplate"/> and <see cref="TextTemplate"/>.
/// </summary>
public sealed record TextEntry
{
    private TextEntry(string source, string text, TextTemplate sourceTemplate, TextTemplate textTemplate)
    {
        Source = source;
        Text = text;
        SourceTemplate = sourceTemplate;
        TextTemplate = textTemplate;
    }

    public string Source { get; }
    public string Text { get; }
    public TextTemplate SourceTemplate { get; }
    public TextTemplate TextTemplate { get; }

    /// <summary>The normalized script name (<see cref="TextCatalog.NormalizeScriptName"/>), or null for global.</summary>
    public string? Script { get; init; }

    /// <summary>The base dword offset of the instruction that draws the text; needs <see cref="Script"/>.</summary>
    public int? At { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Start;
    public TextFit Fit { get; init; } = TextFit.None;

    /// <summary>Overrides the language's measurement mode for this string; null keeps the language's.</summary>
    public TextMeasure? Measure { get; init; }

    /// <summary>This string's length in half cells, in place of the count (spec §5.3).</summary>
    public int? Cells { get; init; }

    /// <summary>A translator's comment; the engine ignores it.</summary>
    public string? Note { get; init; }

    /// <summary>Where the entry was read from, as <c>file:line</c>, for diagnostics and the link report.</summary>
    public string Origin { get; init; } = "";

    public bool IsPattern => SourceTemplate.HasPlaceholders;

    /// <summary>The exact key of an entry that is not a pattern.</summary>
    public string Key => SourceTemplate.Literal;

    /// <summary>Create an entry from the written forms of <paramref name="source"/> and <paramref name="text"/>.
    /// Reports the first problem in <paramref name="error"/>: a malformed placeholder, a placeholder in
    /// <c>text</c> that <c>source</c> does not define, or a string over the length limit.</summary>
    public static bool TryCreate(string source, string text, out TextEntry entry, out string? error)
    {
        entry = null!;
        if (source.Length == 0)
        {
            error = "source is empty";
            return false;
        }
        if (source.Length > TextCatalogLimits.MaxStringLength || text.Length > TextCatalogLimits.MaxStringLength)
        {
            error = $"source and text are limited to {TextCatalogLimits.MaxStringLength} characters";
            return false;
        }
        if (!TextTemplate.TryParse(source, allowKinds: true, out TextTemplate sourceTemplate, out error))
        {
            error = "source: " + error;
            return false;
        }
        if (!TextTemplate.TryParse(text, allowKinds: false, out TextTemplate textTemplate, out error))
        {
            error = "text: " + error;
            return false;
        }
        var defined = sourceTemplate.Placeholders.Select(part => part.Name).ToHashSet(StringComparer.Ordinal);
        if (textTemplate.Placeholders.FirstOrDefault(part => !defined.Contains(part.Name)) is { } unknown)
        {
            error = $"text uses placeholder '{{{unknown.Name}}}', which source does not define";
            return false;
        }
        entry = new TextEntry(source, text, sourceTemplate, textTemplate);
        return true;
    }

    /// <summary>Create an entry for literal texts, escaping their braces.</summary>
    public static TextEntry Literal(string source, string text)
        => TryCreate(TextTemplate.Escape(source), TextTemplate.Escape(text), out TextEntry entry, out string? error)
            ? entry
            : throw new ArgumentException(error);

    /// <summary>Create an entry from written forms, throwing on the first problem.</summary>
    public static TextEntry Create(string source, string text)
        => TryCreate(source, text, out TextEntry entry, out string? error) ? entry : throw new ArgumentException(error);
}

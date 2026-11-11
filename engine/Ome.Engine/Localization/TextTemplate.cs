using System.Text;

namespace Ome.Engine.Localization;

/// <summary>What a placeholder in an entry's <c>source</c> matches (localization spec §6.3).</summary>
public enum PlaceholderKind
{
    /// <summary><c>{name}</c>: any non-empty text.</summary>
    Any,
    /// <summary><c>{name:num}</c>: digits, half or full width, with an optional minus sign in front.</summary>
    Number,
}

/// <summary>One part of a parsed template: literal text, or a placeholder.</summary>
public abstract record TemplatePart;

/// <summary>Literal text, with <c>{{</c> and <c>}}</c> already unescaped.</summary>
public sealed record LiteralPart(string Text) : TemplatePart;

/// <summary>A <c>{name}</c> or <c>{name:num}</c> placeholder.</summary>
public sealed record PlaceholderPart(string Name, PlaceholderKind Kind) : TemplatePart;

/// <summary>
/// The parsed form of an entry's <c>source</c> or <c>text</c> (localization spec §6.3, §9.3). A template with no
/// placeholder is plain text: its <see cref="Literal"/> is the exact key or the presented text. <c>{{</c> and
/// <c>}}</c> stand for literal braces. There are no regular expressions, so parsing and matching stay linear.
/// </summary>
public sealed class TextTemplate
{
    private TextTemplate(IReadOnlyList<TemplatePart> parts)
    {
        Parts = parts;
        Placeholders = parts.OfType<PlaceholderPart>().ToArray();
        LiteralLength = parts.OfType<LiteralPart>().Sum(part => part.Text.Length);
    }

    public IReadOnlyList<TemplatePart> Parts { get; }
    public IReadOnlyList<PlaceholderPart> Placeholders { get; }
    public bool HasPlaceholders => Placeholders.Count > 0;

    /// <summary>The number of literal characters; resolution tries patterns with the most first (spec §6.4).</summary>
    public int LiteralLength { get; }

    public bool StartsWithLiteral => Parts.Count > 0 && Parts[0] is LiteralPart;
    public bool EndsWithLiteral => Parts.Count > 0 && Parts[^1] is LiteralPart;

    /// <summary>The unescaped text of a template without placeholders.</summary>
    public string Literal => HasPlaceholders
        ? throw new InvalidOperationException("a pattern has no single literal text")
        : string.Concat(Parts.Cast<LiteralPart>().Select(part => part.Text));

    /// <summary>The template as written, with each placeholder's kind left out: the form a <c>text</c> may take.</summary>
    public string WithoutKinds()
        => string.Concat(Parts.Select(part => part switch
        {
            LiteralPart literal => Escape(literal.Text),
            PlaceholderPart placeholder => "{" + placeholder.Name + "}",
            _ => "",
        }));

    /// <summary>A source's untranslated text: the source itself, with placeholder kinds left out.</summary>
    public static string UntranslatedText(string source)
        => TryParse(source, allowKinds: true, out TextTemplate template, out _) ? template.WithoutKinds() : source;

    /// <summary>Escape literal text for a <c>source</c> or <c>text</c> value: braces are doubled.</summary>
    public static string Escape(string literal) => literal.Replace("{", "{{").Replace("}", "}}");

    /// <summary>Parse a template. <paramref name="allowKinds"/> permits <c>{name:num}</c> (sources only).</summary>
    public static bool TryParse(string text, bool allowKinds, out TextTemplate template, out string? error)
    {
        var parts = new List<TemplatePart>();
        var literal = new StringBuilder();
        var names = new HashSet<string>(StringComparer.Ordinal);
        template = null!;
        for (int k = 0; k < text.Length; k++)
        {
            char c = text[k];
            if (c == '{' && k + 1 < text.Length && text[k + 1] == '{')
            {
                literal.Append('{');
                k++;
                continue;
            }
            if (c == '}' && k + 1 < text.Length && text[k + 1] == '}')
            {
                literal.Append('}');
                k++;
                continue;
            }
            if (c == '}')
            {
                error = $"a '}}' at character {k + 1} closes no placeholder (write '}}}}' for a literal brace)";
                return false;
            }
            if (c != '{')
            {
                literal.Append(c);
                continue;
            }
            int close = text.IndexOf('}', k + 1);
            if (close < 0)
            {
                error = $"the placeholder at character {k + 1} is not closed (write '{{{{' for a literal brace)";
                return false;
            }
            string body = text[(k + 1)..close];
            int colon = body.IndexOf(':');
            string name = colon < 0 ? body : body[..colon];
            string? kindName = colon < 0 ? null : body[(colon + 1)..];
            if (!IsValidName(name))
            {
                error = $"placeholder '{{{body}}}' needs a name of letters, digits and '_', not starting with a digit";
                return false;
            }
            PlaceholderKind kind = PlaceholderKind.Any;
            if (kindName != null)
            {
                if (!allowKinds)
                {
                    error = $"placeholder '{{{body}}}': a kind such as ':num' belongs in source, not text";
                    return false;
                }
                if (kindName != "num")
                {
                    error = $"placeholder '{{{body}}}': unknown kind '{kindName}' (the only kind is 'num')";
                    return false;
                }
                kind = PlaceholderKind.Number;
            }
            if (allowKinds && !names.Add(name))
            {
                error = $"placeholder '{{{name}}}' appears twice in source";
                return false;
            }
            if (literal.Length > 0)
            {
                parts.Add(new LiteralPart(literal.ToString()));
                literal.Clear();
            }
            parts.Add(new PlaceholderPart(name, kind));
            k = close;
        }
        if (literal.Length > 0 || parts.Count == 0) parts.Add(new LiteralPart(literal.ToString()));
        template = new TextTemplate(parts);
        error = null;
        return true;
    }

    public static TextTemplate Parse(string text, bool allowKinds)
        => TryParse(text, allowKinds, out TextTemplate template, out string? error)
            ? template
            : throw new FormatException(error);

    /// <summary>Fill the placeholders from <paramref name="values"/>. A name with no value stays as written.</summary>
    public string Render(IReadOnlyDictionary<string, string> values)
    {
        var text = new StringBuilder();
        foreach (TemplatePart part in Parts)
            text.Append(part switch
            {
                LiteralPart literal => literal.Text,
                PlaceholderPart placeholder => values.TryGetValue(placeholder.Name, out string? value)
                    ? value
                    : "{" + placeholder.Name + "}",
                _ => "",
            });
        return text.ToString();
    }

    private static bool IsValidName(string name)
    {
        if (name.Length == 0 || char.IsAsciiDigit(name[0])) return false;
        foreach (char c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        return true;
    }
}

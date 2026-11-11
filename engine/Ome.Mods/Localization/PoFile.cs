using System.Globalization;
using System.Text;

namespace Ome.Mods.Localization;

/// <summary>One message of a gettext PO file: its context, source and translation, the extracted comments, references
/// and flags, whether it is obsolete (<c>#~</c>), and the line it starts on. An empty translation is untranslated.
/// </summary>
public sealed record PoEntry(string? Context, string Id, string Str)
{
    public IReadOnlyList<string> TranslatorComments { get; init; } = [];
    public IReadOnlyList<string> ExtractedComments { get; init; } = [];
    public IReadOnlyList<string> References { get; init; } = [];
    public IReadOnlyList<string> Flags { get; init; } = [];
    public bool Obsolete { get; init; }
    public int Line { get; init; }

    public bool Fuzzy => Flags.Contains("fuzzy", StringComparer.Ordinal);
}

/// <summary>A PO file that does not parse: every problem found, each with its file and line.</summary>
public sealed class PoFileException(IReadOnlyList<string> problems) : Exception(string.Join(Environment.NewLine, problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// gettext PO for translation tools (localization spec §9.3, §13): one message per entry, in UTF-8. The writer puts a
/// header entry first and breaks a string after each <c>\n</c>; the reader takes what gettext writes: contexts,
/// multi-line strings, C escapes, comments, flags and obsolete entries. Plural forms are not used by the catalogs and
/// are refused.
/// </summary>
public static class PoFile
{
    public static string Write(string language, IEnumerable<PoEntry> entries, IEnumerable<string> headerComments)
    {
        var text = new StringBuilder();
        foreach (string comment in headerComments) text.Append("# ").Append(comment.ReplaceLineEndings(" ")).Append('\n');
        text.Append("msgid \"\"\nmsgstr \"\"\n");
        foreach (string field in new[]
                 {
                     $"Project-Id-Version: omp text {language}", "Content-Type: text/plain; charset=UTF-8",
                     "Content-Transfer-Encoding: 8bit", $"Language: {language}",
                     "MIME-Version: 1.0", "X-Generator: omp text po-export",
                 })
            text.Append('"').Append(Escape(field + "\n")).Append("\"\n");
        foreach (PoEntry entry in entries)
        {
            text.Append('\n');
            foreach (string comment in entry.TranslatorComments) text.Append("# ").Append(comment).Append('\n');
            foreach (string line in entry.ExtractedComments.SelectMany(comment => comment.Split('\n')))
                text.Append("#. ").Append(line).Append('\n');
            foreach (string reference in entry.References) text.Append("#: ").Append(reference).Append('\n');
            if (entry.Flags.Count > 0) text.Append("#, ").Append(string.Join(", ", entry.Flags)).Append('\n');
            string prefix = entry.Obsolete ? "#~ " : "";
            if (entry.Context != null) Keyword(text, prefix, "msgctxt", entry.Context);
            Keyword(text, prefix, "msgid", entry.Id);
            Keyword(text, prefix, "msgstr", entry.Str);
        }
        return text.ToString();
    }

    private static void Keyword(StringBuilder text, string prefix, string keyword, string value)
    {
        string[] parts = Split(value);
        if (parts.Length == 1)
        {
            text.Append(prefix).Append(keyword).Append(" \"").Append(Escape(value)).Append("\"\n");
            return;
        }
        text.Append(prefix).Append(keyword).Append(" \"\"\n");
        foreach (string part in parts) text.Append(prefix).Append('"').Append(Escape(part)).Append("\"\n");
    }

    /// <summary>The value broken after each line feed, as gettext writes multi-line strings.</summary>
    private static string[] Split(string value)
    {
        if (!value.Contains('\n') || value.IndexOf('\n') == value.Length - 1) return [value];
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < value.Length; i++)
            if (value[i] == '\n')
            {
                parts.Add(value[start..(i + 1)]);
                start = i + 1;
            }
        if (start < value.Length) parts.Add(value[start..]);
        return [.. parts];
    }

    public static string Escape(string value)
    {
        var text = new StringBuilder(value.Length);
        foreach (char c in value)
            switch (c)
            {
                case '\\': text.Append("\\\\"); break;
                case '"': text.Append("\\\""); break;
                case '\n': text.Append("\\n"); break;
                case '\t': text.Append("\\t"); break;
                case '\r': text.Append("\\r"); break;
                default:
                    if (c < 0x20 || c == 0x7f) text.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
                    else text.Append(c);
                    break;
            }
        return text.ToString();
    }

    /// <summary>The messages of <paramref name="text"/>, the header entry (an empty <c>msgid</c> without context)
    /// left out. A header that names a charset other than UTF-8 is refused.</summary>
    /// <exception cref="PoFileException">The file does not parse.</exception>
    public static IReadOnlyList<PoEntry> Parse(string text, string origin)
    {
        var problems = new List<string>();
        var entries = new List<PoEntry>();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        var translator = new List<string>();
        var extracted = new List<string>();
        var references = new List<string>();
        var flags = new List<string>();
        string? context = null, id = null, str = null;
        string? field = null;       // the keyword the last string belongs to
        bool obsolete = false;
        int start = 0;

        void Flush()
        {
            if (id != null)
            {
                if (str == null) problems.Add($"{origin}:{start}: msgid without msgstr");
                else if (id.Length == 0 && context == null)
                {
                    foreach (string header in str.Split('\n'))
                        if (header.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)
                            && header.Contains("charset=", StringComparison.OrdinalIgnoreCase)
                            && !header.Contains("charset=UTF-8", StringComparison.OrdinalIgnoreCase))
                            problems.Add($"{origin}:{start}: the file must be UTF-8 ({header.Trim()})");
                }
                else entries.Add(new PoEntry(context, id, str)
                {
                    TranslatorComments = [.. translator], ExtractedComments = [.. extracted], References = [.. references],
                    Flags = [.. flags], Obsolete = obsolete, Line = start,
                });
            }
            else if (context != null || str != null) problems.Add($"{origin}:{start}: an entry needs msgid");
            translator.Clear(); extracted.Clear(); references.Clear(); flags.Clear();
            context = id = str = field = null;
            obsolete = false;
        }

        for (int index = 0; index < lines.Length; index++)
        {
            int number = index + 1;
            string line = lines[index].Trim();
            bool isObsolete = false;
            if (line.StartsWith("#~", StringComparison.Ordinal))
            {
                isObsolete = true;
                line = line[2..].TrimStart();
                if (line.Length == 0) continue;
            }
            else if (line.StartsWith('#'))
            {
                if (str != null) Flush();
                if (line.StartsWith("#.", StringComparison.Ordinal)) extracted.Add(line[2..].TrimStart());
                else if (line.StartsWith("#:", StringComparison.Ordinal)) references.Add(line[2..].Trim());
                else if (line.StartsWith("#,", StringComparison.Ordinal))
                    flags.AddRange(line[2..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                else if (!line.StartsWith("#|", StringComparison.Ordinal)) translator.Add(line[1..].TrimStart());
                continue;
            }
            if (line.Length == 0)
            {
                Flush();
                continue;
            }
            if (line.StartsWith('"'))
            {
                if (field == null)
                {
                    problems.Add($"{origin}:{number}: a string outside an entry");
                    continue;
                }
                if (!TryUnquote(line, out string more, out string? error))
                {
                    problems.Add($"{origin}:{number}: {error}");
                    continue;
                }
                switch (field)
                {
                    case "msgctxt": context += more; break;
                    case "msgid": id += more; break;
                    default: str += more; break;
                }
                continue;
            }
            int space = line.IndexOf(' ');
            string keyword = space < 0 ? line : line[..space];
            string rest = space < 0 ? "" : line[(space + 1)..].Trim();
            if (keyword is "msgid_plural" || keyword.StartsWith("msgstr[", StringComparison.Ordinal))
            {
                problems.Add($"{origin}:{number}: plural forms are not used by the catalogs");
                continue;
            }
            if (keyword is not ("msgctxt" or "msgid" or "msgstr"))
            {
                problems.Add($"{origin}:{number}: unknown keyword '{keyword}'");
                continue;
            }
            if ((keyword == "msgctxt" || keyword == "msgid") && (str != null || (keyword == "msgctxt" && id != null)))
                Flush();
            if (id == null && context == null) start = number;
            if (!TryUnquote(rest, out string value, out string? problem))
            {
                problems.Add($"{origin}:{number}: {problem}");
                continue;
            }
            if (isObsolete) obsolete = true;
            field = keyword;
            switch (keyword)
            {
                case "msgctxt": context = value; break;
                case "msgid": id = value; break;
                default: str = value; break;
            }
        }
        Flush();
        if (problems.Count > 0) throw new PoFileException(problems);
        return entries;
    }

    private static bool TryUnquote(string text, out string value, out string? error)
    {
        value = "";
        error = null;
        if (text.Length < 2 || text[0] != '"' || text[^1] != '"')
        {
            error = "expected a quoted string";
            return false;
        }
        var result = new StringBuilder(text.Length);
        for (int i = 1; i < text.Length - 1; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                error = "an unescaped quote inside a string";
                return false;
            }
            if (c != '\\')
            {
                result.Append(c);
                continue;
            }
            if (++i >= text.Length - 1)
            {
                error = "a string ends with a backslash";
                return false;
            }
            char next = text[i];
            switch (next)
            {
                case 'n': result.Append('\n'); break;
                case 't': result.Append('\t'); break;
                case 'r': result.Append('\r'); break;
                case 'a': result.Append('\a'); break;
                case 'b': result.Append('\b'); break;
                case 'f': result.Append('\f'); break;
                case 'v': result.Append('\v'); break;
                case '\\' or '"' or '\'' or '?': result.Append(next); break;
                case 'x':
                {
                    int end = i + 1;
                    while (end < text.Length - 1 && end < i + 3 && Uri.IsHexDigit(text[end])) end++;
                    if (end == i + 1)
                    {
                        error = "\\x without hex digits";
                        return false;
                    }
                    result.Append((char)int.Parse(text[(i + 1)..end], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i = end - 1;
                    break;
                }
                case >= '0' and <= '7':
                {
                    int end = i;
                    while (end < text.Length - 1 && end < i + 3 && text[end] is >= '0' and <= '7') end++;
                    result.Append((char)Convert.ToInt32(text[i..end], 8));
                    i = end - 1;
                    break;
                }
                default:
                    error = $"unknown escape \\{next}";
                    return false;
            }
        }
        value = result.ToString();
        return true;
    }
}

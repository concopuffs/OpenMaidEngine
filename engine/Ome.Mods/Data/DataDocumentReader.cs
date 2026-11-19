using Ome.Engine.Modding;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Ome.Mods.Data;

/// <summary>A problem of a data file: at a line where the parser gives one, else at a path in its text.</summary>
public sealed record DataProblem(int? Line, string Text)
{
    /// <summary><c>data/a.toml:3: TEXT</c>, or <c>data/a.toml: TEXT</c>.</summary>
    public string Describe(string origin) => Line is { } line ? $"{origin}:{line}: {Text}" : $"{origin}: {Text}";
}

/// <summary>A data file's document and the values it holds, counted as spec §5.3 counts them.</summary>
public sealed record DataDocument(DataTable Root, int Values);

/// <summary>What <see cref="DataDocumentReader.Read"/> gives: the document when the file has no problem, else the
/// first <see cref="DataDocumentReader.ProblemsKept"/> problems and how many there were.</summary>
public sealed record DataReadResult(DataDocument? Document, IReadOnlyList<DataProblem> Problems, int ProblemCount)
{
    public bool Ok => Document != null;

    /// <summary>The syntax tree of the validating parse, kept with a document so that a shape's check can give each
    /// problem its line without parsing the text again (plan §6.2); null without a document.</summary>
    internal DocumentSyntax? Syntax { get; init; }
}

/// <summary>
/// Reads a data file (data patches spec §5.3): TOML 1.0 in UTF-8, into <see cref="DataValue"/>s, inside the bounds of
/// <see cref="DataBounds"/>. The walk is by hand over Tomlyn's model, with no reflection. Three passes:
/// <list type="number">
/// <item>Tomlyn's syntax parser with its validation, for a syntax error and a key given twice, each with its line.
/// The serializer's model alone takes a repeated key, the last one winning (ran: plan §10).</item>
/// <item>Each integer as it is written, with its line: the model holds a <c>long</c>, and Tomlyn wraps an integer from
/// 2^63 to 2^64 - 1 into one, so <c>18446744073709551615</c> would read as -1 (ran: plan §10).</item>
/// <item>The model, walked into plain values: a date or a time, a float that is not finite, a key with a control
/// character or past the key bound, a nesting past the depth bound and a count past the value bound are each refused
/// with the path.</item>
/// </list>
/// </summary>
public static class DataDocumentReader
{
    /// <summary>The problems a result keeps; the rest are counted.</summary>
    public const int ProblemsKept = 20;

    /// <summary>How deep Tomlyn's parsers nest before they give up. Above <see cref="DataBounds.MaxDepth"/>, so that
    /// the walk reports a nesting past the bound with its path, and bounded, so that no file can make them recurse
    /// without end.</summary>
    private const int ParserDepth = 64;

    private static readonly TomlSerializerOptions ParserOptions = new() { MaxDepth = ParserDepth };

    /// <summary>Reads <paramref name="text"/>; <paramref name="origin"/> names the file in Tomlyn's own diagnostics.</summary>
    public static DataReadResult Read(string text, string origin)
    {
        var problems = new Problems();

        DocumentSyntax syntax;
        try
        {
            syntax = SyntaxParser.Parse(text, ParserOptions, origin, validate: true);
        }
        catch (TomlException error)
        {
            problems.Add(LineOf(error), Reason(error));
            return problems.Result(null);
        }
        if (syntax.HasErrors)
        {
            foreach (DiagnosticMessage diagnostic in syntax.Diagnostics)
                if (diagnostic.Kind == DiagnosticMessageKind.Error)
                    problems.Add(diagnostic.Span.Start.Line + 1, diagnostic.Message);
            if (problems.Count > 0) return problems.Result(null);
        }

        foreach (SyntaxNode node in syntax.Descendants())
            if (node is IntegerValueSyntax { Token.Text: { } written } integer && !WithinLimit(written))
                problems.Add(integer.Span.Start.Line + 1,
                             $"the integer {written} is past 2^53 in size, which Lua cannot hold as written");
        if (problems.Count > 0) return problems.Result(null);

        TomlTable model;
        try
        {
            model = TomlDocument.Model(text) ?? new TomlTable();
        }
        catch (TomlException error)
        {
            problems.Add(LineOf(error), Reason(error));
            return problems.Result(null);
        }

        var walk = new Walk(problems);
        DataTable? root = walk.Table(model, depth: 1);
        if (root == null || problems.Count > 0) return problems.Result(null);
        return problems.Result(new DataDocument(root, walk.Values)) with { Syntax = syntax };
    }

    /// <summary>Whether an integer as TOML writes it (sign, prefix and underscores) is at most 2^53 in size. The
    /// magnitude is built digit by digit and given up as soon as it passes the limit, so it never overflows.</summary>
    internal static bool WithinLimit(string written)
    {
        ReadOnlySpan<char> digits = written.AsSpan();
        if (digits.Length > 0 && digits[0] is '+' or '-') digits = digits[1..];
        int radix = 10;
        if (digits.Length > 2 && digits[0] == '0' && digits[1] is 'x' or 'o' or 'b')
        {
            radix = digits[1] switch { 'x' => 16, 'o' => 8, _ => 2 };
            digits = digits[2..];
        }
        ulong magnitude = 0;
        foreach (char c in digits)
        {
            if (c == '_') continue;
            int digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => int.MaxValue,
            };
            if (digit >= radix) return false;
            magnitude = magnitude * (ulong)radix + (ulong)digit;
            if (magnitude > DataInteger.Limit) return false;
        }
        return true;
    }

    private static int? LineOf(TomlException error) => error.Line > 0 ? error.Line : null;

    private static string Reason(TomlException error)
    {
        string message = error.Message;
        if (message.Contains("maximum depth", StringComparison.Ordinal))
            return $"tables and arrays nest deeper than {DataBounds.MaxDepth}";
        // Tomlyn prefixes its position, "(2,5) : error : ", which the line already gives.
        int reason = message.LastIndexOf(" : error : ", StringComparison.Ordinal);
        return (reason < 0 ? message : message[(reason + " : error : ".Length)..]).Split('\n')[0].Trim();
    }

    /// <summary>The first problems, and how many.</summary>
    private sealed class Problems
    {
        private readonly List<DataProblem> _kept = [];

        public int Count { get; private set; }

        public void Add(int? line, string text)
        {
            if (Count++ < ProblemsKept) _kept.Add(new DataProblem(line, text));
        }

        public DataReadResult Result(DataDocument? document) => new(document, _kept, Count);
    }

    /// <summary>The model to plain values, with the path of each problem. The path is a stack of keys, printed only
    /// for a problem.</summary>
    private sealed class Walk(Problems problems)
    {
        private readonly List<string> _path = [];
        private bool _tooMany;

        public int Values { get; private set; }

        public DataTable? Table(TomlTable table, int depth)
        {
            if (!Within(depth)) return null;
            var entries = new List<KeyValuePair<string, DataValue>>(table.Count);
            foreach (var (key, value) in table)
            {
                if (_tooMany) return null;
                if (key.Length > DataBounds.MaxKeyLength)
                {
                    problems.Add(null, $"{Here()}: a key of {key.Length} characters starts {DataValue.KeyText(key[..32])}; "
                                       + $"a key is at most {DataBounds.MaxKeyLength} characters");
                    continue;
                }
                _path.Add(DataValue.KeyText(key));
                if (FirstControl(key) is >= 0 and var control)
                    problems.Add(null, $"{Here()}: a key holds a control character (U+{control:X4})");
                else if (Value(value, depth) is { } converted)
                    entries.Add(new KeyValuePair<string, DataValue>(key, converted));
                _path.RemoveAt(_path.Count - 1);
            }
            return new DataTable(entries);
        }

        /// <summary>The first control character of <paramref name="key"/>, or -1.</summary>
        private static int FirstControl(string key)
        {
            foreach (char c in key)
                if (char.IsControl(c)) return c;
            return -1;
        }

        private DataArray? Array(IEnumerable<object?> items, int depth)
        {
            if (!Within(depth)) return null;
            var converted = new List<DataValue>();
            int index = 0;
            foreach (object? item in items)
            {
                if (_tooMany) return null;
                _path.Add($"[{++index}]");
                if (Value(item, depth) is { } value) converted.Add(value);
                _path.RemoveAt(_path.Count - 1);
            }
            return new DataArray(converted);
        }

        /// <summary>A value inside a table or array at <paramref name="depth"/>.</summary>
        private DataValue? Value(object? value, int depth)
        {
            switch (value)
            {
                case TomlTable table:
                    return Table(table, depth + 1);
                case TomlTableArray tables:
                    return Array(tables, depth + 1);
                case TomlArray array:
                    return Array(array, depth + 1);
                case TomlDateTime:
                    problems.Add(null, $"{Here()}: a date or a time has no value here");
                    return null;
            }
            if (!Count()) return null;
            switch (value)
            {
                case string text:
                    return new DataString(text);
                case bool flag:
                    return new DataBoolean(flag);
                case long integer when integer is >= -DataInteger.Limit and <= DataInteger.Limit:
                    return new DataInteger(integer);
                case long:
                    problems.Add(null, $"{Here()}: an integer past 2^53 in size has no value here");
                    return null;
                case double number when double.IsFinite(number):
                    return new DataFloat(number);
                case double:
                    problems.Add(null, $"{Here()}: a float that is not finite (inf or nan) has no value here");
                    return null;
                default:
                    problems.Add(null, $"{Here()}: a value Tomlyn reads as {value?.GetType().Name ?? "null"} has no value here");
                    return null;
            }
        }

        private bool Within(int depth)
        {
            if (depth <= DataBounds.MaxDepth) return true;
            problems.Add(null, $"{Here()}: tables and arrays nest deeper than {DataBounds.MaxDepth}");
            return false;
        }

        /// <summary>Counts one value; past the bound, says so once and stops the walk.</summary>
        private bool Count()
        {
            if (++Values <= DataBounds.MaxValues) return true;
            _tooMany = true;
            problems.Add(null, $"the document holds more than {DataBounds.MaxValues:N0} values; a data file holds at most that many");
            return false;
        }

        /// <summary>The path's text (plan §4.2): keys joined by dots, an array's element by its index from 1.</summary>
        private string Here()
        {
            if (_path.Count == 0) return "the document";
            var text = new System.Text.StringBuilder();
            foreach (string part in _path)
            {
                if (text.Length > 0 && part[0] != '[') text.Append('.');
                text.Append(part);
            }
            return text.ToString();
        }
    }
}

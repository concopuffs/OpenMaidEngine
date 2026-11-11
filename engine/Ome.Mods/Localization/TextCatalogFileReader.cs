using System.Text.RegularExpressions;
using Ome.Engine.Localization;
using Ome.Mods.Manifest;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Ome.Mods.Localization;

/// <summary>
/// Reads a catalog file (localization spec §9.3). Unknown keys, wrongly typed values and malformed placeholders are
/// errors, each reported with the file and the line of its <c>[[text]]</c> or <c>[[engine]]</c> table, so a typo
/// cannot drop an entry silently. The TOML library sits behind this class and the writer only.
/// </summary>
public static partial class TextCatalogFileReader
{
    /// <summary>Largest catalog file the reader accepts.</summary>
    public const int MaxBytes = 32 << 20;

    [GeneratedRegex(@"^[A-Za-z0-9_$]{1,64}(?:\.[Bb][Ii][Nn])?$")]
    private static partial Regex ScriptNamePattern();

    [GeneratedRegex(@"^[a-z0-9]+(?:[._-][a-z0-9]+)*$")]
    private static partial Regex EngineKeyPattern();

    public static bool IsValidScriptName(string name) => ScriptNamePattern().IsMatch(name);

    public static TextCatalogFile Read(string path, string? origin = null)
    {
        var info = new FileInfo(path);
        string name = origin ?? path;
        if (info.Length > MaxBytes)
            throw new TextCatalogException([$"{name}: larger than {MaxBytes} bytes"]);
        return Parse(File.ReadAllText(path), name);
    }

    public static TextCatalogFile Parse(string text, string origin)
    {
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException error)
        {
            string at = error.Line is { } line ? $"{origin}:{line}" : origin;
            throw new TextCatalogException([$"{at}: not valid TOML: {error.Message}"]);
        }

        var problems = new List<string>();
        Dictionary<string, List<int>> tableLines = TableArrayLines(text, origin);
        var top = new Fields(root, origin, problems);
        string? fileScript = top.String("script");
        string? fingerprint = top.String("fingerprint");
        if (fileScript is { Length: > 0 } && !IsValidScriptName(fileScript))
            problems.Add($"{origin}: script '{fileScript}' is not a script name");
        if (fingerprint != null && !ManifestReader.IsValidFingerprint(fingerprint))
            problems.Add($"{origin}: fingerprint must be 'sha256:' and 64 lowercase hex digits");
        string? defaultScope = string.IsNullOrEmpty(fileScript) ? null : TextCatalog.NormalizeScriptName(fileScript);

        var entries = new List<TextEntry>();
        foreach ((TomlTable table, int k) in top.TableArray("text"))
        {
            string where = Where(origin, tableLines, "text", k);
            if (ReadEntry(new Fields(table, where, problems), defaultScope, fingerprint) is { } entry)
                entries.Add(entry with { Origin = where });
        }
        var engineEntries = new List<EngineTextEntry>();
        foreach ((TomlTable table, int k) in top.TableArray("engine"))
        {
            string where = Where(origin, tableLines, "engine", k);
            var fields = new Fields(table, where, problems);
            string? key = fields.String("key", required: true);
            string? value = fields.String("text", required: true);
            fields.RejectUnknown();
            if (key != null && !EngineKeyPattern().IsMatch(key))
                problems.Add($"{where}: key '{key}' must be lowercase words joined by '.', '-' or '_'");
            else if (key != null && value != null) engineEntries.Add(new EngineTextEntry(key, value, where));
        }
        top.RejectUnknown();
        if (problems.Count > 0) throw new TextCatalogException(problems);
        return new TextCatalogFile(defaultScope, fingerprint, entries, engineEntries);
    }

    private static TextEntry? ReadEntry(Fields fields, string? defaultScope, string? fingerprint)
    {
        string? source = fields.String("source", required: true);
        string? text = fields.String("text", required: true);
        string? script = fields.String("script");
        long? at = fields.Integer("at");
        string? align = fields.String("align");
        string? fit = fields.String("fit");
        string? measure = fields.String("measure");
        long? cells = fields.Integer("cells");
        string? note = fields.String("note");
        fields.RejectUnknown();
        if (source == null || text == null) return null;

        string? scope = script == null ? defaultScope
            : script.Length == 0 ? null
            : IsValidScriptName(script) ? TextCatalog.NormalizeScriptName(script)
            : fields.Problem($"script '{script}' is not a script name");
        if (!TextEntry.TryCreate(source, text, out TextEntry entry, out string? error))
            return fields.Problem<TextEntry>(error!);
        if (at is { } offset)
        {
            if (offset < 0 || offset > int.MaxValue) return fields.Problem<TextEntry>("at must be a dword offset");
            if (scope == null) return fields.Problem<TextEntry>("an entry with 'at' needs a script scope");
            if (fingerprint == null)
                return fields.Problem<TextEntry>("an entry with 'at' needs the file's 'fingerprint' of the base script");
        }
        if (entry.IsPattern && scope == null && (!entry.SourceTemplate.StartsWithLiteral
                                                 || !entry.SourceTemplate.EndsWithLiteral))
            return fields.Problem<TextEntry>(
                "a pattern that starts or ends with a placeholder needs a script scope");
        if (note is { Length: > TextCatalogLimits.MaxStringLength })
            return fields.Problem<TextEntry>($"note is limited to {TextCatalogLimits.MaxStringLength} characters");
        if (cells is { } count && (count < 0 || count > TextCatalogLimits.MaxCells))
            return fields.Problem<TextEntry>($"cells must be 0 to {TextCatalogLimits.MaxCells}");

        TextAlign alignment = align switch
        {
            null or "start" => TextAlign.Start,
            "center" => TextAlign.Center,
            "end" => TextAlign.End,
            _ => fields.Problem<TextAlign>($"align must be \"start\", \"center\" or \"end\", not \"{align}\""),
        };
        TextFit fitting = fit switch
        {
            null or "none" => TextFit.None,
            "shrink" => TextFit.Shrink,
            _ => fields.Problem<TextFit>($"fit must be \"none\" or \"shrink\", not \"{fit}\""),
        };
        TextMeasure? measuring = measure switch
        {
            null => null,
            "presented" => TextMeasure.Presented,
            "source" => TextMeasure.Source,
            _ => fields.Problem<TextMeasure?>($"measure must be \"presented\" or \"source\", not \"{measure}\""),
        };
        return entry with
        {
            Script = scope,
            At = at is { } value ? (int)value : null,
            Align = alignment,
            Fit = fitting,
            Measure = measuring,
            Cells = cells is { } c ? (int)c : null,
            Note = note,
        };
    }

    private static string Where(string origin, Dictionary<string, List<int>> lines, string table, int index)
        => lines.TryGetValue(table, out List<int>? starts) && index < starts.Count
            ? $"{origin}:{starts[index]}"
            : $"{origin}: [[{table}]] {index + 1}";

    /// <summary>The 1-based line of each top-level <c>[[name]]</c> header, in order.</summary>
    private static Dictionary<string, List<int>> TableArrayLines(string text, string origin)
    {
        var lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        DocumentSyntax document = SyntaxParser.Parse(text, origin, false);
        foreach (TableSyntaxBase table in document.Tables)
        {
            if (table is not TableArraySyntax || table.Name == null) continue;
            string name = table.Name.ToString().Trim();
            if (!lines.TryGetValue(name, out List<int>? starts)) lines[name] = starts = [];
            starts.Add(table.Span.Start.Line + 1);
        }
        return lines;
    }

    /// <summary>Typed access to one TOML table's keys, recording each problem and every key read, so that
    /// <see cref="RejectUnknown"/> can report the rest.</summary>
    private sealed class Fields(TomlTable table, string where, List<string> problems)
    {
        private readonly HashSet<string> _read = new(StringComparer.Ordinal);

        public string? String(string key, bool required = false)
        {
            object? value = Get(key, required);
            if (value is null) return null;
            if (value is string text) return text;
            problems.Add($"{where}: {key} must be a string");
            return null;
        }

        public long? Integer(string key)
        {
            object? value = Get(key, false);
            if (value is null) return null;
            if (value is long number) return number;
            problems.Add($"{where}: {key} must be an integer");
            return null;
        }

        public IEnumerable<(TomlTable Table, int Index)> TableArray(string key)
        {
            object? value = Get(key, false);
            if (value is null) return [];
            if (value is TomlTableArray array) return array.Select((item, k) => (item, k)).ToArray();
            problems.Add($"{where}: {key} must be an array of tables ([[{key}]])");
            return [];
        }

        public void RejectUnknown()
        {
            foreach (string key in table.Keys.Where(key => !_read.Contains(key)))
                problems.Add($"{where}: unknown key '{key}'");
        }

        public string? Problem(string message)
        {
            problems.Add($"{where}: {message}");
            return null;
        }

        public T Problem<T>(string message)
        {
            problems.Add($"{where}: {message}");
            return default!;
        }

        private object? Get(string key, bool required)
        {
            _read.Add(key);
            if (table.TryGetValue(key, out object? value)) return value;
            if (required) problems.Add($"{where}: {key} is required");
            return null;
        }
    }
}

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
        var items = new List<TextCatalogItem>();
        foreach ((TomlTable table, int k) in top.TableArray("text"))
        {
            string where = Where(origin, tableLines, "text", k);
            if (ReadEntry(new Fields(table, where, problems), defaultScope, fingerprint) is not { } item) continue;
            item = item with { Entry = item.Entry with { Origin = where } };
            items.Add(item);
            if (item.Applies) entries.Add(item.Entry);
        }
        var engineEntries = new List<EngineTextEntry>();
        foreach ((TomlTable table, int k) in top.TableArray("engine"))
        {
            string where = Where(origin, tableLines, "engine", k);
            var fields = new Fields(table, where, problems);
            string? key = fields.String("key", required: true);
            string? value = fields.String("text");
            string? note = fields.String("note");
            fields.RejectUnknown();
            if (key != null && !EngineKeyPattern().IsMatch(key))
                problems.Add($"{where}: key '{key}' must be lowercase words joined by '.', '-' or '_'");
            else if (key != null) engineEntries.Add(new EngineTextEntry(key, value, where, note));
        }
        top.RejectUnknown();
        if (problems.Count > 0) throw new TextCatalogException(problems);
        return new TextCatalogFile(defaultScope, fingerprint, entries, engineEntries) { Items = items };
    }

    /// <summary>Largest <c>glyph_delay_scale</c>: four times the source's delay.</summary>
    public const double MaxGlyphDelayScale = 4.0;

    /// <summary>
    /// The <c>[[language]]</c> tables of a <c>languages.toml</c> (spec §9.2), read as a manifest's are
    /// (<see cref="ReadLanguage"/>). Unknown keys are errors, as in a catalog file.
    /// </summary>
    public static IReadOnlyList<LanguageSettings> ParseLanguages(string text, string origin)
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
        var languages = new List<LanguageSettings>();
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach ((TomlTable table, int k) in top.TableArray("language"))
            if (ReadLanguage(table, Where(origin, tableLines, "language", k), problems, tags) is { } language)
                languages.Add(language);
        top.RejectUnknown();
        if (problems.Count > 0) throw new TextCatalogException(problems);
        return languages;
    }

    /// <summary>
    /// One <c>[[language]]</c> table (spec §9.2), from a manifest or a <c>languages.toml</c>: <c>tag</c>, and
    /// optionally <c>name</c>, <c>fallback</c>, <c>line_break</c>, <c>measure</c>, <c>digits</c>,
    /// <c>glyph_delay_scale</c>, <c>min_fit_scale</c> and <c>[[language.font]]</c> tables (<c>requested</c>,
    /// <c>use</c>). Each problem is added to <paramref name="problems"/> prefixed with <paramref name="where"/>;
    /// <paramref name="tags"/> collects the tags read so far, to report one declared twice.
    /// </summary>
    public static LanguageSettings? ReadLanguage(TomlTable table, string where, List<string> problems,
                                                 HashSet<string> tags)
    {
        var fields = new Fields(table, where, problems);
        string? tag = fields.String("tag", required: true);
        string? name = fields.String("name");
        IReadOnlyList<string>? fallback = fields.Strings("fallback");
        string? lineBreak = fields.String("line_break");
        string? measure = fields.String("measure");
        string? digits = fields.String("digits");
        double? delayScale = fields.Number("glyph_delay_scale");
        double? fitScale = fields.Number("min_fit_scale");
        var fontTables = fields.TableArray("font").ToArray();
        fields.RejectUnknown();
        if (tag == null) return null;
        string normalized = LanguageTag.Normalize(tag);
        if (!LanguageTag.IsWellFormed(normalized) || normalized == LanguageTag.Source || normalized == LanguageTag.Pseudo)
            return fields.Problem<LanguageSettings>($"tag '{tag}' is not a language a package can define");
        if (!tags.Add(normalized)) fields.Problem($"language '{normalized}' is declared twice");
        List<string>? chain = null;
        if (fallback != null)
        {
            chain = [];
            foreach (string parent in fallback)
            {
                string next = LanguageTag.Normalize(parent);
                if (LanguageTag.IsWellFormed(next) && next != LanguageTag.Pseudo && next != normalized)
                    chain.Add(next);
                else fields.Problem($"fallback '{parent}' is not another language's tag");
            }
        }
        if (delayScale is { } delay && !(delay > 0 && delay <= MaxGlyphDelayScale))
            fields.Problem($"glyph_delay_scale must be above 0 and at most {MaxGlyphDelayScale}");
        if (fitScale is { } fit && !(fit > 0 && fit <= 1))
            fields.Problem("min_fit_scale must be above 0 and at most 1");
        List<LanguageFont>? fonts = null;
        foreach ((TomlTable fontTable, int k) in fontTables)
        {
            var font = new Fields(fontTable, $"{where}: font {k + 1}", problems);
            IReadOnlyList<string>? requested = font.Strings("requested");
            IReadOnlyList<string>? use = font.Strings("use");
            font.RejectUnknown();
            if (requested is not { Count: > 0 }) font.Problem("requested must name at least one face, or \"*\"");
            if (use is not { Count: > 0 }) font.Problem("use must name at least one font file or family");
            foreach (string entry in use ?? [])
                if (entry.StartsWith(LanguageFont.FontsFolder, StringComparison.OrdinalIgnoreCase)
                    && !IsFontFileName(entry[LanguageFont.FontsFolder.Length..]))
                    font.Problem($"'{entry}' is not a font file under fonts/ ({string.Join(", ", LanguageFont.Extensions)})");
                else if (entry.Trim().Length == 0) font.Problem("use holds an empty name");
            if (requested is { Count: > 0 } && use is { Count: > 0 })
                (fonts ??= []).Add(new LanguageFont(requested, use));
        }
        return new LanguageSettings(normalized)
        {
            Name = name,
            Fallback = chain,
            LineBreak = lineBreak switch
            {
                null => null,
                "words" => LineBreakMode.Words,
                "anywhere" => LineBreakMode.Anywhere,
                _ => fields.Problem<LineBreakMode?>($"line_break must be \"words\" or \"anywhere\", not \"{lineBreak}\""),
            },
            Measure = measure switch
            {
                null => null,
                "presented" => TextMeasure.Presented,
                "source" => TextMeasure.Source,
                _ => fields.Problem<TextMeasure?>($"measure must be \"presented\" or \"source\", not \"{measure}\""),
            },
            Digits = digits switch
            {
                null => null,
                "keep" => DigitsMode.Keep,
                "ascii" => DigitsMode.Ascii,
                _ => fields.Problem<DigitsMode?>($"digits must be \"keep\" or \"ascii\", not \"{digits}\""),
            },
            GlyphDelayScale = delayScale,
            MinFitScale = fitScale,
            Fonts = fonts,
            Origin = where,
        };
    }

    /// <summary>A bundled font's file name: a basename with one of <see cref="LanguageFont.Extensions"/>.</summary>
    public static bool IsFontFileName(string name)
        => name.Length > 0 && !name.Contains('/') && !name.Contains('\\') && name.Trim() == name
           && LanguageFont.Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                                                       && name.Length > extension.Length);

    /// <summary>One <c>[[text]]</c> table. <c>text</c> is optional: an entry without it is a template, checked as if
    /// its text were its source.</summary>
    private static TextCatalogItem? ReadEntry(Fields fields, string? defaultScope, string? fingerprint)
    {
        string? source = fields.String("source", required: true);
        string? text = fields.String("text");
        bool obsolete = fields.Boolean("obsolete") ?? false;
        string? script = fields.String("script");
        long? at = fields.Integer("at");
        string? align = fields.String("align");
        string? fit = fields.String("fit");
        string? measure = fields.String("measure");
        long? cells = fields.Integer("cells");
        long? rowGap = fields.Integer("row_gap");
        string? note = fields.String("note");
        fields.RejectUnknown();
        if (source == null) return null;

        string? scope = script == null ? defaultScope
            : script.Length == 0 ? null
            : IsValidScriptName(script) ? TextCatalog.NormalizeScriptName(script)
            : fields.Problem($"script '{script}' is not a script name");
        if (!TextEntry.TryCreate(source, text ?? TextTemplate.UntranslatedText(source), out TextEntry entry, out string? error))
            return fields.Problem<TextCatalogItem>(error!);
        if (at is { } offset)
        {
            if (offset < 0 || offset > int.MaxValue) return fields.Problem<TextCatalogItem>("at must be a dword offset");
            if (scope == null) return fields.Problem<TextCatalogItem>("an entry with 'at' needs a script scope");
            if (fingerprint == null)
                return fields.Problem<TextCatalogItem>("an entry with 'at' needs the file's 'fingerprint' of the base script");
        }
        if (entry.IsPattern && scope == null && (!entry.SourceTemplate.StartsWithLiteral
                                                 || !entry.SourceTemplate.EndsWithLiteral))
            return fields.Problem<TextCatalogItem>(
                "a pattern that starts or ends with a placeholder needs a script scope");
        if (note is { Length: > TextCatalogLimits.MaxStringLength })
            return fields.Problem<TextCatalogItem>($"note is limited to {TextCatalogLimits.MaxStringLength} characters");
        if (cells is { } count && (count < 0 || count > TextCatalogLimits.MaxCells))
            return fields.Problem<TextCatalogItem>($"cells must be 0 to {TextCatalogLimits.MaxCells}");
        if (rowGap is { } gap && (gap < 0 || gap > TextCatalogLimits.MaxRowGap))
            return fields.Problem<TextCatalogItem>($"row_gap must be 0 to {TextCatalogLimits.MaxRowGap}");

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
        return new TextCatalogItem(entry with
        {
            Script = scope,
            At = at is { } value ? (int)value : null,
            Align = alignment,
            Fit = fitting,
            Measure = measuring,
            Cells = cells is { } c ? (int)c : null,
            RowGap = rowGap is { } g ? (int)g : null,
            Note = note,
        }, text != null, obsolete);
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

        public bool? Boolean(string key)
        {
            object? value = Get(key, false);
            if (value is null) return null;
            if (value is bool flag) return flag;
            problems.Add($"{where}: {key} must be true or false");
            return null;
        }

        public double? Number(string key)
        {
            object? value = Get(key, false);
            if (value is null) return null;
            if (value is double real && double.IsFinite(real)) return real;
            if (value is long whole) return whole;
            problems.Add($"{where}: {key} must be a number");
            return null;
        }

        public IReadOnlyList<string>? Strings(string key)
        {
            object? value = Get(key, false);
            if (value is null) return null;
            if (value is TomlArray array && array.All(item => item is string))
                return array.Cast<string>().ToArray();
            problems.Add($"{where}: {key} must be an array of strings");
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

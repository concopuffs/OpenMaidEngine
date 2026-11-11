using System.Text;
using Ome.Engine.Localization;
using Ome.Mods.Linking;
using Ome.Mods.Localization;

namespace Ome.Mods.Tools;

/// <summary>
/// The catalogs <c>omp text check</c> and <c>stats</c> read (localization spec §13): the packages' text through an
/// inspection link, then a text root's (<c>--text-root</c>), in that order, with the languages both declare merged
/// per key and every bundled font's bytes.
/// </summary>
public sealed class TextSet
{
    public List<(string Language, string Origin, TextCatalogFile File)> Files { get; } = [];
    public Dictionary<string, LanguageSettings> Languages { get; } = new(StringComparer.Ordinal);
    /// <summary>Bundled fonts' bytes, by the <c>use</c> entry the merged settings name them with.</summary>
    public Dictionary<string, byte[]> Fonts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Problems { get; } = [];

    /// <summary>The tags with catalogs or settings, in ordinal order.</summary>
    public IReadOnlyList<string> Tags
        => Files.Select(file => file.Language).Concat(Languages.Keys).Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>A link's text. A file that does not parse is left out: the link reported it.</summary>
    public void AddLink(ModLinkResult result)
    {
        foreach (LinkedLanguage language in result.Text.Languages) Merge(language.Settings);
        foreach (LinkedFont font in result.Text.Fonts) Fonts[font.CachePath] = font.Bytes;
        foreach (LinkedTextFile file in result.Text.Files)
        {
            string text = Encoding.UTF8.GetString(file.Bytes);
            if (text.Length > 0 && text[0] == '﻿') text = text[1..];
            try
            {
                Files.Add((file.Language, file.Origin, TextCatalogFileReader.Parse(text, file.Origin)));
            }
            catch (TextCatalogException) { }
        }
    }

    /// <summary>A text root: its language folders, its <c>languages.toml</c> and its <c>fonts/</c>.</summary>
    public void AddRoot(string root)
    {
        try
        {
            foreach (LanguageSettings language in TextRootLoader.ReadLanguages(root))
            {
                Merge(language);
                foreach (string use in (language.Fonts ?? []).SelectMany(font => font.Use).Where(Path.IsPathFullyQualified))
                    Fonts[use] = File.ReadAllBytes(use);
            }
        }
        catch (TextCatalogException error)
        {
            Problems.AddRange(error.Problems);
        }
        foreach (string folder in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            string tag = Path.GetFileName(folder);
            if (tag.Equals(TextRootLoader.FontsFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (tag != LanguageTag.Normalize(tag) || !LanguageTag.IsWellFormed(tag))
            {
                Problems.Add($"{tag}/: '{tag}' is not a lowercase language tag");
                continue;
            }
            foreach (string path in Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories)
                         .OrderBy(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), StringComparer.Ordinal))
            {
                string origin = tag + "/" + Path.GetRelativePath(folder, path).Replace('\\', '/');
                try
                {
                    Files.Add((tag, origin, TextCatalogFileReader.Read(path, origin)));
                }
                catch (TextCatalogException error)
                {
                    Problems.AddRange(error.Problems);
                }
            }
        }
    }

    private void Merge(LanguageSettings settings)
        => Languages[settings.Tag] = Languages.TryGetValue(settings.Tag, out LanguageSettings? earlier)
            ? earlier.Merge(settings).Merged
            : settings;

    /// <summary>One language's catalog, its files in order.</summary>
    /// <exception cref="ArgumentException">The language holds more entries than a catalog may.</exception>
    public TextCatalog Catalog(string tag)
        => TextCatalog.Build(tag, Files.Where(file => file.Language == tag).SelectMany(file => file.File.Entries));

    /// <summary>The catalogs of <paramref name="tag"/>'s fallback chain that have files.</summary>
    public IReadOnlyList<TextCatalog> Chain(string tag)
    {
        IEnumerable<string> fallback = Languages.TryGetValue(tag, out LanguageSettings? settings) && settings.Fallback != null
            ? settings.Fallback : LanguageTag.Parents(tag);
        return new[] { tag }.Concat(fallback).Where(language => Files.Any(file => file.Language == language))
            .Select(Catalog).ToList();
    }
}

/// <summary><c>omp text extract</c>, <c>check</c> and <c>stats</c> (localization spec §13).</summary>
public static class TextCommands
{
    private const int Examples = 5;

    /// <summary>
    /// <c>check</c> or <c>stats</c> over a stack and a text root: the data root's mods (when given) and the extra
    /// packages are linked as <c>omp check</c> links them, and <c>check</c> prints that report first; then the text
    /// root's catalogs come after the packages'. A stack that does not link fails <c>check</c>.
    /// </summary>
    public static int Run(bool stats, ModLinkBase linkBase, string? dataRoot, IReadOnlyList<string> packages,
                          string? textRoot, ScriptTextCorpus? corpus, string? only, bool perScript, TextWriter console)
    {
        var set = new TextSet();
        bool linked = true;
        IReadOnlyList<Packages.ModPackage> opened = [];
        try
        {
            if (dataRoot != null || packages.Count > 0)
            {
                (ModLinkResult? result, opened) = OmpCommands.LinkForInspection(linkBase, dataRoot, packages, false, console);
                if (result == null) return 1;
                if (!stats) console.Write(result.Report.Render());
                linked = result.Succeeded;
                set.AddLink(result);
            }
            if (textRoot != null) set.AddRoot(textRoot);
            int status = stats ? Stats(set, corpus!, only, perScript, console) : Check(set, corpus, only, console);
            if (!linked) console.WriteLine("omp text: the stack does not link (see the report above)");
            return linked ? status : 1;
        }
        finally
        {
            foreach (Packages.ModPackage package in opened) package.Dispose();
        }
    }

    /// <summary>Writes catalog templates for <paramref name="language"/> under
    /// <c><paramref name="outputRoot"/>/text/&lt;language&gt;/</c> (<see cref="TextTemplateExtractor"/>).</summary>
    public static int Extract(ScriptTextCorpus corpus, string language, string outputRoot, string origin, TextWriter console)
    {
        string folder = Path.Combine(Path.GetFullPath(outputRoot), "text", language);
        var existing = new Dictionary<string, TextCatalogFile>(StringComparer.Ordinal);
        var problems = new List<string>();
        if (Directory.Exists(folder))
            foreach (string path in Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories))
            {
                string name = Path.GetRelativePath(folder, path).Replace('\\', '/');
                try
                {
                    existing[name] = TextCatalogFileReader.Read(path, $"{language}/{name}");
                }
                catch (TextCatalogException error)
                {
                    problems.AddRange(error.Problems);
                }
            }
        if (problems.Count > 0)
        {
            foreach (string problem in problems) console.WriteLine($"error: {problem}");
            console.WriteLine("omp text extract: the folder's catalogs must read before extract adds to them; nothing written");
            return 1;
        }
        var counts = new ExtractCounts();
        IReadOnlyDictionary<string, TextCatalogFile> files = TextTemplateExtractor.Build(corpus, existing, counts);
        string[] header =
        [
            $"Catalog template written by omp text extract from {origin}.",
            "An entry without text is not used: add text = \"...\" to translate it. An entry marked obsolete is no longer "
            + "in the scripts. Extract rewrites the files it changes without their comments; keep notes in note.",
        ];
        foreach (var (name, file) in files)
        {
            string path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, TextCatalogFileWriter.Write(file, header), new UTF8Encoding(false));
        }
        foreach (string script in corpus.Unreadable) console.WriteLine($"note: {script} could not be decoded; it has no template");
        console.WriteLine($"omp text extract: {corpus.Scripts.Count} scripts -> {folder}: {counts.FilesWritten} files written, "
                          + $"{counts.FilesUnchanged} unchanged; {counts.NewTemplates} new templates, {counts.KeptTranslated} "
                          + $"translated and {counts.KeptUntranslated} untranslated entries kept, {counts.MarkedObsolete} "
                          + $"marked obsolete, {counts.Revived} no longer obsolete");
        return 0;
    }

    /// <summary>
    /// Validates catalogs without starting the game: the files read (syntax, keys, placeholders on both sides),
    /// no two patterns of a language can match one string, every exact entry's source is drawn or stored where its
    /// scope reaches, site entries name the installed revision, and the bundled fonts cover the presented text.
    /// Errors (a file that does not read, ambiguous patterns, too many entries) make it fail; the rest are warnings.
    /// </summary>
    public static int Check(TextSet set, ScriptTextCorpus? corpus, string? only, TextWriter console)
    {
        int errors = 0, warnings = 0;
        void Error(string message)
        {
            errors++;
            console.WriteLine($"error: {message}");
        }
        void Warning(string message)
        {
            warnings++;
            console.WriteLine($"warning: {message}");
        }
        foreach (string problem in set.Problems) Error(problem);
        foreach (string tag in set.Tags.Where(tag => only == null || tag == only))
        {
            var files = set.Files.Where(file => file.Language == tag).ToList();
            TextCatalog catalog;
            try
            {
                catalog = set.Catalog(tag);
            }
            catch (ArgumentException error)
            {
                Error($"text {tag}: {error.Message}");
                continue;
            }
            int templates = files.Sum(file => file.File.Items.Count(item => !item.HasText));
            int obsolete = files.Sum(file => file.File.Items.Count(item => item.Obsolete));
            console.WriteLine($"text {tag}: {catalog.Count} entries from {files.Count} files"
                              + (templates > 0 ? $", {templates} without text" : "")
                              + (obsolete > 0 ? $", {obsolete} obsolete" : "")
                              + (set.Languages.ContainsKey(tag) ? "" : " (no [[language]] table: defaults)"));
            foreach (AmbiguousPatterns pair in TextPatternOverlap.Find(catalog))
                Error($"text {tag}: {pair.First.Origin} and {pair.Second.Origin}: the patterns \"{pair.First.Source}\" and "
                      + $"\"{pair.Second.Source}\" can match one string; give one a narrower scope or more literal text");
            if (corpus != null)
                foreach (var (_, origin, file) in files) CheckReach(origin, file, corpus, Warning);
            if (set.Languages.TryGetValue(tag, out LanguageSettings? settings))
                CheckCoverage(tag, settings, catalog, set, Warning);
        }
        if (corpus == null) console.WriteLine("note: no installed scripts given; sources and sites were not checked");
        console.WriteLine($"omp text check: {(errors == 0 ? "passes" : "fails")}: {errors} errors, {warnings} warnings");
        return errors == 0 ? 0 : 1;
    }

    private static void CheckReach(string origin, TextCatalogFile file, ScriptTextCorpus corpus, Action<string> warning)
    {
        var sites = file.Entries.Where(entry => entry.At != null).GroupBy(entry => entry.Script!);
        var unbound = new HashSet<TextEntry>(ReferenceEqualityComparer.Instance);
        foreach (var group in sites)
        {
            string? reason = !corpus.Fingerprints.TryGetValue(group.Key, out string? installed) ? "the script is not installed"
                : !string.Equals(installed, file.Fingerprint, StringComparison.OrdinalIgnoreCase)
                    ? $"the file names {file.Fingerprint}, the installed script is {installed}"
                    : null;
            if (reason == null) continue;
            unbound.UnionWith(group);
            warning($"{origin}: {Count(group.Count(), "site entry", "site entries")} for {group.Key} "
                    + $"{(group.Count() == 1 ? "does" : "do")} not bind: {reason}");
        }
        var misses = file.Entries.Where(entry => !unbound.Contains(entry) && !corpus.Reaches(entry)).ToList();
        if (misses.Count == 0) return;
        warning($"{origin}: {Count(misses.Count, "entry matches", "entries match")} nothing in the installed scripts: "
                + string.Join("; ", misses.Take(Examples).Select(entry => $"{Line(entry)} \"{Short(entry.Source)}\""))
                + (misses.Count > Examples ? $"; {misses.Count - Examples} more" : ""));
    }

    private static void CheckCoverage(string tag, LanguageSettings settings, TextCatalog catalog, TextSet set,
                                      Action<string> warning)
    {
        var characters = new Dictionary<int, TextEntry>();
        foreach (TextEntry entry in catalog.Entries)
            foreach (TemplatePart part in entry.TextTemplate.Parts)
                if (part is LiteralPart literal)
                    for (int k = 0; k < literal.Text.Length; k += char.IsSurrogatePair(literal.Text, k) ? 2 : 1)
                    {
                        int c = char.ConvertToUtf32(literal.Text, k);
                        if (c >= 0x20 && c != 0x7f) characters.TryAdd(c, entry);
                    }
        foreach (LanguageFont table in settings.Fonts ?? [])
        {
            var bundled = table.Use.Where(LanguageFont.IsBundled).ToList();
            if (bundled.Count == 0) continue;
            var coverage = new List<FontCoverage>();
            foreach (string use in bundled)
            {
                if (!set.Fonts.TryGetValue(use, out byte[]? data))
                {
                    warning($"text {tag}: {use} is not available to check");
                    continue;
                }
                try
                {
                    coverage.Add(FontCoverage.Read(data));
                }
                catch (InvalidDataException error)
                {
                    warning($"text {tag}: {use}: {error.Message}");
                }
            }
            if (coverage.Count == 0) continue;
            var missing = characters.Where(pair => !coverage.Any(font => font.Contains(pair.Key)))
                .OrderBy(pair => pair.Key).ToList();
            if (missing.Count == 0) continue;
            string faces = string.Join(", ", table.Requested);
            warning($"text {tag}: {Count(missing.Count, "character is", "characters are")} in none of the bundled fonts for {faces} "
                    + $"({string.Join(", ", bundled.Select(Path.GetFileName))}): "
                    + string.Join("; ", missing.Take(Examples * 4).Select(pair =>
                        $"'{char.ConvertFromUtf32(pair.Key)}' U+{pair.Key:X4} ({pair.Value.Origin})"))
                    + (missing.Count > Examples * 4 ? $"; {missing.Count - Examples * 4} more" : "")
                    + "; they fall back to the fonts listed after them and the system's");
        }
    }

    /// <summary>Translated and untranslated strings per script and language, against the installed scripts:
    /// messages (with or without variables), literals drawn or stored, and composed strings. A message counts as
    /// translated when an entry presents it whole.</summary>
    public static int Stats(TextSet set, ScriptTextCorpus corpus, string? only, bool perScript, TextWriter console)
    {
        foreach (string problem in set.Problems) console.WriteLine($"error: {problem}");
        foreach (string tag in set.Tags.Where(tag => only == null || tag == only))
        {
            IReadOnlyList<TextCatalog> chain;
            try
            {
                chain = set.Chain(tag);
            }
            catch (ArgumentException error)
            {
                console.WriteLine($"error: text {tag}: {error.Message}");
                continue;
            }
            var patterns = chain.Select(catalog => catalog.Patterns
                .Select(entry => (entry.Script, entry.At, entry.Source)).ToHashSet()).ToList();
            bool Translated(ScriptString found)
            {
                if (found.Text is { } text)
                {
                    bool frame = found.Kind == ScriptStringKind.FrameLiteral;
                    return chain.Any(catalog => catalog.TryResolveExact(text, frame ? null : found.Script,
                        frame ? null : found.Offset, out _));
                }
                return patterns.Any(set => set.Contains((found.Script, found.Offset, found.Source))
                                           || set.Contains((found.Script, null, found.Source))
                                           || set.Contains((null, null, found.Source)));
            }
            var totals = new Counts();
            var lines = new List<string>();
            foreach (var (script, strings) in corpus.Scripts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var counts = new Counts();
                foreach (ScriptString found in strings)
                {
                    (int Done, int All) Add((int Done, int All) count) => (count.Done + (Translated(found) ? 1 : 0), count.All + 1);
                    switch (found.Kind)
                    {
                        case ScriptStringKind.Message or ScriptStringKind.Pattern: counts.Messages = Add(counts.Messages); break;
                        case ScriptStringKind.Composed: counts.Composed = Add(counts.Composed); break;
                        case ScriptStringKind.Run: break;
                        default: counts.Strings = Add(counts.Strings); break;
                    }
                }
                totals.Add(counts);
                if (perScript && counts.Any) lines.Add($"  {script,-12} {counts}");
            }
            int entries = chain.FirstOrDefault(catalog => catalog.Language == tag)?.Count ?? 0;
            int templates = set.Files.Where(file => file.Language == tag).Sum(file => file.File.Items.Count(item => !item.HasText));
            int obsolete = set.Files.Where(file => file.Language == tag).Sum(file => file.File.Items.Count(item => item.Obsolete));
            console.WriteLine($"text {tag}: {totals} ({entries} entries, {templates} without text, {obsolete} obsolete; "
                              + $"chain {string.Join(" -> ", chain.Select(catalog => catalog.Language))})");
            foreach (string line in lines) console.WriteLine(line);
        }
        return set.Problems.Count == 0 ? 0 : 1;
    }

    private sealed class Counts
    {
        public (int Done, int All) Messages, Strings, Composed;
        public bool Any => Messages.All + Strings.All + Composed.All > 0;

        public void Add(Counts other)
        {
            Messages = (Messages.Done + other.Messages.Done, Messages.All + other.Messages.All);
            Strings = (Strings.Done + other.Strings.Done, Strings.All + other.Strings.All);
            Composed = (Composed.Done + other.Composed.Done, Composed.All + other.Composed.All);
        }

        private static string Part((int Done, int All) count, string what)
            => $"{what} {count.Done}/{count.All}" + (count.All > 0 ? $" ({100.0 * count.Done / count.All:0.0}%)" : "");

        public override string ToString()
            => $"{Part(Messages, "messages")}, {Part(Strings, "strings")}, {Part(Composed, "composed")}";
    }

    private static string Line(TextEntry entry)
    {
        int colon = entry.Origin.LastIndexOf(':');
        return colon >= 0 && int.TryParse(entry.Origin[(colon + 1)..], out _) ? "line " + entry.Origin[(colon + 1)..] : entry.Origin;
    }

    private static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";

    private static string Short(string text) => (text.Length > 40 ? text[..40] + "…" : text).ReplaceLineEndings("\\n");
}

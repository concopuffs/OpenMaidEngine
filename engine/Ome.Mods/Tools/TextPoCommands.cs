using System.Globalization;
using System.Text;
using Ome.Engine.Localization;
using Ome.Mods.Localization;

namespace Ome.Mods.Tools;

/// <summary>
/// <c>omp text po-export</c> and <c>po-import</c> (localization spec §13): a language's catalogs to and from gettext PO,
/// so translation tools can work on them. The catalog files stay the source of truth: export writes one message per
/// source and scope, and import puts the translations back into the entries they came from, keeping every other
/// key. <c>msgctxt</c> carries the scope: none for a global entry, <c>SCRIPT</c> for a script entry,
/// <c>SCRIPT@0xOFFSET</c> for a site entry (the F3 locator's coordinate), and <c>engine:KEY</c> for an
/// <c>[[engine]]</c> entry, whose <c>msgid</c> is the engine's Japanese wording.
/// </summary>
public static class TextPoCommands
{
    public const string EngineContext = "engine:";
    /// <summary>The comment on a message whose catalog entry is translated as empty text.</summary>
    public const string EmptyNote = "The catalog translates this as empty text (text = \"\"). Leave msgstr empty to keep that.";

    /// <summary>The PO context of an entry's scope.</summary>
    public static string? Context(TextEntry entry)
        => entry.Script == null ? null
            : entry.At is { } at ? $"{entry.Script}@0x{at.ToString("x", CultureInfo.InvariantCulture)}" : entry.Script;

    private static IReadOnlyList<(string Name, string Path)> Files(string folder)
        => Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories)
            .Select(path => (Name: Path.GetRelativePath(folder, path).Replace('\\', '/'), Path: path))
            .OrderBy(file => file.Name, StringComparer.Ordinal).ToList();

    /// <summary>Reads every catalog of <paramref name="folder"/>, in the loader's order; null, with the problems
    /// printed, when one does not read.</summary>
    private static List<(string Name, string Path, TextCatalogFile File)>? Read(string folder, string language,
                                                                               TextWriter console, string verb)
    {
        var files = new List<(string, string, TextCatalogFile)>();
        var problems = new List<string>();
        foreach (var (name, path) in Files(folder))
            try
            {
                files.Add((name, path, TextCatalogFileReader.Read(path, $"{language}/{name}")));
            }
            catch (TextCatalogException error)
            {
                problems.AddRange(error.Problems);
            }
        if (problems.Count == 0) return files;
        foreach (string problem in problems) console.WriteLine($"error: {problem}");
        console.WriteLine($"omp text {verb}: the catalogs must read first; nothing written");
        return null;
    }

    private static string Line(string origin) => origin[(origin.IndexOf('/') + 1)..];

    public static int Export(string folder, string language, string output, TextWriter console, EngineTextKeys? engine = null)
    {
        if (!Directory.Exists(folder))
        {
            console.WriteLine($"error: {folder}: no such folder");
            return 2;
        }
        if (Read(folder, language, console, "po-export") is not { } files) return 1;
        // One message per scope and source: a later file's entry for the same pair overrides an earlier one, as the
        // loader merges them, so the message carries the last text and every file it appears in.
        var messages = new Dictionary<(string? Context, string Id), PoEntry>();
        var order = new List<(string?, string)>();
        int obsolete = 0;
        void Add(string? context, string id, string? str, string reference, string? note, bool isObsolete)
        {
            var key = (context, id);
            if (messages.TryGetValue(key, out PoEntry? earlier))
            {
                messages[key] = earlier with
                {
                    Str = str ?? earlier.Str,
                    References = [.. earlier.References, reference],
                    Obsolete = earlier.Obsolete && isObsolete,
                };
                return;
            }
            order.Add(key);
            messages[key] = new PoEntry(context, id, str ?? "")
            {
                ExtractedComments = note == null ? [] : [note],
                References = [reference],
                Obsolete = isObsolete,
            };
        }
        foreach (var (name, _, file) in files)
        {
            foreach (TextCatalogItem item in file.AllItems)
            {
                TextEntry entry = item.Entry;
                // PO has no empty translation: an empty msgstr is an untranslated message, which import leaves alone.
                string? note = item.HasText && entry.Text.Length == 0
                    ? (entry.Note == null ? "" : entry.Note + "\n") + EmptyNote
                    : entry.Note;
                Add(Context(entry), entry.Source, item.HasText ? entry.Text : null, Line(entry.Origin), note, item.Obsolete);
            }
            foreach (EngineTextEntry entry in file.EngineEntries)
            {
                string id = engine?.Japanese.TryGetValue(entry.Key, out string? japanese) == true ? japanese : entry.Key;
                Add(EngineContext + entry.Key, id, entry.Text, Line(entry.Origin.Length > 0 ? entry.Origin : $"{language}/{name}"),
                    entry.Note, false);
            }
        }
        obsolete = messages.Values.Count(message => message.Obsolete);
        string full = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, PoFile.Write(language, order.Select(key => messages[key]),
            [$"Language {language}, exported by omp text po-export from {folder}.",
             "msgctxt is the entry's scope: none for global, SCRIPT, SCRIPT@0xOFFSET for a site, engine:KEY for the "
             + "engine's own text. Bring translations back with omp text po-import; untranslated and fuzzy messages "
             + "are left out."]), new UTF8Encoding(false));
        int translated = order.Count(key => messages[key].Str.Length > 0 && !messages[key].Obsolete);
        console.WriteLine($"omp text po-export: {files.Count} files -> {full}: {order.Count} messages, "
                          + $"{translated} translated, {obsolete} obsolete");
        return 0;
    }

    public static int Import(string folder, string language, string input, TextWriter console, EngineTextKeys? engine = null)
    {
        if (!Directory.Exists(folder))
        {
            console.WriteLine($"error: {folder}: no such folder");
            return 2;
        }
        IReadOnlyList<PoEntry> po;
        try
        {
            po = PoFile.Parse(File.ReadAllText(input, Encoding.UTF8), Path.GetFileName(input));
        }
        catch (PoFileException error)
        {
            foreach (string problem in error.Problems) console.WriteLine($"error: {problem}");
            console.WriteLine("omp text po-import: the PO file must read first; nothing written");
            return 1;
        }
        if (Read(folder, language, console, "po-import") is not { } files) return 1;

        int fuzzy = 0, untranslated = 0;
        var translations = new Dictionary<(string? Context, string Id), PoEntry>();
        foreach (PoEntry entry in po.Where(entry => !entry.Obsolete))
        {
            if (entry.Fuzzy) fuzzy++;
            else if (entry.Str.Length == 0) untranslated++;
            else translations[(entry.Context, entry.Id)] = entry;
        }
        // An engine message matches by its key alone: its msgid is the engine's wording, which may have changed.
        var engineMessages = translations.Values.Where(entry => entry.Context?.StartsWith(EngineContext, StringComparison.Ordinal) == true)
            .GroupBy(entry => entry.Context!).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var used = new HashSet<(string?, string)>();
        var problems = new List<string>();
        int changed = 0, unchanged = 0;
        var rewrites = new List<(string Path, TextCatalogFile File)>();
        foreach (var (name, path, file) in files)
        {
            bool fileChanged = false;
            var items = new List<TextCatalogItem>();
            foreach (TextCatalogItem item in file.AllItems)
            {
                TextEntry entry = item.Entry;
                if (item.Obsolete || !translations.TryGetValue((Context(entry), entry.Source), out PoEntry? message))
                {
                    items.Add(item);
                    continue;
                }
                used.Add((message.Context, message.Id));
                if (item.HasText && entry.Text == message.Str)
                {
                    unchanged++;
                    items.Add(item);
                    continue;
                }
                if (!TextEntry.TryCreate(entry.Source, message.Str, out TextEntry translated, out string? error))
                {
                    problems.Add($"{Path.GetFileName(input)}:{message.Line}: {error} ({Line(entry.Origin)})");
                    items.Add(item);
                    continue;
                }
                items.Add(item with { Entry = Retext(entry, translated), HasText = true });
                changed++;
                fileChanged = true;
            }
            var engineEntries = new List<EngineTextEntry>();
            foreach (EngineTextEntry entry in file.EngineEntries)
            {
                if (!engineMessages.TryGetValue(EngineContext + entry.Key, out PoEntry? message))
                {
                    engineEntries.Add(entry);
                    continue;
                }
                used.Add((message.Context, message.Id));
                if (entry.Text == message.Str)
                {
                    unchanged++;
                    engineEntries.Add(entry);
                    continue;
                }
                if (engine?.Problem(entry.Key, message.Str) is { } problem)
                {
                    problems.Add($"{Path.GetFileName(input)}:{message.Line}: {problem}");
                    engineEntries.Add(entry);
                    continue;
                }
                engineEntries.Add(entry with { Text = message.Str });
                changed++;
                fileChanged = true;
            }
            if (fileChanged)
                rewrites.Add((path, file with
                {
                    Entries = items.Where(item => item.Applies).Select(item => item.Entry).ToList(),
                    Items = items,
                    EngineEntries = engineEntries,
                }));
        }
        if (problems.Count > 0)
        {
            foreach (string problem in problems) console.WriteLine($"error: {problem}");
            console.WriteLine($"omp text po-import: fails: {problems.Count} translations do not fit their entries; nothing written");
            return 1;
        }
        foreach (var (path, file) in rewrites)
            File.WriteAllText(path, TextCatalogFileWriter.Write(file, HeaderComments(path)), new UTF8Encoding(false));
        var unmatched = translations.Where(pair => !used.Contains(pair.Key)).Select(pair => pair.Value)
            .OrderBy(entry => entry.Line).ToList();
        foreach (PoEntry entry in unmatched)
            console.WriteLine($"warning: {Path.GetFileName(input)}:{entry.Line}: "
                              + $"{(entry.Context == null ? "global" : entry.Context)} \"{Short(entry.Id)}\": no catalog "
                              + "entry has this scope and source; not imported");
        console.WriteLine($"omp text po-import: {changed} entries translated in {rewrites.Count} files, {unchanged} "
                          + $"unchanged; {untranslated} untranslated and {fuzzy} fuzzy messages left out, "
                          + $"{unmatched.Count} unmatched");
        return 0;
    }

    /// <summary>The entry with <paramref name="translated"/>'s text and every other key kept.</summary>
    private static TextEntry Retext(TextEntry entry, TextEntry translated) => translated with
    {
        Script = entry.Script, At = entry.At, Align = entry.Align, Fit = entry.Fit, Measure = entry.Measure,
        Cells = entry.Cells, RowGap = entry.RowGap, Note = entry.Note, Origin = entry.Origin,
    };

    /// <summary>The comment lines a file starts with, which a rewrite keeps.</summary>
    private static IEnumerable<string> HeaderComments(string path)
        => File.ReadLines(path).TakeWhile(line => line.StartsWith('#'))
            .Select(line => line.Length > 1 && line[1] == ' ' ? line[2..] : line[1..]).ToList();

    private static string Short(string text) => (text.Length > 40 ? text[..40] + "…" : text).ReplaceLineEndings("\\n");
}

using Ome.Engine.Localization;

namespace Ome.Mods.Localization;

/// <summary>What one <c>omp text extract</c> run did.</summary>
public sealed class ExtractCounts
{
    public int FilesWritten { get; set; }
    public int FilesUnchanged { get; set; }
    /// <summary>Entries written as templates for the first time.</summary>
    public int NewTemplates { get; set; }
    public int KeptTranslated { get; set; }
    public int KeptUntranslated { get; set; }
    /// <summary>Entries newly marked obsolete: their source is no longer drawn or stored where their scope reaches.</summary>
    public int MarkedObsolete { get; set; }
    /// <summary>Entries marked obsolete before whose source is back.</summary>
    public int Revived { get; set; }
}

/// <summary>
/// The catalog templates of <c>omp text extract</c> (localization spec §13): a file per script with its messages
/// (the authored line breaks in <c>note</c>), its messages with variables and the strings it composes as patterns,
/// all scoped to the script; and <c>_global.toml</c> with every literal the scripts draw or store, as a global
/// candidate. Templates have no <c>text</c>, so the loader skips them until a translator adds it.
/// Run again over a folder that holds catalogs, it keeps every entry there, translated or not, adds templates for
/// sources no file of the language has, and marks <c>obsolete</c> each exact entry whose source is no longer drawn or
/// stored where its scope reaches (or whose site entry names another revision of its script); one whose source is
/// back loses the mark. Patterns are kept as written. Only files whose entries changed are written again; the
/// writer does not keep comments, so notes for translators belong in <c>note</c>.
/// </summary>
public static class TextTemplateExtractor
{
    public const string GlobalFileName = Import.ScriptTranslationImporter.GlobalFileName;

    private readonly record struct Key(string? Script, int? At, string Source)
    {
        public static Key Of(TextEntry entry) => new(entry.Script, entry.At, entry.Source);
    }

    /// <summary>The files to write: each file name (relative to the language folder, <c>/</c>-separated) with its
    /// content. <paramref name="existing"/> holds the language folder's files by the same names.</summary>
    public static IReadOnlyDictionary<string, TextCatalogFile> Build(ScriptTextCorpus corpus,
        IReadOnlyDictionary<string, TextCatalogFile> existing, ExtractCounts counts)
    {
        var known = new HashSet<Key>();
        foreach (TextCatalogFile file in existing.Values)
            foreach (TextCatalogItem item in file.AllItems)
                known.Add(Key.Of(item.Entry));

        var generated = new SortedDictionary<string, (string? Script, List<TextEntry> Templates)>(StringComparer.Ordinal);
        var globals = new List<TextEntry>();
        var globalUses = new Dictionary<string, (ScriptString First, int More)>(StringComparer.Ordinal);
        foreach (var (script, strings) in corpus.Scripts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var templates = new List<TextEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScriptString found in strings)
            {
                switch (found.Kind)
                {
                    case ScriptStringKind.Message or ScriptStringKind.Pattern or ScriptStringKind.Composed:
                        if (seen.Add(found.Source)
                            && TextEntry.TryCreate(found.Source, TextTemplate.UntranslatedText(found.Source), out TextEntry entry, out _))
                            templates.Add(entry with { Script = script, Note = found.Authored });
                        break;
                    case ScriptStringKind.DrawLiteral or ScriptStringKind.StoredLiteral or ScriptStringKind.FrameLiteral:
                        globalUses[found.Source] = globalUses.TryGetValue(found.Source, out var use)
                            ? (use.First, use.More + 1)
                            : (found, 0);
                        break;
                }
            }
            generated[script + ".toml"] = (script, templates);
        }
        foreach (var (source, (first, more)) in globalUses)
            if (TextEntry.TryCreate(source, source, out TextEntry entry, out _))
                globals.Add(entry with { Note = $"{first.Script}@0x{first.Offset:x}" + (more > 0 ? $", {more} more" : "") });
        generated[GlobalFileName] = (null, globals);

        var output = new Dictionary<string, TextCatalogFile>(StringComparer.Ordinal);
        foreach (var (name, (script, templates)) in generated)
        {
            existing.TryGetValue(name, out TextCatalogFile? old);
            var items = new List<TextCatalogItem>();
            var kept = new Dictionary<Key, TextCatalogItem>();
            foreach (TextCatalogItem item in old?.AllItems ?? []) kept.TryAdd(Key.Of(item.Entry), item);
            var placed = new HashSet<TextCatalogItem>(ReferenceEqualityComparer.Instance);
            bool changed = old == null;
            foreach (TextEntry template in templates)
            {
                var key = Key.Of(template);
                if (kept.TryGetValue(key, out TextCatalogItem? item))
                {
                    items.Add(Review(item, old!, corpus, counts, ref changed));
                    placed.Add(item);
                }
                else if (!known.Contains(key))
                {
                    items.Add(new TextCatalogItem(template, false, false));
                    counts.NewTemplates++;
                    changed = true;
                }
            }
            foreach (TextCatalogItem item in old?.AllItems ?? [])
                if (placed.Add(item)) items.Add(Review(item, old!, corpus, counts, ref changed));
            Finish(name, old?.Script ?? script, old, items, changed, output, counts);
        }
        // A file extract does not generate (a translator's own, or a script no longer installed): only marked.
        foreach (var (name, old) in existing.Where(pair => !generated.ContainsKey(pair.Key)))
        {
            bool changed = false;
            var items = old.AllItems.Select(item => Review(item, old, corpus, counts, ref changed)).ToList();
            Finish(name, old.Script, old, items, changed, output, counts);
        }
        return output;
    }

    private static void Finish(string name, string? script, TextCatalogFile? old, List<TextCatalogItem> items, bool changed,
                               Dictionary<string, TextCatalogFile> output, ExtractCounts counts)
    {
        if (!changed)
        {
            counts.FilesUnchanged++;
            return;
        }
        if (items.Count == 0 && old == null) return;
        string? fingerprint = items.Any(item => item.Entry.At != null) ? old?.Fingerprint : null;
        output[name] = new TextCatalogFile(script, fingerprint,
            items.Where(item => item.Applies).Select(item => item.Entry).ToList(), old?.EngineEntries ?? [])
        {
            Items = items,
        };
        counts.FilesWritten++;
    }

    /// <summary>An entry already in a file: kept, its obsolete mark set by whether its source still reaches the
    /// scripts.</summary>
    private static TextCatalogItem Review(TextCatalogItem item, TextCatalogFile file, ScriptTextCorpus corpus,
                                          ExtractCounts counts, ref bool changed)
    {
        TextEntry entry = item.Entry;
        bool reaches = corpus.Reaches(entry)
                       && (entry.At == null || (entry.Script is { } script
                                                && corpus.Fingerprints.TryGetValue(script, out string? installed)
                                                && string.Equals(installed, file.Fingerprint, StringComparison.OrdinalIgnoreCase)));
        if (entry.IsPattern) reaches = !item.Obsolete;
        if (item.HasText) counts.KeptTranslated++;
        else counts.KeptUntranslated++;
        if (reaches == !item.Obsolete) return item;
        changed = true;
        if (reaches) counts.Revived++;
        else counts.MarkedObsolete++;
        return item with { Obsolete = !reaches };
    }
}

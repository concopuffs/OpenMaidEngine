using Ome.Engine.Localization;
using Ome.Mods.Localization;
using Ome.Mods.Packages;
using Ome.Script.Patch;

namespace Ome.Mods.Linking;

/// <summary>A catalog of the stack as the link cache keeps it (localization spec §9.4): its language, its path under
/// the cache's text root (<c>&lt;tag&gt;/&lt;NNN&gt;.&lt;mod&gt;/…</c>, so the root's ordinal file order is the load
/// order), its bytes, and its origin in reports (<c>&lt;mod&gt;/text/&lt;tag&gt;/…</c>).</summary>
public sealed record LinkedTextFile(string Language, string ModId, string Path, byte[] Bytes, string Origin);

/// <summary>A bundled font, kept at <c>fonts/&lt;mod&gt;/&lt;name&gt;</c> in the cache.</summary>
public sealed record LinkedFont(string ModId, string Name, byte[] Bytes)
{
    public string CachePath => $"{LanguageFont.FontsFolder}{ModId}/{Name}";
}

/// <summary>A language the stack defines (spec §9.2): the settings merged in load order, the later package winning
/// per key, with each bundled font as its cache path; and the mods that define it.</summary>
public sealed record LinkedLanguage(LanguageSettings Settings, IReadOnlyList<string> DefinedBy);

/// <summary>The stack's text: catalogs, fonts and languages.</summary>
public sealed record LinkedText(IReadOnlyList<LinkedTextFile> Files, IReadOnlyList<LinkedFont> Fonts,
                                IReadOnlyList<LinkedLanguage> Languages)
{
    public static LinkedText None { get; } = new([], [], []);
    public bool IsEmpty => Files.Count == 0 && Fonts.Count == 0 && Languages.Count == 0;
}

/// <summary>
/// The text part of a link (localization spec §9.4 steps 1–4): reads every <c>text/</c> catalog of the stack, where a
/// file that does not parse stops the link with its file and line; merges the <c>[[language]]</c> tables per tag, the
/// later package winning per key; requires each language a catalog adds to to be defined by some package; and reports
/// entries per language, overrides between files, exact entries whose source no installed script draws or stores where
/// their scope reaches, and site entries that cannot bind to the linked scripts. The
/// catalogs are copied as they are: the port binds their site entries when it loads a language
/// (<see cref="TextSiteBinder"/>), through the offset tables the link keeps for patched scripts.
/// </summary>
public static class ModTextLinker
{
    public static LinkedText Link(IReadOnlyList<ModPackage> order, ModLinkBase linkBase,
                                  IReadOnlyDictionary<string, LinkedScript> scripts, ModLinkReport report)
    {
        if (order.All(mod => mod.Layout.Text.Count == 0 && mod.Layout.Fonts.Count == 0 && mod.Manifest.Languages.Count == 0))
            return LinkedText.None;

        // Languages, in load order.
        var settings = new Dictionary<string, LanguageSettings>(StringComparer.Ordinal);
        var definers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tagOrder = new List<string>();
        foreach (ModPackage mod in order)
            foreach (LanguageSettings declared in mod.Manifest.Languages)
            {
                string id = mod.Id;
                LanguageSettings resolved = declared with
                {
                    Fonts = declared.Fonts?.Select(font => font.Resolve(use =>
                        $"{LanguageFont.FontsFolder}{id}/{use[LanguageFont.FontsFolder.Length..]}")).ToList(),
                    Origin = $"{id}: {declared.Origin}",
                };
                if (settings.TryGetValue(declared.Tag, out LanguageSettings? earlier))
                {
                    var (merged, replaced) = earlier.Merge(resolved);
                    settings[declared.Tag] = merged;
                    if (replaced.Count > 0)
                        report.Add(ModReportClass.Override, [.. definers[declared.Tag], id], null,
                            $"language {declared.Tag}: {id} sets {string.Join(", ", replaced)} over "
                            + $"{string.Join(", ", definers[declared.Tag])} (later in load order)");
                    definers[declared.Tag].Add(id);
                }
                else
                {
                    settings[declared.Tag] = resolved;
                    definers[declared.Tag] = [id];
                    tagOrder.Add(declared.Tag);
                }
            }

        var fonts = new List<LinkedFont>();
        foreach (ModPackage mod in order)
            foreach (ModFontFile font in mod.Layout.Fonts)
            {
                fonts.Add(new LinkedFont(mod.Id, font.Name, mod.ReadAll(font.File)));
                bool named = mod.Manifest.Languages.Any(language => (language.Fonts ?? []).Any(table => table.Use.Any(use =>
                    use.Equals(LanguageFont.FontsFolder + font.Name, StringComparison.OrdinalIgnoreCase))));
                if (!named)
                    report.Add(ModReportClass.Note, mod.Id, null,
                        $"fonts/{font.Name} is named by none of its [[language.font]] tables");
            }

        // Catalogs, in load order; the index keeps that order in the cache's text root.
        int width = Math.Max(3, order.Count.ToString().Length);
        var files = new List<LinkedTextFile>();
        var parsed = new Dictionary<string, List<(ModPackage Mod, string Name, TextCatalogFile File)>>(StringComparer.Ordinal);
        for (int index = 0; index < order.Count; index++)
        {
            ModPackage mod = order[index];
            var undefined = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ModTextFile file in mod.Layout.Text)
            {
                if (!settings.ContainsKey(file.Language)) undefined.Add(file.Language);
                string origin = $"{mod.Id}/{file.File.Path}";
                TextCatalogFile catalog;
                try
                {
                    catalog = TextCatalogFileReader.Parse(mod.ReadText(file.File), origin);
                }
                catch (TextCatalogException error)
                {
                    foreach (string problem in error.Problems) report.Add(ModReportClass.Error, mod.Id, null, problem);
                    continue;
                }
                catch (ModPackageException error)
                {
                    report.Add(ModReportClass.Error, mod.Id, null, $"{origin}: {error.Message}");
                    continue;
                }
                if (!parsed.TryGetValue(file.Language, out var list)) parsed[file.Language] = list = [];
                list.Add((mod, origin, catalog));
                files.Add(new LinkedTextFile(file.Language, mod.Id,
                    $"{file.Language}/{index.ToString().PadLeft(width, '0')}.{mod.Id}/{file.Name}", mod.ReadAll(file.File),
                    origin));
            }
            foreach (string tag in undefined)
                report.Add(ModReportClass.Error, mod.Id, null,
                    $"text/{tag}/ adds entries to language '{tag}', which no package in the stack defines; one must "
                    + $"declare it in its manifest ([[language]] tag = \"{tag}\")");
        }

        ScriptTextCorpus? corpus = parsed.Count > 0 ? BaseText(linkBase) : null;
        foreach (var (tag, list) in parsed.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            TextCatalog catalog;
            try
            {
                catalog = TextCatalog.Build(tag, list.SelectMany(item => item.File.Entries));
            }
            catch (ArgumentException error)
            {
                report.Add(ModReportClass.Error, list.Select(item => item.Mod.Id), null, $"text {tag}: {error.Message}");
                continue;
            }
            var mods = list.Select(item => item.Mod.Id).Distinct().ToList();
            int templates = list.Sum(item => item.File.Items.Count(entry => !entry.HasText));
            int obsolete = list.Sum(item => item.File.Items.Count(entry => entry.Obsolete));
            report.Add(ModReportClass.Note, mods, null,
                $"text {tag}: {catalog.Count} entries from {list.Count} files"
                + (templates > 0 ? $", {templates} without text" : "") + (obsolete > 0 ? $", {obsolete} obsolete" : ""));
            ReportOverrides(tag, catalog, report);
            foreach (var (mod, name, file) in list)
            {
                ReportSites(mod, name, file, linkBase, corpus, scripts, report);
                int misses = corpus == null ? 0 : file.Entries.Count(entry => entry.At == null && !corpus.Reaches(entry));
                if (misses > 0)
                    report.Add(ModReportClass.Note, mod.Id, null,
                        $"{name}: {(misses == 1 ? "1 entry matches" : $"{misses} entries match")} nothing in the "
                        + "installed scripts (omp text check lists them)");
            }
        }

        var languages = tagOrder.Select(tag => new LinkedLanguage(settings[tag], definers[tag])).ToList();
        return new LinkedText(files, fonts, languages);
    }

    /// <summary>The base scripts' text, or null when the base lists no scripts or its opcode table has no text
    /// opcodes.</summary>
    private static ScriptTextCorpus? BaseText(ModLinkBase linkBase)
    {
        if (linkBase.InstalledScripts.Count == 0) return null;
        ScriptTextScanner scanner;
        try
        {
            scanner = new ScriptTextScanner(linkBase.Table, linkBase.Contracts);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        return ScriptTextCorpus.Scan(scanner, linkBase.InstalledScripts);
    }

    /// <summary>The mod an entry came from: its origin starts with the mod's id.</summary>
    private static string ModOf(TextEntry entry) => entry.Origin.Split('/', 2)[0];

    /// <summary>One line per pair of mods: an override between two packages is an override (the later wins), one
    /// inside a package a note.</summary>
    private static void ReportOverrides(string tag, TextCatalog catalog, ModLinkReport report)
    {
        foreach (var group in catalog.Overrides.GroupBy(item => (Replaced: ModOf(item.Replaced), Winner: ModOf(item.Winner))))
        {
            TextCatalogOverride first = group.First();
            int count = group.Count();
            string entries = count == 1 ? "1 entry" : $"{count} entries";
            if (group.Key.Replaced == group.Key.Winner)
                report.Add(ModReportClass.Note, group.Key.Winner, null,
                    $"text {tag}: {entries} replaced by a later file of the same package (first: {first.Winner.Origin} "
                    + $"over {first.Replaced.Origin})");
            else
                report.Add(ModReportClass.Override, [group.Key.Replaced, group.Key.Winner], null,
                    $"text {tag}: {entries} of {group.Key.Replaced} replaced by {group.Key.Winner}'s (first: "
                    + $"{first.Winner.Origin} over {first.Replaced.Origin})");
        }
    }

    /// <summary>Notes for a file's site entries that will not bind: the base lacks the script, its fingerprint
    /// differs, a mod replaces the script, or a patch replaced the instruction (spec §6.1). The port drops them.</summary>
    private static void ReportSites(ModPackage mod, string name, TextCatalogFile file, ModLinkBase linkBase,
                                    ScriptTextCorpus? corpus, IReadOnlyDictionary<string, LinkedScript> scripts,
                                    ModLinkReport report)
    {
        foreach (var group in file.Entries.Where(entry => entry.At != null).GroupBy(entry => entry.Script!))
        {
            string script = group.Key + ".BIN";
            int count = group.Count();
            string entries = count == 1 ? "1 site entry" : $"{count} site entries";
            string bind = count == 1 ? "does not bind" : "do not bind";
            string? fingerprint = corpus != null
                ? corpus.Fingerprints.GetValueOrDefault(group.Key)
                : linkBase.ReadScript(script) is { } bytes ? OmsPatchPlan.FingerprintOf(bytes) : null;
            if (fingerprint == null)
            {
                report.Add(ModReportClass.Note, mod.Id, script, $"{name}: {entries} name a script the base does not have");
                continue;
            }
            if (!string.Equals(fingerprint, file.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                report.Add(ModReportClass.Note, mod.Id, script,
                    $"{name}: {entries} {bind}: the file names {file.Fingerprint}, the base script is {fingerprint}");
                continue;
            }
            if (!scripts.TryGetValue(script, out LinkedScript? linked)) continue;
            if (linked.ReplacedBy != null || linked.Offsets == null)
            {
                report.Add(ModReportClass.Note, linked.ReplacedBy is { } replacer ? [mod.Id, replacer] : [mod.Id], script,
                    $"{name}: {entries} {bind}: {linked.ReplacedBy ?? "a mod"} replaces the script");
                continue;
            }
            int lost = group.Count(entry => linked.Offsets.Map(entry.At!.Value) == null);
            if (lost > 0)
                report.Add(ModReportClass.Note, [mod.Id, .. linked.PatchedBy], script,
                    $"{name}: {lost} of its site entries do not bind: a patch replaces their instructions");
        }
    }
}

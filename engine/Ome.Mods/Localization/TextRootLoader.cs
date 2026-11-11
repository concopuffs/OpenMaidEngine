using System.Text;
using Ome.Engine.Localization;
using Ome.Mods.Linking;

namespace Ome.Mods.Localization;

/// <summary>What loading a language found (localization spec §9.4 steps 1–4).</summary>
public sealed class TextLoadReport
{
    /// <summary>Each language in the chain that had a folder, with its file and entry counts.</summary>
    public List<(string Tag, int Files, int Entries)> Languages { get; } = [];

    /// <summary>Entries a later file replaced at the same scope.</summary>
    public List<TextCatalogOverride> Overrides { get; } = [];

    /// <summary>Site entries dropped, per file and reason: the base script's fingerprint differs, the script is not
    /// installed, a mod replaces the script, or a patch replaced the instruction (spec §6.1).</summary>
    public List<(string File, int SiteEntries, string Reason)> DroppedSites { get; } = [];

    /// <summary>Each language in the chain with <c>[[engine]]</c> entries (spec §11), and how many keys they set.</summary>
    public List<(string Tag, int Keys)> EngineText { get; } = [];

    public IEnumerable<string> Lines()
    {
        foreach (var (tag, files, entries) in Languages)
            yield return $"text {tag}: {entries} entries from {files} files";
        foreach (var (tag, keys) in EngineText)
            yield return $"engine text {tag}: {keys} keys";
        foreach (TextCatalogOverride replaced in Overrides)
            yield return $"text override: {replaced.Replaced.Origin} replaced by {replaced.Winner.Origin}";
        foreach (var (file, sites, reason) in DroppedSites)
            yield return $"text {file}: {sites} site entries dropped ({reason})";
    }
}

/// <summary>
/// A folder of catalogs laid out as a package's <c>text/</c> (spec §9.1), <c>ROOT/&lt;tag&gt;/**.toml</c>, and the
/// languages it defines. The link cache's <c>text/</c> is one, with the languages the stack's manifests declare
/// (<see cref="MountedModLayer.TextSource"/>). A folder given with <c>--text-root</c> is another: it declares its
/// languages in <see cref="TextRootLoader.LanguagesFile"/> beside the language folders, read at each load so that a
/// language switch picks up edits, and its bundled fonts are under <c>ROOT/fonts/</c>.
/// </summary>
/// <param name="Languages">The languages, bundled fonts as absolute paths; null reads the root's
/// <see cref="TextRootLoader.LanguagesFile"/>.</param>
/// <param name="Name">Prefixed to the origin of each entry, to tell sources apart in reports; null for none.</param>
public sealed record TextSource(string Root, IReadOnlyList<LanguageSettings>? Languages = null, string? Name = null)
{
    /// <exception cref="TextCatalogException">The root's languages file does not parse or validate.</exception>
    public IReadOnlyList<LanguageSettings> ReadLanguages() => Languages ?? TextRootLoader.ReadLanguages(Root);

    /// <summary>Whether the source defines any language: a declared one, or a language folder with a catalog
    /// file.</summary>
    public bool DefinesLanguage => Languages is { Count: > 0 } || TextRootLoader.DefinesLanguage(Root);
}

/// <summary>
/// Binds site entries (spec §6.1, §9.4 step 3). An entry with <c>at</c> names a base offset in a script whose
/// fingerprint its file gives. It binds when that script is installed with that fingerprint and no mod replaces it;
/// in a script a mod patches, its offset moves through the link's offset table, and an instruction a patch replaced
/// takes no entry.
/// </summary>
/// <param name="baseFingerprint">An installed script's fingerprint, before any mod layer; null when it is not
/// installed (<see cref="LanguageLaunch.BaseFingerprints"/>).</param>
/// <param name="layerBinding">For a script the mod layer changes, its offset table (null when a mod replaces it);
/// <c>Changed = false</c> for a script the layer leaves alone (<see cref="MountedModLayer.SiteBinding"/>).</param>
public sealed class TextSiteBinder(Func<string, string?> baseFingerprint,
                                   Func<string, (bool Changed, ScriptOffsetMap? Map)>? layerBinding = null)
{
    public static TextSiteBinder For(Func<string, string?> baseFingerprint, MountedModLayer? layer)
        => new(baseFingerprint, layer == null ? null : layer.SiteBinding);

    /// <summary>Why the site entries of a file naming <paramref name="fingerprint"/> cannot bind to
    /// <paramref name="script"/>, or null when they can.</summary>
    public string? Unbindable(string script, string? fingerprint)
    {
        if (baseFingerprint(script) is not { } installed) return "the script is not installed";
        if (!string.Equals(installed, fingerprint, StringComparison.OrdinalIgnoreCase))
            return "the base script's fingerprint differs";
        return layerBinding?.Invoke(script) is (true, null) ? "a mod replaces the script" : null;
    }

    /// <summary>The offset in the running script of base offset <paramref name="at"/>, or null when a patch replaced
    /// the instruction there.</summary>
    public int? Map(string script, int at)
        => layerBinding?.Invoke(script) is (true, { } map) ? map.Map(at) : at;
}

/// <summary>
/// Loads a language from text sources (spec §9.4): for the selected tag and each language of its fallback chain (its
/// parents, or the <c>fallback</c> its settings name), the catalogs of every source that has its folder. Sources merge
/// in order, so a later source's entry wins at equal scope, and each language's settings merge per key, the later
/// source winning. Files load in ordinal order of their path inside the language folder; a problem in any file stops
/// the load, with every problem and its file and line (§9.4 step 1).
/// </summary>
public static class TextRootLoader
{
    /// <summary>A text root's <c>[[language]]</c> tables (spec §9.2), beside the language folders. Packages declare
    /// them in their manifest.</summary>
    public const string LanguagesFile = "languages.toml";

    /// <summary>A text root's bundled fonts, which its languages file names as <c>fonts/NAME</c>.</summary>
    public const string FontsFolder = "fonts";

    /// <summary>Whether the root defines any language: a language folder with a catalog file, or a
    /// <see cref="LanguagesFile"/>. While one does, the VM records string stores (spec §5.2), selected or not.</summary>
    public static bool DefinesLanguage(string root)
        => Directory.Exists(root)
           && (File.Exists(Path.Combine(root, LanguagesFile))
               || Directory.EnumerateDirectories(root)
                   .Where(folder => !Path.GetFileName(folder).Equals(FontsFolder, StringComparison.OrdinalIgnoreCase))
                   .Any(folder => Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories).Any()));

    /// <summary>The tags of the root's language folders that hold a catalog file.</summary>
    public static IEnumerable<string> LanguageFolders(string root)
        => Directory.Exists(root)
            ? Directory.EnumerateDirectories(root)
                .Where(folder => !Path.GetFileName(folder).Equals(FontsFolder, StringComparison.OrdinalIgnoreCase)
                                 && Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories).Any())
                .Select(folder => Path.GetFileName(folder))
            : [];

    /// <summary>The root's <see cref="LanguagesFile"/>, or none, with each bundled font as an absolute path under
    /// <c>ROOT/fonts/</c>.</summary>
    /// <exception cref="TextCatalogException">The file does not parse or validate, or names a font the root does not
    /// have.</exception>
    public static IReadOnlyList<LanguageSettings> ReadLanguages(string root)
    {
        string path = Path.Combine(root, LanguagesFile);
        if (!File.Exists(path)) return [];
        var problems = new List<string>();
        var languages = new List<LanguageSettings>();
        foreach (LanguageSettings language in TextCatalogFileReader.ParseLanguages(File.ReadAllText(path, Encoding.UTF8),
                     LanguagesFile))
            languages.Add(language with
            {
                Fonts = language.Fonts?.Select(font => font.Resolve(use =>
                {
                    string file = Path.GetFullPath(Path.Combine(root, FontsFolder, use[LanguageFont.FontsFolder.Length..]));
                    if (!File.Exists(file)) problems.Add($"{language.Origin}: '{use}' is not in {Path.Combine(root, FontsFolder)}");
                    return file;
                })).ToList(),
            });
        if (problems.Count > 0) throw new TextCatalogException(problems);
        return languages;
    }

    /// <summary>The catalogs of <paramref name="tag"/>'s fallback chain from one folder (tests and tools).</summary>
    public static TextLanguage Load(string root, string tag, TextSiteBinder binder, TextLoadReport report)
        => Load([new TextSource(root)], tag, binder, report);

    /// <summary>
    /// The catalogs of <paramref name="tag"/>'s fallback chain from <paramref name="sources"/>, in order, with the
    /// language's merged settings and the chain's <c>[[engine]]</c> entries (spec §11). Each entry with <c>at</c>
    /// binds through <paramref name="binder"/>. A language whose files hold no <c>[[text]]</c> table, templates
    /// included, has no catalog in the chain: a language with only engine text leaves the game text as written
    /// (§10).
    /// </summary>
    /// <exception cref="TextCatalogException">A file, or a languages file, does not parse or validate.</exception>
    public static TextLanguage Load(IReadOnlyList<TextSource> sources, string tag, TextSiteBinder binder,
                                    TextLoadReport report)
    {
        string normalized = LanguageTag.Normalize(tag);
        LanguageSettings? settings = null;
        foreach (TextSource source in sources)
            foreach (LanguageSettings declared in source.ReadLanguages().Where(declared => declared.Tag == normalized))
                settings = settings == null ? declared : settings.Merge(declared).Merged;
        var chain = new List<TextCatalog>();
        var engine = new List<EngineTextLayer>();
        var problems = new List<string>();
        string[] lookup = [.. new[] { normalized }.Concat(settings?.Fallback ?? LanguageTag.Parents(normalized)).Distinct()];
        foreach (string language in lookup)
        {
            var entries = new List<TextEntry>();
            var engineEntries = new Dictionary<string, EngineTextValue>(StringComparer.Ordinal);
            int fileCount = 0;
            bool found = false, hasText = false;
            foreach (TextSource source in sources)
            {
                string folder = Path.Combine(source.Root, language);
                if (!Directory.Exists(folder)) continue;
                found = true;
                string[] files = Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories)
                    .OrderBy(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), StringComparer.Ordinal)
                    .ToArray();
                fileCount += files.Length;
                foreach (string path in files)
                {
                    string name = (source.Name == null ? "" : source.Name + ":") + language + "/"
                                  + Path.GetRelativePath(folder, path).Replace('\\', '/');
                    TextCatalogFile file;
                    try
                    {
                        file = TextCatalogFileReader.Read(path, name);
                    }
                    catch (TextCatalogException error)
                    {
                        problems.AddRange(error.Problems);
                        continue;
                    }
                    entries.AddRange(Bind(file, name, binder, report));
                    hasText |= file.AllItems.Count > 0;
                    foreach (EngineTextEntry entry in file.EngineEntries)
                        if (entry.Text != null) engineEntries[entry.Key] = new EngineTextValue(entry.Text, entry.Origin);
                }
            }
            if (engineEntries.Count > 0)
            {
                engine.Add(new EngineTextLayer(language, engineEntries));
                report.EngineText.Add((language, engineEntries.Count));
            }
            if (!found || !hasText) continue;
            TextCatalog catalog;
            try
            {
                catalog = TextCatalog.Build(language, entries);
            }
            catch (ArgumentException error)
            {
                problems.Add(error.Message);
                continue;
            }
            report.Languages.Add((language, fileCount, catalog.Count));
            report.Overrides.AddRange(catalog.Overrides);
            chain.Add(catalog);
        }
        if (problems.Count > 0) throw new TextCatalogException(problems);
        return new TextLanguage(normalized, chain)
        {
            Lookup = lookup,
            Engine = engine,
            LineBreak = settings?.LineBreak ?? LineBreakMode.Words,
            Measure = settings?.Measure ?? TextMeasure.Presented,
            Digits = settings?.Digits ?? DigitsMode.Keep,
            GlyphDelayScale = settings?.GlyphDelayScale ?? 1.0,
            Fonts = (settings?.Fonts ?? []).Select(font => new TextFontMapping(font.Requested, font.Use)).ToList(),
        };
    }

    /// <summary>The file's entries, each site entry bound to the running script or dropped (spec §9.4 step 3).</summary>
    private static IEnumerable<TextEntry> Bind(TextCatalogFile file, string name, TextSiteBinder binder,
                                              TextLoadReport report)
    {
        if (file.Entries.All(entry => entry.At == null)) return file.Entries;
        var reasons = new Dictionary<string, string?>(StringComparer.Ordinal);
        var kept = new List<TextEntry>();
        var dropped = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TextEntry entry in file.Entries)
        {
            if (entry.At is not { } at)
            {
                kept.Add(entry);
                continue;
            }
            string script = entry.Script!;
            if (!reasons.TryGetValue(script, out string? reason))
                reasons[script] = reason = binder.Unbindable(script, file.Fingerprint);
            int? linked = reason == null ? binder.Map(script, at) : null;
            if (reason == null && linked == null) reason = "a patch replaces the instruction";
            if (reason != null)
            {
                dropped[reason] = dropped.GetValueOrDefault(reason) + 1;
                continue;
            }
            kept.Add(linked == at ? entry : entry with { At = linked });
        }
        foreach (var (reason, count) in dropped) report.DroppedSites.Add((name, count, reason));
        return kept;
    }
}

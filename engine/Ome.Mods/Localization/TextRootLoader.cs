using System.Text;
using Ome.Engine.Localization;

namespace Ome.Mods.Localization;

/// <summary>What loading a language from a text root found (localization spec §9.4 steps 1–4).</summary>
public sealed class TextLoadReport
{
    /// <summary>Each language in the chain that had a folder, with its file and entry counts.</summary>
    public List<(string Tag, int Files, int Entries)> Languages { get; } = [];

    /// <summary>Entries a later file replaced at the same scope.</summary>
    public List<TextCatalogOverride> Overrides { get; } = [];

    /// <summary>Files whose site entries were dropped, with the reason: the base script's fingerprint differs, the
    /// script is not installed, or a mod patches it (its offsets are no longer base offsets, spec §6.1).</summary>
    public List<(string File, int SiteEntries, string Reason)> DroppedSites { get; } = [];

    public IEnumerable<string> Lines()
    {
        foreach (var (tag, files, entries) in Languages)
            yield return $"text {tag}: {entries} entries from {files} files";
        foreach (TextCatalogOverride replaced in Overrides)
            yield return $"text override: {replaced.Replaced.Origin} replaced by {replaced.Winner.Origin}";
        foreach (var (file, sites, reason) in DroppedSites)
            yield return $"text {file}: {sites} site entries dropped ({reason})";
    }
}

/// <summary>
/// Loads a language from a folder laid out as a package's <c>text/</c> (spec §9.1): <c>ROOT/&lt;tag&gt;/*.toml</c>,
/// for the selected tag and each language of its fallback chain (its parents, or the <c>fallback</c> its
/// <see cref="LanguagesFile"/> entry names), with that entry's settings. This is how the port mounts a catalog before packages carry
/// <c>text/</c> (milestone 4), for example the local catalog <c>import-scripts</c> writes. Files load in ordinal
/// order of their path inside the language folder; a problem in any file stops the load, with every problem and its
/// file and line (§9.4 step 1).
/// </summary>
public static class TextRootLoader
{
    /// <summary>The text root's <c>[[language]]</c> tables (spec §9.2), beside the language folders. Packages declare
    /// them in their manifest from milestone 4.</summary>
    public const string LanguagesFile = "languages.toml";

    /// <summary>Whether the root defines any language: a language folder with a catalog file, or a
    /// <see cref="LanguagesFile"/>. While one does, the VM records string stores (spec §5.2), selected or not.</summary>
    public static bool DefinesLanguage(string root)
        => File.Exists(Path.Combine(root, LanguagesFile))
           || Directory.EnumerateDirectories(root)
               .Any(folder => Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories).Any());

    /// <summary>The root's <see cref="LanguagesFile"/>, or none.</summary>
    /// <exception cref="TextCatalogException">The file does not parse or validate.</exception>
    public static IReadOnlyList<LanguageSettings> ReadLanguages(string root)
    {
        string path = Path.Combine(root, LanguagesFile);
        return File.Exists(path)
            ? TextCatalogFileReader.ParseLanguages(File.ReadAllText(path, Encoding.UTF8), LanguagesFile)
            : [];
    }

    /// <summary>
    /// The catalogs of <paramref name="tag"/>'s fallback chain under <paramref name="root"/>. Each entry with
    /// <c>at</c> binds only to an unpatched base script whose fingerprint the file names:
    /// <paramref name="baseFingerprint"/> gives an installed script's fingerprint (null when it is not installed),
    /// and <paramref name="isPatched"/> whether the mod stack changes it.
    /// </summary>
    public static TextLanguage Load(string root, string tag, Func<string, string?> baseFingerprint,
                                    Func<string, bool> isPatched, TextLoadReport report)
    {
        string normalized = LanguageTag.Normalize(tag);
        LanguageSettings? settings = ReadLanguages(root).FirstOrDefault(declared => declared.Tag == normalized);
        var chain = new List<TextCatalog>();
        var problems = new List<string>();
        foreach (string language in new[] { normalized }.Concat(settings?.Fallback ?? LanguageTag.Parents(normalized)))
        {
            string folder = Path.Combine(root, language);
            if (!Directory.Exists(folder)) continue;
            string[] files = Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), StringComparer.Ordinal)
                .ToArray();
            var entries = new List<TextEntry>();
            foreach (string path in files)
            {
                string name = language + "/" + Path.GetRelativePath(folder, path).Replace('\\', '/');
                TextCatalogFile file;
                try
                {
                    file = TextCatalogFileReader.Parse(File.ReadAllText(path, Encoding.UTF8), name);
                }
                catch (TextCatalogException error)
                {
                    problems.AddRange(error.Problems);
                    continue;
                }
                entries.AddRange(Bind(file, name, baseFingerprint, isPatched, report));
            }
            TextCatalog catalog = TextCatalog.Build(language, entries);
            report.Languages.Add((language, files.Length, catalog.Count));
            report.Overrides.AddRange(catalog.Overrides);
            chain.Add(catalog);
        }
        if (problems.Count > 0) throw new TextCatalogException(problems);
        return new TextLanguage(normalized, chain)
        {
            LineBreak = settings?.LineBreak ?? LineBreakMode.Words,
            Measure = settings?.Measure ?? TextMeasure.Presented,
            Digits = settings?.Digits ?? DigitsMode.Keep,
        };
    }

    /// <summary>The file's entries, without its site entries when they cannot bind (spec §9.4 step 3).</summary>
    private static IEnumerable<TextEntry> Bind(TextCatalogFile file, string name, Func<string, string?> baseFingerprint,
                                              Func<string, bool> isPatched, TextLoadReport report)
    {
        var sites = file.Entries.Where(entry => entry.At != null).ToList();
        if (sites.Count == 0) return file.Entries;
        var reasons = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? Reason(string script)
        {
            if (reasons.TryGetValue(script, out string? known)) return known;
            string? reason = baseFingerprint(script) is not { } installed ? "the script is not installed"
                : !string.Equals(installed, file.Fingerprint, StringComparison.OrdinalIgnoreCase)
                    ? "the base script's fingerprint differs"
                    : isPatched(script) ? "a mod patches the script" : null;
            return reasons[script] = reason;
        }
        var kept = new List<TextEntry>();
        var dropped = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TextEntry entry in file.Entries)
        {
            if (entry.At != null && Reason(entry.Script!) is { } reason)
            {
                dropped[reason] = dropped.GetValueOrDefault(reason) + 1;
                continue;
            }
            kept.Add(entry);
        }
        foreach (var (reason, count) in dropped) report.DroppedSites.Add((name, count, reason));
        return kept;
    }
}

using Ome.Engine.Localization;
using Ome.Engine.Sys4;
using Ome.Mods.Linking;
using Ome.Script.Patch;

namespace Ome.Mods.Localization;

/// <summary>A selected language as the port uses it (spec §10, §11): the game text's presenter, null to draw the
/// source text; the language and its fallbacks, in order; and the sources' engine text along that chain.</summary>
public sealed record LanguageSelection(string Tag, TextPresenter? Presenter, IReadOnlyList<string> Lookup,
                                       IReadOnlyList<EngineTextLayer> Engine);

/// <summary>
/// The presenter for a selected language (localization spec §10): none for the source language or a language with
/// no catalog (gate 1), the pseudo-language's filter for <c>x-pseudo</c> (§14), else the language's catalogs from the
/// text sources (<see cref="TextRootLoader"/>): the mod layer's catalogs, then a folder given with
/// <c>--text-root</c>, which wins where both have an entry. Each selection loads the files again, so a language switch
/// picks up edits.
/// </summary>
public sealed class LanguageLaunch(IReadOnlyList<TextSource> sources, TextSiteBinder binder, TextMissLog? misses,
                                   ITextMessageIndex? messages = null)
{
    public IReadOnlyList<TextSource> Sources { get; } = sources;

    /// <summary>Whether the mounted text defines any language, so the VM records string stores (spec §5.2).</summary>
    public bool DefinesLanguage { get; } = sources.Any(source => source.DefinesLanguage);

    /// <summary>The presenter for <paramref name="tag"/>, or null to draw source text.</summary>
    /// <exception cref="TextCatalogException">A catalog file does not parse or validate.</exception>
    public TextPresenter? Select(string tag, TextLoadReport report) => Load(tag, report).Presenter;

    /// <summary>
    /// <paramref name="tag"/> as the port uses it: the game text's presenter (null to draw source text) and, for
    /// the shell's tables (spec §11), the language's fallback chain with the sources' <c>[[engine]]</c> entries.
    /// </summary>
    /// <exception cref="TextCatalogException">A catalog file does not parse or validate.</exception>
    public LanguageSelection Load(string tag, TextLoadReport report)
    {
        string normalized = LanguageTag.Normalize(tag);
        if (normalized == LanguageTag.Source) return new LanguageSelection(normalized, null, [normalized], []);
        if (normalized == LanguageTag.Pseudo)
            return new LanguageSelection(normalized,
                new TextPresenter(TextLanguage.Pseudo, [new PseudoLanguageFilter()], misses), [normalized], []);
        if (Sources.Count == 0)
            return new LanguageSelection(normalized, null, [normalized, .. LanguageTag.Parents(normalized)], []);
        TextLanguage language = TextRootLoader.Load(Sources, normalized, binder, report);
        TextPresenter? presenter = language.Chain.Count == 0
            ? null : new TextPresenter(language, misses: misses, messages: messages);
        return new LanguageSelection(normalized, presenter, language.Lookup, language.Engine);
    }

    /// <summary>
    /// The languages the sources define (spec §10), for the options panel: each declared language with its merged
    /// <c>name</c>, in the order the sources first declare them, then each language folder no source declares (a
    /// text root may leave a language undeclared), by tag. A source whose languages file does not read is skipped;
    /// the load reports it.
    /// </summary>
    public IReadOnlyList<(string Tag, string? Name)> Languages()
    {
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (TextSource source in Sources)
        {
            IReadOnlyList<LanguageSettings> declared;
            try
            {
                declared = source.ReadLanguages();
            }
            catch (TextCatalogException)
            {
                continue;
            }
            foreach (LanguageSettings language in declared)
            {
                if (!names.ContainsKey(language.Tag)) order.Add(language.Tag);
                names[language.Tag] = language.Name ?? names.GetValueOrDefault(language.Tag);
            }
        }
        foreach (string tag in Sources.SelectMany(source => TextRootLoader.LanguageFolders(source.Root))
                     .Where(tag => !names.ContainsKey(tag)).Distinct().Order(StringComparer.Ordinal))
        {
            names[tag] = null;
            order.Add(tag);
        }
        return order.Select(tag => (tag, names[tag])).ToList();
    }

    /// <summary>An installed script's fingerprint (<c>sha256:…</c>, as catalog files and patches name it), from the
    /// install's own catalog and store, before any mod layer; null when the catalog has no such script. Scripts of
    /// the append packs count (<c>$1$CIINIT</c>), and the first script of a name wins, as <c>import-scripts</c> pairs
    /// them.</summary>
    public static Func<string, string?> BaseFingerprints(Sys4AssetCatalog catalog, IAssetStore store)
    {
        var scripts = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        foreach (PackedAssetEntry packed in catalog.EnumerateScripts())
            if (!packed.Asset.IsPlaceholder)
                scripts.TryAdd(TextCatalog.NormalizeScriptName(packed.Asset.Name), packed.Asset);
        var known = new Dictionary<string, string?>(StringComparer.Ordinal);
        return script =>
        {
            string name = TextCatalog.NormalizeScriptName(script);
            lock (known)
            {
                if (known.TryGetValue(name, out string? fingerprint)) return fingerprint;
                fingerprint = scripts.TryGetValue(name, out AssetEntry? entry)
                    ? OmsPatchPlan.FingerprintOf(store.ReadAll(entry))
                    : null;
                return known[name] = fingerprint;
            }
        };
    }
}

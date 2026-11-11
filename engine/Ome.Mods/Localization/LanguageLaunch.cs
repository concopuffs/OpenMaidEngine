using Ome.Engine.Localization;
using Ome.Engine.Sys4;
using Ome.Mods.Linking;
using Ome.Script.Patch;

namespace Ome.Mods.Localization;

/// <summary>
/// The presenter for a selected language (localization spec §10): none for the source language or a language with
/// no catalog (gate 1), the pseudo-language's filter for <c>x-pseudo</c> (§14), else the language's catalogs from the
/// text root (<see cref="TextRootLoader"/>). Packages carry <c>text/</c> from milestone 4; until then the port mounts
/// one folder with <c>--text-root</c>. Each selection loads the files again, so a language switch picks up edits.
/// </summary>
public sealed class LanguageLaunch(string? textRoot, Func<string, string?> baseFingerprint, Func<string, bool> isPatched,
                                   TextMissLog? misses, ITextMessageIndex? messages = null)
{
    public string? TextRoot { get; } = textRoot;

    /// <summary>Whether the mounted text defines any language, so the VM records string stores (spec §5.2).</summary>
    public bool DefinesLanguage { get; } = textRoot != null && TextRootLoader.DefinesLanguage(textRoot);

    /// <summary>The presenter for <paramref name="tag"/>, or null to draw source text.</summary>
    /// <exception cref="TextCatalogException">A catalog file does not parse or validate.</exception>
    public TextPresenter? Select(string tag, TextLoadReport report)
    {
        string normalized = LanguageTag.Normalize(tag);
        if (normalized == LanguageTag.Source) return null;
        if (normalized == LanguageTag.Pseudo)
            return new TextPresenter(TextLanguage.Pseudo, [new PseudoLanguageFilter()], misses);
        if (TextRoot == null) return null;
        TextLanguage language = TextRootLoader.Load(TextRoot, normalized, baseFingerprint, isPatched, report);
        return language.Chain.Count == 0 ? null : new TextPresenter(language, misses: misses, messages: messages);
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

    /// <summary>Whether the linked mod layer replaces or patches a script, so its offsets are not base offsets.</summary>
    public static Func<string, bool> PatchedScripts(MountedModLayer? layer)
    {
        if (layer == null) return _ => false;
        var scripts = layer.Manifest.Files.Keys
            .Where(name => name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
            .Select(TextCatalog.NormalizeScriptName)
            .ToHashSet(StringComparer.Ordinal);
        return script => scripts.Contains(TextCatalog.NormalizeScriptName(script));
    }
}

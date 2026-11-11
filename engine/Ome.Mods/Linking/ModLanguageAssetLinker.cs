using Ome.Engine.Sys4;
using Ome.Mods.Packages;

namespace Ome.Mods.Linking;

/// <summary>A language asset of a link: its language, its bytes, and whether they are a PNG standing for an AGF.</summary>
public sealed record LinkedLanguageAssetFile(string Language, byte[] Bytes, bool Png);

/// <summary>One package's files for an asset name (localization spec §12): whether it supplies the plain asset of that
/// name (an override of the base asset, or its own new asset), and its language assets by tag.</summary>
public sealed record LinkedLanguageAssetLayer(string ModId, bool Plain, IReadOnlyDictionary<string, LinkedLanguageAssetFile> Files);

/// <summary>
/// The language assets of a link (localization spec §12). Each <c>assets/&lt;language&gt;/NAME</c> names a base asset or
/// one of its package's <c>[assets].names</c>, for a language some package in the stack defines; a PNG must decode.
/// For each name that has a language asset, the result keeps every package with a file for it, plain or language, in
/// load order, which is what <see cref="LanguageAssetTable"/> resolves at run time. The report lists language assets
/// that override another package's, and each one a later package's plain asset hides.
/// </summary>
public static class ModLanguageAssetLinker
{
    public static IReadOnlyDictionary<string, IReadOnlyList<LinkedLanguageAssetLayer>> Link(
        IReadOnlyList<ModPackage> order, ModLinkBase linkBase, IReadOnlyDictionary<string, int> selectors,
        ModLinkReport report)
    {
        var result = new Dictionary<string, IReadOnlyList<LinkedLanguageAssetLayer>>(StringComparer.OrdinalIgnoreCase);
        if (order.All(mod => mod.Layout.LanguageAssets.Count == 0)) return result;
        var defined = order.SelectMany(mod => mod.Manifest.Languages).Select(language => language.Tag)
            .ToHashSet(StringComparer.Ordinal);

        // Per asset name, the language files of each package, by load-order index.
        var byName = new Dictionary<string, SortedDictionary<int, Dictionary<string, LinkedLanguageAssetFile>>>(
            StringComparer.OrdinalIgnoreCase);
        var ownNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < order.Count; index++)
        {
            ModPackage mod = order[index];
            var undefined = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ModLanguageAssetFile file in mod.Layout.LanguageAssets)
            {
                if (!defined.Contains(file.Language))
                {
                    undefined.Add(file.Language);
                    continue;
                }
                string? name = EntryName(mod, file, linkBase, selectors, report, out bool own);
                if (name == null) continue;
                byte[] bytes;
                try
                {
                    bytes = mod.ReadAll(file.File);
                    if (file.Png) PngDecoder.Decode(bytes, $"{mod.Id}/{file.File.Path}");
                }
                catch (Exception error) when (error is InvalidDataException or ModPackageException)
                {
                    report.Add(ModReportClass.Error, mod.Id, file.Name, error.Message);
                    continue;
                }
                if (own) ownNames[name] = index;
                if (!byName.TryGetValue(name, out var layers)) byName[name] = layers = new();
                if (!layers.TryGetValue(index, out var files)) layers[index] = files = new(StringComparer.Ordinal);
                files[file.Language] = new LinkedLanguageAssetFile(file.Language, bytes, file.Png);
            }
            foreach (string tag in undefined)
                report.Add(ModReportClass.Error, mod.Id, null,
                    $"assets/{tag}/ holds language assets for '{tag}', which no package in the stack defines; one must "
                    + $"declare it in its manifest ([[language]] tag = \"{tag}\")");
        }

        // The packages whose plain assets supply the same names, as the asset link takes them.
        var plain = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < order.Count; index++)
            foreach (ModAssetFile file in order[index].Layout.Assets)
                if (!file.ListedAsNew && byName.ContainsKey(file.Name) && linkBase.HasAsset(file.Name)
                    && !file.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
                {
                    if (!plain.TryGetValue(file.Name, out var set)) plain[file.Name] = set = [];
                    set.Add(index);
                }

        foreach (var (name, languageLayers) in byName.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var indices = new SortedSet<int>(languageLayers.Keys);
            if (plain.TryGetValue(name, out var plainIndices)) indices.UnionWith(plainIndices);
            var layers = new List<LinkedLanguageAssetLayer>();
            foreach (int index in indices)
            {
                bool isPlain = (plainIndices?.Contains(index) ?? false) || ownNames.GetValueOrDefault(name, -1) == index;
                IReadOnlyDictionary<string, LinkedLanguageAssetFile> files =
                    languageLayers.TryGetValue(index, out var found) ? found : new Dictionary<string, LinkedLanguageAssetFile>();
                layers.Add(new LinkedLanguageAssetLayer(order[index].Id, isPlain, files));
            }
            Report(name, layers, report);
            result[name] = layers;
        }
        return result;
    }

    /// <summary>The catalog name a language asset overrides: its package's own new asset (<c>$selector$NAME</c>) or a
    /// base asset; null, with the reason reported, when it names neither or a script.</summary>
    private static string? EntryName(ModPackage mod, ModLanguageAssetFile file, ModLinkBase linkBase,
                                     IReadOnlyDictionary<string, int> selectors, ModLinkReport report, out bool own)
    {
        own = false;
        string where = file.File.Path;
        if (file.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
        {
            report.Add(ModReportClass.Error, mod.Id, file.Name,
                $"{where}: a script has no language version; the text a script shows is translated in text/");
            return null;
        }
        string? listed = mod.Manifest.AssetNames.FirstOrDefault(name => name.Equals(file.Name, StringComparison.OrdinalIgnoreCase));
        if (listed != null)
        {
            own = true;
            // Without a selector the allocation has already stopped the link.
            return selectors.TryGetValue(mod.Id, out int selector) ? ModCatalogBuilder.CatalogName(selector, listed, false) : null;
        }
        if (linkBase.HasAsset(file.Name)) return file.Name;
        report.Add(ModReportClass.Error, mod.Id, file.Name,
            $"{where}: neither the base nor this package's [assets].names has an asset {file.Name}");
        return null;
    }

    private static void Report(string name, IReadOnlyList<LinkedLanguageAssetLayer> layers, ModLinkReport report)
    {
        var languageOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        string? plainOwner = null;
        foreach (LinkedLanguageAssetLayer layer in layers)
        {
            foreach (string tag in layer.Files.Keys.Order(StringComparer.Ordinal))
            {
                if (languageOwner.TryGetValue(tag, out string? earlier) && earlier != layer.ModId)
                    report.Add(ModReportClass.Override, [earlier, layer.ModId], name,
                        $"{layer.ModId}'s {tag} asset overrides {earlier}'s (later in load order)");
                else if (plainOwner != null && plainOwner != layer.ModId)
                    report.Add(ModReportClass.Override, [plainOwner, layer.ModId], name,
                        $"{layer.ModId}'s {tag} asset overrides {plainOwner}'s asset while {tag} is selected");
                languageOwner[tag] = layer.ModId;
            }
            if (layer.Plain)
            {
                // Localization spec §12: what a later mod changed wins over an earlier package's language asset.
                foreach (var (tag, owner) in languageOwner.Where(pair => pair.Value != layer.ModId
                                                                         && !layer.Files.ContainsKey(pair.Key))
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    report.Add(ModReportClass.Override, [owner, layer.ModId], name,
                        $"{layer.ModId}'s asset hides {owner}'s {tag} asset (later in load order); a {tag} version of "
                        + $"{layer.ModId}'s asset ships in {layer.ModId} or in a package that loads after it");
                foreach (string tag in languageOwner.Keys.Where(tag => !layer.Files.ContainsKey(tag)).ToList())
                    languageOwner.Remove(tag);
                plainOwner = layer.ModId;
            }
        }
    }
}

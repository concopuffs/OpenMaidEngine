using Ome.Engine.Localization;
using Ome.Mods.Localization;
using Ome.Mods.Manifest;

namespace Ome.Mods.Packages;

/// <summary>A patch under one stem: its fragment source, its stored transform plan, or both (spec §4.1).</summary>
public sealed record ModPatchFiles(string Stem, PackageEntry? Source, PackageEntry? Plan);

/// <summary>A new script: its manifest name and its OMS source, in <c>[scripts].names</c> order.</summary>
public sealed record ModScriptFile(string Name, PackageEntry Source);

/// <summary>A whole-script replacement (<c>replace/NAME.BIN</c>).</summary>
public sealed record ModReplacementFile(string Script, PackageEntry File);

/// <summary>A file under <c>assets/</c>: an override of a base asset, or a new asset named in
/// <c>[assets].names</c>. Which one an unlisted file is depends on the base catalog, so the linker decides.</summary>
public sealed record ModAssetFile(string Name, PackageEntry File, bool ListedAsNew);

/// <summary>A catalog under <c>text/&lt;language&gt;/</c> (localization spec §9.1): its language tag, its path inside
/// the language folder (the merge order within the package), and the file.</summary>
public sealed record ModTextFile(string Language, string Name, PackageEntry File);

/// <summary>A font file under <c>fonts/</c> (localization spec §9.1), named by its basename.</summary>
public sealed record ModFontFile(string Name, PackageEntry File);

/// <summary>
/// A package's files sorted into the content kinds of spec §3.2 and §4, and the localization spec's <c>text/</c> and
/// <c>fonts/</c> (§9.1, format version 2). Directories are matched case-insensitively. Files outside the layout are
/// kept as notes (spec §3.2); a wrongly typed or nested file inside a content directory is an error.
/// </summary>
public sealed record ModLayout(
    IReadOnlyList<ModPatchFiles> Patches,
    IReadOnlyList<ModScriptFile> Scripts,
    IReadOnlyList<ModReplacementFile> Replacements,
    IReadOnlyList<ModAssetFile> Assets,
    IReadOnlyList<string> Notes)
{
    public const string PlanSuffix = ".omspatch.json";
    public const string TextFolder = "text", FontsFolder = "fonts";
    /// <summary>Largest font file a package may bundle (localization spec §16).</summary>
    public const long MaxFontBytes = 64L << 20;

    /// <summary>The catalogs under <c>text/</c>, by language and then path, in ordinal order.</summary>
    public IReadOnlyList<ModTextFile> Text { get; init; } = [];
    /// <summary>The font files under <c>fonts/</c>.</summary>
    public IReadOnlyList<ModFontFile> Fonts { get; init; } = [];

    /// <summary>Whether the package changes no script or asset: it holds only <c>text/</c>, <c>fonts/</c> and
    /// <c>[[language]]</c> tables (localization spec §8, save stamps).</summary>
    public bool ChangesNoContent => Patches.Count == 0 && Scripts.Count == 0 && Replacements.Count == 0 && Assets.Count == 0;
    private static readonly string[] Documents = ["README.md", "README.txt", "LICENSE", "LICENSE.md", "LICENSE.txt"];

    public static ModLayout Classify(ModManifest manifest, IReadOnlyList<PackageEntry> entries, List<string> problems)
    {
        var patchSources = new Dictionary<string, PackageEntry>(StringComparer.OrdinalIgnoreCase);
        var patchPlans = new Dictionary<string, PackageEntry>(StringComparer.OrdinalIgnoreCase);
        var scriptSources = new Dictionary<string, PackageEntry>(StringComparer.OrdinalIgnoreCase);
        var replacements = new List<ModReplacementFile>();
        var assetFiles = new List<PackageEntry>();
        var notes = new List<string>();
        var text = new List<ModTextFile>();
        var fonts = new List<ModFontFile>();

        foreach (PackageEntry entry in entries)
        {
            int slash = entry.Path.IndexOf('/');
            string top = slash < 0 ? entry.Path : entry.Path[..slash];
            string rest = slash < 0 ? "" : entry.Path[(slash + 1)..];
            bool nested = rest.Contains('/');
            if (slash < 0)
            {
                if (!entry.Path.Equals(ManifestReader.FileName, StringComparison.Ordinal)
                    && !Documents.Contains(entry.Path, StringComparer.OrdinalIgnoreCase))
                    notes.Add($"'{entry.Path}' is outside the package layout and is ignored");
                continue;
            }
            switch (top.ToLowerInvariant())
            {
                case "patches" when !nested && rest.EndsWith(PlanSuffix, StringComparison.OrdinalIgnoreCase):
                    patchPlans[rest[..^PlanSuffix.Length]] = entry;
                    break;
                case "patches" when !nested && rest.EndsWith(".oms", StringComparison.OrdinalIgnoreCase):
                    patchSources[rest[..^4]] = entry;
                    break;
                case "scripts" when !nested && rest.EndsWith(".oms", StringComparison.OrdinalIgnoreCase):
                    scriptSources[rest[..^4]] = entry;
                    break;
                case "replace" when !nested && rest.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase):
                    replacements.Add(new ModReplacementFile(rest, entry));
                    break;
                case "assets" when !nested:
                    assetFiles.Add(entry);
                    break;
                case TextFolder or FontsFolder when manifest.OmpVersion < ModManifest.LanguageOmpVersion:
                    problems.Add($"'{entry.Path}': {top.ToLowerInvariant()}/ needs omp = {ModManifest.LanguageOmpVersion}");
                    break;
                case TextFolder:
                    if (TextFile(entry, rest, problems) is { } catalog) text.Add(catalog);
                    break;
                case FontsFolder when !nested && TextCatalogFileReader.IsFontFileName(rest):
                    if (entry.Length > MaxFontBytes)
                        problems.Add($"'{entry.Path}': a font may be at most {MaxFontBytes >> 20} MiB");
                    else fonts.Add(new ModFontFile(rest, entry));
                    break;
                case FontsFolder:
                    problems.Add($"'{entry.Path}': fonts/ holds only font files ({string.Join(", ", LanguageFont.Extensions)}), "
                                 + "without subdirectories");
                    break;
                case "patches":
                    problems.Add($"'{entry.Path}': patches/ holds only *.oms fragments and *{PlanSuffix} plans");
                    break;
                case "scripts":
                    problems.Add($"'{entry.Path}': scripts/ holds only *.oms script units");
                    break;
                case "replace":
                    problems.Add($"'{entry.Path}': replace/ holds only whole scripts (NAME.BIN)");
                    break;
                case "assets":
                    problems.Add($"'{entry.Path}': assets/ holds files by basename, without subdirectories");
                    break;
                default:
                    notes.Add($"'{entry.Path}' is outside the package layout and is ignored");
                    break;
            }
        }

        var patches = patchSources.Keys.Union(patchPlans.Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .Select(stem => new ModPatchFiles(stem, patchSources.GetValueOrDefault(stem), patchPlans.GetValueOrDefault(stem)))
            .ToList();

        var scripts = new List<ModScriptFile>();
        foreach (string name in manifest.ScriptNames)
        {
            if (scriptSources.Remove(name, out PackageEntry? source)) scripts.Add(new ModScriptFile(name, source));
            else problems.Add($"scripts.names: '{name}' has no scripts/{name}.oms");
        }
        foreach (PackageEntry unnamed in scriptSources.Values)
            problems.Add($"'{unnamed.Path}' is not named in [scripts].names");

        foreach (ModReplacementOrder order in manifest.ReplacementOrders)
            if (!replacements.Any(file => file.Script.Equals(order.Script, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"replace.\"{order.Script}\": the package has no replace/{order.Script}");

        var newAssets = new HashSet<string>(manifest.AssetNames, StringComparer.OrdinalIgnoreCase);
        var assets = assetFiles.Select(file => new ModAssetFile(file.Path["assets/".Length..], file,
                                                                newAssets.Contains(file.Path["assets/".Length..])))
            .ToList();
        foreach (string name in manifest.AssetNames.Where(name =>
                     !assets.Any(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
            problems.Add($"assets.names: '{name}' has no assets/{name}");

        // Each bundled font a language names must be in fonts/.
        foreach (LanguageSettings language in manifest.Languages)
            foreach (LanguageFont font in language.Fonts ?? [])
                foreach (string use in font.Use.Where(use => use.StartsWith(LanguageFont.FontsFolder, StringComparison.OrdinalIgnoreCase)))
                    if (!fonts.Any(file => file.Name.Equals(use[LanguageFont.FontsFolder.Length..], StringComparison.OrdinalIgnoreCase)))
                        problems.Add($"{language.Origin}: '{use}' is not in the package");

        return new ModLayout(patches, scripts, replacements, assets, notes)
        {
            Text = text.OrderBy(file => file.Language, StringComparer.Ordinal)
                .ThenBy(file => file.Name, StringComparer.Ordinal).ToList(),
            Fonts = fonts,
        };
    }

    /// <summary>A file under <c>text/</c>: a catalog at <c>text/&lt;tag&gt;/…/NAME.toml</c>, where the tag is a
    /// well-formed lowercase language other than the source and the pseudo-language.</summary>
    private static ModTextFile? TextFile(PackageEntry entry, string rest, List<string> problems)
    {
        int slash = rest.IndexOf('/');
        string tag = slash < 0 ? "" : rest[..slash];
        string name = slash < 0 ? rest : rest[(slash + 1)..];
        if (slash < 0 || !name.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"'{entry.Path}': text/ holds catalogs at text/<language>/*.toml");
            return null;
        }
        if (tag != LanguageTag.Normalize(tag) || !LanguageTag.IsWellFormed(tag) || tag == LanguageTag.Source
            || tag == LanguageTag.Pseudo)
        {
            problems.Add($"'{entry.Path}': '{tag}' is not a lowercase language tag a package can carry text for");
            return null;
        }
        if (entry.Length > TextCatalogFileReader.MaxBytes)
        {
            problems.Add($"'{entry.Path}': a catalog file may be at most {TextCatalogFileReader.MaxBytes >> 20} MiB");
            return null;
        }
        return new ModTextFile(tag, name, entry);
    }
}

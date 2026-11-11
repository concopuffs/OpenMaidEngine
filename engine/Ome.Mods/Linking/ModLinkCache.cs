using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ome.Engine.Localization;
using Ome.Engine.Sys4;
using Ome.Mods.Localization;
using Ome.Mods.Packages;
using Ome.Script.Patch;
using Ome.Script.Syntax;

namespace Ome.Mods.Linking;

/// <summary>One mod of a linked stack, as the cache and save stamps record it. <see cref="Presentation"/> marks a
/// package that only presents text (localization spec §8): it changes no script or asset.</summary>
public sealed record ModStackEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("presentation")] bool Presentation = false);

/// <summary>A <c>[[language.font]]</c> table in <c>link.json</c>.</summary>
public sealed record LinkedFontTable(
    [property: JsonPropertyName("requested")] IReadOnlyList<string> Requested,
    [property: JsonPropertyName("use")] IReadOnlyList<string> Use);

/// <summary>A merged <c>[[language]]</c> in <c>link.json</c> (localization spec §9.2): bundled fonts as paths under
/// the cache directory (<c>fonts/&lt;mod&gt;/&lt;name&gt;</c>), and the mods that define it.</summary>
public sealed record LinkedLanguageEntry
{
    [JsonPropertyName("tag")] public required string Tag { get; init; }
    [JsonPropertyName("defined_by")] public IReadOnlyList<string> DefinedBy { get; init; } = [];
    [JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; init; }
    [JsonPropertyName("fallback"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Fallback { get; init; }
    [JsonPropertyName("line_break"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LineBreak { get; init; }
    [JsonPropertyName("measure"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Measure { get; init; }
    [JsonPropertyName("digits"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Digits { get; init; }
    [JsonPropertyName("glyph_delay_scale"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GlyphDelayScale { get; init; }
    [JsonPropertyName("min_fit_scale"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinFitScale { get; init; }
    [JsonPropertyName("fonts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<LinkedFontTable>? Fonts { get; init; }

    public static LinkedLanguageEntry Of(LinkedLanguage language)
    {
        LanguageSettings s = language.Settings;
        return new LinkedLanguageEntry
        {
            Tag = s.Tag,
            DefinedBy = language.DefinedBy,
            Name = s.Name,
            Fallback = s.Fallback,
            LineBreak = s.LineBreak switch { LineBreakMode.Anywhere => "anywhere", LineBreakMode.Words => "words", _ => null },
            Measure = s.Measure switch { TextMeasure.Source => "source", TextMeasure.Presented => "presented", _ => null },
            Digits = s.Digits switch { DigitsMode.Ascii => "ascii", DigitsMode.Keep => "keep", _ => null },
            GlyphDelayScale = s.GlyphDelayScale,
            MinFitScale = s.MinFitScale,
            Fonts = s.Fonts?.Select(font => new LinkedFontTable(font.Requested, font.Use)).ToList(),
        };
    }

    /// <summary>The settings, each bundled font resolved under <paramref name="directory"/>.</summary>
    public LanguageSettings ToSettings(string directory) => new(Tag)
    {
        Name = Name,
        Fallback = Fallback,
        LineBreak = LineBreak switch { "anywhere" => LineBreakMode.Anywhere, "words" => LineBreakMode.Words, _ => null },
        Measure = Measure switch { "source" => TextMeasure.Source, "presented" => TextMeasure.Presented, _ => null },
        Digits = Digits switch { "ascii" => DigitsMode.Ascii, "keep" => DigitsMode.Keep, _ => null },
        GlyphDelayScale = GlyphDelayScale,
        MinFitScale = MinFitScale,
        Fonts = Fonts?.Select(font => new LanguageFont(font.Requested, font.Use)
            .Resolve(use => Path.GetFullPath(Path.Combine(directory, use)))).ToList(),
        Origin = $"mods: {string.Join(", ", DefinedBy)}",
    };
}

/// <summary>One package's files for a name in <c>link.json</c>'s <c>language_assets</c> (localization spec §12):
/// whether it supplies the plain asset, and its language assets by tag, as paths under the cache directory.</summary>
public sealed record LinkedLanguageAssetEntry(
    [property: JsonPropertyName("mod")] string Mod,
    [property: JsonPropertyName("plain")] bool Plain,
    [property: JsonPropertyName("files")] IReadOnlyDictionary<string, string> Files);

/// <summary>Which mods supplied a file of the linked layer (spec §6.5): for a script, the replacing mod and the
/// patching mods; for an asset override or a catalog, its mod.</summary>
public sealed record ModLayerSource(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("mods")] IReadOnlyList<string> Mods,
    [property: JsonPropertyName("detail")] string Detail);

/// <summary>The <c>link.json</c> of a cache directory.</summary>
public sealed record ModLinkManifest
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("profile")] public required string Profile { get; init; }
    [JsonPropertyName("order")] public required IReadOnlyList<ModStackEntry> Order { get; init; }
    [JsonPropertyName("selectors")] public required IReadOnlyDictionary<string, int> Selectors { get; init; }
    /// <summary>Files of <c>loose/</c> and <c>catalogs/</c>, by name, and what supplied them.</summary>
    [JsonPropertyName("files")] public required IReadOnlyDictionary<string, ModLayerSource> Files { get; init; }
    /// <summary>Every record a mod catalog holds, by its catalog name (<c>$sel$NAME</c>).</summary>
    [JsonPropertyName("records")] public required IReadOnlyDictionary<string, string> Records { get; init; }
    /// <summary>The languages the stack defines (localization spec §9.2).</summary>
    [JsonPropertyName("languages")] public IReadOnlyList<LinkedLanguageEntry> Languages { get; init; } = [];
    /// <summary>The catalogs under <c>text/</c>, as paths inside it, in load order.</summary>
    [JsonPropertyName("text")] public IReadOnlyList<string> Text { get; init; } = [];
    /// <summary>The bundled fonts, as paths under the cache directory.</summary>
    [JsonPropertyName("fonts")] public IReadOnlyList<string> Fonts { get; init; } = [];
    /// <summary>The language assets (localization spec §12), by catalog name: every package with a file for it, in
    /// load order.</summary>
    [JsonPropertyName("language_assets")]
    public IReadOnlyDictionary<string, IReadOnlyList<LinkedLanguageAssetEntry>> LanguageAssets { get; init; }
        = new Dictionary<string, IReadOnlyList<LinkedLanguageAssetEntry>>();
    /// <summary>For each patched script no mod replaces, where its base instructions went (<c>[start, end, delta]</c>
    /// runs of dword offsets; localization spec §6.1).</summary>
    [JsonPropertyName("offsets")]
    public IReadOnlyDictionary<string, IReadOnlyList<int[]>> Offsets { get; init; } = new Dictionary<string, IReadOnlyList<int[]>>();
}

/// <summary>A linked stack as mounted (spec §6.4–§6.5): the cache directory, its <c>loose/</c> root (first in the
/// loose-root list) and its <c>catalogs/</c> directory (mounted after the install's own <c>*.AAI</c>).</summary>
public sealed record MountedModLayer(string Directory, ModLinkManifest Manifest, string ReportText)
{
    public const string LooseFolder = "loose", CatalogFolder = "catalogs", ManifestFile = "link.json", ReportFile = "mod-link.txt";
    public const string TextFolder = "text", LanguageAssetFolder = "assets";

    public string Key => Manifest.Key;
    public string LooseRoot => Path.Combine(Directory, LooseFolder);
    public string CatalogDirectory => Path.Combine(Directory, CatalogFolder);

    /// <summary>The stack's catalogs, laid out as a text root (<see cref="TextRootLoader"/>): one folder per language,
    /// a folder per mod inside it, named so that the ordinal order is the load order.</summary>
    public string TextRoot => Path.Combine(Directory, TextFolder);

    /// <summary>The languages the stack defines, with bundled fonts as absolute paths into the cache.</summary>
    public IReadOnlyList<LanguageSettings> Languages
        => Manifest.Languages.Select(language => language.ToSettings(Directory)).ToList();

    /// <summary>The stack's catalogs and languages, as one text source.</summary>
    public TextSource? TextSource
        => Manifest.Text.Count == 0 && Manifest.Languages.Count == 0 ? null : new TextSource(TextRoot, Languages, "mods");

    /// <summary>The stack's language assets, with their files as absolute paths into the cache.</summary>
    public LanguageAssetTable LanguageAssets
        => Manifest.LanguageAssets.Count == 0
            ? LanguageAssetTable.Empty
            : new LanguageAssetTable(Manifest.LanguageAssets.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<LanguageAssetLayer>)pair.Value.Select(entry => new LanguageAssetLayer(entry.Mod,
                    entry.Plain, entry.Files.ToDictionary(file => file.Key,
                        file => Path.GetFullPath(Path.Combine(Directory, file.Value)), StringComparer.Ordinal))).ToList(),
                StringComparer.OrdinalIgnoreCase));

    private readonly Lazy<IReadOnlyDictionary<string, ScriptOffsetMap>> _offsets = new(() =>
        Manifest.Offsets.ToDictionary(pair => TextCatalog.NormalizeScriptName(pair.Key),
            pair => ScriptOffsetMap.FromArrays(pair.Value), StringComparer.Ordinal));

    /// <summary>How a site entry's base offset in <paramref name="script"/> binds to the linked script: null when the
    /// layer leaves the script as the base has it; otherwise the offset table of a patched script, or none when a mod
    /// replaces it (localization spec §6.1).</summary>
    public (bool Changed, ScriptOffsetMap? Map) SiteBinding(string script)
    {
        string name = TextCatalog.NormalizeScriptName(script) + ".BIN";
        if (!Manifest.Files.TryGetValue(name, out ModLayerSource? file) || file.Kind != "script") return (false, null);
        return (true, _offsets.Value.GetValueOrDefault(TextCatalog.NormalizeScriptName(script)));
    }

    /// <summary>The mod that supplied a script or asset of the layer, by catalog name, or null for the base; with the
    /// language assets that can stand in for it.</summary>
    public string? Explain(string name)
    {
        string? languages = Manifest.LanguageAssets.TryGetValue(name, out var layers)
            ? "; language assets: " + string.Join(", ", layers.SelectMany(layer => layer.Files.Keys
                .Select(tag => $"{tag} from {layer.Mod}")))
            : null;
        if (Manifest.Files.TryGetValue(name, out ModLayerSource? file))
            return $"{file.Kind} from {string.Join(", ", file.Mods)}" + (file.Detail.Length > 0 ? $" ({file.Detail})" : "")
                   + languages;
        if (Manifest.Records.TryGetValue(name, out string? mod)) return $"record of {mod}'s append catalog" + languages;
        return languages == null ? null : "the base" + languages;
    }
}

/// <summary>
/// The link cache of spec §6.4, under <c>mods/.cache/</c> in the profile's data root. The key is a SHA-256 over the
/// profile and the base fingerprints of every touched script, each mod's id, version and package hash in load order,
/// the selector allocation, and the toolchain stamp. It holds only derived files; a directory is written under a
/// temporary name and renamed, so a failed write never leaves half a cache.
/// </summary>
public static class ModLinkCache
{
    public const string FolderName = ".cache";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string ComputeKey(ModLinkBase linkBase, IReadOnlyList<ModPackage> order,
                                    IReadOnlyDictionary<string, int> selectors)
    {
        var text = new StringBuilder();
        text.Append("omp-link 1\n").Append("profile ").Append(linkBase.Profile).Append('\n')
            .Append("catalog ").Append(linkBase.CatalogFingerprint).Append('\n');
        foreach (string script in order.SelectMany(TouchedScripts).Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.Ordinal))
        {
            byte[]? bytes = linkBase.ReadScript(script);
            text.Append("script ").Append(script).Append(' ')
                .Append(bytes == null ? "missing" : OmsPatchPlan.FingerprintOf(bytes)).Append('\n');
        }
        foreach (ModPackage mod in order)
            text.Append("mod ").Append(mod.Id).Append(' ').Append(mod.Manifest.Version).Append(' ').Append(mod.Hash).Append('\n');
        foreach (var (id, selector) in selectors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.Append("selector ").Append(id).Append(' ').Append(selector).Append('\n');
        text.Append("toolchain\n").Append(linkBase.ToolchainStamp).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    /// <summary>The base scripts a mod changes: its replacements and its patch targets. A plan or fragment that cannot
    /// be read is left out here; the link reports it.</summary>
    public static IEnumerable<string> TouchedScripts(ModPackage mod)
    {
        foreach (ModReplacementFile file in mod.Layout.Replacements) yield return file.Script.ToUpperInvariant();
        foreach (ModPatchFiles patch in mod.Layout.Patches)
        {
            string? target = null;
            try
            {
                if (patch.Plan is { } plan) target = OmsPatchPlan.FromJson(mod.ReadText(plan)).Script;
                else if (patch.Source is { } source) target = Parser.ParseScript(mod.ReadText(source), source.Path).Name;
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or OmsSyntaxException
                                              or ModPackageException) { }
            if (target != null) yield return target.ToUpperInvariant();
        }
    }

    /// <summary>The cached layer of <paramref name="key"/>, or null when there is none or it is incomplete.</summary>
    public static MountedModLayer? TryOpen(string cacheRoot, string key)
    {
        string directory = Path.Combine(cacheRoot, key);
        string manifestPath = Path.Combine(directory, MountedModLayer.ManifestFile);
        if (!File.Exists(manifestPath)) return null;
        try
        {
            ModLinkManifest? manifest = JsonSerializer.Deserialize<ModLinkManifest>(File.ReadAllText(manifestPath), Json);
            if (manifest == null || manifest.Key != key) return null;
            var layer = new MountedModLayer(directory, manifest,
                File.Exists(Path.Combine(directory, MountedModLayer.ReportFile))
                    ? File.ReadAllText(Path.Combine(directory, MountedModLayer.ReportFile)) : "");
            foreach (var (name, source) in manifest.Files)
                if (!File.Exists(Path.Combine(source.Kind == "catalog" ? layer.CatalogDirectory : layer.LooseRoot, name)))
                    return null;
            if (manifest.Text.Any(path => !File.Exists(Path.Combine(layer.TextRoot, path)))
                || manifest.Fonts.Any(path => !File.Exists(Path.Combine(directory, path)))
                || manifest.LanguageAssets.Values.SelectMany(entries => entries).SelectMany(entry => entry.Files.Values)
                    .Any(path => !File.Exists(Path.Combine(directory, path))))
                return null;
            // A hit counts as recent use, so pruning keeps the entries that are actually launched.
            try
            {
                Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return layer;
        }
        catch (Exception error) when (error is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>How many link entries the cache keeps; older ones are removed after each write.</summary>
    public const int KeptEntries = 8;
    /// <summary>Leftovers of an interrupted write (<c>.tmp-</c>, <c>.old-</c>) and surplus entries younger than this
    /// stay, in case another running instance is writing or has one mounted.</summary>
    public static readonly TimeSpan PruneMinimumAge = TimeSpan.FromHours(1);

    /// <summary>Removes interrupted-write leftovers and all but the <see cref="KeptEntries"/> most recently written
    /// entries, each only when older than <see cref="PruneMinimumAge"/>; <paramref name="keep"/> always stays.</summary>
    public static void Prune(string cacheRoot, string keep)
    {
        DateTime cutoff = DateTime.UtcNow - PruneMinimumAge;
        DirectoryInfo[] directories;
        try
        {
            directories = new DirectoryInfo(cacheRoot).GetDirectories();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }
        bool IsLeftover(DirectoryInfo directory) => directory.Name.Contains(".tmp-") || directory.Name.Contains(".old-");
        foreach (DirectoryInfo leftover in directories.Where(IsLeftover).Where(item => item.LastWriteTimeUtc < cutoff))
            TryDeleteDirectory(leftover.FullName);
        foreach (DirectoryInfo surplus in directories.Where(item => !IsLeftover(item))
                     .Where(item => !string.Equals(item.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(item => item.LastWriteTimeUtc).Skip(KeptEntries - 1)
                     .Where(item => item.LastWriteTimeUtc < cutoff))
            TryDeleteDirectory(surplus.FullName);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Writes a successful link as the cache directory of <paramref name="key"/> and returns it mounted.</summary>
    public static MountedModLayer Write(string cacheRoot, string key, ModLinkBase linkBase, ModLinkResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("a failed link is never cached");
        Directory.CreateDirectory(cacheRoot);
        string final = Path.Combine(cacheRoot, key);
        string temporary = Path.Combine(cacheRoot, $"{key}.tmp-{Guid.NewGuid():N}");
        string loose = Path.Combine(temporary, MountedModLayer.LooseFolder);
        string catalogs = Path.Combine(temporary, MountedModLayer.CatalogFolder);
        Directory.CreateDirectory(loose);
        Directory.CreateDirectory(catalogs);
        try
        {
            var files = new SortedDictionary<string, ModLayerSource>(StringComparer.OrdinalIgnoreCase);
            var records = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (LinkedScript script in result.Scripts.Values.OrderBy(script => script.Name, StringComparer.Ordinal))
            {
                File.WriteAllBytes(Path.Combine(loose, script.Name), script.Bytes);
                var mods = new List<string>();
                if (script.ReplacedBy != null) mods.Add(script.ReplacedBy);
                mods.AddRange(script.PatchedBy.Where(mod => !mods.Contains(mod)));
                files[script.Name] = new ModLayerSource("script", mods,
                    (script.ReplacedBy != null ? $"replaced by {script.ReplacedBy}" : "base")
                    + (script.PatchedBy.Count > 0 ? $", patched by {string.Join(", ", script.PatchedBy)}" : ""));
            }
            foreach (LinkedAsset asset in result.Assets.Values.OrderBy(asset => asset.Name, StringComparer.Ordinal))
            {
                File.WriteAllBytes(Path.Combine(loose, asset.Name), asset.Bytes);
                files[asset.Name] = new ModLayerSource("asset", [asset.ModId], "override of the base asset");
            }
            foreach (LinkedTextFile file in result.Text.Files)
            {
                string path = Path.Combine(temporary, MountedModLayer.TextFolder, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Bytes);
            }
            foreach (LinkedFont font in result.Text.Fonts)
            {
                string path = Path.Combine(temporary, font.CachePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, font.Bytes);
            }
            // Language assets as assets/<tag>/<mod>/NAME, a PNG under its own extension so the store knows it.
            var languageAssets = new SortedDictionary<string, IReadOnlyList<LinkedLanguageAssetEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, layers) in result.LanguageAssets)
            {
                var entries = new List<LinkedLanguageAssetEntry>();
                foreach (LinkedLanguageAssetLayer layer in layers)
                {
                    var paths = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (tag, file) in layer.Files)
                    {
                        string fileName = file.Png ? Path.GetFileNameWithoutExtension(name) + ModLayout.PngExtension : name;
                        string relative = $"{MountedModLayer.LanguageAssetFolder}/{tag}/{layer.ModId}/{fileName}";
                        string path = Path.Combine(temporary, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, file.Bytes);
                        paths[tag] = relative;
                    }
                    entries.Add(new LinkedLanguageAssetEntry(layer.ModId, layer.Plain, paths));
                }
                languageAssets[name] = entries;
            }
            foreach (LinkedCatalog catalog in result.Catalogs)
            {
                var (aai, alf) = catalog.Write(linkBase.AppendMagic, linkBase.CatalogTitle);
                File.WriteAllBytes(Path.Combine(catalogs, catalog.CatalogFile), aai);
                File.WriteAllBytes(Path.Combine(catalogs, catalog.ArchiveFile), alf);
                string detail = $"selector {catalog.Selector}, {catalog.Records.Count} records";
                files[catalog.CatalogFile] = new ModLayerSource("catalog", [catalog.ModId], detail);
                files[catalog.ArchiveFile] = new ModLayerSource("catalog", [catalog.ModId], detail);
                foreach (LinkedRecord record in catalog.Records) records[record.CatalogName] = catalog.ModId;
            }
            var manifest = new ModLinkManifest
            {
                Key = key,
                Profile = linkBase.Profile,
                Order = result.Order.Select(mod => new ModStackEntry(mod.Id, mod.Manifest.Version.ToString(), mod.Hash,
                    mod.PresentationOnly)).ToList(),
                Selectors = result.Catalogs.ToDictionary(catalog => catalog.ModId, catalog => catalog.Selector, StringComparer.Ordinal),
                Files = files,
                Records = records,
                Languages = result.Text.Languages.Select(LinkedLanguageEntry.Of).ToList(),
                Text = result.Text.Files.Select(file => file.Path).ToList(),
                Fonts = result.Text.Fonts.Select(font => font.CachePath).ToList(),
                LanguageAssets = languageAssets,
                Offsets = result.Scripts.Values.Where(script => script.Offsets != null)
                    .OrderBy(script => script.Name, StringComparer.Ordinal)
                    .ToDictionary(script => script.Name, script => script.Offsets!.ToArrays(), StringComparer.OrdinalIgnoreCase),
            };
            string report = result.Report.Render();
            File.WriteAllText(Path.Combine(temporary, MountedModLayer.ManifestFile), JsonSerializer.Serialize(manifest, Json),
                              new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(temporary, MountedModLayer.ReportFile), report, new UTF8Encoding(false));

            // An existing entry (incomplete, or TryOpen would have used it) is moved aside before the new one moves in,
            // so the key's directory is never missing while a whole entry exists.
            string? previous = null;
            if (Directory.Exists(final))
            {
                previous = Path.Combine(cacheRoot, $"{key}.old-{Guid.NewGuid():N}");
                Directory.Move(final, previous);
            }
            Directory.Move(temporary, final);
            if (previous != null) TryDeleteDirectory(previous);
            Prune(cacheRoot, keep: final);
            return new MountedModLayer(final, manifest, report);
        }
        catch
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            throw;
        }
    }
}

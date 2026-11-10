using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ome.Mods.Packages;
using Ome.Script.Patch;
using Ome.Script.Syntax;

namespace Ome.Mods.Linking;

/// <summary>One mod of a linked stack, as the cache and save stamps record it.</summary>
public sealed record ModStackEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("hash")] string Hash);

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
}

/// <summary>A linked stack as mounted (spec §6.4–§6.5): the cache directory, its <c>loose/</c> root (first in the
/// loose-root list) and its <c>catalogs/</c> directory (mounted after the install's own <c>*.AAI</c>).</summary>
public sealed record MountedModLayer(string Directory, ModLinkManifest Manifest, string ReportText)
{
    public const string LooseFolder = "loose", CatalogFolder = "catalogs", ManifestFile = "link.json", ReportFile = "mod-link.txt";

    public string Key => Manifest.Key;
    public string LooseRoot => Path.Combine(Directory, LooseFolder);
    public string CatalogDirectory => Path.Combine(Directory, CatalogFolder);

    /// <summary>The mod that supplied a script or asset of the layer, by catalog name, or null for the base.</summary>
    public string? Explain(string name)
    {
        if (Manifest.Files.TryGetValue(name, out ModLayerSource? file))
            return $"{file.Kind} from {string.Join(", ", file.Mods)}" + (file.Detail.Length > 0 ? $" ({file.Detail})" : "");
        return Manifest.Records.TryGetValue(name, out string? mod) ? $"record of {mod}'s append catalog" : null;
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
                Order = result.Order.Select(mod => new ModStackEntry(mod.Id, mod.Manifest.Version.ToString(), mod.Hash)).ToList(),
                Selectors = result.Catalogs.ToDictionary(catalog => catalog.ModId, catalog => catalog.Selector, StringComparer.Ordinal),
                Files = files,
                Records = records,
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

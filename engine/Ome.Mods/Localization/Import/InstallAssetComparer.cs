using Ome.Engine.Sys4;

namespace Ome.Mods.Localization.Import;

/// <summary>
/// Compares the non-script assets of a translated install with its base (localization spec §12, §18.1): the
/// images it replaces become the list for <c>assets/&lt;language&gt;/</c>, and anything else it changes is what the
/// design has no home for. Assets that both installs serve from archives of the same name, at the same range, are
/// compared by archive size unless <c>compareArchives</c> asks for the archives' contents.
/// </summary>
public static class InstallAssetComparer
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".AGF", ".BMP", ".PNG", ".JPG", ".JPEG",
    };

    private static readonly HashSet<string> InstallFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYS4INI.BIN",
    };

    public static AssetCounts Compare(Sys4AssetCatalog baseCatalog, Sys4AssetStore baseStore, string baseRoot,
                                      Sys4AssetCatalog translatedCatalog, Sys4AssetStore translatedStore,
                                      string translatedRoot, IReadOnlyList<string> translatedOverlays,
                                      bool compareArchives)
    {
        var counts = new AssetCounts { ArchivesCompared = compareArchives ? "content" : "size" };
        Dictionary<string, AssetEntry> baseAssets = ByName(baseCatalog);
        Dictionary<string, AssetEntry> translatedAssets = ByName(translatedCatalog);
        var archives = new Dictionary<(string, string), bool>();

        foreach (var (name, entry) in baseAssets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (IsScript(name)) continue;
            if (!translatedAssets.TryGetValue(name, out AssetEntry? other))
            {
                counts.BaseOnly.Add(name);
                continue;
            }
            if (Same(entry, baseStore, other, translatedStore, compareArchives, archives)) continue;
            if (ImageExtensions.Contains(Path.GetExtension(name))) counts.ReplacedImages.Add(name);
            else counts.ChangedOther.Add(name);
        }
        foreach (string name in translatedAssets.Keys.Where(name => !baseAssets.ContainsKey(name))
                     .Order(StringComparer.Ordinal))
            counts.TranslatedOnly.Add(name);

        // Loose files that are not catalog assets: the engine, its resource DLL, launchers and notes.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in translatedOverlays.Append(translatedRoot))
            foreach (string path in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(path);
                string extension = Path.GetExtension(name);
                if (translatedAssets.ContainsKey(name.ToUpperInvariant()) || InstallFiles.Contains(name)
                    || extension.Equals(".ALF", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".AAI", StringComparison.OrdinalIgnoreCase)
                    || !seen.Add(name))
                    continue;
                string relative = Path.GetRelativePath(translatedRoot, path);
                string counterpart = Path.Combine(baseRoot, name);
                if (!File.Exists(counterpart)) counts.NonAssetFiles.Add($"{relative} (only in the translation)");
                else if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(counterpart)))
                    counts.NonAssetFiles.Add($"{relative} (differs)");
            }
        return counts;
    }

    private static bool IsScript(string name) => name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, AssetEntry> ByName(Sys4AssetCatalog catalog)
    {
        var assets = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        foreach (PackedAssetEntry packed in catalog.EnumerateAssets())
            assets.TryAdd(packed.Asset.Name.ToUpperInvariant(), packed.Asset);
        return assets;
    }

    private static bool Same(AssetEntry a, Sys4AssetStore aStore, AssetEntry b, Sys4AssetStore bStore,
                             bool compareArchives, Dictionary<(string, string), bool> archives)
    {
        AssetLocation aAt, bAt;
        try
        {
            aAt = aStore.Locate(a);
            bAt = bStore.Locate(b);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        if (!aAt.Loose && !bAt.Loose
            && string.Equals(Path.GetFileName(aAt.Path), Path.GetFileName(bAt.Path), StringComparison.OrdinalIgnoreCase)
            && aAt.Offset == bAt.Offset && aAt.Size == bAt.Size)
        {
            if (!archives.TryGetValue((aAt.Path, bAt.Path), out bool equal))
                archives[(aAt.Path, bAt.Path)] = equal = ArchivesEqual(aAt.Path, bAt.Path, compareArchives);
            if (equal) return true;
        }
        try
        {
            return aStore.ReadAll(a).AsSpan().SequenceEqual(bStore.ReadAll(b));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ArchivesEqual(string a, string b, bool compareContents)
    {
        if (!File.Exists(a) || !File.Exists(b)) return false;
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        if (!compareContents) return true;
        using FileStream first = File.OpenRead(a), second = File.OpenRead(b);
        var x = new byte[1 << 20];
        var y = new byte[1 << 20];
        while (true)
        {
            int read = first.ReadAtLeast(x, x.Length, throwOnEndOfStream: false);
            int other = second.ReadAtLeast(y.AsSpan(0, read), read, throwOnEndOfStream: false);
            if (read != other || !x.AsSpan(0, read).SequenceEqual(y.AsSpan(0, other))) return false;
            if (read == 0) return true;
        }
    }
}

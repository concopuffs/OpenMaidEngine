using System.Text.Json;

namespace Age.Engine.Sys4;

/// <summary>One SYS4INI asset entry.</summary>
public sealed record AssetEntry(string Name, string Archive, long Offset, long Size);

/// <summary>
/// Static asset resolver. SYS4INI's file list is sectioned (one per scene: SCxxxx.BIN + its
/// cross-archive asset manifest); file_number is the index within a section. So a bytecode
/// resId resolves as files[section_base(scene) + resId] -- unified for graphics and audio.
/// See docs/asset-resolution-re.md. Built from build/asset-index.json + build/asset-sections.json.
/// </summary>
public sealed class ResourceMap
{
    private readonly IReadOnlyList<AssetEntry> _files;
    private readonly IReadOnlyDictionary<string, int> _sceneBase;   // "SC0000" -> section base index

    public ResourceMap(IReadOnlyList<AssetEntry> files, IReadOnlyDictionary<string, int> sceneBase)
    {
        _files = files;
        _sceneBase = sceneBase;
    }

    public static ResourceMap Load(string indexPath, string sectionsPath)
    {
        var files = new List<AssetEntry>();
        using (var idx = JsonDocument.Parse(File.ReadAllText(indexPath)))
            foreach (var f in idx.RootElement.GetProperty("files").EnumerateArray())
                files.Add(new AssetEntry(
                    f.GetProperty("name").GetString()!,
                    f.GetProperty("archive").GetString()!,
                    f.GetProperty("offset").GetInt64(),
                    f.GetProperty("size").GetInt64()));

        var sceneBase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var sec = JsonDocument.Parse(File.ReadAllText(sectionsPath)))
            foreach (var p in sec.RootElement.GetProperty("scene_base").EnumerateObject())
                sceneBase[p.Name] = p.Value.GetInt32();

        return new ResourceMap(files, sceneBase);
    }

    public static ResourceMap Load() => Load(Paths.AssetIndexJson, Paths.AssetSectionsJson);

    /// <summary>Resolve a scene-local resId to its asset, or null if out of range / unknown scene.</summary>
    public AssetEntry? Resolve(string scene, long resId)
    {
        var key = scene.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
            ? scene[..^4] : scene;
        if (!_sceneBase.TryGetValue(key, out var b)) return null;
        long p = b + resId;
        return p >= 0 && p < _files.Count ? _files[(int)p] : null;
    }

    /// <summary>Pre-converted BMP path for an AGF asset (see tools/convert_agf.py).</summary>
    public static string? TexturePath(AssetEntry a)
    {
        if (!a.Name.EndsWith(".AGF", StringComparison.OrdinalIgnoreCase)) return null;
        var bmp = Path.Combine(Paths.Textures, Path.GetFileNameWithoutExtension(a.Name) + ".BMP");
        return File.Exists(bmp) ? bmp : null;
    }

    /// <summary>
    /// Resolve a BGM id to its OGG path. BGM is addressed by DIRECT LITERAL NAME (BGM{id:D3}.OGG), NOT the
    /// per-scene section manifest that voices/textures use. Confirmed by ear (play-bgm 5->BGM005, 8->BGM008)
    /// and by the play-bgm 0x23->BGM035 case: BGM035 is a real standalone track (the BGM set skips 030-034),
    /// which the manifest mis-resolved to a graphics entry. See docs/asset-resolution-re.md.
    /// </summary>
    public string? BgmPathById(long id)
    {
        var name = $"BGM{id:D3}.OGG";
        foreach (var f in _files)
            if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return AudioPath(f);
        return null;
    }

    /// <summary>Loose extracted OGG path for an audio asset (extracted/DATA{n}/{name}), or null.
    /// OGG plays natively in Godot. Used for voices (which DO use the per-scene manifest via Resolve).</summary>
    public static string? AudioPath(AssetEntry a)
    {
        if (!a.Name.EndsWith(".OGG", StringComparison.OrdinalIgnoreCase)) return null;
        var dir = a.Archive.EndsWith(".ALF", StringComparison.OrdinalIgnoreCase)
            ? a.Archive[..^4] : a.Archive;                       // "DATA3.ALF" -> "DATA3"
        var ogg = Path.Combine(Paths.Extracted, dir, a.Name);
        return File.Exists(ogg) ? ogg : null;
    }
}

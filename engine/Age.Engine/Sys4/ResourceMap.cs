namespace Age.Engine.Sys4;

/// <summary>
/// Compatibility facade over the runtime SYS4 catalog. SYS4INI's file list is sectioned (one per scene: SCxxxx.BIN + its
/// cross-archive asset manifest); file_number is the index within a section. So a bytecode
/// resId resolves as files[section_base(scene) + resId] -- unified for graphics and audio.
/// See docs/asset-resolution-re.md. Extracted paths remain temporary graphics/audio backends only.
/// </summary>
public sealed class ResourceMap
{
    private readonly Sys4AssetCatalog _catalog;

    public ResourceMap(Sys4AssetCatalog catalog) => _catalog = catalog;

    public static ResourceMap Load() => new(Sys4AssetCatalog.Load(Paths.Sys4Ini));

    /// <summary>Resolve a scene-local resId to its asset, or null if out of range / unknown scene.</summary>
    public AssetEntry? Resolve(string scene, long resId)
    {
        return _catalog.ResolveScene(scene, resId);
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
        var f = _catalog.ResolveName(name);
        return f == null ? null : AudioPath(f);
    }

    /// <summary>Loose extracted OGG/WAV path for an audio asset (extracted/DATA{n}/{name}), or null.
    /// Used by the current bootstrap audio backend; voices and SFX use the scene manifest via Resolve.</summary>
    public static string? AudioPath(AssetEntry a)
    {
        if (!a.Name.EndsWith(".OGG", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.EndsWith(".WAV", StringComparison.OrdinalIgnoreCase)) return null;
        var dir = a.Archive.EndsWith(".ALF", StringComparison.OrdinalIgnoreCase)
            ? a.Archive[..^4] : a.Archive;                       // "DATA3.ALF" -> "DATA3"
        var audio = Path.Combine(Paths.Extracted, dir, a.Name);
        return File.Exists(audio) ? audio : null;
    }
}

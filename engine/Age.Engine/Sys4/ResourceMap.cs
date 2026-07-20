namespace Age.Engine.Sys4;

/// <summary>
/// Compatibility facade over the runtime SYS4 catalog. SYS4INI's file list is sectioned (one per scene: SCxxxx.BIN + its
/// cross-archive asset manifest); file_number is the index within a section. So a bytecode
/// resId resolves as files[section_base(scene) + resId] -- unified for graphics and audio.
/// See docs/asset-resolution-re.md.
/// </summary>
public sealed class ResourceMap
{
    private readonly Sys4AssetCatalog _catalog;
    private readonly IAssetStore _store;

    public ResourceMap(Sys4AssetCatalog catalog, IAssetStore? store = null)
    {
        _catalog = catalog;
        _store = store ?? new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
    }

    public static ResourceMap Load() => new(Sys4AssetCatalog.Load(Paths.Sys4Ini));

    /// <summary>Resolve a scene-local resId to its asset, or null if out of range / unknown scene.</summary>
    public AssetEntry? Resolve(string scene, long resId)
    {
        return _catalog.ResolveScene(scene, resId);
    }

    /// <summary>Resolve graphics normally through the scene manifest, with the universal raw-id
    /// fallback used by SYSTEM4-owned assets such as SO001.</summary>
    public AssetEntry? ResolveTexture(string scene, long resId)
    {
        var entry = _catalog.ResolveScene(scene, resId) ?? _catalog.ResolveRaw(resId);
        return entry is { IsPlaceholder: false } &&
               entry.Name.EndsWith(".AGF", StringComparison.OrdinalIgnoreCase) ? entry : null;
    }

    /// <summary>Resolve an already-normalized raw catalog id without applying a scene section base.</summary>
    public AssetEntry? ResolveRawTexture(long rawId)
    {
        var entry = _catalog.ResolveRaw(rawId);
        return entry is { IsPlaceholder: false } &&
               entry.Name.EndsWith(".AGF", StringComparison.OrdinalIgnoreCase) ? entry : null;
    }

    /// <summary>Decode an AGF directly from loose-first VFS bytes.</summary>
    public RgbaImage DecodeTexture(AssetEntry entry) => AgfDecoder.Decode(_store, entry);

    /// <summary>Resolve a native packed raw id to one of AGE's Windows cursor resources.</summary>
    public AssetEntry? ResolveCursor(long resourceId)
    {
        var entry = _catalog.ResolvePacked(resourceId);
        return entry is { IsPlaceholder: false }
            && entry.Name.EndsWith(".CUR", StringComparison.OrdinalIgnoreCase) ? entry : null;
    }

    public CursorImage DecodeCursor(AssetEntry entry)
    {
        if (!entry.Name.EndsWith(".CUR", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"not a CUR asset: {entry.Name}");
        return CurDecoder.Decode(_store.ReadAll(entry), entry.Name);
    }

    public AssetEntry? ResolveName(string name) => _catalog.ResolveName(name);

    /// <summary>
    /// Resolve a BGM id to its catalog entry. BGM is addressed by DIRECT LITERAL NAME (BGM{id:D3}.OGG), NOT the
    /// per-scene section manifest that voices/textures use. Confirmed by ear (play-bgm 5->BGM005, 8->BGM008)
    /// and by the play-bgm 0x23->BGM035 case: BGM035 is a real standalone track (the BGM set skips 030-034),
    /// which the manifest mis-resolved to a graphics entry. See docs/asset-resolution-re.md.
    /// </summary>
    public AssetEntry? ResolveBgm(long id)
    {
        var name = $"BGM{id:D3}.OGG";
        var entry = _catalog.ResolveName(name);
        return entry is { IsPlaceholder: false } && IsAudio(entry) ? entry : null;
    }

    /// <summary>Read a catalog-resolved OGG/WAV payload through the loose-first ALF/AAI byte store.</summary>
    public AudioPayload ReadAudio(AssetEntry entry)
    {
        if (entry.IsPlaceholder || !IsAudio(entry))
            throw new InvalidDataException($"not an OGG/WAV asset: {entry.Name}");
        return new AudioPayload(entry.Name, _store.ReadAll(entry));
    }

    /// <summary>Read a catalog-resolved MPEG program-stream movie through the same loose-first VFS as
    /// scripts, graphics, and audio. AGE uses an .AGF basename for these payloads; the MPEG pack start
    /// code, rather than the extension, distinguishes them from still-image AGF.</summary>
    public MoviePayload ReadMovie(AssetEntry entry)
    {
        if (entry.IsPlaceholder)
            throw new InvalidDataException($"placeholder movie asset: {entry.Name}");
        byte[] bytes = _store.ReadAll(entry);
        if (bytes.Length < 4 || bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 1 || bytes[3] != 0xba)
            throw new InvalidDataException($"not an MPEG program stream: {entry.Name}");
        return new MoviePayload(entry.Name, bytes);
    }

    private static bool IsAudio(AssetEntry entry)
        => entry.Name.EndsWith(".OGG", StringComparison.OrdinalIgnoreCase)
        || entry.Name.EndsWith(".WAV", StringComparison.OrdinalIgnoreCase);
}

public sealed record AudioPayload(string Name, byte[] Bytes);
public sealed record MoviePayload(string Name, byte[] Bytes);

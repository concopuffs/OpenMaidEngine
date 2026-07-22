namespace Age.Engine.Sys4;

/// <summary>
/// Typed facade over the runtime SYS4 catalog. Graphics, voice, movie, SFX, and cursor operands use
/// universal packed ids; BGM uses direct names.
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

    /// <summary>Resolve a universal packed SYS4INI/AAI id to an AGF texture record.</summary>
    public AssetEntry? ResolveTexture(long resourceId)
    {
        var entry = _catalog.ResolvePacked(resourceId);
        return entry is { IsPlaceholder: false } &&
               entry.Name.EndsWith(".AGF", StringComparison.OrdinalIgnoreCase) ? entry : null;
    }

    /// <summary>Resolve a universal packed SYS4INI/AAI id to a voice audio record.</summary>
    public AssetEntry? ResolveVoice(long resourceId)
    {
        var entry = _catalog.ResolvePacked(resourceId);
        return entry is { IsPlaceholder: false } && IsAudio(entry) ? entry : null;
    }

    /// <summary>Resolve a universal packed SYS4INI/AAI id to an AGF-named movie record. ReadMovie
    /// validates the MPEG signature because still images use the same extension.</summary>
    public AssetEntry? ResolveMovie(long resourceId)
    {
        var entry = _catalog.ResolvePacked(resourceId);
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
    /// universal packed resource table used by voices/textures. Confirmed by ear (play-bgm 5->BGM005, 8->BGM008)
    /// and by the play-bgm 0x23->BGM035 case: BGM035 is a real standalone track (the BGM set skips 030-034),
    /// which the manifest mis-resolved to a graphics entry. See docs/asset-resolution-re.md.
    /// </summary>
    public AssetEntry? ResolveBgm(long id)
    {
        var name = $"BGM{id:D3}.OGG";
        var entry = _catalog.ResolveName(name);
        return entry is { IsPlaceholder: false } && IsAudio(entry) ? entry : null;
    }

    /// <summary>Resolve opcode 0xb4's universal packed SYS4INI/AAI id to an audio entry.</summary>
    public AssetEntry? ResolveSoundEffect(long packedRawId)
    {
        var entry = _catalog.ResolvePacked(packedRawId);
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

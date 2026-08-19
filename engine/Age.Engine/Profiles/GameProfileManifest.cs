using System.Collections.Immutable;
using System.Text;

namespace Age.Engine.Profiles;

/// <summary>The minimal install identity available before a game frontend or VM is constructed.</summary>
public sealed record GameCatalogIdentity
{
    public GameCatalogIdentity(string catalogRevision, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        CatalogRevision = catalogRevision.TrimEnd('\0', ' ');
        Title = title.TrimEnd('\0');
    }

    public string CatalogRevision { get; }
    public string Title { get; }

    public string Display => $"{CatalogRevision} / {Title}";

    public bool Matches(GameCatalogIdentity other)
        => string.Equals(CatalogRevision, other.CatalogRevision, StringComparison.Ordinal)
           && string.Equals(Title, other.Title, StringComparison.Ordinal);

    /// <summary>Read only the fixed SYS4INI identity header; no directory decompression occurs.</summary>
    public static GameCatalogIdentity ReadSys4IniHeader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        const int magicSize = 8;
        const int titleSize = 256;
        var header = new byte[magicSize + titleSize];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int total = 0;
            while (total < header.Length)
            {
                int read = stream.Read(header, total, header.Length - total);
                if (read == 0) break;
                total += read;
            }
            if (total < header.Length)
                throw new InvalidDataException(
                    $"{Path.GetFileName(path)}: catalog identity header is truncated");
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp932 = Encoding.GetEncoding(932);
        return new GameCatalogIdentity(
            DecodeFixed(header.AsSpan(0, magicSize), Encoding.ASCII),
            DecodeFixed(header.AsSpan(magicSize, titleSize), cp932));
    }

    private static string DecodeFixed(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        int zero = bytes.IndexOf((byte)0);
        if (zero >= 0) bytes = bytes[..zero];
        return encoding.GetString(bytes);
    }
}

/// <summary>
/// Authored, content-free description of one supported game. Opcode semantics belong to the selected
/// engine ABI snapshot/layers, not to this game manifest.
/// </summary>
public sealed record GameProfileManifest
{
    public GameProfileManifest(
        string id,
        string displayTitle,
        IEnumerable<GameCatalogIdentity> catalogIdentities,
        IEnumerable<string> scriptRevisions,
        string sysFrontendId,
        string engineAbiId,
        string naturalBootScript,
        IReadOnlyDictionary<string, string>? metadataReferences,
        string persistenceNamespace,
        bool persistenceWritesEnabled)
    {
        RequireStableId(id, nameof(id));
        RequireStableId(persistenceNamespace, nameof(persistenceNamespace));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(sysFrontendId);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineAbiId);
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalBootScript);
        ArgumentNullException.ThrowIfNull(catalogIdentities);
        ArgumentNullException.ThrowIfNull(scriptRevisions);

        ImmutableArray<GameCatalogIdentity> identities = catalogIdentities.ToImmutableArray();
        ImmutableArray<string> revisions = scriptRevisions.ToImmutableArray();
        if (identities.IsDefaultOrEmpty)
            throw new ArgumentException("a profile requires at least one catalog identity", nameof(catalogIdentities));
        if (revisions.IsDefaultOrEmpty || revisions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("a profile requires at least one script revision", nameof(scriptRevisions));
        if (Path.GetFileName(naturalBootScript) != naturalBootScript
            || !naturalBootScript.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("natural boot script must be a .BIN file name", nameof(naturalBootScript));

        Id = id;
        DisplayTitle = displayTitle;
        CatalogIdentities = identities;
        ScriptRevisions = revisions;
        SysFrontendId = sysFrontendId;
        EngineAbiId = engineAbiId;
        NaturalBootScript = naturalBootScript;
        MetadataReferences = (metadataReferences ?? new Dictionary<string, string>())
            .ToImmutableDictionary(StringComparer.Ordinal);
        PersistenceNamespace = persistenceNamespace;
        PersistenceWritesEnabled = persistenceWritesEnabled;
    }

    public string Id { get; }
    public string DisplayTitle { get; }
    public ImmutableArray<GameCatalogIdentity> CatalogIdentities { get; }
    public ImmutableArray<string> ScriptRevisions { get; }
    public string SysFrontendId { get; }
    public string EngineAbiId { get; }
    public string NaturalBootScript { get; }
    public ImmutableDictionary<string, string> MetadataReferences { get; }
    public string PersistenceNamespace { get; }
    public bool PersistenceWritesEnabled { get; }

    public bool Matches(GameCatalogIdentity identity)
        => CatalogIdentities.Any(expected => expected.Matches(identity));

    private static void RequireStableId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!value.All(character => character is >= 'a' and <= 'z'
                                   or >= '0' and <= '9' or '-'))
            throw new ArgumentException(
                "profile ids and persistence namespaces use lowercase ASCII letters, digits, and '-'",
                parameterName);
    }
}

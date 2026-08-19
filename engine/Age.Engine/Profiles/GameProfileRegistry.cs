using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Age.Engine.Persistence;

namespace Age.Engine.Profiles;

/// <summary>The supported content-free profiles embedded in the runtime assembly.</summary>
public sealed class GameProfileRegistry
{
    private const string ResourcePrefix = "Age.Engine.Profiles.";
    private static readonly Lazy<GameProfileRegistry> BuiltInLazy = new(LoadBuiltIn);
    private readonly ImmutableArray<GameProfileManifest> _profiles;

    public GameProfileRegistry(IEnumerable<GameProfileManifest> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles.OrderBy(profile => profile.Id, StringComparer.Ordinal).ToImmutableArray();
        if (_profiles.IsDefaultOrEmpty)
            throw new ArgumentException("a profile registry cannot be empty", nameof(profiles));
        string[] duplicateIds = _profiles.GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateIds.Length != 0)
            throw new ArgumentException(
                $"duplicate profile ids: {string.Join(", ", duplicateIds)}", nameof(profiles));
    }

    public static GameProfileRegistry BuiltIn => BuiltInLazy.Value;
    public ImmutableArray<GameProfileManifest> Profiles => _profiles;

    public GameProfileManifest? Find(string id)
        => _profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));

    private static GameProfileRegistry LoadBuiltIn()
    {
        Assembly assembly = typeof(GameProfileRegistry).Assembly;
        string[] names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                           && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (names.Length == 0)
            throw new InvalidOperationException("no embedded game profile manifests were found");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        var profiles = new List<GameProfileManifest>(names.Length);
        foreach (string name in names)
        {
            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"embedded profile resource is missing: {name}");
            ProfileDocument document = JsonSerializer.Deserialize<ProfileDocument>(stream, options)
                ?? throw new InvalidDataException($"{name}: empty profile manifest");
            try
            {
                profiles.Add(document.ToManifest());
            }
            catch (Exception error) when (
                error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
            {
                throw new InvalidDataException($"{name}: invalid profile manifest: {error.Message}", error);
            }
        }
        return new GameProfileRegistry(profiles);
    }

    private sealed class ProfileDocument
    {
        public string? Id { get; init; }
        public string? DisplayTitle { get; init; }
        public CatalogIdentityDocument[]? CatalogIdentities { get; init; }
        public string[]? ScriptRevisions { get; init; }
        public string? SysFrontendId { get; init; }
        public string? EngineAbiId { get; init; }
        public string? NaturalBootScript { get; init; }
        public Dictionary<string, string>? MetadataReferences { get; init; }
        public PersistenceDocument? Persistence { get; init; }
        public RuntimeDocument? Runtime { get; init; }
        public DirectSceneDiagnosticDocument? DirectSceneDiagnostics { get; init; }
        public DebugSceneLaunchDocument? DebugSceneLaunch { get; init; }

        public GameProfileManifest ToManifest()
        {
            return new GameProfileManifest(
                Id ?? "",
                DisplayTitle ?? "",
                (CatalogIdentities ?? []).Select(identity => identity.ToIdentity()),
                ScriptRevisions ?? [],
                SysFrontendId ?? "",
                EngineAbiId ?? "",
                NaturalBootScript ?? "",
                MetadataReferences,
                (Persistence ?? throw new InvalidOperationException("persistence is required"))
                    .ToPolicy(),
                Runtime?.ToPolicy(),
                DirectSceneDiagnostics?.ToPolicy(),
                DebugSceneLaunch?.ToPolicy());
        }
    }

    private sealed class RuntimeDocument
    {
        public CellSeedDocument[]? ExternalGlobalSeeds { get; init; }
        public string? SceneEntryCoroutineGateAddress { get; init; }

        public GameRuntimePolicy ToPolicy()
            => new(
                (ExternalGlobalSeeds ?? []).Select(seed => seed.ToSeed()),
                SceneEntryCoroutineGateAddress == null
                    ? null : ParseAddress(SceneEntryCoroutineGateAddress));
    }

    private sealed class DirectSceneDiagnosticDocument
    {
        public string? SystemScript { get; init; }
        public string[]? DataBootstrapScripts { get; init; }
        public string[]? DataTableBootstrapScripts { get; init; }
        public CellSeedDocument[]? GlobalSeeds { get; init; }
        public SceneCellSeedDocument[]? SceneExternalGlobalSeeds { get; init; }
        public SurfaceBootstrapDocument[]? InheritedSurfaces { get; init; }
        public WaitIndicatorDocument? WaitIndicator { get; init; }

        public DirectSceneDiagnosticPolicy ToPolicy()
            => new(
                SystemScript ?? "",
                DataBootstrapScripts,
                DataTableBootstrapScripts,
                (GlobalSeeds ?? []).Select(seed => seed.ToSeed()),
                (SceneExternalGlobalSeeds ?? []).Select(seed => seed.ToSeed()),
                (InheritedSurfaces ?? []).Select(surface => surface.ToBootstrap()),
                WaitIndicator?.ToBootstrap());
    }

    private sealed class DebugSceneLaunchDocument
    {
        public string? RootScript { get; init; }
        public string? CoordinatorScript { get; init; }
        public string? PackedScriptIdAddress { get; init; }
        public CellSeedDocument[]? CoordinatorWrites { get; init; }

        public DebugSceneLaunchPolicy ToPolicy()
            => new(
                RootScript ?? "",
                CoordinatorScript ?? "",
                ParseAddress(PackedScriptIdAddress),
                (CoordinatorWrites ?? []).Select(seed => seed.ToSeed()));
    }

    private sealed class CellSeedDocument
    {
        public string? Address { get; init; }
        public long Value { get; init; }

        public ProfileCellSeed ToSeed() => new(ParseAddress(Address), Value);
    }

    private sealed class SceneCellSeedDocument
    {
        public string? Script { get; init; }
        public string? Address { get; init; }
        public long Value { get; init; }

        public ProfileSceneCellSeed ToSeed()
            => new(Script ?? "", ParseAddress(Address), Value);
    }

    private sealed class SurfaceBootstrapDocument
    {
        public string? Resource { get; init; }
        public int SurfaceSlot { get; init; }
        public uint Flags { get; init; }

        public ProfileSurfaceBootstrap ToBootstrap()
            => new(Resource ?? "", SurfaceSlot, Flags);
    }

    private sealed class WaitIndicatorDocument
    {
        public int TextLayout { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int SurfaceSlot { get; init; }
        public int SourceX { get; init; }
        public int SourceY { get; init; }
        public int FrameWidth { get; init; }
        public int FrameHeight { get; init; }
        public int FrameCount { get; init; }
        public int FrameMilliseconds { get; init; }

        public ProfileWaitIndicatorBootstrap ToBootstrap()
            => new(TextLayout, X, Y, SurfaceSlot, SourceX, SourceY,
                   FrameWidth, FrameHeight, FrameCount, FrameMilliseconds);
    }

    private static int ParseAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("profile seed address is required");
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(value[2..], 16)
            : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class PersistenceDocument
    {
        public string? StorageNamespace { get; init; }
        public bool? WritesEnabled { get; init; }
        public string? ReadOnlyReason { get; init; }
        public string? Magic { get; init; }
        public uint? SharedCompatibilityId { get; init; }
        public uint? NumberedCompatibilityId { get; init; }
        public string? GameId { get; init; }
        public int? ExpectedPackedSaveVersion { get; init; }
        public BankDimensionsDocument? BankDimensions { get; init; }
        public int? NumberedGfxRecordSize { get; init; }

        public GamePersistencePolicy ToPolicy()
        {
            if (WritesEnabled == null) throw new InvalidOperationException("writesEnabled is required");
            if (!Enum.TryParse(Magic, ignoreCase: true, out NativeSaveMagic magic))
                throw new InvalidOperationException($"unknown native save magic '{Magic}'");
            return new GamePersistencePolicy(
                StorageNamespace ?? "",
                WritesEnabled.Value,
                ReadOnlyReason,
                magic,
                SharedCompatibilityId
                    ?? throw new InvalidOperationException("sharedCompatibilityId is required"),
                NumberedCompatibilityId
                    ?? throw new InvalidOperationException("numberedCompatibilityId is required"),
                GameId ?? "",
                ExpectedPackedSaveVersion
                    ?? throw new InvalidOperationException("expectedPackedSaveVersion is required"),
                (BankDimensions
                    ?? throw new InvalidOperationException("bankDimensions is required")).ToDimensions(),
                NumberedGfxRecordSize
                    ?? throw new InvalidOperationException("numberedGfxRecordSize is required"));
        }
    }

    private sealed class BankDimensionsDocument
    {
        public int IntegerGlobals { get; init; }
        public int FloatGlobals { get; init; }
        public int StringGlobals { get; init; }
        public int PointerGlobals { get; init; }
        public int PointerStrings { get; init; }
        public int LocalPointerScratch { get; init; }

        public NativeSaveBankDimensions ToDimensions()
            => new(
                IntegerGlobals, FloatGlobals, StringGlobals,
                PointerGlobals, PointerStrings, LocalPointerScratch);
    }

    private sealed class CatalogIdentityDocument
    {
        public string? CatalogRevision { get; init; }
        public string? Title { get; init; }

        public GameCatalogIdentity ToIdentity()
            => new(CatalogRevision ?? "", Title ?? "");
    }
}

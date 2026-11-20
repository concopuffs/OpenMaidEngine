using System.Globalization;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;

namespace Ome.Engine.Profiles;

/// <summary>
/// Authored native-persistence facts of a game that cannot yet be derived from the common SYS4 catalog. The
/// packed save version is still read from SYS4INI and checked against the observed profile value. The engine's own
/// save facts (its version stamp and numbered-save record size) come from the engine build's <see cref="SaveRules"/>.
/// </summary>
public sealed record GamePersistencePolicy
{
    public GamePersistencePolicy(
        string storageNamespace,
        bool writesEnabled,
        string? readOnlyReason,
        NativeSaveMagic magic,
        string gameId,
        int expectedPackedSaveVersion,
        NativeSaveBankDimensions bankDimensions,
        IReadOnlyList<string>? legacyGameIds = null)
    {
        GameProfileManifest.RequireStableId(storageNamespace, nameof(storageNamespace));
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(bankDimensions);
        if (expectedPackedSaveVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPackedSaveVersion));
        if (!writesEnabled && string.IsNullOrWhiteSpace(readOnlyReason))
            throw new ArgumentException(
                "a read-only persistence policy requires a reason", nameof(readOnlyReason));

        StorageNamespace = storageNamespace;
        WritesEnabled = writesEnabled;
        ReadOnlyReason = writesEnabled ? null : readOnlyReason;
        Magic = magic;
        GameId = gameId;
        LegacyGameIds = legacyGameIds?.ToArray() ?? [];
        ExpectedPackedSaveVersion = expectedPackedSaveVersion;
        BankDimensions = bankDimensions;
    }

    public string StorageNamespace { get; }
    public bool WritesEnabled { get; }
    public string? ReadOnlyReason { get; }
    public NativeSaveMagic Magic { get; }
    public string GameId { get; }
    /// <summary>Game ids that earlier port builds wrote, accepted on read only.</summary>
    public IReadOnlyList<string> LegacyGameIds { get; }
    /// <summary>SYS4INI's SAVEVERSION for this game (310, 320): the game's own value, which AGE reads from SYS4INI and
    /// splits into set:SaveVersion1/2 (docs/engine-re.md, "SYS4INI startup-settings catalog"); checked here against the
    /// install.</summary>
    public int ExpectedPackedSaveVersion { get; }
    public NativeSaveBankDimensions BankDimensions { get; }

    public NativeSaveIdentity ResolveNativeIdentity(Sys4StartupSettings startupSettings, SaveRules engineSaves)
    {
        ArgumentNullException.ThrowIfNull(startupSettings);
        ArgumentNullException.ThrowIfNull(engineSaves);
        string? raw = startupSettings.GetValueOrDefault("SAVEVERSION");
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int packed)
            || packed < 0)
            throw new InvalidDataException(
                $"SYS4INI SAVEVERSION '{raw ?? "<missing>"}' is not a non-negative integer");
        if (packed != ExpectedPackedSaveVersion)
            throw new InvalidDataException(
                $"SYS4INI SAVEVERSION {packed} does not match profile value " +
                $"{ExpectedPackedSaveVersion}");

        return CreateNativeIdentity(packed, engineSaves);
    }

    public NativeSaveIdentity CreateExpectedNativeIdentity(SaveRules engineSaves)
        => CreateNativeIdentity(
            ExpectedPackedSaveVersion, engineSaves ?? throw new ArgumentNullException(nameof(engineSaves)));

    private NativeSaveIdentity CreateNativeIdentity(int packed, SaveRules engineSaves)
        => new(
            Magic,
            engineSaves.CompatibilityId,
            GameId,
            SaveVersion1: packed / 100,
            SaveVersion2: packed % 100,
            BankDimensions: BankDimensions,
            NumberedGfxRecordSize: engineSaves.NumberedGfxRecordSize,
            LegacyGameIds: LegacyGameIds,
            NumberedSaveKeepsFloats: engineSaves.KeepsFloatSections(packed % 100));
}

/// <summary>All Godot-owned writable locations and native persistence policy for one selection.</summary>
public sealed record ResolvedGamePersistence(
    string ProfileRoot,
    string DiagnosticsDirectory,
    Sys4PersistencePaths NativePaths,
    NativeSaveIdentity NativeIdentity,
    bool WritesEnabled,
    string? ReadOnlyReason)
{
    /// <param name="profileRootOverride">An explicit profile root (the frontend's <c>--data-root</c>). It replaces
    /// <c>&lt;user data&gt;/games/&lt;namespace&gt;</c> and keeps the native layout beneath it, so a driver sandbox
    /// can hold one game's saves and seed the other's.</param>
    /// <param name="engineSaves">The save rules of the engine build the profile runs.</param>
    public static ResolvedGamePersistence Resolve(
        SelectedGameProfile selected,
        SaveRules engineSaves,
        Sys4StartupSettings startupSettings,
        string godotUserDataRoot,
        string? profileRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(engineSaves);
        ArgumentNullException.ThrowIfNull(startupSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(godotUserDataRoot);

        GamePersistencePolicy policy = selected.Profile.Persistence;
        string profileRoot;
        if (profileRootOverride != null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profileRootOverride);
            profileRoot = Path.GetFullPath(profileRootOverride);
        }
        else
        {
            string userRoot = Path.GetFullPath(godotUserDataRoot);
            profileRoot = Path.GetFullPath(Path.Combine(
                userRoot, "games", policy.StorageNamespace));
            string relativeProfileRoot = Path.GetRelativePath(userRoot, profileRoot);
            if (Path.IsPathRooted(relativeProfileRoot)
                || relativeProfileRoot == ".."
                || relativeProfileRoot.StartsWith(
                    ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("profile persistence root escaped the Godot user-data root");
        }

        NativeSaveIdentity identity = policy.CreateExpectedNativeIdentity(engineSaves);
        string? contractMismatch = null;
        if (selected.IdentityMatched)
        {
            try
            {
                identity = policy.ResolveNativeIdentity(startupSettings, engineSaves);
            }
            catch (InvalidDataException error)
            {
                contractMismatch = $"persistence contract mismatch: {error.Message}";
            }
        }
        bool writesEnabled = selected.PersistenceWritesEnabled && contractMismatch == null;
        string? readOnlyReason = writesEnabled ? null
            : contractMismatch == null ? selected.ReadOnlyReason
            : selected.ReadOnlyReason == null ? contractMismatch
            : $"{selected.ReadOnlyReason}; {contractMismatch}";
        return new ResolvedGamePersistence(
            profileRoot,
            Path.Combine(profileRoot, "diagnostics"),
            Sys4PersistencePaths.ResolveProfileOverride(startupSettings, profileRoot),
            identity,
            writesEnabled,
            readOnlyReason);
    }

    public INativeDatStore? CreateNativeDatStore()
        => WritesEnabled
            ? new DirectoryNativeDatStore(NativePaths.SaveDirectory, NativeIdentity)
            : null;
}

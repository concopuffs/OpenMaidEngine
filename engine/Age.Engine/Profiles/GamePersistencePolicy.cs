using System.Globalization;
using Age.Engine.Persistence;
using Age.Engine.Sys4;

namespace Age.Engine.Profiles;

/// <summary>
/// Authored native-persistence facts that cannot yet be derived from the common SYS4 catalog. The
/// packed save version is still read from SYS4INI and checked against the observed profile value.
/// </summary>
public sealed record GamePersistencePolicy
{
    public GamePersistencePolicy(
        string storageNamespace,
        bool writesEnabled,
        string? readOnlyReason,
        NativeSaveMagic magic,
        uint sharedCompatibilityId,
        uint numberedCompatibilityId,
        string gameId,
        int expectedPackedSaveVersion,
        NativeSaveBankDimensions bankDimensions,
        int numberedGfxRecordSize)
    {
        GameProfileManifest.RequireStableId(storageNamespace, nameof(storageNamespace));
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(bankDimensions);
        if (expectedPackedSaveVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPackedSaveVersion));
        if (numberedGfxRecordSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(numberedGfxRecordSize));
        if (!writesEnabled && string.IsNullOrWhiteSpace(readOnlyReason))
            throw new ArgumentException(
                "a read-only persistence policy requires a reason", nameof(readOnlyReason));
        if (writesEnabled && numberedGfxRecordSize != NativeNumberedSaveState.GfxRecordSize)
            throw new ArgumentException(
                $"write-enabled policy uses unsupported numbered-save gfx record size " +
                $"0x{numberedGfxRecordSize:x}", nameof(numberedGfxRecordSize));

        StorageNamespace = storageNamespace;
        WritesEnabled = writesEnabled;
        ReadOnlyReason = writesEnabled ? null : readOnlyReason;
        Magic = magic;
        SharedCompatibilityId = sharedCompatibilityId;
        NumberedCompatibilityId = numberedCompatibilityId;
        GameId = gameId;
        ExpectedPackedSaveVersion = expectedPackedSaveVersion;
        BankDimensions = bankDimensions;
        NumberedGfxRecordSize = numberedGfxRecordSize;
    }

    public string StorageNamespace { get; }
    public bool WritesEnabled { get; }
    public string? ReadOnlyReason { get; }
    public NativeSaveMagic Magic { get; }
    public uint SharedCompatibilityId { get; }
    public uint NumberedCompatibilityId { get; }
    public string GameId { get; }
    public int ExpectedPackedSaveVersion { get; }
    public NativeSaveBankDimensions BankDimensions { get; }
    public int NumberedGfxRecordSize { get; }

    public NativeSaveIdentity ResolveNativeIdentity(Sys4StartupSettings startupSettings)
    {
        ArgumentNullException.ThrowIfNull(startupSettings);
        string? raw = startupSettings.GetValueOrDefault("SAVEVERSION");
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int packed)
            || packed < 0)
            throw new InvalidDataException(
                $"SYS4INI SAVEVERSION '{raw ?? "<missing>"}' is not a non-negative integer");
        if (packed != ExpectedPackedSaveVersion)
            throw new InvalidDataException(
                $"SYS4INI SAVEVERSION {packed} does not match profile value " +
                $"{ExpectedPackedSaveVersion}");

        return CreateNativeIdentity(packed);
    }

    public NativeSaveIdentity CreateExpectedNativeIdentity()
        => CreateNativeIdentity(ExpectedPackedSaveVersion);

    private NativeSaveIdentity CreateNativeIdentity(int packed)
        => new(
            Magic,
            SharedCompatibilityId,
            GameId,
            SaveVersion1: packed / 100,
            SaveVersion2: packed % 100,
            NumberedCompatibilityId: NumberedCompatibilityId,
            BankDimensions: BankDimensions);
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
    public static ResolvedGamePersistence Resolve(
        SelectedGameProfile selected,
        Sys4StartupSettings startupSettings,
        string godotUserDataRoot)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(startupSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(godotUserDataRoot);

        GamePersistencePolicy policy = selected.Profile.Persistence;
        string userRoot = Path.GetFullPath(godotUserDataRoot);
        string profileRoot = Path.GetFullPath(Path.Combine(
            userRoot, "games", policy.StorageNamespace));
        string relativeProfileRoot = Path.GetRelativePath(userRoot, profileRoot);
        if (Path.IsPathRooted(relativeProfileRoot)
            || relativeProfileRoot == ".."
            || relativeProfileRoot.StartsWith(
                ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("profile persistence root escaped the Godot user-data root");

        NativeSaveIdentity identity = policy.CreateExpectedNativeIdentity();
        string? contractMismatch = null;
        if (selected.IdentityMatched)
        {
            try
            {
                identity = policy.ResolveNativeIdentity(startupSettings);
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

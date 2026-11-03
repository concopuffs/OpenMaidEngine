using System.Globalization;

namespace Age.Engine.Persistence;

/// <summary>What AGE writes into, and checks in, the headers of SAVE.DAT, RT.DAT and numbered saves.</summary>
/// <param name="CompatibilityId">Header +0x04 of every file: the writing AGE.EXE's four-character engine version
/// (<c>ctx+0x53b74</c>; Himegari's current exe writes "424J" = 0x4a343234, its 2019 saves carry "422B"). AGE writes
/// it into every file and never checks it on read, so files from other engine builds load; the port does the same.</param>
/// <param name="LegacyGameIds">Further game ids accepted on read but never written: misspellings that earlier port
/// builds wrote, so their files load once and are rewritten with <paramref name="GameId"/>.</param>
public sealed record NativeSaveIdentity(
    NativeSaveMagic Magic,
    uint CompatibilityId,
    string GameId,
    int SaveVersion1,
    int SaveVersion2,
    NativeSaveBankDimensions? BankDimensions = null,
    int NumberedGfxRecordSize = NativeGfxSaveLayout.BaseRecordSize,
    IReadOnlyList<string>? LegacyGameIds = null)
{
    public NativeSaveMetadata CreateMetadata(NativeSystemTime timestamp, uint accumulatedPlaySeconds)
        => new(Magic, CompatibilityId, GameId, timestamp, accumulatedPlaySeconds, SaveVersion1, SaveVersion2);

    /// <summary>AGE's read check: the game id must equal its own (SYS4INI header +8).</summary>
    public bool AcceptsGameId(string gameId)
        => StringComparer.Ordinal.Equals(gameId, GameId)
           || (LegacyGameIds?.Contains(gameId, StringComparer.Ordinal) ?? false);

    /// <summary>AGE's header check (<c>save_container_read_and_decode</c>): magic, game id, and the save versions.
    /// The compatibility id is not checked.</summary>
    public void Validate(NativeSaveMetadata metadata)
    {
        if (metadata.Magic != Magic)
            throw new InvalidDataException($"Native save generation mismatch: expected {Magic}, got {metadata.Magic}.");
        if (!AcceptsGameId(metadata.GameId))
            throw new InvalidDataException("Native save game id mismatch.");

        bool versionsMatch =
            metadata.SaveVersion1 == SaveVersion1 &&
            metadata.SaveVersion2 == SaveVersion2;
        bool layoutTwoCompatibility =
            metadata.SaveVersion1 == 2 &&
            SaveVersion1 == 2;
        if (!versionsMatch && !layoutTwoCompatibility)
            throw new InvalidDataException(
                $"Native save version mismatch: expected {SaveVersion1}.{SaveVersion2}, " +
                $"got {metadata.SaveVersion1}.{metadata.SaveVersion2}.");
    }
}

public sealed record NativeNumberedSaveFile(NativeSaveDocument Document, byte[] HistoryTail);

public interface INativeDatStore
{
    NativeSaveIdentity Identity { get; }
    NativeSaveDocument? LoadShared();
    void SaveShared(ReadOnlySpan<byte> payload, NativeSystemTime timestamp, uint accumulatedPlaySeconds);
    ReadTextDatabaseSnapshot? LoadReadText();
    void SaveReadText(ReadTextDatabaseSnapshot snapshot);
    NativeSaveMetadata? QueryNumberedMetadata(int slot);
    NativeSaveDocument? LoadNumbered(int slot);
    void SaveNumbered(int slot, ReadOnlySpan<byte> payload, NativeSystemTime timestamp, uint accumulatedPlaySeconds);
    NativeNumberedSaveFile? LoadNumberedFile(int slot)
    {
        NativeSaveDocument? document = LoadNumbered(slot);
        return document == null ? null : new NativeNumberedSaveFile(document, []);
    }
    void SaveNumberedFile(
        int slot, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> historyTail,
        NativeSystemTime timestamp, uint accumulatedPlaySeconds)
        => SaveNumbered(slot, payload, timestamp, accumulatedPlaySeconds);
    int DeleteNumberedPair(int slot);
    int CopyNumberedPair(int sourceSlot, int destinationSlot);
    byte[]? LoadNumberedThumbnail(int slot);
    void SaveNumberedThumbnail(int slot, ReadOnlySpan<byte> data);

    /// <summary>
    /// Move a profile file set that failed to load (SAVE.DAT/SAVE.BAK or RT.DAT/RT.BAK) out of the way, so the
    /// next save cannot overwrite it. Returns the names it now has, or none when the store keeps no files.
    /// </summary>
    IReadOnlyList<string> SetAsideUnreadable(NativeProfileFile file) => [];
}

/// <summary>The shared profile's two files: SAVE.DAT (shared payload) and RT.DAT (read-text database).</summary>
public enum NativeProfileFile
{
    Shared,
    ReadText,
}

/// <summary>
/// Directory-backed native DAT lifecycle. Shared state uses $$SAVE.DAT -> SAVE.DAT with SAVE.BAK
/// fallback; numbered slots are written directly as SAVE##.DAT, matching AGE's separate behavior.
/// Payload ownership remains above this boundary.
/// </summary>
public sealed class DirectoryNativeDatStore : INativeDatStore
{
    public const string SharedFileName = "SAVE.DAT";
    public const string SharedTemporaryFileName = "$$SAVE.DAT";
    public const string SharedBackupFileName = "SAVE.BAK";
    public const string ReadTextFileName = "RT.DAT";
    public const string ReadTextTemporaryFileName = "$$RT.DAT";
    public const string ReadTextBackupFileName = "RT.BAK";

    private readonly string _root;
    private readonly NativeSaveIdentity _identity;
    public NativeSaveIdentity Identity => _identity;

    public DirectoryNativeDatStore(string root, NativeSaveIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _identity = identity;
    }

    public IReadOnlyList<string> SetAsideUnreadable(NativeProfileFile file)
    {
        string[] names = file == NativeProfileFile.Shared
            ? [SharedFileName, SharedBackupFileName]
            : [ReadTextFileName, ReadTextBackupFileName];
        string suffix = ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var moved = new List<string>();
        foreach (string name in names)
        {
            string path = Path.Combine(_root, name);
            if (!File.Exists(path)) continue;
            string target = path + suffix;
            File.Move(path, target);
            moved.Add(Path.GetFileName(target));
        }
        return moved;
    }

    public NativeSaveDocument? LoadShared()
    {
        string primary = Path.Combine(_root, SharedFileName);
        string backup = Path.Combine(_root, SharedBackupFileName);
        if (!File.Exists(primary))
            return File.Exists(backup) ? LoadAndValidate(backup) : null;

        try
        {
            return LoadAndValidate(primary);
        }
        catch (Exception primaryError) when (
            primaryError is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!File.Exists(backup)) throw;
            try
            {
                return LoadAndValidate(backup);
            }
            catch (Exception backupError) when (
                backupError is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new InvalidDataException(
                    "Both SAVE.DAT and SAVE.BAK failed native container validation.",
                    new AggregateException(primaryError, backupError));
            }
        }
    }

    public void SaveShared(
        ReadOnlySpan<byte> payload,
        NativeSystemTime timestamp,
        uint accumulatedPlaySeconds)
    {
        byte[] encoded = NativeSaveContainerCodec.Encode(
            payload, _identity.CreateMetadata(timestamp, accumulatedPlaySeconds));
        Directory.CreateDirectory(_root);

        string temporary = Path.Combine(_root, SharedTemporaryFileName);
        string primary = Path.Combine(_root, SharedFileName);
        string backup = Path.Combine(_root, SharedBackupFileName);
        WriteThrough(temporary, encoded);
        if (File.Exists(backup)) File.Delete(backup);
        if (File.Exists(primary)) File.Move(primary, backup);
        File.Move(temporary, primary);
    }

    public ReadTextDatabaseSnapshot? LoadReadText()
    {
        string path = Path.Combine(_root, ReadTextFileName);
        return File.Exists(path)
            ? ReadTextDatabaseCodec.Decode(File.ReadAllBytes(path), _identity)
            : null;
    }

    public void SaveReadText(ReadTextDatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        byte[] encoded = ReadTextDatabaseCodec.Encode(snapshot, _identity);
        Directory.CreateDirectory(_root);

        string temporary = Path.Combine(_root, ReadTextTemporaryFileName);
        string primary = Path.Combine(_root, ReadTextFileName);
        string backup = Path.Combine(_root, ReadTextBackupFileName);
        WriteThrough(temporary, encoded);
        if (File.Exists(backup)) File.Delete(backup);
        if (File.Exists(primary)) File.Move(primary, backup);
        File.Move(temporary, primary);
    }

    public NativeSaveDocument? LoadNumbered(int slot)
    {
        string path = Path.Combine(_root, NumberedFileName(slot));
        return File.Exists(path) ? LoadAndValidate(path) : null;
    }

    public NativeNumberedSaveFile? LoadNumberedFile(int slot)
    {
        string path = Path.Combine(_root, NumberedFileName(slot));
        if (!File.Exists(path)) return null;
        byte[] source = File.ReadAllBytes(path);
        NativeSaveDocument document = NativeSaveContainerCodec.Decode(source);
        _identity.Validate(document.Metadata);
        return new NativeNumberedSaveFile(
            document, source.AsSpan(document.BytesConsumed).ToArray());
    }

    public NativeSaveMetadata? QueryNumberedMetadata(int slot)
    {
        string path = Path.Combine(_root, NumberedFileName(slot));
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, NativeSaveContainerCodec.HeaderSize,
            FileOptions.SequentialScan);
        byte[] header = new byte[NativeSaveContainerCodec.HeaderSize];
        stream.ReadExactly(header);
        NativeSaveMetadata metadata = NativeSaveContainerCodec.ReadMetadata(header);
        _identity.Validate(metadata);
        return metadata;
    }

    public void SaveNumbered(
        int slot,
        ReadOnlySpan<byte> payload,
        NativeSystemTime timestamp,
        uint accumulatedPlaySeconds)
    {
        byte[] encoded = NativeSaveContainerCodec.Encode(
            payload, _identity.CreateMetadata(timestamp, accumulatedPlaySeconds));
        Directory.CreateDirectory(_root);
        WriteThrough(Path.Combine(_root, NumberedFileName(slot)), encoded);
    }

    public void SaveNumberedFile(
        int slot,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> historyTail,
        NativeSystemTime timestamp,
        uint accumulatedPlaySeconds)
    {
        byte[] container = NativeSaveContainerCodec.Encode(
            payload, _identity.CreateMetadata(timestamp, accumulatedPlaySeconds));
        byte[] encoded = new byte[checked(container.Length + historyTail.Length)];
        container.CopyTo(encoded, 0);
        historyTail.CopyTo(encoded.AsSpan(container.Length));
        Directory.CreateDirectory(_root);
        WriteThrough(Path.Combine(_root, NumberedFileName(slot)), encoded);
    }

    public int DeleteNumberedPair(int slot)
    {
        bool dataDeleted = TryDelete(Path.Combine(_root, NumberedFileName(slot)));
        bool thumbnailDeleted = TryDelete(Path.Combine(_root, NumberedThumbnailFileName(slot)));
        return !thumbnailDeleted ? 2 : !dataDeleted ? 1 : 0;
    }

    public int CopyNumberedPair(int sourceSlot, int destinationSlot)
    {
        Directory.CreateDirectory(_root);
        bool dataCopied = TryCopy(
            Path.Combine(_root, NumberedFileName(sourceSlot)),
            Path.Combine(_root, NumberedFileName(destinationSlot)));
        bool thumbnailCopied = TryCopy(
            Path.Combine(_root, NumberedThumbnailFileName(sourceSlot)),
            Path.Combine(_root, NumberedThumbnailFileName(destinationSlot)));
        return !thumbnailCopied ? 2 : !dataCopied ? 1 : 0;
    }

    public byte[]? LoadNumberedThumbnail(int slot)
    {
        string path = Path.Combine(_root, NumberedThumbnailFileName(slot));
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public void SaveNumberedThumbnail(int slot, ReadOnlySpan<byte> data)
    {
        Directory.CreateDirectory(_root);
        WriteThrough(Path.Combine(_root, NumberedThumbnailFileName(slot)), data);
    }

    public static string NumberedFileName(int slot)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        return "SAVE" + slot.ToString("00", CultureInfo.InvariantCulture) + ".DAT";
    }

    public static string NumberedThumbnailFileName(int slot)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        return "SAVE" + slot.ToString("00", CultureInfo.InvariantCulture) + ".STH";
    }

    private NativeSaveDocument LoadAndValidate(string path)
    {
        NativeSaveDocument document = NativeSaveContainerCodec.Decode(File.ReadAllBytes(path));
        _identity.Validate(document.Metadata);
        return document;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool TryCopy(string source, string destination)
    {
        try
        {
            File.Copy(source, destination, overwrite: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void WriteThrough(string path, ReadOnlySpan<byte> data)
    {
        using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }
}

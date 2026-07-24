using System.Globalization;

namespace Age.Engine.Persistence;

public sealed record NativeSaveIdentity(
    NativeSaveMagic Magic,
    uint CompatibilityId,
    string GameId,
    int SaveVersion1,
    int SaveVersion2)
{
    public NativeSaveMetadata CreateMetadata(NativeSystemTime timestamp, uint accumulatedPlaySeconds)
        => new(Magic, CompatibilityId, GameId, timestamp, accumulatedPlaySeconds, SaveVersion1, SaveVersion2);

    public void Validate(NativeSaveMetadata metadata)
    {
        if (metadata.Magic != Magic)
            throw new InvalidDataException($"Native save generation mismatch: expected {Magic}, got {metadata.Magic}.");
        if (metadata.CompatibilityId != CompatibilityId)
            throw new InvalidDataException("Native save compatibility id mismatch.");
        if (!StringComparer.Ordinal.Equals(metadata.GameId, GameId))
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

public interface INativeDatStore
{
    NativeSaveIdentity Identity { get; }
    NativeSaveDocument? LoadShared();
    void SaveShared(ReadOnlySpan<byte> payload, NativeSystemTime timestamp, uint accumulatedPlaySeconds);
    NativeSaveDocument? LoadNumbered(int slot);
    void SaveNumbered(int slot, ReadOnlySpan<byte> payload, NativeSystemTime timestamp, uint accumulatedPlaySeconds);
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

    private readonly string _root;
    private readonly NativeSaveIdentity _identity;
    public NativeSaveIdentity Identity => _identity;

    public DirectoryNativeDatStore(string root, NativeSaveIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _identity = identity;
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

    public NativeSaveDocument? LoadNumbered(int slot)
    {
        string path = Path.Combine(_root, NumberedFileName(slot));
        return File.Exists(path) ? LoadAndValidate(path) : null;
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

    public static string NumberedFileName(int slot)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        return "SAVE" + slot.ToString("00", CultureInfo.InvariantCulture) + ".DAT";
    }

    private NativeSaveDocument LoadAndValidate(string path)
    {
        NativeSaveDocument document = NativeSaveContainerCodec.Decode(File.ReadAllBytes(path));
        _identity.Validate(document.Metadata);
        return document;
    }

    private static void WriteThrough(string path, ReadOnlySpan<byte> data)
    {
        using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }
}

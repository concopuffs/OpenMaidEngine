namespace Ome.Engine.Persistence;

/// <summary>
/// Keeps a text sidecar beside each numbered save: the stack stamp (mod loader spec §8: <c>SAVEnn.mods.toml</c>) and the
/// mods' state (Lua scripting spec §11: <c>SAVEnn.modstate.json</c>), one store for each suffix. Saving a slot writes the
/// text the save gave for this suffix, or else <paramref name="currentSidecar"/>, and deletes a stale sidecar when there
/// is none, so a save made without mods carries no stamp. Delete and copy carry the sidecar with the pair. Loading a
/// slot hands its sidecar (or null) to <paramref name="loaded"/> and adds it to the file's
/// <see cref="NativeNumberedSaveFile.Sidecars"/>. Each file is written by its own replace, after the save: there is no
/// transaction. Native AGE never reads a sidecar, and the save files themselves are unchanged.
/// </summary>
public sealed class SidecarNativeDatStore : INativeDatStore
{
    private readonly INativeDatStore _inner;
    private readonly string _root;
    private readonly string _suffix;
    private readonly string? _current;
    private readonly Action<int, string?>? _loaded;

    public SidecarNativeDatStore(INativeDatStore inner, string saveDirectory, string suffix, string? currentSidecar,
                                 Action<int, string?>? loaded = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentException.ThrowIfNullOrWhiteSpace(saveDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        _root = Path.GetFullPath(saveDirectory);
        _suffix = suffix;
        _current = currentSidecar;
        _loaded = loaded;
    }

    /// <summary><c>SAVEnn</c> plus the suffix, such as <c>SAVE03.mods.toml</c>.</summary>
    public string SidecarFileName(int slot) => Path.ChangeExtension(DirectoryNativeDatStore.NumberedFileName(slot), null) + _suffix;

    private string SidecarPath(int slot) => Path.Combine(_root, SidecarFileName(slot));

    public NativeSaveIdentity Identity => _inner.Identity;
    public NativeSaveDocument? LoadShared() => _inner.LoadShared();
    public void SaveShared(ReadOnlySpan<byte> payload, NativeSystemTime timestamp, uint accumulatedPlaySeconds)
        => _inner.SaveShared(payload, timestamp, accumulatedPlaySeconds);
    public ReadTextDatabaseSnapshot? LoadReadText() => _inner.LoadReadText();
    public void SaveReadText(ReadTextDatabaseSnapshot snapshot) => _inner.SaveReadText(snapshot);
    public NativeSaveMetadata? QueryNumberedMetadata(int slot) => _inner.QueryNumberedMetadata(slot);
    public byte[]? LoadNumberedThumbnail(int slot) => _inner.LoadNumberedThumbnail(slot);
    public bool HasNumberedThumbnail(int slot) => _inner.HasNumberedThumbnail(slot);
    public void SaveNumberedThumbnail(int slot, ReadOnlySpan<byte> data) => _inner.SaveNumberedThumbnail(slot, data);
    public IReadOnlyList<string> SetAsideUnreadable(NativeProfileFile file) => _inner.SetAsideUnreadable(file);

    public NativeSaveDocument? LoadNumbered(int slot)
    {
        NativeSaveDocument? document = _inner.LoadNumbered(slot);
        if (document != null) _loaded?.Invoke(slot, ReadSidecar(slot));
        return document;
    }

    public NativeNumberedSaveFile? LoadNumberedFile(int slot)
    {
        NativeNumberedSaveFile? file = _inner.LoadNumberedFile(slot);
        if (file == null) return null;
        string? text = ReadSidecar(slot);
        _loaded?.Invoke(slot, text);
        if (text == null) return file;
        var sidecars = new Dictionary<string, string>(file.Sidecars, StringComparer.Ordinal) { [_suffix] = text };
        return file with { Sidecars = sidecars };
    }

    public void SaveNumbered(int slot, ReadOnlySpan<byte> payload, NativeSystemTime timestamp, uint accumulatedPlaySeconds)
    {
        _inner.SaveNumbered(slot, payload, timestamp, accumulatedPlaySeconds);
        WriteSidecar(slot, _current);
    }

    public void SaveNumberedFile(int slot, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> historyTail,
                                 NativeSystemTime timestamp, uint accumulatedPlaySeconds)
        => SaveNumberedFile(slot, payload, historyTail, timestamp, accumulatedPlaySeconds, NoTexts);

    public void SaveNumberedFile(int slot, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> historyTail,
                                 NativeSystemTime timestamp, uint accumulatedPlaySeconds,
                                 IReadOnlyDictionary<string, string?> sidecars)
    {
        _inner.SaveNumberedFile(slot, payload, historyTail, timestamp, accumulatedPlaySeconds, sidecars);
        WriteSidecar(slot, sidecars.TryGetValue(_suffix, out string? given) ? given : _current);
    }

    private static readonly IReadOnlyDictionary<string, string?> NoTexts = new Dictionary<string, string?>();

    public int DeleteNumberedPair(int slot)
    {
        int result = _inner.DeleteNumberedPair(slot);
        if (File.Exists(SidecarPath(slot))) File.Delete(SidecarPath(slot));
        return result;
    }

    public int CopyNumberedPair(int sourceSlot, int destinationSlot)
    {
        int result = _inner.CopyNumberedPair(sourceSlot, destinationSlot);
        string source = SidecarPath(sourceSlot), destination = SidecarPath(destinationSlot);
        if (File.Exists(source)) File.Copy(source, destination, overwrite: true);
        else if (File.Exists(destination)) File.Delete(destination);
        return result;
    }

    private string? ReadSidecar(int slot) => File.Exists(SidecarPath(slot)) ? File.ReadAllText(SidecarPath(slot)) : null;

    private void WriteSidecar(int slot, string? text)
    {
        string path = SidecarPath(slot);
        if (text == null)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(_root);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}

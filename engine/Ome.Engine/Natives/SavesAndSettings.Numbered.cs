using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

/// <summary>
/// What a numbered save holds besides the save native's own records: the natives that write and read theirs (script
/// memory's banks, audio's track and sound-effect channels, graphics' surfaces and objects), and the records of the
/// interpreter (the saved frames) and of text (the history tail and the source glyph objects), which are not natives
/// yet. The interpreter builds it once and hands it to each save and load, so the save native needs none of them to be
/// built first.
/// </summary>
/// <param name="captureFrames">The interpreter's saved frames, up to op 0x1ad's boundary; throws
/// <see cref="InvalidDataException"/> when no frame can be saved.</param>
/// <param name="sourceTextObjects">The source glyph objects of the presented text (localization spec §8).</param>
/// <param name="releaseSurfacesOnLoad">(CreateObject &amp; 2) &amp;&amp; (AutoFreeTex &amp; 2): a load releases every
/// surface before it restores the saved ones (docs/engine-re.md, "AutoFreeTex").</param>
public sealed class NumberedSaveRecords(
    ScriptMemory memory, Audio audio, Graphics graphics, AdvTextHistory history,
    Func<NativeSavedScriptFrame[]> captureFrames, Func<IReadOnlyList<AdvTextSourceObjects>> sourceTextObjects,
    bool releaseSurfacesOnLoad)
{
    public ScriptMemory Memory { get; } = memory;
    public Audio Audio { get; } = audio;
    public Graphics Graphics { get; } = graphics;
    public AdvTextHistory History { get; } = history;
    public bool ReleaseSurfacesOnLoad { get; } = releaseSurfacesOnLoad;
    public NativeSavedScriptFrame[] CaptureFrames() => captureFrames();
    public IReadOnlyList<AdvTextSourceObjects> SourceTextObjects() => sourceTextObjects();
}

/// <summary>How a numbered load ended.</summary>
public enum NumberedLoad
{
    /// <summary>No save store, or no save in the slot: nothing changed.</summary>
    Missing,

    /// <summary>The save could not be read or applied; a pending full load is abandoned.</summary>
    Failed,

    /// <summary>The save's records are live.</summary>
    Loaded,
}

public sealed partial class SavesAndSettings
{
    // A full load's state until the interpreter has restored its frames (ops 0x1a1, 0xae).
    private NativeNumberedSaveState? _loaded;
    // The last load's records, carried into the next save for what the port does not model.
    private NativeNumberedSaveState? _retained;

    /// <summary>A full load's saved frames, root first, until <see cref="EndRestore"/>; null when no full load is
    /// being restored.</summary>
    public IReadOnlyList<NativeSavedScriptFrame>? RestoringFrames => _loaded?.Frames;

    /// <summary>The interpreter has restored every saved frame, or abandoned the restore.</summary>
    public void EndRestore() => _loaded = null;

    /// <summary>Op 0x19e: write save <paramref name="slot"/> and then the shared profile. 0 when both were written,
    /// 1 when there is no save store or a write failed.</summary>
    public int SaveNumbered(int slot, NumberedSaveRecords records)
    {
        if (_store == null) return 1;
        try
        {
            NativeNumberedSaveState state = CaptureNumbered(_store, records);
            byte[] payload = NativeNumberedSaveCodec.Encode(state);
            byte[] history = NativeTextHistoryCodec.Encode(records.History);
            NativeSystemTime timestamp = NativeSystemTime.FromLocalDateTime(DateTime.Now);
            uint playSeconds = AccumulatedPlaySeconds;
            _store.SaveNumberedFile(slot, payload, history, timestamp, playSeconds);
            _profile.Save(_store, timestamp, playSeconds);
            return 0;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentOutOfRangeException or OverflowException)
        {
            return 1;
        }
    }

    /// <summary>Ops 0x19f (data only) and 0x1a1 (<paramref name="restoreHistory"/>: a full load, whose frames the
    /// interpreter then restores from <see cref="RestoringFrames"/>). The whole save is decoded before anything
    /// changes, so a damaged graphics record or history tail fails the load with the live globals, audio and history
    /// untouched.</summary>
    public NumberedLoad LoadNumbered(int slot, bool restoreHistory, NumberedSaveRecords records)
    {
        if (_store == null) return NumberedLoad.Missing;
        try
        {
            NativeNumberedSaveFile? file = _store.LoadNumberedFile(slot);
            if (file == null) return NumberedLoad.Missing;
            NativeNumberedSaveState state = NativeNumberedSaveCodec.Decode(
                file.Document.Payload, _store.Identity.NumberedGfxRecordSize);
            GfxPersistenceSnapshot gfxSnapshot = NativeGfxPersistenceCodec.Decode(state);
            NativeTextHistoryCodec.Decoded? history = restoreHistory && file.HistoryTail.Length != 0
                ? NativeTextHistoryCodec.Decode(file.HistoryTail)
                : null;
            records.Memory.ReadSaveRecords(state);
            records.Audio.ReadSaveRecords(state);
            records.Graphics.ReadSaveRecords(gfxSnapshot, records.ReleaseSurfacesOnLoad);
            _retained = state;
            _pendingPlaySeconds = file.Document.Metadata.AccumulatedPlaySeconds;
            if (restoreHistory)
            {
                // A full load restores the root frame now (script_frame_restore_saved_layout commits); data only
                // does not.
                CommitLoadedPlayTime();
                if (history == null) records.History.Clear();
                else
                {
                    records.History.RestorePersistenceSnapshot(history.Entries, history.Records);
                    records.History.DropLastGroup();   // the resumed page records it again
                }
                _loaded = state;
            }
            else _loaded = null;
            return NumberedLoad.Loaded;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentOutOfRangeException or OverflowException)
        {
            _loaded = null;
            return NumberedLoad.Failed;
        }
    }

    /// <summary>Op 0x1a0: 0 with the slot's metadata, 1 when there is no store or no save or the file fails, 2 when
    /// the save is damaged or the slot is out of range.</summary>
    public int QueryNumbered(int slot, out NativeSaveMetadata? metadata)
    {
        metadata = null;
        if (_store == null) return 1;
        try
        {
            metadata = _store.QueryNumberedMetadata(slot);
            return metadata == null ? 1 : 0;
        }
        catch (Exception error) when (error is EndOfStreamException or InvalidDataException
                                          or ArgumentOutOfRangeException)
        {
            return 2;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    /// <summary>Op 0x1ab: the store's result, 2 without a store or for a slot out of range.</summary>
    public int DeleteNumbered(int slot)
    {
        try
        {
            return _store?.DeleteNumberedPair(slot) ?? 2;
        }
        catch (ArgumentOutOfRangeException) { return 2; }
    }

    /// <summary>Op 0x1ac: the store's result, 2 without a store, for a slot out of range or when the copy fails.
    /// </summary>
    public int CopyNumbered(int sourceSlot, int destinationSlot)
    {
        try
        {
            return _store?.CopyNumberedPair(sourceSlot, destinationSlot) ?? 2;
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or IOException
                                          or UnauthorizedAccessException)
        {
            return 2;
        }
    }

    /// <summary>Op 0x1ae (<see cref="NumberedSaveThumbnails.Write"/>).</summary>
    public int WriteThumbnail(ISurfacePixels graphics, int slot, int surface)
        => NumberedSaveThumbnails.Write(graphics, _store, slot, surface);

    /// <summary>Op 0x1af (<see cref="NumberedSaveThumbnails.Load"/>).</summary>
    public int LoadThumbnail(ISurfacePixels graphics, int slot, int surface)
        => NumberedSaveThumbnails.Load(graphics, _store, slot, surface);

    /// <summary>A numbered save's records, in native's order of the payload: the save native's carried records and
    /// the saved frames, then each native's own.</summary>
    private NativeNumberedSaveState CaptureNumbered(INativeDatStore store, NumberedSaveRecords records)
    {
        NativeSaveBankDimensions dimensions = store.Identity.BankDimensions
            ?? throw new InvalidDataException(
                "The selected native persistence identity has no numbered-save bank dimensions.");
        NativeSavedScriptFrame[] frames = records.CaptureFrames();
        int gfxRecordSize = store.Identity.NumberedGfxRecordSize;
        NativeNumberedSaveState state = (_retained ?? NativeNumberedSaveCodec.Empty(frames, gfxRecordSize)) with
        {
            Frames = frames,
            LocalPointerScratch = new int[dimensions.LocalPointerScratch],
        };
        state = records.Audio.WriteSaveRecords(state);
        state = records.Memory.WriteSaveRecords(state, dimensions);
        return records.Graphics.WriteSaveRecords(state, gfxRecordSize, records.SourceTextObjects());
    }
}

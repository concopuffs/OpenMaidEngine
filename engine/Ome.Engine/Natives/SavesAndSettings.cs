using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

public enum SharedProfileShutdownFlushOutcome
{
    Saved,
    AlreadyHandled,
    Suppressed,
    StoreUnavailable,
    Failed,
}

public readonly record struct SharedProfileShutdownFlushResult(
    SharedProfileShutdownFlushOutcome Outcome,
    string? Error = null);

/// <summary>
/// The save and settings native (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.2; milestone 9): the
/// shared profile (SAVE.DAT's cells, catalog unlocks and playtime, and RT.DAT's read-text database), the numbered saves
/// (<c>SavesAndSettings.Numbered.cs</c>), the save container's playtime clock, and SYS4REG.INI, the settings registry.
/// It has no devices: the save store (<see cref="INativeDatStore"/>) and the settings store read and write files
/// through .NET (spec §6.4).
/// </summary>
/// <remarks>
/// <para>Engine thread, apart from the shutdown flush, which the front end calls once the engine thread has stopped,
/// and the settings store, which the front end's shell also reads and writes (SYS4REG.INI keys native AGERC owned).</para>
/// <para>A numbered save holds other natives' records too; each writes and reads its own, in the order this native
/// decides (spec §5.1, rule 6; <see cref="NumberedSaveRecords"/>).</para>
/// </remarks>
public sealed partial class SavesAndSettings
{
    private readonly SharedProfile _profile;
    private readonly INativeDatStore? _store;
    private readonly Sys4RegIniStore? _settings;
    private readonly bool _noSaveDat;
    private readonly object _shutdownLock = new();
    private bool _shutdownHandled;
    // The save container's playtime clock (docs/engine-re.md, "Playtime"): a save stores now + base - start in whole
    // seconds. Construction and op 0x9 zero it from now; reading a save container sets only the pending value, which
    // a full load's frame restores and op 0xad commit as the new base. Native's now is timeGetTime(), milliseconds
    // since Windows started; TimeProvider.System's timestamp has the same origin.
    private readonly TimeProvider _playClock;
    private uint _playBaseSeconds;
    private long _playStartSeconds;
    private uint _pendingPlaySeconds;

    /// <param name="profile">The shared profile, already read from the store (empty when there is none).</param>
    /// <param name="store">The save store, or null when the run writes no saves (a numbered save then fails).</param>
    /// <param name="settings">SYS4REG.INI, or null when the run has none (self-test, read-only persistence).</param>
    /// <param name="playClock">The playtime clock; the system's by default.</param>
    /// <param name="noSaveDat">Native set:NoSaveDat: the clean-shutdown flush writes nothing.</param>
    public SavesAndSettings(SharedProfile? profile = null, INativeDatStore? store = null,
                            Sys4RegIniStore? settings = null, TimeProvider? playClock = null, bool noSaveDat = false)
    {
        _profile = profile ?? new SharedProfile();
        _store = store;
        _settings = settings;
        _playClock = playClock ?? TimeProvider.System;
        _noSaveDat = noSaveDat;
        ResetPlayTime();
        // AGE constructs the container (base 0 from now) before it reads SAVE.DAT, whose playtime only lands pending.
        _pendingPlaySeconds = _profile.AccumulatedPlaySeconds;
    }

    /// <summary>The shared profile: the cells ops 0x1a2/0x1a3/0x1a9/0x1aa store and load, the catalog unlocks, the
    /// read-text database and message:ReadTextSkip while the game runs.</summary>
    public SharedProfile Profile => _profile;

    /// <summary>SYS4REG.INI, or null when the run has none.</summary>
    public Sys4RegIniStore? Settings => _settings;

    /// <summary>The save store, or null when the run writes no saves.</summary>
    public INativeDatStore? Store => _store;

    /// <summary>op_0x9_reset_scene_and_reload_root: save_container_playtime_reset after the hotspot resets.</summary>
    public void ResetScene() => ResetPlayTime();

    /// <summary>save_container_encode_and_write@0x42fac0's playtime: timeGetTime() / 1000 + base - start, each
    /// truncated to whole seconds on its own.</summary>
    public uint AccumulatedPlaySeconds =>
        unchecked((uint)(PlayClockSeconds() + _playBaseSeconds - _playStartSeconds));

    /// <summary>save_container_playtime_commit_pending@0x4306c0 (SYS4433 @0x431090), which
    /// script_frame_restore_saved_layout runs for each frame a full load restores (layouts 2 and 3), and op 0xad: the
    /// loaded playtime becomes the base, counting from now.</summary>
    public void CommitLoadedPlayTime()
    {
        _playBaseSeconds = _pendingPlaySeconds;
        _playStartSeconds = PlayClockSeconds();
    }

    /// <summary>
    /// Match AGE's accepted-WM_CLOSE shared-profile lifecycle. The native NoSaveDat setting gates only this shutdown
    /// write; numbered-save opcode 0x19e continues to flush shared state independently. Repeated front-end teardown
    /// notifications are handled without rotating backups more than once.
    /// </summary>
    public SharedProfileShutdownFlushResult FlushSharedProfileOnShutdown()
    {
        lock (_shutdownLock)
        {
            if (_shutdownHandled)
                return new(SharedProfileShutdownFlushOutcome.AlreadyHandled);
            _shutdownHandled = true;

            if (_noSaveDat)
                return new(SharedProfileShutdownFlushOutcome.Suppressed);
            if (_store == null)
                return new(SharedProfileShutdownFlushOutcome.StoreUnavailable);

            try
            {
                _profile.Save(_store, NativeSystemTime.FromLocalDateTime(DateTime.Now), AccumulatedPlaySeconds);
                return new(SharedProfileShutdownFlushOutcome.Saved);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or InvalidDataException
                or ArgumentOutOfRangeException or OverflowException)
            {
                return new(SharedProfileShutdownFlushOutcome.Failed, error.Message);
            }
        }
    }

    private long PlayClockSeconds() => _playClock.GetTimestamp() / _playClock.TimestampFrequency;

    /// <summary>save_container_playtime_reset@0x42fa80 (SYS4433 @0x4304c0), from the container's construction and op
    /// 0x9: base and pending 0, counting from now.</summary>
    private void ResetPlayTime()
    {
        _playBaseSeconds = 0;
        _pendingPlaySeconds = 0;
        _playStartSeconds = PlayClockSeconds();
    }
}

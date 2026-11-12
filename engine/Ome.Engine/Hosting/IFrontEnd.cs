using Ome.Engine.Model;

namespace Ome.Engine.Hosting;

/// <summary>
/// What <see cref="EngineHost"/> asks of the front end that runs it: the 30 members of the Godot front end's
/// <c>Main</c> the host called before it was lifted into <c>Ome.Engine</c>, and the log (engine layering spec §6.2,
/// milestone 4).
/// </summary>
/// <remarks>
/// <para>Commands return at once. The front end applies them later on its own thread, in the order they were sent;
/// the Godot front end defers each to its next idle time (<c>CallDeferred</c>), as the host did before the lift. The
/// members marked <i>immediate</i> run on the caller's thread and return their answer at once.</para>
/// <para>The interface is provisional. It carries AGE vocabulary (<see cref="ArmVoiceBgmDuckRelease"/>,
/// <see cref="StopVoiceForMessageSkip"/>), so it lives in <c>Ome.Engine</c> and not with the device interfaces.
/// Milestones 6 to 11 replace its parts with device interfaces, and milestone 12 deletes what is left.</para>
/// </remarks>
public interface IFrontEnd
{
    // ---- BGM ----
    void PlayBgm(byte[] oggBytes, string assetName);
    void RestartBgm(byte[] oggBytes, string assetName, int startMode);
    void StopBgm();
    void FadeBgm(int targetBasisPoints, int steps, double realDurationSeconds);
    void CompleteBgmFade();

    // ---- voice ----
    /// <summary><i>Immediate.</i> Counts a voice in before its <see cref="PlayVoice"/> is applied, so
    /// <see cref="IsVoicePlaybackActive"/> is true from now; returns the generation to pass to it.</summary>
    int QueueVoicePlayback();
    void PlayVoice(byte[] oggBytes, string assetName, int generation, bool duckBgm, int duckTargetPercent, bool muted);
    void StopVoiceForMessageSkip();
    void ArmVoiceBgmDuckRelease();
    /// <summary><i>Immediate.</i> A queued or playing voice has not finished.</summary>
    bool IsVoicePlaybackActive { get; }

    // ---- sound effects and mixer ----
    void LoadSoundEffect(byte[] wavBytes, string assetName, int channel);
    void StartSoundEffect(int channel, int startMode);
    void ScheduleSoundEffectStart(int channel, int startMode, double realDelaySeconds);
    void CancelScheduledSoundEffectStarts();
    void ReleaseSoundEffect(int channel);
    /// <summary><i>Immediate.</i></summary>
    bool IsSoundChannelPlaying(int channel);
    void ApplyAudioVolume(int category, int basisPoints);
    void ApplyAudioRouteEnabled(int category, bool enabled);

    // ---- movies ----
    /// <summary><i>Immediate.</i> Opens the movie and starts it on <paramref name="playbackId"/>; false when it cannot
    /// be played. <paramref name="stopTimeMs"/> is where the decoder stops.</summary>
    bool TryPlayMovie(byte[] mpegBytes, string assetName, long playbackId, long resourceId, int assetId,
                      long movieFlags, long initialPositionMs, long startDelayMs, long? presentationDurationMs,
                      out long? stopTimeMs);
    /// <summary><i>Immediate.</i> The front end applies the rate at its next frame.</summary>
    void QueueMoviePlaybackRate(long playbackId, double rate);
    void StopMovie(long playbackId);

    // ---- page and cursor ----
    void PageBreak();
    void ClearPage();
    void SetAgeCursor(byte[] rgba, int width, int height, int hotspotX, int hotspotY);
    void ClearAgeCursor();
    void WarpAgeCursor(int virtualX, int virtualY);

    // ---- dialogs and window ----
    void ShowAgeDiagnostic(DiagnosticMessage message);
    void ShowAgeScriptError(DiagnosticMessage message);
    void ShowAgeFullwidthTextEditor(string initialText);
    /// <summary><i>Immediate.</i> The front end switches at its next frame.</summary>
    void RequestScreenModeFromScript(bool windowed);

    // ---- log ----
    /// <summary><i>Immediate.</i> A line for the run's log.</summary>
    void Print(string message);
    /// <summary><i>Immediate.</i> A warning for the run's log.</summary>
    void Warn(string message);
}

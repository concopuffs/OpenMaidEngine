using System;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;

public sealed partial class GodotAdvHost
{
    private readonly string?[] _sfxNames = new string?[10];              // SC0000 native channel subset
    private int _voiceBgmDuckControl;
    private (AudioPayload Audio, int PlaybackVariant, bool Muted)? _queuedSkippedVoice;
    private readonly object _scheduledVoiceLock = new();
    private (AudioPayload Audio, int PlaybackVariant, uint DelayMs, uint? StartMs)? _scheduledVoice;
    private int _statementBlockActive;   // ops 0x1f4/0x1f5: delayed starts and the tick's own render wait for the close
    private bool _presentationWithheldByStatementBlock;   // main thread only (ConsumePresentationReasons)

    public bool StatementBlockActive => System.Threading.Volatile.Read(ref _statementBlockActive) != 0;

    public void PlayBgm(long id)
        => DispatchBgm(id, 1, false);

    public void RestartBgm(long id, int startMode)
        => DispatchBgm(id, startMode, true);

    private void DispatchBgm(long id, int startMode, bool forceRestart)
    {
        var asset = _res.ResolveBgm(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("bgm", new()
        {
            ["id"] = id,
            ["file"] = audio?.Name,
            ["start_mode"] = startMode,
            ["force_restart"] = forceRestart,
        });
        if (audio == null) return;
        if (forceRestart)
            _main.CallDeferred("RestartBgm", audio.Bytes, audio.Name, startMode);
        else
            _main.CallDeferred("PlayBgm", audio.Bytes, audio.Name);
    }

    public void StopBgm()
    {
        _timeline?.Event("bgm-stop", new());
        _main.CallDeferred("StopBgm");
    }

    public void PlayVoice(long id) => PlayVoice(id, 0, false);

    public void PlayVoice(long id, int playbackVariant) => PlayVoice(id, playbackVariant, false);

    public void PlayVoice(long id, int playbackVariant, bool muted)
    {
        var asset = _res.ResolveVoice(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("voice", new() { ["id"] = id, ["file"] = audio?.Name,
            ["playback_variant"] = playbackVariant, ["muted"] = muted });
        if (audio == null) return;
        bool queuedForSkip;
        bool firstQueued = false;
        lock (_messageSkipLock)
        {
            queuedForSkip = _messageSkipActive;
            if (queuedForSkip)
            {
                firstQueued = _queuedSkippedVoice == null;
                _queuedSkippedVoice = (audio, playbackVariant, muted);
            }
        }
        if (queuedForSkip)
        {
            if (firstQueued) _main.CallDeferred("StopVoiceForMessageSkip");
            return;
        }
        DispatchVoice(audio, playbackVariant, muted);
    }

    private void DispatchVoice(AudioPayload audio, int playbackVariant, bool muted)
    {
        int generation = _main.QueueVoicePlayback();
        // voice_bgm_duck_begin@0x406de0 reads sound:MusicFadeOnVoicePlaying and its volume (percent) at each
        // voice start; the 設定 dialog's ｻｳﾝﾄﾞ page sets both.
        (bool fadeOnVoice, int fadePercent) = VoiceBgmDuckSettings();
        bool duckBgm = fadeOnVoice && !VoiceBgmDuckSuppressed;
        // Godot's stream player has no matching AGE start-mode control. Retain the native
        // variant through dispatch/timeline so that distinction is not erased at the VM seam.
        // A muted voice (op 0x1d5) still plays, at volume 0, so its completion keeps Auto timing.
        _main.CallDeferred("PlayVoice", audio.Bytes, audio.Name, generation, duckBgm, fadePercent, muted);
    }

    /// <summary>sound:MusicFadeOnVoicePlaying (registered default 1) and sound:MusicFadeOnVoicePlayingVolume (50).
    /// </summary>
    private (bool Enabled, int Percent) VoiceBgmDuckSettings()
    {
        Sys4RegIniStore? store = _messageSettingsStore;
        if (store == null) return (true, 50);
        try
        {
            return (store.LoadInt("sound", "MusicFadeOnVoicePlaying", 1) != 0,
                    store.LoadInt("sound", "MusicFadeOnVoicePlayingVolume", 50));
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            return (true, 50);
        }
    }

    // Op 0x1cf bit 0 suppresses both the duck at voice start and the tick's restore.
    public bool VoiceBgmDuckSuppressed => (System.Threading.Volatile.Read(ref _voiceBgmDuckControl) & 1) != 0;

    public void StopVoice()
    {
        // Op 0x1bc: native stops channel 12 and clears message_skip_queued_voice_id/arg.
        lock (_messageSkipLock) _queuedSkippedVoice = null;
        _timeline?.Event("voice-stop", new());
        _main.CallDeferred("StopVoiceForMessageSkip");
    }

    public void ArmVoiceBgmDuckRelease()
    {
        // Op 0x1bf, and the tick after a delayed voice start: bit 0x10000 makes the 0x1cf mask one-shot.
        int control = System.Threading.Volatile.Read(ref _voiceBgmDuckControl);
        if ((control & 0x10000) != 0)
        {
            control = 0;
            System.Threading.Volatile.Write(ref _voiceBgmDuckControl, 0);
        }
        if ((control & 1) != 0) return;
        _main.CallDeferred("ArmVoiceBgmDuckRelease");
    }

    public void SetStatementBlockActive(bool active)
    {
        System.Threading.Volatile.Write(ref _statementBlockActive, active ? 1 : 0);
        _timeline?.Event("statement-block", new() { ["active"] = active });
    }

    public void SetVoiceBgmDuckControl(long flags)
    {
        System.Threading.Volatile.Write(ref _voiceBgmDuckControl, unchecked((int)flags));
        _timeline?.State("voice-bgm-duck-control", new() { ["flags"] = flags });
    }

    public void ScheduleVoicePlayback(long id, int playbackVariant, long delayMs)
    {
        var asset = _res.ResolveVoice(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("voice-scheduled", new()
        {
            ["id"] = id, ["file"] = audio?.Name, ["playback_variant"] = playbackVariant,
            ["delay_ms"] = unchecked((uint)delayMs),
        });
        lock (_scheduledVoiceLock)
            _scheduledVoice = audio == null
                ? null
                : (audio, playbackVariant, unchecked((uint)delayMs), null);
    }

    public void LoadSoundEffect(long resourceId, int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        var asset = _res.ResolveSoundEffect(resourceId);
        var audio = asset != null ? LoadAudio(asset) : null;
        _sfxNames[channel] = audio?.Name;
        _timeline?.Event("sfx-load", new() { ["resource"] = resourceId, ["channel"] = channel,
            ["file"] = audio?.Name });
        if (audio != null) _main.CallDeferred("LoadSoundEffect", audio.Bytes, audio.Name, channel);
    }

    public void StartSoundEffect(int channel) => StartSoundEffect(channel, 0);

    public void StartSoundEffect(int channel, int startMode)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        _timeline?.Event("sfx-start", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["file"] = _sfxNames[channel] });
        _main.CallDeferred("StartSoundEffect", channel, startMode);
    }

    public void ScheduleSoundEffectStart(int channel, int startMode, long delayMs)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        long ms = System.Math.Clamp(delayMs, 0, 60_000);
        double realSeconds = ms / 1000.0 / System.Math.Max(0.05, _clock.Speed);
        _timeline?.Event("sfx-start-scheduled", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["delay_ms"] = ms, ["file"] = _sfxNames[channel] });
        _main.CallDeferred("ScheduleSoundEffectStart", channel, startMode, realSeconds);
    }

    public void ReleaseSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        _timeline?.Event("sfx-release", new() { ["channel"] = channel,
            ["file"] = _sfxNames[channel] });
        _sfxNames[channel] = null;
        _main.CallDeferred("ReleaseSoundEffect", channel);
    }

    public bool IsSoundChannelPlaying(int channel) => _main.IsSoundChannelPlaying(channel);

    private AudioPayload? LoadAudio(AssetEntry asset)
    {
        try { return _res.ReadAudio(asset); }
        catch (System.Exception e)
        {
            Godot.GD.Print($"audio read failed {asset.Name}: {e.Message}");
            return null;
        }
    }

    public void FadeBgm(int targetBasisPoints, long durationMs)
    {
        long ms = System.Math.Clamp(durationMs, 0, 60_000);
        double realSeconds = ms / 1000.0 / System.Math.Max(0.05, _clock.Speed);
        _timeline?.State("bgm-fade", new() { ["target_basis_points"] = targetBasisPoints, ["duration_ms"] = ms });
        _main.CallDeferred("FadeBgm", targetBasisPoints, NativeAudioVolume.FadeSteps(ms), realSeconds);
        long deadline = _clock.NowMs + ms;
        IsSleeping = true;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            // Native parks the interpreter in its audio service while the main render loop continues.
            // Publish scene changes accumulated before the fade (notably GAMESTART -> SC0000's black
            // frame), then leave presentation ownership with the compositor for the timed wait.
            RequestSynchronizedPresentation();
            while (_clock.NowMs < deadline && !_stopping)
                _frameSignal.WaitOne(_clock.RealMillisecondsUntil(deadline, 50));
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
            IsSleeping = false;
        }
        _timeline?.State("running", new() { ["bgm_fade_complete"] = true });
    }

    public void ApplyAudioVolume(int category, int basisPoints)
    {
        _timeline?.State("audio-volume", new()
        {
            ["category"] = category,
            ["basis_points"] = basisPoints,
        });
        _main.CallDeferred("ApplyAudioVolume", category, basisPoints);
    }

    public void ApplyAudioRouteEnabled(int category, bool enabled)
    {
        _timeline?.State("audio-route", new()
        {
            ["category"] = category,
            ["enabled"] = enabled,
        });
        _main.CallDeferred("ApplyAudioRouteEnabled", category, enabled);
    }
}

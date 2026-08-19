namespace Age.Engine.Hosting;

public interface IAudioHost
{
    void PlayBgm(long id);

    // Ordinary op 0xbf is VM-filtered so reasserting the current track is idempotent. Ops
    // 0xb7/0xb9 deliberately bypass that guard and restart with logical loop/one-shot mode.
    void RestartBgm(long id, int startMode) => PlayBgm(id);

    // Op 0xb8 releases the current source rather than merely applying a zero-volume envelope.
    void StopBgm() => FadeBgm(0, 0);

    void PlayVoice(long id);

    // Native voice playback retains a second start argument: ordinary dialogue passes 0,
    // while History replay (0x1bd) passes 1. Existing non-audio hosts may ignore it.
    void PlayVoice(long id, int playbackVariant) => PlayVoice(id);

    void ScheduleVoicePlayback(long id, int playbackVariant, long delayMs) { }

    // Native op 0x1cf stores a transient control mask. Bit 0 suppresses the automatic
    // BGM attenuation normally applied when a voice starts.
    void SetVoiceBgmDuckControl(long flags) { }

    void LoadSoundEffect(long resourceId, int channel) { }
    void StartSoundEffect(int channel) { }

    // Ops 0xb5/0xba share the native channel-start worker. Mode 0 plays once; mode 1
    // rewinds the decoder at EOF. The one-argument seam remains for simple hosts.
    void StartSoundEffect(int channel, int startMode) => StartSoundEffect(channel);

    // Native SetDelay (op 0x2bf) starts an already-loaded channel after delayMs.
    // startMode is forwarded to the same worker used by immediate SFX starts.
    void ScheduleSoundEffectStart(int channel, int startMode, long delayMs) { }

    void ReleaseSoundEffect(int channel) { }
    void FadeBgm(int targetPercent, long durationMs) { }

    // Native op 0x1be queries the common 13-slot sound manager. Observed assignments are
    // SFX 0..9, BGM 11, and voice 12; unsupported/unavailable slots report false.
    bool IsSoundChannelPlaying(int channel) => false;

    // AGE's sound:* settings registry is VM-owned; the host applies changes to active playback.
    void ApplyAudioVolume(int category, int basisPoints) { }
    void ApplyAudioRouteEnabled(int category, bool enabled) { }
}

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

    // Native op 0x1d5 arms voice_mute_next_flags; the following op 0xc4 then starts its voice on
    // channel 12 at volume 0, so completion timing (ADV Auto, channel-12 queries) is unchanged.
    void PlayVoice(long id, int playbackVariant, bool muted) => PlayVoice(id, playbackVariant);

    // Native op 0x1bc stops channel 12 and drops a voice queued while message Skip was active.
    void StopVoice() { }

    // Native op 0x1bf: advance the one-shot stage of the 0x1cf control mask (bit 0x10000 clears it)
    // and, unless bit 0 suppresses ducking, arm the tick's BGM restore for when the voice ends.
    void ArmVoiceBgmDuckRelease() { }

    // Native ops 0x1f4/0x1f5: while the outermost statement block is open the tick withholds
    // delayed SFX/voice starts; they begin once the block closes.
    void SetStatementBlockActive(bool active) { }

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
    // Op 0xc2: start fading the BGM envelope to a level in basis points (0..10000) over durationMs, and return. The
    // engine tick runs the fade (run-state 0x200) while the script goes on; reaching a zero target releases the source
    // (bgm_fade_tick -> bgm_stop_release), so a zero duration with a zero target releases it at once.
    void FadeBgm(int targetBasisPoints, long durationMs) { }

    // Whether the last op 0xc2 fade is still running.
    bool IsBgmFadeActive => false;

    // Ops 0xb7/0xb8/0xb9/0xbf first end a running fade: apply its endpoint now, releasing on a zero target.
    void CompleteBgmFade() { }

    // Native op 0x1be queries the common 13-slot sound manager. Observed assignments are
    // SFX 0..9, BGM 11, and voice 12; unsupported/unavailable slots report false.
    bool IsSoundChannelPlaying(int channel) => false;

    // AGE's sound:* settings registry is VM-owned; the host applies changes to active playback.
    void ApplyAudioVolume(int category, int basisPoints) { }
    void ApplyAudioRouteEnabled(int category, bool enabled) { }
}

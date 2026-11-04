using System;
using System.IO;
using Godot;
using Age.Engine.Model;
using Age.Engine.Persistence;

public partial class Main
{
    private AudioStreamPlayer _bgm = null!;                // looping background music
    private Tween? _bgmFadeTween;
    private AudioStreamPlayer _voice = null!;              // interrupt-on-new voice
    private int _bgmPlaying;
    private int _voiceQueuedGeneration;
    private int _voiceStartedGeneration;
    private int _voiceCompletedGeneration;
    private bool _voiceBgmDuckActive;
    private int _voiceBgmDuckGeneration;
    private int _voiceBgmDuckRestoreLevel = AudioMixerSettings.MaximumVolume;
    // Native bgm object +0x420: the committed BGM envelope level in basis points. The player's volume is
    // always NativeAudioVolume.ToDecibels(level); the Master and Music buses carry the mixer settings, and
    // decibels add where native multiplies the levels.
    private int _bgmLevel = AudioMixerSettings.MaximumVolume;
    private bool _voiceBgmDuckReleaseArmed;               // native voice_bgm_duck_release_armed (op 0x1bf)
    private readonly System.Collections.Generic.List<Action> _sfxStartsWithheld = new(); // statement block open
    private readonly AudioStreamPlayer[] _sfx = new AudioStreamPlayer[10]; // SC0000 channels 0..9
    private readonly int[] _sfxGenerations = new int[10];
    private readonly int[] _sfxPlaying = new int[10];
    private AudioMixerSettings _audioMixerSettings = null!;
    private Sys4RegIniStore? _sys4RegIniStore;

    // Decode VFS-owned bytes in Godot. Ordinary BGM loops; forced starts retain AGE's
    // loop/one-shot mode. Voice plays once, cutting off any prior line.
    public void PlayBgm(byte[] oggBytes, string assetName)
        => StartBgm(oggBytes, assetName, 1);

    public void RestartBgm(byte[] oggBytes, string assetName, int startMode)
        => StartBgm(oggBytes, assetName, startMode);

    private void StartBgm(byte[] oggBytes, string assetName, int startMode)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
        if (stream == null) { GD.Print($"OGG load failed {assetName}"); return; }
        stream.Loop = startMode != 0;
        // FadeBgm is marshalled from the VM thread and starts on the next Godot frame, while the VM's
        // blocking deadline begins immediately. Scene startup can consequently request the replacement
        // track just before the old fade tween reaches zero. Do not let that orphaned tween mute the new
        // stream after this method restores its normal gain.
        CancelBgmFade();
        SetBgmLevel(AudioMixerSettings.MaximumVolume);
        _bgm.Stream = stream;
        _bgm.Play();
        System.Threading.Volatile.Write(ref _bgmPlaying, 1);
    }

    public void StopBgm()
    {
        RestoreVoiceBgmDuck();
        CancelBgmFade();
        _bgm.Stop();
        _bgm.Stream = null;
        SetBgmLevel(AudioMixerSettings.MaximumVolume);
        System.Threading.Volatile.Write(ref _bgmPlaying, 0);
    }

    private void PersistAudioMixerSettings(AudioMixerSettingsSnapshot snapshot)
    {
        try
        {
            _sys4RegIniStore?.Save(snapshot);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            System.Console.Error.WriteLine(
                $"[settings] audio mixer save failed; runtime setting remains active: {error.Message}");
        }
    }

    private void ApplyAudioMixerSnapshot(AudioMixerSettingsSnapshot snapshot)
    {
        for (int category = 0; category < AudioMixerSettings.CategoryCount; category++)
            ApplyAudioVolume(category, snapshot.Volumes[category]);
        for (int category = (int)AudioMixerCategory.Music;
             category < AudioMixerSettings.CategoryCount;
             category++)
            SetAudioBusMuted(category, !snapshot.Routes[category]);
    }

    public void ApplyAudioVolume(int category, int basisPoints)
    {
        string? busName = AudioBusName(category);
        if (busName == null) return;
        int bus = AudioServer.GetBusIndex(busName);
        if (bus < 0) return;
        // -1 is native's unconfigured sentinel: the hardware default gain stays (full scale here).
        int effectiveBasisPoints = basisPoints < 0 ? AudioMixerSettings.MaximumVolume : basisPoints;
        AudioServer.SetBusVolumeDb(bus, NativeAudioVolume.ToDecibels(effectiveBasisPoints));
    }

    public void ApplyAudioRouteEnabled(int category, bool enabled)
    {
        if (category == (int)AudioMixerCategory.Music)
        {
            RestoreVoiceBgmDuck();
            CancelBgmFade();
            if (enabled)
            {
                if (_bgm.Stream != null)
                {
                    _bgm.Play();
                    System.Threading.Volatile.Write(ref _bgmPlaying, 1);
                }
            }
            else
            {
                _bgm.Stop();
                System.Threading.Volatile.Write(ref _bgmPlaying, 0);
            }
        }
        else if (!enabled && category == (int)AudioMixerCategory.SoundEffect)
        {
            CancelScheduledSoundEffectStarts();
            foreach (AudioStreamPlayer player in _sfx) player.Stop();
        }
        else if (!enabled && category == (int)AudioMixerCategory.Voice)
            StopVoiceForMessageSkip();

        SetAudioBusMuted(category, !enabled);
    }

    private static void SetAudioBusMuted(int category, bool muted)
    {
        string? busName = AudioBusName(category);
        if (busName == null) return;
        int bus = AudioServer.GetBusIndex(busName);
        if (bus >= 0) AudioServer.SetBusMute(bus, muted);
    }

    private static string? AudioBusName(int category) => category switch
    {
        (int)AudioMixerCategory.Master => "Master",
        (int)AudioMixerCategory.Music => "Music",
        (int)AudioMixerCategory.SoundEffect => "SFX",
        (int)AudioMixerCategory.Voice => "Voice",
        (int)AudioMixerCategory.Movie => "Movie",
        _ => null,
    };

    public int QueueVoicePlayback()
        => System.Threading.Interlocked.Increment(ref _voiceQueuedGeneration);

    public bool IsVoicePlaybackActive
        => System.Threading.Volatile.Read(ref _voiceCompletedGeneration)
           < System.Threading.Volatile.Read(ref _voiceQueuedGeneration);

    public void PlayVoice(byte[] oggBytes, string assetName, int generation, bool duckBgm, int duckTargetPercent,
        bool muted)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
        if (stream == null)
        {
            GD.Print($"OGG load failed {assetName}");
            CompleteVoiceGeneration(generation);
            return;
        }
        // Native voice_bgm_duck_begin ducks whenever 0x1cf bit 0 is clear, muted voices included. A
        // suppressed start leaves an earlier duck in place; only the tick's armed release restores it.
        if (duckBgm)
            BeginVoiceBgmDuck(generation, duckTargetPercent);
        stream.Loop = false;
        _voice.Stream = stream;
        _voice.VolumeDb = muted ? NativeAudioVolume.ToDecibels(0) : 0.0f;   // op 0x1d5: channel 12 at volume 0, timing unchanged
        System.Threading.Volatile.Write(ref _voiceStartedGeneration, generation);
        _voice.Play();
    }

    public void StopVoiceForMessageSkip()
    {
        _voice.Stop();
        CompleteVoiceGeneration(System.Threading.Volatile.Read(ref _voiceQueuedGeneration));
    }

    private void UpdateVoicePlaybackState()
    {
        int started = System.Threading.Volatile.Read(ref _voiceStartedGeneration);
        if (started > System.Threading.Volatile.Read(ref _voiceCompletedGeneration) && !_voice.Playing)
            CompleteVoiceGeneration(started);
    }

    private void CompleteVoiceGeneration(int generation)
    {
        int current;
        do
        {
            current = System.Threading.Volatile.Read(ref _voiceCompletedGeneration);
            if (current >= generation) return;
        }
        while (System.Threading.Interlocked.CompareExchange(
                   ref _voiceCompletedGeneration, generation, current) != current);
    }

    public void ArmVoiceBgmDuckRelease() => _voiceBgmDuckReleaseArmed = true;

    // Native engine_main_tick (0x411e12): with BGM ducked, restore it once the voice has ended, the
    // release is armed (op 0x1bf or a delayed voice start), no explicit BGM fade owns the envelope and
    // 0x1cf bit 0 is clear. So BGM stays ducked between lines until the next line arms the release.
    private void UpdateVoiceBgmDuckRelease()
    {
        if (!_voiceBgmDuckActive || !_voiceBgmDuckReleaseArmed || IsVoicePlaybackActive) return;
        if (_bgmFadeTween != null || (_host?.VoiceBgmDuckSuppressed ?? false)) return;
        FadeBackFromVoiceBgmDuck();
    }

    // The release swaps run-state 0x4000 for the 0x200 BGM fade: bgm_fade_arm(10000, 1) on a 10 ms period
    // timer, and bgm_fade_tick@0x464960 adds one progress point per elapsed period up to 100. The level runs
    // linearly in basis points from the ducked level to full (target 10000, not the saved pre-duck level)
    // over one second, through the native volume curve. While it runs, voice_bgm_duck_begin refuses to duck,
    // as for an explicit 0xc2 fade.
    private const double VoiceBgmDuckReleaseSeconds = 1.0;
    private const int VoiceBgmDuckReleaseSteps = 100;

    private void FadeBackFromVoiceBgmDuck()
    {
        _voiceBgmDuckActive = false;
        _voiceBgmDuckGeneration = 0;
        _voiceBgmDuckReleaseArmed = false;
        StartBgmLevelFade(AudioMixerSettings.MaximumVolume, VoiceBgmDuckReleaseSteps, VoiceBgmDuckReleaseSeconds);
    }

    private void SetBgmLevel(int basisPoints)
    {
        _bgmLevel = System.Math.Clamp(basisPoints, 0, AudioMixerSettings.MaximumVolume);
        _bgm.VolumeDb = NativeAudioVolume.ToDecibels(_bgmLevel);
    }

    // bgm_fade_tick@0x464960: the level at progress p is (from × (100 − p) + target × p) / 100, applied in
    // whole steps; the target is committed when the fade completes.
    private void StartBgmLevelFade(int target, int steps, double realDurationSeconds)
    {
        int from = _bgmLevel;
        target = System.Math.Clamp(target, 0, AudioMixerSettings.MaximumVolume);
        var tween = CreateTween();
        _bgmFadeTween = tween;
        tween.Finished += () =>
        {
            if (ReferenceEquals(_bgmFadeTween, tween))
                _bgmFadeTween = null;
        };
        tween.TweenMethod(Callable.From<float>(progress =>
        {
            int points = (int)System.Math.Floor(progress * steps) * (100 / steps);
            SetBgmLevel(NativeAudioVolume.InterpolateFade(from, target, points));
        }), 0.0f, 1.0f, realDurationSeconds);
    }

    private void ReleaseWithheldSoundEffectStarts()
    {
        if (_sfxStartsWithheld.Count == 0 || (_host?.StatementBlockActive ?? false)) return;
        Action[] starts = _sfxStartsWithheld.ToArray();
        _sfxStartsWithheld.Clear();
        foreach (Action start in starts) start();
    }

    private void BeginVoiceBgmDuck(int generation, int targetPercent)
    {
        // Native voice ducking is suppressed while an explicit 0xc2 BGM fade owns the envelope.
        if (_bgmFadeTween != null) return;
        if (!_voiceBgmDuckActive)
            _voiceBgmDuckRestoreLevel = _bgmLevel;
        _voiceBgmDuckActive = true;
        _voiceBgmDuckGeneration = generation;
        _voiceBgmDuckReleaseArmed = false;
        // voice_bgm_duck_begin: MusicFadeOnVoicePlayingVol × 100 basis points (0 reads as 100), applied at
        // once by a terminal fade tick.
        int target = targetPercent * 100;
        SetBgmLevel(target == 0 ? 100 : target);
    }

    private void RestoreVoiceBgmDuck()
    {
        if (!_voiceBgmDuckActive) return;
        SetBgmLevel(_voiceBgmDuckRestoreLevel);
        _voiceBgmDuckActive = false;
        _voiceBgmDuckGeneration = 0;
        _voiceBgmDuckReleaseArmed = false;
    }

    public void LoadSoundEffect(byte[] wavBytes, string assetName, int channel)
    {
        if ((uint)channel >= (uint)_sfx.Length) return;
        _sfxGenerations[channel]++;
        _sfx[channel].Stop();
        _sfx[channel].Stream = null;
        System.Threading.Volatile.Write(ref _sfxPlaying[channel], 0);
        // This is intentionally a Godot-only compatibility boundary. The VFS and engine retain the
        // original WAV bytes; Godot sees an AGE-compatible first-RIFF copy without CP932 INFO metadata.
        byte[] godotWav = RiffWaveSanitizer.PrepareForGodot(wavBytes);
        var stream = AudioStreamWav.LoadFromBuffer(godotWav);
        if (stream == null) { GD.Print($"WAV load failed {assetName}"); return; }
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        _sfx[channel].VolumeDb = 0;
        _sfx[channel].Stream = stream;
    }

    public void StartSoundEffect(int channel) => StartSoundEffect(channel, 0);

    public void StartSoundEffect(int channel, int startMode)
    {
        if ((uint)channel >= (uint)_sfx.Length || _sfx[channel].Stream == null) return;
        if (_sfx[channel].Stream is AudioStreamWav wav)
            wav.LoopMode = startMode == 0
                ? AudioStreamWav.LoopModeEnum.Disabled
                : AudioStreamWav.LoopModeEnum.Forward;
        _sfx[channel].Play();
        System.Threading.Volatile.Write(ref _sfxPlaying[channel], 1);
    }

    public void ScheduleSoundEffectStart(int channel, int startMode, double realDelaySeconds)
    {
        if ((uint)channel >= (uint)_sfx.Length || _sfx[channel].Stream == null) return;
        int generation = _sfxGenerations[channel];
        void StartIfCurrent()
        {
            if (_sfxGenerations[channel] != generation || _sfx[channel].Stream == null) return;
            if (_host?.StatementBlockActive ?? false)
            {
                // Native tick withholds delayed SFX starts while a statement block (op 0x1f4) is open.
                _sfxStartsWithheld.Add(StartIfCurrent);
                return;
            }
            if (_sfx[channel].Stream is AudioStreamWav wav)
                wav.LoopMode = startMode == 0
                    ? AudioStreamWav.LoopModeEnum.Disabled
                    : AudioStreamWav.LoopModeEnum.Forward;
            _sfx[channel].Play();
            System.Threading.Volatile.Write(ref _sfxPlaying[channel], 1);
        }
        if (realDelaySeconds <= 0)
        {
            StartIfCurrent();
            return;
        }
        GetTree().CreateTimer(realDelaySeconds).Timeout += StartIfCurrent;
    }

    public void CancelScheduledSoundEffectStarts()
    {
        // Native scene_context_init_reset calls sfx_clear_scheduled_starts. Generation invalidation
        // cancels the timer callbacks without stopping active sounds or unloading their channel streams.
        for (int channel = 0; channel < _sfxGenerations.Length; channel++)
            _sfxGenerations[channel]++;
    }

    public void ReleaseSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfx.Length) return;
        _sfxGenerations[channel]++;
        _sfx[channel].Stop();
        _sfx[channel].Stream = null;
        System.Threading.Volatile.Write(ref _sfxPlaying[channel], 0);
    }

    public bool IsSoundChannelPlaying(int channel) => channel switch
    {
        >= 0 and < 10 => System.Threading.Volatile.Read(ref _sfxPlaying[channel]) != 0,
        11 => System.Threading.Volatile.Read(ref _bgmPlaying) != 0,
        12 => IsVoicePlaybackActive,
        _ => false,
    };

    private void UpdateSoundChannelPlaybackState()
    {
        if (System.Threading.Volatile.Read(ref _bgmPlaying) != 0 && !_bgm.Playing)
            System.Threading.Volatile.Write(ref _bgmPlaying, 0);
        for (int channel = 0; channel < _sfx.Length; channel++)
            if (System.Threading.Volatile.Read(ref _sfxPlaying[channel]) != 0
                && !_sfx[channel].Playing)
                System.Threading.Volatile.Write(ref _sfxPlaying[channel], 0);
    }

    // Op 0xc2 target is a level in basis points (CONFIG passes setting × 100), not a percent.
    public void FadeBgm(int targetBasisPoints, int steps, double realDurationSeconds)
    {
        RestoreVoiceBgmDuck();
        CancelBgmFade();
        if (realDurationSeconds <= 0) { SetBgmLevel(targetBasisPoints); return; }
        StartBgmLevelFade(targetBasisPoints, steps, realDurationSeconds);
    }

    private void CancelBgmFade()
    {
        var tween = _bgmFadeTween;
        _bgmFadeTween = null;
        if (tween?.IsValid() == true)
            tween.Kill();
    }

    private static void EnsureAudioBuses()
    {
        foreach (string name in new[] { "Music", "SFX", "Voice", "Movie" })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, name);
        }
    }

}

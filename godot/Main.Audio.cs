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
    private int _voiceQueuedGeneration;
    private int _voiceStartedGeneration;
    private int _voiceCompletedGeneration;
    private bool _voiceBgmDuckActive;
    private int _voiceBgmDuckGeneration;
    private float _voiceBgmDuckRestoreDb;
    private readonly AudioStreamPlayer[] _sfx = new AudioStreamPlayer[10]; // SC0000 channels 0..9
    private readonly int[] _sfxGenerations = new int[10];
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
        _bgm.VolumeDb = 0;
        _bgm.Stream = stream;
        _bgm.Play();
    }

    public void StopBgm()
    {
        RestoreVoiceBgmDuck();
        CancelBgmFade();
        _bgm.Stop();
        _bgm.Stream = null;
        _bgm.VolumeDb = 0;
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
        int effectiveBasisPoints = basisPoints < 0
            ? AudioMixerSettings.MaximumVolume
            : System.Math.Clamp(basisPoints, 0, AudioMixerSettings.MaximumVolume);
        float linear = effectiveBasisPoints / (float)AudioMixerSettings.MaximumVolume;
        AudioServer.SetBusVolumeDb(bus, linear <= 0 ? -80.0f : Mathf.LinearToDb(linear));
    }

    public void ApplyAudioRouteEnabled(int category, bool enabled)
    {
        if (category == (int)AudioMixerCategory.Music)
        {
            RestoreVoiceBgmDuck();
            CancelBgmFade();
            if (enabled)
            {
                if (_bgm.Stream != null) _bgm.Play();
            }
            else
                _bgm.Stop();
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

    public void PlayVoice(byte[] oggBytes, string assetName, int generation, bool duckBgm, int duckTargetPercent)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
        if (stream == null)
        {
            GD.Print($"OGG load failed {assetName}");
            CompleteVoiceGeneration(generation);
            return;
        }
        if (duckBgm)
            BeginVoiceBgmDuck(generation, duckTargetPercent);
        else
            RestoreVoiceBgmDuck();
        stream.Loop = false;
        _voice.Stream = stream;
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
        if (_voiceBgmDuckActive && generation >= _voiceBgmDuckGeneration)
            RestoreVoiceBgmDuck();
    }

    private void BeginVoiceBgmDuck(int generation, int targetPercent)
    {
        // Native voice ducking is suppressed while an explicit 0xc2 BGM fade owns the envelope.
        if (_bgmFadeTween != null) return;
        if (!_voiceBgmDuckActive)
            _voiceBgmDuckRestoreDb = _bgm.VolumeDb;
        _voiceBgmDuckActive = true;
        _voiceBgmDuckGeneration = generation;
        float linear = System.Math.Clamp(targetPercent / 100.0f, 0.0f, 1.0f);
        _bgm.VolumeDb = linear <= 0 ? -80.0f : Mathf.LinearToDb(linear);
    }

    private void RestoreVoiceBgmDuck()
    {
        if (!_voiceBgmDuckActive) return;
        _bgm.VolumeDb = _voiceBgmDuckRestoreDb;
        _voiceBgmDuckActive = false;
        _voiceBgmDuckGeneration = 0;
    }

    public void LoadSoundEffect(byte[] wavBytes, string assetName, int channel)
    {
        if ((uint)channel >= (uint)_sfx.Length) return;
        _sfxGenerations[channel]++;
        _sfx[channel].Stop();
        _sfx[channel].Stream = null;
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
    }

    public void ScheduleSoundEffectStart(int channel, int startMode, double realDelaySeconds)
    {
        if ((uint)channel >= (uint)_sfx.Length || _sfx[channel].Stream == null) return;
        int generation = _sfxGenerations[channel];
        void StartIfCurrent()
        {
            if (_sfxGenerations[channel] != generation || _sfx[channel].Stream == null) return;
            if (_sfx[channel].Stream is AudioStreamWav wav)
                wav.LoopMode = startMode == 0
                    ? AudioStreamWav.LoopModeEnum.Disabled
                    : AudioStreamWav.LoopModeEnum.Forward;
            _sfx[channel].Play();
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
    }

    public void FadeBgm(int targetPercent, double realDurationSeconds)
    {
        float linear = System.Math.Clamp(targetPercent / 100.0f, 0.0f, 1.0f);
        float targetDb = linear <= 0 ? -80.0f : Mathf.LinearToDb(linear);
        RestoreVoiceBgmDuck();
        CancelBgmFade();
        if (realDurationSeconds <= 0) { _bgm.VolumeDb = targetDb; return; }

        var tween = CreateTween();
        _bgmFadeTween = tween;
        tween.Finished += () =>
        {
            if (ReferenceEquals(_bgmFadeTween, tween))
                _bgmFadeTween = null;
        };
        tween.TweenProperty(_bgm, "volume_db", targetDb, realDurationSeconds);
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

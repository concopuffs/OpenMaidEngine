using Ome.Engine.Devices;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

public sealed partial class Audio
{
    private const int FullLevel = AudioMixerSettings.MaximumVolume;
    // The voice duck release: bgm_fade_arm(10000, 1) on the 10 ms period timer, one progress point per period, so the
    // level runs from the ducked level to full in 100 steps over one second.
    private const long DuckReleaseMs = 1000;
    private const int DuckReleaseSteps = 100;

    /// <summary>Run-state 0x200: a fade of the BGM envelope. <see cref="Script"/> marks an op 0xc2 fade, which a BGM op
    /// ends first and whose zero target releases the source and clears the retained track; the duck release is the
    /// other kind.</summary>
    private sealed record BgmFade(int From, int Target, int Steps, long StartMs, long DurationMs, bool Script);

    // The retained track, pause flag and start mode are the scene's (_scene.BgmTrack, BgmPaused, BgmStartMode).
    private bool _bgmLoaded;                 // a track is live on channel 11
    // The track last decoded on channel 11, and whether the device holds it. The DirectSound backend's stop
    // (vtable 0x57977c +4, 0x468980) only stops the channel's buffer, and only a decode replaces it, so native's channel
    // keeps the track after a stop; the port unloads the device and keeps the clip.
    private AudioClip? _bgmChannelClip;
    private bool _bgmChannelLoaded;
    private int _bgmLevel = FullLevel;       // bgm object +0x420: the committed envelope level, basis points
    private int _bgmChannelLevel = FullLevel;   // the level channel 11 was last given (a fade step is not committed)
    private BgmFade? _fade;
    private bool _duckActive;                // run-state 0x4000
    private int _duckRestoreLevel = FullLevel;
    private bool _duckReleaseArmed;          // voice_bgm_duck_release_armed (op 0x1bf, a delayed voice start)

    /// <summary>
    /// Op 0xbf (bgm_play_track, start mode 1). The retained track keeps its stream and position: FIELD reasserts the
    /// stage BGM after every unit's action (docs/engine-re.md, "Forced BGM lifecycle siblings"). Where
    /// <paramref name="comparesStartMode"/> (the build's <see cref="AudioRules.PlayComparesStartMode"/>) it does so only
    /// with the retained start mode 1, so a track op 0xb9 played once, or one a scene reset left, starts again. Another
    /// track starts; zero starts the retained track again, and with none releases the source.
    /// </summary>
    public void PlayBgm(long trackId, bool comparesStartMode = false)
    {
        const int loop = 1;
        CompleteScriptFade();
        bool keeps = trackId != 0 && trackId == _scene.BgmTrack
            && (!comparesStartMode || _scene.BgmStartMode == loop);
        // Every path leaves the start mode at 1: 4.24J stores it before its check, and 4.33G's check keeps only a 1.
        _scene.BgmStartMode = loop;
        if (!keeps) StartOrRelease(trackId, loop, forced: false);
        UpdateTimedWork();
    }

    /// <summary>Ops 0xb7 (start mode 1, loop) and 0xb9 (0, once), bgm_force_play_track: store the start mode and start a
    /// track even if it is the current one; zero restarts the retained track, and with none stops.</summary>
    public void RestartBgm(long trackId, int startMode)
    {
        CompleteScriptFade();
        _scene.BgmStartMode = startMode;
        StartOrRelease(trackId, startMode, forced: true);
        UpdateTimedWork();
    }

    // A nonzero track is retained and started; zero starts the retained track, and with none releases the source.
    private void StartOrRelease(long trackId, int startMode, bool forced)
    {
        if (trackId != 0) _scene.BgmTrack = trackId;
        if (_scene.BgmTrack == 0) StopBgmSource();
        else StartBgm(_scene.BgmTrack, startMode, forced);
    }

    /// <summary>Op 0xb8: release the source and clear the retained track (bgm_stop_release@0x464690).</summary>
    public void StopBgm()
    {
        CompleteScriptFade();
        _scene.BgmTrack = 0;
        StopBgmSource();
        UpdateTimedWork();
    }

    /// <summary>
    /// Op 0xc1: flip the BGM's pause (bgm object +0x410) and tell the backend. The DirectSound backend stops channel
    /// 11's buffer where it is, or plays it on from there; with no track on the channel it does nothing, and the flag
    /// still flips. Starting a track and releasing the source clear the flag; an identical play request, which keeps
    /// the stream, does not.
    /// </summary>
    public void ToggleBgmPause()
    {
        _scene.BgmPaused = !_scene.BgmPaused;
        _timeline?.Event("bgm-pause", new() { ["paused"] = _scene.BgmPaused });
        if (_bgmLoaded && _bgmChannelLoaded) _device.SetPaused(BgmChannel, _scene.BgmPaused);
        UpdateTimedWork();
    }

    /// <summary>The BGM object's pause flag (+0x410), which op 0xc1 flips. A scene reset zeroes it and leaves the
    /// stream, so a held stream stays held until a start, and the next op 0xc1 holds it again.</summary>
    public bool BgmPaused => _scene.BgmPaused;

    /// <summary>Op 0xc0: the retained track. A fade to zero that has run its course has cleared it.</summary>
    public long CurrentBgmTrack
    {
        get
        {
            ServiceFade(_clock.NowMs);
            UpdateTimedWork();
            return _scene.BgmTrack;
        }
    }

    /// <summary>Op 0xc2: fade the BGM envelope to a level in basis points (CONFIG passes the setting × 100) and return.
    /// The tick runs the fade while the script goes on; at its end the level is committed, and a zero target releases
    /// the source and clears the retained track (bgm_fade_tick@0x464960). Under ADV fast-forward native applies the
    /// endpoint at once; an advance click sets no fast-forward, so it keeps the authored length. A run on a standing
    /// clock applies it at once too (settleFadesAtOnce).</summary>
    public void FadeBgm(int targetBasisPoints, long durationMs)
    {
        long ms = _skip.FastForwardActive || _settleFadesAtOnce ? 0 : Math.Clamp(durationMs, 0, 60_000);
        _timeline?.Event("bgm-fade", new() { ["target_basis_points"] = targetBasisPoints, ["duration_ms"] = ms });
        RestoreDuck();
        _fade = null;
        var fade = new BgmFade(_bgmLevel, Math.Clamp(targetBasisPoints, 0, FullLevel),
            NativeAudioVolume.FadeSteps(ms), _clock.NowMs, ms, Script: true);
        if (ms == 0) CommitFade(fade, targetBasisPoints);
        else _fade = fade;
        UpdateTimedWork();
    }

    /// <summary>Op 0x1bf, and the tick after a delayed voice start: bit 0x10000 makes the op 0x1cf mask one-shot, and
    /// unless bit 0 suppresses it the duck release is armed for when the voice ends.</summary>
    public void ArmVoiceBgmDuckRelease()
    {
        ArmDuckRelease();
        UpdateTimedWork();
    }

    /// <summary>Op 0x1cf: the transient control mask. Bit 0 suppresses the duck at a voice start and the tick's
    /// release.</summary>
    public void SetVoiceBgmDuckControl(long flags)
    {
        _scene.DuckControl = unchecked((int)flags);
        _timeline?.State("voice-bgm-duck-control", new() { ["flags"] = flags });
        UpdateTimedWork();
    }

    // The start (bgm_play_track and bgm_force_play_track) clears the pause flag before it calls the backend, whose play
    // decodes the track into channel 11 and starts it, looping by the start mode. A start ends a fade, except the Music
    // route's restart (endsFade false), which leaves a running op 0xc2 fade to the tick.
    private void StartBgm(long trackId, int startMode, bool forced, bool endsFade = true)
    {
        AudioClip? clip = _assets.Bgm(trackId);
        _timeline?.Event("bgm", new()
        {
            ["id"] = trackId,
            ["file"] = clip?.Name,
            ["start_mode"] = startMode,
            ["force_restart"] = forced,
        });
        _scene.BgmPaused = false;
        if (clip == null) return;   // the track that played goes on
        if (endsFade) _fade = null;
        _bgmLevel = FullLevel;
        _bgmChannelLevel = FullLevel;
        _bgmLoaded = true;
        _bgmChannelClip = clip;
        _bgmChannelLoaded = true;
        _device.Load(BgmChannel, AudioBus.Music, clip);
        PlayChannel(BgmChannel, loop: startMode != 0, NativeAudioVolume.ToDecibels(FullLevel));
    }

    private void StopBgmSource()
    {
        _timeline?.Event("bgm-stop", new());
        ReleaseBgmSource();
    }

    // bgm_stop_release: the source goes, and the envelope starts again at full.
    private void ReleaseBgmSource()
    {
        RestoreDuck();
        _fade = null;
        ReleaseBgmStream();
    }

    private void ReleaseBgmStream()
    {
        _scene.BgmPaused = false;
        _bgmLoaded = false;
        _bgmChannelLoaded = false;
        UnloadChannel(BgmChannel);
        _bgmLevel = FullLevel;
        _bgmChannelLevel = FullLevel;
    }

    // sound_set_music_route_enabled (4.38D 0x408460, 4.24J 0x407f80) releases the source, puts the retained track back
    // and calls bgm_force_play_track(0, +0x414) on the new route. The retained track starts again from its start at
    // full level, with the retained start mode and the pause cleared; the off route sounds nothing. With no retained
    // track (after a stop, a fade to zero or a scene reset) the source goes. A running op 0xc2 fade goes on: native's
    // tick advances it whatever the route, so a fade to zero still releases the source when it ends.
    private void RestartBgmForRoute(bool enabled)
    {
        if (_scene.BgmTrack == 0) ReleaseBgmStream();
        else if (enabled) StartBgm(_scene.BgmTrack, _scene.BgmStartMode, forced: true, endsFade: false);
        else
        {
            StopChannel(BgmChannel);
            _scene.BgmPaused = false;
            _bgmLevel = FullLevel;
        }
    }

    // Ops 0xb7/0xb8/0xb9/0xbf, and a numbered load, first end a running op 0xc2 fade: bgm_fade_tick(..., 100) applies
    // its endpoint. A fade that has run its course ends as the tick would end it.
    private void CompleteScriptFade()
    {
        if (_fade is not { Script: true } fade) return;
        if (_clock.NowMs - fade.StartMs < fade.DurationMs)
            _timeline?.Event("bgm-fade-complete", new());
        _fade = null;
        CommitFade(fade, fade.Target);
    }

    // bgm_fade_tick: the level at progress p is (from × (100 − p) + target × p) / 100, applied in whole steps and
    // committed only at 100, so a fade armed again part-way starts from the last committed level.
    private void ServiceFade(long now)
    {
        if (_fade is not { } fade) return;
        long elapsed = now - fade.StartMs;
        if (elapsed >= fade.DurationMs)
        {
            _fade = null;
            CommitFade(fade, fade.Target);
            return;
        }
        int points = (int)(Math.Max(0, elapsed) * fade.Steps / fade.DurationMs) * (100 / fade.Steps);
        ApplyBgmChannelLevel(NativeAudioVolume.InterpolateFade(fade.From, fade.Target, points));
    }

    private void CommitFade(BgmFade fade, int target)
    {
        if (fade.Script && target <= 0)
        {
            _timeline?.Event("bgm-fade-end", new() { ["target_basis_points"] = target, ["released"] = true });
            ReleaseBgmSource();
            _scene.BgmTrack = 0;
            return;
        }
        if (fade.Script)
            _timeline?.Event("bgm-fade-end", new() { ["target_basis_points"] = target, ["released"] = false });
        SetBgmLevel(target);
    }

    private void SetBgmLevel(int basisPoints)
    {
        _bgmLevel = Math.Clamp(basisPoints, 0, FullLevel);
        ApplyBgmChannelLevel(_bgmLevel);
    }

    private void ApplyBgmChannelLevel(int basisPoints)
    {
        if (basisPoints == _bgmChannelLevel) return;
        _bgmChannelLevel = basisPoints;
        if (_bgmChannelLoaded) _device.SetVolume(BgmChannel, NativeAudioVolume.ToDecibels(basisPoints));
    }

    // Ops 0xb5/0xba on channel 11 (sound_buffer_start rewinds a buffer that has played): the track the channel holds
    // plays again from its start, once or looping by the op's start mode, at the channel's level, also after a BGM stop.
    // The retained start mode, which the Music route restarts the track with, stays.
    private void StartBgmChannel(int startMode)
    {
        if (_bgmChannelClip is not { } clip) return;
        _timeline?.Event("sfx-start", new() { ["channel"] = BgmChannel, ["start_mode"] = startMode,
            ["file"] = clip.Name });
        if (!_bgmChannelLoaded)
        {
            _device.Load(BgmChannel, AudioBus.Music, clip);
            _bgmChannelLoaded = true;
        }
        PlayChannel(BgmChannel, loop: startMode != 0, NativeAudioVolume.ToDecibels(_bgmChannelLevel));
    }

    // voice_bgm_duck_begin@0x406de0: at a voice start, unless a fade owns the envelope, keep the level to restore and
    // drop to sound:MusicFadeOnVoicePlayingVolume × 100 basis points (0 reads as 100) at once.
    private void BeginDuck(int percent)
    {
        if (_fade != null) return;
        if (!_duckActive) _duckRestoreLevel = _bgmLevel;
        _duckActive = true;
        _duckReleaseArmed = false;
        int target = percent * 100;
        SetBgmLevel(target == 0 ? 100 : target);
        _timeline?.Event("bgm-duck", new() { ["level"] = _bgmLevel });
    }

    // Leaving the duck at once (a BGM stop, an op 0xc2 fade, the Music route): the saved level comes back.
    private void RestoreDuck()
    {
        if (!_duckActive) return;
        SetBgmLevel(_duckRestoreLevel);
        _duckActive = false;
        _duckReleaseArmed = false;
    }

    private void ArmDuckRelease()
    {
        if ((_scene.DuckControl & 0x10000) != 0) _scene.DuckControl = 0;
        if ((_scene.DuckControl & 1) != 0) return;
        _duckReleaseArmed = true;
    }

    // engine_main_tick@0x411e12: with BGM ducked, once the voice has ended, the release is armed, no fade owns the
    // envelope and op 0x1cf bit 0 is clear. So BGM stays ducked between lines until the next line arms the release.
    private bool DuckReleaseDue()
        => _duckActive && _duckReleaseArmed && _fade == null && (_scene.DuckControl & 1) == 0 && !_playing[VoiceChannel];

    // The release swaps run-state 0x4000 for a 0x200 fade to full (10000, not the level saved at the duck), which a
    // new voice does not duck while it runs.
    private void ServiceDuckRelease()
    {
        if (!DuckReleaseDue()) return;
        _duckActive = false;
        _duckReleaseArmed = false;
        _timeline?.Event("bgm-duck-release", new() { ["level"] = _bgmLevel });
        _fade = new BgmFade(_bgmLevel, FullLevel, DuckReleaseSteps, _clock.NowMs, DuckReleaseMs, Script: false);
    }
}

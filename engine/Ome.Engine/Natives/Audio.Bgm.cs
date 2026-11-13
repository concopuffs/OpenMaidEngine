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

    private long _bgmTrack;                  // EngineCtx.current_bgm_track_id: op 0xc0 and numbered saves
    private bool _bgmLoaded;                 // channel 11 holds a track
    private bool _bgmLoop;
    private int _bgmLevel = FullLevel;       // bgm object +0x420: the committed envelope level, basis points
    private int _bgmChannelLevel = FullLevel;   // the level channel 11 was last given (a fade step is not committed)
    private BgmFade? _fade;
    private int _duckControl;                // op 0x1cf: bit 0 suppresses the duck and its release; 0x10000 one-shot
    private bool _duckActive;                // run-state 0x4000
    private int _duckRestoreLevel = FullLevel;
    private bool _duckReleaseArmed;          // voice_bgm_duck_release_armed (op 0x1bf, a delayed voice start)

    /// <summary>Op 0xbf. An identical request keeps the stream and its position: FIELD reasserts the stage BGM after
    /// every unit's action (docs/engine-re.md, "Forced BGM lifecycle siblings").</summary>
    public void PlayBgm(long trackId)
    {
        CompleteScriptFade();
        if (trackId != _bgmTrack)
        {
            _bgmTrack = trackId;
            StartBgm(trackId, startMode: 1, forced: false);
        }
        UpdateTimedWork();
    }

    /// <summary>Ops 0xb7 (start mode 1, loop) and 0xb9 (0, once): start a track even if it is the current one; zero
    /// restarts the retained track, and with none stops.</summary>
    public void RestartBgm(long trackId, int startMode)
    {
        CompleteScriptFade();
        if (trackId != 0) _bgmTrack = trackId;
        if (_bgmTrack == 0) StopBgmLocked();
        else StartBgm(_bgmTrack, startMode, forced: true);
        UpdateTimedWork();
    }

    /// <summary>Op 0xb8: release the source and clear the retained track (bgm_stop_release@0x464690).</summary>
    public void StopBgm()
    {
        CompleteScriptFade();
        _bgmTrack = 0;
        StopBgmLocked();
        UpdateTimedWork();
    }

    /// <summary>Op 0xc0: the retained track. A fade to zero that has run its course has cleared it.</summary>
    public long CurrentBgmTrack
    {
        get
        {
            ServiceFade(_clock.NowMs);
            UpdateTimedWork();
            return _bgmTrack;
        }
    }

    /// <summary>Op 0xc2: fade the BGM envelope to a level in basis points (CONFIG passes the setting × 100) and return.
    /// The tick runs the fade while the script goes on; at its end the level is committed, and a zero target releases
    /// the source and clears the retained track (bgm_fade_tick@0x464960). Under ADV fast-forward native applies the
    /// endpoint at once; an advance click sets no fast-forward, so it keeps the authored length.</summary>
    public void FadeBgm(int targetBasisPoints, long durationMs)
    {
        long ms = _skip.FastForwardActive ? 0 : Math.Clamp(durationMs, 0, 60_000);
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
        _duckControl = unchecked((int)flags);
        _timeline?.State("voice-bgm-duck-control", new() { ["flags"] = flags });
        UpdateTimedWork();
    }

    private void StartBgm(long trackId, int startMode, bool forced)
    {
        AudioClip? clip = _assets.Bgm(trackId);
        _timeline?.Event("bgm", new()
        {
            ["id"] = trackId,
            ["file"] = clip?.Name,
            ["start_mode"] = startMode,
            ["force_restart"] = forced,
        });
        if (clip == null) return;   // the track that played goes on
        _fade = null;
        _bgmLevel = FullLevel;
        _bgmChannelLevel = FullLevel;
        _bgmLoaded = true;
        _bgmLoop = startMode != 0;
        _device.Load(BgmChannel, AudioBus.Music, clip);
        PlayChannel(BgmChannel, _bgmLoop, NativeAudioVolume.ToDecibels(FullLevel));
    }

    private void StopBgmLocked()
    {
        _timeline?.Event("bgm-stop", new());
        ReleaseBgmSource();
    }

    // bgm_stop_release: the source goes, and the envelope starts again at full.
    private void ReleaseBgmSource()
    {
        RestoreDuck();
        _fade = null;
        _bgmLoaded = false;
        UnloadChannel(BgmChannel);
        _bgmLevel = FullLevel;
        _bgmChannelLevel = FullLevel;
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
            _bgmTrack = 0;
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
        if (_bgmLoaded) _device.SetVolume(BgmChannel, NativeAudioVolume.ToDecibels(basisPoints));
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
        if ((_duckControl & 0x10000) != 0) _duckControl = 0;
        if ((_duckControl & 1) != 0) return;
        _duckReleaseArmed = true;
    }

    // engine_main_tick@0x411e12: with BGM ducked, once the voice has ended, the release is armed, no fade owns the
    // envelope and op 0x1cf bit 0 is clear. So BGM stays ducked between lines until the next line arms the release.
    private bool DuckReleaseDue()
        => _duckActive && _duckReleaseArmed && _fade == null && (_duckControl & 1) == 0 && !_playing[VoiceChannel];

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

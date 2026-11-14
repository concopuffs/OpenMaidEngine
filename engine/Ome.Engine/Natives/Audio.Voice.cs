using Ome.Engine.Devices;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

public sealed partial class Audio
{
    // voice_mute_next_flags (ctx+0x1493c): op 0x1d5 requests (bit 0), the next op 0xc4 promotes it (0x10001) and plays
    // muted, and a promoted request belongs to that earlier voice, so the voice after it clears it.
    private const int VoiceMuteRequested = 0x1;
    private const int VoiceMutePromoted = 0x10000;

    private sealed record VoiceStart(AudioClip Clip, int PlaybackVariant, bool Muted);
    private sealed record DelayedVoice(AudioClip Clip, int PlaybackVariant, uint DelayMs, uint? StartMs);

    private int _voicePlaybackVariant;       // kept in channel 12's sound-buffer state by voice_play_indexed_asset

    /// <summary>The playback variant channel 12 last started with: 0 for a line (op 0xc4), 1 for a History replay
    /// (op 0x1bd). Its audible meaning is unproven; the port only keeps it.</summary>
    public int VoicePlaybackVariant
    {
        get { return _voicePlaybackVariant; }
    }

    /// <summary>Op 0xc4: a line's voice. A pending op 0x1d5 request is promoted and this voice plays muted, on channel
    /// 12 at volume 0, so its completion keeps Auto's timing.</summary>
    public void PlayVoice(long voiceId)
    {
        if ((_scene.VoiceMuteFlags & VoiceMutePromoted) != 0) _scene.VoiceMuteFlags = 0;
        else if ((_scene.VoiceMuteFlags & VoiceMuteRequested) != 0) _scene.VoiceMuteFlags |= VoiceMutePromoted;
        StartVoice(voiceId, 0, muted: (_scene.VoiceMuteFlags & VoiceMuteRequested) != 0);
        UpdateTimedWork();
    }

    /// <summary>Op 0x1bd: a voice replayed from History, with playback variant 1. It plays audibly and consumes a
    /// pending op 0x1d5 request.</summary>
    public void PlayHistoryVoice(long voiceId)
    {
        if ((_scene.VoiceMuteFlags & (VoiceMuteRequested | VoiceMutePromoted)) != 0) _scene.VoiceMuteFlags = 0;
        StartVoice(voiceId, 1, muted: false);
        UpdateTimedWork();
    }

    /// <summary>Op 0x1d5: the next op 0xc4 voice plays at volume 0.</summary>
    public void MuteNextVoice()
    {
        _scene.VoiceMuteFlags = VoiceMuteRequested;
    }

    /// <summary>Op 0x1bc's audio part: stop channel 12, drop the voice fast-forward held back and clear the op 0x1d5
    /// request.</summary>
    public void ResetMessageVoiceState()
    {
        _scene.HeldVoice = null;
        _scene.VoiceMuteFlags = 0;
        _timeline?.Event("voice-stop", new());
        StopChannel(VoiceChannel);
        UpdateTimedWork();
    }

    /// <summary>Op 0x2c0: replace the pending delayed voice. The delay counts from the first tick outside a statement
    /// block (voice_tick_delayed_playback@0x4884d0), and the start arms the duck release as op 0x1bf does.</summary>
    public void ScheduleVoicePlayback(long voiceId, int playbackVariant, long delayMs)
    {
        AudioClip? clip = _assets.Voice(voiceId);
        _timeline?.Event("voice-scheduled", new()
        {
            ["id"] = voiceId, ["file"] = clip?.Name, ["playback_variant"] = playbackVariant,
            ["delay_ms"] = unchecked((uint)delayMs),
        });
        _scene.DelayedVoice = clip == null ? null : new DelayedVoice(clip, playbackVariant, unchecked((uint)delayMs), null);
        UpdateTimedWork();
    }

    /// <summary>Fast-forward ended: play the voice it held back (<see cref="IFastForwardListener"/>).</summary>
    void IFastForwardListener.FastForwardChannelSet(FastForwardChannel channel, bool value, bool active, bool changed)
    {
        if (!changed || active) return;
        if (_scene.HeldVoice is not { } held) return;
        _scene.HeldVoice = null;
        DispatchVoice(held.Clip, held.PlaybackVariant, held.Muted);
        UpdateTimedWork();
    }

    private void StartVoice(long voiceId, int playbackVariant, bool muted)
    {
        AudioClip? clip = _assets.Voice(voiceId);
        _timeline?.Event("voice", new() { ["id"] = voiceId, ["file"] = clip?.Name,
            ["playback_variant"] = playbackVariant, ["muted"] = muted });
        if (clip == null) return;
        var start = new VoiceStart(clip, playbackVariant, muted);
        // A voice requested while fast-forward runs is held and played by the notice of its end; otherwise it plays
        // now.
        bool first = false;
        if (_skip.WhileFastForward(() => { first = _scene.HeldVoice == null; _scene.HeldVoice = start; }))
        {
            if (first) StopChannel(VoiceChannel);
            return;
        }
        DispatchVoice(clip, playbackVariant, muted);
    }

    private void DispatchVoice(AudioClip clip, int playbackVariant, bool muted)
    {
        // voice_bgm_duck_begin reads sound:MusicFadeOnVoicePlaying and its volume (percent) at each voice start; the
        // 設定 dialog's ｻｳﾝﾄﾞ page sets both. It ducks whenever op 0x1cf bit 0 is clear, muted voices included; a
        // suppressed start leaves an earlier duck in place.
        (bool duckOnVoice, int duckPercent) = DuckSettings();
        if (duckOnVoice && (_scene.DuckControl & 1) == 0) BeginDuck(duckPercent);
        _voicePlaybackVariant = playbackVariant;
        _device.Load(VoiceChannel, AudioBus.Voice, clip);
        PlayChannel(VoiceChannel, loop: false,
            muted ? NativeAudioVolume.ToDecibels(0) : NativeAudioVolume.ToDecibels(FullLevel));
    }

    /// <summary>sound:MusicFadeOnVoicePlaying (registered default 1) and sound:MusicFadeOnVoicePlayingVolume (50).</summary>
    private (bool Enabled, int Percent) DuckSettings()
    {
        if (_settings == null) return (true, 50);
        try
        {
            return (_settings.LoadInt("sound", "MusicFadeOnVoicePlaying", 1) != 0,
                    _settings.LoadInt("sound", "MusicFadeOnVoicePlayingVolume", 50));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return (true, 50);
        }
    }

    private void ServiceDelayedVoice(long now)
    {
        if (_scene.DelayedVoice is not { } pending) return;
        uint now32 = unchecked((uint)now);
        if (!pending.StartMs.HasValue)
        {
            _scene.DelayedVoice = pending with { StartMs = now32 };
            return;
        }
        if (unchecked(now32 - pending.StartMs.Value) < pending.DelayMs) return;
        _scene.DelayedVoice = null;
        _timeline?.Event("voice-scheduled-start", new()
        {
            ["file"] = pending.Clip.Name, ["playback_variant"] = pending.PlaybackVariant,
        });
        DispatchVoice(pending.Clip, pending.PlaybackVariant, false);
        ArmDuckRelease();   // native tick 0x411aae repeats op 0x1bf after a delayed start
    }
}

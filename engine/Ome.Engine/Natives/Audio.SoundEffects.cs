using Ome.Engine.Devices;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

public sealed partial class Audio
{
    // sfx_set_delay@0x482720's per-channel slot: the start mode and when the start is due.
    private readonly record struct DelayedStart(int StartMode, long DueMs);

    private readonly string?[] _soundEffectNames = new string?[SoundEffectChannelCount];
    private readonly long[] _soundEffectResourceIds = new long[SoundEffectChannelCount];   // numbered saves

    /// <summary>Op 0xb4: load a sound effect on a channel (0..9), replacing what it held.</summary>
    public void LoadSoundEffect(long resourceId, int channel)
    {
        LoadSoundEffectChannel(resourceId, channel);
        UpdateTimedWork();
    }

    /// <summary>Ops 0xb5 (start mode 0, once) and 0xba (1, the decoder rewinds at the end): start a channel that holds
    /// a sound (sfx_channel_start@0x4825d0, then sound_manager_start_channel@0x482f00; 4.33G the same). Nothing starts
    /// while the sound effect route is off. The manager's channels go to 12: channel 11 is the BGM's and 12 the voice's,
    /// whose sound plays again from its start; channel 10 never holds one.</summary>
    public void StartSoundEffect(int channel, int startMode)
    {
        if (!SoundEffectRouteOn()) return;
        switch (channel)
        {
            case BgmChannel:
                StartBgmChannel(startMode);
                return;
            case VoiceChannel:
                StartVoiceChannel(startMode);
                return;
        }
        if (!IsSoundEffectChannel(channel) || _soundEffectNames[channel] == null) return;
        _timeline?.Event("sfx-start", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["file"] = _soundEffectNames[channel] });
        PlayChannel(channel, loop: startMode != 0, NativeAudioVolume.ToDecibels(FullLevel));
    }

    /// <summary>Op 0x2bf, the facade's SetDelay: start a loaded channel after a delay. One start waits per channel; a
    /// new one replaces it, and loading or releasing the channel cancels it.</summary>
    public void ScheduleSoundEffectStart(int channel, int startMode, long delayMs)
    {
        if (!IsSoundEffectChannel(channel) || _soundEffectNames[channel] == null) return;
        long ms = Math.Clamp(delayMs, 0, 60_000);
        _timeline?.Event("sfx-start-scheduled", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["delay_ms"] = ms, ["file"] = _soundEffectNames[channel] });
        _scene.DelayedStarts[channel] = new DelayedStart(startMode, _clock.NowMs + ms);
        UpdateTimedWork();
    }

    /// <summary>Op 0xb6: stop a channel and drop its sound.</summary>
    public void ReleaseSoundEffect(int channel)
    {
        ReleaseSoundEffectChannel(channel);
        UpdateTimedWork();
    }

    /// <summary>Whether a channel of the sound manager holds a sound: a sound effect's (0..9), the BGM's (11, the track
    /// last played) or the voice's (12, the last voice). Ops 0xb5 and 0xba trace a start of one that does not.</summary>
    public bool ChannelHoldsSound(int channel) => channel switch
    {
        BgmChannel => _bgmChannelClip != null,
        VoiceChannel => _voiceLoaded,
        _ => IsSoundEffectChannel(channel) && _soundEffectNames[channel] != null,
    };

    // sfx_channel_start@0x4825d0 starts nothing while the facade's route flag (+0x414, ctx+0x14438, which
    // sound_set_sfx_route_enabled writes) is clear: an op 0xb5 or 0xba start, and a delayed start falling due.
    private bool SoundEffectRouteOn()
        => !_mixer.TryGetRouteEnabled((int)AudioMixerCategory.SoundEffect, out bool on) || on;

    /// <summary>The name of the sound effect a resource id names, as an error box gives it (the id when it names
    /// none).</summary>
    public string SoundEffectName(long resourceId)
        => _assets.SoundEffect(resourceId)?.Name
           ?? resourceId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The resource each sound effect channel holds, for a numbered save (0 for none).</summary>
    public IReadOnlyList<long> SoundEffectResourceIds
    {
        get { return _soundEffectResourceIds.ToArray(); }
    }

    private void LoadSoundEffectChannel(long resourceId, int channel)
    {
        if (!IsSoundEffectChannel(channel)) return;
        _soundEffectResourceIds[channel] = resourceId;
        AudioClip? clip = _assets.SoundEffect(resourceId);
        _soundEffectNames[channel] = clip?.Name;
        _scene.DelayedStarts[channel] = null;
        _timeline?.Event("sfx-load", new() { ["resource"] = resourceId, ["channel"] = channel, ["file"] = clip?.Name });
        if (clip == null) return;
        _playing[channel] = false;
        _device.Load(channel, AudioBus.SoundEffect, clip);
    }

    private void ReleaseSoundEffectChannel(int channel)
    {
        if (!IsSoundEffectChannel(channel)) return;
        _soundEffectResourceIds[channel] = 0;
        _timeline?.Event("sfx-release", new() { ["channel"] = channel, ["file"] = _soundEffectNames[channel] });
        _soundEffectNames[channel] = null;
        _scene.DelayedStarts[channel] = null;
        UnloadChannel(channel);
    }

    private void ServiceDelayedSoundEffects(long now)
    {
        for (int channel = 0; channel < SoundEffectChannelCount; channel++)
        {
            if (_scene.DelayedStarts[channel] is not { } start || now < start.DueMs) continue;
            _scene.DelayedStarts[channel] = null;
            // sfx_tick_delayed_starts@0x4827e0 clears the request before sfx_channel_start, so with the route off it
            // is used up without playing.
            if (_soundEffectNames[channel] == null || !SoundEffectRouteOn()) continue;
            _timeline?.Event("sfx-scheduled-start", new() { ["channel"] = channel,
                ["start_mode"] = start.StartMode, ["file"] = _soundEffectNames[channel] });
            PlayChannel(channel, loop: start.StartMode != 0, NativeAudioVolume.ToDecibels(FullLevel));
        }
    }

    private bool HasDelayedSoundEffect()
    {
        foreach (DelayedStart? start in _scene.DelayedStarts)
            if (start.HasValue) return true;
        return false;
    }

    private static bool IsSoundEffectChannel(int channel) => (uint)channel < SoundEffectChannelCount;
}

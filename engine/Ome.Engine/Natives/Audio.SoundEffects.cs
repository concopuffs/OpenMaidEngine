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
        LoadSoundEffectLocked(resourceId, channel);
        UpdateTimedWork();
    }

    /// <summary>Ops 0xb5 (start mode 0, once) and 0xba (1, the decoder rewinds at the end): start a loaded
    /// channel.</summary>
    public void StartSoundEffect(int channel, int startMode)
    {
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
        ReleaseSoundEffectLocked(channel);
        UpdateTimedWork();
    }

    /// <summary>The resource each sound effect channel holds, for a numbered save (0 for none).</summary>
    public IReadOnlyList<long> SoundEffectResourceIds
    {
        get { return _soundEffectResourceIds.ToArray(); }
    }

    private void LoadSoundEffectLocked(long resourceId, int channel)
    {
        if (!IsSoundEffectChannel(channel)) return;
        _soundEffectResourceIds[channel] = resourceId;
        AudioClip? clip = _assets.SoundEffect(resourceId);
        _soundEffectNames[channel] = clip?.Name;
        _scene.DelayedStarts[channel] = null;
        _timeline?.Event("sfx-load", new() { ["resource"] = resourceId, ["channel"] = channel, ["file"] = clip?.Name });
        if (clip == null) return;
        Volatile.Write(ref _playing[channel], false);
        _device.Load(channel, AudioBus.SoundEffect, clip);
    }

    private void ReleaseSoundEffectLocked(int channel)
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
            if (_soundEffectNames[channel] == null) continue;
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

    private void CancelDelayedSoundEffects() => Array.Clear(_scene.DelayedStarts);

    private static bool IsSoundEffectChannel(int channel) => (uint)channel < SoundEffectChannelCount;
}

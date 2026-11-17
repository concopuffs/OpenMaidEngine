using Ome.Engine.Devices;
using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

public sealed partial class Audio
{
    /// <summary>Op 0xc5 and the options panel: a category's stored volume in basis points (-1: never configured).
    /// False for a category that does not exist.</summary>
    public bool TryGetVolume(int category, out int basisPoints) => _mixer.TryGetVolume(category, out basisPoints);

    /// <summary>Op 0xc6 and the options panel: store a category's volume and apply it. False for a category that does
    /// not exist.</summary>
    public bool TrySetVolume(int category, long basisPoints)
    {
        if (!_mixer.TrySetVolume(category, basisPoints)) return false;
        int level = checked((int)basisPoints);
        _timeline?.State("audio-volume", new() { ["category"] = category, ["basis_points"] = level });
        ApplyBusVolume(category, level);
        return true;
    }

    /// <summary>The options panel's preview: apply a level to a category without storing it.</summary>
    public void PreviewVolume(int category, int basisPoints)
    {
        ApplyBusVolume(category, basisPoints);
    }

    /// <summary>Op 0xc7 and the options panel: whether a category's route is on. False for a category without one
    /// (master has none).</summary>
    public bool TryGetRouteEnabled(int category, out bool enabled) => _mixer.TryGetRouteEnabled(category, out enabled);

    /// <summary>Op 0x1ba and the options panel: turn a category's route on or off. Setting the value it has does
    /// nothing, as native's workers do. False for a category without one.</summary>
    public bool TrySetRouteEnabled(int category, bool enabled)
    {
        if (!_mixer.TrySetRouteEnabled(category, enabled, out bool changed)) return false;
        if (!changed) return true;
        _timeline?.State("audio-route", new() { ["category"] = category, ["enabled"] = enabled });
        ApplyRoute(category, enabled);
        UpdateTimedWork();
        return true;
    }

    private void ApplyMixer(AudioMixerSettingsSnapshot snapshot)
    {
        for (int category = 0; category < AudioMixerSettings.CategoryCount; category++)
            ApplyBusVolume(category, snapshot.Volumes[category]);
        for (int category = (int)AudioMixerCategory.Music; category < AudioMixerSettings.CategoryCount; category++)
            if (BusOf(category) is { } bus) _device.SetBusMuted(bus, !snapshot.Routes[category]);
    }

    private void ApplyBusVolume(int category, int basisPoints)
    {
        if (BusOf(category) is not { } bus) return;
        // -1 is native's unconfigured sentinel: the sound object's level stays at full.
        _device.SetBusVolume(bus, NativeAudioVolume.ToDecibels(basisPoints < 0 ? FullLevel : basisPoints));
    }

    private void ApplyRoute(int category, bool enabled)
    {
        switch ((AudioMixerCategory)category)
        {
            case AudioMixerCategory.Music:
                RestoreDuck();
                // A running op 0xc2 fade goes on: native's tick advances it whatever the route, so a fade to zero still
                // releases the source when it ends. Turning the route on starts the track that was loaded again.
                if (!enabled) StopChannel(BgmChannel);
                else if (_bgmLoaded)
                    PlayChannel(BgmChannel, _bgmLoop, NativeAudioVolume.ToDecibels(_bgmChannelLevel));
                break;
            case AudioMixerCategory.SoundEffect when !enabled:
                // sound_set_sfx_route_enabled@0x408010 stops channels 0..9 and leaves their delayed starts, which the
                // tick uses up without playing while the route stays off.
                for (int channel = 0; channel < SoundEffectChannelCount; channel++) StopChannel(channel);
                break;
            case AudioMixerCategory.Voice when !enabled:
                StopChannel(VoiceChannel);
                break;
        }
        if (BusOf(category) is { } bus) _device.SetBusMuted(bus, !enabled);
    }

    private static AudioBus? BusOf(int category) => category switch
    {
        (int)AudioMixerCategory.Master => AudioBus.Master,
        (int)AudioMixerCategory.Music => AudioBus.Music,
        (int)AudioMixerCategory.SoundEffect => AudioBus.SoundEffect,
        (int)AudioMixerCategory.Voice => AudioBus.Voice,
        (int)AudioMixerCategory.Movie => AudioBus.Movie,
        _ => null,
    };

    // ---- numbered saves ----

    /// <summary>The audio records of a numbered save: the BGM track (a fade to zero that has ended has cleared it) and
    /// each sound-effect channel's loaded sound.</summary>
    public NativeNumberedSaveState WriteSaveRecords(NativeNumberedSaveState state)
        => state with
        {
            BgmTrackId = unchecked((int)CurrentBgmTrack),
            SoundEffectResourceIds = SoundEffectResourceIds.Select(id => unchecked((int)id)).ToArray(),
        };

    /// <summary>A numbered load's audio records (<see cref="RestoreSaved"/>).</summary>
    public void ReadSaveRecords(NativeNumberedSaveState state)
        => RestoreSaved(unchecked((uint)state.BgmTrackId),
                        state.SoundEffectResourceIds.Select(id => (long)unchecked((uint)id)).ToArray());

    /// <summary>A numbered load's audio part (save_data_deserialize_and_begin_restore): a running op 0xc2 fade ends
    /// first, so it cannot clear the restored track; the saved track starts (no track releases the source), and each
    /// channel is released and loaded with its saved sound.</summary>
    public void RestoreSaved(long bgmTrack, IReadOnlyList<long> soundEffectResourceIds)
    {
        ArgumentNullException.ThrowIfNull(soundEffectResourceIds);
        CompleteScriptFade();
        _bgmTrack = bgmTrack;
        if (_bgmTrack == 0)
        {
            _timeline?.Event("bgm-fade", new() { ["target_basis_points"] = 0, ["duration_ms"] = 0L });
            RestoreDuck();
            _fade = null;
            ReleaseBgmSource();
        }
        else StartBgm(_bgmTrack, startMode: 1, forced: false);
        for (int channel = 0; channel < SoundEffectChannelCount; channel++)
        {
            ReleaseSoundEffectChannel(channel);
            long resourceId = channel < soundEffectResourceIds.Count ? soundEffectResourceIds[channel] : 0;
            if (resourceId != 0) LoadSoundEffectChannel(resourceId, channel);
        }
        UpdateTimedWork();
    }
}

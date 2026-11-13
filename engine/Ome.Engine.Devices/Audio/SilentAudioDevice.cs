using System.Collections.Concurrent;

namespace Ome.Engine.Devices;

/// <summary>The headless audio device (spec §6.5): it plays nothing, and every play ends at once, so nothing is ever
/// heard playing.</summary>
public sealed class SilentAudioDevice : IAudioDevice
{
    private readonly ConcurrentQueue<AudioChannelEnded> _ended = new();

    public void Load(int channel, AudioBus bus, AudioClip clip) { }

    public void Play(int channel, bool loop, float volumeDb, int playId)
        => _ended.Enqueue(new AudioChannelEnded(channel, playId));

    public void Stop(int channel) { }

    public void Unload(int channel) { }

    public void SetVolume(int channel, float volumeDb) { }

    public void SetBusVolume(AudioBus bus, float volumeDb) { }

    public void SetBusMuted(AudioBus bus, bool muted) { }

    public bool TryTakeEnded(out AudioChannelEnded ended) => _ended.TryDequeue(out ended);
}

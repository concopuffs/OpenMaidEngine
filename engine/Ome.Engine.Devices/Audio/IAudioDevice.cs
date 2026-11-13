namespace Ome.Engine.Devices;

/// <summary>The mixer bus a channel plays through. Each has a volume and a mute of its own.</summary>
public enum AudioBus { Master, Music, SoundEffect, Voice, Movie }

/// <summary>How a clip's bytes are encoded.</summary>
public enum AudioEncoding { OggVorbis, Wave }

/// <summary>One sound as the game stores it: the file's name and bytes. The device decodes it.</summary>
public sealed record AudioClip(string Name, byte[] Bytes, AudioEncoding Encoding);

/// <summary>A device's report that a channel's playback ended: it ran to its end, or its clip could not be decoded.
/// <see cref="PlayId"/> is the id of the <see cref="IAudioDevice.Play"/> call that started it.</summary>
public readonly record struct AudioChannelEnded(int Channel, int PlayId);

/// <summary>
/// Sound output: numbered channels that each hold one clip and play it through a bus (engine layering spec §6.1).
/// <para>The contract of spec §6.2. The engine calls the device from its own thread. Every call except
/// <see cref="TryTakeEnded"/> is a command: the device applies it when it can, in the order sent, and the caller does
/// not depend on when. The device reports by queuing <see cref="AudioChannelEnded"/> events, which the caller takes
/// with <see cref="TryTakeEnded"/>. The device holds no state the caller reads back: levels, fades and whether a
/// channel plays are the caller's.</para>
/// </summary>
public interface IAudioDevice
{
    /// <summary>Put <paramref name="clip"/> on a channel that plays through <paramref name="bus"/>, stopping and
    /// replacing what the channel held.</summary>
    void Load(int channel, AudioBus bus, AudioClip clip);

    /// <summary>Play the channel's clip from its start, at <paramref name="volumeDb"/>, looping or once. A channel
    /// already playing starts again. If the channel holds no clip, or its clip cannot be decoded, the device reports
    /// the play ended at once.</summary>
    void Play(int channel, bool loop, float volumeDb, int playId);

    /// <summary>Stop the channel and keep its clip. A stopped play is not reported as ended.</summary>
    void Stop(int channel);

    /// <summary>Stop the channel and drop its clip.</summary>
    void Unload(int channel);

    /// <summary>Set the channel's volume; it lasts until the next <see cref="Play"/>, which sets its own.</summary>
    void SetVolume(int channel, float volumeDb);

    void SetBusVolume(AudioBus bus, float volumeDb);

    void SetBusMuted(AudioBus bus, bool muted);

    /// <summary>Take the oldest queued report, if any. Answers at once and changes nothing else.</summary>
    bool TryTakeEnded(out AudioChannelEnded ended);
}

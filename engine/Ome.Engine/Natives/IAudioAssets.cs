using Ome.Engine.Devices;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>Where the audio native finds a sound: native's three resolvers (docs/asset-resolution-re.md). Each answers
/// null for a sound the game does not have or that cannot be read.</summary>
public interface IAudioAssets
{
    /// <summary>A BGM track by its direct-name number (op 0xbf: BGM005.OGG for 5).</summary>
    AudioClip? Bgm(long trackId);

    /// <summary>A voice by its packed catalog id (ops 0xc4, 0x1bd and 0x2c0).</summary>
    AudioClip? Voice(long voiceId);

    /// <summary>A sound effect by its packed resource id (op 0xb4).</summary>
    AudioClip? SoundEffect(long resourceId);
}

/// <summary>No sounds: every lookup answers null.</summary>
public sealed class NoAudioAssets : IAudioAssets
{
    public static readonly NoAudioAssets Instance = new();

    public AudioClip? Bgm(long trackId) => null;
    public AudioClip? Voice(long voiceId) => null;
    public AudioClip? SoundEffect(long resourceId) => null;
}

/// <summary>A game's sounds through its <see cref="ResourceMap"/>. A file that cannot be read is reported through
/// <paramref name="warn"/> and answers null.</summary>
public sealed class ResourceAudioAssets(ResourceMap resources, Action<string>? warn = null) : IAudioAssets
{
    public AudioClip? Bgm(long trackId) => Read(resources.ResolveBgm(trackId));

    public AudioClip? Voice(long voiceId) => Read(resources.ResolveVoice(voiceId));

    public AudioClip? SoundEffect(long resourceId) => Read(resources.ResolveSoundEffect(resourceId));

    private AudioClip? Read(AssetEntry? asset)
    {
        if (asset == null) return null;
        try
        {
            AudioPayload payload = resources.ReadAudio(asset);
            return new AudioClip(payload.Name, payload.Bytes, EncodingOf(payload.Name));
        }
        catch (Exception e)
        {
            warn?.Invoke($"audio read failed {asset.Name}: {e.Message}");
            return null;
        }
    }

    // BGM and voices are Ogg Vorbis; a sound effect is a RIFF wave or an Ogg file (ResolveSoundEffect filters to those).
    private static AudioEncoding EncodingOf(string name)
        => name.EndsWith(".OGG", StringComparison.OrdinalIgnoreCase) ? AudioEncoding.OggVorbis : AudioEncoding.Wave;
}

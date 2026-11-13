using System;
using System.Collections.Concurrent;
using System.IO;
using Godot;
using Ome.Engine.Devices;
using Ome.Engine.Model;
using Ome.Engine.Persistence;

/// <summary>
/// The Godot audio device (engine layering spec §6.1, milestone 6): one stream player per channel of the audio native
/// (sound effects 0..9, BGM 11, voice 12) and Godot's buses. It only plays what it is told: the BGM envelope, the voice
/// duck, delayed starts and whether a channel plays are the native's (<c>Ome.Engine.Natives.Audio</c>).
/// <para>The contract of spec §6.2. Each command is deferred to the main thread, in the order sent, as the host's
/// audio calls were before. Once a frame, before the engine thread is woken, a channel whose play ran out is reported
/// ended.</para>
/// </summary>
public partial class Main : IAudioDevice
{
    private const int AudioChannelCount = 13;
    private readonly AudioStreamPlayer[] _audioChannels = new AudioStreamPlayer[AudioChannelCount];
    private readonly int[] _audioChannelPlayIds = new int[AudioChannelCount];   // main thread: the play each runs, 0 none
    private readonly ConcurrentQueue<AudioChannelEnded> _audioEnded = new();
    private AudioMixerSettings _audioMixerSettings = null!;
    /// <summary>SYS4REG.INI, the save and settings native's (null without one: a self-test, read-only persistence).
    /// </summary>
    private Sys4RegIniStore? SettingsStore => _saves?.Settings;

    private void CreateAudioChannels()
    {
        EnsureAudioBuses();
        for (int channel = 0; channel < AudioChannelCount; channel++)
        {
            _audioChannels[channel] = new AudioStreamPlayer();
            AddChild(_audioChannels[channel]);
        }
    }

    void IAudioDevice.Load(int channel, AudioBus bus, AudioClip clip)
        => Callable.From(() => LoadAudioChannel(channel, bus, clip)).CallDeferred();

    void IAudioDevice.Play(int channel, bool loop, float volumeDb, int playId)
        => Callable.From(() => PlayAudioChannel(channel, loop, volumeDb, playId)).CallDeferred();

    void IAudioDevice.Stop(int channel) => Callable.From(() => StopAudioChannel(channel, unload: false)).CallDeferred();

    void IAudioDevice.Unload(int channel) => Callable.From(() => StopAudioChannel(channel, unload: true)).CallDeferred();

    void IAudioDevice.SetVolume(int channel, float volumeDb)
        => Callable.From(() => SetAudioChannelVolume(channel, volumeDb)).CallDeferred();

    void IAudioDevice.SetBusVolume(AudioBus bus, float volumeDb)
        => Callable.From(() => SetAudioBusVolume(bus, volumeDb)).CallDeferred();

    void IAudioDevice.SetBusMuted(AudioBus bus, bool muted)
        => Callable.From(() => SetAudioBusMuted(bus, muted)).CallDeferred();

    bool IAudioDevice.TryTakeEnded(out AudioChannelEnded ended) => _audioEnded.TryDequeue(out ended);

    private AudioStreamPlayer? AudioChannel(int channel)
        => (uint)channel < AudioChannelCount ? _audioChannels[channel] : null;

    // Decode the VFS-owned bytes. A wave goes through RiffWaveSanitizer, a Godot-only compatibility boundary: the VFS
    // and the engine keep the original bytes, and Godot sees an AGE-compatible first-RIFF copy without CP932 INFO
    // metadata.
    private void LoadAudioChannel(int channel, AudioBus bus, AudioClip clip)
    {
        if (AudioChannel(channel) is not { } player) return;
        _audioChannelPlayIds[channel] = 0;
        player.Stop();
        player.Stream = null;
        player.Bus = AudioBusName(bus);
        AudioStream? stream = clip.Encoding == AudioEncoding.OggVorbis
            ? AudioStreamOggVorbis.LoadFromBuffer(clip.Bytes)
            : AudioStreamWav.LoadFromBuffer(RiffWaveSanitizer.PrepareForGodot(clip.Bytes));
        if (stream == null) GD.Print($"{(clip.Encoding == AudioEncoding.OggVorbis ? "OGG" : "WAV")} load failed {clip.Name}");
        player.Stream = stream;
    }

    private void PlayAudioChannel(int channel, bool loop, float volumeDb, int playId)
    {
        if (AudioChannel(channel) is not { } player) return;
        if (player.Stream == null)
        {
            _audioChannelPlayIds[channel] = 0;
            _audioEnded.Enqueue(new AudioChannelEnded(channel, playId));
            return;
        }
        switch (player.Stream)
        {
            case AudioStreamOggVorbis ogg: ogg.Loop = loop; break;
            case AudioStreamWav wav:
                wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                break;
        }
        player.VolumeDb = volumeDb;
        player.Play();
        _audioChannelPlayIds[channel] = playId;
    }

    private void StopAudioChannel(int channel, bool unload)
    {
        if (AudioChannel(channel) is not { } player) return;
        _audioChannelPlayIds[channel] = 0;
        player.Stop();
        if (unload) player.Stream = null;
    }

    private void SetAudioChannelVolume(int channel, float volumeDb)
    {
        if (AudioChannel(channel) is { } player) player.VolumeDb = volumeDb;
    }

    // Main thread, once a frame before the frame turn: a play that ran out is reported, so the woken engine thread sees it.
    private void ReportEndedAudioChannels()
    {
        for (int channel = 0; channel < AudioChannelCount; channel++)
        {
            int playId = _audioChannelPlayIds[channel];
            if (playId == 0 || _audioChannels[channel].Playing) continue;
            _audioChannelPlayIds[channel] = 0;
            _audioEnded.Enqueue(new AudioChannelEnded(channel, playId));
        }
    }

    private static void SetAudioBusVolume(AudioBus bus, float volumeDb)
    {
        int index = AudioServer.GetBusIndex(AudioBusName(bus));
        if (index >= 0) AudioServer.SetBusVolumeDb(index, volumeDb);
    }

    private static void SetAudioBusMuted(AudioBus bus, bool muted)
    {
        int index = AudioServer.GetBusIndex(AudioBusName(bus));
        if (index >= 0) AudioServer.SetBusMute(index, muted);
    }

    internal static string AudioBusName(AudioBus bus) => bus switch
    {
        AudioBus.Master => "Master",
        AudioBus.Music => "Music",
        AudioBus.SoundEffect => "SFX",
        AudioBus.Voice => "Voice",
        AudioBus.Movie => "Movie",
        _ => "Master",
    };

    private static void EnsureAudioBuses()
    {
        foreach (string name in new[] { "Music", "SFX", "Voice", "Movie" })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, name);
        }
    }

    private void PersistAudioMixerSettings(AudioMixerSettingsSnapshot snapshot)
    {
        try
        {
            SettingsStore?.Save(snapshot);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            System.Console.Error.WriteLine(
                $"[settings] audio mixer save failed; runtime setting remains active: {error.Message}");
        }
    }
}

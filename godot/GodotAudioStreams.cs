using System;
using Godot;
using Ome.Engine.Devices;

/// <summary>
/// The Godot stream device (<see cref="IAudioStreamDevice"/>, engine layering milestone 8): each stream is an
/// <see cref="AudioStreamPlayer"/> over an <see cref="AudioStreamGenerator"/> on its bus. The movie native feeds it on
/// the main thread, in its frame service, so its members touch Godot directly.
/// </summary>
internal sealed class GodotAudioStreamDevice(Node owner, double outputLatencySeconds) : IAudioStreamDevice
{
    public IAudioStream Open(int sampleRate, AudioBus bus, bool muted)
        => new GodotAudioStream(owner, sampleRate, bus, muted, outputLatencySeconds);
}

/// <summary>One stream: a quarter second of buffer, silent at -80 dB when muted. What has been heard is the player's
/// position plus the time since the last mix, less the output's latency.</summary>
internal sealed class GodotAudioStream : IAudioStream
{
    private readonly AudioStreamPlayer _player;
    private readonly double _outputLatencySeconds;
    private AudioStreamGeneratorPlayback? _playback;
    private bool _disposed;

    public GodotAudioStream(Node owner, int sampleRate, AudioBus bus, bool muted, double outputLatencySeconds)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _outputLatencySeconds = Math.Max(0, outputLatencySeconds);
        var generator = new AudioStreamGenerator
        {
            MixRate = sampleRate,
            BufferLength = 0.25f,
        };
        _player = new AudioStreamPlayer
        {
            Stream = generator,
            Bus = Main.AudioBusName(bus),
            VolumeDb = muted ? -80.0f : 0.0f,
        };
        owner.AddChild(_player);
    }

    public void StartHeld()
    {
        _player.Play();
        _player.StreamPaused = true;
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback
                    ?? throw new InvalidOperationException("Godot did not create the stream's generator playback");
    }

    public void Resume() => _player.StreamPaused = false;

    public int FramesFree => _playback?.GetFramesAvailable() ?? 0;

    public bool Push(float[] interleaved, int offsetFrames, int frames)
    {
        var output = new Vector2[frames];
        for (int index = 0; index < frames; index++)
        {
            int source = checked((offsetFrames + index) * 2);
            output[index] = new Vector2(interleaved[source], interleaved[source + 1]);
        }
        return _playback!.PushBuffer(output);
    }

    public bool PushSilence(int frames) => _playback!.PushBuffer(new Vector2[frames]);

    public double HeardSeconds => _playback == null
        ? 0
        : _player.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - _outputLatencySeconds;

    public int Underruns => _playback?.GetSkips() ?? 0;

    public void SetRate(double rate) => _player.PitchScale = (float)rate;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player.Stop();
        _player.QueueFree();
    }
}

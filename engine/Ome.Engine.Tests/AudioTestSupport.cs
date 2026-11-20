using System.Globalization;
using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Ome.Engine.Vm;

/// <summary>An audio device that plays nothing and keeps every command, in order. A play runs until the test ends it
/// (<see cref="End"/>).</summary>
internal sealed class RecordingAudioDevice : IAudioDevice
{
    private readonly object _lock = new();
    private readonly Queue<AudioChannelEnded> _ended = new();
    private readonly Dictionary<int, string> _loaded = new();
    private readonly Dictionary<int, int> _playing = new();

    /// <summary>Every command as text: <c>load 11 Music bgm:24</c>, <c>play 11 bgm:24 loop 0.0dB</c>,
    /// <c>stop 12</c>, <c>unload 11</c>, <c>volume 11 -10.0dB</c>, <c>bus-volume Music -3.0dB</c>,
    /// <c>bus-mute Music True</c>.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Every play: the channel, the clip it held (null for none), loop, volume.</summary>
    public List<(int Channel, string? Clip, bool Loop, float VolumeDb)> Plays { get; } = [];

    public void Load(int channel, AudioBus bus, AudioClip clip)
    {
        lock (_lock)
        {
            _loaded[channel] = clip.Name;
            _playing.Remove(channel);
            Commands.Add($"load {channel} {bus} {clip.Name}");
        }
    }

    public void Play(int channel, bool loop, float volumeDb, int playId)
    {
        lock (_lock)
        {
            string? clip = _loaded.GetValueOrDefault(channel);
            Plays.Add((channel, clip, loop, volumeDb));
            Commands.Add($"play {channel} {clip ?? "-"} {(loop ? "loop" : "once")} {Db(volumeDb)}");
            if (clip == null) _ended.Enqueue(new AudioChannelEnded(channel, playId));
            else _playing[channel] = playId;
        }
    }

    public void Stop(int channel)
    {
        lock (_lock)
        {
            _playing.Remove(channel);
            Commands.Add($"stop {channel}");
        }
    }

    public void Unload(int channel)
    {
        lock (_lock)
        {
            _playing.Remove(channel);
            _loaded.Remove(channel);
            Commands.Add($"unload {channel}");
        }
    }

    public void SetPaused(int channel, bool paused)
    {
        lock (_lock) Commands.Add($"{(paused ? "pause" : "resume")} {channel}");
    }

    public void SetVolume(int channel, float volumeDb)
    {
        lock (_lock) Commands.Add($"volume {channel} {Db(volumeDb)}");
    }

    public void SetBusVolume(AudioBus bus, float volumeDb)
    {
        lock (_lock) Commands.Add($"bus-volume {bus} {Db(volumeDb)}");
    }

    public void SetBusMuted(AudioBus bus, bool muted)
    {
        lock (_lock) Commands.Add($"bus-mute {bus} {muted}");
    }

    public bool TryTakeEnded(out AudioChannelEnded ended)
    {
        lock (_lock) return _ended.TryDequeue(out ended);
    }

    /// <summary>Report that the channel's play ran out, as a device does.</summary>
    public void End(int channel)
    {
        lock (_lock)
            if (_playing.Remove(channel, out int playId)) _ended.Enqueue(new AudioChannelEnded(channel, playId));
    }

    public bool IsLoaded(int channel)
    {
        lock (_lock) return _loaded.ContainsKey(channel);
    }

    /// <summary>The clips played on a channel, in order.</summary>
    public List<string?> PlayedOn(int channel)
    {
        lock (_lock) return Plays.Where(play => play.Channel == channel).Select(play => play.Clip).ToList();
    }

    /// <summary>The ids of the BGM tracks played (from <see cref="TestAudioAssets"/>' names).</summary>
    public List<long> BgmTracks => PlayedOn(Audio.BgmChannel).Select(TestAudioAssets.IdOf).ToList();

    /// <summary>The voices played: id and whether at volume 0.</summary>
    public List<(long Id, bool Muted)> Voices
    {
        get
        {
            lock (_lock)
                return Plays.Where(play => play.Channel == Audio.VoiceChannel)
                    .Select(play => (TestAudioAssets.IdOf(play.Clip), play.VolumeDb <= NativeAudioVolume.ToDecibels(0)))
                    .ToList();
        }
    }

    /// <summary>The commands that start with <paramref name="prefix"/>.</summary>
    public List<string> CommandsStartingWith(string prefix)
    {
        lock (_lock) return Commands.Where(command => command.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    public static string Db(float volumeDb) => volumeDb.ToString("0.0", CultureInfo.InvariantCulture) + "dB";
}

/// <summary>Every sound exists, named for its kind and id (<c>bgm:24</c>, <c>voice:10</c>, <c>sfx:40</c>), except the
/// ids listed as missing.</summary>
internal sealed class TestAudioAssets(params long[] missing) : IAudioAssets
{
    public AudioClip? Bgm(long trackId) => Clip("bgm", trackId, AudioEncoding.OggVorbis);
    public AudioClip? Voice(long voiceId) => Clip("voice", voiceId, AudioEncoding.OggVorbis);
    public AudioClip? SoundEffect(long resourceId) => Clip("sfx", resourceId, AudioEncoding.Wave);

    private AudioClip? Clip(string kind, long id, AudioEncoding encoding)
        => missing.Contains(id) ? null : new AudioClip($"{kind}:{id}", [], encoding);

    public static long IdOf(string? clip)
        => clip == null ? -1 : long.Parse(clip[(clip.IndexOf(':') + 1)..], CultureInfo.InvariantCulture);
}

/// <summary>The audio native over a <see cref="RecordingAudioDevice"/> and a frame-stepped clock, with the skip native
/// and the tick that go with it, and a VM built over all of them.</summary>
internal sealed class AudioRig
{
    public AudioRig(AudioMixerSettings? mixer = null, Sys4RegIniStore? settings = null, IAudioAssets? assets = null,
                    SkipAndAuto? skip = null)
    {
        Skip = skip ?? SkipAndAuto.WithSystemClock();
        Mixer = mixer ?? new AudioMixerSettings();
        Audio = new Audio(Device, assets ?? new TestAudioAssets(), Clock, Skip, Mixer, settings);
        Tick = new EngineTick(Audio);
        Device.Commands.Clear();   // the mixer the native applied at construction
    }

    public RecordingAudioDevice Device { get; } = new();
    public FrameClock Clock { get; } = new();
    public SkipAndAuto Skip { get; }
    public AudioMixerSettings Mixer { get; }
    public Audio Audio { get; }
    public EngineTick Tick { get; }

    /// <summary>A VM over this rig. A <see cref="RecordingWaits"/>'s sleeps move the rig's clock.</summary>
    public VirtualMachine Vm(Script script, OpcodeTable table, IInputAndWaits host, VmOptions? options = null,
                             IScriptProvider? provider = null, AdvTextHistory? textHistory = null,
                             INativeDatStore? nativeDatStore = null)
    {
        if (host is RecordingWaits recording) recording.Clock = Clock;
        return new VirtualMachine(script, table, host, options, provider, textHistory: textHistory,
                                  saves: new SavesAndSettings(store: nativeDatStore), skipAndAuto: Skip, audio: Audio, tick: Tick);
    }

    /// <summary>Move the clock and run the tick, as a wait's wake does.</summary>
    public void Advance(long milliseconds)
    {
        Clock.Advance(milliseconds / 1000.0);
        Tick.Service();
    }
}

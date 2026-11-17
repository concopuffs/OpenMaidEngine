using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

/// <summary>
/// The audio native (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.2, milestone 6): native's sound
/// manager of 13 channels (sound effects 0..9, BGM 11, voice 12) and the engine state around it. It owns the retained
/// BGM track and the BGM envelope (the op 0xc2 fade and the voice duck with its release, run-state 0x200 and 0x4000),
/// the voice that fast-forward holds back, the mute request of op 0x1d5, delayed starts (ops 0x2bf and 0x2c0) and the
/// mixer levels. Handlers call it with operands; it drives an <see cref="IAudioDevice"/> with commands, and the engine
/// tick (<see cref="EngineTick"/>) runs its timed work in <see cref="Service"/>.
/// </summary>
/// <remarks>
/// <para>The device contract of spec §6.2. The native holds every fact it answers: the level, the fade and its end,
/// and whether a channel plays, which it sets when it starts or stops a channel and clears when the device reports the
/// play ended. So no query waits on the device, and no device lag can disagree with it.</para>
/// <para>Time is the engine's <see cref="FrameClock"/>, so a fade, a delay and the duck release run at the clock's
/// speed. A fade advances only when the tick runs: native advances it in its main loop, which stops under a modal
/// dialog, and its period timer makes up the time on the first tick after (docs/engine-re.md, "A fade under the 終了
/// dialog stops, then catches up"). Computing the level from the elapsed time does the same.</para>
/// <para>Engine thread only (spec §5.1, rule 4): the options panel's mixer calls come as requests
/// (InputAndWaits.Invoke).</para>
/// </remarks>
public sealed partial class Audio : IFastForwardListener
{
    public const int SoundEffectChannelCount = NativeNumberedSaveState.SoundEffectChannelCount;   // 0..9
    public const int BgmChannel = 11;
    public const int VoiceChannel = 12;
    /// <summary>The sound manager's channels, 0 to 12: the sound effects', then the engine's own.</summary>
    public const int NativeChannelCount = 13;
    private const int ChannelCount = NativeChannelCount;

    private readonly IAudioDevice _device;
    private readonly IAudioAssets _assets;
    private readonly FrameClock _clock;
    private readonly SkipAndAuto _skip;
    private readonly AudioMixerSettings _mixer;
    private readonly Sys4RegIniStore? _settings;
    private readonly TimelineLog? _timeline;
    private readonly bool _settleFadesAtOnce;

    private readonly bool[] _playing = new bool[ChannelCount];
    private readonly int[] _playIds = new int[ChannelCount];
    private int _lastPlayId;
    // Whether Service has anything to do besides taking the device's reports: a fade runs, a delayed start waits, or
    // the duck release is due. Read at every instruction boundary.
    private bool _timedWork;

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5).
    private sealed class Scene
    {
        public int DuckControl;                // op 0x1cf: bit 0 suppresses the duck and its release; 0x10000 one-shot
        public readonly DelayedStart?[] DelayedStarts = new DelayedStart?[SoundEffectChannelCount];
        public int VoiceMuteFlags;
        // message_skip_queued_voice_id/arg: the last voice started while fast-forward ran, played when it ends.
        public VoiceStart? HeldVoice;
        public DelayedVoice? DelayedVoice;     // op 0x2c0's pending request (voice_schedule_delayed_playback@0x488480)
    }

    private Scene _scene = new();

    /// <param name="device">The sound output.</param>
    /// <param name="assets">Where sounds are found.</param>
    /// <param name="clock">The engine clock: fades and delays run on it.</param>
    /// <param name="skip">The skip and auto native: a fade under fast-forward ends at once, and a voice started under
    /// it is held until it ends.</param>
    /// <param name="mixer">The sound:* settings (SYS4REG.INI). The native applies them to the device now and on every
    /// change it makes; whoever loaded them persists them.</param>
    /// <param name="settings">SYS4REG.INI, for the voice duck settings read at each voice start; none uses native's
    /// registered defaults.</param>
    /// <param name="timeline">The timeline log, if one is kept.</param>
    /// <param name="settleFadesAtOnce">For a run whose clock nothing advances (a headless VM or tool): an op 0xc2 fade
    /// applies its endpoint at once, as under fast-forward, since on a standing clock it would never end.</param>
    public Audio(IAudioDevice device, IAudioAssets assets, FrameClock clock, SkipAndAuto skip,
                 AudioMixerSettings mixer, Sys4RegIniStore? settings = null, TimelineLog? timeline = null,
                 bool settleFadesAtOnce = false)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _skip = skip ?? throw new ArgumentNullException(nameof(skip));
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
        _settings = settings;
        _timeline = timeline;
        _settleFadesAtOnce = settleFadesAtOnce;
        _skip.Listen(this);
        ApplyMixer(_mixer.Snapshot());
    }

    /// <summary>A native that plays nothing: no sounds, a silent device and a clock that stands still, so a script
    /// fade settles at once. Tests and tools that give the VM no audio native get one.</summary>
    public static Audio Silent(SkipAndAuto skip, AudioMixerSettings? mixer = null)
        => new(new SilentAudioDevice(), NoAudioAssets.Instance, new FrameClock(), skip,
               mixer ?? new AudioMixerSettings(), settleFadesAtOnce: true);

    /// <summary>Op 0x1be: whether a channel plays (0..9 sound effects, 11 BGM, 12 voice; any other channel is
    /// silent).</summary>
    public bool IsChannelPlaying(int channel)
        => (uint)channel < ChannelCount && _playing[channel];

    /// <summary>Whether a voice plays on channel 12 (ADV Auto waits for it).</summary>
    public bool IsVoicePlaying => _playing[VoiceChannel];

    /// <summary>The engine tick's audio step (spec §5.3, part 1). In native's order (engine_main_tick@0x411840): take
    /// the device's reports; the delayed sound effect and voice starts, unless the tick withholds them (an open statement
    /// block, EngineTick); the voice duck release (run-state 0x4000); then the BGM fade (run-state 0x200).</summary>
    public void Service(bool withholdDelayedStarts)
    {
        bool reported = _device.TryTakeEnded(out AudioChannelEnded ended);
        if (!reported && !_timedWork) return;
        while (reported)
        {
            ChannelEnded(ended);
            reported = _device.TryTakeEnded(out ended);
        }
        long now = _clock.NowMs;
        if (!withholdDelayedStarts)
        {
            ServiceDelayedSoundEffects(now);
            ServiceDelayedVoice(now);
        }
        ServiceDuckRelease();
        ServiceFade(now);
        UpdateTimedWork();
    }

    private void ChannelEnded(AudioChannelEnded ended)
    {
        if ((uint)ended.Channel >= ChannelCount || _playIds[ended.Channel] != ended.PlayId) return;   // an older play
        _playing[ended.Channel] = false;
    }

    private void UpdateTimedWork()
        => _timedWork = _fade != null || _scene.DelayedVoice != null || HasDelayedSoundEffect() || DuckReleaseDue();

    // ---- channels ----

    private void PlayChannel(int channel, bool loop, float volumeDb)
    {
        int playId = ++_lastPlayId;
        _playIds[channel] = playId;
        _playing[channel] = true;
        _device.Play(channel, loop, volumeDb, playId);
    }

    private void StopChannel(int channel)
    {
        _playing[channel] = false;
        _device.Stop(channel);
    }

    private void UnloadChannel(int channel)
    {
        _playing[channel] = false;
        _device.Unload(channel);
    }

    // ---- scene ----

    /// <summary>scene_context_init_reset's audio part, at a root reload: the held and delayed voices go, the op 0x1cf
    /// control mask and the op 0x1d5 request clear, and delayed sound effect starts are cancelled
    /// (sfx_clear_scheduled_starts) without stopping what plays or unloading a channel.</summary>
    public void ResetScene()
    {
        _scene = new();
        UpdateTimedWork();
    }
}

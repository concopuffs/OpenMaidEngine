using System;
using Godot;

internal enum MovieAudioRoute
{
    Muted = 0,
    Music = 1,
    SoundEffect = 2,
    Voice = 3,
    Movie = 4,
}

/// <summary>Godot-main-thread PCM sink and sound-hardware clock for one movie playback.</summary>
internal sealed class MovieAudioOutput : IDisposable
{
    private const int MaximumPushFrames = 4096;

    private readonly IMovieDecoder _decoder;
    private readonly AudioStreamPlayer _player;
    private readonly int _sampleRate;
    private readonly double _outputLatencySeconds;
    private AudioStreamGeneratorPlayback? _playback;
    private MovieAudioChunk? _pending;
    private int _pendingOffsetFrames;
    private long _pendingGapFrames;
    private long _submittedFrames;
    private long _clockMs;
    private bool _timelineAnchored;
    private bool _started;
    private bool _prerolling;
    private bool _submissionCompleted;
    private bool _disposed;

    public MovieAudioRoute Route { get; }
    public long ClockMs => _clockMs;
    public long SubmittedThroughMs => _sampleRate <= 0 ? 0 : _submittedFrames * 1000 / _sampleRate;
    public int BufferUnderruns => _playback?.GetSkips() ?? 0;

    public MovieAudioOutput(Node owner, IMovieDecoder decoder, MovieAudioRoute route,
                            double outputLatencySeconds)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        MovieAudioInfo info = decoder.AudioInfo
            ?? throw new ArgumentException("movie decoder has no audio stream", nameof(decoder));
        if (info.SampleRate <= 0 || info.Channels != 2)
            throw new ArgumentOutOfRangeException(nameof(decoder), "movie PCM must be stereo with a positive sample rate");

        Route = route;
        _sampleRate = info.SampleRate;
        _outputLatencySeconds = Math.Max(0, outputLatencySeconds);
        var generator = new AudioStreamGenerator
        {
            MixRate = _sampleRate,
            BufferLength = 0.25f,
        };
        _player = new AudioStreamPlayer
        {
            Stream = generator,
            Bus = RouteBus(route),
            VolumeDb = route == MovieAudioRoute.Muted ? -80.0f : 0.0f,
        };
        owner.AddChild(_player);
    }

    public void Update()
    {
        if (_disposed) return;
        if (_started)
        {
            double audibleSeconds = _player.GetPlaybackPosition()
                                      + AudioServer.GetTimeSinceLastMix()
                                      - _outputLatencySeconds;
            long candidate = (long)Math.Floor(Math.Max(0, audibleSeconds) * 1000.0);
            if (candidate > _clockMs) _clockMs = candidate;
        }
        FeedAvailablePcm();
        _decoder.AdvancePlaybackClock(_clockMs);
    }

    private void FeedAvailablePcm()
    {
        while (true)
        {
            if (_pending == null && !_decoder.TryTakeAudioChunk(out _pending))
            {
                if (_decoder.AudioDecodingCompleted && !_submissionCompleted)
                {
                    _submissionCompleted = true;
                    _decoder.MarkAudioSubmitted();
                }
                return;
            }
            if (_pending == null) return;
            if (!_started) Start();

            if (_pendingOffsetFrames == 0 && _pendingGapFrames == 0)
            {
                long audibleFrame = _clockMs * _sampleRate / 1000;
                if (audibleFrame > _submittedFrames) _submittedFrames = audibleFrame;
                long targetFrame = MovieAudioTimeline.PresentationFrame(
                    _pending.PresentationTimeMs, _sampleRate);
                MovieAudioAdjustment adjustment = MovieAudioTimeline.Align(
                    _submittedFrames, targetFrame, _pending.FrameCount, _sampleRate, _timelineAnchored);
                _pendingGapFrames = adjustment.GapFrames;
                _pendingOffsetFrames = adjustment.SkipFrames;
                _timelineAnchored = true;
            }

            int available = _playback!.GetFramesAvailable();
            if (available <= 0) return;
            if (_pendingGapFrames > 0)
            {
                int count = (int)Math.Min(Math.Min(_pendingGapFrames, available), MaximumPushFrames);
                if (!_playback.PushBuffer(new Vector2[count])) return;
                FinishPreroll();
                _pendingGapFrames -= count;
                _submittedFrames += count;
                continue;
            }

            int remaining = _pending.FrameCount - _pendingOffsetFrames;
            if (remaining <= 0)
            {
                _pending = null;
                _pendingOffsetFrames = 0;
                continue;
            }
            int frames = Math.Min(Math.Min(remaining, available), MaximumPushFrames);
            var output = new Vector2[frames];
            for (int index = 0; index < frames; index++)
            {
                int source = checked((_pendingOffsetFrames + index) * 2);
                output[index] = new Vector2(
                    _pending.InterleavedStereo[source],
                    _pending.InterleavedStereo[source + 1]);
            }
            if (!_playback.PushBuffer(output)) return;
            FinishPreroll();
            _pendingOffsetFrames += frames;
            _submittedFrames += frames;
            if (_pendingOffsetFrames == _pending.FrameCount)
            {
                _pending = null;
                _pendingOffsetFrames = 0;
            }
        }
    }

    private void Start()
    {
        _player.Play();
        _player.StreamPaused = true;
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback
                    ?? throw new InvalidOperationException("Godot did not create movie audio generator playback");
        _started = true;
        _prerolling = true;
    }

    public void SetPlaybackRate(double rate)
    {
        if (!double.IsFinite(rate) || rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate));
        _player.PitchScale = (float)rate;
    }

    private void FinishPreroll()
    {
        if (!_prerolling) return;
        _prerolling = false;
        _player.StreamPaused = false;
    }

    private static StringName RouteBus(MovieAudioRoute route) => route switch
    {
        MovieAudioRoute.Music => "Music",
        MovieAudioRoute.SoundEffect => "SFX",
        MovieAudioRoute.Voice => "Voice",
        _ => "Movie",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player.Stop();
        _player.QueueFree();
    }
}

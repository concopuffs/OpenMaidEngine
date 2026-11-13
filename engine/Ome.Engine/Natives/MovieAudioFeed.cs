using Ome.Engine.Devices;

namespace Ome.Engine.Natives;

/// <summary>Where a movie's sound plays, by its flags and SYS4INI DEPENDMOVIESOUND (<see cref="Model.MovieSoundRoute"/>):
/// a category's bus, or silence.</summary>
internal enum MovieAudioRoute
{
    Muted = 0,
    Music = 1,
    SoundEffect = 2,
    Voice = 3,
    Movie = 4,
}

/// <summary>
/// Feeds one playback's decoded PCM to an <see cref="IAudioStream"/> and paces the decoder by what has been heard. The
/// stream starts held and plays once its first block is queued; each block goes in at its presentation time, with
/// silence before a late block and the head of an early one dropped (<see cref="MovieAudioTimeline"/>). Runs on the
/// front end's thread, in the movie native's frame service.
/// </summary>
internal sealed class MovieAudioFeed : IDisposable
{
    private const int MaximumPushFrames = 4096;

    private readonly IMovieDecoder _decoder;
    private readonly IAudioStream _stream;
    private readonly int _sampleRate;
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
    public int BufferUnderruns => _started ? _stream.Underruns : 0;

    public MovieAudioFeed(IMovieDecoder decoder, MovieAudioRoute route, IAudioStreamDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        MovieAudioInfo info = decoder.AudioInfo
            ?? throw new ArgumentException("movie decoder has no audio stream", nameof(decoder));
        if (info.SampleRate <= 0 || info.Channels != 2)
            throw new ArgumentOutOfRangeException(nameof(decoder), "movie PCM must be stereo with a positive sample rate");

        Route = route;
        _sampleRate = info.SampleRate;
        _stream = device.Open(_sampleRate, BusOf(route), muted: route == MovieAudioRoute.Muted);
    }

    /// <summary>Read the heard position, queue what the stream has room for, and let the decoder's video catch up to
    /// the sound.</summary>
    public void Update()
    {
        if (_disposed) return;
        if (_started)
        {
            long candidate = (long)Math.Floor(Math.Max(0, _stream.HeardSeconds) * 1000.0);
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

            int available = _stream.FramesFree;
            if (available <= 0) return;
            if (_pendingGapFrames > 0)
            {
                int count = (int)Math.Min(Math.Min(_pendingGapFrames, available), MaximumPushFrames);
                if (!_stream.PushSilence(count)) return;
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
            if (!_stream.Push(_pending.InterleavedStereo, _pendingOffsetFrames, frames)) return;
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
        _stream.StartHeld();
        _started = true;
        _prerolling = true;
    }

    public void SetPlaybackRate(double rate)
    {
        if (!double.IsFinite(rate) || rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate));
        _stream.SetRate(rate);
    }

    private void FinishPreroll()
    {
        if (!_prerolling) return;
        _prerolling = false;
        _stream.Resume();
    }

    // The route's category bus; a muted movie plays silently on the movie bus.
    private static AudioBus BusOf(MovieAudioRoute route) => route switch
    {
        MovieAudioRoute.Music => AudioBus.Music,
        MovieAudioRoute.SoundEffect => AudioBus.SoundEffect,
        MovieAudioRoute.Voice => AudioBus.Voice,
        _ => AudioBus.Movie,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
    }
}

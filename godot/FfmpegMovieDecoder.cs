using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Age.Engine.Sys4;

internal interface IMoviePacingClock
{
    void StartPresentation() { }
    bool WaitUntil(long elapsedMilliseconds, WaitHandle cancellation);
}

internal interface IExternallyAdvancedMoviePacingClock : IMoviePacingClock
{
    void AdvanceTo(long elapsedMilliseconds);
}

internal sealed class StopwatchMoviePacingClock : IMoviePacingClock
{
    private long _startedAt;

    public void StartPresentation()
    {
        long startedAt = Stopwatch.GetTimestamp();
        Interlocked.CompareExchange(ref _startedAt, startedAt, 0);
    }

    public bool WaitUntil(long elapsedMilliseconds, WaitHandle cancellation)
    {
        StartPresentation();
        while (true)
        {
            long startedAt = Interlocked.Read(ref _startedAt);
            double remaining = elapsedMilliseconds
                               - Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            if (remaining <= 0) return true;
            int waitMilliseconds = (int)Math.Clamp(Math.Ceiling(remaining), 1, 1000);
            if (cancellation.WaitOne(waitMilliseconds)) return false;
        }
    }
}

internal sealed class ExternallyAdvancedMoviePacingClock : IExternallyAdvancedMoviePacingClock, IDisposable
{
    private readonly AutoResetEvent _advanced = new(false);
    private long _now;

    public bool WaitUntil(long elapsedMilliseconds, WaitHandle cancellation)
    {
        while (Interlocked.Read(ref _now) < elapsedMilliseconds)
        {
            int signalled = WaitHandle.WaitAny([cancellation, _advanced]);
            if (signalled == 0) return false;
        }
        return true;
    }

    public void AdvanceTo(long elapsedMilliseconds)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _now);
            if (elapsedMilliseconds <= current) return;
        }
        while (Interlocked.CompareExchange(ref _now, elapsedMilliseconds, current) != current);
        _advanced.Set();
    }

    public void Dispose() => _advanced.Dispose();
}

/// <summary>
/// Timestamp-paced FFmpeg delivery. Video-only streams retain monotonic stopwatch pacing. Audio-bearing
/// streams decode PCM into a bounded queue and advance video against the sound-device clock supplied by Godot.
/// </summary>
internal sealed class FfmpegMovieDecoder : IMovieDecoder
{
    private readonly IFfmpegFrameSource _source;
    private readonly IMoviePacingClock _clock;
    private readonly ManualResetEvent _cancel = new(false);
    private readonly ManualResetEvent _firstFramePresented = new(false);
    private readonly Thread _thread;
    private readonly Thread? _audioThread;
    private readonly object _frameLock = new();
    private readonly object _audioLock = new();
    private readonly Queue<MovieAudioChunk> _audioChunks = new();
    private readonly AutoResetEvent _audioSpace = new(false);
    private readonly int _maximumQueuedAudioFrames;
    private RgbaImage? _latestFrame;
    private volatile bool _completed;
    private volatile bool _videoTimelineCompleted;
    private volatile bool _audioDecodingCompleted;
    private volatile bool _audioSubmitted;
    private string? _failure;
    private int _queuedAudioFrames;
    private int _activeWorkers;
    private int _disposed;
    private int _firstFrameTaken;
    private long _firstFramePresentationTimeMs = -1;

    public long? StopTimeMs => _source.Info.StopTimeMs;
    public bool IsCompleted => _completed;
    public string? Failure => Volatile.Read(ref _failure);
    public long? FirstFramePresentationTimeMs
    {
        get
        {
            long value = Interlocked.Read(ref _firstFramePresentationTimeMs);
            return value < 0 ? null : value;
        }
    }
    public MovieAudioInfo? AudioInfo { get; }
    public bool AudioDecodingCompleted => _audioDecodingCompleted;

    public FfmpegMovieDecoder(MoviePayload movie)
        : this(new FfmpegMovieSession(movie), null) { }

    internal FfmpegMovieDecoder(IFfmpegFrameSource source, IMoviePacingClock? clock)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _clock = clock ?? (source.Info.HasAudio
            ? new ExternallyAdvancedMoviePacingClock()
            : new StopwatchMoviePacingClock());
        if (source.Info.HasAudio)
        {
            AudioInfo = new MovieAudioInfo(source.Info.AudioSampleRate, source.Info.AudioChannels);
            _maximumQueuedAudioFrames = Math.Max(source.Info.AudioSampleRate / 2,
                                                 source.Info.AudioFrameSamples * 2);
        }
        _thread = new Thread(DecodeThread)
        {
            IsBackground = true,
            Name = "AGE FFmpeg movie video",
        };
        _audioThread = source.Info.HasAudio
            ? new Thread(AudioDecodeThread)
            {
                IsBackground = true,
                Name = "AGE FFmpeg movie audio",
            }
            : null;
        _activeWorkers = _audioThread == null ? 1 : 2;
        try
        {
            _thread.Start();
            _audioThread?.Start();
        }
        catch
        {
            _cancel.Set();
            if (_thread.IsAlive) _thread.Join();
            if (_audioThread?.IsAlive == true) _audioThread.Join();
            _source.Dispose();
            _cancel.Dispose();
            _firstFramePresented.Dispose();
            _audioSpace.Dispose();
            if (_clock is IDisposable disposableClock) disposableClock.Dispose();
            throw;
        }
    }

    public bool TryTakeFrame(out RgbaImage frame)
    {
        lock (_frameLock)
        {
            if (_latestFrame == null)
            {
                frame = default!;
                return false;
            }
            frame = _latestFrame;
            _latestFrame = null;
        }
        if (Interlocked.Exchange(ref _firstFrameTaken, 1) == 0)
        {
            _clock.StartPresentation();
            _firstFramePresented.Set();
        }
        return true;
    }

    public bool TryTakeAudioChunk(out MovieAudioChunk chunk)
    {
        lock (_audioLock)
        {
            if (_audioChunks.Count == 0)
            {
                chunk = default!;
                return false;
            }
            chunk = _audioChunks.Dequeue();
            _queuedAudioFrames -= chunk.FrameCount;
        }
        _audioSpace.Set();
        return true;
    }

    public void AdvancePlaybackClock(long elapsedMilliseconds)
    {
        if (_clock is IExternallyAdvancedMoviePacingClock external)
            external.AdvanceTo(Math.Max(0, elapsedMilliseconds));
    }

    public void MarkAudioSubmitted()
    {
        _audioSubmitted = true;
        UpdateCompletion();
    }

    private void DecodeThread()
    {
        try
        {
            long lastTimestamp = -1;
            long decodedFrames = 0;
            long firstTimestamp = -1;
            while (!_cancel.WaitOne(0))
            {
                if (!_source.TryDecodeNextVideoFrame(out var frame))
                {
                    if (decodedFrames == 0)
                        throw new InvalidDataException("FFmpeg stream ended before producing a video frame");
                    long completionTime = PresentationDeadline(
                        Math.Max(_source.Info.StopTimeMs,
                                 lastTimestamp + FrameIntervalMilliseconds(_source.Info)),
                        firstTimestamp);
                    if (_clock.WaitUntil(completionTime, _cancel))
                    {
                        _videoTimelineCompleted = true;
                        UpdateCompletion();
                    }
                    return;
                }
                if (frame.PresentationTimeMs < 0 || frame.PresentationTimeMs < lastTimestamp)
                    throw new InvalidDataException(
                        $"FFmpeg returned non-monotonic video timestamp {frame.PresentationTimeMs} after {lastTimestamp}");
                if (decodedFrames == 0)
                {
                    firstTimestamp = frame.PresentationTimeMs;
                    Interlocked.Exchange(ref _firstFramePresentationTimeMs, firstTimestamp);
                    lock (_frameLock) _latestFrame = frame.Image;
                    lastTimestamp = frame.PresentationTimeMs;
                    decodedFrames++;
                    int signalled = WaitHandle.WaitAny([_cancel, _firstFramePresented]);
                    if (signalled == 0) return;
                    continue;
                }
                if (!_clock.WaitUntil(PresentationDeadline(frame.PresentationTimeMs, firstTimestamp),
                                      _cancel))
                    return;
                lock (_frameLock) _latestFrame = frame.Image;
                lastTimestamp = frame.PresentationTimeMs;
                decodedFrames++;
            }
        }
        catch (Exception error)
        {
            Fail(error);
        }
        finally
        {
            WorkerCompleted();
        }
    }

    private void AudioDecodeThread()
    {
        try
        {
            long priorTimestamp = -1;
            while (!_cancel.WaitOne(0))
            {
                while (Volatile.Read(ref _queuedAudioFrames) >= _maximumQueuedAudioFrames)
                {
                    int signalled = WaitHandle.WaitAny([_cancel, _audioSpace]);
                    if (signalled == 0) return;
                }
                if (!_source.TryDecodeNextAudioChunk(out FfmpegAudioChunk decoded))
                {
                    _audioDecodingCompleted = true;
                    UpdateCompletion();
                    return;
                }
                if (decoded.FrameCount <= 0
                    || decoded.InterleavedStereo.Length != checked(decoded.FrameCount * 2)
                    || decoded.PresentationTimeMs < 0
                    || decoded.PresentationTimeMs < priorTimestamp)
                    throw new InvalidDataException(
                        $"FFmpeg returned invalid audio block {decoded.FrameCount}f " +
                        $"at {decoded.PresentationTimeMs} ms after {priorTimestamp} ms");
                var chunk = new MovieAudioChunk(decoded.InterleavedStereo, decoded.FrameCount,
                                                decoded.PresentationTimeMs);
                lock (_audioLock)
                {
                    _audioChunks.Enqueue(chunk);
                    _queuedAudioFrames += chunk.FrameCount;
                }
                priorTimestamp = decoded.PresentationTimeMs;
            }
        }
        catch (Exception error)
        {
            Fail(error);
        }
        finally
        {
            WorkerCompleted();
        }
    }

    private void Fail(Exception error)
    {
        Interlocked.CompareExchange(ref _failure, error.Message, null);
        _completed = true; // decode failure must never strand an AGE movie wait
        _cancel.Set();
        _audioSpace.Set();
    }

    private void UpdateCompletion()
    {
        if (_failure != null || !_videoTimelineCompleted) return;
        if (AudioInfo != null && (!_audioDecodingCompleted || !_audioSubmitted)) return;
        _completed = true;
    }

    private void WorkerCompleted()
    {
        if (Interlocked.Decrement(ref _activeWorkers) == 0) _source.Dispose();
    }

    private static long FrameIntervalMilliseconds(FfmpegMovieInfo info)
    {
        if (info.FrameRateNumerator <= 0 || info.FrameRateDenominator <= 0) return 0;
        long scaledDenominator = checked((long)info.FrameRateDenominator * 1000);
        return Math.Max(1, scaledDenominator / info.FrameRateNumerator);
    }

    private long PresentationDeadline(long sourceTimestamp, long firstVideoTimestamp)
    {
        // MPEG program streams may put the first video sample hundreds of milliseconds after the audio
        // stream's mux timestamp origin. DirectShow presents the video's first sample as video time zero;
        // retaining the absolute mux offset here would freeze that sample until the audio clock caught up.
        // Preserve every decoded frame and its cadence, but rebase the video stream to its first sample.
        return Math.Max(0, sourceTimestamp - firstVideoTimestamp);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancel.Set();
        _firstFramePresented.Set();
        _audioSpace.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
            _thread.Join();
        if (_audioThread?.IsAlive == true && Thread.CurrentThread != _audioThread)
            _audioThread.Join();
        _cancel.Dispose();
        _firstFramePresented.Dispose();
        _audioSpace.Dispose();
        if (_clock is IDisposable disposableClock) disposableClock.Dispose();
    }
}

internal sealed class FfmpegMovieDecoderFactory : IMovieDecoderFactory
{
    public IMovieDecoder Open(MoviePayload movie) => new FfmpegMovieDecoder(movie);
}

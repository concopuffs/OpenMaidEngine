using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Age.Engine.Sys4;

internal interface IMoviePacingClock
{
    bool WaitUntil(long elapsedMilliseconds, WaitHandle cancellation);
}

internal sealed class StopwatchMoviePacingClock : IMoviePacingClock
{
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public bool WaitUntil(long elapsedMilliseconds, WaitHandle cancellation)
    {
        while (true)
        {
            double remaining = elapsedMilliseconds
                               - Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;
            if (remaining <= 0) return true;
            int waitMilliseconds = (int)Math.Clamp(Math.Ceiling(remaining), 1, 1000);
            if (cancellation.WaitOne(waitMilliseconds)) return false;
        }
    }
}

/// <summary>
/// Timestamp-paced FFmpeg video delivery. The worker decodes no more than one frame ahead, publishes only
/// when its presentation timestamp is due, and reports completion after the final presentation interval.
/// </summary>
internal sealed class FfmpegMovieDecoder : IMovieDecoder
{
    private readonly IFfmpegFrameSource _source;
    private readonly IMoviePacingClock _clock;
    private readonly ManualResetEvent _cancel = new(false);
    private readonly Thread _thread;
    private readonly object _frameLock = new();
    private RgbaImage? _latestFrame;
    private volatile bool _completed;
    private string? _failure;
    private int _disposed;

    public long? StopTimeMs => _source.Info.StopTimeMs;
    public bool IsCompleted => _completed;
    public string? Failure => Volatile.Read(ref _failure);

    public FfmpegMovieDecoder(MoviePayload movie)
        : this(new FfmpegMovieSession(movie), new StopwatchMoviePacingClock()) { }

    internal FfmpegMovieDecoder(IFfmpegFrameSource source, IMoviePacingClock clock)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _thread = new Thread(DecodeThread)
        {
            IsBackground = true,
            Name = "AGE FFmpeg movie",
        };
        try { _thread.Start(); }
        catch
        {
            _source.Dispose();
            _cancel.Dispose();
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
            return true;
        }
    }

    private void DecodeThread()
    {
        try
        {
            long lastTimestamp = -1;
            long decodedFrames = 0;
            while (!_cancel.WaitOne(0))
            {
                if (!_source.TryDecodeNextVideoFrame(out var frame))
                {
                    if (decodedFrames == 0)
                        throw new InvalidDataException("FFmpeg stream ended before producing a video frame");
                    long completionTime = Math.Max(_source.Info.StopTimeMs,
                                                   lastTimestamp + FrameIntervalMilliseconds(_source.Info));
                    if (_clock.WaitUntil(completionTime, _cancel)) _completed = true;
                    return;
                }
                if (frame.PresentationTimeMs < 0 || frame.PresentationTimeMs < lastTimestamp)
                    throw new InvalidDataException(
                        $"FFmpeg returned non-monotonic video timestamp {frame.PresentationTimeMs} after {lastTimestamp}");
                if (!_clock.WaitUntil(frame.PresentationTimeMs, _cancel)) return;
                lock (_frameLock) _latestFrame = frame.Image;
                lastTimestamp = frame.PresentationTimeMs;
                decodedFrames++;
            }
        }
        catch (Exception error)
        {
            Volatile.Write(ref _failure, error.Message);
            _completed = true; // decode failure must never strand an AGE movie wait
        }
        finally
        {
            _source.Dispose();
        }
    }

    private static long FrameIntervalMilliseconds(FfmpegMovieInfo info)
    {
        if (info.FrameRateNumerator <= 0 || info.FrameRateDenominator <= 0) return 0;
        long scaledDenominator = checked((long)info.FrameRateDenominator * 1000);
        return Math.Max(1, scaledDenominator / info.FrameRateNumerator);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancel.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
            _thread.Join();
        _cancel.Dispose();
    }
}

internal sealed class FfmpegMovieDecoderFactory : IMovieDecoderFactory
{
    public IMovieDecoder Open(MoviePayload movie) => new FfmpegMovieDecoder(movie);
}

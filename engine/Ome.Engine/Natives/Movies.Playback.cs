using System.Collections.Concurrent;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

// The decoders. The engine thread opens a playback's decoder and stages it with the rates and stops it requests; the
// front end's frame takes them in ServiceFrame and runs the decoders and their sound, whose stream device is the front
// end's and stays on its thread (spec §5.3, part 2). The frames and completions ServiceFrame takes go to the engine
// thread, which publishes them (PublishServiced) where native's main loop pumps its messages.
public sealed partial class Movies
{
    private readonly record struct Serviced(long PlaybackId, string? Name, int AssetId, RgbaImage? Frame);

    // Taken by the frame service, published by the engine thread.
    private readonly ConcurrentQueue<Serviced> _serviced = new();

    // Staged by the engine thread, taken by the frame service.
    private readonly ConcurrentDictionary<long, MovieRuntime> _opened = new();
    private readonly ConcurrentDictionary<long, double> _requestedRates = new();
    private readonly ConcurrentQueue<long> _requestedStops = new();

    // The front end's thread only.
    private readonly Dictionary<long, MovieRuntime> _playing = new();
    private readonly Dictionary<long, MovieAudioFeed> _feeds = new();
    private readonly HashSet<long> _frameSeen = new();
    private readonly HashSet<long> _completionNotified = new();
    private long _renderFrame;

    // Engine thread: open the decoder now, so its stop time is known before the op returns.
    private bool TryOpen(MoviePayload movie, long playbackId, long resourceId, int assetId, long movieFlags,
                         long initialPositionMs, long startDelayMs, long? presentationDurationMs,
                         out long? stopTimeMs)
    {
        stopTimeMs = null;
        if (_decoders == null) return false;
        try
        {
            MovieRuntime runtime = MovieRuntime.Open(
                movie.Name, assetId, resourceId, movie, _decoders, movieFlags,
                initialPositionMs, startDelayMs, presentationDurationMs, _time);
            stopTimeMs = runtime.Decoder.StopTimeMs;
            while (!_opened.TryAdd(playbackId, runtime))
                if (_opened.TryRemove(playbackId, out var prior)) prior.Decoder.Dispose();
            return true;
        }
        catch (Exception e)
        {
            _print($"movie decode failed {movie.Name}: {e.Message}");
            return false;
        }
    }

    // Engine thread: the next frame service applies the rate.
    private void QueueRate(long playbackId, double rate)
    {
        if (!double.IsFinite(rate) || rate <= 0)
        {
            _print($"movie playback rate ignored playback={playbackId}: {rate}");
            return;
        }
        _requestedRates[playbackId] = rate;
    }

    // Engine thread: the next frame service stops the decoder and its sound.
    private void RequestStop(long playbackId) => _requestedStops.Enqueue(playbackId);

    /// <summary>
    /// Once a frame, on the front end's thread: stop what the engine thread stopped, take the decoders it opened, apply
    /// the rates, and for each playback past its start delay take its newest frame, feed its sound, and see it complete
    /// when its decoder ends or its watchdog fires. The frames and completions wake the engine thread, which publishes
    /// them (<see cref="PublishServiced"/>).
    /// </summary>
    /// <param name="renderFrame">The front end's frame number, for the log.</param>
    public void ServiceFrame(long renderFrame)
    {
        _renderFrame = renderFrame;
        while (_requestedStops.TryDequeue(out long playbackId)) Stop(playbackId);
        AdoptOpened();
        ApplyRequestedRates();
        bool serviced = false;
        foreach (var (playbackId, movie) in _playing)
        {
            long elapsedMs = (long)_time.GetElapsedTime(movie.StartedAtTimestamp).TotalMilliseconds;
            if (elapsedMs < movie.StartDelayMs) continue;
            bool frameWasAlreadySeen = _frameSeen.Contains(playbackId);
            _feeds.TryGetValue(playbackId, out var audio);
            if (frameWasAlreadySeen) audio?.Update();
            if (movie.Decoder.TryTakeFrame(out var frame))
            {
                _serviced.Enqueue(new(playbackId, movie.Name, movie.AssetId, frame));
                serviced = true;
                if (_frameSeen.Add(playbackId))
                {
                    _print($"movie first frame {movie.Name} playback={playbackId}: " +
                           $"{frame.Width}x{frame.Height} RGBA8 at render frame {_renderFrame} " +
                           $"(source PTS {movie.Decoder.FirstFramePresentationTimeMs?.ToString() ?? "unknown"} ms)");
                    // Do not let decode startup consume the opening audio timeline. The first PCM push
                    // begins only after the first decoded image has reached the retained movie surface.
                    audio?.Update();
                }
            }
            bool watchdogExpired = elapsedMs >= movie.WatchdogMs;
            if ((movie.Decoder.IsCompleted || watchdogExpired) && _completionNotified.Add(playbackId))
            {
                if (movie.Decoder.Failure is { } failure)
                    _print($"movie decode failed {movie.Name}: {failure}");
                if (watchdogExpired && !movie.Decoder.IsCompleted)
                    _print($"movie completion watchdog {movie.Name}: forcing completion after {movie.WatchdogMs} ms");
                _serviced.Enqueue(new(playbackId, null, 0, null));
                serviced = true;
            }
        }
        if (serviced) _wake();
    }

    /// <summary>The engine thread: publish the frames the frame service took into their surfaces, and complete the
    /// playbacks it saw end, in the order it took them.</summary>
    public void PublishServiced()
    {
        while (_serviced.TryDequeue(out Serviced item))
        {
            if (item.Frame is { } frame) PublishFrame(item.PlaybackId, item.Name!, item.AssetId, frame);
            else Complete(item.PlaybackId);
        }
    }

    private void AdoptOpened()
    {
        foreach (var (playbackId, _) in _opened)
        {
            if (!_opened.TryRemove(playbackId, out var movie)) continue;
            if (_playing.Remove(playbackId, out var prior)) prior.Decoder.Dispose();
            _playing[playbackId] = movie;
            MovieAudioRoute route = RouteOf(movie.MovieFlags);
            if (movie.Decoder.AudioInfo != null && _sound != null)
            {
                if (_feeds.Remove(playbackId, out var priorAudio)) priorAudio.Dispose();
                _feeds[playbackId] = new MovieAudioFeed(movie.Decoder, route, _sound);
            }
            _completionNotified.Remove(playbackId);
            _print($"movie started {movie.Name} playback={playbackId} " +
                   $"({movie.Decoder.StopTimeMs?.ToString() ?? "unknown"} ms from VFS" +
                   (movie.InitialPositionMs > 0 ? $", start={movie.InitialPositionMs}ms" : "") +
                   (movie.Decoder.AudioInfo is { } info
                       ? $", audio={info.SampleRate}Hz stereo route={route}"
                       : "") + ")");
        }
    }

    private void ApplyRequestedRates()
    {
        foreach (var (playbackId, rate) in _requestedRates)
        {
            if (_playing.TryGetValue(playbackId, out MovieRuntime? movie))
            {
                movie.Decoder.SetPlaybackRate(rate);
                if (_feeds.TryGetValue(playbackId, out MovieAudioFeed? audio))
                    audio.SetPlaybackRate(rate);
                _requestedRates.TryRemove(playbackId, out _);
                _print($"movie rate playback={playbackId}: {rate:0.###}x");
            }
            else if (_opened.TryGetValue(playbackId, out MovieRuntime? opened))
            {
                // The VM normally reaches op 0x246 before the next frame service adopts the decoder.
                // Configure video pacing now and retain the request so the audio feed receives it too.
                opened.Decoder.SetPlaybackRate(rate);
            }
            else
            {
                _requestedRates.TryRemove(playbackId, out _);
            }
        }
    }

    private void Stop(long playbackId)
    {
        _requestedRates.TryRemove(playbackId, out _);
        if (_feeds.Remove(playbackId, out var audio)) audio.Dispose();
        if (_opened.TryRemove(playbackId, out var opened)) opened.Decoder.Dispose();
        if (_playing.Remove(playbackId, out var movie))
        {
            movie.Decoder.Dispose();
            _print($"movie stopped {movie.Name} playback={playbackId} at render frame {_renderFrame}");
        }
        _frameSeen.Remove(playbackId);
        _completionNotified.Remove(playbackId);
    }

    /// <summary>At exit, on the front end's thread once the engine thread has stopped: dispose every decoder and
    /// stream.</summary>
    public void Shutdown()
    {
        foreach (var audio in _feeds.Values) audio.Dispose();
        _feeds.Clear();
        foreach (var movie in _opened.Values) movie.Decoder.Dispose();
        _opened.Clear();
        foreach (var movie in _playing.Values) movie.Decoder.Dispose();
        _playing.Clear();
    }

    // The bus of a category, or silence (MovieSoundRoute.Resolve).
    private MovieAudioRoute RouteOf(long flags)
        => MovieSoundRoute.Resolve(flags, _dependMovieSound) is { } category
            ? (MovieAudioRoute)(int)category
            : MovieAudioRoute.Muted;

    /// <summary>The decoders, for a stall diagnostic: those playing and those opened but not yet taken by a frame
    /// service. Call it on the front end's thread.</summary>
    public MoviePlaybackDiagnostics PlaybackDiagnostics()
    {
        var playing = _playing
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                MovieRuntime movie = pair.Value;
                _feeds.TryGetValue(pair.Key, out var audio);
                return new MoviePlayingDiagnostic(
                    pair.Key, movie.ResourceId, movie.Name, movie.AssetId,
                    movie.Decoder.StopTimeMs, movie.InitialPositionMs,
                    movie.Decoder.IsCompleted, movie.Decoder.Failure,
                    movie.Decoder.FirstFramePresentationTimeMs,
                    _frameSeen.Contains(pair.Key), _completionNotified.Contains(pair.Key),
                    movie.WatchdogMs,
                    (long)_time.GetElapsedTime(movie.StartedAtTimestamp).TotalMilliseconds,
                    movie.Decoder.AudioInfo?.SampleRate,
                    movie.Decoder.AudioInfo == null ? null : movie.Decoder.AudioDecodingCompleted,
                    audio?.Route.ToString(), audio?.ClockMs, audio?.SubmittedThroughMs, audio?.BufferUnderruns,
                    movie.MovieFlags, movie.StartDelayMs);
            })
            .ToArray();
        var opened = _opened
            .OrderBy(pair => pair.Key)
            .Select(pair => new MovieOpenedDiagnostic(
                pair.Key, pair.Value.ResourceId, pair.Value.Name, pair.Value.AssetId,
                pair.Value.Decoder.StopTimeMs, pair.Value.InitialPositionMs,
                pair.Value.Decoder.IsCompleted, pair.Value.Decoder.Failure,
                pair.Value.Decoder.FirstFramePresentationTimeMs,
                pair.Value.MovieFlags, pair.Value.StartDelayMs))
            .ToArray();
        return new MoviePlaybackDiagnostics(playing, opened);
    }
}

/// <summary>The movie native's decoders (<see cref="Movies.PlaybackDiagnostics"/>).</summary>
public sealed record MoviePlaybackDiagnostics(
    IReadOnlyList<MoviePlayingDiagnostic> Playing, IReadOnlyList<MovieOpenedDiagnostic> Opened);

/// <summary>A decoder a frame service runs.</summary>
public sealed record MoviePlayingDiagnostic(
    long PlaybackId, long ResourceId, string Name, int AssetId, long? StopTimeMs, long InitialPositionMs,
    bool DecoderCompleted, string? DecoderFailure, long? FirstFrameSourcePtsMs, bool FrameSeen,
    bool CompletionNotified, long WatchdogMs, long ElapsedMs, int? AudioSampleRate, bool? AudioDecodeCompleted,
    string? AudioRoute, long? AudioClockMs, long? AudioSubmittedThroughMs, int? AudioBufferUnderruns,
    long MovieFlags, long StartDelayMs);

/// <summary>A decoder the engine thread opened and no frame service has taken yet.</summary>
public sealed record MovieOpenedDiagnostic(
    long PlaybackId, long ResourceId, string Name, int AssetId, long? StopTimeMs, long InitialPositionMs,
    bool DecoderCompleted, string? DecoderFailure, long? FirstFrameSourcePtsMs, long MovieFlags, long StartDelayMs);

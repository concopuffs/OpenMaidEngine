using System.Diagnostics;
using Godot;

public partial class Main
{
    private readonly System.Collections.Generic.Dictionary<long, MovieRuntime> _movies = new();
    private readonly System.Collections.Generic.Dictionary<long, MovieAudioOutput> _movieAudio = new();
    // 0x236 opens its decoder synchronously on the VM thread so 0x23f can query timing immediately.
    // Presentation ownership transfers here; _Process adopts staged decoders before sampling frames.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, MovieRuntime> _pendingMovies = new();
    private IMovieDecoderFactory _movieDecoderFactory = new FfmpegMovieDecoderFactory();
    private double _audioOutputLatencySeconds;
    private readonly System.Collections.Generic.HashSet<long> _movieFrameSeen = new();
    private readonly System.Collections.Generic.HashSet<long> _movieCompletionNotified = new();

    public bool TryPlayMovie(byte[] mpegBytes, string assetName, long playbackId,
                             long resourceId, int assetId, long movieFlags,
                             long initialPositionMs, long startDelayMs,
                             long? presentationDurationMs,
                             out long? stopTimeMs)
    {
        stopTimeMs = null;
        try
        {
            var payload = new Age.Engine.Sys4.MoviePayload(assetName, mpegBytes);
            var runtime = MovieRuntime.Open(
                assetName, assetId, resourceId, payload, _movieDecoderFactory, movieFlags,
                initialPositionMs, startDelayMs, presentationDurationMs);
            stopTimeMs = runtime.Decoder.StopTimeMs;
            while (!_pendingMovies.TryAdd(playbackId, runtime))
                if (_pendingMovies.TryRemove(playbackId, out var prior)) prior.Decoder.Dispose();
            return true;
        }
        catch (System.Exception e)
        {
            GD.Print($"movie decode failed {assetName}: {e.Message}");
            return false;
        }
    }

    private void AdoptPendingMovies()
    {
        foreach (var (playbackId, _) in _pendingMovies)
        {
            if (!_pendingMovies.TryRemove(playbackId, out var movie)) continue;
            if (_movies.Remove(playbackId, out var prior)) prior.Decoder.Dispose();
            _movies[playbackId] = movie;
            if (movie.Decoder.AudioInfo != null)
            {
                if (_movieAudio.Remove(playbackId, out var priorAudio)) priorAudio.Dispose();
                _movieAudio[playbackId] = new MovieAudioOutput(
                    this, movie.Decoder, MovieAudioRouteFromFlags(movie.MovieFlags),
                    _audioOutputLatencySeconds);
            }
            _movieCompletionNotified.Remove(playbackId);
            GD.Print($"movie started {movie.Name} playback={playbackId} " +
                     $"({movie.Decoder.StopTimeMs?.ToString() ?? "unknown"} ms from VFS" +
                     (movie.InitialPositionMs > 0 ? $", start={movie.InitialPositionMs}ms" : "") +
                     (movie.Decoder.AudioInfo is { } audio
                         ? $", audio={audio.SampleRate}Hz stereo route={MovieAudioRouteFromFlags(movie.MovieFlags)}"
                         : "") + ")");
        }
    }

    private void UpdateMovieFrames()
    {
        if (_host == null) return;
        foreach (var (playbackId, movie) in _movies)
        {
            long elapsedMs = (long)Stopwatch.GetElapsedTime(
                movie.StartedAtTimestamp).TotalMilliseconds;
            if (elapsedMs < movie.StartDelayMs) continue;
            bool frameWasAlreadySeen = _movieFrameSeen.Contains(playbackId);
            _movieAudio.TryGetValue(playbackId, out var audio);
            if (frameWasAlreadySeen) audio?.Update();
            if (movie.Decoder.TryTakeFrame(out var frame))
            {
                _host.PublishMovieFrame(playbackId, movie.Name, movie.AssetId, frame);
                if (_movieFrameSeen.Add(playbackId))
                {
                    GD.Print($"movie first frame {movie.Name} playback={playbackId}: " +
                             $"{frame.Width}x{frame.Height} RGBA8 at render frame {_timelineFrame} " +
                             $"(source PTS {movie.Decoder.FirstFramePresentationTimeMs?.ToString() ?? "unknown"} ms)");
                    // Do not let decode startup consume the opening audio timeline. The first PCM push
                    // begins only after the first decoded image has reached the retained movie surface.
                    audio?.Update();
                }
            }
            bool watchdogExpired = elapsedMs >= movie.WatchdogMs;
            if ((movie.Decoder.IsCompleted || watchdogExpired) && _movieCompletionNotified.Add(playbackId))
            {
                if (movie.Decoder.Failure is { } failure)
                    GD.Print($"movie decode failed {movie.Name}: {failure}");
                if (watchdogExpired && !movie.Decoder.IsCompleted)
                    GD.Print($"movie completion watchdog {movie.Name}: forcing completion after {movie.WatchdogMs} ms");
                _host.NotifyMovieCompleted(playbackId);
            }
        }
    }

    public void StopMovie(long playbackId)
    {
        if (_movieAudio.Remove(playbackId, out var audio)) audio.Dispose();
        if (_pendingMovies.TryRemove(playbackId, out var pending)) pending.Decoder.Dispose();
        if (_movies.Remove(playbackId, out var movie))
        {
            movie.Decoder.Dispose();
            GD.Print($"movie stopped {movie.Name} playback={playbackId} at render frame {_timelineFrame}");
        }
        _movieFrameSeen.Remove(playbackId);
        _movieCompletionNotified.Remove(playbackId);
    }

    private static MovieAudioRoute MovieAudioRouteFromFlags(long flags)
    {
        ulong value = unchecked((ulong)flags);
        if ((value & 0x10000) != 0) return MovieAudioRoute.Muted;
        if ((value & 0x20000) != 0) return MovieAudioRoute.Music;
        if ((value & 0x40000) != 0) return MovieAudioRoute.SoundEffect;
        if ((value & 0x80000) != 0) return MovieAudioRoute.Voice;
        return MovieAudioRoute.Movie;
    }

}

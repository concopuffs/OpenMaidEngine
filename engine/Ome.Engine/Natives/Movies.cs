using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>
/// The movie native (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.2; milestone 8). It owns which
/// playback is bound to which surface and the frame each shows, the decoders that play them, their start delays,
/// rates and completion, op 0x24d's movie mask transitions, and the movies' sound. A front end supplies only the
/// decoder (<see cref="IMovieDecoderFactory"/>) and a stream to play sound through (<see cref="IAudioStreamDevice"/>).
/// </summary>
/// <remarks>
/// <para>The engine thread runs the ops: it binds a playback, opens its decoder at once (op 0x23f reads the stop time
/// right after op 0x236), and requests rates and stops. The front end's frame runs <see cref="ServiceFrame"/>
/// (<c>Movies.Playback.cs</c>), which takes those requests, runs the decoders and feeds the sound on the front end's
/// thread, where the stream device is; each decoder's newest frame and its completion go to the engine thread, which
/// publishes them (<see cref="PublishServiced"/>). The graphics native asks which surfaces a movie shows
/// (<see cref="IGraphicsMovies"/>) and tells this one when a surface is released (<see cref="ISurfaceListener"/>).
/// Op 0x20f's modal movie is a wait of the input and waits native, over <see cref="ResolveModal"/> and
/// <see cref="StartModal"/>.</para>
/// </remarks>
public sealed partial class Movies : IGraphicsMovies, ISurfaceListener
{
    private readonly Graphics _graphics;
    private readonly IMovieAssets _assets;
    private readonly IMovieDecoderFactory? _decoders;
    private readonly IAudioStreamDevice? _sound;
    private readonly int _dependMovieSound;
    private readonly TimeProvider _time;
    private readonly TimelineLog? _timeline;
    private readonly Action<string> _print;
    private Action _wake = static () => { };

    private readonly MovieSurfaceRegistry _surfaces = new();

    private sealed record MaskPlayback(MovieMaskTransitionRequest Request, RgbaImage Captured);
    private readonly Dictionary<long, MaskPlayback> _masksByPlayback = new();
    private readonly Dictionary<int, long> _maskPlaybackBySurface = new();

    // Engine thread only: the movie a release stops, from SurfaceReleasing (native deletes the slot's movie object
    // first, finished or not) to SurfaceReleased.
    private MovieSurfaceRelease _releasing;
    private int _releasingSlot = -1;

    /// <param name="graphics">The graphics native: movies draw into its surfaces, and this native attaches itself to it
    /// as its movie partner and a surface listener.</param>
    /// <param name="assets">Where a movie's file comes from.</param>
    /// <param name="decoders">The front end's decoder; without one no movie plays, and each completes at once.</param>
    /// <param name="sound">Where a movie's sound plays; without one it is not played, and a decoder that paces its
    /// video by its sound waits for the watchdog.</param>
    /// <param name="dependMovieSound">SYS4INI DEPENDMOVIESOUND: the route of a movie whose flags name none.</param>
    /// <param name="time">The wall clock a start delay and the watchdog count on.</param>
    /// <param name="timeline"><c>--timeline-log</c>'s writer.</param>
    /// <param name="print">Where the movie log lines go.</param>
    public Movies(Graphics graphics, IMovieAssets assets, IMovieDecoderFactory? decoders, IAudioStreamDevice? sound,
                  int dependMovieSound, TimeProvider? time = null, TimelineLog? timeline = null,
                  Action<string>? print = null)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _decoders = decoders;
        _sound = sound;
        _dependMovieSound = dependMovieSound;
        _time = time ?? TimeProvider.System;
        _timeline = timeline;
        _print = print ?? (_ => { });
        _graphics.AttachMovies(this);
        _graphics.Listen(this);
    }

    /// <summary>A native with no movies and no decoder, for a run without a front end: every movie is unresolved.</summary>
    public static Movies Headless(Graphics graphics)
        => new(graphics, NoMovieAssets.Instance, decoders: null, sound: null, dependMovieSound: 0);

    /// <summary>What to call when a frame or a completion waits for the engine thread, a movie completes or a mask frame
    /// lands: the input and waits native wakes its waits.</summary>
    public void WakeWaitsWith(Action wake) => _wake = wake ?? throw new ArgumentNullException(nameof(wake));

    // ---- the ops (engine thread) ----

    /// <summary>Op 0x236: bind the movie to an existing surface and start it after <paramref name="startDelayMs"/>; the
    /// script goes on while it plays. Native attaches its renderer to the surface's texture, so the surface keeps its
    /// size and colour key until the first frame (<see cref="MovieSurfaceRegistry.Begin"/>).</summary>
    /// <param name="script">The running script's file, for the log.</param>
    /// <returns>The decoder's stop position in whole milliseconds, which op 0x23f reads at once; 0 when the movie
    /// cannot play; null when the resource names no movie.</returns>
    public long? PlayToSurface(string script, long resourceId, int surfaceSlot, long movieFlags, long startDelayMs)
        => Play(script, resourceId, surfaceSlot, movieFlags, startDelayMs, positionMs: 0);

    /// <summary>Op 0x241: op 0x236 from <paramref name="positionMs"/>. Seeking does not change the stop position.</summary>
    public long? PlayToSurfaceAtPosition(string script, long resourceId, int surfaceSlot, long movieFlags,
                                         long startDelayMs, long positionMs)
        => Play(script, resourceId, surfaceSlot, movieFlags, startDelayMs, positionMs);

    private long? Play(string script, long resourceId, int surfaceSlot, long movieFlags, long startDelayMs,
                       long positionMs)
    {
        AssetEntry? asset = _assets.Resolve(resourceId);
        if (asset == null)
        {
            _print($"movie unresolved {SceneOf(script)}:0x{resourceId:x}");
            return null;
        }
        StartMovie(asset, resourceId, surfaceSlot, movieFlags, modal: false,
                   initialPositionMs: positionMs, startDelayMs: startDelayMs, presentationDurationMs: null,
                   mask: null, out long? stopTimeMs, out _);
        return stopTimeMs ?? 0;
    }

    /// <summary>Op 0x24d: capture the retained range into the surface, then publish each movie frame's green channel
    /// as the capture's alpha. The transition ends when the movie does; one that cannot play ends at once.</summary>
    public void PlayMaskTransition(string script, MovieMaskTransitionRequest request)
    {
        GfxState gfx = _graphics.State;
        gfx.QueueMovieMaskTransition(request);
        RgbaImage captured = _graphics.CaptureObjectRange(
            request.SurfaceSlot, request.SourceRangeStart, request.SourceRangeCount);
        byte initialFill = request.Mode == 1 ? (byte)0 : (byte)255;
        PublishMaskedCapture(
            request.SurfaceSlot, captured, CreateFilledMask(request.Width, request.Height, initialFill), request);

        var mask = new MaskPlayback(request, captured);
        AssetEntry? asset = _assets.Resolve(request.ResourceId);
        if (asset == null)
        {
            _print($"movie mask unresolved {SceneOf(script)}:0x{request.ResourceId:x}");
            PublishMaskTerminal(mask);
            return;
        }

        bool started = StartMovie(
            asset, request.ResourceId, request.SurfaceSlot, movieFlags: 6,
            modal: false, initialPositionMs: 0,
            startDelayMs: request.StartDelayMs,
            presentationDurationMs: request.DurationMs,
            mask: mask, out long? stopTimeMs, out _);
        gfx.SetMovieStopTime(request.SurfaceSlot, stopTimeMs ?? 0);
        if (!started) PublishMaskTerminal(mask);
    }

    /// <summary>Op 0x23a: a movie is bound to the surface and has not completed.</summary>
    public bool IsSurfaceActive(int surfaceSlot) => _surfaces.IsActive(surfaceSlot);

    /// <summary>Op 0x246: play the surface's movie at <paramref name="rate"/> times its speed, from the next frame
    /// service. A surface with no movie ignores it, as native's null-surface guard does.</summary>
    public void SetPlaybackRate(int surfaceSlot, double rate)
    {
        if (!_surfaces.TryGetBinding(surfaceSlot, out MovieSurfaceBinding binding)) return;
        QueueRate(binding.PlaybackId, rate);
        _timeline?.Event("movie-rate", new()
        {
            ["surface"] = surfaceSlot,
            ["playback"] = binding.PlaybackId,
            ["rate"] = rate,
        });
    }

    /// <summary>engine_main_tick's surface_movie_is_playing scan: a bound movie whose flags have bit 2 set and bit 0
    /// clear holds an effect wait (<see cref="MovieSurfaceRegistry.HoldsServiceWait"/>).</summary>
    public bool HoldsServiceWait => _surfaces.HoldsServiceWait;

    /// <summary>Op 0x20f's movie, before the input and waits native's modal wait begins; null, with a log line, when the resource names
    /// none.</summary>
    public AssetEntry? ResolveModal(long resourceId)
    {
        AssetEntry? asset = _assets.Resolve(resourceId);
        if (asset == null) _print($"modal movie unresolved packed:0x{resourceId:x}");
        return asset;
    }

    /// <summary>Start op 0x20f's movie on the surface. False when it cannot play; it is then already complete.</summary>
    public bool StartModal(AssetEntry movie, long resourceId, int surfaceSlot, long movieFlags, out long playbackId)
        => StartMovie(movie, resourceId, surfaceSlot, movieFlags, modal: true,
                      initialPositionMs: 0, startDelayMs: 0, presentationDurationMs: null, mask: null,
                      out _, out playbackId);

    /// <summary>The playback has completed, or its modal wait was cancelled, which the script cannot tell apart; the
    /// wrapper's following surface release stops the decoder.</summary>
    public void MarkCompleted(long playbackId) => _surfaces.Complete(playbackId);

    private bool StartMovie(AssetEntry asset, long resourceId, int surfaceSlot, long movieFlags,
                            bool modal, long initialPositionMs, long startDelayMs,
                            long? presentationDurationMs, MaskPlayback? mask,
                            out long? stopTimeMs, out long playbackId)
    {
        stopTimeMs = null;
        // A playback is a surface-owned instance, not the shared resource id. BTL can schedule the same
        // asset on multiple surfaces; replacing one binding must not erase another binding's completion.
        // The renderer copies samples into the slot's texture, so the created contents stay visible until the first
        // sample (MovieSurfaceRegistry.Begin). Without them the compositor would take the object for a surfaceless fill.
        RgbaImage? surfaceContents = null;
        if (mask == null)
            surfaceContents = _graphics.TakeSurfacePixels(surfaceSlot);
        MovieSurfaceBinding binding = _surfaces.Begin(
            surfaceSlot, resourceId, movieFlags, out var replaced, surfaceContents);
        playbackId = binding.PlaybackId;
        if (replaced is { } prior)
        {
            AbandonMask(prior.PlaybackId);
            ForgetMaskSurface(prior.SurfaceSlot, prior.PlaybackId);
            RequestStop(prior.PlaybackId);
        }
        if (mask != null)
        {
            _masksByPlayback[playbackId] = mask;
            _maskPlaybackBySurface[surfaceSlot] = playbackId;
        }
        _graphics.ForgetSurfaceColorKey(surfaceSlot);
        // Movie surfaces inherit the selected game's primary size until a decoded frame supplies content.
        if (mask == null) _graphics.SetSurfaceSize(surfaceSlot, _graphics.Canvas.Width, _graphics.Canvas.Height);
        else _graphics.SetSurfaceSize(surfaceSlot, mask.Captured.Width, mask.Captured.Height);
        try
        {
            MoviePayload movie = _assets.Read(asset);
            _timeline?.Event("movie-start", new()
            {
                ["resource"] = resourceId, ["playback"] = playbackId,
                ["surface"] = surfaceSlot, ["file"] = movie.Name,
                ["flags"] = movieFlags, ["modal"] = modal,
                ["initial_position_ms"] = Math.Max(0, initialPositionMs),
                ["start_delay_ms"] = Math.Max(0, startDelayMs),
                ["presentation_duration_ms"] = presentationDurationMs,
                ["movie_mask"] = mask != null,
            });
            bool started = TryOpen(
                movie, playbackId, resourceId, asset.PackedId, movieFlags,
                initialPositionMs, startDelayMs, presentationDurationMs, out stopTimeMs);
            if (!started)
            {
                stopTimeMs = 0;
                Complete(playbackId);
            }
            return started;
        }
        catch (Exception e)
        {
            AbandonMask(playbackId);
            _surfaces.Abandon(playbackId, out _);
            _graphics.ForgetSurfaceSize(surfaceSlot);
            stopTimeMs = 0;
            _print($"movie read failed {asset.Name}: {e.Message}");
            return false;
        }
    }

    // A frame from the front end's frame service: a mask playback's green channel becomes the capture's alpha; any
    // other frame becomes the surface's.
    private void PublishFrame(long playbackId, string name, int assetId, RgbaImage frame)
    {
        MaskPlayback? mask;
        _masksByPlayback.TryGetValue(playbackId, out mask);
        if (mask != null)
        {
            MovieMaskTransitionRequest request = mask.Request;
            byte initialFill = request.Mode == 1 ? (byte)0 : (byte)255;
            byte[] alpha = MovieMaskSurface.ExtractGreen(frame, request.Width, request.Height, initialFill);
            PublishMaskedCapture(request.SurfaceSlot, mask.Captured, alpha, request);
            return;
        }
        if (_surfaces.PublishFrame(playbackId, frame, name, assetId))
            _graphics.RequestPresentation();
    }

    // The playback reached its end, its watchdog fired, or it could not start.
    private void Complete(long playbackId)
    {
        MaskPlayback? mask = RemoveMask(playbackId);
        if (mask != null) PublishMaskTerminal(mask);
        if (!_surfaces.Complete(playbackId)) return;
        _timeline?.Event("movie-complete", new() { ["playback"] = playbackId });
        _wake();
    }

    private static byte[] CreateFilledMask(int width, int height, byte fill)
    {
        var mask = new byte[checked(Math.Max(0, width) * Math.Max(0, height))];
        if (fill != 0) Array.Fill(mask, fill);
        return mask;
    }

    private void PublishMaskedCapture(int surfaceSlot, RgbaImage captured, byte[] mask,
                                      MovieMaskTransitionRequest request)
    {
        RgbaImage image = MovieMaskSurface.Apply(
            captured, mask, Math.Max(0, request.Width), Math.Max(0, request.Height), request.X, request.Y);
        _graphics.PublishSurfacePixels(surfaceSlot, image);
        _graphics.RequestPresentation();
        _wake();
    }

    private void PublishMaskTerminal(MaskPlayback playback)
    {
        byte terminalFill = playback.Request.Mode == 1 ? (byte)255 : (byte)0;
        PublishMaskedCapture(
            playback.Request.SurfaceSlot, playback.Captured,
            CreateFilledMask(playback.Request.Width, playback.Request.Height, terminalFill),
            playback.Request);
        _graphics.State.CompleteMovieMaskTransition(playback.Request.SurfaceSlot);
    }

    private MaskPlayback? RemoveMask(long playbackId)
    {
        return _masksByPlayback.Remove(playbackId, out var playback) ? playback : null;
    }

    private void AbandonMask(long playbackId)
    {
        MaskPlayback? playback = RemoveMask(playbackId);
        if (playback == null) return;
        ForgetMaskSurface(playback.Request.SurfaceSlot, playbackId);
        _graphics.State.CompleteMovieMaskTransition(playback.Request.SurfaceSlot);
    }

    private void ForgetMaskSurface(int surfaceSlot, long playbackId)
    {
        if (_maskPlaybackBySurface.GetValueOrDefault(surfaceSlot) == playbackId)
            _maskPlaybackBySurface.Remove(surfaceSlot);
    }

    // The log names a script as the host's script stack did: its file name, upper case, without the extension.
    private static string SceneOf(string script) => Path.GetFileNameWithoutExtension(script).ToUpperInvariant();

    // ---- the graphics native's questions (the engine thread, in the lowering) ----

    bool IGraphicsMovies.IsMaskedSurface(int surfaceSlot)
    {
        return _maskPlaybackBySurface.ContainsKey(surfaceSlot);
    }

    bool IGraphicsMovies.TryResolveSurface(int surfaceSlot, out MovieSurfaceFrame? frame)
        => _surfaces.TryResolveSurface(surfaceSlot, out frame);

    bool IGraphicsMovies.IsBound(int surfaceSlot) => _surfaces.IsBound(surfaceSlot);

    bool IGraphicsMovies.TryResolveResource(long resourceId, out MovieSurfaceFrame? frame)
        => _surfaces.TryResolveResource(resourceId, out frame);

    bool IGraphicsMovies.IsKnownMovieResource(long resourceId) => _surfaces.IsKnownMovieResource(resourceId);

    // ---- surface releases (engine thread) ----

    void ISurfaceListener.SurfaceReleasing(int slot)
    {
        _releasing = _surfaces.Release(slot);
        _releasingSlot = slot;
    }

    void ISurfaceListener.SurfaceReleased(int slot)
    {
        MovieSurfaceRelease release = _releasingSlot == slot ? _releasing : default;
        _releasing = default;
        _releasingSlot = -1;
        if (release.Kind != MovieSurfaceReleaseKind.Released) return;
        MovieSurfaceBinding binding = release.Binding;
        AbandonMask(binding.PlaybackId);
        ForgetMaskSurface(binding.SurfaceSlot, binding.PlaybackId);
        _timeline?.Event("movie-stop", new()
        {
            ["resource"] = binding.ResourceId,
            ["playback"] = binding.PlaybackId,
            ["surface"] = slot,
            ["completed"] = release.Completed,
        });
        RequestStop(binding.PlaybackId);
    }

    // Stop and unbind every movie on a surface in the range; the surfaces' own pixels stay.
    void ISurfaceListener.SurfaceRangeReleasing(int firstSlot, int count)
    {
        foreach (MovieSurfaceBinding binding in _surfaces.ReleaseRange(firstSlot, count))
        {
            AbandonMask(binding.PlaybackId);
            ForgetMaskSurface(binding.SurfaceSlot, binding.PlaybackId);
            _timeline?.Event("movie-stop", new()
            {
                ["resource"] = binding.ResourceId,
                ["playback"] = binding.PlaybackId,
                ["range_release"] = true,
            });
            RequestStop(binding.PlaybackId);
        }
    }

    void ISurfaceListener.SurfaceRangeReleased(int firstSlot, int count) { }

    /// <summary>Op 0x9's reset: scene_context_init_reset deletes every surface's movie object but keeps the surfaces
    /// (docs/engine-re.md, "AutoFreeTex").</summary>
    public void ResetScene() => ((ISurfaceListener)this).SurfaceRangeReleasing(0, 1000);

    void ISurfaceListener.SurfaceReplaced(int slot) { }

    // ---- diagnostics ----

    /// <summary>The bound surfaces, for the waits' diagnostic snapshot.</summary>
    public IReadOnlyList<MovieSurfaceDiagnostic> SurfaceDiagnostics() => _surfaces.Snapshot();

    /// <summary>The completed playbacks still bound.</summary>
    public IReadOnlyList<long> CompletedPlaybackIds() => _surfaces.CompletedPlaybackIds();
}

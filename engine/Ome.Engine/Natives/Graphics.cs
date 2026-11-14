using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>
/// The graphics native (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.2, §6.3; milestone 7). It owns
/// the retained objects (<see cref="State"/>), the surface table with its pixels (<c>Graphics.Surfaces.cs</c>), the
/// render-target captures and the whole-screen transition (<c>Graphics.Transitions.cs</c>), and it lowers each frame
/// into a frame description (<c>Graphics.Frame.cs</c>) that a renderer or the reference renderer draws.
/// </summary>
/// <remarks>
/// <para>Engine thread only (spec §5.1, rule 4): it runs the ops and lowers the frames, in the input and waits native's
/// frame turns. Surfaces are replaced, never written in place, so the front end draws a frame with the pixels it was
/// lowered with while the engine thread goes on.</para>
/// <para>The movie native (<see cref="Movies"/>) answers which surfaces a movie owns (<see cref="IGraphicsMovies"/>),
/// and it and the text native hear when a surface is released or replaced (<see cref="ISurfaceListener"/>).</para>
/// </remarks>
public sealed partial class Graphics
{
    private readonly ResourceMap? _res;
    private readonly FrameClock _clock;
    private readonly TimelineLog? _timeline;
    private readonly Action<string> _print;
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private IGraphicsMovies _movies = NoGraphicsMovies.Instance;
    private ISurfaceListener[] _listeners = [];

    /// <param name="resources">The catalog the surfaces load from; null for a run with no assets.</param>
    /// <param name="clock">The engine's frame clock.</param>
    /// <param name="canvas">The logical screen. Slot 0 begins as it; a canvas of 0 × 0 leaves slot 0 empty.</param>
    /// <param name="timeline"><c>--timeline-log</c>'s writer.</param>
    /// <param name="print">Where diagnostics go (a failed decode).</param>
    public Graphics(ResourceMap? resources, FrameClock clock, Sys4LogicalCanvas canvas, TimelineLog? timeline = null,
                    Action<string>? print = null)
    {
        _res = resources;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeline = timeline;
        _print = print ?? (_ => { });
        _screenWidth = canvas.Width;
        _screenHeight = canvas.Height;
        if (_screenWidth > 0 && _screenHeight > 0) _slotDims[0] = (_screenWidth, _screenHeight);
    }

    /// <summary>A native with no catalog and no screen, for a VM run without a front end: a created surface has its
    /// size, a loaded one loads nothing and reads 0 × 0, and slot 0 is empty.</summary>
    public static Graphics Headless() => new(null, new FrameClock(), new Sys4LogicalCanvas(0, 0));

    /// <summary>The retained objects, the surface records and the presentation channels.</summary>
    public GfxState State { get; } = new();

    /// <summary>The logical screen.</summary>
    public Sys4LogicalCanvas Canvas => new(_screenWidth, _screenHeight);

    /// <summary>The movie native, which answers which surfaces a movie shows (it attaches itself).</summary>
    public void AttachMovies(IGraphicsMovies movies) => _movies = movies ?? NoGraphicsMovies.Instance;

    /// <summary>Add one who hears that a surface is released or replaced: the movie native and the text native.
    /// Listeners hear in the order they were added. Composition adds them before the engine runs.</summary>
    public void Listen(ISurfaceListener listener)
        => _listeners = [.. _listeners, listener ?? throw new ArgumentNullException(nameof(listener))];

    // ---- presentation requests ----

    private int _presentRequested = 1;

    /// <summary>Something changed that the next frame must show (a surface write, an explicit present).</summary>
    public void RequestPresentation() => Interlocked.Exchange(ref _presentRequested, 1);

    /// <summary>Whether a presentation is requested and not yet taken.</summary>
    public bool PresentationRequested => Volatile.Read(ref _presentRequested) != 0;

    /// <summary>Take the pending request: true once per request.</summary>
    public bool TakePresentationRequest() => Interlocked.Exchange(ref _presentRequested, 0) != 0;
}

/// <summary>The movie native as the graphics native needs it: which surfaces and resources a movie shows, and which
/// surfaces a movie mask transition draws into.</summary>
public interface IGraphicsMovies
{
    /// <summary>A movie mask transition publishes its frames into the surface's own pixels.</summary>
    bool IsMaskedSurface(int surfaceSlot);

    /// <summary>The frame a movie bound to the surface shows; false when no movie has a frame there.</summary>
    bool TryResolveSurface(int surfaceSlot, out MovieSurfaceFrame? frame);

    /// <summary>A movie is bound to the surface, with or without a frame yet.</summary>
    bool IsBound(int surfaceSlot);

    /// <summary>The frame of a playing movie of this resource.</summary>
    bool TryResolveResource(long resourceId, out MovieSurfaceFrame? frame);

    /// <summary>The resource is a movie, playing or not, so it never decodes as a picture.</summary>
    bool IsKnownMovieResource(long resourceId);
}

/// <summary>Hears what happens to a surface: the movie native stops the movies on it, and the text native drops the
/// layouts retained on it (implementation plan §3.2, addition 2).</summary>
public interface ISurfaceListener
{
    /// <summary>Before surface <paramref name="slot"/> is released: native deletes the slot's movie first.</summary>
    void SurfaceReleasing(int slot);

    /// <summary>Before a range of surfaces is released.</summary>
    void SurfaceRangeReleasing(int firstSlot, int count);

    /// <summary>Surface <paramref name="slot"/> was released.</summary>
    void SurfaceReleased(int slot);

    /// <summary>A range of surfaces was released.</summary>
    void SurfaceRangeReleased(int firstSlot, int count);

    /// <summary>Surface <paramref name="slot"/>'s pixels were replaced from outside (op 0x1af's thumbnail).</summary>
    void SurfaceReplaced(int slot);
}

internal sealed class NoGraphicsMovies : IGraphicsMovies
{
    public static readonly NoGraphicsMovies Instance = new();

    public bool IsMaskedSurface(int surfaceSlot) => false;

    public bool TryResolveSurface(int surfaceSlot, out MovieSurfaceFrame? frame)
    {
        frame = null;
        return false;
    }

    public bool IsBound(int surfaceSlot) => false;

    public bool TryResolveResource(long resourceId, out MovieSurfaceFrame? frame)
    {
        frame = null;
        return false;
    }

    public bool IsKnownMovieResource(long resourceId) => false;
}

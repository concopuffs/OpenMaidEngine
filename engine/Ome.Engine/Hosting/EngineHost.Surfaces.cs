using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Hosting;

// The surfaces are the graphics native's (Natives/Graphics.Surfaces.cs). The lifted host is its partner until the
// movie and text natives exist: the movie part answers which surfaces a movie owns, and the movie and text parts hear
// that a surface was released or replaced (engine layering plan §3.2, addition 2).
public sealed partial class EngineHost : IGraphicsMovies, ISurfaceListener
{
    // Engine thread only: the movie a release stops, from SurfaceReleasing (native deletes the slot's movie object
    // first, finished or not) to SurfaceReleased.
    private MovieSurfaceRelease _releasingMovie;
    private int _releasingMovieSlot = -1;

    bool IGraphicsMovies.IsMaskedSurface(int surfaceSlot)
    {
        lock (_movieMaskLock) return _movieMaskPlaybackBySurface.ContainsKey(surfaceSlot);
    }

    bool IGraphicsMovies.TryResolveSurface(int surfaceSlot, out MovieSurfaceFrame? frame)
        => _movieSurfaces.TryResolveSurface(surfaceSlot, out frame);

    bool IGraphicsMovies.IsBound(int surfaceSlot) => _movieSurfaces.IsBound(surfaceSlot);

    bool IGraphicsMovies.TryResolveResource(long resourceId, out MovieSurfaceFrame? frame)
        => _movieSurfaces.TryResolveResource(resourceId, out frame);

    bool IGraphicsMovies.IsKnownMovieResource(long resourceId) => _movieSurfaces.IsKnownMovieResource(resourceId);

    void ISurfaceListener.SurfaceReleasing(int slot)
    {
        _releasingMovie = _movieSurfaces.Release(slot);
        _releasingMovieSlot = slot;
    }

    void ISurfaceListener.SurfaceReleased(int slot)
    {
        DropTextLayoutsOn(slot);
        MovieSurfaceRelease movieRelease = _releasingMovieSlot == slot ? _releasingMovie : default;
        _releasingMovie = default;
        _releasingMovieSlot = -1;
        if (movieRelease.Kind != MovieSurfaceReleaseKind.Released) return;
        var binding = movieRelease.Binding;
        AbandonMovieMaskPlayback(binding.PlaybackId);
        ForgetMovieMaskSurface(binding.SurfaceSlot, binding.PlaybackId);
        _timeline?.Event("movie-stop", new()
        {
            ["resource"] = binding.ResourceId,
            ["playback"] = binding.PlaybackId,
            ["surface"] = slot,
            ["completed"] = movieRelease.Completed,
        });
        _frontEnd.StopMovie(binding.PlaybackId);
    }

    void ISurfaceListener.SurfaceRangeReleasing(int firstSlot, int count) => StopSurfaceMovies(firstSlot, count);

    void ISurfaceListener.SurfaceRangeReleased(int firstSlot, int count)
    {
        int end = checked(firstSlot + count);
        lock (_textLock)
        {
            foreach (int layoutSlot in _retainedTextLayouts
                         .Where(pair =>
                             pair.Value.Binding.SourceSurfaceSlot >= firstSlot
                             && pair.Value.Binding.SourceSurfaceSlot < end)
                         .Select(pair => pair.Key)
                         .ToArray())
                _retainedTextLayouts.Remove(layoutSlot);
            _retainedHistoryLayouts.RemoveWhere(layoutSlot =>
                !_retainedTextLayouts.ContainsKey(layoutSlot));
        }
    }

    void ISurfaceListener.SurfaceReplaced(int slot) => DropTextLayoutsOn(slot);

    // The layouts retained on a surface go with its pixels.
    private void DropTextLayoutsOn(int slot)
    {
        lock (_textLock)
        {
            foreach (int layoutSlot in _retainedTextLayouts
                         .Where(pair => pair.Value.Binding.SourceSurfaceSlot == slot)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _retainedTextLayouts.Remove(layoutSlot);
                _retainedHistoryLayouts.Remove(layoutSlot);
            }
        }
    }

    // Stop and unbind every movie on a surface in the range; the surfaces' own pixels stay.
    private void StopSurfaceMovies(int firstSlot, int count)
    {
        foreach (MovieSurfaceBinding binding in _movieSurfaces.ReleaseRange(firstSlot, count))
        {
            AbandonMovieMaskPlayback(binding.PlaybackId);
            ForgetMovieMaskSurface(binding.SurfaceSlot, binding.PlaybackId);
            _timeline?.Event("movie-stop", new()
            {
                ["resource"] = binding.ResourceId,
                ["playback"] = binding.PlaybackId,
                ["range_release"] = true,
            });
            _frontEnd.StopMovie(binding.PlaybackId);
        }
    }
}

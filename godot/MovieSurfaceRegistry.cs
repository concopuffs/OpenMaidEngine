using System;
using System.Collections.Generic;
using System.Linq;
using Age.Engine.Sys4;

internal readonly record struct MovieSurfaceBinding(long PlaybackId, long ResourceId, int SurfaceSlot);
internal sealed record MovieSurfaceFrame(RgbaImage Image, string Name, int AssetId);
public sealed record MovieSurfaceDiagnostic(int SurfaceSlot, long PlaybackId, long ResourceId,
                                            bool Completed, bool HasFrame, string? Name);
internal enum MovieSurfaceReleaseKind { NotBound, Active, Released }
internal readonly record struct MovieSurfaceRelease(MovieSurfaceReleaseKind Kind,
                                                    MovieSurfaceBinding Binding);

/// <summary>Instance-keyed movie/surface ownership. A resource may be played more than once concurrently;
/// completion and frames therefore belong to a playback, never to the shared asset id.</summary>
internal sealed class MovieSurfaceRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<int, MovieSurfaceBinding> _bySurface = new();
    private readonly Dictionary<long, MovieSurfaceBinding> _byPlayback = new();
    private readonly Dictionary<long, MovieSurfaceFrame> _frames = new();
    private readonly HashSet<long> _completed = new();
    private readonly HashSet<long> _knownMovieResources = new();
    private long _nextPlaybackId;

    public MovieSurfaceBinding Begin(int surfaceSlot, long resourceId,
                                     out MovieSurfaceBinding? replaced)
    {
        lock (_lock)
        {
            replaced = _bySurface.TryGetValue(surfaceSlot, out var prior) ? prior : null;
            if (replaced.HasValue) RemoveLocked(prior);
            var binding = new MovieSurfaceBinding(++_nextPlaybackId, resourceId, surfaceSlot);
            _knownMovieResources.Add(resourceId);
            _bySurface[surfaceSlot] = binding;
            _byPlayback[binding.PlaybackId] = binding;
            return binding;
        }
    }

    public bool PublishFrame(long playbackId, RgbaImage image, string name, int assetId)
    {
        lock (_lock)
        {
            if (!_byPlayback.ContainsKey(playbackId)) return false;
            _frames[playbackId] = new MovieSurfaceFrame(image, name, assetId);
            return true;
        }
    }

    public bool Complete(long playbackId)
    {
        lock (_lock)
            return _byPlayback.ContainsKey(playbackId) && _completed.Add(playbackId);
    }

    public bool IsActive(int surfaceSlot)
    {
        lock (_lock)
            return _bySurface.TryGetValue(surfaceSlot, out var binding)
                && !_completed.Contains(binding.PlaybackId);
    }

    public bool IsBound(int surfaceSlot)
    {
        lock (_lock) return _bySurface.ContainsKey(surfaceSlot);
    }

    public bool HasActivePlayback
    {
        get
        {
            lock (_lock)
                return _byPlayback.Keys.Any(playbackId => !_completed.Contains(playbackId));
        }
    }

    public bool TryResolveSurface(int surfaceSlot, out MovieSurfaceFrame? frame)
    {
        lock (_lock)
        {
            if (_bySurface.TryGetValue(surfaceSlot, out var binding)
                && _frames.TryGetValue(binding.PlaybackId, out var found))
            {
                frame = found;
                return true;
            }
            frame = null;
            return false;
        }
    }

    public bool TryResolveResource(long resourceId, out MovieSurfaceFrame? frame)
    {
        lock (_lock)
        {
            foreach (var binding in _byPlayback.Values
                         .Where(binding => binding.ResourceId == resourceId)
                         .OrderByDescending(binding => binding.PlaybackId))
            {
                if (!_frames.TryGetValue(binding.PlaybackId, out var found)) continue;
                frame = found;
                return true;
            }
            frame = null;
            return false;
        }
    }

    /// <summary>Catalog ids are immutable within a mounted resource set. Remembering that an id entered
    /// the typed movie path prevents a cleanup-frame render snapshot from treating its .AGF-named MPEG
    /// payload as a still image after the last live surface binding has been detached.</summary>
    public bool IsKnownMovieResource(long resourceId)
    {
        lock (_lock) return _knownMovieResources.Contains(resourceId);
    }

    public MovieSurfaceRelease ReleaseIfCompleted(int surfaceSlot)
    {
        lock (_lock)
        {
            if (!_bySurface.TryGetValue(surfaceSlot, out var binding))
                return new MovieSurfaceRelease(MovieSurfaceReleaseKind.NotBound, default);
            if (!_completed.Contains(binding.PlaybackId))
                return new MovieSurfaceRelease(MovieSurfaceReleaseKind.Active, binding);
            RemoveLocked(binding);
            return new MovieSurfaceRelease(MovieSurfaceReleaseKind.Released, binding);
        }
    }

    public bool Abandon(long playbackId, out MovieSurfaceBinding binding)
    {
        lock (_lock)
        {
            if (!_byPlayback.TryGetValue(playbackId, out binding)) return false;
            RemoveLocked(binding);
            return true;
        }
    }

    public IReadOnlyList<MovieSurfaceBinding> ReleaseRange(int firstSlot, int count)
    {
        int end = checked(firstSlot + count);
        lock (_lock)
        {
            var released = _bySurface.Values
                .Where(binding => binding.SurfaceSlot >= firstSlot && binding.SurfaceSlot < end)
                .OrderBy(binding => binding.SurfaceSlot)
                .ToArray();
            foreach (var binding in released) RemoveLocked(binding);
            return released;
        }
    }

    public IReadOnlyList<MovieSurfaceDiagnostic> Snapshot()
    {
        lock (_lock)
            return _bySurface.Values
                .OrderBy(binding => binding.SurfaceSlot)
                .Select(binding => new MovieSurfaceDiagnostic(
                    binding.SurfaceSlot,
                    binding.PlaybackId,
                    binding.ResourceId,
                    _completed.Contains(binding.PlaybackId),
                    _frames.ContainsKey(binding.PlaybackId),
                    _frames.TryGetValue(binding.PlaybackId, out var frame) ? frame.Name : null))
                .ToArray();
    }

    public IReadOnlyList<long> CompletedPlaybackIds()
    {
        lock (_lock) return _completed.OrderBy(id => id).ToArray();
    }

    private void RemoveLocked(MovieSurfaceBinding binding)
    {
        _bySurface.Remove(binding.SurfaceSlot);
        _byPlayback.Remove(binding.PlaybackId);
        _frames.Remove(binding.PlaybackId);
        _completed.Remove(binding.PlaybackId);
    }
}

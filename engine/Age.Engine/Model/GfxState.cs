using System.Linq;

namespace Age.Engine.Model;

/// <summary>Host-agnostic model of the AGE native gfx command-buffer (reversed in
/// docs/engine-re.md, gfx op-contract table). One registry maps an object handle to a GfxObject — the
/// native retained-gfx owner+0x408 map (EngineCtx+0x46a1c) that op 0x215 queries and the geometry get/set ops share. Each object carries a
/// slot (returned by 0x215) and three 3-vectors: V18 (set 0x217 / get 0x218, anchor), V24 (set 0x219 /
/// get 0x21a, position), V16c (set 0x1ff). The native DirectDraw workers are NOT modelled — only the data
/// the query ops read back, which is all the bytecode geometry math needs.</summary>
public sealed partial class GfxState
{
    private long _mutationGeneration;
    private long _publishedMutationGeneration;

    /// <summary>Process-owned solid display background selected by opcode 0x25a (0xRRGGBB).</summary>
    public long DisplayBackgroundColor { get; private set; }

    public void SetDisplayBackgroundColor(long packedRgb)
    {
        lock (_lock)
        {
            DisplayBackgroundColor = packedRgb & 0x00ff_ffff;
            MarkRetainedMutation();
        }
    }

    private void MarkRetainedMutation() => _mutationGeneration++;

    /// <summary>Native scene_context_init_reset ownership boundary used by opcode 0x9: discard
    /// retained objects, command/query state, surfaces, transitions, render-target selection, and the
    /// scene animation clock while leaving VM globals and decoded host assets outside this model.</summary>
    public void ResetSceneContext()
    {
        lock (_lock)
        {
            _objects.Clear();
            _orderedObjectHandles.Clear();
            _surfaces.Clear();
            _createdSurfaces.Clear();
            _reloadableSurfaces.Clear();
            _movieStopTimesMs.Clear();
            _surfaceTransitions.Clear();
            _patternedSurfaceTransitions.Clear();
            _movieMaskTransitions.Clear();
            _radialBlurTransitions.Clear();
            _directionalBlurTransitions.Clear();
            CurrentObject = 0;
            CurrentRenderTargetSlot = -1;
            _rangeTransformFirst = 0;
            _rangeTransformCount = 0;
            _rangeTransform = new GfxObject();
            AnimClockDurationTicks = 0;
            AnimClockGeneration++;
            AnimationServiceFlags = 0;
            PreviousFrameTimeMilliseconds = 0;
            CurrentFrameTimeMilliseconds = 0;
            _previousFrameTimeMs = 0;
            _currentFrameTimeMs = 0;
            MarkRetainedMutation();
        }
    }

    public GfxPersistenceSnapshot CapturePersistenceSnapshot()
    {
        lock (_lock)
        {
            var surfaces = _surfaces.Keys
                .Concat(_reloadableSurfaces)
                .Distinct()
                .Select(slot =>
                {
                    bool active = _surfaces.TryGetValue(slot, out var value);
                    return new GfxSurfacePersistenceState(
                        slot, active ? value.ResId : -1, active ? value.ColorKey : 0,
                        active && _createdSurfaces.Contains(slot),
                        _reloadableSurfaces.Contains(slot));
                })
                .OrderBy(item => item.Slot)
                .ToArray();
            var objects = _orderedObjectHandles
                .Select(handle => (handle, CloneState(_objects[handle])))
                .ToArray();
            return new GfxPersistenceSnapshot(
                surfaces, objects, _rangeTransformFirst, _rangeTransformCount,
                CloneState(_rangeTransform));
        }
    }

    public void RestorePersistenceSnapshot(GfxPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            _surfaces.Clear();
            _createdSurfaces.Clear();
            _reloadableSurfaces.Clear();
            foreach (GfxSurfacePersistenceState surface in snapshot.Surfaces)
            {
                if (surface.ResourceId >= 0 || surface.Created)
                    _surfaces[surface.Slot] = (surface.ResourceId, surface.ColorKey);
                if (surface.Created) _createdSurfaces.Add(surface.Slot);
                if (surface.ReloadOnRestore) _reloadableSurfaces.Add(surface.Slot);
            }
            _objects.Clear();
            _orderedObjectHandles.Clear();
            foreach (var (handle, state) in snapshot.Objects)
            {
                _objects[handle] = CloneState(state);
                _orderedObjectHandles.Add(handle);
            }
            _orderedObjectHandles.Sort();
            _rangeTransformFirst = snapshot.RangeFirst;
            _rangeTransformCount = snapshot.RangeCount;
            _rangeTransform = CloneState(snapshot.RangeTransform);
            MarkRetainedMutation();
        }
    }

    private readonly object _lock = new();
}

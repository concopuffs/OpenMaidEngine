using System.Linq;

namespace Ome.Engine.Model;

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

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5). The live surface-record
    // table is the boot one at construction and a cleared one after a reset.
    private sealed class Scene(int[] surfaceRecords)
    {
        public uint PreviousFrameTimeMilliseconds;
        public uint CurrentFrameTimeMilliseconds;
        public long PreviousFrameTimeMs;
        public long CurrentFrameTimeMs;
        public readonly Dictionary<int, SurfaceTransition> SurfaceTransitions = new();
        public readonly Dictionary<long, PatternedSurfaceTransition> PatternedSurfaceTransitions = new();
        public readonly Dictionary<int, MovieMaskTransition> MovieMaskTransitions = new();
        public readonly Dictionary<long, RadialBlurRangeTransition> RadialBlurTransitions = new();
        public readonly Dictionary<long, DirectionalBlurRangeTransition> DirectionalBlurTransitions = new();
        // ---- geometry/draw object store (V18/V24/draw bind, the compositor's input) ----
        // Populated lazily by the geometry SET ops and draw-texture. Op 0x215 queries this same native map and
        // returns the object's live source slot (obj+4), or -1 when the handle has not been drawn/bound yet.
        public readonly Dictionary<long, GfxObject> Objects = new();
        // Native composition is handle-ascending z order. Mutations maintain this small index so snapshots do
        // not rebuild/sort a dictionary-sized LINQ buffer, while hot handle lookup remains O(1).
        public readonly List<long> OrderedObjectHandles = new();
        // Ops 0x229-0x22e address one embedded gfx-object record outside the ordinary object map. Its sampled
        // matrix is post-multiplied onto only the selected handle range during native composition. FIELD uses
        // this as its map camera while the surrounding dungeon UI remains screen-fixed.
        public long RangeTransformFirst;
        public long RangeTransformCount;
        public GfxObject RangeTransform = new();
        public long CurrentObject;
        public int CurrentRenderTargetSlot = -1;
        // Native surface record +0x08. Ordinary create/load/release workers leave this bit unchanged;
        // the numbered-save restore loop consults it to decide which asset-backed surfaces to reopen.
        public readonly HashSet<int> ReloadableSurfaces = new();
        // A separate entry models the native CMovieToTexture object attached to a surface. A null value means
        // the movie object exists but its host decoder supplied no usable IMediaPosition stop time.
        public readonly Dictionary<int, long?> MovieStopTimesMs = new();
        public readonly int[] SurfaceRecords = surfaceRecords;
        public readonly int[] SurfaceRecordSnapshot = NewSurfaceRecordTable();
        // EngineCtx+0x14e08 (SYS4433 +0x14e04), op 0x80's: the reset's text_manager_initialize sets it to 1.
        // Ops 0x13a and 0x2da's records. The reset zeroes all ten.
        public readonly NumericGlyphStyle[] NumericGlyphStyles = new NumericGlyphStyle[NumericGlyphStyleCount];
    }

    /// <summary>Native's numeric glyph styles (ops 0x13a, 0x2da and 0x23b; EngineCtx+0x55180, ten records). Native's
    /// test admits style 10, which lands in the return-stack table after them; the interpreter halts there.</summary>
    public const int NumericGlyphStyleCount = 10;

    private Scene _scene = new(NewBootSurfaceRecordTable());

    /// <summary>Process-owned solid display background selected by opcode 0x25a (0xRRGGBB).</summary>
    public long DisplayBackgroundColor { get; private set; }

    public void SetDisplayBackgroundColor(long packedRgb)
    {
        DisplayBackgroundColor = packedRgb & 0x00ff_ffff;
        MarkRetainedMutation();
    }

    private void MarkRetainedMutation() => _mutationGeneration++;

    /// <summary>Native scene_context_init_reset ownership boundary used by opcode 0x9: discard retained objects,
    /// command/query state, both surface-record tables, movie objects, transitions, render-target selection and the
    /// scene animation clock. The surfaces themselves stay: native releases them only when set:AutoFreeTex asks, which
    /// the VM decides (docs/engine-re.md, "AutoFreeTex"). VM globals and decoded host assets are outside this
    /// model.</summary>
    public void ResetSceneContext()
    {
        // gfx_surface_record_tables_init: both tables zeroed, then resource id -1 in every record (record +0x08 with
        // them); every ctx+0x52bd4 movie object is deleted.
        _scene = new(NewSurfaceRecordTable());
        ClearAnimationServiceForObjectClear();
        MarkRetainedMutation();
    }

    /// <summary>
    /// The state a numbered save writes, with each of <paramref name="sourceText"/>'s object ranges holding the glyphs
    /// the source-language run would have bound (localization spec §8) in place of the drawn ones. A range with no drawn
    /// object stays empty, as the layout's objects are released. A source glyph keeps the drawn object at its handle
    /// with the glyph's bind applied (gfx_object_bind_draw), or binds a new object.
    /// </summary>
    public GfxPersistenceSnapshot CapturePersistenceSnapshot(IReadOnlyList<AdvTextSourceObjects>? sourceText)
    {
        GfxPersistenceSnapshot snapshot = CapturePersistenceSnapshot();
        if (sourceText is not { Count: > 0 }) return snapshot;
        var objects = snapshot.Objects.ToDictionary(item => item.Handle, item => item.Object);
        foreach (AdvTextSourceObjects layout in sourceText)
        {
            var drawn = new Dictionary<long, GfxObject>();
            for (long handle = layout.FirstHandle; handle < layout.FirstHandle + layout.Capacity; handle++)
                if (objects.Remove(handle, out GfxObject? existing)) drawn[handle] = existing;
            if (drawn.Count == 0) continue;
            for (int index = 0; index < layout.Glyphs.Count; index++)
            {
                AdvRetainedGlyphPlacement placement = layout.Glyphs[index];
                AdvRetainedGlyphRecord cell = placement.Record;
                if (cell.Width <= 0 || cell.Height <= 0) continue;
                long handle = layout.FirstHandle + index;
                GfxObject bound = drawn.GetValueOrDefault(handle) ?? new GfxObject();
                bound.SourceSlot = layout.SurfaceSlot;
                bound.SrcRect = (cell.Left, cell.Top, cell.Width, cell.Height);
                bound.SrcCollapsed = false;
                bound.V24 = (placement.LayoutOriginX + cell.Left, placement.LayoutOriginY + cell.Top, 0);
                bound.Visible = true;
                bound.PostBindStaticColorBlend = false;
                objects[handle] = bound;
            }
        }
        return snapshot with
        {
            Objects = objects.OrderBy(item => item.Key).Select(item => (item.Key, item.Value)).ToArray(),
        };
    }

    public GfxPersistenceSnapshot CapturePersistenceSnapshot()
    {
        var surfaces = _surfaces.Keys
            .Concat(_scene.ReloadableSurfaces)
            .Distinct()
            .Select(slot =>
            {
                bool active = _surfaces.TryGetValue(slot, out var value);
                return new GfxSurfacePersistenceState(
                    slot, active ? value.ResId : -1, active ? value.ColorKey : 0,
                    active && _createdSurfaces.Contains(slot),
                    _scene.ReloadableSurfaces.Contains(slot));
            })
            .OrderBy(item => item.Slot)
            .ToArray();
        var objects = _scene.OrderedObjectHandles
            .Select(handle => (handle, CloneState(_scene.Objects[handle])))
            .ToArray();
        return new GfxPersistenceSnapshot(
            surfaces, objects, _scene.RangeTransformFirst, _scene.RangeTransformCount,
            CloneState(_scene.RangeTransform));
    }

    public void RestorePersistenceSnapshot(GfxPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _surfaces.Clear();
        _createdSurfaces.Clear();
        _scene.ReloadableSurfaces.Clear();
        foreach (GfxSurfacePersistenceState surface in snapshot.Surfaces)
        {
            if (surface.ResourceId >= 0 || surface.Created)
                _surfaces[surface.Slot] = (surface.ResourceId, surface.ColorKey);
            if (surface.Created) _createdSurfaces.Add(surface.Slot);
            if (surface.ReloadOnRestore) _scene.ReloadableSurfaces.Add(surface.Slot);
        }
        _scene.Objects.Clear();
        _scene.OrderedObjectHandles.Clear();
        foreach (var (handle, state) in snapshot.Objects)
        {
            _scene.Objects[handle] = CloneState(state);
            _scene.OrderedObjectHandles.Add(handle);
        }
        _scene.OrderedObjectHandles.Sort();
        _scene.RangeTransformFirst = snapshot.RangeFirst;
        _scene.RangeTransformCount = snapshot.RangeCount;
        _scene.RangeTransform = CloneState(snapshot.RangeTransform);
        MarkRetainedMutation();
    }

}

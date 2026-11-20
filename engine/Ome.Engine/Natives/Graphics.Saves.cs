using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

/// <summary>The graphics records of a numbered save (spec §5.1, rule 6): the surface records, the retained objects and
/// the range transform. The save native decides when they are written and read.</summary>
public sealed partial class Graphics
{
    /// <summary>The graphics records, encoded in the profile's record size. Presented text's glyphs are saved as the
    /// source text's (<paramref name="sourceText"/>, localization spec §8).</summary>
    public NativeNumberedSaveState WriteSaveRecords(NativeNumberedSaveState state, int recordSize,
                                                    IReadOnlyList<AdvTextSourceObjects> sourceText)
    {
        var gfx = NativeGfxPersistenceCodec.Capture(State, recordSize, sourceText);
        return state with
        {
            SurfaceRecords = gfx.SurfaceRecords,
            GfxObjects = gfx.Objects,
            RangeTransformFirst = gfx.RangeFirst,
            RangeTransformCount = gfx.RangeCount,
            RangeTransformRecord = gfx.RangeRecord,
            GraphicsRecordSize = recordSize,
        };
    }

    /// <summary>
    /// A numbered load's graphics records, decoded beforehand so a damaged record fails the load before anything
    /// changes. save_data_deserialize_and_begin_restore: with <paramref name="releaseAllSurfaces"/> ((CreateObject &amp;
    /// 2) &amp;&amp; (AutoFreeTex &amp; 2)) the release worker runs over every slot first (docs/engine-re.md,
    /// "AutoFreeTex"). The restore loop reopens each flagged record through gfx_surface_load_asset, which also rewrites
    /// that slot's live record; the rest of the live and snapshot tables are not replaced by the load. After the
    /// release AGE 4.38D clears its polygon map with <paramref name="clearPolygons"/> (CreateObject &amp; 2): a save
    /// holds no polygon, so a load leaves none.
    /// </summary>
    public void ReadSaveRecords(GfxPersistenceSnapshot snapshot, bool releaseAllSurfaces, bool clearPolygons = false)
    {
        if (releaseAllSurfaces) ReleaseSurfaceRange(0, 1000);
        if (clearPolygons) State.ClearPolygons();
        var restoredSurfaces = State.CapturePersistenceSnapshot().Surfaces
            .ToDictionary(item => item.Slot);
        foreach (GfxSurfacePersistenceState surface in snapshot.Surfaces
                     .Where(item => item.ReloadOnRestore && item.ResourceId >= 0))
            restoredSurfaces[surface.Slot] = surface;
        State.RestorePersistenceSnapshot(snapshot with
        {
            Surfaces = restoredSurfaces.Values.OrderBy(item => item.Slot).ToArray(),
        });
        foreach (GfxSurfacePersistenceState surface in snapshot.Surfaces
                     .Where(item => item.ReloadOnRestore && item.ResourceId >= 0))
            ReloadSurface(surface.Slot, surface.ResourceId, surface.ColorKey);
    }
}

using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Natives;

namespace Ome.Engine.Hosting;

// The surfaces are the graphics native's (Natives/Graphics.Surfaces.cs), and the movies on them the movie native's
// (Natives/Movies.cs). The lifted host's text part hears that a surface was released or replaced until the text native
// exists, and drops the layouts retained on it (engine layering plan §3.2, addition 2).
public sealed partial class EngineHost : ISurfaceListener
{
    void ISurfaceListener.SurfaceReleasing(int slot) { }

    void ISurfaceListener.SurfaceReleased(int slot) => DropTextLayoutsOn(slot);

    void ISurfaceListener.SurfaceRangeReleasing(int firstSlot, int count) { }

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
}

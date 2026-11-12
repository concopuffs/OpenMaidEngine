using Ome.Engine.Model;

namespace Ome.Engine.Hosting;

/// <summary>
/// The objects a frame's directional and radial blurs draw. A blur draws its source range itself, so ordinary
/// z-order skips those objects. Both compositors use it.
/// </summary>
public sealed class BlurRangeSources
{
    private readonly List<RenderObject> _blurs = new();

    // Rebuild for each snapshot: effect lifetime and source ranges can change between passes.
    // Index iteration also avoids boxing List<RenderObject>'s large enumerator through IReadOnlyList.
    public void Collect(IReadOnlyList<RenderObject> visible)
    {
        _blurs.Clear();
        for (int i = 0; i < visible.Count; i++)
        {
            var item = visible[i];
            if (item.DirectionalBlurTransition != null || item.RadialBlurTransition != null)
                _blurs.Add(item);
        }
    }

    /// <summary>True when one of the collected blurs draws <paramref name="handle"/>.</summary>
    public bool Contains(long handle)
    {
        foreach (RenderObject item in _blurs)
            if ((item.DirectionalBlurTransition is { } directional && directional.Contains(handle)) ||
                (item.RadialBlurTransition is { } radial && radial.Contains(handle))) return true;
        return false;
    }
}

using Age.Engine.Model;

namespace Age.Engine.Text;

/// <summary>
/// Transient live-layout presentation state. History persistence owns neither this list nor its Gfx
/// bindings; saved-frame script reconstruction rebuilds both.
/// </summary>
public sealed class RetainedAdvTextLayoutPresentation
{
    private readonly List<AdvRetainedGlyphPlacement> _glyphs = new();

    public RetainedAdvTextLayoutPresentation(AdvTextLayoutPresentationBinding binding)
    {
        if (binding.LayoutSlot <= 0)
            throw new ArgumentOutOfRangeException(nameof(binding));
        if (binding.SourceSurfaceSlot < 0)
            throw new ArgumentOutOfRangeException(nameof(binding));
        if (binding.FirstObjectHandle < 0 || binding.ObjectCapacity <= 0)
            throw new ArgumentException(
                "A retained ADV layout requires a nonnegative first handle and positive capacity.",
                nameof(binding));
        Binding = binding;
    }

    public AdvTextLayoutPresentationBinding Binding { get; }
    public IReadOnlyList<AdvRetainedGlyphPlacement> Glyphs => _glyphs;
    public int PublishedGlyphCount { get; private set; }
    public int PublishableGlyphCount
        => (int)Math.Min(_glyphs.Count, Binding.ObjectCapacity);

    public int Append(
        IReadOnlyList<AdvRetainedGlyphRecord> records,
        int layoutOriginX,
        int layoutOriginY)
    {
        ArgumentNullException.ThrowIfNull(records);
        int first = _glyphs.Count;
        foreach (AdvRetainedGlyphRecord record in records)
            _glyphs.Add(new AdvRetainedGlyphPlacement(
                record,
                new AdvRetainedGlyphPresentationRect(
                    record.Left, record.Top, record.Right, record.Bottom),
                layoutOriginX,
                layoutOriginY));
        return first;
    }

    public int Append(
        IReadOnlyList<AdvRetainedGlyphRecord> records,
        IReadOnlyList<AdvRetainedGlyphPresentationRect> presentationRects,
        int layoutOriginX,
        int layoutOriginY)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(presentationRects);
        if (presentationRects.Count != records.Count)
            throw new ArgumentException(
                "Every retained glyph record requires one presentation rectangle.",
                nameof(presentationRects));
        int first = _glyphs.Count;
        for (int index = 0; index < records.Count; index++)
            _glyphs.Add(new AdvRetainedGlyphPlacement(
                records[index], presentationRects[index], layoutOriginX, layoutOriginY));
        return first;
    }

    /// <summary>Publish newly revealed glyphs through a layout-global target count.</summary>
    public int PublishThrough(GfxState gfx, int targetGlyphCount)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        int target = Math.Clamp(targetGlyphCount, 0, PublishableGlyphCount);
        for (int index = PublishedGlyphCount; index < target; index++)
            Bind(gfx, index);
        PublishedGlyphCount = Math.Max(PublishedGlyphCount, target);
        return PublishedGlyphCount;
    }

    /// <summary>Recreate every currently revealed binding after op 0x20a or temporary suspension.</summary>
    public int Republish(GfxState gfx)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        for (int index = 0; index < PublishedGlyphCount; index++)
            Bind(gfx, index);
        return PublishedGlyphCount;
    }

    public void ErasePublished(GfxState gfx)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        gfx.EraseRange(Binding.FirstObjectHandle, Binding.ObjectCapacity);
    }

    private void Bind(GfxState gfx, int index)
    {
        AdvRetainedGlyphPlacement placement = _glyphs[index];
        AdvRetainedGlyphPresentationRect rect = placement.PresentationRect;
        if (rect.Width <= 0 || rect.Height <= 0) return;
        gfx.BindDraw(
            checked(Binding.FirstObjectHandle + index),
            Binding.SourceSurfaceSlot,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            checked(placement.LayoutOriginX + rect.Left),
            checked(placement.LayoutOriginY + rect.Top));
    }
}

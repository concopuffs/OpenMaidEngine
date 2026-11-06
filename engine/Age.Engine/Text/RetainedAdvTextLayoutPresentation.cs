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
            _glyphs.Add(new AdvRetainedGlyphPlacement(record, layoutOriginX, layoutOriginY));
        if (records.Count > 0) _lastAppendedIsRuby = false;
        return first;
    }

    /// <summary>
    /// Append one run's records in drawing order, marking which are ruby characters. AGE's builders always draw into
    /// the layout's last (pending) record and then push the next one: the base builder pushes chain flag 0, the ruby
    /// builder (adv_text_draw_ruby_over_row@0x456b40) chain flag 1, and a layout reset starts over with 0. A record's
    /// chain flag is therefore 1 exactly when the record drawn before it, in this run or an earlier one, is a ruby
    /// character, and the publisher then shows it in the same tick as that character's successor.
    /// </summary>
    public int AppendRun(
        IReadOnlyList<AdvRetainedGlyphRecord> records,
        IReadOnlyList<bool> isRuby,
        int layoutOriginX,
        int layoutOriginY)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(isRuby);
        if (isRuby.Count != records.Count)
            throw new ArgumentException("Each record needs a ruby marker.", nameof(isRuby));
        int first = _glyphs.Count;
        for (int index = 0; index < records.Count; index++)
        {
            AdvRetainedGlyphRecord record = records[index] with
            {
                PublicationChainFlag = _lastAppendedIsRuby ? 1 : 0,
            };
            _glyphs.Add(new AdvRetainedGlyphPlacement(record, layoutOriginX, layoutOriginY));
            _lastAppendedIsRuby = isRuby[index];
        }
        return first;
    }

    private bool _lastAppendedIsRuby;

    /// <summary>
    /// The glyph count published after <paramref name="ticks"/> publication ticks of a run's records
    /// [<paramref name="firstGlyph"/>, +<paramref name="glyphCount"/>). Each tick of
    /// adv_text_publish_next_glyph@0x451220 publishes one record and keeps going while the record it just published has
    /// chain flag 1, stopping at the run's end.
    /// </summary>
    public int PublicationTarget(int firstGlyph, int glyphCount, int ticks)
    {
        int index = firstGlyph;
        int end = Math.Min(_glyphs.Count, checked(firstGlyph + glyphCount));
        for (int tick = 0; tick < ticks && index < end; tick++)
        {
            bool chained;
            do
            {
                chained = _glyphs[index].Record.PublishesNextInSameTick;
                index++;
            }
            while (chained && index < end);
        }
        return index;
    }

    /// <summary>The number of publication ticks a run's records take (<see cref="PublicationTarget"/>).</summary>
    public int PublicationTicks(int firstGlyph, int glyphCount)
    {
        int ticks = 0;
        int end = Math.Min(_glyphs.Count, checked(firstGlyph + glyphCount));
        for (int index = firstGlyph; index < end; ticks++)
            index = PublicationTarget(index, end - index, 1);
        return ticks;
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
        AdvRetainedGlyphRecord cell = placement.Record;
        if (cell.Width <= 0 || cell.Height <= 0) return;
        gfx.BindDraw(
            checked(Binding.FirstObjectHandle + index),
            Binding.SourceSurfaceSlot,
            cell.Left,
            cell.Top,
            cell.Width,
            cell.Height,
            checked(placement.LayoutOriginX + cell.Left),
            checked(placement.LayoutOriginY + cell.Top));
    }
}

using Ome.Engine.Model;

namespace Ome.Engine.Text;

/// <summary>
/// Transient live-layout presentation state. History persistence owns neither this list nor its Gfx
/// bindings; saved-frame script reconstruction rebuilds both.
/// </summary>
public sealed class RetainedAdvTextLayoutPresentation
{
    private readonly List<AdvRetainedGlyphPlacement> _glyphs = new();
    // The glyphs each run's source text has in the source layout (localization spec §8), in drawing order, and for
    // each run the drawn glyph count after it and its source glyphs' end.
    private readonly List<AdvRetainedGlyphPlacement> _sourceGlyphs = new();
    private readonly List<(int DrawnEnd, int SourceEnd)> _sourceRuns = new();

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

    /// <summary>Whether a run drew something other than its source layout (localization spec §8), so the glyphs the
    /// source-language run would have published (<see cref="PublishedSourceGlyphs"/>) are not <see cref="Glyphs"/>.</summary>
    public bool SourceDiffers { get; private set; }

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
        AddSourceRun(records, layoutOriginX, layoutOriginY);
        return first;
    }

    /// <summary>
    /// Append one run's records in drawing order, marking which are ruby characters. AGE's builders always draw into
    /// the layout's last (pending) record and then push the next one. In SYS4422 the base builder pushes chain flag 0,
    /// the ruby builder (adv_text_draw_ruby_over_row@0x456b40) chain flag 1, and a layout reset starts over with 0, so a
    /// record's chain flag is 1 exactly when the record drawn before it, in this run or an earlier one, is a ruby
    /// character; the publisher then shows it in the same tick as that character's successor. In SYS4433
    /// (<paramref name="chainEveryRubyRecord"/>; adv_text_draw_ruby_over_row_sys4433@0x4590d0) every ruby record has
    /// chain flag 1 and the ruby builder resets the pending record to 0, so a record's flag is 1 exactly when it is a
    /// ruby character.
    /// <paramref name="sourceRecords"/> are the source layout's records when the run drew something else (localization
    /// spec §8), in the same drawing order; null when the run drew its source layout.
    /// </summary>
    public int AppendRun(
        IReadOnlyList<AdvRetainedGlyphRecord> records,
        IReadOnlyList<bool> isRuby,
        int layoutOriginX,
        int layoutOriginY,
        bool chainEveryRubyRecord = false,
        IReadOnlyList<AdvRetainedGlyphRecord>? sourceRecords = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(isRuby);
        if (isRuby.Count != records.Count)
            throw new ArgumentException("Each record needs a ruby marker.", nameof(isRuby));
        int first = _glyphs.Count;
        for (int index = 0; index < records.Count; index++)
        {
            bool chained = chainEveryRubyRecord ? isRuby[index] : _lastAppendedIsRuby;
            AdvRetainedGlyphRecord record = records[index] with { PublicationChainFlag = chained ? 1 : 0 };
            _glyphs.Add(new AdvRetainedGlyphPlacement(record, layoutOriginX, layoutOriginY));
            _lastAppendedIsRuby = !chainEveryRubyRecord && isRuby[index];
        }
        AddSourceRun(sourceRecords ?? records, layoutOriginX, layoutOriginY);
        SourceDiffers |= sourceRecords != null;
        return first;
    }

    /// <summary>A run that drew nothing (a hidden run of a presented message, localization spec §8): only its source
    /// layout's records, which the source-language run would have drawn.</summary>
    public void AppendSourceOnly(IReadOnlyList<AdvRetainedGlyphRecord> sourceRecords, int layoutOriginX, int layoutOriginY)
    {
        ArgumentNullException.ThrowIfNull(sourceRecords);
        AddSourceRun(sourceRecords, layoutOriginX, layoutOriginY);
        SourceDiffers |= sourceRecords.Count > 0;
    }

    private void AddSourceRun(IReadOnlyList<AdvRetainedGlyphRecord> records, int layoutOriginX, int layoutOriginY)
    {
        foreach (AdvRetainedGlyphRecord record in records)
            _sourceGlyphs.Add(new AdvRetainedGlyphPlacement(record, layoutOriginX, layoutOriginY));
        _sourceRuns.Add((_glyphs.Count, _sourceGlyphs.Count));
    }

    /// <summary>The glyphs the source-language run would have published by now (localization spec §8), up to the
    /// layout's capacity: all of every run whose drawn glyphs are published, and of a run still revealing, as many as it
    /// has published.</summary>
    public IReadOnlyList<AdvRetainedGlyphPlacement> PublishedSourceGlyphs()
    {
        int published = 0, drawnStart = 0, sourceStart = 0;
        foreach ((int drawnEnd, int sourceEnd) in _sourceRuns)
        {
            if (PublishedGlyphCount >= drawnEnd)
                published = sourceEnd;
            else
            {
                published = Math.Min(sourceEnd, sourceStart + Math.Max(0, PublishedGlyphCount - drawnStart));
                break;
            }
            (drawnStart, sourceStart) = (drawnEnd, sourceEnd);
        }
        return _sourceGlyphs.Take((int)Math.Min(published, Binding.ObjectCapacity)).ToArray();
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

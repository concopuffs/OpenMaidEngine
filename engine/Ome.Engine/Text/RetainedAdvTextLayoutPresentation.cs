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
    // For each run, what the publisher last did to its glyphs (the rows under their cells, the move), and whether to
    // every record or only to those that are not ruby: its source glyphs are published as its drawn ones were.
    private readonly List<(GlyphPublication Publication, bool EveryRecord)> _sourceRunGrowth = new();

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
        var flagged = new AdvRetainedGlyphRecord[records.Count];
        for (int index = 0; index < records.Count; index++)
        {
            bool chained = chainEveryRubyRecord ? isRuby[index] : _lastAppendedIsRuby;
            AdvRetainedGlyphRecord record = records[index] with { PublicationChainFlag = chained ? 1 : 0 };
            flagged[index] = record;
            _glyphs.Add(new AdvRetainedGlyphPlacement(record, layoutOriginX, layoutOriginY));
            _lastAppendedIsRuby = !chainEveryRubyRecord && isRuby[index];
        }
        AddSourceRun(sourceRecords ?? flagged, layoutOriginX, layoutOriginY);
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
        _sourceRunGrowth.Add((default, false));
    }

    /// <summary>The glyphs the source-language run would have published by now (localization spec §8), up to the
    /// layout's capacity: all of every run whose drawn glyphs are published, and of a run still revealing, as many as it
    /// has published.</summary>
    public IReadOnlyList<AdvRetainedGlyphPlacement> PublishedSourceGlyphs()
    {
        int published = 0, drawnStart = 0, sourceStart = 0;
        var grown = new List<AdvRetainedGlyphPlacement>();
        for (int run = 0; run < _sourceRuns.Count; run++)
        {
            (int drawnEnd, int sourceEnd) = _sourceRuns[run];
            bool whole = PublishedGlyphCount >= drawnEnd;
            published = whole
                ? sourceEnd
                : Math.Min(sourceEnd, sourceStart + Math.Max(0, PublishedGlyphCount - drawnStart));
            (GlyphPublication publication, bool everyRecord) = _sourceRunGrowth[run];
            for (int index = sourceStart; index < published; index++)
            {
                AdvRetainedGlyphPlacement glyph = _sourceGlyphs[index];
                grown.Add(Published(glyph, publication, everyRecord));
            }
            if (!whole) break;
            (drawnStart, sourceStart) = (drawnEnd, sourceEnd);
        }
        return grown.Take((int)Math.Min(published, Binding.ObjectCapacity)).ToArray();
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

    /// <summary>
    /// Publish newly revealed glyphs through a layout-global target count, as adv_text_publish_next_glyph does.
    /// <paramref name="primary"/> is what the build's publisher does to each glyph that is not ruby (a record with
    /// chain flag 0) as it binds it: the rows it adds under the cell while a text effect is on
    /// (<c>TextRules.PublisherGrowsGlyphCells</c>) and the object's move (<c>TextRules.PublisherMovesAliasedEffectGlyphs</c>);
    /// the default does nothing.
    /// </summary>
    public int PublishThrough(GfxState gfx, int targetGlyphCount, GlyphPublication primary = default)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        int target = Math.Clamp(targetGlyphCount, 0, PublishableGlyphCount);
        for (int index = PublishedGlyphCount; index < target; index++)
        {
            Grow(index, primary, everyRecord: false);
            Bind(gfx, index);
        }
        PublishedGlyphCount = Math.Max(PublishedGlyphCount, target);
        return PublishedGlyphCount;
    }

    /// <summary>
    /// Recreate every currently revealed binding. With <paramref name="layout"/> it is op 0x20a's
    /// adv_text_publish_layout, which does that to every record: the rows under the cell (the effect's vertical offset
    /// while an effect is on, twice in mode 3) and the move. It tests the chain flag of the record at the reveal index,
    /// not of the record it binds; for a chained one it moves cells instead, which the port does not model, so the
    /// cells keep what they had. Without it (a layout coming back from a suspension, a redraw) each glyph keeps what
    /// it was published with.
    /// </summary>
    public int Republish(GfxState gfx, GlyphPublication? layout = null)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        bool grows = layout != null
                     && (PublishedGlyphCount >= _glyphs.Count
                         || _glyphs[PublishedGlyphCount].Record.PublicationChainFlag == 0);
        for (int index = 0; index < PublishedGlyphCount; index++)
        {
            if (grows) Grow(index, layout!.Value, everyRecord: true);
            Bind(gfx, index);
        }
        return PublishedGlyphCount;
    }

    /// <summary>
    /// Recreate every currently revealed binding as op 0x82's redraw does on 4.33G and 4.38D
    /// (text_redraw_retained_group, @0x4553c0 and @0x460cd0): it binds each record again, doing to each that is not ruby
    /// what <paramref name="primary"/> says, as <see cref="PublishThrough"/> does, unlike the layout publisher, which
    /// does it to every record when the record at the reveal index is not ruby.
    /// </summary>
    public int RepublishEach(GfxState gfx, GlyphPublication primary)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        for (int index = 0; index < PublishedGlyphCount; index++)
        {
            Grow(index, primary, everyRecord: false);
            Bind(gfx, index);
        }
        return PublishedGlyphCount;
    }

    // Remember what the publisher does to glyph index, and give its run's source glyphs the same.
    private void Grow(int index, GlyphPublication publication, bool everyRecord)
    {
        _glyphs[index] = Published(_glyphs[index], publication, everyRecord);
        for (int run = 0; run < _sourceRuns.Count; run++)
            if (index < _sourceRuns[run].DrawnEnd)
            {
                _sourceRunGrowth[run] = (publication, everyRecord);
                break;
            }
    }

    // A glyph as the publisher binds it: a ruby record (chain flag 1) is left as it is unless the publisher does the
    // same to every record.
    private static AdvRetainedGlyphPlacement Published(AdvRetainedGlyphPlacement glyph, GlyphPublication publication,
                                                       bool everyRecord)
    {
        GlyphPublication applied = everyRecord || glyph.Record.PublicationChainFlag == 0 ? publication : default;
        return glyph with
        {
            CellGrowth = applied.CellGrowth, ObjectShiftX = applied.ShiftX, ObjectShiftY = applied.ShiftY,
        };
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
            checked(cell.Height + placement.CellGrowth),
            placement.ObjectPosition.X,
            placement.ObjectPosition.Y);
    }
}

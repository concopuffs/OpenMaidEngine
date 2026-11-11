using Ome.Engine.Model;
using Ome.Engine.Text;

public class RetainedAdvTextLayoutPresentationTests
{
    [Fact]
    public void PublishesNativeRectsInHandleOrderAndStopsAtCapacity()
    {
        var binding = new AdvTextLayoutPresentationBinding(
            1, 21, 100, 2, 900, 10, 20);
        var presentation = new RetainedAdvTextLayoutPresentation(binding);
        presentation.Append(
            [
                new AdvRetainedGlyphRecord(0, 1, 2, 5, 8),
                new AdvRetainedGlyphRecord(0, 5, 2, 9, 8),
            ],
            layoutOriginX: 30,
            layoutOriginY: 40);
        presentation.Append(
            [new AdvRetainedGlyphRecord(0, 10, 3, 14, 9)],
            layoutOriginX: 50,
            layoutOriginY: 60);
        var gfx = new GfxState();
        gfx.CreateSurface(21);

        Assert.Equal(2, presentation.PublishThrough(gfx, 3));

        Assert.Collection(
            gfx.SnapshotVisibleObjects(),
            first => Assert.Equal(
                (100L, 1, 2, 4, 6, 31, 42),
                (first.Handle, first.SrcX, first.SrcY, first.W, first.H,
                 first.DstX, first.DstY)),
            second => Assert.Equal(
                (101L, 5, 2, 4, 6, 35, 42),
                (second.Handle, second.SrcX, second.SrcY, second.W, second.H,
                 second.DstX, second.DstY)));
        Assert.Null(gfx.TryGet(102));
    }

    [Fact]
    public void RepublishRestoresPartialEraseWithoutAdvancingReveal()
    {
        var presentation = Presentation(capacity: 4);
        var gfx = new GfxState();
        gfx.CreateSurface(21);
        presentation.PublishThrough(gfx, 2);
        gfx.EraseRange(101, 1);

        Assert.Single(gfx.SnapshotVisibleObjects());
        Assert.Equal(2, presentation.Republish(gfx));
        Assert.Equal(new long[] { 100, 101 },
            gfx.SnapshotVisibleObjects().Select(item => item.Handle));

        presentation.ErasePublished(gfx);
        Assert.Empty(gfx.SnapshotVisibleObjects());
        Assert.Equal(2, presentation.PublishedGlyphCount);
    }

    [Fact]
    public void ConsecutiveRunsRetainTheirOwnLayoutOrigins()
    {
        var binding = new AdvTextLayoutPresentationBinding(
            7, 27, 200, 10, -1, 53, 10);
        var presentation = new RetainedAdvTextLayoutPresentation(binding);
        presentation.Append(
            [new AdvRetainedGlyphRecord(0, 3, 4, 7, 10)],
            275, 90);
        presentation.Append(
            [new AdvRetainedGlyphRecord(0, 7, 4, 11, 10)],
            275, 135);
        var gfx = new GfxState();
        gfx.CreateSurface(27);

        presentation.PublishThrough(gfx, 2);

        Assert.Equal(
            new[] { (278, 94), (282, 139) },
            gfx.SnapshotVisibleObjects().Select(item => (item.DstX, item.DstY)));
    }

    [Fact]
    public void PublicationCropsTheLayoutSurfaceToTheNativeCell()
    {
        // adv_text_publish_next_glyph@0x451220 binds the record's measured cell as the source rect.
        var presentation = new RetainedAdvTextLayoutPresentation(
            new AdvTextLayoutPresentationBinding(
                1, 21, 100, 4, -1, 10, 20));
        var record = new AdvRetainedGlyphRecord(0, 3, 4, 7, 10);
        presentation.Append([record], layoutOriginX: 30, layoutOriginY: 40);
        var gfx = new GfxState();
        gfx.CreateSurface(21);

        presentation.PublishThrough(gfx, 1);

        RenderObject item = Assert.Single(gfx.SnapshotVisibleObjects());
        Assert.Equal((3, 4, 4, 6, 33, 44),
            (item.SrcX, item.SrcY, item.W, item.H, item.DstX, item.DstY));
    }

    [Fact]
    public void SavedFrameStyleReconstructionRebuildsTheSameBindings()
    {
        RetainedAdvTextLayoutPresentation before = Presentation(capacity: 4);
        var gfx = new GfxState();
        gfx.CreateSurface(21);
        before.PublishThrough(gfx, 3);
        RenderObject[] expected = gfx.SnapshotVisibleObjects().ToArray();

        gfx.EraseRange(
            before.Binding.FirstObjectHandle,
            before.Binding.ObjectCapacity);
        RetainedAdvTextLayoutPresentation reconstructed =
            Presentation(capacity: 4);
        reconstructed.PublishThrough(gfx, 3);

        Assert.Equal(expected, gfx.SnapshotVisibleObjects());
    }

    private static AdvRetainedGlyphRecord Cell(int left, int width) => new(0, left, 0, left + width, 8);

    [Fact]
    public void ASaveBindsTheSourceGlyphsInPlaceOfThePresentedOnes()
    {
        // Localization spec §8: a presented run drew two glyphs where its source layout has three, and a hidden run of
        // the message drew none where its source has two more. Capacity 4 cuts the fifth.
        var presentation = new RetainedAdvTextLayoutPresentation(
            new AdvTextLayoutPresentationBinding(1, 21, 100, 4, -1, 10, 20));
        presentation.AppendRun([Cell(0, 10), Cell(10, 10)], [false, false], 30, 40,
                               sourceRecords: [Cell(0, 20), Cell(20, 20), Cell(40, 20)]);
        presentation.AppendSourceOnly([Cell(60, 20), Cell(80, 20)], 30, 40);
        var gfx = new GfxState();
        gfx.CreateSurface(21);

        Assert.True(presentation.SourceDiffers);
        Assert.Empty(presentation.PublishedSourceGlyphs());
        presentation.PublishThrough(gfx, 1);
        Assert.Equal(new[] { 0 }, presentation.PublishedSourceGlyphs().Select(glyph => glyph.Record.Left));
        presentation.PublishThrough(gfx, 2);
        IReadOnlyList<AdvRetainedGlyphPlacement> source = presentation.PublishedSourceGlyphs();
        Assert.Equal(new[] { 0, 20, 40, 60 }, source.Select(glyph => glyph.Record.Left));

        gfx.GetOrCreate(100).Color = 0x80ffffff;
        GfxPersistenceSnapshot saved = gfx.CapturePersistenceSnapshot([new AdvTextSourceObjects(100, 4, 21, source)]);

        Assert.Equal(new long[] { 100, 101, 102, 103 }, saved.Objects.Select(item => item.Handle));
        Assert.Equal(new[] { (0, 0, 20, 8), (20, 0, 20, 8), (40, 0, 20, 8), (60, 0, 20, 8) },
                     saved.Objects.Select(item => item.Object.SrcRect));
        Assert.Equal((90L, 40L, 0L), saved.Objects[3].Object.V24);
        Assert.All(saved.Objects, item => Assert.True(item.Object.Visible));
        // The drawn object's other state stays with its handle; the live objects are untouched.
        Assert.Equal(0x80ffffffL, saved.Objects[0].Object.Color);
        Assert.Equal(new long[] { 100, 101 }, gfx.SnapshotVisibleObjects().Select(item => item.Handle));
        Assert.Equal((40L, 40L, 0L), gfx.TryGet(101)!.V24);
    }

    [Fact]
    public void ARunWithoutASourcePassSavesWhatItDrewAndAReleasedRangeStaysEmpty()
    {
        var presentation = new RetainedAdvTextLayoutPresentation(
            new AdvTextLayoutPresentationBinding(1, 21, 100, 4, -1, 10, 20));
        presentation.AppendRun([Cell(0, 10), Cell(10, 10)], [false, false], 30, 40);
        var gfx = new GfxState();
        gfx.CreateSurface(21);
        presentation.PublishThrough(gfx, 2);

        Assert.False(presentation.SourceDiffers);
        Assert.Equal(presentation.Glyphs, presentation.PublishedSourceGlyphs());

        presentation.ErasePublished(gfx);
        GfxPersistenceSnapshot saved = gfx.CapturePersistenceSnapshot(
            [new AdvTextSourceObjects(100, 4, 21, presentation.PublishedSourceGlyphs())]);
        Assert.Empty(saved.Objects);
    }

    private static RetainedAdvTextLayoutPresentation Presentation(int capacity)
    {
        var presentation = new RetainedAdvTextLayoutPresentation(
            new AdvTextLayoutPresentationBinding(
                1, 21, 100, capacity, -1, 10, 20));
        presentation.Append(
            [
                new AdvRetainedGlyphRecord(0, 1, 2, 5, 8),
                new AdvRetainedGlyphRecord(0, 5, 2, 9, 8),
                new AdvRetainedGlyphRecord(0, 9, 2, 13, 8),
            ],
            30,
            40);
        return presentation;
    }
}

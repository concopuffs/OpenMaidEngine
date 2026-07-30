using Age.Engine.Model;
using Age.Engine.Text;

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

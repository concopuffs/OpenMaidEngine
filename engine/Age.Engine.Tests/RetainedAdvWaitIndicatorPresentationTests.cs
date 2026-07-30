using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Text;

public class RetainedAdvWaitIndicatorPresentationTests
{
    private static readonly AdvWaitIndicatorConfig Config = new(
        LayoutSlot: 1,
        X: 385,
        Y: 140,
        SurfaceSlot: 12,
        SourceX: 0,
        SourceY: 0,
        CellWidth: 30,
        CellHeight: 27,
        TerminalFrame: 12,
        FramePeriodMs: 48);

    private static readonly AdvTextLayoutPresentationBinding Binding = new(
        LayoutSlot: 1,
        SourceSurfaceSlot: 21,
        FirstObjectHandle: 0xd6d8,
        ObjectCapacity: 500,
        WaitIndicatorObjectHandle: 0xd674,
        ResetCursorX: 100,
        ResetCursorY: 47);

    [Fact]
    public void PublishesAtlasCellAtLayoutRelativePosition()
    {
        var presentation = new RetainedAdvWaitIndicatorPresentation(
            Config, Binding, layoutOriginX: 0, layoutOriginY: 430);
        var gfx = new GfxState();
        gfx.SetSurface(12, 0x337c, 0xff00);

        Assert.True(presentation.Update(gfx, elapsedMs: 96, visible: true));

        RenderObject marker = Assert.Single(gfx.SnapshotVisibleObjects());
        Assert.Equal(
            (0xd674L, 12, 60, 0, 30, 27, 385, 570),
            (marker.Handle, gfx.TryGet(marker.Handle)!.SourceSlot, marker.SrcX, marker.SrcY,
             marker.W, marker.H, marker.DstX, marker.DstY));
        Assert.Equal(2, presentation.PublishedFrame);
    }

    [Fact]
    public void MutatesOnlyWhenFrameChangesAndErasesWhenHidden()
    {
        var presentation = new RetainedAdvWaitIndicatorPresentation(
            Config, Binding, 0, 430);
        var gfx = new GfxState();

        Assert.True(presentation.Update(gfx, 0, visible: true));
        Assert.False(presentation.Update(gfx, 47, visible: true));
        Assert.True(presentation.Update(gfx, 48, visible: true));
        Assert.Equal(30, Assert.Single(gfx.SnapshotVisibleObjects()).SrcX);

        Assert.True(presentation.Update(gfx, 48, visible: false));
        Assert.Empty(gfx.SnapshotVisibleObjects());
        Assert.False(presentation.Update(gfx, 48, visible: false));
    }

    [Fact]
    public void RepublishRestoresAHandleErasedByScript()
    {
        var presentation = new RetainedAdvWaitIndicatorPresentation(
            Config, Binding, 0, 430);
        var gfx = new GfxState();
        presentation.Update(gfx, 144, visible: true);
        gfx.EraseRange(Binding.WaitIndicatorObjectHandle, 1);

        Assert.True(presentation.Republish(gfx, 144, visible: true));

        RenderObject marker = Assert.Single(gfx.SnapshotVisibleObjects());
        Assert.Equal((0xd674L, 90), (marker.Handle, marker.SrcX));
    }

    [Fact]
    public void RejectsMissingHandleAndMismatchedLayout()
    {
        Assert.Throws<ArgumentException>(() =>
            new RetainedAdvWaitIndicatorPresentation(
                Config,
                Binding with { WaitIndicatorObjectHandle = -1 },
                0,
                430));
        Assert.Throws<ArgumentException>(() =>
            new RetainedAdvWaitIndicatorPresentation(
                Config with { LayoutSlot = 2 },
                Binding,
                0,
                430));
    }
}

using Age.Engine.Model;

public class GfxDiagnosticSnapshotTests
{
    [Fact]
    public void SnapshotIdentifiesOnlyChannelsThatCanBlockPresentationWait()
    {
        var gfx = new GfxState();
        gfx.BindDraw(0x100, 7, 0, 0, 32, 32, 0, 0);
        gfx.SetTranslationChannel(0x100, 25, 300, (20, 30, 0));
        gfx.BindDraw(0x200, 8, 0, 0, 32, 32, 0, 0);
        gfx.SetScaleChannel(0x200, 0, 500, (200, 200, 100));
        gfx.SetOneShotAnimationControl(0x200, 1);

        GfxDiagnosticSnapshot snapshot = gfx.CaptureDiagnosticSnapshot(1000);

        Assert.True(snapshot.HasActiveTimedPresentation);
        var blocking = Assert.Single(snapshot.BlockingObjects);
        Assert.Equal(0x100, blocking.Handle);
        Assert.True(blocking.TranslationEnabled);
        Assert.Equal(25, blocking.TranslationDelayMs);
        Assert.Equal(300, blocking.TranslationDurationMs);
    }
}

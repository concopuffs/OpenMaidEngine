using Age.Engine.Model;

public class AdvRetainedTextContractTests
{
    [Fact]
    public void GlyphRecordUsesNativeEdgesAndExactChainFlag()
    {
        var chained = new AdvRetainedGlyphRecord(1, 10, 20, 34, 51);
        var nonChained = chained with { PublicationChainFlag = 2 };

        Assert.Equal((24, 31), (chained.Width, chained.Height));
        Assert.True(chained.PublishesNextInSameTick);
        Assert.False(nonChained.PublishesNextInSameTick);
    }

    [Fact]
    public void OverflowComparisonsAreStrictAndIndependentPerAxis()
    {
        Assert.Equal(
            AdvTextOverflowFlags.None,
            AdvRetainedTextContract.CheckOverflow(100, 50, 100, 50));
        Assert.Equal(
            AdvTextOverflowFlags.Horizontal,
            AdvRetainedTextContract.CheckOverflow(100, 50, 101, 50));
        Assert.Equal(
            AdvTextOverflowFlags.Vertical,
            AdvRetainedTextContract.CheckOverflow(100, 50, 100, 51));
        Assert.Equal(
            AdvTextOverflowFlags.Horizontal | AdvTextOverflowFlags.Vertical,
            AdvRetainedTextContract.CheckOverflow(100, 50, 101, 51));
    }

    [Theory]
    [InlineData(0x8141)] // 、
    [InlineData(0x8142)] // 。
    [InlineData(0x8176)] // 」
    public void NativeClosingPunctuationPreventsHorizontalWrapBefore(int cp932)
        => Assert.True(AdvRetainedTextContract.PreventsHorizontalWrapBefore((ushort)cp932));

    [Fact]
    public void OtherCp932GlyphsMayWrapNormally()
    {
        Assert.False(AdvRetainedTextContract.PreventsHorizontalWrapBefore(0x8140)); // full-width space
        Assert.False(AdvRetainedTextContract.PreventsHorizontalWrapBefore(0x82a0)); // あ
    }

    [Fact]
    public void TimedPublicationStopsAtConfiguredObjectCapacity()
    {
        Assert.True(AdvRetainedTextContract.TimedPublicationHandleExists(0, 500));
        Assert.True(AdvRetainedTextContract.TimedPublicationHandleExists(499, 500));
        Assert.False(AdvRetainedTextContract.TimedPublicationHandleExists(500, 500));
        Assert.False(AdvRetainedTextContract.TimedPublicationHandleExists(-1, 500));
    }
}

using Age.Engine.Hosting;
using Xunit;

public class FrameClockTests
{
    [Fact]
    public void Advance_AtSpeed1_AddsRealMilliseconds()
    {
        var c = new FrameClock();               // Speed defaults to 1.0
        c.Advance(0.016);                        // one ~60fps frame
        Assert.Equal(16, c.NowMs);
    }

    [Fact]
    public void Advance_ScalesBySpeed()
    {
        var c = new FrameClock { Speed = 4.0 };
        c.Advance(0.016);
        Assert.Equal(64, c.NowMs);               // 4x virtual time
    }

    [Fact]
    public void EffectiveBudget_ScalesBySpeed_AndFloorsAtOne()
    {
        Assert.Equal(30,  new FrameClock { OpsPerFrame = 30, Speed = 1.0 }.EffectiveBudget);
        Assert.Equal(120, new FrameClock { OpsPerFrame = 30, Speed = 4.0 }.EffectiveBudget);
        Assert.Equal(1,   new FrameClock { OpsPerFrame = 0,  Speed = 1.0 }.EffectiveBudget);
    }
}

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
    public void Advance_RetainsFractionalMillisecondsAtSlowSpeed()
    {
        var c = new FrameClock { Speed = 0.1 };
        for (int i = 0; i < 10; i++) c.Advance(1.0 / 144.0);
        Assert.Equal(6, c.NowMs);
    }

    [Fact]
    public void WallClockPacer_RateDoesNotDependOnRenderCallbackCount()
    {
        static long Simulate(int callbacks)
        {
            var c = new FrameClock();
            var p = new WallClockOpPacer(c);
            long ops = 0;
            p.OpcodeCompleted(); ops++;
            for (int frame = 0; frame < callbacks; frame++)
            {
                c.Advance(1.0 / callbacks);
                while (p.CanRunNext) { p.OpcodeCompleted(); ops++; }
            }
            return ops;
        }

        long at60 = Simulate(60), at144 = Simulate(144), at240 = Simulate(240);
        Assert.InRange(at60, 198, 202);
        Assert.InRange(at144, 198, 202);
        Assert.InRange(at240, 198, 202);
        Assert.InRange(System.Math.Abs(at60 - at144), 0, 2);
        Assert.InRange(System.Math.Abs(at60 - at240), 0, 2);
    }

    [Fact]
    public void WallClockPacer_ResetDropsParkedTimeCredit()
    {
        var c = new FrameClock();
        var p = new WallClockOpPacer(c);
        p.OpcodeCompleted();
        c.Advance(10);
        p.Reset();
        p.OpcodeCompleted();
        Assert.False(p.CanRunNext);
    }
}

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

    // ---- Real-time mode: the Godot host's clock, read between frames as native reads timeGetTime. ----

    [Fact]
    public void RealTime_AdvancesWithoutFrames()
    {
        var c = FrameClock.StartRealTime();
        Assert.True(c.IsRealTime);
        System.Threading.Thread.Sleep(30);
        Assert.InRange(c.NowMs, 20, 5_000);
    }

    [Fact]
    public void RealTime_IgnoresAdvance()
    {
        var c = FrameClock.StartRealTime();
        long before = c.NowMs;
        c.Advance(10.0);                         // ten seconds of frames would be 10 000 ms
        Assert.InRange(c.NowMs - before, 0, 1_000);
    }

    [Fact]
    public void RealTime_SpeedChangeKeepsTheTimeContinuous()
    {
        var c = FrameClock.StartRealTime();
        System.Threading.Thread.Sleep(20);
        long before = c.NowMs;
        c.Speed = 8.0;
        long after = c.NowMs;
        Assert.InRange(after - before, 0, 100); // no jump: the anchor is rebased, not rescaled
        System.Threading.Thread.Sleep(20);
        Assert.InRange(c.NowMs - after, 100, 40_000); // 20 real ms at 8x
    }

    [Fact]
    public void RealTime_NeverGoesBackwards()
    {
        var c = FrameClock.StartRealTime();
        long previous = c.NowMs;
        for (int i = 0; i < 10_000; i++)
        {
            if (i == 5_000) c.Speed = 0.05;
            long now = c.NowMs;
            Assert.True(now >= previous);
            previous = now;
        }
    }

    [Fact]
    public void RealMillisecondsUntil_FrameSteppedWaitsForFrames()
    {
        var c = new FrameClock();
        c.Advance(0.100);
        Assert.Equal(0, c.RealMillisecondsUntil(100, 50));
        Assert.Equal(50, c.RealMillisecondsUntil(101, 50));   // only a frame moves it
    }

    [Fact]
    public void RealMillisecondsUntil_RealTimeWakesAtTheDeadline()
    {
        var c = FrameClock.StartRealTime();
        long now = c.NowMs;
        Assert.Equal(0, c.RealMillisecondsUntil(now - 1, 50));
        Assert.Equal(50, c.RealMillisecondsUntil(now + 10_000, 50));
        c.Speed = 4.0;
        Assert.InRange(c.RealMillisecondsUntil(c.NowMs + 40, 50), 1, 10);   // 40 virtual ms = 10 real
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

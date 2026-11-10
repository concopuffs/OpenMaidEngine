using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Xunit;

public class AdvAutoAdvanceTimerTests
{
    [Fact]
    public void UnvoicedWait_UsesAutoMessageTime1()
    {
        var timer = new AdvAutoAdvanceTimer();
        var state = new AdvAutoWaitState(true, false, 500, 2000);

        Assert.False(timer.Poll(state, false, 1000));
        Assert.False(timer.Poll(state, false, 2999));
        Assert.True(timer.Poll(state, false, 3000));
    }

    [Fact]
    public void VoicedWait_WaitsForVoiceThenUsesAutoMessageTime0()
    {
        var timer = new AdvAutoAdvanceTimer();
        var state = new AdvAutoWaitState(true, true, 500, 2000);

        Assert.False(timer.Poll(state, true, 1000));
        Assert.False(timer.Poll(state, true, 9000));
        Assert.False(timer.Poll(state, false, 9000));
        Assert.False(timer.Poll(state, false, 9499));
        Assert.True(timer.Poll(state, false, 9500));
    }

    [Fact]
    public void AWaitWithNoTimerOfItsOwn_IsDueAtOnceUnlessAVoiceIsPending()
    {
        // Op 0xfa arms no Auto timer: its poll reads the shared timer an earlier page armed, so an unvoiced wait is due
        // at the first poll, and a pending voice still waits for its end and AutoMessageTime0.
        var unvoiced = new AdvAutoAdvanceTimer(unvoicedDeadlineDue: true);
        Assert.True(unvoiced.Poll(new AdvAutoWaitState(true, false, 500, 2000), false, 1000));

        var voiced = new AdvAutoAdvanceTimer(unvoicedDeadlineDue: true);
        var state = new AdvAutoWaitState(true, true, 500, 2000);
        Assert.False(voiced.Poll(state, true, 1000));
        Assert.False(voiced.Poll(state, false, 4000));
        Assert.True(voiced.Poll(state, false, 4500));
    }

    [Fact]
    public void DisablingAuto_CancelsDeadlineAndReenableStartsFresh()
    {
        var timer = new AdvAutoAdvanceTimer();
        var on = new AdvAutoWaitState(true, false, 500, 2000);

        Assert.False(timer.Poll(on, false, 0));
        Assert.False(timer.Poll(on with { Enabled = false }, false, 1500));
        Assert.False(timer.Poll(on, false, 5000));
        Assert.False(timer.Poll(on, false, 6999));
        Assert.True(timer.Poll(on, false, 7000));
    }

    [Fact]
    public void ZeroConfiguration_UsesNativeHundredMillisecondFallback()
    {
        var timer = new AdvAutoAdvanceTimer();
        var state = new AdvAutoWaitState(true, false, 0, 0);

        Assert.False(timer.Poll(state, false, 0));
        Assert.False(timer.Poll(state, false, 99));
        Assert.True(timer.Poll(state, false, 100));
    }
}

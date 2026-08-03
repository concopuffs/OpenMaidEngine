using Age.Engine.Hosting;
using Age.Engine.Model;
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

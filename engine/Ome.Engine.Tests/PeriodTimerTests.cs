using Ome.Engine.Hosting;

/// <summary>Native's polled period timer (sleep_timer_arm@0x44cff0, period_timer_poll_elapsed_periods@0x44d0f0), as
/// the ADV glyph reveal uses it (docs/engine-re.md, "The reveal clock").</summary>
public class PeriodTimerTests
{
    /// <summary>The times at which a 1 ms poll from <paramref name="fromMs"/> succeeds, <paramref name="count"/>
    /// times.</summary>
    private static List<long> Ticks(PeriodTimer timer, long fromMs, int count)
    {
        var ticks = new List<long>();
        for (long now = fromMs; ticks.Count < count && now < fromMs + 100_000; now++)
            if (timer.Poll(now) >= 0) ticks.Add(now);
        return ticks;
    }

    [Fact]
    public void TheFirstTickWaitsAFullPeriodAndEachTickOneMore()
    {
        var timer = new PeriodTimer();
        timer.Arm(1000, 100);

        Assert.Equal(-1, timer.Poll(1100));       // boundary 1 passes only once the elapsed time exceeds it
        Assert.Equal(1101, timer.NextDueMs);
        Assert.Equal(new long[] { 1101, 1201, 1301 }, Ticks(timer, 1000, 3));
    }

    [Fact]
    public void ALatePollTakesOneTickAndDropsTheBoundariesItMissed()
    {
        var timer = new PeriodTimer();
        timer.Arm(0, 100);

        Assert.Equal(2, timer.Poll(350));         // boundaries 1-3 crossed: one success, index 4
        Assert.Equal(401, timer.NextDueMs);
        Assert.Equal(-1, timer.Poll(400));
        Assert.Equal(0, timer.Poll(401));
    }

    [Fact]
    public void AnUnarmedTimerNeverElapsesAndAZeroPeriodBecomesOneMillisecond()
    {
        var unarmed = new PeriodTimer();
        Assert.Equal(-1, unarmed.Poll(1_000_000));
        Assert.Null(unarmed.NextDueMs);

        var zero = new PeriodTimer();
        zero.Arm(10, 0);
        Assert.Equal(1, zero.PeriodMs);
        Assert.Equal(new long[] { 12, 14 }, Ticks(zero, 10, 2));   // index jumps past the boundary just crossed
    }

    [Fact]
    public void AFuriganaRunKeepsTheGlyphTimerOfTheShowTextBeforeIt()
    {
        // SC0000 page 18 at MessageSpeed=100: op 0x6e 今にも消えそうな (8 ticks) arms the timer, op 0x196 篝火+かがりび
        // (4 ticks) polls on without re-arming, and op 0x6e の如く… (23 ticks) arms it again. Native: 0.8, 0.4, 2.3 s.
        var timer = new PeriodTimer();
        timer.Arm(0, 100);
        List<long> first = Ticks(timer, 0, 8);
        List<long> furigana = Ticks(timer, first[^1], 4);
        timer.Arm(furigana[^1], 100);
        List<long> following = Ticks(timer, furigana[^1], 23);

        Assert.Equal(801, first[^1]);
        Assert.Equal(new long[] { 901, 1001, 1101, 1201 }, furigana);
        Assert.Equal(1201 + 2301, following[^1]);
    }
}

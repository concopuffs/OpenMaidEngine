using Ome.Engine.Model;
using Xunit;

/// <summary>The bound the port's image and texture caches share (backlog, "The port's image and texture caches never
/// evict").</summary>
public class ByteBudgetCacheTests
{
    [Fact]
    public void TrimDropsTheLeastRecentlyUsedUntilTheCacheIsWithinItsBudget()
    {
        var dropped = new List<string>();
        var cache = new ByteBudgetCache<string, int>(100, (key, _) => dropped.Add(key));
        cache.Set("a", 1, 40);
        cache.Set("b", 2, 40);
        cache.Set("c", 3, 40);
        Assert.True(cache.TryGet("a", out _));   // a is now used later than b

        cache.Trim();

        Assert.Equal(["b"], dropped);
        Assert.Equal(80, cache.Bytes);
        Assert.Equal(1, cache.Trimmed);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out int a) && a == 1);
    }

    [Fact]
    public void WhatTheCurrentFrameUsesStaysEvenPastTheBudget()
    {
        var dropped = new List<string>();
        var cache = new ByteBudgetCache<string, int>(50, (key, _) => dropped.Add(key));
        cache.Set("old", 0, 40);
        cache.BeginFrame();
        cache.Set("x", 1, 40);
        cache.Set("y", 2, 40);

        cache.Trim();
        Assert.Equal(["old"], dropped);
        Assert.Equal(80, cache.Bytes);   // over the budget: the frame draws both

        cache.BeginFrame();
        Assert.True(cache.TryGet("y", out _));
        cache.Trim();
        Assert.Equal(["old", "x"], dropped);
        Assert.Equal(40, cache.Bytes);
    }

    [Fact]
    public void WithoutAFrameEveryEntryMayGo()
    {
        var cache = new ByteBudgetCache<int, int>(10);
        cache.Set(1, 1, 20);
        cache.Trim();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void SettingAKeyAgainReplacesItsValueAndBytesWithoutDroppingIt()
    {
        var dropped = new List<int>();
        var cache = new ByteBudgetCache<string, int>(100, (_, value) => dropped.Add(value));
        cache.Set("a", 1, 30);
        cache.Set("a", 2, 50);
        Assert.Empty(dropped);
        Assert.Equal(50, cache.Bytes);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("a", out int value) && value == 2);
    }

    [Fact]
    public void RemoveRemoveWhereAndClearCallTheDropCallback()
    {
        var dropped = new List<string>();
        var cache = new ByteBudgetCache<string, int>(1000, (key, _) => dropped.Add(key));
        foreach (string key in new[] { "a", "b", "c", "d" }) cache.Set(key, 0, 10);

        cache.Remove("a");
        cache.Remove("missing");
        cache.RemoveWhere(key => key == "b");
        cache.Clear();

        Assert.Equal(["a", "b", "c", "d"], dropped.Order());
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(0, cache.Trimmed);
    }
}

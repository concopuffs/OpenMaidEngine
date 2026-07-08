using Age.Engine.Model;
using Xunit;

public class BlendMathTests
{
    [Fact]
    public void NegativeColorKey_IsNoKey()
    {
        Assert.False(BlendMath.HasColorKey(-1));
        Assert.False(BlendMath.ColorKeyMatches(0, 0, 0, -1));   // never matches when no key
    }

    [Fact]
    public void ZeroColorKey_KeysBlack()
    {
        Assert.True(BlendMath.HasColorKey(0));
        Assert.True(BlendMath.ColorKeyMatches(0, 0, 0, 0));      // (0,0,0) == key black
        Assert.False(BlendMath.ColorKeyMatches(1, 0, 0, 0));    // near-black is NOT keyed (exact match)
    }

    [Fact]
    public void ColorKey_MatchesRgb888Bytes()
    {
        long key = 0xFF00FF;                                    // magenta 0xRRGGBB
        Assert.True(BlendMath.ColorKeyMatches(0xFF, 0x00, 0xFF, key));
        Assert.False(BlendMath.ColorKeyMatches(0xFE, 0x00, 0xFF, key));
    }

    [Fact]
    public void UnpackArgb_SplitsBytes()
    {
        Assert.Equal((0x80, 0x12, 0x34, 0x56), BlendMath.UnpackArgb(0x80123456));
    }
}

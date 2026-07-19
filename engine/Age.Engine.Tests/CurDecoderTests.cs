using System.Linq;
using Age.Engine.Sys4;
using Xunit;

public class CurDecoderTests
{
    [Fact]
    public void HimegariCursor_DecodesPixelsAndHotspot()
    {
        var resources = ResourceMap.Load();
        var entry = resources.ResolveCursor(0x3318);

        Assert.NotNull(entry);
        Assert.Equal("CURSOR03.CUR", entry!.Name);
        var cursor = resources.DecodeCursor(entry);

        Assert.Equal(32, cursor.Image.Width);
        Assert.Equal(32, cursor.Image.Height);
        Assert.Equal(25, cursor.HotspotX);
        Assert.Equal(25, cursor.HotspotY);
        Assert.Contains(cursor.Image.Pixels.Where((_, i) => i % 4 == 3), alpha => alpha == 0);
        Assert.Contains(cursor.Image.Pixels.Where((_, i) => i % 4 == 3), alpha => alpha == 255);
    }

    [Fact]
    public void TruncatedCursor_IsRejected()
        => Assert.Throws<InvalidDataException>(() => CurDecoder.Decode(new byte[21], "bad.cur"));
}

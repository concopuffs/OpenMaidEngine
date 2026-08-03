using System.Linq;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Xunit;

public class CurDecoderTests
{
    [Fact]
    [Trait("Category", "Workspace")]
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
    [Trait("Category", "Workspace")]
    public void HimegariPanCursor_DecodesFourBitPixelsAndHotspot()
    {
        var resources = ResourceMap.Load();
        var entry = resources.ResolveCursor(0x32ce);

        Assert.NotNull(entry);
        Assert.Equal("CURSOR09.CUR", entry!.Name);
        var cursor = resources.DecodeCursor(entry);

        Assert.Equal(32, cursor.Image.Width);
        Assert.Equal(32, cursor.Image.Height);
        Assert.Equal(16, cursor.HotspotX);
        Assert.Equal(14, cursor.HotspotY);
        AssertPixel(cursor.Image, 0, 0, 0, 0, 0, 0);
        AssertPixel(cursor.Image, 10, 10, 128, 128, 128, 255);
        AssertPixel(cursor.Image, 12, 24, 0, 0, 128, 255);
    }

    [Fact]
    public void TruncatedCursor_IsRejected()
        => Assert.Throws<InvalidDataException>(() => CurDecoder.Decode(new byte[21], "bad.cur"));

    private static void AssertPixel(RgbaImage image, int x, int y,
                                    byte r, byte g, byte b, byte a)
    {
        int offset = (y * image.Width + x) * 4;
        Assert.Equal(r, image.Pixels[offset]);
        Assert.Equal(g, image.Pixels[offset + 1]);
        Assert.Equal(b, image.Pixels[offset + 2]);
        Assert.Equal(a, image.Pixels[offset + 3]);
    }
}

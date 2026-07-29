using Age.Engine.Model;
using Age.Engine.Sys4;
using Xunit;

public class MovieMaskSurfaceTests
{
    [Fact]
    public void ExtractGreenUsesLogicalRowsAndRequestedMaskDimensions()
    {
        var frame = new RgbaImage(2, 2,
        [
            1, 10, 2, 255, 3, 20, 4, 255,
            5, 30, 6, 255, 7, 40, 8, 255,
        ]);

        Assert.Equal(new byte[] { 10, 20, 99, 30, 40, 99 },
            MovieMaskSurface.ExtractGreen(frame, 3, 2, 99));
    }

    [Fact]
    public void ApplyPreservesRgbAndUsesExactPackedCarryAlpha()
    {
        var captured = new RgbaImage(2, 1,
        [
            0, 0, 0, 128,
            255, 0, 0, 128,
        ]);

        RgbaImage result = MovieMaskSurface.Apply(captured, [255, 255], 2, 1, 0, 0);

        Assert.Equal(new byte[]
        {
            0, 0, 0, 127,       // packed 0x80000000: native differs from alpha*255/255
            255, 0, 0, 128,     // packed red carry reaches the output alpha byte
        }, result.Pixels);
        Assert.Equal(new byte[] { 0, 0, 0, 128, 255, 0, 0, 128 }, captured.Pixels);
    }

    [Fact]
    public void ApplyClipsSignedRectangleAndLeavesOutsidePixelsUnchanged()
    {
        var captured = new RgbaImage(3, 1,
        [
            1, 2, 3, 255,
            4, 5, 6, 255,
            7, 8, 9, 255,
        ]);

        RgbaImage result = MovieMaskSurface.Apply(captured, [0, 64, 128], 3, 1, -1, 0);

        Assert.Equal((byte)63, result.Pixels[3]);  // mask column 1
        Assert.Equal((byte)127, result.Pixels[7]); // mask column 2
        Assert.Equal((byte)255, result.Pixels[11]);
    }
}

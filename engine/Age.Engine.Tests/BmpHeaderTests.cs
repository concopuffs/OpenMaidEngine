using System.IO;
using Age.Engine.Sys4;
using Xunit;

public class BmpHeaderTests
{
    [Fact]
    public void ReadDimsReadsWidthAndHeightFromBmpHeader()
    {
        // Minimal 54-byte BMP header (BITMAPFILEHEADER 14 + BITMAPINFOHEADER 40); width=4, height=3.
        var b = new byte[54];
        b[0] = (byte)'B'; b[1] = (byte)'M';
        System.BitConverter.GetBytes(40).CopyTo(b, 14);   // header size
        System.BitConverter.GetBytes(4).CopyTo(b, 18);    // width
        System.BitConverter.GetBytes(3).CopyTo(b, 22);    // height
        var tmp = Path.Combine(Path.GetTempPath(), "agehdr_test.bmp");
        File.WriteAllBytes(tmp, b);
        try
        {
            var (w, h) = BmpHeader.ReadDims(tmp);
            Assert.Equal(4, w);
            Assert.Equal(3, h);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ReadDimsReturnsZeroForMissingFile()
    {
        var (w, h) = BmpHeader.ReadDims(Path.Combine(Path.GetTempPath(), "does_not_exist_agehdr.bmp"));
        Assert.Equal((0, 0), (w, h));
    }
}

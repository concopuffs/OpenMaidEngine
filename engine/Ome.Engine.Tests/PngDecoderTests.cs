using System.Buffers.Binary;
using System.IO.Compression;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>The PNG input of language assets (localization spec §12): PNGs built here by hand, so the decoder is
/// checked against an independent encoding of each colour type, filter and the interlace.</summary>
public sealed class PngDecoderTests
{
    private static RgbaImage Sample(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                pixels[at] = (byte)(x * 37 + y * 11);
                pixels[at + 1] = (byte)(x * 5 + y * 71);
                pixels[at + 2] = (byte)(255 - x * 13);
                pixels[at + 3] = (byte)(x * 29 + y * 3 + 7);
            }
        return new RgbaImage(width, height, pixels);
    }

    [Fact]
    public void TheWritersImagesDecodeToTheSamePixels()
    {
        RgbaImage image = Sample(13, 7);
        RgbaImage decoded = PngDecoder.Decode(PngWriter.Write(image));
        Assert.Equal((13, 7), (decoded.Width, decoded.Height));
        Assert.Equal(image.Pixels, decoded.Pixels);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryRowFilterIsUndone(int filter)
    {
        RgbaImage image = Sample(9, 5);
        var rows = new List<byte[]>();
        for (int y = 0; y < image.Height; y++) rows.Add(image.Pixels.AsSpan(y * 36, 36).ToArray());
        byte[] png = Png(9, 5, depth: 8, colour: 6, Filtered(rows, 4, filter));
        Assert.Equal(image.Pixels, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void APalettedImageTakesItsAlphaFromTrns()
    {
        // Two-bit indices, four pixels to a byte: 0 1 2 3 / 3 2 1 0.
        byte[][] rows = [[0b00_01_10_11], [0b11_10_01_00]];
        byte[] palette = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120];
        byte[] png = Png(4, 2, depth: 2, colour: 3, Filtered(rows, 1, 0),
                         ("PLTE", palette), ("tRNS", [0, 128]));
        byte[] pixels = PngDecoder.Decode(png).Pixels;
        Assert.Equal(new byte[] { 10, 20, 30, 0, 40, 50, 60, 128, 70, 80, 90, 255, 100, 110, 120, 255 }, pixels[..16]);
        Assert.Equal(new byte[] { 100, 110, 120, 255 }, pixels[16..20]);
    }

    [Fact]
    public void SixteenBitGrayKeepsTheHighByteAndItsTransparentValue()
    {
        byte[][] rows = [[0x12, 0x34, 0xab, 0xcd, 0xff, 0xff]];
        byte[] png = Png(3, 1, depth: 16, colour: 0, Filtered(rows, 2, 0), ("tRNS", [0xab, 0xcd]));
        Assert.Equal(new byte[] { 0x12, 0x12, 0x12, 255, 0xab, 0xab, 0xab, 0, 0xff, 0xff, 0xff, 255 },
                     PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void OneBitGrayScalesToBlackAndWhite()
    {
        byte[] png = Png(3, 1, depth: 1, colour: 0, Filtered([[0b101_00000]], 1, 0));
        Assert.Equal(new byte[] { 255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255 }, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void AnAdam7ImageIsAssembledFromItsSevenPasses()
    {
        RgbaImage image = Sample(11, 9);
        (int X, int Y, int DX, int DY)[] passes =
            [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
        using var stream = new MemoryStream();
        foreach (var (x0, y0, dx, dy) in passes)
            for (int y = y0; y < image.Height; y += dy)
            {
                var row = new List<byte>();
                for (int x = x0; x < image.Width; x += dx) row.AddRange(image.Pixels.AsSpan((y * image.Width + x) * 4, 4).ToArray());
                if (row.Count == 0) continue;
                stream.WriteByte(0);
                stream.Write(row.ToArray());
            }
        byte[] png = Png(11, 9, depth: 8, colour: 6, stream.ToArray(), interlace: 1);
        Assert.Equal(image.Pixels, PngDecoder.Decode(png).Pixels);
    }

    [Fact]
    public void BrokenFilesAreRefusedWithTheirName()
    {
        byte[] good = PngWriter.Write(Sample(4, 4));

        byte[] crc = (byte[])good.Clone();
        crc[^20] ^= 1;   // inside IDAT's data
        Assert.Contains("fails its CRC", Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(crc, "TITLE.png")).Message);

        byte[] critical = Png(1, 1, 8, 6, [0, 1, 2, 3, 4], ("ABCD", []));
        Assert.Contains("unknown critical chunk ABCD", Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(critical)).Message);
        byte[] ancillary = Png(1, 1, 8, 6, [0, 1, 2, 3, 4], ("abCD", [9]));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, PngDecoder.Decode(ancillary).Pixels);

        byte[] shortData = Png(2, 2, 8, 6, [0, 1, 2, 3, 4]);
        Assert.Contains("image data is", Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(shortData)).Message);

        byte[] huge = Png(PngDecoder.MaxDimension + 1, 1, 8, 6, [0]);
        Assert.Contains("is outside", Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(huge)).Message);

        Assert.Contains("expected a PNG", Assert.Throws<InvalidDataException>(() => PngDecoder.Decode("BM"u8)).Message);
        Assert.Contains("TITLE.png", Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(good[..40], "TITLE.png")).Message);
    }

    // ---- an independent encoder ----------------------------------------------------------------------------------

    private static byte[] Filtered(IReadOnlyList<byte[]> rows, int bpp, int filter)
    {
        using var output = new MemoryStream();
        byte[] previous = new byte[rows[0].Length];
        foreach (byte[] row in rows)
        {
            output.WriteByte((byte)filter);
            for (int i = 0; i < row.Length; i++)
            {
                int a = i >= bpp ? row[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                int predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                output.WriteByte((byte)(row[i] - predictor));
            }
            previous = row;
        }
        return output.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] Png(int width, int height, int depth, int colour, byte[] scanlines,
                              params (string Type, byte[] Data)[] before)
        => Png(width, height, depth, colour, scanlines, 0, before);

    private static byte[] Png(int width, int height, int depth, int colour, byte[] scanlines, int interlace,
                              params (string Type, byte[] Data)[] before)
    {
        using var output = new MemoryStream();
        output.Write(PngDecoder.Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)depth;
        header[9] = (byte)colour;
        header[12] = (byte)interlace;
        Chunk(output, "IHDR", header);
        foreach (var (type, data) in before) Chunk(output, type, data);
        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(scanlines);
            Chunk(output, "IDAT", raw.ToArray());
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var word = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        byte[] body = [.. type.Select(c => (byte)c), .. data];
        output.Write(body);
        uint c = 0xffffffffu;
        foreach (byte b in body)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(word, c ^ 0xffffffffu);
        output.Write(word);
    }
}

using System.Buffers.Binary;
using Age.Engine.Model;
using Age.Engine.Sys4;

namespace Age.Engine.Tests;

public class AgfDecoderTests
{
    [Fact]
    public void DecodesRawFourBitPaletteWithBottomUpStride()
    {
        byte[] palette = Palette((0x10, 0x20, 0x30), (0x40, 0x50, 0x60), (0x70, 0x80, 0x90));
        byte[] bottomUp = { 0x12, 0x10, 0, 0, 0x01, 0x20, 0, 0 };
        var image = AgfDecoder.Decode(BuildAgf(1, 3, 2, 4, palette, bottomUp));

        Assert.Equal((3, 2), (image.Width, image.Height));
        Assert.Equal(new byte[] {
            0x10,0x20,0x30,255, 0x40,0x50,0x60,255, 0x70,0x80,0x90,255,
            0x40,0x50,0x60,255, 0x70,0x80,0x90,255, 0x40,0x50,0x60,255,
        }, image.Pixels);
    }

    [Fact]
    public void DecodesCompressedEightBitPaletteAndAcifAlpha()
    {
        byte[] palette = Palette(256, (1, 2, 3), (4, 5, 6), (7, 8, 9));
        byte[] bottomUp = { 2, 1, 0, 0, 0, 1, 2, 0 };
        byte[] alpha = { 10, 20, 30, 40, 50, 60 };
        var image = AgfDecoder.Decode(BuildAgf(2, 3, 2, 8, palette, bottomUp, alpha,
                                               compressInfo: true, compressPixels: true, compressAlpha: true));

        Assert.Equal(new byte[] {
            1,2,3,10, 4,5,6,20, 7,8,9,30,
            7,8,9,40, 4,5,6,50, 1,2,3,60,
        }, image.Pixels);
    }

    [Fact]
    public void DecodesTruecolorAndDefaultsMissingAlphaToOpaque()
    {
        byte[] bottomUp = { 30,20,10,0, 60,50,40,0 };
        var image = AgfDecoder.Decode(BuildAgf(2, 2, 1, 32, null, bottomUp));
        Assert.Equal(new byte[] { 10,20,30,255, 40,50,60,255 }, image.Pixels);
    }

    [Fact]
    public void DecodesThroughAssetStoreByteSeam()
    {
        byte[] agf = BuildAgf(1, 1, 1, 24, null, new byte[] { 3, 2, 1, 0 });
        var entry = new AssetEntry("TEST.AGF", "DATA.ALF", 0, agf.Length);
        var image = AgfDecoder.Decode(new MemoryStore(agf), entry);
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, image.Pixels);
    }

    [Fact]
    public void BmpPayloadUnderAgfNameRequiresExplicitCompatibilityOption()
    {
        byte[] bmp = BuildBmp(1, 1, 32, topDown: false,
                              new byte[] { 3, 2, 1, 0 });

        var error = Assert.Throws<InvalidDataException>(
            () => AgfDecoder.Decode(bmp, "PATCHED.AGF"));

        Assert.Contains("PATCHED.AGF", error.Message);
        Assert.Contains("--allow-bmp-as-agf", error.Message);
    }

    [Fact]
    public void ExplicitCompatibilityDecodesBottomUpPaddedBmpAsOpaqueRgba()
    {
        byte[] bottomUp = {
            90,80,70, 60,50,40, 0,0,
            30,20,10, 6,5,4, 0,0,
        };
        byte[] bmp = BuildBmp(2, 2, 24, topDown: false, bottomUp);

        var image = AgfDecoder.Decode(bmp, "PATCHED.AGF", allowBmpAsAgf: true);

        Assert.Equal((2, 2), (image.Width, image.Height));
        Assert.Equal(new byte[] {
            10,20,30,255, 4,5,6,255,
            70,80,90,255, 40,50,60,255,
        }, image.Pixels);
    }

    [Fact]
    public void ExplicitCompatibilityDecodesTopDownBmpWithStraightAlpha()
    {
        byte[] bmp = BuildBmp(2, 1, 32, topDown: true,
                              new byte[] { 3,2,1,0, 6,5,4,127 });

        var image = AgfDecoder.Decode(bmp, "PATCHED.AGF", allowBmpAsAgf: true);

        Assert.Equal(new byte[] { 1,2,3,0, 4,5,6,127 }, image.Pixels);
    }

    [Theory]
    [InlineData("AE000A.AGF")]
    [InlineData("AE001A.AGF")]
    [InlineData("BG030A.AGF")]
    [InlineData("EV052CA.AGF")]
    [InlineData("SO001.AGF")]
    public void InstalledAssetMatchesExistingConverterPixels(string name)
    {
        string bmpPath = Path.Combine(Paths.Textures, Path.ChangeExtension(name, ".BMP"));
        if (!File.Exists(bmpPath)) return;
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        var image = AgfDecoder.Decode(store, catalog.ResolveName(name)!);
        var oracle = ReadBmp32(bmpPath);
        Assert.Equal((oracle.Width, oracle.Height), (image.Width, image.Height));
        Assert.Equal(oracle.Pixels, image.Pixels);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void InstalledSo001HasExpectedAlphaBearingDimensions()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        var resources = new ResourceMap(catalog, store);
        Assert.Equal("SO001.AGF", resources.ResolveTexture(0x337e)?.Name);
        var image = AgfDecoder.Decode(store, catalog.ResolveRaw(0x337e)!);
        Assert.Equal((800, 300), (image.Width, image.Height));
        Assert.Contains(image.Pixels.Where((_, i) => (i & 3) == 3), a => a is > 0 and < 255);
    }

    [Theory]
    [InlineData(0x32da, "SO005.AGF")]
    [InlineData(0x32db, "SO007.AGF")]
    [InlineData(0x32dc, "SO008A.AGF")]
    [InlineData(0x32dd, "SO007A.AGF")]
    [Trait("Category", "Workspace")]
    public void InstalledFieldMapSheetsResolveAndDecodeByRawCatalogIndex(int rawId, string name)
    {
        var resources = ResourceMap.Load();
        var asset = resources.ResolveTexture(rawId);
        Assert.NotNull(asset);
        Assert.Equal(name, asset.Name);
        var image = resources.DecodeTexture(asset);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }

    private sealed class MemoryStore(byte[] bytes) : IAssetStore
    {
        public Stream Open(AssetEntry entry) => new MemoryStream(bytes, writable: false);
        public byte[] ReadAll(AssetEntry entry) => bytes;
    }

    [Fact]
    public void ExplicitCompatibilityDecodesBottomUpEightBitPalettedBmpWithDeclaredColorCount()
    {
        // Width 3 pads each 8-bit row to 4 bytes; bottom-up storage lists the lower row first.
        byte[] palette = Palette(3, (255, 0, 0), (0, 255, 0), (0, 0, 255));
        byte[] bottomUp = [2, 1, 0, 0xEE, 0, 1, 2, 0xEE];
        byte[] bmp = BuildPalettedBmp(3, 2, 8, topDown: false, palette, declaredColors: 3, bottomUp);

        var image = AgfDecoder.Decode(bmp, "EIGHT.AGF", allowBmpAsAgf: true);

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new byte[]
        {
            255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255,
            0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255,
        }, image.Pixels);
    }

    [Fact]
    public void DecodesTopDownFourBitPalettedBmpWithImplicitFullPalette()
    {
        // Nibbles are high-first: 0x12 is indices 1 then 2; the third pixel is 0x3_ and its low nibble is padding.
        byte[] palette = Palette(16, (0, 0, 0), (10, 20, 30), (40, 50, 60), (70, 80, 90));
        byte[] pixels = [0x12, 0x30, 0, 0];
        byte[] bmp = BuildPalettedBmp(3, 1, 4, topDown: true, palette, declaredColors: 0, pixels);

        var image = BmpDecoder.Decode(bmp, "FOUR.BMP");

        Assert.Equal(new byte[] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255 }, image.Pixels);
    }

    [Fact]
    public void DecodesOneBitPalettedBmpMostSignificantBitFirstAcrossBytes()
    {
        byte[] palette = Palette(2, (0, 0, 0), (255, 255, 255));
        byte[] pixels = [0b1010_0000, 0b1000_0000, 0, 0];
        byte[] bmp = BuildPalettedBmp(9, 1, 1, topDown: true, palette, declaredColors: 0, pixels);

        var image = BmpDecoder.Decode(bmp, "ONE.BMP");

        byte[] expected = [255, 0, 255, 0, 0, 0, 0, 0, 255];
        Assert.Equal(expected, Enumerable.Range(0, 9).Select(x => image.Pixels[x * 4]).ToArray());
        Assert.All(Enumerable.Range(0, 9), x => Assert.Equal(255, image.Pixels[x * 4 + 3]));
    }

    [Fact]
    public void RejectsMalformedPalettedBmps()
    {
        byte[] palette = Palette(2, (1, 2, 3), (4, 5, 6));

        byte[] outOfRange = BuildPalettedBmp(1, 1, 8, topDown: true, palette, declaredColors: 2, [2, 0, 0, 0]);
        Assert.Contains("palette index 2 of 2",
            Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode(outOfRange, "INDEX.BMP")).Message);

        byte[] truncated = BuildPalettedBmp(1, 1, 8, topDown: true, palette, declaredColors: 2, [0, 0, 0, 0]);
        Put32(truncated, 10, 14 + 40 + 4);
        Assert.Contains("palette is truncated",
            Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode(truncated, "SHORT.BMP")).Message);

        byte[] oversized = BuildPalettedBmp(1, 1, 1, topDown: true, palette, declaredColors: 3, [0, 0, 0, 0]);
        Assert.Contains("invalid BMP palette size 3",
            Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode(oversized, "MANY.BMP")).Message);

        byte[] sixteenBit = BuildBmp(1, 1, 16, topDown: true, [0, 0, 0, 0]);
        Assert.Contains("unsupported BMP depth 16",
            Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode(sixteenBit, "HIGH.BMP")).Message);
    }

    private static byte[] BuildPalettedBmp(int width, int height, int bitsPerPixel, bool topDown,
                                           byte[] palette, int declaredColors, byte[] pixels)
    {
        int pixelOffset = 14 + 40 + palette.Length;
        var file = new byte[pixelOffset + pixels.Length];
        "BM"u8.CopyTo(file);
        Put32(file, 2, file.Length);
        Put32(file, 10, pixelOffset);
        Put32(file, 14, 40);
        Put32(file, 18, width);
        Put32(file, 22, topDown ? -height : height);
        Put16(file, 26, 1);
        Put16(file, 28, bitsPerPixel);
        Put32(file, 34, pixels.Length);
        Put32(file, 46, declaredColors);
        palette.CopyTo(file, 14 + 40);
        pixels.CopyTo(file, pixelOffset);
        return file;
    }

    private static byte[] Palette(params (byte R, byte G, byte B)[] colors) => Palette(16, colors);

    private static byte[] BuildBmp(
        int width, int height, int bitsPerPixel, bool topDown, byte[] pixels)
    {
        const int pixelOffset = 14 + 40;
        var file = new byte[pixelOffset + pixels.Length];
        "BM"u8.CopyTo(file);
        Put32(file, 2, file.Length);
        Put32(file, 10, pixelOffset);
        Put32(file, 14, 40);
        Put32(file, 18, width);
        Put32(file, 22, topDown ? -height : height);
        Put16(file, 26, 1);
        Put16(file, 28, bitsPerPixel);
        Put32(file, 34, pixels.Length);
        pixels.CopyTo(file, pixelOffset);
        return file;
    }
    private static byte[] Palette(int count, params (byte R, byte G, byte B)[] colors)
    {
        var result = new byte[count * 4];
        for (int i = 0; i < colors.Length; i++)
        { result[i * 4] = colors[i].B; result[i * 4 + 1] = colors[i].G; result[i * 4 + 2] = colors[i].R; }
        return result;
    }

    private static byte[] BuildAgf(int type, int width, int height, int bpp, byte[]? palette, byte[] pixels,
                                   byte[]? alpha = null, bool compressInfo = false,
                                   bool compressPixels = false, bool compressAlpha = false)
    {
        var info = new byte[0x38 + (palette?.Length ?? 0)];
        Put32(info, 0x14, width); Put32(info, 0x18, height); Put16(info, 0x1c, 1); Put16(info, 0x1e, bpp);
        palette?.CopyTo(info, 0x38);
        byte[] packedInfo = compressInfo ? LiteralLzss(info) : info;
        byte[] packedPixels = compressPixels ? LiteralLzss(pixels) : pixels;
        byte[]? packedAlpha = alpha == null ? null : compressAlpha ? LiteralLzss(alpha) : alpha;
        int length = 0x18 + packedInfo.Length + 12 + packedPixels.Length +
                     (alpha == null ? 0 : 0x24 + packedAlpha!.Length);
        var file = new byte[length];
        "ACGF"u8.CopyTo(file); Put32(file, 4, type);
        Put32(file, 0x0c, info.Length); Put32(file, 0x14, packedInfo.Length);
        packedInfo.CopyTo(file, 0x18);
        int p = 0x18 + packedInfo.Length;
        Put32(file, p + 4, pixels.Length); Put32(file, p + 8, packedPixels.Length);
        packedPixels.CopyTo(file, p + 12); p += 12 + packedPixels.Length;
        if (alpha != null)
        {
            "ACIF"u8.CopyTo(file.AsSpan(p)); Put32(file, p + 0x1c, alpha.Length);
            Put32(file, p + 0x20, packedAlpha!.Length); packedAlpha.CopyTo(file, p + 0x24);
        }
        return file;
    }

    private static byte[] LiteralLzss(byte[] source)
    {
        var output = new List<byte>();
        for (int p = 0; p < source.Length;)
        {
            int count = Math.Min(8, source.Length - p);
            output.Add((byte)((1 << count) - 1));
            for (int i = 0; i < count; i++) output.Add(source[p++]);
        }
        return output.ToArray();
    }

    private static RgbaImage ReadBmp32(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(10));
        int width = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18));
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(22));
        int bpp = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(28));
        Assert.True(bpp is 24 or 32);
        int height = Math.Abs(signedHeight);
        int stride = ((width * bpp / 8) + 3) & ~3;
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sy = signedHeight > 0 ? height - 1 - y : y;
            for (int x = 0; x < width; x++)
            {
                int s = offset + sy * stride + x * (bpp / 8), d = (y * width + x) * 4;
                rgba[d] = b[s + 2]; rgba[d + 1] = b[s + 1]; rgba[d + 2] = b[s];
                rgba[d + 3] = bpp == 32 ? b[s + 3] : (byte)255;
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    private static void Put32(byte[] b, int p, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p), value);
    private static void Put16(byte[] b, int p, int value) => BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(p), (short)value);
}

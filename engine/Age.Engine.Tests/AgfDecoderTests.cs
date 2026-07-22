using System.Buffers.Binary;
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

    private static byte[] Palette(params (byte R, byte G, byte B)[] colors) => Palette(16, colors);
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

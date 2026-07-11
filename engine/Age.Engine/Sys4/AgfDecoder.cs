using System.Buffers.Binary;

namespace Age.Engine.Sys4;

/// <summary>A decoded, tightly packed, top-down RGBA8 image.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// Platform-neutral Eushully AGF decoder. Format algorithm ported from GARbro's MIT-licensed
/// ArcFormats/Eushully/ImageAGF.cs (Copyright (C) 2015 morkt).
/// </summary>
public static class AgfDecoder
{
    private const int OuterHeaderSize = 0x18;

    public static RgbaImage Decode(IAssetStore store, AssetEntry entry)
        => Decode(store.ReadAll(entry), entry.Name);

    public static RgbaImage Decode(ReadOnlySpan<byte> file, string name = "AGF")
    {
        if (file.Length < OuterHeaderSize ||
            !(file[..4].SequenceEqual("ACGF"u8) || BinaryPrimitives.ReadUInt32LittleEndian(file) == 0))
            throw new InvalidDataException($"{name}: expected an ACGF image");

        int type = I32(file, 4, name);
        if (type is not (1 or 2)) throw new InvalidDataException($"{name}: unsupported AGF type {type}");
        int infoSize = PositiveSize(file, 0x0c, name, "information expanded size");
        int infoPacked = PositiveSize(file, 0x14, name, "information packed size");
        byte[] info = OpenSection(Slice(file, OuterHeaderSize, infoPacked, name, "information section"),
                                  infoSize, infoPacked, name, "information");
        if (info.Length < 0x20) throw new InvalidDataException($"{name}: information header is truncated");

        int width = PositiveSize(info, 0x14, name, "width");
        int height = PositiveSize(info, 0x18, name, "height");
        int sourceBpp = I16(info, 0x1e, name);
        if (sourceBpp is not (4 or 8 or 24 or 32))
            throw new InvalidDataException($"{name}: unsupported source depth {sourceBpp}");
        long pixelCount = (long)width * height;
        if (pixelCount > int.MaxValue / 4) throw new InvalidDataException($"{name}: dimensions are too large");

        ReadOnlySpan<byte> palette = default;
        if (sourceBpp <= 8)
            palette = Slice(info, 0x38, checked((1 << sourceBpp) * 4), name, "palette");

        int dataOffset = checked(OuterHeaderSize + infoPacked);
        ReadOnlySpan<byte> dataHeader = Slice(file, dataOffset, 12, name, "pixel section header");
        int dataSize = PositiveSize(dataHeader, 4, name, "pixel expanded size");
        int dataPacked = PositiveSize(dataHeader, 8, name, "pixel packed size");
        int dataPos = checked(dataOffset + 12);
        byte[] pixels = OpenSection(Slice(file, dataPos, dataPacked, name, "pixel section"),
                                    dataSize, dataPacked, name, "pixels");

        byte[]? alpha = null;
        if (type == 2)
        {
            int alphaOffset = checked(dataPos + dataPacked);
            if (alphaOffset <= file.Length - 0x24 && file.Slice(alphaOffset, 4).SequenceEqual("ACIF"u8))
            {
                ReadOnlySpan<byte> alphaHeader = file.Slice(alphaOffset, 0x24);
                int alphaSize = PositiveSize(alphaHeader, 0x1c, name, "alpha expanded size");
                int alphaPacked = PositiveSize(alphaHeader, 0x20, name, "alpha packed size");
                if (alphaSize != pixelCount) throw new InvalidDataException($"{name}: alpha dimensions do not match image");
                alpha = OpenSection(Slice(file, alphaOffset + 0x24, alphaPacked, name, "alpha section"),
                                    alphaSize, alphaPacked, name, "alpha");
            }
        }

        int sourceRowBytes = checked((checked(width * sourceBpp) + 7) / 8);
        int sourceStride = checked((sourceRowBytes + 3) & ~3);
        if ((long)sourceStride * height > pixels.Length)
            throw new InvalidDataException($"{name}: pixel section is shorter than its bitmap stride");
        var rgba = new byte[checked((int)pixelCount * 4)];
        for (int y = 0; y < height; y++)
        {
            int src = checked((height - 1 - y) * sourceStride);
            int dst = checked(y * width * 4);
            int alphaAt = y * width;
            for (int x = 0; x < width; x++, dst += 4)
            {
                if (sourceBpp == 4)
                {
                    int index = (pixels[src + (x >> 1)] >> ((x & 1) == 0 ? 4 : 0)) & 0x0f;
                    CopyPalette(palette, index, rgba, dst);
                }
                else if (sourceBpp == 8)
                    CopyPalette(palette, pixels[src + x], rgba, dst);
                else
                {
                    int at = src + x * (sourceBpp / 8);
                    rgba[dst] = pixels[at + 2];
                    rgba[dst + 1] = pixels[at + 1];
                    rgba[dst + 2] = pixels[at];
                }
                rgba[dst + 3] = alpha?[alphaAt + x] ?? (byte)255;
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    private static void CopyPalette(ReadOnlySpan<byte> palette, int index, byte[] rgba, int dst)
    {
        int p = index * 4;
        rgba[dst] = palette[p + 2]; rgba[dst + 1] = palette[p + 1]; rgba[dst + 2] = palette[p];
    }

    private static byte[] OpenSection(ReadOnlySpan<byte> source, int expanded, int packed,
                                      string name, string section)
        => expanded == packed ? source.ToArray() : LzssDecoder.Decode(source, expanded, $"{name}: {section}");

    private static int I32(ReadOnlySpan<byte> data, int offset, string name)
    {
        if (offset < 0 || offset > data.Length - 4) throw new InvalidDataException($"{name}: header is truncated");
        return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
    }
    private static int I16(ReadOnlySpan<byte> data, int offset, string name)
    {
        if (offset < 0 || offset > data.Length - 2) throw new InvalidDataException($"{name}: header is truncated");
        return BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
    }
    private static int PositiveSize(ReadOnlySpan<byte> data, int offset, string name, string field)
    {
        int value = I32(data, offset, name);
        if (value <= 0) throw new InvalidDataException($"{name}: invalid {field} {value}");
        return value;
    }
    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int count, string name, string field)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
            throw new InvalidDataException($"{name}: {field} is truncated");
        return data.Slice(offset, count);
    }
}

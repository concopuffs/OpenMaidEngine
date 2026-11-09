using System.Buffers.Binary;

namespace Ome.Engine.Frontend.NativeResources;

/// <summary>An icon image as straight RGBA rows, top row first; or, for a PNG-compressed image, the PNG file.</summary>
public sealed record IconImage(int Width, int Height, byte[] Rgba, byte[]? Png = null);

/// <summary>Reads an icon (RT_GROUP_ICON and its RT_ICON images) from a PE image, as LoadIcon picks one: the image
/// of the requested size with the most colours.</summary>
public static class IconResource
{
    /// <summary>Icon group <paramref name="name"/>'s <paramref name="size"/>×<paramref name="size"/> image (the
    /// closest size when there is none), or null when the image has no such icon.</summary>
    public static IconImage? Load(ReadOnlySpan<byte> pe, ResourceName name, int size = 32)
    {
        byte[]? group = PeResource.Read(pe, PeResource.RtGroupIcon, name);
        if (group == null || group.Length < 6) return null;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4));
        int bestId = -1, bestScore = int.MinValue;
        for (int i = 0; i < count && 6 + 14 * (i + 1) <= group.Length; i++)
        {
            ReadOnlySpan<byte> entry = group.AsSpan(6 + 14 * i, 14);
            int width = entry[0] == 0 ? 256 : entry[0];
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            int id = BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]);
            int score = -Math.Abs(width - size) * 1000 + bits;
            if (score > bestScore) (bestScore, bestId) = (score, id);
        }
        if (bestId < 0) return null;
        byte[]? image = PeResource.Read(pe, PeResource.RtIcon, bestId);
        if (image == null) return null;
        try
        {
            return Decode(image);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Decode one RT_ICON image: a PNG, or a DIB whose height counts the colour rows and the AND mask.</summary>
    public static IconImage? Decode(ReadOnlySpan<byte> image)
    {
        if (image.Length >= 8 && image[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            int pngWidth = BinaryPrimitives.ReadInt32BigEndian(image[16..]);
            int pngHeight = BinaryPrimitives.ReadInt32BigEndian(image[20..]);
            return new IconImage(pngWidth, pngHeight, [], image.ToArray());
        }
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(image);
        int width = BinaryPrimitives.ReadInt32LittleEndian(image[4..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(image[8..]) / 2;
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(image[14..]);
        int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(image[32..]);
        if (width <= 0 || height <= 0 || bitCount is not (1 or 4 or 8 or 24 or 32)) return null;
        int paletteSize = bitCount <= 8 ? (colorsUsed != 0 ? colorsUsed : 1 << bitCount) : 0;
        int palette = headerSize;
        int xor = palette + 4 * paletteSize;
        int xorStride = (width * bitCount + 31) / 32 * 4;
        int and = xor + xorStride * height;
        int andStride = (width + 31) / 32 * 4;
        bool hasAlpha = false;
        if (bitCount == 32)
            for (int y = 0; y < height && !hasAlpha; y++)
                for (int x = 0; x < width && !hasAlpha; x++)
                    hasAlpha = image[xor + y * xorStride + 4 * x + 3] != 0;

        var rgba = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            int source = height - 1 - row;      // bottom-up
            ReadOnlySpan<byte> line = image.Slice(xor + source * xorStride, xorStride);
            ReadOnlySpan<byte> mask = image.Slice(and + source * andStride, andStride);
            for (int x = 0; x < width; x++)
            {
                byte b, g, r, a;
                switch (bitCount)
                {
                    case 32:
                        (b, g, r, a) = (line[4 * x], line[4 * x + 1], line[4 * x + 2], line[4 * x + 3]);
                        break;
                    case 24:
                        (b, g, r, a) = (line[3 * x], line[3 * x + 1], line[3 * x + 2], (byte)255);
                        break;
                    default:
                        int perByte = 8 / bitCount;
                        int shift = 8 - bitCount * (x % perByte + 1);
                        int index = (line[x / perByte] >> shift) & ((1 << bitCount) - 1);
                        int entry = palette + 4 * index;
                        (b, g, r, a) = (image[entry], image[entry + 1], image[entry + 2], (byte)255);
                        break;
                }
                bool transparent = (mask[x / 8] & (0x80 >> (x % 8))) != 0;
                if (!hasAlpha) a = transparent ? (byte)0 : (byte)255;
                int o = 4 * (row * width + x);
                (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = (r, g, b, a);
            }
        }
        return new IconImage(width, height, rgba);
    }
}

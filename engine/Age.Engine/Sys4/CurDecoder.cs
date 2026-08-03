using System.Buffers.Binary;
using Age.Engine.Model;

namespace Age.Engine.Sys4;

/// <summary>A decoded Windows cursor image and its native hotspot.</summary>
public sealed record CursorImage(RgbaImage Image, int HotspotX, int HotspotY);

/// <summary>Decoder for Himegari's paletted Windows .CUR resources.</summary>
public static class CurDecoder
{
    public static CursorImage Decode(ReadOnlySpan<byte> file, string name = "CUR")
    {
        if (file.Length < 22 || U16(file, 0) != 0 || U16(file, 2) != 2 || U16(file, 4) < 1)
            throw new InvalidDataException($"{name}: expected a Windows cursor directory");

        int width = file[6] == 0 ? 256 : file[6];
        int height = file[7] == 0 ? 256 : file[7];
        int hotspotX = U16(file, 10);
        int hotspotY = U16(file, 12);
        int imageSize = I32(file, 14);
        int imageOffset = I32(file, 18);
        if (imageSize <= 0 || imageOffset < 22 || imageOffset > file.Length - imageSize)
            throw new InvalidDataException($"{name}: cursor image range is invalid");

        int headerSize = I32(file, imageOffset);
        if (headerSize < 40 || imageOffset > file.Length - headerSize)
            throw new InvalidDataException($"{name}: unsupported bitmap header");
        int dibWidth = I32(file, imageOffset + 4);
        int dibHeight = I32(file, imageOffset + 8);
        int planes = U16(file, imageOffset + 12);
        int bitsPerPixel = U16(file, imageOffset + 14);
        int compression = I32(file, imageOffset + 16);
        if (dibWidth != width || System.Math.Abs(dibHeight) != height * 2 || planes != 1
            || bitsPerPixel is not (1 or 4) || compression != 0)
            throw new InvalidDataException(
                $"{name}: expected an uncompressed 1-bit or 4-bit {width}x{height} cursor");

        int paletteOffset = checked(imageOffset + headerSize);
        int colorsUsed = I32(file, imageOffset + 32);
        int maximumPaletteEntries = 1 << bitsPerPixel;
        int paletteEntries = colorsUsed == 0 ? maximumPaletteEntries : colorsUsed;
        if (paletteEntries <= 0 || paletteEntries > maximumPaletteEntries)
            throw new InvalidDataException($"{name}: cursor palette size is invalid");
        int paletteBytes = checked(paletteEntries * 4);
        if (paletteOffset > file.Length - paletteBytes)
            throw new InvalidDataException($"{name}: palette is truncated");

        int xorStride = checked(((width * bitsPerPixel + 31) / 32) * 4);
        int andStride = checked(((width + 31) / 32) * 4);
        int xorBytes = checked(xorStride * height);
        int andBytes = checked(andStride * height);
        int xorOffset = checked(paletteOffset + paletteBytes);
        int andOffset = checked(xorOffset + xorBytes);
        if (andOffset > file.Length - andBytes)
            throw new InvalidDataException($"{name}: cursor masks are truncated");

        var rgba = new byte[checked(width * height * 4)];
        bool bottomUp = dibHeight > 0;
        for (int y = 0; y < height; y++)
        {
            int sourceY = bottomUp ? height - 1 - y : y;
            int xorRow = xorOffset + sourceY * xorStride;
            int andRow = andOffset + sourceY * andStride;
            for (int x = 0; x < width; x++)
            {
                int paletteIndex = bitsPerPixel == 1
                    ? (file[xorRow + (x >> 3)] >> (7 - (x & 7))) & 1
                    : (file[xorRow + (x >> 1)] >> ((1 - (x & 1)) * 4)) & 0xf;
                int maskShift = 7 - (x & 7);
                bool transparent = ((file[andRow + (x >> 3)] >> maskShift) & 1) != 0
                                   && paletteIndex == 0;
                int palette = paletteOffset + paletteIndex * 4;
                int dst = (y * width + x) * 4;
                rgba[dst] = file[palette + 2];
                rgba[dst + 1] = file[palette + 1];
                rgba[dst + 2] = file[palette];
                rgba[dst + 3] = transparent ? (byte)0 : (byte)255;
            }
        }

        return new CursorImage(new RgbaImage(width, height, rgba), hotspotX, hotspotY);
    }

    private static int U16(ReadOnlySpan<byte> data, int offset)
    {
        if ((uint)offset > (uint)(data.Length - 2)) throw new InvalidDataException("CUR: truncated field");
        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }

    private static int I32(ReadOnlySpan<byte> data, int offset)
    {
        if ((uint)offset > (uint)(data.Length - 4)) throw new InvalidDataException("CUR: truncated field");
        return BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    }
}

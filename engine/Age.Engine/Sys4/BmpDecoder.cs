using System.Buffers.Binary;
using Age.Engine.Model;

namespace Age.Engine.Sys4;

/// <summary>Strict decoder for the uncompressed Windows BMP payloads used by loose AGE overrides.</summary>
public static class BmpDecoder
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;

    public static RgbaImage Decode(ReadOnlySpan<byte> file, string name = "BMP")
    {
        if (file.Length < FileHeaderSize + InfoHeaderSize || !file[..2].SequenceEqual("BM"u8))
            throw new InvalidDataException($"{name}: expected a BMP image");

        int pixelOffset = PositiveI32(file, 10, name, "pixel offset");
        int headerSize = PositiveI32(file, 14, name, "DIB header size");
        if (headerSize < InfoHeaderSize || headerSize > file.Length - FileHeaderSize)
            throw new InvalidDataException($"{name}: unsupported or truncated DIB header ({headerSize} bytes)");

        int width = I32(file, 18, name);
        int storedHeight = I32(file, 22, name);
        if (width <= 0 || storedHeight == 0 || storedHeight == int.MinValue)
            throw new InvalidDataException($"{name}: invalid BMP dimensions {width}x{storedHeight}");
        int height = Math.Abs(storedHeight);
        if (U16(file, 26, name) != 1)
            throw new InvalidDataException($"{name}: BMP plane count must be one");
        int sourceBpp = U16(file, 28, name);
        if (sourceBpp is not (24 or 32))
            throw new InvalidDataException($"{name}: unsupported BMP depth {sourceBpp}");
        int compression = I32(file, 30, name);
        if (compression != 0)
            throw new InvalidDataException($"{name}: unsupported BMP compression {compression}");

        long rowBits = checked((long)width * sourceBpp);
        int sourceStride = checked((int)(((rowBits + 31) / 32) * 4));
        long sourceLength = checked((long)sourceStride * height);
        if (pixelOffset > file.Length || sourceLength > file.Length - pixelOffset)
            throw new InvalidDataException($"{name}: BMP pixel array is truncated");
        long pixelCount = checked((long)width * height);
        if (pixelCount > int.MaxValue / 4)
            throw new InvalidDataException($"{name}: BMP dimensions are too large");

        var rgba = new byte[checked((int)pixelCount * 4)];
        int bytesPerPixel = sourceBpp / 8;
        bool topDown = storedHeight < 0;
        for (int y = 0; y < height; y++)
        {
            int sourceY = topDown ? y : height - 1 - y;
            int source = checked(pixelOffset + sourceY * sourceStride);
            int destination = checked(y * width * 4);
            for (int x = 0; x < width; x++, source += bytesPerPixel, destination += 4)
            {
                rgba[destination] = file[source + 2];
                rgba[destination + 1] = file[source + 1];
                rgba[destination + 2] = file[source];
                // AGE's translation overlays use the BI_RGB fourth byte as straight alpha.
                rgba[destination + 3] = sourceBpp == 32 ? file[source + 3] : (byte)255;
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    private static int PositiveI32(
        ReadOnlySpan<byte> file, int offset, string name, string field)
    {
        int value = I32(file, offset, name);
        if (value <= 0)
            throw new InvalidDataException($"{name}: invalid {field} {value}");
        return value;
    }

    private static int I32(ReadOnlySpan<byte> file, int offset, string name)
    {
        if (offset < 0 || offset > file.Length - 4)
            throw new InvalidDataException($"{name}: BMP header is truncated");
        return BinaryPrimitives.ReadInt32LittleEndian(file[offset..]);
    }

    private static ushort U16(ReadOnlySpan<byte> file, int offset, string name)
    {
        if (offset < 0 || offset > file.Length - 2)
            throw new InvalidDataException($"{name}: BMP header is truncated");
        return BinaryPrimitives.ReadUInt16LittleEndian(file[offset..]);
    }
}

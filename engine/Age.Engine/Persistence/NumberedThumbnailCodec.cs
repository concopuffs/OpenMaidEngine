using System.Buffers.Binary;
using Age.Engine.Sys4;

namespace Age.Engine.Persistence;

/// <summary>
/// AGE numbered-save thumbnails are ordinary uncompressed bottom-up 24-bit BMPs under the .STH
/// extension. The native writer's bfSize omits the 14-byte BITMAPFILEHEADER even though the file
/// and pixel offset include it; this codec reproduces that harmless historical quirk.
/// </summary>
public static class NumberedThumbnailCodec
{
    public const int FileHeaderSize = 14;
    public const int DibHeaderSize = 40;
    public const int PixelOffset = FileHeaderSize + DibHeaderSize;

    public static byte[] Encode(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateRgba(image);
        if (image.Width <= 0 || image.Height <= 0)
            throw new InvalidDataException("Numbered thumbnail dimensions must be positive.");

        int rowBytes = checked(image.Width * 3);
        int rowStride = checked((rowBytes + 3) & ~3);
        int pixelBytes = checked(rowStride * image.Height);
        byte[] result = new byte[checked(PixelOffset + pixelBytes)];
        Span<byte> header = result.AsSpan(0, PixelOffset);
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(
            header[2..], checked((uint)(DibHeaderSize + pixelBytes)));
        BinaryPrimitives.WriteUInt32LittleEndian(header[10..], (uint)PixelOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(header[14..], (uint)DibHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], image.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 24);

        for (int destinationRow = 0; destinationRow < image.Height; destinationRow++)
        {
            int sourceY = image.Height - 1 - destinationRow;
            int source = sourceY * image.Width * 4;
            int destination = PixelOffset + destinationRow * rowStride;
            for (int x = 0; x < image.Width; x++, source += 4, destination += 3)
            {
                result[destination] = image.Pixels[source + 2];
                result[destination + 1] = image.Pixels[source + 1];
                result[destination + 2] = image.Pixels[source];
            }
        }
        return result;
    }

    public static RgbaImage Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < PixelOffset
            || source[0] != (byte)'B' || source[1] != (byte)'M')
            throw new InvalidDataException("Numbered thumbnail is not a BMP file.");
        uint rawOffset = BinaryPrimitives.ReadUInt32LittleEndian(source[10..]);
        uint dibSize = BinaryPrimitives.ReadUInt32LittleEndian(source[14..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(source[18..]);
        int storedHeight = BinaryPrimitives.ReadInt32LittleEndian(source[22..]);
        ushort planes = BinaryPrimitives.ReadUInt16LittleEndian(source[26..]);
        ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(source[28..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(source[30..]);
        if (dibSize < DibHeaderSize
            || (ulong)rawOffset < (ulong)FileHeaderSize + dibSize
            || rawOffset > int.MaxValue || width <= 0 || storedHeight == 0
            || storedHeight == int.MinValue || planes != 1 || bitsPerPixel != 24 || compression != 0)
            throw new InvalidDataException("Numbered thumbnail has an unsupported BMP layout.");

        bool bottomUp = storedHeight > 0;
        int height = Math.Abs(storedHeight);
        int rowBytes = checked(width * 3);
        int rowStride = checked((rowBytes + 3) & ~3);
        int pixelOffset = (int)rawOffset;
        int pixelBytes = checked(rowStride * height);
        if (pixelOffset > source.Length || pixelBytes > source.Length - pixelOffset)
            throw new InvalidDataException("Numbered thumbnail pixel data is truncated.");

        byte[] rgba = new byte[checked(width * height * 4)];
        for (int storedRow = 0; storedRow < height; storedRow++)
        {
            int destinationY = bottomUp ? height - 1 - storedRow : storedRow;
            int sourcePosition = pixelOffset + storedRow * rowStride;
            int destination = destinationY * width * 4;
            for (int x = 0; x < width; x++, sourcePosition += 3, destination += 4)
            {
                rgba[destination] = source[sourcePosition + 2];
                rgba[destination + 1] = source[sourcePosition + 1];
                rgba[destination + 2] = source[sourcePosition];
                rgba[destination + 3] = 255;
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    private static void ValidateRgba(RgbaImage image)
    {
        if (image.Width < 0 || image.Height < 0
            || image.Pixels.Length != checked(image.Width * image.Height * 4))
            throw new InvalidDataException("Numbered thumbnail RGBA buffer has invalid dimensions.");
    }
}

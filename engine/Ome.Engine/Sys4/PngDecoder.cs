using System.Buffers.Binary;
using System.IO.Compression;
using Ome.Engine.Model;

namespace Ome.Engine.Sys4;

/// <summary>
/// Strict PNG decoder for the images a language package supplies under an image name (localization spec §12): every
/// colour type and bit depth of PNG 1.2, Adam7 interlacing and <c>tRNS</c> transparency, to straight-alpha RGBA. Gamma,
/// colour profiles and the other ancillary chunks are ignored; an unknown critical chunk, a bad CRC or a short image
/// stream is an error. A package is untrusted input, so the dimensions and the inflated size are bounded.
/// </summary>
public static class PngDecoder
{
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    /// <summary>The largest width or height accepted.</summary>
    public const int MaxDimension = 16384;

    public static bool IsPng(ReadOnlySpan<byte> file) => file.Length >= 8 && file[..8].SequenceEqual(Signature);

    public static RgbaImage Decode(ReadOnlySpan<byte> file, string name = "PNG")
    {
        if (!IsPng(file)) throw new InvalidDataException($"{name}: expected a PNG image");
        int width = 0, height = 0, depth = 0, colour = -1, interlace = 0;
        byte[]? palette = null, transparency = null;
        using var idat = new MemoryStream();
        bool header = false, end = false;
        int at = 8;
        while (!end)
        {
            if (at > file.Length - 12) throw new InvalidDataException($"{name}: the image ends inside a chunk");
            uint length = BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
            if (length > int.MaxValue || length > (uint)(file.Length - at - 12))
                throw new InvalidDataException($"{name}: a chunk runs past the end of the file");
            ReadOnlySpan<byte> type = file.Slice(at + 4, 4);
            ReadOnlySpan<byte> data = file.Slice(at + 8, (int)length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(file[(at + 8 + (int)length)..]);
            if (Crc32.Of(file.Slice(at + 4, 4 + (int)length)) != crc)
                throw new InvalidDataException($"{name}: chunk {Text(type)} fails its CRC");
            at += 12 + (int)length;
            string chunk = Text(type);
            if (!header && chunk != "IHDR") throw new InvalidDataException($"{name}: IHDR must come first");
            switch (chunk)
            {
                case "IHDR":
                    if (header || data.Length != 13) throw new InvalidDataException($"{name}: bad IHDR");
                    header = true;
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    depth = data[8];
                    colour = data[9];
                    if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
                        throw new InvalidDataException($"{name}: {width}x{height} is outside 1..{MaxDimension}");
                    if (!DepthAllowed(colour, depth))
                        throw new InvalidDataException($"{name}: colour type {colour} with bit depth {depth} is not PNG");
                    if (data[10] != 0 || data[11] != 0) throw new InvalidDataException($"{name}: unknown compression or filter method");
                    interlace = data[12];
                    if (interlace > 1) throw new InvalidDataException($"{name}: unknown interlace method {interlace}");
                    break;
                case "PLTE":
                    if (data.Length == 0 || data.Length % 3 != 0 || data.Length > 256 * 3 || idat.Length > 0)
                        throw new InvalidDataException($"{name}: bad PLTE");
                    palette = data.ToArray();
                    break;
                case "tRNS":
                    if (idat.Length > 0) throw new InvalidDataException($"{name}: tRNS after the image data");
                    transparency = data.ToArray();
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    end = true;
                    break;
                default:
                    // Lowercase first letter: ancillary, safe to skip. Uppercase: critical, and unknown.
                    if ((type[0] & 0x20) == 0) throw new InvalidDataException($"{name}: unknown critical chunk {chunk}");
                    break;
            }
        }
        if (idat.Length == 0) throw new InvalidDataException($"{name}: no image data");
        if (colour == 3 && palette == null) throw new InvalidDataException($"{name}: a paletted image without PLTE");

        int channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        int bitsPerPixel = channels * depth;
        var passes = interlace == 0
            ? new[] { (X: 0, Y: 0, DX: 1, DY: 1) }
            : [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
        long expected = 0;
        foreach (var pass in passes)
        {
            long columns = Count(width, pass.X, pass.DX), rows = Count(height, pass.Y, pass.DY);
            if (columns > 0 && rows > 0) expected += rows * (1 + (columns * bitsPerPixel + 7) / 8);
        }
        if (expected > int.MaxValue) throw new InvalidDataException($"{name}: the image is too large");
        byte[] raw = Inflate(idat.ToArray(), (int)expected, name);

        var rgba = new byte[checked(width * height * 4)];
        int offset = 0;
        int bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
        foreach (var pass in passes)
        {
            int columns = (int)Count(width, pass.X, pass.DX), rows = (int)Count(height, pass.Y, pass.DY);
            if (columns == 0 || rows == 0) continue;
            int stride = (columns * bitsPerPixel + 7) / 8;
            var previous = new byte[stride];
            var current = new byte[stride];
            for (int row = 0; row < rows; row++)
            {
                byte filter = raw[offset];
                Array.Copy(raw, offset + 1, current, 0, stride);
                offset += 1 + stride;
                Unfilter(filter, current, previous, bytesPerPixel, name);
                int y = pass.Y + row * pass.DY;
                for (int column = 0; column < columns; column++)
                    Pixel(current, column, depth, colour, palette, transparency,
                          rgba, (y * width + pass.X + column * pass.DX) * 4, name);
                (previous, current) = (current, previous);
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    private static bool DepthAllowed(int colour, int depth) => colour switch
    {
        0 => depth is 1 or 2 or 4 or 8 or 16,
        3 => depth is 1 or 2 or 4 or 8,
        2 or 4 or 6 => depth is 8 or 16,
        _ => false,
    };

    private static long Count(int size, int start, int step) => size <= start ? 0 : (size - start + step - 1) / step;

    private static byte[] Inflate(byte[] compressed, int expected, string name)
    {
        var output = new byte[expected];
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            int read = 0;
            while (read < expected)
            {
                int count = zlib.Read(output, read, expected - read);
                if (count == 0) throw new InvalidDataException($"{name}: the image data is {read} bytes, not {expected}");
                read += count;
            }
        }
        catch (InvalidDataException error) when (!error.Message.StartsWith(name, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name}: the image data does not inflate: {error.Message}");
        }
        return output;
    }

    private static void Unfilter(byte filter, byte[] line, byte[] previous, int bpp, string name)
    {
        switch (filter)
        {
            case 0: return;
            case 1:
                for (int i = bpp; i < line.Length; i++) line[i] += line[i - bpp];
                return;
            case 2:
                for (int i = 0; i < line.Length; i++) line[i] += previous[i];
                return;
            case 3:
                for (int i = 0; i < line.Length; i++)
                    line[i] += (byte)(((i >= bpp ? line[i - bpp] : 0) + previous[i]) >> 1);
                return;
            case 4:
                for (int i = 0; i < line.Length; i++)
                {
                    int a = i >= bpp ? line[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    line[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                }
                return;
            default:
                throw new InvalidDataException($"{name}: unknown row filter {filter}");
        }
    }

    /// <summary>Sample <paramref name="index"/> of a row of <paramref name="depth"/>-bit samples, most significant
    /// bits first, as its raw value (16-bit samples whole).</summary>
    private static int Sample(byte[] line, int index, int depth) => depth switch
    {
        8 => line[index],
        16 => (line[index * 2] << 8) | line[index * 2 + 1],
        _ => (line[index * depth / 8] >> (8 - depth - index * depth % 8)) & ((1 << depth) - 1),
    };

    private static byte Scale(int value, int depth) => depth switch
    {
        16 => (byte)(value >> 8),
        8 => (byte)value,
        _ => (byte)(value * 255 / ((1 << depth) - 1)),
    };

    private static void Pixel(byte[] line, int x, int depth, int colour, byte[]? palette, byte[]? transparency,
                              byte[] rgba, int at, string name)
    {
        switch (colour)
        {
            case 0:
            {
                int value = Sample(line, x, depth);
                byte gray = Scale(value, depth);
                rgba[at] = rgba[at + 1] = rgba[at + 2] = gray;
                bool clear = transparency is { Length: >= 2 } && value == ((transparency[0] << 8) | transparency[1]);
                rgba[at + 3] = clear ? (byte)0 : (byte)255;
                return;
            }
            case 2:
            {
                int r = Sample(line, x * 3, depth), g = Sample(line, x * 3 + 1, depth), b = Sample(line, x * 3 + 2, depth);
                rgba[at] = Scale(r, depth); rgba[at + 1] = Scale(g, depth); rgba[at + 2] = Scale(b, depth);
                bool clear = transparency is { Length: >= 6 }
                    && r == ((transparency[0] << 8) | transparency[1])
                    && g == ((transparency[2] << 8) | transparency[3])
                    && b == ((transparency[4] << 8) | transparency[5]);
                rgba[at + 3] = clear ? (byte)0 : (byte)255;
                return;
            }
            case 3:
            {
                int index = Sample(line, x, depth);
                if (index * 3 + 2 >= palette!.Length)
                    throw new InvalidDataException($"{name}: palette index {index} is beyond the palette's {palette.Length / 3} entries");
                rgba[at] = palette[index * 3]; rgba[at + 1] = palette[index * 3 + 1]; rgba[at + 2] = palette[index * 3 + 2];
                rgba[at + 3] = transparency != null && index < transparency.Length ? transparency[index] : (byte)255;
                return;
            }
            case 4:
            {
                byte gray = Scale(Sample(line, x * 2, depth), depth);
                rgba[at] = rgba[at + 1] = rgba[at + 2] = gray;
                rgba[at + 3] = Scale(Sample(line, x * 2 + 1, depth), depth);
                return;
            }
            default:
                for (int channel = 0; channel < 4; channel++)
                    rgba[at + channel] = Scale(Sample(line, x * 4 + channel, depth), depth);
                return;
        }
    }

    private static string Text(ReadOnlySpan<byte> type)
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++) chars[i] = type[i] is >= 0x20 and < 0x7f ? (char)type[i] : '?';
        return new string(chars);
    }
}

/// <summary>Writes RGBA images as 8-bit RGBA PNGs, each row unfiltered (tools and tests; the engine draws nothing to
/// PNG).</summary>
public static class PngWriter
{
    public static byte[] Write(RgbaImage image)
    {
        if (image.Pixels.Length != checked(image.Width * image.Height * 4))
            throw new ArgumentException("pixel buffer size does not match the dimensions", nameof(image));
        using var output = new MemoryStream();
        output.Write(PngDecoder.Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8;   // bit depth
        header[9] = 6;   // RGBA
        Chunk(output, "IHDR", header);
        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
                for (int y = 0; y < image.Height; y++)
                {
                    zlib.WriteByte(0);
                    zlib.Write(image.Pixels, y * image.Width * 4, image.Width * 4);
                }
            Chunk(output, "IDAT", raw.ToArray());
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body.AsSpan(4));
        output.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32.Of(body));
        output.Write(word);
    }
}

/// <summary>The CRC-32 of PNG chunks (ISO 3309, polynomial 0xedb88320).</summary>
internal static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Of(ReadOnlySpan<byte> data)
    {
        uint c = 0xffffffffu;
        foreach (byte b in data) c = Table[(c ^ b) & 0xff] ^ (c >> 8);
        return c ^ 0xffffffffu;
    }
}

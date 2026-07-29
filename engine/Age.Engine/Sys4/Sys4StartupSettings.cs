using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Age.Engine.Sys4;

/// <summary>
/// Ordered per-game key/value pairs stored after the base SYS4INI directory and VM metadata.
/// Unknown keys are retained so newer AGE profiles remain inspectable before they have consumers.
/// </summary>
public sealed class Sys4StartupSettings
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _pairs;
    private readonly IReadOnlyDictionary<string, string> _values;

    public static Sys4StartupSettings Empty { get; } = new([]);

    public IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;
    public int Count => _pairs.Count;

    private Sys4StartupSettings(List<KeyValuePair<string, string>> pairs)
    {
        _pairs = new ReadOnlyCollection<KeyValuePair<string, string>>(pairs);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
            values[pair.Key] = pair.Value;
        _values = new ReadOnlyDictionary<string, string>(values);
    }

    public bool TryGetValue(string key, out string value)
        => _values.TryGetValue(key, out value!);

    public string? GetValueOrDefault(string key)
        => _values.GetValueOrDefault(key);

    internal static Sys4StartupSettings ParseTrailer(byte[] blob, ref int offset, string source)
    {
        if (offset == blob.Length) return Empty;

        uint vmMetadataBytes = ReadU32(blob, ref offset, source, "VM metadata length");
        if (vmMetadataBytes > blob.Length - offset)
            throw new InvalidDataException(
                $"{source}: VM metadata length {vmMetadataBytes} exceeds {blob.Length - offset} remaining bytes");
        offset += checked((int)vmMetadataBytes);
        if (offset == blob.Length) return Empty;

        uint stringBytes = ReadU32(blob, ref offset, source, "settings string length");
        if (stringBytes == 0)
        {
            if (offset != blob.Length)
                throw new InvalidDataException($"{source}: zero-length settings block has trailing data");
            return Empty;
        }
        if (offset > blob.Length - 4)
            throw new InvalidDataException($"{source}: settings pair count is truncated");
        if (stringBytes > blob.Length - offset - 4)
            throw new InvalidDataException(
                $"{source}: settings string length {stringBytes} exceeds the remaining block");

        uint pairCount = ReadU32(blob, ref offset, source, "settings pair count");
        if (pairCount > stringBytes / 2)
            throw new InvalidDataException(
                $"{source}: settings pair count {pairCount} cannot fit in {stringBytes} string bytes");

        int end = checked(offset + (int)stringBytes);
        var pairs = new List<KeyValuePair<string, string>>(checked((int)pairCount));
        for (int i = 0; i < pairCount; i++)
        {
            string key = ReadCString(blob, ref offset, end, source, $"settings key {i}");
            string value = ReadCString(blob, ref offset, end, source, $"settings value {i}");
            pairs.Add(new KeyValuePair<string, string>(key, value));
        }
        if (offset != end)
            throw new InvalidDataException(
                $"{source}: settings pair count consumed {offset} bytes through the blob, expected {end}");
        if (end != blob.Length)
            throw new InvalidDataException($"{source}: settings block has {blob.Length - end} trailing bytes");
        return new Sys4StartupSettings(pairs);
    }

    private static uint ReadU32(byte[] blob, ref int offset, string source, string field)
    {
        if (offset > blob.Length - 4)
            throw new InvalidDataException($"{source}: {field} is truncated");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    private static string ReadCString(
        byte[] blob, ref int offset, int end, string source, string field)
    {
        if (offset >= end)
            throw new InvalidDataException($"{source}: {field} is missing");
        int zero = blob.AsSpan(offset, end - offset).IndexOf((byte)0);
        if (zero < 0)
            throw new InvalidDataException($"{source}: {field} is not NUL-terminated");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string value = Encoding.GetEncoding(932).GetString(blob, offset, zero);
        offset += zero + 1;
        return value;
    }
}

/// <summary>The validated per-game logical canvas selected before presentation allocation.</summary>
public readonly record struct Sys4LogicalCanvas(int Width, int Height)
{
    public const int DefaultWidth = 640;
    public const int DefaultHeight = 480;
    public const int MaximumDimension = 16_384;
    public const int MaximumPixels = 67_108_864;

    public int PixelCount => checked(Width * Height);
    public int RgbaByteCount => checked(PixelCount * 4);

    public static Sys4LogicalCanvas FromSettings(Sys4StartupSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int width = ReadDimension(settings, "SCREENX", DefaultWidth);
        int height = ReadDimension(settings, "SCREENY", DefaultHeight);
        if (width > MaximumDimension || height > MaximumDimension)
            throw new InvalidDataException(
                $"SYS4 logical canvas {width}x{height} exceeds maximum dimension {MaximumDimension}");
        long pixels = (long)width * height;
        if (pixels > MaximumPixels)
            throw new InvalidDataException(
                $"SYS4 logical canvas {width}x{height} exceeds maximum pixel count {MaximumPixels}");
        return new Sys4LogicalCanvas(width, height);
    }

    private static int ReadDimension(
        Sys4StartupSettings settings, string key, int fallback)
    {
        string? raw = settings.GetValueOrDefault(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
               && value > 0
            ? value
            : fallback;
    }
}

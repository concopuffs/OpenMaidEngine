using System.Buffers.Binary;
using System.Collections;
using System.IO.Compression;

namespace Ome.Mods.Localization;

/// <summary>
/// The characters a font file maps, read from its <c>cmap</c> (localization spec §13: <c>omp text check</c>'s glyph
/// coverage for bundled fonts). Reads TrueType and OpenType files (<c>.ttf</c>, <c>.otf</c>), collections
/// (<c>.ttc</c>, every face), WOFF and WOFF2. Every Unicode subtable counts (platform 0, and platform 3 encodings 1 and
/// 10) in formats 0, 4, 6, 12 and 13. Font files come from untrusted packages, so every offset is bounds-checked and a
/// compressed table may not expand past <see cref="MaxTableBytes"/>.
/// </summary>
public sealed class FontCoverage
{
    public const int MaxTableBytes = 64 << 20;
    private const int MaxCodePoint = 0x10FFFF;

    private readonly BitArray _mapped = new(MaxCodePoint + 1);
    // Code points a font's subtables may walk: a few times the whole range, against overlapping hostile ranges.
    private long _budget = 4L * (MaxCodePoint + 1);

    private FontCoverage() { }

    /// <summary>The number of characters mapped.</summary>
    public int Count { get; private set; }

    public bool Contains(int codePoint) => codePoint is >= 0 and <= MaxCodePoint && _mapped[codePoint];

    /// <exception cref="InvalidDataException">The file is not a font this reader knows, or is malformed.</exception>
    public static FontCoverage Read(byte[] data)
    {
        var coverage = new FontCoverage();
        foreach (byte[] cmap in CmapTables(data)) coverage.AddCmap(cmap);
        return coverage;
    }

    // ---- containers --------------------------------------------------------------------------------------------

    private static IEnumerable<byte[]> CmapTables(byte[] data)
    {
        uint tag = U32(data, 0);
        return tag switch
        {
            0x74746366 => Collection(data),                 // 'ttcf'
            0x774F4646 => [Woff(data)],                      // 'wOFF'
            0x774F4632 => Woff2(data),                       // 'wOF2'
            0x00010000 or 0x4F54544F or 0x74727565 => [Sfnt(data, 0)],   // TrueType, 'OTTO', 'true'
            _ => throw new InvalidDataException("not a TrueType, OpenType, WOFF or WOFF2 font"),
        };
    }

    private static IEnumerable<byte[]> Collection(byte[] data)
    {
        uint fonts = U32(data, 8);
        if (fonts == 0 || fonts > 4096) throw new InvalidDataException("a font collection with no or too many faces");
        var tables = new List<byte[]>();
        for (int k = 0; k < fonts; k++) tables.Add(Sfnt(data, checked((int)U32(data, 12 + 4 * k))));
        return tables;
    }

    /// <summary>The <c>cmap</c> of the sfnt table directory at <paramref name="start"/>.</summary>
    private static byte[] Sfnt(byte[] data, int start)
    {
        int count = U16(data, start + 4);
        for (int k = 0; k < count; k++)
        {
            int record = start + 12 + 16 * k;
            if (U32(data, record) != Cmap) continue;
            return Slice(data, checked((int)U32(data, record + 8)), checked((int)U32(data, record + 12)));
        }
        throw new InvalidDataException("the font has no cmap table");
    }

    private const uint Cmap = 0x636D6170;

    private static byte[] Woff(byte[] data)
    {
        int count = U16(data, 12);
        for (int k = 0; k < count; k++)
        {
            int entry = 44 + 20 * k;
            if (U32(data, entry) != Cmap) continue;
            int offset = checked((int)U32(data, entry + 4));
            int compressed = checked((int)U32(data, entry + 8));
            int original = checked((int)U32(data, entry + 12));
            if (original > MaxTableBytes) throw new InvalidDataException("the cmap table is too large");
            byte[] stored = Slice(data, offset, compressed);
            if (compressed >= original) return stored;
            using var input = new ZLibStream(new MemoryStream(stored), CompressionMode.Decompress);
            return ReadBounded(input, original);
        }
        throw new InvalidDataException("the font has no cmap table");
    }

    private static IEnumerable<byte[]> Woff2(byte[] data)
    {
        uint flavor = U32(data, 4);
        int count = U16(data, 12);
        int compressedLength = checked((int)U32(data, 20));
        int position = 48;
        var tables = new List<(uint Tag, int Length)>();
        for (int k = 0; k < count; k++)
        {
            byte flags = Byte(data, position++);
            int index = flags & 0x3f, transform = flags >> 6;
            uint tag = index == 63 ? U32(data, position) : index == 0 ? Cmap : (uint)index;
            if (index == 63) position += 4;
            uint original = Base128(data, ref position);
            // glyf and loca (indices 10 and 11) are transformed at version 0; any other table at a non-zero version.
            bool transformed = index is 10 or 11 ? transform == 0 : transform != 0;
            uint length = transformed ? Base128(data, ref position) : original;
            if (length > MaxTableBytes) throw new InvalidDataException("a WOFF2 table is too large");
            tables.Add((tag, (int)length));
        }
        if (flavor == 0x74746366)   // 'ttcf': the collection directory follows the table directory
        {
            position += 4;
            int fonts = Ushort255(data, ref position);
            for (int f = 0; f < fonts; f++)
            {
                int tablesInFont = Ushort255(data, ref position);
                position += 4;
                for (int t = 0; t < tablesInFont; t++) Ushort255(data, ref position);
            }
        }
        long total = tables.Sum(table => (long)table.Length);
        if (total > MaxTableBytes * 4L) throw new InvalidDataException("the WOFF2 font is too large");
        byte[] stream;
        using (var input = new BrotliStream(new MemoryStream(Slice(data, position, compressedLength)), CompressionMode.Decompress))
            stream = ReadBounded(input, (int)total);
        var cmaps = new List<byte[]>();
        int at = 0;
        foreach (var (tag, length) in tables)
        {
            if (tag == Cmap) cmaps.Add(Slice(stream, at, length));
            at += length;
        }
        if (cmaps.Count == 0) throw new InvalidDataException("the font has no cmap table");
        return cmaps;
    }

    private static uint Base128(byte[] data, ref int position)
    {
        uint value = 0;
        for (int k = 0; k < 5; k++)
        {
            byte b = Byte(data, position++);
            if (k == 0 && b == 0x80) throw new InvalidDataException("a WOFF2 number has a leading zero");
            if ((value & 0xFE000000) != 0) throw new InvalidDataException("a WOFF2 number overflows");
            value = (value << 7) | (uint)(b & 0x7f);
            if ((b & 0x80) == 0) return value;
        }
        throw new InvalidDataException("a WOFF2 number is longer than five bytes");
    }

    private static int Ushort255(byte[] data, ref int position)
    {
        byte code = Byte(data, position++);
        switch (code)
        {
            case 253:
                int word = U16(data, position);
                position += 2;
                return word;
            case 254: return Byte(data, position++) + 253 * 2;
            case 255: return Byte(data, position++) + 253;
            default: return code;
        }
    }

    private static byte[] ReadBounded(Stream input, int expected)
    {
        var output = new MemoryStream(expected);
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            if (output.Length + read > expected) throw new InvalidDataException("a font table expands past its length");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    // ---- cmap --------------------------------------------------------------------------------------------------

    private void AddCmap(byte[] cmap)
    {
        int subtables = U16(cmap, 2);
        var seen = new HashSet<int>();
        for (int k = 0; k < subtables; k++)
        {
            int record = 4 + 8 * k;
            int platform = U16(cmap, record), encoding = U16(cmap, record + 2);
            int offset = checked((int)U32(cmap, record + 4));
            bool unicode = platform == 0 || (platform == 3 && encoding is 1 or 10);
            if (!unicode || !seen.Add(offset)) continue;
            AddSubtable(cmap, offset);
        }
    }

    private void AddSubtable(byte[] cmap, int at)
    {
        switch (U16(cmap, at))
        {
            case 0:
                for (int c = 0; c < 256; c++)
                    if (Byte(cmap, at + 6 + c) != 0) Add(c);
                break;
            case 4:
                Format4(cmap, at);
                break;
            case 6:
            {
                int first = U16(cmap, at + 6), count = U16(cmap, at + 8);
                for (int k = 0; k < count; k++)
                    if (U16(cmap, at + 10 + 2 * k) != 0) Add(first + k);
                break;
            }
            case 12:
            case 13:
            {
                bool manyToOne = U16(cmap, at) == 13;
                uint groups = U32(cmap, at + 12);
                if (groups > (cmap.Length - at) / 12) throw new InvalidDataException("a cmap subtable overruns its table");
                for (int g = 0; g < groups; g++)
                {
                    int group = at + 16 + 12 * g;
                    uint start = U32(cmap, group), end = Math.Min(U32(cmap, group + 4), MaxCodePoint);
                    uint glyph = U32(cmap, group + 8);
                    for (uint c = start; c <= end; c++)
                        if (manyToOne ? glyph != 0 : glyph + (c - start) != 0) Add((int)c);
                        else Spend();
                }
                break;
            }
        }
    }

    private void Format4(byte[] cmap, int at)
    {
        int segments = U16(cmap, at + 6) / 2;
        int ends = at + 14, starts = ends + 2 * segments + 2, deltas = starts + 2 * segments, ranges = deltas + 2 * segments;
        for (int s = 0; s < segments; s++)
        {
            int end = U16(cmap, ends + 2 * s), start = U16(cmap, starts + 2 * s);
            int delta = (short)U16(cmap, deltas + 2 * s);
            int rangeAt = ranges + 2 * s;
            int range = U16(cmap, rangeAt);
            for (int c = start; c <= end && c != 0xFFFF; c++)
            {
                int glyph;
                if (range == 0) glyph = (c + delta) & 0xFFFF;
                else
                {
                    int index = rangeAt + range + 2 * (c - start);
                    if (index + 2 > cmap.Length) break;
                    glyph = U16(cmap, index);
                    if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                }
                if (glyph != 0) Add(c);
                else Spend();
            }
        }
    }

    private void Add(int codePoint)
    {
        if (--_budget < 0) throw new InvalidDataException("the cmap maps more ranges than a font can");
        if (_mapped[codePoint]) return;
        _mapped[codePoint] = true;
        Count++;
    }

    private void Spend()
    {
        if (--_budget < 0) throw new InvalidDataException("the cmap maps more ranges than a font can");
    }

    // ---- bytes -------------------------------------------------------------------------------------------------

    private static byte[] Slice(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException("a font table lies outside the file");
        return data.AsSpan(offset, length).ToArray();
    }

    private static byte Byte(byte[] data, int at)
        => at >= 0 && at < data.Length ? data[at] : throw new InvalidDataException("the font file is truncated");

    private static int U16(byte[] data, int at)
        => at >= 0 && at <= data.Length - 2
            ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at))
            : throw new InvalidDataException("the font file is truncated");

    private static uint U32(byte[] data, int at)
        => at >= 0 && at <= data.Length - 4
            ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at))
            : throw new InvalidDataException("the font file is truncated");
}

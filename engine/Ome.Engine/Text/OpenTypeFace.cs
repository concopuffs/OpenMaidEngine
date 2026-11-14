using System.Buffers.Binary;
using System.Text;

namespace Ome.Engine.Text;

/// <summary>A Windows-platform record of a face's <c>name</c> table.</summary>
public readonly record struct OpenTypeName(ushort NameId, ushort LanguageId, string Value);

/// <summary>A named instance of a variable face (<c>fvar</c>): the name id of its subfamily and its axis coordinates.
/// </summary>
public sealed record OpenTypeNamedInstance(ushort SubfamilyNameId, IReadOnlyDictionary<string, float> Coordinates);

/// <summary>A glyph of an embedded bitmap strike (<c>EBLC</c>/<c>EBDT</c>) in pixels: the bitmap's box and bearings
/// from the strike's metrics, its advance, and the box its set pixels cover inside the bitmap.</summary>
public readonly record struct EmbeddedBitmapGlyph(
    int Width, int Height, int BearingX, int BearingY, int Advance,
    int InkLeft, int InkTop, int InkWidth, int InkHeight);

/// <summary>
/// One face of a TrueType or OpenType font file (<c>.ttf</c>, <c>.otf</c>, or one member of a <c>.ttc</c>/<c>.otc</c>
/// collection), read for what GDI derives from it: the names, weight, pitch and code pages GDI's font list reports,
/// the metrics <c>GetTextMetrics</c> rounds, the embedded bitmaps <c>GetGlyphOutline</c> measures, and whether a
/// character has a glyph with ink (<see cref="GdiFontRules"/>). The small tables are read when the face is; the
/// character map, bitmaps and outlines when first asked for, from the same file.
/// </summary>
public sealed class OpenTypeFace
{
    private const uint CollectionTag = 0x74746366;   // 'ttcf'
    private const ushort WindowsPlatform = 3;

    private readonly FontBytes _bytes;
    private readonly Dictionary<uint, (uint Offset, uint Length)> _tables;
    private readonly object _gate = new();
    private CharacterMap? _characterMap;
    private BitmapStrikes? _strikes;
    private Dictionary<int, (short YMax, short YMin)>? _verticalDeviceMetrics;
    private OutlineInk? _outlineInk;

    private OpenTypeFace(FontBytes bytes, string source, int index, Dictionary<uint, (uint, uint)> tables)
    {
        _bytes = bytes;
        Source = source;
        Index = index;
        _tables = tables;
    }

    /// <summary>The file the face was read from, or the name given to its bytes.</summary>
    public string Source { get; }

    /// <summary>The face's index in its collection (0 for a single-face file).</summary>
    public int Index { get; }

    public int UnitsPerEm { get; private set; }

    /// <summary><c>head.macStyle</c>.</summary>
    public ushort MacStyle { get; private set; }

    public short HorizontalAscender { get; private set; }

    public short HorizontalDescender { get; private set; }

    /// <summary>Whether the face has an <c>OS/2</c> table; without one the fields read from it are zero.</summary>
    public bool HasOs2 { get; private set; }

    /// <summary><c>OS/2.version</c>: version 0 carries no code page ranges.</summary>
    public int Os2Version { get; private set; }

    /// <summary><c>OS/2.xAvgCharWidth</c> in font units.</summary>
    public short AverageCharWidth { get; private set; }

    /// <summary><c>OS/2.usWeightClass</c>, 400 without the table.</summary>
    public int WeightClass { get; private set; } = 400;

    /// <summary><c>OS/2.fsSelection</c>.</summary>
    public ushort Selection { get; private set; }

    /// <summary>The ten PANOSE bytes of <c>OS/2</c> (zeros without the table).</summary>
    public IReadOnlyList<byte> Panose { get; private set; } = new byte[10];

    public ushort WindowsAscent { get; private set; }

    public ushort WindowsDescent { get; private set; }

    /// <summary><c>OS/2.ulCodePageRange1</c> (version 1 and later).</summary>
    public uint CodePageRange1 { get; private set; }

    /// <summary><c>OS/2.ulCodePageRange2</c> (version 1 and later).</summary>
    public uint CodePageRange2 { get; private set; }

    /// <summary><c>OS/2.usDefaultChar</c> (version 2 and later), 0 otherwise.</summary>
    public ushort DefaultChar { get; private set; }

    /// <summary><c>post.isFixedPitch</c> is nonzero.</summary>
    public bool PostFixedPitch { get; private set; }

    /// <summary>Whether the face's outlines are CFF (a <c>CFF </c> table and no <c>glyf</c>): GDI derives a CFF face's
    /// pitch and family by other rules than a TrueType face's (<see cref="GdiFontRules.PitchAndFamily"/>).</summary>
    public bool HasCffOutlines { get; private set; }

    public int GlyphCount { get; private set; }

    /// <summary>The Windows-platform names that GDI's font list reads: family and subfamily (1, 2), full name (4),
    /// typographic family and subfamily (16, 17), and the names of the named instances.</summary>
    public IReadOnlyList<OpenTypeName> Names { get; private set; } = [];

    /// <summary>A variable face's named instances (<c>fvar</c>), in table order; empty for a static face.</summary>
    public IReadOnlyList<OpenTypeNamedInstance> NamedInstances { get; private set; } = [];

    /// <summary>Whether the face has an <c>fvar</c> table.</summary>
    public bool IsVariable { get; private set; }

    /// <summary>Whether <c>fsSelection</c> or <c>macStyle</c> marks the face italic.</summary>
    public bool IsItalic => (Selection & 1) != 0 || (MacStyle & 2) != 0;

    /// <summary>The faces of a font file, in collection order. Throws <see cref="InvalidDataException"/> when the file
    /// is not a TrueType or OpenType font.</summary>
    public static IReadOnlyList<OpenTypeFace> ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
        return ReadFaces(new StreamBytes(stream), new PathBytes(path), path);
    }

    /// <summary>The faces of a font held in memory (a bundled file's bytes), named <paramref name="source"/>.</summary>
    public static IReadOnlyList<OpenTypeFace> ReadData(byte[] data, string source)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = new ArrayBytes(data);
        return ReadFaces(bytes, bytes, source);
    }

    /// <summary>The names with <paramref name="nameId"/>, in table order.</summary>
    public IEnumerable<OpenTypeName> NamesWithId(ushort nameId) => Names.Where(name => name.NameId == nameId);

    /// <summary>The name with <paramref name="nameId"/> in the first of <paramref name="languages"/> that has one, or
    /// the first such name in any language; null when the face has none.</summary>
    public string? Name(ushort nameId, params ushort[] languages)
    {
        foreach (ushort language in languages)
            foreach (OpenTypeName name in Names)
                if (name.NameId == nameId && name.LanguageId == language) return name.Value;
        foreach (OpenTypeName name in Names)
            if (name.NameId == nameId) return name.Value;
        return null;
    }

    /// <summary>The glyph the character map gives <paramref name="scalar"/>, or 0 (<c>.notdef</c>).</summary>
    public int GlyphIndex(int scalar)
    {
        CharacterMap map;
        lock (_gate)
            map = _characterMap ??= CharacterMap.Read(this);
        return map.Lookup(scalar);
    }

    /// <summary>The <c>VDMX</c> record for a 1:1 device at <paramref name="pixelsPerEm"/>, or null.</summary>
    public (int YMax, int YMin)? VerticalDeviceMetrics(int pixelsPerEm)
    {
        Dictionary<int, (short YMax, short YMin)> records;
        lock (_gate)
            records = _verticalDeviceMetrics ??= ReadVerticalDeviceMetrics();
        return records.TryGetValue(pixelsPerEm, out var record) ? (record.YMax, record.YMin) : null;
    }

    /// <summary>Whether a bitmap strike exists at <paramref name="pixelsPerEm"/> (x and y alike).</summary>
    public bool HasBitmapStrike(int pixelsPerEm) => Strikes().Has(pixelsPerEm);

    /// <summary><paramref name="glyph"/> in the bitmap strike at <paramref name="pixelsPerEm"/>, or null when there is
    /// no such strike or the strike lacks the glyph.</summary>
    public EmbeddedBitmapGlyph? BitmapGlyph(int pixelsPerEm, int glyph) => Strikes().Glyph(pixelsPerEm, glyph);

    /// <summary>Whether <paramref name="glyph"/> has an outline whose box is at least <paramref name="minimumUnits"/>
    /// font units wide or tall (<c>glyf</c>); for a CFF face, whether its charstring draws.</summary>
    public bool HasInk(int glyph, int minimumUnits)
    {
        OutlineInk ink;
        lock (_gate)
            ink = _outlineInk ??= OutlineInk.Read(this);
        return ink.HasInk(glyph, minimumUnits);
    }

    private BitmapStrikes Strikes()
    {
        lock (_gate)
            return _strikes ??= BitmapStrikes.Read(this);
    }

    private static IReadOnlyList<OpenTypeFace> ReadFaces(FontBytes eager, FontBytes lazy, string source)
    {
        byte[] header = eager.Read(0, 12);
        uint tag = U32(header, 0);
        var faces = new List<OpenTypeFace>();
        if (tag == CollectionTag)
        {
            int count = checked((int)U32(header, 8));
            if (count is <= 0 or > 4096) throw new InvalidDataException($"{source}: {count} faces in a collection.");
            byte[] offsets = eager.Read(12, checked(count * 4));
            for (int index = 0; index < count; index++)
                faces.Add(ReadFace(eager, lazy, source, index, U32(offsets, index * 4)));
        }
        else
            faces.Add(ReadFace(eager, lazy, source, 0, 0));
        return faces;
    }

    private static OpenTypeFace ReadFace(FontBytes eager, FontBytes lazy, string source, int index, uint offset)
    {
        byte[] directory = eager.Read(offset, 12);
        uint version = U32(directory, 0);
        if (version is not (0x00010000 or 0x4F54544F or 0x74727565))   // 1.0, 'OTTO', 'true'
            throw new InvalidDataException($"{source}: face {index} is not a TrueType or OpenType face.");
        int tableCount = U16(directory, 4);
        byte[] records = eager.Read(offset + 12, tableCount * 16);
        var tables = new Dictionary<uint, (uint, uint)>();
        for (int table = 0; table < tableCount; table++)
            tables.TryAdd(U32(records, table * 16), (U32(records, table * 16 + 8), U32(records, table * 16 + 12)));
        var face = new OpenTypeFace(lazy, source, index, tables);
        face.ReadSmallTables(eager);
        return face;
    }

    private void ReadSmallTables(FontBytes bytes)
    {
        if (Table(bytes, "head") is not { Length: >= 54 } head)
            throw new InvalidDataException($"{Source}: face {Index} has no head table.");
        UnitsPerEm = U16(head, 18);
        if (UnitsPerEm == 0) throw new InvalidDataException($"{Source}: face {Index} has 0 units per em.");
        MacStyle = U16(head, 44);
        if (Table(bytes, "hhea") is { Length: >= 8 } hhea)
        {
            HorizontalAscender = I16(hhea, 4);
            HorizontalDescender = I16(hhea, 6);
        }
        if (Table(bytes, "maxp") is { Length: >= 6 } maxp) GlyphCount = U16(maxp, 4);
        if (Table(bytes, "OS/2") is { Length: >= 78 } os2)
        {
            HasOs2 = true;
            Os2Version = U16(os2, 0);
            AverageCharWidth = I16(os2, 2);
            WeightClass = U16(os2, 4);
            Panose = os2.AsSpan(32, 10).ToArray();
            Selection = U16(os2, 62);
            WindowsAscent = U16(os2, 74);
            WindowsDescent = U16(os2, 76);
            if (Os2Version >= 1 && os2.Length >= 86)
            {
                CodePageRange1 = U32(os2, 78);
                CodePageRange2 = U32(os2, 82);
            }
            if (Os2Version >= 2 && os2.Length >= 92) DefaultChar = U16(os2, 90);
        }
        if (Table(bytes, "post") is { Length: >= 16 } post) PostFixedPitch = U32(post, 12) != 0;
        HasCffOutlines = Table(bytes, "glyf") == null && Table(bytes, "CFF ") != null;
        if (Table(bytes, "fvar") is { Length: >= 16 } fvar)
        {
            IsVariable = true;
            NamedInstances = ReadNamedInstances(fvar);
        }
        var wanted = new HashSet<ushort> { 1, 2, 4, 16, 17 };
        foreach (OpenTypeNamedInstance instance in NamedInstances) wanted.Add(instance.SubfamilyNameId);
        if (Table(bytes, "name") is { } name) Names = ReadNames(name, wanted);
    }

    private static IReadOnlyList<OpenTypeNamedInstance> ReadNamedInstances(byte[] fvar)
    {
        int axesOffset = U16(fvar, 4), axisCount = U16(fvar, 8), axisSize = U16(fvar, 10);
        int instanceCount = U16(fvar, 12), instanceSize = U16(fvar, 14);
        if (axisSize < 20 || instanceSize < 4 + axisCount * 4) return [];
        var tags = new string[axisCount];
        for (int axis = 0; axis < axisCount; axis++)
        {
            int at = axesOffset + axis * axisSize;
            if (at + 4 > fvar.Length) return [];
            tags[axis] = Encoding.ASCII.GetString(fvar, at, 4);
        }
        var instances = new List<OpenTypeNamedInstance>(instanceCount);
        int first = axesOffset + axisCount * axisSize;
        for (int instance = 0; instance < instanceCount; instance++)
        {
            int at = first + instance * instanceSize;
            if (at + 4 + axisCount * 4 > fvar.Length) break;
            var coordinates = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int axis = 0; axis < axisCount; axis++)
                coordinates[tags[axis]] = (int)U32(fvar, at + 4 + axis * 4) / 65536f;
            instances.Add(new OpenTypeNamedInstance(U16(fvar, at), coordinates));
        }
        return instances;
    }

    private static IReadOnlyList<OpenTypeName> ReadNames(byte[] table, HashSet<ushort> wanted)
    {
        if (table.Length < 6) return [];
        int count = U16(table, 2), storage = U16(table, 4);
        var names = new List<OpenTypeName>();
        for (int record = 0; record < count; record++)
        {
            int at = 6 + record * 12;
            if (at + 12 > table.Length) break;
            ushort platform = U16(table, at), encoding = U16(table, at + 2);
            ushort language = U16(table, at + 4), nameId = U16(table, at + 6);
            int length = U16(table, at + 8), offset = U16(table, at + 10);
            // GDI reads the Windows names, which are UTF-16BE (symbol, Unicode BMP and full repertoire).
            if (platform != WindowsPlatform || encoding is not (0 or 1 or 10) || !wanted.Contains(nameId)) continue;
            int start = storage + offset;
            if (start + length > table.Length) continue;
            names.Add(new OpenTypeName(nameId, language, Encoding.BigEndianUnicode.GetString(table, start, length)));
        }
        return names;
    }

    private Dictionary<int, (short YMax, short YMin)> ReadVerticalDeviceMetrics()
    {
        var records = new Dictionary<int, (short, short)>();
        if (Table(_bytes, "VDMX") is not { Length: >= 6 } vdmx) return records;
        int ratios = U16(vdmx, 4);
        for (int ratio = 0; ratio < ratios; ratio++)
        {
            int at = 6 + ratio * 4;
            if (at + 4 > vdmx.Length) break;
            byte x = vdmx[at + 1], yStart = vdmx[at + 2], yEnd = vdmx[at + 3];
            // The first ratio range a 1:1 device falls in (0:0:0 matches any device).
            bool square = (x == 0 && yStart == 0 && yEnd == 0) || (yStart <= x && x <= yEnd);
            if (!square) continue;
            int offsetAt = 6 + ratios * 4 + ratio * 2;
            if (offsetAt + 2 > vdmx.Length) break;
            int group = U16(vdmx, offsetAt);
            if (group + 4 > vdmx.Length) break;
            int recordCount = U16(vdmx, group);
            for (int entry = 0; entry < recordCount; entry++)
            {
                int record = group + 4 + entry * 6;
                if (record + 6 > vdmx.Length) break;
                records.TryAdd(U16(vdmx, record), (I16(vdmx, record + 2), I16(vdmx, record + 4)));
            }
            break;
        }
        return records;
    }

    private byte[]? Table(FontBytes bytes, string tag)
    {
        if (!_tables.TryGetValue(Tag(tag), out var entry)) return null;
        return bytes.Read(entry.Offset, checked((int)entry.Length));
    }

    private bool TryGetTable(string tag, out uint offset, out uint length)
    {
        bool found = _tables.TryGetValue(Tag(tag), out var entry);
        (offset, length) = entry;
        return found;
    }

    private static uint Tag(string tag)
        => (uint)tag[0] << 24 | (uint)tag[1] << 16 | (uint)tag[2] << 8 | tag[3];

    private static ushort U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at, 2));
    private static short I16(byte[] data, int at) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(at, 2));
    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at, 4));

    /// <summary>The character map GDI reads: Windows full repertoire (3,10), then Unicode (0,*), then Windows BMP
    /// (3,1), then Windows symbol (3,0); formats 0, 4, 6 and 12.</summary>
    private sealed class CharacterMap
    {
        private readonly Func<int, int> _lookup;
        private CharacterMap(Func<int, int> lookup) => _lookup = lookup;

        public int Lookup(int scalar) => scalar < 0 ? 0 : _lookup(scalar);

        public static CharacterMap Read(OpenTypeFace face)
        {
            if (face.Table(face._bytes, "cmap") is not { Length: >= 4 } cmap) return new CharacterMap(_ => 0);
            int count = U16(cmap, 2);
            var subtables = new List<(ushort Platform, ushort Encoding, int Offset)>();
            for (int record = 0; record < count; record++)
            {
                int at = 4 + record * 8;
                if (at + 8 > cmap.Length) break;
                subtables.Add((U16(cmap, at), U16(cmap, at + 2), checked((int)U32(cmap, at + 4))));
            }
            int Rank((ushort Platform, ushort Encoding, int Offset) subtable) => subtable switch
            {
                (3, 10, _) => 0,
                (0, 6, _) or (0, 4, _) => 1,
                (0, _, _) => 2,
                (3, 1, _) => 3,
                (3, 0, _) => 4,
                _ => 9,
            };
            foreach (var subtable in subtables.Where(table => Rank(table) < 9).OrderBy(Rank))
                if (Parse(cmap, subtable.Offset, symbol: subtable is (3, 0, _)) is { } lookup)
                    return new CharacterMap(lookup);
            return new CharacterMap(_ => 0);
        }

        private static Func<int, int>? Parse(byte[] cmap, int offset, bool symbol)
        {
            if (offset < 0 || offset + 2 > cmap.Length) return null;
            int format = U16(cmap, offset);
            Func<int, int>? lookup = format switch
            {
                0 => Format0(cmap, offset),
                4 => Format4(cmap, offset),
                6 => Format6(cmap, offset),
                12 => Format12(cmap, offset),
                _ => null,
            };
            // A symbol map lists its characters at U+F000 to U+F0FF; GDI maps the low bytes onto them.
            if (lookup != null && symbol)
            {
                Func<int, int> inner = lookup;
                lookup = scalar =>
                {
                    int glyph = inner(scalar);
                    return glyph != 0 || scalar > 0xff ? glyph : inner(0xf000 + scalar);
                };
            }
            return lookup;
        }

        private static Func<int, int>? Format0(byte[] cmap, int offset)
        {
            if (offset + 262 > cmap.Length) return null;
            byte[] glyphs = cmap.AsSpan(offset + 6, 256).ToArray();
            return scalar => scalar < 256 ? glyphs[scalar] : 0;
        }

        private static Func<int, int>? Format6(byte[] cmap, int offset)
        {
            if (offset + 10 > cmap.Length) return null;
            int first = U16(cmap, offset + 6), count = U16(cmap, offset + 8);
            if (offset + 10 + count * 2 > cmap.Length) return null;
            ushort[] glyphs = new ushort[count];
            for (int i = 0; i < count; i++) glyphs[i] = U16(cmap, offset + 10 + i * 2);
            return scalar => scalar >= first && scalar - first < count ? glyphs[scalar - first] : 0;
        }

        private static Func<int, int>? Format4(byte[] cmap, int offset)
        {
            if (offset + 14 > cmap.Length) return null;
            int length = Math.Min(U16(cmap, offset + 2), cmap.Length - offset);
            int segments = U16(cmap, offset + 6) / 2;
            int ends = offset + 14, starts = ends + segments * 2 + 2, deltas = starts + segments * 2;
            int rangeOffsets = deltas + segments * 2;
            if (rangeOffsets + segments * 2 > offset + length) return null;
            var end = new ushort[segments];
            var start = new ushort[segments];
            var delta = new short[segments];
            var rangeOffset = new ushort[segments];
            for (int i = 0; i < segments; i++)
            {
                end[i] = U16(cmap, ends + i * 2);
                start[i] = U16(cmap, starts + i * 2);
                delta[i] = I16(cmap, deltas + i * 2);
                rangeOffset[i] = U16(cmap, rangeOffsets + i * 2);
            }
            int limit = offset + length;
            return scalar =>
            {
                if (scalar > 0xffff) return 0;
                int low = 0, high = segments - 1;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    if (end[middle] < scalar) low = middle + 1;
                    else high = middle;
                }
                if (low >= segments || start[low] > scalar || end[low] < scalar) return 0;
                if (rangeOffset[low] == 0) return (scalar + delta[low]) & 0xffff;
                int at = rangeOffsets + low * 2 + rangeOffset[low] + (scalar - start[low]) * 2;
                if (at + 2 > limit) return 0;
                int glyph = U16(cmap, at);
                return glyph == 0 ? 0 : (glyph + delta[low]) & 0xffff;
            };
        }

        private static Func<int, int>? Format12(byte[] cmap, int offset)
        {
            if (offset + 16 > cmap.Length) return null;
            long groups = U32(cmap, offset + 12);
            if (offset + 16 + groups * 12 > cmap.Length) return null;
            var first = new uint[groups];
            var last = new uint[groups];
            var glyph = new uint[groups];
            for (int i = 0; i < groups; i++)
            {
                int at = offset + 16 + i * 12;
                first[i] = U32(cmap, at);
                last[i] = U32(cmap, at + 4);
                glyph[i] = U32(cmap, at + 8);
            }
            return scalar =>
            {
                int low = 0, high = (int)groups - 1;
                while (low <= high)
                {
                    int middle = (low + high) / 2;
                    if (last[middle] < scalar) low = middle + 1;
                    else if (first[middle] > scalar) high = middle - 1;
                    else return (int)(glyph[middle] + (uint)scalar - first[middle]);
                }
                return 0;
            };
        }
    }

    /// <summary>The embedded bitmap strikes (<c>EBLC</c>/<c>EBDT</c>, or <c>bloc</c>/<c>bdat</c>): index formats 1 to 5,
    /// image formats 1, 2 and 5 to 7 (the composite formats 8 and 9 report their box as ink).</summary>
    private sealed class BitmapStrikes
    {
        private readonly OpenTypeFace _face;
        private readonly uint _dataOffset;
        private readonly Dictionary<int, Strike> _strikes;

        private BitmapStrikes(OpenTypeFace face, uint dataOffset, Dictionary<int, Strike> strikes)
        {
            _face = face;
            _dataOffset = dataOffset;
            _strikes = strikes;
        }

        private sealed record Strike(byte BitDepth, IReadOnlyList<IndexSubtable> Subtables);

        private sealed record IndexSubtable(
            int FirstGlyph, int LastGlyph, int IndexFormat, int ImageFormat, uint ImageDataOffset,
            uint[]? Offsets, int[]? SparseGlyphs, uint ImageSize, byte[]? BigMetrics);

        public bool Has(int pixelsPerEm) => _strikes.ContainsKey(pixelsPerEm);

        public static BitmapStrikes Read(OpenTypeFace face)
        {
            var strikes = new Dictionary<int, Strike>();
            string location = face._tables.ContainsKey(Tag("EBLC")) ? "EBLC" : "bloc";
            string data = location == "EBLC" ? "EBDT" : "bdat";
            if (!face.TryGetTable(data, out uint dataOffset, out _)
                || face.Table(face._bytes, location) is not { Length: >= 8 } eblc)
                return new BitmapStrikes(face, 0, strikes);
            long sizes = U32(eblc, 4);
            for (int size = 0; size < sizes; size++)
            {
                int at = 8 + size * 48;
                if (at + 48 > eblc.Length) break;
                int arrayOffset = checked((int)U32(eblc, at));
                long subtableCount = U32(eblc, at + 8);
                byte ppemX = eblc[at + 44], ppemY = eblc[at + 45], bitDepth = eblc[at + 46];
                if (ppemX != ppemY || bitDepth is not (1 or 2 or 4 or 8)) continue;
                var subtables = new List<IndexSubtable>();
                for (int entry = 0; entry < subtableCount; entry++)
                {
                    int element = arrayOffset + entry * 8;
                    if (element + 8 > eblc.Length) break;
                    int firstGlyph = U16(eblc, element), lastGlyph = U16(eblc, element + 2);
                    int header = arrayOffset + checked((int)U32(eblc, element + 4));
                    if (ReadSubtable(eblc, header, firstGlyph, lastGlyph) is { } subtable) subtables.Add(subtable);
                }
                strikes.TryAdd(ppemY, new Strike(bitDepth, subtables));
            }
            return new BitmapStrikes(face, dataOffset, strikes);
        }

        private static IndexSubtable? ReadSubtable(byte[] eblc, int at, int firstGlyph, int lastGlyph)
        {
            if (at + 8 > eblc.Length || lastGlyph < firstGlyph) return null;
            int indexFormat = U16(eblc, at), imageFormat = U16(eblc, at + 2);
            uint imageDataOffset = U32(eblc, at + 4);
            int count = lastGlyph - firstGlyph + 1;
            int body = at + 8;
            switch (indexFormat)
            {
                case 1 when body + (count + 1) * 4 <= eblc.Length:
                {
                    var offsets = new uint[count + 1];
                    for (int i = 0; i <= count; i++) offsets[i] = U32(eblc, body + i * 4);
                    return new IndexSubtable(firstGlyph, lastGlyph, 1, imageFormat, imageDataOffset, offsets, null, 0, null);
                }
                case 2 when body + 12 <= eblc.Length:
                    return new IndexSubtable(firstGlyph, lastGlyph, 2, imageFormat, imageDataOffset, null, null,
                                             U32(eblc, body), eblc.AsSpan(body + 4, 8).ToArray());
                case 3 when body + (count + 1) * 2 <= eblc.Length:
                {
                    var offsets = new uint[count + 1];
                    for (int i = 0; i <= count; i++) offsets[i] = U16(eblc, body + i * 2);
                    return new IndexSubtable(firstGlyph, lastGlyph, 3, imageFormat, imageDataOffset, offsets, null, 0, null);
                }
                case 4 when body + 4 <= eblc.Length:
                {
                    int glyphs = checked((int)U32(eblc, body));
                    if (body + 4 + (glyphs + 1) * 4 > eblc.Length) return null;
                    var ids = new int[glyphs + 1];
                    var offsets = new uint[glyphs + 1];
                    for (int i = 0; i <= glyphs; i++)
                    {
                        ids[i] = U16(eblc, body + 4 + i * 4);
                        offsets[i] = U16(eblc, body + 4 + i * 4 + 2);
                    }
                    return new IndexSubtable(firstGlyph, lastGlyph, 4, imageFormat, imageDataOffset, offsets, ids, 0, null);
                }
                case 5 when body + 16 <= eblc.Length:
                {
                    int glyphs = checked((int)U32(eblc, body + 12));
                    if (body + 16 + glyphs * 2 > eblc.Length) return null;
                    var ids = new int[glyphs];
                    for (int i = 0; i < glyphs; i++) ids[i] = U16(eblc, body + 16 + i * 2);
                    return new IndexSubtable(firstGlyph, lastGlyph, 5, imageFormat, imageDataOffset, null, ids,
                                             U32(eblc, body), eblc.AsSpan(body + 4, 8).ToArray());
                }
                default:
                    return null;
            }
        }

        public EmbeddedBitmapGlyph? Glyph(int pixelsPerEm, int glyph)
        {
            if (!_strikes.TryGetValue(pixelsPerEm, out Strike? strike)) return null;
            foreach (IndexSubtable subtable in strike.Subtables)
            {
                if (glyph < subtable.FirstGlyph || glyph > subtable.LastGlyph) continue;
                if (Locate(subtable, glyph) is not { } located) continue;
                (uint start, uint length) = located;
                if (length == 0) return null;
                byte[] image = _face._bytes.Read(_dataOffset + subtable.ImageDataOffset + start, checked((int)length));
                return Decode(subtable, image, strike.BitDepth);
            }
            return null;
        }

        private static (uint Start, uint Length)? Locate(IndexSubtable subtable, int glyph)
        {
            switch (subtable.IndexFormat)
            {
                case 1 or 3:
                {
                    int i = glyph - subtable.FirstGlyph;
                    return (subtable.Offsets![i], subtable.Offsets[i + 1] - subtable.Offsets[i]);
                }
                case 2:
                {
                    uint i = (uint)(glyph - subtable.FirstGlyph);
                    return (i * subtable.ImageSize, subtable.ImageSize);
                }
                case 4:
                {
                    int i = Array.IndexOf(subtable.SparseGlyphs!, glyph, 0, subtable.SparseGlyphs!.Length - 1);
                    return i < 0 ? null : (subtable.Offsets![i], subtable.Offsets[i + 1] - subtable.Offsets[i]);
                }
                case 5:
                {
                    int i = Array.BinarySearch(subtable.SparseGlyphs!, glyph);
                    return i < 0 ? null : ((uint)i * subtable.ImageSize, subtable.ImageSize);
                }
                default:
                    return null;
            }
        }

        private static EmbeddedBitmapGlyph? Decode(IndexSubtable subtable, byte[] image, int bitDepth)
        {
            // Small metrics (formats 1, 2, 8) and big metrics (6, 7, 9) lead with height, width, bearing x, bearing y and
            // advance; format 5 takes the index subtable's big metrics.
            byte[]? metrics = subtable.ImageFormat switch
            {
                1 or 2 or 8 when image.Length >= 5 => image,
                6 or 7 or 9 when image.Length >= 8 => image,
                5 => subtable.BigMetrics,
                _ => null,
            };
            if (metrics == null) return null;
            int height = metrics[0], width = metrics[1], bearingX = (sbyte)metrics[2], bearingY = (sbyte)metrics[3];
            int advance = metrics[4];
            int dataStart = subtable.ImageFormat switch { 1 or 2 => 5, 6 or 7 => 8, _ => 0 };
            bool byteAligned = subtable.ImageFormat is 1 or 6;
            // A composite glyph (formats 8 and 9) is not decoded: its whole box counts as ink.
            if (subtable.ImageFormat is 8 or 9)
                return new EmbeddedBitmapGlyph(width, height, bearingX, bearingY, advance, 0, 0, width, height);
            int left = width, right = -1, top = height, bottom = -1;
            int rowBits = width * bitDepth;
            int rowBytes = (rowBits + 7) / 8;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                long bit = byteAligned ? (long)y * rowBytes * 8 + (long)x * bitDepth : ((long)y * width + x) * bitDepth;
                long byteAt = dataStart + bit / 8;
                if (byteAt >= image.Length) continue;
                int shift = 8 - bitDepth - (int)(bit % 8);
                int value = shift >= 0 ? (image[byteAt] >> shift) & ((1 << bitDepth) - 1) : 0;
                if (value == 0) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
            return right < 0
                ? new EmbeddedBitmapGlyph(width, height, bearingX, bearingY, advance, 0, 0, 0, 0)
                : new EmbeddedBitmapGlyph(width, height, bearingX, bearingY, advance,
                                          left, top, right - left + 1, bottom - top + 1);
        }
    }

    /// <summary>Whether a glyph draws: the box a <c>glyf</c> header records, or for CFF whether the charstring has a
    /// drawing operator (a call into a subroutine counts as drawing).</summary>
    private sealed class OutlineInk
    {
        private readonly Func<int, int, bool> _hasInk;
        private OutlineInk(Func<int, int, bool> hasInk) => _hasInk = hasInk;

        public bool HasInk(int glyph, int minimumUnits) => glyph >= 0 && _hasInk(glyph, minimumUnits);

        public static OutlineInk Read(OpenTypeFace face)
        {
            if (face.TryGetTable("glyf", out uint glyfOffset, out uint glyfLength)
                && face.Table(face._bytes, "loca") is { } loca
                && face.Table(face._bytes, "head") is { Length: >= 54 } head)
            {
                bool longOffsets = I16(head, 50) != 0;
                return new OutlineInk((glyph, minimum) =>
                {
                    int entry = longOffsets ? 4 : 2;
                    if ((glyph + 2) * entry > loca.Length) return false;
                    uint start = longOffsets ? U32(loca, glyph * 4) : U16(loca, glyph * 2) * 2u;
                    uint end = longOffsets ? U32(loca, glyph * 4 + 4) : U16(loca, glyph * 2 + 2) * 2u;
                    if (end <= start || end - start < 10 || end > glyfLength) return false;
                    byte[] header = face._bytes.Read(glyfOffset + start, 10);
                    if (I16(header, 0) == 0) return false;
                    int width = I16(header, 6) - I16(header, 2), height = I16(header, 8) - I16(header, 4);
                    return width >= minimum || height >= minimum;
                });
            }
            if (face.Table(face._bytes, "CFF ") is { } cff) return new OutlineInk((glyph, _) => CffDraws(cff, glyph));
            // CFF2 and anything else: assume the glyph draws.
            return new OutlineInk((_, _) => true);
        }

        private static bool CffDraws(byte[] cff, int glyph)
        {
            try
            {
                int at = cff[2];
                at = SkipIndex(cff, at);                        // Name INDEX
                (int topStart, int topEnd, at) = FirstIndexEntry(cff, at);   // Top DICT INDEX
                int charStrings = -1;
                ParseDict(cff, topStart, topEnd, (op, operands) =>
                {
                    if (op == 17 && operands.Count > 0) charStrings = (int)operands[^1];
                });
                if (charStrings < 0) return true;
                if (IndexEntry(cff, charStrings, glyph) is not { } entry) return false;
                return CharstringDraws(cff, entry.Start, entry.End);
            }
            catch (Exception error) when (error is IndexOutOfRangeException or ArgumentOutOfRangeException
                                              or InvalidDataException)
            {
                return true;
            }
        }

        private static bool CharstringDraws(byte[] data, int start, int end)
        {
            int stack = 0, stems = 0;
            bool widthSeen = false;
            for (int at = start; at < end;)
            {
                byte b = data[at];
                if (b >= 32 || b == 28)
                {
                    at += b == 28 ? 3 : b <= 246 ? 1 : b <= 254 ? 2 : 5;
                    stack++;
                    continue;
                }
                at++;
                switch (b)
                {
                    case 1 or 3 or 18 or 23:                     // hstem, vstem, hstemhm, vstemhm
                        if (!widthSeen && stack % 2 == 1) stack--;
                        widthSeen = true;
                        stems += stack / 2;
                        break;
                    case 19 or 20:                               // hintmask, cntrmask (an implied vstem first)
                        if (!widthSeen && stack % 2 == 1) stack--;
                        widthSeen = true;
                        stems += stack / 2;
                        at += (stems + 7) / 8;
                        break;
                    case 14:                                     // endchar
                        return false;
                    case 4 or 21 or 22:                          // vmoveto, rmoveto, hmoveto
                        widthSeen = true;
                        break;
                    case 5 or 6 or 7 or 8 or 24 or 25 or 26 or 27 or 30 or 31 or 10 or 29:
                        return true;                             // a line, a curve, or a subroutine
                    case 12:
                        if (at < end && data[at] is 34 or 35 or 36 or 37) return true;   // flex
                        at++;
                        break;
                }
                stack = 0;
            }
            return false;
        }

        private static int SkipIndex(byte[] data, int at)
        {
            int count = U16(data, at);
            if (count == 0) return at + 2;
            int offSize = data[at + 2];
            int last = Offset(data, at + 3 + count * offSize, offSize);
            return at + 3 + (count + 1) * offSize + last - 1;
        }

        private static (int Start, int End, int Next) FirstIndexEntry(byte[] data, int at)
        {
            (int Start, int End) entry = IndexEntry(data, at, 0) ?? throw new InvalidDataException("Empty CFF INDEX.");
            return (entry.Start, entry.End, SkipIndex(data, at));
        }

        private static (int Start, int End)? IndexEntry(byte[] data, int at, int index)
        {
            int count = U16(data, at);
            if (index >= count) return null;
            int offSize = data[at + 2];
            int baseAt = at + 3 + (count + 1) * offSize - 1;
            return (baseAt + Offset(data, at + 3 + index * offSize, offSize),
                    baseAt + Offset(data, at + 3 + (index + 1) * offSize, offSize));
        }

        private static int Offset(byte[] data, int at, int size)
        {
            int value = 0;
            for (int i = 0; i < size; i++) value = value << 8 | data[at + i];
            return value;
        }

        private static void ParseDict(byte[] data, int start, int end, Action<int, List<double>> visit)
        {
            var operands = new List<double>();
            for (int at = start; at < end;)
            {
                byte b = data[at];
                if (b <= 21)
                {
                    int op = b == 12 ? 1200 + data[at + 1] : b;
                    at += b == 12 ? 2 : 1;
                    visit(op, operands);
                    operands.Clear();
                }
                else if (b == 28) { operands.Add((short)(data[at + 1] << 8 | data[at + 2])); at += 3; }
                else if (b == 29) { operands.Add(BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at + 1, 4))); at += 5; }
                else if (b == 30)
                {
                    // A real number: nibbles to the 0xf terminator.
                    at++;
                    while (at < end && (data[at] & 0x0f) != 0x0f && (data[at] >> 4) != 0x0f) at++;
                    at++;
                    operands.Add(0);
                }
                else if (b <= 246) { operands.Add(b - 139); at++; }
                else if (b <= 250) { operands.Add((b - 247) * 256 + data[at + 1] + 108); at += 2; }
                else { operands.Add(-(b - 251) * 256 - data[at + 1] - 108); at += 2; }
            }
        }
    }

    private abstract class FontBytes
    {
        public abstract byte[] Read(long offset, int length);
    }

    private sealed class ArrayBytes(byte[] data) : FontBytes
    {
        public override byte[] Read(long offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
                throw new InvalidDataException("A font table lies outside its file.");
            return data.AsSpan((int)offset, length).ToArray();
        }
    }

    private sealed class StreamBytes(Stream stream) : FontBytes
    {
        public override byte[] Read(long offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > stream.Length)
                throw new InvalidDataException("A font table lies outside its file.");
            var buffer = new byte[length];
            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadExactly(buffer);
            return buffer;
        }
    }

    private sealed class PathBytes(string path) : FontBytes
    {
        public override byte[] Read(long offset, int length)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            return new StreamBytes(stream).Read(offset, length);
        }
    }
}

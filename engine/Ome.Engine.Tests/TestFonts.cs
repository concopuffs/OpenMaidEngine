using System.Buffers.Binary;
using System.Text;
using Ome.Engine.Text;

/// <summary>A glyph's outline header in <c>glyf</c>: its contour count (-1 for a composite) and box, in font units.
/// </summary>
internal sealed record TestGlyph(short Contours, short XMin, short YMin, short XMax, short YMax)
{
    public static TestGlyph Box(short xMin, short yMin, short xMax, short yMax) => new(1, xMin, yMin, xMax, yMax);
}

/// <summary>A character map subtable: platform, encoding, format (0, 4, 6 or 12) and its characters' glyphs; format 4
/// with <paramref name="RangeOffsets"/> reads its glyphs through a glyph index array, as large CJK faces do.</summary>
internal sealed record TestCmap(ushort Platform, ushort Encoding, int Format, IReadOnlyDictionary<int, int> Map,
                                bool RangeOffsets = false);

/// <summary>One bitmap of a strike: the glyph, its bearings and advance, and its rows ('.' is blank, '#' full, a hex
/// digit a gray level).</summary>
internal sealed record TestBitmap(int Glyph, int BearingX, int BearingY, int Advance, params string[] Rows)
{
    public int Width => Rows.Length == 0 ? 0 : Rows[0].Length;
    public int Height => Rows.Length;
}

/// <summary>A bitmap strike written as one index subtable of <paramref name="IndexFormat"/> over images of
/// <paramref name="ImageFormat"/>.</summary>
internal sealed record TestStrike(int Ppem, int BitDepth, int IndexFormat, int ImageFormat,
                                  IReadOnlyList<TestBitmap> Bitmaps);

/// <summary>A <c>VDMX</c> ratio range and its records.</summary>
internal sealed record TestDeviceRatio(byte X, byte YStart, byte YEnd,
                                       IReadOnlyList<(ushort Ppem, short YMax, short YMin)> Records);

/// <summary>
/// Builds a small sfnt face holding the tables <see cref="OpenTypeFace"/> reads (head, hhea, maxp, OS/2, post, name,
/// cmap, glyf/loca or CFF, VDMX, EBLC/EBDT, fvar), with whatever values a test sets, so the GDI rules can be tested
/// on faces with known contents. Real fonts are left to the Windows oracle tests.
/// </summary>
internal sealed class TestFont
{
    public ushort UnitsPerEm { get; set; } = 1000;
    public ushort MacStyle { get; set; }
    public short Ascender { get; set; } = 880;
    public short Descender { get; set; } = -120;
    public bool HasOs2 { get; set; } = true;
    public ushort Os2Version { get; set; } = 4;
    public short AverageCharWidth { get; set; } = 500;
    public ushort WeightClass { get; set; } = 400;
    public ushort Selection { get; set; } = 0x40;
    public byte[] Panose { get; set; } = new byte[10];
    public ushort WindowsAscent { get; set; } = 880;
    public ushort WindowsDescent { get; set; } = 120;
    public uint CodePageRange1 { get; set; } = 1u << 17;
    public uint CodePageRange2 { get; set; }
    public ushort DefaultChar { get; set; }
    public bool PostFixedPitch { get; set; }
    public bool ShortLoca { get; set; }
    public List<(ushort Platform, ushort Encoding, ushort Language, ushort NameId, string Value)> Names { get; } = [];
    public List<TestCmap> CharacterMaps { get; } = [];

    /// <summary>The outlines by glyph index; glyph 0 is <c>.notdef</c>, and null is a glyph with no outline.</summary>
    public List<TestGlyph?> Glyphs { get; } = [null];

    /// <summary>A CFF table in place of glyf and loca (<see cref="Cff"/>).</summary>
    public byte[]? CffTable { get; set; }

    public List<TestDeviceRatio> DeviceRatios { get; } = [];
    public List<TestStrike> Strikes { get; } = [];
    public List<string> Axes { get; } = [];
    public List<(ushort SubfamilyNameId, float[] Coordinates)> Instances { get; } = [];

    /// <summary>Adds a Windows name.</summary>
    public TestFont Name(ushort nameId, string value, ushort language = GdiFontRules.UnitedStatesEnglish)
    {
        Names.Add((3, 1, language, nameId, value));
        return this;
    }

    /// <summary>Adds a Windows BMP (3,1) format 4 character map.</summary>
    public TestFont Map(params (int Scalar, int Glyph)[] map)
    {
        CharacterMaps.Add(new TestCmap(3, 1, 4, map.ToDictionary(entry => entry.Scalar, entry => entry.Glyph)));
        return this;
    }

    /// <summary>Adds a glyph and returns its index.</summary>
    public int AddGlyph(TestGlyph? glyph)
    {
        Glyphs.Add(glyph);
        return Glyphs.Count - 1;
    }

    public OpenTypeFace Read(string source = "test.ttf") => Assert.Single(OpenTypeFace.ReadData(Build(), source));

    /// <summary>The face's bytes, its table offsets counted from <paramref name="baseOffset"/> (its place in a
    /// collection).</summary>
    public byte[] Build(long baseOffset = 0)
    {
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["head"] = Head(),
            ["hhea"] = Hhea(),
            ["maxp"] = Maxp(),
            ["post"] = Post(),
            ["name"] = NameTable(),
            ["cmap"] = Cmap(),
        };
        if (HasOs2) tables["OS/2"] = Os2();
        if (CffTable != null) tables["CFF "] = CffTable;
        else (tables["glyf"], tables["loca"]) = GlyfAndLoca();
        if (DeviceRatios.Count > 0) tables["VDMX"] = Vdmx();
        if (Strikes.Count > 0) (tables["EBLC"], tables["EBDT"]) = Bitmaps();
        if (Axes.Count > 0) tables["fvar"] = Fvar();

        var output = new Writer();
        output.U32(CffTable != null ? 0x4F54544Fu : 0x00010000u);
        output.U16(tables.Count);
        int power = 1, log = 0;
        while (power * 2 <= tables.Count) { power *= 2; log++; }
        output.U16(power * 16);
        output.U16(log);
        output.U16(tables.Count * 16 - power * 16);
        long offset = 12 + tables.Count * 16;
        foreach ((string tag, byte[] data) in tables)
        {
            output.Bytes(Encoding.ASCII.GetBytes(tag));
            output.U32(0);
            output.U32((uint)(baseOffset + offset));
            output.U32((uint)data.Length);
            offset += Align(data.Length);
        }
        foreach (byte[] data in tables.Values)
        {
            output.Bytes(data);
            output.Bytes(new byte[Align(data.Length) - data.Length]);
        }
        return output.ToArray();
    }

    /// <summary>A collection (<c>ttcf</c>) of the faces, in order.</summary>
    public static byte[] Collection(params TestFont[] faces)
    {
        var output = new Writer();
        output.U32(0x74746366);
        output.U32(0x00010000);
        output.U32((uint)faces.Length);
        long position = 12 + faces.Length * 4;
        var built = new List<byte[]>();
        foreach (TestFont face in faces)
        {
            output.U32((uint)position);
            byte[] bytes = face.Build(position);
            built.Add(bytes);
            position += Align(bytes.Length);
        }
        foreach (byte[] bytes in built)
        {
            output.Bytes(bytes);
            output.Bytes(new byte[Align(bytes.Length) - bytes.Length]);
        }
        return output.ToArray();
    }

    /// <summary>A CFF table whose charstrings are <paramref name="charstrings"/> (Type 2 bytes), glyph 0 first.</summary>
    public static byte[] Cff(params byte[][] charstrings)
    {
        static byte[] Index(IReadOnlyList<byte[]> entries, int offSize)
        {
            var index = new Writer();
            index.U16(entries.Count);
            if (entries.Count == 0) return index.ToArray();
            index.U8(offSize);
            int at = 1;
            void Offset(int value)
            {
                for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8) index.U8((value >> shift) & 0xff);
            }
            Offset(at);
            foreach (byte[] entry in entries) Offset(at += entry.Length);
            foreach (byte[] entry in entries) index.Bytes(entry);
            return index.ToArray();
        }

        byte[] header = [1, 0, 4, 4];
        byte[] names = Index([Encoding.ASCII.GetBytes("Test")], 1);
        // The Top DICT gives the CharStrings offset as a five-byte integer, so its size does not depend on the value.
        int topSize = Index([new byte[6]], 1).Length;
        byte[] empty = Index([], 1);
        int charStrings = header.Length + names.Length + topSize + empty.Length * 2;
        var top = new Writer();
        top.U8(29);
        top.U32((uint)charStrings);
        top.U8(17);
        var output = new Writer();
        output.Bytes(header);
        output.Bytes(names);
        output.Bytes(Index([top.ToArray()], 1));
        output.Bytes(empty);   // String INDEX
        output.Bytes(empty);   // Global Subr INDEX
        output.Bytes(Index(charstrings, 2));
        return output.ToArray();
    }

    private static int Align(int length) => (length + 3) & ~3;

    private byte[] Head()
    {
        var head = new Writer();
        head.U32(0x00010000);
        head.U32(0x00010000);
        head.U32(0);
        head.U32(0x5F0F3CF5);
        head.U16(0);
        head.U16(UnitsPerEm);
        head.Bytes(new byte[16]);                        // created, modified
        head.I16(0); head.I16(Descender); head.I16((short)UnitsPerEm); head.I16(Ascender);
        head.U16(MacStyle);
        head.U16(8);
        head.I16(2);
        head.I16(ShortLoca ? (short)0 : (short)1);
        head.I16(0);
        return head.ToArray();
    }

    private byte[] Hhea()
    {
        var hhea = new Writer();
        hhea.U32(0x00010000);
        hhea.I16(Ascender);
        hhea.I16(Descender);
        hhea.Bytes(new byte[26]);
        hhea.U16(1);
        return hhea.ToArray();
    }

    private byte[] Maxp()
    {
        var maxp = new Writer();
        maxp.U32(0x00005000);
        maxp.U16(Glyphs.Count);
        return maxp.ToArray();
    }

    private byte[] Os2()
    {
        var os2 = new Writer();
        os2.U16(Os2Version);
        os2.I16(AverageCharWidth);
        os2.U16(WeightClass);
        os2.U16(5);                                      // usWidthClass
        os2.Bytes(new byte[24]);                         // fsType through sFamilyClass
        os2.Bytes(Panose);
        os2.Bytes(new byte[16]);                         // ulUnicodeRange1-4
        os2.Bytes(Encoding.ASCII.GetBytes("TEST"));
        os2.U16(Selection);
        os2.U16(0x20); os2.U16(0xffff);                  // first and last character
        os2.I16(Ascender); os2.I16(Descender); os2.I16(0);
        os2.U16(WindowsAscent);
        os2.U16(WindowsDescent);
        if (Os2Version >= 1)
        {
            os2.U32(CodePageRange1);
            os2.U32(CodePageRange2);
        }
        if (Os2Version >= 2)
        {
            os2.I16(0); os2.I16(0);                      // sxHeight, sCapHeight
            os2.U16(DefaultChar);
            os2.U16(0x20);
            os2.U16(1);
        }
        return os2.ToArray();
    }

    private byte[] Post()
    {
        var post = new Writer();
        post.U32(0x00030000);
        post.U32(0);
        post.I16(-100); post.I16(50);
        post.U32(PostFixedPitch ? 1u : 0u);
        post.Bytes(new byte[16]);
        return post.ToArray();
    }

    private byte[] NameTable()
    {
        var records = new Writer();
        var storage = new Writer();
        foreach ((ushort platform, ushort encoding, ushort language, ushort nameId, string value) in Names)
        {
            byte[] text = platform is 0 or 3 ? Encoding.BigEndianUnicode.GetBytes(value) : Encoding.ASCII.GetBytes(value);
            records.U16(platform); records.U16(encoding); records.U16(language); records.U16(nameId);
            records.U16(text.Length);
            records.U16(storage.Length);
            storage.Bytes(text);
        }
        var name = new Writer();
        name.U16(0);
        name.U16(Names.Count);
        name.U16(6 + Names.Count * 12);
        name.Bytes(records.ToArray());
        name.Bytes(storage.ToArray());
        return name.ToArray();
    }

    private byte[] Cmap()
    {
        var subtables = CharacterMaps.Select(map => map.Format switch
        {
            0 => Format0(map.Map),
            4 => Format4(map.Map, map.RangeOffsets),
            6 => Format6(map.Map),
            12 => Format12(map.Map),
            _ => throw new ArgumentException($"cmap format {map.Format}"),
        }).ToList();
        var cmap = new Writer();
        cmap.U16(0);
        cmap.U16(CharacterMaps.Count);
        int offset = 4 + CharacterMaps.Count * 8;
        for (int i = 0; i < CharacterMaps.Count; i++)
        {
            cmap.U16(CharacterMaps[i].Platform);
            cmap.U16(CharacterMaps[i].Encoding);
            cmap.U32((uint)offset);
            offset += subtables[i].Length;
        }
        foreach (byte[] subtable in subtables) cmap.Bytes(subtable);
        return cmap.ToArray();
    }

    private static byte[] Format0(IReadOnlyDictionary<int, int> map)
    {
        var table = new Writer();
        table.U16(0); table.U16(262); table.U16(0);
        for (int code = 0; code < 256; code++) table.U8(map.TryGetValue(code, out int glyph) ? glyph : 0);
        return table.ToArray();
    }

    private static byte[] Format6(IReadOnlyDictionary<int, int> map)
    {
        int first = map.Keys.Min(), last = map.Keys.Max();
        var table = new Writer();
        table.U16(6); table.U16(10 + (last - first + 1) * 2); table.U16(0);
        table.U16(first); table.U16(last - first + 1);
        for (int code = first; code <= last; code++) table.U16(map.TryGetValue(code, out int glyph) ? glyph : 0);
        return table.ToArray();
    }

    private static byte[] Format4(IReadOnlyDictionary<int, int> map, bool rangeOffsets)
    {
        var codes = map.Keys.Where(code => code < 0xffff).Order().ToList();
        var segments = new List<(int Start, int End, int Delta, int[]? Glyphs)>();
        if (rangeOffsets && codes.Count > 0)
        {
            int start = codes[0], end = codes[^1];
            segments.Add((start, end, 0,
                          Enumerable.Range(start, end - start + 1)
                                    .Select(code => map.TryGetValue(code, out int glyph) ? glyph : 0).ToArray()));
        }
        else
            foreach (int code in codes)
            {
                if (segments.Count > 0 && segments[^1].End == code - 1
                    && map[segments[^1].End] + 1 == map[code])
                    segments[^1] = (segments[^1].Start, code, segments[^1].Delta, null);
                else
                    segments.Add((code, code, map[code] - code, null));
            }
        segments.Add((0xffff, 0xffff, 1, null));
        int count = segments.Count;
        var glyphArray = new List<int>();
        var body = new Writer();
        foreach (var segment in segments) body.U16(segment.End);
        body.U16(0);
        foreach (var segment in segments) body.U16(segment.Start);
        foreach (var segment in segments) body.U16((ushort)(short)segment.Delta);
        for (int i = 0; i < count; i++)
        {
            if (segments[i].Glyphs is { } glyphs)
            {
                // From this idRangeOffset entry to the glyph's place in the array that follows the entries.
                body.U16((count - i + glyphArray.Count) * 2);
                glyphArray.AddRange(glyphs);
            }
            else body.U16(0);
        }
        foreach (int glyph in glyphArray) body.U16(glyph);
        var table = new Writer();
        table.U16(4);
        table.U16(14 + body.Length);
        table.U16(0);
        table.U16(count * 2);
        table.U16(0); table.U16(0); table.U16(0);          // searchRange, entrySelector, rangeShift: not read
        table.Bytes(body.ToArray());
        return table.ToArray();
    }

    private static byte[] Format12(IReadOnlyDictionary<int, int> map)
    {
        var codes = map.Keys.Order().ToList();
        var table = new Writer();
        table.U16(12); table.U16(0);
        table.U32((uint)(16 + codes.Count * 12));
        table.U32(0);
        table.U32((uint)codes.Count);
        foreach (int code in codes)
        {
            table.U32((uint)code); table.U32((uint)code); table.U32((uint)map[code]);
        }
        return table.ToArray();
    }

    private (byte[] Glyf, byte[] Loca) GlyfAndLoca()
    {
        var glyf = new Writer();
        var offsets = new List<int> { 0 };
        foreach (TestGlyph? glyph in Glyphs)
        {
            if (glyph != null)
            {
                glyf.I16(glyph.Contours);
                glyf.I16(glyph.XMin); glyf.I16(glyph.YMin); glyf.I16(glyph.XMax); glyf.I16(glyph.YMax);
                glyf.Bytes(new byte[2]);                 // even, as short offsets need
            }
            offsets.Add(glyf.Length);
        }
        var loca = new Writer();
        foreach (int offset in offsets)
            if (ShortLoca) loca.U16(offset / 2);
            else loca.U32((uint)offset);
        return (glyf.ToArray(), loca.ToArray());
    }

    private byte[] Vdmx()
    {
        var vdmx = new Writer();
        vdmx.U16(1);
        vdmx.U16(DeviceRatios.Count);
        vdmx.U16(DeviceRatios.Count);
        foreach (TestDeviceRatio ratio in DeviceRatios)
        {
            vdmx.U8(1); vdmx.U8(ratio.X); vdmx.U8(ratio.YStart); vdmx.U8(ratio.YEnd);
        }
        int group = 6 + DeviceRatios.Count * 6;
        foreach (TestDeviceRatio ratio in DeviceRatios)
        {
            vdmx.U16(group);
            group += 4 + ratio.Records.Count * 6;
        }
        foreach (TestDeviceRatio ratio in DeviceRatios)
        {
            vdmx.U16(ratio.Records.Count);
            vdmx.U8(ratio.Records.Min(record => record.Ppem));
            vdmx.U8(ratio.Records.Max(record => record.Ppem));
            foreach ((ushort ppem, short yMax, short yMin) in ratio.Records)
            {
                vdmx.U16(ppem); vdmx.I16(yMax); vdmx.I16(yMin);
            }
        }
        return vdmx.ToArray();
    }

    private byte[] Fvar()
    {
        var fvar = new Writer();
        fvar.U16(1); fvar.U16(0);
        fvar.U16(16); fvar.U16(2);
        fvar.U16(Axes.Count); fvar.U16(20);
        fvar.U16(Instances.Count); fvar.U16(4 + Axes.Count * 4);
        foreach (string axis in Axes)
        {
            fvar.Bytes(Encoding.ASCII.GetBytes(axis));
            fvar.U32(0); fvar.U32(400u << 16); fvar.U32(1000u << 16);
            fvar.U16(0); fvar.U16(256);
        }
        foreach ((ushort subfamily, float[] coordinates) in Instances)
        {
            fvar.U16(subfamily); fvar.U16(0);
            foreach (float coordinate in coordinates) fvar.U32((uint)(int)Math.Round(coordinate * 65536));
        }
        return fvar.ToArray();
    }

    private (byte[] Eblc, byte[] Ebdt) Bitmaps()
    {
        var ebdt = new Writer();
        ebdt.U32(0x00020000);
        var subtables = new List<(byte[] Bytes, int First, int Last)>();
        foreach (TestStrike strike in Strikes)
        {
            int dataStart = ebdt.Length;
            var images = new List<(int Glyph, byte[] Image)>();
            foreach (TestBitmap bitmap in strike.Bitmaps.OrderBy(bitmap => bitmap.Glyph))
                images.Add((bitmap.Glyph, Image(bitmap, strike.ImageFormat, strike.BitDepth)));
            int first = images[0].Glyph, last = images[^1].Glyph;
            var index = new Writer();
            index.U16(strike.IndexFormat);
            index.U16(strike.ImageFormat);
            index.U32((uint)dataStart);
            var offsets = new List<int>();
            switch (strike.IndexFormat)
            {
                case 1 or 3:
                {
                    // Every glyph of the range has an entry; one without a bitmap has none of its own.
                    int at = 0;
                    for (int glyph = first; glyph <= last; glyph++)
                    {
                        offsets.Add(at);
                        if (images.FirstOrDefault(entry => entry.Glyph == glyph).Image is { } image)
                        {
                            ebdt.Bytes(image);
                            at += image.Length;
                        }
                    }
                    offsets.Add(at);
                    foreach (int offset in offsets)
                        if (strike.IndexFormat == 1) index.U32((uint)offset);
                        else index.U16(offset);
                    if (strike.IndexFormat == 3 && offsets.Count % 2 == 1) index.U16(0);
                    break;
                }
                case 4:
                {
                    index.U32((uint)images.Count);
                    int at = 0;
                    foreach ((int glyph, byte[] image) in images)
                    {
                        index.U16(glyph); index.U16(at);
                        ebdt.Bytes(image);
                        at += image.Length;
                    }
                    index.U16(0); index.U16(at);
                    break;
                }
                case 2 or 5:
                {
                    TestBitmap shape = strike.Bitmaps[0];
                    index.U32((uint)images[0].Image.Length);
                    index.Bytes(BigMetrics(shape));
                    if (strike.IndexFormat == 5)
                    {
                        index.U32((uint)images.Count);
                        foreach ((int glyph, _) in images) index.U16(glyph);
                    }
                    foreach ((_, byte[] image) in images) ebdt.Bytes(image);
                    break;
                }
                default:
                    throw new ArgumentException($"index format {strike.IndexFormat}");
            }
            subtables.Add((index.ToArray(), first, last));
        }

        var eblc = new Writer();
        eblc.U32(0x00020000);
        eblc.U32((uint)Strikes.Count);
        int arrayAt = 8 + Strikes.Count * 48;
        for (int i = 0; i < Strikes.Count; i++)
        {
            TestStrike strike = Strikes[i];
            eblc.U32((uint)arrayAt);
            eblc.U32((uint)(8 + subtables[i].Bytes.Length));
            eblc.U32(1);
            eblc.U32(0);
            eblc.Bytes(new byte[24]);                    // horizontal and vertical line metrics
            eblc.U16(subtables[i].First);
            eblc.U16(subtables[i].Last);
            eblc.U8(strike.Ppem); eblc.U8(strike.Ppem); eblc.U8(strike.BitDepth); eblc.U8(1);
            arrayAt += 8 + Align(subtables[i].Bytes.Length);
        }
        foreach ((byte[] bytes, int first, int last) in subtables)
        {
            eblc.U16(first); eblc.U16(last); eblc.U32(8);
            eblc.Bytes(bytes);
            eblc.Bytes(new byte[Align(bytes.Length) - bytes.Length]);
        }
        return (eblc.ToArray(), ebdt.ToArray());
    }

    private static byte[] BigMetrics(TestBitmap bitmap)
        => [(byte)bitmap.Height, (byte)bitmap.Width, (byte)(sbyte)bitmap.BearingX, (byte)(sbyte)bitmap.BearingY,
            (byte)bitmap.Advance, 0, 0, (byte)bitmap.Height];

    private static byte[] Image(TestBitmap bitmap, int imageFormat, int bitDepth)
    {
        var image = new Writer();
        switch (imageFormat)
        {
            case 1 or 2 or 8:
                image.U8(bitmap.Height); image.U8(bitmap.Width);
                image.U8((byte)(sbyte)bitmap.BearingX); image.U8((byte)(sbyte)bitmap.BearingY);
                image.U8(bitmap.Advance);
                break;
            case 6 or 7:
                image.Bytes(BigMetrics(bitmap));
                break;
        }
        if (imageFormat == 8)
        {
            image.U8(0);
            image.U16(0);                                // no components: the reader takes the box as ink
            return image.ToArray();
        }
        bool byteAligned = imageFormat is 1 or 6;
        int full = (1 << bitDepth) - 1;
        var bits = new List<int>();
        void Flush()
        {
            while (bits.Count % 8 != 0) bits.Add(0);
        }
        foreach (string row in bitmap.Rows)
        {
            foreach (char pixel in row)
            {
                int value = pixel switch
                {
                    '.' => 0,
                    '#' => full,
                    _ => Math.Min(full, Convert.ToInt32(pixel.ToString(), 16)),
                };
                for (int bit = bitDepth - 1; bit >= 0; bit--) bits.Add((value >> bit) & 1);
            }
            if (byteAligned) Flush();
        }
        Flush();
        for (int at = 0; at < bits.Count; at += 8)
        {
            int value = 0;
            for (int bit = 0; bit < 8; bit++) value = value << 1 | bits[at + bit];
            image.U8(value);
        }
        return image.ToArray();
    }

    private sealed class Writer
    {
        private readonly MemoryStream _stream = new();
        public int Length => (int)_stream.Length;
        public void U8(int value) => _stream.WriteByte((byte)value);
        public void U16(int value) => Write(2, span => BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value));
        public void I16(short value) => Write(2, span => BinaryPrimitives.WriteInt16BigEndian(span, value));
        public void U32(uint value) => Write(4, span => BinaryPrimitives.WriteUInt32BigEndian(span, value));
        public void Bytes(byte[] bytes) => _stream.Write(bytes);
        public byte[] ToArray() => _stream.ToArray();

        private void Write(int size, SpanAction write)
        {
            Span<byte> buffer = stackalloc byte[size];
            write(buffer);
            _stream.Write(buffer);
        }

        private delegate void SpanAction(Span<byte> span);
    }
}

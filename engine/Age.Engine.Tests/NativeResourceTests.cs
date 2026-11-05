using System.Buffers.Binary;
using System.Text;
using Age.Engine.Frontend.NativeResources;
using Age.Engine.Sys4;

/// <summary>
/// The PE resource readers behind the port's AGERC dialogs (docs/engine-re.md, "Native menu bar", ﾍﾙﾌﾟ): dialog
/// templates, icons and the version resource.
/// </summary>
public class NativeResourceTests
{
    // ---- a PE holding arbitrary resources ----

    private sealed record Resource(int Type, ResourceName Name, byte[] Data);

    /// <summary>A 32-bit PE with one .rsrc section (RVA 0x1000, file offset 0x200) holding
    /// <paramref name="resources"/>, each in language 0x411.</summary>
    private static byte[] Pe(params Resource[] resources)
    {
        var tree = resources.GroupBy(r => r.Type).OrderBy(g => g.Key).ToList();
        var r = new List<byte>();
        void At(int offset, uint value)
        {
            while (r.Count < offset + 4) r.Add(0);
            BinaryPrimitives.WriteUInt32LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(r)[offset..], value);
        }
        int Directory(int named, int ids)
        {
            int at = r.Count;
            r.AddRange(new byte[16 + 8 * (named + ids)]);
            BinaryPrimitives.WriteUInt16LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(r)[(at + 12)..], (ushort)named);
            BinaryPrimitives.WriteUInt16LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(r)[(at + 14)..], (ushort)ids);
            return at;
        }
        var fixups = new List<(int Entry, byte[] Data)>();
        var names = new List<(int Entry, string Name)>();
        int root = Directory(0, tree.Count);
        for (int t = 0; t < tree.Count; t++)
        {
            var group = tree[t].OrderBy(x => x.Name.Id == null ? 0 : 1).ToList();
            int named = group.Count(x => x.Name.Name != null);
            int typeDir = Directory(named, group.Count - named);
            At(root + 16 + 8 * t, (uint)tree[t].Key);
            At(root + 20 + 8 * t, 0x80000000 | (uint)typeDir);
            for (int n = 0; n < group.Count; n++)
            {
                int langDir = Directory(0, 1);
                int entry = typeDir + 16 + 8 * n;
                if (group[n].Name.Id is int id) At(entry, (uint)id);
                else names.Add((entry, group[n].Name.Name!));
                At(entry + 4, 0x80000000 | (uint)langDir);
                At(langDir + 16, 0x411);
                int dataEntry = r.Count;
                r.AddRange(new byte[16]);
                At(langDir + 20, (uint)dataEntry);
                fixups.Add((dataEntry, group[n].Data));
            }
        }
        foreach ((int entry, string name) in names)
        {
            int at = r.Count;
            r.AddRange(BitConverter.GetBytes((ushort)name.Length));
            r.AddRange(Encoding.Unicode.GetBytes(name));
            while (r.Count % 4 != 0) r.Add(0);
            At(entry, 0x80000000 | (uint)at);
        }
        const int raw = 0x200, rva = 0x1000;
        foreach ((int dataEntry, byte[] data) in fixups)
        {
            int at = r.Count;
            r.AddRange(data);
            while (r.Count % 4 != 0) r.Add(0);
            At(dataEntry, (uint)(rva + at));
            At(dataEntry + 4, (uint)data.Length);
        }

        const int peHeader = 0x40, optional = peHeader + 24, sectionTable = optional + 224;
        var pe = new byte[raw + r.Count];
        Span<byte> s = pe;
        s[0] = (byte)'M'; s[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(s[0x3c..], peHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(s[peHeader..], 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(peHeader + 4)..], 0x14c);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(peHeader + 6)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(peHeader + 20)..], 224);
        BinaryPrimitives.WriteUInt16LittleEndian(s[optional..], 0x10b);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(optional + 96 + 2 * 8)..], rva);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(optional + 96 + 2 * 8 + 4)..], (uint)r.Count);
        Encoding.ASCII.GetBytes(".rsrc").CopyTo(s[sectionTable..]);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(sectionTable + 8)..], (uint)r.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(sectionTable + 12)..], rva);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(sectionTable + 16)..], (uint)r.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(sectionTable + 20)..], raw);
        r.ToArray().CopyTo(s[raw..]);
        return pe;
    }

    // ---- DLGTEMPLATEEX ----

    private sealed record Control(int Id, object Class, string Text, uint Style, int X, int Y, int W, int H);

    private static byte[] DialogEx(string title, uint style, (int X, int Y, int W, int H) rect, int points, string face,
                                   params Control[] controls)
    {
        var b = new List<byte>();
        void U16(int v) => b.AddRange(BitConverter.GetBytes((ushort)v));
        void U32(uint v) => b.AddRange(BitConverter.GetBytes(v));
        void Str(string v) { b.AddRange(Encoding.Unicode.GetBytes(v)); U16(0); }
        U16(1); U16(0xffff); U32(0); U32(0); U32(style); U16(controls.Length);
        U16(rect.X); U16(rect.Y); U16(rect.W); U16(rect.H);
        U16(0); U16(0); Str(title);
        U16(points); U16(400); b.Add(0); b.Add(0x80); Str(face);
        foreach (Control c in controls)
        {
            while (b.Count % 4 != 0) b.Add(0);
            U32(0); U32(0); U32(c.Style);
            U16(c.X); U16(c.Y); U16(c.W); U16(c.H);
            U32(c.Id < 0 ? 0xffffffff : (uint)c.Id);
            if (c.Class is int atom) { U16(0xffff); U16(atom); } else Str((string)c.Class);
            Str(c.Text);
            U16(0);
        }
        return b.ToArray();
    }

    [Fact]
    public void DialogTemplateExReadsTheFrameFontAndControls()
    {
        byte[] template = DialogEx("ﾊﾞｰｼﾞｮﾝ情報", 0x90c808d4, (0, 7, 215, 185), 9, "ＭＳ Ｐゴシック",
            new Control(2, 0x80, "OK", 0x50010300, 158, 164, 50, 14),
            new Control(0x404, 0x82, "SYSTEM3", 0x50000003, 14, 12, 20, 20),
            new Control(-1, 0x82, "ARCGameEngine", 0x50000000, 44, 104, 80, 10),
            new Control(0x400, 0x81, "", 0x50000800, 124, 104, 84, 13),
            new Control(0x417, "SysTabControl32", "", 0x50010040, 6, 7, 262, 204));

        DialogTemplate? dialog = DialogTemplateResource.Load(Pe(new Resource(PeResource.RtDialog, 2, template)), 2);

        Assert.NotNull(dialog);
        Assert.Equal("ﾊﾞｰｼﾞｮﾝ情報", dialog.Title);
        Assert.Equal((215, 185), (dialog.Width, dialog.Height));
        Assert.Equal((9, "ＭＳ Ｐゴシック"), (dialog.FontPointSize, dialog.FontFace));
        Assert.Equal(["Button", "Static", "Static", "Edit", "SysTabControl32"], dialog.Items.Select(i => i.ClassName));
        Assert.Equal([2, 0x404, -1, 0x400, 0x417], dialog.Items.Select(i => i.Id));
        Assert.Equal(DialogStyles.SsIcon, dialog.Item(0x404)!.TypeBits);
        Assert.Equal("SYSTEM3", dialog.Item(0x404)!.Text);
        Assert.True(dialog.Item(2)!.TabStop);
        Assert.False(dialog.Item(-1)!.TabStop);
        Assert.Equal((124, 104, 84, 13), (dialog.Item(0x400)!.X, dialog.Item(0x400)!.Y, dialog.Item(0x400)!.Width,
                                          dialog.Item(0x400)!.Height));
        Assert.Null(DialogTemplateResource.Load(Pe(new Resource(PeResource.RtDialog, 2, template)), 3));
    }

    [Fact]
    public void BaseUnitsConvertEachCoordinateAsMulDivRounds()
    {
        // ＭＳ Ｐゴシック 9 pt at 96 dpi: base units 7 x 12. AGERC's 終了 dialog (187 x 93) captured as a 327 x 140
        // client, its prompt (46,16,134,11) at (81,24) 235 x 17.
        var units = new DialogBaseUnits(7, 12);
        Assert.Equal((327, 140), (units.ToPixelsX(187), units.ToPixelsY(93)));
        Assert.Equal((81, 24, 235, 17), units.ToPixels(46, 16, 134, 11));
        Assert.Equal((376, 278), (units.ToPixelsX(215), units.ToPixelsY(185)));
        Assert.Equal((12, 125, 352, 2), units.ToPixels(7, 83, 201, 1));
        Assert.Equal(-2, DialogBaseUnits.MulDiv(-1, 7, 4));
        Assert.Equal(7, DialogBaseUnits.AverageCharWidth(c => char.IsUpper(c) ? 8 : 6));
    }

    // ---- icons ----

    private static byte[] IconDib(int size, Func<int, int, (byte R, byte G, byte B, bool Transparent)> pixel)
    {
        int stride = (size * 3 + 3) & ~3, maskStride = (size + 31) / 32 * 4;
        var dib = new byte[40 + stride * size + maskStride * size];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), size);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), size * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var (r, g, b, transparent) = pixel(x, y);
                int row = size - 1 - y;
                int o = 40 + row * stride + 3 * x;
                (dib[o], dib[o + 1], dib[o + 2]) = (b, g, r);
                if (transparent) dib[40 + stride * size + row * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        return dib;
    }

    private static byte[] Group(params (int Size, int Id)[] images)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes((ushort)0));
        b.AddRange(BitConverter.GetBytes((ushort)1));
        b.AddRange(BitConverter.GetBytes((ushort)images.Length));
        foreach ((int size, int id) in images)
        {
            b.Add((byte)size); b.Add((byte)size); b.Add(0); b.Add(0);
            b.AddRange(BitConverter.GetBytes((ushort)1));
            b.AddRange(BitConverter.GetBytes((ushort)24));
            b.AddRange(BitConverter.GetBytes(0u));
            b.AddRange(BitConverter.GetBytes((ushort)id));
        }
        return b.ToArray();
    }

    [Fact]
    public void IconPicksTheRequestedSizeAndAppliesTheMask()
    {
        byte[] pe = Pe(
            new Resource(PeResource.RtGroupIcon, "IDI_ICON1", Group((16, 6), (32, 5))),
            new Resource(PeResource.RtIcon, 6, IconDib(16, (_, _) => (0, 0, 255, false))),
            new Resource(PeResource.RtIcon, 5, IconDib(32, (x, y) => ((byte)x, (byte)y, 7, x == 0 && y == 0))));

        IconImage? icon = IconResource.Load(pe, "idi_icon1", 32);

        Assert.NotNull(icon);
        Assert.Equal((32, 32), (icon.Width, icon.Height));
        Assert.Equal([0, 0, 7, 0], icon.Rgba[..4]);                       // top-left: masked out
        int o = 4 * (3 * 32 + 5);
        Assert.Equal([5, 3, 7, 255], icon.Rgba[o..(o + 4)]);              // (5,3): top-down, RGBA
        Assert.Equal(16, IconResource.Load(pe, "IDI_ICON1", 16)!.Width);
        Assert.Null(IconResource.Load(pe, "IDI_ICON2"));
    }

    // ---- version resource ----

    private static byte[] VersionNode(string key, object? value, params byte[][] children)
    {
        var b = new List<byte>();
        void Pad() { while (b.Count % 4 != 0) b.Add(0); }
        b.AddRange(new byte[6]);
        b.AddRange(Encoding.Unicode.GetBytes(key + "\0"));
        Pad();
        int valueLength = 0, type = 1;
        if (value is string text) { b.AddRange(Encoding.Unicode.GetBytes(text + "\0")); valueLength = text.Length + 1; }
        else if (value is byte[] binary) { b.AddRange(binary); valueLength = binary.Length; type = 0; }
        foreach (byte[] child in children) { Pad(); b.AddRange(child); }
        byte[] node = b.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(node, (ushort)node.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(node.AsSpan(2), (ushort)valueLength);
        BinaryPrimitives.WriteUInt16LittleEndian(node.AsSpan(4), (ushort)type);
        return node;
    }

    [Fact]
    public void VersionResourceReadsAStringFromTheNamedTable()
    {
        byte[] info = VersionNode("VS_VERSION_INFO", new byte[52],
            VersionNode("StringFileInfo", null,
                VersionNode("040904b0", null, VersionNode("FileVersion", "9.99"))),
            VersionNode("StringFileInfo", null,
                VersionNode("041103a4", null, VersionNode("CompanyName", "ARC"), VersionNode("FileVersion", "4.24J"))));
        // A second StringFileInfo is unusual; a lookup takes the first, so put the Japanese table first instead.
        byte[] japaneseFirst = VersionNode("VS_VERSION_INFO", new byte[52],
            VersionNode("StringFileInfo", null,
                VersionNode("041103a4", null, VersionNode("CompanyName", "ARC"), VersionNode("FileVersion", "4.24J"))));
        byte[] pe = Pe(new Resource(PeResource.RtVersion, 1, japaneseFirst));

        Assert.Equal("4.24J", VersionResource.QueryString(pe, "041103a4", "FileVersion"));
        Assert.Equal("ARC", VersionResource.QueryString(pe, "041103a4", "companyname"));
        Assert.Null(VersionResource.QueryString(pe, "040904b0", "FileVersion"));
        Assert.Null(VersionResource.QueryString(pe, "041103a4", "ProductVersion"));
        Assert.Null(VersionResource.QueryString(Pe(new Resource(PeResource.RtVersion, 1, info)), "041103a4",
                                                "FileVersion"));
    }

    // ---- Himegari's own files ----

    [Fact]
    public void HimegarisAgercVersionDialogIconAndExeVersion()
    {
        string agerc = Path.Combine(Paths.GameDir, "AGERC.DLL"), exe = Path.Combine(Paths.GameDir, "AGE.EXE");
        if (!File.Exists(agerc) || !File.Exists(exe)) return;
        byte[] dll = File.ReadAllBytes(agerc);

        DialogTemplate? version = DialogTemplateResource.Load(dll, 2);
        Assert.NotNull(version);
        Assert.Equal("ﾊﾞｰｼﾞｮﾝ情報", version.Title);
        Assert.Equal((215, 185, 9, "ＭＳ Ｐゴシック"), (version.Width, version.Height, version.FontPointSize,
                                                       version.FontFace));
        Assert.Equal(14, version.Items.Count);
        Assert.Equal([0x400, 0x3ee, 0x3ef, 0x407, 0x3f0],
                     version.Items.Where(i => i.ClassName == "Edit").Select(i => i.Id));

        // Every control lands on the pixel rectangle AGE's dialog has (GetWindowRect of each child, 2026-11-05), in
        // template order. The SS_ICON static takes its 32×32 icon's size when it gets one, so only its origin counts.
        var units = new DialogBaseUnits(7, 12);
        (int, int, int, int)[] captured =
        [
            (277, 246, 88, 21), (25, 18, 32, 32), (77, 156, 140, 15), (217, 156, 147, 20), (77, 171, 287, 12),
            (12, 125, 352, 2), (42, 135, 100, 14), (77, 18, 270, 18), (77, 35, 147, 18), (77, 63, 287, 18),
            (77, 224, 287, 15), (77, 197, 287, 27), (77, 99, 287, 18), (12, 99, 65, 12),
        ];
        Assert.Equal((376, 278), (units.ToPixelsX(version.Width), units.ToPixelsY(version.Height)));
        for (int i = 0; i < captured.Length; i++)
        {
            DialogItem item = version.Items[i];
            (int x, int y, int w, int h) = units.ToPixels(item.X, item.Y, item.Width, item.Height);
            if (item.ClassName == "Static" && item.TypeBits == DialogStyles.SsIcon) (w, h) = (32, 32);
            Assert.Equal(captured[i], (x, y, w, h));
        }

        DialogTemplate? settings = DialogTemplateResource.Load(dll, 9);
        Assert.Equal("SysTabControl32", settings!.Item(0x417)!.ClassName);
        Assert.Equal(["ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ"], DialogTemplateResource.Load(dll, 100)!.Items
                         .Where(i => i.Text == "ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ").Select(i => i.Text));

        IconImage? icon = IconResource.Load(dll, "IDI_ICON1", 32);
        Assert.Equal((32, 32), (icon!.Width, icon.Height));
        Assert.Equal("4.24J", VersionResource.QueryString(File.ReadAllBytes(exe), "041103a4", "FileVersion"));
    }
}

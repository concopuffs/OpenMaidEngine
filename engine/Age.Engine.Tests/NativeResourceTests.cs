using System.Buffers.Binary;
using System.Text;
using Age.Engine.Frontend.NativeResources;
using Age.Engine.Sys4;

/// <summary>
/// The PE resource readers behind the port's AGERC dialogs (docs/engine-re.md, "Native menu bar", ﾍﾙﾌﾟ): dialog
/// templates and icons.
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

    private static Control[] ExitDialogControls(string prompt, string quit, string title, string ok, string cancel) =>
    [
        new(0x404, 0x82, "SYSTEM3", 0x50000003, 7, 20, 20, 20),
        new(-1, 0x82, prompt, 0x50020000, 46, 16, 134, 11),
        new(0x3ea, 0x80, quit, 0x50020009, 46, 34, 134, 10),
        new(0x3eb, 0x80, title, 0x50000009, 46, 48, 134, 12),
        new(1, 0x80, ok, 0x50010001, 74, 72, 50, 14),
        new(2, 0x80, cancel, 0x50010000, 130, 72, 50, 14),
    ];

    [Fact]
    public void ExitDialogTakesItsTextsFromTheTemplatesControlsById()
    {
        // A translated AGERC.DLL keeps dialog 3's ids and layout and changes only what the controls say.
        byte[] template = DialogEx("Quit", 0x80c808c0, (0, 0, 187, 93), 9, "ＭＳ Ｐゴシック",
            ExitDialogControls("Would you like to quit?", "Exit(&E)", "Return to title(&T)", "OK", "Cancel"));

        var dialog = Age.Engine.Frontend.NativeMenu.AgercExitDialog.Load(
            Pe(new Resource(PeResource.RtDialog, 3, template)));

        Assert.NotNull(dialog);
        Assert.Equal("Quit", dialog.Title);
        Assert.Equal("Would you like to quit?", dialog.Prompt.Text);
        Assert.Equal(("Exit(&E)", "Return to title(&T)"), (dialog.Quit.Text, dialog.ReturnToTitle.Text));
        Assert.Equal(DialogStyles.BsDefPushButton, dialog.Ok.TypeBits);
        // What the native driver's dialogs step reads: the statics' texts, then every Button control's.
        Assert.Equal("SYSTEM3\nWould you like to quit?", dialog.Text);
        Assert.Equal(["Exit(&E)", "Return to title(&T)", "OK", "Cancel"], dialog.Buttons);
        Assert.Equal(['E', 'T'], new[] { dialog.Quit.Text, dialog.ReturnToTitle.Text }
                         .Select(t => Age.Engine.Frontend.NativeMenu.AgercExitDialog.Mnemonic(t)!.Value));

        // A template without one of the controls the dialog procedure addresses is not the 終了 dialog.
        Control[] noTitle = ExitDialogControls("?", "Exit(&E)", "Return(&T)", "OK", "Cancel")
            .Where(c => c.Id != 0x3eb).ToArray();
        Assert.Null(Age.Engine.Frontend.NativeMenu.AgercExitDialog.Load(Pe(new Resource(PeResource.RtDialog, 3,
            DialogEx("Quit", 0x80c808c0, (0, 0, 187, 93), 9, "ＭＳ Ｐゴシック", noTitle)))));
        Assert.Null(Age.Engine.Frontend.NativeMenu.AgercExitDialog.Load(Pe(new Resource(PeResource.RtDialog, 2,
            template))));
    }

    [Theory]
    [InlineData("Exit(&E)", 'E')]
    [InlineData("タイトルに戻る(&t)", 'T')]
    [InlineData("A && B", null)]
    [InlineData("A && &B", 'B')]
    [InlineData("OK", null)]
    public void MnemonicIsTheCharacterAfterASingleAmpersand(string text, char? expected) =>
        Assert.Equal(expected, Age.Engine.Frontend.NativeMenu.AgercExitDialog.Mnemonic(text));

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

    // ---- Himegari's own files ----

    [Fact]
    public void HimegarisAgercVersionDialogAndIcon()
    {
        string agerc = Path.Combine(Paths.GameDir, "AGERC.DLL");
        if (!File.Exists(agerc)) return;
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
    }

    [Fact]
    public void HimegarisAgercSettingsDialogAndItsPagesLandOnNativesPixels()
    {
        string agerc = Path.Combine(Paths.GameDir, "AGERC.DLL");
        if (!File.Exists(agerc)) return;
        byte[] dll = File.ReadAllBytes(agerc);
        var units = new DialogBaseUnits(7, 12);

        // AGE's 設定 dialog (2026-11-05): its client, then every child window in creation order (EnumChildWindows,
        // rects in the dialog's client): dialog 9's own controls, then the DS_CONTROL pages 105 (動作環境),
        // 104 (その他), 102 (ｻｳﾝﾄﾞ, as set:NoSetMusic 3 picks) and 100 (ﾒｯｾｰｼﾞ), each followed by its controls. A
        // page sits at its template's x and y and has its template's size.
        (string Kind, int X, int Y, int W, int H)[] captured =
        [
            ("Button", 284, 326, 88, 21), ("Button", 382, 326, 88, 21), ("Button", 11, 326, 88, 21),
            ("Button", 103, 326, 88, 21), ("Tab", 11, 11, 459, 306), ("Page", 14, 33, 453, 279),
            ("Static", 32, 59, 124, 18), ("Edit", 163, 54, 287, 20), ("Static", 32, 128, 124, 18),
            ("Edit", 163, 125, 287, 20), ("Static", 32, 152, 124, 18), ("Edit", 163, 147, 287, 20),
            ("Static", 32, 80, 124, 18), ("Edit", 163, 77, 287, 20), ("Button", 23, 41, 438, 62),
            ("Button", 23, 111, 438, 107), ("Static", 32, 176, 124, 18), ("Static", 163, 176, 89, 18),
            ("Edit", 257, 173, 193, 20), ("Static", 163, 197, 89, 18), ("Edit", 257, 194, 193, 20),
            ("Button", 23, 225, 438, 62), ("Edit", 163, 240, 287, 20), ("Edit", 163, 263, 287, 20),
            ("Static", 32, 245, 124, 18), ("Static", 32, 264, 124, 18), ("Button", 312, 290, 149, 21),
            ("Page", 14, 33, 453, 267), ("Button", 23, 42, 438, 71), ("Button", 32, 74, 410, 15),
            ("Page", 14, 33, 453, 267), ("Static", 51, 68, 63, 17), ("Button", 26, 45, 429, 165),
            ("Button", 26, 221, 427, 53), ("Edit", 387, 240, 47, 20), ("Button", 172, 66, 63, 17),
            ("Button", 268, 66, 74, 17), ("Button", 364, 66, 65, 17), ("Trackbar", 58, 95, 39, 107),
            ("Trackbar", 182, 95, 42, 107), ("Trackbar", 277, 95, 39, 107), ("Trackbar", 371, 95, 39, 107),
            ("Button", 40, 242, 201, 15), ("Trackbar", 259, 228, 123, 44), ("Page", 14, 33, 453, 281),
            ("Button", 25, 104, 431, 53), ("Button", 25, 44, 431, 53), ("Button", 25, 164, 431, 81),
            ("Static", 117, 66, 33, 20), ("Static", 117, 128, 33, 21), ("Static", 340, 65, 25, 20),
            ("Static", 340, 128, 25, 20), ("Edit", 371, 63, 70, 20), ("Edit", 371, 125, 70, 20),
            ("Static", 117, 186, 33, 20), ("Static", 117, 213, 33, 20), ("Static", 340, 185, 25, 20),
            ("Static", 340, 218, 25, 20), ("Edit", 371, 183, 70, 20), ("Edit", 371, 215, 70, 20),
            ("Static", 37, 186, 81, 18), ("Static", 37, 212, 81, 18), ("Button", 25, 251, 431, 53),
            ("Trackbar", 154, 59, 179, 29), ("Trackbar", 154, 123, 179, 29), ("Trackbar", 154, 179, 179, 29),
            ("Trackbar", 154, 212, 179, 29), ("Button", 39, 273, 177, 15), ("Button", 257, 273, 177, 15),
        ];

        DialogTemplate settings = DialogTemplateResource.Load(dll, 9)!;
        Assert.Equal("設定", settings.Title);
        Assert.Equal((481, 357), (units.ToPixelsX(settings.Width), units.ToPixelsY(settings.Height)));
        static string Kind(DialogItem item) => item.ClassName switch
        {
            "SysTabControl32" => "Tab",
            "msctls_trackbar32" => "Trackbar",
            string name => name,
        };
        var built = settings.Items.Select(i => (Kind(i), units.ToPixels(i.X, i.Y, i.Width, i.Height))).ToList();
        foreach (int id in new[] { 105, 104, 102, 100 })
        {
            DialogTemplate page = DialogTemplateResource.Load(dll, id)!;
            (int px, int py) = (units.ToPixelsX(page.X), units.ToPixelsY(page.Y));
            built.Add(("Page", (px, py, units.ToPixelsX(page.Width), units.ToPixelsY(page.Height))));
            foreach (DialogItem item in page.Items)
            {
                (int x, int y, int w, int h) = units.ToPixels(item.X, item.Y, item.Width, item.Height);
                built.Add((Kind(item), (px + x, py + y, w, h)));
            }
        }
        Assert.Equal(captured.Select(c => (c.Kind, (c.X, c.Y, c.W, c.H))), built);

        // Trackbar styles: the ﾒｯｾｰｼﾞ page's are TBS_BOTTOM, the ｻｳﾝﾄﾞ volumes TBS_VERT | TBS_BOTH, its fade TBS_BOTH.
        DialogTemplate sound = DialogTemplateResource.Load(dll, 102)!;
        Assert.Equal(DialogStyles.TbsVert | DialogStyles.TbsBoth, sound.Item(0x414)!.Style & 0xf);
        Assert.Equal(DialogStyles.TbsBoth, sound.Item(0x418)!.Style & 0xf);
        Assert.Equal(0u, DialogTemplateResource.Load(dll, 100)!.Item(0x414)!.Style & 0xf);
    }

    // AGE's 終了 dialog as the native driver reads it (2026-11-04 Himegari, 2026-11-07 Kamidori): the title, the statics'
    // texts and the Button controls' texts, in a 327×140 client with the controls on the same pixels in both games.
    [Theory]
    [InlineData("Himegari_Game", "AGERC.DLL", "終了", "次の方法で終了しますか？",
                new[] { "プログラムを終了する(&E)", "タイトルに戻る(&T)", "OK", "ｷｬﾝｾﾙ" })]
    // The clean Kamidori install's AGERC.DLL is packed: its dialogs are not in the file (backlog.md).
    [InlineData("Kamidori_Translated", "agerc.dll", "Quit", "Would you like to quit?",
                new[] { "Exit(&E)", "Return to title(&T)", "OK", "Cancel" })]
    public void AGamesExitDialogIsItsOwnAgercDialog3(string gameDir, string dllName, string title, string prompt,
                                                      string[] buttons)
    {
        string agerc = Path.Combine(Paths.Workspace, gameDir, dllName);
        if (!File.Exists(agerc)) return;

        var dialog = Age.Engine.Frontend.NativeMenu.AgercExitDialog.Load(File.ReadAllBytes(agerc));

        Assert.NotNull(dialog);
        Assert.Equal(title, dialog.Title);
        Assert.Equal("SYSTEM3\n" + prompt, dialog.Text);
        Assert.Equal(buttons, dialog.Buttons);
        var units = new DialogBaseUnits(7, 12);
        Assert.Equal((327, 140), (units.ToPixelsX(dialog.Template.Width), units.ToPixelsY(dialog.Template.Height)));
        (int, int, int, int) Px(DialogItem item) => units.ToPixels(item.X, item.Y, item.Width, item.Height);
        Assert.Equal((12, 30), (Px(dialog.Icon).Item1, Px(dialog.Icon).Item2));   // SS_ICON: 32×32 once it has its icon
        Assert.Equal([(81, 24, 235, 17), (81, 51, 235, 15), (81, 72, 235, 18), (130, 108, 88, 21), (228, 108, 88, 21)],
                     new[] { dialog.Prompt, dialog.Quit, dialog.ReturnToTitle, dialog.Ok, dialog.Cancel }.Select(Px));
    }
}

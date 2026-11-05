using System.Buffers.Binary;
using System.Text;
using Age.Engine.Frontend.NativeMenu;
using Age.Engine.Sys4;

/// <summary>
/// AGERC.DLL's native menu bar (docs/engine-re.md, "Native menu bar"): the RT_MENU reader, the pruning AGERC applies
/// once from <c>set:Menu_*</c> (agerc_prune_menu_by_settings@0x10002ca0) and the refresh it runs whenever a menu
/// opens (agerc_refresh_system_menu_state@0x10002e10).
/// </summary>
public class NativeMenuTests
{
    private const ushort Grayed = 0x1, Checked = 0x8, Popup = 0x10, End = 0x80, Help = 0x4000;

    // ---- a classic MENU template and a PE that holds it ----

    private abstract record Entry(string Text, ushort Flags);
    private sealed record Item(int Id, string Text, ushort Flags = 0) : Entry(Text, Flags);
    private sealed record Sub(string Text, Entry[] Children, ushort Flags = 0) : Entry(Text, Flags);

    private static byte[] Template(params Entry[] bar)
    {
        var bytes = new List<byte> { 0, 0, 0, 0 };   // version 0, header size 0
        void Word(int value) { bytes.Add((byte)value); bytes.Add((byte)(value >> 8)); }
        void Items(Entry[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                Entry entry = items[i];
                ushort flags = (ushort)(entry.Flags | (i == items.Length - 1 ? End : 0) | (entry is Sub ? Popup : 0));
                Word(flags);
                if (entry is Item item) Word(item.Id);
                foreach (char c in entry.Text) Word(c);
                Word(0);
                if (entry is Sub sub) Items(sub.Children);
            }
        }
        Items(bar);
        return bytes.ToArray();
    }

    /// <summary>A 32-bit PE with one section, .rsrc at RVA 0x1000 (file offset 0x200), holding RT_MENU
    /// <paramref name="menuId"/> in language 0x411.</summary>
    private static byte[] PeWithMenu(int menuId, byte[] template)
    {
        const int peHeader = 0x40, optional = peHeader + 24, sectionTable = optional + 224, raw = 0x200, rva = 0x1000;
        const int dataEntry = 0x48, data = 0x58;
        int resourceSize = data + template.Length;
        var pe = new byte[raw + resourceSize];
        Span<byte> s = pe;
        s[0] = (byte)'M'; s[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(s[0x3c..], peHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(s[peHeader..], 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(peHeader + 4)..], 0x14c);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(peHeader + 6)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(s[(peHeader + 20)..], 224);
        BinaryPrimitives.WriteUInt16LittleEndian(s[optional..], 0x10b);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(optional + 96 + 2 * 8)..], rva);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(optional + 96 + 2 * 8 + 4)..], (uint)resourceSize);
        Encoding.ASCII.GetBytes(".rsrc").CopyTo(s[sectionTable..]);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(sectionTable + 8)..], (uint)resourceSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(sectionTable + 12)..], rva);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(sectionTable + 16)..], (uint)resourceSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[(sectionTable + 20)..], raw);
        Span<byte> r = s[raw..];
        static void Directory(Span<byte> r, int at, uint id, uint target)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(r[(at + 14)..], 1);   // one id entry
            BinaryPrimitives.WriteUInt32LittleEndian(r[(at + 16)..], id);
            BinaryPrimitives.WriteUInt32LittleEndian(r[(at + 20)..], target);
        }
        Directory(r, 0x00, 4, 0x80000000 | 0x18);              // RT_MENU
        Directory(r, 0x18, (uint)menuId, 0x80000000 | 0x30);   // the menu id
        Directory(r, 0x30, 0x411, dataEntry);                   // its language: a data entry
        BinaryPrimitives.WriteUInt32LittleEndian(r[dataEntry..], rva + data);
        BinaryPrimitives.WriteUInt32LittleEndian(r[(dataEntry + 4)..], (uint)template.Length);
        template.CopyTo(r[data..]);
        return pe;
    }

    [Fact]
    public void LoadReadsTheMenuTemplateFromAPe()
    {
        byte[] template = Template(
            new Sub("&Game", [new Item(0x101, "&Save"), new Item(0, ""), new Item(0x102, "E&xit\tESC", Grayed)]),
            new Sub("&Options", [new Item(0x201, "&Window", Checked), new Sub("&Speed", [new Item(0x202, "&Fast")])]),
            new Sub("&Help", [new Item(0x301, "&About")], Help));

        List<NativeMenuItem>? bar = NativeMenuResource.Load(PeWithMenu(110, template), 110);

        Assert.NotNull(bar);
        Assert.Equal(["&Game", "&Options", "&Help"], bar.Select(i => i.Text));
        Assert.All(bar, i => Assert.Equal(NativeMenuItemKind.Popup, i.Kind));
        Assert.Equal([false, false, true], bar.Select(i => i.RightJustified));
        List<NativeMenuItem> game = bar[0].Children;
        Assert.Equal([NativeMenuItemKind.Command, NativeMenuItemKind.Separator, NativeMenuItemKind.Command],
                     game.Select(i => i.Kind));
        Assert.Equal("E&xit", game[2].Label);
        Assert.Equal("ESC", game[2].Accelerator);
        Assert.Equal('X', game[2].Mnemonic);
        Assert.False(game[2].Enabled);
        Assert.True(bar[1].Children[0].Checked);
        Assert.Equal(0x202, NativeMenuItem.Find(bar, 0x202)?.Id);
        Assert.Null(NativeMenuResource.Load(PeWithMenu(110, template), 124));
        Assert.Null(NativeMenuResource.Load(new byte[0x400], 110));
    }

    // ---- menu 110 as Himegari's AGERC.DLL holds it ----

    private static List<NativeMenuItem> Menu110()
    {
        byte[] template = Template(
            new Sub("ｹﾞｰﾑ(&G)", [new Item(0x9c43, "ｾｰﾌﾞ(&S)..."), new Item(0x9c44, "ﾛｰﾄﾞ(&L)..."), new Item(0, "", Grayed),
                                 new Item(0x9c41, "終了(&X)\tESC")]),
            new Sub("画像の保存(&C)", [new Item(0x9c46, "表示画面を保存(&S)"), new Item(0x9c47, "画像の保存場所(&P)...")]),
            new Sub("ﾒｯｾｰｼﾞ(&M)", [new Item(0x9c63, "ﾒｯｾｰｼﾞｽｷｯﾌﾟ(&S)"), new Item(0x9c6f, "既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ(&R)"),
                                   new Item(0x9c64, AgercMenu.HideWindowLabel), new Item(0x9c65, "ｵｰﾄﾒｯｾｰｼﾞ(&A)"),
                                   new Item(0, "", Grayed), new Item(0x9c6c, "ﾒｯｾｰｼﾞの設定(&M)...")]),
            new Sub("設定(&O)", [
                new Sub("画面表示(&W)", [new Item(0x9c5a, "ﾌﾙｽｸﾘｰﾝ(&F)"), new Item(0x9c5b, "ｳｲﾝﾄﾞｳ(&W)")]),
                new Sub("ﾏｳｽの右ｸﾘｯｸ時の動作(&M)", [new Item(0x9c49, "ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの消去(&H)"),
                                                 new Item(0x9c4a, "ﾒｯｾｰｼﾞｽｷｯﾌﾟ(&S)"), new Item(0x9c4b, "ﾒﾆｭｰの表示(&M)")]),
                new Sub("文字表示(&A)", [new Item(0x9c5d, "ｱﾝﾁｴｲﾘｱｽを使用する(&U)"), new Item(0x9c5e, "ｱﾝﾁｴｲﾘｱｽを使用しない(&N)")]),
                new Sub("ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ(&T)", [new Item(0x9c4d, "ﾉｰｳｴｲﾄ(&W)"), new Item(0x9c4e, "速い(&F)"),
                                           new Item(0x9c4f, "普通(&N)"), new Item(0x9c50, "遅い(&S)")]),
                new Sub("ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過(&A)", [new Item(0x9c55, "なし(&N)"), new Item(0x9c52, "&25%"),
                                                 new Item(0x9c53, "&50%"), new Item(0x9c54, "&75%")]),
                new Item(0, "", Grayed),
                new Sub("BGMの設定(&B)", [new Item(0x9c56, "&ON"), new Item(0x9c57, "&OFF")]),
                new Sub("SEの設定(&E)", [new Item(0x9c58, "&ON"), new Item(0x9c59, "&OFF")]),
                new Item(0x9c68, "ｻｳﾝﾄﾞ設定(&S)..."),
                new Item(0x9c6e, "その他の設定(&X)...")]),
            new Sub("ﾍﾙﾌﾟ(&H)", [new Item(0x9c6d, "このｺﾝﾋﾟｭｰﾀの動作環境(&S)..."), new Item(0, "", Grayed),
                                 new Item(0x9c45, "ﾊﾞｰｼﾞｮﾝ情報(&V)...")], Help));
        return NativeMenuResource.ParseTemplate(template)!;
    }

    private static Func<string, int> Settings(params (string Key, string Value)[] pairs)
    {
        Sys4StartupSettings settings = Sys4StartupSettings.FromPairs(
            pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));
        return key => AgercMenu.Setting(settings, key);
    }

    private static string[] Labels(IEnumerable<NativeMenuItem> items)
        => items.Select(i => i.Kind == NativeMenuItemKind.Separator ? "-" : i.Label).ToArray();

    [Fact]
    public void SettingFallsBackToTheRegisteredDefaults()
    {
        Func<string, int> setting = Settings(("MENU_SAVE", "0"), ("MENU_MESSAGE", "1"));
        Assert.Equal(0, setting("MENU_SAVE"));
        Assert.Equal(1, setting("MENU_MESSAGE"));
        Func<string, int> defaults = Settings();
        Assert.Equal(1, defaults("MENU_SAVE"));
        Assert.Equal(1, defaults("MENU_RCLICK"));
        Assert.Equal(1, defaults("MENU_MESWINA"));
        Assert.Equal(1, defaults("MENU_SOUNDONOFF"));
        Assert.Equal(0, defaults("MENU_MESSAGE"));
        Assert.Equal(0, defaults("MENU_USEANTIFONT"));
    }

    [Fact]
    public void PruneWithTheDefaultsDropsTheMessageMenuAndTextDisplay()
    {
        List<NativeMenuItem> bar = Menu110();

        AgercMenu.Prune(bar, Settings());

        Assert.Equal(["ｹﾞｰﾑ(&G)", "画像の保存(&C)", "設定(&O)", "ﾍﾙﾌﾟ(&H)"], Labels(bar));
        Assert.Equal(["ｾｰﾌﾞ(&S)...", "ﾛｰﾄﾞ(&L)...", "-", "終了(&X)"], Labels(bar[0].Children));
        Assert.Equal(["画面表示(&W)", "ﾏｳｽの右ｸﾘｯｸ時の動作(&M)", "ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ(&T)", "ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過(&A)", "-",
                      "BGMの設定(&B)", "SEの設定(&E)", "ｻｳﾝﾄﾞ設定(&S)...", "その他の設定(&X)..."],
                     Labels(bar[2].Children));
    }

    [Fact]
    public void PruneFollowsAgercPositionsWhenEveryKeyIsZero()
    {
        // Himegari's SYS4INI: only Menu_Message is 1. The second ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過 test reads Menu_MesWinA again,
        // so its first removal takes ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ (never governed by Menu_MesSpeed) and the second takes 透過.
        List<NativeMenuItem> bar = Menu110();

        AgercMenu.Prune(bar, Settings(("MENU_SAVE", "0"), ("MENU_RCLICK", "0"), ("MENU_USEANTIFONT", "0"),
                                      ("MENU_MESWINA", "0"), ("MENU_MESSPEED", "1"), ("MENU_SOUNDONOFF", "0"),
                                      ("MENU_MESSAGE", "1")));

        Assert.Equal(["ｹﾞｰﾑ(&G)", "画像の保存(&C)", "ﾒｯｾｰｼﾞ(&M)", "設定(&O)", "ﾍﾙﾌﾟ(&H)"], Labels(bar));
        Assert.Equal(["終了(&X)"], Labels(bar[0].Children));
        Assert.Equal(["画面表示(&W)", "-", "ｻｳﾝﾄﾞ設定(&S)...", "その他の設定(&X)..."], Labels(bar[3].Children));
        Assert.Null(NativeMenuItem.Find(bar, AgercMenu.Save));
        Assert.Null(NativeMenuItem.Find(bar, AgercMenu.Load));
    }

    [Fact]
    public void HimegarisAgercMenuPrunesToTheNativeBar()
    {
        string path = Path.Combine(Paths.GameDir, "AGERC.DLL");
        if (!File.Exists(path) || !File.Exists(Paths.Sys4Ini)) return;
        List<NativeMenuItem>? bar = NativeMenuResource.Load(File.ReadAllBytes(path), AgercMenu.ResourceId);
        Assert.NotNull(bar);
        Assert.Equal(Labels(Menu110()), Labels(bar));   // the synthetic menu above is the real one
        Sys4StartupSettings settings = Sys4AssetCatalog.Load(Paths.Sys4Ini).StartupSettings;

        AgercMenu.Prune(bar, key => AgercMenu.Setting(settings, key));

        // As the windowed AGE shows it (PrintWindow captures, 2026-11-05).
        Assert.Equal(["ｹﾞｰﾑ(&G)", "画像の保存(&C)", "ﾒｯｾｰｼﾞ(&M)", "設定(&O)", "ﾍﾙﾌﾟ(&H)"], Labels(bar));
        Assert.True(bar[4].RightJustified);
        Assert.Equal(["終了(&X)"], Labels(bar[0].Children));
        Assert.Equal("ESC", bar[0].Children[0].Accelerator);
        Assert.Equal(6, bar[2].Children.Count);
        Assert.Equal(["画面表示(&W)", "-", "ｻｳﾝﾄﾞ設定(&S)...", "その他の設定(&X)..."], Labels(bar[3].Children));
        Assert.Equal(["このｺﾝﾋﾟｭｰﾀの動作環境(&S)...", "-", "ﾊﾞｰｼﾞｮﾝ情報(&V)..."], Labels(bar[4].Children));
    }

    [Fact]
    public void RefreshSetsChecksLabelsAndEnabledStates()
    {
        List<NativeMenuItem> bar = Menu110();
        var state = new AgercMenuState
        {
            Windowed = true, MessageSkip = true, ReadTextSkip = false, AutoMessage = true, HiddenWindow = false,
            TextWait = true, YieldHandler = true, SystemMenuEnabled = true, RMouseEvent = 1, UseAntiFont = 1,
            MessageSpeed = 5, MesWinAlpha = 8, SoundSe = 1, SoundMusic = -1,
        };

        AgercMenu.Refresh(bar, state);

        bool Check(int id) => NativeMenuItem.Find(bar, id)!.Checked;
        bool Enabled(int id) => NativeMenuItem.Find(bar, id)!.Enabled;
        Assert.True(Check(AgercMenu.Windowed));
        Assert.False(Check(AgercMenu.FullScreen));
        Assert.True(Check(AgercMenu.MessageSkip));
        Assert.False(Check(AgercMenu.ReadMessageSkip));
        Assert.True(Check(AgercMenu.AutoMessage));
        Assert.True(Check(AgercMenu.RClickSkip));
        Assert.False(Check(AgercMenu.RClickHideWindow));
        Assert.True(Check(AgercMenu.AntiFontOn));
        Assert.True(Check(AgercMenu.SpeedFast));      // 2..10 ms per glyph
        Assert.False(Check(AgercMenu.SpeedNoWait));
        Assert.True(Check(AgercMenu.Alpha50));
        Assert.True(Check(AgercMenu.SeOn));
        Assert.True(Check(AgercMenu.BgmOff));
        Assert.Equal(AgercMenu.HideWindowLabel, NativeMenuItem.Find(bar, AgercMenu.HideWindow)!.Text);
        Assert.True(Enabled(AgercMenu.HideWindow));

        // No yield handler: the hide command is grayed. While hidden it reads 表示する and stays enabled.
        AgercMenu.Refresh(bar, state with { YieldHandler = false });
        Assert.False(Enabled(AgercMenu.HideWindow));
        AgercMenu.Refresh(bar, state with { YieldHandler = false, TextWait = false, HiddenWindow = true });
        Assert.Equal(AgercMenu.ShowWindowLabel, NativeMenuItem.Find(bar, AgercMenu.HideWindow)!.Text);
        Assert.True(Enabled(AgercMenu.HideWindow));
        Assert.Equal('O', NativeMenuItem.Find(bar, AgercMenu.HideWindow)!.Mnemonic);

        // Op 0x142's field at 0 grays the seven system commands; the others keep their state.
        AgercMenu.Refresh(bar, state with { SystemMenuEnabled = false });
        foreach (int id in new[] { AgercMenu.MessageSkip, AgercMenu.HideWindow, AgercMenu.AutoMessage,
                                   AgercMenu.MessageSettings, AgercMenu.SoundSettings, AgercMenu.OtherSettings,
                                   AgercMenu.ReadMessageSkip })
            Assert.False(Enabled(id));
        Assert.True(Enabled(AgercMenu.Exit));
        AgercMenu.Refresh(bar, state);
        Assert.True(Enabled(AgercMenu.MessageSkip));
    }
}

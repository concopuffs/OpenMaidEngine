using System.Text.RegularExpressions;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>
/// The engine shell's model (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §4.2-§6): the command table,
/// menu 110's tree with AGERC's <c>Menu_*</c> pruning (agerc_prune_menu_by_settings@0x10002ca0) and refresh rules
/// (agerc_refresh_system_menu_state@0x10002e10), op 0x142's gate, the options and their apply order, the string tables
/// and the 終了 dialog's texts. Everything runs on synthetic SYS4INI keys; only the last two tests read the installed
/// games' SYS4INI.BIN.
/// </summary>
public class ShellModelTests
{
    private static readonly ShellStrings Ja = ShellStrings.Japanese;

    private static Func<string, int> Settings(params (string Key, string Value)[] pairs)
    {
        Sys4StartupSettings settings = Sys4StartupSettings.FromPairs(
            pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));
        return key => ShellMenu.Setting(settings, key);
    }

    /// <summary>Both shipped games' Menu_* keys: every key 0 except Menu_Message.</summary>
    private static readonly (string, string)[] ShippedMenuKeys =
    [
        ("MENU_SAVE", "0"), ("MENU_RCLICK", "0"), ("MENU_USEANTIFONT", "0"), ("MENU_MESWINA", "0"),
        ("MENU_MESSPEED", "0"), ("MENU_SOUNDONOFF", "0"), ("MENU_MESSAGE", "1"),
    ];

    private static string[] Labels(IEnumerable<MenuItem> items)
        => items.Select(i => i.Kind == MenuItemKind.Separator ? "-" : i.Text).ToArray();

    private static readonly VmMenuState TextWaitWithHandler = new(
        MessageSkip: false, AutoMessage: false, ReadTextSkip: false, HiddenWindow: false, TextWait: true,
        YieldHandler: true, SystemMenuEnabled: true);

    // ---- the tree ----

    [Fact]
    public void TheTreeIsMenu110WithAgercsText()
    {
        // As Himegari's AGERC.DLL holds menu 110 (RT_MENU dump, 2026-11-09).
        List<MenuItem> bar = ShellMenu.Build(Ja);

        Assert.Equal(["ｹﾞｰﾑ(&G)", "画像の保存(&C)", "ﾒｯｾｰｼﾞ(&M)", "設定(&O)", "ﾍﾙﾌﾟ(&H)"], Labels(bar));
        Assert.Equal([false, false, false, false, true], bar.Select(i => i.RightJustified));
        Assert.Equal(["ｾｰﾌﾞ(&S)...", "ﾛｰﾄﾞ(&L)...", "-", "終了(&X)\tESC"], Labels(bar[0].Children));
        Assert.Equal(["表示画面を保存(&S)", "画像の保存場所(&P)..."], Labels(bar[1].Children));
        Assert.Equal(["ﾒｯｾｰｼﾞｽｷｯﾌﾟ(&S)", "既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ(&R)", "ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを消す(&H)", "ｵｰﾄﾒｯｾｰｼﾞ(&A)", "-",
                      "ﾒｯｾｰｼﾞの設定(&M)..."], Labels(bar[2].Children));
        Assert.Equal(["画面表示(&W)", "ﾏｳｽの右ｸﾘｯｸ時の動作(&M)", "文字表示(&A)", "ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ(&T)", "ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過(&A)",
                      "-", "BGMの設定(&B)", "SEの設定(&E)", "ｻｳﾝﾄﾞ設定(&S)...", "その他の設定(&X)..."],
                     Labels(bar[3].Children));
        List<MenuItem> settings = bar[3].Children;
        Assert.Equal(["ﾌﾙｽｸﾘｰﾝ(&F)", "ｳｲﾝﾄﾞｳ(&W)"], Labels(settings[0].Children));
        Assert.Equal(["ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの消去(&H)", "ﾒｯｾｰｼﾞｽｷｯﾌﾟ(&S)", "ﾒﾆｭｰの表示(&M)"], Labels(settings[1].Children));
        Assert.Equal(["ｱﾝﾁｴｲﾘｱｽを使用する(&U)", "ｱﾝﾁｴｲﾘｱｽを使用しない(&N)"], Labels(settings[2].Children));
        Assert.Equal(["ﾉｰｳｴｲﾄ(&W)", "速い(&F)", "普通(&N)", "遅い(&S)"], Labels(settings[3].Children));
        Assert.Equal(["なし(&N)", "&25%", "&50%", "&75%"], Labels(settings[4].Children));
        Assert.Equal(["&ON", "&OFF"], Labels(settings[6].Children));
        Assert.Equal(["&ON", "&OFF"], Labels(settings[7].Children));
        Assert.Equal(["このｺﾝﾋﾟｭｰﾀの動作環境(&S)...", "-", "ﾊﾞｰｼﾞｮﾝ情報(&V)..."], Labels(bar[4].Children));

        // Menu 110's command ids, depth first.
        static IEnumerable<int> Ids(IEnumerable<MenuItem> items) => items.SelectMany(i =>
            i.Kind == MenuItemKind.Popup ? Ids(i.Children) : i.Kind == MenuItemKind.Command ? [i.Id] : []);
        Assert.Equal([0x9c43, 0x9c44, 0x9c41, 0x9c46, 0x9c47, 0x9c63, 0x9c6f, 0x9c64, 0x9c65, 0x9c6c, 0x9c5a, 0x9c5b,
                      0x9c49, 0x9c4a, 0x9c4b, 0x9c5d, 0x9c5e, 0x9c4d, 0x9c4e, 0x9c4f, 0x9c50, 0x9c55, 0x9c52, 0x9c53,
                      0x9c54, 0x9c56, 0x9c57, 0x9c58, 0x9c59, 0x9c68, 0x9c6e, 0x9c6d, 0x9c45], Ids(bar));
    }

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
        List<MenuItem> bar = new ShellMenu(Ja, Settings()).CreateBar();

        Assert.Equal(["ｹﾞｰﾑ(&G)", "画像の保存(&C)", "設定(&O)", "ﾍﾙﾌﾟ(&H)"], Labels(bar));
        Assert.Equal(["ｾｰﾌﾞ(&S)...", "ﾛｰﾄﾞ(&L)...", "-", "終了(&X)\tESC"], Labels(bar[0].Children));
        Assert.Equal(["画面表示(&W)", "ﾏｳｽの右ｸﾘｯｸ時の動作(&M)", "ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ(&T)", "ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過(&A)", "-",
                      "BGMの設定(&B)", "SEの設定(&E)", "ｻｳﾝﾄﾞ設定(&S)...", "その他の設定(&X)..."],
                     Labels(bar[2].Children));
    }

    [Fact]
    public void RelabelRetextsABarBuiltInAnotherLanguage()
    {
        static IEnumerable<string> All(IEnumerable<MenuItem> items) =>
            items.SelectMany(i => new[] { i.Text }.Concat(All(i.Children)));
        Func<string, int> setting = Settings(ShippedMenuKeys);
        List<MenuItem> bar = new ShellMenu(Ja, setting).CreateBar();
        var english = new ShellMenu(ShellStrings.Load("en"), setting);

        english.Relabel(bar);

        Assert.Equal(All(english.CreateBar()), All(bar));
        Assert.NotEqual(All(new ShellMenu(Ja, setting).CreateBar()), All(bar));
        // A bar another profile's keys pruned has another shape.
        Assert.Throws<InvalidOperationException>(() => english.Relabel(new ShellMenu(Ja, Settings()).CreateBar()));
    }

    [Fact]
    public void PruneFollowsAgercPositionsWhenEveryKeyButMessageIsZero()
    {
        // Both games' keys. The second ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過 test reads Menu_MesWinA again, so its first removal takes
        // ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ (never governed by Menu_MesSpeed, which is 1 here) and the second takes 透過.
        (string, string)[] keys = ShippedMenuKeys.Select(k => k.Item1 == "MENU_MESSPEED" ? (k.Item1, "1") : k)
            .ToArray();
        List<MenuItem> bar = new ShellMenu(Ja, Settings(keys)).CreateBar();

        Assert.Equal(["ｹﾞｰﾑ(&G)", "画像の保存(&C)", "ﾒｯｾｰｼﾞ(&M)", "設定(&O)", "ﾍﾙﾌﾟ(&H)"], Labels(bar));
        Assert.Equal(["終了(&X)\tESC"], Labels(bar[0].Children));
        Assert.Equal(6, bar[2].Children.Count);
        Assert.Equal(["画面表示(&W)", "-", "ｻｳﾝﾄﾞ設定(&S)...", "その他の設定(&X)..."], Labels(bar[3].Children));
        Assert.Equal(["このｺﾝﾋﾟｭｰﾀの動作環境(&S)...", "-", "ﾊﾞｰｼﾞｮﾝ情報(&V)..."], Labels(bar[4].Children));
        Assert.Null(MenuItem.Find(bar, 0x9c43));
        Assert.Null(MenuItem.Find(bar, 0x9c44));
    }

    [Fact]
    public void EachBarIsItsOwnCopy()
    {
        var menu = new ShellMenu(Ja, Settings(ShippedMenuKeys));
        List<MenuItem> bar = menu.CreateBar(), strip = menu.CreateBar();

        MenuItem.Find(bar, ShellCommands.MessageSkip)!.Checked = true;

        Assert.False(MenuItem.Find(strip, ShellCommands.MessageSkip)!.Checked);
    }

    // ---- refresh ----

    [Fact]
    public void RefreshSetsChecksLabelsAndEnabledStates()
    {
        var menu = new ShellMenu(Ja, Settings());
        List<MenuItem> bar = ShellMenu.Build(Ja);
        var state = new ShellState(Windowed: true, TextWaitWithHandler with { MessageSkip = true, AutoMessage = true });

        menu.Refresh(bar, state);

        MenuItem Item(int id) => MenuItem.Find(bar, id)!;
        Assert.True(Item(ShellCommands.Windowed).Checked);
        Assert.False(Item(ShellCommands.FullScreen).Checked);
        Assert.True(Item(ShellCommands.MessageSkip).Checked);
        Assert.False(Item(ShellCommands.ReadMessageSkip).Checked);
        Assert.True(Item(ShellCommands.AutoMessage).Checked);
        Assert.Equal("ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを消す(&H)", Item(ShellCommands.HideWindow).Text);
        Assert.True(Item(ShellCommands.HideWindow).Enabled);
        Assert.Equal("終了(&X)\tESC", Item(ShellCommands.Exit).Text);

        // The items the shell does not model stay grayed: ｾｰﾌﾞ, ﾛｰﾄﾞ and the radio groups.
        foreach (int id in new[] { 0x9c43, 0x9c44, 0x9c49, 0x9c4d, 0x9c52, 0x9c56, 0x9c58, 0x9c5d })
            Assert.False(Item(id).Enabled);

        // Without an op-0x7b handler the hide command is grayed. While hidden it reads 表示する and stays enabled.
        menu.Refresh(bar, state with { Vm = state.Vm with { YieldHandler = false } });
        Assert.False(Item(ShellCommands.HideWindow).Enabled);
        menu.Refresh(bar, state with { Vm = state.Vm with { YieldHandler = false, TextWait = false, HiddenWindow = true } });
        Assert.Equal("ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを表示する(&O)", Item(ShellCommands.HideWindow).Text);
        Assert.Equal('O', Item(ShellCommands.HideWindow).Mnemonic);
        Assert.True(Item(ShellCommands.HideWindow).Enabled);
        // No text wait: grayed again.
        menu.Refresh(bar, state with { Vm = state.Vm with { TextWait = false } });
        Assert.False(Item(ShellCommands.HideWindow).Enabled);

        menu.Refresh(bar, state with { Windowed = false });
        Assert.True(Item(ShellCommands.FullScreen).Checked);
        Assert.False(Item(ShellCommands.Windowed).Checked);
    }

    [Fact]
    public void Op142GraysTheNativeSetAndNothingElse()
    {
        var menu = new ShellMenu(Ja, Settings());
        List<MenuItem> bar = ShellMenu.Build(Ja);
        var state = new ShellState(Windowed: true, TextWaitWithHandler);
        int[] gated = [ShellCommands.MessageSkip, ShellCommands.HideWindow, ShellCommands.AutoMessage,
                       ShellCommands.MessageSettings, ShellCommands.SoundSettings, ShellCommands.OtherSettings,
                       ShellCommands.ReadMessageSkip];

        menu.Refresh(bar, state with { Vm = state.Vm with { SystemMenuEnabled = false } });

        foreach (ShellCommand command in ShellCommands.All)
            Assert.Equal(!gated.Contains(command.Id), MenuItem.Find(bar, command.Id)!.Enabled);
        Assert.Equal(gated.Order(), ShellCommands.All.Where(c => c.SystemMenuGated).Select(c => c.Id).Order());

        menu.Refresh(bar, state);
        Assert.All(ShellCommands.All, c => Assert.True(MenuItem.Find(bar, c.Id)!.Enabled, c.Name));
    }

    // ---- commands ----

    private sealed class Recorder : IShellHost, IVmMenuServices
    {
        public readonly List<string> Calls = [];
        public bool Windowed => true;
        public VmMenuState MenuState => default;
        public void RequestClose() => Calls.Add("close");
        public void OpenPanel(ShellSection section, bool diagnostics) =>
            Calls.Add($"panel {section}{(diagnostics ? " diagnostics" : "")}");
        public bool SwitchScreenMode(bool windowed, string source)
        {
            Calls.Add(windowed ? "window" : "fullscreen");
            return true;
        }
        public void SaveScreenshot() => Calls.Add("screenshot");
        public void ChooseImageFolder() => Calls.Add("folder");
        public void Quit() => Calls.Add("quit");
        public void ReturnToTitle() => Calls.Add("title");
        public void HoldVm(bool hold) => Calls.Add($"hold {hold}");
        public void RequestMenuMessageSkipToggle() => Calls.Add("skip");
        public void RequestMenuAutoMessageToggle() => Calls.Add("auto");
        public void RequestMenuReadMessageSkipToggle() => Calls.Add("read-skip");
        public bool RequestMenuHideWindow()
        {
            Calls.Add("hide");
            return true;
        }
    }

    [Theory]
    [InlineData("exit", "close")]
    [InlineData("save-screen", "screenshot")]
    [InlineData("image-folder", "folder")]
    [InlineData("skip", "skip")]
    [InlineData("read-skip", "read-skip")]
    [InlineData("hide", "hide")]
    [InlineData("auto", "auto")]
    [InlineData("message-settings", "panel Message")]
    [InlineData("fullscreen", "fullscreen")]
    [InlineData("windowed", "window")]
    [InlineData("sound-settings", "panel Sound")]
    [InlineData("other-settings", "panel Other")]
    [InlineData("system-info", "panel Info diagnostics")]
    [InlineData("version", "panel Info")]
    public void EachCommandCallsItsService(string name, string call)
    {
        var recorder = new Recorder();

        ShellCommands.Find(name)!.Run(recorder, recorder);

        Assert.Equal([call], recorder.Calls);
    }

    [Fact]
    public void CommandNamesAndIdsAreTheDriversMenuNames()
    {
        // tools/drive_core.py's MENU_COMMANDS: the same names on both targets (spec §8). Its ｾｰﾌﾞ and ﾛｰﾄﾞ are the
        // two menu-110 commands the shell does not model.
        string source = File.ReadAllText(Path.Combine(Paths.Repo, "tools", "drive_core.py"));
        string table = Regex.Match(source, @"^MENU_COMMANDS = \{(.*?)\}", RegexOptions.Singleline | RegexOptions.Multiline)
            .Groups[1].Value;
        Dictionary<string, int> driver = Regex.Matches(table, @"""([a-z-]+)"":\s*0x([0-9a-fA-F]+)")
            .ToDictionary(m => m.Groups[1].Value, m => Convert.ToInt32(m.Groups[2].Value, 16));

        Assert.Equal(ShellCommands.All.Count + 2, driver.Count);
        foreach (ShellCommand command in ShellCommands.All) Assert.Equal(command.Id, driver[command.Name]);
        Assert.Equal((0x9c43, 0x9c44), (driver["save"], driver["load"]));
        Assert.Same(ShellCommands.Find("read-skip"), ShellCommands.Find(ShellCommands.ReadMessageSkip));
        Assert.Null(ShellCommands.Find(0x9c43));
    }

    // ---- strings ----

    /// <summary>Every key the shell's model names.</summary>
    private static IEnumerable<string> ModelKeys()
    {
        static IEnumerable<string> TreeKeys(IEnumerable<ShellMenuNode> nodes) => nodes.SelectMany(node => node switch
        {
            ShellMenuPopup popup => TreeKeys(popup.Items).Prepend(popup.LabelKey),
            ShellMenuInert inert => [inert.LabelKey],
            _ => Array.Empty<string>(),
        });
        return TreeKeys(ShellMenu.Tree)
            .Concat(ShellCommands.All.SelectMany(c => new[] { c.LabelKey, c.AcceleratorKey }.OfType<string>()))
            .Append("command.hide.hidden")
            .Concat(["exit.title", "exit.prompt", "exit.quit", "exit.return-to-title", "dialog.ok", "dialog.cancel"])
            .Append("folder.prompt")
            .Concat(Enum.GetValues<ShellSection>().Select(s => $"section.{s.ToString().ToLowerInvariant()}"))
            .Concat(ShellOptions.All.Select(o => o.LabelKey))
            .Concat(ShellPanel.PortSwitches.Concat(ShellPanel.PortChoices).Select(ShellPanel.LabelKey))
            .Concat(ShellPanel.Sections.SelectMany(s => s.Groups).Select(g => g.TitleKey))
            .Concat(ShellPanel.TextKeys);
    }

    [Fact]
    public void BothTablesHoldEveryKeyTheModelNames()
    {
        ShellStrings en = ShellStrings.Load("en");
        Assert.Equal("en", en.Language);
        string[] keys = ModelKeys().Distinct().ToArray();

        Assert.All(keys, key => Assert.True(Ja.Keys.Contains(key), $"ja lacks {key}"));
        Assert.All(keys, key => Assert.True(en.Keys.Contains(key), $"en lacks {key}"));
        Assert.Equal(Ja.Keys.Order(), en.Keys.Order());   // no stray or missing English key
        Assert.Equal(Ja.Keys.Order(), keys.Order());     // no key the model does not use
    }

    [Fact]
    public void AMissingStringFallsBackToJapaneseThenToItsKey()
    {
        var partial = new ShellStrings("xx", new Dictionary<string, string> { ["exit.title"] = "Leave" }, Ja);

        Assert.Equal("Leave", partial["exit.title"]);
        Assert.Equal("次の方法で終了しますか？", partial["exit.prompt"]);
        Assert.Equal("no.such.key", partial["no.such.key"]);
        Assert.False(partial.TryGet("no.such.key", out _));
        Assert.Same(Ja, ShellStrings.Load("fr"));
        Assert.Same(Ja, ShellStrings.Load(null));
        Assert.Same(Ja, ShellStrings.Load(" JA "));
        Assert.Equal("&Skip", ShellStrings.Load("EN")["command.skip"]);
    }

    [Fact]
    public void TheEnglishBarKeepsItsMnemonicsDistinct()
    {
        // Win32's keyboard mode picks an item by its access key, so each level's keys must differ.
        static void Check(List<MenuItem> items)
        {
            char[] keys = items.Where(i => i.Kind != MenuItemKind.Separator).Select(i => i.Mnemonic).ToArray();
            Assert.DoesNotContain('\0', keys);
            Assert.Equal(keys.Length, keys.Distinct().Count());
            foreach (MenuItem popup in items.Where(i => i.Kind == MenuItemKind.Popup)) Check(popup.Children);
        }
        ShellStrings en = ShellStrings.Load("en");

        Check(new ShellMenu(en, Settings(ShippedMenuKeys)).CreateBar());
        Check(ShellMenu.Build(en));
    }

    [Theory]
    [InlineData("Exit(&E)", 'E')]
    [InlineData("タイトルに戻る(&t)", 'T')]
    [InlineData("A && B", null)]
    [InlineData("A && &B", 'B')]
    [InlineData("OK", null)]
    public void MnemonicIsTheCharacterAfterASingleAmpersand(string text, char? expected) =>
        Assert.Equal(expected, MenuItem.MnemonicOf(text));

    // ---- the 終了 dialog ----

    [Fact]
    public void TheExitDialogSpeaksAgercsJapaneseToTheDriver()
    {
        // Dialog 3 of Himegari's AGERC.DLL, as the native driver's dialogs step reads it (2026-11-04).
        ShellExitDialog ja = ShellExitDialog.Japanese;
        Assert.Equal("終了", ja.Title);
        Assert.Equal("SYSTEM3\n次の方法で終了しますか？", ja.DriveText);
        Assert.Equal(["プログラムを終了する(&E)", "タイトルに戻る(&T)", "OK", "ｷｬﾝｾﾙ"], ja.Buttons);

        // The radios' access keys are E and T in either language.
        ShellExitDialog en = ShellExitDialog.For(ShellStrings.Load("en"));
        Assert.Equal(['E', 'T'], new[] { en.Quit, en.ReturnToTitle }.Select(t => MenuItem.MnemonicOf(t)!.Value));
        Assert.Equal(['E', 'T'], new[] { ja.Quit, ja.ReturnToTitle }.Select(t => MenuItem.MnemonicOf(t)!.Value));
    }

    // ---- options ----

    [Fact]
    public void OptionsApplyInAgercSettingsApplysOrder()
    {
        // agerc_settings_apply@0x10005c20 (docs/engine-re.md, "設定").
        Assert.Equal(["message:MessageSpeed", "message:MesWinAlpha", "message:AutoMessageTime0",
                      "message:AutoMessageTime1", "message:UseAntiFont", "message:AdvanceMesOnWheel", "sound:Music",
                      "sound:SE", "sound:Voice", "sound:MusicFadeOnVoicePlaying", "sound:Volume0", "sound:Volume1",
                      "sound:Volume2", "sound:Volume3", "sound:MusicFadeOnVoicePlayingVolume",
                      "system:EffectSkipOnClick"],
                     ShellOptions.ApplyOrder.Select(o => $"{o.RegistrySection}:{o.RegistryKey}"));
        Assert.Equal(ShellOptions.All.Select(o => o.Name).Order(), ShellOptions.ApplyOrder.Select(o => o.Name).Order());
    }

    [Fact]
    public void OptionDomainsAreTheNativeControls()
    {
        ShellOption Option(string name) => ShellOptions.Find(name)!;

        Assert.Equal((0, 100, 1), (Option("message-speed").Min, Option("message-speed").Max, Option("message-speed").Step));
        Assert.Equal(16, Option("window-opacity").Max);
        // The Auto delays are trackbars at ms / 100: a hand-written 2050 shows as 2000, and the panel writes steps.
        Assert.Equal(2000, Option("auto-wait-text").Snap(2050));
        Assert.Equal(10000, Option("master-volume").Snap(12345));
        Assert.Equal(0, Option("bgm-volume").Snap(-1));
        Assert.True(Option("effect-skip").IsSwitch);
        Assert.Equal(ShellOptionApply.Live, Option("se-volume").Apply);
        Assert.Equal(ShellOptionApply.OnOk, Option("lower-bgm-level").Apply);
        Assert.Equal([ShellSection.Message, ShellSection.Sound, ShellSection.Other],
                     ShellOptions.All.Select(o => o.Section).Distinct());

        // 文字の縁をなめらかにする exists only where ENABLEANTIFONT binds message:UseAntiFont.
        Assert.False(Option("smooth-text").IsVisible(Sys4StartupSettings.Empty));
        Assert.True(Option("smooth-text").IsVisible(Sys4StartupSettings.FromPairs([new("ENABLEANTIFONT", "1")])));
        Assert.True(Option("wheel-advance").IsVisible(Sys4StartupSettings.Empty));
    }

    // ---- the installed games ----

    private static void AssertShippedBar(string sys4Ini)
    {
        Assert.True(File.Exists(sys4Ini), $"{sys4Ini} not found");
        Sys4StartupSettings settings = Sys4AssetCatalog.Load(sys4Ini).StartupSettings;

        List<MenuItem> bar = new ShellMenu(Ja, settings).CreateBar();

        // As the windowed AGE shows it (PrintWindow captures, 2026-11-05).
        Assert.Equal(["ｹﾞｰﾑ(&G)", "画像の保存(&C)", "ﾒｯｾｰｼﾞ(&M)", "設定(&O)", "ﾍﾙﾌﾟ(&H)"], Labels(bar));
        Assert.Equal(["終了(&X)\tESC"], Labels(bar[0].Children));
        Assert.Equal(6, bar[2].Children.Count);
        Assert.Equal(["画面表示(&W)", "-", "ｻｳﾝﾄﾞ設定(&S)...", "その他の設定(&X)..."], Labels(bar[3].Children));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void HimegarisSys4IniPrunesToTheNativeBar() => AssertShippedBar(Paths.Sys4Ini);

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void KamidorisSys4IniPrunesToTheNativeBar() =>
        AssertShippedBar(Path.Combine(Paths.KamidoriDir, "SYS4INI.BIN"));
}

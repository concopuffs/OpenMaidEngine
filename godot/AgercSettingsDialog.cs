using System;
using System.Text;
using Age.Engine.Frontend.NativeMenu;
using Age.Engine.Frontend.NativeResources;
using Age.Engine.Vm;
using Godot;

/// <summary>What the 設定 dialog reads and changes: AGE:IAGEService and the AGE:reg settings registry.</summary>
public interface IAgercSettingsHost
{
    /// <summary>message:MessageSpeed, MesWinAlpha and AutoMessageTime0/1 as the engine holds them.</summary>
    VmMessageSettings MessageSettings { get; }

    /// <summary>AGE:reg +4 (get int), with the registered default.</summary>
    int LoadInt(string section, string key, int defaultValue);

    /// <summary>AGE:reg +0xc (set int).</summary>
    void SaveInt(string section, string key, int value);

    /// <summary>IAGEService +0x68 (iage_service_set_antialias_enabled): write message:UseAntiFont, then apply it to
    /// the text manager (text_set_antialias_enabled@0x4137e0).</summary>
    void SetTextAntialias(bool enabled);

    /// <summary>IAGEService +0x40/+0x48/+0x50/+0x58: the live master, music, SE or voice level in basis points (10000
    /// while unconfigured).</summary>
    int Volume(int category);

    /// <summary>IAGEService +0x3c/+0x44/+0x4c/+0x54: apply a level live, without writing sound:VolumeN.</summary>
    void ApplyVolume(int category, int basisPoints);

    /// <summary>sound:VolumeN.</summary>
    void SaveVolume(int category, int basisPoints);

    /// <summary>sound:Music &gt;= 0, sound:SE != 0, sound:Voice != 0 for categories 1..3.</summary>
    bool RouteEnabled(int category);

    /// <summary>IAGEService +0x24/+0x28/+0x2c: enable or disable a route live; they write sound:Music/SE/Voice.</summary>
    void SetRouteEnabled(int category, bool enabled);

    /// <summary>The ﾒｯｾｰｼﾞ page's registry writes (MessageSpeed also through IAGEService +0x10), then
    /// CALLBACK_SETTING.BIN, which IAGEService +0x20 launches.</summary>
    void ApplyMessageSettings(VmMessageSettings settings);

    /// <summary>set:NoSetMusic (SYS4INI NOSETMUSIC).</summary>
    int NoSetMusic { get; }

    /// <summary>The install drive's path (GetCurrentDirectoryA: AGE runs from its folder).</summary>
    string InstallDirectory { get; }

    /// <summary>AGERC host command 2 with this dialog as the owner: the ﾊﾞｰｼﾞｮﾝ情報 dialog over it.</summary>
    void OpenVersionDialog(Win32TemplateDialog owner);
}

/// <summary>
/// AGERC's 設定 (dialog resource 9, agerc_settings_dialog_proc@0x10006400; docs/engine-re.md, "設定 dialog"), opened
/// by menu 0x9c6c, 0x9c68, 0x9c6e or 0x9c6d on tab 0..3. Its tab control holds four child dialogs, created in the
/// order 105 (動作環境), 104 (その他), 102 (ｻｳﾝﾄﾞ; 101 for a game without set:NoSetMusic) and 100 (ﾒｯｾｰｼﾞ); only the
/// selected tab's page shows.
/// <list type="bullet">
/// <item>ﾒｯｾｰｼﾞ (100) shows message:MessageSpeed, MesWinAlpha and AutoMessageTime0/1 on trackbars, with UseAntiFont and
/// AdvanceMesOnWheel. Nothing applies before OK.</item>
/// <item>ｻｳﾝﾄﾞ (102) changes the BGM, SE and voice routes and the four volumes live; Cancel puts the opening state
/// back. MusicFadeOnVoicePlaying and its volume wait for OK.</item>
/// <item>その他 (104) holds system:EffectSkipOnClick.</item>
/// <item>動作環境 (105) shows the OS, DirectX, drives and, 1.1 s after the dialog opens, CPU and memory. Its button
/// copies them to the clipboard.</item>
/// </list>
/// OK (agerc_settings_apply) writes every page's registry values and the routes, then launches
/// CALLBACK_SETTING.BIN. ﾊﾞｰｼﾞｮﾝ情報 opens that dialog over this one. The hidden ﾃﾞﾊﾞｯｸﾞ設定 (0x410, dialog 6) is never
/// shown.
/// </summary>
public partial class AgercSettingsDialog : Win32TemplateDialog
{
    public const int ResourceId = 9;
    public const int MessagePageId = 100, SoundPageId = 101, SoundPageNoSetMusicId = 102, OtherPageId = 104,
                     SystemInfoPageId = 105;

    private const int VersionButton = 0x3ff, TabId = 0x417;
    private const int Check0 = 1000, Check1 = 0x3e9, CheckVoiceDuck = 0x3ea, CheckVoice = 0x3ed;
    private const int Bar0 = 0x414, Bar1 = 0x415, Bar2 = 0x416, Bar3 = 0x417, Bar4 = 0x418;
    private const int Edit0 = 0x3ee, Edit1 = 0x3ef, Edit2 = 0x3f0, Edit3 = 0x3f1, Edit4 = 0x3f2, Edit5 = 0x3f3,
                      Edit6 = 0x3f4;
    private const int CopyButton = 0x419;
    private const int SystemInfoTicks = 11;   // WM_TIMER every 100 ms; the 11th fills CPU and memory

    private readonly IAgercSettingsHost _host;
    private readonly int _startTab;
    private readonly DialogPage _systemInfo, _other, _sound, _message;
    private readonly DialogPage[] _tabPages;
    private readonly int[] _savedVolumes = new int[4];
    private readonly bool[] _savedRoutes = new bool[4];
    private Timer? _systemInfoTimer;
    private int _systemInfoTick;

    public AgercSettingsDialog(DialogTemplate template, Func<int, DialogTemplate?> pageTemplate,
                               IAgercSettingsHost host, int startTab) : base(template)
    {
        _host = host;
        _startTab = Math.Clamp(startTab, 0, AgercSettingsText.Tabs.Length - 1);
        DialogPage Page(int id) => AddPage(pageTemplate(id)
                                           ?? throw new InvalidOperationException($"AGERC has no dialog {id}"));
        _systemInfo = Page(SystemInfoPageId);
        _other = Page(OtherPageId);
        _sound = Page(host.NoSetMusic == 0 ? SoundPageId : SoundPageNoSetMusicId);
        _message = Page(MessagePageId);
        _tabPages = [_message, _sound, _other, _systemInfo];
    }

    protected override void OnInitDialog()
    {
        if (Root.TabControl(TabId) is { } tabs)
        {
            foreach (string label in AgercSettingsText.Tabs) tabs.AddTab(label);
            tabs.Selected = _startTab;
            tabs.SelectionChanging += () => _tabPages[tabs.Selected].Visible = false;
            tabs.SelectionChanged += () => _tabPages[tabs.Selected].Visible = true;
        }
        InitSystemInfoPage();
        InitOtherPage();
        InitSoundPage();
        InitMessagePage();
        for (int i = 0; i < _tabPages.Length; i++) _tabPages[i].Visible = i == _startTab;
        if (Root.Control(0x410) is { } debug) debug.Visible = false;
    }

    private void InitMessagePage()
    {
        DialogPage page = _message;
        VmMessageSettings settings = _host.MessageSettings;
        SetBar(page, Bar0, 100, 100 - settings.MessageSpeed);
        SetBar(page, Bar1, 16, 16 - settings.MesWinAlpha);
        SetBar(page, Bar2, 100, (int)(settings.AutoMessageTime0 / 100));
        SetBar(page, Bar3, 100, (int)(settings.AutoMessageTime1 / 100));
        foreach (int bar in new[] { Bar0, Bar1, Bar2, Bar3 }) OnScroll(page, bar, Win32Trackbar.Top);
        page.SetChecked(Check0, _host.LoadInt("message", "UseAntiFont", 1) != 0);
        page.SetChecked(Check1, _host.LoadInt("message", "AdvanceMesOnWheel", 0) != 0);
    }

    private void InitSoundPage()
    {
        DialogPage page = _sound;
        for (int category = 0; category < 4; category++)
        {
            _savedVolumes[category] = _host.Volume(category);
            SetBar(page, Bar0 + category, 100, AgercSettingsText.VolumePosition(_savedVolumes[category]));
        }
        SetBar(page, Bar4, 100, _host.LoadInt("sound", "MusicFadeOnVoicePlayingVolume", 50));
        for (int category = 1; category < 4; category++)
        {
            _savedRoutes[category] = _host.RouteEnabled(category);
            page.SetChecked(RouteCheck(category), _savedRoutes[category]);
        }
        page.SetChecked(CheckVoiceDuck, _host.LoadInt("sound", "MusicFadeOnVoicePlaying", 1) != 0);
        OnScroll(page, Bar4, Win32Trackbar.Top);
    }

    private static int RouteCheck(int category) => category switch { 1 => Check0, 2 => Check1, _ => CheckVoice };

    private void InitOtherPage() =>
        _other.SetChecked(Check0, _host.LoadInt("system", "EffectSkipOnClick", 0) != 0);

    private void InitSystemInfoPage()
    {
        DialogPage page = _systemInfo;
        _systemInfoTick = 0;
        _systemInfoTimer = new Timer { WaitTime = 0.1, OneShot = false, Autostart = true };
        _systemInfoTimer.Timeout += OnSystemInfoTimer;
        AddChild(_systemInfoTimer);
        page.SetItemText(Edit0, AgercSystemInfo.OsVersion());
        page.SetItemText(Edit4, AgercSystemInfo.DirectXVersion());
        page.SetItemText(Edit5, AgercSystemInfo.DriveSpace(_host.InstallDirectory));
        page.SetItemText(Edit6, AgercSystemInfo.DriveSpace(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows)
                                                          is { Length: > 0 } windows ? windows : "/"));
    }

    // WM_TIMER: the first ten ticks count, the eleventh fills the CPU and memory lines and kills the timer.
    private void OnSystemInfoTimer()
    {
        if (_systemInfoTick < SystemInfoTicks - 1)
        {
            _systemInfoTick++;
            return;
        }
        _systemInfo.SetItemText(Edit1, AgercSystemInfo.Cpu());
        _systemInfo.SetItemText(Edit3, AgercSystemInfo.Memory());
        _systemInfoTimer?.Stop();
    }

    private static void SetBar(DialogPage page, int id, int max, int position)
    {
        if (page.Trackbar(id) is not { } bar) return;
        bar.SetRangeMax(max);
        bar.Value = position;
    }

    private static int BarPosition(DialogPage page, int id) => page.Trackbar(id)?.Value ?? 0;

    protected override void OnScroll(DialogPage page, int id, int code)
    {
        if (code >= Win32Trackbar.EndTrack) return;
        int position = BarPosition(page, id);
        if (page == _message)
        {
            switch (id)
            {
                case Bar0: page.SetItemText(Edit0, AgercSettingsText.MessageSpeed(position)); break;
                case Bar1: page.SetItemText(Edit1, AgercSettingsText.WindowOpacity(position)); break;
                case Bar2: page.SetItemText(Edit2, AgercSettingsText.AutoWait(position)); break;
                case Bar3: page.SetItemText(Edit3, AgercSettingsText.AutoWait(position)); break;
            }
        }
        else if (page == _sound)
        {
            if (id == Bar4) page.SetItemText(Edit0, AgercSettingsText.Percent(position));
            else if (id is >= Bar0 and <= Bar3)
                _host.ApplyVolume(id - Bar0, AgercSettingsText.VolumeBasisPoints(position));   // WM_VSCROLL
        }
    }

    protected override void OnPageCommand(DialogPage page, int id)
    {
        if (page == _sound)
        {
            int category = id switch { Check0 => 1, Check1 => 2, CheckVoice => 3, _ => 0 };
            if (category != 0) _host.SetRouteEnabled(category, page.IsChecked(id));
        }
        else if (page == _systemInfo && id == CopyButton)
        {
            var text = new StringBuilder();
            foreach (int edit in new[] { Edit0, Edit4, Edit1, Edit3, Edit5, Edit6 })
                text.Append(page.GetItemText(edit)).Append("\r\n");
            DisplayServer.ClipboardSet(text.ToString());
        }
    }

    protected override void OnCommand(int id)
    {
        switch (id)
        {
            case VersionButton:
                _host.OpenVersionDialog(this);
                break;
            case IdOk:
                Apply();
                EndDialog(1);
                break;
            case IdCancel:
                for (int category = 1; category < 4; category++)
                    _host.SetRouteEnabled(category, _savedRoutes[category]);
                for (int category = 0; category < 4; category++) _host.ApplyVolume(category, _savedVolumes[category]);
                EndDialog(0);
                break;
        }
    }

    // agerc_settings_apply, then IAGEService +0x20 launching CALLBACK_SETTING.BIN.
    private void Apply()
    {
        _host.SetTextAntialias(_message.IsChecked(Check0));
        _host.SaveInt("message", "AdvanceMesOnWheel", _message.IsChecked(Check1) ? 1 : 0);
        for (int category = 1; category < 4; category++)
            _host.SetRouteEnabled(category, _sound.IsChecked(RouteCheck(category)));
        _host.SaveInt("sound", "MusicFadeOnVoicePlaying", _sound.IsChecked(CheckVoiceDuck) ? 1 : 0);
        for (int category = 0; category < 4; category++)
            _host.SaveVolume(category, AgercSettingsText.VolumeBasisPoints(BarPosition(_sound, Bar0 + category)));
        _host.SaveInt("sound", "MusicFadeOnVoicePlayingVolume", BarPosition(_sound, Bar4));
        _host.SaveInt("system", "EffectSkipOnClick", _other.IsChecked(Check0) ? 1 : 0);
        // mk:mksh goes through AGE:reg +0x14, which never reaches SYS4REG.INI; page 104 has no control 0x3e9 anyway.
        _host.ApplyMessageSettings(new VmMessageSettings(
            100 - BarPosition(_message, Bar0), 16 - BarPosition(_message, Bar1),
            BarPosition(_message, Bar2) * 100L, BarPosition(_message, Bar3) * 100L));
    }
}

using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

namespace Ome.Engine.Frontend.Shell;

/// <summary>
/// What the options panel reads and changes in the engine (docs/superpowers/specs/2026-11-09-engine-shell-design.md,
/// §4.1): AGE:IAGEService on the audio mixer and the VM, and the AGE:reg settings registry that SYS4REG.INI stores, as
/// AGERC's 設定 reached them.
/// </summary>
public interface IShellSettingsHost
{
    /// <summary>message:MessageSpeed, MesWinAlpha and AutoMessageTime0/1 as the engine holds them.</summary>
    VmMessageSettings MessageSettings { get; }

    /// <summary>AGE:reg +4 (get int), with the registered default.</summary>
    int LoadInt(string section, string key, int defaultValue);

    /// <summary>AGE:reg +0xc (set int).</summary>
    void SaveInt(string section, string key, int value);

    /// <summary>IAGEService +0x68 (iage_service_set_antialias_enabled): write message:UseAntiFont, then apply it to
    /// the text manager.</summary>
    void SetTextAntialias(bool enabled);

    /// <summary>IAGEService +0x40/+0x48/+0x50/+0x58: the live master, music, SE or voice level in basis points.
    /// </summary>
    int Volume(int category);

    /// <summary>IAGEService +0x3c/+0x44/+0x4c/+0x54: apply a level live, without writing sound:VolumeN.</summary>
    void ApplyVolume(int category, int basisPoints);

    /// <summary>sound:VolumeN.</summary>
    void SaveVolume(int category, int basisPoints);

    /// <summary>sound:Music &gt;= 0, sound:SE != 0, sound:Voice != 0 for categories 1..3.</summary>
    bool RouteEnabled(int category);

    /// <summary>IAGEService +0x24/+0x28/+0x2c: enable or disable a route live; they write sound:Music/SE/Voice.</summary>
    void SetRouteEnabled(int category, bool enabled);

    /// <summary>The ﾒｯｾｰｼﾞ values' registry writes (MessageSpeed also through IAGEService +0x10), then
    /// CALLBACK_SETTING.BIN, which IAGEService +0x20 launches.</summary>
    void ApplyMessageSettings(VmMessageSettings settings);
}

/// <summary>
/// The options panel's edit of the native options (spec §5.3), with 設定's commit model
/// (agerc_settings_dialog_proc@0x10006400; docs/engine-re.md, "設定"): it opens on the engine's values, the ｻｳﾝﾄﾞ
/// volumes and routes apply live while it is open, <see cref="Commit"/> applies every value in
/// agerc_settings_apply@0x10005c20's order and launches CALLBACK_SETTING.BIN once, and <see cref="Cancel"/> puts the
/// live ｻｳﾝﾄﾞ values back. While op 0x142's gate is 0 (CONFIG runs) the draft is read-only: it changes nothing and
/// commits nothing, so the script-shared settings cannot change under the script, as natively 設定 cannot open then.
/// </summary>
public sealed class ShellOptionsDraft
{
    private readonly IShellSettingsHost _host;
    private readonly Dictionary<ShellOption, int> _opened = [];
    private readonly Dictionary<ShellOption, int> _values = [];
    private readonly int[] _openedVolumes = new int[4];
    private readonly bool[] _openedRoutes = new bool[4];

    /// <param name="settings">The profile's SYS4INI keys, which decide the visible options.</param>
    /// <param name="readOnly">True while op 0x142's gate is 0.</param>
    public ShellOptionsDraft(IShellSettingsHost host, Sys4StartupSettings settings, bool readOnly)
    {
        _host = host;
        ReadOnly = readOnly;
        Visible = ShellOptions.All.Where(o => o.IsVisible(settings)).ToArray();
        for (int category = 0; category < 4; category++) _openedVolumes[category] = host.Volume(category);
        for (int category = 1; category < 4; category++) _openedRoutes[category] = host.RouteEnabled(category);
        foreach (ShellOption option in ShellOptions.All)
        {
            int value = option.Snap(Read(option));
            _opened[option] = value;
            _values[option] = value;
        }
    }

    public bool ReadOnly { get; }

    /// <summary>The options the profile shows, in the panel's order.</summary>
    public IReadOnlyList<ShellOption> Visible { get; }

    public int Value(ShellOption option) => _values[option];

    public int Value(string name) => _values[ShellOptions.Find(name) ?? throw new ArgumentException(name)];

    public int Opened(ShellOption option) => _opened[option];

    /// <summary>Whether any value differs from the one the draft opened with.</summary>
    public bool HasChanges => _values.Any(v => v.Value != _opened[v.Key]);

    /// <summary>The volume categories and route categories (1 BGM, 2 SE, 3 voice) of the ｻｳﾝﾄﾞ options.</summary>
    private static int VolumeCategory(ShellOption option) => option.Name switch
    {
        "master-volume" => 0, "bgm-volume" => 1, "se-volume" => 2, "voice-volume" => 3, _ => -1,
    };

    private static int RouteCategory(ShellOption option) => option.Name switch
    {
        "bgm" => 1, "se" => 2, "voice" => 3, _ => -1,
    };

    // The page controls' opening states (agerc_settings_dialog_proc's WM_INITDIALOG for each page).
    private int Read(ShellOption option)
    {
        VmMessageSettings message = _host.MessageSettings;
        return option.Name switch
        {
            "message-speed" => message.MessageSpeed,
            "window-opacity" => message.MesWinAlpha,
            "auto-wait-voice" => (int)Math.Clamp(message.AutoMessageTime0, 0, int.MaxValue),
            "auto-wait-text" => (int)Math.Clamp(message.AutoMessageTime1, 0, int.MaxValue),
            "smooth-text" => _host.LoadInt("message", Sys4RegIniStore.UseAntiFontKey, 1) != 0 ? 1 : 0,
            "wheel-advance" => _host.LoadInt("message", Sys4RegIniStore.AdvanceMesOnWheelKey, 0) != 0 ? 1 : 0,
            "lower-bgm-on-voice" => _host.LoadInt("sound", "MusicFadeOnVoicePlaying", 1) != 0 ? 1 : 0,
            "lower-bgm-level" => _host.LoadInt("sound", "MusicFadeOnVoicePlayingVolume", 50),
            "effect-skip" => _host.LoadInt("system", "EffectSkipOnClick", 0) != 0 ? 1 : 0,
            _ when VolumeCategory(option) is >= 0 and int volume => _openedVolumes[volume],
            _ when RouteCategory(option) is > 0 and int route => _openedRoutes[route] ? 1 : 0,
            _ => throw new InvalidOperationException($"no engine value for option {option.Name}"),
        };
    }

    /// <summary>Edit one option: the value snaps to the option's domain, and a live option applies at once. False when
    /// the draft is read-only or the profile does not show the option.</summary>
    public bool Set(ShellOption option, int value)
    {
        if (ReadOnly || !Visible.Contains(option)) return false;
        int snapped = option.Snap(value);
        _values[option] = snapped;
        if (option.Apply != ShellOptionApply.Live) return true;
        if (VolumeCategory(option) is >= 0 and int volume) _host.ApplyVolume(volume, snapped);   // WM_VSCROLL
        else if (RouteCategory(option) is > 0 and int route) _host.SetRouteEnabled(route, snapped != 0);   // BN_CLICKED
        return true;
    }

    public bool Set(string name, int value) => Set(ShellOptions.Find(name) ?? throw new ArgumentException(name), value);

    /// <summary>設定's OK: every value in agerc_settings_apply's order, the ﾒｯｾｰｼﾞ values last in one
    /// <see cref="IShellSettingsHost.ApplyMessageSettings"/>, which launches CALLBACK_SETTING.BIN once. Nothing applies
    /// while read-only. False when nothing was applied.</summary>
    public bool Commit()
    {
        if (ReadOnly) return false;
        foreach (ShellOption option in ShellOptions.ApplyOrder)
        {
            int value = _values[option];
            switch (option.Name)
            {
                case "message-speed" or "window-opacity" or "auto-wait-voice" or "auto-wait-text":
                    break;   // with ApplyMessageSettings below
                case "smooth-text":
                    if (Visible.Contains(option)) _host.SetTextAntialias(value != 0);
                    break;
                case "wheel-advance":
                    _host.SaveInt("message", Sys4RegIniStore.AdvanceMesOnWheelKey, value);
                    break;
                case "lower-bgm-on-voice":
                    _host.SaveInt("sound", "MusicFadeOnVoicePlaying", value);
                    break;
                case "lower-bgm-level":
                    _host.SaveInt("sound", "MusicFadeOnVoicePlayingVolume", value);
                    break;
                case "effect-skip":
                    _host.SaveInt("system", "EffectSkipOnClick", value);
                    break;
                default:
                    if (RouteCategory(option) is > 0 and int route) _host.SetRouteEnabled(route, value != 0);
                    else if (VolumeCategory(option) is >= 0 and int volume) _host.SaveVolume(volume, value);
                    break;
            }
        }
        // mk:mksh goes through AGE:reg +0x14, which never reaches SYS4REG.INI; page 104 has no control for it anyway.
        _host.ApplyMessageSettings(new VmMessageSettings(
            Value("message-speed"), Value("window-opacity"), Value("auto-wait-voice"), Value("auto-wait-text")));
        foreach (ShellOption option in ShellOptions.All) _opened[option] = _values[option];
        return true;
    }

    /// <summary>設定's ｷｬﾝｾﾙ: the routes, then the volumes, back to their opening live states; nothing else had
    /// applied.</summary>
    public void Cancel()
    {
        if (ReadOnly) return;
        for (int category = 1; category < 4; category++) _host.SetRouteEnabled(category, _openedRoutes[category]);
        for (int category = 0; category < 4; category++) _host.ApplyVolume(category, _openedVolumes[category]);
        foreach (ShellOption option in ShellOptions.All) _values[option] = _opened[option];
    }
}

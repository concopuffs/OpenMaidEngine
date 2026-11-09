using Ome.Engine.Persistence;
using Ome.Engine.Sys4;

namespace Ome.Engine.Frontend.Shell;

/// <summary>When an option's edit reaches the engine: with the panel's 決定 (as 設定's OK), or at once while the panel
/// is open, its ｷｬﾝｾﾙ restoring the value it opened with (the ｻｳﾝﾄﾞ page's volumes and routes).</summary>
public enum ShellOptionApply { OnOk, Live }

/// <summary>
/// One option of the options panel (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §5.3): a registry key
/// with the domain and step of the native 設定 control, so the panel can show every value native can write and writes
/// nothing native could not.
/// </summary>
/// <param name="Name">The stable name (the driver's <c>option</c> name).</param>
/// <param name="Section">The panel section that shows it.</param>
/// <param name="RegistrySection">The <c>SYS4REG.INI</c> section.</param>
/// <param name="RegistryKey">The <c>SYS4REG.INI</c> key. The sound routes' values go through the audio mixer's
/// encoding (sound:Music is -1 when off).</param>
/// <param name="Min">The smallest value native's control can write.</param>
/// <param name="Max">The largest.</param>
/// <param name="Step">The step between values (a trackbar position's worth).</param>
/// <param name="Apply">When an edit applies.</param>
public sealed record ShellOption(string Name, ShellSection Section, string RegistrySection, string RegistryKey, int Min,
                                 int Max, int Step, ShellOptionApply Apply)
{
    public string LabelKey => $"option.{Name}";

    public bool IsSwitch => Min == 0 && Max == 1;

    /// <summary>Whether the profile shows the option; by default every profile does.</summary>
    public Func<Sys4StartupSettings, bool> VisibleRule { get; init; } = _ => true;

    public bool IsVisible(Sys4StartupSettings settings) => VisibleRule(settings);

    /// <summary>The value <paramref name="value"/> lands on: clamped to the domain, then down to a step.</summary>
    public int Snap(int value)
    {
        int clamped = Math.Clamp(value, Min, Max);
        return Min + (clamped - Min) / Step * Step;
    }
}

/// <summary>The options panel's option set (spec §5.3), and the order 設定's OK applies it in
/// (agerc_settings_apply@0x10005c20; docs/engine-re.md, "設定").</summary>
public static class ShellOptions
{
    private const ShellOptionApply OnOk = ShellOptionApply.OnOk, Live = ShellOptionApply.Live;

    /// <summary>Every option, in the panel's order within each section.</summary>
    public static IReadOnlyList<ShellOption> All { get; } =
    [
        // ﾒｯｾｰｼﾞ (page 100). MessageSpeed is ms per glyph, trackbar 0x414 at 100 - v; MesWinAlpha 0..16, 0x415 at
        // 16 - v; the Auto delays are trackbars 0x416/0x417 at ms / 100.
        new("message-speed", ShellSection.Message, "message", Sys4RegIniStore.MessageSpeedKey, 0, 100, 1, OnOk),
        new("window-opacity", ShellSection.Message, "message", Sys4RegIniStore.MesWinAlphaKey, 0, 16, 1, OnOk),
        new("auto-wait-voice", ShellSection.Message, "message", Sys4RegIniStore.AutoMessageTime0Key, 0, 10000, 100,
            OnOk),
        new("auto-wait-text", ShellSection.Message, "message", Sys4RegIniStore.AutoMessageTime1Key, 0, 10000, 100,
            OnOk),
        // Check 1000, which exists only where ENABLEANTIFONT binds the key.
        new("smooth-text", ShellSection.Message, "message", Sys4RegIniStore.UseAntiFontKey, 0, 1, 1, OnOk)
        {
            VisibleRule = Sys4RegIniStore.BindsAntiFont,
        },
        new("wheel-advance", ShellSection.Message, "message", Sys4RegIniStore.AdvanceMesOnWheelKey, 0, 1, 1, OnOk),
        // ｻｳﾝﾄﾞ (page 102, as set:NoSetMusic nonzero picks it). The volumes are basis points, trackbars at
        // 100 - v / 100.
        new("master-volume", ShellSection.Sound, "sound", "Volume0", 0, 10000, 100, Live),
        new("bgm-volume", ShellSection.Sound, "sound", "Volume1", 0, 10000, 100, Live),
        new("se-volume", ShellSection.Sound, "sound", "Volume2", 0, 10000, 100, Live),
        new("voice-volume", ShellSection.Sound, "sound", "Volume3", 0, 10000, 100, Live),
        new("bgm", ShellSection.Sound, "sound", "Music", 0, 1, 1, Live),
        new("se", ShellSection.Sound, "sound", "SE", 0, 1, 1, Live),
        new("voice", ShellSection.Sound, "sound", "Voice", 0, 1, 1, Live),
        new("lower-bgm-on-voice", ShellSection.Sound, "sound", "MusicFadeOnVoicePlaying", 0, 1, 1, OnOk),
        new("lower-bgm-level", ShellSection.Sound, "sound", "MusicFadeOnVoicePlayingVolume", 0, 100, 1, OnOk),
        // その他 (page 104).
        new("effect-skip", ShellSection.Other, "system", "EffectSkipOnClick", 0, 1, 1, OnOk),
    ];

    /// <summary>agerc_settings_apply's order: MessageSpeed, MesWinAlpha, the Auto delays, UseAntiFont,
    /// AdvanceMesOnWheel, the three route enables, MusicFadeOnVoicePlaying, the four volumes, then
    /// MusicFadeOnVoicePlayingVolume and EffectSkipOnClick. (Its last write, mk:mksh, comes from a control page 104
    /// lacks, so it is always 0 and not persisted; the panel leaves it out.)</summary>
    public static IReadOnlyList<ShellOption> ApplyOrder { get; } =
    [
        .. new[]
        {
            "message-speed", "window-opacity", "auto-wait-voice", "auto-wait-text", "smooth-text", "wheel-advance",
            "bgm", "se", "voice", "lower-bgm-on-voice", "master-volume", "bgm-volume", "se-volume", "voice-volume",
            "lower-bgm-level", "effect-skip",
        }.Select(name => Find(name)!),
    ];

    public static ShellOption? Find(string name) =>
        All.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
}

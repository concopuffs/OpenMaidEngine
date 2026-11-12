namespace Ome.Engine.Persistence;

/// <summary>One SYS4REG.INI key an engine build's settings loader binds, with its registered default.</summary>
public readonly record struct Sys4RegIniKey(string Section, string Key, string Default);

/// <summary>
/// The SYS4REG.INI keys an engine build binds, in its loader's order (docs/superpowers/specs/2026-11-12-engine-layering-design.md
/// §7.3: a table). <see cref="Sys4RegIniStore"/> loads every one (defaults for missing ones) and writes them in this
/// order. The builds that differ are declared in <c>Vm/EngineBuilds.cs</c>.
/// </summary>
public sealed record SettingsRules
{
    private SettingsRules(IReadOnlyList<Sys4RegIniKey> boundKeys) => BoundKeys = boundKeys;

    /// <summary>The keys in load and write order.</summary>
    public IReadOnlyList<Sys4RegIniKey> BoundKeys { get; }

    /// <summary>
    /// The keys engine_settings_load_sys4reg_ini@0x46b6e0 binds (4.24J), in its order, with
    /// engine_settings_register_defaults@0x46be30's values. A SYS4INI trailer pair named like the key overrides the
    /// default (Himegari: FULLSCREENBIT=32, FONT), sound:Music follows NOSETMUSIC (<see cref="Sys4RegIniStore.ForPath"/>),
    /// and message:UseAntiFont is bound only when ENABLEANTIFONT is set.
    /// </summary>
    public static SettingsRules LoaderKeys { get; } = new(
    [
        new("display", "UseDirectDrawEmuration", "0"), new("display", "UseWideSurface", "0"),
        new("display", "ScreenMode", "0"), new("display", "FullScreenBit", "16"), new("display", "UseIVideoWindow", "1"),
        new("display", "DeviceType", "0"), new("display", "VertexProcessing", "0"), new("display", "PresentInterval", "0"),
        new("display", "TextureBit", "32"), new("display", "PixelShader", "-1"), new("display", "ForceScreen", "0"),
        new("display", "VirtualFullScreen", "0"), new("display", "AspectMode", "0"), new("display", "FullScreenWidth", "0"),
        new("display", "FullScreenHeight", "0"), new("display", "LimitAero", "0"),
        new("sound", "Sound", "1"), new("sound", "Music", "0"), new("sound", "SE", "1"), new("sound", "Voice", "1"),
        new("sound", "Movie", "1"), new("sound", "UseDirectSound", "1"), new("sound", "UseDirectSoundEmuration", "1"),
        new("sound", "DirectSoundCooperativeLevel", "1"), new("sound", "Volume0", "-1"), new("sound", "Volume1", "-1"),
        new("sound", "Volume2", "-1"), new("sound", "Volume3", "-1"), new("sound", "Volume4", "-1"),
        new("sound", "MusicFadeOnVoicePlaying", "1"), new("sound", "MusicFadeOnVoicePlayingVolume", "50"),
        new("message", "Font", ""), new("message", "RMouseEvent", "0"), new("message", "MessageSpeed", "5"),
        new("message", "MesWinAlpha", "8"), new("message", "UseAntiFont", "1"), new("message", "AutoMessageTime0", "500"),
        new("message", "AutoMessageTime1", "2000"), new("message", "ReadTextSkip", "0"),
        new("message", "AdvanceMesOnWheel", "0"),
        new("system", "SaveBMPPath", ""), new("system", "UseMMX", "1"), new("system", "EffectSkipOnClick", "0"),
        new("system", "OutErrorLog", "0"),
    ]);

    /// <summary>These keys with <paramref name="added"/> bound right after the key named <paramref name="after"/>.</summary>
    public SettingsRules BindingAfter(string after, Sys4RegIniKey added)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(after);
        int index = BoundKeys.ToList().FindIndex(key => key.Key == after);
        if (index < 0) throw new ArgumentException($"no bound key named '{after}'", nameof(after));
        if (BoundKeys.Any(key => key.Section == added.Section && key.Key == added.Key))
            throw new ArgumentException($"{added.Section}:{added.Key} is already bound", nameof(added));
        var keys = BoundKeys.ToList();
        keys.Insert(index + 1, added);
        return new SettingsRules(keys);
    }
}

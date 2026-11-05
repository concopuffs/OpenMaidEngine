using System.Globalization;
using Age.Engine.Sys4;

namespace Age.Engine.Frontend.NativeMenu;

/// <summary>What AGERC's menu refresh reads from the engine (AGE:IAGEService and AGE:reg) when a menu opens.</summary>
public readonly record struct AgercMenuState
{
    /// <summary>IAGEService +0x08: the game runs in a window (checks ｳｲﾝﾄﾞｳ, else ﾌﾙｽｸﾘｰﾝ).</summary>
    public bool Windowed { get; init; }
    /// <summary>+0x04: persistent all-message Skip (ctx+0x550fc).</summary>
    public bool MessageSkip { get; init; }
    /// <summary>message:ReadTextSkip.</summary>
    public bool ReadTextSkip { get; init; }
    /// <summary>+0x0c: Auto mode (ctx+0x55104).</summary>
    public bool AutoMessage { get; init; }
    /// <summary>+0xb8: the hidden-window wait (run-state 0x4000000).</summary>
    public bool HiddenWindow { get; init; }
    /// <summary>+0xbc: an ADV text wait polls (run-state 0x80000000).</summary>
    public bool TextWait { get; init; }
    /// <summary>+0xc0: the op-0x95 choice-table wait (run-state 0x10000000).</summary>
    public bool ChoiceWait { get; init; }
    /// <summary>+0x80: the running frame has an op-0x7b handler A (coroutine_yield_a != -1).</summary>
    public bool YieldHandler { get; init; }
    /// <summary>+0xb4: op 0x142's system-menu-enabled field is nonzero.</summary>
    public bool SystemMenuEnabled { get; init; }
    public int RMouseEvent { get; init; }
    public int UseAntiFont { get; init; }
    public int MessageSpeed { get; init; }
    public int MesWinAlpha { get; init; }
    /// <summary>sound:SE (1 = on).</summary>
    public int SoundSe { get; init; }
    /// <summary>sound:Music (-1 = off).</summary>
    public int SoundMusic { get; init; }
}

/// <summary>
/// AGERC.DLL's native menu bar: resource 110, pruned once by <c>set:Menu_*</c> when AGE attaches it
/// (agerc_prune_menu_by_settings@0x10002ca0) and refreshed whenever a menu opens
/// (agerc_refresh_system_menu_state@0x10002e10). Decoded from Himegari's AGERC.DLL (docs/engine-re.md,
/// "Native menu bar").
/// </summary>
public static class AgercMenu
{
    public const int ResourceId = 110;

    public const int Exit = 0x9c41;
    public const int Save = 0x9c43, Load = 0x9c44, VersionInfo = 0x9c45, SaveScreen = 0x9c46, ImageFolder = 0x9c47;
    public const int RClickHideWindow = 0x9c49, RClickSkip = 0x9c4a, RClickMenu = 0x9c4b;
    public const int SpeedNoWait = 0x9c4d, SpeedFast = 0x9c4e, SpeedNormal = 0x9c4f, SpeedSlow = 0x9c50;
    public const int Alpha25 = 0x9c52, Alpha50 = 0x9c53, Alpha75 = 0x9c54, AlphaNone = 0x9c55;
    public const int BgmOn = 0x9c56, BgmOff = 0x9c57, SeOn = 0x9c58, SeOff = 0x9c59;
    public const int FullScreen = 0x9c5a, Windowed = 0x9c5b, AntiFontOn = 0x9c5d, AntiFontOff = 0x9c5e;
    public const int MessageSkip = 0x9c63, HideWindow = 0x9c64, AutoMessage = 0x9c65;
    public const int SoundSettings = 0x9c68, MessageSettings = 0x9c6c, SystemInfo = 0x9c6d, OtherSettings = 0x9c6e;
    public const int ReadMessageSkip = 0x9c6f;

    /// <summary>The 0x9c64 label while the window shows (AGERC 0x100114c4) and while it is hidden (0x100114dc).</summary>
    public const string HideWindowLabel = "ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを消す(&H)";
    public const string ShowWindowLabel = "ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを表示する(&O)";

    // The ids the refresh enables and, with op 0x142's field at 0, grays.
    private static readonly int[] SystemMenuIds =
        [MessageSkip, HideWindow, AutoMessage, MessageSettings, SoundSettings, OtherSettings, ReadMessageSkip];

    /// <summary>The native registered value of a <c>set:Menu_*</c> key (engine_settings_register_defaults), unless
    /// SYS4INI sets it: Save, MesWinA, RClick, MesSpeed and SoundONOFF default to 1, Message and UseAntiFont to 0.</summary>
    public static int Setting(Sys4StartupSettings settings, string key)
    {
        int fallback = key.ToUpperInvariant() switch
        {
            "MENU_MESSAGE" or "MENU_USEANTIFONT" => 0,
            _ => 1,
        };
        return int.TryParse(settings.GetValueOrDefault(key)?.Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int value) ? value : fallback;
    }

    /// <summary>agerc_prune_menu_by_settings: remove bar items whose <c>set:Menu_*</c> key is 0, by the same
    /// positions and ids AGERC removes. The second ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過 test reads Menu_MesWinA again, so
    /// Menu_MesSpeed is never read.</summary>
    public static void Prune(List<NativeMenuItem> bar, Func<string, int> setting)
    {
        if (setting("MENU_SAVE") == 0)
        {
            RemoveAt(SubMenu(bar, 0), 2);
            RemoveCommand(bar, Save);
            RemoveCommand(bar, Load);
        }
        int rclick = setting("MENU_RCLICK");
        if (rclick == 0) RemoveAt(SubMenu(bar, 3), 1);
        int kept = rclick != 0 ? 1 : 0;
        int position = kept + 1;
        if (setting("MENU_USEANTIFONT") == 0)
        {
            RemoveAt(SubMenu(bar, 3), position);
            position = kept;
        }
        int next = position + 1;
        if (setting("MENU_MESWINA") == 0)
        {
            RemoveAt(SubMenu(bar, 3), next);
            next = position;
        }
        position = next + 1;
        if (setting("MENU_MESWINA") == 0)
        {
            RemoveAt(SubMenu(bar, 3), position);
            position = next;
        }
        int sound = position + 2;
        if (setting("MENU_SOUNDONOFF") == 0)
        {
            RemoveAt(SubMenu(bar, 3), sound);
            sound = position + 1;
        }
        if (setting("MENU_SOUNDONOFF") == 0)
        {
            sound++;
            RemoveAt(SubMenu(bar, 3), sound);
        }
        if (setting("MENU_MESSAGE") == 0) RemoveAt(bar, 2);
    }

    /// <summary>agerc_refresh_system_menu_state: set the check marks, the hide-window label and the enabled
    /// states from the engine's current state.</summary>
    public static void Refresh(List<NativeMenuItem> bar, AgercMenuState state)
    {
        foreach (int id in SystemMenuIds) Enable(bar, id, true);
        Check(bar, FullScreen, !state.Windowed);
        Check(bar, Windowed, state.Windowed);
        Check(bar, MessageSkip, state.MessageSkip);
        Check(bar, ReadMessageSkip, state.ReadTextSkip);
        Check(bar, AutoMessage, state.AutoMessage);
        if (!state.HiddenWindow)
        {
            SetText(bar, HideWindow, HideWindowLabel);
            Enable(bar, HideWindow, (state.TextWait || state.ChoiceWait) && state.YieldHandler);
        }
        else
        {
            SetText(bar, HideWindow, ShowWindowLabel);
        }
        switch (state.RMouseEvent)
        {
            case 0: Check(bar, RClickHideWindow, true); Check(bar, RClickSkip, false); Check(bar, RClickMenu, false); break;
            case 1: Check(bar, RClickHideWindow, false); Check(bar, RClickSkip, true); Check(bar, RClickMenu, false); break;
            case 2: Check(bar, RClickHideWindow, false); Check(bar, RClickSkip, false); Check(bar, RClickMenu, true); break;
        }
        Check(bar, AntiFontOn, state.UseAntiFont == 1);
        Check(bar, AntiFontOff, state.UseAntiFont != 1);
        int speed = state.MessageSpeed;
        Check(bar, SpeedNoWait, speed == 0);
        Check(bar, SpeedFast, speed is > 1 and < 11);
        Check(bar, SpeedNormal, speed is > 10 and < 21);
        Check(bar, SpeedSlow, speed > 20);
        Check(bar, Alpha25, state.MesWinAlpha == 4);
        Check(bar, Alpha50, state.MesWinAlpha == 8);
        Check(bar, Alpha75, state.MesWinAlpha == 12);
        Check(bar, AlphaNone, state.MesWinAlpha == 0);
        Check(bar, SeOn, state.SoundSe == 1);
        Check(bar, SeOff, state.SoundSe != 1);
        Check(bar, BgmOn, state.SoundMusic != -1);
        Check(bar, BgmOff, state.SoundMusic == -1);
        if (!state.SystemMenuEnabled)
            foreach (int id in SystemMenuIds) Enable(bar, id, false);
    }

    private static List<NativeMenuItem> SubMenu(List<NativeMenuItem> bar, int position)
        => position < bar.Count && bar[position].Kind == NativeMenuItemKind.Popup ? bar[position].Children : [];

    // RemoveMenu(MF_BYPOSITION) on a position past the end fails and changes nothing.
    private static void RemoveAt(List<NativeMenuItem> items, int position)
    {
        if (position >= 0 && position < items.Count) items.RemoveAt(position);
    }

    // RemoveMenu(MF_BYCOMMAND) removes the first match, searching popups depth first.
    private static bool RemoveCommand(List<NativeMenuItem> items, int id)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == NativeMenuItemKind.Command && items[i].Id == id)
            {
                items.RemoveAt(i);
                return true;
            }
            if (items[i].Kind == NativeMenuItemKind.Popup && RemoveCommand(items[i].Children, id)) return true;
        }
        return false;
    }

    private static void Check(List<NativeMenuItem> bar, int id, bool value)
    {
        if (NativeMenuItem.Find(bar, id) is { } item) item.Checked = value;
    }

    private static void Enable(List<NativeMenuItem> bar, int id, bool value)
    {
        if (NativeMenuItem.Find(bar, id) is { } item) item.Enabled = value;
    }

    private static void SetText(List<NativeMenuItem> bar, int id, string text)
    {
        if (NativeMenuItem.Find(bar, id) is { } item) item.Text = text;
    }
}

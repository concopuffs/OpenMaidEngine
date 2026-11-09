using System.Globalization;
using Ome.Engine.Sys4;

namespace Ome.Engine.Frontend.Shell;

/// <summary>A node of the shell's menu tree.</summary>
public abstract record ShellMenuNode;

/// <summary>A popup and its items. <paramref name="RightJustified"/> is MF_HELP: ﾍﾙﾌﾟ sits at the bar's right end.
/// </summary>
public sealed record ShellMenuPopup(string LabelKey, IReadOnlyList<ShellMenuNode> Items, bool RightJustified = false)
    : ShellMenuNode;

/// <summary>An item that runs a shell command (<see cref="ShellCommands"/>).</summary>
public sealed record ShellMenuCommand(string Name) : ShellMenuNode;

/// <summary>An item menu 110 holds that the shell does not model: ｾｰﾌﾞ, ﾛｰﾄﾞ and the 設定 radio groups. Both games prune
/// them; a profile that keeps one gets it drawn and grayed.</summary>
public sealed record ShellMenuInert(int Id, string LabelKey) : ShellMenuNode;

public sealed record ShellMenuSeparator : ShellMenuNode
{
    public static ShellMenuSeparator Instance { get; } = new();
}

/// <summary>
/// The shell's menu bar (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §5.2): menu 110's tree, order and
/// separators as our own data, pruned once by <c>set:Menu_*</c> as agerc_prune_menu_by_settings@0x10002ca0 prunes the
/// attached menu, and refreshed from a <see cref="ShellState"/> whenever a popup opens, as
/// agerc_refresh_system_menu_state@0x10002e10 runs on WM_INITMENU (docs/engine-re.md, "Native menu bar"). The windowed
/// bar and the fullscreen strip each hold their own copy (<see cref="CreateBar"/>), as native's do.
/// </summary>
public sealed class ShellMenu
{
    private static ShellMenuCommand C(string name) => new(name);
    private static ShellMenuInert I(int id, string key) => new(id, key);
    private static ShellMenuSeparator Sep => ShellMenuSeparator.Instance;

    /// <summary>Menu 110 as Himegari's AGERC.DLL holds it (dumped 2026-11-09; Kamidori's has the same tree and ids).
    /// </summary>
    public static IReadOnlyList<ShellMenuNode> Tree { get; } =
    [
        new ShellMenuPopup("menu.game", [I(0x9c43, "menu.game.save"), I(0x9c44, "menu.game.load"), Sep, C("exit")]),
        new ShellMenuPopup("menu.image", [C("save-screen"), C("image-folder")]),
        new ShellMenuPopup("menu.message",
            [C("skip"), C("read-skip"), C("hide"), C("auto"), Sep, C("message-settings")]),
        new ShellMenuPopup("menu.settings",
        [
            new ShellMenuPopup("menu.screen", [C("fullscreen"), C("windowed")]),
            new ShellMenuPopup("menu.right-click", [I(0x9c49, "menu.right-click.hide"),
                I(0x9c4a, "menu.right-click.skip"), I(0x9c4b, "menu.right-click.menu")]),
            new ShellMenuPopup("menu.text-display",
                [I(0x9c5d, "menu.text-display.antialias"), I(0x9c5e, "menu.text-display.no-antialias")]),
            new ShellMenuPopup("menu.message-speed", [I(0x9c4d, "menu.message-speed.no-wait"),
                I(0x9c4e, "menu.message-speed.fast"), I(0x9c4f, "menu.message-speed.normal"),
                I(0x9c50, "menu.message-speed.slow")]),
            new ShellMenuPopup("menu.window-opacity", [I(0x9c55, "menu.window-opacity.none"),
                I(0x9c52, "menu.window-opacity.25"), I(0x9c53, "menu.window-opacity.50"),
                I(0x9c54, "menu.window-opacity.75")]),
            Sep,
            new ShellMenuPopup("menu.bgm", [I(0x9c56, "menu.bgm.on"), I(0x9c57, "menu.bgm.off")]),
            new ShellMenuPopup("menu.se", [I(0x9c58, "menu.se.on"), I(0x9c59, "menu.se.off")]),
            C("sound-settings"),
            C("other-settings"),
        ]),
        new ShellMenuPopup("menu.help", [C("system-info"), Sep, C("version")], RightJustified: true),
    ];

    private readonly ShellStrings _strings;
    private readonly Func<string, int> _setting;

    public ShellMenu(ShellStrings strings, Sys4StartupSettings settings)
        : this(strings, key => Setting(settings, key)) { }

    public ShellMenu(ShellStrings strings, Func<string, int> setting)
    {
        _strings = strings;
        _setting = setting;
    }

    public ShellStrings Strings => _strings;

    /// <summary>A fresh copy of the bar: the tree in this language, pruned by the profile's <c>Menu_*</c> keys.</summary>
    public List<MenuItem> CreateBar()
    {
        List<MenuItem> bar = Build(_strings);
        Prune(bar, _setting);
        return bar;
    }

    /// <summary>The whole tree as menu items, before any pruning.</summary>
    public static List<MenuItem> Build(ShellStrings strings) => Build(Tree, strings);

    private static List<MenuItem> Build(IEnumerable<ShellMenuNode> nodes, ShellStrings strings)
    {
        var items = new List<MenuItem>();
        foreach (ShellMenuNode node in nodes)
        {
            items.Add(node switch
            {
                ShellMenuPopup popup => new MenuItem(MenuItemKind.Popup, 0, strings[popup.LabelKey],
                                                     rightJustified: popup.RightJustified,
                                                     children: Build(popup.Items, strings)),
                ShellMenuCommand command => CommandItem(command, strings),
                ShellMenuInert inert => new MenuItem(MenuItemKind.Command, inert.Id, strings[inert.LabelKey],
                                                     grayed: true),
                _ => new MenuItem(MenuItemKind.Separator, 0, "", grayed: true),
            });
        }
        return items;
    }

    private static MenuItem CommandItem(ShellMenuCommand node, ShellStrings strings)
    {
        ShellCommand command = ShellCommands.Find(node.Name)
                               ?? throw new InvalidOperationException($"the menu tree names no command {node.Name}");
        return new MenuItem(MenuItemKind.Command, command.Id, command.Text(strings, default));
    }

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
    public static void Prune(List<MenuItem> bar, Func<string, int> setting)
    {
        if (setting("MENU_SAVE") == 0)
        {
            RemoveAt(SubMenu(bar, 0), 2);
            RemoveCommand(bar, 0x9c43);
            RemoveCommand(bar, 0x9c44);
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

    /// <summary>agerc_refresh_system_menu_state: each command's enabled state, check mark and label from
    /// <paramref name="state"/>. The items the shell does not model stay grayed.</summary>
    public void Refresh(IEnumerable<MenuItem> items, ShellState state)
    {
        foreach (MenuItem item in items)
        {
            switch (item.Kind)
            {
                case MenuItemKind.Popup:
                    Refresh(item.Children, state);
                    break;
                case MenuItemKind.Command when ShellCommands.Find(item.Id) is { } command:
                    item.Enabled = command.IsEnabled(state);
                    item.Checked = command.IsChecked(state);
                    item.Text = command.Text(_strings, state);
                    break;
                case MenuItemKind.Command:
                    item.Enabled = false;
                    break;
            }
        }
    }

    private static List<MenuItem> SubMenu(List<MenuItem> bar, int position)
        => position < bar.Count && bar[position].Kind == MenuItemKind.Popup ? bar[position].Children : [];

    // RemoveMenu(MF_BYPOSITION) on a position past the end fails and changes nothing.
    private static void RemoveAt(List<MenuItem> items, int position)
    {
        if (position >= 0 && position < items.Count) items.RemoveAt(position);
    }

    // RemoveMenu(MF_BYCOMMAND) removes the first match, searching popups depth first.
    private static bool RemoveCommand(List<MenuItem> items, int id)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == MenuItemKind.Command && items[i].Id == id)
            {
                items.RemoveAt(i);
                return true;
            }
            if (items[i].Kind == MenuItemKind.Popup && RemoveCommand(items[i].Children, id)) return true;
        }
        return false;
    }
}

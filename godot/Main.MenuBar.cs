using System;
using System.Collections.Generic;
using Ome.Engine.Frontend.Control;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Sys4;
using Godot;

/// <summary>
/// The windowed game's menu bar: the engine shell's menu (<see cref="ShellMenu"/>; menu 110's tree as AGERC held it,
/// docs/engine-re.md "Native menu bar"), pruned by <c>set:Menu_*</c> as AGERC prunes it when AGE attaches it, and drawn
/// above the game (<see cref="NativeMenuBar"/>), so the viewport is the game plus <see cref="_menuBarHeight"/> rows and
/// the game's client area starts below them. Every game has it; a run with <c>--no-menu-bar</c> does not.
/// <para>Opening a menu starts native's modal menu loop: the game holds at its next opcode boundary, its frame
/// freezes, and the shell refreshes the items from its state snapshot (native's WM_INITMENU). A chosen command then
/// runs as WM_COMMAND does, through the shell's command table (<see cref="ShellCommands"/>): 終了, 画像の保存
/// (Main.Screenshot.cs), the ﾒｯｾｰｼﾞ toggles, ﾌﾙｽｸﾘｰﾝ and ｳｲﾝﾄﾞｳ (Main.ScreenMode.cs), and the 設定 and ﾍﾙﾌﾟ sections
/// (the options panel, Main.OptionsPanel.cs). The items the shell does not model stay grayed. Fullscreen detaches the bar, and the top-edge
/// strip carries its own copy of the menu.</para>
/// <para>The keyboard reaches the bar as DefWindowProc lets it reach AGE's: an Alt or F10 tap selects it, and the
/// window menu (<see cref="WindowSystemMenu"/>) follows its last item. Op 0x10f's bit 0x200 keeps F10 out.</para>
/// </summary>
public partial class Main
{
    private NativeMenuBar? _menuBar;
    private CanvasLayer? _menuBarLayer;
    private ShellMenu? _shellMenu;
    private List<MenuItem>? _menuItems;
    private int _menuBarHeight;

    /// <summary>Build and prune the menu before the window is sized, since the bar adds rows above the game.</summary>
    private void LoadMenuBar(string gameRoot, Sys4StartupSettings settings, string[] userArgs)
    {
        _menuItems = null;
        _menuBarHeight = 0;
        _shellMenu = new ShellMenu(_shellStrings, settings);
        if (Array.IndexOf(userArgs, "--no-menu-bar") >= 0) return;
        _menuItems = _shellMenu.CreateBar();
        _menuBarHeight = NativeMenuBar.BarHeight;
        GD.Print($"[menu] shell menu: {string.Join(" ", _menuItems.ConvertAll(i => i.Label))}");
    }

    /// <summary>Create the bar once the scene exists. Main itself moves below it.</summary>
    private void CreateMenuBar()
    {
        if (_menuItems == null) return;
        OffsetTop = _menuBarHeight;
        _menuBarLayer = new CanvasLayer { Layer = 101 };   // above the frozen frame (99) and the exit dim (100)
        _menuBar = new NativeMenuBar(_menuItems)
        {
            SystemMenu = WindowSystemMenu.Create(fullscreen: false),
            IgnoreF10 = () => _vm != null && (_vm.SystemKeyDisableMask & 0x200) != 0,   // WM_SYSKEYDOWN VK_F10: return 0
        };
        _menuBar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _menuBar.Opened += OnMenuOpened;
        _menuBar.Closed += OnMenuClosed;
        _menuBar.CommandChosen += ExecuteMenuCommand;
        _menuBarLayer.AddChild(_menuBar);
        AddChild(_menuBarLayer);
    }

    /// <summary>The bar takes input first unless a modal dialog owns the window (a message box or dialog disables
    /// AGE's window, its menu bar included). Fullscreen has no bar.</summary>
    private bool RouteMenuBarInput(InputEvent e)
    {
        if (_menuBar == null || _vm == null || _host == null || !_windowed) return false;
        if (_exitConfirmation != null || _scriptErrorDialog != null || _fullwidthTextEditor != null
            || _screenshotFolderDialog != null || _optionsPanel != null || _debugSceneLauncher?.Visible == true)
            return false;
        return _menuBar.HandleInput(e);
    }

    private void SetMenuBarActive(bool active)
    {
        if (_menuBar == null) return;
        if (!active && _menuBar.IsOpen) GD.Print("[menu] the window lost focus: the menu loop ends");
        _menuBar.SetActive(active);
    }

    private void OnMenuOpened()
    {
        GD.Print("[menu] menu loop starts");
        Shell.HoldVm(true);
        FreezePresentedFrame(true);
        RefreshMenu();
    }

    private void OnMenuClosed()
    {
        GD.Print("[menu] menu loop ends");
        FreezePresentedFrame(false);
        Shell.HoldVm(false);
    }

    /// <summary>The shell's refresh (native's WM_INITMENU) of the bar's menu and the fullscreen strip's.</summary>
    private void RefreshMenu()
    {
        if (_menuItems != null) RefreshMenuItems(_menuItems);
        if (_menuStrip != null) RefreshMenuItems(_menuStrip.Bar.Items);
    }

    private void RefreshMenuItems(IReadOnlyList<MenuItem> items)
    {
        if (ShellSnapshot() is { } state) _shellMenu?.Refresh(items, state);
    }

    /// <summary>The shell's state snapshot, rebuilt each time a surface opens; null before the VM exists.</summary>
    private ShellState? ShellSnapshot() =>
        _vm == null ? null : new ShellState(_windowed, _vm.MenuState);

    /// <summary>A menu command, as age_main_window_proc's WM_COMMAND and AGERC's agerc_menu_command run it: the shell's
    /// commands, then the window menu's.</summary>
    private void ExecuteMenuCommand(int id)
    {
        GD.Print($"[menu] command 0x{id:x}");
        if (_vm == null) return;
        if (ShellCommands.Find(id) is { } command)
        {
            command.Run(Shell, _vm);
            return;
        }
        switch (id)
        {
            // The window menu's commands reach age_main_window_proc as WM_SYSCOMMAND.
            case WindowSystemMenu.Minimize:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
                break;
            case WindowSystemMenu.Maximize:
                SwitchScreenMode(windowed: false, "the window menu's Maximize");   // SC_MAXIMIZE goes fullscreen
                break;
            case WindowSystemMenu.Close:
                RequestUnforcedClose();   // SC_CLOSE posts WM_CLOSE: the 終了 dialog
                break;
            case WindowSystemMenu.Move:
                WindowSystemMenu.StartKeyboardMove();   // DefWindowProc's keyboard move loop
                break;
            default:
                GD.Print($"[menu] command 0x{id:x} is not a shell command (docs/backlog.md, \"Native menu bar\")");
                break;
        }
    }

    /// <summary>The driver's <c>menu</c> step: resolve the command through the shell's table, refresh the menu as
    /// opening it does, refuse a grayed or missing command, and run an enabled one. In fullscreen the menu is the
    /// top-edge strip's, which must be showing. The label is the Japanese one, whatever the player's language, as the
    /// native driver reads it.</summary>
    private System.Text.Json.Nodes.JsonObject ChooseMenuCommand(int id)
    {
        if (_menuItems == null) throw new DriveProtocolException("this run has no menu bar");
        if (!_windowed && (_menuStrip == null || !_menuStripShown))
            throw new DriveProtocolException(
                "fullscreen detaches the menu bar: show the top-edge strip first (hold the pointer on the top edge)");
        IReadOnlyList<MenuItem> items = _windowed ? _menuItems : _menuStrip!.Bar.Items;
        RefreshMenu();
        if (ShellCommands.Find(id) is not { } command || ShellSnapshot() is not { } state)
            throw new DriveProtocolException($"the shell has no command 0x{id:x}");
        if (MenuItem.Find(items, id) is not { Kind: MenuItemKind.Command } item)
            throw new DriveProtocolException($"the menu bar has no command 0x{id:x} ({command.Name})");
        string label = command.Text(ShellStrings.Japanese, state);
        var result = new System.Text.Json.Nodes.JsonObject
        {
            ["command"] = id,
            ["label"] = label,
            ["enabled"] = item.Enabled,
            ["checked"] = item.Checked,
        };
        if (!item.Enabled) throw new DriveProtocolException($"menu command 0x{id:x} ({label}) is grayed");
        if (_windowed) ExecuteMenuCommand(id);
        else ExecuteStripCommand(id);
        return result;
    }

    /// <summary>The game's part of the presented frame (below the menu bar), or null without a rendered frame.</summary>
    private Image? CaptureGameImage()
    {
        Image? image = GetViewport().GetTexture()?.GetImage();
        if (image == null || image.IsEmpty()) return null;
        if (_menuBarHeight <= 0) return image;
        int top = (int)Math.Round(_menuBarHeight * image.GetHeight() / (double)(_screenHeight + _menuBarHeight));
        return image.GetRegion(new Rect2I(0, top, image.GetWidth(), image.GetHeight() - top));
    }

    /// <summary>Cover the game's client area (below the menu bar) with a control in a canvas layer.</summary>
    private void PlaceOverGame(Control control)
    {
        control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        control.OffsetTop = _menuBarHeight;
    }
}

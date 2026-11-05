using System;
using System.Collections.Generic;
using System.IO;
using Age.Engine.Frontend.Control;
using Age.Engine.Frontend.NativeMenu;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Godot;

/// <summary>
/// The windowed AGE's menu bar (AGERC.DLL menu 110; docs/engine-re.md "Native menu bar"). The port reads the menu
/// from the game's own AGERC.DLL, prunes it by <c>set:Menu_*</c> as AGERC does when AGE attaches it, and draws it above
/// the game (<see cref="NativeMenuBar"/>), so the viewport is the game plus <see cref="_menuBarHeight"/> rows and the
/// game's client area starts below them. A game without AGERC.DLL, or a run with <c>--no-menu-bar</c>, has no bar.
/// <para>Opening a menu starts native's modal menu loop: the game holds at its next opcode boundary, its frame
/// freezes, and AGERC refreshes the items (WM_INITMENU). A chosen command then runs as WM_COMMAND does. The port
/// performs 終了, 画像の保存 (表示画面を保存, 画像の保存場所; Main.Screenshot.cs), the ﾒｯｾｰｼﾞ toggles, ｳｲﾝﾄﾞｳ and ﾍﾙﾌﾟ's
/// ﾊﾞｰｼﾞｮﾝ情報 (Main.AgercDialogs.cs); it grays
/// the commands it lacks (docs/backlog.md).</para>
/// </summary>
public partial class Main
{
    private NativeMenuBar? _menuBar;
    private CanvasLayer? _menuBarLayer;
    private List<NativeMenuItem>? _menuItems;
    private int _menuBarHeight;

    private static readonly HashSet<int> PortMenuCommands = new()
    {
        AgercMenu.Exit, AgercMenu.SaveScreen, AgercMenu.ImageFolder, AgercMenu.MessageSkip, AgercMenu.ReadMessageSkip,
        AgercMenu.HideWindow, AgercMenu.AutoMessage, AgercMenu.Windowed, AgercMenu.VersionInfo,
    };

    /// <summary>Read and prune the menu before the window is sized, since the bar adds rows above the game.</summary>
    private void LoadMenuBar(string gameRoot, Sys4StartupSettings settings, string[] userArgs)
    {
        _menuItems = null;
        _menuBarHeight = 0;
        if (Array.IndexOf(userArgs, "--no-menu-bar") >= 0) return;
        string? path = FindFileIgnoringCase(gameRoot, "AGERC.DLL");
        if (path == null)
        {
            GD.Print("[menu] no AGERC.DLL in the game root: no menu bar");
            return;
        }
        List<NativeMenuItem>? items;
        try
        {
            items = NativeMenuResource.Load(File.ReadAllBytes(path), AgercMenu.ResourceId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[menu] reading {path} failed: {error.Message}");
            return;
        }
        if (items == null)
        {
            GD.Print($"[menu] {path} has no menu {AgercMenu.ResourceId}: no menu bar");
            return;
        }
        AgercMenu.Prune(items, key => AgercMenu.Setting(settings, key));
        _agercPath = path;
        _menuItems = items;
        _menuBarHeight = NativeMenuBar.BarHeight;
        GD.Print($"[menu] AGERC menu {AgercMenu.ResourceId}: {string.Join(" ", items.ConvertAll(i => i.Label))}");
    }

    private static string? FindFileIgnoringCase(string directory, string name)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory))
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) return file;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Create the bar once the scene exists. Main itself moves below it.</summary>
    private void CreateMenuBar()
    {
        if (_menuItems == null) return;
        OffsetTop = _menuBarHeight;
        _menuBarLayer = new CanvasLayer { Layer = 101 };   // above the frozen frame (99) and the exit dim (100)
        _menuBar = new NativeMenuBar(_menuItems);
        _menuBar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _menuBar.Opened += OnMenuOpened;
        _menuBar.Closed += OnMenuClosed;
        _menuBar.CommandChosen += ExecuteMenuCommand;
        _menuBarLayer.AddChild(_menuBar);
        AddChild(_menuBarLayer);
    }

    /// <summary>The bar takes input first unless a modal dialog owns the window (a message box or dialog disables
    /// AGE's window, its menu bar included).</summary>
    private bool RouteMenuBarInput(InputEvent e)
    {
        if (_menuBar == null || _vm == null || _host == null) return false;
        if (_exitConfirmation != null || _scriptErrorDialog != null || _fullwidthTextEditor != null
            || _screenshotFolderDialog != null || _agercDialog != null || _debugSceneLauncher?.Visible == true)
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
        _host?.SetFrontendModalHold(true);
        FreezePresentedFrame(true);
        RefreshMenu();
    }

    private void OnMenuClosed()
    {
        GD.Print("[menu] menu loop ends");
        FreezePresentedFrame(false);
        _host?.SetFrontendModalHold(false);
    }

    /// <summary>agerc_refresh_system_menu_state, then gray the commands the port does not perform.</summary>
    private void RefreshMenu()
    {
        if (_menuItems == null || _vm == null) return;
        VmMenuState vm = _vm.MenuState;
        Sys4RegIniStore? reg = _sys4RegIniStore;
        AgercMenu.Refresh(_menuItems, new AgercMenuState
        {
            Windowed = true,
            MessageSkip = vm.MessageSkip,
            ReadTextSkip = vm.ReadTextSkip,
            AutoMessage = vm.AutoMessage,
            HiddenWindow = vm.HiddenWindow,
            TextWait = vm.TextWait,
            YieldHandler = vm.YieldHandler,
            SystemMenuEnabled = vm.SystemMenuEnabled,
            RMouseEvent = reg?.LoadMessageInt("RMouseEvent", 0) ?? 0,
            UseAntiFont = reg?.LoadMessageInt("UseAntiFont", 1) ?? 1,
            MessageSpeed = reg?.LoadMessageInt(Sys4RegIniStore.MessageSpeedKey, Sys4RegIniStore.DefaultMessageSpeed)
                           ?? Sys4RegIniStore.DefaultMessageSpeed,
            MesWinAlpha = reg?.LoadMessageInt(Sys4RegIniStore.MesWinAlphaKey, Sys4RegIniStore.DefaultMesWinAlpha)
                          ?? Sys4RegIniStore.DefaultMesWinAlpha,
            SoundSe = reg?.LoadInt("sound", "SE", 1) ?? 1,
            SoundMusic = reg?.LoadInt("sound", "Music", 0) ?? 0,
        });
        GrayUnperformed(_menuItems);
    }

    private static void GrayUnperformed(List<NativeMenuItem> items)
    {
        foreach (NativeMenuItem item in items)
        {
            if (item.Kind == NativeMenuItemKind.Command && !PortMenuCommands.Contains(item.Id)) item.Enabled = false;
            if (item.Kind == NativeMenuItemKind.Popup) GrayUnperformed(item.Children);
        }
    }

    /// <summary>A menu command, as age_main_window_proc's WM_COMMAND and AGERC's agerc_menu_command run it.</summary>
    private void ExecuteMenuCommand(int id)
    {
        GD.Print($"[menu] command 0x{id:x}");
        if (_vm == null) return;
        switch (id)
        {
            case AgercMenu.Exit:
                RequestUnforcedClose();   // PostMessage(WM_CLOSE, 0): the 終了 dialog
                break;
            case AgercMenu.SaveScreen:
                SaveScreenshotFromMenu();   // IAGEService +0xa4: engine_save_screenshot_bmp
                break;
            case AgercMenu.ImageFolder:
                ChooseScreenshotFolder();   // agerc_choose_screenshot_folder: SHBrowseForFolder
                break;
            case AgercMenu.MessageSkip:
                _vm.RequestMenuMessageSkipToggle();
                break;
            case AgercMenu.AutoMessage:
                _vm.RequestMenuAutoMessageToggle();
                break;
            case AgercMenu.ReadMessageSkip:
                _vm.RequestMenuReadMessageSkipToggle();
                break;
            case AgercMenu.HideWindow:
                _vm.RequestMenuHideWindow();
                break;
            case AgercMenu.Windowed:
                break;   // IAGEService +0x1c(1): already windowed
            case AgercMenu.VersionInfo:
                OpenVersionDialog();   // AGERC host command 2: the ﾊﾞｰｼﾞｮﾝ情報 dialog (Main.AgercDialogs.cs)
                break;
            default:
                GD.Print($"[menu] command 0x{id:x} has no port implementation (docs/backlog.md, \"Native menu bar\")");
                break;
        }
    }

    /// <summary>The driver's <c>menu</c> step: refresh the menu as opening it does, refuse a grayed or missing
    /// command, and run an enabled one.</summary>
    private System.Text.Json.Nodes.JsonObject ChooseMenuCommand(int id)
    {
        if (_menuItems == null) throw new DriveProtocolException("this run has no menu bar");
        RefreshMenu();
        if (NativeMenuItem.Find(_menuItems, id) is not { Kind: NativeMenuItemKind.Command } item)
            throw new DriveProtocolException($"the menu bar has no command 0x{id:x}");
        var result = new System.Text.Json.Nodes.JsonObject
        {
            ["command"] = id,
            ["label"] = item.Text,
            ["enabled"] = item.Enabled,
            ["checked"] = item.Checked,
        };
        if (!item.Enabled) throw new DriveProtocolException($"menu command 0x{id:x} ({item.Text}) is grayed");
        ExecuteMenuCommand(id);
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

using System;
using Ome.Engine.Frontend.Shell;
using Godot;

/// <summary>
/// AGE's screen mode (display_toggle_screen_mode_and_menu_bar@0x412df0; docs/engine-re.md "Screen mode").
/// <para>A window shows the game below the menu bar. Fullscreen is the port's modern style: a borderless window over
/// the monitor with the game scaled to fit, keeping its aspect (Godot's Keep), whatever native's display:AspectMode
/// and VirtualFullScreen say. Without the menu bar, the top-edge strip (<see cref="SystemMenuStrip"/>) offers the menu:
/// a pointer held on the screen's top two rows past op 0x149's delay, or Alt, shows it.</para>
/// <para>Switches come from menu 0x9c5a ﾌﾙｽｸﾘｰﾝ and 0x9c5b ｳｲﾝﾄﾞｳ, Alt+Enter, the maximize box, the strip's restore
/// button and op 0x2cf. A switch to the current mode does nothing; display:ForceScreen 1 keeps a window. Each switch
/// stores display:ScreenMode (native writes it at exit; the port writes the same value at once) and runs
/// CALLBACK_WINDOW.BIN. The game starts in the mode display:ScreenMode names (engine_initialize_graphics_from_settings:
/// a new profile's 0 is fullscreen).</para>
/// </summary>
public partial class Main
{
    private const ulong StripDwellTickMilliseconds = 100;   // age_main_window_proc's SetTimer(1, 100)

    private bool _windowed = true;              // display_windowed (ctx+0x9a298)
    private bool _forceScreen;                  // display:ForceScreen 1: a window stays a window
    private Vector2I _windowedSize, _windowedPosition;
    private int _requestedScreenMode = -1;      // op 0x2cf's target (1 window, 0 fullscreen), from the VM thread
    private SystemMenuStrip? _menuStrip;
    private CanvasLayer? _menuStripLayer;
    private bool _menuStripShown;
    private ulong? _stripDwellStartMs;          // g_dwSystemMenuDwellStartMs, armed by the top two rows
    private ulong _stripDwellNextTickMs;

    /// <summary>engine_initialize_graphics_from_settings: display_windowed = (display:ScreenMode != 0).
    /// <c>--windowed</c> starts a window without changing the setting; so does a run without SYS4REG.INI (self-test,
    /// read-only persistence).</summary>
    private void InitScreenMode(string[] userArgs)
    {
        Window root = GetTree().Root;
        _windowedSize = root.Size;
        _windowedPosition = root.Position;
        _forceScreen = _sys4RegIniStore?.LoadInt("display", "ForceScreen", 0) == 1;
        bool windowed = _sys4RegIniStore == null || _sys4RegIniStore.LoadInt("display", "ScreenMode", 0) != 0
                        || Array.IndexOf(userArgs, "--windowed") >= 0;
        CreateMenuStrip();
        GD.Print($"[display] starting {(windowed ? "in a window" : "fullscreen")} " +
                 $"(display:ScreenMode={_sys4RegIniStore?.LoadInt("display", "ScreenMode", 0).ToString() ?? "none"}" +
                 $"{(Array.IndexOf(userArgs, "--windowed") >= 0 ? ", --windowed" : "")}, " +
                 $"ForceScreen={(_forceScreen ? 1 : 0)})");
        if (windowed) return;
        _windowed = false;
        ApplyScreenLayout();
    }

    /// <summary>The strip holds its own copy of the shell's menu, pruned as the bar is (native's WM_INITDIALOG:
    /// agerc_refresh_system_menu_state, agerc_prune_menu_by_settings). Its caption shows the game's title; the icon
    /// beside it waits for the shell's own OME icon (engine shell spec §5.5), since the game's is inside AGERC.DLL.
    /// </summary>
    private void CreateMenuStrip()
    {
        if (_shellMenu == null || _menuItems == null) return;
        _menuStrip = new SystemMenuStrip(_shellMenu.CreateBar(), _catalog.Title, icon: null) { Visible = false };
        _menuStrip.Bar.SystemMenu = WindowSystemMenu.Create(fullscreen: true);   // the strip's: Restore, no Maximize
        _menuStrip.Bar.Opened += OnMenuOpened;
        _menuStrip.Bar.Closed += OnMenuClosed;
        _menuStrip.Bar.CommandChosen += ExecuteStripCommand;
        _menuStrip.Minimize += () => MinimizeFromStrip("the minimize button");
        _menuStrip.Restore += () => RestoreFromStrip("the strip's restore button");
        _menuStrip.CloseRequested += () => CloseFromStrip("the close button");
        _menuStripLayer = new CanvasLayer { Layer = 101 };   // with the menu bar: above the frozen frame and the dim
        _menuStripLayer.AddChild(_menuStrip);
        AddChild(_menuStripLayer);
        _menuStrip.LayoutStrip(new Vector2I(_screenWidth, _screenHeight));
    }

    /// <summary>display_toggle_screen_mode_and_menu_bar: switch to <paramref name="windowed"/>, or do nothing when the
    /// game is already there or display:ForceScreen keeps the window. True when the mode changed.</summary>
    private bool SwitchScreenMode(bool windowed, string source)
    {
        if (windowed == _windowed) return false;
        if (_windowed && _forceScreen)
        {
            GD.Print($"[display] {source}: display:ForceScreen=1 keeps the window");
            return false;
        }
        HideMenuStrip("a screen-mode switch");
        if (_windowed) RememberWindowedRect();
        _windowed = windowed;
        ApplyScreenLayout();
        _sys4RegIniStore?.SaveInt("display", "ScreenMode", windowed ? 1 : 0);
        _vm?.RequestWindowCallback();   // script_launch_by_name("CALLBACK_WINDOW.BIN")
        GD.Print($"[display] {source}: {(windowed ? "window" : "fullscreen")}; CALLBACK_WINDOW.BIN queued");
        return true;
    }

    private void RememberWindowedRect()
    {
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
        _windowedSize = DisplayServer.WindowGetSize();
        _windowedPosition = DisplayServer.WindowGetPosition();
    }

    /// <summary>A window has the menu bar above the game; fullscreen detaches it (SetMenu(NULL)), so the game's
    /// client area starts at the top.</summary>
    private void ApplyScreenLayout()
    {
        _menuBarHeight = _windowed && _menuItems != null ? NativeMenuBar.BarHeight : 0;
        Window root = GetTree().Root;
        root.ContentScaleSize = new Vector2I(_screenWidth, _screenHeight + _menuBarHeight);
        OffsetTop = _menuBarHeight;
        if (_menuBarLayer != null) _menuBarLayer.Visible = _windowed;
        if (!_windowed) _menuBar?.Close();
        Win32Dialog.GameClientOrigin = new Vector2I(0, _menuBarHeight);
        if (_exitDim != null) _exitDim.OffsetTop = _menuBarHeight;
        if (_frozenFrame != null) _frozenFrame.OffsetTop = _menuBarHeight;
        if (_windowed)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(_windowedSize);
            DisplayServer.WindowSetPosition(_windowedPosition);
        }
        else
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
    }

    /// <summary>Op 0x2cf, from the VM thread: the frontend switches at its next frame.</summary>
    internal void RequestScreenModeFromScript(bool windowed)
        => System.Threading.Volatile.Write(ref _requestedScreenMode, windowed ? 1 : 0);

    /// <summary>Per frame: op 0x2cf's request, the maximize box, the strip's dwell timer and what hides the strip.
    /// </summary>
    private void ProcessScreenMode()
    {
        int requested = System.Threading.Interlocked.Exchange(ref _requestedScreenMode, -1);
        if (requested >= 0) SwitchScreenMode(requested == 1, "op 0x2cf");

        DisplayServer.WindowMode mode = DisplayServer.WindowGetMode();
        if (_windowed && mode == DisplayServer.WindowMode.Maximized)
        {
            // WM_SYSCOMMAND SC_MAXIMIZE goes fullscreen; with display:ForceScreen 1 AGE ignores it.
            if (_forceScreen) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            else SwitchScreenMode(windowed: false, "the maximize box");
        }
        else if (_windowed && mode == DisplayServer.WindowMode.Windowed)
        {
            RememberWindowedRect();
        }

        if (_menuStripShown && HostUiActive()) HideMenuStrip("a modal window");   // a modal window takes the activation
        if (_stripDwellStartMs != null && Time.GetTicksMsec() >= _stripDwellNextTickMs)
        {
            _stripDwellNextTickMs += StripDwellTickMilliseconds;   // WM_TIMER id 1
            TickStripDwell();
        }
    }

    /// <summary>AGERC host command -1: a dialog, message box or menu of the host is open.</summary>
    private bool HostUiActive()
        => _exitConfirmation != null || _scriptErrorDialog != null || _agercDialog != null
           || _screenshotFolderDialog != null || _fullwidthTextEditor != null || _menuBar?.IsOpen == true
           || _debugSceneLauncher?.Visible == true;

    /// <summary>age_main_window_proc's WM_MOUSEMOVE: a pointer on the client's top two rows arms the dwell (or ticks
    /// it when armed); any other row disarms it. <paramref name="position"/> is in viewport coordinates; the rows are
    /// the window's, the top of the screen in fullscreen.</summary>
    private void NoteTopEdgePointer(Vector2 position)
    {
        float row = (GetViewport().GetScreenTransform() * position).Y;
        if (row >= 0 && row < 2)
        {
            if (_stripDwellStartMs != null)
            {
                TickStripDwell();
                return;
            }
            ulong now = Time.GetTicksMsec();
            _stripDwellStartMs = now;
            _stripDwellNextTickMs = now + StripDwellTickMilliseconds;
            return;
        }
        _stripDwellStartMs = null;
    }

    /// <summary>system_menu_top_edge_dwell_timer_tick@0x486280: past op 0x149's delay (strictly), with neither button
    /// held, in fullscreen and with no host UI open, show the strip.</summary>
    private void TickStripDwell()
    {
        if (_stripDwellStartMs is not { } start || _vm == null) return;
        ulong elapsed = Time.GetTicksMsec() - start;
        if (elapsed <= _vm.SystemMenuShowDelayMilliseconds) return;
        if (Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right)) return;
        if (_windowed || HostUiActive()) return;
        ShowMenuStrip("the top edge");
    }

    /// <summary>system_menu_strip_show@0x407bd0 and AGERC host command 4. Native suspends presentation (the frame
    /// stays as last presented) and turns input polling off while the strip shows; the game keeps running.</summary>
    private void ShowMenuStrip(string source)
    {
        _stripDwellStartMs = null;   // the dwell resets and its timer is killed
        if (_menuStrip == null || _menuStripShown || _windowed) return;
        _menuStripShown = true;
        FreezePresentedFrame(true);
        _menuStrip.ResetState();
        _menuStrip.Bar.SetActive(true);
        RefreshStripMenu();
        _menuStrip.Visible = true;
        GD.Print($"[display] system-menu strip shown ({source})");
    }

    /// <summary>agerc_system_menu_strip_hide@0x10002a40, then IAGEService +0x30: presentation and input polling
    /// resume.</summary>
    private void HideMenuStrip(string reason)
    {
        if (_menuStrip == null || !_menuStripShown) return;
        _menuStripShown = false;
        _menuStrip.ResetState();
        _menuStrip.Visible = false;
        FreezePresentedFrame(false);
        GD.Print($"[display] system-menu strip hidden ({reason})");
    }

    /// <summary>SC_MINIMIZE on the strip: hide, IAGEService +0x9c(1, 0), then AGE's own minimize.</summary>
    private void MinimizeFromStrip(string source)
    {
        HideMenuStrip(source);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
    }

    /// <summary>SC_RESTORE on the strip: hide, then IAGEService +0x1c(1).</summary>
    private void RestoreFromStrip(string source)
    {
        HideMenuStrip(source);
        SwitchScreenMode(windowed: true, source);
    }

    /// <summary>IDCANCEL (and SC_CLOSE): PostMessage(WM_CLOSE, 0) to AGE's window, then hide.</summary>
    private void CloseFromStrip(string source)
    {
        HideMenuStrip(source);
        RequestUnforcedClose();
    }

    private void RefreshStripMenu()
    {
        if (_menuStrip == null) return;
        RefreshMenuItems(_menuStrip.Bar.Items);
        _menuStrip.Bar.QueueRedraw();
    }

    /// <summary>The strip's WM_COMMAND: 終了 posts WM_CLOSE(0) and hides; the rest go to agerc_menu_command, which
    /// hides the strip before each command the port performs (表示画面を保存 after its screenshot).</summary>
    private void ExecuteStripCommand(int id)
    {
        switch (id)
        {
            // The strip's window menu goes to the strip's own WM_SYSCOMMAND, as its caption buttons do.
            case WindowSystemMenu.Minimize:
                MinimizeFromStrip("the window menu's Minimize");
                return;
            case WindowSystemMenu.Restore:
                RestoreFromStrip("the strip's window menu Restore");
                return;
            case WindowSystemMenu.Close:
                CloseFromStrip("the window menu's Close");
                return;
            case WindowSystemMenu.Move:
                GD.Print("[display] the strip's window menu Move: the port's strip does not move");
                return;
        }
        // The strip's own hold froze the frame it opened over. Keep that frame through the hide and the command, so a
        // dialog the command opens (終了) freezes the game's frame, not the last rendered one with the strip on it.
        FreezePresentedFrame(true);
        if (id != ShellCommands.SaveScreen) HideMenuStrip($"menu command 0x{id:x}");
        ExecuteMenuCommand(id);
        if (id == ShellCommands.SaveScreen) HideMenuStrip($"menu command 0x{id:x}");
        FreezePresentedFrame(false);
    }

    /// <summary>Input while the strip shows. True when the strip took the event; false lets it reach the game, as a
    /// click outside the strip does after hiding it (the click activates AGE's window).</summary>
    private bool RouteMenuStripInput(InputEvent e)
    {
        if (_menuStrip == null || !_menuStripShown) return false;
        if (HostUiActive() && !_menuStrip.Bar.IsOpen) return false;
        switch (e)
        {
            case InputEventMouseButton button when _menuStrip.TakeCaptionRelease(button):
                return true;
            case InputEventMouse mouse when _menuStrip.Covers(mouse.Position):
                _menuStrip.HandleMouse(mouse);
                return true;
            case InputEventMouseButton { Pressed: true }:
                HideMenuStrip("a click outside it");   // WM_ACTIVATE(WA_INACTIVE) on the strip
                return false;
            case InputEventMouseMotion:
                _menuStrip.HandleMouse(new InputEventMouseMotion { Position = new Vector2(-1, -1) });
                return true;   // input polling is off while the strip shows
            case InputEventKey key:
                // The strip's bar takes keys first: its loop's, and Alt or F10 taps that start it.
                if (_menuStrip.Bar.HandleInput(key) || _menuStrip.Bar.IsOpen) return true;
                if (key.Pressed && !key.Echo)
                {
                    if (key.Keycode == Key.Escape || (key.Keycode == Key.F4 && key.AltPressed))
                    {
                        HideMenuStrip("Escape");   // IDCANCEL (and SC_CLOSE): WM_CLOSE(0) to AGE's window
                        RequestUnforcedClose();
                    }
                    else if (key.Keycode is Key.Enter or Key.KpEnter && key.AltPressed)
                    {
                        HideMenuStrip("Alt+Enter");   // SC_KEYMENU 0xd: hide, then the opposite of the current mode
                        SwitchScreenMode(!_windowed, "Alt+Enter on the strip");
                    }
                }
                return true;   // the strip has the keyboard focus, and the game's input polling is off
            default:
                return true;
        }
    }

    /// <summary>Keys the window procedure takes in either mode: Alt+Enter switches (SC_KEYMENU 0xd), and Alt alone in
    /// fullscreen shows the strip at once (WM_SYSKEYDOWN VK_MENU). Neither applies while a modal window is open.
    /// </summary>
    private bool RouteScreenModeKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key || HostUiActive()) return false;
        if (key.Keycode is Key.Enter or Key.KpEnter && key.AltPressed)
        {
            if (!_forceScreen) SwitchScreenMode(!_windowed, "Alt+Enter");
            return true;
        }
        if (key.Keycode == Key.Alt && !_windowed)
        {
            ShowMenuStrip("Alt");
            return true;
        }
        return false;
    }
}

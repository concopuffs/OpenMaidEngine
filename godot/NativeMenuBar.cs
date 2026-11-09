using System;
using System.Collections.Generic;
using Ome.Engine.Frontend.NativeMenu;
using Godot;

/// <summary>
/// AGE's windowed menu bar (AGERC menu 110), drawn as Windows 10 draws it for AGE: the bar above the game's client
/// area, its dropdowns and their submenus, the hot and pushed states, check marks and the dropdown shadow. Values come
/// from PrintWindow and screen captures of the native bar (2026-11-05, docs/engine-re.md "Native menu bar").
/// <para>While a dropdown is open, native runs Windows' modal menu loop: the game neither ticks nor presents, and the
/// menu takes every mouse and key event. <see cref="Opened"/> and <see cref="Closed"/> bracket that loop, and a chosen
/// command is raised through <see cref="CommandChosen"/> before <see cref="Closed"/>, as WM_COMMAND follows the loop's
/// end. Main routes input here first (<see cref="HandleInput"/>).</para>
/// <para>Keyboard mode is DefWindowProc's. Alt or F10 pressed and released with nothing between selects the first bar
/// item without a dropdown, and the loop starts. Left and Right then cycle the bar items and the window-menu position
/// (<see cref="SystemMenu"/>; nothing is drawn there). Down, Up, Enter or an access key opens a dropdown with its first
/// item selected, as Alt plus an access key does from outside the loop. Alt+Space opens the window menu. Inside the
/// loop, Alt or F10 ends it, and Escape closes the innermost dropdown and then the loop. Measured on native AGE on
/// 2026-11-06 (docs/engine-re.md, "Native menu bar").</para>
/// </summary>
public partial class NativeMenuBar : Control
{
    public const int BarHeight = 20;                 // SM_CYMENU: 19 item rows and the bar's bottom line
    private const int BarItemHeight = 19;
    private const int BarItemPadding = 7;            // each side of a bar item's text
    private const int BarTextTop = 2;                // DT_VCENTER of a 15 px line in 19 rows
    private const int RowHeight = 22, SeparatorHeight = 7;
    private const int PopupInset = 3;                // 1 px border and 2 px padding
    private const int GutterWidth = 28, LabelLeft = 35, RowTextTop = 3;
    private const int PopupExtraWidth = 90;          // a dropdown is its widest label (and accelerator) plus this
    private const int AcceleratorGap = 34;
    private const int SubmenuOverlap = 6;
    private const double SubmenuDelaySeconds = 0.4;  // SPI_GETMENUSHOWDELAY

    private static readonly Color BarBackground = Colors.White;
    private static readonly Color BarLine1 = Color.Color8(0xf2, 0xf2, 0xf2);
    private static readonly Color BarLine2 = Color.Color8(0xf0, 0xf0, 0xf0);
    private static readonly Color BarHotFill = Color.Color8(0xe5, 0xf3, 0xff);
    private static readonly Color BarHotBorder = Color.Color8(0xcc, 0xe8, 0xff);
    private static readonly Color BarHotBottom = Color.Color8(0xc4, 0xe0, 0xf7);
    private static readonly Color BarPushedFill = Color.Color8(0xcc, 0xe8, 0xff);
    private static readonly Color BarPushedBorder = Color.Color8(0x99, 0xd1, 0xff);
    private static readonly Color BarPushedBottom = Color.Color8(0x91, 0xc9, 0xf7);
    private static readonly Color Text = Colors.Black;
    private static readonly Color GrayText = Color.Color8(0x6d, 0x6d, 0x6d);   // disabled items, inactive bar
    private static readonly Color PopupBorder = Color.Color8(0xcc, 0xcc, 0xcc);
    private static readonly Color PopupBackground = Color.Color8(0xf2, 0xf2, 0xf2);
    private static readonly Color Gutter = Color.Color8(0xf0, 0xf0, 0xf0);
    private static readonly Color SeparatorLine = Color.Color8(0xd7, 0xd7, 0xd7);
    private static readonly Color ItemHot = Color.Color8(0x91, 0xc9, 0xf7);
    private static readonly Color ItemHotGutter = Color.Color8(0x90, 0xc8, 0xf6);
    private static readonly Color ItemGrayedHot = Color.Color8(0xe6, 0xe6, 0xe6);   // a selected grayed item
    private static readonly Color ItemGrayedHotGutter = Color.Color8(0xe4, 0xe4, 0xe4);
    private static readonly Color CheckBox = Color.Color8(0x90, 0xc8, 0xf6);
    private static readonly Color CheckBoxHot = Color.Color8(0x56, 0xb0, 0xfa);
    private static readonly Color Glyph = Color.Color8(0x33, 0x33, 0x33);

    // The check mark (10×8 at x 9, row y 6) and the submenu arrow (5×8 at x width-17, row y 7), as alpha over the
    // background, from the captures.
    private static readonly byte[,] CheckMask =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0, 0, 48 },
        { 0, 0, 0, 0, 0, 0, 0, 0, 159, 255 },
        { 0, 0, 0, 0, 0, 0, 0, 159, 255, 96 },
        { 48, 48, 0, 0, 0, 0, 159, 255, 96, 0 },
        { 176, 240, 48, 0, 0, 159, 255, 96, 0, 0 },
        { 17, 207, 240, 48, 159, 255, 96, 0, 0, 0 },
        { 0, 17, 207, 255, 255, 96, 0, 0, 0, 0 },
        { 0, 0, 17, 207, 96, 0, 0, 0, 0, 0 },
    };
    private static readonly byte[,] ArrowMask =
    {
        { 48, 207, 16, 0, 0 },
        { 0, 160, 207, 16, 0 },
        { 0, 0, 160, 207, 16 },
        { 0, 0, 0, 192, 176 },
        { 0, 0, 96, 240, 48 },
        { 0, 96, 240, 48, 0 },
        { 48, 240, 48, 0, 0 },
        { 0, 32, 0, 0, 0 },
    };

    // The dropdown shadow: black at these alphas, 5 px right of and below the dropdown, starting 5 px in from its
    // top and left. The right edge ramps in over 5 rows (Ramp, one row per row from the top), then holds Full; the
    // bottom edge mirrors it; Corner is the 5×5 block past both edges.
    private static readonly float[] Full = { 0.443f, 0.329f, 0.169f, 0.055f, 0.012f };
    private static readonly float[,] Ramp =
    {
        { 0.051f, 0.035f, 0.020f, 0.008f, 0.004f },
        { 0.149f, 0.110f, 0.055f, 0.020f, 0.004f },
        { 0.294f, 0.220f, 0.110f, 0.039f, 0.008f },
        { 0.392f, 0.294f, 0.149f, 0.051f, 0.008f },
        { 0.435f, 0.325f, 0.165f, 0.055f, 0.012f },
    };
    private static readonly float[,] Corner =
    {
        { 0.393f, 0.293f, 0.149f, 0.050f, 0.008f },
        { 0.293f, 0.219f, 0.112f, 0.037f, 0.008f },
        { 0.149f, 0.112f, 0.054f, 0.021f, 0.004f },
        { 0.050f, 0.037f, 0.021f, 0.008f, 0.004f },
        { 0.008f, 0.008f, 0.004f, 0.004f, 0.000f },
    };

    private sealed class Dropdown
    {
        public required List<NativeMenuItem> Items { get; init; }
        public required Rect2I Rect { get; init; }
        public required int[] RowTops { get; init; }
        public required int AcceleratorX { get; init; }
        public int Selected = -1;
        public int RowHeightOf(int index) =>
            Items[index].Kind == NativeMenuItemKind.Separator ? SeparatorHeight : RowHeight;
    }

    private readonly List<NativeMenuItem> _items;
    private readonly Win32Font _font = Win32DialogStyle.MenuFont;
    private Win32Font? _boldFont;
    private readonly List<Dropdown> _dropdowns = new();
    private Rect2I[] _barRects = Array.Empty<Rect2I>();
    private int _hotBar = -1;
    private int _openBar = -1;       // the selected bar position while the loop runs (SystemPosition: the window menu)
    private bool _keyboardMode;
    private bool _active = true;
    private bool _swallowRelease;   // a press that ended the menu loop: its release belongs to the menu too
    private bool _altArmed, _f10Armed;   // Alt or F10 is down with no other key or button since: its release enters
    private Key _swallowKey = Key.None;  // the Alt or F10 that ended the loop: its release is the loop's too
    private (int Level, int Index, double Remaining)? _pendingSubmenu;

    /// <summary>The menu loop starts (WM_INITMENU): refresh the items, hold the game.</summary>
    public event Action? Opened;
    /// <summary>A command was chosen; raised before <see cref="Closed"/>.</summary>
    public event Action<int>? CommandChosen;
    /// <summary>The menu loop ended.</summary>
    public event Action? Closed;

    public NativeMenuBar(List<NativeMenuItem> items)
    {
        _items = items;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);
    }

    public IReadOnlyList<NativeMenuItem> Items => _items;

    /// <summary>The menu loop runs: a bar position is selected, with or without a dropdown.</summary>
    public bool IsOpen => _openBar >= 0;

    /// <summary>The window menu, reached after the last bar item (<see cref="WindowSystemMenu"/>); null leaves the
    /// position out.</summary>
    public List<NativeMenuItem>? SystemMenu { get; set; }

    /// <summary>F10 does not enter the loop (op <c>0x10f</c>'s mask bit <c>0x200</c>: AGE returns 0 for it).</summary>
    public Func<bool>? IgnoreF10 { get; set; }

    private int SystemPosition => _items.Count;
    private int PositionCount => _items.Count + (SystemMenu != null ? 1 : 0);
    private int Neighbour(int direction) => (_openBar + direction + PositionCount) % PositionCount;

    /// <summary>The screen in the bar's own coordinates, which dropdowns stay inside. Null: the bar's own rectangle
    /// (the windowed bar covers the whole viewport). The fullscreen strip's bar sits below its caption.</summary>
    public Rect2I? Screen { get; set; }

    /// <summary>The window is the active window; an inactive window draws its bar labels gray.</summary>
    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (!active) Close();
        QueueRedraw();
    }

    /// <summary>Lay out the bar for a viewport <paramref name="width"/> wide.</summary>
    public void LayoutBar(int width)
    {
        _barRects = new Rect2I[_items.Count];
        int x = 0;
        int right = _items.FindIndex(item => item.RightJustified);
        int end = right < 0 ? _items.Count : right;
        for (int i = 0; i < end; i++)
        {
            int w = BarItemWidth(_items[i]);
            _barRects[i] = new Rect2I(x, 0, w, BarItemHeight);
            x += w;
        }
        if (right >= 0)
        {
            int total = 0;
            for (int i = right; i < _items.Count; i++) total += BarItemWidth(_items[i]);
            x = Math.Max(x, width - total);
            for (int i = right; i < _items.Count; i++)
            {
                int w = BarItemWidth(_items[i]);
                _barRects[i] = new Rect2I(x, 0, w, BarItemHeight);
                x += w;
            }
        }
        QueueRedraw();
    }

    private int BarItemWidth(NativeMenuItem item) => (int)_font.Width(item.Label) + 2 * BarItemPadding;

    public override void _Notification(int what)
    {
        if (what == NotificationResized) LayoutBar((int)Size.X);
    }

    /// <summary>Close the menu without a command, as a cancelled menu loop does.</summary>
    public void Close()
    {
        _altArmed = _f10Armed = false;
        if (_openBar < 0) return;
        _openBar = -1;
        _dropdowns.Clear();
        _pendingSubmenu = null;
        _keyboardMode = false;
        SetProcess(false);
        QueueRedraw();
        Closed?.Invoke();
    }

    /// <summary>Choose command <paramref name="id"/> as a click on it would, if the menu holds it.</summary>
    public void Choose(int id)
    {
        CommandChosen?.Invoke(id);
        Close();
    }

    // ---- input ----

    /// <summary>Take an input event before the game does. True when the menu consumed it. Positions are viewport
    /// coordinates; the bar measures them from its own origin.</summary>
    public bool HandleInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
                return OnMotion(ToPoint(motion.Position - GlobalPosition));
            case InputEventMouseButton button:
                return OnButton(button, ToPoint(button.Position - GlobalPosition));
            case InputEventKey key:
                return OnKey(key);
            default:
                return IsOpen;
        }
    }

    private static Vector2I ToPoint(Vector2 position) => new((int)Math.Floor(position.X), (int)Math.Floor(position.Y));

    private bool OnMotion(Vector2I p)
    {
        if (!IsOpen)
        {
            int hot = p.Y < BarHeight ? BarItemAt(p) : -1;
            if (hot != _hotBar)
            {
                _hotBar = hot;
                QueueRedraw();
            }
            return p.Y < BarHeight;
        }
        if (_dropdowns.Count == 0)
        {
            // The bar is selected without a dropdown: the pointer moves the selection and opens nothing.
            int over = p.Y < BarHeight ? BarItemAt(p) : -1;
            if (over >= 0 && over != _openBar)
            {
                _openBar = over;
                QueueRedraw();
            }
            return true;
        }
        var (level, row) = DropdownAt(p);
        if (level >= 0)
        {
            _keyboardMode = false;
            if (row >= 0) Select(level, row, hover: true);
            return true;
        }
        int bar = BarItemAt(p);
        if (bar >= 0 && bar != _openBar && p.Y < BarHeight) OpenBar(bar, keyboard: false);
        return true;
    }

    private bool OnButton(InputEventMouseButton button, Vector2I p)
    {
        bool left = button.ButtonIndex == MouseButton.Left;
        if (button.Pressed) _altArmed = _f10Armed = false;
        if (!IsOpen)
        {
            if (_swallowRelease && !button.Pressed && left)
            {
                _swallowRelease = false;
                return true;
            }
            if (p.Y >= BarHeight) return false;
            // A press on an inactive window's bar activates the window and opens the menu at once.
            int pressed = left && button.Pressed ? BarItemAt(p) : -1;
            if (pressed >= 0)
            {
                OpenBar(pressed, keyboard: false);
            }
            return true;
        }
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) return true;
        if (button.Pressed)
        {
            // Dropdowns are windows above the bar (the window menu covers part of it).
            var (level, row) = DropdownAt(p);
            if (level >= 0)
            {
                if (row >= 0) Select(level, row, hover: false);
                return true;
            }
            int bar = p.Y < BarHeight ? BarItemAt(p) : -1;
            if (bar >= 0)
            {
                if (bar == _openBar && _dropdowns.Count > 0)
                {
                    _swallowRelease = left;
                    Close();
                }
                else
                {
                    OpenBar(bar, keyboard: false);
                }
                return true;
            }
            _swallowRelease = left;
            Close();   // a press outside the menu ends the loop and is eaten, with its release
            return true;
        }
        // Release: a command item under the pointer runs; a release on the bar item that opened the menu keeps it.
        var (releaseLevel, releaseRow) = DropdownAt(p);
        if (releaseLevel >= 0 && releaseRow >= 0)
        {
            NativeMenuItem item = _dropdowns[releaseLevel].Items[releaseRow];
            if (item.Kind == NativeMenuItemKind.Command && item.Enabled) Choose(item.Id);
            else if (item.Kind == NativeMenuItemKind.Popup && item.Enabled) OpenSubmenu(releaseLevel, releaseRow, false);
        }
        return true;
    }

    private bool OnKey(InputEventKey key)
    {
        if (!key.Pressed) return OnKeyRelease(key);
        if (key.Echo && key.Keycode is Key.Alt or Key.F10) return IsOpen;   // a held Alt or F10 repeats: nothing new
        if (!IsOpen) return OnKeyOutsideLoop(key);
        if (key.Keycode is Key.Alt or Key.F10)
        {
            _swallowKey = key.Keycode;   // the loop ends on the press, so the release enters nothing
            Close();
            return true;
        }
        _keyboardMode = true;
        if (_dropdowns.Count == 0) OnBarKey(key);
        else OnDropdownKey(key);
        return true;
    }

    /// <summary>WM_SYSKEYUP: DefWindowProc raises SC_KEYMENU, and the loop starts on the first bar item, when Alt or
    /// F10 is released with no other key or button since its press.</summary>
    private bool OnKeyRelease(InputEventKey key)
    {
        if (_swallowKey != Key.None && key.Keycode == _swallowKey)
        {
            _swallowKey = Key.None;
            return true;
        }
        bool tapped = false;
        if (key.Keycode == Key.Alt)
        {
            tapped = _altArmed;
            _altArmed = false;
        }
        else if (key.Keycode == Key.F10)
        {
            tapped = _f10Armed;
            _f10Armed = false;
        }
        if (tapped && !IsOpen)
        {
            SelectBar(0);
            return true;
        }
        return IsOpen;
    }

    /// <summary>A press outside the loop: Alt or F10 arms the keyboard entry, any other key disarms it, and Alt with
    /// an access key or Space starts the loop with that dropdown open (SC_KEYMENU). The game still sees the press
    /// unless the menu takes it. A key reaches only the window with the keyboard focus, so the window counts as
    /// active (the driver's keys reach an unfocused one, as its posted messages reach native AGE's).</summary>
    private bool OnKeyOutsideLoop(InputEventKey key)
    {
        if (key.Keycode == Key.Alt)
        {
            _altArmed = true;
            _f10Armed = false;
            return false;
        }
        _altArmed = false;
        if (key.Keycode == Key.F10)
        {
            _f10Armed = IgnoreF10?.Invoke() != true;
            return false;
        }
        _f10Armed = false;
        if (key.Echo || !key.AltPressed) return false;
        if (key.Keycode == Key.Space && SystemMenu != null)
        {
            OpenBar(SystemPosition, keyboard: true);
            return true;
        }
        char letter = KeyLetter(key);
        int bar = letter == '\0' ? -1 : _items.FindIndex(item => item.Mnemonic == letter);
        if (bar < 0) return false;
        OpenOrChoose(bar);
        return true;
    }

    /// <summary>Keys while a bar position is selected with no dropdown open.</summary>
    private void OnBarKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left:
                SelectBar(Neighbour(-1));
                break;
            case Key.Right:
                SelectBar(Neighbour(1));
                break;
            case Key.Up:
            case Key.Down:
                OpenBar(_openBar, keyboard: true);
                break;
            case Key.Enter:
            case Key.KpEnter:
                OpenOrChoose(_openBar);
                break;
            default:
                char letter = KeyLetter(key);
                int bar = letter == '\0' ? -1 : _items.FindIndex(item => item.Mnemonic == letter);
                if (bar >= 0) OpenOrChoose(bar);
                break;
        }
    }

    /// <summary>A bar command runs; any other position opens its dropdown with the first item selected.</summary>
    private void OpenOrChoose(int bar)
    {
        if (bar < _items.Count && _items[bar].Kind == NativeMenuItemKind.Command)
        {
            if (_items[bar].Enabled) Choose(_items[bar].Id);
            else SelectBar(bar);
            return;
        }
        OpenBar(bar, keyboard: true);
    }

    /// <summary>Keys while a dropdown is open.</summary>
    private void OnDropdownKey(InputEventKey key)
    {
        Dropdown top = _dropdowns[^1];
        int level = _dropdowns.Count - 1;
        switch (key.Keycode)
        {
            case Key.Escape:
                // The innermost dropdown closes; the bar's own leaves its bar item selected.
                CloseFrom(level);
                _pendingSubmenu = null;
                break;
            case Key.Up:
                Select(level, Step(top, -1), hover: false);
                break;
            case Key.Down:
                Select(level, Step(top, 1), hover: false);
                break;
            case Key.Right:
                if (top.Selected >= 0 && top.Items[top.Selected].Kind == NativeMenuItemKind.Popup
                    && top.Items[top.Selected].Enabled)
                    OpenSubmenu(level, top.Selected, selectFirst: true);
                else
                    OpenBar(Neighbour(1), keyboard: true);   // the window menu is one of the positions
                break;
            case Key.Left:
                if (level > 0) CloseFrom(level);
                else OpenBar(Neighbour(-1), keyboard: true);
                break;
            case Key.Enter:
            case Key.KpEnter:
            case Key.Space:
                Activate(level, top.Selected);
                break;
            default:
                char letter = KeyLetter(key);
                if (letter != '\0')
                {
                    int row = top.Items.FindIndex(item => item.Mnemonic == letter);
                    if (row >= 0)
                    {
                        Select(level, row, hover: false);
                        Activate(level, row);
                    }
                }
                break;
        }
    }

    private static char KeyLetter(InputEventKey key)
    {
        Key code = key.Keycode;
        if (code is >= Key.A and <= Key.Z) return (char)('A' + (code - Key.A));
        if (code is >= Key.Key0 and <= Key.Key9) return (char)('0' + (code - Key.Key0));
        return '\0';
    }

    private void Activate(int level, int row)
    {
        if (row < 0) return;
        NativeMenuItem item = _dropdowns[level].Items[row];
        if (!item.Enabled) return;
        if (item.Kind == NativeMenuItemKind.Command) Choose(item.Id);
        else if (item.Kind == NativeMenuItemKind.Popup) OpenSubmenu(level, row, selectFirst: true);
    }

    private static int Step(Dropdown dropdown, int direction)
    {
        int count = dropdown.Items.Count;
        int index = dropdown.Selected;
        for (int i = 0; i < count; i++)
        {
            index = index < 0 ? (direction > 0 ? 0 : count - 1) : (index + direction + count) % count;
            if (dropdown.Items[index].Kind != NativeMenuItemKind.Separator) return index;
        }
        return dropdown.Selected;
    }

    // ---- menu state ----

    private int BarItemAt(Vector2I p)
    {
        for (int i = 0; i < _barRects.Length; i++)
            if (p.X >= _barRects[i].Position.X && p.X < _barRects[i].End.X && p.Y >= 0 && p.Y < BarHeight) return i;
        return -1;
    }

    /// <summary>The topmost dropdown under <paramref name="p"/> and its row (-1 on the frame), or (-1, -1).</summary>
    private (int Level, int Row) DropdownAt(Vector2I p)
    {
        for (int level = _dropdowns.Count - 1; level >= 0; level--)
        {
            Dropdown dropdown = _dropdowns[level];
            if (!dropdown.Rect.HasPoint(p)) continue;
            int y = p.Y - dropdown.Rect.Position.Y;
            for (int row = 0; row < dropdown.Items.Count; row++)
                if (y >= dropdown.RowTops[row] && y < dropdown.RowTops[row] + dropdown.RowHeightOf(row))
                    return (level, row);
            return (level, -1);
        }
        return (-1, -1);
    }

    /// <summary>Select bar position <paramref name="position"/> with no dropdown open, starting the loop if needed.
    /// </summary>
    private void SelectBar(int position)
    {
        if (!StartLoop(position)) return;
        _keyboardMode = true;
        _openBar = position;
        _dropdowns.Clear();
        _pendingSubmenu = null;
        QueueRedraw();
    }

    /// <summary>The loop starts (WM_INITMENU) unless it runs. False when a handler ended it at once. The window
    /// looks active from then on, as native's does when a key starts the loop in its inactive window; only the
    /// window losing the focus (<see cref="SetActive"/>) grays the bar again.</summary>
    private bool StartLoop(int position)
    {
        if (_openBar >= 0) return true;
        _active = true;
        _hotBar = -1;
        _openBar = position;   // set first, so a handler that closes the menu sees it open
        Opened?.Invoke();
        if (_openBar < 0) return false;
        SetProcess(true);
        return true;
    }

    private void OpenBar(int bar, bool keyboard)
    {
        if (bar < 0 || bar >= PositionCount) return;
        _hotBar = -1;
        _keyboardMode = keyboard;
        if (!StartLoop(bar)) return;
        _openBar = bar;
        _dropdowns.Clear();
        _pendingSubmenu = null;
        if (bar == SystemPosition)
        {
            // The window menu opens at the window's top-left, a row above the bar (native: window y 25, the bar 26).
            _dropdowns.Add(Layout(SystemMenu!, new Vector2I(0, -1), null));
        }
        else if (_items[bar] is { Kind: NativeMenuItemKind.Popup, Children.Count: > 0 } item)
        {
            Rect2I owner = _barRects[bar];
            _dropdowns.Add(Layout(item.Children, new Vector2I(owner.Position.X, BarItemHeight), null));
        }
        if (keyboard && _dropdowns.Count > 0) _dropdowns[0].Selected = Step(_dropdowns[0], 1);
        QueueRedraw();
    }

    private void OpenSubmenu(int level, int row, bool selectFirst)
    {
        Dropdown parent = _dropdowns[level];
        NativeMenuItem item = parent.Items[row];
        if (item.Kind != NativeMenuItemKind.Popup || item.Children.Count == 0) return;
        CloseFrom(level + 1);
        parent.Selected = row;
        var at = new Vector2I(parent.Rect.End.X - SubmenuOverlap,
                              parent.Rect.Position.Y + parent.RowTops[row] - PopupInset);
        Dropdown sub = Layout(item.Children, at, parent.Rect);
        if (selectFirst) sub.Selected = Step(sub, 1);
        _dropdowns.Add(sub);
        _pendingSubmenu = null;
        QueueRedraw();
    }

    private void CloseFrom(int level)
    {
        if (level < _dropdowns.Count) _dropdowns.RemoveRange(level, _dropdowns.Count - level);
        QueueRedraw();
    }

    private void Select(int level, int row, bool hover)
    {
        Dropdown dropdown = _dropdowns[level];
        if (dropdown.Selected != row)
        {
            dropdown.Selected = row;
            QueueRedraw();
        }
        // The submenu of the newly hovered item opens, and an unrelated open one closes, after the menu show delay.
        bool ownsNext = level + 1 < _dropdowns.Count && row >= 0
                        && ReferenceEquals(dropdown.Items[row].Children, _dropdowns[level + 1].Items);
        if (ownsNext)
        {
            CloseFrom(level + 2);
            _pendingSubmenu = null;
            return;
        }
        if (row >= 0 && dropdown.Items[row].Kind == NativeMenuItemKind.Popup && dropdown.Items[row].Enabled)
        {
            if (!hover) OpenSubmenu(level, row, selectFirst: false);
            else if (_pendingSubmenu is not { } pending || pending.Level != level || pending.Index != row)
                _pendingSubmenu = (level, row, SubmenuDelaySeconds);
        }
        else if (level + 1 < _dropdowns.Count)
        {
            if (!hover) CloseFrom(level + 1);
            else if (_pendingSubmenu is not { } pending || pending.Level != level || pending.Index != -1)
                _pendingSubmenu = (level, -1, SubmenuDelaySeconds);
        }
        else
        {
            _pendingSubmenu = null;
        }
    }

    public override void _Process(double delta)
    {
        if (_pendingSubmenu is not { } pending) return;
        double remaining = pending.Remaining - delta;
        if (remaining > 0)
        {
            _pendingSubmenu = pending with { Remaining = remaining };
            return;
        }
        _pendingSubmenu = null;
        if (pending.Level >= _dropdowns.Count) return;
        if (pending.Index < 0) CloseFrom(pending.Level + 1);
        else if (_dropdowns[pending.Level].Selected == pending.Index) OpenSubmenu(pending.Level, pending.Index, false);
    }

    private Dropdown Layout(List<NativeMenuItem> items, Vector2I at, Rect2I? parent)
    {
        int label = 0, accelerator = 0;
        foreach (NativeMenuItem item in items)
        {
            if (item.Kind == NativeMenuItemKind.Separator) continue;
            Win32Font font = FontOf(item);
            label = Math.Max(label, (int)font.Width(item.Label));
            accelerator = Math.Max(accelerator, (int)font.Width(item.Accelerator));
        }
        int width = label + accelerator + PopupExtraWidth;
        var tops = new int[items.Count];
        int y = PopupInset;
        for (int i = 0; i < items.Count; i++)
        {
            tops[i] = y;
            y += items[i].Kind == NativeMenuItemKind.Separator ? SeparatorHeight : RowHeight;
        }
        int height = y + PopupInset;
        // The port's window is the screen: a dropdown that would leave it moves back inside, and a submenu that
        // would leave on the right opens on its parent's left instead.
        Rect2I screen = Screen ?? new Rect2I(0, 0, (int)Size.X, (int)Size.Y);
        int x = at.X;
        if (x + width > screen.End.X)
            x = parent is { } p ? p.Position.X - width + SubmenuOverlap : screen.End.X - width;
        x = Math.Max(screen.Position.X, x);
        int top = Math.Max(screen.Position.Y, Math.Min(at.Y, screen.End.Y - height));
        return new Dropdown
        {
            Items = items,
            Rect = new Rect2I(x, top, width, height),
            RowTops = tops,
            AcceleratorX = LabelLeft + label + AcceleratorGap,
        };
    }

    // ---- drawing ----

    public override void _Draw()
    {
        DrawBar();
        foreach (Dropdown dropdown in _dropdowns) DrawDropdown(dropdown);
    }

    private void DrawBar()
    {
        int width = (int)Size.X;
        DrawRect(new Rect2(0, 0, width, BarItemHeight - 1), BarBackground);
        DrawRect(new Rect2(0, BarItemHeight - 1, width, 1), BarLine1);
        DrawRect(new Rect2(0, BarItemHeight, width, 1), BarLine2);
        for (int i = 0; i < _items.Count && i < _barRects.Length; i++)
        {
            Rect2I r = _barRects[i];
            bool pushed = i == _openBar, hot = !pushed && i == _hotBar && _items[i].Enabled;
            if (pushed || hot)
            {
                Color fill = pushed ? BarPushedFill : BarHotFill, border = pushed ? BarPushedBorder : BarHotBorder;
                DrawRect(new Rect2(r.Position.X, 0, r.Size.X, BarItemHeight - 1), border);
                DrawRect(new Rect2(r.Position.X + 1, 1, r.Size.X - 2, BarItemHeight - 2), fill);
                DrawRect(new Rect2(r.Position.X, BarItemHeight - 1, r.Size.X, 1), pushed ? BarPushedBottom : BarHotBottom);
            }
            Color color = _items[i].Enabled && _active ? Text : GrayText;
            _font.Draw(this, new Vector2(r.Position.X + BarItemPadding, BarTextTop), _items[i].Label, color,
                       mnemonic: true, underline: _keyboardMode);
        }
    }

    private void DrawDropdown(Dropdown dropdown)
    {
        Rect2I rect = dropdown.Rect;
        DrawShadow(rect);
        int x0 = rect.Position.X, y0 = rect.Position.Y, w = rect.Size.X, h = rect.Size.Y;
        DrawRect(new Rect2(x0, y0, w, h), PopupBorder);
        DrawRect(new Rect2(x0 + 1, y0 + 1, w - 2, h - 2), PopupBackground);
        DrawRect(new Rect2(x0 + PopupInset, y0 + PopupInset, GutterWidth, h - 2 * PopupInset), Gutter);
        for (int i = 0; i < dropdown.Items.Count; i++)
        {
            NativeMenuItem item = dropdown.Items[i];
            int top = y0 + dropdown.RowTops[i];
            if (item.Kind == NativeMenuItemKind.Separator)
            {
                DrawRect(new Rect2(x0 + PopupInset + GutterWidth, top + 3, w - 2 * PopupInset - GutterWidth, 1),
                         SeparatorLine);
                continue;
            }
            // A selected item is hot; a grayed one is selected too (the keyboard stops on it) and shows gray.
            bool selected = i == dropdown.Selected, hot = selected && item.Enabled;
            if (selected)
            {
                DrawRect(new Rect2(x0 + PopupInset, top, GutterWidth, RowHeight),
                         item.Enabled ? ItemHotGutter : ItemGrayedHotGutter);
                DrawRect(new Rect2(x0 + PopupInset + GutterWidth, top, w - 2 * PopupInset - GutterWidth, RowHeight),
                         item.Enabled ? ItemHot : ItemGrayedHot);
            }
            if (item.Checked)
            {
                DrawRect(new Rect2(x0 + PopupInset, top, RowHeight, RowHeight), hot ? CheckBoxHot : CheckBox);
                DrawMask(CheckMask, x0 + 9, top + 6);
            }
            if (item.Bitmap != 0) WindowSystemMenu.DrawBitmap(this, item.Bitmap, x0 + PopupInset, top, item.Enabled);
            Color color = item.Enabled ? Text : GrayText;
            Win32Font font = FontOf(item);
            font.Draw(this, new Vector2(x0 + LabelLeft, top + RowTextTop), item.Label, color,
                      mnemonic: true, underline: _keyboardMode);
            if (item.Accelerator.Length > 0)
                font.Draw(this, new Vector2(x0 + dropdown.AcceleratorX, top + RowTextTop), item.Accelerator, color,
                          mnemonic: false);
            if (item.Kind == NativeMenuItemKind.Popup) DrawMask(ArrowMask, x0 + w - 17, top + 7);
        }
    }

    /// <summary>The default item (MFS_DEFAULT) is drawn bold.</summary>
    private Win32Font FontOf(NativeMenuItem item) => item.Default ? _boldFont ??= Win32DialogStyle.MenuBoldFont : _font;

    private void DrawMask(byte[,] mask, int x, int y)
    {
        for (int row = 0; row < mask.GetLength(0); row++)
            for (int column = 0; column < mask.GetLength(1); column++)
                if (mask[row, column] != 0)
                    DrawRect(new Rect2(x + column, y + row, 1, 1), Glyph with { A = mask[row, column] / 255f });
    }

    private void DrawShadow(Rect2I rect)
    {
        int left = rect.Position.X, top = rect.Position.Y, right = rect.End.X, bottom = rect.End.Y;
        for (int d = 0; d < 5; d++)
        {
            // Right edge: ramp rows from top + 5, then the full band down to the bottom edge.
            for (int r = 0; r < 5; r++) Shade(right + d, top + 5 + r, 1, 1, Ramp[r, d]);
            Shade(right + d, top + 10, 1, bottom - top - 10, Full[d]);
            // Bottom edge: ramp columns from left + 5, then the full band across to the right edge.
            for (int c = 0; c < 5; c++) Shade(left + 5 + c, bottom + d, 1, 1, Ramp[c, d]);
            Shade(left + 10, bottom + d, right - left - 10, 1, Full[d]);
            for (int c = 0; c < 5; c++) Shade(right + c, bottom + d, 1, 1, Corner[d, c]);
        }
    }

    private void Shade(int x, int y, int w, int h, float alpha)
    {
        if (w > 0 && h > 0 && alpha > 0) DrawRect(new Rect2(x, y, w, h), new Color(0, 0, 0, alpha));
    }
}

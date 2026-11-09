using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ome.Engine.Frontend.Shell;
using Godot;

/// <summary>
/// The window menu (system menu) at the end of the menu bar's keyboard cycle. Native AGE gets it from Windows:
/// Left from ｹﾞｰﾑ, or Right from the last bar item, selects the window-menu position. Down, Up or Enter there, Alt+Space,
/// or Left/Right with a dropdown open shows it at the window's top-left (docs/engine-re.md, "Native menu bar", keyboard
/// mode). The port draws it in <see cref="NativeMenuBar"/> like the bar's dropdowns.
/// <para>Items and states as captured on 2026-11-06. A window has Restore and Size grayed. The fullscreen strip's menu
/// (AGE in fullscreen) has Size and Maximize grayed. Close is the default item (bold). The labels are the OS's: on
/// Windows they come from the port window's own window menu, so they follow the display language as native's do.
/// Elsewhere they are the English ones.</para>
/// </summary>
public static class WindowSystemMenu
{
    public const int Restore = 0xF120, Move = 0xF010, Size = 0xF000, Minimize = 0xF020, Maximize = 0xF030,
        Close = 0xF060;   // SC_*

    // HBMMENU_POPUP_*: the glyphs Windows draws in the check-mark column.
    private const int BitmapRestore = 8, BitmapClose = 9, BitmapMaximize = 10, BitmapMinimize = 11;

    private static readonly (int Id, string English, int Bitmap)[] Layout =
    {
        (Restore, "&Restore", BitmapRestore),
        (Move, "&Move", 0),
        (Size, "&Size", 0),
        (Minimize, "Mi&nimize", BitmapMinimize),
        (Maximize, "Ma&ximize", BitmapMaximize),
        (0, "", 0),
        (Close, "&Close\tAlt+F4", BitmapClose),
    };

    private static Dictionary<int, string>? _labels;

    /// <summary>True for a window-menu command id (SC_*), as opposed to a menu-110 command.</summary>
    public static bool IsCommand(int id) => id >= 0xF000;

    /// <summary>The window menu with native's states: a window's (<paramref name="fullscreen"/> false), or the
    /// fullscreen strip's.</summary>
    public static List<MenuItem> Create(bool fullscreen)
    {
        _labels ??= ReadLabels();
        var items = new List<MenuItem>();
        foreach ((int id, string english, int bitmap) in Layout)
        {
            if (id == 0)
            {
                items.Add(new MenuItem(MenuItemKind.Separator, 0, ""));
                continue;
            }
            bool grayed = id == Size || id == (fullscreen ? Maximize : Restore);
            items.Add(new MenuItem(MenuItemKind.Command, id, _labels.GetValueOrDefault(id, english), grayed)
            {
                Default = id == Close,
                Bitmap = bitmap,
            });
        }
        return items;
    }

    /// <summary>The labels of the port window's own window menu (GetMenuString), which Windows localizes; none off
    /// Windows or for an item the menu lacks.</summary>
    private static Dictionary<int, string> ReadLabels()
    {
        var labels = new Dictionary<int, string>();
        if (!OperatingSystem.IsWindows()) return labels;
        IntPtr window = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        IntPtr menu = window == IntPtr.Zero ? IntPtr.Zero : GetSystemMenu(window, false);
        if (menu == IntPtr.Zero) return labels;
        var buffer = new char[128];
        foreach ((int id, _, _) in Layout)
        {
            if (id == 0) continue;
            int length = GetMenuStringW(menu, (uint)id, buffer, buffer.Length, 0 /* MF_BYCOMMAND */);
            if (length > 0) labels[id] = new string(buffer, 0, length);
        }
        return labels;
    }

    /// <summary>SC_MOVE: Windows' keyboard move loop on the port's window, as DefWindowProc runs it for AGE's.</summary>
    public static void StartKeyboardMove()
    {
        if (!OperatingSystem.IsWindows()) return;
        IntPtr window = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (window != IntPtr.Zero) PostMessageW(window, 0x112 /* WM_SYSCOMMAND */, (IntPtr)Move, IntPtr.Zero);
    }

    // The glyphs, captured from the native window menu (PrintWindow, 2026-11-06): opaque gray levels, 0 transparent,
    // with each one's offset in its 22 px row. An enabled glyph shades from 100 at its top to about 40 at its bottom; a
    // grayed one is flat 0xb6.
    private static readonly byte[,] RestoreGlyph =
    {
        { 0, 0, 100, 100, 100, 100, 100, 100 },
        { 0, 0, 0, 0, 0, 0, 0, 93 },
        { 82, 82, 82, 82, 83, 82, 0, 82 },
        { 70, 70, 70, 70, 70, 70, 0, 70 },
        { 58, 58, 0, 0, 58, 58, 0, 58 },
        { 58, 58, 0, 0, 58, 58, 0, 58 },
        { 48, 47, 47, 47, 47, 48, 0, 0 },
        { 40, 40, 40, 40, 40, 40, 0, 0 },
    };
    private static readonly byte[,] MinimizeGlyph =
    {
        { 63, 63, 63, 62, 63, 63, 63, 62 },
        { 49, 49, 49, 49, 49, 49, 49, 49 },
    };
    private static readonly byte[,] MaximizeGlyph =
    {
        { 100, 100, 100, 100, 100, 100, 100, 100 },
        { 93, 93, 93, 93, 93, 92, 92, 93 },
        { 82, 82, 0, 0, 0, 0, 82, 83 },
        { 76, 76, 0, 0, 0, 0, 76, 77 },
        { 70, 70, 0, 0, 0, 0, 70, 70 },
        { 58, 58, 58, 58, 58, 58, 57, 57 },
        { 47, 48, 47, 47, 47, 47, 47, 48 },
    };
    private static readonly byte[,] CloseGlyph =
    {
        { 100, 100, 100, 0, 100, 100, 100 },
        { 0, 92, 93, 93, 93, 93, 0 },
        { 0, 0, 70, 70, 70, 0, 0 },
        { 0, 0, 66, 66, 66, 0, 0 },
        { 0, 58, 57, 57, 58, 58, 0 },
        { 47, 48, 48, 0, 47, 48, 47 },
    };
    private const int GlyphLeft = 9;   // past the 3 px inset: x 12 in the dropdown
    private const byte GrayedLevel = 0xb6;

    /// <summary>Draw <paramref name="bitmap"/>'s glyph for the row whose top is <paramref name="rowTop"/> in a
    /// dropdown whose inner left edge (past its border and padding) is <paramref name="innerLeft"/>.</summary>
    public static void DrawBitmap(CanvasItem canvas, int bitmap, int innerLeft, int rowTop, bool enabled)
    {
        (byte[,]? glyph, int top) = bitmap switch
        {
            BitmapRestore => (RestoreGlyph, 7),
            BitmapMinimize => (MinimizeGlyph, 13),
            BitmapMaximize => (MaximizeGlyph, 8),
            BitmapClose => (CloseGlyph, 9),
            _ => ((byte[,]?)null, 0),
        };
        if (glyph == null) return;
        for (int row = 0; row < glyph.GetLength(0); row++)
            for (int column = 0; column < glyph.GetLength(1); column++)
            {
                byte level = glyph[row, column];
                if (level == 0) continue;
                if (!enabled) level = GrayedLevel;
                canvas.DrawRect(new Rect2(innerLeft + GlyphLeft + column, rowTop + top + row, 1, 1),
                                Color.Color8(level, level, level));
            }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(IntPtr menu, uint item, [Out] char[] text, int maxCount, uint flags);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

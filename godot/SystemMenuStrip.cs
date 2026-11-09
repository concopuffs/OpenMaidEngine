using System;
using System.Collections.Generic;
using Ome.Engine.Frontend.Shell;
using Godot;

/// <summary>
/// The fullscreen system-menu strip, as AGERC's (host command 4, agerc_system_menu_strip_dialog_proc@0x10003c20;
/// docs/engine-re.md "Screen mode"): a maximized captioned popup at the top of the screen holding the game's title and
/// an icon, the minimize, restore and close buttons, and the shell's pruned menu bar. It is SM_CXSCREEN wide and
/// SM_CYCAPTION + 2 × SM_CYFIXEDFRAME + SM_CYMENU (49) tall, and Windows 10 draws its frame's outer 2 px on the left,
/// right and bottom transparent. The port draws it in game pixels over the top of the game, as native's default
/// fullscreen (an 800×600 display mode, where SM_CXSCREEN is the canvas width) shows it. Layout and glyphs come from a
/// PrintWindow capture of the native strip (2026-11-06).
/// <para>Main routes input here while the strip shows (<see cref="HandleInput"/>). The caption buttons raise
/// <see cref="Minimize"/>, <see cref="Restore"/> and <see cref="CloseRequested"/>; the menu bar is a
/// <see cref="NativeMenuBar"/> whose commands Main runs.</para>
/// </summary>
public partial class SystemMenuStrip : Control
{
    public const int WindowHeight = 49;          // 23 + 2 × 3 + 20
    private const int InvisibleBorder = 2;       // the fixed frame's transparent outer part (left, right, bottom)
    private const int CaptionTop = 1, CaptionHeight = Win32Dialog.CaptionHeight;
    private const int MenuTop = CaptionTop + CaptionHeight;    // 26
    private const int ButtonWidth = 46;
    private const int IconLeft = 5, IconTop = 5, IconSize = 16;
    private const int TitleLeft = 26;            // the caption text's origin: its strokes start at native's (x 27)
    private const int GlyphSize = 10, GlyphTop = 8, MinimizeGlyphRow = 13;

    private static readonly Color ButtonHover = Color.Color8(0xe5, 0xe5, 0xe5);
    private static readonly Color ButtonPressed = Color.Color8(0xcc, 0xcc, 0xcc);

    // The restore glyph of a maximized window's caption: two 8 px squares, the back one up and to the right.
    private static readonly string[] RestoreMask =
    {
        "..########",
        "..#......#",
        "########.#",
        "#......#.#",
        "#......#.#",
        "#......#.#",
        "#......#.#",
        "#......###",
        "#......#..",
        "########..",
    };

    // The ✕, as Windows 10 antialiases it ('#' full, 'o' 250/255, '+' 51/255; Win32DialogFrame draws the same mask).
    private static readonly string[] CloseMask =
    {
        "#+......+#",
        "+o+....+o+",
        ".+o+..+o+.",
        "..+o++o+..",
        "...+oo+...",
        "...+oo+...",
        "..+o++o+..",
        ".+o+..+o+.",
        "+o+....+o+",
        "#+......+#",
    };

    private enum CaptionButton { None, Minimize, Restore, Close }

    private readonly string _title;
    private readonly Texture2D? _icon;
    private CaptionButton _hover, _pressed;
    private bool _captionPress;   // a press on the caption or a button: its release belongs to the strip

    /// <summary>The caption's minimize button: SC_MINIMIZE.</summary>
    public event Action? Minimize;
    /// <summary>The caption's restore button or a double click on the caption: SC_RESTORE.</summary>
    public event Action? Restore;
    /// <summary>The close button, Alt+F4 or Escape: IDCANCEL, which posts WM_CLOSE to AGE's window.</summary>
    public event Action? CloseRequested;

    public NativeMenuBar Bar { get; }

    public SystemMenuStrip(List<MenuItem> items, string title, Texture2D? icon)
    {
        _title = title;
        _icon = icon;
        MouseFilter = MouseFilterEnum.Ignore;
        Bar = new NativeMenuBar(items) { Position = new Vector2(InvisibleBorder + 1, MenuTop) };
        AddChild(Bar);
    }

    /// <summary>Lay the strip out across a screen of <paramref name="screen"/> game pixels.</summary>
    public void LayoutStrip(Vector2I screen)
    {
        Position = Vector2.Zero;
        Size = new Vector2(screen.X, WindowHeight);
        int barLeft = InvisibleBorder + 1;
        Bar.Size = new Vector2(screen.X - 2 * barLeft, Math.Max(NativeMenuBar.BarHeight, screen.Y - MenuTop));
        Bar.Screen = new Rect2I(-barLeft, -MenuTop, screen.X, screen.Y);
        Bar.LayoutBar((int)Bar.Size.X);
        QueueRedraw();
    }

    /// <summary>The visible strip, inside the transparent part of its frame.</summary>
    private Rect2I VisibleRect => new(InvisibleBorder, 0, (int)Size.X - 2 * InvisibleBorder, WindowHeight - InvisibleBorder);

    /// <summary>True when <paramref name="p"/> (viewport coordinates) is on the visible strip or an open dropdown.
    /// </summary>
    public bool Covers(Vector2 p) => VisibleRect.HasPoint(ToPoint(p - GlobalPosition)) || Bar.IsOpen;

    private Rect2I ButtonRect(CaptionButton button)
    {
        int right = (int)Size.X - InvisibleBorder - 1;
        int index = button switch { CaptionButton.Close => 1, CaptionButton.Restore => 2, _ => 3 };
        return new Rect2I(right - index * ButtonWidth, CaptionTop, ButtonWidth, CaptionHeight);
    }

    private CaptionButton ButtonAt(Vector2I p)
    {
        foreach (CaptionButton button in new[] { CaptionButton.Close, CaptionButton.Restore, CaptionButton.Minimize })
            if (ButtonRect(button).HasPoint(p)) return button;
        return CaptionButton.None;
    }

    private static Vector2I ToPoint(Vector2 p) => new((int)Math.Floor(p.X), (int)Math.Floor(p.Y));

    /// <summary>Forget hover and press state, as hiding the window does.</summary>
    public void ResetState()
    {
        _hover = _pressed = CaptionButton.None;
        _captionPress = false;
        Bar.Close();
        QueueRedraw();
    }

    // ---- input ----

    /// <summary>A mouse event on the strip: the menu bar takes its own rows, the caption the rest. Main sends only
    /// events the strip <see cref="Covers"/>, and every release while a caption press is held.</summary>
    public void HandleMouse(InputEventMouse e)
    {
        Vector2I p = ToPoint(e.Position - GlobalPosition);
        if (Bar.IsOpen || p.Y >= MenuTop)
        {
            Bar.HandleInput(e);
            if (e is InputEventMouseMotion) SetHover(CaptionButton.None);
            return;
        }
        Bar.HandleInput(new InputEventMouseMotion { Position = new Vector2(-1, -1) });   // the bar loses its hot item
        switch (e)
        {
            case InputEventMouseMotion:
                SetHover(ButtonAt(p));
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                OnCaptionButton(button, p);
                break;
        }
    }

    /// <summary>A release outside the strip after a press on its caption: the press ends without a click.</summary>
    public bool TakeCaptionRelease(InputEventMouseButton button)
    {
        if (!_captionPress || button.Pressed) return false;
        OnCaptionButton(button, ToPoint(button.Position - GlobalPosition));
        return true;
    }

    private void OnCaptionButton(InputEventMouseButton button, Vector2I p)
    {
        CaptionButton under = ButtonAt(p);
        if (button.Pressed)
        {
            _captionPress = true;
            if (under != CaptionButton.None)
            {
                _pressed = under;
                QueueRedraw();
            }
            else if (button.DoubleClick && VisibleRect.HasPoint(p))
            {
                _captionPress = false;
                Restore?.Invoke();   // a double click on a maximized window's caption restores it
            }
            return;
        }
        CaptionButton pressed = _pressed;
        _captionPress = false;
        _pressed = CaptionButton.None;
        QueueRedraw();
        if (pressed == CaptionButton.None || pressed != under) return;
        switch (pressed)
        {
            case CaptionButton.Minimize: Minimize?.Invoke(); break;
            case CaptionButton.Restore: Restore?.Invoke(); break;
            case CaptionButton.Close: CloseRequested?.Invoke(); break;
        }
    }

    private void SetHover(CaptionButton hover)
    {
        if (_hover == hover) return;
        _hover = hover;
        QueueRedraw();
    }

    // ---- drawing ----

    public override void _Draw()
    {
        Rect2I visible = VisibleRect;
        DrawRect(visible, Win32DialogStyle.Frame);
        int captionWidth = visible.Size.X - 2;
        DrawRect(new Rect2(visible.Position.X + 1, CaptionTop, captionWidth, CaptionHeight), Win32DialogStyle.Caption);
        if (_icon != null) DrawTextureRect(_icon, new Rect2(IconLeft, IconTop, IconSize, IconSize), false);
        Win32Font font = Win32DialogStyle.CaptionFont;
        float top = Mathf.Round(CaptionTop + (CaptionHeight - font.Height) / 2f) - 1;
        font.Draw(this, new Vector2(TitleLeft, top), _title, Win32DialogStyle.CaptionText, mnemonic: false);

        foreach (CaptionButton button in new[] { CaptionButton.Minimize, CaptionButton.Restore, CaptionButton.Close })
        {
            Rect2I rect = ButtonRect(button);
            bool pressed = _pressed == button && _hover == button, hover = _hover == button || _pressed == button;
            Color glyph = Win32DialogStyle.CaptionText;
            if (button == CaptionButton.Close && hover)
            {
                DrawRect(rect, pressed ? Win32DialogStyle.ClosePressed : Win32DialogStyle.CloseHover);
                glyph = Colors.White;
            }
            else if (hover)
            {
                DrawRect(rect, pressed ? ButtonPressed : ButtonHover);
            }
            // Each glyph is 10 px wide and starts 18 px into its button; the minimize line starts one column later
            // (captured 31, 77 and 122 px left of the strip's right edge).
            int x = rect.Position.X + (ButtonWidth - GlyphSize) / 2;
            switch (button)
            {
                case CaptionButton.Minimize:
                    DrawRect(new Rect2(x + 1, MinimizeGlyphRow, GlyphSize, 1), glyph);
                    break;
                case CaptionButton.Restore:
                    DrawMask(RestoreMask, x, GlyphTop, glyph);
                    break;
                case CaptionButton.Close:
                    DrawMask(CloseMask, x, GlyphTop, glyph);
                    break;
            }
        }
    }

    private void DrawMask(string[] mask, int x0, int y0, Color color)
    {
        for (int y = 0; y < mask.Length; y++)
        for (int x = 0; x < mask[y].Length; x++)
        {
            float alpha = mask[y][x] switch { '#' => 1f, 'o' => 250 / 255f, '+' => 51 / 255f, _ => 0f };
            if (alpha > 0) Win32DialogStyle.Pixel(this, x0 + x, y0 + y, new Color(color, alpha));
        }
    }
}

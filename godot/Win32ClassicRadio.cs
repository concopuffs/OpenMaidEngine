using Godot;

/// <summary>
/// A classic (unthemed) Win32 radio button: GDI's 12×12 radio glyph one pixel in, vertically centred in a
/// 13 px cell, the label 18 px from the control's left edge, and a dotted focus rectangle around the label that
/// spans the control's height (captured from AGE's 終了 dialog).
/// </summary>
public partial class Win32ClassicRadio : BaseButton
{
    private const int TextLeft = 18;

    // The glyph as captured: a/k are the shadow and dark-shadow arc, w/e the highlight and light arc,
    // o the check dot, and '.' is the face. While pressed, the white well turns COLOR_BTNFACE.
    private static readonly string[] Glyph =
    {
        "....aaaa....",
        "..aakkkkaa..",
        ".akkwwwwkkw.",
        ".akwwwwwwew.",
        "akwwwoowwwew",
        "akwwoooowwew",
        "akwwoooowwew",
        "akwwwoowwwew",
        ".akwwwwwwew.",
        ".aeewwwweew.",
        "..wweeeeww..",
        "....wwww....",
    };

    private readonly string _text;
    private readonly Win32Font _font = Win32DialogStyle.DialogFont;

    public Win32ClassicRadio(string text, ButtonGroup group)
    {
        _text = text;
        ToggleMode = true;
        ButtonGroup = group;
        FocusMode = FocusModeEnum.All;
        Toggled += _ => QueueRedraw();
    }

    public override void _Draw()
    {
        int h = (int)Size.Y;
        bool pressed = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed && !ButtonPressed;
        int top = (h - 13) / 2;
        for (int y = 0; y < Glyph.Length; y++)
        for (int x = 0; x < Glyph[y].Length; x++)
        {
            Color? color = Glyph[y][x] switch
            {
                'a' => Win32DialogStyle.Shadow,
                'k' => Win32DialogStyle.DarkShadow,
                'w' => pressed && IsWell(x, y) ? Win32DialogStyle.Face : Win32DialogStyle.Highlight,
                'e' => Win32DialogStyle.Light,
                'o' => ButtonPressed ? Win32DialogStyle.Text : pressed ? Win32DialogStyle.Face : Win32DialogStyle.Highlight,
                _ => null,
            };
            if (color is { } c) Win32DialogStyle.Pixel(this, 1 + x, top + y, c);
        }

        var dialog = GetWindow() as Win32Dialog;
        _font.Draw(this, new Vector2(TextLeft, (h - _font.Height) / 2), _text, Win32DialogStyle.Text,
                   underline: dialog?.AcceleratorCuesVisible != false);
        if (HasFocus() && dialog?.FocusCuesVisible != false)
        {
            int width = (int)Mathf.Ceil(_font.Width(_text));
            Win32DialogStyle.DrawFocusRect(this, new Rect2I(TextLeft - 1, 0, width + 2, h));
        }
    }

    // The white inside the arcs; the outer white highlight arc keeps its colour while pressed.
    private static bool IsWell(int x, int y) => y is >= 2 and <= 9 && x is >= 2 and <= 9;

    public override void _Notification(int what)
    {
        if (what is (int)NotificationFocusEnter or (int)NotificationFocusExit) QueueRedraw();
    }
}

using Godot;

/// <summary>
/// A classic (unthemed) Win32 push button, as AGE's dialogs draw them: a two-pixel bevel on COLOR_BTNFACE, a
/// 1 px frame on the default button (the focused push button, or the dialog's default while focus is elsewhere),
/// a sunken face while pressed, and a dotted focus rectangle 4 px inside the edge.
/// </summary>
public partial class Win32ClassicButton : BaseButton
{
    private const int FocusInset = 4;

    private readonly string _text;
    private readonly Win32Font _font;

    /// <summary>The dialog's default button (DEFPUSHBUTTON or MB_DEFBUTTONn).</summary>
    public bool IsDefault { get; init; }

    public Win32ClassicButton(string text, Win32Font font)
    {
        _text = text;
        _font = font;
        FocusMode = FocusModeEnum.All;
        ActionMode = ActionModeEnum.Release;
    }

    private Win32Dialog? Dialog => GetWindow() as Win32Dialog;

    private bool ShowsDefaultFrame => HasFocus() || (IsDefault && Dialog?.AnyPushButtonFocused() == false);

    public override void _Draw()
    {
        int w = (int)Size.X, h = (int)Size.Y;
        bool pressed = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        var r = new Rect2I(0, 0, w, h);
        DrawRect(r, Win32DialogStyle.Face);
        if (ShowsDefaultFrame)
        {
            Outline(r, Win32DialogStyle.DefaultFrame);
            r = r.Grow(-1);
        }
        if (pressed)
            Outline(r, Win32DialogStyle.Shadow);
        else
        {
            Bevel(r, Win32DialogStyle.Highlight, Win32DialogStyle.DarkShadow);
            Bevel(r.Grow(-1), Win32DialogStyle.Light, Win32DialogStyle.Shadow);
        }

        int textWidth = (int)Mathf.Ceil(_font.Width(_text));
        var topLeft = new Vector2((w - textWidth) / 2, (h - _font.Height) / 2);
        if (pressed) topLeft += Vector2.One;
        _font.Draw(this, topLeft, _text, Win32DialogStyle.Text, underline: Dialog?.AcceleratorCuesVisible != false);

        if (HasFocus() && Dialog?.FocusCuesVisible != false)
            Win32DialogStyle.DrawFocusRect(this, new Rect2I(FocusInset, FocusInset, w - 2 * FocusInset, h - 2 * FocusInset));
    }

    private void Outline(Rect2I r, Color color) => Bevel(r, color, color);

    // Top and left in one colour, bottom and right in the other (the bottom-right pixels belong to the latter).
    private void Bevel(Rect2I r, Color topLeft, Color bottomRight)
    {
        DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X - 1, 1), topLeft);
        DrawRect(new Rect2(r.Position.X, r.Position.Y, 1, r.Size.Y - 1), topLeft);
        DrawRect(new Rect2(r.Position.X, r.End.Y - 1, r.Size.X, 1), bottomRight);
        DrawRect(new Rect2(r.End.X - 1, r.Position.Y, 1, r.Size.Y), bottomRight);
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationFocusEnter or (int)NotificationFocusExit) Dialog?.RedrawButtons();
    }
}

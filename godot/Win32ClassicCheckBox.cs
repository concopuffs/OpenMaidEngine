using Godot;

/// <summary>
/// A classic (unthemed) Win32 auto check box (BS_AUTOCHECKBOX): a sunken 13×13 box at the control's left, vertically
/// centred, with a white 9×9 well (COLOR_BTNFACE while the mouse holds it down) and GDI's check mark; the label
/// 18 px from the control's left edge, and a dotted focus rectangle around the label that spans the control's height
/// (captured from AGERC's 設定 pages, 2026-11-05). A click or Space toggles it and then reports BN_CLICKED
/// (<see cref="BaseButton.Pressed"/>).
/// </summary>
public partial class Win32ClassicCheckBox : BaseButton
{
    private const int BoxSize = 13, TextLeft = 18;

    // The check mark's pixels in the box, row by row: (row, first column, last column).
    private static readonly (int Row, int From, int To)[] Check =
    {
        (3, 9, 9), (4, 8, 9), (5, 3, 3), (5, 7, 9), (6, 3, 4), (6, 6, 8), (7, 3, 7), (8, 4, 6), (9, 5, 5),
    };

    private readonly string _text;
    private readonly Win32Font _font = Win32DialogStyle.DialogFont;

    public Win32ClassicCheckBox(string text)
    {
        _text = text;
        ToggleMode = true;
        FocusMode = FocusModeEnum.All;
        ActionMode = ActionModeEnum.Release;
        Toggled += _ => QueueRedraw();
    }

    public override void _Draw()
    {
        int h = (int)Size.Y;
        int top = (h - BoxSize) / 2;
        Win32Edges.Sunken(this, new Rect2I(0, top, BoxSize, BoxSize));
        bool held = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed && IsHeld();
        DrawRect(new Rect2(2, top + 2, BoxSize - 4, BoxSize - 4), held ? Win32DialogStyle.Face : Win32DialogStyle.Window);
        if (ButtonPressed)
            foreach ((int row, int from, int to) in Check)
                DrawRect(new Rect2(from, top + row, to - from + 1, 1), Win32DialogStyle.Text);

        var dialog = GetWindow() as Win32Dialog;
        _font.Draw(this, new Vector2(TextLeft, (h - _font.Height) / 2), _text, Win32DialogStyle.Text,
                   underline: dialog?.AcceleratorCuesVisible != false);
        if (HasFocus() && dialog?.FocusCuesVisible != false)
        {
            int width = (int)Mathf.Ceil(_font.Width(_text));
            Win32DialogStyle.DrawFocusRect(this, new Rect2I(TextLeft - 1, 0, width + 2, h));
        }
    }

    // GetDrawMode reports Pressed for a toggled-on button too; the well only turns face while the button is held.
    private bool IsHeld() => Input.IsMouseButtonPressed(MouseButton.Left) && IsHovered();

    public override void _Notification(int what)
    {
        if (what is (int)NotificationFocusEnter or (int)NotificationFocusExit) QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left }) QueueRedraw();
    }
}

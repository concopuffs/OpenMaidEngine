using Godot;

/// <summary>
/// The visible Windows 10 frame of a <see cref="Win32Dialog"/>: a 1 px border, a white 25 px caption with the
/// title and the ✕, and a COLOR_BTNFACE client. Dragging the caption moves the dialog. The ✕ is drawn disabled
/// (grey, inert) for message boxes without a cancel answer, as Windows does for MB_ABORTRETRYIGNORE.
/// </summary>
public partial class Win32DialogFrame : Control
{
    // The ✕ glyph is 10×10 px, 21 px from the frame's right edge and 8 px from its top (captured).
    private const int CrossSize = 10, CrossRight = 21, CrossTop = 8;
    private const int CloseButtonWidth = 36;

    private readonly Win32Dialog _dialog;
    private readonly string _caption;
    private readonly bool _closeEnabled;
    private bool _closeHover, _closePressed, _dragging;

    public Win32DialogFrame(Win32Dialog dialog, string caption, bool closeEnabled)
    {
        _dialog = dialog;
        _caption = caption;
        _closeEnabled = closeEnabled;
        MouseFilter = MouseFilterEnum.Stop;
    }

    private Rect2 CloseRect => new(Size.X - 1 - CloseButtonWidth, 1, CloseButtonWidth, Win32Dialog.CaptionHeight);

    public override void _Draw()
    {
        var size = (Vector2I)Size;
        DrawRect(new Rect2(0, 0, size.X, size.Y), Win32DialogStyle.Frame);
        DrawRect(new Rect2(1, 1, size.X - 2, Win32Dialog.CaptionHeight), Win32DialogStyle.Caption);
        DrawRect(new Rect2(1, 1 + Win32Dialog.CaptionHeight, size.X - 2, size.Y - Win32Dialog.CaptionHeight - 2),
            Win32DialogStyle.Face);

        // The caption text is vertically centred in the caption, 5 px from the frame's left edge.
        Win32Font font = Win32DialogStyle.CaptionFont;
        float top = Mathf.Round(1 + (Win32Dialog.CaptionHeight - font.Height) / 2f) - 1;
        font.Draw(this, new Vector2(5, top), _caption, Win32DialogStyle.CaptionText, mnemonic: false);

        Color cross = Win32DialogStyle.CaptionText;
        if (!_closeEnabled) cross = Win32DialogStyle.CloseDisabled;
        else if (_closeHover)
        {
            DrawRect(CloseRect, _closePressed ? Win32DialogStyle.ClosePressed : Win32DialogStyle.CloseHover);
            cross = Colors.White;
        }
        int x0 = size.X - CrossRight;
        for (int i = 0; i < CrossSize; i++)
        {
            Win32DialogStyle.Pixel(this, x0 + i, CrossTop + i, cross);
            Win32DialogStyle.Pixel(this, x0 + CrossSize - 1 - i, CrossTop + i, cross);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
                if (_dragging)
                {
                    _dialog.Position += (Vector2I)motion.Relative;
                    break;
                }
                bool hover = _closeEnabled && CloseRect.HasPoint(motion.Position);
                if (hover != _closeHover) { _closeHover = hover; QueueRedraw(); }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                bool onClose = _closeEnabled && CloseRect.HasPoint(button.Position);
                if (button.Pressed)
                {
                    if (onClose) { _closePressed = true; QueueRedraw(); }
                    else if (button.Position.Y < 1 + Win32Dialog.CaptionHeight) _dragging = true;
                }
                else
                {
                    bool click = _closePressed && onClose;
                    _closePressed = false;
                    _dragging = false;
                    QueueRedraw();
                    if (click) _dialog.PressCloseButton();
                }
                break;
            default:
                return;
        }
        AcceptEvent();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _closeHover && !_closePressed)
        {
            _closeHover = false;
            QueueRedraw();
        }
    }
}

using Godot;

/// <summary>
/// A single-line Win32 edit control as AGERC's dialogs use it: display only (read-only, no caret or selection).
/// Its text sits at the control's top, 1 px in from the left (ES_LEFT), centred (ES_CENTER) or right-aligned
/// (ES_RIGHT), on COLOR_BTNFACE when ES_READONLY (WM_CTLCOLORSTATIC) and on COLOR_WINDOW otherwise. Text wider
/// than the control is clipped (ES_AUTOHSCROLL would scroll it to the caret, which a display never moves).
/// </summary>
public partial class Win32Edit : Control
{
    private const int Margin = 1;

    private readonly Win32Font _font;
    private readonly Win32StaticText.Alignment _alignment;
    private readonly bool _readOnly;
    private string _text;

    public Win32Edit(string text, Win32Font font, Win32StaticText.Alignment alignment, bool readOnly)
    {
        _text = text;
        _font = font;
        _alignment = alignment;
        _readOnly = readOnly;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    /// <summary>The control's text (WM_SETTEXT).</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), _readOnly ? Win32DialogStyle.Face : Win32DialogStyle.Window);
        float width = _font.Width(_text);
        float room = Size.X - 2 * Margin;
        float x = _alignment switch
        {
            Win32StaticText.Alignment.Center => Margin + Mathf.Floor((room - width) / 2),
            Win32StaticText.Alignment.Right => Margin + room - width,
            _ => Margin,
        };
        _font.Draw(this, new Vector2(Mathf.Max(x, Margin), 0), _text, Win32DialogStyle.Text, mnemonic: false);
    }
}

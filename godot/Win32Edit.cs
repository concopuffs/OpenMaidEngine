using Godot;

/// <summary>
/// A single-line Win32 edit control as AGERC's dialogs use it: display only (read-only, no caret or selection).
/// Its text sits at the control's top, 1 px in from the left (ES_LEFT), centred (ES_CENTER) or right-aligned
/// (ES_RIGHT), on COLOR_BTNFACE when ES_READONLY (WM_CTLCOLORSTATIC) and on COLOR_WINDOW otherwise. Text wider
/// than the control is clipped (ES_AUTOHSCROLL would scroll it to the caret, which a display never moves).
/// A template's WS_BORDER becomes WS_EX_CLIENTEDGE (DS_3DLOOK): a 2 px sunken edge. Inside it the text starts 1 px
/// down and 2 px in, and centres in the width up to 1 px from the right (AGERC's 設定 pages, captured 2026-11-05).
/// </summary>
public partial class Win32Edit : Control
{
    private const int Margin = 1;

    private readonly Win32Font _font;
    private readonly Win32StaticText.Alignment _alignment;
    private readonly bool _readOnly;
    private readonly bool _clientEdge;
    private string _text;

    public Win32Edit(string text, Win32Font font, Win32StaticText.Alignment alignment, bool readOnly,
                     bool clientEdge = false)
    {
        _text = text;
        _font = font;
        _alignment = alignment;
        _readOnly = readOnly;
        _clientEdge = clientEdge;
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
        int edge = _clientEdge ? 2 : 0, top = _clientEdge ? edge + 1 : 0;
        int leftMargin = _clientEdge ? Margin + 1 : Margin;
        float width = _font.Width(_text);
        float left = edge + leftMargin, room = Size.X - 2 * edge - leftMargin - Margin;
        float x = _alignment switch
        {
            Win32StaticText.Alignment.Center => left + Mathf.Floor((room - width) / 2),
            Win32StaticText.Alignment.Right => left + room - width,
            _ => left,
        };
        _font.Draw(this, new Vector2(Mathf.Max(x, left), top), _text, Win32DialogStyle.Text, mnemonic: false);
        // Text that runs past the formatting rectangle is clipped at its right margin, under the edge.
        if (!_clientEdge) return;
        DrawRect(new Rect2(Size.X - edge - Margin, edge, Margin, Size.Y - 2 * edge),
                 _readOnly ? Win32DialogStyle.Face : Win32DialogStyle.Window);
        Win32Edges.Sunken(this, new Rect2I(Vector2I.Zero, (Vector2I)Size));
    }
}

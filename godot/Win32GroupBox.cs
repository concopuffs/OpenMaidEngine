using Godot;

/// <summary>
/// A classic Win32 group box (BS_GROUPBOX): an etched frame whose top runs half a text line (tmHeight / 2) below the
/// control's top, and the label on the frame, 9 px in, with 2 px of face cleared on each side of it (captured from
/// AGERC's 設定 pages, 2026-11-05). It takes no input.
/// </summary>
public partial class Win32GroupBox : Control
{
    private const int TextLeft = 9, Gap = 2;

    private readonly string _text;
    private readonly Win32Font _font;

    public Win32GroupBox(string text, Win32Font font)
    {
        _text = text;
        _font = font;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        int top = _font.LineHeight / 2;   // tmHeight / 2
        Win32Edges.Etched(this, new Rect2I(0, top, (int)Size.X, (int)Size.Y - top));
        if (_text.Length == 0) return;
        int width = (int)Mathf.Ceil(_font.Width(_text));
        DrawRect(new Rect2(TextLeft - Gap, 0, width + 2 * Gap, _font.Height), Win32DialogStyle.Face);
        var dialog = GetWindow() as Win32Dialog;
        _font.Draw(this, new Vector2(TextLeft, 0), _text, Win32DialogStyle.Text,
                   underline: dialog?.AcceleratorCuesVisible != false);
    }
}

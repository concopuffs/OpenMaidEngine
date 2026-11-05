using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A Win32 static text control: lines from the control's top, word-wrapped to a width like DrawText's
/// DT_WORDBREAK (<see cref="Win32Font.Wrap"/>), one line every <see cref="Win32Font.LineHeight"/> pixels, aligned as
/// SS_LEFT, SS_CENTER or SS_RIGHT align them. Without SS_NOPREFIX, "&amp;" marks a mnemonic, which is underlined
/// only while the dialog shows keyboard cues.
/// </summary>
public partial class Win32StaticText : Control
{
    public enum Alignment { Left, Center, Right }

    private readonly Win32Font _font;
    private readonly float _wrapWidth;
    private readonly Alignment _alignment;
    private readonly bool _prefix;
    private string _text;
    private List<string> _lines;

    public Win32StaticText(string text, Win32Font font, float wrapWidth, Alignment alignment = Alignment.Left,
                           bool prefix = false)
    {
        _font = font;
        _wrapWidth = wrapWidth;
        _alignment = alignment;
        _prefix = prefix;
        _text = text;
        _lines = Wrap(text);
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
            _lines = Wrap(value);
            QueueRedraw();
        }
    }

    private List<string> Wrap(string text) => _font.Wrap(text, _wrapWidth);

    public int LineCount => _lines.Count;

    /// <summary>The widest line, at most the wrap width unless one word is wider.</summary>
    public float ContentWidth
    {
        get
        {
            float width = 0;
            foreach (string line in _lines) width = Math.Max(width, _font.Width(line));
            return width;
        }
    }

    public override void _Draw()
    {
        bool cues = (GetWindow() as Win32Dialog)?.AcceleratorCuesVisible ?? true;
        for (int i = 0; i < _lines.Count; i++)
        {
            float width = _font.Width(_lines[i]);
            float x = _alignment switch
            {
                Alignment.Center => Mathf.Floor((Size.X - width) / 2),
                Alignment.Right => Size.X - width,
                _ => 0,
            };
            _font.Draw(this, new Vector2(x, i * _font.LineHeight), _lines[i], Win32DialogStyle.Text, mnemonic: _prefix,
                       underline: cues);
        }
    }
}

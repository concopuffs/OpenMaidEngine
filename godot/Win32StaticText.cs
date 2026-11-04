using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A Win32 static text control: left-aligned lines from the control's top, word-wrapped to a width like
/// DrawText's DT_WORDBREAK (<see cref="Win32Font.Wrap"/>), one line every <see cref="Win32Font.Height"/> pixels.
/// </summary>
public partial class Win32StaticText : Control
{
    private readonly Win32Font _font;
    private readonly List<string> _lines;

    public Win32StaticText(string text, Win32Font font, float wrapWidth)
    {
        _font = font;
        _lines = font.Wrap(text, wrapWidth);
        MouseFilter = MouseFilterEnum.Ignore;
    }

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
        for (int i = 0; i < _lines.Count; i++)
            _font.Draw(this, new Vector2(0, i * _font.Height), _lines[i], Win32DialogStyle.Text, mnemonic: false);
    }
}

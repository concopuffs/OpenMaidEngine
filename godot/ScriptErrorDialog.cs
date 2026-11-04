using System;
using Age.Engine.Model;
using Godot;

/// <summary>
/// Godot presentation of AGE's interpreter error box: age_show_message_box@0x409370 mode 7, MessageBoxA style
/// 0x112 (MB_ABORTRETRYIGNORE | MB_ICONHAND | MB_DEFBUTTON2) captioned エラーが発生しました. 再試行 is the default
/// button. Like the Win32 box, it has no cancel and its ✕ is disabled: only the three answers dismiss it.
/// <para>Drawn as Windows 10 draws that box for AGE (<see cref="Win32Dialog"/>): a white message area with the
/// IDI_HAND icon over a COLOR_BTNFACE button strip, in the system message font. The layout values were captured
/// from AGE's box on 2026-11-04. Windows centres the box on the monitor; the port centres it on the game.</para>
/// </summary>
public partial class ScriptErrorDialog : Win32Dialog
{
    // MessageBoxA labels its buttons in the Windows UI language. These are Japanese Windows' labels, which the
    // box's own text names; the driver also accepts the English labels (&Abort, &Retry, &Ignore).
    private static readonly string[] ButtonTexts = { "中止(&A)", "再試行(&R)", "無視(&I)" };
    private static readonly string[] EnglishButtonTexts = { "&Abort", "&Retry", "&Ignore" };
    private static readonly ScriptErrorChoice[] Choices =
        { ScriptErrorChoice.Abort, ScriptErrorChoice.Retry, ScriptErrorChoice.Ignore };

    // Captured layout (client coordinates): the icon at (21,23), the text at (62,23) wrapped at 254 px, 28 px
    // right of the text, 23 px under it; a 40 px button strip with 75×23 buttons 7 px below its top, their left edges 255, 173 and 90 px from the client's right edge.
    private const int TextLeft = 62, TextTop = 23, TextWrap = 254, TextRight = 28, TextBottom = 23;
    private const int StripHeight = 40, ButtonTop = 7, ButtonWidth = 75, ButtonHeight = 23;
    private static readonly int[] ButtonFromRight = { 255, 173, 90 };

    private readonly string _caption;
    private readonly string _text;
    private readonly Win32ClassicButton[] _buttons = new Win32ClassicButton[3];
    private bool _completed;

    public event Action<ScriptErrorChoice>? Completed;

    public ScriptErrorDialog(string caption, string text) : this(caption, text, Layout(text)) { }

    private ScriptErrorDialog(string caption, string text, (Win32StaticText Text, Vector2I Client, int White) layout)
        : base(caption, layout.Client, closeEnabled: false)
    {
        _caption = caption;
        _text = text;
        int width = layout.Client.X;

        Client.AddChild(new ColorRect
        {
            Color = Win32DialogStyle.Window, Size = new Vector2(width, layout.White),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        Client.AddChild(new Win32DialogIcon(Win32DialogIcon.Kind.Hand) { Position = new Vector2(21, 23) });
        layout.Text.Position = new Vector2(TextLeft, TextTop);
        Client.AddChild(layout.Text);

        Win32Font font = Win32DialogStyle.MessageFont;
        for (int i = 0; i < ButtonTexts.Length; i++)
        {
            ScriptErrorChoice choice = Choices[i];
            var button = new Win32ClassicButton(ButtonTexts[i], font)
            {
                IsDefault = i == 1,     // MB_DEFBUTTON2
                Position = new Vector2(width - ButtonFromRight[i], layout.White + ButtonTop),
                Size = new Vector2(ButtonWidth, ButtonHeight),
            };
            button.Pressed += () => Complete(choice);
            Client.AddChild(button);
            _buttons[i] = button;
        }

        WindowInput += OnWindowInput;
    }

    private static (Win32StaticText, Vector2I, int) Layout(string text)
    {
        var message = new Win32StaticText(text.TrimEnd('\n'), Win32DialogStyle.MessageFont, TextWrap);
        int textWidth = (int)Mathf.Ceil(message.ContentWidth) + 2;     // the static is 2 px wider than its widest line
        int textHeight = message.LineCount * Win32DialogStyle.MessageFont.Height + 2;
        message.Size = new Vector2(textWidth, textHeight);
        int white = TextTop + Math.Max(textHeight, 32) + TextBottom;
        int width = Math.Max(TextLeft + textWidth + TextRight, ButtonFromRight[0] + 15);
        return (message, new Vector2I(width, white + StripHeight), white);
    }

    public void Open(Vector2I client)
    {
        _completed = false;
        Vector2I window = NativeWindowSize(Size - new Vector2I(2, CaptionHeight + 2));
        PopupAtNativeWindow((client - window) / 2);
        _buttons[1].GrabFocus();        // MB_DEFBUTTON2
    }

    // The buttons' mnemonics: A, R and I answer at once, as in the Win32 box. Enter, Space, Tab and the arrows
    // reach the focused button through Godot's focus handling.
    private void OnWindowInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        ScriptErrorChoice? choice = key.Keycode switch
        {
            Key.A => ScriptErrorChoice.Abort,
            Key.R => ScriptErrorChoice.Retry,
            Key.I => ScriptErrorChoice.Ignore,
            _ => null,
        };
        if (choice is not { } picked) return;
        Complete(picked);
        SetInputAsHandled();
    }

    // MB_ABORTRETRYIGNORE has no cancel answer: its ✕ is disabled.
    protected override void OnCloseButton() { }

    /// <summary>The dialog as the drive protocol lists it.</summary>
    public System.Text.Json.Nodes.JsonObject Describe()
    {
        var buttons = new System.Text.Json.Nodes.JsonArray();
        foreach (string text in ButtonTexts) buttons.Add(text);
        return new System.Text.Json.Nodes.JsonObject { ["title"] = _caption, ["text"] = _text, ["buttons"] = buttons };
    }

    /// <summary>Press a button by its Japanese or English label (ampersands and case ignored). Returns the label,
    /// or null when none matches.</summary>
    public string? Press(string button)
    {
        string wanted = Normalize(button);
        for (int i = 0; i < ButtonTexts.Length; i++)
        {
            if (Normalize(ButtonTexts[i]) != wanted && Normalize(ButtonTexts[i].Split('(')[0]) != wanted
                && Normalize(EnglishButtonTexts[i]) != wanted)
                continue;
            Complete(Choices[i]);
            return ButtonTexts[i];
        }
        return null;
    }

    private static string Normalize(string text) => text.Replace("&", "").Trim().ToLowerInvariant();

    private void Complete(ScriptErrorChoice choice)
    {
        if (_completed) return;
        _completed = true;
        Hide();
        Completed?.Invoke(choice);
    }
}

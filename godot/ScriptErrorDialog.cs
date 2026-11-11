using System;
using System.Collections.Generic;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Model;
using Godot;

/// <summary>
/// Godot presentation of AGE's interpreter error box: age_show_message_box@0x409370 mode 7, MessageBoxA style
/// 0x112 (MB_ABORTRETRYIGNORE | MB_ICONHAND | MB_DEFBUTTON2) captioned エラーが発生しました. 再試行 is the default
/// button. Like the Win32 box, it has no cancel and its ✕ is disabled: only the three answers dismiss it.
/// <para>The box shows the shell's language (localization spec §11: <see cref="ShellBoxes"/>); the drive protocol
/// lists it in AGE's Japanese, and its buttons answer to the Japanese, the English and the shown labels.</para>
/// <para>Drawn as Windows 10 draws that box for AGE (<see cref="Win32Dialog"/>): a white message area with the
/// IDI_HAND icon over a COLOR_BTNFACE button strip, in the system message font. The layout values were captured
/// from AGE's box on 2026-11-04. Windows centres the box on the monitor; the port centres it on the game.</para>
/// </summary>
public partial class ScriptErrorDialog : Win32Dialog
{
    // MessageBoxA labels its buttons in the Windows UI language. AgeBoxText's are Japanese Windows' labels, which the
    // box's own text names; the driver also accepts English Windows' labels.
    private static readonly string[] JapaneseButtonTexts =
        [.. System.Linq.Enumerable.Select(AgeBoxText.ButtonKeys, AgeBoxText.JapaneseText)];
    private static readonly string[] EnglishButtonTexts = { "&Abort", "&Retry", "&Ignore" };
    private static readonly char[] NativeMnemonics = { 'A', 'R', 'I' };
    private static readonly ScriptErrorChoice[] Choices =
        { ScriptErrorChoice.Abort, ScriptErrorChoice.Retry, ScriptErrorChoice.Ignore };

    // Captured layout (client coordinates): the icon at (21,23), the text at (62,23) wrapped at 254 px, 28 px
    // right of the text, 23 px under it; a 40 px button strip with 75×23 buttons 7 px below its top, their left edges 255, 173 and 90 px from the client's right edge.
    private const int TextLeft = 62, TextTop = 23, TextWrap = 254, TextRight = 28, TextBottom = 23;
    private const int StripHeight = 40, ButtonTop = 7, ButtonWidth = 75, ButtonHeight = 23;
    private static readonly int[] ButtonFromRight = { 255, 173, 90 };

    private readonly DiagnosticMessage _message;
    private readonly IReadOnlyList<string> _buttonTexts;
    private readonly Win32ClassicButton[] _buttons = new Win32ClassicButton[3];
    private bool _completed;

    public event Action<ScriptErrorChoice>? Completed;

    public ScriptErrorDialog(DiagnosticMessage message, ShellStrings strings)
        : this(message, ShellBoxes.Present(message, strings), ShellBoxes.ErrorButtons(strings)) { }

    private ScriptErrorDialog(DiagnosticMessage message, (string Caption, string Text) shown, IReadOnlyList<string> buttons)
        : this(message, shown.Caption, buttons, Layout(shown.Text)) { }

    private ScriptErrorDialog(DiagnosticMessage message, string caption, IReadOnlyList<string> buttons,
                              (Win32StaticText Text, Vector2I Client, int White) layout)
        : base(caption, layout.Client, closeEnabled: false)
    {
        _message = message;
        _buttonTexts = buttons;
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
        for (int i = 0; i < Choices.Length; i++)
        {
            ScriptErrorChoice choice = Choices[i];
            var button = new Win32ClassicButton(_buttonTexts[i], font)
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
        int textHeight = message.LineCount * Win32DialogStyle.MessageFont.LineHeight + 2;
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

    // The buttons' mnemonics answer at once, as in the Win32 box: each label's '&' letter (A, R and I in Japanese
    // and English). Enter, Space, Tab and the arrows reach the focused button through Godot's focus handling.
    private void OnWindowInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        for (int i = 0; i < Choices.Length; i++)
        {
            char mnemonic = MenuItem.MnemonicOf(_buttonTexts[i]) ?? NativeMnemonics[i];
            if ((long)key.Keycode != char.ToUpperInvariant(mnemonic)) continue;
            Complete(Choices[i]);
            SetInputAsHandled();
            return;
        }
    }

    // MB_ABORTRETRYIGNORE has no cancel answer: its ✕ is disabled.
    protected override void OnCloseButton() { }

    /// <summary>The dialog as the drive protocol lists it: in AGE's Japanese, whatever the shell shows.</summary>
    public System.Text.Json.Nodes.JsonObject Describe()
    {
        var buttons = new System.Text.Json.Nodes.JsonArray();
        foreach (string text in JapaneseButtonTexts) buttons.Add(text);
        return new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = _message.Caption, ["text"] = _message.Text, ["buttons"] = buttons,
        };
    }

    /// <summary>Press a button by its Japanese, English or shown label (ampersands and case ignored). Returns the
    /// Japanese label, or null when none matches.</summary>
    public string? Press(string button)
    {
        string wanted = Normalize(button);
        for (int i = 0; i < Choices.Length; i++)
        {
            if (Normalize(JapaneseButtonTexts[i]) != wanted && Normalize(JapaneseButtonTexts[i].Split('(')[0]) != wanted
                && Normalize(EnglishButtonTexts[i]) != wanted && Normalize(_buttonTexts[i]) != wanted)
                continue;
            Complete(Choices[i]);
            return JapaneseButtonTexts[i];
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

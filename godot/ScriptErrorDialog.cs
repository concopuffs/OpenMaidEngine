using System;
using System.Collections.Generic;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Model;
using Godot;

/// <summary>
/// Godot presentation of AGE's interpreter error boxes, captioned エラーが発生しました: age_show_message_box@0x409370
/// mode 7, MessageBoxA style 0x112 (MB_ABORTRETRYIGNORE | MB_ICONHAND | MB_DEFBUTTON2), whose default button is
/// 再試行, and mode 5, style 0x14 (MB_YESNO | MB_ICONHAND), whose default is はい. Like the Win32 boxes, neither has a
/// cancel and the ✕ is disabled: only the answers dismiss them. The script loader's mode 3, style 0x15
/// (MB_RETRYCANCEL | MB_ICONHAND), and the fatal box, mode 1, style 0x10 (MB_OK | MB_ICONHAND, captioned
/// 続行不可能なエラーが発生しました), have a cancel answer, キャンセル or OK: the ✕ and Esc give it, as in Win32.
/// <para>The box shows the shell's language (localization spec §11: <see cref="ShellBoxes"/>); the drive protocol
/// lists it in AGE's Japanese, and its buttons answer to the Japanese, the English and the shown labels.</para>
/// <para>Drawn as Windows 10 draws these boxes for AGE (<see cref="Win32Dialog"/>): a white message area with the
/// IDI_HAND icon over a COLOR_BTNFACE button strip, in the system message font. The layout values were captured
/// from AGE's boxes (mode 7 on 2026-11-04, mode 5 on 2026-11-16). Windows centres the box on the monitor; the port
/// centres it on the game.</para>
/// </summary>
public partial class ScriptErrorDialog : Win32Dialog
{
    // One box's buttons. MessageBoxA labels its buttons in the Windows UI language. AgeBoxText's are Japanese Windows'
    // labels, which the box's own text names; the driver also accepts English Windows' labels. FromRight is each
    // button's left edge, in px from the client's right edge, as captured.
    // Cancel is the answer the ✕ and Esc give ('\0' in Mnemonics: the button has none), null where they do nothing.
    private sealed record Kind(string[] Japanese, string[] English, char[] Mnemonics, ScriptErrorChoice[] Choices,
                               int[] FromRight, int Default, ScriptErrorChoice? Cancel = null);

    private static readonly Kind AbortRetryIgnore = Create(AgeBoxButtons.AbortRetryIgnore,
        ["&Abort", "&Retry", "&Ignore"], ['A', 'R', 'I'], [255, 173, 90], defaultButton: 1);    // MB_DEFBUTTON2
    private static readonly Kind YesNo = Create(AgeBoxButtons.YesNo,
        ["&Yes", "&No"], ['Y', 'N'], [172, 90], defaultButton: 0);
    // Not captured from AGE: laid out as the captured two-button box, and the one button where its right one is.
    private static readonly Kind RetryCancel = Create(AgeBoxButtons.RetryCancel,
        ["&Retry", "Cancel"], ['R', '\0'], [172, 90], defaultButton: 0, cancel: ScriptErrorChoice.Abort);
    private static readonly Kind Fatal = Create(AgeBoxButtons.Fatal,
        ["OK"], ['\0'], [90], defaultButton: 0, cancel: ScriptErrorChoice.Abort);

    private static Kind Create(AgeBoxButtons buttons, string[] english, char[] mnemonics, int[] fromRight,
                               int defaultButton, ScriptErrorChoice? cancel = null)
        => new([.. System.Linq.Enumerable.Select(AgeBoxText.ButtonKeys(buttons), AgeBoxText.JapaneseText)], english,
               mnemonics, [.. AgeBoxText.ButtonChoices(buttons)], fromRight, defaultButton, cancel);

    private static (Kind Kind, AgeBoxButtons Buttons) KindOf(DiagnosticMessage message)
        => message.Parts?.Buttons switch
        {
            AgeBoxButtons.YesNo => (YesNo, AgeBoxButtons.YesNo),
            AgeBoxButtons.RetryCancel => (RetryCancel, AgeBoxButtons.RetryCancel),
            AgeBoxButtons.Fatal => (Fatal, AgeBoxButtons.Fatal),
            _ => (AbortRetryIgnore, AgeBoxButtons.AbortRetryIgnore),
        };

    // Captured layout (client coordinates): the icon at (21,23), the text at (62,23) wrapped at 254 px, 28 px
    // right of the text, 23 px under it; a 40 px button strip with 75×23 buttons 7 px below its top.
    private const int TextLeft = 62, TextTop = 23, TextWrap = 254, TextRight = 28, TextBottom = 23;
    private const int StripHeight = 40, ButtonTop = 7, ButtonWidth = 75, ButtonHeight = 23;

    private readonly DiagnosticMessage _message;
    private readonly Kind _kind;
    private readonly IReadOnlyList<string> _buttonTexts;
    private readonly Win32ClassicButton[] _buttons;
    private bool _completed;

    public event Action<ScriptErrorChoice>? Completed;

    public ScriptErrorDialog(DiagnosticMessage message, ShellStrings strings)
        : this(message, KindOf(message), ShellBoxes.Present(message, strings), strings) { }

    private ScriptErrorDialog(DiagnosticMessage message, (Kind Kind, AgeBoxButtons Buttons) kind,
                              (string Caption, string Text) shown, ShellStrings strings)
        : this(message, kind.Kind, shown.Caption, ShellBoxes.ErrorButtons(kind.Buttons, strings),
               Layout(shown.Text, kind.Kind)) { }

    private ScriptErrorDialog(DiagnosticMessage message, Kind kind, string caption, IReadOnlyList<string> buttons,
                              (Win32StaticText Text, Vector2I Client, int White) layout)
        : base(caption, layout.Client, closeEnabled: kind.Cancel != null)
    {
        _message = message;
        _kind = kind;
        _buttonTexts = buttons;
        _buttons = new Win32ClassicButton[kind.Choices.Length];
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
        for (int i = 0; i < kind.Choices.Length; i++)
        {
            ScriptErrorChoice choice = kind.Choices[i];
            var button = new Win32ClassicButton(_buttonTexts[i], font)
            {
                IsDefault = i == kind.Default,
                Position = new Vector2(width - kind.FromRight[i], layout.White + ButtonTop),
                Size = new Vector2(ButtonWidth, ButtonHeight),
            };
            button.Pressed += () => Complete(choice);
            Client.AddChild(button);
            _buttons[i] = button;
        }

        WindowInput += OnWindowInput;
    }

    private static (Win32StaticText, Vector2I, int) Layout(string text, Kind kind)
    {
        var message = new Win32StaticText(text.TrimEnd('\n'), Win32DialogStyle.MessageFont, TextWrap);
        int textWidth = (int)Mathf.Ceil(message.ContentWidth) + 2;     // the static is 2 px wider than its widest line
        int textHeight = message.LineCount * Win32DialogStyle.MessageFont.LineHeight + 2;
        message.Size = new Vector2(textWidth, textHeight);
        int white = TextTop + Math.Max(textHeight, 32) + TextBottom;
        int width = Math.Max(TextLeft + textWidth + TextRight, kind.FromRight[0] + 15);
        return (message, new Vector2I(width, white + StripHeight), white);
    }

    public void Open(Vector2I client)
    {
        _completed = false;
        Vector2I window = NativeWindowSize(Size - new Vector2I(2, CaptionHeight + 2));
        PopupAtNativeWindow((client - window) / 2);
        _buttons[_kind.Default].GrabFocus();
    }

    // The buttons' mnemonics answer at once, as in the Win32 box: each label's '&' letter (A, R and I, Y and N, or R,
    // in Japanese and English). Esc gives a box's cancel answer. Enter, Space, Tab and the arrows reach the focused
    // button through Godot's focus handling.
    private void OnWindowInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape && _kind.Cancel is { } cancel)
        {
            Complete(cancel);
            SetInputAsHandled();
            return;
        }
        for (int i = 0; i < _kind.Choices.Length; i++)
        {
            char mnemonic = MenuItem.MnemonicOf(_buttonTexts[i]) ?? _kind.Mnemonics[i];
            if (mnemonic == '\0' || (long)key.Keycode != char.ToUpperInvariant(mnemonic)) continue;
            Complete(_kind.Choices[i]);
            SetInputAsHandled();
            return;
        }
    }

    // MB_ABORTRETRYIGNORE and MB_YESNO have no cancel answer: the ✕ is disabled. MB_RETRYCANCEL's and MB_OK's give
    // キャンセル and OK.
    protected override void OnCloseButton()
    {
        if (_kind.Cancel is { } cancel) Complete(cancel);
    }

    /// <summary>The dialog as the drive protocol lists it: in AGE's Japanese, whatever the shell shows.</summary>
    public System.Text.Json.Nodes.JsonObject Describe()
    {
        var buttons = new System.Text.Json.Nodes.JsonArray();
        foreach (string text in _kind.Japanese) buttons.Add(text);
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
        for (int i = 0; i < _kind.Choices.Length; i++)
        {
            if (Normalize(_kind.Japanese[i]) != wanted && Normalize(_kind.Japanese[i].Split('(')[0]) != wanted
                && Normalize(_kind.English[i]) != wanted && Normalize(_buttonTexts[i]) != wanted)
                continue;
            Complete(_kind.Choices[i]);
            return _kind.Japanese[i];
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

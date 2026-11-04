using System;
using Age.Engine.Model;
using Godot;

/// <summary>
/// Godot presentation of AGE's interpreter error box: age_show_message_box@0x409370 mode 7, MessageBoxA style
/// 0x112 (MB_ABORTRETRYIGNORE | MB_ICONHAND | MB_DEFBUTTON2) captioned エラーが発生しました. 再試行 is the default
/// button. Like the Win32 box, it has no cancel and no close button: only the three answers dismiss it.
/// </summary>
public partial class ScriptErrorDialog : Window
{
    // MessageBoxA labels its buttons in the Windows UI language. These are Japanese Windows' labels, which the
    // box's own text names; the driver also accepts the English labels (&Abort, &Retry, &Ignore).
    private static readonly string[] ButtonTexts = { "中止(&A)", "再試行(&R)", "無視(&I)" };
    private static readonly string[] EnglishButtonTexts = { "&Abort", "&Retry", "&Ignore" };
    private static readonly ScriptErrorChoice[] Choices =
        { ScriptErrorChoice.Abort, ScriptErrorChoice.Retry, ScriptErrorChoice.Ignore };

    private readonly string _caption;
    private readonly string _text;
    private readonly Button[] _buttons = new Button[3];
    private bool _completed;

    public event Action<ScriptErrorChoice>? Completed;

    public ScriptErrorDialog(string caption, string text)
    {
        _caption = caption;
        _text = text;
        Title = caption;
        Exclusive = true;
        Transient = true;
        Unresizable = true;
        WrapControls = true;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);
        column.AddChild(new Label { Text = text.TrimEnd('\n') });

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        for (int i = 0; i < ButtonTexts.Length; i++)
        {
            ScriptErrorChoice choice = Choices[i];
            _buttons[i] = new Button { Text = ButtonTexts[i].Replace("&", ""), CustomMinimumSize = new Vector2(96, 0) };
            _buttons[i].Pressed += () => Complete(choice);
            actions.AddChild(_buttons[i]);
        }
        column.AddChild(actions);

        CloseRequested += () => { };    // no close button answer, as with MB_ABORTRETRYIGNORE
        WindowInput += OnWindowInput;
    }

    public void Open()
    {
        _completed = false;
        PopupCentered();
        _buttons[1].GrabFocus();        // MB_DEFBUTTON2
    }

    // The buttons' mnemonics: A, R and I answer at once, as in the Win32 box.
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

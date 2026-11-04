using System;
using Godot;

/// <summary>
/// Godot presentation of AGE's close confirmation: AGERC.DLL dialog resource 3 (title 終了), opened by
/// host command 3 from FUN_00405740 when the window receives an unforced WM_CLOSE (close button, ESC,
/// system-menu command 0x9c41). Radio 0x3ea プログラムを終了する (checked by WM_INITDIALOG) answers 1, radio
/// 0x3eb タイトルに戻る answers 2, and Cancel answers 0.
/// </summary>
public partial class ExitConfirmationDialog : Window
{
    public enum Choice { Cancel = 0, Quit = 1, ReturnToTitle = 2 }

    public const string NativeTitle = "終了";
    public const string Prompt = "次の方法で終了しますか？";
    // The native driver joins a dialog's static texts: the icon control's resource name, then the prompt.
    public const string NativeText = "SYSTEM3\n" + Prompt;
    // AGERC dialog 3's control texts, as the native driver reads them (radio buttons are Button controls too).
    private static readonly string[] NativeButtons =
        { "プログラムを終了する(&E)", "タイトルに戻る(&T)", "OK", "ｷｬﾝｾﾙ" };

    private readonly CheckBox _quit = new() { Text = "プログラムを終了する(E)" };
    private readonly CheckBox _title = new() { Text = "タイトルに戻る(T)" };
    private bool _completed;

    public event Action<Choice>? Completed;

    public ExitConfirmationDialog()
    {
        Title = NativeTitle;
        Exclusive = true;
        Transient = true;
        Unresizable = true;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var column = new VBoxContainer();
        margin.AddChild(column);
        column.AddChild(new Label { Text = Prompt });
        var group = new ButtonGroup();
        _quit.ButtonGroup = group;
        _title.ButtonGroup = group;
        column.AddChild(_quit);
        column.AddChild(_title);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var ok = new Button { Text = "OK", CustomMinimumSize = new Vector2(80, 0) };
        var cancel = new Button { Text = "ｷｬﾝｾﾙ", CustomMinimumSize = new Vector2(80, 0) };
        actions.AddChild(ok);
        actions.AddChild(cancel);
        column.AddChild(actions);

        ok.Pressed += () => Complete(_title.ButtonPressed ? Choice.ReturnToTitle : Choice.Quit);
        cancel.Pressed += () => Complete(Choice.Cancel);
        CloseRequested += () => Complete(Choice.Cancel);
        WindowInput += OnWindowInput;
    }

    public void Open()
    {
        _completed = false;
        _quit.ButtonPressed = true;
        PopupCentered(new Vector2I(300, 150));
        _quit.GrabFocus();
    }

    // The dialog's mnemonics and keys: E and T pick a choice, Enter is OK, Escape is Cancel.
    private void OnWindowInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.E: _quit.ButtonPressed = true; break;
            case Key.T: _title.ButtonPressed = true; break;
            case Key.Enter or Key.KpEnter:
                Complete(_title.ButtonPressed ? Choice.ReturnToTitle : Choice.Quit);
                break;
            case Key.Escape: Complete(Choice.Cancel); break;
            default: return;
        }
        SetInputAsHandled();
    }

    /// <summary>The dialog as the drive protocol lists it, with native AGE's control texts.</summary>
    public System.Text.Json.Nodes.JsonObject Describe()
    {
        var buttons = new System.Text.Json.Nodes.JsonArray();
        foreach (string text in NativeButtons) buttons.Add(text);
        return new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = NativeTitle, ["text"] = NativeText, ["buttons"] = buttons,
        };
    }

    /// <summary>Press a control by its native text (ampersands and case ignored), as BM_CLICK does natively.
    /// Returns the control's text, or null when no control matches.</summary>
    public string? Press(string button)
    {
        string wanted = button.Replace("&", "").ToLowerInvariant();
        string? match = Array.Find(NativeButtons, text => text.Replace("&", "").ToLowerInvariant() == wanted);
        switch (Array.IndexOf(NativeButtons, match))
        {
            case 0: _quit.ButtonPressed = true; break;
            case 1: _title.ButtonPressed = true; break;
            case 2: Complete(_title.ButtonPressed ? Choice.ReturnToTitle : Choice.Quit); break;
            case 3: Complete(Choice.Cancel); break;
            default: return null;
        }
        return match;
    }

    private void Complete(Choice choice)
    {
        if (_completed) return;
        _completed = true;
        Hide();
        Completed?.Invoke(choice);
    }
}

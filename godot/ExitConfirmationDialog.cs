using System;
using Godot;

/// <summary>
/// Godot presentation of AGE's close confirmation: AGERC.DLL dialog resource 3 (title 終了), opened by
/// host command 3 from FUN_00405740 when the window receives an unforced WM_CLOSE (close button, ESC,
/// system-menu command 0x9c41). Radio 0x3ea プログラムを終了する (checked by WM_INITDIALOG) answers 1, radio
/// 0x3eb タイトルに戻る answers 2, and Cancel answers 0.
/// <para>Drawn as the native dialog looks on Windows 10 (<see cref="Win32Dialog"/>): the control rectangles,
/// font and placement are the ones captured from AGE.</para>
/// </summary>
public partial class ExitConfirmationDialog : Win32Dialog
{
    public enum Choice { Cancel = 0, Quit = 1, ReturnToTitle = 2 }

    public const string NativeTitle = "終了";
    public const string Prompt = "次の方法で終了しますか？";
    // The native driver joins a dialog's static texts: the icon control's resource name, then the prompt.
    public const string NativeText = "SYSTEM3\n" + Prompt;
    // AGERC dialog 3's control texts, as the native driver reads them (radio buttons are Button controls too).
    private static readonly string[] NativeButtons =
        { "プログラムを終了する(&E)", "タイトルに戻る(&T)", "OK", "ｷｬﾝｾﾙ" };

    // Captured client size and control rectangles (client coordinates), 2026-11-04.
    private static readonly Vector2I NativeClientSize = new(327, 140);

    private readonly Win32ClassicRadio _quit;
    private readonly Win32ClassicRadio _title;
    private readonly Win32ClassicButton _ok;
    private readonly Win32ClassicButton _cancel;
    private bool _completed;

    public event Action<Choice>? Completed;

    public ExitConfirmationDialog() : base(NativeTitle, NativeClientSize, closeEnabled: true)
    {
        Win32Font font = Win32DialogStyle.DialogFont;
        Place(new Win32DialogIcon(Win32DialogIcon.Kind.Asterisk), 12, 30, 32, 32);
        Place(new Win32StaticText(Prompt, font, 235), 81, 24, 235, 17);
        var group = new ButtonGroup();
        _quit = Place(new Win32ClassicRadio(NativeButtons[0], group), 81, 51, 235, 15);
        _title = Place(new Win32ClassicRadio(NativeButtons[1], group), 81, 72, 235, 18);
        _ok = Place(new Win32ClassicButton(NativeButtons[2], font) { IsDefault = true }, 130, 108, 88, 21);
        _cancel = Place(new Win32ClassicButton(NativeButtons[3], font), 228, 108, 88, 21);

        _ok.Pressed += () => Complete(SelectedChoice);
        _cancel.Pressed += () => Complete(Choice.Cancel);
        CloseRequested += () => Complete(Choice.Cancel);
        WindowInput += OnWindowInput;
    }

    private T Place<T>(T control, int x, int y, int w, int h) where T : Control
    {
        control.Position = new Vector2(x, y);
        control.Size = new Vector2(w, h);
        Client.AddChild(control);
        return control;
    }

    private Choice SelectedChoice => _title.ButtonPressed ? Choice.ReturnToTitle : Choice.Quit;

    /// <summary>Open centred on AGE's outer window over a game client of <paramref name="client"/> pixels.</summary>
    public void Open(Vector2I client)
    {
        _completed = false;
        _quit.ButtonPressed = true;
        PopupCenteredOnAgeWindow(client, NativeClientSize);
        _quit.GrabFocus();
    }

    // The dialog's keys, as Win32's dialog manager handles them: E and T select a radio, the arrows move within
    // the radio group, Tab cycles the checked radio, OK and Cancel, Enter presses the focused push button (else
    // OK, the default), and Escape is Cancel.
    private void OnWindowInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        Control? focus = Client.GetViewport().GuiGetFocusOwner();
        Win32ClassicRadio selected = _title.ButtonPressed ? _title : _quit;
        switch (key.Keycode)
        {
            case Key.E: Select(_quit); break;
            case Key.T: Select(_title); break;
            case Key.Up or Key.Down or Key.Left or Key.Right when focus is Win32ClassicRadio:
                Select(selected == _quit ? _title : _quit);
                break;
            case Key.Tab:
                Control[] stops = { selected, _ok, _cancel };
                int at = Array.IndexOf(stops, focus);
                int step = key.ShiftPressed ? stops.Length - 1 : 1;
                stops[at < 0 ? 0 : (at + step) % stops.Length].GrabFocus();
                break;
            case Key.Enter or Key.KpEnter:
                if (key.Echo) return;
                Complete(focus == _cancel ? Choice.Cancel : SelectedChoice);
                break;
            case Key.Escape:
                if (key.Echo) return;
                Complete(Choice.Cancel);
                break;
            default: return;
        }
        SetInputAsHandled();
    }

    private static void Select(Win32ClassicRadio radio)
    {
        radio.ButtonPressed = true;
        radio.GrabFocus();
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
            case 2: Complete(SelectedChoice); break;
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

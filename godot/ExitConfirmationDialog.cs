using System;
using Ome.Engine.Frontend.Shell;
using Godot;

/// <summary>
/// The shell's 終了 dialog (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §7.3): AGE's close confirmation,
/// which wm_close_confirm_exit_or_title@0x405740 opens through AGERC's dialog 3 when the window receives an unforced
/// WM_CLOSE (close button, ESC, menu command 0x9c41). The first radio (プログラムを終了する, checked at open) answers 1,
/// the second (タイトルに戻る) answers 2, and ｷｬﾝｾﾙ answers 0.
/// <para>Drawn as the native dialog looks on Windows 10 (<see cref="Win32Dialog"/>). The texts come from the shell's
/// string table (<see cref="ShellExitDialog"/>), and the layout is dialog 3's: a 187×93 DLU template in ＭＳ Ｐゴシック
/// 9 pt, at the pixels MapDialogRect gives it (base units 7×12), which match the rectangles captured from AGE (a
/// 327×140 client, 2026-11-04; the clean Kamidori install's translated dialog has the same layout). The driver reads
/// and answers it by its Japanese texts, whatever language it shows.</para>
/// </summary>
public partial class ExitConfirmationDialog : Win32Dialog
{
    public enum Choice { Cancel = 0, Quit = 1, ReturnToTitle = 2 }

    // Dialog 3's controls in client pixels: the SS_ICON static at its 32×32 icon's size, the prompt, the two radios,
    // then OK (the default push button) and ｷｬﾝｾﾙ.
    private static readonly Vector2I ClientSize = new(327, 140);
    private static readonly Rect2I IconRect = new(12, 30, 32, 32);
    private static readonly Rect2I PromptRect = new(81, 24, 235, 17);
    private static readonly Rect2I QuitRect = new(81, 51, 235, 15);
    private static readonly Rect2I ReturnToTitleRect = new(81, 72, 235, 18);
    private static readonly Rect2I OkRect = new(130, 108, 88, 21);
    private static readonly Rect2I CancelRect = new(228, 108, 88, 21);

    private readonly Win32ClassicRadio _quit;
    private readonly Win32ClassicRadio _title;
    private readonly Win32ClassicButton _ok;
    private readonly Win32ClassicButton _cancel;
    private readonly char? _quitKey;
    private readonly char? _titleKey;
    private readonly ShellExitDialog _shown;
    private bool _completed;

    public event Action<Choice>? Completed;

    /// <param name="shown">The texts in the player's language.</param>
    public ExitConfirmationDialog(ShellExitDialog shown)
        : base(shown.Title, ClientSize, closeEnabled: true)   // dialog 3 has WS_SYSMENU
    {
        _shown = shown;
        Win32Font font = Win32DialogStyle.DialogFont;
        Place(new Win32DialogIcon(Win32DialogIcon.Kind.Asterisk), IconRect);
        Place(new Win32StaticText(shown.Prompt, font, PromptRect.Size.X), PromptRect);
        var group = new ButtonGroup();
        _quit = Place(new Win32ClassicRadio(shown.Quit, group), QuitRect);
        _title = Place(new Win32ClassicRadio(shown.ReturnToTitle, group), ReturnToTitleRect);
        _ok = Place(new Win32ClassicButton(shown.Ok, font) { IsDefault = true }, OkRect);
        _cancel = Place(new Win32ClassicButton(shown.Cancel, font), CancelRect);
        _quitKey = MenuItem.MnemonicOf(shown.Quit);
        _titleKey = MenuItem.MnemonicOf(shown.ReturnToTitle);

        _ok.Pressed += () => Complete(SelectedChoice);
        _cancel.Pressed += () => Complete(Choice.Cancel);
        CloseRequested += () => Complete(Choice.Cancel);
        WindowInput += OnWindowInput;
    }

    private T Place<T>(T control, Rect2I rect) where T : Control
    {
        control.Position = rect.Position;
        control.Size = rect.Size;
        Client.AddChild(control);
        return control;
    }

    private Choice SelectedChoice => _title.ButtonPressed ? Choice.ReturnToTitle : Choice.Quit;

    /// <summary>Open centred on AGE's outer window over a game client of <paramref name="client"/> pixels.</summary>
    public void Open(Vector2I client)
    {
        _completed = false;
        _quit.ButtonPressed = true;
        PopupCenteredOnAgeWindow(client, ClientSize);
        _quit.GrabFocus();
    }

    // The dialog's keys, as Win32's dialog manager handles them: a radio's mnemonic (E and T in both languages) selects
    // it, the arrows move within the radio group, Tab cycles the checked radio, OK and Cancel, Enter presses the focused
    // push button (else OK, the default), and Escape is Cancel.
    private void OnWindowInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        Control? focus = Client.GetViewport().GuiGetFocusOwner();
        Win32ClassicRadio selected = _title.ButtonPressed ? _title : _quit;
        char? letter = key.Keycode is >= Key.A and <= Key.Z ? (char)('A' + (key.Keycode - Key.A)) : null;
        if (letter != null && (letter == _quitKey || letter == _titleKey))
        {
            Select(letter == _quitKey ? _quit : _title);
            SetInputAsHandled();
            return;
        }
        switch (key.Keycode)
        {
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

    /// <summary>The texts the driver's <c>dialogs</c> step reads from the native dialog, in Japanese.</summary>
    public string Text => ShellExitDialog.Japanese.DriveText;

    /// <summary>The dialog as the drive protocol lists it: its Japanese texts, as the native driver reads dialog 3.
    /// </summary>
    public System.Text.Json.Nodes.JsonObject Describe()
    {
        ShellExitDialog ja = ShellExitDialog.Japanese;
        var buttons = new System.Text.Json.Nodes.JsonArray();
        foreach (string text in ja.Buttons) buttons.Add(text);
        return new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = ja.Title, ["text"] = ja.DriveText, ["buttons"] = buttons,
        };
    }

    /// <summary>Press a control by its text, Japanese or as shown (ampersands and case ignored), as BM_CLICK does
    /// natively. Returns the control's Japanese text, or null when no control matches.</summary>
    public string? Press(string button)
    {
        string wanted = Normalize(button);
        ShellExitDialog ja = ShellExitDialog.Japanese;
        int index = IndexOf(ja.Buttons, wanted);
        if (index < 0) index = IndexOf(_shown.Buttons, wanted);
        switch (index)
        {
            case 0: _quit.ButtonPressed = true; break;
            case 1: _title.ButtonPressed = true; break;
            case 2: Complete(SelectedChoice); break;
            case 3: Complete(Choice.Cancel); break;
            default: return null;
        }
        return ja.Buttons[index];
    }

    private static string Normalize(string text) => text.Replace("&", "").ToLowerInvariant();

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<string> texts, string wanted)
    {
        for (int i = 0; i < texts.Count; i++)
            if (Normalize(texts[i]) == wanted) return i;
        return -1;
    }

    private void Complete(Choice choice)
    {
        if (_completed) return;
        _completed = true;
        Hide();
        Completed?.Invoke(choice);
    }
}

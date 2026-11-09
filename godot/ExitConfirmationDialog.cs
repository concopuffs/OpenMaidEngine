using System;
using Ome.Engine.Frontend.NativeMenu;
using Ome.Engine.Frontend.NativeResources;
using Godot;

/// <summary>
/// Godot presentation of AGE's close confirmation: AGERC.DLL dialog resource 3 (<see cref="AgercExitDialog"/>; 終了 in
/// Himegari's), opened by host command 3 from wm_close_confirm_exit_or_title@0x405740 when the window receives an
/// unforced WM_CLOSE (close button, ESC, system-menu command 0x9c41). Radio 0x3ea (プログラムを終了する, checked by
/// WM_INITDIALOG) answers 1, radio 0x3eb (タイトルに戻る) answers 2, and Cancel answers 0.
/// <para>Drawn as the native dialog looks on Windows 10 (<see cref="Win32Dialog"/>): its title, texts and control
/// rectangles are the game's template at MapDialogRect's pixels (<see cref="Win32TemplateDialog.BaseUnits"/>), which
/// reproduce the rectangles captured from AGE (327×140 client, 2026-11-04). The SS_ICON static takes its 32×32 icon's
/// size.</para>
/// </summary>
public partial class ExitConfirmationDialog : Win32Dialog
{
    public enum Choice { Cancel = 0, Quit = 1, ReturnToTitle = 2 }

    private const int IconSize = 32;

    private readonly AgercExitDialog _template;
    private readonly Vector2I _clientSize;
    private readonly Win32ClassicRadio _quit;
    private readonly Win32ClassicRadio _title;
    private readonly Win32ClassicButton _ok;
    private readonly Win32ClassicButton _cancel;
    private readonly char? _quitKey;
    private readonly char? _titleKey;
    private bool _completed;

    public event Action<Choice>? Completed;

    public ExitConfirmationDialog(AgercExitDialog template)
        : this(template, Win32TemplateDialog.ClientSizeOf(template.Template)) { }

    private ExitConfirmationDialog(AgercExitDialog template, Vector2I clientSize)
        : base(template.Title, clientSize, closeEnabled: (template.Template.Style & DialogStyles.WsSysMenu) != 0)
    {
        _template = template;
        _clientSize = clientSize;
        Win32Font font = Win32DialogStyle.DialogFont;
        (int iconX, int iconY, _, _) = Rect(template.Icon);
        Place(new Win32DialogIcon(Win32DialogIcon.Kind.Asterisk), (iconX, iconY, IconSize, IconSize));
        (_, _, int promptWidth, _) = Rect(template.Prompt);
        Place(new Win32StaticText(template.Prompt.Text, font, promptWidth), Rect(template.Prompt));
        var group = new ButtonGroup();
        _quit = Place(new Win32ClassicRadio(template.Quit.Text, group), Rect(template.Quit));
        _title = Place(new Win32ClassicRadio(template.ReturnToTitle.Text, group), Rect(template.ReturnToTitle));
        _ok = Place(new Win32ClassicButton(template.Ok.Text, font)
                    { IsDefault = template.Ok.TypeBits == DialogStyles.BsDefPushButton }, Rect(template.Ok));
        _cancel = Place(new Win32ClassicButton(template.Cancel.Text, font), Rect(template.Cancel));
        _quitKey = AgercExitDialog.Mnemonic(template.Quit.Text);
        _titleKey = AgercExitDialog.Mnemonic(template.ReturnToTitle.Text);

        _ok.Pressed += () => Complete(SelectedChoice);
        _cancel.Pressed += () => Complete(Choice.Cancel);
        CloseRequested += () => Complete(Choice.Cancel);
        WindowInput += OnWindowInput;
    }

    private static (int X, int Y, int Width, int Height) Rect(DialogItem item) =>
        Win32TemplateDialog.BaseUnits.ToPixels(item.X, item.Y, item.Width, item.Height);

    private T Place<T>(T control, (int X, int Y, int Width, int Height) rect) where T : Control
    {
        control.Position = new Vector2(rect.X, rect.Y);
        control.Size = new Vector2(rect.Width, rect.Height);
        Client.AddChild(control);
        return control;
    }

    private Choice SelectedChoice => _title.ButtonPressed ? Choice.ReturnToTitle : Choice.Quit;

    /// <summary>Open centred on AGE's outer window over a game client of <paramref name="client"/> pixels.</summary>
    public void Open(Vector2I client)
    {
        _completed = false;
        _quit.ButtonPressed = true;
        PopupCenteredOnAgeWindow(client, _clientSize);
        _quit.GrabFocus();
    }

    // The dialog's keys, as Win32's dialog manager handles them: a radio's mnemonic (E and T in both templates here)
    // selects it, the arrows move within the radio group, Tab cycles the checked radio, OK and Cancel, Enter presses
    // the focused push button (else OK, the default), and Escape is Cancel.
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

    /// <summary>The texts the driver's <c>dialogs</c> step reads from the native dialog.</summary>
    public string Text => _template.Text;

    /// <summary>The dialog as the drive protocol lists it, with the template's control texts.</summary>
    public System.Text.Json.Nodes.JsonObject Describe()
    {
        var buttons = new System.Text.Json.Nodes.JsonArray();
        foreach (string text in _template.Buttons) buttons.Add(text);
        return new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = _template.Title, ["text"] = Text, ["buttons"] = buttons,
        };
    }

    /// <summary>Press a control by its native text (ampersands and case ignored), as BM_CLICK does natively.
    /// Returns the control's text, or null when no control matches.</summary>
    public string? Press(string button)
    {
        string wanted = button.Replace("&", "").ToLowerInvariant();
        DialogItem[] controls = { _template.Quit, _template.ReturnToTitle, _template.Ok, _template.Cancel };
        int index = Array.FindIndex(controls, item => item.Text.Replace("&", "").ToLowerInvariant() == wanted);
        switch (index)
        {
            case 0: _quit.ButtonPressed = true; break;
            case 1: _title.ButtonPressed = true; break;
            case 2: Complete(SelectedChoice); break;
            case 3: Complete(Choice.Cancel); break;
            default: return null;
        }
        return controls[index].Text;
    }

    private void Complete(Choice choice)
    {
        if (_completed) return;
        _completed = true;
        Hide();
        Completed?.Invoke(choice);
    }
}

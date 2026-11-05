using System;
using System.Collections.Generic;
using Age.Engine.Frontend.NativeResources;
using Godot;

/// <summary>
/// A modal Win32 dialog built from a dialog template (an AGERC.DLL RT_DIALOG resource) as CreateDialogIndirect
/// builds it: the client area and every control at MapDialogRect's pixels for the dialog font's base units, the
/// controls in template order. AGERC's templates use ＭＳ Ｐゴシック 9 pt, whose base units are 7×12 (docs/engine-re.md,
/// "Native menu bar"). Subclasses are the dialog procedures: they fill the controls (WM_INITDIALOG) and answer
/// commands (WM_COMMAND), ending the dialog with <see cref="EndDialog"/>.
/// <para>A dialog can hold child dialogs (<see cref="AddPage"/>): DS_CONTROL pages such as the 設定 dialog's tabs,
/// created hidden at their template position. Their controls join the dialog's tab order and its
/// <see cref="Describe"/> listing in creation order, and their notifications go to
/// <see cref="OnPageCommand"/> and <see cref="OnScroll"/>.</para>
/// <para>The dialog manager's keys: Tab and Shift+Tab move between the visible WS_TABSTOP controls, Enter commands
/// the focused push button (else the default push button, else IDOK), Escape and the caption's ✕ command IDCANCEL.
/// Focus rectangles and mnemonic underlines stay hidden in a dialog opened by the mouse until a key reveals them.
/// </para>
/// Controls the port does not draw yet (radio buttons and other common controls) are left out; <see cref="Describe"/>
/// still reports their texts, since it reads the template.
/// </summary>
public partial class Win32TemplateDialog : Win32Dialog
{
    public const int IdOk = 1, IdCancel = 2;

    private sealed class Item
    {
        public required DialogItem Template { get; init; }
        public required DialogPage Owner { get; init; }
        public required string Text { get; set; }
        public Control? Control { get; init; }
    }

    /// <summary>The dialog itself or one of its child dialogs: the window GetDlgItem and SetDlgItemText address, since
    /// control ids are only unique within it.</summary>
    protected sealed class DialogPage
    {
        private readonly Win32TemplateDialog _dialog;

        internal DialogPage(Win32TemplateDialog dialog, Control container)
        {
            _dialog = dialog;
            Container = container;
        }

        /// <summary>The page's client area.</summary>
        public Control Container { get; }

        /// <summary>ShowWindow SW_SHOW / SW_HIDE.</summary>
        public bool Visible
        {
            get => Container.Visible;
            set => Container.Visible = value;
        }

        private Item? Find(int id) => _dialog._items.Find(i => i.Owner == this && i.Template.Id == id);

        public Control? Control(int id) => Find(id)?.Control;

        /// <summary>SetDlgItemText.</summary>
        public void SetItemText(int id, string text)
        {
            if (Find(id) is not { } item) return;
            item.Text = text;
            switch (item.Control)
            {
                case Win32StaticText label: label.Text = text; break;
                case Win32Edit edit: edit.Text = text; break;
            }
        }

        /// <summary>GetDlgItemText.</summary>
        public string GetItemText(int id) => Find(id)?.Text ?? "";

        /// <summary>IsDlgButtonChecked.</summary>
        public bool IsChecked(int id) => Control(id) is BaseButton { ButtonPressed: true };

        /// <summary>CheckDlgButton: no BN_CLICKED.</summary>
        public void SetChecked(int id, bool check)
        {
            if (Control(id) is BaseButton button) button.SetPressedNoSignal(check);
        }

        public Win32Trackbar? Trackbar(int id) => Control(id) as Win32Trackbar;

        public Win32TabControl? TabControl(int id) => Control(id) as Win32TabControl;
    }

    private readonly Vector2I _clientSize;
    private readonly List<Item> _items = new();
    private bool _ended;

    /// <summary>Raised once with EndDialog's result.</summary>
    public event Action<int>? Ended;

    /// <summary>The dialog's own controls.</summary>
    protected DialogPage Root { get; }

    protected Win32TemplateDialog(DialogTemplate template) : this(template, ClientSizeOf(template)) { }

    private Win32TemplateDialog(DialogTemplate template, Vector2I clientSize)
        : base(template.Title, clientSize, (template.Style & DialogStyles.WsSysMenu) != 0)
    {
        _clientSize = clientSize;
        Root = new DialogPage(this, Client);
        AddItems(Root, template.Items);
        CloseRequested += () => OnCommand(IdCancel);   // WM_CLOSE: DefDlgProc sends IDCANCEL
        WindowInput += OnWindowInput;
    }

    /// <summary>CreateDialogIndirectParam of a DS_CONTROL child dialog: its client area at the template's position in
    /// this dialog's client area, hidden unless the template is WS_VISIBLE, and its controls after every control
    /// created so far.</summary>
    protected DialogPage AddPage(DialogTemplate template)
    {
        var container = new Control
        {
            Position = new Vector2(BaseUnits.ToPixelsX(template.X), BaseUnits.ToPixelsY(template.Y)),
            Size = ClientSizeOf(template),
            Visible = (template.Style & DialogStyles.WsVisible) != 0,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        Client.AddChild(container);
        var page = new DialogPage(this, container);
        AddItems(page, template.Items);
        return page;
    }

    private void AddItems(DialogPage page, IEnumerable<DialogItem> items)
    {
        foreach (DialogItem item in items)
        {
            (int x, int y, int w, int h) = BaseUnits.ToPixels(item.X, item.Y, item.Width, item.Height);
            Control? control = CreateControl(item);
            if (control != null)
            {
                control.Position = new Vector2(x, y);
                control.Size = new Vector2(w, h);
                control.Visible = item.Visible;
                page.Container.AddChild(control);
                Connect(page, item.Id, control);
            }
            _items.Add(new Item { Template = item, Owner = page, Text = item.Text, Control = control });
        }
    }

    private void Connect(DialogPage page, int id, Control control)
    {
        switch (control)
        {
            case Win32ClassicButton button:
                button.Pressed += () => Command(page, id);
                break;
            case Win32ClassicCheckBox check:
                check.Pressed += () => Command(page, id);
                break;
            case Win32Trackbar trackbar:
                trackbar.Scrolled += code => OnScroll(page, id, code);
                break;
        }
    }

    // WM_COMMAND BN_CLICKED goes to the control's parent window.
    private void Command(DialogPage page, int id)
    {
        if (page == Root) OnCommand(id);
        else OnPageCommand(page, id);
    }

    /// <summary>The dialog font's base units: GdiGetCharDimensions' average width and the font's height.</summary>
    public static DialogBaseUnits BaseUnits
    {
        get
        {
            if (_baseUnits is { } units) return units;
            Win32Font font = Win32DialogStyle.DialogFont;
            units = new DialogBaseUnits(
                DialogBaseUnits.AverageCharWidth(c => (int)Mathf.Round(font.Width(c.ToString()))), font.Size);
            _baseUnits = units;
            return units;
        }
    }

    private static DialogBaseUnits? _baseUnits;

    public static Vector2I ClientSizeOf(DialogTemplate template) =>
        new(BaseUnits.ToPixelsX(template.Width), BaseUnits.ToPixelsY(template.Height));

    private static Control? CreateControl(DialogItem item)
    {
        Win32Font font = Win32DialogStyle.DialogFont;
        switch (item.ClassName)
        {
            case "Button" when item.TypeBits is DialogStyles.BsPushButton or DialogStyles.BsDefPushButton:
                return new Win32ClassicButton(item.Text, font) { IsDefault = item.TypeBits == DialogStyles.BsDefPushButton };
            case "Button" when item.TypeBits == DialogStyles.BsAutoCheckBox:
                return new Win32ClassicCheckBox(item.Text);
            case "Button" when item.TypeBits == DialogStyles.BsGroupBox:
                return new Win32GroupBox(item.Text, font);
            case "Static":
                bool sunken = (item.Style & DialogStyles.SsSunken) != 0;
                return item.TypeBits switch
                {
                    DialogStyles.SsLeft => Text(item, font, Win32StaticText.Alignment.Left),
                    DialogStyles.SsCenter => Text(item, font, Win32StaticText.Alignment.Center),
                    DialogStyles.SsRight => Text(item, font, Win32StaticText.Alignment.Right),
                    0xb or 0xc => new Win32StaticText(item.Text, font, float.MaxValue, prefix: Prefix(item)), // SS_SIMPLE, SS_LEFTNOWORDWRAP
                    DialogStyles.SsIcon => new Win32StaticImage(),
                    DialogStyles.SsBlackFrame => new Win32StaticEdge(sunken, Win32DialogStyle.DefaultFrame),
                    0x8 => new Win32StaticEdge(sunken, Win32DialogStyle.Shadow),       // SS_GRAYFRAME
                    0x9 => new Win32StaticEdge(sunken, Win32DialogStyle.Highlight),    // SS_WHITEFRAME
                    _ => Unsupported(item),
                };
            case "Edit":
                var alignment = (item.Style & 3) switch
                {
                    DialogStyles.EsCenter => Win32StaticText.Alignment.Center,
                    DialogStyles.EsRight => Win32StaticText.Alignment.Right,
                    _ => Win32StaticText.Alignment.Left,
                };
                return new Win32Edit(item.Text, font, alignment, (item.Style & DialogStyles.EsReadOnly) != 0,
                                     clientEdge: item.Border);
            case "msctls_trackbar32":
                return new Win32Trackbar(vertical: (item.Style & DialogStyles.TbsVert) != 0,
                                         both: (item.Style & DialogStyles.TbsBoth) != 0);
            case "SysTabControl32":
                return new Win32TabControl(font);
            default:
                return Unsupported(item);
        }
    }

    private static Win32StaticText Text(DialogItem item, Win32Font font, Win32StaticText.Alignment alignment) =>
        new(item.Text, font, BaseUnits.ToPixelsX(item.Width), alignment, Prefix(item));

    private static bool Prefix(DialogItem item) => (item.Style & DialogStyles.SsNoPrefix) == 0;

    private static Control? Unsupported(DialogItem item)
    {
        GD.Print($"[dialog] control {item.Id} ({item.ClassName} style 0x{item.Style:x8}) is not drawn by the port");
        return null;
    }

    /// <summary>Show the dialog centred on its parent (agerc_center_dialog_on_parent), after
    /// <see cref="OnInitDialog"/>: on <paramref name="owner"/> when another dialog owns it, else on AGE's window around
    /// a game client of <paramref name="gameClient"/> pixels. <paramref name="openedByKeyboard"/> starts it with the
    /// keyboard cues visible.</summary>
    public void Open(Vector2I gameClient, bool openedByKeyboard, Win32Dialog? owner = null)
    {
        _ended = false;
        InitializeCues(openedByKeyboard, openedByKeyboard);
        OnInitDialog();
        if (owner != null) PopupCenteredOnDialog(owner, _clientSize);
        else PopupCenteredOnAgeWindow(gameClient, _clientSize);
        // WM_INITDIALOG returned TRUE: the dialog manager focuses the first tab stop.
        List<Control> stops = TabStops();
        if (stops.Count > 0) stops[0].GrabFocus();
    }

    /// <summary>WM_INITDIALOG: fill the controls.</summary>
    protected virtual void OnInitDialog() { }

    /// <summary>WM_COMMAND from one of the dialog's own controls, Enter or Escape. The default ends the dialog on IDOK
    /// and IDCANCEL.</summary>
    protected virtual void OnCommand(int id)
    {
        if (id is IdOk or IdCancel) EndDialog(id);
    }

    /// <summary>WM_COMMAND BN_CLICKED from a control of a child dialog.</summary>
    protected virtual void OnPageCommand(DialogPage page, int id) { }

    /// <summary>WM_HSCROLL or WM_VSCROLL from a trackbar, with its TB_* code.</summary>
    protected virtual void OnScroll(DialogPage page, int id, int code) { }

    protected void EndDialog(int result)
    {
        if (_ended) return;
        _ended = true;
        Hide();
        Ended?.Invoke(result);
    }

    /// <summary>SetDlgItemText on the dialog's own controls.</summary>
    protected void SetItemText(int id, string text) => Root.SetItemText(id, text);

    /// <summary>STM_SETICON on an SS_ICON static.</summary>
    protected void SetItemIcon(int id, Texture2D icon)
    {
        if (Root.Control(id) is Win32StaticImage image) image.Texture = icon;
    }

    private List<Control> TabStops()
    {
        var stops = new List<Control>();
        foreach (Item item in _items)
            if (item.Template.TabStop && !item.Template.Disabled
                && item.Control is { FocusMode: not Control.FocusModeEnum.None } control && control.IsVisibleInTree())
                stops.Add(control);
        return stops;
    }

    private int DefaultId()
    {
        foreach (Item item in _items)
            if (item.Owner == Root && item.Template.ClassName == "Button"
                && item.Template.TypeBits == DialogStyles.BsDefPushButton)
                return item.Template.Id;
        return IdOk;
    }

    private void OnWindowInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        RevealCues(key);
        Control? focus = Client.GetViewport().GuiGetFocusOwner();
        switch (key.Keycode)
        {
            case Key.Tab:
                List<Control> stops = TabStops();
                if (stops.Count == 0) return;
                int at = focus == null ? -1 : stops.IndexOf(focus);
                int step = key.ShiftPressed ? stops.Count - 1 : 1;
                stops[at < 0 ? 0 : (at + step) % stops.Count].GrabFocus();
                break;
            case Key.Enter or Key.KpEnter:
                if (key.Echo) return;
                if (_items.Find(i => i.Control != null && i.Control == focus && i.Control is Win32ClassicButton)
                    is { } pushed)
                    Command(pushed.Owner, pushed.Template.Id);
                else
                    OnCommand(DefaultId());
                break;
            case Key.Escape:
                if (key.Echo) return;
                OnCommand(IdCancel);
                break;
            default:
                return;
        }
        SetInputAsHandled();
    }

    /// <summary>The dialog as the driver's <c>dialogs</c> step lists a native one (EnumChildWindows over the dialog
    /// and its child dialogs, hidden windows included): its title, the non-empty texts of its Static controls joined
    /// by newlines, and its Button controls' texts (push buttons, check boxes and group boxes), in creation order.
    /// </summary>
    public System.Text.Json.Nodes.JsonObject Describe()
    {
        var statics = new List<string>();
        var buttons = new System.Text.Json.Nodes.JsonArray();
        foreach (Item item in _items)
        {
            if (item.Template.ClassName == "Static" && item.Text.Length > 0) statics.Add(item.Text);
            if (item.Template.ClassName == "Button") buttons.Add(item.Text);
        }
        return new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = Title, ["text"] = string.Join("\n", statics), ["buttons"] = buttons,
        };
    }

    /// <summary>Click a Button control by its text (ampersands and case ignored), as the driver's BM_CLICK does
    /// natively: a push button commands, a check box toggles and then commands, a group box ignores it. Returns the
    /// control's text, or null when no Button control matches.</summary>
    public string? Press(string button)
    {
        string wanted = button.Replace("&", "").ToLowerInvariant();
        Item? item = _items.Find(i => i.Template.ClassName == "Button"
                                      && i.Text.Replace("&", "").ToLowerInvariant() == wanted);
        if (item == null) return null;
        switch (item.Control)
        {
            case Win32ClassicButton:
                Command(item.Owner, item.Template.Id);
                break;
            case Win32ClassicCheckBox check:
                check.ButtonPressed = !check.ButtonPressed;
                Command(item.Owner, item.Template.Id);
                break;
        }
        return item.Text;
    }
}

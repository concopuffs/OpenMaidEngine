using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ome.Engine.Frontend.Shell;
using Godot;

/// <summary>
/// The engine shell's options panel (docs/superpowers/specs/2026-11-09-engine-shell-design.md, §7.1): an overlay inside
/// the game viewport, laid out in the game's logical pixels over the frozen frame, which replaces AGERC's 設定 and
/// ﾊﾞｰｼﾞｮﾝ情報. It draws <see cref="ShellPanel"/>'s sections and edits a <see cref="ShellOptionsDraft"/>: the ｻｳﾝﾄﾞ
/// volumes and routes apply live, 決定 commits (<see cref="Committed"/>) and ｷｬﾝｾﾙ, Escape or a controller's B discards
/// (<see cref="Cancelled"/>). The port options edit a copy of the run's values that 決定 hands back. While op 0x142's
/// gate is 0 the native sections are read-only. The mouse, Tab and the arrows, Enter and a controller's D-pad and A move
/// through Godot's focus chain; Ctrl+Tab or the shoulder buttons change section. Its text comes from the shell's
/// string table, in Noto Sans JP (<see cref="ShellAssets"/>); the driver reads and presses it by its Japanese texts.
/// </summary>
public partial class OptionsPanel : Control
{
    /// <summary>What the panel shows and reaches beyond its draft.</summary>
    /// <param name="PortOptions">The run's port options when the panel opens.</param>
    /// <param name="Canvas">The game's logical canvas.</param>
    /// <param name="WindowSizes">画面's window sizes (<see cref="ShellPanel.WindowSizes"/>).</param>
    /// <param name="SwitchScreenMode">The screen-mode command; false when nothing changed.</param>
    /// <param name="ForceScreen">display:ForceScreen 1 keeps the window.</param>
    public sealed record Context(ShellStrings Strings, ShellOptionsDraft Draft, PortOptions PortOptions, Vector2I Canvas,
                                 IReadOnlyList<(int Width, int Height)> WindowSizes, Func<bool> Windowed,
                                 Func<bool, bool> SwitchScreenMode, bool ForceScreen, ShellAbout About,
                                 ShellDiagnostics Diagnostics, Texture2D Icon);

    /// <summary>決定, with the edited port options.</summary>
    public event Action<PortOptions>? Committed;

    /// <summary>ｷｬﾝｾﾙ, Escape or B.</summary>
    public event Action? Cancelled;

    private const int FontSize = 15, SmallFontSize = 13, TitleFontSize = 18;
    private const int NameWidth = 188, RangeWidth = 34, ValueWidth = 72;
    private static readonly Color Text = new("e8ebf2"), Dim = new("a3acbe"), Accent = new("8fb0ff"),
                                  Warning = new("ffcf70"), CardColor = new("1c212b"), Border = new("3a4458");
    private static readonly ShellStrings Ja = ShellStrings.Japanese;

    private readonly Context _context;
    private readonly VBoxContainer _content = new();
    private readonly ScrollContainer _scroll = new();
    private readonly Label _note = new();
    private readonly Button _ok = new(), _cancel = new();
    private readonly Dictionary<ShellSection, Button> _sectionButtons = [];
    private readonly Dictionary<string, CheckBox> _switches = [];
    private readonly ButtonGroup _sectionGroup = new();
    private PortOptions _port;
    private ShellSection _section;
    private Button? _copy;
    private Label? _copied;
    private bool _completed;

    private ShellStrings S => _context.Strings;

    public OptionsPanel(Context context, ShellSection section)
    {
        _context = context;
        _port = context.PortOptions;
        _section = section;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = CreateTheme();
        BuildFrame();
    }

    public ShellSection CurrentSection => _section;

    /// <summary>Show the panel on its section, focusing the section's first control, or the copy button for the
    /// diagnostics.</summary>
    public void Open(bool diagnostics)
    {
        ShowSection(_section);
        CallDeferred(MethodName.FocusFirst, diagnostics);
    }

    private void FocusFirst(bool diagnostics)
    {
        if (diagnostics && _copy != null)
        {
            _copy.GrabFocus();
            _scroll.EnsureControlVisible(_copy);
            return;
        }
        (FirstFocusable(_content) ?? _sectionButtons[_section]).GrabFocus();
    }

    private static Control? FirstFocusable(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control { Visible: true } control && control.FocusMode == FocusModeEnum.All
                && !(control is BaseButton { Disabled: true }) && !(control is Slider { Editable: false }))
                return control;
            if (FirstFocusable(child) is { } found) return found;
        }
        return null;
    }

    // ---- the frame ----

    private void BuildFrame()
    {
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var size = new Vector2(Math.Min(720, _context.Canvas.X - 24), Math.Min(532, _context.Canvas.Y - 24));
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Box(CardColor, Border, 1, 10, 0));
        AddChild(card);
        card.SetAnchorsPreset(LayoutPreset.Center);
        card.OffsetLeft = -size.X / 2;
        card.OffsetRight = size.X / 2;
        card.OffsetTop = -size.Y / 2;
        card.OffsetBottom = size.Y / 2;

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        card.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        header.AddChild(new TextureRect
        {
            Texture = _context.Icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(24, 24),
        });
        header.AddChild(Label(S["panel.title"], TitleFontSize, Text));
        column.AddChild(Margin(header, 16, 12, 16, 10));
        column.AddChild(new HSeparator());

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        column.AddChild(body);

        var nav = new VBoxContainer { CustomMinimumSize = new Vector2(148, 0) };
        nav.AddThemeConstantOverride("separation", 4);
        foreach (ShellPanelSection panelSection in ShellPanel.Sections)
        {
            ShellSection section = panelSection.Section;
            var button = new Button
            {
                Text = S[panelSection.TitleKey], ToggleMode = true, ButtonGroup = _sectionGroup,
                Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.All,
            };
            StyleSectionButton(button);
            button.Pressed += () => ShowSection(section);
            _sectionButtons[section] = button;
            nav.AddChild(button);
        }
        body.AddChild(Margin(nav, 10, 12, 10, 12));
        body.AddChild(new VSeparator());

        _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _content.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(Margin(_content, 16, 12, 18, 12));
        body.AddChild(_scroll);

        column.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _note.AddThemeFontSizeOverride("font_size", SmallFontSize);
        _note.AddThemeColorOverride("font_color", Warning);
        footer.AddChild(_note);
        _ok.Text = S["panel.ok"];
        _cancel.Text = S["panel.cancel"];
        foreach ((Button button, bool primary) in new[] { (_ok, true), (_cancel, false) })
        {
            button.CustomMinimumSize = new Vector2(104, 32);
            StyleButton(button, primary);
            footer.AddChild(button);
        }
        _ok.Pressed += Commit;
        _cancel.Pressed += Cancel;
        column.AddChild(Margin(footer, 16, 10, 16, 12));
    }

    // ---- sections ----

    private void ShowSection(ShellSection section)
    {
        _section = section;
        // SetPressedNoSignal leaves the group alone, so each button takes its state.
        foreach ((ShellSection each, Button button) in _sectionButtons) button.SetPressedNoSignal(each == section);
        _switches.Clear();
        _copy = null;
        _copied = null;
        foreach (Node child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
        ShellPanelSection panelSection = ShellPanel.Section(section);
        bool locked = panelSection.ScriptShared && _context.Draft.ReadOnly;
        foreach (ShellPanelGroup group in panelSection.Groups)
        {
            var rows = new VBoxContainer();
            rows.AddThemeConstantOverride("separation", 6);
            foreach (ShellPanelItem item in group.Items)
                if (Row(item, group, locked) is { } row) rows.AddChild(row);
            if (rows.GetChildCount() == 0) continue;
            _content.AddChild(Label(S[group.TitleKey], FontSize, Accent));
            _content.AddChild(Margin(rows, 10, 0, 0, 8));
        }
        _note.Text = locked ? S["panel.read-only"] : "";
        _scroll.ScrollVertical = 0;
    }

    private Control? Row(ShellPanelItem item, ShellPanelGroup group, bool locked) => item switch
    {
        ShellPanelSlider slider => SliderRow(slider, group, locked),
        ShellPanelSwitch toggle => Switch(toggle.Option, locked),
        ShellPanelChoice choice => ChoiceRow(choice, group),
        ShellPanelScreenMode => ScreenModeRow(),
        ShellPanelAbout => AboutRows(),
        ShellPanelDiagnostics => DiagnosticsRows(),
        _ => null,
    };

    private Control? SliderRow(ShellPanelSlider slider, ShellPanelGroup group, bool locked)
    {
        ShellOption option = ShellOptions.Find(slider.Option)!;
        if (!_context.Draft.Visible.Contains(option)) return null;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        Control name = slider.Switch != null
            ? Switch(slider.Switch, locked)!
            : Label(S[option.LabelKey] == S[group.TitleKey] ? "" : S[option.LabelKey], FontSize, Text);
        name.CustomMinimumSize = new Vector2(NameWidth, 0);
        row.AddChild(name);
        Label low = Label(S[slider.LowKey], SmallFontSize, Dim);
        low.HorizontalAlignment = HorizontalAlignment.Right;
        low.CustomMinimumSize = new Vector2(RangeWidth, 0);
        row.AddChild(low);
        var value = Label("", FontSize, Text);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.CustomMinimumSize = new Vector2(ValueWidth, 0);
        var bar = new HSlider
        {
            MinValue = option.Min, MaxValue = option.Max, Step = option.Step, Scrollable = false, Editable = !locked,
            FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(120, 24),
        };
        bar.SetValueNoSignal(ShellPanel.Position(option, slider, _context.Draft.Value(option)));
        value.Text = ShellPanel.Format(S, slider.Format, _context.Draft.Value(option));
        bar.ValueChanged += position =>
        {
            _context.Draft.Set(option, ShellPanel.ValueAt(option, slider, (int)Math.Round(position)));
            value.Text = ShellPanel.Format(S, slider.Format, _context.Draft.Value(option));
        };
        row.AddChild(bar);
        Label high = Label(S[slider.HighKey], SmallFontSize, Dim);
        high.CustomMinimumSize = new Vector2(RangeWidth, 0);
        row.AddChild(high);
        row.AddChild(value);
        // A locked slider reads as disabled, as a disabled check box does (which keeps its own look).
        if (locked)
            foreach (Control part in new[] { name, low, bar, high, value })
                if (part is not CheckBox) part.Modulate = new Color(1, 1, 1, 0.5f);
        return row;
    }

    /// <summary>A native switch option's check box, or a port switch's.</summary>
    private CheckBox? Switch(string name, bool locked)
    {
        var check = new CheckBox { Text = S[ShellPanel.LabelKey(name)], FocusMode = FocusModeEnum.All };
        if (ShellOptions.Find(name) is { } option)
        {
            if (!_context.Draft.Visible.Contains(option)) return null;
            check.SetPressedNoSignal(_context.Draft.Value(option) != 0);
            check.Disabled = locked;
            check.Toggled += on => _context.Draft.Set(option, on ? 1 : 0);
        }
        else
        {
            check.SetPressedNoSignal(name == "fps-counter" ? _port.FpsCounter : _port.LocatorHud);
            check.Toggled += on => _port = name == "fps-counter" ? _port with { FpsCounter = on }
                                                                  : _port with { LocatorHud = on };
        }
        _switches[name] = check;
        return check;
    }

    private Control ChoiceRow(ShellPanelChoice choice, ShellPanelGroup group)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        string label = S[ShellPanel.LabelKey(choice.Option)];
        if (label != S[group.TitleKey])
            row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(NameWidth, 0) });
        var list = new OptionButton { FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(280, 32) };
        if (choice.Option == "language")
        {
            foreach (string language in ShellStrings.Languages) list.AddItem(S[$"language.{language}"]);
            list.Selected = ShellStrings.Languages.ToList().IndexOf(_port.Language);
            list.ItemSelected += index => _port = _port with { Language = ShellStrings.Languages[(int)index] };
        }
        else
        {
            (int Width, int Height) canvas = (_context.Canvas.X, _context.Canvas.Y);
            (int Width, int Height) current = _port.CanvasWindowSize ? canvas : (_port.WindowWidth, _port.WindowHeight);
            foreach ((int width, int height) in _context.WindowSizes)
                list.AddItem(ShellPanel.WindowSizeText(S, width, height, (width, height) == canvas));
            list.Selected = Math.Max(0, _context.WindowSizes.ToList().IndexOf(current));
            list.ItemSelected += index =>
            {
                (int width, int height) = _context.WindowSizes[(int)index];
                _port = (width, height) == canvas ? _port with { WindowWidth = 0, WindowHeight = 0 }
                                                  : _port with { WindowWidth = width, WindowHeight = height };
            };
        }
        row.AddChild(list);
        return row;
    }

    private Control ScreenModeRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var group = new ButtonGroup();
        var fullscreen = new Button { Text = S["screen.fullscreen"], ToggleMode = true, ButtonGroup = group };
        var windowed = new Button { Text = S["screen.windowed"], ToggleMode = true, ButtonGroup = group };
        void Sync()
        {
            bool inWindow = _context.Windowed();
            windowed.SetPressedNoSignal(inWindow);
            fullscreen.SetPressedNoSignal(!inWindow);
        }
        foreach ((Button button, bool toWindow) in new[] { (fullscreen, false), (windowed, true) })
        {
            button.CustomMinimumSize = new Vector2(132, 32);
            button.FocusMode = FocusModeEnum.All;
            StyleSectionButton(button);
            button.Pressed += () =>
            {
                _context.SwitchScreenMode(toWindow);   // a command: it applies at once (spec §5.3)
                Sync();
            };
            row.AddChild(button);
        }
        fullscreen.Disabled = _context.ForceScreen;
        Sync();
        return row;
    }

    private Control AboutRows()
    {
        ShellAbout about = _context.About;
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);
        var engine = new HBoxContainer();
        engine.AddThemeConstantOverride("separation", 14);
        engine.AddChild(new TextureRect
        {
            Texture = _context.Icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(64, 64),
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        });
        var lines = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        lines.AddThemeConstantOverride("separation", 2);
        lines.AddChild(Label(EngineIdentity.Name, TitleFontSize, Text));
        lines.AddChild(Label(Fill(S["about.version"], about.EngineVersion), FontSize, Text));
        lines.AddChild(Wrapped(EngineIdentity.License, Dim));
        lines.AddChild(Wrapped(EngineIdentity.FfmpegNotice, Dim));
        lines.AddChild(Wrapped(EngineIdentity.NoticesPointer, Dim));
        engine.AddChild(lines);
        rows.AddChild(engine);
        rows.AddChild(new HSeparator());
        rows.AddChild(Wrapped(about.GameName, Text, FontSize + 1));
        if (about.GameVersion.Length > 0) rows.AddChild(Label(Fill(S["about.game-version"], about.GameVersion), FontSize, Text));
        if (about.Copyright.Length > 0) rows.AddChild(Wrapped(about.Copyright, Dim));
        return rows;
    }

    private Control DiagnosticsRows()
    {
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach ((string key, string value) in _context.Diagnostics.Rows)
        {
            Label name = Label(S[key], SmallFontSize, Dim);
            name.CustomMinimumSize = new Vector2(112, 0);
            name.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            grid.AddChild(name);
            Label text = Wrapped(value, Text, SmallFontSize);
            text.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
            grid.AddChild(text);
        }
        rows.AddChild(grid);
        var copyRow = new HBoxContainer();
        copyRow.AddThemeConstantOverride("separation", 10);
        _copy = new Button { Text = S["diag.copy"], FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 30) };
        StyleButton(_copy, primary: false);
        _copied = Label("", SmallFontSize, Accent);
        _copied.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _copy.Pressed += CopyDiagnostics;
        copyRow.AddChild(_copy);
        copyRow.AddChild(_copied);
        rows.AddChild(copyRow);
        return rows;
    }

    private void CopyDiagnostics()
    {
        DisplayServer.ClipboardSet(_context.Diagnostics.ClipboardText());
        if (_copied != null) _copied.Text = S["diag.copied"];
    }

    // ---- keys and outcomes ----

    /// <summary>The panel's own keys, which Main passes on before the GUI sees them: ui_cancel (Escape, a controller's
    /// B) cancels, Ctrl+Tab and the shoulder buttons change section, and Enter commits unless a button has the focus.
    /// True when the panel took the event; everything else reaches the panel's controls as GUI input.</summary>
    public bool HandleInput(InputEvent e)
    {
        if (_completed || !IsVisibleInTree() || ListOpen()) return false;
        if (e.IsActionPressed("ui_cancel"))
        {
            Cancel();
            return true;
        }
        int step = e switch
        {
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab, CtrlPressed: true } tab => tab.ShiftPressed ? -1 : 1,
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.LeftShoulder } => -1,
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.RightShoulder } => 1,
            _ => 0,
        };
        if (step != 0)
        {
            int count = ShellPanel.Sections.Count;
            int at = ShellPanel.Sections.ToList().FindIndex(s => s.Section == _section);
            ShowSection(ShellPanel.Sections[(at + step + count) % count].Section);
            _sectionButtons[_section].GrabFocus();
            return true;
        }
        // Enter presses the focused button; anywhere else it is 決定, the default.
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }
            && GetViewport().GuiGetFocusOwner() is not BaseButton)
        {
            Commit();
            return true;
        }
        return false;
    }

    private bool ListOpen() => FindChildren("*", nameof(OptionButton), recursive: true, owned: false)
        .OfType<OptionButton>().Any(list => list.GetPopup().Visible);

    private void Commit()
    {
        if (_completed) return;
        _completed = true;
        Committed?.Invoke(_port);
    }

    private void Cancel()
    {
        if (_completed) return;
        _completed = true;
        Cancelled?.Invoke();
    }

    // ---- the driver ----

    /// <summary>The panel as the drive protocol lists it, in Japanese whatever it shows: its title, the section shown
    /// and the buttons <see cref="Press"/> takes (the section's check boxes, the sections, 決定 and ｷｬﾝｾﾙ).</summary>
    public JsonObject Describe()
    {
        var buttons = new JsonArray();
        foreach (string name in _switches.Keys) buttons.Add(Ja[ShellPanel.LabelKey(name)]);
        if (_copy != null) buttons.Add(Ja["diag.copy"]);
        foreach (ShellPanelSection section in ShellPanel.Sections) buttons.Add(Ja[section.TitleKey]);
        buttons.Add(Ja["panel.ok"]);
        buttons.Add(Ja["panel.cancel"]);
        return new JsonObject
        {
            ["title"] = Ja["panel.title"], ["text"] = Ja[ShellPanel.Section(_section).TitleKey], ["buttons"] = buttons,
        };
    }

    /// <summary>Press a control by its text, Japanese or as shown, mnemonics and case ignored: a check box toggles, a
    /// section shows, the copy button copies, and 決定 (or OK, 設定's) and ｷｬﾝｾﾙ end the panel. Returns the control's
    /// Japanese text, or null when nothing matches.</summary>
    public string? Press(string button)
    {
        string wanted = Normalize(button);
        bool Matches(string key) => Normalize(Ja[key]) == wanted || Normalize(S[key]) == wanted;
        foreach ((string name, CheckBox check) in _switches)
        {
            if (!Matches(ShellPanel.LabelKey(name))) continue;
            if (check.Disabled) return null;
            check.ButtonPressed = !check.ButtonPressed;
            return Ja[ShellPanel.LabelKey(name)];
        }
        if (_copy != null && Matches("diag.copy"))
        {
            CopyDiagnostics();
            return Ja["diag.copy"];
        }
        foreach (ShellPanelSection section in ShellPanel.Sections)
        {
            if (!Matches(section.TitleKey)) continue;
            ShowSection(section.Section);
            return Ja[section.TitleKey];
        }
        if (Matches("panel.ok") || wanted == "ok")
        {
            Commit();
            return Ja["panel.ok"];
        }
        if (Matches("panel.cancel") || wanted == "cancel")
        {
            Cancel();
            return Ja["panel.cancel"];
        }
        return null;
    }

    /// <summary>The driver's <c>option</c> step: the draft takes <paramref name="value"/> as the option's control would
    /// set it, and the section redraws. Returns the value the draft holds, or null when the section shown has no
    /// usable control for the option (another section's, one the profile hides, or a read-only section's).</summary>
    public int? SetOption(ShellOption option, int value)
    {
        if (option.Section != _section || !_context.Draft.Set(option, value)) return null;
        ShowSection(_section);
        return _context.Draft.Value(option);
    }

    /// <summary>The driver's <c>options</c> step: the native options the profile shows, with the draft's values.
    /// </summary>
    public JsonArray ListOptions()
    {
        var options = new JsonArray();
        foreach (ShellOption option in _context.Draft.Visible)
            options.Add(new JsonObject
            {
                ["name"] = option.Name, ["section"] = option.Section.ToString().ToLowerInvariant(),
                ["value"] = _context.Draft.Value(option), ["min"] = option.Min, ["max"] = option.Max,
                ["step"] = option.Step, ["applies"] = option.Apply == ShellOptionApply.Live ? "live" : "ok",
            });
        return options;
    }

    /// <summary>A label without its mnemonic: "効果音(&amp;S)" and "&amp;Sound" read as 効果音 and sound.</summary>
    private static string Normalize(string text)
    {
        string plain = System.Text.RegularExpressions.Regex.Replace(text, @"\(&.\)$", "");
        return plain.Replace("&", "").Trim().ToLowerInvariant();
    }

    // ---- looks ----

    private static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFont = ShellAssets.Font, DefaultFontSize = FontSize };
        foreach (string type in new[] { "Label", "Button", "CheckBox", "OptionButton" })
            theme.SetColor("font_color", type, Text);
        theme.SetColor("font_disabled_color", "CheckBox", Dim with { A = 0.6f });
        theme.SetColor("font_disabled_color", "Button", Dim with { A = 0.6f });
        // Godot's default boxes are dark gray, which the card hides: ours keep a light outline.
        theme.SetIcon("unchecked", "CheckBox", CheckIcon(on: false, disabled: false));
        theme.SetIcon("checked", "CheckBox", CheckIcon(on: true, disabled: false));
        theme.SetIcon("unchecked_disabled", "CheckBox", CheckIcon(on: false, disabled: true));
        theme.SetIcon("checked_disabled", "CheckBox", CheckIcon(on: true, disabled: true));
        return theme;
    }

    private static Texture2D CheckIcon(bool on, bool disabled)
    {
        string opacity = disabled ? "0.45" : "1";
        string box = on
            ? """<rect x="1.5" y="1.5" width="15" height="15" rx="3" fill="#3d63c9" stroke="#7aa2ff" stroke-width="1.5"/>"""
              + """<path d="M4.8 9.2 L7.6 12 L13.2 6" fill="none" stroke="#ffffff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>"""
            : """<rect x="1.5" y="1.5" width="15" height="15" rx="3" fill="#141821" stroke="#8a96b0" stroke-width="1.5"/>""";
        string svg = $"""<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18"><g opacity="{opacity}">{box}</g></svg>""";
        var image = new Image();
        image.LoadSvgFromString(svg);
        return ImageTexture.CreateFromImage(image);
    }

    private static StyleBoxFlat Box(Color fill, Color border, int borderWidth, int radius, int margin)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(margin);
        return box;
    }

    private static void StyleSectionButton(Button button)
    {
        StyleBoxFlat Style(Color fill, int border = 0)
        {
            StyleBoxFlat box = Box(fill, new Color("7aa2ff"), border, 6, 0);
            box.ContentMarginLeft = box.ContentMarginRight = 12;
            box.ContentMarginTop = box.ContentMarginBottom = 6;
            return box;
        }
        button.AddThemeStyleboxOverride("normal", Style(Colors.Transparent));
        button.AddThemeStyleboxOverride("hover", Style(new Color("262d3b")));
        button.AddThemeStyleboxOverride("pressed", Style(new Color("2f4a8a")));
        button.AddThemeStyleboxOverride("hover_pressed", Style(new Color("36539a")));
        button.AddThemeStyleboxOverride("disabled", Style(Colors.Transparent));
        StyleBoxFlat focus = Style(Colors.Transparent, 2);
        focus.DrawCenter = false;
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeColorOverride("font_color", new Color("c9d1e0"));
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
    }

    private static void StyleButton(Button button, bool primary)
    {
        Color fill = primary ? new Color("3d63c9") : new Color("2a3140");
        Color hover = primary ? new Color("4a72dc") : new Color("343d50");
        Color pressed = primary ? new Color("3354ad") : new Color("232a37");
        StyleBoxFlat Style(Color color) => Box(color, new Color("7aa2ff"), 0, 6, 8);
        button.AddThemeStyleboxOverride("normal", Style(fill));
        button.AddThemeStyleboxOverride("hover", Style(hover));
        button.AddThemeStyleboxOverride("pressed", Style(pressed));
        StyleBoxFlat focus = Box(Colors.Transparent, new Color("7aa2ff"), 2, 6, 8);
        focus.DrawCenter = false;
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
    }

    private static Label Label(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Label Wrapped(string text, Color color, int size = SmallFontSize)
    {
        Label label = Label(text, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    private static MarginContainer Margin(Control child, int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer { SizeFlagsHorizontal = child.SizeFlagsHorizontal };
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        margin.AddChild(child);
        return margin;
    }

    private static string Fill(string format, string value) => format.Replace("{0}", value);
}

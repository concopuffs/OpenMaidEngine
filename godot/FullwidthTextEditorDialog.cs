using System;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Sys4;
using Godot;

/// <summary>Godot presentation for AGERc command 10's blocking INPUTNAME editor, in the shell's language
/// (<see cref="ShellBoxes"/>).</summary>
public partial class FullwidthTextEditorDialog : Window
{
    private readonly LineEdit _edit = new() { MaxLength = 255 };
    private readonly Label _error = new()
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(0, 28),
    };
    private readonly ShellStrings _strings;
    private bool _completed;

    public event Action<bool, string>? EditCompleted;

    public FullwidthTextEditorDialog(ShellStrings strings)
    {
        _strings = strings;
        Title = strings[ShellBoxes.NameEntryTitle];
        Exclusive = true;
        Transient = true;
        Unresizable = true;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var column = new VBoxContainer();
        margin.AddChild(column);
        column.AddChild(new Label { Text = strings[ShellBoxes.NameEntryPrompt] });
        _edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        column.AddChild(_edit);
        _error.AddThemeColorOverride("font_color", new Color(1.0f, 0.35f, 0.35f));
        column.AddChild(_error);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var cancel = new Button { Text = strings[ShellBoxes.NameEntryCancel] };
        var accept = new Button { Text = strings[ShellBoxes.NameEntryAccept] };
        actions.AddChild(cancel);
        actions.AddChild(accept);
        column.AddChild(actions);

        accept.Pressed += TryAccept;
        cancel.Pressed += Cancel;
        _edit.TextSubmitted += _ => TryAccept();
        CloseRequested += Cancel;
    }

    public void Open(string initialText)
    {
        _completed = false;
        _error.Text = "";
        _edit.Text = initialText;
        PopupCentered(new Vector2I(430, 160));
        _edit.GrabFocus();
        _edit.SelectAll();
    }

    private void TryAccept()
    {
        FullwidthTextValidationError error = Cp932Text.ValidateFullwidthName(_edit.Text);
        if (error != FullwidthTextValidationError.None)
        {
            _error.Text = error == FullwidthTextValidationError.TooLong
                ? _strings[ShellBoxes.NameEntryTooLong]
                : _strings[ShellBoxes.NameEntryHalfWidth];
            _edit.GrabFocus();
            return;
        }
        Complete(true);
    }

    private void Cancel() => Complete(false);

    private void Complete(bool accepted)
    {
        if (_completed) return;
        _completed = true;
        Hide();
        EditCompleted?.Invoke(accepted, _edit.Text);
    }
}

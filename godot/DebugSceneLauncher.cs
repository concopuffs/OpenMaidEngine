using System;
using System.Collections.Generic;
using Age.Engine.Diagnostics;
using Godot;

/// <summary>Godot-only developer overlay. Runtime transition policy remains in Main/VirtualMachine.</summary>
public partial class DebugSceneLauncher : PopupPanel
{
    private readonly LineEdit _search = new() { PlaceholderText = "Name or exact packed id (0x...)" };
    private readonly OptionButton _category = new();
    private readonly ItemList _list = new() { SelectMode = ItemList.SelectModeEnum.Single };
    private readonly Label _details = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button _launch = new() { Text = "Launch", Disabled = true };
    private IReadOnlyList<DebugSceneEntry> _all = Array.Empty<DebugSceneEntry>();
    private IReadOnlyList<DebugSceneEntry> _visible = Array.Empty<DebugSceneEntry>();
    private DebugSceneEntry? _selected;
    private string _currentContext = "";

    public event Action<DebugSceneEntry>? LaunchRequested;

    public DebugSceneLauncher()
    {
        Title = "AGE Debug Scene Launcher";
        Exclusive = true;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var column = new VBoxContainer();
        margin.AddChild(column);

        var heading = new Label { Text = "Launch a packed SYS4 script through SYSTEM4" };
        heading.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(heading);

        var filters = new HBoxContainer();
        column.AddChild(filters);
        _category.AddItem("All");
        _category.AddItem("Scenario (SC)");
        _category.AddItem("Secondary / Event (SP)");
        _category.AddItem("Debug");
        _category.AddItem("Other / Expert");
        _category.CustomMinimumSize = new Vector2(190, 0);
        filters.AddChild(_category);
        _search.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        filters.AddChild(_search);

        _list.CustomMinimumSize = new Vector2(0, 290);
        _list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(_list);

        _details.CustomMinimumSize = new Vector2(0, 76);
        column.AddChild(_details);
        _status.CustomMinimumSize = new Vector2(0, 34);
        column.AddChild(_status);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        column.AddChild(actions);
        var cancel = new Button { Text = "Cancel" };
        actions.AddChild(cancel);
        actions.AddChild(_launch);

        _search.TextChanged += _ => Refresh();
        _category.ItemSelected += _ => Refresh();
        _list.ItemSelected += SelectEntry;
        _list.ItemActivated += SelectAndLaunch;
        cancel.Pressed += Hide;
        _launch.Pressed += RequestLaunch;
    }

    public void Open(IReadOnlyList<DebugSceneEntry> entries, string currentContext, bool present = true)
    {
        _all = entries;
        _currentContext = currentContext;
        _status.Text = "";
        Refresh();
        if (!present) return;
        PopupCentered(new Vector2I(700, 540));
        _search.GrabFocus();
    }

    public void SetStatus(string message) => _status.Text = message;

    private void Refresh()
    {
        var filter = (DebugScriptFilter)_category.Selected;
        _visible = DebugSceneCatalog.Filter(_all, filter, _search.Text);
        _list.Clear();
        foreach (var entry in _visible)
            _list.AddItem($"{entry.Name}    0x{entry.PackedId:x8}");
        _selected = null;
        _launch.Disabled = true;
        _details.Text = $"{_visible.Count} scripts shown. Current: {_currentContext}";
    }

    private void SelectEntry(long index)
    {
        if (index < 0 || index >= _visible.Count) return;
        _selected = _visible[(int)index];
        _launch.Disabled = !_selected.Launchable;
        string guard = _selected.Launchable
            ? "Launch returns TITLE to SYSTEM4, which performs the actual script dispatch. " +
              "Current live globals/profile state is retained; no story state is synthesized."
            : "Protected coordinator/root script; direct launch is disabled.";
        _details.Text =
            $"{_selected.Name}  [{_selected.Kind}]\n" +
            $"packed=0x{_selected.PackedId:x8} ({_selected.PackedId})  " +
            $"pack={_selected.PackId} raw=0x{_selected.RawIndex:x}  " +
            $"archive={_selected.Archive} size={_selected.Size:N0}\n{guard}";
    }

    private void SelectAndLaunch(long index)
    {
        SelectEntry(index);
        RequestLaunch();
    }

    private void RequestLaunch()
    {
        if (_selected is { Launchable: true } selected) LaunchRequested?.Invoke(selected);
    }
}

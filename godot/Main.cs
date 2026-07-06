using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public partial class Main : Godot.Control
{
    private Label _text = null!;
    private Label _status = null!;
    private VirtualMachine _vm = null!;
    private GodotAdvHost _host = null!;
    private volatile bool _done;
    private bool _ended;
    private bool _selftest;

    public override void _Ready()
    {
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _text.OffsetLeft = 40; _text.OffsetTop = 40; _text.OffsetRight = -40; _text.OffsetBottom = -80;
        AddChild(_text);
        _status = new Label();
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _status.OffsetLeft = 40; _status.OffsetTop = -60;
        AddChild(_status);

        _selftest = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--selftest") >= 0;

        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        _host = new GodotAdvHost(this);
        _vm = new VirtualMachine(script, table, _host);
        _ = Task.Run(() => { _vm.Run(); _done = true; });

        if (_selftest)
            _ = Task.Run(async () => { while (!_done) { if (_host.IsWaiting) _host.SignalInput(); await Task.Delay(1); } });
    }

    public override void _Process(double delta)
    {
        if (_done && !_ended)
        {
            _ended = true;
            ShowEnd();
            if (_selftest) RunSelfTest();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_selftest) return;
        if (e.IsActionPressed("ui_accept") ||
            (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left))
            _host.SignalInput();
    }

    public override void _ExitTree() { _host?.SignalInput(); }

    // ---- UI methods invoked on the main thread via CallDeferred ----
    public void AppendLine(string text) => _text.Text += text + "\n";
    public void PageBreak() => _status.Text = "▼ click / Enter";
    public void ClearPage() { _text.Text = ""; _status.Text = ""; }
    public void ShowEnd() => _status.Text = "— end —";

    private void RunSelfTest()
    {
        var expected = LoadExpected("SC0000.BIN");
        var actual = _host.Captured.ConvertAll(c => c.Offset);
        bool ok = expected != null && actual.Count == expected.Count;
        for (int i = 0; ok && i < actual.Count; i++) ok = actual[i] == expected![i];
        if (ok) GD.Print($"SELFTEST OK: {actual.Count} lines match vm0 trace");
        else GD.Print($"SELFTEST FAIL: cs={actual.Count} expected={(expected?.Count.ToString() ?? "n/a")}");
        GetTree().Quit(ok ? 0 : 1);
    }

    private static List<int>? LoadExpected(string scene)
    {
        string p = System.IO.Path.Combine(Paths.Build, "vm0-trace.json");
        if (!System.IO.File.Exists(p)) return null;
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(p));
        if (!doc.RootElement.TryGetProperty(scene, out var e)) return null;
        var list = new List<int>();
        foreach (var x in e.GetProperty("offsets").EnumerateArray()) list.Add(x.GetInt32());
        return list;
    }
}

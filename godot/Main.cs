using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public partial class Main : Godot.Control
{
    private Control _stage = null!;                       // texture layer (behind the text)
    private readonly Dictionary<int, TextureRect> _slots = new();
    private Label _text = null!;
    private Label _status = null!;
    private AudioStreamPlayer _bgm = null!;                // looping background music
    private AudioStreamPlayer _voice = null!;              // interrupt-on-new voice
    private VirtualMachine _vm = null!;
    private GodotAdvHost _host = null!;
    private volatile bool _done;
    private bool _ended;
    private bool _selftest;

    public override void _Ready()
    {
        // texture stage, added first so it draws BEHIND the dialogue text
        _stage = new Control();
        _stage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _stage.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_stage);

        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _text.OffsetLeft = 40; _text.OffsetTop = 40; _text.OffsetRight = -40; _text.OffsetBottom = -80;
        AddChild(_text);
        _status = new Label();
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _status.OffsetLeft = 40; _status.OffsetTop = -60;
        AddChild(_status);

        // Best-effort CJK font so the visual isn't tofu (headless self-test doesn't depend on it).
        foreach (var fp in new[] { "C:/Windows/Fonts/YuGothM.ttc", "C:/Windows/Fonts/YuGothR.ttc",
                                   "C:/Windows/Fonts/msgothic.ttc", "C:/Windows/Fonts/meiryo.ttc" })
        {
            if (!System.IO.File.Exists(fp)) continue;
            try
            {
                var ff = new FontFile { Data = System.IO.File.ReadAllBytes(fp) };
                _text.AddThemeFontOverride("font", ff);
                _status.AddThemeFontOverride("font", ff);
                _text.AddThemeFontSizeOverride("font_size", 22);
                break;
            }
            catch { /* fall back to the default font */ }
        }

        _bgm = new AudioStreamPlayer();
        _voice = new AudioStreamPlayer();
        AddChild(_bgm);
        AddChild(_voice);

        _selftest = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--selftest") >= 0;

        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        _host = new GodotAdvHost(this, ResourceMap.Load(), "SC0000");
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

    // _Input (not _UnhandledInput): the root Control consumes mouse clicks as GUI input before they
    // reach _UnhandledInput, so clicks were swallowed while keyboard ui_accept still got through.
    public override void _Input(InputEvent e)
    {
        if (_selftest) return;
        if (e.IsActionPressed("ui_accept") ||
            (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left))
            _host.SignalInput();
    }

    public override void _ExitTree() { _host?.SignalInput(); }

    // ---- UI methods invoked on the main thread via CallDeferred ----
    // Composite a resolved texture into a slot at (x,y) sized (w,h). One TextureRect per slot,
    // layered in draw order (backgrounds are drawn before sprites, so they sit behind).
    public void DrawSlot(int slot, string bmpPath, int x, int y, int w, int h)
    {
        var img = new Image();
        var err = img.LoadBmpFromBuffer(System.IO.File.ReadAllBytes(bmpPath));
        if (err != Error.Ok) { GD.Print($"BMP load failed {bmpPath}: {err}"); return; }
        if (!_slots.TryGetValue(slot, out var tr))
        {
            tr = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _stage.AddChild(tr);
            _slots[slot] = tr;
        }
        tr.Texture = ImageTexture.CreateFromImage(img);
        tr.Position = new Vector2(x, y);
        tr.Size = new Vector2(w > 0 ? w : img.GetWidth(), h > 0 ? h : img.GetHeight());
        tr.Visible = true;
    }

    // Load an OGG off disk and play it. BGM loops; voice plays once, cutting off any prior line.
    public void PlayBgm(string oggPath)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(System.IO.File.ReadAllBytes(oggPath));
        if (stream == null) { GD.Print($"OGG load failed {oggPath}"); return; }
        stream.Loop = true;
        _bgm.Stream = stream;
        _bgm.Play();
    }

    public void PlayVoice(string oggPath)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(System.IO.File.ReadAllBytes(oggPath));
        if (stream == null) { GD.Print($"OGG load failed {oggPath}"); return; }
        stream.Loop = false;
        _voice.Stream = stream;
        _voice.Play();
    }

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

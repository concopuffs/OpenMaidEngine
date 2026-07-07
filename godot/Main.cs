using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public partial class Main : Godot.Control
{
    private TextureRect _screenView = null!;              // shows the composited screen backbuffer
    private Image _screen = null!;                        // 800x600 immediate-mode canvas
    private ImageTexture _screenTex = null!;
    private Label _text = null!;
    private Label _status = null!;
    private AudioStreamPlayer _bgm = null!;                // looping background music
    private AudioStreamPlayer _voice = null!;              // interrupt-on-new voice
    private VirtualMachine _vm = null!;
    private GodotAdvHost _host = null!;
    private volatile bool _done;
    private bool _ended;
    private bool _selftest;
    private string? _shotPath;                 // --shot <png>: capture a page then quit (dev tool)
    private int _shotPage = 1;                  // --shot-page <n>: which page to capture (default 1)
    private volatile int _pageCount;
    private int _shotSettle;
    private bool _shotDone;

    public override void _Ready()
    {
        // Screen backbuffer: one 800x600 canvas that draw-texture blits into, shown behind the dialogue.
        _screen = Image.CreateEmpty(800, 600, false, Image.Format.Rgba8);
        _screenTex = ImageTexture.CreateFromImage(_screen);
        _screenView = new TextureRect
        {
            Texture = _screenTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _screenView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_screenView);   // added first -> draws behind the text/status labels

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

        var userArgs = OS.GetCmdlineUserArgs();
        _selftest = System.Array.IndexOf(userArgs, "--selftest") >= 0;
        for (int i = 0; i < userArgs.Length; i++)
        {
            if (userArgs[i] == "--shot" && i + 1 < userArgs.Length) _shotPath = userArgs[i + 1];
            if (userArgs[i] == "--shot-page" && i + 1 < userArgs.Length) int.TryParse(userArgs[i + 1], out _shotPage);
        }

        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        _host = new GodotAdvHost(this, ResourceMap.Load(), "SC0000");
        _vm = new VirtualMachine(script, table, _host);
        _ = Task.Run(() => { _vm.Run(); _done = true; });

        if (_selftest)
            _ = Task.Run(async () => { while (!_done) { if (_host.IsWaiting) _host.SignalInput(); await Task.Delay(1); } });
        // --shot: auto-advance up to (but not past) the target page, then _Process captures + quits.
        if (_shotPath != null)
            _ = Task.Run(async () => { while (!_done) { if (_host.IsWaiting && _host.Pages < _shotPage) _host.SignalInput(); await Task.Delay(1); } });
    }

    public override void _Process(double delta)
    {
        // --shot: once the target page is composed and parked at wait-for-input, settle a few frames then grab it.
        if (_shotPath != null && !_shotDone && (_host.Pages >= _shotPage && _host.IsWaiting || _done))
        {
            if (++_shotSettle >= 3)
            {
                _shotDone = true;
                var img = GetViewport().GetTexture().GetImage();
                img.SavePng(_shotPath);
                GD.Print($"SHOT saved page {_pageCount} -> {_shotPath}");
                GetTree().Quit(0);
            }
            return;
        }
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
    // Blit a source BMP (src rect) onto the screen backbuffer at (dstX,dstY), then refresh the display
    // texture. Execution order == paint order, so later draws (sprites) land over earlier ones (bg).
    public void BlitSlot(string bmpPath, int srcX, int srcY, int w, int h, int dstX, int dstY)
    {
        var src = new Image();
        if (src.LoadBmpFromBuffer(System.IO.File.ReadAllBytes(bmpPath)) != Error.Ok)
        { GD.Print($"BMP load failed {bmpPath}"); return; }
        if (src.GetFormat() != Image.Format.Rgba8) src.Convert(Image.Format.Rgba8);

        // Clamp the source rect to the image; a zero/negative size falls back to the full image.
        int sw = w > 0 ? w : src.GetWidth();
        int sh = h > 0 ? h : src.GetHeight();
        sw = System.Math.Min(sw, src.GetWidth() - srcX);
        sh = System.Math.Min(sh, src.GetHeight() - srcY);
        if (sw <= 0 || sh <= 0) return;

        _screen.BlitRect(src, new Rect2I(srcX, srcY, sw, sh), new Vector2I(dstX, dstY));
        _screenTex.Update(_screen);
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
    public void PageBreak() { _pageCount++; _status.Text = "▼ click / Enter"; }
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

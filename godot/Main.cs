using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Script = Age.Engine.Model.Script;   // disambiguate from Godot.Script

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
    private GodotTraceSink _trace = null!;
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
        bool boot = System.Array.IndexOf(userArgs, "--boot") >= 0;   // run SYSTEM4's state prefix first
        string scene = "SC0000";                        // --scene <NAME>: which scene to play (default SC0000)
        var seeds = new List<(int Addr, long Val)>();   // --seed 0xADDR=VAL (repeatable) — initial global state
        for (int i = 0; i < userArgs.Length; i++)
        {
            if (userArgs[i] == "--scene" && i + 1 < userArgs.Length) scene = userArgs[i + 1];
            if (userArgs[i] == "--shot" && i + 1 < userArgs.Length) _shotPath = userArgs[i + 1];
            if (userArgs[i] == "--shot-page" && i + 1 < userArgs.Length) int.TryParse(userArgs[i + 1], out _shotPage);
            if (userArgs[i] == "--seed" && i + 1 < userArgs.Length)
            {
                var kv = userArgs[i + 1].Split('=');
                if (kv.Length == 2)
                {
                    int k = kv[0].StartsWith("0x") ? System.Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
                    long v = kv[1].StartsWith("0x") ? System.Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
                    seeds.Add((k, v));
                }
            }
        }

        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // Full op handling everywhere: the provider lets call-script load & run subroutines. Selftest
        // runs a SYNTHESIZED scene (not a real scene in a crippled mode) so its output is deterministic.
        Script script;
        IScriptProvider provider;
        if (_selftest) (script, provider) = BuildSelfTestScene(table);
        else { script = Sys4Loader.Load(Paths.Scripts()[scene.ToUpperInvariant() + ".BIN"], table); provider = Sys4ScriptProvider.Load(table); }
        _host = new GodotAdvHost(this, ResourceMap.Load(), scene);
        _trace = new GodotTraceSink();
        _vm = new VirtualMachine(script, table, _host, new VmOptions(MaxSteps: 20_000_000), provider, _trace);
        // --boot: run SYSTEM4's state prefix (INITCONFIG/INIT2/INIT) so the scene sees boot state — chiefly
        // INIT2's gfx handle array 0x62455.. (skips the UI scripts LOGO/OP/TITLE). State carries via globals.
        if (boot && !_selftest)
        {
            var session = new GameSession();
            foreach (var b in new[] { "INITCONFIG.BIN", "INIT2.BIN", "INIT.BIN" })
                session.RunScene(Sys4Loader.Load(Paths.Scripts()[b], table), table, new CaptureHost(), null, provider);
            foreach (var kv in session.Globals) _vm.Globals[kv.Key] = kv.Value;
            foreach (var kv in session.GlobalStrings) _vm.GlobalStrings[kv.Key] = kv.Value;
            GD.Print($"[boot] system boot done: {session.Globals.Count} globals seeded");
        }
        foreach (var (addr, val) in seeds) _vm.Globals[addr] = val;   // --seed overrides boot state
        _ = Task.Run(() => { _vm.Run(); _done = true; });

        if (_selftest)
            _ = Task.Run(async () => { while (!_done) { if (_host.IsWaiting) _host.SignalInput(); await Task.Delay(1); } });
        // --shot: auto-advance up to (but not past) the target page, then _Process captures + quits.
        if (_shotPath != null)
            _ = Task.Run(async () => { while (!_done) { if (_host.IsWaiting && _host.Pages < _shotPage) _host.SignalInput(); await Task.Delay(1); } });
    }

    public override void _Process(double delta)
    {
        if (!_selftest && _vm != null) Recomposite();   // retained per-frame compositor (surface+object model)
        // --shot: once the target page is composed and parked at wait-for-input, settle a few frames then grab it.
        if (_shotPath != null && !_shotDone && (_host.Pages >= _shotPage && _host.IsWaiting || _done))
        {
            if (++_shotSettle >= 3)
            {
                _shotDone = true;
                var img = GetViewport().GetTexture().GetImage();
                img.SavePng(_shotPath);
                GD.Print($"SHOT saved page {_pageCount} -> {_shotPath}");
                ReportSubroutines();
                GetTree().Quit(0);
            }
            return;
        }
        if (_done && !_ended)
        {
            _ended = true;
            ReportSubroutines();
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

    // ---- retained per-frame compositor (main thread, from _Process) ----
    // Clear the screen and composite the VM's current VISIBLE gfx objects in ascending-handle order (= the
    // engine's z-order), each blitting its live surface's rect at its position. Surfaces are cached by BMP
    // path (this runs every frame). Alpha/colorkey/animation come in later phases (objects opaque here).
    private readonly System.Collections.Generic.Dictionary<string, Image?> _imgCache = new();

    private void Recomposite()
    {
        _screen.Fill(new Color(0, 0, 0, 0));
        foreach (var v in _vm.Gfx.SnapshotVisibleObjects())   // already ascending-handle = z-order
        {
            if (v.SurfaceResId == 0) continue;                 // render-target/blank surface (no file) — later phase
            var bmp = _host.ResolveResIdTexture(v.SurfaceResId);
            if (bmp != null) BlitLayer(bmp, v.SrcX, v.SrcY, v.W, v.H, v.DstX, v.DstY);
        }
        _screenTex.Update(_screen);
    }

    private void BlitLayer(string bmpPath, int srcX, int srcY, int w, int h, int dstX, int dstY)
    {
        if (!_imgCache.TryGetValue(bmpPath, out var src))
        {
            src = new Image();
            if (src.LoadBmpFromBuffer(System.IO.File.ReadAllBytes(bmpPath)) != Error.Ok)
            { GD.Print($"BMP load failed {bmpPath}"); src = null; }
            else if (src.GetFormat() != Image.Format.Rgba8) src.Convert(Image.Format.Rgba8);
            _imgCache[bmpPath] = src;
        }
        if (src == null) return;
        // Clamp the source rect to the image; a zero/negative size falls back to the full image.
        int sw = w > 0 ? w : src.GetWidth();
        int sh = h > 0 ? h : src.GetHeight();
        sw = System.Math.Min(sw, src.GetWidth() - srcX);
        sh = System.Math.Min(sh, src.GetHeight() - srcY);
        if (sw <= 0 || sh <= 0) return;
        _screen.BlitRect(src, new Rect2I(srcX, srcY, sw, sh), new Vector2I(dstX, dstY));
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

    // The selftest verifies the GODOT PLUMBING (background thread + semaphore suspend on wait-for-input
    // + CallDeferred marshalling) drives the VM faithfully — i.e. produces the SAME output as a plain
    // in-process run of the identical scene. Full op handling is on (the synthetic scene includes a real
    // nested call-script); the expected value is computed live from a headless run, not a frozen golden.
    // Reports (from the main thread) the call-scripts the VM executed as nested subroutines this run.
    private void ReportSubroutines()
    {
        var ids = new List<long>();
        while (_trace.CallScripts.TryDequeue(out var id)) ids.Add(id);
        if (ids.Count == 0) { GD.Print("[subroutines] none dispatched on this path"); return; }
        var distinct = new List<string>();
        foreach (var id in ids) { var h = "0x" + id.ToString("x"); if (!distinct.Contains(h)) distinct.Add(h); }
        GD.Print($"[subroutines] {ids.Count} call-scripts executed as nested frames ({distinct.Count} distinct: {string.Join(", ", distinct)})");
    }

    private void RunSelfTest()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var (script, provider) = BuildSelfTestScene(table);
        var headless = new VirtualMachine(script, table, new CaptureHost(), null, provider);
        headless.Run();
        var expected = headless.Emitted.ConvertAll(e => e.Offset);

        var actual = _host.Captured.ConvertAll(c => c.Offset);
        bool ok = actual.Count == expected.Count;
        for (int i = 0; ok && i < actual.Count; i++) ok = actual[i] == expected[i];
        if (ok) GD.Print($"SELFTEST OK: threaded host matches headless ({actual.Count} lines, full handling)");
        else GD.Print($"SELFTEST FAIL: threaded={actual.Count} vs headless={expected.Count}");
        GetTree().Quit(ok ? 0 : 1);
    }

    // A deterministic synthesized scene: show-text, wait-for-input (exercises the suspend plumbing), a
    // nested call-script into a synthetic subroutine (exercises call-script handling), shared globals.
    private static (Script, IScriptProvider) BuildSelfTestScene(OpcodeTable table)
    {
        (int, Operand[]) ShowText(int s) => (0x6e, new[] { new Operand(2, s), new Operand(0, 0) });
        (int, Operand[]) Wait() => (0x72, new[] { new Operand(0, 0) });
        (int, Operand[]) CallScript(long id) => (0x3, new[] { new Operand(0, id) });
        (int, Operand[]) MovGG(int d, int s) => (0x55, new[] { new Operand(3, d), new Operand(3, s) });
        (int, Operand[]) MovGI(int d, long v) => (0x55, new[] { new Operand(3, d), new Operand(0, v) });
        (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

        var callee = ScriptAssembler.Assemble(table, "SUBSCENE",
            new List<(int, Operand[])> { ShowText(0), MovGI(0x31, 42), Exit() }, new[] { "Sub" });
        var caller = ScriptAssembler.Assemble(table, "SELFTEST",
            new List<(int, Operand[])> { ShowText(0), Wait(), ShowText(1), CallScript(5), MovGG(0x30, 0x31), Exit() },
            new[] { "Hello", "World" });
        return (caller, new SelfTestProvider(callee));
    }

    private sealed class SelfTestProvider : IScriptProvider
    {
        private readonly Script _callee;
        public SelfTestProvider(Script callee) => _callee = callee;
        public Script? GetById(long id) => id == 5 ? _callee : null;
    }
}

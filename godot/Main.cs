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
    private readonly Age.Engine.Hosting.FrameClock _clock = new();
    private GodotTraceSink _trace = null!;
    private Age.Engine.Diagnostics.HistogramTraceSink? _hist;   // --trace-histogram: profile the real run
    private string? _histFile;
    private Age.Engine.Model.OpcodeTable? _table;
    private bool _histDumped;
    private volatile bool _done;
    private bool _ended;
    private bool _selftest;
    private string? _shotPath;                 // --shot <png>: capture a page then quit (dev tool)
    private int _shotPage = 1;                  // --shot-page <n>: which page to capture (default 1)
    private volatile int _pageCount;
    private int _shotSettle;
    private int _shotSettleTarget = 3;          // --shot-settle <frames>: settle N frames before grabbing (to
                                                // capture mid-tween — the anim clock keeps running while parked)
    private bool _shotDone;
    private string? _seqDir;                    // --shot-sequence <dir>: dump one PNG per frame (verify paced anim)
    private int _seqFrames = 180;               // --frames <n>: how many frames to dump (default ~3s @60fps)
    private int _seqIdx;
    private string? _gfxLogPath;                // --gfx-log <file>: log per-object compositor draw/skip CHANGES
    private System.IO.StreamWriter? _gfxLog;
    private readonly System.Collections.Generic.Dictionary<long, string> _lastGfxDecision = new();
    private int _gfxLogFrame;
    private string? _timelineLogPath;            // --timeline-log <jsonl>: synchronized VM/host/compositor evidence
    private GodotTimelineLog? _timeline;
    private int _timelineFrame;

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
        double sleepScale = 1.0;                         // --sleep-scale <f>: slow/speed the paced opening for inspection
        double speed = 1.0;                              // --speed <f>: whole-runtime diagnostic speed
        string? histFile = null;                         // --trace-histogram <file>: op/call-site execution counts of the REAL run
        for (int i = 0; i < userArgs.Length; i++)
        {
            if (userArgs[i] == "--scene" && i + 1 < userArgs.Length) scene = userArgs[i + 1];
            if (userArgs[i] == "--shot" && i + 1 < userArgs.Length) _shotPath = userArgs[i + 1];
            if (userArgs[i] == "--shot-page" && i + 1 < userArgs.Length) int.TryParse(userArgs[i + 1], out _shotPage);
            if (userArgs[i] == "--shot-settle" && i + 1 < userArgs.Length) int.TryParse(userArgs[i + 1], out _shotSettleTarget);
            if (userArgs[i] == "--shot-sequence" && i + 1 < userArgs.Length) _seqDir = userArgs[i + 1];
            if (userArgs[i] == "--gfx-log" && i + 1 < userArgs.Length) _gfxLogPath = userArgs[i + 1];
            if (userArgs[i] == "--timeline-log" && i + 1 < userArgs.Length) _timelineLogPath = userArgs[i + 1];
            if (userArgs[i] == "--frames" && i + 1 < userArgs.Length) int.TryParse(userArgs[i + 1], out _seqFrames);
            if (userArgs[i] == "--sleep-scale" && i + 1 < userArgs.Length) double.TryParse(userArgs[i + 1], out sleepScale);
            if (userArgs[i] == "--speed" && i + 1 < userArgs.Length) double.TryParse(userArgs[i + 1], out speed);
            if (userArgs[i] == "--trace-histogram" && i + 1 < userArgs.Length) histFile = userArgs[i + 1];
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

        if (!double.IsFinite(speed) || speed <= 0) speed = 1.0;
        _clock.Speed = System.Math.Clamp(speed, 0.05, 8.0);

        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // Full op handling everywhere: the provider lets call-script load & run subroutines. Selftest
        // runs a SYNTHESIZED scene (not a real scene in a crippled mode) so its output is deterministic.
        Script script;
        IScriptProvider provider;
        if (_selftest) (script, provider) = BuildSelfTestScene(table);
        else { script = Sys4Loader.Load(Paths.Scripts()[scene.ToUpperInvariant() + ".BIN"], table); provider = Sys4ScriptProvider.Load(table); }
        if (_timelineLogPath != null) _timeline = new GodotTimelineLog(_timelineLogPath);
        _host = new GodotAdvHost(this, ResourceMap.Load(), scene, _clock, _timeline) { SleepScale = sleepScale, TraceOps = _gfxLogPath != null };
        _trace = new GodotTraceSink(_timeline);
        // --trace-histogram: aggregate op/call-site execution counts of the REAL Godot run (headless flow
        // diverges — wait-for-input is a no-op there — so this is the only way to profile the live path).
        _table = table;
        _histFile = histFile;
        Age.Engine.Diagnostics.ITraceSink sink = _trace;
        if (histFile != null) { _hist = new Age.Engine.Diagnostics.HistogramTraceSink();
                                 sink = new Age.Engine.Diagnostics.CompositeTraceSink(_trace, _hist); }
        _vm = new VirtualMachine(script, table, _host, new VmOptions(MaxSteps: 20_000_000), provider, sink);
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
        // --shot-sequence: auto-advance past every input wait so the paced burst isn't blocked on a click.
        if (_seqDir != null)
            _ = Task.Run(async () => { while (!_done) { if (_host.IsWaiting) _host.SignalInput(); await Task.Delay(1); } });
    }

    public override void _Process(double delta)
    {
        _clock.Advance(delta);
        _timeline?.SetFrame(++_timelineFrame, _clock.NowMs);
        _host?.PulseFrame();
        if (!_selftest && _vm != null) Recomposite();   // retained per-frame compositor (surface+object model)
        // --shot-sequence: dump one PNG per frame across the opening so a time-based (paced) effect can be
        // verified as distinct frames, not just the final state. Captures after Recomposite; quits when full.
        if (_seqDir != null && _seqIdx < _seqFrames && !_done)
        {
            System.IO.Directory.CreateDirectory(_seqDir);
            // Headless has no rendered viewport texture (GetImage() is null). Still advance/count/quit so the
            // real-run trace-histogram can profile the live path without a display; only the PNG grab is skipped.
            var fimg = GetViewport().GetTexture()?.GetImage();
            fimg?.SavePng($"{_seqDir}/frame_{_seqIdx:0000}.png");
            _seqIdx++;
            if (_seqIdx >= _seqFrames) { GD.Print($"SEQ saved {_seqIdx} frames -> {_seqDir}"); GetTree().Quit(0); }
            return;
        }
        // --shot: once the target page is composed and parked at wait-for-input, settle a few frames then grab it.
        if (_shotPath != null && !_shotDone && (_host.Pages >= _shotPage && _host.IsWaiting || _done))
        {
            if (++_shotSettle >= _shotSettleTarget)
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
            DumpHistogram();
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

    public override void _ExitTree() { DumpHistogram(); _host?.Stop(); _timeline?.Dispose(); }

    // Write the real-run op/call-site histogram to --trace-histogram <file>. Idempotent; called when the
    // scene ends or the window closes (the opening parks at wait-for-input, so closing is the usual trigger).
    private void DumpHistogram()
    {
        if (_histDumped || _hist == null || _histFile == null) return;
        _histDumped = true;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_histFile);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            using var w = new System.IO.StreamWriter(_histFile);
            _hist.WriteReport(w, _table);
            GD.Print($"[trace-histogram] wrote {_hist.TotalSteps} steps -> {_histFile}");
        }
        catch (System.Exception e) { GD.Print($"[trace-histogram] write failed: {e.Message}"); }
    }

    // ---- retained per-frame compositor (main thread, from _Process) ----
    // Clear the screen and composite the VM's current VISIBLE gfx objects in ascending-handle order (= the
    // engine's z-order), each blitting its live surface's rect at its position. Surfaces are cached by BMP
    // path (this runs every frame). Native scale/translation matrix channels are sampled independently by
    // GfxState and applied here; object opacity comes only from the actual blend/color path.
    private readonly System.Collections.Generic.Dictionary<(string Path, long Key), Image?> _imgCache = new();

    private void Recomposite()
    {
        _screen.Fill(new Color(0, 0, 0, 0));
        System.Collections.Generic.Dictionary<long, string>? decisions = _gfxLogPath != null || _timeline != null ? new() : null;
        int z = 0;
        foreach (var v in _vm.Gfx.SnapshotVisibleObjects(_clock.NowMs))   // interpolate at the throttled clock
        {
            var t = v.Transform;
            var affine = Age.Engine.Model.Transform2DMath.Build(t, v.Rotation);
            var localToDest = affine.FromLocalOrigin(v.DstX, v.DstY);
            var projected = localToDest.Apply(0, 0);
            int dstX = (int)System.Math.Round(projected.X);
            int dstY = (int)System.Math.Round(projected.Y);
            float opacity = v.Alpha / 255f;                  // transform Z is never opacity
            float strength = v.TintStrength / 255f;          // tint-blend / fill strength
            string outcome;
            if (v.SurfaceResId == 0)
            {
                // A colored object with no bound surface = a fade/flash fill (e.g. fade-to-black). Its presence
                // is the tint STRENGTH (0=absent, 255=solid), scaled by any object opacity. Uncolored surfaceless
                // objects are render targets — still skipped (slice C).
                if (v.Blend != Age.Engine.Model.BlendKind.Opaque)
                {
                    int baseW = v.W > 0 ? v.W : 800, baseH = v.H > 0 ? v.H : 600;
                    float fillA = opacity * strength;
                    FillAffineQuad(baseW, baseH, localToDest, v.Tint, fillA);
                    outcome = $"FILL tint=0x{v.Tint:x6} a={fillA:0.00} {baseW}x{baseH}@({dstX},{dstY}) " +
                              $"base=({v.DstX},{v.DstY}) anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) " +
                              $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) " +
                              $"trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) rot={v.Rotation.AngleDegrees:0.0}";
                }
                else outcome = "SKIP(no-resId, opaque render-target)";
            }
            else
            {
                var bmp = _host.ResolveResIdTexture(v.SurfaceResId);
                if (bmp == null) outcome = $"SKIP(resId=0x{v.SurfaceResId:x} UNRESOLVED)";
                else
                {
                    BlitLayer(bmp, v.ColorKey, v.Tint, strength, v.SrcX, v.SrcY, v.W, v.H,
                              localToDest, opacity);
                    var raw = _vm.Gfx.TryGet(v.Handle);
                    outcome = $"slot={raw?.SourceSlot} DRAWN resId=0x{v.SurfaceResId:x} {System.IO.Path.GetFileName(bmp)} " +
                              $"src=({v.SrcX},{v.SrcY} {v.W}x{v.H}) base=({v.DstX},{v.DstY}) " +
                              $"anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) dst=({dstX},{dstY}) " +
                              $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) " +
                              $"rot=({t.RotationAngleDegrees:0.0}+{v.Rotation.AngleDegrees:0.0}) " +
                              $"op={opacity:0.00} tintStr={strength:0.00}";
                }
            }
            decisions?.Add(v.Handle, $"z{z} {outcome}");
            z++;
        }
        _screenTex.Update(_screen);
        if (decisions != null) LogGfxDecisionChanges(decisions);
    }

    // Diagnostic (--gfx-log): print, per rendered frame, only the objects whose compositor outcome CHANGED
    // since last frame (added / gone / drawn↔skip / resId change). Quiet until something actually changes, so
    // the frame where the background drops out — and WHY — stands out. See systematic-debugging of the grey-BG.
    private void LogGfxDecisionChanges(System.Collections.Generic.Dictionary<long, string> curr)
    {
        if (_gfxLogPath != null && _gfxLog == null)
        {
            var dir = System.IO.Path.GetDirectoryName(_gfxLogPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            _gfxLog = new System.IO.StreamWriter(_gfxLogPath!) { AutoFlush = true };
        }
        _gfxLogFrame++;
        var lines = new System.Collections.Generic.List<string>();
        foreach (var kv in curr)
            if (!_lastGfxDecision.TryGetValue(kv.Key, out var prev) || prev != kv.Value)
                lines.Add($"  0x{kv.Key:x}: {kv.Value}" + (_lastGfxDecision.ContainsKey(kv.Key) ? "" : "  [NEW]"));
        foreach (var kv in _lastGfxDecision)
            if (!curr.ContainsKey(kv.Key))
                lines.Add($"  0x{kv.Key:x}: GONE (was {kv.Value})");
        if (lines.Count > 0 && _gfxLog != null)
        {
            _gfxLog.WriteLine($"[frame {_gfxLogFrame} nowMs={_clock.NowMs} page={_pageCount}] {curr.Count} visible, {lines.Count} changes:");
            foreach (var l in lines) _gfxLog.WriteLine(l);
        }
        if (lines.Count > 0)
            _timeline?.Event("objects", new() { ["visible_count"] = curr.Count, ["changes"] = lines.ToArray() });
        _lastGfxDecision.Clear();
        foreach (var kv in curr) _lastGfxDecision[kv.Key] = kv.Value;
    }

    // Blit one object's surface rect. The source Image is cached per (path, colorKey): on first load, texels
    // matching the surface colorkey are made transparent (native bakes the key at load — engine-re.md §Blend).
    // tintStrength (0..1, the op 0x202/0x203 alpha) LERPs the texel RGB toward tint (0=keep texel, 1=full tint;
    // fade-to-black uses tint=black, strength=1); alpha is the object's OPACITY (independent of the tint).
    private void BlitLayer(string bmpPath, long colorKey, long tint, float tintStrength, int srcX, int srcY, int w, int h,
                           Age.Engine.Model.Affine2D localToDest, float alpha = 1f)
    {
        var cacheKey = (bmpPath, colorKey);
        if (!_imgCache.TryGetValue(cacheKey, out var src))
        {
            src = new Image();
            if (src.LoadBmpFromBuffer(System.IO.File.ReadAllBytes(bmpPath)) != Error.Ok)
            { GD.Print($"BMP load failed {bmpPath}"); src = null; }
            else
            {
                if (src.GetFormat() != Image.Format.Rgba8) src.Convert(Image.Format.Rgba8);
                if (Age.Engine.Model.BlendMath.HasColorKey(colorKey)) BakeColorKey(src, colorKey);
            }
            _imgCache[cacheKey] = src;
        }
        if (src == null) return;

        int sw = w > 0 ? w : src.GetWidth();
        int sh = h > 0 ? h : src.GetHeight();
        sw = System.Math.Min(sw, src.GetWidth() - srcX);
        sh = System.Math.Min(sh, src.GetHeight() - srcY);
        if (sw <= 0 || sh <= 0) return;
        byte[] dst = _screen.GetData(); byte[] ss = src.GetData();
        Age.Engine.Model.SoftwareAffineRasterizer.BlitRgba(
            dst, _screen.GetWidth(), _screen.GetHeight(), ss, src.GetWidth(), src.GetHeight(),
            srcX, srcY, sw, sh, localToDest, tint, tintStrength, alpha);
        _screen.SetData(_screen.GetWidth(), _screen.GetHeight(), false, _screen.GetFormat(), dst);
    }

    private void FillAffineQuad(int w, int h, Age.Engine.Model.Affine2D localToDest, long tint, float alpha)
    {
        byte[] dst = _screen.GetData();
        Age.Engine.Model.SoftwareAffineRasterizer.FillRgba(
            dst, _screen.GetWidth(), _screen.GetHeight(), w, h, localToDest, tint, alpha);
        _screen.SetData(_screen.GetWidth(), _screen.GetHeight(), false, _screen.GetFormat(), dst);
    }

    // Alpha-blend a solid tint (0xRRGGBB) rectangle over the screen — the surfaceless fade/flash fill.
    private void FillQuad(int dstX, int dstY, int w, int h, long tint, float alpha)
    {
        int ia = (int)(System.Math.Clamp(alpha, 0f, 1f) * 255);
        if (ia == 0) return;
        int tr = (int)((tint >> 16) & 0xff), tg = (int)((tint >> 8) & 0xff), tb = (int)(tint & 0xff);
        byte[] dst = _screen.GetData();
        int dw = _screen.GetWidth(), dh = _screen.GetHeight();
        int x0 = System.Math.Max(0, -dstX), x1 = System.Math.Min(w, dw - dstX);
        int y0 = System.Math.Max(0, -dstY), y1 = System.Math.Min(h, dh - dstY);
        if (x1 <= x0 || y1 <= y0) return;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int dxp = dstX + x, dyp = dstY + y;
                int di = (dyp * dw + dxp) * 4;
                dst[di]     = (byte)((tr * ia + dst[di]     * (255 - ia)) / 255);
                dst[di + 1] = (byte)((tg * ia + dst[di + 1] * (255 - ia)) / 255);
                dst[di + 2] = (byte)((tb * ia + dst[di + 2] * (255 - ia)) / 255);
                dst[di + 3] = (byte)System.Math.Min(255, dst[di + 3] + ia);
            }
        _screen.SetData(dw, dh, false, _screen.GetFormat(), dst);
    }

    // Make colorkey-matching texels transparent (native colorkey is baked at surface load).
    private static void BakeColorKey(Image img, long colorKey)
    {
        byte[] px = img.GetData();
        int w = img.GetWidth(), h = img.GetHeight();
        for (int i = 0; i < px.Length; i += 4)
            if (Age.Engine.Model.BlendMath.ColorKeyMatches(px[i], px[i + 1], px[i + 2], colorKey))
                px[i + 3] = 0;
        img.SetData(w, h, false, img.GetFormat(), px);
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

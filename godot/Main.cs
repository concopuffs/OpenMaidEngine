using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Script = Age.Engine.Model.Script;   // disambiguate from Godot.Script

[SupportedOSPlatform("windows")]
public partial class Main : Godot.Control
{
    private const int ScreenWidth = 800;
    private const int ScreenHeight = 600;
    private TextureRect _screenView = null!;              // shows the composited screen backbuffer
    private Image _screen = null!;                        // 800x600 immediate-mode canvas
    private ImageTexture _screenTex = null!;
    private TextureRect _waitIndicator = null!;
    private ImageTexture? _waitIndicatorSheet;
    private AtlasTexture? _waitIndicatorAtlas;
    private int _waitIndicatorAssetId = -1;
    // One managed composition target for the entire frame. Layer helpers mutate it in place; only the
    // completed frame crosses the Godot Image boundary, avoiding a full GetData/SetData round-trip per layer.
    private readonly byte[] _screenPixels = new byte[ScreenWidth * ScreenHeight * 4];
    private Label _text = null!;
    private Label _speaker = null!;
    private Label _status = null!;
    private Label _locatorHud = null!;
    private AudioStreamPlayer _bgm = null!;                // looping background music
    private AudioStreamPlayer _voice = null!;              // interrupt-on-new voice
    private int _voiceQueuedGeneration;
    private int _voiceStartedGeneration;
    private int _voiceCompletedGeneration;
    private readonly AudioStreamPlayer[] _sfx = new AudioStreamPlayer[10]; // SC0000 channels 0..9
    private VirtualMachine _vm = null!;
    private GodotAdvHost _host = null!;
    private readonly Age.Engine.Hosting.FrameClock _clock = new();
    private readonly System.Collections.Generic.Dictionary<long, MovieRuntime> _movies = new();
    private readonly System.Collections.Generic.HashSet<long> _movieFrameSeen = new();
    private GodotTraceSink _trace = null!;
    private PageLocatorState _locator = null!;
    private bool _locatorHudVisible;
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
        _screen = Image.CreateEmpty(ScreenWidth, ScreenHeight, false, Image.Format.Rgba8);
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

        // Native ADV wait marker: a tiny independently animated atlas region. Keeping it separate from the
        // 800x600 software backbuffer avoids recompositing the entire retained scene throughout static waits.
        _waitIndicator = new TextureRect
        {
            Visible = false,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Keep,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_waitIndicator);

        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        _text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_text);
        _speaker = new Label { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _speaker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_speaker);
        _status = new Label();
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _status.OffsetLeft = 40; _status.OffsetTop = -60;
        AddChild(_status);
        _locatorHud = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _locatorHud.Position = new Vector2(8, 8);
        AddChild(_locatorHud);

        // Best-effort CJK font so the visual isn't tofu (headless self-test doesn't depend on it).
        foreach (var fp in new[] { "C:/Windows/Fonts/msgothic.ttc", "C:/Windows/Fonts/YuGothM.ttc",
                                   "C:/Windows/Fonts/YuGothR.ttc", "C:/Windows/Fonts/meiryo.ttc" })
        {
            if (!System.IO.File.Exists(fp)) continue;
            try
            {
                var ff = new FontFile { Data = System.IO.File.ReadAllBytes(fp) };
                _text.AddThemeFontOverride("font", ff);
                _speaker.AddThemeFontOverride("font", ff);
                _status.AddThemeFontOverride("font", ff);
                _locatorHud.AddThemeFontOverride("font", ff);
                _text.AddThemeFontSizeOverride("font_size", 25);
                _speaker.AddThemeFontSizeOverride("font_size", 25);
                _text.AddThemeConstantOverride("outline_size", 1);
                _speaker.AddThemeConstantOverride("outline_size", 1);
                var outline = new Color(0x60 / 255f, 0x60 / 255f, 0x60 / 255f, 1);
                _text.AddThemeColorOverride("font_outline_color", outline);
                _speaker.AddThemeColorOverride("font_outline_color", outline);
                break;
            }
            catch { /* fall back to the default font */ }
        }

        _bgm = new AudioStreamPlayer();
        _voice = new AudioStreamPlayer();
        AddChild(_bgm);
        AddChild(_voice);
        for (int i = 0; i < _sfx.Length; i++)
        {
            _sfx[i] = new AudioStreamPlayer();
            AddChild(_sfx[i]);
        }

        var userArgs = OS.GetCmdlineUserArgs();
        _selftest = System.Array.IndexOf(userArgs, "--selftest") >= 0;
        bool boot = System.Array.IndexOf(userArgs, "--boot") >= 0;   // run SYSTEM4's state prefix first
        string scene = "SC0000";                        // --scene <NAME>: which scene to play (default SC0000)
        var seeds = new List<(int Addr, long Val)>();   // --seed 0xADDR=VAL (repeatable) — initial global state
        double sleepScale = 1.0;                         // --sleep-scale <f>: scale explicit op-0xc8 holds
        double speed = 1.0;                              // --speed <f>: sleeps + retained presentation clocks
        long transitionClickMs = -1;                    // --transition-click-ms <n>: force active transitions after n virtual ms
        string? histFile = null;                         // --trace-histogram <file>: op/call-site execution counts of the REAL run
        string? pageMapPath = null;                      // --page-map <jsonl>: override default build/page-map-SCxxxx.jsonl
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
            if (userArgs[i] == "--transition-click-ms" && i + 1 < userArgs.Length) long.TryParse(userArgs[i + 1], out transitionClickMs);
            if (userArgs[i] == "--trace-histogram" && i + 1 < userArgs.Length) histFile = userArgs[i + 1];
            if (userArgs[i] == "--page-map" && i + 1 < userArgs.Length) pageMapPath = userArgs[i + 1];
            if (userArgs[i] == "--locator-hud") _locatorHudVisible = true;
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
        Sys4ScriptProvider? scripts = null;
        if (_selftest) (script, provider) = BuildSelfTestScene(table);
        else { scripts = Sys4ScriptProvider.Load(table); script = scripts.RequireByName(scene + ".BIN"); provider = scripts; }
        if (_timelineLogPath != null) _timeline = new GodotTimelineLog(_timelineLogPath);
        if (!_selftest && pageMapPath == null)
            pageMapPath = System.IO.Path.Combine(Paths.Build, $"page-map-{scene.ToUpperInvariant()}.jsonl");
        _locator = new PageLocatorState(scene, _selftest ? null : pageMapPath);
        _locatorHud.Visible = _locatorHudVisible;
        var resources = scripts != null ? new ResourceMap(scripts.Catalog) : ResourceMap.Load();
        _host = new GodotAdvHost(this, resources, scene, _clock, _locator, _timeline) { SleepScale = sleepScale, TraceOps = _gfxLogPath != null };
        _trace = new GodotTraceSink(_locator, _timeline);
        // --trace-histogram: aggregate op/call-site execution counts of the REAL Godot run (headless flow
        // diverges — wait-for-input is a no-op there — so this is the only way to profile the live path).
        _table = table;
        _histFile = histFile;
        Age.Engine.Diagnostics.ITraceSink sink = _trace;
        if (histFile != null) { _hist = new Age.Engine.Diagnostics.HistogramTraceSink();
                                 sink = new Age.Engine.Diagnostics.CompositeTraceSink(_trace, _hist); }
        _vm = new VirtualMachine(script, table, _host, new VmOptions(MaxSteps: 20_000_000), provider, sink);
        // SYSTEM4 loads the shared SO001 chrome sheet into surface slot 17 before any scene runs.
        // Seed that inherited retained-surface state without replaying the entrypoint's unrelated UI flow.
        if (!_selftest && resources.ResolveName("SO001.AGF") is { } systemChrome)
        {
            _host.SetTexture(systemChrome.RawIndex, 0x11);
            _vm.Gfx.SetSurface(0x11, systemChrome.RawIndex, 0);
        }
        // SYSTEM4 also loads SO000 and configures op 0x73 before entering scene code. The Phase-A
        // single-scene harness does not replay those graphics side effects, so inject their exact state
        // alongside the existing SO001 bootstrap until Phase B runs the complete SYSTEM4 entrypoint.
        if (!_selftest && resources.ResolveName("SO000.AGF") is { } waitIndicator)
        {
            _host.SetTexture(waitIndicator.RawIndex, 0x0c);
            _vm.Gfx.SetSurface(0x0c, waitIndicator.RawIndex, 0xff00);
            _host.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
                1, 385, 140, 0x0c, 0, 0, 30, 27, 12, 48));
        }
        // --boot: run SYSTEM4's state prefix (INITCONFIG/INIT2/INIT) so the scene sees boot state — chiefly
        // INIT2's gfx handle array 0x62455.. (skips the UI scripts LOGO/OP/TITLE). State carries via globals.
        if (boot && !_selftest)
        {
            var session = new GameSession();
            foreach (var b in new[] { "INITCONFIG.BIN", "INIT2.BIN", "INIT.BIN" })
                session.RunScene(scripts!.RequireByName(b), table, new CaptureHost(), null, provider);
            foreach (var kv in session.Globals) _vm.Globals[kv.Key] = kv.Value;
            foreach (var kv in session.GlobalStrings) _vm.GlobalStrings[kv.Key] = kv.Value;
            GD.Print($"[boot] system boot done: {session.Globals.Count} globals seeded");
        }
        // The native SYSTEM4 UI boot enables standard ADV chrome after the data-only *INIT prefix above.
        // Without this inherited value the visible SO001 strip is still drawn, but every ADV script skips
        // its five pointer rectangles and registers only the off-screen keyboard/pad records.
        if (!_selftest) _vm.Globals[0x6c1] = 1;
        // Native AGE owns this transient secondary-SFX channel outside script-visible writes.
        // The matching SC0000 trace has value 4 at 0xc31; seed only this proven profile/slice.
        if (!_selftest && scene.Equals("SC0000", System.StringComparison.OrdinalIgnoreCase))
            _vm.ExternalGlobals[0x6242d] = 4;
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
        if (transitionClickMs >= 0)
            _ = Task.Run(async () =>
            {
                while (!_done)
                {
                    if (_host.IsTransitionWaiting && _host.TransitionStartedAtMs >= 0 &&
                        _clock.NowMs - _host.TransitionStartedAtMs >= transitionClickMs)
                        _host.SignalInput();
                    await Task.Delay(1);
                }
            });
    }

    public override void _Process(double delta)
    {
        _clock.Advance(delta);
        _timelineFrame++;
        _timeline?.SetFrame(_timelineFrame, _clock.NowMs);
        _host?.PulseFrame();
        UpdateVoicePlaybackState();
        UpdateMovieFrames();
        if (!_selftest && _vm != null && _host != null && _host.ShouldRecomposite(_vm.Gfx))
            Recomposite();   // native publishes retained mutations only at present/service boundaries
        if (!_selftest && _host != null) UpdateAdvTextPresentation();
        if (!_selftest && _host != null) UpdateAdvWaitIndicatorPresentation();
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
        if (_shotPath != null && !_shotDone && _host != null && (_host.Pages >= _shotPage && _host.IsWaiting || _done))
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
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F2)
        {
            _locatorHudVisible = !_locatorHudVisible;
            _locatorHud.Visible = _locatorHudVisible;
            if (_locatorHudVisible) _locatorHud.Text = _locator.CurrentDisplay;
            return;
        }
        if (e is InputEventKey copy && copy.Pressed && !copy.Echo && copy.Keycode == Key.F3)
        {
            DisplayServer.ClipboardSet(_locator.CurrentDisplay);
            _locatorHud.Text = _locator.CurrentDisplay + " · copied";
            return;
        }
        if (e is InputEventMouseMotion motion)
        {
            var p = ToNativeScreen(motion.Position);
            _vm.UpdatePointer(p.X, p.Y);
            return;
        }
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            var p = ToNativeScreen(mb.Position);
            if (_vm.TryActivatePointer(p.X, p.Y))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            _host.SignalInput();
            return;
        }
        if (e.IsActionPressed("ui_accept")) _host.SignalInput();
    }

    private (int X, int Y) ToNativeScreen(Vector2 position)
    {
        Vector2 size = GetViewportRect().Size;
        if (size.X <= 0 || size.Y <= 0) return (0, 0);
        return ((int)System.Math.Floor(position.X * ScreenWidth / size.X),
                (int)System.Math.Floor(position.Y * ScreenHeight / size.Y));
    }

    public override void _ExitTree()
    {
        DumpHistogram(); _host?.Stop(); _timeline?.Dispose(); _locator?.Dispose();
        foreach (var movie in _movies.Values) movie.Decoder.Dispose();
        _movies.Clear();
    }

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
    // engine's z-order), each blitting its live surface's rect at its position. Decoded AGF pixels are cached
    // by catalog identity (this runs every frame). Native scale/translation matrix channels are sampled independently by
    // GfxState and applied here; object opacity comes only from the actual blend/color path.
    private sealed record CachedPixels(int Width, int Height, byte[] Rgba);
    private readonly System.Collections.Generic.Dictionary<(int AssetId, long Key), CachedPixels> _pixelCache = new();

    private void Recomposite()
    {
        System.Array.Clear(_screenPixels);
        _speaker.Visible = false;
        System.Collections.Generic.Dictionary<long, string>? decisions = _gfxLogPath != null || _timeline != null ? new() : null;
        int z = 0;
        var visible = _vm.Gfx.SnapshotVisibleObjects(_clock.NowMs); // one synchronized sample for objects + ranges
        foreach (var v in visible)   // interpolate at the retained-presentation clock
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
            if (v.SurfaceTransition is { } transition)
            {
                int layers = DrawTransitionRange(visible, transition);
                outcome = $"TRANSITION slot={transition.TargetSlot} key=0x{transition.CommandKey:x} " +
                          $"progress={transition.Progress:0.000} forced={transition.Forced} layers={layers}";
            }
            else if (v.SurfaceResId == 0)
            {
                // A colored object with no bound surface = a fade/flash fill (e.g. fade-to-black). Its presence
                // is the tint STRENGTH (0=absent, 255=solid), scaled by any object opacity. Uncolored surfaceless
                // objects are render targets — still skipped (slice C).
                if (v.Blend != Age.Engine.Model.BlendKind.Opaque)
                {
                    int baseW = v.W > 0 ? v.W : 800, baseH = v.H > 0 ? v.H : 600;
                    // One-shot/mode-1 packed color supplies opacity directly. Static mode-0 fills retain
                    // the tint-strength convention used by the existing effect objects.
                    float fillA = v.MultiplyTint ? opacity : opacity * strength;
                    FillAffineQuad(baseW, baseH, localToDest, v.Tint, fillA);
                    outcome = $"FILL tint=0x{v.Tint:x6} a={fillA:0.00} {baseW}x{baseH}@({dstX},{dstY}) " +
                              $"base=({v.DstX},{v.DstY}) anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) " +
                              $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) " +
                              $"trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) rot={v.Rotation.AngleDegrees:0.0}" +
                              ColorTimeline(v.ColorTransition);
                }
                else outcome = "SKIP(no-resId, opaque render-target)";
            }
            else
            {
                var texture = _host.ResolveResIdTexture(v.SurfaceResId);
                if (texture == null) outcome = $"SKIP(resId=0x{v.SurfaceResId:x} UNRESOLVED)";
                else
                {
                    BlitLayer(texture.Value.Image, texture.Value.AssetId, v.ColorKey, v.Tint, strength, v.SrcX, v.SrcY, v.W, v.H,
                              localToDest, opacity, v.MultiplyTint, texture.Value.IsDynamic);
                    var raw = _vm.Gfx.TryGet(v.Handle);
                    outcome = $"slot={raw?.SourceSlot} DRAWN resId=0x{v.SurfaceResId:x} {texture.Value.Name} " +
                              $"src=({v.SrcX},{v.SrcY} {v.W}x{v.H}) base=({v.DstX},{v.DstY}) " +
                              $"anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) dst=({dstX},{dstY}) " +
                              $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) " +
                              $"rot=({t.RotationAngleDegrees:0.0}+{v.Rotation.AngleDegrees:0.0}) " +
                              $"mode={raw?.StaticColorMode} op={opacity:0.00} tintStr={strength:0.00}" +
                              ColorTimeline(v.ColorTransition);
                }
            }
            decisions?.Add(v.Handle, $"z{z} {outcome}");
            var rawObject = _vm.Gfx.TryGet(v.Handle);
            if (rawObject != null && _host.TryGetSurfaceText(rawObject.SourceSlot, out var surfaceText))
            {
                var textPos = localToDest.Apply(surfaceText.X, surfaceText.Y);
                _speaker.Position = new Vector2((float)textPos.X, (float)textPos.Y);
                _speaker.Size = new Vector2(System.Math.Max(1, v.W - surfaceText.X), System.Math.Max(1, v.H - surfaceText.Y));
                _speaker.Text = surfaceText.Text;
                _speaker.Visible = true;
            }
            z++;
        }
        _screen.SetData(ScreenWidth, ScreenHeight, false, Image.Format.Rgba8, _screenPixels);
        _screenTex.Update(_screen);
        if (decisions != null) LogGfxDecisionChanges(decisions);
    }

    private void UpdateAdvTextPresentation()
    {
        var t = _host.SnapshotAdvText();
        _text.Position = new Vector2(t.X, 430 + t.Y);
        _text.Size = new Vector2(System.Math.Max(1, 720 - t.X), System.Math.Max(1, 147 - t.Y));
        int count = System.Math.Clamp(t.VisibleGlyphs, 0, t.Text.Length);
        _text.Text = count == 0 ? "" : t.Text[..count];
    }

    private void UpdateAdvWaitIndicatorPresentation()
    {
        var snapshot = _host.SnapshotAdvWaitIndicator();
        if (snapshot == null)
        {
            _waitIndicator.Visible = false;
            return;
        }

        var s = snapshot.Value;
        if (_waitIndicatorAssetId != s.AssetId)
        {
            var image = Image.CreateFromData(s.Image.Width, s.Image.Height, false, Image.Format.Rgba8, s.Image.Pixels);
            _waitIndicatorSheet = ImageTexture.CreateFromImage(image);
            _waitIndicatorAtlas = new AtlasTexture { Atlas = _waitIndicatorSheet };
            _waitIndicator.Texture = _waitIndicatorAtlas;
            _waitIndicatorAssetId = s.AssetId;
        }

        var c = s.Config;
        _waitIndicatorAtlas!.Region = new Rect2(
            c.SourceX + s.Frame * c.CellWidth, c.SourceY, c.CellWidth, c.CellHeight);
        _waitIndicator.Position = new Vector2(c.X, 430 + c.Y);
        _waitIndicator.Size = new Vector2(c.CellWidth, c.CellHeight);
        _waitIndicator.Visible = true;
    }

    private static string ColorTimeline(Age.Engine.Model.ColorTransitionState? state)
        => state is { } c
            ? $" color=0x{c.Current:x8}->0x{c.Target:x8} colorProgress={c.Progress:0.000}"
            : "";

    // Native type-0 surface commands first leave range A in normal z-order, then alpha-composite range B
    // into the target surface. SC0000 binds that target to handle+2, above both source handles, so drawing
    // range B here with progress produces old*(1-progress)+new*progress without disturbing ambient channels.
    private int DrawTransitionRange(IReadOnlyList<RenderObject> visible, SurfaceTransitionState transition)
    {
        int drawn = 0;
        long end = transition.RangeBStart + transition.RangeBCount;
        foreach (var source in visible)
        {
            if (source.Handle < transition.RangeBStart || source.Handle >= end || source.SurfaceTransition != null)
                continue;
            var affine = Transform2DMath.Build(source.Transform, source.Rotation).FromLocalOrigin(source.DstX, source.DstY);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            if (source.SurfaceResId == 0)
            {
                if (source.Blend == BlendKind.Opaque) continue;
                int w = source.W > 0 ? source.W : 800, h = source.H > 0 ? source.H : 600;
                FillAffineQuad(w, h, affine, source.Tint, opacity * source.TintStrength / 255f);
            }
            else
            {
                var texture = _host.ResolveResIdTexture(source.SurfaceResId);
                if (texture == null) continue;
                BlitLayer(texture.Value.Image, texture.Value.AssetId, source.ColorKey, source.Tint, source.TintStrength / 255f,
                          source.SrcX, source.SrcY, source.W, source.H, affine, opacity, source.MultiplyTint,
                          texture.Value.IsDynamic);
            }
            drawn++;
        }
        return drawn;
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

    // Blit one object's surface rect. Static source pixels are cached per (assetId, colorKey): on first use, texels
    // matching the surface colorkey are made transparent (native bakes the key at load — engine-re.md §Blend).
    // Mode 0 uses tintStrength to LERP texel RGB toward tint. Mode 1 sets multiplyTint and uses packed RGB as
    // multiplicative modulation while alpha is object opacity.
    private void BlitLayer(RgbaImage decoded, int assetId, long colorKey, long tint, float tintStrength, int srcX, int srcY, int w, int h,
                           Age.Engine.Model.Affine2D localToDest, float alpha = 1f, bool multiplyTint = false,
                           bool dynamic = false)
    {
        var cacheKey = (assetId, colorKey);
        int sourceWidth, sourceHeight;
        byte[] sourcePixels;
        if (dynamic)
        {
            // Decoder samples replace the pixels of one retained surface. Never enter them in the static cache.
            // Clone only when applying a key so the decoder-owned newest-frame buffer remains untouched.
            sourceWidth = decoded.Width;
            sourceHeight = decoded.Height;
            sourcePixels = decoded.Pixels;
            if (Age.Engine.Model.BlendMath.HasColorKey(colorKey))
            {
                sourcePixels = (byte[])sourcePixels.Clone();
                BakeColorKey(sourcePixels, colorKey);
            }
        }
        else
        {
            if (!_pixelCache.TryGetValue(cacheKey, out var cached))
            {
                byte[] pixels = decoded.Pixels;
                if (Age.Engine.Model.BlendMath.HasColorKey(colorKey))
                {
                    pixels = (byte[])pixels.Clone();
                    BakeColorKey(pixels, colorKey);
                }
                cached = new CachedPixels(decoded.Width, decoded.Height, pixels);
                _pixelCache[cacheKey] = cached;
            }
            sourceWidth = cached.Width;
            sourceHeight = cached.Height;
            sourcePixels = cached.Rgba;
        }

        int sw = w > 0 ? w : sourceWidth;
        int sh = h > 0 ? h : sourceHeight;
        sw = System.Math.Min(sw, sourceWidth - srcX);
        sh = System.Math.Min(sh, sourceHeight - srcY);
        if (sw <= 0 || sh <= 0) return;
        Age.Engine.Model.SoftwareAffineRasterizer.BlitRgba(
            _screenPixels, ScreenWidth, ScreenHeight, sourcePixels, sourceWidth, sourceHeight,
            srcX, srcY, sw, sh, localToDest, tint, tintStrength, alpha, multiplyTint);
    }

    private void FillAffineQuad(int w, int h, Age.Engine.Model.Affine2D localToDest, long tint, float alpha)
    {
        Age.Engine.Model.SoftwareAffineRasterizer.FillRgba(
            _screenPixels, ScreenWidth, ScreenHeight, w, h, localToDest, tint, alpha);
    }

    // Alpha-blend a solid tint (0xRRGGBB) rectangle over the screen — the surfaceless fade/flash fill.
    private void FillQuad(int dstX, int dstY, int w, int h, long tint, float alpha)
    {
        int ia = (int)(System.Math.Clamp(alpha, 0f, 1f) * 255);
        if (ia == 0) return;
        int tr = (int)((tint >> 16) & 0xff), tg = (int)((tint >> 8) & 0xff), tb = (int)(tint & 0xff);
        byte[] dst = _screenPixels;
        int dw = ScreenWidth, dh = ScreenHeight;
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
    }

    // Make colorkey-matching texels transparent (native colorkey is baked at surface load).
    private static void BakeColorKey(byte[] px, long colorKey)
    {
        for (int i = 0; i < px.Length; i += 4)
            if (Age.Engine.Model.BlendMath.ColorKeyMatches(px[i], px[i + 1], px[i + 2], colorKey))
                px[i + 3] = 0;
    }

    // Decode VFS-owned bytes in Godot. BGM loops; voice plays once, cutting off any prior line.
    public void PlayBgm(byte[] oggBytes, string assetName)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
        if (stream == null) { GD.Print($"OGG load failed {assetName}"); return; }
        stream.Loop = true;
        _bgm.VolumeDb = 0;
        _bgm.Stream = stream;
        _bgm.Play();
    }

    public int QueueVoicePlayback()
        => System.Threading.Interlocked.Increment(ref _voiceQueuedGeneration);

    public bool IsVoicePlaybackActive
        => System.Threading.Volatile.Read(ref _voiceCompletedGeneration)
           < System.Threading.Volatile.Read(ref _voiceQueuedGeneration);

    public void PlayVoice(byte[] oggBytes, string assetName, int generation)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
        if (stream == null)
        {
            GD.Print($"OGG load failed {assetName}");
            CompleteVoiceGeneration(generation);
            return;
        }
        stream.Loop = false;
        _voice.Stream = stream;
        System.Threading.Volatile.Write(ref _voiceStartedGeneration, generation);
        _voice.Play();
    }

    private void UpdateVoicePlaybackState()
    {
        int started = System.Threading.Volatile.Read(ref _voiceStartedGeneration);
        if (started > System.Threading.Volatile.Read(ref _voiceCompletedGeneration) && !_voice.Playing)
            CompleteVoiceGeneration(started);
    }

    private void CompleteVoiceGeneration(int generation)
    {
        int current;
        do
        {
            current = System.Threading.Volatile.Read(ref _voiceCompletedGeneration);
            if (current >= generation) return;
        }
        while (System.Threading.Interlocked.CompareExchange(
                   ref _voiceCompletedGeneration, generation, current) != current);
    }

    public void LoadSoundEffect(byte[] wavBytes, string assetName, int channel)
    {
        if ((uint)channel >= (uint)_sfx.Length) return;
        var stream = AudioStreamWav.LoadFromBuffer(wavBytes);
        if (stream == null) { GD.Print($"WAV load failed {assetName}"); return; }
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        _sfx[channel].Stop();
        _sfx[channel].VolumeDb = 0;
        _sfx[channel].Stream = stream;
    }

    public void StartSoundEffect(int channel)
    {
        if ((uint)channel < (uint)_sfx.Length && _sfx[channel].Stream != null)
            _sfx[channel].Play();
    }

    public void ReleaseSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfx.Length) return;
        _sfx[channel].Stop();
        _sfx[channel].Stream = null;
    }

    public void FadeBgm(int targetPercent, double realDurationSeconds)
    {
        float linear = System.Math.Clamp(targetPercent / 100.0f, 0.0f, 1.0f);
        float targetDb = linear <= 0 ? -80.0f : Mathf.LinearToDb(linear);
        if (realDurationSeconds <= 0) { _bgm.VolumeDb = targetDb; return; }
        CreateTween().TweenProperty(_bgm, "volume_db", targetDb, realDurationSeconds);
    }

    public void PlayMovie(byte[] mpegBytes, string assetName, long resourceId, int rawIndex)
    {
        if (_movies.Remove(resourceId, out var prior)) prior.Decoder.Dispose();
        try
        {
            var payload = new Age.Engine.Sys4.MoviePayload(assetName, mpegBytes);
            _movies[resourceId] = new MovieRuntime(assetName, rawIndex, new DirectShowMovieDecoder(payload));
            GD.Print($"movie started {assetName} ({mpegBytes.Length} bytes from VFS)");
        }
        catch (System.Exception e)
        {
            GD.Print($"movie decode failed {assetName}: {e.Message}");
            _host.NotifyMovieCompleted(resourceId); // release a pending 0x21c boundary on deterministic load failure
        }
    }

    private void UpdateMovieFrames()
    {
        if (_host == null) return;
        foreach (var (resourceId, movie) in _movies)
        {
            if (movie.Decoder.TryTakeFrame(out var frame))
            {
                _host.PublishMovieFrame(resourceId, movie.Name, movie.RawIndex, frame);
                if (_movieFrameSeen.Add(resourceId))
                    GD.Print($"movie first frame {movie.Name}: {frame.Width}x{frame.Height} RGBA8 at render frame {_timelineFrame}");
            }
            if (movie.Decoder.IsCompleted) _host.NotifyMovieCompleted(resourceId);
        }
    }

    public void StopMovie(long resourceId)
    {
        if (_movies.Remove(resourceId, out var movie))
        {
            movie.Decoder.Dispose();
            GD.Print($"movie stopped {movie.Name} at render frame {_timelineFrame}");
        }
        _movieFrameSeen.Remove(resourceId);
    }

    private sealed record MovieRuntime(string Name, int RawIndex, DirectShowMovieDecoder Decoder);

    public void AppendLine(string text) => _text.Text += text + "\n";
    public void PageBreak()
    {
        _pageCount++;
        _status.Text = "";
        if (_locatorHudVisible) _locatorHud.Text = _locator.CurrentDisplay;
    }
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

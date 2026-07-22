using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Age.Engine.Diagnostics;
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
    private ImageTexture? _ageCursorTexture;
    private TextureRect _waitIndicator = null!;
    private ImageTexture? _waitIndicatorSheet;
    private AtlasTexture? _waitIndicatorAtlas;
    private int _waitIndicatorAssetId = -1;
    // One managed composition target for the entire frame. Layer helpers mutate it in place; only the
    // completed frame crosses the Godot Image boundary, avoiding a full GetData/SetData round-trip per layer.
    private readonly byte[] _screenPixels = new byte[ScreenWidth * ScreenHeight * 4];
    private Label _text = null!;
    private Label _speaker = null!;
    private readonly System.Collections.Generic.List<Label> _surfaceTextLabels = new();
    private readonly System.Collections.Generic.Dictionary<int, Label> _historyTextLabels = new();
    private Font? _presentationRegularFont;
    private FontVariation? _presentationBoldFont;
    private Label _status = null!;
    private Label _locatorHud = null!;
    private AudioStreamPlayer _bgm = null!;                // looping background music
    private AudioStreamPlayer _voice = null!;              // interrupt-on-new voice
    private int _voiceQueuedGeneration;
    private int _voiceStartedGeneration;
    private int _voiceCompletedGeneration;
    private bool _voiceBgmDuckActive;
    private int _voiceBgmDuckGeneration;
    private float _voiceBgmDuckRestoreDb;
    private readonly AudioStreamPlayer[] _sfx = new AudioStreamPlayer[10]; // SC0000 channels 0..9
    private readonly int[] _sfxGenerations = new int[10];
    private VirtualMachine _vm = null!;
    private GodotAdvHost _host = null!;
    private Sys4ScriptProvider? _scripts;
    private DebugSceneLauncher? _debugSceneLauncher;
    private IReadOnlyList<DebugSceneEntry> _debugSceneEntries = System.Array.Empty<DebugSceneEntry>();
    private readonly Age.Engine.Hosting.FrameClock _clock = new();
    private readonly System.Collections.Generic.Dictionary<long, MovieRuntime> _movies = new();
    // 0x236 opens its decoder synchronously on the VM thread so 0x23f can query timing immediately.
    // Presentation ownership transfers here; _Process adopts staged decoders before sampling frames.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, MovieRuntime> _pendingMovies = new();
    private IMovieDecoderFactory _movieDecoderFactory = new FfmpegMovieDecoderFactory();
    private readonly System.Collections.Generic.HashSet<long> _movieFrameSeen = new();
    private readonly System.Collections.Generic.HashSet<long> _movieCompletionNotified = new();
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
        AddChild(_screenView);   // added first -> draws behind the text/status labels
        _screenView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

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
        AddChild(_text);
        _text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _speaker = new Label { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_speaker);
        _speaker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _surfaceTextLabels.Add(_speaker);
        _status = new Label();
        AddChild(_status);
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _status.OffsetLeft = 40; _status.OffsetTop = -60;
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
        bool boot = System.Array.IndexOf(userArgs, "--boot") >= 0; // diagnostic prefix for direct-scene runs
        bool nativeDebugMenu = System.Array.IndexOf(userArgs, "--native-debug-menu") >= 0;
        if (nativeDebugMenu)
            GD.Print("[debug] native exit requests disabled; post-0x1 bytecode may execute");
        string scene = "SYSTEM4";                       // natural persistent root; --scene keeps direct diagnostics
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
        _scripts = scripts;
        bool directSceneHarness = !_selftest
            && !scene.Equals("SYSTEM4", System.StringComparison.OrdinalIgnoreCase);
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
        _vm = new VirtualMachine(script, table, _host,
            new VmOptions(MaxSteps: 20_000_000, IgnoreExitRequests: nativeDebugMenu), provider, sink);
        if (scripts != null)
        {
            _debugSceneEntries = DebugSceneCatalog.Build(scripts.Catalog);
            _debugSceneLauncher = new DebugSceneLauncher();
            _debugSceneLauncher.LaunchRequested += LaunchDebugScene;
            AddChild(_debugSceneLauncher);
        }
        // SYSTEM4.BIN defines these nine shared ADV text layouts before dispatching any scene. The
        // single-scene harness starts after that prefix, so carry forward its exact script-owned state
        // alongside the inherited SO000/SO001 state below. Full Phase-B SYSTEM4 replay will replace this
        // bootstrap as one unit; HISTORY depends on the 650x150 dimensions of layouts 2..6 for clipping.
        if (directSceneHarness)
        {
            var systemScript = scripts!.RequireByName("SYSTEM4.BIN");
            AdvTextLayoutBootstrap.ApplyLeadingDefinitionsAndResets(systemScript, table, _vm.TextHistory);
            InputBindingBootstrap.Apply(systemScript, _vm.InputBindings);
        }
        // SYSTEM4 loads the shared SO001 chrome sheet into surface slot 17 before any scene runs.
        // Seed that inherited retained-surface state without replaying the entrypoint's unrelated UI flow.
        if (directSceneHarness && resources.ResolveName("SO001.AGF") is { } systemChrome)
        {
            _host.SetTexture(systemChrome.PackedId, 0x11);
            _vm.Gfx.SetSurface(0x11, systemChrome.PackedId, 0);
        }
        // SYSTEM4 also loads SO000 and configures op 0x73 before entering scene code. The Phase-A
        // single-scene harness does not replay those graphics side effects, so inject their exact state
        // alongside the existing SO001 bootstrap until Phase B runs the complete SYSTEM4 entrypoint.
        if (directSceneHarness && resources.ResolveName("SO000.AGF") is { } waitIndicator)
        {
            _host.SetTexture(waitIndicator.PackedId, 0x0c);
            _vm.Gfx.SetSurface(0x0c, waitIndicator.PackedId, 0xff00);
            _host.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
                1, 385, 140, 0x0c, 0, 0, 30, 27, 12, 48));
        }
        // --boot: run SYSTEM4's state prefix (INITCONFIG/INIT2/INIT) so the scene sees boot state — chiefly
        // INIT2's gfx handle array 0x62455.. (skips the UI scripts LOGO/OP/TITLE). State carries via globals.
        if (boot && directSceneHarness)
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
        if (directSceneHarness)
        {
            _vm.Globals[0x6c1] = 1;
        }
        // The native ADV scheduler supplies this Hide Window permission outside script-visible writes.
        // It is an engine service default, not a scene bootstrap, and remains present for either entry path.
        if (!_selftest) _vm.Globals[0x62425] = 1;
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
        AdoptPendingMovies();
        UpdateMovieFrames();
        if (!_selftest && _vm != null && _host != null && _host.ShouldRecomposite(_vm.Gfx))
            Recomposite();   // native publishes retained mutations only at present/service boundaries
        if (!_selftest && _host != null) UpdateAdvTextPresentation();
        if (!_selftest && _host != null) UpdateAdvWaitIndicatorPresentation();
        if (!_selftest && _host != null) UpdateHistoryTextPresentation();
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
            GD.Print($"[vm] ended: {_vm!.HaltReason ?? "unknown"} after {_vm.Steps} steps");
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
        if (e is InputEventKey debugKey && debugKey.Pressed && !debugKey.Echo && debugKey.Keycode == Key.F4)
        {
            ToggleDebugSceneLauncher();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventKey diagnosticKey && diagnosticKey.Keycode == Key.F6)
        {
            if (diagnosticKey.Pressed && !diagnosticKey.Echo) CaptureStallDiagnostic();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_debugSceneLauncher?.Visible == true)
        {
            if (e is InputEventKey escape && escape.Pressed && !escape.Echo && escape.Keycode == Key.Escape)
            {
                _debugSceneLauncher.Hide();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        if (e is InputEventMouseMotion motion)
        {
            var p = ToNativeScreen(motion.Position);
            _vm.UpdatePointer(p.X, p.Y);
            return;
        }
        if (e is InputEventMouseButton wheel
            && wheel.Pressed
            && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown))
        {
            // WM_MOUSEWHEEL supplies signed multiples of WHEEL_DELTA (120). AGE accumulates that
            // value until op 0x10d reads and clears it; HISTORY currently uses only its sign.
            int direction = wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
            int steps = System.Math.Max(1, (int)System.Math.Round(wheel.Factor));
            _vm.QueueMouseWheelDelta(direction * 120 * steps);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventMouseButton mb
            && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right))
        {
            var p = ToNativeScreen(mb.Position);
            bool advPageSuspended = _host.IsAdvPagePresentationSuspended;
            bool rawInputCallbackActive = _vm.IsRawInputCallbackActive;
            _vm.UpdatePointer(p.X, p.Y);
            int nativeButtonBit = mb.ButtonIndex == MouseButton.Left ? 0x1 : 0x2;
            int physicalButton = mb.ButtonIndex == MouseButton.Left ? 0 : 1;
            _vm.UpdateMouseButtonState(nativeButtonBit, mb.Pressed);
            int action = _vm.UpdatePhysicalMouseButtonState(physicalButton, mb.Pressed);
            if (mb.Pressed && _host.IsModalMovieWaiting)
            {
                _host.SignalInput();
                GetViewport().SetInputAsHandled();
                return;
            }
            // AGE exposes mouse buttons twice: op 0x108 reads the raw bitmask while op 0xff translates
            // the held physical button through the script-configured logical action map.
            if (mb.Pressed && action >= 0 && _vm.TryActivateInputActions(1 << action))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed && _vm.TryActivatePointer(p.X, p.Y))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            // Modal callback scripts return through their own bytecode. Signaling the enclosing ADV wait
            // here would also advance the restored dialogue page after HISTORY/HIDEWIN exits.
            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed
                && !advPageSuspended && !rawInputCallbackActive) _host.SignalInput();
            return;
        }
        if (e is InputEventKey gameplayKey && !gameplayKey.Echo
            && Win32VirtualKeyTranslator.TryTranslate(gameplayKey, out int virtualKey))
        {
            int action = _vm.UpdateKeyboardVirtualKeyState(virtualKey, gameplayKey.Pressed);
            if (gameplayKey.Pressed && action >= 0 && _vm.TryActivateInputActions(1 << action))
            {
                GetViewport().SetInputAsHandled();
            }
            else if (gameplayKey.Pressed && IsAdvanceAction(action))
            {
                if (_host.IsModalMovieWaiting)
                {
                    _host.SignalInput();
                    GetViewport().SetInputAsHandled();
                }
                else if (!_host.IsAdvPagePresentationSuspended && !_vm.IsRawInputCallbackActive)
                    _host.SignalInput();
            }
            return;
        }
        if (e is InputEventJoypadButton joyButton)
        {
            int actionMask = _vm.UpdateJoystickButtonState((int)joyButton.ButtonIndex, joyButton.Pressed);
            if (joyButton.Pressed && _vm.TryActivateInputActions(actionMask))
            {
                GetViewport().SetInputAsHandled();
            }
            else if (joyButton.Pressed && HasAdvanceAction(actionMask))
            {
                if (_host.IsModalMovieWaiting)
                {
                    _host.SignalInput();
                    GetViewport().SetInputAsHandled();
                }
                else if (!_host.IsAdvPagePresentationSuspended && !_vm.IsRawInputCallbackActive)
                    _host.SignalInput();
            }
            return;
        }
        if (e is InputEventJoypadMotion joyMotion && (int)joyMotion.Axis is 0 or 1)
            _vm.UpdateJoystickAxisState((int)joyMotion.Axis, joyMotion.AxisValue);
    }

    private static bool IsAdvanceAction(int action) => action is 4 or 5;
    private static bool HasAdvanceAction(int mask) => (mask & ((1 << 4) | (1 << 5))) != 0;

    private void CaptureStallDiagnostic()
    {
        try
        {
            long nowMs = _clock.NowMs;
            GodotTraceSnapshot trace = _trace.Snapshot();
            var activeMovies = _movies
                .OrderBy(pair => pair.Key)
                .Select(pair => new
                {
                    playback_id = pair.Key,
                    resource_id = pair.Value.ResourceId,
                    name = pair.Value.Name,
                    asset_id = pair.Value.AssetId,
                    stop_time_ms = pair.Value.Decoder.StopTimeMs,
                    decoder_completed = pair.Value.Decoder.IsCompleted,
                    decoder_failure = pair.Value.Decoder.Failure,
                    frame_seen = _movieFrameSeen.Contains(pair.Key),
                    completion_notified = _movieCompletionNotified.Contains(pair.Key),
                    watchdog_ms = pair.Value.WatchdogMs,
                    elapsed_ms = (long)Stopwatch.GetElapsedTime(pair.Value.StartedAtTimestamp).TotalMilliseconds,
                })
                .ToArray();
            var pendingMovies = _pendingMovies
                .OrderBy(pair => pair.Key)
                .Select(pair => new
                {
                    playback_id = pair.Key,
                    resource_id = pair.Value.ResourceId,
                    name = pair.Value.Name,
                    asset_id = pair.Value.AssetId,
                    stop_time_ms = pair.Value.Decoder.StopTimeMs,
                    decoder_completed = pair.Value.Decoder.IsCompleted,
                    decoder_failure = pair.Value.Decoder.Failure,
                })
                .ToArray();
            var snapshot = new
            {
                format_version = 1,
                captured_utc = System.DateTimeOffset.UtcNow.ToString("O"),
                render_frame = _timelineFrame,
                clock_ms = nowMs,
                vm = new
                {
                    steps = _vm.Steps,
                    halt_reason = _vm.HaltReason,
                    done = _done,
                    trace,
                },
                host = _host.CaptureDiagnosticSnapshot(),
                gfx = _vm.Gfx.CaptureDiagnosticSnapshot(nowMs),
                active_movies = activeMovies,
                pending_movies = pendingMovies,
            };

            string directory = ProjectSettings.GlobalizePath("user://diagnostics");
            System.IO.Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(directory,
                $"stall-{System.DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(snapshot, jsonOptions));
            string coordinate = trace.CurrentOffset >= 0
                ? $"{System.IO.Path.GetFileNameWithoutExtension(trace.CurrentScript).ToUpperInvariant()}@0x{trace.CurrentOffset:x}"
                : trace.CurrentScript;
            string clipboard = $"{coordinate} · stall snapshot {path}";
            DisplayServer.ClipboardSet(clipboard);
            _status.Text = $"Diagnostic saved: {coordinate} (path copied)";
            GD.Print($"[diagnostic] stall snapshot {coordinate} -> {path}");
        }
        catch (System.Exception exception)
        {
            _status.Text = "Diagnostic capture failed; see Godot log.";
            GD.Print($"[diagnostic] stall snapshot failed: {exception}");
        }
    }

    private void ToggleDebugSceneLauncher()
    {
        if (_debugSceneLauncher == null) return;
        if (_debugSceneLauncher.Visible)
        {
            _debugSceneLauncher.Hide();
            return;
        }
        if (!TryGetTitleDebugFrame(out var frame, out string reason))
        {
            _status.Text = reason;
            GD.Print($"[debug-launcher] unavailable: {reason}");
            return;
        }
        _debugSceneLauncher.Open(_debugSceneEntries, string.Join(" > ", frame.CallStack));
    }

    private void LaunchDebugScene(DebugSceneEntry entry)
    {
        if (_debugSceneLauncher == null || _scripts == null) return;
        if (!TryGetTitleDebugFrame(out var frame, out string reason))
        {
            _debugSceneLauncher.SetStatus(reason);
            return;
        }
        if (!entry.Launchable || _scripts.GetById(entry.PackedId) == null)
        {
            _debugSceneLauncher.SetStatus("The selected packed script could not be parsed; no state was changed.");
            return;
        }

        var coordinatorWrites = new Dictionary<int, long>
        {
            [0] = 1,
            [0xaba5c] = -1,
            [0x62ccf] = 0,
            [0x699] = entry.PackedId,
        };
        if (!_vm.TryRequestDebugFrameReturn(frame.FrameId, coordinatorWrites))
        {
            _debugSceneLauncher.SetStatus("TITLE changed frames before launch; reopen the launcher and try again.");
            return;
        }

        _timeline?.Event("debug-scene-launch-request", new()
        {
            ["script"] = entry.Name,
            ["packed_id"] = entry.PackedId,
        });
        GD.Print($"[debug-launcher] SYSTEM4 dispatch requested: {entry.Name} (0x{entry.PackedId:x8})");
        _debugSceneLauncher.Hide();
        // ADV waits need an explicit wake; TITLE's actual menu is a 1 ms sleep/poll loop and will consume
        // the request at its next opcode boundary without leaving a stale input signal for the child scene.
        if (_host.IsWaiting) _host.SignalInput();
    }

    private bool TryGetTitleDebugFrame(out DebugFrameSnapshot frame, out string reason)
    {
        frame = _vm.DebugFrame!;
        if (_done || frame == null)
        {
            reason = "Available only while TITLE is the active SYSTEM4 child.";
            return false;
        }
        if (frame.CallStack.Count != 2
            || !frame.CallStack[0].Equals("SYSTEM4.BIN", System.StringComparison.OrdinalIgnoreCase)
            || !frame.CallStack[1].Equals("TITLE.BIN", System.StringComparison.OrdinalIgnoreCase)
            || !frame.CurrentScript.Equals("TITLE.BIN", System.StringComparison.OrdinalIgnoreCase))
        {
            reason = "Refused: the active stack is not SYSTEM4 > TITLE.";
            return false;
        }
        reason = "";
        return true;
    }

    private (int X, int Y) ToNativeScreen(Vector2 position)
    {
        Vector2 size = GetViewportRect().Size;
        if (size.X <= 0 || size.Y <= 0) return (0, 0);
        return ((int)System.Math.Floor(position.X * ScreenWidth / size.X),
                (int)System.Math.Floor(position.Y * ScreenHeight / size.Y));
    }

    public void SetAgeCursor(byte[] rgba, int width, int height, int hotspotX, int hotspotY)
    {
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        _ageCursorTexture = ImageTexture.CreateFromImage(image);
        Input.SetCustomMouseCursor(_ageCursorTexture, Input.CursorShape.Arrow,
                                   new Vector2(hotspotX, hotspotY));
    }

    public void ClearAgeCursor()
    {
        Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
        _ageCursorTexture = null;
    }

    public override void _ExitTree()
    {
        DumpHistogram(); _host?.Stop(); _timeline?.Dispose(); _locator?.Dispose();
        foreach (var movie in _pendingMovies.Values) movie.Decoder.Dispose();
        _pendingMovies.Clear();
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
        foreach (var label in _surfaceTextLabels) label.Visible = false;
        int surfaceTextLabelIndex = 0;
        System.Collections.Generic.Dictionary<long, string>? decisions = _gfxLogPath != null || _timeline != null ? new() : null;
        if (_host.TrySnapshotScreenTransition(out var transition))
        {
            // Native mode 4 keeps the captured source opaque and alpha-composites the complete target
            // surface over it. Each offscreen target has an opaque-black clear beneath its objects.
            CompositeVisibleObjects(transition.Source, 1f, ref surfaceTextLabelIndex, decisions, false);
            FillQuad(0, 0, ScreenWidth, ScreenHeight, 0, (float)transition.Progress);
            CompositeVisibleObjects(transition.Target, (float)transition.Progress,
                                    ref surfaceTextLabelIndex, decisions, false);
        }
        else
        {
            var visible = _vm.Gfx.SnapshotVisibleObjects(_clock.NowMs); // synchronized objects + ranges
            CompositeVisibleObjects(visible, 1f, ref surfaceTextLabelIndex, decisions, true);
        }
        _screen.SetData(ScreenWidth, ScreenHeight, false, Image.Format.Rgba8, _screenPixels);
        _screenTex.Update(_screen);
        if (decisions != null) LogGfxDecisionChanges(decisions);
    }

    private void CompositeVisibleObjects(IReadOnlyList<RenderObject> visible, float globalOpacity,
                                         ref int surfaceTextLabelIndex,
                                         System.Collections.Generic.Dictionary<long, string>? decisions,
                                         bool includeSurfaceText)
    {
        int z = 0;
        foreach (var v in visible)   // interpolate at the retained-presentation clock
        {
            var t = v.Transform;
            var affine = Age.Engine.Model.Transform2DMath.Build(t, v.Rotation);
            var localToDest = affine.FromLocalOrigin(v.DstX, v.DstY);
            if (v.RangeTransform is { } rangeTransform)
                localToDest = localToDest.Then(rangeTransform);
            var projected = localToDest.Apply(0, 0);
            int dstX = (int)System.Math.Round(projected.X);
            int dstY = (int)System.Math.Round(projected.Y);
            float opacity = v.Alpha / 255f * globalOpacity;  // transform Z is never opacity
            float strength = v.TintStrength / 255f;          // tint-blend / fill strength
            var rawObject = _vm.Gfx.TryGet(v.Handle);
            var surfaceTexture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, v.SurfaceResId)
                : null;
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);
            string outcome;
            if (v.SurfaceTransition is { } transition)
            {
                int layers = DrawTransitionRange(visible, transition);
                outcome = $"TRANSITION slot={transition.TargetSlot} key=0x{transition.CommandKey:x} " +
                          $"progress={transition.Progress:0.000} forced={transition.Forced} layers={layers}";
            }
            else if (v.SurfaceResId == 0 && surfaceTexture == null)
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
                var texture = surfaceTexture
                    ?? (movieSurfaceBound ? null : _host.ResolveResIdTexture(v.SurfaceResId));
                if (texture == null) outcome = $"SKIP(resId=0x{v.SurfaceResId:x} UNRESOLVED)";
                else
                {
                    BlitLayer(texture.Value.Image, texture.Value.AssetId, v.ColorKey, v.Tint, strength, v.SrcX, v.SrcY, v.W, v.H,
                              localToDest, opacity, v.MultiplyTint, texture.Value.IsDynamic, v.Blend);
                    outcome = $"slot={rawObject?.SourceSlot} DRAWN resId=0x{v.SurfaceResId:x} {texture.Value.Name} " +
                              $"src=({v.SrcX},{v.SrcY} {v.W}x{v.H}) base=({v.DstX},{v.DstY}) " +
                              $"anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) dst=({dstX},{dstY}) " +
                              $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) " +
                              $"rot=({t.RotationAngleDegrees:0.0}+{v.Rotation.AngleDegrees:0.0}) " +
                              $"mode={rawObject?.StaticColorMode} op={opacity:0.00} tintStr={strength:0.00}" +
                              ColorTimeline(v.ColorTransition);
                }
            }
            if (decisions != null) decisions[v.Handle] = $"z{z} {outcome}";
            if (includeSurfaceText && rawObject != null)
            {
                foreach (var surfaceText in _host.SnapshotSurfaceText(rawObject.SourceSlot))
                {
                    if (surfaceText.X < v.SrcX || surfaceText.X >= v.SrcX + v.W ||
                        surfaceText.Y < v.SrcY || surfaceText.Y >= v.SrcY + v.H) continue;
                    var textPos = localToDest.Apply(surfaceText.X - v.SrcX, surfaceText.Y - v.SrcY);
                    var label = GetSurfaceTextLabel(surfaceTextLabelIndex++);
                    label.Position = new Vector2((float)textPos.X, (float)textPos.Y);
                    label.Size = new Vector2(System.Math.Max(1, v.W - (surfaceText.X - v.SrcX)),
                                             System.Math.Max(1, v.H - (surfaceText.Y - v.SrcY)));
                    label.Text = surfaceText.Text;
                    ApplyAdvTextStyle(label, surfaceText.Style);
                    label.Visible = true;
                }
            }
            z++;
        }
    }

    private void UpdateAdvTextPresentation()
    {
        // Modal callback scripts composite their own full-screen UI while the enclosing ADV wait remains
        // parked. The ordinary dialogue Label is a Godot overlay rather than part of the retained surface,
        // so hide it while that nested input owner is active or it leaks above HISTORY's background.
        _text.Visible = !_host.IsAdvPagePresentationSuspended && !_vm.IsRawInputCallbackActive;
        if (!_text.Visible) return;
        var t = _host.SnapshotAdvText();
        var layout = _vm.TextHistory.GetLayoutSnapshot(_host.AdvPageLayoutSlot);
        _text.Position = new Vector2(layout.OriginX + layout.CursorX, layout.OriginY + layout.CursorY);
        _text.Size = new Vector2(System.Math.Max(1, layout.Right - layout.CursorX),
                                 System.Math.Max(1, layout.Bottom - layout.CursorY));
        int count = System.Math.Clamp(t.VisibleGlyphs, 0, t.Text.Length);
        _text.Text = count == 0 ? "" : t.Text[..count];
    }

    private void UpdateHistoryTextPresentation()
    {
        foreach (var label in _historyTextLabels.Values) label.Visible = false;
        foreach (var batch in _host.SnapshotRenderedTextHistory())
        {
            if (batch.Text.Length == 0) continue;
            if (!_historyTextLabels.TryGetValue(batch.LayoutSlot, out var label))
            {
                label = CreateAdvPresentationLabel();
                _historyTextLabels.Add(batch.LayoutSlot, label);
            }
            var layout = batch.Layout;
            label.Position = new Vector2(layout.OriginX + layout.CursorX, layout.OriginY + layout.CursorY);
            label.Size = new Vector2(System.Math.Max(1, layout.Right - layout.CursorX),
                                     System.Math.Max(1, layout.Bottom - layout.CursorY));
            label.Text = batch.Text;
            ApplyAdvTextStyle(label, batch.Style);
            label.Visible = true;
        }
    }

    private Label GetSurfaceTextLabel(int index)
    {
        while (_surfaceTextLabels.Count <= index) _surfaceTextLabels.Add(CreateAdvPresentationLabel());
        return _surfaceTextLabels[index];
    }

    private Label CreateAdvPresentationLabel()
    {
        var label = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = true,
        };
        label.AddThemeFontOverride("font", _text.GetThemeFont("font"));
        AddChild(label);
        return label;
    }

    private void ApplyAdvTextStyle(Label label, AdvTextStyle style)
    {
        int fontSize = style.PrimaryFontSize > 0 ? style.PrimaryFontSize : 24;
        _presentationRegularFont ??= _text.GetThemeFont("font");
        if (style.Bold)
        {
            _presentationBoldFont ??= new FontVariation
            {
                BaseFont = _presentationRegularFont,
                VariationEmbolden = 1.2f,
            };
            label.AddThemeFontOverride("font", _presentationBoldFont);
        }
        else label.AddThemeFontOverride("font", _presentationRegularFont);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", RgbColor(style.TextColor, Colors.White));
        label.AddThemeColorOverride("font_outline_color", RgbColor(style.EffectColor, new Color(0.38f, 0.38f, 0.38f)));
        label.AddThemeConstantOverride("line_spacing", style.LineSpacing);
        int outline = style.RenderMode == 0 ? 0 : System.Math.Max(1,
            System.Math.Max(System.Math.Abs(style.EffectOffsetX), System.Math.Abs(style.EffectOffsetY)));
        label.AddThemeConstantOverride("outline_size", outline);
    }

    private static Color RgbColor(long rgb, Color fallback)
    {
        if ((rgb & 0x00ff_ffff) == 0) return fallback;
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1);
    }

    private void UpdateAdvWaitIndicatorPresentation()
    {
        // As with the ordinary dialogue Label, the enclosing page's separately animated marker must sit
        // out while HISTORY/HIDEWIN owns raw input; native retained composition naturally covers it.
        if (_vm.IsRawInputCallbackActive)
        {
            _waitIndicator.Visible = false;
            return;
        }
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
            if (source.RangeTransform is { } rangeTransform)
                affine = affine.Then(rangeTransform);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            var rawObject = _vm.Gfx.TryGet(source.Handle);
            var texture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, source.SurfaceResId)
                : null;
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);
            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque) continue;
                int w = source.W > 0 ? source.W : 800, h = source.H > 0 ? source.H : 600;
                FillAffineQuad(w, h, affine, source.Tint, opacity * source.TintStrength / 255f);
            }
            else
            {
                if (!movieSurfaceBound) texture ??= _host.ResolveResIdTexture(source.SurfaceResId);
                if (texture == null) continue;
                BlitLayer(texture.Value.Image, texture.Value.AssetId, source.ColorKey, source.Tint, source.TintStrength / 255f,
                          source.SrcX, source.SrcY, source.W, source.H, affine, opacity, source.MultiplyTint,
                          texture.Value.IsDynamic, source.Blend);
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
    // Mode 0 uses tintStrength to LERP texel RGB toward tint. Mode 1 uses packed RGB modulation and
    // SRCALPHA/ONE additive composition; black source pixels therefore contribute nothing.
    private void BlitLayer(RgbaImage decoded, int assetId, long colorKey, long tint, float tintStrength, int srcX, int srcY, int w, int h,
                           Age.Engine.Model.Affine2D localToDest, float alpha = 1f, bool multiplyTint = false,
                           bool dynamic = false, BlendKind blend = BlendKind.Alpha)
    {
        // Native gfx_object_blit_d3d9 clips the explicit source rectangle and returns without drawing when
        // right<=left or bottom<=top. FIELD deliberately creates zero-area prototype objects from SO005;
        // expanding those dimensions to the full texture leaks the entire spritesheet onto the map.
        if (w <= 0 || h <= 0) return;
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

        int sw = w;
        int sh = h;
        sw = System.Math.Min(sw, sourceWidth - srcX);
        sh = System.Math.Min(sh, sourceHeight - srcY);
        if (sw <= 0 || sh <= 0) return;
        Age.Engine.Model.SoftwareAffineRasterizer.BlitRgba(
            _screenPixels, ScreenWidth, ScreenHeight, sourcePixels, sourceWidth, sourceHeight,
            srcX, srcY, sw, sh, localToDest, tint, tintStrength, alpha, multiplyTint, blend);
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

    public void PlayVoice(byte[] oggBytes, string assetName, int generation, bool duckBgm, int duckTargetPercent)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
        if (stream == null)
        {
            GD.Print($"OGG load failed {assetName}");
            CompleteVoiceGeneration(generation);
            return;
        }
        if (duckBgm)
            BeginVoiceBgmDuck(generation, duckTargetPercent);
        else
            RestoreVoiceBgmDuck();
        stream.Loop = false;
        _voice.Stream = stream;
        System.Threading.Volatile.Write(ref _voiceStartedGeneration, generation);
        _voice.Play();
    }

    public void StopVoiceForMessageSkip()
    {
        _voice.Stop();
        CompleteVoiceGeneration(System.Threading.Volatile.Read(ref _voiceQueuedGeneration));
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
        if (_voiceBgmDuckActive && generation >= _voiceBgmDuckGeneration)
            RestoreVoiceBgmDuck();
    }

    private void BeginVoiceBgmDuck(int generation, int targetPercent)
    {
        if (!_voiceBgmDuckActive)
            _voiceBgmDuckRestoreDb = _bgm.VolumeDb;
        _voiceBgmDuckActive = true;
        _voiceBgmDuckGeneration = generation;
        float linear = System.Math.Clamp(targetPercent / 100.0f, 0.0f, 1.0f);
        _bgm.VolumeDb = linear <= 0 ? -80.0f : Mathf.LinearToDb(linear);
    }

    private void RestoreVoiceBgmDuck()
    {
        if (!_voiceBgmDuckActive) return;
        _bgm.VolumeDb = _voiceBgmDuckRestoreDb;
        _voiceBgmDuckActive = false;
        _voiceBgmDuckGeneration = 0;
    }

    public void LoadSoundEffect(byte[] wavBytes, string assetName, int channel)
    {
        if ((uint)channel >= (uint)_sfx.Length) return;
        _sfxGenerations[channel]++;
        _sfx[channel].Stop();
        _sfx[channel].Stream = null;
        // This is intentionally a Godot-only compatibility boundary. The VFS and engine retain the
        // original WAV bytes; only Godot's UTF-8-assuming INFO parser sees the sanitized copy.
        byte[] godotWav = RiffWaveSanitizer.RemoveInfoMetadata(wavBytes);
        var stream = AudioStreamWav.LoadFromBuffer(godotWav);
        if (stream == null) { GD.Print($"WAV load failed {assetName}"); return; }
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        _sfx[channel].VolumeDb = 0;
        _sfx[channel].Stream = stream;
    }

    public void StartSoundEffect(int channel)
    {
        if ((uint)channel < (uint)_sfx.Length && _sfx[channel].Stream != null)
            _sfx[channel].Play();
    }

    public void ScheduleSoundEffectStart(int channel, int startMode, double realDelaySeconds)
    {
        if ((uint)channel >= (uint)_sfx.Length || _sfx[channel].Stream == null) return;
        int generation = _sfxGenerations[channel];
        void StartIfCurrent()
        {
            if (_sfxGenerations[channel] != generation || _sfx[channel].Stream == null) return;
            if (_sfx[channel].Stream is AudioStreamWav wav)
                wav.LoopMode = startMode == 0
                    ? AudioStreamWav.LoopModeEnum.Disabled
                    : AudioStreamWav.LoopModeEnum.Forward;
            _sfx[channel].Play();
        }
        if (realDelaySeconds <= 0)
        {
            StartIfCurrent();
            return;
        }
        GetTree().CreateTimer(realDelaySeconds).Timeout += StartIfCurrent;
    }

    public void CancelScheduledSoundEffectStarts()
    {
        // Native scene_context_init_reset calls sfx_clear_scheduled_starts. Generation invalidation
        // cancels the timer callbacks without stopping active sounds or unloading their channel streams.
        for (int channel = 0; channel < _sfxGenerations.Length; channel++)
            _sfxGenerations[channel]++;
    }

    public void ReleaseSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfx.Length) return;
        _sfxGenerations[channel]++;
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

    public bool TryPlayMovie(byte[] mpegBytes, string assetName, long playbackId,
                             long resourceId, int assetId,
                             out long? stopTimeMs)
    {
        stopTimeMs = null;
        try
        {
            var payload = new Age.Engine.Sys4.MoviePayload(assetName, mpegBytes);
            var runtime = MovieRuntime.Open(assetName, assetId, resourceId, payload, _movieDecoderFactory);
            stopTimeMs = runtime.Decoder.StopTimeMs;
            while (!_pendingMovies.TryAdd(playbackId, runtime))
                if (_pendingMovies.TryRemove(playbackId, out var prior)) prior.Decoder.Dispose();
            return true;
        }
        catch (System.Exception e)
        {
            GD.Print($"movie decode failed {assetName}: {e.Message}");
            return false;
        }
    }

    private void AdoptPendingMovies()
    {
        foreach (var (playbackId, _) in _pendingMovies)
        {
            if (!_pendingMovies.TryRemove(playbackId, out var movie)) continue;
            if (_movies.Remove(playbackId, out var prior)) prior.Decoder.Dispose();
            _movies[playbackId] = movie;
            _movieCompletionNotified.Remove(playbackId);
            GD.Print($"movie started {movie.Name} playback={playbackId} " +
                     $"({movie.Decoder.StopTimeMs?.ToString() ?? "unknown"} ms from VFS)");
        }
    }

    private void UpdateMovieFrames()
    {
        if (_host == null) return;
        foreach (var (playbackId, movie) in _movies)
        {
            if (movie.Decoder.TryTakeFrame(out var frame))
            {
                _host.PublishMovieFrame(playbackId, movie.Name, movie.AssetId, frame);
                if (_movieFrameSeen.Add(playbackId))
                    GD.Print($"movie first frame {movie.Name} playback={playbackId}: " +
                             $"{frame.Width}x{frame.Height} RGBA8 at render frame {_timelineFrame}");
            }
            bool watchdogExpired = Stopwatch.GetElapsedTime(movie.StartedAtTimestamp).TotalMilliseconds
                                   >= movie.WatchdogMs;
            if ((movie.Decoder.IsCompleted || watchdogExpired) && _movieCompletionNotified.Add(playbackId))
            {
                if (movie.Decoder.Failure is { } failure)
                    GD.Print($"movie decode failed {movie.Name}: {failure}");
                if (watchdogExpired && !movie.Decoder.IsCompleted)
                    GD.Print($"movie completion watchdog {movie.Name}: forcing completion after {movie.WatchdogMs} ms");
                _host.NotifyMovieCompleted(playbackId);
            }
        }
    }

    public void StopMovie(long playbackId)
    {
        if (_pendingMovies.TryRemove(playbackId, out var pending)) pending.Decoder.Dispose();
        if (_movies.Remove(playbackId, out var movie))
        {
            movie.Decoder.Dispose();
            GD.Print($"movie stopped {movie.Name} playback={playbackId} at render frame {_timelineFrame}");
        }
        _movieFrameSeen.Remove(playbackId);
        _movieCompletionNotified.Remove(playbackId);
    }

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
        var debugEntries = DebugSceneCatalog.Build(Sys4AssetCatalog.Load(Paths.Sys4Ini));
        bool launcherOk = debugEntries.Any(entry => entry.Name == "DEBUG.BIN" && entry.Launchable)
                          && debugEntries.Select(entry => entry.PackedId).Distinct().Count() == debugEntries.Count;
        var launcherSmoke = new DebugSceneLauncher();
        AddChild(launcherSmoke);
        launcherSmoke.Open(debugEntries, "SYSTEM4.BIN > TITLE.BIN");
        launcherSmoke.Hide();
        launcherSmoke.QueueFree();
        bool sleepMinimumOk = GodotAdvHost.NormalizeSleepMilliseconds(0, 1.0) == 1
                              && GodotAdvHost.NormalizeSleepMilliseconds(100, 1.0) == 100;
        bool inputTranslationOk = Win32VirtualKeyTranslator.TryTranslate(
                                      new InputEventKey { PhysicalKeycode = Key.Z }, out int zVk) && zVk == 0x5a
                                  && Win32VirtualKeyTranslator.TryTranslate(
                                      new InputEventKey { PhysicalKeycode = Key.Up }, out int upVk) && upVk == 0x26
                                  && Win32VirtualKeyTranslator.TryTranslate(
                                      new InputEventKey { PhysicalKeycode = Key.Ctrl }, out int ctrlVk) && ctrlVk == 0x11;
        var selftestResources = ResourceMap.Load();
        AudioPayload glowSfx = selftestResources.ReadAudio(selftestResources.ResolveSoundEffect(0x28)!);
        byte[] glowGodotWav = RiffWaveSanitizer.RemoveInfoMetadata(glowSfx.Bytes);
        bool cp932WavMetadataOk = glowGodotWav.Length == 688_336
                                 && AudioStreamWav.LoadFromBuffer(glowGodotWav) != null;
        ok &= launcherOk && sleepMinimumOk && inputTranslationOk && cp932WavMetadataOk;
        if (ok) GD.Print($"SELFTEST OK: threaded host matches headless ({actual.Count} lines, full handling); " +
                         $"debug launcher catalog/UI smoke ({debugEntries.Count} packed scripts); " +
                         $"sleep-min=1ms; native-key-translation=ok; cp932-wav-info=ok");
        else GD.Print($"SELFTEST FAIL: threaded={actual.Count} vs headless={expected.Count}; " +
                      $"debug-launcher={launcherOk}; sleep-min={sleepMinimumOk}; " +
                      $"native-key-translation={inputTranslationOk}; cp932-wav-info={cp932WavMetadataOk}");
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

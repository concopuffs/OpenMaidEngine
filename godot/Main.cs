using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Text;
#if AGE_WINDOWS_GDI
using Age.Engine.Text.Windows;
#endif
using Age.Engine.Vm;
using Script = Age.Engine.Model.Script;   // disambiguate from Godot.Script

public partial class Main : Godot.Control
{
    private int _screenWidth = Sys4LogicalCanvas.DefaultWidth;
    private int _screenHeight = Sys4LogicalCanvas.DefaultHeight;
    private WindowLaunchOptions _windowOptions;
    private Sys4AssetCatalog _catalog = null!;
    private IAssetStore _assetStore = null!;
    private TextureRect _screenView = null!;              // shows the composited screen backbuffer
    private Image _screen = null!;                        // SYS4INI-sized immediate-mode canvas
    private ImageTexture _screenTex = null!;
    private GpuRetainedRenderer _gpuRenderer = null!;
    private bool _useGpuBackend = true;
    private ImageTexture? _ageCursorTexture;
    // One managed composition target for the entire frame. Layer helpers mutate it in place; only the
    // completed frame crosses the Godot Image boundary, avoiding a full GetData/SetData round-trip per layer.
    private byte[] _screenPixels = [];
    private Label _status = null!;
    private Label _locatorHud = null!;
    private AudioStreamPlayer _bgm = null!;                // looping background music
    private Tween? _bgmFadeTween;
    private AudioStreamPlayer _voice = null!;              // interrupt-on-new voice
    private int _voiceQueuedGeneration;
    private int _voiceStartedGeneration;
    private int _voiceCompletedGeneration;
    private bool _voiceBgmDuckActive;
    private int _voiceBgmDuckGeneration;
    private float _voiceBgmDuckRestoreDb;
    private readonly AudioStreamPlayer[] _sfx = new AudioStreamPlayer[10]; // SC0000 channels 0..9
    private readonly int[] _sfxGenerations = new int[10];
    private AudioMixerSettings _audioMixerSettings = null!;
    private Sys4RegIniStore? _sys4RegIniStore;
    private VirtualMachine _vm = null!;
    private GodotAdvHost _host = null!;
    private IDisposable? _glyphRasterizerOwner;
    private FullwidthTextEditorDialog? _fullwidthTextEditor;
    private Sys4ScriptProvider? _scripts;
    private DebugSceneLauncher? _debugSceneLauncher;
    private IReadOnlyList<DebugSceneEntry> _debugSceneEntries = System.Array.Empty<DebugSceneEntry>();
    private readonly Age.Engine.Hosting.FrameClock _clock = new();
    private readonly System.Collections.Generic.Dictionary<long, MovieRuntime> _movies = new();
    private readonly System.Collections.Generic.Dictionary<long, MovieAudioOutput> _movieAudio = new();
    // 0x236 opens its decoder synchronously on the VM thread so 0x23f can query timing immediately.
    // Presentation ownership transfers here; _Process adopts staged decoders before sampling frames.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, MovieRuntime> _pendingMovies = new();
    private IMovieDecoderFactory _movieDecoderFactory = new FfmpegMovieDecoderFactory();
    private double _audioOutputLatencySeconds;
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
    private Task? _vmTask;
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
    private string? _perfLogPath;                // --perf-log <csv>: low-overhead frame/compositor timings + work
    private PerformanceFrameLog? _perf;

    public override void _Ready()
    {
        var userArgs = OS.GetCmdlineUserArgs();
        GameRootSelection gameRoot;
        Sys4AssetCatalog catalog;
        try
        {
            string workingDirectory = System.IO.Directory.GetCurrentDirectory();
            if (!System.OperatingSystem.IsWindows())
            {
                // Godot can chdir to the project before managed startup. Unix shells preserve the launch
                // directory in PWD, which keeps terminal-launched exports faithful to their caller.
                string? inheritedWorkingDirectory =
                    System.Environment.GetEnvironmentVariable("PWD");
                if (!string.IsNullOrWhiteSpace(inheritedWorkingDirectory)
                    && System.IO.Directory.Exists(inheritedWorkingDirectory))
                    workingDirectory = inheritedWorkingDirectory;
            }
            gameRoot = GameRootSelection.Resolve(
                userArgs, OS.GetExecutablePath(), workingDirectory);
            catalog = Sys4AssetCatalog.Load(gameRoot.Sys4IniPath);
        }
        catch (System.Exception error) when (
            error is System.ArgumentException or System.IO.IOException or System.UnauthorizedAccessException)
        {
            GD.PushError($"[startup] {error.Message}");
            GetTree().Quit(2);
            return;
        }
        _catalog = catalog;
        _assetStore = new Sys4AssetStore(catalog, gameRoot.Root, gameRoot.Root);
        GD.Print($"[profile] game root={gameRoot.Root} source={gameRoot.SourceName}");

        // Resolve the selected game's logical canvas before any presentation allocation. The same catalog
        // instance is reused for scripts and assets later in startup.
        var logicalCanvas = catalog.LogicalCanvas;
        try
        {
            _windowOptions = WindowLaunchOptions.Resolve(userArgs, logicalCanvas);
        }
        catch (System.ArgumentException error)
        {
            GD.PushError($"[startup] {error.Message}");
            GetTree().Quit(2);
            return;
        }
        _screenWidth = logicalCanvas.Width;
        _screenHeight = logicalCanvas.Height;
        _screenPixels = new byte[logicalCanvas.RgbaByteCount];
        Window rootWindow = GetTree().Root;
        rootWindow.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        rootWindow.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        rootWindow.ContentScaleSize = new Vector2I(_screenWidth, _screenHeight);
        if (rootWindow.Mode == Window.ModeEnum.Windowed)
            rootWindow.Size = new Vector2I(_windowOptions.Width, _windowOptions.Height);
        GD.Print($"[profile] SYS4INI logical canvas={_screenWidth}x{_screenHeight} " +
                 $"requested window={_windowOptions.Width}x{_windowOptions.Height} " +
                 $"source={(_windowOptions.IsOverridden ? "boot-arguments" : "logical-canvas")} " +
                 $"actual={rootWindow.Size.X}x{rootWindow.Size.Y} mode={rootWindow.Mode}");

        // One logical canvas that draw-texture blits into, shown behind the dialogue.
        _screen = Image.CreateEmpty(_screenWidth, _screenHeight, false, Image.Format.Rgba8);
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
        _gpuRenderer = new GpuRetainedRenderer(this);

        _status = new Label();
        AddChild(_status);
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _status.OffsetLeft = 40; _status.OffsetTop = -60;
        _locatorHud = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _locatorHud.Position = new Vector2(8, 8);
        AddChild(_locatorHud);

        // Best-effort CJK font for diagnostic/status UI. Gameplay text is always surface pixels.
        foreach (var fp in new[] { "C:/Windows/Fonts/msgothic.ttc", "C:/Windows/Fonts/YuGothM.ttc",
                                   "C:/Windows/Fonts/YuGothR.ttc", "C:/Windows/Fonts/meiryo.ttc" })
        {
            if (!System.IO.File.Exists(fp)) continue;
            try
            {
                var ff = new FontFile { Data = System.IO.File.ReadAllBytes(fp) };
                _status.AddThemeFontOverride("font", ff);
                _locatorHud.AddThemeFontOverride("font", ff);
                break;
            }
            catch { /* fall back to the default font */ }
        }

        _bgm = new AudioStreamPlayer();
        _voice = new AudioStreamPlayer();
        EnsureAudioBuses();
        _bgm.Bus = "Music";
        _voice.Bus = "Voice";
        AddChild(_bgm);
        AddChild(_voice);
        for (int i = 0; i < _sfx.Length; i++)
        {
            _sfx[i] = new AudioStreamPlayer();
            _sfx[i].Bus = "SFX";
            AddChild(_sfx[i]);
        }
        _audioOutputLatencySeconds = AudioServer.GetOutputLatency();

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
        bool holdMessageSkip = false;                    // --hold-message-skip: hold native logical action 6 for diagnostics
        string textBackend = "auto";                     // --text-backend auto|gdi|portable
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
            if (userArgs[i] == "--perf-log" && i + 1 < userArgs.Length) _perfLogPath = userArgs[i + 1];
            if (userArgs[i] == "--render-backend" && i + 1 < userArgs.Length)
            {
                if (userArgs[i + 1].Equals("gpu", System.StringComparison.OrdinalIgnoreCase))
                    _useGpuBackend = true;
                else if (userArgs[i + 1].Equals("software", System.StringComparison.OrdinalIgnoreCase))
                    _useGpuBackend = false;
                else GD.PushWarning($"unknown --render-backend '{userArgs[i + 1]}'; using gpu");
            }
            if (userArgs[i] == "--frames" && i + 1 < userArgs.Length) int.TryParse(userArgs[i + 1], out _seqFrames);
            if (userArgs[i] == "--sleep-scale" && i + 1 < userArgs.Length) double.TryParse(userArgs[i + 1], out sleepScale);
            if (userArgs[i] == "--speed" && i + 1 < userArgs.Length) double.TryParse(userArgs[i + 1], out speed);
            if (userArgs[i] == "--transition-click-ms" && i + 1 < userArgs.Length) long.TryParse(userArgs[i + 1], out transitionClickMs);
            if (userArgs[i] == "--hold-message-skip") holdMessageSkip = true;
            if (userArgs[i] == "--text-backend" && i + 1 < userArgs.Length)
                textBackend = userArgs[i + 1].ToLowerInvariant();
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
        if (textBackend is not ("auto" or "gdi" or "portable"))
        {
            GD.PushError(
                $"[startup] unknown --text-backend '{textBackend}'; " +
                "expected auto, gdi, or portable");
            GetTree().Quit(2);
            return;
        }
        _clock.Speed = System.Math.Clamp(speed, 0.05, 8.0);
        GD.Print($"[renderer] retained backend={(_useGpuBackend ? "gpu" : "software")}");

        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // Persistence retains AGE's native filenames and formats, but the port owns one profile-root
        // interception point. Himegari's SYS4INI makes SAVEPATH the SAVE child of REGFILEPATH, so both
        // save payloads and SYS4REG.INI remain isolated together under Godot's user directory.
        Sys4PersistencePaths persistencePaths = Sys4PersistencePaths.ResolveProfileOverride(
            catalog.StartupSettings,
            ProjectSettings.GlobalizePath("user://"));
        var nativeSaveStore = new DirectoryNativeDatStore(
            persistencePaths.SaveDirectory,
            new NativeSaveIdentity(
                NativeSaveMagic.S4SD, 0x4a343234, "姫狩りダンジョンマイスター",
                SaveVersion1: 3, SaveVersion2: 10, NumberedCompatibilityId: 0x42323234));
        var sharedProfile = new SharedProfile();
        if (!_selftest) sharedProfile.Load(nativeSaveStore);
        _audioMixerSettings = new AudioMixerSettings();
        if (!_selftest)
        {
            try
            {
                _sys4RegIniStore = Sys4RegIniStore.ForPath(
                    catalog.StartupSettings, persistencePaths.Sys4RegIniPath);
                _audioMixerSettings = _sys4RegIniStore.Load();
                GD.Print($"[settings] native engine options={_sys4RegIniStore.FilePath}");
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                GD.PushWarning($"[settings] audio mixer load failed; using native defaults: {error.Message}");
            }
            _audioMixerSettings.Changed += PersistAudioMixerSettings;
        }
        ApplyAudioMixerSnapshot(_audioMixerSettings.Snapshot());

        // Full op handling everywhere: the provider lets call-script load & run subroutines. Selftest
        // runs a SYNTHESIZED scene (not a real scene in a crippled mode) so its output is deterministic.
        Script script;
        IScriptProvider provider;
        Sys4ScriptProvider? scripts = null;
        IAssetStore? trackedAssetStore = null;
        if (_selftest) (script, provider) = BuildSelfTestScene(table);
        else
        {
            sharedProfile.ConfigureCatalogUnlockSlots(
                catalog.RawSlots.Count,
                catalog.AppendPacks.Select(pair =>
                    new KeyValuePair<int, int>(pair.Key, pair.Value.RawSlots.Count)));
            trackedAssetStore = new CatalogTrackingAssetStore(
                _assetStore,
                entry => sharedProfile.MarkCatalogResourceOpened(entry.PackedId));
            scripts = new Sys4ScriptProvider(table, catalog, trackedAssetStore);
            script = scripts.RequireByName(scene + ".BIN");
            provider = scripts;
        }
        _scripts = scripts;
        bool directSceneHarness = !_selftest
            && !scene.Equals("SYSTEM4", System.StringComparison.OrdinalIgnoreCase);
        if (_timelineLogPath != null) _timeline = new GodotTimelineLog(_timelineLogPath);
        if (!_selftest && pageMapPath == null)
            pageMapPath = System.IO.Path.Combine(Paths.Build, $"page-map-{scene.ToUpperInvariant()}.jsonl");
        _locator = new PageLocatorState(scene, _selftest ? null : pageMapPath);
        _locatorHud.Visible = _locatorHudVisible;
        var resources = scripts != null
            ? new ResourceMap(scripts.Catalog, trackedAssetStore)
            : new ResourceMap(catalog, _assetStore);
        IGlyphMaskRasterizer? surfaceTextRasterizer = null;
        PortableTextRenderingPolicy? portableTextPolicy = null;
        string? exactUnavailable = null;
        try
        {
#if AGE_WINDOWS_GDI
            if (textBackend != "portable")
            {
                if (WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out string availability))
                {
                    try
                    {
                        var exact = new WindowsGdiGlyphMaskRasterizer();
                        surfaceTextRasterizer = exact;
                        _glyphRasterizerOwner = exact;
                    }
                    catch (Exception error) when (textBackend == "auto")
                    {
                        exactUnavailable =
                            $"Exact Windows GDI glyph backend failed to initialize: {error.Message}";
                    }
                }
                else exactUnavailable = availability;
            }
#else
            if (textBackend == "gdi")
                exactUnavailable =
                    "The exact Windows GDI glyph adapter is not part of this platform build.";
#endif
            if (surfaceTextRasterizer == null)
            {
                if (textBackend == "gdi")
                    throw new PlatformNotSupportedException(
                        exactUnavailable ?? "Exact Windows GDI text is unavailable.");
                portableTextPolicy = PortableTextRenderingPolicy.Load();
                var portable =
                    new GodotTextServerGlyphMaskRasterizer(portableTextPolicy);
                surfaceTextRasterizer = portable;
                _glyphRasterizerOwner = portable;
            }
        }
        catch (Exception error)
        {
            GD.PushError($"[startup] text backend selection failed: {error.Message}");
            GetTree().Quit(2);
            return;
        }
        var selectedTextBackend =
            (surfaceTextRasterizer as IIdentifiedGlyphMaskRasterizer)?.BackendInfo
            ?? throw new InvalidOperationException(
                "Selected text backend does not report its policy.");
        GD.Print(
            $"[text] gameplay strings use {selectedTextBackend.Id}: " +
            selectedTextBackend.Detail);
        if (textBackend == "auto" && exactUnavailable != null)
            GD.Print($"[text] exact backend unavailable; selected portable: {exactUnavailable}");
        _host = new GodotAdvHost(
            this, resources, scene, _clock, _locator, logicalCanvas,
            surfaceTextRasterizer,
            timeline: _timeline,
            synchronizeExplicitPresentation: !_selftest,
            surfaceTextMaskCacheCapacity:
                portableTextPolicy?.GlyphMaskCacheCapacity ?? 2048)
        {
            SleepScale = sleepScale,
            TraceOps = _gfxLogPath != null,
        };
        _trace = new GodotTraceSink(_locator, _timeline);
        if (_perfLogPath != null) _perf = new PerformanceFrameLog(_perfLogPath);
        // --trace-histogram: aggregate op/call-site execution counts of the REAL Godot run (headless flow
        // diverges — wait-for-input is a no-op there — so this is the only way to profile the live path).
        _table = table;
        _histFile = histFile;
        Age.Engine.Diagnostics.ITraceSink sink = _trace;
        if (histFile != null) { _hist = new Age.Engine.Diagnostics.HistogramTraceSink();
                                 sink = new Age.Engine.Diagnostics.CompositeTraceSink(_trace, _hist); }
        _vm = new VirtualMachine(script, table, _host,
            GodotVmOptions.Create(_selftest, nativeDebugMenu),
            provider, sink,
            sharedProfile: sharedProfile,
            nativeDatStore: nativeSaveStore,
            audioMixerSettings: _audioMixerSettings);
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
            AdvTextLayoutSnapshot waitLayout =
                _vm.TextHistory.GetLayoutSnapshot(1);
            _host.BindAdvWaitIndicator(
                _vm.TextHistory.GetPresentationBinding(1),
                waitLayout);
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
        if (holdMessageSkip)
            _vm.UpdateKeyboardVirtualKeyState(0x11, true); // Ctrl; bindings resolve it to logical action 6.
        _vmTask = Task.Run(() =>
        {
            try { _vm.Run(); }
            finally { _done = true; }
        });

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
        var perf = _perf;
        var step = perf != null ? _trace.LatestStep : null;
        perf?.BeginFrame(_timelineFrame, _clock.NowMs, delta,
                         step?.Script ?? "<startup>", step?.Offset ?? -1, step?.Opcode ?? -1);
        try
        {
            long phase = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            _host?.PulseFrame();
            perf?.RecordPulse(PerformanceFrameLog.Timestamp() - phase);

            phase = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            UpdateVoicePlaybackState();
            AdoptPendingMovies();
            UpdateMovieFrames();
            perf?.RecordMovies(PerformanceFrameLog.Timestamp() - phase);

            phase = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            HostPresentationReason presentationReasons = HostPresentationReason.None;
            bool presentationEntered = !_selftest && _vm != null && _host != null
                && _host.TryEnterPresentation();
            long allocationPhase = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            bool shouldRecomposite = false;
            try
            {
                if (presentationEntered)
                {
                    presentationReasons = _host!.ConsumePresentationReasons(_vm!.Gfx);
                    shouldRecomposite = presentationReasons != HostPresentationReason.None;
                    perf?.RecordPresentationReasons((int)presentationReasons);
                    perf?.RecordShouldRecomposite(PerformanceFrameLog.Timestamp() - phase);
                    if (shouldRecomposite)
                        Recomposite();   // native publishes retained mutations only at present/service boundaries
                }
                else
                {
                    perf?.RecordPresentationReasons(0);
                    perf?.RecordShouldRecomposite(PerformanceFrameLog.Timestamp() - phase);
                }
            }
            finally
            {
                if (presentationEntered) _host!.ExitPresentation();
            }
            perf?.RecordRecomposeAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);

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
                if (_vm.HaltReason == "STEP-LIMIT")
                {
                    GodotTraceSnapshot haltTrace = _trace.HaltSnapshot ?? _trace.Snapshot();
                    GD.Print(StepLimitDiagnosticFormatter.Format(haltTrace, _table!));
                    CaptureStallDiagnostic(haltTrace, "step-limit");
                }
                if (_selftest) RunSelfTest();
            }
        }
        finally { perf?.EndFrame(); }
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
            // Native blocking effect services poll logical action 4 before ADV hotspot dispatch.
            // Consume the trigger here so a retained hotspot under the transition cannot steal it.
            if (mb.Pressed && action == 4 && _host.IsTransitionWaiting)
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
            if (gameplayKey.Pressed && action == 4 && _host.IsTransitionWaiting)
            {
                _host.SignalInput();
                GetViewport().SetInputAsHandled();
            }
            else if (gameplayKey.Pressed && action >= 0 && _vm.TryActivateInputActions(1 << action))
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
            if (joyButton.Pressed && (actionMask & (1 << 4)) != 0 && _host.IsTransitionWaiting)
            {
                _host.SignalInput();
                GetViewport().SetInputAsHandled();
            }
            else if (joyButton.Pressed && _vm.TryActivateInputActions(actionMask))
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

    private void CaptureStallDiagnostic(GodotTraceSnapshot? traceOverride = null,
                                        string snapshotKind = "stall")
    {
        try
        {
            long nowMs = _clock.NowMs;
            GodotTraceSnapshot trace = traceOverride ?? _trace.Snapshot();
            var activeMovies = _movies
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                {
                    _movieAudio.TryGetValue(pair.Key, out var audio);
                    return new
                    {
                        playback_id = pair.Key,
                        resource_id = pair.Value.ResourceId,
                        name = pair.Value.Name,
                        asset_id = pair.Value.AssetId,
                        stop_time_ms = pair.Value.Decoder.StopTimeMs,
                        initial_position_ms = pair.Value.InitialPositionMs,
                        decoder_completed = pair.Value.Decoder.IsCompleted,
                        decoder_failure = pair.Value.Decoder.Failure,
                        first_frame_source_pts_ms = pair.Value.Decoder.FirstFramePresentationTimeMs,
                        frame_seen = _movieFrameSeen.Contains(pair.Key),
                        completion_notified = _movieCompletionNotified.Contains(pair.Key),
                        watchdog_ms = pair.Value.WatchdogMs,
                        elapsed_ms = (long)Stopwatch.GetElapsedTime(pair.Value.StartedAtTimestamp).TotalMilliseconds,
                        audio_sample_rate = pair.Value.Decoder.AudioInfo?.SampleRate,
                        audio_decode_completed = pair.Value.Decoder.AudioInfo == null
                            ? (bool?)null : pair.Value.Decoder.AudioDecodingCompleted,
                        audio_route = audio?.Route.ToString(),
                        audio_clock_ms = audio?.ClockMs,
                        audio_submitted_through_ms = audio?.SubmittedThroughMs,
                        audio_buffer_underruns = audio?.BufferUnderruns,
                    };
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
                    initial_position_ms = pair.Value.InitialPositionMs,
                    decoder_completed = pair.Value.Decoder.IsCompleted,
                    decoder_failure = pair.Value.Decoder.Failure,
                    first_frame_source_pts_ms = pair.Value.Decoder.FirstFramePresentationTimeMs,
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
                $"{snapshotKind}-{System.DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(snapshot, jsonOptions));
            string coordinate = trace.CurrentOffset >= 0
                ? $"{System.IO.Path.GetFileNameWithoutExtension(trace.CurrentScript).ToUpperInvariant()}@0x{trace.CurrentOffset:x}"
                : trace.CurrentScript;
            string clipboard = $"{coordinate} · {snapshotKind} snapshot {path}";
            DisplayServer.ClipboardSet(clipboard);
            _status.Text = $"Diagnostic saved: {coordinate} (path copied)";
            GD.Print($"[diagnostic] {snapshotKind} snapshot {coordinate} -> {path}");
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
        // Godot reports input in viewport coordinates after content scaling. This ratio is therefore
        // normally identity, while remaining correct for any viewport expansion policy.
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (size.X <= 0 || size.Y <= 0) return (0, 0);
        return ((int)System.Math.Floor(position.X * _screenWidth / size.X),
                (int)System.Math.Floor(position.Y * _screenHeight / size.Y));
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

    public void ShowAgeDiagnostic(string text, string caption)
    {
        try
        {
            OS.Alert(text, caption);
        }
        catch (System.Exception error)
        {
            GD.PushError($"[diagnostic] native alert failed: {error.Message}");
        }
        finally
        {
            _host?.CompleteDiagnosticMessage();
        }
    }

    public void ShowAgeFullwidthTextEditor(string initialText)
    {
        if (_fullwidthTextEditor != null)
        {
            GD.PushWarning("[inputname] replaced an already-open full-width text editor");
            _fullwidthTextEditor.QueueFree();
        }

        var editor = new FullwidthTextEditorDialog();
        _fullwidthTextEditor = editor;
        editor.EditCompleted += (accepted, text) =>
        {
            if (_fullwidthTextEditor == editor) _fullwidthTextEditor = null;
            _host?.CompleteFullwidthTextEdit(accepted, text);
            editor.QueueFree();
        };
        AddChild(editor);
        editor.Open(initialText);
    }

    public override void _ExitTree()
    {
        bool vmStopped = true;
        if (_vm != null) _vm.RequestStop();
        _host?.Stop();
        if (_vmTask != null)
        {
            try
            {
                vmStopped = _vmTask.Wait(System.TimeSpan.FromSeconds(5));
            }
            catch (System.AggregateException error)
            {
                vmStopped = true;
                GD.PushError($"[vm] worker failed during shutdown: {error.Flatten().InnerException?.Message}");
            }
        }

        if (!_selftest && _vm != null)
        {
            if (!vmStopped)
            {
                GD.PushWarning("[persistence] VM did not stop within 5 seconds; skipped concurrent shared-profile flush");
            }
            else
            {
                SharedProfileShutdownFlushResult flush = _vm.FlushSharedProfileOnShutdown();
                if (flush.Outcome == SharedProfileShutdownFlushOutcome.Saved)
                    GD.Print("[persistence] clean shutdown wrote SAVE.DAT and RT.DAT");
                else if (flush.Outcome == SharedProfileShutdownFlushOutcome.Failed)
                    GD.PushWarning($"[persistence] clean-shutdown shared-profile write failed: {flush.Error}");
            }
        }

        DumpHistogram(); _timeline?.Dispose(); _locator?.Dispose();
        _gpuRenderer?.Dispose();
        _glyphRasterizerOwner?.Dispose();
        _glyphRasterizerOwner = null;
        if (_perf != null)
        {
            _perf.Dispose();
            GD.Print($"[perf-log] wrote {_perf.FrameCount} frames / {_perf.RecompositeCount} recomposites -> {_perf.Path}");
            _perf = null;
        }
        foreach (var audio in _movieAudio.Values) audio.Dispose();
        _movieAudio.Clear();
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
    private readonly System.Collections.Generic.List<RenderObject> _visibleSnapshot = new(1024);

    private void Recomposite()
    {
        bool gpuSnapshotCaptured = false;
        BackbufferPublicationPolicy publicationPolicy = default;
        if (_useGpuBackend &&
            TryRecompositeGpu(out gpuSnapshotCaptured, out publicationPolicy)) return;
        _gpuRenderer.Visible = false;
        _screenView.Visible = true;
        RecompositeSoftware(
            gpuSnapshotCaptured ? _visibleSnapshot : null,
            publicationPolicy.PreserveExistingPixels);
    }

    private bool TryRecompositeGpu(
        out bool snapshotCaptured,
        out BackbufferPublicationPolicy publicationPolicy)
    {
        snapshotCaptured = false;
        publicationPolicy = default;
        // Preserve the existing high-volume object/timeline diagnostics exactly. They are debugging tools,
        // not performance workloads, and their software decision strings remain the canonical evidence.
        if (_gfxLogPath != null || _timeline != null) return false;

        long phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        if (_host.TrySnapshotScreenTransition(out _)) return false; // P4: whole-screen offscreen targets
        publicationPolicy =
            _host.SnapshotBackbufferObjects(_vm.Gfx, _clock.NowMs, _visibleSnapshot);
        snapshotCaptured = true;
        _perf?.RecordSnapshotAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        _perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);

        // Additive LERP-tint has not appeared in the target workloads and needs a dedicated additive shader
        // variant before leaving the software oracle.
        if (_visibleSnapshot.Any(v =>
            v.Blend == BlendKind.Additive && !v.MultiplyTint && v.TintStrength > 0))
            return false;

        if (_perf != null)
        {
            var presentStep = _trace.LatestStep;
            _perf.RecordPresentationCoordinate(presentStep?.Script ?? "<startup>",
                                               presentStep?.Offset ?? -1, presentStep?.Opcode ?? -1);
        }
        _perf?.BeginRecomposite(screenTransition: false);
        _gpuRenderer.BeginFrame(publicationPolicy.AppendGpuLayers);
        foreach (var v in _visibleSnapshot)
        {
            _perf?.RecordObject(v.TimeVarying);
            var affine = Transform2DMath.Build(v.Transform, v.Rotation, v.ScaleCycle)
                .FromLocalOrigin(v.DstX, v.DstY);
            if (v.RangeTransform is { } rangeTransform) affine = affine.Then(rangeTransform);
            float opacity = v.Alpha / 255f;
            var rawObject = _vm.Gfx.TryGet(v.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, v.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);

            if (v.SurfaceTransition is { } transition)
            {
                _perf?.RecordTransitionLayer();
                DrawTransitionRangeGpu(_visibleSnapshot, transition);
            }
            else if (v.SurfaceResId == 0 && texture == null)
            {
                if (v.Blend != BlendKind.Opaque)
                {
                    int width = v.W > 0 ? v.W : _screenWidth;
                    int height = v.H > 0 ? v.H : _screenHeight;
                    float fillOpacity = v.MultiplyTint
                        ? opacity
                        : opacity * v.TintStrength / 255f;
                    _perf?.RecordFillLayer();
                    if (_gpuRenderer.DrawFill(width, height, affine, v.Tint, fillOpacity))
                        _perf?.RecordGpuLayer(width, height, affine, _screenWidth, _screenHeight,
                                              dynamic: false, BlendKind.Alpha);
                }
                else _perf?.RecordSkippedLayer();
            }
            else
            {
                if (texture == null && !movieSurfaceBound)
                {
                    resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = _host.ResolveResIdTexture(v.SurfaceResId);
                    _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null) _perf?.RecordSkippedLayer();
                else
                {
                    var resolved = texture.Value;
                    bool drawn = _gpuRenderer.DrawTexture(resolved.Image, resolved.AssetId, v.ColorKey,
                        v.SrcX, v.SrcY, v.W, v.H, affine, v.Tint, v.TintStrength,
                        opacity, v.MultiplyTint, resolved.IsDynamic,
                        rawObject?.SourceSlot ?? v.Handle, v.Blend);
                    if (drawn)
                        _perf?.RecordGpuLayer(v.W, v.H, affine, _screenWidth, _screenHeight,
                                              resolved.IsDynamic, v.Blend);
                }
            }
        }

        var stats = _gpuRenderer.EndFrame();
        _perf?.RecordGpu(stats.DrawItems, stats.TextureUploads, stats.TextureUploadTicks);
        _screenView.Visible = false;
        _gpuRenderer.Visible = true;
        _perf?.EndRecomposite();
        return true;
    }

    // Native type-0 retained range transition: range A has already passed through ordinary z-order;
    // republish range B at the transition placeholder with progress-scaled source opacity. This mirrors
    // DrawTransitionRange's software-oracle order without allocating an offscreen CPU surface.
    private int DrawTransitionRangeGpu(IReadOnlyList<RenderObject> visible, SurfaceTransitionState transition)
    {
        int drawn = 0;
        long end = transition.RangeBStart + transition.RangeBCount;
        foreach (var source in visible)
        {
            if (source.Handle < transition.RangeBStart || source.Handle >= end || source.SurfaceTransition != null)
                continue;
            _perf?.RecordObject(source.TimeVarying);
            var affine = Transform2DMath.Build(source.Transform, source.Rotation, source.ScaleCycle)
                .FromLocalOrigin(source.DstX, source.DstY);
            if (source.RangeTransform is { } rangeTransform) affine = affine.Then(rangeTransform);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            var rawObject = _vm.Gfx.TryGet(source.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, source.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);

            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque)
                {
                    _perf?.RecordSkippedLayer();
                    continue;
                }
                int width = source.W > 0 ? source.W : _screenWidth;
                int height = source.H > 0 ? source.H : _screenHeight;
                _perf?.RecordFillLayer();
                if (_gpuRenderer.DrawFill(width, height, affine, source.Tint,
                    opacity * source.TintStrength / 255f))
                {
                    _perf?.RecordGpuLayer(width, height, affine, _screenWidth, _screenHeight,
                                          dynamic: false, BlendKind.Alpha);
                    drawn++;
                }
                continue;
            }

            if (!movieSurfaceBound && texture == null)
            {
                resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
                texture = _host.ResolveResIdTexture(source.SurfaceResId);
                _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            }
            if (texture == null)
            {
                _perf?.RecordSkippedLayer();
                continue;
            }
            var resolved = texture.Value;
            if (_gpuRenderer.DrawTexture(resolved.Image, resolved.AssetId, source.ColorKey,
                source.SrcX, source.SrcY, source.W, source.H, affine, source.Tint, source.TintStrength,
                opacity, source.MultiplyTint, resolved.IsDynamic,
                rawObject?.SourceSlot ?? source.Handle, source.Blend))
            {
                _perf?.RecordGpuLayer(source.W, source.H, affine, _screenWidth, _screenHeight,
                                      resolved.IsDynamic, source.Blend);
                drawn++;
            }
        }
        return drawn;
    }

    private void RecompositeSoftware(
        IReadOnlyList<RenderObject>? sampledVisible = null,
        bool preserveExistingPixels = false)
    {
        if (_perf != null)
        {
            var presentStep = _trace.LatestStep;
            _perf.RecordPresentationCoordinate(presentStep?.Script ?? "<startup>",
                                               presentStep?.Offset ?? -1, presentStep?.Opcode ?? -1);
        }
        long phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        bool hasScreenTransition = _host.TrySnapshotScreenTransition(out var transition);
        _perf?.RecordSnapshotAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        _perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);
        if (!hasScreenTransition && sampledVisible == null)
        {
            phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            preserveExistingPixels = _host.SnapshotBackbufferObjects(
                _vm.Gfx, _clock.NowMs, _visibleSnapshot).PreserveExistingPixels;
            _perf?.RecordSnapshotAllocation(
                PerformanceFrameLog.AllocatedBytes() - allocationPhase);
            _perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);
        }
        if (hasScreenTransition) preserveExistingPixels = false;
        _perf?.BeginRecomposite(hasScreenTransition);

        phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        if (!preserveExistingPixels)
        {
            System.Array.Clear(_screenPixels);
        }
        _perf?.RecordClear(PerformanceFrameLog.Timestamp() - phase);
        System.Collections.Generic.Dictionary<long, string>? decisions = _gfxLogPath != null || _timeline != null ? new() : null;
        if (hasScreenTransition)
        {
            allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            // Native mode 4 keeps the captured source opaque and alpha-composites the complete target
            // surface over it. Each offscreen target has an opaque-black clear beneath its objects.
            CompositeVisibleObjects(transition.Source, 1f, decisions);
            FillQuad(0, 0, _screenWidth, _screenHeight, 0, (float)transition.Progress);
            CompositeVisibleObjects(transition.Target, (float)transition.Progress, decisions);
            _perf?.RecordCompositeAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        }
        else
        {
            allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            CompositeVisibleObjects(sampledVisible ?? _visibleSnapshot, 1f, decisions);
            _perf?.RecordCompositeAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        }
        phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        _screen.SetData(_screenWidth, _screenHeight, false, Image.Format.Rgba8, _screenPixels);
        _perf?.RecordSetDataAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        _perf?.RecordSetData(PerformanceFrameLog.Timestamp() - phase);
        phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        _screenTex.Update(_screen);
        _perf?.RecordTextureUpdate(PerformanceFrameLog.Timestamp() - phase);
        if (decisions != null) LogGfxDecisionChanges(decisions);
        _perf?.EndRecomposite();
    }

    private void CompositeVisibleObjects(
        IReadOnlyList<RenderObject> visible,
        float globalOpacity,
        System.Collections.Generic.Dictionary<long, string>? decisions)
    {
        int z = 0;
        foreach (var v in visible)   // interpolate at the retained-presentation clock
        {
            _perf?.RecordObject(v.TimeVarying);
            var t = v.Transform;
            var affine = Age.Engine.Model.Transform2DMath.Build(t, v.Rotation, v.ScaleCycle);
            var localToDest = affine.FromLocalOrigin(v.DstX, v.DstY);
            if (v.RangeTransform is { } rangeTransform)
                localToDest = localToDest.Then(rangeTransform);
            var projected = localToDest.Apply(0, 0);
            int dstX = (int)System.Math.Round(projected.X);
            int dstY = (int)System.Math.Round(projected.Y);
            float opacity = v.Alpha / 255f * globalOpacity;  // transform Z is never opacity
            float strength = v.TintStrength / 255f;          // tint-blend / fill strength
            var rawObject = _vm.Gfx.TryGet(v.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var surfaceTexture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, v.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);
            // These strings exist only for --gfx-log/timeline diagnostics. DEBUGMAP visits roughly one
            // thousand retained objects per composition, so formatting them unconditionally creates
            // several megabytes of short-lived garbage even in an ordinary run.
            string? outcome = null;
            if (v.SurfaceTransition is { } transition)
            {
                _perf?.RecordTransitionLayer();
                int layers = DrawTransitionRange(visible, transition);
                if (decisions != null)
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
                    int baseW = v.W > 0 ? v.W : _screenWidth;
                    int baseH = v.H > 0 ? v.H : _screenHeight;
                    // One-shot/mode-1 packed color supplies opacity directly. Static mode-0 fills retain
                    // the tint-strength convention used by the existing effect objects.
                    float fillA = v.MultiplyTint ? opacity : opacity * strength;
                    _perf?.RecordFillLayer();
                    FillAffineQuad(baseW, baseH, localToDest, v.Tint, fillA);
                    if (decisions != null)
                        outcome = $"FILL tint=0x{v.Tint:x6} a={fillA:0.00} {baseW}x{baseH}@({dstX},{dstY}) " +
                                  $"base=({v.DstX},{v.DstY}) anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) " +
                                  $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) " +
                                  $"trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) rot={v.Rotation.AngleDegrees:0.0}" +
                                  ColorTimeline(v.ColorTransition);
                }
                else
                {
                    _perf?.RecordSkippedLayer();
                    if (decisions != null) outcome = "SKIP(no-resId, opaque render-target)";
                }
            }
            else
            {
                var texture = surfaceTexture;
                if (texture == null && !movieSurfaceBound)
                {
                    resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = _host.ResolveResIdTexture(v.SurfaceResId);
                    _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null)
                {
                    _perf?.RecordSkippedLayer();
                    if (decisions != null) outcome = $"SKIP(resId=0x{v.SurfaceResId:x} UNRESOLVED)";
                }
                else
                {
                    BlitLayer(texture.Value.Image, texture.Value.AssetId, v.ColorKey, v.Tint, strength, v.SrcX, v.SrcY, v.W, v.H,
                              localToDest, opacity, v.MultiplyTint, texture.Value.IsDynamic, v.Blend);
                    if (decisions != null)
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
            z++;
        }
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
            _perf?.RecordObject(source.TimeVarying);
            var affine = Transform2DMath.Build(source.Transform, source.Rotation, source.ScaleCycle)
                .FromLocalOrigin(source.DstX, source.DstY);
            if (source.RangeTransform is { } rangeTransform)
                affine = affine.Then(rangeTransform);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            var rawObject = _vm.Gfx.TryGet(source.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, source.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);
            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque)
                {
                    _perf?.RecordSkippedLayer();
                    continue;
                }
                int w = source.W > 0 ? source.W : _screenWidth;
                int h = source.H > 0 ? source.H : _screenHeight;
                _perf?.RecordFillLayer();
                FillAffineQuad(w, h, affine, source.Tint, opacity * source.TintStrength / 255f);
            }
            else
            {
                if (!movieSurfaceBound && texture == null)
                {
                    resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = _host.ResolveResIdTexture(source.SurfaceResId);
                    _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null)
                {
                    _perf?.RecordSkippedLayer();
                    continue;
                }
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
        long sourcePrepStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long sourcePrepAllocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
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
        _perf?.RecordSourcePrep(PerformanceFrameLog.Timestamp() - sourcePrepStarted);
        _perf?.RecordSourcePrepAllocation(PerformanceFrameLog.AllocatedBytes() - sourcePrepAllocated);
        if (sw <= 0 || sh <= 0) return;
        long rasterStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        Age.Engine.Model.SoftwareAffineRasterizer.BlitRgba(
            _screenPixels, _screenWidth, _screenHeight, sourcePixels, sourceWidth, sourceHeight,
            srcX, srcY, sw, sh, localToDest, tint, tintStrength, alpha, multiplyTint, blend);
        _perf?.RecordRaster(sw, sh, localToDest, _screenWidth, _screenHeight, dynamic, blend,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    private void FillAffineQuad(int w, int h, Age.Engine.Model.Affine2D localToDest, long tint, float alpha)
    {
        long rasterStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        Age.Engine.Model.SoftwareAffineRasterizer.FillRgba(
            _screenPixels, _screenWidth, _screenHeight, w, h, localToDest, tint, alpha);
        _perf?.RecordRaster(w, h, localToDest, _screenWidth, _screenHeight, false, BlendKind.Alpha,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    // Alpha-blend a solid tint (0xRRGGBB) rectangle over the screen — the surfaceless fade/flash fill.
    private void FillQuad(int dstX, int dstY, int w, int h, long tint, float alpha)
    {
        int ia = (int)(System.Math.Clamp(alpha, 0f, 1f) * 255);
        if (ia == 0) return;
        long rasterStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        int tr = (int)((tint >> 16) & 0xff), tg = (int)((tint >> 8) & 0xff), tb = (int)(tint & 0xff);
        byte[] dst = _screenPixels;
        int dw = _screenWidth, dh = _screenHeight;
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
        _perf?.RecordFillLayer();
        _perf?.RecordRaster(w, h, new Affine2D(1, 0, 0, 1, dstX, dstY),
                            _screenWidth, _screenHeight, false, BlendKind.Alpha,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    // Make colorkey-matching texels transparent (native colorkey is baked at surface load).
    private static void BakeColorKey(byte[] px, long colorKey)
    {
        for (int i = 0; i < px.Length; i += 4)
            if (Age.Engine.Model.BlendMath.ColorKeyMatches(px[i], px[i + 1], px[i + 2], colorKey))
                px[i + 3] = 0;
    }

    // Decode VFS-owned bytes in Godot. Ordinary BGM loops; forced starts retain AGE's
    // loop/one-shot mode. Voice plays once, cutting off any prior line.
    public void PlayBgm(byte[] oggBytes, string assetName)
        => StartBgm(oggBytes, assetName, 1);

    public void RestartBgm(byte[] oggBytes, string assetName, int startMode)
        => StartBgm(oggBytes, assetName, startMode);

    private void StartBgm(byte[] oggBytes, string assetName, int startMode)
    {
        var stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
        if (stream == null) { GD.Print($"OGG load failed {assetName}"); return; }
        stream.Loop = startMode != 0;
        // FadeBgm is marshalled from the VM thread and starts on the next Godot frame, while the VM's
        // blocking deadline begins immediately. Scene startup can consequently request the replacement
        // track just before the old fade tween reaches zero. Do not let that orphaned tween mute the new
        // stream after this method restores its normal gain.
        CancelBgmFade();
        _bgm.VolumeDb = 0;
        _bgm.Stream = stream;
        _bgm.Play();
    }

    public void StopBgm()
    {
        RestoreVoiceBgmDuck();
        CancelBgmFade();
        _bgm.Stop();
        _bgm.Stream = null;
        _bgm.VolumeDb = 0;
    }

    private void PersistAudioMixerSettings(AudioMixerSettingsSnapshot snapshot)
    {
        try
        {
            _sys4RegIniStore?.Save(snapshot);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            System.Console.Error.WriteLine(
                $"[settings] audio mixer save failed; runtime setting remains active: {error.Message}");
        }
    }

    private void ApplyAudioMixerSnapshot(AudioMixerSettingsSnapshot snapshot)
    {
        for (int category = 0; category < AudioMixerSettings.CategoryCount; category++)
            ApplyAudioVolume(category, snapshot.Volumes[category]);
        for (int category = (int)AudioMixerCategory.Music;
             category < AudioMixerSettings.CategoryCount;
             category++)
            SetAudioBusMuted(category, !snapshot.Routes[category]);
    }

    public void ApplyAudioVolume(int category, int basisPoints)
    {
        string? busName = AudioBusName(category);
        if (busName == null) return;
        int bus = AudioServer.GetBusIndex(busName);
        if (bus < 0) return;
        int effectiveBasisPoints = basisPoints < 0
            ? AudioMixerSettings.MaximumVolume
            : System.Math.Clamp(basisPoints, 0, AudioMixerSettings.MaximumVolume);
        float linear = effectiveBasisPoints / (float)AudioMixerSettings.MaximumVolume;
        AudioServer.SetBusVolumeDb(bus, linear <= 0 ? -80.0f : Mathf.LinearToDb(linear));
    }

    public void ApplyAudioRouteEnabled(int category, bool enabled)
    {
        if (category == (int)AudioMixerCategory.Music)
        {
            RestoreVoiceBgmDuck();
            CancelBgmFade();
            if (enabled)
            {
                if (_bgm.Stream != null) _bgm.Play();
            }
            else
                _bgm.Stop();
        }
        else if (!enabled && category == (int)AudioMixerCategory.SoundEffect)
        {
            CancelScheduledSoundEffectStarts();
            foreach (AudioStreamPlayer player in _sfx) player.Stop();
        }
        else if (!enabled && category == (int)AudioMixerCategory.Voice)
            StopVoiceForMessageSkip();

        SetAudioBusMuted(category, !enabled);
    }

    private static void SetAudioBusMuted(int category, bool muted)
    {
        string? busName = AudioBusName(category);
        if (busName == null) return;
        int bus = AudioServer.GetBusIndex(busName);
        if (bus >= 0) AudioServer.SetBusMute(bus, muted);
    }

    private static string? AudioBusName(int category) => category switch
    {
        (int)AudioMixerCategory.Master => "Master",
        (int)AudioMixerCategory.Music => "Music",
        (int)AudioMixerCategory.SoundEffect => "SFX",
        (int)AudioMixerCategory.Voice => "Voice",
        (int)AudioMixerCategory.Movie => "Movie",
        _ => null,
    };

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
        // Native voice ducking is suppressed while an explicit 0xc2 BGM fade owns the envelope.
        if (_bgmFadeTween != null) return;
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
        // original WAV bytes; Godot sees an AGE-compatible first-RIFF copy without CP932 INFO metadata.
        byte[] godotWav = RiffWaveSanitizer.PrepareForGodot(wavBytes);
        var stream = AudioStreamWav.LoadFromBuffer(godotWav);
        if (stream == null) { GD.Print($"WAV load failed {assetName}"); return; }
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        _sfx[channel].VolumeDb = 0;
        _sfx[channel].Stream = stream;
    }

    public void StartSoundEffect(int channel) => StartSoundEffect(channel, 0);

    public void StartSoundEffect(int channel, int startMode)
    {
        if ((uint)channel >= (uint)_sfx.Length || _sfx[channel].Stream == null) return;
        if (_sfx[channel].Stream is AudioStreamWav wav)
            wav.LoopMode = startMode == 0
                ? AudioStreamWav.LoopModeEnum.Disabled
                : AudioStreamWav.LoopModeEnum.Forward;
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
        RestoreVoiceBgmDuck();
        CancelBgmFade();
        if (realDurationSeconds <= 0) { _bgm.VolumeDb = targetDb; return; }

        var tween = CreateTween();
        _bgmFadeTween = tween;
        tween.Finished += () =>
        {
            if (ReferenceEquals(_bgmFadeTween, tween))
                _bgmFadeTween = null;
        };
        tween.TweenProperty(_bgm, "volume_db", targetDb, realDurationSeconds);
    }

    private void CancelBgmFade()
    {
        var tween = _bgmFadeTween;
        _bgmFadeTween = null;
        if (tween?.IsValid() == true)
            tween.Kill();
    }

    public bool TryPlayMovie(byte[] mpegBytes, string assetName, long playbackId,
                             long resourceId, int assetId, long movieFlags,
                             long initialPositionMs, long startDelayMs,
                             long? presentationDurationMs,
                             out long? stopTimeMs)
    {
        stopTimeMs = null;
        try
        {
            var payload = new Age.Engine.Sys4.MoviePayload(assetName, mpegBytes);
            var runtime = MovieRuntime.Open(
                assetName, assetId, resourceId, payload, _movieDecoderFactory, movieFlags,
                initialPositionMs, startDelayMs, presentationDurationMs);
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
            if (movie.Decoder.AudioInfo != null)
            {
                if (_movieAudio.Remove(playbackId, out var priorAudio)) priorAudio.Dispose();
                _movieAudio[playbackId] = new MovieAudioOutput(
                    this, movie.Decoder, MovieAudioRouteFromFlags(movie.MovieFlags),
                    _audioOutputLatencySeconds);
            }
            _movieCompletionNotified.Remove(playbackId);
            GD.Print($"movie started {movie.Name} playback={playbackId} " +
                     $"({movie.Decoder.StopTimeMs?.ToString() ?? "unknown"} ms from VFS" +
                     (movie.InitialPositionMs > 0 ? $", start={movie.InitialPositionMs}ms" : "") +
                     (movie.Decoder.AudioInfo is { } audio
                         ? $", audio={audio.SampleRate}Hz stereo route={MovieAudioRouteFromFlags(movie.MovieFlags)}"
                         : "") + ")");
        }
    }

    private void UpdateMovieFrames()
    {
        if (_host == null) return;
        foreach (var (playbackId, movie) in _movies)
        {
            long elapsedMs = (long)Stopwatch.GetElapsedTime(
                movie.StartedAtTimestamp).TotalMilliseconds;
            if (elapsedMs < movie.StartDelayMs) continue;
            bool frameWasAlreadySeen = _movieFrameSeen.Contains(playbackId);
            _movieAudio.TryGetValue(playbackId, out var audio);
            if (frameWasAlreadySeen) audio?.Update();
            if (movie.Decoder.TryTakeFrame(out var frame))
            {
                _host.PublishMovieFrame(playbackId, movie.Name, movie.AssetId, frame);
                if (_movieFrameSeen.Add(playbackId))
                {
                    GD.Print($"movie first frame {movie.Name} playback={playbackId}: " +
                             $"{frame.Width}x{frame.Height} RGBA8 at render frame {_timelineFrame} " +
                             $"(source PTS {movie.Decoder.FirstFramePresentationTimeMs?.ToString() ?? "unknown"} ms)");
                    // Do not let decode startup consume the opening audio timeline. The first PCM push
                    // begins only after the first decoded image has reached the retained movie surface.
                    audio?.Update();
                }
            }
            bool watchdogExpired = elapsedMs >= movie.WatchdogMs;
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
        if (_movieAudio.Remove(playbackId, out var audio)) audio.Dispose();
        if (_pendingMovies.TryRemove(playbackId, out var pending)) pending.Decoder.Dispose();
        if (_movies.Remove(playbackId, out var movie))
        {
            movie.Decoder.Dispose();
            GD.Print($"movie stopped {movie.Name} playback={playbackId} at render frame {_timelineFrame}");
        }
        _movieFrameSeen.Remove(playbackId);
        _movieCompletionNotified.Remove(playbackId);
    }

    private static MovieAudioRoute MovieAudioRouteFromFlags(long flags)
    {
        ulong value = unchecked((ulong)flags);
        if ((value & 0x10000) != 0) return MovieAudioRoute.Muted;
        if ((value & 0x20000) != 0) return MovieAudioRoute.Music;
        if ((value & 0x40000) != 0) return MovieAudioRoute.SoundEffect;
        if ((value & 0x80000) != 0) return MovieAudioRoute.Voice;
        return MovieAudioRoute.Movie;
    }

    private static void EnsureAudioBuses()
    {
        foreach (string name in new[] { "Music", "SFX", "Voice", "Movie" })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, name);
        }
    }

    public void PageBreak()
    {
        _pageCount++;
        _status.Text = "";
        if (_locatorHudVisible) _locatorHud.Text = _locator.CurrentDisplay;
    }
    public void ClearPage()
    {
        _status.Text = "";
    }
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
        var debugEntries = DebugSceneCatalog.Build(_catalog);
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
        var selftestResources = new ResourceMap(_catalog, _assetStore);
        AudioPayload glowSfx = selftestResources.ReadAudio(selftestResources.ResolveSoundEffect(0x28)!);
        byte[] glowGodotWav = RiffWaveSanitizer.PrepareForGodot(glowSfx.Bytes);
        bool cp932WavMetadataOk = glowGodotWav.Length == 688_336
                                 && AudioStreamWav.LoadFromBuffer(glowGodotWav) != null;
        AudioPayload bossSfx = selftestResources.ReadAudio(
            selftestResources.ResolveSoundEffect(0x125)!);
        byte[] bossGodotWav = RiffWaveSanitizer.PrepareForGodot(bossSfx.Bytes);
        bool firstRiffBoundaryOk = bossSfx.Bytes.Length == 323_009
                                   && bossGodotWav.Length == 157_940
                                   && AudioStreamWav.LoadFromBuffer(bossGodotWav) != null;
        AudioPayload bgm = selftestResources.ReadAudio(selftestResources.ResolveBgm(5)!);
        FadeBgm(0, 10.0);
        bool bgmFadeStarted = _bgmFadeTween?.IsValid() == true;
        PlayBgm(bgm.Bytes, bgm.Name);
        bool bgmReplacementCancelsFade = bgmFadeStarted
                                         && _bgmFadeTween == null
                                         && System.Math.Abs(_bgm.VolumeDb) < 0.001f;
        RestartBgm(bgm.Bytes, bgm.Name, 0);
        bool bgmOneShotModeOk = (_bgm.Stream as AudioStreamOggVorbis)?.Loop == false;
        RestartBgm(bgm.Bytes, bgm.Name, 1);
        bool bgmLoopModeOk = (_bgm.Stream as AudioStreamOggVorbis)?.Loop == true;
        StopBgm();
        bool bgmStopReleaseOk = _bgm.Stream == null
                                && !_bgm.Playing
                                && _bgmFadeTween == null
                                && System.Math.Abs(_bgm.VolumeDb) < 0.001f;
        IReadOnlyList<(int X, int Y)> outline =
            AgeGlyphMaskCompositor.GetMode3OutlineOffsets(1, 1);
        bool textEffectModesOk =
            outline.Count == 12
            && outline.Contains((1, 0))
            && outline.Contains((-1, 0))
            && outline.Contains((0, 1))
            && outline.Contains((0, -1));
        bool fontCalibrationOk =
            ImmediateSurfaceTextRenderer.NativePixelHeight(24) == 24
            && ImmediateSurfaceTextRenderer.NativePixelHeight(25) == 24
            && ImmediateSurfaceTextRenderer.NativePixelHeight(32) == 31
            && ImmediateSurfaceTextRenderer.NativePixelHeight(33) == 31;
        bool immediateSurfaceTextOk;
        string immediateSurfaceTextMode;
        _host.CreateTexture(997, 64, 24);
        var immediateStyle = AdvTextStyle.Default with
        {
            PrimaryFontSize = 16,
            TextColor = 0xffffff,
            FontFace = ImmediateSurfaceTextRenderer.DefaultFontFace,
        };
        _host.DrawStringToSurface(997, 1, 1, "A姫", immediateStyle);
        if (_host.UsesSurfaceTextPixels)
        {
            RgbaImage? pixels = _host.CaptureSurfacePixels(997);
            GlyphRasterizerBackendInfo? backend = _host.SurfaceTextBackendInfo;
            var cache = _host.SurfaceTextMaskCacheStats;
            bool pixelsPresent = pixels != null
                                 && pixels.Pixels.Where((_, index) => index % 4 == 3)
                                     .Any(alpha => alpha != 0);
            bool gpuAccepted = false;
            if (pixels != null)
            {
                _gpuRenderer.BeginFrame(false);
                gpuAccepted = _gpuRenderer.DrawTexture(
                    pixels, int.MinValue + 997, -1,
                    0, 0, pixels.Width, pixels.Height,
                    new Affine2D(1, 0, 0, 1, 2, 3),
                    0xff8080, 255, 0.5f, true,
                    dynamic: true, dynamicKey: 997, BlendKind.Alpha);
                GpuRetainedRenderer.FrameStats stats = _gpuRenderer.EndFrame();
                gpuAccepted &= stats.DrawItems == 1 && stats.TextureUploads == 1;
            }

            _host.CreateTexture(996, 64, 24);
            _host.CopySurfaceRect(new SurfaceRectCopy(
                997, 996, 0, 0, 64, 24, 0, 0));
            RgbaImage? copied = _host.CaptureSurfacePixels(996);
            bool copiedPixels = pixels != null
                                && copied != null
                                && pixels.Pixels.SequenceEqual(copied.Pixels);
            _host.FillSurfaceRect(new SurfaceRectFill(
                996, 0, 0, 64, 24, 0, 0));
            RgbaImage? cleared = _host.CaptureSurfacePixels(996);
            bool clearedPixels = cleared != null && cleared.Pixels.All(value => value == 0);
            immediateSurfaceTextOk =
                pixelsPresent
                && backend is not null
                && (backend.Policy == GlyphRasterPolicy.NativeCp932Gray4
                    ? backend is
                    {
                        Id: "windows-gdi-gray4",
                        NativePixelExact: true,
                    }
                    : backend is
                    {
                        Id: "portable-godot-textserver",
                        Policy: GlyphRasterPolicy.PortableUnicode,
                        NativePixelExact: false,
                    })
                && cache.Count is > 0 and <= 2048
                && cache.Capacity == 2048
                && gpuAccepted
                && copiedPixels
                && clearedPixels;
            immediateSurfaceTextMode =
                $"rgba:{backend?.Id}";
        }
        else
        {
            immediateSurfaceTextOk = false;
            immediateSurfaceTextMode = "unavailable";
        }
        _host.ReleaseSurface(996);
        _host.ReleaseSurface(997);
        bool liveRetainedTextOk;
        string liveRetainedTextMode;
        AdvTextLayoutPresentationBinding liveBinding =
            _vm.TextHistory.GetPresentationBinding(1);
        if (_host.UsesSurfaceTextPixels)
        {
            static bool InLiveRange(
                RenderObject item, AdvTextLayoutPresentationBinding binding)
                => item.Handle >= binding.FirstObjectHandle
                   && item.Handle - binding.FirstObjectHandle < binding.ObjectCapacity;
            IReadOnlyList<RenderObject> liveObjects = _vm.Gfx.SnapshotVisibleObjects()
                .Where(item => InLiveRange(item, liveBinding))
                .ToArray();
            RgbaImage? liveSurface =
                _host.CaptureSurfacePixels(liveBinding.SourceSurfaceSlot);
            bool builtCompleteLine =
                liveObjects.Count == "HelloWorldSub".Length
                && liveObjects.Select(item => item.Handle)
                    .SequenceEqual(Enumerable.Range(0, liveObjects.Count)
                        .Select(index => liveBinding.FirstObjectHandle + index))
                && liveSurface != null
                && liveSurface.Pixels.Where((_, index) => index % 4 == 3)
                    .Any(alpha => alpha != 0)
                && _vm.TextHistory.GetLayoutSnapshot(1).CursorX
                   > liveBinding.ResetCursorX;

            _vm.Gfx.EraseRange(liveBinding.FirstObjectHandle + 1, 1);
            _host.PublishAdvTextLayout(_vm.Gfx, liveBinding);
            bool republishedPartialErase =
                _vm.Gfx.SnapshotVisibleObjects()
                    .Count(item => InLiveRange(item, liveBinding))
                == liveObjects.Count;

            _host.SetAdvPagePresentationSuspended(_vm.Gfx, true);
            bool suspended =
                !_vm.Gfx.SnapshotVisibleObjects()
                    .Any(item => InLiveRange(item, liveBinding));
            _host.SetAdvPagePresentationSuspended(_vm.Gfx, false);
            bool restored =
                _vm.Gfx.SnapshotVisibleObjects()
                    .Count(item => InLiveRange(item, liveBinding))
                == liveObjects.Count;

            _vm.Gfx.EraseRange(
                liveBinding.FirstObjectHandle, liveBinding.ObjectCapacity);
            _host.ResetRenderedAdvTextLayout(_vm.Gfx, liveBinding);
            RgbaImage? resetSurface =
                _host.CaptureSurfacePixels(liveBinding.SourceSurfaceSlot);
            bool reset =
                resetSurface != null
                && resetSurface.Pixels.All(value => value == 0)
                && !_vm.Gfx.SnapshotVisibleObjects()
                    .Any(item => InLiveRange(item, liveBinding));
            liveRetainedTextOk =
                builtCompleteLine
                && republishedPartialErase
                && suspended
                && restored
                && reset;
            liveRetainedTextMode =
                $"retained-glyphs;built={builtCompleteLine};objects={liveObjects.Count};" +
                $"republished={republishedPartialErase};suspended={suspended};" +
                $"restored={restored};reset={reset}";
        }
        else
        {
            liveRetainedTextOk = false;
            liveRetainedTextMode = "unavailable";
        }
        _vm.TextHistory.DefineLayout(2, 256, 64, 10, 100);
        _vm.TextHistory.SetResetCursor(2, 1, 1);
        _vm.TextHistory.SetBounds(2, 255, 63);
        _vm.TextHistory.SetTextObjectRange(2, 710000, 64);
        _vm.TextHistory.ResetLayout(2);
        AdvTextLayoutSnapshot historyLayout =
            _vm.TextHistory.GetLayoutSnapshot(2);
        AdvTextLayoutPresentationBinding historyBinding =
            _vm.TextHistory.GetPresentationBinding(2);
        _host.ResetRenderedAdvTextLayout(_vm.Gfx, historyBinding);
        var historyBatch = new AdvTextHistoryRenderBatch(
            2,
            0,
            0,
            historyLayout,
            "History",
            immediateStyle);
        bool historyUsedRetained =
            _host.RenderTextHistory(
                _vm.Gfx,
                historyBinding,
                historyBatch);
        bool historyRetainedTextOk;
        string historyRetainedTextMode;
        if (_host.UsesSurfaceTextPixels)
        {
            RgbaImage? historySurface =
                _host.CaptureSurfacePixels(historyBinding.SourceSurfaceSlot);
            historyRetainedTextOk =
                historyUsedRetained
                && _vm.Gfx.SnapshotVisibleObjects()
                    .Count(item =>
                        item.Handle >= historyBinding.FirstObjectHandle
                        && item.Handle - historyBinding.FirstObjectHandle
                           < historyBinding.ObjectCapacity)
                   == historyBatch.Text.Length
                && historySurface != null
                && historySurface.Pixels.Where((_, index) => index % 4 == 3)
                    .Any(alpha => alpha != 0);
            _vm.Gfx.EraseRange(
                historyBinding.FirstObjectHandle,
                historyBinding.ObjectCapacity);
            _host.ClearRenderedAdvTextLayout(historyBinding.LayoutSlot);
            _host.EndTextHistoryPresentation(_vm.Gfx);
            historyRetainedTextOk &=
                !_vm.Gfx.SnapshotVisibleObjects()
                    .Any(item =>
                        item.Handle >= historyBinding.FirstObjectHandle
                        && item.Handle - historyBinding.FirstObjectHandle
                           < historyBinding.ObjectCapacity)
                && _host.CaptureSurfacePixels(historyBinding.SourceSurfaceSlot)
                    ?.Pixels.All(value => value == 0) == true;
            historyRetainedTextMode =
                $"retained-glyphs;used={historyUsedRetained}";
        }
        else
        {
            historyRetainedTextOk = false;
            _host.EndTextHistoryPresentation(_vm.Gfx);
            historyRetainedTextMode = "unavailable";
        }
        Window rootWindow = GetTree().Root;
        bool logicalCanvasOk = _host.LogicalCanvas == new Sys4LogicalCanvas(_screenWidth, _screenHeight)
                               && _screen.GetWidth() == _screenWidth
                               && _screen.GetHeight() == _screenHeight
                               && _screenPixels.Length == checked(_screenWidth * _screenHeight * 4)
                               && rootWindow.ContentScaleSize
                                  == new Vector2I(_screenWidth, _screenHeight)
                               && _windowOptions == WindowLaunchOptions.Resolve(
                                      OS.GetCmdlineUserArgs(),
                                      new Sys4LogicalCanvas(_screenWidth, _screenHeight));
        var backbufferSnapshot = new List<RenderObject>();
        _host.PresentFrame(_vm.Gfx);
        BackbufferPublicationPolicy fullPolicy =
            _host.SnapshotBackbufferObjects(
                _vm.Gfx, _clock.NowMs, backbufferSnapshot);
        bool backbufferPreservationOk =
            fullPolicy
                == new BackbufferPublicationPolicy(
                    PreserveExistingPixels: true,
                    AppendGpuLayers: backbufferSnapshot.Count == 0)
            && GodotAdvHost.ResolveBackbufferPublicationPolicy(
                true, GfxHandleRange.All, 1)
                == new BackbufferPublicationPolicy(true, false)
            && GodotAdvHost.ResolveBackbufferPublicationPolicy(
                true, new GfxHandleRange(0, 60000), 1)
                == new BackbufferPublicationPolicy(true, true);
        _host.ClearRenderTarget(-1);
        _host.PresentFrame(_vm.Gfx);
        BackbufferPublicationPolicy clearedPolicy =
            _host.SnapshotBackbufferObjects(
                _vm.Gfx, _clock.NowMs, backbufferSnapshot);
        backbufferPreservationOk &=
            clearedPolicy == new BackbufferPublicationPolicy(false, false);
        ok &= launcherOk && sleepMinimumOk && inputTranslationOk && cp932WavMetadataOk
              && firstRiffBoundaryOk
              && bgmReplacementCancelsFade && bgmOneShotModeOk && bgmLoopModeOk
              && bgmStopReleaseOk && textEffectModesOk && fontCalibrationOk
              && immediateSurfaceTextOk
              && liveRetainedTextOk
              && historyRetainedTextOk
              && logicalCanvasOk && backbufferPreservationOk;
        if (ok) GD.Print($"SELFTEST OK: threaded host matches headless ({actual.Count} lines, full handling); " +
                         $"debug launcher catalog/UI smoke ({debugEntries.Count} packed scripts); " +
                         $"sleep-min=1ms; native-key-translation=ok; cp932-wav-info=ok; " +
                         $"first-riff-boundary=ok; " +
                         $"bgm-fade-replacement=ok; bgm-start-modes-stop=ok; " +
                         $"text-effect-modes=ok; font-calibration=ok; " +
                         $"immediate-surface-text={immediateSurfaceTextMode}; " +
                         $"live-adv-text={liveRetainedTextMode}; " +
                         $"history-text={historyRetainedTextMode}; " +
                         $"backbuffer-preservation=ok; " +
                         $"logical-canvas={_screenWidth}x{_screenHeight}; " +
                         $"window-request={_windowOptions.Width}x{_windowOptions.Height}");
        else GD.Print($"SELFTEST FAIL: threaded={actual.Count} vs headless={expected.Count}; " +
                      $"debug-launcher={launcherOk}; sleep-min={sleepMinimumOk}; " +
                      $"native-key-translation={inputTranslationOk}; cp932-wav-info={cp932WavMetadataOk}; " +
                      $"first-riff-boundary={firstRiffBoundaryOk}; " +
                      $"bgm-fade-replacement={bgmReplacementCancelsFade}; " +
                      $"bgm-one-shot={bgmOneShotModeOk}; bgm-loop={bgmLoopModeOk}; " +
                      $"bgm-stop-release={bgmStopReleaseOk}; " +
                      $"text-effect-modes={textEffectModesOk}; font-calibration={fontCalibrationOk}; " +
                      $"immediate-surface-text={immediateSurfaceTextOk}({immediateSurfaceTextMode}); " +
                      $"live-adv-text={liveRetainedTextOk}({liveRetainedTextMode}); " +
                      $"history-text={historyRetainedTextOk}({historyRetainedTextMode}); " +
                      $"backbuffer-preservation={backbufferPreservationOk}; " +
                      $"logical-canvas={logicalCanvasOk}({_screenWidth}x{_screenHeight}); " +
                      $"window-request={_windowOptions.Width}x{_windowOptions.Height}");
        GetTree().Quit(ok ? 0 : 1);
    }

    // A deterministic synthesized scene: show-text, wait-for-input (exercises the suspend plumbing), a
    // nested call-script into a synthetic subroutine (exercises call-script handling), shared globals.
    private static (Script, IScriptProvider) BuildSelfTestScene(OpcodeTable table)
    {
        (int, Operand[]) ShowText(int s) => (0x6e, new[] { new Operand(0, 0), new Operand(2, s) });
        (int, Operand[]) Wait() => (0x72, new[] { new Operand(0, 0) });
        (int, Operand[]) CallScript(long id) => (0x3, new[] { new Operand(0, id) });
        (int, Operand[]) MovGG(int d, int s) => (0x55, new[] { new Operand(3, d), new Operand(3, s) });
        (int, Operand[]) MovGI(int d, long v) => (0x55, new[] { new Operand(3, d), new Operand(0, v) });
        (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

        var callee = ScriptAssembler.Assemble(table, "SUBSCENE",
            new List<(int, Operand[])> { ShowText(0), MovGI(0x31, 42), Exit() }, new[] { "Sub" });
        var caller = ScriptAssembler.Assemble(table, "SELFTEST",
            new List<(int, Operand[])>
            {
                (0x70, new[]
                {
                    new Operand(0, 1), new Operand(0, 512), new Operand(0, 64),
                    new Operand(0, 0), new Operand(0, 0),
                }),
                (0x79, new[]
                {
                    new Operand(0, 1), new Operand(0, 1), new Operand(0, 1),
                }),
                (0x71, new[] { new Operand(0, 1) }),
                (0x1c1, new[]
                {
                    new Operand(0, 1), new Operand(0, 511), new Operand(0, 63),
                }),
                (0x213, new[]
                {
                    new Operand(0, 1), new Operand(0, 700000), new Operand(0, 64),
                }),
                ShowText(0),
                Wait(),
                ShowText(1),
                CallScript(5),
                MovGG(0x30, 0x31),
                Exit(),
            },
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

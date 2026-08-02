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
    private ImageTexture? _ageCursorTexture;
    private Label _status = null!;
    private Label _locatorHud = null!;
    private VirtualMachine _vm = null!;
    private GodotAdvHost _host = null!;
    private IDisposable? _glyphRasterizerOwner;
    private FullwidthTextEditorDialog? _fullwidthTextEditor;
    private Sys4ScriptProvider? _scripts;
    private DebugSceneLauncher? _debugSceneLauncher;
    private IReadOnlyList<DebugSceneEntry> _debugSceneEntries = System.Array.Empty<DebugSceneEntry>();
    private readonly Age.Engine.Hosting.FrameClock _clock = new();
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

        var table = HimegariRuntimeMetadata.LoadOpcodeTable();
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
            pageMapPath = DefaultPageMapPath(scene);
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
                if (_selftest) RunSelfTestAndQuitOnFailure();
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

    private static string DefaultPageMapPath(string scene)
    {
        string fileName = $"page-map-{scene.ToUpperInvariant()}.jsonl";
#if TOOLS
        // Preserve the established workspace handoff for editor/development runs.
        return System.IO.Path.Combine(Paths.Build, fileName);
#else
        // Exports have no repository and may be installed read-only. Keep diagnostics with the
        // profile-owned Godot data instead of probing for an age-reimpl ancestor.
        return System.IO.Path.Combine(
            ProjectSettings.GlobalizePath("user://"), "diagnostics", "page-maps", fileName);
#endif
    }

}

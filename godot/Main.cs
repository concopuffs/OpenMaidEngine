using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
#if OME_WINDOWS_GDI
using Ome.Engine.Text.Windows;
#endif
using Ome.Engine.Vm;
using Ome.Mods.Launch;
using Ome.Scripting.Lua;
using Script = Ome.Engine.Model.Script;   // disambiguate from Godot.Script

public partial class Main : Godot.Control
{
    private int _screenWidth = Sys4LogicalCanvas.DefaultWidth;
    private int _screenHeight = Sys4LogicalCanvas.DefaultHeight;
    private WindowLaunchOptions _windowOptions;
    private Sys4AssetCatalog _catalog = null!;
    private SelectedGameProfile _selectedProfile = null!;
    // The engine build the profile runs (Ome.Engine.Vm/EngineBuilds.cs): its rules go to the VM, the host and the
    // stores below, and its ABI picks the opcode table. Nothing past this file reads its id.
    private EngineBuild _engineBuild = null!;
    // The opcode table of the build's ABI that the VM runs; the self-test's headless twin runs it too.
    private OpcodeTable _opcodeTable = null!;
    private ResolvedGamePersistence _profilePersistence = null!;
    private IAssetStore _assetStore = null!;
    private LanguageAssetStore? _languageAssets;
    private Label _status = null!;
    private Label _fpsHud = null!;
    private double _fpsHudUpdateSeconds;
    private VirtualMachine _vm = null!;
    // The input and waits native (milestone 11): every wait, and the engine thread's loop. This thread reaches the
    // engine only through it: queued input, a frame turn each frame, and requests (InputAndWaits.Turns.cs).
    private InputAndWaits _waits = null!;
    private Audio _audio = null!;   // the audio native (engine layering milestone 6): the options panel's requests set its mixer
    private Graphics _graphics = null!;   // the graphics native (milestone 7): surfaces, publication, frame lowering
    private Movies? _movies;   // the movie native (milestone 8): this node supplies its decoder and its sound streams
    // The save and settings native (milestone 9): the shared profile, the numbered saves and SYS4REG.INI, which the shell
    // also reads and writes (SettingsStore).
    private SavesAndSettings? _saves;
    // The shell-services native (milestone 9): the system-menu fields, the menu requests, the dialogs this node shows and
    // the screen mode this node's window shows.
    private ShellServices? _shell;
    // The text native (milestone 10): the layouts, the reveal, the wait indicator and the message settings, drawn with
    // this node's glyph rasterizers.
    private TextManager _text = null!;
    private double _audioOutputLatencySeconds;
    private IDisposable? _glyphRasterizerOwner;
    // The Unicode rasterizer a language draws with when the gameplay one is GDI (localization spec §7.4).
    private IDisposable? _presentedRasterizerOwner;
    private Sys4ScriptProvider? _scripts;
    // Real time, as native's tick reads timeGetTime: a service wait ends with its effect, not at the next frame.
    private readonly Ome.Engine.Hosting.FrameClock _clock = Ome.Engine.Hosting.FrameClock.StartRealTime();
    private GodotTraceSink _trace = null!;

    private void ReportAssetResolutionWarning(string message)
    {
        var snapshot = _trace?.Snapshot();
        if (snapshot != null)
            message += $" [script={snapshot.CurrentScript}@0x{snapshot.CurrentOffset:x}, " +
                $"opcode=0x{snapshot.CurrentOpcode:x}, stack={string.Join(" > ", snapshot.CallStack)}]";
        GD.PushWarning(message);
    }

    private Ome.Engine.Diagnostics.HistogramTraceSink? _hist;   // --trace-histogram: profile the real run
    private string? _histFile;
    private Ome.Engine.Model.OpcodeTable? _table;
    private bool _histDumped;
    private volatile bool _done;
    private bool _ended;
    private System.Threading.Thread? _engineThread;
    private Exception? _vmFailure;
    /// <summary>The engine thread's stack (Lua scripting plan §3.3): set here, so the Lua nesting cap does not depend on
    /// what the host gives a pool thread (8 MiB under Godot on Windows, which this keeps).</summary>
    private const int EngineThreadStackBytes = 8 << 20;
    // The Lua runtime, created only when a mod ships Lua or the driver's lua step runs a chunk (Lua scripting spec §5.1);
    // the scripting native over it, and how a handler's error is taken (--lua-errors).
    private readonly LazyModRuntime _luaRuntime = LuaModRuntime.Lazy();
    private Scripting? _scripting;
    private int _luaNoticesSeen;
    private bool _selftest;
    // The resolver's normalized arguments (launcher spec §5.2): the command line with a profile file's equivalents.
    private string[] _launchArguments = [];
    // The data root's run lock (launcher spec §5.5), held for the process's life: only a held lock means "running".
    private RunLock? _runLock;
    private string? _shotPath;                 // --shot <png>: capture a page then quit (dev tool)
    private int _shotPage = 1;                  // --shot-page <n>: which page to capture (default 1)
    private int _pageNoticesSeen, _pagesSeen;   // the waits native's page notices the status line and HUD took
    private int _shotSettle;
    private int _shotSettleTarget = 3;          // --shot-settle <frames>: settle N frames before grabbing (to
                                                // capture mid-tween — the anim clock keeps running while parked)
    private bool _shotDone;
    private string? _seqDir;                    // --shot-sequence <dir>: dump one PNG per frame (verify paced anim)
    private int _seqFrames = 180;               // --frames <n>: how many frames to dump (default ~3s @60fps)
    private int _seqIdx;
    private string? _timelineLogPath;            // --timeline-log <jsonl>: synchronized VM/host/compositor evidence
    private TimelineLog? _timeline;
    private int _timelineFrame;
    private string? _perfLogPath;                // --perf-log <csv>: low-overhead frame/compositor timings + work
    private PerformanceFrameLog? _perf;

    /// <summary>The directory the engine was started from, which relative paths resolve against.</summary>
    private static string LaunchDirectory()
    {
        string workingDirectory = System.IO.Directory.GetCurrentDirectory();
        if (!System.OperatingSystem.IsWindows())
        {
            // Godot can chdir to the project before managed startup. Unix shells preserve the launch
            // directory in PWD, which keeps terminal-launched exports faithful to their caller.
            string? inheritedWorkingDirectory = System.Environment.GetEnvironmentVariable("PWD");
            if (!string.IsNullOrWhiteSpace(inheritedWorkingDirectory)
                && System.IO.Directory.Exists(inheritedWorkingDirectory))
                workingDirectory = inheritedWorkingDirectory;
        }
        return workingDirectory;
    }

    public override void _Ready()
    {
        string[] commandLine = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(commandLine, "--package-smoke") >= 0)
        {
            RunPackageSmoke();
            return;
        }
        string workingDirectory = LaunchDirectory();
        // Launcher spec §5.6: no argument, no game here, and the launcher beside us: start it instead.
        if (LauncherHandOver.Decide(commandLine, OS.GetExecutablePath(), workingDirectory, System.OperatingSystem.IsWindows())
            is { } launcher)
        {
            int process = OS.CreateProcess(launcher, []);
            if (process < 0)
            {
                GD.PushError($"[startup] no game was given or found here, and {launcher} did not start");
                GetTree().Quit(2);
                return;
            }
            GD.Print($"[startup] no game was given or found here: started {launcher}");
            GetTree().Quit(0);
            return;
        }
        InitializeExitPolicy();
        // Every reader below takes the resolver's list: the command line, and a data root's profile.toml mapped to the
        // flags it stands for (launcher spec §5.2).
        string[] userArgs;
        ResolvedRun run;
        GameRootSelection gameRoot;
        SelectedGameProfile selectedProfile;
        EngineBuild engineBuild;
        ResolvedGamePersistence profilePersistence;
        Sys4AssetCatalog catalog;
        AssetLaunchOptions assetOptions;
        try
        {
            // This machine's own state (approved network locations) lives in Godot's user folder.
            run = RunResolver.Resolve(commandLine, workingDirectory,
                                      machineFolder: ProjectSettings.GlobalizePath("user://"));
            userArgs = run.Arguments.ToArray();
            _launchArguments = userArgs;
            gameRoot = GameRootSelection.Resolve(
                userArgs, OS.GetExecutablePath(), workingDirectory);
            assetOptions = AssetLaunchOptions.Resolve(userArgs, gameRoot.Root);
            GameCatalogIdentity detectedIdentity =
                GameCatalogIdentity.ReadSys4IniHeader(gameRoot.Sys4IniPath);
            selectedProfile = GameProfileSelection.Resolve(userArgs, detectedIdentity);
            engineBuild = EngineBuilds.ForProfile(selectedProfile.Profile);
            catalog = Sys4AssetCatalog.Load(gameRoot.Sys4IniPath);
            profilePersistence = ResolvedGamePersistence.Resolve(
                selectedProfile,
                engineBuild.Rules.Saves,
                catalog.StartupSettings,
                ProjectSettings.GlobalizePath("user://"),
                ArgumentValue(userArgs, "--data-root"));   // --data-root <dir>: driver sandboxes
        }
        catch (System.Exception error) when (
            error is RunResolutionException or System.ArgumentException or System.IO.IOException
                or System.UnauthorizedAccessException)
        {
            GD.PushError($"[startup] {error.Message}");
            GetTree().Quit(2);
            return;
        }
        // Launcher spec §5.5: the root's run lock, before anything writes into it (LinkMods is the first writer), on
        // every data root, the default one included. A refused start stops having written nothing.
        if (!RunLock.TryAcquire(profilePersistence.ProfileRoot, markStart: true, out _runLock,
                                out RunLockRefusal? lockRefusal))
        {
            GD.PushError($"[startup] {lockRefusal.Message}");
            GetTree().Quit(2);
            return;
        }
        if (_runLock.Warning != null) GD.PushWarning($"[startup] {_runLock.Warning}");
        _catalog = catalog;
        _selectedProfile = selectedProfile;
        _engineBuild = engineBuild;
        _profilePersistence = profilePersistence;
        _assetStore = new Sys4AssetStore(catalog, gameRoot.Root, assetOptions.LooseRoots.ToArray());
        // The install's own scripts, before the mod layer mounts: site entries name their fingerprints.
        Sys4AssetCatalog baseCatalog = catalog;
        IAssetStore baseStore = _assetStore;
        try
        {
            if (!LinkMods(userArgs, workingDirectory, selectedProfile, profilePersistence, run, gameRoot.Sys4IniPath,
                          catalog, _assetStore, _luaRuntime, out _modLayer, out _unpackedLua))
            {
                GetTree().Quit(2);
                return;
            }
            if (_modLayer != null)
            {
                // Spec §6.5: the linked layer's loose files outrank --overlay-root roots and the game root; its
                // catalogs mount after the install's own append catalogs.
                catalog = Sys4AssetCatalog.Load(gameRoot.Sys4IniPath, [_modLayer.CatalogDirectory]);
                _catalog = catalog;
                _assetStore = new Sys4AssetStore(catalog, gameRoot.Root,
                    [_modLayer.LooseRoot, .. assetOptions.LooseRoots]);
                // Localization spec §12: the stack's language assets stand above its loose files while their
                // language is selected.
                if (_modLayer.LanguageAssets is { Count: > 0 } languageAssets)
                {
                    _languageAssets = new LanguageAssetStore(_assetStore, languageAssets);
                    _assetStore = _languageAssets;
                    GD.Print($"[assets] language assets for {languageAssets.Count} names in "
                             + string.Join(", ", languageAssets.Languages));
                    // The first read of each language file, so a modder sees which version the game loaded.
                    var served = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(
                        System.StringComparer.OrdinalIgnoreCase);
                    _languageAssets.Served += (entry, path) =>
                    {
                        if (served.TryAdd(path, 0)) GD.Print($"[assets] {entry.Name}: {path}");
                    };
                }
            }
        }
        catch (System.Exception error) when (
            error is System.ArgumentException or System.IO.IOException or System.UnauthorizedAccessException
                or System.IO.InvalidDataException)
        {
            GD.PushError($"[startup] mods: {error.Message}");
            GetTree().Quit(2);
            return;
        }
        GD.Print($"[profile] id={selectedProfile.Profile.Id} title=\"{selectedProfile.Profile.DisplayTitle}\" " +
                 $"selection={selectedProfile.SourceName} identity-match={selectedProfile.IdentityMatched} " +
                 $"frontend={selectedProfile.Profile.SysFrontendId} " +
                 $"build={engineBuild.Id} abi={engineBuild.AbiId} probe={selectedProfile.ProbeMode}");
        GD.Print($"[profile] catalog={selectedProfile.DetectedIdentity.Display} " +
                 $"game-root={gameRoot.Root} root-source={gameRoot.SourceName}");
        GD.Print($"[input] mouse clicks trigger on {(catalog.StartupSettings.ClickOnUp ? "release" : "press")} (SYS4INI CLICKONUP)");
        GD.Print($"[assets] overlays=" +
                 (assetOptions.OverlayRoots.Count == 0
                    ? "<none>"
                    : string.Join(" -> ", assetOptions.OverlayRoots)) +
                 $" bmp-as-agf={(assetOptions.AllowBmpAsAgf ? "enabled" : "disabled")}");
        if (selectedProfile.MismatchDiagnostic is { } mismatch)
            GD.PushWarning($"[profile] {mismatch}");
        if (selectedProfile.SupportDiagnostic is { } notPlayable)
            GD.PushWarning($"[profile] {notPlayable}");
        if (!profilePersistence.WritesEnabled)
            GD.PushWarning($"[profile] persistence is read-only: {profilePersistence.ReadOnlyReason}");
        GD.Print($"[profile] data-root={profilePersistence.ProfileRoot} " +
                 $"persistence={(profilePersistence.WritesEnabled ? "read-write" : "read-only")}");
        if (run.Describe() is { } profileFileLine)
            GD.Print($"[profile-file] {profileFileLine}");
        foreach (string warning in run.Warnings)
            GD.PushWarning($"[profile-file] {warning}");

        // Resolve the selected game's logical canvas before any presentation allocation. The same catalog
        // instance is reused for scripts and assets later in startup.
        var logicalCanvas = catalog.LogicalCanvas;
        try
        {
            // OME.INI's port options (the language, the window size, the overlays) and the flags that override them.
            LoadPortOptions(userArgs);
            _windowOptions = WindowLaunchOptions.Resolve(userArgs, logicalCanvas, _portOptions.Effective);
            NoteWindowSizeFlags(logicalCanvas);
        }
        catch (System.ArgumentException error)
        {
            GD.PushError($"[startup] {error.Message}");
            GetTree().Quit(2);
            return;
        }
        _screenWidth = logicalCanvas.Width;
        _screenHeight = logicalCanvas.Height;
        // The windowed AGE's menu bar sits above the game's client area, so the viewport is the canvas plus the bar.
        LoadMenuBar(gameRoot.Root, catalog.StartupSettings, userArgs);
        InitScreenshots(userArgs, gameRoot.Root,
                        int.TryParse(catalog.StartupSettings.GetValueOrDefault("USEAPPDATAFOLDER"),
                                     out int useAppDataFolder) && useAppDataFolder == 1);
        Window rootWindow = GetTree().Root;
        DisplayServer.SetIcon(ShellAssets.Icon(64));   // the OME icon: the game's is inside its binaries
        rootWindow.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        rootWindow.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        rootWindow.ContentScaleSize = new Vector2I(_screenWidth, _screenHeight + _menuBarHeight);
        int windowBarHeight = (int)System.Math.Round(_menuBarHeight * (double)_windowOptions.Height / _screenHeight);
        if (rootWindow.Mode == Window.ModeEnum.Windowed)
            rootWindow.Size = new Vector2I(_windowOptions.Width, _windowOptions.Height + windowBarHeight);
        GD.Print($"[profile] SYS4INI logical canvas={_screenWidth}x{_screenHeight} menu-bar={_menuBarHeight} " +
                 $"requested window={_windowOptions.Width}x{_windowOptions.Height} " +
                 $"source={WindowSizeSource()} " +
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
        CreateMenuBar();
        Win32Dialog.GameClientOrigin = new Vector2I(0, _menuBarHeight);
        _gpuRenderer = new GpuRetainedRenderer(this);

        _status = new Label();
        AddChild(_status);
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _status.OffsetLeft = 40; _status.OffsetTop = -60;
        // A long line (a data file's notice names the file and the first problem's path) wraps upward from the bottom
        // edge rather than running off the window's right edge.
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.VerticalAlignment = VerticalAlignment.Bottom;
        _status.GrowVertical = GrowDirection.Begin;
        _locatorHudVisible = _portOptions.Effective.LocatorHud;
        _locatorHud = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _locatorHud.Position = new Vector2(8, 8);
        AddChild(_locatorHud);
        _fpsHud = new Label
        {
            Visible = _portOptions.Effective.FpsCounter,
            Text = "FPS --",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AddChild(_fpsHud);
        _fpsHud.SetAnchorsPreset(LayoutPreset.TopRight);
        _fpsHud.OffsetLeft = -180;
        _fpsHud.OffsetTop = 8;
        _fpsHud.OffsetRight = -8;
        _fpsHud.OffsetBottom = 36;
        _fpsHud.AddThemeColorOverride("font_outline_color", Colors.Black);
        _fpsHud.AddThemeConstantOverride("outline_size", 4);

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
                _fpsHud.AddThemeFontOverride("font", ff);
                break;
            }
            catch { /* fall back to the default font */ }
        }

        CreateAudioChannels();
        _audioOutputLatencySeconds = AudioServer.GetOutputLatency();

        _selftest = System.Array.IndexOf(userArgs, "--selftest") >= 0;
        bool nativeDebugMenu = System.Array.IndexOf(userArgs, "--native-debug-menu") >= 0;
        if (nativeDebugMenu)
            GD.Print("[debug] native exit requests disabled; post-0x1 bytecode may execute");
        string scene = System.IO.Path.GetFileNameWithoutExtension(
            selectedProfile.Profile.NaturalBootScript); // natural profile root; --scene picks a hosted scene
        var seeds = new List<(int Addr, long Val)>();   // --seed 0xADDR=VAL (repeatable) — initial global state
        double sleepScale = 1.0;                         // --sleep-scale <f>: scale explicit op-0xc8 holds
        double speed = 1.0;                              // --speed <f>: sleeps + retained presentation clocks
        long transitionClickMs = -1;                    // --transition-click-ms <n>: force active transitions after n virtual ms
        bool holdMessageSkip = false;                    // --hold-message-skip: hold native logical action 6 from boot
        string textBackend = "auto";                     // --text-backend auto|gdi|portable
        string? histFile = null;                         // --trace-histogram <file>: op/call-site execution counts of the REAL run
        string? pageMapPath = null;                      // --page-map <jsonl>: override default build/games/<game>/page-map-SCxxxx.jsonl
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
            if (userArgs[i] == "--sleep-scale" && i + 1 < userArgs.Length)
            {
                if (TryParseLaunchDouble(userArgs[i + 1], out double parsed)) sleepScale = parsed;
                else GD.PushWarning($"[startup] ignoring --sleep-scale '{userArgs[i + 1]}': not a number");
            }
            if (userArgs[i] == "--speed" && i + 1 < userArgs.Length)
            {
                if (TryParseLaunchDouble(userArgs[i + 1], out double parsed)) speed = parsed;
                else GD.PushWarning($"[startup] ignoring --speed '{userArgs[i + 1]}': not a number");
            }
            if (userArgs[i] == "--transition-click-ms" && i + 1 < userArgs.Length) long.TryParse(userArgs[i + 1], out transitionClickMs);
            if (userArgs[i] == "--hold-message-skip") holdMessageSkip = true;
            if (userArgs[i] == "--text-backend" && i + 1 < userArgs.Length)
                textBackend = userArgs[i + 1].ToLowerInvariant();
            if (userArgs[i] == "--trace-histogram" && i + 1 < userArgs.Length) histFile = userArgs[i + 1];
            if (userArgs[i] == "--page-map" && i + 1 < userArgs.Length) pageMapPath = userArgs[i + 1];
            if (userArgs[i] == "--seed" && i + 1 < userArgs.Length)
            {
                var kv = userArgs[i + 1].Split('=');
                if (kv.Length == 2 && TryParseLaunchInteger(kv[0], out long k) && k is >= 0 and <= int.MaxValue
                    && TryParseLaunchInteger(kv[1], out long v))
                    seeds.Add(((int)k, v));
                else
                    GD.PushWarning($"[startup] ignoring --seed '{userArgs[i + 1]}': expected ADDRESS=VALUE (decimal or 0x hex)");
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

        var table = OmeRuntimeMetadata.LoadOpcodeTable(engineBuild.AbiId);
        _opcodeTable = table;
        GD.Print($"[profile] opcode-layers={string.Join(",", table.ResolvedLayers)} " +
                 $"contracts={table.Count}");
        INativeDatStore? nativeSaveStore = null;
        Sys4RegIniStore? settingsStore = null;
        var sharedProfile = new SharedProfile();
        _audioMixerSettings = new AudioMixerSettings();
        if (!_selftest && profilePersistence.WritesEnabled)
        {
            nativeSaveStore = StampSaves(profilePersistence.CreateNativeDatStore()
                ?? throw new InvalidOperationException(
                    "write-enabled profile did not construct a native persistence store"), profilePersistence);
            foreach (SharedProfileLoadFailure failure in sharedProfile.LoadRecovering(nativeSaveStore))
            {
                // Keep the damaged files: the next shared-profile write would otherwise replace them.
                string asideNote;
                try
                {
                    IReadOnlyList<string> aside = nativeSaveStore.SetAsideUnreadable(failure.File);
                    asideNote = aside.Count == 0 ? "" : $"; kept as {string.Join(", ", aside)}";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    asideNote = $"; could not set it aside ({error.Message})";
                }
                GD.PushWarning($"[persistence] {failure.File} profile file unreadable, starting it empty: " +
                               $"{failure.Error.Message}{asideNote}");
            }
            try
            {
                settingsStore = Sys4RegIniStore.ForPath(
                    catalog.StartupSettings, profilePersistence.NativePaths.Sys4RegIniPath,
                    engineBuild.Rules.Settings);
                _audioMixerSettings = settingsStore.Load();
                // SYS4REG.INI holds 既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ; the shared profile carries it while the game runs.
                sharedProfile.ReadMessageSkipEnabled = settingsStore.LoadReadTextSkip();
                GD.Print($"[settings] native engine options={settingsStore.FilePath}");
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                GD.PushWarning($"[settings] audio mixer load failed; using native defaults: {error.Message}");
            }
            _audioMixerSettings.Changed += PersistAudioMixerSettings;
        }
        // The save and settings native and the shell-services native (engine layering milestone 9). A self-test writes
        // no shared profile at shutdown (set:NoSaveDat). This node is the shell's window and shows its dialogs.
        _saves = new SavesAndSettings(sharedProfile, nativeSaveStore, settingsStore,
                                      noSaveDat: GodotVmOptions.NoSaveDat(_selftest));
        _shell = new ShellServices(settingsStore, window: this, dialogs: this, print: message => GD.Print(message));
        InitScreenMode(userArgs);

        // Full op handling everywhere: the provider lets call-script load & run subroutines. Selftest
        // runs a SYNTHESIZED scene (not a real scene in a crippled mode) so its output is deterministic.
        Script script;
        IScriptProvider provider;
        Sys4ScriptProvider? scripts = null;
        IAssetStore? trackedAssetStore = null;
        // A direct-scene run (--scene other than the natural root) enters as the F4 launcher does: the profile's
        // root (SYSTEM4) hosts the scene after its own boot setup, and the coordinator (TITLE) returns the launcher's
        // writes. A profile without that coordinator runs the scene as the root, with no setup at all.
        DebugSceneLaunchPolicy? hostPolicy = !_selftest
            && !scene.Equals(
                System.IO.Path.GetFileNameWithoutExtension(selectedProfile.Profile.NaturalBootScript),
                System.StringComparison.OrdinalIgnoreCase)
            ? selectedProfile.Profile.DebugSceneLaunch
            : null;
        Script? hostedScene = null;
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
            if (hostPolicy != null)
            {
                if (script.Name.Equals(hostPolicy.CoordinatorScript, System.StringComparison.OrdinalIgnoreCase))
                {
                    GD.PushError($"[startup] --scene {scene}: {hostPolicy.CoordinatorScript} is the launcher's "
                                 + "coordinator; run the natural boot instead");
                    GetTree().Quit(2);
                    return;
                }
                hostedScene = script;
                script = scripts.RequireByName(hostPolicy.RootScript);
                GD.Print($"[startup] {hostPolicy.RootScript} hosts {hostedScene.Name} "
                         + $"(0x{hostedScene.PackedId:x8}) as the F4 launcher does; LOGO and OP are skipped");
            }
        }
        _scripts = scripts;
        if (_timelineLogPath != null) _timeline = new TimelineLog(_timelineLogPath);
        if (!_selftest && pageMapPath == null)
            pageMapPath = DefaultPageMapPath(scene);
        _locator = PageLocatorState.Open(scene, _selftest ? null : pageMapPath, out string? pageMapWarning);
        if (pageMapWarning != null) GD.PushWarning($"[locator] {pageMapWarning}");
        _locatorHud.Visible = _locatorHudVisible;
        var resources = scripts != null
            ? new ResourceMap(scripts.Catalog, trackedAssetStore, ReportAssetResolutionWarning,
                              assetOptions.AllowBmpAsAgf)
            : new ResourceMap(catalog, _assetStore, ReportAssetResolutionWarning,
                              assetOptions.AllowBmpAsAgf);
        IGlyphMaskRasterizer? surfaceTextRasterizer = null;
        PortableTextRenderingPolicy? portableTextPolicy = null;
        string portablePolicyResource =
            selectedProfile.Profile.MetadataReferences.TryGetValue(
                "portableTextRendering", out string? configuredPolicy)
            ? configuredPolicy
            : PortableTextRenderingPolicy.JapaneseFallbackResourcePath;
        string? exactUnavailable = null;
        // The installed fonts read from their own files: the faces portable text draws, and the installed-font list
        // where GDI does not give it. Read once, when first needed.
        var installedFonts = new Lazy<InstalledFontIndex>(ScanInstalledFonts,
                                                          System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        // Read from startup on a worker thread, so the first glyph or font-list query does not wait for the files.
        _ = Task.Run(() => installedFonts.Value);
        try
        {
#if OME_WINDOWS_GDI
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
                portableTextPolicy = PortableTextRenderingPolicy.Load(portablePolicyResource);
                var portable =
                    new GodotTextServerGlyphMaskRasterizer(portableTextPolicy, () => installedFonts.Value);
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
        // The font the game ships in its catalog (AGE 4.38D registers it for its own process at start-up): every
        // rasterizer draws its face from the file.
        GameFont? gameFont = GameFont.Load(_catalog, _assetStore, message => GD.PushWarning($"[text] {message}"));
        if (gameFont != null)
        {
            bool added = false;
            try
            {
                added = gameFont.AddTo(surfaceTextRasterizer);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException)
            {
                GD.PushWarning($"[text] game font {gameFont.FaceName} ({gameFont.FileName}): {error.Message}");
            }
            GD.Print($"[text] game font {gameFont.FaceName} ({gameFont.FileName}, {gameFont.Data.Length} bytes)" +
                     (added ? "" : " is not used: the text backend takes no font files"));
        }
        // The skip and auto native the host reads and the VM's handlers call (engine layering milestone 5), and the audio
        // native over this node's players with the engine tick that runs it (milestone 6). The audio native applies the
        // mixer settings now and on every change; their Changed event persists them.
        SkipAndAuto skipAndAuto = SkipAndAuto.WithSystemClock(GodotVmOptions.CreateSkipAndAuto(_catalog.StartupSettings));
        _audio = new Audio(this, new ResourceAudioAssets(resources, message => GD.Print(message)), _clock, skipAndAuto,
                           _audioMixerSettings, SettingsStore, _timeline);
        var tick = new EngineTick(_audio, _timeline);
        // The graphics native (milestone 7) owns the surfaces and what each frame publishes; the movie native and the
        // host's text part draw into it, and the host's waits block on it. The movie native (milestone 8) plays movies
        // with the FFmpeg decoder, their sound through this node's stream players, and runs in _Process.
        _graphics = new Graphics(resources, _clock, logicalCanvas, _timeline, message => GD.Print(message))
        {
            TraceOps = _gfxLogPath != null,
        };
        _movies = new Movies(_graphics, new ResourceMovieAssets(resources), new FfmpegMovieDecoderFactory(),
                             new GodotAudioStreamDevice(this, _audioOutputLatencySeconds),
                             _catalog.StartupSettings.DependMovieSound, timeline: _timeline,
                             print: message => GD.Print(message));
        // The text native (milestone 10) draws with the selected gameplay rasterizer, and with the Unicode one while a
        // language is active; it reads its message settings from SYS4REG.INI.
        int glyphCacheCapacity = portableTextPolicy?.GlyphMaskCacheCapacity ?? 2048;
        _text = new TextManager(
            _graphics, skipAndAuto, _clock,
            new TextDevices(
                surfaceTextRasterizer, engineBuild.Rules.Text.GlyphCoverage,
                GameplayCacheCapacity: glyphCacheCapacity,
                Presented: () =>
                {
                    PortableTextRenderingPolicy policy = PortableTextRenderingPolicy.Load(portablePolicyResource);
                    var portable = new GodotTextServerGlyphMaskRasterizer(policy, () => installedFonts.Value);
                    gameFont?.AddTo(portable);
                    _presentedRasterizerOwner = portable;
                    GD.Print($"[text] presented text uses {portable.BackendInfo.Id}: {portable.BackendInfo.Detail}");
                    return portable;
                },
                PresentedCacheCapacity: glyphCacheCapacity,
                FontFamilies: InstalledFontFamilySource(surfaceTextRasterizer, _catalog.StartupSettings, installedFonts),
                FontMetrics: surfaceTextRasterizer as IFontMetricsProbe),
            engineBuild.Rules.Text.Ruby.For(_catalog.StartupSettings, SettingsStore?.RubyShiftMode),
            blankExtentMode: _catalog.StartupSettings.BlankExtentMode == 1,
            settings: SettingsStore, locator: _locator, timeline: _timeline,
            lineEnd: engineBuild.Rules.Text.LineEnd.For(_catalog.StartupSettings),
            extendedGlyphFace: engineBuild.Rules.Text.ExtendedGlyphFace,
            aspectCalibration: engineBuild.Rules.Text.AspectCalibration,
            publisherGrowsGlyphCells: engineBuild.Rules.Text.PublisherGrowsGlyphCells);
        // The input and waits native (milestone 11) runs every wait the VM blocks in, over the natives above; the lifted
        // host keeps what is left (the cursor, the Auto delays' seed and the message settings' shutdown write).
        _waits = new InputAndWaits(skipAndAuto, _audio, tick, _graphics, _movies, _shell, _text, _clock, _locator,
                                   _timeline, synchronizeExplicitPresentation: !_selftest, settings: SettingsStore,
                                   cursors: resources, cursor: this)
        {
            SleepScale = sleepScale,
            ClickOnUp = _catalog.StartupSettings.ClickOnUp,
        };
        // The Auto delays come from SYS4REG.INI's [message] (the save native writes them back at shutdown).
        if (_saves.Settings is { } messageSettings) skipAndAuto.ReadSettings(messageSettings);
        if (!_selftest && !PrepareLanguage(userArgs, baseCatalog, baseStore, table))
        {
            GetTree().Quit(2);
            return;
        }
        _trace = new GodotTraceSink(_locator, _timeline);
        if (_perfLogPath != null) _perf = new PerformanceFrameLog(_perfLogPath);
        // --trace-histogram: aggregate op/call-site execution counts of the REAL Godot run (headless flow
        // diverges — wait-for-input is a no-op there — so this is the only way to profile the live path).
        _table = table;
        _histFile = histFile;
        Ome.Engine.Diagnostics.ITraceSink sink = _trace;
        if (histFile != null) { _hist = new Ome.Engine.Diagnostics.HistogramTraceSink();
                                 sink = new Ome.Engine.Diagnostics.CompositeTraceSink(_trace, _hist); }
        VmOptions vmOptions = GodotVmOptions.Create(_selftest, nativeDebugMenu, _catalog.StartupSettings) with
        {
            ProfileId = selectedProfile.Profile.Id,
            ProbeMode = selectedProfile.ProbeMode,
        };
        // The scripting native (Lua scripting spec §14.1) is in every run; it creates the Lua runtime only when a mod
        // ships Lua or the driver's lua step is used. The console runs after the waits native has run the front end's
        // requests, so a chunk runs while the game waits.
        if (!TryParseLuaErrors(userArgs, out LuaErrorPolicy luaErrors))
        {
            GetTree().Quit(2);
            return;
        }
        // --lua-trace (Lua scripting spec §12): a line per handler call in diagnostics/lua-trace.jsonl.
        bool luaTrace = userArgs.Contains("--lua-trace");
        // Script memory draws op 0x60 and Lua from the graphics native's generator, as the title's leaves do.
        _scripting = new Scripting(new ScriptMemory(vmOptions.NativeStringCodePage, _graphics.GameRandom), new ScriptingSetup
        {
            Runtime = _luaRuntime,
            Stack = _modLayer?.LuaStack ?? ModRuntimeStack.Empty,
            Plan = _modLayer?.HookPlan ?? HookPlan.Empty,
            Sites = _modLayer?.ScriptSites ?? UnchangedScriptSites.Instance,
            Errors = luaErrors,
            Trace = luaTrace,
            DiagnosticsDirectory = profilePersistence.DiagnosticsDirectory,
            StampedMods = StampedMods,
            ConsoleLayout = () => Ome.Mods.Packages.ShippedPackages.BaseLayoutOf(selectedProfile.Profile.Id),
        });
        _waits.ServiceAfterRequests(_scripting.ServiceRequests);
        if (_modLayer?.LuaStack is { HasLua: true } luaStack)
        {
            // A base mod with no lua/ of its own is in the stack for its layout; it ships no Lua.
            var shipping = luaStack.Mods.Where(mod => mod.Files.Count > 0).Select(mod => mod.Id).ToList();
            var layouts = luaStack.Mods.Where(mod => mod.Files.Count == 0).Select(mod => mod.Id).ToList();
            GD.Print($"[lua] {shipping.Count} mods ship Lua ({string.Join(", ", shipping)})"
                     + (layouts.Count > 0 ? $", with the layout of {string.Join(", ", layouts)}" : "")
                     + $"; {luaStack.Handlers.Count} handlers; errors {luaErrors.ToString().ToLowerInvariant()}"
                     + (luaStack.Mods.Any(mod => mod.LuaDev)
                         ? "; --lua-dev for " + string.Join(", ", luaStack.Mods.Where(mod => mod.LuaDev).Select(mod => mod.Id))
                         : "")
                     + (luaTrace ? $"; tracing to diagnostics/{Scripting.TraceFile}" : ""));
        }
        _vm = new VirtualMachine(script, table, _waits, vmOptions,
            provider, sink,
            saves: _saves,
            shell: _shell,
            build: engineBuild,
            skipAndAuto: skipAndAuto,
            audio: _audio,
            tick: tick,
            graphics: _graphics,
            movies: _movies,
            text: _text,
            scripting: _scripting);
        _reference = new ReferenceRenderer(logicalCanvas) { Perf = _perf };
        _gpuRenderer.Perf = _perf;
        _decisionLog = new GfxDecisionLog(_gfxLogPath, _timeline);
        if (scripts != null && selectedProfile.Profile.DebugSceneLaunch != null)
        {
            _debugSceneEntries = DebugSceneCatalog.Build(scripts.Catalog);
            _debugSceneLauncher = new DebugSceneLauncher();
            _debugSceneLauncher.LaunchRequested += LaunchDebugScene;
            AddChild(_debugSceneLauncher);
        }
        if (hostedScene != null)
        {
            var coordinatorWrites = hostPolicy!.CoordinatorWrites
                .ToDictionary(seed => seed.Address, seed => seed.Value);
            coordinatorWrites[hostPolicy.PackedScriptIdAddress] = hostedScene.PackedId;
            // --seed applies at the handoff, so it overrides the state SYSTEM4's boot setup wrote.
            foreach (var (addr, val) in seeds) coordinatorWrites[addr] = val;
            _vm.HostScene(new HostedSceneEntry(
                hostPolicy.CoordinatorScript, hostedScene.PackedId, coordinatorWrites));
        }
        else
            foreach (var (addr, val) in seeds) _vm.SeedGlobal(addr, val);   // --seed: initial global state
        if (holdMessageSkip)
            _waits.HoldKeyFromLaunch(0x11); // Ctrl; bindings resolve it to logical action 6.
        if (!ApplyGameLanguage(_portOptions.Effective.Language, startup: true))
        {
            GetTree().Quit(2);
            return;
        }
        // The engine thread (InputAndWaits.Turns.cs): the interpreter, its waits and every native run on it, on a stack
        // of a known size (Lua scripting plan §3.3).
        _engineThread = new System.Threading.Thread(() =>
        {
            try { _waits.RunEngine(() => _vm.Run()); }
            catch (Exception error) { _vmFailure = error; }
            finally { _done = true; }
        }, EngineThreadStackBytes)
        {
            Name = "engine",
            IsBackground = true,
        };
        _engineThread.Start();
        StartLuaReload(userArgs);   // --lua-reload (Main.LuaReload.cs)
        // --control [--control-file <json>]: the game-driving protocol (Main.Drive.cs).
        if (!_selftest && System.Array.IndexOf(userArgs, "--control") >= 0
            && !StartDriveServer(ArgumentValue(userArgs, "--control-file")))
        {
            GetTree().Quit(2);
            return;
        }

        if (_selftest)
            _ = Task.Run(async () => { while (!_done) { if (_waits.IsWaiting) _waits.SignalInput(); await Task.Delay(1); } });
        // --shot: auto-advance up to (but not past) the target page, then _Process captures + quits.
        if (_shotPath != null)
            _ = Task.Run(async () => { while (!_done) { if (_waits.IsWaiting && _waits.Pages < _shotPage) _waits.SignalInput(); await Task.Delay(1); } });
        // --shot-sequence: auto-advance past every input-cancellable wait, including modal movies,
        // so a bounded capture can reach the requested natural-boot presentation slice.
        if (_seqDir != null)
            _ = Task.Run(async () =>
            {
                while (!_done)
                {
                    if (_waits.IsWaiting || _waits.IsModalMovieWaiting) _waits.SignalInput();
                    await Task.Delay(1);
                }
            });
        if (transitionClickMs >= 0)
            _ = Task.Run(async () =>
            {
                while (!_done)
                {
                    if (_waits.IsTransitionWaiting && _waits.TransitionStartedAtMs >= 0 &&
                        _clock.NowMs - _waits.TransitionStartedAtMs >= transitionClickMs)
                        _waits.SignalInput();
                    await Task.Delay(1);
                }
            });
    }

    public override void _Process(double delta)
    {
        PumpDriveServer();
        FlushTextMisses(delta);
        ProcessScreenMode();
        ServicePageNotices();
        ServiceLuaNotices();
        if (_fpsHud != null && _fpsHud.Visible)
        {
            _fpsHudUpdateSeconds += delta;
            if (_fpsHudUpdateSeconds >= 0.25)
            {
                _fpsHudUpdateSeconds %= 0.25;
                double fps = Godot.Engine.GetFramesPerSecond();
                _fpsHud.Text = fps > 0
                    ? $"FPS {fps:0}  ·  {1000.0 / fps:0.0} ms"
                    : "FPS --";
            }
        }
        _timelineFrame++;
        _timeline?.SetFrame(_timelineFrame, _clock.NowMs);
        var perf = _perf;
        var step = perf != null ? _trace.LatestStep : null;
        perf?.BeginFrame(_timelineFrame, _clock.NowMs, delta,
                         step?.Script ?? "<startup>", step?.Offset ?? -1, step?.Opcode ?? -1);
        try
        {
            long phase = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            ReportEndedAudioChannels();   // before the turn, so the engine thread sees them
            _movies?.ServiceFrame(_timelineFrame);   // its frames and completions wait for the engine thread
            perf?.RecordMovies(PerformanceFrameLog.Timestamp() - phase);

            // The frame turn (InputAndWaits.Turns.cs): the engine thread answers at its next instruction boundary or
            // wait, presents if that is where native would render (native publishes retained mutations only at its
            // present and service boundaries) and lowers the frame, and this thread draws it. The self-test presents
            // nothing and only wakes the waits.
            phase = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            if (_selftest || _vm == null || _waits == null)
            {
                _waits?.PulseFrame();
                perf?.RecordPulse(PerformanceFrameLog.Timestamp() - phase);
                perf?.RecordPresentationReasons(0);
            }
            else
            {
                var decisions = _decisionLog.NextFrame();
                var frame = _waits.RunFrameTurn(perf, decisions,
                                                perf != null ? (_presentationCoordinateNote ??= NotePresentationCoordinate) : null,
                                                FrameTurnTimeoutMs, out bool late);
                perf?.RecordPulse(PerformanceFrameLog.Timestamp() - phase);
                if (frame != null) DrawFrame(frame, late);
            }

            // --shot-sequence: dump one PNG per frame across the opening so a time-based (paced) effect can be
            // verified as distinct frames, not just the final state. Captures after Recomposite; quits when full.
            if (_seqDir != null && _seqIdx < _seqFrames && !_done)
            {
                System.IO.Directory.CreateDirectory(_seqDir);
                // Headless has no rendered viewport texture (GetImage() is null). Still advance/count/quit so the
                // real-run trace-histogram can profile the live path without a display; only the PNG grab is skipped.
                var fimg = CaptureGameImage();
                fimg?.SavePng($"{_seqDir}/frame_{_seqIdx:0000}.png");
                _seqIdx++;
                if (_seqIdx >= _seqFrames) { GD.Print($"SEQ saved {_seqIdx} frames -> {_seqDir}"); GetTree().Quit(0); }
                return;
            }
            // --shot: once the target page is composed and parked at wait-for-input, settle a few frames then grab it.
            if (_shotPath != null && !_shotDone && _waits != null && (_waits.Pages >= _shotPage && _waits.IsWaiting || _done))
            {
                if (++_shotSettle >= _shotSettleTarget)
                {
                    _shotDone = true;
                    var img = CaptureGameImage();
                    img?.SavePng(_shotPath);
                    GD.Print($"SHOT saved page {_waits.Pages} -> {_shotPath}");
                    ReportSubroutines();
                    GetTree().Quit(0);
                }
                return;
            }
            if (_done && !_ended)
            {
                _ended = true;
                DumpHistogram();
                if (_vmFailure != null)
                {
                    GD.PushError($"[vm] worker failed after {_vm!.Steps} steps: {_vmFailure}");
                    ReportSubroutines();
                    if (!_selftest)
                        CaptureStallDiagnostic(_trace.Snapshot(), "worker-failure");
                }
                else
                {
                    GD.Print($"[vm] ended: {_vm!.HaltReason ?? "unknown"} after {_vm.Steps} steps");
                    ReportSubroutines();
                    if (_vm.CompatibilityFailure is { } compatibilityFailure)
                    {
                        GD.PushError($"[compatibility] {compatibilityFailure}");
                        _status.Text = "— compatibility halt —";
                        if (!_selftest)
                            CaptureStallDiagnostic(_trace.HaltSnapshot ?? _trace.Snapshot(),
                                                   "compatibility-halt");
                    }
                    else if (_vm.ScriptError is { } scriptError)
                    {
                        // Native AGE closes the game after its エラーが発生しました box is answered 中止.
                        GD.PushError($"[vm] script error: {scriptError.Text}");
                        _status.Text = "— script error —";
                        if (!_selftest) QuitAfterExitRequest("script error answered 中止");
                    }
                    else if (_vm.HaltReason == "exit-request" && !_selftest) QuitAfterExitRequest();
                    else ShowEnd();
                    if (_vm.HaltReason == "STEP-LIMIT")
                    {
                        GodotTraceSnapshot haltTrace = _trace.HaltSnapshot ?? _trace.Snapshot();
                        GD.Print(StepLimitDiagnosticFormatter.Format(haltTrace, _table!));
                        CaptureStallDiagnostic(haltTrace, "step-limit");
                    }
                }
                if (_selftest) RunSelfTestAndQuitOnFailure();
            }
        }
        finally { perf?.EndFrame(); }
    }

    /// <summary>How <see cref="CaptureStallDiagnostic"/> writes a snapshot: indented, with snake_case names.</summary>
    private static readonly PortJsonContext StallDiagnosticJson = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    });

    /// <summary>Write a diagnostic snapshot. Only the user's F6 copies its locator and path to the clipboard; a
    /// snapshot the port takes on its own (worker failure, halt, step limit) leaves the clipboard alone.</summary>
    private void CaptureStallDiagnostic(GodotTraceSnapshot? traceOverride = null,
                                        string snapshotKind = "stall", bool copyToClipboard = false)
    {
        try
        {
            long nowMs = _clock.NowMs;
            GodotTraceSnapshot trace = traceOverride ?? _trace.Snapshot();
            MoviePlaybackDiagnostics? movies = _movies?.PlaybackDiagnostics();
            // The waits' and graphics' state is the engine thread's: ask it, and say so when it does not answer.
            bool engineAnswered = _waits.TryInvoke(
                () => (_waits.CaptureDiagnosticSnapshot(), _vm.Gfx.CaptureDiagnosticSnapshot(nowMs)),
                DiagnosticAnswerTimeoutMs, out var engineState);
            var snapshot = new StallDiagnostic(
                FormatVersion: 1,
                CapturedUtc: System.DateTimeOffset.UtcNow.ToString("O"),
                RenderFrame: _timelineFrame,
                ClockMs: nowMs,
                Vm: new StallDiagnosticVm(_vm.Steps, _vm.HaltReason, _vm.CompatibilityFailure, _done, trace),
                EngineThreadAnswered: engineAnswered,
                Waits: engineAnswered ? engineState.Item1 : null,
                Gfx: engineAnswered ? engineState.Item2 : null,
                ActiveMovies: movies?.Playing,
                PendingMovies: movies?.Opened);

            string directory = _profilePersistence.DiagnosticsDirectory;
            System.IO.Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(directory,
                $"{snapshotKind}-{System.DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.json");
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(snapshot, StallDiagnosticJson.StallDiagnostic));
            string coordinate = trace.CurrentOffset >= 0
                ? $"{System.IO.Path.GetFileNameWithoutExtension(trace.CurrentScript).ToUpperInvariant()}@0x{trace.CurrentOffset:x}"
                : trace.CurrentScript;
            if (copyToClipboard)
            {
                DisplayServer.ClipboardSet($"{coordinate} · {snapshotKind} snapshot {path}");
                _status.Text = $"Diagnostic saved: {coordinate} (path copied)";
            }
            else
            {
                _status.Text = $"Diagnostic saved: {coordinate}";
            }
            GD.Print($"[diagnostic] {snapshotKind} snapshot {coordinate} -> {path}");
        }
        catch (System.Exception exception)
        {
            _status.Text = "Diagnostic capture failed; see Godot log.";
            GD.Print($"[diagnostic] stall snapshot failed: {exception}");
        }
    }

    /// <summary>A launch-flag number, read the same in every locale (a comma-decimal culture would read "1.5" as 15).</summary>
    private static bool TryParseLaunchDouble(string text, out double value)
        => double.TryParse(text, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>A launch-flag integer: decimal, or hex after <c>0x</c>.</summary>
    private static bool TryParseLaunchInteger(string text, out long value)
        => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                            System.Globalization.CultureInfo.InvariantCulture, out value)
            : long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign,
                            System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>How long a diagnostic snapshot waits for the engine thread to answer: a stalled one does not.</summary>
    private const int DiagnosticAnswerTimeoutMs = 500;

    /// <summary>The exit code when the engine thread does not stop at shutdown and the port exits without teardown.</summary>
    private const int VmStopTimeoutExitCode = 3;

    public override void _ExitTree()
    {
        StopDriveServer();
        _luaReload?.Dispose();
        bool vmStopped = true;
        if (_vm != null) _vm.RequestStop();
        CallDeferred(MethodName.ClearAgeCursor);
        _waits?.Stop();
        if (_engineThread != null)
        {
            vmStopped = _engineThread.Join(System.TimeSpan.FromSeconds(5));
            if (vmStopped && _vmFailure != null)
                GD.PushError($"[vm] worker failed during shutdown: {_vmFailure.Message}");
        }
        if (vmStopped)
        {
            _scripting?.Dispose();
            _luaRuntime.Dispose();
        }

        if (!vmStopped)
        {
            // The engine thread may still be using the glyph rasterizer and the movie decoders, and disposing native
            // resources under it can fault. Leave everything to process exit, with a nonzero code to show the stall.
            string message = "[vm] the VM did not stop within 5 seconds; exiting without teardown or the shared-profile flush";
            GD.PushError(message);
            System.Console.Error.WriteLine(message);
            System.Console.Error.Flush();
            System.Environment.Exit(VmStopTimeoutExitCode);
        }

        KeepWindowPosition();
        if (!_selftest && _vm != null)
        {
            if (_saves!.FlushMessageSettingsOnShutdown(_text!, _vm.SkipAndAuto) is { } settingsError)
                GD.PushWarning($"[settings] message settings save failed: {settingsError}");
            SharedProfileShutdownFlushResult flush = _saves!.FlushSharedProfileOnShutdown();
            if (flush.Outcome == SharedProfileShutdownFlushOutcome.Saved)
                GD.Print("[persistence] clean shutdown wrote SAVE.DAT and RT.DAT");
            else if (flush.Outcome == SharedProfileShutdownFlushOutcome.Failed)
                GD.PushWarning($"[persistence] clean-shutdown shared-profile write failed: {flush.Error}");
        }

        FlushTextMisses();
        DumpHistogram(); _timeline?.Dispose(); _locator?.Dispose();
        _gpuRenderer?.Dispose();
        _glyphRasterizerOwner?.Dispose();
        _glyphRasterizerOwner = null;
        _presentedRasterizerOwner?.Dispose();
        _presentedRasterizerOwner = null;
        if (_perf != null)
        {
            _perf.Dispose();
            GD.Print($"[perf-log] wrote {_perf.FrameCount} frames / {_perf.RecompositeCount} recomposites -> {_perf.Path}");
            _perf = null;
        }
        _movies?.Shutdown();
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

    /// <summary>--lua-errors continue|halt (Lua scripting spec §5.5): how a handler's error is taken.</summary>
    private static bool TryParseLuaErrors(string[] userArgs, out LuaErrorPolicy policy)
    {
        policy = LuaErrorPolicy.Continue;
        switch (ArgumentValue(userArgs, "--lua-errors"))
        {
            case null or "continue":
                return true;
            case "halt":
                policy = LuaErrorPolicy.Halt;
                return true;
            case var other:
                GD.PushError($"[startup] --lua-errors takes continue or halt, not '{other}'");
                return false;
        }
    }

    /// <summary>The scripting native's notices (spec §5.5): a handler's error, a mod switched off. The latest shows in the
    /// status line until the next page clears it; diagnostics/lua.log has them all.</summary>
    private void ServiceLuaNotices()
    {
        if (_scripting == null) return;
        int notices = _scripting.NoticeCount;
        if (notices == _luaNoticesSeen) return;
        _luaNoticesSeen = notices;
        if (_scripting.LatestNotice is { } notice)
        {
            _status.Text = notice;   // every notice opens with "Lua: "
            GD.PushWarning($"[lua] {notice}");
        }
    }

    /// <summary>The waits native's page notices (<see cref="InputAndWaits.PageNotices"/>): a text wait starting or
    /// ending its page, or a scene reset, clears the status line, and a new page refreshes the locator HUD.</summary>
    private void ServicePageNotices()
    {
        if (_waits == null) return;
        int notices = _waits.PageNotices;
        if (notices == _pageNoticesSeen) return;
        _pageNoticesSeen = notices;
        _status.Text = "";
        int pages = _waits.Pages;
        if (pages == _pagesSeen) return;
        _pagesSeen = pages;
        if (_locatorHudVisible) _locatorHud.Text = _locator.CurrentDisplay;
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

    private string DefaultPageMapPath(string scene)
    {
#if TOOLS
        // Editor/development runs hand the map to tools/locate_page.py through the workspace's per-profile build
        // directory.
        return PageLocatorState.DevelopmentMapPath(_selectedProfile.Profile.Id, scene);
#else
        // Exports have no repository and may be installed read-only. Keep diagnostics with the
        // profile-owned Godot data instead of probing for an age-reimpl ancestor.
        return System.IO.Path.Combine(
            _profilePersistence.DiagnosticsDirectory, "page-maps", PageLocatorState.MapFileName(scene));
#endif
    }

    /// <summary>AGE's own installed-font list (<see cref="FontFamilyCatalog"/>): from Windows GDI when text draws through
    /// it, so the names the scripts look up are the names GDI draws; otherwise from the installed font files, with the
    /// names and pitch GDI would give them (<see cref="OpenTypeFontFamilySource"/>).</summary>
    private static Func<IReadOnlyList<EnumeratedFontFamily>> InstalledFontFamilySource(
        IGlyphMaskRasterizer rasterizer, Sys4StartupSettings settings, Lazy<InstalledFontIndex> installedFonts)
    {
#if OME_WINDOWS_GDI
        if (rasterizer is WindowsGdiGlyphMaskRasterizer && OperatingSystem.IsWindows())
            return () =>
            {
                using var source = new WindowsGdiFontFamilySource();
                return FontFamilyCatalog.Build(source, settings.UseProportionalFont, settings.ValidFontCheckers);
            };
#endif
        return () => FontFamilyCatalog.Build(new OpenTypeFontFamilySource(installedFonts.Value),
                                             settings.UseProportionalFont, settings.ValidFontCheckers);
    }

    /// <summary>The installed fonts, read from the platform's font directories (<see cref="SystemFontFiles"/>); away
    /// from Windows also from each file the system's font configuration gives for a family it lists, which may lie in
    /// a directory of its own.</summary>
    private static InstalledFontIndex ScanInstalledFonts()
    {
        var stopwatch = Stopwatch.StartNew();
        IEnumerable<string> files = SystemFontFiles.Enumerate(SystemFontFiles.Directories());
        if (!OperatingSystem.IsWindows())
            files = files.Concat(OS.GetSystemFonts()
                                   .Select(name => OS.GetSystemFontPath(name))
                                   .Where(path => !string.IsNullOrEmpty(path)));
        InstalledFontIndex index = InstalledFontIndex.Scan(files, message => GD.PushWarning($"[text] font {message}"));
        GD.Print($"[text] installed fonts: {index.Families.Count} families from their files in " +
                 $"{stopwatch.ElapsedMilliseconds} ms");
        return index;
    }
}

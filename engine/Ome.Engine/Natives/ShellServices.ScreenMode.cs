using Ome.Engine.Devices;

namespace Ome.Engine.Natives;

/// <summary>
/// AGE's screen mode (display_toggle_screen_mode_and_menu_bar@0x412df0; docs/engine-re.md, "Screen mode"): in a window
/// or fullscreen. Every switch comes here, whoever asks (menu 0x9c5a ﾌﾙｽｸﾘｰﾝ and 0x9c5b ｳｲﾝﾄﾞｳ, Alt+Enter, the maximize
/// box, the strip's restore button, op 0x2cf). A switch to the current mode does nothing, and display:ForceScreen 1
/// keeps a window. A switch stores display:ScreenMode (native writes it at exit; the port writes the same value at
/// once), tells the window, and queues CALLBACK_WINDOW.BIN through the script launch queue, which the interpreter runs
/// as it runs CALLBACK_SETTING.BIN.
/// </summary>
/// <remarks>Front end's thread, apart from op 0x2cf's <see cref="RequestScreenMode"/>, which the engine thread records
/// for the front end's next <see cref="ServiceFrame"/>.</remarks>
public sealed partial class ShellServices
{
    private readonly IWindowDevice? _window;
    private volatile bool _windowed = true;   // display_windowed (ctx+0x9a298)
    private bool _forceScreen;                // display:ForceScreen 1: a window stays a window
    private int _requestedScreenMode = -1;    // op 0x2cf's target (1 window, 0 fullscreen), from the engine thread

    /// <summary>IAGEService +0x08: the game runs in a window.</summary>
    public bool Windowed => _windowed;

    /// <summary>display:ForceScreen 1: the maximize box, Alt+Enter and every switch keep the window.</summary>
    public bool ForceScreen => _forceScreen;

    /// <summary>engine_initialize_graphics_from_settings: display_windowed = (display:ScreenMode != 0), and
    /// display:ForceScreen. <paramref name="windowedArgument"/> (<c>--windowed</c>) starts a window without changing
    /// the setting; so does a run without SYS4REG.INI (self-test, read-only persistence). Fullscreen is shown at once;
    /// nothing is stored and no callback runs.</summary>
    public void StartScreenMode(bool windowedArgument)
    {
        _forceScreen = _settings?.LoadInt("display", "ForceScreen", 0) == 1;
        bool windowed = _settings == null || _settings.LoadInt("display", "ScreenMode", 0) != 0 || windowedArgument;
        _print($"[display] starting {(windowed ? "in a window" : "fullscreen")} " +
               $"(display:ScreenMode={_settings?.LoadInt("display", "ScreenMode", 0).ToString() ?? "none"}" +
               $"{(windowedArgument ? ", --windowed" : "")}, " +
               $"ForceScreen={(_forceScreen ? 1 : 0)})");
        if (windowed) return;
        _windowed = false;
        _window?.ShowScreenMode(windowed: false);
    }

    /// <summary>IAGEService +0x1c (display_toggle_screen_mode_and_menu_bar): switch to <paramref name="windowed"/>, or
    /// do nothing when the game is already there or display:ForceScreen keeps the window. True when the mode changed.
    /// </summary>
    /// <param name="source">What asked, for the log.</param>
    public bool SwitchScreenMode(bool windowed, string source)
    {
        if (windowed == _windowed) return false;
        if (_windowed && _forceScreen)
        {
            _print($"[display] {source}: display:ForceScreen=1 keeps the window");
            return false;
        }
        _windowed = windowed;
        _window?.ShowScreenMode(windowed);
        _settings?.SaveInt("display", "ScreenMode", windowed ? 1 : 0);
        Queue(new Queued(QueuedKind.ScreenModeSwitched));   // script_launch_by_name("CALLBACK_WINDOW.BIN")
        _print($"[display] {source}: {(windowed ? "window" : "fullscreen")}; CALLBACK_WINDOW.BIN queued");
        return true;
    }

    /// <summary>Op 0x2cf (op_0x2cf_handler@0x425370), from the engine thread: the front end switches at its next
    /// frame.</summary>
    public void RequestScreenMode(bool windowed) => Volatile.Write(ref _requestedScreenMode, windowed ? 1 : 0);

    /// <summary>Per frame, on the front end's thread: op 0x2cf's request.</summary>
    public void ServiceFrame()
    {
        int requested = Interlocked.Exchange(ref _requestedScreenMode, -1);
        if (requested >= 0) SwitchScreenMode(requested == 1, "op 0x2cf");
    }
}

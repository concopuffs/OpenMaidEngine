using System.Collections.Concurrent;
using Ome.Engine.Devices;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

/// <summary>message:MessageSpeed, MesWinAlpha and AutoMessageTime0/1, as the 設定 dialog reads and commits them.
/// </summary>
public readonly record struct MessageSettings(int MessageSpeed, int MesWinAlpha, long AutoMessageTime0,
                                              long AutoMessageTime1);

/// <summary>A menu command the interpreter applies at its next safe point.</summary>
public enum ShellMenuRequestKind
{
    /// <summary>Menu 0x9c63 (IAGEService +0x88, adv_toggle_skip_mode@0x406c20).</summary>
    MessageSkipToggle,

    /// <summary>Menu 0x9c65 (IAGEService +0x84, adv_toggle_auto_mode@0x406b70).</summary>
    AutoMessageToggle,

    /// <summary>Menu 0x9c6f (IAGEService +0xd8 at 0x406cd0): toggle message:ReadTextSkip and refresh read skip.</summary>
    ReadMessageSkipToggle,

    /// <summary>The 設定 dialog's OK (agerc_settings_apply): <see cref="ShellMenuRequest.Settings"/>.</summary>
    MessageSettings,
}

/// <summary>One queued menu command, with the 設定 values for <see cref="ShellMenuRequestKind.MessageSettings"/>.
/// </summary>
public readonly record struct ShellMenuRequest(ShellMenuRequestKind Kind, MessageSettings Settings = default);

/// <summary>
/// The shell-services native (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.2, §8; plan §3.2,
/// addition 4; milestone 9): what the native menu and its dialogs, and the scripts, share. It owns the state a script
/// can read or change that the shell also reads (op 0x142's system-menu gate, op 0x148/0x149's strip delay, op 0x10f's
/// system-key mask), the menu commands queued for the interpreter, the callback scripts they and a screen-mode switch
/// launch, op 0x1b2 to 0x1b4's diagnostic text, the engine's dialogs (<c>ShellServices.Dialogs.cs</c>) and the screen
/// mode (<c>ShellServices.ScreenMode.cs</c>). Its devices are the window and the front end's dialogs.
/// </summary>
/// <remarks>
/// <para>The menu state the shell shows (VmMenuState) stays a composition at the VM, which asks each owner (plan §3.2,
/// addition 4).</para>
/// <para>Threads: the front end queues menu requests and reads the system-menu fields; the interpreter takes the requests
/// and the callbacks on the engine thread (spec §5.4: a native never runs a script, it records a request).</para>
/// </remarks>
public sealed partial class ShellServices
{
    private const string SettingCallbackScript = "CALLBACK_SETTING.BIN";
    private const string WindowCallbackScript = "CALLBACK_WINDOW.BIN";

    private enum QueuedKind { Menu, ScreenModeSwitched }

    private readonly record struct Queued(QueuedKind Kind, ShellMenuRequest Request = default);

    private readonly Sys4RegIniStore? _settings;
    private readonly DiagnosticOutputState _diagnostics;
    private readonly Action<string> _print;
    private Action _wake = static () => { };

    // Queued from the front end's thread, taken on the engine thread.
    private readonly ConcurrentQueue<Queued> _requests = new();
    private volatile bool _requestsPending;
    // Engine thread: the callback scripts native's launch queue would hold.
    private bool _settingCallbackPending;   // CALLBACK_SETTING.BIN: a menu toggle, the 設定 OK, a mode-2 click cancel
    private bool _windowCallbackPending;    // CALLBACK_WINDOW.BIN: a screen-mode switch
    // EngineCtx +0xa0d10 (op 0x142): AGERC queries this through IAGEService to gray its native settings/save menu
    // actions while CONFIG owns the scripted settings screen.
    private int _systemMenuActionsEnabled = 1;
    // EngineCtx +0x5511c (ops 0x148/0x149): unsigned TIMER_SHOWMENU dwell threshold. Native op 0x148 returns the same
    // dword through the VM's signed integer-cell representation.
    private uint _systemMenuShowDelayMilliseconds;
    // EngineCtx +0x6da7c (op 0x10f): the window procedure's system-key disable mask. Bit 0 suppresses F1's screenshot
    // BMP, bit 9 (0x200) swallows F10's menu-bar focus.
    private int _systemKeyDisableMask;

    /// <param name="settings">SYS4REG.INI: display:ScreenMode and ForceScreen. Null when the run has none.</param>
    /// <param name="window">The window a screen-mode switch shows; without one a switch changes only the state.</param>
    /// <param name="dialogs">The front end's dialogs; without them a dialog is never shown and waits for a stop.</param>
    /// <param name="diagnostics">Op 0x1b2's text, which a session shares across its scene VMs as native's EngineCtx
    /// keeps it across frames; a new one by default.</param>
    /// <param name="print">Where the display log lines go.</param>
    public ShellServices(Sys4RegIniStore? settings = null, IWindowDevice? window = null, IShellDialogs? dialogs = null,
                         DiagnosticOutputState? diagnostics = null, Action<string>? print = null)
    {
        _settings = settings;
        _window = window;
        _dialogs = dialogs;
        _diagnostics = diagnostics ?? new DiagnosticOutputState();
        _print = print ?? (_ => { });
    }

    /// <summary>A shell native without a window or dialogs (tests, tools, a headless session): a screen-mode switch
    /// changes only the state, and a dialog stays open until a stop. <paramref name="diagnostics"/> is op 0x1b2's text,
    /// a new one by default.</summary>
    public static ShellServices Headless(DiagnosticOutputState? diagnostics = null) => new(diagnostics: diagnostics);

    /// <summary>What to call when a menu request is queued: the lifted host's waits, so a text wait takes it at once
    /// (until milestone 11).</summary>
    public void WakeWaitsWith(Action wake) => _wake = wake ?? throw new ArgumentNullException(nameof(wake));

    // ---- the system menu ----

    /// <summary>Op 0x142's gate: 0 while CONFIG runs, when the native menu grays its settings commands.</summary>
    public int SystemMenuActionsEnabled
    {
        get => Volatile.Read(ref _systemMenuActionsEnabled);
        set => Volatile.Write(ref _systemMenuActionsEnabled, value);
    }

    /// <summary>Ops 0x148/0x149: how long the pointer must rest on the screen's top edge before the fullscreen
    /// strip shows (system_menu_top_edge_dwell_timer_tick@0x486280).</summary>
    public uint SystemMenuShowDelayMilliseconds
    {
        get => Volatile.Read(ref _systemMenuShowDelayMilliseconds);
        set => Volatile.Write(ref _systemMenuShowDelayMilliseconds, value);
    }

    /// <summary>Op 0x10f's system-key disable mask, read by the front end's F1 and F10 handling. Written on the engine
    /// thread; a front-end read sees the last completed write.</summary>
    public int SystemKeyDisableMask
    {
        get => Volatile.Read(ref _systemKeyDisableMask);
        set => Volatile.Write(ref _systemKeyDisableMask, value);
    }

    // ---- menu requests and the callbacks they launch ----

    /// <summary>Menu 0x9c63's Skip toggle. Safe from the front end's thread.</summary>
    public void RequestMessageSkipToggle() => QueueMenu(new(ShellMenuRequestKind.MessageSkipToggle));

    /// <summary>Menu 0x9c65's Auto toggle.</summary>
    public void RequestAutoMessageToggle() => QueueMenu(new(ShellMenuRequestKind.AutoMessageToggle));

    /// <summary>Menu 0x9c6f's read-skip toggle.</summary>
    public void RequestReadMessageSkipToggle() => QueueMenu(new(ShellMenuRequestKind.ReadMessageSkipToggle));

    /// <summary>The 設定 dialog's OK (agerc_settings_apply, then IAGEService +0x20 launching CALLBACK_SETTING.BIN):
    /// MessageSpeed as op 0x1b5 sets it (IAGEService +0x10 writes ctx+0x14e9c), MesWinAlpha as op 0x141 and the Auto
    /// delays as op 0x1b9.</summary>
    public void RequestMessageSettings(MessageSettings settings)
        => QueueMenu(new(ShellMenuRequestKind.MessageSettings, settings));

    /// <summary>True while requests wait for <see cref="TakeMenuRequests"/>.</summary>
    public bool HasMenuRequests => _requestsPending;

    /// <summary>The queued menu commands, oldest first, for the interpreter to apply on the engine thread; each is
    /// followed by CALLBACK_SETTING.BIN (<see cref="RequestSettingCallback"/>), as AGERC's command handler launches it
    /// after the service call. A screen-mode switch in the queue requests CALLBACK_WINDOW.BIN here.</summary>
    public IReadOnlyList<ShellMenuRequest> TakeMenuRequests()
    {
        if (!_requestsPending) return [];
        _requestsPending = false;
        List<ShellMenuRequest>? taken = null;
        while (_requests.TryDequeue(out Queued queued))
        {
            if (queued.Kind == QueuedKind.ScreenModeSwitched) _windowCallbackPending = true;
            else (taken ??= []).Add(queued.Request);
        }
        return taken ?? (IReadOnlyList<ShellMenuRequest>)[];
    }

    /// <summary>Queue CALLBACK_SETTING.BIN (engine thread): after a menu command, or set:CancelMesSkipOnClick 2's
    /// cancel (script_launch_queue_enqueue@0x40f820).</summary>
    public void RequestSettingCallback() => _settingCallbackPending = true;

    /// <summary>The next queued callback script, in the order native's launch queue would hold them, or null. The
    /// interpreter runs it at its next instruction boundary outside a statement block, or from a text wait's input
    /// service.</summary>
    public string? TakeCallbackScript()
    {
        if (_settingCallbackPending)
        {
            _settingCallbackPending = false;
            return SettingCallbackScript;
        }
        if (_windowCallbackPending)
        {
            _windowCallbackPending = false;
            return WindowCallbackScript;
        }
        return null;
    }

    // ---- op 0x1b2 to 0x1b4's diagnostic text ----

    /// <summary>The text ops 0x1b2/0x1b3 have appended since op 0x1b4 last showed it.</summary>
    public string PendingDiagnosticText => _diagnostics.PendingText;

    public void AppendDiagnostic(string text) => _diagnostics.Append(text);

    public void ClearDiagnostic() => _diagnostics.Clear();

    // ---- scene reset ----

    /// <summary>scene_context_init_reset (op 0x9): the system-menu fields return to their defaults, and queued menu
    /// commands and callbacks go with the frames they were for; an open dialog is cancelled.</summary>
    public void ResetScene()
    {
        _settingCallbackPending = false;
        _windowCallbackPending = false;
        _requests.Clear();
        _requestsPending = false;
        SystemMenuActionsEnabled = 1;
        SystemMenuShowDelayMilliseconds = 0;
        SystemKeyDisableMask = 0;
        CancelDialogs();
    }

    private void QueueMenu(ShellMenuRequest request) => Queue(new Queued(QueuedKind.Menu, request));

    private void Queue(Queued queued)
    {
        _requests.Enqueue(queued);
        _requestsPending = true;
        _wake();
    }
}

/// <summary>
/// EngineCtx-lifetime accumulator used by opcodes 0x1b2 through 0x1b4. GameSession shares this
/// state across fresh scene VMs just as native AGE retains the embedded string across script frames.
/// </summary>
public sealed class DiagnosticOutputState
{
    private readonly System.Text.StringBuilder _text = new();

    public string PendingText => _text.ToString();

    internal void Append(string value) => _text.Append(value);
    internal void Clear() => _text.Clear();
}

using Ome.Engine.Devices;
using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Natives;

/// <summary>
/// The skip and auto native (docs/superpowers/specs/2026-11-12-engine-layering-design.md §5.2, milestone 5): persistent
/// Skip (op 0x88 and the menu's toggle), run-state 0x08000000 (ADV fast-forward) and its two channels, read skip, the ADV
/// skip service (ops 0x19b and 0x19c), fast-forward from logical action 6 (SkipAndAuto.PhysicalInput.cs),
/// CancelMesSkipOnClick's click machine (SkipAndAuto.ClickCancel.cs), Auto and its two delays, and fast-forward pacing
/// (SkipAndAuto.Pace.cs). Handlers call it with what other owners hold (the input bindings, the read-text verdict); it
/// tells its <see cref="IFastForwardListener"/>s each time a channel is set.
/// </summary>
/// <remarks>
/// Threads: the VM thread calls everything except <see cref="PhysicalInputChanged"/> and <see cref="TryLatchCancelClick"/>,
/// which the front end's input thread calls. The front end and the lifted host read the state from theirs. Two locks
/// remain until milestone 11 (spec §5.1, rule 4): the input lock orders the action-6 decisions, and the state lock
/// joins the two channels.
/// </remarks>
public sealed partial class SkipAndAuto
{
    private readonly object _stateLock = new();
    private IFastForwardListener[] _listeners = [];

    private volatile bool _messageSkipEnabled;   // ctx+0x550fc message_skip_display_enabled: the Skip toggle
    private bool _messageSkipInjected;           // ctx+0x13dc message_skip_enabled: Skip a text wait acts on
    private bool _scriptFastForward;             // run-state 0x08000000 as scripts and the Skip toggles set it
    private bool _physicalFastForward;           // run-state 0x08000000 from logical action 6
    private volatile bool _fastForward;          // run-state 0x08000000: either channel
    private bool _readSkipActive;                // adv_read_skip_state
    // ctx+0x55100 adv_skip_service_enabled: scene_context_init_reset sets it, op 0x19b clears it, op 0x19c sets it.
    // The menu's Skip and Auto toggles change the active fast-forward state only while it is set.
    private bool _advSkipServiceEnabled = true;
    private bool _autoMessageEnabled;
    // message:AutoMessageTime0/1 (registered defaults 500/2000 ms): ops 0x1b8/0x1b9 and the 設定 dialog read and write
    // them, and SYS4REG.INI keeps them (the lifted host loads and saves them).
    private long _autoMessageTime0Ms = Sys4RegIniStore.DefaultAutoMessageTime0;
    private long _autoMessageTime1Ms = Sys4RegIniStore.DefaultAutoMessageTime1;
    private bool _autoVoicePending;              // a voice started since the last op 0x1bc

    /// <param name="settings">The SYS4INI settings and the pace this native reads.</param>
    /// <param name="paceClock">The time device fast-forward pacing reads and sleeps on.</param>
    public SkipAndAuto(SkipAndAutoSettings settings, IPaceClock paceClock)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paceClock = paceClock ?? throw new ArgumentNullException(nameof(paceClock));
    }

    /// <summary>A native over the system clock (a Stopwatch and Thread.Sleep), with native's registered settings and
    /// no pacing unless <paramref name="settings"/> says otherwise.</summary>
    public static SkipAndAuto WithSystemClock(SkipAndAutoSettings? settings = null)
        => new(settings ?? new SkipAndAutoSettings(), new SystemPaceClock());

    public SkipAndAutoSettings Settings { get; }

    /// <summary>Tell <paramref name="listener"/> each time a channel is set. A listener already told is not added
    /// twice.</summary>
    public void Listen(IFastForwardListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_stateLock)
        {
            if (_listeners.Any(known => ReferenceEquals(known, listener))) return;
            _listeners = [.. _listeners, listener];
        }
    }

    /// <summary>Run-state 0x08000000: ADV fast-forward, from either channel.</summary>
    public bool FastForwardActive => _fastForward;

    /// <summary>ctx+0x550fc, the persistent Skip toggle (op 0x19a reads it; the menu shows it).</summary>
    public bool MessageSkipEnabled => _messageSkipEnabled;

    /// <summary>ctx+0x13dc: persistent Skip as a text wait acts on it. While it is set, the wait's poll adds action 6
    /// and starts fast-forward again.</summary>
    public bool MessageSkipInjected => _messageSkipInjected;

    /// <summary>adv_read_skip_state (op 0x1cc reads it).</summary>
    public bool ReadSkipActive => _readSkipActive;

    /// <summary>Set or clear the script channel of run-state 0x08000000.</summary>
    public void SetScriptFastForward(bool active) => SetChannel(FastForwardChannel.Script, active);

    /// <summary>Set or clear the action-6 channel of run-state 0x08000000. The input methods
    /// (SkipAndAuto.PhysicalInput.cs) decide when; a test may also start from it.</summary>
    public void SetPhysicalFastForward(bool active) => SetChannel(FastForwardChannel.Physical, active);

    private void SetChannel(FastForwardChannel channel, bool value)
    {
        bool active;
        bool changed;
        lock (_stateLock)
        {
            if (channel == FastForwardChannel.Physical) _physicalFastForward = value;
            else _scriptFastForward = value;
            active = _scriptFastForward || _physicalFastForward;
            changed = active != _fastForward;
            _fastForward = active;
        }
        foreach (IFastForwardListener listener in Volatile.Read(ref _listeners))
            listener.FastForwardChannelSet(channel, value, active, changed);
    }

    /// <summary>Run <paramref name="hold"/> under the state lock if fast-forward is on, and say whether it ran. The
    /// lifted host holds a voice back this way while fast-forward runs (native's message_skip_queued_voice): a channel
    /// set on another thread cannot end fast-forward between the test and the hold, so the listener that takes the
    /// voice when it ends always finds it.</summary>
    public bool WhileFastForward(Action hold)
    {
        ArgumentNullException.ThrowIfNull(hold);
        lock (_stateLock)
        {
            if (!_fastForward) return false;
            hold();
            return true;
        }
    }

    /// <summary>Op 0x88. op_0x88_set_message_skip@0x41f130 writes ctx+0x13dc and ctx+0x550fc alike. Enabling sets no
    /// run-state bit: the next polling text wait starts fast-forward from the injected action 6, so SC0000's Skip
    /// button still sleeps its 100 ms and the page passes once the callback returns into op 0x72. Disabling clears
    /// run-state 0x8000000.</summary>
    public void SetMessageSkip(bool enabled)
    {
        _messageSkipEnabled = enabled;
        _messageSkipInjected = enabled;
        if (enabled) return;
        SetScriptFastForward(false);
        EndPhysicalFastForward();
    }

    /// <summary>Op 0x19b: leave ADV presentation, keeping the toggle.</summary>
    public void SuspendAdvSkipService()
    {
        _advSkipServiceEnabled = false;
        _messageSkipInjected = false;   // ctx+0x13dc; the toggle (ctx+0x550fc) survives
        SetScriptFastForward(false);
        EndPhysicalFastForward();   // it clears run-state 0x8000000, however started
    }

    /// <summary>Op 0x19c: recompute active fast-forward on ADV entry.</summary>
    public void ResumeAdvSkipService()
    {
        _advSkipServiceEnabled = true;
        // ctx+0x13dc follows the toggle; a read-skip-only resume leaves it as it was.
        if (_messageSkipEnabled) _messageSkipInjected = true;
        else if (!_readSkipActive) _messageSkipInjected = false;
        SetScriptFastForward(_messageSkipEnabled || _readSkipActive);
        EndPhysicalFastForward();   // it rebuilds run-state 0x8000000 from persistent Skip and read skip only
    }

    /// <summary>Op 0x101's part: it writes the CancelMesSkipOnClick press/release state ctx+0x6da80 = 0, clears
    /// run-state 0x8000000 (0x4170ce) however it started, and sets ctx+0x6da74, so a held action 6 starts no reveal's
    /// fast-forward until a reveal runs with it released. Persistent Skip and read skip start it again at the next
    /// text wait, or when the callback that ran the op returns into op 0x72.</summary>
    public void ResetMessageSkipInput()
    {
        _clickCancelState = 0;
        Interlocked.Exchange(ref _clickCancelTriggers, 0);
        SetScriptFastForward(false);
        EndPhysicalFastForward();
        lock (_inputLock) _revealPhysicalSkipSuppressed = true;
    }

    /// <summary>
    /// The read-skip block that ops 0x6e, 0x71 and 0x72 share and the menu bar's read-skip toggle repeats
    /// (op_0x71_reset_adv_text_layout@0x41e540; docs/engine-re.md, "Read skip and ctx+0x13dc"). With
    /// message:ReadTextSkip on, a read message sets run-state 0x08000000 and adv_read_skip_state; an unread one clears
    /// adv_read_skip_state only while persistent Skip is off. It never ends fast-forward, and never starts it from
    /// persistent Skip, so a layout reset outside ADV (after op 0x19b) leaves it off.
    /// </summary>
    /// <param name="readTextSkip">message:ReadTextSkip.</param>
    /// <param name="messageRead">ReadTextDB holds the current message (read only when the setting is on).</param>
    public void ApplyReadTextSkip(bool readTextSkip, bool messageRead)
    {
        if (!readTextSkip)
        {
            _readSkipActive = false;
            return;
        }
        if (messageRead)
        {
            _readSkipActive = true;
            SetScriptFastForward(true);
        }
        else if (!_messageSkipEnabled)
        {
            _readSkipActive = false;
        }
    }

    /// <summary>The entry of op_0x72_handler after the read-skip block, which also runs when a hotspot callback's ret
    /// re-runs the waiting op: run-state 0x8000000 cleared unless the message is read or action 6 is held. Persistent
    /// Skip (ctx+0x13dc) sets it again as the wait polls its injected action 6, so the page still passes.</summary>
    public void EnterTextWait(InputBindings input)
    {
        if (!_readSkipActive) SetScriptFastForward(_messageSkipInjected);
        ReconcilePhysicalFastForward(input);
    }

    /// <summary>The entry of op_0xfa_refresh_adv_skip_input_state (/v2 0x416de0, 4.33G 0x417580). It polls the held
    /// mask, which carries no Skip injection, so without action 6 held it clears run-state 0x08000000 even on a read
    /// message.</summary>
    public void RefreshAdvSkipInputState(InputBindings input)
    {
        ReconcilePhysicalFastForward(input);
        if (!FastForwardActionHeld(input)) SetScriptFastForward(false);
    }

    /// <summary>adv_toggle_skip_mode@0x406c20 (menu 0x9c63): flip persistent Skip (enabling clears Auto unless
    /// set:CoexistMesSkip). With the skip service enabled, enabling starts fast-forward at once: it ends a text wait,
    /// completes a reveal and stops the wait indicator. Disabling clears only the active flag (ctx+0x13dc) and leaves
    /// run-state 0x08000000, so fast-forward runs on until op 0xfa or 0x19c recomputes it at the next page.</summary>
    public void ToggleMessageSkipFromMenu()
    {
        if (!_messageSkipEnabled)
        {
            if (!Settings.CoexistMessageSkip) _autoMessageEnabled = false;
            _messageSkipEnabled = true;
        }
        else
        {
            _messageSkipEnabled = false;
        }
        if (!_advSkipServiceEnabled) return;
        _messageSkipInjected = _messageSkipEnabled;   // ctx+0x13dc follows only while the service is enabled
        if (!_messageSkipEnabled) return;
        SetScriptFastForward(true);
    }

    /// <summary>adv_toggle_auto_mode@0x406b70 (menu 0x9c65): flip Auto (enabling clears Skip unless
    /// set:CoexistMesSkip). Native arms its sleep timer with AutoMessageTime1 unless a voice is pending; the port's text
    /// wait arms its auto timer the same way when it first sees Auto enabled. With the skip service enabled, enabling
    /// also clears run-state 0x80000000, so a polling text wait ends at once and the next page comes without input. It
    /// sets ctx+0x13dc (the injected skip action) from the persistent Skip but leaves run-state 0x08000000 to the next
    /// page's op 0xfa.</summary>
    /// <returns>Whether the toggle clears run-state 0x80000000: the caller ends a polling text wait.</returns>
    public bool ToggleAutoMessageFromMenu()
    {
        if (_autoMessageEnabled)
        {
            _autoMessageEnabled = false;
            return false;
        }
        _autoMessageEnabled = true;
        if (!Settings.CoexistMessageSkip) _messageSkipEnabled = false;
        if (_advSkipServiceEnabled) _messageSkipInjected = _messageSkipEnabled;
        return _advSkipServiceEnabled;
    }

    /// <summary>Auto (ops 0x1b6/0x1b7 and the menu's toggle).</summary>
    public bool AutoMessageEnabled => _autoMessageEnabled;

    /// <summary>Op 0x1b7.</summary>
    public void SetAutoMessage(bool enabled) => _autoMessageEnabled = enabled;

    /// <summary>message:AutoMessageTime0: Auto's delay after a voice.</summary>
    public long AutoMessageTime0Ms => Interlocked.Read(ref _autoMessageTime0Ms);

    /// <summary>message:AutoMessageTime1: Auto's delay on an unvoiced page.</summary>
    public long AutoMessageTime1Ms => Interlocked.Read(ref _autoMessageTime1Ms);

    /// <summary>Op 0x1b9's write, the 設定 dialog's and SYS4REG.INI's: selector 0 is message:AutoMessageTime0 (after a
    /// voice), 1 is AutoMessageTime1 (unvoiced); other selectors write nothing.</summary>
    public void SetAutoMessageTime(int selector, long milliseconds)
    {
        if (selector == 0) Interlocked.Exchange(ref _autoMessageTime0Ms, milliseconds);
        else if (selector == 1) Interlocked.Exchange(ref _autoMessageTime1Ms, milliseconds);
    }

    /// <summary>A voice started (ops 0xc4 and 0x1bd): Auto waits for it and then AutoMessageTime0.</summary>
    public void NoteVoiceStarted() => _autoVoicePending = true;

    /// <summary>Op 0x1bc's part: no voice is pending any more.</summary>
    public void ClearPendingVoice() => _autoVoicePending = false;

    /// <summary>What a text wait's Auto timer reads.</summary>
    public AdvAutoWaitState AutoWaitState
        => new(_autoMessageEnabled, _autoVoicePending, AutoMessageTime0Ms, AutoMessageTime1Ms);

    /// <summary>scene_context_init_reset's part: Skip, Auto, read skip and the click machine start over, the skip
    /// service is enabled, and run-state 0x08000000 is zeroed (a held action 6 starts nothing). The delays and the
    /// action-6 history are kept.</summary>
    public void ResetScene()
    {
        _autoMessageEnabled = false;
        _autoVoicePending = false;
        _messageSkipEnabled = false;
        _messageSkipInjected = false;
        _readSkipActive = false;
        _clickCancelState = 0;
        Interlocked.Exchange(ref _clickCancelTriggers, 0);
        _advSkipServiceEnabled = true;
        SetScriptFastForward(false);
        EndPhysicalFastForward();
    }
}

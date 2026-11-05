using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using System.Text;
namespace Age.Engine.Vm;

/// <summary>A stable identity/snapshot of the exact script frame currently executing.</summary>
public sealed record DebugFrameSnapshot(long FrameId, string CurrentScript, IReadOnlyList<string> CallStack);

public enum SharedProfileShutdownFlushOutcome
{
    Saved,
    AlreadyHandled,
    Suppressed,
    StoreUnavailable,
    Failed,
}

public readonly record struct SharedProfileShutdownFlushResult(
    SharedProfileShutdownFlushOutcome Outcome,
    string? Error = null);

public sealed partial class VirtualMachine
{
    private const long NoJump = 0xFFFFFFFF;
    private const int HALT = int.MinValue;
    private const int FRAME_RETURN = int.MinValue + 1;
    private const int HOTSPOT_RETURN = int.MinValue + 2;
    private const int ROOT_RELOAD = int.MinValue + 3;
    private const int T_IMM = 0, T_FLOAT = 1, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_GPTR = 6,
                      T_GSTRPTR = 8, T_LINT = 9, T_LFLOAT = 10, T_LSTR = 11, T_LPTR = 12,
                      T_LSTRPTR = 14;
    private const string DiagnosticCaption = "エラーが発生しました";

    private readonly Script _s;
    private readonly OpcodeTable _t;
    private readonly IHost _host;
    private readonly VmOptions _o;
    private readonly VmCompatibilityContext _compatibility;
    private readonly Encoding _nativeStringEncoding;
    private readonly IScriptProvider? _provider;
    private readonly SharedProfile _sharedProfile;
    private readonly DiagnosticOutputState _diagnosticOutput;
    private readonly INativeDatStore? _nativeDatStore;
    private static readonly bool _diagSetTexture = System.Environment.GetEnvironmentVariable("AGE_DIAG_SETTEX") == "1";
    private ExecFrame _cur = null!;
    private int _depth;
    private readonly ITraceSink _sink;
    private readonly object _interactiveLock = new();
    private readonly object _debugControlLock = new();
    private readonly object _sharedProfileShutdownLock = new();
    private readonly List<string> _activeFrameNames = new();
    private readonly List<ExecFrame> _activeExecutionFrames = new();
    private ExecFrame? _saveResumeFrame;
    private NativeNumberedSaveState? _loadedNumberedState;
    private NativeNumberedSaveState? _retainedNativeNumberedState;
    private int _restoreFrameIndex = -1;
    private uint _accumulatedPlaySeconds;
    private readonly long _sessionStartTimestamp;
    private ExecFrame? _debugActiveFrame;
    private long _debugActiveFrameId;
    private long _debugNextFrameId;
    private DebugFrameReturnRequest? _debugFrameReturnRequest;
    private HostedSceneEntry? _hostedSceneEntry;   // pending until the coordinator frame enters
    private long? _hostedScenePackedId;            // set once the coordinator returned, until the scene enters
    private ExecFrame? _interactiveFrame;
    private ExecFrame? _rawInputFrame;
    private int _pointerX = int.MinValue, _pointerY = int.MinValue;
    private int _mouseButtonState;
    private int _mouseWheelDelta;
    // Native run-state bit 0x80000000: an ADV text wait (op 0x72) is polling input. It is clear while that wait
    // runs a hotspot callback, as native clears the bit on dispatch and the callback's ret re-runs the wait.
    private volatile bool _advTextWaitPolling;
    private int _heldInputCallbackMask;
    private int _queuedInputCallbackMask;
    private bool _autoMessageEnabled;
    private long _autoMessageTime0Ms = 500;
    private long _autoMessageTime1Ms = 2000;
    private bool _autoVoicePending;
    private const int VoiceMuteRequested = 0x1, VoiceMutePromoted = 0x10000;
    private int _voiceMuteNextFlags;          // native voice_mute_next_flags (ctx+0x1493c), ops 0x1d5/0xc4/0x1bd/0x1bc
    private bool _statementBlockActive;       // native statement_block_active (ctx+0x5f2fc), ops 0x1f4/0x1f5
    private int _statementBlockDepth;         // native statement_block_depth (ctx+0x5f300): nested opens
    private bool _tickPresentationEnabled;    // native tick_presentation_enabled (ctx+0x99294), ops 0x21b/0x247
    private bool _initialRootRun = true;
    private volatile bool _stopRequested;
    private volatile bool _rootReloadRequested;   // frontend 終了 dialog: タイトルに戻る
    private bool _sharedProfileShutdownHandled;
    private volatile bool _messageSkipEnabled;
    private volatile bool _messageSkipServiceActive;
    private bool _advReadSkipState;
    private AdvTextStyle _advTextStyle = AdvTextStyle.Default;
    private int _messageWindowAlphaSetting;
    private int _messageGlyphDelayMilliseconds;
    // EngineCtx +0xa0d10: AGERC queries this through IAGEService to gray its native
    // settings/save menu actions while CONFIG owns the scripted settings screen.
    private int _systemMenuActionsEnabled = 1;
    // EngineCtx +0x5511c: unsigned TIMER_SHOWMENU dwell threshold. Native op 0x148
    // returns the same dword through the VM's signed integer-cell representation.
    private uint _systemMenuShowDelayMilliseconds;
    // EngineCtx +0x6da7c, op 0x10f: the window procedure's system-key disable mask. Bit 0 suppresses F1's screenshot
    // BMP, bit 9 (0x200) swallows F10's menu-bar focus. Scene reset clears it.
    private int _systemKeyDisableMask;
    private readonly Dictionary<string, int> _valueSwitchTargets = new(StringComparer.Ordinal);
    // Eleven safely isolated handler-addressable FIFO slots. Native physically owns ten at
    // +0x55130; its admitted id 10 aliases stack slot zero. ATSEEK/MVSEEK use FIFO slot zero.
    private readonly Queue<int>?[] _intQueues = new Queue<int>?[11];
    // Eleven safely isolated handler-addressable LIFO slots. Native constructs ten at +0x55158
    // on every scene reset; its admitted id 10 aliases numeric-glyph-style storage.
    private readonly Stack<int>[] _intStacks = CreateIntegerStacks();
    // Opcodes 0x06/0x08 load scripts into numbered EngineCtx frame slots and invoke them later.
    // Unlike ordinary call-script frames, native non-adjacent slots survive return with locals intact.
    private readonly Dictionary<int, PreloadedScriptSlot> _preloadedScriptSlots = new();
    public long CallScriptDispatches { get; private set; }

    public Dictionary<int, long> Globals { get; } = new();
    public Dictionary<int, long> GlobalFloats { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    public Dictionary<int, int> GlobalPointers { get; } = new();
    public Dictionary<int, int> GlobalStringPointers { get; } = new();
    public GfxState Gfx { get; } = new();
    public InputBindings InputBindings { get; } = new();
    public List<(int Offset, string Text, string Script)> Emitted { get; } = new();
    public string? HaltReason { get; private set; }
    public UnsupportedOpcodeDiagnostic? CompatibilityFailure { get; private set; }
    /// <summary>The native script error that ended the run (AGE's エラーが発生しました box), or null.</summary>
    public DiagnosticMessage? ScriptError { get; private set; }
    public long Steps { get; private set; }
    public bool AutoMessageEnabled => _autoMessageEnabled;
    public bool MessageSkipEnabled => _messageSkipEnabled;
    public int SystemMenuActionsEnabled => _systemMenuActionsEnabled;
    public uint SystemMenuShowDelayMilliseconds => _systemMenuShowDelayMilliseconds;
    /// <summary>Op 0x10f's system-key disable mask (EngineCtx +0x6da7c), read by the frontend's F1 and F10 handling.
    /// Written on the VM thread; a frontend read sees the last completed write.</summary>
    public int SystemKeyDisableMask => Volatile.Read(ref _systemKeyDisableMask);
    public string PendingDiagnosticText => _diagnosticOutput.PendingText;
    /// <summary>
    /// Zero-based active-frame cutoff selected by opcode 0x1ad, or null when no surviving marker
    /// exists. A numbered-save serializer consumes this boundary in the full payload slice.
    /// </summary>
    public int? SaveResumeFrameDepth
    {
        get
        {
            lock (_debugControlLock)
            {
                int index = _saveResumeFrame == null ? -1 : _activeExecutionFrames.IndexOf(_saveResumeFrame);
                return index >= 0 ? index : null;
            }
        }
    }
    /// <summary>True while the currently executing script frame owns a timed raw mouse/input callback
    /// loop (HISTORY/HIDEWIN/FIELD family). A registered callback in a suspended parent frame is dormant
    /// while a nested ADV scene owns the VM and must not suppress that child's ordinary page input.</summary>
    public bool IsRawInputCallbackActive
    {
        get
        {
            ExecFrame? rawInputFrame;
            lock (_interactiveLock) rawInputFrame = _rawInputFrame;
            if (rawInputFrame == null) return false;
            lock (_debugControlLock) return ReferenceEquals(rawInputFrame, _debugActiveFrame);
        }
    }
    public string? RawInputCallbackScriptName
    {
        get { lock (_interactiveLock) return _rawInputFrame?.Script.Name; }
    }
    public AdvTextHistory TextHistory { get; }

    /// <summary>The currently executing recursive script frame and stack, or null outside VM execution.</summary>
    public DebugFrameSnapshot? DebugFrame
    {
        get
        {
            lock (_debugControlLock)
                return _debugActiveFrame == null
                    ? null
                    : new DebugFrameSnapshot(_debugActiveFrameId, _debugActiveFrame.Script.Name,
                                             _activeFrameNames.ToArray());
        }
    }

    public VirtualMachine(Script s, OpcodeTable t, IHost host, VmOptions? o = null,
                          IScriptProvider? provider = null, ITraceSink? sink = null,
                          AdvTextHistory? textHistory = null, SharedProfile? sharedProfile = null,
                          INativeDatStore? nativeDatStore = null,
                          AudioMixerSettings? audioMixerSettings = null,
                          DiagnosticOutputState? diagnosticOutput = null,
                          VmCompatibilityContext? compatibility = null)
    {
        _s = s; _t = t; _host = host; _o = o ?? new VmOptions(); _provider = provider;
        _compatibility = compatibility ?? VmCompatibilityContext.ForTable(t);
        if (!string.Equals(_compatibility.EngineAbiId, t.AbiId, StringComparison.Ordinal))
            throw new ArgumentException(
                $"VM compatibility ABI '{_compatibility.EngineAbiId}' does not match opcode table ABI '{t.AbiId}'",
                nameof(compatibility));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _nativeStringEncoding = Encoding.GetEncoding(_o.NativeStringCodePage);
        _sink = sink ?? NullTraceSink.Instance; TextHistory = textHistory ?? new AdvTextHistory();
        _sharedProfile = sharedProfile ?? new SharedProfile();
        _audioMixerSettings = audioMixerSettings ?? new AudioMixerSettings();
        _diagnosticOutput = diagnosticOutput ?? new DiagnosticOutputState();
        _nativeDatStore = nativeDatStore;
        _messageWindowAlphaSetting = host.MessageWindowAlphaSetting;
        _messageGlyphDelayMilliseconds = System.Math.Max(0, host.MessageGlyphDelayMilliseconds);
        _accumulatedPlaySeconds = _sharedProfile.AccumulatedPlaySeconds;
        _sessionStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>Request a clean stop at the next opcode boundary.</summary>
    public void RequestStop() => _stopRequested = true;

    /// <summary>Host one scene the way the F4 developer launcher does, before <see cref="Run"/>. The root starts as
    /// after a return to title (so SYSTEM4 skips LOGO and OP), the coordinator returns at its entry with the entry's
    /// writes, and the run ends when the hosted scene returns (<see cref="HostedSceneEntry"/>).</summary>
    public void HostScene(HostedSceneEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.CoordinatorScript);
        _hostedSceneEntry = entry with
        {
            CoordinatorWrites = new Dictionary<int, long>(entry.CoordinatorWrites),
        };
        _hostedScenePackedId = null;
        _initialRootRun = false;
    }

    /// <summary>
    /// Return to title from outside the script: native's 終了 dialog (AGERC dialog 3) answers 2 for
    /// タイトルに戻る, and the window procedure's WM_CLOSE path (FUN_00405740) then calls
    /// op_0x9_reset_scene_and_reload_root directly. The port ends every active frame at its next opcode
    /// boundary, interrupting any host wait, and runs the same scene reset and resource-0 reload as op 0x9.
    /// </summary>
    public void RequestRootReload()
    {
        _rootReloadRequested = true;
        _host.InterruptForRootReload();
    }

    /// <summary>
    /// Match AGE's accepted-WM_CLOSE shared-profile lifecycle. The native NoSaveDat setting gates only
    /// this shutdown write; numbered-save opcode 0x19e continues to flush shared state independently.
    /// Repeated frontend teardown notifications are handled without rotating backups more than once.
    /// </summary>
    public SharedProfileShutdownFlushResult FlushSharedProfileOnShutdown()
    {
        lock (_sharedProfileShutdownLock)
        {
            if (_sharedProfileShutdownHandled)
                return new(SharedProfileShutdownFlushOutcome.AlreadyHandled);
            _sharedProfileShutdownHandled = true;

            if (_o.NoSaveDat)
                return new(SharedProfileShutdownFlushOutcome.Suppressed);
            if (_nativeDatStore == null)
                return new(SharedProfileShutdownFlushOutcome.StoreUnavailable);

            try
            {
                _sharedProfile.Save(
                    _nativeDatStore,
                    NativeSystemTime.FromLocalDateTime(DateTime.Now),
                    AccumulatedPlaySeconds());
                return new(SharedProfileShutdownFlushOutcome.Saved);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or InvalidDataException
                or ArgumentOutOfRangeException or OverflowException)
            {
                return new(SharedProfileShutdownFlushOutcome.Failed, error.Message);
            }
        }
    }

    /// <summary>Queue global writes and return only the identified active frame at its next opcode boundary.
    /// Writes are copied here and applied by the VM thread before another opcode executes.</summary>
    public bool TryRequestDebugFrameReturn(long frameId, IReadOnlyDictionary<int, long> globalWrites)
    {
        ArgumentNullException.ThrowIfNull(globalWrites);
        lock (_debugControlLock)
        {
            if (_debugActiveFrame == null || _debugActiveFrameId != frameId
                || _debugFrameReturnRequest != null) return false;
            _debugFrameReturnRequest = new DebugFrameReturnRequest(
                _debugActiveFrame, new Dictionary<int, long>(globalWrites));
            return true;
        }
    }

    /// <summary>Update the native 800x600 cursor coordinate without advancing the current ADV page.</summary>
    public void UpdatePointer(int x, int y)
    {
        bool wake;
        lock (_interactiveLock) wake = MovePointerLocked(x, y);
        if (wake) _host.WakeInputCallbackService();
    }

    /// <summary>Queue the activation callback of the armed hotspot under the pointer. True means the click was
    /// consumed.</summary>
    public bool TryActivatePointer(int x, int y)
    {
        bool consumed = false;
        lock (_interactiveLock)
        {
            // Windows delivers a WM_MOUSEMOVE before a button message at a new point. The click itself hit-tests
            // nothing: it activates the current hit, which op 0x7c may have cleared under a still pointer.
            MovePointerLocked(x, y);
            if (_interactiveFrame != null)
                consumed = _interactiveFrame.Hotspots.Activate();
        }
        if (consumed) _host.WakeInputCallbackService();
        return consumed;
    }

    /// <summary>The WM_MOUSEMOVE path: <c>age_main_window_proc</c> hit-tests a move only at a point other than the last
    /// move's (call at 0x48690f). Caller holds <see cref="_interactiveLock"/>.</summary>
    private bool MovePointerLocked(int x, int y)
    {
        if (x == _pointerX && y == _pointerY) return false;
        _pointerX = x; _pointerY = y;
        return _interactiveFrame?.Hotspots.MovePointer(x, y) == true;
    }

    /// <summary>Queue the first armed hotspot callback bound by op 0x97 to any action in
    /// <paramref name="actionMask"/>. True means the logical input was consumed.</summary>
    public bool TryActivateInputActions(int actionMask)
    {
        bool consumed;
        lock (_interactiveLock)
            consumed = _interactiveFrame?.Hotspots.ActivateBoundActions(actionMask) == true;
        if (consumed) _host.WakeInputCallbackService();
        return consumed;
    }

    /// <summary>Update one native mouse-button bit (left=0x1, right=0x2 in Himegari).</summary>
    public void UpdateMouseButtonState(int bit, bool pressed) => UpdateMaskBit(ref _mouseButtonState, bit, pressed);

    /// <summary>Accumulate a signed native mouse-wheel delta until op 0x10d consumes it.</summary>
    public void QueueMouseWheelDelta(int delta)
    {
        if (delta == 0) return;
        Interlocked.Add(ref _mouseWheelDelta, delta);
        _host.WakeInputCallbackService();
    }

    /// <summary>True while an ADV text wait polls input with no hotspot callback running (native run-state
    /// 0x80000000).</summary>
    public bool IsAdvTextWaitPolling => _advTextWaitPolling;

    /// <summary>Route one native WM_MOUSEWHEEL delta as age_main_window_proc@0x486320 and the ADV services do.
    /// <list type="bullet">
    /// <item>While an ADV text wait polls, the sign raises logical action set:WheelKeyUp/Down. The wait runs a
    /// hotspot record bound to that action by op 0x97 (both games bind wheel-up's action 8 to History). Otherwise
    /// a wheel-down advances when <paramref name="advanceMessageOnWheel"/> is set
    /// (adv_input_service_poll@0x411230), and the delta is dropped. It never reaches op 0x10d.</item>
    /// <item>During a text reveal with <paramref name="advanceMessageOnWheel"/> set, the reveal service (0x406fa0)
    /// reads and clears the accumulated delta: a negative total finishes the reveal, any other total is
    /// discarded.</item>
    /// <item>In the hidden-window wait entered from a text wait, the run state still holds the wait's 0x80000000, so
    /// the window proc raises the same action, which ends the wait (the window comes back). Entered from a control
    /// callback, the run state is 0x4000000 alone and the delta accumulates.</item>
    /// <item>Otherwise the delta accumulates for op 0x10d (HISTORY scrolls with it).</item>
    /// </list>
    /// set:ReDrawTextOnKey (CALLBACK_TEXT.BIN) is not modelled: both games set REDRAWTEXTONKEY=0.</summary>
    public MouseWheelOutcome ApplyMouseWheel(int delta, bool advanceMessageOnWheel, bool textRevealing)
    {
        if (delta == 0) return MouseWheelOutcome.Dropped;
        if (_hiddenWindowWaiting && _hiddenWindowWheelTriggers)
        {
            int action = delta > 0 ? _o.WheelKeyUpAction : _o.WheelKeyDownAction;
            if (action is >= 0 and < 32 && TryLatchAdvCoroutineYieldTrigger(1 << action))
                return MouseWheelOutcome.Activated;
            return MouseWheelOutcome.Dropped;
        }
        if (_advTextWaitPolling)
        {
            int action = delta > 0 ? _o.WheelKeyUpAction : _o.WheelKeyDownAction;
            if (action is >= 0 and < 32 && TryActivateInputActions(1 << action)) return MouseWheelOutcome.Activated;
            return delta < 0 && advanceMessageOnWheel ? MouseWheelOutcome.Advance : MouseWheelOutcome.Dropped;
        }
        if (textRevealing && advanceMessageOnWheel)
        {
            int total = Interlocked.Exchange(ref _mouseWheelDelta, 0) + delta;
            return total < 0 ? MouseWheelOutcome.Advance : MouseWheelOutcome.Dropped;
        }
        QueueMouseWheelDelta(delta);
        return MouseWheelOutcome.Accumulated;
    }

    /// <summary>Update one held logical AGE action directly. Physical frontends should use the
    /// keyboard/mouse/joystick methods below so script-configured bindings remain authoritative.</summary>
    public void UpdateInputCallbackState(int index, bool pressed)
    {
        if ((uint)index >= 32) return;
        UpdateMaskBit(ref _heldInputCallbackMask, 1 << index, pressed);
        _host.WakeInputCallbackService();
    }

    public int UpdateKeyboardVirtualKeyState(int virtualKey, bool pressed)
    {
        InputBindings.UpdateKeyboardVirtualKey(virtualKey, pressed);
        RefreshPhysicalMessageSkipState();
        _host.WakeInputCallbackService();
        return InputBindings.KeyboardAction(virtualKey);
    }

    public int UpdatePhysicalMouseButtonState(int physicalButton, bool pressed)
    {
        InputBindings.UpdateMouseButton(physicalButton, pressed);
        RefreshPhysicalMessageSkipState();
        _host.WakeInputCallbackService();
        return InputBindings.MouseAction(physicalButton);
    }

    public int UpdateJoystickButtonState(int physicalButton, bool pressed)
    {
        InputBindings.UpdateJoystickButton(physicalButton, pressed);
        RefreshPhysicalMessageSkipState();
        _host.WakeInputCallbackService();
        return InputBindings.JoystickButtonActionMask(physicalButton);
    }

    public void UpdateJoystickAxisState(int axis, double value)
    {
        InputBindings.UpdateJoystickAxis(axis, value);
        _host.WakeInputCallbackService();
    }

    /// <summary>Queue a one-shot logical action for diagnostics/tests. The native no-input callback is
    /// table index ActionCount and is selected automatically when polling returns an empty mask.</summary>
    public void QueueInputCallback(int index)
    {
        if ((uint)index >= 32) return;
        if (index == InputBindings.ActionCount)
        {
            _host.WakeInputCallbackService();
            return;
        }
        int bit = 1 << index;
        int before, after;
        do
        {
            before = Volatile.Read(ref _queuedInputCallbackMask);
            after = before | bit;
        } while (Interlocked.CompareExchange(ref _queuedInputCallbackMask, after, before) != before);
        _host.WakeInputCallbackService();
    }

    private static void UpdateMaskBit(ref int field, int bit, bool set)
    {
        int before, after;
        do
        {
            before = Volatile.Read(ref field);
            after = set ? before | bit : before & ~bit;
        } while (Interlocked.CompareExchange(ref field, after, before) != before);
    }

    /// <summary>Mirror adv_interpreter_tick's live bit-0x40 poll. The bit is logical action 6 from the
    /// process-owned binding map, not a hardcoded Ctrl test; Himegari also binds C and retains the
    /// engine's default Backspace binding. Native polls this physical channel independently of the
    /// op-0x19b/0x19c lifecycle used by persistent/read-message Skip.</summary>
    private void RefreshPhysicalMessageSkipState()
    {
        bool active = (InputBindings.PollActionMask() & 0x40) != 0;
        _host.SetPhysicalMessageSkipActive(active);
    }

    private int CurrentReadMessageIndex()
    {
        if (_cur.ReadMessageOffset < 0) return -1;
        for (int i = 0; i < _cur.Script.ReadMessageOffsets.Count; i++)
            if (_cur.Script.ReadMessageOffsets[i] == _cur.ReadMessageOffset) return i;
        return -1;
    }

    private void RefreshAdvReadSkipState()
    {
        int messageIndex = CurrentReadMessageIndex();
        _advReadSkipState = _sharedProfile.ReadMessageSkipEnabled
                            && _sharedProfile.ReadText.IsMessageRead(
                                _cur.Script.PackedId, messageIndex);
        _messageSkipServiceActive = _messageSkipEnabled || _advReadSkipState;
        _host.SetMessageSkipActive(_messageSkipServiceActive);
    }

    private static long Gi(Dictionary<int, long> d, int k) => d.TryGetValue(k, out var v) ? v : 0;
    private int GlobalPointer(int index)
        => GlobalPointers.TryGetValue(index, out int value) ? value : unchecked((int)Gi(Globals, index));
    private int GlobalStringPointer(int index)
        => GlobalStringPointers.TryGetValue(index, out int value) ? value : unchecked((int)Gi(Globals, index));
    private long ReadGlobal(int k) => Gi(Globals, k);
    private static string Gs(Dictionary<int, string> d, int k) => d.TryGetValue(k, out var v) ? v : "";
    // Ops 0x53/0x54 are native CDQ+IDIV: truncate toward zero, remainder takes the dividend's sign.
    private static long NativeDiv(long a, long b) => b == 0 ? 0 : a / b;
    private static long NativeMod(long a, long b) => b == 0 ? 0 : a % b;

    private static bool IsStr(Operand o) => o.Type is T_STR or T_GSTR or T_GSTRPTR or T_LSTR or T_LSTRPTR;
    private static bool SameOperand(Operand a, Operand b) => a.Type == b.Type && a.Value == b.Value;

    private const int CommentOpcode = 0x1a7;

    private static bool IsAdvLabeledYield(Script script, Instruction ins)
        => ins.Opcode == 0x140 && ins.Args.Count >= 4
           && ins.Args[1].Type == T_STR && ins.Args[2].Type == T_STR
           && script.GetString((int)ins.Args[1].Value) == "LABEL"
           && script.GetString((int)ins.Args[2].Value) == "J";

    /// <summary>The ADV labeled yield's terminal is the scene loop head: the nearest <c>call</c> followed by
    /// <c>exit</c> before the yield. The script's own terminal immediate (the <c>mov</c> after the yield, compared
    /// with the output) must name that instruction. A stale immediate, left by code that moved without relocating it,
    /// is reported in <paramref name="problem"/>, never echoed back as if the service had returned it. Comments
    /// (op 0x1a7, which the native handler skips) may sit anywhere in the pattern, and the immediate may name a run of
    /// them leading into the head, as a patch's notes do (Open Maid Script spec §10).</summary>
    private bool TryGetAdvYieldTerminal(int pc, Operand output, out long terminal, out string? problem)
    {
        terminal = 0;
        problem = null;
        var instructions = _cur.Script.Instructions;
        bool IsComment(int k) => instructions[k].Opcode == CommentOpcode;
        int Next(int k) { do k++; while (k < instructions.Count && IsComment(k)); return k; }
        int Previous(int k) { do k--; while (k >= 0 && IsComment(k)); return k; }
        int setAt = Next(pc), compareAt = setAt < instructions.Count ? Next(setAt) : setAt;
        if (compareAt >= instructions.Count) return false;
        var setTerminal = instructions[setAt];
        var compare = instructions[compareAt];
        if (_t.Label(setTerminal.Opcode) != "mov" || setTerminal.Args.Count < 2
            || setTerminal.Args[1].Type != T_IMM
            || _t.Label(compare.Opcode) != "eq" || compare.Args.Count < 3)
            return false;

        var terminalOperand = setTerminal.Args[0];
        bool comparesTerminalToOutput =
            (SameOperand(compare.Args[1], terminalOperand) && SameOperand(compare.Args[2], output))
            || (SameOperand(compare.Args[2], terminalOperand) && SameOperand(compare.Args[1], output));
        if (!comparesTerminalToOutput) return false;

        int head = -1;
        for (int k = Previous(pc); k > 0 && head < 0; k = Previous(k))
            if (_t.Label(instructions[k].Opcode) == "exit" && Previous(k) is int call and >= 0
                && _t.Label(instructions[call].Opcode) == "call")
                head = call;
        long immediate = Read(setTerminal.Args[1]);
        bool namesHead = head >= 0 && immediate >= 0 && immediate <= int.MaxValue
                         && _cur.Script.IndexByOffset.TryGetValue((int)immediate, out int named) && named <= head
                         && Enumerable.Range(named, head - named).All(IsComment);
        if (!namesHead)
        {
            problem = head < 0
                ? $"coroutine-yield-terminal@0x{instructions[pc].Offset:x}: no scene loop head precedes the yield"
                : $"coroutine-yield-terminal@0x{instructions[pc].Offset:x}: terminal 0x{immediate:x} is not the scene loop head 0x{instructions[head].Offset:x}";
            return false;
        }
        terminal = immediate;   // names the head, or comments that lead into it
        return true;
    }

    private long Read(Operand op) => op.Type switch
    {
        T_IMM => op.Value,
        T_GINT => ReadGlobal((int)op.Value),
        T_GFLOAT => Gi(GlobalFloats, (int)op.Value),
        T_GPTR => Gi(Globals, GlobalPointer((int)op.Value)),
        T_LINT => Gi(_cur.Locals.I, (int)op.Value),
        T_LFLOAT => Gi(_cur.Locals.F, (int)op.Value),
        T_LPTR => ReadIntCell(Ga(_cur.Locals.P, (int)op.Value)),
        _ => op.Value,
    };

    private void Write(Operand op, long val)
    {
        switch (op.Type)
        {
            case T_GINT: Globals[(int)op.Value] = val; break;
            case T_GFLOAT: GlobalFloats[(int)op.Value] = val; break;
            case T_GPTR: Globals[GlobalPointer((int)op.Value)] = val; break;
            case T_LINT: _cur.Locals.I[(int)op.Value] = val; break;
            case T_LFLOAT: _cur.Locals.F[(int)op.Value] = val; break;
            case T_LPTR: WriteIntCell(Ga(_cur.Locals.P, (int)op.Value), val); break;
        }
    }

    private string ReadStr(Operand op) => op.Type switch
    {
        T_STR => _cur.Script.GetString((int)op.Value),
        T_GSTR => Gs(GlobalStrings, (int)op.Value),
        T_GSTRPTR => Gs(GlobalStrings, GlobalStringPointer((int)op.Value)),
        T_LSTR => Gs(_cur.Locals.S, (int)op.Value),
        T_LSTRPTR => ReadStringCell(Ga(_cur.Locals.SP, (int)op.Value)),
        _ => "",
    };

    private void WriteStr(Operand op, string val)
    {
        switch (op.Type)
        {
            case T_GSTR: GlobalStrings[(int)op.Value] = val; break;
            case T_GSTRPTR: GlobalStrings[GlobalStringPointer((int)op.Value)] = val; break;
            case T_LSTR: _cur.Locals.S[(int)op.Value] = val; break;
            case T_LSTRPTR: WriteStringCell(Ga(_cur.Locals.SP, (int)op.Value), val); break;
        }
    }

    private int NativeStringByteLength(string value)
    {
        int nul = value.IndexOf('\0');
        return _nativeStringEncoding.GetByteCount(nul < 0 ? value : value[..nul]);
    }

    private string FormatIntegerForSurface(ref int x, int value, int fieldWidth, int flags)
    {
        int width = Math.Max(0, fieldWidth);
        var field = new char[Math.Max(1, width)];
        bool signed = value < 0
            || (value > 0 && (flags & 0x08) != 0)
            || (value == 0 && (flags & 0x30) != 0);
        char sign = value < 0 || (value == 0 && (flags & 0x10) == 0 && (flags & 0x20) != 0) ? '-' : '+';
        int digitSlots = width - (signed ? 1 : 0);
        int first = 0;
        long magnitude = value < 0 ? -(long)value : value;

        for (int pos = digitSlots - 1; pos >= 0; pos--)
        {
            if (pos == digitSlots - 1 || magnitude != 0 || (flags & 0x01) != 0)
            {
                field[pos + (signed ? 1 : 0)] = (char)('0' + magnitude % 10);
                first = pos;
            }
            magnitude /= 10;
        }

        if (signed)
            field[first] = digitSlots < 0 ? '#' : sign;

        int length = first;
        while (length < field.Length && field[length] != '\0') length++;
        string text = new(field, first, length - first);

        // Native uses the primary LOGFONT cell height as a fixed-width advance, halved for ASCII.
        // Odd heights (and the engine's 32/33-pixel special cases) are rounded down on font rebuild.
        int fontSize = _advTextStyle.PrimaryFontSize > 0 ? _advTextStyle.PrimaryFontSize : 24;
        int cellAdvance = fontSize == 33 ? 31
            : fontSize == 32 || (fontSize & 1) != 0 ? fontSize - 1
            : fontSize;
        if ((flags & 0x04) == 0)
        {
            int divisor = (flags & 0x10000) != 0 ? 2 : 1;
            if ((flags & 0x02) != 0) divisor *= 2;
            x += (cellAdvance * first) / divisor;
        }

        if ((flags & 0x10000) == 0)
            text = string.Concat(text.Select(c => c switch
            {
                >= '0' and <= '9' => (char)('０' + c - '0'),
                '-' => '－',
                >= 'A' and <= 'Z' => (char)('Ａ' + c - 'A'),
                >= 'a' and <= 'z' => (char)('ａ' + c - 'a'),
                _ => '？',
            }));
        return text;
    }

    private static VmAddress Ga(Dictionary<int, VmAddress> d, int k)
        => d.TryGetValue(k, out var value) ? value : VmAddress.Global(0);

    private long ReadIntCell(VmAddress address) => address.Space switch
    {
        VmAddressSpace.LocalInteger => Gi(_cur.Locals.I, address.Address),
        VmAddressSpace.LocalFloat => Gi(_cur.Locals.F, address.Address),
        _ => Gi(Globals, address.Address),
    };

    private void WriteIntCell(VmAddress address, long value)
    {
        switch (address.Space)
        {
            case VmAddressSpace.LocalInteger: _cur.Locals.I[address.Address] = value; break;
            case VmAddressSpace.LocalFloat: _cur.Locals.F[address.Address] = value; break;
            default: Globals[address.Address] = value; break;
        }
    }

    private string ReadStringCell(VmAddress address)
        => address.Space == VmAddressSpace.LocalString
            ? Gs(_cur.Locals.S, address.Address)
            : Gs(GlobalStrings, address.Address);

    private void WriteStringCell(VmAddress address, string value)
    {
        if (address.Space == VmAddressSpace.LocalString) _cur.Locals.S[address.Address] = value;
        else GlobalStrings[address.Address] = value;
    }

    private VmAddress BaseAddr(Operand op) => op.Type switch
    {
        T_LINT => VmAddress.LocalInteger((int)op.Value),
        T_LFLOAT => VmAddress.LocalFloat((int)op.Value),
        T_LSTR => VmAddress.LocalString((int)op.Value),
        T_GPTR => VmAddress.Global(GlobalPointer((int)op.Value)),
        T_GSTRPTR => VmAddress.Global(GlobalStringPointer((int)op.Value)),
        T_LPTR => Ga(_cur.Locals.P, (int)op.Value),
        T_LSTRPTR => Ga(_cur.Locals.SP, (int)op.Value),
        _ => VmAddress.Global((int)op.Value),
    };

    private bool TryResolveSharedProfileCell(Operand operand, bool isString, out int address)
    {
        bool acceptedType = isString
            ? operand.Type is T_GSTR or T_GSTRPTR or T_LSTRPTR
            : operand.Type is T_GINT or T_GPTR or T_LPTR;
        VmAddress resolved = acceptedType ? BaseAddr(operand) : default;
        if (!acceptedType || resolved.Space != VmAddressSpace.Global || resolved.Address < 0)
        {
            address = 0;
            return false;
        }
        address = resolved.Address;
        return true;
    }

    private bool TryStoreAddress(Operand destination, VmAddress address)
    {
        switch (destination.Type)
        {
            case T_LPTR: _cur.Locals.P[(int)destination.Value] = address; return true;
            case T_LSTRPTR: _cur.Locals.SP[(int)destination.Value] = address; return true;
            case T_GPTR: GlobalPointers[(int)destination.Value] = address.Address; return true;
            case T_GSTRPTR: GlobalStringPointers[(int)destination.Value] = address.Address; return true;
            default: return false;
        }
    }

    private void LookupStore(Operand dst, VmAddress addr)
    {
        if (TryStoreAddress(dst, addr)) return;
        switch (dst.Type)
        {
            default:
                if (IsStr(dst)) WriteStr(dst, ReadStringCell(addr));
                else Write(dst, ReadIntCell(addr));
                break;
        }
    }

    private void WriteConsecutive(Operand destination, int index, long value)
    {
        int address = checked((int)destination.Value + index);
        switch (destination.Type)
        {
            case T_GINT: Globals[address] = value; break;
            case T_GFLOAT: GlobalFloats[address] = value; break;
            case T_LINT: _cur.Locals.I[address] = value; break;
            case T_LFLOAT: _cur.Locals.F[address] = value; break;
            case T_GPTR:
                Globals[checked(GlobalPointer((int)destination.Value) + index)] = value;
                break;
            case T_LPTR: WriteIntCell(Ga(_cur.Locals.P, (int)destination.Value).Offset(index), value); break;
        }
    }

    private long ReadAddressedCell(Operand operand, int offset)
    {
        return operand.Type switch
        {
            T_LINT => Gi(_cur.Locals.I, checked((int)operand.Value + offset)),
            T_LFLOAT => Gi(_cur.Locals.F, checked((int)operand.Value + offset)),
            T_GINT => ReadGlobal(checked((int)operand.Value + offset)),
            T_GFLOAT => Gi(GlobalFloats, checked((int)operand.Value + offset)),
            T_LPTR => ReadIntCell(Ga(_cur.Locals.P, (int)operand.Value).Offset(offset)),
            T_GPTR => Gi(Globals, checked(GlobalPointer((int)operand.Value) + offset)),
            _ => Gi(Globals, checked((int)operand.Value + offset)),
        };
    }

    private (VmAddressSpace Space, int Address) AddressedCellIdentity(Operand operand, int offset)
        => operand.Type switch
        {
            T_LINT => (VmAddressSpace.LocalInteger, checked((int)operand.Value + offset)),
            T_LFLOAT => (VmAddressSpace.LocalFloat, checked((int)operand.Value + offset)),
            T_LPTR => PointerIdentity(Ga(_cur.Locals.P, (int)operand.Value).Offset(offset)),
            T_GPTR => (VmAddressSpace.Global,
                       checked(GlobalPointer((int)operand.Value) + offset)),
            _ => (VmAddressSpace.Global, checked((int)operand.Value + offset)),
        };

    private static (VmAddressSpace Space, int Address) PointerIdentity(VmAddress address)
        => (address.Space, address.Address);

    private string FormatSwitchValue(Operand operand)
        => IsStr(operand)
            ? ReadStr(operand)
            : unchecked((int)Read(operand)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private string FormatDiagnosticOperand(Operand operand)
    {
        if (IsStr(operand)) return ReadStr(operand);
        if (operand.Type is T_FLOAT or T_GFLOAT or T_LFLOAT)
        {
            float value = BitConverter.Int32BitsToSingle(unchecked((int)Read(operand)));
            return value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        }
        return unchecked((int)Read(operand))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private DiagnosticMessage BuildDiagnosticMessage(Instruction instruction)
        => new(DiagnosticCaption, _diagnosticOutput.PendingText + DiagnosticContext(instruction));

    private string DiagnosticContext(Instruction instruction)
    {
        // Himegari's release AGE initializes both optional debug metadata tables to null and has no
        // writer for either one. The native formatter consequently emits -1 and "-" here.
        const int sourceLine = -1;
        const string commandName = "-";
        int nativeDepth = Math.Max(0, _depth - 1);
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "\n\nデバック情報：\nFILE={0} ADDRESS={1:X} LINE={2} COMMAND={3}({4}) DEPTH={5}\n",
            _cur.Script.Name, instruction.Offset, sourceLine, commandName,
            instruction.Opcode, nativeDepth);
    }

    // age_show_message_box@0x409370 mode 7 (the errors the interpreter throws with flags 0x10007): body "%s%s"
    // followed by this prompt, MessageBoxA style 0x112 (abort/retry/ignore).
    private const string ScriptErrorPrompt =
        "\n\nプログラムを続行しますか？\n\n中止：プログラムを終了します．\n再試行：もう一度このコマンドを実行します．\n"
        + "        (ただし正常に動作しない場合があります)\n無視：エラーを無視しプログラムを続行します．\n"
        + "      (ただし正常に動作しない場合があります)\n";

    /// <summary>
    /// A native interpreter error (a C++ throw with flags 0x10007) raised while the current frame waits: AGE shows
    /// エラーが発生しました with the current FILE/ADDRESS/DEPTH and acts on the answer (engine_main_tick's policy
    /// at 0x412689, driven 2026-11-04). 中止 ends the run, as native's forced close ends the game. 再試行 restarts
    /// with nothing re-executed. 無視 advances the frame by its last decoded instruction, the wait, so the wait's
    /// successor is skipped by that many dwords. While a frame waits, native's frame pc is already past the waiting
    /// instruction, so the coordinate is the next instruction's.
    /// </summary>
    private ScriptErrorChoice PresentWaitScriptError(string text)
    {
        ScriptErrorChoice choice = ShowScriptError(text, Math.Min(_cur.Pc + 1, _cur.Script.Instructions.Count - 1));
        if (choice == ScriptErrorChoice.Ignore)
            Interlocked.Add(ref _cur.IgnoredDwords, InstructionDwords(_cur.Script, _cur.Pc));
        return choice;
    }

    /// <summary>
    /// A native interpreter error raised while the instruction at <paramref name="pc"/> executes (or, for the timed
    /// callback service, while the frame sits on it). Native's frame pc is still on that instruction, so it is the
    /// coordinate. Returns the instruction index to continue at. 再試行 restarts with the pc unchanged, so the
    /// instruction runs again. 無視 adds the frame's last decoded instruction length, which for these handlers is the
    /// instruction's own (op 0xcd sets it on entry; op 0xd5 sets 0, so its error repeats). 中止 ends the run.
    /// </summary>
    private int RaiseInstructionScriptError(string text, int pc, bool ignoreSkipsInstruction = true)
        => ShowScriptError(text, pc) switch
        {
            ScriptErrorChoice.Retry => pc,
            ScriptErrorChoice.Ignore => ignoreSkipsInstruction ? pc + 1 : pc,
            _ => throw new ScriptErrorException(),
        };

    /// <summary>"Depth が不正です <running> != <owner>": a callback registered by one script is dispatched while
    /// another runs (native throws flags 0x10007 from each guard).</summary>
    private string DepthErrorText(Script owner) => $"Depth が不正です {_cur.Script.Name} != {owner.Name}";

    /// <summary>
    /// Native callback guards compare the running frame's packed resource id with the registrant's, so two
    /// activations of one script are the same owner. Synthesized scripts carry no packed id; their names decide.
    /// </summary>
    private static bool IsSameScript(Script a, Script b)
        => ReferenceEquals(a, b)
           || (a.PackedId != 0 && b.PackedId != 0
               ? a.PackedId == b.PackedId
               : string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

    private ScriptErrorChoice ShowScriptError(string text, int coordinateIndex)
    {
        var message = new DiagnosticMessage(DiagnosticCaption,
            text + DiagnosticContext(_cur.Script.Instructions[coordinateIndex]) + ScriptErrorPrompt);
        ScriptErrorChoice choice = _host.ShowScriptError(message);
        if (choice is ScriptErrorChoice.Retry or ScriptErrorChoice.Ignore) return choice;
        ScriptError = message;
        HaltReason ??= "script-error: " + text;
        return ScriptErrorChoice.Abort;
    }

    /// <summary>An instruction's encoded length in dwords: the opcode and two per operand.</summary>
    private static int InstructionDwords(Script script, int index)
    {
        IReadOnlyList<Instruction> code = script.Instructions;
        if ((uint)(index + 1) < (uint)code.Count) return code[index + 1].Offset - code[index].Offset;
        return 1 + 2 * code[index].Args.Count;
    }

    /// <summary>Apply a pending 無視 skip once the waiting instruction completes: native resumes that many dwords
    /// past the successor. An offset inside or past the code has no port equivalent (native decodes whatever is
    /// there), so the run halts.</summary>
    private int ApplyIgnoredDwords(ExecFrame frame, int next)
    {
        if (Volatile.Read(ref frame.IgnoredDwords) == 0 || (uint)next >= (uint)frame.Script.Instructions.Count)
            return next;
        int offset = frame.Script.Instructions[next].Offset + Interlocked.Exchange(ref frame.IgnoredDwords, 0);
        if (frame.Script.IndexByOffset.TryGetValue(offset, out int index)) return index;
        HaltReason ??= $"script-error-ignored: {frame.Script.Name} resumed at 0x{offset:x}, outside its instructions";
        return HALT;
    }

    private sealed class RootReloadRequestedException : Exception { }
    private sealed class NumberedRestoreRequestedException : Exception { }
    private sealed class ProcessExitRequestedException : Exception { }
    private sealed class ScriptErrorException : Exception { }
    private sealed record DebugFrameReturnRequest(ExecFrame Frame, IReadOnlyDictionary<int, long> GlobalWrites);
    private sealed record PreloadedScriptSlot(long ScriptId, ExecFrame Frame);
    private enum FrameOutcome { Returned, DebugReturned, RootReload, ExitRequested, Halted, RanOff }

    public void Run(int entryOffset = 0)
    {
        Script root = _s;
        int rootEntry = root.IndexByOffset.TryGetValue(entryOffset, out var idx) ? idx : 0;
        FrameCause cause = FrameCause.TopScene;
        NativeSavedScriptFrame? restoredRootFrame = null;
        while (true)
        {
            FrameOutcome outcome;
            try
            {
                ExecFrame rootFrame = restoredRootFrame == null
                    ? new ExecFrame(root, rootEntry)
                    : CreateRestoredFrame(root, restoredRootFrame);
                restoredRootFrame = null;
                outcome = RunFrame(rootFrame, cause);
            }
            catch (ScriptErrorException)
            {
                outcome = FrameOutcome.Halted;
                break;
            }
            catch (NumberedRestoreRequestedException)
            {
                if (_loadedNumberedState == null) throw;
                // Native also arms the yield from the engine (adv_input_dispatch_callbacks, adv_input_service_poll and
                // engine_main_tick set the resume and its owner to the running frame), which the port does not model.
                // The port's restored scenes take op 0x7c's cold path, so a yield saved before the load must not
                // survive it.
                ResetCoroutineYield();
                Script? callback = _provider?.GetByName("CALLBACK_LOAD.BIN");
                if (callback != null)
                {
                    int callbackEntry = callback.IndexByOffset.TryGetValue(0, out int ci) ? ci : 0;
                    FrameOutcome callbackOutcome = RunFrame(
                        new ExecFrame(callback, callbackEntry), FrameCause.SaveRestore, callback.PackedId);
                    if (callbackOutcome is FrameOutcome.Halted or FrameOutcome.ExitRequested)
                    {
                        outcome = callbackOutcome;
                        break;
                    }
                    if (callbackOutcome == FrameOutcome.RootReload)
                    {
                        _loadedNumberedState = null;
                        Script? reloadedRoot = ReloadRootOrNull();
                        if (reloadedRoot == null) break;
                        root = reloadedRoot;
                        rootEntry = root.IndexByOffset.TryGetValue(0, out int lri) ? lri : 0;
                        cause = FrameCause.RootReload;
                        continue;
                    }
                }
                root = ResolveSavedScript(_loadedNumberedState.Frames[0]);
                rootEntry = 0;
                _restoreFrameIndex = 0;
                restoredRootFrame = _loadedNumberedState.Frames[0];
                cause = FrameCause.SaveRestore;
                continue;
            }
            if (outcome == FrameOutcome.RootReload)
            {
                // Native 0x9 performs the scene reset before attempting the resource-0 load. Keep
                // that ordering even when a diagnostic provider cannot resolve the root script.
                var reloaded = ReloadRootOrNull();
                if (reloaded == null) break;
                root = reloaded;
                rootEntry = root.IndexByOffset.TryGetValue(0, out int ri) ? ri : 0;
                cause = FrameCause.RootReload;
                continue;
            }
            if (outcome == FrameOutcome.RanOff) HaltReason ??= "pc-out-of-range";
            else if (outcome is FrameOutcome.Returned or FrameOutcome.DebugReturned) HaltReason ??= "exit";
            else if (outcome == FrameOutcome.ExitRequested) HaltReason ??= "exit-request";
            // Halted: HaltReason already set by the halting op.
            break;
        }
        _sink.Emit(TraceEvent.Halt(HaltReason ?? "unknown", Steps));
    }

    private Script? ReloadRootOrNull()
    {
        ResetSceneContextForRootReload();
        var reloaded = _provider?.GetById(0);
        if (reloaded == null) HaltReason ??= "root-reload-unresolved:0x0";
        return reloaded;
    }

    private void ResetSceneContextForRootReload()
    {
        _rootReloadRequested = false;
        _initialRootRun = false;   // op_0x9_reset_scene_and_reload_root: SYSTEM4 skips LOGO/OP from now on
        Gfx.ResetSceneContext();
        _valueSwitchTargets.Clear();
        _preloadedScriptSlots.Clear();
        ResetIntegerStacks();
        // scene_context_init_reset@0x40b3b0 clears coroutine_runstate, the timed sequence and every callback owner
        // (-1). It keeps the 0xcc target, which no script dispatches before registering again; the port clears it
        // with its owner.
        ResetCoroutineYield();
        ResetTimedCallbackSequence();
        _mouseCallbackTarget = -1;
        _mouseCallbackOwner = null;
        lock (_interactiveLock)
        {
            _interactiveFrame = null;
            _rawInputFrame = null;
            _mouseButtonState = 0;
            _mouseWheelDelta = 0;
            _heldInputCallbackMask = 0;
            _queuedInputCallbackMask = 0;
        }
        lock (_debugControlLock)
        {
            _debugActiveFrame = null;
            _debugActiveFrameId = 0;
            _debugFrameReturnRequest = null;
            _saveResumeFrame = null;
        }
        // A return to title reloads the root the natural way: TITLE runs, and nothing ends the run at a scene's return.
        _hostedSceneEntry = null;
        _hostedScenePackedId = null;
        _autoMessageEnabled = false;
        _autoVoicePending = false;
        _voiceMuteNextFlags = 0;
        _statementBlockActive = false;   // a root reload abandons every open block with its frames
        _statementBlockDepth = 0;
        _messageSkipEnabled = false;
        _messageSkipServiceActive = false;
        _advReadSkipState = false;
        _skipClickCancelState = 0;
        _skipClickCancelTriggers = 0;
        _settingCallbackPending = false;
        _menuToggles.Clear();
        _menuTogglesPending = false;
        _advSkipServiceEnabled = true;
        _advTextStyle = AdvTextStyle.Default;
        _systemMenuActionsEnabled = 1;
        _systemMenuShowDelayMilliseconds = 0;
        Volatile.Write(ref _systemKeyDisableMask, 0);
        TextHistory.SetRecordingEnabled(true);
        _host.SetMessageSkipActive(false);
        _host.SetPhysicalMessageSkipActive(false);
        _host.ResetSceneContext();
        RefreshPhysicalMessageSkipState();
    }

    private static Stack<int>[] CreateIntegerStacks()
    {
        var stacks = new Stack<int>[11];
        for (int i = 0; i < stacks.Length; i++)
            stacks[i] = new Stack<int>(0x100);
        return stacks;
    }

    private void ResetIntegerStacks()
    {
        for (int i = 0; i < _intStacks.Length; i++)
            _intStacks[i] = new Stack<int>(0x100);
    }


    private FrameOutcome RunFrame(ExecFrame frame, FrameCause cause, long callId = 0)
    {
        ExecFrame? previousInteractiveFrame;
        ExecFrame? previousRawInputFrame;
        lock (_interactiveLock)
        {
            previousInteractiveFrame = _interactiveFrame;
            previousRawInputFrame = _rawInputFrame;
        }
        var prev = _cur; _cur = frame; _depth++;
        ExecFrame? previousDebugActiveFrame;
        long previousDebugActiveFrameId;
        lock (_debugControlLock)
        {
            previousDebugActiveFrame = _debugActiveFrame;
            previousDebugActiveFrameId = _debugActiveFrameId;
            _debugActiveFrame = frame;
            _debugActiveFrameId = ++_debugNextFrameId;
            _activeFrameNames.Add(frame.Script.Name);
            _activeExecutionFrames.Add(frame);
        }
        bool hostContextEntered = false;
        try
        {
            _host.EnterScriptContext(frame.Script.Name);
            hostContextEntered = true;
            _sink.Emit(TraceEvent.FrameEnter(frame.Script.Name, _depth, cause, callId));
            var outcome = FrameOutcome.RanOff;
            int pc = frame.Pc;
            bool hostedScene = false;
            if (cause == FrameCause.CallScript)
            {
                if (TryReturnHostedCoordinator(frame))
                {
                    _sink.Emit(TraceEvent.FrameExit(frame.Script.Name, _depth, FrameOutcome.DebugReturned.ToString()));
                    return FrameOutcome.DebugReturned;
                }
                if (_hostedScenePackedId == callId)
                {
                    _hostedScenePackedId = null;
                    hostedScene = true;
                }
            }
            try
            {
                while (pc >= 0 && pc < frame.Script.Instructions.Count)
                {
                    if (_stopRequested) { outcome = FrameOutcome.ExitRequested; break; }
                    if (_rootReloadRequested) { outcome = FrameOutcome.RootReload; break; }
                    if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; outcome = FrameOutcome.Halted; break; }
                    Steps++;
                    frame.Pc = pc;
                    if (ServiceInstructionBoundary() is { } boundaryOutcome)
                    {
                        outcome = boundaryOutcome;
                        break;
                    }
                    if (_sink.TracingSteps) _sink.Emit(TraceEvent.Step(pc, frame.Script.Instructions[pc], _depth));
                    int next = ApplyIgnoredDwords(frame, Step(frame.Script.Instructions[pc], pc));
                    _host.FrameYield();
                    PaceFastForwardOp();
                    if (next == FRAME_RETURN) { outcome = FrameOutcome.Returned; break; }
                    if (next == ROOT_RELOAD) { outcome = FrameOutcome.RootReload; break; }
                    if (next == HALT) { outcome = FrameOutcome.Halted; break; }
                    if (TryConsumeDebugFrameReturn(frame)) { outcome = FrameOutcome.DebugReturned; break; }
                    pc = next;
                }
            }
            catch (RootReloadRequestedException) { outcome = FrameOutcome.RootReload; }
            catch (ProcessExitRequestedException) { outcome = FrameOutcome.ExitRequested; }
            _sink.Emit(TraceEvent.FrameExit(frame.Script.Name, _depth, outcome.ToString()));
            // A hosted scene's return ends the run, as a root scene's exit did: the scene is the run's unit.
            if (hostedScene && outcome is FrameOutcome.Returned or FrameOutcome.RanOff)
            {
                HaltReason ??= outcome == FrameOutcome.Returned ? "exit" : "pc-out-of-range";
                return FrameOutcome.Halted;
            }
            return outcome;
        }
        finally
        {
            lock (_debugControlLock)
            {
                if (ReferenceEquals(_debugFrameReturnRequest?.Frame, frame)) _debugFrameReturnRequest = null;
                if (ReferenceEquals(_saveResumeFrame, frame)) _saveResumeFrame = null;
                if (_activeFrameNames.Count > 0) _activeFrameNames.RemoveAt(_activeFrameNames.Count - 1);
                if (_activeExecutionFrames.Count > 0)
                    _activeExecutionFrames.RemoveAt(_activeExecutionFrames.Count - 1);
                _debugActiveFrame = previousDebugActiveFrame;
                _debugActiveFrameId = previousDebugActiveFrameId;
            }
            lock (_interactiveLock)
            {
                if (cause == FrameCause.CallScript)
                    _interactiveFrame = previousInteractiveFrame?.Hotspots.Armed == true
                        ? previousInteractiveFrame : null;
                else if (ReferenceEquals(_interactiveFrame, frame))
                    _interactiveFrame = null;
                if (ReferenceEquals(_rawInputFrame, frame)) _rawInputFrame = previousRawInputFrame;
            }
            try
            {
                if (hostContextEntered) _host.ExitScriptContext();
            }
            finally
            {
                _cur = prev; _depth--;
            }
        }
    }

    /// <summary>The coordinator of a <see cref="HostScene"/> run returns before its first instruction, as TITLE's
    /// developer menu returns its choice, and arms the end of the run at the scene's return.</summary>
    private bool TryReturnHostedCoordinator(ExecFrame frame)
    {
        if (_hostedSceneEntry is not { } entry
            || !frame.Script.Name.Equals(entry.CoordinatorScript, StringComparison.OrdinalIgnoreCase))
            return false;
        _hostedSceneEntry = null;
        foreach (var (address, value) in entry.CoordinatorWrites)
            Globals[address] = value;
        _hostedScenePackedId = entry.ScenePackedId;
        return true;
    }

    private bool TryConsumeDebugFrameReturn(ExecFrame frame)
    {
        if (Volatile.Read(ref _debugFrameReturnRequest) is not { } pending
            || !ReferenceEquals(pending.Frame, frame)) return false;
        lock (_debugControlLock)
        {
            if (!ReferenceEquals(_debugFrameReturnRequest?.Frame, frame)) return false;
            foreach (var (address, value) in _debugFrameReturnRequest.GlobalWrites)
                Globals[address] = value;
            _debugFrameReturnRequest = null;
            return true;
        }
    }

    private NativeNumberedSaveState CaptureNumberedState()
    {
        NativeSaveBankDimensions dimensions = _nativeDatStore?.Identity.BankDimensions
            ?? throw new InvalidDataException(
                "The selected native persistence identity has no numbered-save bank dimensions.");

        ExecFrame[] active;
        int cutoff;
        lock (_debugControlLock)
        {
            active = _activeExecutionFrames.ToArray();
            cutoff = _saveResumeFrame == null ? active.Length - 1 : Array.IndexOf(active, _saveResumeFrame);
        }
        if (cutoff < 0)
            throw new InvalidDataException("No active script frame is available for a numbered save.");

        var frames = new NativeSavedScriptFrame[cutoff + 1];
        for (int i = 0; i <= cutoff; i++)
        {
            ExecFrame frame = active[i];
            NativeSavedScriptFrame? restored = frame.RestoredSaveFrame;
            // Native looks each live return address up in T3 and stops at the first that is not a
            // T3 site (context_state_serialize@0x40d320); later entries are not saved.
            int[] liveReturns = frame.CallStack
                .Select(returnPc => (uint)returnPc < (uint)frame.Script.Instructions.Count
                    ? FindTableIndex(frame.Script.LocalCallOffsets,
                        frame.Script.Instructions[returnPc].Offset - 3)
                    : -1)
                .ToArray();
            int[] returns = restored?.ReturnIndices.ToArray()
                ?? liveReturns.TakeWhile(index => index >= 0).ToArray();
            bool stoppedAtUnmatched = restored?.StoppedAtUnmatchedReturn
                ?? returns.Length < liveReturns.Length;
            int resumeIndex = restored?.ResumeIndex ?? CurrentReadMessageIndex(frame);
            int callTargetIndex = i == cutoff
                ? -1
                : restored?.CallTargetIndex
                    ?? ((uint)frame.Pc >= (uint)frame.Script.Instructions.Count
                        ? -1
                        : FindTableIndex(
                            frame.Script.ScriptCallOffsets,
                            frame.Script.Instructions[frame.Pc].Offset));
            frames[i] = new NativeSavedScriptFrame(
                i - 1, frame.Script.PackedId, returns, resumeIndex, callTargetIndex)
            {
                StoppedAtUnmatchedReturn = stoppedAtUnmatched,
            };
        }

        int gfxRecordSize = _nativeDatStore.Identity.NumberedGfxRecordSize;
        NativeNumberedSaveState basis = _retainedNativeNumberedState
            ?? NativeNumberedSaveCodec.Empty(frames, gfxRecordSize);
        var gfx = NativeGfxPersistenceCodec.Capture(Gfx, gfxRecordSize);
        return basis with
        {
            BgmTrackId = unchecked((int)_currentBgmTrackId),
            SoundEffectResourceIds = _loadedSoundEffectResourceIds
                .Select(id => unchecked((int)id)).ToArray(),
            Frames = frames,
            IntegerGlobals = DenseValues(Globals, dimensions.IntegerGlobals),
            FloatGlobals = DenseValues(GlobalFloats, dimensions.FloatGlobals),
            StringGlobals = DenseStrings(GlobalStrings, dimensions.StringGlobals),
            PointerGlobals = DensePointerValues(GlobalPointers, dimensions.PointerGlobals),
            PointerStrings = DensePointerValues(GlobalStringPointers, dimensions.PointerStrings),
            LocalPointerScratch = new int[dimensions.LocalPointerScratch],
            SurfaceRecords = gfx.SurfaceRecords,
            GfxObjects = gfx.Objects,
            RangeTransformFirst = gfx.RangeFirst,
            RangeTransformCount = gfx.RangeCount,
            RangeTransformRecord = gfx.RangeRecord,
            GraphicsRecordSize = gfxRecordSize,
        };
    }

    private bool TryLoadNumberedState(int slot, bool restoreHistory)
    {
        if (_nativeDatStore == null) return false;
        try
        {
            NativeNumberedSaveFile? file = _nativeDatStore.LoadNumberedFile(slot);
            if (file == null) return false;
            NativeNumberedSaveState state = NativeNumberedSaveCodec.Decode(
                file.Document.Payload, _nativeDatStore.Identity.NumberedGfxRecordSize);
            ApplyNumberedState(state);
            _retainedNativeNumberedState = state;
            _accumulatedPlaySeconds = file.Document.Metadata.AccumulatedPlaySeconds;
            if (restoreHistory)
            {
                if (file.HistoryTail.Length == 0) TextHistory.Clear();
                else NativeTextHistoryCodec.DecodeInto(file.HistoryTail, TextHistory);
                _loadedNumberedState = state;
            }
            else
            {
                _loadedNumberedState = null;
                _restoreFrameIndex = -1;
            }
            return true;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentOutOfRangeException or OverflowException)
        {
            _loadedNumberedState = null;
            _restoreFrameIndex = -1;
            return false;
        }
    }

    private void ApplyNumberedState(NativeNumberedSaveState state)
    {
        // Layout 3 of save_data_deserialize_and_begin_restore@0x40fd10 zeroes each global bank in full, to its live
        // size (ctx+0x53c7c..0x53c90; Himegari 1419442 integers and 18180 strings), and only then copies the saved
        // prefixes (402459 integers, 789 strings). The cells past a prefix are therefore zero after any load until a
        // restored frame re-authors them: every saved script re-runs its prologue from its entry to op 0xae, and
        // SYSTEM4's (LOADCONFIG or INITCONFIG, the 0x1ca table copy, INIT2 and its *INIT data tables) precedes its 0xae.
        // Script flags past the prefix such as G[0x62428], which SC0000 sets around its MENU.BIN call and a load from
        // MENU never clears, read zero again (native-read 2026-11-05; docs/engine-re.md, "Native menu bar").
        ReplaceBank(Globals, state.IntegerGlobals.Select(value => (long)value).ToArray(), value => value != 0);
        ReplaceBank(GlobalFloats, state.FloatGlobals.Select(value => (long)value).ToArray(), value => value != 0);
        ReplaceBank(GlobalStrings, state.StringGlobals, value => value.Length != 0);
        ReplaceBank(GlobalPointers, state.PointerGlobals, value => value != 0);
        ReplaceBank(GlobalStringPointers, state.PointerStrings, value => value != 0);

        _currentBgmTrackId = unchecked((uint)state.BgmTrackId);
        if (_currentBgmTrackId == 0) _host.FadeBgm(0, 0);
        else _host.PlayBgm(_currentBgmTrackId);
        for (int channel = 0; channel < _loadedSoundEffectResourceIds.Length; channel++)
        {
            _host.ReleaseSoundEffect(channel);
            long resourceId = unchecked((uint)state.SoundEffectResourceIds[channel]);
            _loadedSoundEffectResourceIds[channel] = resourceId;
            if (resourceId != 0) _host.LoadSoundEffect(resourceId, channel);
        }

        GfxPersistenceSnapshot gfxSnapshot = NativeGfxPersistenceCodec.Decode(state);
        bool releaseAllSurfaces = _o.CreateObject && _o.AutoFreeTextures;
        if (releaseAllSurfaces)
        {
            Gfx.ReleaseSurfaceRange(0, 1000);
            _host.ReleaseSurfaceRange(0, 1000);
        }
        var restoredSurfaces = Gfx.CapturePersistenceSnapshot().Surfaces
            .ToDictionary(item => item.Slot);
        foreach (GfxSurfacePersistenceState surface in gfxSnapshot.Surfaces
                     .Where(item => item.ReloadOnRestore && item.ResourceId >= 0))
            restoredSurfaces[surface.Slot] = surface;
        Gfx.RestorePersistenceSnapshot(gfxSnapshot with
        {
            Surfaces = restoredSurfaces.Values.OrderBy(item => item.Slot).ToArray(),
        });
        // The restore loop reopens each flagged record through gfx_surface_load_asset, which also rewrites
        // that slot's live record; the rest of the live and snapshot tables are not replaced by the load.
        foreach (GfxSurfacePersistenceState surface in gfxSnapshot.Surfaces
                     .Where(item => item.ReloadOnRestore && item.ResourceId >= 0))
        {
            Gfx.SetSurface(surface.Slot, surface.ResourceId, surface.ColorKey);
            _host.SetTexture(surface.ResourceId, surface.Slot, surface.ColorKey);
        }
    }

    private static void ReplaceBank<T>(Dictionary<int, T> bank, IReadOnlyList<T> values, Func<T, bool> retain)
    {
        bank.Clear();
        for (int i = 0; i < values.Count; i++)
            if (retain(values[i])) bank[i] = values[i];
    }

    private Script ResolveSavedScript(NativeSavedScriptFrame frame)
    {
        if (_s.PackedId == frame.ScriptId) return _s;
        Script? script = _provider?.GetById(frame.ScriptId);
        return script ?? throw new InvalidDataException(
            $"Numbered save references unresolved script 0x{frame.ScriptId:x}.");
    }

    private static int FindRestoreRendezvous(Script script)
    {
        for (int i = 0; i < script.Instructions.Count; i++)
            if (script.Instructions[i].Opcode == 0xae) return i;
        throw new InvalidDataException(
            $"Saved script {script.Name} has no opcode 0xae restore rendezvous.");
    }

    private static ExecFrame CreateRestoredFrame(Script script, NativeSavedScriptFrame saved)
    {
        // Native creates each saved script context at its ordinary entrypoint. The script runs its
        // local-array/constants/resource prologue and rendezvouses at 0xae itself; jumping directly
        // to 0xae leaves those frame-local tables zeroed (FIELD then collapses its map zoom to 0%).
        _ = FindRestoreRendezvous(script);
        int entry = script.IndexByOffset.TryGetValue(0, out int index) ? index : 0;
        var frame = new ExecFrame(script, entry)
        {
            RestoredSaveFrame = saved,
        };
        if ((uint)saved.ResumeIndex < (uint)script.ReadMessageOffsets.Count)
            frame.ReadMessageOffset = script.ReadMessageOffsets[saved.ResumeIndex];
        foreach (int returnIndex in saved.ReturnIndices)
        {
            if ((uint)returnIndex >= (uint)script.LocalCallOffsets.Count) continue;
            int returnOffset = checked(script.LocalCallOffsets[returnIndex] + 3);
            if (script.IndexByOffset.TryGetValue(returnOffset, out int returnPc))
                frame.CallStack.Add(returnPc);
        }
        return frame;
    }

    private static int ResolveTableOffset(
        Script script, IReadOnlyList<int> table, int index, int fallback)
    {
        if ((uint)index >= (uint)table.Count) return fallback;
        return script.IndexByOffset.TryGetValue(table[index], out int pc) ? pc : fallback;
    }

    private static int CurrentReadMessageIndex(ExecFrame frame)
    {
        for (int i = 0; i < frame.Script.ReadMessageOffsets.Count; i++)
            if (frame.Script.ReadMessageOffsets[i] == frame.ReadMessageOffset) return i;
        return -1;
    }

    private static int FindTableIndex(IReadOnlyList<int> table, int offset)
    {
        for (int i = 0; i < table.Count; i++)
            if (table[i] == offset) return i;
        return -1;
    }

    private static int[] DenseValues(IReadOnlyDictionary<int, long> source, int fixedCount)
    {
        var result = new int[fixedCount];
        foreach ((int index, long value) in source)
            if ((uint)index < (uint)result.Length) result[index] = unchecked((int)value);
        return result;
    }

    private static int[] DensePointerValues(IReadOnlyDictionary<int, int> source, int fixedCount)
    {
        var result = new int[fixedCount];
        foreach ((int index, int value) in source)
            if ((uint)index < (uint)result.Length) result[index] = value;
        return result;
    }

    private static string[] DenseStrings(IReadOnlyDictionary<int, string> source, int fixedCount)
    {
        var result = Enumerable.Repeat(string.Empty, fixedCount).ToArray();
        foreach ((int index, string value) in source)
            if ((uint)index < (uint)result.Length) result[index] = value;
        return result;
    }

    private uint AccumulatedPlaySeconds()
    {
        double elapsedSeconds = System.Diagnostics.Stopwatch.GetElapsedTime(_sessionStartTimestamp).TotalSeconds;
        return unchecked(_accumulatedPlaySeconds + (uint)Math.Min(uint.MaxValue, elapsedSeconds));
    }

    private bool ServiceHotspotCallback()
    {
        // adv_input_service_poll takes op-0x97 bound actions first, which Godot activates before it requests the
        // action-5 yield, so a pending request comes before the hover and click callbacks queued after it.
        if (Interlocked.Exchange(ref _advWaitCoroutineYieldRequested, 0) != 0 && ServiceAdvWaitCoroutineYield())
            return true;
        // A native menu command's toggle and its CALLBACK_SETTING.BIN, which native's launch queue runs from the tick.
        if (ServiceWaitSettingCallback()) return true;
        int target;
        ExecFrame owner;
        lock (_interactiveLock)
        {
            if (_interactiveFrame == null || !_interactiveFrame.Hotspots.TryDequeue(out target)) return false;
            owner = _interactiveFrame;
        }
        // Native keeps one hotspot table and records the script that registered it (+0x7494 of the table at
        // ctx+0x5248). Every hotspot dispatch from an ADV wait first checks that owner against the running frame
        // (FUN_004079b0): a script `run` from a scene whose menu bindings are still armed cannot take them, and AGE
        // stops with "Depth が不正です <running> != <owner>" (docs/engine-re.md).
        if (!ReferenceEquals(owner, _cur))
        {
            // The dequeued callback is gone either way: 再試行 and 無視 keep waiting without it, with the owner's
            // bindings still armed (natively, the next right click raises the error again).
            if (PresentWaitScriptError(DepthErrorText(owner.Script)) == ScriptErrorChoice.Abort)
                throw new ScriptErrorException();
            lock (_interactiveLock) owner.Hotspots.RearmAfterCallback(_pointerX, _pointerY);
            return false;
        }
        if (_cur.Script.IndexByOffset.TryGetValue(target, out int pc))
        {
            _cur.CallStack.Add(HOTSPOT_RETURN);
            RunWaitCallback(pc);
            // A malformed callback must not leave its sentinel in the page's ordinary local-call stack.
            int sentinel = _cur.CallStack.LastIndexOf(HOTSPOT_RETURN);
            if (sentinel >= 0) _cur.CallStack.RemoveAt(sentinel);
        }
        RearmAfterWaitCallback();
        return true;
    }

    /// <summary>The action-5 entry of adv_input_service_poll@0x411230: the waiting frame enters its op-0x7b handler A
    /// with the wait saved as the resume. The handler ends at op 0x7c (after the hidden-window wait and handler B
    /// when it takes that path), which returns here to the parked wait. Native pushes no return pc.</summary>
    private bool ServiceAdvWaitCoroutineYield()
    {
        if (_cur.CoroutineYieldHandlerA == -1
            || !_cur.Script.IndexByOffset.ContainsKey(_cur.CoroutineYieldHandlerA))
            return false;
        // The handlers run with the wait's poll stopped (native clears run-state 0x80000000), so no hover or click
        // dispatches until the frame is back on the wait; RearmAfterWaitCallback resumes it.
        lock (_interactiveLock) _interactiveFrame?.Hotspots.SuspendDispatch();
        RunWaitCallback(EnterCoroutineYieldHandlerA(_cur.Pc + 1, resumesTextWait: true));
        RearmAfterWaitCallback();
        return true;
    }

    /// <summary>Run a callback inside the instruction that waits (native AGE nests it the same way) until it returns
    /// to the wait. The frame is then back on that waiting instruction, which observers (saves, frame locations)
    /// read.</summary>
    private void RunWaitCallback(int pc)
    {
        int waitingPc = _cur.Pc;
        // Native clears run-state 0x80000000 on dispatch, so the wheel feeds op 0x10d while the callback (HISTORY)
        // runs; the callback's ret re-runs the wait, which sets it again.
        bool waitPolling = _advTextWaitPolling;
        _advTextWaitPolling = false;
        try
        {
            while (pc >= 0 && pc < _cur.Script.Instructions.Count)
            {
                if (_stopRequested) break;
                if (_rootReloadRequested) throw new RootReloadRequestedException();
                if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; break; }
                Steps++;
                _cur.Pc = pc;
                if (_sink.TracingSteps) _sink.Emit(TraceEvent.Step(pc, _cur.Script.Instructions[pc], _depth));
                int next = Step(_cur.Script.Instructions[pc], pc);
                _host.FrameYield();
                if (next == ROOT_RELOAD) throw new RootReloadRequestedException();
                if (next == HOTSPOT_RETURN || next == FRAME_RETURN) break;
                if (next == HALT) break;
                pc = next;
            }
        }
        finally
        {
            _advTextWaitPolling = waitPolling;
        }
        _cur.Pc = waitingPc;
    }

    private void RearmAfterWaitCallback()
    {
        lock (_interactiveLock)
        {
            // History/Hide callbacks cancel the active registry, run a nested script, then republish the
            // parent frame's definitions. Nested RunFrame deliberately clears the disarmed interactive
            // pointer, so restore the still-running parent before rearming its rebuilt registry.
            if (_interactiveFrame == null && _cur.Hotspots.HasDefinitions) _interactiveFrame = _cur;
            _interactiveFrame?.Hotspots.RearmAfterCallback(_pointerX, _pointerY);
        }
        _host.InputCallbackCompleted(Gfx);
    }

    private int Step(Instruction ins, int pc)
    {
        int op = ins.Opcode;
        var a = ins.Args;
        if (!_t.TryGetDefinition(op, out OpcodeDefinition definition))
        {
            HaltReason = $"opcode-not-in-abi:{_t.AbiId}:0x{op:x}";
            return HALT;
        }
        string label = definition.Label;
        if (!OpcodeRuntimeCoverage.TryResolve(_t, op, out OpcodeHandlerResolution handler)
            || !handler.IsExecutable)
        {
            var diagnostic = new UnsupportedOpcodeDiagnostic(
                _compatibility.ProfileId,
                _compatibility.EngineAbiId,
                _cur.Script.EngineRevision,
                _cur.Script.Name,
                _cur.Script.PackedId,
                ins.Offset,
                op,
                definition.CanonicalLabel,
                definition.ContractProvenance.LayerId,
                ins.Args.ToArray());
            _sink.Emit(TraceEvent.UnsupportedOpcode(diagnostic, pc));
            if (_compatibility.ProbeMode) return pc + 1;
            CompatibilityFailure = diagnostic;
            HaltReason = diagnostic.ToString();
            return HALT;
        }
        switch (label)
        {
            case "script-entry":
            case "scale-copy-surface-rect": // 0x32: clipped source rectangle scaled into destination rectangle
            case "set-surface-persistence-flags": // 0x258 (slot)(flags): bit 0 = numbered-load reload
            case "u00423120": // opcode 0x25a
            case "set-display-background-color":
                return StepSurface(label, a, pc);
            case "add":
            case "sub":
            case "mul":
            case "div":
            case "mod":
            case "and":
            case "or":
            case "sar":
            case "shl":
            case "eq":
            case "ne":
            case "string-equals":
            case "string-not-equals":
            case "concat":
            case "toString":
            case "absolute-value":
                return StepValue(label, a, pc);
            case "get-monotonic-time-ms":
                return StepTiming(label, a, pc);
            case "lt":
            case "lte":
            case "gr":
            case "gre":
            case "mov":
            case "set-string":
            case "halve-strlen": // 0x1a6: strlen(native encoded bytes) >> 1
            case "edit-fullwidth-string-dialog": // 0x144: blocking AGERc command-10 editor
            case "cp932-character-length": // 0x2c6: Japanese-locale _mbstrlen
            case "cp932-substring": // 0x2c8: multibyte-character interval [start,start+count)
                return StepValue(label, a, pc);
            case "u00425790": // upstream ABI label
            case "append-diagnostic-value": // 0x1b2: generic operand text -> EngineCtx accumulator
            case "u004257D0": // upstream ABI label
            case "append-diagnostic-newline": // 0x1b3: exact native CRLF bytes
            case "u004237C0": // upstream ABI label
            case "show-and-clear-diagnostic": // 0x1b4: synchronous host prompt, then erase
                return StepDiagnostic(label, ins, pc);
            case "is-catalog-resource-unlocked":
            case "save-numbered-slot": // 0x19e
            case "load-numbered-slot-data-only": // 0x19f
            case "load-numbered-slot-and-resume": // 0x1a1
                return StepPersistence(label, a, pc);
            case "continue-save-load-stack-restore": // 0xae
            case "query-numbered-save-metadata": // 0x1a0
                return StepPersistence(label, a, pc);
            case "delete-numbered-save": // 0x1ab
            case "copy-numbered-save": // 0x1ac
            case "mark-save-resume-frame": // 0x1ad
            case "write-numbered-save-thumbnail": // 0x1ae
            case "load-numbered-save-thumbnail": // 0x1af
                return StepPersistence(label, a, pc);
            case "store-shared-profile-int": // 0x1a2
            case "load-shared-profile-int": // 0x1a3
            case "store-shared-profile-string": // 0x1a9
            case "load-shared-profile-string": // 0x1aa
                return StepPersistence(label, a, pc);
            case "strlen":
            case "lookup-array":
            case "lookup-array-2d":
            case "take-address": // 0x63: pointer destination <- underlying address of operand 2
            case "copy-inline-int-array": // 0x64: count dword followed by plain file values
            case "copy-dwords": // 0x1b0: memcpy(count * 4) across resolved integer-cell spans
            case "set-array-to": // upstream ABI label for SYS4433 opcode 0x2d8
            case "fill-int-array": // 0x2d8: fill count consecutive resolved integer cells
                return StepMemoryCollection(label, a, pc);
            case "find-hit-rectangle": // 0x12e: inclusive rectangle intersection over addressed arrays
            case "u0041E940":
            case "sort-indices-by-key-sum": // 0x12f: stable ascending permutation by signed key sum
            case "u0041ECB0":
            case "point-in-polygon": // 0x147: alternate-fill polygon membership
                return StepMemoryCollection(label, a, pc);
            case "u0041EF00":
            case "reset-int-queue": // 0x132: 11 safe logical slots; native's admitted id 10 aliases stack 0
            case "u0041EFF0":
            case "enqueue-int": // 0x133 (queue_id, value)
            case "u0041F050":
            case "try-dequeue-int": // 0x134 (queue_id, out_success, out_value)
            case "u0041F1C0":
            case "reset-int-stack": // 0x137 (stack_id)
            case "u0041F2B0":
            case "push-int-stack": // 0x138 (stack_id, value)
            case "u0041F310":
            case "try-pop-int-stack": // 0x139 (stack_id, out_success, out_value)
                return StepMemoryCollection(label, a, pc);
            case "bit-set":
            case "bit-reset":
            case "check-bit":
            case "zero-int-range":
            case "copy-to-global": // pre-reference compatibility for opcode 0x6c
            case "random-modulo": // 0x60: native CRT rand() % bound
            case "u0041A270":
                return StepMemoryCollection(label, a, pc);
            case "jmp":
            case "call":
            case "ret":
            case "jcc":
            case "begin-value-switch":
            case "add-value-switch-case":
            case "value-switch-jump":
            case "u0041ADB0":
            case "coroutine-save-yield-handlers":   // 0x7b: retain native handler metadata
            case "u00414D50":
            case "yield-adv-coroutine":             // 0x199: A -> nested service -> B -> 0x7c resume
            case "u00416A90":
            case "coroutine-resume":                // 0x7c: restore the PC saved by op 0x199
            case "u0041F9C0":
            case "coroutine-label-yield":           // 0x140: bounded host model for LABEL/J only
                return StepControlFlow(label, ins, pc);
            case "throw-exit-request":
            case "exit":
            case "exit-script":
            case "call-script":
            case "u00415FB0":
            case "run-mounted-append-autoruns": // 0x143: selector slots 1..255, packed record zero
            case "u00417E80":
            case "preload-script-slot": // 0x06 (script_id, frame_slot), valid slots 0..39
            case "u00417FC0":
            case "call-preloaded-script-slot": // 0x08 (frame_slot)
                return StepScriptLifecycle(label, a, pc);
            case "show-text":
            case "define-adv-text-layout":
            case "reset-adv-text-layout":
            case "set-adv-text-reset-cursor":
            case "set-adv-text-cursor":
            case "set-adv-text-bounds":
            case "configure-adv-wait-indicator":
            case "u0041B9F0":
            case "set-adv-wait-indicator-enabled": // 0x1ce: explicit marker service start/stop
            case "u00420CE0":
            case "publish-adv-text-layout": // 0x20a: publish layout and current marker frame if active
            case "draw-string":
            case "u00420A60":                 // pre-reference compatibility
            case "draw-formatted-integer":   // 0x205 (surface slot, x, y, value, field width, flags)
                return StepAdvText(label, ins, pc);
            case "wait-for-input":
            case "u0041BEB0":
            case "register-hotspot-callbacks": // 0x90: inclusive rect + enter/leave/activate local callbacks
            case "u00415040":
            case "cancel-hotspot-wait": // 0x93
            case "u00415090":
            case "arm-hotspot-wait": // 0x94
            case "u0041C150":
            case "bind-hotspot-key": // 0x97: bind a configured logical action to this record
            case "u0041B210":
            case "set-cursor-resource": // 0x86: raw indexed .CUR resource
            case "u00414D10":
            case "clear-cursor-resource": // 0x87
                return StepInput(label, a, pc);
            case "mouse_callback":
            case "register-mouse-callback": // 0xcc (poll interval ms, local target dword offset)
            case "get-input-type":
            case "dispatch-mouse-callback": // 0xcd
            case "joy_callback":
            case "register-joy-callback": // 0xfb (input index, local target dword offset)
            case "get-joystick-button-count": // 0x106: active-device physical-button capability
            case "u0041E360":
            case "set-input-action-count": // 0xfe: actions [0,count), no-input callback at count
            case "u00415A10":
            case "poll-joy-callback-input": // 0xff
            case "u00415A60":
            case "dispatch-joy-callbacks": // 0x100
                return StepInput(label, a, pc);
            case "u0041E500":
            case "map-joystick-button": // 0x107: button slot N emits action N+4
            case "u00415E70":
            case "get-mouse-button-state": // 0x108
            case "u00415F10":
            case "consume-mouse-wheel-delta": // 0x10d
            case "u00415EC0":
            case "get-cursor-virtual": // 0x109
            case "u0041E540":
            case "set-cursor-virtual": // 0x10a; retain the virtual position even without OS warping
            case "u0041E5A0":
            case "map-mouse-button": // 0x10b: physical button -> slot, polled action is slot+4
            case "u0041E5E0":
            case "map-keyboard-scancode": // 0x10c: logical action <- DIK translated through native VK table
                return StepInput(label, a, pc);
            case "sleep":
            case "u00425960":
            case "begin-timed-callback-sequence": // 0xd3: clear the frame-local relative schedule
            case "u004266F0":
            case "append-relative-timed-callbacks": // 0xd4: (interval ms, count, on-time PC, catch-up PC)
            case "u004262C0":
            case "run-timed-callback-sequence": // 0xd5: dispatch each scheduled local callback, resuming here after ret
                return StepTiming(label, a, pc);
            case "u0041B290":
            case "set-message-skip": // 0x88: persistent all-message fast-forward service state
            case "refresh-adv-skip-input-state": // 0xfa: reconcile physical/read skip services
            case "u00414E50": // 0x19a: persistent state used by the SO001 active overlay
            case "u00414E80":
            case "suspend-adv-skip-service": // 0x19b: preserve the toggle while leaving ADV presentation
            case "u00414EC0":
            case "resume-adv-skip-service": // 0x19c: recompute active fast-forward on ADV entry
            case "get-message-skip": // 0x1c7: persistent Skip or host-supplied Ctrl fast-forward
            case "get-adv-read-skip-state": // 0x1cc: per-message read/click skip service state
            case "get-adv-service-state":   // compatibility with pre-recovery generated tables
            case "u0041B9B0":
            case "set-read-message-skip": // 0x1ca: engine setting message:ReadTextSkip
            case "u00414FD0":
            case "get-read-message-skip": // 0x1cb
            case "u00414F60":
            case "get-auto-message": // 0x1b6: VM service state used by the ADV redraw callback
            case "u0041B640":
            case "set-auto-message": // 0x1b7
            case "u0041B670":
            case "get-auto-message-time": // 0x1b8 (selector 0=post-voice Time0, 1=unvoiced Time1, out)
            case "u0041B710":
            case "set-auto-message-time": // 0x1b9 (selector, milliseconds)
            case "u00415670":
            case "block-mark":
            case "reset-message-voice-state": // 0x1bc resets native per-message voice/queued-voice state
                return StepAdvService(label, a, pc);
            case "set-text-history-recording":
            case "append-text-history-metadata":
            case "step-text-history":
            case "query-current-text-redraw": // 0x83: direction plus current retained entry
            case "redraw-retained-text-group": // 0x82: ordinary ADV redraw after CONFIG returns
            case "render-text-history": // 0x1d1: rasterize/bind one retained group to a target layout
            case "u0041BAE0":
            case "u0041BB90":
            case "find-text-history-value": // 0x1d3: operand 3 is accepted but ignored natively
            case "u0041BC00":
            case "find-text-history-pair": // 0x1d4: operand 3 is accepted but ignored natively
            case "clear-text-history":
                return StepTextHistory(label, a, pc);
            case "set-font-size":
            case "set-ruby-font-size":
            case "u0041B3D0":
            case "set-text-line-spacing":
            case "set-font-bold":
            case "set-text-color":
            case "set-text-effect-color":
            case "set-text-render-mode":
            case "u004235C0": // SYS4433 opcode 0x2db
            case "set-text-aspect-mode":
            case "u0042BA80": // SYS4433 opcode 0x2dc
            case "get-font-family-count":
            case "u0042D880": // SYS4433 opcode 0x2dd
            case "get-font-family-name":
            case "u0042BAC0": // SYS4433 opcode 0x2de
            case "find-font-family-index":
            case "set-text-effect-offset":
            case "set-adv-text-layout-origin": // 0x198; slot 0 selects the current layout
            case "u0041B540":
                return StepAdvText(label, ins, pc);
            case "get-message-window-alpha": // 0x131: process-owned message:MesWinAlpha setting
            case "u00415F70":
            case "set-message-window-alpha": // 0x141: paired message:MesWinAlpha configuration setter
            case "u0041FAA0":
            case "set-system-menu-enabled": // 0x142: native AGERC menu reentrancy guard
            case "u0041FB10": // pre-reference compatibility
            case "get-system-menu-show-delay": // 0x148: paired TIMER_SHOWMENU getter
            case "u004160A0": // pre-reference compatibility
            case "set-system-menu-show-delay": // 0x149: top-edge dwell threshold in milliseconds
            case "u0041FCE0": // pre-reference compatibility
            case "set-system-key-disable-mask": // 0x10f: F1 screenshot / F10 menu-focus disable bits
            case "u0041E690": // pre-reference compatibility
                return StepRuntimeSetting(label, a, pc);
            case "get-message-glyph-delay": // 0x7f
            case "u00414C60":
            case "set-message-glyph-delay": // 0x1b5
            case "u0041B5F0":
                return StepAdvText(label, ins, pc);
            case "u00415BF0":
            case "reset-message-skip-input": // 0x101 clears transient input/run bits, not op 0x88 state
                return StepAdvService(label, a, pc);
            case "end-text-line":
            case "u00423020":
            case "configure-adv-wait-indicator-grid":
            case "u0041B5C0":
            case "set-adv-wait-indicator-follow-last-glyph":
            case "set-font":
            case "comment": case "display-furigana": case "dev_ukn":
                return StepAdvText(label, ins, pc);
            case "create-texture":
            case "set-texture":
            case "u00422E80":                         // pre-reference compatibility
            case "set-tiled-surface-edge-length":    // 0x248 (edge pixels)
            case "u00422EB0":                 // pre-reference compatibility
            case "load-raw-texture-surface":  // 0x249 (packed resource id)(slot)(colorkey)
            case "draw-texture":
                return StepSurface(label, a, pc);
            case "u0041F3A0":
            case "register-numeric-glyph-style": // 0x13a: (style)(surface)(atlas x/y)(digit w/h)
            case "u004234E0": // SYS4433 opcode 0x2da
            case "register-extended-numeric-glyph-style":
            case "u00422460":
            case "draw-decimal-glyphs": // 0x23b: retained decimal glyph draw
                return StepAdvText(label, ins, pc);
            case "get-texture-size":
            case "fill-surface-rect":
            case "u00420D50":
            case "copy-surface-rect":
                return StepSurface(label, a, pc);
            case "clear-retained-gfx-objects":
                return StepRetainedObject(label, a, pc);
            case "select-render-target":
            case "clear-render-target":
            case "release-transient-surfaces":
                return StepSurface(label, a, pc);
            case "play-bgm":
            case "restart-bgm-loop":
            case "stop-bgm":
            case "restart-bgm-once":
            case "get-current-bgm-track":
            case "play-voice":
            case "play-history-voice":
            case "u0041D910":
            case "set-voice-bgm-duck-control":
            case "schedule-voice-playback":
            case "play-sound-effect":
            case "u0041D050":
            case "sfx-start-loop":
            case "u0041D080":
            case "schedule-sfx-start":
            case "u0041D9D0":
            case "get-sound-channel-playing":
            case "u0041D2B0":
            case "get-audio-volume":
            case "set-audio-volume":
            case "get-audio-route-enabled":
            case "set-audio-route-enabled":
                return StepAudio(label, a, pc);
            case "u00415880":           // 0xd9 / semantics: clear-run-state-0x1000
            case "get-initial-root-run": // 0x130 (out)
                return StepScriptLifecycle(label, a, pc);
            case "play-modal-movie-to-surface":
            case "u004221A0":
            case "play-movie-to-surface":
            case "u00422B80":
            case "play-movie-to-surface-at-position":
                return StepMovie(label, ins, pc);
            case "query-gfx-object?":
            case "query-gfx-field?":
            case "get-gfx-geom3?":
            case "get-gfx-geom3-b?":
            case "set-gfx-geom3":
            case "set-gfx-geom3-b":
            case "u0041AF00":           // 0x80: default object slot substituted by native op 0x1d9
            case "set-default-gfx-object-slot":
            case "u00421120":
            case "swap-gfx-objects": // 0x214: exchange/move complete retained-object records
                return StepRetainedObject(label, a, pc);

            // ---- SC0000 anim/transform/spritesheet cluster (docs/engine-re.md §"SC0000 anim ... cluster") ----
            case "u00421DD0":                         // pre-reference compatibility
            case "set-gfx-range-translation-target": // 0x22f (delay)(duration)(x)(y)(z)
            case "u004219E0":                  // pre-reference compatibility
            case "set-gfx-range-transform":   // 0x229 (first)(count)(anchor x/y/z)
            case "u00421A90":                  // pre-reference compatibility
            case "set-gfx-range-scale-current": // 0x22a (sx%)(sy%)(sz%)
            case "u00421BD0":                  // pre-reference compatibility
            case "set-gfx-range-translation-current": // 0x22c (tx)(ty)(tz)
            case "u00421C60":                  // pre-reference compatibility
            case "set-gfx-range-scale-target": // 0x22d (delay)(duration)(sx%)(sy%)(sz%)
            case "set-gfx-range-rotation-target": // 0x22e (delay)(duration)(axis x/y/z)(angle degrees)
                return StepRetainedObject(label, a, pc);
            case "u004223C0":   // 0x239 one-shot spritesheet: (handle)(delay)(duration)(frame count)(columns)(flags)
            case "reset-gfx-cyclic-animations": // 0x230: stop all five retained looping channels
            case "u00421E70":
            case "u00421EA0":   // 0x231 looping spritesheet: (handle)(ms per frame)(frame count)(columns)
            case "u00421EF0":   // 0x232 cyclic packed ARGB; negative alpha/RGB preserve static obj color
            case "set-scale-cycle": // 0x233 (handle)(period ms)(target scale x/y/z percent)
            case "set-translation-cycle": // 0x235 (handle)(period ms)(target translation x/y/z)
                return StepAnimation(label, a, pc);
            case "query-gfx-translation-target":  // pre-correction generated-name compatibility
            case "query-gfx-translation-current":
            case "u00421940":   // 0x228: (succ)(handle)(outX)(outY)(outZ) <- current translation matrix
            case "u00421880":
            case "query-gfx-rotation-current": // 0x227: current axis-angle + missing-object status
                return StepRetainedObject(label, a, pc);
            case "u00422930":
            case "query-surface-stop-time-ms":
            case "query-movie-surface-active":
            case "set-movie-playback-rate":
                return StepMovie(label, ins, pc);
            case "sample-frame-time":
                return StepAnimation(label, a, pc);
            case "set-gfx-geom3-c":
            case "u00420620":             // upstream ABI label
            case "gfx-set-scale-current": // 0x1fd (handle)(sx%)(sy%)(sz%) -> current scale matrix
            case "set-current-rotation-axis-angle":
                return StepRetainedObject(label, a, pc);
            case "set-adv-wait-indicator-handle":
            case "set-adv-text-object-range":
                return StepAdvText(label, ins, pc);
            case "gfx-elem-erase":
                return StepRetainedObject(label, a, pc);
            case "gfx-elem-release":    // 0x1fa (surface slot)
                return StepSurface(label, a, pc);
            case "clone-gfx-object":
                return StepRetainedObject(label, a, pc);
            case "gfx-blit-color":
            case "gfx-draw-color":
            // ---- sprite transform / animation cluster (docs/engine-re.md "0x21c-0x243 ... ANIMATION") ----
            case "set-anim-transform-abs":   // 0x220 (handle)(delay)(duration)(tx)(ty)(tz)
            case "set-anim-transform-norm":  // 0x21e (handle)(delay)(duration)(sx%)(sy%)(sz%)
            case "set-anim-rotation-axis-angle": // 0x21f (handle)(delay)(duration)(axis x/y/z)(angle deg)
            case "anim-start":   // 0x234 legacy name: (handle)(period)(axis x/y/z), cyclic rotation channel
            case "set-anim-clock":   // 0x238 (duration) — global, non-blocking (host advances it per-frame)
            case "set-object-animation-detached": // 0x242 (handle)(flags): bit 0 is nonblocking/force-proof
            case "reset-anim-clock": // 0x243: force unprotected one-shots and reset the global service clock
            case "set-gfx-animation-service-flags": // 0x24e: bit 1 suppresses op 0x243
                return StepAnimation(label, a, pc);
            case "play-movie-mask-transition":
                return StepMovie(label, ins, pc);
            case "u004156C0": // 0x1bf arm-voice-duck-release
            case "arm-voice-duck-release":
            case "u00415700": // 0x1d5 mute-next-voice
            case "mute-next-voice":
                return StepAudio(label, a, pc);
            case "u004160D0": // 0x1f4 stmt-begin: the outermost open withholds delayed audio starts
            case "stmt-begin":
                if (_statementBlockActive) _statementBlockDepth++;
                else { _statementBlockActive = true; _host.SetStatementBlockActive(true); }
                return pc + 1;
            case "u00416120": // 0x1f5 stmt-end: the outermost close releases them
            case "stmt-end":
                if (_statementBlockDepth > 0) _statementBlockDepth--;
                else if (_statementBlockActive) { _statementBlockActive = false; _host.SetStatementBlockActive(false); }
                return pc + 1;
            case "u004213E0": // 0x21b set-tick-presentation
            case "set-tick-presentation":
                _tickPresentationEnabled = Read(a[0]) != 0; return pc + 1;
            case "u00416390": // 0x247 query-tick-presentation
            case "query-tick-presentation":
                Write(a[0], _tickPresentationEnabled ? 1 : 0); return pc + 1;
            case "queue-surface-alpha-transition": // 0x223: target surface crossfade over two object ranges
            case "u00422ED0": // shared opcode 0x24f upstream ABI label
            case "queue-patterned-surface-transition": // shared 0x24f type-2 retained range transition
            case "u00422F60": // SYS4433 opcode 0x250 upstream ABI label
            case "queue-directional-blur-range-transition": // SYS4433 0x250: retained range post-effect
            case "u00422FF0": // SYS4433 opcode 0x251 upstream ABI label
            case "queue-radial-blur-range-transition": // SYS4433 0x251: retained range post-effect
            case "present-frame": // 0x20c: read/message-skip path snaps a queued transition to its endpoint
            case "fade-surface-in-from-black": // 0x21: blocking black -> captured full-frame surface
            case "u00418860":
            case "fade-surface-out-to-black": // 0x22: blocking captured full-frame surface -> black
            case "u00418920":
            case "fade-surface-out-to-white": // 0x24: blocking captured full-frame surface -> white
            case "u00418A90":
            case "crossfade-surfaces": // 0x25: legacy full-frame surface alpha transition
            case "u00418B40":
            case "u00418CC0": // SYS4433 opcode 0x27 upstream ABI label
            case "reveal-surface-striped":
            case "u00418D90": // SYS4433 opcode 0x28 upstream ABI label
            case "reveal-surface-staggered-strips":
            case "mark-frame-yield": // 0x21c: normal foreground-transition scheduler/resume boundary
            case "clear-gfx-command-queue": // 0x224: retained compositor does not use this native queue
            case "present-gfx-object-range": // 0x222: publish pending retained changes in the selected range
            case "u004216C0":
                return StepPresentation(label, a, pc);
            default:
                // Coverage and dispatch are checked independently. Drift is an internal hard error,
                // never permission to skip an instruction.
                HaltReason = $"runtime-coverage-inconsistency:0x{op:x}:{label}";
                return HALT;
        }
    }

}

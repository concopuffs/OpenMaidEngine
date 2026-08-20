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
    private ExecFrame? _interactiveFrame;
    private ExecFrame? _rawInputFrame;
    private int _pointerX = int.MinValue, _pointerY = int.MinValue;
    private int _mouseButtonState;
    private int _mouseWheelDelta;
    private int _heldInputCallbackMask;
    private int _queuedInputCallbackMask;
    private bool _autoMessageEnabled;
    private long _autoMessageTime0Ms = 500;
    private long _autoMessageTime1Ms = 2000;
    private bool _autoVoicePending;
    private bool _initialRootRun = true;
    private volatile bool _stopRequested;
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
    /// <summary>Native/profile-owned values read by scripts but maintained outside script-visible writes.</summary>
    public Dictionary<int, long> ExternalGlobals { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    public Dictionary<int, int> GlobalPointers { get; } = new();
    public Dictionary<int, int> GlobalStringPointers { get; } = new();
    public GfxState Gfx { get; } = new();
    public InputBindings InputBindings { get; } = new();
    public List<(int Offset, string Text, string Script)> Emitted { get; } = new();
    public string? HaltReason { get; private set; }
    public UnsupportedOpcodeDiagnostic? CompatibilityFailure { get; private set; }
    public long Steps { get; private set; }
    public bool AutoMessageEnabled => _autoMessageEnabled;
    public bool MessageSkipEnabled => _messageSkipEnabled;
    public int SystemMenuActionsEnabled => _systemMenuActionsEnabled;
    public uint SystemMenuShowDelayMilliseconds => _systemMenuShowDelayMilliseconds;
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
        bool wake = false;
        lock (_interactiveLock)
        {
            _pointerX = x; _pointerY = y;
            if (_interactiveFrame != null)
                wake = _interactiveFrame.Hotspots.UpdatePointer(x, y);
        }
        if (wake) _host.WakeInputCallbackService();
    }

    /// <summary>Queue an armed hotspot's activation callback. True means the click was consumed.</summary>
    public bool TryActivatePointer(int x, int y)
    {
        bool consumed = false;
        lock (_interactiveLock)
        {
            _pointerX = x; _pointerY = y;
            if (_interactiveFrame != null)
            {
                _interactiveFrame.Hotspots.UpdatePointer(x, y);
                consumed = _interactiveFrame.Hotspots.Activate(x, y);
            }
        }
        if (consumed) _host.WakeInputCallbackService();
        return consumed;
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
    private long ReadGlobal(int k) => ExternalGlobals.TryGetValue(k, out var v) ? v : Gi(Globals, k);
    private static string Gs(Dictionary<int, string> d, int k) => d.TryGetValue(k, out var v) ? v : "";
    private static long PyDiv(long a, long b) { if (b == 0) return 0; long q = a / b, r = a % b; if (r != 0 && (r < 0) != (b < 0)) q--; return q; }
    private static long PyMod(long a, long b) { if (b == 0) return 0; long r = a % b; if (r != 0 && (r < 0) != (b < 0)) r += b; return r; }

    private static bool IsStr(Operand o) => o.Type is T_STR or T_GSTR or T_GSTRPTR or T_LSTR or T_LSTRPTR;
    private static bool SameOperand(Operand a, Operand b) => a.Type == b.Type && a.Value == b.Value;

    private static bool IsAdvLabeledYield(Script script, Instruction ins)
        => ins.Opcode == 0x140 && ins.Args.Count >= 4
           && ins.Args[1].Type == T_STR && ins.Args[2].Type == T_STR
           && script.GetString((int)ins.Args[1].Value) == "LABEL"
           && script.GetString((int)ins.Args[2].Value) == "J";

    private bool TryGetAdvYieldTerminal(int pc, Operand output, out long terminal)
    {
        terminal = 0;
        if (pc + 2 >= _cur.Script.Instructions.Count) return false;
        var setTerminal = _cur.Script.Instructions[pc + 1];
        var compare = _cur.Script.Instructions[pc + 2];
        if (_t.Label(setTerminal.Opcode) != "mov" || setTerminal.Args.Count < 2
            || setTerminal.Args[1].Type != T_IMM
            || _t.Label(compare.Opcode) != "eq" || compare.Args.Count < 3)
            return false;

        var terminalOperand = setTerminal.Args[0];
        bool comparesTerminalToOutput =
            (SameOperand(compare.Args[1], terminalOperand) && SameOperand(compare.Args[2], output))
            || (SameOperand(compare.Args[2], terminalOperand) && SameOperand(compare.Args[1], output));
        if (!comparesTerminalToOutput) return false;
        terminal = Read(setTerminal.Args[1]);
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
    {
        // Himegari's release AGE initializes both optional debug metadata tables to null and has no
        // writer for either one. The native formatter consequently emits -1 and "-" here.
        const int sourceLine = -1;
        const string commandName = "-";
        int nativeDepth = Math.Max(0, _depth - 1);
        string context = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "\n\nデバック情報：\nFILE={0} ADDRESS={1:X} LINE={2} COMMAND={3}({4}) DEPTH={5}\n",
            _cur.Script.Name, instruction.Offset, sourceLine, commandName,
            instruction.Opcode, nativeDepth);
        return new DiagnosticMessage(DiagnosticCaption, _diagnosticOutput.PendingText + context);
    }

    private sealed class RootReloadRequestedException : Exception { }
    private sealed class NumberedRestoreRequestedException : Exception { }
    private sealed class ProcessExitRequestedException : Exception { }
    private sealed record DebugFrameReturnRequest(ExecFrame Frame, IReadOnlyDictionary<int, long> GlobalWrites);
    private sealed record PreloadedScriptSlot(long ScriptId, ExecFrame Frame);
    private enum FrameOutcome { Returned, DebugReturned, RootReload, ExitRequested, Halted, RanOff }

    public void Run(int entryOffset = 0)
    {
        // The native scheduler supplies this scene-entry state outside script-visible global writes.
        // Restrict it to the byte-identical ADV LABEL/J idiom; op 0x140 also has an unrelated TITLE use.
        if (entryOffset == 0
            && _compatibility.SceneEntryCoroutineGateAddress is { } sceneEntryGate
            && _s.Instructions.Any(ins => IsAdvLabeledYield(_s, ins)))
            Globals[sceneEntryGate] = 1;

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
            catch (NumberedRestoreRequestedException)
            {
                if (_loadedNumberedState == null) throw;
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
                ResetSceneContextForRootReload();
                var reloaded = _provider?.GetById(0);
                if (reloaded == null)
                {
                    HaltReason ??= "root-reload-unresolved:0x0";
                    break;
                }
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

    private void ResetSceneContextForRootReload()
    {
        Gfx.ResetSceneContext();
        _valueSwitchTargets.Clear();
        _preloadedScriptSlots.Clear();
        ResetIntegerStacks();
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
        _autoMessageEnabled = false;
        _autoVoicePending = false;
        _messageSkipEnabled = false;
        _messageSkipServiceActive = false;
        _advReadSkipState = false;
        _advTextStyle = AdvTextStyle.Default;
        _systemMenuActionsEnabled = 1;
        _systemMenuShowDelayMilliseconds = 0;
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
            try
            {
                while (pc >= 0 && pc < frame.Script.Instructions.Count)
                {
                    if (_stopRequested) { outcome = FrameOutcome.ExitRequested; break; }
                    if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; outcome = FrameOutcome.Halted; break; }
                    Steps++;
                    frame.Pc = pc;
                    if (_sink.TracingSteps) _sink.Emit(TraceEvent.Step(pc, frame.Script.Instructions[pc], _depth));
                    int next = Step(frame.Script.Instructions[pc], pc);
                    _host.FrameYield();
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
            int[] returns = restored?.ReturnIndices.ToArray()
                ?? frame.CallStack
                    .Where(returnPc => (uint)returnPc < (uint)frame.Script.Instructions.Count)
                    .Select(returnPc =>
                    {
                        int returnOffset = frame.Script.Instructions[returnPc].Offset;
                        return FindTableIndex(frame.Script.LocalCallOffsets, returnOffset - 3);
                    })
                    .Where(index => index >= 0)
                    .ToArray();
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
                i - 1, frame.Script.PackedId, returns, resumeIndex, callTargetIndex);
        }

        NativeNumberedSaveState basis = _retainedNativeNumberedState
            ?? NativeNumberedSaveCodec.Empty(frames);
        var gfx = NativeGfxPersistenceCodec.Capture(Gfx);
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
        };
    }

    private bool TryLoadNumberedState(int slot, bool restoreHistory)
    {
        if (_nativeDatStore == null) return false;
        try
        {
            NativeNumberedSaveFile? file = _nativeDatStore.LoadNumberedFile(slot);
            if (file == null) return false;
            NativeNumberedSaveState state = NativeNumberedSaveCodec.Decode(file.Document.Payload);
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
        ReplaceDensePrefix(
            Globals, state.IntegerGlobals.Select(value => (long)value).ToArray(), value => value != 0);
        ReplaceDensePrefix(
            GlobalFloats, state.FloatGlobals.Select(value => (long)value).ToArray(), value => value != 0);
        ReplaceDensePrefix(GlobalStrings, state.StringGlobals, value => value.Length != 0);
        ReplaceDensePrefix(GlobalPointers, state.PointerGlobals, value => value != 0);
        ReplaceDensePrefix(GlobalStringPointers, state.PointerStrings, value => value != 0);

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
        foreach (GfxSurfacePersistenceState surface in gfxSnapshot.Surfaces
                     .Where(item => item.ReloadOnRestore && item.ResourceId >= 0))
            _host.SetTexture(surface.ResourceId, surface.Slot, surface.ColorKey);
    }

    private static void ReplaceDensePrefix<T>(
        Dictionary<int, T> bank, IReadOnlyList<T> values, Func<T, bool> retain)
    {
        foreach (int key in bank.Keys.Where(key => (uint)key < (uint)values.Count).ToArray())
            bank.Remove(key);
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
        int target;
        lock (_interactiveLock)
        {
            if (_interactiveFrame == null || !_interactiveFrame.Hotspots.TryDequeue(out target)) return false;
        }
        if (_cur.Script.IndexByOffset.TryGetValue(target, out int pc))
        {
            _cur.CallStack.Add(HOTSPOT_RETURN);
            while (pc >= 0 && pc < _cur.Script.Instructions.Count)
            {
                if (_stopRequested) break;
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
            // A malformed callback must not leave its sentinel in the page's ordinary local-call stack.
            int sentinel = _cur.CallStack.LastIndexOf(HOTSPOT_RETURN);
            if (sentinel >= 0) _cur.CallStack.RemoveAt(sentinel);
        }
        lock (_interactiveLock)
        {
            // History/Hide callbacks cancel the active registry, run a nested script, then republish the
            // parent frame's definitions. Nested RunFrame deliberately clears the disarmed interactive
            // pointer, so restore the still-running parent before rearming its rebuilt registry.
            if (_interactiveFrame == null && _cur.Hotspots.HasDefinitions) _interactiveFrame = _cur;
            _interactiveFrame?.Hotspots.RearmAfterCallback(_pointerX, _pointerY);
        }
        _host.InputCallbackCompleted(Gfx);
        return true;
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
                return StepRetainedObject(label, a, pc);
            case "u004223C0":   // 0x239 spritesheet cell: (handle)(delay)(duration)(frame count)(columns)(cell)
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
            case "u004156C0": // 0x1bf call-end marker
            case "u00415700": // 0x1d5 conditional-body marker
            case "u004160D0": // 0x1f4 statement-begin marker
            case "u00416120": // 0x1f5 statement-end marker
            case "u004213E0": // 0x21b tentative line/statement id marker
                return pc + 1;
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

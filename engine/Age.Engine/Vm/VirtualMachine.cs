using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using System.Text;
namespace Age.Engine.Vm;

/// <summary>A stable identity/snapshot of the exact script frame currently executing.</summary>
public sealed record DebugFrameSnapshot(long FrameId, string CurrentScript, IReadOnlyList<string> CallStack);

public sealed class VirtualMachine
{
    private const long NoJump = 0xFFFFFFFF;
    private const int HALT = int.MinValue;
    private const int FRAME_RETURN = int.MinValue + 1;
    private const int HOTSPOT_RETURN = int.MinValue + 2;
    private const int ROOT_RELOAD = int.MinValue + 3;
    private const int SceneEntryCoroutineGate = 0xaba5c;
    private const int T_IMM = 0, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_GPTR = 6,
                      T_GSTRPTR = 8, T_LINT = 9, T_LFLOAT = 10, T_LSTR = 11, T_LPTR = 12,
                      T_LSTRPTR = 14;

    private readonly Script _s;
    private readonly OpcodeTable _t;
    private readonly IHost _host;
    private readonly VmOptions _o;
    private readonly Encoding _nativeStringEncoding;
    private readonly IScriptProvider? _provider;
    private static readonly bool _diagSetTexture = System.Environment.GetEnvironmentVariable("AGE_DIAG_SETTEX") == "1";
    private ExecFrame _cur = null!;
    private int _depth;
    private readonly ITraceSink _sink;
    private readonly object _interactiveLock = new();
    private readonly object _debugControlLock = new();
    private readonly List<string> _activeFrameNames = new();
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
    private volatile bool _messageSkipEnabled;
    private volatile bool _messageSkipServiceActive;
    private volatile bool _advSkipServiceEnabled;
    private AdvTextStyle _advTextStyle = AdvTextStyle.Default;
    private readonly Dictionary<string, int> _valueSwitchTargets = new(StringComparer.Ordinal);
    public long CallScriptDispatches { get; private set; }

    public Dictionary<int, long> Globals { get; } = new();
    /// <summary>Native/profile-owned values read by scripts but maintained outside script-visible writes.</summary>
    public Dictionary<int, long> ExternalGlobals { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    public GfxState Gfx { get; } = new();
    public InputBindings InputBindings { get; } = new();
    public List<(int Offset, string Text, string Script)> Emitted { get; } = new();
    public string? HaltReason { get; private set; }
    public long Steps { get; private set; }
    public bool AutoMessageEnabled => _autoMessageEnabled;
    public bool MessageSkipEnabled => _messageSkipEnabled;
    /// <summary>True while a script-owned timed mouse/input callback loop (HISTORY/HIDEWIN family) owns input.</summary>
    public bool IsRawInputCallbackActive
    {
        get { lock (_interactiveLock) return _rawInputFrame != null; }
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
                          AdvTextHistory? textHistory = null)
    {
        _s = s; _t = t; _host = host; _o = o ?? new VmOptions(); _provider = provider;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _nativeStringEncoding = Encoding.GetEncoding(_o.NativeStringCodePage);
        _sink = sink ?? NullTraceSink.Instance; TextHistory = textHistory ?? new AdvTextHistory();
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

    /// <summary>Mirror adv_interpreter_tick's bit-0x40 path. The bit is logical action 6 from the
    /// process-owned binding map, not a hardcoded Ctrl test; Himegari also binds C and retains the
    /// engine's default Backspace binding. The presentation lifecycle gate prevents a held action
    /// from leaking into non-ADV script execution.</summary>
    private void RefreshPhysicalMessageSkipState()
    {
        bool active = _advSkipServiceEnabled && (InputBindings.PollActionMask() & 0x40) != 0;
        _host.SetPhysicalMessageSkipActive(active);
    }

    private static long Gi(Dictionary<int, long> d, int k) => d.TryGetValue(k, out var v) ? v : 0;
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
        T_GINT or T_GFLOAT => ReadGlobal((int)op.Value),
        T_GPTR => Gi(Globals, (int)Gi(Globals, (int)op.Value)),
        T_LINT => Gi(_cur.Locals.I, (int)op.Value),
        T_LFLOAT => Gi(_cur.Locals.F, (int)op.Value),
        T_LPTR => ReadIntCell(Ga(_cur.Locals.P, (int)op.Value)),
        _ => op.Value,
    };

    private void Write(Operand op, long val)
    {
        switch (op.Type)
        {
            case T_GINT: case T_GFLOAT: Globals[(int)op.Value] = val; break;
            case T_GPTR: Globals[(int)Gi(Globals, (int)op.Value)] = val; break;
            case T_LINT: _cur.Locals.I[(int)op.Value] = val; break;
            case T_LFLOAT: _cur.Locals.F[(int)op.Value] = val; break;
            case T_LPTR: WriteIntCell(Ga(_cur.Locals.P, (int)op.Value), val); break;
        }
    }

    private string ReadStr(Operand op) => op.Type switch
    {
        T_STR => _cur.Script.GetString((int)op.Value),
        T_GSTR => Gs(GlobalStrings, (int)op.Value),
        T_GSTRPTR => Gs(GlobalStrings, (int)Gi(Globals, (int)op.Value)),
        T_LSTR => Gs(_cur.Locals.S, (int)op.Value),
        T_LSTRPTR => ReadStringCell(Ga(_cur.Locals.SP, (int)op.Value)),
        _ => "",
    };

    private void WriteStr(Operand op, string val)
    {
        switch (op.Type)
        {
            case T_GSTR: GlobalStrings[(int)op.Value] = val; break;
            case T_GSTRPTR: GlobalStrings[(int)Gi(Globals, (int)op.Value)] = val; break;
            case T_LSTR: _cur.Locals.S[(int)op.Value] = val; break;
            case T_LSTRPTR: WriteStringCell(Ga(_cur.Locals.SP, (int)op.Value), val); break;
        }
    }

    private int NativeStringByteLength(string value)
    {
        int nul = value.IndexOf('\0');
        return _nativeStringEncoding.GetByteCount(nul < 0 ? value : value[..nul]);
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
        T_GPTR or T_GSTRPTR => VmAddress.Global((int)Gi(Globals, (int)op.Value)),
        T_LPTR => Ga(_cur.Locals.P, (int)op.Value),
        T_LSTRPTR => Ga(_cur.Locals.SP, (int)op.Value),
        _ => VmAddress.Global((int)op.Value),
    };

    private bool TryStoreAddress(Operand destination, VmAddress address)
    {
        switch (destination.Type)
        {
            case T_LPTR: _cur.Locals.P[(int)destination.Value] = address; return true;
            case T_LSTRPTR: _cur.Locals.SP[(int)destination.Value] = address; return true;
            case T_GPTR: case T_GSTRPTR: Globals[(int)destination.Value] = address.Address; return true;
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
            case T_GINT: case T_GFLOAT: Globals[address] = value; break;
            case T_LINT: _cur.Locals.I[address] = value; break;
            case T_LFLOAT: _cur.Locals.F[address] = value; break;
            case T_GPTR: Globals[checked((int)Gi(Globals, (int)destination.Value) + index)] = value; break;
            case T_LPTR: WriteIntCell(Ga(_cur.Locals.P, (int)destination.Value).Offset(index), value); break;
        }
    }

    private long ReadAddressedCell(Operand operand, int offset)
    {
        return operand.Type switch
        {
            T_LINT => Gi(_cur.Locals.I, checked((int)operand.Value + offset)),
            T_LFLOAT => Gi(_cur.Locals.F, checked((int)operand.Value + offset)),
            T_GINT or T_GFLOAT => ReadGlobal(checked((int)operand.Value + offset)),
            T_LPTR => ReadIntCell(Ga(_cur.Locals.P, (int)operand.Value).Offset(offset)),
            T_GPTR => Gi(Globals, checked((int)Gi(Globals, (int)operand.Value) + offset)),
            _ => Gi(Globals, checked((int)operand.Value + offset)),
        };
    }

    private (VmAddressSpace Space, int Address) AddressedCellIdentity(Operand operand, int offset)
        => operand.Type switch
        {
            T_LINT => (VmAddressSpace.LocalInteger, checked((int)operand.Value + offset)),
            T_LFLOAT => (VmAddressSpace.LocalFloat, checked((int)operand.Value + offset)),
            T_LPTR => PointerIdentity(Ga(_cur.Locals.P, (int)operand.Value).Offset(offset)),
            T_GPTR => (VmAddressSpace.Global, checked((int)Gi(Globals, (int)operand.Value) + offset)),
            _ => (VmAddressSpace.Global, checked((int)operand.Value + offset)),
        };

    private static (VmAddressSpace Space, int Address) PointerIdentity(VmAddress address)
        => (address.Space, address.Address);

    private string FormatSwitchValue(Operand operand)
        => IsStr(operand)
            ? ReadStr(operand)
            : unchecked((int)Read(operand)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed class RootReloadRequestedException : Exception { }
    private sealed class ProcessExitRequestedException : Exception { }
    private sealed record DebugFrameReturnRequest(ExecFrame Frame, IReadOnlyDictionary<int, long> GlobalWrites);
    private enum FrameOutcome { Returned, DebugReturned, RootReload, ExitRequested, Halted, RanOff }

    public void Run(int entryOffset = 0)
    {
        // The native scheduler supplies this scene-entry state outside script-visible global writes.
        // Restrict it to the byte-identical ADV LABEL/J idiom; op 0x140 also has an unrelated TITLE use.
        if (entryOffset == 0 && _s.Instructions.Any(ins => IsAdvLabeledYield(_s, ins)))
            Globals[SceneEntryCoroutineGate] = 1;

        Script root = _s;
        int rootEntry = root.IndexByOffset.TryGetValue(entryOffset, out var idx) ? idx : 0;
        FrameCause cause = FrameCause.TopScene;
        while (true)
        {
            var outcome = RunFrame(new ExecFrame(root, rootEntry), cause);
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
        }
        _autoMessageEnabled = false;
        _autoVoicePending = false;
        _messageSkipEnabled = false;
        _messageSkipServiceActive = false;
        _advSkipServiceEnabled = false;
        _advTextStyle = AdvTextStyle.Default;
        TextHistory.SetRecordingEnabled(true);
        _host.SetMessageSkipActive(false);
        _host.SetPhysicalMessageSkipActive(false);
        _host.ResetSceneContext();
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
                    if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; outcome = FrameOutcome.Halted; break; }
                    Steps++;
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
                if (_activeFrameNames.Count > 0) _activeFrameNames.RemoveAt(_activeFrameNames.Count - 1);
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
                if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; break; }
                Steps++;
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
        switch (_t.Label(op))
        {
            case "add": Write(a[0], Read(a[1]) + Read(a[2])); return pc + 1;
            case "sub": Write(a[0], Read(a[1]) - Read(a[2])); return pc + 1;
            case "mul": Write(a[0], Read(a[1]) * Read(a[2])); return pc + 1;
            case "div": Write(a[0], PyDiv(Read(a[1]), Read(a[2]))); return pc + 1;
            case "mod": Write(a[0], PyMod(Read(a[1]), Read(a[2]))); return pc + 1;
            case "and": Write(a[0], Read(a[1]) & Read(a[2])); return pc + 1;
            case "or":  Write(a[0], Read(a[1]) | Read(a[2])); return pc + 1;
            case "sar": Write(a[0], Read(a[1]) >> (int)(Read(a[2]) & 31)); return pc + 1;
            case "shl": Write(a[0], Read(a[1]) << (int)(Read(a[2]) & 31)); return pc + 1;
            case "eq":  Write(a[0], Read(a[1]) == Read(a[2]) ? 1 : 0); return pc + 1;
            case "ne":  Write(a[0], Read(a[1]) != Read(a[2]) ? 1 : 0); return pc + 1;
            case "string-equals":
                Write(a[0], string.Equals(ReadStr(a[1]), ReadStr(a[2]), StringComparison.Ordinal) ? 1 : 0);
                return pc + 1;
            case "lt":  Write(a[0], Read(a[1]) <  Read(a[2]) ? 1 : 0); return pc + 1;
            case "lte": Write(a[0], Read(a[1]) <= Read(a[2]) ? 1 : 0); return pc + 1;
            case "gr":  Write(a[0], Read(a[1]) >  Read(a[2]) ? 1 : 0); return pc + 1;
            case "gre": Write(a[0], Read(a[1]) >= Read(a[2]) ? 1 : 0); return pc + 1;
            case "mov":
            case "set-string":
                if (IsStr(a[0]) || IsStr(a[1])) WriteStr(a[0], ReadStr(a[1]));
                else Write(a[0], Read(a[1]));
                return pc + 1;
            case "halve-strlen": // 0x1a6: strlen(native encoded bytes) >> 1
                Write(a[0], NativeStringByteLength(ReadStr(a[1])) >> 1);
                return pc + 1;
            case "lookup-array":
                LookupStore(a[0], BaseAddr(a[1]).Offset(Read(a[2]))); return pc + 1;
            case "lookup-array-2d":
                LookupStore(a[0], BaseAddr(a[1]).Offset(Read(a[2]) * Read(a[3]) + Read(a[4]))); return pc + 1;
            case "take-address": // 0x63: pointer destination <- underlying address of operand 2
                if (!TryStoreAddress(a[0], BaseAddr(a[1])))
                {
                    HaltReason ??= $"take-address-destination-type:{a[0].Type}";
                    return HALT;
                }
                return pc + 1;
            case "copy-inline-int-array": // 0x64: count dword followed by plain file values
            {
                int offset = checked((int)Read(a[1]));
                if ((uint)offset >= (uint)_cur.Script.BodyDwords.Count)
                {
                    HaltReason ??= $"inline-array-offset@0x{offset:x}";
                    return HALT;
                }
                uint rawCount = _cur.Script.BodyDwords[offset];
                int available = _cur.Script.BodyDwords.Count - offset - 1;
                if (rawCount > (uint)available)
                {
                    HaltReason ??= $"inline-array-length@0x{offset:x}:{rawCount}";
                    return HALT;
                }
                int count = (int)rawCount;
                for (int i = 0; i < count; i++)
                    WriteConsecutive(a[0], i, unchecked((int)_cur.Script.BodyDwords[offset + 1 + i]));
                return pc + 1;
            }
            case "copy-dwords": // 0x1b0: memcpy(count * 4) across resolved integer-cell spans
            {
                int count = checked((int)Read(a[2]));
                if (count < 0)
                {
                    HaltReason ??= $"copy-dwords-negative-count:{count}";
                    return HALT;
                }
                VmAddress source = BaseAddr(a[0]);
                VmAddress destination = BaseAddr(a[1]);
                var values = new long[count];
                for (int i = 0; i < count; i++)
                    values[i] = unchecked((int)ReadIntCell(source.Offset(i)));
                for (int i = 0; i < count; i++) WriteIntCell(destination.Offset(i), values[i]);
                return pc + 1;
            }
            case "find-hit-rectangle": // 0x12e: inclusive rectangle intersection over addressed arrays
            case "u0041E940":
            {
                int previous = (int)Read(a[0]);
                int count = System.Math.Max(0, (int)Read(a[7]));
                long refLeft = ReadAddressedCell(a[1], 0);
                long refRight = ReadAddressedCell(a[1], 1);
                long refTop = ReadAddressedCell(a[1], 2);
                long refBottom = ReadAddressedCell(a[1], 3);
                int match = -1;
                for (int index = previous + 1; index < count; index++)
                {
                    long x = Read(a[2]) - ReadAddressedCell(a[5], index);
                    long y = Read(a[3]) - ReadAddressedCell(a[6], index);
                    long left = ReadAddressedCell(a[4], index * 4);
                    long right = ReadAddressedCell(a[4], index * 4 + 1);
                    long top = ReadAddressedCell(a[4], index * 4 + 2);
                    long bottom = ReadAddressedCell(a[4], index * 4 + 3);
                    bool isReferenceRectangle = AddressedCellIdentity(a[1], 0)
                        == AddressedCellIdentity(a[4], index * 4);
                    if (!isReferenceRectangle
                        && x + refLeft <= right && x + refRight >= left
                        && y + refTop <= bottom && y + refBottom >= top)
                    {
                        match = index;
                        break;
                    }
                }
                Write(a[0], match);
                return pc + 1;
            }
            case "sort-indices-by-key-sum": // 0x12f: stable ascending permutation by signed key sum
            case "u0041ECB0":
            {
                VmAddress output = BaseAddr(a[0]);

                // Native writes element zero even when count is zero or negative, then builds the
                // permutation in place with insertion sort. Read the count for each outer iteration:
                // the handler fetches operand 4 repeatedly rather than caching it.
                WriteIntCell(output, 0);
                for (int sourceIndex = 1; sourceIndex < unchecked((int)Read(a[3])); sourceIndex++)
                {
                    int position = sourceIndex;
                    while (position > 0)
                    {
                        int previousIndex = unchecked((int)ReadIntCell(output.Offset(position - 1)));
                        int previousKey = unchecked(
                            unchecked((int)ReadAddressedCell(a[1], previousIndex))
                            + unchecked((int)ReadAddressedCell(a[2], previousIndex)));
                        int sourceKey = unchecked(
                            unchecked((int)ReadAddressedCell(a[1], sourceIndex))
                            + unchecked((int)ReadAddressedCell(a[2], sourceIndex)));
                        if (sourceKey >= previousKey) break;

                        WriteIntCell(output.Offset(position), previousIndex);
                        position--;
                    }
                    WriteIntCell(output.Offset(position), sourceIndex);
                }
                return pc + 1;
            }
            case "bit-set":
            {
                long bit = Read(a[1]);
                if ((ulong)bit >= 32) { HaltReason ??= $"bit-index-out-of-range:{bit}"; return HALT; }
                Write(a[0], Read(a[0]) | (1L << (int)bit)); return pc + 1;
            }
            case "bit-reset":
            {
                long bit = Read(a[1]);
                if ((ulong)bit >= 32) { HaltReason ??= $"bit-index-out-of-range:{bit}"; return HALT; }
                Write(a[0], Read(a[0]) & ~(1L << (int)bit)); return pc + 1;
            }
            case "check-bit": Write(a[0], (Read(a[1]) >> (int)(Read(a[2]) & 31)) & 1); return pc + 1;
            case "zero-int-range":
            case "copy-to-global": // pre-reference compatibility for opcode 0x6c
            {
                int count = System.Math.Max(0, checked((int)Read(a[1])));
                for (int i = 0; i < count; i++) WriteConsecutive(a[0], i, 0);
                return pc + 1;
            }
            case "random-modulo": // 0x60: native CRT rand() % bound
            case "u0041A270":
            {
                long bound = Read(a[1]);
                if (bound == 0)
                {
                    Write(a[0], 0);
                    HaltReason ??= "random-modulo-zero";
                    return HALT;
                }
                Write(a[0], System.Random.Shared.Next(0x8000) % bound);
                return pc + 1;
            }
            case "jmp": return _cur.Script.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);
            case "call": _cur.CallStack.Add(pc + 1); return _cur.Script.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);
            case "ret":
                if (_cur.CallStack.Count > 0) { int r = _cur.CallStack[^1]; _cur.CallStack.RemoveAt(_cur.CallStack.Count - 1); return r; }
                return FRAME_RETURN;    // empty intra-call stack => return from the script frame
            case "jcc":
            {
                long tgt = Read(a[0]) != 0 ? a[1].Value : a[2].Value;
                return tgt == NoJump ? pc + 1 : _cur.Script.IndexByOffset.GetValueOrDefault((int)tgt, pc + 1);
            }
            case "begin-value-switch":
                _valueSwitchTargets.Clear(); return pc + 1;
            case "add-value-switch-case":
                _valueSwitchTargets[FormatSwitchValue(a[0])] = checked((int)Read(a[1])); return pc + 1;
            case "value-switch-jump":
            {
                int target = _valueSwitchTargets.TryGetValue(FormatSwitchValue(a[0]), out int matched)
                    ? matched : checked((int)Read(a[1]));
                return _cur.Script.IndexByOffset.GetValueOrDefault(target, pc + 1);
            }
            case "u0041ADB0":
            case "coroutine-save-yield-handlers":   // 0x7b: retain native handler metadata
                _cur.CoroutineYieldHandlerA = (int)Read(a[0]);
                _cur.CoroutineYieldHandlerB = (int)Read(a[1]);
                return pc + 1;
            case "u00414D50":
            case "yield-adv-coroutine":             // 0x199: A -> nested service -> B -> 0x7c resume
            {
                int? targetOffset;
                if (!_cur.CoroutineYieldActive)
                {
                    _cur.CoroutineResumePc = pc + 1;
                    _cur.CoroutineYieldActive = true;
                    _host.SetAdvPagePresentationSuspended(true);
                    targetOffset = _cur.CoroutineYieldHandlerA;
                }
                else targetOffset = _cur.CoroutineYieldHandlerB;

                return targetOffset is int offset
                    ? _cur.Script.IndexByOffset.GetValueOrDefault(offset, pc + 1)
                    : pc + 1;
            }
            case "u00416A90":
            case "coroutine-resume":                // 0x7c: restore the PC saved by op 0x199
                if (_cur.CoroutineResumePc is int resumePc)
                {
                    _cur.CoroutineResumePc = null;
                    _cur.CoroutineYieldActive = false;
                    _host.SetAdvPagePresentationSuspended(false);
                    return resumePc;
                }
                return pc + 1;                       // cold bounded scene-entry path
            case "u0041F9C0":
            case "coroutine-label-yield":           // 0x140: bounded host model for LABEL/J only
            {
                if (!IsAdvLabeledYield(_cur.Script, ins))
                {
                    if (_sink.TracingSteps) _sink.Emit(TraceEvent.Stub(op, pc));
                    return pc + 1;
                }
                if (!TryGetAdvYieldTerminal(pc, a[0], out long terminal))
                {
                    HaltReason ??= $"coroutine-yield-pattern@0x{ins.Offset:x}";
                    return HALT;
                }

                int visits = _cur.CoroutineYieldVisits.GetValueOrDefault(pc);
                _cur.CoroutineYieldVisits[pc] = visits + 1;
                // First visit must enter setup even if out retained this same terminal from a prior scene.
                // Every later visit returns the script-encoded terminal and exits the bounded loop.
                Write(a[0], visits == 0 ? (terminal == 0 ? 1 : 0) : terminal);
                return pc + 1;
            }
            case "throw-exit-request":
                if (_o.IgnoreExitRequests) return pc + 1;
                // Native op 0x1 throws Command_Exit_Exception through callbacks and nested script
                // frames. The outer engine loop catches it and exits without advancing frame_pc.
                throw new ProcessExitRequestedException();
            case "exit": return FRAME_RETURN;
            case "exit-script":
                // Native op 0x9 clears the process-initial flag, disposes every active script frame,
                // resets scene-owned services, and loads raw script resource 0 as the new root.
                _initialRootRun = false;
                return ROOT_RELOAD;
            case "call-script":
            {
                long id = a.Count > 0 ? Read(a[0]) : 0;
                CallScriptDispatches++;
                if (_provider == null)
                {
                    _sink.Emit(TraceEvent.CallScript(id, null));   // stub mode: notify only, no child pushed
                    return pc + 1;
                }
                if (_depth >= _o.CallDepthCap) { HaltReason ??= "call-depth-exceeded"; return HALT; }
                var child = _provider.GetById(id);
                _sink.Emit(TraceEvent.CallScript(id, child?.Name));
                if (child == null) { HaltReason ??= $"callscript-unresolved:0x{id:x}"; return HALT; }
                var entry = child.IndexByOffset.TryGetValue(0, out var ci) ? ci : 0;
                var outcome = RunFrame(new ExecFrame(child, entry), FrameCause.CallScript, id);
                if (outcome == FrameOutcome.Halted) return HALT;   // propagate whole-VM halt up
                if (outcome == FrameOutcome.RootReload) return ROOT_RELOAD; // discard every caller frame
                if (outcome == FrameOutcome.ExitRequested) throw new ProcessExitRequestedException();
                return pc + 1;                                      // Returned / RanOff: resume caller
            }
            case "show-text":
                foreach (var o in a)
                {
                    if (o.Type != T_STR) continue;
                    int off = (int)o.Value;
                    _cur.EmitSeen.TryGetValue(off, out var c); c++; _cur.EmitSeen[off] = c;
                    if (c > _o.EmitCap) { HaltReason = $"LOOP:line@0x{off:x}×{c}"; return HALT; }
                    string text = _cur.Script.GetString(off);
                    Emitted.Add((off, text, _cur.Script.Name));
                    int layoutSlot = a.Count > 0 ? (int)Read(a[0]) : 0;
                    TextHistory.AppendText(layoutSlot, off, text, _advTextStyle);
                    _host.ShowText(off, text);
                }
                return pc + 1;
            case "define-adv-text-layout": // 0x70: configure layout and begin a logical retained group
                TextHistory.DefineLayout((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                         (int)Read(a[3]), (int)Read(a[4]));
                return pc + 1;
            case "reset-adv-text-layout": // 0x71: reset layout and begin the next logical retained group
            {
                int requestedSlot = (int)Read(a[0]);
                TextHistory.ResetLayout(requestedSlot);
                var layout = TextHistory.GetLayoutSnapshot(requestedSlot);
                _host.SetAdvTextCursor(layout.Slot, layout.CursorX, layout.CursorY);
                _host.ClearRenderedAdvTextLayout(layout.Slot);
                return pc + 1;
            }
            case "set-adv-text-reset-cursor": // 0x79: configure cursor restored by a later 0x71
                TextHistory.SetResetCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
                return pc + 1;
            case "set-adv-text-cursor": // 0x7a (layout slot, x, y); slot 0 means current natively
                TextHistory.SetCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
                _host.SetAdvTextCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-adv-text-bounds": // 0x1c1: layout-local right/bottom overflow boundaries
                TextHistory.SetBounds((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
                return pc + 1;
            case "configure-adv-wait-indicator": // 0x73: per-layout animated input-wait marker
                _host.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
                    (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                    (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
                    (int)Read(a[8]), Read(a[9])));
                return pc + 1;
            case "u0041B9F0":
            case "set-adv-wait-indicator-enabled": // 0x1ce: explicit marker service start/stop
                _host.SetAdvWaitIndicatorEnabled(Read(a[0]) != 0);
                return pc + 1;
            case "u00420CE0":
            case "publish-adv-text-layout": // 0x20a: publish layout and current marker frame if active
                _host.PublishAdvTextLayout((int)Read(a[0]));
                return pc + 1;
            case "draw-string": // 0x204 (surface slot, x, y, string)
                _host.DrawStringToSurface((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), ReadStr(a[3]),
                                          _advTextStyle);
                return pc + 1;
            case "wait-for-input":
                // Faithful headless: no player => halt here rather than plow past every prompt (see VmOptions).
                if (_o.HaltAtWaitForInput) { HaltReason ??= "wait-for-input"; return HALT; }
                // The native ADV chrome is a coroutine: after an earlier 0x93 cancellation its shared
                // registration pass runs again before a stable message wait. Our blocking host models that
                // scheduler boundary by re-arming the frame's retained definitions here.
                bool wakeAtWait = false;
                lock (_interactiveLock)
                {
                    if (_cur.Hotspots.HasDefinitions && !_cur.Hotspots.Armed)
                    {
                        _interactiveFrame = _cur;
                        wakeAtWait = _cur.Hotspots.Arm(_pointerX, _pointerY);
                    }
                }
                if (wakeAtWait) _host.WakeInputCallbackService();
                _host.WaitForInput((int)Read(a[0]), ServiceHotspotCallback,
                    () => new AdvAutoWaitState(_autoMessageEnabled, _autoVoicePending,
                                               _autoMessageTime0Ms, _autoMessageTime1Ms));
                return pc + 1;
            case "u0041BEB0":
            case "register-hotspot-callbacks": // 0x90: inclusive rect + enter/leave/activate local callbacks
                lock (_interactiveLock)
                    _cur.Hotspots.Register((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                                           (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]));
                return pc + 1;
            case "u00415040":
            case "cancel-hotspot-wait": // 0x93
                lock (_interactiveLock)
                {
                    _cur.Hotspots.Reset();
                    if (ReferenceEquals(_interactiveFrame, _cur)) _interactiveFrame = null;
                }
                return pc + 1;
            case "u00415090":
            case "arm-hotspot-wait": // 0x94
            {
                bool wake;
                lock (_interactiveLock)
                {
                    _interactiveFrame = _cur;
                    wake = _cur.Hotspots.Arm(_pointerX, _pointerY);
                }
                if (wake) _host.WakeInputCallbackService();
                return pc + 1;
            }
            case "u0041C150":
            case "bind-hotspot-key": // 0x97: bind a configured logical action to this record
                lock (_interactiveLock)
                    _cur.Hotspots.BindKey((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                          (int)Read(a[3]), (int)Read(a[4]));
                return pc + 1;
            case "u0041B210":
            case "set-cursor-resource": // 0x86: raw indexed .CUR resource
                _host.SetCursorResource(Read(a[0])); return pc + 1;
            case "u00414D10":
            case "clear-cursor-resource": // 0x87
                _host.ClearCursorResource(); return pc + 1;
            case "mouse_callback":
            case "register-mouse-callback": // 0xcc (poll interval ms, local target dword offset)
                _cur.MouseCallbackIntervalMs = System.Math.Max(0, Read(a[0]));
                _cur.MouseCallbackTarget = (int)Read(a[1]);
                _cur.MouseCallbackNextAtMs = _host.InputClockMilliseconds + _cur.MouseCallbackIntervalMs;
                lock (_interactiveLock) _rawInputFrame = _cur;
                return pc + 1;
            case "get-input-type":
            case "dispatch-mouse-callback": // 0xcd
            {
                long now = _host.InputClockMilliseconds;
                if (_cur.MouseCallbackTarget < 0 || now < _cur.MouseCallbackNextAtMs) return pc + 1;
                _cur.MouseCallbackNextAtMs = now + _cur.MouseCallbackIntervalMs;
                if (!_cur.Script.IndexByOffset.TryGetValue(_cur.MouseCallbackTarget, out int target))
                    return pc + 1;
                _cur.CallStack.Add(pc + 1);
                return target;
            }
            case "joy_callback":
            case "register-joy-callback": // 0xfb (input index, local target dword offset)
            {
                int index = (int)Read(a[0]);
                if ((uint)index < 32) _cur.InputCallbackTargets[index] = (int)Read(a[1]);
                return pc + 1;
            }
            case "u0041E360":
            case "set-input-action-count": // 0xfe: actions [0,count), no-input callback at count
            {
                int count = unchecked((int)Read(a[0]));
                if (!InputBindings.SetActionCount(count))
                {
                    HaltReason ??= $"input-action-count-out-of-range:{count}";
                    return HALT;
                }
                return pc + 1;
            }
            case "u00415A10":
            case "poll-joy-callback-input": // 0xff
                _cur.PendingInputCallbackMask = InputBindings.PollActionMask()
                    | Volatile.Read(ref _heldInputCallbackMask)
                    | Interlocked.Exchange(ref _queuedInputCallbackMask, 0);
                _cur.InputCallbackScanIndex = 0;
                return pc + 1;
            case "u00415A60":
            case "dispatch-joy-callbacks": // 0x100
            {
                int actionCount = InputBindings.ActionCount;
                if (_cur.PendingInputCallbackMask == 0)
                {
                    int idleTarget = _cur.InputCallbackTargets[actionCount];
                    if (idleTarget < 0 || !_cur.Script.IndexByOffset.TryGetValue(idleTarget, out int target))
                        return pc + 1;
                    _cur.CallStack.Add(pc + 1);
                    return target;
                }
                while (_cur.InputCallbackScanIndex < actionCount)
                {
                    int index = _cur.InputCallbackScanIndex++;
                    if ((_cur.PendingInputCallbackMask & (1 << index)) == 0) continue;
                    int targetOffset = _cur.InputCallbackTargets[index];
                    if (targetOffset < 0 || !_cur.Script.IndexByOffset.TryGetValue(targetOffset, out int target))
                        continue;
                    // Resume on op 0x100 so another simultaneously active input can dispatch.
                    _cur.CallStack.Add(pc);
                    return target;
                }
                return pc + 1;
            }
            case "u0041E500":
            case "map-joystick-button": // 0x107: button slot N emits action N+4
                InputBindings.MapJoystickButton(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
                return pc + 1;
            case "u00415E70":
            case "get-mouse-button-state": // 0x108
                Write(a[0], Volatile.Read(ref _mouseButtonState)); return pc + 1;
            case "u00415F10":
            case "consume-mouse-wheel-delta": // 0x10d
                Write(a[0], Interlocked.Exchange(ref _mouseWheelDelta, 0)); return pc + 1;
            case "u00415EC0":
            case "get-cursor-virtual": // 0x109
            {
                int x, y;
                lock (_interactiveLock) { x = _pointerX; y = _pointerY; }
                Write(a[0], x == int.MinValue ? 0 : x);
                Write(a[1], y == int.MinValue ? 0 : y);
                return pc + 1;
            }
            case "u0041E540":
            case "set-cursor-virtual": // 0x10a; retain the virtual position even without OS warping
                UpdatePointer((int)Read(a[0]), (int)Read(a[1])); return pc + 1;
            case "u0041E5A0":
            case "map-mouse-button": // 0x10b: physical button -> slot, polled action is slot+4
                InputBindings.MapMouseButton(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
                return pc + 1;
            case "u0041E5E0":
            case "map-keyboard-scancode": // 0x10c: logical action <- DIK translated through native VK table
            {
                int action = unchecked((int)Read(a[0]));
                int dik = unchecked((int)Read(a[1]));
                if (!InputBindings.MapKeyboardScanCode(action, dik))
                {
                    HaltReason ??= $"keyboard-action-out-of-range:{action}";
                    return HALT;
                }
                return pc + 1;
            }
            case "sleep":   // 0xc8 (duration) — pause the host duration ms; headless hosts no-op (parity). Frame pacing.
                _host.Sleep(Read(a[0])); return pc + 1;
            case "u00425960":
            case "begin-timed-callback-sequence": // 0xd3: clear the frame-local relative schedule
                _cur.TimedCallbacks.Clear();
                _cur.TimedCallbackCursor = 0;
                _cur.TimedCallbackStartedAtMs = null;
                _cur.TimedCallbackAbortOffset = -1;
                return pc + 1;
            case "u004266F0":
            case "append-relative-timed-callbacks": // 0xd4: (interval ms, count, on-time PC, catch-up PC)
            {
                long intervalMs = Read(a[0]);
                int count = System.Math.Max(0, unchecked((int)Read(a[1])));
                int primaryOffset = unchecked((int)Read(a[2]));
                int catchUpOffset = unchecked((int)Read(a[3]));
                long deadlineMs = _cur.TimedCallbacks.Count == 0 ? 0 : _cur.TimedCallbacks[^1].DeadlineMs;
                for (int i = 0; i < count; i++)
                {
                    deadlineMs += intervalMs;
                    _cur.TimedCallbacks.Add(new ExecFrame.TimedCallback(
                        deadlineMs, primaryOffset, catchUpOffset));
                }
                return pc + 1;
            }
            case "u004262C0":
            case "run-timed-callback-sequence": // 0xd5: dispatch each scheduled local callback, resuming here after ret
            {
                if (_cur.TimedCallbackStartedAtMs == null)
                {
                    _cur.TimedCallbackStartedAtMs = _host.InputClockMilliseconds;
                    _cur.TimedCallbackAbortOffset = unchecked((int)Read(a[0]));
                }
                // Native op 0xd5 stops at last_index rather than count. The final entry is a
                // look-ahead sentinel: it supplies the next deadline for the preceding event but
                // is not itself dispatched.
                if (_cur.TimedCallbackCursor >= _cur.TimedCallbacks.Count - 1)
                {
                    _cur.TimedCallbackStartedAtMs = null;
                    return pc + 1;
                }

                var callback = _cur.TimedCallbacks[_cur.TimedCallbackCursor];
                long elapsedMs = _host.InputClockMilliseconds - _cur.TimedCallbackStartedAtMs.Value;
                if (elapsedMs < callback.DeadlineMs)
                {
                    _host.Sleep(callback.DeadlineMs - elapsedMs);
                    elapsedMs = _host.InputClockMilliseconds - _cur.TimedCallbackStartedAtMs.Value;
                }

                bool fellBehind = _cur.TimedCallbackCursor + 1 < _cur.TimedCallbacks.Count
                    && _cur.TimedCallbacks[_cur.TimedCallbackCursor + 1].DeadlineMs < elapsedMs;
                int targetOffset = fellBehind ? callback.CatchUpOffset : callback.PrimaryOffset;
                _cur.TimedCallbackCursor++;
                if (targetOffset < 0 || !_cur.Script.IndexByOffset.TryGetValue(targetOffset, out int target))
                    return pc;
                _cur.CallStack.Add(pc);
                return target;
            }
            case "u0041B290":
            case "set-message-skip": // 0x88: persistent all-message fast-forward service state
                _messageSkipEnabled = Read(a[0]) != 0;
                _messageSkipServiceActive = _messageSkipEnabled;
                _host.SetMessageSkipActive(_messageSkipServiceActive);
                return pc + 1;
            case "u00414E50": // 0x19a: persistent state used by the SO001 active overlay
                Write(a[0], _messageSkipEnabled ? 1 : 0); return pc + 1;
            case "u00414E80":
            case "suspend-adv-skip-service": // 0x19b: preserve the toggle while leaving ADV presentation
                _advSkipServiceEnabled = false;
                _messageSkipServiceActive = false;
                _host.SetMessageSkipActive(false);
                _host.SetPhysicalMessageSkipActive(false);
                return pc + 1;
            case "u00414EC0":
            case "resume-adv-skip-service": // 0x19c: recompute active fast-forward on ADV entry
                _advSkipServiceEnabled = true;
                _messageSkipServiceActive = _messageSkipEnabled || _host.IsAdvReadSkipActive;
                _host.SetMessageSkipActive(_messageSkipServiceActive);
                RefreshPhysicalMessageSkipState();
                return pc + 1;
            case "get-message-skip": // 0x1c7: persistent Skip or host-supplied Ctrl fast-forward
                // The native per-op tick continually re-arms the transient run-state bit while the
                // ADV service is enabled. The host channel also carries physical fast-forward input.
                Write(a[0], _messageSkipServiceActive || _host.IsMessageSkipActive ? 1 : 0); return pc + 1;
            case "get-adv-read-skip-state": // 0x1cc: per-message read/click skip service state
            case "get-adv-service-state":   // compatibility with pre-recovery generated tables
                Write(a[0], _host.IsAdvReadSkipActive ? 1 : 0); return pc + 1;
            case "u00414F60":
            case "get-auto-message": // 0x1b6: VM service state used by the ADV redraw callback
                Write(a[0], _autoMessageEnabled ? 1 : 0); return pc + 1;
            case "u0041B640":
            case "set-auto-message": // 0x1b7
                _autoMessageEnabled = Read(a[0]) != 0; return pc + 1;
            case "u0041B670":
            case "get-auto-message-time": // 0x1b8 (selector 0=post-voice Time0, 1=unvoiced Time1, out)
                Write(a[1], Read(a[0]) == 0 ? _autoMessageTime0Ms : _autoMessageTime1Ms); return pc + 1;
            case "u0041B710":
            case "set-auto-message-time": // 0x1b9 (selector, milliseconds)
                if (Read(a[0]) == 0) _autoMessageTime0Ms = Read(a[1]);
                else if (Read(a[0]) == 1) _autoMessageTime1Ms = Read(a[1]);
                return pc + 1;
            case "u00415670":
            case "block-mark":
            case "reset-message-voice-state": // 0x1bc resets native per-message voice/queued-voice state
                _autoVoicePending = false; return pc + 1;
            case "set-text-history-recording": // 0x1bb: HISTORY.BIN suppresses recording its own UI
                if (Read(a[0]) is 0 or 1)
                {
                    bool enabled = Read(a[0]) == 1;
                    TextHistory.SetRecordingEnabled(enabled);
                    if (enabled) _host.EndTextHistoryPresentation();
                }
                return pc + 1;
            case "append-text-history-metadata": // 0x1d2: (metadata type, value)
                TextHistory.AppendMetadata(Read(a[1]), Read(a[0]), _advTextStyle); return pc + 1;
            case "step-text-history": // 0x1d0: cumulative delta from the latest retained boundary
                if (TextHistory.TryStepGroup((int)Read(a[2]), out var historyEntry))
                {
                    Write(a[0], historyEntry.LayoutSlot);
                    Write(a[1], historyEntry.FirstRecordIndex);
                }
                else
                {
                    Write(a[0], -1);
                    Write(a[1], -1);
                }
                return pc + 1;
            case "render-text-history": // 0x1d1: rasterize/bind one retained group to a target layout
            case "u0041BAE0":
            {
                int flags = (int)Read(a[2]);
                if ((flags & 4) == 0 && TextHistory.TryBuildRenderBatch(
                        (int)Read(a[0]), (int)Read(a[1]), flags, Read(a[3]), Read(a[4]), out var batch))
                    _host.RenderTextHistory(batch with
                    {
                        // Native History uses the text manager's current leading, not a retained-record field.
                        Style = batch.Style with { LineSpacing = _advTextStyle.LineSpacing }
                    });
                return pc + 1;
            }
            case "u0041BB90":
            case "find-text-history-value": // 0x1d3: operand 3 is accepted but ignored natively
            {
                bool found = TextHistory.TryFindMetadata((int)Read(a[3]), Read(a[4]), out long value);
                Write(a[0], found ? 1 : 0);
                Write(a[1], value);
                return pc + 1;
            }
            case "u0041BC00":
            case "find-text-history-pair": // 0x1d4: operand 3 is accepted but ignored natively
                TextHistory.TryFindVoicePair((int)Read(a[3]), out long voiceId, out long voiceArgument);
                Write(a[0], voiceId);
                Write(a[1], voiceArgument);
                return pc + 1;
            case "clear-text-history": // 0x85: bound the backlog to the current ordinary ADV block
                TextHistory.Clear(); return pc + 1;
            case "set-font-size":
                _advTextStyle = _advTextStyle with { PrimaryFontSize = (int)Read(a[0]) }; return pc + 1;
            case "set-ruby-font-size":
                _advTextStyle = _advTextStyle with { RubyFontSize = (int)Read(a[0]) }; return pc + 1;
            case "u0041B3D0":
            case "set-text-line-spacing":
                _advTextStyle = _advTextStyle with { LineSpacing = (int)Read(a[0]) }; return pc + 1;
            case "set-font-bold":
                _advTextStyle = _advTextStyle with { Bold = Read(a[0]) != 0 }; return pc + 1;
            case "set-text-color":
                _advTextStyle = _advTextStyle with { TextColor = Read(a[0]) }; return pc + 1;
            case "set-text-effect-color":
                _advTextStyle = _advTextStyle with { EffectColor = Read(a[0]) }; return pc + 1;
            case "set-text-render-mode":
                _advTextStyle = _advTextStyle with { RenderMode = (int)Read(a[0]) }; return pc + 1;
            case "set-text-effect-offset":
                _advTextStyle = _advTextStyle with
                {
                    EffectOffsetX = (int)Read(a[0]),
                    EffectOffsetY = (int)Read(a[1])
                };
                return pc + 1;
            case "set-adv-text-layout-origin": // 0x198; slot 0 selects the current layout
            case "u0041B540":
                TextHistory.SetLayoutOrigin((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]));
                return pc + 1;
            case "get-message-window-alpha": // 0x131: host profile/config seam; default host currently supplies 0
            case "u00415F70":
                Write(a[0], _host.MessageWindowAlphaSetting); return pc + 1;
            case "u00415BF0":
            case "reset-message-skip-input": // 0x101 clears transient input/run bits, not op 0x88 state
                return pc + 1;
            case "end-text-line": case "set-font":
            case "comment": case "display-furigana": case "dev_ukn":
                return pc + 1;
            case "create-texture":   // 0x1f8 (slot)(w)(h) — allocate a blank surface at the slot
                _host.ReleaseSurface((int)Read(a[0]));
                Gfx.ClearSurface((int)Read(a[0]));
                _host.CreateTexture((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-texture":   // 0x1f9 (resId)(slot)(colorkey) — load a file into the slot's surface
            {
                long requestedResourceId = Read(a[0]);
                long resolvedResourceId = _host.ResolveTextureResourceId(requestedResourceId);
                if (_diagSetTexture)   // AGE_DIAG_SETTEX: log the SLOT operand source (literal vs which global) — grey-BG slot dig
                    System.Console.Error.WriteLine($"[settex] resId=0x{requestedResourceId:x}->0x{resolvedResourceId:x} slot={(int)Read(a[1])} " +
                        $"slotOp=(type={a[1].Type} val=0x{a[1].Value:x}){(a[1].Type == 3 ? $" G[0x{a[1].Value:x}]" : "")}");
                _host.ReleaseSurface((int)Read(a[1]));
                Gfx.SetSurface((int)Read(a[1]), resolvedResourceId, a.Count > 2 ? Read(a[2]) : 0);
                _host.SetTexture(resolvedResourceId, (int)Read(a[1]));
                return pc + 1;   // host still tracks dims for get-texture-size
            }
            case "u00422EB0":                 // pre-reference compatibility
            case "load-raw-texture-surface":  // 0x249 (raw catalog id)(slot)(colorkey)
            {
                // Native shares 0x1f9's release/load/colorkey path, but constructs its mode-1
                // surface subclass and receives an already-global SYS4INI catalog index. The
                // CPU compositor does not need the D3D subclass distinction; it does need the
                // resource id to bypass the executing script's scene-section normalization.
                long rawResourceId = Read(a[0]);
                int surfaceSlot = (int)Read(a[1]);
                _host.ReleaseSurface(surfaceSlot);
                Gfx.SetSurface(surfaceSlot, rawResourceId, Read(a[2]));
                _host.SetTexture(rawResourceId, surfaceSlot);
                return pc + 1;
            }
            case "draw-texture":   // 0x1fb (handle)(slot)(srcX)(srcY)(w)(h)(dstX)(dstY) — bind object -> surface + rect + pos
                Gfx.BindDraw(Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                             (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]));
                _host.DrawTexture((int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
                                  (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])); return pc + 1;   // IHost seam (oracle log; Godot no-ops)
            case "u0041F3A0":
            case "register-numeric-glyph-style": // 0x13a: (style)(surface)(atlas x/y)(digit w/h)
            {
                int styleIndex = unchecked((int)Read(a[0]));
                if (!Gfx.RegisterNumericGlyphStyle(styleIndex, unchecked((int)Read(a[1])),
                        unchecked((int)Read(a[2])), unchecked((int)Read(a[3])),
                        unchecked((int)Read(a[4])), unchecked((int)Read(a[5]))))
                {
                    HaltReason ??= $"numeric-glyph-style-index-out-of-range:{styleIndex}";
                    return HALT;
                }
                return pc + 1;
            }
            case "u00422460":
            case "draw-decimal-glyphs": // 0x23b: retained decimal glyph draw
            {
                int styleIndex = unchecked((int)Read(a[1]));
                if (!Gfx.DrawDecimalGlyphs(Read(a[0]), styleIndex, unchecked((int)Read(a[2])),
                        unchecked((int)Read(a[3])), unchecked((int)Read(a[4])),
                        unchecked((int)Read(a[5])), unchecked((int)Read(a[6]))))
                {
                    HaltReason ??= (uint)styleIndex >= 11
                        ? $"numeric-glyph-style-index-out-of-range:{styleIndex}"
                        : $"numeric-glyph-style-unregistered:{styleIndex}";
                    return HALT;
                }
                return pc + 1;
            }
            case "get-texture-size":   // 0x208 (slot) (out_w) (out_h)
            {
                var (gw, gh) = _host.GetTextureSize((int)Read(a[0]));
                Write(a[1], gw); Write(a[2], gh);
                return pc + 1;
            }
            case "fill-surface-rect": // 0x20b: clipped alpha/RGB fill of a mutable surface
            case "u00420D50":
                _host.FillSurfaceRect(new SurfaceRectFill(
                    (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
                    (int)System.Math.Min(Read(a[5]), 255), Read(a[6]) & 0x00ff_ffff));
                return pc + 1;
            case "clear-retained-gfx-objects": // 0x1f6: erase object records, but preserve surfaces
                Gfx.ClearRetainedObjects(); return pc + 1;
            case "select-render-target": // 0x20d: slot <1000 selects a surface; >=1000 restores backbuffer
                Gfx.SelectRenderTarget(Read(a[0])); return pc + 1;
            case "clear-render-target": // 0x20e: clear color to black and depth to one
                _host.ClearRenderTarget(Gfx.CurrentRenderTargetSlot); return pc + 1;
            case "release-transient-surfaces": // 0x23d: native fixed range [42,1000)
                Gfx.ReleaseSurfaceRange(42, 1000 - 42);
                _host.ReleaseSurfaceRange(42, 1000 - 42); return pc + 1;
            case "play-bgm":   _host.PlayBgm(Read(a[0])); return pc + 1;
            case "play-voice":
                _autoVoicePending = true;
                TextHistory.AppendVoice(Read(a[0]), 0, _advTextStyle);
                _host.PlayVoice(Read(a[0]), 0); return pc + 1;
            case "play-history-voice": // 0x1bd: native voice start/history argument is one
            case "u0041D910":
                _autoVoicePending = true;
                TextHistory.AppendVoice(Read(a[0]), 1, _advTextStyle);
                _host.PlayVoice(Read(a[0]), 1); return pc + 1;
            case "set-voice-bgm-duck-control": // 0x1cf: bit 0 suppresses automatic voice ducking
                _host.SetVoiceBgmDuckControl(Read(a[0])); return pc + 1;
            case "play-sound-effect":   // 0xb4 / semantics: sfx-load
                _host.LoadSoundEffect(Read(a[0]), (int)Read(a[1])); return pc + 1;
            case "u0041D050":           // 0xb5 / semantics: sfx-start
                _host.StartSoundEffect((int)Read(a[0])); return pc + 1;
            case "u0041D080":           // 0xb6 / semantics: sfx-release
                _host.ReleaseSoundEffect((int)Read(a[0])); return pc + 1;
            case "schedule-sfx-start":  // 0x2bf / native SetDelay(channel, start mode, delay ms)
                _host.ScheduleSoundEffectStart((int)Read(a[0]), (int)Read(a[1]), Read(a[2])); return pc + 1;
            case "u0041D2B0":           // 0xc2 / semantics: fade-bgm
                _host.FadeBgm((int)Read(a[0]), Read(a[1])); return pc + 1;
            case "u00415880":           // 0xd9 / semantics: clear-run-state-0x1000
                return pc + 1;
            case "get-initial-root-run": // 0x130 (out)
                Write(a[0], _initialRootRun ? 1 : 0);
                return pc + 1;
            case "play-modal-movie-to-surface": // 0x20f (raw resource)(surface)(movie flags)
            {
                long rawResourceId = Read(a[0]);
                int surfaceSlot = (int)Read(a[1]);
                // The modal and scene-local paths share retained-surface composition. The host's
                // distinct entry point preserves 0x20f's raw-id resolver and blocking lifecycle.
                Gfx.SetSurface(surfaceSlot, rawResourceId, 0);
                _host.PlayModalMovieToSurface(rawResourceId, surfaceSlot, Read(a[2]));
                return pc + 1;
            }
            case "u004221A0":           // pre-reference compatibility
            case "play-movie-to-surface": // 0x236 (resource)(surface)(movie flags)(sync mask)
            {
                long resourceId = Read(a[0]);
                int surfaceSlot = (int)Read(a[1]);
                // Native SC0000's warm-engine trace evaluates this existing site as surface 0. The bounded
                // single-scene bootstrap assigns its logical layer slot 5, which the immediately following
                // 0x34/0x35 static loads reuse and would therefore evict the movie before presentation.
                // Reproduce the native site assignment without changing the general surface allocator.
                if (ins.Offset == 0x13c8 && _cur.Script.Name.StartsWith("SC0000", StringComparison.OrdinalIgnoreCase)
                    && surfaceSlot != 0)
                {
                    int logicalLayer = (int)Globals.GetValueOrDefault(0x62450);
                    long movieHandle = Globals.GetValueOrDefault(0x62455 + logicalLayer);
                    Gfx.RemapObjectSurface(movieHandle, surfaceSlot, 0);
                    surfaceSlot = 0;
                }
                // Graph construction is synchronous. Keep the existing created surface blank during that
                // boundary; publishing the movie resource first would let a concurrent compositor mistake
                // the MPEG payload's .AGF name for a still image before the host registers/decodes it.
                long? stopTimeMs = _host.PlayMovieToSurface(resourceId, surfaceSlot, Read(a[2]), Read(a[3]));
                // The native CMovieToTexture renderer replaces the pixels of the already-created surface.
                // Retain the same resource binding so the compositor resolves live movie frames for its objects.
                Gfx.SetSurface(surfaceSlot, resourceId, 0);
                Gfx.SetMovieStopTime(surfaceSlot, stopTimeMs);
                return pc + 1; // native cmd size 9 resumes at the next instruction; playback is asynchronous
            }
            // ---- gfx command-buffer ops (VM-internal GfxState; docs/engine-re.md op-contract table) ----
            case "query-gfx-object?":   // 0x215 (out)(handle) -> slot | -1
                if (_diagSetTexture)   // reuse the flag: show what the slot query returns (grey-BG slot dig)
                {
                    long h = Read(a[1]);
                    System.Console.Error.WriteLine($"[query] handle=0x{h:x} handleOp=(type={a[1].Type} val=0x{a[1].Value:x}) " +
                        $"-> QuerySlot={Gfx.QuerySlot(h)} objectPresent={Gfx.TryGet(h) != null}");
                }
                Write(a[0], Gfx.QuerySlot(Read(a[1]))); return pc + 1;
            case "query-gfx-field?":    // 0x216 (out)(idx)
                Write(a[0], Gfx.QueryField(Read(a[1]))); return pc + 1;
            case "get-gfx-geom3?":      // 0x218 (handle)(outA)(outB)(outC) <- V18
            {
                var v = Gfx.TryGet(Read(a[0]))?.V18 ?? default;
                Write(a[1], v.X); Write(a[2], v.Y); Write(a[3], v.Z); return pc + 1;
            }
            case "get-gfx-geom3-b?":    // 0x21a (handle)(outA)(outB)(outC) <- V24
            {
                var v = Gfx.TryGet(Read(a[0]))?.V24 ?? default;
                Write(a[1], v.X); Write(a[2], v.Y); Write(a[3], v.Z); return pc + 1;
            }
            case "set-gfx-geom3":       // 0x217 (handle)(a)(b)(c) -> V18
                Gfx.GetOrCreate(Read(a[0])).V18 = (Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            case "set-gfx-geom3-b":     // 0x219 (handle)(a)(b)(c) -> V24
                Gfx.GetOrCreate(Read(a[0])).V24 = (Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            case "u0041AF00":           // 0x80: default object slot substituted by native op 0x1d9
            case "set-default-gfx-object-slot":
                Gfx.SetDefaultObjectSlot((int)Read(a[0])); return pc + 1;

            // ---- SC0000 anim/transform/spritesheet cluster (docs/engine-re.md §"SC0000 anim ... cluster") ----
            case "u00421DD0":   // 0x22f set-position: (handle)(op2)(x)(y)(z) -> base position (direct set)
                Gfx.GetOrCreate(Read(a[0])).V24 = (Read(a[2]), Read(a[3]), Read(a[4])); return pc + 1;
            case "u004219E0":                  // pre-reference compatibility
            case "set-gfx-range-transform":   // 0x229 (first)(count)(anchor x/y/z)
                Gfx.SetRangeTransform(Read(a[0]), Read(a[1]), (Read(a[2]), Read(a[3]), Read(a[4])));
                return pc + 1;
            case "u00421A90":                  // pre-reference compatibility
            case "set-gfx-range-scale-current": // 0x22a (sx%)(sy%)(sz%)
                Gfx.SetRangeScaleCurrent((Read(a[0]), Read(a[1]), Read(a[2]))); return pc + 1;
            case "u00421BD0":                  // pre-reference compatibility
            case "set-gfx-range-translation-current": // 0x22c (tx)(ty)(tz)
                Gfx.SetRangeTranslationCurrent((Read(a[0]), Read(a[1]), Read(a[2]))); return pc + 1;
            case "u00421C60":                  // pre-reference compatibility
            case "set-gfx-range-scale-target": // 0x22d (delay)(duration)(sx%)(sy%)(sz%)
                Gfx.SetRangeScaleChannel(Read(a[0]), Read(a[1]), (Read(a[2]), Read(a[3]), Read(a[4])));
                return pc + 1;
            case "u004223C0":   // 0x239 spritesheet cell: (handle)(delay)(duration)(frame count)(columns)(cell)
                Gfx.SetSrcRect(Read(a[0]), Read(a[3]), Read(a[4]), Read(a[5]), 0); return pc + 1;
            case "u00421EA0":   // 0x231 looping spritesheet: (handle)(ms per frame)(frame count)(columns)
                Gfx.SetSrcRect(Read(a[0]), Read(a[2]), Read(a[3]), 0, Read(a[1])); return pc + 1;
            case "u00421EF0":   // 0x232 cyclic packed ARGB; negative alpha/RGB preserve static obj color
                Gfx.SetColorAnimResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            case "u00421940":   // 0x228: (succ)(handle)(outX)(outY)(outZ) <- target translation matrix
            {
                if (Gfx.TryQueryTranslationTarget(Read(a[1]), out var v))
                {
                    Write(a[2], (long)v.X); Write(a[3], (long)v.Y); Write(a[4], (long)v.Z);
                    Write(a[0], 0);
                }
                else Write(a[0], 1);   // native missing-object path leaves output operands untouched
                return pc + 1;
            }
            case "u00422930":                    // pre-reference compatibility
            case "query-surface-stop-time-ms":   // 0x23f (out_stop_time_ms)(surface_slot)
            {
                int surfaceSlot = (int)Read(a[1]);
                if (!Gfx.TryGetMovieStopTime(surfaceSlot, out long? stopTimeMs))
                {
                    Write(a[0], -1); // native null CMovieToTexture slot
                    return pc + 1;
                }
                if (!stopTimeMs.HasValue)
                {
                    _host.ReportWarning(
                        $"movie stop-time unavailable {_cur.Script.Name}@0x{ins.Offset:x} " +
                        $"surface={surfaceSlot}; returning -1");
                    Write(a[0], -1);
                    return pc + 1;
                }
                Write(a[0], stopTimeMs.Value);
                return pc + 1;
            }
            case "set-gfx-geom3-c":     // 0x1ff: set current translation matrix
                Gfx.SetCurrentTranslation(Read(a[0]), (Read(a[1]), Read(a[2]), Read(a[3]))); return pc + 1;
            case "u00420620":             // upstream ABI label
            case "gfx-set-scale-current": // 0x1fd (handle)(sx%)(sy%)(sz%) -> current scale matrix
                Gfx.SetCurrentScale(Read(a[0]), (Read(a[1]), Read(a[2]), Read(a[3]))); return pc + 1;
            case "set-gfx-field64":     // 0x212 (idx)(val)
                Gfx.GetOrCreate(Read(a[0])).Field64 = Read(a[1]); return pc + 1;
            case "set-gfx-xy":          // 0x213 (idx)(x)(y)
            {
                var o = Gfx.GetOrCreate(Read(a[0])); o.Field68 = Read(a[1]); o.Field6c = Read(a[2]); return pc + 1;
            }
            case "gfx-elem-erase":      // 0x1f7 (handle)(count) — erase retained-object range
                Gfx.EraseRange(Read(a[0]), Read(a[1])); return pc + 1;
            case "gfx-elem-release":    // 0x1fa (surface slot)
                _host.ReleaseSurface((int)Read(a[0])); Gfx.ClearSurface((int)Read(a[0])); return pc + 1;
            case "clone-gfx-object":    // 0x21d (source handle)(destination handle)
                Gfx.CloneObject(Read(a[0]), Read(a[1])); return pc + 1;
            case "gfx-blit-color":      // 0x202 (handle)(delay)(duration)(alpha)(color) — one-shot color
                Gfx.SetAnimatedObjectColorResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3]), Read(a[4]));
                return pc + 1;
            case "gfx-draw-color":      // 0x203 (handle)(v)(alpha)(color) — static alpha/tint
                Gfx.SetStaticObjectColorResolved(Read(a[0]), Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            // ---- sprite transform / animation cluster (docs/engine-re.md "0x21c-0x243 ... ANIMATION") ----
            case "set-anim-transform-abs":   // 0x220 (handle)(delay)(duration)(tx)(ty)(tz)
                Gfx.SetTranslationChannel(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5]))); return pc + 1;
            case "set-anim-transform-norm":  // 0x21e (handle)(delay)(duration)(sx%)(sy%)(sz%)
                Gfx.SetScaleChannel(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5]))); return pc + 1;
            case "set-anim-rotation-axis-angle": // 0x21f (handle)(delay)(duration)(axis x/y/z)(angle deg)
                Gfx.SetRotationChannel(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5])), Read(a[6])); return pc + 1;
            case "anim-start":   // 0x234 legacy name: (handle)(period)(axis x/y/z), cyclic rotation channel
                Gfx.SetRotationCycle(Read(a[0]), Read(a[1]), (Read(a[2]), Read(a[3]), Read(a[4]))); return pc + 1;
            case "set-anim-clock":   // 0x238 (duration) — global, non-blocking (host advances it per-frame)
                Gfx.SetAnimClock(Read(a[0])); return pc + 1;
            case "set-object-animation-detached": // 0x242 (handle)(flags): bit 0 is nonblocking/force-proof
                Gfx.SetOneShotAnimationControl(Read(a[0]), Read(a[1])); return pc + 1;
            case "reset-anim-clock": // 0x243: force unprotected one-shots and reset the global service clock
                Gfx.ResetAnimClock(); return pc + 1;
            case "queue-surface-alpha-transition": // 0x223: target surface crossfade over two object ranges
                Gfx.QueueSurfaceAlphaTransition(Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
                    Read(a[4]), (int)Read(a[5]), Read(a[6]), Read(a[7])); return pc + 1;
            case "present-frame": // 0x20c: read/message-skip path snaps a queued transition to its endpoint
                _host.PresentFrame(Gfx); return pc + 1;
            case "crossfade-surfaces": // 0x25: legacy full-frame surface alpha transition
            case "u00418B40":
                _host.CrossfadeSurfaces(Gfx, (int)Read(a[0]), (int)Read(a[1]), Read(a[2]));
                return pc + 1;
            case "mark-frame-yield": // 0x21c: normal foreground-transition scheduler/resume boundary
                _host.WaitForForegroundTransition(Gfx); return pc + 1;
            case "clear-gfx-command-queue": // 0x224: retained compositor does not use this native queue
                return pc + 1;
            case "present-gfx-object-range": // 0x222: publish pending retained changes in the selected range
            case "u004216C0":
                _host.PresentObjectRange(Gfx, Read(a[0]), Read(a[1])); return pc + 1;
            default:
                // Stub is per-instruction frequency (the VM handles ~30 ops; the rest hit here, e.g.
                // 0x258/0x259 stmt markers appear en masse), so gate it with Step — else --trace floods.
                if (_sink.TracingSteps) _sink.Emit(TraceEvent.Stub(op, pc)); return pc + 1;
        }
    }

}

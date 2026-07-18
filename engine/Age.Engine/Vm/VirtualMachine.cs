using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
namespace Age.Engine.Vm;

public sealed class VirtualMachine
{
    private const long NoJump = 0xFFFFFFFF;
    private const int HALT = int.MinValue;
    private const int FRAME_RETURN = int.MinValue + 1;
    private const int HOTSPOT_RETURN = int.MinValue + 2;
    private const int SceneEntryCoroutineGate = 0xaba5c;
    private const int T_IMM = 0, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_GPTR = 6,
                      T_GSTRPTR = 8, T_LINT = 9, T_LFLOAT = 10, T_LSTR = 11, T_LPTR = 12,
                      T_LSTRPTR = 14;

    private readonly Script _s;
    private readonly OpcodeTable _t;
    private readonly IHost _host;
    private readonly VmOptions _o;
    private readonly IScriptProvider? _provider;
    private static readonly bool _diagSetTexture = System.Environment.GetEnvironmentVariable("AGE_DIAG_SETTEX") == "1";
    private ExecFrame _cur = null!;
    private int _depth;
    private readonly ITraceSink _sink;
    private readonly object _interactiveLock = new();
    private ExecFrame? _interactiveFrame;
    private int _pointerX = int.MinValue, _pointerY = int.MinValue;
    private bool _autoMessageEnabled;
    private long _autoMessageTime0Ms = 500;
    private long _autoMessageTime1Ms = 2000;
    private bool _autoVoicePending;
    public long CallScriptDispatches { get; private set; }

    public Dictionary<int, long> Globals { get; } = new();
    /// <summary>Native/profile-owned values read by scripts but maintained outside script-visible writes.</summary>
    public Dictionary<int, long> ExternalGlobals { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    public GfxState Gfx { get; } = new();
    public List<(int Offset, string Text, string Script)> Emitted { get; } = new();
    public string? HaltReason { get; private set; }
    public long Steps { get; private set; }
    public bool AutoMessageEnabled => _autoMessageEnabled;

    public VirtualMachine(Script s, OpcodeTable t, IHost host, VmOptions? o = null,
                          IScriptProvider? provider = null, ITraceSink? sink = null)
    { _s = s; _t = t; _host = host; _o = o ?? new VmOptions(); _provider = provider;
      _sink = sink ?? NullTraceSink.Instance; }

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
        T_LPTR => Gi(Globals, (int)Gi(_cur.Locals.P, (int)op.Value)),
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
            case T_LPTR: Globals[(int)Gi(_cur.Locals.P, (int)op.Value)] = val; break;
        }
    }

    private string ReadStr(Operand op) => op.Type switch
    {
        T_STR => _cur.Script.GetString((int)op.Value),
        T_GSTR => Gs(GlobalStrings, (int)op.Value),
        T_GSTRPTR => Gs(GlobalStrings, (int)Gi(Globals, (int)op.Value)),
        T_LSTR => Gs(_cur.Locals.S, (int)op.Value),
        T_LSTRPTR => Gs(GlobalStrings, (int)Gi(_cur.Locals.SP, (int)op.Value)),
        _ => "",
    };

    private void WriteStr(Operand op, string val)
    {
        switch (op.Type)
        {
            case T_GSTR: GlobalStrings[(int)op.Value] = val; break;
            case T_GSTRPTR: GlobalStrings[(int)Gi(Globals, (int)op.Value)] = val; break;
            case T_LSTR: _cur.Locals.S[(int)op.Value] = val; break;
            case T_LSTRPTR: GlobalStrings[(int)Gi(_cur.Locals.SP, (int)op.Value)] = val; break;
        }
    }

    private long BaseAddr(Operand op) => op.Type switch
    {
        T_IMM or T_GINT or T_GFLOAT or T_GSTR or T_GPTR or T_GSTRPTR => op.Value,
        T_LINT => Gi(_cur.Locals.I, (int)op.Value),
        T_LPTR => Gi(_cur.Locals.P, (int)op.Value),
        _ => op.Value,
    };

    private void LookupStore(Operand dst, long addr)
    {
        switch (dst.Type)
        {
            case T_LPTR: _cur.Locals.P[(int)dst.Value] = addr; break;
            case T_LSTRPTR: _cur.Locals.SP[(int)dst.Value] = addr; break;
            case T_GPTR: Globals[(int)dst.Value] = addr; break;
            case T_GSTRPTR: Globals[(int)dst.Value] = addr; break;
            default: Write(dst, Gi(Globals, (int)addr)); break;
        }
    }

    private enum FrameOutcome { Returned, Halted, RanOff }

    public void Run(int entryOffset = 0)
    {
        // The native scheduler supplies this scene-entry state outside script-visible global writes.
        // Restrict it to the byte-identical ADV LABEL/J idiom; op 0x140 also has an unrelated TITLE use.
        if (entryOffset == 0 && _s.Instructions.Any(ins => IsAdvLabeledYield(_s, ins)))
            Globals[SceneEntryCoroutineGate] = 1;

        var top = new ExecFrame(_s, _s.IndexByOffset.TryGetValue(entryOffset, out var idx) ? idx : 0);
        var outcome = RunFrame(top, FrameCause.TopScene);
        if (outcome == FrameOutcome.RanOff) HaltReason ??= "pc-out-of-range";
        else if (outcome == FrameOutcome.Returned) HaltReason ??= "exit";
        // Halted: HaltReason already set by the halting op.
        _sink.Emit(TraceEvent.Halt(HaltReason ?? "unknown", Steps));
    }

    private FrameOutcome RunFrame(ExecFrame frame, FrameCause cause, long callId = 0)
    {
        ExecFrame? previousInteractiveFrame;
        lock (_interactiveLock) previousInteractiveFrame = _interactiveFrame;
        var prev = _cur; _cur = frame; _depth++;
        _sink.Emit(TraceEvent.FrameEnter(frame.Script.Name, _depth, cause, callId));
        var outcome = FrameOutcome.RanOff;
        int pc = frame.Pc;
        while (pc >= 0 && pc < frame.Script.Instructions.Count)
        {
            if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; outcome = FrameOutcome.Halted; break; }
            Steps++;
            if (_sink.TracingSteps) _sink.Emit(TraceEvent.Step(pc, frame.Script.Instructions[pc], _depth));
            int next = Step(frame.Script.Instructions[pc], pc);
            _host.FrameYield();
            if (next == FRAME_RETURN) { outcome = FrameOutcome.Returned; break; }
            if (next == HALT) { outcome = FrameOutcome.Halted; break; }
            pc = next;
        }
        _sink.Emit(TraceEvent.FrameExit(frame.Script.Name, _depth, outcome.ToString()));
        lock (_interactiveLock)
        {
            if (cause == FrameCause.CallScript)
                _interactiveFrame = previousInteractiveFrame?.Hotspots.Armed == true
                    ? previousInteractiveFrame : null;
            else if (ReferenceEquals(_interactiveFrame, frame))
                _interactiveFrame = null;
        }
        _cur = prev; _depth--;
        return outcome;
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
                if (next == HOTSPOT_RETURN || next == FRAME_RETURN) break;
                if (next == HALT) break;
                pc = next;
            }
            // A malformed callback must not leave its sentinel in the page's ordinary local-call stack.
            int sentinel = _cur.CallStack.LastIndexOf(HOTSPOT_RETURN);
            if (sentinel >= 0) _cur.CallStack.RemoveAt(sentinel);
        }
        lock (_interactiveLock)
            _interactiveFrame?.Hotspots.RearmAfterCallback(_pointerX, _pointerY);
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
            case "lt":  Write(a[0], Read(a[1]) <  Read(a[2]) ? 1 : 0); return pc + 1;
            case "lte": Write(a[0], Read(a[1]) <= Read(a[2]) ? 1 : 0); return pc + 1;
            case "gr":  Write(a[0], Read(a[1]) >  Read(a[2]) ? 1 : 0); return pc + 1;
            case "gre": Write(a[0], Read(a[1]) >= Read(a[2]) ? 1 : 0); return pc + 1;
            case "mov":
            case "set-string":
                if (IsStr(a[0]) || IsStr(a[1])) WriteStr(a[0], ReadStr(a[1]));
                else Write(a[0], Read(a[1]));
                return pc + 1;
            case "lookup-array":
                LookupStore(a[0], BaseAddr(a[1]) + Read(a[2])); return pc + 1;
            case "lookup-array-2d":
                LookupStore(a[0], BaseAddr(a[1]) + Read(a[2]) * Read(a[3]) + Read(a[4])); return pc + 1;
            case "bit-set": Write(a[0], Read(a[0]) | Read(a[1])); return pc + 1;
            case "bit-reset": Write(a[0], Read(a[0]) & ~Read(a[1])); return pc + 1;
            case "check-bit": Write(a[0], (Read(a[1]) >> (int)(Read(a[2]) & 31)) & 1); return pc + 1;
            case "copy-to-global": Write(a[0], Read(a[1])); return pc + 1;
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
            case "u0041ADB0":
            case "coroutine-save-yield-handlers":   // 0x7b: retain native handler metadata
                _cur.CoroutineYieldHandlerA = (int)Read(a[0]);
                _cur.CoroutineYieldHandlerB = (int)Read(a[1]);
                return pc + 1;
            case "u00416A90":
            case "coroutine-resume":                // 0x7c: host FrameYield/FrameClock owns re-entry
                return pc + 1;
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
            case "exit":
            case "exit-script": return FRAME_RETURN;
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
                    _host.ShowText(off, text);
                }
                return pc + 1;
            case "set-adv-text-cursor": // 0x7a (layout slot, x, y); slot 0 means current natively
                _host.SetAdvTextCursor((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "configure-adv-wait-indicator": // 0x73: per-layout animated input-wait marker
                _host.ConfigureAdvWaitIndicator(new AdvWaitIndicatorConfig(
                    (int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                    (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]),
                    (int)Read(a[8]), Read(a[9])));
                return pc + 1;
            case "draw-string": // 0x204 (surface slot, x, y, string)
                _host.DrawStringToSurface((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), ReadStr(a[3]));
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
            case "bind-hotspot-key": // 0x97: keyboard/pad routing is a later host-input slice
                lock (_interactiveLock)
                    _cur.Hotspots.BindKey((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2]),
                                          (int)Read(a[3]), (int)Read(a[4]));
                return pc + 1;
            case "sleep":   // 0xc8 (duration) — pause the host duration ms; headless hosts no-op (parity). Frame pacing.
                _host.Sleep(Read(a[0])); return pc + 1;
            case "get-message-skip": // 0x1c7: Ctrl/message fast-forward run-state bit
                Write(a[0], _host.IsMessageSkipActive ? 1 : 0); return pc + 1;
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
            case "end-text-line": case "set-font":
            case "comment": case "display-furigana": case "dev_ukn":
                return pc + 1;
            case "create-texture":   // 0x1f8 (slot)(w)(h) — allocate a blank surface at the slot
                _host.ReleaseSurface((int)Read(a[0]));
                Gfx.ClearSurface((int)Read(a[0]));
                _host.CreateTexture((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-texture":   // 0x1f9 (resId)(slot)(colorkey) — load a file into the slot's surface
                if (_diagSetTexture)   // AGE_DIAG_SETTEX: log the SLOT operand source (literal vs which global) — grey-BG slot dig
                    System.Console.Error.WriteLine($"[settex] resId=0x{Read(a[0]):x} slot={(int)Read(a[1])} " +
                        $"slotOp=(type={a[1].Type} val=0x{a[1].Value:x}){(a[1].Type == 3 ? $" G[0x{a[1].Value:x}]" : "")}");
                _host.ReleaseSurface((int)Read(a[1]));
                Gfx.SetSurface((int)Read(a[1]), Read(a[0]), a.Count > 2 ? Read(a[2]) : 0);
                _host.SetTexture(Read(a[0]), (int)Read(a[1])); return pc + 1;   // host still tracks dims for get-texture-size
            case "draw-texture":   // 0x1fb (handle)(slot)(srcX)(srcY)(w)(h)(dstX)(dstY) — bind object -> surface + rect + pos
                Gfx.BindDraw(Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                             (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]));
                _host.DrawTexture((int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
                                  (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])); return pc + 1;   // IHost seam (oracle log; Godot no-ops)
            case "get-texture-size":   // 0x208 (slot) (out_w) (out_h)
            {
                var (gw, gh) = _host.GetTextureSize((int)Read(a[0]));
                Write(a[1], gw); Write(a[2], gh);
                return pc + 1;
            }
            case "play-bgm":   _host.PlayBgm(Read(a[0])); return pc + 1;
            case "play-voice":
                _autoVoicePending = true;
                _host.PlayVoice(Read(a[0])); return pc + 1;
            case "play-sound-effect":   // 0xb4 / semantics: sfx-load
                _host.LoadSoundEffect(Read(a[0]), (int)Read(a[1])); return pc + 1;
            case "u0041D050":           // 0xb5 / semantics: sfx-start
                _host.StartSoundEffect((int)Read(a[0])); return pc + 1;
            case "u0041D080":           // 0xb6 / semantics: sfx-release
                _host.ReleaseSoundEffect((int)Read(a[0])); return pc + 1;
            case "u0041D2B0":           // 0xc2 / semantics: fade-bgm
                _host.FadeBgm((int)Read(a[0]), Read(a[1])); return pc + 1;
            case "u00415880":           // 0xd9 / semantics: clear-run-state-0x1000
                return pc + 1;
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
                // The native CMovieToTexture renderer replaces the pixels of the already-created surface.
                // Retain the same resource binding so the compositor resolves live movie frames for its objects.
                Gfx.SetSurface(surfaceSlot, resourceId, 0);
                _host.PlayMovieToSurface(resourceId, surfaceSlot, Read(a[2]), Read(a[3]));
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

            // ---- SC0000 anim/transform/spritesheet cluster (docs/engine-re.md §"SC0000 anim ... cluster") ----
            case "u00421DD0":   // 0x22f set-position: (handle)(op2)(x)(y)(z) -> base position (direct set)
            case "u004219E0":   // 0x229 set-position2: same shape, direct position
                Gfx.GetOrCreate(Read(a[0])).V24 = (Read(a[2]), Read(a[3]), Read(a[4])); return pc + 1;
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
            case "u00422930":   // 0x23f query-object: (out)(handle) <- 0 if the object exists, else -1
                Write(a[0], Gfx.TryGet(Read(a[1])) != null ? 0 : -1); return pc + 1;
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
            case "gfx-cmd-register":    // 0x1a2 (handle) — operand-descriptor hash insert; separate from
                                        // op 0x215's retained gfx-object/source-slot lookup
                Gfx.Register(Read(a[0])); return pc + 1;
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
            case "reset-anim-clock": // 0x243: reset the separate global animation-service clock
                Gfx.ResetAnimClock(); return pc + 1;
            case "queue-surface-alpha-transition": // 0x223: target surface crossfade over two object ranges
                Gfx.QueueSurfaceAlphaTransition(Read(a[0]), (int)Read(a[1]), Read(a[2]), (int)Read(a[3]),
                    Read(a[4]), (int)Read(a[5]), Read(a[6]), Read(a[7])); return pc + 1;
            case "present-frame": // 0x20c: read/message-skip path snaps a queued transition to its endpoint
                _host.PresentFrame(Gfx); return pc + 1;
            case "mark-frame-yield": // 0x21c: normal foreground-transition scheduler/resume boundary
                _host.WaitForForegroundTransition(Gfx); return pc + 1;
            case "clear-gfx-command-queue": // 0x224: retained compositor does not use this native queue
                return pc + 1;
            default:
                // Stub is per-instruction frequency (the VM handles ~30 ops; the rest hit here, e.g.
                // 0x258/0x259 stmt markers appear en masse), so gate it with Step — else --trace floods.
                if (_sink.TracingSteps) _sink.Emit(TraceEvent.Stub(op, pc)); return pc + 1;
        }
    }

}

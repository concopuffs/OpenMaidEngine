using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
namespace Age.Engine.Vm;

public sealed class VirtualMachine
{
    private const long NoJump = 0xFFFFFFFF;
    private const int HALT = int.MinValue;
    private const int FRAME_RETURN = int.MinValue + 1;
    private const int SceneEntryCoroutineGate = 0xaba5c;
    private const int T_IMM = 0, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_GPTR = 6,
                      T_LINT = 9, T_LFLOAT = 10, T_LSTR = 11, T_LPTR = 12;

    private readonly Script _s;
    private readonly OpcodeTable _t;
    private readonly IHost _host;
    private readonly VmOptions _o;
    private readonly IScriptProvider? _provider;
    private static readonly bool _diagSetTexture = System.Environment.GetEnvironmentVariable("AGE_DIAG_SETTEX") == "1";
    private ExecFrame _cur = null!;
    private int _depth;
    private readonly ITraceSink _sink;
    public long CallScriptDispatches { get; private set; }

    public Dictionary<int, long> Globals { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    public GfxState Gfx { get; } = new();
    public List<(int Offset, string Text, string Script)> Emitted { get; } = new();
    public string? HaltReason { get; private set; }
    public long Steps { get; private set; }

    public VirtualMachine(Script s, OpcodeTable t, IHost host, VmOptions? o = null,
                          IScriptProvider? provider = null, ITraceSink? sink = null)
    { _s = s; _t = t; _host = host; _o = o ?? new VmOptions(); _provider = provider;
      _sink = sink ?? NullTraceSink.Instance; }

    private static long Gi(Dictionary<int, long> d, int k) => d.TryGetValue(k, out var v) ? v : 0;
    private static string Gs(Dictionary<int, string> d, int k) => d.TryGetValue(k, out var v) ? v : "";
    private static long PyDiv(long a, long b) { if (b == 0) return 0; long q = a / b, r = a % b; if (r != 0 && (r < 0) != (b < 0)) q--; return q; }
    private static long PyMod(long a, long b) { if (b == 0) return 0; long r = a % b; if (r != 0 && (r < 0) != (b < 0)) r += b; return r; }

    private static bool IsStr(Operand o) => o.Type == T_STR || o.Type == T_GSTR || o.Type == T_LSTR;
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
        T_GINT or T_GFLOAT => Gi(Globals, (int)op.Value),
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
        T_LSTR => Gs(_cur.Locals.S, (int)op.Value),
        _ => "",
    };

    private void WriteStr(Operand op, string val)
    {
        switch (op.Type)
        {
            case T_GSTR: GlobalStrings[(int)op.Value] = val; break;
            case T_LSTR: _cur.Locals.S[(int)op.Value] = val; break;
        }
    }

    private long BaseAddr(Operand op) => op.Type switch
    {
        T_IMM or T_GINT or T_GFLOAT or T_GSTR or T_GPTR => op.Value,
        T_LINT => Gi(_cur.Locals.I, (int)op.Value),
        T_LPTR => Gi(_cur.Locals.P, (int)op.Value),
        _ => op.Value,
    };

    private void LookupStore(Operand dst, long addr)
    {
        switch (dst.Type)
        {
            case T_LPTR: _cur.Locals.P[(int)dst.Value] = addr; break;
            case T_GPTR: Globals[(int)dst.Value] = addr; break;
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
        _cur = prev; _depth--;
        return outcome;
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
            case "wait-for-input":
                // Faithful headless: no player => halt here rather than plow past every prompt (see VmOptions).
                if (_o.HaltAtWaitForInput) { HaltReason ??= "wait-for-input"; return HALT; }
                _host.WaitForInput(); return pc + 1;
            case "sleep":   // 0xc8 (duration) — pause the host duration ms; headless hosts no-op (parity). Frame pacing.
                _host.Sleep(Read(a[0])); return pc + 1;
            case "end-text-line": case "set-font":
            case "comment": case "display-furigana": case "dev_ukn":
                return pc + 1;
            case "create-texture":   // 0x1f8 (slot)(w)(h) — allocate a blank surface at the slot
                Gfx.ClearSurface((int)Read(a[0]));
                _host.CreateTexture((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-texture":   // 0x1f9 (resId)(slot)(colorkey) — load a file into the slot's surface
                if (_diagSetTexture)   // AGE_DIAG_SETTEX: log the SLOT operand source (literal vs which global) — grey-BG slot dig
                    System.Console.Error.WriteLine($"[settex] resId=0x{Read(a[0]):x} slot={(int)Read(a[1])} " +
                        $"slotOp=(type={a[1].Type} val=0x{a[1].Value:x}){(a[1].Type == 3 ? $" G[0x{a[1].Value:x}]" : "")}");
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
            case "play-voice": _host.PlayVoice(Read(a[0])); return pc + 1;
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
            case "u004223C0":   // 0x239 spritesheet cell: (handle)(p3)(p4)(gridW)(gridH)(cell) — static cell
                Gfx.SetSrcRect(Read(a[0]), Read(a[3]), Read(a[4]), Read(a[5]), 0); return pc + 1;
            case "u00421EA0":   // 0x231 anim spritesheet: (handle)(period)(gridW)(gridH) — ping-pong the cell
                Gfx.SetSrcRect(Read(a[0]), Read(a[2]), Read(a[3]), 0, Read(a[1])); return pc + 1;
            case "u00421EF0":   // 0x232 anim color/glow: (handle)(period)(alpha)(color) — ping-pong the color
                Gfx.SetColorAnim(Read(a[0]), Read(a[1]), GfxState.PackColor(Read(a[2]), Read(a[3]))); return pc + 1;
            case "u00421940":   // 0x228 query-position: (succ)(handle)(outX)(outY)(outZ) <- current V24
            {
                var obj = Gfx.TryGet(Read(a[1]));
                var v = obj?.V24 ?? default;
                Write(a[2], v.X); Write(a[3], v.Y); Write(a[4], v.Z);
                Write(a[0], obj != null ? 0 : 1); return pc + 1;
            }
            case "u00422930":   // 0x23f query-object: (out)(handle) <- 0 if the object exists, else -1
                Write(a[0], Gfx.TryGet(Read(a[1])) != null ? 0 : -1); return pc + 1;
            case "set-gfx-geom3-c":     // 0x1ff (handle)(a)(b)(c) -> V16c
                Gfx.GetOrCreate(Read(a[0])).V16c = (Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
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
                Gfx.ClearSurface((int)Read(a[0])); return pc + 1;
            case "gfx-blit-color":      // 0x202 (handle)(x)(y)(alpha)(color) — static alpha/tint (anim interp deferred)
                Gfx.SetObjectColor(Read(a[0]), GfxState.PackColor(Read(a[3]), Read(a[4]))); return pc + 1;
            case "gfx-draw-color":      // 0x203 (handle)(v)(alpha)(color) — static alpha/tint
                Gfx.SetObjectColor(Read(a[0]), GfxState.PackColor(Read(a[2]), Read(a[3]))); return pc + 1;
            // ---- sprite transform / animation cluster (docs/engine-re.md "0x21c-0x243 ... ANIMATION") ----
            case "set-anim-transform-abs":   // 0x220 (handle)(p1)(p2)(x)(y)(z) — set transform directly
                Gfx.SetAnimTransform(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5])), normalized: false); return pc + 1;
            case "set-anim-transform-norm":  // 0x21e — same, operands are ~percent (/_DAT_00571c28)
                Gfx.SetAnimTransform(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5])), normalized: true); return pc + 1;
            case "anim-start":   // 0x234 (handle)(duration)(x)(y)(z) — animate toward target over the global clock
                Gfx.StartAnim(Read(a[0]), Read(a[1]), (Read(a[2]), Read(a[3]), Read(a[4]))); return pc + 1;
            case "set-anim-clock":   // 0x238 (duration) — global, non-blocking (host advances it per-frame)
                Gfx.SetAnimClock(Read(a[0])); return pc + 1;
            default:
                // Stub is per-instruction frequency (the VM handles ~30 ops; the rest hit here, e.g.
                // 0x258/0x259 stmt markers appear en masse), so gate it with Step — else --trace floods.
                if (_sink.TracingSteps) _sink.Emit(TraceEvent.Stub(op, pc)); return pc + 1;
        }
    }

}

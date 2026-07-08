using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
namespace Age.Engine.Vm;

public sealed class VirtualMachine
{
    private const long NoJump = 0xFFFFFFFF;
    private const int HALT = int.MinValue;
    private const int FRAME_RETURN = int.MinValue + 1;
    private const int T_IMM = 0, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_GPTR = 6,
                      T_LINT = 9, T_LFLOAT = 10, T_LSTR = 11, T_LPTR = 12;

    private readonly Script _s;
    private readonly OpcodeTable _t;
    private readonly IHost _host;
    private readonly VmOptions _o;
    private readonly IScriptProvider? _provider;
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
            case "wait-for-input": _host.WaitForInput(); return pc + 1;
            case "end-text-line": case "set-font":
            case "comment": case "display-furigana": case "dev_ukn":
                return pc + 1;
            case "create-texture":   // 0x1f8 (slot)(w)(h) — allocate a blank surface at the slot
                Gfx.ClearSurface((int)Read(a[0]));
                _host.CreateTexture((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-texture":   // 0x1f9 (resId)(slot)(colorkey) — load a file into the slot's surface
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
            case "set-gfx-geom3-c":     // 0x1ff (handle)(a)(b)(c) -> V16c
                Gfx.GetOrCreate(Read(a[0])).V16c = (Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            case "set-gfx-field64":     // 0x212 (idx)(val)
                Gfx.GetOrCreate(Read(a[0])).Field64 = Read(a[1]); return pc + 1;
            case "set-gfx-xy":          // 0x213 (idx)(x)(y)
            {
                var o = Gfx.GetOrCreate(Read(a[0])); o.Field68 = Read(a[1]); o.Field6c = Read(a[2]); return pc + 1;
            }
            case "gfx-cmd-register":    // 0x1a2 (handle) — insert into the op-0x215 query registry (native
                                        // FUN_0042d360 -> FUN_0042cf70 hash insert; the ONLY populator of that map)
                Gfx.Register(Read(a[0])); return pc + 1;
            case "gfx-elem-erase":      // 0x1f7 (handle)(count) — erase registry range (teardown, NOT create)
                Gfx.EraseRange(Read(a[0]), Read(a[1])); return pc + 1;
            case "gfx-elem-release":    // 0x1fa (handle)
                Gfx.Release(Read(a[0])); return pc + 1;
            case "gfx-blit-color":      // 0x202 (handle)(x)(y)(alpha)(color) — blend deferred
                Gfx.GetOrCreate(Read(a[0])).Color = GfxState.PackColor(Read(a[3]), Read(a[4]));
                WarnAlphaDeferredOnce(); return pc + 1;
            case "gfx-draw-color":      // 0x203 (handle)(v)(alpha)(color) — blend deferred
                Gfx.GetOrCreate(Read(a[0])).Color = GfxState.PackColor(Read(a[2]), Read(a[3]));
                WarnAlphaDeferredOnce(); return pc + 1;
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

    // Colored-draw ops (0x202/0x203) store the packed color on the object now; the actual alpha/additive
    // blend in the compositor is deferred. Surface it once (not silently) via the trace sink — observe-only,
    // so parity holds. See docs/superpowers/specs/2026-07-07-gfx-command-buffer-design.md (Deferrals).
    private bool _warnedAlpha;
    private void WarnAlphaDeferredOnce()
    {
        if (_warnedAlpha) return; _warnedAlpha = true;
        _sink.Emit(TraceEvent.Stub(0x202, -1));
    }
}

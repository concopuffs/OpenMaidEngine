using Age.Engine.Hosting;
using Age.Engine.Model;
namespace Age.Engine.Vm;

public sealed class VirtualMachine
{
    private const long NoJump = 0xFFFFFFFF;
    private const int HALT = int.MinValue;
    private const int T_IMM = 0, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_GPTR = 6,
                      T_LINT = 9, T_LFLOAT = 10, T_LSTR = 11, T_LPTR = 12;

    private readonly Script _s;
    private readonly OpcodeTable _t;
    private readonly IHost _host;
    private readonly VmOptions _o;
    private readonly Frame _fr = new();
    private readonly List<int> _callstack = new();
    private readonly Dictionary<int, int> _emitSeen = new();

    public Dictionary<int, long> Globals { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    public List<(int Offset, string Text)> Emitted { get; } = new();
    public string? HaltReason { get; private set; }
    public long Steps { get; private set; }

    public VirtualMachine(Script s, OpcodeTable t, IHost host, VmOptions? o = null)
    { _s = s; _t = t; _host = host; _o = o ?? new VmOptions(); }

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
        T_LINT => Gi(_fr.I, (int)op.Value),
        T_LFLOAT => Gi(_fr.F, (int)op.Value),
        T_LPTR => Gi(Globals, (int)Gi(_fr.P, (int)op.Value)),
        _ => op.Value,
    };

    private void Write(Operand op, long val)
    {
        switch (op.Type)
        {
            case T_GINT: case T_GFLOAT: Globals[(int)op.Value] = val; break;
            case T_GPTR: Globals[(int)Gi(Globals, (int)op.Value)] = val; break;
            case T_LINT: _fr.I[(int)op.Value] = val; break;
            case T_LFLOAT: _fr.F[(int)op.Value] = val; break;
            case T_LPTR: Globals[(int)Gi(_fr.P, (int)op.Value)] = val; break;
        }
    }

    private string ReadStr(Operand op) => op.Type switch
    {
        T_STR => _s.GetString((int)op.Value),
        T_GSTR => Gs(GlobalStrings, (int)op.Value),
        T_LSTR => Gs(_fr.S, (int)op.Value),
        _ => "",
    };

    private void WriteStr(Operand op, string val)
    {
        switch (op.Type)
        {
            case T_GSTR: GlobalStrings[(int)op.Value] = val; break;
            case T_LSTR: _fr.S[(int)op.Value] = val; break;
        }
    }

    private long BaseAddr(Operand op) => op.Type switch
    {
        T_IMM or T_GINT or T_GFLOAT or T_GSTR or T_GPTR => op.Value,
        T_LINT => Gi(_fr.I, (int)op.Value),
        T_LPTR => Gi(_fr.P, (int)op.Value),
        _ => op.Value,
    };

    private void LookupStore(Operand dst, long addr)
    {
        switch (dst.Type)
        {
            case T_LPTR: _fr.P[(int)dst.Value] = addr; break;
            case T_GPTR: Globals[(int)dst.Value] = addr; break;
            default: Write(dst, Gi(Globals, (int)addr)); break;
        }
    }

    public void Run(int entryOffset = 0)
    {
        int pc = _s.IndexByOffset.TryGetValue(entryOffset, out var idx) ? idx : 0;
        while (pc >= 0 && pc < _s.Instructions.Count)
        {
            if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; return; }
            Steps++;
            int next = Step(_s.Instructions[pc], pc);
            if (next == HALT) return;
            pc = next;
        }
        HaltReason ??= "pc-out-of-range";
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
            case "jmp": return _s.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);
            case "call": _callstack.Add(pc + 1); return _s.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);
            case "ret":
                if (_callstack.Count > 0) { int r = _callstack[^1]; _callstack.RemoveAt(_callstack.Count - 1); return r; }
                HaltReason = "ret-underflow"; return HALT;
            case "jcc":
            {
                long tgt = Read(a[0]) != 0 ? a[1].Value : a[2].Value;
                return tgt == NoJump ? pc + 1 : _s.IndexByOffset.GetValueOrDefault((int)tgt, pc + 1);
            }
            case "exit":
            case "exit-script": HaltReason = "exit"; return HALT;
            case "call-script": _host.CallScript(a.Count > 0 ? Read(a[0]) : 0); return pc + 1;
            case "show-text":
                foreach (var o in a)
                {
                    if (o.Type != T_STR) continue;
                    int off = (int)o.Value;
                    _emitSeen.TryGetValue(off, out var c); c++; _emitSeen[off] = c;
                    if (c > _o.EmitCap) { HaltReason = $"LOOP:line@0x{off:x}×{c}"; return HALT; }
                    string text = _s.GetString(off);
                    Emitted.Add((off, text));
                    _host.ShowText(off, text);
                }
                return pc + 1;
            case "wait-for-input": _host.WaitForInput(); return pc + 1;
            case "end-text-line": case "set-font":
            case "comment": case "display-furigana": case "dev_ukn":
                return pc + 1;
            case "create-texture":
                _host.CreateTexture((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-texture":
                _host.SetTexture(Read(a[0]), (int)Read(a[1])); return pc + 1;
            case "draw-texture":   // (handle, slot, srcX, srcY, w, h, dstX, dstY)
                _host.DrawTexture((int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
                                  (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])); return pc + 1;
            case "get-texture-size":   // 0x208 (slot) (out_w) (out_h)
            {
                var (gw, gh) = _host.GetTextureSize((int)Read(a[0]));
                Write(a[1], gw); Write(a[2], gh);
                return pc + 1;
            }
            case "play-bgm":   _host.PlayBgm(Read(a[0])); return pc + 1;
            case "play-voice": _host.PlayVoice(Read(a[0])); return pc + 1;
            default:
                _host.OnStub(op); return pc + 1;
        }
    }
}

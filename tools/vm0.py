#!/usr/bin/env python3
"""vm0 — headless AGE bytecode interpreter (Phase A0 prototype).

Executes one script's bytecode to validate the *execution model* before any C#/Godot work.
Reuses tools/sys4load.py for all parsing/decoding. Effectful ops and call-script are STUBBED;
show-text is captured. Named-op semantics come from the generated canonical registry; the classified markers are
treated as no-ops (this run TESTS that assumption).

Purpose (see docs/phase-a-slice-plan.md):
  * `--test`  run the RECOVER unit test (pointer / 2D-array / loop / control-flow correctness).
  * <file>    run a script; print captured show-text + an opcode-coverage/stub report.

Memory model:
  * one flat GLOBAL bank G (dict addr->int); globals are raw offsets into one space.
  * per-call local frame with sparse typed banks (int/float/string/ptr).
  * a `-ptr` variable retains an address plus its local/global storage domain. lookup-array/2d
    with a ptr dst stores that reference (take-reference); reads/writes dereference the matching
    bank. RECOVER exercises global references; HISTORY's copied coordinate arrays exercise local ones.
  * jcc(cond, tA, tB): cond truthy -> goto tA else tB; 0xFFFFFFFF = fall through (from RECOVER+SCJUMP).
  * call/ret (0x8F/0x05) are intra-script subroutine calls (shared frame); call-script (0x03) is
    the inter-script one and is stubbed.
"""
from __future__ import annotations
import sys
import collections
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths
import sys4load
from age_opcodes import OPCODES

NOJUMP = 0xFFFFFFFF

# operand type tags
T_IMM, T_STR = 0x0, 0x2
T_GINT, T_GFLOAT, T_GSTR, T_GPTR = 0x3, 0x4, 0x5, 0x6
T_LINT, T_LFLOAT, T_LSTR, T_LPTR = 0x9, 0xA, 0xB, 0xC

# opcodes treated as no-ops in v1 (classified markers; see age_opcode_semantics.py)
# 0x71 = label-definition pseudo-op (count == T1 table size) — structural, no runtime effect.
MARKERS = {0x71, 0x1f4, 0x1f5, 0x1d5, 0x1bc, 0x1bf, 0x21b, 0x1d2, 0x258}

# loop-guard: halt a run once any single show-text line has been emitted this many times.
# State-gated scenes run with zero initial state can spin a loop that re-emits the same block
# forever; capping re-emission terminates them cleanly and flags them LOOPED (vs. blind step limit).
EMIT_CAP = 2


class Frame:
    def __init__(self):
        self.i = collections.defaultdict(int)     # local-int
        self.f = collections.defaultdict(int)     # local-float (stored raw)
        self.s = collections.defaultdict(str)     # local-string
        self.p = collections.defaultdict(lambda: ("g", 0))  # local-ptr: (bank, address)


class VM:
    def __init__(self, scr: sys4load.Sys4Script, verbose=False, emit_cap=EMIT_CAP, record_trace=False):
        self.scr = scr
        self.verbose = verbose
        self.record_trace = record_trace
        self.trace = []                           # executed code offsets (only if record_trace)
        self.code = scr.instructions
        self.by_off = {ins.offset: idx for idx, ins in enumerate(self.code)}
        self.G = collections.defaultdict(int)     # global-int bank (flat address space)
        self.Gs = collections.defaultdict(str)    # global-string bank
        self.fr = Frame()
        self.callstack = []                       # return indices for call/ret
        self.text = []                            # captured show-text as (str_offset, text)
        self.settex = []                          # set-texture calls: (code_offset, resId, slot)
        self.emit_seen = collections.Counter()    # per-offset emit count (loop-guard)
        self.emit_cap = emit_cap
        self.halt_reason = None                   # 'exit' | 'LOOP:...' | 'STEP-LIMIT' | 'ret-underflow'
        self.log = collections.Counter()          # stub/unknown opcode hits
        self.exec_count = collections.Counter()   # opcode coverage
        self.steps = 0

    # ---- operand resolution --------------------------------------------------
    def _string(self, off):
        e = self.scr.strings.get(off)
        if e is not None:
            return e[0]
        txt, _ = sys4load._decode_string(self.scr.dwords, off)
        return txt if txt is not None else ""

    def read(self, op):
        t, v = op
        if t == T_IMM:    return v
        if t == T_STR:    return self._string(v)
        if t == T_GINT:   return self.G[v]
        if t == T_GFLOAT: return self.G[v]
        if t == T_GSTR:   return self.Gs[v]
        if t == T_GPTR:   return self.G[self.G[v]]
        if t == T_LINT:   return self.fr.i[v]
        if t == T_LFLOAT: return self.fr.f[v]
        if t == T_LSTR:   return self.fr.s[v]
        if t == T_LPTR:   return self.read_cell(self.fr.p[v])   # deref ptr
        self.log[f"read?t{t:#x}"] += 1
        return v

    def write(self, op, val):
        t, v = op
        if t == T_GINT or t == T_GFLOAT:  self.G[v] = val
        elif t == T_GSTR:                 self.Gs[v] = val
        elif t == T_GPTR:                 self.G[self.G[v]] = val
        elif t == T_LINT:                 self.fr.i[v] = val
        elif t == T_LFLOAT:               self.fr.f[v] = val
        elif t == T_LSTR:                 self.fr.s[v] = val
        elif t == T_LPTR:                 self.write_cell(self.fr.p[v], val)
        else:                             self.log[f"write?t{t:#x}"] += 1

    def read_cell(self, address):
        bank, addr = address
        if bank == "i": return self.fr.i[addr]
        if bank == "f": return self.fr.f[addr]
        return self.G[addr]

    def write_cell(self, address, value):
        bank, addr = address
        if bank == "i": self.fr.i[addr] = value
        elif bank == "f": self.fr.f[addr] = value
        else: self.G[addr] = value

    def base_addr(self, op):
        """The base ADDRESS an operand names, for array lookups."""
        t, v = op
        if t in (T_IMM, T_GINT, T_GFLOAT, T_GSTR, T_GPTR): return ("g", v)
        if t == T_LINT: return ("i", v)
        if t == T_LFLOAT: return ("f", v)
        if t == T_LSTR: return ("s", v)
        if t == T_LPTR:  return self.fr.p[v]
        return ("g", v)

    def lookup_store(self, dst, addr):
        """lookup result: ptr dst gets the reference (address); non-ptr gets the element value."""
        t, v = dst
        if t == T_LPTR:   self.fr.p[v] = addr
        elif t == T_GPTR: self.G[v] = addr[1]
        else:             self.write(dst, self.read_cell(addr))

    # ---- execution -----------------------------------------------------------
    def run(self, entry_off=0, max_steps=2_000_000):
        pc = self.by_off.get(entry_off, 0)
        while 0 <= pc < len(self.code):
            if self.steps >= max_steps:
                self.log["STEP-LIMIT"] += 1
                self.halt_reason = self.halt_reason or "STEP-LIMIT"
                break
            self.steps += 1
            ins = self.code[pc]
            op = ins.opcode
            self.exec_count[op] += 1
            if self.record_trace:
                self.trace.append(ins.offset)
            nxt = self.step(ins, pc)
            if nxt is None:                 # halt
                break
            pc = nxt
        else:
            self.halt_reason = self.halt_reason or "pc-out-of-range"
        return self

    def step(self, ins, pc):
        op, a = ins.opcode, ins.args
        lbl = OPCODES.get(op, ("?", 0))[0]

        # arithmetic / bitwise: dst = a1 <op> a2
        alu = {"add": lambda x, y: x + y, "sub": lambda x, y: x - y,
               "mul": lambda x, y: x * y, "div": lambda x, y: (x // y if y else 0),
               "mod": lambda x, y: (x % y if y else 0), "and": lambda x, y: x & y,
               "or": lambda x, y: x | y, "sar": lambda x, y: x >> (y & 31),
               "shl": lambda x, y: x << (y & 31)}
        cmp = {"eq": lambda x, y: int(x == y), "ne": lambda x, y: int(x != y),
               "lt": lambda x, y: int(x < y), "lte": lambda x, y: int(x <= y),
               "gr": lambda x, y: int(x > y), "gre": lambda x, y: int(x >= y)}

        if lbl in alu:
            self.write(a[0], alu[lbl](self.read(a[1]), self.read(a[2])));  return pc + 1
        if lbl in cmp:
            self.write(a[0], cmp[lbl](self.read(a[1]), self.read(a[2])));  return pc + 1
        if lbl == "mov":
            self.write(a[0], self.read(a[1]));                             return pc + 1
        if lbl == "set-string":
            self.write(a[0], self.read(a[1]));                             return pc + 1
        if lbl == "lookup-array":                       # dst = base[idx]
            bank, base = self.base_addr(a[1])
            addr = (bank, base + self.read(a[2]))
            self.lookup_store(a[0], addr);                                 return pc + 1
        if lbl == "lookup-array-2d":                    # dst = base[i*stride + col]
            bank, base = self.base_addr(a[1])
            addr = (bank, base + self.read(a[2]) * self.read(a[3]) + self.read(a[4]))
            self.lookup_store(a[0], addr);                                 return pc + 1
        if lbl == "bit-set":
            bit = self.read(a[1])
            if not 0 <= bit < 32:
                self.halt_reason = f"bit-index-out-of-range:{bit}"
                return None
            self.write(a[0], self.read(a[0]) | (1 << bit));                return pc + 1
        if lbl == "bit-reset":
            bit = self.read(a[1])
            if not 0 <= bit < 32:
                self.halt_reason = f"bit-index-out-of-range:{bit}"
                return None
            self.write(a[0], self.read(a[0]) & ~(1 << bit));               return pc + 1
        if lbl == "check-bit":                          # p1 = (p2 >> p3) & 1
            self.write(a[0], (self.read(a[1]) >> (self.read(a[2]) & 31)) & 1); return pc + 1
        if lbl == "copy-to-global":                     # best-effort: p1 = p2 (single cell)
            self.write(a[0], self.read(a[1]));                             return pc + 1

        # control flow
        if lbl == "jmp":
            return self.by_off.get(a[0][1], pc + 1)
        if lbl == "call":                               # intra-script subroutine
            self.callstack.append(pc + 1)
            return self.by_off.get(a[0][1], pc + 1)
        if lbl == "ret":
            if self.callstack:
                return self.callstack.pop()
            self.halt_reason = "ret-underflow"
            return None
        if lbl == "jcc":                                # (cond, tA, tB); 0xFFFFFFFF=fallthrough
            tgt = a[1][1] if self.read(a[0]) else a[2][1]
            return pc + 1 if tgt == NOJUMP else self.by_off.get(tgt, pc + 1)
        if lbl in ("exit", "exit-script"):
            self.halt_reason = "exit"
            return None
        if lbl == "call-script":                        # STUB (inter-script)
            self.log[f"call-script({self.read(a[0]) if a else '?'})"] += 1;  return pc + 1

        # ADV text: capture the string operand as (offset, text); loop-guard on re-emission
        if lbl == "show-text":
            for t, v in a:
                if t == T_STR:
                    self.emit_seen[v] += 1
                    if self.emit_seen[v] > self.emit_cap:
                        self.halt_reason = f"LOOP:line@{v:#x}×{self.emit_seen[v]}"
                        return None
                    self.text.append((v, self._string(v)))
            return pc + 1
        if lbl in ("end-text-line", "wait-for-input", "set-font", "comment",
                   "display-furigana", "dev_ukn"):
            return pc + 1

        if lbl == "set-texture":                        # 0x1f9 (resId, slot, flag) — trace the load
            resid = self.read(a[0]) if a else None
            slot = self.read(a[1]) if len(a) > 1 else None
            self.settex.append((ins.offset, resid, slot, len(self.trace)))   # +trace index
            return pc + 1

        if op in MARKERS:                               # classified no-op markers
            return pc + 1

        # everything else (effectful draw/audio/ui/input, unnamed) -> stub + continue
        self.log[f"stub:{lbl if not lbl.startswith('u00') else hex(op)}"] += 1
        return pc + 1


def run_test():
    """RECOVER unit test — pointer/2D-array/loop/control-flow correctness."""
    scr = sys4load.load(paths.DATA1 / "RECOVER.BIN")
    vm = VM(scr)
    unit = 0
    vm.G[0x152616] = unit
    A, B = 0x4e11b, 0x4e085          # block-1 tables (stride 14 / 3)
    C, E, F, FL = 0x52383, 0x52f3b, 0x5295f, 0xaacb4   # block-2 tables (stride 30) + flags
    # block 1 source: A[unit*14 + 11..13]
    for k in range(3):
        vm.G[A + unit * 14 + (11 + k)] = 100 + k
    # block 2: slot 5 active, slot 6 zero-C (skip), slot 7 flag-off (skip)
    vm.G[C + unit * 30 + 5] = 7;  vm.G[FL + 5] = 1;  vm.G[E + unit * 30 + 5] = 42
    vm.G[C + unit * 30 + 6] = 0;  vm.G[FL + 6] = 1;  vm.G[E + unit * 30 + 6] = 99
    vm.G[C + unit * 30 + 7] = 3;  vm.G[FL + 7] = 0;  vm.G[E + unit * 30 + 7] = 88
    vm.run()

    checks = [
        ("block1 B[0]=A[11]", vm.G[B + unit * 3 + 0], 100),
        ("block1 B[1]=A[12]", vm.G[B + unit * 3 + 1], 101),
        ("block1 B[2]=A[13]", vm.G[B + unit * 3 + 2], 102),
        ("block2 s5 C:=E",    vm.G[C + unit * 30 + 5], 42),
        ("block2 s5 F:=-1",   vm.G[F + unit * 30 + 5], -1),
        ("block2 s6 C skip",  vm.G[C + unit * 30 + 6], 0),   # C stayed 0 (guard)
        ("block2 s7 C skip",  vm.G[C + unit * 30 + 7], 3),   # flag off -> untouched
    ]
    ok = True
    for name, got, want in checks:
        status = "OK " if got == want else "FAIL"
        if got != want:
            ok = False
        print(f"  [{status}] {name}: got {got}, want {want}")
    print(f"  steps={vm.steps}  call-script stubs={sum(v for k,v in vm.log.items() if k.startswith('call-script'))}")
    print("RECOVER unit test:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


def run_file(path):
    scr = sys4load.load(path)
    vm = VM(scr).run()
    print(f"== {Path(path).name}: {vm.steps} steps, {len(vm.text)} show-text lines captured "
          f"(halt: {vm.halt_reason}) ==")
    for i, (off, t) in enumerate(vm.text[:20]):
        print(f"  [{i}] {t}")
    if len(vm.text) > 20:
        print(f"  ... (+{len(vm.text)-20} more)")
    stubs = [(k, v) for k, v in vm.log.most_common() if k.startswith("stub:")]
    if stubs:
        print("  top stubbed effectful/unknown ops:", ", ".join(f"{k[5:]}×{v}" for k, v in stubs[:10]))
    ncall = sum(v for k, v in vm.log.items() if k.startswith("call-script"))
    print(f"  call-script stubs: {ncall}   distinct opcodes executed: {len(vm.exec_count)}")
    return 0


# ---- dialogue oracle ---------------------------------------------------------
import json
import re

SCENE_RE = re.compile(r"^S[CP]\d{4}\.BIN$")


def load_oracle(path=None):
    """file (UPPER) -> ordered list of (str_offset, text): the static show-text lines
    extract_phase2 dumped. This is the independent oracle the VM is validated against."""
    path = path or (paths.BUILD / "text" / "dialogue.jsonl")
    by = collections.defaultdict(list)
    with open(path, encoding="utf-8") as f:
        for line in f:
            d = json.loads(line)
            by[d["file"].upper()].append((int(d["off"], 16), d["text"]))
    return by


def subsequence_status(emitted_offs, static_offs):
    """Classify the emitted show-text offset stream against the static ordered set.

    Emitted may repeat offsets (loop re-emission); we validate the distinct stream in
    first-seen order as an in-order subsequence of the static lines. Returns (status,
    detail): OK | STRAY (offset never in static) | ORDER (in static but out of order)."""
    static_list = list(static_offs)
    static_set = set(static_list)
    seen, distinct = set(), []
    for o in emitted_offs:
        if o not in seen:
            seen.add(o)
            distinct.append(o)
    strays = [o for o in distinct if o not in static_set]
    if strays:
        return "STRAY", strays
    j = 0
    for o in distinct:
        while j < len(static_list) and static_list[j] != o:
            j += 1
        if j >= len(static_list):
            return "ORDER", [o]
        j += 1
    return "OK", []


def verdict(status, halt, n_emit, n_static):
    """Fold subsequence status + halt reason into one scene verdict."""
    if n_static == 0:
        return "NO-DIALOGUE"          # not an ADV scene (no static show-text) — skip in scoring
    if n_emit == 0:
        return "EMPTY"                # scene has dialogue but VM emitted none — investigate
    if status != "OK":
        return status                 # STRAY / ORDER — a real divergence
    return "CLEAN" if halt == "exit" else f"OK/{halt}"   # OK subsequence; did it exit cleanly?


def run_scene(name, path, oracle):
    """Run one scene, return a result dict comparing emitted vs static show-text."""
    scr = sys4load.load(path)
    vm = VM(scr).run()
    emitted = [off for off, _ in vm.text]
    static = oracle.get(name.upper(), [])
    static_offs = [o for o, _ in static]
    status, detail = subsequence_status(emitted, static_offs)
    n_emit = len({o for o in emitted})
    return {"name": name, "vm": vm, "emitted": emitted, "static": static,
            "n_emit_distinct": n_emit, "n_static": len(static_offs),
            "status": status, "detail": detail,
            "verdict": verdict(status, vm.halt_reason, n_emit, len(static_offs))}


def run_one_scene(name):
    """Detailed single-scene oracle diff (for investigating one script)."""
    oracle = load_oracle()
    scripts = paths.scripts()
    name = name.upper()
    if not name.endswith(".BIN"):
        name += ".BIN"
    if name not in scripts:
        print(f"no such script: {name}")
        return 1
    r = run_scene(name, scripts[name], oracle)
    vm = r["vm"]
    print(f"== {name}: verdict {r['verdict']} ==")
    print(f"   emitted {len(r['emitted'])} lines ({r['n_emit_distinct']} distinct), "
          f"static {r['n_static']}, steps {vm.steps}, halt {vm.halt_reason}")
    if r["status"] == "STRAY":
        stat_set = {o for o, _ in r["static"]}
        print(f"   STRAY offsets (emitted, not in static): "
              f"{', '.join(hex(o) for o in r['detail'])}")
        for off, txt in vm.text:
            if off in set(r["detail"]):
                print(f"     @ {off:#x}: {txt!r}")
    elif r["status"] == "ORDER":
        print(f"   first out-of-order offset: {hex(r['detail'][0])}")
    return 0


def run_sweep(limit=None):
    """Run every SC####/SP#### scene against the dialogue oracle; print a coverage table."""
    oracle = load_oracle()
    scripts = paths.scripts()
    names = sorted(n for n in scripts if SCENE_RE.match(n))
    if limit:
        names = names[:limit]
    buckets = collections.Counter()
    rows = []
    for name in names:
        r = run_scene(name, scripts[name], oracle)
        buckets[r["verdict"]] += 1
        rows.append(r)

    # non-clean scenes get listed for follow-up
    problem = [r for r in rows if r["verdict"] not in ("CLEAN", "NO-DIALOGUE")]
    if problem:
        print("non-clean scenes:")
        for r in sorted(problem, key=lambda r: r["verdict"]):
            d = ""
            if r["status"] in ("STRAY", "ORDER"):
                d = "  " + " ".join(hex(o) for o in r["detail"][:4])
            print(f"  {r['name']:<14} {r['verdict']:<16} "
                  f"emit {r['n_emit_distinct']:>4}/{r['n_static']:<4} "
                  f"steps {r['vm'].steps:>7} halt {r['vm'].halt_reason}{d}")
    print()
    scene_total = sum(v for k, v in buckets.items() if k != "NO-DIALOGUE")
    valid = sum(v for k, v in buckets.items()
                if k == "CLEAN" or k.startswith("OK/"))
    print(f"scenes with dialogue: {scene_total}  (skipped {buckets['NO-DIALOGUE']} with no static show-text)")
    print("verdict breakdown:", dict(sorted(buckets.items())))
    print(f"DIALOGUE-VALID (clean in-order subsequence, no garbage): "
          f"{valid}/{scene_total} = {valid/scene_total*100:.1f}%")
    return 0


def run_trace(out_path):
    """Dump per-scene emitted show-text offsets + halt + steps for the differential test."""
    oracle = load_oracle()
    scripts = paths.scripts()
    names = sorted(n for n in scripts if SCENE_RE.match(n))
    trace = {}
    for name in names:
        r = run_scene(name, scripts[name], oracle)
        vm = r["vm"]
        trace[name] = {"offsets": [off for off, _ in vm.text],
                       "halt": vm.halt_reason, "steps": vm.steps}
    Path(out_path).write_text(json.dumps(trace, ensure_ascii=False), encoding="utf-8")
    print(f"trace: {len(trace)} scenes -> {out_path}")
    return 0


def run_settex(name):
    """Execute a scene and dump its set-texture(resId) trace in execution order.

    This is the VM side of the asset-resolution scope-selector correlation
    (docs/asset-resolution-re.md): each entry is {i, off, resId, slot}, and aligning this
    ordered resId sequence with the game's Frida load order pins every load to a bytecode
    offset -> localizes where the active CG package/scope switches.
    """
    scripts = paths.scripts()
    key = name.upper() if name.upper().endswith(".BIN") else name.upper() + ".BIN"
    if key not in scripts:
        raise SystemExit(f"scene not found: {name}")
    vm = VM(sys4load.load(scripts[key]), record_trace=True)
    vm.run()
    out = paths.BUILD / f"settex-{key.removesuffix('.BIN')}.json"
    rows = [{"i": i, "off": f"0x{off:x}", "resId": rid, "slot": slot, "trace_i": ti}
            for i, (off, rid, slot, ti) in enumerate(vm.settex)]
    out.write_text(json.dumps({"scene": key, "halt": vm.halt_reason, "steps": vm.steps,
                               "count": len(rows), "settex": rows,
                               "trace": [f"0x{o:x}" for o in vm.trace]},
                              ensure_ascii=False), encoding="utf-8")
    print(f"{key}: {len(rows)} set-texture calls (halt={vm.halt_reason}, steps={vm.steps}) "
          f"-> {out.relative_to(paths.REPO)}")
    for r in rows[:20]:
        print(f"  #{r['i']:<3} {r['off']:>8}  resId={r['resId']} (0x{r['resId']:x})  slot={r['slot']}")
    return 0


def main(argv=None):
    argv = argv if argv is not None else sys.argv[1:]
    if not argv or argv[0] == "--test":
        return run_test()
    if argv[0] == "--sweep":
        return run_sweep(limit=int(argv[1]) if len(argv) > 1 else None)
    if argv[0] == "--scene":
        return run_one_scene(argv[1])
    if argv[0] == "--settex":
        return run_settex(argv[1])
    if argv[0] == "--trace":
        return run_trace(argv[1])
    return run_file(argv[0])


if __name__ == "__main__":
    sys.exit(main())

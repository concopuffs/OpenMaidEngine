#!/usr/bin/env python3
"""Static decoder for SCJUMP.BIN's progression decision logic. Guarded DFS over its acyclic CFG
extracts, per decision site, the (chapter_mode, path-condition guards) -> decision value rule.
Decision->scene resolution is native (u00428010) and out of scope. See
docs/superpowers/specs/2026-07-07-scjump-decision-decode-design.md.
  (no flag)   -> build/scjump-decisions.json + build/scjump-decisions.md
  --verify    synthesize a witness per decision, run SCJUMP in vm0, assert emitted 0x62ccf matches"""
from __future__ import annotations
import os, sys, json, argparse, collections
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load
from age_opcodes import is_label_argument

MOV, JMP, JCC = 0x55, 0x8c, 0xa0
CMP_OPS = {0x5a: "==", 0x5b: "!=", 0x5c: "<", 0x5d: "<=", 0x5e: ">", 0x5f: ">="}
LOGIC_OPS = {0x56: "and", 0x57: "or"}
GLOBAL_ATYPES = {3, 4, 5, 6, 8}
IMM = 0
FALLTHROUGH = 0xffffffff
CHAPTER_GLOBAL = 0x3234
DECISION_GLOBAL = 0x62ccf
VALID_GLOBAL = 0x0
END_TARGET = 0x2f78d


def load_scjump():
    return sys4load.load(paths.scripts()["SCJUMP.BIN"])


def _code_targets(ins):
    """Yield the code-target offsets of an instruction (excludes 0xffffffff fallthrough)."""
    for i, (t, v) in enumerate(ins.args):
        if is_label_argument(ins.opcode, i, v) and v != FALLTHROUGH:
            yield v


def forward_edges_only(scr) -> bool:
    by_off = {ins.offset: ins.offset for ins in scr.instructions}
    for ins in scr.instructions:
        for tgt in _code_targets(ins):
            if tgt in by_off and tgt <= ins.offset:
                return False        # a back-edge or self-loop
    return True


def chapter_dispatch(scr) -> dict:
    """Scan the top dispatch: eq(dest, 0x3234, N) immediately followed by jcc(dest, block, ...)."""
    out = {}
    code = scr.instructions
    for i, ins in enumerate(code):
        if ins.opcode == 0x5a and len(ins.args) >= 3 \
           and tuple(ins.args[1]) == (3, CHAPTER_GLOBAL) and ins.args[2][0] == IMM:
            n = ins.args[2][1]
            nxt = code[i + 1] if i + 1 < len(code) else None
            if nxt and nxt.opcode == JCC and nxt.args[1][1] != FALLTHROUGH:
                out[n] = nxt.args[1][1]
    return out


FLIP = {"==": "==", "!=": "!=", "<": ">", "<=": ">=", ">": "<", ">=": "<="}
NEG  = {"==": "!=", "!=": "==", "<": ">=", ">=": "<", "<=": ">", ">": "<="}


def negate(g):
    if "opaque" in g:      return {"opaque": "!(" + g["opaque"] + ")"}
    if "and" in g:         return {"or": [negate(x) for x in g["and"]]}
    if "or" in g:          return {"and": [negate(x) for x in g["or"]]}
    return {"global": g["global"], "op": NEG[g["op"]], "value": g["value"]}


# A local's symbolic content is one of:
#   ("gref", global_addr)   value of a global (from `mov local, global`)
#   ("imm", constant)       a constant
#   ("expr", guard_dict)    a boolean comparison result (from cmp / and / or)
#   ("opaque", desc)        anything we can't model (arithmetic, indirection)
def _rval(localmap, operand):
    """Resolve an operand to its symbolic value tuple."""
    t, v = operand
    if t in GLOBAL_ATYPES: return ("gref", v)
    if t == IMM:           return ("imm", v)
    if t == 9:             return localmap.get(v, ("opaque", f"local{v}"))
    return ("opaque", f"op{list(operand)}")


def _cmp_guard(op_sym, ra, rb):
    """Guard from a comparison of two resolved values (ra <op> rb). global-vs-imm only."""
    if ra[0] == "gref" and rb[0] == "imm": return {"global": ra[1], "op": op_sym, "value": rb[1]}
    if ra[0] == "imm" and rb[0] == "gref": return {"global": rb[1], "op": FLIP[op_sym], "value": ra[1]}
    return {"opaque": f"cmp {op_sym} {ra} {rb}"}


def _as_guard(rval):
    """A boolean guard from a resolved condition-local value."""
    return rval[1] if rval[0] == "expr" else {"opaque": f"cond {rval}"}


def chapter_of(guards):
    for g in guards:
        if g.get("global") == CHAPTER_GLOBAL and g.get("op") == "==":
            return g["value"]
    return None


def decode(scr):
    code = scr.instructions
    by_off = {ins.offset: i for i, ins in enumerate(code)}
    decisions = []
    # explicit-stack DFS: each item is (idx, localmap, guards). No back-edges -> terminates.
    stack = [(0, {}, [])]
    while stack:
        idx, localmap, guards = stack.pop()
        localmap = dict(localmap)
        while 0 <= idx < len(code):
            ins = code[idx]
            op, a = ins.opcode, ins.args
            if ins.offset == END_TARGET:
                break
            if op == MOV and a and tuple(a[0]) == (3, DECISION_GLOBAL) and a[1][0] == IMM:
                decisions.append({"site_offset": ins.offset, "chapter": chapter_of(guards),
                                  "decision": a[1][1], "guards": list(guards)})
                idx += 1
                continue
            if op == MOV and len(a) >= 2 and a[0][0] == 9:      # mov local, <value> — track it
                localmap[a[0][1]] = _rval(localmap, a[1])
                idx += 1
                continue
            if op in CMP_OPS and len(a) >= 3 and a[0][0] == 9:
                localmap[a[0][1]] = ("expr", _cmp_guard(CMP_OPS[op], _rval(localmap, a[1]), _rval(localmap, a[2])))
                idx += 1
                continue
            if op in LOGIC_OPS and len(a) >= 3 and a[0][0] == 9:
                kind = LOGIC_OPS[op]
                localmap[a[0][1]] = ("expr", {kind: [_as_guard(_rval(localmap, a[1])), _as_guard(_rval(localmap, a[2]))]})
                idx += 1
                continue
            if op == JCC and len(a) >= 3:
                expr = _as_guard(_rval(localmap, a[0]))
                a_tgt, b_tgt = a[1][1], a[2][1]
                true_idx = idx + 1 if a_tgt == FALLTHROUGH else by_off.get(a_tgt)
                false_idx = idx + 1 if b_tgt == FALLTHROUGH else by_off.get(b_tgt)
                # explore false branch later; continue true branch inline
                if false_idx is not None:
                    stack.append((false_idx, dict(localmap), guards + [negate(expr)]))
                if true_idx is None:
                    break
                idx, guards = true_idx, guards + [expr]
                continue
            if op == JMP and a:
                tgt = a[0][1]
                if tgt == END_TARGET or tgt not in by_off:
                    break
                idx = by_off[tgt]
                continue
            idx += 1
    return decisions


def load_names() -> dict:
    """addr:int -> curated registry name (from build/globals.json), or {} if absent."""
    try:
        data = json.loads((paths.BUILD / "globals.json").read_text(encoding="utf-8"))
    except Exception:
        return {}
    out = {}
    for addr_s, e in data.get("globals", {}).items():
        if e.get("name"):
            out[int(addr_s, 16)] = e["name"]
    return out


def render_guard(g, names) -> str:
    if "opaque" in g:  return g["opaque"]
    if "and" in g:     return "(" + " AND ".join(render_guard(x, names) for x in g["and"]) + ")"
    if "or" in g:      return "(" + " OR ".join(render_guard(x, names) for x in g["or"]) + ")"
    lbl = names.get(g["global"], f"0x{g['global']:x}")
    return f"{lbl}{g['op']}{g['value']}"


def emit_json(decs, names) -> str:
    def jguard(g):
        if "opaque" in g or "and" in g or "or" in g: return g
        return {"global": f"0x{g['global']:x}", "name": names.get(g["global"]), "op": g["op"], "value": g["value"]}
    out_decs = [{"site_offset": f"0x{d['site_offset']:x}", "chapter": d["chapter"],
                 "decision": d["decision"], "guards": [jguard(g) for g in d["guards"]]} for d in decs]
    by_chapter = collections.defaultdict(list)
    for d in out_decs:
        by_chapter[d["chapter"]].append(d["decision"])
    meta = {"note": "GENERATED by tools/scjump_decode.py — do not edit. Decision->scene is native (u00428010), not resolved here.",
            "decision_sites": len({d["site_offset"] for d in decs}),
            "distinct_decisions": len({d["decision"] for d in decs})}
    return json.dumps({"meta": meta, "decisions": out_decs,
                       "by_chapter": {str(k): sorted(set(v)) for k, v in sorted(by_chapter.items(), key=lambda kv: (kv[0] is None, kv[0]))}},
                      ensure_ascii=False, indent=2) + "\n"


def emit_md(decs, names) -> str:
    L = ["<!-- GENERATED by tools/scjump_decode.py — do not edit -->",
         "# SCJUMP progression decisions (generated)", "",
         f"{len({d['site_offset'] for d in decs})} decision sites. Each rule: guards (all true along the "
         "path) -> decision value written to `0x62ccf`. Decision->scene is native (`u00428010`), see "
         "`docs/scjump-progression.md`.", ""]
    by_ch = collections.defaultdict(list)
    for d in decs:
        by_ch[d["chapter"]].append(d)
    for ch in sorted(by_ch, key=lambda c: (c is None, c)):
        L += [f"## chapter {ch}", ""]
        for d in sorted(by_ch[ch], key=lambda d: d["site_offset"]):
            guards = " AND ".join(render_guard(g, names) for g in d["guards"]) or "(unconditional)"
            L.append(f"- `0x{d['site_offset']:x}`: {guards} → **decision {d['decision']}**")
        L.append("")
    return "\n".join(L) + "\n"


def build(scr=None) -> int:
    scr = scr or load_scjump()
    decs = decode(scr)
    names = load_names()
    paths.BUILD.mkdir(parents=True, exist_ok=True)
    (paths.BUILD / "scjump-decisions.json").write_text(emit_json(decs, names), encoding="utf-8")
    (paths.BUILD / "scjump-decisions.md").write_text(emit_md(decs, names), encoding="utf-8")
    print(f"decode: {len({d['site_offset'] for d in decs})} decision sites, "
          f"{len({d['decision'] for d in decs})} distinct decisions -> build/scjump-decisions.{{json,md}}")
    return 0


def _collect_constraints(guard, cons) -> bool:
    """Flatten a guard into per-global (op,value) constraints. Returns False if unsynthesizable
    (opaque, or an `or` whose disjuncts we don't resolve). Handles `and` by flattening."""
    if "opaque" in guard:
        return False
    if "and" in guard:
        return all(_collect_constraints(x, cons) for x in guard["and"])
    if "or" in guard:
        return False        # disjunctions: left to the execution-driven check
    cons[guard["global"]].append((guard["op"], guard["value"]))
    return True


def synthesize_witness(guards):
    """Return {global_addr: int} satisfying all guards, or None if any guard is opaque/or or the
    per-global constraints are unsatisfiable by the simple integer solver. Only fully-static
    (global-vs-immediate) decisions are synthesizable — most SCJUMP paths are gated by a native
    computed value (op 0x60) and are covered by the execution-driven check instead."""
    cons = collections.defaultdict(list)
    for g in guards:
        if not _collect_constraints(g, cons):
            return None
    state = {}
    for addr, cs in cons.items():
        lo, hi = -(1 << 31), (1 << 31) - 1     # inclusive integer window
        eq_vals = {v for op, v in cs if op == "=="}
        ne_vals = {v for op, v in cs if op == "!="}
        for op, v in cs:
            if op == "<":  hi = min(hi, v - 1)
            elif op == "<=": hi = min(hi, v)
            elif op == ">":  lo = max(lo, v + 1)
            elif op == ">=": lo = max(lo, v)
        if eq_vals:
            if len(eq_vals) > 1: return None
            val = next(iter(eq_vals))
            if val in ne_vals or not (lo <= val <= hi): return None
            state[addr] = val
            continue
        val = lo
        while val in ne_vals and val <= hi:
            val += 1
        if val > hi: return None
        state[addr] = val
    return state


def eval_guard(g, state):
    """Evaluate a guard against a {global:value} state (unseeded globals = 0, matching vm0).
    Returns True / False / None (None = contains an unresolved opaque/native term)."""
    if "opaque" in g:
        return None
    if "and" in g:
        vs = [eval_guard(x, state) for x in g["and"]]
        if any(v is False for v in vs): return False
        return None if any(v is None for v in vs) else True
    if "or" in g:
        vs = [eval_guard(x, state) for x in g["or"]]
        if any(v is True for v in vs): return True
        return None if any(v is None for v in vs) else False
    val, op, c = state.get(g["global"], 0), g["op"], g["value"]
    return {"==": val == c, "!=": val != c, "<": val < c, "<=": val <= c,
            ">": val > c, ">=": val >= c}[op]


def _guard_globals(g, acc):
    if "opaque" in g: return
    if "and" in g or "or" in g:
        for x in g.get("and") or g.get("or"): _guard_globals(x, acc)
        return
    acc.add(g["global"])


def run_verify(scr, trials=2000):
    """Two-pronged validation against the executing VM (vm0):
    1. static-witness — for fully-static decisions, synthesize a state and confirm the VM emits it;
    2. execution-driven — seed many random states, run SCJUMP, and for each realized decision check
       the table has that site with the right value AND no static guard is violated by the state.
    Returns a stats dict with a `fails` list (empty = sound)."""
    import random
    import vm0
    decs = decode(scr)
    by_site = {d["site_offset"]: d for d in decs}
    fails = []

    static_ok = static_n = 0
    for d in decs:
        w = synthesize_witness(d["guards"])
        if w is None:
            continue
        static_n += 1
        vm = vm0.VM(scr)
        for addr, val in w.items():
            vm.G[addr] = val
        vm.run()
        if vm.G[DECISION_GLOBAL] == d["decision"]:
            static_ok += 1
        elif len(fails) < 20:
            fails.append(f"static @0x{d['site_offset']:x}: table {d['decision']}, VM {vm.G[DECISION_GLOBAL]}")

    inputs = set()
    for d in decs:
        for g in d["guards"]:
            _guard_globals(g, inputs)
    inputs.discard(VALID_GLOBAL); inputs.discard(DECISION_GLOBAL)
    inputs = sorted(inputs)
    rng = random.Random(0)
    rand_reached = rand_ok = 0
    for _ in range(trials):
        state = {CHAPTER_GLOBAL: rng.randint(1, 9)}
        for g in inputs:
            state[g] = rng.randint(0, 6)          # small range -> exercises equality branches
        vm = vm0.VM(scr, record_trace=True)
        for addr, val in state.items():
            vm.G[addr] = val
        vm.run()
        site = next((off for off in vm.trace if off in by_site), None)
        if site is None:
            continue
        rand_reached += 1
        entry = by_site[site]
        emitted = vm.G[DECISION_GLOBAL]
        problem = None
        if entry["decision"] != emitted:
            problem = f"exec @0x{site:x}: table {entry['decision']}, VM emitted {emitted}"
        elif any(eval_guard(g, state) is False for g in entry["guards"]):
            problem = f"exec @0x{site:x}: a static guard is False on the realizing state"
        if problem:
            if len(fails) < 20: fails.append(problem)
        else:
            rand_ok += 1

    return {"total": len(by_site), "static_ok": static_ok, "static_n": static_n,
            "rand_reached": rand_reached, "rand_ok": rand_ok, "trials": trials, "fails": fails}


def verify(scr) -> int:
    r = run_verify(scr)
    print(f"verify: static-witness {r['static_ok']}/{r['static_n']} exact; "
          f"execution-driven {r['rand_ok']}/{r['rand_reached']} consistent "
          f"(over {r['trials']} random seeds); {len(r['fails'])} failures")
    for m in r["fails"]:
        print("  " + m)
    return 1 if r["fails"] else 0


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--verify", action="store_true")   # implemented in Task 4
    args = ap.parse_args(argv)
    scr = load_scjump()
    if args.verify:
        return verify(scr)                             # Task 4
    return build(scr)                                  # Task 3


if __name__ == "__main__":
    sys.exit(main())

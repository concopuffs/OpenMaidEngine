# SCJUMP Decision-Logic Decode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decode SCJUMP's 29,790-instruction progression state machine into a readable, VM-verified table of `(chapter_mode, path-condition guards) → decision value`, faithfully extracted from the bytecode.

**Architecture:** A static analyzer (`tools/scjump_decode.py`) that reuses `sys4load` to decode SCJUMP, walks its acyclic control-flow graph with a guarded DFS (carrying a guard stack + local→expression map), and emits a decision table (`build/scjump-decisions.{json,md}`). A `--verify` mode synthesizes a witness state per decision and runs SCJUMP through the existing headless VM (`vm0.py`) to confirm the emitted decision matches. Decision→scene resolution is native (`u00428010`) and documented as the deferred boundary.

**Tech Stack:** Python 3.11 (`py -3.11 -X utf8`), stdlib only. Reuses `tools/paths.py`, `tools/sys4load.py`, `tools/vm0.py`, `tools/age_opcodes.py` (`is_label_argument`), and `build/globals.json` (registry names). Standalone test script matching `tools/test_globals.py`.

## Global Constraints

- Run every tool as `py -3.11 -X utf8 tools/<name>.py …` (the `-X utf8` is mandatory on Windows for cp932/Shift-JIS text).
- Tools never hard-code paths — always `import paths` and derive from it.
- `build/scjump-decisions.{json,md}` are **generated & disposable** (`build/` is gitignored); regenerate via `scjump_decode.py`. Never hand-edit.
- `tools/age_opcodes.py` and `tools/vm0.py` are reused **read-only** — do not modify them (direct `vm.G[...]` seeding needs no VM change).
- Git repo root is `age-reimpl/`. Commit from there. End commit messages with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- Tests are standalone scripts using a `check(cond, msg)` helper that exits nonzero on any failure (pattern: `tools/test_globals.py`). Run: `py -3.11 -X utf8 tools/test_scjump.py`.

**Reference facts (verified against SCJUMP.BIN, 2026-07-07):**
- SCJUMP.BIN: 29,790 instructions; code range `[0x00000 .. 0x2f798)`; the END/exit jump target is `0x2f78d` (1765 edges converge there — the max-indegree jump target).
- Opcodes: `mov 0x55` (dest=arg0). Comparisons `eq 0x5a, ne 0x5b, lt 0x5c, lte 0x5d, gr 0x5e, gre 0x5f` — all `(dest_local, a, b)` computing `dest = (a <op> b)`. Logical `and 0x56, or 0x57` `(dest_local, a_local, b_local)`. `jmp 0x8c` (target=arg0). `jcc 0xa0` `(cond, A, B)`: `cond`≠0 → target `A`; `cond`==0 → target `B`; operand `0xffffffff` = fall through to next instruction.
- Operand tuple = `(atype, value)`. Global atypes = {3,4,5,6,8}; immediate = 0; local-int = 9.
- `is_label_argument(opcode, arg_index, value)` returns True for code-target operands (jcc args 1&2, jmp arg 0); it does NOT flag the jcc condition (arg 0) or `0xffffffff`. CFG is a pure DAG — every code-target edge is forward (target offset > source offset); **0 back-edges**.
- Outputs: SCJUMP writes `0x0` (always 1, decision-valid flag) and `0x62ccf` (the decision value) at each of **1755** decision sites.
- Top dispatch on `0x3234` (`chapter_mode`): chapter→block offsets `{1:0x81, 2:0x9f, 3:0x1ded, 4:0x1e6d, 5:0x54c1, 6:0x93c4, 7:0x1e868, 8:0x2aa42, 9:0x2b8a3}`.
- Anchor decision: chapter-1 block (`0x81`) begins `ne(L1, 0x6d3, 1) ; jcc(L1, 0xffffffff, 0x9c) ; mov 0x0,1 ; mov 0x62ccf,0 (@offset 0x94) ; jmp END` → decision `0` under guards `chapter_mode==1 AND 0x6d3!=1`.
- `vm0.VM(scr)` exposes public `.G` (defaultdict global-int bank) and `.run()`. Seed by assigning `vm.G[addr]=val` before `run()`, read `vm.G[0x62ccf]` after. SCJUMP has no back-edges and no text emits → terminates quickly.

---

### Task 1: Scaffold — load, CFG edges, acyclic assert, chapter dispatch

**Files:**
- Create: `tools/scjump_decode.py`
- Create: `tools/test_scjump.py`

**Interfaces:**
- Produces (consumed by Tasks 2-4):
  - `scjump_decode.load_scjump() -> Sys4Script` — loads SCJUMP.BIN via `sys4load`.
  - `scjump_decode.END_TARGET: int` — the exit jump offset (`0x2f78d`).
  - `scjump_decode.forward_edges_only(scr) -> bool` — True iff every code-target edge is forward (acyclic guarantee).
  - `scjump_decode.chapter_dispatch(scr) -> dict[int,int]` — `{chapter_mode_value: block_offset}`.
  - Opcode constants `MOV, JMP, JCC, CMP_OPS, LOGIC_OPS, GLOBAL_ATYPES, IMM, CHAPTER_GLOBAL=0x3234, DECISION_GLOBAL=0x62ccf, VALID_GLOBAL=0x0`.

- [ ] **Step 1: Write the failing test**

Create `tools/test_scjump.py`:

```python
#!/usr/bin/env python3
"""Standalone tests for the SCJUMP decoder. Run: py -3.11 -X utf8 tools/test_scjump.py"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scjump_decode as S

FAILS = []
def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond: FAILS.append(msg)

def test_cfg_acyclic_and_dispatch():
    scr = S.load_scjump()
    check(S.forward_edges_only(scr), "SCJUMP CFG is acyclic (all code edges forward)")
    disp = S.chapter_dispatch(scr)
    expected = {1:0x81, 2:0x9f, 3:0x1ded, 4:0x1e6d, 5:0x54c1, 6:0x93c4, 7:0x1e868, 8:0x2aa42, 9:0x2b8a3}
    check(disp == expected, f"chapter dispatch matches known offsets (got {disp})")

if __name__ == "__main__":
    test_cfg_acyclic_and_dispatch()
    print(f"\n{len(FAILS)} failures")
    sys.exit(1 if FAILS else 0)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_scjump.py`
Expected: FAIL — `ModuleNotFoundError: No module named 'scjump_decode'`.

- [ ] **Step 3: Implement the scaffold in `tools/scjump_decode.py`**

Create `tools/scjump_decode.py`:

```python
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
           and ins.args[1] == (3, CHAPTER_GLOBAL) and ins.args[2][0] == IMM:
            n = ins.args[2][1]
            nxt = code[i + 1] if i + 1 < len(code) else None
            if nxt and nxt.opcode == JCC and nxt.args[1][1] != FALLTHROUGH:
                out[n] = nxt.args[1][1]
    return out


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
```

Note: `main` references `build()`/`verify()` added in later tasks — do not run `scjump_decode.py` with no/`--verify` args until Task 3/4.

- [ ] **Step 4: Run test to verify it passes**

Run: `py -3.11 -X utf8 tools/test_scjump.py`
Expected: PASS — both checks `ok`, `0 failures`.

- [ ] **Step 5: Commit**

```bash
git add tools/scjump_decode.py tools/test_scjump.py
git commit -m "feat: SCJUMP decoder scaffold — CFG acyclic check + chapter dispatch (Task 1)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Guarded DFS + expression model → decision list

**Files:**
- Modify: `tools/scjump_decode.py` (add `Expr` helpers, `decode()`)
- Modify: `tools/test_scjump.py` (anchor-decision + site-count tests)

**Interfaces:**
- Consumes: Task 1 scaffold + constants.
- Produces (consumed by Tasks 3-4):
  - `scjump_decode.decode(scr) -> list[dict]` — one entry per reached decision:
    `{"site_offset": int, "chapter": int|None, "decision": int, "guards": [Guard, …]}`.
  - Guard shape: a `Cmp` = `{"global": int, "op": "==", "value": int}`; a compound =
    `{"and": [Guard,…]}` / `{"or": [Guard,…]}`; opaque = `{"opaque": str}`.
  - `scjump_decode.negate(guard) -> Guard` and `scjump_decode.chapter_of(guards) -> int|None`.

- [ ] **Step 1: Write the failing test**

Add to `tools/test_scjump.py`:

```python
def test_decode_anchor_and_count():
    scr = S.load_scjump()
    decs = S.decode(scr)
    check(len({d["site_offset"] for d in decs}) == 1755, f"1755 distinct decision sites (got {len({d['site_offset'] for d in decs})})")
    # anchor: chapter 1, decision 0, guarded by 0x6d3 != 1
    anchor = [d for d in decs if d["chapter"] == 1 and d["decision"] == 0]
    check(len(anchor) >= 1, "chapter-1 decision 0 exists")
    a = anchor[0]
    has_chapter = any(g.get("global") == 0x3234 and g.get("op") == "==" and g.get("value") == 1 for g in a["guards"])
    has_flag = any(g.get("global") == 0x6d3 and g.get("op") == "!=" and g.get("value") == 1 for g in a["guards"])
    check(has_chapter, "anchor guarded by chapter_mode==1")
    check(has_flag, "anchor guarded by 0x6d3!=1")
```

Add `test_decode_anchor_and_count()` to `__main__`.

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_scjump.py`
Expected: FAIL — `AttributeError: module 'scjump_decode' has no attribute 'decode'`.

- [ ] **Step 3: Implement `Expr` helpers + `decode()` in `tools/scjump_decode.py`**

Add above `main`:

```python
FLIP = {"==": "==", "!=": "!=", "<": ">", "<=": ">=", ">": "<", ">=": "<="}
NEG  = {"==": "!=", "!=": "==", "<": ">=", ">=": "<", "<=": ">", ">": "<="}


def _cmp_expr(op_sym, a, b):
    """Build a Cmp guard from a comparison's operands (a <op> b). Global vs immediate only;
    anything else -> opaque."""
    if a[0] in GLOBAL_ATYPES and b[0] == IMM:
        return {"global": a[1], "op": op_sym, "value": b[1]}
    if a[0] == IMM and b[0] in GLOBAL_ATYPES:
        return {"global": b[1], "op": FLIP[op_sym], "value": a[1]}
    return {"opaque": f"cmp {op_sym} {a} {b}"}


def negate(g):
    if "opaque" in g:      return {"opaque": "!(" + g["opaque"] + ")"}
    if "and" in g:         return {"or": [negate(x) for x in g["and"]]}
    if "or" in g:          return {"and": [negate(x) for x in g["or"]]}
    return {"global": g["global"], "op": NEG[g["op"]], "value": g["value"]}


def _resolve(localmap, operand):
    """Resolve a jcc/logic operand (a local holding a condition) to its guard expression."""
    return localmap.get(operand[1], {"opaque": f"local {operand}"})


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
            if op == MOV and a and a[0] == (3, DECISION_GLOBAL) and a[1][0] == IMM:
                decisions.append({"site_offset": ins.offset, "chapter": chapter_of(guards),
                                  "decision": a[1][1], "guards": list(guards)})
                idx += 1
                continue
            if op in CMP_OPS and len(a) >= 3 and a[0][0] == 9:
                localmap[a[0][1]] = _cmp_expr(CMP_OPS[op], a[1], a[2])
                idx += 1
                continue
            if op in LOGIC_OPS and len(a) >= 3 and a[0][0] == 9:
                kind = LOGIC_OPS[op]
                localmap[a[0][1]] = {kind: [_resolve(localmap, a[1]), _resolve(localmap, a[2])]}
                idx += 1
                continue
            if op == JCC and len(a) >= 3:
                expr = _resolve(localmap, a[0])
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `py -3.11 -X utf8 tools/test_scjump.py`
Expected: PASS — `1755 distinct decision sites`, anchor guards present, `0 failures`.

- [ ] **Step 5: Commit**

```bash
git add tools/scjump_decode.py tools/test_scjump.py
git commit -m "feat: SCJUMP guarded-DFS decode -> decision list (Task 2)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Emit `build/scjump-decisions.{json,md}` (registry-named)

**Files:**
- Modify: `tools/scjump_decode.py` (add `load_names()`, `render_guard()`, `emit_json()`, `emit_md()`, `build()`)
- Modify: `tools/test_scjump.py` (JSON-shape test)

**Interfaces:**
- Consumes: `decode()` (Task 2), `build/globals.json` (registry names, optional).
- Produces:
  - `build/scjump-decisions.json` — `{"meta":…, "decisions":[…], "by_chapter":{…}}`.
  - `build/scjump-decisions.md` — human table grouped by chapter.
  - `scjump_decode.render_guard(g, names) -> str` — e.g. `chapter_mode==7`.

- [ ] **Step 1: Write the failing test**

Add to `tools/test_scjump.py`:

```python
def test_emit_json_shape():
    scr = S.load_scjump()
    decs = S.decode(scr)
    names = S.load_names()
    obj = json.loads(S.emit_json(decs, names))
    check(obj["meta"]["decision_sites"] == 1755, "json meta reports 1755 sites")
    check(all(set(("site_offset", "chapter", "decision", "guards")) <= set(d) for d in obj["decisions"]), "each decision has required keys")
    # rendering uses registry names when present
    check(S.render_guard({"global": 0x3234, "op": "==", "value": 7}, {0x3234: "chapter_mode"}) == "chapter_mode==7",
          "render_guard uses registry name")
```

Add `import json` at the top of the test file (next to existing imports) and add `test_emit_json_shape()` to `__main__`.

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_scjump.py`
Expected: FAIL — `AttributeError: module 'scjump_decode' has no attribute 'load_names'` (and `emit_json`/`render_guard`).

- [ ] **Step 3: Implement emitters + `build()` in `tools/scjump_decode.py`**

Add above `main`:

```python
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
```

- [ ] **Step 4: Run test + build**

Run: `py -3.11 -X utf8 tools/scjump_decode.py`
Expected: `decode: 1755 decision sites, 847 distinct decisions -> build/scjump-decisions.{json,md}`.

Run: `py -3.11 -X utf8 tools/test_scjump.py`
Expected: PASS, `0 failures`.

- [ ] **Step 5: Eyeball the output**

Run: `py -3.11 -X utf8 -c "import json; d=json.load(open('build/scjump-decisions.json',encoding='utf-8')); print(d['meta']); print(d['decisions'][0]); print('chapters:', list(d['by_chapter']))"`
Expected: meta shows 1755/847; first decision has `chapter_mode`-named guard; chapters `1..9` present.

- [ ] **Step 6: Commit**

```bash
git add tools/scjump_decode.py tools/test_scjump.py
git commit -m "feat: emit SCJUMP decision table (json+md, registry-named) (Task 3)

build/scjump-decisions.* are generated (build/ gitignored).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: `--verify` — witness synthesis + VM cross-check

**Files:**
- Modify: `tools/scjump_decode.py` (add `synthesize_witness()`, `verify()`)
- Modify: `tools/test_scjump.py` (verify soundness test)

**Interfaces:**
- Consumes: `decode()` (Task 2), `vm0.VM` (read-only).
- Produces:
  - `scjump_decode.synthesize_witness(guards) -> dict[int,int] | None` — a `{global_addr: value}` satisfying all guards, or `None` if any guard is unsupported/unsatisfiable by the simple solver.
  - `scjump_decode.verify(scr) -> int` — runs the cross-check, prints coverage + mismatches, returns nonzero if any mismatch.

- [ ] **Step 1: Write the failing test**

Add to `tools/test_scjump.py`:

```python
def test_verify_sound_on_sample():
    scr = S.load_scjump()
    decs = S.decode(scr)
    covered = mismatch = 0
    import vm0
    for d in decs[:400]:                      # bounded sample for the test
        w = S.synthesize_witness(d["guards"])
        if w is None:
            continue
        covered += 1
        vm = vm0.VM(scr)
        for addr, val in w.items():
            vm.G[addr] = val
        vm.run()
        if vm.G[0x62ccf] != d["decision"]:
            mismatch += 1
    check(covered > 0, f"synthesized witnesses for some sampled decisions (covered={covered})")
    check(mismatch == 0, f"VM cross-check: 0 mismatches on covered decisions (mismatch={mismatch})")
```

Add `test_verify_sound_on_sample()` to `__main__`.

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_scjump.py`
Expected: FAIL — `AttributeError: module 'scjump_decode' has no attribute 'synthesize_witness'`.

- [ ] **Step 3: Implement witness synthesis + `verify()` in `tools/scjump_decode.py`**

Add above `main`:

```python
def synthesize_witness(guards):
    """Return {global_addr: int} satisfying all guards, or None if any guard is compound/opaque or
    the per-global constraints are unsatisfiable by the simple integer solver."""
    # collect (op, value) constraints per global; only flat Cmp guards are supported.
    cons = collections.defaultdict(list)
    for g in guards:
        if "global" not in g:            # and/or/opaque -> can't synthesize simply
            return None
        cons[g["global"]].append((g["op"], g["value"]))
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
        # pick the smallest in-range integer not excluded by !=
        val = lo
        while val in ne_vals and val <= hi:
            val += 1
        if val > hi: return None
        state[addr] = val
    return state


def verify(scr) -> int:
    import vm0
    decs = decode(scr)
    covered = mismatch = 0
    fails = []
    for d in decs:
        w = synthesize_witness(d["guards"])
        if w is None:
            continue
        covered += 1
        vm = vm0.VM(scr)
        for addr, val in w.items():
            vm.G[addr] = val
        vm.run()
        got = vm.G[DECISION_GLOBAL]
        if got != d["decision"]:
            mismatch += 1
            if len(fails) < 10:
                fails.append((d["site_offset"], d["decision"], got))
    total = len({d["site_offset"] for d in decs})
    print(f"verify: {covered}/{total} decisions synthesizable; {mismatch} mismatches")
    for off, want, got in fails:
        print(f"  MISMATCH @0x{off:x}: table says {want}, VM emits {got}")
    return 1 if mismatch else 0
```

- [ ] **Step 4: Run test + full verify**

Run: `py -3.11 -X utf8 tools/test_scjump.py`
Expected: PASS — covered > 0, 0 mismatches, `0 failures`.

Run: `py -3.11 -X utf8 tools/scjump_decode.py --verify`
Expected: `verify: <N>/1755 decisions synthesizable; 0 mismatches` (N is the covered count; some decisions have compound/opaque guards and are skipped — that is expected and reported, not a failure).

- [ ] **Step 5: Commit**

```bash
git add tools/scjump_decode.py tools/test_scjump.py
git commit -m "feat: --verify witness synthesis + VM cross-check for SCJUMP decode (Task 4)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Curate progression counters into the globals registry

**Files:**
- Modify: `vm-map/globals.toml`
- Regenerate: `build/globals.json`, `docs/global-reference.md`, `build/scjump-decisions.{json,md}`

**Interfaces:**
- Consumes: the globals registry tooling (`globals_build.py`) from the prior slice.
- Produces: named progression counters that render in the SCJUMP table.

- [ ] **Step 1: Add the progression counters to `globals.toml`**

Append to `vm-map/globals.toml` (these are SCJUMP's dominant switch inputs — the story-progress state):

```toml
[[global]]
address = "0x4dfbc"
name = "scjump_progress_a"
category = "counter"
type = "int"
value_domain = "progress index"
usage = "Dominant SCJUMP switch input (1609 comparison reads) — a per-chapter story-progress counter/position the progression machine branches on. INFERENCE from SCJUMP usage; confirm exact meaning via a listing/playthrough."
source = "inference"
confidence = "med"
depends_on = []

[[global]]
address = "0x2052e"
name = "scjump_progress_b"
category = "counter"
type = "int"
value_domain = "progress index"
usage = "Second SCJUMP switch input (1223 comparison reads) — progression counter/position. INFERENCE from SCJUMP usage."
source = "inference"
confidence = "med"
depends_on = []

[[global]]
address = "0x152618"
name = "scjump_progress_c"
category = "counter"
type = "int"
value_domain = "progress index"
usage = "Third SCJUMP switch input (530 comparison reads) — progression counter/position. INFERENCE from SCJUMP usage."
source = "inference"
confidence = "med"
depends_on = []

[[global]]
address = "0xe6c5d"
name = "scjump_progress_d"
category = "counter"
type = "int"
value_domain = "progress index"
usage = "SCJUMP switch input (168 comparison reads) — progression counter/position. INFERENCE from SCJUMP usage."
source = "inference"
confidence = "med"
depends_on = []
```

- [ ] **Step 2: Rebuild the registry + SCJUMP table, verify lint/tests**

Run: `py -3.11 -X utf8 tools/globals_build.py --lint`
Expected: `lint: 0 errors, 0 warnings`.

Run: `py -3.11 -X utf8 tools/globals_build.py --build`
Expected: `build: … globals -> build/globals.json, docs/global-reference.md`.

Run: `py -3.11 -X utf8 tools/scjump_decode.py`
Expected: re-emits the table; the progress counters now render by name.

Run: `py -3.11 -X utf8 tools/test_globals.py` and `py -3.11 -X utf8 tools/test_scjump.py`
Expected: both `0 failures`.

- [ ] **Step 3: Confirm the counters render in the SCJUMP table**

Run: `py -3.11 -X utf8 -c "d=open('build/scjump-decisions.md',encoding='utf-8').read(); print('scjump_progress_a' in d, 'scjump_progress_b' in d)"`
Expected: `True True`.

- [ ] **Step 4: Commit**

```bash
git add vm-map/globals.toml docs/global-reference.md
git commit -m "feat: name SCJUMP progression counters in globals registry (Task 5)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Docs + canonical map + status memory

**Files:**
- Create: `docs/scjump-progression.md`
- Modify: `CLAUDE.md` (canonical-documents map row — root, untracked by the repo but edit on disk)
- Modify: `docs/tools-reference.md`, `docs/name-resolution.md`, `docs/PROJECT-STRUCTURE.md`
- Modify: `~/.claude/…/memory/himegari-port-status.md` + `MEMORY.md`

**Interfaces:** Docs only; no code interfaces.

- [ ] **Step 1: Write `docs/scjump-progression.md`**

Create the canonical narrative doc:

```markdown
# SCJUMP — progression / scene-sequencing logic

`SCJUMP.BIN` (29,790 instructions) is the game's **progression state machine**: it decides "what
comes next" in the story. `tools/scjump_decode.py` decodes its decision logic faithfully into a
readable, VM-verified table.

## Mechanism
- **Top switch** on `0x3234` (`chapter_mode`, 1..9) dispatches to a per-chapter block
  (`1→0x81 … 9→0x2b8a3`).
- Each chapter block is an **acyclic tree** (0 back-edges) of comparisons on progress counters
  (`scjump_progress_a/b/c/d` = `0x4dfbc`/`0x2052e`/`0x152618`/`0xe6c5d`) and story flags.
- At each of **1755 decision sites** it writes `0x0 = 1` (decision-valid) and
  `0x62ccf = <decision value>` (847 distinct values).

## The decode
`scjump_decode.py` walks the CFG with a guarded DFS (guard stack + local→expression map) and emits,
per site, `(chapter, path-condition guards) → decision`. Output (generated, disposable):
`build/scjump-decisions.json` (machine) and `build/scjump-decisions.md` (human, globals rendered by
their `vm-map/globals.toml` registry names). Regenerate: `py -3.11 -X utf8 tools/scjump_decode.py`.

**Verification.** `--verify` synthesizes a witness state per decision from its guards, runs SCJUMP
headlessly through `vm0.py`, and asserts the emitted `0x62ccf` matches — a faithful-decode oracle.
Coverage is < 100% (decisions with compound/opaque guards are skipped and reported); agreement on
covered decisions is 100%.

## The native boundary (deferred)
The decision value → actual `SCxxxx.BIN` is resolved by the **native op `u00428010`** (consumers do
`lookup-array(ptr, 0x5f0ed, 0x62ccf)` then `u00428010(ptr)`). That mapping is compiled into `AGE.EXE`,
not present in any script — so it is engine-level, the same bucket as the `call-script` dispatch
(see `name-resolution.md §1`). Cracking it needs the engine dump / Frida and is a separate slice.

## See also
- `vm-map/globals.toml` — the named globals SCJUMP switches on (chapter_mode, progress counters, flags).
- `name-resolution.md §1` — call-script / native dispatch (the decision→scene boundary lives here too).
```

- [ ] **Step 2: Add the CLAUDE.md canonical-map row**

In `CLAUDE.md`'s canonical-documents map table (workspace root; edit on disk — it is not in the repo), add after the global-variable row:

```markdown
| Progression / scene-sequencing logic (SCJUMP) | `age-reimpl/docs/scjump-progression.md` |
```

- [ ] **Step 3: Update `docs/tools-reference.md`**

Add rows (match the existing table columns):

```markdown
| `scjump_decode.py` | Decode SCJUMP's progression logic → decision table; `--verify` VM cross-check. | `scjump_decode.py` · `--verify` | SCJUMP.BIN, `build/globals.json` → ⚙ `build/scjump-decisions.{json,md}` |
| `test_scjump.py` | Unit tests for the SCJUMP decoder. | `test_scjump.py` | — |
```

- [ ] **Step 4: Cross-reference in `docs/name-resolution.md`**

In `docs/name-resolution.md §1` (call-script resolution), add a note after the SCJUMP paragraph:

```markdown
**Update (2026-07-07):** SCJUMP's *decision logic* is now decoded — `(chapter_mode, guards) →
decision value` — see `docs/scjump-progression.md` and `tools/scjump_decode.py`. Only the native
decision→scene hop (`u00428010`) remains engine-level, in the same bucket as `call-script`.
```

- [ ] **Step 5: Update `docs/PROJECT-STRUCTURE.md`**

Add the tool + generated artifacts (mirror the existing entries):

```markdown
    │   ├── scjump_decode.py                    decode SCJUMP progression logic -> build/scjump-decisions.* ; --verify
```
(under `tools/`), and under `docs/`:
```markdown
    │   ├── scjump-progression.md               SCJUMP progression decode + native decision→scene boundary
```
and under `build/`:
```markdown
    │   ├── scjump-decisions.{json,md}          GENERATED by scjump_decode.py (progression decision table)
```

- [ ] **Step 6: Commit docs**

```bash
git add docs/scjump-progression.md docs/tools-reference.md docs/name-resolution.md docs/PROJECT-STRUCTURE.md
git commit -m "docs: SCJUMP progression decode — canonical doc + references (Task 6)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 7: Update the status memory**

Edit `~/.claude/projects/S--Game-Hacking-Eushully-Himegari/memory/himegari-port-status.md`: add a bullet under Phase B recording that SCJUMP's decision logic is decoded (`tools/scjump_decode.py` → `build/scjump-decisions.{json,md}`, 1755 sites / 847 decisions, VM-verified), progression counters named in the registry, and that decision→scene (`u00428010`) remains the documented native boundary. Update the `MEMORY.md` index line. Convert "2026-07-07" to the absolute date. (Memory files are outside the git repo — no commit needed.)

---

## Notes for the implementer

- **Ordering:** `main` in Task 1 references `build()`/`verify()` defined in Tasks 3/4 — don't run the tool until those exist. Run tasks in order.
- **`py -3.11 -X utf8` always.**
- `build/*` artifacts are gitignored — commit only source (`tools/*.py`, `vm-map/globals.toml`) and tracked docs (`docs/*`). `CLAUDE.md` at the workspace root is outside the repo; edit it on disk (it won't appear in `git add`).
- The `--verify` covered count is expected to be well under 1755 (many decisions have `and`/`or`/opaque guards the simple witness solver skips). That is a reported coverage metric, not a failure — 0 mismatches on the covered set is the success criterion.
- If `test_scjump.py`'s verify test is slow (400 VM runs), that is acceptable for a correctness test; the full `--verify` runs all synthesizable decisions.
- **DFS performance relies on SCJUMP's tree-like shape** (verified: every decision ends in `jmp END`, comparison cascades don't merge, so no control-flow diamonds). The un-memoized DFS is therefore ~linear. It always terminates (0 back-edges). If `decode()` is unexpectedly slow/explosive, that signals block-merging we didn't observe — the fix is per-`idx` memoization, but note two paths reaching the same `idx` with different guards yield different decisions, so any memo must be keyed by more than `idx`; don't add it unless measurement shows it's needed.

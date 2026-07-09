# Differential Oracle (control-flow) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pinpoint mis-modeled opcodes by diffing the real engine's offset-execution path against our VM's, on the deterministic opening — output "agreed N steps, diverge at offset X = op 0xYY".

**Architecture:** A Frida engine op-tracer emits per-op `(codebase, offset)` to `build/engine-optrace.jsonl` (recon-gated capture). Our VM emits the SC0000 offset sequence to `build/vm-optrace.json`. `diff_optrace.py` identifies SC0000's codebase in the engine trace, aligns the two offset sequences, and reports the first divergence. The diff/parse logic is pure (unit-tested); Frida + VM emitter are exercised live.

**Tech Stack:** Python 3.11 (Frida 17.x for the tracer, `tomllib`/json for the diff), `Age.Cli` (.NET 8) for the VM trace, ghidra-mcp only if we need to re-confirm a handler addr.

**Spec:** `docs/superpowers/specs/2026-07-09-differential-oracle-design.md`.

## Global Constraints

- Frida scripts: `py -3.11 -u -X utf8 tools/frida/<name>.py`; **read-only** (Interceptor + memory reads; no patching). Model on `dump_engine.py`/`probe_frame_cadence.py` (attach + `send()`/`on(message)`; frida-subdir tools compute `REPO = parents[2]`, no `paths.py`).
- **Known-safe vs risky hooks:** `vm_operand_fetch@0x41b940` is proven-safe (plain-JS, `probe_frame_cadence.py`). `adv_interpreter_tick@0x410fb0` crashed with a CModule hook — only ever plain-JS here, and Task 1 gates on it.
- Engine ctx offsets (now typed): `cur_ctx_index@0x53d14`, `frame_pc@0x53d2c`, `frame_codebase@0x53d28`; per-context stride `0x78`. `offset = (pc − codebase)/4`.
- The opening is `SC0000 --boot` (deterministic). `build/` is disposable/gitignored.
- Control-flow only (offset path); effects-diff is out of scope (phase 2).

---

## Task 1: Engine op-tracer + recon gate

**Files:**
- Create: `tools/frida/trace_engine_ops.py`
- Reference: `tools/frida/probe_frame_cadence.py` (the proven 0x41b940 hook + attach boilerplate)

- [ ] **Step 1: Write the tracer with a selectable hook + batched flush**

`trace_engine_ops.py`: attach to the running game; a `--hook tick|operand` flag (default `tick`). JS `Interceptor.attach`:
- `tick` mode → `adv_interpreter_tick` `0x410fb0`; `operand` mode → `vm_operand_fetch` `0x41b940`.
- onEnter: `ctx` = `this.context.ecx` (thiscall `ecx=ctx`); `curCtx = ctx.add(0x53d14).readU32()`; `pc = ctx.add(0x53d2c + curCtx*0x78).readU32()` (read via a computed pointer — recompute the address each call); `codebase = ctx.add(0x53d28 + curCtx*0x78).readU32()`; push `{cb: codebase, off: (pc-codebase)>>2}` to a JS buffer. In `operand` mode, skip if `pc === lastPc` (dedupe consecutive same-pc).
- Flush the buffer to Python via `send(batch)` every ~2000 entries; Python appends each `{codebase, offset}` to `build/engine-optrace.jsonl`.
Model attach/`on(message)` exactly on `probe_frame_cadence.py`.

- [ ] **Step 2: RECON — capture the opening safely (THE GATE)**

Launch the game to the opening (or attach at the title and let the opening replay). Run:
`py -3.11 -u -X utf8 tools/frida/trace_engine_ops.py --hook tick`
Let the opening auto-advance to the first wait-for-input, then stop. Inspect `build/engine-optrace.jsonl`.
- **GATE — tick hook stable + trace captured:** proceed. Record entry count + number of distinct codebases.
- **GATE — tick hook destabilizes the game** (crash/hang): switch to `--hook operand` (proven-safe) and re-capture. Record which hook worked.
- **GATE — neither yields a usable trace:** STOP, document in `engine-re.md`, end the slice.
Expected: a jsonl with thousands of `(codebase, offset)` entries across a handful of codebases (boot scripts + SC0000).

- [ ] **Step 3: Sanity-identify the SC0000 codebase**

Quick check (inline Python): group entries by `codebase`; for each, does its offset-0/low-offset region's static opcode (from `build/disasm/SC0000.asm` via `sys4load`) match SC0000's opening? Report the codebase whose executed offsets are consistent with SC0000 (largest plausible run). This de-risks Task 3's codebase identification.
Expected: one codebase identifiable as SC0000.

- [ ] **Step 4: Commit the tracer**

```bash
git add tools/frida/trace_engine_ops.py
git commit -m "re(frida): engine op-path tracer (tick/operand hook, per-script offsets)"
```

---

## Task 2: VM offset-trace emitter

**Files:**
- Modify (if needed): `Age.Cli` trace command (a `--trace-json <path>` that dumps executed SC0000 offsets), or reuse an existing offset-trace artifact.
- Test: `engine/Age.Engine.Tests/` (a small test for the JSON emitter if one is added).

- [ ] **Step 1: Check for an existing full offset trace**

Inspect how `Age.Cli trace --trace` / the `vm0-trace.json` selftest artifact are produced (`grep` `Age.Cli`, `ITraceSink`, `vm0-trace`). If a full ordered-offset JSON for a booted scene already exists or is trivially emittable, use it and skip to Step 3.
Expected: a decision — reuse vs add a small emitter.

- [ ] **Step 2: Add a minimal `--trace-json` emitter (if needed)**

Add a `JsonOffsetTraceSink : ITraceSink` (or a post-run collector on the existing sink) that records each Step's script-relative offset in order, and a `--trace-json <path>` option on the trace command writing `{"scene":"SC0000","offsets":[...]}`. Keep it observe-only (parity preserved — non-JSON runs unaffected). Add one xUnit test: running a tiny synthetic scene yields the expected offset list. `dotnet test` green.

- [ ] **Step 3: Produce the VM trace for SC0000 (booted, to match the engine)**

Run: `dotnet run --project engine/Age.Cli -- trace SC0000.BIN --boot --trace-json build/vm-optrace.json` (exact invocation per the CLI). Confirm the offset count is on the order of the engine trace's SC0000 run.
Expected: `build/vm-optrace.json` with the ordered SC0000 offsets.

- [ ] **Step 4: Commit (if a C# change was made)**

```bash
git add engine/
git commit -m "feat(engine): --trace-json emitter for the differential oracle"
```

---

## Task 3: `diff_optrace.py` — align + first-divergence

**Files:**
- Create: `tools/diff_optrace.py`
- Create: `tools/test_diff_optrace.py`

**Interfaces:**
- `first_divergence(a: list[int], b: list[int]) -> dict` → `{"agreed": n, "index": i, "a": a[i]|None, "b": b[i]|None}` or `{"agreed": len, "index": None}` if one is a prefix of the other / equal.
- `pick_scene_codebase(entries: list[dict], vm_offsets: list[int]) -> int|None` → the codebase whose offset sequence shares the longest common prefix with `vm_offsets` (identifies SC0000's instance).

- [ ] **Step 1: Write failing tests**

```python
# tools/test_diff_optrace.py (plain runner)
import sys
from diff_optrace import first_divergence, pick_scene_codebase
FAILS=[]
def check(c,m): (FAILS.append(m) or print("FAIL:",m)) if not c else print("ok:",m)

def test_equal_no_divergence():
    r = first_divergence([0,1,2,3],[0,1,2,3])
    check(r["index"] is None and r["agreed"]==4, "equal traces -> no divergence")

def test_first_divergence_point():
    r = first_divergence([0,1,2,9],[0,1,2,3])
    check(r["index"]==3 and r["a"]==9 and r["b"]==3, "divergence at first differing offset")

def test_prefix_shorter_vm():
    r = first_divergence([0,1,2,3],[0,1])          # vm ends early
    check(r["index"]==2 and r["b"] is None and r["agreed"]==2, "shorter VM trace flagged at end")

def test_pick_codebase_by_longest_common_prefix():
    entries=[{"codebase":100,"offset":0},{"codebase":100,"offset":5},   # cb100: [0,5,...]
             {"codebase":200,"offset":0},{"codebase":200,"offset":1},{"codebase":200,"offset":2}]
    check(pick_scene_codebase(entries,[0,1,2])==200, "codebase matching VM prefix chosen")

def main():
    test_equal_no_divergence(); test_first_divergence_point(); test_prefix_shorter_vm()
    test_pick_codebase_by_longest_common_prefix()
    print("FAILURES:",len(FAILS)); return 1 if FAILS else 0
if __name__=="__main__": sys.exit(main())
```
Run → FAIL (`ModuleNotFoundError`).

- [ ] **Step 2: Implement `diff_optrace.py`**

Pure functions `first_divergence` + `pick_scene_codebase` (per the tests), plus a CLI: load `build/engine-optrace.jsonl` (list of `{codebase, offset}` in order) and `build/vm-optrace.json` (`{"offsets":[...]}`); `cb = pick_scene_codebase(entries, vm_offsets)`; extract the engine offset sequence for `cb`; `d = first_divergence(engine_seq, vm_offsets)`; if diverged, print the offset, the **opcode at that offset** (decode `build/disasm/SC0000.asm` or call `sys4load`), and ±3 ops of context on each side. Run tests → PASS.

- [ ] **Step 3: Run the oracle end-to-end**

Run: `py -3.11 -X utf8 tools/diff_optrace.py SC0000`
Expected: either "no divergence over N steps" or "agreed N, diverge at offset X = op 0xYY" with context. **Interpret the result:** if it lands on the known coroutine yield (`0x140`/`0x7b`, `~0x450–0x50f`), that confirms the tool pinpoints a real known gap (the self-test). Record the finding.

- [ ] **Step 4: Commit**

```bash
git add tools/diff_optrace.py tools/test_diff_optrace.py
git commit -m "re: diff_optrace.py — engine-vs-VM offset-path divergence oracle"
```

---

## Task 4: Docs + memory + close

**Files:**
- Modify: `docs/engine-re.md` (the oracle: mechanism, capture method that worked, the first divergence found)
- Modify: `docs/tools-reference.md` (`trace_engine_ops.py`, `diff_optrace.py`, `--trace-json`)
- Modify: `~/.claude/…/memory/himegari-port-status.md` (milestone; lever #3 done; phase-2 effects-diff next)

- [ ] **Step 1: Update engine-re.md**

Record: the differential-oracle mechanism (offset-path diff), which capture hook worked (tick vs 0x41b940), the SC0000 codebase identification, and the first divergence the oracle reported (offset + op + interpretation). Note it as the repeatable way to localize a mis-modeled op.

- [ ] **Step 2: Update tools-reference.md**

Rows for `trace_engine_ops.py` (Runtime capture / Frida) and `diff_optrace.py` (Native engine RE or a new "Validation" group); note the `--trace-json` VM emitter.

- [ ] **Step 3: Update the status memory**

Record: differential oracle DONE — capture method, first divergence found, tool names; lever #3 of 3 complete; phase-2 (effects-diff: global/gfx writes) is the next extension.

- [ ] **Step 4: Commit docs**

```bash
git add docs/engine-re.md docs/tools-reference.md
git commit -m "re: record differential oracle outcome + first divergence"
```

---

## Self-Review

**Spec coverage:** engine tracer + recon gate → Task 1; per-script codebase tagging → Task 1 Step 3 + Task 3 `pick_scene_codebase`; VM offset trace → Task 2; diff + first-divergence + opcode context → Task 3; coroutine-gap self-test → Task 3 Step 3; docs/memory → Task 4. Covered. (The spec's loader-hook codebase-tagging is simplified to post-hoc `pick_scene_codebase` matching — noted; a loader hook can be added later if identification is ambiguous.)

**Placeholder scan:** Task 1/Task 2 describe the Frida/C# bodies to the key calls (hook target, ctx reads, `--trace-json`) rather than full source, and flag "model on `probe_frame_cadence.py`" / "reuse if exists" — intentional (live-API bring-up like the earlier Frida/Ghidra tasks). Task 3 (the pure, deterministic core) is complete code + tests. No hidden TODOs.

**Type consistency:** engine trace = list of `{codebase:int, offset:int}`; VM trace = `{"offsets":[int]}`; `pick_scene_codebase(entries, vm_offsets)->int`; `first_divergence(list[int],list[int])->{agreed,index,a,b}` — consumed consistently by the Task 3 CLI. Matches the tests.

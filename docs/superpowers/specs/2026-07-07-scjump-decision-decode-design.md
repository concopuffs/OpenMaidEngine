# SCJUMP static decision-logic decode — design

**Date:** 2026-07-07
**Status:** approved (brainstorming) → ready for implementation plan
**Slice:** Phase B story-state layer, progression sub-slice (#2, following the globals registry).

---

## 1. Problem & goal

`SCJUMP.BIN` (29,790 instructions) is the game's **progression state machine** — it decides "what
comes next" in the story. Today it is an opaque blob. This slice **decodes its decision logic
faithfully into a readable, VM-verified progression graph**: for every decision site, the exact
`(chapter_mode, guarding conditions) → decision value` rule, extracted from the bytecode.

This is *decode*, not stub: every rule traces to specific instructions; nothing is invented. The VM
already executes SCJUMP correctly — this slice makes that logic **legible and verified**, it does not
change execution.

## 2. Non-goals (this slice)

- **Decision→scene resolution is deferred.** The decision value `0x62ccf` becomes an actual
  `SCxxxx.BIN` via the **native op `u00428010`** + array `0x5f0ed` — the resolution is compiled into
  `AGE.EXE`, not present in any script. We document this as the engine boundary (same bucket as the
  unsolved `call-script` dispatch, see `docs/name-resolution.md §1`) and stop there. No Frida / engine
  dump in this slice.
- **No VM changes** beyond, at most, a minimal headless-seed hook if `vm0.py` lacks one (used only by
  `--verify`; must not alter existing trace/sweep parity).
- Not decoding other flow scripts (FIELD/CAMP dispatch loops) — only SCJUMP's decision production.

## 3. Findings (verified against the bytecode, 2026-07-07)

- **Outputs:** SCJUMP writes exactly two globals, paired at every decision: `mov 0x0, 1`
  (decision-valid flag, always 1) and `mov 0x62ccf, <value>` (the decision). **1755 decision sites,
  847 distinct values, range 0..1693.** `0x62ccf` is our curated `scjump_decision_out`.
- **Top dispatch** switches on `0x3234` (`chapter_mode`): `eq(0x3234==N) ; jcc`. Chapter → block:
  `1→0x81, 2→0x9f, 3→0x1ded, 4→0x1e6d, 5→0x54c1, 6→0x93c4, 7→0x1e868, 8→0x2aa42, 9→0x2b8a3`.
- **Dominant decision inputs** (comparison-read): `0x4dfbc` (1609×), `0x2052e` (1223×), `0x152618`
  (530×) — the progression counters — plus story flags (`0xa63`, `0xa99`, `0x71b`, …).
- **Control flow is a pure DAG:** 1764 `jcc` + 1764 `jmp`, **0 back-edges** (1763 forward edges + 1765
  edges to the END jump `0x2f78d`). No loops → guarded DFS is bounded and safe.
- **jcc semantics** (`0xa0`, argc 3): `jcc(cond, A, B)` — `cond`≠0 → target `A`; `cond`==0 → target
  `B`; an operand `0xffffffff` = fall through to the next instruction. `cond` is a local holding a
  prior comparison result. Example (chapter-1 block entry):
  `ne(L1, 0x6d3, 1) ; jcc(L1, 0xffffffff, 0x9c) ; mov 0x0,1 ; mov 0x62ccf,0 ; jmp END` — decision 0 is
  emitted exactly when `0x6d3 != 1` (the fall-through branch, i.e. `L1` true).
- **Comparison ops** (result→arg0 local, operands arg1/arg2): `eq 0x5a, ne 0x5b, lt 0x5c, lte 0x5d,
  gr 0x5e, gre 0x5f`. **Logical combiners:** `and 0x56, or 0x57` (arg0=dest local, arg1/arg2=source
  locals). `mov 0x55` writes arg0.
- **The consumer is native:** FIELD/CAMP/scenes do `lookup-array(ptr, 0x5f0ed, 0x62ccf)` then
  `u00428010(ptr)` — decision→scene is a native handler. (`lookup-array` = `0x61`.)

## 4. Architecture — `tools/scjump_decode.py`

Static analyzer, reusing `sys4load` for decode and `paths` for location. Three stages.

### 4a. CFG build
Load SCJUMP; collect all jump/jcc code-target offsets (via `is_label_argument`) as block leaders;
split the instruction stream into basic blocks; build the successor graph. Assert acyclic (finding
§3). The END jump target (`0x2f78d`) is a terminal sink.

### 4b. Guarded DFS (the core)
Walk from entry (offset 0) carrying two path-local structures:
- a **guard stack** — the list of asserted condition-expressions along the current path;
- a **local→expression map** — updated whenever a compute op writes a local.

**Expression model** (`Expr`):
- `Cmp(global_addr, op, value)` — from a comparison op where one operand is a global (atype ∈
  {3,4,5,6,8}) and the other an immediate (atype 0). `op ∈ {==,!=,<,<=,>,>=}` from the opcode.
- `And(l, r)`, `Or(l, r)` — from `0x56`/`0x57` combining two locals (resolved via the local map).
- `Opaque(desc)` — any condition we cannot model faithfully (global-vs-global compare, arithmetic
  feeding a condition, an unresolved local). Represented, never silently dropped.

**Traversal:** at `cmp Ln, …` set `localmap[Ln] = Cmp(...)` (or `Opaque`); at `and/or Ln, La, Lb`
set `localmap[Ln] = And/Or(localmap[La], localmap[Lb])`. At `jcc(Lc, A, B)`:
- expr = `localmap.get(Lc, Opaque(Lc))`
- recurse to target `A` (or next instr if `A==0xffffffff`) with guard **`expr` asserted true**;
- recurse to target `B` (or next instr if `B==0xffffffff`) with guard **`Not(expr)`** asserted.

At `mov 0x62ccf, V` emit a decision `{guards: copy of stack, decision: V, site_offset}`. At the END
jump, return. DAG + no revisiting within a path → bounded (~1755 leaves).

**Guard negation** normalizes for readability: `Not(Cmp(g,==,v)) → Cmp(g,!=,v)`, `< ↔ >=`,
`<= ↔ >`, `> ↔ <=`, `>= ↔ <`; `Not(And/Or)` via De Morgan; `Not(Opaque)` kept as a negated-opaque
marker.

### 4c. Chapter attribution
The first guard on every path is `chapter_mode == N` (top dispatch). Tag each decision with that N.
Decisions whose path never asserts a `chapter_mode` equality (shouldn't occur) are tagged `null`.

## 5. Output

- **`build/scjump-decisions.json`** (generated, disposable — `build/` is gitignored): `meta` +
  `decisions[]`, each `{site_offset, chapter, decision_value, guards:[…]}`. Each guard is
  `{global:"0xADDR", name:<from registry or null>, op:"==", value:N}` for `Cmp`, or a nested
  `{and:[…]}`/`{or:[…]}`/`{not:…}`/`{opaque:"desc"}`. Also a `by_chapter` index and summary counts
  (sites, distinct decisions, opaque-guard count).
- **`build/scjump-decisions.md`** (generated, disposable): human table grouped by chapter — each rule
  as `chapter_mode==7 AND 0x6d3!=1 → decision 0`, globals rendered with their **globals.toml registry
  names** (loaded from `build/globals.json`; e.g. `0x3234`→`chapter_mode`).

The generated artifacts live in `build/`; the stable narrative lives in the canonical doc (§8).

## 6. Validation — VM cross-check (`--verify`)

Because the table is a decision function over a deterministic DAG, exactly one path is realized per
concrete state. Validate the decode by **witness synthesis + VM ground truth**:

1. For each decision, **synthesize a witness state** from its guards: collect the per-global
   constraints (equalities and `</<=/>/>=/!=` bounds) and pick a satisfying value per global (solve
   the conjunction; equality wins; inequalities → pick an in-range integer; `!=` → avoid the value).
   If a guard is `Opaque` or the per-global constraints are unsatisfiable by the simple solver, mark
   the decision **uncovered** and skip (reported, not failed).
2. **Run SCJUMP headlessly** with the witness as the initial global bank (via `vm0.py`; add a minimal
   seed hook if absent — must not change existing modes) and read the emitted `0x62ccf`.
3. **Assert** the emitted value equals the decision's value. Any mismatch = a mis-modeled condition →
   hard failure with the offending site.

`--verify` reports: covered/total decisions, and agreement (must be 100% on covered). Optionally also
seed a batch of random `(chapter, counters, flags)` states and check that the single table-predicted
decision (all guards concrete-true) matches the VM — a second, independent check. Coverage < 100% is
expected (opaque guards) and is a documented measure, not a failure.

## 7. Cross-link — name the progression counters

SCJUMP's dominant inputs are the story-progress state. As part of this slice, curate them into
`vm-map/globals.toml` (then `globals_build.py --build`): `0x4dfbc`, `0x2052e`, `0x152618` (and
`0xe6c5d`) as `progress_*` counters (`category = "counter"` or `story-flag` as fits, `source =
inference`, evidence = "top SCJUMP switch input"). This makes both the SCJUMP table and the wider
corpus more readable, and closes the loop with the registry built in the previous slice.

## 8. Docs & canonical map

- **New canonical doc `docs/scjump-progression.md`** (hand-authored, stable narrative): what SCJUMP is,
  the decode mechanism, the **native decision→scene boundary** (`u00428010` + `0x5f0ed`), how to read
  `build/scjump-decisions.{json,md}` and regenerate them, the `--verify` coverage/agreement summary,
  and cross-links to `name-resolution.md §1` (call-script) and `globals.toml`.
- **CLAUDE.md canonical-documents map** — add a row: `Progression / scene-sequencing logic (SCJUMP) →
  docs/scjump-progression.md`.
- **`docs/tools-reference.md`** — add `scjump_decode.py` (+ `test_scjump.py`).
- **`docs/name-resolution.md §1`** — add a back-reference: SCJUMP's *decision logic* is now decoded
  (`scjump-progression.md`); only the native decision→scene hop remains engine-level.
- Update the status memory (`himegari-port-status.md` + `MEMORY.md`).

## 9. Testing — `tools/test_scjump.py`

Standalone script (`check(cond,msg)` pattern, like `test_globals.py`):
- **CFG acyclic** — the built graph has no back-edge (regression-guards the DFS assumption).
- **Chapter dispatch** — extracted chapter→block map equals the known offsets (`1→0x81 … 9→0x2b8a3`).
- **Known decision** — the chapter-1 entry decision decodes to guards `{chapter_mode==1, 0x6d3!=1} →
  decision 0` (hand-traced anchor).
- **Totals** — decode yields 1755 decision sites (regression on the site count).
- **`--verify` sound** — witness cross-check runs on a bounded sample and reports 100% agreement on
  covered decisions (0 mismatches).

## 10. Implementation order (for the plan)

1. `scjump_decode.py`: load + CFG build + acyclic assert; `test_scjump.py` CFG/chapter tests.
2. Guarded DFS + Expr model + negation → in-memory decisions; known-decision + totals tests.
3. Emit `build/scjump-decisions.json` + `build/scjump-decisions.md` (registry-named); JSON shape test.
4. `--verify`: witness synthesis + headless SCJUMP run + agreement report; verify test.
5. Curate progression counters into `globals.toml`; rebuild registry.
6. Docs: `docs/scjump-progression.md`, CLAUDE.md map row, tools-reference, name-resolution back-ref;
   status memory.

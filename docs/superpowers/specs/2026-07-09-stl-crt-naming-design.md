# STL/CRT auto-naming — de-noising the library layer — design

**Date:** 2026-07-09
**Status:** approved (design), plan pending
**Lever:** #2 of 3 in the RE-front-loading program (after handler-labeling, import-map, ctx-struct;
before the differential oracle). See the status memory.
**Home in the canonical map:** results in `docs/engine-re.md`; if a curated registry is built, it becomes
a NEW canonical source (`vm-map/lib-functions.toml`) with a row in the CLAUDE.md maps.

## Motivation

3,660 of 4,415 functions in the `/v2` image are still default-named (`FUN_`/`LAB_`), a large slice of
which is statically-linked CRT/STL. We keep re-recognizing the same library functions by behavior
(`std::map::find` = `FUN_0047f280`, the open-addressing hash insert = `FUN_0042cf70`, `operator new`,
string/`memcpy` ops). Naming them lets the decompile stop burying real engine logic in unnamed library
noise — the last cheap static front-loader before the dynamic oracle. This is explicitly a *de-noising
polish*, lower-leverage than the first three levers, so it is scoped to stay light.

## Approach (recon-first, decided in brainstorming)

Ghidra's **Function ID** analyzer is enabled but has named little so far (the bundled FidDbs may not
cover this VC9/VS2008-era static runtime). So the recon decides the shape.

### Task 1 — Function ID recon (THE GATE)

Re-run the Function ID analyzer against `/v2`, including a pass with **"Always Apply FID Labels"**
enabled, and measure the delta: how many `FUN_`/`LAB_` functions become named (source `ANALYSIS`/library
names). Record the before (3,660 default) and after counts.

- **GATE — STRONG** (names a meaningful number, say ≥ 100 new library names): Function ID *is* the win.
  Keep the labels, `save_program`, document the count, done. No curated registry needed.
- **GATE — WEAK** (~0 new): the bundled FidDbs don't match this runtime → fall back to the curated
  hot-set (Task 2). Do **not** invest in generating a custom VC9 FidDb (out of scope — heavy, low return).

Function ID applies labels with `ANALYSIS` source, which never clobbers our `USER_DEFINED` names — so a
strong result composes cleanly with the handler/ctx work.

### Task 2 — curated hot-set (ONLY if Task 1 is WEAK)

A small canonical registry, same pattern as `engine-ctx.toml`:

`vm-map/lib-functions.toml` — `[[func]]` entries `{ address, name, note, source, confidence }` for the
library functions we have already identified in our RE (seeded from `engine-re.md`): e.g.
`0x47f280 → std_map_find`, `0x42cf70 → hashmap_insert`, plus `operator new`/`delete`, `memcpy`, and any
string ops we've pinned. `tools/lib_functions_build.py` (`--build`/`--lint`) → `build/lib-functions.json`
+ `docs/lib-functions-reference.md`; a `run_script_inline` pass renames each function (skip if already
`USER_DEFINED`). Grows one `[[func]]` at a time as we identify more — the durable home for "this worker
is a known library function." **Small by design** (a handful to low-dozens), not a comprehensive sweep.

## Validation

- Task 1: report the newly-named count; spot-check 2–3 (e.g. does `FUN_0047f280` now carry a
  `std::map`-family name? does a known `operator new` site read named?).
- Task 2 (if taken): decompile `gfx_op_0x215_register_query` — its `gfx_registry_map_find(...)` /
  `FUN_0047f280` call should read as the curated `std_map_find`; `--lint` clean; 0 clobbers.
- `save_program` succeeds.

## Scope & boundaries

- **In:** run Function ID + apply; (conditionally) a small curated registry of known library functions.
- **Out:** generating a custom VC9/MSVCR90 FidDb (heavy, low marginal return); BSim; comprehensively
  naming all 3,660 (we take what Function ID gives + what we've RE'd, not a big-bang).
- **Regenerable:** `build/lib-functions.json` + `docs/lib-functions-reference.md` are generated.

## Acceptance criteria

- Task 1 recon run; before/after default-function counts recorded; gate verdict explicit.
- If STRONG: labels applied, saved, count documented. If WEAK: `vm-map/lib-functions.toml` seeded with the
  known hot functions, built (`--lint` clean), applied to `/v2` (0 clobbers), saved.
- ≥2 validation spot-checks read as named library calls.
- `docs/engine-re.md` records the outcome; `tools-reference.md` + CLAUDE.md maps updated iff a registry
  was built; status memory records the milestone (and marks lever #2 done, #3 next).

## Outcome (2026-07-09) — PIVOTED

Task 1 recon: Function ID = **WEAK** (0 new names; bundled FidDbs don't cover the VC9 runtime). Also
discovered the library functions we actually touch are already `USER_DEFINED` (annotate-as-you-go). So
the curated CRT/STL registry (Task 2 as specced) would have renamed already-named functions — near-zero
value. **Pivoted** (user-approved) to naming the *hot documented-but-unnamed helpers* the recon surfaced
by call-count: `vm_operand_fetch`@`0x41b940` (1021 refs), `vm_operand_write`@`0x425fb0`,
`vm_operand_lvalue`@`0x415f30`, `__security_check_cookie`@`0x54f981` (692), `operator_new`@`0x5502be`
(533) — ~2,400 call sites de-noised. **No `lib-functions.toml`/builder built** (the Ghidra name is the
record for these; no tool consumes them), so CLAUDE.md/tools-reference were not touched. Full account:
`docs/engine-re.md` "Hot-helper naming pass".

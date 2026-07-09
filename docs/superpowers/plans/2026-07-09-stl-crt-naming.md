# STL/CRT Naming — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** De-noise the library layer of the `/v2` image — take whatever Ghidra Function ID names for free, then curate the specific CRT/STL functions we've already RE'd so handlers stop burying real logic in unnamed `FUN_` calls.

**Architecture:** Recon-gated. Task 1 forces a Function ID pass and measures; if it names a meaningful number, that's the win. If weak (expected), Task 2 builds a small canonical `vm-map/lib-functions.toml` (same pattern as `engine-ctx.toml`) of known library functions and applies it. Deliberately light — no custom FidDb generation.

**Tech Stack:** ghidra-mcp `run_analysis` + `run_script_inline` (Java; `GHIDRA_MCP_ALLOW_SCRIPTS=1`); Python 3.11 (`tomllib`) for the registry builder.

**Spec:** `docs/superpowers/specs/2026-07-09-stl-crt-naming-design.md`.

## Global Constraints

- Confirm the active program is `/v2/range_00400000.bin` (base `0x400000`, ~4415 fns) before any script — the two-program gotcha. End image mutation with `save_program`.
- **Never clobber a `USER_DEFINED` symbol** (our handler/worker names). Function ID applies `ANALYSIS`-source labels (won't clobber); the curated rename skips any `USER_DEFINED`.
- Baseline (measured this session): **3,660 of 4,415 functions default-named** (`FUN_`/`LAB_`).
- `build/` is disposable/gitignored; never hand-edit generated files.
- Keep it light: **no custom VC9/MSVCR90 FidDb generation** (out of scope).

---

## Task 1: Function ID recon (THE GATE)

**Files:** none in-repo — driven via MCP.

- [ ] **Step 1: Confirm program + record baseline**

MCP `get_current_program_info` → assert base `0x400000`. Then a counting `run_script_inline` (Java): iterate functions, count `default (FUN_/LAB_)` vs `user`. Record the baseline (expected ~3660 default).
Expected: baseline default count confirmed.

- [ ] **Step 2: Force a Function ID pass with "Always Apply FID Labels"**

`run_script_inline` (Java): enable the option, then re-run analysis so Function ID re-evaluates:
```java
import ghidra.app.plugin.core.analysis.AutoAnalysisManager;
ghidra.framework.options.Options opts = currentProgram.getOptions("Analyzers");
opts.setBoolean("Function ID.Always Apply FID Labels", true);
AutoAnalysisManager mgr = AutoAnalysisManager.getAnalysisManager(currentProgram);
int tx = currentProgram.startTransaction("re-run Function ID");
try { mgr.reAnalyzeAll(currentProgram.getMemory()); mgr.startAnalysis(monitor); }
finally { currentProgram.endTransaction(tx, true); }
println("re-analysis triggered");
```
If `reAnalyzeAll`/`startAnalysis` is impractical here (timeout / no monitor), fall back to MCP `run_analysis` after setting the option. Either way, allow it to finish.
Expected: analysis completes (may take a minute).

- [ ] **Step 3: Measure the delta + gate**

Re-run the Step 1 counting script. Compute `newlyNamed = baselineDefault - afterDefault`. Spot-check: does `FUN_0047f280` (our `std::map::find`) or any obvious CRT function now carry a library name (`get_function_by_address`)?
- **GATE — STRONG (`newlyNamed >= 100`):** Function ID is the win. `save_program`. Skip Task 2; go to Task 3 (document the count).
- **GATE — WEAK (`newlyNamed` small / ~0):** the bundled FidDbs don't cover this runtime. Revert the option if desired, and proceed to Task 2. Record the weak result.
Expected: an explicit STRONG/WEAK verdict with the number.

- [ ] **Step 4: If STRONG, save + commit note**

`save_program`. (No repo files change; the doc commit is Task 3.) If WEAK, nothing to save from this task.

---

## Task 2: Curated library-function registry (ONLY if Task 1 is WEAK)

**Files:**
- Create: `vm-map/lib-functions.toml` (canonical source)
- Create: `tools/lib_functions_build.py` (model + lint + emit; mirrors `engine_ctx_build.py`)
- Create: `tools/test_lib_functions.py` (unit tests)
- Create: `build/lib-functions.json`, `docs/lib-functions-reference.md` (generated)

**Interfaces:**
- `load(toml_text) -> {"funcs":[{"address":int,"name","note"}...]}`; `lint(model) -> list[str]` (dup address, dup name, bad name chars); `emit_json(model) -> {hex_addr: name}`.

- [ ] **Step 1: Seed `vm-map/lib-functions.toml` with the known library functions**

Include the library functions we've identified in RE (grep `engine-re.md` for `std::map`, `FUN_0047f280`, `FUN_0042cf70`, hash insert/find, `operator new`, etc.). Start conservative — only addresses we're confident about:
```toml
# vm-map/lib-functions.toml -- CANONICAL registry of identified statically-linked library functions.
# Generated: build/lib-functions.json + docs/lib-functions-reference.md via tools/lib_functions_build.py --build.
# Applied to /v2 via run_script_inline (rename; never clobbers USER_DEFINED). Grows as we identify more.
[[func]]
address = 0x47f280
name = "std_map_find"
note = "std::map::find over the gfx object registry (op 0x215 handler calls it; returns value or 0xffffffff)"
source = "native-RE"
confidence = "high"
[[func]]
address = 0x42cf70
name = "gfx_registry_hash_insert"
note = "open-addressing hash insert into the op-0x215 query registry (op 0x1a2 handler calls it)"
source = "native-RE"
confidence = "high"
```
*(Add more only where confident — verify each address in `engine-re.md`/the image first. A small seed is fine; the registry's value is that it grows.)*

- [ ] **Step 2: Write failing tests `tools/test_lib_functions.py`**

Mirror `test_engine_ctx.py`: a `load`/`emit_json` shape test and a `lint` dup-address + dup-name test (plain runner, no pytest). Run → FAIL (`ModuleNotFoundError: lib_functions_build`).

- [ ] **Step 3: Implement `tools/lib_functions_build.py`**

Mirror `engine_ctx_build.py`: `load` (tomllib → `{"funcs":[...]}`), `lint` (duplicate address, duplicate name, name not `[A-Za-z0-9_]+`), `emit_json` (`{hex_addr: name}`), `emit_reference_md` (address/name/note table), `--build`/`--lint` CLI writing `build/lib-functions.json` + `docs/lib-functions-reference.md`. Run tests → PASS.

- [ ] **Step 4: Build + lint**

Run: `py -3.11 -X utf8 tools/lib_functions_build.py --build`
Expected: `built N funcs -> build/lib-functions.json + docs/lib-functions-reference.md`, lint clean.

- [ ] **Step 5: Apply to `/v2` (rename, skip USER_DEFINED)**

`run_script_inline` (Java): read `build/lib-functions.json`; for each `addr → name`, get the function; if its symbol is `USER_DEFINED` skip (already named), else `rename` to `name` (`SourceType.USER_DEFINED`). Count renamed/skipped. One transaction; `save_program`.
Expected: N renamed (or skipped if we'd already named them), 0 clobbers.

- [ ] **Step 6: Validate + commit**

`decompile_function 0x42a0b0` (`gfx_op_0x215_register_query`) — its registry-find call should read `std_map_find(...)`.
```bash
git add vm-map/lib-functions.toml tools/lib_functions_build.py tools/test_lib_functions.py docs/lib-functions-reference.md
git commit -m "re: curated library-function registry (lib-functions.toml) + apply"
```

---

## Task 3: Docs + memory + close

**Files:**
- Modify: `docs/engine-re.md` (record the outcome — Function ID result, and/or the curated registry)
- Modify (iff Task 2 ran): `CLAUDE.md` (canonical map + single-source table: `lib-functions.toml` row), `docs/tools-reference.md` (`lib_functions_build.py`)
- Modify: `~/.claude/…/memory/himegari-port-status.md` (milestone; lever #2 done, #3 next)

- [ ] **Step 1: Update engine-re.md**

Add a short note: Function ID recon result (STRONG count / WEAK), and — if built — the curated `lib-functions.toml` registry as the home for identified library functions. Link `docs/lib-functions-reference.md` if it exists.

- [ ] **Step 2: Update CLAUDE.md + tools-reference.md (iff a registry was built)**

CLAUDE.md canonical map row: `| Identified library (CRT/STL) functions | age-reimpl/vm-map/lib-functions.toml (generated → build/lib-functions.json, docs/lib-functions-reference.md) |`. Single-source table row + `engine_ctx`-style update trigger. tools-reference.md: `lib_functions_build.py` row.

- [ ] **Step 3: Update the status memory**

Record: STL/CRT naming DONE — Function ID result (N named or weak); curated registry seeded with M funcs (if built); lever #2 of 3 complete, #3 (differential oracle) next.

- [ ] **Step 4: Commit docs**

```bash
git add docs/engine-re.md docs/tools-reference.md
git commit -m "re: record STL/CRT naming outcome (Function ID + curated registry)"
```

---

## Self-Review

**Spec coverage:** recon gate → Task 1 (explicit STRONG/WEAK); curated fallback → Task 2 (conditional); no custom FidDb → Global Constraints + Task 2 note; validation spot-checks → Task 1 Step 3 + Task 2 Step 6; docs/memory → Task 3. Covered.

**Placeholder scan:** Task 2 Steps 2–3 say "mirror `engine_ctx_build.py`/`test_engine_ctx.py`" rather than repeating the full code — justified: those files exist in-repo as the exact template (load/lint/emit + plain-runner tests), and the shapes are specified in the Interfaces block. The toml seed is concrete. Task 1 Step 2 has a fallback path stated (run_analysis) for the uncertain re-analysis API. No hidden TODOs.

**Type consistency:** `load` → `{"funcs":[{address,name,note}]}` consumed by `lint`/`emit_json`; `emit_json` → `{hex_addr:name}` consumed by the Task 2 Step 5 Ghidra rename parser. Matches the `engine_ctx_build.py` contract it mirrors.

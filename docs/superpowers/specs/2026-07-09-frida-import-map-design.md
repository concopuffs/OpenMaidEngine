# Frida import-map — naming dynamically-resolved Win32 APIs — design

**Date:** 2026-07-09
**Status:** approved (design), plan pending
**Replaces:** the abandoned pe-sieve Task B in
`docs/superpowers/plans/2026-07-09-re-tooling-handler-labels-and-iat.md`.
**Home in the canonical map:** results + the mapping mechanism land in `docs/engine-re.md`
(the runbook already carries the design sketch under "IAT reconstruction — tried, DOESN'T WORK →
the right approach"); the tools go in `docs/tools-reference.md`. This spec is the design record.

## Motivation

AGE.EXE ships with a **zeroed IAT** resolved via `GetProcAddress` at load (confirmed: `pack_check.py`,
and pe-sieve's `/imp` produced ~17 genuine + 300+ spurious entries — see the `engine-re.md` runbook).
So the game's hot Win32 APIs (`ReadFile`/`CreateFileA`/`SetFilePointer`/`timeGetTime`/d3d9 device
methods) show in Ghidra only as indirect calls through unnamed pointers, and we have been identifying
them by format-string and behaviour archaeology. This slice names them at their call sites in one pass —
the same high-leverage move as the Task A dispatch-handler labeling, for the Win32 surface.

## Approach (decided in brainstorming)

**Attach + scan, recon-first**, applying **clean labels** (no type archive):
- Build `{runtime_addr → "dll!Func"}` from the **live** process's loaded-module export tables.
- Scan the `0x400000` module's memory for aligned DWORDs whose value is in that set → those locations
  hold resolved import pointers → record **`RVA → name`**.
- Apply `imp_<dll>_<func>` **labels** to the `/v2` Ghidra image at `0x400000 + RVA`.

**Why ASLR-safe:** the export map and the module scan are done in the *same* live process, so the stored
pointer values and the export addresses are from one consistent address space. We carry back only the
**RVA** (offset into the fixed-base `0x400000` main module, which is not relocated), so the labels
computed live apply to the earlier dump — the code/table *locations* are identical across launches even
though the stored pointer *values* differ.

**Why read-only / anti-tamper-safe:** only Frida `enumerateModules`/`enumerateExports` + memory reads —
the same plain-JS read pattern the existing capture scripts use. No spawn, no `GetProcAddress` hook, no
patching. (This is *not* the "live-debugger" case the user gated behind "probe carefully first.")

## The one unknown, and the recon gate

We do **not** yet know *where* the packer stores the resolved pointers. pe-sieve found genuine import
thunks at RVA `~0x202xxx` **inside the module** (`in_main:1`), which suggests a module-resident,
RVA-stable pointer table — exactly what this approach needs. But whether the `GetProcAddress`-resolved
hot APIs also land in-module, or in a heap block (non-stable base, **not** in the `/v2` dump), is unproven.

**Recon is a HARD GATE.** Task 1 is read-only and only *reports*. It must show the matches cluster in a
**module-resident** region within the dump's range (RVA `< 0x260000`, the dump size) before any labeler
is built. If the pointers turn out to be heap-resident, we **stop and report that honestly** — the slice
cannot label the dump and would need a different mechanism (e.g. a heap dump keyed to the same launch).
No forcing.

## Components

### 1. `tools/frida/map_imports.py` — export map + module scan (Tasks 1 & 2)

- Attach by PID (default: locate `AGE.EXE`, like `dump_engine.py`).
- Frida JS: `Process.enumerateModules()` → `Module.enumerateExports(m.name)` → `{addr → "dll!Func"}`.
  Handle collisions/forwarders (multiple names per addr) by keeping a canonical name + noting alternates.
- Read the `0x400000` module range; scan 4-byte-aligned DWORDs; collect `(RVA, value, name)` for values
  in the export set.
- **`--recon` (Task 1, report only):** print total exports mapped, total matches, and the **clustering**:
  contiguous aligned runs (candidate import tables) vs isolated singletons; min/max RVA of matches and how
  many fall within the dump range (`< 0x260000`). No file written. This is the gate.
- **default (Task 2):** write `build/import-map.json` = `{ "0xRVA": "dll!Func", ... }` restricted to matches
  inside identified contiguous table region(s); isolated singletons written to a separate
  `build/import-map-singletons.json` for review (not auto-applied — a lone match is more likely coincidental).

### 2. Ghidra labeler (Task 3) — `run_script_inline` Java (reuse the Task A pattern)

- Confirm the active program is `/v2/range_00400000.bin` (base `0x400000`, 4308 fns — the two-program
  gotcha from Task A).
- Read `build/import-map.json`; for each `RVA → name`: `createLabel(toAddr(0x400000+RVA),
  "imp_" + sanitize(dll) + "_" + func, SourceType.USER_DEFINED)`. Skip if a `USER_DEFINED` label already
  exists there (idempotent, no clobber). One transaction; `save_program`.

## Data flow

live process ─▶ export map `{addr→name}` + module DWORD scan ─▶ `RVA→name` (`build/import-map.json`)
─▶ Ghidra `createLabel` on `/v2` ─▶ call sites read `CALL dword ptr [imp_kernel32_ReadFile]`.

## Validation (Task 4)

Cross-check against APIs already hand-identified, so the mapping is proven, not assumed:
- The `call-script` resolver chain (`FUN_0044f390` / `FUN_0040e980`) should now show `CreateFileA` /
  `SetFilePointer` / `ReadFile` at its file-I/O call sites.
- The `sleep` timer source (`sleep_op_0xc8` → `DAT_0056f3d4`) should resolve to a `timeGetTime`/`GetTickCount`-class import.
- A `d3d9` `Present`/`Direct3DCreate9` site should be named.

If those light up correctly, the mapping is real. Record the count of labels applied and the validated
anchors. Then update `docs/engine-re.md` (mechanism + result), `docs/tools-reference.md` (the new tool),
and the status memory.

## Scope & boundaries

- **In:** all loaded modules' exports (no curation); module-resident import pointers; clean labels.
- **Out (explicitly):** typed Win32 signatures / the type archive (labels only this pass — signatures are a
  later layer); heap-resident pointer tables (out of reach of the dump — recon decides if this blocks us);
  the `GetProcAddress`-hook/spawn variant (rejected: fragile, needs spawn); renaming the DLL functions
  themselves (we label the *pointer slots*, not the targets, which aren't in the dump).
- **Regenerable:** `build/import-map.json` regenerates from a live run; disposable per project convention.

## Acceptance criteria

- Recon (Task 1) reports match clustering and the in-module/in-range verdict; **the gate is passed
  explicitly** (matches are module-resident, within the dump) before Task 2/3 run — or the slice stops
  here with a documented finding.
- `build/import-map.json` produced with a sane count (dozens+ of real imports, not ~17 and not 300+ noise).
- Ghidra labeler applies labels with 0 clobbers of existing `USER_DEFINED` symbols; `save_program` succeeds.
- ≥2 of the three validation anchors (resolver-chain file I/O, sleep timer, d3d9) read as named calls.
- Docs + memory updated.

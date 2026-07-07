# Name resolution — recovering what the compiler stripped

The disassembler reads the SYS4 bytecode's **operations and control flow** cleanly (see any
`build/disasm/*.asm`). What it can't show is the two kinds of *names* the AGE compiler
discarded: **which function a call targets** (#1) and **what a global variable means** (#2).
Both are data-labeling problems, not decoding problems. This note records what each is, what
we found, and how tractable it is.

Motivating example: `RECOVER.BIN` translates to correct pseudocode today, but reads as
`call-script 0x329d` (#1) and `C[unit][s] = E[unit][s]` over raw addresses (#2). Naming those
would make it read like source.

---

## #1 — `call-script` target resolution (naming the call graph)

**What it is.** `call-script N` (Kelebek opcode 0x03) carries a bare number — `0x329d`,
`0x2ade` — the id of an engine entry point. To render `call RECOVER` instead of
`call-script 0x329d` you need a table `id → (script, entry)`.

**Findings (inspected 2026-07-06):**
- `SYSTEM4.BIN` is **not** an index — it's a small SYS4 script (375 instrs) titled
  "SYSTEM4 INIT", the engine boot/init routine (ADV mode, fonts, error text).
- `SYS4INI.BIN` (`S4IC422`) is the **ALF asset index** — archive filenames for extraction
  (`SYSTEM4.BIN`, `M002.OGG`, `EV049A.AGF`…), not a script-call registry.
- The ids are large and sparse (`0x329d` = 12,957 ≫ 481 scripts), so the number is an index
  into a global **entry-point registry** the engine builds, not a script-file index.
- Even Kelebek's reference decompiler leaves these numeric (its comment only says "param =
  SYSTEM4.bin index"). So this is genuinely **unresolved upstream**, not merely unfinished.

**Why it's engine-level (harder than a file lookup).** There is no `id → name` table sitting
on disk to read. Resolving it needs one of:
- ~~Decode `SCJUMP.BIN`~~ **RULED OUT as the registry (recon 2026-07-06).** `SCJUMP.BIN` (29,796
  instrs) is a **progression state machine**, not an id→code table: it switches on `global 0x3234`
  (mode 1–9) then nested `eq`/`ne`/`and`/`jcc` on flags, ending in `mov`s to output globals. It
  decides *what comes next* via state; it barely uses `call-script`. Useful for game-flow logic, not
  for resolving `call-script` ids. So the id→code registry is genuinely engine-level.
- **Watch the engine resolve one (Frida)** — breakpoint the `call-script` handler in the running
  game, log `id → resolved address/script`. Ground truth; Phase-3 (live-tools) work.
- **Find the registration path** — if a boot script assigns ids to entry points, extract it
  statically (SYSTEM4.BIN is far too small to hold ~13k, so it's cumulative or lives in AGE.EXE).

**Status: deferred.** Not the quick win first assumed. Belongs with the engine/dispatch work
(Phase 3), or a dedicated `SCJUMP.BIN` reverse. Until then `call-script` stays numeric.

---

## #2 — The global-variable map (naming the data)

**What it is.** The VM has one flat **global memory bank**; the bytecode addresses it by raw
offset (`global-int 0x152616`, `global-int 0x52383`). Each offset is a specific piece of game
state (a unit's HP, the current-unit index, a stat table). The map we want is
`offset → (name, type, structure)`.

**Why it's opaque.** No symbol table exists anywhere; meaning lives in how AGE.EXE and the
scripts *use* each global. Nothing declares "0x152616 is the current unit."

**Why a big chunk is recoverable statically (the tractable one).** Unlike #1, #2 has strong
free handholds — several of which we've already built:

1. **The `*INIT` scripts are the writers, and we already extracted them.** `EBINIT`/`ITINIT`/
   `SKINIT`/`CGINIT`/`MPINIT` populate global arrays with names and data (`build/data/*.json`).
   The base address `EBINIT` writes 277 unit names into *is* the unit-name table. Each JSON's
   `name_array_base`, `desc_array_bases`, and `field_columns` are literally global addresses we
   can label by which table wrote them.
2. **Strings anchor the string side for free.** `set-string` writes skill names to
   `global-string 0x23a3…` → that array is the skill-name table. `*MES` tables likewise.
3. **Access shape reveals structure without names.** A global read as `base[unit*stride + col]`
   exposes a per-unit record and its width (RECOVER showed 14-, 3-, 30-column tables). A global
   used as the loop-invariant row index everywhere (`0x152616`) is a "current X" pointer.
   Constants-compared → mode/flag; only-incremented → counter.
4. **Frida for the ambiguous ones (heavy, ground truth).** Do a known action in-game (take
   damage, gain a level), watch which global changes → definitive labels. Reserve for leftovers.

**Feasibility.** A *partial* map — enough to make most gameplay scripts readable — is achievable
now, statically, from methods 1–3. A *complete* map needs Frida for the tail. It's incremental:
label the ~dozen hottest globals first (biggest readability payoff), grow the rest on demand.

**Partial map — BUILT (v1, 2026-07-06).** `tools/global_map.py` → `build/global-var-map.json`
(all evidence) + `build/global-var-map.md` (labelled subset). It ingests `build/data/*.json`
(name/desc/field bases), scans the 481-script corpus for each global's **access shape**
(2D-table base + stride, 1D-array base, row-index, scalar), and ranks "current entity" index
pointers by purity. **First result: 16,354 of 49,435 distinct globals labelled** —

| kind | count | example |
|---|---|---|
| string tables (names/descs/messages) | 3,199 | `0x23a3` = skill-name table |
| per-entity data-field arrays (from *INIT) | 12,700 | dense = shared fields, `?` = sparse per-entity |
| row-major record tables (from access shape) | 122 | `0x52383` = record-table[stride 30] |
| 1D arrays | 307 | |
| index / "current entity" pointers | 26 | `0x152616` (purity 0.51), `0xeff75` (0.95) |

**Validated against `RECOVER`:** the map independently reproduces its hand-traced layout —
`0x4e11b`→stride 14, `0x52383`→stride 30, `0xaacb4`→1D array, `0x152616`→current-entity index.

**Wired into the disassembler.** `sys4load` annotates global operands with the map's high/medium
-confidence labels (low-confidence tail omitted for readability), e.g. RECOVER now renders
`lookup-array-2d p0 (global-int 0x4e11b =rec[s14]) (global-int 0x152616 =current-entity-index?) …`.
Labels are prefixed `=` to mark them as inferred aliases. Regenerate the `.asm` corpus with
`tools/extract_phase2.py` after refreshing the map. Turn it off by deleting/renaming
`build/global-var-map.json` (the loader degrades gracefully).

Confidence is marked per entry; labels ending `?` are low-confidence guesses.

### The curated registry — `vm-map/globals.toml` (2026-07-07)

The v1 auto map (`build/global-var-map.json`) infers *shapes* but cannot recover branch-flag
*meaning* — and is sometimes wrong (it labels `0xa57`, the Lily form-A story flag, as a
"string-table"). The curated registry fixes this, modelled exactly on `vm-map/opcodes.toml`:

- **`vm-map/globals.toml`** — the only hand-edited source. One `[[global]]` per known address:
  `name`, `category` (`story-flag`/`index-pointer`/`data-table`/`string-table`/`ui-toggle`/
  `choice-output`/`counter`/`unknown`), `type`, `value_domain`, `usage`, and provenance
  (`source`/`confidence`/`depends_on`).
- **`tools/globals_build.py --build`** merges curated entries *over* the auto map →
  `build/globals.json` (machine) + `docs/global-reference.md` (generated human view). `--lint`
  checks vocabulary, the auto-shape≠high rule, and dangling `depends_on`. `sys4load` reads
  `build/globals.json` for operand labels (curated names win, shown as `name(category)`; the auto
  tail is kept only at high/med confidence). Regenerate the `.asm` corpus with
  `tools/extract_phase2.py` to pick up new labels.

#### Story-state flags (the first populated category)

Story flags are scalar globals that ADV/progression logic *branches on* (chapter, character
forms, choices, routes) — a category the auto shape map never enumerated. **`tools/story_flags.py`**
is a 100% static miner: it flags a global as a candidate when it feeds a comparison (`eq`/`ne`/
`lt`/`lte`/`gr`/`gre`), a logical (`and`/`or`), or a `jcc` condition, and is not a genuine table/
index in the shape map. Per candidate it records compared-against constants (→ value domain), the
writer set (progression-written but scene-read = strong story flag), total- and scene-reach, and
near-universal (ADV-chrome) status → an auto category + confidence. Output:
`build/story-flags-candidates.json` (review surface: 1261 branch-read globals, **205 story-flag
candidates**); `--bootstrap` seeds high-signal skeletons (med-confidence, non-chrome) into
`globals.toml` for human naming. Dynamic confirmation of a flag's reach stays separate —
`Age.Cli sweep 0xADDR=VAL`.

**Reading the catalog:** `reach_scenes > 0` = the flag changes SC/SP scene dialogue directly (e.g.
`0xa57` Lily form, scene-reach 78). `reach_scenes = 0` with progression writers = a
progression/menu-layer flag read by the game-flow scripts, not scenes (e.g. `0x3234` chapter, read
by SCJUMP/FIELD). **Known/named anchors:** `0x3234` `chapter_mode` (enum 1..9), `0x3231`
`game_mode` (adjacent mode selector), `0xa57/8/9` Lily forms A/B/C (boolean, externally set),
`0x62ccf/0x62ccc` SCJUMP decision outputs, `0x6642c` `route_branch` (BUNKI = 分岐 writer),
`0x6c9–0x6cd` UI toggles. Config/settings globals written by `CONFIG`/`INITCONFIG` (scene-reach 0)
are *not* story flags — the miner over-tags them; they are recategorized `unknown` when curated.

### Future step — growing the map (planned, not yet done)

The v1 map labels *shapes and tables*; the next increments add *meaning*, cheapest first:

1. **Fold in the `*MES` message-table writers** (`ITMES`, `SKMES`, `VIMES`, …) and any other
   `set-string`/`copy-to-global` writers not covered by the `*INIT` set — pure static win,
   extends the string/data labels. (Also: most name-table bases are *read* rarely — reads
   likely go through `*MES`/an indirection; tracing that would connect names to their readers.)
2. **Label 2D record tables by their readers** — cross-reference which scripts read each
   `rec[sN]` table and infer purpose from context (e.g. RECOVER's 30-wide tables ↔ a
   status/recovery system). Static, medium effort.
3. **Name *which stat* each field is (Frida).** The one step needing live tools: change a
   known value in-game (take damage, gain XP), watch which global moves → definitive
   `field@X = "HP"`. Reserve for the fields that matter; this is the last mile.

Re-run `tools/global_map.py` after each increment; `sys4load` picks up the new labels
automatically (it reads `build/global-var-map.json` at load).

---

## How the two relate
#1 names **functions** (the call graph); #2 names **data** (game state). In `RECOVER`, #1 turns
`call-script 0x329d` into a name; #2 turns `C[unit][s] = E[unit][s]` into `unit.hp[s] =
unit.maxHp[s]`. Priority reversal from the first guess: **#2 is the tractable readability lever
now** (static handholds already half-built via the `*INIT` extraction); **#1 needs the engine**
(dispatch reverse or Frida) and is deferred.

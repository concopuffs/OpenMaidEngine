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

## #1 — `call-script` target resolution (naming the call graph) — ✅ SOLVED (2026-07-07)

**RESOLVED via native-RE.** `call-script <id>` is a **direct RAW index into the SYS4INI file table** —
the very asset index we already parsed. No hidden engine registry: SYS4INI *is* the registry. Cracked
by decompiling the handler chain in Ghidra (op 0x03 → `FUN_0041bc90` → loader `FUN_0040e980` →
resolver `FUN_0044f390`, which does `record = table_base + id*0x50` over the 80-byte SYS4INI records).
**Statically confirmed:** all 297/297 distinct corpus `call-script` ids resolve to a `.BIN` script with
a semantically-exact name (`0x1ab→ADDITEM`, `0x2ae7→MES`, `0x143→BUNKI`), 0 out-of-range. Full
mechanism in `engine-re.md` (“op 0x03 (call-script)…”). Tooling: `parse_sys4ini.py` →
`build/callscript-names.json` (id→name); `sys4load` renders `call-script 0x1ab =ADDITEM.BIN`; the
`build/disasm/*.asm` call graph now reads by name. The one caveat: index the RAW SYS4INI records
(*including* the 2 `@` placeholders) — `asset-index.json` carries each entry's `raw_index` (= the id)
for exactly this. **Runtime (VFS-A):** `Sys4AssetCatalog` now reads that raw table directly and
`Sys4ScriptProvider` opens the selected record through loose-first/bounded-ALF storage; generated JSON is
only the disassembler annotation and parity oracle. The VM executes the loaded target as a nested frame.
The original analysis (kept below for provenance) had concluded this was engine-level and deferred — it
was, and the Ghidra loop is what resolved it.

**What it is (original framing).** `call-script N` (Kelebek opcode 0x03) carries a bare number —
`0x329d`, `0x2ade` — the id of an engine entry point. To render `call RECOVER` instead of
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

**Status: ✅ SOLVED** (see the banner at the top of this section). It did belong with the
engine/dispatch work — the Ghidra + MCP loop resolved it via the opcode-dispatch table.

**Update (2026-07-07):** SCJUMP's *decision logic* is now decoded — `(chapter_mode, guards) →
decision value` — see `docs/scjump-progression.md` and `tools/scjump_decode.py`. That confirmed
SCJUMP is not the `call-script` registry (it produces a decision value, not a script id). Then the
Ghidra + MCP loop **cracked `call-script` itself** (the SOLVED banner above): via the opcode-dispatch
table it walked the handler → loader → resolver and found the id is a raw SYS4INI file index. What
remains of the earlier `decision→scene` question is now narrow: scenes are `SCxxxx.BIN` records loaded
through the *same* id-indexed loader, so the only open piece is where the SCJUMP decision *value*
becomes a scene *id* (a caller of SCJUMP). The `u00428010` guess for that hop was disproven via Ghidra
(it's a graphics command-buffer op; see `docs/engine-re.md`).

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
   `name_array_base`, `desc_array_bases`, `field_columns`, and `record_field_columns` are literally
   global addresses and access shapes we can label by which table wrote them.
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

**Partial map — BUILT (v1, refreshed 2026-07-22).** `tools/global_map.py` → `build/global-var-map.json`
(all evidence) + `build/global-var-map.md` (labelled subset). It ingests `build/data/*.json`
(name/desc/field bases), scans the 481-script corpus for each global's **access shape**
(2D-table base + stride, 1D-array base, row-index, scalar), and ranks "current entity" index
pointers by purity. **Current result: 4,960 of 41,611 distinct globals labelled** —

| kind | count | example |
|---|---|---|
| string tables (names/descs/messages) | 3,206 | `0x23a2` = skill-name lookup base |
| per-entity data-field arrays (from *INIT) | 1,353 | dense = shared fields, `?` = sparse per-entity |
| row-major record tables (from access shape) | 122 | `0x52383` = record-table[stride 30] |
| 1D arrays | 253 | |
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

### INIT field-semantics workflow and initial item/skill/unit mappings (2026-07-22)

The old name-mode extractor's boundary rule was wrong for sparse tables: it treated any increasing
`global-string` destination as another description. ITINIT begins with 101 consecutive name-only records,
so the generated JSON collapsed them into item zero and fabricated 67 description columns. Static consumer
evidence also proves the tables are one-based: scripts look up item names from `0x1bd2 + item_id`, while the
first populated name is written to `0x1bd3`. `extract_init.py` now infers the parallel-array record span from
the dominant name-to-description delta (SKINIT 300; ITINIT/EBINIT 1000), recognizes column-zero names inside
that span, emits the one-based runtime id, and distinguishes the lookup base from the first written cell.
Corrected counts are **131 skills, 287 items, and 277 units**. Name-mode INIT scripts also encode negative
constants as `sub destination, 0, magnitude`; the extractor now evaluates that static form as well as `mov`,
recovering 113 negative item cells, 212 negative skill cells, and 86 negative unit cells.

Semantic recovery is an evidence ladder, cheapest and strongest first:

1. Profile each write base across named records (population, value domain, common values and examples).
2. Mine every direct corpus consumer of that base and identify its role from the consuming operation/script.
3. Cross-resolve enums and foreign keys against other INIT/MES tables and visible descriptions.
4. Curate only supported names in `vm-map/globals.toml`; retain uncertainty in the profile rather than
   promoting guesses. Use dynamic observation only for fields that remain ambiguous after static consumers.

`tools/init_table_profile.py ITINIT --build` materializes steps 1–2 in
`build/data/ITINIT-field-profile.{json,md}`. The initial pass names thirteen parallel arrays: catalog sort
key, random-item tier, item category, icon id, shared ITMES handler id, attack and defense elements, weapon
class, granted skill id, minimum/maximum range, essence recovery, and an equipment sex mask. The strongest
joins are independently human-readable: attack/defense values index AFINIT's Japanese attribute strings,
granted-skill values resolve to SKINIT, all handler values resolve to ITMES.BIN, and every min/max-range
record says `range 2` in its item description.

The apparent per-record ITINIT field bases were a structural artifact, not hundreds of sparse arrays. For
each write, subtracting `item_id * stride` and comparing the destination with corpus-observed
`lookup-array-2d` consumers assigns all **877** writes (764 positive/direct writes plus 113 recovered negative
writes) unambiguously to six row-major tables and 44 populated columns:

| base | stride | populated writes | semantic role |
|---|---:|---:|---|
| `0x8e7b9` | 5 | 20 | character-id equipment whitelist |
| `0x906f9` | 30 | 47 | signed condition/drain deltas (positive inflicts, `-5` cures) |
| `0x97c29` | 30 | 11 | equipped/passive condition levels |
| `0x9f541` | 14 | 403 | signed additive equipment stat modifiers |
| `0xa2bf1` | 10 | 379 | per-stat tuning curve ids |
| `0xa5301` | 3 | 17 | HP/SP/FS recovery amounts |

`extract_init.py` now records these as `record_fields["base/stride/column"]` rather than inventing a
one-off `fields` base for every row. Applying the same rule exposes 18 linked SKINIT columns and 84 linked
EBINIT columns. This correction reduces the auto map's false INIT-field labels from 12,311 to 1,353; the raw
write addresses were valid, but their former ownership model and omission of negative writes were not.

The first SKINIT pass names the stable catalog and combat surface: sort key, seven-way category, icon and
SKMES handler, encoded minimum/maximum range, attack element, condition strengths, signed combat-stat deltas,
HP recovery/SP cost, proc chance, and battle-animation id. The negative-write fix is essential here: all 95
active-skill SP costs are stored as `0 - cost`, so the old JSON omitted the cost column entirely.

The first EBINIT pass names the unit schema shared by setup, menus, and combat: sort key, icon, sex category,
provisional species category, defense element, natural-attack and starting-equipment item ids, allowed weapon
item category, canonical variant id, four starting-skill slots, deployment cost, starting level, level cap,
fourteen-column base stats, and matching per-level stat-growth rates. These joins are structural rather than
positional guesses: item/skill ids resolve into ITINIT/SKINIT, SETEN/UNITECH/SALLY copy complete records into
runtime unit state, ADDEXP performs the growth-rate divide/modulo-100 calculation, and SALLY checks deployment
cost against the live party-capacity aggregate.

The follow-up pass resolves three more coherent sub-schemas. First, SYS4INI joins and decoded dimensions/
pixels identify six presentation tables: CP map sprite sheets, CA battle portraits, CB full-body battle
figures, CS status illustrations, CIC/CIN battle cut-ins, and a 30-slot OGG voice bank. Second, BTL exposes
base experience, eight item-drop ids, and their paired percentage rolls; INFOEN independently renders the
same drop-item ids. Third, explicit menu messages and state updates identify capture eligibility, enemy-info
listing, summon unlock indices/knowledge thresholds/point costs, essence yield, automatic enemy level
scaling, and the large-battle-sprite layout flag. The signed `unit_boss_class` is intentionally only
medium-confidence: every nonzero row is a boss, hazard, or special encounter and all consumers treat it as
such, but the positive/negative class distinction remains unknown. AI and the remaining sparse flags stay
unnamed until comparable consumer evidence exists.

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

### Future step — growing the map

The v1 map labels *shapes and tables*; the next increments add *meaning*, cheapest first:

1. **Continue INIT semantics by evidence density.** Resolve ITINIT/SKINIT's remaining stat and condition
   columns, then work through EBINIT's AI, route/evolution, and remaining sparse-flag tables by consumer
   strength. Preserve explicit item → skill and unit → attack/skill/equipment/drop joins. Do not infer
   meaning from column position alone.
2. **Fold in the `*MES` message-table writers** (`ITMES`, `SKMES`, `VIMES`, …) and any other
   `set-string`/`copy-to-global` writers not covered by the `*INIT` set — pure static win,
   extends the string/data labels. (Also: most name-table bases are *read* rarely — reads
   likely go through `*MES`/an indirection; tracing that would connect names to their readers.)
3. **Label 2D record tables by their readers** — cross-reference which scripts read each
   `rec[sN]` table and infer purpose from context (e.g. RECOVER's 30-wide tables ↔ a
   status/recovery system). Static, medium effort.
4. **Name *which stat* each field is (Frida).** The one step needing live tools: change a
   known value in-game (take damage, gain XP), watch which global moves → definitive
   `field@X = "HP"`. Reserve for the fields that matter; this is the last mile.

Re-run `tools/global_map.py` after each increment; `sys4load` picks up the new labels
automatically (it reads `build/global-var-map.json` at load).

---

## How the two relate
#1 names **functions** (the call graph); #2 names **data** (game state). In `RECOVER`, #1 turns
`call-script 0x329d` into `CALCREVISE.BIN`; #2 turns `C[unit][s] = E[unit][s]` into `unit.hp[s] =
unit.maxHp[s]`. Both are now largely in hand: **#1 is SOLVED** (the SYS4INI-index dispatch reverse —
turned out to need the engine, and the Ghidra loop delivered it), and **#2 has a partial static map**
(the `*INIT` handholds) that grows on demand.

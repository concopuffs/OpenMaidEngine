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

**Updates (2026-07-07 through 2026-07-23):** SCJUMP's *decision logic* is decoded as
`(chapter_mode, guards) → decision value`; `call-script` ids are raw SYS4INI file indices; and the
remaining join is now closed. SCINIT writes `G[0x87a57 + decision] = packed SCxxxx resource id`, which
SYSTEM4, FIELD, SALLY, and TRAIN consume. Its parallel `G[0x8a167 + decision]` column is authored
chapter metadata. The `u00428010` guess for this hop was disproven via Ghidra: that operation persists
a selected global cell and is unrelated to dispatch. See `docs/scjump-progression.md` for the full join.

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
one-off `fields` base for every row. Applying the same rule exposes 18 linked SKINIT columns and 82 linked
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
scaling, and the large-battle-sprite layout flag. The signed `unit_boss_class` remains medium-confidence as
an authoring vocabulary, but its runtime split is now concrete. Every nonzero value receives the shared
boss damage adjustment, condition immunity, targeting exclusions, and boss battle treatment. FIELD's
`stage_clear_rule == -2` path scans only living enemy units whose class is positive, so a positive class is
a required defeat-boss target while the matching negative class is a boss-treated add, decoy, or hazard
that does not delay victory. STINIT confirms the distinction in the same encounters: Bridget is positive
while Octavia and the boss orc are negative in stage 11; Deirdre is positive while her four shadows and
Laumakar are negative in stages 92/98; Tiamat is positive while the EX-8 boss roster is negative in stage
167; and the final heart is positive class 4 while its three organs are negative class 4. Absolute classes
1, 2, and 3 group named story characters, monster/special bosses, and demon-lord-class bosses respectively,
but the shipped scripts do not branch differently among those three values. Either sign of class 4 alone
selects the final-boss BGM and FIELD's special tactical-map presentation. AI and the remaining sparse flags
stay unnamed until comparable consumer evidence exists.

The roster/event follow-up resolves five more EBINIT tables through SALLY's complete action path. A
four-cell persistent-state block records recruitment/removal outcomes for seven heroines; a four-column
requirement table gates actions against the shared flag bank; and an eight-column event table feeds
`scjump_decision_out` before SCJUMP resolves the next script. A two-column unit-id table selects normal and
explicitly named brainwashed variants, while the final item-id field is passed to USEITEM under SALLY's
literal “sex magic bonus” message. This is roster and event routing data rather than enemy AI. The adjacent
`0x7843e` enum remains unnamed because no non-EBINIT script references it, directly or through a detected
table operation.

The same consumer trace closes the last unnamed item/skill combat-stat column. CALCBTPARAM adds stat column
7 (luck) and column 8 into a clamped percentage; CALCDMG compares it with `random-modulo 100` immediately
after the hit check and selects the critical-result state on success. Column 8 is therefore critical chance
for both `item_stat_modifiers` and `skill_combat_stat_deltas`; the skill descriptions and matching item
columns also confirm evasion, magic defense, and speed.

ITMES and SKMES are now joined back to their INIT records by a reusable id-dispatch extractor: all 287 item
ids and all 131 skill ids match exactly. `init_table_profile.py --message-query REGEX` puts the complete
player-facing description beside every populated field, which confirms the item/skill condition, resource,
range, combat-stat, and restriction mappings without relying on column position. The same CHMENU trace
identifies SKINIT `0xa70b2` as `skill_change_catalog_eligible`, distinguishes persistent
`skill_acquired_flags` from broader `skill_info_revealed_flags`, and the explicit ITMES “female-only” record
raises `item_sex_restriction_mask` to high confidence.

Confirmed row-column meanings are no longer prose-only. The relevant `globals.toml` entries carry a
machine-readable `columns` map; `globals_build.py` preserves it in `build/globals.json`, and
`extract_init.py` emits a top-level `field_semantics` mapping while retaining raw address/stride/column keys
as provenance. Generated profiles therefore render names such as
`item_stat_modifiers.critical_chance` and `skill_status_levels.paralysis` directly.

The same structured metadata now covers the confirmed EBINIT layouts. The 14-column base-stat and growth
records use the shared accuracy-through-max-FS vocabulary; starting skills, drop items/chances, normal versus
brainwashed roster forms, battle portraits/cut-ins, and health-selected status art all expose named fields.
Consumer control flow further divides the five CP sprite assets into normal/alternate compact and directional
sheets plus the special compact sheet, and SHOWGROW proves voice column 24 is the level-up reaction. Of
EBINIT's 108 genuine populated profile fields, 107 now have specific semantic names. SALLY's SO012 button
atlas and action dispatch resolve all four unlock columns as contract, brainwash, a reserved/unreachable
slot, and sex magic. The paired eight event columns are contract, brainwash, the same reserved slot, three
form-dependent Lily sex-magic events, sacrifice, and release. The reserved slot has a switch arm but is
deliberately skipped by both drawing and input; its event ids also lack SCJUMP mappings, so it is recorded
as unreachable rather than assigned a speculative action.

The voice bank's remaining 23 populated columns are also consumer-resolved. FIELD supplies warp, treasure-
capture, and objective-interaction call sites. CALCDMG establishes BTL's miss/hit/critical result and
actor/target ownership, allowing BTL's selectors to separate ordinary attack, critical, skill-use,
damage-reaction, defeated, and finishing-blow voices. The five populated slots that no shipped selector can
reach remain explicit `unused_slot_*` authoring fields rather than generic address fallbacks; the two battle-
adjacent unused slots duplicate the final normal/critical skill pair in all 116 populated rows.

The last broad EBINIT field, `0x7843e`, is an authoring-only seven-value power tier. Its 243 rows do not
partition by species, sex, defense element, or boss class, but values rise strongly with deployment cost,
essence yield, level cap, and base statistics. Lily's three forms are exactly tiers 2/4/6, and recurring
heroine boss definitions generally rise with their later, stronger appearances. The static corpus contains
no read of this array, and the `/v2` native image contains neither its global index as an instruction operand
nor as a little-endian constant. `unit_power_tier` is therefore curated at medium confidence as descriptive
authoring metadata, not a runtime behavior claim.

The apparent final two anonymous EBINIT writes were address-ownership collisions, not new fields. The flat
global range occupied by stride-300 runtime table `0x4e693` overlaps established EBINIT parallel arrays.
Address `0x6fd4e` can be expressed as row 456, column 91 of that table, but it is also exactly
`unit_battle_sprite_asset_id[456]`; its value 12585 resolves to `CB456A.AGF`. Likewise `0x7a5d6` can be
expressed as row 600, column 35, but is exactly `unit_starting_level[600] = 80`. The extractor now gives an
already-established parallel base precedence over a coincidental row-table range match. EBINIT therefore
has 82 genuine linked columns and specific semantic names for all 108 populated profile fields.

### STINIT mixed stage records (2026-07-23)

STINIT is not a name table. Its preamble allocates 29 fixed global work buffers, then 74 sparse branches
compare `scjump_progress_a` with stage ids 1 through 170. Each selected branch populates the same current-
stage buffer with four strings, six scalar globals, sparse cells inside the fixed buffers, and
length-prefixed arrays copied from the script footer. `extract_init.py` now detects this shape as `mixed`,
evaluates preamble length arithmetic, attaches writes to their containing buffer, and preserves the branch
offset and footer offset as provenance. The extraction accounts for all 296 string writes and all 1,396
`copy-local-array` operations. Six buffers also inherit exact strides from independent
`lookup-array-2d` consumers.

`init_table_profile.py STINIT --build` profiles the four string slots, six scalars, 932 distinct buffer
cell destinations, and 37 footer-array destinations across the 74 records. The record label falls back to
the first nonempty victory-condition string, making consumer/value correlations readable without inventing
a stage-name field.

The strongest header meanings are curated in `globals.toml`: `0x27b9..0x27bc` are the two victory and two
defeat-condition lines rendered by AIM/FIELD; `0xe7302` is passed by FIELD to `play-bgm`; `0xe730c` is the
turn limit displayed by DRAWCHP and checked by FIELD; and `0xe730d` selects defeat versus forced-retreat
clear when that limit expires. STAGECLEAR establishes `0xe7303` as the target/par turn count and scales
`0xe7304`'s persistent reward increment by performance against that target. FIELD establishes `0xe730b` as
the gate that disables its already-cleared-stage retreat/replay conversion.

The first map/object pass resolves seven more buffer families. FIELD loads `0xe7311[1..19]` into tiled
surface slots and DRAWMAP selects those surfaces through terrain metadata, proving it is the current stage's
map-texture override list: positive values are SYS4INI resource ids, zero disables a slot, and -1 selects
the shared fallback. DRAWOBJ converts `0xe7325` and `0xe7357` to map-space coordinates, while
SETOBJ/DRAWOBJ/FIELD use `0xe7389` to index shared object definitions. They are object tile X, tile Y, and
type id. SETOBJ tests the `{3,4,7}` masks in `0xe7483` against GAMESTART's three-way `difficulty_index`,
then applies seven required and five forbidden one-based ids from `0xe74b5`/`0xe7613` against the shared
`story_event_flags` bank. FIELD's turn loop establishes `0xe741f` and `0xe7451` as each object's
reinforcement interval and spawn limit. Type 27 uses the same pair for a one-shot special spawn.

The intervening `0xe73bb`/`0xe73ed` pair is deliberately not assigned one global name: FIELD dispatches it
by `stage_object_type_id`, making it a tagged payload. The generated join decodes only consumer-proven
variants:

- types 1--4: `initial_faction_id` in the first cell;
- types 6 and 36: teleport `destination_tile_x` / `destination_tile_y`;
- types 7 and 8: treasure `item_id` / `item_quantity`, passed to ADDITEM;
- type 28: `card_generation_list_id`, passed to CDINIT.
- types 18--25: `non_triggering_faction_id` in the first cell when populated. FIELD suppresses the
  hazard/barrier interaction when the entering unit's faction equals this value.

This accounts for 220 initial-owner values, 229 teleport destinations, 626 treasure pairs, and 246 card
list ids. The faction-gate branch resolves another 104 cells on populated types 18--21 and 25. Type 17
(`針`, spikes) is explicitly outside FIELD's faction comparison, so it does not borrow the neighboring
hazard meaning.

A separate initialization/render path resolves the remaining state-row payloads. On a fresh stage, FIELD
copies the first tagged payload into `stage_object_runtime_state[stage][slot]` only when the object's
OBINIT `object_sprite_state_row_mode` equals 1. DRAWOBJ applies the same mode check and multiplies that
runtime state by the object's sprite height to select its vertical source row. The join therefore exposes
78 cells as `initial_object_state_id`: one door (type 11), 16 spikes (type 17), and 61 deployment flags
(type 26, `出撃の旗`). All deployment-flag values are 2, and all 63 enemies linked to those flags are also
faction 2, consistent with OBINIT's `敵の増援地点` description; because the spawn branch accepts type 26
without comparing those values, the field remains the directly proven object state rather than a guessed
faction id.

The final three populated tagged cells are understood as engine-dead authoring data rather than a hidden
type-27 (`異界の門`) parameter. STINIT explicitly writes `2` to the first payload cell for object slot 22
in stages 52--54. Type 27 has no OBINIT state-row mode, so FIELD's fresh-stage initializer does not copy
that value to runtime state; the generic interaction dispatch also has no type-27 branch. Its dedicated
turn path reads only the object's active flag, type, reinforcement interval/limit, and coordinates, then
forces ADDEN enemy slot 0. ADDEN hardcodes EBINIT unit 465 (`漂着した異界の姫/BOSS`) into runtime entity
slot 49; on success FIELD changes to BGM 10 and reports `異界の姫が漂着した！`. Neither `0xe73bb` nor
`0xe73ed` participates. The join therefore preserves the three explicit writes under
`ignored_payload_fields` instead of inventing a semantic name or leaving them unresolved.

OBINIT is the authoritative object-definition table: 46 one-based records provide the type names, and 34
provide short player-facing effect descriptions used by the field object-information path. The STINIT join
now adds `type_name` to every placement and `type_description` when populated while retaining `type_id`;
top-level `object_definition_table: "OBINIT"` records the join provenance. This is intentionally separate
from payload decoding: a known display label does not by itself establish the meaning of a tagged cell.

The enemy pass follows the separate 30-cell family through FIELD, SETEN, ADDEN, MVRTN, and BTRTN. Slot
zero is reserved for ADDEN's synthesized special-unit path; the stage table populates slots 1 through 29.
`0xe7811` selects the EBINIT unit, `0xe7799` is its faction, `0xe773f`/`0xe775d` are direct tile
coordinates, and `0xe777b` optionally anchors the unit to a stage-object slot. FIELD checks the
three-bit difficulty mask in `0xe77b7`, uses `0xe77f3` as a weighted-random alternative value, and applies
the seven required plus five forbidden story flags in `0xe793d`/`0xe7a0f`. SETEN proves `0xe782f`,
`0xe784d`, and `0xe786b` are the scenario level floor, cap, and party-level scaling divisor. Finally,
the three-value footer rows in `0xe7889` and optional `0xe78e3` become difficulty-specific movement and
battle routine-set ids selected by MVRTN/BTRTN. The final `0xe77d5` gate is also resolved:
STAGECLEAR writes `stage_clear_state[current_stage] = 1`, and FIELD suppresses a spawn when that state is
set and the spawn's cell equals 2. The joined view exposes all 485 populated cases as
`first_clear_only: true`.

Generated INIT records now retain their raw `fields`/`record_fields`/buffer keys and additionally expose a
flat `semantic_fields` projection joined through the top-level `field_semantics` map. For STINIT, the four
confirmed parallel buffers plus both prerequisite tables are also assembled into 2,312
`object_placements` across 66 stages. Each placement contains its slot, numeric type plus OBINIT
name/available description, tile coordinates, difficulty mask, populated positive/negative story
prerequisites, optional reinforcement schedule, and the decoded type-tagged payload variants above. The
three engine-dead type-27 payload writes stay attached under `ignored_payload_fields`; there are no
remaining populated object payloads under `unknown_fields`, so this convenience view loses no evidence or
invents names.
The same records now contain 1,378 joined `enemy_spawns` across 66 stages, with unit/faction, direct or
object-linked placement data when present, difficulty and story gates, level rules, random-selection
weight, movement/battle routine rows, and `first_clear_only` replay gating. Raw footer metadata stays in
`footer_arrays`, while its `semantic_fields` value is the copied row itself.

### CCINIT conditional class-change rules (2026-07-23)

CCINIT is not a parallel INIT database. It is a source-ordered program of 71 conditional promotion rules
covering 33 EBINIT unit ids. Each rule tests `current_unit_id`, normally requires the unit's current level
to meet a threshold, and requires one of ten persistent class-change slots to be clear. The rule then
selects a level/title, adds a deployment-cost delta and fourteen-column stat bonuses, optionally awards
SKINIT skills, and sets the applied slot. Sixty-nine rules award named titles; the two empty-title,
level-independent rules are Lily's girl/adult form adjustments and are also queried directly by EVOLVE
when it previews the next form's movement.

CALCCC establishes the surrounding protocol. It clears the selection, cost, stat, and skill outputs,
copies the current unit's ten persistent state cells into a working buffer, then call-scripts up to 32
providers from `class_change_rule_script_ids`. Eligible rules retain the highest selected level. CALCCC
copies the successful title to `unit_class_titles`, adds the cost and stat outputs to persistent unit
state with stat caps, installs positive skill awards into the first three skill slots, persists the
updated applied-state row, and reveals the awarded skills. ADDEXP calls CALCCC after level growth and
uses the same outputs to construct title, cost, and learned/replaced-skill notifications.

`extract_init.py` detects this fifth shape as `rules` and writes `build/data/CCINIT.json`. Each source-order
record keeps its bytecode guard offset and raw output keys alongside `unit_id`/EBINIT `unit_name`,
`minimum_level`, zero-based `class_change_slot_index`, `title`, named `stat_bonuses`,
`deployment_cost_delta`, joined one-based `skill_awards`, and `state_flag_indices_set`. The common
`field_semantics`/`semantic_fields` join resolves the
raw title, selected-level, cost, stat, skill, and state-work addresses through `vm-map/globals.toml`;
the raw keys remain provenance. The shipped profile reports 69 titled rules, two level-independent rules,
30 skill awards, three used promotion slots, and 19 populated output fields. The underlying input,
working, and persistent globals are now named there as one coherent class-change ABI rather than as
unrelated addresses.

### SCINIT scene-dispatch registry (2026-07-23)

SCINIT is a paired sparse registry, not the malformed 372-row numeric table produced by the generic
numeric heuristic. Each source assignment writes a decision-indexed packed script resource id at
`0x87a57` and an authored chapter tag at the parallel base `0x8a167`, exactly 10,000 cells later.
All 135 distinct resource ids resolve through SYS4INI to `SCxxxx.BIN`. The source contains 2,179
assignments and 1,209 final decision ids; 710 ids are assigned more than once, so extraction preserves
the complete offset-tagged assignment history as well as final values.

The chapter meaning is independently supported rather than inferred from the small integer domain:
SCINIT's source order consists of contiguous 1-through-9 runs followed by a `-1` unassigned run, and
844 of the 847 decision ids emitted by decoded SCJUMP end with the same chapter. The three mismatches
are retained as legacy/stale authoring metadata. `extract_init.py` detects this sixth shape as
`dispatch`; `build/data/SCINIT.json` is the single join from decision id to packed resource id,
resolved script name, authored chapter, SCJUMP chapters, overwrite history, and raw column keys.
`field_semantics`/`semantic_fields` resolves those raw keys through `vm-map/globals.toml`.

### RTINIT movement/battle routine banks (2026-07-23)

RTINIT is a banked sparse program registry, not the generic numeric extractor's former 14-record result.
Its 3,336 static writes address twenty parallel banks separated by exactly 20,000 cells. Direct
`lookup-array-2d` consumers establish that every bank is `int[1000][20]`: `current_routine_set_id`
selects a one-based row and `routine_step_index` iterates slots 0 through 19. Rows 1..176 are populated
except 150..153, yielding 172 routine sets and 3,307 final cells. Twenty-nine cells are written twice;
eleven of those overwrites change the value, all retained in source order.

Banks 0..9 form the movement family. MVRTN reads bank 0 as a provider selector, resolves it through
RTN_M001..018/051..053/061, applies bank 1 as a random-modulo-100 activation percentage, gates the step
on a per-entity progress count plus required/forbidden story flags in banks 7..9, and call-scripts the
provider. Banks 2..5 are provider-tagged parameters and bank 6 is reserved/empty. This produces 1,043
final movement steps.

Banks 10..19 form the battle family. BTRTN dispatches selectors 1..4 to RTN_B001..004, compares each
entity's pre-rolled 0..99 step value against bank 11's activation percentage, and applies the same
required/forbidden story-flag convention in banks 18/19. Bank 12 is an RTN_B004 parameter; banks 13..17
are reserved/empty. Only fourteen shipped battle steps are populated.

`extract_init.py` detects this seventh shape as `banked` and writes `build/data/RTINIT.json`. Each record
keeps its raw `base/20/slot` fields and complete offset-tagged assignment history while adding joined
`movement_steps`/`battle_steps` with provider script names. The bank layout explicitly includes all six
empty reserved banks. Structural and consumer-proven meanings live in `vm-map/globals.toml`; the generic
parameter names remain as raw provenance while each RTN_M/RTN_B consumer proves its tagged schema.

The provider-specific join now covers
RTN_M001/003/004/005/006/007/008/010/011/012/013/014/015/051: 858 movement steps, 974 semantically
consumed populated parameters, thirteen explicit zero defaults, and three authored-but-unread
parameter cells. RTN_M005/011/012 read banks 2/3 as
`destination_tile_x` / `destination_tile_y` and approach that exact map tile, incrementing the current
step's progress counter after arrival. Their alternate completion test recognizes a type-6 stage object
at the authored destination and also accepts the object's linked exit tile. RTN_M012 is byte-for-byte
equivalent to M005 except for its MVSEEK mode: mode 2 masks the doubled-coordinate terrain cell of every
active foreign-faction entity before the flood fill, so the generated behavior distinguishes this
foreign-entity-avoiding route from ordinary M005.

RTN_M004's bank-2 value is a zero-based `stage_object_slot_index`. It approaches that current-stage
object and uses the same exact-tile/type-6-linked-exit completion contract as the coordinate providers.
Three of its 70 steps leave the cell unwritten and therefore receive an explicit semantic slot-0 default;
the generic raw bank remains absent in those rows.

RTN_M006 is the nearest-enemy form. Bank 2 is an inclusive `maximum_target_route_steps`, authored from
1 through 7 plus 10 and 20. It requires the normal-attack bit at encoded range 0, selects the nearest
active entity of another faction which remains eligible after SETMVWORK's offensive-action filtering,
randomizes equal-distance ties, and approaches a reachable tile nearest that enemy. Producing a valid
destination increments the step's progress counter.

RTN_M011 is the cyclic-waypoint form. Bank 4 is a one-based `waypoint_ordinal`; only the step whose
ordinal minus one matches `entity_patrol_waypoint_indices[current_entity]` executes. Arrival advances
that runtime index modulo the largest RTN_M011 ordinal in the selected routine set. Bank 5 is an optional
`path_cost_limit_override`; zero or an unwritten cell falls back to the entity's current FS.

RTN_M007 is the injured-ally form. Bank 2 is `maximum_target_route_steps` (authored as 5 or 10) and bank
3 is an inclusive `maximum_target_hp_percent` (50, 70, or 80). It runs MVSEEK from the acting entity,
keeps active non-self entities of the same faction below the HP cutoff and within the route-step radius,
selects randomly among the nearest tied allies, then chooses a reachable movement tile nearest that
ally. Producing a valid destination increments the step's progress counter.

RTN_M010 is the Healing Feather form. Bank 2 is a `resource_index` into current HP/SP/FS and their
max-stat columns; all seven shipped cells are unwritten, so the generated join explicitly projects the
zero/HP default without fabricating a raw assignment. Bank 3 is an inclusive
`maximum_resource_percent`, authored as 30 or 50. When the selected current/max percentage passes, the
provider chooses the nearest active OBINIT type 15 or 16 object (`治癒の羽`, full status recovery, or its
single-use red variant) and approaches a reachable tile nearest it.

RTN_M015 is the Magic Pillar form. It chooses the nearest active OBINIT type 2/3/4 object (small,
medium, or large `魔力の柱`) whose runtime ownership state differs from the acting entity's faction.
Bank 2 is an inclusive `maximum_target_route_steps`, authored from 2 through 6; the provider produces
movement only when the chosen foreign-controlled pillar is within that radius.

RTN_M008 is the unrestricted-radius form of that Magic Pillar search: it chooses and approaches the
nearest active foreign-controlled type-2/3/4 object without consulting a parameter bank. Its sole
authored bank-2 value is therefore retained under `ignored_movement_parameters`, not presented as a
behavior input. RTN_M001 likewise reads no parameter bank; it advances the current routine step's
per-entity progress counter and sets the execution state. Its two authored cells are preserved as unread
residue. These three cells are the complete authored-but-unread movement-parameter set.

RTN_M013 approaches terrain permitted to a selected faction. Bank 2 is `target_faction_filter`: a
nonzero value selects that faction's bit, while zero/unwritten means any faction except the actor's own.
The provider requires the current tile not already to match that mask and selects the nearest reachable
tile whose `tile_faction_traversal_masks` cell overlaps it. Three of its four steps use the explicit
zero default.

RTN_M014 retreats from nearby enemies. Bank 2 is an inclusive
`maximum_threat_route_steps`, authored as 3 or 6. It collects active foreign-faction entities within
that route radius, sums a proximity surface from all collected threats, removes occupied and
movement-unreachable tiles, and randomly chooses among the lowest-positive-score tiles. This makes the
destination the reachable tile farthest from the nearby threat set rather than merely farthest from one
enemy.

RTN_M003 is a parameterless normal-attack routing provider. It requires the normal-attack bit, builds
the actor's movement-limited MVSEEK grid, and runs SETMVWORK so active foreign-faction entity tiles
remain eligible only when the normal attack's element has positive effectiveness. It then requires the
target tile's movement cost to fit current FS, ranks candidates by descending remaining-route score
with randomized ties, and accepts the first candidate for which SETROUTE constructs a path. Success
advances progress and returns state 1, so FIELD performs movement rather than battle.

RTN_M051 is a parameterless immediate-attack selector. ATSEEK supplies a one-based range band for each
enemy tile; CALCSCOPE supplies the normal-attack/equipped-skill bits and their attack elements at each
band. M051 keeps active foreign targets for which at least one allowed action has positive effectiveness
against the target's equipped or unit defense element. Encountering a closer band clears the accumulated
candidate list, after which a target is randomized from the retained entries. It then randomizes among
the effective actions in the tracked closest band, writing zero for a normal attack or an equipped skill
id. Success stores `target_entity_index` and `entity_selected_action_ids[acting_entity_index]`, advances
progress, and returns state 2 so FIELD enters battle without producing a movement route.

MVSEEK's mode contract is also now explicit. Mode 0 replaces the input coordinate with the current
entity tile and seeds the origin with movement+1, producing the movement-limited reachability grid.
Modes 1/2 retain the caller coordinate and seed it with 9999, producing a broad target-distance grid;
mode 2 additionally applies the foreign-entity terrain mask described above. Every traversed edge
decrements `pathfinding_remaining_route_steps`, so origin minus target is the route-step distance used
by M006/007/015.

`movement_steps` now carry these selector-scoped semantic fields beside the original
`movement_parameter_1..4`, and top-level `movement_provider_parameter_schemas` records the reusable
mapping, target-selection rules, zero/unwritten behavior, and authored fields proven unread by their
provider. All 977 populated movement-parameter cells are now accounted for: 974 semantic inputs and
three explicit residue cells. The five used providers still without schemas
(M002/009/017/052/061) have no populated parameter cells, so their remaining work is behavior
decoding rather than column semantics. The supporting runtime joins are now curated too:
`entity_runtime_flags`,
`entity_faction_ids`, `entity_tile_x`/`entity_tile_y`, the fourteen-column `entity_effective_stats`,
current HP/SP/FS, `stage_object_runtime_flags`, `movement_search_mode`,
`offensive_action_scope_masks`, `pathfinding_remaining_route_steps`,
`pathfinding_filtered_route_scores`, `pathfinding_movement_costs`, and the per-entity patrol waypoint
index. The faction-specific terrain masks used by M013 are curated as
`tile_faction_traversal_masks`. M051's shared action-selection ABI also names `acting_entity_index`,
`target_entity_index`, `attack_range_distance_grid`, the offensive range/mask/element tables,
`attack_element_effectiveness_percent`, equipped active-skill slots, and the per-entity selected action.

### The curated registry — `vm-map/globals.toml` (2026-07-07)

The v1 auto map (`build/global-var-map.json`) infers *shapes* but cannot recover branch-flag
*meaning* — and is sometimes wrong (it labels `0xa57`, the Lily form-A story flag, as a
"string-table"). The curated registry fixes this, modelled exactly on `vm-map/opcodes.toml`:

- **`vm-map/globals.toml`** — the only hand-edited source. One `[[global]]` per known address:
  `name`, `category` (`story-flag`/`index-pointer`/`data-table`/`string-table`/`ui-toggle`/
  `choice-output`/`counter`/`unknown`), `type`, optional row-table `columns`, `value_domain`, `usage`, and provenance
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

1. **Continue INIT semantics by evidence density.** ITINIT/SKINIT, EBINIT, STINIT, CCINIT, SCINIT, and RTINIT now have
   machine-readable investigation surfaces and semantic joins; EBINIT's populated schema is fully named,
   STINIT's joined object/enemy payloads are decoded, and CCINIT's 71 class-change rules expose predicates
   and effects. SCINIT closes the progression decision-to-scene join, and RTINIT's twenty movement/battle
   banks are structurally decoded with every populated movement-parameter cell classified. Next decode
   the five remaining behavior-only RTN_M providers by evidence density, beginning with the closely
   related M017 and M052; never assign one universal meaning to a parameter bank whose
   meaning varies by provider selector.
2. **Extend message-table joins beyond the completed ITMES/SKMES pair** (`VIMES`, other id dispatchers, …)
   and fold in other `set-string`/`copy-to-global` writers not covered by the `*INIT` set.
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

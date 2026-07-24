# SYS4 VM Mapping — Plan of Action

> **For the executing agent:** This is a reverse-engineering playbook. Work it phase-by-phase; each phase ends with a concrete, checkable deliverable. Verify claims against bytes before recording them. Validated seed data lives in `vm-map/`.

**Goal:** Decode the SYS4 bytecode into named instructions so the game logic can be re-implemented in Godot.

---

## ✅ BREAKTHROUGH (2026-07-05): the opcode set is already solved

**The prior "unpack AGE.EXE in Ghidra" critical path is no longer needed to disassemble scripts.** Kelebek1's decompiler ships a complete AGE opcode table that decodes this game directly.

**What was verified this session** (see `tools/validate_opcode_table.py`, run it to reproduce):

- Kelebek1/Eushully-Decompiler's `age-shared.cpp` contains an opcode table (`{op_code, label, argument_count}`) and a header parser that **explicitly handles the SYS4 signature** (`"SYS4"`, header length `0x3C`, cp932 XOR-0xFF strings) — this exact game's format.
- The instruction model: **code = a flat sequence of instructions; each instruction = `<opcode:u32>` followed by `argument_count` arguments, where every argument is a `<type:u32><value:u32>` pair. Instruction length in dwords = `1 + 2*argc`.** Inline strings sit *after* the code inside the `[0,F8)` region; stop decoding at the lowest string offset referenced (a type-2 arg, or op `0x64` arg 1).
- Applying that table to Himegari's scripts: **476 of 476 parseable scripts decode 100% clean — 1,463,788 instructions, 0 unknown opcodes, and all 37,392 inline-string arguments resolve to valid decoded strings.** (The 7 non-decoding `.BIN` are container-level non-scripts like `SYS4AB`/`SYS4INI`, different magic.)
- Himegari uses **248 distinct opcodes; 52 have semantic names** (in `vm-map/opcodes.toml`). The other 196 decode perfectly (known length) but have engine-internal names only (`u004xxxx`). **Caveat (measured 2026-07-06):** the named 52 are the dialogue/ADV core but cover only **72.6% of instruction volume**, not "the entire core" — the unnamed 27.4% is concentrated in the highest-frequency opcodes and must be partly addressed before Phase 4. See Phase 3's coverage correction.

**This resolves the header unknowns too.** Kelebek's `BinaryHeader` struct maps my F0–F12 exactly: `F0`=local_integer_1, `F1`=local_floats, `F2`=local_strings_1, `F3`=local_integer_2, `F4`=unknown, `F5`=local_strings_2, `F6`=sub_header_length(0x1C), then the three (length, offset) table pairs. The "flag fields" were **local-variable counts**. Arg `type` codes: 0=immediate, 1=float, 2=string, 3=global-int, 4=global-float, 5=global-string, 6=global-ptr, 8=global-string-ptr, 9=local-int, A=local-float, B=local-string, C=local-ptr, D=local-float-ptr, E=local-string-ptr.

**Consequence:** Unpacking `AGE.EXE` (still packed — see appendix) drops from *the blocker* to an *optional enrichment* used only to name the 196 unnamed opcodes' fine semantics, and even that has a cheaper dynamic alternative.

**Provenance / sources in `vm-map/`:** `kelebek1-age-shared.cpp` (the opcode table), `kelebek1-disassembler.cpp` (the parser), `opcodes.toml` (validated table filtered to what this game uses), `opcode-leads.json` + `small-script-listings.md` (this session's static analysis, now confirmed).

---

## Global constraints

- **Python:** `py -3.11 -X utf8 …` always (Shift-JIS output needs utf8 mode on Windows).
- **Authoritative copies:** 49 loose game-folder script `.BIN` files shadow `extracted/DATA1/` copies at runtime; two engine BINs are root-only. Target the game-folder copy where both exist. `sys4load.load()` is copy-agnostic; `paths.scripts()` resolves the override.
- **Units:** all script offsets/counts are DWORDS (×4 bytes), relative to body start `0x3C`.
- **Instruction rule:** `len_dwords = 1 + 2*argc`; args are `(type,value)`; **stop code decode at the first inline-string/array offset**, not blindly at `F8`.
- Record confidence per finding (confirmed-by-bytes / confirmed-by-runtime / hypothesis).

---

## Phase 1 — Port the opcode table into `sys4load.py`, disassemble everything *(✅ DONE 2026-07-05)*

**Deliverable:** `sys4load.py` emits real named instructions; every script disassembles with zero unknown opcodes. **Achieved: 481/481 DATA1 scripts decode fully clean; MENU.BIN and SC0030.BIN verified by hand.**

- [x] **1.1 — Embed the opcode table.** Full Kelebek table (548 entries) transcribed to `tools/age_opcodes.py` (`OPCODES`, `ARG_TYPES`, `CONTROL_FLOW`, `is_label_argument`), generated from `vm-map/kelebek1-age-shared.cpp`.
- [x] **1.2 — Replace the T3-chunking stub.** `sys4load.py` now has `decode_code()` (the `1+2*argc` walker with shrinking `code_end`) and a rewritten `render_listing()` that prints mnemonics, typed operands, inline strings, and `label_xxxx:` control-flow anchors.
- [x] **1.3 — Validate.** `tools/sys4load.py ../../extracted/DATA1 --validate` → **481/481 parsed clean, 481/481 opcode-decode clean.** MENU.BIN: 148 instrs, `set-font "ＭＳ 明朝"` + `comment` strings correct. SC0030.BIN: 11,951 instrs, `show-text` shows dialogue inline. (`tools/validate_opcode_table.py` still reproduces the standalone 476/476 over the merged root+DATA1 set.)
- [x] **1.4 — Regression-guard:** container `--validate` still reports 481 clean, 0 failures, 0 impure tags.
- [x] **1.5 — Disassembler is the artifact.** `sys4load.py <file>` prints the full listing; `--json` now includes decoded `code` (with `--json` + `to_dict(with_code=True)`), instruction counts, and decode-clean flag.

**Note:** 7 root `.BIN` are non-script engine indices with different magic (`SYS4INI` = `S4IC422`, `SYS4AB` = `S4AB`, etc.) — correctly rejected by the container parser, not scripts.

## Phase 2 — Extract data tables + dialogue *(✅ mostly DONE 2026-07-06)*

**Deliverable:** game database as JSON + full translatable dialogue corpus. Structure spec: `docs/PROJECT-STRUCTURE.md`. Extractors: `tools/extract_phase2.py`, `tools/extract_init.py`.

- [x] **2.0 — Project structure.** Established `docs/`, `build/{disasm,text,data,scripts-json}/`, `godot/`; game install stays read-only in place. Also relaxed the loader magic check to the `SYS4` family (`SYS4424` patch scripts now parse — was silently skipping 5 scripts).
- [x] **2.1 — Text corpora.** `tools/extract_phase2.py` → 481/481 scripts: full disassembly (`build/disasm/*.asm`), per-script strings, `build/text/dialogue.jsonl` (**30,057 show-text lines** — the translation corpus), `build/text/strings.jsonl` (38,449 strings tagged by source opcode), `build/manifest.json`.
- [x] **2.2 — `*INIT` data and rule sources → JSON.** `tools/extract_init.py` auto-detects shape (`name`/`numeric`/`footer`/`mixed`/`rules`/`dispatch`/`banked`) → **SKINIT (131 skills), ITINIT (287 items), EBINIT (277 units), OBINIT (46 object definitions), CIINIT (24 character-information profiles)** [name: sparse one-based name/description/fields, with CIINIT's dedicated profile registry], **CGINIT (851 sparse gallery images)** [numeric: full image, optional save/stage preview, thumbnail sheet/slot, and variant ordinal in a reserved 2,000-row layout], **ALINIT (107 sparse alchemy recipes)** [numeric: output, level/story gates, point cost, and four paired ingredient slots in a reserved 1,000-row layout], **AFINIT (13 authored affinity rows plus tuning/facility curves)** [name/footer specialization], **CTINIT (five 70-cell name-entry pages)** [string-matrix specialization], **CVINIT (13 character-voice settings)** [numeric: preview assets, slot-to-unit joins, and inverse suppression-setting map], **LAINIT (20 shipped terrain definitions)** [name: sparse strings, topology/rendering, combat-stat, traversal-skill, and texture-fallback joins], **MPINIT (1,472 sparse terrain rows joined to 66 stage maps)** [footer: 53-column doubled-coordinate atlas], **SPINIT (eight 15-slot H-scene gallery pages)** [numeric: INIT2 thumbnail-sheet and SYS4INI scene joins], **TRINIT (21 training actions)** [name/string-matrix: eligibility, cost, stat/alignment/progress effects, rewards, and ten event slots], **CDINIT (nine weighted card-generation lists)** [numeric/dispatch specialization], **CDINIT2 (81 card definitions)** [name: story gates, typed effects, and item/event/condition/visual joins], **BTANINIT (202 battle effects)** [numeric/dispatch: visual/audio/atlas/hit-pulse work records], **BTANINIT2 (122 battle-animation timelines)** [numeric: six effect ids, delays, duration, and skill/weapon joins], **STINIT2 (74 stage definitions)** [name/string-matrix: descriptions, gates, geometry, rewards, and flow joins], **STINIT (74 stages)** [mixed: selector-dispatched strings/scalars/buffer cells/footer arrays], **CCINIT (71 class-change rules over 33 units)** [rules: unit/level/state predicates plus title/cost/stat/skill effects], **SCINIT (1,209 final decision rows)** [dispatch: scene resource plus authored chapter metadata and overwrite history], and **RTINIT (172 routine sets)** [banked: twenty movement/battle step banks, provider joins, and overwrite history]. Validated; see `build/data/README.md`. Raw addresses remain bytecode provenance; confirmed semantics come from `vm-map/globals.toml`.
- [x] **2.3 — Extract `STINIT`'s 74 sparse stage records.** The mixed mode identifies the dominant `scjump_progress_a` dispatch, recovers 29 preallocated buffer layouts (including six consumer-confirmed row strides), and keeps four condition strings, six scalars, fixed-buffer writes, and all 1,396 footer-array copies separated by stage id. Profiles supply population/value and direct-consumer evidence. Header/map semantics cover conditions, BGM, turn/replay/clear-reward settings, and map texture overrides. The object join assembles 2,312 placements with type/position/gates, OBINIT names and available descriptions, 604 reinforcement schedules, and typed initial-faction, teleport, treasure, card-list, non-triggering-faction, and initial-object-state payloads; FIELD's dedicated special-spawn path proves the final three populated type-27 tagged writes are engine-dead, so they remain visible as ignored provenance rather than unresolved semantics. The 30-cell enemy family contributes 1,378 joined spawns with unit/faction, placement, difficulty/story gates, level scaling, weighted selection, difficulty-specific movement/battle routine sets, and 485 first-clear-only gates. Raw address views remain alongside generated `semantic_fields`.
- [x] **2.3a — Extract `CCINIT`'s 71 class-change rules.** Rules mode preserves source-order guards and detects the unit id, minimum level, clear applied-state slot, selected title/level, deployment-cost delta, named fourteen-stat bonuses, awarded SKINIT skills, and state slot set by each rule. EBINIT/SKINIT definition joins and the common global semantic projection coexist with raw addresses. CALCCC/ADDEXP establish the apply/report ABI; EVOLVE explains the two level-independent empty-title Lily rules. The generated profile covers 33 units, 69 titled rules, 30 skill awards, three used promotion slots, and 19 populated output fields.
- [x] **2.3b — Extract `SCINIT`'s scene-dispatch registry.** Dispatch mode recognizes 2,179 alternating writes to two 10,000-cell arrays, preserves all 710 overwritten decision ids, and emits 1,209 final rows. The primary column maps decisions to 135 packed SYS4INI ids, all resolved to numbered SC scripts; the parallel column is authored chapter metadata. All 847 live SCJUMP decisions join to rows and 844 final chapter tags agree with the independently decoded paths; three legacy/stale mismatches remain explicit.
- [x] **2.3c — Extract `RTINIT`'s routine-step banks.** Banked mode recognizes twenty parallel 1000-by-20 tables and emits 172 sparse routine-set rows with all 3,336 source assignments and 3,307 final cells. Movement banks join all 1,043 steps to all 19 used RTN_M providers (22 dispatchable); battle banks join fourteen steps to all four RTN_B providers. Activation percentages, progress gates, and required/forbidden story flags are consumer-proven; six empty banks remain explicit. RTN_M001/002/003/004/005/006/007/008/009/010/011/012/013/014/015/017/051/052/061 add selector-specific progress, randomized roaming, object-slot, coordinate, enemy/ally, treasure and object search, Healing Feather, waypoint, faction-terrain, retreat, normal-attack routing, offensive target/action selection, and immediate allied-healing semantics. All 977 populated movement-parameter cells are classified: 974 semantic inputs and three M001/M008 cells proven unread; thirteen unwritten defaults are projected separately while the generic raw banks remain intact.
- [x] **2.3d — Extract `ALINIT`'s alchemy recipes.** Numeric specialization recognizes seven recipe-indexed structures and emits 107 sparse recipes from all 914 static writes. ALCHEMY proves the output, minimum level, required/forbidden story gates, point cost, and four paired ingredient-id/quantity slots. Every output and all 286 ingredient references join to ITINIT; raw coordinates remain beside the semantic and nested ingredient views.
- [x] **2.3e — Extract `AFINIT` affinity/progression data and `CTINIT` name-entry characters.** AFINIT classifies 27 element strings and 54 footer arrays into thirteen signed effectiveness rows, paired tuning bonus/cost curves, and three facility-progress rows. CTINIT preserves 273 authored characters and 77 empty cells across INPUTNAME's five reserved 70-cell pages. Both schemas account for every instruction and retain raw coordinates.
- [x] **2.3f — Extract `CVINIT` character-voice settings.** Numeric specialization accounts for all 37 static writes as thirteen preview OGG assets, twelve CONFIG-setting-slot-to-EBINIT-unit joins, and the exact inverse unit-to-suppression-setting map. CONFIG proves preview, row-label, persisted speaker-seen unlock, and setting-edit behavior; the shared CNINIT/CVINIT voice chain proves the inverse map.
- [x] **2.3g — Extract `MPINIT`'s stage-terrain atlas.** Footer specialization classifies all 1,472 row copies as columns 1..50 of one stride-53 sparse atlas, with destination-derived grid Y=2..1600 and 127 implicit-zero rows. FIELD proves that STINIT2's tile bounds are doubled before copying into the mutable current-stage grid. The schema joins 66 stage definitions to 53 unique rectangles, preserves eight shared-map groups and 47 border-context cells, and resolves terrain ids through LAINIT's names, texture slots, area-fill flags, and layout classes.
- [x] **2.3h — Extract `LAINIT`'s terrain definitions.** Name specialization classifies all 98 instructions into twenty shipped terrain ids inside a reserved thirty-row registry: seventeen names, five effect descriptions, four parallel terrain arrays, a ten-column signed combat-stat matrix, five SKINIT traversal/reveal requirements, and ten SYS4INI-resolved shared texture fallbacks. CALCBTPARAM, MVSEEK, FIELD, INFOAF, and DRAWMAP prove the complete consumer contract.
- [x] **2.3i — Extract `SPINIT`'s H-scene gallery registry.** Numeric specialization classifies all 118 writes as eight fifteen-slot HMODE pages with two implicit trailing zero cells. Every row joins to INIT2's `SO027A` through `SO027H` thumbnail sheet and every populated cell resolves to an `SP*.BIN` resource. HMODE proves the opcode-0x19d availability-filter and indirect-call contract.
- [x] **2.3j — Extract `TRINIT`'s training-action registry.** Name/string-matrix specialization classifies all 365 instructions into 21 six-text-slot actions and nineteen contiguous numeric families. TRAIN proves story/level/alignment/progress/stat/item/skill gates, spirit and fourteen-stat effects, fractional alignment/progress changes, rewards, execution counts, and event dispatch. ITINIT/SKINIT resolve every gate and reward; all 75 event cells resolve through SCINIT, while GAMESTART proves their restored story-flag role.
- [x] **2.3k — Extract `CDINIT`'s card-generation lists.** Numeric/dispatch specialization classifies all 1,565 instructions into nine selectors and 383 weighted candidates, joins every card to CDINIT2, and links seven live selectors to 246 STINIT card objects. FIELD proves the turn-scaled cumulative selection rule, 100-slot scan, 50-slot clear prefix, and two-column effective required-story-gate contract.
- [x] **2.3l — Extract `CDINIT2`'s card definitions.** Name specialization classifies all 558 instructions into 81 cards inside a reserved 100-row registry. FIELD proves six type-specific effect paths and the engine-dead third required-flag column; ITINIT, SCINIT, ILINIT, and SYS4INI resolve all item, event, condition, and visual references, while STAGECLEAR proves the deferred point-award accumulator.
- [x] **2.3m — Extract `BTANINIT`/`BTANINIT2`'s battle-animation registry.** BTANINIT2 specialization classifies all 1,018 instructions into 122 sparse six-effect timelines, 573 effect references, paired start delays, and 111 complete durations. BTANINIT dispatch specialization classifies all 4,472 instructions into 202 visual/audio/hit-pulse definitions. BTL proves movie/sprite playback, blend, anchor, geometry, sound, and pulse semantics; SKINIT/ITINIT resolve skill and weapon-class selection.
- [x] **2.3n — Extract `STINIT2`'s stage-definition registry.** Name/string-matrix specialization corrects the generic 321-record view to 74 sparse stages plus 296 strings in six pre/post-clear description slots, classifying all 1,634 instructions. FORT, FIELD, SELSTAGE, and STAGECLEAR prove main/EX flags, seven-column story gates, unlock groups, numbered/event presentation, map/minimap geometry, base point and three-coin rewards, and the loader contract. All 174 entry/clear/failure decisions resolve through SCINIT and all 74 loader ids resolve to STINIT. The remaining `0xedc4d` array has no shipped reader and is retained at medium confidence as the authoring-only stage difficulty tier: values 1..8 correlate with challenge/reward progression but have no runtime effect.
- [x] **2.3o — Name the ADV layer surface-slot registry.** `G[0x3239]` is an eight-by-three primary/alternate/transition surface bank initialized by 143 scripts and read across 309. The common CG loader proves double-buffer selection and transition staging; INIT2 supplies the paired retained-object handles, and the companion resource, layer-index, live-slot, and handle globals are curated.
- [x] **2.3p — Name the selected movement-route grid.** `G[0xc6077]` is a reserved 1000-by-27 work grid carrying the one-cell-wide route copied from MVSEEK's remaining-step scores. SETROUTE proves the cardinal backtrace, FIELD proves route rendering and execution, SELACT proves move-before-action use, and RTN_M002..018 prove candidate-endpoint testing. The shared map target coordinates and INIT2 cardinal vectors are curated with it.
- [x] **2.4 — Partial global-var map BUILT + wired into the disassembler.** `tools/global_map.py` → `build/global-var-map.{json,md}` (16,354/49,435 globals labelled: string tables, `*INIT` field arrays, 122 record tables w/ strides, current-entity index pointers). `sys4load` renders the labels inline (`=rec[s30]`, `=current-entity-index?`). See `docs/name-resolution.md`.
- [ ] **2.5 — Grow the global-var map (future, incremental).** Static first: RTINIT is closed at 1,043/1,043 movement steps; the RECOVER/ILINIT condition ABI, ALINIT recipes, AFINIT affinity/progression tables, CTINIT name palette, CVINIT character-voice registry, LAINIT terrain definitions, MPINIT stage-terrain atlas, SPINIT H-scene gallery, TRINIT training actions, CDINIT/CDINIT2 card selection/effects, BTANINIT/BTANINIT2 battle animations, STINIT2's complete stage surface, the 309-script ADV layer surface-slot registry, and the selected movement-route grid are closed; ITMES/SKMES/VIMES/EIMES/CIMES/MAMES are joined to their definitions; and the non-table INFOMES/MES ABIs are classified. Next, continue remaining consumer-led 2D table naming. Full detail: `docs/name-resolution.md` → "Future step — growing the map". Packed `call-script` ids, SCJUMP decision-to-scene dispatch, the shipped RTINIT movement-provider join, and the six completed message joins are resolved.

## Phase 3 — Name the unnamed opcodes *(top ~20 BEFORE Phase 4; the rest on demand)*

> **⚠️ Coverage correction (measured 2026-07-06).** The earlier framing — "52 named ops
> cover the entire core, name the other 196 lazily" — is **overstated**. Across the full
> corpus (1,503,166 instructions, all 481 scripts), **named opcodes are only 72.6% of
> instructions; the 195 unnamed `u004xxxx` ops are 27.4%** — and that 27% is front-loaded
> into the *most common* opcodes, not a deferrable long tail. The top unnamed ops by
> frequency: `0x1f4`/`0x1f5` (**60,297 each** — equal counts → a begin/end or push/pop
> pair, both zero-arg), `0x1d5` (34k), `0x1bc` (27k), `0x71` (26,445 — *exactly* the
> corpus T1 label-table entry count, so it's the **label-definition pseudo-op**, nameable
> by structure for free), `0x1a2` (18k), `0x7a` (17k, argc 3, follows arithmetic →
> computational), `0x1d2` (17k). **A Godot VM hits these in the first few instructions of
> any script.** So naming the top ~20 is a *prerequisite* for Phase 4, not a lazy
> follow-on. Only the genuine long tail (rare ops) is deferrable. Reproduce the measurement
> by iterating `sys4load.load` over the corpus and bucketing `ins.opcode` against
> `age_opcodes.OPCODES` (label starting `u00`/`dev_ukn` = unnamed).

> **⚠️ Named labels are from a *different* AGE title.** The 52 semantic labels are
> transcribed from Kelebek's table for a *later* AGE game. The opcode **number + argc** are
> validated for Himegari (481/481 clean decode proves structure), but the **semantics are
> not independently verified**. The ADV/text core is empirically safe — the 30,057-line
> `build/text/dialogue.jsonl` is proof that `show-text`/`end-text-line`/the string
> mechanism are right, and arithmetic/control-flow labels are corroborated by operand-type
> and jump-target consistency. The exposure is the **effectful named ops you can't see in
> text output** (`play-voice 0xc4`, `draw-texture 0x1fb`, sound/UI/draw ops) — Frida-confirm
> those against Himegari before the VM relies on them; don't assume them.

**Do this before Phase 4:** name/classify the ~20 highest-frequency unnamed opcodes.
Most fall to free inference (3.0); a few opaque effectful ones want a Frida session;
computational ones suit Unicorn. Everything below still applies — it's the *ordering* that
changes, not the toolkit. The genuine rare tail stays lazy (name on demand).

- [x] **3.0 — Inference pass DONE (2026-07-06).** Classified the top 21 unnamed opcodes →
  **instruction coverage 72.62% (named) → 96.94% (classified)**; ~90.5% is VM-handleable by
  inference alone. Tooling: `tools/opcode_context.py` (evidence gatherer). Results:
  `vm-map/opcodes.toml` (per-op evidence + provenance), `tools/age_opcodes_himegari.py`
  (`INFERRED` dict consumed by the disassembler + future VM), `build/opcode-coverage.md`
  (tiers + Frida/Unicorn shortlist). `sys4load` now renders inferred names (verified: MENU's
  `label-def 0x71` land exactly on its T1 targets). Key findings: `0x1f4`/`0x1f5` = stmt
  begin/end brackets, `0x1d5`/`0x1bc`/`0x1bf` = block markers (all zero-arg no-ops); `0x71`
  = label-def (count == T1 size); `0x21b`/`0x1d2`/`0x258` = tentative-no-op statement metadata
  (harness-verify); `0x7a` = ADV text param, `0x202/0x203/0x1f7/0x1fa/0x217/0x218/0x21a/0x1ff`
  = draw/UI, `0xb6` = audio, `0x215` = count/search — the effectful/computational Frida/Unicorn
  shortlist. Reserve live tools for those; rare tail (3%) stays lazy.

### 3.1 — Frida: dynamic observation *(primary tool for effectful opcodes)*

Frida injects a JS engine into the **running** game and hooks functions live. It sidesteps the packer (memory is already decrypted by the time you attach), gives ground-truth behavior, and lets you correlate an opcode with its on-screen/audible effect — the only reliable way to name rendering/audio/input/save/UI handlers. Two stages:

- [ ] **3.1a — Locate the dispatch loop.** Kelebek's `u004xxxx` addresses are from a *different* AGE title and will NOT match Himegari's `AGE.EXE`, so find Himegari's dispatch first. Best anchor: search process memory for a known script's opening opcode sequence (you have every script decoded), set a **hardware read breakpoint / `MemoryAccessMonitor` guard page** on its first opcode dword; when the VM fetches it, the instruction pointer is inside the dispatch fetch. Alternate anchors: breakpoint a winmm/DirectSound call and trigger `play-voice` (0xC4), then walk the stack back; or pattern-scan for the bounds-check + `call [table + opcode*4]`. **Payoff:** read the jump-table base → you get the handler address for all 548 opcodes in Himegari at once.
- [ ] **3.1b — Instrument + correlate.** `Interceptor.attach` the dispatch (or a specific handler); log opcode + operand `(type,value)` pairs (read from the bytecode pointer — layout known) + effect. Three correlation techniques: **API** (hook a basket of D3D9/winmm/user32/file APIs; see which an unknown handler calls), **behavioral** (trigger one in-game action, diff the opcode trace vs. baseline to attribute ops to subsystems), **memory** (log which global/local var-bank slots — sized by header F0–F5 — the handler reads/writes).
- **Setup / gotchas:** `pip install frida-tools`; **attach to the already-running game** (`frida AGE.EXE`) after the title screen rather than spawning — this skips the packer's startup anti-debug. 32-bit x86 target. Japanese locale required to run. Eushully's protector *may* detect Frida's injected thread; if it trips, quiet it (ScyllaHide-style hooks or `frida-gadget`).

### 3.2 — Unicorn: microexecution *(complement for computational opcodes)*

Unicorn is a bare CPU emulator (no OS). It is the **better** tool for the *pure-computation* handlers — arithmetic/bit/string/array helpers and especially the `CALC*` damage/stat formulas — where you want the *exact* operation, not a label. It is **blind** to effectful handlers: the instant one calls D3D9/winmm/file APIs it runs into unmapped code and stubbing tells you nothing (the effect *is* the meaning). Do not use it as a Frida replacement.

- [ ] **3.2a — Microexecute a handler.** Map the handler's code + a synthetic VM state (variable bank + operand), run from entry to `ret`, read back what changed; sweep inputs to recover the formula deterministically, offline.
- [ ] **3.2b — Preferred combo: Frida-snapshot → Unicorn-replay.** Use Frida (3.1a) to find handler addresses and dump the relevant memory (code + var banks + globals) at a known-good moment (e.g. mid-battle); load that snapshot into Unicorn and microexecute individual handlers with input sweeps. Gets Frida's context-setup for free + Unicorn's determinism. **Caveat:** microexecution only recovers behavior that's a pure function of the captured state — if a handler reads a global you didn't snapshot, results are wrong silently. Fine for pure ops; a rabbit hole for stateful ones (leave those to live Frida). Needs a decrypted image to feed (a dump, or bytes pulled via Frida) since `AGE.EXE` is packed.

### 3.3 — Cross-reference siblings *(free, do alongside 3.0)*
- [ ] Kelebek's labels come from a later AGE title; marcussacana/EushullyEditor targets *Kamidori* (same SYS4 era). Diff their handler notes for the specific opcodes you need.

### 3.4 — Static unpack + Ghidra *(last resort)*
- [ ] Only if the above stall. See appendix — dump the decrypted image, load in Ghidra, read the handler at its address. High effort; reserve for genuinely opaque ops that Frida/Unicorn can't pin down.

## Phase 4 — Godot re-implementation

**Deliverable:** the AGE VM running Himegari scripts in Godot.

> **Prerequisite:** don't start Phase 4 against a blank opcode set — the top ~20 unnamed ops
> (Phase 3 preamble) are hit in the first few instructions of any script. Do that thin
> naming slice first, or bring-up stalls immediately on `0x1f4`/`0x71`/etc.

- [ ] **4.0 — Stand up the validation harness *first* (before writing VM opcodes).** The
  strongest correctness oracle already exists in `build/`: for a given scene script, the VM's
  emitted `show-text` sequence must match that file's lines in `build/text/dialogue.jsonl`.
  Wire this as an automated diff (drive one `SC####` script → collect show-text → compare to
  the 30k-line corpus filtered by `file`). This turns "is the VM right?" into a per-scene
  regression test and catches control-flow/branch bugs (wrong jcc → wrong dialogue order)
  early. Extend later to assert extracted-table reads (SKINIT/ITINIT/EBINIT JSON) once
  data-driven opcodes come online.
- [ ] **4.1 — Re-implement the VM** (GDScript/C#): a dword-fetch loop, the core opcodes (arithmetic, comparisons, `jmp`/`call`/`jcc`, `mov`, string ops) **plus the high-frequency unnamed ops named in Phase 3**, the global/local variable banks (sized by the header's F0–F5 counts), and the ADV layer (`show-text`/`end-text-line`/`wait-for-input`/`set-font`/`play-voice`/`draw-*`). Note the named set is only ~73% of instruction volume — budget for the unnamed remainder. The bytecode-heavy design (damage calc, dungeon loop, battle flow are all scripts) makes re-implementation the right call over transpilation.
- [ ] **4.2 — Fill opcodes on demand** from Phase 3's genuine long tail as scripts exercise them (the top ~20 are already done as a Phase 3/4 prerequisite).
- [ ] **4.3 — Deferred:** save-file format (reverse `SAVE.BIN` only if the port must read existing saves).

---

## Appendix — `AGE.EXE` is packed (relevant to Phase 3.4, and as the image source for 3.2)

Verified this session (`tools/pack_check.py`): 32-bit PE, code sections at max entropy (8.00), blank section names, IAT RVA 0, no plaintext anchors. **`SYS4AB.BIN` (magic `S4AB`) is a dead end for static analysis — it decrypts to `AGE.EXE` byte-for-byte** (2026-07-06): 0x2c-byte header (`"S4AB"` + version + `0x0010E000` size dword ×3 + an 8-byte key/hash field), then the payload is a trivial **XOR-`0xFF`** of the *same packed* `AGE.EXE` (`bytes(x^0xFF for x in payload) == AGE.EXE`, exact). So it is **not** a patched/unpacked VM — both on-disk engine images are the identical packed binary, and VA `0x421160` (any handler) is entropy-8.00 garbage in both. The real handler code exists **only unpacked in the runtime heap** (`~30 MB r-x @ 0x62411000`, nonstable base per run — see `docs/global-memory-re.md`). Static Unicorn/Ghidra therefore requires a **runtime dump** of that region, or hook it live.

Static analysis therefore requires a **runtime dump first** — you're dumping for *analysis* not redistribution, so don't chase OEP: launch to the title screen (Japanese locale required), then dump the decrypted image and load it in Ghidra. Tools: **PE-sieve** (CLI, agent-drivable: `pe-sieve.exe /pid <PID> /imp 3`) or **x32dbg + Scylla + ScyllaHide** (GUI, handles the anti-debug). Validate the dump by confirming `SYS4422`/`.BIN`/`DATA1` now appear in plaintext. **But prefer Frida dynamic hooking (Phase 3.1) — it avoids the unpack entirely.**

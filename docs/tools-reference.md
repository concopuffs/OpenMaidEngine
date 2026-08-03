# Tools Reference

Living catalogue of every script in `tools/` — **what it does, how to run it, and what it
reads/writes**. This is the operational companion to `docs/PROJECT-STRUCTURE.md` (which is the
*where-things-live* map); when they overlap, PROJECT-STRUCTURE owns layout, this file owns
usage + I/O. Keep it current: **add a row here whenever you add a tool, and update the row
whenever a tool's inputs/outputs change.**

## Conventions (apply to every tool)

- **Run with** `py -3.11 -X utf8 tools/<name>.py …` — the `-X utf8` is required on Windows so
  cp932/Shift-JIS source text renders (and generated files stay UTF-8).
- **Paths are never hard-coded.** Every tool imports `tools/paths.py` for `GAME_DIR` /
  `EXTRACTED` / `DATA1` / `BUILD` / `VM_MAP` / `BIN`. Relocate the tree by editing only that file.
- **Generated files are never hand-edited** (they're marked ⚙ below). Edit the source, re-run
  the generator.
- **`build/` and `extracted/` are disposable** — everything under them regenerates from a tool.

## Path anchor

| Tool | Purpose | I/O |
|---|---|---|
| `paths.py` | ★ Single path anchor — derives all workspace dirs from its own location; `paths.scripts()` returns the override-aware `{NAME.BIN → path}` corpus map (loose game-folder patches shadow `extracted/DATA1`). | *Imported, not run.* |

## Project validation

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `validate.py` | Layered project validation front door. `core` regenerates/runtime-checks opcode metadata, lints canonical registries, runs pure Python tooling tests and the 502 .NET cases without the `Workspace` trait, checks generated opcode references, and runs `git diff --check`. `workspace` adds the 88 explicitly traited installed-data/native-oracle cases, corpus-derived global generation, real-data Python suites, full SYS4 decode, and Python RECOVER. `runtime` adds the Godot C# build and forced-portable threaded self-test. `full` combines all phases and adds the booted faithful-wait C# scene sweep. Selected prerequisites are strict: an unavailable game/corpus/Godot requirement fails before execution instead of becoming a green skip. Each gate has a timeout and UTF-8 log under `build/validation/validate-<timestamp>/`; Godot receives an isolated validation-owned user-data/log root there so it cannot read or modify the developer's saves/settings. The final table reports results/durations and a before/after Godot-process leak audit. | `validate.py` (defaults to `--level full`) · `--level core|workspace|runtime|full` · `--godot <console>` · `--game-root <install>` · `--verbose` · `--fail-fast` | sources + selected toolchain/game/corpus prerequisites → console summary + ⚙ `build/validation/validate-*/<gate>.log` |
| `test_validate.py` | Pure tests for launcher-equivalent explicit/environment/PATH/conventional resolution precedence, invalid-explicit hard failure, level composition, and final gate ordering. | `test_validate.py` | temporary files only |

Levels are cumulative around `core`: `workspace` means core+workspace-corpus, `runtime` means core+Godot,
and `full` means every phase. `workspace`/`full` intentionally require the disposable generated inputs named
by a failed preflight; rebuild each through its owning tool in this reference. Runtime Godot resolution uses
`--godot`, then `AGE_GODOT_CONSOLE`, then `godot4`/`godot`/`godot-mono` on `PATH`. Game-root resolution uses
`--game-root`, then `AGE_GAME_ROOT`, then the conventional sibling install and always requires `SYS4INI.BIN`.

The hosted wrapper is `.gitea/workflows/core-validation.yml`. It matches the target server's demonstrated
`ubuntu-latest`, `actions/checkout@v4`, `actions/setup-dotnet@v4`, and
`christopherhx/gitea-upload-artifact@v4` baseline, adds Python 3.11, and runs only `--level core`. A standalone
source-only repository under an arbitrary directory name passes that exact driver without sibling game or
extracted data. Repository discovery uses tracked marker files rather than requiring the checkout directory to
be named `age-reimpl`. The workflow has no secrets, private corpus, Godot runtime, cache, packaging, or deployment
access; failure logs are retained for seven days. The private remote's default and tracked development branch is
`develop`; pushes and pull requests targeting it select the gate. The rewritten repository is published there,
and the first actual Linux/Gitea core run succeeded on 2026-08-03 before the later source-history sanitation;
its tree-equivalent rewritten commit is `524ea74`.

## Optional local binary tools

`bin/` is an ignored machine-local tool directory, not a source or artifact distribution directory. A local
`BinExtractALF.exe` plus its matching `LzssCpp.dll` may be used by the installed-data archive parity test and to
regenerate `../extracted/`; neither is a runtime dependency. Their known local hashes and unverified provenance
are recorded in `bin/README.md`. PE-sieve is obsolete and is not retained; its historical experiment remains in
`docs/engine-re.md`.

## Container parse / disassemble

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `sys4load.py` | Loader + opcode-decoding disassembler for SYS4 `.BIN` scripts (the container-format core every other tool builds on). Annotates global operands (`build/globals.json`) and **`call-script` targets by name** (`build/callscript-names.json`, e.g. `call-script 0x1ab =ADDITEM.BIN`). | `sys4load.py <file.BIN>` · `--summary` · `--strings` · `--json` · `sys4load.py <dir> --validate` (corpus check) | `.BIN` + `age_opcodes*.py` + `build/globals.json` + `build/callscript-names.json` → stdout listing, or `build/scripts-json/` with `--json` |
| `age_opcodes.py` | ⚙ Generated Python ABI view: the complete opcode label/argument-count catalog, operand-type labels, control-flow target operands, and inline-array opcode. **Do not hand-edit.** | *Imported.* | `vm-map/opcodes.toml` → generated module |

## Opcode reference toolchain — single source of truth = `vm-map/opcodes.toml`

All opcode knowledge (ABI, semantics, provenance, `depends_on`) is hand-edited **only** in
`vm-map/opcodes.toml`. Everything else is generated from it.

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `opcodes_build.py` | Generator + linter for the opcode reference. `--bootstrap` appends corpus-observed entries to a new/alternate registry; `--bootstrap-age` copies every missing canonical catalog entry into one as an unobserved compatibility stub. Stubs retain the registry's ABI label/argument count so other AGE scripts decode past them, but remain `noop_headless=false`: the VM traces/skips them while coverage reports them as unresolved rather than semantically safe. | `--build` · `--lint` · `--bootstrap` · `--bootstrap-age` | `vm-map/opcodes.toml` → ⚙ `tools/age_opcodes.py`, ⚙ `tools/age_opcodes_himegari.py`, ⚙ `build/opcodes.json`, ⚙ `docs/opcode-reference.md`, ⚙ `build/opcode-coverage.md` |
| `opcodes_model.py` | In-memory model + loader + linter (dangling-ref / confidence-ceiling / vocabulary / dependents). | *Imported by `opcodes_build.py`.* | `vm-map/opcodes.toml` → — |
| `test_opcodes.py` | Unit tests for the opcode tooling. Bootstrap coverage injects 248 synthetic observations derived from the canonical observed-opcode set; it does not read the private script corpus. Production `--bootstrap` still scans the real corpus by default. | `test_opcodes.py` | `vm-map/opcodes.toml` → temporary files only |
| `opcode_context.py` | Read-only evidence gatherer for classifying unnamed opcodes (frequency, argc, operand-type signature, neighbours, disassembly snippets, canonical registry note). | `--top 20` · `opcode_context.py 0x1f4 0x71 …` | `vm-map/opcodes.toml` + corpus → stdout |
| `validate_opcode_table.py` | Definitive decode-coverage validator using the canonical registry's generated ABI and the SYS4 code/data boundary. | `validate_opcode_table.py` | corpus → stdout |
| `validate_opcode_table_naive.py` | Naïve variant of the above (baseline comparison). | `validate_opcode_table_naive.py` | corpus → stdout |
| `age_opcodes_himegari.py` | ⚙ Inferred Himegari opcode semantics — **generated; do not hand-edit.** | *Imported by `sys4load.py`.* | — |
| `globals_build.py` | Merge curated `globals.toml` over the auto shape map, preserve optional machine-readable row-table `columns`, and generate the global registry + linter. | `--build` · `--lint` | `vm-map/globals.toml`, `build/global-var-map.json` → ⚙ `build/globals.json`, ⚙ `docs/global-reference.md` |
| `story_flags.py` | Static story-flag miner (branch-condition mining) + `--bootstrap` skeleton seeding. | `story_flags.py` · `--bootstrap` | corpus, `build/global-var-map.json` → ⚙ `build/story-flags-candidates.json`, appends `vm-map/globals.toml` |
| `test_globals.py` | Unit tests for the globals registry + story-flag miner. | `test_globals.py` | — |
| `scjump_decode.py` | Decode SCJUMP's progression logic → decision table; `--verify` VM cross-check. | `scjump_decode.py` · `--verify` | SCJUMP.BIN, `build/globals.json` → ⚙ `build/scjump-decisions.{json,md}` |
| `test_scjump.py` | Unit tests for the SCJUMP decoder. | `test_scjump.py` | — |

## Extraction / data corpora

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `extract_phase2.py` | Batch: disassembly + text corpora for every script. | `extract_phase2.py` | corpus → `build/disasm/*.asm`, `build/text/{dialogue.jsonl,strings.jsonl,*.strings.txt}`, `build/manifest.json` |
| `extract_message_table.py` | Discover repeated global-id dispatch chains in either fallthrough-body (ITMES/SKMES/MAMES) or branch-target-body (VIMES/EIMES/CIMES) form, reconstruct player-facing text and furigana, preserve guard/body offsets, and emit an ID-keyed message table. Title-bearing tables expose `title`/`description`; EIMES exposes its untitled lines as `summary`/`strategy`; CIMES exposes one untitled multiline `biography`; MAMES exposes untitled `description` text. | `extract_message_table.py <MES> [OUTNAME]` | `<MES>.BIN` → `build/data/<OUTNAME>.json` |
| `extract_init.py` | Parse a `*INIT` data source (auto-detects name / numeric / footer / mixed / rules / dispatch / banked shape). Name tables infer their reserved record span, preserve sparse one-based runtime ids, distinguish lookup bases from first written cells, statically evaluate direct and negative-value writes, and separate parallel `fields` from linked row-major `record_fields`; when flat global ranges overlap, a parallel base established by other records wins over a coincidental row-table cell calculation. Dedicated name-mode schemas recover VIINIT's pre-title glossary prerequisites, CIINIT's profile registry, and STINIT2's corrected 74-row stage catalog plus six-line pre/post-clear text matrix, availability gates, geometry, rewards, SCJUMP joins, and loader resources. Footer-mode MPINIT recovers its sparse 53-column doubled-coordinate terrain atlas, joins 66 STINIT2 stage rectangles, and resolves terrain ids through LAINIT. Mixed tables recover selector-dispatched records, condition strings, scalars, preallocated buffer cells, consumer-confirmed strides, and length-prefixed footer arrays; STINIT additionally assembles confirmed buffers into `object_placements` and `enemy_spawns`, joins object type names and available descriptions from OBINIT, and exposes consumer-proven tagged payloads (teleport, treasure, card, initial faction, non-triggering hazard/barrier faction, and OBINIT-gated initial object state), preserves engine-dead type-27 writes under `ignored_payload_fields`, and exposes reinforcement schedules and first-clear-only enemy gates. Rules mode decodes CCINIT's source-ordered class-change predicates and output accumulators, joining EBINIT unit names, SKINIT skill names, titles, named stat bonuses, cost deltas, and applied-state slots. Dispatch mode decodes SCINIT's paired decision-to-packed-scene and authored-chapter arrays, preserving source-order overwrites, resolving SYS4INI script names, and cross-checking chapter tags against decoded SCJUMP paths. Banked mode decodes RTINIT's twenty parallel 1000-by-20 movement/battle routine banks, retains all assignments and overwrites, assembles final per-slot steps, resolves provider selectors to RTN_M/RTN_B scripts, and applies selector-scoped schemas where proven. Current schemas cover all nineteen used providers, RTN_M001/002/003/004/005/006/007/008/009/010/011/012/013/014/015/017/051/052/061, including progress-only, randomized roaming, object-slot, coordinate, enemy/ally, treasure and Magic Pillar search, Healing Feather, waypoint, faction-terrain, retreat, reachable normal-attack routing, immediate offensive target/action selection, and immediate allied healing. Parameterless behavior schemas publish an empty `parameter_fields` mapping while still joining `provider_behavior`, target selection, action selection, and completion rules. Provider defaults such as M004's unwritten slot 0, M010's zero/HP resource index, and M013's any-foreign-faction filter are projected explicitly and counted separately from populated source cells. Authored cells proven unread by M001/M008 remain under per-step `ignored_movement_parameters` and contribute to top-level `ignored_movement_parameter_count`; they are not silently discarded or assigned invented meanings. Raw provider banks remain intact, and top-level `movement_provider_parameter_schemas` documents the projection. ITINIT, SKINIT, VIINIT, EBINIT, and CIINIT join their matching MES text. Top-level `field_semantics` maps raw keys to canonical global/column names, while each record's `semantic_fields` is the generated name-keyed join; complete footer copies map to a row and expose its values without the raw provenance wrapper. Raw keys and unresolved tagged payload cells remain provenance. OUTNAME accepts a stem or one `.json` suffix and rejects paths. Refreshes the generated data index. | `extract_init.py <TABLE> [OUTNAME] [--mode …]` | `<TABLE>.BIN` plus EBINIT/SKINIT for CCINIT, OBINIT for STINIT, ITINIT/SCINIT and `build/callscript-names.json` for STINIT2, STINIT2/LAINIT for MPINIT, CDINIT2/STINIT for CDINIT, ITINIT/SCINIT/ILINIT and `build/callscript-names.json` for CDINIT2, BTANINIT/SKINIT/ITINIT and `build/callscript-names.json` for BTANINIT2, BTANINIT2 and `build/callscript-names.json` for BTANINIT, `build/callscript-names.json` for RTINIT/SCINIT, `build/scjump-decisions.json` for SCINIT, matching `<MES>.BIN` when supported, and `build/globals.json` → `build/data/<OUTNAME>.json`, `build/data/README.md` |
| `init_table_profile.py` | Build the static investigation surface for an extracted name/numeric/footer/mixed/rules/dispatch/banked table: per-scalar/string/array-cell/footer-array population and value distributions, representative records, and direct opcode/script consumers. MPINIT's footer specialization summarizes atlas geometry, implicit-zero rows, terrain ids, stage-rectangle joins, sharing, and border cells; STINIT2 summarizes corrected record/text geometry, mapped/event/main/EX populations, story gates, SCJUMP/loader joins, rewards, and its unresolved column; CDINIT summarizes selector/list sizes, CDINIT2 joins and story gates, STINIT references, and the runtime scan/clear geometry; CDINIT2 summarizes card-type and effect populations plus item/event/condition/visual join coverage; BTANINIT/BTANINIT2 summarize effect modes/resources, six-slot timeline geometry, delays, duration, skill/weapon joins, and unreferenced authoring. Name tables report message coverage for title/description, summary/strategy, biography, and description-only layouts; rule tables summarize covered units, titled and level-independent rules, threshold/slot distributions, and skill awards; dispatch tables summarize assignments, overwrites, script-name resolution, and SCJUMP chapter agreement; banked tables summarize populated/reserved banks, movement/battle steps and providers, selector-specific semantic coverage, and overwrite conflicts. `--message-query REGEX` searches every supported message field beside all populated fields for semantic correlation. Findings are evidence only; confirmed meanings go in `vm-map/globals.toml`. | `init_table_profile.py <TABLE> [--build] [--limit N] [--message-query REGEX]` | `build/data/<TABLE>.json` + corpus → stdout; with `--build`, `build/data/<TABLE>-field-profile.{json,md}` |
| `test_extract_init.py`, `test_init_table_profile.py` | Regression checks for sparse one-based, mixed selector-dispatched, CCINIT rule, SCINIT dispatch, RTINIT banked, ILINIT condition-matrix, CNINIT unit-name/voice-family, CGINIT gallery, ALINIT alchemy-recipe, AFINIT affinity/progression, CTINIT name-entry-palette, CVINIT character-voice, TRINIT training-action, CDINIT card-generation, CDINIT2 card-effect, BTANINIT/BTANINIT2 battle-animation, STINIT2 stage-definition, and MPINIT terrain-atlas extraction; RECOVER ABI validation; MES reconstruction/joins; footer-array accounting and semantic projection; EBINIT/SKINIT/OBINIT definition joins; STINIT object/enemy joins and tagged-payload behavior; overwrite/provider joins; and field/message/rule/dispatch/banked/map/training/card/battle-animation/stage profiling. | run each directly | — |
| `global_map.py` | Build the partial global-variable name map from static evidence. | `global_map.py` | corpus + `build/data/` → `build/global-var-map.{json,md}` |

For selector-keyed append INIT fragments, pass the universal packed script id:

```powershell
py -3.11 -X utf8 tools/extract_init.py EBINIT --packed-id 0x01000001
```

The extractor finds the mounted AAI by the packed id's high-byte selector, bounds-checks and reads the
indexed ALF payload, and parses it without materializing an extracted `.BIN`. Packed EBINIT fragments reuse
the base EBINIT table geometry so their additive writes retain the same semantic field names. Output is
kept separate as `build/data/APPEND<selector>-<TABLE>.json` and records `packed_id`,
`source`, and `fragment_layout_source`; it is not an inferred base/append merge. Matching base MES data is
not joined to a packed fragment.

Use the profiler's focused record view to inspect a unit by integer id, exact name, or name regex:

```powershell
py -3.11 -X utf8 tools/init_table_profile.py APPEND01-EBINIT --record 81
py -3.11 -X utf8 tools/init_table_profile.py APPEND01-EBINIT --record "紫色の珍獣/BOSS"
```

`--record ID_OR_NAME` prints the INIT description strings and a semantic-field/value/raw-provenance table;
packed asset values are shown in both decimal and hexadecimal. It is mutually prioritized over
`--message-query` for console rendering, while `--build` may still be used to write the complete profile.

`extract_init.py` also has name-mode specializations for VIINIT's sparse 200-row glossary table,
CNINIT's 277 EBINIT-keyed display-name/voice-family rows within parallel 1,000-cell arrays,
CIINIT's 24 populated character-information profiles within four reserved 100-cell columns, MAINIT's
eleven populated magic-action rows within a reserved 30-cell layout, and ILINIT's thirteen populated
condition ids within a reserved 30-by-5 level layout. ILINIT classifies every authored write, adds nested
names/durations/stat/resource deltas, and validates/joins RECOVER's current-level, passive-baseline,
remaining-turn, recovery-policy, and full-resource-restore protocol. The profiler accepts the same
`base/stride/column` provenance for row-major string fields as for integer record columns. Matching message joins are
ITINIT→ITMES, SKINIT→SKMES, VIINIT→VIMES, EBINIT→EIMES, CIINIT→CIMES, and MAINIT→MAMES. EBINIT is
intentionally sparse across unrelated unit definitions; MAINIT intentionally lacks MAMES bodies for growth
ritual ids 10 and 11.

Numeric-mode CGINIT has its own consumer-proven specialization rather than the generic stride-one
heuristic. It emits 851 sparse rows in the reserved 2,000-row layout, classifies all 3,941 integer
writes into one two-column image/preview table plus thumbnail sheet, 30-cell atlas slot, and variant
ordinal arrays, and joins the packed asset ids through `build/callscript-names.json`. The four thumbnail
sheets are read from INIT2's ten-cell CGMODE configuration and exposed as the 6-by-5 `SO026A.AGF`
through `SO026D.AGF` atlases. The optional preview column
is identified from SAVE/SELSTAGE's 112-by-84 consumer path; all raw addresses remain present.

Numeric-mode ALINIT also has a consumer-proven specialization. It classifies all 914 static writes
into 107 sparse recipe ids within a reserved 1,000-row layout: output item, minimum alchemy level,
required and forbidden story flags, point cost, and four paired ingredient-id/quantity cells. Output
and ingredient ids must resolve through ITINIT. The generated record adds a nested `ingredients` view
while retaining every raw scalar and `base/stride/column` coordinate.

Name-mode AFINIT and CTINIT bypass the generic name-record heuristic because their strings are
vocabularies rather than record boundaries. AFINIT classifies its 27 element labels and 54 footer
copies into the signed affinity matrix, paired item-tuning bonus/cost curves, and three facility
progression rows. CTINIT emits five reserved 70-cell INPUTNAME pages with 273 authored characters and
every empty slot preserved. Both retain raw global/stride/footer provenance beside their semantic views.

Numeric-mode CVINIT classifies all 37 writes into CONFIG's thirteen preview-voice assets, twelve
setting-slot-to-EBINIT-unit joins, and the exact inverse unit-to-suppression-setting map. Preview ids
join to shipped OGG filenames, character slots join to EBINIT names, and the schema records the
per-unit persisted speaker-seen cells that gate CONFIG row availability. Every raw coordinate remains
beside the joined setting view.

Name-mode LAINIT classifies all 98 instructions into twenty shipped terrain definitions inside a
reserved thirty-row registry. It preserves seventeen names, five effect descriptions, four parallel
terrain arrays, the signed ten-column combat-stat matrix, and three implicit-default terrain ids.
Required skill ids join to SKINIT names; shared texture-slot fallbacks join to SYS4INI AGF filenames.
The profile summarizes those populations and the CALCBTPARAM/MVSEEK/FIELD/INFOAF/DRAWMAP contract.

Numeric-mode SPINIT emits eight complete fifteen-slot HMODE gallery pages instead of 118 fragmented
address records. It joins the page rows to INIT2's `SO027A.AGF` through `SO027H.AGF` thumbnail sheets,
resolves every populated cell to its `SP*.BIN` resource, preserves the final two zero slots, and
records HMODE's opcode-0x19d availability-filter plus indirect-call contract.

Name-mode TRINIT bypasses the generic record-boundary heuristic and emits 21 training/sexual-magic
actions. Each row preserves six available/locked text slots and the contiguous eligibility, spirit-cost,
fourteen-stat, alignment, training-progress, award, and ten-event numeric families. Required and awarded
ids join through ITINIT/SKINIT; all 75 event cells join through SCINIT, and the output records TRAIN's
execution path plus GAMESTART's restored-story-flag contract. Its profile summarizes text/gate/effect
populations, execution-limit distribution, and event dispatch coverage.

Numeric-mode CDINIT bypasses the generic fragmented-address view and emits nine selector-dispatched
card-generation lists with 383 candidate entries. Each one-based slot joins its CDINIT2 card id,
display name, result text, effective required/forbidden story flags, and three FIELD-proven weight
parameters. STINIT type-28 objects join back to seven live selectors; authored selectors 55 and 94
remain explicit as unreferenced data. FIELD's effective weight is
`base_weight + floor(current_stage_turn / growth_interval_turns) * growth_weight`, followed by
cumulative weighted random selection across its 100-slot scan. The schema separately preserves the
eighteen CDINIT2 third-column required flags that FIELD never reads, plus the shipped 50-slot clear
prefix even though selector 160 authors 75 slots.

Name-mode CDINIT2 emits all 81 card definitions within the reserved 100-row registry. It preserves
the two text cells and every raw numeric array while projecting FIELD's six card types: story event,
item award, stage-clear point bonus, resource recovery, trap, and random warp. Required/forbidden
story gates distinguish FIELD's two effective required columns from the authored but unread third
column. Item ids join to ITINIT, event ids to SCINIT, condition ids to ILINIT, and all visual ids to
SYS4INI `MVS*.AGF` resources. Ranged effects expose the authored minimum and maximum-exclusive bound;
an equal pair is a fixed value. The consumer contract also records FIELD's rendering/effect order
and STAGECLEAR's deferred spendable-point award.

Numeric-mode BTANINIT2 emits 122 sparse battle-animation rows inside three reserved 1,000-row
arrays: six effect ids, six paired start delays, and one overall duration. Every one of its 573
effect references joins to BTANINIT. The output also joins 101 SKINIT skill references across 98
animations and ITINIT weapon classes to normal-attack rows 1 through 21; passive reactions 801--808,
hardcoded defeat row 809, four unjoined authored rows, and the unused slot 4 remain explicit.

Numeric-mode BTANINIT decodes its conditional effect-id program into 202 definitions rather than
one generic scalar. Each definition retains the six-slot work-array writes for visual resource and
mode, additive blend, surface geometry, combatant anchoring and offsets, sprite-atlas timing,
optional WAV and delay, and three reserved hit-pulse offsets. All 202 AGF and 199 populated WAV ids
resolve through SYS4INI. The schema records BTL's movie/sprite playback and actor/target slot
contract, the engine-dead authored atlas-row count, and sixteen definitions not referenced by
BTANINIT2.

Name-mode STINIT2 emits 74 sparse stage definitions rather than the generic view's 321 apparent
records. It recognizes the separate `string[1000]` title array and `string[1000][6]` description
matrix, retaining three uncleared and three cleared lines per stage. The schema joins numbered,
event-only, and EX presentation; main-progression, unlock-group, and seven-column story gates;
tile/doubled-grid/minimap geometry; point and bronze/silver/gold coin rewards; all 174
entry/clear/failure decisions through SCINIT; and all 74 loader ids to STINIT.BIN. The 66 populated
cells at `0xedc4d` are exposed as medium-confidence authoring-only difficulty tiers: their 1..8
ordering tracks challenge/reward progression, but no shipped script reads the array.

Footer-mode MPINIT classifies all 1,472 copies as fifty-cell rows within one stride-53 terrain atlas.
It derives grid Y from each destination, retains 127 implicit-zero gaps, and joins STINIT2's inclusive
tile bounds after the consumer-proven two-times coordinate conversion. The output contains 66 named
stage maps over 53 unique rectangles, shared-rectangle groups, terrain populations, LAINIT definitions,
and the exact FIELD/DRAWMINIMAP/RESETLAND copy/fallback/restore contract.

## VM

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `vm0.py` | Headless Python bytecode VM (Phase A0 execution-model prototype; reuses `sys4load`; native `bit-set`/`bit-reset` operands are bounded bit indices). | `--test` (RECOVER unit test) · `--sweep [N]` (oracle coverage) · `--scene NAME` · `--settex NAME` (set-texture resId trace + exec trace) · `<file.BIN>` | corpus → stdout; `build/vm0-trace.json`; `build/settex-<NAME>.json` |
| `scene_opcode_coverage.py` | Per-scene opcode completeness gauge: histograms a scene's static opcodes and classifies each **impl** / **safe-noop** / **GAP** (effectful op the VM silently stubs). Implemented set parsed from `VirtualMachine.cs` `case` arms; metadata from `opcodes.json`. Surfaces the concrete rendering/feature holes so a half-drawn scene reads as "N ops still stubbed", not "mystery". | `scene_opcode_coverage.py [SCENE …]` (default SC0000) | corpus, `build/opcodes.json`, `engine/…/VirtualMachine.cs`, `build/callscript-names.json` → ⚙ `build/scene-opcode-coverage/<SCENE>.md` + stdout |
| `correlate_scope.py` | Align the VM's `set-texture(resId)` trace with the game's Frida load order → tag each load's DATA2 package, flag package transitions, dump the significant ops in each transition span (the **scope selector** hunt). | `correlate_scope.py <SCENE>` | `build/settex-<SCENE>.json` + `build/frida-load-order-result.json` + index → stdout |
| `diff_optrace.py` | **Differential offset-path oracle** (`docs/engine-re.md`): diff the engine's executed offset path (`trace_engine_ops.py`) against the VM's (`Age.Cli trace --trace-json`) → first divergence = the mis-modeled branch/op/state, with opcode + ±3 ops of context. Identifies the scene's codebase by longest-common-prefix; filters the VM trace to argc≥1 (operand-capture parity). Pure core unit-tested (`test_diff_optrace.py`). | `py -3.11 -X utf8 tools/diff_optrace.py SC0000 [--full]` | `build/engine-optrace.jsonl` + `build/vm-optrace.json` + disasm → stdout |

### Runtime page locator

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `locate_page.py` | Resolve a run-relative ADV page number to its canonical wait script/offset, last show-text instruction, call stack, and nearby disassembly. Pure selection/window logic is tested by `test_locate_page.py`. | `py -3.11 -X utf8 tools/locate_page.py SC0000 14 [--map <jsonl>] [--context N]` | `build/page-map-<SCENE>.jsonl` + script corpus → stdout |

In a live Godot run, **F6** writes an observe-only stall snapshot under
`user://diagnostics/stall-<timestamp>.json`, prints the absolute path, and copies
`SCRIPT@offset · stall snapshot <path>` to the clipboard. The JSON contains the current call stack, a bounded
128-instruction ring, VM/host wait flags, surface/movie completion state, decoder state, and the exact finite
graphics channels capable of holding an op-`0x21c` presentation wait. Press it while the apparent stall is still
active; unlike `--timeline-log`, it does not require a special launch or emit a continuous per-op stream. Active
and pending movie records include `first_frame_source_pts_ms`; the ordinary `movie first frame` console line prints
the same source PTS alongside the render frame, which distinguishes encoded stream lead-in from decode/presentation
latency.

Normal interactive Godot sessions have no cumulative instruction ceiling. Bounded Godot diagnostic runs retain
`STEP-LIMIT`; if one fires, Godot captures the same diagnostic automatically as
`user://diagnostics/step-limit-<timestamp>.json` and copies its coordinate/path to the clipboard. Before nested
frames unwind, the trace sink preserves the deepest active script stack. The console also prints the exact final
script/offset/opcode, that frame chain, the hottest sites in the bounded final 128-instruction window, and the
final 16-instruction sequence. This makes the last ADV locator unnecessary for identifying a post-dialogue loop;
send either the `step-limit` console block or the generated JSON.

## Native FFmpeg movie shim (Windows and Linux x64)

The target-specific dependency manifests pin immutable LGPL shared FFmpeg archives and SHA-256 hashes. The
Windows tools use PowerShell/MSVC; the Linux tools run in Bash with Python 3, curl, a C compiler, binutils, and
the ordinary archive/core utilities available.

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `native/age_movie_ffmpeg/bootstrap-win64.ps1` | Download, hash-check, extract, and version-check the pinned FFmpeg SDK. Returns the resolved SDK root. | `.\\native\\age_movie_ffmpeg\\bootstrap-win64.ps1 [-Destination <dir>]` | `dependency-win64.json`, network/archive cache → disposable `build/downloads/`, `build/ffmpeg-sdk/` |
| `native/age_movie_ffmpeg/build-win64.ps1` | Discover the MSVC x64 toolchain, build `age_movie_ffmpeg.dll`, and stage its exact shared-library/license dependencies. | `.\\native\\age_movie_ffmpeg\\build-win64.ps1 -SdkRoot <bootstrap-output> [-OutputDirectory <dir>]` | C ABI source + FFmpeg SDK → disposable `build/native/win-x64/` by default |
| `native/age_movie_ffmpeg/bootstrap-linux-x64.sh` | Download, SHA-256-check, extract, and version-check the pinned Linux x64 FFmpeg SDK. Prints the resolved SDK root. | `native/age_movie_ffmpeg/bootstrap-linux-x64.sh [destination]` | `dependency-linux-x64.json`, network/archive cache → disposable `build/downloads/`, `build/ffmpeg-sdk/` |
| `native/age_movie_ffmpeg/build-linux-x64.sh` | Build the ELF64 x86-64 shim; stage the five exact FFmpeg SONAME libraries and license; reject stale `.so` files, unresolved/nonlocal FFmpeg dependencies, a missing `$ORIGIN` runpath, or a glibc requirement newer than the pinned 2.28 baseline. | `sdk_root="$(native/age_movie_ffmpeg/bootstrap-linux-x64.sh)"` then `native/age_movie_ffmpeg/build-linux-x64.sh "$sdk_root" [output-directory]` | C ABI source + FFmpeg SDK → disposable `build/native/linux-x64/` by default |
| `tools/movie-corpus-gate` | Discover every MPEG program stream stored under an `.AGF` catalog entry; decode every video frame and, when present, every audio block through the unpaced FFmpeg session; validate independent sequence dimensions, metadata, RGBA size, stereo finite PCM, monotonic video/audio timestamps, EOF, timeout, and teardown; then emit a per-asset JSON report. Audio fields include sample rate/channels, block and PCM-frame counts, first/last PTS, signal presence, and decode time. Exit 0 means the expected corpus count and every asset passed; exit 1 is a gate failure; exit 2 means the native shim is absent. | `dotnet run --project tools/movie-corpus-gate -- --output build/movie-corpus-ffmpeg.json --expected-count 213 --max-item-ms 30000` · optional `--native-dir <dir>` | `SYS4INI.BIN` + loose/ALF VFS assets + staged FFmpeg shim → stdout progress + disposable `build/movie-corpus-ffmpeg.json` |

The managed isolated probes load from `AGE_FFMPEG_NATIVE_DIR` when set, then application-local and
`runtimes/<rid>/native` locations. Build the target shim before running the `FfmpegShim*` tests; no original-game
movie is copied into the repository or native output. A target-aware `dotnet build godot/Himegari.csproj`
stages the matching shim, five shared-library dependencies, and `FFmpeg-LICENSE.txt` beside `Himegari.dll`
when the corresponding `build/native/win-x64/` or `build/native/linux-x64/` bundle exists.

The current native movie ABI is version 3. In addition to sequential video/audio decode, it exposes a
synchronous position seek for both independent demuxers; exact video-frame selection and audio trimming are
performed by the managed decoder's preroll before normal paced delivery.

The corpus gate intentionally bypasses presentation waits: it validates video/audio decode compatibility and
lifecycle, not wall-clock playback pacing or audible output. `--expected-count` makes additions, omissions, or profile changes explicit;
changing the pinned FFmpeg dependency requires rerunning this gate.

## Engine (C#) — VM core, CLI, Godot frontend

The `engine/` .NET solution (`AgeEngine.sln`) is the runtime VM; `godot/` is the ADV frontend. Not
Python, but listed here as the things you *run*. Build: `dotnet build engine/AgeEngine.sln`; test:
`dotnet test engine/AgeEngine.sln`. Run a CLI command: `dotnet run --project engine/Age.Cli -- <cmd>`.

**call-script executes** on the product paths: they inject `Sys4ScriptProvider`, which runtime-parses
`SYS4INI.BIN` and opens `.BIN` bytes through the native loose-first/bounded-ALF store, so `call-script <id>` loads & runs the target as a nested subroutine
frame sharing globals. `trace`/`audio`/`gfx` stay **provider-less** (call-script stubbed) — base-ISA /
subsystem oracles. Test scenes are **synthesized** via `Age.Engine/Sys4/ScriptAssembler` (see
[[testing-synthesize-dont-disable]]: synthesize test data, never disable a feature to keep a golden green).

The runtime SYS4 front-end is `Sys4AssetCatalog` (universal packed-id and name views; scene groupings are diagnostic only), `IAssetStore` /
`Sys4AssetStore` (exact-basename loose roots, then a bounded ALF range), and `Sys4ScriptProvider` (cached
root/call-script parsing). Generated asset/callscript JSON remains a tooling and test oracle only.

| Command | Purpose | Notes |
|---|---|---|
| `run <file.BIN>` | Execute a script; print steps, show-text count, **call-script dispatch count**, the first 30 lines (each tagged with its source script), and the distinct source scripts. | `CaptureHost` (headless); **executes call-script**. |
| `trace <out.json>` | Trace every SC/SP scene → offsets + halt + steps. **Provider-less** (call-script stubbed) = a base-ISA offset dump. | writes JSON. (Was the vm0 differential oracle; vm0 is retired from oracle duty — `TraceDiffTests` removed.) |
| `trace <SCENE.BIN> [--boot] [--state <f>] [0xADDR=VAL…] --trace-json <out>` | ★ Emit the **full per-op executed-offset path** of one scene (not just show-text), filtered to the scene's own frame — the VM side of the differential offset-path oracle (`diff_optrace.py`). `--boot` runs the SYSTEM4 state prefix; **`--state <f>` loads a captured scene-entry snapshot** (`capture_global_writes.py`) = the engine's real pre-scene state; `0xADDR=VAL` hand-seeds. | `JsonOffsetTraceSink` (observe-only, parity held) → `{scene, offsets:[…]}` JSON. |
| `audio <SCENE.BIN> [0xADDR=VAL…]` | Dump executed ordinary/forced/stopped BGM and `play-voice` in order + resolved catalog record; forced BGM events distinguish loop and one-shot mode. | optional seeds. provider-less (stub) for now. |
| `gfx [--boot] <SCENE.BIN> [0xADDR=VAL…]` | Dump executed `set-texture`/`get-texture-size`/`draw-texture` (resolved file + computed geometry) **plus the per-object gfx slots** — the headless geometry oracle. **`--boot`** runs SYSTEM4's state prefix (`INITCONFIG/INIT2/INIT`) via `GameSession` first (so INIT2's gfx handle array is present) and runs the target with call-script on; without it, seeds-only + provider-less. | gfx ops now execute against `GfxState`. |
| `play [--boot] [--state <f>] [--save-state <f>] <SCENE.BIN…> [0xADDR=VAL…]` | ★ Cross-scene **state runner**: run a scene sequence carrying persistent globals. `--boot` first runs the 9 `*INIT` data scripts (real skill/item/unit/map/stage state). `--state`/`--save-state` load/persist a JSON snapshot. | `GameSession`; **executes call-script**. |
| `sweep [--boot] [0xADDR=VAL…]` | Corpus-scale run. **With call-script execution on: 284/297 exit, 13 STEP-LIMIT** (input/state-gated ADV scenes spin headless once subroutine global-writes drive their loops — state divergence, not a bug; 0 depth-cap/unresolved). **With seeds = a story-state explorer**: reports which scenes' dialogue changes ±seed (e.g. form flag `0xa57=1` → 34/297 scenes). | |

**Faithful headless vs plow (`HaltAtWaitForInput`)** — headless has no player, so op `0x72 wait-for-input`
either **halts** ("the scene is waiting; with no input, stop here") or is ignored (**plow** — walk every page).
Plow is a *fiction*: it runs past every prompt into code no real playthrough reaches — e.g. a plowed
`SC0000` fell through 166 prompts into the name-entry poll loop and spun `sleep 1` **493k×** to STEP-LIMIT.
So: **`run`/`play` HALT at the first `wait-for-input` by default** (faithful; `SC0000` stops at ~402 steps,
0 sleeps — matching the real run's path to the first prompt), with **`--plow`** to opt into full-page
coverage. **`sweep` PLOWS by default** (it *is* the dialogue-coverage oracle: 284 exit / 13 STEP-LIMIT),
with **`--halt-at-wait`** to opt into faithful mode (then all 297 scenes halt cleanly at their first prompt
— 0 STEP-LIMIT). Interactive Godot is unaffected (it really blocks on input; flag stays false there).

**`--trace [--trace-file <path>] [--trace-steps]`** (on `run`/`play`/`sweep`): stream the engine's own
diagnostic events over the `Age.Engine.Diagnostics.ITraceSink` seam — scene/subroutine frame enter+exit
(indented by call depth), call-script dispatch with resolved name, and the final halt+step count — to
console or a file. Add `--trace-steps` for per-instruction opcode/arg + stub-op detail (high volume;
gated). Absent ⇒ no tracing (`NullTraceSink`, byte-identical run). This is an **engine** fact stream:
frontends consume it instead of reimplementing a diagnostic `IHost`. Example: `play SC0000.BIN --trace`
shows `» SC0000.BIN (enter, TopScene)` → `call-script 0xee =INPUTNAME.BIN (resolved)` → `halt: …`.

**Aggregating / filtered diagnostics** (added 2026-07-08 after a full `--trace-steps` dump proved unusable
at 2.5M lines). All observe-only → parity preserved; all on `run`/`play`/`sweep`:
- **`--trace-histogram`** — instead of a per-line dump, aggregate **execution counts per opcode** and per
  **call-site `(script:pc)`** (with a sample first operand), dumped sorted after the run. Answers "how many
  times did op X run, and from where?" directly. This is what pinpointed, in one line, that the 493,175
  `sleep`s in a headless `play` come from `INPUTNAME.BIN:0x1c3` — a name-entry input-poll loop that spins
  only because headless has no keyboard — not from the opening. Step lines are attributed to the **real
  running script** (nested call-script frames included), the "which script is this pc in?" answer a bare
  step trace can't give.
- **`--trace-ops <csv>`** — filter the text trace to only the named ops (mnemonics or `0x` hex, e.g.
  `--trace-ops sleep,draw-texture,wait-for-input`), each line tagged `script:pc`. The ordered interleaving of
  a few ops of interest without the flood.
- `--trace-file <path>` now creates the parent directory if missing.
- **Godot** accepts **`--trace-histogram <file>`** — profile the **real** run (headless flow diverges because
  `wait-for-input` is a no-op there; the real run to page 1 is ~562 steps with **0** sleeps vs headless's 2M
  steps / 493k sleeps). Dumped when the scene ends or the window closes. e.g.
  `godot --path godot -- --scene SC0000 --boot --shot out/p1.png --trace-histogram out/hist.txt`.

**Godot frontend** (Godot 4.7 .NET; project = `godot/`). Toolchain: `godot --headless --path godot
--import` → `dotnet build godot/Himegari.csproj` → `godot [--headless] --path godot [-- <userargs>]`.
Plays the real bytecode with call-script execution on (subroutines run live). The game root is selected
before catalog loading: `--game-root <directory>` wins, otherwise the directory containing the running
executable is tried, then the current working directory. A candidate must contain `SYS4INI.BIN`; invalid
explicit roots and unresolved defaults stop startup with the searched locations. Relative overrides are
resolved against the current working directory, and repeated overrides use the last value. On Unix,
Godot preserves the shell launch directory through inherited `PWD` because editor/project startup may
change the process directory before managed code runs.

**Linux x64 export** uses the committed `godot/export_presets.cfg` preset and `godot/Himegari.sln`:

```powershell
.\tools\export-linux-x64.ps1 -GodotConsole <path-to-Godot-4.7-.NET-console.exe>
```

Install the Godot 4.7 .NET export templates first and build the pinned native bundle under
`build/native/linux-x64` with the commands in “Native FFmpeg movie shim” above. The script safely replaces
only `build/export/linux-x64`, invokes the release preset with a bounded headless shutdown, verifies the
executable/PCK/self-contained managed payload plus all six project-owned FFmpeg files, and rejects the
Windows GDI adapter and Windows FFmpeg DLLs. Output is `build/export/linux-x64/Himegari.x86_64` with its
PCK and `data_Himegari_linuxbsd_x86_64/` runtime directory. Do not relocate only the executable; the three
items are one artifact.

The portable font policy requires a Japanese-capable system font. A minimal Ubuntu install needs, for
example, `sudo apt install fonts-noto-cjk`; absence is a startup failure rather than silent missing-glyph
output. The exported headless smoke command is:

```bash
./build/export/linux-x64/Himegari.x86_64 --headless -- \
  --selftest --game-root "/absolute/path/to/Himegari_Game" --text-backend portable
```

This validates the packaged .NET/Godot runtime, archive access, portable text realization, retained
rendering logic, audio payload parsing, and teardown. It does not validate a desktop window, physical
input, or audible output; run those gates on a real Linux desktop.

With the exported executable placed in an AGE install, a no-argument launch therefore uses that install
and starts the persistent `SYSTEM4.BIN` root naturally. Direct development runs are hosted by the Godot
editor executable, so pass `--game-root <install>` after Godot's `--` separator. The tracked
`run-godot.ps1`/`.cmd` launchers resolve Godot and the game root without machine-specific tracked paths.
Explicit `-GodotConsole`/`-GameRoot` values win, followed by `AGE_GODOT_CONSOLE`/`AGE_GAME_ROOT`; Godot
then falls back to `godot4`, `godot`, or `godot-mono` on `PATH`, while the game root falls back to the
conventional `../Himegari_Game` sibling only when it contains `SYS4INI.BIN`. `run-godot.ps1 -Doctor`
prints the resolved repository, Godot, game-root, .NET, and Python prerequisites without building or
launching. The launchers make the natural route explicit with
`--scene SYSTEM4` and pass neither `--boot` nor SC0000 seeds. Examples below focus on their
feature-specific arguments and assume the game root is already selected this way. `--headless` can't
render texture ops (no GPU context) — run windowed for real scenes.
On VM termination the console prints the exact halt reason and step count before the generic on-screen
`-end-` marker. User args (after `--`):

The local launchers also accept **`-StartupDiagnostics`** for a native-faithful cold boot through TITLE,
Game Start, and SC0000. The switch overrides `run-godot.cmd`'s usual `-NativeDebugMenu` convenience for
that run and adds no `--boot`, seeds, timing changes, screenshots, or input injection. It writes four
disposable files under `build/validation/title-newgame/`: `godot.log` (Godot warnings/errors),
`timeline.jsonl` (ordered VM/host/compositor evidence), `histogram.txt` (executed opcode and hot-call-site
counts), and `page-map.jsonl` (authoritative wait/text locations and call stack). After Godot closes, the
PowerShell launcher reports each artifact's byte size and warns if any was not flushed. Run
`run-godot.cmd -StartupDiagnostics`, choose Game Start normally, stop at the first stable SC0000 input
wait, optionally press F3, and close the window normally. Until shutdown cancellation is separated from
ADV input release, normal window close can append one trailing shutdown-only record to `page-map.jsonl`;
use the last `input-wait` event in `timeline.jsonl` as the authoritative manual stop coordinate.

- `--game-root <directory>` — select the read-only AGE installation supplying `SYS4INI.BIN`, loose
  overrides, AAI append catalogs, and ALF archives. Absolute paths are launcher-friendly; relative paths
  are current-working-directory-relative. This is the stable handoff for future game-profile launchers.
- `--scene <NAME>` — override the default `SYSTEM4` root with a direct diagnostic scene, e.g. `--scene SC0240` (executes 29 nested subroutines).
- `--selftest` — headless; runs a **synthesized** scene through the thread/suspend/`CallDeferred` plumbing and asserts it matches a live headless run (full handling; no vm0/frozen golden). Exits.
- `--text-backend auto|gdi|portable` — select gameplay glyph-mask rasterization. `auto` (default) uses the
  exact GDI gray-4 backend only when the Windows ACP-932 gate passes, otherwise the explicitly non-identical
  Godot TextServer Unicode policy. `gdi` fails startup when exact selection is unavailable; `portable`
  never initializes GDI and is the cross-platform acceptance/debug mode. The selected backend and portable
  policy id are printed at startup.
- `--window-width <pixels>` / `--window-height <pixels>` — override only the physical windowed client
  dimensions; each omitted dimension defaults independently to the SYS4INI logical canvas. Godot keeps
  that logical canvas and aspect-preserving letterboxing, so these options do not change VM coordinates,
  AGE surfaces, layout, or backbuffer allocation. Values must be integers in `1..16384`; duplicate
  options use the last value.
- `--seed 0xADDR=VAL` (repeatable) — seed initial global state, e.g. `--seed 0xa57=1` unlocks Lily's form-A voiced dialogue.
- `--boot` — direct-scene diagnostic only: with `--scene <non-SYSTEM4>`, run the old
  `INITCONFIG/INIT2/INIT` state prefix before that isolated scene. The normal SYSTEM4-rooted launch neither
  needs nor applies it. e.g. `godot --path godot -- --scene SC0000 --boot`.
- `--native-debug-menu` — intentionally treat exit-request opcode `0x1` as a no-op, exposing TITLE's
  otherwise unreachable shipped developer menu. This is a non-native debug divergence and also suppresses
  SYSTEM4's invalid-execution-mode exit request for that run. Direct Godot and PowerShell launches leave it
  disabled unless explicitly requested. The local `run-godot.cmd` convenience launcher enables it by
  default; use `run-godot.ps1` without `-NativeDebugMenu` for a native-faithful launch.
- `--shot <png> [--shot-page N]` — capture page N to a PNG then quit (dev screenshot). At scene end it also prints the call-scripts executed as nested frames.
- `--shot-sequence <dir> [--frames N]` — dump one PNG per rendered frame (`frame_0000.png…`, default N=180 ≈ 3s @60fps) then quit, auto-advancing past input waits. Verifies time-based retained effects and publication boundaries as distinct frames, which a single `--shot` cannot. CPU/IO-heavy by design (a PNG every frame); a dev diagnostic, not a normal run. e.g. `godot --path godot -- --scene SC0000 --boot --shot-sequence out/seq --frames 300`.
- `--sleep-scale <f>` — multiply every explicit `sleep` (op 0xc8) duration by `f` (default 1.0). This stretches only script-authored sleep holds; it does not slow ordinary opcode bursts or replace `0x20c`/`0x21c` presentation pacing. Debug-only; leave at 1.0 for real playback.
- `--speed <f>` — scale sleeps and retained presentation clocks without throttling ordinary opcode bursts or auto-advancing input waits. Values 0.05–8 are accepted; `--speed 0.25` is useful for transform inspection, while 1.0 is normal playback.
- `--gfx-log <file>` — **compositor + op diagnostic** (the tool that root-caused the grey background). Logs, per rendered frame, only the objects whose draw outcome **CHANGED** (drawn↔skip↔gone, resId, resolved file, `slot`, `src`/`dst`, `op`acity, `tintStr`ength) — quiet until something actually changes, so the exact frame a layer drops out (and why) stands out. Also traces every `set-texture`/`create-texture` **slot assignment** (via `GodotAdvHost.TraceOps`). Works live or with `--shot-sequence`. Use it before theorising about layering/blend/geometry: it showed the grey BG = the slot-selecting globals resolving to 0 → every texture collapsing into slot 0 (see engine-re.md §"Grey-background root cause"). e.g. `godot --path godot -- --scene SC0000 --boot --gfx-log out/gfx.log` then click to the bad page.
  Matrix-channel outcomes also include `base`, `anchor`, projected `dst`, sampled `scale`/`trans`, and
  one-shot-plus-cyclic `rot`ation angles. Active op-`0x202` outcomes include packed `color=current->target`
  and `colorProgress`, synchronized with the same frame/clock in `--timeline-log`. Parent directories are
  created automatically.
- `--transition-click-ms <n>` — diagnostic-only input injector: after a foreground transition has been active for `n` virtual milliseconds, send one click through the real input lifecycle. The click completes/consumes the transition and does not advance a stable page. Use with `--timeline-log`, `--gfx-log`, and windowed `--shot-sequence`; omit for normal play.
- `--hold-message-skip` — diagnostic-only held-input injector: hold Ctrl through the VM's real keyboard
  binding path, producing native logical action 6 continuously. Use it to compare persistent fast-forward
  against `--transition-click-ms` at the same page without relying on synthetic host state.
- `--timeline-log <jsonl>` — diagnostic-only synchronized event stream for a real Godot run. Records every
  executed script byte offset/opcode, script-frame entry/exit with depth/cause/outcome, resolved call-script
  ids/names, unknown-fallback `stub` events, virtual time/frame, VM state changes (`running`, `sleep`, `input-wait`,
  `halted`), audio events, and changed visible-object compositor outcomes in one ordered JSONL file. Combine with
  `--scene SC0000 --boot --shot-sequence ... --gfx-log ...` to distinguish control-flow stalls from retained-object/compositor
  failures at an exact bytecode boundary. Relative output paths are project-relative (`godot/`).

**Godot performance frame log:** `--perf-log <csv>` enables a buffered, diagnostic-only capture of the real
Godot path. Each row carries VM `script`/`offset`/`opcode` at frame entry and again at the actual presentation
boundary (`present_*`), Godot `delta_ms`, measured main-loop
time, pulse/movie/UI time, and a compositor breakdown: recomposition, clear, retained snapshot, texture
resolution/decode, color-key/source preparation, raster, `Image.SetData`, and `ImageTexture.Update`. Workload columns report transition
state, retained object visits, time-varying object visits, drawn/fill/transition/skipped layers, integer/
affine/singular raster paths, dynamic/opaque/alpha/additive layers, source pixels, clipped affine bounding-
box candidate pixels, full-screen layers, main-thread managed allocations, and GC collection deltas.
Presentation-reason columns distinguish host publication requests, legacy screen transitions, retained VM
mutations, continuously sampled channels, and discrete spritesheet cell changes. Affine work is additionally
split into fractional translation, axis-aligned scale, and general affine layers while retaining the aggregate
`affine_layers` column for comparison with the first two captures. The
allocation total is split into recomposition, retained snapshot, compositor, source-preparation,
`Image.SetData`, and Godot UI phases so a normal windowed capture can attribute remaining managed garbage
without enabling a high-volume trace. The
`render_backend` column is `0` for the software compositor and `1` for the opt-in retained GPU path;
`gpu_draw_items`, `gpu_texture_uploads`, and `gpu_texture_upload_ms` distinguish cheap retained-item updates
from new/static or changed/dynamic texture publication. Existing layer/transform/pixel workload columns are
populated for either backend, while `raster_ms`, `set_data_ms`, and `texture_update_ms` remain zero on a native
GPU presentation. GPU retained rendering is the normal backend; select the correctness oracle explicitly
with `--render-backend software` or the local launcher's `-SoftwareRenderer` switch. An explicit
`--render-backend gpu` is accepted but normally unnecessary.

The writer replaces the target, buffers 120 rows between flushes, and prints its frame/recomposition counts on
normal shutdown. Use a windowed Release-equivalent run at speed 1 for performance evidence; headless runs
validate the schema only. Before a baseline, verify that no older Godot game processes remain alive; an
apparently closed window can otherwise leave a renderer consuming CPU and contaminate later runs. Do not
combine baseline captures with `--shot-sequence`, `--gfx-log`, or
`--timeline-log`, whose diagnostics add substantial or differently shaped work. Example from `age-reimpl/`:
`godot --path godot -- --scene SC0000 --boot --perf-log ../build/perf/sc0000.csv`.

`run-godot.ps1 -PerfLog` creates `build/perf/run-yyyyMMdd-HHmmss-fff.csv` and prints the absolute target
before Godot starts; normal shutdown flushes it and prints the captured frame/recomposition counts. The
performance effort's temporary `run-godot.cmd` opt-in was removed after GPU acceptance, so ordinary batch-file
runs no longer write a log. Selftests also remain unprofiled.

**Godot debug scene launcher:** press **F4** while the natural boot is showing TITLE. TITLE's visible menu is
a live 1 ms sleep/input-poll loop rather than an ADV `wait-for-input`; the launcher identifies that exact
active child frame and returns it cooperatively at the next completed opcode boundary. The overlay enumerates
all base and mounted-append `.BIN` records by packed id, with All/SC/SP/Debug/Other filters, name or exact
hexadecimal/decimal id search, and archive/raw-id details. `SYSTEM4.BIN` and `TITLE.BIN` are intentionally
unlaunchable. Launch is accepted only for the exact `SYSTEM4.BIN > TITLE.BIN` wait stack; it returns TITLE
with the coordinator globals queued on the VM thread, then lets SYSTEM4 perform its normal computed child
call. F4 outside TITLE prints an unavailable reason and changes no state. Cancel or Escape closes the panel.
The launcher does not seed story/profile state, jump to byte offsets, or force-switch an active child scene.

**Godot page locator:** every editor/development run recreates `build/page-map-<SCENE>.jsonl`; a packaged
export writes the same automatic map below `user://diagnostics/page-maps`. Each `wait-for-input` adds the
run-relative page, page-start location, canonical wait script/offset, last show-text instruction and string
offsets, text, and nested call stack. Use `--page-map <jsonl>` to override the output; pass that explicit path
to `locate_page.py --map` when resolving a map copied from an export profile. `--locator-hud` shows
`SC0000 P014 · wait SC0000@0x… · text SC0000@0x…` at launch; **F2**
toggles it and **F3** copies the current locator to the clipboard. The offset remains authoritative because
branching/state can shift page ordinals between runs. Resolve a reported page with
`py -3.11 -X utf8 tools/locate_page.py SC0000 14`.

## Asset resolution / graphics

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `tools/frida/capture_native_transforms.py` | Capture native `0x21f`/`0x223`/`0x234` worker operands, corrected integer base/anchor coordinates, all one-shot/cyclic retained fields, the one-shot 4×4 matrix, and the final post-cyclic 4×4 matrix. Optional handle filter; read-only. | `py -3.11 -u -X utf8 tools/frida/capture_native_transforms.py [secs] [pid|AGE.EXE] [--handle 0xHANDLE]` | running game → `build/native-transform-trace.jsonl` |
| `parse_sys4ini.py` | Parse `SYS4INI.BIN` (S4IC422, LZSS-compressed) into the diagnostic JSON asset-index mirror — name ↔ archive ↔ offset ↔ size for all DATA*.ALF. Each real entry carries universal `raw_index`; the runtime parses SYS4INI itself, while these generated files remain tooling/test oracles. Also emits the `call-script <id> → name` annotation map. | `parse_sys4ini.py [--check]` (`--check` validates vs `extracted/` + `.ALF` sizes) | `姫狩り…/SYS4INI.BIN` → `build/asset-index.json` + `build/callscript-names.json` |
| `resolve_asset.py` | **Historical scene-group correlation diagnostic, not a runtime resolver.** Builds/queries the strong `file_number ≈ position − group_start` relationship that helped classify SYS4INI ordering. Native RE proves bytecode resources are already universal packed ids, so do not feed this tool's scene-relative result to runtime lookup. | `resolve_asset.py --build` · `resolve_asset.py <SCENE> [resId]` | `build/asset-index.json` → `build/asset-sections.json`; inspects inferred groups |
| `resolve_frida_reads.py` | Rescue noisy Frida archive-read offsets → asset names via the index (per-archive range search; drops 0x20000 paging reads); recovers the per-scene asset load order. | `resolve_frida_reads.py [reads.log] [-o out.json]` | `build/frida-reads.log` + `build/asset-index.json` → `build/frida-asset-loads.json` |
| `convert_agf.py` | Convert AGF stills to BMP via `AGF2BMP2AGF.exe` (searches all `extracted/DATA*`). `--scene` batch-converts a scene's whole SYS4INI manifest. Since VFS-C, output is a diagnostic pixel-parity oracle; the runtime decodes AGF bytes directly. | `convert_agf.py EV052CA.AGF …` · `convert_agf.py --scene SC0000` | `extracted/DATA*/*.AGF` → `build/textures/*.BMP` |

## Runtime capture (Frida)

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `tools/frida/capture_graphics.py` | Attach Frida to the running game; log archive reads/opens (ground-truth for asset resolution). See `tools/frida/README.md`. | `py -3.11 -u -X utf8 tools/frida/capture_graphics.py [AGE.EXE]` | running game → `build/frida-reads.log`, `build/frida-opens.log` |
| `tools/frida/capture_load_order.py` | **Primary asset-resolution capture:** recover a scene's per-asset load order from exact-start `ReadFile` reads → names via the index; confirms `resId==file_number`. Attach; replay scene; `--analyze`. | `py -3.11 -u -X utf8 tools/frida/capture_load_order.py [pid]` · `--analyze` | running game + index → `build/frida-load-order.jsonl`, `…-result.json` |
| `tools/frida/locate_resource_load.py` | Phase-1 locator: back-traces asset-opens to find the native AGF load chain (`0x16d5d7→0x74f1f`). | `py -3.11 -u -X utf8 tools/frida/locate_resource_load.py [pid]` · `--aggregate` | running game → `build/frida-resource-bt.jsonl` |
| `tools/frida/capture_resid_args.py` | Phase-2 probe: dumps the decoder's args / context / caller frame (established the loader carries only offsets, not names). | `py -3.11 -u -X utf8 tools/frida/capture_resid_args.py [pid]` · `--analyze` | running game → `build/frida-resid-args.jsonl` |
| `tools/frida/find_globals_base.py` | Runtime-global RE (SHELVED — see `docs/global-memory-re.md`): flat-int32 signature scan for the VM global array. Finds nothing → layout isn't flat. | `--build-sig` · `py -3.11 -u -X utf8 tools/frida/find_globals_base.py [pid]` | `*INIT` → `build/globals-signature.json`; scans running game |
| `tools/frida/find_global_by_sequence.py` | Runtime-global RE (SHELVED): differential resId value-scan + stability filter. Finds stack proxies; proved `G[0x62424]` is a transient arg-register. | `py -3.11 -u -X utf8 tools/frida/find_global_by_sequence.py [pid]` | running game + index → stdout |
| `tools/frida/dump_engine.py` | ★ **Dump the UNPACKED engine code** from the live process for offline static RE (native handlers). `AGE.EXE` unpacks in-place at `0x400000`; Kelebek VAs map `VA−0x400000` = file-off. Validated via the AGF-decoder landmark `+0x74f1f`. | `py -3.11 -u -X utf8 tools/frida/dump_engine.py [pid]` | running game → `build/engine-dump/{manifest.json,range_<base>.bin}` |
| `tools/frida/map_imports.py` (+ `map_imports_full.py`) | ★ **Name dynamically-resolved Win32 APIs** in the Ghidra image. Read-only: maps live-process module exports → `{addr→dll!Func}`, scans the `0x400000` module for pointer matches → `RVA→name` (ASLR-stable). `--recon` = clustering report (the gate); default writes the map. Applied to `/v2` via a `run_script_inline` pass → 248 `imp_<dll>_<func>` labels at the RVA `0x16f000` IAT (validated: CreateFileA/SetFilePointer/timeGetTime). Pure scan/cluster logic unit-tested (`test_map_imports.py`). | `py -3.11 -u -X utf8 tools/frida/map_imports.py [--recon]` | running game → `build/import-map.json` (+ `-singletons.json`) |
| `tools/frida/probe_handlers.py` | Probe which region the interpreter executes from (module vs heap). Confirmed: **operand-fetch `+0x1b940` fires ~8500/s ⇒ interpreter runs from the module `0x400000`** (handlers hookable by dump address). | `py -3.11 -u -X utf8 tools/frida/probe_handlers.py [pid]` | running game → stdout (per-hook fire counts) |
| `tools/frida/capture_gfx_objects.py` | Legacy/misnamed probe: grab engine ctx (`esi` via operand-fetch `ecx`) and poll `[esi+0x53d64]`. Ghidra later proved these are 20×120-byte **script-context records**, with the current instruction length at record `+0x24`, not gfx objects or command types. Its former "0 CG records ⇒ drift is state-divergence" conclusion is invalid; op 0x215 queries the separate retained-object map described in `docs/engine-re.md`. Kept only for raw runtime context observation and historical reproducibility. | `py -3.11 -u -X utf8 tools/frida/capture_gfx_objects.py [pid] [secs]` | running game → `build/gfx-objects.jsonl` |
| `tools/frida/probe_frame_cadence.py` | **Frame-cadence probe** (`docs/engine-re.md` "Frame cadence — live measurement"): plain-JS hook on operand-fetch `0x41b940` (grab ctx + count operand reads) + system-DLL message/timing hooks; auto-buckets by Ctrl/skip-bit. Measured ~1,788 **operand fetches/sec** normal, ~4× fast-forward; this is not an opcode count. **Read-only/import-only — never CModule-hook the hot interpreter (crashes the game).** Play actively during capture; hold Ctrl the back half. | `py -3.11 -u -X utf8 tools/frida/probe_frame_cadence.py [secs] [proc]` | running game → `build/frida-frame-cadence.jsonl` + stdout report |
| `tools/frida/probe_present.py` | **Present-rate probe:** grab ctx, scan it for the D3D9 device (d3d9-vtable object with a full ~119-method table), hook `IDirect3DDevice9::Present`/`EndScene` (+ GDI-blit fallback). Found: **D3D9, UNCAPPED** (`Present` ~1908/sec, no vsync; no `ddraw`; 2D StretchRect compositor) ⇒ no fixed frame rate. Click 2–3× at start to grab ctx. | `py -3.11 -u -X utf8 tools/frida/probe_present.py [secs]` | running game → `build/frida-present.jsonl` + stdout report |
| `tools/frida/trace_engine_ops.py` | **Engine op-path tracer** for the differential oracle (`docs/engine-re.md` "Differential offset-path oracle"): per executed op, read `cur_ctx_index@0x53d14`/`frame_pc@0x53d2c`/`frame_codebase@0x53d28` → emit `(codebase, offset=(pc−codebase)/4)`. **Use `--hook operand` (0x41b940, proven-safe)** — `--hook tick` (0x410fb0) sees `ecx≠ctx` (0 entries). Writes `build/tracer-live.flag` when the hook is installed → launch in the background, gate the New-Game trigger on the flag (else the scene-entry burst is missed). | `py -3.11 -u -X utf8 tools/frida/trace_engine_ops.py [--hook operand\|tick] [secs]` | running game → `build/engine-optrace.jsonl` |
| `tools/frida/capture_global_writes.py` | **Scene-entry state capture** → auto-seed for single-scene runs (`docs/engine-re.md` "Scene-entry state snapshot"). Hooks `vm_operand_write@0x425fb0` and logs `(codebase, index, PLAINTEXT value)` for global-ints (the helper sees the value before the obfuscated store — no de-obfuscation needed). **`--spawn` captures from boot** (packer-aware: polls until `0x425fb0` unpacks, then attaches; kills the spawned pid on setup failure so no suspended orphan). `--attach` = partial (misses pre-attach writes). Validated: a real boot→New-Game→SC0000 capture seeds the VM to match the engine's whole opening. | `py -3.11 -u -X utf8 tools/frida/capture_global_writes.py --spawn [secs]` | running/spawned game → `build/global-writes.jsonl` (raw) + `build/scene-entry-state.json` (GameSession snapshot) |

| `tools/frida/capture_presentation_trace.py` | **Retained-state presentation trace:** correlates the current script offset with native draw/color writes, object composition, surface-command consumption, `gfx_render_frame`, queue clear, and D3D9 Present count. Read-only; distinguishes live retained state from state actually published to the window. | `py -3.11 -u -X utf8 tools/frida/capture_presentation_trace.py [secs] [pid\|AGE.EXE]` | native game → `build/native-presentation-trace.jsonl` |
| `tools/frida/capture_adv_text_trace.py` | **ADV text trace:** correlates SC offsets with op `0x6e`/`0x7a`/`0x204`, layout cursor/origin and 20-byte record counts, CP932 strings, surface draw/bind, and timed glyph-record publication. Read-only and deliberately limited to low-frequency known handlers; the first experimental version's D3D scan plus hot per-glyph/render hooks crashed in `frida-agent.dll` during teardown and was removed. | `py -3.11 -u -X utf8 tools/frida/capture_adv_text_trace.py [secs] [pid\|AGE.EXE]` | native game → `build/native-adv-text-trace.jsonl` |
| `tools/frida/capture_sfx_trace.py` | **SFX/DirectSound trace:** correlates SC offsets with `0xb4`/`0xb5`/`0xb6`/`0xc2`/`0xd9`, resource/channel load/start/release, decoder/buffer state, BGM fade ticks, and dynamically discovered DirectSound `Play`/`Stop`/volume/pan calls. Read-only. | `py -3.11 -u -X utf8 tools/frida/capture_sfx_trace.py [secs] [pid\|AGE.EXE]` | native game → `build/native-sfx-trace.jsonl` |

*(Static disassembly of `build/engine-dump/range_00400000.bin` uses **capstone** — `py -3.11 -m pip install capstone`; VA `X` → file offset `X−0x400000`.)*

### B0 script-load capture

`tools/frida/capture_script_loads.py` attaches at TITLE, hooks
`script_frame_load_resource@0x40e980`, and resolves each raw SYS4 resource id to a `.BIN` name and parent
frame in `build/script-loads.jsonl`. Launch the game normally, then run
`py -3.11 -u -X utf8 tools/frida/capture_script_loads.py [secs]` before selecting New Game. Use
`--analyze` to print an existing log and `--selftest` for its pure resolver checks. It is deliberately
attach-only: a process-start loader-hook trial triggered Protection Error 45, so the tool does not spawn
or bypass protection.

## Native engine RE (Ghidra)

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `ghidra_handler_map.py` | Extract the opcode→real-handler dispatch table (`handler(op)=ctx[0x26c93+op]`) from `FUN_00413860`'s override stores — the general fix for Kelebek VA-drift. `--check` diffs derived handlers vs `opcodes.toml` prose (found 0 real drift). Feeds the one-shot Ghidra annotation pass that names every handler `op_0xNN_handler` (see `docs/engine-re.md` "Materialized + applied image-wide"). | `ghidra_handler_map.py build/engine-dump/FUN_00413860.disasm.txt [--check]` | ⚙ `build/engine-dump/FUN_00413860.disasm.txt` (from ghidra-mcp `disassemble_function(0x413860)`) → ⚙ `build/op-handler-map.json` |
| `test_ghidra_handler_map.py` | Unit tests for the dispatch-table parser (plain runner, no pytest). | `test_ghidra_handler_map.py` | — |
| `engine_ctx_build.py` | Build the `EngineCtx` struct artifacts from `vm-map/engine-ctx.toml` (canonical `ctx`-field registry). `--lint` = overlap/OOB/dup/type checks. The struct is then applied to the `/v2` image via `run_script_inline` (creates `EngineCtx`, retypes all dispatch-handler `this` → `EngineCtx *`) so handlers decompile `ctx->field` not `param_1+0x…`. Grows one `[[field]]` at a time. | `engine_ctx_build.py --build` · `--lint` | ⚙ `vm-map/engine-ctx.toml` → ⚙ `build/engine-ctx.json`, ⚙ `docs/engine-ctx-reference.md` |
| `test_engine_ctx.py` | Unit tests for the ctx builder (load/lint/emit; plain runner). | `test_engine_ctx.py` | — |

## Tooling improvement backlog

Prioritized from the 2026-07-10 Phase-A retrospective. These are proposed capabilities, not tools that
already exist. Prefer additions that produce reusable, offset-keyed evidence over one-off capture scripts.

| Priority | Improvement | Payoff / acceptance boundary |
|---|---|---|
| P0 | **Native/port checkpoint harness** | Launch either runtime windowed, identify the process/window, record script offset + timestamp, capture a screenshot and retained-state/trace excerpt, and classify normal exit vs harness stop vs crash. A checkpoint should produce one comparable artifact bundle for the same bytecode offset in both runtimes. |
| P0 | **Sanctioned Windows GUI automation** (small Win32 helper, AutoHotkey, or pywinauto) | Repeatably focus the game, capture window geometry, and inject only explicitly requested clicks/keys. Manual input remains the independent oracle; automation removes capture/setup repetition. |
| P0 | **Scriptable 32-bit debugger** (x32dbg or WinDbg) | Breakpoints and memory snapshots for hot render/audio workers that are unsafe to hook densely with Frida. Keep Frida probes on known low-frequency handlers; never restore hot interpreter/glyph/render hooks merely for convenience. |
| P1 | **Timestamped video capture** (ffmpeg desktop capture or command-controlled OBS) | Frame-by-frame native/port evidence for movie `0x236`, fades, and short animation boundaries without PNG-per-frame overhead. |
| P1 | **WASAPI loopback/audio capture** | Objective SFX/BGM/voice start time, channel reuse, volume, stop, and waveform comparison. This is the main evidence upgrade for the pending SFX slice. |
| DONE 2026-08-02 | **One-command validation driver** | Landed as `validate.py` with strict layered levels, per-gate logs/timeouts, generated-reference checks, Python/.NET/corpus/Godot gates, the faithful-wait sweep, whitespace checking, and a Godot-process leak audit. Canonical usage is under "Project validation" above. |
| P1 | **Golden SC0000 checkpoint corpus** | Preserve the first 10-15 native pages as offset-keyed screenshots, click/wait events, retained-state summaries, and trace excerpts. Port regressions should be comparable without replaying the entire investigation. |
| P2 | **Opcode dossier generator** | Combine corpus callsites/operands, native handler/worker addresses, runtime samples, Ghidra names, opcode provenance, and port coverage into a per-op investigation packet. |
| P2 | **More typed Ghidra state** | Materialize retained-object, surface-slot, text-layout, and audio-channel structures so related handlers decompile against shared named fields. |
| P2 | **Narrow write access for cross-session memory** | Let mandatory milestone closeout update `MEMORY.md` and `himegari-port-status.md` directly; the current out-of-workspace copy/approval path adds avoidable handoff friction. |

## Historical / one-off

| Tool | Purpose |
|---|---|
| `probe_*.py` (`probe_header`, `probe_leads`, `probe_refs`, `probe_tables`, `probe_tags`, `probe_types`, `probe_xref`) | Container/opcode format-RE probes used to reverse the format originally. Kept for reproducibility; not part of the normal workflow. |
| `pack_check.py` | Checks whether `AGE.EXE` is packed (it is: entropy-8 code sections, zeroed IAT). `SYS4AB.BIN` is NOT a separate image — it's `XOR-0xFF(AGE.EXE)` byte-for-byte (0x2c header + XOR payload). The unpacked engine exists only in memory → dump it with `frida/dump_engine.py`. |

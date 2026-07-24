# DATA1 Script Inventory — 481 `.BIN` files

All scripts share the magic header `SYS4422 ` (8 bytes), confirming a uniform SYS4
bytecode format (engine version 4.4.2.2) across the entire set. Header is followed
by what appear to be little-endian u32 fields (version/section table — to be mapped
during disassembler work).

Source: `extracted\DATA1\` (extracted from `DATA1.ALF`).

**Patch overrides (runtime re-counted 2026-07-11):** 49 loose `.BIN` scripts sit in the game root and
shadow DATA1 counterparts at runtime (sizes differ slightly — e.g. `FIELD.BIN` root 200,536 vs archive
200,224). These v1.03 / append-patch versions are **authoritative**. Two additional engine BINs exist only
in the root: `SYS4INI.BIN` (272 KB) and `SYS4AB.BIN` (1.08 MB), for 51 root BINs total. The earlier count of
52 shadowing scripts conflated this inventory and was not reproducible; VFS-A enumerates and byte-checks all
49 real catalog/name intersections.

---

## Breakdown by series

| Series | Count | Total size | Role (inferred) |
|---|---|---|---|
| `SC####` | 136 | 15.6 MB | Scenario/event scripts (numbered 0000–1690, step 10) |
| `SP####` | 163 | 10.6 MB | Secondary scene series (0010–1369; incl. `SP0051A/B` split) — likely character/H-events |
| Named scripts | 175 | ~7.5 MB | Engine subsystems, data tables, UI, battle logic |
| `DEBUG*` | 5 | 1.4 MB | Debug tools (`DEBUGADV.BIN` alone is 1.4 MB — a scene viewer/jump menu) |
| `RTN_M###` / `RTN_B###` | 26 | ~70 KB | Small routine scripts (map routines M001–M061, battle routines B001–B004) |

### SC series notes
- Numbered `SC0000`–`SC0880` (main chapters, largest files — up to 700 KB) and
  `SC1000`–`SC1690` (smaller; likely sub-events, endings, appendix content).
- `SCJUMP.BIN` (778 KB) is almost certainly the master scene-dispatch/jump table.
- `SCINIT.BIN` (88 KB) initializes the SCJUMP decision-to-scene-script registry and authored chapter tags.

---

## Named scripts by subsystem (inferred from names)

### Engine core / boot flow
`SYSTEM4.BIN` (config root), `INIT.BIN` (64 bytes — smallest script, ideal first
disassembly target), `INITCONFIG`, `LOADCONFIG`, `CONFIG`, `TUNE`, `LOGO`, `OP`,
`ED`, `TITLE`, `GAMESTART`, `GAMECLEAR`, `STAGECLEAR`

### INIT data and rule scripts
Large, table-like scripts — prime candidates for data extraction:
- `STINIT` (579 KB) — stages/scenario tables
- `EBINIT` (338 KB) — enemy battle data
- `MPINIT` (330 KB) — maps
- `SCINIT` (88 KB), `CGINIT` (79 KB — CG gallery), `ITINIT` (70 KB — items),
  `RTINIT` (67 KB — movement/battle routine-step banks), `CCINIT` (41 KB — class-change rule program),
  `SKINIT` (37 KB — skills), `CDINIT` (31 KB — weighted card-generation lists), `CDINIT2`
  (card definitions and effects), `BTANINIT` (105 KB — battle-effect definitions), `BTANINIT2`
  (six-slot battle-animation timelines)
- Smaller: `AFINIT`, `ALINIT`, `CIINIT`, `CNINIT`, `CTINIT`, `CVINIT`, `ILINIT`,
  `LAINIT`, `MAINIT`, `OBINIT` (46 object definitions), `SPINIT`, `TRINIT`, `VIINIT`

**Extracted-table correction and semantic pilot (2026-07-22).** Name-mode INIT tables use sparse,
one-based runtime ids over fixed reserved array spans, not a new record whenever the string destination
decreases. The corrected extractor finds SKINIT 131 skills (span 300), ITINIT 287 items (span 1000), and
EBINIT 277 units (span 1000). In particular, the earlier ITINIT JSON's 189 records were invalid: it collapsed
items 1–101 into the first record and treated consecutive item names as descriptions. Generated table state
and counts are indexed in `build/data/README.md`; field semantics are curated in `vm-map/globals.toml` and
described by the workflow in `docs/name-resolution.md`.

The same audit found that linked row-major writes must not be normalized as independent parallel arrays.
Corpus `lookup-array-2d` bases and strides assign every such ITINIT write unambiguously to six tables (44
populated columns), while SKINIT and EBINIT expose 18 and 82 linked columns respectively. The extractor also
preserves the scripts' zero-minus-immediate negative writes, which carry item penalties, condition cures, and
skill SP costs. Generated records keep linked cells under `record_fields[base/stride/column]`; `fields`
contains only genuine parallel arrays.

STINIT uses the separate mixed shape: 74 sparse `scjump_progress_a` branches load one current-stage work
buffer rather than parallel per-id arrays. Its generated records preserve four mission-condition strings,
six scalars, cells in 29 preallocated buffers, and all 1,396 length-prefixed footer-array copies. Confirmed
stage field meanings and the evidence workflow live in `docs/name-resolution.md`. The first semantic join
now assembles the confirmed type/X/Y/difficulty and story-prerequisite buffers into 2,312 object placements
across 66 stages. It also exposes 604 reinforcement schedules and consumer-proven tagged payload variants
for initial factions, teleport destinations, treasure item/count pairs, card-generation lists, and 104
hazard/barrier non-triggering faction gates. Every placement joins its type name and, where populated, its
effect description from OBINIT's 46 definitions. OBINIT's state-row mode resolves another 78 initial
object states on types 11, 17, and 26. The last three populated tagged writes, all type 27, are preserved
as ignored authoring data after FIELD's dedicated spawn path proves it never reads them. A second join
assembles all 1,378 populated enemy slots
across the same 66 stages, including unit/faction, placement, difficulty/story gates, level rules, weighted
alternatives, difficulty-specific movement/battle routine sets, and 485 first-clear-only replay gates.

CCINIT is the important exception to the static-table heuristic. Its 71 source-ordered branches are
class-change rules over 33 EBINIT unit ids, not address-indexed database rows. CALCCC evaluates CCINIT
through a 32-script provider list and applies the selected rule's title, deployment-cost delta, fourteen
stat bonuses, skill awards, and persistent applied-state slot; EVOLVE directly queries the two untitled
Lily form rules for movement preview. `extract_init.py` emits this as `rules` mode with raw guards and
output addresses joined to EBINIT unit names, SKINIT skill names, and the curated class-change ABI.

SCINIT is a sixth paired-dispatch shape. Its 2,179 alternating writes populate a sparse decision-indexed
script-resource array at `0x87a57` and authored-chapter array at `0x8a167`, leaving 1,209 final rows.
All 135 packed resource ids resolve to numbered SC scripts. The generated view preserves 710 overwritten
decision ids and joins every one of SCJUMP's 847 live decision ids; 844 final chapter tags agree, while
three legacy/stale mismatches remain explicit.

CGINIT's generic numeric parse formerly reported 379 records and 1,304 address-derived fields; that was
another ownership artifact. Its 3,941 writes actually define 851 sparse gallery-image ids in a reserved
2,000-row layout: a full-size image asset, an optional 112-by-84 SAVE/SELSTAGE preview, one of four
`SO026A`--`SO026D` thumbnail sheets, one of the sheet's thirty atlas cells, and a one-based variant
ordinal behind that thumbnail. CGMODE supplies the grouping and ordering evidence and consumes every
record; the dedicated extraction joins all asset ids to filenames while retaining the raw arrays.

ALINIT's generic numeric parse likewise collapsed 914 writes into 18 malformed records and 853
address-derived fields. ALCHEMY proves that they are 107 sparse recipe ids in a reserved 1,000-row
layout, backed by output item, minimum level, required/forbidden story flags, point cost, and four paired
ingredient-id/quantity slots. The dedicated extraction classifies every write, joins all 107 output
items and 286 ingredient references to ITINIT, and retains the seven raw table coordinates.

AFINIT is an affinity and progression initializer, not a name-record table. Its 27 strings form sparse
attack- and defense-element vocabularies; its 54 footer copies form a signed `20 × 20` effectiveness
matrix (thirteen by eighteen cells authored), eighteen usable item-tuning bonus/cost curves plus a
reserved zero row, and equipment-tuning/alchemy/magic facility progression thresholds. CTINIT is
INPUTNAME's separate `5 × 70` character palette: 273 authored hiragana, katakana, Latin, numeral, and
symbol cells with all reserved gaps retained.

CVINIT is CONFIG's character-voice control registry. Its 37 writes form thirteen preview-voice asset
slots, a twelve-entry setting-slot-to-EBINIT-unit map, and the exact inverse unit-to-setting map used
after CNINIT's voice-family normalization. Slot 0 is the non-unit system voice; slots 1..12 join to
Lily and eleven other named characters. CONFIG uses persisted per-unit speaker-seen flags to reveal those
twelve rows, plays the corresponding preview clip, and writes the selected suppression setting.

MPINIT is one sparse stage-terrain atlas rather than 1,472 independent map records. Each footer copy
writes fifty authored cells starting at column 1 of a 53-cell row; destination arithmetic yields
doubled-coordinate grid rows Y=2..1600 with 127 omitted all-zero rows. FIELD doubles STINIT2's inclusive
tile bounds and copies the selected atlas rectangle into a mutable `2000 × 53` current-stage grid.
The dedicated view joins 66 named stage definitions to 53 unique rectangles, preserves eight shared-map
groups and 47 nonzero border-context cells, and resolves terrain ids through LAINIT's name, texture-slot,
area-fill, and layout-class columns.

LAINIT defines the twenty shipped terrain ids inside a reserved thirty-row registry. Its sparse
strings provide seventeen names and five effect summaries; its numeric surface provides
texture-slot, area-fill, layout-class, required-skill, and ten-column combat-stat data. CALCBTPARAM
proves the stat ABI, MVSEEK/FIELD prove five SKINIT traversal requirements, and FIELD joins ten
shared `MP000*.AGF` fallback assets to the stage texture slots. The dedicated view accounts for all
98 instructions and preserves ids 0, 5, and 6 as meaningful implicit-default rows.

SPINIT is HMODE's `8 × 15` H-scene gallery registry. Its 118 populated resource ids fill seven
complete pages and thirteen cells of the final page; the final two slots remain zero. INIT2 provides
the matching `SO027A.AGF` through `SO027H.AGF` thumbnail sheets, and all populated cells resolve to
`SP*.BIN` resources. HMODE filters cells through opcode `0x19d` and indirectly call-scripts the
selected available entry.

TRINIT defines TRAIN's 21 training/sexual-magic actions. Each row owns six available/locked text
slots plus eligibility, spirit-cost, fourteen-stat, alignment, training-progress, reward, and
ten-event fields in one contiguous numeric block. TRAIN consumes the complete schema; GAMESTART
replays event ids below each restored execution count as completed story flags. All eight item
requirements, eight skill awards, three item awards, and 75 event cells resolve through
ITINIT/SKINIT/SCINIT.

CDINIT defines nine weighted card-generation lists with 383 total candidates. FIELD selects the
list through STINIT type-28 card objects, filters CDINIT2 story gates, applies the current-stage-turn
weight curve, and performs cumulative random selection across a 100-slot work buffer. The native
script clears only the first 50 slots even though its largest authored list contains 75.

CDINIT2 defines 81 cards inside a reserved 100-row registry. Its names and result messages pair with
six consumer-proven effect types: story event, item award, deferred stage-clear point bonus, ranged
resource recovery, trap, and random warp. All item, event, condition, and visual references resolve
through ITINIT, SCINIT, ILINIT, and SYS4INI. FIELD reads two of the three authored required-story-flag
columns; eighteen third-column values are preserved as engine-dead data.

BTANINIT2 defines 122 sparse battle-animation ids in a reserved 1,000-row layout. Each row owns up
to six BTANINIT effect ids, their start delays, and an overall duration. CALCDMG selects rows 1--21
from ITINIT weapon classes or a skill's explicit animation id; 101 SKINIT references join to 98
rows, passive reactions occupy 801--808, and BTL hardcodes defeat row 809.

BTANINIT materializes the selected timeline into BTL's six-slot work record. Its 202 effect
definitions resolve 186 movie visuals and sixteen sprite sheets, 199 optional WAV sounds, blend and
anchor flags, surface geometry and offsets, sprite-atlas timing, sound delay, and hit-pulse timing.
All populated visual/audio resources resolve through SYS4INI; sixteen authored effects are
unreferenced by BTANINIT2.

RTINIT is a seventh banked shape. Its 3,336 writes populate twenty parallel `1000 × 20` banks: ten
movement banks consumed by MVRTN/RTN_M providers and ten battle banks consumed by BTRTN/RTN_B providers.
The generated 172 routine-set records assemble 1,043 movement steps and fourteen battle steps, join
provider selectors to script names, retain all 29 overwritten cells, and keep the six reserved empty
banks visible. Selector-specific schemas now cover
all nineteen used providers,
RTN_M001/002/003/004/005/006/007/008/009/010/011/012/013/014/015/017/051/052/061:
progress-only steps, randomized roaming, stage-object slots, coordinate destinations, enemy/ally and
Magic Pillar searches, resource-threshold Healing Feather search, collectible-treasure seeking, cyclic
waypoints, faction-traversable terrain, retreat, reachable normal-attack routing, immediate
element-effective target/action selection, and immediate allied healing. The generated join covers all
1,043 steps and accounts for all 977 authored movement parameters:
974 semantic inputs plus three M001/M008 cells proven unread by their providers. Thirteen
unwritten zero defaults are projected separately while the structurally named raw banks remain intact.
The three additionally dispatchable providers which are absent from the shipped table have no
instantiated steps to join.

### Message/string tables (`*MES`)
`ITMES` (64 KB — item text), `VIMES` (43 KB), `EIMES` (37 KB), `SKMES` (31 KB — skill
text), `CIMES` (15 KB), `MAMES`, `INFOMES`, `MES` — where most translatable text
outside scenes lives. The generated message join covers ITMES's 287 item records, SKMES's 131 skill
records, VIMES's complete 65-topic VIINIT glossary, EIMES's 192 sparse EBINIT enemy-commentary
records, CIMES's complete 24-profile CIINIT character registry, and nine of MAINIT's eleven magic actions.
ITMES/SKMES/MAMES use fallthrough message bodies; VIMES/EIMES/CIMES place all guards first and branch to
later bodies. VIMES records retain title
plus multiline description, EIMES's untitled two-line records expose `summary` and `strategy`, and
CIMES exposes one untitled multiline `biography`; MAMES exposes one untitled action `description`.
INFOCH uses each CIINIT profile id to join its displayed name, backing unit definition, optional portrait,
and CIMES text. INFOMES itself contains no text: it walks a 32-by-4 first-handler-wins registry whose
shipped row zero routes the character, enemy, and glossary tabs to CIMES/EIMES/VIMES. MES is the generic
modal renderer for caller-populated line buffers, with a shared optional annotation ABI also consumed by
SBUNKI. Full data-flow details live in `name-resolution.md`.

### Battle system
`BTL` (61 KB — main battle loop), `BTRTN`, `ROUND`, `AIM`, `ATSEEK`, `MVSEEK`,
`MVRTN`, `MAGIC`, `USEMAGIC`, `SUMMON`, `EXILE`, `DISARM`, `RECOVER`, `COUNTUNIT`,
`SETOCC`, and the `CALC*` family: `CALCBTPARAM`, `CALCDMG` (16 KB — damage formula!),
`CALCSCOPE`, `CALCOCC`, `CALCREVISE`, `CALCCC`, `CALCILL`, `CALCARR`

### Dungeon/map engine
`FIELD` (200 KB — the core dungeon-crawl loop), `DRAWMAP`, `RENDERMAP`,
`DRAWMINIMAP`, `DRAWCH`/`DRAWCHP`/`DRAWENP`/`DRAWOBJ`/`DRAWTIP`/`DRAWVOL`,
`SETCH`/`SETEN`/`SETLAND`/`SETOBJ`/`SETROUTE`/`SETMVWORK`,
`DELCH`/`DELEN`/`DELENMASS`/`DELLAND`, `RESETLAND`, `WARPU`/`WARPD`, `LOOK`,
`READICON`

### Unit/party management
`ADDEXP`, `ADDSKILL`, `ADDEN`, `ADDITEM`, `ADDRANDOMITEM`, `LOSTRANDOMITEM`,
`ADDILL`/`ADDILLSUB`, `EVOLVE`, `IMPROVE` (37 KB), `TRAIN`, `STUDY`, `UNITECH`,
`REMOVECH`, `SHOWGROW`, `STATUS`, `USEITEM`, `SETCH`

### Base/facility gameplay
`CAMP`, `ROOM`, `FORT`, `ALCHEMY` (40 KB), `SALLY` (40 KB — sortie/deployment),
`READY` (39 KB — pre-battle prep), `SELSTAGE` (34 KB), `SELACT` (30 KB)

### Menus / UI / meta
`MENU`, `CHMENU` (69 KB — character menu), `INFO`/`INFOAF`/`INFOCH`/`INFOEN`/
`INFOIT`/`INFOVO` (info panels: characters, enemies, items, glossary/help topics), `SAVE` (40 KB),
`HISTORY`, `HIDEWIN`, `CLOSE`, `INPUTNAME` (28 KB), `CGMODE` (24 KB — gallery),
`MMODE` (music mode), `HMODE` (22 KB — scene replay)

### Flow control / branching
`BUNKI` (分岐 = branch, 15 KB), `SBUNKI`, `BUNKIMOVE`, `SBUNKIMOVE`, `SCJUMP`

### Callbacks (engine → script hooks)
`CALLBACK_LOAD`, `CALLBACK_LOST`, `CALLBACK_SETTING`, `CALLBACK_WINDOW`

### Debug
`DEBUG`, `DEBUGADV` (1.4 MB), `DEBUGANIME`, `DEBUGBTL`, `DEBUGMAP` (+2 numbered)

---

## Call graph — scripts are addressable by `call-script <id>` (2026-07-07)

`call-script <id>` (opcode 0x03) loads another script by a **raw index into the SYS4INI file table**
(id = the entry's `raw_index` = its global position in SYS4INI). This is the resolved call-graph
registry — there is no separate id→code table; SYS4INI is it. Mechanism: `engine-re.md` (op 0x03
section); the runtime source is `Sys4AssetCatalog` over SYS4INI, while the mechanically generated
`build/callscript-names.json` feeds `sys4load` diagnostics. The regenerated `build/disasm/*.asm` corpus renders targets by name
(`call-script 0x1ab =ADDITEM.BIN`). **297 distinct scripts are called** across the corpus (3002 sites);
the hottest are `HISTORY` (backlog), `MENU`, `HIDEWIN`, `BUNKI` (branch), `MES` (message), `ADDITEM`,
`ADDEN`, `LOOK`, `RENDERMAP`. Scenes (`SCxxxx.BIN`) load through the *same* id-indexed loader.

**Living-reference decision:** no separate generated markdown call-script reference is kept. Unlike
`opcode-reference.md` / `global-reference.md` (rendered from *curated* knowledge bases), the id→name
mapping is purely mechanical (SYS4INI index → filename) with no semantics to curate — it already lives
in the build artifact and in the named disasm corpus. Full call-graph edges (caller→callee counts) are
derivable on demand from the corpus; materialize a doc only if a consumer needs it.

---

## Implications for the port

1. **Much more game logic lives in bytecode than expected.** Damage formulas
   (`CALCDMG`), the dungeon loop (`FIELD`), battle flow (`BTL`, `ROUND`), and unit
   progression (`ADDEXP`, `EVOLVE`) are all scripts — `AGE.EXE` is closer to a pure
   VM/renderer. This strengthens the case for **re-implementing the AGE VM in Godot**
   rather than transpiling every script by hand (the doc's open question #12).
2. **Disassembler bootstrapping order** (small → large, system → scene):
   `INIT.BIN` (64 B) → `ED.BIN`/`OP.BIN`/`LOGO.BIN` (~230 B) → `CALLBACK_LOST`
   (176 B) → `MENU.BIN` (3 KB) → `CALCDMG` → a mid-size `SC####`.
3. **The `*INIT` giants are likely data tables**, decodable early even with a
   partial opcode map — instant win for extracting item/skill/enemy/stage databases.
4. **Use root-directory overrides, not archive copies**, for the 49 archive-backed patched scripts.

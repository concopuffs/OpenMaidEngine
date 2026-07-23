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
- `SCINIT.BIN` (88 KB) initializes scenario state.

---

## Named scripts by subsystem (inferred from names)

### Engine core / boot flow
`SYSTEM4.BIN` (config root), `INIT.BIN` (64 bytes — smallest script, ideal first
disassembly target), `INITCONFIG`, `LOADCONFIG`, `CONFIG`, `TUNE`, `LOGO`, `OP`,
`ED`, `TITLE`, `GAMESTART`, `GAMECLEAR`, `STAGECLEAR`

### Data-table INIT scripts (likely static game data, not logic)
Large, table-like scripts — prime candidates for data extraction:
- `STINIT` (579 KB) — stages/scenario tables
- `EBINIT` (338 KB) — enemy battle data
- `MPINIT` (330 KB) — maps
- `SCINIT` (88 KB), `CGINIT` (79 KB — CG gallery), `ITINIT` (70 KB — items),
  `RTINIT` (67 KB), `CCINIT` (41 KB), `SKINIT` (37 KB — skills), `CDINIT` (31 KB),
  `BTANINIT` (105 KB — battle animations)
- Smaller: `AFINIT`, `ALINIT`, `CIINIT`, `CNINIT`, `CTINIT`, `CVINIT`, `ILINIT`,
  `LAINIT`, `MAINIT`, `OBINIT`, `SPINIT`, `TRINIT`, `VIINIT`

**Extracted-table correction and semantic pilot (2026-07-22).** Name-mode INIT tables use sparse,
one-based runtime ids over fixed reserved array spans, not a new record whenever the string destination
decreases. The corrected extractor finds SKINIT 131 skills (span 300), ITINIT 287 items (span 1000), and
EBINIT 277 units (span 1000). In particular, the earlier ITINIT JSON's 189 records were invalid: it collapsed
items 1–101 into the first record and treated consecutive item names as descriptions. Generated table state
and counts are indexed in `build/data/README.md`; field semantics are curated in `vm-map/globals.toml` and
described by the workflow in `docs/name-resolution.md`.

The same audit found that linked row-major writes must not be normalized as independent parallel arrays.
Corpus `lookup-array-2d` bases and strides assign every such ITINIT write unambiguously to six tables (44
populated columns), while SKINIT and EBINIT expose 18 and 84 linked columns respectively. The extractor also
preserves the scripts' zero-minus-immediate negative writes, which carry item penalties, condition cures, and
skill SP costs. Generated records keep linked cells under `record_fields[base/stride/column]`; `fields`
contains only genuine parallel arrays.

STINIT uses the separate mixed shape: 74 sparse `scjump_progress_a` branches load one current-stage work
buffer rather than parallel per-id arrays. Its generated records preserve four mission-condition strings,
six scalars, cells in 29 preallocated buffers, and all 1,396 length-prefixed footer-array copies. Confirmed
stage field meanings and the evidence workflow live in `docs/name-resolution.md`.

### Message/string tables (`*MES`)
`ITMES` (64 KB — item text), `VIMES` (43 KB), `EIMES` (37 KB), `SKMES` (31 KB — skill
text), `CIMES` (15 KB), `MAMES`, `INFOMES`, `MES` — where most translatable text
outside scenes lives.

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
`INFOIT`/`INFOVO` (info panels: characters, enemies, items, voices), `SAVE` (40 KB),
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

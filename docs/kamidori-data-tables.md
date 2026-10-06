# Kamidori data tables (`*INIT`)

Canonical home for how 神採りアルケミーマイスター (Kamidori, SYS4433) stores its static game data: which
`*INIT` scripts exist, how they are layered, where each table lives in the global bank, and what its fields
mean. Field names themselves live in [`vm-map/globals-kamidori.toml`](../vm-map/globals-kamidori.toml)
(generated → [`global-reference-kamidori.md`](global-reference-kamidori.md)); this document explains the
structures and points at the evidence. Himegari's equivalent decode is Phase 2 of
[`vm-mapping-plan.md`](vm-mapping-plan.md) plus [`vm-map/globals.toml`](../vm-map/globals.toml).

## How the data gets into memory

Like Himegari, Kamidori has no data files: every table is a SYS4 script that writes constants into the flat
global bank (`set-string` for text, `mov`/`sub` for integers, `copy-local-array` for footer-stored bulk
arrays). Runtime code then reads the bank with `lookup-array` (1D, `base + id`) and `lookup-array-2d`
(`base + row * stride + column`).

**Boot order.** `INIT2.BIN` calls 28 base initializers in this order: SCINIT, AMINIT2, QUINIT, ALINIT,
IMINIT, BUINIT, ARINIT, EBINIT, CNINIT, ITINIT, SKINIT, ILINIT, AFINIT, LAINIT, OBINIT, RTINIT, STINIT2,
HAINIT, DIINIT, CDINIT2, CGINIT, SPINIT, CVINIT, FAINIT, CIINIT, VIINIT, EUINIT, BTANINIT2. Op `0x143` then
runs each mounted append catalog's record zero (`$n$AUTORUN.BIN`) in ascending selector order
([`asset-resolution-re.md`](asset-resolution-re.md)). Kamidori mounts three appends; each AUTORUN calls its
own `$n$<TABLE>.BIN` fragment for the same 28 tables, in the same order. A fragment writes into the base
table's arrays: the runtime image of a table is **base, then `$1$`, then `$2$`, then `$3$`**, later writes
winning. 64-byte fragments are empty stubs.

The remaining tables run on demand, called by game scripts with a selector global set first: STINIT
(per-stage loader), CCINIT (class-change rules), MPINIT and COINIT (footer-stored map data), CSINIT, EVINIT,
SHINIT, SIINIT, AMINIT, CDINIT and BTANINIT.

## Which files are authored data

The installed `Kamidori/` root mixes three sources:

| files | origin | used by data tooling |
|---|---|---|
| `DATA1-5.ALF`, `APPEND01-03.ALF` | shipped archives | yes (via `extracted-kamidori/`) |
| non-`$` root `*.BIN` (94: 90 differ from `DATA1.ALF`, 2 identical, plus root-only SYS4INI/SYS4RK) | official update: Japanese text, none backed up by the translation installer | yes — shadow the archive copy |
| root `$n$*.bin` (92 scripts) | **English fan translation** replacing append records | **no** |
| `patch/` | fan translation overlay (`--overlay-root patch`) | no |

The `$n$` finding and its evidence are recorded in [`asset-resolution-re.md`](asset-resolution-re.md). The
Python tooling enforces it through `paths._UNTRUSTED_LOOSE_APPEND_OVERRIDES`.

## Tooling workflow

All commands run from `age-reimpl/` with `py -3.11 -X utf8` and `--profile kamidori`
(details in [`tools-reference.md`](tools-reference.md)):

1. Extract archives once: `bin/BinExtractALF.exe` over `SYS4INI.BIN` and each `APPENDnn.AAI` into
   `../extracted-kamidori/` (append records land flat in `APPENDnn/`).
2. `parse_sys4ini.py` → asset index + call-script names, including append packed ids.
3. `extract_phase2.py` → `build/games/kamidori/disasm/*.asm` and text corpora.
4. `extract_init.py <TABLE>` and `extract_init.py <TABLE> --append N` → per-layer JSON in
   `build/games/kamidori/data/`; Kamidori specializations live in `tools/init_kamidori.py`.
5. `init_table_profile.py <TABLE> --build` → field population/value/consumer evidence.
6. `init_image.py` → `build/games/kamidori/init-image.json`, the **executed** boot image: the C# VM runs the
   boot order above and records each script's global delta. This is the ground truth that static extraction
   is checked against; `init_image.merged(TABLE)` gives a table's final cells.
7. `global_map.py` then `globals_build.py --build` → auto shape map merged under `globals-kamidori.toml`.

## Table inventory

Record counts are per layer from `extract_init.py` (the base, then `--append 1/2/3`; – means an empty
stub). EBINIT uses the generic name mode. Every other table has a Kamidori specialization in
`tools/init_kamidori_<domain>.py`. Hook tables run through the 32-slot hook chains described under
[Quests, progression and content registries](#quests-progression-and-content-registries).

| table | holds | runs | records: base [$1$ / $2$ / $3$] |
|---|---|---|---|
| AFINIT | species, affinity scale, susceptibility, costume-enhancement catalog, progression constants | boot | 6 [– / – / –] |
| ALINIT | alchemy recipes | boot | 369 [8 / 7 / 103] |
| AMINIT | world-area state hook | on demand | 26 [2 / – / 5] |
| AMINIT2 | world areas | boot | 26 [2 / – / 5] |
| ARINIT | arena battles | boot | 38 [– / – / 3] |
| BTANINIT | battle effect dispatch (hook) | on demand | 237 [6 / 0 / 0] |
| BTANINIT2 | battle animation timelines | boot | 152 [4 / – / –] |
| BUINIT | studio extensions | boot | 40 [– / – / –] |
| CCINIT | class-change rules (hook) | on demand | 103 [3 / 5 / 16] |
| CDINIT | field-card generation lists (hook) | on demand | 46 [7 / – / 2] |
| CDINIT2 | field cards | boot | 85 [– / – / –] |
| CGINIT | CG gallery | boot | 1114 [82 / 21 / 34] |
| CIINIT | character profiles | boot | 34 [9 / – / –] |
| CNINIT | speaker names, voice families | boot | 368 [22 / 6 / 49] |
| COINIT | costume enhancement boards (hook) | on demand | 23 [3 / – / –] |
| CSINIT | costume sprite substitution (hook) | on demand | 573 [75 / 0 / 0] |
| CVINIT | voice settings | boot | 29 [4 / – / –] |
| DIINIT | gathering drop tables | boot | 79 [– / 4 / 10] |
| EBINIT | units / characters | boot | 368 [22 / 6 / 49] |
| EUINIT | gallery character roster | boot | 13 [2 / 2 / 2] |
| EVINIT | event-CG substitution (hook) | on demand | 36 [27 / – / –] |
| FAINIT | chapter objectives | boot | 203 [– / – / –] |
| HAINIT | dungeon hall art | boot | 121 [15 / 2 / 11] |
| ILINIT | conditions | boot | 28 [– / – / –] |
| IMINIT | equipment growth | boot | 35 [– / – / 26] |
| ITINIT | items | boot | 993 [19 / 20 / 288] |
| LAINIT | terrain | boot | 26 [– / – / 5] |
| MPINIT | dungeon floor maps (hook) | on demand | 145 [11 / 2 / 14] |
| OBINIT | map object types | boot | 44 [2 / 1 / 3] |
| QUINIT | quests | boot | 135 [6 / 3 / 43] |
| RTINIT | enemy AI routines | boot | 259 [29 / 16 / 43] |
| SCINIT | scene dispatch | boot | 1545 [78 / 21 / 153] |
| SHINIT | shop catalogue (hook) | on demand | 16 [– / 8 / 10] |
| SIINIT | town sites (hook) | on demand | 5 [– / – / –] |
| SKINIT | skills | boot | 376 [11 / 2 / 63] |
| SPINIT | H-scene replays | boot | 46 [3 / 1 / 2] |
| STINIT | per-stage loader (hook) | on demand | 181 [11 / 5 / 20] |
| STINIT2 | stage catalog | boot | 181 [11 / 2 / 15] |
| VIINIT | glossary | boot | 155 [2 / – / 6] |

## Characters and units

### Unit definitions (EBINIT)

EBINIT ids are **1-based**, and the name array base is `0x2ab7`, so unit 1 writes `0x2ab8`. SETEN@`0x1f` and
UNITECH@`0x3a5` index the title array `0x3287` with the same id. Every parallel array spans 1000 ids. The table
has 445 units, ids 1..996: 368 base, plus 22 from `$1$`, 6 from `$2$` and 49 from `$3$`. Append layers only add
records; the boot image shows no base cell overwritten.

**Strings:**

| base | contents |
|---|---|
| `0x2ab7` | full name |
| `0x2e9f` | short display name |
| `0x3287` | initial class title |
| `0x366f` | bestiary blurb, `string[1000][3]` (INFO5@`0xffd`) |

The generic extractor reports the bestiary blurb at `0x36fb…`. That metadata is wrong; the values themselves
are correct.

**Parallel ints, one value per unit:**

| address | field |
|---|---|
| `0x2b570c` | sort key |
| `0x2b5af4` | listed in the bestiary |
| `0x2b5edc` | race, 1..16 (names at `0x61fe`: 人間…半魔人) |
| `0x2b62c4` | sex, 1/2/3; a bit index into the item gender mask `0x2e6898` |
| `0x2b66ac` | boss class (med confidence) |
| `0x2b6a94` | **canonical character id**: maps variants to one character (e.g. 11→36, 48/49→45). All persistent character state is indexed by canonical id < 100. About 45 scripts use it. |
| `0x2b6e7c` | cut-in element (med confidence) |
| `0x2b91a4` | default attack item (素手, 噛み付き…) |
| `0x2b958c` | initial weapon level |
| `0x2b9974` | equippable-category mask |
| `0x2ba914` | default costume item (ITINIT 101..151, 普段着) |
| `0x2bcc3c` | initial party capacity (protagonist only) |
| `0x2bd40c` | party cost |
| `0x2bd7f4` | initial affinity ÷ 10 |
| `0x2bdbdc` | starting level |
| `0x2bdfc4` | level cap |
| `0x2be3ac` | auto-level divisor |
| `0x2be794` | exp level cap |
| `0x2c8f8c` | exp reward |
| `0x2c9374` | bestiary knowledge gain per defeat |
| `0x2d0c8c` | large battle sprite (med confidence) |

**Row tables:**

| base | stride | contents |
|---|---|---|
| `0x2beb7c` | 14 | base stats |
| `0x2c222c` | 14 | growth per level, in hundredths. A fractional carry plus a random roll is applied by SETEN/ADDEXP; when a unit joins above its starting level, UNITECH applies growth×2×levels/100. |
| `0x2c58dc` | 14 | **per-character stat caps** (44 player rows, cols 0..8). UNITECH copies them to `0x64c8d`; ADDEXP and USEITEM clamp to them; class changes raise them. A cap of 0 falls back to the global cap row `0x2a2da7`. |
| `0x2a9b8c` | 30 | voice bank. Same selectors as Himegari: 0 warp, 1 pickup, 3 occupied, 7..24 battle and level-up. 33 units are voiced. |
| `0x2b10bc` | 17 | graphics assets: CS/CA/CP/CIC/CB families |
| `0x2b7264` | 8 | element resistances. Cols 1..7 are 物理, 地脈, 冷却, 火炎, 電撃, 神聖, 暗黒. Values −5..+6 map to labels at `0x621a+5+v` (激弱…吸収). |
| `0x2b9d5c` | 3 | initial equipment: weapon, accessory |
| `0x2bacfc` | 8 | SKINIT skill ids |
| `0x2c975c` | 5 | drop items |
| `0x2caae4` | 5 | drop chance %; a value ≤ 5 counts as a rare drop |
| `0x2cd1f4` | 10 | drop required flags |
| `0x2cf904` | 5 | drop excluding flags |
| `0x2d1074` | 4 | habitat areas (AMINIT2 ids) |

**The 14 stat columns** are named by SELSEWUP@`0x0..0x41`, which builds its list in column order:

| column | stat |
|---|---|
| 0 | 命中 accuracy |
| 1 | 回避 evasion |
| 2 | physical attack |
| 3 | physical defense |
| 4 | magic attack |
| 5 | magic defense |
| 6 | 敏捷 agility |
| 7 | 運 luck |
| 8 | 移動 movement (MVSEEK@`0x2c` reads it as the move range) |
| 9 | 接客 customer service |
| 10 | max HP |
| 11 | max SP |
| 12 | max FS |
| 13 | キャパシティ capacity |

Two consumers confirm the layout. SETEN and SETCH copy columns 10..12 into current HP/SP/FS. Hard mode
(SETEN@`0x3d4`) doubles column 10, adds 20 to column 0 and 10 to column 1, and multiplies columns 2..7 by 1.2.
The order differs from Himegari's.

### Runtime character state

**Selectors:**

| address | selects |
|---|---|
| `0x2a9b8b` | EBINIT id |
| `0x2d2845` | canonical character |
| `0x2a9b89` | battle slot (0..49) |
| `0x2d2843` | roster slot |

**Persistent per canonical character:**

| address | contents |
|---|---|
| `0x2b3` | class title (string) |
| `0x6432d` | level (0 = never joined) |
| `0x64391` | exp, 0..100 |
| `0x64715` | stats (×14) |
| `0x64c8d` | stat caps (×14) |
| `0x65205` | growth fractions (×14) |
| `0x643f5` | resistances (×8) |
| `0x657e1` | skill list (×8) |
| `0x65b01` | learned-skill flags (×1000) |
| `0x6577d` | party cost |
| `0x64265` | affinity |
| `0x642c9` | costume |
| `0x7e1a1` / `0x7e205` | weapon LV / weapon exp |
| `0x7e269` | class-change state (×20) |
| `0x64201` | join flags |

**Scalars:** party capacity `0x7ea3b`; protagonist craftsman rank `0x7ea49`.

**Roster:** unit `0x6383d`, flags `0x63bc1`, equipment `0x63905[3]`, equipped skills `0x63a31[4]`.

**Battle slots:**

| address | contents |
|---|---|
| `0x53bb2` | unit |
| `0x53cde` | level |
| `0x54062` | effective stats |
| `0x5431e` | unrevised stats |
| `0x545da` | current HP/SP/FS |
| `0x546a2` | skills (×11) |
| `0x538c4` | graphics (×15) |

**Bestiary:** knowledge % `0xf29db`, defeat counts `0xf2dc3`.

### Names, voices, profiles

**CNINIT** (generic extraction is wrong) is keyed by EBINIT id:

| address | contents |
|---|---|
| `0xfdc6c` | voice family (445 rows, 188 aliases) |
| `0xfd49c` | backlog face `CF###.AGF` (used by HISTORY) |
| `0x322` | `string[1000][5]` name forms |

Scenes pick a name form with `0x306f`: 0 name, 1 alternate, 2 descriptor, 4 `？？？？`.

**CVINIT** (generic extraction failed) has 33 voice-setting slots: slot 0 is エキストラ, slots 1..28 are base,
and 29..32 come from `$1$`.

| address | contents |
|---|---|
| `0x16aa` | `string[33][3]`: name / descriptor / voice actress |
| `0xfe43c` | slot → unit |
| `0xfe054` | unit → slot (not an exact inverse) |
| `0xfe824` | preview OGG |
| `0xfd884` | default text colour; copied into setting `0x3225` and edited by CONFIG4 |
| `0x2c84` | voice-mute flags |

**CIINIT/CIMES** hold character-information profiles 1..199, selected through `0x3d19d5`. The base has 1..34;
`$1$` adds 51..59. The generic `APPEND01-CIINIT.json` keys these as 1..9, which is wrong.

| address | contents |
|---|---|
| `0xd0eb` / `0xd1b3` / `0xd27b` / `0xd343` | strings: name / race / sex / epithet |
| `0x3d19d6` | sort key |
| `0x3d1a9e` | EBINIT unit |
| `0x3d1b66[200×3]` | unlock flags, 1-based into `0xf35a3` |
| `0x3d1f4e` | portrait, with x/y at `0x3d1dbe`/`0x3d1e86` |
| `0x3d2016` | biography script id (`CIMES.BIN`; `$1$CIMES` = `0x1000261`) |

A profile is visible when it has no unlock flags or any of them is set, and also, if it links to a unit, that
unit's `0xf5cb3[unit]` is set (INFO7@`0x73f..0x811`).

### Class changes (CCINIT)

CALCCC zeroes its output block, then calls each script id in the hook array `0x3d4260[0..31]`: `0x4523` from
INIT2, then `0x1000230`, `0x2000114` and `0x300024e` from the AUTORUNs.

**Rule shape:** `char == N && !(0x7e269[N][k] & 1) [&& level >= L]`. Event-gated rules also require bit 1,
which a story scene sets (e.g. SC0450@`0x5123`); without it, the script exits.

**When a rule fires**, it:

1. sets bit 0;
2. takes the title (`0x4227`) if the rule's priority beats the current one — priority is L, or −1 for event
   rules;
3. adds its deltas: capacity `0x2d2015`, cost `0x2d2016`, resistances `0x2d2017[8]`, stats `0x2d201f[14]`,
   caps `0x2d202d[14]`, skill-slot replacements `0x2d203b[8]`, and skill-learn flags `0x2d2043[1000]`.

**Counts:**
- Base: 103 rules for 26 characters, 15 of them event-gated, including the protagonist's ranks 匠巣…匠王.
- Appends: `$1$` 3 rules, `$2$` 5, `$3$` 16 (including 大匠王).

**Open questions:**
- `0x2bd024` has no reader.
- `0x4f9a7` (enemy auto-scale reference level) is not decoded.
- What exactly do `0x2b66ac`, `0x2d0c8c` and `0x2b6e7c` mean?
- Several graphics and voice columns are named only from file prefixes.
- `0x2cbe6c` (drop knowledge threshold) is read but never written.
- What does stat column 13 do in gameplay?


## Items, skills and combat

**Shared selectors.** `0x2d2846` is the current item (ITINIT arrays are `base + id`, ids 1..1999), and
`0x3a10c7` is the current skill (SKINIT arrays are `base + id`, ids 1..999).

**Shared unit-state arrays:**

| address | contents |
|---|---|
| `0x53c48[unit][3]` | equipment: 0 weapon (empty → species default attack `0x2b91a4`), 1 accessory, 2 costume (empty → default costume `0x642c9`) |
| `0x54062[unit][14]` | effective stats, clamped between `0x2a2dc3` and `0x2a2da7` |
| `0x53d42[unit][8]` | affinity levels 0..11 (5 = neutral) |
| `0x545da[unit][3]` | current HP/SP/FS |

The 14 stat columns are the same as EBINIT's (see Characters). The element and affinity columns are:
1 物理, 2 地脈, 3 冷却, 4 火炎, 5 電撃, 6 神聖, 7 暗黒. Column 0 is unused. The orb items (each raises exactly one
column) and the growth stones hard-coded in USEITEM@`0x16` prove both layouts.

### Items (ITINIT)

ITINIT is a fixed-layout table with 2000 ids. It has 1,310 named items: 992 base, 19 `$1$`, 20 `$2$`, 279
`$3$`. The appends only add ids. There are 42 parallel arrays and 7 row tables, packed from `0x2d2848`, and every
one of the 15,838 cells is classified.

**Strings:** names at `0x4228+id`; one-line summaries such as `弓 物理 射程2` at `0x49f8+id`.

**Categories (`0x2dd428`).** The ITMES dispatch proves 26 categories:

| value | category |
|---|---|
| 0 | basic attack |
| 1 | key |
| 2 | costume |
| 3 | invention |
| 4 | consumable |
| 5 | growth stone |
| 6 | costume orb |
| 7 | furniture |
| 8 | sell-only treasure |
| 9 | material |
| 10 | accessory |
| 11–25 | weapons: hammer, staff, sword, bow, crystal, pipe, glove, fang, spear, scythe, claw, vacuum, kettle, bucket, fan |

**Arrays.** These are the main ones; the full list is in the registry.

| address | stride | field | evidence |
|---|---|---|---|
| `0x2d2848` | 1 | handler script (ITMES/`$n$ITMES`) | SHOP@`0x3a49` |
| `0x2d3018` | 17 | costume graphics (col 4 face, col 3 CGMODE/ENHANCE, cols 15/16 battle) | SHOP, BTL@`0x474a` |
| `0x2db4e8` | 1 | icon | SHOP@`0x30c7` |
| `0x2dc488` | 1 | sort key | |
| `0x2dcc58` | 1 | rank 0..6 (weapon-rank gate, alchemy exp, shop sale chance 100 − 10×rank) | SETARMSTATE@`0x1bf` |
| `0x2ddbf8` / `0x2de3c8` | 1 | min / max range | CALCSCOPE@`0x4c` |
| `0x2deb98` | 1 | attack element | CALCBTPARAM@`0x1ae` |
| `0x2dfb38` | 8 | affinity-level deltas | CALCARM@`0x0`, INFO3@`0x17a4` |
| `0x2e39b8` | 5 | character whitelist | SETARMSTATE@`0xa7` |
| `0x2e6898` | 1 | sex restriction mask | SETARMSTATE@`0x14c` |
| `0x2e7068` | 1 | cursed (cannot unequip) | FELLOW@`0xff1` |
| `0x2e7838` | 30 | condition levels inflicted (consumables and weapon on-hit) | USEITEM@`0x4fd`, CALCDMG@`0x1360` |
| `0x2f6298` | 30 | passive condition levels while equipped | EQUIP@`0xd9` |
| `0x304cf8` | 1 | granted skill | CALCREVISE@`0x38f` |
| `0x3054c8` | 14 | stat modifiers | CALCARM, CALCREVISE@`0x341` |
| `0x30c228` | 1 | critical bonus | CALCREVISE@`0x36f` |
| `0x30c9f8` | 3 | HP/SP/FS recovery | USEITEM@`0x312` |
| `0x30e938` | 1 | battle animation id (BTANINIT2) | CALCDMG@`0x1ea` |
| `0x30f108` | 3 | prices: buy, sell, retail base | SHOP@`0xe34`, SALE@`0xf5a` |
| `0x310878` | 1 | furniture area: 1 shop, 2 workshop, 3 room, 4 garden | SELORN |
| `0x311818` / `0x311fe8` | 1 | furniture width / height | DRAWBUILD, ARRANGE |
| `0x3127b8` / `0x312f88` | 1 | garden production period / product | CHECKREWARD@`0x17a` |
| `0x313758..0x317da8` | 1 | ten furniture bonus stats, summed by SETSTUDIO | |

**Runtime arrays:**

- **Inventory.** `0x7ea4f[2000]` holds item counts, capped at 999. Anything above that is auto-sold into money
  `0x7ea39`.
- **Usability.** SETARMSTATE builds the per-item flags `0x319ce8`. An item is usable only when all four checks
  pass: its category is in the character's mask `0x2b9974` (categories 2 and 10 always pass), the whitelist
  allows the character, the sex mask allows the character, and the item's rank is no higher than the
  character's weapon rank.
- **Costume enhancement board.** `0x82100[item][15×15]`; COINIT defines the board layout per costume, and
  AFINIT's catalog holds the node effects.
- **Gem sockets.** `0xefed0[item][4]`.

**Compared with Himegari:**

- 2000 ids instead of 1000.
- 14 stat columns, adding customer service and skill capacity.
- One 8-column affinity table replaces separate attack and defense elements.
- The tuning curves are gone; costume boards, gem sockets and IMINIT growth replace them.
- New fields: a third price, the cursed flag, and furniture/garden data.

### Skills (SKINIT)

SKINIT has 1000 ids and 448 skills: 376 base, 11 `$1$`, 2 `$2$`, 59 `$3$`. Names are at `0x5998+id` and
summaries at `0x5d80+id`; descriptions come from SKMES.

| address | stride | field |
|---|---|---|
| `0x3a10c8` | 1 | icon |
| `0x3a14b0` | 1 | sort key |
| `0x3a1898` | 1 | upgrade family; the highest-cost member wins (CALCREVISE@`0x4e9`) |
| `0x3a1c80` | 1 | category (below) |
| `0x3a2068` | 1 | shown in the skill catalog |
| `0x3a2450` | 1 | equip cost; FELLOW@`0x12f0` caps the sum of 4 learned skills at stat column 13 |
| `0x3a2838` | 5 | bundled skills (e.g. 飛行潜水 = 1 + 2) |
| `0x3a3bc0` / `0x3a3fa8` | 1 | range min / max, stored +1 |
| `0x3a4390` | 1 | required weapon-category bitmask |
| `0x3a4b60`, `0x3a4f48` | 1 | area shape, area cooldown |
| `0x3a5330` | 1 | element, stored +1 |
| `0x3a5718` | 30 | inflicted condition levels |
| `0x3acc48` | 10 | stat deltas (stat columns 0–9) |
| `0x3af740` | 3 | HP/SP/FS deltas (heal, SP cost) |
| `0x3b02f8` | 1 | proc % |
| `0x3b06e0` | 1 | battle animation |
| `0x3b0ac8` | 2 | combo partner characters |
| `0x3b1298` | 1 | SKMES handler |
| `0x3b1680` / `0x3b1a68` / `0x3b1e50` | 1 | area effect texture / sound / blend |

**Categories:**

| value | category |
|---|---|
| 1 | movement passive |
| 2 | command |
| 3 | special passive |
| 4 | combat passive |
| 5 | physical special (including combos) |
| 6 | attack magic |
| 7 | heal |
| 8 | support |
| 9 | area special |
| 10 | area magic |
| 12 | area heal |

**When a skill is usable (CALCSCOPE):**

- Charm (condition 6) blocks every skill.
- Confusion (condition 7) blocks skills below category 9.
- An area skill's cooldown must be at zero.
- The unit must be able to pay the resource cost.
- If the skill has a weapon mask, the equipped weapon's category must match it.
- A combo skill needs its partner as a living, adjacent ally.

### Conditions (ILINIT)

ILINIT has Himegari's structure at new addresses. It defines 30 condition ids × 5 levels, 28 of them used:

| ids | conditions |
|---|---|
| 1 | instant death |
| 2–4 | HP/SP/FS drain |
| 5–13 | curse, charm, confusion, paralysis, poison, water flow, strong acid, inflammation, fear |
| 14–16 | HP/SP/FS regeneration |
| 17 | exaltation |
| 18–25 | buffs |
| 26 | movement down |
| 27 | no return |
| 28 | injury |

Level names are at `0x6168 + id*5 + level − 1`.

| address | field |
|---|---|
| `0x3b223a` | susceptibility group 1..7 (the column into AFINIT's species matrix) |
| `0x3b2258` | immunity skill (e.g. water flow → 潜水) |
| `0x3b2276` | can affect bosses |
| `0x3b2294` | cleared by RECOVER |
| `0x3b22b2` | icon |
| `0x3b22d0[30×5]` | duration in turns (−1 = until cleared) |
| `0x3b2366[30×45]` | stat deltas: 9 per level, stat columns 0–8 |
| `0x3b28ac` | critical delta, scaled by level/5 |
| `0x3b28ca[30×15]` | HP/SP/FS deltas, 3 per level |

**Unit state:** current level `0x60d44`, equipment baseline `0x618fc`, remaining turns `0x61320`. The RECOVER ABI
is identical to Himegari's. Append layers are empty.

### Affinity and progression constants (AFINIT)

Kamidori's AFINIT bundles several unrelated tables:

- **Species names** at `0x61ff..` (16 species).
- **Affinity scale.** Level names are at `0x621a+L`, and damage percents at `0x3b2a8c[12]`:
  150/140/130/120/110/100/90/75/50/25/0/−100, where −100 means absorb. CALCBTPARAM@`0x7a3` multiplies attack
  power by this percent.
- **Condition susceptibility.** `0x3b2a98[species][10]` gives the percent chance per condition group; 0 means
  immune.
- **Costume-enhancement catalog** (30 entries):
  - effect type `0x3a0e8d[3]`: 1 affinity, 2 stat, 3 gem socket;
  - effect target `0x3a0ee7[3]` and amount `0x3a0f41[3]`;
  - tier `0x3a0f9b` and cost `0x3a0fb9`;
  - materials `0x3a0fd7[4]` and quantities `0x3a104f[4]`.
- **Progression thresholds:**
  - weapon rank `0x3b2b60`;
  - synthesis rank `0x3b2b67` and its 評価 bonuses `0x3b2b6e`;
  - alchemy exp by item rank `0x3b2b75`;
  - event scalars `0x3b2b7d..88`.

### Battle animations (BTANINIT2, BTANINIT)

These use Himegari's schema, with every address shifted by +`0x2612d5`.

- **BTANINIT2** runs at boot. It holds effect ids `0x3b3b63[1000×6]`, start delays `0x3b52d3[1000×6]` and
  duration `0x3b6a43` (BTL@`0x3467`), for 156 rows.
- **BTANINIT** is the hook at chain `0x3d41a0`. It dispatches 243 effects into 14 work arrays at
  `0x3b6e2b + 6k`, plus hit pulses at `0x3b6e7f[6×3]`. Its selector is `0x3b3b61`, which CALCDMG@`0x1c4`/`0x1ea`
  sets from the skill or item animation id.

### Enemy AI routines (RTINIT)

RTINIT holds 29 banks of [1000 routines × 50 steps]. The root is `0x14d09e` and the banks are 50,000 cells apart.
A table with 3 entries per step spans three banks. The routine id is `0x14d09c` and the step `0x14d09d`.

**Map routines** (MVRTN; the unit's routine is in `0x61ed8`):

| bank | address | field |
|---|---|---|
| 0 | `0x14d09e` | provider: 1..19, 51..54, 61, 71 → `RTN_M###` |
| 1 | | activation % |
| 2–5 | | parameters |
| 7 | | use limit, checked against `0x62004` |
| 8–10 | | required story flags |
| 11–13 | | forbidden story flags |

**Battle routines** (BTRTN; the unit's routine is in `0x61f6e`):

| bank | address | field |
|---|---|---|
| 14 | `0x1f7efe` | provider 1..5 → `RTN_B###` |
| 15 | | activation % |
| 16 | | parameter 1 (a skill id for RTN_B004) |
| 22 | | use limit, checked against `0x629c8` |
| 23–25 | | required story flags |
| 26–28 | | forbidden story flags |

A step runs when its provider is nonzero, all its flag checks pass, its use count is under the limit, and the
random roll is under the activation %. If no step fires, BTRTN uses skill 21 (防御). 347 routines are populated,
and the appends only add new ones.

### Combat pipeline

| script | role |
|---|---|
| CALCARM | totals for one item |
| CALCREVISE | folds equipment, conditions and passives into the unit |
| CALCBTPARAM | per-side attack, defense, agility, hit and crit, then the affinity damage scale |
| CALCDMG | picks the animation and the on-hit conditions |
| CALCSCOPE | builds the list of usable skills |

**Open questions:**

- What do the costume battle variant `0x2df368` values mean?
- What are the furniture exclusive group `0x311048`, the garden bonus `0x317da8`, and the field target kind
  `0x318578`?
- What are `0x2d3018` columns 0–2 and 5–14?
- What do SKINIT's `0x3a4778` (1 vs 2) and `0x3b1e50` do?
- What are AFINIT's `0x3b2b90` and `0x3b2b96`?


## Alchemy, economy and town

Two conventions apply to every table in this section:

- Flag ids are 1-based story flags; scripts test global `0x50d + id − 1`.
- Append layers only add data. The pristine `$1$`/`$2$` SIINIT and IMINIT files and every `$n$BUINIT` are empty
  stubs.

### Recipes (ALINIT)

ALINIT is a sparse **2000-slot recipe registry**. It is 24 arrays laid out back to back (`0x38c8d9..0x3a0929`)
and holds 487 recipes: 369 base, 8 `$1$`, 7 `$2$`, 103 `$3$`, with ids 1..1921. Its 240 strings are unlock
hints such as `クエスト『…』`, `店舗LV3　知識30` and `周回`. Generic name mode treated those hints as record
names, which is why it misread the table.

| base | ×cols | field | consumer |
|---|---|---|---|
| `0x38c8d9` | 1 | output item id (nonzero = recipe exists; usually = recipe id) | CHECKRECIPE@`0x24`, ALCHEMY@`0x1224` |
| `0x38d0a9` | 1 | quest-recipe badge | ALCHEMY@`0x1d4d` |
| `0x38d879` | 1 | synthesis rank required to craft (unused: all Ｅ) | ALCHEMY@`0x16b7` |
| `0x38e049` / `0x390759` | 5 | flags required / forbidden to list (hide flag = 9000+id+1) | ALCHEMY@`0x15d3`/`0x162d` |
| `0x393639` / `0x395579` | 4 | ingredient item ids / quantities | ALCHEMY@`0x102d`/`0x1056` |
| `0x3974b9`, `0x3993f9` | 1 | mutation (突然変異) chance %, mutation item | ALCHEMY@`0x995`/`0x92f` |
| `0x397c89` / `0x398c29` | 2 / 1 | flags required for / blocking mutation | ALCHEMY@`0x1841`/`0x187e` |
| `0x399bc9`, `0x39a399` | 1 | unlock: craftsman rank 0..10, synthesis rank 0..6 | CHECKRECIPE@`0x55`/`0x6b` |
| `0x39ab69`, `0x39b339` | 1 | unlock: item fully grown (IMINIT), prerequisite recipe | CHECKRECIPE@`0x121`/`0x155` |
| `0x39bb09`/`0x39c2d9`/`0x39caa9`/`0x39d279` | 1 | unlock: shop / workshop / quarters / room-4 level (displayed, 1-based) | CHECKRECIPE@`0x81..0xd8` |
| `0x39da49`, `0x39e219` | 1 | unlock: minimum knowledge (知識), sense (センス) | CHECKRECIPE@`0xf5`/`0x10b` |
| `0x39e9e9` | 3 | unlock: story flags, all required | CHECKRECIPE@`0x19d` |
| `0x3a0159` | 1 | 評価 (evaluation) reward for first craft | ALCHEMY@`0xba6` |
| string `0x51c8` | 1 | unlock hint text | INFO4@`0x1069` |

**How the game uses it:**

- **Learning.** CHECKRECIPE learns a recipe once every gate passes. It sets `0x8115f[r]` (learned this
  playthrough), `0xf686b[r]` (learned in any playthrough) and the new-recipe notice `0xf35a2`.
- **Ingredients.** Each crafted unit consumes `max(1, qty·(99 − material_saving)/100 + 1)` of every
  ingredient.
- **Mutation.** A mutation happens when `random(100) < chance · mutation_rate / 100`.
- **Synthesis exp.** Each unit gives `(rank·5 + 0x3b2b75[item rank]) · (100 + exp_bonus) / 100`.
- **Rank.** AFINIT writes the rank thresholds `0x3b2b67` (0/3000/6000/12000/30000/90000/200000) and the
  rank-up 評価 bonuses `0x3b2b6e`.
- **評価 reward.** The full reward is paid on the first craft ever and a tenth of it afterwards. It accumulates
  in `0x7ea3a`.

**Compared with Himegari.** Himegari's table has 1000 slots, a point cost and 2+2 flags. Kamidori adds:

- mutation
- a separate learn step
- studio, knowledge and sense gates
- growth and prerequisite-recipe gates
- hint text

### Studio extensions (BUINIT) and studio parameters

BUINIT covers 4 rooms × 20 level slots, of which 10 are authored. The rooms are 0 shop (店舗), 1 workshop (工房),
2 quarters (部屋), and 3 a fourth room that supplies sense. Row = `room*20 + level`; level 0 is the starting
state.

**Per level:**

| field | address | consumer |
|---|---|---|
| craftsman rank | `0x13ec64` | ARRANGE@`0xcb2` |
| money cost | `0x13ecb4` | ARRANGE@`0xcf7`/`0xe0a` |
| materials / quantities (items 581..596) | `0x13ed04[×4]` / `0x13ee44[×4]` | ARRANGE |
| 5×5 placement grid, cells 0/1/2 | `0x13ef84[80×25]` | ARRANGE, DRAWBUILD, SELORN, STUDIO |
| upkeep | `0x13f754` | CALCSETTLE |

**Parameter curves.** There are ten 20-level curves. SETSTUDIO (`0x0..0x73`) reads each from the owning room's
current level, then adds the bonuses of the ornaments placed in `0x369b[room][25]`. ITINIT writes the per-item
bonus arrays.

| room | parameters |
|---|---|
| shop | shelf capacity `0x13f86c`, purchase rate `0x13f86d`, visitor rate `0x13f86e`, price rate `0x13f86f` |
| workshop | material saving `0x13f870`, exp bonus `0x13f871`, mutation rate `0x13f872` |
| quarters | knowledge `0x13f873`, resident capacity `0x13f874` |
| room 4 | sense `0x13f876` |

The current room levels are `0x7ea4b..0x7ea4e`, stored 0-based.

### Equipment growth (IMINIT)

IMINIT is keyed by ITINIT item id. It covers 61 items: 35 base and 26 in `$3$`.

| field | address |
|---|---|
| growth enabled | `0x31a4b9` |
| exp needed to leave level L (the first 0 marks the max level) | `0x31ac89[×11]` |
| stat increments, 10 levels × 14; only columns 0, 2, 3, 4, 6 are used | `0x347379` |

Never authored: `0x320279[10×8]`, the mastery item `0x38b939` and the mastery flag `0x38c109`.

At runtime the growth level is `0x8098f[item]` and growth exp is `0x801bf[item]`:

- COMMITBTL@`0x483` adds 1 exp per battle to each equipped growth item.
- CHECKIMPROVE levels the item up.
- CALCARM@`0x316` sums the increments into the item's stats.

### Shop catalogue (SHINIT) and town sites (SIINIT)

**SHINIT** is a hook (`0x3d4200`): the base table sits in slot 0 and each AUTORUN in slot n. SHOP@`0x167a`
clears `0x3c5b4c[100]`, then runs every registered hook.

| layer | slots | contents and gate |
|---|---|---|
| base | 1–15 | items 234..248, gated by craftsman rank ≥ 1/4/7 |
| base | 16 | item 981, gated by flag 1304 |
| `$2$` | 17–24 | |
| `$3$` | 25–34 | |

Prices are `0x30f108[item*3 + {buy, sell, settle}]`. The player's own shelf is `0x36ff[20]` (items) and
`0x3713[20]` (stock).

**SIINIT** is a hook (`0x3d41e0`), called by TOWN@`0x194` and SELSTAGE@`0x539`. It branches on the current town
`0x3612`. Yuidora's data:

| field | address |
|---|---|
| BGM | `0x13eace` |
| background | `0x13eacc` |
| stage-select preview | `0x13eacd` |
| per site i = 1..10: icon | `0x13eacf+i` |
| per site: x / y | `0x13eaed+i` / `0x13eb0b+i` |
| per site: name | `0x2481+i` |
| per site: 4 cursor links (−1 = none) | `0x13eb29+i*4+d` |

The sites are:

1. 街を出る
2. ウィルの工房
3. 中央広場
4. 裏路地
5. 領主の館
6. レグナーの工房
7. 酒場
8. 教会
9. 闘技場
10. ユイドラ鉱脈

**Open questions:**

- Who raises the recipe hide flags?
- What distinguishes BUINIT grid value 1 from 2?
- What is the fourth room called in game?
- What are the exact roles of `0x13f86d..f` in CALCSETTLE?
- What is `0x13f875`? Nothing writes it.


## Maps, dungeons and world

### How a dungeon floor is stored (MPINIT)

Kamidori stores **one block per floor**, not a single shared atlas as Himegari does.

**The selector.** MPINIT runs on demand and dispatches on `current_labyrinth_id` (`0x428e`) and
`current_floor_no` (`0x428f`). Labyrinth −1 holds the non-dungeon battle maps; its selector value is built with
`sub local, 0, 1` (MPINIT@`0x429d`).

**The template.** The prologue (MPINIT@`0x0`) zeroes the whole 53×53 `floor_terrain_template` at
`0x378e..0x4286`. Each matching block then:

1. writes the floor size in tiles: width `0x4287` (4..25) and height `0x4288` (5..25);
2. footer-copies `2H` rows of `2W` cells to `0x378e + y*53 + 1`, for `y = 1..2H`.

There are 172 maps: 145 in the base, 11 in `$1$`, 2 in `$2$` and 14 in `$3$`. Append layers only add new
(labyrinth, floor) blocks.

**Cell encoding: doubled coordinates.**

- Tile `(tx, ty)`, with `tx ∈ 1..W` and `ty ∈ 1..H`, lives at `grid[2ty][2tx]`. It holds a LAINIT terrain id;
  0 means no tile.
- Cells between tiles are edges: odd x with even y is a horizontal edge, and even x with odd y is a vertical edge.

The value of an edge cell means:

| edge value | meaning | count |
|---|---|---|
| `0` | wall | |
| a terrain with the room-join flag set (normally the same id as the adjoining tiles) | open floor inside one room | |
| `1` (平地, no join flag) | **door** between rooms; never a tile | 3,484 edges |
| `3` (隠し平地) | **hidden door**; never a tile | 267 edges |

Odd/odd corner cells and the frame row and column are 0 in all 172 maps.

Consumers:
- SETROOM@`0x374..0x3b8`
- MVSEEK@`0x280..0x2c6`
- DRAWMAP@`0xae0`/`0xb01`
- INIT2's direction tables `map_neighbor_dx`/`dy` (`0x2a2d9a`/`0x2a2d9f`)

Example: labyrinth 2, floor 1 (4×9 tiles). The first five grid rows below the frame are shown; `.` is 0,
`2` and `13` are terrain tiles or room interiors, and `1` is a door.

```
 .  .  .  .  2  .  .  .  .
 .  .  .  .  1  .  .  .  .
 .  .  .  . 13  .  .  .  .
 .  .  .  .  1  .  .  .  .
 .  . 13 13 13 13 13  .  .
```

**Selecting and loading a floor.**

1. FIELD@`0x0` maps the stage id (`current_stage_id`, `0x378b`) to a labyrinth and floor through STINIT2
   (`0x141134`/`0x14151c`). When the stage id is 0, FIELD@`0x22` searches in the other direction.
2. Unless MapContinue (`0x4290`) is 1, FIELD@`0x4df` runs the MPINIT hook chain (`0x3d42c0[32]`), then the
   STINIT chain (`0x3d42e0[32]`). INIT2 registers the base scripts in slot 0, and each append AUTORUN registers
   its own slot.
3. When MapContinue is 0, FIELD@`0x69e` copies the template into the live grid `0x4fde5` (53×53).

Tiles are 100 px. The camera bounds run from 150 to 50+100·W (and 50+100·H).

**Runtime map state** lives beside the terrain grid:

| address | contents |
|---|---|
| `0x50bb7[27×27]` | room id per tile; SETROOM fills it by flooding across room-joining edges |
| `0x50ec2` | room count |
| `0x50ec3` / `0x51135` / `0x513a7` / `0x51619` | room bounding boxes |
| `0x5188b` | the room's HAINIT hall art |
| `0x508de[27×27]` | territory owner per tile, as a faction bitmask: 2 player (blue), 4 red, 8 green, 16 yellow. CALCOCC paints whole rooms; DRAWMAP and DRAWMINIMAP colour by it. |
| `0x4fde0[5]` | occupation percent per faction |

**Per-stage persistence** (capacity 1000 stages):

| address | contents |
|---|---|
| `0x1ec65[1000×100]` | revealed-tile bitmask, indexed `[stage][ty]`, one bit per `tx` |
| `0x5625[1000×100]` | player-owned tiles, saved at stage end |
| `0x523d` | occupation percent, shown by SELSTAGE. At 100 %, entering the stage reveals and claims everything. |
| `0x4685` | visited |
| `0x4a6d` | cleared |
| `0x4e55` | all objectives complete |
| `0x1dcc5[1000×4]` | objective reward bits |
| `0x429d` | availability bits, set by CHECKSTAGE |

FIELD@`0x6a5` clears a stage's fog and occupation while the stage is uncleared, and during arena battles.

**Digging and opening passages** works by changing the terrain:

- SETLAND writes `set_land_terrain_id` (`0x2a3510`) over a doubled rectangle (parameters `0xfec0c..f`) and
  reveals it.
- DELLAND zeroes a rectangle and its edges, and hides it again.
- RESETLAND restores cells from the template.

STAGEREADY, STAGEROUND, STAGECLOSE and SC scripts call these. DIG.BIN is item gathering, described below.

### Terrain (LAINIT)

LAINIT is a table of 40 terrain slots. Names are at string `0x289b+id`; the other fields are:

| address | field |
|---|---|
| `0x2a3511` | texture slot |
| `0x2a3539` | room-join flag |
| `0x2a3561` | visibility class: 0 void, 1 known from the start, 2 must be explored, 3 hidden (needs 探索) |
| `0x2a3589` | element (names at `0x6212`) |
| `0x2a35b1` | move cost |
| `0x2a35d9[40×2]` | two alternative required skills: 1 飛行, 2 潜水, 4 探索, 5 耐熱, 7 耐電, 8 土流, 11 進行不可 (no unit has it) |
| `0x2a3629[20]` | default texture per slot (`MP000A..N.AGF`) |

Terrains come in pairs: a visible variant (class 1) and an unexplored variant (class 2). `$3$` adds 神殿 and
砂塵地帯, and texture slots 6 and 14. Himegari has a terrain combat matrix; Kamidori does not. Terrain bonuses
come from element-affinity skills in CALCSUPPORT.

### Stage catalog (STINIT2) and per-stage loader (STINIT)

**STINIT2** is a 1000-slot catalog with 209 named stages; names are at `0x24b3+id`. Every write is keyed by
`(address − base) // stride`. The generic extractor gets this wrong because each record's labyrinth and floor
writes come before its name.

| field | address |
|---|---|
| select-screen background | `0x140d4c` |
| labyrinth / floor | `0x141134` / `0x14151c` |
| required party units | `0x141904[×10]` |
| prerequisite stage (labyrinth / floor) | `0x144014` / `0x1443fc` |
| required / forbidden story flags | `0x1447e4[×7]` / `0x14633c[×7]` |
| recommended level | `0x147e94` |
| objective rewards, four slots each: money / item / 評価 / おまけポイント | `0x14827c` / `0x14921c` / `0x14a1bc` / `0x14b15c` |
| boss flag (the ◆ stages) | `0x14c4e4` |
| event-entry flag: the stage has no map and runs the SCJUMP chain instead | `0x14c8cc` |
| sort key | `0x14ccb4` |
| unknown; no reader found | `0x14c0fc` |

Stage ids come in blocks per labyrinth: labyrinth 2 uses 1..45, labyrinth 3 starts at 51, labyrinth 4 at 71,
and so on. Floors 11 and above are side or companion stages. Labyrinth −1 uses ids 900+floor: arena floors
11–45 and 75–77, story battles 51–68, and `$3$` 69–74.

Every stage that has a map has an MPINIT block. The 38 stages without a map are exactly the event-entry stages.

**STINIT** loads one stage. It runs on demand and has 181 guards on `0x378b`. Append layers repeat the guard
chain and add stages or slots without overwriting existing cells.

Stage header:

| field | address |
|---|---|
| background textures | `0x140414[2]` |
| tint | `0x140416` |
| BGM | `0x140417` |
| texture overrides (−1 = LAINIT default) | `0x140432[20]` |
| turn limit / its outcome | `0x14041a[2]` / `0x140418[2]` |
| victory/defeat block | starts at `0x14041d` |
| alliances | `0x14042c[5]` |
| reward-gating flags | `0x140424[4×2]` |
| objective strings / description / reward texts | `0x249f[2×6]` / `0x24ab[4]` / `0x24af[4]` |

The stage also defines three kinds of slot:

- **50 object slots:**
  - position: x `0x140446`, y `0x140478`
  - OBINIT type `0x1404aa`, base value `0x1404dc`
  - parameters `0x14050e`/`0x140540`
  - reinforcement and difficulty fields
  - flag tables `0x140608[×7]`/`0x140766[×5]`/`0x140860[×5]`
- **30 enemy slots** starting at `0x14095a`: unit id `0x140a2c`, level range and divisor, routine sets, and
  flags.
- **10 fixed ally slots** at `0x140cc0`.

### Map objects (OBINIT) and hall art (HAINIT)

**OBINIT** defines object types 1..72. Names are at `0x28c3`. Descriptions are at `0x2927+id*2`, which the
generic extractor misreads as extra records. Fields:

| field | address |
|---|---|
| sprite | `0x2a363e` |
| state mode | `0x2a37ce` |
| command | `0x2a412e` |
| battle deltas | `0x2a3d46[×7]` |
| size, frames, use limit, key flag | not yet located |
| required skill (32 = 開錠) | not yet located |

Per-stage object state: owner `0x43655`, use count `0x37305`.

**HAINIT** places room artwork. It has 1000 slots; ids 1..181 are used (121 base, 15 `$1$`, 2 `$2$`, 11
`$3$`).

| field | address |
|---|---|
| texture (`TPnnn.AGF`; SETHALLTEX@`0x88`) | `0x2a4193` |
| labyrinth (−1 = disabled) | `0x2a457b` |
| floor | `0x2a4963` |
| tile x / y; SETROOM looks up the room at `0x50bb7[y*27+x]` and stores the hall in `0x5188b[room]` | `0x2a4d4b` / `0x2a5133` |
| required / forbidden flags, used for before/after variants | `0x2a551b[×2]` / `0x2a5ceb[×2]` |

### World map, gathering, arena

**AMINIT2** is the world-area catalogue and runs at boot. Ids run 1–38; `$1$` adds 31 and 33, and `$3$` adds
34–38.

| field | address |
|---|---|
| name | `0x241d+id` |
| kind (1 labyrinth, 2 town) | `0x13e93c` |
| travel days | `0x13ea68` |
| MI picture | `0x13e874` |
| world-map x / y | `0x13e9a0` / `0x13ea04` |

**The labyrinth id is the area id:** ALLMAP@`0xc7a` copies `0x3612` into `0x428e`.

**AMINIT** runs on demand as hook `0x3d41c0`. It writes the list order and the area state `0x3613` (bit 0
discovered, bit 1 enterable). When the day counter `0x3697` reaches 30, the player is forced back home.

**DIINIT** holds the **gathering ("dig") drop tables**: 200 tables × 10 slots.

| field | address |
|---|---|
| threshold | `0x2a7b66` |
| item | `0x2a8336` |
| required flag | `0x2a8b06` |
| forbidden flag | `0x2a92d6` |

The table index is (object type − `0x20`)·100 + payload; type `0x20` is 採取 and `0x21` is 発掘. DIG.BIN and
REWARD.BIN use it. DIMES is an unused stub.

**ARINIT** lists 41 arena battles in four tabs. Each maps to a stage (labyrinth −1, floor) through
`0x3c5a82`/`0x3c5ae6`. Win counts are in `0x5385e`.

**Open questions:**
- Who reads `0x14c0fc` and the camera globals `0x4295..0x4298`?
- What does MapContinue ≥ 2 do?
- What does MVSEEK's ×3 move-cost branch do?
- What does `0x140540` mean for chests and gather points?
- What are OBINIT's `0x2a3832` and `0x2a3896`?


## Quests, progression and content registries

**Flag convention.** An authored flag id `v` is the cell `G[0x50c+v]`: scripts read it with
`lookup-array 0x50d [v-1]` (CHECKCONTRACT@`0x131`, FIELD@`0x8a81`).

### Hook chains

The on-demand tables run as **hook chains**. Each chain is a 32-slot array at `0x3d40c0 + 0x20*k`. INIT2 puts
the base script in slot 0, and each `$n$AUTORUN` writes slot n. The SCJUMP chain is the exception: `$2$` and
`$3$` prepend to it, so its effective order is `$3$`, `$2$`, base, `$1$`. Callers stop walking the SCJUMP chain
when a handler clears `0x2c81`.

| chain | handler | chain | handler |
|---|---|---|---|
| `0x3d40c0` | CLOSE | `0x3d41e0` | SIINIT |
| `0x3d40e0` | START | `0x3d4200` | SHINIT |
| `0x3d4100` | STAGEREADY | `0x3d4220` | CSINIT |
| `0x3d4120` | STAGECLOSE | `0x3d4240` | EVINIT |
| `0x3d4140` | STAGEROUND | `0x3d4260` | CCINIT |
| `0x3d4160` | SETSTAGEFLAG | `0x3d4280` | COINIT |
| `0x3d4180` | SCJUMP | `0x3d42a0` | CDINIT |
| `0x3d41a0` | BTANINIT | `0x3d42c0` | MPINIT |
| `0x3d41c0` | AMINIT | `0x3d42e0` | STINIT |

### Quests (QUINIT)

QUINIT has **187 quests**, not the 744 records the generic output shows: that output turned each quest's nine
description lines into extra records. The 187 break down as:

- 153 journal quests (ids 1..510);
- 14 auto-completing reward notices (ids 1001..1015);
- 20 disabled `仮` placeholders, gated on flag 10000.

Append layers only add ids: `$1$` 451..456, `$2$` 461..463, `$3$` 471..510 and 1013..1015. Every array is 2000
wide.

| array | base | shape | meaning |
|---|---|---|---|
| name | str `0x625f` | [2000] | non-empty means the quest exists |
| description | str `0x6a2f` | [2000×9] | line 0 header, 1–4 overview, 5–6 clear condition, 7–8 failure condition (INFO2@`0x1171`) |
| requester / category | str `0xb07f` / `0xb84f` | [2000] | |
| requester icon | `0x3b6e92` | [2000] | `IC3xxx.AGF` |
| journal sort key | `0x3b7662` | [2000] | |
| deadline days | `0x3b7e32` | [2000] | 0 = none |
| prerequisite quest | `0x3b8602` | [2000] | never authored |
| appear: required / blocking flags | `0x3b8dd2` / `0x3ba542` | [2000×3] | |
| clear flags (all set → cleared) | `0x3bbcb2` | [2000×3] | |
| failure trigger flags / failure result flag | `0x3bd422` / `0x3beb92` | [2000×3] / [2000] | |
| difficulty stars | `0x3bf362` | [2000] | 0..10 |
| 評価 reward (success, failure) | `0x3bfb32` | [2000×2] | |
| money reward | `0x3c0ad2` | [2000] | |
| reward items / quantities | `0x3c12a2` / `0x3c31e2` | [2000×4] | |

**Lifecycle.** A quest moves from posted to cleared, or to failed (by deadline or by flag), and then to settled.
There is no accept step. The quest state lives in `0xf9c5f`: bit 0 posted, bit 1 failed, bit 2 cleared, bit 3
settled.

- **Posting.** CHECKCONTRACT, called from ALLMAP, TOWN, INFO and SYSTEM4, posts quests.
- **Settling.** CHECKREWARD (STUDIO@`0x3ea`) runs RECEIPT for each finished quest. RECEIPT pays money, items and
  評価 (`0x7ea3a`).
- **Deadlines.** Elapsed days are tracked in `0xfa42f`. The outing day counter `0x3697` is added to them on
  settlement.

**Flag idioms:**

- A quest is offered by a story flag paired with an 8xxx scene flag.
- Chapter-entry flags fail quests: 151 = 4章, 201 = 5章, 401 = 終章.
- `$3$` chains bulletin-board quests: a quest is blocked by another quest's failure-result flag.

Himegari has no quest system, so all of this is new.

### Chapter objectives (FAINIT + FAMES)

FAINIT holds 126 objectives in 203 slots; INFO1 is the consumer. Each array is 2000 wide.

| field | address |
|---|---|
| chapter label | str `0xc14b` |
| objective text (empty = reserved row) | str `0xc91b` |
| sort key (written before the strings) | `0x3cac75` |
| message script (FAMES) | `0x3d1205` |
| required flags | `0x3cb445[×3]` |
| forbidden flags | `0x3ccbb5[×3]` |
| complete flags (all set → shown grey) | `0x3ce325[×3]` |

The current objective is in `0x3cac74`, which is also the FAMES selector.

### Scene dispatch (SCINIT + SCJUMP)

SCINIT maps a decision id to a scene through three arrays:

- **`0x1372dd`** — the script resource id. SYSTEM4@`0x2dc` reads it.
- **`0x1399ed`** — the SCJUMP **context tag**, not a chapter. Values 1..16 equal the `0x3070` dispatch branch
  that emits the decision.
- **`0x13c0fd`** — a queue priority. Only `$3$` writes it; the lower value wins.

The selector is `0xfec10`. There are 1,797 rows after merging: 1545 base, 78 `$1$`, 21 `$2$` and 153 `$3$`.

SCJUMP contexts (names are medium confidence):

| context | meaning |
|---|---|
| 1 | world map |
| 2..9, 14, 16 | dungeon (FIELD) timings |
| 10 | town or stage entry |
| 11 | town location visit |
| 12 | studio or system |
| 13 | SETSTUDIOCH |
| 15 | after quest settlement |

Many decisions are not dispatched directly. They are queued into town-location slots `0x13eba1` or
per-character studio slots `0x140386`, or chained from inside SC scripts. Viewed scenes are recorded in the
cross-save bank `0xf35a3`.

### Glossary, galleries, cards, CG substitution

**VIINIT + VIMES** is the glossary, with 163 entries; INFO8 is the consumer.

| field | address |
|---|---|
| name (`【category】term`) | str `0xd40b` |
| sort key | `0x3d20df` |
| unlock scenes | `0x3d24c7[×3]` |
| message script | `0x3d3c37` |

Unlock values are scene id + 1, checked against the `0xf35a3` bank. If column 0 is empty, the entry is always
listed.

**CGINIT** is the CG gallery: 1,251 records on 16 pages.

| field | address |
|---|---|
| gallery CG / save preview | `0xfec76[3000×2]` |
| page | `0x1003e6` |
| slot | `0x100f9e` |
| variant ordinal (0 = unlisted) | `0x101b56` |
| thumbnails | `0x103e7e[30×100]` |

**SPINIT** holds 52 H-scene replays, indexed `[heroine page][slot]`: the replay script is `0x136331[20×50]` and
the thumbnail `0x136719[20×50]`. EUINIT supplies the page tabs (`0xfec4e`).

**CDINIT2 / CDINIT** are dungeon field cards, as in Himegari but at Kamidori addresses. There are 85 cards:

| type | kind | count |
|---|---|---|
| 2 | item | 68 |
| 3 | money | 3 |
| 4 | recovery | 6 |
| 5 | trap | 7 |
| 6 | teleport | 1 |

CDINIT is the weighted generation list: selector `0x2a79d4`, weights `0x2a79d5`/3, card ids `0x2a7b01`.

**EVINIT** is not a data table. It is a set of 215 event-CG substitution rules:

- **Rule form.** `if CG == SRC && member k has form F (0x642c9+k): CG = TARGET`. The last matching rule wins.
- **Callers.** Scenes and CGMODE run it through chain `0x3d4240`.
- **Sprite rules.** CSINIT applies the same form-id scheme to standing sprites. It has 573 rules over 87
  sprites, and `$1$` adds 75.

**GSMES** is the new-game-plus help. The bits of `0x320f` select what carries over:

| bit | carries over |
|---|---|
| bit0 | 評価 |
| bit1 | home expansion levels |
| bit2 | synthesis rank |
| bit3 | money |
| bit4 | units |

**EUINIT** is the CGMODE/HMODE character roster:

| field | address |
|---|---|
| portrait / sprite | `0xfec12[20×2]` |
| costume slot | `0xfec3a[20]` |
| HMODE list | `0xfec4e[20×2]` |

Open questions:

- What is `0xfd492`? It suspends quest evaluation.
- What do the individual FIELD SCJUMP contexts mean?
- What does the 精気 card do?
- Who writes `0xfec3a`?
- Why do six SCINIT context tags disagree with SCJUMP?

# Modding Kamidori

Kamidori-specific facts for the generic guides. The identity block (game id, engine ABI, modules, tables,
patch points) is generated in [README.md](README.md#kamidori-alchemy-meister).

## Install and options

The expected install is `Kamidori/` beside the repository: the game with its official updates applied. The English
fan translation is a separate install, `Kamidori_Translated/`, with its overlay in `patch/`. Every `Ome.Cli`
command needs the game options:

| Install | Options |
|---|---|
| untranslated | `--game kamidori --game-root ../Kamidori` |
| translated | `--game kamidori --game-root ../Kamidori_Translated --overlay-root patch` |

- Port: `.\run-godot.cmd -Kamidori`, or `.\run-godot.cmd -Kamidori -TranslationPatch` for the translated install.
  Add `-Mod PATH` for a mod.
- Driver, translated:

  ```text
  py -3.11 -X utf8 tools/drive.py --game kamidori --game-root ../Kamidori_Translated port start --mod PATH --port-arg=--overlay-root --port-arg=<absolute path to Kamidori_Translated\patch> --port-arg=--allow-bmp-as-agf
  ```

  The game's points include `title.start` (Game Start on the title screen) and `newgame.begin` (Begin Game on
  the next screen); define others with `point NAME X Y`.
- Native AGE on the untranslated install shows Japanese dialogs, so `dialogs` and `answer` use Japanese labels:
  the close dialog is 終了, with `プログラムを終了する(&E)`, `タイトルに戻る(&T)`, `OK` and `ｷｬﾝｾﾙ`.
- The menu bar, the fullscreen strip and the 終了 dialog are the port's own, so both installs have them. The driver
  reads and answers 終了 by its Japanese texts on the port, whatever language it shows. On native AGE, the translated
  install's `agerc.dll` shows 終了 in English: Quit, with `Exit(&E)`, `'Return to title(&T)'`, `OK` and `Cancel`.
- 設定, ﾊﾞｰｼﾞｮﾝ情報 and 動作環境 are the port's own options panel, so both installs have them, although the
  untranslated install's `AGERC.DLL` is packed. While CONFIG runs (op `0x142`'s gate is 0) its ﾒｯｾｰｼﾞ, ｻｳﾝﾄﾞ and
  その他 sections are read-only, as native's 設定 cannot open then. The port's shell is Japanese unless `OME.INI`'s
  `ui:Language` or `--language en` picks English; native AGE on the translated install shows its `agerc.dll`'s
  English. The driver's `option` and `options` steps set and list them on both targets without labels, and
  `tools/drive/scenarios/kamidori-shell.steps` checks 終了 and three options on the clean install.
- Registries for fragments: `py -3.11 -X utf8 tools/globals_build.py --game kamidori --build`.

## The translation is a different base

The translation replaces scripts such as `SC0000.bin` with translated ones, which have different bytes and
offsets. A fragment matches one of them only. Print fingerprints and decompile with the same options you play
with, and ship two fragments if the mod should work on both.

| `SC0000.BIN` | Fingerprint |
|---|---|
| translated | `sha256:54d6bc4d8cf85e32903808979d16ff851f10e62f06dcb8ab87bc8076a31c1e30` |
| untranslated | `sha256:e8db06e1287c557533e93380084358dbc28beb684b3344f3890852514ad97f58` |

Both installs hold loose `$n$*.BIN` append records in their root. AGE loads a root-loose `$n$` file in place of
the append catalog's record of that name, and so does the port, with or without `--overlay-root patch`. The
untranslated install has 17 of them, its official data updates (ITINIT, SKINIT, SCJUMP and others); fingerprint and
decompile those records from the root file. The translated install replaces 11 of the 17 and adds 75 more, so
append content shows in English there with or without the overlay (`../../asset-resolution-re.md`). The translated
records' strings are partly damaged, so do not copy code from them.

## The translation as a language package

One command turns the translated install into a package that shows the untranslated install in English in the port
([languages.md](../languages.md), "From a translation made in place"):

```text
Ome.Cli omp text import-scripts --game kamidori --game-root ../Kamidori --from ../Kamidori_Translated --overlay-root patch --allow-bmp-as-agf --output build/games/kamidori/import-en --package --verify
```

- The package holds 48,586 entries in 217 catalog files and 232 images: 207 from `patch/`, and the 25 `$n$`
  append images from the translated root. All of them are BMPs under AGF names, written as PNG.
- The manifest's `glyph_delay_scale` comes out at 0.48.
- Play it with `.\run-godot.cmd -Kamidori -Mod build\games\kamidori\import-en`, and choose English in the options
  panel's OME section (or pass `--language en` to the port).
- Keep it under `build/`. It is the translators' work, for playing on your own machine only.
- Kamidori's text font is the player's choice in its config screen (`CONFIG2`: メイリオ, ＭＳ ゴシック or
  ＭＳ 明朝), kept in `SAVE.DAT`. The clean install starts with メイリオ, a proportional face (ＭＳ 明朝 where it is
  missing). The translated install's `INITCONFIG` and `CHECKCONFIG` settle on ＭＳ ゴシック instead. Those are face
  names, not text, so the importer reports them and carries none: to play in the translated install's face, choose
  ＭＳ ゴシック in the config screen.
- `SYSTEM4` selects aspect mode at boot, so the port lays English out on the cells native AGE gives the same
  characters ([languages.md](../languages.md), "Fonts"). On the pages localization gate 12 compares, every name plate
  and every run of text starts within a pixel of native AGE on the translated install
  (`../../localization-import-test.md`). Stat boxes and item tables are not compared yet (`../../backlog.md`).

## First mod values

The values for [first-mod.md](../first-mod.md), on the translated opening scene:

| Step | Value |
|---|---|
| Page | The prologue's first page. |
| Site | `0xd0a` |
| Fingerprint | the translated one above |

```text
oms 1
patch "SC0000.BIN" fingerprint "sha256:54d6bc4d8cf85e32903808979d16ff851f10e62f06dcb8ab87bc8076a31c1e30"
use kamidori.globals
use kamidori.adv

before site 0xd0a {
    "[MOD] Hello from my first mod."
}
```

The finished mod is `oms/examples/mods/example.kamidori-prologue-line`.

## Dialogue lines

The prelude's template is `line(text, speaker = none, voice = none, form = form_name)`:

```text
"[MOD] A narration line."
水那 "「…………」" (voice: 0x1be, form: form_descriptor)
```

`form` picks what the nameplate shows:

- `form_name`, the default (leave it out);
- `form_alternate`;
- `form_descriptor`, a description shown before the character is introduced;
- `form_unknown`, `？？？？`.

Speakers come from `unit_speaker_names` (`enum speaker` in `oms/kamidori/adv.oms`).

## Data edits

A data file ([../data-files.md](../data-files.md)) changes the unit, item and skill tables, and each floor's
enemies, with no Lua and no script. `kamidori-base` accepts the kind `kamidori.tables`. It applies each file's boot
tables at `kamidori.data.layered`, after every layer has written them and before INIT2's own work reads them, and a
stage's part at `kamidori.stage.loaded`, each time the floor's loaders have run. This one changes the enemy unit 761
(緑プテテット), which appears in the first stage; the comments give the game's own values:

```toml
# data/slimes.toml; the manifest names it with [[data]] kind = "kamidori.tables", file = "slimes.toml"
[unit.761]
stats.max_hp = 150         # 12
experience_reward = 999    # 10
drop_chance.drop_1 = 100   # 30
```

- **Tables and fields** are those of `../../global-reference-kamidori.md`, "Tables", by the names in its `field`
  column: `unit` (records 0 to 999), `item` (0 to 1999) and `skill` (0 to 999). A field with a row takes its columns
  by the names the reference gives (`stats.max_hp`), and by number only where a column has no name.
- **Saved fields are refused:** `item.inventory_count`, `item.growth_exp`, `item.growth_level`,
  `item.costume_enhancement_grid` and `item.costume_gem_sockets`. They lie below the saved prefix, so a load would
  bring the saved value back over the edit.
- **The shape.** `kamidori-base` ships the kind's shape, `shapes/tables.schema.json`, generated from the same record
  map as the tables above ([../data-files.md](../data-files.md), "Shapes"). So the link, `omp check` and `omp build`
  refuse a table, field or column the reference does not name, a saved field, an id or a column out of range and a
  value of the wrong kind, with the file's line, before the game starts. Text that code page 932 cannot encode
  passes the shape; `kamidori-base` refuses it at the file's first delivery.
- **A floor's enemies** are the `stage_enemy` table, which a file names through the stage: `stage.51.enemy.3` is
  stage 51's enemy slot 3 (stage ids 1 to 999, of which STINIT writes 1 to 968 and the appends up to 977; slots 0 to
  29, of which FIELD reads 1 to 29). Its fields are the reference's: `unit_id` (0 for an empty slot), `min_level`,
  `max_level`, `auto_level_scale_divisor`, the spawn (`spawn_tile_x` and `spawn_tile_y`, or `spawn_object_slot`),
  `faction_id`, `difficulty_mask`, `first_visit_only`, `random_selection_weight`, and four rows whose columns have no
  names and are given by number: `movement_routine_set_ids` and `battle_routine_set_ids` (columns 0 to 2),
  `required_story_flags` (0 to 6) and `forbidden_story_flags` (0 to 4). None of them is saved.
- **Which slots the floor places.** At a floor's start FIELD goes through slots 1 to 29 (`FIELD@0xaa5`) and skips a
  slot whose gates fail: an empty `unit_id`, a `difficulty_mask` without the current difficulty, `first_visit_only` 2
  on a cleared floor, a required story flag not set or a forbidden one set (`FIELD@0x49a6`). A slot whose
  `random_selection_weight` is 0 is placed there and then. One whose weight is not 0 joins the pool of the spawner
  its `spawn_object_slot` names; spawners place from their pools at the floor's start (`FIELD@0xc8a`) and send
  reinforcements from them later (`FIELD@0xa040`). So an edit shows at the start only on a slot placed then: at
  ◆開けた涼風西湖畔◆ (stage 51), when シセティカ湖 has just opened (before the first visit to 蒼月の南湖畔), only
  slot 1, ハルピュア, whose spawner places it. This file puts 緑プテテット there:

  ```toml
  # data/stages.toml
  [stage.51.enemy.1]
  unit_id = 761              # 331, ハルピュア; 761 is 緑プテテット
  ```

- **The stage part holds through the floor.** FIELD runs the floor's loaders at its start, before it places the
  units, and again each time it is entered again during the floor; INFO's third tab, アイテム情報, runs them again
  too when its 獲得地点 view is opened on the floor (below, "FIELD runs the layers more than once a floor"). The part
  is applied after each run, so the edit changes what the floor places, and the reinforcements a spawner sends later
  follow it. A save made on the floor keeps the units placed (`battle_unit_unit_ids`): loaded without the mod, the
  table is the floor's own again, and a unit the edit placed is still on the field.
- **The stage list's preview shows the edit.** The stage list (SELSTAGE) runs the loaders of the stage under its
  cursor for its preview, which lists the enemies whose gates pass, and `stage.loaded` is raised after them, so the
  preview shows a file's edit. That stage is not being played: nothing is placed, and the list runs them again each
  time the cursor moves onto another stage.
- **INFO's 獲得地点 view reads no enemy.** To find the floors whose dig points yield an item, it runs every stage's
  loaders and reads the stages' objects, which no data file edits; it raises no event for those stages. Off a floor
  (INFO opened from the town or the workshop) it raises no event at all and leaves `current_stage_id` at 1000.
- **A character keeps a copy of its definition.** When a unit joins, UNITECH copies part of its definition into the
  character's saved state (`../../kamidori-data-tables.md`, "Runtime character state"), which the game uses from
  then on. An edit to these fields reaches a character who joins after it, not one who has joined, and what was copied
  while the mod was on stays in the save when the mod is removed. UNITECH takes the character through
  `unit.canonical_character_id`, refuses one of 100 or more (`UNITECH@0x6d`), and makes the first join's copies when
  the character's `character_levels` is 0 (`UNITECH@0x13a`). `example.kamidori-unit-stats` raises ウィル's maximum HP:
  `tools/drive/scenarios/port/kamidori-unit-stats.steps` loads a save made after he joined with it (below,
  "Examples"), where his definition holds the edit and his saved stats the save's own.

  | Field | Saved value | When |
  |---|---|---|
  | `unit.default_costume_item_id` | `character_costume_item_id`, and the costume itself | each join of a character with no costume yet or at level 0 (`UNITECH@0xf7`); the costume, through ADDITEM, when the inventory has none (`UNITECH@0x129`) |
  | `unit.initial_party_capacity` | `party_cost_capacity` | character 1's first join (`UNITECH@0x156`) |
  | `unit.party_cost` | `character_party_cost` | the first join (`UNITECH@0x169`) |
  | `unit.initial_affinity_tenths` | `character_affinity`, times 10 | the first join (`UNITECH@0x17c`) |
  | `unit.starting_level` | `character_levels` | the first join (`UNITECH@0x191`) |
  | `unit.initial_weapon_level` | `character_weapon_level` | the first join (`UNITECH@0x1b0`) |
  | `unit.stats` | `character_stats`, all 14 columns | the first join (`UNITECH@0x1e1`) |
  | `unit.initial_stat_caps` | `character_stat_caps` | the first join (`UNITECH@0x208`) |
  | `unit.element_resistance` | `character_resistances` | the first join (`UNITECH@0x22f`) |
  | `unit.skill` | `character_skill_list`, with `character_learned_skill_flags` set for each, and the first four in the roster slot's `roster_equipped_skill_ids` | the first join (`UNITECH@0x256`, `0x2a7`); the roster's copy at the character's first place in the roster (`UNITECH@0x4fe`) |
  | `unit.initial_class_title` | `character_class_titles` | the first join (`UNITECH@0x3ac`) |
  | `unit.initial_equipment` | the roster slot's `roster_equipment_item_ids` | the character's first place in the roster (`character_join_flags` bit 0 clear, `UNITECH@0x417`; the copy at `0x4ac`), and a new game's first party (`GAMESTART@0xb0d`) |
  | `unit.stat_growth` | `character_stats` and `character_stat_growth_fractions` | the first join, for each level it adds past the starting level (`UNITECH@0x325`), and each level up (`ADDEXP@0x2a5`). The rate is read each time, so an edit changes later levels' growth, and the growth already added stays |

- **The battle units are copies too.** When FIELD places a unit, SETEN (an enemy) or SETCH (a character) writes its
  battle slot (the `battle_unit_*` arrays) from the tables: an enemy's level from its slot's `min_level`,
  `max_level` and `auto_level_scale_divisor` and the unit's `starting_level`, `level_cap` and
  `auto_level_scale_divisor`, and its `stats`, `element_resistance`, `skill`, `initial_equipment`, `party_cost`,
  `graphic` and `initial_class_title`, with the slot's `movement_routine_set_ids` and `battle_routine_set_ids`; a
  character's from its saved state above, with `unit.graphic` and its costume's `item.costume_graphic_asset_ids`.
  CALCREVISE works out the effective values from the equipment and skills (`item.stat_modifiers`, `min_range`,
  `max_range`, `critical_rate_bonus`, `affinity_level_deltas`, `costume_battle_variant` and `granted_skill_id`, the
  skills' ranges, `unit.cutin_element`), and EQUIP the condition floors (`item.equipped_condition_levels`, into
  `unit_condition_baseline_levels`). The battle units are saved, so a save made on a floor keeps them as they were
  placed and worked out, with the mod or without it.
- **Play keeps its results,** as it does without a mod: while the mod is on, an edit changes what an action gives or
  costs, and the result is saved. `item.price` is paid from `player_money` when an item is bought (SHOP), and its
  `sell` column is added to it when one is sold (SALE, `CHANGEARM@0x103`, `USEITEM@0x20d`) and for the part of a
  stock past 999 when an item is added (`ADDITEM@0x8d`); `unit.bestiary_knowledge_gain` is added to
  `bestiary_knowledge_percent` for each unit beaten (`COMMITBTL@0x50e`); `item.rank` is added to a saved tally in
  the shop's settlement (`G[0x63c89]`, unnamed, `CALCSETTLE@0x4ac`), and the settlement's sales, which CALCSETTLE
  works out from each item sold's `price.retail_base` (`CALCSETTLE@0x4de`), to a saved cell (`G[0x3698]`, unnamed,
  `SHOWSETTLE@0x2b`); `unit.experience_reward` sets the experience a battle awards (`COMMITBTL@0x3f9`, which
  ADDEXP adds through `shared_scratch_value`), and the levels and stats that follow are saved; and
  `item.growth_mastery_reward_items` names the item added to the inventory when an item's growth is mastered
  (`CHECKIMPROVE@0x139`, through ADDITEM).
- **Every other field** is read where the game uses it, so an edit reaches a save made before it. These lists come
  from a scan of the installed scripts that follows each field's value through a script's locals and block copies
  into a cell the save keeps, and into a global it leaves for a script it calls or takes back from one, and from
  reading each cell it found. The scan does not follow a value left in a global for a script that runs later without
  being called there (another screen), so a copy made that way can be missing.
- **Old names.** No table or field has been renamed yet. When one is, the old name keeps working until
  `kamidori-base`'s next major version, and `diagnostics/lua.log` names each old name a file uses.

A boot script does the same with OMS (`oms/examples/kamidori-gate6/OMSAUTO.oms`), and writes the same three cells:

```text
with unit[761] {
    .stats.max_hp = 150
    .experience_reward = 999
    .drop_chance.drop_1 = 100
}
```

The unit tables sit above the saved range, so boot rebuilds them with the edit on every start, and the edit holds
after loading a save. Saves made before the edit pick it up too. `../../kamidori-data-tables.md` explains how the
init scripts fill each table.

## Patch points

`kamidori.experience.after_calculation` (`ADDEXP.BIN`) is reached once per experience award, after the costume
bonus. `L[0x1]` holds the amount the character receives: the amount the caller passes, times 100 plus 25 for each
experience gem (`0x118`) in the character's costume sockets, over 100. The point lists both installs' ADDEXP, which
differ only in two strings:

| `ADDEXP.BIN` | Fingerprint |
|---|---|
| untranslated | `sha256:606b83e952a3ce45ee406a2383710734f1ea707288f5751b5614be50e3edd7b6` |
| translated | `sha256:23bb6ac1e1aa83ef248bbacba4cadd71801fa07188aed3938c4768034f52b40b` |

```text
oms 1
patch "ADDEXP.BIN" fingerprint "sha256:606b83e952a3ce45ee406a2383710734f1ea707288f5751b5614be50e3edd7b6"
use kamidori.globals

local int exp @ 0x1

before site kamidori.experience.after_calculation {
    exp = exp * 2
}
```

A fragment names one fingerprint, so this one patches the untranslated install; put the translated fingerprint in a
second fragment for the other. A Lua hook or event subscriber names no fingerprint and links on both.

In Lua ([../lua.md](../lua.md), "Game names"), `kamidori-base` gives the game's tables by name.
`require("kamidori-base").table("unit_condition_levels")[unit].charm` reads a battle unit's charm level. The table
has 50 rows, which end where `unit_condition_remaining_turns` starts. The names are those of
`../../global-reference-kamidori.md`. Levels and experience belong to the character, not the unit:
`character_levels` and `character_experience` are indexed by `current_character_id`, the unit's canonical
character.

`kamidori-base` declares seven game events ([../lua.md](../lua.md), "Game events"). The experience award is raised at
the experience point:

| Event | Field | Type | Access | Cell |
|---|---|---|---|---|
| `kamidori.experience.award` | `amount` | int | rw | `L[0x1]`, the experience the character receives |
| | `unit` | int | r | `current_unit_id` |
| | `character` | int | r | `current_character_id` |
| | `level` | int | r | `character_levels[current_character_id]` |

A subscriber needs no `vm`. `oms/examples/mods/example.kamidori-double-exp-event` doubles every award with
`event.amount = event.amount * 2`, and leaves the same state as the patch above. An amount of 0 or less, or a
character at the level cap, gains no experience. Weapon experience is awarded after the point and does not read the
amount.

The other six follow the boot and each floor. Each is raised where INIT2 or FIELD has walked the chains the appends
register their handlers in (`../../kamidori-data-tables.md`, "Hook chains"), so a handler runs after the base game's
and every append's. Each point lists both installs' scripts, which put it at the same site. Their fields are
read-only:

| Event | Raised | Fields |
|---|---|---|
| `kamidori.data.layered` | `kamidori.boot.data_layered` (INIT2, after op `0x143`): every layer has written its tables, the appends' included, and the boot has not used them yet | none |
| `kamidori.data.ready` | `kamidori.boot.data_ready` (INIT2's exit): every data table is built, and INIT2's own work on them is done | none |
| `kamidori.stage.loaded` | `kamidori.stage.after_loader` (FIELD): every layer's MPINIT and STINIT have run; `kamidori.stage.after_info_reload` (INFO3): INFO's third tab, アイテム情報, has run them again for its 獲得地点 view on the floor; and `kamidori.stage.after_list_preview` (SELSTAGE): the stage list has run them for its preview of the stage under its cursor, which is not being played. A data file's stage part is applied there | `stage` (`current_stage_id`) |
| `kamidori.stage.ready` | `kamidori.stage.after_ready_handlers`: once per floor, at its start, with the units placed | `stage` |
| `kamidori.stage.round` | `kamidori.stage.after_round_handlers`: each player turn after the first | `stage`, `turn` (`current_stage_turn`) |
| `kamidori.stage.close` | `kamidori.stage.after_close_handlers`: the floor ends | `stage`, `result` (`stage_result`: 1 won, 2 lost), `turn` |

- **Edit the boot's tables at `data.layered`.** An edit there lands where an append's edit lands. INIT2's own work
  after it writes no record table and reads the edited fields: at the load of a save made on a floor it rebuilds the
  field, and SETMAPTEX and READICON read `unit_canonical_character_id`, `unit_graphic_asset_ids` and
  `item_costume_graphic_asset_ids`. An edit at `data.ready` comes after that rebuild.
- **Data files are applied at `data.layered`,** among its handlers in load order. A handler at `data.ready` runs
  after every data file, whatever the load order; one that should take its place among them by load order
  subscribes to `data.layered`.
- **A load builds the data tables again** and raises `data.layered` and `data.ready` again while it restores
  (`maid.session.restoring` is true), so a handler that edits them makes its edit again.
- **FIELD runs the layers more than once a floor.** At a floor's start (`field_map_continue_mode` 0) it runs them,
  places the units and walks the ready chain. When FIELD is entered again during the floor (at the load of a save made
  on it, for one) it runs them again and raises `stage.loaded` again, but places nothing and raises no `stage.ready`.
  INFO's third tab, アイテム情報 (FIELD's menu, then INFO), runs every stage's loaders when its 獲得地点 view is
  opened for an item, to find the floors whose dig points yield it, then the floor's again, and `stage.loaded` is
  raised after that second run (`kamidori.stage.after_info_reload`, `INFO3@0xd20`). A handler that rewrites the
  layers' tables makes the same change each time.
- **The stage list raises `stage.loaded` for a stage that is not being played.** For its preview it runs the loaders
  of the stage under its cursor (`SELSTAGE@0x13d0` to `0x1405`), when it opens, each time the pointer moves onto
  another stage's panel or a key moves the cursor, and when the party screen comes back to it without setting out;
  `stage.loaded` is raised after each run (`kamidori.stage.after_list_preview`, `SELSTAGE@0x1408`), and `stage` is
  the stage under the cursor. Work that belongs once to a played floor goes to `stage.ready`.
- **`result`.** Withdrawing from a free floor (撤退, 探索を終了しますか？) closes it with 1, as winning does, and the
  floor's clear screen follows; forfeiting an arena match (試合を放棄しますか？) closes it with 2.

`kamidori-base`'s helpers ([../lua.md](../lua.md), "Game names"):

| Helper | Calls | What it does |
|---|---|---|
| `give_item(item, count)` | `ADDITEM.BIN` | adds `count` (-999 to 999, 1 when nil) of item `item` (1 to 1999) as the game does: the count stays at 0 or more, a count past 999 is paid out as money at the item's price, the item is marked obtained, the skills it grants are learned, and a key item (`item_category` 1 or 2) sets its story flag. It puts back `current_item_id`, `current_item_count_change`, and the `shared_loop_index` and `shared_scratch_value` ADDITEM uses, and returns `item_inventory_counts[item]`. |

## Catalogs, scenes and saves

- **Selectors.** The install ships three append catalogs (`APPEND01`–`03`), so a first mod's catalog normally gets
  selector 4.
- **Scene registry.** SCJUMP decision 0 already maps to SC0000. Writing a mod scene into
  `scjump_scene_script_resource_ids[0x0]` replaces the opening instead of preceding it. Pick the decision your
  scene belongs to.
- **Untested.** No Kamidori save-capable scene mod has been tested yet (`../../backlog.md`). Copy the skeleton from
  one of Kamidori's own append scenes, and test saving on native AGE as well as in the port.
- **Free cells.** The examples use the string cells `GS[0x9000]` and `GS[0x9001]` (`oms/examples/kamidori-gate6/`).

## Examples

| Path | What it shows |
|---|---|
| `oms/examples/mods/example.kamidori-prologue-line` | a patch against the translated opening |
| `oms/examples/kamidori-gate6/` | a boot-time data edit and a script called by packed id |
| `oms/examples/mods/example.kamidori-double-exp-event` | a Lua subscriber of `kamidori.experience.award`, without `vm` |
| `oms/examples/mods/example.kamidori-stage-enemies` | a data file that edits two floors' enemy slots, applied at `kamidori.stage.loaded`; `example.kamidori-stage-enemies-mistake` is its twin with a mistake no shape can say (a unit name code page 932 cannot encode), which the link takes and the boot reports; `tools/drive/scenarios/port/kamidori-stage-enemies*.steps` drive both through the stage list's preview, INFO's 獲得地点 view, a floor save and its load with the mod and without |
| `oms/examples/mods/example.kamidori-unit-stats` | a data file that raises ウィル's maximum HP, which reaches only a character who joins after it; `tools/drive/scenarios/port/kamidori-unit-stats.steps` loads a save with it |

The Kamidori scenarios start from saves in the clean install's story: one in ウィルの工房 (the workshop) when
シセティカ湖 has just opened, before the first visit to 蒼月の南湖畔, and two on 蒼月の南湖畔 (turns 1 and 5). Each
scenario's header names the save slots it loads. The repository does not ship them: make saves at those places, or
change the slots in the scenario.

# Modding Himegari

Himegari-specific facts for the generic guides. The identity block (game id, engine ABI, modules, tables,
patch points) is generated in [README.md](README.md#himegari-dungeon-meister).

## Install and options

- Expected install: `Himegari_Game/` beside the repository. `Ome.Cli` uses it by default, so Himegari commands need
  no game options. Pass `--game-root DIR` for another location.
- Port: `.\run-godot.cmd`, with `-Mod PATH` for a mod.
- Driver: `py -3.11 -X utf8 tools/drive.py port start --mod PATH` (`native` for AGE). Named points such as
  `adv.next`, `title.start` and `newgame.begin` are predefined.
- Registries for fragments: `py -3.11 -X utf8 tools/globals_build.py --build` and
  `py -3.11 -X utf8 tools/patch_points_build.py --build`.

## First mod values

The values for [first-mod.md](../first-mod.md), on the opening scene `SC0000.BIN`:

| Step | Value |
|---|---|
| Page | The third page. Its locator reads `SC0000 P003 · wait SC0000@0x8a6 · text SC0000@0x89e`. |
| Site | `0x877`: the `reset-adv-text-layout` after page 2's wait (`0x871`) and `end-text-line` (`0x874`). |
| Fingerprint | `sha256:029718d6c927f90f6dcb1b89851591be310b5889dd53d6c8892604ad1fc9a9f6` |

```text
oms 1
patch "SC0000.BIN" fingerprint "sha256:029718d6c927f90f6dcb1b89851591be310b5889dd53d6c8892604ad1fc9a9f6"
use himegari.globals
use himegari.adv

before site 0x877 {
    "[MOD] Hello from my first mod."
}
```

The finished mod is `oms/examples/mods/example.sc0000-page3`. `example.sc0000-page4` inserts before the fourth
page (site `0x8ac`) and composes with it. `example.sc0000-merge` replaces pages 3 and 4 (`0x874` through `0x8de`) and
conflicts with both.

## Dialogue lines

The prelude's template is `line(text, speaker = none, voice = none)`:

```text
"――かつて、戦いがあった。"                         // narration
魔王 "「元の台本は、一文字も書き換えられていない。」"   // a speaker
魔王 "「…」" (voice: 0x34e)                        // with a voice clip
```

Speakers are CNINIT's character names (`enum speaker` in `oms/himegari/adv.oms`). Names that repeat, or that are
not identifiers such as `？？？？`, carry their id (`speaker_0x10`).

## Data edits

A data file ([../data-files.md](../data-files.md)) changes the unit, item and skill tables, and each stage's
enemies, with no Lua and no script. `himegari-base` accepts the kind `himegari.tables`. It applies each file's boot
tables at `himegari.data.layered`, after every layer has written them and before TUNE reads them, and a stage's part
at `himegari.stage.loaded`, each time FIELD has run the stage's loaders. This one raises the experience unit 103
gives; the comment gives the game's own value:

```toml
# data/rewards.toml; the manifest names it with [[data]] kind = "himegari.tables", file = "rewards.toml"
[unit.103]
experience_reward = 40     # 30
```

- **Tables and fields** are those of `../../global-reference.md`, "Tables", by the names in its `field` column:
  `unit` (records 0 to 999), `item` (0 to 999) and `skill` (0 to 299). A field with a row takes its columns by the
  names the reference gives (`stat_modifiers.max_fs`), and by number only where a column has no name.
- **Saved fields are refused:** `unit.story_speaker_seen_flag`, `skill.acquired_flag` and
  `skill.info_revealed_flag`. They lie below the saved prefix, so a load would bring the saved value back over the
  edit.
- **The shape.** `himegari-base` ships the kind's shape, `shapes/tables.schema.json`, generated from the same record
  map as the tables above ([../data-files.md](../data-files.md), "Shapes"). So the link, `omp check` and `omp build`
  refuse a table, field or column the reference does not name, a saved field, an id or a column out of range and a
  value of the wrong kind, with the file's line, before the game starts. Text that code page 932 cannot encode
  passes the shape; `himegari-base` refuses it at the file's first delivery.
- **TUNE corrects the save against the edited items.** It lowers each saved tuning level to the last level its
  item's curve gives a bonus for (below, "Edit the boot's tables at `data.layered`"). A file that gives an item a
  shorter curve (`item.tuning_curve_ids`) lowers that item's saved levels, and they stay lowered when the mod is
  removed.
- **A unit keeps a copy of its definition.** When a unit joins, UNITECH copies part of its definition into its saved
  state, which the game uses from then on. An edit to these fields reaches a unit that joins after it, not one that
  has joined, and what was copied while the mod was on stays in the save when the mod is removed. UNITECH takes the
  unit through `unit.canonical_id` and `unit.roster_variant_ids` first, and makes the first join's copies for a unit
  below 100 whose `unit_current_levels` is 0 (`UNITECH@0x2cf`, `0x2e4`); a form change in 配下 (SALLY's first two
  actions) makes the same copies for a form used for the first time. `example.unit-stats` raises リリィ's maximum HP:
  `tools/drive/scenarios/port/himegari-unit-stats.steps` shows her saved stats take it at a new game, and
  `himegari-unit-stats-load-without.steps` that a save made then keeps it without the mod.

  | Field | Saved value | When |
  |---|---|---|
  | `unit.canonical_id`, `unit.roster_variant_ids` | the unit a party slot holds (`party_slot_character_id`) | each join (`UNITECH@0x2ab`) and each form change (`SALLY@0x1668`) |
  | `unit.starting_level` | `unit_current_levels` | the first join (`UNITECH@0x34b`) and a form's first use (`SALLY@0x16aa`) |
  | `unit.stats` | `unit_current_stats`, all 14 columns | the same (`UNITECH@0x37c`, `SALLY@0x16db`) |
  | `unit.starting_skill` | `unit_skill_ids` | the same (`UNITECH@0x3a3`, `SALLY@0x1702`); and FORT (SYSTEM4's mode 2) gives the unit it shows each skill of the row the game has not acquired yet (`skill_acquired_flags`, `FORT@0x3e7` to `0x42a`, through ADDSKILL) |
  | `unit.starting_equipment_item_id` | the party slot's item (`G[0x6868]`, unnamed), and the item itself | the slot's item at the first join (`UNITECH@0x338`); the item, through ADDITEM, at each join that equips (`UNITECH@0x27f`) |
  | `unit.stat_growth` | `unit_current_stats` and `unit_stat_growth_fractions` | the first join, for each level it adds past the starting level (`UNITECH@0x419`), and each level up (`ADDEXP@0x20b`). The rate is read each time, so an edit changes later levels' growth, and the growth already added stays |

- **The battle entities are copies too.** When FIELD places a unit, SETEN (an enemy) or SETCH (a party unit) writes
  its battle entity (the `entity_*` arrays) from the tables: an enemy's level from its slot's `min_level`,
  `max_level` and `auto_level_scale_divisor` and the unit's `starting_level`, `level_cap` and
  `auto_level_scale_divisor`, its stats from `unit.stats` and `unit.stat_growth`, and its routines
  (`movement_routine_set`, `battle_routine_set`), carried item (`starting_equipment_item_id`), attack
  (`default_attack_item_id`) and class title (`initial_class_title`); a party unit's stats come from its saved copy
  above. CALCREVISE works out an entity's effective stats and ranges from its equipment and skills
  (`item.stat_modifiers`, `min_range`, `max_range` and `granted_skill_id`, `skill.min_range_encoded` and
  `max_range_encoded`) each time the game calls it, among others when a unit is placed, levels up, or uses an item or
  a spell. The entities are saved, so a save made on a stage keeps them as they were placed and worked out, with the
  mod or without it.
- **Play keeps its results,** as it does without a mod: while the mod is on, an edit changes what an action gives or
  costs, and the result is saved. `item.essence_recovery_amount` is added to the user's side's essence when the item
  is used (`USEITEM@0x2fb`; the player's is `current_spirit`); `unit.summon_point_cost` is taken from
  `shared_spendable_points` at a summon (`SUMMON@0xe13`); `unit.essence_yield` is added to `current_spirit`, with
  a change to `familiar_alignment`, when 配下 releases a unit (`SALLY@0xd12`, `0xd49`); `unit.experience_reward` sets
  the experience a battle awards (`BTL@0x366e`, which ADDEXP adds through `G[0x62ccb]`, unnamed), and the levels and
  growth that follow are saved; and an item of the beaten unit's `unit.drop_item` row is added to the inventory when
  the battle drops it (`BTL@0x3939`, through ADDITEM).
- **Every other field** is read where the game uses it, so an edit reaches a save made before it. These lists come
  from a scan of the installed scripts that follows each field's value through a script's locals and block copies
  into a cell the save keeps, and into a global it leaves for a script it calls or takes back from one, and from
  reading each cell it found. The scan does not follow a value left in a global for a script that runs later without
  being called there (another screen), so a copy made that way can be missing.
- **A stage's enemies** are the `stage_enemy` table, which a file names through the stage: `stage.1.enemy.2` is
  stage 1's enemy slot 2 (stage ids 1 to 999, of which STINIT writes 1 to 170; slots 0 to 29, and no stage gives
  slot 0 a unit). Its fields are the reference's: `unit_id` (0 for an empty slot), `min_level`, `max_level`,
  `auto_level_scale_divisor`, the spawn (`spawn_tile_x` and `spawn_tile_y`, or `spawn_object_slot`), `faction_id`,
  `difficulty_mask`, `first_clear_only`, `random_selection_weight`, and four rows whose columns have no names and are
  given by number: `movement_routine_set_ids` and `battle_routine_set_ids` (columns 0 to 2), `required_story_flags`
  (0 to 6) and `forbidden_story_flags` (0 to 4). None of them is saved. OMS names the slots of the stage being
  played (`stage_enemy[2].unit_id`), as the loaders leave them. This file puts another unit in stage 1's slot 2:

  ```toml
  # data/stages.toml
  [stage.1.enemy.2]
  unit_id = 810              # 200, 緑プテテット; 810 is ハルピュア
  ```

- **The stage part holds through the stage.** FIELD runs the stage's loaders at its start, before it places the
  units, and again each time it is entered again during the stage (below, "FIELD runs the loaders more than once a
  stage"), and the part is applied after each run. So the edit changes what the stage places, and ADDEN, which places
  a slot later when a stage's handler calls it, reads the edited slot too. The placed units are saved
  (`entity_unit_definition_ids`): a save made on the stage with the mod and loaded without it has the stage's own
  table again, and the unit the edit placed is still on the field.
- **Old names.** No table or field has been renamed yet. When one is, the old name keeps working until
  `himegari-base`'s next major version, and `diagnostics/lua.log` names each old name a file uses.

## Patch points

`himegari.experience.after_calculation` (`ADDEXP.BIN`) is reached once per experience award, after the multipliers.
`L[0x1]` holds the amount the unit receives:

```text
oms 1
patch "ADDEXP.BIN" fingerprint "sha256:b4f38c43f2c500012be48517ce773d3c9e81c12deeb4d712cab4b99749661e1c"
use himegari.globals

local int exp @ 0x1

before site himegari.experience.after_calculation {
    exp = exp * 2
}
```

The same in Lua ([../lua.md](../lua.md)), as `oms/examples/mods/example.double-exp-lua` ships it: a hook on the point
and a handler that doubles the local. It leaves the same state as the patch.

```toml
[[hooks]]
point = "himegari.experience.after_calculation"
handler = "double_exp.lua:on_award"
```

```lua
function on_award(ctx)
  maid.vm.l[0x1] = maid.vm.l[0x1] * 2
end
```

Game names ([../lua.md](../lua.md), "Game names") come from `himegari-base`. A mod that requires it reads
`require("himegari-base").g.stage_turn_limit` or `.table("unit_current_levels")[unit]` without `vm`, and writes
them once its `[lua] writes` lists them. The names are those of `../../global-reference.md`.

`himegari-base` declares seven game events ([../lua.md](../lua.md), "Game events"). The experience award is raised at
the experience point:

| Event | Field | Type | Access | Cell |
|---|---|---|---|---|
| `himegari.experience.award` | `amount` | int | rw | `L[0x1]`, the experience the unit receives |
| | `unit` | int | r | `current_unit_id` |
| | `level` | int | r | `unit_current_levels[current_unit_id]` |

A subscriber needs no `vm`. `oms/examples/mods/example.double-exp-event` doubles every award with
`event.amount = event.amount * 2`, and leaves the same state as the patch above.

The other six follow the boot and each stage. Each is raised where INIT2 or FIELD has walked the chain the appends
register their handlers in, so a handler runs after the base game's and every append's. Their fields are read-only:

| Event | Raised | Fields |
|---|---|---|
| `himegari.data.layered` | `himegari.boot.data_layered` (INIT2, before TUNE): every layer has written its tables, the appends' included, and the boot has not used them yet | none |
| `himegari.data.ready` | `himegari.boot.data_ready` (INIT2's exit): every data table is built, and TUNE has corrected the save against them | none |
| `himegari.stage.loaded` | `himegari.stage.after_loader` (FIELD): the stage's loaders have run, and a data file's stage part is applied | `stage` (`current_stage_id`) |
| `himegari.stage.ready` | `himegari.stage.after_ready_handlers`: once per stage, at its start, with the units placed | `stage` |
| `himegari.stage.round` | `himegari.stage.after_round_handlers`: each player turn after the first | `stage`, `turn` (`current_stage_turn`) |
| `himegari.stage.close` | `himegari.stage.after_close_handlers_won` and `_lost`: the stage ends | `stage`, `result` (`stage_result`: 1 won, 2 lost), `turn` |

- **Edit the boot's tables at `data.layered`.** An edit there lands where an append's edit lands, and TUNE then runs
  over it as it runs over the appends'. TUNE lowers each saved tuning level to the last level its item's curve gives
  a bonus for (`item_tuning_curve_ids`, `item_tuning_stat_bonus_curves`), and raises each `item_inventory_counts` cell
  to the party's items. An edit at `data.ready` comes after that, so TUNE does not correct the save against it: a
  saved tuning level could then lie past a curve the edit shortened. The correction TUNE makes against an edit is in
  the save from then on, and stays when the mod is removed.
- **Data files are applied at `data.layered`,** among its handlers in load order. A handler at `data.ready` runs
  after every data file, whatever the load order; one that should take its place among them by load order
  subscribes to `data.layered`.
- **The data tables are not saved** (they lie above the saved range, below). A load builds the tables again and
  raises `data.layered` and `data.ready` again while it restores (`maid.session.restoring` is true), so the handler
  makes its edit again.
- **FIELD runs the loaders more than once a stage.** `field_map_continue_mode` is 0 at a stage's start: FIELD runs the
  loaders, places the units and walks the ready chain. Back from the camp screens (the deploy screen, for one), or at
  the load of a save made in the field, it is 2 or 3: FIELD runs the loaders again and raises `stage.loaded` again,
  but places nothing and raises no `stage.ready`. The loaders write their tables afresh each time, so a handler that
  rewrites them makes the same change each time.
- **The stage's enemies** are `stage_enemy_unit_id` (30 slots, zero for an empty one), each with its spawn
  (`stage_enemy_spawn_tile_x` and `_y`, or `stage_enemy_spawn_object_slot`), faction, difficulty mask and replay gate.
  `unit_boss_class` is nonzero for a boss. A rewrite at the stage's start changes what the stage places:
  `oms/examples/mods/example.random-enemies` replaces each ordinary enemy with one drawn from every ordinary enemy of
  the game, from a seed it keeps in `maid.state`.
- **`stage.close`** comes after the close chain, which FIELD walks for a result of 1 or 2 before it takes the stage's
  SCJUMP decision. Stage 1 lost at its 50-turn limit raises it with result 2 and turn 50.

`himegari-base`'s helpers ([../lua.md](../lua.md), "Game names"):

| Helper | Calls | What it does |
|---|---|---|
| `give_item(item, count)` | `ADDITEM.BIN` | adds `count` (-99 to 99, 1 when nil) of item `item` (1 to 999) as the game does: the count stays from 0 to 99, an item gained is marked obtained, and a key item (`item_category` 1) sets its story flag. It puts back `current_item_id` and `current_item_count_change`, and returns `item_inventory_counts[item]`. |

## Catalogs, scenes and saves

- **Selectors.** The install ships one append catalog (`APPEND01`), so a first mod's catalog normally gets selector
  2.
- **Scene registry.** SCJUMP decision 0 is empty in the base game and falls back to SC0000. A mod scene written into
  `scjump_scene_script_resource_ids[0x0]` therefore plays before the opening. `oms/examples/mods/example.prologue`
  does this, on the skeleton of the shipped append scene `$1$SC1260`. Saving and loading inside it passed in the
  port and on native AGE (`tools/drive/scenarios/himegari-mod-{save,load}-scene.steps`).
- **Saved range.** Saves keep the integer globals below `0x6241b` and the first 789 string globals. A table cell
  below that range is restored by loading a save. Check a data edit's cells against `../../global-reference.md`.
- **Free cells.** The examples use the string cells `GS[0x1400]` and `GS[0x1401]`, which no shipped script uses
  (`oms/examples/himegari-gate6/`).

## Examples

| Path | What it shows |
|---|---|
| `oms/examples/mods/example.sc0000-page3`, `-page4`, `-merge` | patches that compose or conflict |
| `oms/examples/mods/example.prologue` | a save-capable scene in an append catalog |
| `oms/examples/himegari-patch/SC0000.oms` | two inserted pages, with speakers |
| `oms/examples/himegari-gate6/` | a boot-time script and a script called by packed id |
| `oms/examples/mods/example.double-exp-lua` | a Lua point hook on the experience award |
| `oms/examples/mods/example.double-exp-event` | a Lua subscriber of `himegari.experience.award`, without `vm` |
| `oms/examples/mods/example.lua-random` | `math.random` in a script hook on `SC0000.BIN`, inside a `pairs` loop too; `tools/drive/scenarios/port/himegari-lua-random.steps` checks that `rand-seed 7` gives the same rolls in every run |
| `oms/examples/mods/example.random-enemies` | a subscriber of `himegari.stage.loaded` that rewrites the stage's enemies and keeps a seed per playthrough in `maid.state`; `tools/drive/scenarios/port/himegari-random-enemies.steps` checks stage 1's draw under `rand-seed 7` |
| `oms/examples/mods/example.stage-enemies` | a data file that edits two stages' enemy slots, applied at `himegari.stage.loaded`; `example.stage-enemies-mistake` is its twin with a mistake no shape can say (a unit name code page 932 cannot encode), which the link takes and the boot reports; `tools/drive/scenarios/port/himegari-stage-enemies*.steps` drive both, through the camp, a field save and its load with the mod and without |
| `oms/examples/mods/example.unit-stats` | a data file that raises リリィ's maximum HP, which UNITECH copies into her saved stats when she joins; `tools/drive/scenarios/port/himegari-unit-stats*.steps` drive a new game with it and the load of its save without it |

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
at `himegari.stage.loaded`, each time FIELD has run the stage's loaders. This one raises the experience unit 103 gives; the comment
gives the game's own value:

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
- **TUNE corrects the save against the edited items.** It lowers each saved tuning level to the last level its
  item's curve gives a bonus for (below, "Edit the boot's tables at `data.layered`"). A file that gives an item a
  shorter curve (`item.tuning_curve_ids`) lowers that item's saved levels, and they stay lowered when the mod is
  removed.
- **A stage's enemies** are the `stage_enemy` table, which a file names through the stage: `stage.1.enemy.2` is
  stage 1's enemy slot 2 (stage ids 1 to 999, of which STINIT writes 1 to 170; slots 0 to 29, and no stage gives
  slot 0 a unit). Its fields are the reference's: `unit_id` (0 for an empty slot), `min_level`, `max_level`,
  `auto_level_scale_divisor`, the spawn (`spawn_tile_x` and `spawn_tile_y`, or `spawn_object_slot`), `faction_id`,
  `difficulty_mask`, `first_clear_only`, `random_selection_weight`, and four rows whose columns have no names and are
  given by number: `movement_routine_set_ids` and `battle_routine_set_ids` (columns 0 to 2), `required_story_flags`
  (0 to 6) and `forbidden_story_flags` (0 to 4). None of them is saved. OMS names the slots of the stage being
  played (`stage_enemy[2].unit_id`), as the loaders leave them. This file puts another unit in stage 1's slot 2:

  ```toml
  # data/stage1.toml
  [stage.1.enemy.2]
  unit_id = 810              # 200
  ```

- **The stage part holds through the stage.** FIELD runs the stage's loaders at its start, before it places the
  units, and again each time it is entered again during the stage (below, "FIELD runs the loaders more than once a
  stage"), and the part is applied after each run. So the edit changes what the stage places, and ADDEN, which places
  a slot later when a stage's handler calls it, reads the edited slot too.
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

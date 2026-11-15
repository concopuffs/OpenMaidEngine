# Modding Himegari

Himegari-specific facts for the generic guides. The identity block (profile id, engine ABI, modules, tables,
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

`himegari-base` declares one game event ([../lua.md](../lua.md), "Game events"), raised at the experience point:

| Event | Field | Type | Access | Cell |
|---|---|---|---|---|
| `himegari.experience.award` | `amount` | int | rw | `L[0x1]`, the experience the unit receives |
| | `unit` | int | r | `current_unit_id` |
| | `level` | int | r | `unit_current_levels[current_unit_id]` |

A subscriber needs no `vm`. `oms/examples/mods/example.double-exp-event` doubles every award with
`event.amount = event.amount * 2`, and leaves the same state as the patch above.

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

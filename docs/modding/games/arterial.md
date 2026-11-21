# Modding Arterial

Arterial-specific facts for the generic guides. The identity block (game id, engine ABI, modules, tables,
patch points) is generated in [README.md](README.md#soukoku-no-arterial).

Script mods, text packages, Lua mods and data files work for this game (modding level M3). Its base mod,
`arterial-base`, names its cells for Lua, declares its events and applies its data files; the generated page says
which level it is at.

## Install and options

The expected install is `games/arterial/` beside the repository. The install this guide was written against
mounts four append catalogs (`APPEND01`–`04`), and its title screen shows `Ver 2.00.0022`. Every `Ome.Cli`
command needs the game options:

| Install | Options |
|---|---|
| 創刻のアテリアル | `--game arterial --game-root ../games/arterial` |

- Port: `.\run-godot.cmd -Game arterial`. Add `-Mod PATH` for a mod.
- Driver: `py -3.11 -X utf8 tools/drive.py --game arterial port start --mod PATH`. The game's points include
  `title.start` (Game Start on the title screen, which goes straight to the opening scene), `adv.next`, and the
  character menu's `menu.save`, `menu.load` and `menu.close` (`tools/drive/arterial.toml`). The middle of the
  canvas is Game Start on the title screen, so do not click `adv.next` there.
- Native AGE shows Japanese dialogs, so `dialogs` and `answer` use Japanese labels.
- The menu bar, the fullscreen strip, the 終了 dialog and 設定 are the port's own. This game's 設定 has a fifth
  page, ｵｰﾄﾒｯｾｰｼﾞ, with Auto mode's delays; the driver's `option` steps set its controls on both targets
  (`auto-pitch-voice`, `auto-pitch-text`, `auto-wait-voice-end`).
- `use arterial.globals` names the page's cells, the scene dispatch, the data tables' arrays and the saved cells
  the game reads them through (361 names; [reference](../../global-reference-arterial.md)). A cell without a
  name is written by its address (`G[0x9883]`), as the decompiled scripts print it.

## First mod values

The values for [first-mod.md](../first-mod.md), on the opening scene:

| Step | Value |
|---|---|
| Page | The opening scene's first page (`重苦しい風が吹く。`). |
| Site | `0xf1c` |
| Fingerprint | `sha256:123a8a055e79f3ec45feff5bb6edba15c365cfcefe75ee07c8a4b249f0fdcf3c` |

A page of this game starts at the instruction that sets `G[0x3141]`, right after the previous page's
`reset-adv-text-layout`. That instruction is the site to insert a page before:

```text
oms 1
patch "SC0000.BIN" fingerprint "sha256:123a8a055e79f3ec45feff5bb6edba15c365cfcefe75ee07c8a4b249f0fdcf3c"
use arterial.adv

before site 0xf1c {
    "[MOD] Hello from my first mod."
}
```

The finished mod is `oms/examples/mods/example.arterial-prologue-line`. Its line is the first page on both
targets (native AGE on the exported `SC0000.BIN`, and the port with `-Mod`), at `SC0000@0xf3f`; the game's own
first page follows it.

## Dialogue lines

The prelude's template is `line(text, face = none, voice = none, form = 0x0)`:

```text
"[MOD] A narration line."
"重苦しい風が吹く。" (voice: 0x146)
"「一点突破！　共に行きましょう！」" (face: 0x158, voice: 0x16f, form: 0x2)
```

- A speaker is given by a **face**: the id of a face picture. The scene library looks the picture up for its
  character, draws the name plate, and shows the picture by the message window where the game shows one. Copy a
  face id from a line of the character you want, in the decompiled scene (`Ome.Cli oms-decompile --level
  idiomatic --script SC0000.BIN`). A line without a face is narration.
- `voice` is the voice clip. Narration can carry one too.
- `form` picks the column of the character's names that the name plate shows, 0 (the default) to 5. Any other
  value shows `？？？？`.

There is no speaker list by name yet. A character's faces are the `character` table's `face_id` (100 slots a
character; `docs/arterial-data-tables.md`, "CFINIT"), and `character[<id>].name` is the name the plate shows,
so the tables say whose a face id is; a decompiled line still prints the number. Pages that carry furigana,
interpolate a name or span several `show_text` calls stay core statements in a decompiled scene, as in the
other games.

## Text and language packages

`omp text extract` writes a catalog for every script that draws text (331 files and about 50,000 entries for
this install). The game's text uses private-use characters (`U+E000` and up), which it draws with its own font,
`AGE Extend`: the first page's source is `重苦しい風が吹く。`. They do not show in most editors, so
copy a `source` from the extracted catalog instead of typing it; `omp text check` warns of an entry that
matches nothing. A translation leaves them out or keeps them as it likes.

A language package with three translated lines of the opening scene was checked and run in the port
(2026-11-20, `--mod … --language en`); nothing larger has been tried on this game.

## Data edits

A data file ([../data-files.md](../data-files.md)) changes the game's record tables with no Lua and no script.
`arterial-base` accepts the kind `arterial.tables`. It applies each file's boot tables at
`arterial.data.layered`, after every layer has written them and before INIT2's own work reads them, and an area's
part each time the area's loaders have run, at `arterial.area.loaded`. This one renames the name plate 龍司
(character 12) shows in the opening scene; the comment gives the game's own value:

```toml
# data/characters.toml; the manifest names it with [[data]] kind = "arterial.tables", file = "characters.toml"
[character.12]
name.descriptor_2 = "[MOD] 傷だらけの男"   # 無数の傷を持つ男
```

A fragment can read and write the same tables (`use arterial.globals`). The tables, by what they hold (the
generated page lists them with their fields):

| Kind | Tables |
|---|---|
| Cards and what they carry | `card`, `talent`, `item`, `ultimate` (奥義), `skill`, `condition` |
| Characters | `character`, `party_member` (the party characters, their levels and their saved state) |
| Battles | `battle`, `battle_rule`, `deck`, `opponent` (arena opponents), `cpu_profile` |
| Shops and progress | `recipe`, `shop_entry`, `goal`, `scene` |
| The map | `area_map`, `unit_routine`, and for the area in play `area_point`, `area_route`, `area_unit` |
| Registries | `profile`, `glossary`, `tip`, `cg`, `music` |

```text
use arterial.globals

card[31].cost = 0x1
battle_rule[1].turn_limit = 0x14
```

- **Tables and fields** are those of `../../global-reference-arterial.md`, "Tables", by the names in its `field`
  column. A field with a row takes its columns by the names the reference gives (`name.descriptor_2`: a
  character's names are a row of six, `name` and five descriptors, and a line shows the one its form names), and
  by number only where a column has no name.
- **Saved fields are refused:** `card.experience`, `card.obtained`, `card.owned_count`, `item.obtained`,
  `item.owned_count`, `goal.state`, `opponent.state`, `scene.played`, `scene.seen` and the `party_member` fields
  `experience`, `flags`, `level`, `picture_set`, `skill_lines` and `ultimate_level`. They lie below the saved
  prefix, so a load would bring the saved value back over the edit. Change them from Lua, or with the helpers
  below.
- **The shape.** `arterial-base` ships the kind's shape, `shapes/tables.schema.json`
  ([../data-files.md](../data-files.md), "Shapes"), so the link, `omp check` and `omp build` refuse a table, field
  or column the reference does not name, a saved field, an id or a column out of range and a value of the wrong
  kind, with the file's line. `omp lua-stubs --game arterial` writes a copy and prints the `#:schema` line for an
  editor.
- **An area's tables** are `area_point`, `area_route` and `area_unit`, which a file names through the area:
  `area.1.unit.2.deck` is area 1's unit 2's deck (area ids 1 to 299, of which `AMINIT` has 150). The area's
  loaders (`MPINIT`, `ROINIT`, `UNINIT`, the base's and each append's) clear and fill them each time the game sets
  the area up: when the party enters it, after a battle whose next scene is played on a map, and when a save
  made on a map is loaded. The part is applied each of those times, after the loaders. SETMAPDATA gives
  `area_unit[1]`, the player's unit, the pictures of the map agent or the leader before the part is applied, so a
  file that sets them wins.

- A card's id is `row * 30 + slot`: 200 rows of 30 slots, and a deck names a card by its row and its slot
  (`deck[<id>].card_row`, `deck[<id>].card_slot`).
- Every table's layout, and what each of its fields holds, is in `docs/arterial-data-tables.md`; a few
  numbers are read only as far as the script that uses them, and that document's last section lists them. A
  card's `race_flags`, `slots`, `control`, `cost`, `hp`, `leader_lp` and `attack` are the card panel's 種族,
  スロット, 支配力, コスト, ＨＰ, ＬＰ and 攻撃力.
- Several tables name a card by a pair of fields, its row and its slot (`recipe[<id>].card_row`,
  `recipe[<id>].card_slot`), and a scene by its id plus one (`recipe[<id>].scenes_played`). A field of
  `recipe`, `shop_entry`, `opponent` or `goal` that the game tests and no record of the game writes (a least
  day, the days of the week, the time slots) is still read: a mod that sets it adds the condition.
- A card's numbers in the tables are its numbers at level 1. The game works out what a card has now
  (`card[<id>].current_hp` and the like) from them, the growth fields and the card's level, so write the
  table's field, not the computed one.
- **Saves.** No table is in a save: boot rebuilds every one, and most fields are read from the table where
  they are used, so a changed value is there on an old save too. A save keeps the player's state the tables are
  read through: how many of a card and an item the player holds and whether one was ever obtained, a card's
  experience, a party character's `party_member` fields (`flags`, `level`, `experience`, `ultimate_level`,
  `picture_set`, `skill_lines`), and `opponent[<id>].state` and `goal[<id>].state`. What the game copies out of
  a table into a save, so that a change does not reach an old one, is listed in
  `docs/arterial-data-tables.md`, "Saved state": among it a party character's starting skill lines
  (`party_member[<id>].start_skill_lines`, copied when the character joins), the cards and items a level
  brings (given once), the 奥義 level a level brings (`level_ultimate_level`: the saved level is raised to it
  and never lowered), and a map unit's deck.
- A fragment's write to a table is not saved, and it lasts until the game builds the tables again, at its next
  start at the latest. A data file's edit is made again at each boot and each load, while its mod is enabled.
- **Run in the game:** the example's boot edit (`tools/drive/scenarios/port/arterial-character-name.steps`) and
  an area part (`example.arterial-area-point`, in `tools/drive/scenarios/port/arterial-debug-events.steps`), in
  the port. No edit was run on native AGE (data files are port-only).

## Patch points and events

The named points (`../../patch-point-reference-arterial.md` gives each one's description and fingerprint) sit
after the chains the appends hook, so a fragment or a Lua hook there sees every layer's work:

| Point | Script | Where |
|---|---|---|
| `arterial.boot.data_layered` | `INIT2.BIN` | after every layer's data tables (op `0x143` runs the appends') |
| `arterial.boot.data_ready` | `INIT2.BIN` | INIT2's exit, after its own work on the tables |
| `arterial.area.after_loaders` | `SETMAPDATA.BIN` | after the area's loaders, each layer's `MPINIT`, `ROINIT` and `UNINIT` |
| `arterial.area.after_ready_handlers` | `READYEND.BIN` | after every layer's `READY`, more than once in an area |
| `arterial.battle.before_phases` | `BTL.BIN` | the call that plays a battle out, at each try |
| `arterial.battle.after_phases` | `BTL.BIN` | the battle decided, nothing of its outcome applied |
| `arterial.experience.after_calculation` | `RESULT.BIN` | one party character's award from a won battle, before it is applied |

The award's amount is the third operand of the add that applies it, a pointer into RESULT's own array, so a
fragment reaches it through a local pointer; a Lua subscriber of `arterial.experience.award` reads and writes it
as `e.amount`.

`arterial-base` declares seven game events ([../lua.md](../lua.md), "Game events"):

| Event | Fields |
|---|---|
| `arterial.data.layered`, `arterial.data.ready` | none |
| `arterial.area.loaded`, `arterial.area.ready` | `area` (`current_area_id`) |
| `arterial.battle.start` | `battle` (`battle_id`), `rule` (`battle_rule_id`), `opponent` (`current_opponent_id`, 0 outside the arena) |
| `arterial.battle.close` | `battle`, `result` (`battle_result`: 1 won, 2 lost), `opponent` |
| `arterial.experience.award` | `amount` (rw), `character` (`character_arg_id`), `level` (`party_member_levels[character_arg_id]`) |

Its helpers change the game through its own scripts: `give_item(item, count)` runs `ADDITEM` and
`give_card(card, count)` runs `ADDCARD` (a card's id is `row * 30 + slot`). Each keeps a count from 0 to 99, pays
what passes 99 out as money at the selling price, marks the item or the card obtained and records it in the
shared collection, as the game's own gifts do, and puts back the cells it used. Each returns the new count.

Every event has been raised in a run of the port: the boot events, the helpers and the data file's boot edit by
`ModLuaLinkTests` and the scenario above, and the area, battle and experience events through the debug menu
(below) by `tools/drive/scenarios/port/arterial-debug-events.steps`. A new game's own maps and battles were not
reached yet.

Other chains the appends hook have no event: `OPEN` (an area's opening, after the map is shown), `SCJUMP`'s
chain (the scene dispatch), `BGINIT` (the scenes' pictures), `TUMES`, the item actions, and the 奥義 and the
talents in battle (`docs/arterial-data-tables.md`, "How the data gets into memory").

## The debug menu

The title builds the developers' デバッグメニュー right after its Quit calls `QUIT.BIN`, which always ends the
game, so no retail run reaches it. `oms/examples/mods/example.arterial-debug-menu` deletes that call, so Quit opens
the menu, and returns from each entry instead of jumping to the code at `TITLE@0x574`, which since Arterial Force
Blade starts a new game, so the mode the entry sets takes effect. It targets `native`: `omp export` gives a
`TITLE.BIN` that opens the same menu on native AGE.

| Entry | Point | What it runs |
|---|---|---|
| ADVパート | | a scene picker (`SC????.BIN`) |
| 全体マップ | 440 270 | `DEBUGAM`, then the whole map: area 1 with the debug party |
| カードバトル | 440 299 | `DEBUGBTL2`, then a battle |
| カードバトル（ＰＧ用） | 440 329 | `DEBUGBTL`, then a battle |
| カードバトル（ＰＧ用２） | 440 358 | `DEBUGBTL3`, then a battle |
| ＡＤＶデバッグ | | `DEBUGAM`, then the scene `DEBUGADV` |
| ＡＤＶデバッグ２, ３ | | the scenes `DEBUGADV2`, `DEBUGADV3` |
| システム | 440 478 | `DEBUG.BIN`: a polygon test, waiting for a click, then the rain, the snow and the leaves for ten seconds, then the title |

- The battles open on BTLINFO; its battle start is at (826, 242) and Auto Battle at (68, 548). The scenario
  above wins one by setting the battle's turn-limit mode (`G[0x46910a]`) to 1, where passing the limit
  (`G[0x46910c]`) is a win, and the limit to 1.
- The debug party is at level 34, and RESULT draws a level as one glyph of a strip, at `x = 160 + level * 23`
  (`RESULT@0x2658`): level 5 is `5`, and 34 is `Y`.
- Run on both targets: the menu, the whole map (pixel for pixel alike), a PG battle's BTLINFO, and システム (its
  polygon test pixel for pixel alike but for the leaves' phase; the effects alike with the ambient light raised
  by a scratch patch, as `../../engine-re.md`, "The 3D scene: the rain and the snow", tells).
- The scripts draw polygons with the alpha and additive blends alone. `oms/examples/mods/example.arterial-polygon-blends`
  gives システム's polygon test a replace polygon, a subtractive one and one with two textures, so the renderers'
  other paths draw on a frame of the game; exported with the debug menu, it draws on native AGE as in the port's
  reference renderer. `tools/drive/scenarios/port/arterial-debug-reference-diff.steps` compares the GPU renderer
  with the reference on that test, its effects, a battle and the whole map.

## Catalogs, scenes and saves

- **Selectors.** The install ships four append catalogs (`APPEND01`–`04`), so a first mod's catalog normally gets
  selector 5.
- **Record 0 runs at boot.** Op `0x143` runs it at the end of `INIT2`, after the install's own appends have run
  their `$n$AUTORUN`, so it sees every table filled and can edit any of them. The tables lie above the saved prefix
  and the game does not save a character's names, so an edit made there holds after a load.
- **A scene of your own** needs three things, and `oms/examples/mods/example.arterial-new-scene` does all three. It is
  tested on native AGE and in the port, saving and loading inside the scene included.
  - **A scene id.** `SCINIT` uses ids up to 8932 of its 10000. Write your scene script's id into an unused one:
    `scene[0x2706].script_id = asset "SCENE"`. Its played cell, `scene[0x2706].played`, is in the saved prefix.
  - **A rule in SCJUMP's chain.** The game has no registry row to fill. A caller runs every script of
    `scjump_chain_script_ids` (32 slots) and the first rule that holds picks the next scene
    (`../../arterial-data-tables.md`, "Scene dispatch"). Put a rule script of yours in the slot of your selector,
    as each of the game's appends does: `scjump_chain_script_ids[asset "OFFER" >> 0x18] = asset "OFFER"`. Write the
    rule in the shipped rules' form: test the context and your conditions, then, if `scjump_chain_pending` is still
    over 0, set `G[0x0] = 0x1` and `current_scene_id`, and clear `scjump_chain_pending`. The game's own rules run
    first, and one that holds takes the turn.
  - **The scene script.** SYSTEM4 plays the script of the picked scene's record. Copy a shipped append scene's
    skeleton (`$4$SC8770.BIN` is a small one, decompiled `--level idiomatic`) and replace its content, where it
    branches on `current_scene_id`. Mark the scene played at its start, so the rule does not offer it again.
    When its content returns, the caller goes on: from the map, the map.
- The example's rule offers its scene in context 4 while area 1 is in play. The title's debug menu reaches that map at
  once (`example.arterial-debug-menu`, 全体マップ), and `tools/drive/scenarios/arterial-new-scene.steps` plays the
  scene there on both targets.

## Examples

| Path | What it shows |
|---|---|
| `oms/examples/mods/example.arterial-prologue-line` | a line inserted before the opening scene's first page |
| `oms/examples/mods/example.arterial-character-name` | a data file that renames 龍司's name plate at `arterial.data.layered`; `tools/drive/scenarios/port/arterial-character-name.steps` plays a new game to his first line with it |
| `oms/examples/mods/example.arterial-area-point` | a data file with an area part: area 1's 出現地点 moves right, applied at `arterial.area.loaded` |
| `oms/examples/mods/example.arterial-debug-menu` | the debug menu above, for both targets |
| `oms/examples/mods/example.arterial-polygon-blends` | a patch of `DEBUG.BIN` that gives its polygon test the replace and subtractive blends and a second texture, for both targets |
| `oms/examples/mods/example.arterial-new-scene` | new scripts in the mod's own append catalog: a boot script that renames 龍司's name plate and registers a scene, a rule in SCJUMP's chain, and the scene, on both targets; `tools/drive/scenarios/arterial-new-scene.steps` plays it from the debug menu's map |
| `oms/examples/mods/example.arterial-object-blends` | a patch of `SC0000.BIN` that holds object colour modes 0 to 3 still on the first page: on the screen, and into render targets of kinds 1 and 2; `tools/drive/scenarios/arterial-object-blends.steps` takes the frame on both targets |
| `oms/examples/mods/example.arterial-event-log` | a Lua mod that logs every event of `arterial-base` with its fields; `tools/drive/scenarios/port/arterial-debug-events.steps` runs it with the two above |

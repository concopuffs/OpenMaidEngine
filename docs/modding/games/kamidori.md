# Modding Kamidori

Kamidori-specific facts for the generic guides. The identity block (profile id, engine ABI, modules, tables,
patch points) is generated in [README.md](README.md#kamidori-alchemy-meister).

## Install and options

The expected install is `Kamidori/` beside the repository: the game with its official updates applied. The English
fan translation is a separate install, `Kamidori_Translated/`, with its overlay in `patch/`. Every `Ome.Cli`
command needs the game options:

| Install | Options |
|---|---|
| untranslated | `--profile kamidori --game-root ../Kamidori` |
| translated | `--profile kamidori --game-root ../Kamidori_Translated --overlay-root patch` |

- Port: `.\run-godot.cmd -Kamidori`, or `.\run-godot.cmd -Kamidori -TranslationPatch` for the translated install.
  Add `-Mod PATH` for a mod.
- Driver, translated:

  ```text
  py -3.11 -X utf8 tools/drive.py --profile kamidori --game-root ../Kamidori_Translated port start --mod PATH --port-arg=--overlay-root --port-arg=<absolute path to Kamidori_Translated\patch> --port-arg=--allow-bmp-as-agf
  ```

  The profile's points include `title.start` (Game Start on the title screen) and `newgame.begin` (Begin Game on
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
- Registries for fragments: `py -3.11 -X utf8 tools/globals_build.py --profile kamidori --build`.

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
Ome.Cli omp text import-scripts --profile kamidori --game-root ../Kamidori --from ../Kamidori_Translated --overlay-root patch --allow-bmp-as-agf --output build/games/kamidori/import-en --package --verify
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

A boot script can edit the unit, item, skill and stage-enemy tables. This one changes the enemy unit 761, which
appears in the first stage (`oms/examples/kamidori-gate6/OMSAUTO.oms`):

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

`kamidori-base` declares one game event ([../lua.md](../lua.md), "Game events"), raised at the experience point:

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

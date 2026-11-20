# Modding Arterial

Arterial-specific facts for the generic guides. The identity block (game id, engine ABI, modules, tables,
patch points) is generated in [README.md](README.md#soukoku-no-arterial).

Script mods and text packages work for this game (modding level M1). Its data tables are not decoded yet and it
has no base mod, so Lua mods and data files are not available: the generated page says which level it is at.

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
- The globals registry is young: `use arterial.globals` names the page's cells, the scene dispatch and the main
  tables' text, references and a card's and an item's numbers (93 names; [reference](../../global-reference-arterial.md)). Every
  other cell has no name yet: name it by its address (`G[0x9883]`), as the decompiled scripts print it.

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

## Data edits and patch points

The game has no base mod (modding level M3), so there are no data files, no events and no named patch points.
A fragment can still patch any instruction by its offset, and it can read and write the tables the registry
declares: `character`, `card`, `talent`, `skill`, `item`, `area` and `deck`.

```text
use arterial.globals

card[31].cost = 0x1
```

- A card's id is `row * 30 + slot`: 200 rows of 30 slots, and a deck names a card by its row and its slot
  (`deck[<id>].card_row`, `deck[<id>].card_slot`).
- The tables are decoded in part (modding level M2 is not reached). Every table's layout is in
  `docs/arterial-data-tables.md`; names exist for the text, the references between tables, a card's numbers
  with their growth by level, and what an item asks of the card that carries it. A card's `race_flags`,
  `slots`, `control`, `cost`, `hp`, `leader_lp` and `attack` are the card panel's 種族, スロット, 支配力, コスト,
  ＨＰ, ＬＰ and 攻撃力. Many numeric fields have no name.
- A card's numbers in the tables are its numbers at level 1. The game works out what a card has now
  (`card[<id>].current_hp` and the like) from them, the growth fields and the card's level, so write the
  table's field, not the computed one.
- **Saves.** No table is in a save: boot rebuilds every one, so a changed value is there on an old save too. Of
  a card a save keeps how many the player holds and its experience (`card[<id>].owned_count`,
  `card[<id>].experience`, 100 to a level), and of an item how many the player holds
  (`item[<id>].owned_count`). Whether the game keeps a copy of any other field in a save is not
  checked.
- A fragment's write to a table is not saved, and it lasts until the game builds the tables again, at its next
  start at the latest. No table edit was run in the game, on either target, and no way to make a lasting one
  was tried.

## Catalogs, scenes and saves

- **Selectors.** The install ships four append catalogs (`APPEND01`–`04`), so a first mod's catalog normally gets
  selector 5.
- **Untested.** No Arterial mod that adds a scene or a script has been tested, on either target. Start from one
  of the game's own append scenes (`$1$SC0510.BIN` is one), and test saving on native AGE as well as in the port.

## Examples

| Path | What it shows |
|---|---|
| `oms/examples/mods/example.arterial-prologue-line` | a line inserted before the opening scene's first page |

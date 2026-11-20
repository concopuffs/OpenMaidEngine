# Arterial data tables (`*INIT`)

Canonical home for how 創刻のアテリアル (Arterial, SYS4438) stores its static game data: which `*INIT` scripts
exist, how they are layered, where each table lives in the global bank, and what is known of its fields. Names
for the fields that are understood live in [`vm-map/globals-arterial.toml`](../vm-map/globals-arterial.toml)
(generated → [`global-reference-arterial.md`](global-reference-arterial.md)). Kamidori's equivalent is
[`kamidori-data-tables.md`](kamidori-data-tables.md).

**How far this goes (2026-11-20).** Every table's *layout* is here: its arrays, their strides and what values
they hold, all 280 arrays of the 29 boot tables. What an array *means* is given where the data or a consumer
shows it: names and text, the references between tables, the panels' numbers, the tests the shop, the recipes, the
arenas and the deck check make, and the battle rules' numbers against their own texts. A row that says nothing in
its last column is not read yet; "What is not decoded" at the end lists the gaps.

## How the data gets into memory

Like the other two games, Arterial has no data files. Every table is a script of constant writes into the flat
global bank: `mov` for integers, `set-string` for text, `copy-inline-int-array` for a few bulk arrays. Runtime
code reads the bank with `lookup-array` (`base + id`) and `lookup-array-2d` (`base + row * stride + column`).

**One array per field.** A table is a set of parallel arrays. A record's field is the cell
`base + cells_per_id * id + column`: one cell per id for a scalar, several for a short list (a card's two
talents, an item's three lines of text). Each array is allocated for the table's *capacity* (1000 characters,
2000 items, 6000 card slots) whether or not an id is used, and the arrays of a table lie back to back. Ids are
sparse and mostly start at 1.

**Boot order.** `INIT2.BIN` calls 29 initializers in this order: SCINIT, CHINIT, CFINIT, CCINIT, CDINIT,
TAINIT, COINIT, SKINIT, ITINIT, IMINIT, ILINIT, RFINIT, AMINIT, DEINIT, DUINIT, REINIT, ALINIT, SHINIT, ARINIT,
RTINIT, FAINIT, CVINIT, CIINIT, VIINIT, TIINIT, EUINIT, CGINIT, SPINIT, MUINIT. Op `0x143` then runs each
mounted append catalog's `$n$AUTORUN.BIN` in ascending selector order, and each of those calls its own
`$n$<TABLE>.BIN` for the same 29 tables. The install has four appends, so a table's runtime image is the
**base, then `$1$`, `$2$`, `$3$`, `$4$`**, later writes winning. A fragment adds records and also rewrites
cells of records an earlier layer made. A fragment of four lines is an empty stub (`$2$` is almost all stubs).

Several tables stamp a record with the append that added it (1 to 4), in an array of their own; the base data
leaves that array unset. The stamp is written *before* the record's first field, where every other field
follows it.

**A record's append picks its scripts.** Fifteen scripts that act on records have an append version, and
`INIT2` and each `$n$AUTORUN` fill a slot array for each of them: slot 0 is the base script and slot n the
`$n$` one. The record's append stamp picks the slot. `SELITEM` runs `G[0x45265d + item's append]` (`ITEMACT`
or `$n$ITEMACT`) for an item used in battle; a 奥義 runs `G[0x45267d + 3 × append + k]` (`COUPACT`, `COUPANIME`,
`COUPRTN`) and a talent `G[0x4526dd + 3 × append + k]` (`TALENT`, `TALENTACT`, `TALENTRTN`). So an append's
items, 奥義 and talents have their effects in that append's scripts. Other slot arrays run every layer in turn:
`SETMAPDATA` runs slots 0 to 31 of `MPINIT` (`G[0x45275d]`), `ROINIT` (`G[0x45277d]`) and `UNINIT`
(`G[0x45279d]`), and `SCJUMP`'s chain is the same kind (`G[0x4525dd]`, "Scene dispatch" below). `INIT2` also
fills the slots of `TUMES` (`G[0x45263d]`), `BGINIT` (`G[0x45273d]`), `READY` and `OPEN` (`G[0x4525fd]` and
`G[0x4525fe]`, two cells an append), and each `$n$AUTORUN` sets bit n of `G[0x4525dc]`. `ALLMAP` walks `READY`'s
slots when it sets an area up and in three places of its map loop, each time followed by `READYEND` and `SETFATE`,
and `OPEN`'s once the area's map is shown. The base mod's events sit after these chains
(`vm-map/patch-points-arterial.toml`).

Four more tables run on demand, not at boot: BGINIT, MPINIT, ROINIT and UNINIT ("On-demand tables" below).

## Which files are authored data

`games/arterial/` holds the archives `DATA1`–`6.ALF` and `APPEND01`–`04.ALF`, and 119 loose `.BIN` files in its
root: `SYS4INI.BIN`, `SYS4RK.BIN`, 94 with the name of a base record and 23 `$n$` ones. Of the 117 that have a
record in an archive, 113 differ from it and 4 are identical. Native AGE reads a loose file before the archive,
and the data tooling reads the install the same way, so every count below includes them. (Where the loose
files come from is not established here.)

## Tooling

All commands run from `age-reimpl/` with `py -3.11 -X utf8` and `--game arterial`
([`tools-reference.md`](tools-reference.md)):

1. Extract the archives once: `bin/BinExtractALF.exe ../games/arterial/SYS4INI.BIN ../extracted-arterial`, and
   each `APPENDnn.AAI` so that its records land flat in `../extracted-arterial/APPENDnn/`.
2. `parse_sys4ini.py` → the asset index and call-script names.
3. `extract_phase2.py` → `build/games/arterial/disasm/*.asm` and the text corpora.
4. `init_image.py` → `build/games/arterial/init-image.json`, the **executed** boot image: the port's VM runs
   the boot order above and records what each of the 145 scripts wrote.
5. `init_arterial.py` → each table's layout, inferred from the write order and the image. `init_arterial.py
   <TABLE>` prints a table's arrays column by column, and `--write` saves them as
   `build/games/arterial/data/<TABLE>.layout.json`.

`extract_init.py`'s shape modes were written for the other two games' tables and do not fit these (its name
mode takes each string for a record). `init_arterial.py` needs no schema per table: it is given the array each
record writes first (the *leader*) and the table's capacity, takes a record's id from the leader cell, and
finds every other field as the `(stride, base)` on which the most records agree. A table whose records are of
two kinds has two leaders (`ALINIT` and `SHINIT`: an entry is an item or a card). Twenty-four boot tables
have a leader. The other five (`IMINIT`, `RFINIT`, `DUINIT`, `RTINIT`, `EUINIT`) are shown by the arrays the
scripts index (`lookup-array` operands), which is a weaker view: an array no script indexes with a variable is
not seen, its cells are counted in the array before it, and the base shown is the indexed array's, not always
the field's.

**The grid, and `†`.** A table's arrays lie back to back, each a whole number of capacities long, so their bases
agree modulo the capacity. The tool marks an array whose base does not: `†` in the layouts below. Such an array
either has another table's cells between it and the rest, or is not indexed by the record's id at all, and then
the base and the ids in its row are wrong. Each `†` row says which.

## Table inventory

| table | holds | runs |
|---|---|---|
| SCINIT | scenes: the script each scene id plays | boot |
| CHINIT | characters (speakers): names by form, voice, pictures | boot |
| CFINIT | faces: each character's face pictures | boot |
| CCINIT | party characters: the skill lines they start with, and the cards, skills and 奥義 level of each of their levels | boot |
| CDINIT | cards: 30 slots for each of 200 rows | boot |
| TAINIT | talents: a card's abilities, by name and description | boot |
| COINIT | 奥義: 30 of them, each with nine levels | boot |
| SKINIT | skills (飛行, 転移, 錬金Ⅰ …) | boot |
| ITINIT | items | boot |
| IMINIT | card growth: what each of up to eight level steps adds to four of a card's numbers | boot |
| ILINIT | conditions (操血, 封印, 幻覚 …) | boot |
| RFINIT | constants: the ranks' points, a character's experience per level, battle experience, random conditions | boot |
| AMINIT | areas (歪秤世界, 天慶市 …) | boot |
| DEINIT | decks: a name and up to 20 cards | boot |
| DUINIT | battles: each side's leader and deck, the opponent's 奥義, the rule, music and pictures | boot |
| REINIT | battle rules: what wins and loses, the turn limit, hand and deck size, and the deck's limits | boot |
| ALINIT | recipes: what each makes, from which ingredients, and when `ALCHEMY` offers it | boot |
| SHINIT | the shops' entries: an item or a card, and when `SHOPP` lists it | boot |
| ARINIT | arena opponents: battle, arena, conditions, scenes and reward; and ten arenas (対戦台, 連戦台 …) | boot |
| RTINIT | map units' routines, step by step, and the CPU's battle profiles | boot |
| FAINIT | goals: text, the scenes that open and close each, a kind and a reward | boot |
| CVINIT | voice settings: one per voiced character | boot |
| CIINIT | character profiles | boot |
| VIINIT | the glossary (【用語】), an entry listed once a scene of its is seen | boot |
| TIINIT | a second list of titled entries (日常モード …), listed once a scene of theirs is seen | boot |
| EUINIT | pictures for `CGMODE` and `HMODE`, two a record | boot |
| CGINIT | 1577 entries of two resource ids, in sections and groups (by its name, the CG gallery) | boot |
| SPINIT | 65 pairs of resource ids | boot |
| MUINIT | the music list | boot |
| BGINIT | picture substitution: given a picture id and `G[0x987c]` (0 to 4), another picture id | on demand |
| MPINIT | the points of the area in play: names, kinds, positions, where a point leads | on demand |
| ROINIT | the routes between the area's points | on demand |
| UNINIT | the units on the area: a card each, and nineteen more arrays | on demand |

The layouts below give, for each array: its base, the cells it has per id, which columns are written, how many
ids have a value, and the values (for a string array, the first three).

## Scenes

### SCINIT

Leader `G[0x214b71]`, capacity 10000; 1426 scene ids are set, 0 to 8932. Scene 0 is the opening, `SC0000`.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x214b71]` | 1 |  | 1426 | 323..67109455, 235 distinct | the script that plays scene `id`: a packed script id (323 is `SC0000.BIN`; an append's scenes carry its selector in the top byte) |
| `G[0x217281]` | 1 |  | 1426 | 1..7, 7 distinct | a number 1 to 7 beside the script; no script reads it |
| `G[0x219991]` | 1 |  | 4 | 1..3, 3 distinct 1×1, 2×2, 3×1 | a sort key: `AFFAIR` orders the scenes it offers by it |
| `G[0x21c0a1]` | 1 |  | 662 | 1..1, 1 distinct 1×662 | 1 on 662 scenes. `ALLMAP` reads it for the scene `SCJUMP` picked, into `G[0x36ca]` |

### Scene dispatch (SCINIT and SCJUMP)

A scene is a number, and `G[0x21480a]` holds the one being played. `SCINIT` gives each its script; one script
holds several scenes and branches on the number (`SC0000` holds scenes 0 to 9). Two cells a scene record that
it ran. `G[0x5e0 + scene]` is set by the scene's own code (`SC0000` sets `G[0x5e0]` to `G[0x5e9]`, `SC3000`
`G[0x1198]`) and is in a save: the scene has been played in this game. `G[0x18880 + scene]` is set by each of
the 219 base scene scripts and stored in the shared profile: the scene has been seen by this player.

`SCJUMP` picks the next scene. A caller (`ALLMAP`, `AFFAIR`, `BTL`, `SYSTEM4`) sets the context `G[0x352f]` and
`G[0x21480b] = 0x7fffffff`, runs `SETADVFLAG` (an empty script here), and then runs every script of a 32-slot
chain, `G[0x4525dd + n]`. `INIT2` puts `SCJUMP` in slot 0, and each `$n$AUTORUN` its own `$n$SCJUMP` in slot n,
so the chain runs **base, `$1$`, `$2$`, `$3$`, `$4$`**. `SCJUMP` branches on the context (1 to 7) to a list of
rules, each of this form:

```
if <condition> {
    if G[0x21480b] > 0x0 { G[0x0] = 0x1; G[0x21480a] = <scene>; G[0x21480b] = 0x0 }
}
```

The first rule whose condition holds takes the turn: it clears `G[0x21480b]`, and every later rule, in this
script and in the rest of the chain, finds it clear. The caller then sees `G[0x0] == 1` and starts the scene.

| script | rules | scenes they name |
|---|---|---|
| `SCJUMP` | 1239 | 874, from 1 to 8760 |
| `$1$SCJUMP` | 76 | 65, from 510 to 593 |
| `$3$SCJUMP` | 10 | 10, from 710 to 765 |
| `$4$SCJUMP` | 1505 | 103, from 811 to 8891 |

What a rule tests is mostly two things. The first is the played cells: 585 of the base script's rules begin
`G[0x5e0 + scene] != 1` for the scene they name, so a scene is offered until it has been played, and others ask
that another scene has been (`G[0x11a9] == 1 && G[0x5e1] != 1` offers scene 1 once scene 3017 is played). The
second is the place: the area in play, `G[0x9883]` (1151 tests), `G[0x9ba8]` (973) and bit 0 of `G[0x9ae0]`
(970). `G[0x9ba8]` is the second cell of `G[0x9ba7]`, which `ALLMAP` indexes with the unit in `G[0x9ade]` (a
slot of `UNINIT`) to get the point that unit stands on. Unit 1 has no card in `UNINIT` and is read as the
player's, so a rule such as `G[0x1198] != 1 && G[0x9883] == 3 && (G[0x9ae0] & 1) == 1 && G[0x9ba8] == 1`
offers scene 3000 when unit 1 is on point 1 of area 3. So the played cells are the game's story
flags, first of all: of the 836 candidates `tools/story_flags.py` lists on this game, 372 lie in the played
cells' range (`0x5e0` to `0x2cef`).

Not decoded: what the seven contexts are, what `G[0x9ae0]`'s bit is, and the story cells outside the played
cells. `tools/scjump_decode.py` was written for Himegari's `SCJUMP` and was not run;
`docs/scjump-progression.md` is Himegari's, and nothing of it is assumed here.

## Characters

### CHINIT (characters)

Leader `G[0x297db9]`, capacity 1000. 107 characters: 81 in the base data, 4, 8 and 14 from appends 1, 3 and 4.
A character is whoever can speak a page: the scene library sets `G[0x3140]` to the character of the page's
face and reads these arrays for the name plate and the voice (`oms/arterial/adv.oms`).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x297db9]` | 1 |  | 107 | 1..316, 91 distinct | the character whose voice the speaker uses (itself for 91 of the 107); the page's voice block finds the voice setting through it (`oms/arterial/adv.oms`) |
| `G[0x29eaa6]` | 1 |  | 108 | 10..9990, 107 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x29ee8e]` | 1 |  | 107 | 1..3, 3 distinct 1×38, 2×43, 3×26 | 1, 2 or 3. The page's voice block plays a voice only where this is 2 |
| `G[0x29f65e]` | 1 |  | 26 | 1..32, 26 distinct | the character's 奥義, a `COINIT` id (26 characters have one). The 奥義's numbers are read at the character's 奥義 level, `G[0x3c70 + character]` in a save |
| `G[0x2a09e6]` | 9 | 0..8 | 16 | 3968..16777975, 143 distinct | nine voice clips: `RESULT` plays column 0 when the character's level rises, `ALLMAP` column 5 and `BTL` column 7 |
| `G[0x2a2d0e]` | 5 | 0..3 | 27 | 21382..16777990, 30 distinct | pictures; the 奥義 and skill books (`INFOCO`, `INFOSK`) draw column 0 |
| `G[0x2a4096]` | 5 | 0..3 | 27 | 19282..67110525, 38 distinct | pictures, a column for each of the character's picture sets: the column is `G[0x3d38 + character]` (0 to 4; `UNITECH` sets it to 0 when the character joins). `FELLOW`, `GAMESTART` and `GAMECLEAR` draw it |
| `G[0x2a541e]` | 5 | 0..3 | 27 | 19283..67110526, 42 distinct | pictures by picture set, as the array before; `FELLOW` draws them |
| `G[0x2a67a6]` | 5 | 0..3 | 27 | 14963..67110527, 42 distinct | the character's unit on the map, by picture set: `SETAGENT` and `SETMAPDATA` copy it, with the two arrays after it, into the player unit's cells of `UNINIT` (`G[0x334600]`, `G[0x334601]`, `G[0x33478f]`) |
| `G[0x2a7b2e]` | 5 | 0..3 | 27 | 14969..67110528, 42 distinct | the unit's second picture, by picture set (see the array before) |
| `G[0x2a8eb6]` | 5 | 0..3 | 27 | 14970..67110529, 39 distinct | the unit's third picture, by picture set |
| `G[0x2aa23e]` | 5 | 0..3 | 27 | 19284..67110530, 38 distinct | pictures by picture set; `FELLOW` and `MENU` draw them |
| `GS[0x4154]` | 6 | 0..5 | 108 | 秀哉 / 美來 / 杏里咲 | the names the name plate can show, column = the page's `form` (0 to 5): the name, then descriptors (兄, 女の子 …) |
| `GS[0x7994]` | 1 |  | 27 | 仙崎秀哉／せんざきしゅうや / 仙崎美來／せんざきみらい / 蘇芳杏里咲／すおうありさ | the full name with its reading |

### CFINIT (faces)

Leader `G[0x266c91]`, 100 slots a character. 660 faces. A dialogue line does not name its speaker: it names a
**face** (`face: 0x16a`), which is the picture the page draws, and the scene library finds the speaker from it.
`INIT2` builds that map at boot from this table: for every face, `G[0x3fc08 + 30000 * layer + id]` is its
character and `G[0x12a208 + 30000 * layer + id]` its slot, where `layer` is the face id's top byte (an append's
selector, 0 for the base data) and `id` the rest. `set_page_face` reads the two. Face `0x16a` is character 12
(龍司), slot 6.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x266c91]` | 1 |  | 660 | 47..67110425, 660 distinct | a face: the picture id a dialogue line's `face` names. The id here is 100 × character + slot (1 to 99) |
| `G[0x27f331]` | 1 |  | 660 | 18273..67110432, 660 distinct | a second picture for the same slot; only `HISTORY` reads it |

### CVINIT (voice settings) and CIINIT (profiles)

`CVINIT`: leader `G[0x2979d1]`, ids from 0, 36 settings. `CIINIT`: leader `GS[0x6864]`, capacity 200, 35
profiles.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x2979d1]` | 1 |  | 36 | 136..16032864, 35 distinct | the setting's text colour (`0x3a3a3a` …); `INIT2` copies it to the player's colour (`G[0x36ed]`) where that is unset |
| `G[0x2981a1]` | 1 |  | 35 | 1..35, 35 distinct | a character's voice setting, indexed by character id; the page's voice block reads the player's switch for it (`G[0x2d57]`) |
| `G[0x298589]` | 1 |  | 35 | 1..313, 35 distinct | the setting's character (an id in `CHINIT`'s range) |
| `G[0x298971]` | 1 |  | 22 | 5804..50332434, 22 distinct | a sample voice for the setting; `CONFIG4` plays it |
| `G[0x298da9]` † | 6 | 0..5 | 16 | 19606..16778016, 96 distinct | resource ids. Off the grid only because `EUINIT`'s 80 cells lie between it and the table's other arrays |
| `GS[0x58c4]` | 3 | 0..2 | 36 | エキストラ / 仙崎 秀哉 / 仙崎 美來 | three labels (エキストラ; 仙崎 秀哉 …) |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x29c45b]` | 1 |  | 35 | 10..350, 35 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x29c523]` | 1 |  | 35 | 1..82, 35 distinct | the profile's character (a `CHINIT` id): `INFOCH` shows the profile once the shared profile's cell for that character (`G[0x1af90 + character]`) is set |
| `G[0x29c9d3]` | 1 |  | 35 | 21382..16777990, 35 distinct | the profile's picture (`INFOCH`) |
| `G[0x29ca9b]` | 1 |  | 35 | 19558..16778000, 2 distinct 19558×32, 16778000×3 | a script `INFOCH` runs for the profile: `CIMES`, or `$1$CIMES` on three |
| `GS[0x6864]` | 1 |  | 35 | 仙崎　秀哉 / 仙崎　美來 / 蘇芳　杏里咲 | the profile's name |
| `GS[0x692c]` | 16 | 0..15 | 35 | 仙崎　秀哉　（せんざき しゅうや） / 仙崎　美來　（せんざき みらい） / 蘇芳　杏里咲　（すおう ありさ） | the profile's sixteen lines |

### CCINIT (party characters)

Leader `G[0x29fa46]` (four cells an id), capacity 100: 26 records, keyed by character id (1 to 82). They are the
characters who can join the party, and what each brings at each of its levels. A character's state lives in
saved cells, which the scripts read the table through: `G[0x3ae0 + character]` (bit 1 while in the party;
`UNITECH` sets bits 0 and 1, `LEAVECH` clears bit 1, or only bit 0 while bit 0 of `G[0x36e0]` is set),
`G[0x3ba8 + character]` its level and `G[0x3c0c + character]` its experience (`RESULT`: the level rises as
`RFINIT`'s `G[0x32fb16]` gives, to 9, or to 35 once `G[0x36d1]` is set), `G[0x3c70 + character]` its 奥義 level,
`G[0x3d38 + character]` its picture set, and `G[0x3d9c + 4 × character + line]` its open skill lines. `CALCCC`
gives the cards and items of each level the character reaches, once (an item column, `G[0x2ad507]`, is in the
scripts and no record writes it).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x29fa46]` | 4 | 0..3 | 26 | 0..1, 2 distinct 0×23, 1×81 | which of the character's four skill lines are open (1) at the start. `UNITECH` copies the four cells into the save's `G[0x3d9c + 4 * character]` when the character first joins |
| `G[0x2ab5c7]` | 40 | 2..35 | 26 | 1..88, 26 distinct | a card the character brings at each level (the column, 2 to 35): its row in `CDINIT`. `CALCCC` gives it with `ADDCARD` when the level is reached |
| `G[0x2ac567]` | 40 | 2..35 | 26 | 1..2, 2 distinct 1×684, 2×117 | and that card's slot |
| `G[0x2ae4a7]` | 160 | 4..131 | 26 | 1..931, 265 distinct | the skills (`SKINIT` ids) the character has at each level, four a level, one for each skill line: the column is `4 * level + line`. `COUNTSKILL` counts a skill only while its line is open (`G[0x3d9c + 4 * character + line]` is 1) |
| `G[0x2b2327]` | 40 | 1..35 | 26 | 1..9, 9 distinct | the 奥義 level (1 to 9) the character reaches at each level: `COUNTSKILL` raises `G[0x3c70 + character]` to it, and `OPENCOUP` sets column 1's |

## Cards and what they carry

### CDINIT (cards)

Leader `GS[0x7d7c]`, capacity 6000: 200 rows of 30 slots, and a card is one slot of one row (`row * 30 +
slot`; slots 1 to 25 are used). 923 cards on 76 rows up to row 167: 725 in the base data, 50, 3, 32 and 113
from the four appends. The scripts read these arrays with `lookup-array-2d` at stride 30 (60, 90 or 150 where a
card has 2, 3 or 5 cells). Rows 1 to 4 hold the cards of characters 1 to 4 under several names (仙崎 秀哉 and
勇者しゅうや in row 1); whether every row is one character's is not checked. `G[0x4f30 + card]` is how many of
a card the player holds (`ADDCARD` keeps it within 0 to 99), and `G[0x3ad7]` the player's money (0 to 99999).
A scene can bar a card row for a while: it sets `G[0x183d0 + row]` to 1 and clears it later (`SC0180` does so
for row 5), and `CHECKCARD` refuses the row's cards meanwhile. `CHECKCARD` is the deck check: it sets a bit of
`G[0x4688fd]` for each reason a card may not go into the deck under the battle rule in play, and the deck
builder and the book mark a card with any bit set.

The numbers' names are the card panel's own labels. `DRAWCARDINFO` draws a panel from picture `0x3ac8`
(`SO019.AGF`) whose six rows are lettered 種族, スロット, 支配力, コスト, a blank and 攻撃力; over the blank it
draws the sprite `HP`, or `LP` for a leader card (`G[0x43f66c]`), and it writes one array's value on each row.
Beside them are 所持能力1 and 所持能力2, with the names and descriptions of the card's two `TAINIT` ids.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x2b2866]` † | 2 | 0..1 | 19 | 1..82, 32 distinct | **not a card array.** The cells are `G[0x2b32c7 + row]`, one a row, written where the row's first card starts: the row's character, a `CHINIT` id (`BTL` reads a unit's pictures from `CHINIT` through it). Rows 1 to 4 hold 1 to 4, and the values go to 82 |
| `G[0x2b338f]` | 1 |  | 198 | 1..4, 4 distinct 1×50, 2×3, 3×32, 4×113 | the append that added the record (1 to 4); the base data leaves it unset |
| `G[0x2b4aff]` | 1 |  | 923 | 10..60400, 923 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x2b626f]` | 1 |  | 300 | 1..1, 1 distinct 1×300 | 1 on 300 cards that may be in a deck only while their row's character is in the party: `CHECKCARD` refuses such a card while bit 1 of `G[0x3ae0 + character]` is clear |
| `G[0x2b79df]` | 1 |  | 923 | 1..5, 5 distinct 1×250, 2×254, 3×220, 4×108, 5×91 | rarity, 1 to 5: C, UC, R, SR, EU (`INIT2` sets the names in `GS[0x12a77 + rarity - 1]`). The card panel draws one of five marks for it |
| `G[0x2b914f]` | 1 |  | 903 | 1..1, 1 distinct 1×903 | 1 on the 903 cards the card book lists (`INFOCD`; `INFOTA` finds a talent's cards among them) |
| `G[0x2ba8bf]` | 1 |  | 229 | 1..1, 1 distinct 1×229 | 1 on the 229 cards the skills 複製 and 捕縛 can win (their texts' ※一部不可 is the rest). `DECKBUILD` marks an opponent's card that has it and is within the skill level's コスト and rarity, which `INIT2` sets in `G[0x43f51a]` (1 to 6) and `G[0x43f523]` (UC, then R and SR), as the skills' texts give them |
| `G[0x2bc02f]` | 1 |  | 923 | 1..72, 21 distinct | 種族: one bit a race, bit 0 to 6 = 無機, 人間, 怪物, 天使, 悪魔, 魔神, 英雄 (the names are `GS[0x12a7c + bit]`, set by `INIT2`) |
| `G[0x2bd79f]` | 1 |  | 919 | 1..8, 8 distinct | コスト, 1 to 8 |
| `G[0x2bef0f]` | 1 |  | 857 | 1..3, 3 distinct 1×344, 2×496, 3×17 | 支配力, 1 to 3 |
| `G[0x2c067f]` | 1 |  | 889 | 1..28, 26 distinct | 攻撃力, 1 to 28 |
| `G[0x2c1def]` | 1 |  | 338 | 8..400, 135 distinct | ＬＰ, 8 to 400: a leader card's, in the place of ＨＰ; 338 cards have it |
| `G[0x2c355f]` | 1 |  | 585 | 1..70, 26 distinct | ＨＰ, 1 to 70; 585 cards have it |
| `G[0x2c4ccf]` | 1 |  | 461 | 1..3, 3 distinct 1×317, 2×111, 3×33 | スロット, 1 to 3; grows by level, to 3 at most |
| `G[0x2c643f]` | 2 | 0..1 | 898 | 1..957, 393 distinct | two ids in `TAINIT`'s range: the card panel prints their names and descriptions under 所持能力1 and 所持能力2 |
| `G[0x2c931f]` | 2 | 0..1 | 311 | 4..35000, 123 distinct | two prices, the second a fifth of the first. The second is the selling price: `ADDCARD` sells what the player holds over 99 at it |
| `G[0x2cc1ff]` | 1 |  | 226 | 1..1, 1 distinct 1×226 | 1 on 226 cards whose attack effect `ATTACK` draws in `gfx_draw_color`'s mode 3 (subtractive) instead of mode 1 (additive) |
| `G[0x2cd96f]` | 2 | 0..1 | 923 | 15422..67110368, 1844 distinct | two pictures; column 0 is the one the card panel draws |
| `G[0x2d084f]` | 2 | 0..1 | 474 | 14903..67110360, 302 distinct | pictures of the card as a battle's leader, a column for each side (`PHASECONTROLLER`) |
| `G[0x2d372f]` | 2 | 0..1 | 923 | 14924..67110369, 835 distinct | two pictures `ATTACK` draws when the card attacks |
| `G[0x2d660f]` | 1 |  | 913 | 14905..67109855, 3 distinct 14905×808, 33554482×3, 67109855×102 | the icon `DRAWRECIPE` draws for the card on a recipe (three pictures over all the cards) |
| `G[0x2d7d7f]` | 1 |  | 923 | 14904..21285, 39 distinct | the card's attack effect, a resource id; `ATTACK` handles three of them (`0x3aa7`, `0x5324`, `0x5325`) apart |
| `G[0x2d94ef]` | 5 | 0..4 | 389 | 3967..67110366, 808 distinct | five voice clips the battle plays for the card: column 0 in the player's actions (`PH03_PLR_ACTION`), 1 when it attacks and 3 at a later point of `ATTACK`, 4 when its talent acts (`TALENT`) |
| `GS[0x7d7c]` | 1 |  | 923 | 仙崎 秀哉 / 仙崎 秀哉 / 仙崎 秀哉 | the card's name |
| `GS[0x94ec]` | 3 | 0..2 | 907 | 俺の親友だ！　頭良いし宿題見せてくれ / 能力が使えてちょっとグロくなった秀哉 / 腕まくりして何をしようとしてるのかね | up to three lines of flavour text |

### A card's numbers in play (CALCARM) and IMINIT (growth)

The tables hold a card's numbers at level 1. `CALCARM` works out what a card has now. `SYSTEM4` preloads it
into script slot `0x1c`, and nine scripts (`DECKCARDLIST`, `BOOK`, `BTLINFO`, `RESULT`, `SETDECK`, the shops,
`ALCHEMY`, `INIT2`) set the card in `G[0x43f664]` (row) and `G[0x43f665]` (slot) and call the slot. The results
go to arrays of 6000 cells each. The card panel draws those where its caller sets `G[0x43f66d]` (the deck
builder and the book do) and the tables' own numbers where it does not (`ARENAINFO`):

| computed | from |
|---|---|
| `G[0x43f6e6]` the highest level | 1, or 2 + the highest step (0 to 7) at which `IMINIT` has a number for the card |
| `G[0x440e56]` the level | `G[0x7e10 + card] / 100 + 1`, at most the highest level |
| `G[0x4425c6]` progress to the next level | `G[0x7e10 + card] % 100`; 0 at the highest level |
| `G[0x4454a6]` スロット | `G[0x2c4ccf]` and its growth, kept within 0 to 3 |
| `G[0x446c16]` コスト | `G[0x2bd79f]`, kept within 0 to 9 |
| `G[0x448386]` 支配力 | `G[0x2bef0f]`, kept within 0 to 9 |
| `G[0x449af6]` 攻撃力 | `G[0x2c067f]` and its growth, kept within 0 to 999 |
| `G[0x44b266]` ＬＰ | `G[0x2c1def]` and its growth, kept within 0 to 999 |
| `G[0x44c9d6]` ＨＰ | `G[0x2c355f]` and its growth, kept within 0 to 999 |

`G[0x7e10 + card]` is the card's experience, 100 to a level: `RESULT` adds to it after a battle. Growth is
`IMINIT`: four arrays of ten cells a card (`base + 10 * card + step`), where step n holds what reaching level
n + 2 adds, so a card at level L has the sum of steps 0 to L - 2. `IMINIT`'s script writes whichever of the
four a card has, in no fixed order, so it has no leader; the layout below is by the bases `CALCARM` indexes, and
its rows are card rows.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x2e0a1f]` | 300 | 10..257 | 21 | 1..3, 3 distinct 1×121, 2×1, 3×1 | what each level adds to a card's 攻撃力. The cell is `base + 10 * card + step`; a row here is a card row (30 slots of ten steps) |
| `G[0x2ef47f]` | 300 | 20..257 | 23 | 1..30, 14 distinct | what each level adds to a leader's ＬＰ, laid out the same way |
| `G[0x2fdedf]` | 300 | 10..241 | 19 | 1..1, 1 distinct 1×107 | what each level adds to ＨＰ |
| `G[0x30c93f]` | 300 | 10..244 | 62 | 1..1, 1 distinct 1×383 | what each level adds to スロット |

### TAINIT (talents), COINIT (奥義), SKINIT (skills), ILINIT (conditions)

`TAINIT`: leader `GS[0xdb3c]`, capacity 1000, 395 talents (296 base; 41, 17, 41 from appends 1, 3, 4).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x31b3a0]` | 1 |  | 99 | 1..4, 3 distinct 1×41, 3×17, 4×41 | the append that added the record (1 to 4); the base data leaves it unset |
| `G[0x31b788]` | 1 |  | 395 | 10..9600, 395 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x31bb70]` | 1 |  | 395 | 1..1, 1 distinct 1×395 | 1 for every record: the talent book lists the records that have it (`INFOTA`) |
| `G[0x31bf58]` | 1 |  | 395 | 1..9, 9 distinct | when the talent acts, 1 to 9: 特殊, 移動, 登場, 行動, 攻撃, 退却, 前衛エリア, 後衛エリア, 全エリア, the word its description opens with. The panels draw a label for it |
| `GS[0xdb3c]` | 1 |  | 395 | 悪魔の上下関係 / 無尽蔵 / 二回移動 | the talent's name |
| `GS[0xdf24]` | 1 |  | 395 | 特殊：『悪魔』の存在するマスに、上書 / 特殊：このカードは編成時に何枚でも入 / 特殊：ターン開始時、このカードに《再 | its description |

`COINIT`: leader `GS[0xe30c]`, capacity 100, 30 奥義 (27 base, 3 from append 1). Every numeric array has ten
cells an id, columns 1 to 9 for the nine levels. A 奥義 belongs to a character (`CHINIT`'s `G[0x29f65e]`), and
the scripts read its row at the character's 奥義 level (`G[0x3c70 + character]`); an opponent's 奥義 and level
come from the battle (`DUINIT`).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x31c72a]` | 1 |  | 3 | 1..1, 1 distinct 1×3 | the append that added the record (1 to 4); the base data leaves it unset |
| `G[0x31c78e]` | 1 |  | 30 | 10..320, 30 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x31c7f2]` | 1 |  | 30 | 1..1, 1 distinct 1×30 | 1 for every record: the 奥義 book lists the records that have it (`INFOCO`) |
| `G[0x31c856]` | 1 |  | 26 | 1..4, 4 distinct 1×20, 2×1, 3×3, 4×2 | 1 to 4; no script reads it |
| `G[0x31c8ba]` | 10 | 1..9 | 30 | 3..25, 10 distinct | how much the 奥義 gauge fills each turn, by level: `PHASECONTROLLER` adds it to the gauge (100 at most), and `INFOCO` prints it |
| `G[0x31cca2]` | 10 | 1..9 | 28 | -9..100, 42 distinct | the level's number, the one its text gives: damage, turns, a percent or a count (操血の鎌撃: 6 at level 1, 30 at level 9). `COUPACT` uses it |
| `G[0x31d08a]` | 10 | 1..9 | 26 | 180..810, 24 distinct | the x at which `COUP` places the 奥義's picture |
| `G[0x31d472]` | 10 | 1..9 | 26 | 100..320, 15 distinct | and its y |
| `G[0x31d85a]` | 10 | 1..9 | 26 | 19563..16778004, 27 distinct | the 奥義's picture; `DECKBUILD` and `BTL` take it with the x and y |
| `G[0x31dc42]` | 10 | 1..9 | 16 | 19564..16778001, 16 distinct | a second picture `DECKBUILD` takes for the 奥義 |
| `G[0x31e02a]` | 10 | 1..9 | 30 | 14936..19575, 7 distinct | the movie `COUP` plays for the 奥義 |
| `GS[0xe30c]` | 1 |  | 30 | 操血の鎌撃 / 操血の錬成者 / グラビティダウン | the 奥義's name |
| `GS[0xe370]` | 30 | 3..29 | 30 | 【奥義】　操血の鎌撃　ＬＶ１ / 【奥義】　操血の錬成者　ＬＶ１ / 【奥義】　グラビティダウン　ＬＶ１ | three lines of text for each level: column 3 × level + line, levels 1 to 9 |

`SKINIT`: leader `GS[0xef28]`, capacity 1000, 265 skills (223 base, 42 from append 1).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x31e412]` | 1 |  | 265 | 10..9210, 265 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x31e7fa]` | 1 |  | 265 | 1..1, 1 distinct 1×265 | 1 for every record: the skill book lists the records that have it (`INFOSK`) |
| `G[0x31ebe2]` | 1 |  | 265 | 1..3, 3 distinct 1×132, 2×113, 3×20 | where the skill works, 1 to 3: 1 outside battle (its heading 【お店】, 【合成】, 【移動】, 【イベント】 or 【特殊】), 2 in battle (【戦闘】), 3 on the battlefield (【戦場】). `COUNTSKILL` counts a kind-3 skill only for the characters in three roles (`G[0x3ada]`, `G[0x3adb]`, `G[0x3adc]`) |
| `G[0x31efca]` | 1 |  | 133 | -6..300, 24 distinct | the skill's amount, the number its text gives (交渉術Ⅰ: 5 for ５％ＯＦＦ; 隠密行動Ⅰ: -1). `COUNTSKILL` and the battle scripts read it |
| `GS[0xef28]` | 1 |  | 265 | 飛行 / 転移 / 錬金Ⅰ | the skill's name |
| `GS[0xf310]` | 3 | 0..2 | 265 | 【移動】　飛行 / 【移動】　転移 / 【合成】　錬金Ⅰ | three lines of text; the first is the heading (`【移動】　飛行`) |

`ILINIT`: leader `GS[0x11e08]`, capacity 32, 19 conditions. Its arrays lie on a grid of 32 cells from
`G[0x32f96a]`, and the scripts read them at those bases. Six of them get one or two writes, which the layout
tool put at wrong bases; the rows below place those writes on the grid. Each numeric array holds what a
condition does to one of a card's numbers in battle.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x32f96a]` | 1 |  | 19 | 1..2, 2 distinct 1×11, 2×8 | 1 for a helpful condition (再生, 必中, 回避, 攻１ …), 2 for a harmful one (操血, 封印, 幻覚 …) |
| `G[0x32f9aa]` | 1 |  | 1 | -1..-1, 1 distinct -1×1 | what the condition adds to a number of a card in battle (`G[0x45d145]`) while it holds: 束縛 -1 |
| `G[0x32f9ca]` | 1 |  | 5 | -1..4, 5 distinct -1×1, 1×1, 2×1, 3×1, 4×1 | what the condition adds to a card's 攻撃力 in battle while it holds (`G[0x45d005]`): 攻１ to 攻４ add 1 to 4, 操血 -1. `PHASECONTROLLER` takes it off again when the condition ends |
| `G[0x32f9ea]` | 1 |  | 1 | -1..-1, 1 distinct -1×1 | what it adds to a card's ＨＰ or ＬＰ in battle (`G[0x45d055]`): 操血 -1 |
| `G[0x32fa0a]` | 1 |  | 1 | -1..-1, 1 distinct -1×1 | 操血 -1 again; `PHASECONTROLLER` reads this array only on a branch whose test is constant false |
| `G[0x32fa2a]` | 1 |  | 2 | -1..1, 2 distinct -1×1, 1×1 | a change each turn: 強酸 -1, 再生 +1. `PHASECONTROLLER` reads the two cells by their address (`G[0x32fa30]`, `G[0x32fa3a]`) and adds 再生's to a card's ＨＰ at the start of a turn |
| `G[0x32fa4a]` | 1 |  | 1 | 1..1, 1 distinct 1×1 | 再生 1; `PHASECONTROLLER` adds it to a card's ＨＰ like the array before, on another branch |
| `G[0x32fa6a]` | 1 |  | 19 | 1..19, 19 distinct | the condition's icon, 1 to 19: its row in a strip of 15 × 15 icons that `DRAWFIELD` and `DRAWTALENT` draw from, at the column of its kind |
| `GS[0x11e08]` | 1 |  | 19 | 操血 / 封印 / 幻覚 | the condition's name (操血, 封印, 幻覚 …) |

### ITINIT (items)

Leader `GS[0xfec8]`, capacity 2000, 492 items (399 base; 33, 1, 21, 38 from the appends). `G[0x3f90 + item]` is
how many the player holds (`ADDITEM` keeps it within 0 to 99).

A card of the deck can carry a 装備 item, and ten arrays say which card may: `INIT2` takes the item off a card
that fails one of them (a sub of it checks each against the card's numbers from `CALCARM`). The item then adds
its bonuses to the card's numbers (the card panel shows the sums, `CHECKCARD` tests them), and may give it a
talent. A 消費 item is used in battle (`SELITEM`), and its effect is the item's append's `ITEMACT` ("A record's
append picks its scripts" above).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x31f79a]` | 1 |  | 93 | 1..4, 4 distinct 1×33, 2×1, 3×21, 4×38 | the append that added the record (1 to 4); the base data leaves it unset |
| `G[0x31ff6a]` | 1 |  | 492 | 10..9670, 492 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x32073a]` | 1 |  | 492 | 1..5, 5 distinct 1×62, 2×31, 3×61, 4×60, 5×278 | the item's kind, 1 to 5: 重要, 消費, 売却, 素材, 装備 (`INIT2` sets the names in `GS[0x12a84 + kind - 1]`, and every item's heading names its kind). A card carries a 装備 item; `SELITEM` offers 消費 items in battle |
| `G[0x320f0a]` | 1 |  | 59 | 1..1, 1 distinct 1×59 | 1 on 59 of the 62 重要 items: the ones a new game does not carry over. `GAMESTART`, when it carries the items over (bit 5 of `G[0x36d4]`), loads every other item's count from the shared profile |
| `G[0x3216da]` | 1 |  | 492 | 1..5, 5 distinct 1×118, 2×97, 3×125, 4×94, 5×58 | 1 to 5; the card panel draws it with the marks it draws a card's rarity with |
| `G[0x321eaa]` | 1 |  | 492 | 1..1, 1 distinct 1×492 | 1 for every record: the item book lists the records that have it (`INFOIT`) |
| `G[0x32267a]` | 1 |  | 278 | 1..3, 3 distinct 1×126, 2×106, 3×46 | the least スロット a card may have to carry the item |
| `G[0x322e4a]` | 1 |  | 11 | -1..1, 2 distinct -1×5, 1×6 | what the item adds to the コスト of the card that carries it (`DRAWCARDINFO`, `CHECKCARD`) |
| `G[0x32361a]` | 1 |  | 16 | -2..1, 3 distinct -2×1, -1×10, 1×5 | what it adds to the card's 支配力 |
| `G[0x323dea]` | 1 |  | 142 | -12..10, 14 distinct | what it adds to the card's 攻撃力 |
| `G[0x3245ba]` | 1 |  | 20 | 8..150, 13 distinct | what it adds to a leader card's ＬＰ (the item panel's ＬＰ row) |
| `G[0x324d8a]` | 1 |  | 150 | -5..30, 18 distinct | what it adds to the card's ＨＰ |
| `G[0x32555a]` | 1 |  | 8 | 16..24, 8 distinct | a condition (an `ILINIT` id) the 消費 item gives its target in battle (`SELITEM`) |
| `G[0x325d2a]` | 1 |  | 31 | 1..4, 3 distinct 1×22, 3×1, 4×8 | which cells of the battlefield the 消費 item can target: 1 the first side's, 2 the second's, 3 both; 4 and 5 the empty cells of the first or the second side (`SELITEM`) |
| `G[0x3264fa]` | 1 |  | 174 | 3..930, 91 distinct | a talent (a `TAINIT` id) the item gives the card that carries it; the item and card panels print its name and description |
| `G[0x326cca]` | 1 |  | 191 | 1..88, 30 distinct | where set, the one card row whose cards can carry the item |
| `G[0x32749a]` | 1 |  | 6 | 2..21, 5 distinct 2×2, 4×1, 6×1, 8×1, 21×1 | where set, the one slot of a row whose card can carry it |
| `G[0x327c6a]` | 1 |  | 24 | 1..64, 7 distinct | races (bits as in `CDINIT`'s 種族) of which the card must be one |
| `G[0x32843a]` | 1 |  | 16 | 1..16, 2 distinct 1×15, 16×1 | races the card must not be |
| `G[0x328c0a]` | 1 |  | 8 | 1..6, 5 distinct 1×3, 2×1, 4×1, 5×1, 6×2 | the least コスト |
| `G[0x3293da]` | 1 |  | 11 | 1..2, 2 distinct 1×6, 2×5 | the least 支配力 |
| `G[0x329baa]` | 1 |  | 52 | 1..12, 5 distinct 1×35, 2×7, 3×8, 8×1, 12×1 | the least 攻撃力 |
| `G[0x32a37a]` | 1 |  | 21 | 1..1, 1 distinct 1×21 | the least ＬＰ |
| `G[0x32ab4a]` | 1 |  | 157 | 1..6, 6 distinct 1×126, 2×6, 3×12, 4×7, 5×1, 6×5 | the least ＨＰ |
| `G[0x32b31a]` | 2 | 0..1 | 429 | 16..90000, 102 distinct | two prices, the second a fifth of the first where there is one. The second is the selling price: `ADDITEM` sells what the player holds over 99 at it |
| `G[0x32c2ba]` | 2 | 0..1 | 492 | 19712..67110610, 984 distinct | two pictures; column 1 is the one the card panel draws |
| `G[0x32d25a]` | 1 |  | 31 | 1..3, 3 distinct 1×22, 2×6, 3×3 | how `SELITEM` plays the 消費 item's effect movie: 1 on the target cell, 2 in the middle at 512 × 512, 3 at a fixed place |
| `G[0x32da2a]` | 1 |  | 31 | 1..1, 1 distinct 1×31 | the mode `gfx_draw_color` draws the effect movie in (1, additive, on all 31) |
| `G[0x32e1fa]` | 3 | 0..2 | 31 | 14948..19849, 13 distinct | the effect movies; style 1 takes the target column's |
| `GS[0xfec8]` | 1 |  | 492 | 美來のハンカチ / 美來のカバン / ＮＴＤカード３Ｆ | the item's name |
| `GS[0x10698]` | 3 | 0..2 | 492 | 【重要】美來のハンカチ / 【重要】美來のカバン / 【重要】ＮＴＤカード３Ｆ | three lines of text; the first is the heading (`【重要】美來のハンカチ`) |

### DEINIT (decks), ALINIT (recipes), SHINIT (shop), REINIT (battle rules)

`DEINIT`: leader `GS[0x1201c]`, capacity 1000, 573 decks: the opponents' decks, of a battle (`DUINIT`) or of
a unit on the map (`UNINIT`'s `G[0x332f56]`). A map unit's copies of each card are kept in the save's
`G[0x16217 + 30 × unit + slot]`, which `ALLMAP` fills between the two counts below. `ALLMAP` also reads two
arrays that no record writes, each a scene (`id + 1`) for each card slot: the slot gets the most copies while
the first's scene has not been played (`G[0x359246]`) or once the second's has (`G[0x360776]`).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x334856]` | 30 | 1..20 | 558 | 1..167, 56 distinct | the cards, slot 1 to 20: each card's row in `CDINIT` (1 to 167) |
| `G[0x33bd86]` | 30 | 1..20 | 558 | 1..23, 23 distinct | and its slot in that row (1 to 23): the two name one card |
| `G[0x3432b6]` | 30 | 1..20 | 558 | 1..30, 13 distinct | the most copies of each card a map unit's deck holds |
| `G[0x34a7e6]` | 30 | 1..16 | 534 | 1..6, 5 distinct 1×1327, 2×115, 3×15, 4×5, 6×1 | how many copies of each card the deck holds: a battle's deck takes this many (`DECKBUILD`), and a map unit's starts from it |
| `G[0x351d16]` | 30 | 1..9 | 65 | 10..90, 13 distinct | a percent for each card slot: for each copy between the two counts `ALLMAP` draws 0 to 99 and adds the copy where the draw is at least this |
| `G[0x367ca6]` | 6 | 0..5 | 573 | -1..13, 15 distinct | the opponent's battlefield at the start, six cells (two rows of three): a value of 0 or less goes into the cell as it is, a positive one is a slot of the deck whose card starts there (`PHASECONTROLLER`) |
| `G[0x369416]` | 1 |  | 547 | 1..59, 51 distinct | the CPU profile the deck is played with, an id of `RTINIT`'s CPU arrays (`SETREGULATION` passes it on) |
| `G[0x3697fe]` | 4 | 0..3 | 454 | 1..200, 42 distinct | four drops: each one's weight. `RESULT` makes weight sum / 100 drops (and one more with the rest's percent), each a drop drawn by weight |
| `G[0x36a79e]` | 4 | 0..3 | 388 | 34..868, 150 distinct | a drop that is an item: its id |
| `G[0x36b73e]` | 4 | 0..1 | 69 | 50..165, 15 distinct | a drop that is a card: its row |
| `G[0x36c6de]` | 4 | 0..1 | 69 | 2..22, 15 distinct | and its slot |
| `GS[0x1201c]` | 1 |  | 573 | 初期デッキ / 対新聞部員 / 第一新聞部員？ | the deck's name |

`ALINIT`: capacity 2000, 617 recipes. A recipe makes an item (`G[0x372ba6]`, 185 recipes) or a card
(`G[0x373376]` and `G[0x373b46]`, the other 432) from up to four ingredients, each an item or a card. `ALCHEMY`
offers a recipe only where every condition below holds, and not where it makes a 重要 item the player already
holds. Its test also reads four arrays no record writes: a least day (`G[0x375a86]`, against `G[0x987b]`), the
days of the week (`G[0x376256]`, bits of `G[0x987b] % 7`), the time slots (`G[0x376a26]`, bits of `G[0x987c]`)
and five scenes none of which may have been played (`G[0x37cfb6]`).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x372ba6]` | 1 |  | 185 | 27..952, 185 distinct | the item the recipe makes (`ITINIT`'s range), on 185 entries |
| `G[0x373376]` | 1 |  | 432 | 1..167, 60 distinct | or the card it makes: its row in `CDINIT`, on the other 432 |
| `G[0x373b46]` | 1 |  | 432 | 1..25, 25 distinct | and its slot (1 to 25) |
| `G[0x374316]` | 1 |  | 617 | 5..25, 5 distinct 5×210, 10×129, 15×139, 20×92, 25×47 | points the recipe earns each time it is made: `ALCHEMY` adds them to `G[0x3ad8]`, which sets the rank `G[0x3ad9]` (`CALCPR`, by `RFINIT`'s thresholds) |
| `G[0x374ae6]` | 1 |  | 617 | 2..2, 1 distinct 2×617 | the alchemy places that offer the recipe, one bit a place (`G[0x4525d8]` is the place in use); all have 2 |
| `G[0x3752b6]` | 1 |  | 2 | 6..6, 1 distinct 6×2 | the least chapter (`G[0x36cd]`, which the chapters' first scenes set) |
| `G[0x3771f6]` | 1 |  | 183 | 7..55, 18 distinct | the least rank (`G[0x3ad9]`) |
| `G[0x3779c6]` | 1 |  | 412 | 1..82, 26 distinct | a character (a `CHINIT` id) who must be in the party |
| `G[0x378196]` | 1 |  | 244 | 1..35, 13 distinct | and that character's least level (`G[0x3ba8 + character]`) |
| `G[0x378966]` | 1 |  | 468 | 3..427, 39 distinct | a skill the party must have (`COUNTSKILL`'s counts) |
| `G[0x379136]` | 1 |  | 43 | 105..952, 35 distinct | an item the player must have obtained: `ADDITEM` sets `G[0x4760 + item]` the first time |
| `G[0x379906]` | 1 |  | 261 | 1..167, 37 distinct | a card the player must have obtained (`ADDCARD` sets `G[0x66a0 + card]` the first time): its row |
| `G[0x37a0d6]` | 1 |  | 261 | 1..24, 18 distinct | and its slot |
| `G[0x37a8a6]` | 5 | 0..1 | 324 | 77..9899, 28 distinct | scenes (each `id + 1`) that must all have been played |
| `G[0x37f6c6]` | 4 | 0..3 | 599 | 101..952, 231 distinct | the four ingredients: an item id where the ingredient is an item |
| `G[0x381606]` | 4 | 0..3 | 358 | 1..167, 58 distinct | or a card's row where it is a card |
| `G[0x383546]` | 4 | 0..3 | 358 | 1..24, 23 distinct | and its slot |
| `G[0x385486]` | 4 | 0..3 | 617 | 1..40, 11 distinct | how many of each ingredient the recipe takes |

`SHINIT`: capacity 1000, 232 entries. An entry sells an item (`G[0x3873c6]`, 135 entries) or a card
(`G[0x3877ae]` and `G[0x387b96]`, the other 97). `SHOPP` lists an entry where every condition below holds, and
not where it sells a 重要 item the player already holds. Its test reads three arrays no entry writes, as
`ALCHEMY`'s does: a least day (`G[0x38874e]`), the days of the week (`G[0x388b36]`) and the time slots
(`G[0x388f1e]`).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x3873c6]` | 1 |  | 135 | 13..950, 128 distinct | the item the entry sells (`ITINIT`'s range), on 135 entries |
| `G[0x3877ae]` | 1 |  | 97 | 11..167, 12 distinct | or the card it sells: its row in `CDINIT`, on the other 97 |
| `G[0x387b96]` | 1 |  | 97 | 1..21, 21 distinct | and its slot |
| `G[0x387f7e]` | 1 |  | 232 | 2..32, 5 distinct 2×81, 4×34, 8×75, 10×7, 32×35 | the shops that sell it, one bit a shop (`G[0x4525d8]` is the shop in use) |
| `G[0x388366]` | 1 |  | 57 | 2..7, 6 distinct 2×2, 3×2, 4×9, 5×13, 6×16, 7×15 | the least chapter (`G[0x36cd]`) |
| `G[0x389306]` | 1 |  | 10 | 10..40, 4 distinct 10×2, 20×2, 25×3, 40×3 | the least rank (`G[0x3ad9]`) |
| `G[0x3896ee]` | 1 |  | 125 | 1..13, 12 distinct | a character who must be in the party |
| `G[0x389ad6]` | 1 |  | 7 | 3..7, 3 distinct 3×3, 5×1, 7×3 | and that character's least level |
| `G[0x389ebe]` | 1 |  | 126 | 18..107, 49 distinct | a skill the party must have |
| `G[0x38a2a6]` | 2 | 0..1 | 71 | 63..9822, 21 distinct | scenes (each `id + 1`) that must all have been played |
| `G[0x38aa76]` | 2 | 0..1 | 6 | 3124..9069, 8 distinct | scenes (each `id + 1`) none of which may have been played |

`REINIT`: leader `G[0x3718e6]` (five cells an id), capacity 100, 76 rules: a battle's special conditions
and its limits on the deck. The rule in play is `G[0x469105]`: `SETREGULATION` takes it from the battle
(`DUINIT`'s `G[0x37055e]`) or, for a battle on the map, from the area (`AMINIT`'s `G[0x330b01]`), copies the
rule's numbers into the battle's cells, and without a rule uses a turn limit of 99, a hand of 6, a deck of 30
and the events 0, 0, 2, 1, 2. The rule's six lines say what it does (`◆特殊勝敗条件・編成制限◆`, then lines such
as `コスト３以下：編成`), and every number below was read against them; `CHECKCARD` applies the limits.
「リーダー固定」 and 「ＥＸデッキ」 match no array here.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x3718e6]` | 5 | 0..4 | 76 | 1..2, 2 distinct 1×92, 2×155 | what five events do to the battle, 0 nothing, 1 a win, 2 a loss; by the rules' texts, the events are: the player's deck runs out, the opponent's deck runs out, the player's ＬＰ reaches 0, the opponent's ＬＰ reaches 0, the turn limit is reached. Rule 1, whose text is 敵のＬＰを０にする：敗北, has 2 in the fourth |
| `G[0x371ada]` | 1 |  | 51 | 1..1, 1 distinct 1×51 | 1 where the cards' ＨＰ recovers at the start of a turn (ターン開始時にＨＰ回復) |
| `G[0x371ec2]` | 1 |  | 76 | 2..99, 12 distinct | the turn limit (99 for none): ２０ターン経過する |
| `G[0x371f26]` | 1 |  | 76 | 4..6, 2 distinct 4×1, 6×75 | the most cards in hand (最大手札４ is 4; 6 elsewhere) |
| `G[0x371f8a]` | 1 |  | 76 | 15..30, 3 distinct 15×1, 20×4, 30×71 | the most cards in a deck (編成最大枚数２０ is 20; 30 elsewhere) |
| `G[0x371fee]` | 1 |  | 41 | 3..3, 1 distinct 3×41 | two bits: `PH03_PLR_ACTION` tests the first, `BTL` and `SETBTSKILL` the second; アイテム・奥義使用不可 is 3 |
| `G[0x3723d6]` | 2 | 1 | 6 | 2..5, 4 distinct 2×1, 3×1, 4×3, 5×1 | a card's コスト must lie in [least, most], a most of 0 for none (コスト３以下 is 0 and 3). `CHECKCARD` adds the carried item's bonus first |
| `G[0x37249e]` | 2 | 0..1 | 3 | 1..1, 1 distinct 1×3 | the same for 支配力 (支配力１以上 is 1 and 0) |
| `G[0x372566]` | 2 | 0..1 | 5 | 3..10, 5 distinct 3×2, 5×1, 8×1, 9×1, 10×2 | the same for 攻撃力 (攻撃力３～１０ is 3 and 10) |
| `G[0x37262e]` | 2 | 0..1 | 3 | 3..13, 5 distinct 3×1, 4×1, 9×1, 10×1, 13×1 | the same for ＨＰ |
| `G[0x3726f6]` | 2 | 0..1 | 15 | 2..126, 11 distinct | two race masks: a card with a race of the first may not be used (全『人間』使用不可 is 2), nor a card all of whose races are in the second (純『天使』使用不可 is 8; 『人間』のみ使用可 is every race but 人間) |
| `G[0x3727be]` | 10 | 0..7 | 13 | 1..153, 18 distinct | card rows that may not be used (「美來」使用不可 is row 2; 過去作カード不可 rows 32, 33, 34, 38 and 39) |
| `GS[0x12404]` | 6 | 0..5 | 76 | ◆特殊勝敗条件・編成制限◆ / ◆特殊勝敗条件・編成制限◆ / ◆特殊勝敗条件・編成制限◆ | six lines of text; the first is the heading ◆特殊勝敗条件・編成制限◆ |

### RFINIT (constants), DUINIT (battles), RTINIT (unit routines, CPU profiles)

No leaders; by the arrays the scripts index.

`RFINIT` copies bulk arrays (`copy-inline-int-array`): the game's constant tables. `G[0x3ad8]` is a points
total that recipes add to (0 to 99999), and `CALCPR` sets the rank `G[0x3ad9]` from it by the thresholds here.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x32fa8a]` | 10 | 0..8 | 4 | 1..2000, 21 distinct | four rows of nine numbers (1, 1, 1, 2 …; 1, 15, 30 … 500; up to 1000; up to 2000): the experience `RESULT` gives the party for each enemy card defeated, at the cell two numbers of that card pick |
| `G[0x32fab2]` | 10 | 0..9 | 10 | 0..99999, 99 distinct | the points each rank needs (`CALCPR`): rank r at cell `10 * (r % 10) + r / 10`, ranks 1 to 99, up to 99999 |
| `G[0x32fb16]` | 10 | 0..9 | 7 | 0..9999, 35 distinct | row 0: the experience a character needs for each level, 0 to 9999 (`RESULT`, `FELLOW`). The rows after it hold arrays the tool lumps here (`RFINIT` writes five lists of eight at a stride of nine from `G[0x32fb2a]`, which no script reads) |
| `G[0x32fb57]` | 10 | 0..7 | 8 | 0..1, 2 distinct 0×27, 1×37 | eight rows of eight switches: which of the eight conditions of the next array `BTL` may give at random, by a row it picks |
| `G[0x32fbbb]` | 1 |  | 8 | 17..25, 8 distinct | eight conditions (`ILINIT` ids 17 to 25: 必中, 回避, 攻１, 攻２, 攻３, 連撃, 再動, 返戻) |

`DUINIT`: a battle, by id in `G[0x3ad5]` (scene code sets it, 0x322 and up, or an arena opponent does). Side 0
is the player's.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x36d67e]` | 1 |  | 208 | 1..1, 1 distinct 1×208 | 1 where the player fights with their own deck and leader; elsewhere the battle sets the player's side too (`SETREGULATION`) |
| `G[0x36da66]` | 4 | 0..3 | 257 | 1..167, 65 distinct | the leader card of each side: row and slot for side 0, then for side 1 |
| `G[0x36ea06]` | 2 | 0..1 | 257 | 2..957, 280 distinct | each side's deck (a `DEINIT` id) |
| `G[0x36f1d6]` | 1 |  | 110 | 1..27, 23 distinct | the opponent's 奥義 (a `COINIT` id) |
| `G[0x36f5be]` | 1 |  | 110 | 1..9, 9 distinct | and its level |
| `G[0x36f9a6]` | 1 |  | 47 | 19583..19704, 21 distinct | the opponent's 奥義 picture |
| `G[0x36fd8e]` | 1 |  | 47 | 180..810, 14 distinct | and its x |
| `G[0x370176]` | 1 |  | 47 | 100..320, 10 distinct | and its y |
| `G[0x37055e]` | 1 |  | 135 | 1..86, 75 distinct | the battle's rule (a `REINIT` id); `SETREGULATION` takes it |
| `G[0x370946]` | 1 |  | 257 | 8..42, 12 distinct | the battle's music (`BTL` plays it) |
| `G[0x370d2e]` | 1 |  | 90 | 1..1, 1 distinct 1×90 | a switch `BTL` tests (1 on 90 battles) |
| `G[0x371116]` | 2 | 0..1 | 257 | 14897..67110532, 56 distinct | two pictures `PHASECONTROLLER` sets behind the battlefield (where `AMINIT`'s pair is used for a battle on the map) |

`RTINIT`: the routines a map unit follows (by routine and step, 50 steps a routine), then the CPU's profiles
for a battle (by profile).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x391fa9]` | 50 | 0..16 | 167 | 1..8, 8 distinct | a map unit's routine (the unit's routine is `UNINIT`'s `G[0x333276]`), step by step: the step's action, 1 to 8 for `RTN_M001` to `RTN_M008`; 0 ends the routine (`MVRTN`) |
| `G[0x39e2f9]` | 50 | 0..16 | 167 | 10..400, 11 distinct | the percent chance the step runs (`MVRTN` draws 0 to 99) |
| `G[0x3aa649]` | 50 | 0..16 | 167 | 1..128, 92 distinct | the step's first argument (`RTN_M001` waits that long, 20 without one) |
| `G[0x3b6999]` | 50 | 0..15 | 162 | 1..99, 8 distinct | its second argument |
| `G[0x3c2ce9]` | 50 | 0..15 | 125 | 1..9, 9 distinct | its third argument |
| `G[0x3e76d9]` | 150 | 0..46 | 24 | 2801..10000, 27 distinct | for each step, three scenes (each `id + 1`) that must all have been played |
| `G[0x40c0c9]` | 150 | 0..27 | 15 | 3159..9760, 18 distinct | for each step, three scenes none of which may have been played |
| `G[0x430ab9]` | 20 | 0..11 | 53 | -30..15, 13 distinct | a CPU profile (`DEINIT`'s `G[0x369416]` picks one for a deck): twenty settings `PH12_CPU_MOVE` and `PH13_CPU_ACTION` read |
| `G[0x4358d9]` | 10 | 0..5 | 53 | 1..4, 4 distinct 1×53, 2×44, 3×5, 4×1 | the profile's choices for moving (`PH12_CPU_MOVE`) |
| `G[0x437fe9]` | 10 | 0..3 | 54 | 1..2, 2 distinct 1×54, 2×27 | its choices for acting (`PH13_CPU_ACTION`) |
| `G[0x43a6f9]` | 10 | 0..4 | 53 | 1..2, 2 distinct 1×53, 2×40 | its choices for attacking (`PH14_CPU_ATTACK`) |
| `G[0x43ce09]` | 10 | 0..5 | 46 | 1..2, 2 distinct 1×66, 2×22 | a list `ATTACK` reads for the profile |

## World and progression

### AMINIT (areas)

Leader `G[0x32fbc5]`, capacity 300, 150 areas.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x32fbc5]` | 1 |  | 150 | 1..197, 51 distinct | the place an area belongs to (an `AMINIT` id; 51 distinct): scene code tests whether the area in play is in the same place as a given area |
| `G[0x32fcf1]` | 6 | 0..5 | 150 | 4093..67109805, 288 distinct | the area's map pictures: columns 0 to 4 by the time slot `G[0x987c]`, column 5 another (`SETMAPTEX`, `ALLMAP`) |
| `G[0x3303f9]` | 2 | 0..1 | 150 | 2..50, 16 distinct | the area's two tracks of music, by `G[0x987a]` (`READY`) |
| `G[0x330651]` | 2 | 0..1 | 150 | 40..70, 4 distinct 40×2, 50×230, 60×2, 70×66 | the size of a unit at the top and at the bottom of the map, in percent: `DRAWOBJ` scales a unit between the two by its point's y |
| `G[0x3308a9]` | 2 | 0..1 | 150 | 14897..67109803, 54 distinct | two pictures `PHASECONTROLLER` sets behind the battlefield for a battle in the area |
| `G[0x330b01]` | 1 |  | 16 | 33..72, 2 distinct 33×15, 72×1 | the rule (a `REINIT` id) of a battle in the area (16 areas) |
| `GS[0x11e28]` | 1 |  | 150 | 歪秤世界 / 天慶市 / 天慶第二学園 | the area's name (歪秤世界, 天慶市, 天慶第二学園 …) |

### ARINIT (arenas and opponents) and FAINIT

`ARINIT`: leader `GS[0x1268e]`, capacity 1000, 73 opponents. The script also fills the arenas' own arrays,
which hold ten. `G[0x18323]` is the opponent in play, and `G[0x17f36 + opponent]` its state in a save (bit 0
set once its conditions have held, bit 1 once beaten). `ARENA` lists an opponent where every condition below holds; its test also reads arrays no
record writes (a least chapter, rank and day, time slots, a party character and its level, a skill:
`G[0x38e50e]`, `G[0x38f4ae]`, `G[0x38e8f6]`, `G[0x38f0c6]`, `G[0x38f896]`, `G[0x38fc7e]`, `G[0x390066]`).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x38b246]` | 1 |  | 73 | 801..965, 60 distinct | the battle the opponent is fought in (a `DUINIT` id) |
| `G[0x38b62e]` | 1 |  | 73 | 1..9, 9 distinct | the arena the opponent fights in, 1 to 9 |
| `G[0x38ba16]` | 1 |  | 73 | 10..270, 22 distinct | a sort key: `ARENA` lists the opponents in its order |
| `G[0x38bdfe]` | 1 |  | 54 | 12..913, 50 distinct | the reward when it is an item: the item |
| `G[0x38c1e6]` | 1 |  | 5 | 23..153, 2 distinct 23×3, 153×2 | the reward when it is a card: its row |
| `G[0x38c5ce]` | 1 |  | 5 | 2..8, 4 distinct 2×1, 4×1, 6×2, 8×1 | and its slot |
| `G[0x38c9b6]` | 1 |  | 59 | 1..3, 3 distinct 1×57, 2×1, 3×1 | how many of the reward the player gets |
| `G[0x38d186]` | 3 | 0..2 | 42 | 511..8932, 34 distinct | three scenes: column 0 is played before the battle, and `BTL` plays column 1 or 2 after it by its outcome (1 or 2, a win and a loss in `REINIT`'s coding) |
| `G[0x38dd3e]` | 1 |  | 73 | 2..8, 3 distinct 2×20, 4×24, 8×29 | the places that hold the opponent, one bit a place (`G[0x1831e]` is the place in use) |
| `G[0x38e126]` | 1 |  | 21 | 1..426, 17 distinct | another opponent who must have been beaten (bit 1 of `G[0x17f36 + opponent]`, a saved cell) |
| `G[0x38ecde]` | 1 |  | 7 | 1..1, 1 distinct 1×7 | the days of the week (bits of `G[0x987b] % 7`) the opponent is there |
| `G[0x39044e]` | 2 | 0..1 | 27 | 511..9899, 10 distinct | scenes (each `id + 1`) that must all have been played |
| `G[0x390c1e]` | 2 | 0 | 2 | 3122..3122, 1 distinct 3122×2 | scenes (each `id + 1`) none of which may have been played |
| `G[0x3913ee]` | 1 |  | 21 | 2..8, 7 distinct | a group (2 to 8) `ARENA` draws a tournament arena's opponents from |
| `G[0x391bbe]` | 1 |  | 21 | 50..100, 2 distinct 50×12, 100×9 | a number `ARENA` compares between two opponents of a tournament (50 or 100) |
| `GS[0x12429]` | 1 |  | 35 | 対戦台 / 連戦台 / 大会戦 | **not an opponent array.** The arenas' own strings, ten arenas: `GS[0x1265c + arena]` is an arena's name (対戦台, 連戦台, 大会戦 …) and `GS[0x12666 + 4 * arena + line]` its four lines of description |
| `GS[0x1268e]` | 1 |  | 73 | リュカアース / ゲオテット / 歪秤の鳥娘 | the opponent's name |

`FAINIT`: leader `GS[0x29e4]`, capacity 1000, 454 entries (370 in the base data; 32, 12 and 40 from appends 1,
3 and 4): goals the game opens and closes by which scenes have been played. An entry's state is
`G[0x18498 + entry]` in a save. `SETFATE` opens an entry (bit 0) by the scenes below, and sets bit 1 or bit 2
of an open one by two more pairs of scene lists; it also tests a least chapter (`G[0x260ae9]`), which no entry
writes. `RECEIPT` shows the current entry's reward.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x260701]` | 1 |  | 454 | 9010..25270, 453 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x260ed1]` | 3 | 0..2 | 454 | 12..9900, 374 distinct | three scenes (each `id + 1`) that must all have been played for the entry to open (`SETFATE`) |
| `G[0x261a89]` | 3 | 0..1 | 90 | 7005..9900, 12 distinct | three scenes none of which may have been played for it to open |
| `G[0x262641]` | 3 | 0..1 | 450 | 13..9899, 385 distinct | three scenes that, all played, set bit 2 of the entry's state |
| `G[0x2631f9]` | 3 | 0 | 5 | 9524..9751, 5 distinct 9524×1, 9525×1, 9634×1, 9654×1, 9751×1 | three scenes none of which may have been played for that |
| `G[0x263db1]` | 3 | 0 | 97 | 101..9658, 20 distinct | three scenes that, all played, set bit 1 of the entry's state |
| `G[0x264969]` | 3 | 0 | 71 | 13..7090, 46 distinct | three scenes none of which may have been played for that |
| `G[0x265521]` | 1 |  | 454 | 1..3, 3 distinct 1×228, 2×71, 3×155 | the entry's kind, 1 to 3. `SETFATE` counts the open entries of each kind into `G[0x3d46c + kind]`, and `GETFATE` makes the open kind-1 entry latest in the sort order the current one (`G[0x260700]`) |
| `G[0x265909]` | 1 |  | 60 | 105..953, 56 distinct | the entry's reward when it is an item (`RECEIPT`) |
| `G[0x265cf1]` | 1 |  | 11 | 31..39, 3 distinct 31×4, 38×5, 39×2 | the reward when it is a card: its row |
| `G[0x2660d9]` | 1 |  | 11 | 1..10, 9 distinct | and its slot |
| `G[0x2664c1]` | 1 |  | 71 | 1..1, 1 distinct 1×71 | a count `RECEIPT` prints (1 on 71) |
| `G[0x2668a9]` | 1 |  | 38 | 20..200, 6 distinct 20×3, 40×1, 50×12, 60×1, 100×7, 200×14 | money the goal gives, 20 to 200: `RECEIPT` prints it and adds it to `G[0x3ad7]` |
| `GS[0x29e4]` | 1 |  | 454 | 『天慶第二学園』へ / 『２年教室』へ / 『合同教室』へ | a line of text (『天慶第二学園』へ) |
| `GS[0x2dcc]` | 1 |  | 454 | 『天慶第二学園』へ / 『２年教室』へ / 『合同教室』へ | a second line, the same as the first on 449 of the 454 entries |
| `GS[0x31b4]` | 4 | 0..3 | 454 | 【共通ルート】　序章 / 【共通ルート】　序章 / 【共通ルート】　序章 | four lines of text; the first is a chapter's heading (【共通ルート】　序章) |

## Registries

### VIINIT, TIINIT, CGINIT, SPINIT, MUINIT, EUINIT

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x29a51a]` | 1 |  | 193 | 10..4690, 193 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x29a902]` | 3 | 0 | 193 | 1..8487, 89 distinct | up to three scenes (each `id + 1`): `INFOVI` lists the entry once one of them is seen (the shared profile's seen cells); none, and it is always listed |
| `G[0x29b4ba]` | 1 |  | 193 | 570..570, 1 distinct 570×193 | the x at which `INFOVI` draws the entry's picture (570 for all) |
| `G[0x29b8a2]` | 1 |  | 193 | 130..130, 1 distinct 130×193 | and the y (130 for all) |
| `G[0x29bc8a]` | 1 |  | 193 | 20964..21048, 85 distinct | the entry's picture |
| `G[0x29c072]` | 1 |  | 193 | 20963..16778103, 2 distinct 20963×192, 16778103×1 | a script `INFOVI` runs for the entry: `VIMES`, or `$1$VIMES` on one |
| `GS[0x647c]` | 1 |  | 193 | 【用語】ＭＨＩ / 【用語】最先端医療 / 【用語】八葉 | the glossary entry's title (【用語】ＭＨＩ) |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x29cb64]` | 1 |  | 75 | 10..750, 75 distinct | a sort number: unique to each record, in steps of ten through the base data. The game's lists of these records are in its order (`sort_indices_by_key_sum`) |
| `G[0x29cf4c]` | 3 | 0 | 75 | 1..55, 19 distinct | up to three scenes: `INFOTI` lists the entry once one of them is seen; none, and it is always listed |
| `G[0x29db04]` | 1 |  | 75 | 570..570, 1 distinct 570×75 | the x of the entry's picture (570 for all) |
| `G[0x29deec]` | 1 |  | 75 | 130..130, 1 distinct 130×75 | and the y (130 for all) |
| `G[0x29e2d4]` | 1 |  | 75 | 21012..21493, 74 distinct | the entry's picture |
| `G[0x29e6bc]` | 1 |  | 75 | 20615..20615, 1 distinct 20615×75 | a script `INFOTI` runs for the entry: `TIMES` for all |
| `GS[0x75ac]` | 1 |  | 75 | 日常モード / 拠点アイコン・サムネイル / プレイヤーアイコン | the entry's title (日常モード, 拠点アイコン・サムネイル …) |

`CGINIT`: leader `G[0x2283f1]` (two cells an id), capacity 3000, 1577 entries. An entry carries three numbers:
a section (1 to 9), a group in it (1 to 57), and its place in the group (1 to 35). 224 groups have a picture
of their own (191 in the base data).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x2283f1]` | 2 | 0..1 | 1577 | 94..67110458, 1714 distinct | two pictures: `CGMODE` shows column 0; where column 1 is set, `ALLMAP` and `SAVE` find the entry whose column 0 is the picture last shown (`G[0x36cf]`) and use column 1 in its place |
| `G[0x229b61]` | 1 |  | 1577 | 1..9, 9 distinct | the entry's section, 1 to 9 |
| `G[0x22a719]` | 1 |  | 1577 | 1..57, 57 distinct | its group in the section, 1 to 57 |
| `G[0x22b2d1]` | 1 |  | 1577 | 1..35, 35 distinct | its place in the group, 1 to 35 |
| `G[0x22d301]` † | 1 |  | 224 | 18968..67110457, 224 distinct | **not indexed by entry.** The cells are `G[0x22d5f9 + 100 * section + group]`, one resource id a group, written with the group's first entry |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x25fade]` | 1 |  | 65 | 654..67109429, 65 distinct | resource ids; no script reads them |
| `G[0x25fec6]` | 1 |  | 65 | 20555..67110626, 65 distinct | resource ids; no script reads them |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x260667]` | 1 |  | 34 | 1..20554, 33 distinct | a resource id `SETMEMOIR` asks whether the player has unlocked (`is_catalog_resource_unlocked`) |
| `G[0x260699]` | 1 |  | 34 | 2..41, 34 distinct | a number, 2 to 41, which `SETMEMOIR` lists for each track whose resource is unlocked |
| `GS[0x29b2]` | 1 |  | 34 | ｂｒａｖｅ ｇｅｎｅｓｉｓ　FULL / ｂｒａｖｅ ｇｅｎｅｓｉｓ　SHOR / ｔｒｉｎｉｔｙ ｎｉｇｈｔ | the track's title |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x298d59]` | 2 | 0..1 | 9 | 278..67110534, 18 distinct | pictures `CGMODE` draws, two a record |
| `G[0x298d81]` | 2 | 0..1 | 8 | 278..67110534, 16 distinct | pictures `HMODE` draws, two a record |

## On-demand tables

These four are not in the boot image: a game script sets a selector and runs them.

- **BGINIT** maps a picture: for a picture id in `G[0x3fb90]` and a variant in `G[0x987c]` (0 to 4) it leaves
  another picture id in `G[0x3fb90]`.
- **MPINIT**, **ROINIT** and **UNINIT** load one area (`AMINIT`) each time: they clear their arrays (200 slots,
  with 1 to 7 cells a slot) and fill them from the branch for `G[0x9883]`, the area in play. (`MPINIT` has a
  branch for each of `AMINIT`'s 150 ids and for nothing else, and scripts index `AMINIT`'s arrays with the
  cell.) The appends have `$n$` scripts of the same names with branches of their own (the first of
  `$1$MPINIT` adds a point to area 1), which `SETMAPDATA` runs after the base script, each layer in turn.

The clears give each array's base and cells per slot exactly, so these layouts are not inferred. A row gives how
many areas write the array, which slots, and the values over all areas.

`MPINIT`, the area's points:

| array | cells per slot | areas | slots | values | holds |
|---|---|---|---|---|---|
| `G[0x330c2d]` | 1 | 150 | 1..131 | 1..3, 3 distinct 1×5524, 2×240, 3×479 | the point's kind, 1 to 3. A point of kind 3 leads to another point: `ALLMAP` reads the target below for it |
| `G[0x330cf5]` | 2 | 149 | 1..123 | 1..197, 169 distinct | where a point of kind 3 leads: an area (an `AMINIT` id) and a point of it. 479 points have one, as many as are of kind 3 |
| `G[0x330e85]` | 2 | 7 | 1..57 | 1..9732, 5 distinct 1×15, 7007×1, 9607×2, 9731×1, 9732×1 |  |
| `G[0x331015]` | 1 | 150 | 1..131 | 23..1000, 899 distinct | the point's x on the map (`ALLMAP` puts its hot area around x and y) |
| `G[0x3310dd]` | 1 | 150 | 1..131 | 51..537, 459 distinct | the point's y |
| `G[0x3311a5]` | 5 | 88 | 1..120 | 151..10000, 179 distinct |  |
| `G[0x33158d]` | 5 | 36 | 1..120 | 151..9799, 48 distinct |  |
| `G[0x331975]` | 1 | 1 | 2..49 | 7..24, 3 distinct 7×2, 16×6, 24×1 |  |
| `G[0x331a3d]` | 1 | 0 | none | never written |  |
| `G[0x331b05]` | 1 | 7 | 1..76 | 20514..50331696, 47 distinct | a picture for the point, in seven areas; the six arrays after it place it and size its hot area |
| `G[0x331bcd]` | 1 | 7 | 1..76 | 5..923, 25 distinct |  |
| `G[0x331c95]` | 1 | 7 | 1..76 | 20..522, 34 distinct |  |
| `G[0x331d5d]` | 1 | 7 | 1..76 | 5..944, 24 distinct |  |
| `G[0x331e25]` | 1 | 7 | 1..76 | 20..525, 34 distinct |  |
| `G[0x331eed]` | 1 | 7 | 1..76 | 70..152, 3 distinct 70×43, 134×5, 152×6 |  |
| `G[0x331fb5]` | 1 | 7 | 1..76 | 28..78, 3 distinct 28×6, 42×43, 78×5 |  |
| `GS[0x11f54]` | 1 | 150 | 1..131 | 出現地点 / 西蒼川商業区 / ショッピングモール　東部 | the point's name (出現地点, 西蒼川商業区 …) |

`ROINIT`, the routes between points:

| array | cells per slot | areas | slots | values | holds |
|---|---|---|---|---|---|
| `G[0x33207d]` | 2 | 150 | 1..133 | 1..131, 131 distinct | the two points a route joins (`DRAWMAP` and `ALLMAP` read the pair) |
| `G[0x33220d]` | 3 | 12 | 1..118 | 831..9799, 15 distinct |  |
| `G[0x332465]` | 3 | 9 | 1..99 | 831..9799, 13 distinct |  |
| `G[0x3326bd]` | 1 | 1 | 4..31 | 7..16, 2 distinct 7×3, 16×11 |  |
| `G[0x332785]` | 1 | 150 | 1..133 | 1..3, 3 distinct 1×6404, 2×52, 3×42 |  |
| `G[0x33284d]` | 1 | 0 | none | never written |  |
| `G[0x332915]` | 1 | 10 | 1..82 | 1..2, 2 distinct 1×7, 2×8 |  |
| `G[0x3329dd]` | 1 | 0 | none | never written |  |
| `G[0x332aa5]` | 1 | 0 | none | never written |  |
| `G[0x332b6d]` | 3 | 26 | 1..83 | 1..157, 10 distinct |  |

`UNINIT`, the units on the area:

| array | cells per slot | areas | slots | values | holds |
|---|---|---|---|---|---|
| `G[0x332dc6]` | 1 | 150 | 1..106 | 1..4, 4 distinct 1×799, 2×550, 3×203, 4×1232 | the unit's kind, 1 to 4 (`ALLMAP` and `DRAWOBJ` treat kind 4 and kind 1 apart) |
| `G[0x332e8e]` | 1 | 150 | 1..99 | 1..5, 5 distinct 1×151, 2×50, 3×112, 4×162, 5×324 | the unit's icon, 1-based, in a strip `DRAWFRAME` draws from |
| `G[0x332f56]` | 1 | 150 | 1..99 | 1..907, 316 distinct | the unit's deck (a `DEINIT` id): `SETREGULATION` gives a battle with the unit that deck, and `UNITINFO` shows its drops |
| `G[0x33301e]` | 2 | 96 | 2..99 | 1..167, 59 distinct | the unit's card: its row and its slot in `CDINIT` (`BTL` passes the pair to `CALCARM`) |
| `G[0x3331ae]` | 1 | 119 | 2..100 | 2..4532, 276 distinct |  |
| `G[0x333276]` | 1 | 97 | 2..99 | 1..999, 161 distinct | the unit's routine (`RTINIT`), which `MVRTN` runs |
| `G[0x33333e]` | 1 | 97 | 2..99 | 10..100, 10 distinct |  |
| `G[0x333406]` | 1 | 150 | 1..106 | 1..126, 115 distinct | the unit's id in saved state, 1 to 126: `ALLMAP` copies it into the save's `G[0x9ba7 + slot]` when it sets the unit up, and tests the bits of `G[0x17cd8 + id]` |
| `G[0x3334ce]` | 1 | 150 | 1..85 | 1..3, 3 distinct 1×55, 2×385, 3×22 |  |
| `G[0x333596]` | 1 | 7 | 2..62 | 1..1, 1 distinct 1×23 |  |
| `G[0x33365e]` | 1 | 18 | 2..49 | 1..1, 1 distinct 1×51 |  |
| `G[0x333726]` | 7 | 121 | 2..106 | 11..10000, 625 distinct |  |
| `G[0x333c9e]` | 7 | 106 | 2..106 | 12..9805, 674 distinct |  |
| `G[0x334216]` | 1 | 0 | none | never written |  |
| `G[0x3342de]` | 1 | 25 | 2..66 | 15..41, 6 distinct 15×1, 19×1, 20×21, 21×6, 23×20, 41×15 | the music of a battle with the unit (`BTL`) |
| `G[0x3343a6]` | 1 | 25 | 2..66 | 1..1, 1 distinct 1×64 |  |
| `G[0x33446e]` | 1 | 85 | 2..104 | 1..1, 1 distinct 1×257 | an offset `DRAWOBJ` draws the unit at |
| `G[0x334536]` | 1 | 88 | 2..104 | 1..3, 2 distinct 1×267, 3×1 | a second offset `DRAWOBJ` draws the unit at |
| `G[0x3345fe]` | 2 | 150 | 1..106 | 14962..67110630, 169 distinct | two pictures of the unit (`SETMAPTEX`); for the player's unit, `SETAGENT` copies them from `CHINIT` |
| `G[0x33478e]` | 1 | 150 | 1..99 | 14970..67110517, 55 distinct | a third picture of the unit |

## Saved state

A save keeps the integer cells below `0x3fb81` and the string cells below `0xe7` (the profile's
`persistence.bankDimensions`; [`sys4-format-notes.md`](sys4-format-notes.md), "Saved prefix"). Every cell the
29 boot tables write lies above both (the lowest are `SCINIT`'s `G[0x214b71]` and `MUINIT`'s `GS[0x29b3]`), and
so does every cell the four on-demand tables write (the lowest is `BGINIT`'s `G[0x3fb90]`): **no table cell is
in a save**, and boot rebuilds all of the boot tables, the map `INIT2` makes from the faces included. A changed
table therefore reaches an old save as well as a new game.

The other direction is whether the game copies a table's value into a saved cell and reads the copy
afterwards. It does not for a card's numbers: `CALCARM` works them out from the tables each time, into cells
above the saved prefix, and what a save keeps of a card is how many the player holds (`G[0x4f30 + card]`) and
its experience (`G[0x7e10 + card]`), and of an item how many the player holds (`G[0x3f90 + item]`); and of
each whether the player has ever obtained one (`G[0x66a0 + card]`, `G[0x4760 + item]`, which `ADDCARD` and
`ADDITEM` set). So a changed スロット, 支配力, コスト, ＨＰ, ＬＰ, 攻撃力 or growth reaches the cards of an old
save.

Three scans of the 693 decompiled scripts (all but the tables' own and the `DEBUG` ones) looked for a write into
a saved cell whose value reads a table cell: directly, through a lookup's pointer (`LP`), or through a local
that the script set from a table earlier (control flow ignored). What they find is below, with one copy found
by reading (`COUNTSKILL`'s, through another global cell); every other field is read from its table where it is
used, so a changed value reaches an old save. A copy the scans cannot see, through another global cell or a
local of another script, is not ruled out.

- **One-time effects.** The money and points that prices, sales, recipes, drops and goals add or take (`ADDCARD`,
  `ADDITEM`, `SHOPP`, `SHOPS`, `ALCHEMY`, `RESULT`, `RECEIPT`), a card's experience after a battle, and a party
  character's level, experience and 奥義 level (`RESULT`, `CALCCC`, `OPENCOUP`). `CALCCC` gives a level's card
  and item once, when the level is reached. A changed table does not redo what an old save has had.
- **A party character** (`CCINIT`): `UNITECH` copies the four open skill lines into `G[0x3d9c + 4 × character]`
  when the character first joins, so a changed start does not reach a character who has joined. The skills of
  each level are read from the table each time (`COUNTSKILL`), at the saved level. The 奥義 level a level brings
  is copied: `COUNTSKILL` raises the saved `G[0x3c70 + character]` to the highest one up to the character's level
  (through `G[0x45023b + character]`) and never lowers it, so a raised value reaches an old save and a lowered
  one does not.
- **A map unit**: when `ALLMAP` sets a unit up, it zeroes and refills the unit's copies of each card
  (`G[0x16217 + 30 × slot + n]`, from `DEINIT`) and copies its state id (`UNINIT`'s `G[0x333406]`) into
  `G[0x9ba7 + slot]`. The routine scripts (`RTN_M001` to `RTN_M008`, `SETROUTE`) keep the moving unit's step
  state in `G[0x17f31]`, `G[0x17f32]`, `G[0x17987]` and `G[0x1614f + unit]`, from `RTINIT`'s arguments.
- **Arena matches**: `ARENA` copies an opponent's battle and its two after-battle scenes into `G[0x3ad5]`,
  `G[0x3d46a]` and `G[0x3d46b]` when it sets a match up, and keeps a tournament's draw and tallies in
  `G[0x18324 + 8 × arena + n]` and `G[0x18374 + …]`.
- **Scenes**: `SYSTEM4` copies the scene's script id (`SCINIT`) into `G[0x5c9]`, `BTL` and `ALLMAP` copy
  `G[0x21c0a1]` into `G[0x36ca]`, and `SAVE` writes the current goal (`FAINIT`) into the slot's
  `G[0x1a9 + slot]`.
- **The text colour**: `INIT2`, `INITCONFIG` and `CONFIG4` copy `CVINIT`'s colour into `G[0x36ed]` where that is
  unset.
- **Story and progress state** the tables are read through, all in a save: the scenes' played cells
  (`G[0x5e0 + scene]`), the chapter `G[0x36cd]`, the points and rank `G[0x3ad8]` and `G[0x3ad9]`, an arena
  opponent's state `G[0x17f36 + opponent]`, a goal's state `G[0x18498 + entry]`, a barred card row
  `G[0x183d0 + row]`, and a character's party bits, level, experience, 奥義 level and picture set (`CCINIT`
  above).

## What is not decoded

- **Fields without a full reading.** Every array of the boot tables has a reading, but several only name the
  script that reads it and not what the number does there: the CPU profiles' settings and choices, a map unit's
  routine steps' arguments, `RFINIT`'s battle experience table, `REINIT`'s two bits, `DUINIT`'s switch,
  `FAINIT`'s two state bits and its reward's count, `ARINIT`'s tournament number, and `MUINIT`'s list.
  No script reads four arrays (`SCINIT`'s `G[0x217281]`, `COINIT`'s `G[0x31c856]`, `SPINIT`'s two), and the
  tests of `ALINIT`, `SHINIT`, `ARINIT` and `FAINIT` read arrays that no record writes.
- **The five tables without a leader** are shown by the arrays the scripts index, which can lump an array no
  script indexes with the one before it (`RFINIT`'s `G[0x32fb16]` holds such a lump).
- **The area tables' numbers.** `MPINIT`, `ROINIT` and `UNINIT` have exact layouts and a reading for the
  points' names, kinds, positions and targets, the routes' two points, and a unit's card, kind, icon, deck,
  routine, music and pictures; the rows with nothing in their last column, eleven of `MPINIT`'s, nine of
  `ROINIT`'s and ten of `UNINIT`'s, are not read.
- **Copies in saved state** ("Saved state" above) are found by three scans of the scripts, which cannot see a
  copy that passes through another script's local.
- **`SCJUMP`'s seven contexts** ("Scene dispatch" above), and the story cells that are not a scene's played
  cell.

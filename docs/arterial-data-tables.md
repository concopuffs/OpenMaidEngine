# Arterial data tables (`*INIT`)

Canonical home for how 創刻のアテリアル (Arterial, SYS4438) stores its static game data: which `*INIT` scripts
exist, how they are layered, where each table lives in the global bank, and what is known of its fields. Names
for the fields that are understood live in [`vm-map/globals-arterial.toml`](../vm-map/globals-arterial.toml)
(generated → [`global-reference-arterial.md`](global-reference-arterial.md)). Kamidori's equivalent is
[`kamidori-data-tables.md`](kamidori-data-tables.md).

**How far this goes (2026-11-20).** Every table's *layout* is here: its arrays, their strides and what values
they hold, all 278 arrays of the 29 boot tables. What an array *means* is given where the data or a consumer
shows it (names and text, the references between tables, prices, the card panel's numbers); most numeric
fields are not read yet, and their rows say nothing in the last column. "What is not decoded" at the end lists
the gaps.

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
| CCINIT | 26 records with lists of up to 40 entries, among them ids in `SKINIT`'s range | boot |
| CDINIT | cards: 30 slots for each of 200 rows | boot |
| TAINIT | talents: a card's abilities, by name and description | boot |
| COINIT | 奥義: 30 of them, each with nine levels | boot |
| SKINIT | skills (飛行, 転移, 錬金Ⅰ …) | boot |
| ITINIT | items | boot |
| IMINIT | card growth: what each of up to eight level steps adds to four of a card's numbers | boot |
| ILINIT | conditions (操血, 封印, 幻覚 …) | boot |
| RFINIT | five small number tables, copied in bulk | boot |
| AMINIT | areas (歪秤世界, 天慶市 …) | boot |
| DEINIT | decks: a name and up to 20 cards | boot |
| DUINIT | twelve arrays with card rows and ids in `TAINIT`'s range; not read further | boot |
| REINIT | battle rules: special victory conditions and formation limits (by their text) | boot |
| ALINIT | 617 entries, each an item or a card with up to four more items and cards (by `ALCHEMY`'s name, recipes) | boot |
| SHINIT | 232 entries, each an item or a card (by its name, the shop) | boot |
| ARINIT | 73 opponents by name, and ten arenas (対戦台, 連戦台 …) | boot |
| RTINIT | twelve tables of 10 to 150 columns a row; not read further | boot |
| FAINIT | 454 entries of text: a line (『天慶第二学園』へ) and a chapter's heading and lines | boot |
| CVINIT | voice settings: one per voiced character | boot |
| CIINIT | character profiles | boot |
| VIINIT | the glossary (【用語】) | boot |
| TIINIT | a second list of titled entries (日常モード …) | boot |
| EUINIT | two short lists of pairs | boot |
| CGINIT | 1577 entries of two resource ids, in sections and groups (by its name, the CG gallery) | boot |
| SPINIT | 65 pairs of resource ids | boot |
| MUINIT | the music list | boot |
| BGINIT | picture substitution: given a picture id and `G[0x987c]` (0 to 4), another picture id | on demand |
| MPINIT | an area's points: clears 16 arrays of 200 slots and fills them for area `G[0x9883]` | on demand |
| ROINIT | pairs of point numbers for area `G[0x9883]` (read as the routes between its points) | on demand |
| UNINIT | twenty more arrays for area `G[0x9883]`; not read | on demand |

The layouts below give, for each array: its base, the cells it has per id, which columns are written, how many
ids have a value, and the values (for a string array, the first three).

## Scenes

### SCINIT

Leader `G[0x214b71]`, capacity 10000; 1426 scene ids are set, 0 to 8932. Scene 0 is the opening, `SC0000`.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x214b71]` | 1 |  | 1426 | 323..67109455, 235 distinct | the script that plays scene `id`: a packed script id (323 is `SC0000.BIN`; an append's scenes carry its selector in the top byte) |
| `G[0x217281]` | 1 |  | 1426 | 1..7, 7 distinct | a number 1 to 7 beside the script |
| `G[0x219991]` | 1 |  | 4 | 1..3, 3 distinct 1×1, 2×2, 3×1 |  |
| `G[0x21c0a1]` | 1 |  | 662 | 1..1, 1 distinct 1×662 | 1 on 662 scenes. `ALLMAP` reads it for the scene `SCJUMP` picked, into `G[0x36ca]` |

### Scene dispatch (SCINIT and SCJUMP)

A scene is a number, and `G[0x21480a]` holds the one being played. `SCINIT` gives each its script; one script
holds several scenes and branches on the number (`SC0000` on ten values). Each of the 219 base scene scripts
sets `G[0x18880 + scene]` and stores it in the shared profile: the scene has been seen.

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

Most conditions of the base script test the area in play, `G[0x9883]` (1151 times), `G[0x9ba8]` (973) and a bit of
`G[0x9ae0]` (970), with one more cell that differs from rule to rule (`G[0x1198] != 1`, `G[0x119b] != 1` …):
a scene on the map is offered at a place and until something is set. 85 rules test `G[0x5e0 + scene] != 1` for
the scene they name.

Not decoded: what the seven contexts are, what the per-rule cells mean and who sets them, and with that the
game's story flags. `docs/scjump-progression.md` is Himegari's; nothing of it is assumed here.

## Characters

### CHINIT (characters)

Leader `G[0x297db9]`, capacity 1000. 107 characters: 81 in the base data, 4, 8 and 14 from appends 1, 3 and 4.
A character is whoever can speak a page: the scene library sets `G[0x3140]` to the character of the page's
face and reads these arrays for the name plate and the voice (`oms/arterial/adv.oms`).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x297db9]` | 1 |  | 107 | 1..316, 91 distinct | the character whose voice the speaker uses (itself for 91 of the 107); the page's voice block finds the voice setting through it (`oms/arterial/adv.oms`) |
| `G[0x29eaa6]` | 1 |  | 108 | 10..9990, 107 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x29ee8e]` | 1 |  | 107 | 1..3, 3 distinct 1×38, 2×43, 3×26 | 1, 2 or 3. The page's voice block plays a voice only where this is 2 |
| `G[0x29f65e]` | 1 |  | 26 | 1..32, 26 distinct |  |
| `G[0x2a09e6]` | 9 | 0..8 | 16 | 3968..16777975, 143 distinct | resource ids |
| `G[0x2a2d0e]` | 5 | 0..3 | 27 | 21382..16777990, 30 distinct | resource ids |
| `G[0x2a4096]` | 5 | 0..3 | 27 | 19282..67110525, 38 distinct | resource ids |
| `G[0x2a541e]` | 5 | 0..3 | 27 | 19283..67110526, 42 distinct | resource ids |
| `G[0x2a67a6]` | 5 | 0..3 | 27 | 14963..67110527, 42 distinct | resource ids |
| `G[0x2a7b2e]` | 5 | 0..3 | 27 | 14969..67110528, 42 distinct | resource ids |
| `G[0x2a8eb6]` | 5 | 0..3 | 27 | 14970..67110529, 39 distinct | resource ids |
| `G[0x2aa23e]` | 5 | 0..3 | 27 | 19284..67110530, 38 distinct | resource ids |
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
| `G[0x298971]` | 1 |  | 22 | 5804..50332434, 22 distinct |  |
| `G[0x298da9]` † | 6 | 0..5 | 16 | 19606..16778016, 96 distinct | resource ids. Off the grid only because `EUINIT`'s 80 cells lie between it and the table's other arrays |
| `GS[0x58c4]` | 3 | 0..2 | 36 | エキストラ / 仙崎 秀哉 / 仙崎 美來 | three labels (エキストラ; 仙崎 秀哉 …) |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x29c45b]` | 1 |  | 35 | 10..350, 35 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x29c523]` | 1 |  | 35 | 1..82, 35 distinct |  |
| `G[0x29c9d3]` | 1 |  | 35 | 21382..16777990, 35 distinct | resource ids |
| `G[0x29ca9b]` | 1 |  | 35 | 19558..16778000, 2 distinct 19558×32, 16778000×3 | resource ids |
| `GS[0x6864]` | 1 |  | 35 | 仙崎　秀哉 / 仙崎　美來 / 蘇芳　杏里咲 | the profile's name |
| `GS[0x692c]` | 16 | 0..15 | 35 | 仙崎　秀哉　（せんざき しゅうや） / 仙崎　美來　（せんざき みらい） / 蘇芳　杏里咲　（すおう ありさ） | the profile's sixteen lines |

### CCINIT

Leader `G[0x29fa46]` (four cells an id), capacity 100: 26 records with ids from 1 to 82. `CDINIT` writes a
number of the same range for each card row, and rows 1 to 4 have 1 to 4. `G[0x2ae4a7]` holds ids in `SKINIT`'s
range.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x29fa46]` | 4 | 0..3 | 26 | 0..1, 2 distinct 0×23, 1×81 |  |
| `G[0x2ab5c7]` | 40 | 2..35 | 26 | 1..88, 26 distinct |  |
| `G[0x2ac567]` | 40 | 2..35 | 26 | 1..2, 2 distinct 1×684, 2×117 |  |
| `G[0x2ae4a7]` | 160 | 4..131 | 26 | 1..931, 265 distinct |  |
| `G[0x2b2327]` | 40 | 1..35 | 26 | 1..9, 9 distinct |  |

## Cards and what they carry

### CDINIT (cards)

Leader `GS[0x7d7c]`, capacity 6000: 200 rows of 30 slots, and a card is one slot of one row (`row * 30 +
slot`; slots 1 to 25 are used). 923 cards on 76 rows up to row 167: 725 in the base data, 50, 3, 32 and 113
from the four appends. The scripts read these arrays with `lookup-array-2d` at stride 30 (60, 90 or 150 where a
card has 2, 3 or 5 cells). Rows 1 to 4 hold the cards of characters 1 to 4 under several names (仙崎 秀哉 and
勇者しゅうや in row 1); whether every row is one character's is not checked. `G[0x4f30 + card]` is how many of
a card the player holds (`ADDCARD` keeps it within 0 to 99), and `G[0x3ad7]` the player's money (0 to 99999).

The numbers' names are the card panel's own labels. `DRAWCARDINFO` draws a panel from picture `0x3ac8`
(`SO019.AGF`) whose six rows are lettered 種族, スロット, 支配力, コスト, a blank and 攻撃力; over the blank it
draws the sprite `HP`, or `LP` for a leader card (`G[0x43f66c]`), and it writes one array's value on each row.
Beside them are 所持能力1 and 所持能力2, with the names and descriptions of the card's two `TAINIT` ids.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x2b2866]` † | 2 | 0..1 | 19 | 1..82, 32 distinct | **not a card array.** The cells are `G[0x2b32c7 + row]`, one a row, written where the row's first card starts: rows 1 to 4 hold 1 to 4, the characters whose cards those rows hold, and the values go to 82 |
| `G[0x2b338f]` | 1 |  | 198 | 1..4, 4 distinct 1×50, 2×3, 3×32, 4×113 | the append that added the record (1 to 4); the base data leaves it unset |
| `G[0x2b4aff]` | 1 |  | 923 | 10..60400, 923 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x2b626f]` | 1 |  | 300 | 1..1, 1 distinct 1×300 |  |
| `G[0x2b79df]` | 1 |  | 923 | 1..5, 5 distinct 1×250, 2×254, 3×220, 4×108, 5×91 | rarity, 1 to 5: the card panel draws one of five marks for it (`DRAWCARDINFO`) |
| `G[0x2b914f]` | 1 |  | 903 | 1..1, 1 distinct 1×903 |  |
| `G[0x2ba8bf]` | 1 |  | 229 | 1..1, 1 distinct 1×229 |  |
| `G[0x2bc02f]` | 1 |  | 923 | 1..72, 21 distinct | 種族: one bit a race, bit 0 to 6 = 無機, 人間, 怪物, 天使, 悪魔, 魔神, 英雄 (the names are `GS[0x12a7c + bit]`, set by `INIT2`) |
| `G[0x2bd79f]` | 1 |  | 919 | 1..8, 8 distinct | コスト, 1 to 8 |
| `G[0x2bef0f]` | 1 |  | 857 | 1..3, 3 distinct 1×344, 2×496, 3×17 | 支配力, 1 to 3 |
| `G[0x2c067f]` | 1 |  | 889 | 1..28, 26 distinct | 攻撃力, 1 to 28 |
| `G[0x2c1def]` | 1 |  | 338 | 8..400, 135 distinct | ＬＰ, 8 to 400: a leader card's, in the place of ＨＰ; 338 cards have it |
| `G[0x2c355f]` | 1 |  | 585 | 1..70, 26 distinct | ＨＰ, 1 to 70; 585 cards have it |
| `G[0x2c4ccf]` | 1 |  | 461 | 1..3, 3 distinct 1×317, 2×111, 3×33 | スロット, 1 to 3; grows by level, to 3 at most |
| `G[0x2c643f]` | 2 | 0..1 | 898 | 1..957, 393 distinct | two ids in `TAINIT`'s range: the card panel prints their names and descriptions under 所持能力1 and 所持能力2 |
| `G[0x2c931f]` | 2 | 0..1 | 311 | 4..35000, 123 distinct | two prices, the second a fifth of the first. The second is the selling price: `ADDCARD` sells what the player holds over 99 at it |
| `G[0x2cc1ff]` | 1 |  | 226 | 1..1, 1 distinct 1×226 |  |
| `G[0x2cd96f]` | 2 | 0..1 | 923 | 15422..67110368, 1844 distinct | two pictures; column 0 is the one the card panel draws |
| `G[0x2d084f]` | 2 | 0..1 | 474 | 14903..67110360, 302 distinct | resource ids |
| `G[0x2d372f]` | 2 | 0..1 | 923 | 14924..67110369, 835 distinct | resource ids |
| `G[0x2d660f]` | 1 |  | 913 | 14905..67109855, 3 distinct 14905×808, 33554482×3, 67109855×102 | resource ids |
| `G[0x2d7d7f]` | 1 |  | 923 | 14904..21285, 39 distinct | resource ids |
| `G[0x2d94ef]` | 5 | 0..4 | 389 | 3967..67110366, 808 distinct | resource ids |
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
| `G[0x31b788]` | 1 |  | 395 | 10..9600, 395 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x31bb70]` | 1 |  | 395 | 1..1, 1 distinct 1×395 | 1 for every record |
| `G[0x31bf58]` | 1 |  | 395 | 1..9, 9 distinct | a kind, 1 to 9 |
| `GS[0xdb3c]` | 1 |  | 395 | 悪魔の上下関係 / 無尽蔵 / 二回移動 | the talent's name |
| `GS[0xdf24]` | 1 |  | 395 | 特殊：『悪魔』の存在するマスに、上書 / 特殊：このカードは編成時に何枚でも入 / 特殊：ターン開始時、このカードに《再 | its description |

`COINIT`: leader `GS[0xe30c]`, capacity 100, 30 奥義 (27 base, 3 from append 1). Every numeric array has ten
cells an id, columns 1 to 9 for the nine levels.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x31c72a]` | 1 |  | 3 | 1..1, 1 distinct 1×3 | the append that added the record (1 to 4); the base data leaves it unset |
| `G[0x31c78e]` | 1 |  | 30 | 10..320, 30 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x31c7f2]` | 1 |  | 30 | 1..1, 1 distinct 1×30 | 1 for every record |
| `G[0x31c856]` | 1 |  | 26 | 1..4, 4 distinct 1×20, 2×1, 3×3, 4×2 |  |
| `G[0x31c8ba]` | 10 | 1..9 | 30 | 3..25, 10 distinct |  |
| `G[0x31cca2]` | 10 | 1..9 | 28 | -9..100, 42 distinct |  |
| `G[0x31d08a]` | 10 | 1..9 | 26 | 180..810, 24 distinct |  |
| `G[0x31d472]` | 10 | 1..9 | 26 | 100..320, 15 distinct |  |
| `G[0x31d85a]` | 10 | 1..9 | 26 | 19563..16778004, 27 distinct | resource ids |
| `G[0x31dc42]` | 10 | 1..9 | 16 | 19564..16778001, 16 distinct | resource ids |
| `G[0x31e02a]` | 10 | 1..9 | 30 | 14936..19575, 7 distinct | resource ids |
| `GS[0xe30c]` | 1 |  | 30 | 操血の鎌撃 / 操血の錬成者 / グラビティダウン | the 奥義's name |
| `GS[0xe370]` | 30 | 3..29 | 30 | 【奥義】　操血の鎌撃　ＬＶ１ / 【奥義】　操血の錬成者　ＬＶ１ / 【奥義】　グラビティダウン　ＬＶ１ | three lines of text for each level: column 3 × level + line, levels 1 to 9 |

`SKINIT`: leader `GS[0xef28]`, capacity 1000, 265 skills (223 base, 42 from append 1).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x31e412]` | 1 |  | 265 | 10..9210, 265 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x31e7fa]` | 1 |  | 265 | 1..1, 1 distinct 1×265 | 1 for every record |
| `G[0x31ebe2]` | 1 |  | 265 | 1..3, 3 distinct 1×132, 2×113, 3×20 | a kind, 1 to 3 |
| `G[0x31efca]` | 1 |  | 133 | -6..300, 24 distinct |  |
| `GS[0xef28]` | 1 |  | 265 | 飛行 / 転移 / 錬金Ⅰ | the skill's name |
| `GS[0xf310]` | 3 | 0..2 | 265 | 【移動】　飛行 / 【移動】　転移 / 【合成】　錬金Ⅰ | three lines of text; the first is the heading (`【移動】　飛行`) |

`ILINIT`: leader `GS[0x11e08]`, capacity 32, 19 conditions.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x32f96a]` | 1 |  | 19 | 1..2, 2 distinct 1×11, 2×8 |  |
| `G[0x32f9a0]` † | 3 | 0 | 1 | -1..-1, 1 distinct -1×1 | one cell that one record writes; the stride is a guess (the table's arrays are 32 cells each) |
| `G[0x32f9ca]` | 1 |  | 5 | -1..4, 5 distinct -1×1, 1×1, 2×1, 3×1, 4×1 |  |
| `G[0x32fa06]` † | 5 | 0 | 1 | -1..-1, 1 distinct -1×1 | one cell that one record writes; the stride is a guess |
| `G[0x32fa2a]` | 1 |  | 2 | -1..1, 2 distinct -1×1, 1×1 |  |
| `G[0x32fa6a]` | 1 |  | 19 | 1..19, 19 distinct | a number 1 to 19, different for each condition |
| `GS[0x11e08]` | 1 |  | 19 | 操血 / 封印 / 幻覚 | the condition's name (操血, 封印, 幻覚 …) |

### ITINIT (items)

Leader `GS[0xfec8]`, capacity 2000, 492 items (399 base; 33, 1, 21, 38 from the appends). `G[0x3f90 + item]` is
how many the player holds (`ADDITEM` keeps it within 0 to 99).

A card of the deck can carry an item, and ten arrays say which card may: `INIT2` takes the item off a card
that fails one of them (a sub of it checks each against the card's numbers from `CALCARM`).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x31f79a]` | 1 |  | 93 | 1..4, 4 distinct 1×33, 2×1, 3×21, 4×38 | the append that added the record (1 to 4); the base data leaves it unset |
| `G[0x31ff6a]` | 1 |  | 492 | 10..9670, 492 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x32073a]` | 1 |  | 492 | 1..5, 5 distinct 1×62, 2×31, 3×61, 4×60, 5×278 | a kind, 1 to 5 |
| `G[0x320f0a]` | 1 |  | 59 | 1..1, 1 distinct 1×59 |  |
| `G[0x3216da]` | 1 |  | 492 | 1..5, 5 distinct 1×118, 2×97, 3×125, 4×94, 5×58 | 1 to 5; the card panel draws it with the marks it draws a card's rarity with |
| `G[0x321eaa]` | 1 |  | 492 | 1..1, 1 distinct 1×492 | 1 for every record |
| `G[0x32267a]` | 1 |  | 278 | 1..3, 3 distinct 1×126, 2×106, 3×46 | the least スロット a card may have to carry the item |
| `G[0x322e4a]` | 1 |  | 11 | -1..1, 2 distinct -1×5, 1×6 |  |
| `G[0x32361a]` | 1 |  | 16 | -2..1, 3 distinct -2×1, -1×10, 1×5 |  |
| `G[0x323dea]` | 1 |  | 142 | -12..10, 14 distinct |  |
| `G[0x3245ba]` | 1 |  | 20 | 8..150, 13 distinct |  |
| `G[0x324d8a]` | 1 |  | 150 | -5..30, 18 distinct |  |
| `G[0x32555a]` | 1 |  | 8 | 16..24, 8 distinct |  |
| `G[0x325d2a]` | 1 |  | 31 | 1..4, 3 distinct 1×22, 3×1, 4×8 |  |
| `G[0x3264fa]` | 1 |  | 174 | 3..930, 91 distinct |  |
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
| `G[0x32d25a]` | 1 |  | 31 | 1..3, 3 distinct 1×22, 2×6, 3×3 |  |
| `G[0x32da2a]` | 1 |  | 31 | 1..1, 1 distinct 1×31 |  |
| `G[0x32e1fa]` | 3 | 0..2 | 31 | 14948..19849, 13 distinct | resource ids |
| `GS[0xfec8]` | 1 |  | 492 | 美來のハンカチ / 美來のカバン / ＮＴＤカード３Ｆ | the item's name |
| `GS[0x10698]` | 3 | 0..2 | 492 | 【重要】美來のハンカチ / 【重要】美來のカバン / 【重要】ＮＴＤカード３Ｆ | three lines of text; the first is the heading (`【重要】美來のハンカチ`) |

### DEINIT (decks), ALINIT (recipes), SHINIT (shop), REINIT (battle rules)

`DEINIT`: leader `GS[0x1201c]`, capacity 1000, 573 decks.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x334856]` | 30 | 1..20 | 558 | 1..167, 56 distinct | the cards, slot 1 to 20: each card's row in `CDINIT` (1 to 167) |
| `G[0x33bd86]` | 30 | 1..20 | 558 | 1..23, 23 distinct | and its slot in that row (1 to 23): the two name one card |
| `G[0x3432b6]` | 30 | 1..20 | 558 | 1..30, 13 distinct | a number per card slot, 1 to 30 |
| `G[0x34a7e6]` | 30 | 1..16 | 534 | 1..6, 5 distinct 1×1327, 2×115, 3×15, 4×5, 6×1 |  |
| `G[0x351d16]` | 30 | 1..9 | 65 | 10..90, 13 distinct |  |
| `G[0x367ca6]` | 6 | 0..5 | 573 | -1..13, 15 distinct |  |
| `G[0x369416]` | 1 |  | 547 | 1..59, 51 distinct |  |
| `G[0x3697fe]` | 4 | 0..3 | 454 | 1..200, 42 distinct |  |
| `G[0x36a79e]` | 4 | 0..3 | 388 | 34..868, 150 distinct |  |
| `G[0x36b73e]` | 4 | 0..1 | 69 | 50..165, 15 distinct |  |
| `G[0x36c6de]` | 4 | 0..1 | 69 | 2..22, 15 distinct |  |
| `GS[0x1201c]` | 1 |  | 573 | 初期デッキ / 対新聞部員 / 第一新聞部員？ | the deck's name |

`ALINIT`: capacity 2000, 617 entries. An entry starts at `G[0x372ba6]` (an item id, 185 entries) or at
`G[0x373376]` (a card's row, the other 432). Which of an entry's ids it makes and which it takes is not read.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x372ba6]` | 1 |  | 185 | 27..952, 185 distinct | an item id (`ITINIT`'s range) on 185 entries |
| `G[0x373376]` | 1 |  | 432 | 1..167, 60 distinct | a card's row in `CDINIT` (1 to 167) on the other 432 |
| `G[0x373b46]` | 1 |  | 432 | 1..25, 25 distinct | and its slot (1 to 25) |
| `G[0x374316]` | 1 |  | 617 | 5..25, 5 distinct 5×210, 10×129, 15×139, 20×92, 25×47 |  |
| `G[0x374ae6]` | 1 |  | 617 | 2..2, 1 distinct 2×617 |  |
| `G[0x3752b6]` | 1 |  | 2 | 6..6, 1 distinct 6×2 |  |
| `G[0x3771f6]` | 1 |  | 183 | 7..55, 18 distinct |  |
| `G[0x3779c6]` | 1 |  | 412 | 1..82, 26 distinct |  |
| `G[0x378196]` | 1 |  | 244 | 1..35, 13 distinct |  |
| `G[0x378966]` | 1 |  | 468 | 3..427, 39 distinct |  |
| `G[0x379136]` | 1 |  | 43 | 105..952, 35 distinct |  |
| `G[0x379906]` | 1 |  | 261 | 1..167, 37 distinct | a card's row |
| `G[0x37a0d6]` | 1 |  | 261 | 1..24, 18 distinct | and its slot |
| `G[0x37a8a6]` | 5 | 0..1 | 324 | 77..9899, 28 distinct |  |
| `G[0x37f6c6]` | 4 | 0..3 | 599 | 101..952, 231 distinct | up to four ids in `ITINIT`'s range |
| `G[0x381606]` | 4 | 0..3 | 358 | 1..167, 58 distinct | up to four cards' rows |
| `G[0x383546]` | 4 | 0..3 | 358 | 1..24, 23 distinct | and their slots |
| `G[0x385486]` | 4 | 0..3 | 617 | 1..40, 11 distinct |  |

`SHINIT`: capacity 1000, 232 entries. An entry starts at `G[0x3873c6]` (an item id, 135 entries) or at
`G[0x3877ae]` (a card's row, the other 97).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x3873c6]` | 1 |  | 135 | 13..950, 128 distinct | an item id (`ITINIT`'s range) on 135 entries |
| `G[0x3877ae]` | 1 |  | 97 | 11..167, 12 distinct | a card's row in `CDINIT` on the other 97 |
| `G[0x387b96]` | 1 |  | 97 | 1..21, 21 distinct | and its slot |
| `G[0x387f7e]` | 1 |  | 232 | 2..32, 5 distinct 2×81, 4×34, 8×75, 10×7, 32×35 |  |
| `G[0x388366]` | 1 |  | 57 | 2..7, 6 distinct 2×2, 3×2, 4×9, 5×13, 6×16, 7×15 |  |
| `G[0x389306]` | 1 |  | 10 | 10..40, 4 distinct 10×2, 20×2, 25×3, 40×3 |  |
| `G[0x3896ee]` | 1 |  | 125 | 1..13, 12 distinct |  |
| `G[0x389ad6]` | 1 |  | 7 | 3..7, 3 distinct 3×3, 5×1, 7×3 |  |
| `G[0x389ebe]` | 1 |  | 126 | 18..107, 49 distinct |  |
| `G[0x38a2a6]` | 2 | 0..1 | 71 | 63..9822, 21 distinct |  |
| `G[0x38aa76]` | 2 | 0..1 | 6 | 3124..9069, 8 distinct |  |

`REINIT`: leader `G[0x3718e6]` (five cells an id), capacity 100, 76 rules.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x3718e6]` | 5 | 0..4 | 76 | 1..2, 2 distinct 1×92, 2×155 |  |
| `G[0x371ada]` | 1 |  | 51 | 1..1, 1 distinct 1×51 |  |
| `G[0x371ec2]` | 1 |  | 76 | 2..99, 12 distinct |  |
| `G[0x371f26]` | 1 |  | 76 | 4..6, 2 distinct 4×1, 6×75 |  |
| `G[0x371f8a]` | 1 |  | 76 | 15..30, 3 distinct 15×1, 20×4, 30×71 |  |
| `G[0x371fee]` | 1 |  | 41 | 3..3, 1 distinct 3×41 |  |
| `G[0x3723d6]` | 2 | 1 | 6 | 2..5, 4 distinct 2×1, 3×1, 4×3, 5×1 |  |
| `G[0x37249e]` | 2 | 0..1 | 3 | 1..1, 1 distinct 1×3 |  |
| `G[0x372566]` | 2 | 0..1 | 5 | 3..10, 5 distinct 3×2, 5×1, 8×1, 9×1, 10×2 |  |
| `G[0x37262e]` | 2 | 0..1 | 3 | 3..13, 5 distinct 3×1, 4×1, 9×1, 10×1, 13×1 |  |
| `G[0x3726f6]` | 2 | 0..1 | 15 | 2..126, 11 distinct |  |
| `G[0x3727be]` | 10 | 0..7 | 13 | 1..153, 18 distinct |  |
| `GS[0x12404]` | 6 | 0..5 | 76 | ◆特殊勝敗条件・編成制限◆ / ◆特殊勝敗条件・編成制限◆ / ◆特殊勝敗条件・編成制限◆ | six lines of text; the first is the heading ◆特殊勝敗条件・編成制限◆ |

### RFINIT, DUINIT, RTINIT

No leaders; by the arrays the scripts index.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x32fa8a]` | 10 | 0..8 | 4 | 1..2000, 21 distinct |  |
| `G[0x32fab2]` | 10 | 0..9 | 10 | 0..99999, 99 distinct |  |
| `G[0x32fb16]` | 10 | 0..9 | 7 | 0..9999, 35 distinct |  |
| `G[0x32fb57]` | 10 | 0..7 | 8 | 0..1, 2 distinct 0×27, 1×37 |  |
| `G[0x32fbbb]` | 1 |  | 8 | 17..25, 8 distinct |  |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x36d67e]` | 1 |  | 208 | 1..1, 1 distinct 1×208 |  |
| `G[0x36da66]` | 4 | 0..3 | 257 | 1..167, 65 distinct |  |
| `G[0x36ea06]` | 2 | 0..1 | 257 | 2..957, 280 distinct |  |
| `G[0x36f1d6]` | 1 |  | 110 | 1..27, 23 distinct |  |
| `G[0x36f5be]` | 1 |  | 110 | 1..9, 9 distinct |  |
| `G[0x36f9a6]` | 1 |  | 47 | 19583..19704, 21 distinct |  |
| `G[0x36fd8e]` | 1 |  | 47 | 180..810, 14 distinct |  |
| `G[0x370176]` | 1 |  | 47 | 100..320, 10 distinct |  |
| `G[0x37055e]` | 1 |  | 135 | 1..86, 75 distinct |  |
| `G[0x370946]` | 1 |  | 257 | 8..42, 12 distinct |  |
| `G[0x370d2e]` | 1 |  | 90 | 1..1, 1 distinct 1×90 |  |
| `G[0x371116]` | 2 | 0..1 | 257 | 14897..67110532, 56 distinct |  |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x391fa9]` | 50 | 0..16 | 167 | 1..8, 8 distinct |  |
| `G[0x39e2f9]` | 50 | 0..16 | 167 | 10..400, 11 distinct |  |
| `G[0x3aa649]` | 50 | 0..16 | 167 | 1..128, 92 distinct |  |
| `G[0x3b6999]` | 50 | 0..15 | 162 | 1..99, 8 distinct |  |
| `G[0x3c2ce9]` | 50 | 0..15 | 125 | 1..9, 9 distinct |  |
| `G[0x3e76d9]` | 150 | 0..46 | 24 | 2801..10000, 27 distinct |  |
| `G[0x40c0c9]` | 150 | 0..27 | 15 | 3159..9760, 18 distinct |  |
| `G[0x430ab9]` | 20 | 0..11 | 53 | -30..15, 13 distinct |  |
| `G[0x4358d9]` | 10 | 0..5 | 53 | 1..4, 4 distinct 1×53, 2×44, 3×5, 4×1 |  |
| `G[0x437fe9]` | 10 | 0..3 | 54 | 1..2, 2 distinct 1×54, 2×27 |  |
| `G[0x43a6f9]` | 10 | 0..4 | 53 | 1..2, 2 distinct 1×53, 2×40 |  |
| `G[0x43ce09]` | 10 | 0..5 | 46 | 1..2, 2 distinct 1×66, 2×22 |  |

## World and progression

### AMINIT (areas)

Leader `G[0x32fbc5]`, capacity 300, 150 areas.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x32fbc5]` | 1 |  | 150 | 1..197, 51 distinct |  |
| `G[0x32fcf1]` | 6 | 0..5 | 150 | 4093..67109805, 288 distinct | resource ids |
| `G[0x3303f9]` | 2 | 0..1 | 150 | 2..50, 16 distinct |  |
| `G[0x330651]` | 2 | 0..1 | 150 | 40..70, 4 distinct 40×2, 50×230, 60×2, 70×66 |  |
| `G[0x3308a9]` | 2 | 0..1 | 150 | 14897..67109803, 54 distinct | resource ids |
| `G[0x330b01]` | 1 |  | 16 | 33..72, 2 distinct 33×15, 72×1 |  |
| `GS[0x11e28]` | 1 |  | 150 | 歪秤世界 / 天慶市 / 天慶第二学園 | the area's name (歪秤世界, 天慶市, 天慶第二学園 …) |

### ARINIT (arenas and opponents) and FAINIT

`ARINIT`: leader `GS[0x1268e]`, capacity 1000, 73 opponents. The script also fills the arenas' own arrays,
which hold ten.

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x38b246]` | 1 |  | 73 | 801..965, 60 distinct |  |
| `G[0x38b62e]` | 1 |  | 73 | 1..9, 9 distinct | a number 1 to 9; nine arenas have a name |
| `G[0x38ba16]` | 1 |  | 73 | 10..270, 22 distinct |  |
| `G[0x38bdfe]` | 1 |  | 54 | 12..913, 50 distinct |  |
| `G[0x38c1e6]` | 1 |  | 5 | 23..153, 2 distinct 23×3, 153×2 |  |
| `G[0x38c5ce]` | 1 |  | 5 | 2..8, 4 distinct 2×1, 4×1, 6×2, 8×1 |  |
| `G[0x38c9b6]` | 1 |  | 59 | 1..3, 3 distinct 1×57, 2×1, 3×1 |  |
| `G[0x38d186]` | 3 | 0..2 | 42 | 511..8932, 34 distinct |  |
| `G[0x38dd3e]` | 1 |  | 73 | 2..8, 3 distinct 2×20, 4×24, 8×29 |  |
| `G[0x38e126]` | 1 |  | 21 | 1..426, 17 distinct |  |
| `G[0x38ecde]` | 1 |  | 7 | 1..1, 1 distinct 1×7 |  |
| `G[0x39044e]` | 2 | 0..1 | 27 | 511..9899, 10 distinct |  |
| `G[0x390c1e]` | 2 | 0 | 2 | 3122..3122, 1 distinct 3122×2 |  |
| `G[0x3913ee]` | 1 |  | 21 | 2..8, 7 distinct |  |
| `G[0x391bbe]` | 1 |  | 21 | 50..100, 2 distinct 50×12, 100×9 |  |
| `GS[0x12429]` | 1 |  | 35 | 対戦台 / 連戦台 / 大会戦 | **not an opponent array.** The arenas' own strings, ten arenas: `GS[0x1265c + arena]` is an arena's name (対戦台, 連戦台, 大会戦 …) and `GS[0x12666 + 4 * arena + line]` its four lines of description |
| `GS[0x1268e]` | 1 |  | 73 | リュカアース / ゲオテット / 歪秤の鳥娘 | the opponent's name |

`FAINIT`: leader `GS[0x29e4]`, capacity 1000, 454 entries (370 in the base data; 32, 12 and 40 from appends 1,
3 and 4).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x260701]` | 1 |  | 454 | 9010..25270, 453 distinct |  |
| `G[0x260ed1]` | 3 | 0..2 | 454 | 12..9900, 374 distinct |  |
| `G[0x261a89]` | 3 | 0..1 | 90 | 7005..9900, 12 distinct |  |
| `G[0x262641]` | 3 | 0..1 | 450 | 13..9899, 385 distinct |  |
| `G[0x2631f9]` | 3 | 0 | 5 | 9524..9751, 5 distinct 9524×1, 9525×1, 9634×1, 9654×1, 9751×1 |  |
| `G[0x263db1]` | 3 | 0 | 97 | 101..9658, 20 distinct |  |
| `G[0x264969]` | 3 | 0 | 71 | 13..7090, 46 distinct |  |
| `G[0x265521]` | 1 |  | 454 | 1..3, 3 distinct 1×228, 2×71, 3×155 |  |
| `G[0x265909]` | 1 |  | 60 | 105..953, 56 distinct |  |
| `G[0x265cf1]` | 1 |  | 11 | 31..39, 3 distinct 31×4, 38×5, 39×2 |  |
| `G[0x2660d9]` | 1 |  | 11 | 1..10, 9 distinct |  |
| `G[0x2664c1]` | 1 |  | 71 | 1..1, 1 distinct 1×71 |  |
| `G[0x2668a9]` | 1 |  | 38 | 20..200, 6 distinct 20×3, 40×1, 50×12, 60×1, 100×7, 200×14 |  |
| `GS[0x29e4]` | 1 |  | 454 | 『天慶第二学園』へ / 『２年教室』へ / 『合同教室』へ | a line of text (『天慶第二学園』へ) |
| `GS[0x2dcc]` | 1 |  | 454 | 『天慶第二学園』へ / 『２年教室』へ / 『合同教室』へ | a second line, the same as the first on 449 of the 454 entries |
| `GS[0x31b4]` | 4 | 0..3 | 454 | 【共通ルート】　序章 / 【共通ルート】　序章 / 【共通ルート】　序章 | four lines of text; the first is a chapter's heading (【共通ルート】　序章) |

## Registries

### VIINIT, TIINIT, CGINIT, SPINIT, MUINIT, EUINIT

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x29a51a]` | 1 |  | 193 | 10..4690, 193 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x29a902]` | 3 | 0 | 193 | 1..8487, 89 distinct |  |
| `G[0x29b4ba]` | 1 |  | 193 | 570..570, 1 distinct 570×193 |  |
| `G[0x29b8a2]` | 1 |  | 193 | 130..130, 1 distinct 130×193 |  |
| `G[0x29bc8a]` | 1 |  | 193 | 20964..21048, 85 distinct | resource ids |
| `G[0x29c072]` | 1 |  | 193 | 20963..16778103, 2 distinct 20963×192, 16778103×1 | resource ids |
| `GS[0x647c]` | 1 |  | 193 | 【用語】ＭＨＩ / 【用語】最先端医療 / 【用語】八葉 | the glossary entry's title (【用語】ＭＨＩ) |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x29cb64]` | 1 |  | 75 | 10..750, 75 distinct | a sort number: unique to each record, in steps of ten through the base data |
| `G[0x29cf4c]` | 3 | 0 | 75 | 1..55, 19 distinct |  |
| `G[0x29db04]` | 1 |  | 75 | 570..570, 1 distinct 570×75 |  |
| `G[0x29deec]` | 1 |  | 75 | 130..130, 1 distinct 130×75 |  |
| `G[0x29e2d4]` | 1 |  | 75 | 21012..21493, 74 distinct | resource ids |
| `G[0x29e6bc]` | 1 |  | 75 | 20615..20615, 1 distinct 20615×75 | resource ids |
| `GS[0x75ac]` | 1 |  | 75 | 日常モード / 拠点アイコン・サムネイル / プレイヤーアイコン | the entry's title (日常モード, 拠点アイコン・サムネイル …) |

`CGINIT`: leader `G[0x2283f1]` (two cells an id), capacity 3000, 1577 entries. An entry carries three numbers:
a section (1 to 9), a group in it (1 to 57), and its place in the group (1 to 35). 224 groups have a picture
of their own (191 in the base data).

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x2283f1]` | 2 | 0..1 | 1577 | 94..67110458, 1714 distinct | two resource ids per entry |
| `G[0x229b61]` | 1 |  | 1577 | 1..9, 9 distinct | the entry's section, 1 to 9 |
| `G[0x22a719]` | 1 |  | 1577 | 1..57, 57 distinct | its group in the section, 1 to 57 |
| `G[0x22b2d1]` | 1 |  | 1577 | 1..35, 35 distinct | its place in the group, 1 to 35 |
| `G[0x22d301]` † | 1 |  | 224 | 18968..67110457, 224 distinct | **not indexed by entry.** The cells are `G[0x22d5f9 + 100 * section + group]`, one resource id a group, written with the group's first entry |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x25fade]` | 1 |  | 65 | 654..67109429, 65 distinct | resource ids |
| `G[0x25fec6]` | 1 |  | 65 | 20555..67110626, 65 distinct | resource ids |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x260667]` | 1 |  | 34 | 1..20554, 33 distinct | a resource id |
| `G[0x260699]` | 1 |  | 34 | 2..41, 34 distinct | a number, 2 to 41 |
| `GS[0x29b2]` | 1 |  | 34 | ｂｒａｖｅ ｇｅｎｅｓｉｓ　FULL / ｂｒａｖｅ ｇｅｎｅｓｉｓ　SHOR / ｔｒｉｎｉｔｙ ｎｉｇｈｔ | the track's title |

| array | cells per id | columns written | ids | values | holds |
|---|---|---|---|---|---|
| `G[0x298d59]` | 2 | 0..1 | 9 | 278..67110534, 18 distinct |  |
| `G[0x298d81]` | 2 | 0..1 | 8 | 278..67110534, 16 distinct |  |

## On-demand tables

These four are not in the boot image: a game script sets a selector and runs them.

- **BGINIT** maps a picture: for a picture id in `G[0x3fb90]` and a variant in `G[0x987c]` (0 to 4) it leaves
  another picture id in `G[0x3fb90]`.
- **MPINIT**, **ROINIT** and **UNINIT** load one area (`AMINIT`) each time: they clear their arrays (200 slots,
  with 1 to 7 cells a slot) and fill them from the branch for `G[0x9883]`, the area in play. (`MPINIT` has a
  branch for each of `AMINIT`'s 150 ids and for nothing else, and scripts index `AMINIT`'s arrays with the
  cell.) `MPINIT` writes the area's points with their names (出現地点, 西蒼川商業区). `ROINIT` writes pairs of
  point numbers (1 and 2, 1 and 3 …), read as the routes between points. `UNINIT` fills twenty arrays that are
  not read. No field of the three has a layout here.

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
its experience (`G[0x7e10 + card]`), and of an item how many the player holds (`G[0x3f90 + item]`). So a changed スロット, 支配力, コスト, ＨＰ, ＬＰ, 攻撃力 or
growth reaches the cards of an old save. No other field is checked: until one is, a changed value may not reach what an old save
already holds.

## What is not decoded

- **Most numeric fields.** A row with nothing in its last column has a layout and no meaning yet. That is
  half of `ITINIT`'s 31 arrays, `CDINIT`'s kinds, `COINIT`'s per-level numbers, and all of `DUINIT`, `RTINIT`,
  `RFINIT`, `CCINIT`, `FAINIT`, `SHINIT` and `REINIT`'s numbers.
- **The five tables without a leader** are shown by the arrays the scripts index, which can lump an array no
  script indexes with the one before it.
- **`ILINIT`'s sparse cells.** Its arrays are 32 cells each, from `G[0x32f96a]` on; four cells that one record
  each writes are shown as two `†` rows and two cells outside every array.
- **The four on-demand tables** have no field layout here.
- **Who reads the sort numbers** is not found: no script reads those arrays through a lookup.
- **Copies in saved state** ("Saved state" above) are checked for a card's numbers only.
- **`SCJUMP`'s contexts and per-rule cells** ("Scene dispatch" above), and with them the story flags.

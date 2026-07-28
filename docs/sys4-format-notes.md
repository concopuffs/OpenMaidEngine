# SYS4 Binary Format Notes (hex-first)

The script sections were derived initially from byte-pattern analysis of the 481 DATA1 `.BIN`
files, before Ghidra work on `AGE.EXE`; their probe scripts live in `tools/probe_*.py`. The
persistence sections combine native-code reconstruction, shipped-script consumers, installed
read-only files, and port codec round trips. Confidence and remaining semantic limits are stated
per format.

> **UPDATE 2026-07-05 — opcode set solved via Kelebek1's table.** Everything below
> under "Instruction stream — PARTIAL" is now resolved: code = instructions of
> `<opcode:u32> + argc*(<argtype:u32><value:u32>)`, length `1+2*argc`; inline strings
> live after code inside `[0,F8)`, so stop decoding at the first type-2/`0x64` arg
> offset. 476/476 scripts decode clean (0 unknown opcodes). Header fields F0–F5 are
> **local-variable counts** (F0=local_integer_1, F1=local_floats, F2=local_strings_1,
> F3=local_integer_2, F4=unknown, F5=local_strings_2). See `vm-mapping-plan.md` and
> `vm-map/opcodes.toml`. The tag values below (0x71/0x03/0x8F etc.) are the
> *opcodes at table targets*: 0x8F=`call`, 0x03=`call-script`, 0x71=`u0041A7B0`.

## SYS4INI directory and per-game settings trailer — CONFIRMED

`SYS4INI.BIN` is not only the asset catalog. Its `S4IC` LZSS stream begins with the archive names and
80-byte file records documented in `asset-resolution-re.md`, then continues with VM-bank metadata and a
NUL-delimited per-game engine-settings trailer. `tools/parse_sys4ini.py` currently consumes only the
directory prefix.

After the directory prefix, Himegari's decompressed stream has this tail:

```text
decompressed offset  field
0x102488             vm_metadata_bytes:u32 = 0x90
0x10248c             vm_metadata[0x90]
0x10251c             settings_string_bytes:u32 = 0x2bb
0x102520             setting_pair_count:u32 = 36
0x102524             repeated { key:cstring, value:cstring }[36]
0x1027df             end of decompressed stream
```

The string-byte count covers only the concatenated key/value strings, not the preceding pair count.
Keys are ASCII, values are CP932, and every key and value is individually NUL-terminated. Native
`engine_settings_import_sys4ini_pairs@0x46da80` matches keys case-insensitively and rejects an
unrecognized key rather than silently retaining it.

Himegari's complete serialized list, in file order, is:

| # | Key | Value |
|---:|---|---|
| 1 | `CREATEOBJECT` | `2` |
| 2 | `DRAWMODE` | `1` |
| 3 | `DEPENDMOVIESOUND` | `1` |
| 4 | `SCREENX` | `800` |
| 5 | `SCREENY` | `600` |
| 6 | `FONT` | `ＭＳ 明朝` |
| 7 | `ENABLEANTIFONT` | `1` |
| 8 | `ANTIFONTVERSION` | `3` |
| 9 | `FULLSCREENBIT` | `32` |
| 10 | `SCREENWARNING` | `1` |
| 11 | `NOSETMUSIC` | `3` |
| 12 | `MENU_SAVE` | `0` |
| 13 | `MENU_MESWINA` | `0` |
| 14 | `MENU_RCLICK` | `0` |
| 15 | `MENU_MESSPEED` | `0` |
| 16 | `MENU_USEANTIFONT` | `0` |
| 17 | `MENU_MESSAGE` | `1` |
| 18 | `MENU_SOUNDONOFF` | `0` |
| 19 | `ENABLEMEMFLIP` | `0` |
| 20 | `CLICKONUP` | `1` |
| 21 | `ALWAYSBACKUPSURFACE` | `0` |
| 22 | `CANCELMESSKIPONCLICK` | `2` |
| 23 | `CONTROLDISIBLECURSOR` | `1` |
| 24 | `COEXISTMESSKIP` | `1` |
| 25 | `REDRAWTEXTONKEY` | `0` |
| 26 | `WHEELKEYUP` | `8` |
| 27 | `WHEELKEYDOWN` | `9` |
| 28 | `USEAPPDATAFOLDER` | `1` |
| 29 | `SAVEPATH` | `Eushully\姫狩りダンジョンマイスター\SAVE` |
| 30 | `REGFILEPATH` | `Eushully\姫狩りダンジョンマイスター` |
| 31 | `COPYRIGHT` | `Copyright (c) Eushully 2009` |
| 32 | `SAVEVERSION` | `310` |
| 33 | `GAMEVERSION` | `1.00` |
| 34 | `RCVERSION` | `23` |
| 35 | `VERREGPOS` | `70724B91-F673-40FB-9EC4-5DE21EA68367` |
| 36 | `REGKEY` | `1886538641` |

This list is the canonical serialized-data inventory. Native consumers, compiled fallbacks, settings
that are inert under Himegari's selected renderer, and current port coverage are cataloged in
`engine-re.md` under "SYS4INI startup-settings catalog."

`SCREENX=800` and `SCREENY=600` are independently corroborated by Kamidori's `1024x576` pair, proving
that the authored logical canvas is per-game data. Fullscreen/display-mode settings are separate from
these logical dimensions. `USEAPPDATAFOLDER` and `SAVEPATH` select the native Windows save root; fixed
save filenames remain AGE policy rather than trailer or script strings.

## Native persistence files — Himegari 3.10

This section is the canonical binary-format specification for the persistence files used by Himegari.
Native function provenance, opcode behavior, filesystem policy, and restore control flow remain in
`engine-re.md`; game-global meanings remain sourced from `vm-map/globals.toml` and its generated
`global-reference.md`.

Unless noted otherwise, offsets are byte offsets, integers are little-endian DWORDs, and strings are
NUL-terminated CP932. Himegari separates four physical domains:

| File | Scope | Structural owner | Semantic owner |
|---|---|---|---|
| `SAVE.DAT` / `SAVE.BAK` | whole profile | AGE's typed selected-cell payload inside the common save container | scripts choose which integer/string global cells to persist |
| `RT.DAT` / `RT.BAK` | whole profile | AGE's standalone `S3RT` ReadTextDB | script resource ids and T1 message boundaries identify the flags |
| `SAVE%02d.DAT` | one numbered slot | AGE's common container, full VM-state layout, and appended text-history tail | scripts populate live state, choose the frame cutoff, and request a slot save |
| `SAVE%02d.STH` | one numbered slot | AGE's BMP writer/reader | scripts choose the captured surface and slot |

The menu-preview record is not read from `RT.DAT`. Its timestamp/playtime comes from the numbered
`.DAT` header, its screenshot comes from `.STH`, and its title/location/name/stats/difficulty/ending
history come from slot-indexed selected cells in shared `SAVE.DAT`. The numbered `.DAT` still contains
the full restorable VM state; `SAVE.DAT` holds presentation-oriented copies so `SAVE.BIN` can list slots
without decoding every numbered body.

### Common `S3SD` / `S4SD` `.DAT` container — CONFIRMED

Shared `SAVE.DAT` and numbered `SAVE%02d.DAT` use the same wrapper. `RT.DAT` does not: it has the
standalone `S3RT` layout documented below. The fixed `.DAT` header is `0x124` bytes:

| Offset | Size | Field |
|---:|---:|---|
| `0x000` | 4 | ASCII magic `S3SD` or `S4SD` |
| `0x004` | 4 | compatibility id |
| `0x008` | `0x100` | NUL-terminated CP932 game id area |
| `0x108` | `0x10` | Win32 `SYSTEMTIME`: eight little-endian WORDs |
| `0x118` | 4 | accumulated playtime in seconds |
| `0x11c` | 4 | signed `SaveVersion1`, selecting the logical state layout |
| `0x120` | 4 | signed `SaveVersion2`, selecting the payload-codec subversion |

Himegari uses game id `姫狩りダンジョンマイスター` and versions 3.10. Shared `SAVE.DAT` uses
compatibility id `0x4a343234`; installed numbered files use the distinct id `0x42323234`.

A `0x14`-byte codec frame begins at file offset `0x124`, and expanded data begins at `0x138`:

| Frame offset | File offset | Size | Field |
|---:|---:|---:|---|
| `+0x00` | `0x124` | 4 | expanded-data DWORD count; always even |
| `+0x04` | `0x128` | 4 | MSB-first CRC-32 of expanded bytes |
| `+0x08` | `0x12c` | 4 | reflected CRC-32 of expanded bytes |
| `+0x0c` | `0x130` | 4 | rolling XOR seed |
| `+0x10` | `0x134` | 4 | nonzero odd multiplier; only the low 16 bits are valid |

The logical payload is protected in this order:

1. Prefix the payload with two DWORD CRCs over the payload: MSB-first CRC-32, then reflected CRC-32.
2. For `SaveVersion2 >= 2`, optionally compress that checked buffer with the 4 KiB LZSS dialect and
   wrap it as `{original_bytes, consumed_bytes, stored_bytes, stored_data, padding}`. The first two
   lengths agree. If compression does not shrink the data, `stored_bytes == original_bytes` and the
   bytes are verbatim.
3. XOR each source DWORD with the current seed. Multiply its high and low 16-bit halves separately by
   the current odd multiplier, emitting two DWORD products. Advance the seed by `0x0b0b0b0b` and the
   16-bit multiplier by `0x0b02` for each source DWORD.
4. Store CRCs of the expanded products in the outer codec frame.

The inverse requires both products to divide exactly by the current multiplier and both quotients to
fit 16 bits. This supplies an integrity check in addition to the two outer and two inner CRCs.

The LZSS stream uses a zero-filled 4096-byte ring starting at `0xfee`. Each LSB-first flag byte controls
eight tokens (`1=literal`); a match token stores a 12-bit ring offset and four-bit `length-3`, allowing
lengths 3 through 18. The container's expanded-data count determines where it ends. Numbered files may
append another domain after that boundary, so a reader must retain the consumed byte count rather than
assuming the container occupies the entire physical file.

### Shared `SAVE.DAT` logical payload — CONFIRMED

After decoding the common container, the shared payload is sequential:

| Order | Field |
|---:|---|
| 1 | `catalog_count:u32`, then the encrypted base-catalog unlock table |
| 2 | `integer_count:u32`, then `integer_entries[integer_count]`, 16 bytes each |
| 3 | `string_count:u32`, `string_blob_dwords:u32`, then the DWORD-padded string blob |
| 4 | for version 3.10 or later, `selector_counts[256]:u32` for append-catalog unlock tables |
| 5 | for version 3.10 or later, `extended_count:u32`, then the flattened encrypted append tables |
| 6 | `reserved_tail[9]:u32` |

An integer entry is:

| Entry offset | Size | Field |
|---:|---:|---|
| `+0x00` | `0x0c` | typed key field |
| `+0x0c` | 4 | raw integer value |

The meaningful key bytes are `{type_tag, address_hex[8], NUL}`. Integer cells use raw type byte
`0x03`; the address is eight ASCII hexadecimal digits naming the resolved VM global-bank index.
Bytes 10 and 11 in the fixed integer key field are unused native stack residue and cannot be required
to be zero when importing original data.

The string blob repeats `{typed_key_cstring, value_cstring}` `string_count` times. String keys use raw
type byte `0x05` plus the same eight-digit address and NUL; values are CP932. The stored blob length is
a DWORD count. AGE rounds with `(unpadded_bytes / 4) + 1`, deliberately adding a whole zero DWORD when
the last value already ends on a DWORD boundary.

The catalog sections are AGE's profile-wide **resource-seen/unlock database**. Each encrypted table begins
with two header DWORDs:

| Table word | Meaning |
|---:|---|
| `0` | private exponent XOR `0x87912345` |
| `1` | modular-exponentiation modulus |
| `index + 2` | zero when locked/unseen; otherwise the encrypted resource marker |

Loading computes `plain = cipher^private_exponent mod modulus`. A marker is available when its low word
equals `low16(index * 0x053d6f99 + 0xb0b0b0b0)`. Native `asset_open_indexed_entry` writes that marker
only after successfully opening the resource, and opcode `0x19d` queries it. Thus ordinary CG assets
unlock when their AGF is opened and H-scene entries unlock when their SP script is opened.

The base table maps universal resource ids whose high byte is zero. For version 3.10 or later,
`selector_counts[selector]` gives the slot count of each append catalog; the extended array has its own
two-word crypto header followed by those selector tables concatenated in selector order. Packed resource
ids use the high byte as selector and the low 24 bits as the per-catalog index. Runtime lookup accepts
selectors 1 through 127.

The nine-DWORD tail consists of one explicit terminator plus eight DWORDs produced by the native
allocation formula. It remains compatibility-owned rather than mod storage.

The installed Himegari profile is a complete oracle: version 3.10 decodes to 13,210 base-table DWORDs
(13,208 resource slots), 24,070 integer cells, 605 string cells, selector entry 1 equal to 81, 83
extended-table DWORDs (two header plus 81 resource slots), and nine tail DWORDs. The port copy contains
7,966 valid base markers and 75 valid append markers. Those include all 851 CGINIT gallery images and
all 118 SPINIT H-scene scripts; a different encryption key can change every nonzero stored DWORD without
changing the decoded unlock set.

The integer/string maps are AGE's generic selected-cell service, not copies of whole global banks.
Himegari scripts assign their meanings through opcodes `0x1a2`/`0x1a3` and `0x1a9`/`0x1aa`. Known
slot-preview banks include chapter/title, location, protagonist name, portrait entity, level, training
count, growth, personality, difficulty, cleared-ending mask, and installed-append mask. Their canonical
addresses and semantics are generated in `global-reference.md` from `vm-map/globals.toml`; the
cleared-ending value is a 15-bit NG+/inheritance mask whose set bits select the displayed ending badges.

### Shared `RT.DAT` (`S3RT`) — CONFIRMED

`RT.DAT` is not wrapped, transformed, compressed, or checksummed like the `.DAT` save container. It is
a standalone ReadTextDB with a `0x114`-byte header:

| Offset | Size | Field |
|---:|---:|---|
| `0x000` | 4 | ASCII magic `S3RT` |
| `0x004` | 4 | compatibility id (`0x4a343234` for Himegari) |
| `0x008` | `0x100` | NUL-terminated CP932 game id area |
| `0x108` | 4 | major version (`1`) |
| `0x10c` | 4 | minor version (`0`) |
| `0x110` | 4 | script-record count |

At `0x114` are all 12-byte script records:

| Record offset | Size | Field |
|---:|---:|---|
| `+0x00` | 4 | raw packed SYS4/AAI script resource id |
| `+0x04` | 4 | message count |
| `+0x08` | 4 | serialized native heap pointer residue |

After the complete record table, each record's `message_count` DWORD flags follows in record order.
Zero means unread and nonzero means read. The third record word is process-local residue rather than a
portable offset; AGE allocates and replaces it on load. Readers accept original nonzero values, while
portable writers emit zero.

Message indices are positions in that script's T1/F7 table, whose entries point to op-`0x71`
read-message boundaries. Thus the file is profile-wide engine state derived from game scripts, not an
arbitrary script-selected persistence table and not numbered-slot state.

The installed Himegari file is exactly 76,752 bytes: 192 records plus 18,543 flag DWORDs consume it
without trailing data. Base script `SC0000` has packed id `0x22` and 320 flags, exactly matching its
T1 count.

### Numbered `SAVE%02d.DAT`, logical layout 3 — CONFIRMED FOR HIMEGARI

Himegari's numbered file begins with the common container using compatibility id `0x42323234`,
`SaveVersion1=3`, and `SaveVersion2=10`. The header supplies the save-menu timestamp and accumulated
playtime without decoding the body.

The decoded layout-3 body begins:

| Offset | Size | Field |
|---:|---:|---|
| `0x000` | 4 | terminal saved-frame index, called `cutoff`; frame count is `cutoff + 1` |
| `0x004` | 4 | saved frame-owner/context word |
| `0x008` | 4 | current direct-name BGM track id |
| `0x00c` | `0x28` | ten packed SFX resource ids, one per retained channel |
| `0x034` | `0x4b0` | 100 resource-reload records of three DWORDs |
| `0x4e4` | `0x4e20` | 1,000 surface-reload records of 20 bytes |
| `0x5304` | `(cutoff + 1) * 0x414` | saved script-frame records |
| `0x5718 + cutoff * 0x414` | variable | global banks and retained graphics |

Each `0x414`-byte frame is:

| Frame offset | Size | Field |
|---:|---:|---|
| `+0x000` | 4 | parent context |
| `+0x004` | 4 | packed script resource id |
| `+0x008` | 4 | saved local-return count, at most 256 |
| `+0x00c` | up to `0x400` | T3 local-return indices |
| `+0x40c` | 4 | T1 resume/message-boundary index |
| `+0x410` | 4 | T2 resumable call-script index; forced to `-1` on the cutoff frame |

The T1/T2/T3 indices refer to the three footer tables in the saved SYS4 script. T1 restores the active
message boundary, T2 restores the inter-script call site, and T3 entries restore local returns as
`T3[index] + 3`.

The variable section starts with six DWORD counts in this order:

1. integer globals;
2. raw float storage;
3. string globals;
4. integer-pointer globals;
5. string-pointer globals;
6. local pointer scratch.

The integer and float arrays follow their counts. String count comes from bank count 3, but its storage
begins with an additional `string_blob_dwords` followed by that many bytes of concatenated NUL-terminated
CP932 strings and DWORD padding. The three pointer-family DWORD arrays follow.

These counts are serialized mutable-prefix lengths, not declarations that every typed runtime bank ends
there. Native deserialization zeroes and replaces only each counted prefix. Initialization-authored
definitions after the prefix remain live: in Himegari the saved integer prefix ends at `0x6241b`, before
unit/stage definition tables such as `0x66716` and `0xe8275`, while the saved string prefix ends at
`0x315`, exactly where `unit_story_display_names` begins. Clearing the whole runtime dictionary during
import therefore destroys data that is intentionally absent from the numbered file.

Retained graphics then uses:

```text
0x00  gfx_record_size:u32              # 0x2d4
+0x04  gfx_object_count:u32
+0x08  repeated sparse entries:
         handle:u32
         record[0x2d4]                 # 0xb5 meaningful DWORDs
         padding[0x87c]
       entry stride = 0x2d5 DWORDs = 0xb54 bytes
...    range_first:u32
...    range_count:i32
...    range_transform_record[0x2d4]
...    native allocation slack
```

The sparse stride is not an allocator-only artifact. Both native writer and reader hold a DWORD pointer:
after the handle, they copy `0x2d4` **bytes** but advance by `0x2d4` **DWORDs**. Consecutive handles are
therefore `0x2d5` DWORDs apart. Early experimental port saves incorrectly packed
`{handle,record}` contiguously; compatibility import recognizes that layout, while all new output uses
the native sparse form.

The 1,000 surface records preserve the native 20-byte metadata cells verbatim:

| Offset | Size | Meaning |
|---:|---:|---|
| `+0x00` | 4 | signed resource id; `-1` means no reloadable asset |
| `+0x04` | 4 | packed color key |
| `+0x08` | 4 | reload-on-restore flag |
| `+0x0c` | 4 | unknown lifecycle field; cleared with `+0x08` at script entry |
| `+0x10` | 4 | blank/mutable created-surface flag |

Initialization writes resource id `-1` to every otherwise-zero record. Ordinary asset load, blank-surface
creation, and release do not synthesize or clear the reload flag; opcode `0x259` clears `+0x08` and
`+0x0c` across both live/saved tables at script entry. During normal layout-3 restoration AGE reopens only
records whose reload flag is one and whose resource id is nonnegative. Flag-zero records describe
surfaces expected to remain live across the load. The installed file is decisive: all 1,000 reload flags
are zero, while SYSTEM4's reusable choice-frame atlas remains recorded as resource `0x3383` in slot 15.
A separate configuration-gated path can release all 1,000 surfaces before this loop, but Himegari's
registered `CreateObject=1`, `AutoFreeTex=0` defaults leave it inactive. A retained `0x2d4`
record is structurally complete but not every internal graphics field is semantically named. Native
`gfx_object_init_default` initializes six embedded 4x4 matrices (current/target scale, rotation, and
translation) to identity, packed colors at `+0x60/+0x64/+0x240` to `0xffffffff`, and other fields to zero.
Deserialization initializes an object and then overwrites all `0x2d4` bytes, so a compatibility writer
must emit or preserve those defaults rather than zero-fill unnamed fields. The graphics sizing term remains
larger than the meaningful sparse entries and range record
(`0x2e1 + object_count * 0x2d8` DWORDs), leaving final allocation slack as well.

The installed `SAVE00.DAT` validates the complete layout-3 decode: cutoff 1, global-bank counts
`[402459,1,789,1,1,1]`, current BGM id `0x18`, retained SFX ids `0x3321` (channel 1) and
`0x2aea` (channel 2), and 211 retained graphics objects.

#### Appended text-history tail

The common container's expanded-data count ends before the numbered file ends. Layout 3 appends a
separate text-history stream immediately at that consumed boundary:

| Tail offset | Size | Field |
|---:|---:|---|
| `+0x00` | 4 | logical byte length |
| `+0x04` | 4 | duplicate logical byte length |
| `+0x08` | 4 | stored byte length |
| `+0x0c` | variable | the same 4 KiB-ring LZSS stream, or verbatim bytes when lengths match |

The decoded history data is:

1. `entry_count:u32`, then `entry_count` pairs `{layout_slot:i32, first_record_index:i32}`;
2. `record_count:u32`, then `record_count` records of 11 DWORDs;
3. `string_blob_dwords:u32`, then a DWORD-aligned CP932 string blob whose DWORDs are bitwise inverted.

Each 11-DWORD record stores layout slot, origin X/Y, width/height, primary value, auxiliary value,
primary font size, text color, cursor Y, and flags. The string blob contains one NUL-terminated string
per record. The installed save has a 21-byte physical history tail, and container plus tail consume the
file exactly.

### Numbered `SAVE%02d.STH` thumbnail — CONFIRMED

The thumbnail is a separate ordinary BMP under a nonstandard extension:

- `BM` file signature and `0x36` pixel offset;
- 14-byte bitmap file header plus 40-byte `BITMAPINFOHEADER`;
- uncompressed 24-bit BGR pixels;
- bottom-up positive height;
- rows padded to four-byte boundaries.

Installed Himegari thumbnails are 112x84 and 28,278 physical bytes. The native writer stores
`bfSize=28,264`, omitting the physically present 14-byte file header from that field while retaining the
correct physical header and pixel offset. Compatibility writers reproduce this harmless historical quirk.

Copy, move, and delete operations treat `.DAT` and `.STH` as a pair, but neither file contains the other.

### Scope and remaining uncertainty

The following are complete enough for compatible import/export:

- the common S3SD/S4SD header, CRCs, optional LZSS wrapper, and DWORD transform;
- Himegari's shared `SAVE.DAT` 3.10 section boundaries and typed selected cells;
- `RT.DAT` 1.0 records and flag arrays;
- Himegari's numbered layout-3 fixed state, global banks, frames, graphics region, and history tail;
- `.STH` BMP encoding.

The remaining uncertainty is deliberately narrower:

- numbered layouts 1 and 2 are structurally identified but are not the implemented or installed
  Himegari compatibility target;
- some shared selected cells and the nine-DWORD tail remain opaque at the game-semantic level;
- the `0x2d4` retained-graphics record is byte-bounded, but not every field is named.

This distinction is important: AGE owns most numbered-save and ReadTextDB structure, while Himegari
scripts primarily own the values in live VM globals, the save cutoff/invocation, and the semantic choice
of selected `SAVE.DAT` cells.

## Header — CONFIRMED

Fixed 60-byte (0x3C) header: 8-byte magic + thirteen little-endian u32 fields.
Verified across all 481 files.

```
off   field  meaning                                    evidence
0x00  magic  "SYS4422 " (0x53 59 53 34 34 32 32 20)     481/481 identical
0x08  F0     local integer count
0x0C  F1     local float count
0x10  F2     local string count
0x14  F3     local integer-pointer count
0x18  F4     second pointer-family count (exact subtype unresolved)
0x1C  F5     local string-pointer count
0x20  F6     = 0x1C always                              481/481 == 0x1C  (header-size marker)
0x24  F7     table-1 entry count                        see below
0x28  F8     table-1 offset  == code-section length     ordering F8<=F10<=F12<=EOF
0x2C  F9     table-2 entry count
0x30  F10    table-2 offset
0x34  F11    table-3 entry count
0x38  F12    table-3 offset
0x3C  body   dword stream (code + 3 tables + strings)
```

**All offsets/counts are in DWORDS (×4 bytes), relative to body start (0x3C).**
The `0x1C` in F6 is the only *byte* count — it's the offset from F6's own position
(0x20) back-referenced, i.e. a self-describing "28 bytes of descriptor follow" marker
consistent with the SYS4/SYS5 family.

### Section layout (CONFIRMED — 0 ordering violations, 481/481)

```
body[0        .. F8)    CODE          bytecode instruction stream
body[F8       .. F10)   TABLE-1  (F7 entries, 1 dword each)  -> read-message boundaries (op 0x71)
body[F10      .. F12)   TABLE-2  (F9 entries, 1 dword each)  -> call-script sites (op 0x03)
body[F12      .. EOF)   TABLE-3  (F11 entries, 1 dword each) -> local-call sites (op 0x8F)
```

Every table entry is exactly **1 dword** — a pointer (dword index into body).
Solved algebraically across the whole corpus: `(F10-F8)/F7 == (F12-F10)/F9 ==
(EOF-F12)/F11 == 1` with zero non-integer results.

### The three tables are typed pointer indexes (CONFIRMED)

Each table points at body locations, and the dword *at* every target is a constant
tag identifying the pointed-to construct:

| Table | count/off | Target dword tag | Hits | Meaning (inferred) |
|---|---|---|---|---|
| T1 | F7 / F8  | **0x71** | 26,445/26,445 | per-script read-message boundary index |
| T2 | F9 / F10 | **0x03** | 3,018/3,018   | resumable call-script sites |
| T3 | F11 / F12| **0x8F** | 72,941/72,941 | local-call sites used to reconstruct return stacks |

100% type purity — not a single target had a different tag. Native
`read_text_db_find_message_index@0x468f50` searches T1 for the code DWORD coordinate most recently
snapshotted by op `0x71`; its zero-based entry index and F7 count are the message index/count stored in
shared `RT.DAT`. Thus T1 is not a generic label table even though every entry is a control-structure
site. Numbered layout-3 frame serialization proves the remaining roles: saved d259 indexes T1,
d260 indexes T2, and each intra-script return maps through T3 (restored as `T3[index]+3`).

## Instruction stream — PARTIAL

The code section is a flat dword stream. Recurring "type/opcode" dwords observed:
`0x03, 0x55, 0x6E, 0x6F, 0x71, 0x72, 0x8F`. These read as **operand-type tags**
in a tagged-operand VM rather than raw opcodes, e.g. the repeating shape:

```
... <TAG> <value> ...        tag 0x02 => string pointer (see below)
... 0x71 0x00 0x01 0x55 ...  label marker + following instruction
```

- **First body dword is 0x259 (601) in 301/481 files** — likely a standard
  "script entry" / prologue opcode. Second-most-common openers are small ints.
- `0x55` appears pervasively as an instruction lead — probably the most common
  opcode (statement / expression separator).

*Full opcode semantics need the VM dispatch loop in `AGE.EXE` — that's the Ghidra
task. These tags give a head start on labeling the disassembly.*

## Strings — CONFIRMED

- Stored inline in the body as **byte-complement (XOR 0xFF) cp932 / Shift-JIS**,
  packed 4 bytes per dword, NUL-terminated (a `\0` byte, i.e. `0xFF` after XOR,
  ends the string), then padded to the next dword.
- **Referenced by a tagged operand: the dword `0x02` immediately followed by the
  dword-offset of the string.** Confirmed directly by xref:
  - `MENU.BIN`: `...2 30b...` @0x2A9 → offset 0x30B = `"ＭＳ 明朝"`; `...2 30e...` → `"loadmesskip menu"`
  - `SC0030.BIN`: `...2 ef80...` → `"▼G0030　２章マップ０２１クリア"`; `...2 ef89...` → `"「よし、素晴らしい成果だな」"`
- Decoder (validated — pulls clean Japanese dialogue):
  ```python
  raw = bytes(b ^ 0xFF for b in body[off*4:])   # until a 0x00 appears
  text = raw.split(b"\0")[0].decode("cp932")
  ```
- Scene scripts hold the full dialogue; e.g. `SC0030.BIN` decodes to readable
  story text, choice-branch labels ("本来の分岐", "チェック用"), font names
  ("ＭＳ 明朝"), and engine directives ("loadmesskip advset", "ＡＤＶパート").
- Non-scene scripts (`MENU`, `ADDEXP`) contain only a handful of control strings —
  consistent with the inventory's subsystem/data-table categorization.

## Inline integer arrays — CONFIRMED

Opcode `0x64` operand 2 points to a footer blob in the same body-dword coordinate space as strings. The blob
is `[count:u32, value0:u32, ... valueN:u32]`; these dwords are stored plainly in the original SYS4 file.
For example, `HISTORY.BIN@0x13d4` is `[4, 0, 5, 0, 538]`, and its fifteen blobs initialize the backlog UI's
rectangle, coordinate, and lookup arrays. An op-`0x64` reference lowers the executable-code boundary just
like a type-2 string reference.

Native `AGE.EXE` applies a per-context rotate/XOR representation after loading script data and reverses it
inside the opcode handler. That transformation is not part of the on-disk format: file readers and the port
must copy the footer's plain dwords, including two's-complement negative values, without applying the native
memory-cookie transform.

## Patch-override caveat (re-confirmed)

49 loose script `.BIN` files in the game root shadow DATA1 copies at runtime and differ slightly in size;
two additional root-only engine BINs bring the root total to 51. The disassembler should target the **root** copies where present.
The header format is identical (same magic/layout) so tooling is copy-agnostic.

## What's solid vs. what needs Ghidra

**Solid (byte-verified, build a loader now):**
- 60-byte header, all 13 fields, dword units, section boundaries
- 3 typed pointer tables (0x71 / 0x03 / 0x8F), 1 dword each, 100% pure
- String encoding (XOR-0xFF cp932) + reference mechanism (tag 0x02 + offset)

**Needs the VM (Ghidra on `AGE.EXE`):**
- Opcode dispatch — confirm tagged-operand model, enumerate opcodes
- Meaning of F0/F2/F3/F5 flag fields
- Exact operand grammar per instruction (how many dwords each opcode consumes)

## Loader — DONE

`tools/sys4load.py` parses the header, splits the 4 sections, resolves the 3
tables, decodes inline strings, and emits an assembly-ish listing with strings
inlined at their `2 <off>` refs (opcodes not yet named — code is chunked by the
T3 line-index). Importable API (`load()` → `Sys4Script`) plus CLI:

```
sys4load.py <file.BIN>              full listing
sys4load.py <file.BIN> --summary    header + section sizes + table/string counts
sys4load.py <file.BIN> --strings    decoded string pool
sys4load.py <file.BIN> --json       machine-readable structure
sys4load.py <dir> --validate        re-check invariants across a folder
```

`--validate` over all 481 DATA1 scripts: **481 parsed clean, 0 failures, 0 impure
table tags** — the format spec above is fully machine-verified. This listing is the
artifact to diff against Ghidra output once the VM dispatch loop is mapped.

### Observations surfaced by the listing (leads for the VM work)
- String-display sites look like `<opcode> 0x02 <str-off>` — e.g. opcodes `0x1A7`
  and `0x1A5` immediately precede string refs in `MENU.BIN`. Candidate text/message ops.
- T3 entries are 3-dword records `[0x8F, 0x00, value]`; T1 labels are `[0x71, 0x00,
  value]`. T3 acts as a per-statement line index (editor metadata) — note scene
  scripts even carry editor annotation strings like `"LABEL"`, `"ループ開始"` (loop start).
- `0x55` is the most frequent code lead (likely statement/expr separator); `0x09`
  recurs as an operand-type prefix (register/var reference?).

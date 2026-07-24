# SYS4 Script Format — Reverse-Engineering Notes (hex-first)

Derived purely from byte-pattern analysis of the 481 DATA1 `.BIN` scripts, before
any Ghidra work on `AGE.EXE`. Confidence levels flagged per finding. Probe scripts
live in `tools/probe_*.py`.

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

The Himegari trailer contains `SCREENX=800` and `SCREENY=600`; Kamidori's independent SYS4INI contains
`SCREENX=1024` and `SCREENY=576`. This establishes the authored logical canvas as per-game data. Native
initialization and the executable's `640x480` fallback are documented in `engine-re.md`;
fullscreen/display-mode settings are a separate concern from these logical dimensions.

The same Himegari trailer also contains `USEAPPDATAFOLDER=1` and
`SAVEPATH=Eushully\姫狩りダンジョンマイスター\SAVE`. AGE's native save-root resolver consumes those
settings to select `%LOCALAPPDATA%` plus that relative path on modern Windows. Filename patterns remain
native policy rather than trailer or script strings; see `engine-re.md` under "Save-root resolution."

## Header — CONFIRMED

Fixed 60-byte (0x3C) header: 8-byte magic + thirteen little-endian u32 fields.
Verified across all 481 files.

```
off   field  meaning                                    evidence
0x00  magic  "SYS4422 " (0x53 59 53 34 34 32 32 20)     481/481 identical
0x08  F0     scenario/script id or flags                98 distinct; 0x5 dominant (252×)
0x0C  F1     = 1 always                                 481/481 == 1
0x10  F2     = 1 (457×) or 2 (20×); 4 outliers          format/feature flag
0x14  F3     0x06 (268×),0x04,0x01,0x05...              15 distinct — minor version?
0x18  F4     = 1 always                                 481/481 == 1
0x1C  F5     = 2 (319×) or 1 (144×); rarely 3/5         flag
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
body[F10      .. F12)   TABLE-2  (F9 entries, 1 dword each)  -> targets of type 0x03
body[F12      .. EOF)   TABLE-3  (F11 entries, 1 dword each) -> targets of type 0x8F
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
| T2 | F9 / F10 | **0x03** | 3,018/3,018   | data/variable entries (operand at +2 large, e.g. addresses) |
| T3 | F11 / F12| **0x8F** | 72,941/72,941 | instruction/line entries (largest table; operand at +2 huge) |

100% type purity — not a single target had a different tag. Native
`read_text_db_find_message_index@0x468f50` searches T1 for the code DWORD coordinate most recently
snapshotted by op `0x71`; its zero-based entry index and F7 count are the message index/count stored in
shared `RT.DAT`. Thus T1 is not a generic label table even though every entry is a control-structure
site. T3 is the big one
(~73k entries corpus-wide), consistent with it being a per-instruction or
per-source-line index (a debug/line table). T1 ≈ labels, T2 ≈ a smaller symbol set.

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
- Semantics of T1/T2/T3 beyond "label/data/line" guesses

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

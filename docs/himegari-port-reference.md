# Princess Hunting Dungeon Meister — Godot Port Reference

Working reference for porting *姫狩りダンジョンマイスター* (Eushully, 2009) to Godot.

Source install: `C:\Program Files (x86)\Eushully\姫狩りダンジョンマイスター\`
Workspace: `S:\Game Hacking\Eushully\Himegari\` — three siblings: `姫狩りダンジョンマイスター\` (pristine game), `extracted\` (extracted ALF data), `age-reimpl\` (our work). See `docs/PROJECT-STRUCTURE.md`.

---

## Engine background

Eushully built their own engine, called **AGE**, with three major generations distinguished by their config/index files and archive magic bytes:

| Generation | Index file | ALF magic | Era (rough) |
|---|---|---|---|
| SYS3 | `sys3ini.bin` | `S3IC` / `S3IN` | mid-2000s |
| SYS4 | `sys4ini.bin` / `.AAI` | `S4AC` / `S4IC` | late-2000s — early-2010s |
| SYS5 | `sys5ini.bin` | `S5IN` / `S5IC` / `S5A` | ~2013+ |

**Confirmed SYS4** for this game — `extracted\DATA1\SYSTEM4.BIN` is the smoking gun, and the `APPEND01.AAI` sidecar is the SYS4 signature pattern.

Install dir contents: `AGE.EXE`, `DATA1-5.ALF`, `APPEND01.ALF`, `APPEND01.AAI`, plus `BinExtractALF.exe` and `AGE Patch.exe`.

---

## Tier 1 — Primary references

### [Kelebek1/Eushully-Decompiler](https://github.com/Kelebek1/Eushully-Decompiler)
C++ decompiler + recompiler for `.bin` scripts, with `extract_alf.py` for archive extraction.

- Targets **SYS5** specifically (its extractor scans for `SYS5INI.BIN` and `S5*` magic). Will need adaptation for SYS4, but the structure is parallel and the opcode work is gold as a starting framework.
- Most actively maintained (latest release Aug 2024). 22 stars but the most serious decompiler effort in the wild.
- Known limitation: opcode tables are hand-built per engine version — community has open requests to add more opcodes.
- See [extract_alf.py](https://github.com/Kelebek1/Eushully-Decompiler/blob/master/extract_alf.py) for the cleanest reference implementation of ALF parsing.

### [morkt/GARbro](https://github.com/morkt/GARbro)
Swiss-army VN asset extractor. Has [`ArcFormats/Eushully/ArcALF.cs`](https://github.com/morkt/GARbro/blob/master/ArcFormats/Eushully/ArcALF.cs) covering **all three engine generations** (SYS3/4/5), plus contained formats AGF (graphics), AOG (audio), SCR.

- The more directly applicable extractor for this game — use it as the authoritative reference for the SYS4 ALF format and LZSS decompression of the index.
- C# / .NET. Source is readable and well-organized per format.

## Tier 2 — Supporting tools

### [marcussacana/EushullyEditor](https://github.com/marcussacana/EushullyEditor)
C# library for string-level edits to `.bin` scripts (translation-focused, not full decompilation).

- Pre-configured for *Kamidori Alchemy Meister* and *Kami no Rhapsody*.
- Useful as a second reference for the script string-table structure, especially since Kamidori is the same SYS4 era as Dungeon Meister.

### Existing tools already on disk
- `BinExtractALF.exe` — pre-built ALF extractor someone already ran here. Worth confirming origin (likely from a Japanese tools site or HongFire / Mikocon thread).
- `extracted\DATA1\AGF2BMP2AGF.exe` + `LzssCpp.dll` — bidirectional AGF↔BMP converter for the proprietary graphics format. Original by **asmodean** ([asmodean.reverse.net](http://asmodean.reverse.net/) — canonical source of older Japanese game tooling, worth bookmarking).
- `AGE Patch.exe` — likely an English localization or no-DVD patch, not a tool per se.

---

## Current extraction state

| Archive | Files | Size | Contents |
|---|---|---|---|
| DATA1 | 3,749 | 2.2 GB | **Mixed core data**: 1511 AGF + 1508 BMP (graphics, ~all already converted), **481 .BIN scripts**, 238 WAV (system sfx), 9 cursors, the AGF2BMP2AGF tool + LzssCpp.dll |
| DATA2 | 985 | 791 MB | Event CGs (`.AGF` only) — pure graphics archive |
| DATA3 | 39 | 124 MB | BGM tracks (`.OGG` only) |
| DATA4 | 9,733 | 471 MB | Voice files (`.OGG` only) — character lines |
| DATA5 | 210 | 291 MB | More `.AGF` graphics — likely appendix/extra content |

### Notable scripts already extracted (DATA1)
- `SYSTEM4.BIN` — engine config/setup (the SYS4 index)
- `HISTORY.BIN`, `MENU.BIN`, `HIDEWIN.BIN` — UI/system scripts
- `SC0000.BIN` and presumably hundreds of `SC####.BIN` — scene scripts, the actual game logic

### Status by category
- **Graphics**: 1508/1511 in DATA1 already converted to BMP. ~1,195 AGFs remaining in DATA2 and DATA5.
- **Audio**: Fully extracted as standard OGG Vorbis (9,772 files). Godot ingests natively — no further work.
- **Scripts**: 481 `.BIN` files extracted but still in AGE bytecode form. This is the real porting work.

---

## Next steps

### Where things stand (read this first)
Container format is **fully reversed and machine-verified** (header, 4 sections, 3
typed pointer tables, inline string encoding — all 481 scripts parse clean). Tooling
exists: `tools/sys4load.py` (loader + disassembler-ish dumper + `--validate`) and the
`tools/probe_*.py` analysis scripts. Companion docs: [script-inventory.md](script-inventory.md)
(what the 481 scripts are) and [sys4-format-notes.md](sys4-format-notes.md) (the byte format).

**The one blocker for everything downstream is opcode semantics** — the code stream is
a tagged-dword format whose instruction meanings are unknown. That needs the VM dispatch
loop in `AGE.EXE`, which is the Ghidra task below. Graphics conversion (DATA2/5 AGFs) and
save-format work remain deferred.

### Immediate (no tools needed beyond what's on disk)
1. ~~**Relocate the `Output\` tree**~~ **DONE** — workspace now at `S:\Game Hacking\Eushully\Himegari\姫狩りダンジョンマイスター\`.
2. **Convert remaining AGFs** in DATA2 (985 files) and DATA5 (210 files) with `AGF2BMP2AGF.exe`. *(Deferred — graphics not needed yet.)* The 3-file DATA1 gap is `CHAPTER.AGF`, `LOGO.AGF`, `TEST.AGF`.
3. ~~**Inventory the script files**~~ **DONE** — see [script-inventory.md](script-inventory.md). Key findings: all 481 scripts share magic `SYS4422 `; 52 loose root-dir `.BIN` files are patch overrides that shadow DATA1 copies (use those as authoritative); heavy game logic (damage calc, dungeon loop, battle flow) lives in bytecode, favoring a VM re-implementation in Godot.
1. ~~**Relocate the `Output\` tree**~~ **DONE** — workspace now at `S:\Game Hacking\Eushully\Himegari\姫狩りダンジョンマイスター\`.
2. **Convert remaining AGFs** in DATA2 (985 files) and DATA5 (210 files) with `AGF2BMP2AGF.exe`. *(Deferred — graphics not needed yet.)* The 3-file DATA1 gap is `CHAPTER.AGF`, `LOGO.AGF`, `TEST.AGF`.
3. ~~**Inventory the script files**~~ **DONE** — see [script-inventory.md](script-inventory.md). Key findings: all 481 scripts share magic `SYS4422 `; 52 loose root-dir `.BIN` files are patch overrides that shadow DATA1 copies (use those as authoritative); heavy game logic (damage calc, dungeon loop, battle flow) lives in bytecode, favoring a VM re-implementation in Godot.

### Header structure — DONE (hex-first, pre-Ghidra)
See [sys4-format-notes.md](sys4-format-notes.md). Confirmed across all 481 files: 60-byte header (magic `SYS4422 ` + 13 u32 fields, all offsets in dwords), body split into CODE + 3 typed pointer tables (tags 0x71/0x03/0x8F, 1 dword each, 100% pure) + inline strings. Strings are XOR-0xFF cp932, referenced by a `0x02 <dword-offset>` tagged operand — verified by decoding real dialogue out of `SC0030.BIN`. Remaining unknowns (opcode dispatch, flag fields F0/F2/F3/F5) need the VM.

### NEXT ACTION — port the opcode table (see [vm-mapping-plan.md](vm-mapping-plan.md))
**The full phased playbook lives in [vm-mapping-plan.md](vm-mapping-plan.md)** — start there.

> ✅ **BREAKTHROUGH (verified 2026-07-05): the opcode set is already solved.**
> Kelebek1's decompiler (`age-shared.cpp`) ships an AGE opcode table that decodes
> this game directly — **476/476 scripts decode 100% clean, 1.46M instructions, 0
> unknown opcodes, 37,392/0 string args resolved.** Model: code = instructions of
> `<opcode> + argc*(<type><value>)`, length `1+2*argc`; stop code at the first inline
> string offset. Himegari uses 248 opcodes, 52 named (see `vm-map/opcodes-himegari.json`).
> Header fields F0–F5 are now known = local-variable counts (Kelebek's `BinaryHeader`).
> **Unpacking `AGE.EXE` is no longer the blocker** — it's demoted to optional Phase 3
> enrichment (prefer Frida hooking). Reproduce: `tools/validate_opcode_table.py`.
>
> ⚠️ **Coverage nuance (measured 2026-07-06):** "solved" means every instruction *decodes*
> (structure/length known, 481/481 clean). It does **not** mean every instruction is
> *understood*: the 52 named ops are only **72.6% of instruction volume**; the unnamed
> `u004xxxx` 27.4% is concentrated in the highest-frequency opcodes and can't be fully
> deferred before the Godot VM. Also the 52 semantic *labels* come from a later AGE title —
> numbers+argc are validated for Himegari, semantics are not (text ops confirmed by the
> dialogue corpus; effectful ops need Frida confirmation). See `vm-mapping-plan.md` Phase 3.

> ⚠️ **Note on `AGE.EXE`:** still packed (max-entropy sections, IAT RVA 0). Only
> relevant if you later need to name the 196 unnamed opcodes statically — see the
> plan's appendix. `SYS4AB.BIN` (magic `S4AB`) is a 2nd encrypted engine image.

Original Ghidra sketch (superseded; kept only for the appendix unpack route):

4. **Find the dispatch loop.** Look for where `AGE.EXE` reads a script's first body
   dword (`0x259` in 301/481 files — likely the prologue/entry opcode) and switches on
   dword tag values. Expect a large switch or jump table. Diff against Kelebek1's SYS5
   opcode-handler addresses to map SYS4 equivalents (same engine family, parallel structure).
5. **Seed the opcode map from known anchors** (from `sys4load.py` dumps, already observed):
   - `0x02` = string-pointer operand tag (confirmed).
   - `0x1A7` / `0x1A5` = opcodes that immediately precede string refs in `MENU.BIN` →
     candidate text/message-display instructions. Start here; they're the easiest to confirm.
   - `0x8F` / `0x71` / `0x03` = the tags at T3-line / T1-label / T2-data table targets.
   - `0x55` = most frequent code lead (likely statement/expr separator); `0x09` = recurring
     operand-type prefix (register/var ref?).
   Cross-reference with marcussacana's Kamidori (same SYS4 era) as a second opcode source.
6. **Encode the opcode table into `sys4load.py`.** As each opcode's length + operand
   grammar is confirmed in Ghidra, add it so the dumper decodes real instructions instead
   of chunking by the T3 line-index. `MENU.BIN` (3 strings, small control flow) is the
   validation target — decode it fully first, then a mid-size `SC####` scene end-to-end.
7. **(Optional sanity check)** Locate the ALF mount/decrypt code to confirm GARbro's parser
   matches this build. Low priority — extraction already succeeded, so this is only if an
   archive anomaly shows up.

### Data-table extraction (unblocks in parallel once a few opcodes are known)
8. The `*INIT` giants (`STINIT` 579 KB stages, `EBINIT` 338 KB enemies, `MPINIT` 330 KB
   maps, `ITINIT` items, `SKINIT` skills, `CGINIT` gallery) are static data tables. Once
   the T2/data-entry grammar is understood they can be dumped to JSON/CSV **without** a
   complete opcode set — an early, high-value win for the game database.
9. **Bulk-extract all dialogue** — `sys4load.py --strings` already pulls clean cp932 text
   from every scene today. A batch run over all `SC####`/`SP####` yields the full script
   corpus for translation, independent of the VM work.

### Deferred tracks
10. **Save-file format** — reverse `SAVE.BIN` / the save layout only if the port must read
    existing saves. Likely a small struct dump; low priority until gameplay runs.
11. **Godot representation** — decide: re-implement the AGE VM in GDScript/C#, or transpile
    `.BIN` → native Godot scenes. The inventory already tilts toward **re-implementing the
    VM** (damage calc, dungeon loop, battle flow all live in bytecode, so a transpiler would
    have to cover nearly the whole opcode set anyway). Ghidra's view of how much logic sits
    in `AGE.EXE` vs. bytecode makes the final call.

---

## Sources

- [Kelebek1/Eushully-Decompiler](https://github.com/Kelebek1/Eushully-Decompiler)
- [Kelebek1/Eushully-Decompiler/extract_alf.py](https://github.com/Kelebek1/Eushully-Decompiler/blob/master/extract_alf.py)
- [Kelebek1issue #2 — AGE engine version discussion](https://github.com/Kelebek1/Eushully-Decompiler/issues/2)
- [marcussacana/EushullyEditor](https://github.com/marcussacana/EushullyEditor)
- [morkt/GARbro](https://github.com/morkt/GARbro)
- [GARbro ArcFormats/Eushully/ArcALF.cs](https://github.com/morkt/GARbro/blob/master/ArcFormats/Eushully/ArcALF.cs)
- [Eushully Fandom Wiki — Princess Hunting Dungeon Meister notes](https://eushully.fandom.com/wiki/Princess_Hunting_Dungeon_Meister:Notes)
- [asmodean.reverse.net](http://asmodean.reverse.net/) — canonical home of older Japanese game tooling

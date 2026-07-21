# Project Structure

Layout for the *姫狩りダンジョンマイスター* (Himegari) → open AGE-engine reimplementation.
The guiding rule is **source vs. derived vs. our work**: the shipped game is read-only
input, the extracted archives and everything our tools generate are disposable/reproducible,
and our code + docs live entirely apart from the game install. Nothing we produce is ever
written back into the game folder.

Workspace root: `S:\Game Hacking\Eushully\Himegari\`

```
S:\Game Hacking\Eushully\Himegari\           ← workspace root (three siblings)
│
├── 姫狩りダンジョンマイスター/               ← SOURCE — pristine game install (read-only)
│   │   Never edit, move, or add to this folder. It holds ORIGINALS ONLY.
│   ├── AGE.EXE, AGERC.DLL, *.dll             shipped engine (packed). Stays intact and
│   │                                         runnable in place — Frida launches it if needed.
│   ├── DATA1-5.ALF, APPEND01.ALF/.AAI        shipped archives (~2.3 GB).
│   ├── *.BIN                                 49 loose patch-override scripts (v1.03) —
│   │                                         AUTHORITATIVE over DATA1. Plus two root-only
│   │                                         engine files (SYS4INI=S4IC, SYS4AB=S4AB).
│   └── *.exe (uninstallers), SAS0099.OGG …   other shipped files.
│
├── extracted/                               ← DERIVED (game-side) — extracted ALF contents,
│   │                                          ~3.9 GB, regenerable via age-reimpl/bin/BinExtractALF.
│   └── DATA1/ … DATA5/                         DATA1 = 481 .BIN scripts (the corpus we parse)
│                                               + AGF/BMP/WAV in the others.
│
└── age-reimpl/                              ← OUR WORK (everything we made lives here)
    │
    ├── tools/                               Python tooling (parser/disassembler + extractors + VM)
    │   ├── paths.py                           ★ central path anchor — the ONLY place that knows
    │   │                                        where the game / extracted / build dirs are. All
    │   │                                        tools import it; relocatable with no other edits.
    │   ├── sys4load.py                         loader + disassembler (opcode-decoding)
    │   ├── age_opcodes.py                      548-entry Kelebek AGE opcode/arg-type table (PRISTINE; never edit)
    │   ├── opcodes_build.py                    generator/linter: vm-map/opcodes.toml -> the 4 artifacts below
    │   ├── opcodes_model.py                    load + lint (dangling-ref, confidence-ceiling, vocab) + dependents
    │   ├── age_opcodes_himegari.py             GENERATED from opcodes.toml (do not hand-edit)
    │   ├── globals_build.py                    generator/linter: vm-map/globals.toml (+auto map) -> build/globals.json, docs/global-reference.md
    │   ├── story_flags.py                      static story-flag miner (branch-condition) -> build/story-flags-candidates.json; --bootstrap
    │   ├── scjump_decode.py                    decode SCJUMP progression logic -> build/scjump-decisions.* ; --verify (VM cross-check)
    │   ├── test_globals.py, test_opcodes.py    unit tests for the globals / opcode tooling
    │   ├── vm0.py                              headless Python VM (Phase A0); `--test` = RECOVER unit test
    │   ├── extract_phase2.py                   batch: disasm + text + data extraction
    │   ├── extract_init.py, global_map.py …    *INIT parsers, global-var map builder
    │   ├── validate_opcode_table*.py           decode-coverage validators
    │   └── probe_*.py                          format reverse-engineering probes (historical)
    │
    ├── bin/                                  3rd-party binaries we use (not ours, not the game's)
    │   ├── BinExtractALF.exe                   ALF archive extractor → produces extracted/
    │   ├── LzssCpp.dll                         its LZSS codec dependency
    │   └── pe-sieve32.exe                      (hollowsprocess) unpacked-PE dumper — engine-RE escalation (see docs/engine-re.md)
    │
    ├── vm-map/                               VM / reverse-engineering reference artifacts
    │   ├── opcodes.toml                        ★ CANONICAL opcode reference (hand-edited: ABI + semantics
    │   │                                        + provenance + depends_on). Single source of truth for opcodes.
    │   ├── globals.toml                        ★ CANONICAL global-variable registry (hand-edited: name + category
    │   │                                        + value_domain + provenance). Single source of truth for globals/story-flags.
    │   ├── kelebek1-age-shared.cpp / -disassembler.cpp   upstream opcode-table source
    │   └── opcode-leads.json, small-script-listings.md
    │
    ├── docs/                                 all documentation
    │   ├── PROJECT-STRUCTURE.md                this file (where things live)
    │   ├── tools-reference.md                  every tool: purpose, usage, I/O (operational companion)
    │   ├── asset-resolution-re.md              resId→file RE (graphics/audio); asset-index steering
    │   ├── global-memory-re.md                 runtime global observation RE (SHELVED; future starting point)
    │   ├── remake-architecture-and-roadmap.md  THE direction doc (phases A–E)
    │   ├── phase-a-slice-plan.md               the current slice (A0/A1/A2)
    │   ├── phase-b-framework.md                natural boot/menu/session → first-gameplay framework
    │   ├── platform-portability.md             OS dependencies + future cross-platform readiness
    │   ├── vm-mapping-plan.md                  the phased decode plan
    │   ├── himegari-port-reference.md          master reference + engine background
    │   ├── name-resolution.md                  call-script + global-var name recovery (+ globals.toml registry)
    │   ├── sys4-format-notes.md                byte-level container format
    │   ├── script-inventory.md                 what the 481 scripts are
    │   ├── opcode-reference.md                 GENERATED from opcodes.toml (human-readable opcode reference)
    │   ├── global-reference.md                 GENERATED from globals.toml (human-readable global registry)
    │   ├── scjump-progression.md               SCJUMP progression decode + native decision→scene boundary
    │   └── engine-re.md                        native-engine RE (Ghidra+MCP): dispatch table, decompiled ops
    │
    ├── build/                               DERIVED (our-work-side) — generated by tools/; disposable
    │   ├── disasm/                            <NAME>.asm — human-readable disassembly, one per script
    │   ├── text/                              extracted text:
    │   │   ├── <NAME>.strings.txt               all inline strings in a script
    │   │   ├── dialogue.jsonl                   show-text lines only (the translation corpus)
    │   │   └── strings.jsonl                    every string, tagged by source opcode
    │   ├── data/                              parsed data tables (*INIT → JSON)
    │   ├── scripts-json/                      machine-readable full dumps (on demand via --json)
    │   ├── textures/                          AGF-to-BMP stills (convert_agf.py) - diagnostic pixel oracle only
    │   ├── engine-dump/                       UNPACKED engine dump (frida/dump_engine.py): range_<base>.bin + manifest.json
    │   ├── asset-index.json, asset-sections.json   asset resolver data (parse_sys4ini / resolve_asset)
    │   ├── global-var-map.{json,md}           partial global-variable name map (auto shape inference; feeds globals.toml merge)
    │   ├── globals.json                        GENERATED from globals.toml merged over global-var-map (sys4load labels + C# VM)
    │   ├── story-flags-candidates.json         GENERATED by story_flags.py (ranked story-flag review surface)
    │   ├── scjump-decisions.{json,md}          GENERATED by scjump_decode.py (progression decision table)
    │   ├── opcodes.json                        GENERATED from opcodes.toml (machine view for the C# VM)
    │   └── manifest.json, opcode-coverage.md   (opcode-coverage.md GENERATED from opcodes.toml)
    │
    ├── engine/                              DELIVERABLE — the .NET VM core (AgeEngine.sln: Age.Engine / Age.Cli / tests)
    │   └── Age.Engine/Sys4/                 runtime catalog parser, loose-first bounded ALF asset store,
    │                                       script provider, AGF/LZSS and Windows CUR decoders, and resource facade
    ├── tools/frida/                         runtime-capture + engine-dump scripts (see tools/frida/README.md)
    └── godot/                               DELIVERABLE — the Godot/C# ADV front-end (references Age.Engine),
                                            including the TITLE-only F4 debug scene launcher
```

The disposable `build/page-map-<SCENE>.jsonl` files are produced by normal Godot runs and map runtime ADV
page ordinals to their authoritative script offsets for `tools/locate_page.py`.

## Conventions

- **Three-way separation.** `姫狩りダンジョンマイスター/` = untouched originals; `extracted/` =
  game-derived data (regenerable, game-side); `age-reimpl/` = everything we authored. The first two
  are consumed, never modified.
- **Tools never hard-code paths.** `tools/paths.py` derives `GAME_DIR`, `EXTRACTED`, `DATA1`,
  `BUILD`, etc. from its own location. To point the tools at a different install, edit that one file.
  The whole tree can be relocated without touching any other tool.
- **Path references in docs** are `age-reimpl/`-relative (e.g. `tools/sys4load.py`,
  `build/text/dialogue.jsonl`) unless they name a game/extracted path explicitly.
- **Authoritative script copies:** where a script exists both as a loose `.BIN` in the game folder
  and under `extracted/DATA1/`, the game-folder copy (patch v1.03) wins. `paths.scripts()` resolves
  this automatically (overrides win).
- **`build/` and `extracted/` are disposable.** `build/` regenerates via `tools/extract_phase2.py`
  (or `sys4load.py`); `extracted/` regenerates via `bin/BinExtractALF.exe` on the `.ALF` files.
  Safe to delete and rebuild; do not hand-edit.
- **Opcode knowledge is edited ONLY in `vm-map/opcodes.toml`** (ABI + semantics + provenance +
  `depends_on`). Run `tools/opcodes_build.py --build` to regenerate the shim (`tools/age_opcodes_himegari.py`),
  machine JSON (`build/opcodes.json`), reference (`docs/opcode-reference.md`), and coverage. `--lint`
  checks dangling deps / confidence-ceiling / vocabulary. Kelebek's `tools/age_opcodes.py` stays pristine.
- **Global-variable knowledge is edited ONLY in `vm-map/globals.toml`** (name + category + value_domain +
  provenance). Run `tools/globals_build.py --build` to regenerate `build/globals.json` (sys4load labels) and
  `docs/global-reference.md`; `--lint` checks vocabulary / auto-shape≠high / dangling deps. Curated entries
  override the auto shape map (`build/global-var-map.json`). Discover story-flag candidates with `tools/story_flags.py`.
- **Encoding:** all generated text is UTF-8 (source strings are cp932/Shift-JIS, decoded on
  extraction). Run Python as `py -3.11 -X utf8`.
- **The game install is a runnable unit** — do not relocate `AGE.EXE`/`*.ALF`/DLLs relative to each
  other, or the game (and any Frida work) breaks.

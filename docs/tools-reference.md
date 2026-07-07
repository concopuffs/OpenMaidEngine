# Tools Reference

Living catalogue of every script in `tools/` — **what it does, how to run it, and what it
reads/writes**. This is the operational companion to `docs/PROJECT-STRUCTURE.md` (which is the
*where-things-live* map); when they overlap, PROJECT-STRUCTURE owns layout, this file owns
usage + I/O. Keep it current: **add a row here whenever you add a tool, and update the row
whenever a tool's inputs/outputs change.**

## Conventions (apply to every tool)

- **Run with** `py -3.11 -X utf8 tools/<name>.py …` — the `-X utf8` is required on Windows so
  cp932/Shift-JIS source text renders (and generated files stay UTF-8).
- **Paths are never hard-coded.** Every tool imports `tools/paths.py` for `GAME_DIR` /
  `EXTRACTED` / `DATA1` / `BUILD` / `VM_MAP` / `BIN`. Relocate the tree by editing only that file.
- **Generated files are never hand-edited** (they're marked ⚙ below). Edit the source, re-run
  the generator.
- **`build/` and `extracted/` are disposable** — everything under them regenerates from a tool.

## Path anchor

| Tool | Purpose | I/O |
|---|---|---|
| `paths.py` | ★ Single path anchor — derives all workspace dirs from its own location; `paths.scripts()` returns the override-aware `{NAME.BIN → path}` corpus map (loose game-folder patches shadow `extracted/DATA1`). | *Imported, not run.* |

## Container parse / disassemble

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `sys4load.py` | Loader + opcode-decoding disassembler for SYS4 `.BIN` scripts (the container-format core every other tool builds on). | `sys4load.py <file.BIN>` · `--summary` · `--strings` · `--json` · `sys4load.py <dir> --validate` (corpus check) | `.BIN` + `age_opcodes*.py` + `build/global-var-map.json` → stdout listing, or `build/scripts-json/` with `--json` |
| `age_opcodes.py` | 548-entry Kelebek AGE opcode/arg-type table. **PRISTINE upstream data — never edit.** | *Imported.* | — |

## Opcode reference toolchain — single source of truth = `vm-map/opcodes.toml`

All opcode knowledge (ABI, semantics, provenance, `depends_on`) is hand-edited **only** in
`vm-map/opcodes.toml`. Everything else is generated from it.

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `opcodes_build.py` | Generator + linter for the opcode reference. | `--build` · `--lint` · `--bootstrap` | `vm-map/opcodes.toml` → ⚙ `tools/age_opcodes_himegari.py`, ⚙ `build/opcodes.json`, ⚙ `docs/opcode-reference.md`, ⚙ `build/opcode-coverage.md` |
| `opcodes_model.py` | In-memory model + loader + linter (dangling-ref / confidence-ceiling / vocabulary / dependents). | *Imported by `opcodes_build.py`.* | `vm-map/opcodes.toml` → — |
| `test_opcodes.py` | Unit tests for the opcode tooling. | `test_opcodes.py` | — |
| `opcode_context.py` | Read-only evidence gatherer for classifying unnamed opcodes (frequency, argc, operand-type signature, neighbours, disasm snippets, Kelebek comment). | `--top 20` · `opcode_context.py 0x1f4 0x71 …` | corpus → stdout |
| `validate_opcode_table.py` | Definitive decode-coverage validator (replicates Kelebek's `data_array_end` code/data split). | `validate_opcode_table.py` | corpus → stdout |
| `validate_opcode_table_naive.py` | Naïve variant of the above (baseline comparison). | `validate_opcode_table_naive.py` | corpus → stdout |
| `age_opcodes_himegari.py` | ⚙ Inferred Himegari opcode semantics — **generated; do not hand-edit.** | *Imported by `sys4load.py`.* | — |

## Extraction / data corpora

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `extract_phase2.py` | Batch: disassembly + text corpora for every script. | `extract_phase2.py` | corpus → `build/disasm/*.asm`, `build/text/{dialogue.jsonl,strings.jsonl,*.strings.txt}`, `build/manifest.json` |
| `extract_init.py` | Parse a `*INIT` data table (auto-detects name / numeric / footer shape). | `extract_init.py <TABLE> [OUTNAME] [--mode …]` | `<TABLE>.BIN` → `build/data/<OUTNAME>.json` |
| `global_map.py` | Build the partial global-variable name map from static evidence. | `global_map.py` | corpus + `build/data/` → `build/global-var-map.{json,md}` |

## VM

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `vm0.py` | Headless Python bytecode VM (Phase A0 execution-model prototype; reuses `sys4load`). | `--test` (RECOVER unit test) · `--sweep [N]` (oracle coverage) · `--scene NAME` · `--settex NAME` (set-texture resId trace + exec trace) · `<file.BIN>` | corpus → stdout; `build/vm0-trace.json`; `build/settex-<NAME>.json` |
| `correlate_scope.py` | Align the VM's `set-texture(resId)` trace with the game's Frida load order → tag each load's DATA2 package, flag package transitions, dump the significant ops in each transition span (the **scope selector** hunt). | `correlate_scope.py <SCENE>` | `build/settex-<SCENE>.json` + `build/frida-load-order-result.json` + index → stdout |

## Asset resolution / graphics

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `parse_sys4ini.py` | Parse `SYS4INI.BIN` (S4IC422, LZSS-compressed) into the authoritative asset index — name ↔ archive ↔ offset ↔ size for all DATA*.ALF (the `resId→file` answer key). | `parse_sys4ini.py [--check]` (`--check` validates vs `extracted/` + `.ALF` sizes) | `姫狩り…/SYS4INI.BIN` → `build/asset-index.json` |
| `resolve_asset.py` | ★ **The static asset resolver.** SYS4INI is sectioned (one per scene: `SCxxxx.BIN` + its cross-archive manifest; `file_number` = index within section). Resolves `resId → files[section_base(scene) + resId]` for graphics AND audio, no capture. | `resolve_asset.py --build` · `resolve_asset.py <SCENE> [resId]` | `build/asset-index.json` → `build/asset-sections.json`; resolves any (scene, resId) |
| `resolve_frida_reads.py` | Rescue noisy Frida archive-read offsets → asset names via the index (per-archive range search; drops 0x20000 paging reads); recovers the per-scene asset load order. | `resolve_frida_reads.py [reads.log] [-o out.json]` | `build/frida-reads.log` + `build/asset-index.json` → `build/frida-asset-loads.json` |
| `convert_agf.py` | Convert AGF stills to BMP via `AGF2BMP2AGF.exe` (searches all `extracted/DATA*`). `--scene` batch-converts a scene's whole SYS4INI manifest — feeds the Godot render. | `convert_agf.py EV052CA.AGF …` · `convert_agf.py --scene SC0000` | `extracted/DATA*/*.AGF` → `build/textures/*.BMP` |

## Runtime capture (Frida)

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `tools/frida/capture_graphics.py` | Attach Frida to the running game; log archive reads/opens (ground-truth for asset resolution). See `tools/frida/README.md`. | `py -3.11 -u -X utf8 tools/frida/capture_graphics.py [AGE.EXE]` | running game → `build/frida-reads.log`, `build/frida-opens.log` |
| `tools/frida/capture_load_order.py` | **Primary asset-resolution capture:** recover a scene's per-asset load order from exact-start `ReadFile` reads → names via the index; confirms `resId==file_number`. Attach; replay scene; `--analyze`. | `py -3.11 -u -X utf8 tools/frida/capture_load_order.py [pid]` · `--analyze` | running game + index → `build/frida-load-order.jsonl`, `…-result.json` |
| `tools/frida/locate_resource_load.py` | Phase-1 locator: back-traces asset-opens to find the native AGF load chain (`0x16d5d7→0x74f1f`). | `py -3.11 -u -X utf8 tools/frida/locate_resource_load.py [pid]` · `--aggregate` | running game → `build/frida-resource-bt.jsonl` |
| `tools/frida/capture_resid_args.py` | Phase-2 probe: dumps the decoder's args / context / caller frame (established the loader carries only offsets, not names). | `py -3.11 -u -X utf8 tools/frida/capture_resid_args.py [pid]` · `--analyze` | running game → `build/frida-resid-args.jsonl` |
| `tools/frida/find_globals_base.py` | Runtime-global RE (SHELVED — see `docs/global-memory-re.md`): flat-int32 signature scan for the VM global array. Finds nothing → layout isn't flat. | `--build-sig` · `py -3.11 -u -X utf8 tools/frida/find_globals_base.py [pid]` | `*INIT` → `build/globals-signature.json`; scans running game |
| `tools/frida/find_global_by_sequence.py` | Runtime-global RE (SHELVED): differential resId value-scan + stability filter. Finds stack proxies; proved `G[0x62424]` is a transient arg-register. | `py -3.11 -u -X utf8 tools/frida/find_global_by_sequence.py [pid]` | running game + index → stdout |

## Historical / one-off

| Tool | Purpose |
|---|---|
| `probe_*.py` (`probe_header`, `probe_leads`, `probe_refs`, `probe_tables`, `probe_tags`, `probe_types`, `probe_xref`) | Container/opcode format-RE probes used to reverse the format originally. Kept for reproducibility; not part of the normal workflow. |
| `pack_check.py` | Checks whether `AGE.EXE` is packed (it is). No longer a blocker — we run our own VM. |

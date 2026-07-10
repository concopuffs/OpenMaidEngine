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
| `sys4load.py` | Loader + opcode-decoding disassembler for SYS4 `.BIN` scripts (the container-format core every other tool builds on). Annotates global operands (`build/globals.json`) and **`call-script` targets by name** (`build/callscript-names.json`, e.g. `call-script 0x1ab =ADDITEM.BIN`). | `sys4load.py <file.BIN>` · `--summary` · `--strings` · `--json` · `sys4load.py <dir> --validate` (corpus check) | `.BIN` + `age_opcodes*.py` + `build/globals.json` + `build/callscript-names.json` → stdout listing, or `build/scripts-json/` with `--json` |
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
| `globals_build.py` | Merge curated `globals.toml` over the auto shape map; generate the global registry + linter. | `--build` · `--lint` | `vm-map/globals.toml`, `build/global-var-map.json` → ⚙ `build/globals.json`, ⚙ `docs/global-reference.md` |
| `story_flags.py` | Static story-flag miner (branch-condition mining) + `--bootstrap` skeleton seeding. | `story_flags.py` · `--bootstrap` | corpus, `build/global-var-map.json` → ⚙ `build/story-flags-candidates.json`, appends `vm-map/globals.toml` |
| `test_globals.py` | Unit tests for the globals registry + story-flag miner. | `test_globals.py` | — |
| `scjump_decode.py` | Decode SCJUMP's progression logic → decision table; `--verify` VM cross-check. | `scjump_decode.py` · `--verify` | SCJUMP.BIN, `build/globals.json` → ⚙ `build/scjump-decisions.{json,md}` |
| `test_scjump.py` | Unit tests for the SCJUMP decoder. | `test_scjump.py` | — |

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
| `scene_opcode_coverage.py` | Per-scene opcode completeness gauge: histograms a scene's static opcodes and classifies each **impl** / **safe-noop** / **GAP** (effectful op the VM silently stubs). Implemented set parsed from `VirtualMachine.cs` `case` arms; metadata from `opcodes.json`. Surfaces the concrete rendering/feature holes so a half-drawn scene reads as "N ops still stubbed", not "mystery". | `scene_opcode_coverage.py [SCENE …]` (default SC0000) | corpus, `build/opcodes.json`, `engine/…/VirtualMachine.cs`, `build/callscript-names.json` → ⚙ `build/scene-opcode-coverage/<SCENE>.md` + stdout |
| `correlate_scope.py` | Align the VM's `set-texture(resId)` trace with the game's Frida load order → tag each load's DATA2 package, flag package transitions, dump the significant ops in each transition span (the **scope selector** hunt). | `correlate_scope.py <SCENE>` | `build/settex-<SCENE>.json` + `build/frida-load-order-result.json` + index → stdout |
| `diff_optrace.py` | **Differential offset-path oracle** (`docs/engine-re.md`): diff the engine's executed offset path (`trace_engine_ops.py`) against the VM's (`Age.Cli trace --trace-json`) → first divergence = the mis-modeled branch/op/state, with opcode + ±3 ops of context. Identifies the scene's codebase by longest-common-prefix; filters the VM trace to argc≥1 (operand-capture parity). Pure core unit-tested (`test_diff_optrace.py`). | `py -3.11 -X utf8 tools/diff_optrace.py SC0000 [--full]` | `build/engine-optrace.jsonl` + `build/vm-optrace.json` + disasm → stdout |

## Engine (C#) — VM core, CLI, Godot frontend

The `engine/` .NET solution (`AgeEngine.sln`) is the runtime VM; `godot/` is the ADV frontend. Not
Python, but listed here as the things you *run*. Build: `dotnet build engine/AgeEngine.sln`; test:
`dotnet test engine/AgeEngine.sln`. Run a CLI command: `dotnet run --project engine/Age.Cli -- <cmd>`.

**call-script executes** on the product paths: they inject `Sys4ScriptProvider` (id→`.BIN`, via
`build/callscript-names.json`), so `call-script <id>` loads & runs the target as a nested subroutine
frame sharing globals. `trace`/`audio`/`gfx` stay **provider-less** (call-script stubbed) — base-ISA /
subsystem oracles. Test scenes are **synthesized** via `Age.Engine/Sys4/ScriptAssembler` (see
[[testing-synthesize-dont-disable]]: synthesize test data, never disable a feature to keep a golden green).

| Command | Purpose | Notes |
|---|---|---|
| `run <file.BIN>` | Execute a script; print steps, show-text count, **call-script dispatch count**, the first 30 lines (each tagged with its source script), and the distinct source scripts. | `CaptureHost` (headless); **executes call-script**. |
| `trace <out.json>` | Trace every SC/SP scene → offsets + halt + steps. **Provider-less** (call-script stubbed) = a base-ISA offset dump. | writes JSON. (Was the vm0 differential oracle; vm0 is retired from oracle duty — `TraceDiffTests` removed.) |
| `trace <SCENE.BIN> [--boot] [--state <f>] [0xADDR=VAL…] --trace-json <out>` | ★ Emit the **full per-op executed-offset path** of one scene (not just show-text), filtered to the scene's own frame — the VM side of the differential offset-path oracle (`diff_optrace.py`). `--boot` runs the SYSTEM4 state prefix; **`--state <f>` loads a captured scene-entry snapshot** (`capture_global_writes.py`) = the engine's real pre-scene state; `0xADDR=VAL` hand-seeds. | `JsonOffsetTraceSink` (observe-only, parity held) → `{scene, offsets:[…]}` JSON. |
| `audio <SCENE.BIN> [0xADDR=VAL…]` | Dump executed `play-bgm`/`play-voice` in order + resolved file. | optional seeds. provider-less (stub) for now. |
| `gfx [--boot] <SCENE.BIN> [0xADDR=VAL…]` | Dump executed `set-texture`/`get-texture-size`/`draw-texture` (resolved file + computed geometry) **plus the per-object gfx slots** — the headless geometry oracle. **`--boot`** runs SYSTEM4's state prefix (`INITCONFIG/INIT2/INIT`) via `GameSession` first (so INIT2's gfx handle array is present) and runs the target with call-script on; without it, seeds-only + provider-less. | gfx ops now execute against `GfxState`. |
| `play [--boot] [--state <f>] [--save-state <f>] <SCENE.BIN…> [0xADDR=VAL…]` | ★ Cross-scene **state runner**: run a scene sequence carrying persistent globals. `--boot` first runs the 9 `*INIT` data scripts (real skill/item/unit/map/stage state). `--state`/`--save-state` load/persist a JSON snapshot. | `GameSession`; **executes call-script**. |
| `sweep [--boot] [0xADDR=VAL…]` | Corpus-scale run. **With call-script execution on: 284/297 exit, 13 STEP-LIMIT** (input/state-gated ADV scenes spin headless once subroutine global-writes drive their loops — state divergence, not a bug; 0 depth-cap/unresolved). **With seeds = a story-state explorer**: reports which scenes' dialogue changes ±seed (e.g. form flag `0xa57=1` → 34/297 scenes). | |

**Faithful headless vs plow (`HaltAtWaitForInput`)** — headless has no player, so op `0x72 wait-for-input`
either **halts** ("the scene is waiting; with no input, stop here") or is ignored (**plow** — walk every page).
Plow is a *fiction*: it runs past every prompt into code no real playthrough reaches — e.g. a plowed
`SC0000` fell through 166 prompts into the name-entry poll loop and spun `sleep 1` **493k×** to STEP-LIMIT.
So: **`run`/`play` HALT at the first `wait-for-input` by default** (faithful; `SC0000` stops at ~402 steps,
0 sleeps — matching the real run's path to the first prompt), with **`--plow`** to opt into full-page
coverage. **`sweep` PLOWS by default** (it *is* the dialogue-coverage oracle: 284 exit / 13 STEP-LIMIT),
with **`--halt-at-wait`** to opt into faithful mode (then all 297 scenes halt cleanly at their first prompt
— 0 STEP-LIMIT). Interactive Godot is unaffected (it really blocks on input; flag stays false there).

**`--trace [--trace-file <path>] [--trace-steps]`** (on `run`/`play`/`sweep`): stream the engine's own
diagnostic events over the `Age.Engine.Diagnostics.ITraceSink` seam — scene/subroutine frame enter+exit
(indented by call depth), call-script dispatch with resolved name, and the final halt+step count — to
console or a file. Add `--trace-steps` for per-instruction opcode/arg + stub-op detail (high volume;
gated). Absent ⇒ no tracing (`NullTraceSink`, byte-identical run). This is an **engine** fact stream:
frontends consume it instead of reimplementing a diagnostic `IHost`. Example: `play SC0000.BIN --trace`
shows `» SC0000.BIN (enter, TopScene)` → `call-script 0xee =INPUTNAME.BIN (resolved)` → `halt: …`.

**Aggregating / filtered diagnostics** (added 2026-07-08 after a full `--trace-steps` dump proved unusable
at 2.5M lines). All observe-only → parity preserved; all on `run`/`play`/`sweep`:
- **`--trace-histogram`** — instead of a per-line dump, aggregate **execution counts per opcode** and per
  **call-site `(script:pc)`** (with a sample first operand), dumped sorted after the run. Answers "how many
  times did op X run, and from where?" directly. This is what pinpointed, in one line, that the 493,175
  `sleep`s in a headless `play` come from `INPUTNAME.BIN:0x1c3` — a name-entry input-poll loop that spins
  only because headless has no keyboard — not from the opening. Step lines are attributed to the **real
  running script** (nested call-script frames included), the "which script is this pc in?" answer a bare
  step trace can't give.
- **`--trace-ops <csv>`** — filter the text trace to only the named ops (mnemonics or `0x` hex, e.g.
  `--trace-ops sleep,draw-texture,wait-for-input`), each line tagged `script:pc`. The ordered interleaving of
  a few ops of interest without the flood.
- `--trace-file <path>` now creates the parent directory if missing.
- **Godot** accepts **`--trace-histogram <file>`** — profile the **real** run (headless flow diverges because
  `wait-for-input` is a no-op there; the real run to page 1 is ~562 steps with **0** sleeps vs headless's 2M
  steps / 493k sleeps). Dumped when the scene ends or the window closes. e.g.
  `godot --path godot -- --boot --shot out/p1.png --trace-histogram out/hist.txt`.

**Godot frontend** (`S:/Godot/Godot_v4.7…`; project = `godot/`). Toolchain: `godot --headless --path godot
--import` → `dotnet build godot/Himegari.csproj` → `godot [--headless] --path godot [-- <userargs>]`.
Plays the real bytecode with call-script execution on (subroutines run live). `--headless` can't render
texture ops (no GPU context) — run windowed for real scenes. User args (after `--`):
- `--scene <NAME>` — which scene to play (default `SC0000`), e.g. `--scene SC0240` (executes 29 nested subroutines).
- `--selftest` — headless; runs a **synthesized** scene through the thread/suspend/`CallDeferred` plumbing and asserts it matches a live headless run (full handling; no vm0/frozen golden). Exits.
- `--seed 0xADDR=VAL` (repeatable) — seed initial global state, e.g. `--seed 0xa57=1` unlocks Lily's form-A voiced dialogue.
- `--boot` — run SYSTEM4's state prefix (`INITCONFIG/INIT2/INIT`) via `GameSession` before the scene, so scene-assumed boot state (chiefly INIT2's gfx handle array) is present. **Needed for the gfx CGs to render** (without it the opening event CGs collapse/drift). e.g. `godot --path godot -- --boot`.
- `--shot <png> [--shot-page N]` — capture page N to a PNG then quit (dev screenshot). At scene end it also prints the call-scripts executed as nested frames.
- `--shot-sequence <dir> [--frames N]` — dump one PNG per rendered frame (`frame_0000.png…`, default N=180 ≈ 3s @60fps) then quit, auto-advancing past input waits. Verifies **time-based (sleep-paced) effects** — e.g. the opening `AE*` burst — as distinct frames, which a single `--shot` cannot. CPU/IO-heavy by design (a PNG every frame); a dev diagnostic, not a normal run. e.g. `godot --path godot -- --boot --shot-sequence out/seq --frames 300`.
- `--sleep-scale <f>` — multiply every `sleep` (op 0xc8) duration by `f` (default 1.0). The authentic opening burst is only ~2 s, too fast to eyeball live; `--sleep-scale 5` stretches it to ~10 s so the paced sequence (arcane `AE*` → character CGs → settled BG) is watchable. Debug-only; leave at 1.0 for real playback.
- `--speed <f>` — scale the unified runtime clock (VM cadence, sleeps, and retained animation) without auto-advancing input waits. Values 0.05–8 are accepted; `--speed 0.25` is useful for transform inspection, while 1.0 is normal playback.
- `--gfx-log <file>` — **compositor + op diagnostic** (the tool that root-caused the grey background). Logs, per rendered frame, only the objects whose draw outcome **CHANGED** (drawn↔skip↔gone, resId, resolved file, `slot`, `src`/`dst`, `op`acity, `tintStr`ength) — quiet until something actually changes, so the exact frame a layer drops out (and why) stands out. Also traces every `set-texture`/`create-texture` **slot assignment** (via `GodotAdvHost.TraceOps`). Works live or with `--shot-sequence`. Use it before theorising about layering/blend/geometry: it showed the grey BG = the slot-selecting globals resolving to 0 → every texture collapsing into slot 0 (see engine-re.md §"Grey-background root cause"). e.g. `godot --path godot -- --boot --gfx-log out/gfx.log` then click to the bad page.
  Matrix-channel outcomes also include `base`, `anchor`, projected `dst`, sampled `scale`/`trans`, and
  one-shot-plus-cyclic `rot`ation angles. Parent directories are created automatically.
- `--transition-click-ms <n>` — diagnostic-only input injector: after a foreground transition has been active for `n` virtual milliseconds, send one click through the real input lifecycle. The click completes/consumes the transition and does not advance a stable page. Use with `--timeline-log`, `--gfx-log`, and windowed `--shot-sequence`; omit for normal play.
- `--timeline-log <jsonl>` — diagnostic-only synchronized event stream for a real Godot run. Records every
  executed script byte offset/opcode, virtual time/frame, VM state changes (`running`, `sleep`, `input-wait`,
  `halted`), BGM events, and changed visible-object compositor outcomes in one ordered JSONL file. Combine with
  `--boot --shot-sequence ... --gfx-log ...` to distinguish control-flow stalls from retained-object/compositor
  failures at an exact bytecode boundary. Relative output paths are project-relative (`godot/`).

## Asset resolution / graphics

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `tools/frida/capture_native_transforms.py` | Capture native `0x21f`/`0x223`/`0x234` worker operands, corrected integer base/anchor coordinates, all one-shot/cyclic retained fields, the one-shot 4×4 matrix, and the final post-cyclic 4×4 matrix. Optional handle filter; read-only. | `py -3.11 -u -X utf8 tools/frida/capture_native_transforms.py [secs] [pid|AGE.EXE] [--handle 0xHANDLE]` | running game → `build/native-transform-trace.jsonl` |
| `parse_sys4ini.py` | Parse `SYS4INI.BIN` (S4IC422, LZSS-compressed) into the authoritative asset index — name ↔ archive ↔ offset ↔ size for all DATA*.ALF (the `resId→file` answer key). Each entry carries `raw_index` (its 0-based position in the SYS4INI record table incl. `@` placeholders) = the engine's universal file id. Also emits the **`call-script <id> → name`** map (id = `raw_index`; see `engine-re.md`). | `parse_sys4ini.py [--check]` (`--check` validates vs `extracted/` + `.ALF` sizes) | `姫狩り…/SYS4INI.BIN` → `build/asset-index.json` + `build/callscript-names.json` |
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
| `tools/frida/dump_engine.py` | ★ **Dump the UNPACKED engine code** from the live process for offline static RE (native handlers). `AGE.EXE` unpacks in-place at `0x400000`; Kelebek VAs map `VA−0x400000` = file-off. Validated via the AGF-decoder landmark `+0x74f1f`. | `py -3.11 -u -X utf8 tools/frida/dump_engine.py [pid]` | running game → `build/engine-dump/{manifest.json,range_<base>.bin}` |
| `tools/frida/map_imports.py` (+ `map_imports_full.py`) | ★ **Name dynamically-resolved Win32 APIs** in the Ghidra image. Read-only: maps live-process module exports → `{addr→dll!Func}`, scans the `0x400000` module for pointer matches → `RVA→name` (ASLR-stable). `--recon` = clustering report (the gate); default writes the map. Applied to `/v2` via a `run_script_inline` pass → 248 `imp_<dll>_<func>` labels at the RVA `0x16f000` IAT (validated: CreateFileA/SetFilePointer/timeGetTime). Pure scan/cluster logic unit-tested (`test_map_imports.py`). | `py -3.11 -u -X utf8 tools/frida/map_imports.py [--recon]` | running game → `build/import-map.json` (+ `-singletons.json`) |
| `tools/frida/probe_handlers.py` | Probe which region the interpreter executes from (module vs heap). Confirmed: **operand-fetch `+0x1b940` fires ~8500/s ⇒ interpreter runs from the module `0x400000`** (handlers hookable by dump address). | `py -3.11 -u -X utf8 tools/frida/probe_handlers.py [pid]` | running game → stdout (per-hook fire counts) |
| `tools/frida/capture_gfx_objects.py` | Capture the native gfx object-manager state: grab engine ctx (`esi` via operand-fetch `ecx`), poll the object-record array `[esi+0x53d64]` (20×120B; `field[0]=0xffffffff`=free, cmd-type at rec+0x24). **⚠ Its "0 CG records ⇒ drift is state-divergence" reading was DISPROVEN** (Ghidra: op 0x215 read settles the drift as a native command-buffer op — `docs/engine-re.md`; the poll observed the record array, not the lookup map that drives the branch, and cmd-buffer records are transient). Kept as a runtime-observation tool. | `py -3.11 -u -X utf8 tools/frida/capture_gfx_objects.py [pid] [secs]` | running game → `build/gfx-objects.jsonl` |
| `tools/frida/probe_frame_cadence.py` | **Frame-cadence probe** (`docs/engine-re.md` "Frame cadence — live measurement"): plain-JS hook on operand-fetch `0x41b940` (grab ctx + count operand reads) + system-DLL message/timing hooks; auto-buckets by Ctrl/skip-bit. Measured ~1,788 **operand fetches/sec** normal, ~4× fast-forward; this is not an opcode count. **Read-only/import-only — never CModule-hook the hot interpreter (crashes the game).** Play actively during capture; hold Ctrl the back half. | `py -3.11 -u -X utf8 tools/frida/probe_frame_cadence.py [secs] [proc]` | running game → `build/frida-frame-cadence.jsonl` + stdout report |
| `tools/frida/probe_present.py` | **Present-rate probe:** grab ctx, scan it for the D3D9 device (d3d9-vtable object with a full ~119-method table), hook `IDirect3DDevice9::Present`/`EndScene` (+ GDI-blit fallback). Found: **D3D9, UNCAPPED** (`Present` ~1908/sec, no vsync; no `ddraw`; 2D StretchRect compositor) ⇒ no fixed frame rate. Click 2–3× at start to grab ctx. | `py -3.11 -u -X utf8 tools/frida/probe_present.py [secs]` | running game → `build/frida-present.jsonl` + stdout report |
| `tools/frida/trace_engine_ops.py` | **Engine op-path tracer** for the differential oracle (`docs/engine-re.md` "Differential offset-path oracle"): per executed op, read `cur_ctx_index@0x53d14`/`frame_pc@0x53d2c`/`frame_codebase@0x53d28` → emit `(codebase, offset=(pc−codebase)/4)`. **Use `--hook operand` (0x41b940, proven-safe)** — `--hook tick` (0x410fb0) sees `ecx≠ctx` (0 entries). Writes `build/tracer-live.flag` when the hook is installed → launch in the background, gate the New-Game trigger on the flag (else the scene-entry burst is missed). | `py -3.11 -u -X utf8 tools/frida/trace_engine_ops.py [--hook operand\|tick] [secs]` | running game → `build/engine-optrace.jsonl` |
| `tools/frida/capture_global_writes.py` | **Scene-entry state capture** → auto-seed for single-scene runs (`docs/engine-re.md` "Scene-entry state snapshot"). Hooks `vm_operand_write@0x425fb0` and logs `(codebase, index, PLAINTEXT value)` for global-ints (the helper sees the value before the obfuscated store — no de-obfuscation needed). **`--spawn` captures from boot** (packer-aware: polls until `0x425fb0` unpacks, then attaches; kills the spawned pid on setup failure so no suspended orphan). `--attach` = partial (misses pre-attach writes). Validated: a real boot→New-Game→SC0000 capture seeds the VM to match the engine's whole opening. | `py -3.11 -u -X utf8 tools/frida/capture_global_writes.py --spawn [secs]` | running/spawned game → `build/global-writes.jsonl` (raw) + `build/scene-entry-state.json` (GameSession snapshot) |

*(Static disassembly of `build/engine-dump/range_00400000.bin` uses **capstone** — `py -3.11 -m pip install capstone`; VA `X` → file offset `X−0x400000`.)*

## Native engine RE (Ghidra)

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `ghidra_handler_map.py` | Extract the opcode→real-handler dispatch table (`handler(op)=ctx[0x26c93+op]`) from `FUN_00413860`'s override stores — the general fix for Kelebek VA-drift. `--check` diffs derived handlers vs `opcodes.toml` prose (found 0 real drift). Feeds the one-shot Ghidra annotation pass that names every handler `op_0xNN_handler` (see `docs/engine-re.md` "Materialized + applied image-wide"). | `ghidra_handler_map.py build/engine-dump/FUN_00413860.disasm.txt [--check]` | ⚙ `build/engine-dump/FUN_00413860.disasm.txt` (from ghidra-mcp `disassemble_function(0x413860)`) → ⚙ `build/op-handler-map.json` |
| `test_ghidra_handler_map.py` | Unit tests for the dispatch-table parser (plain runner, no pytest). | `test_ghidra_handler_map.py` | — |
| `engine_ctx_build.py` | Build the `EngineCtx` struct artifacts from `vm-map/engine-ctx.toml` (canonical `ctx`-field registry). `--lint` = overlap/OOB/dup/type checks. The struct is then applied to the `/v2` image via `run_script_inline` (creates `EngineCtx`, retypes all dispatch-handler `this` → `EngineCtx *`) so handlers decompile `ctx->field` not `param_1+0x…`. Grows one `[[field]]` at a time. | `engine_ctx_build.py --build` · `--lint` | ⚙ `vm-map/engine-ctx.toml` → ⚙ `build/engine-ctx.json`, ⚙ `docs/engine-ctx-reference.md` |
| `test_engine_ctx.py` | Unit tests for the ctx builder (load/lint/emit; plain runner). | `test_engine_ctx.py` | — |

## Historical / one-off

| Tool | Purpose |
|---|---|
| `probe_*.py` (`probe_header`, `probe_leads`, `probe_refs`, `probe_tables`, `probe_tags`, `probe_types`, `probe_xref`) | Container/opcode format-RE probes used to reverse the format originally. Kept for reproducibility; not part of the normal workflow. |
| `pack_check.py` | Checks whether `AGE.EXE` is packed (it is: entropy-8 code sections, zeroed IAT). `SYS4AB.BIN` is NOT a separate image — it's `XOR-0xFF(AGE.EXE)` byte-for-byte (0x2c header + XOR payload). The unpacked engine exists only in memory → dump it with `frida/dump_engine.py`. |

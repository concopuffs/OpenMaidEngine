# GFX Command-Buffer Subsystem Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the background/sprite render drift (`Screenshot 2026-07-06 211353.png`) by modelling the AGE engine's native graphics command-buffer host-side and *executing* the gfx ops instead of stubbing them, so per-object slot selection and geometry are correct.

**Architecture:** The drift is settled as **(b) a native command-buffer op** (`docs/engine-re.md`, op `0x215`): the gfx ops maintain a native **object-record array** (`ctx+0x53d64`, stride `0x78`) plus a **handle→object registry** (a hash map) that `0x215` queries to pick each object's slot; stubbing collapses every draw onto slot 0. Because the VM is ours, we reproduce this as a clean **host-side model** behind `IHost` (the same pattern as `GetTextureSize`): the gfx ops call into a shared `GfxState`, and `label_12649`'s bytecode geometry math — already correct — then produces correct `dst`/`w`/`h`. The registry inserts are bytecode-driven, so the state rebuilds itself from the same scripts; **no Frida, no state-seeding**.

**Tech Stack:** C# / .NET 8 (`engine/`, solution `AgeEngine.sln`, xUnit); Godot 4.7 .NET (`godot/Himegari.csproj`); Python 3.11 for the opcode build (`tools/opcodes_build.py`); Ghidra 12.1.2 + ghidra-mcp for the RE phase (engine dump `build/engine-dump/range_00400000.bin`, base `0x400000`).

## Global Constraints

- **This is a discovery-gated plan.** Phase 1 (RE) is fully specified and executable now; it produces the **op-contract table** that is the spec for Phases 3–4. **Phase 2 is a re-plan checkpoint:** after Phase 1, expand Phases 3–4 into bite-sized TDD tasks with exact code (a second `writing-plans` pass) — do not write handler code before its contract is reversed.
- **Run Python as** `py -3.11 -X utf8 tools/<name>.py …` (utf8 mode mandatory on Windows for cp932).
- **Never hand-edit generated files.** After editing `vm-map/opcodes.toml`, run `py -3.11 -X utf8 tools/opcodes_build.py --build` (regenerates `tools/age_opcodes_himegari.py`, `build/opcodes.json`, `docs/opcode-reference.md`, `build/opcode-coverage.md`); `--lint` must report `0 errors`.
- **Ghidra VA-drift rule:** never analyse a native op by its Kelebek `u00XXXXXX` VA. Resolve the real handler through the dispatch table **`ctx[0x26c93 + op]` = the store `[ESI + (0x26c93+op)*4] = <handler>` in `FUN_00413860`** (`docs/engine-re.md` master key).
- **Engine seam rule:** `Age.Engine/Vm` references only `Model` + `Hosting` + `Diagnostics` (never `Sys4`). Gfx state lives **host-side**, reached through `IHost`. `IHost` lives in `Age.Engine/Hosting`.
- **Parity guardrail:** non-Godot hosts (`CaptureHost`, test/CLI recording hosts) must keep any new `IHost` method a pure value-returning no-op that leaves trace/step-count unchanged, so existing base-ISA tests stay byte-identical. Verify with `dotnet test engine/AgeEngine.sln`.
- **Testing principle (user-directed):** synthesize test data; never disable a feature to keep a real scene matching a frozen number. Use `Age.Engine/Sys4/ScriptAssembler` to build synthetic gfx scenes for unit tests.
- **Opcode `category` vocabulary** is fixed: `marker/structural/control/adv/draw/audio/input/compute/unknown`. Gfx ops = `draw`. **Confidence:** `high` only once the handler is Ghidra-read; `med` for inference.
- **Canonical doc homes** (do not duplicate): native-op decode → `docs/engine-re.md`; opcode semantics → `vm-map/opcodes.toml`; slice status/results → `docs/phase-a-slice-plan.md`; host-side design → this plan's Phase-2 spec.

---

## File structure

| File | Responsibility | Phase |
|---|---|---|
| `docs/engine-re.md` | Canonical **op-contract table** for the gfx command-buffer family (per-op: handler VA, cmd-type, record/registry effects, operand roles) | 1 |
| `vm-map/opcodes.toml` | Per-op `semantics` for each reversed gfx op (summary/evidence/source/confidence) | 1 |
| `docs/superpowers/specs/2026-07-07-gfx-command-buffer-design.md` | Host-side model design (record set, registry, `IHost` surface, compositor) — written from the Phase-1 contract | 2 |
| `engine/Age.Engine/Hosting/GfxState.cs` | **New.** Host-side command-buffer model: object-record set + handle→object registry; the ops mutate/query it | 3 |
| `engine/Age.Engine/Hosting/IHost.cs` | New gfx-op methods (register/query/geometry), mirroring the `GetTextureSize` pattern | 3 |
| `engine/Age.Engine/Vm/VirtualMachine.cs` | Dispatch the reversed gfx ops to the new `IHost` methods (replace `OnStub` fall-through) | 3 |
| `engine/Age.Engine/Hosting/CaptureHost.cs` + test/CLI hosts | Parity no-op impls of the new `IHost` methods | 3 |
| `engine/Age.Cli/Program.cs` (`GfxTraceHost`) | Drive the real `GfxState` so `Age.Cli gfx` reports true per-object geometry | 3 |
| `godot/GodotAdvHost.cs`, `godot/Main.cs` | Composite from the `GfxState` object set (per-object slots) instead of collapsing to slot 0 | 3 |
| `engine/Age.Engine.Tests/GfxCommandBufferTests.cs` | **New.** Synthetic-scene tests: register→query returns the object; two different-sized bgs don't drift | 3 |

---

## Phase 0 — Branch

- [ ] **Step 1: Create the working branch**

Run: `cd age-reimpl && git checkout -b feat/gfx-command-buffer`
Expected: `Switched to a new branch 'feat/gfx-command-buffer'`

---

## Phase 1 — Reverse the gfx command-buffer op contract (executable now)

**Deliverable:** a complete **op-contract table** in `docs/engine-re.md` covering every still-stubbed gfx-family op, plus updated `vm-map/opcodes.toml` semantics and Ghidra annotations. This is the spec for Phases 3–4. No C# is written in this phase.

**Target ops** (the `draw`-category + adjacent handlers that build/query the command buffer; `0x215` is already done as the worked example):
`0x1a2` (gfx-cmd-register, cmd-type 3), `0x1f7` (ui-elem?), `0x1fa` (ui-clear?), `0x1ff` (draw?), `0x202` (draw-blit?), `0x203` (draw?), `0x212` (`u00421090`), `0x213` (`u004210D0`), `0x216` (`u004211A0`), `0x217`/`0x218`/`0x21a` (gfx-geom?), `0x219` (`u004212E0`).
Already implemented (read only if a contract dependency surfaces): `0x1f8` create-texture, `0x1f9` set-texture, `0x1fb` draw-texture, `0x208` get-texture-size.

**Per-op procedure** (repeat for each target op; `0x215` in `engine-re.md` is the template):

1. **Resolve the real handler.** Compute the table byte-offset `off = (0x26c93 + op) * 4`. In Ghidra (mcp) run `search_instructions` with `operand_pattern` = that offset in hex (e.g. op `0x217` → `(0x26c93+0x217)*4 = 0x9baac`) — the single match in `FUN_00413860` is `MOV dword ptr [ESI + 0x…], <handler_va>`. `<handler_va>` is the real handler.
2. **Decompile** the handler (`decompile_function <handler_va>`) and, if it shares code, the operand helpers (`FUN_0041b940` operand-fetch, `FUN_00425fb0` operand-write, `FUN_0047f280` registry-find, `FUN_0042cf70` registry-insert are already identified).
3. **Extract the contract:** argc + operand roles; which **record-array** fields it writes (`ctx+0x53d64 + curidx*0x78`; cmd-type at `+0x24`; note any geometry offsets); whether it **inserts/queries** the registry; and any output written back to a VM global.
4. **Annotate Ghidra:** `rename_function_by_address <va> gfx_op_0x<op>_<role>` + `set_plate_comment` with the decode (as done for `0x42a0b0`/`0x47f280`/`0x42cf70`). `save_program` at the end of the phase.
5. **Record the contract:** add a row to the `engine-re.md` gfx-op-contract table and set the op's `vm-map/opcodes.toml` `semantics` (`source = "investigation"`, `confidence = "high"`, evidence = handler VA + effects).

- [ ] **Step 1: Add the contract-table skeleton to `engine-re.md`**

Under the op-`0x215` section in `docs/engine-re.md`, add a new subsection `#### gfx command-buffer — op contract table` with a Markdown table header: `| op | handler | argc | cmd-type | record fields | registry | operand roles | summary |`. Seed the `0x215`, `0x1a2` rows from what's already known.

- [ ] **Step 2: Reverse each target op (one checkbox per op)**

Work the per-op procedure above. Check off as each contract row lands:
  - [ ] `0x1a2` gfx-cmd-register (confirm cmd-type 3 + registry insert key format)
  - [ ] `0x212` `u00421090`
  - [ ] `0x213` `u004210D0`
  - [ ] `0x216` `u004211A0`
  - [ ] `0x217` gfx-geom?
  - [ ] `0x218` gfx-geom?
  - [ ] `0x219` `u004212E0`
  - [ ] `0x21a` gfx-geom?
  - [ ] `0x1f7` ui-elem?
  - [ ] `0x1fa` ui-clear?
  - [ ] `0x1ff` draw?
  - [ ] `0x202` draw-blit?
  - [ ] `0x203` draw?

- [ ] **Step 3: Cross-check against `label_12649`**

Re-read `build/disasm/SC0000.asm` around `label_12649` (and `label_123ef`, `label_125bd @0x0050f`) and confirm the reversed contracts explain the slot-select branch and the per-object slot assignment (slots 4..13 via record table `0x3239`). Note in the contract table exactly which op(s) the branch keys on and what a correct return is (found ≥0 → existing slot; -1 → new).

- [ ] **Step 4: Rebuild generated opcode files + lint**

Run: `py -3.11 -X utf8 tools/opcodes_build.py --build`
Then: `py -3.11 -X utf8 tools/opcodes_build.py --lint`
Expected: build writes 4 files; lint = `0 errors, 0 warnings`.

- [ ] **Step 5: Save Ghidra + commit the RE**

`save_program` (mcp). Then:
```bash
git add docs/engine-re.md vm-map/opcodes.toml tools/age_opcodes_himegari.py docs/opcode-reference.md
git commit -m "docs(gfx): reverse the gfx command-buffer op contract (0x1a2/0x212-0x21a/…)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

**Phase 1 exit criteria:** every target op has (a) a real handler VA, (b) a Ghidra rename + plate comment, (c) a contract-table row, (d) `opcodes.toml` semantics at `confidence high`; and the `label_12649` slot-select branch is explained end-to-end. **← Re-plan checkpoint: Phase 3/4 tasks are authored now, from this table.**

---

## Phase 2 — Host-side model design (spec) — gated on Phase 1

**Deliverable:** `docs/superpowers/specs/2026-07-07-gfx-command-buffer-design.md`, approved before implementation. Written from the Phase-1 contract table. Must specify, concretely:

- [ ] **The `GfxState` model** — the object-record set (fields the reversed ops actually touch: handle, slot, cmd-type, geometry) and the handle→object **registry** (a `Dictionary`), with the insert/query semantics the contract table dictates. Only the fields the ops use — YAGNI.
- [ ] **The `IHost` surface** — one method per op-group (register / query / geometry-write), each mirroring the `GetTextureSize` pattern: the VM handler calls it and writes any result into the op's output operand(s). Exact C# signatures.
- [ ] **State ownership** — `GfxState` lives in the host layer and is shared by `GodotAdvHost` (real compositing) and the CLI `GfxTraceHost` (numeric oracle); the VM stays seam-clean (`Vm` → `Model`+`Hosting`+`Diagnostics`).
- [ ] **Compositor change** — how `godot/Main.cs` composites the per-object slot set (replacing the collapse-to-slot-0 blit), and how it stays compatible with the existing `BlitSlot`/`0x208` geometry path.
- [ ] **Parity + test strategy** — non-Godot no-op contracts; synthetic-scene unit tests (register→query→correct slot; two different-sized bgs → no drift); the `Age.Cli gfx` and screenshot oracles.
- [ ] **Explicit deferrals** — alpha/blend (`AE*`, `0x202/0x203` blend semantics), green chromakey, true multi-surface. In scope: **static positioned geometry only** (kills the drift).

**Phase 2 exit criteria:** design doc self-reviewed (no placeholders/contradictions) and user-approved. Then run `writing-plans` again to expand Phase 3 into TDD tasks.

---

## Phase 3 — Implement (task-level; expanded to TDD steps after Phase 2)

Each task is test-first (synthetic scenes via `ScriptAssembler`), ends with `dotnet test engine/AgeEngine.sln` green + a commit, and preserves parity on non-Godot hosts.

- [ ] **Task 3a — `GfxState` model + parity `IHost` surface.** Create `GfxState.cs`; add the Phase-2 `IHost` methods; give every non-Godot host a value-returning no-op. Unit test: register an object then query its handle → returns the object's slot; unknown handle → -1. **Gate:** all existing engine tests byte-identical.
- [ ] **Task 3b — Dispatch the reversed ops in the VM.** Replace the `OnStub` fall-through for each contract'd op in `VirtualMachine.Step`, calling the new `IHost` methods and writing outputs to operands per the contract. Unit test: a synthetic scene that registers two different-sized backgrounds and draws them lands each at its own correct `dst` (no cumulative drift). **Gate:** `--selftest` + engine suite green.
- [ ] **Task 3c — CLI oracle uses the real model.** Wire `GfxTraceHost` to a real `GfxState` so `Age.Cli gfx <SCENE>` reports true per-object slots/geometry. Verify on the drift scene: background resolves to `(0,0)` full-frame, not `(300,500)`.
- [ ] **Task 3d — Godot compositor.** `GodotAdvHost`/`Main.cs` composite the per-object slot set. Build (`godot --import` → `dotnet build godot/Himegari.csproj`), selftest parity.

---

## Phase 4 — Validate against the drift scene

- [ ] **Task 4a — Numeric oracle.** `Age.Cli gfx <drift-scene>` shows every `BG*`/sprite at plausible on-screen geometry (no `0×0`, no marching `dst`). Record the before/after numbers in `docs/phase-a-slice-plan.md` (A2b section).
- [ ] **Task 4b — Screenshot acceptance.** `godot --path godot -- --scene <drift-scene> --shot <png> [--shot-page N]`; the background fills the frame correctly (the bottom-right castle from `Screenshot 2026-07-06 211353.png` is snapped into place). This is the human oracle — no machine oracle for pixels.
- [ ] **Task 4c — Regression + docs.** Full `dotnet test engine/AgeEngine.sln` green; Godot `--selftest` OK; update `docs/phase-a-slice-plan.md` (A2b: drift RESOLVED, implemented) and the status memory. Then `finishing-a-development-branch` for merge.

**Phase 4 exit criteria:** the drift scene renders correctly (screenshot-verified), engine + selftest green, docs/memory updated to "drift resolved (implemented)".

---

## Self-review

- **Spec coverage:** the (b) verdict → Phase 1 reverses the whole family; the fix ("model + execute the ops") → Phases 2–3; acceptance (the drift screenshot) → Phase 4. ✓
- **Discovery gate honesty:** Phase 3/4 are deliberately task-level, not fake TDD code, because the handler contracts are Phase 1's output; the plan states the re-plan checkpoint explicitly. ✓
- **Placeholder scan:** Phase 1 & 2 are fully actionable now; Phase 3/4 carry concrete files, gates, and acceptance criteria, with the expansion gate named. No "TBD/handle edge cases" hand-waving. ✓
- **Seam/parity consistency:** every phase re-states the `Vm`→`Model+Hosting+Diagnostics` seam and the non-Godot no-op parity rule; `IHost`-method pattern matches the established `GetTextureSize`. ✓

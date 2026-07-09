# Differential VM-vs-engine oracle (control-flow) — design

**Date:** 2026-07-09
**Status:** approved (design), plan pending
**Lever:** #3 of 3 in the RE-front-loading program (after handler-labeling, import-map, ctx-struct,
hot-helper naming). The high-value anti-walk-back tool. See the status memory.
**Home in the canonical map:** results in `docs/engine-re.md`; new tools in `docs/tools-reference.md`.

## Motivation

The expensive RE churn this project keeps hitting is **wrong opcode *semantics*, believed then
retracted** — the `0x215` state-vs-command-buffer flip, "immediate-mode slot-0," "the opening is
sleep-paced." Every one was a claim about runtime behavior assumed from docs and built on before being
verified against the running engine. The first three levers made the *static* image far more legible;
this lever closes the loop by making *runtime truth* cheap to diff, so a semantics claim can be checked
against the real engine in minutes instead of hypothesized.

**Method:** run the same scene in the real engine (Frida trace) and our C# VM (`ITraceSink` trace) and
diff the **offset-execution path**. The first divergence is exactly the opcode/branch we modeled wrong.

**Key simplification:** both run the *same bytecode*, so the opcode at each script offset is *static*
(known from disassembly). We therefore diff the **sequence of script offsets executed** (control flow) —
not opcodes or effects. A path divergence = a branch (`jcc`) or opcode-length/semantics we got wrong.
Cheaper and higher-signal than capturing per-op effects, and it is precisely where the render-drift
walk-backs lived.

## Architecture

Engine trace (Frida) + VM trace (exists) → offset-path diff.

### 1. `tools/frida/trace_engine_ops.py` — engine op-tracer (read-only)

- Attach to the running game (like `dump_engine.py`/`probe_frame_cadence.py`).
- **Per executed op**, read from the engine `ctx`: `curCtx = ctx->cur_ctx_index` (`+0x53d14`),
  `pc = *(ctx->frame_pc + curCtx*0x78)` (`+0x53d2c`), `codebase = *(ctx->frame_codebase + curCtx*0x78)`
  (`+0x53d28`); emit `(codebase, offset = (pc − codebase)/4)`.
- **Loader hook** on the script loader (`call-script` handler `op_0x3_handler`@`0x41bc90` → loader
  `FUN_0040e980`) records `codebase → script-id` as scripts load (id→name via `parse_sys4ini`'s
  `callscript-names.json`), so the trace yields clean **per-script** offset streams (isolating SC0000
  from boot/system scripts and coroutine interleavings).
- Capture the opening window: attach, let the deterministic opening auto-advance to the first
  `wait-for-input`. Buffer entries in JS (array/ring), flush to `build/engine-optrace.jsonl`
  (`{codebase, script, offset}` per op, in execution order).
- **Capture mechanism (recon-gated, Task 1):** primary = plain-JS `Interceptor.attach` on
  `adv_interpreter_tick@0x410fb0` (clean one-op-per-tick signal). CModule on this path crashed before
  (status memory); **plain-JS is untested here** → Task 1 proves it or falls back. Fallback = the
  proven-safe `vm_operand_fetch@0x41b940` hook (`probe_frame_cadence.py` already uses it), reading the
  same ctx fields and **deduping consecutive same-pc** (fires per-operand; misses zero-operand no-ops —
  acceptable for control-flow since markers/no-ops don't branch).

### 2. VM op-trace (mostly exists — small emitter)

Our VM already emits the Step stream via `ITraceSink` (`Age.Cli trace`; byte-identical to `vm0.py`,
SC0000 ≈ 27,994 steps). Add a machine-readable emitter if not present: `Age.Cli trace SC0000.BIN
--boot --trace-json build/vm-optrace.json` → the ordered list of executed script-relative offsets for
SC0000. (If a suitable JSON trace already exists — e.g. the `vm0-trace.json` selftest artifact — reuse
its format.)

### 3. `tools/diff_optrace.py` — the diff

- Inputs: `build/engine-optrace.jsonl` + `build/vm-optrace.json` + the scene name (SC0000).
- Filter the engine trace to SC0000's script → engine offset sequence `E`. VM offset sequence `V`.
- Walk `E` and `V` in lockstep; report the **first index where they differ** (or where one ends early):
  the diverging offset, the **opcode at that offset** (from `build/disasm/SC0000.asm` / `sys4load`), and
  a few ops of surrounding context on each side. Also report how many steps agreed before divergence.
- Output: a concise divergence report to stdout (+ optional JSON).

## Data flow

running engine ─(Frida tick/operand hook + loader hook)─▶ `build/engine-optrace.jsonl`
`Age.Cli trace SC0000 --boot --trace-json` ─▶ `build/vm-optrace.json`
both + disasm ─(`diff_optrace.py`)─▶ "agreed N steps, diverge at offset X = op 0xYY (context …)".

## Validation

- **Self-test (expected):** our VM does not model the scene-coroutine framework (known gap), so the
  oracle should pinpoint divergence at/near the coroutine yield (`0x140`/`0x7b`, SC0000 `~0x450–0x50f`)
  — confirming that gap surgically. A divergence that lands on a *known* gap validates the tool.
- The engine trace length/shape for SC0000 is plausible (starts near offset 0, comparable magnitude to
  the VM's opening run); the loader hook correctly tags the SC0000 codebase.
- `diff_optrace.py` on identical inputs reports "no divergence" (a trivial equal-traces unit test).

## Scope & boundaries

- **In:** control-flow (offset-path) diff on the deterministic opening (`SC0000 --boot`); read-only Frida
  capture; the diff tool + report.
- **Out (phase 2+):** effects-diff (global-bank / gfx-registry writes — needs hooking engine write
  paths); branchy/input-driven scenes (need matched input + state); auto-classifying the divergence
  (we report it; the human/next-slice reverses it). No engine patching.
- **Regenerable:** both traces + any report live under `build/` (disposable).

## Acceptance criteria

- Task 1 recon: a clean SC0000 engine op-path captured safely (tick-hook or 0x41b940 fallback); the
  capture method + trace length recorded; if neither is safe, an explicit documented stop.
- `build/engine-optrace.jsonl` (per-script tagged) + `build/vm-optrace.json` produced for SC0000.
- `diff_optrace.py SC0000` runs and reports either agreement or a first-divergence (offset + opcode +
  context); the equal-traces unit test passes.
- The first real divergence is explainable (ideally the known coroutine gap), demonstrating the tool
  pinpoints a mis-modeled op.
- `docs/engine-re.md` + `docs/tools-reference.md` updated; status memory records the milestone (lever #3
  done; note phase-2 effects-diff as the next extension).

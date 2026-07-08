# Frame-paced `sleep` — the SC0000 opening animates

> **⚠ OUTCOME CORRECTION (2026-07-08):** this spec's premise — that bare `sleep` paces the opening burst, so
> implementing it would make the opening animate — was **wrong**. `sleep` is correctly implemented and the
> one-shot dramatic pauses work, but the rapid CG/AE\* burst is not sleep-paced (trace shows no sleep between the
> draws) and did not animate. Canonical corrected result: `docs/phase-a-slice-plan.md` (A2b frame-paced sleep).
> Kept as-authored below for provenance.

**Date:** 2026-07-08
**Branch:** continues `feat/gfx-command-buffer`
**Slice goal:** make the SC0000 opening's `sleep`-paced retained-object burst (`AE001D → AE002B →
AE003B` surface swaps + the alpha-tween channel) actually animate on screen, by giving `sleep`
(`0xc8`) real timing. Activates the already-built per-object alpha-tween subsystem and the per-frame
compositor, both currently dormant because the burst runs instantly.

## Problem (root cause, verified)

The Godot compositor `Main.Recomposite()` already runs every `_Process` frame (~60 fps) and composites
the VM's **live** `GfxState` (`_vm.Gfx.SnapshotVisibleObjects()`, ascending-handle z-order, with the
wall-clock alpha tween applied). The infrastructure to *present per-frame* already exists.

The opening does **not** animate for exactly one reason: `sleep` (`0xc8`) is a GAP op — it has no VM
`case`, so it falls through to `pc+1` with **zero delay**. The VM runs the whole opening
load/draw/`sleep` burst to completion in microseconds on its background thread, so the compositor only
ever catches the **final** retained state. The intermediate `AE*` surface swaps are overwritten before
a single frame composites.

This is confirmed native-side (`docs/engine-re.md`): the engine is retained and does **not** block the
VM on present — animation runs in the host's per-frame loop while the VM is parked; `sleep` paces the
burst. Raw SC0000 bytecode shows `sleep 0x64 / 0x3e8 / 0x2ee` between paced retained-object draws.

**Therefore:** give `sleep` real timing (pause the VM thread) → the existing compositor catches each
intermediate frame → the opening animates. No new rendering machinery required.

## Non-goals (explicitly deferred)

- **Scene-coroutine framework** — `0x7b` / `0x7c` / `0x140`, the `G[0xaba5c]==1` re-entry gate, and the
  `label_125bd` slot-table setup. This is the interactive multi-object subsystem; it is not on the
  opening's linear path. Stays the "scene-coroutine backlog" item.
- **ADV native text** (`0x204` draw-string, `0x7a`) — text already renders via `IHost.ShowText`.
- **Audio/SFX cluster** (`0xb4/b5/b6/c2`) — separate self-contained slice.

## Design

### 1. RE task (bounded) — confirm `sleep` semantics

Decode the `0xc8` handler via the master dispatch table `handler(op) = ctx[0x26c93+op]` (read from
`FUN_00413860`, per `docs/engine-re.md`). Determine:

- **Time unit** of the operand. Operands seen: `0x64`(100), `0xc8`(200), `0x2ee`(750), `0x3e8`(1000),
  `0x1f4`(500), `0xfa0`(4000). Need to know: milliseconds, frames (÷60), or engine ticks. This gates
  correct pacing (getting it wrong makes the opening 60× too fast or slow).
- Confirm it is a **simple blocking pause** (busy/sleep loop or message-pump wait), not a
  clock-relative / conditional wait that would need extra state.

Annotate in Ghidra (rename `FUN_...` → `sleep_op_0xc8` + plate comment with the decode) and record the
finding in `docs/engine-re.md` and `vm-map/opcodes.toml` (op `0xc8`, source=investigation).

**Output of this task:** the unit conversion `operand → milliseconds` used by the Godot host.

### 2. Engine seam — `IHost.Sleep` + VM dispatch

- `IHost` gains `void Sleep(int milliseconds)`.
- `VirtualMachine` gains `case "sleep": _host.Sleep((int)Read(a[0])); return pc + 1;` (unit conversion
  applied in the op or the host per the RE — decided in the plan; simplest is the host converts).
- **Parity:** still `pc + 1` → step counts and emitted offsets are byte-identical; the only behavioral
  delta versus the current GAP fall-through is that the gated `Stub` trace event for `0xc8` is replaced
  by the op executing. No golden regen.
- The **9 non-Godot hosts** (`CaptureHost` + test doubles `RecHost`/`CountHost`/etc.) implement `Sleep`
  as a **no-op**. Headless and every `Age.Cli` path stay instant → `sweep` 284 exit / 13 STEP-LIMIT and
  all engine tests unchanged.

### 3. Godot host — real pause on the VM thread

`GodotAdvHost.Sleep(ms)` blocks the VM **background thread** for the (converted) duration — the same
thread `vm.Run()` executes on via `Task.Run`. This mirrors the existing `WaitForInput` semaphore
suspend, but time-based. The Godot **main thread is untouched**, so `_Process` → `Recomposite()` keeps
compositing and presenting during the pause. No deadlock (the VM thread is a throwaway task thread).

Safety / debugging:
- Optional total-duration cap so a pathological script can't hang the window indefinitely.
- Optional `--sleep-scale <f>` CLI knob (default 1.0) to speed up/slow down for inspection. Out of the
  critical path; include only if cheap.

### 4. Thread-safety audit (primary correctness risk)

Today the compositor already races the VM thread, but the mutation window is microseconds so nothing is
ever observed mid-write. Once the VM genuinely runs concurrently **for seconds**, `GfxState` reads and
writes truly overlap. Requirement:

- Every `GfxState` **mutator** (`GetOrCreate`, `draw-texture` bind, `SetSurface`, the anim ops,
  `Register`/`Release`, `EraseRange`, `CurrentObject` set) and the reader `SnapshotVisibleObjects()`
  must serialize on the existing `_lock`.
- `SnapshotVisibleObjects()` returns an **immutable copy** built under the lock (it largely does; audit
  and close gaps — several mutators currently lock, some may not).

This is the one place a subtle bug could hide; the audit is explicit work, not incidental.

### 5. `0x20c` present — minimal handling

Our compositor presents continuously, so an explicit present op is implicitly satisfied. During the RE
(Task 1) confirm whether `0x20c` merely presents the frame (→ mark `noop_headless`/`safe-noop` in
`opcodes.toml`, a free GAP-count shrink) or gates double-buffering (→ small handler). Default expectation:
`safe-noop`. Kept intentionally minimal.

### 6. Verification tooling — frame-sequence capture

A time-based effect cannot be verified by the existing single `--shot`. Add a Godot dev flag
`--shot-sequence <dir> [--frames N]` that saves one PNG per `_Process` frame across the opening (up to N
frames or scene end). Success criterion: the captured sequence shows `AE001D`, `AE002B`, `AE003B` as
**distinct** frames (the explosion stepping), not just the final CG. This is the agent's eyes for the
animation.

## Testing & parity

- **New engine test:** a synthesized scene (via `ScriptAssembler`) containing a `sleep` op, run against
  a recording host that asserts `Sleep` was invoked with the expected converted value. Synthesize test
  data; never disable a feature to keep a real scene matching a frozen number (standing project rule).
- **Regression guard:** `sweep` (headless) unchanged 284/13; all engine tests byte-identical (no-op
  sleep everywhere headless); `--selftest` unchanged (synthetic selftest scene has no `sleep`).
- **Visual verify:** `--shot-sequence` frame set shows the opening `AE*` burst animating.
- **Completeness gauge:** `tools/scene_opcode_coverage.py SC0000` — `0xc8` moves GAP→impl; `0x20c` likely
  GAP→safe-noop. Record the GAP delta.

## Docs to update on landing

- `docs/engine-re.md` — `sleep` (`0xc8`) handler decode + time unit; `0x20c` present disposition.
- `vm-map/opcodes.toml` — `0xc8` (name/semantics/source), `0x20c` (noop_headless if confirmed); rebuild.
- `docs/phase-a-slice-plan.md` — A2b frame-pacing slice result; the honest "AE* now animates" update.
- `docs/tools-reference.md` — `--shot-sequence` flag.
- Status memory (`himegari-port-status.md`) + `MEMORY.md` line — frame-pacing landed; coroutine still
  deferred.

## Risks

- **Time-unit wrong** → mitigated by Task 1 (RE before implementing) + visual verify.
- **GfxState race** → mitigated by the Task 4 audit + immutable snapshot.
- **Godot-headless real-time cost** → only `GodotAdvHost` blocks; CLI/headless hosts no-op. A headless
  Godot play of a sleep-heavy scene takes real wall-clock time, which is acceptable and `--shot`
  auto-advances; `--selftest` is unaffected.

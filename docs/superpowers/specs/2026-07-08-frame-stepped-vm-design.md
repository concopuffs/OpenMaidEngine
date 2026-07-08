# Frame-stepped VM — design spec

- **Date:** 2026-07-08
- **Status:** design (awaiting review → implementation plan)
- **Area:** `Age.Engine` VM/host seam + Godot frontend
- **Related RE:** `docs/engine-re.md` — "Frame cadence — the interpreter tick" + "Frame cadence — live measurement"; `docs/phase-a-slice-plan.md` — "opening speed-through / engine-cadence"

## Problem

The SC0000 opening **visibly speeds through** in our Godot port. Root cause (confirmed): our VM runs on a
**free-running background thread** (`Task.Run(() => vm.Run())` in `godot/Main.cs`) with no binding to real
time. Only `WaitForInput` and `Sleep` pause it; the main-thread compositor samples `GfxState` at ~60 fps. So
the VM executes a page's opcodes — and any auto-playing stretch — in microseconds, and the compositor only
catches the final state. Distinct visible states that aren't separated by a `sleep`/`wait` collapse.

The native engine is cooperative and **rate-limited**: the interpreter advances a bounded number of opcodes
per main-loop iteration; visible state therefore changes over real wall-clock time. Our fix must give the VM
the same bounded wall-clock execution rate.

## Live measurement (Frida, 2026-07-08 — the basis for the mechanism)

Read-only / import-only instrumentation of the running game (`tools/frida/probe_frame_cadence.py`,
`probe_present.py`; see `docs/engine-re.md` "Frame cadence — live measurement"):

- **Execution is rate-limited, not free-running.** Active interpreter rate ≈ **1,788 operand-fetches/sec**
  (peak ~5,796) in normal play — orders of magnitude below an unthrottled interpreter, so the engine is
  pacing itself.
- **Fast-forward (Ctrl) scales the rate ~4×** (≈7,738/sec avg, peak ~15,572), gated by the engine's own skip
  bit `flags & 0x8000000`. It runs *more ops per unit time*, it does not skip content.
- **Rendering is Direct3D 9, uncapped.** `ddraw.dll` is not loaded; the game renders via `d3d9.dll`.
  `IDirect3DDevice9::Present` fires ~**1,908/sec** (no vsync), `BeginScene`/`EndScene` never fire → it's a
  **2D StretchRect-style compositor**. So there is **no fixed display-frame cadence** to match; `Present`
  rate ≈ op rate (~1 op per present). The meaningful pacing quantity is the **wall-clock op rate**, not a
  per-display-frame budget.

**Correction this supersedes:** an earlier draft of this spec used `present` (op `0x20c`) as the frame
boundary. Measurement killed that — `present` occurs only 7× statically / ~2× per opening run in *our*
bytecode path, and the native present is uncapped anyway. The mechanism is now a wall-clock op-rate throttle.

## Goals

1. The VM advances at a bounded **wall-clock execution rate** (~1,800 ops/sec target, tunable) so visible
   state changes over real time instead of instantly.
2. Keep the host behind `IHost` and the VM engine-agnostic — pacing lives at the seam + in the Godot host.
3. Preserve byte-identical headless parity (Steps / emitted lines / halt reasons) across all non-Godot hosts
   and the existing oracles (`sweep`, `--selftest`, engine tests).
4. Route all timing (the throttle, `sleep`, the anim tween) through **one host-owned clock** so a future
   speed multiplier scales everything coherently.

## Non-goals (YAGNI)

- **The Ctrl key / speed multiplier is NOT wired.** Build the single-clock seam so the ~4× multiplier is a
  trivial later addition; no key handler, no multiplier value now. When added it must be **ADV-mode-scoped**
  (the native governor does not speed up gameplay/menus), so no global-speed assumption.
- **No present/`0x20c` gating** (rare + native present is uncapped), **no transitions/tweens for CG pacing**
  (CGs hard-swap; back-to-back loads are layers that correctly composite into one image), **no pull-based VM
  rewrite** (background thread stays), **no automated pacing assertion** (validated by the user on the build).

## Design

### Driving model (approach A: background thread + host-timed throttle)

The VM keeps its background thread and its recursive `Run`/`RunFrame`/`Step` unchanged. The only VM change is
**one host call per executed opcode**; all pacing policy lives in the Godot host, mirroring the existing
`WaitForInput`/`Sleep` suspend pattern (VM thread blocks on a host primitive; main-thread `_Process` releases
it).

### Interface change (`IHost`)

Add **one** method:

```csharp
void FrameYield();   // called by the VM after each executed opcode; host paces / no-ops
```

`Sleep(long)` keeps its signature (only its Godot body changes to use the host clock). `present-frame`
(`0x20c`) stays a no-op — it is NOT a gate. No other `IHost` changes.

### VM change

In `RunFrame`, after each `Step`, call `_host.FrameYield()`. That is the whole VM change — the VM does not
know the budget, the rate, or the multiplier; it just offers a yield point per opcode. (If per-op virtual
dispatch shows up in headless profiling, batch to every K ops — parity is unaffected either way.)

### Host-owned scalable clock + op-budget throttle (Godot)

The Godot host owns a single **`FrameClock`** (a pure, Godot-independent class in `Age.Engine/Hosting`, so it
is unit-testable and off the `Vm` seam): monotonic virtual ms + a `Speed` factor (default `1.0`; the future
Ctrl hook). `Advance(realDeltaSeconds)` is called once per `_Process`. All host timing reads only this clock:

- **`FrameYield()` (the throttle):** increment an op counter; when it reaches `opsPerFrame` (the budget,
  scaled by `Speed`), block the VM thread until the next `_Process` advances the clock, then reset. So the VM
  runs ~`opsPerFrame` ops per real frame ⇒ ~`opsPerFrame × displayFps` ops/sec. Start `opsPerFrame ≈ 30`
  (≈1,800 ops/sec at 60 fps) — **tunable**, calibrated by eye.
- **`Sleep(ms)`:** block until the clock advances `ms` (replaces the raw `Thread.Sleep`).
- **anim tween:** reads `FrameClock` instead of Godot's raw wall clock.

Because the throttle, sleeps, and tweens share one clock, a future `Speed = 4` scales all three together — the
~4× fast-forward — with no desync, and the multiplier stays entirely inside the host.

### Concurrency

Same shape as `WaitForInput`: the VM background thread blocks on a host primitive; `_Process` advances the
clock and releases waiters. `GfxState` access is already serialized on its re-entrant lock (sleep-race fix);
the VM now runs concurrently with the compositor for real durations, so that lock discipline is relied upon
(covered by `GfxStateConcurrencyTests`).

## Parity (hard constraint)

- All non-Godot hosts implement `FrameYield()` as an **empty method** and keep `Sleep()` a no-op. The VM never
  blocks headless → `Steps`, emitted lines, halt reasons are byte-identical.
- Regression gates unchanged: `Age.Cli sweep` (284 exit / 13 STEP-LIMIT), Godot `--selftest`, engine tests.
- A trace-diff on a synthetic scene proves the added per-op `FrameYield()` (no-op headless) changes nothing
  the oracle observes.

## Testing (synthesize, don't disable)

- **`FrameClock` unit tests** — deterministic `Advance` math + budget/deadline crossing at `Speed=1` and other
  values; no real sleeping.
- **Throttle gate test with a fake-clock host** — a synthesized scene (`ScriptAssembler`) run through a test
  host whose clock is advanced by hand; assert the VM blocks after `opsPerFrame` ops and resumes on the next
  clock advance, and that `Speed` scales the budget.
- **Parity trace-diff** — synthetic scene through the no-op headless path == pre-change baseline.
- **Regression** — `sweep` counts unchanged; `--selftest` green.
- **Pacing acceptance** — the user validates the finished live build (tunes `opsPerFrame` by eye).

## Affected components

| File | Change |
|---|---|
| `engine/Age.Engine/Hosting/IHost.cs` | add `void FrameYield()` |
| `engine/Age.Engine/Hosting/FrameClock.cs` | **new** — pure virtual clock (`NowMs`, `Speed`, `Advance`); the op counter + thread-blocking live in the Godot host, not here |
| `engine/Age.Engine/Vm/VirtualMachine.cs` | call `_host.FrameYield()` after each `Step` in `RunFrame` |
| `engine/Age.Engine/Hosting/CaptureHost.cs` + all other non-Godot hosts | add empty `FrameYield()` |
| `godot/GodotAdvHost.cs` | `FrameYield()` = op-budget throttle on `FrameClock`; `Sleep()` blocks on `FrameClock` |
| `godot/Main.cs` | own `FrameClock`; `Advance(delta)` + release waiters each `_Process`; anim tween reads `FrameClock` |
| `engine/Age.Engine.Tests/` | new `FrameClock`, throttle-gate, parity trace-diff tests |

## Risks / open items

- **`opsPerFrame` tuning:** the initial ~30 (≈1,800 ops/sec) comes from live measurement; final value is
  dialed in visually. Op *cost* varies (a `call-script` vs a `mov`), so a pure op count is an approximation —
  acceptable for pacing, tune by eye.
- **Per-op host call overhead** in headless (empty `FrameYield`): negligible expected; batch to every K ops if
  profiling disagrees. Parity holds regardless.
- **Concurrency:** the VM now runs concurrently with the compositor for real durations; relies on the existing
  `GfxState` lock — watch for unlocked shared state during validation.
- **Stretch (only if needed):** confirm the 2D-composite model by hooking `StretchRect`/`Clear` — not required
  for the fix.

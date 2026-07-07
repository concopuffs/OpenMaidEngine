# Design: Retained Animated Compositor (the AE* fades / gfx effects)

Status: **approved (design)** · Date: 2026-07-07
RE source: `docs/engine-re.md` (op `0x215` + "The gfx animation/effects subsystem"). Builds on the gfx
command-buffer work (`docs/phase-a-slice-plan.md` A2b; `docs/superpowers/specs/2026-07-07-gfx-command-buffer-design.md`).
Touches: `engine/Age.Engine/Model/GfxState.cs`, `engine/Age.Engine/Vm/VirtualMachine.cs`,
`engine/Age.Cli/Program.cs` (`GfxTraceHost`), `godot/{Main,GodotAdvHost}.cs`.

## Problem / goal

SC0000's opening event CGs now render (gfx command-buffer + system boot), but the `AE*` flash/glow effects
draw **opaque and never clear** — a white "explosion" glow stays at the bottom of the screen. Root cause
(RE, `engine-re.md`): those effects are a **native time-animated retained render loop** — objects carry
animation state (progress/duration/target), op `0x238` sets a non-blocking global animation clock, and the
engine's per-frame loop interpolates + composites. Our **immediate-mode permanent canvas** can neither
animate (fade) nor clear (remove the overlay). **Goal:** replace it with a **retained, host-driven,
per-frame animated compositor** so effects fade and clear correctly — completing SC0000's rendering, which
proves the ADV effectful-op layer for all ADV scenes.

## Key architectural shift: immediate → retained

Today `draw-texture` (and `BlitSlot`) blit **immediately and permanently** onto one canvas. The real engine
is **retained**: the VM configures object/layer state; a per-frame render loop clears, advances animations,
and re-composites the active set. We adopt that:

- **The VM's gfx ops update retained `GfxState`** (they already do for geometry; extend to alpha + animation
  + a draw/layer list). They do **not** paint pixels.
- **The host owns a per-frame render loop** (`godot/_Process`): advance time, interpolate, clear, composite.
- **No VM/host frame-lockstep** — the VM parks at `wait-for-input` and the host animates during the park;
  the bytecode's `configure → wait` structure gives the timing (RE-confirmed: `0x238` is non-blocking).

## The model — `GfxState` extensions

`GfxState` (VM-owned execution state) gains, per object, the reversed animation fields and a compositor-
facing draw list:

```
GfxObject (additions)
  int  Alpha            // 0..255, from 0x202/0x203 (default 255)
  bool Animating        // native flag bit 4
  long AnimDuration     // obj+0x228 (from gfx_anim_start / 0x234)
  long AnimProgress     // obj+0x214 (advanced by the host clock)
  (long X,Y,Z) AnimTarget   // obj+0x244/248/24c
  // (existing: Slot, V18/V24/V16c, Field64/68/6c, Color)

GfxState (additions)
  long AnimClockElapsed, AnimClockDuration   // op 0x238 global clock (ctx+0x51b78/7c)
  IReadOnlyList<DrawLayer> Layers            // retained composite list (order = paint order)
```

`DrawLayer` = a resolved draw command (slot/source-rect/dst/alpha) captured when the VM executes
`draw-texture`/`0x202`/`0x203`, **replacing** the immediate blit. The host composites `Layers` each frame.

- **Layer lifecycle:** a full-screen opaque draw resets the layer set (it covers everything, matching the
  engine's frame); `0x1f7` erase / animation-complete removes layers; a new frame's draws append. Exact
  reset policy is a Phase-1 decision, validated by the oracle/screenshot (see Phase 1 plan).
- **Native workers still NOT modelled** — only the retained data the render loop needs.

## Data flow (per frame, host)

```
VM thread:  gfx ops → mutate GfxState (layers, alpha, anim target/duration/clock) → park at wait-for-input
Host _Process(delta):
  1. advance AnimClockElapsed += delta-scaled; per-object AnimProgress += ...            (Phase 3)
  2. interpolate each Animating object's current props toward AnimTarget by progress/dur (Phase 3)
  3. clear screen; composite Layers in order, each blended by its Alpha                  (Phase 1–2)
  4. present (update the TextureRect)
```

## Phasing (each a runnable, screenshot-checkable checkpoint)

- **Phase 1 — Retained compositor + clear.** `draw-texture` registers a `DrawLayer` instead of blitting;
  Godot `_Process` clears + composites the layer set each frame. No alpha/anim yet (layers opaque). Fixes
  the *never-clears* half — stuck overlays disappear when the frame resets. **Check:** opening CGs still
  render; the stuck bottom glow no longer persists across pages.
- **Phase 2 — Alpha/blend.** `0x202/0x203` set per-object/layer `Alpha`; the composite blends by alpha.
  **Check:** the glow renders translucent, not solid white.
- **Phase 3 — Time-animation.** Host clock + per-object interpolation (`0x234`/`0x1fd`/`0x238`) advance in
  `_Process`. **Check:** the explosion/glow **fades** over its duration.
- **Phase 4 — Remaining effect ops + polish.** RE + fold in the render/present family
  (`0x243/0x20c/0x21c/0x224`), green chromakey for sprites, residual effect ops. **Check:** opening correct
  end-to-end by-eye.

## Parity & testing

- **Engine tests stay host-agnostic.** `GfxState` additions are pure data → unit-testable (layer list built
  by the ops; alpha/anim fields set; clock advance is a pure function). No Godot needed.
- **`Age.Cli gfx` oracle** dumps the resolved layer list (slot/dst/alpha) + anim state — the headless
  numeric check that the VM builds the right retained state.
- **Godot `--selftest`** stays green (synthetic scene, no gfx) — the compositor rewrite must not perturb it.
- **Screenshot / by-eye** on `SC0000 --boot` is the human oracle per phase (the user drives it live — do
  **not** auto-screenshot-and-quit).
- **Guardrail:** the existing booted CG render (event CGs at `(0,0)`) must not regress at any phase.

## Deferrals (explicit)

- **SRPG rendering ops** (map/sprite `RENDERMAP`/`DRAWOBJ` family) — a separate surface SC0000 doesn't
  exercise; out of scope.
- **Exact easing curve** of the interpolation (linear vs the engine's curve) — start linear; refine if
  by-eye shows it matters.
- **The render/present family internals** (`0x243/0x20c/0x21c/0x224`) — RE in Phase 4 as needed; Phases 1–3
  don't require them (the host loop is our present).

## Risks / open questions

1. **Layer-reset policy** (when does the frame clear / which layers persist): the main Phase-1 design risk.
   Fallback: reset on each full-screen opaque draw + on `0x1f7` erase; validated by the oracle + screenshot,
   iterate if wrong.
2. **VM-runs-ahead** vs animation timing: mitigated by the bytecode's `setup → wait-for-input` structure
   (the VM parks after each animation setup). If a sequence sets up an animation without a following wait,
   the retained state still animates from its configured values — acceptable.
3. **Time base** for the clock (`delta` seconds vs the engine's unit): calibrate the elapsed→progress scale
   against a known-duration effect (e.g. the `9000` explosion) by-eye in Phase 3.

## Success criteria (design done)

- This spec approved; the retained/host-loop architecture + 4-phase split committed.
- Phase 1 expanded into a TDD plan (`docs/superpowers/plans/2026-07-07-animated-compositor-phase1.md`).
- RE findings recorded in `engine-re.md` (done).

# Design — sprite transform / animation subsystem (opening slice)

Date: 2026-07-07
Status: approved (brainstorming), pending implementation plan
Branch: `feat/gfx-command-buffer` (continues the gfx command-buffer work)

## Problem

The scene-completeness tracker (`tools/scene_opcode_coverage.py`, added 2026-07-07) makes SC0000's
un-implemented opcodes legible: instruction-weighted the VM handles ~94.8%, but **68 GAP opcodes** are
silently skipped. The largest rendering cluster of these is `0x21c–0x243` (+ `0x2bd/0x2bf`) — e.g.
`0x220`×66, `0x22f`×34, `0x228`×33, `0x21e`×25 static sites in SC0000.

Native RE (see `docs/engine-re.md`, "The `0x21c–0x243` sprite transform / ANIMATION cluster") resolved
**every** handler in the band through the anchored dispatch table (`ctx[0x26c93+op]`, from `FUN_00413860`)
and showed it is **one coherent subsystem: sprite transform + animation/tween**. Two members were already
named in prior RE — `0x234 gfx_op_0x234_anim_start`, `0x238 gfx_op_0x238_set_anim_clock`. The representative
SET ops `0x220`/`0x21e` (both `argc 6`, now annotated in Ghidra) write **cmd-type `0xd`**, fetch operands
1..6, and call a transform worker `(handle, op2, op3, float op4, float op5, float op6)`; `0x21e` normalizes
the floats by `/_DAT_00571c28` (→ scale/percentage). The worker `gfx_anim_set_channel`@`0x47eaa0` uses the
**same `gfx_object_get_or_create`** our `GfxState` already models and arms an animation channel on the object
record (`obj+0x3c=op2`, `obj+0x50=op3`, `obj+0x68=1` enable, `obj+0xac=vec3(op4,op5,op6)` target). Combined
with `anim_start`/`set_anim_clock`, this is a per-frame clock that interpolates the target over time — i.e.
**what makes `AE*` fades/effects animate instead of snap**. Today the Godot compositor blits every object
**opaque** with no transform and no time, so the opening's fade "explosion glow" stays put.

## Goal

Render the SC0000 opening's animated effect(s) — specifically the `AE*` fade — correctly: the effect
**animates over time** rather than snapping or staying opaque. Do it as the smallest verifiable slice, and
leave a clear, measurable trail (tracker GAP shrink) for expanding to the rest of the cluster later.

## Decisions (from brainstorming)

1. **Timing fidelity = wall-clock tween.** The VM records the animation channels (targets + durations) into
   `GfxState` deterministically; the Godot compositor interpolates them over its own frame delta. Rendering
   is frontend-only, so **headless VM trace parity is untouched**. We decode `anim_start`/`set_anim_clock`
   enough to get durations/targets but do **not** replicate the game's exact native tick.
2. **Scope = opening-driven subset first.** Trace what SC0000's booted opening actually executes, implement
   only those anim ops, verify the fade animates, then expand in follow-ups. Only model what we can confirm
   on screen.
3. **Architecture = passive `GfxState` + compositor-owned tween + alpha-aware blit.** The anim ops only
   record channel data on the object; the compositor owns all wall-clock/tween logic and the new alpha
   compositing. Keeps `GfxState`'s established passive-data-model role and folds in the deferred
   `0x202/0x203` alpha as a side effect.

## Non-goals / deferred

- Cluster ops that appear only in battle/debug scenes (`BTL`/`DEBUGADV`) and are **not** on the opening
  path — they stay on the `default` VM stub and keep showing as GAP in the tracker (honest remaining work).
- Scene-coroutine / script-yield timing ops (`0x7b`/`0x140`/`sleep 0xc8`). If the opening's fade is driven
  by a script poll-loop that needs these, we handle the *fire-and-forget* animation-command path and record
  the coupling; the coroutine model is a separate backlog item.
- The native DirectDraw workers (`FUN_0047xxxx`) — not modeled, consistent with the existing gfx family.
- Frame-exact reproduction of the game's animation clock.

## Invariants (must hold)

- **Headless VM parity:** anim ops are record-only on the VM side (like every existing gfx op); the
  non-Godot hosts (`CaptureHost`, test `RecHost`/`CountHost`) read none of the new state → `--selftest`,
  `sweep`, and existing engine tests stay byte-identical.
- **`GfxState` stays passive** — no time/`Tick`; it exposes recorded channel data, nothing more.
- **`opcodes.toml` is the source of truth** for op labels; the VM dispatches on `label` and the table is
  rebuilt via `opcodes_build.py --build` (never hand-edit generated files).

## Architecture

### A. RE step (executed first in the plan — the one real risk)

The chunk gates on two facts we don't yet have:

1. **Decode `0x234 anim_start` and `0x238 set_anim_clock`** (handler addresses already resolved in the
   `FUN_00413860` dispatch map). Recover their operand contract and which fields they set (duration/clock,
   the start trigger, which object/channel). Annotate the handlers in the Ghidra project (`gfx_op_0x234_*`,
   `gfx_op_0x238_*`) and record the contract in `docs/engine-re.md`.
2. **Trace the `--boot` opening** (`Age.Cli gfx --boot SC0000.BIN` and/or `play --boot --trace`) to confirm
   *which* cluster ops execute on the opening path, in what order relative to the `AE*` CG loads /
   `draw-texture` / `0x202`/`0x203`, and therefore **what quantity is being animated** — the transform vec3
   (`obj+0xac`) or the packed alpha from `0x202/0x203`.

Output: the confirmed op subset + an `engine-re.md` addendum. **If the trace shows the fade's alpha is
`0x202/0x203`-driven**, the chunk narrows to "alpha-blend compositing + those two ops" — even smaller — and
the transform ops become a later slice.

### B. Data model — `GfxState` / `GfxObject` extensions (`Age.Engine/Model/GfxState.cs`)

Extend `GfxObject` with the animation channel the worker revealed:

- `(long X, long Y, long Z) AnimTarget;` — the `obj+0xac` transform target (semantics per RE: scale, color,
  or offset; stored faithfully regardless).
- `long AnimParam1, AnimParam2;` — `obj+0x3c` (op2) and `obj+0x50` (op3).
- `bool AnimEnabled;` — `obj+0x68`.
- `long AnimDurationTicks;` — from `set_anim_clock`/`anim_start`.
- `long AnimGeneration;` — bumped every time `anim_start` fires on the object; the compositor's trigger to
  (re)start a tween.

New passive methods (mutate the object, bump generation where appropriate), called from the VM dispatch:

- `SetAnimTransform(long handle, long p1, long p2, (long,long,long) target, bool normalized)` — for
  `0x21e`/`0x220`. (Normalization: `0x21e` divides operands by the runtime divisor; store the fraction as the
  design's chosen fixed representation — decide the unit during RE, e.g. permille or a float field.)
- `StartAnim(long handle, …)` — for `0x234`; sets `AnimEnabled` + bumps `AnimGeneration`.
- `SetAnimClock(long handle_or_scope, long duration)` — for `0x238`.

`SnapshotVisibleObjects()` (and the `RenderObject` record) gain the anim channel + `AnimGeneration` +
`AnimDurationTicks` so the compositor can tween without reaching into `GfxState` internals.

### C. VM dispatch (`Age.Engine/Vm/VirtualMachine.cs` + `vm-map/opcodes.toml`)

- Add `label`s in `opcodes.toml` for the confirmed subset (dispatch keys on `label`, the `0x212–0x21a`
  precedent): e.g. `0x21e = set-anim-transform-norm`, `0x220 = set-anim-transform-abs`, `0x234 = anim-start`,
  `0x238 = set-anim-clock`, plus any opening-path query op. Rebuild `opcodes.json`/reference via
  `opcodes_build.py --build`.
- Add `case` arms in `Step` that read operands and call the `GfxState` methods, each returning `pc+1` —
  identical shape to the existing gfx ops. Everything else in the cluster stays on `default` (still GAP).

### D. Godot compositor — the visible payoff (`godot/Main.cs`)

- **Per-handle tween table** (frontend-only): `Dictionary<long, TweenState { Vector startTransform,
  targetTransform; float elapsed, duration; long generation; }>`.
- Each `_Process`/`Recomposite` frame: for each animated visible object, if its `AnimGeneration` differs from
  the tracked one → (re)initialize the tween (start = current on-screen transform, or identity on first
  sight); `elapsed += delta`; `t = clamp(elapsed / duration, 0, 1)`; interpolate; apply.
- **Alpha-aware blit:** replace the opaque `BlitRect` in `BlitLayer` with a composite that supports per-object
  **alpha** (Godot `Image.BlendRect` for straight alpha; manual per-pixel only if scale/offset is needed).
  First pass prioritizes **alpha (fades)** — the user-visible `AE*` defect; scale/position added only if the
  trace shows the opening needs them. This is where `0x202/0x203`'s stored packed color is finally applied.

## Data flow

```
VM thread (deterministic):
  set-anim-transform (0x21e/0x220)  -> GfxState.SetAnimTransform(handle, …, target)
  anim-start          (0x234)       -> GfxState.StartAnim(handle)   [bumps AnimGeneration]
  set-anim-clock      (0x238)       -> GfxState.SetAnimClock(…, duration)
        (records channel data on the object; no time)

Godot main thread, every _Process(delta):
  Recomposite():
    for each visible object o in SnapshotVisibleObjects() (ascending handle = z-order):
      if o.AnimGeneration != tween[o.Handle].generation:  start tween (capture start transform)
      advance tween.elapsed += delta; t = clamp(elapsed/duration)
      transform = lerp(start, target, t)
      BlitLayer(surface, srcRect, dst, alpha/scale from transform)   [alpha-aware]
```

## Testing / verification

- **Engine unit tests** on *synthesized* scenes (per the "synthesize, don't disable" rule,
  `ScriptAssembler`): assert `GfxState` records target/duration/generation from the anim ops; assert the
  non-Godot hosts stay byte-identical (parity). No real-scene golden is frozen.
- **Tracker:** `scene_opcode_coverage.py SC0000` — the implemented subset moves impl-ward and the GAP count
  drops; this is the chunk's completeness metric.
- **Visual:** extend the `--shot` capture to grab a few time offsets mid-tween (or capture at fixed elapsed
  points) and confirm the `AE*` glow **fades** across frames instead of staying opaque.
- **Headless parity:** existing `--selftest` and `sweep` outputs unchanged.

## Sequencing

RE (A) → data model (B) → dispatch (C) → compositor (D), running verification (tracker + tests +
screenshot) after each so a wrong assumption surfaces early. **A is the gate**: its trace result can shrink
the whole chunk (alpha-only) or confirm the transform-tween scope.

## Risks / open questions

- **What `obj+0xac` controls visually** (scale vs color vs offset) — resolved by the RE step; the data model
  stores it faithfully either way, so a wrong guess costs only the compositor's interpretation, not the
  recording.
- **Script-driven vs engine-driven animation.** In the real engine the script arms the anim then *yields*
  (coroutine/`sleep`) while the native loop ticks. Our VM runs straight through (sleep/coroutine stubbed),
  so we capture anim commands as fire-and-forget and tween them in the compositor. If the opening's fade
  instead relies on a script poll-loop we can't run headless, that's the scene-coroutine backlog, not this
  chunk — the RE trace tells us which.
- **Normalization unit** for `0x21e` (`/_DAT_00571c28`, runtime-init, static 0) — pin the divisor's runtime
  value (or infer from corpus `0x64`→1.0 scale) during RE; pick a stable stored representation.

## Canonical docs touched

- `docs/engine-re.md` — op-contract addendum for `0x234`/`0x238` + confirmed opening subset.
- `vm-map/opcodes.toml` — labels for the subset (then `opcodes_build.py --build`).
- `docs/phase-a-slice-plan.md` — A2b results for this slice.
- Status memory — milestone entry.

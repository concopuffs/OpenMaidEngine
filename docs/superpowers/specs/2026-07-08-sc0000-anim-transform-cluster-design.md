# SC0000 gfx animation / transform / spritesheet cluster (design)

**Date:** 2026-07-08
**Status:** design, pending implementation plan
**Scope:** the gfx animation/transform/spritesheet opcode cluster (`0x1fd`, `0x21c`–`0x243`) as **one unit**,
to drive SC0000 from "half the graphics broken" to a coherent, validatable scene. Follows graphics slice A
(blend/transparency, merged). `draw-string` (on-screen text) and `play-sound-effect` are separate adjacent
efforts, NOT in this cluster.

## Problem

SC0000 has 62 GAP ops (effectful but stubbed). Three of the four visible defects are one subsystem — the
retained sprite **animation/transform cluster** — because these ops all read/write the same per-object record
and clock:

| Symptom (user, eyes-on) | Stubbed ops |
|---|---|
| Sprites don't move | `0x22f`×34, `0x228`×33, `0x229`×7, `0x21f`×8, `0x1fd`×12, `0x21d`, `0x223`, `0x224` |
| Animated sprites show the whole spritesheet | `0x239`×7, `0x231`×9, `0x232`, `0x236` (src-rect / cell-grid) |
| Glow/explosion runs too fast ("mach 5") | the anim *timing* — periods + clock coupling; `0x242`, `0x243`, `0x23f`, `0x23d` |

"Mach 5" is **not** the frame-stepped throttle failing (that governs op execution). The visible animation is
paced by **per-channel periods** on the engine's frame clock; we model neither, so effects snap/run unbounded.

## Native model (reversed — source of truth)

Canonical: `docs/engine-re.md` §"The full gfx render model" + §"Blend & transparency". Confirmed this session
(Ghidra, annotated + saved):

**Dispatch-table unlock (general, foundational).** The op→handler table is registered statically by
`FUN_00413860`: `handler(op) = ctx[0x26c93 + op]` (dword index; default `FUN_004162b0`). The `u004xxxxx`
labels in `opcodes.toml` are **Kelebek VAs with drift** and point at the wrong functions — always resolve a
handler via this table, not the label. Recovered cluster handler addresses (all real):

| op | handler | op | handler | op | handler |
|---|---|---|---|---|---|
| 0x1fd | `gfx_op_0x1fd_set_vec_scaled` | 0x224 | `LAB_00417550` | 0x236 | `FUN_00423ee0` |
| 0x21c | `LAB_00417520` | 0x228 | `FUN_0042a3a0` | 0x239 | `FUN_00424120`→`FUN_0047ed90` |
| 0x21d | `FUN_00423310` | 0x229 | `FUN_00423700` | 0x23d | `LAB_004175c0` |
| 0x21f | `FUN_00423410` | 0x22f | `FUN_00423b00`→`FUN_00472e90` | 0x23f | `FUN_0042a520` |
| 0x223 | `FUN_00423620` | 0x231 | `FUN_00423be0` | 0x242 | `FUN_004249d0` |
| (0x20a `FUN_00422ce0`, 0x20e `LAB_004174f0`, 0x232 `FUN_00423c30`, 0x243 `LAB_004182d0`) | | | | | |

**Setter shape.** Each cluster op is a thin wrapper that fetches operands (`FUN_0041b940(n)`) and calls a
worker (`FUN_0047xxxx`) that writes the object's channel fields. E.g. `0x22f` = `(handle, op2, x, y, z)` →
`FUN_00472e90`; `0x239` = 6 operands → `FUN_0047ed90`.

**The interpolator — `gfx_object_anim_interpolate` (`0x473ed0`), the CONSUMER = ground truth.** Called by
`gfx_object_composite` when the object's anim bit is set. Timebase = the **global frame clock `ctx+0xb550`**
(advances per present), NOT the op-`0x238` clock. It interpolates **5 independent channels**, each with its
own **period** + **start timestamp** (start inits to the clock on first frame), and each **ping-pongs**
(triangle wave: `progress = (now-start) % period`, folded at `period/2`) — these OSCILLATE toward a target
and back (pulsing/glow), they are not one-shot:

| Channel | period | start | target/params |
|---|---|---|---|
| Color/alpha | `obj+0x220` | `obj+0x20c` | `obj+0x240` (packed, LERP vs current) |
| Matrix A | `obj+0x224` | `obj+0x210` | `obj+0x250..0x28c` (4×4) |
| Rotation | `obj+0x228` | `obj+0x214` | `obj+0x244` (range `0x168`=360°) |
| Matrix B | `obj+0x22c` | `obj+0x218` | `obj+0x290..0x2cc` (4×4) |
| Src-rect scroll | `obj+0x230` | `obj+0x21c` | grid `obj+0x238/0x23c`, crops `obj+8..0x14` |

## Architecture (hybrid: engine resolves, host blits)

Same seam as slice A. The **engine** owns the channel model + a faithful port of the interpolator; the
**host** blits the resolved result. Keeping the interpolator engine-side makes it unit-testable
(`channels + nowMs → expected transform/cell/color`, no pixels) and headless-consistent.

**The pacing fix.** Drive the interpolator off the **`FrameClock`** built for frame-stepping
(`Age.Engine.Hosting.FrameClock.NowMs`), using the RE'd per-channel periods. Anim time then == the throttled
virtual time, so effects play at native cadence — this is the direct fix for "mach 5". (The native's frame
clock advances per present; our `FrameClock` advances per rendered `_Process` — the same wall-clock basis.)

### Data flow

```
VM cluster ops (engine thread)          Model (GfxState.GfxObject)              Host (_Process)
0x22f/0x228/... (motion)              → position channel {period,start,target}
0x239/0x231/... (spritesheet)        → src-rect channel {period,start,grid,cells}
0x21f/... (rotation/scale)           → rotation/scale channels
0x202/0x203 (color — slice A)        → color channel
0x234/0x238 (existing)               → one-shot anim + global clock (unchanged)
                                                 │
                    SnapshotVisibleObjects(nowMs)  ── ports gfx_object_anim_interpolate ──►
                    per visible obj (ascending-handle z-order), interpolate each active channel
                    (ping-pong over nowMs w/ its period) → RenderObject {
                        dstPos, Rotation, Scale, SrcRect(selected cell), Alpha, Tint, Blend, ColorKey }
                                                                        ├─► blit surface sub-rect (cell) at pos
                                                                        ├─► (rotation/scale ONLY if used — Task-0)
                                                                        └─► colorkey/alpha/tint (slice A)
    Host passes FrameClock.NowMs as nowMs each frame.
```

### Units

**Unit 1 — Engine: anim-channel model** (`GfxState.GfxObject`). Add the per-channel `{period, start, target}`
fields the interpolator reads (position, rotation, scale, src-rect grid/cells; color exists from slice A).
Offsets/semantics from the interpolator table above; per-op mapping pinned in Task-0.

**Unit 2 — Engine: cluster setter ops** (`VirtualMachine.Step` + `GfxState` mutators). Implement the GAP ops
to populate channels, one mutator per channel family. Each op's worker is decompiled via its known address
(Task-0) to get the exact operand→field mapping.

**Unit 3 — Engine: the interpolator** (`GfxState.SnapshotVisibleObjects(long nowMs)`). Faithful port of
`gfx_object_anim_interpolate`: for each visible object, for each active channel, ping-pong over `nowMs` with
its period and produce the current transform/cell/color into the `RenderObject`. Pure → unit-tested.

**Unit 4 — Host: apply transform + src-cell** (`godot/Main.cs`). Compositor passes `FrameClock.NowMs`, blits
the selected src sub-rect (spritesheet cell) at the interpolated position. **Rotation/scale rendering is
included only if Task-0 shows the SC0000 opening animates those channels** (a larger affine-blit change);
otherwise deferred with a logged note — position + src-cell + slice-A color covers the axis-aligned case.

## RE Task-0 (first plan task — bounded, now low-risk)

For each cluster op, decompile its **known-address** worker and record the operand→channel-field mapping;
and enumerate **which channels the SC0000 opening actually animates** (bounds Unit 4). Verify against the
`gfx` oracle (model state) and the disassembly (which ops fire). Output: the op→field table (into
`engine-re.md`) + the used-channel set. If a channel is unused in the opening (e.g. a raw 4×4 matrix), scope
its rendering out of this slice and note it.

## Testing & acceptance

- **Engine (xUnit):** (a) each setter op writes the expected channel fields; (b) the interpolator — given
  channels + a sequence of `nowMs`, assert the ping-pong transform/cell/color at chosen phases (0, quarter,
  half period) — this is where "mach 5" is pinned to a *number* (a period at `t` yields a specific offset).
- **Parity:** engine suite, `sweep` (`exit=284, STEP-LIMIT=13`), Godot `--selftest` stay green — the new
  ops are effectful only through the gfx model (headless dialogue path unaffected); `SnapshotVisibleObjects`
  gains a `nowMs` param but headless callers pass a fixed value → deterministic.
- **Scene (pixels, the user's validation):** `--shot-sequence` on `SC0000 --boot` — sprites now translate,
  animated sprites show a single cycling cell (not the full sheet), and the glow/explosion plays at a
  readable cadence. The coverage gauge (`scene_opcode_coverage.py SC0000`) shows the cluster GAP ops move to
  handled. This is the **whole-scene** confirmation the user has been unable to give — validated once, at the
  end, not per-op.

## Non-goals

- `draw-string 0x204` (on-screen text) and `play-sound-effect` — separate adjacent efforts.
- Graphics slices B (geometry/anchors beyond what these channels set) and C (render-targets).
- Rotation/scale rendering **if** the opening doesn't use it (Task-0 decides; seam left).
- Smooth one-shot color-anim interpolation and additive/glow blend — still deferred from slice A (tracked
  in `engine-re.md` §Blend); this cluster is the *oscillating* channels, a distinct mechanism.
- Reimplementing D3D9 / the raw 4×4 matrix path unless the opening exercises it.

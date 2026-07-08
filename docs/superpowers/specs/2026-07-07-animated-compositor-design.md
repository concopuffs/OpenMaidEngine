# Design: Surface + Object Compositor (the AE* fades / gfx effects)

Status: **approved model, redesigned 2026-07-07** (supersedes the earlier flat-layer version, which was
wrong — see below). RE source: `docs/engine-re.md` "The full gfx render model". Touches:
`engine/Age.Engine/Model/GfxState.cs`, `engine/Age.Engine/Vm/VirtualMachine.cs`, `engine/Age.Cli/Program.cs`,
`godot/{Main,GodotAdvHost}.cs`.

## Why the first attempt failed (do not repeat)

The first pass modelled rendering as a flat list of "draw layers" blitted to one screen, and resolved a
layer's texture from its *slot* at composite time. That has no **surface** concept and snapshots textures
wrong. Symptoms: alternating grey CGs, the glow drawn over backgrounds, vanishing sprites. Reverted. The RE
(`engine-re.md`) shows the real model is **surfaces + objects + a per-frame composite in handle order** — this
spec is built on that, model-first.

## The model (RE-confirmed — build exactly this)

**Surfaces** — image buffers indexed by slot (`ctx+0x52bd4[slot]`). `create-texture` makes a blank one;
`set-texture(resId, slot, colorkey)` loads a file into the slot's surface with a chromakey; it *replaces* the
old surface. Surfaces persist until overwritten.

**Objects** — a registry keyed by handle. Each object carries: a **source slot** + **source rect** + a
**position** (V24) + **anchor** (V18) + scale + **animation** (progress/duration/target) + **alpha/color** +
a **visible** flag. `draw-texture(handle, slot, srcRect, dstXY)` binds slot/rect/position and sets visible;
the geometry/anim/color ops set the rest. **An object references its surface by slot index, live** (resolved
each frame) — never a snapshot.

**Render frame** — iterate objects **in ascending handle order (= z-order)**; for each *visible* object,
compute its transform from geometry, interpolate if animating, and blit `surface[slot]`'s rect at its position
with alpha + colorkey. Then present. Slot 0 is not special.

## Architecture

- **`GfxState` (VM-owned) gains two coherent stores:**
  - **`SurfaceStore`**: `slot → Surface{ long ResId, long ColorKey }` (blank if create-texture). Set by the
    `set-texture`/`create-texture` VM cases.
  - **Object fields** (extend the existing `GfxObject`): `SourceSlot`, `(int X,Y,W,H) SrcRect`, `bool Visible`
    (plus the existing `Slot`, `V18`, `V24`, animation, `Color`). Set by `draw-texture` + the gfx ops.
- **Host compositor** (`godot/Main._Process`): each frame, iterate `GfxState` objects **ordered by ascending
  handle**; for each `Visible` object, resolve `SurfaceStore[obj.SourceSlot].ResId → BMP` (host), and blit its
  `SrcRect` at `obj.V24` position. Clear+recomposite each frame. (Alpha, colorkey, animation added in later
  phases.)
- **VM↔host:** no lockstep. Objects/surfaces are retained VM state; the host composites what's current each
  frame while the VM is parked at wait-for-input. The `0x238` clock is non-blocking (confirmed).
- **Seam:** `GfxState` stays version-neutral — it stores `ResId`/`ColorKey`/geometry (no BMP paths). The host
  resolves `ResId → BMP` (it already does via `ResourceMap`).

## Phasing (each runnable + user-eyeballed; RE-first, no guessing)

| Phase | Deliverable | Live check |
|---|---|---|
| **1. Surfaces + objects + composite** | `SurfaceStore`; object gets `SourceSlot`/`SrcRect`/`Visible`; host composites visible objects in ascending-handle order from their live surface. No alpha/anim (opaque). | CGs render correctly across pages (fixes the alternating grey); no vanished content |
| **2. Colorkey + alpha** | chromakey transparency from `set-texture`'s colorkey; per-object alpha (`0x202/0x203`) in the blit | glow translucent; sprites' green boxes gone |
| **3. Time-animation** | host advances the anim clock + per-object interpolation (`0x234`/`0x1fd`/`0x238`) by elapsed time | the explosion/glow fades over its duration |
| **4. Transform + remaining ops** | scale/rotate from `gfx_object_composite` math; residual effect/render ops | opening correct end-to-end |

## Testing

- **Engine (host-agnostic):** `SurfaceStore` + object fields are pure data → unit-tested (set-texture records a
  surface; draw-texture sets the object's source slot/rect/position/visible; ascending-handle iteration).
- **`Age.Cli gfx` oracle:** dump the object list (handle-ordered) with resolved surface + rect + position — the
  headless numeric check.
- **Godot `--selftest`:** stays green (synthetic scene, no gfx).
- **Live, user-driven:** the user runs `godot -- --boot` and eyeballs per phase. **No auto-screenshot-and-quit.**
- **Guardrail:** booted CGs must render correctly at Phase 1 (the flat-layer version regressed this — the
  faithful model must not).

## Risks / open (RE during implementation, don't guess)

1. **Scale/transform math** in `gfx_object_composite` (`FUN_00472f00`/`FUN_00473ed0`) — Phase 4; Phase 1 uses
   position + rect only.
2. **Colorkey format** (the `>>0x10 | 0xff00` packing in set-texture) — Phase 2; RE the exact bits then.
3. **Visible-flag lifecycle** — bit 0 is set by draw-texture; confirm what clears it (erase? a hide op?) so
   objects stop compositing when they should. Pin empirically via the oracle before relying on it.

## Success criteria (design)

- Model recorded in `engine-re.md` (done); this spec rebuilt on it; Phase 1 replanned (surfaces+objects, not
  flat layers). Then implement Phase 1 TDD.

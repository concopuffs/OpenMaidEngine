# Slice A — Blend & Transparency (design)

**Date:** 2026-07-08
**Status:** design, pending implementation plan
**Scope:** the first of three graphics-fidelity slices (A = blend/transparency, B = geometry/anchors,
C = render-targets). This spec covers **A only**.

## Problem

The Godot compositor renders every visible gfx object as an **opaque rectangle**. Two consequences:

1. **Sprites/overlays are opaque boxes** — the per-surface **colorkey** (chroma transparency, op `0x1f9`
   arg 3) is captured in the model but never applied, so a character sprite paints its whole rectangle
   (including the background color that should be keyed out) over the scene.
2. **Fades/flashes (`AE*`) draw opaque and never clear** — ops `0x202`/`0x203` set a per-object
   color/alpha modulation (fade-to-black/white, glow), captured on the object as a packed ARGB but never
   read by the compositor. In the SC0000 opening this shows as a full-screen grey layer that covers the
   event-CG and stays (verified via windowed `--shot-sequence`, frame ~45–60).

Both defects are *blend/transparency*: the model already **captures** the inputs; the render path does not
**consume** them. This slice makes the compositor honor colorkey + per-object alpha/tint + blend mode.

Out of scope (later slices): object placement/anchor math (V18/V24, the "doubling") = slice B;
render-target / off-screen surfaces (`SurfaceResId==0` blanks) = slice C.

## Native model (reversed — this is the source of truth)

Canonical: `docs/engine-re.md` §"The full gfx render model". Confirmed/refined in Ghidra this session
(functions renamed + plate-commented, program saved):

- **Surfaces** (`ctx+0x52bd4[slot]`) carry `{image, colorkey}`. `gfx_op_0x1f9_load_surface` (`0x422360`)
  loads the file into the slot **with a colorkey**; colorkey-matching texels become transparent **at load
  time** — it is NOT a per-blit compare.
- **Objects** (`ctx+0x408` map, keyed by handle) carry `{slot, srcRect, position V24, anchor V18, anim,
  color/alpha, visible, blend-mode}`. Composited in **ascending-handle order = z-order**.
- **`gfx_op_0x202_worker_set_color_anim`** (`0x47ea00`): op `0x202` sets an **animated** color/alpha target
  (`obj+0x64 = packedARGB`), sets the color-anim active bit, resets progress `obj+0x34=0`. Animates over the
  global clock (op `0x238`), like `0x234` does for position. Fades = animate a full-screen object (e.g.
  handle `0xcf08`) toward black/white × alpha.
- **`gfx_op_0x203_worker_set_color`** (`0x47e9b0`): op `0x203` sets a **static** color/alpha (`obj+0x60`),
  no anim bit. Sibling of `0x202`.
- **`gfx_object_composite`** (`0x47f650`): transform (`FUN_00472f00`) → anim interpolation
  (`FUN_00473ed0`) if the anim bit is set → **`gfx_object_blit_d3d9`** (`0x4774c0`).
- **`gfx_object_blit_d3d9`** (`0x4774c0`): clips the source rect to the surface bounds, selects a **blend
  mode** via `local_2c` (0 = opaque; 1 = alpha, `D3DRS_SRCBLEND=SRCALPHA`/`DESTBLEND=INVSRCALPHA`;
  2/3 = additive/special for glow/flash), and passes a **modulation color/alpha** (default `1.0f`) to the
  device draw. We do **not** reimplement D3D9 — we reproduce its observable effect (alpha blend, additive
  blend, tint modulation, colorkey transparency) in Godot.

## Architecture (approach 3: hybrid — engine resolves, host blits)

The engine resolves each visible object into a fully-specified **blend plan** (`RenderObject`); the Godot
host executes a dumb colorkey/alpha/tint/blend blit. The existing `Age.Engine/Model` → host-compositor seam
is unchanged. Rationale: the RE-derived *decision* logic becomes unit-testable in the engine xUnit suite
without building a software rasterizer; the pixel work stays in the host where Godot's `Image` already helps.

### Data flow

```
VM ops (engine thread)                     Model (GfxState)                     Host compositor (_Process)
set-texture 0x1f9(resId,slot,colorkey) →  _surfaces[slot]={resId,colorKey}
draw-texture 0x1fb(handle,slot,rect,pos)→ obj{slot,srcRect,V24,visible}
0x203 draw-color (handle, packedARGB)   → obj.Color   (static)  + HasColor
0x202 blit-color (handle, packedARGB)   → obj.ColorTarget (anim) + anim bit
0x234/0x238 (anim + global clock)       → obj.anim, AnimClock
                                                 │
                        SnapshotVisibleObjects() → RenderObject per visible obj, ascending-handle:
                        { Handle, SurfaceResId, ColorKey, srcRect, dstPos,
                          Alpha(0..255), Tint(RGB24), BlendMode(Opaque|Alpha|Additive), Anim }
                                                                    ├─► load BMP; bake ColorKey→α0 (cache by (path,key))
                                                                    ├─► Alpha = colorAlpha × animTweenAlpha
                                                                    └─► blit rect@dstPos with BlendMode + Alpha + Tint
```

### Units

**Unit 1 — Engine: `RenderObject` blend fields + resolution** (`Age.Engine/Model/GfxState.cs`; xUnit-tested)
- Extend `RenderObject` with `Alpha` (0–255), `Tint` (RGB24), `BlendMode` (`Opaque|Alpha|Additive`).
  `ColorKey` already exists.
- `GfxObject` gains a `HasColor` flag so an object that never received `0x202`/`0x203` resolves to
  **opaque (255)**, avoiding the "packed-0 ⇒ alpha 0 ⇒ invisible" trap.
- `SnapshotVisibleObjects` resolves `Alpha`/`Tint`/`BlendMode` from the object's static (`0x203`, `obj+0x60`)
  and animated (`0x202`, `obj+0x64`) color per the Task-0 formula; unchanged for objects with no color.
- Pure data transform — tested as **ops-in → RenderObject-fields-out** (no pixels).

**Unit 2 — Host: colorkey bake + alpha/tint/blend blit** (`godot/Main.cs` compositor)
- Image cache keyed by `(bmpPath, colorKey)`: on first load, set colorkey-matching pixels to α0.
- `BlitLayer` honors object `Alpha` (folds into the existing alpha path) and `Tint`, and selects the Godot
  blend for `BlendMode` (`Alpha` = normal, `Additive` = additive `CanvasItemMaterial`/manual add).
- Surfaceless colored objects (`SurfaceResId==0` **and** `HasColor`) fill a `Tint`×`Alpha` quad over their
  rect instead of being skipped.

**Unit 3 — Alpha source unification** (`AlphaFor` in `Main.cs`)
- Final opacity = anim-tween alpha (existing, from the `0x238` clock) combined with the object's static/
  animated color alpha, so a fade that ramps its alpha over the clock works and a static alpha holds.

## RE Task-0 (first task in the implementation plan — bounded)

Pin three unknowns in Ghidra (program already loaded: `build/engine-dump/range_00400000.bin`), each with a
known address and a concrete output. Verify each against the SC0000 opening via the `gfx` oracle (model)
and `--shot` (pixels). If a mode proves unused in the opening (e.g. additive), scope it out of this slice
and `log`/note it.

1. **Colorkey format & application** — `gfx_op_0x1f9_load_surface` (`0x422360` → resolver `0x44f390`):
   the colorkey value's pixel format (RGB888 / RGB565 / palette index) and confirm load-time transparency.
   *Output:* the exact compare the host uses when baking `(path, colorKey)`.
2. **Blend-mode source** — `gfx_object_blit_d3d9` (`0x4774c0`) `local_2c`: which object field selects
   opaque/alpha/additive and which op sets it. *Output:* `RenderObject.BlendMode` resolution + the
   D3D→Godot mapping.
3. **Color/alpha modulation & anim coupling** — `gfx_object_composite` (`0x47f650`) → `FUN_00473ed0`
   (anim interp) + `obj+0x60`/`obj+0x64`: how static (`0x203`) and animated (`0x202`) color resolve to a
   final (tint, alpha) at a given clock progress. *Output:* the `Alpha`/`Tint` formula in
   `SnapshotVisibleObjects`.

## Testing & acceptance

- **Engine (xUnit):** resolution tests — a small op sequence produces the expected `RenderObject`
  {Alpha, Tint, BlendMode, ColorKey}; explicitly cover the opaque-default (no color op) case and the
  static-vs-animated color paths.
- **Parity:** the engine suite, `sweep` (exit=284/STEP-LIMIT=13), and the Godot `--selftest` stay green
  (this slice adds model fields + host rendering; headless output is unaffected).
- **Host (pixels):** `--shot`/`--shot-sequence` on `SC0000 --boot` — the opening's fade should **fade and
  clear** (event-CG visible, no persistent opaque grey), and any keyed sprite composites without its
  background box. This is the eyeball acceptance the user validates.

## Non-goals

- Object placement / anchor / scale math (slice B).
- Off-screen render-target surfaces (slice C).
- Reimplementing D3D9; we reproduce its observable blend effects only.
- Ctrl fast-forward `Speed` (separate, already-seamed slice).

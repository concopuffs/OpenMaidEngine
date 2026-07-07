# Design: A2b — Graphics Geometry (the `0x208` keystone + blit compositor)

Status: **approved (design)** · Date: 2026-07-06
Related: `docs/phase-a-slice-plan.md` (A2b), `docs/superpowers/specs/2026-07-06-a2b-background-design.md`
(the first-pass render this builds on), `vm-map/opcodes.toml` (op `0x208`), `engine/Age.Engine`
(`Vm/VirtualMachine.cs`, `Hosting/IHost.cs`), `godot/{GodotAdvHost,Main}.cs`,
`engine/Age.Cli/Program.cs`, `build/disasm/SC0000.asm` (subroutine `label_12649`).

## Problem / goal

A2b's first-pass render composites the full-screen event-CG layer correctly, but **sprites and `BG*`
backgrounds routed through the CG-load subroutine have garbage geometry** — they land off-center and
their sizes come out `0×0`. The status doc framed the fix as implementing "the sprite
position/registration/animation chain," implying a large native subsystem.

**Recon overturns that.** In `SC0000.asm`, the CG-load subroutine `label_12649` computes all geometry
**in bytecode** using ordinary `add`/`sub`/`div`/`lookup-array` the VM already executes correctly. The
only missing native primitive is **`0x208 = get-texture-size(slot) → (out_w, out_h)`**. Because it is
stubbed, the width/height output globals stay `0`, so every downstream centering/anchoring computation
operates on zeros → garbage `dst`, `0×0` sizes.

The load-bearing sequence (from `label_12649`):

```
0x126e1:  set-texture (G[0x62424]=resId) (slot=G[0x62452]) -1      ; load the AGF into the slot
0x126e8:  op 0x208 (slot G[0x62452]) → w=G[0x62453], h=G[0x62454]  ; GET TEXTURE SIZE  ← stubbed = 0,0
0x126ef:  div local = w / 2
0x126f6:  sub G[0x62498] = G[0x6249b] - w/2       ; top-left x = anchorX - w/2   (horizontal center)
0x126fd:  sub G[0x62499] = G[0x6249c] - h          ; top-left y = anchorY - h     (foot anchor)
0x1270b:  draw-texture (handle) (slot) 0 0 w h dstX=G[0x62498] dstY=G[0x62499]
```

**Goal:** implement `0x208` (host returns the real dimensions of the image loaded in a slot) and replace
the TextureRect-per-slot approximation with a faithful **immediate-mode blit compositor**, so that
sprites and backgrounds position and size correctly — driven entirely by the executed bytecode. Add a
headless `gfx` diagnostic to verify computed geometry without launching Godot.

## Scope

**In:**
- `0x208` promoted from stub to a real VM op that writes texture dims into its output operands, backed by
  a new `IHost.GetTextureSize(slot) → (w, h)`.
- A **screen backbuffer blit compositor** in the Godot host: one mutable 800×600 `Image`; `draw-texture`
  blits the source slot's image (`src rect → dst`) into it in execution order; a single `TextureRect`
  displays it. Source images kept per slot (`dict<slot, Image>`) so the dims `0x208` returns come from
  the actually-loaded image.
- `Age.Cli gfx <SCENE.BIN> [0xADDR=VAL ...]` — dumps each executed `set-texture`/`draw-texture`/`0x208`
  with resolved file + computed geometry (mirrors the `audio` diagnostic), for a headless numeric check.
- Trace/selftest parity preserved (see below).

**Out (deferred, explicitly not this pass):**
- **Alpha/additive blend + `AE*` fades** (`0x202`/`0x203` and the fade/flash effects). The compositor is
  *built to accept* alpha later, but blend semantics are not implemented here — they need Frida to confirm
  op roles and would risk stalling this pass.
- **Green chromakey** for sprites (depends on whether AGF→BMP conversion already carries alpha; a separate
  question).
- **True multi-surface compositing** — resolving what the non-screen dest handles (the per-sprite
  `lookup-array`'d handles vs `0xcf08` = screen) actually mean. This pass **collapses every `draw-texture`
  dest onto the one screen canvas** (see below); genuine offscreen surfaces are a later, Frida-informed step.
- The sprite **animation** chain (`0x217`/`0x218`/`0x21a` write geometry globals for stored/animated
  records) beyond what static positioning needs. We treat these as they currently are unless a wrong
  picture forces otherwise.

## Why `0x208` is the keystone (and why the math is already correct)

The VM already runs `add`/`sub`/`div`/`lookup-array`/`lookup-array-2d` with validated pointer/lvalue
semantics (A0 RECOVER test). Every coordinate in `label_12649` is derived by those ops from two inputs:
the anchor globals and the **texture width/height**. The width/height are the *only* values the bytecode
cannot compute itself — they come from the loaded image via `0x208`. Supply them and the existing,
already-correct arithmetic yields correct `dst`/`w`/`h`. No new positioning logic is written in C#; we
provide one measurement primitive and let the bytecode do what it already does.

This also explains the two `0x208` calls around a `set-texture`: the engine preserves a
center-x / bottom-y **anchor** across a texture swap — measure old size to derive the anchor, load the new
texture, measure new size to re-derive the top-left. Both calls need real dims; a stub breaks both.

## Architecture

### Engine — `0x208` becomes a real op (`Age.Engine`)

`IHost` gains one method (mirrors the `WaitForInput`/texture-op additions):

```csharp
(int Width, int Height) GetTextureSize(int slot);
```

`VirtualMachine.Step` replaces the `default → OnStub` fall-through for opcode `0x208` with a handler that
reads the slot operand, queries the host, and **writes both dims into the output operands**:

```csharp
case "get-texture-size":   // 0x208 (slot) (out_w) (out_h)
    var (w, h) = _host.GetTextureSize((int)Read(a[0]));
    Write(a[1], w); Write(a[2], h);
    return pc + 1;
```

(The opcode is renamed from `u00420BF0` to `get-texture-size` in `opcodes.toml`, `source = inference`,
with the `label_12649` evidence; then `opcodes_build.py --build` regenerates the derived files.)

**Non-Godot hosts** (`CaptureHost`, test `RecHost`/`CountHost`) return `(0, 0)` → the handler writes
`0`/`0`, which equals the pre-change behavior (globals defaulted to `0` and were never written). So:
- `--selftest` (SC0000 show-text offset sequence) stays byte-identical.
- A1 trace-diff stays byte-identical (offsets + halt + **step count** — `0x208` was already one step via
  `OnStub`; it is still one step).
- Engine test count is preserved; add a `TextureOps`/geometry test asserting `0x208` writes host dims into
  its two output globals (with a fake host returning known dims).

### Godot host — screen backbuffer blit compositor (`godot/`)

Replace the `dict<slot, string bmpPath>` + TextureRect-per-slot model with:

- `GodotAdvHost`: `dict<int, Image> _slotImg` — `SetTexture(resId, slot)` resolves via `ResourceMap`
  (unchanged), loads the pre-converted BMP into an `Image`, stores it, and records its dims.
  `GetTextureSize(slot)` returns the stored image's `(GetWidth(), GetHeight())` (or `(0,0)` if empty).
- `Main`: one screen `Image _screen` (800×600, `Format.Rgba8`) shown by a single `TextureRect`.
  `DrawTexture(slot, sx, sy, w, h, dx, dy)` → `CallDeferred` a `BlitSlot` that
  `_screen.BlitRect(_slotImg[slot], srcRect(sx,sy,w,h), new Vector2I(dx,dy))` and refreshes the
  `TextureRect`'s `ImageTexture`. Execution order = paint order → correct z (full-frame bg first, sprites
  over it) with no separate layering logic.
- **Dest-handle collapse:** `draw-texture`'s dest handle arg is ignored for now; every draw blits onto the
  one screen canvas. A sprite the real engine routes through an offscreen buffer still lands on screen at
  its computed `dst`. If this collapse visibly double-draws or misplaces, that is the signal to implement
  true multi-surface compositing (a later, Frida-informed pass).

`IHost.DrawTexture` already carries `(slot, srcX, srcY, w, h, dstX, dstY)`; no signature change. The VM's
`draw-texture` dispatch is unchanged — it already passes those operands.

### CLI — the `gfx` diagnostic (`Age.Cli`)

`Age.Cli gfx <SCENE.BIN> [0xADDR=VAL ...]` runs the scene under a small recording host that implements
`GetTextureSize` from a lightweight in-memory image-dims lookup (reads the pre-converted BMP headers for
each resolved slot, no Godot) and logs, in execution order: each `set-texture` (slot ← resId → file), each
`0x208` (slot → returned `w×h`), and each `draw-texture` (slot, src rect, computed `dst`, `w×h`, resolved
file). Optional `0xADDR=VAL` seeds globals (same as `audio`). This gives a **headless numeric oracle**:
the background should resolve to `(0,0) 800×500`; sprites should be horizontally centered and foot-anchored
at plausible on-screen coordinates. It reuses the same section resolver — no new resolution logic.

## Data flow

```
bytecode (label_12649)                 host                                screen
──────────────────────                 ────                                ──────
set-texture(resId, slot)  ───────────► SetTexture: resolve → load BMP →
                                        _slotImg[slot] = Image (w×h known)
0x208(slot) → w,h globals ───────────► GetTextureSize(slot) → (w,h)
  (VM Writes w,h into globals)
add/sub/div (VM)  ── compute dst, size from anchor + w,h  ──►
draw-texture(_, slot, sx,sy,w,h, dx,dy) ─► BlitSlot: _screen.BlitRect(     ──► TextureRect shows _screen
                                            _slotImg[slot], (sx,sy,w,h), (dx,dy))
```

## Error handling / edge cases

- **Empty slot on `0x208`** (the pre-`set-texture` call in the anchor-preserve path): host returns
  `(0,0)`; the engine does the same when a slot has no texture. Acceptable — the second `0x208` (after
  load) drives the final draw.
- **BMP missing / load failure:** `SetTexture` leaves the slot empty (logs once); `GetTextureSize` → `(0,0)`;
  `draw-texture` on an empty slot is a no-op blit. Fails soft, never throws.
- **`dst` off-canvas / oversized src rect:** clamp the blit to the screen bounds (Godot `BlitRect` already
  ignores out-of-bounds regions; verify and clamp `srcRect` to the source image size).
- **Dims mismatch (AGF native vs converted BMP):** the converted BMP is authoritative for what we render;
  `0x208` returns the BMP dims so geometry matches what is actually blitted.

## Testing / verification

1. **Engine unit test** — `0x208` writes host-provided dims into its two output operands; parity assertions
   that `CaptureHost.GetTextureSize` returns `(0,0)` and leaves trace/step-count unchanged.
2. **`--selftest` + A1 trace-diff** — must stay byte-identical (SC0000 186-line offset sequence; all SC/SP
   step counts). This is the guardrail that the new op didn't perturb control flow.
3. **`gfx` diagnostic** — run `Age.Cli gfx SC0000.BIN`; assert (by eye, then a smoke check) the background
   resolves to `(0,0) 800×500` and sprite draws are centered/foot-anchored at sane coordinates (not `0×0`,
   not off-screen).
4. **Eyeball in Godot** — the opening renders with the background placed correctly and any sprites at
   plausible positions/sizes. This is the final human oracle (no machine oracle for pixels).
5. **Frida cross-check (only if numbers look wrong)** — capture the real game's `draw-texture` geometry and
   diff against the `gfx` dump.

## Risks / open questions

- **Dest-handle collapse is a simplification.** If the opening relies on offscreen surfaces (sprite buffers
  composited to screen with transforms), collapsing to one canvas could double-draw or misplace. Mitigation:
  the `gfx` dump + eyeball surface it immediately; true multi-surface work is the designated follow-up.
- **`0x217`/`0x218`/`0x21a` (`gfx-geom?`)** write geometry globals for stored/animated sprite records. Static
  positioning in `label_12649` flows through `draw-texture`'s explicit `dst`, so we expect these are not
  needed for a correct still. If a sprite is misplaced *despite* correct `0x208`, these are the next suspects.
- **Chromakey deferred:** sprites may render with a visible background box until chromakey/alpha lands. This
  is expected and out of scope; note it when eyeballing so it isn't mistaken for a geometry bug.
- **`vm0.py` divergence on branchy scenes** (known): use the C# VM / `gfx` diagnostic for non-opening scenes,
  not `vm0.py --settex`.

## Success criteria (this pass done)

- `0x208` implemented; `IHost.GetTextureSize` added; opcode renamed in `opcodes.toml` and rebuilt.
- Godot host uses the screen backbuffer blit compositor; source images kept per slot.
- `--selftest` and A1 trace-diff byte-identical; engine tests green (+1 geometry test).
- `Age.Cli gfx SC0000.BIN` shows the background at `(0,0) 800×500` and sprite draws with non-zero,
  centered/foot-anchored geometry (no `0×0`, no off-center background).
- Eyeball: SC0000 opening renders with correct background placement and plausibly-placed sprites.

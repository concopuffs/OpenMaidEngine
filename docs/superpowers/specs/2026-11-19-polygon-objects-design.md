# Polygon objects — design

**Status: accepted 2026-11-19** (the user: "Design is fine"). How the title's 3D scene is handled for P2 (§4) is
still the user's to decide. Written while bringing 創刻のアテリアル to P2 (`docs/adding-a-game.md`, step 6), from a
reading of its AGE 4.38D image.

## 1. Purpose

AGE 4.38D adds a second kind of retained graphics object: a polygon with a colour at each vertex. 創刻のアテリアル
draws its fades, panels and gauges with it. Four opcodes drive it, at 22,324 sites (`0x320` 3,856, `0x321` 42,
`0x322` 8,633, `0x323` 9,793), and the game's boot reaches them in its logo, before its title. The port has no
such object, so the game cannot reach P2 with handlers alone. This is the design for it.

The logo script shows the whole of it in fifteen instructions. It draws the logo picture as an ordinary object,
makes two full-screen black polygons above it, fades the first from opaque to clear over 500 ms and the second
from clear to opaque after 4,500 ms, waits, and erases all three with one `gfx-elem-erase`.

## 2. What native does

Every claim here was read in `/arterial/4.38D/range_00400000.bin` on 2026-11-19. Each function named carries the
reading in its plate comment.

**The record.** The graphics manager keeps a second map beside the retained objects', keyed by the same handles
(`gfx_polygon_get_or_create@0x4938f0`; its head is at manager `+0x42c`). A record is 15 dwords:

| Offset | Field | Default |
|---|---|---|
| `+0x00` | flags: 1 = made, 2 = a fade runs, `0x10000` = not read | 0 |
| `+0x04` | vertex buffer | none |
| `+0x08`, `+0x0c`, `+0x10` | kept copies of each vertex's x, y and diffuse | none |
| `+0x14` | vertex count | 0 |
| `+0x18` | op `0x320`'s flags: bit 0 = triangle fan, else triangle strip | 0 |
| `+0x1c`, `+0x20` | the surface slots of texture stages 0 and 1 | -1, -1 |
| `+0x24` | blend mode | 0 |
| `+0x28`, `+0x2c`, `+0x30` | fade start time, delay, duration (ms) | 0 |
| `+0x34` | colour (ARGB) | `0xffffffff` |
| `+0x38` | fade target (ARGB) | `0xffffffff` |

**The opcodes.**

| Op | Operands | What it does |
|---|---|---|
| `0x320` | handle; float cells x, y, z; integer cells alpha, RGB; float cells u, v; count; flags | Makes the polygon. A vertex is x - 0.5, y - 0.5, z, rhw 1, alpha << 24 \| RGB, and u, v for both texture stages (FVF `0x244`). A count below 1 traces a script error. |
| `0x321` | handle, stage, surface slot | Sets the surface of texture stage 0 or 1. |
| `0x322` | handle, blend mode, alpha, RGB | Sets the colour and the blend mode. Alpha `0x100` or more is `0xff`. A negative alpha or RGB keeps the current one. |
| `0x323` | handle, delay, duration, alpha, RGB | Starts a fade of the colour to a target, built as `0x322` builds it. |

**The colour.** A polygon's colour multiplies each vertex's own, channel by channel, as `kept * colour / 255` in
integers. The colour `0xffffffff` leaves the vertices as they are (`gfx_polygon_calc_diffuse@0x48b9d0`).

**The fade** runs when the polygon is drawn (`gfx_render_polygon@0x496d10`):

- its start is the frame time of its first draw;
- a duration below 1 only clears the fade: the target is never applied;
- before `start + delay` nothing changes;
- until `start + delay + duration` the colour drawn is the two colours mixed, each channel truncated;
- from then on, or at once when manager `+0xb590` is set, the colour becomes the target and the fade ends.

While a fade runs, each draw sets two manager flags: `+0xb58c`, the dirty flag every polygon worker sets, and
`+0xb594`. `gfx_render_frame@0x49ab00` clears both before its walk; after it, a walk that left `+0xb594` clear
calls `0x492d10`, and the frame render clears `+0xb590`. The logo script starts its two fades and then runs op
`0x21c`, the effect wait, so `+0xb594` reads as what keeps that wait waiting and `+0xb590` as the force-complete
request a click or fast-forward makes. `0x492d10` and the flags' other readers were not read; acceptance 3 checks
the wait against native.

**The draw** (`gfx_draw_polygon_primitive@0x4895b0`): alpha blending is on, and the blend mode chooses how the
polygon lands:

| Mode | Blend |
|---|---|
| 0 (and any other) | source alpha, inverse source alpha |
| 1 | source alpha, one (additive) |
| 2 | one, zero: the polygon replaces what is under it |
| 3 | reverse subtract, source alpha, one |

Mode 2 sets its blend only for a render target whose `+0x418` is 1, and otherwise draws with the state the walk
left, which is the same one, zero. With one texture, the texture is multiplied by the diffuse. With two, stage 1
is blended over stage 0 by stage 1's alpha, then multiplied by the diffuse. Textures clamp and are filtered
linearly. With none, the routine sets no stage state; the diffuse alone is expected, and acceptance 3 checks it.

**The order.** `gfx_render_frame@0x49ab00` and `gfx_present_object_range@0x49adf0` walk the retained objects'
map (manager `+0x40c`) and the polygons' (`+0x42c`) together. The frame render takes the object while its handle
is below the polygon's, and the polygon otherwise, so a polygon is drawn where its handle falls among the
objects. The range present walks the same way within its range; its comparison was not read line by line.

**Not read yet:** what sets flag `0x10000` (each walk clears it after the polygon's turn); how
`gfx-elem-erase` and the scene reset remove a polygon; whether a numbered save keeps polygons; how the z of a
vertex is used (the vertices are pre-transformed, so it can only matter with a depth buffer); and the rewrite of
positions for some render targets (manager `+0xb558` of `0x26` and `0x27`).

## 3. The design

By layer, as the engine layering spec orders them (`2026-11-12-engine-layering-design.md`).

**Handlers** (`Ome.Engine.Vm`). Four rows in the handler table: `create-polygon`, `set-polygon-texture`,
`set-polygon-color`, `fade-polygon-color`. Each reads its operands and calls the graphics native. `create-polygon`
reads its seven arrays through the VM's address resolver; the float arrays need float cells addressed as arrays,
which is the float pointer work below.

**The graphics native** (`Ome.Engine/Natives/Graphics*.cs`, `Model/GfxState*`).

- `GfxState` gains a polygon map beside its retained objects, keyed by handle, in a private `Scene` so that op
  `0x9` drops it. A polygon holds what the record holds: vertices (x, y, z, own ARGB, u, v), fan or strip, two
  surface slots, blend mode, colour, and the fade (start, delay, duration, target).
- Erase, release and range operations treat a handle's polygon as they treat its object, once native's side of
  each is read (§2, "Not read yet"). Until one is read, the operation leaves polygons alone and the gap is listed.
- The fade is sampled where the frame is lowered, from the frame clock, with native's rules above. It reports
  itself to the effect wait and the force-complete request as the existing one-shot channels do, so op `0x21c`,
  click-skip and fast-forward need no polygon case of their own.

**The frame description** (`Ome.Engine.Devices`). One new item kind, `Polygon`: a triangle list in canvas
pixels, each vertex with its position, its final ARGB (the colour already multiplied in) and its u, v; up to two
textures; a blend. The native lowers a strip or a fan to triangles and merges polygons into the item list in
handle order with the objects. `BlendKind` gains `Subtractive`. The -0.5 offset stays out of the description: it
is Direct3D 9's pixel-centre rule, which each renderer already owns.

**The renderers.** The reference renderer defines the item first: it fills each triangle with colours
interpolated across it, samples its textures with clamped bilinear filtering, and lands it with the item's blend.
The GPU renderer then draws the same item as a vertex-coloured mesh, and declines a frame that holds a polygon
until it does, as it declines what it cannot draw today. `reference-diff` holds the two together.

**The blends** map to `BlendKind`: mode 0 `Alpha`, 1 `Additive`, 2 `Opaque`, 3 the new `Subtractive`.

## 4. What this leaves out

**The title's 3D scene.** Ops `0x324` to `0x32f` are a separate module. Its workers were read on 2026-11-19, after
this design was proposed, and the first version of this paragraph had two of them wrong (it took the lights for
mesh instances). Each function carries its reading in the Ghidra image.

- **Meshes:** 1,000 mesh slots, each a Direct3D `.X` file loaded with its materials (`0x329` loads, `0x32a` and
  `0x32b` release).
- **Camera and lights:** a look-at view (`0x32c`), an ambient colour (`0x32d`), and Direct3D lights with a type,
  a position, a direction, a colour and a range (`0x32e` sets one, `0x32f` disables one).
- **Three effect objects** the display draws during the frame walk, when the walk reaches two handles that op
  `0x325` names: the mesh particles, which op `0x328` builds from a list of mesh slots, and two others that ops
  `0x326` and `0x327` build. Op `0x324` releases all three.

The title's drifting white feathers are the mesh particles, built from `LEAF0.X` and `LEAF1.X`. The engine moves
them; the script only builds the object. Native screenshots taken seconds apart show them in different places. In
the game only TITLE.BIN and DEBUG.BIN use the group, at 23 sites; GAMESTART and CALLBACK_LOAD call the two
releases, and only DEBUG.BIN builds the two other effects. What the mesh particles do each frame, what the two
other effects draw and how the `.X` files are encoded were not read.

Two ways to handle it:

- **State only.** The twelve handlers keep what the scripts set and draw nothing. The title shows everything but
  the feathers, and the gap is listed in the backlog.
- **Drawn.** The port reads the `.X` files, reproduces the mesh particles' motion from the native object, and
  draws them. The polygon item of §3 can carry them: the graphics native transforms and lights each mesh on the
  engine thread and emits triangles, so neither renderer needs a 3D pass. It is a design of its own, and its
  frames cannot be compared with native's pixel for pixel, since the motion runs on the clock.

The proposal for P2 is state only, with the drawn version after P2. The title, its menu and Game Start do not
depend on the feathers.

**Float references.** The scripts pass local float references (operand type `0xd`) at 584 places, among them op
`0x320`'s arrays. It is VM work, not graphics work, and it is done (2026-11-19; `engine-re.md`, "Float cells as
arrays, and float references"): `create-polygon` reads its float arrays through the address resolver as it reads
its integer ones.

## 5. Acceptance

1. Synthetic tests of the four handlers and of the fade's rules: the truncating mix, a duration below 1, the
   delay, the force flag, and the end state.
2. Reference renderer tests of a polygon item: a flat triangle, a colour gradient, a textured quad, and each
   blend.
3. `tools/drive/scenarios/arterial-opening.steps` reaches the title on the port, and the logo's fade frames match
   native AGE's at the same times, checked at a low `speed`.
4. `reference-diff` passes on a frame with polygons once the GPU renderer draws them.
5. Himegari's and Kamidori's gates are unchanged: neither game's scripts use these opcodes.

## 6. Milestones

1. Float references in the VM (done 2026-11-19, ahead of this design's acceptance: it stands alone).
2. The polygon map, the four handlers and the fade, with their tests; the frame item and the reference renderer.
   The port reaches the logo's end on the reference renderer.
3. The GPU renderer draws polygon items.
4. The remaining reads of §2 (erase, reset, ranges, saves, flag `0x10000`), each with its handling or its
   backlog entry.

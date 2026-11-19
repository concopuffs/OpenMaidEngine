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
| `+0x00` | flags: 1 = made, 2 = a fade runs, `0x10000` = skip this walk (the pre-pass has drawn it) | 0 |
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
calls `0x492d10`, and the frame render clears `+0xb590`. They are the one-shot channels' own flags.
`gfx_object_evaluate_one_shot` and the colour and transition workers set `+0xb594` too, and
`gfx_animation_service_poll`, the effect wait's service, reads it. `gfx_request_force_complete_and_reset_anim_clock`
and op `0x243` set `+0xb590`, and `gfx_object_evaluate_one_shot` reads it as the polygon's draw does. So a running
polygon fade keeps op `0x21c` waiting, and a click or fast-forward ends it, exactly as a channel's.

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

**The polygon's life.** Read while building (2026-11-19). A handle's polygon goes where its object goes:

- `gfx_object_erase@0x4942a0` and `gfx_object_erase_range@0x494440` (op `0x1f7`, and the text code) take the
  handles out of both maps;
- `retained_gfx_objects_clear@0x494100` (op `0x1f6` and the scene reset) clears both;
- `gfx_object_clone@0x494880` (op `0x21d`) copies the polygon record whole, fade and colours included, whether or
  not the source handle has an object;
- op `0x214`'s swap does not touch polygons.

The walks' pre-pass (`0x497620`) draws the polygons of a transition's ranges into its surfaces with their objects
(it calls `gfx_render_polygon` at seven places) and the main walk then skips what it drew: that is flag
`0x10000`, which objects have too.

**Not read yet:**

- whether a numbered save keeps polygons (no function that reads the polygon map is a save's);
- the depth test: the frame walk runs with it on (the 3D effects turn it off and back on), each present clears
  depth with colour once op `0x252` says so, and a vertex z of 0, which the logo gives, passes; what another z
  does was not looked for;
- whether native culls a polygon by its winding (Direct3D's default culls counter-clockwise triangles);
- whether a surface's texture is larger than its image, so that u = 1 is the texture's edge and not the image's;
- the rewrite of positions for some render targets (manager `+0xb558` of `0x26` and `0x27`), which undoes the
  window's scaling: the port draws in canvas units everywhere, so it has no counterpart.

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

**The blends** map to `BlendKind`: mode 0 `Alpha`, 1 `Additive`, 2 `Opaque`, 3 the new `Subtractive`. (Built
otherwise: §7.)

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

The title's drifting white leaves are the mesh particles, built from `LEAF0.X` and `LEAF1.X`: native's error
texts name op `0x328` `Set3DEffectLeaf` and op `0x326` `Set3DEffectSnow`. The engine moves the leaves; the script
only builds the object. Native screenshots taken seconds apart show them in different places. In
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
   native AGE's at the same times, checked at a low `speed`. (Measured for the logo on 2026-11-19, §7; the
   scenario waits for the title.)
4. `reference-diff` passes on a frame with polygons once the GPU renderer draws them.
5. Himegari's and Kamidori's gates are unchanged: neither game's scripts use these opcodes.

## 6. Milestones

1. Float references in the VM (done 2026-11-19, ahead of this design's acceptance: it stands alone).
2. The polygon map, the four handlers and the fade, with their tests; the frame item and the reference renderer.
   The port reaches the logo's end on the reference renderer. (Done 2026-11-19.)
3. The GPU renderer draws polygon items. (Done 2026-11-19, §7.)
4. The remaining reads of §2 (erase, reset, ranges, saves, flag `0x10000`), each with its handling or its
   backlog entry. (Erase, reset, clone and the flag are read and handled; the rest is §2's list.)

## 7. As built (2026-11-19)

Milestone 2 is built: `GfxState.Polygons.cs`, `VirtualMachine.Polygons.cs`, the frame description's `Polygon` item
(`FramePolygon.cs`), `SoftwareAffineRasterizer.Polygons.cs` and the reference renderer; `PolygonObjectsTests`.

**Departures from §3.**

- **The blend is the polygon's own**, `FramePolygonBlend` (`Alpha`, `Additive`, `Replace`, `Subtractive`), not
  `BlendKind`. The rasterizer draws `BlendKind.Opaque` as source-over, which is what a mode-0 object needs, so it
  could not also mean a polygon's copy.
- **A polygon rides the snapshot as a `RenderObject`** with its `Polygon` set, in its place by handle. The range
  filters, a transition's range and a render target's publication then treat it as they treat an object, with no
  second list to keep in step.
- **Its triangles are kept**, and replaced when the vertices, the colour shown or the blend mode change, so a
  frame costs no allocation for a polygon that did not change and a renderer may keep what it built from them.
- **A polygon in a blur's source range is drawn unblurred** (`backlog.md`).
- **The record keeps the colour last written to the vertices** (`GfxPolygon.ShownColor`). Native writes it into
  the vertex buffer and the buffer keeps it between draws, so a fade that another replaces leaves its last drawn
  colour showing through the new one's delay.

**Measured against native AGE.** Both were driven through the logo with a screenshot about every 0.1 s
(`tools/drive.py`, the port with the reference renderer and again with the default backend, which hands a frame
that holds a polygon to the reference renderer). The logo script shows a notice under two full-screen black
polygons: one fades out over 500 ms, the other fades in after 4,500 ms.

- With both polygons clear, the port's frame equals native's in every pixel.
- The fade-in and the fade-out each take about 0.5 s on both, with 4.0 s between them, and a pixel's value falls
  in step with the polygon's alpha on both.
- The effect wait that follows ends with the second fade on both; the logo movie then starts 2.6 s later on
  native and 2.4 s on the port, within what a screenshot every 0.1 s and a decoder's start can tell apart.

A frame at the same instant of a fade on both was not captured, so the mix's truncation is checked only by
`PolygonObjectsTests`, against the arithmetic read in the image.

**Milestone 3, the GPU renderer.** `GpuRetainedRenderer` draws a polygon item as a mesh of its triangles with a
colour at each vertex (a `MeshInstance2D`), in the item's place among the sprites: a pool slot that draws a
polygon shows its mesh node, which sits right after the slot's sprite, in place of the sprite. The mesh is built
again only when the item's geometry is another object. The alpha, additive and subtractive blends are canvas
blend modes, the replace blend and a second texture are shaders.
`tools/drive/scenarios/port/arterial-reference-diff.steps` compares it with the reference renderer on the logo:
no pixel differs with both polygons clear, and at most one step of a channel in two frames of the fade (the GPU
rounds where the reference truncates). A textured polygon, the three other blends and a second texture are not
compared: the port reaches no screen that uses them yet (`backlog.md`).

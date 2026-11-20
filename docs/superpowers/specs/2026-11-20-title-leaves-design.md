# The title's leaves — design

**Status: accepted 2026-11-20, with the user's three changes** (§7), **and built the same day** (§8). Draws the
leaves that drift over 創刻のアテリアル's title: AGE 4.38D's leaf effect, op `0x328`, which the polygon objects design
left as state only (`2026-11-19-polygon-objects-design.md` §4; the backlog item "The title's 3D leaves are not drawn",
which the build resolved). Everything in §2 was read in `/arterial/4.38D/range_00400000.bin`
on 2026-11-20, unless it says otherwise; each function named carries its reading in the Ghidra image.

## 1. Purpose

The title shows thirty grey-white leaves falling and turning over its picture. They are what the port's title lacks:
between 2,000 and 5,000 of its pixels differ from native's, depending on where the leaves are. The scripts only set up
the scene, and the port keeps that state (`Graphics.Scene3D.cs`). The engine moves the leaves and draws them. This
design draws them with what the port has: the polygon item from the polygon objects design. Neither renderer gets a
3D pass.

## 2. What native does

**The script** (TITLE.BIN, `0x62a` to `0x675`):

| At | Op | Values |
|---|---|---|
| `0x62a` | `0x325`, the handles | 2, 2 |
| `0x62f` | `0x32d`, the ambient colour | alpha `0xff`, RGB `0xffffff` |
| `0x63b` | `0x32e`, light 0 | type 3 (directional), position 0, direction (1, -0.3, 1), alpha 0, RGB `0xcc9966`, range 1000 |
| `0x659` | `0x32c`, the camera | eye (0, 4, -8), target (0, 1, 0) |
| `0x666`, `0x66b` | `0x329` | `LEAF0.X` into slot 0, `LEAF1.X` into slot 1 |
| `0x675` | `0x328` | 30 leaves, from slots 0 and 1 |

The title's picture is handle 1, and its logo and menu are handles `0xa` and up. The leaves are drawn above the
picture and under everything else. `label_9e6` releases the effects (`0x324`), then the objects, then the meshes
(`0x32b`). Op `0x328`'s first operand is the number of leaves; the port calls it an id, which it is not.

**The meshes** (`mesh_load_from_x_memory@0x472630`). The loader works in four steps:

- It loads the file with `D3DXLoadMeshFromXInMemory` (`0x4aa81d`), with options `0x220` (managed).
- It copies each material, setting the material's ambient to its diffuse.
- It keeps no texture.
- It optimizes the mesh in place with `0x08000000`, `D3DXMESHOPT_STRIPREORDER`, which sorts by attribute and
  reorders the faces.

D3DX's loader collapses a file's frames into one mesh and applies each frame's transform (its documented behaviour).

The two files are text `.X` (`xof 0303txt 0032`). Each holds:

- one frame, `Cylinder01`, whose `FrameTransformMatrix` scales by 0.52, 0.52 and 0.82 and shifts by a few hundredths;
- one `Mesh` of 181 positions and 556 triangles;
- a `MeshNormals` with 290 normals and its own face list;
- a `MeshTextureCoords`;
- a `MeshMaterialList` of one material: diffuse (0.867, 0.867, 0.867, 1), power 0, no specular, no emissive, no
  texture.

496 of the 556 triangles have a twin wound the other way, so the leaves are two-sided. After the frame transform a
leaf measures about 0.08 by 0.02 to 0.05 by 0.19 units.

**The effect.** `display_build_leaf_effect@0x44f5c0` builds it as `effect_leaf_construct@0x472020` with vtable
`0x5ac78c`. It holds:

- the meshes, in the order given;
- a world matrix: the manager's, which is identity (`FUN_004907f0` sets it, and nothing else writes it);
- the leaves, which `leaf_effect_create_particles@0x472440` makes. Each is respawned at once.

**A leaf** (`leaf_particle_construct@0x4722b0`, 0x70 bytes):

| Field | Offset | Respawn (`leaf_particle_respawn@0x471a90`) |
|---|---|---|
| position | `+0x00` | x = 10·c, y = 5·u, z = 10·c |
| velocity | `+0x0c` | vx = 0.01·c, vy = -0.01 - 0.01·u, vz = 0.01·c |
| rotation (yaw, pitch, roll) | `+0x18` | 2π·u each |
| angular velocity | `+0x24` | 0.03·c each |
| scale | `+0x30` | 1 + 2·u |
| mesh | `+0x6c` | `rand() % ` the number of meshes |

- **The draws.** u = (`rand() % 100`) / 100 (`rand_unit_percent@0x471990`), and c = u - 0.5 (`rand_centered@0x4719c0`).
  Each is a fresh draw, made in the table's order, x first and the mesh last: fourteen draws a respawn.
- **The random source.** `rand` is the C runtime's (`crt_rand@0x585975`, MSVC's linear congruence), the same one op
  `0x60` draws from.
- **The constants.** The script gives none of them: six come with the constructor's call, and four are fixed in
  `leaf_effect_create_particles`.

**A step** (`leaf_particle_step@0x471c10`) happens 60 times a second:

1. position += velocity;
2. rotation += angular velocity;
3. vx += 0.001·c, then vz += 0.001·c;
4. if y < 0, respawn, then set y = 5, so the leaf starts again at the top.

Nothing damps the drift.

**The clock** (`display_scene_effects_step_60hz@0x44f6f0`) runs after every frame walk:

- ticks = ⌊(now - base)·60/1000⌋ - ⌊(last - base)·60/1000⌋, read from `timeGetTime`, at most 100;
- each tick steps every leaf;
- then last = now.

It runs whether or not an effect exists, so a new effect starts from the present.

**Where it is drawn** (`display_scene_effects_at_handle@0x44f7a0`):

- **When.** The frame walk (`gfx_render_frame@0x49ab00`) calls this function with each object's or polygon's handle
  before drawing that item, and with -1 at the end. So the leaves are drawn once a frame: before the first item
  whose handle is at or past op `0x325`'s second handle, or after the last item.
- **The state around them.** It turns the depth test and depth writes off and blends with source alpha and inverse
  source alpha. Afterwards it turns depth back on and sets the blend to one, zero.
- **Order.** `leaf_effect_draw_all@0x471e50` draws nothing when any mesh pointer is null. Otherwise it draws the
  leaves in index order, not by depth.

**How a leaf is drawn** (`leaf_particle_draw@0x471c80`):

- **The world matrix.** world = Scaling(s) · `RotationYawPitchRoll`(yaw, pitch, roll) · Translation(position) · the
  effect's world. This is D3DX's row-vector order: roll first, then pitch, then yaw (`0x4a6fc7`).
- **The colours.** Every material colour (diffuse, ambient, specular, emissive) is multiplied by (1, 1, 1, a), with
  a = sin(π·(5 - y)/5) (`crt_sin@0x586ba0`). A leaf fades in at the top, is opaque at half height, and fades out at
  the bottom.
- **Each subset.** `SetMaterial`, `SetTexture(0, null)`, `DrawSubset` (`mesh_draw_scaled_materials@0x472790`).

**The scene** (`gfx_setup_3d_transforms_and_lights@0x486260`) is set at device setup and reset:

- the projection is `PerspectiveFovLH`(π/4, the viewport's width over its height, 1, 1000);
- the view is op `0x32c`'s `LookAtLH` with up (0, 1, 0), and identity until that op runs;
- the world is identity;
- lighting is on, with op `0x32d`'s colour as the ambient;
- the enabled lights are set again.

Op `0x32e` fills a `D3DLIGHT9` with only its diffuse, position, direction and range: its attenuations are 0.

**Direct3D 9's fixed function** then does the rest. This is its documented behaviour, not code read in AGE:

- **The colour.** vertex colour = material emissive + material ambient × the ambient colour + the sum over the lights
  of (material ambient × light ambient + material diffuse × light diffuse × max(0, N·(-direction))). Each channel is
  clamped to 0..1. The alpha is the material diffuse's alpha. Specular is off by default.
- **The normals.** N is the normal transformed by the inverse transpose of world·view, and it is not renormalized:
  `D3DRS_NORMALIZENORMALS` is false by default, and no `SetRenderState` with it as an immediate was found. A leaf
  scaled by s gets 1/s of the light.
- **Culling.** Triangles wound counter-clockwise on screen are culled: `D3DCULL_CCW` is the default, and no
  immediate `SetRenderState` of `D3DRS_CULLMODE` was found below `0x4b0000`. Since the meshes are two-sided, a leaf
  shows only its front faces.
- **The texture.** A null texture in stage 0 reads as white, so the lit colour is drawn alone.

For the title, this gives each leaf 0.867 from the white ambient, plus the light's (0.8, 0.6, 0.4) × 0.867 on the
faces turned towards it. The leaves are grey-white and warmer on their lit side.

## 3. The design

By layer, as the engine layering spec orders them.

**The `.X` reader** (`Ome.Engine/Sys4/XMeshDecoder.cs`):

- **The format.** It reads a text `.X` file (`xof 0302txt` or `0303txt`, with 32- or 64-bit floats) into one mesh.
  The mesh has positions, a normal at each triangle corner, triangles, each triangle's material, and the materials:
  diffuse, power, specular and emissive.
- **What it reads and skips.** It reads `Frame`, `FrameTransformMatrix`, `Mesh`, `MeshNormals`, `MeshMaterialList`
  and `Material`. It skips templates and every other data object by its braces: `MeshTextureCoords`, `AnimationSet`,
  `AnimTicksPerSecond`, and a `TextureFilename`, which the port would not use.
- **Frames.** It collapses them as D3DX does. Positions are multiplied by the product of the frame matrices; normals
  are multiplied by that product's inverse transpose, then normalized.
- **Faces.** A face of more than three corners becomes a fan.
- **Refusals.** A binary or compressed file (`bin `, `tzip`, `bzip`) is refused with a message that names it. Op
  `0x329` then fails, as a load that D3DX refuses does, with native's script error. No shipped game reaches it.

The reader runs once, when op `0x329` loads the slot, and the slot keeps the mesh (`Scene3DMesh`, immutable). The
effect holds the meshes themselves, so releasing a slot later cannot change it. Native would draw freed memory there;
the title releases the effects first.

**The effect** (`Graphics.Scene3D.cs` and a new `Scene3DLeaves.cs` beside it):

- **Building.** `SetLeafEffect(count, slots)` builds the leaves with native's respawn and its fourteen draws.
- **The random source.** It is the game's generator, the one op `0x60` and Lua's `math.random` draw from when no
  `rand-seed` is set: a new `CrtRandom` (`Hosting/CrtRandom.cs`), MSVC's `rand` seeded from the tick count when the
  engine starts, as native's context constructor seeds it. It replaces `System.Random.Shared` there. Composition
  gives the graphics native and script memory the same one, so the leaves' draws advance op `0x60`'s stream as they
  do on native (§7).
- **Edge cases.** A count below 1 builds no leaves. With no mesh the effect draws nothing; native would divide by
  zero there.

**The step** runs where the frame is lowered. `LowerFrame(nowMs)` first emits the leaves where they are, then steps
them by ⌊nowMs·60/1000⌋ - ⌊lastMs·60/1000⌋ ticks, at most 100. That is native's order: draw, then step. Lowering
twice at the same time steps nothing.

**The lowering** takes the leaves in index order. For each leaf:

1. Build its world matrix, then transform each corner by world·view·projection.
2. Light each corner as §2 says.
3. Place each corner on the canvas through the viewport: x = (1 + x/w)·W/2, y = (1 - y/w)·H/2, plus half a pixel. The
   half pixel makes the renderers' Direct3D 9 pixel-centre rule land the corner where Direct3D does.
4. Drop each triangle that is wound counter-clockwise on screen, or that has a corner nearer than the near plane.
5. Emit the leaf as one polygon item: its triangles in the mesh's order, untextured, with the alpha blend and opacity 1.

**Where the items go.** They go into the frame's item list where native draws them: before the first object or
polygon whose handle is at or past the second handle, or after the last item. During a screen transition, the
target's walk gets them. The source's walk and op `0x222`'s range presents do not (§4).

Why one item for each leaf: after culling, a leaf has about 280 triangles, so each item's vertex array stays small.
The 30 leaves leave about 0.5 MB of short-lived arrays a frame. One item for all of them would put a 500 KB array on
the large-object heap every frame.

**The renderers** do not change. A polygon item already draws both windings and its vertex colours, which is all
that the lowering gives it.

**The state.** `Scene3DLeafEffect` keeps the count, the meshes and the leaves. It is not scene state, as now, and it
is not saved, since native saves none of it.

## 4. Departures, and what it leaves out

- **The rest of the C runtime's draws.** On native the main tick, the screen transitions, the save encoder and the
  catalog parse draw from the same stream too (`engine-re.md`, "Op 0x60 randomness"). The port does not model them,
  so its sequence never matches native's, which is seeded from the tick count anyway.
- **The face order.** D3DX's strip reorder changes the order of a mesh's faces; the port draws them in the file's
  order. With depth off, this changes only which of a leaf's overlapping triangles lands last. Both are within a few
  levels of the same colour.
- **The frames' normals.** How D3DX transforms normals when it collapses frames was not read. The port uses the
  inverse transpose, normalized.
- **Lights other than directional** are not drawn; the title sets one directional light. The lowering skips a point
  light or a spotlight and reports it once on the timeline.
- **The snow effect (op `0x326`) and the first effect (op `0x327`)** stay state only, because only DEBUG.BIN builds
  them.
- **No leaves in op `0x222`'s range presents or a screen transition's source.** Native's range present
  (`gfx_present_object_range@0x49adf0`) calls the same draw at `0x49af7c`, `0x49b02b` and `0x49b0ec`, and steps the
  effects at `0x49b0f7`. A drawn flag (`+0x4e0`) keeps them to one draw per step. No shipped script presents a range
  while leaves exist.
- **The pixels.** The motion runs on the clock and on a random stream, so no frame can be compared with native's
  pixel for pixel.

## 5. Checks

- **Tests** use synthetic data, never a game file:
  - the reader, on a small text `.X` with a frame, a four-corner face and two materials, and the refusal of a binary
    header;
  - a respawn's fields from a known `rand` sequence;
  - the 60 Hz tick count and its cap, and a respawn below y = 0 that ends at y = 5;
  - the projection of known points against the D3DX formulas;
  - a counter-clockwise triangle dropped;
  - the lighting of a corner facing the light and of one facing away, and the alpha at y = 5, 2.5 and 0;
  - the items' place among the objects by handle, and last when no item is at or past the handle.
- **The port.**
  - The title shows the leaves.
  - `reference-diff` on the title holds the GPU renderer to the reference renderer.
  - `--perf-log` gives the title's frame cost.
- **Against native, on a screen.** Shots of the two titles side by side, compared for the number of leaves on
  screen, their size, colour, fade and speed. Done when they look alike, as the backlog item says.

## 6. Milestones

1. The reader, with the mesh kept in its slot (tests).
2. The effect: the leaves, the random source and the step (tests).
3. The lowering and its place in the walk (tests, title shots on the port, `reference-diff`).
4. The native comparison, then the references: the backlog item removed, a pointer in `engine-re.md`, the status.

## 7. The user's changes (2026-11-20)

- **One stream.** "I think drawing them from the same random number stream should be kept, if they influence RNG then
  that behavior should remain the same. If it'll cause testing issues, maybe use a parameter or something for dev
  drives to split them to their own stream." The leaves draw from the game's generator (§3). No parameter is needed:
  under the driver's `rand-seed`, op `0x60` already draws from streams of its own on both targets (and Lua does on the
  port), which no other draw shifts, so a seeded drive is the same with the leaves as without them.
- **One item a leaf**: "fine as long as it doesn't impact anything". `--perf-log` on the title checks it.
- **What §4 leaves out** is backlogged: "fine to leave out today but we should make sure we backlog it, at least the
  ones within DEBUG.BIN are testable. If the others have no script that exercises them, then its fine to leave out
  today. technically we can build a script to test them with OMS, but we can backlog that for later". The backlog
  item "The 3D scene's other effects and cases are not drawn" lists them: the two effects DEBUG.BIN builds, which a
  run of DEBUG.BIN can test, and the cases no shipped script reaches, for an OMS test script.

## 8. The build (2026-11-20)

The four milestones were built on `feature/arterial`: `Sys4/XMeshDecoder.cs`, `Hosting/CrtRandom.cs`,
`Natives/Scene3DLeaves.cs` and `Natives/Graphics.Scene3DLowering.cs`, with `Scene3DLeavesTests` for §5's tests.

**What the checks found.**

- **The renderers.** `reference-diff` on the title, three frames with the leaves: no pixel over the tolerance, the
  largest difference 3 levels.
- **Against native.** Twelve shots of each title, a second apart; each shot's leaves are its difference from the
  per-pixel median of its twelve:

  | | native AGE | the port |
  |---|---|---|
  | leaves seen in a shot, median (range) | 19 (15 to 24) | 20 (15 to 26) |
  | leaf pixels in a shot, median (range) | 2,564 (1,706 to 3,895) | 3,368 (1,999 to 4,736) |
  | mean level of the leaf pixels | 213 | 212 |

  Zoomed, the leaves have the same white, feathered shape and size on both.
- **The cost of one item a leaf** (`--perf-log`, an optimized build, 2,454 frames of the title): the engine's turn
  0.51 ms at the median (0.93 ms at the 95th percentile), the lowering to the draw 1.15 ms (1.58 ms), the GPU
  renderer's item updates, the leaves' mesh rebuilds among them, 0.63 ms (0.82 ms). The leaves allocate about 0.28 MB a frame on the engine thread and
  0.46 MB on the front end: 37 gen-0, 11 gen-1 and no gen-2 collections over the run. Frames stayed at 10 ms.

**What the build adds to §3.**

- Op `0x328` halts the VM on a leaf count above 2^20 (`set-3d-effect-leaf-leaves-too-many`), as it already did on a
  slot count that large: native would allocate that many leaves.
- The lowering drops a triangle with a corner past the far plane as well as the near one: Direct3D clips both.
- The leaves' 60 Hz clock and the lowering's scratch arrays are kept across op `0x9`'s reload, as the effect is:
  native's scene reset touches none of the display's effects.

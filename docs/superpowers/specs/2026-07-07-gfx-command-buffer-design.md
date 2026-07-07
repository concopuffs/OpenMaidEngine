# Design: Gfx Command-Buffer Host-Side Model (Phase 2)

Status: **draft (awaiting approval)** · Date: 2026-07-07
Plan: `docs/superpowers/plans/2026-07-07-gfx-command-buffer.md` · RE source (canonical op contract):
`docs/engine-re.md` (gfx op-contract table) · opcode semantics: `vm-map/opcodes.toml` (0x1a2, 0x1f7,
0x1fa, 0x1ff, 0x202, 0x203, 0x212, 0x213, 0x215–0x21a). Touches: `engine/Age.Engine/Vm/VirtualMachine.cs`,
`engine/Age.Engine/Model`, `engine/Age.Cli/Program.cs` (`GfxTraceHost`), `godot/{GodotAdvHost,Main}.cs`,
`engine/Age.Engine.Tests/`.

## Problem / goal

The background/sprite render drift (`Screenshot 2026-07-06 211353.png`) is caused by stubbed native gfx
ops. Phase-1 RE pinned **two** stubbed drivers: `0x215` (per-object slot select → collapses every draw to
slot 0) and `0x218`/`0x21a` (per-object geometry vectors → the anchor-preserve math reads stale globals).
Both read object state that the SET ops (`0x217`/`0x219`/`0x1ff`/`0x212`/`0x213`) wrote — all bytecode-driven.
**Goal:** model that per-object state host-side and *execute* the gfx ops instead of stubbing them, so the
QUERY ops return what the SET ops stored and the existing bytecode geometry math produces correct
`dst`/`w`/`h`. Scope decision (approved): model **all 13 ops' state** now; defer **only alpha
blend/compositing rendering**, explicitly guarded.

## Key insight — this is VM execution state, not a new render backend

The gfx object ops are almost entirely **pure data**: they build and query per-object records. The one op
that already needs the host — `0x208 get-texture-size` — is implemented. And the existing `draw-texture`
(`0x1fb`) op **already carries the bytecode-computed `dst`/`w`/`h` to the compositor**. So:

- The drift is fixed **VM-side**: once `0x215` returns distinct slots and `0x218`/`0x21a` return the stored
  vectors, the bytecode computes correct geometry and passes it to the *unchanged* draw path.
- The Godot compositor is **already per-slot** (`_slotBmp`/`_slotDims` keyed by slot; `BlitSlot` blits at
  the given `dst`). It collapses today only because the VM feeds it slot 0 for everything. Feed it correct
  slots + `dst` and it composites correctly — **little or no compositor change for geometry.**

This is why the fix is tractable and low-churn: **implement the ops against a VM-owned `GfxState`; the
rendering surface barely moves.**

## Architecture

### `GfxState` — new VM execution state (not an IHost surface)

A concrete class **`Age.Engine/Model/GfxState.cs`** (version-neutral engine state, like the global bank),
owned by `VirtualMachine` alongside `Globals` and exposed read-only (`public GfxState Gfx { get; }`) so a
rendering host can read it if needed. The 13 gfx ops are implemented as **VM handlers that mutate/query
`GfxState` directly** — no IHost round-trip for pure-data ops. QUERY ops read `GfxState` and `Write` the
result into their output operands exactly like `get-texture-size` does.

**Why VM-side, not behind `IHost`:** `GfxState` is deterministic execution state produced by opcodes,
identical on every host (it is data, not pixels). Putting it behind `IHost` would force 13 no-op
implementations across 10 hosts and blur the "IHost = effectful rendering backend" seam. Keeping it VM-side
means **non-Godot hosts need zero new methods**, base-ISA tests stay byte-identical, and `GfxState` is unit-
testable with no Godot. (Alternative considered — a single `IHost.Gfx` property returning a host-owned
`GfxState` — rejected: it needlessly routes pure engine state through the render seam.)

### The one host touch: `0x202`/`0x203` colored draws

These are the only gfx ops that actually *render* (blit an object with color/alpha). They (1) store color on
the `GfxState` record and (2) issue a draw. For now the draw reuses the **existing `IHost.DrawTexture`
path with alpha ignored**, behind a one-time guard log `"[gfx] alpha/blend deferred (0x202/0x203)"`. No new
IHost method; when blend lands we extend `DrawTexture`/`BlitSlot` to honor the stored color. If the drift
scene doesn't use `0x202`/`0x203` (Phase-1 grep shows `label_12649` draws via `0x1fb`), this path is exercised
only by effect scenes and stays correct-but-opaque.

### Compositor (`godot/Main.cs`, `GodotAdvHost.cs`)

Unchanged for geometry (already per-slot + blit-at-`dst`). Only additions: the guarded alpha log above, and
— if the gfx oracle reveals it — honoring the distinct slots the VM now assigns (the machinery already
exists; verify no slot-0 assumption remains).

## The model — `GfxState`

```
GfxState
  Dictionary<int handle, GfxObject> objects      // the registry 0x215 queries / 0x1a2·0x1f7 populate
  int currentObject                              // analogue of ctx[0x53d14] (current record index)
  SlotAllocator slots                            // hands out distinct slots (observed range 4..13)

GfxObject
  int handle
  int slot                                       // returned by 0x215 → G[0x62452]
  int[3] vecA, vecB, vecC                         // the geometry 3-vectors (set by 0x217/0x219/0x1ff, got by 0x218/0x21a)
  int field64, field68, field6c                   // scalar fields (set by 0x212/0x213)
  int color                                       // packed ARGB (set by 0x202/0x203) — stored; blend deferred
  int cmdType                                     // last command tag (3/5/7/9/0xb) — stored; see note
```

Notes:
- **`cmdType`** is written by every op into the current record natively; because we execute ops immediately
  (no deferred native flush), nothing in our model *reads* it. We store it for fidelity/diagnostics but do
  not branch on it. If a reader is found later, it is already captured.
- **Native workers (`FUN_0047xxxx`) are NOT modelled** — only the object-record data. That is the whole
  point of (b)-is-tractable: the QUERY ops return what the SET ops stored; the DirectDraw surface math the
  workers do is replaced by our compositor.

## Op → `GfxState` mapping

Authoritative per-op contract is the table in `docs/engine-re.md`; here is the model action (operand indices
1-based, matching the contract):

| op | model action |
|---|---|
| `0x1a2` register | `objects[key(op1)] ??= new; currentObject = that` (populate registry) |
| `0x1f7` elem-create | ensure `objects[op1]`; assign a slot if new (`op2>1` → array of `op2`); `currentObject = op1` |
| `0x1fa` elem-release | free `objects[op1]` + its slot |
| `0x212` set-field64 | `objects[op1].field64 = op2` |
| `0x213` set-xy | `objects[op1].field68 = op2; .field6c = op3` |
| `0x215` **query** | `Write(op1, objects.TryGet(op2)?.slot ?? -1)` (the slot-select driver) |
| `0x216` **query** | `Write(op1, fieldTable[op2])` (per-object stride-0x14 field) |
| `0x217` set-geom3 | `objects[op1].vec? = (op2,op3,op4)` |
| `0x218` **query** | `(Write(op2), Write(op3), Write(op4)) = objects[op1].vec?` |
| `0x219` set-geom3-b | `objects[op1].vec? = (op2,op3,op4)` |
| `0x21a` **query** | `(Write(op2), Write(op3), Write(op4)) = objects[op1].vec?` |
| `0x1ff` set-geom3-c | `objects[op1].vec? = (op2,op3,op4)` |
| `0x202` blit-color | store `objects[op1].color = pack(op4,op5)`; draw via existing path (alpha deferred) |
| `0x203` draw-color | store `objects[op1].color = pack(op3,op4)`; draw via existing path (alpha deferred) |

The `vec?` placeholders (which of `vecA/B/C` each op targets) are pinned in Phase 3 — see Risks.

## Data flow — the drift chain, fixed

```
0x1f7 elem-create(handle)      → GfxState assigns a distinct slot to `handle`
0x217 set-geom3(handle, anchor)→ store anchor 3-vector on the object
0x215 query(out=G[0x62452], handle) → G[0x62452] = object.slot   (no longer 0)
0x21a/0x218 query(handle) → G[0x62498/9/a], G[0x6249b/c/d] = stored vectors
  … bytecode: G[0x62498] = G[0x6249b] − w/2, foot-anchor …       (already correct math)
set-texture(resId, slot=G[0x62452]) → host loads BMP into that slot
draw-texture(slot, …, dst=computed) → compositor blits the right image at the right place
```

Every object lands in its own slot at its own `dst`; no collapse, no cumulative anchor drift.

## Error handling / edge cases

- **Query on unknown handle:** `0x215` → `-1` (matches native `0xffffffff`; the sign test then takes the
  "new object" branch, as the engine intends). Geometry queries on unknown handle → write `0` (default),
  logged once.
- **Release then query:** freed handle → `-1`; slot returned to the allocator.
- **Slot exhaustion (>10 live objects):** log and reuse the LRU slot; flagged as an assumption to verify
  (the observed range is 4..13 = 10 slots).
- **`Write` to an immediate operand** (shouldn't happen for out params) → the existing `Write` helper's
  behavior; assert in a test.

## Parity & testing

- **Base-ISA parity holds.** The gfx ops were previously `OnStub` (1 step, `pc+1`); the real handlers are
  also 1 step, `pc+1`. Existing tests and the Godot `--selftest` run synthetic/non-gfx scenes, so they are
  unaffected and stay byte-identical. (The QUERY ops now write gfx globals that were previously left `0`;
  those globals are gfx-only and do not gate dialogue — verified by the sweep diff below.)
- **New unit tests (synthesized via `ScriptAssembler`, per the "synthesize, don't disable" principle):**
  1. register two objects → each gets a distinct slot; `0x215` returns them; unknown handle → `-1`.
  2. set a geom vector via `0x217`, read it back via `0x218` → round-trips (and the set/get pairing is
     correct per the Phase-3 table).
  3. **drift regression:** two different-sized backgrounds registered + drawn land at their own correct
     `dst` (no cumulative drift) — the machine-checkable heart of the fix.
  4. `0x202`/`0x203` **color packing** arithmetic (pure, host-free) → exact packed ARGB.
- **`Age.Cli gfx <drift-scene>`** numeric oracle: background resolves to `(0,0)` full-frame, sprites
  centered/foot-anchored (not `0×0`, not marching). Record before/after in `phase-a-slice-plan.md`.
- **Sweep diff** (`Age.Cli sweep`) on a few gfx-heavy scenes with ops-on vs ops-off: confirm dialogue
  emission is unchanged (gfx globals don't gate ADV flow) — the parity guard for "did new global writes
  leak into control flow."
- **Screenshot** (`godot -- --scene <drift> --shot`): the bottom-right castle snaps into place.

## Deferrals (explicit, guarded — no structural debt)

- **Alpha/additive blend rendering** (`0x202`/`0x203` alpha, `AE*` fade sheets): the color/alpha is *stored*
  on the record now; only the compositor's blend step is deferred, behind a one-time guard log. Localized,
  one-line future change in `BlitSlot`; the geometry spec already built the compositor "to accept alpha."
- **Green chromakey** (sprite transparency): needs alpha; deferred with the same guard.
- **Native workers `FUN_0047xxxx`** (DirectDraw surface math): intentionally not modelled — the object-record
  data model replaces them. Not debt; it is the design.
- **`cmdType` branching:** stored, unused; no reader known. If one appears, the value is already captured.

None of these are stubbed *ops* — every op executes and updates state. Only the *pixel-level blend* is
deferred, and it is visibly logged so it never reads as "done."

## Risks / open questions (Phase-3 confirmations, not deferrals)

1. **Set/get → vector pairing — RESOLVED (2026-07-07).** Read the 5 native workers: **V18** (`+0x18/1c/20`) is
   SET by `0x217` (`FUN_0047e960`) and GET by `0x218` (`FUN_0047f360`) = the anchor vector; **V24**
   (`+0x24/28/2c`) is SET by `0x219` (`FUN_0047e910`) and GET by `0x21a` (`FUN_0047f2e0`) = the position vector;
   **V16c** (`+0x16c`) is SET by `0x1ff` (`FUN_0047e800`), no getter in the family. All resolve the object in the
   **one `ctx+0x408` registry** (via `FUN_0047ded0`) that `0x215` also queries → the model is a single
   handle→object map, getters return `(0,0,0)` on miss. Constants baked into the Phase-3 plan.
2. **Slot-allocation policy** (distinct 4..13): confirm against `label_125bd` / the `0x3239` record table
   (whether slots are positional or free-list). A `SlotAllocator` abstraction isolates this; the drift fix
   only needs *distinct, stable* slots per live object.
3. **Registry key derivation** (`"%c%8.8x"(cmdtype, ·)`): modelled as `handle → object`; confirm the key
   space covers the observed handle families (`0x62455[idx]`, immediates `0xcf08/0xe678/…`).
4. **Global-write leakage into control flow:** covered by the sweep-diff parity test above.

Each is a bounded confirmation with a fallback, isolated behind an abstraction — not open-ended risk.

## Success criteria (Phase 2 done)

- This spec self-reviewed (no placeholders/contradictions) and user-approved.
- Architecture committed: `GfxState` as VM execution state; pure-data ops VM-internal; only `0x202`/`0x203`
  color touches the (existing) draw path; compositor essentially unchanged for geometry.
- The four Phase-3 confirmations are named with fallbacks (not deferrals).
- Then: re-run `writing-plans` to expand Phase 3 into bite-sized TDD tasks from this spec + the contract table.

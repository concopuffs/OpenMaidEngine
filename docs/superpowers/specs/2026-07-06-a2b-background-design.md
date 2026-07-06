# Design: A2b-Background — Engine-Driven Background Layer (SC0000)

Status: **approved (design)** · Date: 2026-07-06
Related: `docs/phase-a-slice-plan.md` (A2), the A2a Godot project (`godot/`), `engine/Age.Engine`,
`build/data/CGINIT.json`, `extracted/DATA1/AGF2BMP2AGF.exe`, `extracted/DATA2|DATA5/*.AGF`.

## Problem / goal

A2a plays SC0000's dialogue on a black screen. A2b-Background renders the scene's **background layer**,
**driven by the bytecode** — the VM's `create/set/draw-texture` ops execute for real, the Godot backend
renders the slots whose resource it can resolve through a **data map**, and the background appears
*because* those ops ran (remove them → nothing draws). This is **not** a pinned/hardcoded image; it is
the first real graphics op-path promoted from stub to backend, with resolution as growable profile data.

## Scope

**In:** the single **background layer** for SC0000 — promote `create-texture`/`set-texture`/
`draw-texture` to typed `IHost` methods; a `resourceId → AGF` resolution map (seeded for SC0000's bg via
investigation); offline AGF→image conversion; Godot composites the resolved full-screen background behind
the dialogue. **Out:** the other ~239 draws (full sprite/UI/effects compositor), general resolution for
*all* resources, per-op fade/blend semantics, voice, choices, non-SC0000 scenes. These follow later; the
resolution map and rendered slots grow over time.

## The engine-driven principle (why this isn't a pin)

The bytecode drives every texture op. The backend keeps a `slot → texture` table; `SetTexture(resId,slot)`
resolves `resId` via the map and loads the (pre-converted) image into that slot; `DrawTexture(slot,…)`
renders a resolved full-screen slot into the background node. Unresolved slots (the other 239 draws) render
nothing **yet**. The background shows only because `set-texture`/`draw-texture` executed and `resId`
resolved. The map is per-game profile *data*, not a code shortcut; coverage grows as we map more resources.

## Architecture

### Engine — promote texture ops (`Age.Engine`)
Add to `IHost` (mirrors the `WaitForInput` addition; `CaptureHost` no-ops them so **A1 trace-diff + A2a
selftest stay green**):
```csharp
void CreateTexture(int slot, int width, int height);
void SetTexture(long resourceId, int slot);
void DrawTexture(int slot, int x, int y, int width, int height);
```
In `VirtualMachine.Step`, replace the `OnStub` fall-through for these opcodes with handlers that read the
operands and call the host (operand roles are **inferred** from disassembly — Phase 1 confirms/adjusts):
- `create-texture` args `(slot, w, h, ?)` → `CreateTexture(Read(a0), (int)Read(a1), (int)Read(a2))`
- `set-texture` args `(resId, slot, ?)` → `SetTexture(Read(a0), (int)Read(a1))`
- `draw-texture` args `(fade, slot, x, y, w, h, ?, ?)` → `DrawTexture((int)Read(a1), (int)Read(a2), (int)Read(a3), (int)Read(a4), (int)Read(a5))`

Opcode numbers (from `vm-map/opcodes.toml`): `create-texture`, `set-texture`, `draw-texture` are named
Kelebek ops; the VM dispatches on those labels exactly as it does `show-text`.

### Resolution map (profile data)
`build/resources.json` (or a profile dir): `{ "<resourceId hex>": "<AGF filename>" }`, seeded by Phase 1
for SC0000's background. **Format is tentative** — Phase 1 may show ids are scene-scoped (then keyed
`{scene: {id: agf}}`) rather than global. Lives on the backend/profile side, never in the VM core.

### AGF → image pipeline
Offline: run `AGF2BMP2AGF.exe <name>.AGF` (verified: produces an 800×600 24-bit BMP) on the mapped AGFs,
into a Godot-loadable assets dir (e.g. `build/textures/`). Godot loads with `Image.LoadFromFile` +
`ImageTexture.CreateFromImage`. (A tiny `tools/convert_agf.py` wrapper converts the set named in
`resources.json`.)

### Godot backend + compositing (`godot/`)
`Main` adds a background `TextureRect` behind the dialogue Label (full-rect, `StretchMode` = keep-aspect-
covered), created before the dialogue label so it's z-behind. `GodotAdvHost` implements the texture ops:
- `SetTexture(resId, slot)` → resolve `resId` in the map; if found, `CallDeferred` load the pre-converted
  image into a `slot → ImageTexture` dict (main thread).
- `DrawTexture(slot, x, y, w, h)` → if the slot has a texture and the draw is full-screen (≈800×600),
  `CallDeferred` set the background `TextureRect`'s texture. Unresolved/non-fullscreen slots: ignore.
- `CreateTexture` → record slot dimensions (used to distinguish full-screen bg from small textures).

## Phase 1 — Investigation (gated; the real unknown)

Goal: resolve **SC0000's background AGF** and document the `resourceId → AGF` mechanism.
1. **Static:** confirm the operand roles of `create/set/draw-texture`; identify which full-screen draw is
   the *visible* background (SC0000 draws slot 3 cross-context and slot 0xe from local `set-texture 0x21`);
   probe the resolution — `SYS4INI` (ALF filename index), `*INIT` tables, any id→name path. (`CGINIT` is a
   per-CG numeric record table, **not** a direct filename map, so resolution is likely a runtime path.)
2. **Frida (if static is inconclusive):** attach to the running translated game on SC0000; hook the
   graphic-load call; capture the actual `resourceId → AGF filename` for the background draw. This yields
   both the mechanism and a **ground-truth oracle**.
3. **Confirm:** convert the resolved AGF, eyeball that it is SC0000's intro background; seed
   `resources.json`.

**Gate:** Phase 1's result confirms the operand mapping + resolution shape before the engine handlers and
map format are finalized. Expect Frida to be needed (first Frida use in the project).

## Validation

- **Headless resolve-oracle:** a test runs SC0000 with a recording host and asserts the background draw
  path fires `SetTexture` with the expected resource id, and the map resolves it to the expected AGF
  (Phase 1 ground truth) — validates engine-driven resolution *without pixels*.
- **Regression:** `dotnet test engine/AgeEngine.sln` stays green and the A2a `--selftest` still prints
  `SELFTEST OK` (new `IHost` methods no-op in `CaptureHost`).
- **Manual visual (human):** the converted background renders behind SC0000's dialogue in the Godot window
  and looks like the intro art; dialogue still advances on click/Enter.

## Risks

- **Resolution is the crux and uncertain-sized.** `CGINIT` isn't a filename map and SC0000's primary bg
  slot is loaded cross-context; static RE may quickly stall → Frida. Mitigation: the machinery (op wiring,
  pipeline, compositing, map-lookup) is built independently of *how* the map is populated; Phase 1 only has
  to yield SC0000's one bg entry to unblock the visual.
- **Draw-op operand roles are inferred** — Phase 1 confirms before the handlers are trusted; the headless
  oracle (which resId the bg path resolves) catches a wrong mapping.
- **No pixel oracle** — background correctness is human-eyeballed; the resolve-oracle is the automatable
  proxy. Frida capture is the strongest available ground truth.
- **First Frida use** — attaching to the packed AGE.EXE at runtime (packer is bypassed once running);
  budget setup time. If Frida is impractical, static-plus-eyeball is the fallback for this one bg.
- **Trace parity** — adding `IHost` texture methods must not change `Steps`/`Emitted`; guaranteed by
  `CaptureHost` no-ops (verified by the A1 trace-diff regression).

## Out of scope (later slices)

Full draw compositor (all 239 draws, layers, blends, fades), general/automated resolution for all
resources, sprite/CG/UI rendering, voice + BGM, choices, `call-script`/state seeding, non-SC0000 scenes.

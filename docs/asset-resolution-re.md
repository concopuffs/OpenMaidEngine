# Asset Resolution — foundational RE (graphics + audio)

**The problem.** The bytecode loads assets by a small numeric **resource id** (`set-texture 0x23`,
`play-voice N`, …). To render/play the *real* asset — driven by the bytecode, not hardcoded — the
engine must resolve `resId → asset file`. This is **foundational** (nearly all visuals + all audio
depend on it) and **not machine-verifiable** (no pixel/audio oracle), which makes it the largest,
highest-risk area of the port. This doc is the steering state; it feeds the A2b render/audio slices.

## What's already landed

- **Graphics ops wired** (A2b-background, engine-driven): `create-texture 0x1f8` `(slot,w,h)`,
  `set-texture 0x1f9` `(resId,slot)`, `draw-texture 0x1fb` `(slot,x,y,w,h)` promoted from VM stubs to
  typed `IHost` methods; `CaptureHost` no-ops them (A1 trace-diff/A2a selftest stay green). The VM
  now *drives* graphics; only resolution + backend rendering remain.
- **Audio ops named, not yet wired:** `play-voice 0xc4`, `play-bgm`.
- **Tools:** `tools/convert_agf.py` (AGF→BMP for *stills* via `AGF2BMP2AGF.exe`); `tools/frida/`
  (runtime capture harness — see its README); Frida core installed (17.15.3).

## Findings (2026-07-06)

- **SC0000 background = a slot-0 full-screen slideshow.** The intro loads ~30 distinct full-screen
  images into slot 0 in order (`set-texture 0x23→0`, `0x25→0`, `0x27→0`, …), each drawn 800×600.
  Res `0x23` is the first. (There is also a persistent full-screen **slot 3** set *cross-context*,
  not in SC0000 — inherited from the parent/system scene.)
- **The opening mixes movies + stills.** `AGF2BMP2AGF` reports `OP.AGF`/`MVB*.AGF` as
  "unsupported type (possibly MPEG)" → DATA5 `MVB*` (210) and `OP`/`ED` are **movies**, not stills.
  The opening's visible background did **not** match any `EV001*` still (confirmed by eye), so res
  `0x23`'s file is not obvious from the name space alone — resolution is required.
- **Asset name spaces:** DATA2 = `EV*`/`EVM*` stills (985). DATA5 = `MVB*`/`OP`/`ED` movies (210).
  DATA3 = `.OGG` audio (`BGM*`, `ANA*` voice).
- **The resolution chain is opaque statically.** `CGINIT` (`build/data/CGINIT.json`) is a
  925-column *numeric* record table (row-major, sparse) — **not** an id→filename map.
  **`SYS4INI.BIN` (magic `S4IC422`) is the authoritative asset index** the game + `BinExtractALF`
  use (name ↔ archive ↔ offset ↔ size), but filenames are **not stored as plain ASCII** (an
  `EV001AA` search misses), so it needs S4IC-format RE to parse.
- **Frida file-I/O is noisy.** `ReadFile` hooks on `DATA2.ALF` capture reads during the opening, but
  the offsets/spans don't line up with extracted AGF sizes → the game likely **memory-maps** the
  archives (so `ReadFile` offsets are OS paging, not clean per-asset loads) and/or uses async reads.
  The robust hook is the game's **internal load-by-id function**, not file I/O.

## The RE plan (ordered)

1. **Parse `SYS4INI` (S4IC422) → an asset index** `{name, archive, offset, size}`. *Reusable and
   bounded* — it names every asset in every DATA*.ALF, gives archive-offset→name (to rescue Frida
   offsets), and is the **answer key** for step 2. Deliverable: `tools/parse_sys4ini.py` +
   `build/asset-index.json`. (Format reference: asmodean's `exs4alf`, which `BinExtractALF` is based on.)
2. **Crack `resId → filename`.** With SYS4INI as the answer key, either (a) **order-correlate**: run
   SC0000 in our engine to get the `set-texture(resId)` sequence, capture the real game's asset-load
   order via a *reliable* Frida hook, and align them; or (b) **hook the internal load-by-id
   function** directly (find via the opcode dispatch for `0x1f9`) to read `resId → name` at the
   source. Likely underlying rule: `resId → CGINIT/table → name`. Deliverable: the mechanism +
   `vm-map/resources.json` (or a generated map) seeding at least SC0000's slideshow.
3. **Wire the backend** (already designed — A2b-background plan Tasks 3–5): `ResourceMap` resolver +
   Godot `TextureRect` compositing; render only resolved full-screen slots. Mechanical once (1)+(2) land.
4. **Audio** (parallel, same shape): resolve `play-voice`/`play-bgm` `id → OGG` via SYS4INI + a
   Frida audio capture (hook `DATA3.ALF` reads or the audio-play fn); play via Godot. Reuses the
   `tools/frida/` framework.
5. **Movies** (`OP`/`MVB`, MPEG) — a separate video-playback path; deferred.

## Validation reality (why this is the big haul)

Unlike the VM/dialogue work (byte-exact trace oracle), graphics + audio have **no machine oracle**.
Validation is: **Frida ground truth** (what the real game loads/plays for a scene) as the correctness
anchor, plus **human eyeball/ear**. Treat every mapping as provisional until Frida-confirmed; the
`resId→file` map is *data we curate against ground truth*, and the engine stays honest by only ever
rendering what the executed bytecode + the map produce (never a hardcoded image).

## Status

A2b-background: **machinery landed** (texture ops engine-driven, tools, findings). The **render is
blocked on asset resolution** (steps 1–2), which is promoted to its own foundational effort. Next:
either start step 1 (`SYS4INI` parser) or bank momentum with the Frida-free **choices** sub-slice
(static-RE opcode hunt) while resolution waits its scheduled turn.

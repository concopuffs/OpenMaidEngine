# Backlog

Open work items that nothing else schedules: known fidelity gaps, deferred follow-ups and accepted ideas. Each item
says what is missing, where the evidence lives, and what "done" means. Delete an item in the change that resolves
it; git history keeps the record.

- Direction and phase order stay in `remake-architecture-and-roadmap.md`.
- Future work a design spec accepted stays in that spec, linked from here rather than restated.
- The cross-session status memory points here for open items instead of keeping its own list.

## Engine fidelity

- **Callback ownership guards beyond hotspots.** Native refuses to dispatch a callback while a frame other than
  its registrant runs, and throws "Depth が不正です <running> != <owner>" (`engine-re.md`, "Callbacks belong to the
  script that registered them"). The port models this for hotspot callbacks only (`26f5411`). The same guard
  protects:
  - the op-`0xcc` mouse callback (`mouse_callback_owner_script_id`, checked by op `0xcd`);
  - coroutine resume (`coroutine_yield_owner_script_id`, checked by op `0x7c`);
  - op `0xcf`;
  - `timed_callback_sequence_tick`.

  The port keeps that state per frame, so a deeper frame silently has none. Done when each path raises the
  native error from a non-owner frame, with a synthesized test per path.
- **Retry and Ignore in the native error box.** For interpreter errors (flags `0x10007`), native offers
  中止/再試行/無視. Its catch policy re-executes the command on result 2, skips it on result 4, and exits otherwise
  (`engine-re.md`). The port always ends the run (中止). The mapping from MessageBoxA's button codes to that policy
  is unverified, because the message-box call goes through a thunk. Done when a native run confirms each button's
  effect and the port offers the same choices outside driven runs.
- **Exit and return-to-title boundary.** Op `0x1` and SYSTEM4's invalid-mode exit end the port's VM session, but
  the frontend policy that follows (full exit versus returning to TITLE) is not implemented (`engine-re.md`,
  "Frontend exit request is not a root reload").
- **Statement-block movie and modal gating.** Open `0x1f4`/`0x1f5` blocks withhold the tick render and delayed
  SFX/voice starts in the port, but not delayed movie starts or the `0x2000` modal movie service (`engine-re.md`,
  statement blocks, "Not modelled"). No observed block contains a movie start yet.
- **Delayed movie start.** The movie op's fourth operand delays the graph start until `current - start >= operand4`.
  The port threads it as `syncMask` but does not delay FFmpeg for nonzero values (`engine-re.md`, movie start;
  24 of 25 sites pass zero, BTL passes a dynamic value).
- **Read-message Skip service.** The ADV menu toggle and `message:ReadTextSkip` are stored, but gating advancement on
  read-message state is not implemented (`engine-re.md`, ADV menu table).
- **SYS4INI trailer ADV policy.** Himegari's trailer sets `CANCELMESSKIPONCLICK=2` (press/release cancellation). The
  port keeps the engine fallback `0` because it does not apply the trailer's ADV policy settings (`engine-re.md`,
  `CancelMesSkipOnClick`).
- **Native audio preferences.** Native applies the user's stored attenuation and pan; the port uses unity gain and
  centered pan (`engine-re.md`, audio load handlers).
- **Per-glyph font calibration.** The port implements native's layout rule, not the calibrated font realization
  (`text_create_calibrated_single_byte_font@0x451b70`) or display-aspect scaling (`engine-re.md`).
- **Kamidori translated append files load without the overlay.** Kamidori's root `$n$*.bin` files are the fan
  translation's append replacements. Tooling ignores them (`paths._UNTRUSTED_LOOSE_APPEND_OVERRIDES`), but the port
  (like native AGE) loads them, so English append content shows even without `--overlay-root patch`. Decide whether
  the port should require the overlay for them.
- **LZSS encoder parity (optional).** Port saves match native byte for byte except the LZSS-compressed bytes,
  where the encoder's match choices differ. Matching is optional, since native reads both.

## Open Maid Script

- **Bound parameters by name.** The language design binds a sub parameter to any declared cell, including registry
  names (`sub adv_load_layer(layer: adv_gfx_layer_index, …)`, OMS spec §6.3). The parser accepts only raw cells
  (`L[0x40]`, `G[0x1234]`; `Parser.ParseCell`), so `sub f(n: current_unit_id)` fails with "expected a storage cell".
  Neither prelude declares parameters, so nothing exercised it. Done when named globals and pinned locals bind, with
  a test, and `docs/modding/oms-language.md` drops its workaround note.
- **Compiler warnings.** The §8.2 warnings (recursion with bound parameters, string/number mixing, a wait inside an
  `atomic` body) need a warning channel and, for the type-based ones, a type checker (OMS spec §16, gate 2 C3).
- **Whole-script rebuilds drift saves.** `oms-compile` re-derives T1/T2/T3, so old saves can drift on a rebuilt
  scene. Fragments keep tables stable and are the save-safe route; a `replace/` script built from edited source does
  not. Done when whole-script compiles can keep the base's table order (for example from the sidecar).
- **Prelude coverage.** About 2.4k dialogue pages stay core statements: pages that interpolate `GS[…]`, carry
  furigana or span several rows. A scene-skeleton template would also replace the copied skeleton in
  `example.prologue`.
- **Allocator residue (optional).** The remaining temporary-slot overrides (OMS spec §13) and Kamidori's explicit
  `L[0x0]` in `place_scene_background` (394 sidecars) only matter for sidecar-free byte identity.

## Mod loader and modding

- **Toolchain packaging for exported ports.** Fragments compile against the repository's OMS modules
  (`oms/<profile>/`), globals and patch points (`build/games/<profile>/`). An exported port without the repository
  links only mods that need none of them (`tools-reference.md`, port `--mod`). Done when an export carries or
  embeds these files and a fragment mod links in an exported build.
- **Warn when record 0 is not meant to autorun.** A mod's first `[scripts].names` entry is its append catalog's
  record 0, which native op `0x143` runs at INIT2. A mod whose first script is a scene hangs native AGE at boot. Done
  when `omp check`/`omp build` warn if record 0 waits for input or otherwise looks like a scene
  (`docs/modding/new-scenes.md` documents the rule meanwhile).
- **Mod-owned globals.** `global` declarations must pin a cell (`@ address`). The linker-allocated and `saved`
  globals of the language design (OMS spec §5.2, §9.2) are not implemented, and which cells are free (and which a
  save keeps) is open (§13). Mods pick unused cells by hand meanwhile (`docs/modding/oms-language.md`). Done when
  the linker allocates mod globals per profile from evidenced free cells.
- **Kamidori new-scene example.** The save-capable scene recipe (`docs/modding/new-scenes.md`) is proven on
  Himegari only (`example.prologue`, gates 4 and 5). Done when a Kamidori scene mod, built on one of its append
  scenes' skeletons, plays and saves on native AGE.
- **Kamidori named patch points.** `vm-map/patch-points-himegari.toml` has one point. Kamidori has no registry yet,
  so Kamidori fragments name sites by offset only.
- **Accepted future work** in the mod loader design (`superpowers/specs/2026-11-03-mod-loader-design.md` §12):
  - a launcher;
  - runtime scripting (Lua);
  - mod-owned sites (patching another mod's inserted code);
  - declarative data patches;
  - mod merging;
  - precompiled new scripts.

## Platform and project

- **Portability gates** listed in `remake-architecture-and-roadmap.md` ("The open portability gates remain separate
  follow-ups"): mixed-case roots, fresh-profile writes, native Linux desktop, the movie preroll recheck, and Linux
  font bundling (`platform-portability.md`, "Deferred Linux font-bundling follow-up").
- **Flaky test.** `Transform2DMathTests.ValueMatrixBuild_DoesNotAllocatePerObject` counts allocations and fails
  intermittently in full parallel runs; it passes alone.
- **Deferred by the user:** the `LICENSE` copyright holder is blank; `main` trails `develop` (the GitHub default).

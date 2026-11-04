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
- **LZSS encoder parity (optional).** Port saves match native byte for byte except the LZSS-compressed bytes,
  where the encoder's match choices differ. Matching is optional, since native reads both.

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
- **Kamidori saves in the port.** The built-in Kamidori profile is read-only until its persistence contract is
  validated (`tools-reference.md`, `--probe`). Saving inside a modded Kamidori game is therefore untested in the
  port.
- **Accepted future work** in the mod loader design (`superpowers/specs/2026-11-03-mod-loader-design.md` §12):
  - a launcher;
  - runtime scripting (Lua);
  - mod-owned sites (patching another mod's inserted code);
  - declarative data patches;
  - mod merging;
  - precompiled new scripts.

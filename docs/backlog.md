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
- **Modal dialog fidelity (終了 and エラーが発生しました).** The port's dialogs match native's answers, but not
  these details (`engine-re.md`, "Close policy" and the Depth error):
  - Native's modal dialogs freeze its main loop, while Godot keeps presenting: animations advance behind them.
  - The error box's Godot window shows a close ✕. It does nothing, whereas Win32's abort/retry/ignore box has
    no close button.
  - Run-state bit `0x1000000` makes native close without asking; its setter is unidentified.
  - System-menu commands `0x9c41` (ask) and `0x9c6b` (forced close) have no port equivalent.
  - The 終了 dialog has no `IDI_ASTERISK` icon.

  Done when a driven run shows the port frozen behind both dialogs, and each remaining item is modelled or shown
  to be unreachable in retail.
- **Statement-block movie and modal gating.** Open `0x1f4`/`0x1f5` blocks withhold the tick render and delayed
  SFX/voice starts in the port, but not delayed movie starts or the `0x2000` modal movie service (`engine-re.md`,
  statement blocks, "Not modelled"). No observed block contains a movie start yet.
- **Delayed movie start.** The movie op's fourth operand delays the graph start until `current - start >= operand4`.
  The port threads it as `syncMask` but does not delay FFmpeg for nonzero values (`engine-re.md`, movie start;
  24 of 25 sites pass zero, BTL passes a dynamic value).
- **Control-strip tooltip after a click.** Clicking a hovered ADV control (for example Read-message Skip, x=750)
  hides its tooltip natively until the mouse moves; the port shows it again at once while the cursor stays still
  (driven runs 2026-11-03, `native-after-hover-click.png` versus `port-after-second-click.png`). Likely the port
  re-arms hotspots at the next wait with a hover-enter for the current pointer, while native fires hover only on
  entry. Done when native's hover re-arm rule is reversed and the port matches it.
- **SYS4INI trailer ADV keys still unapplied.** `CONTROLDISIBLECURSOR=1` (suppress the cursor restore when a bound
  hotspot action or hover callback is selected), `WHEELKEYUP`/`WHEELKEYDOWN` (8/9, action bits for ADV wheel-key
  handling in `adv_input_service_poll@0x411230`) and `REDRAWTEXTONKEY=0` are not sourced from the trailer, and
  Kamidori's extra keys (`OUTERFRAMEMODE`, `AUTOFREETEX`, `BLANKEXTENTMODE`, `USEPROPORTIONALFONT`,
  `VALIDFONTCHECKER`, `RUBYSHIFTMODE`, `DEPENDMOVIESOUND=2`) are not reverse-engineered (`engine-re.md`, trailer
  catalog). Done when each key is reversed and either applied or shown inert for both games.
- **Calibrated font realization (unobserved).** Native builds fonts through
  `text_create_calibrated_single_byte_font@0x451b70` and its mode-1/mode-2 calibration (found in the SYS4433 image,
  display-aspect text). The port implements the layout rule it feeds, not the realization itself (`engine-re.md`).
  No visible or save difference is known: glyph baselines and crops match native and the SC0000 page-3 save is byte
  identical (`4b621ed`). Done when a native comparison shows whether it changes any glyph, and the port matches if
  it does.
- **Kamidori translated append files load without the overlay.** Kamidori's root `$n$*.bin` files are the fan
  translation's append replacements. Tooling ignores them (`paths._UNTRUSTED_LOOSE_APPEND_OVERRIDES`), but the port
  (like native AGE) loads them, so English append content shows even without `--overlay-root patch`. Decide whether
  the port should require the overlay for them.
- **The `0x90`-byte SYS4INI block.** Its meaning is unknown and no consumer is known (OMS spec §13). Revisit only if
  one appears.
- **LZSS encoder parity (optional).** Port saves match native byte for byte except the LZSS-compressed bytes,
  where the encoder's match choices differ. Matching is optional, since native reads both.

## Open Maid Script

- **Record keys and field values by name.** Tables index by number (`unit[761]`). The design goal is
  `unit[緑プテテット]` and id-valued fields by name (`.drop_item[0] = 緑の液`) through enums such as `unit_id` and
  `item_id` generated from the init image, with each table declaring its key kind and each field its value kind,
  as template parameters do (OMS spec §13). Open: names that are not identifiers or repeat
  (`牙コウモリ（牙蝙蝠/ニュクテリス）`), qualifying members of different enums that share a name, and translations
  that rename entries. Done when both games' registries generate the enums, the decompiler prints keys and
  values by name, and both games still rebuild byte-identically.
- **Overlapping registry arrays.** `globals_build.py --lint` reports 2 Himegari and 39 Kamidori pairs of
  overlapping arrays. Classify each as a deliberate sub-view or a size error, since element names depend on it
  (OMS spec §11.3, §13).
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

## Localization

- **Brainstorm a localization framework (not started; user request 2026-11-04).** There are two layers:
  - **Engine-owned text.** This is the strings that AGE.EXE and AGERC.DLL draw themselves, which the port reproduces
    in Japanese, for example:
    - the 終了 dialog (`godot/ExitConfirmationDialog.cs`);
    - the エラーが発生しました box (`VirtualMachine.DiagnosticCaption`);
    - other native message boxes.
  - **Game text.** This is script dialogue, CG text and system graphics. Mods and translation packs carry it.

  The engine's concern is the first layer. The goal is a framework, not a one-off English translation. Questions
  to settle:
  - String identity: keys versus native source text.
  - Locale selection and fallback: the native Japanese stays the default and the fidelity reference.
  - Per-profile overrides: games differ in wording.
  - How mods ship and override both layers through the loader.
  - Fonts and glyph coverage.
  - Keeping the drive protocol's native control texts (`dialogs`/`answer`) stable whatever locale is shown.

  Done when a design is agreed (a spec under `superpowers/specs/`) and the engine-owned strings go through it.

## Platform and project

- **Intermittent allocation test.** `MovieSurfaceRegistryTests.ResourceLookup_SelectsNewestPublishedPlaybackWithoutAllocating`
  failed once in a `validate.py --level core` run on 2026-11-03 and passed on every rerun. It measures
  `GC.GetAllocatedBytesForCurrentThread` around the lookup, which tiered JIT or runtime work on the test thread can
  disturb. Done when the test is deterministic (for example a warmed loop with a tolerance) or the lookup is shown
  to allocate.
- **Intermittent debug-return test.** `DebugSceneLaunchTests.PollingTitleReturnsAtOpcodeBoundaryWithoutAdvInputWait`
  failed once in a full `dotnet test` run on 2026-11-03 and passed alone and on the full rerun (1015/1015). It
  waits on cross-thread handshakes with 5 s timeouts, which a loaded test host can miss. Done when the failure is
  reproduced and its cause fixed, or the handshake no longer depends on wall-clock time.
- **Portability gates** listed in `remake-architecture-and-roadmap.md` ("The open portability gates remain separate
  follow-ups"): mixed-case roots, fresh-profile writes, native Linux desktop, the movie preroll recheck, and Linux
  font bundling (`platform-portability.md`, "Deferred Linux font-bundling follow-up").
- **Flaky test.** `Transform2DMathTests.ValueMatrixBuild_DoesNotAllocatePerObject` counts allocations and fails
  intermittently in full parallel runs; it passes alone.
- **Deferred by the user:** the `LICENSE` copyright holder is blank; `main` trails `develop` (the GitHub default).

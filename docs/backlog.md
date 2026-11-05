# Backlog

Open work items that nothing else schedules: known fidelity gaps, deferred follow-ups and accepted ideas. Each item
says what is missing, where the evidence lives, and what "done" means. Delete an item in the change that resolves
it; git history keeps the record.

- Direction and phase order stay in `remake-architecture-and-roadmap.md`.
- Future work a design spec accepted stays in that spec, linked from here rather than restated.
- The cross-session status memory points here for open items instead of keeping its own list.

## Engine fidelity

- **Native menu bar: the remaining commands (AGERC menu 110).** The port draws the pruned bar and runs ｹﾞｰﾑ>終了,
  画像の保存 and the four ﾒｯｾｰｼﾞ toggles as native does (`engine-re.md`, "Native menu bar"). It grays everything
  else. Still missing:
  - The AGERC dialogs behind ﾒｯｾｰｼﾞ設定, 画面表示, ｻｳﾝﾄﾞ設定, その他の設定 and the ﾍﾙﾌﾟ items (version, system
    information).
  - ﾌﾙｽｸﾘｰﾝ/ｳｲﾝﾄﾞｳ (`0x9c5a`/`0x9c5b`), and in fullscreen the top-edge strip that shows the bar (op `0x149` sets
    its delay; the port keeps that state and op `0x142`'s).
  - The right-click ﾒｯｾｰｼﾞ popup under `message:RMouseEvent=2`.
  - Keyboard focus without a dropdown: Alt pressed and released, or F10, selects the bar; bit `0x200` of op
    `0x10f`'s mask swallows F10. The port's keyboard access starts only from Alt with an access key, which opens
    that dropdown.
  - Smaller look differences. Native popups may extend past the game window (the ﾍﾙﾌﾟ popup spans x 744..989), but
    the port clamps them inside it. Latin glyphs are drawn without ClearType's exact rasterization.

  Done when each command reaches the same native service or dialog, or is closed as out of scope.
- **Statement-block movie and modal gating.** Open `0x1f4`/`0x1f5` blocks withhold the tick render and delayed
  SFX/voice starts in the port, but not delayed movie starts or the `0x2000` modal movie service (`engine-re.md`,
  statement blocks, "Not modelled"). A static scan of both games' scripts (2026-11-04) finds a movie start inside
  an open block only twice, both `play-movie-mask-transition` in `DEBUG.BIN` (`0x139`, `0x1ab`), which retail never
  reaches (the port's F4 debug route does). Done when those two are modelled, or the item is closed as
  debug-only.
- **Kamidori's extra SYS4INI trailer keys.** `OUTERFRAMEMODE`, `AUTOFREETEX`, `BLANKEXTENTMODE`,
  `USEPROPORTIONALFONT`, `VALIDFONTCHECKER`, `RUBYSHIFTMODE` and `DEPENDMOVIESOUND=2` are not reverse-engineered
  (`engine-re.md`, trailer catalog). The ADV keys both games share are done: the wheel ids are applied, and
  `CONTROLDISIBLECURSOR`/`REDRAWTEXTONKEY` are matched or inert. Done when each key is reversed and either applied
  or shown inert for Kamidori.
- **Op `0x82` redraw reflows instead of restoring each record (unobserved).**
  - Native `text_redraw_retained_group@0x451530` draws each retained record at its recorded rectangle, in that
    record's retained style (pixel height, render mode, `+0x554`), and then restores the manager's style.
  - The port's `AdvTextHistory.TryBuildRenderBatch` reflows the group's text through the target layout, in the
    first text record's style and the current leading.
  - Only Kamidori calls it (CONFIG1–CONFIG5 return paths, flags 2). A group recorded in a single style in that
    same layout should come out the same, and no visible difference is known.
  - Done when a native Kamidori CONFIG-return comparison shows whether any page differs, and the port matches if
    one does.
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
- **Intermittent root-reload test.** `RootReloadRequestTests.RequestRootReloadInterruptsAWaitAndReloadsRootWithoutTheInitialFlag`
  failed once in a full Release `dotnet test` run on 2026-11-04 ("sleep was not interrupted", its host's
  wall-clock timeout) and passed alone and on the full rerun (1038/1038). Done when its interrupt handshake no
  longer depends on wall-clock time.
- **Portability gates** listed in `remake-architecture-and-roadmap.md` ("The open portability gates remain separate
  follow-ups"): mixed-case roots, fresh-profile writes, native Linux desktop, the movie preroll recheck, and Linux
  font bundling (`platform-portability.md`, "Deferred Linux font-bundling follow-up").
- **Flaky test.** `Transform2DMathTests.ValueMatrixBuild_DoesNotAllocatePerObject` counts allocations and fails
  intermittently in full parallel runs; it passes alone.
- **Deferred by the user:** the `LICENSE` copyright holder is blank; `main` trails `develop` (the GitHub default).

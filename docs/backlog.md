# Backlog

Open work items that nothing else schedules: known fidelity gaps, deferred follow-ups and accepted ideas. Each item
says what is missing, where the evidence lives, and what "done" means. Delete an item in the change that resolves
it; git history keeps the record.

- Direction and phase order stay in `remake-architecture-and-roadmap.md`.
- Future work a design spec accepted stays in that spec, linked from here rather than restated.
- The cross-session status memory points here for open items instead of keeping its own list.

## Engine fidelity

- **Native menu bar: look differences and undriven cases (AGERC menu 110).** The port runs every command either game
  can reach as native does: ｹﾞｰﾑ>終了, 画像の保存, the four ﾒｯｾｰｼﾞ toggles, ﾌﾙｽｸﾘｰﾝ/ｳｲﾝﾄﾞｳ (with the fullscreen top-edge
  strip), ﾍﾙﾌﾟ>ﾊﾞｰｼﾞｮﾝ情報, the 設定 dialog's four commands and the keyboard mode (`engine-re.md`, "Native menu bar"
  and "Screen mode"). It grays everything else, as native does. Still different:
  - Smaller look differences. Native popups may extend past the game window (the ﾍﾙﾌﾟ popup spans x 744..989), but
    the port clamps them inside it. For the same reason, the window menu opens at the bar's top, not one row above
    it as natively. Latin glyphs are drawn without ClearType's exact rasterization. The 設定 tab control is
    `TCS_HOTTRACK`, and native's hover look is neither captured nor drawn. The fullscreen strip's minimize and
    restore buttons use Windows 10's default hover colours, not captured ones.
  - Keyboard mode's pointer moves are not driven natively: the driver's posted moves do not reach Windows' menu
    loop. The port moves the selection without opening a dropdown, by Windows' rule. Escape in the loop is modelled
    on Windows' rule too, since the driver refuses ESC on native AGE. The strip's window-menu Move does nothing in
    the port; natively it would start Windows' move loop on the strip.

  Done when each difference is matched, or accepted here as a gap. Out of scope since 2026-11-06: the 設定 radio
  groups, the right-click ﾒｯｾｰｼﾞ popup and 設定 page 101, which neither shipped game can reach (`engine-re.md`,
  "Native menu bar", "Out of scope").
- **Input-latch cases the port does not model.** The port's latch (`VirtualMachine.InputLatch`) covers effects,
  sleeps, the ops between waits, the coroutine yield, and every consumer and flush point that SC0000 reaches
  (`engine-re.md`, "Effect skipping and the input latch"). Still open:
  - A trigger held during a run-state-`0x400` effect with service-flags bit 0 set, which never polls, is not offered
    to a later effect that polls under `EffectSkipOnClick` 1. The port offers it only to the reveal, the movie, the
    hidden-window wait, the timed sequence and the resumed text wait.
  - The op-`0xd5` timed sequence's poll (`timed_callback_sequence_tick`) is implemented from the decode and
    synthesized tests only. No native run has clicked during a sequence: HISTORY's lasts 50 ms, and the others run
    deep in gameplay (SHOWGROW is the one Himegari sequence with an abort pc).
  - `input_hotspot_wait_poll` and `adv_input_dispatch_callbacks` (op `0x91`/`0x92`/`0x95`/`0x96` waits) take no held
    triggers in the port. Himegari never runs those ops; Kamidori is unchecked.

  Done when each case is driven natively and the port matches, or the case is shown unreachable in both games.
- **One unexplained port crash on exit (2026-11-06).** A driven port exited with `0xC0000005` on `drive.py port
  stop`. That session had switched to fullscreen and shown the top-edge strip. An injected click had then landed in
  the side bar outside the canvas: the driver passed viewport coordinates as window coordinates, fixed the same day.
  Seven later sessions exited 0. They covered stops in fullscreen and while windowed, and clicks in the side bars.
  Windows logged no Application Error event, and the next start overwrote `godot.log`. Done when a crash recurs with
  its log kept and is fixed, or after a period of driven runs without one.
- **Statement-block movie and modal gating.** Open `0x1f4`/`0x1f5` blocks withhold the tick render and delayed
  SFX/voice starts in the port, but not delayed movie starts or the `0x2000` modal movie service (`engine-re.md`,
  statement blocks, "Not modelled"). A static scan of both games' scripts (2026-11-04) finds a movie start inside
  an open block only twice, both `play-movie-mask-transition` in `DEBUG.BIN` (`0x139`, `0x1ab`), which retail never
  reaches (the port's F4 debug route does). Done when those two are modelled, or the item is closed as
  debug-only.
- **Aspect mode: the unmodelled parts.** The mode-1 calibration of the primary and single-byte fonts, the in-cell
  placement and the surface-text draws are implemented (`engine-re.md`, "Aspect-mode font calibration", "Aspect-mode
  surface text"). Still missing:
  - the `display:AspectMode` setting, which calibrates every face when nonzero;
  - mode 2 (`text_calibrate_mode2_glyph_font@0x451910`), which no shipped game selects;
  - the display scale on the placement (`+0x40c->+0x4e0`);
  - a control byte's blank extent in surface text: native measures it with `GetTextExtentPoint32A`, and the port gives
    it the fallback advance.

  No difference has been seen. Done when `display:AspectMode` and the display scale are modelled or shown
  unreachable.
- **Op `0x82` flag 8 (voice replay) is not modelled.** Native replays a group's retained voice records under flag 8
  (`engine-re.md`, "Op 0x82 redraw"); the port ignores the flag. No shipped site sets it: Kamidori's six CONFIG sites
  pass flag 2. Done when a game that sets it is profiled and the replay matches native.
- **Kamidori data tooling reads pre-update copies of eleven append records.** Kamidori's official update shipped
  eleven root-loose append overrides (`$1$` ALINIT, CIMES, ITINIT, ITMES, SKINIT, SKMES; `$2$` ALINIT, ITINIT,
  ITMES, SKINIT, SKMES). The English translation overwrote them, and its installer kept the Japanese originals as
  `Backup/<name>.bak` (`asset-resolution-re.md`, "Root `$n$` files are AGE's own mechanism"). Tooling excludes every
  root `$n$` file for Kamidori, so `paths.scripts()` and `init_image.py`'s pristine view fall back to the
  `APPENDnn.ALF` records, which predate the update (`$1$ITINIT` 7,208 bytes against the update's 9,640). The item,
  skill and alchemy tables decoded from appends 1 and 2 (`kamidori-data-tables.md`, `vm-map/globals-kamidori.toml`)
  may therefore miss the update's changes. Done when the pristine view uses each record's `Backup/` original where
  one exists, and the init image, the extracted tables and their documentation are rebuilt from it.
- **Persistent Skip and read-skip cases the port does not model** (`engine-re.md`, "Read skip and ctx+0x13dc"):
  - Op `0x88(1)` while the ADV service is suspended (after op `0x19b`). Native sets only `ctx+0x13dc`, and the next
    polling text wait starts fast-forward. The port sets its script fast-forward channel at once.
  - Op `0x19c`'s guard `ctx+0x6f86c`. With the guard set and nothing to restore, native keeps `ctx+0x13dc` and
    run-state `0x08000000`. The port always clears them.
  - Op `0x101` clears run-state `0x08000000` whatever started it (`0x4170ce`). The port ends only its physical
    channel, so fast-forward from persistent Skip or read-skip survives the op.

  Neither game has shown a difference from these. Done when each is modelled with a test, or shown unreachable.
- **A sleep inside an open statement block may present a frame (suspected).** `GodotAdvHost.Sleep` requests
  presentation on every sleep, which sets `_presentRequested`. `ConsumePresentationReasons` withholds the tick render
  in an open `0x1f4` block only while no presentation is requested, so a sleep inside an open block publishes the
  frame. Native's main loop renders nothing while a block is open, unless run-state `0x400` parks it on a transition
  (`engine-re.md`, statement blocks). That a sleep leaves `0x400` clear is not checked. The behaviour predates the
  2026-11-07 short-sleep change. Done when a script that sleeps inside an open block is compared with native, or a
  scan shows that none does.
- **The `0x90`-byte SYS4INI block.** Its meaning is unknown and no consumer is known (OMS spec §13). Revisit only if
  one appears.
- **LZSS encoder parity (optional).** Port saves match native byte for byte except the LZSS-compressed bytes,
  where the encoder's match choices differ. Matching is optional, since native reads both.
- **Surface 0x20's created flag in Kamidori saves.** Kamidori SAVE01 and SAVE05 (k1 sandboxes, 2026-11-07), made
  by both targets from the same history, differ in one surface record besides the op `0xd0` clock samples: slot
  `0x20` has created `1` on the port and `0` on native. Slot `0x20` is the legacy transition scratch surface
  ([sys4-format-notes.md](sys4-format-notes.md), "Surface-record bookkeeping"), so the port probably runs a
  scratch write that native skips somewhere between the field and the workshop. Only saves see the flag: neither the
  restore nor op `0x216` reads it. Done when the writer is found and matched.

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

- **Portability gates** listed in `remake-architecture-and-roadmap.md` ("The open portability gates remain separate
  follow-ups"): mixed-case roots, fresh-profile writes, native Linux desktop, the movie preroll recheck, and Linux
  font bundling (`platform-portability.md`, "Deferred Linux font-bundling follow-up").
- **CI actions pinned to Node.js 20 releases.** Since 2026-11-06, CI's annotations warn that `actions/checkout`
  v4.4.0, `actions/setup-dotnet` v4.3.1, `actions/upload-artifact` v4.6.2 and `actions/cache` v4.3.0 target
  Node.js 20. The runners force them onto Node.js 24 for now. `actions/download-artifact` v4.3.0 is the same
  generation; it runs only in tag publication, whose annotations were not read. Done when each action is SHA-pinned
  to a release that targets Node.js 24, and the core, release-build and FFmpeg SDK workflows run green without the
  warning.
- **Deferred by the user:** the `LICENSE` copyright holder is blank; `main` trails `develop` (the GitHub default);
  artifacts published before their notices existed are not backfilled, but taken down or updated if anyone asks:
  the `v0.4.0` packages lack the Godot, .NET and MinGW-w64 notices, and the `deps-ffmpeg-ome-8.1.3-mpeg1-r1` Windows
  SDK lacks the MinGW-w64 one. Later SDK revisions and packages carry them.

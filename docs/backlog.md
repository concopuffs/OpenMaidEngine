# Backlog

Open work items that nothing else schedules: known fidelity gaps, deferred follow-ups and accepted ideas. Each item
says what is missing, where the evidence lives, and what "done" means. Delete an item in the change that resolves
it; git history keeps the record.

- Direction and phase order stay in `remake-architecture-and-roadmap.md`.
- Future work a design spec accepted stays in that spec, linked from here rather than restated.
- The cross-session status memory points here for open items instead of keeping its own list.

## Engine fidelity

- **Engine shell menu bar: undriven cases, and optional look differences.** The bar, the fullscreen strip and 終了
  come from the shell model, and 設定, ﾊﾞｰｼﾞｮﾝ情報 and 動作環境 are the options panel
  (`superpowers/specs/2026-11-09-engine-shell-design.md`). Every command either game can reach does what native's
  does, the rest are grayed as natively, and the `option` scenarios leave the same `SYS4REG.INI` on both targets
  (`engine-re.md`, "Native menu bar" and "Screen mode"). Still open:
  - Keyboard mode's pointer moves are not driven natively: the driver's posted moves do not reach Windows' menu
    loop. The port moves the selection without opening a dropdown, by Windows' rule. Escape in the loop is modelled
    on Windows' rule too, since the driver refuses ESC on native AGE. The strip's window-menu Move does nothing in
    the port; natively it would start Windows' move loop on the strip.

  Done when each case is driven natively and the port matches, or is accepted here as a gap. Optional, since the
  spec makes pixel parity of the chrome a non-goal (§10): native popups may extend past the game window (the ﾍﾙﾌﾟ
  popup spans x 744..989) where the port clamps them inside it, and so opens the window menu at the bar's top rather
  than one row above; Latin glyphs lack ClearType's exact rasterization; the strip's minimize and restore buttons use
  Windows 10's default hover colours. Out of scope since 2026-11-06: the 設定 radio groups, the right-click ﾒｯｾｰｼﾞ
  popup and 設定 page 101, which neither shipped game can reach (`engine-re.md`, "Native menu bar", "Out of scope").
- **Input-latch cases the port does not model.** The port's latch (`VirtualMachine.InputLatch`) covers effects,
  sleeps, the ops between waits, the coroutine yield, and every consumer and flush point that SC0000 reaches
  (`engine-re.md`, "Effect skipping and the input latch"). Still open:
  - A trigger held during a run-state-`0x400` effect with service-flags bit 0 set, which never polls, is not offered
    to a later effect that polls under `EffectSkipOnClick` 1. The port offers it only to the reveal, the movie, the
    hidden-window wait, the timed sequence and the resumed text wait.
  - The op-`0xd5` timed sequence's poll (`timed_callback_sequence_tick`) is implemented from the decode and
    synthesized tests only. No native run has clicked during a sequence: HISTORY's lasts 50 ms, and the others run
    deep in gameplay (SHOWGROW is the one Himegari sequence with an abort pc).

  Done when each case is driven natively and the port matches, or the case is shown unreachable in both games.
- **One unexplained port crash on exit (2026-11-06).** A driven port exited with `0xC0000005` on `drive.py port
  stop`. That session had switched to fullscreen and shown the top-edge strip. An injected click had then landed in
  the side bar outside the canvas: the driver passed viewport coordinates as window coordinates, fixed the same day.
  Seven later sessions exited 0. They covered stops in fullscreen and while windowed, and clicks in the side bars.
  Windows logged no Application Error event, and the next start overwrote `godot.log`. Done when a crash recurs with
  its log kept and is fixed, or after a period of driven runs without one.
- **Aspect mode: the unmodelled parts.** The mode-1 calibration of the primary and single-byte fonts, the in-cell
  placement and the surface-text draws are implemented (`engine-re.md`, "Aspect-mode font calibration", "Aspect-mode
  surface text"). Still missing:
  - the `display:AspectMode` setting, which calibrates every face when nonzero;
  - mode 2 (`text_calibrate_mode2_glyph_font@0x451840`), which no shipped game selects;
  - the display scale on the placement (`+0x40c->+0x4e0`);
  - a control byte's blank extent in surface text: native measures it with `GetTextExtentPoint32A`, and the port gives
    it the fallback advance.

  No difference has been seen. Done when `display:AspectMode` and the display scale are modelled or shown
  unreachable.
- **Op `0x82` flag 8 (voice replay) is not modelled.** Native replays a group's retained voice records under flag 8
  (`engine-re.md`, "Op 0x82 redraw"); the port ignores the flag. No shipped site sets it: Kamidori's six CONFIG sites
  pass flag 2. Done when a game that sets it is profiled and the replay matches native.
- **Packaged executables carry Godot's icon.** The window, the fullscreen strip and 情報 show the OME icon
  (`godot/assets/ome-icon.svg`, set at runtime), but the project sets no `application/config/icon` and the Windows
  export no `.ico`, so `OME.exe` in a package and the taskbar before startup show Godot's. Done when both exports
  carry the OME icon, built from the same SVG.
- **Kamidori saves carry the translation's engine id.** The port's Kamidori profile writes compatibility id
  `"433F"` (`0x46333334`, `engine/Ome.Engine/Profiles/kamidori.json`) at header `+0x04` of every save, the id of
  the translation's 4.33F. The clean install's 4.33G writes `"433G"` (`0x47333334`): native sandbox `SAVE.DAT` and
  `RT.DAT`, 2026-11-09. No reader checks the id (`engine-re.md`, "Save identity and native read checks"), so saves
  load both ways, but `Ome.Cli save-diff` reports the header difference. Done when the profile writes `"433G"` and
  `GameProfileSelectionTests` checks it.
- **The `0x90`-byte SYS4INI block.** Its meaning is unknown and no consumer is known (OMS spec §13). Revisit only if
  one appears.
- **LZSS encoder parity (optional).** Port saves match native byte for byte except the LZSS-compressed bytes,
  where the encoder's match choices differ. Matching is optional, since native reads both.
- **Resume the Kamidori fidelity sweep.** Since 2026-11-03 the sweep has driven native AGE and the port through
  the same steps (`tools/drive.py`, `rand-seed` before battles) and compared screenshots, VM positions and decoded
  saves. It paused on 2026-11-08 at 木漏れ日の深緑地, turn 4, with every difference so far fixed or listed above.
  Everything below was driven on the translated install's 4.33F build. Since then the game root is the clean
  install (4.33G, Japanese UI). Its native AGE loads the turn-4 save and runs a battle and a turn end, with
  `rand-seed` drawing as before. Re-check the port against it on the clean install before going further.
  Covered at parity:
  - the title, prologue and first map and battle;
  - 平穏なる採取地 to its clear (採取, DIG, STAGECLEAR) and the chapter scenes that follow;
  - the world map, town, church shop, tavern and workshop scenes, an alchemy craft and the SC1250 delivery;
  - 木漏れ日の深緑地's first four turns, three battles in all.

  Saves on both targets compare by decoded payload (`Ome.Cli save-diff`, `tools-reference.md`). Clock
  samples, gfx start stamps and save-history globals are expected to differ (`engine-re.md`, "Op 0x216 reads past
  the live surface table" and "Finished channels reset their targets"). Done when the story has been driven to its
  ending on both targets, with each difference fixed or listed here.

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

- **Brainstorm a localization framework (user request 2026-11-04).** There are two layers:
  - **Engine-owned text.** This is the strings that AGE.EXE and AGERC.DLL draw themselves. The engine shell's (the
    menu bar, the strip, 終了 and the options panel) already come from keyed tables, `Shell/Strings/ja.json` and
    `en.json`, which `OME.INI`'s `ui:Language` picks (`superpowers/specs/2026-11-09-engine-shell-design.md`, §6).
    Still fixed Japanese: the エラーが発生しました box (`VirtualMachine.DiagnosticCaption`) and other native message
    boxes.
  - **Game text.** This is script dialogue, CG text and system graphics. Mods and translation packs carry it.

  The engine's concern is the first layer. The goal is a framework, not a one-off English translation. Questions
  to settle:
  - String identity: keys versus native source text.
  - Locale selection and fallback: the native Japanese stays the default and the fidelity reference.
  - Per-profile overrides: games differ in wording.
  - How mods ship and override both layers through the loader.
  - Fonts and glyph coverage.
  - Keeping the drive protocol's native control texts (`dialogs`/`answer`) stable whatever locale is shown. The
    shell settled this for its own controls: they report their Japanese texts whatever `ui:Language` shows (spec §8).

  Done when a design is agreed (a spec under `superpowers/specs/`) and the remaining engine-owned strings go through
  it.

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

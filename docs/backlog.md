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
- **The window's close button does nothing while 設定 is open (the user's report, 2026-11-18).** With the options
  panel open, the game window's close button neither opens the 終了 dialog nor quits, and the player sees nothing
  happen. The fullscreen strip's close button goes the same way. `RequestUnforcedClose` (`godot/Main.Exit.cs`) returns
  at once while the panel, the 終了 dialog or the script error box is open. That follows native, where a modal dialog
  disables AGE's window, so its close button cannot ask. But native's 設定 is a window of its own, and Windows answers
  a click on its disabled owner window by beeping and flashing the dialog. That is Windows' rule; it has not been
  driven on native AGE. The port's panel is drawn inside the game window, so there the click just looks lost. Done
  when native's answer to the close button during 設定 is driven, and the port shows the player an answer: the
  flash and the beep, or another the user chooses, such as cancelling the panel and asking.
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
- **The `0x90`-byte SYS4INI block.** Its meaning is unknown and no consumer is known (OMS spec §13). Revisit only if
  one appears.
- **LZSS encoder parity (optional).** Port saves match native byte for byte except the LZSS-compressed bytes,
  where the encoder's match choices differ. Matching is optional, since native reads both.
- **Resume the Kamidori fidelity sweep.** Since 2026-11-03 the sweep has driven native AGE and the port through
  the same steps (`tools/drive.py`, `rand-seed` before battles) and compared screenshots, VM positions and decoded
  saves, with every difference so far fixed or listed above. Until 2026-11-08 it ran on the translated install's
  4.33F build; since 2026-11-09 it runs on the clean install (4.33G, Japanese UI). It stands on turn 5 of
  蒼月の南湖畔 at シセティカ湖, the third stage, after the water-spirit event. Covered at parity:
  - the title, prologue and first map and battle;
  - 平穏なる採取地 to its clear (採取, DIG, STAGECLEAR) and the chapter scenes that follow;
  - the world map, town, church shop, tavern and workshop scenes, an alchemy craft and the SC1250 delivery;
  - 木漏れ日の深緑地 from turn 4 to its clear on the clean install: battles, both 採取 points, the Shinigami
    event, STAGECLEAR, the rewards, SC0000's chapter scenes and SC0010 through SHOWALLMAP (op `0xfa`'s click wait,
    fixed 2026-11-09);
  - the town's 領主の館 event (SC1150) and 酒場 event (SC1250; its second event-CG display was black on the port
    until SYS4433's op `0x215` rule, fixed 2026-11-09);
  - the town's 広場 event (SC1060), ウィルの工房's events (SC1000 with its 開業成績 settlement, SC6050, SC6100), the
    増築&家具配置 screens and the departure scene (SC1490), 2026-11-10;
  - 一陣の訓練地 to its clear on turn 23: the SC0500 and SC2000 openings, trap and 採取 cards, two vortex seals, a
    locked door, battles with a level-up and enemy-phase counters, healing support, the Earthman scenes (SC4000,
    SC4510), magic attacks, STAGECLEAR, the 素材袋鑑定 appraisal and SC0500's closing scene. Battles end about
    0.26 s sooner on the port (the "Kamidori battles end" item below);
  - back in town, the next ウィルの工房 event (SC1000 from `0x8364`) and its 開業成績 settlement, 2026-11-10;
  - the town's next 広場 event (SC1050) and 裏路地 event (`$1$SC2600`, with SHOWALLMAP), ウィルの工房's SC1020
    event, and 蒼月の南湖畔 turns 1-5: the SC0500 and SC2000 openings, the SC5500 stage events, enemy-phase
    counters with level-ups, attacks, healing support, and money and healing cards, 2026-11-10.

  Saves on both targets compare by decoded payload (`Ome.Cli save-diff`, `tools-reference.md`). Clock
  samples, gfx start stamps and save-history globals are expected to differ (`engine-re.md`, "Op 0x216 reads past
  the live surface table" and "Finished channels reset their targets"). Done when the story has been driven to its
  ending on both targets, with each difference fixed or listed here.
- **Triage the opcode revision diff.** `tools/revision_diff.py himegari kamidori` lists 125 differing opcodes and
  26 differing shared helpers (`build/revision-diff/himegari-kamidori.md`), and `kamidori arterial` lists 36
  (`engine-re.md`, "Comparing engine builds"). Only `0x215` (fixed), `0x202` (the same in both) and the 4.33F/4.33G
  pair have been read. Each remaining row is a SYS4 revision change, an engine change outside the opcodes, or tool
  noise. The same triage is the first step for every game added later: diff its engine against the nearest
  profile's instead of playing it through. Start from the shared helpers, since one helper explains many rows
  (layering spec §7.2, §7.8). Each real change becomes a registry fact, a handler variant or a rule in the build's
  definition (spec §7.2, §7.3), or is recorded as unreachable by the port's games. Done when every Himegari–Kamidori row is triaged, with port changes made or listed here and
  native engine changes documented in `engine-re.md`.
- **Kamidori battles end about 0.26 s sooner on the port.** In 一陣の訓練地 (2026-11-10, sandbox k1clean) a battle
  took 10.92 s from 決定 to COMMITBTL on native and 10.66 s on the port. Timed in segments: click to BTL's
  timed sequence (`BTL@0x3c93`) 2.62 s against 2.52 s; from there to COMMITBTL 6.47 s against 6.31 s; COMMITBTL to
  REWARD 0.91 s against 0.89 s. SHOWGROW's level-up fill was visibly later on native too. The sequence itself cannot
  drift: native's `timed_callback_sequence_tick@0x408170` uses absolute `timeGetTime` deadlines, as the port does.
  Both gaps sit around the battle-effect movies: BTL opens them with op `0x236` before the sequence, and after it
  loops on `query-movie-surface-active` (`BTL@0x3d01`) until they end. This machine has LAV Filters installed, whose
  probe native pays on every movie open ("What the open costs" in `engine-re.md`). The probable cause is therefore
  the native environment, not the port. Done when the native driver can refuse third-party DirectShow filters
  (a `combase!CoCreateInstance` refusal of LAV's CLSIDs, as the 2026-11-06 probe did) and a battle timed with it
  either matches or shows a port gap to fix.
- **Revision diff: identify API calls and object layouts.** Windows API calls are facts without identity, so they
  are weak; `tools/frida/map_imports.py`'s RVA→name map (Himegari only so far) could name them per build. Object
  fields are weak because each struct's layout moves on its own; typing the base register (which struct a pointer
  holds) would let them map. Done when either makes a fact strong without raising the 4.33F/4.33G noise floor
  (one differing opcode).

- **Strip reveals and mask wipes are drawn as crossfades.** The port keeps each transition's mode, thickness,
  timing and blocking, but both render paths blend the whole target in at one alpha.
  - The legacy strip reveals `0x27`/`0x28` (Kamidori: one `0x27` call and 192 `0x28` calls across 47 scripts;
    `engine-re.md`, "Kamidori SYS4433 comparison image and K6 frontier") end in
    `LegacyScreenTransitionComposite.Alpha`, which ignores the mode.
  - The type-2 range wipe `0x24f` (Kamidori's story-portrait helper in ten scripts; `engine-re.md`, "Patterned
    retained-range transition opcode `0x24f`") goes through `CrossfadeApproximation` (`godot/Main.Compositor.cs`).

  Done when each mode reveals its strips or clip mask as native draws them, checked frame by frame against native
  with `drive.py` at a low `speed`.
- **Kamidori: History leaves different objects in a save.** `kamidori-history-after-load.steps` saves slot No.003
  after opening and closing History on SC0000's first name-plate page. Native's `SAVE03.DAT` and the port's differ
  under `save-diff` in 29 gfx records (2026-11-12): field `+0x14` of objects `0xd6d8`–`0xd6ef` is 53 natively and
  51 on the port, `0xe678`–`0xe67a` 26 against 24, and `0xd6f0` and `0xe67b` exist only natively. The port's save is
  the same on `develop` (`1d7a39e`), so this predates the engine layering work. The scenario's History pictures match
  native. Done when the records match, or the field is shown to be a stamp and listed with `save-diff`'s expected
  differences (`tools-reference.md`).
- **Sweep for ops that are still stubbed.** An op counts as implemented once the handler table binds its name
  (`VirtualMachine.Implements`), and both games' catalog reports list no unsupported op
  (`build/games/<game>/catalog-opcode-coverage.json`). So a handler that only stores its operand, or models one
  path of several, counts the same as a complete one, and nothing lists the gaps. Known cases:
  - op `0x21b` stores its value, and op `0x247` reads it back, but there is no draw-mode-1 tick render gate
    (`engine-re.md`, "Statement blocks"); both games use `0x21b`;
  - op `0x140` outside the ADV labelled-yield pattern emits a `Stub` trace and goes on
    (`VirtualMachine.ControlFlow.cs`);
  - op `0xd8`'s timer and op `0x1d9` have no handler; neither game's scripts use them.

  The registry's markers are stale too:
  - `noop_headless = true` is still set on ops `0x1bf`, `0x1d5`, `0x1f4` and `0x1f5`, which the port models;
  - 255 catalog-only ops say "the port currently traces and skips it", though an op without a handler halts unless
    `--probe` is given (`VirtualMachine.StepInstruction`).

  Done when the sweep has compared each bound handler with its native handler and listed each one that does less.
  Each listed gap is then fixed or gets its own item here, and the registry's `noop_headless` flags and stub summaries
  say what the port does.
- **A voice replayed from History may loop natively.** Op `0x1bd` replays a History voice with playback variant 1,
  which `voice_play_indexed_asset@0x488330` writes into channel 12's sound-buffer state. That is the field
  `sound_buffer_start@0x484270` writes an op `0xb5`/`0xba` start mode to, and `sound_stream_fill_quarter@0x483b70`
  rewinds the decoder at the end of the sound whenever it is nonzero (`engine-re.md`, "Channels 10 to 12" and "SC0000
  native SFX / BGM-fade family"). So natively the replayed voice may play again from its start until something stops
  or replaces it. No native run has listened past a replayed voice's end. The port plays every voice once and only
  keeps the variant (`Audio.VoicePlaybackVariant`; `engine-re.md`, "Retained History record model and lifetime",
  calls its meaning unproven). Done when native is driven past the end of a History replay, with channel 12's state
  read while it runs, and the port plays variant 1 as native does, with `engine-re.md`'s passage saying which.

## Robustness

Gaps the 2026-11-10 codebase review found and the fixes that followed it left open. None affects retail scripts.

- **Unknown launch flags are ignored.** Flags are parsed across `Main._Ready`, `AssetLaunchOptions`,
  `GameProfileSelection`, `WindowLaunchOptions`, mod linking, screenshots and the port options, each by its own
  lookup, so a misspelt flag is silently ignored. Since 2026-11-10 `--seed`, `--speed` and `--sleep-scale` reject a
  bad value with a warning. Done when one table lists every flag with its arity, startup warns on any argument it does
  not name, and `tools-reference.md` is generated from or checked against it.
- **The movie shim configures audio resampling once.** `native/ome_movie_ffmpeg/ome_movie.c` sets up swr when the
  movie opens, from the audio decoder's channel layout, sample format and rate, and a seek only re-initialises that
  setup. A stream whose layout or format changes mid-stream would be converted with the old setup, which can read a
  missing plane. This is inferred from FFmpeg's behaviour; no shipped movie is known to change layout. Done when the shim reconfigures swr when a frame's layout, rate or format differs from the
  last, with a synthesized test stream that changes layout.

## Architecture

Structural problems the 2026-11-10 codebase review found in `VirtualMachine` (29 partial files), `GodotAdvHost` (6;
lifted into `Ome.Engine` as `EngineHost` in milestone 4 of the design below, and removed in milestone 12) and `Main` (15). A partial class only changes which
file a member is in, so any file can still read or write any field. The review proposed one migration for all of them
(`vm-host-main-restructure-design-2026-11-10.md` at the workspace root, outside this repository); that plan was not
adopted. On 2026-11-12 the engine layering design was accepted instead
(`docs/superpowers/specs/2026-11-12-engine-layering-design.md`): handlers, natives and devices in three projects, and
engine builds declared in one file. Each item below that it covers names the milestone that does it; the others are
still handled on their own. Each change should be behaviour-neutral and pass the spec's acceptance 4: the suite, the
`oms-rebuild` trace gate, the drive scenarios, `save-diff`, and timing or frame-time captures where they apply.

- **No single error path.** The VM reports a fault in several ways: 57 `HaltReason` assignments, `VmHaltException`,
  the native error dialog (`ShowScriptError`), host warnings (`ReportWarning`), and exceptions that end the VM
  thread. The mod linker, the OMS compiler and the CLI each keep their own catch filters. Three of the review's bugs
  were a fault that matched no filter: a halt inside a callback, `ModPackageException` at launch, and a drive command
  that threw. All three are fixed. Done when each layer has one documented fault type that its filters name, and the
  VM turns any fault raised during a step into a halt, with a reason and a trace event, in one place.
- **Modal dialogs are separate fields.** `Main` keeps the 終了 dialog (`_exitConfirmation`), the script error box,
  the options panel, the screenshot folder dialog and the full-width editor in their own fields, plus a hold
  counter (`_frontendModalHolds`). Input routing, the drive `dialogs` and `answer` steps and screen-mode changes each
  test the fields: they are used in 7 files. The host's half is done: since engine layering milestone 9
  (2026-11-13) the error box, the diagnostic box and the full-width editor are requests of the shell-services native
  (`Natives/ShellServices.Dialogs.cs`) that `Main` completes and a stop or a reset cancels, where the host kept an
  event and a result field for each. Done when one owner knows which modal is open, and routing and the drive steps
  ask it.
- **`Main` has no seams.** `_Ready` is 542 lines (`Main.cs:76-617`) of flag parsing, session building and wiring, and
  12 files read `_screenWidth`. Launch-flag parsing has its own item under Robustness. Done when startup, the frame
  pipeline and each front-end concern (audio, movies, compositing, the shell, input, driving, screenshots) own their
  fields; `_Process` calls them in an explicit order rather than Godot's node order; and the screen geometry is one
  shared object.
- **The GPU renderer declines screen transitions.** `GpuRetainedRenderer.TryDraw` (`godot/GpuRetainedRenderer.cs`)
  leaves a frame description to the reference renderer (`engine/Ome.Engine/Hosting/ReferenceRenderer.cs`) while a
  whole-screen transition runs (the description's `ScreenTransition`), and `Main.Recomposite`
  (`godot/Main.Compositor.cs`) sends it every frame of a `--gfx-log` or `--timeline-log` run. The screen transitions
  are AGE's blocking transitions between two complete frames: the black and white fades, `0x25`'s crossfade and the
  `0x27`/`0x28` strip reveals. Kamidori's title fades to and from Game Start are among them. Native (mode 4) flattens
  the target frame into one surface and blends it over the captured source frame
  (`ReferenceRenderer.DrawFlattenedTransitionTarget`). Fading each item on the GPU would dim the frame and let layers
  show through each other, so the GPU renderer needs an offscreen target it does not have.

  The reference renderer's frames can take tens of milliseconds (engine layering spec §6.3). Also, where a full
  redraw draws over the previous frame (`PreserveExistingPixels` without `AppendLayers`), the reference keeps the old
  pixels under what the frame does not cover and the GPU renderer shows black there; no driven frame has shown it.
  Done when the GPU renderer draws screen transitions through an offscreen texture (a `SubViewport`) blended over the
  captured source and keeps the uncovered pixels, and the reference renderer draws only when chosen
  (`--render-backend software`, the two logs). Check it with the driver's `reference-diff` on a driven fade, within
  the tolerance of the engine layering spec §6.3, "As built".

- **The two renderers filter scaled layers differently.** Where a texture item is scaled or rotated, the GPU renderer
  (Godot's linear filter, blending in floats) and the reference renderer
  (`engine/Ome.Engine/Model/SoftwareAffineRasterizer.cs`: D3D9's bilinear with 8-bit weights, blending in integers)
  differ by a few levels, in fine stripes across the layer.
  The driver's `reference-diff` found it in Himegari's SC0000 effect band
  (`tools/drive/scenarios/port/himegari-reference-diff.steps`): on page 14 and mid-effect on page 15 a full-screen CG
  is drawn scaled under additive light, and 0.3 % and 10 % of the frame's pixels differ by more than 3 levels, at
  most 6. The tolerance set before measuring allowed 1 % of a frame (engine layering spec §6.3, "As built"). The
  difference predates milestone 7: the GPU path's pixels there are the same as before (`himegari-effect-waits.steps`'
  page 14 shot), and the two paths filtered this way before. Kamidori's field, 214 scaled map cells, differs by at
  most 3. Neither renderer has been compared with native's output on a scaled layer. Done when a native capture of
  such a frame shows which filter matches AGE, the other renderer is brought to it, and `reference-diff` keeps those
  pages within 1 %.

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
- **Compiler warnings.** Two of the five §8.2 warnings are missing: an implicit string↔number conversion, which needs
  a type checker, and an `atomic` body that contains a wait (OMS spec §8.2). The compiler reports the other three
  through `OmsWarning`: an opcode no corpus executes, a division by a constant zero, and a recursive call over bound
  parameters. Done when both are reported, each with a test.
- **Whole-script rebuilds drift saves.** `oms-compile` re-derives T1/T2/T3, so old saves can drift on a rebuilt
  scene. Fragments keep tables stable and are the save-safe route; a `replace/` script built from edited source does
  not. Done when whole-script compiles can keep the base's table order (for example from the sidecar).
- **Prelude coverage.** About 2.4k dialogue pages stay core statements: pages that interpolate `GS[…]`, carry
  furigana or span several rows. A scene-skeleton template would also replace the copied skeleton in
  `example.prologue`.
- **Allocator residue (optional).** The remaining temporary-slot overrides (OMS spec §13) and Kamidori's explicit
  `L[0x0]` in `place_scene_background` (394 sidecars) only matter for sidecar-free byte identity.
- **The `extended` compile mode.** The language design gives each target a mode (OMS spec §3.3). Under `extended` a
  unit may use engine extension opcodes, at `0x10000` and above, through a generated `abi.ome` module (§9.4); its new
  scripts get virtual identities the engine owns; and its unpinned globals live in engine-owned, namespaced mod state
  (§9.2). None of it is built. `Ome.Script` has no mode, and a manifest's `target = "extended"` only marks a package
  that the port alone runs (`ManifestReader`, `ModeCheck`). The loader spec leaves the mode out of version 1 (its §1)
  and does not list it as future work (its §12). Lua has since taken most of its purpose: mod state is `maid.state`,
  and logic beyond the AGE ABI is a handler. Still without a route: bytecode that calls the engine or a Lua function
  (Lua spec §16, "Calling Lua from bytecode"), and how mod state that OMS can reach would meet the save (OMS spec
  §13, "Extended mod-state model and its save interplay"). Done when a design note settles which of the three parts
  are still wanted beside Lua, and each one kept is built with a gate while each one dropped is recorded as dropped.

## Mod loader and modding

- **Toolchain packaging for exported ports.** Fragments compile against the repository's OMS modules
  (`oms/<game>/`), globals and patch points (`build/games/<game>/`). An exported port without the repository
  links only mods that need none of them (`tools-reference.md`, port `--mod`). Done when an export carries or
  embeds these files and a fragment mod links in an exported build.
- **Mod-owned globals.** `global` declarations must pin a cell (`@ address`). The linker-allocated and `saved`
  globals of the language design (OMS spec §5.2, §9.2) are not implemented, and which cells are free (and which a
  save keeps) is open (§13). Mods pick unused cells by hand meanwhile (`docs/modding/oms-language.md`). Done when
  the linker allocates mod globals per profile from evidenced free cells.
- **Kamidori new-scene example.** The save-capable scene recipe (`docs/modding/new-scenes.md`) is proven on
  Himegari only (`example.prologue`, gates 4 and 5). Done when a Kamidori scene mod, built on one of its append
  scenes' skeletons, plays and saves on native AGE.
- **Two mods' unpinned fragment locals share slots.** A fragment's unpinned `local` takes the first slot above the
  base's highest (`OmsCompiler` patch mode, `UsePatchBase`), and the applier keeps each plan's slots as compiled
  (`OmsPatchApplier`), so two mods patching one script get the same slots. If one mod's code runs while the other's
  local is live (an inserted block between the other's set and use, or a call into it), they overwrite each other,
  and no conflict is reported. Spec §10.2 does not cover it. Done when the design picks a rule (the applier
  relocates each plan's unpinned locals above the previous plan's, or the linker reports overlapping live ranges)
  and a two-mod test shows it.
- **An inserted instruction does not name its mod.** For code a patch inserted, a Lua frame, a handler's error report
  and the trace give the offset in the linked script and every mod that patched that script, not the one mod that
  inserted the instruction (Lua spec §8.1, "Positions"). The applier knows the owner (`OmsPatchProvenance.Plan`); the
  link cache keeps only where the base instructions went (`ScriptOffsetMap`). Done when the cache keeps each inserted
  run's owner, and `vm.frame`, an error report and `--lua-trace` name that mod, with a test over two mods that patch
  one script.
- **Package size limits are provisional.** A package may hold 65,536 entries, 1 GiB an entry and 8 GiB in all, and no
  entry may expand past 1 MiB at over 200:1 (`PackageLimits`; loader spec §11). They were set before any real package
  existed, to admit a full voice or CG replacement and stop a decompression bomb, and the spec says to revisit them
  once real packages exist. Done when a package of that size has been built and loaded, with its link time and memory
  noted, and the limits are kept or changed on that evidence.
- **A dev mod that stops the title's idle opening movie.** Kamidori's title leaves for `OP.BIN` once its idle
  count `L[0x29]` passes 100 while sound channel `0xb` is silent (`TITLE@0xd7`..`0xf1`). Callback slot `0xa`
  (`label_254`), the slot `dispatch-joy-callbacks` runs when no action is held, adds one every other 50 ms mouse
  tick, and a hover change resets it. So the title gives way to the movie after about 10 s without a hover change.
  Himegari's title has the same exit at 460 counts (`TITLE@0xd7`, `L[0x29] > 0x1cc`), about 46 s. A driven session
  or a person looking at the title that long finds the movie playing, and the next click skips the movie instead of
  pressing a button. That happened during the title-hover measurements (`../oms/examples/mods/README.md`,
  `example.kamidori-title-hover`). Done when a committed native-target mod removes that exit on each base it
  supports, so it runs in the port (`--mod`) and through `omp export` on native AGE. It is for development, so it
  goes outside `mods/`, which `Ome.Mods` embeds whole.
- **`kamidori.stage.loaded` may be missed at the load of a save made on a floor.** Read from the disassembly, not
  run. FIELD runs the loader chains, and reaches the point `kamidori.stage.after_loader` (`FIELD@0x527`), only when
  `field_map_continue_mode` is not 1 (`FIELD@0x4c9`). In ordinary play on a floor the mode is 1 (`FIELD@0x144d`).
  FIELD's own save command sets it to 2 around `SAVE.BIN` (`FIELD@0x28cb`..`0x28d8`), so a save made with it loads
  with 2 and the event is raised. At a load INIT2 runs the stage loaders itself when `G[0x428c]` is set
  (`INIT2@0x274`..`0x2b0`; FIELD sets it on entering a floor, `FIELD@0x1fd`), and raises no event. So a save made on
  a floor by any other route would load with the mode at 1: INIT2 has written the stage's tables, FIELD does not
  write them again, and a handler that rewrites them (`../oms/examples/mods/example.random-enemies`) has not run.
  Not known: whether the game offers such a route (the menu of an event scene during a floor, for one), and whether
  it reads the enemy table again after it has placed the enemies. Himegari's INIT2 runs no stage loader. Done when a
  driven run shows that no such save can be made, or `kamidori.stage.loaded` names a second point after INIT2's
  loader loop (`INIT2@0x2b3`) and a driven load of such a save shows the handler's change in place.
- **Accepted future work** in the mod loader design (`superpowers/specs/2026-11-03-mod-loader-design.md` §12; its
  launcher is built: `superpowers/specs/2026-11-15-launcher-design.md`, `launcher/`):
  - mod-owned sites (patching another mod's inserted code);
  - declarative data patches: designed, and accepted on 2026-11-18
    (`superpowers/specs/2026-11-17-data-patches-design.md`, which replaces that spec's sentence). Milestone 0 is
    built on `feature/data-patches` (`superpowers/plans/2026-11-18-data-patches.md`): the record map, `ome-records`,
    `<game>.data.layered` and `maid.text_fits`. No data file reaches a game yet. Done when the gates of its §12
    pass;
  - mod merging;
  - precompiled new scripts.
- **What each game copies from a field a data file can set.** A data file or a Lua edit at `<game>.data.layered`
  changes a unit's definition, but both games copy part of a definition into saved state when the unit joins, and
  into the battle entities when a stage places them. An edit then reaches a unit that has already joined only as
  far as the game reads the definition again, and what was copied while the mod was on stays in the save. A
  time-boxed scan of the disassembly found the copies (read, not run;
  `superpowers/plans/2026-11-18-data-patches.md` §8): Himegari's UNITECH copies `unit_starting_level`,
  `unit_base_stats`, `unit_starting_skill_ids`, `unit_starting_equipment_item_id` and the growth rates. Kamidori's
  UNITECH copies the costume, party cost and capacity, affinity, starting and weapon levels, base stats, stat
  caps, resistances and skills. SETEN, SETCH, CALCREVISE and EQUIP copy into the battle entities in both games.
  The scan does not follow branches, local calls or block copies, so the list may be short. Done for a game when
  its guide page (`modding/games/<game>.md`) says, for each field a data file can set, which saved values the game
  derives from it and when, each claim checked on the game's scripts, with a driven run for at least one copy.
- **The link report misses an overlap through an old name.** A record table or a field the registry renames keeps its
  old name (a `[[table]]`'s `aliases`, its `[table.field_aliases]`), and a `<game>.tables` file may still write it:
  the applier writes the cell the current name does and logs the old name (`superpowers/plans/2026-11-18-data-patches.md`
  §9.1; `mods/ome-records/lua/init.lua`). The report compares the documents' paths as text (`ModLuaLinker.KindLines`,
  data patches spec §5.7), so a file that writes `units.12.experience_reward` and another that writes
  `unit.12.experience_reward` set one cell with no shared-path line (`modding/data-files.md`, "The report"). No
  registry has an old name yet. Done when the report reads each kind's old names from its owner (the record map, or a
  list the kind declares), lists such a pair as a shared path under the current spelling, and a two-mod link test shows
  it.
- **Accepted future work** in the Lua scripting design (`superpowers/specs/2026-11-10-lua-scripting-design.md` §16).
  The UI and settings items below are two of its entries; the others are kept there only:
  - Lua that waits, and queued script calls;
  - calling Lua from bytecode (see also the `extended` compile mode, under Open Maid Script);
  - `maid.audio` and `maid.assets`, and profile state;
  - write groups and pattern selectors;
  - hooks on another mod's code (with the loader's mod-owned sites, above);
  - a debugger, and outside capabilities.
- **A devkit for mod authors.** Writing a mod takes the command line and the port's flags: `omp check`, `build`,
  `export`, `explain` and `lua-stubs`, the `omp text` commands, `oms-patch --fingerprint` and `oms-decompile`, and in
  the port `--mod`, `--lua-dev` (the writes a mod does not declare), `--lua-trace`, `--lua-reload`, `--text-log`, the
  Lua console and the page locator (F2, F3). `modding/cli.md` and `modding/lua.md` document each, but an author has
  to know them all and read their output as text. A GUI devkit would put the common work in one window: start a mod
  folder, fingerprint a script and start a fragment at the page the locator names, check and build with each mod's
  report shown, list a Lua mod's writes beside its `[lua] writes`, export for native AGE, and run the port with the
  authoring flags while showing its logs (`diagnostics/lua.log`, the trace). It would drive the same commands and
  files, as the launcher drives the engine (`launcher/`, launcher spec §5.1), and may share the launcher's Avalonia
  shell (`launcher/Ome.Launcher`): the launcher serves players, the devkit authors. Outside the repository it needs
  the toolchain an export lacks (the first item of this section). An editor extension (VS Code, say) is the other form to weigh, instead of the
  application or beside it: it could bundle OMS language support (highlighting, the modules' names, the compiler's
  errors in place), run the commands from the editor, point lua-language-server at `omp lua-stubs`' definitions, and
  open a game script (`.BIN`) as its `oms-decompile` text. Done when a design spec settles the form (an application,
  an extension or both), its scope and its toolkit, and a first version covers check, build and a port run with the
  authoring flags.
- **An Original mode for profiles: Play runs the game's own AGE instead of OME.** The user proposed it on 2026-11-16, to
  sanity-check the port against the original from the launcher. They left it out of the launcher's first version,
  so as not to turn the polish round into UI work (launcher plan §10).
  - **The modes:** Extended and Compatibility stay as they are (launcher spec §6), and Original is a third. It needs
    the install's AGE executable, and most settings are off in it.
  - **What a native run uses:** AGE reads and writes its own `SYS4REG.INI` and saves, by its `USEAPPDATAFOLDER` (the
    install folder or `%APPDATA%\Eushully\…`), not the profile's. No mods apply, nothing reaches the engine log, and
    it is Windows only.
  - **The name:** Original, stored as `mode = "original"` (the user's choice, 2026-11-16). Compatibility keeps
    `native`, which matches the package manifest's `[game] target = "native"`, the stack native AGE can run.
    The launcher already says "the original game" for AGE's own saves.
  - It changes the modes the user decided (spec §15.1).
  - Done when a short design note settles what the mode turns off, where its saves are shown, and how the launcher
    starts and waits for AGE, and the launcher builds it.
- **A hook point survey for each game.** Each base mod raises seven events: the experience award,
  `<game>.data.layered` and `<game>.data.ready` at boot, and the four stage events around FIELD's walks of the stage chains
  (`superpowers/plans/2026-11-14-lua-scripting.md` §11). The appends register handlers in many more chains, each a
  place Eushully's own extensions hook: Himegari's class change, SCJUMP and information messages; Kamidori's 18 32-slot
  chains (`kamidori-data-tables.md`, "Hook chains"), among them the shop, costume sprites, CG substitution and the
  stage flags. Done for a game when a session has surveyed its chains and other natural extension points, and the
  base mod raises an event at each that has a use, with its fields named in the registry and a test that it binds on
  every install the game guide lists. Repeat for each game the engine adds.
- **More base mod helpers.** Each base mod has one helper, `give_item` over ADDITEM (`mods/<game>-base/lua/`;
  plan §9.3). The check ahead found more wait-free scripts in both games (plan §9.2: 22 of 27 probed in Himegari, 30
  of 44 in Kamidori), most of them battle arithmetic (`CALC*`) or map and unit edits (`SET*`, `DEL*`) that read
  their arguments from cells nobody has named yet. Done for a script when its argument and scratch cells are in the
  registry with evidence, its base mod's helper sets and puts them back, and a test runs it on the game's own
  script.
- **A UI for Lua mods.** A mod cannot show the player anything but a log line, and the status line's notice belongs
  to errors; it cannot open a window or ask a question. The Lua spec leaves `maid.ui` as future work
  (`superpowers/specs/2026-11-10-lua-scripting-design.md` §16, "Engine services"). The way is not the game's own UI:
  a SYS screen is hot spots and graphics objects its scripts place one by one, so a mod would rebuild each game's look
  from cells. The front end already draws windows of its own over the game, the options panel (設定) and the 終了
  dialog, and holds the engine while one is open (the front end's modal hold). A mod would ask the front end, through
  the native that owns it, for a window built from a small set of controls (text, buttons, a list, number and switch
  fields) and get the player's answer back. To settle: whether a window holds the game, as the panel does, or stays
  open beside it; how the answer reaches the mod, since a handler cannot wait (§16, "Lua that waits"); what a save
  made while one is open records; and how a window looks in each game and language. Done when a design spec settles
  the API and the front end's part, and an example mod opens a window, reads the player's choice and acts on it, in
  the port and under the driver.
- **Settings for mods.** A mod has no setting a player can change: a value that should be tunable is written into
  its Lua. The Lua spec lists mod settings as future work (§16, "Profile state and mod settings"). The options panel
  already has a section of the port's own, OME, whose values live in `user://OME.INI` and are committed by 決定
  (`PortOptions`, `OmeIniStore`; `superpowers/specs/2026-11-09-engine-shell-design.md` §5.6). A mod would declare its
  settings in its manifest (a key, a label in the mod's languages, a type such as a switch, a number with a range or a
  choice, and a default); the panel would show each enabled mod's settings in a section of their own and keep the
  player's values outside the saves; and the mod would read them through `maid`, with a way to learn of a change made
  while it runs. To settle: one value per profile or one across games; what becomes of a kept value a mod's new
  version no longer accepts; whether a setting may also be per save; how `omp check` checks the declarations; and
  how the driver sets the values and sandboxes them, as it does `OME.INI`. Done when a design spec settles these, the
  panel shows a mod's settings and keeps the player's values, a handler reads them, and an example mod uses one
  (`example.random-enemies` choosing between every ordinary enemy and only those near the level of the stage's own, say), with a test
  and a driven run.
- **Lua-CSharp past 0.5.8.** 0.5.8 writes fractions in .NET's form (`3 * 1.1` reads `3.3000000000000003`, `1/0`
  reads `Infinity`), raises on `string.format("%d", 2.5)`, writes `%e` unformatted, and refuses to compile `t[0x10]`,
  which `LuaSource.Prepare` works around. Upstream's `main` fixes them all, the number text through the option
  `LuaPlatform.NumberFormat = Lua` (`superpowers/plans/2026-11-14-lua-scripting.md` §5). It is text only and does not
  hold Lua back (§5.5, decided 2026-11-14). Done when the runtime uses a Lua-CSharp release with the fixes, sets
  `NumberFormat = Lua` and formats `print`'s numbers the same way (`LuaModSession.Show`), drops `LuaSource.Prepare`
  once its test passes raw, passes the conformance run of plan §5 again, and `modding/lua.md` and
  `modding/troubleshooting.md` describe numbers as the new build writes them.

## Localization

The localization framework is built: milestones 0 to 6 of `superpowers/specs/2026-11-10-localization-design.md`, with
its eleven gates, and `align`, `fit` and the overflow report (§7.2, §7.3), which no milestone held (2026-11-11).
Modders' guides: `modding/translating.md` (the walkthrough) and `modding/languages.md` (the reference); the import
test's results: `localization-import-test.md`. The future work spec §17 lists stays in the spec.

- **Translators cannot find or export the pictures to translate.** A language asset replaces a picture by its file
  name (`modding/languages.md`, "Images and other assets"), but no modder command names the pictures on screen or
  writes an original as PNG to edit. Today only developer tools come close: the port's `--gfx-log FILE` logs each
  drawn object's resolved file, `omp text import-scripts --package` writes PNGs only for pictures an in-place
  translation replaced, and `tools/convert_agf.py` converts extracted AGFs to BMP for Himegari's archives only.
  Done when the port or `Ome.Cli` names the pictures on the current screen, and `Ome.Cli` exports a named asset as
  PNG with the port's decoder, both documented in `modding/translating.md` step 8.

- **English in Kamidori's stat boxes and item tables is not compared with native.** With `--language en`, names and
  text spilled out of Kamidori's RPG stat boxes, and a name out of its plate in the opening ADV scene (the user's
  report, 2026-11-14). Presented text was then laid out by a second model; it now takes native's cells (localization
  spec §7.4), and on gate 12's ADV pages every plate and run of text is within a pixel of native AGE on the
  translated install (`localization-import-test.md`, "Gate 12 results"). The stat boxes and item tables have not
  been compared: a click route to the native item screen landed on another screen. Done when a geometry pair in
  `tools/drive/scenarios/geometry/` reaches a stat box screen and an item table on both targets, with their regions
  in `text_geometry_compare.py`, and passes, or each difference is explained.

## Platform and project

- **Portability gates** listed in `remake-architecture-and-roadmap.md` ("The open portability gates remain separate
  follow-ups"): mixed-case roots, fresh-profile writes, native Linux desktop, the movie preroll recheck, and Linux
  font bundling (`platform-portability.md`, "Deferred Linux font-bundling follow-up").
- **Data beside the program.** Since 2026-11-18 the engine ships as one NativeAOT library (`platform-portability.md`,
  "Fewer files in a package"), and all its data stays inside it: the game manifests
  (`engine/Ome.Engine/Profiles/*.json`, which `GameProfileRegistry` reads from `Ome.Engine`'s resources), the opcode
  table, the base mods (`Ome.Mods`), the shell's strings, the panel font and the icon. On 2026-11-16 the user named
  the game manifests as data to separate out eventually. A manifest holds the art addresses and their hashes (launcher
  spec §8.7), so where a separate manifest is read from decides whether it is trusted as the program is.
  "Toolchain packaging for exported ports" above asks the same question for the OMS modules. Done when the user has
  chosen the data that sits beside the library and the release builds ship it there.
- **A package folder in a cloud folder may be refused.**
  - `FolderPackageContainer` (`engine/Ome.Mods/Packages/`) refuses every entry with the `ReparsePoint` attribute, to
    keep a package inside its folder (mod loader spec §3.1). A cloud folder's placeholder (OneDrive's
    `IO_REPARSE_TAG_CLOUD_*`) is a reparse point too, so a working copy kept in OneDrive may not open.
  - Whether a process sees the attribute depends on Windows' placeholder compatibility mode. That is not measured,
    and making a placeholder needs a sync root registered through the Cloud Filter API, which changes the machine's
    state.
  - The launcher's copy refuses only what .NET reports a `LinkTarget` for (symbolic links and junctions), which
    would keep the rule and let placeholders through.
  - This is older than the launcher. It came to light on 2026-11-16, when the user dropped launcher spec §12's rule
    that a profile root holds no link (launcher plan §9.7); that rule's own placeholder question went with it.
  - Done when the package walk refuses only links, or a measurement on a real OneDrive folder (dehydrated files
    included) shows the attribute is not seen.
- **Keep the game installs in one folder.** The workspace root holds each install under its own name:
  `Himegari_Game/`, `Kamidori/`, `Kamidori_Translated/` and `Arterial/創刻のアテリアル/` (added 2026-11-09 for
  native-engine comparison only). Every tool that finds them has its own table of those names:
  `tools/paths.py` (`_CONVENTIONAL_GAME_ROOTS`, `REFERENCE_INSTALLS`), `engine/Ome.Engine/Sys4/Paths.cs`,
  `engine/Ome.Cli/Program.cs`, `run-godot.ps1`, and about 25 engine tests. Done when the installs sit under one
  games folder, every table resolves a game's install from that folder, and the docs naming the old locations
  (`PROJECT-STRUCTURE.md`, `AGENTS.md`'s three-way separation, the modding game pages) follow.
- **Deferred by the user:** the `LICENSE` copyright holder is blank; `main` trails `develop` (the GitHub default);
  artifacts published before their notices existed are not backfilled, but taken down or updated if anyone asks:
  the `v0.4.0` packages lack the Godot, .NET and MinGW-w64 notices, and the `deps-ffmpeg-ome-8.1.3-mpeg1-r1` Windows
  SDK lacks the MinGW-w64 one. Later SDK revisions and packages carry them.

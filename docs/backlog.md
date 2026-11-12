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
  profile's instead of playing it through. Revision changes need a decision: model them per ABI as op `0x215` is, or record that the port's games
  never reach them. Done when every Himegari–Kamidori row is triaged, with port changes made or listed here and
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

- **Strings with bytes outside JIS X 0208 decode to nothing.** `Sys4StringCodec.Decode` (and its twin in
  `tools/sys4load.py`) accepts single bytes below `0x80` and `0xA1`-`0xDF`, and double-byte characters whose lead byte
  is `0x81`-`0x9F` or `0xE0`-`0xEA`. It refuses any other byte: lead bytes `0xEB`-`0xFC` (CP932's NEC and IBM
  extensions and user-defined characters, and two unused), `0x80`, `0xA0`, `0xFD`-`0xFF`, and a bad trail byte. The
  whole string then decodes to nothing and its run draws empty. Native reads every byte before the NUL and hands it
  to GDI (`engine-re.md`, "Strings with control characters"). No script in Himegari or Kamidori (clean and
  translated) holds such a byte, so nothing shows it yet; a mod or another game could. Done when the codec keeps
  these strings byte for byte (CP932's extensions decoded, other bytes kept so they encode back) and the port draws
  them as native does.
- **Strip reveals and mask wipes are drawn as crossfades.** The port keeps each transition's mode, thickness,
  timing and blocking, but both render paths blend the whole target in at one alpha.
  - The legacy strip reveals `0x27`/`0x28` (Kamidori: one `0x27` call and 192 `0x28` calls across 47 scripts;
    `engine-re.md`, "Kamidori SYS4433 comparison image and K6 frontier") end in
    `LegacyScreenTransitionComposite.Alpha`, which ignores the mode.
  - The type-2 range wipe `0x24f` (Kamidori's story-portrait helper in ten scripts; `engine-re.md`, "Patterned
    retained-range transition opcode `0x24f`") goes through `CrossfadeApproximation` (`godot/Main.Compositor.cs`).

  Done when each mode reveals its strips or clip mask as native draws them, checked frame by frame against native
  with `drive.py` at a low `speed`.

## Robustness

Gaps the 2026-11-10 codebase review found and the fixes that followed it left open. None affects retail scripts.

- **Integer cells are not normalised to 32 bits when written.** Native cells are 32-bit. The port's are `long`:
  immediates load zero-extended (`Sys4Loader`), C# writes and loaded saves sign-extended, and `Write` stores
  whatever it is given (`VirtualMachine.cs`, `Read`/`Write`). Since 2026-11-10 arithmetic wraps and compares take
  the signed low 32 bits (`VirtualMachine.Values.cs`, `I32`), so `bit-set g,31` then `lt g,0` gives native's 1.
  Values that only move between cells (packed ids, colours) still keep whichever extension they arrived with, and
  consumers that compare or index with the wide value could still differ. Neither disassembled corpus has a high-bit
  immediate in an arithmetic or compare op, so this is latent for retail scripts and live for mod bytecode. Done
  when every integer cell holds one canonical form, written in one place (see "VM state has no owners" under
  Architecture), and a test round-trips a high-bit value through an immediate, a write, a save and a load.
- **The port's image and texture caches never evict.** `GodotAdvHost._images` (`godot/GodotAdvHost.Surfaces.cs`),
  `Main._pixelCache` (`Main.Compositor.cs`) and `GpuRetainedRenderer`'s `_textures` and `_lerpMaterials` keep every
  entry for the session: about 1.9 MB of RAM per 800×600 image, and again in VRAM. A long session grows without
  bound. Done when each cache has a bound or a release point (a scene change, a released surface) and a long
  driven session shows flat memory.
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

Structural problems the 2026-11-10 codebase review found in `VirtualMachine` (27 partial files), `GodotAdvHost` (6)
and `Main` (15). A partial class only changes which file a member is in, so any file can still read or write any
field. The review proposed one migration for all of them: state components, opcode modules and front-end
controllers (`vm-host-main-restructure-design-2026-11-10.md` at the workspace root, outside this repository). That
plan was not adopted on 2026-11-10, so each problem below is to be handled on its own, by whatever design fits when
the work touches it. Each change should be behaviour-neutral and checked by the suite, the `oms-rebuild` trace gate
and the drive scenarios.

- **Opcode dispatch is kept in three hand-written lists.** `StepInstruction`'s switch (`VirtualMachine.cs`, 401
  case labels), the 18 family handlers' switches (392 labels), and `OpcodeRuntimeCoverage.ImplementedOpcodes` (283
  opcodes). `opcodes_build.py` reads that last list with a regex (`load_runtime_coverage`) to publish coverage. Each
  instruction runs two string switches. If the lists drift, the only check is `Step`'s default case, at run time
  (`runtime-coverage-inconsistency`), and no test reaches it. About 120 of `Step`'s labels are `u004xxxxx` aliases
  paired with a semantic name. Since 2026-11-10 executability is resolved once per opcode, so a step no longer
  allocates. Done when one table maps each opcode to its handler; coverage is derived from that table, with no hand
  list and no regex; and a test over every ABI table fails on an implemented opcode without a handler, or on a
  handler bound to an unknown name. Fast-forward pacing is calibrated per op (`PaceFastForwardOp`), so re-time it
  after the change.
- **Scene reset clears fields one by one.** `ResetSceneContextForRootReload` (`VirtualMachine.cs:1085-1154`)
  assigns 38 fields from many partials, and `GodotAdvHost.ResetSceneContext` (`GodotAdvHost.PresentationInput.cs`)
  does the same on the host. Scene state added without a line in these methods survives タイトルに戻る. That is how
  the host's dialog events stayed signalled until 2026-11-10, when each was reset by hand. Done when a root reload
  rebuilds scene-lifetime state instead of clearing it, so a new field cannot be missed, and a test shows a reload
  keeps no scene state from before it.
- **Two instruction loops.** `RunFrame` (`VirtualMachine.cs:1184`) and `RunWaitCallback` (`:1678`) each run the
  per-instruction services (`ServiceInstructionBoundary`, `ApplyIgnoredDwords`, `FrameYield`, `PaceFastForwardOp`),
  and each maps the `int.MinValue + n` step sentinels (`HALT`, `FRAME_RETURN`, `HOTSPOT_RETURN`, `ROOT_RELOAD`) to
  outcomes. Until 2026-11-10 the callback loop ignored a halt and skipped those services. Done when one loop serves
  frames and callbacks, and a step returns a typed result instead of a sentinel int.
- **No single error path.** The VM reports a fault in several ways: 57 `HaltReason` assignments, `VmHaltException`,
  the native error dialog (`ShowScriptError`), host warnings (`ReportWarning`), and exceptions that end the VM
  thread. The mod linker, the OMS compiler and the CLI each keep their own catch filters. Three of the review's bugs
  were a fault that matched no filter: a halt inside a callback, `ModPackageException` at launch, and a drive command
  that threw. All three are fixed. Done when each layer has one documented fault type that its filters name, and the
  VM turns any fault raised during a step into a halt, with a reason and a trace event, in one place.
- **VM state has no owners.** Any of the 27 files can write any field. The skip and auto-message flags are the most
  shared: `_messageSkipServiceActive` is used in 7 files, and `_messageSkipInjected` and `_advTextWaitPolling` in 5
  each. Integer-cell normalisation (under Robustness) and the bounds checks on script-controlled indices also have
  no single home. Done when each group of state is private to one type that others change through named methods:
  memory banks, frame stack, skip and auto, input, audio, text, menu, play clock, saves. Moving the skip and input
  group last gives its callers time to settle what they need.
- **Revision behaviour is one boolean.** `EngineRevisions.IsSys4433` (in `Text/`) selects behaviour at five sites:
  `Sys4RegIniStore`, `AgeGlyphMaskCompositor`, `RubyLayoutRules` (two) and the op-`0x215` rule
  (`VirtualMachine.cs:214`). The revision-diff triage (under Engine fidelity) will add rules, and a third revision
  cannot be expressed as a boolean. Done when one traits record per ABI, outside `Text/`, describes each revision,
  and each site reads the trait it needs.
- **The host's threads have no stated ownership.** `GodotAdvHost` holds nine lock objects, five `AutoResetEvent`s,
  a `ManualResetEventSlim` and a `SemaphoreSlim`, and nothing documents which fields each lock guards or the lock
  order. `_slotDims` was written from both threads without a lock until 2026-11-10; it is a `ConcurrentDictionary`
  now. The host's 29 `_main.CallDeferred("Name", …)` calls name `Main` methods by string, so a rename fails when the
  game runs, not when it builds. Done when each lock's fields and the lock order are written down (or no lock is
  taken under another), and calls to the main thread are typed. A typed dispatcher must keep `CallDeferred`'s timing;
  check that on a drive scenario.
- **Modal dialogs are separate fields.** `Main` keeps the 終了 dialog (`_exitConfirmation`), the script error box,
  the options panel, the screenshot folder dialog and the full-width editor in their own fields, plus a hold
  counter (`_frontendModalHolds`). Input routing, the drive `dialogs` and `answer` steps and screen-mode changes each
  test the fields: they are used in 7 files. On the host, each dialog is an event plus a result field that the
  scene reset must clear by hand (the item above). Done when one owner knows which modal is open, routing and the
  drive steps ask it, and a reset cancels any pending dialog request.
- **`Main` has no seams.** `_Ready` is 542 lines (`Main.cs:76-617`) of flag parsing, session building and wiring, and
  12 files read `_screenWidth`. Launch-flag parsing has its own item under Robustness. Done when startup, the frame
  pipeline and each front-end concern (audio, movies, compositing, the shell, input, driving, screenshots) own their
  fields; `_Process` calls them in an explicit order rather than Godot's node order; and the screen geometry is one
  shared object.
- **The render traversal is written three times.** `Main.Compositor.cs` has a GPU path (`TryRecompositeGpu` and
  its range helpers) and a software path (`RecompositeSoftware`) over the same retained objects, and
  `Sys4/RetainedSurfaceRasterizer.cs` walks them a third time. A rule fixed in one can be missed in the others. Done
  when one Godot-free traversal drives both the GPU and the software output, and the rasterizer either shares it or
  documents why its rules differ.
- **Normal play still uses the software compositor.** `TryRecompositeGpu` (`godot/Main.Compositor.cs`) hands the
  frame to `RecompositeSoftware` in three cases:
  - A whole-screen transition runs (`_host.HasScreenTransition`): AGE's blocking transitions between two complete
    frames, which are the black and white fades, `0x25`'s crossfade and the `0x27`/`0x28` strip reveals. Kamidori's
    title fades to and from Game Start are among them. Native (mode 4) flattens the target frame into one surface
    and blends it over the captured source frame (`CompositeFlattenedTransitionTarget`). Fading each object on the
    GPU would dim the frame and let layers show through each other, so the GPU path needs an offscreen target it
    does not have.
  - An object has an additive LERP tint: there is no shader variant, and neither game has shown one.
  - `--gfx-log` or `--timeline-log` is on.

  So every rule and cache lives twice. Until 2026-11-11 a language switch cleared the GPU's textures but not the
  software path's `_pixelCache`, so fades showed the old language's images. Done when the GPU path draws screen
  transitions through an offscreen texture (a `SubViewport`) blended over the captured source and has the additive
  LERP-tint variant, and the software path runs only when chosen (`--render-backend software`, the two logs) as the
  reference. A driven fade's GPU frames must match the software path's within a stated tolerance.
- **The Godot front end has no unit tests.** `godot/` is tested only by `--selftest` (`Main.SelfTest.cs`), which runs
  the host with `synchronizeExplicitPresentation: false` (`Main.cs:521`). `GodotAdvHost.PresentationInput.cs` (the
  waits and input) uses no Godot API, but sits in the Godot project, which no test project references. Done when the
  host's VM-thread side and the other Godot-free front-end code live in a library with tests (`Ome.Engine.Frontend`
  is the existing one).

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
- **Two mods' unpinned fragment locals share slots.** A fragment's unpinned `local` takes the first slot above the
  base's highest (`OmsCompiler` patch mode, `UsePatchBase`), and the applier keeps each plan's slots as compiled
  (`OmsPatchApplier`), so two mods patching one script get the same slots. If one mod's code runs while the other's
  local is live (an inserted block between the other's set and use, or a call into it), they overwrite each other,
  and no conflict is reported. Spec §10.2 does not cover it. Done when the design picks a rule (the applier
  relocates each plan's unpinned locals above the previous plan's, or the linker reports overlapping live ranges)
  and a two-mod test shows it.
- **Accepted future work** in the mod loader design (`superpowers/specs/2026-11-03-mod-loader-design.md` §12):
  - a launcher;
  - runtime scripting (Lua): design drafted, see the next item;
  - mod-owned sites (patching another mod's inserted code);
  - declarative data patches;
  - mod merging;
  - precompiled new scripts.
- **Runtime scripting (Lua): design draft under review.** The port runs no mod code of its own: mods change
  behaviour only through bytecode (patches and scripts). The roadmap plans Lua beside the VM
  (`remake-architecture-and-roadmap.md` §3.8, and §3.10 step 7 for the runtime boundary and the spike).
  - **Draft (2026-11-10, not accepted):** `lua-scripting-design-2026-11-10.md` at the workspace root, outside this
    repository. It covers the runtime choice, hooks declared in the manifest, the engine API (`maid.vm` and the
    rest), per-game base mods that hold every game-specific name, mod state beside the save, and the validation
    gates. Its §19 lists the questions still open.
  - It raises the package manifest to `omp = 2`, as the localization design does
    (`superpowers/specs/2026-11-10-localization-design.md` §9.2). Localization milestone 4 introduced version 2
    (2026-11-11), so the draft's manifest tables join it, unless a released version 2 calls for a version 3.
  - Packages that target no profile are decided (2026-11-10, localization design §11.1). The draft still has to
    say what such a package may hold for Lua.

  Done when the draft is accepted (moved to `superpowers/specs/2026-11-10-lua-scripting-design.md` with its row in
  the canonical documents map) and this item is replaced by its milestones. The first of those is the runtime
  spike (draft §5).

## Localization

The localization framework is built: milestones 0 to 6 of `superpowers/specs/2026-11-10-localization-design.md`, with
its eleven gates, and `align`, `fit` and the overflow report (§7.2, §7.3), which no milestone held (2026-11-11).
Modders' guide: `modding/languages.md`; the import test's results: `localization-import-test.md`. Nothing is open;
the future work spec §17 lists stays in the spec.

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

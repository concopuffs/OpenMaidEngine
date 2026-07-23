# Phase B Framework — Natural Boot to First Gameplay

Phase B broadens the proven ADV vertical slice into a naturally booted, stateful play session and then into
the first narrow gameplay loop. This document is a sequencing framework, not a task-level implementation
plan. Phase A remains active until SC0000 meets its completion criteria.

The architectural preference is:

```
complete SC0000
  -> persistent session and scene coordinator
  -> faithful system/data boot
  -> title and New Game happy path
  -> SC0000 under naturally initialized state
  -> natural first-dungeon transition
  -> bounded first-dungeon gameplay slice
```

This order makes gameplay failures attributable to gameplay rather than to missing boot state, discarded
globals, or manually inherited host state.

## Entry criteria from Phase A

Phase B may begin when SC0000 is a reliable presentation baseline:

- A normal windowed playthrough is visually and audibly coherent from entry to natural exit.
- Reproducible presentation inconsistencies have been resolved or explicitly classified with evidence.
- Every SC0000-executed effectful opcode is implemented, or its lack of a host-visible effect is supported
  by native/script evidence. Remaining unrelated corpus gaps do not block entry.
- The run no longer relies on unexplained timing, input, layer, or resource workarounds.
- Automated VM/host regressions and a repeatable manual playthrough form the acceptance baseline.
- SC0000's terminal global state and control-flow boundary can be captured for comparison once natural
  scene chaining exists.

The Phase A implementation/result history remains in `docs/phase-a-slice-plan.md`.

## Principles

1. **State correctness before systems breadth.** Dungeon logic depends on initialized unit, item, skill,
   progression, configuration, and heroine state. Establish their natural producers before debugging their
   consumers.
2. **Follow script control flow.** The bytecode owns game rules. Implement the effectful operations and
   lifecycle services it calls; do not replace dungeon/combat logic with a parallel rules engine.
3. **One natural path first.** Boot → title → New Game → SC0000 → first dungeon is the initial spine.
   Alternate menu branches and broad gameplay coverage grow from it later.
4. **Persistent session, replaceable scenes.** Globals and game/profile state survive scene changes while
   script frames, retained presentation state, and scene-owned resources observe proven lifecycle rules.
5. **Demand-driven opcode work.** Investigate an unknown opcode when the chosen path executes it or evidence
   connects it to a reproduced defect.
6. **Bound every slice by an observable transition.** Each stage starts from a known state and ends at a
   visible screen, input boundary, scene handoff, or gameplay action.

## Stage B0 — Ground-truth reconnaissance

Before changing runtime architecture, record the original game's path from process start through the first
meaningful dungeon interaction. The goal is an answer key, not exhaustive reverse engineering.

Capture:

- Script/load order across system boot, title, New Game, SC0000, and first dungeon entry.
- Which initialization scripts run and which global banks or native/profile values they establish.
- Retained graphics/audio state that survives each boundary.
- The title selection and New Game dispatch path.
- SC0000's natural terminal decision and the corresponding next loaded script.
- The first dungeon's executed opcode/call-script families, assets, and obvious state dependencies.

Detailed progression semantics remain canonical in `docs/scjump-progression.md`; native loader findings
belong in `docs/engine-re.md` and `docs/name-resolution.md`.

### Initial B0 result (2026-07-20)

Static SYSTEM4/INIT2 control flow plus an existing native opcode trace establishes the first natural spine:

`SYSTEM4 → config load/init → INIT2 (+23 nested data initializers, then TUNE) → optional LOGO/OP
→ INIT → TITLE → GAMESTART → UNITECH/CALCARR → TUNE → TITLE return → SYSTEM4 → SC0000`.

SYSTEM4, not an opaque native dispatcher, is the long-lived scene coordinator. It maps the SCJUMP decision
through a global resource-id table, places the result in `G[0x699]`, and uses computed `call-script`; the
initial zero decision falls back to raw SYS4INI id `0x22`, `SC0000.BIN`. The current VM already supports
computed nested call-script frames. Consequently B1 should preserve one VM and host rooted at SYSTEM4,
letting script-owned setup/cleanup surround child scenes, rather than invent an out-of-band replacement
protocol. Full process-start observation remains useful for profile/default and retained host-state evidence,
but is no longer needed to guess the script coordinator architecture.

The current headless C# runner already follows this root naturally: one SYSTEM4 run entered INITCONFIG,
INIT2 and all 23 of its data-initializer children, TUNE, INIT, and TITLE (28 nested script calls total), then
remained in TITLE's input-poll loop because the diagnostic host supplies no user input. Direct opcode
coverage is 100% for all 23 data initializers, CALCARR, and TUNE; the remaining direct coverage is SYSTEM4
64/82, INIT2 9/12, TITLE 61/65, GAMESTART 43/47, and UNITECH 29/31. B0/B1 should therefore make the
SYSTEM4-rooted path visible and interactive in Godot, then investigate only the gaps actually reached on
that route instead of treating every static gap as a prerequisite.

**Godot root landing (2026-07-20).** The no-argument Godot/run-godot path now starts SYSTEM4 directly and
does not apply the direct-SC0000 layout/surface bootstrap or the diagnostic `--boot` prefix. A windowed run
reaches and renders TITLE using SYSTEM4-owned retained state. A real-script integration test drives TITLE's
Game Start input, GAMESTART's release-gated default selection, and proves the same VM enters SC0000 through
SYSTEM4's computed resource id `G[0x699]=0x22`; `G[0]=1` and the script-produced ADV-chrome flag
`G[0x6c1]=1` are present at that boundary. `--scene SC0000 --boot` remains available only as the explicit
single-scene diagnostic harness. This lands the boot/title/New Game entry half of B1–B3; proving a completed
scene return plus boundary cleanup still belongs to B1 completion.

**TITLE SFX packed-raw correction (2026-07-20).** Hover and activation callbacks were already executing their
scripted `0xb5` starts. The load failed earlier because op `0xb4` uses universal packed SYS4INI/AAI ids,
while Godot treated them as active-script manifest ids. The packed resolver maps TITLE's
`0x2aea`/`SE020.WAV` hover, `0x3321`/`SE015.WAV` activation, and GAMESTART's `0x2aeb`/`SE013.WAV` cancel
through the existing channel players. A synchronized TITLE→GAMESTART→TITLE trace records every load/start
with its filename, and manual validation confirms they are audible; BGM remains unaffected.

**Pre-title video sequence implemented (2026-07-20).** SYSTEM4 already owns the native sequence; the
port did not lose an executable-side launcher. Its sole op `0x130` call returns an engine initial-root flag
that is one at context construction and cleared only when op `0x9` resets/reloads root script id zero.
SYSTEM4 calls `LOGO.BIN` and `OP.BIN` only while that flag is nonzero. The former stubbed-zero output
explained the direct jump to TITLE. LOGO and OP then use the modal movie op
`0x20f` with packed catalog movies `0x335f`/`LOGO.AGF` and `0x3364`/`OP.AGF`; existing `0x236` is the distinct
non-modal movie-to-surface path and uses the same packed lookup. The VM now models the initial-root flag and clears
it at the op-`0x9` whole-stack root-reload boundary. Godot resolves a typed MPEG asset, reuses the asynchronous decoder
and retained compositor, and parks the VM until EOF or mouse/Accept/Cancel input. Focused natural-boot tests
prove `LOGO -> OP -> INIT -> TITLE` ordering and exact movie operands. MPEG audio remains explicitly deferred
until the decoder abstraction has an engine-owned synchronized audio/volume contract.

## Stage B1 — Persistent session and scene coordinator

Replace the single-SC0000-root assumption with an application-owned session that runs SYSTEM4 as its root.
SYSTEM4's computed `call-script` is the authoritative scene coordinator: child scenes return to that frame,
while globals, the host, and intentional retained state remain owned by the same live VM session.

**Root-reload boundary implemented (2026-07-20).** Ordinary op `0x2` child exits still return to their
calling SYSTEM4 frame. Op `0x9` is the distinct native reset path: it discards the complete active script
stack, clears scene-owned graphics/input/ADV state, cancels deferred SFX starts while preserving active
audio, preserves global banks and process-owned host state, and starts raw script resource zero
(`SYSTEM4.BIN`) at offset zero. The implementation propagates the boundary
through nested calls without running any caller continuation and records the new root frame with
`FrameCause.RootReload`. Native RE and the one intentional history-lifetime exception are documented in
`docs/engine-re.md`; the history backlog remains preserved until its ownership is proven rather than guessed.

Required responsibilities:

- Own global integer/string banks and any proven external/profile state across scenes.
- Preserve the SYSTEM4 root while distinguishing ordinary child frames from scene-boundary children for
  diagnostics and lifecycle assertions; do not perform host-driven top-level replacement.
- Define scene-owned versus session-owned host state and tear each down at the correct boundary.
- Preserve intentional system-owned surfaces, configuration, and audio while releasing scene-local state.
- Expose deterministic transition evidence: outgoing scene, reason/decision, incoming scene, and state
  summary suitable for tests.

Completion evidence now present: SYSTEM4 reaches computed child scripts in one VM; ordinary children return
to SYSTEM4; op `0x9` performs a tested whole-stack reload of SYSTEM4; selected globals and process-owned state
survive; and scene-owned presentation/input state is released. Manual validation of a natural gameplay
route through the first `0x9` remains deferred: Himegari's readily accessible return-to-title choice belongs
to the still-unimplemented frontend exit-request policy, while the other known natural paths require later
gameplay, game over, or completion. Do not use TITLE's post-`0x1` developer menu as evidence; native `0x1`
is non-returning, and the port now propagates that exit request instead of falling through into the hidden
bytecode. The opt-in `--native-debug-menu` diagnostic deliberately restores fall-through for exploring that
retained developer UI, but does not qualify as native lifecycle validation. See `docs/engine-re.md`.

**Godot debug scene launcher (2026-07-20; implemented and manually validated).** The first version
is deliberately narrower than arbitrary hot swapping:

- Expose an F4-style Godot overlay only while `TITLE.BIN` is the persistent VM's active SYSTEM4 child.
- Resolve the chosen `.BIN` through the existing SYS4 catalog, then ask the VM to return the current TITLE
  child frame with the game-authored coordinator writes (`G[0]=1`, `G[0xaba5c]=-1`, `G[0x62ccf]=0`, and
  selected packed id in `G[0x699]`) applied on the VM thread.
- Let SYSTEM4 resume at `0x2b0` and execute its real entry wrapper and computed `call-script`; do not replace
  the VM root or call the selected scene directly from Godot.
- Disable switching while another scene is active. That scene must reach its own terminal cleanup and then
  either return through SYSTEM4's post-child cleanup or execute its genuine op `0x9`. A separate clean
  relaunch remains the escape hatch for a stuck/incomplete scene.

The runtime now has a generic debug-only "return this exact active child frame with queued global writes"
request, thread-safe frame-generation/stack reporting, and the distinct `DebugReturned` trace outcome. TITLE
does not park in ADV op `0x72`: its visible menu continuously polls input and executes a 1 ms op-`0xc8` sleep
at `TITLE@0xe5`. The request therefore targets the observed active frame generation and is consumed by the
VM thread at its next completed opcode boundary, before another TITLE opcode can execute. `SignalInput` is
used only if the target happens to be in a real ADV wait, avoiding a stale signal that could advance the
selected child. Synthetic coordinator tests cover both an ADV wait and TITLE's sleep/poll shape, selected-
child dispatch, ordinary SYSTEM4 continuation, stale/ineligible request rejection, and selected-child
op-`0x9` whole-stack propagation.

This launcher would provide the real visible TITLE→selected scene sequence and preserve the coordinator
boundary, but it cannot manufacture valid late-game state. The current direct harness and opcode coverage
suggest early ADV scenes and `DEBUG.BIN` are plausible targets; later scenarios, GAMECLEAR, battle/map, and
profile-dependent scripts may still require progression data or missing opcodes. A startup-only/direct-scene
selector is cheaper, but it is merely a UI for `--scene ... --boot` and provides no transition-lifecycle
evidence. An unrestricted in-process switch would additionally require VM cancellation, task joining,
movie/audio disposal, locator/trace regeneration, and an explicit global-state policy, so it is not a quick
or trustworthy first version.

**Menu population and selection contract.** The runtime SYS4 catalog—not `build/` inventory—is the
source of truth. Himegari currently has 481 unique base-catalog `.BIN` records: 136 `SC####`, 164 `SP*`, 8
`DEBUG*`, 29 initializer-named scripts, and 144 other named scripts. Each menu row keeps the packed resource
id as its identity and carries display name, pack selector, raw index, archive, size, and category; names are
labels rather than keys so future append-pack collisions remain representable. Population should enumerate
base `Catalog.Files` plus every mounted append catalog, exclude placeholders/non-BIN records, and compute
`packed_id = (pack_id << 24) | raw_index` without parsing all scripts up front. The selected script is decoded
and validated only when Launch is pressed; an unsupported decode reports an error and leaves TITLE running.
The currently mounted append pack contributes 39 additional `.BIN` records, so the shipped launcher smoke
test sees 520 distinct packed script ids.

The initial UI groups entries rather than implying every BIN is a standalone scene:

- **Scenario:** `SC####.BIN`, naturally sorted by number.
- **Secondary/event:** `SP*.BIN`, naturally sorted by name and suffix.
- **Debug:** `DEBUG*.BIN`.
- **Other/expert:** every remaining script; the separate **All** filter includes every category. Initializers,
  callbacks, data routines, and modal UI scripts may require caller-owned state and may immediately return
  or corrupt the live session.

`SYSTEM4.BIN` and `TITLE.BIN` are not launchable in the first version; recursively dispatching either through
SYSTEM4's child slot is not a scene test. Search is case-insensitive over name and hexadecimal/decimal packed
id. The detail pane shows name, category, packed/raw id, archive, size, and the fixed warning that launch uses
the current live global/profile state. Compatibility or opcode-gap badges are deferred until coverage logic
has an engine-owned runtime API; the menu must not parse generated Markdown or call Python tooling.

The implementation should leave one explicit extension point for future test sequences:
`DebugLaunchPreset(label, packed_script_id, extra_global_writes, note)`. Catalog rows use only the four
coordinator writes above; profile-authored presets may later add proven story/progression globals without
turning the menu into a free-form state editor or save backend. Arbitrary PC/offset jumps are out of scope.

**Implementation order.** (1) Add catalog script-entry enumeration with packed ids and unit coverage for
base/append mounts, placeholders, duplicate names across packs, and category/sort/filter behavior. (2) Add a
generic VM debug request targeted at an exact active frame generation; it applies an immutable set of global
writes on the VM thread and returns that child at the next opcode boundary. Test SYSTEM4→TITLE→selected child,
ordinary child return/cleanup continuation, op-`0x9` propagation, TITLE's sleep/poll loop, and stale/ineligible
request rejection. (3) Add the Godot F4 overlay (`PopupPanel`, search/category controls, `ItemList`, detail
pane, Launch/Cancel), consume all overlay input, and enable Launch only for the active `SYSTEM4 > TITLE`
stack. (4) Add a Godot smoke test for catalog population and request wiring, then
manually validate `TITLE -> DEBUG -> 0x9 -> SYSTEM4 -> TITLE` before expanding the selectable categories or
adding presets.

Steps 1–4 are complete. F4 opens the Godot `PopupPanel` only for the exact active
`SYSTEM4.BIN > TITLE.BIN` stack; search, category filters, packed-id metadata, guarded Launch, and Cancel are
live. `SYSTEM4.BIN` and `TITLE.BIN` remain visible but unlaunchable. While the panel is open, AGE gameplay
input is not forwarded. Launch reparses the selected packed id before queuing any writes. The threaded Godot
selftest constructs the catalog and panel and currently reports 520 unique packed scripts. Manual validation
confirmed `TITLE -> F4 -> DEBUG.BIN`: its four scripted ADV pages at `0xc7`, `0x110`, `0x17b`, and `0x1ed`
were presented, its terminal op `0x9` at `0x1fb` ran, and SYSTEM4 reconstructed the visible TITLE menu. No
launcher/session-lifecycle discrepancy was observed. DEBUG-specific content oddities are not acceptance
failures for this developer route and remain out of scope unless they reproduce in a normal game script.

**TITLE Extra Room entry (`ROOM.BIN`, 2026-07-20 through 2026-07-21).** The title dispatch itself is correct. The first manual
entry exposed a scheduler discrepancy: ROOM's stable input poll uses `sleep 0`, which native `0xc8` clamps
to a one-millisecond timer. Godot previously treated it as a zero-duration no-op, letting the script consume
the 20-million-step guard and display `-end-`. The interactive host now preserves the native one-tick yield;
Godot also prints the VM halt reason and step count before its generic end marker so future terminations are
distinguishable. Manual follow-up confirmed the room remains interactive and its genuine op-`0x9` returns to
TITLE, then exposed three presentation gaps. Native RE resolved and implemented all three underlying
contracts: `0x60` selects the four room variants through CRT `rand()%4`; corrected `0x6c` zero-fills the
13-cell character-voice profile table instead of writing scalar 13 into its first cell; and `0x25` performs
a blocking target-over-source crossfade between the two frames ROOM captures through offscreen `0x20c`
presents. Entry/menu transitions use timing argument 10, the final black transition uses 30, and the port
now waits for the terminal captured frame before surface release or SYSTEM4 reload. The release script's
fourth random presentation variant intentionally has no voice ids; the first three have distinct
greeting/farewell pairs. ROOM now has 55/56 distinct opcodes and 420/421 instructions handled or proven
safe; its sole remaining gap is the already classified one-shot `0x1fe` current-rotation setter on a
decorative object, unrelated to these transition/audio paths. Manual visual/audio parity remains to be
rechecked on the updated build. The first recheck confirmed the transition was visible but exposed two
follow-up corrections: the decompiled alpha-step branch had initially been read backwards (`arg<=64`
means step 16, not step 1), making argument 10 about 160 ms and argument 30 about 480 ms; and ROOM's raw
voice ids were reaching `play-voice` but failing the port's SC-section-only lookup because ROOM owns no SC
section. The initial compatibility fix added a raw fallback, but later native RE proved the broader rule:
voice and frontend texture operands are already universal packed catalog ids and never receive a scene base.

**Natural Game Start diagnostic gate (2026-07-21; captured).** The opt-in `-StartupDiagnostics` route kept
the persistent `SYSTEM4` root and native exit semantics and added no seeds, boot prefix, timing changes, or
input automation. A user-driven cold boot traversed the complete initialization family, `LOGO`/`LOGO.AGF`,
`OP`/`OP.AGF`, `INIT`, `TITLE`, `GAMESTART`, its nested unit-data setup, and finally raw script `0x22` /
`SC0000`. It stopped at the first stable wait, `SC0000@0x83c`, whose preceding text instruction is
`SC0000@0x834` (`――かつて、戦いがあった。`). The 97,730-event timeline contains 96,908 opcode steps and
129 distinct opcodes; the Godot log has no warnings/errors, both movies start and stop normally, no audio
resource is unresolved, and no VM halt occurs.

The 137 fallback events are not a single boot blocker. Most are declaration/statement/line markers already
proven safe, or deliberately deferred profile/read-text operations (`0x1a2`, `0x1a3`, `0x1cb`). The reached
effectful unknowns divide into SYSTEM4 layout setup and unit-data initialization. Native follow-up identifies
`0x194` as a string-equality predicate reached in `INIT2` and `GAMESTART`, and `0x1b0` as a dword-block copy
paired with still-unresolved pointer-preparation opcode `0x63` in `UNITECH`/`CALCCC`. `0x194` is now
implemented for every supported SYS4 string operand form, with focused equality/inequality and
compare-then-branch regressions. A step-traced natural-boot test reaches SC0000 without a `0x194` fallback;
static coverage now reports 10/12 INIT2 opcodes and 44/47 GAMESTART opcodes handled, with the remaining
GAMESTART profile operations still deliberately deferred. The reached `0x63`/`0x1b0` unit-data pair is
also implemented: native `0x63` aliases a typed backing-cell address into a pointer, while `0x1b0` copies a
counted dword span through direct or pointer endpoints. The traced natural boot reaches both without
fallback; UNITECH is now 31/31 handled and CALCCC 14/15, with only deferred profile op `0x1a2` remaining.
SYSTEM4's paired `0x79`/`0x1c1` setup is now natively resolved and implemented: `0x79` configures the
cursor restored by later layout resets, while `0x1c1` configures layout-local right/bottom overflow
boundaries. `AdvTextHistory`, VM dispatch, the direct-scene SYSTEM4 bootstrap, and ordinary/history Godot
label geometry now share that script-owned state; the former slot-1 hardcoding is removed. The next
reached SYSTEM4 cluster is also resolved: `0xfe` establishes ten logical input actions, while `0x107`,
`0x10b`, and `0x10c` configure joystick-button, mouse-button, and DIK keyboard mappings consumed by
`0xff`/`0x100`. The port now owns the native seven-action defaults, executes all four setters, translates
Godot physical key/mouse/joy events through the resulting map, scans only actions below the configured
count, and invokes callback slot `count` for the empty-mask release path. Direct-scene diagnostics replay
SYSTEM4's same 16 immediate configuration calls. This is independent of profile/save ownership. Closing
Godot currently releases a parked ADV wait before process teardown, so the page map may contain one trailing
shutdown-only page; the final timeline `input-wait` is the authoritative stop.

**SC0000 character-menu roster discrepancy (2026-07-21; investigated).** A natural-boot state probe at
SC0000 entry shows that GAMESTART/UNITECH already created party slot 2 with flags `0x13`, character id 2,
and selected slot 2. The empty Character Info page is therefore not missing boot data. CHMENU gathers that
active slot, constructs two 100-cell sort-key arrays, calls opcode `0x12f` at `0x1c9b`, and reads the
populated tail of the resulting index permutation. The port had been falling through `0x12f`, leaving the
output zero-filled and making CHMENU select empty slot zero. Native RE fully identifies `0x12f` as a stable
ascending index sort by the signed sum of two key arrays. The generic opcode is now implemented with native
32-bit overflow and stable ordering. Focused regressions cover equal keys, overflow, and zero count; a
real-script regression carries the naturally booted state into CHMENU and proves the first roster sort keeps
slot 2 selected with no `0x12f` fallback. This uses no seed or menu-specific injection. Remaining acceptance
is manual: open Character Info at the first stable SC0000 page and confirm the initial character is visible.

## Stage B2 — Faithful full boot

Replace `--boot`'s diagnostic seeding and separately injected inherited surfaces with normal boot execution.
First prove the installed game's actual ordering; do not assume the current helper lists are complete.

The boot path must cover two existing categories:

- System/session initialization currently approximated by `INITCONFIG`, `INIT2`, and `INIT`, including
  host-visible side effects that `CaptureHost` discards.
- Game-data initialization represented by the `*INIT` family used by the headless boot/session tools.

Completion evidence:

- A fresh application reaches the same initial title state without `--boot`, `--seed`, or manual surface
  injection.
- Required globals come from executed scripts or clearly identified profile/native defaults.
- Inherited retained state has a traceable owner and lifecycle.
- A boot snapshot is reproducible for tests, but the shipped path performs the real boot rather than loading
  a developer snapshot.

This stage may reveal platform/install-selection work; track that separately in
`docs/platform-portability.md` rather than folding cross-platform export into Phase B.

## Stage B3 — Title and New Game happy path

Implement only enough menu behavior to choose New Game naturally and enter the story. This is primarily a
state-initialization and dispatch slice, not a mandate to complete every submenu.

In scope:

- Title/main-menu presentation required by the executed path.
- Keyboard/mouse selection and the menu-specific coroutine/hotspot forms actually reached.
- Configuration defaults that affect New Game or the subsequent runtime.
- New Game initialization and its transition request.
- Natural empty-save-state behavior when no saves exist.

Deferred to bounded follow-ups unless the happy path requires them:

- Full configuration UI and every setting.
- Load/save implementation and save-format reversal.
- Extras, galleries, replay modes, and unrelated submenus.
- Menu visual polish that does not obstruct correct selection or state production.

Completion evidence: launching the application, selecting New Game, and reaching SC0000 with no manual
state seeds. The resulting SC0000 opening state must match the Phase A visual/audio baseline.

## Stage B4 — Natural progression through SC0000

Run the completed scene inside the persistent session and honor its real terminal transition. This stage
closes the currently unidentified decision-to-scene boundary described in `docs/scjump-progression.md`.

Completion evidence:

- Title/New Game reaches SC0000 through actual script/native dispatch.
- SC0000 completes without the developer auto-advance harness.
- The outgoing decision, selected next script, and persistent global changes agree with the original run.
- The first dungeon scene starts without reconstructing the VM or reseeding state.

## Stage B5 — First-dungeon vertical slice

Do not scope “gameplay” as maps + units + items + magic + combat + AI + win/loss all at once. After B0
identifies the first real interaction, select the smallest end-to-end loop that exercises authoritative
script state. A likely target is:

- Load and render the first map and its initial UI.
- Populate the units required for the opening state.
- Select one unit and display its relevant state.
- Perform one legal move or scripted action.
- Resolve one combat or event interaction if the natural opening reaches one.
- End one player action/turn and return to a stable input boundary.

The exact acceptance path must follow the installed game's first dungeon rather than forcing this example
shape. Items, skills, magic, AI, win/loss, deployment, and progression are added only as the selected path
requires them.

Completion evidence combines original-game observation, executed-opcode/call traces, visible map/UI output,
and before/after global-state comparisons for the action.

### DEBUGMAP field-entry result (2026-07-21; manually validated)

The shipped `DEBUGMAP.BIN` path is useful for the first bounded field slice, but it is not treated as a
replacement for the natural campaign path. It performs substantial script-authored setup itself: it creates
the test units, writes stage id `0xa5` to `G[0x4dfbc]`, fills the field-mode globals, writes system-flow
request `G[0]=3`, and returns so SYSTEM4 enters `FIELD.BIN`. Runs launched from TITLE also retain the real
SYSTEM4/INIT tables. A future discrepancy in party, inventory, stage, or progression state may still be an
unpublished debug-level prerequisite; do not invent a seed unless its missing producer is proven.

The first observed field discrepancy was a black central map while the surrounding field UI and minimap
input remained alive. `DRAWMAP.BIN` is 23/23 opcodes handled and `RENDERMAP.BIN` is 31/31. FIELD's missing
opcode `0x249` loads universal raw map sheets `0x32da..0x32dd`
(`SO005`/`SO007`/`SO008A`/`SO007A`) into surfaces `0x3e..0x41`, after which DRAWMAP binds its generated tile
objects to those surfaces. Skipping the loader left valid retained objects pointing at empty surfaces.

The first retest after adding `0x249` showed the complete SO005 sheet enlarged over a grey field. The native
mode-1 class is now fully identified as a large-image tiled wrapper over the same decoded pixels, ruling out
a special spritesheet or blend interpretation. Two independent presentation gaps caused the retest:

- native treats a zero-area draw-texture source rectangle as an empty draw; the port incorrectly expanded
  it to the entire source image, exposing FIELD's intentionally invisible SO005 prototype object;
- FIELD's camera depends on the shared retained-object range transform. Corrected `0x229` selects the map
  handle range and anchor (it is not a per-object position opcode), while newly implemented `0x22a`,
  `0x22c`, and `0x22d` apply immediate zoom, immediate translation, and animated zoom to that range without
  moving the surrounding UI.

The related native `0x22b`/`0x22e` range-rotation setters have zero Himegari corpus calls and need no runtime
implementation yet. The remaining `DRAWMINIMAP` gap is `0x207` (eight calls) and is confined to minimap
work; FIELD's other 14 static gaps do not produce the main terrain layer. Installed-asset decode,
range-isolation/animation, VM dispatch, full engine tests, and the threaded Godot selftest pass. Manual
acceptance confirms that DEBUGMAP now displays the dungeon map correctly; the earlier full-sheet overlay is
gone and the field presentation remains operational after the camera-transform correction.

Drag-panning exposes one bounded presentation follow-up. FIELD op `0x86` at `0x2e4d` selects raw cursor
`0x32ce` on pan entry and op `0x87` clears it on exit. That catalog entry is the installed 4-bpp color
`CURSOR09.CUR`, whereas the port originally decoded only the eight 1-bpp ADV cursors. This was a
decoder-format gap, not missing or EXE-embedded artwork. The CUR decoder now handles both installed 1-bpp
and 4-bpp formats with independent XOR/AND strides; archive-backed pixel/hotspot tests pass. Manual
acceptance confirms the gripped cursor now displays correctly during drag-panning.

A follow-up full-corpus analytics audit corrected a metadata artifact: ops `0x228`, `0x22f`, `0x231`,
`0x232`, `0x239`, and `0x23f` had detailed native RE but still retained opaque semantic names, while
`0x1a8` was an unnamed explicit no-op. Their semantic names are now current, and native proves `0x1a8`
is a structural marker whose handler only records instruction length. The same audit exposed a real runtime
gap, now closed: `0x23f` queries a loaded surface's DirectShow stop position in truncated integer
milliseconds (all 23 sites are associated with a preceding `0x236`). On a freshly opened graph the stop
position normally equals its duration; FIELD uses it for 16 ms animation scheduling. The port now queries
that value during synchronous movie-graph initialization and retains it per movie surface. Empty movie
slots return -1; unavailable timing metadata warns and returns -1 rather than exposing native's undefined
failure output.

The next visual comparison found all field-HUD numbers absent while their labels, bars, and source artwork
were present, plus apparently blank unit/weapon text. Native and script-side tracing resolves this into two
bounded opcode gaps rather than missing game state:

- `0x13a` registers one of 11 `(surface, atlas x/y, digit width/height)` styles, and `0x23b` expands a
  decimal value into retained per-digit objects with zero-pad/center/left/right layout flags. `DRAWCHP.BIN`
  contains eight style registrations and 22 numeric draws covering the visible turn/control/mana/level and
  HP/SP/FS fields. Skipping both opcodes precisely explained the blank values.
- `DRAWCHP.BIN` already reads and submits the unit and weapon strings to `0x204`, but its two preceding
  `0x1a6` centering calls were unimplemented. Native `0x1a6` returns `strlen(CP932_bytes) >> 1`; skipping it
  places the strings at x=257 on a 263-pixel scratch surface, so the existing compositor clips them.

The exact native contracts and addresses live in `docs/engine-re.md`; opcode-source metadata is in
`vm-map/opcodes.toml`. The shared implementation is now landed in the VM/retained-graphics model: the style
registry holds the 11 EngineCtx records, decimal glyphs become ordinary retained objects, and `0x1a6`
measures the configured native encoding (CP932 for SYS4). Focused coverage plus the full 278-test engine
suite, zero-warning Godot build, and threaded frontend selftest pass. The next field action is the manual
visual recheck of the same `DEBUGMAP` HUD before pursuing any state seeding.

That recheck confirms the numeric HUD and centered unit/weapon strings are restored. The next visible
discrepancy is shared menu text placed too far right, reproduced by both FIELD's three-choice wait/retreat
popup and TITLE's shipped developer menu. Both routes use `BUNKI.BIN`'s temporary-surface renderer rather
than the ordinary VN overlay. Native RE identifies the missing input as opcode `0x2c5`, raw byte-string
length: BUNKI uses it to size the panel and compute a common primary-label x origin. With the opcode skipped,
the destination stays zero; the FIELD popup shifts 53 pixels right and the longer developer menu incorrectly
remains at its 240-pixel minimum. Corrected native/port screenshots reveal a separate exact one-row vertical
shift: BUNKI uses missing opcode `0x195` (`string-not-equals`) to test its optional title against the empty
string. Because a skipped opcode leaves its destination untouched, the final test reuses a nonzero graphics
handle and falsely advances the choice cursor by 30 pixels. Both shared operations are now implemented with
focused CP932/NUL, operand-resolution, and stale-destination regressions. All 281 engine tests, the zero-warning
Godot build, and threaded selftest pass. The next bounded action is a visual recheck of the DEBUGMAP popup and
TITLE developer menu.

The subsequent DEBUGMAP deployment-picker comparison exposed a separate numeric renderer in
`DRAWENP.BIN`. Its red unit-information card draws labels and `/` separators through `0x204`, but draws
level, HP/SP/FS pairs, and both stat columns through 16 calls to opcode `0x205`. Native `0x205` formats a
fixed-width signed decimal field, applies zero-pad/alignment/half-width flags, adjusts the x anchor for
suppressed leading cells, and rasterizes the result into the same temporary surface using current text
style. The shared VM implementation now covers that path; focused tests use DRAWENP's exact level call and
the observed half-width, zero-padded, and full-width variants. DRAWENP is now 611/611 instructions handled;
all 284 engine tests, the zero-warning Godot build, and threaded frontend selftest pass. The manual
deployment-card comparison now passes.

### Post-DEBUGMAP script audit and next slice (2026-07-21)

The next slice should prove one complete **player attack and battle presentation** from the working
DEBUGMAP field: deploy a unit, enter its action flow, choose a reachable target, resolve one `BTL` exchange,
observe the HP/damage presentation settle, and return to an interactive `FIELD`. This is a stronger next
gate than expanding save/configuration or polishing isolated menus because the underlying gameplay scripts
are already much closer to complete than their presentation suggests:

- `CALCSCOPE`, `CALCARR`, `CALCBTPARAM`, `CALCREVISE`, `CALCOCC`, and `CALCDMG` are fully handled; so are
  `BTANINIT`, `SETEN`, `REMOVECH`, and the ordinary map renderers.
- `BTL` has 2,194 of 2,215 static instructions handled (including one safe no-op) and 78 of 85 distinct
  opcodes handled. Its remaining cluster is presentation-oriented: elapsed-time sampling (`0xd0`),
  frame-time sampling (`0x23c`), movie
  completion polling (`0x23a`), animation-service flags (`0x24e`), result-string construction (`0x193`),
  profile bookkeeping (`0x1a2`), and delayed combat voice playback (`0x2c0`).
- `SELACT` has only five calls to missing `0x191`, now proven to be signed absolute value. `ADDEXP` is
  similarly blocked mainly on string concatenation/decimal conversion plus two frame-time samples.
- FIELD itself has only 31 unhandled instructions out of 7,933. Save/load/profile and read-skip services
  account for several of those and need not block the first attack loop.

The implementation order inside that slice should be evidence-driven and checkpointed: first `0x191` plus
the battle clock/frame cluster (`0xd0`, `0x23c`, `0x23a`, `0x24e`), then `0x193`/`0x1c8` for battle and EXP
messages. The sole `0x2c0` site is now proven audio-only, so delayed combat voice can follow the visible
exchange rather than block it.

The existing Godot `FrameClock` is already the correct millisecond timebase, so this does not require a new
scheduler. Defer `0x1a2` profile persistence unless the attack trace proves its value affects same-session
combat state.

One independent low-risk cleanup is now precisely scoped but should not displace the attack gate: opcode
`0x207` is a clipped surface-to-surface rectangle copy. Its 15 corpus calls restore eight minimap marker
copies plus STATUS/READICON/DRAWTIP composition. It is a good first commit or visual checkpoint within the
next work period, but it does not unlock combat state. After the first player attack returns cleanly to
FIELD, the next decision gate is enemy-turn/end-turn behavior, then stage-clear/win-loss transition—not
save/profile breadth.

The subsequent manual movement check found and closed a prerequisite earlier in that path. `MVSEEK`'s three
effectful gaps (`0x132`-`0x134`) are native reset/enqueue/try-dequeue operations on one of 11 engine integer
FIFOs. With the trio stubbed, queue 0's packed-coordinate flood fill wrote only the origin and exited because
the dequeue-success local stayed zero. That produced the exact observed state: Wait remained available on
the current tile, but no blue range overlay or other clickable movement target existed. The VM now owns the
11 FIFO slots and implements all three operations. Focused service tests plus seeded executions of the real
`MVSEEK.BIN` and `ATSEEK.BIN` prove movement costs and attack distances expand to neighboring tiles; both
scripts are now 100% handled. All 298 engine tests, the zero-warning Godot build, threaded selftest, opcode
validation, and full-corpus decode validation pass. The manual movement/overlay recheck is the next acceptance
gate, followed by the pending one-attack return to FIELD.

That first recheck remained broken because the direct worker tests bypassed SYSTEM4's persistent service
frames. SYSTEM4 uses missing `0x06` to preload ATSEEK, SETROUTE, and MVSEEK into frame slots `0x1d`-`0x1f`;
FIELD uses missing `0x08` to invoke them. Native RE proves that `0x06` allocates a script in a selected slot
without running it, while `0x08` restarts that slot at codebase and returns to the caller without discarding
its local bank. Both are now implemented as reusable VM frames and cleared on root reload. A focused test
proves restart/local persistence, and a bridge regression drives the real MVSEEK through the exact
`0x06(0x3381,0x1f)` / `0x08(0x1f)` ABI. All 300 engine tests, opcode validation, the zero-warning Godot
build, and threaded selftest pass. Manual DEBUGMAP acceptance now confirms that the complete live path shows
reachable-tile overlays, accepts movement, and reaches working player combat. The next bounded work is to
investigate the concrete combat discrepancies found during that acceptance run.

### Player-attack runtime frontier implemented; combat reached (2026-07-21)

The selected attack-path opcode cluster is now implemented. `0x191` preserves native signed-32-bit absolute
value behavior (including `INT_MIN`); `0xd0` and `0x23c` sample the shared monotonic/frame clock; `0x23a`
queries live movie-surface completion; and `0x24e` retains animation-service flags, including bit 1's
suppression of `0x243`. `0x193` and `0x1c8` now build aliased battle/progression strings, while `0x2c0`
replaces and services one delayed combat-voice request on the Godot frame clock.

The independent `0x207` checkpoint also landed as a real mutable-surface path. Created textures own RGBA
buffers, static sources apply their load-time color key before copying, paired source/destination clipping
is platform-neutral, overlapping self-copies use stable source pixels, and the compositor resolves generated
surfaces by slot even though they have no asset resource id. This covers minimap markers and the shared
STATUS/READICON/DRAWTIP background composition family.

Static coverage after implementation is: SELACT 59/59 opcodes and 1,099/1,099 instructions; ADDEXP 45/45
opcodes handled (including its two proven safe markers) and 310/310 instructions; DRAWMINIMAP, STATUS,
READICON, and DRAWTIP are all 100%; BTL is 84/85 opcodes handled and 2,213/2,215 instructions handled when
its safe marker is included. BTL's only remaining gap is two `0x1a2` shared-profile bookkeeping writes.
Those remain intentionally deferred until save/profile ownership is implemented because neither write feeds
the current exchange.

Focused regressions cover exact dispatch, signed edge cases, string aliasing, clock rollover truncation,
movie polling, animation reset suppression, delayed voice operands, paired clipping, overlap, and colorkey
transparency. Manual DEBUGMAP acceptance reached player combat and exposed the next concrete frontier:
combat-effect movies do not play. The resolver/decoder diagnosis is canonical in
`docs/asset-resolution-re.md`; packed resolution is now corrected, with manual BTL validation and the
separate decoder decision remaining before enemy-turn/end-turn breadth.

**Universal packed resource resolver implemented; combat movie validation pending.** BTL's
`0x236@0x2b21` consumes universal packed MVB ids, exactly as texture, voice, script-load, and modal-movie
consumers do. Native Ghidra analysis shows that none of those paths applies an SC section base or fallback;
the port's former scene-first compatibility resolver was therefore generally wrong, not merely incomplete for BTL.
That explains each `movie unresolved BTL:...` warning and the secondary attempt to decode MPEG-backed
`MVB914.AGF` as a still image. A packed-catalog decoder probe also found a separate backend wall: the current
DirectShow graph handles `MVB914` (400x400) but rejects the reached 280x352 MVB001/MVB004/MVB955/MVB958
assets with `0x80040217`. Broader samples tie current compatibility to 16-aligned display widths, while 125
installed MVB assets use 280x352. Texture, voice, and both movie consumers now use universal packed
resolution; VM surface state and Godot caches retain the full selector. Focused tests prove SC0010's low
texture/voice ids and append-pack identity; 302 engine tests, a zero-warning Godot build, and threaded
selftest pass. Manual combat acceptance confirms the split: 400x400 MVB908 plays and completes, while
280x352 MVB961/MVB238 resolve correctly but DirectShow rejects their graph connection with `0x80040217`.
The user observed a stall after the sequence. Static BTL tracing proves failed `0x23f == -1` cannot create
an infinite callback count: the total horizon starts at `base_time + 1000`, and the explicit post-callback
loop polls `0x23a` until surfaces 7..10 are inactive. MVB908's stop log occurs after that loop. As a bounded
safety correction, decoder failure now becomes an explicitly completed zero-duration effect, while started
movies have a stop-time-based completion watchdog so a missing EOF cannot hold `0x21c` forever. **NEXT:**
replace DirectShow through the bounded FFmpeg plan below. The user confirmed that combat can still stall, but
that symptom remains tabled until the replacement removes DirectShow as a variable. Preserve created destination
dimensions and failed-movie identity through the backend change.

### Portable FFmpeg movie backend selected; bounded implementation plan (2026-07-21)

FFmpeg is selected as the single movie backend for stock content and future profiles. The exact native ABI,
dependency pin, corpus codec inventory, timing contract, and packaging rules live in
`docs/platform-portability.md`; this section owns execution order and acceptance.

1. **Completed — land the seam without changing behavior.** Add `IMovieDecoder` plus an injected factory, type
   `MovieRuntime` against the interface, and adapt DirectShow temporarily. Cover synchronous metadata, frame
   handoff, EOF, failure-as-completed, replacement, cancellation, and disposal with fakes. This commit must retain
   current live behavior and gives the FFmpeg work a testable boundary.
2. **Completed — prove the native ABI in isolation on Windows x64.** Add the C shim and pinned shared FFmpeg dependency,
   then open VFS-owned bytes without a temporary file. The initial gate decodes changing RGBA frames and positive
   stop times from one formerly failing 280x352 effect (`MVB961` or `MVB238`), aligned `MVB908`, and 800x600
   `CHAPTER`. It must also reject a truncated payload with a bounded diagnostic and survive repeated open/close.
   Do not switch the live runtime at this step.
3. **Completed — implement managed pacing and switch the factory.** Decode on a cancellable worker using FFmpeg timestamps
   and a monotonic clock, publish only due frames, delay EOF completion through the final presentation interval,
   and retain the existing watchdog. Make FFmpeg the default with no dimension/effect dispatch and exercise both
   non-modal `0x236` and modal/cancellable `0x20f` paths.
4. **Completed — run the installed-corpus gate.** Every one of the 213 MPEG payloads must open, report its expected display
   dimensions and a positive stop time, produce a correctly sized RGBA frame, maintain nondecreasing timestamps,
   reach EOF, and dispose within a bound. Normal tests use small project-authored 280-wide and aligned MPEG
   fixtures so decoder behavior is never disabled when the original install is absent; the full local sweep is
   an additional release/acceptance gate. Spot comparisons against DirectShow-compatible assets establish timing
   tolerance before DirectShow is removed.
5. **Validate the live sequences, then delete DirectShow.** Recheck LOGO/OP cancellation, SC0000 CHAPTER,
   aligned and 280-wide battle effects, combat cleanup, and the previously observed post-effect stall. Once those
   pass, delete the COM declarations/temp-file path and Windows platform annotation rather than retaining a
   fallback. If combat still stalls with FFmpeg, capture the VM/service coordinate after BTL cleanup and treat it
   as a separate battle-timeline defect.
6. **Complete distributable packaging.** Bundle pinned dynamically linked libraries and exact license/source
   provenance per target, validate loader isolation from machine-installed codecs, then add Linux/macOS build and
   smoke gates. Timestamped PCM delivery and Godot `AudioStreamGenerator` integration remain a later movie-audio
   slice; the video replacement must merely avoid closing that path.

Steps 1 and 2 landed on 2026-07-21 without changing live playback: `Main` owns an
`IMovieDecoderFactory`, `MovieRuntime` is backend-neutral, and the default factory still constructs DirectShow.
The isolated Windows-x64 C ABI opens VFS bytes through seekable custom AVIO and decodes changing RGBA frames from
both formerly failing 280x352 effects (`MVB961` = 500 ms, `MVB238` = 866 ms), aligned `MVB908` (333 ms), and
800x600 `CHAPTER` (12016 ms). Truncated input is rejected with a bounded error and ten repeated open/decode/close
cycles pass. During the full-suite gate, direct swscale output into a managed array exposed native heap corruption;
conversion now targets an FFmpeg-owned aligned frame and copies only the exact visible RGBA rows across the ABI.
The complete 313-test suite, three additional 312-test concurrency-sensitive repeats, zero-warning Godot build,
and threaded selftest pass. Step 3 is the next bounded slice; do not infer that combat is fixed until the paced
backend is selected live and revalidated.

Step 3 landed on 2026-07-22. `FfmpegMovieDecoder` owns the native session on a cancellable background thread,
decodes at most one frame ahead, waits against a monotonic `Stopwatch` clock for each normalized PTS, preserves
newest-frame-wins handoff, and reports EOF only after the larger of declared stop time or the final frame interval.
Disposal interrupts a far-future frame wait and joins the worker; asynchronous decode failure records a diagnostic
and completes the decoder so AGE cannot remain blocked. `Main` now selects `FfmpegMovieDecoderFactory`, while the
Godot build stages the shim and its five local shared-library dependencies beside `Himegari.dll`. DirectShow stays
in-tree but is no longer selected; delete it only after the corpus and live acceptance gates. Deterministic fake-
clock tests cover due-frame publication, final completion, cancellation, and failure. Real paced probes cover
formerly failing 280x352 `MVB961` and aligned `MVB908`. A natural headless SYSTEM4 smoke played and released
`LOGO.AGF` at its reported 7288 ms, then opened 106919 ms `OP.AGF` and published its first frame before bounded
shutdown. The user then confirmed the opening movies play correctly in a normal windowed run. The complete suite
is 318 tests, the Godot build is warning-free, and threaded selftest passes. That established the prerequisite
for the installed-corpus gate recorded below.

Step 4 completed on 2026-07-22. `tools/movie-corpus-gate` selects MPEG program streams by signature from the
complete native-order VFS catalog and runs the unpaced FFmpeg session to EOF. The acceptance run discovered the
expected 213 assets and passed all 213 under the 30-second per-item bound; every stream reported independent
matching dimensions, positive duration/frame rate, correctly sized RGBA frames, nondecreasing timestamps,
changing imagery, EOF, and clean teardown. The run decoded 15,788 frames across all twelve installed dimension
variants in 8.1 seconds; the longest item, 263-second `ED.AGF`, decoded in 4.4 seconds. Detailed compatibility
evidence lives in `docs/platform-portability.md`, and reproduction/report options live in
`docs/tools-reference.md`. This moved acceptance to step 5's windowed SC0000 `CHAPTER`, aligned plus 280-wide
combat effects, combat cleanup, and prior-stall checks. DirectShow remains unselected but in-tree until those
manual checks pass.

The first post-corpus combat recheck still stalled after an apparently absent effect. The ordinary Godot log
ruled out an FFmpeg decode/EOF failure: both reached battle batches opened, published first frames, and stopped
(`MVB005`/`MVB952`/`MVB913`, then `MVB921`/`MVB126`/`MVB953`) with no decoder failure or watchdog. F6 was added as
an observe-only bounded JSON snapshot of the current/recent VM path, host waits, movie/surface lifecycle, and
blocking finite gfx channels.

The first F6 capture localized the stall exactly to BTL's `0x2492..0x2515` movie polling loop, parked at the
16 ms sleep `BTL@0x250a`. Gfx had no blocking timed presentation, and the only real decoder (`MVB033`, surface
44) was complete. Surfaces 7 and 8 nevertheless retained incomplete/no-frame registrations for already stopped
`MVB953` and `MVB126`, so op `0x23a` could never clear. The user's observation that the last effect frame remained
until replacement was the same ownership defect: movie frames, completion, and decoder dictionaries were keyed
by shared resource id even though BTL can bind/restart one asset through multiple surfaces. Releasing one binding
removed shared state while another surface registration survived forever.

Movie ownership is now keyed by a unique playback instance; resource id is asset identity only. Each surface
resolves its own instance frame/completion, replacing a surface invalidates late callbacks from only its prior
instance, and releasing one of two same-resource playbacks cannot affect the other. Blank pre-roll surfaces no
longer borrow a concurrent instance's frame, and console/timeline diagnostics include playback ids. Duplicate-
resource and replacement regressions pass, as do all 327 engine tests, the zero-warning Godot build, threaded
selftest, and the 213/213 unpaced corpus gate. A quick repeat of the same combat exchange no longer stalled.
The run did expose false `AGF decode failed ... expected an ACGF image` lines after successful first frames and
immediately before teardown. This was not FFmpeg failure: the compositor could retain a pre-release `GfxState`
snapshot for one frame after the host detached the movie binding, then send the `.AGF`-named MPEG id through the
still-image fallback. Runtime-learned movie resource typing now survives instance teardown and suppresses only
that invalid fallback; it does not retain the frame or decoder. The focused cleanup regression, all 328 engine
tests, zero-warning Godot build, and threaded selftest pass. **NEXT:** perform a longer combat/return-to-FIELD
acceptance run, then delete DirectShow if the remaining live gate stays clean.

An independent long-standing console-spam issue was also localized at the same SC0000 third-CG boundary.
`play-sound-effect 0x28` loads `E0808.WAV`, whose RIFF `LIST/INFO` metadata uses CP932; Godot assumes those
unused fields are UTF-8 and logged each invalid byte twice because the script loaded the effect on two channels.
This was neither the AE glow renderer nor corrupt PCM. The complete extracted corpus contains 61 affected INFO
chunks among 238 valid WAVs, which also explains intermittent combat spam. A Godot-only adapter now strips only
INFO metadata from the transient buffer immediately before `AudioStreamWav.LoadFromBuffer`; the shared engine,
VFS bytes, and functional RIFF chunks are unchanged. Synthetic preservation tests and the real E0808 regression
pass, as do all 331 engine tests, the zero-warning Godot build, and the headless loader selftest with no Unicode
warnings.

**Mutable-surface fill/blend regression corrected.** The first visual recheck exposed BUNKI's menu interior
as transparent. SYSTEM4 creates 800x600 surface 3 and fills it opaque white through `0x20b`; the metadata-only
host fill left the new pixel buffer transparent. Implementing the fill alone made the panel solid gray and
covered FIELD's paper minimap backing, exposing the second half of the contract: native created/render-target
surfaces are not loaded mode-0 textures and are not surfaceless fills. BUNKI draws created surface 3 with
alpha `0xd0`; FIELD draws it beneath the minimap with alpha `0x40`. Created-surface mode 0 now uses packed
alpha as opacity and packed RGB as multiplicative modulation, while loaded textures retain their established
opaque/alpha-inert behavior. Created surfaces also carry no-colorkey `-1` rather than key-black zero. Focused
regressions cover clipped RGBA replacement and the exact created-surface blend classification; all 295 engine
tests, the zero-warning Godot build, and threaded selftest pass. Manual recheck confirms both the translucent
BUNKI panel and minimap paper are correct. The movement/overlay recheck described above is now the next manual
gate on the one-player-attack acceptance path.

## Later Phase B breadth

**INIT data-semantics side track started (2026-07-22).** Before naming more gameplay state, the static
extractor itself was audited. It now preserves sparse one-based ids and corrects ITINIT from 189 malformed
records to 287 items (plus SKINIT 129→131 skills); regression checks cover the real tables. It also evaluates
the INIT convention `sub destination, 0, magnitude`, preserving 113 negative ITINIT cells, 212 SKINIT cells,
and 86 EBINIT cells that the former `mov`-only pass silently dropped. A reusable field profiler reports
distributions, examples, and direct script/opcode consumers.

The first semantic tranche now curates the strongest item, skill, and unit fields in
`vm-map/globals.toml`. ITINIT has thirteen parallel arrays plus six linked row-major tables and 877 linked
writes across 44 populated columns; signed fields include equipment penalties and condition-removal deltas.
SKINIT contributes skill category/order/icon/handler, encoded range, element, status, combat/resource deltas,
proc chance, and battle animation. EBINIT now contributes unit order/icon, sex and provisional species,
elements, attacks/equipment/skills, level/cost/base/growth data, canonical variants, CP/CA/CB/CS/cut-in and
voice assets, XP/drops, capture and compendium flags, summoning economy, essence yield, and level scaling.
Its linked-table shapes are 18 SKINIT and 84 EBINIT populated columns. The next EB tranche identifies roster
state flags, per-action unlock requirements, SCJUMP event ids, normal/brainwashed unit variants, and SALLY's
per-unit bonus item. Combat tracing also names the shared column-8 item/skill modifier as critical chance.

The message-table tranche adds a reusable extractor for global-id dispatch chains and joins player-facing
ITMES/SKMES text back to INIT records. All 287 item ids and all 131 skill ids match exactly in both
directions. Each joined message retains its rendered title, richer description, furigana annotations, and
dispatch offset independently of INIT's shorter effect label; generated profiles report complete coverage.
The dispatch keys also establish `current_item_id` and `current_skill_id` as high-confidence shared index
slots. This makes message/field correlation the next evidence source for the remaining sparse item and skill
columns.

The follow-up correlation pass makes that evidence directly queryable with
`init_table_profile.py --message-query REGEX` and moves confirmed item/skill row-column meanings into
structured `globals.toml` metadata. Extracted JSON and profiles now resolve raw keys to names such as
`item_stat_modifiers.critical_chance` while preserving the original address key. The message audit confirms
all populated condition columns and the female-only item mask. CHMENU also resolves the last anonymous
SKINIT parallel field as the skill-change catalog inclusion flag and separates acquired-skill state from
skill-information visibility. The remaining item/skill work is refinement rather than an unnamed populated
schema.

The remaining EBINIT unknowns are two isolated sparse writes into a runtime table; enemy AI appears to live
outside the static EBINIT schema. The signed boss class is now behaviorally separated: all nonzero values
receive boss protections and targeting treatment, positive values alone count as required targets under
FIELD's defeat-boss clear rule, negative values are boss-treated adds/decoys/hazards that do not block
victory, and either sign of class 4 selects final-boss music and tactical-map presentation. Classes 1--3
otherwise remain authoring categories rather than distinct runtime branches. The former seven-value
`0x7843e` unknown is now a medium-confidence authoring-only `unit_power_tier`: it tracks unit cost, yield,
caps, stats, and form/story strength but has no shipped script or native-index consumer. SALLY's four unlock
requirements and eight event ids are fully joined to their contract, brainwash, reserved, form-dependent
sex-magic, sacrifice, and release columns. FIELD/BTL/SHOWGROW also resolve all 23 formerly generic populated
voice columns, including five authoring slots unreachable in the shipped selectors. STINIT's separate mixed
parser is now complete:
all 74 sparse stage ids retain their victory/defeat strings, six scalars, fixed-buffer cells, and 1,396
footer-array copies. AIM/FIELD/DRAWCHP consumers establish the four condition slots, stage BGM, turn limit,
and turn-limit outcome. Further passes establish target clear turns, performance reward, replay behavior,
the 20-slot map-texture override list, object type/tile/difficulty fields, and seven required plus five
forbidden story prerequisites. Generated records now carry a joined `semantic_fields` view and assemble
2,312 object placements across 66 stages. FIELD resolves the universal reinforcement interval/limit and
type-tagged initial-faction, teleport, treasure, card-list, and non-triggering-faction payloads. OBINIT
supplies authoritative names for all 46 object types and descriptions for 34. Its state-row mode and
FIELD/DRAWOBJ initialization path resolve 78 initial object states on types 11, 17, and 26. FIELD's
dedicated special-spawn path proves the last three populated type-27 tagged writes are ignored by the
engine; they remain preserved as provenance. The separate 30-cell enemy family is also decoded:
FIELD/SETEN/ADDEN establish unit/faction, direct or object-linked placement, difficulty/story gates, level
floor/cap/scaling, weighted selection, and three difficulty-specific movement/battle routine-set ids. The
generated view assembles all 1,378 populated enemy slots across 66 stages, including 485 enemies suppressed
after the stage's first clear.

Once the natural spine and first gameplay loop are trustworthy, broaden in independent tracks:

- Remaining title/configuration/load/save branches.
- Save-file format and restoration of a persistent session.
- Map navigation, camera, terrain, deployment, and turn lifecycle.
- Unit statistics, equipment, inventory, skills, magic, heroine forms, and progression.
- Combat resolution presentation, enemy turns/AI services, and win/loss transitions.
- A full chapter of ADV and the scene types encountered between gameplay segments.
- Other data schemas when their runtime consumers make them necessary.

The high-level Phase B direction remains canonical in `docs/remake-architecture-and-roadmap.md`; this file
only provides the execution framework.

## Slice record template

When a stage becomes active, add its concrete plan/results to this document or the then-current slice plan
without pre-planning all later systems. Record:

- Starting state and exact reproduction path.
- One observable end condition.
- Executed unknown/effectful opcode families.
- Required global/profile/host state and its producer.
- Native/original evidence to capture.
- Explicit non-goals.
- Automated regression gates and manual acceptance check.
- Result, remaining discrepancies, and the next decision gate.

## Decision gates

- After B0: confirm or revise the boot/title/SC0000/dungeon sequence using observed load order.
- After B2: decide whether session state is trustworthy enough to remove the diagnostic boot path from
  ordinary runs.
- After B3: choose whether save/config work blocks natural SC0000 entry; otherwise defer it.
- After B4: use the actual first-dungeon trace to write the concrete B5 slice rather than guessing its
  systems in advance.
- After B5: choose breadth based on the next natural blocker, not opcode coverage percentage alone.

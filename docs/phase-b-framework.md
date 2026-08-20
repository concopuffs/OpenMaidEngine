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
7. **Escalate persistent or cross-subsystem discrepancies.** Start with the smallest evidence-backed local
   investigation. If a discrepancy crosses a subsystem boundary, or survives one focused local correction,
   stop making local patches and apply the subsystem escalation rule below.

### Subsystem escalation rule

When the escalation trigger is met:

1. Bound the affected native subsystem and map its relevant entry points, lifecycle, owned state, and
   interactions with adjacent subsystems. This is a focused map of the failing contract, not an exhaustive
   decompilation of `AGE.EXE`.
2. Define the observable contract at stable coordinates and capture native evidence for it. Prefer a
   repeatable differential oracle such as VM/global snapshots, save/resume state, retained-surface
   fingerprints, audio/video timestamps, or input/yield transitions.
3. Compare the native evidence with the port and localize the first meaningful divergence before changing
   the implementation again.
4. Implement the smallest correction consistent with the mapped lifecycle, then retain the differential
   comparison as an automated regression where practical and finish with the slice's manual acceptance check.

End the escalation once the reproduced discrepancy and its relevant contract are explained. Do not expand it
into reconstructing unused handlers, recovering the whole native engine, or producing a byte-matching C build.

### DEBUGMAP P008/P009 GDI worker-end regression corrected (2026-08-15)

A live `SYSTEM4 > FIELD > SC0270` run first reported `[vm] ended: unknown` while the locator still showed P008
(`SC0270@0x5b22`). The coordinate is an ordinary `wait-for-input`; its next instruction begins P009. A
corpus-backed regression and automated portable-text runs crossed that boundary, but those runs did not exercise
the exact Windows GDI rasterizer used by normal Windows play. The `unknown` message also hid the actionable
exception because the worker's `finally` set the completion flag while the main loop printed only the unset VM
halt reason. Godot now retains and prints the complete worker exception and automatically writes a
`worker-failure` diagnostic snapshot at the last traced script/opcode coordinate.

The next live failure and that snapshot localized the defect to P009's `show-text` at `SC0270@0x5bea`. This line
contains an ideographic full-width space (`U+3000`, CP932 `0x8140`). With `ＭＳ 明朝` at the active 24-pixel
style, `GetGlyphOutlineA(GGO_GRAY4_BITMAP)` returns the spacing glyph's advance and a nominal 1x1 metric box but
reports a zero-byte bitmap requirement. The adapter incorrectly required the reported buffer size to equal the
four-byte aligned metric box and threw before P009 could be published. It now treats only a zero-byte result as
a zero-ink glyph: the GDI placement and advance metrics are retained and the implied mask is filled with
transparent coverage. A focused Windows regression reproduces the original 0-versus-4 result and verifies that
the ideographic space advances without drawing ink; nonzero buffers smaller than the metric-defined rows remain
hard failures. The SYSTEM4/DEBUGMAP boundary regression remains in place. The user then repeated the normal
exact-GDI DEBUGMAP entry and confirmed that the dialogue now continues into the map without the worker failure,
accepting the fix end to end.

### SYSTEM4 P009 oversized GDI buffer corrected (2026-08-16)

A new-game run with Ctrl held reached `SYSTEM4 P009`, whose text is `SC0000@0xa77` and whose following wait is
`SC0000@0xa7f`, then failed while rasterizing the retained line. GDI reported a 672-byte gray-4 requirement for
a 25x23 metric box with 28-byte rows; the adapter rejected it because the metric-defined coverage is only 644
bytes. This is not the earlier zero-ink spacing-glyph case. A focused independent ANSI/Unicode probe reproduces
the same oversized-storage contract with 24-pixel bold MS Mincho `p`: `GLYPHMETRICS` reports 12x15 and a
12-byte row stride, while `GetGlyphOutline` requests 192 bytes rather than the 180 bytes described by those
rows. The surplus may contain unspecified data and is not an additional drawable metric row.

The adapter now follows the two returned contracts independently: it allocates and reads the complete size
requested by GDI, but normalizes only `stride * gmBlackBoxY` bytes into `GlyphMask` for layout/composition.
A nonzero undersized result, a failed read, or changed metrics between the query and read remains an error. The
regression compares the ANSI adapter with an independent Unicode GDI call, proves that the raw buffer is larger
than the normalized mask, and checks the exact metric-defined coverage. A second regression covers every glyph
in the reported SC0000 line with its active 24-pixel bold Mincho style. The real direct-scene harness, exact GDI
backend, and held-Ctrl input then recorded the reported P009 coordinate and continued through P166 without a
worker failure; the probe stopped at the later interactive name-entry boundary.

### SC0000 BGM fades now honor active ADV Skip (2026-08-16)

The runtime locator captured `SYSTEM4 P046` at `wait SC0000@0x4180`, after which all-message Skip appeared to
stall and clicks did not advance anything until execution eventually continued. The next effectful instruction
is `fade-bgm SC0000@0x418d`, targeting zero over 3000 ms. The Godot audio host always entered its blocking fade
clock, and neither its click path nor a skip wake changed the deadline. The same captured run exposes equivalent
post-page fades at P015 (3000 ms) and P090 (1500 ms).

Native `/v2` separates those inputs. `op_0xc2_bgm_fade@0x4204c0` immediately applies the endpoint without
entering run-state `0x200` when ADV fast-forward state `0x08000000` is already active. A normal action-4 click
only releases the preceding text wait; the native `0x200` service itself does not poll click input, so an
ordinary click intentionally does not skip a BGM fade. VM audio dispatch now substitutes a zero-duration host
fade only for active persistent/read/physical fast-forward and preserves the authored duration otherwise. The
focused regression locks both outcomes.

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

The current headless C# runner already follows this root naturally: one SYSTEM4 run enters INITCONFIG,
INIT2 and all 23 of its base data-initializer children, APPEND01's AUTORUN and 22 append initializer
children, TUNE, INIT, and TITLE (51 nested script calls total), then
remained in TITLE's input-poll loop because the diagnostic host supplies no user input. Direct opcode
coverage is 100% for all 23 data initializers, CALCARR, and TUNE; the remaining direct coverage is SYSTEM4
64/82, INIT2 10/12, TITLE 61/65, GAMESTART 43/47, and UNITECH 29/31. B0/B1 should therefore make the
SYSTEM4-rooted path visible and interactive in Godot, then investigate only the gaps actually reached on
that route instead of treating every static gap as a prerequisite.

**Append boot implemented (2026-07-24).** Mounted `APPEND01` has
`$1$AUTORUN.BIN` at packed id `0x01000000`; that script calls 22 append INIT deltas and registers the
append's class-change, SCJUMP, message, stage, and scenario resources. INIT2's sole op `0x143` site,
after its 23 base initializer children and registry setup and before TUNE, scans mounted selectors in
ascending order and serially executes each selector's record zero. The port now snapshots the mounted
selectors through its script-provider seam, constructs each `selector << 24` id, and runs those scripts
through normal nested VM frames. A natural SYSTEM4 regression proves the shipped order
`BTANINIT2 -> $1$AUTORUN -> $1$EBINIT -> TUNE`; focused regressions cover multiple-selector ordering,
deduplication, caller resumption, and missing record zero. The packed sequence is canonical in
`docs/asset-resolution-re.md`; native queue/frame mechanics are canonical in `docs/engine-re.md`.

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

**TITLE-to-SC0000 BGM fade ownership corrected (2026-07-28).** SC0000's `0xc2@0x7c1` blocks the VM for
the three-second title-music fade, then starts `BGM005` at `0x7fa`. Godot created the actual gain tween
through a deferred main-thread call, however, so its start could lag the already-running virtual deadline
by one frame. On the unlucky schedule, `BGM005` restored unity gain just before that stale title tween
finished and wrote `-80 dB`. Voice ducking exposed the diagnosis: a voice wrote the configured 50-percent
level, making the live BGM audible, then restored the captured silent level. The Godot backend now owns
exactly one explicit BGM fade, cancels it before replacing the track or starting another fade, and suppresses
voice ducking while the fade owns the envelope as native AGE does. The threaded selftest starts a live
fade and proves a replacement BGM cancels it and retains normal gain.

**Pre-title video sequence implemented (2026-07-20).** SYSTEM4 already owns the native sequence; the
port did not lose an executable-side launcher. Its sole op `0x130` call returns an engine initial-root flag
that is one at context construction and cleared only when op `0x9` resets/reloads root script id zero.
SYSTEM4 calls `LOGO.BIN` and `OP.BIN` only while that flag is nonzero. The former stubbed-zero output
explained the direct jump to TITLE. LOGO and OP then use the modal movie op
`0x20f` with packed catalog movies `0x335f`/`LOGO.AGF` and `0x3364`/`OP.AGF`; existing `0x236` is the distinct
non-modal movie-to-surface path and uses the same packed lookup. The VM now models the initial-root flag and clears
it at the op-`0x9` whole-stack root-reload boundary. Godot resolves a typed MPEG asset, reuses the asynchronous decoder
and retained compositor, and parks the VM until EOF or mouse/Accept/Cancel input. Focused natural-boot tests
prove `LOGO -> OP -> INIT -> TITLE` ordering and exact movie operands. MPEG audio was explicitly deferred
at this milestone; the engine-owned synchronized audio/volume contract landed on 2026-07-25 as recorded below.

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

**Extra Room CG/H-scene unlock database (`CGMODE.BIN` / `HMODE.BIN`, 2026-07-28).** The copied
`user://SAVE/SAVE.DAT` was intact: native decode finds 7,966/13,208 valid base-catalog markers and
75/81 append markers, including all 851 CGINIT gallery-image ids and all 118 SPINIT H-scene script ids.
The encyclopedia and New Game settings looked correct because they restore selected integer cells through
`0x1a3`; CGMODE/HMODE instead filter their entries through opcode `0x19d`, which the port still stubbed and
therefore left as zero/locked.

Native RE identifies `0x19d` as the profile catalog-resource unlock predicate. Successful catalog opens
write a deterministic per-index stamp into a live marker table and its modular-exponent encrypted
SAVE.DAT table; shared-profile load decrypts base and selector-keyed append arrays before the query runs.
`SharedProfile` now owns that decoded set, the VM implements the query, and a shared VFS wrapper marks only
successful script/texture/audio/movie/cursor opens. Saving emits native-decodable base and append tables,
so imported unlocks display immediately and newly encountered resources persist. Focused regressions cover
exact native ciphertexts, base/append queries, successful-open timing, round-trip import/export, and known
installed CG/H-scene entries.

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
- Mounted append initialization: implement op `0x143` at INIT2's natural bytecode position by enumerating
  mounted selectors 1..255 in ascending order and executing packed record zero serially, preserving
  base-versus-append order and provenance rather than treating same-suffix INIT fragments as filename
  replacements.

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
- Remaining configuration/save UI branches beyond the native compatibility floor. Shared and numbered
  codecs, real SAVE.BIN listing/load, port-authored round trips, and clean-shutdown profile persistence
  landed on 2026-07-24.
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

Kamidori SYS4433 later reached the same subsystem through its extended opcode `0x2da` at
`DRAWCHP.BIN@0x626` on the hidden-menu Dungeon route. SYS4433 stores seven fields per numeric-glyph style:
the five `0x13a` fields plus independent source-atlas and destination-object cell spacing. Its `0x13a`
writer clears those additions; `0x2da` supplies them explicitly; `0x23b` consumes both when stepping digits.
The neutral retained-graphics model now represents all seven fields while preserving zero spacing for
SYS4422 and legacy SYS4433 registrations. This is revision-scoped opcode behavior reusing a shared renderer,
not a Kamidori gameplay special case.

Continuing that route through `SC2310` exposed a diagnostic false positive rather than another ABI frontier.
The halt printed as `LOOP:line@0x57b7×3` (the multiplication marker can disappear when copied). Translated
`SC2310@0x57b7` is a reusable `show-text 9, local-string-pointer` helper that publishes the resolved speaker
name before many distinct dialogue lines. The VM's authored-line loop guard had generalized its original
inline-string rule to all string operand forms, so the helper's third legitimate invocation looked like one
literal line looping. The guard again counts only inline string coordinates, matching the headless oracle;
dynamic global/local string renderers remain bounded by the VM step limit. Installed translation coverage
anchors the exact `SC2310` instruction, while synthetic regressions prove reusable dynamic emission and retain
the repeated-inline-line halt.

The following real compatibility frontier is `AIM.BIN@0xae`, reached when the ADV portion ends and the
dungeon win/loss conditions appear. SYS4433 opcode `0x251` is a retained radial-blur range transition, not
an AIM gameplay operation: command handle 90000 republishes handles 70010..70032 through target surface 14,
animating blur length 100 to 0 around `(512,204)` after a 100 ms delay over 500 ms. The shared presentation
model now carries the native command/range/timing contract through the ordinary `0x21c` wait service and both
Godot compositor backends. The original D3D9 effect is not available to the cross-platform compositor, so presentation uses
six center-anchored weighted zoom samples while length is nonzero and the exact unfiltered range at the zero
endpoint. This approximation is host presentation policy; the VM contract remains the native SYS4433
contract, and no game-specific AIM branch was added.

### Kamidori no-implementation opcode catalog (2026-08-19)

A complete `catalog-scan` of all 732 scripts (542 base and 190 append) provides the static backlog rather
than relying only on whichever opcode strict execution reaches next. The official root/archive corpus has
2,126,378 instructions; the translated overlay has 2,161,326. Both decode with zero failures, use the same
269 distinct opcodes, and produce the same missing-opcode distribution. After implementing `0x2d8`, the
initial catalog contained 17 gaps. The following `0x27`/`0x28` patterned surface-reveal slice removed 193
calls across 47 scripts. Implementing the retained directional-blur command `0x250` then removed another
34 calls across 11 scripts. The shared cyclic-translation opcode `0x235` removed 28 calls across 12 more
scripts. Shared movie-rate opcode `0x246` then removed the sole BTL-only call. The shared current-rotation
query `0x227` subsequently removed twelve calls across `SC0600` and `SC2060`, leaving eleven recognized
opcodes without an executable runtime handler. Complete retained-object swap `0x214` then removed one call
from each of ten story/presentation scripts. Shared type-2 patterned transition `0x24f` removed the ten
copies of Kamidori's story-portrait helper. The paired ordinary-ADV redraw operations `0x83`/`0x82` then
removed all twelve CONFIG1--CONFIG5 calls. Shared scaled surface copy `0x32` then removed the two map/save
thumbnail calls. Shared point-in-polygon `0x147` then removed the ARRANGE/STUDIO pair. The shared ADV
skip/input refresh `0xfa` removed SHOWALLMAP's singleton. Shared joystick capability query `0x106` then
removed CONFIG's singleton, leaving three:

| Opcode | Calls | Scripts | Static scope |
|---|---:|---:|---|
| `0x22e` | 1 | 1 | `SC6400` |
| `0x2dc` | 1 | 1 | `SELFONT` |
| `0x2dd` | 2 | 1 | `SELFONT` |

The patterned-reveal pair preserves the native selector mapping, direction, blocking duration, fast-forward
endpoint, and surface target. Godot currently approximates the legacy simultaneous/staggered strip masks as
a timed crossfade; this is host presentation policy rather than a VM contract change. `0x250` now preserves
the native type-3/mode-0 command contract, selected retained range, Length/Angle interpolation, timing, and
completion behavior; Godot approximates the unavailable D3D9 shader with translated alpha-weighted samples.
`0x235` now preserves the shared SYS4422/SYS4433 cyclic-translation contract, native triangular clock,
independent retained state, save-record fields, and composition after cyclic rotation. `0x246` now maps the
shared SYS4422/SYS4433 `IMediaPosition::put_Rate` contract onto synchronized FFmpeg video and Godot audio
pacing. `0x227` now returns current retained-object axis/angle state with the native missing-handle contract;
its native handlers also expose a real subrevision detail, because SYS4433 added a float-aware operand
writer where SYS4422 explicitly truncated before its ordinary writer. This does not change Kamidori's
all-integer call sites. `0x214` now swaps or moves complete retained-object records, including preserved
native state and the handle-sorted presentation index. `0x24f` preserves the shared type-2 command's two
retained ranges, command-object bounds, modes 0..11, pattern thickness, timing, validation, and endpoint;
Godot currently uses a timed crossfade in place of the unavailable native clip-mask compositor. `0x83` now
returns the retained redraw pair and neutral direction used by every Kamidori CONFIG return, while `0x82`
republishes that group with the shipped temporary color override. The native comparison records a genuine
revision distinction: SYS4422 queries the latest anchor, while SYS4433 queries its mutable direct-history
cursor. `0x32` preserves the shared ten-operand paired-rectangle contract; Kamidori's two in-bounds calls
area-downsample the full 1024x576 map surface to 384x216 thumbnails, while equal-size requests reuse the
existing exact surface-copy path. `0x147` preserves Win32 ALTERNATE-fill point-in-polygon semantics,
including the half-open top/left-inclusive boundary convention used by its four-vertex ARRANGE/STUDIO
sites. `0xfa` maps SHOWALLMAP's boundary onto the existing physical/script skip, queued-voice, and read-skip
services; it has no map-specific behavior. `0x106` exposes the input host's active joystick physical-button
count and defaults to zero when no device/capability is available. Story and font-selection-only gaps can follow unless live
execution reaches one first. This ranking is triage, not a substitute for strict-runtime evidence: the next
real compatibility halt still takes precedence. The machine-readable reports are
`build/games/kamidori/catalog-opcode-coverage.json` and
`build/games/kamidori/translation-overlay-corpus.json`; each entry includes every script/offset, operand-type
signatures, and representative typed operands. Generated handler bindings for the SYS4433-only findings
`0x27`, `0x28`, `0x250`, `0x251`, `0x2d8`, `0x2da`, `0x2db`, and `0x2de` are explicitly evidence-confirmed for
SYS4433; a generator regression prevents their provenance from falling back to Himegari/SYS4422 defaults.
The `0x32`, `0x82`, `0xfa`, `0x106`, `0x147`, `0x214`, `0x227`, `0x235`, `0x246`, and `0x24f` evidence independently record both
Himegari/SYS4422 and Kamidori/SYS4433 rather than inferring one revision from the other.

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
   smoke gates. Timestamped PCM delivery and Godot `AudioStreamGenerator` integration were assigned to a later
   movie-audio slice; that slice landed on 2026-07-25 as recorded below.

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
Godot build stages the shim and its five local shared-library dependencies beside `OME.dll`. DirectShow stays
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

### MPEG movie audio implemented; audible acceptance in progress (2026-07-25)

The FFmpeg shim ABI is now version 3. Each in-memory MPEG payload feeds independent seekable video and audio
demuxers, preserving concurrent decode without temporary files, and exposes timestamped interleaved stereo
float PCM resampled at the source rate. Video and audio timestamps share the same normalized media origin.
The managed decoder owns bounded video and PCM queues plus separate workers; audio-bearing movies use the
Godot audio-device clock as the presentation master, while video-only movies retain monotonic stopwatch pacing.
Completion waits for the final video interval and for decoded PCM to be submitted through the shared zero-based
presentation endpoint.

ABI v3 adds a synchronous dual-demuxer initial seek used by opcode `0x241`. Native seek lands on preceding
indexed packets; managed preroll selects the video frame active at the requested position, sample-trims audio
to the same boundary, and rebases remaining pacing and completion. This preserves CALLBACK_LOAD's terminal
frame restoration without decode-from-zero latency or stale pre-seek audio.

Godot creates a per-playback `AudioStreamGenerator`, compensates for output latency, handles timestamp gaps and
overlaps, and tears it down with the corresponding playback instance. Native movie flags route sound to the
Movie bus by default, with exact overrides for mute (`0x10000`), Music (`0x20000`), SFX (`0x40000`), and Voice
(`0x80000`). F6 diagnostics now expose audio format, decode/submission state, route, clock, and underruns.

The full installed-corpus gate passes all 213 movies: 29 audio-bearing assets decode 17,537 blocks /
18,185,856 stereo PCM frames at 44.1 kHz with nondecreasing timestamps and non-silent signal, while all 184
video-only assets remain audio-free. At the initial landing, focused real-stream tests covered MP2 and MP1
content, all 404 engine tests passed, the Godot build was warning-free, threaded selftest passed, and a natural
headless boot completed LOGO with its audio path active before opening OP. This moved acceptance to audible,
synchronized LOGO/OP and representative CHAPTER/MVS playback in a normal windowed run; DirectShow remained
unselected but in-tree until that live gate.

The first normal windowed LOGO/OP run confirmed that audio reaches the intended output, but it sounded
crackly/warbled. Corpus endpoints showed continuous decoded PCM; the fault was presentation alignment.
Audio PTS crosses the ABI in whole milliseconds, losing up to 44 samples of precision at 44.1 kHz, while the
initial sink inserted or dropped that tiny discrepancy at every MPEG block boundary. Established timelines
now tolerate 2 ms of timestamp quantization and remain sample-contiguous, while initial offsets and material
later gaps/overlaps still insert silence or trim PCM. A regression simulates all 4,093 OP blocks without a
splice and separately pins real discontinuity handling. The repeat LOGO/OP run and SC0000 CHAPTER playback are
audibly clean. Together with signal-bearing decode of all 29 audio streams, this closes the movie-audio acceptance
gate: missing, distorted, or unsynchronized audio is now a runtime bug. The unused DirectShow implementation,
COM/temp-file adapter, compatibility test, and managed Windows platform annotations are deleted. **NEXT:** add
Linux/macOS FFmpeg builds and packaging smoke gates when work returns to cross-platform distribution.
Post-cleanup validation passes all 405 engine tests, the warning-free Godot build, threaded selftest, opcode
lint/tooling, and the 213/213 installed video/audio corpus gate.

The shared Windows/Linux residual A/V report was investigated on 2026-08-11. A read-only native LOGO trace
proved DirectShow supplies AGE zero-based 33.3667 ms video samples and contiguous 26.1224 ms audio samples; an
early hook measured the first non-silent native PCM block at 2,351 ms. Sparse native video fingerprints match
FFmpeg frames at equal timestamps and expose changing native frames during 0--600 ms. The shim had skipped those
frames by rewinding to the video stream's declared 600 ms `start_time`, then managed pacing displayed frame 600 at
time zero while audio retained the common origin. Rewinding video to the format timeline origin recovers 218 LOGO
frames beginning at PTS 0 instead of 200 beginning at 600. Its audio onset around 2,380 ms again precedes the
first `E` at PTS 2,502 ms. Completion separately retains the terminal image until queued audio drains through the
full graph endpoint. Focused and real-asset regressions pin both corrections; manual LOGO/OP confirmation remains.

**CORPUS-WIDE OPCODE HARDENING PASS STARTED (2026-07-28).**
With numbered-save implementation complete and further live save testing explicitly deferred, the active
breadth pass now ranks every Himegari-observed VM fallthrough across all 481 decoded scripts. The starting
inventory has only 25 distinct effectful gaps. The first tranche closes the two widest independent holes:

- `0xba` occurs 334 times in 100 scripts. Native `op_0xba_sfx_start_loop@0x420290` calls the same retained
  SFX start worker as one-shot `0xb5`, but with logical loop mode 1. The host seam now preserves that mode
  and Godot rewinds the WAV stream at EOF.
- `0x1fe` occurs 187 times in 47 scripts. Native
  `op_0x1fe_set_rotation_current@0x422700` immediately replaces the retained object's current axis-angle
  rotation. The existing compositor already sampled that field; the VM now writes it directly.

Together these handlers close 521 formerly skipped instructions and reduce the corpus-wide effectful
fallthrough inventory from 25 to 23 distinct opcodes. Focused regressions distinguish one-shot/looping SFX
starts and prove immediate rotation does not arm the delayed `0x21f` channel. The opcode registry and
generated references are current, and the native `/v2` image is annotated and saved.

**Cyclic scale `0x233` implemented (2026-07-28).** The retained model now owns the native channel
independently of immediate and delayed one-shot scale. It samples the exact triangular
identity-to-target-to-identity phase, remains frame-driven during visible waits, survives object cloning,
and enters the separately anchored cyclic product after one-shot translation and before cyclic rotation.
The VM handler closes all 31 skipped sites across DEBUGADV and eight ordinary ADV scenes, reducing the
effectful fallthrough inventory from 23 to 22 distinct opcodes. Focused tests cover dispatch, percentages,
odd/even period timing, continuous-presentation classification, and matrix order; the full engine suite,
warning-free Godot build, and Himegari-targeted threaded selftest pass (468 engine tests).

**CONFIG mixer tranche implemented (2026-07-29).** `0xc5`/`0xc6` now get/set master, music, SFX,
voice, and movie volume in native basis points; `0x1ba`/`0xc7` set/get the four non-master route flags.
The registry is shared across fresh scene VMs and persisted in AGE's native CP932 `SYS4REG.INI`. A shared
path resolver models SYS4INI's independent `SAVEPATH`/`REGFILEPATH` choices; Godot redirects Himegari's
related profile root to `user://games/himegari/`, yielding `user://games/himegari/SAVE` and
`user://games/himegari/SYS4REG.INI`. The compatibility
writer updates only the nine audio keys and preserves every unrelated option. Raw music state retains
native `2 ↔ -1` band toggling. Godot applies
gains through its nested audio buses, so master and category volume compose, and route changes immediately
stop/mute active playback according to the native worker distinctions. All 37 CONFIG sites now dispatch,
reducing the effectful fallthrough inventory from 22 to 18 distinct opcodes. Eight focused tests cover
native defaults, category isolation, getter/setter round trips, idempotent routes, cross-VM state, invalid
selectors, native and redirected path resolution, CP932 INI preservation, and persistence. All 476 engine tests, the
warning-free Godot build, opcode lint, and the Himegari-targeted threaded selftest pass.

**Post-tranche rerank:** a fresh 481-script aggregate found exactly 18 effectful gaps totaling only
39 instructions. The widest were `0x22` (eight sites across seven scripts), `0x230`
(six sites across two scripts), then `0x21` and `0x1b2` (three sites each); every other gap had at most two
sites. CONFIG itself is now 1659/1665 instructions implemented, with four unrelated calls remaining
(`0x142` twice, `0xb7`, and `0xb8`).

**Next-opcode investigation complete (2026-07-29):** `0x22` is the mode-1 half of AGE's blocking
single-surface black-fade pair, not a larger subsystem. `0x21(surface,timing)` fades black to the captured
surface; `0x22(surface,timing)` fades the captured surface to black. They share `0x25`'s already-modeled
timer conversion and full-frame snapshot lifecycle. All 11 combined Himegari sites use `(1,30)`, about
480 ms, across menu entry/exit paths. The existing `LegacyScreenTransition` host seam can own the pair,
but it needs an explicit black endpoint rather than its mode-4 missing-surface fallback.

**Black-fade pair implemented (2026-07-29):** `0x21` and `0x22` now dispatch through the shared blocking
full-frame transition path. The compositor represents the solid-black side explicitly as an empty captured
object list over its normal opaque-black clear: source-empty for black-to-surface and target-empty for
surface-to-black. This preserves mode-4 `0x25`'s separate missing-target fallback, shares the native timing
conversion and exact terminal-frame publication, and closes all 11 sites. The remaining effectful inventory
is 16 distinct opcodes / 28 instructions. All 477 engine tests, opcode build/lint, the zero-warning Godot
build, and the Himegari-targeted threaded selftest pass.

**Legacy-transition Skip/click lifecycle corrected (2026-07-29):** Ghidra's outer engine tick exposes the
shared mechanism behind native skippable effects. With ADV fast-forward already active, `0x21`, `0x22`, and
`0x25` publish their terminal endpoint without starting the timed service. Otherwise run-state bit 8 owns
the transition; when `SYS4REG.INI` `system:EffectSkipOnClick` is enabled, logical action 4 is consumed and
the active effect tick receives delta `0x10000000`, forcing that same endpoint. The port now carries the
fast-forward state into legacy-transition dispatch and prioritizes action 4 over retained ADV hotspots
while a transition is parked. A focused regression covers forced black-fade and crossfade dispatch.
Validation passes 519/519 engine tests, opcode build/lint, the zero-warning Godot build, clean diff
checking, and the Himegari-targeted threaded selftest.

**Retained CG-fade click completion corrected (2026-07-29):** the legacy service above was only one of two
native paths and did not cover the story CG fades observed in the port. The outer tick's separate
run-state-`0x400` service applies `EffectSkipOnClick` to the complete ordinary finite retained-animation set:
queued type-0 surface commands plus object color/alpha, scale, rotation, and translation channels. The port's
click handler had incorrectly forced only the surface-command subset, so CGs implemented as object alpha or
matrix fades still consumed their full duration. Interactive advance now completes the complete native set;
activating Ctrl/Skip during the wait uses the same endpoint path. Ambient loops, op-`0x242` detached channels,
and movie masks remain unaffected. Focused exclusions and endpoint regressions pass in the 523-test engine
suite, and the Godot build is warning-free.

**Retained transition wait ownership corrected (2026-07-29):** playthrough testing with Skip held exposed
that endpoint completion alone was insufficient. Skip could force an ordinary fade invisible, yet the host
continued waiting on the broad presentation predicate until a movie-backed/protected channel or the original
duration ended. Native clears run-state `0x400` before requesting endpoint completion, so interpreter resume
is independent of whether excluded presentation remains active. The host now records that explicit service
bypass, exits the transition wait immediately when the action is accepted, and reapplies an already-active
Skip state at every new retained-transition boundary. Focused coverage distinguishes service-flags bit 0
(reject the click) from bit 1 (accept resume but suppress endpoint forcing) and proves movie masks continue
asynchronously after wait bypass. A direct `SC0000@0xc01` runtime trace—the page before the ritual CG
sequence—shows its four click-skipped retained waits completing in 14, 14, 14, and 34 ms despite scripted
durations of 150/890/300 ms; the later 1.587-second interval is the next page's ordinary glyph reveal at
`SC0000@0xe04`, not an invisible transition wait.

**Held-Skip pacing corrected (2026-07-29):** comparing held logical action 6 against the click trace at
`SC0000@0xc01` exposed a separate delay outside the transition service. The Godot host was waiting for one
rendered-frame pulse after every VM opcode while Skip was active, stretching hundreds of burst-fast CG
setup/cleanup instructions into an invisible multi-second hold. Native live evidence instead measures roughly
7,738 operand fetches/sec under Ctrl, and Ghidra shows the physical action-6 mask polled independently of the
`0x19b`/`0x19c` persistent-Skip lifecycle. Per-op Skip pacing is removed, and a held Ctrl/C/Backspace channel
now survives `0x19b`; presentation, sleep, and input boundaries remain the only wall-clock owners. The
post-fix held-action trace reaches page 14 from `SC0000@0xc01` in 83 ms and takes the native
`0x243`+`0x20c` endpoint branch at every intervening transition. The engine suite passes 525/525.

**Nested ADV input ownership corrected (2026-07-29):** the first-dungeon boss event reaches ordinary
dialogue at `SC0600@0x2343` and parks at `wait-for-input@0x234b`, but FIELD's registered timed mouse
callback remains stored in its suspended parent script frame. The frontend incorrectly treated any stored
raw callback as current modal input ownership. It consequently hid the wait indicator and withheld normal
mouse/keyboard/joy advance signals from the nested SC0600 page, producing a softlock immediately before
combat.

Raw callback ownership is now frame-relative: it is active only while its owning script frame is the
currently executing frame. The dormant FIELD registration survives the nested call and becomes active
again when FIELD resumes, while SC0600 retains its ordinary ADV marker and input path. A synthetic
parent-callback → child-dialogue regression preserves both halves of that lifetime. The engine suite passes
526/526, the Godot build has zero warnings, and the Himegari-targeted threaded selftest passes.

**Post-boss `A1215.WAV` lockup corrected (2026-07-29):** after the first boss,
SC0010 executes `play-sound-effect 0x125` at `0xea1`. The packed `DATA1/A1215.WAV` entry is 323,009 bytes:
a valid 157,940-byte RIFF, then a multipart-upload header, then a second valid RIFF. Godot's whole-buffer WAV
importer walks into the appended header, treats `Cont` as a chunk id with an impossible size, and loops through
`FileAccessMemory.seek` errors on the main thread.

AGE does not special-case this resource. Its WinMM decoder (`wav_decoder_open@0x488790`) descends into the
first `RIFF/WAVE`, finds `fmt ` and `data` within that parent, saves the first `data` chunk's declared size,
and `wav_decoder_fill_buffer@0x4889b0` stops after exactly that many bytes. The appended upload material and
second RIFF are unreachable.

`RiffWaveSanitizer.PrepareForGodot` now applies that first-RIFF boundary to the transient decoder buffer after
validating every declared child, then performs the existing INFO-metadata removal within that boundary.
Archive/VFS bytes remain pristine, and invalid RIFFs are returned unchanged. Synthetic concatenation and
installed-A1215 regressions prove the exact prefix and unchanged PCM payload. Validation passes 528/528 engine
tests, a zero-warning Godot build, and the Himegari-targeted threaded selftest, which loads the real A1215
buffer through Godot and reports `first-riff-boundary=ok` without seek spam.

**Stage 01-01 cumulative STEP-LIMIT corrected (2026-07-29):** after a reveal-zone event, the persistent
SYSTEM4 run reached its artificial 20,000,000-instruction cap. `SYSTEM4 P592` correctly identifies the last
ADV boundary (`SC0600@0x33fd`, followed by cleanup and return), but the page map cannot identify code that
runs after that page. The final page was recorded about 29 seconds before the halt, so the existing report
cannot distinguish an SC0600 cleanup loop from resumed FIELD processing.

Godot now retains the deepest frame chain before a halted frame unwinds and automatically emits a bounded
STEP-LIMIT report: exact script/offset/opcode, nested frame chain, hottest sites in the final 128 instructions,
and the final 16-instruction sequence. It also writes the ordinary full stall snapshot as
`user://games/<profile-id>/diagnostics/step-limit-<timestamp>.json` and copies its coordinate/path. Focused
formatter/stack
regressions made the next reproduction decisive: the cap fired at `DRAWMINIMAP@0xc4` under
`SYSTEM4 > TITLE > SAVE > SYSTEM4 > FIELD > DRAWMINIMAP`, 127.452 seconds into the run. Its final trace is
the ordinary bounded minimap scan: X increments at `0x77`, exits after 25, and the outer scan spans a fixed
101 rows. `DRAWMINIMAP` was only where the boot-to-session counter happened to reach exactly 20,000,000;
FIELD polling and redraw calls had accumulated those steps normally.

`GodotVmOptions` now gives persistent interactive runs `long.MaxValue` rather than a test-harness lifetime
ceiling. Selftest and CLI/corpus paths keep their bounded diagnostic budgets, and the automatic halt report
remains available for bounded Godot modes. The focused policy regression and all 531 engine tests pass; the
Godot build is warning-free and the Himegari-targeted threaded selftest passes.

**Numbered save/load playthrough acceptance confirmed (2026-07-29):** following the native-layout
restoration, retained-gfx, frame-continuation, script-prologue, and shared-profile lifecycle fixes, the user
has exercised repeated saving and loading throughout the live early-dungeon playthrough without another
persistence failure. This closes the generic “richer dungeon save stress” acceptance item for the current
slice. Native-layout gameplay save/load is provisionally accepted; future failures should be tracked as
specific edge cases rather than leaving the entire persistence effort unconfirmed. Extended JSON
inspection/export, mod-data namespacing, migrations, and broader platform packaging remain separate work.

**Cyclic reset implemented (2026-07-29):** `0x230(handle)` now gets or creates the retained object,
disables the four looping channels represented by the compositor, and clears the complete native
start/period block—including the preserved raw state for the currently unmodeled second cyclic matrix.
Base/current transforms, cyclic targets, and finite one-shot channels remain intact. This closes its six
sites (five DEBUGADV demonstrations and one FIELD movement setup), reducing the remaining effectful
inventory to 15 distinct opcodes / 22 instructions. All 479 engine tests, opcode build/lint, the
zero-warning Godot build, and the Himegari-targeted threaded selftest pass.

**Diagnostic-output trio implemented (2026-07-29):** `0x1b2(value)` now formats string, signed-integer,
pointer, and float operand families into an EngineCtx-lifetime accumulator; `0x1b3` appends exact CRLF;
and `0x1b4` appends the native interpreter coordinate, synchronously presents it, then clears only after
the host returns. The release image's optional debug-name/line tables are never populated, so the port
matches its deterministic `LINE=-1 COMMAND=-(436)` suffix while retaining live `FILE`, dword `ADDRESS`,
and zero-based frame `DEPTH`.

`GameSession` carries the accumulator across fresh scene VMs, preserving FIELD's unpresented invalid-state
line and SYSTEM4's post-clear newline. Interactive Godot dispatches `OS.Alert` on the main thread and parks
the VM until dismissal; headless `CaptureHost` records rather than drops the effect. Focused regressions
cover operand formatting, append/show/clear ordering, context formatting, headless presentation, and
cross-scene lifetime. All 482 engine tests, opcode/EngineCtx build and lint, the zero-warning Godot build,
and the Himegari-targeted threaded selftest pass. The six instructions are closed, reducing the remaining
effectful inventory from 15 distinct opcodes / 22 instructions to 12 / 16.

**NEXT:** rerank the remaining 12 effectful opcode gaps and investigate the widest coherent slice.

**Remaining-gap rerank and forced-BGM investigation (2026-07-29):** a fresh scan of all 481 scripts
confirms 12 effectful gaps totaling 16 instructions. `0xb7`, `0xb8`, `0x142`, and `0x24d` occur twice
each; `0xb9`, `0x137`, `0x144`, `0x149`, `0x241`, `0x248`, `0x2c6`, and `0x2c8` are singletons. Raw
frequency no longer identifies a subsystem, but native RE does: `0xb7`/`0xb8`/`0xb9` are the complete
forced-BGM lifecycle around the already-implemented `0xbf`/`0xc2` path.

`0xb7(track_or_zero)` force-restarts looping playback, including the same retained track and the zero
operand's "restart current" form; `0xb9(track_or_zero)` is the corresponding one-shot start; and `0xb8`
stops/releases and clears the retained track. Each first completes/cancels a pending BGM fade. CONFIG's
BGM029 preview needs the force-loop/stop pair, CALLBACK_LOAD uses zero to restart restored music, MMODE
stops current music on entry, and STAGECLEAR uses the one-shot form for BGM025. The existing Godot music
player and retained VM track provide the whole implementation seam; it needs an explicit loop/restart
request while ordinary `0xbf` remains same-track-idempotent.

**NEXT:** implement and test `0xb7`/`0xb8`/`0xb9` together. The likely follow-up is INPUTNAME's CP932
string/UI cluster (`0x144`, `0x2c6`, `0x2c8`), but its modal host contract should be finished only after
the bounded BGM tranche lands.

**Forced-BGM lifecycle implemented (2026-07-29):** `0xb7` and `0xb9` now force-replace the active BGM
stream with logical loop and one-shot mode respectively, including zero's native alias to the retained
track; `0xb8` stops/releases and clears that track. Ordinary `0xbf` remains idempotent for same-track
FIELD cleanup. Godot cancels deferred fades on replacement, applies the start mode through the OGG loop
flag, and makes stop release the stream rather than leaving a silent source. The same stop seam now closes
`0xc2` target zero after its blocking fade, matching native source release instead of retaining a muted
Godot stream.

All five sites now dispatch, reducing the effectful-gap inventory from 12 opcodes / 16 instructions to
9 / 11. A focused VM regression covers forced same-track restart, zero reuse, both modes, replacement,
stop/clear, and restart after stop. All 483 engine tests, opcode/EngineCtx lint, the zero-warning Godot
build, and the Himegari-targeted threaded self-test pass; the self-test now also validates OGG loop mode
and true stop/release.

**NEXT:** investigate INPUTNAME's `0x144`/`0x2c6`/`0x2c8` cluster as one modal name-entry and CP932
string slice.

**INPUTNAME string/input slice investigated (2026-07-29):** all three singleton handlers are now exact.
`0x2c6(out,string)` is Japanese-locale `_mbstrlen`; `0x2c8(out,string,start,count)` slices by CP932
multibyte-character positions while keeping lead/trail pairs intact. INPUTNAME uses them together to split
the current name into its eight script-owned display/edit cells.

`0x144(result_inout,initial_text)` is the separate native keyboard-entry button. AGE copies both strings
into bounded buffers and calls runtime host command 10. A read-only live probe resolved that callback to
`AGERC.DLL+0x1050`; the shipped DLL is now imported and annotated in Ghidra. Its command-10 resource dialog
initializes from operand 2, accepts at most 16 CP932 bytes, rejects every non-double-byte character, writes
valid accepted text to operand 1, and leaves operand 1 unchanged on cancel. This resolves the former
runtime-pointer uncertainty and also identifies AGERC as the owner of the previously studied command-8
LABEL service used by `0x140`.

The port has a bounded implementation seam: the two pure helpers use `VmOptions.NativeStringCodePage`,
while the modal op follows the existing diagnostic-message pattern (VM worker waits, Godot main thread owns
the dialog, accept/cancel signals completion). Implementing the trio removes 3 of the remaining 9 opcode
gaps and 3 of 11 instructions; no persistence or save-format work is involved.

**NEXT:** implement and test `0x144`/`0x2c6`/`0x2c8` together, including CP932 mixed-width helper cases,
the native 16-byte/full-width acceptance rules, accept/cancel behavior, and INPUTNAME's split/rejoin shape.

**INPUTNAME string/input slice implemented (2026-07-29):** the VM now dispatches all three opcodes.
`0x2c6` counts CP932 characters rather than UTF-16 units or bytes, and `0x2c8` slices encoded character
spans with native end clamping. A shared validator applies command 10's 16-byte limit first and then
requires every accepted cell to be a valid CP932 lead/trail pair.

`0x144` uses an explicit synchronous host result, preserving operand 1 on cancel and operand 2 in both
paths. Godot parks the VM worker while a main-thread modal editor owns input; invalid submissions leave
the editor open with AGERC's original error text. Headless hosts cancel safely by default. Focused
regressions cover mixed-width count/slicing, end clamping, valid and invalid names, accept replacement,
cancel preservation, and operand-2 preservation. The slice removes three gaps and three instructions,
leaving 6 effectful opcodes / 8 instructions. Validation passes 493/493 engine tests, opcode and
EngineCtx lint, a zero-warning Godot build, clean diff checking, and the Himegari-targeted threaded
`SELFTEST OK`.

**NEXT:** rerank the remaining six gaps; begin with the two-site `0x24d` and check whether adjacent
`0x248` belongs to the same native subsystem before choosing the next implementation boundary.

**MOVIE-MASK/TILED-SURFACE INVESTIGATION COMPLETE (2026-07-29):** `0x24d` and `0x248` are unrelated.
`0x248(edge)` sets the legacy mode-1 tiled-surface edge length; SYSTEM4 supplies 128, while the port's
contiguous RGBA surfaces do not need to reproduce AGE's old texture-size partitioning. It is an exact,
small state-parity implementation.

`0x24d(new_key,movie_slot,old_start,old_count,x,y,width,height,mode,movie_id,delay_ms,duration_ms)` is a
movie-driven per-pixel transition. Native retimes the MPEG to `duration_ms`, allocates a byte mask at the
requested size, and registers a blocking retained command that composites the old and new ranges through
successive movie frames. Both sites are confined to DEBUG.BIN's optional TEST.AGF pages; one uses signed
x=-184. The port's explicit F4 diagnostic route makes those pages reachable, but implementing the opcode
requires a new mask-frame/compositor seam rather than reuse of the existing scalar crossfade.

The remaining inventory is still 6 opcodes / 8 instructions because this slice changed metadata only.
`0x248` is the sensible next implementation: it is exact and closes SYSTEM4's singleton without imposing
obsolete D3D tiling. After that, investigate the two normal CONFIG sites of `0x142` before deciding whether
the developer-only `0x24d` transition justifies its larger compositor slice. The `/v2` handlers,
transition helpers, and tiled-size global are named/commented and the program is saved.

**NEXT:** implement `0x248` as retained graphics configuration/state parity, then investigate CONFIG's
two-site `0x142`; keep exact `0x24d` semantics documented for a later diagnostic-compositor slice.

**TILED-SURFACE EDGE `0x248` IMPLEMENTED (2026-07-29):** the VM now retains AGE's signed-dword tile-edge
configuration in `GfxState`. It starts at zero, SYSTEM4 sets 128, later writes replace the complete value,
and scene-context reset does not clear it. Like native, the setter does not dirty presentation or rebuild
existing mode-1 surfaces. The portable `0x249` path still uses one contiguous RGBA image, preserving every
script-visible result without reproducing the legacy D3D tile allocation.

Two focused regressions cover SYSTEM4's exact setting followed by a mode-1 texture load, native signed
replacement, and reset lifetime. The slice removes one instruction and leaves 5 effectful opcodes / 7
instructions. Validation passes 495/495 engine tests, opcode and EngineCtx lint, a zero-warning Godot
build, clean diff checking, and the Himegari-targeted threaded `SELFTEST OK`.

**NEXT:** investigate CONFIG's two-site `0x142`; keep developer-only `0x24d` for a later dedicated
movie-mask compositor slice.

**SYSTEM-MENU GUARD `0x142` INVESTIGATION COMPLETE (2026-07-29):** the opcode directly replaces
`EngineCtx.system_menu_actions_enabled`. Scene reset defaults it to one. CONFIG's first instruction writes
zero; its second and final call restores one immediately before UI/surface/SFX teardown and exit.

AGE exposes the field through `AGE:IAGEService` virtual offset `+0xb4`. The shipped AGERC menu refresher
calls it and, when false, grays seven settings/save-related native menu actions so they cannot reenter
configuration while CONFIG owns the scripted settings screen. No interpreter, renderer, gameplay, or save
consumer reads the field. The port has no AGERC native menu, so the exact implementation is a small
EngineCtx-lifetime boolean retained for parity and available to gate any future host equivalent; it should
not block game input or change persisted options. The remaining inventory stays 5 opcodes / 7 instructions
until implementation.

**NEXT:** implement `0x142` as the isolated system-menu availability guard. Afterwards rerank the four
remaining opcodes, with SYSTEM4 singleton `0x149` the likely next native investigation.

**SYSTEM-MENU GUARD `0x142` IMPLEMENTED (2026-07-29):** the VM now retains AGE's complete signed-dword
field, starts it at one, replaces it on every call, and restores one on root scene-context reload.
CONFIG's exact `0 -> 1` bracket is handled without routing the flag into game input, options persistence,
or a synthetic host service. The public retained value is available if a future frontend grows an
equivalent application menu.

Three focused regressions cover the native default and signed replacement, CONFIG's exact pair, and
root-reload lifetime. Both shipped sites are closed, reducing the remaining effectful inventory to
4 opcodes / 5 instructions. Validation passes 498/498 engine tests, opcode and EngineCtx build/lint, a
zero-warning Godot build, clean diff checking, and the Himegari-targeted threaded `SELFTEST OK`.

**NEXT:** rerank the final four gaps and investigate SYSTEM4's singleton `0x149`; keep developer-only
`0x24d` for its dedicated movie-mask compositor slice.

**SYSTEM-MENU SHOW DELAY `0x149` INVESTIGATION COMPLETE (2026-07-29):** the opcode replaces the unsigned
millisecond threshold at `EngineCtx.system_menu_show_delay_ms`; scene reset defaults it to zero and
SYSTEM4's sole call sets 1000. The exact, unobserved paired getter is opcode `0x148`.

AGE's Win32 window procedure starts a 100-millisecond timer while the pointer dwells in the top two client
rows. Once unsigned elapsed time is strictly greater than the configured threshold, with no mouse button
held, windowed ScreenMode, and no active AGERC host UI, it reveals AGERC's modeless full-width system-menu
strip. Alt reveals the same strip immediately and bypasses the threshold. This is the same settings/save
menu whose actions opcode `0x142` guards against CONFIG reentrancy, but it is unrelated to the adjacent
`0x248` graphics configuration.

The current Godot frontend has no equivalent application-menu strip. The bounded implementation is exact
state parity: retain the full unsigned dword, reset it to zero with scene context, implement the paired
getter, and expose it for a future host strip without changing game input, the script-authored right-click
menu, or persistence. This investigation changes metadata only, so the inventory remains 4 opcodes /
5 instructions. The `/v2` and `/support/AGERC.DLL` handlers, reveal/timer/dialog paths, typed EngineCtx
field, and key globals are annotated and saved.

**NEXT:** implement the exact `0x148`/`0x149` state pair. Afterwards rerank `0x137`, `0x241`, and the two
developer-only `0x24d` sites.

**SYSTEM-MENU SHOW DELAY `0x148`/`0x149` IMPLEMENTED (2026-07-29):** the VM now retains the complete
unsigned threshold, returns the same native dword through signed script integer cells, and restores zero
on root scene-context reload. SYSTEM4's 1000-millisecond setup is handled without inventing an `IHost`
effect for an AGERC strip the Godot frontend does not have.

Three focused regressions cover the exact SYSTEM4 setter/getter sequence, high-bit dword preservation, and
root-reload lifetime. The shipped setter site is closed; the unobserved getter is also available for
broader AGE compatibility. The remaining effectful inventory is 3 opcodes / 4 instructions. Validation
passes 501/501 engine tests, opcode and EngineCtx build/lint, a zero-warning Godot build, clean diff
checking, and the Himegari-targeted threaded `SELFTEST OK`.

**NEXT:** rerank `0x137`, `0x241`, and developer-only `0x24d` ×2 before choosing the next slice.

**FINAL OPCODE INVESTIGATION COMPLETE (2026-07-29):** all three remaining contracts are now
implementation-ready.

- `0x137(stack_id)` replaces one of AGE's ten integer LIFOs with an empty 0x100-dword stack. Its exact
  unobserved siblings are `0x138(stack_id,value)` push and `0x139(stack_id,out_success,out_value)` pop.
  AGE's handlers accidentally admit id 10 even though reset constructs only slots 0..9, aliasing adjacent
  EngineCtx storage; Himegari uses only id 0. The sole call is `CALLBACK_LOAD@0xf2`.
- `0x241(movie,surface,flags,delay_ms,position_ms)` is `0x236` plus
  `IMediaPosition::put_CurrentPosition` before playback configuration. CALLBACK_LOAD passes the
  per-layer stop time recorded by `0x23f`, minus one, so its shipped job is restoring the terminal movie
  frame after numbered load rather than resuming an arbitrary saved cursor.
- `0x24d` retains the already-decoded type-1 movie transition ABI. The remaining uncertainty is closed:
  AGE copies each decoded RGB24 pixel's green byte into the transition mask, then applies that byte to
  captured pixel alpha inside the supplied rectangle. Completion flips the mask to its terminal fill and
  releases the blocking command. Both calls remain DEBUG.BIN-only.

The implementation order is correspondingly clear. First land the complete `0x137`-`0x139` stack family:
it is a small VM-only analogue of the existing FIFO and closes the normal load-path gap. Next land `0x241`
with one reusable FFmpeg initial-seek primitive for video and audio, including keyframe-preroll discard.
Finish with `0x24d` as the dedicated diagnostic compositor slice: publish movie green-channel masks,
capture old/new ranges, use the software renderer as the per-pixel oracle, and fall the GPU path back while
the type-1 command is active.

The inventory remains 3 opcodes / 4 instructions because this was investigation only. Opcode, global, and
EngineCtx sources and generated references are current; `/v2` names/comments and the applied EngineCtx
fields are saved.

**NEXT:** implement the coherent `0x137`-`0x139` integer-stack family, then `0x241`, then the DEBUG-only
`0x24d` compositor slice.

**INTEGER STACK `0x137`-`0x139` IMPLEMENTED (2026-07-29):** the VM now owns eleven safely isolated,
handler-addressable LIFO slots. They are constructed at VM scene entry, reconstructed empty on whole-root
scene reload, and individually replaced by `0x137`; `0x138` pushes signed dwords and `0x139` pops in LIFO
order. Empty pop writes success zero while preserving the prior value destination instead of leaking
native's reused internal EngineCtx pointer. Slot 10 is intentionally independent logical state rather than
AGE's adjacent numeric-glyph-table corruption.

Three focused regressions cover independent LIFO ordering and signed values, reset plus empty-output
behavior, safe slot 10, pre-reset availability, and root-reload lifetime. `CALLBACK_LOAD`'s shipped
`0x137` is closed, while unobserved `0x138`/`0x139` are implemented for wider AGE compatibility. The
remaining effectful inventory is 2 opcodes / 3 instructions: singleton `0x241` and DEBUG-only `0x24d` x2.
Validation passes 504/504 engine tests, opcode/global/EngineCtx build and lint suites, the zero-warning
Godot build, clean diff checking, and the Himegari-targeted threaded `SELFTEST OK`.

**NEXT:** implement positioned movie playback `0x241` with synchronized FFmpeg initial seek, then finish
the DEBUG-only `0x24d` green-channel movie-mask compositor.

**POSITIONED MOVIE `0x241` IMPLEMENTED (2026-07-29):** the opcode now uses the existing nonmodal
movie-to-surface ownership and stop-time metadata path while passing its fifth operand through a dedicated
initial-position host seam. FFmpeg ABI v3 seeks the independent video and audio format/decoder pipelines
before worker start. Video keyframe preroll selects the frame active at the requested position and retains a
later decoded frame for normal pacing; EOF after preroll therefore publishes the terminal frame for
CALLBACK_LOAD's native `stop_time_ms-1` call. Audio preroll drops earlier blocks, sample-trims a block that
straddles the position, and rebases the remaining timestamps to the same playback-zero origin.

Focused regressions cover exact opcode/host dispatch and retained graph stop time, runtime initial-position
and watchdog propagation, active-frame selection, subsequent pacing, terminal-frame retention, synchronized
audio trim/rebase, and a real VFS `CHAPTER.AGF` seek proving both native demuxers reach the requested
11-second neighborhood. Ordinary `0x236` and modal `0x20f` continue to open at position zero. The remaining
effectful inventory is one opcode / two DEBUG-only instructions: `0x24d`.
Validation passes 511/511 engine tests, the complete 213/213 installed-movie corpus gate, opcode/global/
EngineCtx build and lint suites, the zero-warning Godot build, clean diff checking, and the
Himegari-targeted threaded `SELFTEST OK`.

**NEXT:** implement the DEBUG-only `0x24d` green-channel movie-mask compositor.

**MOVIE-MASK TRANSITION `0x24d` IMPLEMENTED (2026-07-29):** the final effectful opcode now dispatches
all twelve operands through a dedicated host request. The old/source retained range is captured into the
script-created scratch surface with the existing platform-neutral retained rasterizer. Each due TEST.AGF
frame publishes its logical green channel as a byte mask, and the scratch surface applies AGE's exact
packed-ARGB alpha multiplication inside the signed/clipped destination rectangle while preserving captured
RGB and every pixel outside the rectangle. Mode 1 runs from zero to 255; other modes run from 255 to zero.

Movie playback retains ordinary instance ownership and VFS/FFmpeg decode, while the mask path redirects
frame publication away from visible movie RGB. Start delay parks first-frame consumption, stop/duration
retiming scales decoder presentation deadlines, and the terminal frame is held to the requested endpoint.
The type-1 command remains blocking until decoder completion and cannot be click-completed through the
type-0 transition shortcut. Completion publishes the exact terminal fill before opcode `0x21c` resumes.
The resulting scratch surface is an ordinary dynamic RGBA image, so software and GPU presentation share
one correctness path.

Focused regressions cover exact VM dispatch, transition lifetime, click immunity, green extraction,
dimension padding, signed clipping, RGB/outside preservation, the native packed-color carry, requested
decoder retiming, and real VFS TEST.AGF metadata/green-mask decode. The effectful opcode inventory is now
empty. Validation passes 518/518 engine tests, the zero-warning Godot build, opcode/global/EngineCtx suites,
clean diff checking, and the Himegari-targeted threaded selftest.

**NEXT:** return to playthrough-led polish and save compatibility testing; no decoded effectful opcode gap
remains in the shipped 481-script corpus.

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
Its linked-table shapes are 18 SKINIT and 82 EBINIT populated columns. The next EB tranche identifies roster
state flags, per-action unlock requirements, SCJUMP event ids, normal/brainwashed unit variants, and SALLY's
per-unit bonus item. Combat tracing also names the shared column-8 item/skill modifier as critical chance.

The message-table tranche adds a reusable extractor for both shipped global-id control-flow layouts and
joins player-facing ITMES/SKMES/VIMES/EIMES/CIMES text back to INIT records. All 287 item ids, 131 skill
ids, 65 glossary topic ids, and 24 character-profile ids match exactly in both directions; all 192 sparse
EIMES ids resolve to EBINIT unit definitions. Joined records retain dispatch and message-body offsets,
rendered text, and furigana. Title-bearing tables expose title/description, EIMES preserves its untitled
lines as enemy summary and strategy, and CIMES preserves its untitled multiline body as `biography`.
The dispatch keys establish `current_item_id`, `current_skill_id`, `current_glossary_topic_id`,
`current_enemy_encyclopedia_unit_id`, and `current_character_profile_id` as high-confidence shared index
slots.

The follow-up closes the three smaller MES-named scripts. MAINIT now extracts eleven records in its
reserved 30-cell layout and joins MAMES descriptions for ids 1 through 9; its two growth rituals have no
authored MAMES branch. INFOMES is a text-free 32-by-4 first-handler-wins extension registry initialized
with CIMES/EIMES/VIMES, while MES renders and clears a generic caller-populated modal line buffer plus an
optional annotation buffer shared with SBUNKI. These are stable runtime ABIs, not three additional
translatable table shapes.

VIINIT is now extracted as a sparse 200-by-3 glossary definition table: 65 populated names, one required
one-based seen-scene decision for every topic, and a second alternative prerequisite for thirteen topics.
INFOVO evaluates those prerequisites against persistent scene-decision seen flags before listing a topic.
INFOEN likewise gates EBINIT's enabled enemy-information rows through persistent encounter/reveal flags.
The generated VIINIT profile reports 65/65 VIMES coverage, while EBINIT reports 192/277 EIMES coverage;
the remaining EBINIT rows are valid player/unused unit definitions rather than missing EIMES records.
CIINIT defines 24 rows in four reserved 100-cell columns: displayed names, backing EBINIT unit ids,
optional portrait resources, and default-zero portrait placement offsets. INFOCH consumes that registry,
and the generated CIINIT profile reports complete 24/24 CIMES biography coverage.

The follow-up correlation pass makes that evidence directly queryable with
`init_table_profile.py --message-query REGEX` and moves confirmed item/skill row-column meanings into
structured `globals.toml` metadata. Extracted JSON and profiles now resolve raw keys to names such as
`item_stat_modifiers.critical_chance` while preserving the original address key. The message audit confirms
all populated condition columns and the female-only item mask. CHMENU also resolves the last anonymous
SKINIT parallel field as the skill-change catalog inclusion flag and separates acquired-skill state from
skill-information visibility. The remaining item/skill work is refinement rather than an unnamed populated
schema.

EBINIT's populated schema is now fully named. Its apparent final two sparse runtime-table writes were flat-
address overlap collisions: they are the already-established battle-sprite asset for unit 456 and starting
level for unit 600, not distinct stride-300 fields. Enemy AI appears to live outside the static EBINIT
schema. The signed boss class is now behaviorally separated: all nonzero values
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

CCINIT is the fifth extracted shape rather than a failed name-table parse. Its 71 conditional
class-change rules cover 33 EBINIT units and expose unit/level/applied-slot predicates plus the selected
title, deployment-cost delta, fourteen-stat bonuses, SKINIT skill awards, and state slot set. The shipped
program contains 69 named promotions, 30 skill awards, and two level-independent empty-title Lily
form-adjustment rules; EVOLVE directly queries those form effects while CALCCC/ADDEXP establish the normal
promotion apply/report protocol. The input, working-output, and persistent destination globals are named
as one class-change ABI in `vm-map/globals.toml`, and generated rules carry both raw address provenance and
the shared `semantic_fields` join. SCINIT is now the sixth shape: 2,179 paired assignments produce a
1,209-row SCJUMP decision-to-packed-scene registry plus authored chapter metadata, with all script ids
resolved and the full overwrite history retained. RTINIT is now the seventh shape: its twenty parallel
1000-by-20 banks produce 172 routine sets, 1,043 movement steps, and fourteen battle steps with RTN_M/RTN_B
provider joins, activation/progress/story gates, six explicit reserved banks, and complete overwrite
history. Provider-tagged decoding now covers movement providers 1, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13,
14, 15, 17, 51, and 52, plus providers 2, 9, and 61: progress-only steps, randomized roaming,
stage-object slots, coordinate destinations (including M012's
foreign-entity route mask), enemy/ally and Magic Pillar searches, resource-gated Healing Feather
selection, collectible-treasure seeking, cyclic waypoints, faction-traversable terrain, retreat,
normal-attack routing, immediate element-effective target/action selection, and immediate allied
healing. This covers all 1,043 steps across all nineteen used providers and classifies every one of the 977
authored movement-parameter cells: 974 are semantic inputs, while the three cells attached to M001/M008
are retained as provider-unread residue; thirteen unwritten zero defaults are projected separately. The
three dispatchable providers absent from the shipped RTINIT table remain outside the generated join.

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

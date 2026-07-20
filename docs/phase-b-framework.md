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

**TITLE SFX investigation (2026-07-20).** Hover and activation callbacks are already executing their
scripted `0xb5` starts. The load fails earlier because op `0xb4` uses universal packed SYS4INI/AAI ids,
while Godot currently treats them as active-script manifest ids. TITLE's `0x2aea`/`SE020.WAV` hover and
`0x3321`/`SE015.WAV` activation loads therefore resolve null; BGM is unaffected. The next bounded correction
is a packed-raw SFX resolver plus TITLE/GAMESTART regression coverage, with no mixer redesign indicated.

## Stage B1 — Persistent session and scene coordinator

Replace the single-SC0000-root assumption with an application-owned session that runs SYSTEM4 as its root.
SYSTEM4's computed `call-script` is the authoritative scene coordinator: child scenes return to that frame,
while globals, the host, and intentional retained state remain owned by the same live VM session.

Required responsibilities:

- Own global integer/string banks and any proven external/profile state across scenes.
- Preserve the SYSTEM4 root while distinguishing ordinary child frames from scene-boundary children for
  diagnostics and lifecycle assertions; do not perform host-driven top-level replacement.
- Define scene-owned versus session-owned host state and tear each down at the correct boundary.
- Preserve intentional system-owned surfaces, configuration, and audio while releasing scene-local state.
- Expose deterministic transition evidence: outgoing scene, reason/decision, incoming scene, and state
  summary suitable for tests.

Completion evidence: SYSTEM4 reaches a computed child script in one VM, the child returns to SYSTEM4,
selected globals and system-owned state survive, and script-owned boundary cleanup releases scene-local
presentation state.

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

## Later Phase B breadth

Once the natural spine and first gameplay loop are trustworthy, broaden in independent tracks:

- Remaining title/configuration/load/save branches.
- Save-file format and restoration of a persistent session.
- Map navigation, camera, terrain, deployment, and turn lifecycle.
- Unit statistics, equipment, inventory, skills, magic, heroine forms, and progression.
- Combat resolution presentation, enemy turns/AI services, and win/loss transitions.
- A full chapter of ADV and the scene types encountered between gameplay segments.
- `STINIT` and other data schemas when their runtime consumers make them necessary.

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

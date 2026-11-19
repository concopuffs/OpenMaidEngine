# Adding a game

**Status: accepted 2026-11-19.** Written from a survey of how Kamidori was added (roadmap phases K0 to K6, the
engine layering spec §7.8) and a first read-only look at 創刻のアテリアル (Arterial, AGE 4.38D). The user's
decisions on acceptance: two tracks, with "supported" meaning P3 and M3; the revision-diff triage belongs to P3,
though that leaves Kamidori an open gap; Himegari stands at P3; and the first-look command and the one-folder
install layout come before Arterial, the other tooling gaps as Arterial meets them.

This is the checklist for bringing a game into OME, and the levels that say how far a game has come. It assumes the
game's SYS generation is one the engine already runs, which today means SYS4. §2 says how to tell. The first game of
another generation (SYS3, SYS5) needs a design of its own before these steps apply.

The engine part of the work (dump, compare, triage, declare the build) is the engine layering spec's §7.8
(`superpowers/specs/2026-11-12-engine-layering-design.md`). This document puts it in order with everything else a
game needs, and gives each step a gate.

## 1. Support levels

A game has two levels, one on each track. They move separately: Kamidori has its base mod and data files while its
comparison with native AGE stands at its third stage, and a single ladder would either hide that or force an order.

**Play: how well the port runs the game.**

| Level | Name | What is true | Gate |
|---|---|---|---|
| P0 | Reference | The install is kept to compare engines. Nothing of it runs in the port. | Its engine is dumped with hashes, imported in Ghidra and pinned in `tools/operand_access.py` `ENGINES`, and `revision_diff.py` has compared it with the nearest build. |
| P1 | Recognized | The engine names the install and reads all of it. No script runs, and nothing is saved. | A profile, an engine build and an ABI snapshot exist. `validate.py --level workspace --game <id>` passes: the whole catalog decodes with no failure, and the registry's observation equals a fresh scan. The opcodes without a handler are counted and listed. |
| P2 | Boots | The game goes by its own boot route to the title, a new game and the first ADV page, with no opcode skipped. | The natural boot test passes. One opening scenario passes under `drive.py` on native AGE and on the port. `validate.py --level full --game <id>` has a terminal gate for the game and passes. |
| P3 | Complete | Every opcode its scripts use has a handler, its engine build's rules are declared, and it saves. | `catalog-scan` reports no unsupported opcode. Every row of the revision diff has a disposition. The port loads a native save and native loads a port save, and `save-diff` shows only expected differences. The profile's `writesEnabled` is true. |
| P4 | Verified | The game has been driven to its ending on native AGE and on the port alike. | The sweep (step 8) is done, with each difference fixed or listed in `backlog.md`. |

**Modding: what a modder gets.**

| Level | Name | What is true | Gate |
|---|---|---|---|
| M1 | Scripts | Script mods and text packages work. | `oms-rebuild --game <id>` passes (rebuilt scripts run like the originals). `omp check --game <id>` links a sample package. The game has its guide (`modding/games/<id>.md`) with install options, fingerprints and first-mod values, and a row on the generated games page. |
| M2 | Data | The game's data tables are decoded, documented and named. | `docs/<id>-data-tables.md` covers every `*INIT` table in the inventory. The registry `vm-map/globals-<id>.toml` names their globals and declares the main record tables (`[[table]]`, `[[scope]]`). |
| M3 | Base mod | Lua mods and data files work. | `mods/<id>-base/` ships its layout, events, the `<id>.tables` data kind and at least one helper. Its tests bind on every install the guide lists, and an example mod runs under the driver. |

M1 needs P2, because the rebuild gate runs scenes. M2 needs P1. M3 needs M2 and P3, because its events are proven
in real play.

**"Supported" means P3 and M3.** P4 is "verified". A release that carries a game below P3 says so.

**A level includes its documents.** A game is not at a level until the references the level touched are written
(`AGENTS.md`, "Mandatory reference closeout"). There is no separate "documented" level.

**Where a level is recorded.** Nothing in a profile manifest says how far its game has come, and the launcher reads
the same registry, so it cannot tell a P1 game from a P3 one. The proposal is a `support` entry in the manifest
(`play`, `modding`), which the generated games page shows and the launcher warns from (`backlog.md`, "Game
onboarding"). Until that is built, this table holds the levels:

| Game | Engine build | Play | Modding | Open |
|---|---|---|---|---|
| Himegari | 4.24J | P3 | M3 | It has not been driven to its ending on native and the port alike (P4). |
| Kamidori | 4.33G | P3, but for the diff triage | M3 | 125 revision-diff rows against Himegari are not triaged; the sweep stands at the third stage (`backlog.md`). |
| Arterial | 4.38D | P0 | none | `backlog.md`, "Onboard 創刻のアテリアル". |

## 2. Before starting

**Is it the same SYS generation?** Three checks, each read-only. `Ome.Cli intake --game-root <install>` runs the
first two on an install that has no profile yet, and exits 0 when they pass (`tools-reference.md`, "Engine"):

- the catalog signature is `S4IC…` and the scripts' magic is `SYS4…`, with the 60-byte header;
- every script decodes with the whole opcode catalog: no opcode outside it, no unknown operand type;
- the dispatch-table finder locates the context constructor in the dump (step 2).

If one fails, the game is outside this checklist: a new instruction model or a new container is engine design, not
onboarding. One opcode missing from the catalog is not such a failure, though it fails the decode check until it is
added: its operand count is read from its native handler and added with `opcodes_build.py --bootstrap`.

**Which build is nearest?** The supported build with the closest `AGE.EXE` version. The new build is declared as
that build plus changes (step 7), and the revision diff is run against it.

**What is the install?** Record the `AGE.EXE` version resource and SHA-256, the catalog revision, the title, and
which appends are present. Do not call an install official or patched without evidence. Keep it read-only: every
native run uses a scratch copy.

## 3. The steps

Each step lists what to do, what it is done with, where its facts go, and its gate.

### Step 1: intake

- Give the game an id, a short ASCII name (`arterial`), and put its install at `games/<id>/` beside the repository.
  Every tool finds a game's install by that rule, so no table needs an entry.
- Run `Ome.Cli intake --game-root ../games/<id> --output <report.json>`. It needs no profile. It reports:
  - the install's identity, its `AGE.EXE` version and hash, and the header's bank sizes;
  - whether the catalog reads, and any block of it the reader does not know, as bytes;
  - the settings keys and file types that no supported game's install has;
  - the opcodes its scripts use, those without a handler with their sites, and those no supported game uses.
- **Facts go to:** `PROJECT-STRUCTURE.md` (the install), `engine-re.md` (the build table in "Comparing engine
  builds"), `sys4-format-notes.md` (anything the catalog or script format does differently), and the game's
  backlog item (the counts).
- **Done when** the intake's checks pass and what it lists as new is written down.

### Step 2: dump the engine and compare it (P0)

- Make a scratch copy (`native_scratch.py`) and dump the unpacked module from it (`frida/dump_engine.py --spawn`,
  which redirects the shell folders so the real saves are untouched). Check the install is unchanged
  (`native_scratch.py --check`).
- Import the dump in Ghidra as `/<id>/<build>/range_00400000.bin`: raw binary, `x86:LE:32:default`, base
  `0x400000` (`engine-re.md`, "Runbook"). Restore the packer's API sites and type the imports. Port names, plates
  and comments from the nearest build's image through a function alignment, and name the opcode handlers from the
  registry. An image without names makes every later step slower.
- Pin the build in `operand_access.py` `ENGINES`: the context constructor, the opcode table's offset, the accessors
  (`--align-accessors --align-to <nearest>`) and `runtime_start`.
- Run `revision_diff.py <nearest> <id>`. It gives the opcodes only the new engine has, the shared opcodes whose
  native code differs, and the shared helpers that differ.
- **Facts go to:** `engine-re.md` (the build's row, hashes, what the largest differences are), the Ghidra project.
- **Done when** the diff report exists and the image carries the ported names.

### Step 3: profile, catalog and opcode inventory (P1)

- Make the catalog load. A catalog revision can add to `SYS4INI.BIN`. The engine's reader refuses what it does not
  know, and only the intake reads past it, so a new block is reversed and documented before anything else runs.
- Write the profile, `engine/Ome.Engine/Profiles/<id>.json`: identity, script revision, `engineBuild` and
  `engineAbiId`, the boot script, and persistence with `writesEnabled` false. `expectedPackedSaveVersion` is the
  catalog's `SAVEVERSION`. `bankDimensions` are the six saved-prefix counts at header `+0xf0`
  (`sys4-format-notes.md`, "SYS4INI file header").
- Declare the build in `engine/Ome.Engine.Vm/EngineBuilds.cs` as the nearest build with its own id, ABI and save
  stamp, and no other change yet.
- In `vm-map/opcodes.toml`, add the id to `profile_ids`, and add the game's `[[observation]]` and its
  `[[abi_layer]]` snapshot. The snapshot claims no parent: it is what this game's scripts use. The intake report's
  `opcode_occurrences` gives the first opcode list, before any table for the game exists.
- Scan the whole catalog (`Ome.Cli catalog-scan --game <id> --output
  build/games/<id>/catalog-opcode-coverage.json`). It gives the opcode set, the opcodes without a handler with
  every site and operand shape, and the per-operand type census.
- Derive operand access from the dump (`operand_access.py --game <id>`, then `--check-corpus`, then `--apply`).
  This is where an operand read as a float in the new build shows up, with no reading by hand.
- Add the game's workspace tests (`[Trait("Category", "Workspace")]` and `[Trait("Profile", "<id>")]`): the
  install selects the profile, the header's counts equal the manifest's, the observation equals a fresh scan.
- Extract what the Python tools read: `parse_sys4ini.py` (asset index, call-script names), `extract_phase2.py`
  (disassembly and text), `compiler_census.py` (the compiler's output shapes, which Open Maid Script must already
  know).
- **Facts go to:** `opcodes.toml`, `sys4-format-notes.md`, `tools-reference.md` if a tool changed.
- **Done when** `validate.py --level workspace --game <id>` passes and the inventory is written into the game's
  backlog item: how many opcodes lack a handler, in how many sites.

### Step 4: triage what is new and what changed

Three lists come out of steps 2 and 3. Each needs a different amount of work.

| List | From | Work |
|---|---|---|
| Opcodes the game's scripts use that have no handler | `catalog-scan` | Each must be implemented for P3. Order them by the boot route first, then by sites. |
| Shared opcodes and helpers whose native code differs | `revision_diff.py` | Triage each row (below). Start from the helpers: one helper explains many opcode rows. |
| Opcodes only the new engine has that no script uses | both | None. They stay catalog entries. |

Each differing row gets one disposition (layering spec §7.2, §7.3, §7.7):

- **noise**: one context field or one helper call, usually a correspondence gap;
- **not modelled**: code the port replaces whole (file loaders, Direct3D device handling);
- **the same in practice**: one implementation is right for every input either build produces, with the reason in
  the registry;
- **real**: a registry fact (an operand contract), a handler variant, or a rule named for the behaviour and set in
  the build's definition.

Read a low-scored row too when its fact is a compare or a bit test in the handler itself. Kamidori's op `0x215`
difference scored 4 and broke an event picture.

Also settle each new settings key and each new file type from step 1: implement it, or record why the port does
not need it.

- **Facts go to:** `opcodes.toml` (evidence scoped to the build), `engine-re.md` (native findings), the Ghidra
  image (names and plates as you read).
- **Done when** every row has a disposition and every real one has a place to go. The work itself is steps 6 and 7.

### Step 5: drive native AGE

The driver is needed before the port boots: it shows what native does, and it makes the native saves step 7 needs.

- Write `tools/drive/<id>.toml`: the canvas points (advance, title, new game), the sandbox's settings, and the
  native facts: the context global, the frame-record offsets, the input-polling setter and its call's return, op
  `0x60`'s `rand` return, the build's hash, and the dialogs to answer. Translate the nearest game's addresses
  through the revision diff's correspondence (its JSON keeps the function, field and address maps), then check
  each in Ghidra.
- **Done when** `drive.py --game <id> native start` reaches the title in a sandbox, `state` reads VM positions,
  `shot` captures, `rand-seed` reports draws, `stop` closes the game its own way, and the install checks
  unchanged.

### Step 6: boot (P2)

- Run the port's natural boot in strict mode. It halts at the first opcode without a handler. Implement the
  opcodes in the order the boot reaches them.
- For each opcode: read its handler in the game's own image and annotate it there; write the registry entry with
  evidence scoped to this build; add one row to `VirtualMachine.Handlers.cs` and the handler; test it with
  synthetic data and at one installed site; run `opcode_flow.py --apply` and `opcodes_build.py --build`.
- Behaviour goes where the layering spec puts it: a handler, a native, or a device. No check of a build's or a
  game's name (`EngineBuildTests`).
- Reach the title, a new game and the first ADV page. Add the game's case to the natural boot tests and its
  terminal gate to `validate.py --level full`. Write one opening scenario (`tools/drive/scenarios/<id>-….steps`)
  and compare its screenshots and VM positions on both targets.
- **Done when** P2's gate passes.

### Step 7: complete the opcodes, the build and the saves (P3)

- Implement the remaining opcodes from step 4's first list.
- Put each real difference from the triage into the build's definition in `EngineBuilds.cs`, as the nearest build
  plus named changes, each with a comment that points to its evidence.
- Settings: bind the build's `SYS4REG.INI` keys (`SettingsRules`), and compare the game's `AGERC.DLL` menu and
  dialogs with the shell the port draws.
- Saves: make a native save with the driver. Read its identity, version stamp and record sizes. Round-trip it
  through the save codec. Then seed each target's sandbox from the other (`drive.py --seed`), load on both, and
  compare decoded saves with `Ome.Cli save-diff`. Set `writesEnabled` only after that.
- **Done when** P3's gate passes.

### Step 8: verify against native (P4)

Two passes, the cheap one first.

- **Targeted scenarios.** For each real difference, and each new opcode that draws, sounds or waits, find a script
  site (`catalog-scan` lists them), write a short scenario that reaches it, and compare the two targets.
- **The sweep.** Drive both targets through the same steps with `rand-seed`, and compare screenshots, VM
  positions, draws per call site and decoded saves at each checkpoint. Keep the route, the save slots and the
  click coordinates in a checkpoint memory, and the coverage in the game's backlog item.
- **Done when** the story has been driven to its ending on both targets, with each difference fixed or listed.

### Step 9: data (M2)

This step is static and can start once P1 is reached.

- Inventory the scripts by role: scenes, systems, `*INIT` tables, message tables, calculators.
- Extract each `*INIT` table with the shared shape modes (`extract_init.py`). Run the boot's table chain through
  the port's VM (`init_image.py`) for the merged image across the appends; this needs the handlers those scripts
  use. Add the game's own extractors where a shape mode is not enough (`tools/init_<id>*.py`).
- Build the auto shape map (`global_map.py`), mine story flags (`story_flags.py`), and decode `SCJUMP` if the game
  has one (`scjump_decode.py`).
- Name what is understood in `vm-map/globals-<id>.toml`, declare the record tables and scopes, and run
  `globals_build.py --game <id> --build`. For each field, say whether the game copies it into saved state.
- **Facts go to:** `docs/<id>-data-tables.md` (layouts and how the data is stored), the registry, and the generated
  `docs/global-reference-<id>.md`. Each new document gets a row in `AGENTS.md`'s map.
- **Done when** M2's gate passes.

### Step 10: modding (M1, then M3)

- **Scripts (M1).** Pass `oms-rebuild --game <id>`. Write the ADV module `oms/<id>/adv.oms` (speakers and the
  dialogue idioms) so the idiomatic rebuild passes too. Name patch points in `vm-map/patch-points-<id>.toml`.
  Export the text catalogs and run one language package. Write `docs/modding/games/<id>.md`, add the launcher's
  art entry to the profile, and run `patch_points_build.py`, then `modding_games_build.py --build`.
- **Base mod (M3).** Survey the places the game's own appends hook (its handler chains). Write
  `mods/<id>-base/`: the manifest, the events (boot data layered and ready, the stage events, the experience
  award, and what the survey found a use for), `lua/tables.lua` for the `<id>.tables` kind, and at least one
  helper over a wait-free game script. Run `base_mod_build.py --build`. Test that each event binds on every
  install the guide lists, and run an example mod in the port under the driver.
- **Done when** M1's and M3's gates pass.

### Step 11: close

- Bring every living reference the game touched up to date (`AGENTS.md`'s map), and regenerate what is generated.
- Record the game's levels (§1) and say them in the release notes.
- Update the status memory and add a changelog entry.

## 4. Order

The steps are numbered in the order they unblock each other, not in one line:

- Steps 1 and 2 need only the install. Step 2 is the long pole of P0 and can be done long before the rest.
- Step 3 needs step 2 only for operand access. Steps 4 and 5 need steps 2 and 3.
- Step 9 needs step 3 and can run beside steps 5 to 8.
- Step 10's script part needs step 6. Its base mod needs steps 7 and 9.
- Step 8 is the longest and never blocks a release at P3.

## 5. What is settled without a playthrough

The revision diff exists so that a new game does not need a full playthrough to find what its engine changed. It
does not replace the sweep. What each method can settle:

| Settled by reading (static) | Settled by a short driven scenario | Left to the sweep |
|---|---|---|
| An opcode's operand count, types and access. | Anything drawn or timed: text layout, transitions, effects, waits. | Code no opcode reaches: the frame loop, the renderer, input, audio mixing. The diff compares it only through shared helpers. |
| A new opcode whose handler is self-contained (data, arrays, strings). | A difference whose effect depends on state the scripts set up. | Systems acting on each other, and script paths that depend on draws and branches. |
| Constants and tables, settings keys and their defaults, save stamps and record sizes. | A new opcode's lifecycle: skip, fast-forward, a scene reset, a save in the middle. | Port faults that are no revision difference at all. Most of what Kamidori's sweep found was of this kind. |
| Catalog and script format changes. | | |

## 6. What a game adds or edits

| Area | Files |
|---|---|
| Identity and build | `engine/Ome.Engine/Profiles/<id>.json`, `engine/Ome.Engine.Vm/EngineBuilds.cs`; the install is `games/<id>/`, outside the repository |
| Opcodes | `vm-map/opcodes.toml` (`profile_ids`, `[[observation]]`, `[[abi_layer]]`, evidence), `VirtualMachine.Handlers.cs` and the handlers, `tools/operand_access.py` `ENGINES` |
| Driving and checks | `tools/drive/<id>.toml`, `tools/drive/scenarios/<id>-*.steps`, `tools/validate.py` (the game's test filter and terminal gate), the game's workspace tests |
| Data | `vm-map/globals-<id>.toml`, `tools/init_<id>*.py`, `docs/<id>-data-tables.md` |
| Modding | `oms/<id>/adv.oms`, `vm-map/patch-points-<id>.toml`, `mods/<id>-base/`, `docs/modding/games/<id>.md`, examples |
| Generated | `docs/global-reference-<id>.md`, `docs/patch-point-reference-<id>.md`, `docs/modding/games/README.md`, `docs/opcode-reference.md`, the base mod's `layout/`, record map and shape |
| References | `engine-re.md`, `sys4-format-notes.md`, `PROJECT-STRUCTURE.md`, `tools-reference.md`, `AGENTS.md`'s map, `backlog.md`, the Ghidra project, the status memory and changelog |

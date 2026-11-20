# Tools Reference

Living catalogue of every script in `tools/` — **what it does, how to run it, and what it
reads/writes**. This is the operational companion to `docs/PROJECT-STRUCTURE.md` (which is the
*where-things-live* map); when they overlap, PROJECT-STRUCTURE owns layout, this file owns
usage + I/O. Keep it current: **add a row here whenever you add a tool, and update the row
whenever a tool's inputs/outputs change.**

## Conventions (apply to every tool)

- **Run with** `py -3.11 -X utf8 tools/<name>.py …` — the `-X utf8` is required on Windows so
  cp932/Shift-JIS source text renders (and generated files stay UTF-8).
- **Paths are never hard-coded.** Every tool imports the immutable context in `tools/paths.py`.
  Common `--game`, `--game-root`, and `--extracted-root` options may appear anywhere on a tool
  command line; the equivalent child-process variables are `OME_PROFILE`, `OME_GAME_ROOT`, and
  `OME_EXTRACTED_ROOT`. Omitting them preserves the conventional Himegari sibling roots. `--profile` is the
  same option as `--game` under its old name (the last of either wins), and the names match exactly: a prefix
  such as `--game-r` is not read as a common option.
- **Generated files are never hand-edited** (they're marked ⚙ below). Edit the source, re-run
  the generator.
- **`build/` and extracted roots are disposable** — engine-wide ABI outputs remain directly under
  `build/`; game-derived outputs live under `build/games/<game>/`. In the tables below, an
  unqualified game-derived `build/...` path is shorthand for that selected profile directory.

## Path anchor

| Tool | Purpose | I/O |
|---|---|---|
| `paths.py` | ★ Shared immutable tool context — validates ids against the embedded runtime profile manifests; resolves common command-line/environment/conventional roots (a game's conventional install is `games/<game id>/` beside the repository, by rule: `conventional_game_root`; a reference-only install is listed in `REFERENCE_INSTALLS`, which is empty since Arterial got its profile); separates shared `build/` from `build/games/<game>/`. `paths.scripts()` is the extracted/loose analysis view: extracted `DATA1/`, then extracted append catalogs `APPENDnn/` (literal `$n$` record names), then root-loose files (`$n$` records included, as native AGE mounts them), each matching `.BIN` in any letter case as Windows does (the Kamidori fan translation ships lowercase `.bin`). `ToolContext.globals_toml` / `.global_reference_doc` name the per-game global registry (`vm-map/globals.toml` + `docs/global-reference.md` for Himegari, `vm-map/globals-<game>.toml` + `docs/global-reference-<game>.md` otherwise). New whole-catalog scans use the runtime store. | *Imported, not run.* |
| `test_paths.py` | Pure profile/root precedence, round-trip environment, default-compatibility, output-isolation, per-game registry naming, and append-catalog / root-loose `$n$` shadowing script-map regressions. | temporary files only |

## Project validation

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `validate.py` | Layered project validation front door. `core` remains content-free; besides the Python tests, the .NET build and tests and the launcher solution's
(`launcher/OmeLauncher.sln`, restored and tested by gates of their own) it checks every profile's registries whichever profile is selected (each global registry linted and its `build/games/<game>/globals.json` written by `globals_build.py --build-json`, which the tests' OMS modules load and a checkout lacks; each patch-point registry and its reference by `--check-reference`), and the generated opcode files, engine-ctx reference, modding games page, base mod layouts (`base_mod_build.py --check`) and the launcher's icon (`launcher_icon_build.py --check`) against their sources. `workspace` selects one installed profile and asks of it what its support level promises (`adding-a-game.md` §1). For every game it runs only that game's traited .NET gate (`Profile=<id>`; Himegari's are the tests no other game's trait claims) and decodes its complete production catalog through `Sys4AssetStore`. For a game with a global registry it checks the generated global reference against its auto shape map (`globals_build.py --check`, which needs `build/games/<game>/global-var-map.json`). For a game at M1 or above by its manifest's `support` entry it runs the Open Maid Script canonical-rebuild gate (`Ome.Cli oms-rebuild`, plus `--level idiomatic` when the profile has source modules in `oms/<game>/`); Himegari additionally runs its current extracted-data globals/Python/RECOVER gates. `runtime` adds Godot. `full` combines all phases with the selected profile's proven terminal gate: Himegari's faithful-wait direct-scene sweep, Kamidori's natural SYSTEM4/title/New Game/first-ADV-page acceptance, or 創刻のアテリアル's natural boot to SC0000's first page (its P2 gate). The selected game writes only its own coverage and logs. The game root is the one `paths.py` resolves (`--game-root`, then `OME_GAME_ROOT`, then the conventional install); prerequisites, gates and their child tools all use it. | `validate.py` (defaults to `--level full`) · `--level core|workspace|runtime|full` · `--game <id>` · `--game-root <install>` · `--extracted-root <root>` · `--godot <console>` · `--verbose` · `--fail-fast` | sources + selected prerequisites → console summary + ⚙ `build/games/<game>/validation/validate-*/<gate>.log` + ⚙ `catalog-opcode-coverage.json` |
| `test_validate.py` | Pure tests for Godot explicit/environment/PATH resolution precedence, invalid-explicit hard failure, the game root taken from the tool context even when `OME_GAME_ROOT` names another install, level composition (every profile's registry gates in core, the global-reference check before the rebuild), and final gate ordering. | `test_validate.py` | temporary files only |

Levels are cumulative around `core`: `workspace` means core+workspace-corpus, `runtime` means core+Godot,
and `full` means every phase. `workspace`/`full` intentionally require the disposable generated inputs named
by a failed preflight; rebuild each through its owning tool in this reference. Runtime Godot resolution uses
`--godot`, then `OME_GODOT_CONSOLE`, then `godot4`/`godot`/`godot-mono` on `PATH`. Game-root resolution uses
`--game-root`, then `OME_GAME_ROOT`, then the selected profile's conventional sibling install and always
requires `SYS4INI.BIN`. For example, independently analyze both installs with:

```powershell
py -3.11 -X utf8 tools/validate.py --level workspace --game himegari
py -3.11 -X utf8 tools/validate.py --level workspace --game kamidori
```

The hosted wrapper is `.github/workflows/core-validation.yml` (GitHub Actions). One read-only job on the pinned
`ubuntu-24.04` image provisions Python 3.11 and the SDK selected by `global.json`, reports both, and runs only
`--level core`. Every action is pinned to a full commit SHA with its release in a trailing comment;
`.github/dependabot.yml` proposes weekly grouped pin bumps against `develop`. A standalone source-only repository
under an arbitrary directory name passes that exact driver without sibling game or extracted data. Repository
discovery uses tracked marker files rather than requiring the checkout directory to be named `age-reimpl`. The
workflow has no secrets, private corpus, Godot runtime, cache, packaging, or deployment access; checkout does not
persist credentials, and failure logs are retained for seven days. `develop` is the default and tracked
development branch; pushes and pull requests targeting it, plus manual dispatch, select the gate (`pull_request`,
never `pull_request_target`). The same driver owns the constrained-memory .NET environment rather than relying on
workflow-only settings, so local and hosted core/workspace runs share the Roslyn memory contract. The gate ran on
the former Gitea host from 2026-08-03 until the 2026-10 move to GitHub; that history is recorded in
`docs/remake-architecture-and-roadmap.md`.

The artifact wrapper is `.github/workflows/release-build.yml` (GitHub Actions). It runs independently from the
fast core gate on `develop` pushes, manual dispatch, and `v*` tag pushes. Independent Linux and Windows jobs on the
pinned `ubuntu-24.04` image use the same Python 3.11 and `global.json` .NET setup actions and call their locally
accepted build commands unchanged. The Windows job adds Ubuntu's MinGW-w64 GCC/binutils and never installs Wine.
Each job has its own manifest-keyed `actions/cache` entry with no prefix fallback: shared Linux Godot editor plus
only that target's hash-verified FFmpeg archive and selectively installed release template. Linux uploads its
`.tar.gz`, external SHA-256, `BUILD-INFO.json`, `SHA256SUMS`, and dynamic package-smoke log. Windows uploads its
ZIP, external SHA-256, `BUILD-INFO.json`, `SHA256SUMS`, and `WINDOWS-VERIFICATION.json`; it does not execute the
EXE. Both artifacts are retained for 30 days and failures retain bounded evidence for seven days. Both build jobs
inherit the workflow's read-only `contents: read` permission and have no secrets, token, private game corpus, or
release publication authority. Every action is pinned to a full commit SHA, as in the core gate.

Only a successful `v*` tag run after both platform jobs enables the dependent promotion job. It downloads the
two verified workflow artifacts into separate directories rather than rebuilding, checks out only the release
tool, and is the only job granted `contents: write` (which covers releases on GitHub); branch and manual runs
skip it and never receive write authority. It passes the run's built-in `github.token` as `GITHUB_TOKEN`, so no
personal token or third-party release action is required. `publish_github_release.py` validates the
version-like tag, both exact clean-build commit/target records, both external archive hashes, matching
metadata/ledgers inside each archive, the accepted Linux package smoke (its three lines: the engine's, the
launcher's check and the hand-over), and the Windows AMD64/ABI/import
verification. It also requires each ledger to list the launcher, and the Windows report's `launcher` section to
record an AMD64 windowed program with AMD64 natives (launcher plan §9.3). Then it generates one `RELEASE-SHA256SUMS`. Before touching any release it resolves the tag through
GitHub's git-ref API (dereferencing annotated tags) and requires that commit to equal `--target`; GitHub's
`target_commitish` is not trusted because it can name a branch. It then creates a **draft** release (or resumes
the single matching draft — drafts are invisible to GitHub's release-by-tag lookup, so they are found in the
release list; more than one is refused), uploads only missing assets, deletes and re-uploads any placeholder left
by an interrupted upload (asset `state` other than `uploaded`, drafts only), verifies the exact three-asset set
with sizes and `uploaded` state, and only then publishes and marks the release latest. A published release is
never modified: an exact, complete one is an idempotent success; anything else is refused. Mismatched release
metadata, unexpected assets, and same-name/different-size collisions are refused rather than edited or
overwritten. Draft-first publication keeps releases compatible with GitHub's immutable-releases setting. The
artifact and promotion route ran on the former Gitea host from 2026-08-03 (first dual-job run, `v0.2.0`
acceptance) until the 2026-10 move to GitHub; that history is recorded in
`docs/remake-architecture-and-roadmap.md`.

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `publish_github_release.py` | Tag-only, retry-safe promotion of paired verified Linux/Windows workflow artifacts to a GitHub release. Every release description ends with the FFmpeg credit, license, and exact source link, read from `dependency-linux-x64.json`. Revalidates both archives/evidence sets, checks the tag's commit via the git-ref API, writes combined release checksums, and publishes exactly three assets through a draft → upload → verify → publish flow. A resumed draft keeps an existing asset only when GitHub's `digest` (`sha256:…`) matches the verified file; an asset with no digest is replaced rather than trusted by size, and one of another size or digest is refused. The final check also compares each digest GitHub reports. Requires `GITHUB_TOKEN` (`contents: write`); the token is passed to the upload `curl` through standard input rather than its argument list. | `publish_github_release.py --repository <owner/name> --tag <v...> --target <sha> --linux-artifact-directory <dir> --windows-artifact-directory <dir> --output-directory <dir>` | two verified downloaded workflow artifacts + tag context → published GitHub release with Linux archive, Windows archive, and `RELEASE-SHA256SUMS` |
| `test_publish_github_release.py` | Pure synthetic paired-artifact/archive validation plus a GitHub-faithful fake API: draft creation then publish-after-verify, draft resume uploading only missing assets, replacement of a resumed asset with no digest, refusal of a resumed asset with equal size and another digest and of a published release whose digest differs, interrupted-upload placeholder replacement, idempotent completed release, refusal of incomplete published releases, tag/target mismatch, multiple drafts, metadata/collision/unexpected-asset refusals, annotated-tag dereferencing, the token never appearing in the upload command line, and archives whose ledgers or Windows report leave out the launcher, or whose smoke log lacks one of its three lines. | `test_publish_github_release.py` | temporary files only |
| `test_write_ffmpeg_source_notice.py` | Source-only checks that both real manifests yield a complete FFmpeg source offer (version, LGPL 2.1, hosted URL and SHA-256, upstream, configure line, correct spelling), and that missing fields, another license, or a missing `BUILD-CONFIG.txt` are refused. | `test_write_ffmpeg_source_notice.py` | dependency manifests, temporary files → assertions only |
| `test_release_workflow.py` | Source-only policy regression for the independent read-only Linux/Windows jobs, target-specific MinGW/cache/artifact boundary, no-Wine/no-Windows-smoke rule, the Linux build's check of every smoke line, promotion's tag gate, sole `contents: write` grant, `github.token` use, and dependency/download of both successful artifacts; the FFmpeg manifests' exact GitHub release URLs; and, across every `.github/workflows/*.yml`, full-SHA action pins with version comments, the pinned `ubuntu-24.04` runner, no `pull_request_target`, and no `.gitea/` directory. | `test_release_workflow.py` | `.github/workflows/*.yml`, FFmpeg manifests, Godot project files → assertions only |

## Optional local binary tools

`bin/` is an ignored machine-local tool directory, not a source or artifact distribution directory. A local
`BinExtractALF.exe` plus its matching `LzssCpp.dll` may be used by the installed-data archive parity test and to
regenerate `../extracted/`; neither is a runtime dependency. Their known local hashes and unverified provenance
are recorded in `bin/README.md`. PE-sieve is obsolete and is not retained; its historical experiment remains in
`docs/engine-re.md`.

## Container parse / disassemble

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `sys4load.py` | Loader + opcode-decoding disassembler for SYS4 `.BIN` scripts (the container-format core every other tool builds on). Annotates global operands (`build/games/<game>/globals.json`) and **`call-script` targets by name** (`build/games/<game>/callscript-names.json`, e.g. `call-script 0x1ab =ADDITEM.BIN`). | `sys4load.py <file.BIN>` · `--summary` · `--strings` · `--json` · `sys4load.py <dir> --validate` (corpus check) | `.BIN` + `age_opcodes*.py` + `build/games/<game>/globals.json` + `build/games/<game>/callscript-names.json` → stdout listing, or `build/games/<game>/scripts-json/` with `--json` |
| `age_opcodes.py` | ⚙ Generated Python ABI view: the complete opcode label/argument-count catalog, operand-type labels, control-flow target operands, and inline-array opcode. **Do not hand-edit.** | *Imported.* | `vm-map/opcodes.toml` → generated module |
| `compiler_census.py` | Read-only census of the original compiler's output, which a faithful compiler must reproduce. It reports: T1/T2/T3 derivability; string sharing, data order and unreferenced data; `jcc` lowering shapes (`if`, `if`/`else`, `for`, `while`, conditional goto, braced `if { goto }`); statement-block balance and transfer-versus-reference crossings; how call, callback, coroutine and switch targets terminate; local-bank slack and temporary-pool position; negative-constant idioms; and position-independent fingerprints of local subroutines shared across scripts. Every count in `sys4-format-notes.md` "Compiler-emitted structure" comes from it. | `compiler_census.py [--game ID] [--output PATH]` | corpus (`paths.scripts()`) → ⚙ `build/games/<game>/compiler-census.json` + stdout summary |
| `test_compiler_census.py` | Pure tests of the census classifiers on synthetic instruction streams: the lowering shapes, block crossing and balance, subroutine termination, and position-independent helper fingerprints. | `test_compiler_census.py` | — |

## Opcode reference toolchain — single source of truth = `vm-map/opcodes.toml`

All opcode catalog framing, per-game corpus observation, ABI snapshot/layer composition, semantic grounding,
structured evidence provenance, per-operand access contracts, and `depends_on` knowledge is hand-edited **only** in
`vm-map/opcodes.toml`. Operand access is the exception to hand-editing: `operand_access.py --apply` writes it from
the native handlers. So are an implemented opcode's control facts, which `opcode_flow.py --apply` writes from the
port's handlers. Observation is independent from
the revision-unscoped upstream catalog, semantic status, and engine-owned runtime implementation coverage.
Everything else is generated from those owners.

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `opcodes_build.py` | Generator + linter for the opcode reference. It validates observation profile ids against embedded manifests, observation opcode references/count metadata, ABI layer parents/cycles/contracts, structured evidence methods/scopes/artifacts/sites/profile/revision references, and semantic dependencies. Current `SYS4422`/`SYS4433` layers are complete direct snapshots; the schema can later compose evidence-backed parents with additions, full operand-contract replacements, and removals. `--bootstrap` appends framing skeletons without inventing a game observation; `--bootstrap-age` adds revision-unscoped upstream-catalog compatibility entries. It describes the registry only: which opcodes the port runs is the engine's handler table (`engine/Ome.Engine.Vm/VirtualMachine.Handlers.cs`), and `catalog-scan` reports a corpus's opcodes without a handler. The lint rejects two opcodes with one name (semantic name, else label), since the handler table binds by name. `--check` lints and exits nonzero when a tracked generated file (`tools/age_opcodes.py`, `tools/age_opcode_semantics.py`, `docs/opcode-reference.md`) differs from what `--build` would write; it writes nothing and is the core validate gate, so it passes on uncommitted but regenerated files. | `--build` · `--check` · `--lint` · `--bootstrap` · `--bootstrap-age` | `vm-map/opcodes.toml` → ⚙ `tools/age_opcodes.py`, ⚙ `tools/age_opcode_semantics.py`, ⚙ `build/opcodes.json`, ⚙ `docs/opcode-reference.md`, ⚙ union `build/opcode-coverage.md`, ⚙ `build/games/<game>/opcode-coverage.md` |
| `opcode_flow.py` | Derive every implemented opcode's control facts from the port's handlers (Lua scripting spec §7.1): `waits` (`never`, `may`, `always`) and `leaves` (any of `falls`, `jumps`, `calls`, `ends`, `saves`, `loads`, `restores`). The link reads them to refuse a `skip`, to warn about an `after` hook across a wait, and to check the scripts a mod calls. It reads the handler table (`engine/Ome.Engine.Vm/VirtualMachine.Handlers.cs`) and every method of the interpreter's partial classes with comments and literals removed, and follows each handler through the interpreter's methods and the methods it passes as arguments (a wait's callback). It stops at `RunFrame` and `RunInstructions` (`calls`), and at the script-error box (`ShowScriptError`), whose wait and outcome are an error's: it ends the run or skips the instruction, as a halt does, and a script call refuses it before it waits; a step result counts in a method that returns one; `_waits.X` waits when the wait native's `X` reaches its blocking helpers (`InputAndWaits.Blocking.cs`, the one place the engine blocks), unless `engine/Ome.Engine.Vm/ModCallWaits.cs` lists `X` under `NotWaits`: a present and the front end's modal hold block and are not waits (Lua spec §10); the save native's entry points give `saves`, `loads` and `restores`. A fact reachable on some path is given. `always` is given only to the handlers its `ALWAYS_WAITS` lists (the two modal boxes), since Skip, Auto and fast-forward pass the other waits. The engine's tests check every instruction they run against `leaves` (`VirtualMachine.OpcodeFlow.cs`, on through the `Ome.Engine.CheckOpcodeFlow` AppContext switch the test project sets). `--why NAME` prints the methods behind one opcode's facts. `--check` is a core `validate.py` gate. | (none) · `--apply` · `--check` · `--why NAME` | `engine/Ome.Engine.Vm/VirtualMachine*.cs`, `engine/Ome.Engine/Natives/InputAndWaits*.cs`, `engine/Ome.Engine.Vm/ModCallWaits.cs` → ⚙ `build/opcode-flow.json`; `--apply` → `vm-map/opcodes.toml` (`waits`, `leaves`) |
| `test_opcode_flow.py` | Core tests for `opcode_flow.py`: the C# reading (literals, interpolation holes, comments, block and expression bodies) and the derived facts of control flow, the waits and their callbacks, and the save native. | `test_opcode_flow.py` | — |
| `opcodes_model.py` | In-memory catalog/observation/evidence/ABI-layer model + profile/revision/provenance/composition/dependency linter. It owns the operand-access vocabulary (`ACCESS_KINDS`). The linter requires every operand of an observed opcode to carry an `access` list, or an `access_by_abi` list for each observed ABI, requires code-target operands to be read, and requires every opcode with code targets to name their `code_target_kind` (`CODE_TARGET_KINDS`: `transfer`, `call`, `callback`, `coroutine`). It also lints each operand's `unchecked_index`, the native table the operand indexes without native keeping it inside (`UNCHECKED_INDEX_KEYS`: `table`, `count`, optional `first`, `admits`, `signed`), which the OMS compiler warns from. | *Imported by `opcodes_build.py`.* | `vm-map/opcodes.toml` → — |
| `test_opcodes.py` | Pure regressions for general observation loading, structured evidence resolution/lint, runtime/JSON/reference views, and bootstrap behavior. Its synthetic scan uses the canonical Himegari observation set but never reads private game content. | `test_opcodes.py` | `vm-map/opcodes.toml` → temporary files only |
| `opcode_context.py` | Read-only evidence gatherer for classifying unnamed opcodes (frequency, argc, operand-type signature, neighbours, disassembly snippets, canonical registry note). | `--top 20` · `opcode_context.py 0x1f4 0x71 …` | `vm-map/opcodes.toml` + corpus → stdout |
| `validate_opcode_table.py` | Definitive decode-coverage validator using the canonical registry's generated ABI and the SYS4 code/data boundary. | `validate_opcode_table.py` | corpus → stdout |
| `validate_opcode_table_naive.py` | Naïve variant of the above (baseline comparison). | `validate_opcode_table_naive.py` | corpus → stdout |
| `age_opcode_semantics.py` | ⚙ Investigated AGE opcode semantic-name view, without implying Himegari ownership — **generated; do not hand-edit.** | *Imported by `sys4load.py`.* | `vm-map/opcodes.toml` → `SEMANTICS` |
| `globals_build.py` | Merge the selected profile's curated registry over its auto shape map, preserve optional machine-readable row-table `columns`, and generate the global registry + linter. Global addresses are per game, so each profile owns its own registry (same schema): Himegari `vm-map/globals.toml`, Kamidori `vm-map/globals-kamidori.toml`. Entries are keyed by bank and address (a `string…` type is a string-bank cell); the linter rejects a cell defined twice. The JSON keeps the address-keyed `globals` view (the integer cell wins where banks share an address) and adds `cells`, keyed `G:0x…`/`GS:0x…`, which Open Maid Script loads as `<game>.globals`. The linter also warns about fully sized arrays of one bank whose cells overlap, since the decompiler names a cell by the smallest array containing it (spec §11.3). A registry's `[[table]]` entries (`name`, `records`, `built_by`, `usage`, `[table.fields]` mapping each field to a curated global by name) declare Open Maid Script tables (spec §5.3). `built_by` names the part of the game that writes the table (`boot` for INIT2's layers, `stage` for a stage's loaders; data patches spec §6.3), and the base mods' record maps copy it. The build rejects a table without that label, and a field whose global is missing or is not a fully sized 1-D or 2-D array with `records` entries or rows, and emits them as the JSON's `tables` list and a Tables section of the reference doc. Himegari and Kamidori declare `unit`, `item`, `skill` and `stage_enemy`. A `[[scope]]` entry (`name`, `first` and `last`, the range of its ids, `usage`, and `[scope.tables]`, each short name to a table) keys tables by a scope of the game: both registries key `stage_enemy` as `enemy` under `stage`, ids 1 to 999, so a data file writes `stage.12.enemy.3` (data patches spec §6.2). The linter refuses a scope named like a table, a table's alias or a global, a range that is not `0 <= first <= last`, a short name that names no table, and a table under two scopes; the JSON lists them as `scopes`, and the reference doc's Tables section shows each. A global's `aliases` (lowercase identifiers) keep the names it had before a rename: the linter refuses an alias that is another global's name or alias, and the reference doc shows them as "formerly" names; `base_mod_build.py` exports them beside the name. A `[[table]]` keeps its old names the same way, in its `aliases`, and its fields' in `[table.field_aliases]` (`old = "current"`; data patches plan §9.1), which a data file may still write: the linter refuses a table's alias that is a table's or a global's name or another table's alias, and an old field name that is a field of the table or names none; the JSON's `tables` carry them, and the reference doc shows them as "formerly" names. | `--build` · `--build-json` (lint, then write only the JSON; without the auto map it holds the curated globals, and the tracked reference doc is left alone: the core level runs it for every profile) · `--lint` · `--check` (lint, then fail if the reference doc differs from what `--build` writes; needs the auto map, so it is a workspace gate) · `--auto-map <json>` · `--game ID` | `vm-map/globals.toml` / `vm-map/globals-<game>.toml`, selected `build/games/<game>/global-var-map.json` → ⚙ selected `build/games/<game>/globals.json`, ⚙ `docs/global-reference.md` / `docs/global-reference-<game>.md` |
| `patch_points_build.py` | Validate and generate the selected profile's named patch points (mod loader spec §5): stable fragment site names (`before site himegari.experience.after_calculation { … }`), each with one site per script revision. Names are dotted lowercase identifiers led by the profile id. For the installed revision of the point's script (the bytes the catalog serves), the site must be an instruction start; a point that lists no site for the installed revision is a warning, since it may describe another game revision. `Ome.Cli oms-patch` loads the JSON when it exists and resolves dotted site names through it; the plan keeps the point name beside the resolved offset (`point`, `through_point`). | `--build` · `--check` (validate against the install) · `--check-reference` (validate the registry's own form without the install, then fail if the reference doc differs from what `--build` writes; a core gate) · `--game ID` | `vm-map/patch-points-<game>.toml`, installed scripts → ⚙ `build/games/<game>/patch-points.json`, ⚙ `docs/patch-point-reference.md` / `docs/patch-point-reference-<game>.md` |
| `modding_games_build.py` | Generate the modding guide's supported-games page: one row and one identity block per profile (its support levels from the manifest's `support` entry, profile id, titles, engine build, engine ABI, catalog revision, boot script, conventional install, port saves, OMS modules and `enum speaker` counts, registry tables with record and field counts, named patch points, the publisher's page from the manifest's `launcher` entry, and the link to
`docs/modding/games/<game>.md`). Every input is a repository source, so the page does not depend on which installs exist. A profile without its hand-written guide is a warning. `--check` is a core `validate.py` gate. | `--build` · `--check` | `engine/Ome.Engine/Profiles/*.json`, `vm-map/globals[-<game>].toml` `[[table]]`, `vm-map/patch-points-<game>.toml`, `oms/<game>/*.oms`, `tools/paths.py` → ⚙ `docs/modding/games/README.md` |
| `base_mod_build.py` | Generate each game's base mod layout (Lua scripting spec §9.1) for every profile with a hand-written `mods/<game>-base/manifest.toml`: `layout/globals.json` (each named global's bank, address, dimensions, columns and aliases; the engine builds a mod's accessors from it), `layout/points.json` (the patch points, validated without the install, in `patch-points.json`'s form, so an exported build carries them) and `layout/exports.txt` (the exported names: the base mod's API: the globals, then each record table as `table NAME` and its fields as `field TABLE.FIELD`, an old name as `OLD = NAME`); and the record map `lua/generated/tables.lua` (data patches spec §6.3), the one generated file under `lua/`: a Lua module with each `[[table]]`'s record count and `built_by` label, and for each field its global, its kind (`integer`, `float` or `string`), a row's `width` and named `columns` (from 0), and `state = true` where the field's array ends at or below its bank's saved prefix (`persistence.bankDimensions` in the profile manifest), so that a load brings its value back; and the old names, `old_tables = { old = "current" }` beside `tables` and `old_fields` in a table, each only when there is one; and from the same map the kind's shape `shapes/tables.schema.json` (data patches spec §7, plan §6.2), JSON in the shape subset that the link checks each `<game>.tables` file against and an editor reads as draft-07 JSON Schema: the top names each table and scope, a table's records are keyed by decimal id (`x-ome-decimal-keys`), a record names its fields, an integer field takes -2^31 to 2^32 - 1, a row its columns by number or by name, and a state field, a scoped table's own name and a named column's number are marked not offered (`{ "type": "null", "description": WHY }`); old names take their current name's schema, and the definitions are `records.<table>`, `record.<table>` and `scope.<scope>`, each schema's description first and the names sorted. A registry's `[[scope]]` entries (`globals_build.py`) give the map `scopes = { stage = { first = 1, last = 999, tables = { enemy = "stage_enemy" } } }` after `tables`, and `exports.txt` a `scope stage` and a `scope stage.enemy` line after the fields. A field whose array straddles a prefix, a 1-D field with column names, a registry with tables but a profile without bank dimensions, and a table no scope reaches whose `built_by` label builds a scoped table (ome-records writes such a label only through its scopes, so the table would be checked and never written; data patches plan §12.6) are errors. Only repository sources are read. The rename rule: a name the checked-in `exports.txt` lists stays exported, as a global's name or one of its `aliases` (`vm-map/globals*.toml`), until the base mod's major version is raised; a change that breaks it is refused. A record table's name and its fields' names follow it, kept as the table's `aliases` or in its `[table.field_aliases]`; a field under a table's old name is the field under its current one, and a dropped table's error stands for its fields. A scope and its short names have no old-name form, so one that leaves needs a raised major version, and a dropped scope's error stands for its short names. Type shapes: `int[N]`, `int[R][C]`, `int[RxC]`, and an open first dimension (`int[][C]`, `int[unit][C]`). `--check` is a core `validate.py` gate. | `--build` · `--check` | `vm-map/globals[-<game>].toml`, `vm-map/patch-points-<game>.toml`, `mods/<game>-base/manifest.toml`, `engine/Ome.Engine/Profiles/<game>.json`, the checked-in `exports.txt` → ⚙ `mods/<game>-base/layout/`, ⚙ `mods/<game>-base/lua/generated/tables.lua`, ⚙ `mods/<game>-base/shapes/tables.schema.json` |
| `test_base_mod_build.py` | Core (content-free) tests for `base_mod_build.py` and the registry's `aliases`: the type shapes, the layout's form, columns, shape errors, the rename rule (refused, kept by an alias, a raised major version, a major version that goes down), the record map (kinds, rows and columns, state at and beside the saved prefix, a Lua keyword as a key, and its errors), the alias lint, the old names of tables and fields (in `exports.txt`, the record map, the registry's JSON and reference, their lint, and the rename rule over them), and the shape (the top's tables and scopes, a scoped table's name and old name not offered, records and records' keys, each field's kind and range, state fields and their old names, named and unnamed rows, a scope's ids and short names, key order, and a registry without tables), and a table no scope reaches built by a scoped table's label. | `test_base_mod_build.py` | — |
| `launcher_icon_build.py` | Draw the launcher's icon from the OME icon (`godot/assets/ome-icon.svg`, which the port draws itself, and which Godot's Windows export draws into `OME.exe` from the preset's `application/icon`): one `.ico` at 16, 20, 24, 32, 40, 48, 64 and 256 pixels, the window's icon and the one the build embeds in `OME-Launcher.exe` (`ApplicationIcon`). Pillow draws the SVG at 8 times each size and reduces it with premultiplied alpha. Only what the icon uses is read (`rect` with `rx`, a stroked `circle`, and `path` with absolute M, L, A and Z, in `#rrggbb` or `none`); anything else stops the build. `--check` needs no Pillow: it compares the SVG's SHA-256 with the one `--build` recorded, and is a core `validate.py` gate. | `--build` (needs Pillow) · `--check` | `godot/assets/ome-icon.svg` → ⚙ `launcher/Ome.Launcher/Assets/ome-icon.ico`, ⚙ `ome-icon.source.json` beside it |
| `test_launcher_icon_build.py` | Core tests for `launcher_icon_build.py`: the path reading (lines, an arc's points on its radius, the icon's own path, an unknown command refused) and the check against the recorded hash. | `test_launcher_icon_build.py` | — |
| `test_patch_points.py` | Core (content-free) tests for `patch_points_build.py`: validation against synthesized script bytes and instruction offsets, each rejection reason, the other-revision warning, the generated reference, and `--check-reference` (no catalog lookups, stale and malformed registries). | `test_patch_points.py` | — |
| `story_flags.py` | Static story-flag miner (branch-condition mining) + `--bootstrap` skeleton seeding. | `story_flags.py` · `--bootstrap` | corpus, `build/games/<game>/global-var-map.json` → ⚙ `build/games/<game>/story-flags-candidates.json`, appends the selected profile's registry (`vm-map/globals[-<game>].toml`) |
| `test_globals.py` | Unit tests for the globals registry + story-flag miner, including the `--check` staleness comparison over a synthetic registry and auto map. | `test_globals.py` | — |
| `scjump_decode.py` | Decode SCJUMP's progression logic → decision table; `--verify` VM cross-check. | `scjump_decode.py` · `--verify` | SCJUMP.BIN, `build/games/<game>/globals.json` → ⚙ `build/games/<game>/scjump-decisions.{json,md}` |
| `test_scjump.py` | Unit tests for the SCJUMP decoder. | `test_scjump.py` | — |

## Extraction / data corpora

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `extract_phase2.py` | Batch: disassembly + text corpora for every script. | `extract_phase2.py` | corpus → `build/games/<game>/disasm/*.asm`, `build/games/<game>/text/{dialogue.jsonl,strings.jsonl,*.strings.txt}`, `build/games/<game>/manifest.json` |
| `extract_message_table.py` | Discover repeated global-id dispatch chains in either fallthrough-body (ITMES/SKMES/MAMES) or branch-target-body (VIMES/EIMES/CIMES) form, reconstruct player-facing text and furigana, preserve guard/body offsets, and emit an ID-keyed message table. Title-bearing tables expose `title`/`description`; EIMES exposes its untitled lines as `summary`/`strategy`; CIMES exposes one untitled multiline `biography`; MAMES exposes untitled `description` text. | `extract_message_table.py <MES> [OUTNAME]` | `<MES>.BIN` → `build/games/<game>/data/<OUTNAME>.json` |
| `extract_init.py` | Parse a `*INIT` data source (auto-detects name / numeric / footer / mixed / rules / dispatch / banked shape). Name tables infer their reserved record span, preserve sparse one-based runtime ids, distinguish lookup bases from first written cells, statically evaluate direct and negative-value writes, and separate parallel `fields` from linked row-major `record_fields`; when flat global ranges overlap, a parallel base established by other records wins over a coincidental row-table cell calculation. Dedicated name-mode schemas recover VIINIT's pre-title glossary prerequisites, CIINIT's profile registry, and STINIT2's corrected 74-row stage catalog plus six-line pre/post-clear text matrix, availability gates, geometry, rewards, SCJUMP joins, and loader resources. Footer-mode MPINIT recovers its sparse 53-column doubled-coordinate terrain atlas, joins 66 STINIT2 stage rectangles, and resolves terrain ids through LAINIT. Mixed tables recover selector-dispatched records, condition strings, scalars, preallocated buffer cells, consumer-confirmed strides, and length-prefixed footer arrays; STINIT additionally assembles confirmed buffers into `object_placements` and `enemy_spawns`, joins object type names and available descriptions from OBINIT, and exposes consumer-proven tagged payloads (teleport, treasure, card, initial faction, non-triggering hazard/barrier faction, and OBINIT-gated initial object state), preserves engine-dead type-27 writes under `ignored_payload_fields`, and exposes reinforcement schedules and first-clear-only enemy gates. Rules mode decodes CCINIT's source-ordered class-change predicates and output accumulators, joining EBINIT unit names, SKINIT skill names, titles, named stat bonuses, cost deltas, and applied-state slots. Dispatch mode decodes SCINIT's paired decision-to-packed-scene and authored-chapter arrays, preserving source-order overwrites, resolving SYS4INI script names, and cross-checking chapter tags against decoded SCJUMP paths. Banked mode decodes RTINIT's twenty parallel 1000-by-20 movement/battle routine banks, retains all assignments and overwrites, assembles final per-slot steps, resolves provider selectors to RTN_M/RTN_B scripts, and applies selector-scoped schemas where proven. Current schemas cover all nineteen used providers, RTN_M001/002/003/004/005/006/007/008/009/010/011/012/013/014/015/017/051/052/061, including progress-only, randomized roaming, object-slot, coordinate, enemy/ally, treasure and Magic Pillar search, Healing Feather, waypoint, faction-terrain, retreat, reachable normal-attack routing, immediate offensive target/action selection, and immediate allied healing. Parameterless behavior schemas publish an empty `parameter_fields` mapping while still joining `provider_behavior`, target selection, action selection, and completion rules. Provider defaults such as M004's unwritten slot 0, M010's zero/HP resource index, and M013's any-foreign-faction filter are projected explicitly and counted separately from populated source cells. Authored cells proven unread by M001/M008 remain under per-step `ignored_movement_parameters` and contribute to top-level `ignored_movement_parameter_count`; they are not silently discarded or assigned invented meanings. Raw provider banks remain intact, and top-level `movement_provider_parameter_schemas` documents the projection. ITINIT, SKINIT, VIINIT, EBINIT, and CIINIT join their matching MES text. Top-level `field_semantics` maps raw keys to canonical global/column names, while each record's `semantic_fields` is the generated name-keyed join; complete footer copies map to a row and expose its values without the raw provenance wrapper. Raw keys and unresolved tagged payload cells remain provenance. OUTNAME accepts a stem or one `.json` suffix and rejects paths. Refreshes the generated data index. All table-specific schemas named above are Himegari's and apply only to the Himegari profile (`profile_specializations()`); other profiles get their own module (Kamidori: `tools/init_kamidori.py`) or the shared shape modes, and the address-bound `rules`/`banked` detectors never fire for them. Overlapping consumer-observed row tables resolve to the narrowest stride. `--append N` extracts the literal `$N$<TABLE>` append record (trusted root-loose copy first, else `<extracted>/APPENDnn/`) to `APPENDnn-<TABLE>.json`, joining the matching `$N$<MES>` when present. | `extract_init.py <TABLE> [OUTNAME] [--mode …] [--append N] [--packed-id 0x…] [--game ID]` | `<TABLE>.BIN` plus EBINIT/SKINIT for CCINIT, OBINIT for STINIT, ITINIT/SCINIT and `build/games/<game>/callscript-names.json` for STINIT2, STINIT2/LAINIT for MPINIT, CDINIT2/STINIT for CDINIT, ITINIT/SCINIT/ILINIT and `build/games/<game>/callscript-names.json` for CDINIT2, BTANINIT/SKINIT/ITINIT and `build/games/<game>/callscript-names.json` for BTANINIT2, BTANINIT2 and `build/games/<game>/callscript-names.json` for BTANINIT, `build/games/<game>/callscript-names.json` for RTINIT/SCINIT, `build/games/<game>/scjump-decisions.json` for SCINIT, matching `<MES>.BIN` when supported, and `build/games/<game>/globals.json` → `build/games/<game>/data/<OUTNAME>.json`, `build/games/<game>/data/README.md` |
| `init_table_profile.py` | Build the static investigation surface for an extracted name/numeric/footer/mixed/rules/dispatch/banked table: per-scalar/string/array-cell/footer-array population and value distributions, representative records, and direct opcode/script consumers. MPINIT's footer specialization summarizes atlas geometry, implicit-zero rows, terrain ids, stage-rectangle joins, sharing, and border cells; STINIT2 summarizes corrected record/text geometry, mapped/event/main/EX populations, story gates, SCJUMP/loader joins, rewards, and its unresolved column; CDINIT summarizes selector/list sizes, CDINIT2 joins and story gates, STINIT references, and the runtime scan/clear geometry; CDINIT2 summarizes card-type and effect populations plus item/event/condition/visual join coverage; BTANINIT/BTANINIT2 summarize effect modes/resources, six-slot timeline geometry, delays, duration, skill/weapon joins, and unreferenced authoring. Name tables report message coverage for title/description, summary/strategy, biography, and description-only layouts; rule tables summarize covered units, titled and level-independent rules, threshold/slot distributions, and skill awards; dispatch tables summarize assignments, overwrites, script-name resolution, and SCJUMP chapter agreement; banked tables summarize populated/reserved banks, movement/battle steps and providers, selector-specific semantic coverage, and overwrite conflicts. `--message-query REGEX` searches every supported message field beside all populated fields for semantic correlation. Findings are evidence only; confirmed meanings go in `vm-map/globals.toml`. | `init_table_profile.py <TABLE> [--build] [--limit N] [--message-query REGEX]` | `build/games/<game>/data/<TABLE>.json` + corpus → stdout; with `--build`, `build/games/<game>/data/<TABLE>-field-profile.{json,md}` |
| `test_extract_init.py`, `test_init_table_profile.py` | Regression checks for sparse one-based, mixed selector-dispatched, CCINIT rule, SCINIT dispatch, RTINIT banked, ILINIT condition-matrix, CNINIT unit-name/voice-family, CGINIT gallery, ALINIT alchemy-recipe, AFINIT affinity/progression, CTINIT name-entry-palette, CVINIT character-voice, TRINIT training-action, CDINIT card-generation, CDINIT2 card-effect, BTANINIT/BTANINIT2 battle-animation, STINIT2 stage-definition, and MPINIT terrain-atlas extraction; RECOVER ABI validation; MES reconstruction/joins; footer-array accounting and semantic projection; EBINIT/SKINIT/OBINIT definition joins; STINIT object/enemy joins and tagged-payload behavior; overwrite/provider joins; and field/message/rule/dispatch/banked/map/training/card/battle-animation/stage profiling. | run each directly | — |
| `global_map.py` | Build the partial global-variable name map from static evidence. | `global_map.py` | corpus + `build/games/<game>/data/` → `build/games/<game>/global-var-map.{json,md}` |
| `init_kamidori.py` + `init_kamidori_{characters,items,alchemy,maps,progression}.py` | Kamidori (SYS4433) table specializations for `extract_init.py`: `specializations()` returns `{forced_modes, extractors}` keyed by `(mode, TABLE)`. Every extractor decodes a fixed absolute layout (destination minus array base = record id, `divmod` by stride for row tables), so the same function reads the base table and each `--append N` layer without rebasing. Domain modules: **characters** — CCINIT (class-change rule blocks, `rules`), CNINIT, CIINIT (+ CIMES join), CVINIT; **items** — ITINIT/SKINIT (fixed-layout, + ITMES/SKMES joins), ILINIT, AFINIT (one record per tuning sub-table), RTINIT, BTANINIT, BTANINIT2; **alchemy** — ALINIT, BUINIT, IMINIT, SHINIT, HAINIT, CSINIT, SIINIT (town sites); **maps** — MPINIT (doubled-coordinate floor grids; `render_ascii()` debug view), STINIT, STINIT2, LAINIT, OBINIT, COINIT, AMINIT, AMINIT2, DIINIT, ARINIT; **progression** — QUINIT, FAINIT, VIINIT (+ VIMES join), CGINIT, SPINIT, CDINIT, CDINIT2, SCINIT (joined to the SCJUMP contexts of every non-stub layer), EVINIT, EUINIT. Shared static-write walkers live in `init_kamidori_common.py`. Decoded values use semantic keys (`attributes`, not raw hex `fields`), so `field_semantics` and `init_table_profile.py` have no raw columns to profile for these tables; EBINIT and the INIT/INITCONFIG drivers stay on the shared shape modes. Table layouts are documented in [kamidori-data-tables.md](kamidori-data-tables.md). Himegari's address-bound specializations stay in `extract_init.py` and apply only to the Himegari profile. | *Imported by `extract_init.py`.* | `<TABLE>.BIN` or `$N$<TABLE>.BIN`; CCINIT also reads EBINIT/SKINIT names, CDINIT reads CDINIT2 names, SCINIT reads SCJUMP (each across base + non-stub `<extracted>/APPENDnn/` layers) |
| `init_arterial.py` | Layouts of 創刻のアテリアル's `*INIT` tables, inferred from how its scripts write them instead of from a schema per table (`docs/arterial-data-tables.md`, "Tooling"). A record starts at each write into the table's leader array (`LEADERS` in the tool: bank, base, capacity, for the cards the slots per row, and a second leading array where a table's records are of two kinds); each other write is tried at every stride, for the record before it and the record after it (the appends stamp a record before its leader), and the record and stride whose key (address less stride times id) the most records share are the field's; the executed image's cells are then placed in those arrays, so an append layer's edits of existing records count. An array whose base does not agree with its bank's other arrays modulo the capacity is marked `†`, off the table's grid: another table's cells lie between, or the array is not indexed by the record's id. A table without a leader is listed by the arrays the scripts index (`lookup-array` and `lookup-array-2d` in the listings), which misses an array no script indexes with a variable. The three tables the game runs for an area (`MPINIT`, `ROINIT`, `UNINIT`) are read from the listings alone: the range clears each opens with give its arrays' bases and cells per slot, and the writes under each area's test give the areas, slots and values an array gets. `test_init_arterial.py` holds the inference on synthetic records. | `init_arterial.py --game arterial` (a line per table) · `<TABLE> …` (arrays, column by column) · `--markdown` (a row per array) · `--write` · `--leader BANK:BASE:CAPACITY[:GROUP[:STRIDE]]` (try a leader) | `build/games/<game>/init-image.json` (`init_image.py`), `build/games/<game>/disasm/*.asm` (`extract_phase2.py`) → stdout; `--write` → ⚙ `build/games/<game>/data/<TABLE>.layout.json` |
| `init_image.py` | **Runtime data-table oracle.** Derives the native boot order (INIT2's `*INIT` calls, then each mounted append catalog's AUTORUN `*INIT` calls in ascending selector order) and runs it through `Ome.Cli init-image`, recording every script's global-bank delta. It runs against the selected install, root-loose records included. `load()`, `table_layers(TABLE)` and `merged(TABLE)` give other tools a table's per-layer and final cells. Deltas omit writes that store a cell's existing value. `--project TABLE` fills a name-mode table's static base layout (name/description bases, parallel field bases, row-table columns from `<TABLE>.json`) from the executed image, producing one effective table across every layer; each record carries `origin` (the layer that first wrote its name) and `layers` (every layer that wrote one of its cells). It needs a generic name-mode base extraction, so it applies to tables without a profile specialization (for Kamidori, EBINIT); specialized tables already emit fixed-layout records for every layer. | `init_image.py --game kamidori` · `--summary` · `--project EBINIT` | catalog via `Ome.Cli`, `build/games/<game>/callscript-names.json`, `paths.scripts()` → ⚙ `build/games/<game>/init-image.json`; `--project` → ⚙ `build/games/<game>/data/MERGED-<TABLE>.json` |
| `native_scratch.py` | Gate-6 scratch install for native AGE experiments: hard-links the selected install's `*.ALF` archives and copies every other file into `build/games/<game>/native-scratch/`, then copies `--overlay` files (loose scripts, generated `APPENDnn.AAI`/`.ALF`) into its root. An overlay named like an install archive replaces that archive's hard link instead of writing through it into the install. It records a manifest of the pristine install; `--check` verifies the install is unchanged after a native run. `--name NAME` (letters, digits, hyphens) keeps a second copy beside the profile's own, `native-scratch-NAME/` with `native-scratch-NAME-manifest.json`: localization gate 12 copies the translated Kamidori install with `--game-root ../games/kamidori-translated --name translated`. Native saves live under `%LOCALAPPDATA%`, shared with the real install: drive the copy with `drive.py native`, which sandboxes them, or back them up first. `--reference ID` copies a reference-only install from `paths.REFERENCE_INSTALLS` (an install with no port profile, kept for engine comparison; none now) into `build/engines/<id>/native-scratch/` instead. | `native_scratch.py --game himegari --replace --overlay F …` · `--check` · `--name NAME` · `--reference ID` | install → ⚙ `build/games/<game>/native-scratch/` (reference: `build/engines/<id>/`) + `native-scratch-manifest.json` |
| `test_native_scratch.py` | Content-free tests for `native_scratch.py` over a synthetic temporary install: archives are hard-linked and other files copied, an overlay named like a linked archive leaves the install's archive unchanged, `--replace` rebuilds keep it, and `--check` passes afterwards; `--name` places a named copy and its manifest beside the profile's own and takes only letters, digits and hyphens. | `test_native_scratch.py` | temporary files only |
| `test_init_image.py` | Pure boot-order, layer-merge, and merged-projection regressions over synthetic images. | `test_init_image.py` | — |
| `test_init_kamidori.py` | Pure Kamidori specialization regressions over synthetic stand-in scripts (no game content): MPINIT doubled-grid decoding with a `-1` labyrinth from a local `sub`, STINIT2 base/stride keying independent of write order, ITINIT fixed-layout `divmod` keying on an append-range id, CCINIT rule-block parsing, QUINIT description-line rows and rewards, and forced-mode/extractor registration. Part of `validate.py` core tests. | `test_init_kamidori.py` | — |

For selector-keyed append INIT fragments, pass the universal packed script id:

```powershell
py -3.11 -X utf8 tools/extract_init.py EBINIT --packed-id 0x01000001
```

The extractor finds the mounted AAI by the packed id's high-byte selector, bounds-checks and reads the
indexed ALF payload, and parses it without materializing an extracted `.BIN`. Packed EBINIT fragments reuse
the base EBINIT table geometry so their additive writes retain the same semantic field names. Output is
kept separate as `build/games/<game>/data/APPEND<selector>-<TABLE>.json` and records `packed_id`,
`source`, and `fragment_layout_source`; it is not an inferred base/append merge. Matching base MES data is
not joined to a packed fragment.

When the append catalog is extracted (`<extracted>/APPENDnn/`), `--append N` reads the same record by its literal
`$N$` name instead (`extract_init.py EBINIT --append 3 --game kamidori` → `APPEND03-EBINIT.json`); like
`--packed-id`, the result is one layer, not a merged image. `tools/init_image.py` provides the executed, merged view.

Use the profiler's focused record view to inspect a unit by integer id, exact name, or name regex:

```powershell
py -3.11 -X utf8 tools/init_table_profile.py APPEND01-EBINIT --record 81
py -3.11 -X utf8 tools/init_table_profile.py APPEND01-EBINIT --record "紫色の珍獣/BOSS"
```

`--record ID_OR_NAME` prints the INIT description strings and a semantic-field/value/raw-provenance table;
packed asset values are shown in both decimal and hexadecimal. It is mutually prioritized over
`--message-query` for console rendering, while `--build` may still be used to write the complete profile.

`extract_init.py` also has name-mode specializations for VIINIT's sparse 200-row glossary table,
CNINIT's 277 EBINIT-keyed display-name/voice-family rows within parallel 1,000-cell arrays,
CIINIT's 24 populated character-information profiles within four reserved 100-cell columns, MAINIT's
eleven populated magic-action rows within a reserved 30-cell layout, and ILINIT's thirteen populated
condition ids within a reserved 30-by-5 level layout. ILINIT classifies every authored write, adds nested
names/durations/stat/resource deltas, and validates/joins RECOVER's current-level, passive-baseline,
remaining-turn, recovery-policy, and full-resource-restore protocol. The profiler accepts the same
`base/stride/column` provenance for row-major string fields as for integer record columns. Matching message joins are
ITINIT→ITMES, SKINIT→SKMES, VIINIT→VIMES, EBINIT→EIMES, CIINIT→CIMES, and MAINIT→MAMES. EBINIT is
intentionally sparse across unrelated unit definitions; MAINIT intentionally lacks MAMES bodies for growth
ritual ids 10 and 11.

Numeric-mode CGINIT has its own consumer-proven specialization rather than the generic stride-one
heuristic. It emits 851 sparse rows in the reserved 2,000-row layout, classifies all 3,941 integer
writes into one two-column image/preview table plus thumbnail sheet, 30-cell atlas slot, and variant
ordinal arrays, and joins the packed asset ids through `build/games/<game>/callscript-names.json`. The four thumbnail
sheets are read from INIT2's ten-cell CGMODE configuration and exposed as the 6-by-5 `SO026A.AGF`
through `SO026D.AGF` atlases. The optional preview column
is identified from SAVE/SELSTAGE's 112-by-84 consumer path; all raw addresses remain present.

Numeric-mode ALINIT also has a consumer-proven specialization. It classifies all 914 static writes
into 107 sparse recipe ids within a reserved 1,000-row layout: output item, minimum alchemy level,
required and forbidden story flags, point cost, and four paired ingredient-id/quantity cells. Output
and ingredient ids must resolve through ITINIT. The generated record adds a nested `ingredients` view
while retaining every raw scalar and `base/stride/column` coordinate.

Name-mode AFINIT and CTINIT bypass the generic name-record heuristic because their strings are
vocabularies rather than record boundaries. AFINIT classifies its 27 element labels and 54 footer
copies into the signed affinity matrix, paired item-tuning bonus/cost curves, and three facility
progression rows. CTINIT emits five reserved 70-cell INPUTNAME pages with 273 authored characters and
every empty slot preserved. Both retain raw global/stride/footer provenance beside their semantic views.

Numeric-mode CVINIT classifies all 37 writes into CONFIG's thirteen preview-voice assets, twelve
setting-slot-to-EBINIT-unit joins, and the exact inverse unit-to-suppression-setting map. Preview ids
join to shipped OGG filenames, character slots join to EBINIT names, and the schema records the
per-unit persisted speaker-seen cells that gate CONFIG row availability. Every raw coordinate remains
beside the joined setting view.

Name-mode LAINIT classifies all 98 instructions into twenty shipped terrain definitions inside a
reserved thirty-row registry. It preserves seventeen names, five effect descriptions, four parallel
terrain arrays, the signed ten-column combat-stat matrix, and three implicit-default terrain ids.
Required skill ids join to SKINIT names; shared texture-slot fallbacks join to SYS4INI AGF filenames.
The profile summarizes those populations and the CALCBTPARAM/MVSEEK/FIELD/INFOAF/DRAWMAP contract.

Numeric-mode SPINIT emits eight complete fifteen-slot HMODE gallery pages instead of 118 fragmented
address records. It joins the page rows to INIT2's `SO027A.AGF` through `SO027H.AGF` thumbnail sheets,
resolves every populated cell to its `SP*.BIN` resource, preserves the final two zero slots, and
records HMODE's opcode-0x19d availability-filter plus indirect-call contract.

Name-mode TRINIT bypasses the generic record-boundary heuristic and emits 21 training/sexual-magic
actions. Each row preserves six available/locked text slots and the contiguous eligibility, spirit-cost,
fourteen-stat, alignment, training-progress, award, and ten-event numeric families. Required and awarded
ids join through ITINIT/SKINIT; all 75 event cells join through SCINIT, and the output records TRAIN's
execution path plus GAMESTART's restored-story-flag contract. Its profile summarizes text/gate/effect
populations, execution-limit distribution, and event dispatch coverage.

Numeric-mode CDINIT bypasses the generic fragmented-address view and emits nine selector-dispatched
card-generation lists with 383 candidate entries. Each one-based slot joins its CDINIT2 card id,
display name, result text, effective required/forbidden story flags, and three FIELD-proven weight
parameters. STINIT type-28 objects join back to seven live selectors; authored selectors 55 and 94
remain explicit as unreferenced data. FIELD's effective weight is
`base_weight + floor(current_stage_turn / growth_interval_turns) * growth_weight`, followed by
cumulative weighted random selection across its 100-slot scan. The schema separately preserves the
eighteen CDINIT2 third-column required flags that FIELD never reads, plus the shipped 50-slot clear
prefix even though selector 160 authors 75 slots.

Name-mode CDINIT2 emits all 81 card definitions within the reserved 100-row registry. It preserves
the two text cells and every raw numeric array while projecting FIELD's six card types: story event,
item award, stage-clear point bonus, resource recovery, trap, and random warp. Required/forbidden
story gates distinguish FIELD's two effective required columns from the authored but unread third
column. Item ids join to ITINIT, event ids to SCINIT, condition ids to ILINIT, and all visual ids to
SYS4INI `MVS*.AGF` resources. Ranged effects expose the authored minimum and maximum-exclusive bound;
an equal pair is a fixed value. The consumer contract also records FIELD's rendering/effect order
and STAGECLEAR's deferred spendable-point award.

Numeric-mode BTANINIT2 emits 122 sparse battle-animation rows inside three reserved 1,000-row
arrays: six effect ids, six paired start delays, and one overall duration. Every one of its 573
effect references joins to BTANINIT. The output also joins 101 SKINIT skill references across 98
animations and ITINIT weapon classes to normal-attack rows 1 through 21; passive reactions 801--808,
hardcoded defeat row 809, four unjoined authored rows, and the unused slot 4 remain explicit.

Numeric-mode BTANINIT decodes its conditional effect-id program into 202 definitions rather than
one generic scalar. Each definition retains the six-slot work-array writes for visual resource and
mode, additive blend, surface geometry, combatant anchoring and offsets, sprite-atlas timing,
optional WAV and delay, and three reserved hit-pulse offsets. All 202 AGF and 199 populated WAV ids
resolve through SYS4INI. The schema records BTL's movie/sprite playback and actor/target slot
contract, the engine-dead authored atlas-row count, and sixteen definitions not referenced by
BTANINIT2.

Name-mode STINIT2 emits 74 sparse stage definitions rather than the generic view's 321 apparent
records. It recognizes the separate `string[1000]` title array and `string[1000][6]` description
matrix, retaining three uncleared and three cleared lines per stage. The schema joins numbered,
event-only, and EX presentation; main-progression, unlock-group, and seven-column story gates;
tile/doubled-grid/minimap geometry; point and bronze/silver/gold coin rewards; all 174
entry/clear/failure decisions through SCINIT; and all 74 loader ids to STINIT.BIN. The 66 populated
cells at `0xedc4d` are exposed as medium-confidence authoring-only difficulty tiers: their 1..8
ordering tracks challenge/reward progression, but no shipped script reads the array.

Footer-mode MPINIT classifies all 1,472 copies as fifty-cell rows within one stride-53 terrain atlas.
It derives grid Y from each destination, retains 127 implicit-zero gaps, and joins STINIT2's inclusive
tile bounds after the consumer-proven two-times coordinate conversion. The output contains 66 named
stage maps over 53 unique rectangles, shared-rectangle groups, terrain populations, LAINIT definitions,
and the exact FIELD/DRAWMINIMAP/RESETLAND copy/fallback/restore contract.

## VM

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `vm0.py` | Headless Python bytecode VM (Phase A0 execution-model prototype; reuses `sys4load`; native `bit-set`/`bit-reset` operands are bounded bit indices). | `--test` (RECOVER unit test) · `--sweep [N]` (oracle coverage) · `--scene NAME` · `--settex NAME` (set-texture resId trace + exec trace) · `<file.BIN>` | corpus → stdout; `build/vm0-trace.json`; `build/settex-<NAME>.json` |
| `correlate_scope.py` | Align the VM's `set-texture(resId)` trace with the game's Frida load order → tag each load's DATA2 package, flag package transitions, dump the significant ops in each transition span (the **scope selector** hunt). | `correlate_scope.py <SCENE>` | `build/settex-<SCENE>.json` + `build/games/<game>/frida-load-order-result.json` + index → stdout |
| `diff_optrace.py` | **Differential offset-path oracle** (`docs/engine-re.md`): diff the engine's executed offset path (`trace_engine_ops.py`) against the VM's (`Ome.Cli trace --trace-json`) → first divergence = the mis-modeled branch/op/state, with opcode + ±3 ops of context. Identifies the scene's codebase by longest-common-prefix; filters the VM trace to argc≥1 (operand-capture parity). Pure core unit-tested (`test_diff_optrace.py`). | `py -3.11 -X utf8 tools/diff_optrace.py SC0000 [--full]` | `build/games/<game>/engine-optrace.jsonl` + `build/games/<game>/vm-optrace.json` + disasm → stdout |

### Runtime page locator

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `locate_page.py` | Resolve a run-relative ADV page number to its canonical wait script/offset, last show-text instruction, call stack, and nearby disassembly. Pure selection/window logic is tested by `test_locate_page.py`. | `py -3.11 -X utf8 tools/locate_page.py [--game <game>] SC0000 14 [--map <jsonl>] [--context N]` | `build/games/<game>/page-map-<SCENE>.jsonl` + script corpus → stdout |
| `page_map_compare.py` | Localization gate 11 (spec §18.1): compare two runs' page maps page by page. Run A is an install translated in place with no catalog, run B the clean install with the catalog imported from it, over the same scenarios and `rand-seed` (`tools/drive/scenarios/kamidori-opening-pages.steps`). Required: A's `dialogue`, wraps joined and deliberate breaks kept, equals B's `presented` with whitespace collapsed (none between two wide characters), and the page counts match. Reviewed: the same with line breaks kept, and B's `cut` pages. Offsets are not compared. | `py -3.11 -X utf8 tools/page_map_compare.py A.jsonl B.jsonl [--report OUT.json] [--show N]` | two page maps → stdout (counts, then the first N pages per check), optional JSON report of every page; exit 1 when a required check fails |
| `text_geometry_compare.py` | Localization gate 12 (spec §18.1): compare where text lands in two runs' screenshots. Run A is native AGE on an install translated in place, run B the port on the clean install with the package imported from it, over the scenario pair in `tools/drive/scenarios/geometry/` (Game driver, "Geometry pairs"). For each shot it measures, in the profile's ADV window (`GEOMETRY` in the tool), the column the name plate's bottom edge ends at and every run of text-coloured columns in each text row. A shot passes when the plate edges are within the tolerance and every run's first column in either shot has one in the other within it, or lies inside a run there (glyphs whose ink touches in one shot). GDI and the text server antialias differently, so pixels are not compared. Profiles: `kamidori`. Pillow is imported only to read shots. Tests: `test_text_geometry_compare.py` (synthetic shots; the shot tests skip without Pillow). | `py -3.11 -X utf8 tools/text_geometry_compare.py A_SHOTS B_SHOTS [NAME ...] [--game ID] [--tolerance PX] [--report OUT.json]` (NAME defaults to every `geometry-*.png` in both folders; tolerance 1) | two shot folders → stdout (one line per shot, then its problems), optional JSON report of every shot's measures; exit 1 when a shot fails or a named shot is missing |

In a live Godot run, **F6** writes an observe-only stall snapshot under
`user://games/<game>/diagnostics/stall-<timestamp>.json`, prints the absolute path, and copies
`SCRIPT@offset · stall snapshot <path>` to the clipboard. The JSON contains the current call stack, a bounded
128-instruction ring, VM/host wait flags, surface/movie completion state, decoder state, and the exact finite
graphics channels capable of holding an op-`0x21c` presentation wait. Press it while the apparent stall is still
active; unlike `--timeline-log`, it does not require a special launch or emit a continuous per-op stream. Active
and pending movie records include `first_frame_source_pts_ms`; the ordinary `movie first frame` console line prints
the same source PTS alongside the render frame, which distinguishes encoded stream lead-in from decode/presentation
latency. The port writes the same snapshot by itself when the VM worker fails, halts on a compatibility failure or
hits the step limit (`worker-failure-`, `compatibility-halt-`, `step-limit-<timestamp>.json`); those leave the
clipboard alone.

If the VM thread has not stopped 5 s after the window closes, the port logs it and exits at once with code 3,
skipping the shared-profile flush and the teardown of the glyph rasterizer and movie decoders the thread may still
be using.

Normal interactive Godot sessions have no cumulative instruction ceiling. Bounded Godot diagnostic runs retain
`STEP-LIMIT`; if one fires, Godot captures the same diagnostic automatically as
`user://games/<game>/diagnostics/step-limit-<timestamp>.json` and copies its coordinate/path to the
clipboard. Before nested
frames unwind, the trace sink preserves the deepest active script stack. The console also prints the exact final
script/offset/opcode, that frame chain, the hottest sites in the bounded final 128-instruction window, and the
final 16-instruction sequence. This makes the last ADV locator unnecessary for identifying a post-dialogue loop;
send either the `step-limit` console block or the generated JSON.

## Native FFmpeg movie shim (Windows and Linux x64)

The target-specific dependency manifests pin the project-built minimal FFmpeg SDK by exact byte size and SHA-256.
`native/ome_movie_ffmpeg/build-ffmpeg-sdk.sh` produces that SDK from the official FFmpeg release tarball pinned in
`ffmpeg-source.json` (size, SHA-256, signing-key fingerprint, the configure flags, and the SDK revision), unmodified,
with only the MPEG-1 components both supported profiles need; see `docs/platform-portability.md` for the component
set and rationale. `.github/workflows/ffmpeg-sdk.yml` runs the script — Linux inside a digest-pinned
`manylinux_2_28` container so the libraries stay within the glibc 2.28 baseline, Windows as a MinGW cross-build —
on every branch push that changes the script, the source pin, or the workflow (never on tag pushes), uploading
artifacts only. A manual dispatch with
`publish` ticked additionally drafts the GitHub release `deps-ffmpeg-<mirror_version>` (currently
`deps-ffmpeg-ome-8.1.3-mpeg1-r1`) with both SDK archives and their `.sha256` files, the exact source tarball, and
both `BUILD-CONFIG` files; a maintainer reviews and publishes it. The tag deliberately does not start with `v`, so it
never triggers the release workflow. Each manifest's `url` is an asset of that release, `source_url`/`source_sha256`
name the hosted source tarball, and `upstream_url` the ffmpeg.org original. Replacing an asset on an existing
`deps-ffmpeg-*` release is forbidden; a dependency change requires a new `sdk_revision`/`mirror_version`, a new
release, a paired manifest update, and the installed-movie gate for both profiles. `test_release_workflow.py`
asserts the manifests against `ffmpeg-source.json` (derived URLs, names, version, license, a 500 KB–10 MB size band
that catches an accidental full build) and forbids `--enable-gpl`, `--enable-nonfree`, and `--enable-version3`. The
SDK ships no programs, so all three bootstraps verify the version from `include/libavutil/ffversion.h`. Bootstrap
downloads remove partial files on HTTP failure before validating the complete archive; the Bash bootstraps therefore
need curl 7.83 or newer (`--remove-on-error`), which Ubuntu 24.04 provides and Ubuntu 20.04's 7.68 does not. Windows
development can use PowerShell/MSVC. Linux-hosted builds use Bash with Python 3, curl, MinGW-w64/binutils for the
Windows target, a native C compiler/binutils for Linux, and the ordinary archive/core utilities available.

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `native/ome_movie_ffmpeg/build-ffmpeg-sdk.sh` | Build the minimal LGPL FFmpeg SDK for one target from the pinned, size/SHA-256-checked release tarball with exactly the `ffmpeg-source.json` configure flags and a fixed `/ome-ffmpeg` prefix; enforce the glibc baseline (Linux) or system-only DLL imports and covered MinGW runtime objects (Windows: make writes each DLL's link map, checked by `tools/mingw_runtime_notices.py`); write `BUILD-CONFIG.txt` (and, for win64, the pinned `MinGW-w64-runtime-COPYING.txt`); pack a deterministic SDK archive (regular files before symlinks) plus `.sha256`. Linux must run in a glibc-2.28 environment. | `native/ome_movie_ffmpeg/build-ffmpeg-sdk.sh <linux-x64\|win64> [output-dir]` | `ffmpeg-source.json`, `dependency-linux-x64.json` (`minimum_glibc`), downloaded source → `build/ffmpeg-sdk-work/<target>/` (disposable), `<output>/ome-ffmpeg-*-<target>-lgpl-shared.{tar.xz,zip}` + `.sha256` + `BUILD-CONFIG-<target>.txt` |
| `native/ome_movie_ffmpeg/write_ffmpeg_source_notice.py` | Write `FFmpeg-SOURCE.txt` beside the staged FFmpeg libraries: the LGPL-2.1 credit, the exact hosted source tarball URL and SHA-256 with its ffmpeg.org original, and the SDK's `BUILD-CONFIG.txt`. Refuses a manifest without the source fields or with another license, and an SDK without `BUILD-CONFIG.txt`. Called by all three native build scripts. | `python3 native/ome_movie_ffmpeg/write_ffmpeg_source_notice.py <dependency-manifest> <sdk-root> <output>` | dependency manifest + SDK `BUILD-CONFIG.txt` → `FFmpeg-SOURCE.txt` |
| `native/ome_movie_ffmpeg/bootstrap-win64.ps1` | Download, size/hash-check, extract, and version-check (`ffversion.h`) the pinned FFmpeg SDK. Returns the resolved SDK root. | `.\\native\\ome_movie_ffmpeg\\bootstrap-win64.ps1 [-Destination <dir>]` | `dependency-win64.json`, network/archive cache → disposable `build/downloads/`, `build/ffmpeg-sdk/` |
| `native/ome_movie_ffmpeg/build-win64.ps1` | Discover the MSVC x64 toolchain, build `ome_movie_ffmpeg.dll`, and stage its exact shared-library/license dependencies. | `.\\native\\ome_movie_ffmpeg\\build-win64.ps1 -SdkRoot <bootstrap-output> [-OutputDirectory <dir>]` | C ABI source + FFmpeg SDK → disposable `build/native/win-x64/` by default |
| `native/ome_movie_ffmpeg/bootstrap-win64.sh` | Download, size/SHA-256-check, extract, and version-check (`ffversion.h`) the exact pinned Windows FFmpeg SDK, including its MinGW import libraries. Prints the resolved SDK root. | `native/ome_movie_ffmpeg/bootstrap-win64.sh [destination]` | `dependency-win64.json`, network/archive cache → disposable `build/downloads/`, `build/ffmpeg-sdk/` |
| `native/ome_movie_ffmpeg/build-win64.sh` | Cross-build the AMD64 PE shim with MinGW, check its link map's runtime objects (`tools/mingw_runtime_notices.py`), stage exactly five FFmpeg DLLs and the license, and run the static native-bundle contract gate. `MINGW_CC` and `MINGW_OBJDUMP` may override the standard tool names. | `sdk_root="$(native/ome_movie_ffmpeg/bootstrap-win64.sh)"` then `native/ome_movie_ffmpeg/build-win64.sh "$sdk_root" [output-directory]` | C ABI source + Windows FFmpeg SDK → disposable `build/native/win-x64/` by default, including `verification.json` and the link map `ome_movie_ffmpeg.map` (not packaged) |
| `tools/mingw_runtime_notices.py` | Fail unless every toolchain object a MinGW link pulled in is covered by the Windows package's notices, and the compiler's MinGW-w64 runtime is the release `notices/mingw-w64/notices.json` pins (Third-party notices, below). | `python3 -X utf8 tools/mingw_runtime_notices.py --compiler <MinGW-gcc> <map>...` | ld `-Map` files + compiler queries → summary line (stdout) or the uncovered objects (stderr, exit 1) |
| `tools/verify_windows_native.py` | Without executing Windows code, require the exact native bundle, AMD64 PE architecture, all seven ABI exports and five FFmpeg imports, and no Cygwin/MSYS compatibility runtime. Optionally writes the machine-readable verification report used by later packaging. Its `verify_launcher`, which the Windows packager calls, checks the launcher's PE files: an AMD64 program with the Windows GUI subsystem, and AMD64 natives. | `python3 -X utf8 tools/verify_windows_native.py <bundle> [--objdump <MinGW-objdump>] [--report <json>]` | disposable Windows native bundle → stdout and optional JSON report |
| `native/ome_movie_ffmpeg/bootstrap-linux-x64.sh` | Download, size/SHA-256-check, extract, and version-check (`ffversion.h`) the pinned Linux x64 FFmpeg SDK. Prints the resolved SDK root. | `native/ome_movie_ffmpeg/bootstrap-linux-x64.sh [destination]` | `dependency-linux-x64.json`, network/archive cache → disposable `build/downloads/`, `build/ffmpeg-sdk/` |
| `native/ome_movie_ffmpeg/build-linux-x64.sh` | Build the ELF64 x86-64 shim; stage the five exact FFmpeg SONAME libraries and license; reject stale `.so` files, unresolved/nonlocal FFmpeg dependencies, a missing `$ORIGIN` runpath, or a glibc requirement newer than the pinned 2.28 baseline. | `sdk_root="$(native/ome_movie_ffmpeg/bootstrap-linux-x64.sh)"` then `native/ome_movie_ffmpeg/build-linux-x64.sh "$sdk_root" [output-directory]` | C ABI source + FFmpeg SDK → disposable `build/native/linux-x64/` by default |
| `tools/movie-corpus-gate` | Discover every MPEG program stream in a game's catalog by its MPEG pack header, whatever the asset name (Himegari stores movies under `.AGF` names, Kamidori under `.MPG`; placeholder records are skipped), mounting every `*.AAI` append catalog beside `SYS4INI.BIN`; decode every video frame and, when present, every audio block through the unpaced FFmpeg session; validate independent sequence dimensions, metadata, RGBA size, stereo finite PCM, monotonic video/audio timestamps, EOF, timeout, and teardown; then emit a per-asset JSON report. Audio fields include sample rate/channels, block and PCM-frame counts, first/last PTS, signal presence, and decode time. `--game-root` selects the install (default: the Himegari install) and repeatable `--overlay-root` mounts loose overlays ahead of it exactly as the runtime launcher does. Expected counts: 213 for Himegari, 280 for Kamidori (with `--overlay-root patch`). Exit 0 means the expected corpus count and every asset passed; exit 1 is a gate failure; exit 2 means the native shim or the game's `SYS4INI.BIN` is absent. | `dotnet run --project tools/movie-corpus-gate -- --output build/movie-corpus-ffmpeg.json --expected-count 213 --max-item-ms 30000` · Kamidori: `... --game-root ../games/kamidori --overlay-root patch --expected-count 280` · optional `--native-dir <dir>` | `SYS4INI.BIN` + `*.AAI` + loose/overlay/ALF VFS assets + staged FFmpeg shim → stdout progress + disposable report JSON |

The managed isolated probes load from `OME_FFMPEG_NATIVE_DIR` when set, then application-local and
`runtimes/<rid>/native` locations. Build the target shim before running the `FfmpegShim*` tests; no original-game
movie is copied into the repository or native output. A target-aware `dotnet build godot/OME.csproj`
stages the matching shim, five shared-library dependencies, and `FFmpeg-LICENSE.txt` beside `OME.dll`
when the corresponding `build/native/win-x64/` or `build/native/linux-x64/` bundle exists.

The current native movie ABI is version 3. In addition to sequential video/audio decode, it exposes a
synchronous position seek for both independent demuxers; exact video-frame selection and audio trimming are
performed by the managed decoder's preroll before normal paced delivery.

The corpus gate intentionally bypasses presentation waits: it validates video/audio decode compatibility and
lifecycle, not wall-clock playback pacing or audible output. `--expected-count` makes additions, omissions, or profile changes explicit;
changing the pinned FFmpeg dependency requires rerunning this gate.

## Engine (C#) — VM core, CLI, Godot frontend

The `engine/` .NET solution (`OmeEngine.sln`) is the runtime VM; `godot/` is the ADV frontend. Not
Python, but listed here as the things you *run*. Build: `dotnet build engine/OmeEngine.sln`; test:
`dotnet test engine/OmeEngine.sln`. Run a CLI command: `dotnet run --project engine/Ome.Cli -- <cmd>`.

`Ome.Engine` embeds the generated `build/opcodes.json` (`OpcodeTableJson.LoadEmbedded`,
`OpcodeContracts.LoadEmbedded`), so every engine build needs it: run `opcodes_build.py --build` first, as
`validate.py` and the release scripts do. Every linker of an install builds its link base from that copy through
`ModLinkBase.ForGame` (launcher spec §5.9): the Godot host, the `omp` commands and the launcher. They therefore
share one toolchain stamp, with the profile's globals, OMS modules and patch points taken from the repository
when there is one. After `opcodes_build.py --build`, rebuild before `omp` sees the change. The other CLI commands
are development tools and read `build/opcodes.json` live.

Every CLI command resolves `--game ID` and `--game-root PATH` before opening scripts or assets; without
those options the established Himegari workspace layout remains the default. Asset diagnostics use that
selected catalog/store. They also accept repeatable `--overlay-root PATH` and the explicit
`--allow-bmp-as-agf` compatibility option described with the Godot arguments below. A relative overlay path
is resolved against the selected game root, and `catalog-scan` records both options in its JSON report so an
overlay corpus cannot be confused with the official root/archive corpus. `--boot` is evidence-scoped profile
behavior: it uses only the selected profile's declared diagnostic bootstrap lists and fails clearly when the
profile has none, rather than borrowing Himegari's script names.

**call-script executes** on the product paths: they inject `Sys4ScriptProvider`, which runtime-parses
`SYS4INI.BIN` and opens `.BIN` bytes through the native loose-first/bounded-ALF store, so `call-script <id>` loads & runs the target as a nested subroutine
frame sharing globals. `trace`/`audio`/`gfx` stay **provider-less** (call-script stubbed) — base-ISA /
subsystem oracles. Test scenes are **synthesized** via `Ome.Engine/Sys4/ScriptAssembler` (see
[[testing-synthesize-dont-disable]]: synthesize test data, never disable a feature to keep a golden green).

The runtime SYS4 front-end is `Sys4AssetCatalog` (universal packed-id and name views; scene groupings are diagnostic only), `IAssetStore` /
`Sys4AssetStore` (exact-basename loose roots, then a bounded ALF range), and `Sys4ScriptProvider` (cached
root/call-script parsing). Generated asset/callscript JSON remains a tooling and test oracle only.

The command is the first argument. With no arguments or an unknown command, `Ome.Cli` prints its usage and the
command list to stderr and exits with code 2; a command's own usage errors also exit 2.

| Command | Purpose | Notes |
|---|---|---|
| `init-image <out.json> <SCRIPT.BIN…>` | Run the named data-table scripts in order through one `GameSession` (no input halts) and write each script's global-bank delta: new or changed int and string cells, plus halt/steps. Names are exact catalog record names, so append fragments are addressed as `$n$NAME.BIN`. Driven by `tools/init_image.py`, which supplies the native boot order and game root. | writes JSON; `--game`/`--game-root` select the catalog |
| `intake --game-root INSTALL [--compare [NAME=]INSTALL …] [--output REPORT.json]` | A first look at an install that has no profile yet (`adding-a-game.md`, step 1). It needs no profile, engine build or ABI snapshot, and runs before a game is selected. It reports the install's identity (catalog revision, title, and the profile that matches if one does), `AGE.EXE`'s size, version resource and SHA-256, the header's saved-prefix and full bank sizes, the catalog's archives, entries, appends and file types, the settings pairs, every script's revision, the opcodes used with their counts, the opcodes without a handler (`VirtualMachine.Implements`) with their heaviest scripts, and, for each observation in the registry, the opcodes that game never uses. | Scripts decode with the whole opcode catalog (the `AGE-catalog` layer of `build/opcodes.json`), read loose-first as the engine reads them. The catalog is read leniently (`InstallIntake`, `Sys4AssetCatalog.ParseLeniently`): a block after the settings pairs that the reader does not know is reported as bytes, where the engine's own reader refuses the catalog; a catalog that does not read at all leaves only the root's loose scripts. Settings keys and file types are compared with each supported game's conventional install (`games/<id>/`) that exists, and with each `--compare`. Four checks decide `same SYS generation`: the catalog signature, the script revisions, the whole-catalog decode and the operand types (`meta.arg_types`). Exit 0 when all pass, 1 when one fails, 2 on a usage error. On the supported games' installs its counts equal the registry's observations (`InstallIntakeTests`). It also lists the fonts the catalog ships (`Sys4AssetCatalog.Fonts`) and writes `operand_type_occurrences`, `catalog-scan`'s operand census: kept as `build/games/<id>/intake.json`, the report gives `operand_access.py --apply` a new game's operand types before any opcode table exists for it. **Does not execute scripts**, and writes nothing but `--output`. |
| `catalog-scan [--output <report.json>]` | Decode the selected profile's complete production catalog and report its engine build and ABI, script/instruction/opcode totals plus recognized opcodes without a handler (`VirtualMachine.Implements`). | Unsupported entries include their complete per-script occurrence/offset distribution, operand-type signatures, and representative typed operands. `operand_type_occurrences` gives, for every opcode, one operand-type histogram per operand position, which serves as the corpus census for `operand_access.py --check-corpus`. Accepts the profile/game-root/overlay options above; **does not execute scripts**. |
| `oms-roundtrip [--level raw\|core\|idiomatic] [--fixed-point] [--output <report.json>]` | Open Maid Script gates 1 and 2. Without `--level` it is gate 1; it decodes every catalog script (base and mounted appends, through the production store) into the `Ome.Script` intermediate form, re-encodes it, and compares bytes. `--level raw\|core\|idiomatic` runs gate 2 at that level: it prints each script as OMS source (raw `@op` instructions, core structured statements, or idiomatic source that also lifts the source modules' library subs and templates; named as for `oms-decompile` unless `--no-names`) plus a layout sidecar, round-trips the sidecar through JSON, then parses, compiles and encodes the result. `--fixed-point` (gate 4) starts each script from its canonical rebuild, so the report counts how much sidecar compiler-generated code still needs. | Gate 1 reports scripts and identical counts, label and data-reference operands, data items by kind, referenced/unreferenced raw data, explicit-table scripts and failures. `--level` reports empty sidecars, per-field sidecar use, source size, instructions, `raw-statements` (`@op` escapes left in the source; 0 at core level), and at core level `folded` (instructions folded into expressions), `fold-rejections` and `temp-overrides` (statements whose temporary slots the sidecar pins). The idiomatic level adds `library-subs`, `template-lifts`, `lift-coverage` (instructions inside accepted lifts per source module, with the percentage of all instructions; spec §12 gate 5) and `lift-failures` (scripts whose lifted source did not reproduce the original and were written as core source, listed on stderr). Both exit non-zero unless every script is byte-identical. |
| `oms-decompile (--script NAME [--output <file.oms>] \| --output-dir DIR) [--level raw\|idiomatic] [--no-names] [--fixed-point]` | Print one catalog script as OMS source, or write every catalog script to `DIR`: core-level structured statements and expressions by default, raw `@op` instructions with `--level raw`. Core source names registry globals from `<game>.globals` (`build/games/<game>/globals.json`, when generated) and `run` targets from `<game>.scripts` (the mounted catalog), with the matching `use` lines. A cell inside a declared array prints as its element, through the registry's tables where one holds the array (`unit[0x2f9].stats.max_hp`), and runs of field writes to one constant record print as `with` blocks (spec §11.3). `--level idiomatic` also loads the hand-written source modules in `oms/<game>/*.oms`: it recognizes their library subs (calls print by name, bodies and `exit` guards leave the source) and lifts their template uses, dialogue lines included (spec §11). `--no-names` keeps raw cells and ids and loads no modules; `--fixed-point` decompiles the canonical rebuild instead of the original. | With `--output`, it writes the source and, when needed, `<file>.omslayout.json` beside it. A script whose lifted source would not reproduce the original is written as core source, with the reason on stderr. |
| `oms-temps [--limit N] [--match TEXT] [--dump DIR]` | Rank the core level's temporary-allocator residue: folded statement shapes by how often the original slots differ from the canonical plan, with the slot patterns seen (`#k` integer temporaries relative to the pool base, `@k` reference slots). `--match` keeps shapes containing `TEXT`. | `--dump DIR` writes one JSON line per statement (structural path, expression tree, original slots) per script, for offline allocator models. |
| `oms-append --selector N --output-dir DIR --script A.oms [--script B.oms …] [--mod-id ID]` | Spec §9.3: compile OMS scripts into a new S4AC append catalog for the selected profile. Each `--script` is one record, in order; record 0 is the one native op 0x143 runs at INIT2, and record `n` is packed id `(N << 24) \| n`. A script's record name is its `script "…"` name, conventionally `$N$NAME.BIN`. Title and magic come from the base catalog; a selector the install already mounts is refused. With `--mod-id ID` the scripts compile as one mod's catalog (mod loader spec §6.2): `run NAME`, where NAME is another `--script`'s file stem, becomes a resource reference and is bound at once to that record's packed id. | Writes `DIR/APPENDnn.AAI` and `DIR/APPENDnn.ALF` and lists each record's packed id. |
| `oms-compile --source <file.oms> [--layout <sidecar>] [--no-layout] [--output <file.BIN>]` | Compile one OMS source file to a SYS4 script for the selected profile. A sidecar next to the source (`<file>.omslayout.json`) is applied unless `--no-layout`, which builds the canonical layout. A sidecar `oms-decompile` wrote records the source's fingerprint (`source`: sha256 of the text, CRLF read as LF); when the source no longer matches it, the sidecar is set aside with a warning and the canonical layout is built, since the old layout would put the old data back. A malformed sidecar is an error. `use <game>.globals` / `<game>.scripts` and the source modules in `oms/<game>/` resolve as for `oms-decompile`. | Writes the `.BIN` (default: the source path with `.BIN`); warnings (spec §8.2) and errors go to stderr as `file:line:col`; exits non-zero on an error. |
| `omp build DIR [--output FILE.omp] [--mod DEP …]`, `omp check [--data-root DIR] [--mod PATH …] [--skip-failed-mods]`, `omp explain NAME [--data-root DIR] [--mod PATH …]`, `omp export --output-dir DIR [--data-root DIR] [--mod PATH …]`, `omp lua-stubs --output DIR [--data-root DIR] [--mod PATH …]` | Mod loader spec §10. Package commands for modders: `build` links one mod folder and writes a deterministic `.omp` with stored plans; `check` links a stack without writing anything; `explain` names the layer and mod supplying a script or asset, and an asset's language versions (localization spec §12; which win, and which a later plain asset hides); `export` writes the linked stack as loose files and `APPENDnn` catalogs for native AGE; `lua-stubs` writes lua-language-server definitions for `maid`, the declared events and each base mod's names (Lua scripting spec §13, `Ome.Mods/Tools/LuaStubs.cs`). Full options, defaults and examples: `docs/modding/cli.md` (the canonical modder reference). | `build`: the link report and the package path. `check`: the report, each mod catalog's records with packed ids, and a summary; exits non-zero when the stack does not link. `export`: the report and each written file. `explain`: one line per supplier. `lua-stubs`: each file written and the `.luarc.json` line. |
| `omp text extract --language TAG --output DIR`, `omp text check [--mod PATH …] [--data-root DIR] [--text-root DIR] [--language TAG] [--no-scripts]`, `omp text stats [--mod PATH …] [--data-root DIR] [--text-root DIR] [--language TAG] [--scripts]`, `omp text po-export --language TAG (--package DIR \| --text-root DIR) --output FILE.po`, `omp text po-import --language TAG (--package DIR \| --text-root DIR) --input FILE.po` | Localization spec §13 (milestone 4; PO in milestone 6). `po-export`/`po-import` convert a language's catalogs to one gettext PO file and back (`PoFile`, `TextPoCommands`): `msgctxt` is the entry's scope (`SCRIPT`, `SCRIPT@0xOFF`, `engine:KEY`), import rewrites only `text` in the entries that changed and stops before writing on a translation that does not fit its entry. Catalog commands for translators: `extract` writes or updates catalog templates (entries without `text`) from the selected profile's installed scripts, appends included, keeping what the folder holds and marking `obsolete` the entries whose source is gone, and for a language the shell has no table for adds `[[engine]]` templates to `_engine.toml` (milestone 5); `check` links the packages as `omp check` does and validates their catalogs and the `--text-root` folder's (ambiguous patterns and `[[engine]]` entries with arguments their key does not take are errors; sources that reach no script, unbound site entries, characters no bundled font covers, a table whose first bundled font is proportional to the game (`GdiFontRules`) and unknown engine keys are warnings; the engine keys come from the shell's tables, `ShellStrings`); `stats` counts what each language translates. All three scan the install's scripts with `ScriptTextScanner` (the presenter's message segmentation, `draw-string`/`set-string` literals, and the importer's composed-string walk). Full options and the catalog format: `docs/modding/cli.md` and `docs/modding/languages.md` (the canonical modder references). | `extract`: the files written and the counts. `check`: the link report, per-language entry counts, then `error:`/`warning:` lines and a summary; exits non-zero on an error. `stats`: one line per language (and per script with `--scripts`). |
| `omp text import-scripts --from GAME_ROOT [--overlay-root DIR] [--allow-bmp-as-agf] --output DIR [--language TAG] [--package] [--verify] [--compare-archives]` | Localization spec §18.1 (milestone 0, gate 10). Builds a text catalog from an install whose scripts were translated in place (`--from`; there `--overlay-root` and `--allow-bmp-as-agf` apply to that install), aligned against the selected profile's install. Each translated script is aligned instruction by instruction with its base; added `show-text`/`end-text-line` pairs (hand-made line breaks) are set aside, and the translated text inside each base message's span becomes one entry (spec §6.2). Each line break in it is a wrap, joined with a space, or deliberate, kept as `\n`, judged by the script's line edge. Variable runs become `{vN}` patterns, and strings the scripts build with `concat` become patterns scoped to the composing script. `set-string` and `draw-string` literals become entries too, scoped by the instruction that stores or draws them; text the translation wrote into an empty stored line is folded into the line before it. A literal stored into a string cell that some script passes to a font opcode (`set-font`, `find-font-family-index`, `get-font-family-name`) is a face name, not text: it gets no entry, and one the translation changed is reported (`literals.font_faces`). A source gets a global entry for its most common translation (a translation wins over an untranslated copy); scripts and sites that differ get script and site entries, and a file with site entries carries the base script's fingerprint. Strings that are not translations keep their source and are reported on their own: copies of the source with characters missing and empty strings written as spaces. `--verify` reads the files back, presents every compared message, literal and composed string through the catalog, and compares exactly with the translated text as the importer read it; patterns are also checked with sample values, presented by the port's own presenter. `--compare-archives` compares same-named archives by content rather than size. `--package` makes `DIR` a package that loads as it is (`--mod DIR`): it also writes each image the translated install replaces (the report's `replaced_images`, `$n$` append records included) to `DIR/assets/<lang>/`, an AGF as `NAME.png` decoded as the port decodes that install (a BMP under an AGF name included) and any other image as it is, and, when `DIR` has no `manifest.toml`, writes one: `omp = 2`, id `local.<game>-<lang>`, the profile's `[game]` with target `native`, and a `[[language]]` table named after the language whose `glyph_delay_scale` is the source's characters over the translation's across the translated entries. The translated text and images belong to their translators: keep the output under `build/` and never commit or ship it. Kamidori: `--game kamidori --from ../games/kamidori-translated --overlay-root patch --allow-bmp-as-agf --output build/games/kamidori/import-en --package --verify`. | Replaces the `.toml` files in `DIR/text/<lang>/` with `_global.toml` and one `<SCRIPT>.toml` per script with scoped entries, writes `DIR/import-report.json` (counts, with up to 200 samples per list) and prints a summary; exits 1 when `--verify` finds a mismatch. With `--package`, also replaces the images in `DIR/assets/<lang>/` (other files there are kept), counts them in the report's `assets.written` with each failure in `assets.not_written`, and writes `DIR/manifest.toml` unless one exists (an existing manifest is kept as it is). Results and the importer's rules: `localization-import-test.md`. |
| `oms-patch (--source FRAGMENT.oms … \| --plan PLAN.omspatch.json …) [--plan-dir DIR] [--output-dir DIR]`, `oms-patch --fingerprint SCRIPT`, `[--mod-id ID --names A,B,ICON.AGF [--selector N]]` | Spec §10 fragments. Each `--source` (repeatable) is a `patch "SCRIPT" fingerprint "sha256:…"` unit. It is compiled against that script as the selected profile's mounted catalog serves it: the fingerprint must match, and base labels, subs and linked library subs come from the script's decompilation (idiomatic when `oms/<game>/` has source modules). The resulting transform plan is checked by applying it, then written beside the source as `<name>.omspatch.json` (or into `--plan-dir`). `--plan` (repeatable) loads plans already written. With `--output-dir`, all plans are applied per script in the order given (sources first, then plans) to the pristine catalog script, and each patched script is written to `DIR/<SCRIPT>` for use as a loose override. `--fingerprint SCRIPT` prints the header line for a new fragment. A dotted site name (`site himegari.experience.after_calculation`) is a named patch point, resolved for the base's fingerprint through `build/games/<game>/patch-points.json` (`patch_points_build.py`). `--mod-id ID --names …` compiles the fragments as part of a mod whose catalog records are those names in order (names with an extension are assets): `run NAME` and `asset "NAME"` stay symbolic in the plan (version 2, `res` operands), and `--selector N` binds them for `--output-dir`. | Prints each plan's operation and instruction counts, ordered interactions between plans, notes (table sites a region removes, proven code positions that stay numbers) and each patched script's size. Errors go to stderr as `file:line:col`; conflicts between plans stop the apply; exits non-zero on an error. |
| `oms-rebuild [--level idiomatic \| --patch-probe [STRIDE]] [--scene <SCENE.BIN>] [--boot] [--halt-at-wait] [--limit N] [--dump DIR]` | Open Maid Script gate 3: serve every catalog script rebuilt from core source (idiomatic source with `--level idiomatic`) without its sidecar, run each SC/SP scene (or one `--scene`) on the original and the rebuilt catalog from the same baseline (`--boot`: the profile's data-table bootstrap, run on both), and compare executed instruction sequences, frames, emitted text, halts and final session states on a simulated input clock. Scenes run as the root with the profile's engine build and zeroed banks, so each takes its retail entry (Himegari's developer-entry gate `G[0xaba5c]` stays 0); unlike a Godot `--scene` run, SYSTEM4 does not host them. Idiomatic rebuilds link library subs elsewhere, so positions compare by relocation key (library sub and offset, or the script's own instruction ordinal), and a global holding a code position may differ when both values name the same instruction. Scenes walk every page unless `--halt-at-wait`. `--patch-probe` runs spec §12 gate 7 instead: it serves every script patched with its probe plan (`OmsPatchProbe`: inert notes, op 0x1a7, before branch targets and every STRIDE-th instruction (default 5) and after calls and other fall-through instructions, every STRIDE-th instruction replaced by a copy of itself, and one operand replaced by its own value) and checks that an empty plan reproduces each script byte for byte. The comparison drops the notes' steps, and the simulated clock does not advance for them. Probed runs get four times the step limit, and a step-limited original is compared as a prefix. `--dump DIR` writes each served (rebuilt or probed) script to `DIR`. | Prints rebuilt/byte-identical/failed script counts, equivalent and divergent scenes with the first divergence, and total steps/lines; with `--patch-probe`, also the number of scripts an empty plan reproduces. Set `OMS_REBUILD_CONTEXT=1` to print the last five events up to a divergence on both sides. Exits non-zero on any divergence. |
| `oms-residue [--limit N]` | Explain the gate-1 residue: operands at code-target positions that stayed literal (with the reason: variable operand, past code, mid-instruction) and referenced data items the decoder could not type. | Diagnostic listing only; reads the same store as `oms-roundtrip`. |
| `run <file.BIN>` | Execute a script; print steps, show-text count, **call-script dispatch count**, the first 30 lines (each tagged with its source script), and the distinct source scripts. | `HeadlessWaits` (headless); **executes call-script**. |
| `trace <out.json>` | Trace every SC/SP scene → offsets + halt + steps. **Provider-less** (call-script stubbed) = a base-ISA offset dump. | writes JSON. (Was the vm0 differential oracle; vm0 is retired from oracle duty — `TraceDiffTests` removed.) |
| `trace <SCENE.BIN> [--boot] [--state <f>] [0xADDR=VAL…] --trace-json <out>` | ★ Emit the **full per-op executed-offset path** of one scene (not just show-text), filtered to the scene's own frame — the VM side of the differential offset-path oracle (`diff_optrace.py`). `--boot` runs the selected profile's proven direct-scene state prefix (Himegari: `INITCONFIG/INIT2/INIT`); **`--state <f>` loads a captured scene-entry snapshot** (`capture_global_writes.py`) = the engine's real pre-scene state; `0xADDR=VAL` hand-seeds. | `JsonOffsetTraceSink` (observe-only, parity held) → `{scene, offsets:[…]}` JSON. |
| `audio <SCENE.BIN> [0xADDR=VAL…]` | Run the scene with the audio native over a trace device and dump, in order, what it sends with the resolved catalog record: BGM starts (`play-bgm` looping, `play-bgm-once` one-shot; a repeat of the current track sends none), BGM releases (`stop-bgm`) and voices (`play-voice`). The clock stands still, so an op 0xc2 fade settles at once, as in every headless run (`run`, `play`, `sweep`, `trace`, `GameSession`): a fade to zero releases the BGM and clears the retained track at the op. | optional seeds. provider-less (stub) for now. |
| `gfx [--boot] <SCENE.BIN> [0xADDR=VAL…]` | Dump the executed surface ops, one line each from the graphics native's trace (`Graphics.OpTrace`): `create-texture slot=S WxH`, `set-texture slot=S res=0xR -> NAME` (`<unresolved>` when the catalog lacks it, `[NO AGF]` when it is not an image), `release-surface slot=S`, `release-surface-range first=F count=C`, `get-tex-size slot=S -> WxH` and `draw-texture slot=S src=(x,y WxH) dst=(x,y) file=NAME` — **plus the per-object gfx slots**; the headless geometry oracle. **`--boot`** runs the selected profile's direct-scene state prefix via `GameSession` first and runs the target with call-script on; without it, seeds-only + provider-less. | The ops run in the graphics native (`Ome.Engine.Natives.Graphics`) over the selected profile's catalog/store, with the canvas's size for slot 0. |
| `play [--boot] [--state <f>] [--save-state <f>] [--show N] <SCENE.BIN…> [0xADDR=VAL…]` | ★ Cross-scene **state runner**: run a scene sequence carrying persistent globals. `--boot` first runs the selected profile's proven data-table bootstrap list (Himegari currently declares its 9 `*INIT` scripts). `--state`/`--save-state` load/persist a JSON snapshot. `--show N` prints each scene's first N emitted lines. | `GameSession`; **executes call-script**. |
| `sweep [--boot] [0xADDR=VAL…]` | Corpus-scale run. **With call-script execution on: 284/297 exit, 13 STEP-LIMIT** (input/state-gated ADV scenes spin headless once subroutine global-writes drive their loops — state divergence, not a bug; 0 depth-cap/unresolved). **With seeds = a story-state explorer**: reports which scenes' dialogue changes ±seed (e.g. form flag `0xa57=1` → 34/297 scenes). | |
| `run-lock DATA_ROOT [--hold]` | Take a data root's `run.lock` as the engine's start does (launcher spec §5.5), marking the start. The run lock tests start it as a second process. It loads no game. | Prints `held PATH` once it holds the lock. With `--hold` it keeps the lock until its standard input closes or the process is killed; otherwise it releases it at once. Exit code 0 when it took the lock, 3 when another process holds it, 2 on a usage error or a root that cannot hold the file. |
| `save-diff A.DAT B.DAT [--game ID] [--section NAME]… [--limit N]` | Compare two native save containers (numbered `SAVEnn.DAT`, or any S3SD/S4SD file) by their decoded payloads (`NativeSaveDiff`). The files themselves never compare, because the transform seed is random and the LZSS match choices differ. Numbered saves are compared section by section: `metadata`, `header` (frame cutoff, saved-frame owner, BGM, gfx record size), `sfx`, `resource`, `surface` (fields id/key/reload/lifecycle/created), `frame`, `int-global`, `float-global`, `string-global`, `pointer-global`, `float-pointer`, `string-pointer`, `gfx` (per handle, by record offset) and `range-transform`. Any other payload is compared dword by dword in section `payload`. The text-history tail that follows a numbered save's container is not compared. With `--game`, global rows carry the profile registry's names (`name` or `array[offset]`), and the bank sections that game's engine build never writes are left out and named in a line of their own: native leaves them as its buffer was (`sys4-format-notes.md`, the six counts). Without `--game` every section is compared, so those sections usually differ. Use it for native-versus-port save checks in driven sweeps. Expected differences: op `0xd0` clock samples, gfx start stamps, the last-saved-slot global, and the surface records native keeps from before a load (`engine-re.md`, "Op 0x216 reads past the live surface table"). | Prints an A/B description line, then rows of `section key field A -> B`, at most `--limit` per section (default 40, `0` = all), and a per-section summary. `--section` (repeatable) keeps only the named sections. Exit code 0 when the saves match, 1 when they differ, 2 on a usage or decode error. |

| `save-info FILE [--game ID]` | What one native save file says about itself (`NativeSaveInfo`), for the first save read of a game that is being added (`adding-a-game.md`, step 7): the magic, the game id, the four-character version stamp, the save version, the payload's size and the bytes after the container; and for a numbered save its frames (script id, parent, resume and call-target indices, returns), its six bank counts, its graphics record size and object count, and whether the codec carries it through a decode and an encode (the decoded sections, and the bytes). With `--game` it is set against that game's profile and its engine build's save rules: the magic, the game id, the save version, the stamp, the bank counts and the record size. | Prints one line per fact, then `profile agrees …` or a `profile DIFFERS: …` line per mismatch. Exit code 0 when the file reads, survives the codec and agrees, 1 when it does not, 2 on a usage or decode error. |

**Faithful headless vs plow (`HaltAtWaitForInput`)** — headless has no player, so op `0x72 wait-for-input`
either **halts** ("the scene is waiting; with no input, stop here") or is ignored (**plow** — walk every page).
Plow is a *fiction*: it runs past every prompt into code no real playthrough reaches — e.g. a plowed
`SC0000` fell through 166 prompts into the name-entry poll loop and spun `sleep 1` **493k×** to STEP-LIMIT.
So: **`run`/`play` HALT at the first `wait-for-input` by default** (faithful; `SC0000` stops at ~402 steps,
0 sleeps — matching the real run's path to the first prompt), with **`--plow`** to opt into full-page
coverage. **`sweep` PLOWS by default** (it *is* the dialogue-coverage oracle: 284 exit / 13 STEP-LIMIT),
with **`--halt-at-wait`** to opt into faithful mode (then all 297 scenes halt cleanly at their first prompt
— 0 STEP-LIMIT). Interactive Godot is unaffected (it really blocks on input; flag stays false there).

**`--trace [--trace-file <path>] [--trace-steps]`** (on `run`/`play`/`sweep`): stream the engine's own
diagnostic events over the `Ome.Engine.Diagnostics.ITraceSink` seam — scene/subroutine frame enter+exit
(indented by call depth), call-script dispatch with resolved name, and the final halt+step count — to
console or a file. Add `--trace-steps` for per-instruction opcode/arg + stub-op detail (high volume;
gated). Absent ⇒ no tracing (`NullTraceSink`, byte-identical run). This is an **engine** fact stream:
frontends consume it instead of reimplementing a diagnostic stand-in for the waits native. Example: `play SC0000.BIN --trace`
shows `» SC0000.BIN (enter, TopScene)` → `call-script 0xee =INPUTNAME.BIN (resolved)` → `halt: …`.

**Aggregating / filtered diagnostics** (added 2026-07-08 after a full `--trace-steps` dump proved unusable
at 2.5M lines). All observe-only → parity preserved; all on `run`/`play`/`sweep`:
- **`--trace-histogram`** — instead of a per-line dump, aggregate **execution counts per opcode** and per
  **call-site `(script:pc)`** (with a sample first operand), dumped sorted after the run. Answers "how many
  times did op X run, and from where?" directly. This is what pinpointed, in one line, that the 493,175
  `sleep`s in a headless `play` come from `INPUTNAME.BIN:0x1c3` — a name-entry input-poll loop that spins
  only because headless has no keyboard — not from the opening. Step lines are attributed to the **real
  running script** (nested call-script frames included), the "which script is this pc in?" answer a bare
  step trace can't give.
- **`--trace-ops <csv>`** — filter the text trace to only the named ops (mnemonics or `0x` hex, e.g.
  `--trace-ops sleep,draw-texture,wait-for-input`), each line tagged `script:pc`. The ordered interleaving of
  a few ops of interest without the flood.
- `--trace-file <path>` now creates the parent directory if missing.
- **Godot** accepts **`--trace-histogram <file>`** — profile the **real** run (headless flow diverges because
  `wait-for-input` is a no-op there; the real run to page 1 is ~562 steps with **0** sleeps vs headless's 2M
  steps / 493k sleeps). Dumped when the scene ends or the window closes. e.g.
  `godot --path godot -- --scene SC0000 --shot out/p1.png --trace-histogram out/hist.txt`.

**Godot frontend** (Godot 4.7 .NET; project = `godot/`). Toolchain: `godot --headless --path godot
--import` → `dotnet build godot/OME.csproj` → `godot [--headless] --path godot [-- <userargs>]`.
The build stamps the engine version that the options panel's 情報 shows from `git describe --tags --match "v[0-9]*"
--dirty=+ --always` (target `OmeVersion` in `godot/OME.csproj`); `-p:OmeVersion=<version>` sets it instead, and a
build without git or a checkout shows "unknown" (`engine-re.md`, "Native menu bar", 情報 names the port).
Plays the real bytecode with call-script execution on (subroutines run live). The game root is selected
before catalog loading: `--game-root <directory>` wins, otherwise the directory containing the running
executable is tried, then the current working directory. A candidate must contain `SYS4INI.BIN`; invalid
explicit roots and unresolved defaults stop startup with the searched locations. Relative overrides are
resolved against the current working directory, and repeated overrides use the last value. On Unix,
Godot preserves the shell launch directory through inherited `PWD` because editor/project startup may
change the process directory before managed code runs.

Started with no game (launcher spec §5.6; `LauncherHandOver`): with no engine argument at all, no `SYS4INI.BIN`
in either of those folders, and `OME-Launcher` (`OME-Launcher.exe` on Windows) beside the executable, the engine
starts the launcher, prints `[startup] no game was given or found here: started …` and quits with 0, so
double-clicking either program works. With any argument, or without the launcher, the game root error above is
unchanged: a mistyped command shows its own error. A launcher that does not start is a startup error (exit 2).
Running from the editor binary never hands over, since no launcher sits beside it.

**Linux x64 export** uses the committed `godot/export_presets.cfg` preset and `godot/OME.sln`:

For the complete Linux-native release path, run from `age-reimpl/`:

```bash
./tools/build-linux-x64.sh
```

The command requires Python 3.11, the .NET SDK selected by `global.json`, a C compiler/binutils, `curl`,
`tar`, and normal ELF loader tools. `tools/godot-linux-x64.json` pins the Godot 4.7 .NET Linux editor and
Linux release template by URL, size, and SHA-256. `bootstrap-godot-linux-x64.sh` caches the editor below
`build/toolchains/` and uses `install_godot_templates.py` to range-fetch only the selected Linux template
member from Godot's all-platform archive; it does not download the complete 1.2 GB template set.

The build regenerates embedded opcode metadata, bootstraps/builds the pinned Linux FFmpeg bundle, performs
the Godot release export, and delegates the shared required/forbidden payload checks to
`package_linux_x64.py`. It does not compile the engine: the engine is one NativeAOT library that
`publish_engine.py --runtime linux-x64` builds beforehand on Linux (below), and the build takes that publish only if
its stamp names the checkout's commit (`publish_engine.py --check`). Godot then performs its real PCK/executable
export against the library alone, supplied by `dotnet_publish_proxy.py` (`OME_PUBLISH_ASSEMBLY="OME.so"`); the proxy
refuses any project, configuration, runtime, self-contained flag, or output root other than the invocation reserved
by the build driver. Because the data folder is external to the PCK, the build stages it after Godot exits: the
engine publish and the FFmpeg bundle. The payload verifier and packaged smoke test therefore validate the same
runtime that is archived.

**The engine library (`tools/publish_engine.py`).** The engine ships as one NativeAOT library, `OME.dll` or
`OME.so` in the data folder (`platform-portability.md`, "Fewer files in a package"). `publish_engine.py --runtime
win-x64|linux-x64` publishes `godot/OME.csproj` with `-p:PublishAot=true` (`ExportRelease`, self-contained, one
MSBuild node, no build servers, the conserving GC, no symbols) into `build/engine-publish/<rid>/`. The publish must
hold exactly the library and the .NET runtime pack's two notice files; anything else fails it, a JIT runtime file
or managed assembly above all. It keeps the build's dependency list as `build/engine-publish/<rid>.deps.json` for the
notices check, since the library holds the packages, and writes `build/engine-publish/<rid>.json` with the runtime,
the commit and whether the checkout had uncommitted changes. NativeAOT compiles for the host's own system only, so
the tool refuses another: `win-x64` on Windows with the MSVC build tools (found through `vswhere`, whose folder the
tool adds to `PATH`), `linux-x64` on Linux with gcc. A release links the Linux library in the `manylinux_2_28` image
(the release workflow's `linux-engine` job), so it needs glibc 2.28 at most; one linked on a newer glibc fails the
Linux packager. `--check` only checks that a complete publish for this checkout's commit is in place, as the build
scripts do. `--dotnet` names another `dotnet`.

Then `publish_launcher.py` puts the launcher beside the engine (launcher plan §9.2). It is a step of its own after
Godot has exited, so its memory peak never joins the export's (plan §3.7). It runs `dotnet publish` of
`launcher/Ome.Launcher` for the target, whose project gives the release form (Launcher, "Publishing"), with one
MSBuild node, no build servers and the conserving GC, into `build/launcher-publish/<rid>/`. It keeps the bundled
dependency list as `build/launcher-publish/<rid>.deps.json` for the notices check. It refuses an output that is not
exactly the launcher's files (the program, the target's natives, notice files in `OME-Launcher-notices/`, among
them the ones `LAYOUTS` requires): a `.pdb`, a loose `.deps.json` or another target's native fails it. Then it
copies them beside `OME`/`OME.exe`, refusing an export that has no engine. On Linux it prints the publish's peak
memory: its largest process (`getrusage`), and the job's memory cgroup peak where the cgroup has `memory.peak`.
The payload checks then require the launcher's files as the engine's (plan §9.3). They refuse the Windows launcher
and its natives, and any `.pdb`, and require the executable bit on `OME-Launcher` as on `OME`. The data folder must
hold exactly the engine's library, the runtime's two notice files and the FFmpeg bundle (`DATA_FILES`). The library is
checked with `readelf` (`package_linux_x64.verify_engine_library`): an x86-64 ELF file that exports
`godotsharp_game_main_init`, needs only glibc's own libraries, and needs no glibc newer than the FFmpeg manifest's
`minimum_glibc` (2.28).

The packager adds `LICENSE`, `README.md`, `THIRD_PARTY_NOTICES.md`, Godot's notices, a source/dependency
`BUILD-INFO.json`, and per-file `SHA256SUMS` to a stable top-level directory. It normalizes archive ownership,
modes, ordering, and timestamps using `SOURCE_DATE_EPOCH` or the source commit time. Outputs are the loose
export under `build/export/linux-x64/`, the staged installation and `package-smoke.log` under
`build/package/`, and `build/package/OME-linux-x64.tar.gz`.

**Third-party notices.** Both packagers copy `notices/godot/LICENSE.txt` and `COPYRIGHT.txt` to the package root
as `Godot-LICENSE.txt` and `Godot-COPYRIGHT.txt`. They refuse either file if it differs from its SHA-256 in
`notices/godot/notices.json` (`tools/package_notices.py`). The self-contained publish of each program (the root
`Directory.Build.targets`, target `PublishPackageNotices`, which `godot/OME.csproj` and the launcher opt into with
`OmeProgram`) copies the root `LICENSE*`, `THIRD-PARTY-NOTICES*` and `NOTICE*` files of every NuGet package it ships
as `<package id>-<file name>`: the engine's into the data folder, the launcher's into `OME-Launcher-notices/`
(`OmePackageNoticeFolder`). A NativeAOT publish leaves the runtime pack out of its file list, so the target adds it
back for the engine. The verifiers require the .NET runtime pack's two, and the launcher's package notice files that
`publish_launcher.LAYOUTS` lists. They also read two dependency lists, which the publishes kept: the engine's as
`build/engine-publish/<rid>.deps.json` and the launcher's as `build/launcher-publish/<rid>.deps.json` (`verify`'s
and `package`'s `--engine-deps` and `--launcher-deps` name other copies). They refuse an export that ships a NuGet
package `THIRD_PARTY_NOTICES.md` does not name in backticks, and an export with either list missing. A shipped package is a package or
runtime-pack library with runtime, native or resource assets; a runtime pack counts as its framework
(`Microsoft.NETCore.App`). A Godot upgrade changes `release_tag` in `tools/godot-linux-x64.json`, and
`test_package_notices.py` then fails until `notices/godot/` holds the new tag's files and pins.

The Windows packager also copies `notices/mingw-w64/COPYING.MinGW-w64-runtime.txt` as
`MinGW-w64-runtime-COPYING.txt`, pinned the same way (`notices/mingw-w64/notices.json`). It covers the MinGW-w64
runtime code that MinGW links statically into the shim and the FFmpeg DLLs (`THIRD_PARTY_NOTICES.md`, "MinGW-w64 and
GCC runtimes"). Both MinGW builds write an ld link map: `build-win64.sh` writes `ome_movie_ffmpeg.map` beside the
shim, and `build-ffmpeg-sdk.sh win64` passes `LDSOFLAGS=… -Wl,-Map=$$@.map` to make, not to configure, so the
configure line FFmpeg embeds in its libraries stays the manifest's (a rebuilt SDK matches the published one apart
from PE timestamps). Each then runs `tools/mingw_runtime_notices.py`. It fails unless every archive member and object
file under the compiler's libgcc and MinGW-w64 directories comes from one of these:
- the MinGW-w64 runtime: `libmingw32.a`, `libmingwex.a`, `libmoldname.a`, `libmsvcrt.a`, `dllcrt2.o`;
- GCC's runtime: `libgcc.a`, `libgcc_eh.a`, `crtbegin.o`, `crtend.o`;
- an import library's dlltool stubs (`<library>h.o`, `t.o`, `sNNNNN.o`).
It also fails when the compiler's MinGW-w64 version (`__MINGW64_VERSION_*` from `_mingw.h`) differs from
`release_tag`. Anything else, such as a static winpthreads (`libpthread.a`), needs its license reviewed and its
notice shipped before the check may accept it.

Both packagers also copy `notices/noto-sans-jp/LICENSE.txt` as `NotoSansJP-LICENSE.txt`, pinned the same way
(`notices/noto-sans-jp/notices.json`): the SIL Open Font License of the Noto Sans JP font that `OME.dll` embeds for the
options panel (`THIRD_PARTY_NOTICES.md`, "Noto Sans JP"). `test_package_notices.py` also checks that the committed
font's SHA-256 is the one `THIRD_PARTY_NOTICES.md` records.

Before reporting success, the command launches the staged executable as
`OME --headless -- --package-smoke`. This asset-independent mode runs before game-root discovery
and proves that the packaged engine can read all 548 embedded opcode records and dynamically load the bundled
native movie shim plus its local FFmpeg dependencies at ABI version 3. It also reaches the code a NativeAOT release
compiles differently from the JIT the tests run under: it reads the embedded game manifests, the shell's strings and
every shipped package, round-trips a session snapshot, and compiles a Lua chunk. It does not exercise game
archives, fonts, rendering, audio output, or a desktop window; retain the installed-game self-test below as a
separate trusted/runtime gate.

The smoke runs through `package_linux_x64.py smoke PACKAGE_ROOT --log FILE` with `--hold`, which keeps the engine
running after its line until its standard input closes. The line must reach the pipe while the engine runs, and
the engine must then exit 0. That proves an exported engine flushes its output after each print, which
`godot/project.godot` asks for with `application/run/flush_stdout_on_print=true` (Godot does it unasked only in
debug builds). The launcher captures the engine's output live (launcher spec §8.5), and without the flush a Linux
release engine's lines would wait in stdio's buffer and be lost when it is killed. `test_release_workflow.py`
checks the setting.

Two launcher steps follow, each adding to the log (launcher plan §9.4). Both run on the package as a player has it:
a copy (hard links where it can) in `OME package`, a folder whose name has a space, in a temporary folder outside
the repository. A program inside a checkout links with the repository's toolchain (`ModLinkBase.ForGame`), so the
staged package would not show the release's behaviour; a `TMPDIR` inside the repository is refused. Both steps run
with `DISPLAY` and `WAYLAND_DISPLAY` removed, and with `HOME` and the XDG folders in a scratch folder, so neither
reads nor writes the machine's own state.
- **The launcher's check (gate 11):** `OME-Launcher --check smoke --home HOME`. The temporary home holds
  `profiles/smoke/` in native mode on a synthetic Himegari install. Its `SYS4INI.BIN` is the smallest complete
  catalog: the header of the first catalog identity in `engine/Ome.Engine/Profiles/himegari.json`, and a directory
  of one archive and one placeholder entry. The launcher must exit 0, with each of its six checks (Install,
  Fingerprint, Root, Packages, Link, Lock) passed. Then the log gets `LAUNCHER CHECK OK: checks=6`.
- **The hand-over (launcher spec §5.6):** the copy's `OME-Launcher` is replaced by a shell script that records how
  it started (the hard link is removed first, so the package's own file is untouched). Both runs start from a folder
  with no game:
  - `OME --headless -- --game-root <that folder>` must keep the engine: it exits 2 with its own error, and the
    stand-in does not start;
  - `OME --headless` must start the stand-in once, with no arguments, and exit 0.

  Then the log gets `HAND-OVER OK`, and the copy is removed.

`build-linux-x64.sh` and the release publisher both require all three lines (`package_linux_x64.SMOKE_MARKERS`).

The focused component commands are:

```bash
./tools/bootstrap-godot-linux-x64.sh
python3 -X utf8 tools/publish_engine.py --runtime linux-x64
python3 -X utf8 tools/publish_launcher.py --runtime linux-x64 --into build/export/linux-x64
python3 -X utf8 tools/package_linux_x64.py verify build/export/linux-x64
python3 -X utf8 tools/package_linux_x64.py package build/export/linux-x64
python3 -X utf8 tools/package_linux_x64.py smoke build/package/staging/OME-linux-x64 --log build/package/package-smoke.log
```

The bootstrap prints the pinned editor path. The verifier prints the accepted export directory; the packager
prints the staged root and archive path. Those Python components and the guarded publish proxy have source-only
synthetic regressions in the core validation level.

**Windows x64 cross-export** uses the same pinned Linux editor and publish proxy, with MinGW and the selectively
installed Windows template. Its engine library comes from a Windows host: `publish_engine.py --runtime win-x64`
there (the release workflow's `windows-engine` job), copied into `build/engine-publish/` with its list and stamp.

```bash
./tools/build-windows-x64.sh
```

The command requires Python 3.11, the `global.json` .NET SDK, MinGW-w64 GCC/binutils, `curl`, and `sha256sum`.
It regenerates runtime metadata, builds and statically verifies the pinned Windows FFmpeg bundle, takes the engine
library for this commit (`publish_engine.py --check`), performs the real Godot `Windows x86_64`
release export (the preset's `application/icon` names `res://assets/ome-icon.svg`, which Godot draws into
`OME.exe` at 16 to 256 pixels; the project's `application/config/icon` names it too, which every export
stores in its `.pck` and shows on the window from the start), and then cross-publishes the launcher beside it with the SDK alone (`publish_launcher.py --runtime
win-x64`, as on Linux; plan §3.2). No produced Windows program is executed. `package_windows_x64.py` requires the Godot AMD64 EXE,
PCK, and a data folder of exactly the engine library, the runtime's two notice files and the FFmpeg DLL/license set
(`DATA_FILES`). It checks the library with objdump (`verify_windows_native.verify_engine_library`): an AMD64 PE that
exports `godotsharp_game_main_init` and imports neither a Visual C++ redistributable nor a .NET runtime. It also
requires the launcher: `OME-Launcher.exe`, an AMD64 PE with the Windows GUI subsystem, its three AMD64 natives, and
its notices. It rejects Linux shared objects, the Linux launcher and any `.pdb`. It embeds `BUILD-INFO.json`,
`WINDOWS-VERIFICATION.json` (whose `engine` and `launcher` sections record those checks), the notices (Third-party
notices, above), and a complete `SHA256SUMS`. The normalized output is
`build/package/windows-x64/OME-windows-x64.zip`.

Focused commands are:

```bash
./tools/bootstrap-godot-linux-x64.sh windows-x64
# on Windows, then copy build/engine-publish/ across:
#   py -3.11 -X utf8 tools/publish_engine.py --runtime win-x64
python3 -X utf8 tools/publish_launcher.py --runtime win-x64 --into build/export/windows-x64
python3 -X utf8 tools/package_windows_x64.py verify build/export/windows-x64
python3 -X utf8 tools/package_windows_x64.py package build/export/windows-x64
```

The bootstrap defaults to `linux-x64`; passing `windows-x64` changes only the selectively installed template.
The package verifier needs `x86_64-w64-mingw32-objdump` unless `--objdump` names an equivalent tool. On a real
Windows host, an extracted package can optionally run the same asset-independent dynamic gate as Linux:

```powershell
.\OME.exe --headless -- --package-smoke
```

That command proves the embedded 548-opcode table and local FFmpeg ABI 3 load, and reaches the NativeAOT-compiled
readers the smoke covers. It is a manual acceptance aid, not part of Linux-hosted Windows CI. To play a package's
engine under the driver, use `drive.py port start --package <folder>` (Game driver).

A Windows host cannot build the Linux package: NativeAOT compiles the Linux library on Linux only. The Windows-hosted
`tools/export-linux-x64.ps1` was retired for that reason on 2026-11-18; build in WSL or on Linux with
`build-linux-x64.sh`. Output is `build/export/linux-x64/OME` with `OME.pck` and the `data_OME_linuxbsd_x86_64/` data
folder. Do not relocate only the executable; the three items are one artifact.

The portable font policy requires a Japanese-capable system font. A minimal Ubuntu install needs, for
example, `sudo apt install fonts-noto-cjk`; absence is a startup failure rather than silent missing-glyph
output. The exported headless smoke command is:

```bash
./build/export/linux-x64/OME --headless -- \
  --selftest --game-root "/absolute/path/to/games/himegari" --text-backend portable
```

This validates the packaged .NET/Godot runtime, archive access, portable text realization, retained
rendering logic, audio payload parsing, and teardown. It does not validate a desktop window, physical
input, or audible output; run those gates on a real Linux desktop.

With the exported executable placed in an AGE install, a no-argument launch therefore uses that install
and auto-selects a built-in profile from SYS4INI's catalog revision/title before starting that profile's
natural boot script. Direct development runs are hosted by the Godot
editor executable, so pass `--game-root <install>` after Godot's `--` separator. The tracked
`run-godot.ps1`/`.cmd` launchers resolve Godot and the game root without machine-specific tracked paths.
Explicit `-GodotConsole`/`-GameRoot` values win, followed by `OME_GODOT_CONSOLE`/`OME_GAME_ROOT`; Godot
then falls back to `godot4`, `godot`, or `godot-mono` on `PATH`, while the game root falls back to the
selected profile's conventional sibling (`../games/himegari` or `../games/kamidori`) only when it contains
`SYS4INI.BIN`. `run-godot.cmd -Kamidori` is the one-flag shortcut for the conventional Kamidori install:
it authoritatively selects the `kamidori` profile and install root while still allowing an explicit
`-GameRoot` override. `-OverlayRoot <directory>` is repeatable and `-AllowBmpAsAgf` opts into BMP payloads
under `.AGF` catalog names. `run-godot.cmd -Kamidori -TranslationPatch` is the convenience spelling for the
translated install `../games/kamidori-translated` (unless `-GameRoot` is given) with its `patch/` overlay plus that BMP
compatibility option; neither behavior is inferred from the game profile. `-Mod <folder or .omp>` (repeatable) forwards each path, resolved against the caller's
directory, as `--mod`, and `-NoMods` forwards `--no-mods`; `-Language <tag>`, `-TextRoot <dir>` (resolved the same way)
and `-TextLog` forward `--language`, `--text-root` and `--text-log`, and `-LuaErrors continue|halt`, `-LuaDev`,
`-LuaTrace` and `-LuaReload` forward `--lua-errors`, `--lua-dev`, `--lua-trace` and `--lua-reload` (windowed runs
only; `docs/modding/cli.md`).
`-FpsCounter` forwards `--fps-counter` to display a low-overhead live FPS/frame-time overlay.
For an immediate native Vulkan startup crash involving injected overlay DLLs, see the tested
[Windows overlay diagnosis and process-local workaround](platform-portability.md#windows-vulkan-startup-and-injected-overlays).
`run-godot.ps1 -Doctor` prints the resolved repository, Godot, game root, ordered overlays,
BMP compatibility, requested game/probe state, .NET, and Python prerequisites without building or
launching. `-Game <id>` (alias `-Profile`) forwards an authoritative `--game`; `-Probe` requires it and forces the
read-only diagnostic policy. The launchers leave the natural boot script to the selected manifest and pass
neither `--scene` nor seeds. Examples below focus on their
feature-specific arguments and assume the game root is already selected this way. `--headless` can't
render texture ops (no GPU context) — run windowed for real scenes.
On VM termination the console prints the exact halt reason and step count before the generic on-screen
`-end-` marker.

Every engine command has the shape `OME [Godot's flags] -- [the engine's flags]` (launcher spec §5.1). The engine
reads only the arguments after Godot's `--` (`OS.GetCmdlineUserArgs`), so `OME --data-root R` passes it nothing,
and the form is `OME -- --data-root R`. Godot's own flags, such as `--headless` or `--rendering-driver`, go before
the `--`: Godot reads them before managed code runs, so no file the engine reads can carry them. User args (after
`--`):

The local launchers also accept **`-StartupDiagnostics`** for a native-faithful cold boot through TITLE,
Game Start, and SC0000. The switch overrides `run-godot.cmd`'s usual `-NativeDebugMenu` convenience for
that run and adds no `--scene`, seeds, timing changes, screenshots, or input injection. It writes four
disposable files under `build/validation/title-newgame/`: `godot.log` (Godot warnings/errors),
`timeline.jsonl` (ordered VM/host/compositor evidence), `histogram.txt` (executed opcode and hot-call-site
counts), and `page-map.jsonl` (authoritative wait/text locations and call stack). After Godot closes, the
PowerShell launcher reports each artifact's byte size and warns if any was not flushed. Run
`run-godot.cmd -StartupDiagnostics`, choose Game Start normally, stop at the first stable SC0000 input
wait, optionally press F3, and close the window normally. Until shutdown cancellation is separated from
ADV input release, normal window close can append one trailing shutdown-only record to `page-map.jsonl`;
use the last `input-wait` event in `timeline.jsonl` as the authoritative manual stop coordinate.

- `--game-root <directory>` — select the read-only AGE installation supplying `SYS4INI.BIN`, loose
  overrides, AAI append catalogs, and ALF archives. Absolute paths are launcher-friendly; relative paths
  are current-working-directory-relative. Content location remains independent from profile selection.
- `--overlay-root <directory>` (repeatable) — prepend an explicitly selected read-only loose-file root to
  asset and script lookup. Relative paths are game-root-relative. Roots are searched in argument order,
  followed by the ordinary game root and then the catalog-selected ALF range; duplicates are ignored while
  preserving the first position. Missing/non-directory roots fail startup. Nothing auto-mounts `patch/` or
  otherwise infers a translation from a profile.
- `--mod <path>` (repeatable), `--no-mods`, `--skip-failed-mods`, `--forget-mod <id>` (repeatable) — the mod
  loader (`docs/superpowers/specs/2026-11-03-mod-loader-design.md` §9). Startup links the profile root's `mods/` plus
  every `--mod` package (or reuses `mods/.cache/`), writes `diagnostics/mod-link.txt`, and stops with exit 2 on a
  failed link unless `--skip-failed-mods`. The linked layer outranks `--overlay-root` roots and the game root. Flag
  semantics, the profile root's mod files, save stamps and the outside-the-repository limits are documented for
  modders in `docs/modding/cli.md`.
- `--lua-errors continue|halt` — what a Lua handler's error does (Lua scripting spec §5.5): `continue` (the default)
  logs it to `diagnostics/lua.log`, shows a notice in the status line, and switches the handler off after its third
  failure in a row; `halt` ends the run at the instruction. Another value fails startup (exit 2). When a mod ships
  Lua the startup log prints a `[lua]` line with the mods, the handler count and the policy; with none, no Lua
  runtime is created unless the driver's `lua` step asks for one. Modders' reference: `docs/modding/lua.md`.
- `--lua-dev` — while writing a mod (spec §9.3, §13): a mod loaded from a folder that requires a base mod may write
  an exported name its `[lua] writes` list lacks; `diagnostics/lua.log` names each such write once per mod and name,
  and the `[lua]` line lists the mods it applies to. A packed `.omp` keeps its list. The link cache keys on the flag.
- `--lua-trace` — the attribution trace (spec §12): one JSON line per handler call in `diagnostics/lua-trace.jsonl`
  (started anew by the run's first line), with the mod, handler, hook, kind, `at`, `ms`, `result`, `within`, the
  declared fields before and after, and each write through `maid.vm`, an accessor or `ctx.args` (`Scripting.Trace.cs`);
  a site or script hook's `when` and `cause` (with `caller` for a frame `maid.vm.call` ran), `skip` on the handler
  that skipped, a watch's cell, old and new values and writer, and the scripts the handler called (`calls`: script,
  result, `through` for a base mod's helper). Every line has `instructions` (in steps of 1,000) and `budget`; a data
  file's delivery names its contributor as `mod`, with `file` and `owner` (data patches spec §5.7), and a failed one
  `put_back`, the cells its journal put back (data patches plan §12.6).
  The `[lua]` line says when it is on. Format: `docs/modding/lua.md`, "The link report and the trace".
- `--lua-reload` — while writing a mod (spec §13): each unpacked mod that ships Lua has its `lua/` watched
  (`LuaReloadWatcher`, `godot/Main.LuaReload.cs`; a `[lua] --lua-reload watches` line names the folders). Once its
  `.lua` files settle (300 ms), they are read and checked as the link checks them (`ModLuaReload`: syntax, module
  names, each handler still a top-level function of its file) and handed to the scripting native through the waits
  native's requests; the session is rebuilt at the next instruction boundary with no Lua on the stack and outside a
  wait's callback, keeping each mod's `maid.state`, and `ome.session.start` fires with `reload`. A refused check, a
  state that does not serialize, or a build that switches off a mod that was on keeps the running session;
  `diagnostics/lua.log` and a notice say why. The link does not run again: a manifest or `events/` change needs a
  restart. Modders' reference: `docs/modding/lua.md`, "Reloading".
- `--allow-bmp-as-agf` — opt into decoding a `BM` payload found under an `.AGF` catalog record as an
  uncompressed Windows BMP. The supported compatibility surface is `BITMAPINFOHEADER`-compatible `BI_RGB` at
  1/4/8-bit paletted (opaque), 24-bit opaque, or 32-bit straight alpha, including top-down images and padded rows. Without this option,
  such a payload fails with an actionable diagnostic; ordinary `ACGF` decoding is unchanged. This is an
  asset-format compatibility option, not an opcode or SYS-revision handler.
- `--game <id>` — authoritatively select an embedded game (`himegari` or `kamidori`). Omit it for
  exact catalog revision/title auto-detection. `--profile <id>` is the same option under its old name, with no
  warning; the last of either wins, in the port and in `Ome.Cli` alike (`GameProfileSelection.RequestedGameId`),
  and `Ome.Cli`'s conventional game root follows that same value. An explicit identity mismatch emits the
  expected and detected identities but continues with the requested game; the current K0 safety policy
  suppresses persistence writes for mismatches. An unknown game id is an error rather than a fallback.
- `--probe` — require an explicit `--game` and run it read-only for diagnostic inspection of an unknown or
  incomplete install. It disables save and `SYS4REG.INI` stores. Both built-in profiles write saves (Kamidori since `ce4eb1c`,
  2026-09-23), each in its own `games/<game>/` namespace.
- `--data-root <directory>` — the profile root (`SAVE/`, `SYS4REG.INI`, `OME.INI`, `mods/`, `diagnostics/`)
  instead of `user://games/<game>/`. A relative path resolves against the launch directory, as `--game-root` and
  `--mod` do. Every run holds its root's `run.lock` (`RunLock`, `engine/Ome.Mods/Launch/`; launcher spec §5.5), on the
  default root too. The engine takes it after the root is known and before anything writes into it, and sets the
  file's last-write time ("last played") once it holds it. The launcher's own lock, taken while it writes a profile,
  leaves the time alone, and sets it to 2000-01-01 (`RunLock.NeverStarted`) when its open created the file, so a
  profile the launcher made reads as never played (`RunLock.LastStart`). A second start on a held root stops with the reason
  (exit 2), having written nothing. Only a held lock means running: a crash leaves the file, and the next start
  takes it. When the root holds a `profile.toml` (launcher spec §4.3), the run is that profile: one resolver
  (`RunResolver`, `engine/Ome.Mods/Launch/`), shared by the port and `Ome.Cli`, maps its typed keys to the flags they
  stand for, so the existing selections read them. Every `Ome.Cli` command that takes `--data-root` goes through
  it, so `omp check --data-root <profile root>` needs no game option (launcher spec §5.7; gate 6 is
  `OmeCliProfileTests`). The file:

  ```toml
  ome_profile = 1                  # format version; another stops startup with the version this engine reads
  name = "Kamidori (English)"      # optional
  game = "kamidori"                # required: the game, which must match the install's SYS4INI.BIN header
  mode = "native"                  # required: "native" or "extended"
  [install]                        # required
  root = "D:/Games/Kamidori"       # --game-root; absolute or relative to the profile root
  fingerprint = "sha256:…"         # optional: a different SYS4INI.BIN prints a warning and continues
  overlays = ["patch"]             # --overlay-root, in order, relative to the install root
  allow_bmp_as_agf = true          # --allow-bmp-as-agf
  [mods]                           # optional; without it the root's mods/ folder works as before
  folders = ["../../mods/kamidori"]
  order = ["example.one", "-example.two"]   # -ID: disabled, place kept
  on_failure = "stop"              # "skip": --skip-failed-mods
  [launch]                         # each optional: --render-backend gpu|software, --text-backend auto|gdi|portable,
  renderer = "gpu"                 # --lua-errors continue|halt; a flag on the command line wins for that run
  text_backend = "auto"
  lua_errors = "continue"
  [launcher]                       # the launcher's own; the engine reads none of it
  ```

  Every key is typed, and outside `[launcher]` an unknown key or table is an error, as in a package manifest; the
  file holds no engine arguments and no environment. With a profile file, `--game`/`--profile`, `--game-root`,
  `--overlay-root`, `--allow-bmp-as-agf` and `--probe` on the command line stop startup with the reason (exit 2),
  and so does a game the install does not match; without one, a data root behaves as before. Every single-valued
  option (`--game`, `--game-root`, `--data-root`, `--render-backend`, `--text-backend`, `--lua-errors`) keeps one
  occurrence, the last given, so the port and `Ome.Cli` read the same value. The startup log adds a
  `[profile-file]` line with the file and the values in effect. A root without the file: the driver's sandboxes.
  With a `[mods]` table, discovery (`ModLaunch.DiscoverTable`; launcher spec §5.3) works in three steps:
  - It indexes every candidate of every folder by its manifest alone (`PackageIndex`, `engine/Ome.Mods/Packages/`),
    and opens and hashes only the ids `order` enables and the `--mod` packages.
  - An unreadable candidate is a note. A candidate for another game is left out unless no candidate for this game
    has an enabled id.
  - These stop the start before the link, or with `--skip-failed-mods` drop the mod: an enabled id in no folder, an
    id found twice, and an enabled package whose hard dependency is in a folder but not enabled.

  A base mod joins when an enabled package requires it. A folder's copy of it replaces the engine's only when
  `order` enables it, and an enabled base id with no such package is a note. `mods/load-order.txt` is not read, and
  one that exists is a note. `selectors.toml` and `.cache/` stay in the profile root's `mods/`. The host's early "no
  mods" test and `omp check`, `export`, `explain`, `lua-stubs` and `text` follow the table.

  The profile's `mode` is checked on the discovered packages, with or without a `[mods]` table (`ModeCheck`,
  launcher spec §5.4). `native` refuses an enabled or `--mod` package that targets `extended`, ships Lua (a Lua
  library has no target), or requires a package the mode refuses, the engine's base mods included, since they target
  `extended`. The check runs before a base mod mounts, so the refusal names the package that was enabled. Such a
  package stops the start, or with `--skip-failed-mods` is dropped. `extended` admits everything, as a run without a
  profile file does. `omp check` and the other `omp` commands apply the same mode.

  Before anything the file names is opened, the resolver checks it (`RootSafety`, launcher spec §12). The check
  applies only to a root with a profile file. A link inside the root is allowed: the user dropped spec §12's link
  rule on 2026-11-16 (launcher plan §9.7).
  - A Windows network path (`\\host\share\…`, `//host/…`, `\\?\UNC\…`) that the file names for `install.root`,
    `install.overlays` or `mods.folders` stops startup with each key and path, unless this machine's player approved
    the location. A path inside the profile root needs no approval. This check runs before the install is read, so
    nothing on the share is opened.
  - The approved locations live in `machine.toml` in Godot's user folder for this project (`%APPDATA%\Godot\app_userdata\Open Maid Engine\`;
    on Linux `$XDG_DATA_HOME/godot/app_userdata/Open Maid Engine/`). That is outside OME home and every profile, so
    a root from elsewhere brings no approval. The launcher writes the approvals; the engine only reads them.
  - The same file holds `cache_value`, a random value the first engine (or launcher) that links mods makes once
    (`MachineState.CacheValueFor`). It joins every link cache key (`ModLinkBase.MachineValue`), so a cache entry
    that another machine wrote is linked again rather than mounted (launcher spec §5.3). A `machine.toml` that cannot
    be read or written gives a value for that run only, with a `[mods]` warning, and the next start links again.
  - A profile file's `mode` joins the key of a link that dropped failed mods (`--skip-failed-mods`), since the mode
    decides which mods fail.
  - `--allow-network-paths` approves the file's network paths for one run. A share reached through a drive letter
    and a network mount on Linux are ordinary paths.
  - The host passes Godot's own user folder. `Ome.Cli` computes it by Godot's rule (`MachineState.DefaultFolder`),
    and the host's `--selftest` checks that the two agree (`machine-folder=ok`).
- `--scene <NAME>` — start in one script instead of the natural boot, e.g. `--scene SC0240`. When the profile
  declares the F4 launcher's coordinator (Himegari), the scene is entered as that launcher enters it
  (`VirtualMachine.HostScene`; `engine-re.md`, "Native ADV hosting and Himegari's developer entry"):
  - `SYSTEM4` starts as after a return to title, so it skips LOGO and OP, runs its boot scripts and ADV setup,
    and hosts the scene;
  - TITLE returns at its entry with the launcher's writes (`G[0]=1`, `G[0xaba5c]=-1`, `G[0x62ccf]=0`) and the
    scene's packed id;
  - the run ends when the scene returns;
  - `--scene TITLE` is refused (TITLE is the coordinator).

  So a scene that only writes state and returns ends the run at once. DEBUGMAP is one: it fills the debug party and
  items, sets the system-flow request `G[0]=3` and returns, and SYSTEM4 then enters `FIELD`
  (`docs/phase-b-framework.md`, "DEBUGMAP field-entry result"). Enter such a scene with the F4 launcher, after which
  SYSTEM4 carries on (`DebugSceneLaunchTests.RealDebugMapLaunchContinuesPastSc0270Page8Wait` follows that path through
  `FIELD` into SC0270).

  A profile without a coordinator (Kamidori) runs the script as the root with no setup.
- `--selftest` — headless; runs a **synthesized** scene through the thread/suspend/`CallDeferred` plumbing and
  asserts it matches a live headless run (full handling; no vm0/frozen golden). Profile-owned fixture checks
  use the selected install's proven assets; the complete self-test passes for both built-in profiles. Exits.
- `--text-backend auto|gdi|portable` — select gameplay glyph-mask rasterization. `auto` (default) uses the
  exact GDI gray-4 backend only when the Windows ACP-932 gate passes, otherwise the explicitly non-identical
  Godot TextServer Unicode policy. `gdi` fails startup when exact selection is unavailable; `portable`
  never initializes GDI and is the cross-platform acceptance/debug mode. The selected backend and portable
  policy id are printed at startup.
- `--window-width <pixels>` / `--window-height <pixels>` — override only the physical windowed client
  dimensions for this run; each omitted dimension defaults independently to `OME.INI`'s window size (below), else
  the SYS4INI logical canvas. Godot keeps that logical canvas and aspect-preserving letterboxing, so these options
  do not change VM coordinates, AGE surfaces, layout, or backbuffer allocation. Values must be integers in
  `1..16384`; duplicate options use the last value.
- `--fps-counter` — show Godot's rendered FPS and corresponding average frame time in the top-right corner,
  refreshed four times per second, for this run, whatever `OME.INI` says. The overlay observes the presentation loop
  only; it does not alter VM timing, input, compositor selection, or opcode handling.
- `--language TAG` — the language for this run, whatever `OME.INI` says: any well-formed tag (`ja`, `en`, `pt-br`,
  `zh_Hant` reads as `zh-hant`), or `x-pseudo`. A malformed tag fails startup. It selects the engine shell's strings
  (the menu bar, the strip, 終了, the folder picker's prompt, the options panel, the name entry and AGE's error
  box; `ja` for a tag the shell has no table for unless the mods supply one) and the game text's language (localization spec §10): `ja` draws the scripts' text as written; `x-pseudo`
  presents every show-text run and `draw-string` a third longer and in brackets, with no catalog, to find text that
  bypasses the presenter and layouts that cannot take longer text; any other tag presents the catalogs `--text-root`
  holds for it and its fallback chain (its parents, `pt-br` then `pt`, unless `languages.toml` names another), and
  without any draws as written. The mods' `text/` catalogs and `[[language]]` tables load first, from the link cache,
  then `--text-root`'s; a later source wins at equal scope and per settings key. A language's
  `glyph_delay_scale` scales the reveal delay of presented runs (never to 0), and its `[[language.font]]` tables put
  their bundled files, then their family names, in front of the rasterizer's own fallback for presented text. While a language presents text, every string is drawn by the Godot TextServer
  rasterizer (spec §7.4) and presented text wraps at word breaks. Otherwise presented text is laid out as the same
  characters in the source would be: each glyph carries its CP932 code, so Kamidori's aspect mode gives it the
  source's cells, calibration, bracket placement and raise (`docs/modding/languages.md`, "Fonts").
  A message of several runs, such as a sentence split by furigana, resolves as one string: on an entry its first run
  draws the presented sentence and the rest draw nothing; on a miss each run resolves on its own (spec §6.2). A string
  read from a global cell is also looked up at the instruction that stored it (spec §5.2, §6.4), so a site entry on a
  table's store applies wherever the row is drawn. Patterns match strings the scripts build, and their captures are
  presented too (§6.3). `strlen`, `halve-strlen` and `cp932-character-length` measure the presented text of a string
  that has an entry, in half cells (§5.3), so a plate the script sizes from a name fits the presented name. History
  and saves keep the source text (§8): each run is also laid out as its source text, without drawing, and history
  records those rows. A save holds the glyph objects the source text would have bound, so with `measure = "source"`
  it decodes as the source language's does. The backlog (op `0x1d1`) and the redraw after a settings change (op
  `0x82`) present each group again in the current language: by the lookup recorded this session, or after a load or
  language change by matching the records' text against unscoped entries. A group with nothing presented draws as
  recorded. An entry's `align` and `fit` place presented text against its source text's extent (spec §7.2): the
  source layout pass's for a run, the source string measured for a `draw-string`; `fit = "shrink"` narrows the glyph
  masks in the glyph cache (`GlyphRasterRequest.WidthPercent`), down to the language's `min_fit_scale` (default
  0.75). A run that would run past its layout's bottom (§7.3) is narrowed, under the language's `overflow = "shrink"`
  (the default), to the widest width at which it fits, down to the same floor: `PresentedTextPlacement.Place` halves
  between the floor and the width that did not fit, and the host decides each width by laying the run out with the
  options it draws with, without drawing. A run that fits nowhere above the floor, or with `overflow = "cut"`, is cut.
  Both go to the miss log. The timeline's `adv-text-layout-result` events carry `width_percent`, `narrowed_to_fit`,
  `row_shifts` and `cut_rows`, and `draw-string` events a `placement`. Not yet: ruby in
  presented text. The startup log's `[text]` lines
  name what loaded. The shell's own text (spec §11) takes the sources' `[[engine]]` entries for the language, then
  the shell's embedded table for it, along the fallback chain, then Japanese; an entry the shell cannot use (an
  unknown key, or a value its Japanese does not take) is left out with a `[text]` warning. AGE's error box and op
  `0x1b4`'s box show in that language too, while the drive protocol keeps their Japanese. A language with only
  engine text leaves the game text as written. Switching the language in the options panel, whose OME section lists
  the shell's languages and every language the sources define by `name`, applies at once to the shell and to text
  drawn afterwards.
- `--text-root DIR` — mount a folder of catalogs laid out as a package's `text/` (spec §9.1): `DIR/<tag>/*.toml`,
  loaded in path order after the mods' catalogs, later files winning at equal scope. A malformed file stops startup
  with every problem and its file and line. Site entries (`at`) bind only when the file's `fingerprint` matches the
  installed base script and no mod replaces it; in a script a mod patches they move through the link's offset table,
  and one on an instruction a patch replaced is dropped. The `[text]` lines report the entries dropped.
  `DIR/languages.toml` declares `[[language]]` tables as a package manifest does (spec §9.2; keys in
  `docs/modding/languages.md`), and names bundled fonts as `fonts/NAME`, read from `DIR/fonts/`; a missing one stops
  startup. While the root or the mods define any language the VM records which instruction stored each global string,
  whichever language is selected. This is how to play the local catalog `omp text import-scripts` writes without
  packaging it, for example Kamidori in English: `--game kamidori` with `--language en --text-root
  build/games/kamidori/import-en/text` and a `languages.toml` there declaring `en` (an absolute path when run through
  `drive.py`, whose port starts in `godot/`). A package cannot hold that file, since its manifest declares the
  language, so with `--package` load the import's output as a package instead, images included: `--mod
  build/games/kamidori/import-en --language en`.
- `--text-log` — write every string that reached a presentation site without a catalog entry to
  `<data root>/diagnostics/text-misses.jsonl` (spec §14), one JSON object per line: `source`, `script`, `at` (the
  instruction, as the F3 locator prints it) and `count`. Written every 2 seconds when it changed, and at exit.
  A message of several runs that misses is logged once, as its joined text at its first run; a string that is only a
  number is never a miss, and the length opcodes log nothing. Under `x-pseudo` every string is a miss. A presented
  message narrowed to fit its layout (spec §7.3) gets a line of its own after any miss at that site, with
  `width_percent`, the narrowest it was drawn; one whose rows still ran past its layout's bottom gets another, with
  `rows_cut`, the most rows it lost. Each line's `count` is how often that happened.
- **`OME.INI`** — the port's own options, shared by every game (engine shell spec §5.6): `[ui] Language` (a language
  tag; `ja`, the default; a malformed tag reads as `ja`), `[display] WindowWidth`/`WindowHeight` (the windowed game area; 0 or a missing or invalid pair is
  the canvas), `[debug] FpsCounter` and `LocatorHud` (0/1). It also keeps `[display] WindowLeft`/`WindowTop`, where
  the window was left, which the port writes at exit and reads at startup for a game whose catalog sets
  `SAVEWINPOS` to 1 (`engine-re.md`, "The kept window position"); no option edits them. It lives in Godot's `user://` folder, or beside the saves
  under `--data-root`, so a driven run reads its sandbox's and never the player's; a self-test reads none. The
  options panel's 画面 and OME sections edit it, and its 決定 writes only the options the player changed, so a flag's
  value never reaches the file. A write keeps unknown sections and keys but drops comments. The startup log's
  `[options]` line names the file and the values in effect.
- `--no-menu-bar` — leave out the menu bar. By default every game gets the engine shell's bar (AGERC's menu 110,
  pruned by the game's `SYS4INI.BIN`) above the game canvas, drawn as native's Win32 bar: the window is 20 px taller,
  and the canvas, the cursor mapping and the dialogs sit below the bar (`engine-re.md`, "Native menu bar").
- `--windowed` — start in a window whatever `display:ScreenMode` says. The port otherwise starts in the mode it
  names, and a new profile's 0 is fullscreen: a borderless window over the monitor with the canvas scaled to fit
  (`engine-re.md`, "Screen mode"). A run without `SYS4REG.INI` (self-test, read-only persistence) also starts
  windowed. ﾌﾙｽｸﾘｰﾝ/ｳｲﾝﾄﾞｳ, Alt+Enter and the maximize box switch while the game runs, and each switch stores
  `display:ScreenMode`.
- `--desktop-dir DIR` — use DIR as the Desktop folder, where F1 and 表示画面を保存 write `CG%6.6d.BMP` while
  `system:SaveBMPPath` is empty and which 画像の保存場所 preselects for Himegari. The driver points it into its
  sandbox, as it redirects native AGE's Desktop.
- `--seed 0xADDR=VAL` (repeatable) — seed initial global state, e.g. `--seed 0xa57=1` unlocks Lily's form-A voiced
  dialogue. Address and value are decimal or `0x` hex. In a hosted `--scene` run the seeds apply at TITLE's return,
  after SYSTEM4's boot scripts, so they override boot state. A malformed seed is skipped with a warning, as is a
  `--speed` or `--sleep-scale` value that is not a number; numbers read the same in every locale (`1.5`, never
  `1,5`).
- `--native-debug-menu` — intentionally treat exit-request opcode `0x1` as a no-op, exposing TITLE's
  otherwise unreachable shipped developer menu. This is a non-native debug divergence and also suppresses
  SYSTEM4's invalid-execution-mode exit request for that run. Direct Godot and PowerShell launches leave it
  disabled unless explicitly requested. The local `run-godot.cmd` convenience launcher enables it by
  default; use `run-godot.ps1` without `-NativeDebugMenu` for a native-faithful launch.
- `--shot <png> [--shot-page N]` — capture page N to a PNG then quit (dev screenshot). At scene end it also prints the call-scripts executed as nested frames.
- `--shot-sequence <dir> [--frames N]` — dump one PNG per rendered frame (`frame_0000.png…`, default N=180 ≈ 3s @60fps) then quit, auto-advancing past ordinary input waits and input-cancellable modal movies. Verifies time-based retained effects and publication boundaries as distinct frames, which a single `--shot` cannot. CPU/IO-heavy by design (a PNG every frame); a dev diagnostic, not a normal run. e.g. `godot --path godot -- --scene SC0000 --boot --shot-sequence out/seq --frames 300`.
- `--sleep-scale <f>` — multiply every explicit `sleep` (op 0xc8) duration by `f` (default 1.0). This stretches only script-authored sleep holds; it does not slow ordinary opcode bursts or replace `0x20c`/`0x21c` presentation pacing. Debug-only; leave at 1.0 for real playback.
- `--speed <f>` — scale sleeps and retained presentation clocks without throttling ordinary opcode bursts or auto-advancing input waits. Values 0.05–8 are accepted; `--speed 0.25` is useful for transform inspection, while 1.0 is normal playback.
- `--gfx-log <file>` — **frame lowering + op diagnostic** (the tool that root-caused the grey background). Logs, per drawn frame, only the objects whose outcome in the graphics native's frame lowering **CHANGED** (drawn↔skip↔gone, resId, resolved file, `slot`, `src`/`dst`, `op`acity, `tintStr`ength) — quiet until something actually changes, so the exact frame a layer drops out (and why) stands out (`GfxDecisionLog`). Its frames draw with the reference renderer. Also prints every surface op, slot assignments included (the graphics native's `TraceOps`). Works live or with `--shot-sequence`. Use it before theorising about layering/blend/geometry: it showed the grey BG = the slot-selecting globals resolving to 0 → every texture collapsing into slot 0 (see engine-re.md §"Grey-background root cause"). e.g. `godot --path godot -- --scene SC0000 --boot --gfx-log out/gfx.log` then click to the bad page.
  Matrix-channel outcomes also include `base`, `anchor`, projected `dst`, sampled `scale`/`trans`, and
  one-shot-plus-cyclic `rot`ation angles. Active op-`0x202` outcomes include packed `color=current->target`
  and `colorProgress`, synchronized with the same frame/clock in `--timeline-log`. Parent directories are
  created automatically.
- `--transition-click-ms <n>` — diagnostic-only input injector: after a foreground transition has been active for `n` virtual milliseconds, send one click through the real input lifecycle. The click completes/consumes the transition and does not advance a stable page. Use with `--timeline-log`, `--gfx-log`, and windowed `--shot-sequence`; omit for normal play.
- `--hold-message-skip` — diagnostic-only held-input injector: hold Ctrl from boot through the VM's real
  keyboard binding path (native logical action 6). As on native, a held key starts fast-forward only at a
  text reveal that op 0x101 has not suppressed, and the ADV lifecycle ops end it (`docs/engine-re.md`,
  "Physical fast-forward"). Use it to compare held-Ctrl fast-forward against `--transition-click-ms` at the
  same page without relying on synthetic host state.
- `--timeline-log <jsonl>` — diagnostic-only synchronized event stream for a real Godot run. Records every
  executed script byte offset/opcode, script-frame entry/exit with depth/cause/outcome, resolved call-script
  ids/names, unknown-fallback `stub` events, virtual time/frame, VM state changes (`running`, `sleep`, `input-wait`,
  `halted`), audio events, and changed visible-object compositor outcomes in one ordered JSONL file. `now_ms` is the
  frame clock as of the last main-thread frame, so it moves in frame steps; `real_ms` is the writer's own real time
  since the log opened, at 0.01 ms, for timing VM-thread events between frames. Like `--gfx-log`, it draws with the
  reference renderer (`render_backend` 0), whose frames can take tens of milliseconds, so waits that meet a render
  run longer than in normal play. Combine with
  `--scene SC0000 --shot-sequence ... --gfx-log ...` to distinguish control-flow stalls from retained-object/compositor
  failures at an exact bytecode boundary. Relative output paths are project-relative (`godot/`).

**Godot performance frame log:** `--perf-log <csv>` enables a buffered, diagnostic-only capture of the real
Godot path. Each row carries VM `script`/`offset`/`opcode` at frame entry and again at the actual presentation
boundary (`present_*`), Godot `delta_ms`, measured main-loop
time, pulse/movie/UI time, and a compositor breakdown: recomposition, clear, retained snapshot, texture
resolution/decode, color-key/source preparation, raster, `Image.SetData`, and `ImageTexture.Update`. Workload columns report transition
state, retained object visits, time-varying object visits, drawn/fill/transition/skipped layers, integer/
affine/singular raster paths, dynamic/opaque/alpha/additive layers, source pixels, clipped affine bounding-
box candidate pixels, full-screen layers, main-thread managed allocations, and GC collection deltas.
Presentation-reason columns distinguish host publication requests, legacy screen transitions, retained VM
mutations, continuously sampled channels, and discrete spritesheet cell changes. Affine work is additionally
split into fractional translation, axis-aligned scale, and general affine layers while retaining the aggregate
`affine_layers` column for comparison with the first two captures. The
allocation total is split into recomposition, retained snapshot, compositor, source-preparation,
`Image.SetData`, and Godot UI phases so a normal windowed capture can attribute remaining managed garbage
without enabling a high-volume trace. The
`render_backend` column is `0` for a frame the reference renderer drew and `1` for one the GPU renderer drew;
`gpu_draw_items`, `gpu_texture_uploads`, and `gpu_texture_upload_ms` distinguish cheap retained-item updates
from new/static or changed/dynamic texture publication. Existing layer/transform/pixel workload columns are
populated for either backend, while `raster_ms`, `set_data_ms`, and `texture_update_ms` remain zero on a native
GPU presentation. GPU rendering is the normal backend; select the software renderer for every frame
with `--render-backend software` or the local launcher's `-SoftwareRenderer` switch. An explicit
`--render-backend gpu` is accepted but normally unnecessary.

The software renderer is what players see named (the launcher's Advanced tab, `renderer = "software"` in
`profile.toml`). In the engine it is the reference renderer (`engine/Ome.Engine/Hosting/ReferenceRenderer.cs`):
it defines what each frame shows, the GPU renderer draws the same frame or hands it back, and `reference-diff`
compares the two. It is slow on full-screen blended effects: when it was measured, the opening scene's exit
(SC0000) took 65 ms a frame at the median even after the CPU work of `phase-a-slice-plan.md` P2, which is why GPU
became the normal backend (P3, P4).

Since engine layering milestone 11 the frame is lowered on the engine thread, in the frame turn the front end asks
for each display frame (`docs/superpowers/specs/2026-11-12-engine-layering-design.md` §5.3). `pulse_ms` is the whole
turn: the handshake, the presentation reasons and the lowering. `recomposite_ms` runs from the lowering's start on the
engine thread to the end of the draw on the front end. The lowering's allocations are counted on the engine thread. A
turn the engine thread did not start within 4 ms publishes its frame in a later turn. That frame's row carries no
presentation reasons or decisions, and its recomposition starts at the draw.

The writer replaces the target, buffers 120 rows between flushes, and prints its frame/recomposition counts on
normal shutdown. Use a windowed Release-equivalent run at speed 1 for performance evidence; headless runs
validate the schema only. Before a baseline, verify that no older Godot game processes remain alive; an
apparently closed window can otherwise leave a renderer consuming CPU and contaminate later runs. Do not
combine baseline captures with `--shot-sequence`, `--gfx-log`, or
`--timeline-log`, whose diagnostics add substantial or differently shaped work. Example from `age-reimpl/`:
`godot --path godot -- --scene SC0000 --perf-log ../build/perf/sc0000.csv`.

`run-godot.ps1 -PerfLog` creates `build/perf/run-yyyyMMdd-HHmmss-fff.csv` and prints the absolute target
before Godot starts; normal shutdown flushes it and prints the captured frame/recomposition counts. The
performance effort's temporary `run-godot.cmd` opt-in was removed after GPU acceptance, so ordinary batch-file
runs no longer write a log. Selftests also remain unprofiled.

**Godot debug scene launcher:** press **F4** while the natural boot is showing TITLE. TITLE's visible menu is
a live 1 ms sleep/input-poll loop rather than an ADV `wait-for-input`; the launcher identifies that exact
active child frame and returns it cooperatively at the next completed opcode boundary. The overlay enumerates
all base and mounted-append `.BIN` records by packed id, with All/SC/SP/Debug/Other filters, name or exact
hexadecimal/decimal id search, and archive/raw-id details. `SYSTEM4.BIN` and `TITLE.BIN` are intentionally
unlaunchable. Launch is accepted only for the exact `SYSTEM4.BIN > TITLE.BIN` wait stack; it returns TITLE
with the coordinator globals queued on the VM thread, then lets SYSTEM4 perform its normal computed child
call. F4 outside TITLE prints an unavailable reason and changes no state. Cancel or Escape closes the panel.
The launcher does not seed story/profile state, jump to byte offsets, or force-switch an active child scene.

**Godot page locator:** every editor/development run recreates `build/games/<game>/page-map-<SCENE>.jsonl`, so
two games' same-named scenes keep separate maps; a packaged export writes the same automatic map below
`user://games/<game>/diagnostics/page-maps`. A map the port cannot open (another running port holds it, or
the directory is read-only) logs a `[locator]` warning, and the run keeps no map. Each
`wait-for-input` adds the
run-relative page, page-start location, canonical wait script/offset, last show-text instruction and string
offsets, text, and nested call stack. It also adds the page's text (localization spec §14): `dialogue`, the source
text of the page's runs joined with `\n` where a run starts a new row of its layout (two for a blank row between);
`breaks`, one class per `\n`, `wrap` when the row before it (trailing spaces dropped), a space (none between two
wide characters) and the next run's first word would cross the layout's right edge, otherwise `break`, which a blank
row, a run starting with a space and a change of layout always are (the importer's rule, measured with the run's
real layout); with a language active, `presented`, what the runs drew joined at the same new rows (a message's later
runs draw nothing); and `cut: true` when a drawn run stopped at its layout's bottom. Use `--page-map <jsonl>` to
override the output; pass that explicit path
to `locate_page.py --map` when resolving a map copied from an export profile. `--locator-hud` (or `OME.INI`'s
`debug:LocatorHud`) shows `SC0000 P014 · wait SC0000@0x… · text SC0000@0x…` at launch; **F2**
toggles it and **F3** copies the current locator to the clipboard. The offset remains authoritative because
branching/state can shift page ordinals between runs. Resolve a reported page with
`py -3.11 -X utf8 tools/locate_page.py SC0000 14`, adding `--game <game>` for a game other than Himegari.

## Launcher (`OME-Launcher`)

`launcher/Ome.Launcher` builds `OME-Launcher` (`OME-Launcher.exe` on Windows), a windowed net10.0 program over
`engine/Ome.Launcher.Core` (launcher spec §8). Its command line (spec §8.6) runs before any window and needs no
display. Where the command line's outcome names the window, the program opens it (Avalonia 12, launcher plan §6).
The window (launcher plan M3) has the profile list, the header, Play, the Overview tab
(the game, the profile with its mode switch, the mods in load order, the checks, and locating or accepting the
install) and the Mods tab (the library's packages for the game in the order the stack loads, each with a switch and
its state; dragging to set the order; the detail with the README; what the selected package does, in a card as wide
as the page under the list; the link report; each of those two cards opening and closing from its heading, a list of more than 8 lines starting closed, the choice kept until the window closes; adding a package from a folder or an `.omp`, removing one, and releasing a
gone package's selector) and the Settings tab (the language, fullscreen or
windowed start, the window size, the FPS counter and the page locator, each written to the profile's `OME.INI` or
`SYS4REG.INI` at once, as the engine writes them; the start mode waits until the install is found) and the Saves tab
(the save folder and its count; copying saves in from another profile of the game, with its selectors, or from where
the original game keeps them, the saves it would replace asked first) and the Advanced tab (the overlays, chosen from
the install's folders of files the catalog names, and the BMP switch; the renderer, text drawing, the Lua error policy,
what a failed link does and Direct3D 12, each written to `profile.toml` at once; the diagnostics folder, and a summary
of the profile, its checks, each package the Mods tab lists with what it does (names past 20 counted), the link report in `mod-link.txt`'s form and the log's end copied for a report, with the profile's, the install's and OME home's folders written by name and the account's folder as `~`). Each
change is followed by a check run that links the mod stack again, which a large package makes take seconds; until it
ends, the page shows what it is working on under the tabs, and the Mods tab's changes wait. "New
profile" opens the wizard: the install (browsed, dropped on the window, or one a profile already uses) with its
overlays, then the name, the mode, and what the profile starts from (nothing, a copy of another profile of the game
with or without its saves, the data the bare engine has for the game, or the original game's saves), and under
Advanced its location and library folders; with no profile yet the wizard opens at once. The header's Shortcut gives
the command that starts the profile through the launcher (`--launch`, the default) or the engine directly, as its
target, arguments, working folder and whole command line, each with Copy, and writes a `.lnk` (Windows) or `.desktop`
file (Linux) on the desktop or in a picked folder; Duplicate copies the profile with or without its saves; Rename and
Delete are under More. "Add an existing profile" adds a folder holding a `profile.toml` under an id made from the
folder's name and opens its review (spec §12): the paths its file names, each network location marked, and its
enabled mods (a link inside the root is allowed, launcher plan §9.7); Accept, Decline (the root is forgotten, its
folder kept) or Later. Under the list,
Games shows each supported game with the installs its profiles use, its art (downloaded or removed when the player
asks, the host named first) and the publisher's page; Launcher settings writes `launcher.toml` at once (the
launcher's language and theme, shown at once, `while_running`, the art consent, and the language, FPS counter and
page locator of a new profile's `OME.INI`); About shows the version, the engine Play starts, the licences beside the
launcher and the repository. An overlay folder the profile names that is gone fails the install's
check by name, as the engine would refuse it. Play
starts the engine of that run of the launcher (`--engine` names another) as `--launch` does. While an engine it started runs, the
launcher's process stays, the window stays, minimizes or hides by `launcher.toml`'s `while_running`, and closing it
only hides it; the launcher ends once that engine exits. A failed start brings the window forward with the end of the
log.

| Command | Effect | Exit code |
|---|---|---|
| `OME-Launcher` | the window, at the last profile | 0 when it closes |
| `--launch ID\|DIR [-- ARGS]` | runs the checks and starts the profile, with `ARGS` after the profile's own engine arguments; returns when the engine exits. Both streams go to the profile's `diagnostics/engine.log`. A refused start prints why, and a failed start prints the log's last 40 lines and the checks, run again; either then opens the window at the problem. A folder no profile lists is a root the launcher did not create: nothing starts, and the window opens at its review (spec §12) | 0 started and exited 0; 1 a refused or failed start, once the window closes |
| `--check ID\|DIR` | runs the checks and prints one line per check (`passed`, `warning`, `FAILED` or `not run`, the check, its message), with the link report under a link check that did not pass. Writes nothing | 0 Play can start; 1 it cannot |
| `--print-command ID\|DIR [-- ARGS]` | prints the command a launch would use as JSON: `program`, `arguments`, `working_directory`, `environment` | 0 |
| `--home DIR` | uses `DIR` as OME home, instead of a portable `data/` beside the program or the per-user default | |
| `--output FILE` | writes what is printed to standard output to `FILE` as well, in UTF-8 | |
| `--engine PROGRAM`, `--engine-arg ARG` | starts `PROGRAM` with these arguments before the `--`, instead of the engine beside the launcher (spec §8.5). An `--engine-arg` of `--` is refused | |

A malformed command line, an unknown profile id or folder, and a `--data-root` among a launch's arguments exit 2,
with the reason and the usage on standard error. `ID` is a profile's id, `DIR` its root folder.

**Printing on Windows** (spec §8.6, measured in launcher plan §3.5). The program attaches to the console it was
started from, but a shell does not always wait for a windowed program:

- **PowerShell:** `Start-Process -Wait -PassThru OME-Launcher.exe -ArgumentList '--check','ID'`, and read
  `ExitCode` from its result. Plain `.\OME-Launcher.exe --check ID` does not wait, and `$LASTEXITCODE` stays empty.
  A pipe waits, but loses standard error and garbles non-ASCII text.
- **cmd:** inside a batch file, which waits and sets `%ERRORLEVEL%`, or with `start "" /wait`. At cmd's prompt, the
  prompt comes back before the lines.
- **A script:** `--output FILE` is the dependable form. A process that captures the streams gets them in UTF-8.

During development, run it with `dotnet run --project launcher/Ome.Launcher -- --check ID --home DIR`, or with no
command for the window. Pass `--engine <godot console> --engine-arg --path --engine-arg godot` to start the port from
the repository, from the command line or from the window's Play.

**Publishing** (launcher plan §9.1). `dotnet publish launcher/Ome.Launcher -c Release -r linux-x64` (or `win-x64`)
gives the release form with no other flag: one self-contained `OME-Launcher` file, not compressed, not ReadyToRun,
not trimmed, with Avalonia's native libraries beside it (`libSkiaSharp` and `libHarfBuzzSharp`, and on Windows
`av_libglesv2.dll`), so nothing is unpacked outside its folder. The shipped packages' own notice files go in
`OME-Launcher-notices/` and no symbols are published. `-p:OmeDependencyList=FILE` keeps the dependency list the
single file bundles. The program carries the version the engine does (`OmeVersion`, `git describe`), which About
shows. The release builds publish it through `tools/publish_launcher.py` ("Engine (C#)", the Linux x64 export and the
Windows x64 cross-export).

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `launcher/Ome.Launcher.Tests` | Gate 9 (launcher plan §5.12): `OME-Launcher` run as a child process with its own `APPDATA`, on a synthetic Kamidori install in a temporary `--home`, with `engine/Ome.Launcher.StubEngine` as the engine (its `--wait-for PATH` keeps it running until `PATH` exists, `--delete-file PATH` deletes a file before it exits, and `--exit-code N` is its exit code). It covers `--print-command`'s JSON and `--output`, `--check`'s lines and exit codes, the usage errors, and a launch. The outcomes that open the window (no command, a failed start, `--launch DIR` on an unreviewed root) run in the test's process through `LauncherCommandLine.Run`. The window's tests (launcher plan §6): its string tables; its view models, with Play on the stub engine and a recording stand-in for the window; and its pages drawn headless (`Avalonia.Headless`, test-only) on Skia, which fail on any warning Avalonia logs and click nothing. | `dotnet test launcher/OmeLauncher.sln` (the core level's `launcher-tests` gate) | temporary files; the drawn pages to disposable `build/launcher-shots/<task>-<state>-<theme>.png`, to be looked at against the mock |

## Asset resolution / graphics

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `tools/frida/capture_native_transforms.py` | Capture native `0x21f`/`0x223`/`0x234` worker operands, corrected integer base/anchor coordinates, all one-shot/cyclic retained fields, the one-shot 4×4 matrix, and the final post-cyclic 4×4 matrix. Optional handle filter; read-only. | `py -3.11 -u -X utf8 tools/frida/capture_native_transforms.py [secs] [pid|AGE.EXE] [--handle 0xHANDLE]` | running game → `build/games/<game>/native-transform-trace.jsonl` |
| `parse_sys4ini.py` | Parse the selected profile's `SYS4INI.BIN` into a diagnostic JSON asset-index mirror — name ↔ archive ↔ offset ↔ size for base and append catalogs. Each real entry carries universal `raw_index`; the runtime parses SYS4INI itself, while these files remain tooling/test oracles. Also emits the `call-script <id> → name` annotation map, including every mounted `*.AAI` append record under its packed id (`selector << 24 | raw_index`; a later catalog with the same selector wins), so append calls such as `call-script 0x3000232` disassemble as `=$3$EBINIT.BIN`. `read_settings()` returns the base catalog's ordered settings trailer for other tools (the game driver derives the save root from it). | `parse_sys4ini.py [--check] [--game ID] [--game-root PATH] [--extracted-root PATH]` (`--check` validates against extracted files and archive sizes) | selected `SYS4INI.BIN` → selected `build/games/<game>/asset-index.json` + `build/games/<game>/callscript-names.json` |
| `resolve_asset.py` | **Historical scene-group correlation diagnostic, not a runtime resolver.** Builds/queries the strong `file_number ≈ position − group_start` relationship that helped classify SYS4INI ordering. Native RE proves bytecode resources are already universal packed ids, so do not feed this tool's scene-relative result to runtime lookup. | `resolve_asset.py --build` · `resolve_asset.py <SCENE> [resId]` | `build/games/<game>/asset-index.json` → `build/games/<game>/asset-sections.json`; inspects inferred groups |
| `resolve_frida_reads.py` | Rescue noisy Frida archive-read offsets → asset names via the index (per-archive range search; drops 0x20000 paging reads); recovers the per-scene asset load order. | `resolve_frida_reads.py [reads.log] [-o out.json]` | `build/games/<game>/frida-reads.log` + `build/games/<game>/asset-index.json` → `build/games/<game>/frida-asset-loads.json` |
| `convert_agf.py` | Convert AGF stills to BMP via `AGF2BMP2AGF.exe` (searches all `extracted/DATA*`). `--scene` batch-converts a scene's whole SYS4INI manifest. Since VFS-C, output is a diagnostic pixel-parity oracle; the runtime decodes AGF bytes directly. | `convert_agf.py EV052CA.AGF …` · `convert_agf.py --scene SC0000` | `extracted/DATA*/*.AGF` → `build/games/<game>/textures/*.BMP` |

## Game driver

Use this to play native AGE or the Godot port from the shell or, through its MCP server, from an agent: native
gates, save tests, checking what the original does on a page, and comparing it with the port. Both targets run the
same `.steps` files. The design and protocol are
in `docs/superpowers/specs/2026-11-03-game-driving-design.md`. Native driving needs Windows, Frida and Pillow; port
driving needs the Godot .NET console binary and `dotnet`.

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `drive.py` | The command line for both targets. `start` launches the game under the driver and returns once it is up; the session belongs to the game side, so later `do`/`run` calls drive the same running game. Steps run in this process; a step the target cannot run fails before the first step starts. `stop` closes the game the way its own quit does. `native backup`/`restore` copy the live save folder (`restore` refuses a live folder in the install, below). | `drive.py [--game ID] native\|port start [options]` · `… do STEP…` · `… run FILE.steps` · `… status` · `… stop` · `… steps` · `drive.py native backup` · `drive.py native restore DIR` (`do`/`run` take `--json`) | `tools/drive/<game>.toml` → `build/games/<game>/drive/<native\|port>/` (below) |
| `drive_core.py` | The shared half: step parsing and validation, profiles, conditions, the protocol client, the step executor, sandboxes and seeds, Win32 window helpers. | *(imported)* | — |
| `drive_native.py` | The native target: the Frida daemon that serves the protocol for AGE. **`start` spawns AGE suspended** under Frida and loads `tools/drive/native-agent.js` before AGE runs, then leaves a detached daemon holding the session. AGE runs in a kill-on-close job owned by that daemon, so AGE never keeps running without the hooks. | *(through `drive.py native`)* | the `native_scratch.py` copy (default exe) + `vm-map/engine-ctx.toml` + `build/games/<game>/callscript-names.json` + every `*.AAI` beside the exe (append-record names, so frames in an exported mod catalog read `$2$SCENE`, not `#0x2000001`) |
| `drive_port.py` | The port target: builds `godot/OME.csproj`, prepares the sandbox, and launches the port with `--control` (the port serves the protocol itself; see "Port control channel" below). | *(through `drive.py port`)* | `godot/` |
| `drive_mcp.py` | MCP server (stdio) for agents: `drive_start`, `drive_steps`, `drive_screenshot`, `drive_state`, `drive_stop`, each taking `target`. A thin client over `drive.py` (below, "MCP server"). Needs the optional `mcp` package, 2.x. | `drive_mcp.py [--game ID]` (an agent launches it) | as `drive.py` |
| `test_drive_mcp.py` | The MCP tools called in-process through the SDK against the fake protocol server: listing, step results with the final screenshot as an image, failures, `.steps` files, `close`, state, refusals. Fails without `mcp`; run by hand (not in `validate.py`, since no layer requires `mcp`). | `test_drive_mcp.py` | — |
| `test_drive_core.py` | Platform-independent tests: step parsing, refusals and capabilities, locations, conditions and native frame decoding, the settle rule, INI updates, save roots, backups, the restore refusal for a live folder in the install, session identity across pid reuse (on Windows also creation-time-checked termination of a child process), port sandboxes and a start on a player profile root (`--data-root`), the Himegari profile, the SYS4INI settings trailer, the client and executor against a fake server, the `option` and `language` steps and the native driver's 設定 control table (every option's trackbar positions round-trip), and every committed scenario, the port-only ones against the port's capabilities. | `test_drive_core.py` | — |

**Start options.**

| Option | Targets | Effect |
|---|---|---|
| `--sandbox NAME` | both | persistent named save sandbox; the default `fresh` is emptied at every start |
| `--seed live\|native:NAME\|port:NAME\|DIR` | both | fill a new sandbox from the live save folder, another sandbox of either target, or a folder (a `backups/` stamp or a plain copy). The copy leaves the source's top-level `run.lock` and `profile.toml` out (launcher spec §5.5): the first is the source's run lock, and the second would make the sandbox a player profile, which refuses the `--game-root` a sandbox starts with. A sandbox that already exists is left as it is: the seed is skipped with only a `serve.log` note, so use a new name to re-seed. The source is read at start, so do not restart the source target in between, because that empties its `fresh` |
| `--sound` | both | keep the sandbox's volume instead of writing `[sandbox.quiet]` |
| `--set SECTION.KEY=VALUE` | both | write this `SYS4REG.INI` value into the sandbox before launch, after the profile's `[sandbox.settings]` and `[sandbox.quiet]` (repeatable), e.g. `--set message.MessageSpeed=100` for slow reveals. Native refuses it with `--live-saves` or `--attach` |
| `--foreground` | both | leave the window where Windows puts it, and natively let AGE take the foreground as it does when a user starts it. By default the window goes to the bottom of the z-order without being activated, and native AGE never takes the foreground ("What the native hooks do inside AGE") |
| `--timeout S` | both | seconds to wait for the game (native 90, port 180, which includes Godot's boot) |
| `--exe PATH` | native | the AGE.EXE to spawn (default: the `native_scratch.py` copy) |
| `--live-saves` | native | use the real save folder, after an automatic backup |
| `--attach [PID]` | native | hook an AGE you started yourself; its saves stay live and `close` is refused (use `stop`, which detaches) |
| `--inline` | native | run the daemon in this process (debugging) |
| `--godot PATH` | port | the Godot .NET console binary (default `OME_GODOT_CONSOLE`, then PATH) |
| `--no-build` | port | skip `dotnet build godot/OME.csproj` |
| `--optimize` | port | build with the JIT optimizer on (`-p:Optimize=true`, still the Debug configuration), for timing against native ("Port timing" below); refused with `--no-build` |
| `--mod PATH` | port | load this mod package (`.omp` or folder) in addition to the sandbox's `mods/` (repeatable; the port's `--mod`) |
| `--port-arg ARG` | port | an extra port argument after `--`, e.g. `--port-arg=--locator-hud` (repeatable) |
| `--package DIR` | port | run this exported package's engine (`DIR/OME.exe`, or `DIR/OME` on Linux) instead of the Godot project, with the same engine arguments: a release's NativeAOT engine (`platform-portability.md`, "Fewer files in a package"). Nothing is built; `--godot` and `--optimize` are refused. A package outside the repository links mods with its own toolchain, which does not yet carry the OMS modules (backlog, "Toolchain packaging for exported ports"). The MCP tool's `drive_start` takes it as `package` |
| `--data-root DIR` | port | run this player profile root as it is (launcher spec §5.10). The port gets `--data-root DIR` and no `--game-root`, since the root's `profile.toml` names the install. No sandbox is made and the driver writes nothing into the root, so the profile's own settings apply, its display mode and volume included. The engine still writes its `SYS4REG.INI` and saves there as a run does. The Desktop is `drive/port/desktop/`. The root must hold a `profile.toml` whose `game` is the driver's (`--game`, which picks the drive folder and named points). `--sandbox`, `--seed`, `--set` and `--sound` are refused with it. The MCP tool's `drive_start` takes it as `data_root` |

**Port timing.** The port runs Godot's Debug configuration (`godot/.godot/mono/temp/bin/Debug`; Godot's editor
binary loads no other), where the JIT optimizer is disabled. CPU-bound VM work therefore takes two to three times as
long as in a release build: an AGF decode takes 10.6 ms instead of 4.4 ms (`engine-re.md`, "What a texture load
costs"). To compare such work with native, start with `--optimize`, which builds the same configuration with
`-p:Optimize=true`.

- MSBuild's incremental compile does not hash `Optimize`. The root `Directory.Build.targets` therefore adds the define
  `OME_OPTIMIZED_DEBUG` to such builds, so a change of mode recompiles every project. The next start without
  `--optimize`, or an editor build, returns to unoptimized assemblies; that rebuild takes longer than an incremental
  one.
- The start line ends with the mode the port reports in its `hello` (`jit_optimized`): `JIT optimized` or
  `JIT optimizer off (Debug build)`, or `NativeAOT (a release's engine)` for a `--package` start (`native_aot`).
  Check it after `--no-build`, which keeps whatever was built last.

**Output.** Everything lives in `build/games/<game>/drive/<target>/`: `session.json` (port, token, pids; deleted
when the session ends), `serve.log` (native daemon) or `godot.log` and `launch.json` (port), `events.jsonl` (native),
`shots/`, `sandbox/<name>/`, and for native `backups/` and `last-session.json`. Native `session.json` names the daemon
as `pid` and AGE as `game_pid`; the port's names the Godot process. Both record that process's creation time as
`pid_created` (a Windows FILETIME), and the port's `launch.json` records the console launcher's as `launcher_created`.
A session file whose pid has exited, or now names a process created at another time (a server that died hard leaves
its file, and Windows reuses pids), is stale and removed. `stop` terminates a process that does not exit only through
a handle whose creation time still matches, and leaves one with no recorded time alone.

**Steps.** One per line in a `.steps` file, with `#` comments; `drive.py <target> steps` lists them with the
profile's points. Points are named coordinates from `[points]` in the profile, or `X Y`, in native client pixels
(the 800×600 canvas on both targets).

| Step | Effect |
|---|---|
| `move X Y\|POINT` | park the cursor (hover) |
| `click X Y\|POINT [hold=MS]`, `rclick …` | left/right click; right click opens the ADV menu |
| `key [alt+]NAME\|0xVK [hold=MS]` | press a key (`enter`, `space`, `ctrl`, `alt`, arrows, `f1`…, letters); `alt+NAME` presses it with Alt held, as a chord (`alt+m`, `alt+space`, `alt+enter`). Native gets Alt, F10 and every Alt chord as `WM_SYSKEYDOWN`/`WM_SYSKEYUP` with `lParam` bit 29 while Alt is down, so `DefWindowProc` runs the menu bar's keyboard mode; the release goes to whichever window took the focus on the press (the fullscreen strip). Natively an Alt or F10 tap, or Alt with a letter or Space, needs AGE's window to be the active window of its thread, as the window a user types into always is; `DefWindowProc` starts no keyboard menu otherwise. After a minimize it is not, so the agent first activates it inside AGE's thread, which takes no foreground; the step fails only when the window refuses that, as a disabled window under a modal box does. ESC is refused on both targets: it asks to close (use `close-request`). So is `alt+f4`, which natively posts `WM_CLOSE`. F1 saves a screenshot BMP in the sandbox Desktop; a native session on the live folders (`--live-saves`, `--attach`) refuses it, and the `menu` commands `save-screen` and `image-folder` too |
| `wheel up\|down [N]` | wheel notches at the cursor |
| `wait SECONDS` | sleep (in the client) |
| `settle [timeout=S] [quiet=S] [threshold=F]` | wait until the game is idle for `quiet` seconds (0.7). Native: the frame stays within `threshold` (default 0.4 % of pixels) of one reference; typing adds up and resets the wait, the ADV wait indicator stays below the threshold, and animated menus such as the title never settle (use `wait` or a higher `threshold`). Port: an `input` or `menu` wait that is not busy |
| `advance [N]` | N × (click the profile's `[advance] point`, `settle`), then report the position |
| `shot [NAME]` | the game canvas → `shots/NAME.png`; the result carries the VM position |
| `state` | the frame stack, e.g. `SYSTEM4@0x477 > SC0000@0x8a9` |
| `expect [at=S@OFF] [top=S@OFF] [script=S] [waiting=KIND]` | fail unless the VM matches every condition given: `at` any frame, `top` the innermost, `script` any frame's script, `waiting` one of `input`, `menu`, `movie`, `transition`, `sleep` (port only) |
| `wait-state CONDITIONS [timeout=S] [click=POINT every=S]` | poll until `expect` would pass, clicking `POINT` every `S` seconds (default 2) meanwhile (boot: `wait-state script=TITLE click=adv.next every=3`) |
| `point NAME X Y` | define a point for the session |
| `speed FACTOR` | port: run the frame clock 0.05–8 times as fast |
| `lua [mod=ID] [timeout=S] CODE` | port: run the Lua chunk `CODE` (the rest of the line, `#` included) on the engine thread and report what it returns as text, values separated by tabs (Lua scripting spec §13). It runs at the next instruction boundary or inside the wait the game sits in, so it also runs at a text wait; there `maid.vm.frame`, `maid.vm.frames()` and the waiting frame's locals read as the `state` step shows them. Without `mod=` it runs in the console's own environment, which has `maid.vm` and lasts for the session (a return to title or a load ends it), so a chunk can use what an earlier one defined. With `mod=ID` it runs in that mod's environment, with its globals. With no Lua mod enabled the step creates the runtime and a session for the console alone on its first use. A chunk cannot call a script. An error fails the step with the error and its traceback. `timeout` (default 30 s) bounds the wait for the chunk to run. The result reads `result` and `elapsed_ms`. Native AGE has no Lua |
| `rand-seed SEED\|off` | draw op `0x60` from SEED's streams (0 to `0xffffffff`, decimal or hex), one per call site and the same on both targets, until the next `rand-seed`; `off` returns to the game's own draws. Every stream restarts at each `rand-seed`. In the port each Lua mod's `math.random` draws from a stream of its own too, from the seed and the mod's id (Lua scripting spec §5.6; `docs/modding/lua.md`, "Random numbers"), which a load does not restart. The result reports what the period that just ended drew: `previous_seed`, `draws` and `sites`, the count per `SCRIPT@0xOFFSET`, and `mods`, each Lua mod's draws, when one drew (`draws` counts op `0x60` only). Seed both targets at the same point, for example after loading one save, then compare the counts as well as the screens. The streams are defined in `docs/engine-re.md`, "Op 0x60 randomness". Native needs the profile's `[native.rand]` |
| `dialogs` / `answer BUTTON` | list the open dialogs / press a button, radio buttons included (native: AGE's message boxes, AGERC's dialogs and the 画像の保存場所 folder picker; port: the 終了 and script-error dialogs, the folder picker and the options panel). AGERC's dialogs list every static text and button of the dialog and its pages, hidden pages included, the topmost dialog first; `answer` presses the first match from the topmost dialog down, and a check box toggles. The port's options panel lists itself in Japanese whatever its language: title 設定, as `text` the section shown, then the section's check boxes, 情報's ｸﾘｯﾌﾟﾎﾞｰﾄﾞにｺﾋﾟｰ, the six sections (seven where the build has the ｵｰﾄﾒｯｾｰｼﾞ page), 決定 and ｷｬﾝｾﾙ. `answer` takes a Japanese or shown label, mnemonics ignored (`効果音(&S)` matches 効果音): a check box toggles (a read-only section's refuses), a section shows, and 決定 (or `OK`) and ｷｬﾝｾﾙ (or `Cancel`) end the panel. Steps split like a shell, so quote a label with spaces (`answer 'Return to title(&T)'`). Natively the press is a posted `BM_CLICK`, which AGE's thread takes only when it next reads its queue, as it would a user's click, so `answer` returns once AGE has taken it (10 s at most). 設定's first open in a process holds AGE's thread for about 2 s with its VRAM query (`engine-re.md`, "Native menu bar", 設定). The folder picker is the shell's, so its title and buttons follow the Windows UI language. `SHARED_CAPABILITIES` in `drive_core.py` lists what committed scenarios may use |
| `close-request` | ask to close as the close button and ESC do (an unforced `WM_CLOSE`): AGE opens its 終了 dialog. While the window is minimized, AGE quits at once without the dialog (run-state `0x1000000`), so the session ends |
| `window minimize\|restore` | minimize or restore the game window, as its title-bar and taskbar buttons do; the result reads `minimized`. Native posts `WM_SYSCOMMAND` `SC_MINIMIZE`/`SC_RESTORE` and waits for `IsIconic` to match; the port sets Godot's window mode, restoring the screen mode the game was in, and its result also reads `windowed`. After a restore, both put the window back at the bottom of the z-order unless the session started with `--foreground`. Natively the restore does not activate the window ("What the native hooks do inside AGE") |
| `menu NAME\|ID` | choose a menu-bar command as a user does. The menu refreshes first, and a grayed item fails the step. Names: `exit`, `save`, `load`, `version`, `save-screen`, `image-folder`, `fullscreen`, `windowed`, `skip`, `hide`, `auto`, `read-skip`, `sound-settings`, `message-settings`, `system-info`, `other-settings`, and `auto-settings` where the menu has it (AGE 4.38D); or a numeric `WM_COMMAND` id. The result prints the item's `label` and whether it was `checked` before. Native sends `WM_INITMENU` (AGE has AGERC refresh the items), reads the item, then posts `WM_COMMAND`; the port runs the same refresh and command on its drawn bar. In fullscreen the menu is the top-edge strip's, so the step fails unless the strip shows (hold the pointer on row 0 past op `0x149`'s delay first); native then refreshes the strip with `WM_ENTERMENULOOP` and posts `WM_COMMAND` to it |
| `option NAME VALUE` | set one 設定 option and commit it as OK does (engine shell spec §8). The option's section command (`message-settings`, `sound-settings`, `other-settings`, or `auto-settings` for the ｵｰﾄﾒｯｾｰｼﾞ page) opens 設定 at that tab as `menu` does, so a grayed command fails the step (while op `0x142`'s gate is 0). Names and domains (`OPTIONS` in `drive_core.py`, the port's `ShellOptions`): `message-speed` 0–100 (ms per glyph), `window-opacity` 0–16, `auto-wait-voice` and `auto-wait-text` 0–10000 in steps of 100 (ms), `smooth-text`, `wheel-advance`, `master-volume`, `bgm-volume`, `se-volume` and `voice-volume` 0–10000 in steps of 100 (basis points), `bgm`, `se`, `voice`, `lower-bgm-on-voice`, `lower-bgm-level` 0–100 (%), `effect-skip`. Where 設定 has the ｵｰﾄﾒｯｾｰｼﾞ page (AGE 4.38D; `engine-re.md`, "4.38D's menu bar and 設定 dialog") there are three more, `auto-pitch-voice` and `auto-pitch-text` 0–1000 in steps of 10 (ms per character) and `auto-wait-voice-end` (音声終了まで待つ: on is `AutoMessageOption` 0), and the two `auto-wait-` options are on that page; `auto-pitch-voice` has no usable control while `auto-wait-voice-end` is on, and the three fail on a game without the page. VALUE is the registry's value; a switch also takes `on`/`off`, and an out-of-range or off-step value fails before anything runs. Native drives AGERC's dialog 9: a trackbar takes `TBM_SETPOS` and its page the `WM_HSCROLL`/`WM_VSCROLL` notification (`TB_THUMBPOSITION`, then `TB_ENDTRACK`), so a volume applies live as a drag does; a check box takes a posted `BM_CLICK` when it differs. The step reads the control back, presses OK and returns once 設定 has closed, so `agerc_settings_apply` and `CALLBACK_SETTING.BIN` run after every `option`. The port opens the options panel the same way, sets the option in its draft and presses 決定. The result reads `option`, `value` and `section` |
| `options` | list every option 設定 shows, with `name`, `section`, `value`, `min`, `max`, `step` and `applies` (`live` or `ok`). Both targets open 設定 at ﾒｯｾｰｼﾞ from its command and cancel it; native reads each page's controls, hidden pages included, and the port its draft. `smooth-text` is listed only where `ENABLEANTIFONT` binds it, the ｵｰﾄﾒｯｾｰｼﾞ page's three only where 設定 has the page, and a control a check disables (`auto-pitch-voice` while `auto-wait-voice-end` is on) not at all |
| `language TAG` | port: choose the port's language as a player does and commit it. ﾊﾞｰｼﾞｮﾝ情報 opens the options panel (no script gate grays it), the OME section's list takes `TAG` (normalized as `--language` reads it: `PT_BR` is `pt-br`), and 決定 commits it. The menus, the game text and the language assets switch at once, and the sandbox's `OME.INI` keeps the language; no native option changes, so `CALLBACK_SETTING.BIN` does not run. The list offers `ja`, `en` and every language the mounted packages define, so a tag it lacks fails the step, which lists the offered ones and cancels the panel. The result reads `language`, `previous` and `languages`. Native AGE has no language to choose |
| `reference-diff on\|NAME [tolerance=N] [max-over=M]` | port: compare the GPU renderer with the reference renderer on the same frames (engine layering spec §6.3). `on` makes the reference draw every frame as well, into its own buffer, so a frame that draws over the previous one has the same history on both. `NAME` compares the screen, as `shot` captures it, with the reference's drawing of the same frame description, its colour without its alpha as the screen shows it, and writes `shots/NAME-gpu.png`, `-reference.png` and `-diff.png` (each channel's difference times eight, red where one is over the tolerance). Run it settled, so both show the same frame. The result reads `renderer` (`gpu`, or `reference` for a frame the GPU declined), `items` (the frame's draws), `transformed` (its texture draws that are scaled, rotated or at a fractional position, where filtering may differ), `polygons`, `polygon_textured`, `polygon_two_textures` and `polygon_blends` (its polygon draws, the textured ones, the ones with a second texture, and each blend's count, such as `alpha:33,replace:1`: which polygon paths it covered), `over` (pixels with a channel more than `tolerance`, default 3, apart), `max`, `mean` and the `bbox` of the over pixels. With `max-over`, the step fails when more than M pixels are over, and says how many. Native AGE has no reference renderer |
| `close [timeout=S]` | quit the game the way its own quit does (native `WM_CLOSE` wParam=1, which saves the shared profile), then the session ends |

A failing step stops the request, and `do`/`run` exit 1. Results also carry what the game did meanwhile: a script
cursor warp reads `cursor_moved_to` (AGE glides the cursor onto a confirmation's default button), and native results
attach the daemon's new events (dialogs answered, the window appearing).

`tools/drive/scenarios/himegari-save-load.steps` is the worked example: boot, new game, save at page 3, read on,
load, and expect page 3 again. It runs unchanged on both targets, about 35 s each on a fresh sandbox, at the same
offsets.

`arterial-save-load.steps` is the same round trip for 創刻のアテリアル, through its character menu.
`arterial-auto-options.steps` sets the five controls of its 設定's ｵｰﾄﾒｯｾｰｼﾞ page and one on the ﾒｯｾｰｼﾞ page with
`option` on SC0000's first page. Both targets list the same `options` before and after and leave identical
`SYS4REG.INI` files (2026-11-19).

`himegari-save-page3.steps` and `himegari-load-page3.steps` cross a save between the targets. Save on one target, then
start the other with `--seed port:fresh` (or `native:fresh`) and load. Their headers give the commands. Both directions
resume at `SC0000@0x8a9` and reach `0x913`.

`himegari-script-error.steps` and `himegari-script-error-abort.steps` cover the interpreter error box
(エラーが発生しました) on both targets, raised by the `oms/examples/mods/example.depth-error` mod: 再試行, 無視 (the
pc steps past the wait's successor), and 中止 (the game closes). Their headers give the setup. On the port,
`answer` takes the Japanese label (`再試行`) or the English one (`Retry`).

`himegari-exit-dialog.steps` covers the close policy on both targets: Cancel on the title, タイトルに戻る from
SC0000 page 1 back to TITLE without the logos, then TITLE's fifth action (op `0x1`) quits without asking.

`himegari-minimized-close.steps` covers a close while minimized on both targets. On the title it minimizes and
restores, and `close-request` still opens the 終了 dialog (answered ｷｬﾝｾﾙ). It then minimizes again, and
`close-request` quits at once with exit code 0, no dialog and a `SAVE.DAT` write. The last step ends the session.

`himegari-read-skip.steps` saves on SC0000 page 1, reads six pages, reloads and turns on Read-message Skip (x=750).
Both targets fast-forward over the read pages and stop at `SC0000@0xa18`, the first page not yet advanced past.

`himegari-hover-after-click.steps` clicks the read-skip control on SC0000 page 1 and shoots the control strip as the
pointer rests, moves inside the control, moves to its neighbour and comes back. Compare the shots: a clicked control
stays hovered until the pointer leaves it, so its tooltip only returns after the pointer has been elsewhere.

`himegari-wheel-history.steps` turns the wheel at the SC0000 page-7 wait (`SC0000@0xa18`). Wheel-down leaves the page
alone, and wheel-up opens HISTORY through `SC0000@0x35a`. Inside HISTORY a wheel-up scrolls one row older. Its close
button returns to the same wait, where wheel-up reopens it. The History shots (`wheel-3`, `wheel-4`, `wheel-6`) also
compare History's rows between targets. Those rows are drawn in HISTORY's own 22 px style and keep the ADV window's
row breaks.

`himegari-hide-window.steps` hides the text window on SC0000 page 3 in both ways: with the control (x=772) and with
Space at the text wait. Retail never runs HIDEWIN.BIN, so both targets wait in the engine with the box hidden.
- After the control, the frames stay at `SC0000@0x30d`. The wheel and an unmapped key (A) keep the box hidden, and a
  left click restores it.
- After Space, the frames stay on the wait. The wheel, a right click or Space restores the box.
- Restoring never advances the page or opens the menu, and Enter still advances.
- On page 4, Space hides and restores the box under a pointer resting on the hide control. The control loses its
  tooltip, and a click on the same spot advances to `0x913`: op `0x7c` cleared the hotspot hit.
- The `hide-*` shots compare the hidden and restored frames, and `hide-8`/`hide-9` the tooltip.

`himegari-effect-skip-on-click.steps` clicks during the hide window's two text-box fades on SC0000 pages 3–4, with
`system:EffectSkipOnClick` 0 (a new profile's default) and then 1, set with `option effect-skip on`. Its clicks hold
20 ms so that each lands inside its 500 ms fade.
- With 0, the fade runs out and the click stays latched. A click during the control's fade-out restores the window
  once the fade ends (`SC0000@0x8a9`). A click during the fade-in after a Space hide advances the page (`0x8de`).
- With 1, the click skips the fade. After the control the window stays hidden (`SC0000@0x30d`); after Space the page
  stays.
- The `esk-*` shots match between targets apart from glyph edges and the wait indicator's phase (`engine-re.md`,
  "Effect skipping and the input latch").

`himegari-effect-waits.steps` times SC0000's page 13→14 transition: four op-`0x21c` effect waits (150, 890, 150 and
300 ms on native's clock), then page 14's text. Half a second after the click the frames must still be in the waits
(`SC0000@0x123df`). Page 14's text wait (`SC0000@0xe0f`) must follow within 1.6 s. From the click step, native
reaches it in 1.81 s and the port in 1.76–1.78 s (four runs; the driver rounds each step to 10 ms; `engine-re.md`,
"Service waits end on the engine clock").

`himegari-latch-sleep.steps` clicks while nothing polls, on a fresh sandbox started with
`--set message.MessageSpeed=100`. It advances SC0000 page 17, right-clicks during the 1000 ms sleep at
`SC0000@0x1345`, and left-clicks during the 750 ms sleep at `0x16cb`. 6.2 s later page 18 must already wait at
`0x1a5b`: the held left click finished its two-run reveal at once, and the right click opened no MENU. Without the
click the reveal still runs at that point (`engine-re.md`, "Effect skipping and the input latch", clicks during a
sleep). The `latch-sleep-page18` shots differ only in the scene's animation phase.

`himegari-furigana.steps` advances to SC0000 page 18 (`SC0000@0x1a5b`), whose op `0x196` draws 篝火 with the ruby
かがりび, and opens History with a wheel-up. In the `furigana-page18` and `furigana-history` shots the text matches
native pixel for pixel, the ruby included; the scene's wisp and the wait indicator differ by animation phase
(`engine-re.md`, "Furigana").

`himegari-reveal-clock.steps` times SC0000 page 18's three runs on a fresh sandbox started with
`--set message.MessageSpeed=300`: op `0x6e` (8 ticks, 2.4 s), op `0x196` (4 ticks, 1.2 s) and op `0x6e` (23 ticks,
6.9 s). Each `expect` checks the run 150 ms before it ends, so a reveal that shows its first tick at once fails
(`engine-re.md`, "The reveal clock").

`himegari-menu-bar.steps` chooses the menu bar's ﾒｯｾｰｼﾞ items and ｹﾞｰﾑ > 終了 on SC0000. It saves on page 1, reads
six pages and loads. Then:
- 既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ fast-forwards to `SC0000@0xa18` with the control strip's read-skip cell ON.
- ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを消す hides the box at the wait, and the same item, now ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを表示する, restores it.
- ｵｰﾄﾒｯｾｰｼﾞ on and straight off again moves exactly one page, to `0xa4d`.
- 終了 opens the 終了 dialog, answered ｷｬﾝｾﾙ.

Both targets match at every checkpoint. The `menu-*` shots also compare the control strip; they differ only in the
wait indicator's phase.

`himegari-menu-keyboard.steps` drives the menu bar's keyboard mode on SC0000 (`engine-re.md`, "Native menu bar",
Keyboard mode):
- An Alt tap selects ｹﾞｰﾑ, and a click on the game ends the loop and is eaten; the next click advances.
- F10, `m` and Down ×3 reach ｵｰﾄﾒｯｾｰｼﾞ. Enter turns it on, and Alt+M then A turns it off, so the pair moves exactly
  one page.
- Alt, Left and Down open the window menu, Up wraps to Close, and Enter opens the 終了 dialog, answered ｷｬﾝｾﾙ.

Both targets match at every checkpoint, and the native run leaves the user's foreground alone.

`himegari-fullscreen.steps` switches to fullscreen on the title screen with ﾌﾙｽｸﾘｰﾝ, holds the pointer on the top edge
until the system-menu strip shows, and chooses ｳｲﾝﾄﾞｳ from the strip. Both targets pass, and both `SYS4REG.INI` files
end with `ScreenMode=1` after `stop`. The native run covers the screen for a few seconds. Native's fullscreen `shot`
is its desktop-sized client, not a canvas image (`engine-re.md`, "Screen mode").

`himegari-screenshot.steps` saves screenshots on SC0000 pages 1 and 2, with 表示画面を保存 and F1, and writes
`CG000000`–`CG000002.BMP` into the sandbox Desktop. It cancels the 画像の保存場所 folder picker, then accepts it. Himegari
preselects the Desktop, so `SaveBMPPath` becomes the sandbox Desktop and `CG000003.BMP` goes there. Compare each BMP
with the `screenshot-*` shot taken before it. On both targets they differ only in the wait indicator, and the BMP
headers are identical. Native writes `SYS4REG.INI` when it exits, so `SaveBMPPath` shows only after `stop`; the port
writes it at once (`engine-re.md`, "Native menu bar", Screenshots).

The port's 設定, ﾊﾞｰｼﾞｮﾝ情報 and 動作環境 are its options panel (engine shell M3), so the scenarios below set options
with `option` steps, which drive AGERC's controls natively and the panel on the port, and answer only the buttons
both share: 設定's own check boxes, OK and ｷｬﾝｾﾙ.

`himegari-version-dialog.steps` opens ﾍﾙﾌﾟ > ﾊﾞｰｼﾞｮﾝ情報 on SC0000 page 1. It lists the dialog, takes a shot, answers OK,
and checks that no dialog is left and that the game goes on to page 2 (`SC0000@0x874`). Natively it is AGERC's
dialog 2; on the port the options panel at 情報, where `OK` presses 決定, which applies nothing without an edit, as
AGERC's OK does (`engine-re.md`, "Native menu bar", 情報 names the port). Native's `shot` is AGE's own window, so it
leaves the dialog out; the port's `version-dialog` shot includes the panel.

`himegari-settings-dialog.steps` drives 設定 on SC0000 page 1. `options` lists the sandbox's values. A wheel-down does
not advance until `option wheel-advance on`, then does. It turns 効果音 off live from the ｻｳﾝﾄﾞ tab and cancels, sets
eight more options, lists them again, opens and cancels 動作環境, and advances to page 3 (`SC0000@0x8a9`). Both
targets reach the same positions and list the same values. After `close` their `SYS4REG.INI` files are identical
(2026-11-09): the options set, `SE=1`, and `Volume0..3` written by every OK.

`himegari-antifont-off.steps` clears 文字の縁をなめらかにする (`message:UseAntiFont`) with `option smooth-text off` on SC0000
page 1. It takes four shots:
- page 1 before OK;
- page 1 after OK, which looks the same (AGE does not redraw text already drawn);
- page 2, drawn from `GGO_BITMAP` glyphs, one pixel right and down;
- History, opened with a wheel-up.

On both targets, each shot matches the other target's pixel for pixel apart from the wait indicator's phase. After
`close`, both `SYS4REG.INI` files are identical, with `UseAntiFont=0` (`engine-re.md`, "AntiFont off").

`arterial-antifont-off.steps` and `kamidori-antifont-off.steps` (`--game arterial`, `--game kamidori`) do the same
on 創刻のアテリアル's first page and on Kamidori's first page with furigana (`SC0000@0xfa2`), then take the next two
pages and History. Kamidori's also has CONFIG's 初期設定 redraw the first page through op `0x82`. There the aliased
text stays in its place, and each shot matches the other target's apart from the wait indicator's phase (2026-11-20;
`engine-re.md`, "AntiFont off in 4.33G and 4.38D").

Kamidori's scenarios run with `--game kamidori`, on a fresh sandbox, on the clean install (`games/kamidori/`, the
default for both targets; the fan translation blanks every ruby). Each file's header gives its start commands.
- `kamidori-furigana.steps` reaches the prologue page `SC0000@0xfa2` (はんらん over 叛乱) and opens its History. Shots
  `kamidori-furigana-page` and `-history`; History matches native in every text pixel (`engine-re.md`, "Furigana in
  SYS4433").
- `kamidori-config-redraw.steps` reaches the same page, then makes two CONFIG changes from the right-click menu's
  環境設定: 初期設定 on 基本設定, and ADV設定's 効果 縁なし. Each change runs op `0x82`, which redraws the page behind
  CONFIG. Shots `kamidori-config-redraw-page`, `-defaults-config`, `-defaults`, `-no-outline-config` and `-no-outline`.
  Apart from the animated wait indicator they match native within 1 per channel, CONFIG2's own surface text included
  (`engine-re.md`, "Op 0x82 redraw", "Aspect-mode surface text").
- `kamidori-shell.steps` cancels 終了 over the title, sets `message-speed`, `lower-bgm-level` and `effect-skip` with
  `option` on the prologue page, then returns to the title through 終了's タイトルに戻る. The clean install's
  `AGERC.DLL` is packed, so the port's 終了 and panel come from the shell. Both targets list the same `options`,
  reach `SC0000@0xfa2` and leave identical `SYS4REG.INI` files (2026-11-09). It is for the clean install: the
  translated scripts have other offsets, and native's translated 終了 is English.
- `kamidori-history-after-load.steps` saves on the first name-plate page (`SC0000@0x1488`) to slot No.003, returns to
  the title through 終了 and loads that slot. It opens History before the save and after the load: shots
  `history-page`, `-before`, `-loaded` and `-after`. `history-after` equals `history-before` pixel for pixel, with
  Wil's line once at the bottom, and both match the other target's (2026-11-11; `engine-re.md`, "Retained History
  record model and lifetime").

**Port-only scenarios** live in `tools/drive/scenarios/port/`, where `test_drive_core.py` checks them against the
port's capabilities instead of `SHARED_CAPABILITIES`. `kamidori-language-switch.steps` switches the language to `en`
on the title and back to `ja` on GAMESTART with `language`, and the VM stays where it was (2026-11-11). With no
package defining a language, English changes only the shell's text; add `--mod` with a language package to switch the
game text and its images too. `himegari-reference-diff.steps` and `kamidori-reference-diff.steps` compare the GPU
renderer with the reference on the title, GAMESTART, ADV pages, SC0000's effect band, the history and Kamidori's
first field and its map (`reference-diff`; engine layering spec §6.3, "As built"). `arterial-reference-diff.steps`
compares them on the polygon items of 創刻のアテリアル's logo, and must start with the port, since the logo runs on
the clock. `arterial-debug-reference-diff.steps` compares them on every polygon path, through the debug menu and
`example.arterial-polygon-blends`, and bounds each comparison with `max-over`. The data patches scenarios
(`himegari-stage-enemies*.steps`, `kamidori-stage-enemies*.steps`, `himegari-unit-stats*.steps` and
`kamidori-unit-stats.steps`) drive the data-file examples of `oms/examples/mods/`, whose README says what each shows;
on Kamidori they include the stage list's preview (`-list`) and two saves on 蒼月の南湖畔 (`-lake`). Each header gives
its start command, with its `--mod`, its `dp-` sandbox and, on Kamidori, the sandbox of saves it seeds from, which
the repository does not ship (`k1clean`, made on the clean install while native AGE and the port were compared).
Most of their `lua` steps read cells by number in the console (`maid.vm.g`): a `lua mod=<game>-base`
step needs the base mod in the stack, which a run without the example lacks.

**Geometry pairs** live in `tools/drive/scenarios/geometry/`: localization gate 12 (spec §18.1), whose results are in
`docs/localization-import-test.md`. `<game>-translated-native.steps` runs native AGE on an install translated in
place and `<game>-translated-port.steps` the port on the clean install with the package imported from it.
`<game>-translated-meiryo-{native,port}.steps` do the same after choosing メイリオ in each game's config screen, so
aspect mode calibrates a proportional face. Each waits for the same pages at its own install's offsets and shoots
them under the same names; `test_drive_core.py` checks that both halves parse with the shared capabilities and shoot
the same names in the same order. Run the native half first on the fresh sandbox and stop native (AGE writes
`SAVE.DAT`, the shared profile that keeps the font, only when it quits). Then start the port on a new sandbox seeded
from it (`--sandbox NAME --seed native:fresh`), so the port starts with the settings native played with, the text
font among them; the files' headers give the commands. `text_geometry_compare.py` compares the two shot folders.

**Profiles.** `tools/drive/<game>.toml` holds the facts for both targets: `[advance]`, `[points]`, `[sandbox]`
(`settings_file`, `[sandbox.settings]`, `[sandbox.quiet]`), and native-only `[native.engine]`,
`[native.keep_polling]`, `[native.rand]`, `[native.build."NAME"]` and `[[native.dialog]]`. Unknown sections are
refused, and so are unknown keys in `[native.engine]` and `[native.keep_polling]`. A profile without
`[native.engine]` still drives native AGE; it just has no `state`.
- `[native.engine]` needs `ctx_global`, `frame_stride` and `frame_count`. The frame fields `cur_ctx_index`,
  `frame_codebase`, `frame_pc` and `frame_script_resource_id` come from `vm-map/engine-ctx.toml`, which describes
  Himegari's engine. A profile for another engine revision names them in the table instead.
- `[native.keep_polling]` needs `setter` and `activate_return`. Its optional `gate`, the polling gate's ctx offset,
  likewise replaces `engine-ctx.toml`'s `input_polling_enabled`.
- `tools/drive/kamidori.toml` gives all of them for SYS4433 (`engine-re.md`, "Driving native AGE"), so the driver
  never reads `engine-ctx.toml` for Kamidori.
- `[native.build."NAME"]` names one AGE.EXE build by the SHA-256 of the file (`sha256`) and gives the keys of
  `[native.engine]`, `[native.keep_polling]` or `[native.rand]` whose values differ in that build, as inline tables
  (`rand = { op60_rand_return = 0x42772f }`); the other keys keep the tables' values. The driver hashes the AGE.EXE it
  spawns, or the image of the process it attaches to, and uses that build's values; the `spawned` or `attached`
  event in `serve.log` names the build. A profile that declares builds refuses an AGE.EXE that is none of them. Kamidori declares 4.33G (the
  clean install, as written) and 4.33F (the translated install; `engine-re.md`, "Kamidori engine builds").

**VM position.** Both targets report frames as `SCRIPT@offset`, with `offset` the dword offset of the disassembly and
the page locator. At a text wait the pc is already **past** the wait instruction, so a page reads as the port page
map's *next* `page_start_offset`. Example: SC0000 page 3 waits at `0x8a6` and reads `0x8a9`. Caller frames stay
**on** their `call-script` instruction (`SYSTEM4@0x477`, not the following `0x47a`), also after a load. Native AGE
reads the frame stack through `g_pEngineCtx@0x5c6020` and the `engine-ctx.toml` frame fields; `waiting` is not
available there. An idle loop reads wherever each target is when asked: Kamidori's title loop reads `TITLE@0xf8`, its
`sleep 0x1`, natively and `TITLE@0x9ab`, after `present-frame`, on the port, so scenarios check such loops with
`script=`.

**Saves.** Both targets keep the native profile layout (`SYS4REG.INI`, `SAVE\`) in `sandbox/<name>/`, so one
target's sandbox can seed the other's.
- Before launch the driver writes the profile's `[sandbox.settings]` into the sandbox `SYS4REG.INI`. For Himegari that
  is windowed `display.ScreenMode=1`: a fresh profile starts fullscreen, which takes over the display and captures
  black. It also sets `display.VirtualFullScreen=1`, so a native switch to fullscreen covers the desktop with a
  window instead of changing the display mode.
- It also writes `[sandbox.quiet]` (master volume 0, so the audio paths still run) unless you pass `--sound`.
- Each `--set SECTION.KEY=VALUE` is written last, so it overrides both.
- Port: the sandbox is the port's `--data-root`.
- Native: `SHGetFolderPathA` points LocalAppData, AppData, Desktop, Documents and ProgramData into the sandbox, which
  covers the save root, `SYS4REG.INI`, screenshots (F1, 表示画面を保存) and the debug report. Port: the sandbox's
  `Desktop` is the port's `--desktop-dir`. The profile root sits at
  `sandbox/<name>/LocalAppData/<the live path below LocalAppData>`. At the end the daemon checks that the live save
  folder is unchanged and logs `live-saves-unchanged` or what changed. If shell32 does not export `SHGetFolderPathA`,
  the agent reports a `hook-error` as the module loads, `start` prints it with a warning that the sandbox is not in
  effect, and `hooked` in the `hello` leaves the module out (it lists only hooks that are in place).
- `--live-saves` and `--attach` use the real folder after an automatic backup to `backups/<stamp>/`. That backup is
  skipped when it would equal the newest one. Even an unsaved session changes the real folder: quitting rewrites
  `SYS4REG.INI`, `SAVE.DAT` and `RT.DAT`.
- `native restore DIR` replaces the live folder with the copy (after backing the current one up). It refuses a live
  folder that is the install, lies inside it or contains it: with `USEAPPDATAFOLDER=0` the save root resolves against
  the game directory, and replacing it would delete the pristine install's own files.

**What the native hooks do inside AGE.**
- `GetAsyncKeyState` reports only the keys and buttons the driver holds.
- `GetCursorPos` reports the driver's client point.
- `SetCursorPos` moves only that point. For a new point inside the client area it also posts the `WM_MOUSEMOVE`
  Windows would send, which is what re-hit-tests AGE's hotspots after the ADV callbacks' cursor nudge.
- `joyGetPos[Ex]`/`joyGetDevCapsA` report no joystick.
- Windowed AGE turns input polling off when its window loses activation, and a driven AGE is never the foreground
  window. Once the
  window exists the agent ignores that call (`[native.keep_polling]`) and reopens the gate; `hello` reports the calls
  it ignored as `activations_ignored`. Without this, polled menus such as the title ignore clicks. See
  `docs/engine-re.md`, "Driving native AGE".
- **The foreground**, in a spawned session without `--foreground`. AGE never asks for the foreground, but a process
  started by one that may set it inherits that permission, so AGE took it at start or with its first dialog.
  - `CreateWindowExA/W`: AGE's first visible top-level window is created under `LockSetForegroundWindow(LSFW_LOCK)`.
    Its activation stays inside AGE's thread and spends the permission; `serve.log` reports
    `foreground {"permission": "spent"}`. AGE's window then starts active in its thread but not in the foreground,
    so Windows' keyboard menu works.
  - `DefWindowProcA`: `SC_MINIMIZE` hands the thread's activation to a hidden helper window on AGE's thread, as a
    user's minimize of the foreground window leaves none. Windows makes a restored window that is still active in
    its thread the foreground one even without the permission. `SC_RESTORE` restores with `SW_SHOWNOACTIVATE`.
  - The Windows rules behind this are in `docs/engine-re.md`, "Driving native AGE", The foreground.
- `DispatchMessageA/W` and `IsDialogMessageA/W` count the `BM_CLICK`s AGE's thread takes, for `answer` (a modal
  dialog's loop dispatches through `IsDialogMessage`, which bypasses the exported `DispatchMessage`).
- `MessageBoxA` answers the boxes the profile's `[[native.dialog]]` policy names without showing them. Himegari's
  registration box on copied installs gets **Yes**. Other boxes are shown and reported (`dialogs`), and `answer
  BUTTON` presses one.
  - The replacement is C (a CModule). A JS frame around a shown box's modal loop crashed AGE.
  - Its buttons carry the Windows UI language's labels, for example `&Retry` on English Windows.
- Mouse and key messages are not posted while AGE's window is disabled, for example under a modal box, because
  hardware input would not reach it either.
- Op `0x60`, after the first `rand-seed`: a Frida probe on the instruction after the handler's `rand()` call
  (`[native.rand] op60_rand_return`) replaces the draw with the seeded stream's value. The agent checks the bytes
  there first, since AGE.EXE is unpacked only at run time.

So your own mouse, keyboard and gamepad cannot disturb a run, and AGE cannot move your cursor. Clicks set the polled
button *and* post the button messages, because the movie skip and `CLICKONUP` use the message latch. Keys likewise
post `WM_KEYDOWN`/`WM_KEYUP`.

### MCP server

`tools/drive_mcp.py` exposes the driver to agents as five tools over stdio. Each tool takes `target` (`native` or
`port`) and does what the CLI does:

| Tool | CLI equivalent | Returns |
|---|---|---|
| `drive_start(target, sandbox, seed, sound, foreground, build, mods, settings, optimize, data_root, package)` | `drive.py <target> start` (`build=false` is `--no-build`, `mods` is `--mod` per path, `optimize=true` is `--optimize`, `data_root` is `--data-root`, `package` is `--package`, all port only; `settings` is `--set` per `SECTION.KEY=VALUE`) | the CLI's output |
| `drive_steps(target, steps \| file, screenshot=true)` | `do STEP…` or `run FILE` | a summary line (`ok: …` or `FAILED at step N …`), one line per step result, and a screenshot after the last step unless `screenshot=false` or a step closed the game |
| `drive_screenshot(target, name)` | `do "shot NAME"` | the PNG as an image, with its path and the VM position |
| `drive_state(target)` | `status` | the session, hello, VM stack and new events, as JSON |
| `drive_stop(target, timeout)` | `stop` | the CLI's output |

The server's instructions carry the step syntax, the conditions and the profile's named points (the output of
`drive.py <target> steps`), so an agent needs no separate lookup. A failing step is reported in the result, with
its screenshot, rather than as a tool error. Tool errors are reserved for calls that cannot run: no session, an
unknown step, a step the target lacks. `--live-saves` and `--attach` are deliberately not offered; use the CLI for
those. Sessions belong to the game side, so the CLI and the server can drive the same running game.

**Setup.** Install the SDK once (`py -3.11 -m pip install --user mcp`; the server needs 2.x), then register the
server. A newly added server loads at the agent's next session.

```text
claude mcp add drive --scope local -e OME_GODOT_CONSOLE=<Godot console exe> -- py -3.11 -X utf8 <repo>/tools/drive_mcp.py
codex mcp add drive --env OME_GODOT_CONSOLE=<Godot console exe> -- py -3.11 -X utf8 <repo>/tools/drive_mcp.py
```

- Pass `OME_GODOT_CONSOLE` (or put Godot on `PATH`). MCP clients may start servers with a reduced environment; the
  SDK's own stdio client passes only `PATH`, the profile folders and a few system variables.
- Add `--game ID` after `drive_mcp.py` for another game profile.
- Codex stops a tool after 60 s by default, and a port start with its build can take longer. Add
  `tool_timeout_sec = 600` under `[mcp_servers.drive]` in `~/.codex/config.toml`. In Claude Code,
  `MCP_TOOL_TIMEOUT` (milliseconds) sets the limit.

### Port control channel

The Godot port serves the driving protocol itself: JSON lines on loopback TCP. `drive.py port start` launches it with
these flags; you need them directly only to drive the port from another client.

| Flag (after `--`) | Effect |
|---|---|
| `--control` | Start the control server after the VM. Every other mouse, keyboard and joystick event is dropped for the whole run, so your own input cannot disturb it. Script cursor warps become injected moves (reported as `cursor_moved_to`) instead of moving your cursor. |
| `--control-file PATH` | Where to write the session file (default `<profile diagnostics>/drive-session.json`). It holds the `hello` fields plus `pid`, `pid_created` (on Windows), `port`, `token` and `started`, is written atomically, and is deleted on exit. |
| `--data-root DIR` | Use `DIR` as the profile root (`SAVE/`, `SYS4REG.INI`, `diagnostics/`) instead of `user://games/<game>`: a save sandbox. |

- The token comes from `OME_DRIVE_TOKEN`, or the port generates one and writes it to the session file. The first
  line must be `{"op":"hello","token":…}`. The server takes one client at a time, so a connection must send its
  hello within 10 s and in at most 4096 characters, or it is closed; after the hello a request line may be up to
  1 MiB.
- The port's `hello` also carries `jit_optimized`: whether its VM assembly was compiled with the JIT optimizer on
  (`drive.py port start --optimize`); and `native_aot`: whether the engine was compiled ahead of time, as a release's
  is (`drive.py port start --package`).
- `waiting` is `input` (an ADV page or hotspot wait), `menu` (a script-polled menu), `movie`, `transition`, `sleep`
  or null. `busy` means text is still revealing or a screen transition is running. `settle` waits for `input` or
  `menu` that stays not busy for `quiet_ms`.
- `wheel` without `x`/`y` turns at the driven cursor. Both targets send one notch at a time, 80 ms apart, and the
  step ends 80 ms after the last: a script that reads the wheel each frame (op `0x10d`, which SELFONT's list scrolls
  a row by per read) sees every notch, as it sees a user's.
- Port-only ops: `speed` (clock factor 0.05–8) and `lua` (`code`, optional `mod` and `timeout_ms`; capability
  `lua`). `lua` hands the chunk over through `InputAndWaits.Invoke` and polls for its result once a frame, as the
  timed operations do (`DriveDispatcher`). `shot` needs an **absolute** path, because the port's working
  directory is `godot/`. It captures the 800×600 viewport, without the menu bar above it, so the window must not be
  minimized. Points and cursor positions are canvas pixels on both targets, below the bar.
- Give `--data-root` and `--control-file` as absolute paths. A relative `--control-file` resolves against `godot/`;
  a relative `--data-root` against the launch directory, as `--game-root` does (`godot/` for an editor run on
  Windows, the caller's directory on Linux).

## Runtime capture (Frida)

Every Frida tool imports Frida as `from frida_runtime import frida` (`tools/frida_runtime.py`). Frida unpacks its
injector, about 53 MB of `frida-helper-*.exe` and per-architecture `frida-agent.dll`, into a new
`%TEMP%\frida-<id>` folder in each process that injects, and removes that folder only when the device manager
closes. The module calls `frida.shutdown()` at exit. Before it existed, every capture run and native driving
session left one folder behind. A process that is killed still leaves its folder.

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `tools/frida_runtime.py` | The `frida` module with `frida.shutdown()` registered at exit, so a run removes its `%TEMP%\frida-<id>` injector folder. Every Frida tool and `drive_native.py` import Frida through it. | *(imported)* | — |
| `tools/frida/capture_graphics.py` | Attach Frida to the running game; log archive reads/opens (ground-truth for asset resolution). See `tools/frida/README.md`. | `py -3.11 -u -X utf8 tools/frida/capture_graphics.py [AGE.EXE]` | running game → `build/games/<game>/frida-reads.log` |
| `tools/frida/capture_load_order.py` | **Primary asset-resolution capture:** recover a scene's per-asset load order from exact-start `ReadFile` reads → names via the index; confirms `resId==file_number`. Attach; replay scene; `--analyze`. | `py -3.11 -u -X utf8 tools/frida/capture_load_order.py [pid]` · `--analyze` | running game + index → `build/games/<game>/frida-load-order.jsonl`, `…-result.json` |
| `tools/frida/locate_resource_load.py` | Phase-1 locator: back-traces asset-opens to find the native AGF load chain (`0x16d5d7→0x74f1f`). | `py -3.11 -u -X utf8 tools/frida/locate_resource_load.py [pid]` · `--aggregate` | running game → `build/games/<game>/frida-resource-bt.jsonl` |
| `tools/frida/capture_resid_args.py` | Phase-2 probe: dumps the decoder's args / context / caller frame (established the loader carries only offsets, not names). | `py -3.11 -u -X utf8 tools/frida/capture_resid_args.py [pid]` · `--analyze` | running game → `build/games/<game>/frida-resid-args.jsonl` |
| `tools/frida/find_globals_base.py` | Runtime-global RE (SHELVED — see `docs/global-memory-re.md`): flat-int32 signature scan for the VM global array. Finds nothing → layout isn't flat. | `--build-sig` · `py -3.11 -u -X utf8 tools/frida/find_globals_base.py [pid]` | `*INIT` → `build/games/<game>/globals-signature.json`; scans running game |
| `tools/frida/find_global_by_sequence.py` | Runtime-global RE (SHELVED): differential resId value-scan + stability filter. Finds stack proxies; proved `G[0x62424]` is a transient arg-register. | `py -3.11 -u -X utf8 tools/frida/find_global_by_sequence.py [pid]` | running game + index → stdout |
| `tools/frida/dump_engine.py` | ★ **Dump one profile's UNPACKED engine code** from the live process for offline native RE. Captures the complete AGE module plus large anonymous executable ranges; refuses accidental overwrite. Its schema-v2 manifest records profile/catalog/ABI identity, packed-executable size/hash, runtime module path/base/size, every dumped range size/hash/gap, and an optional profile-proven landmark. Himegari's `+0x74f1f` landmark is not borrowed by Kamidori. Pure provenance helpers are covered by `frida/test_dump_engine.py`. `--reference ID` dumps a reference-only install (`paths.REFERENCE_INSTALLS`) into `build/engines/<id>/engine-dump/`; its manifest records the reference id instead of a profile. `--spawn` starts that target's `native_scratch.py` copy under Frida instead of attaching, with `SHGetFolderPathA` redirected to `build/<…>/dump-sandbox/` for LocalAppData, AppData, Desktop, Documents and ProgramData so the real saves under `%LOCALAPPDATA%\Eushully\` are never touched; it dumps after `--wait` seconds (default 20) and kills the process. | `py -3.11 -u -X utf8 tools/frida/dump_engine.py --game ID --game-root PATH <pid>` · deliberate recapture: `--overwrite` · `--game ID --spawn` · `--reference ID --spawn` | running selected game → ⚙ `build/games/<game>/engine-dump/{manifest.json,range_<base>.bin}` (reference: `build/engines/<id>/engine-dump/`) |
| `tools/frida/map_imports.py` (+ `map_imports_full.py`) | ★ **Name dynamically-resolved Win32 APIs** in the Ghidra image. Read-only: maps live-process module exports → `{addr→dll!Func}`, scans the `0x400000` module for pointer matches → `RVA→name` (ASLR-stable). `--recon` = clustering report (the gate); default writes the map. Applied to `/v2` via a `run_script_inline` pass → 248 `imp_<dll>_<func>` labels at the RVA `0x16f000` IAT (validated: CreateFileA/SetFilePointer/timeGetTime). Pure scan/cluster logic unit-tested (`test_map_imports.py`). | `py -3.11 -u -X utf8 tools/frida/map_imports.py [--recon]` | running game → `build/games/himegari/import-map.json` (+ `-singletons.json`) |
| `tools/frida/capture_gdi_fonts.py` | Record the fonts a running native AGE creates and the glyphs it asks GDI for: every `CreateFontIndirectA` `LOGFONT`, the size query of each `GetGlyphOutlineA` with the font selected at the time and the `GLYPHMETRICS` returned, and each `AddFontMemResourceEx`. These are gdi32 exports, so the game's packer does not matter. It attaches to the one running `AGE.EXE` (the game driver's native session is fine: start it, run this in the background, then run a scenario) and records for the given seconds. `--summary` prints the distinct fonts and glyph queries of the last capture, faces decoded as CP932. It showed that 4.38D creates its aspect-mode text font one unit wider than 4.33G's rule gives (`engine-re.md`, "Kamidori against Arterial, triaged"). | `py -3.11 -u -X utf8 tools/frida/capture_gdi_fonts.py [--game ID] [SECONDS]` · `--summary` | running game → `build/games/<game>/native-gdi-fonts.jsonl` |
| `tools/frida/probe_handlers.py` | Probe which region the interpreter executes from (module vs heap). Confirmed: **operand-fetch `+0x1b940` fires ~8500/s ⇒ interpreter runs from the module `0x400000`** (handlers hookable by dump address). | `py -3.11 -u -X utf8 tools/frida/probe_handlers.py [pid]` | running game → stdout (per-hook fire counts) |
| `tools/frida/capture_gfx_objects.py` | Legacy/misnamed probe: grab engine ctx (`esi` via operand-fetch `ecx`) and poll `[esi+0x53d64]`. Ghidra later proved these are 20×120-byte **script-context records**, with the current instruction length at record `+0x24`, not gfx objects or command types. Its former "0 CG records ⇒ drift is state-divergence" conclusion is invalid; op 0x215 queries the separate retained-object map described in `docs/engine-re.md`. Kept only for raw runtime context observation and historical reproducibility. | `py -3.11 -u -X utf8 tools/frida/capture_gfx_objects.py [pid] [secs]` | running game → `build/games/<game>/gfx-objects.jsonl` |
| `tools/frida/probe_frame_cadence.py` | **Frame-cadence probe** (`docs/engine-re.md` "Frame cadence — live measurement"): plain-JS hook on operand-fetch `0x41b940` (grab ctx + count operand reads) + system-DLL message/timing hooks; auto-buckets by Ctrl/skip-bit. Measured ~1,788 **operand fetches/sec** normal, ~4× fast-forward; this is not an opcode count. **Read-only/import-only — never CModule-hook the hot interpreter (crashes the game).** Play actively during capture; hold Ctrl the back half. | `py -3.11 -u -X utf8 tools/frida/probe_frame_cadence.py [secs] [proc]` | running game → `build/games/<game>/frida-frame-cadence.jsonl` + stdout report |
| `tools/frida/probe_present.py` | **Present-rate probe:** grab ctx, scan it for the D3D9 device (d3d9-vtable object with a full ~119-method table), hook `IDirect3DDevice9::Present`/`EndScene` (+ GDI-blit fallback). Found: **D3D9, UNCAPPED** (`Present` ~1908/sec, no vsync; no `ddraw`; 2D StretchRect compositor) ⇒ no fixed frame rate. Click 2–3× at start to grab ctx. | `py -3.11 -u -X utf8 tools/frida/probe_present.py [secs]` | running game → `build/games/<game>/frida-present.jsonl` + stdout report |
| `tools/frida/trace_engine_ops.py` | **Engine op-path tracer** for the differential oracle (`docs/engine-re.md` "Differential offset-path oracle"): per executed op, read `cur_ctx_index@0x53d14`/`frame_pc@0x53d2c`/`frame_codebase@0x53d28` → emit `(codebase, offset=(pc−codebase)/4)`. **Use `--hook operand` (0x41b940, proven-safe)** — `--hook tick` (0x410fb0) sees `ecx≠ctx` (0 entries). Writes `build/tracer-live.flag` when the hook is installed → launch in the background, gate the New-Game trigger on the flag (else the scene-entry burst is missed). | `py -3.11 -u -X utf8 tools/frida/trace_engine_ops.py [--hook operand\|tick] [secs]` | running game → `build/games/<game>/engine-optrace.jsonl` |
| `tools/frida/capture_global_writes.py` | **Scene-entry state capture** → auto-seed for single-scene runs (`docs/engine-re.md` "Scene-entry state snapshot"). Hooks `vm_operand_write@0x425fb0` and logs `(codebase, index, PLAINTEXT value)` for global-ints (the helper sees the value before the obfuscated store — no de-obfuscation needed). **`--spawn` captures from boot** (packer-aware: polls until `0x425fb0` unpacks, then attaches; kills the spawned pid on setup failure so no suspended orphan). `--attach` = partial (misses pre-attach writes). Validated: a real boot→New-Game→SC0000 capture seeds the VM to match the engine's whole opening. Misses writes that bypass `vm_operand_write`, such as op `0x64`'s inline array copies (`SYSTEM4@0x1ca`'s SFX channel table): a global missing from the capture is not proof that no script writes it. | `py -3.11 -u -X utf8 tools/frida/capture_global_writes.py --spawn [secs]` | running/spawned game → `build/games/<game>/global-writes.jsonl` (raw) + `build/games/<game>/scene-entry-state.json` (GameSession snapshot) |

| `tools/frida/capture_presentation_trace.py` | **Retained-state presentation trace:** correlates the current script offset with native draw/color writes, object composition, surface-command consumption, `gfx_render_frame`, queue clear, and D3D9 Present count. Read-only; distinguishes live retained state from state actually published to the window. | `py -3.11 -u -X utf8 tools/frida/capture_presentation_trace.py [secs] [pid\|AGE.EXE]` | native game → `build/games/<game>/native-presentation-trace.jsonl` |
| `tools/frida/capture_adv_text_trace.py` | **ADV text trace:** correlates SC offsets with op `0x6e`/`0x7a`/`0x204`, layout cursor/origin and 20-byte record counts, CP932 strings, surface draw/bind, and timed glyph-record publication. Read-only and deliberately limited to low-frequency known handlers; the first experimental version's D3D scan plus hot per-glyph/render hooks crashed in `frida-agent.dll` during teardown and was removed. | `py -3.11 -u -X utf8 tools/frida/capture_adv_text_trace.py [secs] [pid\|AGE.EXE]` | native game → `build/games/<game>/native-adv-text-trace.jsonl` |
| `tools/frida/capture_sfx_trace.py` | **SFX/DirectSound trace:** correlates SC offsets with `0xb4`/`0xb5`/`0xb6`/`0xc2`/`0xd9`, resource/channel load/start/release, decoder/buffer state, BGM fade ticks, and dynamically discovered DirectSound `Play`/`Stop`/volume/pan calls. Read-only. | `py -3.11 -u -X utf8 tools/frida/capture_sfx_trace.py [secs] [pid\|AGE.EXE]` | native game → `build/games/<game>/native-sfx-trace.jsonl` |

*(Static disassembly of `build/games/<game>/engine-dump/range_00400000.bin` uses **capstone** — `py -3.11 -m pip install capstone`; VA `X` → file offset `X−0x400000`.)*

### B0 script-load capture

`tools/frida/capture_script_loads.py` attaches at TITLE, hooks
`script_frame_load_resource@0x40e980`, and resolves each raw SYS4 resource id to a `.BIN` name and parent
frame in `build/games/<game>/script-loads.jsonl`. Launch the game normally, then run
`py -3.11 -u -X utf8 tools/frida/capture_script_loads.py [secs]` before selecting New Game. Use
`--analyze` to print an existing log and `--selftest` for its pure resolver checks. It is deliberately
attach-only: a process-start loader-hook trial triggered Protection Error 45, so the tool does not spawn
or bypass protection.

## Native engine RE (Ghidra)

| Tool | Purpose | Run | Reads → Writes |
|---|---|---|---|
| `ghidra_handler_map.py` | Extract the opcode→real-handler dispatch table (`handler(op)=ctx[0x26c93+op]`) from `FUN_00413860`'s override stores — the general fix for Kelebek VA-drift. `--check` diffs derived handlers vs `opcodes.toml` prose (found 0 real drift). Feeds the one-shot Ghidra annotation pass that names every handler `op_0xNN_handler` (see `docs/engine-re.md` "Materialized + applied image-wide"). | `ghidra_handler_map.py build/games/himegari/engine-dump/FUN_00413860.disasm.txt [--check]` | ⚙ `build/games/himegari/engine-dump/FUN_00413860.disasm.txt` (from ghidra-mcp `disassemble_function(0x413860)`) → ⚙ `build/games/himegari/op-handler-map.json` |
| `test_ghidra_handler_map.py` | Unit tests for the dispatch-table parser (plain runner, no pytest). | `test_ghidra_handler_map.py` | — |
| `operand_access.py` | Derive every opcode's operand access contract statically from the selected profile's unpacked engine dump, without Ghidra. It reads the dispatch table from the constructor's immediate stores; for Himegari this reproduces `build/games/himegari/op-handler-map.json` exactly. It then walks each handler and two callee levels and records every VM operand-accessor call with its constant operand index. The backward argument scan follows branch predecessors, because MSVC hoists pushes above conditional branches. Both dumps are of packed executables that call each Windows API through `call rel32` into a stub outside the module, followed by one junk byte; the walk resumes past that byte. Accessor identities are pinned per engine revision in `ENGINES` (Kamidori: the clean install's AGE 4.33G). For a new revision, `--align-accessors` proposes them by aligning same-opcode handlers with the Himegari reference image (`--align-to ID` aligns against another `ENGINES` entry). `--engine ID` reads an engine that is not the selected profile's: `kamidori-433f` (the translation's 4.33F); its report goes to `build/engines/<id>/operand-access.json`. Each `ENGINES` entry also pins `runtime_start`, the first function of the statically linked C runtime, which `revision_diff.py` needs. `--check-corpus` reports any literal operand in a store position and any operand with no native access, using the `catalog-scan` census (for a game being added, `Ome.Cli intake`'s report kept as `build/games/<game>/intake.json`, until its first `catalog-scan`). `--apply` writes `access` / `access_by_abi` for every observed opcode into `vm-map/opcodes.toml`. It is idempotent, keeps LF line endings, writes each contract where the old one stood, leaves every other line in its place (the hand-read access lines of an opcode no script uses among them), and refuses to run while any observed operand lacks evidence. `--check` reports whether `--apply` would change the registry, with the first lines that would differ, and writes nothing (exit 1 if it would). Operands proven untouched are listed in `VERIFIED_IGNORED`. Needs `capstone`. | `operand_access.py [--game ID] [--check-corpus]` · `--game ID --align-accessors` · `--apply` or `--check` (after every profile's report) | `build/games/<game>/engine-dump/range_00400000.bin` (Himegari: legacy `build/games/<game>/engine-dump/`), `build/opcodes.json`, `build/games/<game>/catalog-opcode-coverage.json` → ⚙ `build/games/<game>/operand-access.json`; `--apply` → `vm-map/opcodes.toml` |
| `test_operand_access.py` | Pure tests for contract folding and idempotent registry apply. Synthetic x86 tests cover a constructor stosd default, a hoisted push across `jle`, a branch-target push and a register-held index; they need capstone and are reported as skipped without it. | `test_operand_access.py` | — |
| `revision_diff.py` | Compare the native code behind every opcode two engine builds share, and report which opcodes behave differently. See "Opcode revision diff" below. Needs `capstone`. | `revision_diff.py REFERENCE OTHER [--depth 3] [--op 0x215 …] [--output PATH]` | both engines' dumps → ⚙ `build/revision-diff/<reference>-<other>.{json,md}` |
| `test_revision_diff.py` | Pure tests for the idiom helpers. Synthetic two-image tests cover identical builds, a changed compare constant, a moved context field, a tail jump against a call, a jump table behind a no-return call, the division and element-count idioms, and pointer-shaped strings; they need capstone. | `test_revision_diff.py` | — |
| `ghidra_port_names.py` | Carry function names from one engine build's Ghidra image to another's, for a new game's unnamed dump (`adding-a-game.md`, step 2). It uses `revision_diff.py`'s function correspondence, walked to the leaves (`--depth`, default 64), and adds a second set of starting pairs: two functions that are each the only one in their build to name some string literal, which reaches code no opcode does. A pair is used when its votes are clear (the best candidate has at least twice the runner-up's), no other function claims the partner with more votes, and the instruction shapes are alike (`--min-similarity`, default 0.6; a same-opcode handler pair is exempt). A ported name drops a script-revision suffix (`_sys4433`); a ported plate ends with a note naming its source and saying its addresses are the reference build's. Every opcode handler the reference gives no name is named from the registry (`op_0xNN_<name>`, or `op_0xNN_handler` for a placeholder label), and every operand accessor by the name `operand_access.py` `ENGINES` pins. The plan is bound to the target image's Ghidra path. In Git Bash, keep that path from being rewritten (`MSYS_NO_PATHCONV=1`). Needs `capstone`. | `ghidra_port_names.py REFERENCE OTHER --names EXPORT.json --program /folder/image --output PLAN.json [--depth N] [--min-similarity F]` | both engines' dumps, `ExportFunctionNames.java`'s export of the reference image, `build/opcodes.json` → the plan (its `summary`, `functions` and `skipped` with each reason) |
| `ghidra/DecompileToFile.java` | Decompile a list of functions of an open image into one text file, each under a `// ===== <address> <name>` line with its plate comment. An address inside a function gives that function. It is the way to read many functions in a session: ghidra-mcp's own results carry every script's compile output and run to 100,000 characters. It prints one `OMEMARK` line. | ghidra-mcp `run_ghidra_script(script_name=<full path>, args="<output path without spaces> 0x… 0x…", program=<image>)` | the open image → the text file |
| `decomp_pair_diff.py` | Show how two builds' versions of a function differ, for the revision diff's triage (`adding-a-game.md`, step 4). It reads two files `ghidra/DecompileToFile.java` wrote, one per image, with the same functions in the same order, and pairs them by position. Each line first loses what differs between any two builds of the same code: the addresses in Ghidra's default names, numbers of four hex digits or more (context offsets and addresses), the decompiler's variable numbering, plate comments and plain local declarations. With no index it prints one line per pair with the count of changed lines; with indices it prints those pairs' diffs. A changed line is not always changed code: the decompiler types a local differently, or one image names a function the other does not. | `decomp_pair_diff.py A.c B.c [index …]` | two decompile files → stdout |
| `test_decomp_pair_diff.py` | Pure tests for the masking and the pairing. | `test_decomp_pair_diff.py` | — |
| `ghidra/ExportFunctionNames.java`, `ghidra/ApplyFunctionNames.java` | The Ghidra side of the name port. `ExportFunctionNames` writes every function that has a name of its own or a plate comment, as JSON. `ApplyFunctionNames` applies a plan: it refuses a plan made for another program, creates a function where the plan names an entry Ghidra has none at, leaves a function that already has a name of its own or a plate, and adds an address suffix on a name clash. A plan is also a way to keep hand annotations reproducible. Each prints one `OMEMARK` line, which is the line to look for in ghidra-mcp's long result. | ghidra-mcp `run_ghidra_script(script_name=<full path>, args=<JSON path without spaces>, program=<image>)`, then `save_program` | the open image ↔ the JSON file |
| `engine_ctx_build.py` | Build the `EngineCtx` struct artifacts from `vm-map/engine-ctx.toml` (canonical `ctx`-field registry). `--lint` checks overlap/OOB/dup/type plus required profile, executable, unpacked-image, size, image-base, and SHA-256 provenance. The struct applies only to the identified Himegari `S4IC422`/`SYS4422` `/v2` image via `run_script_inline`; offsets cannot transfer to another AGE executable by implication. | `engine_ctx_build.py --build` · `--lint` · `--check` (lint, then fail if `docs/engine-ctx-reference.md` is stale) | ⚙ `vm-map/engine-ctx.toml` → ⚙ `build/engine-ctx.json`, ⚙ `docs/engine-ctx-reference.md` |
| `test_engine_ctx.py` | Unit tests for the ctx builder, including rejection of unscoped executable metadata and the `--check` staleness comparison (plain runner). | `test_engine_ctx.py` | — |

### Opcode revision diff

`tools/revision_diff.py` compares two engine builds opcode by opcode, without Ghidra, to find where a SYS4 revision
changed what an opcode does. Engine ids are `tools/operand_access.py` `ENGINES` keys: the profiles `himegari`
(AGE 4.24J) and `kamidori` (4.33G), the translation's `kamidori-433f` and `arterial` (4.38D).
Each needs its unpacked dump (`tools/frida/dump_engine.py`) and pinned dispatch facts, accessors and
`runtime_start`.

```
py -3.11 -X utf8 tools/revision_diff.py himegari kamidori            # all shared opcodes, ~10 s
py -3.11 -X utf8 tools/revision_diff.py himegari kamidori --op 0x215  # one opcode, written where --output says
```

It pairs same-opcode handlers, learns which functions, context fields and globals correspond by aligning them,
reduces each opcode's handler and three callee levels to behavior facts, and compares those. Compiler and layout
idioms are weak facts. The method and its limits are in `docs/engine-re.md`, "Comparing engine builds".

Each opcode, and each shared helper (a function at least eight opcode trees reach, compared once), gets a verdict:

| Verdict | Meaning |
|---|---|
| `same` | No fact differs. Strong evidence that the code behaves alike for the same state. |
| `minor` | Only weak facts differ: layout, register allocation, inlining, unidentified API calls. |
| `differs` | A strong fact differs: a compared or tested constant, an operand access, a string, a float constant, a context field, a helper call. A candidate to read, not a finding. |

The Markdown report lists the differing opcodes by score with their strong facts (`−` only in the reference build,
`+` only in the other) and the function pairs that hold them. The JSON adds every fact with its count, up to six
sites (`<instruction> in <function>`), the weak facts and the localized per-function differences. Rows whose only
strong fact is one context field or one helper call (scores up to about 6) are usually correspondence gaps.

## Tooling improvement backlog

Prioritized from the 2026-07-10 Phase-A retrospective. These are proposed capabilities, not tools that
already exist. Prefer additions that produce reusable, offset-keyed evidence over one-off capture scripts.

| Priority | Improvement | Payoff / acceptance boundary |
|---|---|---|
| P0 | **Native/port checkpoint harness** | Launch either runtime windowed, identify the process/window, record script offset + timestamp, capture a screenshot and retained-state/trace excerpt, and classify normal exit vs harness stop vs crash. A checkpoint should produce one comparable artifact bundle for the same bytecode offset in both runtimes. |
| P0 | **Sanctioned Windows GUI automation** (small Win32 helper, AutoHotkey, or pywinauto) | Repeatably focus the game, capture window geometry, and inject only explicitly requested clicks/keys. Manual input remains the independent oracle; automation removes capture/setup repetition. |
| P0 | **Scriptable 32-bit debugger** (x32dbg or WinDbg) | Breakpoints and memory snapshots for hot render/audio workers that are unsafe to hook densely with Frida. Keep Frida probes on known low-frequency handlers; never restore hot interpreter/glyph/render hooks merely for convenience. |
| P1 | **Timestamped video capture** (ffmpeg desktop capture or command-controlled OBS) | Frame-by-frame native/port evidence for movie `0x236`, fades, and short animation boundaries without PNG-per-frame overhead. |
| P1 | **WASAPI loopback/audio capture** | Objective SFX/BGM/voice start time, channel reuse, volume, stop, and waveform comparison. This is the main evidence upgrade for the pending SFX slice. |
| DONE 2026-08-02 | **One-command validation driver** | Landed as `validate.py` with strict layered levels, per-gate logs/timeouts, generated-reference checks, Python/.NET/corpus/Godot gates, the faithful-wait sweep, whitespace checking, and a Godot-process leak audit. Canonical usage is under "Project validation" above. |
| P1 | **Golden SC0000 checkpoint corpus** | Preserve the first 10-15 native pages as offset-keyed screenshots, click/wait events, retained-state summaries, and trace excerpts. Port regressions should be comparable without replaying the entire investigation. |
| P2 | **Opcode dossier generator** | Combine corpus callsites/operands, native handler/worker addresses, runtime samples, Ghidra names, opcode provenance, and port coverage into a per-op investigation packet. |
| P2 | **More typed Ghidra state** | Materialize retained-object, surface-slot, text-layout, and audio-channel structures so related handlers decompile against shared named fields. |
| P2 | **Narrow write access for cross-session memory** | Let mandatory milestone closeout update `MEMORY.md` and `himegari-port-status.md` directly; the current out-of-workspace copy/approval path adds avoidable handoff friction. |

## Historical / one-off

| Tool | Purpose |
|---|---|
| `probe_*.py` (`probe_header`, `probe_leads`, `probe_refs`, `probe_tables`, `probe_tags`, `probe_types`, `probe_xref`) | Container/opcode format-RE probes used to reverse the format originally. Kept for reproducibility; not part of the normal workflow. |
| `pack_check.py` | Checks whether `AGE.EXE` is packed (it is: entropy-8 code sections, zeroed IAT). `SYS4AB.BIN` is NOT a separate image — it's `XOR-0xFF(AGE.EXE)` byte-for-byte (0x2c header + XOR payload). The unpacked engine exists only in memory → dump it with `frida/dump_engine.py`. |

### GPU compositor phase counters (2026-09-21)

`-PerfLog` additionally appends `gpu_blur_scan_ms`, `gpu_transform_ms`,
`gpu_object_lookup_ms`, `gpu_texture_lookup_ms`, and `gpu_sprite_update_ms`, each
paired with a matching `gpu_<phase>_allocated_bytes` column. These are summed
CPU wall times and current-thread managed allocations, enabled only during profiling.
Since engine layering milestone 7 the first three time the graphics native's frame lowering
(`Natives/Graphics.Frame.cs`) on every frame, whichever renderer draws it: blur scanning
(including the once-per-pass blur-range collection), transform construction (including range
transforms) and the object's source-slot lookup in its main object loop; nested effect expansion
is not included in those three counters. Texture lookup and sprite updates cover all renderer texture/fill
submissions, including effect-generated draws. Sprite updates include pooled-item
acquisition, material selection and property assignments; final unused-item hiding
is outside that counter. Texture lookup includes cache misses/preparation/uploads,
so its time overlaps the existing upload counter. These counters are subsets of
recomposition, not additional time to add to it, and do not measure GPU execution.

For FIELD comparison, reload the same save, repeat idle/scroll/selection intervals,
and close normally to flush the CSV. Compare against the unmodified baseline as
fine-grained timing itself introduces some profiling overhead.

### ADV layout rejection diagnostics

`-StartupDiagnostics` includes `adv-text-layout-result` events in
`build/validation/title-newgame/timeline.jsonl`. Each show-text run records its
layout, text, starting/ending cursor, right/bottom bounds, font/size/line spacing,
requested/rendered glyph counts, wrapped-line count, and vertical-overflow stop.
Use these to distinguish layout truncation from retained publication problems.
The events are emitted only when timeline logging is enabled. Reproduce through
normal startup: direct SC0000 launch may lack the game's required layout setup.

### Asset warning script coordinates

Normal Godot `[asset-resolution]` warnings now append the current script/offset,
opcode, and script call stack when VM tracing has initialized. ResourceMap still
deduplicates by the original asset error, so the first occurrence is reported.
For a repeatable failure, restart normally and copy the complete first warning;
no timeline capture is needed to identify its caller. These coordinates describe
the current VM state; main-thread render-resolution warnings may occur after the
original resource binding, whereas synchronous set-texture warnings identify its
executing instruction.

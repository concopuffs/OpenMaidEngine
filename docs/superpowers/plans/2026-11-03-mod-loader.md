# Mod loader — implementation plan

**Date:** 2026-11-03
**Spec:** `docs/superpowers/specs/2026-11-03-mod-loader-design.md` (cited as "spec §n"). This plan orders the work and
names the code seams; the spec stays the design's home.
**Status:** accepted 2026-11-03, with the four §1 decisions. Work starts at M1.

## 0. Starting point (what the code has today)

| Need | Existing seam | Gap |
|---|---|---|
| Compile fragments, apply plans | `Age.Script/Patch/OmsPatchCompiler`, `OmsPatchApplier` (load-order composition, conflicts, interactions, notes) | plans hold `run` targets as literal packed ids (`OmsPatchOperandKind` has Literal, Data, Label, Site) |
| Build append catalogs | `Age.Engine/Sys4/Sys4AppendCatalogWriter`; CLI `oms-append` | selector is a CLI argument; records are scripts only |
| Resolve `run NAME` | `OmsCompiler.ScriptOperand` → `GameModule.TryGetScript` → `IrOperand.Literal` | no mod-scoped names, no `<mod-id>:NAME` |
| Mount catalogs | `Sys4AssetCatalog.Load` mounts every `*.AAI` beside `SYS4INI.BIN`; `MountAppend` | no way to mount a catalog from another directory |
| Read assets | `Sys4AssetStore(catalog, archiveRoot, looseRoots)`: loose roots first, then one archive root | one archive root for every pack, so an ALF outside the game root cannot be read |
| Overlays | `AssetLaunchOptions` (`--overlay-root`) builds the loose-root list | no mod layer above it |
| Port startup | `godot/Main.cs` `_Ready`: game root → identity → profile → catalog → `ResolvedGamePersistence` → store | the port references `Age.Engine` only, not `Age.Script`, so it cannot compile fragments |
| Saves | `DirectoryNativeDatStore.SaveNumberedFile`, called from `VirtualMachine.Persistence` | no side file per slot; delete/copy handle `.DAT`/`.STH` pairs only |
| TOML in C# | none (Python tools read TOML; C# reads generated JSON) | manifests, `selectors.toml` and stamps need a reader |

## 1. Decisions this plan makes

The user accepted all four on 2026-11-03.

1. **New project `engine/Age.Mods`** (references `Age.Engine` and `Age.Script`; referenced by `Age.Cli`, the tests,
   and `godot/OME.csproj`). Packages, manifests, linking, cache and stamps live there, so the port and `omp check`
   run the same code. The port then also carries `Age.Script`; it has no platform dependencies.
2. **TOML reader: the Tomlyn NuGet package** (BSD-2-Clause), recorded in `THIRD_PARTY_NOTICES.md`. The alternative is
   a hand-written reader for the subset the spec uses. That is smaller to ship but a bug surface for untrusted input.
   Recommendation: Tomlyn, behind one `ManifestReader` so it can be swapped.
3. **Patch-point data reaches C# as JSON.** `tools/patch_points_build.py` reads `vm-map/patch-points-<profile>.toml`
   and writes `build/games/<profile>/patch-points.json`, the same pattern as the globals registry. The compiler reads
   the JSON.
4. **The linked layer is materialized on disk** in the cache (`mods/.cache/<key>/`): patched and replaced scripts and
   winning asset overrides as loose files in `loose/`, each mod catalog as `APPENDnn.AAI`/`.ALF` in `catalogs/`. The
   resolver mounts that directory instead of reading packages at runtime. A cache hit is then a plain directory mount,
   and `omp export` copies the same files.

## 2. Milestones

Each milestone ends with its tests green (`dotnet test engine/AgeEngine.sln`), the Godot self-test passing, and a
commit. Gate numbers are spec §13.

### M1 — Packages and manifests (gate 1)

- `Age.Mods/Packages/`:
  - `IModPackage` with `ZipModPackage` (`System.IO.Compression`) and `FolderModPackage`. Both expose a sorted list of
    entry paths and `Open(path)`. The zip form enforces the spec §11 limits while it reads: entry count, expanded size
    per entry and in total, a 200:1 ratio check on each entry's header, and the expanded byte count checked again while
    streaming, since headers can lie.
  - Path checks shared by both forms: no absolute paths, no `..`, no drive letters, no case-insensitive collisions.
    Folder packages also refuse symlinks and junctions that leave the folder.
  - `PackageHash`: SHA-256 over the sorted (path, content hash) list. The zip and folder forms of one mod then hash the
    same, which gate 1 needs.
- `Age.Mods/Manifest/`: `ModManifest` record, `ManifestReader` (decision 2), validation of spec §3.3:
  - `omp = 1`, id grammar, semantic version, `[game].profile`, `target = "native"`, `revisions` format;
  - `[[requires]]` version ranges (a small range parser: `>=`, `<`, `,`), `[[conflicts]]`, `[order]`;
  - `[scripts].names`, `[assets].names`, `[replace]` with `over`;
  - unknown keys are errors with the key's path.
- Layout classification (spec §3.2): each entry is a patch source, a plan, a new script, a replacement, an asset, a
  readme or license, or an ignored stray (informational note). Cross-checks: every `scripts/*.oms` is named in
  `[scripts].names` and the reverse; `replace/` names are `*.BIN`.
- Tests: synthesized packages built in the test (zip and folder of the same tree) produce the same classification and
  hash; each malformed case (bad key, `..`, collision, oversized entry, ratio bomb, unnamed script) is rejected with its
  reason.

### M2 — Named patch points

- `vm-map/patch-points-himegari.toml` with one real point to start, for example after the experience award in
  `ADDEXP.BIN` if the decode confirms the site, otherwise a site in SC0000 that gate 7 already uses.
- `tools/patch_points_build.py --build [--profile P]`: validates each `at` entry against the installed base (the
  fingerprint matches a catalog script; the site is an instruction start in its decode), writes
  `build/games/<profile>/patch-points.json` and `docs/patch-point-reference[-<profile>].md`.
- Compiler: the fragment parser accepts `site <dotted.name>`. `OmsPatchCompiler` looks the name up for the base's
  fingerprint and fails with the supported fingerprints when there is no match. `OmsPatchOperation` gains an optional
  `point` name, kept for diagnostics.
- Docs: `AGENTS.md` generated-files table and canonical map, `tools-reference.md`, `PROJECT-STRUCTURE.md`.
- Tests: a fragment by point name compiles to the same plan as the same fragment by offset; an unknown fingerprint
  lists the supported ones.

### M3 — Resource operands and mod-scoped names

- IR and plans: `IrOperandKind.Resource` and `OmsPatchOperandKind.Resource`, both holding an index into a reference
  table (`resources`: `<mod-id>:NAME` strings) carried by `IrScript` and `OmsPatchPlan`. The JSON form is
  `{"t": 0, "res": k}`. The plan version moves to 2; version 1 plans keep loading.
- Compiler: `ScriptOperand` and resource operands consult a `ModNameScope` (the current mod's own names, and the
  names of declared dependencies under `<mod-id>:`). A mod name compiles to a Resource operand; base names stay
  literal, as spec §6.2 allows.
- `IrEncoder.Encode(script, resolver)` and `OmsPatchApplier.Apply(..., resolver)`: a Resource operand must be
  resolved to a packed id at encode time; an unresolved one is an error naming the reference.
- `oms-append` and `oms-patch` gain `--mod-id` and `--names` so the single-file tools emit the same operands.
- Tests: a plan with a Resource operand round-trips through JSON; applied with two different selector maps it encodes
  the two expected packed ids; a version 1 plan still loads.

### M4 — Linker core (gate 2)

- `Age.Mods/Linking/`:
  - `StackResolver`: profile and target, `[game].revisions` against the SHA-256 of `SYS4INI.BIN`, dependencies and
    ranges, declared conflicts, then load order by spec §6.3 (topological sort; hints; load-order file; id).
    Cycles are reported with the cycle.
  - `ModLinker.Link(base, stack, selectors) → LinkResult`: spec §6.1 steps 4–8. Per script: base or the highest
    replacement (with the `over` rule), then plans in load order through `OmsPatchApplier`. Stored plans are used
    when their fingerprint matches; otherwise the fragment is compiled and a note is recorded.
  - `LinkReport`: the spec §7 classes, each line naming mods, script and original site; text written to
    `diagnostics/mod-link.txt`; a console summary.
  - Failure policy: errors stop the link; `--skip-failed-mods` drops the mods involved and relinks.
- Examples: `oms/examples/mods/` with three small Himegari mods: two patching different sites of SC0000 and a third
  that overlaps one of them.
- Tests (gate 2): the two compose; the third stops the link with a report naming all three; shuffling the user order
  links identically where dependencies fix the order; a replacement conflict and its `over` resolution.

### M5 — Selectors, catalogs and mounting (gate 3)

- `SelectorStore` (`mods/selectors.toml`): lowest free selector above the install's catalogs, kept across links,
  released only by `--forget-mod`. Exhaustion is the spec §6.2 error that lists the allocations.
- Catalog building per mod: scripts compiled with the mod's `ModNameScope`, then new assets, in manifest order;
  record 0 is the first script or a generated empty stub. Resources resolve after allocation. Uses
  `Sys4AppendCatalogWriter`, which gains non-script records.
- Engine seams:
  - `Sys4AssetCatalog.Load(path, extraAppendDirectories)`: mount the cache's `catalogs/` after the install's own
    `*.AAI` files. A mod catalog whose selector the install already uses is an error, not a silent replace.
  - `Sys4AssetStore`: archive root per pack. Each mounted catalog remembers the directory its `.AAI` came from, and
    its ALF is opened there.
  - The linked `loose/` directory goes first in the loose-root list, ahead of `--overlay-root` roots (spec §6.5).
  - Layer attribution: the store can say which root or pack served an entry, so `omp explain` and the report can
    name the mod.
- Cache (spec §6.4): the key computed from the listed inputs; on a hit, manifests and package hashes are still
  checked; the cache directory is written to a temporary name and renamed, so a failed link never leaves a half cache.
- Tests (gate 3): two mods with new scripts keep their selectors through reorder, removal and re-adding; a
  `run other.mod:NAME` across a declared dependency resolves; a VM run of an INIT2 that executes both record 0s in
  selector order.

### M6 — Port integration, saves and the CLI (gate 4)

- Port startup (`godot/Main.cs`): after `ResolvedGamePersistence`, run discovery (spec §9: `mods/`,
  `load-order.txt`, `--mod`, `--no-mods`, `--skip-failed-mods`, `--forget-mod`), link or hit the cache, then build
  the catalog and store with the linked layer. A link failure stops startup with the report path, as a bad game root
  does now. Print the resolved order and the layer list under `[mods]`.
- Save stamps: `SAVE/SAVEnn.mods.toml` written beside `SAVEnn.DAT` by the same save path, only when a stack is
  loaded; `DeleteNumberedPair` and `CopyNumberedPair` carry it along. On load, compare and warn (spec §8): a console
  warning, and a line in `diagnostics/`. A visible in-game notice is out of scope for v1.
- `Age.Cli omp` group: `build`, `check`, `explain` (spec §10). `omp build` writes a deterministic zip: sorted
  entries, fixed timestamps, stored plans beside fragments with the toolchain version.
- Driver: `tools/drive.py port start` gains `--mod PATH` pass-through.
- Tests and gate 4: port-driven scenario under `tools/drive/scenarios/` that starts with a mod whose append-catalog
  scene is reachable, saves inside it, restarts, and loads; a load under a different stack reports the difference; the
  gate 7 SC0000 fragment as a mod resumes a pre-patch save on its page.

### M7 — Native export and Kamidori (gates 5 and 6)

- `omp export --output-dir DIR`: the linked cache's files in native form (loose `.BIN`, `APPENDnn.AAI`/`.ALF`,
  loose asset overrides), refused for a non-`native` target.
- Gate 5: export the gate 2–3 stack, install it into a `tools/native_scratch.py` copy, and drive native AGE through
  the same `.steps` scenario as the port; pages and save behavior match.
- Gate 6: a Kamidori fragment against the translated `patch/SC0000.bin` base (its overlay root is part of the base,
  spec §2) links and runs in the port. This also lands the long-standing "Kamidori example fragment" item.

## 3. Out of this plan

Everything in spec §12 (launcher, Lua, mod-owned sites, declarative data patches, mod merging, precompiled new
scripts), an in-game notice for stamp mismatches, and LZSS encoder parity.

## 4. Reference closeout per milestone

- `docs/tools-reference.md` for `omp`, `patch_points_build.py`, the new port flags and `drive.py --mod`;
- `docs/PROJECT-STRUCTURE.md` for `engine/Age.Mods`, `oms/examples/mods/`, the cache and `mods/` folders;
- `AGENTS.md` generated-files table (patch points);
- `THIRD_PARTY_NOTICES.md` if Tomlyn is adopted;
- the spec's status line once gates pass; the roadmap §3.6 entry; the status memory.

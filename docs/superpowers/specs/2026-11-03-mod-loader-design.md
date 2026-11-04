# Mod loader — Open Maid Pack loading, linking and native export

**Date:** 2026-11-03
**Status:** accepted 2026-11-03. Implementation plan: `docs/superpowers/plans/2026-11-03-mod-loader.md`; no loader
code has been written yet.
**Home in the canonical map:** this file is the mod loader design. Direction stays in
`docs/remake-architecture-and-roadmap.md` §3.4–§3.6 and §3.10, which this spec implements in part. Fragment syntax,
transform plans and their application rules stay in `docs/superpowers/specs/2026-10-06-open-maid-script-design.md`
§9–§10 (cited below as "OMS §n"). Append catalog facts stay in `docs/engine-re.md` ("S4AC append catalogs and packed
resource ids"). Save layout stays in `docs/sys4-format-notes.md`. This spec cites those homes and does not restate
their evidence.

## 1. Purpose and scope

The OMS toolchain already compiles fragments into transform plans, applies several plans to one script in load order
with conflict detection, and builds append catalogs. Native AGE ran both (OMS gates 6 and 7). What is missing is the
step from "a folder of mod files" to "the game runs with them": packages, load order, linking at startup, caching,
attribution, and save bookkeeping. This spec defines that step.

**Version 1 loads:**

| Content | Form | Effect |
|---|---|---|
| Patches | transform plans, fragment source, or both (§4.1) | composed per script on the pristine base (OMS §10.4) |
| Script replacements | whole `.BIN` files | ordered overrides of a catalog script |
| New scripts | OMS script source | linked into one append catalog per mod (§6.2) |
| Assets | files under an original asset name, or under a new name | ordered overrides of catalog assets; new names become records of the mod's append catalog (§4.4) |

Data edits (balance values, table records) ride on new scripts: a mod's record 0 runs at INIT2 through op `0x143`
and edits tables with OMS `with` blocks, as `oms/examples/kamidori-gate6/` does. A declarative data-patch format is
future work (§12).

**Not in version 1:**

- Lua or any other runtime scripting, host hooks and semantic events (roadmap §3.8; phases D–E);
- `extended`-mode content: extension opcodes, virtual script identities and engine-owned mod state (OMS §3.3, §9.4);
- patches that target code another mod inserted (OMS §13: "Patches against code another mod inserted");
- a mod manager inside the engine. Enabling, ordering and configuring mods belongs to a future launcher (§12). The
  engine keeps a file-and-flag interface that such a launcher can drive.

## 2. Terms

- **Mod:** one package with one manifest and one id.
- **Base:** the game install as the port serves it, including any selected overlay roots (`--overlay-root`), but
  without mods. A fragment's fingerprint is taken against the base (OMS §10.1), so a translation overlay is part of
  the base and needs its own fragments.
- **Stack:** the enabled mods in load order.
- **Link:** turning the base plus the stack into the scripts, catalogs and asset layers the VM runs.

## 3. Packages

### 3.1 Container

An Open Maid Pack is either:

- a `.omp` file: a ZIP archive with `manifest.toml` at its root. Entries may be stored or deflated. The ZIP container
  is the only packaging; OMP adds no compression of its own;
- an unpacked folder with the same layout, for development.

Both forms load identically. Version 1 reads every package as a read-only tree.

### 3.2 Layout

```text
<mod>/
├── manifest.toml
├── patches/        fragments (*.oms) and transform plans (*.omspatch.json)
├── scripts/        new scripts (*.oms), linked into the mod's append catalog
├── replace/        whole-script replacements (<SCRIPT>.BIN)
├── assets/         asset overrides, by original asset name
├── README.md       optional
└── LICENSE         optional
```

Directories a mod does not use may be absent. A file outside these directories is ignored and listed as an
informational note, so a stray file never changes behavior silently.

### 3.3 Manifest

```toml
omp = 1                              # OMP format version
id = "example.double-exp"            # lowercase, dot-separated; unique in a stack
name = "Double EXP"
version = "1.2.0"                    # semantic version
authors = ["…"]
description = "…"

[game]
profile = "himegari"                 # embedded profile id
target = "native"                    # "native" only in version 1 (OMS §3.3)
revisions = ["sha256:…"]             # optional: SYS4INI.BIN fingerprints this mod supports

[engine]
oms = 1                              # OMS language version the sources use

[[requires]]
id = "himegari-base"
version = ">=1.0, <2"

[[conflicts]]
id = "example.half-exp"
reason = "both rescale the same award"

[order]
after = ["himegari-base"]            # hints; hard requirements already order dependencies
before = []

[scripts]
names = ["DBLEXP", "DBLEXP_UI"]      # first records of the append catalog; record 0 runs at INIT2

[assets]
names = ["DBLEXP_ICON.AGF"]          # new assets, as the records after the scripts

[replace]
"SC0010.BIN" = { over = "other.mod" } # this replacement deliberately supersedes other.mod's (§4.2)
```

- `[game].revisions`, when present, lists the SHA-256 fingerprints of `SYS4INI.BIN` the mod supports. Any other
  installed catalog is a hard error that prints the installed fingerprint (`omp check` prints it too). The catalog
  revision tag (`S4IC422`) is not used, because it names the engine ABI and a game patch can leave it unchanged.
  Patches need no entry, since each carries its own script fingerprint. Replacements, new scripts and data edits
  should declare one, because nothing else detects a revision they assume.
- `id` is the identity used everywhere else: load order, selector allocation (§6.2), save stamps (§8) and
  attribution (§7).
- A `[[requires]]` entry is a hard dependency. A `[[conflicts]]` entry is a declared hard conflict.
- `[scripts].names` and then `[assets].names` fix the append catalog's record order, so record numbers survive edits
  to the sources. A file in `scripts/` that the list does not name is an error, and so is a new asset in `assets/`
  that `[assets].names` does not name.
- Unknown keys are errors in version 1, so a typo cannot drop a setting silently.

## 4. Content kinds

### 4.1 Patches

A patch is a fragment (`patches/<name>.oms`), a transform plan (`patches/<name>.omspatch.json`), or both under one
stem.

- **Both.** The packing tool (§10) compiles every fragment and stores the plan beside it, recording the plan's base
  fingerprint and the toolchain version. The loader uses the stored plan when its fingerprint matches the base.
  Otherwise it recompiles the source and uses that result.
- **Plan only.** The loader uses the plan; a fingerprint mismatch is a hard error.
- **Source only.** The loader compiles it at link time. Packed and unpacked mods may both be source-only.

Shipping source keeps a mod readable and auditable. Shipping the plan protects players from two risks of compiling
on their machine: a later compiler or prelude can bind a fragment differently, and a compile error then appears for
the player instead of the author. When the loader compiles a fragment whose stored plan is missing or stale, it
reports which mod and fragment it compiled, and any compile error names the fragment's file, line and column.

A fragment names its sites by original offset, by base label or sub (OMS §10.1), or by a named patch point (§5).

### 4.2 Script replacements

`replace/<SCRIPT>.BIN` replaces a catalog script for the stack's later layers. It is coarse by design (roadmap
§3.5):

- two mods replacing one script is a hard conflict, unless the later one names the earlier in its `[replace]` table
  (`"SC0010.BIN" = { over = "other.mod" }`, §3.3). The named mod must then load first;
- a patch from any mod against a replaced script is checked against the replacement's fingerprint. A fragment
  written against the original therefore fails with a fingerprint error that names the replacing mod, rather than
  landing on the wrong instructions;
- to patch another mod's replacement on purpose, write the fragment against the replacement's fingerprint and
  declare that mod in `[[requires]]`, so it always loads first.

### 4.3 New scripts

`scripts/*.oms` are OMS script units. Each mod's new scripts form one append catalog (§6.2). Inside a mod, scripts
refer to each other with `run NAME`, using the name in `[scripts].names`. Another mod's scripts are reached only
through a declared dependency, as `run <mod-id>:NAME`.

### 4.4 Assets

`assets/<NAME>` overrides the catalog asset of that basename. Lookup is case-insensitive, as AGE's own loose
lookup is.

An asset whose name the base catalog does not contain is a **new asset**. It becomes a record of the mod's append
catalog, after the mod's scripts, in `[assets].names` order. AGE's resource operands are universal packed ids, and
an id with a nonzero high byte opens a record of a mounted append catalog (`engine-re.md`, "Universal packed
resource addressing"). A new asset therefore needs no engine extension and works natively. Scripts name it as
`<mod-id>:NAME` (or `NAME` inside its own mod), and the linker resolves the name to its packed id (§6.2).

## 5. Named patch points

A named patch point gives a fragment a stable, revision-proof site name (roadmap §3.4; OMS §13).

Each profile owns a registry at `vm-map/patch-points-<profile>.toml`:

```toml
[[point]]
name = "himegari.experience.after_calculation"
script = "ADDEXP.BIN"
description = "after the experience award is computed, before it is applied"

[[point.at]]
fingerprint = "sha256:9f2c…"
site = 0x4f1

[[point.at]]
fingerprint = "sha256:41aa…"         # a patched game revision
site = 0x501
```

- A fragment writes `after site himegari.experience.after_calculation`. The compiler looks up the base's
  fingerprint in the point's `at` list; no match is a compile error that lists the fingerprints the point supports.
- A transform plan stores the resolved original offset together with the point name, so attribution and
  diagnostics can show the name.
- The registry is source data. `patch_points_build.py --build` validates every entry against the installed base
  (the fingerprint matches; the site is an instruction start) and generates `docs/patch-point-reference[-<profile>].md`.
  The registry and its generator join the generated-files table in `AGENTS.md`.
- The first-party base mod (roadmap §3.8) will own the curated points. Until it exists, the registry lives in
  `vm-map/`, beside the global registries it complements.

## 6. Linking

### 6.1 Pipeline

Loading is transactional. Nothing is mounted until every step succeeds.

1. **Discover** the packages: the user's mods folder and every `--mod PATH` (§9).
2. **Read and validate** each manifest and its file tree against §3 and §11.
3. **Resolve the stack:** profile and target, dependencies and version ranges, declared conflicts, then the load
   order (§6.3).
4. **Fingerprint the base** scripts the stack touches.
5. **Compile:** fragments without a usable plan, and every mod's new scripts (§6.2).
6. **Combine,** per catalog script:
   - take the base script, or the highest-ordered replacement of it;
   - apply every plan against it in load order, by OMS §10.4. A plan whose fingerprint does not match the script
     chosen here is a hard error.
7. **Build** one append catalog per mod that ships new scripts or new assets (§6.2).
8. **Report** interactions (§7). A hard conflict or error stops the load.
9. **Write the link cache** (§6.4) and mount it (§6.5).

### 6.2 Append catalogs and selectors

A catalog's selector is stored in its header, and every packed id that reaches one of its scripts is
`selector << 24 | record` (`engine-re.md`, "S4AC append catalogs"). Saves store packed script ids in their frames,
and `RT.DAT` stores read flags by packed id. A mod's selector must therefore stay the same for as long as the mod
stays installed.

- **Allocation.** The loader keeps `mods/selectors.toml` in the profile's data root, mapping mod id to selector. A
  mod that ships new scripts or new assets receives the lowest selector that is neither used by the base install's `*.AAI`
  catalogs nor already allocated. It keeps it on every later link, whatever the load order.
- **Release.** A selector is freed only by an explicit `--forget-mod ID`. Removing a mod's package does not free
  it, so reinstalling the mod restores its saves.
- **Range.** Selectors run from 1 to `0x7f`. AGE's selector extraction is an arithmetic shift, so higher selectors
  are unusable (`engine-re.md`). That leaves 127 minus the install's own catalogs (Himegari ships 1, Kamidori 3).
  Only mods with new scripts or assets use one. When none is free, the link fails with an error that lists the
  allocations; `--forget-mod` releases a removed mod's selector. Merging mods is future work (§12).
- **Symbolic references.** Plans and compiled scripts must not freeze a selector. A new plan operand kind,
  `Resource`, holds a script or asset reference by name (`<mod-id>:NAME`, or a catalog name). The linker resolves it
  to a packed id after allocation. `oms-patch` and `oms-append` emit `Resource` operands for every `run` and every
  resource operand whose target belongs to a mod; references to base scripts and assets may stay literal.
- **Record 0.** Op `0x143` runs every mounted catalog's record 0 as a script at INIT2, in ascending selector
  order. Record 0 is therefore always a script: the first name in `[scripts].names`, or, for a mod that ships new
  assets but no scripts, an empty stub script the linker generates. Selector order controls the order between
  mods.

### 6.3 Load order

1. Dependencies come before their dependents.
2. Within that, `[order]` hints are applied.
3. Remaining ties are broken by the order of the user's load-order file (§9), then by mod id.

A cycle in requirements or hints is a hard error that lists the cycle. The resolved order is printed at startup,
written to the link report (§7), and stamped into saves (§8).

### 6.4 Cache

The link result is written to `mods/.cache/<key>/` in the profile's data root. The key is a SHA-256 over:

- the profile id and the base fingerprints of every touched script;
- each mod's id, version and package hash, in load order;
- the selector allocation;
- the toolchain version (compiler and preludes).

A cache hit skips steps 4–8 but still validates the manifests and package hashes. The cache holds only derived
files and can be deleted at any time. The loader never writes into the game install or a package.

### 6.5 Mounting

The linked result is one more read-only layer of the content resolver. From highest to lowest priority:

1. the linked mod layer (patched and replaced scripts, mod append catalogs, asset overrides in load order: later
   mods win);
2. `--overlay-root` roots, in argument order;
3. the game root's loose files and append catalogs;
4. the catalog's ALF archives.

The resolver can report, for any script or asset, which layer and which mod supplied it.

## 7. Conflicts, interactions and reporting

The loader classifies what it finds, following roadmap §3.5:

| Class | Examples | Effect |
|---|---|---|
| Hard conflict | two edits of one operand; overlapping regions (OMS §10.4); two unordered replacements of one script; a declared `[[conflicts]]`; a fingerprint mismatch; a missing dependency | the load stops |
| Ordered interaction | several mods inserting at one site | reported with the order applied |
| Override | several mods supplying one asset or script | reported with the winner |
| Note | a fragment compiled at load time; a file outside the layout; a proven code position that stays a number (OMS §10.4) | reported |

Every line names the mods involved and, for script content, the script and original site. The full report is
written to `diagnostics/mod-link.txt` in the profile's data root and summarized on the console.

**Failure policy.** By default a hard conflict or error stops startup with the report. `--skip-failed-mods` drops each
mod involved in a failure, relinks, and continues if the rest links. It also prints which mods it dropped. Silently
dropping a mod is never the default, because a partially modded game is harder to diagnose than one that does not
start.

## 8. Saves

Native save files have no free space for the stack, and the port writes native layouts (`sys4-format-notes.md`).
The stack is therefore recorded beside each save:

- `SAVE/SAVEnn.mods.toml` beside `SAVEnn.DAT`, written in the same save transaction. It holds the resolved stack
  (id, version, package hash, in order), the selector allocation, and the link cache key.
- On load, the loader compares the stamp with the current stack. It reports added, removed, reordered and
  re-versioned mods and changed selectors. It **warns and never blocks** (roadmap §3.6): the stamp is evidence,
  not a compatibility verdict.
- A save without a stamp was made without mods, or by native AGE.

**What keeps saves working.** Transform plans keep the base's T1/T2/T3 entries at their indices (OMS §10.4, step 7),
so a save made before a patch resumes on the same page after it. Stable selectors (§6.2) keep saves made inside a
mod's script valid across load-order changes. Saving and loading inside a scene that an append catalog supplies is
still untested (OMS §13) and is gate 4 below.

## 9. Enabling mods

Version 1 has no in-engine mod manager. The engine reads:

- the profile's `mods/` folder in its data root (`user://games/<profile>/mods/`, or the `--data-root` sandbox):
  each `.omp` file and each folder with a `manifest.toml` is a candidate;
- `mods/load-order.txt`: one mod id per line, in the user's preferred order. A line `-ID` disables an installed
  mod. Mods missing from the file are enabled and ordered after the listed ones, by id;
- `--mod PATH` (repeatable): load this package in addition, for development. It is ordered after the folder's mods
  unless its manifest orders it otherwise;
- `--no-mods`: link nothing and mount the base only.

These files and flags are the interface a future launcher drives (§12).

## 10. Tools

`Age.Cli` gains an `omp` command group:

| Command | Effect |
|---|---|
| `omp build DIR [--output FILE.omp]` | validate the folder, compile every fragment and new script against the installed base, store plans beside fragments, and write a deterministic `.omp` (sorted entries, fixed timestamps) |
| `omp check [--mod PATH …]` | run the link pipeline for the profile's mods plus the given packages without starting the game, and print the report |
| `omp export --output-dir DIR [--mod PATH …]` | link, then write the result as native AGE files (§10.1) |
| `omp explain NAME` | show which layer and mod supplies a script or asset, and every plan applied to a script |

`oms-patch` and `oms-append` keep working on single files and gain `Resource` operands (§6.2).

### 10.1 Native export

`omp export` writes what native AGE loads: patched and replaced scripts as loose `.BIN` files, one
`APPENDnn.AAI`/`APPENDnn.ALF` pair per mod catalog, and asset overrides as loose files. It is refused when a mod's
target is not `native`. It writes only to the output directory. Installing the files into a game copy is the user's
decision; the project's own native tests use a `tools/native_scratch.py` copy, never the pristine install.

## 11. Untrusted input

Packages are untrusted:

- reject absolute paths, `..` segments, drive letters, and names that collide case-insensitively;
- bound each package to 65,536 entries, 1 GiB per expanded entry and 8 GiB expanded in total, and reject an entry
  whose compression ratio exceeds 200:1. These limits are provisional: they admit a full voice or CG replacement
  and stop a decompression bomb, and will be revisited once real packages exist;
- treat transform plans as untrusted: the loader revalidates every plan against its base (OMS §10.4, step 3);
- load no native code. Version 1 has nothing executable except AGE bytecode, which runs in the VM.

## 12. Future work

- **Launcher.** A separate launcher would centralize game manifests, mod management and launch parameters, and
  drive the engine through §9's files and flags. The engine keeps its command-line interface. To be designed
  separately.
- **Runtime scripting.** Lua hooks, semantic events and `maid.state` (roadmap §3.8).
- **Mod-owned sites.** Patches against code another mod inserted, through exported mod-owned site identities and a
  declared dependency (OMS §13).
- **Declarative data patches.** Named post-initialization edits to registry tables without a script (roadmap §3.2).
- **Mod merging.** A toolchain command that merges several mods into one package, so a large stack shares one
  selector, as Bethesda modders merge plugins to stay under that engine's plugin limit. It would combine the
  append catalogs, rewrite `Resource` references, and keep each source mod's saves loadable through a recorded
  mapping from its old packed ids to the merged ones.
- **Precompiled new scripts.** Shipping compiled scripts with relocation records for their selector, so new scripts
  need no compile at link time, as plans already avoid it.

## 13. Validation gates

| Gate | Passes when |
|---|---|
| 1. Packages | `.omp` and folder forms of one mod produce the same link; malformed manifests, unsafe paths and oversized entries are rejected with their reason |
| 2. Link | two example mods patching different sites of one Himegari scene compose; a third that overlaps them stops the load with a report naming all three; the same stack in a different user order links identically where dependencies fix the order |
| 3. Selectors | two mods with new scripts receive stable selectors; reordering, removing and re-adding them leaves each mod's selector unchanged; a `run` across mods resolves |
| 4. Saves | the port saves inside a scene an append catalog supplies and reloads it; a save made with one stack reports the difference when loaded under another; a pre-patch save resumes on its page after the patch |
| 5. Native export | the exported stack of gate 2–3's mods runs in native AGE in a scratch copy, with the same pages and the same save behavior as the port |
| 6. Kamidori | a fragment against the translated `patch/SC0000.bin` base links and runs on Kamidori in the port |

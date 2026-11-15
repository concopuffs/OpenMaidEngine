# Packages (Open Maid Pack)

An Open Maid Pack (OMP) is one mod. It comes in two forms that load identically:

- a **folder** containing `manifest.toml`, for development;
- a **`.omp` file**: a ZIP archive of the same tree, with `manifest.toml` at its root. `omp build` writes one
  (`cli.md`). ZIP entries may be stored or deflated; OMP adds no compression of its own.

## Layout

```text
<mod>/
├── manifest.toml
├── patches/        fragments (NAME.oms) and compiled plans (NAME.omspatch.json)
├── scripts/        new scripts (NAME.oms), one per [scripts].names entry
├── replace/        whole-script replacements (SCRIPT.BIN)
├── assets/         asset overrides by original name, and new assets; assets/<language>/ (format version 2; languages.md)
├── text/           translation catalogs, text/<language>/*.toml (format version 2; languages.md)
├── fonts/          font files a language uses (format version 2; languages.md)
├── lua/            Lua sources; init.lua is optional (format version 3; lua.md)
├── events/         game event declarations (NAME.toml), for a mod that declares events (format version 3; lua.md)
├── layout/         a game's base mod only: the names of its cells (format version 3)
├── README.md       optional
└── LICENSE         optional
```

Leave out folders you do not use. A file anywhere else is ignored and reported as a note, so a stray file never
changes behavior without a mention.

## Manifest

```toml
omp = 1                              # package format version: 1, 2 for languages, 3 for Lua (required)
id = "example.double-exp"            # required
name = "Double EXP"                  # required
version = "1.2.0"                    # required, MAJOR.MINOR.PATCH
authors = ["you"]
description = "Doubles experience awards."

[game]                               # required (a version 2 package without it is for every game)
profile = "<profile>"                # a profile id from games/README.md (required)
target = "native"                    # "native", or "extended" for a mod with Lua (required)
revisions = ["sha256:…"]             # SYS4INI.BIN fingerprints this mod supports

[engine]
oms = 1                              # OMS language version of the sources

[[requires]]                         # repeatable
id = "example.library"
version = ">=1.0, <2"

[[conflicts]]                        # repeatable
id = "example.half-exp"
reason = "both rescale the same award"

[order]
after = ["example.ui-base"]          # load-order hints
before = []

[scripts]
names = ["DBLEXP", "DBLEXP_UI"]      # new scripts, in append catalog record order

[assets]
names = ["DBLEXP_ICON.AGF"]          # new assets, as records after the scripts

[replace]
"SC0010.BIN" = { over = "other.mod" } # this replacement deliberately supersedes other.mod's

[[language]]                         # format version 2 and later; repeatable (languages.md)
tag = "en"

[lua]                                # format version 3; lua.md
api = 1
capabilities = ["vm"]

[[hooks]]                            # format version 3; repeatable (lua.md)
point = "<a patch point>"
handler = "double_exp.lua:on_award"
```

Unknown keys are errors, so a misspelled setting cannot be silently ignored.

**Format versions.** `omp = 2` adds the `[[language]]` table, the `text/` and `fonts/` folders and language assets
in `assets/<language>/`, and lets a package leave out `[game]` (below); everything else is as in version 1, which a
version 2 loader still reads. A version 1 package with any of these, or without `[game]`, is an error.
`omp = 3` adds the `[lua]` table, `[[hooks]]`, the `lua/` folder and the `extended` target ([lua.md](lua.md)); a
version 3 loader still reads versions 1 and 2. A package with Lua and an earlier version, or with Lua and
`target = "native"`, is an error.

### Identity

- **`id`:** lowercase letters and digits in dot-separated parts, each part optionally hyphenated, at most 64
  characters (`my.first-line`, `example.sc0000-page3`). The id identifies the mod everywhere: load order,
  selector allocation, save stamps and the link report. Changing it makes it a different mod.
- **`version`:** semantic version `MAJOR.MINOR.PATCH` (semver 2.0, so `1.0.0-beta.1` is allowed). Dependents
  match it with a range: comma-separated comparators that must all hold, each `>=`, `>`, `<=`, `<`, `=` or a bare
  version, such as `">=1.0, <2"`. Missing minor and patch numbers count as zero, except in a bare or `=` version:
  `1.2` accepts every 1.2.x release and `1` every 1.x.y release, while `1.2.0` accepts only 1.2.0. `<2` rejects
  2.0.0's prereleases (2.0.0-rc.1) as well as 2.0.0. `*` or an empty range accepts any version.

### `[game]`

- **`profile`:** the game. A mod targets exactly one game. A version 2 package without `[game]` targets every game
  and holds only the engine's own text in other languages: `[[language]]` tables, catalogs with `[[engine]]` entries
  alone, and `fonts/` ([languages.md](languages.md), "A shell language for every game").
- **`target`:** `native`: the mod uses only what the original engine supports, so it can be exported for
  `AGE.EXE`. `extended` (format version 3): the mod has Lua, which only the port runs, so `omp export` refuses it.
- **`revisions`:** optional SHA-256 fingerprints of `SYS4INI.BIN`. When present, any other installed catalog stops
  the load and prints the installed fingerprint (`omp check` prints it too). Patches do not need it, because each
  fragment carries its own script fingerprint. Declare it when the mod has replacements, new scripts or data edits
  that assume a particular game version, since nothing else detects a mismatch for them.

### Dependencies and conflicts

- **`[[requires]]`** is a hard dependency. A missing mod or a version outside the range stops the load. A required
  mod always loads first, and only a required mod's scripts and assets can be referenced (`run other.mod:NAME`).
- **`[[conflicts]]`** declares mods that must not load together. If both are enabled, the load stops with your
  `reason`.
- **`[order]`** lists soft hints. `after` and `before` place this mod relative to others *when they are present*,
  and are ignored otherwise.

### Load order

The loader orders the enabled mods in three steps:

1. Dependencies come before the mods that require them.
2. `[order]` hints are applied.
3. Remaining ties follow the player's `mods/load-order.txt`, then mod id.

A cycle stops the load and lists it. Load order decides which mod wins an override and in what order insertions at
one site appear (`patching.md`).

### `[scripts]` and `[assets]`

These lists fix the order of the mod's **append catalog** records:

- the records are the scripts in `[scripts].names` order, then the new assets in `[assets].names` order;
- record 0, the first script, runs at boot ("Record 0 runs at boot" below), so list a short boot script first;
- a file in `scripts/` that the list does not name is an error, and so is a new asset that `[assets].names` does
  not name;
- script names are ASCII letters, digits and `_`, at most 48 characters. Asset names are ASCII basenames with an
  extension.

Keep the order stable across versions. Saves store scripts by record number, so reordering the list breaks saves
made inside the mod's scripts.

### `[replace]`

Only needed when this mod's `replace/` file must supersede another mod's replacement of the same script. Without it,
two replacements of one script are a hard conflict. An `over` entry also orders the named mod before this one.

## Content

### Patches: `patches/`

A patch is a fragment `NAME.oms`, a compiled plan `NAME.omspatch.json`, or both with the same stem
(`patching.md`).

- **Source only:** the loader compiles it when the game starts and notes that it did.
- **Both** (what `omp build` produces): the loader uses the stored plan when its fingerprint matches the base, and
  recompiles the source otherwise.
- **Plan only:** a fingerprint mismatch stops the load.

Ship the source as well as the plan, so players can read and rebuild the mod.

### New scripts: `scripts/`

One OMS script unit per file, named like its `[scripts].names` entry (`scripts/DBLEXP.oms` holds
`script "DBLEXP"`). New scripts are compiled into the mod's append catalog. See `new-scenes.md`.

### Record 0 runs at boot

The first name in `[scripts].names` is record 0 of the mod's append catalog. When the engine starts (INIT2, after the
game's data tables are built), it runs record 0 of every mounted catalog once, in selector order, before the title
screen. Record 0 is the mod's entry point, for a data edit at boot for example (`new-scenes.md`, "Data edits at
boot"), and whatever script is listed first runs then:

- **Make record 0 a short script that ends with `exit`.** A record 0 that shows text or waits for the player hangs
  native AGE at boot. `omp build`, `omp check` and the port's link report warn when record 0 itself does either (a
  `note` that starts `warning: record 0 runs at boot`). They do not follow a `run` from record 0 into another script.
- **Do not list a script meant to be called first.** A scene, a script a patch runs, or one that Lua calls
  ([lua.md](lua.md), "Calling a script") would also run once at boot. Put a small boot script before it:

  ```toml
  [scripts]
  names = ["AUTO", "SCENE"]                # AUTO is record 0
  ```

- A mod that ships new assets but no scripts gets an empty record 0 generated for it.

### Replacements: `replace/`

`replace/SC0010.BIN` replaces that compiled script wholesale. Prefer patches: a replacement cannot combine with any
other mod's edits to the script. Any patch to a replaced script, from any mod, must target the replacement's
fingerprint. A fragment written for the original then fails with an error that names the replacing mod, instead of
landing on the wrong instructions.

### Assets: `assets/`

- A file named like an original asset (`assets/BG001.AGF`) overrides it, matched case-insensitively. When several
  mods override one asset, the last in load order wins, and the link report says so. The records of the game's own
  append catalogs count as original assets under their `$n$` names (`assets/$1$BG001.AGF`).
- A file with a name the game does not have is a **new asset**. List it in `[assets].names`. It becomes a record of
  the mod's append catalog, and scripts reach it with `asset "NAME"` (`new-scenes.md`).
- `assets/<language>/NAME` (format version 2) is a **language asset**: it replaces `NAME` only while that language is
  selected, in the port only. A `.png` there stands for the `.AGF` of the same name. Plain `assets/` holds no PNG,
  because native AGE reads only AGF. See [languages.md](languages.md), "Images and other assets".

### Text and fonts: `text/`, `fonts/`

`text/<language>/` holds translation catalogs and `fonts/` the font files a `[[language]]` table names. They change
no script or asset: the port presents the game's text through them as it is drawn. A package that holds nothing
else, or language assets besides, is presentation only, which its save stamps record. See [languages.md](languages.md).

### Lua: `lua/`

`lua/` holds the mod's Lua sources (`*.lua` only), which `[lua]` and `[[hooks]]` use. A package with `lua/` needs
a `[lua]` table, and it is never presentation only: Lua can change what the game stores. `events/` holds the game
events the mod declares (`*.toml`, no subfolders), and takes a `[lua]` table and a `[game]` too. See
[lua.md](lua.md), "Game events".

### Base mods: `layout/`

The engine ships one base mod per game, `<profile>-base`, which names the game's cells for Lua mods. Its
`layout/globals.json` gives each named global its bank, address and shape; `layout/points.json` copies the game's
patch points, which the link uses when the profile has none of its own; `layout/exports.txt` lists the names it
exports, which stay exported until its major version is raised. Its `events/` declares the game's events, and its
`lua/init.lua` returns its helpers, with the game scripts they call under `[lua] calls`. A mod that names cells requires the base mod (`[[requires]] id = "<profile>-base"`). The engine mounts it
only then, and uses an installed package of the same id instead when there is one. The files are written by the
project's `tools/base_mod_build.py`; a mod has no reason to carry `layout/`, and a package that does needs omp 3,
its own game's `[game]` and `[lua] capabilities = ["vm"]`. How a Lua mod reads and writes names through a base mod
is in [lua.md](lua.md), "Game names".

## Limits

Packages are treated as untrusted input:

- absolute paths, `..` segments, drive letters, and names that collide case-insensitively are rejected;
- a package may have at most 65,536 entries, 1 GiB per expanded entry and 8 GiB in total;
- an entry that expands past 1 MiB at a ratio over 200:1 is rejected;
- compiled plans are revalidated against the base on every load;
- catalog files are at most 32 MiB each, and font files 64 MiB;
- a package carries at most 512 Lua files, each at most 1 MiB;
- a package contains no native code. It runs AGE bytecode inside the VM, and Lua source inside the engine's sandbox
  ([lua.md](lua.md)): no bytecode, no native modules, and no access to files, processes or the network.

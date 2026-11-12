# Commands and installing mods

This page is the reference for the mod commands, the port's mod options, and where installed mods live. Run commands
from `age-reimpl/`.

`Ome.Cli` is `dotnet run --project engine/Ome.Cli -- <command>`. Every `Ome.Cli` command takes the game options:

| Option | Meaning |
|---|---|
| `--profile <profile>` | the game; without it, `himegari` |
| `--game-root DIR` | the install; without it, the game's expected install beside the repository |
| `--overlay-root DIR` | a loose-file layer over the install, such as a translation, relative to the game root (repeatable) |

Your game guide lists the options its installs need ([games/README.md](games/README.md)).

## `omp`: packages

| Command | What it does |
|---|---|
| `omp check [--mod PATH …] [--data-root DIR] [--skip-failed-mods]` | Links the mods and prints the link report, each mod catalog's records with their packed ids, and a summary. It writes nothing. It exits non-zero if the stack does not link. |
| `omp build DIR [--output FILE.omp] [--mod DEP …]` | Validates and links the mod folder `DIR`, compiles every fragment, and writes a reproducible `.omp` with the plans stored inside (default `<id>-<version>.omp` beside the folder). The folder is not changed. Pass the mod's dependencies with `--mod`. |
| `omp export --output-dir DIR [--mod PATH …] [--data-root DIR]` | Links the mods and writes what native AGE loads: patched and replaced scripts and asset overrides as loose files, and one `APPENDnn.AAI`/`.ALF` pair per mod catalog. It refuses a stack that does not link. It leaves out `text/`, `fonts/`, language assets (`assets/<language>/`) and `[[language]]` with a note, since native AGE cannot present them. |
| `omp explain NAME [--mod PATH …] [--data-root DIR]` | Says which layer and mod supplies a script or asset, such as `SC0000.BIN`, `$2$SCENE.BIN` or `my.mod:NAME`, and lists every plan applied to a script. A base entry shows its loose file or its range in the ALF archive. An asset with language versions (`assets/<language>/`) also lists each one, the language it shows in, and whether a later package's plain asset hides it. |

`--mod` takes a folder or an `.omp` file and repeats. `--data-root DIR` also links the mods installed in a port
profile root (see below), with its load order and kept selectors. Without it only the `--mod` packages link, and
selectors are allocated fresh.

**Exports and saves.** A mod catalog's selector is part of every packed id in a save. To export a stack whose
native saves must match saves made in the port, pass the port's profile root as `--data-root`, so the export uses
the same selectors.

## `omp text`: translations

| Command | What it does |
|---|---|
| `omp text extract --language TAG --output DIR` | Writes catalog templates for language `TAG` into the package folder `DIR` (`DIR/text/TAG/`), from the install's scripts: one file per script with its messages and patterns, and `_global.toml` with every literal the scripts draw or store. Run again, it keeps every entry already there, adds templates for new sources, marks `obsolete = true` the entries whose source is gone, and rewrites only the files that changed. For a language the engine has no table of its own for (it has `ja` and `en`), it also adds to `_engine.toml` an `[[engine]]` template for each engine key no file there sets ([languages.md](languages.md), "Engine text"). A file there that does not read stops it before it writes anything. |
| `omp text check [--mod PATH …] [--data-root DIR] [--text-root DIR] [--language TAG] [--no-scripts]` | Links the packages as `omp check` does (printing its report), then validates every language's catalogs, the `--text-root` folder's last. Errors: a file that does not read, two patterns that can match one string, too many entries, a stack that does not link, an `[[engine]]` entry whose text names a value its key does not take or does not format. Warnings: entries whose source the installed scripts do not draw or store where their scope reaches, site entries that do not bind, characters no bundled font covers, `[[engine]]` keys the engine does not have. `--language` checks one language; `--no-scripts` skips reading the install. Exits non-zero on an error. |
| `omp text stats [--mod PATH …] [--data-root DIR] [--text-root DIR] [--language TAG] [--scripts]` | Counts, per language, the messages, strings and composed strings of the installed scripts that its catalogs and their fallback chain translate. `--scripts` adds a line per script. |
| `omp text po-export --language TAG (--package DIR \| --text-root DIR) --output FILE.po` | Writes language `TAG`'s catalogs (`DIR/text/TAG/` of a package folder, or `DIR/TAG/` of a text root) as one gettext PO file: a message per entry with its scope as `msgctxt`, its `note` and origin as comments, templates with an empty `msgstr` and obsolete entries as `#~` messages. `[[engine]]` entries have the context `engine:KEY` and the engine's Japanese as `msgid`. |
| `omp text po-import --language TAG (--package DIR \| --text-root DIR) --input FILE.po` | Puts the PO file's translations into those catalogs, entry by entry, keeping every other key and each file's header comments, and rewrites only the files that changed. Empty, fuzzy and obsolete messages are left out, and a message no entry has is a warning. A translation that does not fit its entry stops it before it writes anything. |
| `omp text import-scripts --from GAME_ROOT [--overlay-root DIR] [--allow-bmp-as-agf] --output DIR [--language TAG] [--package] [--verify] [--compare-archives]` | Builds language `TAG`'s catalogs (default `en`) in `DIR/text/TAG/` from the install at `GAME_ROOT`, whose scripts were translated in place, against the selected install. It replaces the `.toml` files already in that folder and writes `DIR/import-report.json`. `--overlay-root` and `--allow-bmp-as-agf` apply to the translated install. `--package` also writes the images the translation replaced to `DIR/assets/TAG/`, each AGF as a PNG and replacing the images already there. When `DIR` has no `manifest.toml`, it writes one with a `[[language]]` table. `--verify` checks every message against the translated scripts and exits 1 on a mismatch. `--compare-archives` compares archives by content rather than size ([languages.md](languages.md), "From a translation made in place"). |

The catalog format and the workflow are in [languages.md](languages.md).

## OMS tools

| Command | What it does |
|---|---|
| `oms-patch --fingerprint SCRIPT.BIN` | Prints the `patch "…" fingerprint "…"` header for a script as installed. Use the full file name. |
| `oms-decompile --script SCRIPT.BIN [--level idiomatic\|raw] [--output FILE.oms]` | Prints a script as OMS. `idiomatic` names everything the modules know, which is what you want for reading. |
| `oms-patch --source F.oms … [--output-dir DIR]` | Compiles fragments outside a mod, and optionally writes the patched scripts to `DIR`. |
| `oms-compile --source F.oms [--output F.BIN]` | Compiles one script unit to a `.BIN`. |

The full list, including the validation tools, is in `../tools-reference.md`.

## Running mods in the port

| Option | Meaning |
|---|---|
| `--mod PATH` | Also load this mod (folder or `.omp`). Repeatable. Loaded after the installed mods, unless its manifest orders it otherwise. |
| `--no-mods` | Load no mods at all, not even installed ones. |
| `--skip-failed-mods` | If the stack does not link, drop the mods involved, say so, and start with the rest. Without it a failed link stops startup (exit code 2). |
| `--forget-mod ID` | Release a removed mod's selector (see below). Repeatable. |
| `--data-root DIR` | Use `DIR` as the profile root instead of the default, for a separate set of mods and saves. |
| `--language TAG` | Present the game's text in `TAG` for this run, whatever `OME.INI` says: the catalogs the mods (and `--text-root`) hold for it. `ja` draws the scripts' text as written; `x-pseudo` brackets and lengthens every string, with no catalog. The menus and dialogs take the mods' `[[engine]]` entries for `TAG`, then the engine's own table for it, along its fallback chain, then Japanese ([languages.md](languages.md), "Engine text"). |
| `--text-root DIR` | Also load a loose folder of catalogs laid out as a package's `text/`, after the mods' (languages.md, "Testing without a package"). |
| `--text-log` | Write every string drawn without a catalog entry to `diagnostics/text-misses.jsonl`, every translated message narrowed to fit its window, with `width_percent`, the width it got, and every one cut at the bottom of its window, with `rows_cut`, the rows it lost. |

`run-godot.ps1` (or `run-godot.cmd`) passes these through as `-Mod PATH` (repeatable) and `-NoMods`. Combine them
with the game switches:

```text
.\run-godot.cmd <game switches> -Mod path\to\my.mod
```

The game switches (such as `-Profile`, `-GameRoot`, `-OverlayRoot`, or a game's shortcut) are in your game guide.

At startup the console prints `[mods]` lines with the load order and every file the mods supplied. The full report
is `diagnostics/mod-link.txt` in the profile root.

## Installing mods in the port

Each game has a **profile root**:

```text
%APPDATA%\Godot\app_userdata\Open Maid Engine\games\<profile>\
```

`<profile>` is the game's profile id. `--data-root` replaces this folder. Inside it:

| Path | Contents |
|---|---|
| `mods/` | Installed mods: each `.omp` file and each folder with a `manifest.toml`. |
| `mods/load-order.txt` | Optional. One mod id per line, in your preferred order. A line `-ID` disables an installed mod without removing it. Mods not listed load after the listed ones, by id. |
| `mods/selectors.toml` | Written by the port: the selector each mod with new scripts or assets was given. Do not edit it. |
| `mods/.cache/` | The linked results, each reused while nothing changes. A link keeps the 8 most recent and removes older ones, but none younger than an hour. Safe to delete at any time. |
| `SAVE/` | Saves. With mods loaded, each `SAVEnn.DAT` gets a `SAVEnn.mods.toml` stamp beside it. |
| `diagnostics/` | `mod-link.txt` (the last link report) and `mod-save-stamps.txt` (save-stamp warnings). |

To install a mod, copy its `.omp` (or folder) into `mods/`. To remove it, delete it. Its selector stays reserved,
so reinstalling it later restores saves made inside its scripts. When you are sure it is gone for good, start once
with `--forget-mod ID` to release the selector.

**Save stamps.** When you load a save, the port compares the save's stamp with the current mods. It reports added,
removed, reordered and re-versioned mods and changed selectors, on the console and in
`diagnostics/mod-save-stamps.txt`. It warns and never blocks. A save without a stamp was made without mods, or by
native AGE.

## Running mods in native AGE

Native AGE has no mod loader. Export the stack and install the files into a **copy** of the game:

```text
Ome.Cli omp export --output-dir build/my-export --mod path\to\my.mod <game options>
py -3.11 -X utf8 tools/native_scratch.py --profile <profile> --replace --overlay build/my-export/SC0000.BIN --overlay build/my-export/APPENDnn.AAI --overlay build/my-export/APPENDnn.ALF
```

`native_scratch.py` builds a copy at `build/games/<profile>/native-scratch/` with the listed files added. Pass one
`--overlay` per file the export wrote; `nn` is the mod catalog's selector. Run its
`AGE.EXE` directly. Native AGE reads loose `.BIN` files before its archives, and mounts every `APPENDnn` catalog
beside it. Native AGE does not check versions, conflicts or save stamps; the export already resolved those.

## Automated testing

`tools/drive.py` drives the port and native AGE with one step language, and takes the mod in its `start` command:

```text
py -3.11 -X utf8 tools/drive.py --profile <profile> port start --mod path\to\my.mod
```

Extra port options go through `--port-arg=…`, one per argument; your game guide has the full command for its
installs. Each start uses an empty save sandbox. See `../tools-reference.md`, "Game driver", for the steps and the
native side. `tools/drive/scenarios/*-mod-*.steps` are working examples.

`language TAG` switches the port's language as the settings panel's OME section does, and fails for a language no
installed package defines (other than `ja` and `en`). Native AGE has no language, so a scenario that uses it runs on
the port only; `tools/drive/scenarios/port/` holds those.

## Outside the repository

Fragments and new scripts compile against the repository's OMS modules (`oms/<profile>/`) and generated registries
(`build/games/<profile>/`). An exported port build without the repository has none of them. It can still load:

- asset-only mods;
- whole-script replacements;
- fragments whose stored plan `omp build` compiled, provided the plan's fingerprint matches.

New scripts always compile from source, so they need the repository. Ship built `.omp` files for players. Packaging
the toolchain with exported builds is in `../backlog.md`.

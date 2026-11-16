# Launcher — design

**Date:** 2026-11-15
**Status:** accepted by the user on 2026-11-15, after two reviews the same day (§15.3 lists what they changed).
Not built yet: §14 has the milestones. The points of §15.1 are the user's own decisions; the rest is this spec's
design, accepted with it.
**Home in the canonical map:** this file is the launcher design. Packages, linking, selectors and save stamps stay
in `2026-11-03-mod-loader-design.md` ("mod loader §n"). `OME.INI`, the options panel and the shell's option model stay
in `2026-11-09-engine-shell-design.md` ("shell §n"). Languages stay in `2026-11-10-localization-design.md`
("localization §n"), Lua and mod state in `2026-11-10-lua-scripting-design.md` ("Lua §n"). The engine's flags stay in
`docs/tools-reference.md`, release packaging in `docs/platform-portability.md`. This spec cites those homes and does
not restate them.

## 1. Purpose and scope

Today a player needs a command line to play: `--game-root`, `--profile`, `--overlay-root`, `--allow-bmp-as-agf`,
`--language`. Installing a mod means copying it into a folder under `%APPDATA%\Godot\app_userdata\…` and editing
`load-order.txt` by hand, and a failed link is reported in a text file under that folder
(`diagnostics/mod-link.txt`) that a player has to know to look for. The mod loader left "enabling, ordering and
configuring mods" to a launcher and kept a file-and-flag interface for it (mod loader §1, §9, §12). This spec
designs that launcher.

It has two halves:

- **A profile the engine can run.** One folder describes one way of playing one game: which install, which mods,
  where the saves are, how to start. The engine reads it by itself (§5).
- **An application that edits profiles and starts the engine** (§8).

**Version 1 does:**

| Area | What |
|---|---|
| Profiles | create (wizard), duplicate, rename, delete; several per game; a profile may live in any folder |
| Games | recognize an install by its `SYS4INI.BIN`; say which games this engine supports |
| Mods | a shared library per game; per profile: enable, disable, order; the link report shown before the game starts |
| Settings | the engine's startup options, the port's options (`OME.INI`), the screen mode |
| Launch | checks before start, starting the engine, showing a start that failed, a command that starts a profile without the window |

**Not in version 1:**

- a mod browser, mod downloads or an update check. The launcher's one use of the network is fetching a game's
  art, and only when the player allows it (§8.7, §12);
- free engine arguments or environment variables stored in a profile (§12);
- a save folder apart from the profile's own folder (§15.1, point 11);
- moving a profile from inside the launcher, and backing one up to a zip. A profile is a folder: a player moves
  or copies it and adds it again (§8.4);
- mod authoring. The devkit is its own backlog item (`docs/backlog.md`, "A devkit for mod authors"); it may reuse
  the launcher's model (§8.1) and shell;
- changing a game install in any way, or starting native AGE;
- several engine versions side by side. One archive holds one launcher and one engine, at one version;
- a save editor or save viewer. Version 1 copies save folders, and reads nothing inside a save;
- controller navigation. A profile can be started without the window instead (§8.6).

## 2. The shape, in six rules

1. **A separate application.** The launcher is an Avalonia program beside the engine, not a mode of the Godot
   project. The two share libraries, never UI code.
2. **A profile is a folder the engine runs by itself.** `OME -- --data-root <profile root>` starts it with no
   other engine flag (§5.1, §5.2). The launcher has no private channel into the engine: everything it does is a
   file the engine reads or a flag the engine documents. A shortcut, a script and the driver can therefore start
   the same profile.
3. **The engine works without the launcher, as it does today.** Dropped into a game folder, or started with flags,
   it behaves as before. No engine feature may need the launcher.
4. **One fact, one file.** Each setting has one file (§4.4). The launcher edits the engine's own files through the
   engine's own readers and writers; it keeps no second copy to sync.
5. **The launcher shows what the engine will do, with the engine's code and the engine's data.** The link report
   on the Mods page is the linker's own, run in the launcher's process from the same tables the engine links from
   (§5.9, §8.5). The launcher does not reimplement a rule.
6. **The launcher never writes into a game install or a package, and never runs a mod's code.**

## 3. Terms

- **Game:** a game the engine supports, by its manifest (`engine/Ome.Engine/Profiles/<id>.json`). The other docs
  call its id `<profile>`, and the engine's option is `--profile`. From this spec on it is the game, and the option
  is `--game` (§5.8).
- **Install:** a folder that holds one copy of a game (`SYS4INI.BIN`, the archives). A player may have several of
  one game.
- **Profile:** what the player makes in the launcher: one install, one set of enabled mods, one set of saves and
  settings, under a name.
- **Profile root:** the profile's folder. It is the engine's data root (`--data-root`), which the modding docs
  already call the profile root (`docs/modding/cli.md`). Without a launcher each game has one, under Godot's
  `user://games/<game>/`.
- **OME home:** the folder that holds the launcher's settings, the profiles and the library (§4.1).
- **Library:** the folders packages are found in (§7). A profile enables packages from it.
- **Launcher:** this application. Older text uses the word for the `run-godot` scripts and for parts of the
  engine's own startup ("runtime launcher", "scene launcher"). Those keep their qualified names; "the launcher"
  alone means this application.

## 4. On disk

### 4.1 OME home

```text
<OME home>/
├── launcher.toml              the launcher's own settings (§8.8)
├── profiles/
│   └── <profile id>/          one profile root each (§4.2)
├── mods/
│   ├── <game>/                packages for one game
│   └── common/                packages for every game (localization §11.1)
├── art/
│   └── <game>/                the player's own art for a game (§8.7)
└── cache/
    └── art/<game>/            the art the launcher downloaded (§8.7); safe to delete
```

- **Where.** `%APPDATA%\OpenMaidEngine` on Windows, `$XDG_DATA_HOME/OpenMaidEngine` on Linux,
  `~/Library/Application Support/OpenMaidEngine` on macOS.
- **Portable.** When a folder `data/` sits beside the launcher, that folder is OME home. The archive ships without
  it, so a player makes it to opt in. Portable covers OME's own files. The Godot runtime inside the engine still
  keeps its files (its shader cache, for one) under Godot's `user://`; M0 lists them.
- **Profiles elsewhere.** A profile root may be anywhere. `launcher.toml` lists the roots outside `profiles/`.
  This is how a player chooses where a profile's saves are.
- **Profile id.** The folder's name: lowercase letters, digits and `-`. It never changes with the profile's name.
  Ids are unique among the roots the launcher knows. Adding a root whose folder name is taken asks for another
  id, which `launcher.toml` records for that root.

### 4.2 Profile root

```text
<profile root>/
├── profile.toml               new: what this profile is (§4.3). The launcher writes it; the engine only reads it
├── OME.INI                    the port's options (shell §5.6)
├── SYS4REG.INI                the game's settings registry (docs/sys4-format-notes.md)
├── SAVE/                      saves, with their stamps and mod state
├── mods/                      the engine's own: selectors.toml, .cache/ (mod loader §6.2, §6.4)
├── diagnostics/               link report, logs
└── run.lock                   locked while the engine runs this profile; its time is "last played" (§5.5)
```

- **Everything but `profile.toml` and `run.lock` exists today under `--data-root`.**
- **`OME.INI` per profile is a deliberate change.** The bare engine keeps one `OME.INI` for all games, in
  `user://` (shell §5.6). Under `--data-root` it already reads the one beside the saves, so a profile gets its own
  with no engine change.
- **A profile is the unit of backup and of removal:** copy the folder without `run.lock`, delete the folder.

### 4.3 `profile.toml`

```toml
ome_profile = 1                      # format version
name = "Kamidori (English)"
game = "kamidori"                    # the game manifest's id
mode = "native"                      # "native" or "extended" (§6)

[install]
root = "D:/Games/Kamidori_Translated"
fingerprint = "sha256:…"             # SYS4INI.BIN when the profile last accepted the install
overlays = ["patch"]                 # --overlay-root, in order
allow_bmp_as_agf = true

[mods]
folders = ["../../mods/kamidori", "../../mods/common"]
order = ["example.kamidori-prologue-line", "-example.kamidori-double-exp-event"]
on_failure = "stop"                  # or "skip": --skip-failed-mods

[launch]                             # options the engine reads at startup
renderer = "gpu"                     # --render-backend
text_backend = "auto"                # --text-backend
lua_errors = "continue"              # --lua-errors

[launcher]                           # the launcher's own; the engine does not read this table
cover = "cover.png"                  # this profile's own cover (§8.7)
graphics_api = "default"             # "d3d12": start Godot with --rendering-driver d3d12 (§8.5)
notes = ""
```

- **A fixed set of typed keys.** The file holds no engine arguments and no environment variables (§12).
- **A network path is used once this machine's player approved it.** A path that starts with `\\` (a Windows
  network share) works like any folder; the operating system does the rest. It is only opened when the player
  has approved that location on this machine (§12).
- **Paths** are absolute, or relative to the profile root (`[install] overlays`: to the install root, as
  `--overlay-root` is). The launcher writes a relative path when both ends are inside OME home, so a portable OME
  home keeps working on another drive letter.
- **Strict.** Outside `[launcher]`, an unknown key is an error, as in a package manifest (mod loader §3.3). A
  format version the engine does not know stops startup and says which version it reads.
- **`[install] fingerprint`** is the value a package's `[game] revisions` names (mod loader §3.3). It only detects
  that the catalog changed. A changed loose file is not seen here; the link fingerprints every script it touches, so
  correctness never rests on this value.
- **`[mods] order`** lists the enabled packages in the player's preferred order. `-ID` marks a package as disabled
  and keeps its place in the list. A package that is not listed is not loaded (§5.3).

### 4.4 Which file holds which setting

| Setting | File | Who writes it |
|---|---|---|
| Game, install, overlays, mode, mod folders and order, startup options | `profile.toml` | the launcher |
| Language, window size, FPS counter, locator HUD | `OME.INI` | the options panel and the launcher |
| Screen mode, volumes, message speed and the other native keys | `SYS4REG.INI` | the game, the options panel; the launcher for the screen mode only (§8.4) |
| Selectors, link cache, save stamps, mod state | `mods/selectors.toml`, `mods/.cache/`, `SAVE/*.mods.toml`, `SAVE/*.modstate.json` | the engine; the launcher when the player releases a selector or copies saves in (§8.4) |
| Launcher language, window, what to do at launch, profile roots elsewhere | `launcher.toml` | the launcher |

A new profile's `OME.INI` starts from the launcher's defaults. Nothing is inherited at run time: the file in the
profile root is what the engine reads.

The launcher writes a profile's files only while it holds the profile's lock (§5.5).

## 5. Engine changes

Each is useful without the launcher.

### 5.1 The command line

The engine reads only the arguments after Godot's `--` separator (`OS.GetCmdlineUserArgs`). So every command in
this spec has the shape:

```text
OME [Godot's flags] -- [the engine's flags]
```

- **`OME --data-root R` does nothing useful:** the engine sees no argument. The form is `OME -- --data-root R`,
  as every documented run of the exported engine already is (`OME --headless -- --package-smoke`).
- **Godot's own flags** go before `--`. The launcher uses one, `--rendering-driver` (§8.5). Godot reads its
  flags before managed code runs, so no file the engine reads can carry them. Only a command line can.

### 5.2 Reading a profile

- **`--data-root DIR` reads `DIR/profile.toml` when it exists.** The file supplies the game, the install, the
  overlays, the mode, the mod folders and order, and the startup options. No new flag is needed.
- **One resolver.** A shared resolver in `Ome.Mods` turns the command line and the profile file into the run's
  options. The Godot host and every `Ome.Cli` command that takes `--data-root` use it. Today the two parse
  separately and disagree on a repeated option: the host takes the last value, `Ome.Cli` the first.
- **A flag and the file.** With a profile file:

  | Flag | Effect |
  |---|---|
  | `--game` / `--profile`, `--game-root`, `--overlay-root`, `--allow-bmp-as-agf` | refused: startup stops and says the profile names the game and the install. A profile's saves and selectors belong to one game and one install |
  | `--mod PATH` | adds a package for this run, after the profile's, as today. The mode applies to it (§5.4) |
  | `--probe` | refused: it needs a game named on the command line. Probing is for a data root without a profile file |
  | `--no-mods` | runs the base game for this run |
  | `--skip-failed-mods` | acts as `on_failure = "skip"` for this run |
  | `--render-backend`, `--text-backend`, `--lua-errors`, `--language`, `--window-width` / `--window-height` | the flag wins for this run and is written nowhere (shell §5.6) |
  | a switch that only turns something on (`--fps-counter`, `--locator-hud`, `--windowed`) | turns it on for this run, as today. No "off" form is added |
  | any other flag | as today |

- **The identity check is strict for a profile file.** The engine reads the install's `SYS4INI.BIN` header, and a
  `game` it does not match stops startup. Without a profile file, a game named on the command line keeps today's
  behaviour: a mismatch continues read-only with its diagnostic (`GameProfileSelection`).
- **`[install] fingerprint`** that differs from the install prints a warning and continues.
- **The startup log** gains a `[profile-file]` line with the file and the values in effect. The `[profile]`
  prefix already labels the game's own lines.
- **The root is checked before anything is opened or written:** a network path the player has not approved, or
  a link inside the root, stops startup (§12). `--allow-network-paths` approves the file's network paths for one
  run.
- **A data root without `profile.toml`** behaves as today. The driver's sandboxes are that case.

### 5.3 Mods from a profile

With a `[mods]` table:

- **Discovery is an index, then the enabled packages.** The engine reads the manifest of each `.omp`, and of each
  folder with a `manifest.toml`, in `folders`. It opens and hashes only the packages `order` enables. Today
  discovery opens and hashes every candidate, which a shared library of voice and image packs would pay at each
  start.
- **A package the profile does not enable cannot stop it.** A candidate whose manifest cannot be read is a note
  in the report, by its file name. Two profiles share a folder, so one broken file must not stop both.
- **Folders.** Their order has no meaning. Paths are normalized, so one folder listed twice counts once. A
  candidate for another game is ignored unless it is enabled.
- **Enabling is explicit.** Only the ids in `order` load. This is the difference from `load-order.txt`, where a
  package missing from the file is enabled: a library shared by several profiles must not switch a package on in
  all of them.
- **Errors, raised at discovery and before the link:**
  - an enabled id found in no folder;
  - an enabled id found twice, in two folders or as two versions in one;
  - an enabled package whose hard dependency is in a folder but not enabled. The message names it. The launcher
    enables dependencies when the player enables a package (§8.4), so the engine can stay strict.
- **The engine's own packages are not listed.** A game's base mod is inside the engine and joins the stack when
  an enabled package requires it (Lua §9.1). It satisfies that requirement whatever the folders hold. A package
  in a folder with a base mod's id is used instead of the engine's only when `order` enables it. A base mod's id
  in `order` with no such package in a folder is a note, and changes nothing.
- **`mods/load-order.txt` is not read**, and one that exists gets a note in the link report.
- **The engine's own state stays in the profile root:** `mods/selectors.toml` and `mods/.cache/`. Two profiles
  that share a library still have their own selectors, as their saves need (mod loader §6.2).
- **`--mod`, `--no-mods`, `--skip-failed-mods` and `--forget-mod`** keep their meaning.
- **The cache key of a link that dropped failed mods** covers the enabled packages, the `--mod` packages, the
  order, the selectors and the mode. Today it covers every installed package with its content hash, which would
  hash the whole library.
- **A link cache is used only on the machine that wrote it.** The cache key gains a random value that the engine
  makes once and keeps outside every data root, in Godot's `user://`, where the list of approved network
  locations is too (§12). Opening a cache entry checks only that its files exist, and anyone can compute the
  rest of the key. Without the value, a data root from someone else could carry an entry that the engine mounts
  without linking, whatever mods the root seems to enable.
- **Every reader of a data root's mods follows the table:** the Godot host's early "no mods" test, and
  `omp check`, `export`, `explain`, `lua-stubs` and `text`.

### 5.4 Mode

- **`mode = "native"` refuses** an enabled package, or a `--mod` package, that:
  - targets `extended`;
  - ships Lua. A Lua library has no `[game]` table and so no target, and the target alone would let it through;
  - requires a package the mode refuses. The engine's own base mods count: they target `extended`, and nothing
    stops a `native` package from requiring one.

  The message names the package and the profile's mode.
- **The check runs on those packages, before a base mod is mounted.** A refusal therefore names the package the
  player enabled, never the base mod it required.
- **The mode is part of the cache key of a link that dropped failed mods** (§5.3).
- **`extended` admits everything.** Without a profile file the engine admits everything, as today (§6).

### 5.5 Run lock

- **The engine opens `<data root>/run.lock` for exclusive use and keeps it open until it exits.** It does so
  before it writes any other file. A second start on the same root cannot open the file, and stops with a
  message, having changed nothing. Two engines on one root would otherwise overwrite each other's saves and
  settings.
- **Only a held lock means "running".** The file's presence means nothing, and nothing reads its content. A
  crash leaves a file that the next start opens normally.
- **"Last played" is the file's time.** The engine sets the last-write time of `run.lock` once it holds the lock.
  A start that is refused never gets that far, so the time moves only when the game really started.
- **The launcher tests the lock by trying the same open.** It holds the lock itself while it writes a profile's
  `profile.toml`, `OME.INI`, `SYS4REG.INI` or `selectors.toml`, so a shortcut cannot start the engine in the
  middle of a write.
- **A copy of a data root leaves `run.lock` out:** the launcher's duplicate, and the driver's sandbox seeding,
  which copies whole folders today (`tools/drive_core.py`). The driver's seed leaves `profile.toml` out too: a
  sandbox is started with `--game-root`, which a profile file refuses.
- **One lock class,** in `Ome.Mods`, is what the engine and the launcher both open (§5.9).
- M0 confirms the exclusive open on both operating systems.

### 5.6 Started with no game

When the engine gets no engine argument at all, finds no `SYS4INI.BIN` in its own folder or the current
directory, and the launcher sits beside it, it starts the launcher and exits. Double-clicking either program
then works. With any argument the engine reports the error as today: a mistyped command must show its own error.

### 5.7 `Ome.Cli`

Every command that takes `--data-root` reads `profile.toml` through the shared resolver (§5.2), so
`omp check --data-root <profile root>` needs no game options and links what the profile would run.

### 5.8 `--game`

"Profile" now means the player's profile (§3), so the option that names a game changes its spelling.

- **The engine and `Ome.Cli` accept `--game ID`** with the meaning `--profile ID` has. `--profile` stays as an
  alias with no warning. They are one option, and the last one given wins in both programs, through the shared
  resolver. `omp save-diff` has its own option parser and gains `--game` there.
- **The Python tools gain an exact `--game`.** `tools/paths.py` reads the common flags with argparse, which
  accepts an unambiguous prefix: today `--game kamidori` is silently read as `--game-root kamidori`. The exact
  alias goes into `tools/paths.py`, `tools/validate.py` and `tools/text_geometry_compare.py`, and
  `run-godot.ps1` gains `-Game` with `-Profile` as its alias.
- **Messages a player reads** name `--game` and say "game": `GameProfileSelection`'s messages, the tests that
  assert them, and the options panel's diagnostics line.
- **The docs change with the flag, in the same change (M1):** every modder guide, `docs/tools-reference.md`, the
  generated `docs/modding/games/README.md` (through `tools/modding_games_build.py`), and the rule in `AGENTS.md`
  that names the placeholder. `--profile` becomes `--game` and `<profile>` becomes `<game>`, all at once, so no
  guide mixes the two words.
- **What keeps the old word:**
  - the package manifest's `[game] profile` key. It is a released format and packages exist; the table's name
    already says what the id is;
  - keys named `profile` in JSON the tools already emit, and the driver protocol's `hello.profile`;
  - file and folder names: `engine/Ome.Engine/Profiles/`, `vm-map/globals-<profile>.toml`,
    `build/games/<profile>/`, `mods/<profile>-base/`;
  - type names in the code (`GameProfileManifest`);
  - AGE's own "shared profile" (`SAVE.DAT`), which is a third thing and keeps its name.

### 5.9 What `Ome.Mods` gains

The launcher has to link exactly as the engine does (§2, rule 5). Today that is not possible from outside the
Godot host, so these move or are added:

- **One link base.** The Godot host builds the link's base privately, from an opcode table embedded in the Godot
  assembly, and `Ome.Cli` builds its own from the repository's files. One factory in `Ome.Mods` replaces both,
  and the embedded tables move to a shared assembly. The engine, `Ome.Cli` and the launcher then link from the
  same data.
- **An index of a folder:** for each candidate its id, version, game, target, requirements and whether it ships
  Lua, from the manifest alone (§5.3).
- **A link for inspection that writes nothing.** No cache and no selector: the only path that writes the cache
  today also saves `selectors.toml`, so a launcher that used it on every switch of the Mods page would hand a
  permanent selector to every package the player only tried. The inspection returns the report, the discovery
  notes that the host prints to its console today (the base mod's mount among them), the resolved order, and the
  reason where that order differs from the player's.
- **States for packages that are not enabled:** needs Extended, needs a dependency no folder holds, does not
  support this install's revision. They come from the index and the install.
- **Releasing a selector as a call,** which is what `--forget-mod` does at startup.
- **The run lock's class** (§5.5), so the engine and the launcher make the same open.
- **The game version's rule,** which is in the options panel today, moves to a shared assembly so the launcher
  shows the version 情報 shows (§8.2).

Three of these are more work than they read:

- **The shared tables make the core build depend on a generated file.** Today only the Godot build fails without
  `build/opcodes.json`. `Ome.Cli` reads that file live today; with the embedded copy, a developer who runs
  `opcodes_build.py --build` must rebuild before `omp` sees the change.
- **The reason for a moved package is new resolver work.** The resolver keeps no record of why it moved one.
- **The index needs a manifest-only read** in both kinds of package, a folder and an `.omp`, under the reader's
  path and size limits.

The repository's OMS files stay as they are: a build outside the repository links without them (backlog,
"Toolchain packaging for exported ports"). The launcher and the engine of one archive are in the same position
and agree. A launcher from an archive and an engine run from the repository can disagree; that pairing is for
development only.

### 5.10 The driver starts a profile

`tools/drive.py port start` gains a way to start the engine on an existing profile root: it passes `--data-root`
with that root and no `--game-root`. Today it always makes a sandbox and names the game root, which a profile
file refuses, and it cannot attach to an engine that something else started. Gate 10 needs this.

## 6. Mode

The roadmap separates compatibility mode from extended mode (§3.1), and a package already says which it needs:
`target = "native"` or `"extended"` (mod loader §3.3, Lua §6.2). The profile's mode is the same word, and its
rule is which packages link (§5.4).

| | Compatibility (`native`) | Extended (`extended`) |
|---|---|---|
| Packages | `native` targets, and packages with no game, when they ship no Lua | every package |
| Saves | no mod state beside them | mod state beside them (`SAVEnn.modstate.json`) |
| Stack | `omp export` accepts it | `omp export` refuses it once it holds an `extended` package |
| Later | | the switch for engine extensions that are not native: extension opcodes, engine-owned save data, non-native presentation options |

- **Names.** The UI says Compatibility and Extended, the roadmap's words. The file says `native` and `extended`,
  the package manifest's. No third word is introduced.
- **What Compatibility says.** It describes what the profile holds: a stack `omp export` accepts, and saves with
  no mod state. It is not a promise about native AGE. The roadmap says such a stack may stay usable with
  `AGE.EXE` and that this is not a product requirement (§3.1); the mode does not change that.
- **Languages.** A language package keeps saves loadable in native AGE (localization §8), so Compatibility admits
  it.
- **The wizard starts a profile in Compatibility mode.** Enabling a package that needs Extended asks the player
  to switch, and says what changes. Switching up is always allowed. The switch is on the Overview page.
- **Switching down** warns: saves made with Lua mods keep their mod state, which a Compatibility profile no longer
  reads, and the packages the mode refuses are disabled.

## 7. Library

- **Shared, per game.** `<OME home>/mods/<game>/` holds a game's packages, and `mods/common/` those with no
  `[game]` table. A profile points at folders (`[mods] folders`) and enables ids (`order`). A package is stored once
  however many profiles use it, which matters for voice and CG packs, and two profiles cannot drift onto
  different copies of one mod.
- **A profile may use other folders:** its own, or any folder the player picks. This answers "where the mods
  folder lives". A folder listed by only one profile is that profile's private library.
- **One version of an id per folder.** Adding a package whose id is already there replaces it, after asking. Two
  versions that a player copied in by hand are shown as a problem on that package.
- **Adding** copies the `.omp` (or folder) into the library; the original stays where it was. The launcher reads
  its manifest with the engine's reader first (§12), and files it by its `[game] profile`.
- **Removing** a package from the library lists the profiles that enable it and those that still hold a selector
  for it. A selector stays reserved (mod loader §6.2) until the player releases it (§8.4).
- **The base mods are not in the library.** The engine carries each game's base mod inside its own build and
  mounts it when an enabled package requires it (Lua §9.1; `ShippedPackages` in `Ome.Mods`). A player installs
  nothing and a mod packages nothing. The Mods page shows a mounted base mod as part of the engine, with its
  version, from the link report's note.

## 8. The launcher

### 8.1 Projects

| Project | Where | Holds | Tested by |
|---|---|---|---|
| `Ome.Launcher.Core` | `engine/`, in `OmeEngine.sln` | the model: OME home, profiles, installs, the library, checks, the engine command. No Avalonia type | unit tests in the core gate, on synthetic installs |
| `Ome.Launcher` | `launcher/`, with `launcher/OmeLauncher.sln` | the Avalonia views and view models, and `Main` | a headless smoke of the command line (§8.6) |

- **Same repository, same version.** `launcher/OmeLauncher.sln` references the engine projects it needs, as
  `godot/OME.sln` does. The core gate builds and tests the model without the UI packages.
- **The split is the shell's** (shell §4): a headless model and a thin presentation. The devkit can later be a
  second head over the same model.
- **What the model references:**
  - `Ome.Mods`: manifests, the index, the link, the profile file's reader and the resolver (§5.2). It has the
    engine's only TOML reader, and the Godot host and `Ome.Cli` both reference it;
  - `Ome.Scripting.Lua`: without a Lua runtime the link fails every package that ships Lua. The link compiles
    those files to check them and runs nothing (§12);
  - `Ome.Engine`: game manifests, the catalog, `Sys4RegIniStore`;
  - `Ome.Engine.Frontend`: `OmeIniStore` and `PortOptions`.
- **Avalonia and every package the launcher ships** join `THIRD_PARTY_NOTICES.md`.

### 8.2 Games and installs

- **Recognizing an install.** The launcher reads the folder's `SYS4INI.BIN` header and matches it against the
  manifests, with the engine's own `GameCatalogIdentity`. The result is one of: a supported game; an AGE game this
  engine does not support (the catalog revision and title are shown); not an AGE install.
- **What it shows for a match:** the game's titles, the game version by the engine's own rule (§5.9), and the
  catalog fingerprint.
- **Overlay candidates, by structure only.** A subfolder that holds loose files under names the catalog lists is
  offered as an overlay, with the count. Files there with an AGF name and a BMP header switch on
  `allow_bmp_as_agf`. The launcher says what it found, not whose work it is.
- **Known installs** are the install roots of the existing profiles. There is no separate list to keep.
- **The games page** lists the supported games from the manifests, with their art, and the installs known for each.

Looking for installs by itself (uninstall entries in the Windows registry, store folders) is left out: it needs
per-game facts nobody has collected.

### 8.3 The new-profile wizard

Two steps:

1. **Game.** Choose the install: browse, drop a folder on the window, or pick a known install. The step shows what
   §8.2 found and the overlay candidates. A folder that is not a supported game cannot continue, and says why.
2. **Profile.** The name (the game's title by default), the mode (Compatibility), and what to start from:
   - **nothing;**
   - **a copy of another profile** of this game, with or without its saves;
   - **the data the bare engine already has** for this game, under Godot's `user://games/<game>/`. Its saves and
     selectors are copied. The shared `user://OME.INI` is copied as the profile's own. Its `mods/` folder becomes
     a library folder of the profile, and `order` lists every package there that `load-order.txt` leaves enabled:
     the listed ones in their order, then the unlisted ones by id. The profile then runs the stack the bare
     engine ran. It starts in Extended mode when that stack needs it, and the wizard says so;
   - **the original game's saves,** copied from where native AGE keeps them, with its `SYS4REG.INI`. That folder
     is often not in the install: Himegari keeps it under `%LOCALAPPDATA%`. The launcher finds it with the
     engine's own `Sys4PersistencePaths.ResolveNative`.

   The profile's location and its library folders sit under "Advanced", with their defaults.

The wizard then opens the profile's page.

**Copying saves copies `mods/selectors.toml` with them.** A save made inside a mod's script holds that mod's
selector (mod loader §6.2), so saves and selectors travel together. The link cache and `run.lock` are not
copied.

### 8.4 A profile's pages

Every page sits under the header of §9, which holds the cover, the name, the state and **Play**. While the
profile is running, everything that writes one of its files is locked: the Mods page, the mode's switch, Settings,
Advanced and copying saves in (§5.5).

- **Overview.** The game (titles, install path, overlays, the publisher's page), the profile (mode and its
  switch, language, display, saves, folder), the enabled mods in load order, and the checks of §8.5.
- **Mods.**
  - The library's packages for this game, each with a switch, its place in the order, and a state: off, ready,
    needs a dependency, conflicts, does not support this install's revision, needs Extended.
  - A disabled package keeps its place in the list (§4.3). A package the profile has never listed is at the end.
    A mounted base mod is shown first, since it loads first.
  - Dragging sets the preferred order. The list shows the resolved order, and says so where a dependency or a hint
    overrode the player's (mod loader §6.3).
  - Enabling a package enables its dependencies that the library holds, and says which. The engine's own base
    mods are never switched on this way; they mount by themselves (§5.3).
  - A detail pane: the manifest's fields, the README as plain text, and what the package supplies and overrides,
    from the link report.
  - **The link report**, after every change: hard conflicts, ordered interactions and overrides (mod loader §7),
    each line leading to the packages involved.
  - Add, remove, open the library folders. Under "Advanced": the selectors this profile holds, with "release" for a
    package that is gone. Releasing is one action, done at once (§5.9); nothing is stored for the next start.
- **Settings.**
  - *Language:* the shell's languages plus those the enabled packages define, as the options panel's OME section
    lists them. Written to `OME.INI`.
  - *Display:* start fullscreen or windowed (`display:ScreenMode` in `SYS4REG.INI`), and the window size
    (`OME.INI`). When the game's registry forces a window (`display:ForceScreen`), the choice is shown as fixed,
    with the reason.
  - *FPS counter and page locator* (`OME.INI`).
  - These are the five fields of `PortOptions` and one registry key, edited directly. There is no shared option
    model to draw them from.
  - The game's other settings (volumes, message speed) stay in the game's options panel: offline edits there
    need each key's interplay with the scripts' own config screens looked at first.
- **Saves.** Open the save folder; how many saves it holds and when the last was written; copy saves in from
  another profile of this game or from the original game's saves (§8.3); and where the original game keeps its
  saves, with a button that opens that folder. The launcher copies nothing out: a player who wants a save in the
  original game copies the file by hand.
  - **Copying into a profile that has selectors of its own** merges the two `selectors.toml` files when they
    agree. It is refused, with the two ids named, when they give one number to two packages or one package two
    numbers.
- **Advanced.** Overlays and the BMP switch; the renderer (GPU, or the reference renderer as a diagnostic); the
  text backend; the Lua error policy; what a failed link does; "start with Direct3D 12" (Windows); open
  `diagnostics/`; copy a diagnostics summary.
  - **Direct3D 12** is the documented way past a crash at Vulkan startup that an overlay tool's Vulkan layer
    causes (`docs/platform-portability.md`, "Windows Vulkan startup and injected overlays"). The other documented
    way is an environment variable for one vendor's layer, which a shortcut cannot set.

**The header's actions:** Play, Duplicate, Shortcut, and under More: rename, delete. The profile list has "add
an existing profile", which is also how a profile that was moved by hand comes back (§12).

**Shortcut** opens a dialog that gives the command which starts this profile without the launcher's window, so a
player can make a Windows shortcut, a script, or an entry in another program's game list. It has two forms:

| Form | Command | What it does |
|---|---|---|
| Through the launcher (the default) | `OME-Launcher --launch <profile id>`, with `--home DIR` when OME home is not the default one | runs the checks and shows a start that failed. No window opens when the start succeeds |
| The engine directly | the command Play runs (§8.5): `OME [--rendering-driver d3d12] -- --data-root <profile root>` | needs no launcher, and runs no check. The engine's log goes where Godot keeps it, not to the profile |

- **Separate fields.** For the chosen form the dialog shows the target, the arguments and the working folder, each
  with a copy button, and the whole command line. Separate fields are what a Windows shortcut's properties and
  other programs' "add a game" forms ask for.
- **Create a shortcut** writes one for the chosen form, on the desktop or where the player picks: a `.lnk` file on
  Windows, a `.desktop` file on Linux.
- **The paths are absolute.** A shortcut stops working when the OME folder or the profile moves.
- **One builder.** The commands come from the code that Play and `--print-command` use (§8.6), so the dialog
  cannot disagree with what Play runs.

### 8.5 Checks and launch

**Checks**, when a profile's page opens and again at Play:

| Check | On failure |
|---|---|
| The install folder exists and holds the profile's game | Play is off; "locate the install…" |
| The catalog fingerprint equals the recorded one | a warning; "accept the install as it is now" records the new one |
| The profile root can be written | Play is off; the path and the OS error |
| Every enabled package is found, and the mode admits it | Play is off; the Mods page names them |
| The stack links | Play is off, or with `on_failure = "skip"` a warning that names what will be dropped |
| No engine holds the profile's lock | Play reads "running" |

- **The link check is the linker's,** through the inspection of §5.9. It writes nothing. The engine links again
  when it starts, and writes the cache and the selectors then, as it does today.
- **It has the engine's limits.** A stack that needs the OMS toolchain links only where the engine could link it
  (backlog, "Toolchain packaging for exported ports").

**Launch:**

- **The command.**

  ```text
  OME [--rendering-driver d3d12] -- --data-root <profile root>
  ```

  Every path is absolute. The working folder is the engine's own. On Linux the launcher also sets `PWD` to it,
  because the engine prefers `PWD` to its real working folder there.
- **Before each start** the launcher checks the root for links (§12).
- **The log.** The launcher captures the engine's standard output and standard error and writes them to
  `diagnostics/engine.log`.
  - It does not pass Godot's `--log-file`. Godot opens that file before the engine can take the lock or check the
    folder, so a second start would cut off the running session's log, and a link at that path would be followed.
  - The launcher's process stays alive for as long as an engine it started runs. A launcher that "closes while
    the game runs" closes its window only. A `--launch` with no window does the same.
  - An engine started directly logs where Godot logs today, under `user://`.
  - M0 confirms the capture on both operating systems. If a release engine on Windows gives its parent nothing
    to capture, the launcher passes `--log-file` with a file name that is new for each start.
- **What a failed start is:** any exit code but 0. Code 2 is the engine refusing to start; other codes are crashes
  and timeouts.
- **When a start fails,** the launcher comes forward with the end of the log and runs the checks again. A check
  that now fails, the link among them, is shown where it is fixed. The engine writes `diagnostics/mod-link.txt`
  on every link, a good one included, so that file does not show that a link failed.
- **What an exit code does not show:** a script error the player answered by quitting exits with 0, and a halted
  VM keeps its window open. The player sees those in the game's own window.
- **While the game runs** the launcher's window stays open, minimizes or closes, by a launcher setting. It does
  not talk to the engine.
- **Last played** is the time of the profile's `run.lock` (§5.5).
- **Development.** `OME-Launcher --engine PROGRAM`, with `--engine-arg ARG` for each of its arguments, names
  another engine for that run of the launcher (the Godot editor with `--path godot`), so the launcher runs from
  the repository. The builder puts Godot's flags after those arguments, then the `--`. An `--engine-arg` of `--`
  is refused. No file can name a program (§12).

### 8.6 Command line

| Command | Effect |
|---|---|
| `OME-Launcher` | the window |
| `OME-Launcher --launch ID\|DIR [-- ARGS]` | run the checks and start the profile with no window; a failed check opens the window at the problem. `ARGS` are engine flags for that run |
| `OME-Launcher --check ID\|DIR` | run the checks, print them, exit non-zero on a failure |
| `OME-Launcher --print-command ID\|DIR` | print the engine command a launch would use: the program, the arguments and the working folder, as JSON |
| `--home DIR` | use `DIR` as OME home |
| `--output FILE` | write what `--check` or `--print-command` prints to `FILE` as well |
| `--engine PROGRAM`, `--engine-arg ARG` | start this program, with these arguments, instead of the engine beside the launcher (§8.5) |

- **Shortcuts.** The Shortcut dialog (§8.4) defaults to `--launch`, because only a launch through the launcher
  runs the checks and shows a failed start.
- **`--check` and `--print-command` run before Avalonia starts,** so they need no display, and they make the
  model testable end to end without a window.
- **Printing on Windows.** The release engine has no console, and a windowed launcher has none either. The
  launcher attaches to its parent's console when there is one, and `--output FILE` is the dependable form for a
  script. A shell does not always wait for a windowed program: PowerShell needs `Start-Process -Wait` or a pipe,
  and cmd needs `start /wait` outside a batch file. `docs/tools-reference.md` gives those forms beside `--output`.
  M0 measures all of it from cmd, PowerShell and a captured process.
- **A root the launcher has not reviewed** (§12) is not started by `--launch DIR`: the window opens at its
  review.

### 8.7 Art

A game has two images, and each has one place (§9):

| Image | Shape | Where it is shown |
|---|---|---|
| Banner | 5 : 1 | the profile list, once per game, as the heading of that game's profiles; the wizard, beside the game it recognized |
| Cover | 32 : 45 | the header of a profile's page, on every tab |

For each image the launcher takes the first of:

1. for the cover only, the profile's own image (`[launcher] cover`, a file inside the profile root);
2. the player's image for the game (`banner.*` or `cover.*` in `<OME home>/art/<game>/`);
3. the game's image, downloaded from the address its manifest names (below);
4. a generated card: the game's title set in the launcher's font on a colour derived from the game's id.

**Nothing is distributed.** The repository and the archives hold no game art, only addresses. The player's own
machine fetches the files from their publisher.

- **The manifest names the images.** A game manifest (`engine/Ome.Engine/Profiles/<id>.json`) gains an entry that
  only the launcher reads:

  ```json
  "launcher": {
    "site": "https://…",
    "banner": { "url": "https://…/banner.jpg", "sha256": "…" },
    "cover": { "url": "https://…/package.jpg", "sha256": "…" }
  }
  ```

  `site` is the publisher's page for the game. The launcher's page for the game links to it, which is what a
  support banner is for.
- **The images chosen** by the user on 2026-11-15. The user read the publisher's terms and found nothing against
  this use of the banners or the covers. The banners are 500×100 JPEG files and the covers 320×450. The hashes
  are of the files as served that day:

  | Game | Image | `url` | `sha256` |
  |---|---|---|---|
  | `himegari` | banner | `https://www.eukleia.co.jp/eushully/eu10/banner_eu10a.jpg` | `7c9707b361b449cc5eda2bd7f9cc91ed61229cb502f3368b77c1390748f9757e` |
  | `himegari` | cover | `https://www.eukleia.co.jp/eushully/eu10/package.jpg` | `3fea5a5c47077be9ed49c46d4a77cd9512c44f32f6279dc5d412e589a342daef` |
  | `kamidori` | banner | `https://www.eukleia.co.jp/eushully/eu12/banner/banner_eu12_a.jpg` | `962de2e9bda6634d89211b7938a5e517f5c29444aae3586a672eaa58d3f594da` |
  | `kamidori` | cover | `https://www.eukleia.co.jp/eushully/eu12/image/eu12_package.jpg` | `2335906c1c675fd0461bac9190bfc709b5b0d4751fc1dd1c309edb4433f0648f` |

  `site` is `https://www.eukleia.co.jp/eushully/` for both. These values move into the manifests in M2, and the
  manifests are their home from then on.
- **Download on consent.** When a game the player has a profile for has an image missing, the launcher asks once
  whether to download art, and names the host it will contact. `launcher.toml` keeps the answer: `ask`, `always`
  or `never`. The games page can download a game's art, and remove what was downloaded, at any time.
- **What a download is.** One request per image, with a generic user agent and a timeout.
  - The address must be `https`, and a redirect is refused.
  - The size limit is enforced while the body arrives.
  - The SHA-256 is checked before any byte reaches an image decoder, and only a file that then decodes is kept.
  - It goes to `<OME home>/cache/art/<game>/` and is not requested again while it is there. Its hash is checked
    again each time it is loaded from there.
  - With `always`, a missing image is tried once per launcher start.
- **A failure shows the placeholder.** No connection, a missing file, a wrong hash: each leaves the generated
  card and one line in the launcher's log. Nothing blocks, and Play never waits on art.
- **Why a hash.** The address belongs to someone else and may outlive the page behind it. If the file is replaced,
  or the domain changes hands, the launcher shows the placeholder instead of whatever is served. The cost is that
  an image the publisher re-encodes needs a new hash in an engine release.
- **Only a built-in manifest names an address.** A package, a README and a profile file cannot make the launcher
  open a connection (§12).
- **Local art has limits too:** a profile's own cover and a player's image for a game are read only up to a file
  size and a pixel size.
- **Every screen has to look right with the generated cards alone**, because a player may answer `never`.
- **Neither image is stretched.** The list draws the banner about 280 pixels wide and the header draws the cover
  96 pixels wide, so both are scaled down from files of 500 and 320 pixels. A dense display draws them at up to
  their own size.
- **No banner per profile.** The banner is the game's, so a profile has no banner of its own. A profile's own
  image is a cover.
- **Later:** art read from the player's own install (a title image the manifest names). It ships nothing, but needs
  a decode path in the launcher and a per-game pointer.

### 8.8 The launcher's own settings

`launcher.toml`: UI language, theme, window placement, what to do while a game runs, whether to download art
(§8.7), the last profile, the profile roots outside `profiles/` with their ids, the roots the player has
reviewed (§12), and the defaults for a new profile. It names no program.

**Language.** The launcher has keyed string tables, `ja` and `en`, as the shell has (shell §6). It starts in the
OS language when it has that table, else English. A community language for the launcher's own text is future work;
the `[[engine]]` catalog of a language package is the natural carrier.

## 9. Presentation

This section fixes what is on each screen. **The reference is the mock,**
`2026-11-15-launcher-design-mock.html` beside this file, which the user accepted on 2026-11-15. Open it in a
browser. Its controls switch the states below, both themes and the wizard; the Shortcut dialog opens from the
header. It reads the art from `build/launcher-mock-art/`, which is not in the repository, and draws the
placeholders without it.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ Open Maid Engine                                                    -  []  x │
├──────────────────────┬───────────────────────────────────────────────────────┤
│ PROFILES             │ ┌───────┐  Kamidori, modded               [ ▶ Play ]  │
│ ┌──────────────────┐ │ │       │  game title, both languages     last played │
│ │ Kamidori banner  │ │ │ cover │  ● Ready  Extended  2 mods  English         │
│ └──────────────────┘ │ │       │  D:\Games\Kamidori_Translated               │
│  ● Kamidori (English)│ └───────┘                                             │
│ ▌● Kamidori, modded  │ [ a failed check, with the action that fixes it ]     │
│  ● Kamidori (ja)     │ Overview   Mods   Settings   Saves   Advanced         │
│ ┌──────────────────┐ │ ───────────────────────────────────────────────────── │
│ │ Himegari banner  │ │ …                                                     │
│ └──────────────────┘ │                                                       │
│  ● Himegari          │                                                       │
│ [ + New profile ]    │                                                       │
│ Games · Launcher · … │                                                       │
└──────────────────────┴───────────────────────────────────────────────────────┘
```

- **One window.** The profile list on the left, the selected profile on the right, with Play always in the same
  place.
- **The list.** Each game's banner appears once, as the heading of that game's profiles. A profile is a row: a
  state dot, its name, its mode and its mod count. A game with no profile is not in the list; the games page has
  it. Below the list: "New profile", "Add an existing profile", then Games, Launcher settings and About.
- **The header,** on every tab: the cover, the profile's name, the game's titles, the state, the mode, the mod
  count, the language, the install path, Play, and when it was last played. The user's note: the cover's place
  may change later.
- **A problem is shown where it is fixed.** A failed check is a notice between the header and the tabs, with the
  action that fixes it or a link to the page and the row that does.
- **States every page must have:** no profile yet (the wizard's first step fills the window); install missing;
  a failed check; running; a start that failed; art missing (the placeholders).
- **The wizard** is a dialog over the window, in the two steps of §8.3. **Shortcut** (§8.4) is a dialog too,
  opened from the header.
- **Font.** Noto Sans JP, which the port already bundles (shell §11), so Japanese titles read the same on every OS.
- **What the mock does not fix.** Its colours, type sizes and spacing are the starting point for M3 and may
  change. Its names, paths, versions and report lines are invented.
- **What the mock does not show,** and M3 designs from this spec's text:
  - the window with no profile yet, and a start that failed;
  - the fingerprint warning with "accept the install as it is now", and the warning for `on_failure = "skip"`;
  - the Mods page's "Advanced" area, and two of its package states: needs a dependency, and does not support
    this install's revision;
  - report lines that lead to the packages they name;
  - the display choice shown as fixed when the game forces a window;
  - the games page, the launcher's settings and About;
  - the art consent question, the review of a profile the launcher did not create (§12), and the other dialogs;
  - the wizard's known installs, and its answers for a folder that cannot continue.

## 10. Packaging

- **Both archives carry the launcher** beside the engine: `OME-Launcher.exe` next to `OME.exe` and `OME.pck` in
  `OME-windows-x64.zip`, and the same pair in `OME-linux-x64.tar.gz`. The engine's files keep their places.
- **A self-contained publish. Not NativeAOT:** the Windows archive is cross-built on Linux
  (`docs/platform-portability.md`), which NativeAOT cannot do, and the engine libraries the launcher uses are not
  written for trimming.
- **Single-file is the aim, and M0 decides it.** The pipeline has never produced a .NET program of its own. What
  is not known:
  - whether a Windows program cross-built on Linux gets its icon, version and manifest resources;
  - where Avalonia's native libraries go. A single file unpacks them to a per-user temporary folder, which is
    outside a portable home. The other way puts them beside the programs;
  - what the Linux build needs from the system (font and X11 libraries), and whether the glibc floor rises.
- **The .NET version.** The engine targets .NET 8, whose support ended on 2026-11-10 (`docs/backlog.md`,
  "Platform and project"). A self-contained launcher carries its own runtime, so M0 also decides which version
  it targets.
- **The package gates grow:**
  - the required-file lists and checksum ledgers include the launcher;
  - the notices check reads the engine's dependency list from a file on disk, which a single-file program hides
    inside itself; it needs the launcher's list another way;
  - `tools/publish_github_release.py` and its tests pin the exact artifact sets;
  - Linux's dynamic smoke runs `OME-Launcher --check` on a synthetic profile; Windows's structural verification
    covers the launcher's PE.
- **Size** is to be measured in M0. The Godot export already carries a .NET runtime of its own that the launcher
  cannot share.

## 11. What the launcher does not own

- **The engine's flags.** They stay documented in `tools-reference.md`. The launcher uses only what a player could
  type.
- **The link.** Rules, conflicts and order stay in `Ome.Mods`.
- **Option definitions.** `OME.INI`'s options stay defined by `PortOptions`.

## 12. Untrusted input and privacy

- **Packages are untrusted** (mod loader §11). The launcher reads them only through `Ome.Mods`' reader, with its
  path and size limits. The link compiles a package's Lua to check it and never runs it.
- **A profile folder is data, never a command.** A player may get one from someone else, and a profile root can
  be any folder. It has to be harmless by what it can hold, because nothing can make sure a player reads a
  review:
  - **`profile.toml` has a fixed set of typed keys** (§4.3). It cannot hold engine arguments: several engine
    flags write to any path they are given, and one truncates the file it names. It cannot hold environment
    variables: some make a process load code. A player who wants an extra flag for one run types it
    (`OME-Launcher --launch ID -- ARGS`).
  - **A network path in it is opened only if this machine's player approved that location.** A game or a mod
    folder on a network share needs no special handling: the operating system reads it like any folder. But
    opening a Windows network path (`\\host\share\…`) contacts the host it names and offers it the player's
    sign-in, so a profile from someone else must not be able to choose the host.
    - Picking a network folder in the launcher approves it. So does accepting the review of a profile that names
      one, which shows the location and says it is another computer.
    - The approved locations are kept on the machine, outside OME home and every profile (§5.3), so a profile
      or a whole OME home from someone else brings none.
    - The engine and the launcher open no other network path. The engine stops and names the path; the launcher
      shows it as needing approval. `--allow-network-paths` approves a profile's paths for one run of the
      engine.
    - A share reached through a drive letter, and a network mount on Linux, are ordinary paths.
  - **Nothing in it is a link.** With a profile file, the engine refuses a root in which a folder or a file is a
    symbolic link or a junction, and the launcher makes the same check at the review and before each start. Both
    write into the root, and a link there would send a write to another file. A cloud folder's placeholders are
    not links.
  - **Its link cache is not used** unless this machine wrote it (§5.3).
  - **`[launcher] cover` must be inside the profile root.**
- **A root the launcher did not create is reviewed once.** That is a folder added as an existing profile, or
  named by `--launch DIR`.
  - The launcher shows its game, its install path, its mod folders and its enabled mods, and the player accepts
    or declines. Until then Play is off. `launcher.toml` records the accepted roots.
  - The launcher opens none of the root's paths before the player accepts. The review shows them as text, and
    marks a network location as such.
  - The review informs the player. It is not what keeps a root harmless; the rules above are.
- **A whole OME home from someone else is the player's own,** as far as the launcher can tell: its profiles and
  its record of accepted roots come with it. The rules above still hold for every profile in it.
- **Enabling a mod is the decision it always is.** A profile from elsewhere can bring mods in its own folder;
  they are packages like any other (mod loader §11; Lua §15).
- **No file names a program.** The development engine is named by flags of the launcher (§8.5), so a shared
  portable `data/` folder cannot make Play start something else.
- **A README is shown as plain text.** It loads no image and follows no link by itself.
- **One use of the network.** The launcher downloads a game's banner and cover from the addresses in that game's
  built-in manifest, and only after the player allowed it (§8.7). Nothing else opens a connection: no update
  check, no telemetry, and no address taken from a package, a README or a profile file. An update check, if it
  comes later, is off until the player turns it on. Reading a folder on a network share is the operating
  system's file access, at a location the player approved; it is not a connection the launcher makes.
- **What an art request tells others.** The publisher's server sees the player's network address, as for any
  download, and a generic user agent. The request carries nothing about the player, their profiles or their
  mods. Looking up the host's name and checking its certificate contact the player's name server and the
  certificate's issuer, as for any `https` address. The consent question names the host.
- **The diagnostics summary** holds paths on the player's machine. It is copied only when the player asks.

## 13. Validation gates

Gates 1–9, 11, 13 and 14 are automated and run on synthetic data. Gates 10 and 12 need both games and the
Windows driver, so they are local gates, run before a release.

| Gate | Passes when |
|---|---|
| 1. Profile file | the engine started as `OME -- --data-root R` runs the same game, overlays, stack and options as the equivalent flags: the same `[profile]`, `[assets]` and `[options]` lines and the same link cache key. A flag of §5.2's table has its listed effect, and a refused flag stops with its reason. `--game` and `--profile` select alike, and the last one given wins in the engine and in `Ome.Cli`. An unknown key, a wrong game and an unknown format version each stop with their reason |
| 2. Explicit stack | two profiles sharing one library with different `order` link their own stacks; adding a package to the library changes neither link; each keeps its own selectors; an enabled package with a disabled dependency stops with its name; an enabled id in no folder stops at discovery; an unreadable package that neither enables stops neither; packages the profile does not enable are not hashed, with failed mods dropped or not |
| 3. Mode | a `native` profile refuses an `extended` package by name, a Lua library with no game, and a `--mod` package of either kind; a `native` package that requires a base mod is refused under its own name; the same profile as `extended` links them; a stack linked as `extended` with failed mods dropped is not reused after a switch to `native` |
| 4. Run lock | across two processes, with a readiness signal and no sleep: a second holder of a root's lock is refused and has changed no file; a `run.lock` left by a killed process does not read as running; a copied root holds no `run.lock`; the file's time moves when the lock is taken and not when a start is refused |
| 5. Hand-over | an engine with no argument, no game and a launcher beside it (a stub program, until M3) starts it; with any argument it reports its error as today |
| 6. `Ome.Cli` | `omp check`, `export`, `explain`, `lua-stubs` and `text` with `--data-root R` use the profile's game, install and `[mods]` |
| 7. Model | on synthetic installs: create, duplicate (saves and selectors together), adopt a bare-engine data root (its `OME.INI`, and every package `load-order.txt` left enabled), rename and delete a profile; merge two agreeing selector files and refuse two that clash; recognize a supported game, an unsupported AGE game and a non-install; find an overlay candidate and the BMP case |
| 8. Checks | each row of §8.5's table is reported with its own message; the link check's report equals `omp check`'s for the same profile; a check writes no cache and no selector |
| 9. Launch command | `--print-command` gives the program, Godot's flags, the `--` and the engine flags of §8.5, with absolute paths; `--engine` and `--engine-arg` name a stub, and an `--engine-arg` of `--` is refused; what the stub prints is in `diagnostics/engine.log`; a stub that exits non-zero takes the failed-start path, which shows the log's end and runs the checks again; `--output` holds what was printed; a home that is not the default one adds `--home` to the Shortcut dialog's first form, and its second form equals `--print-command`'s |
| 10. Launch, both games (local) | the driver, started on a profile's root (§5.10), reaches the title for each game; `--launch` on the same profiles starts them, and each one's log is in its `diagnostics/` |
| 11. Packages | both archives hold the launcher; the Linux smoke passes `--check` on a synthetic header-only `SYS4INI.BIN` with a temporary `--home` and no display |
| 12. Kamidori's translated install (local) | the wizard, pointed at it, offers `patch` and the BMP switch; the profile it makes starts with the `[profile]` and `[assets]` lines of `run-godot.ps1 -Kamidori -TranslationPatch`, and the driver's screenshots at the title and the first text page are equal for both |
| 13. Art | through a test seam for the manifests and the HTTP handler: an image whose hash matches is stored and shown; a wrong hash, a body over the size limit and a file that is not an image each leave the placeholder and store nothing, and a wrong hash never reaches the decoder; an `http` address and a redirect are refused; with `never`, and before the player has answered, no request is made; a stored image is not requested again; a profile's own cover and the player's image for a game each win over the download |
| 14. Trust | a root the launcher did not create is not started before its review, by Play or by `--launch DIR`, and none of its paths is opened before then; a network path the player has not approved on this machine stops the engine and is not opened by the launcher, one the player picked or accepted is used, and a profile or an OME home from elsewhere brings no approval; a root that holds a link stops both; a cache entry that another machine wrote is not used; a `profile.toml` with an arguments or environment key stops the engine as an unknown key; a cover path outside the profile root is refused |

UI behaviour is checked through the model and the command line. Automated clicking of the Avalonia window is not a
gate.

## 14. Milestones

Each milestone updates the docs its changes touch, in the same change (§16).

0. **M0 Measure.** Publish a bare Avalonia window as §10 says, for both targets, and measure:
   - size and start time, and a Japanese title drawn in Noto Sans JP;
   - the cross-built Windows program run on real Windows, with its resources;
   - native libraries unpacked against placed beside the program;
   - the Linux build's system libraries and glibc floor, and `--check` with no display;
   - the launcher's output from cmd, PowerShell and a captured process;
   - a release engine's standard output and error captured by a .NET parent, on both operating systems; a second
     start on a running profile; and the exclusive lock of §5.5;
   - `--rendering-driver d3d12` given through a `.lnk`;
   - the publish's memory use in CI, where the runner already kills builds for memory;
   - the list of packages the launcher would ship, and the .NET version it targets.
1. **M1 Engine.** §5: the resolver and the profile file with its path and link checks, mods from a profile, the
   cache key's two changes, mode, the run lock, the hand-over, `Ome.Cli`, `--game` with its docs and tool
   aliases, the shared link base, and the driver's start on a profile root. Gates 1–6. This is useful alone: a
   profile folder starts from a shortcut.
2. **M2 Model.** The rest of §5.9, `Ome.Launcher.Core`, and the command line of §8.6. Gates 7–9, 13 and 14, with
   no window.
3. **M3 Window.** The wizard and the pages over the model, built from the mock (§9), with its colours and type
   settled as Avalonia styles. Gate 10.
4. **M4 Release.** Packaging and its gates (11, 12), and the notices.

## 15. Decisions

### 15.1 Decided

By the user, on 2026-11-15.

1. **The word.** The player's thing is the profile, because mod managers use the word that way and "profile root"
   already means its folder. The game is "the game", and the engine's option becomes `--game` (§5.8). Calling the
   player's thing an instance, and leaving the engine alone, was the alternative.
2. **What the mode is.** The package target a profile admits (§6). A new profile starts in Compatibility mode and
   switches up when a package needs Extended. The label "Compatibility" is this spec's choice of the roadmap's
   word and was not part of the decision.
3. **A shared library.** Per game, with explicit enabling per profile, and any folder a profile wants besides
   (§7). The accepted cost is §5.3: a second way to state the stack beside `load-order.txt`, which stays for runs
   without a profile file.
4. **Where OME home is.** Its own folder (§4.1). The bare engine keeps Godot's `user://`
   (`%APPDATA%\Godot\app_userdata\Open Maid Engine`), and the wizard adopts what is there. Giving the Godot project
   a custom user folder equal to OME home was the alternative; it would move existing data once.
5. **Art is downloaded, not distributed.** The user's design: a game's manifest names the addresses of its art,
   the launcher offers to download what is missing, and a placeholder stands in when there is none (§8.7).
   Carrying the art in the repository or the archives was the alternative, and it needed redistribution terms.
   The consent setting, the hash, the cache folder and the link to the publisher's page are this spec's details.
   The user chose the images and read the publisher's terms for them (§8.7).
6. **How much of the game's settings the launcher edits.** Language, display and the port's options only (§8.4).
7. **Names and layout.** `OME-Launcher` beside `OME`, and the hand-over of §5.6. The engine's files keep their
   places, so dropping the engine into a game folder, the package gates and existing scripts stay as they are. The
   launcher taking the name players click first, with the engine in a subfolder, was the alternative.
8. **Bundled base mods:** withdrawn, because there was nothing to decide. The engine already carries the base
   mods inside its build and mounts one when an enabled package requires it (Lua §9.1), so a mod never packages
   one. An earlier draft of this spec asked the question without having read that.
9. **Where the art goes.** The user's split: the banner in the profile list, as the sign of which game a profile
   is for, and the box cover on the profile's page. The banner appears once per game, as the heading of that
   game's profiles, and the cover is in the header on every tab (§8.7, §9). A banner on every profile, and a cover
   on the Overview page only, were the alternatives. The user accepted the mock's layout, and noted that the
   cover's place can change later.
10. **Exporting the launch command.** The user's addition: a way to get the command that starts a profile, for
    Windows shortcuts. It is the Shortcut action in the header (§8.4). The two forms, the separate fields and
    writing the shortcut file are this spec's details.

11. **Where saves go.** A profile's saves are in the profile's folder, and the player chooses where that folder
    is (§4.1, §8.3). The user had asked for a profile to set where its saves go, and an earlier draft had a save
    folder setting (`[paths] saves`, `--save-dir`). The review found that it breaks two things: the folder stops
    being the unit of backup, and saves part from the selectors they depend on, which stay in the profile root.
    Keeping the setting and moving `selectors.toml` with the saves was the alternative; it is one more engine
    change.

12. **Two rules from the reviews, accepted for now.** Nothing in a profile's folder may be a link (§12), and the
    launcher's process stays alive while a game it started runs (§8.5). The user's note: the first can be removed
    later if it causes problems.

### 15.2 Open

Nothing.

### 15.3 Changed after the review of 2026-11-15

A read-only review checked the draft against the code. Its findings held, and these are the changes of
substance:

- **The engine command needs Godot's `--`** (§5.1). The draft's `OME --data-root R` reached the engine with no
  argument.
- **A profile file holds no free arguments and no environment variables,** a profile from elsewhere is reviewed
  once, and no file names a program (§12). The draft trusted a profile folder fully.
- **A flag against the file has a rule per kind of flag,** and the game and the install cannot be overridden
  (§5.2). One resolver serves the engine and `Ome.Cli`.
- **The launcher's link check writes nothing** (§5.9, §8.5). The draft let it write the link cache, which would
  also have handed out selectors.
- **The link base, the folder index, an inspection link and package states move into `Ome.Mods`** (§5.9), so
  the launcher is not a third copy of host code.
- **Compatibility mode refuses Lua whatever the target,** and promises nothing about native AGE (§5.4, §6).
- **The launcher keeps the engine's log in the profile, treats any non-zero exit as a failed start,** and can
  write its command-line output to a file (§8.5, §8.6).
- **The overlay workaround is `--rendering-driver d3d12`,** which a shortcut can carry (§8.4).
- **The run lock is a held open,** not a file with a process id (§5.5).
- **Discovery hashes only enabled packages,** and a broken package stops only the profiles that enable it (§5.3).
- **`--game` reaches the Python tools,** where it was silently read as `--game-root`, and its docs change with
  the flag (§5.8).
- **The original game's saves are found where native AGE keeps them** (§8.3). The draft assumed a `SAVE` folder
  in the install.
- **Cut from version 1:** the save folder setting (§15.1, point 11), free arguments and environment variables,
  moving a profile inside the launcher, and backup to a zip. Kept against the review's suggestion: writing
  shortcut files, and the second theme.
- **Docs change in the milestone that changes behaviour** (§14, §16), as `AGENTS.md` requires.

A second review the same day checked the rewrite. It found every finding of the first resolved or resolved in
part, and these four to fix before acceptance:

- **A link cache is used only on the machine that wrote it** (§5.3). A received folder could carry a cache entry
  the engine would mount unchecked.
- **A network path in a profile is opened only once this machine's player approved it, and nothing in a
  profile's root is a link** (§12). A review opens none of a profile's paths. A network path from someone else
  would have made the launcher contact a host of their choosing, and a link would have sent a write elsewhere.
- **A failed start is shown by running the checks again** (§8.5). The draft read a failed link from
  `mod-link.txt`, which the engine writes on every link.
- **The cache key of a link that dropped failed mods covers the enabled packages only** (§5.3). It hashed every
  installed package, which the new discovery avoids.

It also led to these: the launcher captures the engine's output itself instead of passing `--log-file`;
"last played" is the lock file's time; Compatibility refuses a package that requires a base mod; the
development engine is `--engine` with `--engine-arg`; and the driver gains a start on a profile root, which
gate 10 needs.

## 16. Docs this will touch when built

- **M1:** `docs/tools-reference.md` (the command's shape, the profile file, the lock, `--game`, and under "Game
  driver" the start on a profile root and what a seed leaves out); every modder
  guide, the generated `docs/modding/games/README.md` and `AGENTS.md` for `--game` and `<game>` (§5.8);
  `docs/modding/cli.md` and `packages.md` (`profile.toml`'s `[mods]`); `docs/modding/troubleshooting.md`; the
  roadmap's §3.1, where this spec's reading of compatibility mode has to agree with its text.
- **M2:** `docs/tools-reference.md` (the launcher's command line, with the forms that make a shell wait); the game manifests' `launcher` entry and
  whatever `tools/modding_games_build.py` shows of it; `docs/modding/packages.md` (installing through the
  library).
- **M3:** `docs/PROJECT-STRUCTURE.md` (`launcher/`).
- **M4:** `docs/platform-portability.md` (packaging); `THIRD_PARTY_NOTICES.md`; `docs/backlog.md` (the launcher
  item under "Accepted future work"; the devkit item's pointer); the roadmap's pointer to this spec.

# Modding guide

This directory is the modder's documentation for Open Maid Engine. It covers writing mods in Open Maid Script (OMS),
packaging them as Open Maid Packs (OMP), and running them in the Godot port and in the original `AGE.EXE`.

The guides are the same for every supported game. Where a guide writes `<profile>`, use your game's profile id.
Game-specific facts (install options, fingerprints, worked-example values, quirks) live only in the game guides:

- [games/README.md](games/README.md): the supported games, each with a generated identity block;
- `games/<profile>.md`: one guide per game.

## What a mod can do

A mod is one package with a `manifest.toml`. Version 1 supports four kinds of content, in any combination:

- **Patches:** small edits to an original script that leave the rest of it untouched. Examples are inserting a
  dialogue page, changing one operand, or replacing a range of instructions. Several mods can patch one script
  as long as their edits do not overlap ([patching.md](patching.md)).
- **New scripts:** whole scripts written in OMS. They live in the mod's own *append catalog*, as the games' own
  DLC does. The first one runs at boot, which is how a mod edits data tables. A new script can also be a full scene
  the player saves in ([new-scenes.md](new-scenes.md)).
- **Script replacements:** a whole compiled `.BIN` that replaces an original script. Two mods replacing the same
  script conflict, so prefer patches ([packages.md](packages.md)).
- **Assets:** files that override an original asset by name, or new assets that a mod's scripts use.

Format version 2 adds **language packages**: translation catalogs and fonts that present the game's text in another
language without changing a script ([languages.md](languages.md)). They run in the port only.

Version 1 does not support runtime scripting (Lua), engine extensions, or patching code another mod inserted.
`../backlog.md` and the mod loader design's future work list what may come later.

## Port and native

Every version 1 mod targets **native** AGE bytecode. The port and the original engine run the same result:

- **In the port,** mods load at startup from the profile's `mods/` folder or from `--mod PATH`. The port links them,
  caches the result and stamps each save with the mod list ([cli.md](cli.md)).
- **In native `AGE.EXE`,** `omp export` writes the linked result as plain game files: loose scripts and
  `APPENDnn.AAI`/`.ALF` catalogs. You copy those into a copy of the install. Native AGE has no mod loader of its
  own, so it does not check versions or stamp saves.

Test a mod in the port first: its link report names every conflict, while native AGE fails less helpfully.

## Where to go next

| You want to… | Read |
|---|---|
| make a first mod, end to end, on either game | [first-mod.md](first-mod.md) |
| know every manifest field and folder | [packages.md](packages.md) |
| learn the language | [oms-language.md](oms-language.md) |
| edit an original script without replacing it | [patching.md](patching.md) |
| add a scene, a boot-time data edit, or new assets | [new-scenes.md](new-scenes.md) |
| translate a game | [languages.md](languages.md) |
| look up a command or a port flag | [cli.md](cli.md) |
| understand an error or a crash | [troubleshooting.md](troubleshooting.md) |
| find your game's options, values and quirks | [games/README.md](games/README.md) |

Working examples live in `oms/examples/mods/` (see its `README.md`). Every one of them links, and the gates that
proved the loader run them in the port and in native AGE.

## Ground rules

- **Never modify the original install.** Mods load from the profile's data folder. Native tests use a copy
  (`tools/native_scratch.py`), never the game directory.
- **A fragment is tied to exact bytes.** Each patch names the SHA-256 of the script it edits. An install whose
  scripts a translation or update replaces is a different base, and needs its own fragment.
- **Text in scripts must be encodable in CP932** (Shift-JIS), since that is the games' only text encoding. English,
  Japanese and full-width characters work. Characters outside CP932 are compile errors. A language package's
  translations are Unicode, because the port draws them itself.

## Where the deeper material lives

These guides are the home for how to make mods. The design records behind them are:

- the language design: `../superpowers/specs/2026-10-06-open-maid-script-design.md`;
- the loader design: `../superpowers/specs/2026-11-03-mod-loader-design.md`;
- the localization design: `../superpowers/specs/2026-11-10-localization-design.md`;
- engine facts: `../engine-re.md` and `../sys4-format-notes.md`;
- the generated opcode reference, `../opcode-reference.md`. Each game's global and patch-point references are
  linked from its identity block in [games/README.md](games/README.md).

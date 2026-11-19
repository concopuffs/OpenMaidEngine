# himegari-base

The engine's base mod for 姫狩りダンジョンマイスター (Lua scripting spec §9,
`docs/superpowers/specs/2026-11-10-lua-scripting-design.md`). The engine ships it and mounts it only when an
enabled mod requires it. A Lua mod reads and writes the game's cells by name through it.

- `manifest.toml` is hand-written. Its major version is the version of the names the base mod exports.
- `layout/` is generated from `vm-map/globals.toml` and `vm-map/patch-points-himegari.toml` by
  `py -3.11 -X utf8 tools/base_mod_build.py --build`. Do not edit it.
- `events/` is hand-written: the game events the base mod declares, with each field's cell by an exported name:
  `experience.toml` (the experience award at `himegari.experience.after_calculation`), `boot.toml`
  (`himegari.data.layered`, once every layer has written its data tables and before the boot uses them, and
  `himegari.data.ready`, once the boot's own work on them is done) and `stage.toml` (`himegari.stage.loaded`, `.ready`, `.round` and `.close`, around
  FIELD's walks of the stage chains the appends register their handlers in).
- `lua/init.lua` is hand-written: the helpers the base mod returns to a mod that requires it, each changing the game
  through one of its own scripts (`give_item` through ADDITEM). `[lua] calls` in the manifest lists those scripts.
- `layout/exports.txt` is the list of exported names, checked in: a name in it stays exported, as a global's name
  or one of its `aliases`, until the major version is raised.

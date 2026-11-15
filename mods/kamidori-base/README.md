# kamidori-base

The engine's base mod for 神採りアルケミーマイスター (Lua scripting spec §9,
`docs/superpowers/specs/2026-11-10-lua-scripting-design.md`). The engine ships it and mounts it only when an
enabled mod requires it. A Lua mod reads and writes the game's cells by name through it.

- `manifest.toml` is hand-written. Its major version is the version of the names the base mod exports.
- `layout/` is generated from `vm-map/globals-kamidori.toml` and `vm-map/patch-points-kamidori.toml` by
  `py -3.11 -X utf8 tools/base_mod_build.py --build`. Do not edit it.
- `layout/exports.txt` is the list of exported names, checked in: a name in it stays exported, as a global's name
  or one of its `aliases`, until the major version is raised.

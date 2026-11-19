# kamidori-base

The engine's base mod for 神採りアルケミーマイスター (Lua scripting spec §9,
`docs/superpowers/specs/2026-11-10-lua-scripting-design.md`). The engine ships it and mounts it only when an
enabled mod requires it. A Lua mod reads and writes the game's cells by name through it.

- `manifest.toml` is hand-written. Its major version is the version of the names the base mod exports.
- `layout/` is generated from `vm-map/globals-kamidori.toml` and `vm-map/patch-points-kamidori.toml` by
  `py -3.11 -X utf8 tools/base_mod_build.py --build`. Do not edit it.
- `events/` is hand-written: the game events the base mod declares, with each field's cell by an exported name:
  `experience.toml` (the experience award at `kamidori.experience.after_calculation`), `boot.toml`
  (`kamidori.data.layered`, once every layer has written its data tables and before the boot uses them, and
  `kamidori.data.ready`, once the boot's own work on them is done) and `stage.toml` (`kamidori.stage.loaded`,
  `.ready`, `.round` and `.close`, around FIELD's walks of the stage chains the appends register their handlers in;
  `kamidori.stage.loaded` is raised too where INFO's third tab, アイテム情報, runs a floor's loaders again for its
  獲得地点 view, and where the stage list runs the loaders of the stage under its cursor for its preview).
- `lua/init.lua` is hand-written: the helpers the base mod returns to a mod that requires it, each changing the game
  through one of its own scripts (`give_item` through ADDITEM). `[lua] calls` in the manifest lists those scripts.
- `lua/tables.lua` is hand-written: the handlers of the `kamidori.tables` format (data patches spec §6,
  `docs/superpowers/specs/2026-11-17-data-patches-design.md`), which the manifest's `[[accepts]]` declares.
  `apply_boot`, bound at `kamidori.data.layered`, checks a data file's document against the record map and writes its
  boot tables, and `apply_stage`, bound at `kamidori.stage.loaded`, writes its part for the stage whose loaders have
  run (`stage.<id>.enemy`), both through `ome-records`, the library the manifest requires. The modder's guide to the
  format is `docs/modding/data-files.md`.
- `lua/generated/tables.lua` is the record map, generated with `layout/` by `tools/base_mod_build.py --build` from
  the registry's `[[table]]` and `[[scope]]` entries and the profile's saved prefix, with the old names of tables
  and fields. Do not edit it. `lua/tables.lua` hands the applier `maid.log.warn`, so a file that writes an old name
  gets a line for it in `lua.log`.
- `shapes/tables.schema.json` is the kind's shape (data patches spec §7), generated with the record map by
  `tools/base_mod_build.py --build`, and named by the manifest's `[[accepts]] shape`. The link checks each data file
  of `kamidori.tables` against it before any code runs; what it cannot say (text code page 932 cannot encode, one
  cell named twice through an old name, a part at an event this install does not raise) `lua/tables.lua` checks at
  the file's first delivery. Do not edit it.
- `layout/exports.txt` is the list of exported names, checked in: the globals, then the record tables
  (`table NAME`) and their fields (`field TABLE.FIELD`), then the scopes (`scope NAME`) and their tables' short
  names (`scope NAME.SHORT`). A name in it stays exported, as a global's name or one of its `aliases`, a table's
  name or one of its `aliases`, or a field's name or an old name in its table's `[table.field_aliases]`, until the
  major version is raised; a scope and a short name have no old-name form.

# ome-records

A Lua library the engine ships beside the base mods (data patches spec §6.4,
`docs/superpowers/specs/2026-11-17-data-patches-design.md`). It checks and applies edits to record tables that a game
keeps as parallel arrays, the code the base mods' `<game>.tables` formats share. It names no game and no table: a base
mod passes its record map and its own access in. The engine mounts it when a mounted base mod requires it.

- `manifest.toml` is hand-written. It has no `[game]`, so it is a library (Lua scripting spec §6.4): no capability,
  no hooks, no events, and nothing it writes on its own.
- `lua/init.lua` is hand-written. It returns `records.applier(config)`, whose `check(contribution)` and
  `apply(contribution, options)` a base mod's `lua/tables.lua` calls:

  ```lua
  local records = require("ome-records")
  local applier = records.applier {
      map = require("generated.tables"),  -- the base mod's record map (tools/base_mod_build.py)
      table = maid.names.table,           -- the base mod's own accessors
      labels = { "boot" },                -- the built_by labels the base mod applies at an event of its own
  }
  applier:apply(contribution, { built_by = "boot" })
  ```

  A contribution is `{ mod = id, file = path, data = document }`. `apply` checks the whole document once per Lua
  session, before its first write: every table, id, field, column and value against the record map, and each string
  with `maid.text_fits`. A document with a problem writes nothing and raises one error naming the mod, the file and
  each problem's path; each later `apply` of it returns 0 and says nothing. A document that passes is written: the
  parts whose table `built_by` names `options.built_by`. `apply` returns the number of cells written.
- A document may be a Lua table or a read-only view of one (a userdata): the library walks it with `pairs` and
  indexing only.

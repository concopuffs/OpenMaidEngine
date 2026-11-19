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
      labels = { "boot", "stage" },       -- the built_by labels the base mod applies at an event of its own
      events = {                          -- optional: the event each label is applied at
          boot = "<game>.data.layered", stage = "<game>.stage.loaded",
      },
      log = maid.log.warn,                -- optional: takes the line for each old name a file uses
  }
  applier:apply(contribution, { built_by = "boot" })
  applier:apply(contribution, { built_by = "stage", scope = { stage = 12 } })
  ```

  A contribution is `{ mod = id, file = path, data = document, unraised = events }`. `apply` checks the whole document once per Lua
  session, before its first write: every table, id, field, column and value against the record map, and each string
  with `maid.text_fits`. A document with a problem writes nothing and raises one error naming the mod, the file, the
  number of problems and the first 20 by path, the first of them on the error's first line (the line a notice and a
  halt reason show); each later `apply` of it returns 0 and says nothing. A document that passes is written: the
  parts whose table `built_by` names `options.built_by`, at the document's top level, or with `options.scope` the
  parts of each scope's id it gives. `apply` returns the number of cells written.
- **Scopes** (data patches spec §6.2). A record map may key tables by a scope of the game (`scopes = { stage = {
  first = 1, last = 999, tables = { enemy = "stage_enemy" } } }` beside `tables`). A document reaches such a table
  only through its scope, by a decimal id in the range and the table's short name: `stage["12"].enemy["3"].unit_id`.
  Its own name at the top level is a problem that gives that spelling, and so are an id outside the range
  (`stage.0: stage has ids 1 to 999`) and a short name the scope lacks (`stage.12.enemyy: stage has no table enemyy
  (tables: enemy)`). An `apply` without `scope` writes none of a scope's parts.
- **Events.** With `events`, a part whose table's label is applied at an event the contribution's `unraised` lists is
  a problem at its path, `stage.12.enemy: stage_enemy is applied at <game>.stage.loaded, which this install does not
  raise`, so the document is refused whole rather than applied in part. A document without such a part is applied.
- A document may be a Lua table or a read-only view of one (a userdata): the library walks it with `pairs` and
  indexing only.
- **Old names.** A record map may give old names of its tables (`old_tables = { old = "current" }` beside `tables`)
  and of a table's fields (`old_fields` in the table). A document that names a table or a field by one writes the
  cell its current name does, and `log` gets one line for each file and old name in the session:
  `example.a data/a.toml: units is an old name of the table unit; write unit`. A document that names one cell twice,
  by an old name and the current one, has a problem, and nothing of it is written.

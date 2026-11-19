# Data files

A data file is a TOML file a mod ships under `data/`. It holds values, not code: the mod that defines its **kind**
reads it and decides what the values mean. A base mod defines a kind for its game's record tables, so a mod can
change a unit's stats or an item's price with a file like this one and no Lua at all:

```toml
[unit.12]                  # the record id is a placeholder; the game's guide has real ones
experience_reward = 999
```

Data files need format version 4 and run in the port only, as Lua does. Each game's base mod accepts `<game>.tables`
(below, "The record tables"); its tables and fields are in that game's guide (`games/<game>.md`).

## Words

- **Kind:** a named format for data files, such as `<game>.tables`. One mod declares it and gives it its meaning.
- **Owner:** the mod that declares a kind, with `[[accepts]]`.
- **Contributor:** a mod that ships a file of a kind, with `[[data]]`.
- **Document:** a data file's content, as the owner's handler reads it.

## Shipping a data file

```text
example.tougher-slimes/
├── manifest.toml
└── data/
    └── slimes.toml
```

```toml
omp = 4
id = "example.tougher-slimes"
name = "Tougher slimes"
version = "1.0.0"

[game]
profile = "<game>"
target = "extended"            # a package with data files targets extended

[[requires]]
id = "<game>-base"             # the kind's owner
version = ">=1.1, <2"

[[data]]
kind = "<game>.tables"         # a kind that a mod of the stack accepts
file = "slimes.toml"           # a path under data/
priority = 0                   # optional; where the file comes among the event's handlers
```

- **Format version 4** (`omp = 4`) and **`target = "extended"`**. `omp export` refuses the package, and a player
  profile in Compatibility mode refuses it ([packages.md](packages.md)).
- **Require the owner.** A file of a kind reaches only the mod that defines the kind, so the contributor requires it,
  as a mod that subscribes to an event requires the event's owner. An owner may ship files of its own kind without
  requiring itself.
- **`file`** is a path under `data/`, with forward slashes. It may name a file in a subfolder, it ends in `.toml`,
  and no part of it is empty, `.` or `..`.
- **Every file under `data/` is named by one entry.** A file that no entry names is an error, since it would do
  nothing without a word. A mod may ship several files, of one kind or of several.
- **No `[lua]` is needed.** The package may still hold Lua, patches, scripts and assets.
- **A package without `[game]` holds no `data/`.** A kind is one game's.
- **Bounds.** A package names at most 64 data files, and a file is at most 16 MiB.

## The file

- **TOML 1.0, in UTF-8.** A key or a table given twice is an error, as TOML has it. So is one thing TOML takes: an
  array of tables that another table interrupts (`[[rows]]`, `[other]`, `[[rows]]`). The engine's TOML library would
  keep only the tables after the interruption, so an array's tables come together, each followed by its own
  sub-tables.
- **Values are strings, booleans, integers and floats.** An integer of more than 2^53 in size, a float that is not
  finite (`inf`, `nan`), and a date or a time are errors: the owner's Lua cannot hold them as they were written.
- **An error names the line** where the parser gives one, and otherwise the path to the value:
  `data/slimes.toml:3: the integer 18446744073709551615 is past 2^53 in size, …`, or
  `data/slimes.toml: unit.12.when: a date or a time has no value here`. The link reports the first 20 problems of a
  file and counts the rest.
- **Keys are strings.** `[rows.12]` has the key `"12"`. What a key means is the kind's.
- **Bounds.** A document holds at most 400,000 values, where each string, boolean and number counts one (each
  element of an array too) and so does each table and array, an empty one included: `[rows.12]` with one key under it
  is three, the tables `rows` and `12` and the value. This count is the document's size in the report, the budget
  and the shape's check below. A document nests tables and arrays at most 16 deep, and its keys are at most 128
  characters, with none that does not print: no control or format character (Unicode Cc and Cf, such as U+200B or
  U+202E), no U+2028 or U+2029, and no half of a surrogate pair. A string value may hold them.

## The record tables: `<game>.tables`

Each game's base mod accepts `<game>.tables`: edits to the game's record tables. It applies the boot's tables at
`<game>.data.layered`, which comes once every layer has written them (the base game's, then each append's) and
before the boot's own work reads them, so an edit lands where an append's edit would. It applies a stage's part at
`<game>.stage.loaded`, each time the stage's loaders have written its tables (below). The game's guide lists its
tables, the fields a file cannot set, and what the game does with an edit. In the examples here, as at the top, the
ids are placeholders, and a message's names and numbers are a game's: the game's guide has real ones.

```toml
[unit.12]                    # a table, then a record id
stats.max_hp = 150           # one column of a field with a row for each record, by its name
experience_reward = 999      # a field with one value for each record
```

- **The top level names a record table** of the game, or a scope (below), and the keys below it are the table's
  fields, by the names the global reference's "Tables" section gives. They are the names OMS uses:
  `unit[12].stats.max_hp` is `unit.12.stats.max_hp`.
- **The record id is decimal,** with no sign and no leading zero, and inside the table.
- **A field with a row takes its columns,** by name where the reference names the column, and by number only where it
  has none.
- **Each cell has one spelling.** A hexadecimal id (`unit.0xc`), a named column given by its number
  (`stats.0`) and an array for a whole row are refused with the spelling to use:
  `unit.12.stats: takes a table, not an array (unit.stats: <global>, a row of <N> columns by name; write
  stats.<column> = ...)`, which names the field's global and its width. So two files that set one cell share a path
  in the report.
- **A value follows its cell:** a whole number from -2147483648 to 4294967295, a float, or a string that the save's
  code page (932) can encode.
- **Every edit is an assignment.** A later file's value replaces an earlier one's, so the mod later in the load
  order wins.
- **A field the save holds is refused,** by name: a load would bring the saved value back over the edit.
- **A stage's table is named through its stage.** The stage's loaders write the enemy table afresh for each stage,
  so a file gives each stage's slots under its id, and the table's short name, which the reference's "Scope stage"
  lists:

  ```toml
  [stage.12.enemy.3]           # the scope, a stage id, the table's short name, then a slot
  unit_id = 40
  ```

  The stage id is decimal, from 1 to 999; an id that no stage has is taken and never applied. The table's own name
  at the top level (`stage_enemy.3`) is refused with this spelling. The base mod writes stage 12's part at
  `<game>.stage.loaded` each time that event is raised for stage 12: at the stage's start, before the game places
  the enemies, and each time the game runs the stage's loaders again during it, so the edit holds through the stage.
  A boot's part and a stage's may share a file.
- **A file with a part at an event this install does not raise is refused whole,** rather than applied in part. When
  `<game>.stage.loaded` is not raised (a script revision its point does not name), a file with a stage part applies
  nothing, and says why: `stage.12.enemy: stage_enemy is applied at <game>.stage.loaded, which this install does not
  raise`. A file of the boot's tables alone is applied.
- **Most mistakes stop the link.** The base mod ships the kind's shape (below, "Shapes"), so a table, field or
  column it does not have, a saved field, an id out of range, a hexadecimal id and a value of the wrong kind are link
  errors, with the file's line, in `omp check` and before the game starts. The check does not look under a key it
  refuses, so a mistake inside `[unit.0xc]` shows once the id is written in decimal:

  ```text
  error: [example.a] data/a.toml:3: unit.0xc: a key here is written in decimal: 12
  error: [example.a] data/a.toml:7: unit.12.experiance_reward: no such key; the nearest name here is experience_reward
  ```

- **A file with a problem the shape cannot say applies nothing.** The base mod checks the whole file at its first
  delivery in a session, before it writes, and `diagnostics/lua.log` lists each problem by its path, the first on the
  first line:

  ```text
  example.a data/a.toml: 1 problem, so nothing of it is applied: unit.12.display_name: a string cell holds text the save can encode (code page 932); U+00E9 is not in it
  ```

  The notice and the halt reason show that first line after the file's name, so they name the first problem's path
  too: `Lua: example.a data/a.toml failed: 1 problem, so nothing of it is applied: unit.12.display_name: …`. The
  file's later deliveries in the session pass it over, with no second error. `--lua-errors halt` ends the run at the
  first such file.
- **The tables are not saved.** A load builds them again and raises `<game>.data.layered` again while it restores,
  and a return to title does the same, so the file is applied again each time. A stage's table lies outside the save
  too: a load of a save made on the stage runs its loaders again, and the stage part is applied again. A save made
  with the mod and loaded without it has the game's own values. What the game itself copies from an edited field into
  saved state stays in the save; the game's guide names such fields.
- **An old name keeps working.** When the base mod renames a table or a field, a file may still use the old name
  until the base mod's next major version. It writes the cell the current name does, and `diagnostics/lua.log` says
  so once a session: `example.a data/a.toml: units is an old name of the table unit; write unit`. A file that names
  one cell by its old name and its current one is refused. A scope's name (`stage`) and a table's short name in it
  (`enemy`) have no old names: they do not change inside a major version.
- **A Lua handler on the same tables.** A hook on `<game>.data.layered` runs among the files, in load order. A hook on
  `<game>.data.ready` runs after every file, whatever the load order, because that event comes later in the boot. A
  mod whose edits should take their place among the files by load order subscribes to `<game>.data.layered`. A hook
  on `<game>.stage.loaded` runs among the files' stage parts, in load order, in the same way.

## Defining a kind

A mod with Lua and a `[game]` table can define a kind. A library cannot, as it declares no event.

```toml
[[accepts]]
kind = "example.lists"                 # named as an event is; one mod of a stack declares it
description = "Lists of names."
shape = "lists.schema.json"            # optional: a file under shapes/ that the link checks each file against

[[accepts.at]]                         # one or more
event = "example.lists.ready"          # an event the owner could subscribe to: its own, or a required mod's
handler = "lists.lua:apply"            # a file under lua/, then a top-level function of it
```

- **Each `at` names an event the owner could subscribe to:** one it declares, or one a mod it requires declares.
  `handler` is a top-level function of a file under `lua/`, as a hook's is ([lua.md](lua.md)).
- **The handler's file is loaded only in a stack that holds a file of the kind.** An owner with no contributor loads
  and runs what it did before.
- **A kind is unique in a stack.** Two mods that declare one kind are a hard conflict.
- **A kind is part of its owner's API.** Adding to a format is a minor version of the owner; taking a field away,
  or changing what a value means, is a major one.
- **`shape`** names a file under `shapes/` that describes the kind's files (below, "Shapes"). The link checks each
  file of the kind against it before any code runs, so a mistake it describes stops the load with the file's line.
  Without a shape, only the owner's handler checks a file, at its first delivery.

### The handler

The handler named by an `at` entry takes the event and the contribution, once for each file at each raising of the
event:

```lua
function apply(event, contribution)
    -- event:                 the event's context, as any subscriber gets it
    -- contribution.mod:      "example.tougher-slimes", the contributor
    -- contribution.file:     "data/slimes.toml", the path in its package, as the report and the log print it
    -- contribution.data:     the document
    -- contribution.unraised: the kind's events this install does not raise, in the order of the at entries; usually empty
end
```

- **The contribution is read-only,** and so is every table of the document: a write raises "a data file's document
  is read-only".
- **It is not a Lua table.** `type()` gives `"userdata"`. It answers indexing, `#`, `pairs` and `ipairs`; code that
  uses `next` or the `table` functions on it cannot be relied on. `pairs` gives the keys in an order that repeats
  between runs and need not be the file's, so a format that needs an order uses an array.
- **One contribution for each file and session.** Every delivery of a file in a session gets the same one, so the
  handler can tell a file it has seen.
- **An integer is a Lua number,** exact up to 2^53 in size.

## Shapes

A shape is a JSON file under the owner's `shapes/`, in a small part of JSON Schema (draft-07), so that an editor that
checks TOML against JSON Schema can read the same file:

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "properties": {
    "names": { "type": "array", "items": { "type": "string" }, "description": "The names, in order." },
    "level": { "type": "integer", "minimum": 1, "maximum": 99 },
    "seen": { "type": "null", "description": "seen is kept by the game: a file does not set it" },
    "by_id": { "$ref": "#/definitions/by_id" }
  },
  "additionalProperties": false,
  "definitions": {
    "by_id": {
      "type": "object",
      "x-ome-decimal-keys": { "minimum": 1, "maximum": 999 },
      "additionalProperties": { "type": "string" }
    }
  }
}
```

- **The keywords:** `type`, `properties`, `additionalProperties`, `items` (one schema for every element), `enum`
  (strings, numbers and booleans, so a table or an array fails it), `minimum` and `maximum` (inclusive), `oneOf`, `description`, and `$ref`, which
  names `#/definitions/NAME` of the same file and takes only a `description` beside it. `$schema` and `definitions`
  are at the top. `integer` takes `5.0` too, as JSON Schema has it.
- **`x-ome-decimal-keys`** is the engine's own: an object's keys that `properties` does not name are whole numbers in
  decimal, with no sign and no leading zero, from `minimum` to `maximum`, and `additionalProperties` checks their
  values. An editor passes over a keyword it does not know, so give the values under `additionalProperties`, which it
  does check.
- **A key the format does not offer** is `{ "type": "null", "description": WHY }`. TOML has no null, so no value
  passes, and the link's error is the description: `data/a.toml:3: seen: seen is kept by the game: a file does not
  set it`. Such a key is never suggested as a nearest name.
- **Anything else is an error of the owner,** at the link: a keyword outside this list (`required`, `pattern`,
  `x-taplo`, …), a `$ref` to another file or to no definition, a chain of `$ref` that comes back to itself, a range
  whose minimum is above its maximum, a property's name longer than a key may be (128 characters), or a file that is
  not JSON. The report line names the owner, the shape and the
  place in it as a JSON pointer: `accepts[0] example.lists: shapes/lists.schema.json: #/properties/level/pattern: the
  keyword pattern is outside the shape subset (…)`. The kind's files are then not delivered.
- **The files.** `shapes/` holds `*.json` files, at paths with forward slashes and no empty, `.` or `..` part, each at
  most 4 MiB. Each one is named by an `[[accepts]] shape`, or it is an error, since it would do nothing.
- **An error of a file** names its contributor, then the file, the line, the path and the reason:
  `data/a.toml:4: t.kk: no such key; the nearest names here are k and j`. The nearest names are the names the shape
  offers there that are closest in spelling, three at most; a name that starts with the key, or that the key starts
  with, is close when the shorter has three characters or more (`min` gives `min_level`, `unit` gives `unit_id`).
  With none close, the error lists the names there. A key that should be a whole number says how: `by_id.0x10: a key
  here is written in decimal: 16`. A value of the wrong kind where a table or an array goes adds the shape's
  description of what goes there: `names: takes an array, not a string (The names, in order.)`. The link reports the
  first 20 problems of a file and counts the rest.
- **What a shape cannot say, the owner still checks.** A shape describes a document's keys and values; the owner's
  handler checks the rest at the first delivery, and everything when its kind has no shape. `<game>.tables` checks
  three such things there: text the save's code page cannot encode, one cell named twice through an old name, and a
  part at an event this install does not raise (above).
- **In an editor.** `omp lua-stubs --output DIR` ([cli.md](cli.md)) copies each accepted kind's shape into `DIR` as
  `<kind>.schema.json`, the game's `<game>.tables` included, and prints a line to put first in a data file of the
  kind: `#:schema file:///C:/path/to/DIR/<game>.tables.schema.json`. An editor that checks TOML against JSON Schema
  (Taplo, and the editor extensions built on it) then marks a misspelt key, a key the format does not offer, and a
  value out of range or of the wrong kind as you type. It does not read `x-ome-decimal-keys`, so it takes any key
  where a record id, a stage, a slot or an unnamed column goes (`unit.0xc`, `unit.1000`, `stage.1.enemy.30`), and
  its messages are its own (`1 is not of type "null"` for a saved field). The link refuses those keys, and its
  messages give the reason and the spelling to use, so run `omp check` before you ship.
- **Work.** A check applies at most 64 schemas for each value of a file, each table and array counting one, and
  100,000 more. A shape like the base mods' applies four to six to each, so no file inside the bounds reaches the
  limit with it, empty tables included; only a shape built to cost time does, and the error is its owner's.

## What the link checks

The link checks every data file before any mod code runs, and so do `omp check` and `omp build`
([cli.md](cli.md)). Each of these stops the load, with a report line that names the mod and the file:

- no mod of the stack accepts the file's kind;
- the contributor does not require the kind's owner;
- the file does not read (a syntax error or a key given twice, with its line), holds a value the owner's Lua cannot
  hold, or passes a bound;
- the file does not match its kind's shape (above), with its line, the path and the nearest names;
- none of the kind's events is raised in this install (below).

A file under `data/` that no entry names, a file past 16 MiB, and `[[data]]` in a package without `[game]` or with
`target = "native"` stop the load sooner, when the package is opened.

The owner's own mistakes are its link errors: a kind two mods declare (a hard conflict naming both), an `at` event no
mod declares or the owner cannot subscribe to, a handler that is not a top-level function, and a shape that is
broken or uses a keyword outside the subset.

**An event this install does not raise.** A base mod declares its events over the game's scripts, and an event is not
raised when one of its points does not bind: another revision of a script, or a point the game does not name. Such
an event loses its deliveries and nothing else. The report says so once, as a note naming the kind, the event, the
reason and the files, and the kind's other events deliver as planned. The owner's handler finds the missing events in
the delivery's `unraised` list and decides what that means for a file. When none of a kind's events is raised, its
files fail to link.

## The report

The link report's Lua section lists each kind with its owner and its events, then its files in delivery order with the
values each holds (each table and array counting one, as in "The file"), then each path that two files give a value
at:

```text
kind <game>.tables (<game>-base), at <game>.data.layered and <game>.stage.loaded
  example.tougher-slimes data/slimes.toml: 3 values
  example.hard-mode data/hard.toml: 212 values
  unit.12.experience_reward: example.tougher-slimes, then example.hard-mode (shared path, in delivery order)
```

- **A path** is the keys from the top of the file down to a value that is not a table, joined by dots; an array is one
  path. A key that is not a bare TOML key is printed quoted.
- **A shared path is listed, not judged.** Which value holds is the kind's rule: `<game>.tables` lets the later file
  replace the earlier one; another kind may add values up.
- **Paths compare as text.** The report does not know what a key means, so two spellings of one thing hide an overlap
  from it. `<game>.tables` has one such case: a table or a field the base mod renamed keeps its old name, which a
  file may still write, and a file that writes `units.12.experience_reward` and another that writes
  `unit.12.experience_reward` set one cell with no shared-path line. The later file's value holds, as for any shared
  path, and `diagnostics/lua.log` names each old name a file uses (the game's guide lists the old names).
- At most 20 shared paths are listed for a kind, and the rest are counted. A file is named by its mod, and by its mod
  and file where the mod ships more than one file of the kind.
- Each place the files are delivered also lists its handlers in the order they run, a delivery as
  `example.a data/a.toml (<game>-base tables.lua:apply_boot)`.

## Delivery

- **Where and when.** A file is delivered at each of its kind's events that this install raises, each time the event
  is raised, to the handler its `at` entry names. Data files, like Lua, run in the port only.
- **Order.** At an event, the deliveries and the hooks run in one order: ascending priority, then load order, and at
  one priority a mod's files come before its hooks, in its manifest's order. A file's `priority` places it as a
  hook's places the hook. The report lists the order (above).
- **Which value holds** is the kind's rule. With `<game>.tables`, a later file replaces an earlier one's value, so the
  mod later in the load order wins.
- **An error is the file's.** When the owner's handler fails on a file, `diagnostics/lua.log` names the contributor,
  its file and the owner's handler, then where it ran:

  ```text
  … error example.a: data/a.toml (<game>-base tables.lua:apply_boot) failed at <game>.data.layered at <script>@<offset>: …
  ```

  The game goes on, with a notice. Three failures of one file in a row, at whichever of the kind's events, switch
  that file off for the session ("Lua: example.a data/a.toml failed 3 times in a row and is not applied again this
  session"). The owner's handler goes on with the other files. `--lua-errors halt` ends the run at the first failure
  instead. What a delivery wrote before its error stays. `<game>.tables` checks the whole document before it writes,
  so a file with a problem the check finds writes nothing; a delivery stopped after its first write, by its budget or
  by an error the check does not foresee, keeps what it wrote until the tables are built again.
- **A mod is on or off as a whole.** A contributor that is off for the session (its own Lua failed, or a mod it
  requires is off) has no deliveries, and a contributor with no Lua is off when a Lua mod it requires is off. When the
  owner is off, so is every mod that requires it. The notice of a mod that is off ends with the files it leaves
  unapplied: `Lua: <game>-base is off for this session: …; not applied: example.a data/a.toml, …`.
- **Budget.** A delivery may run the handler budget, 1,000,000 Lua instructions, and 250 more for each value of its
  document, each table and array counting one. The allowance is one constant of the engine; a kind has no setting for it.

## The trace

With `--lua-trace` ([lua.md](lua.md), "The link report and the trace"), a delivery's line is the contributor's: `mod`
is the contributor, with `file` and the kind's `owner`, and `handler` is the owner's.

```json
{"mod":"example.a","file":"data/a.toml","owner":"<game>-base","handler":"tables.lua:apply_boot","hook":"<game>.data.layered","kind":"event","at":"<script>@<offset>","ms":0.162,"instructions":0,"budget":1000750,"result":"ok","writes":[…]}
```

- **A write is the contributor's,** with `through` naming the owner, as a base mod's helper's write names the base
  mod for the mod that called it.
- **A watch on a cell the file sets** names the contributor as the writer: `"writer":"mod","by":"example.a"`.
- **`budget`** is the delivery's: the handler budget and the allowance for the document's values, its tables and
  arrays counted. `instructions` counts in steps of 1,000 ([lua.md](lua.md)), so a file of a few values reads 0; one
  of every item field of a game runs about 100 for each value, and a file of empty tables about as much for each of
  them.

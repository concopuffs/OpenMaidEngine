# Data patches design (draft)

**Date:** 2026-11-17
**Status:** draft for review. Nothing in it is built. It sits in the repository root for the review. Once accepted
its home is `docs/superpowers/specs/2026-11-17-data-patches-design.md`, with a row in the documents map of
`AGENTS.md`. On 2026-11-18 the user decided the first three questions (§14.1), and the boot readings of §6.5 were
made. Five smaller questions are open (§14.2).
**Checked against:** `9041dde9` on `develop`, and the boot readings against `3f793be0`; no file this draft cites
changed between the two. Code references name files and members, not lines. Paths are relative to `age-reimpl/`; a
short path is under `engine/`.
**Direction:** `docs/remake-architecture-and-roadmap.md` §3.2 ("post-initialization patches") and §3.8 (base mods);
the mod loader design's future work (`docs/superpowers/specs/2026-11-03-mod-loader-design.md` §12, "Declarative data
patches"); and the rule the user set on 2026-11-16: the engine knows nothing about a game, also where two games
happen to work alike.
**Builds on:** the Lua scripting design (`docs/superpowers/specs/2026-11-10-lua-scripting-design.md`, cited below as
"Lua §n"). This draft adds to it and changes none of its rules.

**It replaces one sentence.** The mod loader design describes this feature as "named post-initialization edits to
registry tables without a script" (its §12). Here a data file is written in a format that a base mod defines, and
that base mod's Lua applies it. The loader and the linker do not know what a table is.

**Not verified.** Each is work for the plan:

- which fields of each registry table the game changes while it runs, beyond those the registry's own text names
  (§6.5);
- what applying a large file costs, in time and in Lua instructions (§5.6);
- the boot readings of §6.5 in a run. They are read from the disassembly. No edit has been made at the two new
  points yet, at a boot or at a load;
- whether Kamidori lets a save be made on a floor by a route other than FIELD's own save command. Such a save
  would load without `kamidori.stage.loaded` being raised (§6.5). The user accepted this as a gap on 2026-11-18,
  and the backlog has it.

## 1. Purpose and scope

### 1.1 The problem

Neither game has data files. Every table is bytecode that writes constants into the global bank, at boot or when a
stage is entered (`docs/kamidori-data-tables.md`, "How the data gets into memory"). A mod that changes a unit's hit
points has two routes today:

| Route | How | What it costs |
|---|---|---|
| A boot script | the mod's record 0 runs at INIT2 through op `0x143` and edits tables with OMS `with` blocks (`oms/examples/kamidori-gate6/`) | a selector, a script to write, and it reaches boot tables only |
| A Lua handler | a handler on `<game>.data.ready` or `<game>.stage.loaded` writes through the accessors, with the names in `[lua] writes` (`oms/examples/mods/example.random-enemies`) | port only, code to write, and the author must know when each table is ready and which cells a load brings back |

The cheapest kind of mod is missing: a balance change stated as data. It has no code, a tool can read and write it,
and the loader can show it in its report.

### 1.2 The rule

**What a data patch means belongs to the game's base mod. The engine only delivers it.**

A data patch needs four pieces of knowledge, and all four are a game's:

- what the data is: which tables exist, and what a record's fields are;
- where it lives: which cells hold a field;
- when it can be changed: after the game built it and before the game reads it;
- what else must follow: values the game derives from it, and fields that are saved state and must be left alone.

Himegari and Kamidori answer these in similar ways. A third game may not. So none of the four goes into the engine,
the loader or the linker. They go into `mods/<game>-base/`, as generated data and hand-written Lua.

### 1.3 The shape

A data mod is a manifest and a TOML file. It has no Lua, and it names no address, script or event:

```toml
# example.tougher-slimes/manifest.toml
omp = 4
id = "example.tougher-slimes"
name = "Tougher slimes"
version = "1.0.0"

[game]
profile = "kamidori"
target = "extended"                # port only (§5.8)

[[requires]]
id = "kamidori-base"
version = ">=1.1, <2"

[[data]]
kind = "kamidori.tables"           # a format kamidori-base defines
file = "slimes.toml"               # data/slimes.toml
```

```toml
# example.tougher-slimes/data/slimes.toml: read by kamidori-base, never by the engine
[unit.761]                         # the unit oms/examples/kamidori-gate6 edits
stats.max_hp = 150                 # 12
experience_reward = 999            # 10
drop_chance.drop_1 = 100           # 30
```

The base mod says that it accepts the format, and which of its functions receives a file at which of its events:

```toml
# mods/kamidori-base/manifest.toml (added)
[[accepts]]
kind = "kamidori.tables"
description = "Edits to the game's record tables: units, items, skills, and each stage's enemies."

[[accepts.at]]
event = "kamidori.data.layered"    # a new event; §6.2 says why it is not data.ready
handler = "tables.lua:apply_boot"

[[accepts.at]]
event = "kamidori.stage.loaded"
handler = "tables.lua:apply_stage"
```

The engine's whole part is to read the file, check that it is well formed, and call that function with it at that
event. It calls it at the place in the handler order where a handler of `example.tougher-slimes` would run, and the
trace names `example.tougher-slimes` for the writes.

### 1.4 What this design delivers

| Part | Where | Knows the game |
|---|---|---|
| Data files and kinds: a package ships data for another mod's Lua, and the engine delivers it in order (§5) | the engine and the loader | no |
| The `<game>.tables` kind: the format, and the Lua that applies it (§6) | each base mod | yes |
| The code the two base mods share (§6.4) | a first-party Lua library | no: a base mod passes its tables in |
| Checks of a file's fields before the game starts (§7), as a later step | the loader, from a description the base mod ships | no |

### 1.5 Not in this design

- **Native export.** A data mod is port only. The boot script of §1.1 stays the route that native AGE runs (§11).
- **Relative edits and selectors:** "twice the hit points", "every enemy of this race". The first format assigns
  values to records named by id (§6.1).
- **New records.** A file can fill an unused id's fields. Whether the game then lists the record is that table's
  own business, and no rule here hands out free ids between mods.
- **Saved state.** Fields the game changes and saves are not offered (§6.2). "The player starts with ten potions"
  is a different edit, made once at a new game, and stays Lua.
- **Every save path.** A data file is applied again whenever the game builds the table again and the base mod's
  event is raised. This design adds no mechanism of its own for saves. Where a game's load skips the step that
  raises the event, the edit is missing until the game next builds the table. Such a case is a known gap, kept in
  the backlog, and the design is not bent around it (§14.1). §6.5 has the one found so far.
- **How a string that a data file sets meets a language package.**
- **A Lua function that applies the same format** for Lua mods (§6.6 says why).

## 2. What the code gives to build on

| Fact | Where | Consequence |
|---|---|---|
| Every table is written by bytecode. Boot tables are built by INIT2's base scripts and then by each append's record 0 (op `0x143`). Others are built on demand, the stage's among them | `docs/kamidori-data-tables.md`; Lua §11 | a patch is writes to cells, after the scripts that build them |
| Himegari's INIT2 runs op `0x143` at `0x17f`, calls TUNE at `0x180` and exits at `0x183`, which is the point `himegari.boot.data_ready`. TUNE reads item data and corrects saved state against it (§6.5) | `build/games/himegari/disasm/INIT2.asm`, `TUNE.asm`; `vm-map/patch-points-himegari.toml` | an edit made at `data.ready` comes after that correction |
| Kamidori's INIT2 runs op `0x143` at `0x20c` and exits at `0x2cf`, the point `kamidori.boot.data_ready`. Between them it fills unset text colour settings from their defaults, redraws the field when a save made on a floor is loaded, and sets up the studio's ornaments (§6.5) | `build/games/kamidori/disasm/INIT2.asm`; `vm-map/patch-points-kamidori.toml` | an edit made at `data.ready` comes after that redraw has read unit and item fields |
| The data tables are not saved. A save holds the saved cells and the stack of scripts, with one resume marker for each. A load does not put execution back where it stopped: it re-enters each saved script at its entry and runs it forward to its op `0xae`. So the prologue runs again, INIT2 included, and `<game>.data.ready` is raised again while it restores | Lua §11, "What a full load runs"; `mods/<game>-base/events/boot.toml` | the game rebuilds its tables at a load, and an edit is made again by the same event as at boot. It must give the same result each time |
| FIELD runs the stage's loaders, and raises `<game>.stage.loaded`, whenever `field_map_continue_mode` is not 1: at a stage's start, back from the camp screens, and at the load of a save made in the field. At 1 it resumes a field whose tables are still in memory | `docs/modding/games/<game>.md`; `mods/<game>-base/events/stage.toml` | a stage edit is made again each time the loaders write the stage again |
| The registries group parallel arrays as record tables: Himegari has `unit`, `item` and `skill`; Kamidori has those and `stage_enemy`. OMS reads them: `with unit[761] { .stats.max_hp = 150 }` | `[[table]]` in `vm-map/globals[-<game>].toml`; OMS spec §5.3; `Ome.Script/Modules/GameModule.cs` | the record vocabulary exists, and the decompiler already prints it |
| A record table mixes authored fields with state: Kamidori's `item` has `inventory_count`, `growth_exp` and `growth_level`, and Himegari's `skill` has `acquired_flag` | the same `[[table]]` entries | the registry does not mark which is which |
| A base mod's layout holds the globals (name, bank, address, shape, columns, aliases), not the record tables | `Ome.Engine/Modding/GameLayout.cs`; `tools/base_mod_build.py` | Lua has no map from `unit.stats` to `unit_base_stats` today |
| An event's subscribers run in one list, by priority and then load order. The link builds the list as `PlannedHandler(Handler, Priority, LoadOrder, …)` | `Ome.Engine/Modding/HookPlan.cs`; `ModLuaLinker.Link` | a delivery can be one more planned handler |
| A mod may subscribe to an event it declares itself, or one whose owner it requires | `ModLuaLinker.Link` | a base mod's own functions can be bound to its own events |
| The trace and a watch record name the mod of the innermost running handler. A base mod's helper writes for the mod that called it | `Ome.Engine/Natives/Scripting.Trace.cs`; Lua §12 | a delivery has to run for the mod that shipped the file |
| A handler that fails three times in a row is switched off for the session | Lua §5.5 | a delivery must be counted by file, not by function |
| A handler call may run 1,000,000 Lua instructions | `ModLimits.HandlerInstructions` | a large file needs its own allowance (§5.6) |
| Lua cannot read a file: there is no `io`, and `require` finds only the mod's own `lua/` and the mods it requires | Lua §5.3, §5.4 | data reaches Lua only when the engine hands it over |
| The link runs no mod code | Lua §5.5, §14.2 | a check on a file before the start has to be data (§7) |
| A file outside the package layout is a note. `lua/` holds only `*.lua`, and `layout/` only its three generated files | `Ome.Mods/Packages/ModLayout.cs` (`Classify`) | `data/` is a new folder, and generated Lua sits under `lua/` |
| The manifest is at format version 3, and the reader rejects unknown keys | `ModManifest.CurrentOmpVersion`; `ManifestReader` | the new tables take version 4 |
| The Compatibility mode refuses a package that targets `extended`, and one that requires a base mod | `Ome.Mods/Launch/ModeCheck.cs` | a data mod is port only by the rules that exist |
| The loader reads TOML with Tomlyn | `Ome.Mods/Ome.Mods.csproj` | no new dependency |
| The engine ships every package under `mods/` inside its build. It mounts one when an enabled mod requires it, and then the shipped packages that one requires in turn | `Ome.Mods/Packages/ShippedPackages.cs` (`Required`); `Ome.Mods/Ome.Mods.csproj` | a first-party library ships the same way, and a base mod can require it |

## 3. Decisions

1. **The engine delivers, and the owner decides what the data means.** The engine parses a data file into plain
   values and calls a function of the mod that defined its format. It never reads a key. The same holds for the
   loader, the linker and the tools.
2. **A format is declared as data, and its handlers are ordinary handlers.** A mod's manifest says which kinds it
   accepts, at which events, and with which top-level functions. The link checks all of it before any mod code runs,
   as it checks a hook (Lua §3, decision 2).
3. **A data file runs where a handler of its own mod would.** Load order then means one thing. A later mod wins,
   whichever of the two forms each mod used, and a mod that turns its Lua into a data file keeps its place.
4. **A data mod has no code and no access.** Its effect is what the owner's handler does with its values. The owner
   runs with its own access, as a base mod's helper does (Lua §9.3).
5. **A check before the start is data too.** The owner may ship a description of its format, and the link checks a
   file against it (§7). The link never runs the owner's Lua to check a file.
6. **The format for these two games follows the registry's record tables.** A file names `unit.761.stats.max_hp`
   because OMS names `unit[761].stats.max_hp`. A modder learns one vocabulary, and the decompiler already prints it.
7. **Facts about a game's data stay in its registry. What to do with them stays in its base mod.** Which fields of
   a table are state, and which part of the game builds the table, are facts. When to apply an edit and what to
   derive again afterwards is behaviour, written in Lua.
8. **What the two games share is a library, not engine code.** A Lua library package with no game holds the code
   that walks a file and writes records. Each base mod passes its tables and its access in (Lua §6.4).
9. **Port only.** Native AGE cannot run the base mod's Lua. The boot script stays the native route.
10. **The first format only assigns.** A value replaces the cell's value. An assignment gives the same result each
    time it is made, which a load and a stage entry need (§2). Anything that reads the old value, or selects records
    by a rule, is Lua for now.

## 4. Terms

- **Data file:** a TOML file that a package ships under `data/` and names in its manifest.
- **Kind:** a named format for data files. One mod declares it and gives it its meaning.
- **Owner:** the mod that declares a kind.
- **Contributor:** a mod that ships a data file of a kind.
- **Document:** a data file's content as plain values, after parsing.
- **Delivery:** one call of an owner's handler with one document, at one raise of an event.
- **Record table:** the registry's `[[table]]`: parallel arrays that hold one kind of record (OMS spec §5.3).
- **Record map:** a base mod's generated list of its record tables, with the array behind each field.
- **Applier:** the base mod's Lua that turns a document into writes.

## 5. The engine's part: data files and kinds

Nothing in this section names a table, a record or a game.

### 5.1 Shipping a data file

```text
<mod>/
├── manifest.toml
└── data/                TOML files, each named by a [[data]] entry
```

```toml
[[data]]
kind = "kamidori.tables"   # a kind that a mod of the stack accepts
file = "slimes.toml"       # a path under data/
priority = 0               # optional, as a hook's (Lua §6.2)
```

- **Format version 4.** `[[data]]` and `[[accepts]]` need `omp = 4`. The reader still takes versions 1 to 3.
- **The package has `[game]` and names `target = "extended"`,** as a package with Lua does (Lua §6.2). `omp export`
  refuses it.
- **The contributor requires the owner,** as an event's subscriber requires the event's owner.
- **Every file under `data/` is named by one entry.** A file that no entry names is an error, because a file that
  does nothing without saying so is a trap. A mod may ship several files, of one kind or of several.
- **The package needs no `[lua]` table.** It may still hold Lua, patches, scripts and assets.
- **A package without `[game]` holds no `data/`.** A kind is one game's.

### 5.2 Accepting a kind

```toml
[[accepts]]
kind = "kamidori.tables"
description = "Edits to the game's record tables."
shape = "tables.schema.json"       # optional, a file under shapes/ (§7)

[[accepts.at]]
event = "kamidori.data.layered"
handler = "tables.lua:apply_boot"  # a file under lua/, then a top-level function (Lua §6.2)
```

- **A kind is named like an event** and is unique in a stack. Two mods that declare one kind are a hard conflict.
- **Each `at` names a game event that the owner could subscribe to,** its own or one of a mod it requires, and a
  handler of the owner. A kind has at least one.
- **The owner ships Lua and has `[game]`.** A library declares no kind, as it declares no event (Lua §6.4).
- **A kind is part of its owner's API.** Additions to a format are a minor version of the owner. Taking a field
  away, or changing what a value means, is a major one.

The handler takes the event and the contribution:

```lua
function apply_boot(event, contribution)
    -- event:               the event's context, as any subscriber gets it
    -- contribution.mod:    "example.tougher-slimes"
    -- contribution.file:   "slimes.toml"
    -- contribution.data:   the document, read-only
end
```

### 5.3 The document

- **A data file is TOML 1.0 in UTF-8.**
- **It becomes plain values.** A table is a Lua table with string keys, and an array is a table from 1 to n. Strings,
  booleans, integers and floats are themselves. An integer past 2^53 in size, a float that is not finite, and a date
  or time have no value here and are link errors.
- **Keys are strings,** as TOML's are. `[unit.761]` has the key `"761"`. What a key means is the owner's.
- **The owner cannot change it.** The document is a read-only view, built the way an export's is (Lua §5.2). It is
  built once per session and handed to every delivery.
- **Order.** Keys are inserted in the file's order. Lua §5.1 promises that iteration repeats between runs, and not
  that it follows insertion. A format that needs an order uses an array.
- **Bounded:** the file's size, its nesting depth and its count of values. Milestone 1 sets the numbers from a file
  that fills a whole table (§13).

### 5.4 The link

The Lua link (`ModLuaLinker`) already reads the events before it checks the hooks. It gains three steps:

1. **Read the kinds.** Each `[[accepts]]` is checked as §5.2 says. A broken one is its owner's link error.
2. **Read the data files.** For each `[[data]]` entry: a mod of the stack accepts the kind, the contributor
   requires that mod, the file parses, and the document is inside the bounds and has only values Lua can hold. With
   a shape, the document is checked against it (§7). An error names the contributor, the file, and the line where
   the parser gives one.
3. **Plan the deliveries.** For each event of the kind and each of its documents, the event's list gets one more
   planned handler: the owner's function, the entry's priority, and the contributor's load order.

- No mod code runs.
- The link cache keeps the documents with the plan. A package's hash is in the cache key already.
- **An event that is not raised in this install** (Lua §9.2: a point that does not bind) fails the contributors of
  a kind delivered at it, as it fails a subscriber. The engine cannot tell whether a document needs that event. An
  owner that wants the two parts to stand alone declares two kinds.

### 5.5 Delivery and order

- **At each raise of an event that a kind names,** every document of the kind is delivered once: the owner's handler
  runs with the event's context and the contribution.
- **Its place** is in the event's one list (Lua §7.4): ascending priority, then the contributor's load order.
- **Within one mod at one priority,** its data files come first, in manifest order, and then its hooks. A mod's own
  handler then sees its own file applied.

```text
kamidori.data.layered
   -10  example.early    lua    early.lua:on_data
     0  example.a        data   data/a.toml    (kamidori-base tables.lua:apply_boot)
     0  example.a        lua    a.lua:on_data
     0  example.b        data   data/b.toml    (kamidori-base tables.lua:apply_boot)
```

- **The handler runs as the owner:** in the owner's environment, with the owner's `maid.names` and `maid.vm`. The
  contributor gains nothing.
- **It runs for the contributor.** The trace, a watch record and an error name the contributor (§5.7).
- **It is a handler in every other way.** It runs to completion (Lua §3, decision 4), and it may call the scripts
  that the owner lists under `[lua] calls` (Lua §10).
- **During a restore** deliveries happen as handlers do (Lua §11). The boot's events are raised again by the
  prologue, with `maid.session.restoring` true.
- **An owner can bracket the edits.** A delivery may be bound to an event that the owner raises from Lua
  (`maid.events.emit`, Lua §8.2). The subscribers and the deliveries run inside the `emit`. What the owner's code
  does after the `emit` returns therefore comes after every edit. That is where an owner derives values again.

### 5.6 Errors and budgets

- **An error in a delivery is the contribution's.** The log names the contributor and its file, then the owner's
  handler, the script and offset, and the traceback. The game continues, and `--lua-errors halt` ends the run
  (Lua §5.5).
- **Failures are counted by file.** Three failures in a row switch off that file's deliveries for the session. They
  do not switch off the owner's function for other mods' files.
- **What a failed delivery already wrote stays,** as for any handler. An owner that wants all or nothing checks the
  document before its first write. The library does (§6.4).
- **The owner is off** when its `init.lua` failed (Lua §5.5). Its deliveries then do not run, and the notice names
  the files that were not applied.
- **Budget.** One delivery may run the handler budget plus an allowance for each value of its document. A file that
  sets every field of Kamidori's items, about 1,300 records of several dozen fields each, is unlikely to fit in
  1,000,000 instructions. Milestone 1 measures the allowance.

### 5.7 The report and the trace

The link report's Lua section gains each kind, with its owner, its events, and its documents in delivery order:

```text
kamidori.tables (kamidori-base), at kamidori.data.layered and kamidori.stage.loaded
  example.tougher-slimes   data/slimes.toml   3 values
  example.hard-mode        data/hard.toml     212 values
  unit.761.stats.max_hp: example.tougher-slimes, then example.hard-mode (ordered interaction)
```

- **Two mods that give a value at one path** are an ordered interaction, in delivery order. A path is the keys from
  the root down to a value that is not a table. An array is one value. A long list of shared paths is counted and
  shown in part.
- **Paths compare as text** without a shape: `unit.761` and `unit.0x2f9` are two paths. A shape that says the keys
  are integers makes them one (§7).
- **A data mod is not opaque.** The report can list everything it ships.
- **What the report cannot show:**
  - what the owner's handler does with a value;
  - a data file and a Lua write that reach one cell. The trace shows it;
  - a data file and a boot script's edit of one cell. The boot script runs first, inside op `0x143`, and the trace
    does not record bytecode.
- **The trace** (`--lua-trace`) writes one line for each delivery: the contributor and its file, the owner's
  handler, the event, the script and offset, the duration and every write. A write is the contributor's, as a
  helper's write is its caller's (Lua §12).

### 5.8 Saves, modes and tools

- **The stamp.** A package with `data/` changes content, so it is never marked presentation-only
  (`ModLayout.ChangesNoContent`).
- **State.** A data mod has no `maid.state`, so it adds no block to the state file.
- **Modes.** The Compatibility mode refuses the package by the rules it has: the package targets `extended`, and it
  requires a base mod (`ModeCheck`).
- **`omp check` and `omp build`** run the checks of §5.4. `omp build` refuses a package whose data file fails them.
  When the stack that `omp build` links against lacks the kind's owner, the file is checked for syntax and bounds
  only, and a note says so. A base mod is always there, because the engine ships it.
- **A changed data file needs a restart,** as a changed manifest or event file does (Lua §13). A reload would not
  apply a boot edit anyway, because the boot's events are not raised again until a load.
- **The guide.** `docs/modding/` gains a generic page for data files. Each game's page lists that game's kinds,
  tables and fields.

## 6. The game's part: record tables in the base mods

Everything in this section is a base mod's choice. It can change without engine work, and another game's base mod
can do something else with the same mechanism.

### 6.1 The format of `<game>.tables`

```toml
[unit.761]
stats.max_hp = 150               # one column of a row field, by its name
experience_reward = 999          # a field with one value for each record
drop_chance.drop_1 = 100

[item.14]                        # the ids 14 and 12 below are placeholders
max_range = 3

[stage.12.enemy.3]               # stage 12, enemy slot 3 (§6.2, "Scoped tables")
unit_id = 761
min_level = 10
max_level = 12
```

- **The top level names a record table** of the game's registry (`unit`, `item`, `skill`), or a scope (`stage`).
- **The next key is the record's id,** in decimal or as `0x` and hexadecimal. It must be inside the table.
- **Fields are the table's fields, by the registry's names.** They are the names OMS uses: `unit[761].stats.max_hp`
  is `unit.761.stats.max_hp`.
  - A field with one cell for each record takes a value.
  - A field with a row for each record takes a table of columns, by name or by number, or an array that gives the
    whole row.
- **Values follow the cell's rules** (Lua §8.1): a whole number in 32 bits, a float, or a string that CP932 can
  encode.
- **Every edit is an assignment.**
- **A file with an error applies nothing.** The applier checks the whole document at its first delivery of a
  session, the stage parts included, so a mistake in stage 12's entry is reported at boot and not when the player
  reaches stage 12.
- **Names as keys are left out.** `[unit."緑プテテット"]` would read better, but several records can share a name,
  and two spellings of one record would hide an overlap from the report. They are future work (§11). A comment
  holds the name meanwhile.

### 6.2 What the base mod decides

**When.** Each table has a moment after the game built it and before the game reads it. The base mod binds each
part of the format to an event of its own.

- **Boot tables.** `<game>.data.ready` exists, but it comes after the boot's own work with the tables (§2). An edit
  made there is not seen by that work. The proposal is a second event, `<game>.data.layered`, at the instruction
  after op `0x143`: every layer has written its data, and the boot has not used it yet.
  - An edit made there lands where an append's edit lands. The game's own work then runs over it, as it runs over
    its appends' edits.
  - `data.ready` stays, for a mod that wants to run after the boot's own work.
  - Each game needs a new patch point for it (`vm-map/patch-points-<game>.toml`): `INIT2@0x180` in Himegari and
    `INIT2@0x20d` in Kamidori. §6.5 reads what follows each, and neither boot writes a record table after it.
- **Scoped tables.** A stage's enemy table is rewritten by the stage's loader each time FIELD runs the loaders. The
  file keys such a table by its scope: `stage.12.enemy.3`. The applier writes it at `<game>.stage.loaded` when the
  event's `stage` is 12, and writes it again each time the event is raised, as `example.random-enemies` does by hand.

**What is not offered.** A field that the game changes and saves is state. A load would bring the saved value back,
and the edit would then replace it at every load: an inventory count would reset each time. The applier refuses such
a field by name and says why.

**What follows an edit.** If a game derives values in a way that no event placement avoids, the base mod brackets
the edits (§5.5) and derives again after them. Neither game needs it at boot once the event sits before the boot's
own work (§6.5).

**Bounds.** The applier refuses a record id outside its table and a column outside its row. The accessors would
refuse them too (`docs/modding/lua.md`, "Game names"). Checking first is what makes a bad file apply nothing.

### 6.3 The record map, and two facts for the registry

The applier needs the record tables in Lua: `unit.stats` is `unit_base_stats`. Lua cannot read the layout's JSON.

- **`tools/base_mod_build.py` writes a Lua module,** `mods/<game>-base/lua/generated/tables.lua`, from the
  registry's `[[table]]` entries: each table's record count, and each field's array. Column names need no copy,
  because the accessors already know them.
  - It is the one generated file under `lua/`. The generated-files table in `AGENTS.md` gains it, and the
    `validate.py` gate that checks `layout/` checks it too.
  - No engine change is needed for it.
- **The registry gains two facts for each table,** which it holds only as prose today:
  - which fields are state. Kamidori's `item` would list `inventory_count`, `growth_exp` and `growth_level`;
  - which part of the game builds the table: a label such as `boot` or `stage`. The base mod's Lua maps a label to
    its event.

  The generator copies both into the record map. The engine reads neither.
- **Table and field names become API.** A mod's file names them, so the rename rule covers them (Lua §9.1): they
  join `layout/exports.txt`, and a rename keeps the old name or raises the base mod's major version. The registry's
  tables have no aliases today, so the generator has to add them.

### 6.4 The shared library

Both games keep record tables in parallel arrays, so both appliers do the same walk: check the document against the
record map, then write each value through an accessor. That code goes into a first-party Lua library with no game,
here called `ome-records` (Lua §6.4). The engine ships it beside the base mods.

```lua
-- mods/kamidori-base/lua/tables.lua: a sketch, and the names are suggestions
local records = require("ome-records")

local applier = records.applier {
    map = require("generated.tables"),          -- the record map (§6.3)
    table = maid.names.table,                   -- the base mod's own access, passed in (Lua §6.4)
    scoped = { stage = { enemy = "stage_enemy" } },
}

function apply_boot(event, contribution)
    applier:apply(contribution, { built = "boot" })
end

function apply_stage(event, contribution)
    applier:apply(contribution, { built = "stage", stage = event.stage })
end
```

- **The library holds no game fact.** It gets the record map, the access and the scopes from its caller.
- **A third game with another data model does not use it.** Its base mod writes its own handlers, or another
  library, behind the same `[[accepts]]`.
- **Shipping it needs no engine change.** `Ome.Mods` embeds every folder under `mods/`. `ShippedPackages.Required`
  mounts a shipped package that an enabled mod requires, and then the shipped packages that one requires. So
  `ome-records` is mounted when a base mod that requires it is. A package without `[game]` is already a library to
  the loader (`ManifestReader`; `ModLuaLinkTests`).

### 6.5 The two games

| | Himegari | Kamidori |
|---|---|---|
| Record tables | `unit` (1,000), `item` (1,000), `skill` (300) | `unit` (1,000), `item` (2,000), `skill` (1,000), `stage_enemy` (30) |
| State that the registry's text names | `skill.acquired_flag`, and the per-unit story flags among `unit`'s fields | `item.inventory_count`, `item.growth_exp`, `item.growth_level`, and the per-unit runtime arrays among `unit`'s fields |
| After the layers, before `data.ready` | TUNE (`INIT2@0x180`) | INIT2's own work, `0x20d` to `0x2cf` |
| The new boot point | `INIT2@0x180` | `INIT2@0x20d` |
| The stage's enemies | the `stage_enemy_*` arrays, 30 slots. The registry has no table for them yet | the `stage_enemy` table |
| Stage event | `himegari.stage.loaded` | `kamidori.stage.loaded` |

**What each boot does after the layers.** Read from the disassembly on 2026-11-18, and not yet run with an edit in
place.

- **Himegari's TUNE corrects saved state against the item data.** It writes no field of a record table.
  - For each item whose `item_category` is 10 or more, it lowers each saved tuning level to the last level that the
    item's curve gives a bonus for. It reads `item_tuning_curve_ids` and `item_tuning_stat_bonus_curves`. The levels
    are `G[0x3ebe]`, ten for each item, which the registry does not name yet.
  - When `G[0x767b]` is set, it runs CALCREVISE for each live entity. CALCREVISE reads item, skill and unit fields:
    `item_stat_modifiers`, `item_granted_skill_id`, the range fields, `unit_default_attack_item_id`.
  - It sets `G[0x617fd]` to 30 when it is 0.
  - It raises each `item_inventory_counts` cell to the number of party slots that name the item in `G[0x6868]`,
    which the registry does not name either.

  An edit at `INIT2@0x180` therefore needs nothing after it, and TUNE corrects a save against the edited data, as
  it does against an append's. An edit at `data.ready` comes after that correction: a saved tuning level could
  then lie past a curve that a file shortened.
- **Kamidori's INIT2 writes no record table after the layers.**
  - It copies `voice_setting_default_text_colors` into each unset text colour setting (`G[0x3225]`) and stores the
    setting in the shared profile. That table is not a record table.
  - When `G[0x428c]` is set, it rebuilds the field for a load. FIELD sets that flag on entering a floor
    (`FIELD@0x1fd`). INIT2 then runs the stage loaders and calls SETMAPTEX, SETHALLTEX, SETOBJ, DRAWOBJ and
    DRAWCHALL. Of the record tables, SETMAPTEX reads `unit.canonical_character_id` and, through READICON,
    `unit.graphic` and `item.costume_graphic_asset_ids`. None of them reads the `stage_enemy` table.
  - It calls SETSTUDIOORN, which reads `studio_room_ornaments`.

  An edit at `INIT2@0x20d` is therefore not overwritten, and the redraw at a load reads the edited fields. An edit
  at `data.ready` comes after that redraw.
- **Kamidori: a stage edit after a load.** FIELD's own save command sets `field_map_continue_mode` to 2 around SAVE
  (`FIELD@0x28cb`). A save made with it loads with 2, so FIELD runs the loaders again and raises `stage.loaded`
  (`docs/modding/games/kamidori.md`). In ordinary play the mode is 1 (`FIELD@0x144d`). A save made on a floor by
  another route would load with 1: INIT2 has run the stage loaders itself, FIELD does not run them, and
  `stage.loaded` is not raised. The stage's tables would then lack every mod's stage edit, a Lua handler's as much
  as a data file's.
  - Whether the game offers such a route is not known. Neither is whether the game reads the enemy table again
    after it has placed the enemies.
  - **It is an accepted gap** (§14.1), and the backlog has the item ("Mod loader and modding"). It does not hold up
    a milestone.
  - If it is closed later, `kamidori.stage.loaded` takes a second point after INIT2's loader loop (`INIT2@0x2b3`).
    That is a change to the base mod's event file and the patch points, not to the engine.
  - Himegari's INIT2 runs no stage loader.

What still has to be read before each base mod offers a table:

- **Both:** each table's fields, sorted into authored data and state. The table above names some. The rest needs
  the executed boot image (`tools/init_image.py`) and the save layout.
- **Himegari:** a `stage_enemy` table in the registry.
- **Kamidori keeps a character's persistent stats apart from the unit's definition**
  (`docs/kamidori-data-tables.md`, "Runtime character state"). An edit to a definition then reaches a character
  who has already joined only as far as the game reads the definition again. The game's guide has to say this for
  each such field.

### 6.6 Lua mods and the same tables

A Lua mod keeps what it has: it writes through the accessors and lists the names (Lua §9.3). It gains the new boot
event, which is the right moment for its own edits too. It may also ship a data file beside its Lua.

**The base mod does not export the applier as a function.** A helper's writes are the base mod's (Lua §9.3). They
are not in the calling mod's write list, and the link report does not show them. That is right for `give_item`,
which makes one change the way the game does. A function that writes any field of any table would let a Lua mod
write most of the game's data unlisted, and the report would no longer say which mod changes what. A data file has
no such gap: the report lists everything it holds.

## 7. Shape checks

Without a shape, a misspelt field is found when the file is first delivered: at boot, in `diagnostics/lua.log`, with
a notice. That is early, but it is after `omp build`, and the player sees it and not the author. A shape moves the
check to the link.

- **A shape is a description of a kind's documents, shipped by the owner as data:** a file under `shapes/`, which
  `[[accepts]] shape` names. The link checks each document against it and runs no code.
- **The proposal is a small subset of JSON Schema:** `type`, `properties`, `additionalProperties`, `items`, `enum`,
  `minimum`, `maximum`, `oneOf`, `description`, and `$ref` inside the file.
  - Editors that check TOML against JSON Schema (Taplo does) then complete and check a data file as it is typed,
    from the same file the link uses.
  - One keyword of our own says that a table's keys are integers in a range. The link then reads `761` and `0x2f9`
    as one key, in its checks and in the report's paths (§5.7).
  - A shape that uses a keyword outside the subset is its owner's link error, so no check is skipped without notice.
- **For a base mod the shape is generated** from the record map by `tools/base_mod_build.py`, state fields left out.
- **An error names** the contributor, the file and line, the path, and the nearest names the shape allows.
- **`omp build` refuses** a package whose file fails its kind's shape.
- **The owner's handler still checks** what a shape cannot say, and what an owner without a shape must check.

This is milestone 3. The first two milestones work without it.

## 8. Engine changes

| Part | Change |
|---|---|
| `Ome.Mods` | `data/` and `shapes/` in the package layout (`ModLayout`); `[[data]]` and `[[accepts]]` in the manifest, with format version 4; the document reader over Tomlyn, with its bounds; the three link steps (§5.4); the report's kinds and shared paths; the documents in the link cache; a package with `data/` is not presentation-only; later, the shape check |
| `Ome.Engine` (`Modding/`) | the document as plain values, with no TOML type in it; a planned handler may carry a contribution; the runtime stack lists the contributions |
| The scripting native (`Natives/Scripting*.cs`) | a delivery runs for its contributor in the trace, the watch records and the log; failures are counted by file; the budget's allowance |
| `Ome.Engine.Vm` | none beyond passing the contribution with the context |
| `Ome.Scripting.Lua` | the document as a read-only Lua value, built once per session; the handler's second argument |
| Tools | `base_mod_build.py` writes the record map and, later, the shape; the two registry facts (§6.3); the exported-names check covers table and field names |
| `mods/` | `ome-records`; in each base mod `[[accepts]]`, `lua/tables.lua`, `lua/generated/tables.lua`, the new event and its patch point, and version 1.1.0 |
| Tests | `ProjectBoundaryTests`: `Ome.Engine` and `Ome.Engine.Vm` reference no TOML library; the gates of §12 |

Nothing here reads an engine build's name or a profile's id.

## 9. Untrusted input

Packages are untrusted (mod loader spec §11; Lua §15).

- **A data file is data.** It is parsed, bounded and handed over as values. Nothing in it is executed.
- **It reaches only its kind's owner,** and only as a read-only value. It cannot name a function, a cell or a file.
- **Its effect is bounded by the owner's handler.** A base mod's applier writes only the fields it offers, inside
  the tables' bounds.
- **A hostile file can still cost time.** Hence the bounds of §5.3 and the budget of §5.6.
- **A third-party owner** runs its own Lua on other mods' data, with its own access. The link report already shows
  that mod's capabilities (Lua §15).

## 10. Alternatives considered

| Alternative | What it gave | Why not |
|---|---|---|
| **The linker lowers edits to bytecode** at a point that each registry table declares (the first sketch of this feature) | native export, and conflicts found cell by cell at link | the linker would know when a game's tables are ready, which cells a save holds, and how a stage guards its loader. That is these two games' shape inside the engine |
| **Lua only:** a mod calls a base mod function with a Lua table from its own handler | no engine change at all | every data mod needs Lua and a hook, and its author must know the event; the link sees nothing of it; it is the unlisted write of §6.6. Milestone 0 still uses this shape inside the base mod to prove the applier |
| **Pull:** the owner asks for every file of its kind and applies them in one handler of its own | a smaller engine part | every file would run at the owner's place in the order, before or after every Lua handler whatever the load order, and each write would be the owner's in the trace |
| **The engine applies records itself,** from tables in the layout | less Lua in the base mods | "records keyed by id, built at boot" would become an engine concept. It is how these two games work |
| **The owner's Lua checks a file at link** | checks as rich as code | the link would run mod code, which Lua §5.5 rules out for the link and for `omp check` |

## 11. Future work

- **Relative edits:** `{ add = 5 }` and `{ scale = 1.5 }` in place of a value, so that two balance mods compose
  where they now replace each other. Each needs a rounding rule, and bounds for each field. It is a new minor
  version of a base mod's format and needs nothing from the engine.
- **Selectors and names:** records chosen by name or by a rule. The applier can look a name up in the game's own
  name table when it runs, so an append's records are found too.
- **Native export of a data file.** For a format that maps onto registry tables, a tool could turn a file into a
  boot script of `with` blocks, which native AGE runs. It would be a conversion in the toolchain, owned by the
  format, and not a second way to apply a file in the port.
- **Record accessors for Lua mods:** `game.record("unit")[761].stats.max_hp`, built by the engine from the record
  map and checked against the write list as the other accessors are.
- **A table printed as a data file.** `Ome.Cli oms-decompile` already prints each unit as a `with` block. The same
  walk could print a table in this format, as the starting point of a balance mod.
- **Kinds delivered at engine events,** such as `ome.session.start`.
- **Reloading a data file while the game runs,** for the scoped tables.

## 12. Validation gates

Every gate except the driven ones runs in `Ome.Engine.Tests`, without Godot.

| Gate | Passes when |
|---|---|
| 1. Off means off | a stack with no `[[data]]` entry links and runs as it does today, and an event with no kind does no new work |
| 2. Link | each of these stops the load with a report that names the mod and the file, and no mod code has run by then: a kind that no mod accepts; an owner that the contributor does not require; a file under `data/` that no entry names; a TOML syntax error, with its line; a date; an integer past 2^53; a file past a bound; a kind declared twice, naming both owners; a handler that is not a top-level function; an event its owner cannot subscribe to; `[[data]]` in a package without `[game]`, or with `target = "native"` |
| 3. Order | two data mods and a Lua mod on one event run in the order the report gives; swapping the two data mods in the load order swaps which value holds; a mod's file is applied before its own handler at the same priority |
| 4. Attribution | the trace line of a delivery names the contributor, its file and the owner's handler; a watch on a cell the file sets names the contributor; three failures of one file leave another mod's file delivered |
| 5. Same cells | the file of §1.3 leaves `unit[761]`'s three fields as the `with` block of `oms/examples/kamidori-gate6` leaves them, in a headless boot |
| 6. Loads | the edit holds after boot, after a load, and after a return to title; a save made with the mod and loaded without it shows the game's own values; the decoded save made right after boot is the same with and without the mod |
| 7. Errors | a misspelt field is reported with the contributor, the file and the path; nothing of that file is applied, the stage parts included; `--lua-errors halt` ends the run |
| 8. Stage | a stage's enemy edit changes what the stage places, holds when FIELD is entered again and after the load of a save made with the field's save command, and leaves another stage as it was. Driven on the port, on both games |
| 9. Not shaped by records | a test mod accepts a kind whose documents are a list of strings and receives them by the same path; the test stack has no record table in it |
| 10. Size | a file that sets every authored field of the game's largest table is applied inside the measured budget at boot and at a load, and the numbers are recorded |
| 11. Second game | both base mods accept their kind with no difference in engine code |
| 12. Shape (milestone 3) | a misspelt field fails `omp check` with the file, the line and the nearest names, and `omp build` refuses the package; `761` and `0x2f9` are one path in the report; a shape with a keyword outside the subset is its owner's link error |
| 13. Layers | `ProjectBoundaryTests` pass, and neither `Ome.Engine` nor `Ome.Engine.Vm` references a TOML library |

## 13. Milestones

0. **The applier, before the engine's part.** What §6.5 still lists to read. The two registry facts and the record
   map. `ome-records` and each base mod's `tables.lua`. The new boot point and event for each game. Tests call the
   handlers with a Lua table, as a console chunk in the base mod's environment (`IModSession.RunChunk`, Lua §13).
   This settles the format, the timing and the list of state fields on two games. It needs no engine change (§6.4).
   Gate 5, and the applier's half of gates 6 and 7.
1. **Delivery.** Format version 4, `data/`, `[[data]]`, `[[accepts]]`, the document, the link steps, the planned
   deliveries, attribution, failures counted by file, the report and the trace. The bounds and the budget's
   allowance are measured here. The generic guide page. Gates 1 to 4, 6, 7, 9, 10, 13.
2. **Stages, and both games driven.** The scoped tables on both games, an example mod for each game under
   `oms/examples/mods/`, and each game's guide page. Gates 8 and 11.
3. **Shapes.** The schema subset, the generated shapes, integer keys in the report's paths, the `omp build` refusal.
   Gate 12.

When it is built: the backlog's "declarative data patches" line goes, and the mod loader spec stays as it is, with
this spec named as what replaced its sentence.

## 14. Questions

### 14.1 Decided

The user decided these on 2026-11-18.

1. **Port only is acceptable** ("Port only is fine"). A data mod needs the base mod's Lua, so the Compatibility
   mode and `omp export` refuse it, and the boot script stays the native route. The alternative was the linker
   design of §10, which the rule of §1.2 rules out. A later conversion tool (§11) could give native export back for
   formats that map onto registry tables.
2. **How much engine: milestones 0 and 1,** as recommended. Milestone 0 alone already works, with the costs of "Lua
   only" in §10. Milestone 1 is what makes a data mod free of code and visible to the link.
3. **The two facts of §6.3 live in the registry's `[[table]]` entries,** as recommended. They are facts about the
   game's data, and the registry already says them in prose. The alternative was a hand-kept file in the base mod.

Also decided that day, outside the numbered list:

- **A robust system first, and gaps around saves are accepted.** The user: "For now I'd rather focus on a robust
  system and accept certain gaps, particularly around saving." A save path on which an edit is not made again goes
  to the backlog (§1.5). It does not change the design or hold up a milestone.

### 14.2 Open

Each has a recommendation. The numbers continue from §14.1.

4. **The boot moment.** Recommended: the new event after op `0x143`. The readings of §6.5 support it on both
   games: neither boot writes a record table after that point, and each reads the tables there in a way that an
   edit should reach. The alternative is `data.ready`, with the base mod deriving again after the edits.
5. **A library, or the walker copied into each base mod?** Recommended: the library. It keeps the two games'
   likeness out of the engine and in one place, and it gives Lua §6.4 its first real user.
6. **The shape's form.** Recommended: the JSON Schema subset, for the editors. The alternative is a small format of
   our own, in the style of the event declarations. And should it wait for milestone 3? Recommended: yes.
7. **Relative edits in the first format?** Recommended: no (§11).
8. **Names.** `[[data]]`, `[[accepts]]`, "kind", `<game>.tables`, `<game>.data.layered` and `ome-records` are all
   suggestions.

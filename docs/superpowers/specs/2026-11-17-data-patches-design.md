# Data patches design

**Date:** 2026-11-17; accepted 2026-11-18
**Status:** accepted by the user on 2026-11-18, after their own review of the whole text. Nothing in it is built
yet. The work is on `feature/data-patches`, and its plan goes in `docs/superpowers/plans/`.
**How it was decided:** on 2026-11-18 the user decided the eight questions of §14, and the boot readings of §6.5
were made. The same day an agent that had no part in writing the draft reviewed it against the repository. §15
lists what that changed. Four of the changes touched what the user had read or decided. The user accepted them the
same day, and §14 ends with them.
**Checked against:** `9041dde9` on `develop`, with the boot readings made at `3f793be0`. Read again at `b3760c74`
for what the NativeAOT engine changed (§2), and at `d4f3dae9` for the review's findings. Code references name files
and members, not lines. Paths are relative to `age-reimpl/`; a short path is under `engine/`.
**Direction:** `docs/remake-architecture-and-roadmap.md` §3.2 ("post-initialization patches") and §3.8 (base mods);
the mod loader design's future work (`docs/superpowers/specs/2026-11-03-mod-loader-design.md` §12, "Declarative data
patches"); and the rule the user set on 2026-11-16: the engine knows nothing about a game, also where two games
happen to work alike.
**Builds on:** the Lua scripting design (`docs/superpowers/specs/2026-11-10-lua-scripting-design.md`, cited below as
"Lua §n"). This design adds to it and changes none of its rules.

**It replaces one sentence.** The mod loader design describes this feature as "named post-initialization edits to
registry tables without a script" (its §12). Here a data file is written in a format that a base mod defines, and
that base mod's Lua applies it. The loader and the linker do not know what a table is.

**Not verified.** Each is work for the plan:

- which saved values the game derives from the fields a file can set, beyond the two cases §6.5 names. Such a
  value stays in a save after the mod is gone (§1.5);
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
- **Saved state.** Fields that a save holds are not offered (§6.2). "The player starts with ten potions" is a
  different edit, made once at a new game, and stays Lua.
- **Undoing what the game derived.** A game may copy a table's value into saved state, or correct saved state
  against it. Kamidori's UNITECH copies a unit's base stats into the character's saved stats, and Himegari's TUNE
  lowers saved tuning levels to fit an item's curve (§6.5). Such a value stays in the save when the mod is removed,
  as it stays after an append's edit. The game's guide names the fields this holds for.
- **Every save path.** A data file is applied again whenever the game builds the table again and the base mod's
  event is raised. This design adds no mechanism of its own for saves. Where a game's load skips the step that
  raises the event, the edit is missing until the game next builds the table. Such a case is a known gap, kept in
  the backlog, and the design is not bent around it (§14). §6.5 has the one found so far.
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
| A record table mixes authored fields with saved state. A cell below a profile's saved prefix is kept by a save, and a cell above it is rebuilt at boot. By that test the saved fields are `unit.story_speaker_seen_flag`, `skill.acquired_flag` and `skill.info_revealed_flag` in Himegari, and `item.inventory_count`, `growth_exp`, `growth_level`, `costume_enhancement_grid` and `costume_gem_sockets` in Kamidori. No other field of a table is saved | `docs/sys4-format-notes.md` ("Saved prefix"); `bankDimensions` in `Ome.Engine/Profiles/<game>.json`; the same `[[table]]` entries | which fields are state follows from each field's address, so nothing has to be marked by hand (§6.3) |
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
| The loader reads TOML with Tomlyn, through `TomlDocument.Parse` | `Ome.Mods/TomlDocument.cs`; `Ome.Mods/Ome.Mods.csproj` | no new dependency |
| The engine ships every package under `mods/` inside its build. It mounts one when an enabled mod requires it, and then the shipped packages that one requires in turn | `Ome.Mods/Packages/ShippedPackages.cs` (`Required`); `Ome.Mods/Ome.Mods.csproj` | a first-party library ships the same way, and a base mod can require it |
| An event with a point that does not bind in the install is a note for its owner and not an error: "one script that differs must not stop the mods that use the others". Only a subscriber to that event fails to link | `ModLuaLinker.Events` | a kind's event that is not raised should cost that event's deliveries and no more (§5.4) |
| A string cell refuses a text that CP932 cannot encode, and it does so at the write | `Ome.Engine/Modding/ScriptAccess.cs` (`Text`) | Lua cannot test a string before it writes it (§6.2, "Checks") |
| A table that another mod exports reaches Lua as a read-only userdata. It answers indexing, `#`, `pairs` and `ipairs` | `Ome.Scripting.Lua/LuaViews.cs` | the document is such a view, not a Lua table (§5.3) |
| The trace lists the first 1,000 writes of a call and counts the rest | `Scripting.TracedWritesPerCall` | the line of a large file's delivery does not list every write (§5.7) |
| The engine ships compiled with NativeAOT, and the trim and AOT analyzers' warnings are errors. TOML is parsed through a source-generated context, and JSON is read and written through source-generated contexts | `Ome.Mods/TomlDocument.cs`; `Ome.Mods/ModsJson.cs`; `docs/platform-portability.md`, "Fewer files in a package" | the document reader walks Tomlyn's `TomlTable` by hand; the documents in the link cache need a type in `ModsJsonContext`; a shape is read without reflection |

## 3. Decisions

1. **The engine delivers, and the owner decides what the data means.** The engine parses a data file into plain
   values and calls a function of the mod that defined its format. It gives no key and no value a meaning. The
   one thing it does with keys is compare their paths as text, for the report (§5.7). The same holds for the
   loader, the linker and the tools.
2. **A format is declared as data, and its handlers are ordinary handlers.** A mod's manifest says which kinds it
   accepts, at which events, and with which top-level functions. The link checks all of it before any mod code runs,
   as it checks a hook (Lua §3, decision 2).
3. **A data file runs where a handler of its own mod would.** On one event, load order then means one thing: a
   later mod comes later, whichever of the two forms each mod used. Between events the game's own order holds. A
   boot script runs inside op `0x143`, before both boot events, and a handler on `<game>.data.ready` runs after
   every data file (§6.6).
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
- **Contribution:** what a delivery hands to the handler: the contributor's id, the data file's path and the
  document (§5.2).
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
- **The contributor requires the owner,** as an event's subscriber requires the event's owner. An owner may ship
  files of its own kind without that, as it subscribes to its own events.
- **`file` is a path under `data/`,** with forward slashes. It may name a file in a subfolder, and it may not
  leave `data/`.
- **Every file under `data/` is named by one entry.** A file that no entry names is an error, because a file that
  does nothing without saying so is a trap. A mod may ship several files, of one kind or of several.
- **A package of format version 1 to 3 is read as before.** A `data/` folder in it is outside the layout, and a
  note (`ModLayout.Classify`).
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
- **The handler's file is loaded as a hook's file is** (Lua §6.2), and only in a stack that holds a document of
  the kind. An owner without a contributor loads and runs what it does today.
- **A kind is part of its owner's API.** Additions to a format are a minor version of the owner. Taking a field
  away, or changing what a value means, is a major one.

The handler takes the event and the contribution:

```lua
function apply_boot(event, contribution)
    -- event:                 the event's context, as any subscriber gets it
    -- contribution.mod:      "example.tougher-slimes"
    -- contribution.file:     "data/slimes.toml": the path in the package, as the report and the trace print it
    -- contribution.data:     the document, read-only (§5.3)
    -- contribution.unraised: the kind's events that this install does not raise (§5.4). Usually empty
end
```

The field names are the plan's to settle.

### 5.3 The document

- **A data file is TOML 1.0 in UTF-8.**
- **It is read as plain values.** A table is indexed by string keys, and an array from 1 to n. Strings, booleans,
  integers and floats are themselves. An integer past 2^53 in size, a float that is not finite, and a date or time
  have no value here and are link errors.
- **Keys are strings,** as TOML's are. `[unit.761]` has the key `"761"`. What a key means is the owner's.
- **The owner cannot change it.** The document is a read-only view, built the way an export's is (Lua §5.2). It is
  built once per session and handed to every delivery.
- **A view is not a Lua table.** It is a userdata, and `type()` gives `"userdata"`. It answers indexing, `#`,
  `pairs` and `ipairs`, and a handler walks it with those. Code written for a plain table, with `next` or the
  `table` functions, cannot be relied on with it.
- **Order.** Keys are inserted in the file's order. Lua §5.1 promises that iteration repeats between runs, and not
  that it follows insertion. A format that needs an order uses an array.
- **Bounded:** the file's size, its nesting depth, its count of values, the length of a key, and the number of
  data files that one package names. A value is a string, a boolean or a number, so an array of ten numbers is ten
  values. A key holds no control character, since the report and the log print keys. Milestone 1 sets the numbers
  from a file that fills a whole table (§13).

### 5.4 The link

The Lua link (`ModLuaLinker`) already reads the events before it checks the hooks. It gains three steps:

1. **Read the kinds.** Each `[[accepts]]` is checked as §5.2 says. A broken one is its owner's link error.
2. **Read the data files.** For each `[[data]]` entry: a mod of the stack accepts the kind, the contributor
   requires that mod or is that mod, the file parses, and the document is inside the bounds and has only values Lua
   can hold. With a shape, the document is checked against it (§7). An error names the contributor, the file, and
   the line where the parser gives one.
3. **Plan the deliveries.** For each event of the kind and each of its documents, the event's list gets one more
   planned handler: the owner's function, the entry's priority, and the contributor's load order.

- No mod code runs.
- The link cache keeps the documents with the plan. A package's hash is in the cache key already.
- **An event of a kind that this install does not raise** loses its deliveries and nothing else. An event is not
  raised when one of its points does not bind: another revision of its script, or a point the profile does not
  name (`ModLuaLinker.Events`).
  - The report says it once, as a note: the kind, the event, the reason and the contributors. The kind's other
    events deliver as planned.
  - It is the rule that events follow already (§2). A data file that edits boot tables must not fail because FIELD
    is another revision, when the same edit written as a Lua handler links.
  - The engine cannot tell whether a document needs the missing event. So it tells the owner: the contribution
    lists the kind's unraised events, and the owner's handler decides what that means for a document (§6.2).
  - When none of a kind's events is raised, its contributors fail to link, as a subscriber to such an event does.

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
- **Failures are counted by file.** Three failures in a row, at whichever of the kind's events, switch off that
  file's deliveries at every event for the session. They do not switch off the owner's function for other mods'
  files.
- **What a failed delivery already wrote stays,** as for any handler. An owner that wants all or nothing checks the
  document before its first write. The library does (§6.4). No check covers a stop by the budget, which is why the
  budget has to fit every file inside the bounds (below).
- **A mod is on or off as a whole.** A contributor that is off for the session (Lua §5.5) has no deliveries. When
  the owner is off, every mod that requires it is off too, by the same rule. The notice names the files that were
  not applied.
- **Budget.** One delivery may run the handler budget plus an allowance for each value of its document, counted as
  §5.3 counts values. A file that sets every field of Kamidori's items, about 1,300 records of several dozen fields
  each, is unlikely to fit in 1,000,000 instructions.
  - Milestone 1 measures the allowance, and sets it so that the largest file the bounds admit ends inside its
    budget with the base mods' applier (gate 10).
  - The allowance is one constant of the engine. An owner whose handler costs more for each value has no setting
    for it. If a real kind needs one, the extension is a per-kind allowance under an engine cap.

### 5.7 The report and the trace

The link report's Lua section gains each kind, with its owner, its events, and its documents in delivery order:

```text
kamidori.tables (kamidori-base), at kamidori.data.layered and kamidori.stage.loaded
  example.tougher-slimes   data/slimes.toml   3 values
  example.hard-mode        data/hard.toml     212 values
  unit.761.stats.max_hp: example.tougher-slimes, then example.hard-mode (shared path, in delivery order)
```

- **Two mods that give a value at one path** are listed as a shared path, in delivery order. A path is the keys
  from the root down to a value that is not a table, and an array is one path. A long list of shared paths is
  counted and shown in part.
- **The report does not say which value holds.** That is the kind's rule: `<game>.tables` lets the later one
  replace the earlier, and another kind may add values up or join lists.
- **Paths compare as text.** The engine gives a key no meaning (§3, decision 1). A format that lets one thing be
  spelt in two ways therefore hides overlaps from the report, so a format should have one spelling for each
  thing. `<game>.tables` has (§6.1).
- **A data mod is not opaque.** The report can list everything it ships.
- **What the report cannot show:**
  - what the owner's handler does with a value;
  - a data file and a Lua write that reach one cell. The trace shows it;
  - a data file and a boot script's edit of one cell. The boot script runs first, inside op `0x143`, and the trace
    does not record bytecode.
- **The trace** (`--lua-trace`) writes one line for each delivery: the contributor and its file, the owner's
  handler, the event, the script and offset, the duration and the writes. A write is the contributor's, as a
  helper's write is its caller's (Lua §12). The line lists the first 1,000 writes and counts the rest, as any
  call's line does (§2).

### 5.8 Saves, modes and tools

- **The stamp.** A package with `data/` changes content, so it is never marked presentation-only
  (`ModLayout.ChangesNoContent`).
- **State.** A data mod has no `maid.state`, so it adds no block to the state file.
- **Modes.** The Compatibility mode refuses the package by the rules it has: the package targets `extended`, and it
  requires a base mod (`ModeCheck`).
- **`omp check` and `omp build`** run the checks of §5.4, and `omp build` refuses a package whose data file fails
  them. A contributor requires its kind's owner, so a stack without the owner already fails to link on the missing
  requirement (`StackResolver`), and no data check is reached. A base mod is always there, because the engine
  ships it.
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
- **The next key is the record's id, in decimal,** with no sign and no leading zero. It must be inside the table.
- **Fields are the table's fields, by the registry's names.** They are the names OMS uses: `unit[761].stats.max_hp`
  is `unit.761.stats.max_hp`.
  - A field with one cell for each record takes a value.
  - A field with a row for each record takes a table of its columns. A column is given by its name where the
    registry names it, and by its number only where it has no name.
- **Each cell has one spelling.** A hexadecimal id, a named column given by its number, and an array for a whole
  row are refused, each with the spelling to use. Two files that set one cell then always share a path in the
  report (§5.7). One file cannot name a cell twice, because TOML refuses a repeated key.
- **Values follow the cell's rules** (Lua §8.1): a whole number in 32 bits, a float, or a string that CP932 can
  encode.
- **Every edit is an assignment.**
- **A file with an error applies nothing.** The applier checks the whole document at its first delivery of a
  session, before its first write (§6.2, "Checks"). The stage parts are checked then too, so a mistake in stage
  12's entry is reported at boot and not when the player reaches stage 12. The applier keeps the result: a
  document that failed is passed over at every later delivery, with no second error.
- **Names as keys are left out.** `[unit."緑プテテット"]` would read better, but several records can share a name,
  and two spellings of one record would hide an overlap from the report. They are future work (§11). A comment
  holds the name meanwhile.

### 6.2 What the base mod decides

**When.** Each table has a moment after the game built it and before the game reads it. The base mod binds each
part of the format to an event of its own.

- **Boot tables.** `<game>.data.ready` exists, but it comes after the boot's own work with the tables (§2). An edit
  made there is not seen by that work. So each base mod gets a second event, `<game>.data.layered`, at the
  instruction after op `0x143`: every layer has written its data, and the boot has not used it yet.
  - An edit made there lands where an append's edit lands. The game's own work then runs over it, as it runs over
    its appends' edits.
  - `data.ready` stays, for a mod that wants to run after the boot's own work.
  - Each game needs a new patch point for it (`vm-map/patch-points-<game>.toml`): `INIT2@0x180` in Himegari and
    `INIT2@0x20d` in Kamidori. §6.5 reads what follows each, and neither boot writes a boot table after it.
- **Scoped tables.** A stage's enemy table is rewritten by the stage's loader each time FIELD runs the loaders. The
  file keys such a table by its scope: `stage.12.enemy.3`. The applier writes it at `<game>.stage.loaded` when the
  event's `stage` is 12, and writes it again each time the event is raised, as `example.random-enemies` does by hand.
  - The scope's key (`stage`), the range of its ids, and the short name of each table under it (`enemy`) are part
    of the format, and they are facts of the game. Milestone 2 puts them in the registry beside the label of §6.3.
    The check can then refuse a stage that does not exist, and the rename rule covers the names.

**What is not offered.** A field that a save holds is state: its array lies below the profile's saved prefix (§2).
A load brings the saved value back, and the edit would then replace it at every load: an inventory count would
reset each time. The applier refuses such a field by name and says why.

**What follows an edit.** If a game derives values in a way that no event placement avoids, the base mod brackets
the edits (§5.5) and derives again after them. Neither game needs it at boot once the event sits before the boot's
own work (§6.5). Where the game derives a saved value from an edited field, that value is in the save from then
on, and the applier does not undo it (§1.5).

**Checks.** The applier checks a document before its first write. The record map gives it what it needs (§6.3).

- **Ids and columns.** A record id outside its table and a column outside its row are refused. The accessors would
  refuse them too (`docs/modding/lua.md`, "Game names"), but only at the write.
- **Kinds of value.** A value is checked against its cell: a whole number in 32 bits, a float, or a string.
- **Strings need one thing from the engine.** A string cell refuses a text that CP932 cannot encode, and it does so
  at the write (§2). Lua has no way to ask before. So the `maid` API gains one function, which says whether a
  string cell would take a text and writes nothing. It knows no game: the code page is the profile's. It is the
  one engine change of milestone 0.
- **Parts whose event is not raised.** A file with a stage part, on an install whose FIELD the base mod does not
  know, is refused as a whole and says why (§5.4). A file with boot tables only is applied there.
- **What no check covers:** a stop by the budget, and a write that the engine refuses although the check passed.
  What the delivery wrote until then stays (§5.6). The budget's size keeps the first out (gate 10). The second is
  a bug in the check.

### 6.3 The record map, and what the registry adds

The applier needs the record tables in Lua: `unit.stats` is `unit_base_stats`. Lua cannot read the layout's JSON.

- **`tools/base_mod_build.py` writes a Lua module,** `mods/<game>-base/lua/generated/tables.lua`, from the
  registry's `[[table]]` entries. It holds each table's record count, and for each field its array, its kind, its
  row's width and column names, and whether it is state.
  - The check needs a field's kind before it writes, and the accessors do not tell it. So the map carries what
    the check needs, and the applier makes no trial reads.
  - It is the one generated file under `lua/`. The generated-files table in `AGENTS.md` gains it, and the
    `validate.py` gate that checks `layout/` checks it too.
  - No engine change is needed for it.
- **Which fields are state is computed, not marked.** The generator compares each field's address with the saved
  prefix of the game's profile (§2). The registry holds the address already, so it gains nothing for this, and no
  list can fall behind the data. §6.5 has the result for both games.
- **The registry gains one fact for each table,** which it holds only as prose today: which part of the game
  builds the table. It is a label such as `boot` or `stage`, and the base mod's Lua maps a label to its event.

  The generator copies the label into the record map. The engine reads none of this.
- **Table and field names become API.** A mod's file names them, so the rename rule covers them (Lua §9.1): they
  join `layout/exports.txt`, and a rename keeps the old name or raises the base mod's major version. The registry's
  tables have no aliases today, so the generator has to add them.

### 6.4 The shared library

Both games keep record tables in parallel arrays, so both appliers do the same walk: check the document against the
record map, then write each value through an accessor. That code goes into a first-party Lua library with no game,
`ome-records` (Lua §6.4). The engine ships it beside the base mods.

```lua
-- mods/kamidori-base/lua/tables.lua: a sketch; the library's own function names are the plan's to settle
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
  the loader (`ManifestReader`; `ModLuaLinkTests`). One test changes: `BaseModTests` pins the shipped ids and
  expects each to be a base mod.

### 6.5 The two games

| | Himegari | Kamidori |
|---|---|---|
| Record tables | `unit` (1,000), `item` (1,000), `skill` (300) | `unit` (1,000), `item` (2,000), `skill` (1,000), `stage_enemy` (30) |
| State: the fields below the saved prefix (§2) | `unit.story_speaker_seen_flag`, `skill.acquired_flag`, `skill.info_revealed_flag` | `item.inventory_count`, `item.growth_exp`, `item.growth_level`, `item.costume_enhancement_grid`, `item.costume_gem_sockets`. `unit`'s runtime arrays lie above the prefix, so a load does not bring them back, and they are not state |
| After the layers, before `data.ready` | TUNE (`INIT2@0x180`) | INIT2's own work, `0x20d` to `0x2cf` |
| The new boot point | `INIT2@0x180` | `INIT2@0x20d`, named for both INIT2 fingerprints the profile lists (the untranslated install and the translated one), as `kamidori.boot.data_ready` is |
| The stage's enemies | the `stage_enemy_*` arrays, 30 slots, all above the saved prefix. The registry has no table for them yet | the `stage_enemy` table, with no field below the prefix |
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

  The correction lasts. `tuning_curve_ids` is a field of the `item` table, and the levels are saved. A file that
  gives an item a shorter curve lowers that item's saved levels, and they stay lowered when the mod is removed
  (§1.5).
- **Kamidori's INIT2 writes no boot table after the layers.**
  - It copies `voice_setting_default_text_colors` into each unset text colour setting (`G[0x3225]`) and stores the
    setting in the shared profile. That table is not a record table.
  - When `G[0x428c]` is set, it rebuilds the field for a load. FIELD sets that flag on entering a floor
    (`FIELD@0x1fd`). INIT2 then runs the stage loaders and calls SETMAPTEX, SETHALLTEX, SETOBJ, DRAWOBJ and
    DRAWCHALL. Of the record tables, SETMAPTEX reads `unit.canonical_character_id` and, through READICON,
    `unit.graphic` and `item.costume_graphic_asset_ids`. None of them reads the `stage_enemy` table. The loaders
    write the stage's tables, `stage_enemy` among them. Those are edited at `stage.loaded`, and not here.
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
  - **It is an accepted gap** (§14), and the backlog has the item ("Mod loader and modding"). It does not hold up
    a milestone.
  - If it is closed later, `kamidori.stage.loaded` takes a second point after INIT2's loader loop (`INIT2@0x2b3`).
    That is a change to the base mod's event file and the patch points, not to the engine.
  - Himegari's INIT2 runs no stage loader.

What still has to be read before each base mod offers a table:

- **Both:** the saved values that the game derives from a field a file can set, for the guide (§1.5). Which fields
  are state needs no reading (§6.3).
- **Himegari:** a `stage_enemy` table in the registry.
- **Kamidori keeps a character's persistent stats apart from the unit's definition**
  (`docs/kamidori-data-tables.md`, "Runtime character state"). UNITECH copies `unit_base_stats` into the saved
  `character_stats`. An edit to a definition then reaches a character who has already joined only as far as the
  game reads the definition again, and what was copied while the mod was on stays in the save. The game's guide
  has to say this for each such field.

### 6.6 Lua mods and the same tables

A Lua mod keeps what it has: it writes through the accessors and lists the names (Lua §9.3). It gains the new boot
event, which is the right moment for its own edits too. It may also ship a data file beside its Lua.

**A handler on `<game>.data.ready` runs after every data file,** whatever the load order, because that event comes
later in the boot. Every Lua mod written for a base mod at 1.0 is there, since it was the only boot event. Such a
mod keeps working and keeps the last word over the data files. One that should take its place among them by load
order moves its handler to `<game>.data.layered`. The guide and both events' descriptions say so.

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
- **A shape is written in a small subset of JSON Schema:** `type`, `properties`, `additionalProperties`, `items`,
  `enum`, `minimum`, `maximum`, `oneOf`, `description`, and `$ref` inside the file.
  - Editors that check TOML against JSON Schema (Taplo does) then complete and check a data file as it is typed,
    from the same file the link uses.
  - One keyword of our own says that a table's keys are decimal integers in a range. An editor passes over a
    keyword it does not know, so a shape also gives the records' fields under `additionalProperties`, which an
    editor does check.
  - A shape that uses a keyword outside the subset is its owner's link error, so no check is skipped without notice.
- **For a base mod the shape is generated** from the record map by `tools/base_mod_build.py`. A state field stays
  in it, marked as not offered, so that the link's error says the field is saved state and does not call it
  unknown. The plan settles how the subset marks it.
- **An error names** the contributor, the file and line, the path, and the nearest names the shape allows. The
  reader's model keeps no positions, so the line comes from a second parse of the syntax, as the text catalog
  reader finds its lines (`Ome.Mods/Localization/TextCatalogFileReader.cs`).
- **`omp build` refuses** a package whose file fails its kind's shape.
- **The owner's handler still checks** what a shape cannot say, and what an owner without a shape must check.

This is milestone 3. The first two milestones work without it.

## 8. Engine changes

| Part | Change |
|---|---|
| `Ome.Mods` | `data/` and `shapes/` in the package layout (`ModLayout`); `[[data]]` and `[[accepts]]` in the manifest, with format version 4; the document reader over Tomlyn, with its bounds; the three link steps (§5.4), with the note for a kind's event that is not raised; the report's kinds and shared paths; the documents in the link cache; a package with `data/` is not presentation-only; later, the shape check |
| `Ome.Engine` (`Modding/`) | the document as plain values, with no TOML type in it; a planned handler may carry a contribution; the runtime stack lists the contributions |
| The scripting native (`Natives/Scripting*.cs`) | a delivery runs for its contributor in the trace, the watch records and the log; failures are counted by file; the budget's allowance |
| `Ome.Engine.Vm` | none beyond passing the contribution with the context |
| `Ome.Scripting.Lua` | the document as a read-only view, built once per session; the handler's second argument; the function that says whether a string cell takes a text (§6.2), which milestone 0 needs |
| Tools | `base_mod_build.py` writes the record map, with the state fields it computes, and later the shape; the registry's label for what builds a table (§6.3); the exported-names check covers table and field names |
| `mods/` | `ome-records`; in each base mod `[[accepts]]`, `lua/tables.lua`, `lua/generated/tables.lua`, the new event and its patch point, and version 1.1.0 |
| Tests | `ProjectBoundaryTests`: `Ome.Engine` and `Ome.Engine.Vm` reference no TOML library; `BaseModTests`' list of shipped packages; the gates of §12 |

Nothing here reads an engine build's name or a profile's id.

## 9. Untrusted input

Packages are untrusted (mod loader spec §11; Lua §15).

- **A data file is data.** It is parsed, bounded and handed over as values. Nothing in it is executed.
- **It reaches only its kind's owner,** and only as a read-only value. It cannot name a function, a cell or a file.
- **Its effect is bounded by the owner's handler.** A base mod's applier writes only the fields it offers, inside
  the tables' bounds.
- **It can do what the offered fields can do.** Some name a script: `item.handler_script_id` and
  `skill.handler_script_id`. A file that sets one makes the game call a script that exists, where it called
  another. Which fields a format offers is the owner's decision, and the game's guide lists them.
- **A hostile file can still cost time.** Hence the bounds of §5.3 and the budget of §5.6. The budget grows with
  the file, so the bounds are what limit it: on one file's size, and on the number of files in a package.
- **Its keys are printed** in the report and the log. They are bounded in length and hold no control character
  (§5.3).
- **A third-party owner** runs its own Lua on other mods' data, with its own access. The link report already shows
  that mod's capabilities (Lua §15).

## 10. Alternatives considered

| Alternative | What it gave | Why not |
|---|---|---|
| **The linker lowers edits to bytecode** at a point that each registry table declares (the first sketch of this feature) | native export, and conflicts found cell by cell at link | the linker would know when a game's tables are ready, which cells a save holds, and how a stage guards its loader. That is these two games' shape inside the engine |
| **Lua only:** a mod calls a base mod function with a Lua table from its own handler | no engine change at all | every data mod needs Lua and a hook, and its author must know the event; the link sees nothing of it; it is the unlisted write of §6.6. Milestone 0's tests call the applier this way, with a Lua table, from inside the base mod (§13) |
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
- **Record accessors for Lua mods:** `game.record("unit")[761].stats.max_hp`, offered by the base mod from its
  record map. Its writes would have to count against the calling mod's write list, and a base mod's function
  cannot write under its caller's list today (Lua §9.3). That is the engine part this would need.
- **More spellings:** hexadecimal ids, and a whole row as an array, once a shape can tell the link that two
  spellings name one cell.
- **A file that names what its own package adds:** a script or an asset by name, where the format takes a number.
- **A table printed as a data file.** `Ome.Cli oms-decompile` already prints each unit as a `with` block. The same
  walk could print a table in this format, as the starting point of a balance mod.
- **Kinds delivered at engine events,** such as `ome.session.start`.
- **Reloading a data file while the game runs,** for the scoped tables.

## 12. Validation gates

Every gate except the driven ones runs in `Ome.Engine.Tests`, without Godot.

| Gate | Passes when |
|---|---|
| 1. Off means off | a stack with no `[[data]]` entry links and runs as it does today, and an event with no kind does no new work |
| 2. Link | each of these stops the load with a report that names the mod and the file, and no mod code has run by then: a kind that no mod accepts; an owner that the contributor does not require; a file under `data/` that no entry names; a TOML syntax error, with its line; a date; an integer past 2^53; a file past a bound; a kind declared twice, naming both owners; a handler that is not a top-level function; an event its owner cannot subscribe to; `[[data]]` in a package without `[game]`, or with `target = "native"`; a kind none of whose events is raised. A kind with one event that is not raised and one that is still links: the report names the event and the contributors, and the other event delivers |
| 3. Order | two data mods and a Lua mod on one event run in the order the report gives; swapping the two data mods in the load order swaps which value holds; a mod's file is applied before its own handler at the same priority; a handler on `<game>.data.ready` finds every file applied |
| 4. Attribution | the trace line of a delivery names the contributor, its file and the owner's handler; a watch on a cell the file sets names the contributor; three failures of one file leave another mod's file delivered |
| 5. Same cells | the file of §1.3, shipped by a mod and delivered, leaves `unit[761]`'s three fields as the `with` block of `oms/examples/kamidori-gate6` leaves them, in a headless boot. Milestone 0 runs the applier's half, with the same values as a Lua table |
| 6. Loads | the edit holds after boot, after a load, and after a return to title; a save made with the mod and loaded without it shows the game's own values in the tables; for a file that sets no field the game derives saved values from (§1.5), the decoded save made right after boot is the same with and without the mod. Milestone 0 runs the event's half, with a test mod's Lua handler on the new event |
| 7. Errors | each of these is reported with the contributor, the file and the path, and nothing of that file is applied at any event: a misspelt field; a state field; a hexadecimal id; a named column given by its number; a value of the wrong kind; a string that CP932 cannot encode. `--lua-errors halt` ends the run. Milestone 0 runs the applier's half |
| 8. Stage | a stage's enemy edit changes what the stage places, holds when FIELD is entered again and after the load of a save made with the field's save command, and leaves another stage as it was. A mistake in a stage part is reported at boot, and nothing of that file is applied. Driven on the port, on both games |
| 9. Not shaped by records | a test mod accepts a kind whose documents are a list of strings and receives them by the same path; the test stack has no record table in it |
| 10. Size | the largest file the bounds admit is applied inside its budget, at boot and at a load; a file that sets every authored field of the game's largest table adds no more time to a boot or a load than a ceiling the plan names before it measures; the numbers are recorded |
| 11. Second game | both base mods accept their kind with no difference in engine code |
| 12. Shape (milestone 3) | a misspelt field fails `omp check` with the file, the line and the nearest names, and `omp build` refuses the package; a state field's error says that it is saved state; a shape with a keyword outside the subset is its owner's link error |
| 13. Layers | `ProjectBoundaryTests` pass, and neither `Ome.Engine` nor `Ome.Engine.Vm` references a TOML library |

## 13. Milestones

0. **The applier, before the engine's delivery.** What §6.5 still lists to read. The registry's label for what
   builds each table, and the record map with the state fields it computes. `ome-records` and each base mod's
   `tables.lua`. The new boot point and event for each game. The `maid` function for the string check (§6.2),
   which is the one engine change here.
   - **The applier is tested with a Lua table,** from a console chunk in the base mod's environment
     (`IModSession.RunChunk`, Lua §13). That proves the format, the checks and the writes. It does not prove the
     moment, since a console chunk runs at a wait. The library walks a document with `pairs` and `ipairs`, which a
     Lua table and the view of §5.3 both answer, so the same code takes the delivered document in milestone 1.
   - **The moment is tested with a test mod's Lua handler on the new event:** its edit is in place when the boot's
     own work reads the tables, and it is made again at a load.
   - **The descriptions of `data.ready` are brought up to date:** both `events/boot.toml` files, the patch points,
     and each game's guide page. Himegari's point says "TUNE's derived item data", which the reading of §6.5 does
     not bear out.
   - The applier's half of gates 5 and 7, and the event's half of gate 6.
1. **Delivery.** Format version 4, `data/`, `[[data]]`, `[[accepts]]`, the document, the link steps, the planned
   deliveries, attribution, failures counted by file, the report and the trace. The bounds and the budget's
   allowance are measured here. The generic guide page. Gates 1 to 7, 9, 10 and 13.
2. **Stages, and both games driven.** The scoped tables on both games, with their registry facts (§6.2) and
   Himegari's `stage_enemy` table. An example mod for each game under `oms/examples/mods/`, and each game's guide
   page. Gates 8 and 11.
3. **Shapes.** The schema subset, the generated shapes, the `omp build` refusal. Gate 12.

When it is built: the backlog's "declarative data patches" line goes, and the mod loader spec stays as it is, with
this spec named as what replaced its sentence. `AGENTS.md` gains a row for `mods/ome-records/` in its documents
map, and the record maps in its table of generated files (§6.3).

## 14. Decisions by the user

The draft put eight questions, each with a recommendation. The user decided all of them on 2026-11-18. The review
of the same day led to four further answers, which the user accepted that day too. They are at the end of this
section. The user then read the resulting text and accepted the design as a whole: "Looks good to me, I approve
it." Nothing is open.

1. **Port only is acceptable** ("Port only is fine"). A data mod needs the base mod's Lua, so the Compatibility
   mode and `omp export` refuse it, and the boot script stays the native route. The alternative was the linker
   design of §10, which the rule of §1.2 rules out. A later conversion tool (§11) could give native export back for
   formats that map onto registry tables.
2. **How much engine: milestones 0 and 1,** as recommended ("2 is fine with your recommendation"). The question
   was whether to stop at the applier of milestone 0 or to build the engine's delivery as well. Milestones 2 and 3
   were not part of it, and they follow as §13 plans them. The question said that milestone 0 alone "already
   works". That claimed too much: the applier is not exported (§6.6), so milestone 0 alone gives a modder the new
   boot event and nothing else.
3. **The two facts of §6.3 live in the registry's `[[table]]` entries,** as recommended. They are facts about the
   game's data, and the registry already says them in prose. The alternative was a hand-kept file in the base mod.
   The review found that one of the two needs no entry (the first answer below).
4. **The boot moment is the new event after op `0x143`,** as recommended (§6.2). The readings of §6.5 support it on
   both games: neither boot writes a boot table after that point, and each reads the tables there in a way that
   an edit should reach. The alternative was `data.ready`, with the base mod deriving again after the edits.
5. **The shared code is a library,** as recommended (§6.4). It keeps the two games' likeness out of the engine and
   in one place, and it gives Lua §6.4 its first real user. The alternative was a copy in each base mod.
6. **A shape is a JSON Schema subset, and it waits for milestone 3,** as recommended (§7). The alternative was a
   small format of our own, in the style of the event declarations.
7. **The first format has no relative edits,** as recommended (§11).
8. **The names stand:** `[[data]]`, `[[accepts]]`, "kind", `<game>.tables`, `<game>.data.layered` and
   `ome-records`.

Questions 4 to 8 were answered together: "I'm fine with your recommendations for all of the questions."

Also decided that day, outside the list:

- **A robust system first, and gaps around saves are accepted.** The user: "For now I'd rather focus on a robust
  system and accept certain gaps, particularly around saving." A save path on which an edit is not made again goes
  to the backlog (§1.5). It does not change the design or hold up a milestone.

**Answers the review led to.** Each changed something the user had read or decided. The user accepted all four on
2026-11-18, provided they also held for the spec's author: "Those changes look fine to me if they look fine to
you." They do. Each follows from a finding that was checked against the code (§15). Below, "the draft" is the
text before the review.

1. **Which fields are state is computed from the saved prefix** (§6.3), and the registry gains no list of them.
   This narrows decision 3. The draft's own lists were wrong for both games, and a hand-kept list could go wrong
   the same way.
2. **An event of a kind that the install does not raise costs only its own deliveries** (§5.4). The draft failed
   every contributor of the kind. A file that edits boot tables would then fail on an install whose FIELD is
   another revision, where the same edit as a Lua handler links today.
3. **`<game>.tables` has one spelling for each cell** (§6.1): ids in decimal, a named column by its name, and no
   array for a whole row. The draft allowed hexadecimal ids, columns by number and whole rows. Two files could
   then set one cell under different paths, and the report would not show it before milestone 3.
4. **Milestone 0 has one engine change** (§6.2): a `maid` function that says whether a string cell takes a text.
   Without it the applier cannot check a string before it writes, and a file with one bad name would be applied
   in part.

## 15. What the independent review changed

On 2026-11-18 an agent that had not seen the draft being written reviewed it against the repository. Its findings
were checked against the code before the text was changed. It confirmed the boot and stage readings of §6.5 and
the loader facts of §2 that it checked. What it found wrong or missing:

| Finding | Where the draft changed |
|---|---|
| The lists of state fields were wrong for both games, and the saved prefix sorts the fields without any reading | §2, §6.3, §6.5; the first answer of §14 |
| "A file with an error applies nothing" could not be kept: a string is refused only at the write, the record map lacked each field's kind, and no check covers a stop by the budget | §5.6, §6.1, §6.2 ("Checks"), §6.3; gate 7; the fourth answer of §14 |
| One kind at two events made a file of boot edits depend on FIELD's revision | §5.4; gate 2; the second answer of §14 |
| Gates and milestones did not line up: gate 5 never ran with a real file, a console chunk cannot prove the moment, and gate 7 tested stage parts before they exist | §12, §13 |
| "The engine never reads a key" contradicted the report's shared paths; the report claimed which value holds; one cell had several spellings | §3 (decision 1), §5.7, §6.1, §7; the third answer of §14 |
| Load order means one thing only on one event: a handler on `data.ready` runs after every data file | §3 (decision 3), §6.6; gate 3 |
| A saved value that the game derived from an edited field outlives the mod | §1.5, §6.2, §6.5; gate 6 |
| The budget's "value" was not defined, gate 10 could not fail, and the trace does not list every write of a large delivery | §5.3, §5.6, §5.7, §9; gate 10 |
| `omp build`'s check without the owner could never be reached | §5.8 |
| Behaviour left open: a contributor that is off, an owner's own files, when the handler's file loads, the document as a userdata, a subfolder under `data/`, how failures count across events, a scope's names and ids | §4, §5.1 to §5.6, §6.2 |
| Smaller points: "no record table" where "no boot table" was meant; fields that name a script; the shape's keyword and its state fields; the pinned list in `BaseModTests`; keys printed without a bound | §5.3, §6.4, §6.5, §7, §9 |

Not taken up: a cap on the total work of all data files at one event. Each file is bounded, and so is the number
of files in a package.

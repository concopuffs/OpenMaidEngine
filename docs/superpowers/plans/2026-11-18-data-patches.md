# Data patches — implementation plan

**Date:** 2026-11-18
**Spec:** `docs/superpowers/specs/2026-11-17-data-patches-design.md` (cited as "spec §n"); the Lua scripting design
(`docs/superpowers/specs/2026-11-10-lua-scripting-design.md`) is cited as "Lua §n". The spec stays the design's home.
This plan keeps what each milestone checks and builds, and what that settles for the later ones.
**Status:** written before milestone 0 was built; §4 was expanded before milestone 1. M0 and M1 are planned in detail
(§3, §4), M2 and M3 in outline (§5, §6). §8 keeps what M0 found, §9 what changed since the spec was accepted, and §10
what M1 found.
**Branch:** `feature/data-patches`, off `develop` at `8970fbe6`.

Paths are relative to `age-reimpl/`.

## 1. What the code says today

Before planning, the claims that M0 relies on (spec §2, §6.5) were checked. "Ran" marks a check made by running code,
"read" one made by reading the code or the disassembly.

- **Himegari's boot** (read): `INIT2@0x17f` is op `0x143`, `0x180` calls TUNE, `0x183` exits. The point
  `himegari.boot.data_ready` names INIT2's fingerprint `sha256:00ec969c…` at `0x183`; the new point names the same
  fingerprint at `0x180`. TUNE (`build/games/himegari/disasm/TUNE.asm`) does what spec §6.5 says: for each item whose
  `item_category` is 10 or more, it lowers each saved tuning level `G[0x3ebe + item*10 + k]` that is above 0 to one
  more than the last index below it at which `item_tuning_stat_bonus_curves[item_tuning_curve_ids[item][k]]` is not 0.
- **Kamidori's boot** (ran: both files decoded with `sys4load`): the untranslated `INIT2.BIN` (`sha256:5e493218…`)
  and the translated `patch/INIT2.bin` (`sha256:a60b2768…`) have the same offsets around the layers: op `0x143` at
  `0x20c`, a `mov` (op `0x55`) at `0x20d`, the exit at `0x2cf`. The new point names `0x20d` under both fingerprints.
  The spec calls them the fingerprints "the profile lists"; they are listed by `vm-map/patch-points-kamidori.toml`,
  not by `Profiles/kamidori.json`. That is wording only.
- **What Kamidori's INIT2 reads after the layers** (read): as spec §6.5 says. SETMAPTEX reads
  `unit_canonical_character_id`; READICON reads `character_costume_item_id`, `item_costume_graphic_asset_ids` and
  `unit_graphic_asset_ids`.
- **Which fields are state** (ran: each `[[table]]` field's array against `persistence.bankDimensions` of
  `engine/Ome.Engine/Profiles/<game>.json`): exactly the lists of spec §2 and §6.5. No field's array straddles a
  prefix. Himegari's `unit.story_display_name` (`unit_story_display_names`, GS `0x315` to `0x6fd`) begins exactly at
  the GS prefix (789, `0x315`): a cell at the prefix is not saved. The generator's test is therefore "the array ends at
  or below the prefix" for state and "it begins at or above it" for authored data, and anything else is an error.
- **No table field is a 1-D array with column names** (ran, same script). A 1-D global's column names name its
  elements (`docs/modding/lua.md`, "Game names"), which for a field would be records, so the generator refuses one.
- **Loads** (read): `ScriptMemory.ReadSaveRecords` zeroes every bank before it copies the saved prefixes, and the
  prologue runs again (Lua §11). So both boot events are raised again at a load, with `maid.session.restoring` true.
- **Stale descriptions** (read): the `data_ready` points of both games say "once per run at boot" and "no load
  replaces them: an edit made here lasts the whole run". The load just described contradicts both. Himegari's also
  says "TUNE's derived item data", which TUNE does not make (spec §6.5). The events' descriptions in
  `events/boot.toml` are right about loads, but say nothing of the new event.
- **The string rule** (read): `CellRules.Text(value, codePage)` throws a `ScriptAccessException` for a text the code
  page cannot encode, and the scripting native writes strings with its memory's code page (`Scripting.WriteCell`).
  `CellRules.Integer` takes a whole number from -2^31 to 2^32-1, and a float cell takes any number (`FloatBits`). The
  library checks values by exactly these rules, so that a check that passes is a write the engine takes.
- **A base mod's own writes** (read): `ModFace.Build` gives a base mod's own accessors no write list. The applier writes
  through the base mod's `maid.names.table`, so it needs no `[lua] writes`.
- **Requiring a library** (read): `require("ome-records")` in a base mod gives a read-only view of what the library's
  `init.lua` returned. `ReadOnlyViews.Wrap` wraps tables only, so a function read through the view is the library's
  own function, which runs in the library's environment, and what it returns is not wrapped. The applier the library
  builds is a plain table.
- **The console chunk** (read): `IModSession.RunChunk(modId, code)` runs in that mod's environment, under the handler
  budget (1,000,000 instructions). `require("tables")` there loads the base mod's own `lua/tables.lua`, and a module
  name maps `.` to `/`, so `require("generated.tables")` loads `lua/generated/tables.lua`. `ModLayout` allows a
  subfolder under `lua/`.
- **Shipping a library** (read): `Ome.Mods.csproj` embeds every file under `mods/`; `ShippedPackages.Required` mounts
  the shipped packages that a mounted one requires; a package without `[game]` is a library to the loader
  (`ModLuaLinkTests.ALibraryLinksWithoutAGameAndAModWithoutLuaLinksWithoutARuntime`). The places that assume every
  shipped package is a base mod:
  - `BaseModTests` pins `ShippedPackages.Ids` to the two base mods. The spec names this one (§6.4).
  - **`LuaStubs.Inputs` links the profile's shipped base mod alone** (`ModLinker.Link(linkBase, [shipped], [])`). Once
    the base mod requires `ome-records`, that link fails on the missing requirement, and `omp lua-stubs` silently
    drops the base mod's names. It has to add `ShippedPackages.Required([shipped], …)`. The spec does not list it.
  - The launcher's Mods tab shows each shipped package the link mounts as an engine row (`ModsViewModel`), so
    `ome-records` shows as one too, "Part of the engine, mounted by the link." `WindowTests`' list of rows gains it.
    Its shots under `build/launcher-shots/` are written, not compared.
  - A save's stamp lists the mounted stack (`ModSaveStamp.Of`). A save made before M0 and loaded after it reports
    "added ome-records 1.0.0" and "himegari-base 1.0.0 -> 1.1.0", as the loader reports any change; it never blocks.
  - The Compatibility mode refuses `ome-records` by itself ("it ships Lua", `ModeCheck`). A stack only reaches it
    through a base mod, which that mode refuses already.
  - `godot/Main.PackageSmoke.cs` opens every shipped id, which holds for a library too.
- **Version pins in the tests** (read): `BaseModTests` expects "the engine's own himegari-base 1.0.0 is mounted…",
  `ModLuaLinkTests` expects "mod himegari-base 1.0.0: …" and stacks of `["himegari-base", "user"]`, and the boot
  tests pin INIT2's sites to `[0x183]` and `[0x2cf]`. Each changes with M0.

## 2. What the plan settles

The spec leaves these to the plan.

| Question | Settled |
|---|---|
| The `maid` function for the string check (spec §6.2) | **`maid.text_fits(text)`**: `true` when a string cell would take `text`, else `false` and the reason the write would give. It reads and writes nothing, so every mod's `maid` has it, libraries included. An argument that is not a string is an error. `maid.text` is left free, since the localization design may want it for the engine's text |
| The registry's key for what builds a table (spec §6.3) | **`built_by`** in each `[[table]]`: a lowercase identifier, required. `boot` for every table today except Kamidori's `stage_enemy`, which is `stage`. `globals_build.py` lints it, the JSON carries it, and the reference shows it |
| The record map (spec §6.3) | `mods/<game>-base/lua/generated/tables.lua`, format 1 (§2.1) |
| The library's functions (spec §6.4) | `records.applier(config)`, `applier:check(contribution)` and `applier:apply(contribution, options)` (§2.2) |
| The contribution's fields (spec §5.2) | `mod`, `file`, `data` and `unraised`, as the spec's sketch has them. M0's tests build the same table |
| When each base mod goes to 1.1.0 | **In M0**, in the commit that adds `<game>.data.layered`: a mod that subscribes to it needs `>=1.1`. M1's `[[accepts]]` stays inside 1.1.0 when no release is tagged between the two. If one is, M1 raises it to 1.2.0 and the examples' requirement follows |
| `ome-records`' version and requirement | 1.0.0. Each base mod adds `[[requires]] id = "ome-records"`, `version = ">=1, <2"` |
| The new points | `himegari.boot.data_layered` and `kamidori.boot.data_layered`, beside `<game>.boot.data_ready`; the events are `<game>.data.layered` (spec §14, decision 8) |
| How the tests reach the applier (spec §13) | **a console chunk in the base mod's environment**, `RunChunk("<game>-base", …)`, over a stack that the real linker builds from the shipped packages and a synthetic link base. So the requirement on `ome-records` and its mount are exercised as a player's stack has them. The library is tested on its own through a synthetic base mod with a hand-written map |

### 2.1 The record map

```lua
-- DO NOT EDIT -- generated from vm-map/globals-kamidori.toml and the kamidori profile's saved prefix
-- (engine/Ome.Engine/Profiles/kamidori.json) by tools/base_mod_build.py --build.
-- The record tables of kamidori-base (data patches spec §6.3): each table's record count and what builds it, and for
-- each field its global, its kind, a row's width and named columns, and whether a save holds it.
return {
    format = 1,
    tables = {
        unit = {
            records = 1000,
            built_by = "boot",
            fields = {
                experience_reward = { global = "unit_experience_reward", kind = "integer" },
                stats = { global = "unit_base_stats", kind = "integer", width = 14,
                          columns = { max_hp = 0, --[[ … ]] } },
            },
        },
        item = {
            -- …
            fields = {
                inventory_count = { global = "item_inventory_counts", kind = "integer", state = true },
            },
        },
    },
}
```

- **Kinds** are `integer`, `float` and `string`, from the global's bank.
- **`width`** only for a field with a row (`[records][n]`). **`columns`** maps a named column to its index, from 0 as
  the game counts; a column without a name has no entry.
- **`state = true`** only for a field whose array ends at or below its bank's saved prefix (§1).
- **Errors that block the build:** a field array that straddles its bank's prefix; a profile manifest without
  `persistence.bankDimensions`; a 1-D field with column names; a field whose global the layout does not map. A key
  that is a Lua keyword is written `["end"]`. Tables, fields and columns are sorted, so a registry change is a small
  diff.
- **The generator reads** `paths.PROFILE_MANIFEST_DIR / "<game>.json"` for `persistence.bankDimensions` only.
  `--check` compares the file as it compares `layout/`, so the `validate.py` gate covers it with no new gate.

### 2.2 The library

`mods/ome-records/`: `manifest.toml` (`omp = 3`, `id = "ome-records"`, `version = "1.0.0"`, no `[game]`,
`[lua] api = 1`), `lua/init.lua`, `README.md`. It names no game and no table.

```lua
local records = require("ome-records")
local applier = records.applier {
    map = require("generated.tables"),  -- a record map, format 1
    table = maid.names.table,           -- the caller's access: a global's name to its accessor
    labels = { "boot" },                -- the built_by labels the caller applies at some event of its own
}
applier:check(contribution)             -- true, or false and the message
applier:apply(contribution, { built_by = "boot" })  -- the number of cells written
```

- **`check`** walks the whole document once and keeps the result for the applier's life, by `mod` and `file`. The
  applier lives as long as the module that made it: one Lua session. A load builds a new session (Lua §7.1), so a bad
  file is reported once per session, as spec §6.1 has it.
- **`apply`** checks first. A document that fails raises one error, `error(message, 0)`, at its first `apply`, and
  writes nothing; each later `apply` of it returns 0 and says nothing. A document that passes is written: every cell
  of the parts whose table `built_by` names `options.built_by`.
- **The rules**, each a problem of the document:
  - the document and each record are tables;
  - a top-level key names a table of the map, and that table's `built_by` is one of `labels`; a table built by
    another label is refused, because no event of the caller applies it (M0: Kamidori's `stage_enemy`);
  - a record id is decimal, with no sign and no leading zero, and inside the table; a hexadecimal id is refused with
    its decimal spelling;
  - a field is one of the table's; a state field is refused as saved state, and says why;
  - a field without a row takes one value; a field with a row takes a table of its columns. A whole row as an array
    is refused. A named column is given by its name, and given by its number it is refused with the name; a column
    without a name is given by its number, from 0;
  - a value fits its cell: for `integer` a whole number from -2^31 to 2^32-1, for `float` a number, for `string` a
    string that `maid.text_fits` takes. A boolean fits no cell.
- **A table in a document** is a Lua table in M0 and a read-only view (a userdata) in M1 (spec §5.3). The library
  takes either: it tests `type(v) == "table" or type(v) == "userdata"`, and walks with `pairs` only. A TOML key is
  always a string, so a table with a number key is an array.
- **The message** names the contributor and the file, then each problem as its path and its reason, sorted by path,
  at most 20 and then a count of the rest:

  ```text
  example.tougher-slimes data/slimes.toml: 2 problems, so nothing of it is applied
    unit.761.stats.max_hpp: unit.stats has no column max_hpp
    unit.0x2f9: an id is written in decimal: unit.761
  ```

### 2.3 Each base mod's `tables.lua`

```lua
-- mods/kamidori-base/lua/tables.lua
local records = require("ome-records")
local applier = records.applier {
    map = require("generated.tables"),
    table = maid.names.table,
    labels = { "boot" },
}

--- The boot tables of a contribution, at kamidori.data.layered.
function apply_boot(event, contribution)
    return applier:apply(contribution, { built_by = "boot" })
end
```

- In M0 nothing loads it but the tests: `init.lua` does not require it, and no `[[accepts]]` binds it before M1.
- `apply_stage` comes in M2 with the scopes, and `labels` gains `stage` then.

### 2.4 The new event and its points

- `vm-map/patch-points-<game>.toml` gains `<game>.boot.data_layered`: Himegari `INIT2@0x180`, Kamidori `INIT2@0x20d`
  under both fingerprints. The `data_ready` points' descriptions are rewritten (§1).
- `mods/<game>-base/events/boot.toml` gains `<game>.data.layered`, with no fields. Its description says that every
  layer has written its data and the boot has not used it yet, that an edit there lands where an append's edit lands,
  and that it is raised again at a load. `data.ready`'s says that it comes after the boot's own work, and so after
  every data file, and that a mod which should take its place among the data files by load order uses
  `data.layered` (spec §6.6).

### 2.5 The tests of M0

| Test | Where | Proves |
|---|---|---|
| The record map's generation: kinds, widths, columns, state at and around a prefix, a straddle, a 1-D field with columns, a keyword key, missing bank dimensions, `--check` staleness | `tools/test_base_mod_build.py` (plain, synthetic registry and profile) | spec §6.3 |
| `built_by` is required and linted | `tools/test_base_mod_build.py` or a `globals_build` lint case | spec §6.3 |
| `maid.text_fits` | `LuaNamesTests` or `LuaScriptingTests` | spec §6.2 |
| The applier on the shipped base mods: §1.3's file as a Lua table writes the cells that `oms/examples/kamidori-gate6`'s `with` block writes, and nothing else | a new `DataPatchTests` (not Workspace) | gate 5, applier half |
| Each problem of gate 7 (misspelt field, state field, hexadecimal id, named column by number, wrong kind, a string CP932 cannot encode), and more (a table built by another label, a row as an array, an id outside the table): reported with the contributor, the file and the path, nothing written, a second `apply` silent | `DataPatchTests` | gate 7, applier half |
| The library alone: a synthetic map and access, a userdata document (an export's read-only view stands in for M1's) | `DataPatchTests` | spec §5.3, §6.4 |
| Himegari: a test mod's handler at `himegari.data.layered` makes TUNE lower a seeded saved tuning level, and the same edit at `data.ready` does not; at a load the event comes again with `restoring` true and TUNE lowers the restored level again | `ModLuaLinkTests` or `DataPatchTests`, Workspace | gate 6, event half |
| Kamidori, both installs: the points bind at `0x20d` and `0x2cf`; INIT2 raises `data.layered` before `data.ready`, and an edit made at the first is still in place at the second | Workspace, `Profile=kamidori` | gate 6, event half |

The Himegari moment test edits item 999 at `data.layered`: `item_category` to 10, `item_tuning_curve_ids[999][0]` to
a curve, and that curve's row so that its bonus ends early. The saved level `G[0x3ebe + 9990]` is seeded to 5, and a
handler at `data.ready` logs it. The load case runs the installed INIT2 from a synthetic root (`call-script INIT2`,
op `0xae`, a `mov` that sets the level back to 5, the save), then a full load of that slot, as `LuaStateTests` runs its
synthetic root. Values are seeded non-zero and read with `GetValueOrDefault`.

## 3. Milestone 0: tasks

In commit order; each commit builds and passes.

1. **The plan** (this file).
2. **`built_by` in the registries.** Both `[[table]]` lists gain it; `globals_build.py` lints it, emits it in the JSON
   tables and shows it in the reference's Tables section; `globals_build.py --build` regenerates both references (the
   worktree's `build/` holds copies of the main checkout's auto maps, so no auto row is lost: check the diff).
   `GameModule` ignores keys it does not read, so OMS needs no change; check by the engine tests.
3. **The record map.** `base_mod_build.py` writes `lua/generated/tables.lua` (§2.1), its tests, and
   `docs/tools-reference.md`.
4. **The new event.** The points (§2.4) and their reference docs (`patch_points_build.py --build` for both games),
   `events/boot.toml` in both base mods, `base_mod_build.py --build` (the points' copies),
   `modding_games_build.py --build`, both base mods at 1.1.0, each game's guide page (seven events, the new row, the
   corrected `data.ready` text), `docs/modding/lua.md`, the backlog's "six events", and the tests that pin offsets
   and versions.
5. **`maid.text_fits`.** `IModAccess.TextFits` in `Ome.Engine/Modding`, implemented in `Scripting.ModFace` with
   `CellRules.Text` and the memory's code page; `maid.text_fits` in `ModEnvironment`; the test fakes; the `maid` stub
   in `LuaStubs`; `docs/modding/lua.md`.
6. **`ome-records` and `tables.lua`.** The library (§2.2), each base mod's `tables.lua` (§2.3) and its requirement on
   `ome-records`; the record map regenerated with the base mods' README; `LuaStubs.Inputs` mounting what the shipped
   base mod requires; `BaseModTests`, `ModLuaLinkTests` and `WindowTests` for the new shipped package; the applier
   tests (§2.5); `docs/PROJECT-STRUCTURE.md`, `docs/modding/packages.md` (the engine ships a library too).
7. **The moment tests** (§2.5, the last two rows).
8. **The derived values.** A static reading, time-boxed, of the saved values each game derives from a field a file
   can set (spec §6.5, "Both"): candidates and their evidence go to §8 and the backlog, for the guide pages of M2.
9. **Closeout.** The backlog, the references of the documents map, and §8 brought up to date.

## 4. Milestone 1: delivery

Planned on 2026-11-18, after M0 was accepted. §4.1 checks what M1 relies on, §4.2 settles what the spec leaves open,
§4.3 names gate 10's ceiling and the bounds before anything is measured, and §4.4 lists the tasks in commit order.
§10 keeps what M1 found while it was built.

### 4.1 What the code says for M1

"Ran" and "read" are as in §1.

- **The manifest reader** (read: `Manifest/ManifestReader.cs`, `ModManifest.cs`). It reads `omp` 1 to 3
  (`CurrentOmpVersion = 3`) and refuses a key it does not know (`TableReader.RejectUnknown`). Each format version's
  tables are gated with "needs omp = N" (`[[language]]` 2; `[lua]`, `[[hooks]]` and `target = "extended"` 3). A
  hook's handler is `FILE.lua:FUNCTION` (`HandlerPattern`); `ModLayout.Classify` checks that the file is under
  `lua/`, and the link checks that the function is top-level. A package without `[game]` is a library: it has no
  hooks and no events, and a mod with Lua must target `extended`. `[[data]]` and `[[accepts]]` follow the same
  pattern.
- **The layout** (read: `Packages/ModLayout.cs`, `PackageContainer.cs`). Top folders are matched case-insensitively,
  and a folder the layout does not know is a note ("outside the package layout and is ignored"). So a `data/` folder
  in a package of format 1 to 3 is already the note spec §5.1 asks for. `ChangesNoContent` lists the content kinds.
  A Lua file is at most 1 MiB, and a package holds at most 512 of them. The package reader admits an entry of up to
  1 GiB and a package of up to 8 GiB (`PackageLimits`), so the data bounds below are the binding ones.
- **The resolver** (read: `StackResolver.Resolve`). A missing requirement, or one outside its range, fails the link,
  so a data mod whose owner is missing fails on the requirement before any data check (spec §5.8).
  `ShippedPackages.Required` mounts a base mod that a data mod requires.
- **The Lua link** (read: `Linking/ModLuaLinker.cs`).
  - `Link` returns `LinkedLua.None` when no package ships Lua (`ShipsLua`: Lua files, a `[lua]` table or a hook).
    A data mod with no Lua does not ship Lua, so the data steps must run before that return, or a stack whose
    contributors have no owner would link without a word.
  - `Events` reads the declarations first. An event a point of which does not bind goes into `unavailable` with its
    reason, as a note, and a broken declaration into `broken`. A hook on an unavailable event is an error that
    gives the reason. Spec §5.4's "not raised" is exactly `unavailable`.
  - A handler gets one `HandlerId` for each (mod, file, function). `Pending` carries the hook, the handler, the load
    order and the event at a point.
  - `Merged` sorts by priority, then load order. LINQ's sort is stable, so one mod's hooks keep manifest order.
  - A point event's subscribers are planned at each of its places, as `PlannedHandler(GameEvent: name)`; a Lua
    event's go into `luaEvents`.
  - The report's Lua section is `Summary`: mods, hooks, watches, and events with their subscribers.
  - A source is checked, never run (`IModRuntime.Check`).
- **`PlannedHandler`** (read: `Modding/HookPlan.cs`) is `(Handler, Priority, LoadOrder, Skip, EngineEvent, GameEvent,
  Site)`. `Scripting.RunBefore` gives one with `GameEvent` the event's context, and `Scripting.Emit` runs a Lua
  event's list. A delivery is a planned handler with one more field, and these are the two places it runs.
- **The link cache** (read: `Linking/ModLinkCache.cs`, `ModsJson.cs`). `link.json` keeps the Lua part as
  `LinkedLuaEntry`: the mods with their file lists, the handlers, the sites, the scripts, the engine and Lua events
  and the watches, all through `ModsJsonContext`. The Lua sources themselves are files under `lua/<mod>/<path>`,
  which `ToStack` reads back. `TryOpen` refuses an entry when a listed file is missing. The key hashes each mod's
  id, version and package hash, so a changed data file gives a new key.
- **The read-only views** (read: `Ome.Scripting.Lua/LuaViews.cs`). `ReadOnlyViews.Wrap` gives a Lua table a userdata
  view with `__index`, `__newindex` (refused: "a table another mod exports is read-only"), `__len`, `__pairs`,
  `__ipairs` and a locked metatable. A view is made once for each table and kept. Each mod's environment has its
  own set.
- **The trace** (read: `Natives/Scripting.Trace.cs`). One JSON line per handler call: `mod`, `handler`, `hook`,
  `kind`, `at`, `ms`, `result`, `error`, `within`, `fields`, `writes` (the first 1,000, then `writes_not_listed`) and
  `calls`. `mod` is the handler's mod. A write's `through` is the mod whose access made it when that is not the
  call's mod, so a base mod's helper writes "through" the base mod for its caller. The line does not say how many
  instructions the call ran.
- **The watch records** (read: `Natives/Scripting.Watches.cs`). `Recorded` names the handler's mod as the writer
  while a handler runs (`_running`).
- **The log and the notices** (read: `Scripting.Failed`, `Scripting.Console.cs`). A failure is logged under the
  handler's mod as "FILE:FUNCTION failed at WHERE: message" with the traceback, and given a notice. Under
  `--lua-errors halt` the run ends with "lua-error: MOD FILE:FUNCTION at WHERE: first line".
- **Failures** (read: `Scripting.cs`). `Scene.FailuresInARow` counts by handler and is reset by a success. At three
  (`FailuresBeforeSwitchOff`) the handler joins `SwitchedOff` for the session. `IsOn` checks that set and
  `IModSession.IsActive`.
- **The budget** (read: `ModLimits`, `LuaModSession.Invoke` and `Guarded`). A handler call may run
  `HandlerInstructions` (1,000,000). A count hook adds 1,000 every 1,000 instructions (`HookInterval`), so the count
  goes in steps of 1,000. A call made inside another one has its own budget.
- **The build** (read: `LuaModSession.BuildMod`). It runs `init.lua` and loads each file that a handler of the mod
  names, then finds each handler's function. A mod that fails is off, and so is every mod that requires it. So an
  owner's handler file is loaded only in a stack with a document of its kind, as spec §5.2 asks, if the link adds
  the handler only when it plans a delivery.
- **`IModSession`** (read) has one implementation, `LuaModSession`, so its two changed methods change one class.
- **`omp build` and `omp check`** (read: `Tools/OmpCommands.cs`, `Launch/ModInspection.cs`). Both link: `build`
  links the package with the shipped packages it requires and refuses it on a failed link, and `check` links the
  stack. Both get the data checks through the link.
- **Tomlyn 2.10.1** (read: its API documentation). `TomlException` has `Line` and `Column`, a date or time is a
  `TomlDateTime`, and an integer is 64-bit. So an integer past 2^63 is already a syntax error, and one between 2^53
  and 2^63 is ours to refuse.
- **The project boundary** (read: `ProjectBoundaryTests`). It checks that only `Ome.Scripting.Lua` names a Lua
  library. Nothing checks TOML. Only `Ome.Mods` references Tomlyn.
- **The largest table** (ran: a one-off count over both record maps): Kamidori's `item`, with 2,000 records and 48
  authored fields, which hold 150 cells a record. A file that sets every authored cell has 300,000 values. Next come
  Kamidori's `unit` (178,000), Himegari's `unit` (144,000) and Himegari's `item` (105,000).

### 4.2 What the plan settles for M1

| Question | Settled |
|---|---|
| A `[[data]]` file's path (spec §5.1) | Relative to `data/`, with forward slashes, no empty, `.` or `..` part, and ending in `.toml`. A package names a file once |
| `[[accepts]] shape` before M3 | **Refused**, with "shape checks come with data patches milestone 3". Taking it and checking nothing would skip a check without notice (spec §7) |
| A kind's name | A dotted name, as an event's (`ManifestReader.DottedNamePattern`) |
| The plain value type (spec §8) | `DataValue` in `Ome.Engine/Modding`: a table (string keys in the file's order), an array, a string, a boolean, an integer (a `long` from -2^53 to 2^53) or a finite float. It has no TOML type in it |
| The documents in the link cache (spec §5.4) | **The files as they were read**, under `data/<mod>/<path>` in the cache entry, listed in `link.json`'s `lua.contributions`. The mount reads them again with the same reader. That is how the cache keeps the Lua sources, and it needs no second encoding of the values to keep in step. (§4 of this plan had said "through `ModsJsonContext`"; the list is, the documents are not) |
| The contribution (spec §5.2) | A read-only view of `{ mod, file, data, unraised }`: `file` is the package path (`data/slimes.toml`), `unraised` an array of the kind's events that the install does not raise, in the order of its `at` entries. It is built at its first delivery in a session and kept for the session |
| `unraised` in M1 | `apply_boot` ignores it: the boot tables have one event. M2's `apply_stage` refuses a document with a stage part when `<game>.stage.loaded` is in it (spec §6.2) |
| Delivery order within one mod (spec §5.5) | A pending entry gets a phase, 0 for a delivery and 1 for a hook, sorted after priority and load order. So a mod's files come before its hooks at one priority, its files in manifest order |
| What the trace line of a delivery says (spec §5.7) | `mod` is the contributor; it gains `file` and `owner`, and `handler` is the owner's. Every line gains `instructions` (in steps of 1,000) and `budget`, which gate 10 reads |
| The log and the halt reason of a delivery (spec §5.6) | `example.a data/a.toml (kamidori-base tables.lua:apply_boot) failed at kamidori.data.layered at INIT2@0x20d: …`, logged under the contributor. A switched-off file's notice is "Lua: example.a data/a.toml failed 3 times in a row and is not applied again this session" |
| The notice of an owner or contributor that is off | Its "is off for this session" notice ends with "; not applied: example.a data/a.toml, …" |
| The report's kinds (spec §5.7) | `kind K (OWNER), at E1 and E2`, then one line for each document in delivery order, `MOD FILE: N values`, then the shared paths, at most 20, then "and N more shared paths". A document is named by its mod, and by its mod and file where the mod has more than one document of the kind |
| A path's text (spec §5.7) | Its keys joined by dots, a key that is not a bare TOML key written as a quoted one |
| Old names in the registry (§9) | A `[[table]]` takes `aliases = ["old"]`; its fields take `[table.field_aliases] old = "current"` |
| Old names in `exports.txt` (§9) | `table NAME`, `table OLD = NAME`, `field TABLE.FIELD` and `field TABLE.OLD = TABLE.FIELD`, after the globals |
| Old names in the record map (§9) | `old_tables = { old = "current" }` beside `tables`, and `old_fields = { old = "current" }` in a table, each only when there is one. The map has not been released, so its format stays 1 |
| The applier's old-name line (§9) | Through `config.log`, which `tables.lua` sets to `maid.log.warn`: `example.a data/a.toml: units is an old name of the table unit; write unit`, once for each file and old name in a session |

### 4.3 Gate 10's ceiling and the bounds, named before measuring

Named on 2026-11-18, before any data file had been parsed or delivered.

- **The ceiling.** The full Kamidori item file of §4.1 (300,000 values) adds at most **10 seconds** to a boot and at
  most 10 seconds to a load, in the engine's test build on this machine. The time counts the parse at the mount, the
  view, the check and the writes. It is a pathological file: a balance mod sets hundreds or thousands of values,
  and §10 records what such a file costs too.
- **The bounds.** One document holds at most **400,000 values** (a third above the full item file), is at most
  **16 MiB**, nests tables and arrays at most **16 deep** (a `<game>.tables` file needs 6), and has keys of at most
  **128 characters** with no control character. A package names at most **64 data files**. Task 8 checks that the
  full item file fits under them and changes a number only if it does not, with the reason.
- **The allowance.** Until task 8 measures it, 1,000 instructions for each value. Task 8 measures the instructions
  for each value from the trace, on three files: the full item file, a valid file at the value bound, and a file at
  the value bound of which every value is refused. The allowance becomes the largest of the three, doubled and
  rounded up to a round number.

### 4.4 Tasks

In commit order; each commit builds and passes.

1. **The plan** (this section and §9).
2. **Format 4 and `data/`.** `ModManifest`: `CurrentOmpVersion = 4`, `DataOmpVersion = 4`, `Data` (index, kind, file,
   priority) and `Accepts` (index, kind, description, its `at` entries of event and handler). `ManifestReader`:
   both need `omp = 4` and `[game]` with `target = "extended"`; `[[accepts]]` needs `[lua]`; a library has neither;
   the path rule and the 64-file bound; `shape` refused; one kind per manifest once. `ModLayout`: `data/` from
   format 4, each file named by one entry, each entry's file present, a file over 16 MiB refused, an `at`'s handler
   file under `lua/`; `ChangesNoContent` false with data. Tests in `ModPackageTests`. `docs/modding/packages.md`.
3. **The document.** `DataValue` (`Ome.Engine/Modding`), and `DataDocumentReader` (`Ome.Mods/Data`) over
   `TomlDocument.Parse`, by hand: the bounds of §4.3, a syntax error with its line, a date, an integer past 2^53, a
   float that is not finite and a key with a control character each refused with its path. Tests in a new
   `DataDocumentTests`. `ProjectBoundaryTests` gains the TOML check (gate 13).
4. **The link.** In `ModLuaLinker`: the kinds (step 1), the data files (step 2) and the deliveries (step 3), with
   the note for an unraised event and the contributors' failure when none is raised. `ModContribution` and
   `ContributionId`, `ModRuntimeStack.Contributions`, `PlannedHandler.Contribution`, the phase of §4.2. The report's
   kinds. The cache: `lua.contributions`, the files under `data/`, `TryOpen`. Tests in a new `DataLinkTests` (gate
   2, the report, the cache). The generic guide page `docs/modding/data-files.md`, first part.
5. **The delivery.** `IModSession.IsActive` and `Invoke` take the contribution; `ModLimits.DataInstructionsPerValue`;
   the Lua session's contribution view and second argument, and the budget with the allowance. In the scripting
   native: `Call` with the contribution, failures counted by file, the contributor in the trace, the watch records
   and the log, the trace's `instructions` and `budget`, the notices of §4.2. Tests in a new `DataDeliveryTests`
   (gates 1, 3 in part, 4 and 9, and the halt). `docs/modding/data-files.md`, `lua.md`, `troubleshooting.md`.
6. **Old names** (§9). `globals_build.py`: the lint, the JSON and the reference. `base_mod_build.py`: table and
   field names in `exports.txt`, the rename rule over them, the record map's old names. The applier: old names, the
   log line, and a cell named twice refused. Tests: `tools/test_base_mod_build.py` and `DataPatchTests`, on a
   synthetic registry and map. Regenerate both base mods. `docs/modding/data-files.md`, the backlog's gap item,
   `docs/tools-reference.md`.
7. **The base mods accept `<game>.tables`.** Both manifests at `omp = 4` with `[[accepts]]`; both
   `events/boot.toml` descriptions with spec §6.6's sentence; `base_mod_build.py --build` and
   `modding_games_build.py --build`; the game pages' kinds, tables and fields. Tests: `BaseModTests`, and
   Workspace tests for gates 3, 5, 6 and 7 on the installed games.
8. **The budget and the bounds, measured** (gate 10, §4.3). The numbers go into §10 and the constants.
9. **Closeout.** The backlog's data-patches line, §10, `docs/tools-reference.md` and `docs/PROJECT-STRUCTURE.md`,
   the NativeAOT publish, and the full checks.

## 5. Milestone 2: stages, and both games driven

Planned on 2026-11-18, after M1 was accepted and its five corrections were made. §5.1 checks what M2 relies on, §5.2
settles what the spec leaves open, §5.3 plans the driven gates and the derived values, and §5.4 lists the tasks in
commit order. §11 keeps what M2 finds while it is built.

### 5.1 What the scripts and the code say for M2

"Ran" and "read" are as in §1. The disassembly read is the main checkout's `build/games/<game>/disasm/` (the
untranslated Kamidori), read and not regenerated; a scan of it lists every instruction with an operand inside the
fifteen `stage_enemy_*` arrays of each game.

- **Himegari's stage enemy arrays** (read: `vm-map/globals.toml` and the scan). Fifteen arrays of 30 slots, from
  `stage_enemy_spawn_tile_x` (`0xe773f`) to `stage_enemy_forbidden_story_flags` (ending at `0xe7aa4`): eleven of one
  cell a slot, and four rows, the movement and battle routines (`[30][3]`, columns `difficulty_0` to `_2`) and the
  required and forbidden story flags (`[30][7]`, `[30][5]`, named columns). All lie above the saved prefix
  (`0x6241b`), so none is state.
  - **Only STINIT writes them.** It zeroes every slot of each array (`copy-to-global`, `STINIT@0x54` to `0xb6`), then
    branches on `current_stage_id` (74 stages, ids 1 to 170, none 0) and writes that stage's slots (`mov`, and
    `copy-inline-int-array` for the rows). No stage gives slot 0 a unit.
  - **Readers:** FIELD (the placement at a stage's start, slots 0 to 29 from `FIELD@0x7f4`; and `FIELD@0x8533`),
    ADDEN (one slot, `current_stage_enemy_spawn_slot`, `ADDEN@0x26`: a unit a stage's handler adds later) and SETEN
    (the routines into the entity).
- **Kamidori's** (read: the same scan, `vm-map/globals-kamidori.toml`).
  - STINIT zeroes them (`STINIT@0x7b` to `0xdd`) and writes its stages (181 ids, 1 to 968); each append's `$1$`,
    `$2$` and `$3$STINIT` writes its own (up to 977). The table's `usage` says "STINIT2 writes them": STINIT2 writes
    the stage names.
  - Readers: FIELD, ADDEN, CHANGEEN, SELSTAGE and SETEN.
  - The registry's `stage_enemy` table has eleven fields. The four rows are typed `int[30x3]`, `int[30x7]` and
    `int[30x5]`. The layout reads that notation (`base_mod_build.dimensions`), but `globals_build.dims_of` and the OMS
    module loader (`GameModule.TypeShape`) do not, which is why the table left them out.
- **Who runs the stage loaders** (read).
  - Himegari: FIELD alone (`FIELD@0x200`, `stage_loader_script_ids[current_stage_id]`). No other script names that
    array or calls STINIT.
  - Kamidori: FIELD (`FIELD@0x50c`), SELSTAGE (`0x13ed`, the stage list's enemy preview), ORGANIZE (`0xa7b`), INFO3
    (`0xbfd` and `0xd05`), ARENA (`0x14da`), and INIT2 at a load when `G[0x428c]` is set (`INIT2@0x298`).
  - **INFO3 puts a floor's tables back as authored.** INFO3 is the third tab of INFO (`INFO@0x4f`), which FIELD opens
    from its menu (choice `0xc`, `FIELD@0x28ac`), as MENU and STUDIO2 do. It runs every stage's loaders in turn
    (`INFO3@0xb88` to `0xc12`, `current_stage_id` from 1 to 999). Then, when `G[0x428c]` is set, it puts
    `current_stage_id` back and runs that stage's loaders again (`INFO3@0xcdc` to `0xd1a`). FIELD sets `G[0x428c]` on
    entering a floor (`FIELD@0x1fd`) and clears it when it leaves the floor for the arena, the town or the world map
    (`FIELD@0xcafe` to `0xcb54`); scenes write it too. So that second run happens on a floor. No event is raised
    there.
- **The game reads the enemy table after the placement** (read), on both games. Kamidori's FIELD spawns
  reinforcements from it each time a spawner's interval comes round (`stage_object_reinforcement_interval_turns`;
  `FIELD@0x9d9f` calls `label_9f58`, which reads `stage_enemy_unit_id` at `FIELD@0xa040`). Himegari's ADDEN places one
  slot when a stage's handler calls it. So a Kamidori floor visited in INFO's third tab spawns the authored enemies for
  the rest of the floor, whether a data file or a Lua handler made the edit. Spec §6.5 listed this as not known.
- **Where `<game>.stage.loaded` binds** (ran: `ModLuaLinkTests.HimegarisBaseModRaisesItsBootAndStageEventsAtTheInstalledScripts`
  and `KamidorisBaseModRaisesItsBootAndStageEventsOnBothInstalls`). Himegari `FIELD@0x20a`; Kamidori `FIELD@0x527`,
  under both fingerprints. Its one field, `stage`, reads `current_stage_id`. The translated install's
  `patch/INFO3.BIN` has the restore loop at the same offsets (read: `sys4load.py`, `INFO3@0xcdc` and `0xd20`).
- **What entering FIELD again does** (read; for Himegari the registry also records a port run on 2026-11-15).
  - Himegari, `field_map_continue_mode` (`G[0x767d]`): 0 at a stage's start. FIELD runs the loader unless it is 1
    (`FIELD@0x1f2`) and places the units only at 0 (`FIELD@0x3d8`). It writes 1 once the field is up
    (`FIELD@0xfbe`). FIELD leaves for CAMP with `system_flow_request` 5 (`FIELD@0x230e`), and CAMP writes 2 or 3
    when it returns to the field (`CAMP@0x73`, `CAMP@0x300`).
  - Kamidori (`G[0x4290]`): FIELD runs the loaders unless it is 1 (`FIELD@0x4c9`) and places the units only at 0
    (`FIELD@0x67d` skips to `0x1264` otherwise). It writes 1 once the floor is up (`FIELD@0x144d`). When FIELD leaves
    for an ADV scene (`G[0]` 1) it writes 2 (`FIELD@0xc5a8` to `0xc622`), so a scene that leaves the floor brings
    FIELD back at 2.
- **The field's save command** (read). Himegari's menu choice 9 writes the mode 2, calls SAVE.BIN and writes 1
  (`FIELD@0x2349` to `0x2356`); choice 10 calls SAVE.BIN to load (`G[0x6241b]` 1). Kamidori's choice `0xd` does the
  same (`FIELD@0x28cb` to `0x28d8`). A save made with it loads at 2: FIELD runs the loaders, the event is raised, and
  nothing is placed, so the units on the field are the save's.
- **The applier** (read: `mods/ome-records/lua/init.lua`). A top-level key names a table of the map, or an old name of
  one; a table whose `built_by` is not one of `labels` is refused. `apply` writes every part of the tables that
  `options.built_by` names. `problems` takes the document alone, so it cannot see `unraised`.
- **The generators** (read). `globals_build.py` lints `[[table]]` entries and emits them; `base_mod_build.py` writes
  the record map's `tables` and `old_tables` and `exports.txt`'s `table` and `field` lines. Neither knows a scope.
- **The engine** (ran: a search of every engine source outside the tests). No source names a base mod, a kind or a game
  event (`himegari-base`, `kamidori.tables`, `.data.layered`, `.stage.loaded`). A second `at` entry, the scopes and a
  new point are the base mods' and the registry's, and need no engine change.

### 5.2 What the plan settles for M2

| Question | Settled |
|---|---|
| Where the scope facts live (spec §6.2) | A **`[[scope]]`** entry in the registry: `name` (`stage`), `first` and `last` (the ids' range), `usage`, and `[scope.tables]`, each short name to its table (`enemy = "stage_enemy"`). The table keeps its own `[[table]]` entry. `globals_build.py` lints it (a lowercase name that is no table's, alias's or global's; whole numbers with `0 <= first <= last`; a short name that is a lowercase identifier and a table of the registry; a table in one scope at most), the JSON lists it under `scopes`, and the reference shows it beside the tables |
| The stage ids' range | **1 to 999 on both games.** The per-stage arrays have 1,000 cells (`stage_loader_script_ids[1000]` in Himegari, `stage_display_names[1000]` in both), and no loader has a stage 0. An id inside the range that no loader writes is taken and never applied, since no stage of that id loads; the guide says so |
| How a scoped table is reached | Only through its scope: `stage.<id>.enemy.<slot>.<field>`. Its own name at the top level is refused with that spelling. An old name of a scoped table is not a short name |
| The record map | `scopes = { stage = { first = 1, last = 999, tables = { enemy = "stage_enemy" } } }` beside `tables`, only when the registry has a scope. Format 1 stays: the map has not been released |
| `exports.txt` | `scope stage` and `scope stage.enemy`, after the fields. A name that leaves is refused unless the major version is raised. A scope has no old-name form: nothing is renamed, and §9.1's decision covers tables and fields |
| The applier's API | `config.events` (optional), each label to the event the caller applies it at: `{ boot = "<game>.data.layered", stage = "<game>.stage.loaded" }`. `apply(c, { built_by = "stage", scope = { stage = 12 } })` writes the parts of stage 12 whose table that label builds; without `scope` it writes the unscoped tables of the label. `scope` names scopes of the map, with whole-number ids |
| The unraised rule (spec §6.2) | The check takes the contribution. A table whose label's event (`config.events`) is in `contribution.unraised` is a problem at its path (`unit`, or `stage.12.enemy`), "stage_enemy is applied at <game>.stage.loaded, which this install does not raise", so the document is refused whole. A file with boot tables only is applied |
| The scope's messages | an id outside the range (`stage.0: stage has ids 1 to 999`), an id not in decimal (as a record id), a short name the scope lacks (`stage.12.enemyy: stage has no table enemyy (tables: enemy)`), a scope or an id that is not a table, and a scoped table at the top |
| Himegari's `stage_enemy` | 30 records, `built_by = "stage"`, the fifteen arrays, named as Kamidori's fields are (Himegari's `first_clear_only` where Kamidori has `first_visit_only`) |
| Kamidori's `stage_enemy` | **Gains the four rows**, so both games offer the same fields. Their types are written `int[30][3]`, `int[30][7]` and `int[30][5]`: the layout is the same, and OMS can now name them. Their columns stay unnamed, so a file gives them by number. The `usage` says STINIT |
| INFO3 (§5.1) | **`kamidori.stage.loaded` gains a second point,** `kamidori.stage.after_info_reload` at `INFO3@0xd20`, the instruction after the restore loop, under both INFO3 fingerprints. A file's stage edit, and a Lua handler's, then holds after a visit to INFO's third tab. It is the base mod's choice (spec §6.2), so §9 takes nothing |
| The kind's description | "Edits to the game's record tables: units, items and skills, which the boot builds, and each stage's enemies, which its loader writes." |
| The base mods' version | 1.1.0 stays (§10.4): the stage part and the second `at` arrive before any release |
| The example mods | `oms/examples/mods/example.stage-enemies` (Himegari) and `example.kamidori-stage-enemies`, data only, and a mistake twin of each for gate 8's error half (`example.stage-enemies-mistake`, `example.kamidori-stage-enemies-mistake`), as `example.depth-error` raises an error on purpose. The README says what each shows |

### 5.3 The driven gates, and the derived values

Each driven run uses a sandbox of its own (`dp-…`) and the worktree's port (`tools/drive.py port`), and each reusable
run is a `.steps` file under `tools/drive/scenarios/port/`. A step reads the table and the placed units with a `lua`
step in the base mod's environment (`lua mod=<game>-base`).

- **Gate 8 on Himegari.** A new game reaches stage 1, as `himegari-random-enemies.steps` does.
  `example.stage-enemies` gives stage 1's slot 2 another unit and stage 2's slot 3 another.
  1. At stage 1, `stage_enemy_unit_id` holds stage 1's edit in slot 2, and slot 3 keeps stage 1's own unit: stage 2's
     edit leaves another stage as it was. The placed entities (`entity_unit_definition_ids`) include the new unit
     and lack the one it replaced, and a shot shows it.
  2. FIELD entered again: to CAMP and back (`system_flow_request` 5, then the mode 2 or 3). The table holds the edit.
  3. The field's save command (choice 9), then a load of that slot: the table holds the edit, and the placed units
     are the save's.
  4. `example.stage-enemies-mistake` on a new sandbox: the boot's notice and `diagnostics/lua.log` name the file and
     the stage part's path, and at stage 1 the table is the game's own.
- **Gate 8 on Kamidori.** A sandbox seeded from the main checkout's port sandbox `k1clean` (read only). From a
  workshop save (014, before 一陣の訓練地, or 019, before the lake), the route enters a floor afresh (the mode 0).
  `example.kamidori-stage-enemies` edits the floor entered and one other. The same four steps follow: the placement,
  the table after FIELD is entered again (a visit to INFO's third tab with the new point, and the load of a field save),
  another stage as it was, and the mistake twin. The UI points are in the sweep's memory notes.
- **If a route cannot be driven** with reasonable effort, that part stops and the report says what blocked it. A
  headless test does not stand in for a driven step.
- **Gate 11.** Tests (§5.4, task 6): both base mods declare `<game>.tables` with the same `at` entries but for the
  game's name, their `tables.lua` files are the same text but for it, and no engine source names a kind, a base mod
  or a game event. The driven runs use one engine build for both games.
- **The derived values** (backlog, "What each game copies from a field a data file can set"). Its done-condition: the
  guide says, for each field a file can set, which saved values the game derives from it and when; each claim is
  checked on the game's scripts; and one copy is driven.
  - Each claim of §8 is read again at its offset, with the condition each copy is made under (Himegari's UNITECH
    condition was not read in M0).
  - A second scan follows a field's value through the locals of a script in any order (M0's followed program order),
    and through block copies, into a cell below the saved prefix. Each candidate it finds is read by hand.
  - The guide pages get a table of the copies: the field, the saved value, and when the game makes the copy. The
    fields the scan finds no copy of are named as read where they are, with the scan's limits stated.
  - The driven copy: on Himegari, a file raises a first-party unit's base stats; after a new game its saved current
    stats hold the raised value; a field save, loaded without the mod, still holds it while the definition is the
    game's own (`example.stage-enemies` stays apart from this; the run uses a mod of its own, kept beside the
    scenario's examples).

### 5.4 Tasks

In commit order; each commit builds and passes.

1. **The plan** (this section, and §10.3's line on the TOML readers).
2. **The registry.** `[[scope]]` in `globals_build.py` (lint, JSON, the reference's scopes), with its tests in
   `tools/test_globals.py`. Himegari's `stage_enemy` table and `stage` scope; Kamidori's four rows (the types), the
   `usage`, and its scope. `globals_build.py --build` for both games (the worktree's `build/` holds the auto maps).
3. **The record map's scopes.** `base_mod_build.py`: `scopes` in the map, `scope` lines in `exports.txt`, the rename
   rule over them; `tools/test_base_mod_build.py`; `base_mod_build.py --build`; `docs/tools-reference.md`.
4. **The applier's scopes and the unraised rule.** `ome-records` (`config.events`, the scope walk, `apply`'s `scope`,
   the check over the contribution), its README; `DataPatchTests` on a synthetic map (each message of §5.2, an apply
   per scope id, a boot apply that skips the scopes, the unraised rule both ways).
5. **The base mods apply the stage tables.** Both `tables.lua` (`labels`, `events`, `apply_stage`), both manifests'
   second `at` and description; `kamidori.stage.after_info_reload` (`vm-map/patch-points-kamidori.toml`) and
   `events/stage.toml`; `patch_points_build.py --build`, `base_mod_build.py --build`, `modding_games_build.py --build`;
   the tests that pin points and offsets; each game's guide page (tables, scope, fields, the loaders and INFO) and
   `docs/modding/data-files.md` (the scoped tables, the unraised rule).
6. **Workspace tests and gate 11.** On each installed game: a stage file shipped by a mod and delivered at the
   installed FIELD's point writes the stage's slots and no other stage's (running FIELD headless where it runs, else
   STINIT and the delivery's handler through the base mod's console); the stage-part problems at boot; a file with a
   stage part on an install where `stage.loaded` is not raised. Gate 11's tests (§5.3).
7. **The example mods,** their README entries, and the guide pages' examples.
8. **Gate 8, driven** (§5.3), with the scenarios.
9. **The derived values** (§5.3): the claims read, the second scan, the guide pages, the driven copy; the backlog item
   goes.
10. **Closeout.** The backlog's data-patches line, §11, `docs/tools-reference.md` and `docs/PROJECT-STRUCTURE.md`, the
    NativeAOT publish, and the full checks.

## 6. Milestone 3: shapes (outline)

- The JSON Schema subset of spec §7, its checker in `Ome.Mods`, the line from a second parse.
- `base_mod_build.py` writes `shapes/tables.schema.json` from the record map, with state fields marked as not
  offered; `omp check` and `omp build` refuse a file that fails. Gate 12.
- **The generated shape takes the old names too** (§9): a table's and a field's old names are allowed where the
  current ones are, so that a file which the applier takes also passes its shape.

## 7. Questions and risks

- **One spelling against aliases.** Decided by the user on 2026-11-18 (§9): an old name keeps working, and the
  report's blind spot for it is stated.
- **The budget.** A console chunk has the handler budget. If the applier's cost per value is high, §2.5's tests stay
  small and §8 says what a whole table would cost.
- **Running INIT2 headless** in the tests: Himegari's runs in `HimegarisBaseModRaisesItsBootAndStageEventsAtTheInstalledScripts`
  already. Kamidori's has not been run headless by a test; if it stops at a wait the headless waits cannot answer,
  the Kamidori test falls back to the binding offsets and says so in §8.

## 8. What M0 found

M0 landed on `feature/data-patches` on 2026-11-18, tasks 2 to 9 of §3. "Ran" and "read" are as in §1.

- **The moment, on both games** (ran: `DataPatchWorkspaceTests`, Workspace).
  - Himegari at boot: a handler at `himegari.data.layered` gives item 999 category 10 and a curve with a bonus at its
    first two levels, and TUNE lowers a seeded saved level of 5 to 2. The same edit at `himegari.data.ready` leaves
    5. INIT2's INIT scripts and the append leave the saved level alone until TUNE.
  - Himegari at a load: a synthetic root calls the installed INIT2 in its prologue, then op `0xae`, sets the level
    to 5 and saves, and a full load of that slot follows. Both events come again with `restoring` true. At
    `data.layered` the level is the 5 the save brought back. The handler makes its edit again on the rebuilt tables,
    and TUNE lowers the level to 2.
  - Kamidori, on both installs: INIT2 runs headless with the headless waits and `EngineBuilds.Age433G`, so the
    fallback of §7 was not needed. `data.layered` comes before `data.ready`. At `data.layered`, unit 761's experience
    reward is EBINIT's 10, and the 999 set there is still in place at `data.ready` and after INIT2.
- **The applier** (ran: `DataPatchTests`, 26 cases, core).
  - §1.3's file writes exactly `G[0x2c1524] = 150`, `G[0x2c9285] = 999` and `G[0x2cb9c1] = 100`. These are the cells
    the shipped layout gives for those names, and the cells `oms/examples/kamidori-gate6`'s `with` block compiles to
    (`TheGate6WithBlockCompilesToTheCellsTheApplierWrites`, Workspace).
  - Each problem of gate 7, and the others of §2.5, is reported with the contributor, the file and the path.
    Nothing is written, and a second `apply` returns 0 and says nothing.
  - The library takes a read-only view, here a library's export, as it takes a Lua table.
- **The messages as built.** §2.2's sample lines were a sketch. The built lines read
  `unit.0x2f9: a record id is written in decimal: 761`, `unit.761.drop_chance.0: column 0 is named: write
  unit.761.drop_chance.drop_1` and `item.12.name: a string cell holds text the save can encode (code page 932);
  U+D55C is not in it`. A single problem is counted as "1 problem".
- **The cost.** Not measured. The largest document M0's tests apply has 25 records, and it runs inside one console
  chunk's budget. Gate 10 in M1 measures the cost per value.
- **The derived values** (read: a time-boxed scan of both games' disassembly, then the strongest candidates by hand).
  The scan follows values through the locals of one script, in program order. It does not follow branches, local
  calls or block copies (`copy-dwords`), so the block copies below were found by hand. These are candidates for the
  guide pages of M2, and the backlog carries them.
  - **When a unit joins, both games copy its definition into saved state** (UNITECH, which adds a unit to the roster;
    GAMESTART calls it for the first party, `docs/engine-re.md`). An edit to one of these fields reaches a unit that
    joins after it, not one that has already joined. What was copied while the mod was on stays in the save after the
    mod is removed.
    - Himegari (the condition UNITECH tests before it copies was not read): `unit_starting_level` into `unit_current_levels` (`UNITECH@0x34b`); `unit_base_stats` into
      `unit_current_stats`, 14 cells (`UNITECH@0x37c`); `unit_starting_skill_ids` into `unit_skill_ids`
      (`UNITECH@0x3a3`); `unit_starting_equipment_item_id` into the party slot's `G[0x6868]` (`UNITECH@0x338`); and
      `unit_stat_growth_rates` into `unit_stat_growth_fractions` for each level the join adds (`UNITECH@0x419`, and
      at each level up, `ADDEXP@0x20b`).
    - Kamidori, when `character_levels` is 0 (`UNITECH@0x13a`): `unit_default_costume_item_id`, given as an item
      through ADDITEM (`UNITECH@0x129`) and kept in `character_costume_item_id`; `unit_initial_party_capacity` into
      `party_cost_capacity` (character 1); `unit_party_cost` into `character_party_cost`;
      `unit_initial_affinity_tenths`, times 10, into `character_affinity`; `unit_starting_level` into
      `character_levels`; and `unit_initial_weapon_level` into `character_weapon_level`. The block copies are
      `unit_base_stats` into `character_stats` (`UNITECH@0x1e1`), `unit_initial_stat_caps` into
      `character_stat_caps` (`0x208`), `unit_element_resistance_levels` into `character_resistances` (`0x22f`), and
      `unit_skill_ids` into `character_skill_list` (`0x256`), with `character_learned_skill_flags` set for each
      (`0x2a7`). `unit_stat_growth` feeds `character_stat_growth_fractions` (`UNITECH@0x349`, `ADDEXP@0x276`).
  - **The battle copies.** SETEN, SETCH, CALCREVISE and EQUIP, in both games, write unit, item and skill fields into
    the battle entity arrays (Himegari's `entity_*`, Kamidori's `battle_unit_*`), which lie below the prefix. A save
    made during a stage keeps the entities as they were placed.
  - **Himegari's TUNE** (spec §6.5) lowers the saved levels through its loop's branches, which the scan does not
    follow. The moment test above runs it.
- **The spec and the code.** Each disagreement of §1 was handled as follows:
  - the stale `data_ready` descriptions were rewritten (task 4);
  - "the fingerprints the profile lists" is wording only: they are in `vm-map/patch-points-kamidori.toml`;
  - `LuaStubs.Inputs` links the shipped base mod with what it requires (task 6);
  - the launcher shows `ome-records` as an engine row. Its note now names the shipped package that requires it,
    "Mounted because kamidori-base requires it." (`ModsViewModel`), where before it would have said only "mounted by
    the link";
  - save stamps and the Compatibility mode behave as §1 says, and need no change.
- **For M1.**
  - Both boot events' descriptions gain the sentence of spec §6.6 about data files (a handler at `data.ready` runs
    after every data file; one that should take its place among them by load order moves to `data.layered`) when the
    format is bound. M0 left it out, since no data file reaches the event yet.
  - The question of §7, one spelling against aliases, was open when M0 ended. §9 has the user's answer.

## 9. What changed since the spec was accepted

The spec stays as it was accepted. This section keeps the decisions taken after it.

### 9.1 An old name keeps working (2026-11-18)

**The question** (§7): spec §6.1 gives each cell of `<game>.tables` one spelling, so that two files that set one cell
always share a path in the report. Spec §6.3 has a renamed table or field keep its old name until the base mod's
next major version. Both cannot hold at once.

**The decision.** The user, on 2026-11-18, to the parent session's recommendations: "Go ahead with your
recommendations". A renamed table or field keeps its old name until the base mod's next major version, as spec §6.3
has it, and M1 builds it:

- **The registry.** A `[[table]]` and each of its fields can carry old names, as a global carries `aliases`.
  `globals_build.py` lints them: an old name must not be a current name or another old name of the same table.
- **`exports.txt` and `--check`.** Table and field names join the list. A name that left the registry is allowed
  when it is kept as an old name; otherwise the base mod's major version must be raised, the rule the globals follow.
- **The record map** carries the old names, so that the applier can resolve them.
- **The applier** takes a table or field by an old name. It writes the cell, and logs one line for each file and
  old name that names the contributor, the file and the current spelling. A document that names one cell under an
  old and a current name is a problem of the document, and nothing of it is applied (TOML cannot catch that).
- **The gap, stated.** The link report still compares paths as text: spec §3, decision 1 stands, and the engine does
  not read the record map. So the report misses an overlap between a file that uses an old name and a file that uses
  the current one, until the next major version drops the old name. The modder guide says so where it describes the
  report, and the backlog carries it.
- **Milestone 3.** The generated shape must take the old names too (§6).

**What it amends.** The third answer under "Answers the review led to" in spec §14: `<game>.tables` has one
spelling for each cell, except an old name kept after a rename.

**Where it is built:** §4.2 (the formats) and §4.4, task 6. No registry renames a table or a field today, so the
tests use a synthetic registry and a synthetic record map.

## 10. What M1 found

### 10.1 Gate 10, measured (task 8, 2026-11-18)

Ran: `DataPatchWorkspaceTests.TheLargestFilesCostLessThanHalfTheAllowanceAndTheItemFileStaysUnderTheCeiling`
(Workspace), on the untranslated Kamidori, in the engine's Debug test build on this machine. Each file is built from
the record map and a plain boot's values, shipped by a mod with no Lua, linked with the shipped base mod, and
delivered while the installed INIT2 runs. The instructions are the delivery's trace line (in steps of 1,000), measured
with an allowance no file reaches. The times are one run each, after a warm-up boot, and the ranges are over the three runs made while the library
changed below; the instructions did not change between them.

| File | Values | Size | Instructions | A value | Link | Boot | Load |
|---|---|---|---|---|---|---|---|
| the first 1,000 values of the item file, a balance mod's size | 1,000 | 34 KB | 102,000 | 102.0 | 0.01 s | 0.02 s | not run |
| every item field a file may set (2,000 records, 48 fields, 150 cells a record) | 300,000 | 10.0 MB | 30,708,000 | 102.4 | 1.5 to 2.0 s | 0.8 to 1.2 s | 1.6 to 1.7 s |
| the item file and unit records up to the value bound | 400,000 | 12.6 MB | 41,146,000 | 102.9 | 1.8 to 2.0 s | 1.1 to 1.3 s | not run |
| the bound file, every value of the wrong kind | 400,000 | 13.3 MB | 45,777,000 | 114.4 | 1.9 to 2.4 s | 0.9 to 1.0 s | not run |

- **The ceiling holds.** A boot with no mod takes 0.02 s and a load 0.8 to 0.9 s. The item file adds about 2.5 to 3
  seconds to a boot (its link and its delivery) and about 0.8 s to a load (the view, the check and the writes again in
  the new session), against the 10 seconds of §4.3. The test asserts both, with the margin a timing check needs.
- **The allowance is 250 instructions a value:** the largest cost, 114.4, doubled and rounded up. The test fails when
  a measured cost passes half the allowance, so a slower applier shows there first.
- **The bounds stand.** The full item file fits under all of them: 300,000 values of the 400,000, 10 MB of the
  16 MiB, well under the 16 levels, and keys far under 128 characters. No number changed.
- **The budget at the bound.** `AFileAtTheValueBoundIsAppliedInsideItsBudgetAtBootAndAtALoadAndARefusedOneFailsItsCheckNotItsBudget`
  runs the two bound files with the engine's own limits, at boot and at a load: the valid one is applied, and the
  refused one fails its check with "400000 problems", not the budget.
- **What the measuring changed in `ome-records`.** The first run of the refused file took 22 s at boot, against 1 s
  for the valid one, with no more than twice its instructions. The library kept every problem in a list and sorted
  it; with LuaCSharp, appending 400,000 entries to one table cost the time (a list kept to its first 20 took 1.2 s).
  It now keeps the count, the first 20 problems by path, and each old name once.
- **A file a balance mod writes** costs nothing a player sees: 1,000 values link in 0.01 s, and the boot takes the
  0.02 s it takes with no mod. The Lua runtime was already started in the test process, so its own start is not in
  these numbers.

### 10.2 The gates and their tests

M1 landed on `feature/data-patches` on 2026-11-18, tasks 1 to 9 of §4.4 (`942c66d7` to the closeout). Each gate is
proven by tests that ran: "core" runs in `dotnet test --filter "Category!=Workspace"`, "Workspace" reads the
installed games.

| Gate | Tests |
|---|---|
| 1. Off means off | `DataLinkTests.AnOwnerWithoutAContributorLinksAsItDidAndGetsNoHandler` and `AStackWithoutDataWritesNoContributionsToTheCache`; `DataDeliveryTests.AnOwnerWithoutAContributorRunsAsItDidAndItsHandlerFileIsNeverLoaded` (core). Every test that links a shipped base mod with no data file runs it as before |
| 2. Link | `DataLinkTests` (each case that stops the load, with a top-level `error()` in every owner's Lua that only running it would show); `DataDocumentTests` (the syntax error with its line, the date, the integer past 2^53, the bounds); `DataPackageTests` (`[[data]]` in a library or a native package, an unnamed file under `data/`, the 64 files and 16 MiB) (core) |
| 3. Order | `DataDeliveryTests.TwoDataModsAndALuaModRunInTheReportsOrderAndTheLoadOrderDecidesWhichValueHolds` and `APriorityPutsAFileBeforeTheModsLoadedEarlier` (core); `DataPatchWorkspaceTests.AHandlerAtDataReadyFindsEveryFileAppliedWhateverTheLoadOrder` (Workspace, Himegari) |
| 4. Attribution | `DataDeliveryTests.TheTraceAndAWatchNameTheContributorAndItsFile` and `ThreeFailuresOfOneFileSwitchOffThatFileAndLeaveAnotherModsFileDelivered` (core) |
| 5. Same cells | `DataPatchWorkspaceTests.TheSpecsFileShippedByAModLeavesUnit761AsTheWithBlockDoesAndNothingElse` (Workspace, both Kamidori installs), with M0's `TheGate6WithBlockCompilesToTheCellsTheApplierWrites`; on Himegari, whose examples have no `with` block, `OnHimegariAFileLeavesUnit103AsTheSameWithBlockDoesAndNothingElse` compiles one in the test |
| 6. Loads | `DataPatchWorkspaceTests.TheEditHoldsAtBootAtALoadAndAfterAReturnToTitleAndASaveKeepsNoneOfIt` (Workspace, both games) |
| 7. Errors | `DataPatchWorkspaceTests.EachProblemOfAShippedFileIsReportedWithTheContributorTheFileAndThePathAndNothingOfItIsApplied` (six cases on each game) and `UnderHaltAShippedFilesProblemEndsTheBootNamingTheContributorItsFileAndThePath` (Workspace, both games); `DataDeliveryTests.UnderHaltADeliverysErrorEndsTheRunNamingTheContributorAndItsFile` (core) |
| 9. Not shaped by records | `DataDeliveryTests.TheOwnerGetsTheContributionAsAReadOnlyViewMadeOnceForTheSession`: the kind is a list of strings and the stack has no base mod (core) |
| 10. Size | §10.1 |
| 13. Layers | `ProjectBoundaryTests.OnlyTheModsProjectNamesTheTomlLibrary` (core) |

- **Gate 6's return to title** runs op `0x9` into a synthetic script 0 that calls INIT2, as the game's root does in
  its prologue (M0's load test, §8). The installed SYSTEM4 is not run.
- **Both games.** Gates 5, 6 and 7 run on both games since the corrections after M1's review (2026-11-18); gate 3
  runs on Himegari and gate 10 on Kamidori, whose item table is the largest. Gate 11 (milestone 2) runs both base mods
  on both games.
- **Old names** (§9.1): `tools/test_base_mod_build.py` (the lint, `exports.txt`, the record map and the rename rule,
  on a synthetic registry) and `DataPatchTests.AnOldNameWritesTheCellItsCurrentNameDoesAndIsLoggedOnceForEachFile`
  and `AFileThatNamesOneCellByAnOldNameAndItsCurrentOneIsRefusedWhole` (on a synthetic record map).

### 10.3 Where the plan, the spec and the code disagreed

- **§4.1 said an integer past 2^63 is already a syntax error** (read, from Tomlyn's documentation). It is not: a
  probe of Tomlyn 2.10.1 found that 2^63 to 2^64-1 wrap to negative numbers (`18446744073709551615` reads as -1), in
  both of its parsers. The reader reads each integer token's text and refuses one past 2^53 with its line.
- **A key given twice.** Spec §6.1 says one file cannot name a cell twice because TOML refuses a repeated key.
  Tomlyn's serializer takes one and keeps the last; only `SyntaxParser.Parse(…, validate: true)` refuses it. The
  data files' reader validated first, so the spec held for data files, and the other readers did not. After M1's
  review every TOML reader of the engine and the launcher refuses one through `TomlDocument.Parse`, with the line,
  as it refuses a syntax error (`RepeatedTomlKeyTests`).
- **An integer's problem names its line, not its path,** since it is found in the syntax tree. The guide says so.
- **§4.2's log example** gives the place as `INIT2@0x20d`; the engine prints `INIT2.BIN@0x20d`, as every hook's
  place. The guide gives the printed form.
- **The halt reason has the error's first line** (§4.2). For a `<game>.tables` file that was "N problems, so nothing
  of it is applied", with no path. After M1's review the library puts the first problem on that line
  ("…, so nothing of it is applied: unit.761.x: unit has no field x"), so the notice and the halt reason name a path;
  the rest follow in `diagnostics/lua.log`.

### 10.4 What M1 settled beside §4.2

- **The base mods stay at 1.1.0.** That version has not left this branch, and `<game>.data.layered` and the kind
  arrive in it together. A data mod requires `>=1.1, <2`.
- **The kind's description** is "Edits to the game's record tables that the boot builds: units, items and skills."
  M2 adds the stage's enemies with the stage event.
- **The report counts "1 value"** in the singular.
- **The allowance is 250** (§10.1), and `ome-records` keeps only the first 20 problems and a count.

## 11. What M2 found

M2 landed on `feature/data-patches` on 2026-11-18 and 2026-11-19, tasks 1 to 9 of §5.4 (`50224d09` to `92b94c47`).
"Ran", "read" and "driven" are as in §1; "driven" is a port run through `tools/drive.py port` on a `dp-` sandbox of
the worktree's build.

### 11.1 The gates and their proofs

- **Gate 8, Himegari** (driven: `tools/drive/scenarios/port/himegari-stage-enemies.steps`, `-load.steps`,
  `-load-without.steps` and `-mistake.steps`). A new game reaches stage 1 with `example.stage-enemies`: slot 2 holds
  ハルピュア (810), slot 3 the stage's own unit, and FIELD placed 810 (`entity_unit_definition_ids`). The camp and
  back (the mode 1), the field's save command and its load, and a title load with the mod keep the edit; a title load
  without it has the stage's own table and the placed 810 still on the field. The mistake twin's notice shows at the
  title and `diagnostics/lua.log` names the file and the path. Gap: the shots do not show the enemies (stage 1 is
  dark until explored, at turn 1), so the placement is shown by the cells only.
- **Gate 8, Kamidori** (driven: `kamidori-stage-enemies.steps`, `-load-without.steps` and `-mistake.steps`, from the
  sweep's slot 019 of the clean install, the sandbox seeded from the main checkout's `k1clean`). INFO's third tab
  opened off a floor raises no event (`--lua-trace`). ◆開けた涼風西湖畔◆ (stage 51) entered afresh holds 緑プテテット
  (761) in slot 1, stage 51's slot 3 keeps 水精 (402), and slot 1's spawner placed 761. A console mark in the slot
  before INFO's 獲得地点 view on the floor and before a floor save's load is replaced by 761 each time: the event at
  `INFO3@0xd20` and at `FIELD@0x527` after the load. Loaded without the mod, slot 1 is ハルピュア (331) again and the
  placed 761 stays. The mistake twin as on Himegari.
- **Gate 11** (ran, core: `DataPatchTests.BothBaseModsAcceptTheirTablesAtTheSameEventsByTheSameHandlersButForTheGamesName`,
  `BothBaseModsTablesLuaAreOneTextButForTheGamesName` and `NoEngineSourceNamesAKindABaseModOrAGameEvent`). Every
  driven run used one engine build for both games.
- **The derived values** (read and driven; §11.3). The backlog item is gone.

### 11.2 Where §5.1 and the scripts disagreed

- **The scripts an install runs are not always DATA1's.** §5.1 read Kamidori's INFO3 in the main checkout's
  `extracted-kamidori` DATA1 (`e850c07a…`); both installs run another revision (`3b5e1175…`, the bytes of the
  translated install's `patch/INFO3.BIN`), which the main checkout's `build/games/kamidori/disasm/` shows. So the
  point has one fingerprint, for both installs, where §5.2 said two. In DATA1's revision the restore loop's test
  falls through to `0xd20` off a floor; in the one the installs run, `INFO3@0xcdc` jumps past it to `0xd5d`, and the
  loop leaves `current_stage_id` at 1000. Driven: no event off a floor, the event on one.
- **INFO's third tab is アイテム情報, not a list of enemies.** Its loader loop belongs to the 獲得地点 view of the
  selected item, which looks through every stage's dig points for it. The point's and the event's descriptions, the
  base mod's README and the guide say so.
- **Kamidori's slots are not all placed at a floor's start.** A slot whose `random_selection_weight` is not 0 joins
  the pool of the spawner its `spawn_object_slot` names (`FIELD@0xb42` to `0xb67`); spawners place from their pools
  at the start (`FIELD@0xc8a`) and send reinforcements later (`FIELD@0xa040`). The example's slot 2 was in a pool, so
  nothing showed it at the start; the example edits slot 1, the one placed in the sweep's story state. The guide
  says which slots a floor places, and the Workspace test checks the placement.
- **Himegari's placed units are saved** (`entity_unit_definition_ids`, below the prefix): a save made with a stage
  edit and loaded without the mod keeps the unit the edit placed. Kamidori's battle slots likewise
  (`battle_unit_unit_ids`, with the slot state `G[0x53c16]` bit 0 set for a placed slot; stale slots keep old
  units).
- **SELSTAGE** runs the loaders of one stage for the stage list's preview, the stage under the list's cursor
  (`SELSTAGE@0x13c4` to `0x1405`). This bullet said every stage's, which holds for INFO3 only; after M2's review the
  event is raised there too (§11.5).
- **The port's Kamidori title loop** waits at `TITLE@0x9ab`, where the steps first tried `0x9a6`.
- **One doc comment** in `ManifestReader` named a Himegari event as an example; it names the guide's example.

### 11.3 The derived values

- **Each claim of §8 was read again** at its offset in the scripts the installs run, with its condition: Himegari's
  UNITECH makes the first join's copies for a unit below 100 at level 0 (`UNITECH@0x2cf`, `0x2e4`), after taking the
  unit through `unit_canonical_id` and `unit_roster_variant_ids`; Kamidori's when `character_levels` is 0
  (`UNITECH@0x13a`), through `unit_canonical_character_id`. Both read the growth rates again at each level up
  (`ADDEXP@0x20b`, `ADDEXP@0x276`).
- **The second scan** (a scratch script, not kept) followed each field's value through a script's locals in any
  order, through the array each lookup binds a pointer to and through `take-address` and `copy-dwords` block
  copies, into a cell below the saved prefix, and each cell it found was read by hand. Beside §8's list it found
  SALLY's form change on Himegari (the same copies for a form used for the first time, `SALLY@0x15c4`), and on
  Kamidori the roster's copies at a character's first place in the roster (`unit_initial_equipment_item_ids` into
  `roster_equipment_item_ids`, also at `GAMESTART@0xb0d`, and the first four skills into
  `roster_equipped_skill_ids`) and `unit_initial_class_titles` into `character_class_titles`. The rest were shared
  temporaries (`G[0xfec0c]`, a few of FIELD's locals), the battle units, or the results of play. Its limit: a value
  passed from one script to another through a global is not followed. §11.5 follows those.
- **The guide pages** list the copies, the battle copies and the results of play, and say the rest is read where it is
  used.
- **Driven:** `example.unit-stats` raises リリィ's (unit 2) maximum HP from 14 to 40; a new game with it gives her saved
  stats 40, and its save at SC0000 page 3, loaded without the mod, keeps 40 while her definition is 14
  (`himegari-unit-stats.steps`, `-load-without.steps`). `example.kamidori-unit-stats` raises ウィル's from 18 to 50;
  the sweep's slot 019 loaded with it has his definition at 50 and his saved stats at the save's 23
  (`kamidori-unit-stats.steps`). `DataPatchWorkspaceTests.TheUnitStatsExamplesEditTheDefinitionAtBoot` boots both.

### 11.4 What M2 settled beside §5.2

- **A notice names the file once.** The applier's first line opens with the contributor and the file, and
  `Scripting.DeliveryFailed` put them in front again; the notice and the halt reason now drop the repeat
  (`ANoticeAndAHaltReasonNameTheContributorOnceWhereTheOwnersMessageOpensWithIt`), and the port's status line wraps
  upward instead of running off the window. It also showed "Lua: " twice, which the port no longer adds; that was
  older than this branch.
- **Stage 52 was not run** in M2. The tests and the drives showed stage 52's part leaving stage 51 as authored; after
  M2's review both are run on 蒼月の南湖畔 itself (§11.5).

### 11.5 After M2's review (2026-11-19)

The coordinator accepted M2 and asked for six follow-ups before milestone 3. "Ran", "read" and "driven" as in §11.

- **The stage list's preview shows a file's edit** (driven and ran). The list (SELSTAGE) puts the stage under its
  cursor into `current_stage_id` (`SELSTAGE@0x13c4`) and runs that stage's 32 loader slots (`0x13d0` to `0x1405`);
  from `0x1a8a` the preview collects the unit of each slot 1 to 29 whose gates pass (`label_7ba`) and, for a cleared
  stage only (`stage_cleared_flags`, `0x16b2`), names them in its 出現モンスターリスト. `kamidori.stage.loaded` has
  a third point, `kamidori.stage.after_list_preview`, at `SELSTAGE@0x1408`, the loop's exit, under each install's
  SELSTAGE fingerprint (`c8389a8e…` clean, `be5dae26…` translated; the same code, other text, so the same offset).
  The only subscriber in the repository is the base mods' `apply_stage`, which makes the same change each time. The
  event's description, the point's, the guide and both READMEs say the list raises it for a stage that is not being
  played, with `stage` that stage, and that work that belongs once to a played floor goes to `stage.ready`.
  `ModLuaLinkTests` pins `0x1408` on both installs.
  - **Driven:** `tools/drive/scenarios/port/kamidori-stage-enemies-list.steps` (sandbox `dp-kamidori-stage-list`,
    `--lua-trace`). With the cursor on stage 51 slot 1 holds 761; on stage 52 slot 1 holds 401 and slot 3 762; back on
    51, 761 again, each time after a console mark of 12345 in slots 1 and 3. The trace has four deliveries at
    `SELSTAGE.BIN@0x1408`: two for stage 51 as the list opens (which of the triggers below made the second was not
    settled), then 52, then 51, each writing only its slot. With stage 51 marked cleared in memory (the save has not
    cleared it), the preview's 出現モンスターリスト reads 緑プテテット with the mod and ハルピュア without it (the
    second shot by hand, from the same sandbox without the mod).
  - **How often the list runs the loaders:** when it opens (`SELSTAGE@0x4d`), each time the pointer moves onto
    another stage's panel (`0x1d18`, through the hover routine at `0x1c7a`), each move of the cursor by a key or the
    wheel (`0xa03`, `0xa61`), and when the party screen (ORGANIZE) comes back to the list without setting out
    (`0xcb8`). Not every frame: a pointer resting on a panel runs nothing.
  - **What a delivery costs:** `ome-records` checks a file once a session (`Applier:result`, by mod and file), at the
    boot's `data.layered`, so a delivery at the list writes the stage's part only. The trace gives 0.1 ms for a part of
    one value (4 ms for the session's first, which warms the handler up). A stage's part holds at most 30 slots of 29
    cells, 870 values; at §10.1's cost (102 instructions and about 3 to 4 µs a value at boot) that is about 89,000
    instructions and 3 ms, for each cursor move onto a stage the file edits. Estimated, not measured with such a file.
  - **INFO3's every-stage loop** (`INFO3@0xb88` to `0xc12`) reads the stages' objects for the 獲得地点 view, which no
    data file edits; it raises no event for those stages. If a stage's objects become a table a file edits, a point
    goes at that loop's exit, as at SELSTAGE's. The guide's bullet says INFO's view reads no enemy, so it no longer
    reads as a limit of an enemy edit.
- **Scopes have no old-name form** (accepted): `data-files.md`'s old-names bullet says a scope's name and a table's
  short name in it do not change inside a major version.
- **Stage 52, driven.** `kamidori-stage-enemies-lake.steps` (sandbox `dp-kamidori-stage-lake`, `--lua-trace`) loads
  the main checkout's k1clean saves on 蒼月の南湖畔 with the mod: slot 020 (turn 1) from the title and slot 021
  (turn 5) through the floor's LOAD, after a console mark in slot 3. After each load slots 1 to 15 are STINIT's own
  for stage 52 but slot 3, which holds 762 (`401,631,762,441,441,631,441,601,601,601,601,631,631,631,631`), and the
  trace has the deliveries at `FIELD.BIN@0x527`, each writing slot 3 only: stage 51's part touched nothing.
  - **The second edits' own values,** read in STINIT: Himegari's stage 2 slot 3 is 202 (黒プテテット) with weight 0,
    placed at the stage's start; Kamidori's stage 52 slot 3 is 631 (バギル) with weight 0, placed at the floor's
    start. Each example's comment gives the value and the name.
  - **Headless:** `DataPatchWorkspaceTests.TheStageExamplesEditTheirSlotAndTheirMistakeTwinsAreReportedAtBoot` also
    runs each example at its second stage: the slot holds the edit in place of the stage's own unit, and FIELD placed
    it. In a booted session's story state Kamidori's stage 52 has scenes (an arrival scene FIELD leaves for first, and
    one after the placement), so that run starts on the floor (`G[0x428c]` set) and ends where FIELD leaves for the
    second scene, after the placement.
- **The scan through globals** (read; a scratch script, not kept). The first scan's taint stays in one script, so a
  value left in a global for another script was not followed. The second run lists each unsaved global that takes a
  field's value in a script (42 on Himegari, 51 on Kamidori, among them `G[0x62ccb]` and `shared_scratch_value`,
  `G[0xfec0c]`), the scripts that read each one, and the values passed around a call through one: a field left in a
  global before a call to a script that reads it, and a global a called script leaves that the caller then copies
  into a saved cell (33 such sites on Himegari, 58 on Kamidori, each read by hand). FIELD's locals need no such pass:
  a script's local banks are its own, and its pool temporaries never outlive their statement
  (`../../sys4-format-notes.md`, "Local banks and temporaries"), so the first scan already followed every copy out of
  them.
  - **What reaches a saved cell that way** is a result of play or a copy the guides list: the experience a battle
    awards from `unit.experience_reward` (Himegari `BTL@0x366e` through `G[0x62ccb]`, Kamidori `COMMITBTL@0x3f9`
    through `shared_scratch_value`, both into ADDEXP), Himegari's dropped item (`unit.drop_item`, `BTL@0x3939`, into
    ADDITEM), Kamidori's mastery reward (`item.growth_mastery_reward_items`, `CHECKIMPROVE@0x139`, into ADDITEM) and
    its settlement's sales (`item.price.retail_base`, `CALCSETTLE@0x4de`, into `G[0x3698]` at `SHOWSETTLE@0x2b`),
    the prices and stock payouts the guide already gave, the battle units' copies (SETEN, SETCH, ADDILLSUB), and one
    copy past the first join: Himegari's FORT (SYSTEM4's mode 2) gives the unit it shows each skill of its
    `unit.starting_skill` row that the game has not acquired (`FORT@0x3e7` to `0x42a`, into ADDSKILL). The rest were
    ids used as indices, a scene's voice slot used to pick a voice, or the flow-insensitive taint of one script
    reaching an unrelated value (FIELD's experience at `0x67b6` is worked out from the battle pools; SALLY's item at
    `0x18a3` from a local).
  - **The guides** list the new results of play and FORT's copy, and their sentence on the scan's limit now says what
    is left: a value left in a global for a script that runs later without being called there.
- **Number text in shipped Lua** (ran, `Ome.Scripting.Lua.Spike conformance-eval`). On Lua-CSharp 0.5.8,
  `string.format("%d", n)` and `tostring(n)` give the same text for a whole number (51, 51.0, 2^40, 2^53), and `%d`
  raises "number has no integer representation" for a fraction (2.5), as Lua 5.3 does and 5.2 does not.
  `ome-records`'s `string.format("%d", id)` (`init.lua:478`) gets an id the line at `init.lua:454` has refused unless
  it is whole, so it stays. The memory's note that shipped Lua should not use `%d` is the one that is too broad: `%d`
  needs a whole number, as `docs/modding/lua.md` says.
- **The validating parse of TOML** (ran, a throwaway test in the Release test build, median of five after a warm-up,
  on the largest converted catalog, `build/games/kamidori/import-en/text/en/_global.toml`, 5.4 million characters).
  `SyntaxParser.Parse(…, validate: true)` takes 306 to 350 ms, the plain syntax parse the catalog reader used for its
  lines 246 ms, Tomlyn's model about 47 ms, and the whole catalog read 751 ms. The validation added more than 100 ms,
  so `TomlDocument.Parse(text, out syntax)` gives the validated syntax tree, and the catalog reader takes its lines
  from it: the read takes 479 ms, against about 420 ms before M1 added the validation.
- **The words of the modder pages.** "The sweep" left `kamidori.md` and `oms/examples/mods/README.md`: they say
  where in the story the Kamidori scenarios' saves are (ウィル's workshop when シセティカ湖 has just opened; 蒼月の南湖畔
  at turns 1 and 5) and that the repository does not ship them. Three over-long lines are rewrapped.

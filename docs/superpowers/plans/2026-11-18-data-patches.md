# Data patches — implementation plan

**Date:** 2026-11-18
**Spec:** `docs/superpowers/specs/2026-11-17-data-patches-design.md` (cited as "spec §n"); the Lua scripting design
(`docs/superpowers/specs/2026-11-10-lua-scripting-design.md`) is cited as "Lua §n". The spec stays the design's home.
This plan keeps what each milestone checks and builds, and what that settles for the later ones.
**Status:** written before milestone 0 was built. M0 is planned in detail (§3), M1 far enough that M0 builds nothing
M1 must undo (§4), M2 and M3 in outline (§5, §6). §8 keeps what M0 found while it was built.
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

Not built by this stretch. What M0 leaves for it, and what M0 must not get in the way of:

- **Manifest and layout.** `omp = 4`; `[[data]]` (`kind`, `file`, `priority`) and `[[accepts]]` (`kind`,
  `description`, `shape`, `[[accepts.at]] event, handler`) in `ManifestReader`; `data/` in `ModLayout`; a package
  with `data/` changes content (`ModLayout.ChangesNoContent`). The base mods' manifests go to `omp = 4` and gain
  `[[accepts]] kind = "<game>.tables"` with one `at`: `<game>.data.layered` → `tables.lua:apply_boot`.
- **The document.** A reader over Tomlyn's `TomlTable` in `Ome.Mods`, by hand (NativeAOT, spec §2), into a plain
  value type in `Ome.Engine/Modding` with no TOML type in it; the bounds of spec §5.3, set from a file that fills
  Kamidori's `item` table; the documents in the link cache through `ModsJsonContext`.
- **The link steps** of spec §5.4 in `ModLuaLinker`, with the note for a kind's event that is not raised and the
  contributors' failure when none is. A planned handler carries the contribution; the report lists the kinds, the
  documents in delivery order, and the shared paths.
- **The delivery.** `Ome.Scripting.Lua` builds each document's read-only view once per session, from the plain values,
  and passes `{ mod, file, data, unraised }` as the handler's second argument. The library already walks a userdata
  with `pairs` (§2.2), and M0's tests run it on one.
- **Attribution and failures.** A delivery runs as the owner and for the contributor in the trace, the watch records
  and the log. Failures are counted by file. The applier raises once per session per bad file and is silent after,
  so a bad file costs one failure per session, never three in a row; the per-file switch-off covers an applier that
  fails each time.
- **`unraised`.** The base mod maps its labels to its events. In M1 `apply_boot` passes nothing for it, since the
  boot tables have one event. M2's `apply_stage` turns an unraised `<game>.stage.loaded` into a refusal of the whole
  document when it has a stage part (spec §6.2).
- **The budget's allowance.** Measured here (gate 10). §8 keeps M0's numbers for the applier's cost per value as a
  starting point.
- **Names as API.** Table and field names join `layout/exports.txt`, and the rename rule covers them; the registry's
  tables gain aliases (spec §6.3). See the question in §7.
- **The generic guide page** for data files under `docs/modding/`, and the gates 1 to 7, 9, 10 and 13.

## 5. Milestone 2: stages, and both games driven (outline)

- The scope facts in the registry: the scope's key (`stage`), its ids' range, and the short name of each table under
  it (`enemy` → `stage_enemy`), beside `built_by` (spec §6.2). The record map carries them.
- Himegari's `stage_enemy` table in `vm-map/globals.toml` (30 slots, `built_by = "stage"`).
- `records.applier` gains `scopes`: a scoped table is reached only through its scope (`stage.12.enemy.3`), its scope
  id is checked against the range, and `apply` takes the scope's id (`{ built_by = "stage", scope = { stage = 12 } }`).
  `labels` gains `stage`.
- `apply_stage` in both `tables.lua`, bound at `<game>.stage.loaded`; the unraised rule above.
- An example mod for each game under `oms/examples/mods/`, each game's guide page with its kinds, tables and fields
  and the derived values of §8. Gates 8 and 11, driven on the port.

## 6. Milestone 3: shapes (outline)

- The JSON Schema subset of spec §7, its checker in `Ome.Mods`, the line from a second parse.
- `base_mod_build.py` writes `shapes/tables.schema.json` from the record map, with state fields marked as not
  offered; `omp check` and `omp build` refuse a file that fails. Gate 12.

## 7. Questions and risks

- **One spelling against aliases.** Spec §6.1 gives each cell one spelling, so that two files that set a cell share a
  path in the report. Spec §6.3 has a renamed table or field keep its old name as an alias until the major version
  changes. With an alias, a cell has two spellings again until then, and the report would not show their overlap.
  The user's decision is needed before M1 adds names to `exports.txt`. Two ways out: the applier takes an alias and
  the engine's report gets no help (an overlap under an old name goes unreported for one major version), or the
  record map carries the aliases and the link maps an old name to the new one before it compares paths (the engine
  would then read a base mod's file, which spec §3 decision 1 rules out). M0 builds no aliases, so either way stays
  open.
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
  - The question of §7, one spelling against aliases, is still open.

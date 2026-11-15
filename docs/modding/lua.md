# Lua mods

A Lua mod runs code of its own beside the game's scripts: a handler that the engine calls at an instruction you
name, or at an engine event such as a session's start or a save. Lua mods run in the port only. Native AGE has no Lua, so `omp export` refuses them.

This version supports:

- **point hooks:** a handler runs before a named instruction of an original script, with the frame's locals and
  the instruction's operands;
- **the engine events** `ome.session.start`, `ome.session.resumed` and `ome.save.writing`;
- **game names:** the game's base mod names its cells, and a mod reads any of them and writes the ones it lists,
  without a capability;
- **`maid.vm`:** raw access to the game's cells, the frame's locals and the call stack;
- **`maid.state`:** a table that is saved beside each numbered save and comes back when that save is loaded;
- **`maid.log`,** and **the console:** the driver's `lua` step runs a chunk in a running game.

Not yet: game events, hooks on raw sites, on script entry and exit or on cell writes, `skip`, calling a script from Lua, and reloading Lua without a restart. A
mod that uses one of them fails to link, and the report says that this engine does not build it yet. The design
behind this guide is `../superpowers/specs/2026-11-10-lua-scripting-design.md`.

Game-specific values (point names, which local holds what) are in your game guide, `games/<profile>.md`.

## A first Lua mod

```text
my.double-exp/
├── manifest.toml
└── lua/
    └── double_exp.lua
```

```toml
omp = 3
id = "my.double-exp"
name = "Double EXP"
version = "1.0.0"

[game]
profile = "<profile>"
target = "extended"

[lua]
api = 1
capabilities = ["vm"]

[[hooks]]
point = "<a patch point of your game>"
handler = "double_exp.lua:on_award"
```

```lua
-- lua/double_exp.lua
function on_award(ctx)
  maid.vm.l[0x1] = maid.vm.l[0x1] * 2
end
```

Check it with `omp check --mod my.double-exp`, then run it with `--mod my.double-exp` ([cli.md](cli.md)). The check
compiles every Lua file and checks every hook without running any of your code. The startup log prints a `[lua]`
line with the mods that ship Lua and the number of handlers.

## The manifest

Lua needs **format version 3** (`omp = 3`), which adds the `[lua]` table, `[[hooks]]` and the `lua/` folder. A
version 3 loader still reads versions 1 and 2. All other keys are as in [packages.md](packages.md).

- **`[game] target = "extended"`** is required for a mod with Lua. The port runs it, and `omp export` refuses it.
- **`[lua] api`** is the `maid` API version the mod was written for. This engine provides `1`.
- **`[lua] capabilities`** lists what the mod may reach. Version 1 has `"vm"`, the raw access of `maid.vm`. A mod
  without it has no `maid.vm`, and its point hooks are link errors.
- **`[lua] writes`** lists the global names the mod writes through a base mod ("Game names").
- **`[lua] calls`** is read but not built yet: the link refuses a mod that lists any.

Each `[[hooks]]` table has one form and a `handler`:

| Form | Keys | Needs `vm` |
|---|---|---|
| Point | `point = "NAME"`, a named patch point of the game | yes |
| Engine event | `engine = "EVENT"`, one of the events in "Engine events" | no |

`handler = "FILE.lua:FUNCTION"` names a file under `lua/` (with folders: `util/hooks.lua:f`) and a function in
it. `priority` (default 0) orders the handlers due at one point: lower runs first, then load order. The link report
counts an interaction where more than one handler is due at one instruction.

### Libraries

A package without `[game]` is a library. It holds `lua/` files and `[lua] api`, nothing else: no hooks,
capabilities, writes or calls. Another mod lists it in `[[requires]]` and loads it with `require("<library id>")`,
which returns what the library's `lua/init.lua` returned. The requiring mod sees that table through a read-only
view, so no mod can change what another mod exports. A library has no `maid.vm` of its own: a mod that has one can
pass it to a library's function.

## Handlers and the session

A handler is a global function declared at the top level of its file: `function on_award(ctx)`. The check finds
it without running the file, so these are not handlers: a `local function`, `function M.f()` or `function M:f()`
on a table, `on_award = function() end`, or a function declared inside a block.

The engine builds a **session** at the start of a run, after a return to title and after a load. A session gives
each mod its own environment, fills its `maid.state` (from the save after a load, empty otherwise), runs the mod's
`lua/init.lua` (when it has one), then each file that holds a handler, and then raises `ome.session.start`. So a
mod's globals start fresh in every session: a value kept in a Lua global is gone after a load or a return to
title. Keep what must last in `maid.state` ("State").

### Engine events

| Event | When | `ctx` fields |
|---|---|---|
| `ome.session.start` | a session has been built, before the game goes on | `reason`: `"boot"`, `"title"` or `"load"` |
| `ome.session.resumed` | a load has restored the saved game, before its first instruction ("Loads") | `slot`: the slot loaded |
| `ome.save.writing` | before each numbered save, ahead of the save's capture: what a handler writes, to `maid.state` or to the game's cells, is in the save | `slot`: the slot written |

Every event's `ctx.event` is its name. `ome.save.writing` takes its place among the point hooks on the save's
instruction by `priority`, then load order. An event's handler runs outside any frame, so the locals and `ctx.args`
are not there.

## Point hooks

A point hook runs before the instruction its point names, after the engine's own work at that instruction
boundary, every time the game reaches it. The handler gets `ctx`:

| Field | Value |
|---|---|
| `ctx.script` | the script, `NAME.BIN` |
| `ctx.offset` | the instruction's offset in the script as loaded |
| `ctx.site` | its offset in the original script |
| `ctx.point` | the point's name |
| `ctx.opcode` | the instruction's opcode |
| `ctx.args[i]` | operand `i` (from 1): `.value` to read or write it, `.kind` (`"integer"`, `"float"` or `"string"`), `.writable` |

A write to `ctx.args[i].value` writes the cell the operand names. An immediate or a literal is not writable. The
context is live only during its call: a `ctx` kept in a global and used later is an error.

A point names an instruction of the original script, and the link finds it in the script as your stack loaded it:

- when another mod's patch inserts code before it, the hook follows the instruction;
- when a patch replaces the instruction, or a mod replaces the whole script, the link stops and names both mods;
- when the installed script is not the revision the point was written for, the link stops: the game's catalog
  differs (an update or a translation that replaces the script).

## Game names

The engine ships one base mod per game, `<profile>-base`. It exports every global the project has named, with its
shape. A mod that requires it reads any of those names, and writes the ones its manifest lists, with no capability:

```toml
[lua]
api = 1
writes = ["<a scalar global>", "<a table global>"]

[[requires]]
id = "<profile>-base"
version = ">=1, <2"
```

```lua
local game = require("<profile>-base")

local unit = game.g.<scalar global>             -- read a named global
game.g.<scalar global> = 30                     -- write one the list names
local levels = game.table("<table global>")
levels[unit] = levels[unit] + 1                 -- an element of a table
local row = game.table("<table of rows>")[unit] -- a row of a 2-D table
row.<column>, row[0]                            -- its cells, by column name or number
```

- **Names.** The names, their shapes and their columns are in your game's global reference
  ([games/README.md](games/README.md)). A name that a later version renames stays exported until the base mod's
  major version changes, so `version = ">=1, <2"` keeps a mod working.
- **Indices are the game's,** from 0, as its scripts index: `levels[0]` is the first element. An index outside the
  table is an error. A table whose row count the registry does not know ends where the next named global starts.
  `#t` is a table's rows and `#t[row]` a row's columns.
- **Columns.** A named column is a field: `t[row].charm` is `t[row][6]` when the registry names column 6 `charm`. A
  1-D table's column names name its elements.
- **Values** keep their cell's rules, as `maid.vm` does: integers whole and in 32 bits, floats rounded to single
  precision, strings in CP932. `g` takes a scalar's name and `table` a table's.
- **Writes.** `[lua] writes` lists the names the mod writes; a table's name covers all its elements.
  - A write to a name the list lacks is an error that names the list (`pcall` catches it).
  - A listed name the base mod does not export is a link error, and so is a list in a mod that requires no base mod.
  - A mod with `vm` lists what it writes by name too. It can still write any cell by address.
- **What `require` gives** is read-only: `g` and `table`, and beside them what the base mod's own `lua/init.lua`
  returns. The base mods have no helpers yet.
- **Mounting.** The engine mounts a base mod only when an enabled mod requires it, and uses an installed package of
  the same id instead when there is one ([packages.md](packages.md), "Base mods").

## State

`maid.state` is the mod's own table that lasts across saves. The engine writes it beside each numbered save, in
`SAVEnn.modstate.json` next to `SAVEnn.DAT`, and gives it back to the session a load of that save builds. Any mod
has it, with or without `vm`.

```lua
-- lua/hooks.lua
function on_start(ctx)
  maid.state.visits = (maid.state.visits or 0) + 1
end
```

It starts as an empty table in a new session, and you may replace it with another table (`maid.state = { … }`).
It holds:

- booleans, numbers and strings, and tables of them, nested at most 64 deep;
- keys that are strings or integers. The keys `1` and `"1"` are two keys and come back as two keys;
- not functions, coroutines or engine objects, a number that is not finite (`1/0`, `0/0`), a string cut inside a
  character (half of a character outside the Basic Multilingual Plane), or a table that contains itself. A mod's
  state may be at most 1,048,576 characters as saved.

A table's metatable is not saved, and a table that two places of the state hold is saved twice: after a load they
are two tables.

The state is written at each numbered save, after the `ome.save.writing` handlers have run. A state the save
cannot hold never stops the game's save. The save is written, your mod's last good state is written beside it
(the one it loaded, or the one it saved last), and `diagnostics/lua.log` names the value and where it is in the
table (`maid.state.items[3].f is a function`). `--lua-errors halt` ends the run at that save.

- **`maid.mod.state_version`** is the version of your mod that wrote the loaded state, or nil when the state
  started empty. Use it to convert a state an older version wrote.
- **A mod that is removed keeps its state.** The engine carries its block into every later save unchanged, so the
  mod finds it again when it is enabled again. So does a mod that is off for the session after an error.
- **The state starts empty** in a new game, at the title, and after a load of a save that has no state file (a
  save made without Lua mods, or by native AGE). A state file that does not belong to its save, or that cannot be
  read, is reported and gives empty state ([troubleshooting.md](troubleshooting.md), "Lua").
- **The game's save is unchanged:** `SAVEnn.DAT` is the same with and without a mod that only uses `maid.state`,
  and native AGE loads it. Native AGE does not read the state file.

## Loads

A load does not drop the game at the saved instruction. It does what native AGE does:

1. the session is rebuilt with each mod's saved state, and `ome.session.start` fires with the reason `"load"`;
2. the game's load callback runs, then the saved scripts are entered from their starts, so their prologue runs
   again: the configuration scripts and the tables the game fills at its boot;
3. the game jumps to the start of the page that was on screen at the save, and `ome.session.resumed` fires;
4. the game plays on from there. The instructions from the page's start to where the save was made run a second
   time.

Hooks fire through all of it as they do in play. `maid.session.restoring` is true from the session's build until
step 3, and false when `ome.session.resumed` fires. A handler that keeps a running total in `maid.state` must skip
step 2, or every load adds the prologue's work to the saved total:

```lua
function on_award(ctx)
  if maid.session.restoring then return end
  maid.state.total = (maid.state.total or 0) + maid.vm.l[0x1]
end
```

Step 4 needs care too. A hook on a page the player can save in fires once before the save and again after each
load of it, with the flag clear: a total it keeps counts that page twice. Nothing marks where the repeated part
ends, because a save records the page, not the instruction. The game's own instructions there run twice as well,
and so does a patch placed there. Prefer state that is set rather than added to (`maid.state.met_boss = true`
rather than a count).

A load the game abandons (its load callback returns to the title) ends with the flag set; the title's session
starts with it clear and its state empty.

## `maid`

### `maid.vm` (capability `vm`)

```lua
local vm = maid.vm
vm.g[addr]               -- integer global: read or write
vm.gf[addr], vm.gs[addr] -- float and string globals
vm.gp[addr], vm.gsp[addr] -- pointer cells: read only
vm.l[i], vm.lf[i], vm.ls[i] -- the frame's integer, float and string locals
vm.g:range(addr, count)  -- count cells as a table (at most 65,536)
vm.frame                 -- { script, offset, depth, site, changed_by } of the frame the hook runs in
vm.frames()              -- every frame, outermost first, as vm.frame describes one
vm.script_id("NAME")     -- a script's packed id, or nil
```

- **Integers** must be whole and fit in 32 bits; anything else is an error, not a truncation. A read gives the
  signed 32-bit value.
- **Floats** are 32-bit: a write rounds to single precision.
- **Strings** must be encodable in CP932, the games' text encoding.
- **Locals** exist while a point hook runs, and in the console at a wait. In an engine event they are an error.
- **`site`** is absent for an instruction a patch inserted. **`changed_by`** lists the mods that patched or
  replaced the frame's script.
- `vm.call` is not available in this version.

`maid.vm` is raw access: it can corrupt game state and the saves made after it. The link report lists the mods that
have it.

### `maid.log`, `maid.mod`, `maid.session`

- `maid.log.info(…)`, `maid.log.warn(…)`, `maid.log.error(…)` write a line to `diagnostics/lua.log`, with the
  mod's id. `print` writes at `info`.
- `maid.mod.id` and `maid.mod.version` are the mod's own; `maid.mod.state_version` is the version that wrote the
  loaded state ("State").
- `maid.session.reason` is why the session was built: `"boot"`, `"title"` or `"load"`.
  `maid.session.restoring` is true while a load restores the saved game ("Loads").
- `maid.state` is the mod's saved table ("State").
- `maid.names` exists only in a base mod and in the console: `g` and `table` over that base mod's names, with no
  write list ("Game names").

## The sandbox

The engine runs Lua 5.2 (the Lua-CSharp interpreter). A mod's environment has `assert`, `error`, `pcall`,
`xpcall`, `select`, `type`, `tostring`, `tonumber`, `pairs`, `ipairs`, `next`, the `raw*` functions,
`setmetatable`, `getmetatable`, `unpack`, `require`, `print`, and its own copies of `string`, `table`, `math`,
`bit32` and `coroutine`. There is no `io`, `os`, `debug`, `package`, `load`, `loadstring`, `dofile`, `loadfile`
or `string.dump`: nothing reaches files, processes, the clock or the network.

- **`require("name")`** loads the mod's own `lua/name.lua` (`require("util.names")` is `lua/util/names.lua`), or a
  required mod's export, once per session.
- **Each mod has its own library tables.** Changing `string.format` changes it for your mod only, and the string
  metatable is locked.
- **`math.random`** draws from the engine's generator, so the driver's `rand-seed` makes it repeatable.
  `math.randomseed` is absent.
- **Strings count UTF-16 code units,** not bytes: `#"日本語"` is 3, and `("日本"):sub(1, 1)` is `"日"`.
- **Whole numbers print as Lua prints them; fractions may not.** A number that is not a whole number, such as
  `3 * 1.1`, prints with every digit it holds (`3.3000000000000003`; Lua 5.2 prints `3.3`), and `1/0` prints
  `Infinity`. Format a number you show: `string.format("%.1f", x)` gives the same text on every build, and
  `%d` needs a whole number (`math.floor(x)`).
- **Weak tables are never cleared, and `__gc` never runs.** The .NET collector owns memory, so a table with
  `__mode` keeps its entries.

## Errors and budgets

| Bound | Value |
|---|---|
| A handler call | 1,000,000 Lua instructions |
| A mod's start-up code (`init.lua` and its handler files, together) | 10,000,000 instructions |
| Handlers nested inside handlers | 32 |
| Lua files in a package | 512, each at most 1 MiB |

Unbounded recursion ends as a "stack overflow" error. When a handler fails or runs past its budget:

- `diagnostics/lua.log` gets the mod, the handler, the script and offset it ran at, the error and its traceback;
- the port's status line shows a notice;
- the game goes on, and the third failure in a row switches that handler off for the session.

`--lua-errors halt` ends the run at the failing instruction instead ([cli.md](cli.md)), which is useful while
writing a mod. A mod whose start-up code fails is off for the session, and so is every mod that requires it.

Two things no budget bounds: a pattern that backtracks (`string.find`, `match`, `gmatch`, `gsub` with patterns like
`a*a*a*…b`) can stall the game, and a mod can use up memory. If the game hangs with mods on, disable mods and enable
them again one at a time to find the one.

## The console

The driver's `lua` step runs a chunk in a running game and returns what it returns ([cli.md](cli.md), "Automated
testing"):

```text
lua return maid.vm.g[0x100]
lua return #maid.vm.frames(), maid.vm.frame.script
lua mod=my.double-exp return some_global
```

It runs at the next instruction boundary, or inside the wait the game sits in, so it also runs at a text wait.
Without `mod=` it runs in the console's own environment, which has `maid.vm`, and `maid.names` over the game's base
mod with no write list (`require("<profile>-base")` gives the same, and works even when no mod mounts the base mod).
The environment lasts for the session, so a later chunk sees what an earlier one defined. With `mod=ID` it runs in that mod's environment, with its globals: that is
how to inspect a mod. A chunk cannot call a script. The console works with no Lua mod enabled too: its first use
starts the Lua runtime.

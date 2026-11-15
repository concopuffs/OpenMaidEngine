# Lua mods

A Lua mod runs code of its own beside the game's scripts: a handler that the engine calls at an instruction you
name, or when a session starts. Lua mods run in the port only. Native AGE has no Lua, so `omp export` refuses them.

This version supports:

- **point hooks:** a handler runs before a named instruction of an original script, with the frame's locals and
  the instruction's operands;
- **the engine event `ome.session.start`;**
- **`maid.vm`:** raw access to the game's cells, the frame's locals and the call stack;
- **`maid.log`,** and **the console:** the driver's `lua` step runs a chunk in a running game.

Not yet: game events and base mods, writing globals by name, state that survives a save, hooks on raw sites, on
script entry and exit or on cell writes, `skip`, calling a script from Lua, and reloading Lua without a restart. A
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
- **`[lua] writes`** and **`calls`** are read but not built yet: the link refuses a mod that lists any.

Each `[[hooks]]` table has one form and a `handler`:

| Form | Keys | Needs `vm` |
|---|---|---|
| Point | `point = "NAME"`, a named patch point of the game | yes |
| Engine event | `engine = "ome.session.start"` | no |

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
each mod its own environment, runs the mod's `lua/init.lua` (when it has one), then each file that holds a
handler, and then raises `ome.session.start`. So a mod's globals start fresh in every session: a value kept in a
Lua global is gone after a load or a return to title. State that survives a save comes in a later version.

`ome.session.start`'s `ctx.reason` is `"boot"`, `"title"` or `"load"`, and `ctx.event` is the event's name.

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
- `maid.mod.id` and `maid.mod.version` are the mod's own.
- `maid.session.reason` is why the session was built: `"boot"`, `"title"` or `"load"`.

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
Without `mod=` it runs in the console's own environment, which has `maid.vm` and lasts for the session, so a later
chunk sees what an earlier one defined. With `mod=ID` it runs in that mod's environment, with its globals: that is
how to inspect a mod. A chunk cannot call a script. The console works with no Lua mod enabled too: its first use
starts the Lua runtime.

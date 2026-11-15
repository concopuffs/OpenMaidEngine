# Lua scripting design

**Date:** 2026-11-10; accepted 2026-11-14
**Status:** accepted. Not built: §18 has the milestones, and the first is a spike that chooses the library
(§5.1).
**Checked against:** `ac843b3b` on `develop`, after the engine layering work was merged. Code references name files
and members, not lines. Paths are relative to `age-reimpl/`; a short path is under `engine/` (`Natives/` and
`Persistence/` are `Ome.Engine`'s, and `VirtualMachine*.cs` is `Ome.Engine.Vm`'s).
**Direction:** `docs/remake-architecture-and-roadmap.md` §3.2–§3.10, which this spec implements in part. The
backlog's "Runtime scripting (Lua)" item tracks the milestones.

**What already exists.** The roadmap fixes most of the direction: Lua runs beside the AGE VM, selectors stay
declarative, callbacks run on the VM thread and cannot yield, each mod owns a serializable namespace, and
game-specific surfaces ship as a per-game base mod (§3.8). The mod loader (version 1) deliberately left Lua out.
The engine layering design (`docs/superpowers/specs/2026-11-12-engine-layering-design.md`, built and merged
2026-11-14) lists Lua mods among the callers of natives and leaves the binding to this spec (its §8). This spec
turns that direction into a design against the code as it is today.

**It replaces two sentences of earlier specs:**

- the localization design expected Lua's manifest tables to share version 2 with its own (its §9.2). They take
  version 3 (§6.2);
- the mod loader design said the base mod would own the patch points (its §5). The registry stays their source,
  and the base mod carries a generated copy (§9.1).

**Not verified** (each is marked where it matters):

- whether an engine unwind passes through the Lua library's frames, `pcall` included, and whether a budget stop
  does (§5.1);
- which opcodes reach a wait. The waits themselves are listed; the registry does not yet record the fact per
  opcode (§10);
- the Lua libraries' features. What this spec says about them comes from package listings, not from use, and
  the spike in §5.1 settles it. That includes whether Lua-CSharp can be called synchronously at no extra cost,
  whether a library can keep one mod from changing what another sees (§5.2), and whether deep recursion ends as
  an error.

## 1. Purpose and scope

### 1.1 Does Lua make sense here

Yes, for a specific set of jobs. Open Maid Script already covers changes to the game's own logic, and it compiles
to bytecode that native AGE also runs. Lua should not compete with it.

| Lua adds | Why bytecode cannot do it well |
|---|---|
| General logic: tables, string handling, loops over data | AGE bytecode has no value-returning calls and no data structures beyond cells |
| Mod-owned state | native globals need free cells inside the saved range, which is an open problem (OMS §9.2) |
| Observers with no site: "whenever this global changes", "whenever this script runs" | a patch needs one site per place |
| Engine-side filters, such as the localization text filter | these run in the engine, not in a script |
| A console and hot reload for exploring a game | a patch needs a relink |

| Lua costs | |
|---|---|
| Port only | a mod with Lua cannot be exported to native AGE |
| Partly opaque to conflict analysis | the loader orders handlers and lists the names a mod writes (§9.3); it cannot see what a handler does |
| A second interpreter inside the instruction loop | bounded by §7.5 |

**What stays bytecode:** anything that waits, shows a scene, or must survive a save in the middle. §3 decision 4
explains why.

### 1.2 Version 1 delivers

Version 1 arrives in two steps (decided 2026-11-14). §18 has the milestones.

**The first release** (milestones 0 to 3):

- a Lua runtime behind an engine interface, created only when an enabled mod needs it;
- `lua/` and `events/` in the package, with `[lua]` and `[[hooks]]` in the manifest;
- a game-agnostic engine API (`maid`), with raw VM access behind a capability;
- hooks on named patch points and on engine events;
- typed events that a base mod declares as data;
- per-save mod state in a sidecar file;
- first-party base mods for Himegari and Kamidori, mostly generated from the global registries, through which a
  mod reads and writes the game's globals by name;
- a console step for the driver, type stubs for editors, and an attribution trace;
- the guide for mod authors.

**After it** (milestones 4 to 6), once real mods show which of these they need:

- hooks on raw sites, before or after an instruction, and `skip` at a point or a site;
- hooks on script entry and exit, and on global writes;
- synchronous calls into game scripts, and the base mods' helpers that use them;
- reload while the game runs, and a mod's own random stream under the driver's seed.

The design covers both steps, and the manifest format is whole from the first (§6.3).

### 1.3 Not in version 1

Lua code that waits (scenes, dialogue, choices), UI, audio and asset services, extension opcodes that call Lua
from bytecode, file or network access, and hooks on code another mod inserted. §16 lists them.

## 2. What the code gives to build on

| Fact | Where | Consequence |
|---|---|---|
| The engine is three layers, each its own project: opcode handlers (`Ome.Engine.Vm`), natives (`Ome.Engine`) and devices (`Ome.Engine.Devices`). Calls go one way, and tests check it | engine layering spec §3; `ProjectBoundaryTests` | Lua needs a stated place in the layers (§14) |
| One thread, the engine thread, runs the interpreter, every native and every wait. The front end reaches engine state only through requests that thread runs | `Natives/InputAndWaits.Turns.cs` (`RunEngine`, `Invoke`) | Lua handlers run on it and take no lock; the console and the reload are requests (§13) |
| One loop executes every instruction, for a frame's body and for a wait's callback | `VirtualMachine.RunInstructions` | hook dispatch has one home |
| The loop gives each instruction its services in a fixed order: the stop and reload requests, the step limit, the boundary's services, the opcode handler, the front end's frame turn, fast-forward's pace | the same; `ServiceInstructionBoundary` | §7.1 says where in that order a hook runs |
| The frame turn also blocks while the front end has a modal loop open: the options panel, the 終了 dialog, a menu | `Natives/InputAndWaits.Sleep.cs` (`FrameYield`) | a called script can be held there (§10) |
| A statement block is scene state of the tick, not of a frame | `Natives/EngineTick.cs` | a failed call puts it back (§10) |
| The interpreter recurses on the C# stack: `call-script` calls `RunFrame` | `VirtualMachine.ScriptLifecycle.cs`, `OpCallScript` | a Lua handler can run a script synchronously the same way |
| A frame's end travels as a return value. Five exceptions unwind from below a loop: a root reload, a numbered restore, an exit request, a script error and a halt | `StepResult`, `FrameOutcome`; the exception classes in `VirtualMachine.cs` | a script called from Lua can end the run or the scene, and the Lua handler is then abandoned (§10) |
| Every wait is a member of one interface and blocks in one file | `Natives/IInputAndWaits.cs`, `InputAndWaits.Blocking.cs`; engine layering spec §5.3 | the waits are a known list, and one place can refuse them (§10) |
| Globals are the script memory native's five banks. Every opcode handler writes them through its five setters | `Natives/ScriptMemory.cs` (`SetInteger` and its siblings) | watched writes are recorded there. Tools that seed the banks write the dictionaries directly, which records nothing |
| Locals, the frame stack and operands are the interpreter's | `ExecFrame` (internal), `Frame` | `maid.vm` binds to two owners (§8.1) |
| Scene state is rebuilt on a root reload, which a return to title is: each owner replaces its `Scene` object. A full load does not run that reset | engine layering spec §5.1, rule 5; `SceneReloadTests`; `VirtualMachine.Run` | the Lua session is scene state, and the scripting native ends it itself at a load (§5.7) |
| Each native writes its own save records, and the save native orders them | the same, rule 6; `NumberedSaveRecords` | mod state is a native's record (§11) |
| A text sidecar is kept beside each save: written after it, copied and deleted with it | `Persistence/SidecarNativeDatStore.cs`, `ModSaveStamp` | mod state uses the same store. The store takes one fixed text today; mod state differs per save |
| A full load re-enters every saved script at its entry and runs it to its op `0xae`. The last one then resumes at its page's read marker, not where the save was made | `CreateRestoredFrame`, `OpContinueSaveLoadStackRestore` | hooks fire during a restore, and some fire again after it (§11) |
| Op `0xae` continues a pending restore for whichever frame runs it | `OpContinueSaveLoadStackRestore` | a script called during a restore must not reach one (§10) |
| Frame enter and exit are reported with a cause. The entry is reported before the frame's first instruction boundary | `TraceEvent.FrameEnter`, `FrameCause`; `RunFrame` | script hooks attach beside this, so a session is built before a frame is entered (§5.7) |
| Named patch points exist with fingerprints | `vm-map/patch-points-<profile>.toml` | hooks reuse them unchanged |
| The linker keeps, for each patched script, where every original instruction went | `Ome.Mods/Linking/ScriptOffsetMap.cs`; `MountedModLayer.SiteBinding` | site hooks bind through it (§7.6) |
| The manifest is at format version 2, and a version 2 package may target no profile | `Ome.Mods/Manifest/ManifestReader.cs` | §6.2, §6.4 |
| The C# toolchain already reads the global registry | `Ome.Script/Modules/GameModule.cs` | events can name globals instead of addresses |

## 3. Decisions

1. **The engine knows no game.** `maid` exposes raw VM access, hooks, state and logging. Names such as "gold" or
   "unit level" come from a base mod. The only per-game knowledge in the engine remains the engine builds' rules
   (engine layering spec §7).
2. **Registration is data; Lua is behaviour.** A manifest says which function handles which hook. A handler is a
   top-level function of its file, so a parse finds it (§6.2). The loader validates every hook before any mod
   code runs, and a reload only swaps function bodies.
3. **Lua runs at instruction boundaries, on the engine thread.** There is no second thread and no locking. The
   front end reaches the runtime only by request (§13). There are two exceptions to the boundary: a session is
   built between frames (§5.7), and a console chunk may also run when a wait wakes.
4. **A handler runs to completion.** It cannot wait. A save stores script frames; it cannot store a suspended Lua
   function, so anything the player could save in the middle of has to be bytecode.
5. **Mod state is plain data, saved beside the save.** The native save file is not touched.
6. **The Lua state is rebuilt on every load and return to title.** Only `maid.state` survives. A mod cannot keep
   stale values in a local variable by accident.
7. **Raw access is a declared capability, and named writes are declared too.** `vm` gives cells by address,
   locals, operands and script calls. A mod without it uses events, reads the game's globals by name through its
   base mod, and writes the names its manifest lists (§9.3).
8. **Lua has no clock, and its randomness is the engine's.** `math.random` draws where the game's own op `0x60`
   draws, so it is deterministic exactly when the game is: under the driver's seed (§5.6).
9. **The runtime is replaceable.** The engine talks to an interface. The library behind it is a managed one,
   chosen by a spike (§5.1).
10. **Lua sits beside the opcode handlers.** The interpreter invokes it, and it calls natives and the
    interpreter through interfaces that `Ome.Engine` defines. One project names the Lua library (§14).
11. **Mods share nothing that one of them can change.** Each mod has its own copies of the library tables, and
    what a mod exports is read-only to the mods that require it (§5.2). A capability then goes only where its
    owner sends it.

## 4. Terms

- **Handler:** a Lua function a manifest binds to a hook. (The engine layering spec uses "handler" for an opcode's
  C# handler. Here that is always written "opcode handler".)
- **Hook:** one binding of a handler to a place or occurrence.
- **Point:** a named position in a script (mod loader spec §5). Control is about to execute the instruction there.
- **Site:** a position given directly as script, fingerprint and original offset.
- **Event:** a named occurrence with typed fields, declared as data.
- **Watch:** a hook on writes to a global.
- **Boundary:** the point between two instructions, where the loop runs its services.
- **Session:** the life of one Lua state, from its build to the next load or return to title.
- **Base mod:** the first-party package that gives one game its named surface.
- **Layout:** a base mod's generated list of the game's globals: each one's name, bank, address and shape.
- **Accessor:** a view of a global or a table by its name, for reading and writing. The engine builds it from
  a base mod's layout (§9.3).
- **Capability:** a permission a mod declares in its manifest. Version 1 has one, `vm`: raw access.
- **Write list:** the names a mod's manifest says it writes through accessors (`[lua] writes`).

## 5. Runtime

### 5.1 Choosing the library

The roadmap leaves "managed interpreter versus native runtime" to a spike. The candidates:

| Candidate | Kind | Language | State |
|---|---|---|---|
| Lua-CSharp | pure C# | Lua 5.2, part of the standard library | active; .NET Standard 2.1; no IL generation; source only, no bytecode |
| MoonSharp | pure C# | Lua 5.2 | last stable release 2.0.0 in October 2016; a 3.0.0 beta is listed on NuGet |
| NLua / KeraLua | binding to native Lua | Lua 5.4 | targets .NET 8; needs a native library per platform |

**Decided 2026-11-14: a managed interpreter.** The reasons are specific to this engine:

- **Unwinding.** A script called from a handler (§10) can end the run or the scene. The engine must then abandon
  the handler from inside a callback, through every Lua frame above it. In a managed interpreter that is a C#
  exception. A C# exception must never cross a native Lua frame, so a native runtime needs a catch-and-rethrow
  shim at every boundary.
- **Strings.** VM strings are .NET strings. A managed interpreter shares them; native Lua needs a UTF-8 copy on
  every crossing.
- **Packaging.** The project builds and pins each native component for two platforms. A managed library adds
  none.
- **Speed is not the constraint.** Handlers are short and cross the boundary often. Native Lua wins on long
  computations, which mods here do not run.

What native Lua would give instead: Lua 5.4 (integers, bitwise operators) and a hard memory cap through its
allocator.

**The spike decides between the managed candidates.** It passes a library that can do all of these:

1. call Lua from C# while another Lua call is on the same thread's stack;
2. let a C# exception thrown inside a callback pass out through every Lua frame, `pcall` and coroutines
   included. Every such unwind ends the session (§10), so the state must then dispose cleanly and need not work
   again;
3. stop a handler that exceeds an instruction budget, in a way `pcall` cannot catch, and keep working
   afterwards;
4. run with a restricted standard library and a custom module loader;
5. give each mod its own global environment inside one state, with its own copies of the library tables, a
   string metatable no mod can change, and a way to hand one mod's table to another that the receiver cannot
   write to, with `rawset` either (§5.2);
6. dispatch a trivial handler fast enough for §7.5's budget;
7. be called from C# synchronously, with no asynchronous machinery per call. Lua-CSharp's API appears to be
   asynchronous first; the spike measures what a synchronous call costs there;
8. build a session fast enough for a load. A session is built on every load and every return to title, and it
   reads each base mod's generated layout. Kamidori's registry is the large one;
9. ship under a licence compatible with the project's MIT licence and notices file;
10. end unbounded recursion as an error the engine can report: plain recursion, and recursion through `pcall`,
    through a metamethod, and through a callback such as `table.sort`'s comparison. A .NET stack overflow cannot
    be caught; it ends the process;
11. iterate a table in the same order in two processes, given the same inserts and removes. .NET randomizes
    string hashes for each process. A .NET `Dictionary` does not order its entries by hash, but a library with
    its own hash part may. A mod that draws `math.random` inside a `pairs` loop depends on this (§5.6).

The spike also reports what bounds a library function that loops inside one call, such as a pattern that
backtracks. An instruction budget does not count those (§5.5, §15).

If neither managed candidate passes, the choice between managed and native is open again.

The language level modders rely on is then **Lua 5.2**, and the manifest records the API version, not the
library.

### 5.2 One state, one environment per mod

- **One Lua state per session.** Mods share it, so a base mod can hand tables and functions to its dependents
  directly.
- **Each mod has its own global environment.** A global one mod defines is invisible to the others.
- **Each mod gets its own `maid` table.** A mod without the `vm` capability has no `maid.vm` field. A base mod's
  functions keep working for it, because they hold the base mod's own `maid.vm`.

A separate environment separates globals. It does not separate the tables those environments point at. Three
more rules keep one mod from changing what another sees:

- **Each mod has its own copies of the library tables** (`string`, `table`, `math`, `bit32`, `coroutine`). What
  a mod assigns into its copy stays its own.
- **The string metatable is locked.** `getmetatable("")` gives a mod nothing it can change, and the string
  methods come from a table no mod can reach.
- **What a mod exports is read-only to the mods that require it** (§5.4). A requiring mod can read it and call
  it. It cannot assign into it, or into a table inside it, and `rawset` cannot either. A guard in a metatable
  is not enough for that, because `rawset` goes past it.
  - **The view covers what the export holds:** its fields, and the tables inside them. What a function returns,
    or passes to a callback, is not wrapped. That is its author's choice to hand out, so a helper can build a
    table for its caller to fill in. A base mod that returns one of its own tables gives write access to it.

Without these rules a mod with no capability could replace a function that a base mod calls with its `maid.vm`,
and so receive it. The link report shows the player which mods have the capability (§15), so it must not be
something a mod can take.

### 5.3 The sandbox

| Available | Not available |
|---|---|
| `assert`, `error`, `pcall`, `xpcall`, `select`, `type`, `tostring`, `tonumber`, `pairs`, `ipairs`, `next`, `rawget`, `rawset`, `rawequal`, `setmetatable`, `getmetatable` | `io`, `os`, `debug`, `package` |
| `string` (without `dump`), `table`, `math` (without `randomseed`), `bit32`, `coroutine` | `load`, `loadstring`, `dofile`, `loadfile`, `collectgarbage` |
| `require` (§5.4), `print` (writes to `maid.log`) | any .NET interop or reflection |

Coroutines are allowed inside a handler. They do not change decision 4: the handler still has to return.

### 5.4 Modules

- `require("util.names")` loads `lua/util/names.lua` from the mod's own package, once per session.
- `require("himegari-base")` returns what that mod's `lua/init.lua` returned, if the manifest lists it under
  `[[requires]]`. A local file whose module name equals a dependency's id is a link error.
  - **The requiring mod gets a read-only view of it** (§5.2). Two mods that require one package see the same
    tables, each through its own view.
  - **For a base mod, the view also has the accessors** `g` and `table`, which the engine builds for the
    requiring mod (§9.3). A base mod's own table may not use those two names; the session build reports it.
- Nothing else resolves. There is no search path.

### 5.5 Budgets and errors

- **Budget.** Each handler call has an instruction budget, and so does each mod's start-up code. Running out
  ends the call with an error. The numbers are set from the spike's measurements. The budget counts Lua
  instructions. It does not count a library function that loops inside one call (§15).
- **Depth.** Hooks can nest when a handler calls a script (§10) or raises an event (§8.2). The nesting depth is
  capped. The cap is set from the engine thread's stack size and from the spike's measurement of what one level
  costs, so that the cap is reached before the stack's end is.
- **Load errors stop the start.** A syntax error, a missing handler function, an undeclared event, a bad
  fingerprint, or a name in `writes` that the base mod does not export is a link error, reported like any other
  (mod loader spec §7). `--skip-failed-mods` applies. The link runs no mod code: it parses each file through
  the runtime's check (§14.2).
- **Build errors switch the mod off for the session.** A session is built many times in a run (§5.7). A mod's
  `init.lua` reads its saved state, so a build can fail at a load after it succeeded at the start. When a mod's
  `init.lua`, or a file its hooks name, raises an error as it runs:
  - the mod is off for that session, with a notice, and its hooks do not fire;
  - its state block is carried unchanged into the next save, as a removed mod's is (§11);
  - a mod that requires it is off too;
  - a reload that fails this way keeps the session that was running (§13).
- **Runtime errors are reported, and the game continues** by default:
  - the handler's call ends; what it already changed stays;
  - `diagnostics/lua.log` gets the mod, handler, script and offset, and the Lua traceback;
  - the front end shows a short notice. It reads the notices each frame, as it reads the page notices
    (`InputAndWaits.PageNotices`); the engine does not call it;
  - a handler that fails three times in a row is switched off for the session, and the notice says so.
- **`--lua-errors halt`** ends the run instead: the dispatch ends the instruction with a halt and its reason, as
  an opcode handler does. A build error ends the run the same way. The driver uses it, so a scenario cannot pass
  over an error.

### 5.6 Randomness and time

- **`math.random` draws from script memory's generator,** the one op `0x60` draws from
  (`Natives/ScriptMemory.Random.cs`). The engine then has one source of randomness.
- **Without a seed** that is the system's generator. A mod's rolls differ between runs, and between two loads of
  one save, as the game's own do. A generator that the session owned would restart at every rebuild, and every
  load would repeat the same rolls.
- **Under the driver's `rand-seed`** each mod draws from its own stream, derived from the seed and the mod's id,
  as each op `0x60` site has its own. The stream is script memory's, so a load does not restart it. The next
  `rand-seed` does, with the sites' streams. The streams come after the first release (§1.2). Until then a
  mod's draws ignore the seed, so a driven scenario with such a mod does not repeat.
- **What one call draws.** Op `0x60`'s generator gives 15 bits a draw. The draws of one call are joined with the
  first as the highest bits.
  - `math.random(m, n)` takes as many draws as cover the range's size: one for up to 2^15 values, two up to
    2^30, three up to 2^45. A wider range is an error. It gives `m` plus the joined value modulo the size, as op
    `0x60` gives its draw modulo its bound (`OpRandomModulo`). So the number of draws depends only on the size,
    and the small bias of a modulo is the game's own. `math.random(n)` is `math.random(1, n)`.
  - `math.random()` takes four draws, 60 bits, and gives the highest 53 divided by 2^53.

  So a seeded stream gives the same numbers by definition, whatever the library.
- **`math.randomseed` is not available.** The seed is the engine's.
- There is no clock and no date.
- Handlers at one instruction run in a fixed order (§7.4), and a table is iterated in a repeatable order (§5.1,
  criterion 11).

### 5.7 Session life

The session is scene state (engine layering spec §5.1, rule 5): a root reload, which a return to title is, drops
it with every other owner's scene state. A full load does not run that reset. The scripting native ends the
session itself when a load has read its record (§11).

1. **Build** between frames, before the next frame is entered:
   - at the start of the run, before the root script;
   - after a return to title (op `0x9`, or the 終了 dialog's タイトルに戻る), before the root script is entered
     again;
   - after a full load (op `0x1a1`), before `CALLBACK_LOAD.BIN` is entered, or before the restored root script
     when the game has no such callback. Either way it is before any saved frame is restored (§11).

   At each of these the interpreter is between frames (`VirtualMachine.Run`), so nothing is on the stack, Lua
   included. The first instruction boundary would be too late: a frame's entry is reported before its first
   boundary (`RunFrame`), so the `enter` hook of the root, and of `CALLBACK_LOAD.BIN`, would be due before the
   session existed.

   Three builds happen while a frame is running:
   - **after a data-only load** (op `0x19f`), at the first boundary after it. The old session ends as soon as
     the op has replaced the banks: the instruction's own `after` hooks do not fire, and the record of its
     status write is dropped. A load cannot happen inside a handler's script call (§10), so no Lua is on the
     stack. Neither game uses the op (§11);
   - **at a reload** (§13), at a boundary with no Lua on the stack;
   - **the console's own session** (§13), at the console step's first use when no enabled mod ships Lua. It runs
     no mod code, so it may be built where the chunk runs, inside a wait too.
2. **Populate** `maid.state` for every mod, from the save's sidecar or empty.
3. **Run** each mod's `lua/init.lua` in load order, then load the files its hooks name. §5.5 says what a failure
   does.
4. **Fire** `ome.session.start` with the reason: `boot`, `title`, `load` or `reload`.

**The order after a build is fixed:** `ome.session.start`, then the next frame's `enter` hook (§7.2), then the
`before` hooks of that frame's first instruction.

**A build's own code runs outside any instruction.** So `init.lua` and an `ome.session.start` handler cannot
call a script (§10), and what they write to a watched cell is delivered with the next instruction's records
(§7.3).

Nothing else carries over. With no enabled mod that ships Lua, no runtime is created at all, unless the console
asks for one (§13). The scripting native still exists then, to carry saved state (§11).

## 6. Packages

### 6.1 Layout

```text
<mod>/
├── manifest.toml
├── lua/                 Lua source; init.lua is optional and runs at session build
└── events/              event declarations (*.toml), for mods that define events
```

These join the directories the loader already reads: `patches/`, `scripts/`, `replace/`, `assets/`, and
localization's `text/` and `fonts/`.

### 6.2 Manifest

```toml
omp = 3                              # version 2 added localization; these tables need the next one

[game]
profile = "himegari"
target = "extended"                  # required with Lua; omp export refuses the mod

[lua]
api = 1                              # the maid API version the mod was written for
capabilities = []                    # ["vm"] for raw access
writes = []                          # the base mod's names the mod writes (§9.3)
calls = []                           # scripts the mod may call synchronously (§10)

[[requires]]
id = "himegari-base"
version = ">=1.0, <2"

[[hooks]]
event = "himegari.experience.award"
handler = "double_exp.lua:on_award"  # file under lua/, then the function
priority = 0
```

- **Format version 3** (decided 2026-11-14). The reader takes versions 1 and 2 and rejects unknown keys.
  Version 2 shipped in v0.5.0, with `[[language]]` and no Lua. That loader refuses a Lua mod either way: as an
  unknown key if the manifest said 2, as an unsupported version now that it says 3. The second message is the
  true one. The localization spec expected both features to share version 2 (its §9.2), before either was
  built; this decision replaces that sentence. A version 3 reader still reads versions 1 and 2.
- **Target.** The reader accepts only `target = "native"` today. `extended` is the other target the Open Maid
  Script spec defines (its §3.3). A package with `[game]` and Lua must name it, and `omp export` refuses the
  package, because native AGE cannot run what its Lua does.
- **Handlers.** A handler is a function declared at the top level of its file, as a global of the mod:
  `function on_award(event)`. A parse finds that declaration, so the link checks every hook's handler without
  running the file (decision 2). The simplest mod is one file with one global function.
  - A function that is only a field of the table a file returns is not a handler. That table exists only after
    the file has run, so no link could check it.
  - The session takes the function when the file has run. Two files of one mod may therefore use one name.

### 6.3 Hook forms

| Form | Keys | Needs `vm` |
|---|---|---|
| Event | `event` | no |
| Engine event | `engine` | no |
| Point | `point`; optionally `skip = true` (§7.1) | yes |
| Site | `script`, `fingerprint`, `site`, `when = "before"` or `"after"`; with `before`, optionally `skip = true` | yes |
| Script | `script`, `when = "enter"` or `"exit"` | yes |
| Watch | `watch` (a registry name, `name[*]` for a whole table, or `G[0x…]`) | only for `G[0x…]` |

`vm` is raw access (§8.1). A point, a site and a script hook hand the handler a frame, with its locals and the
instruction's operands. That is why they need the capability. An event gives typed fields, and a watch on a
name gives a named cell, so neither needs it. A name resolves through the base mod's layout, so a mod that
watches one requires the base mod.

**The first release builds the first three forms,** without `skip`, and no `calls` (§1.2). Its reader still
takes all six forms and `calls`, and the link refuses what the release has not built, naming it. So the later
forms need no new format version, and a mod that uses one fails on an earlier release with a true message.

### 6.4 Packages without a profile

A version 2 manifest may leave out `[game]`, and every profile's loader then accepts the package (localization
spec §11.1, built). Today such a package may hold only `[[language]]` tables, catalogs with `[[engine]]` entries,
and `fonts/`. That spec leaves it to this one to say what it may hold for Lua:

- **It may hold** `lua/` files and `[lua] api`. It is a library: other packages `require` it by id.
- **It may not hold** `[[hooks]]`, `events/`, `capabilities`, `writes` or `calls`. Each of them names something
  of one game.
- **Access is passed in.** A library has no `maid.vm` and no accessors of its own. A mod that has them may pass
  them to a library's function. No other mod can change what `require` returned (§5.2), so the access goes only
  where the caller sent it.

## 7. Hooks

### 7.1 Points and sites

- **When it fires.** At a point, or `before` a site: control has reached the instruction and has not executed it.
  `after` a site: the instruction has finished and nothing else has run.
- **Where in the loop.** `before` hooks run after the boundary's services (the engine tick, queued menu commands,
  a queued callback script) and before the opcode handler. `after` hooks run when the opcode handler returns,
  before the loop answers the front end's frame turn.
- **What the handler gets.** A context with the script name, the original offset, the point's name, the opcode
  and its operands.
  - `ctx.args[i].value` reads an operand. Writing is allowed when the operand is a cell, not an immediate.
  - There is no jump. Redirecting control flow is a patch.
- **`ctx:skip()`**, in a `before` handler, makes the VM pass over the instruction.
  - **A hook that may skip says so** in its manifest entry (`skip = true`). The loader then sees it, and the link
    report lists it. Calling `skip` from a hook that did not declare it is an error.
  - **The loader refuses it** on an instruction that jumps, calls, returns, waits, saves, loads or restores a
    frame (op `0xae`). Skipping a conditional jump would be a jump. Skipping the others would break the frame
    stack, a save or a restore.
  - **A skipped instruction** fires no `after` hook and writes nothing.
  - **The handlers after it still run.** Every `before` handler due at the instruction runs, whichever of them
    skipped. `ctx.skipped` reads true from then on, a skip cannot be undone, and the trace names the handler
    that skipped. An event on the instruction still reaches its subscribers. What they write to its fields is
    written, and the instruction that would have used it does not run.
- **What `after` means** depends on how the instruction leaves:

  | The instruction | `after` fires |
  |---|---|
  | falls through, or jumps inside its frame | when the opcode handler returns. The context does not say which way a branch went |
  | calls a script | when the callee has returned, which may be long after |
  | waits | when the wait has ended |
  | ends its frame, the scene or the run | never. An `exit` hook sees a frame's return (§7.2) |
  | raises a script error | never: the instruction did not finish. If the player's answer runs it again (再試行), its `before` hooks fire again |

- **Across a save.** An `after` hook on an instruction that waits, or that calls a script which waits, does not
  fire if the player saved in between and loaded later, because the session was rebuilt. The loader warns about
  such a hook.
- **After a load.** The frame the game resumes in runs part of its script a second time, and the hooks there
  fire again (§11).
- **The registry needs two facts per opcode** for these rules and for §10: whether it waits (always, may or
  never), and how it leaves (falls through, jumps, calls a script, ends its frame, saves, loads, restores). Its
  `category` and `code_target_kind` fields cover part of the second and are not complete: half the opcodes have
  the category `unknown`.

### 7.2 Script entry and exit

`enter` fires before the first instruction of a frame that starts at its script's entry. For the first frame
after a build it comes after `ome.session.start` (§5.7). `exit` fires when the frame returns to its caller. The
context gives the cause, as the trace does (`FrameCause`):

| Cause | `FrameCause` | The frame is |
|---|---|---|
| `call` | `CallScript` | a script another script called, or a callback script the engine queued |
| `root` | `TopScene` | the run's first root script |
| `title` | `RootReload` | the root script after a return to title |
| `restore` | `SaveRestore` | `CALLBACK_LOAD.BIN`, or a script that a load re-entered |
| `mod` | a new value | a script that a handler called through `maid.vm.call` (§10). The context names the calling mod |

**`exit` fires for a return and for no other end.** A frame can also end with a return to title, a load, a stop,
an exit request, a halt or an aborted script error. At a return to title every frame on the stack ends at once,
and a handler for each would run Lua in the middle of the unwind. Each of those ends also ends the session or
the run, so nothing a handler did there would last. The trace still reports them.

**A restored frame fires `enter`** (decided 2026-11-14). A load does not resume frames where they stopped. It
re-enters each saved script at its entry, runs it to its op `0xae`, and jumps from there to the saved position
(§11). Native does the same. So the frame is entered, and its `enter` handler runs with the cause `restore`. A
mod that prepares a script in `enter` needs this, because the session was rebuilt and its Lua locals are gone.

**The cause marks only the restored frames.** The scripts a restored frame calls on its way to op `0xae` are
ordinary calls, with the cause `call`. SYSTEM4's configuration scripts and INIT2 are among them. A handler that
needs to know whether a restore is under way reads `maid.session.restoring` (§11).

### 7.3 Watches

- **Recorded during the instruction, delivered after it.** Script memory records a write to a watched cell, with
  its old value, in its setters. The interpreter delivers the records when the opcode handler returns. Running
  Lua in the middle of an opcode handler would let it change state the handler is still using.
- **A record belongs to the instruction that made it.** Instructions nest: a wait's callback and a called script
  run inside the outer instruction's opcode handler. An inner instruction delivers its own records when it
  returns, and leaves the outer one's for the outer one's return. Otherwise the first inner instruction would
  deliver them in the middle of the outer handler.
- **The handler gets** the address, the registry name if there is one, the index for a table, the old and new
  values, and what wrote: the script and offset for an opcode, the mod for a handler (§12 says which mod), or
  the console.
- **A write that Lua makes is recorded too,** and delivered at the first delivery after it:
  - a hook's write, with the records of the instruction it ran at;
  - a watch handler's write, after the handlers already due. The nesting cap of §5.5 bounds a loop;
  - a write made outside any instruction, with the next instruction's records. Those are the writes of the
    session's build (`init.lua`, a session start handler) and of a console chunk (§13). They are not delivered
    where they are made, because a delivery runs handlers, and a handler may call a script.
- **What is not a write.** A load replaces the banks, and a return to title zeroes them; neither is recorded.
  Tools that seed the banks (`--seed`, `GameSession`) write the dictionaries directly, which is not recorded
  either. The records that an op `0x19f` instruction made are dropped with its session (§5.7).
- **A restore does write.** The scripts a load re-runs write cells, and those writes are delivered, as the same
  writes are at boot. A handler tells them apart by `maid.session.restoring` (§11).
- Version 1 watches integer and string globals, on write only.

### 7.4 Order

One instruction can carry several hook forms at once: an event on its point, a point hook and a site hook.

- **Before the instruction,** every handler due there runs in one list, by ascending `priority` and then by load
  order, whatever its form. An event's subscribers take their places in that list.
- **After it,** the `after` hooks run in one list by the same rule. The watch records are delivered next, each
  watch's handlers by the same rule.
- **The link report** lists the merged order for every instruction with more than one handler, as an ordered
  interaction (mod loader spec §7).
- **Subscribers of an event raised from Lua** (§8.2) run inside the `emit`, in their own order.

Bytecode that another mod inserted before a site runs before that site's hooks: the hooks fire when control
reaches the original instruction.

### 7.5 Cost

- **Plan.** At link, the loader turns every hook into a table per script: instruction index to handler list.
- **Dispatch.** A frame takes its script's table when it is entered. The loop tests that one reference per
  instruction; for a script with no hooks it is null.
- **Watches.** A global write tests one reference; with watches present, one bitmap lookup.
- **Broad watches at a load.** A load re-runs the scripts that fill the game's data tables (§11). A watch on a
  whole table therefore receives the whole table at every load, as it does at boot. The cost of one such watch on
  Kamidori's largest table is measured and recorded with the other budgets.
- **Target.** With no Lua mod, a corpus sweep is no slower than today within measurement noise. The per-call cost
  of a trivial handler is measured in the spike and becomes a recorded budget.
- **One loop.** The engine has one instruction loop (`RunInstructions`, engine layering spec §5.4), so no path
  executes an instruction without the dispatch.

### 7.6 Hooks and patches

- A hook names an original site: script fingerprint plus original offset. Patches from other mods do not move it.
- **The linker keeps what this needs.** For each patched script, the link cache stores where every original
  instruction went (`ScriptOffsetMap`, the `offsets` of `link.json`). `MountedModLayer.SiteBinding` answers for a
  script: unchanged, patched with its table, or replaced. The localization work built this for catalog entries
  with `at`. Hooks bind through the same call.
- If a patch removed or replaced the instruction, the table has no entry for it. The link fails and names both
  mods.
- If a mod replaces the whole script, a hook that names the original's fingerprint fails the link, and the
  report names the replacing mod. The mod loader checks a patch against the replacement's fingerprint (its
  §4.2), so a patch written for the replacement links. A hook is not given that: a replaced script is another
  mod's code, which version 1 does not hook (§1.3).

### 7.7 Engine events

Defined by the engine, prefixed `ome.`:

| Event | When | Fields |
|---|---|---|
| `ome.session.start` | when a session has been built, before the next frame is entered (§5.7). Its handlers cannot call a script | `reason` |
| `ome.session.resumed` | when a load has restored its last frame, before the first instruction the game resumes at (§11) | `slot` |
| `ome.save.writing` | before every op `0x19e` instruction, as a `before` hook on it would. Nothing has been captured yet, so what a handler writes is in the save | `slot` |
| `ome.text.present` | reserved for the localization filter (localization design §6.5, `docs/superpowers/specs/2026-11-10-localization-design.md`). That section's example writes the hook as `point = "ome.text.present"`; in this spec's forms it is `engine = "ome.text.present"` | |

`ome.text.present` would run inside the text native, in the middle of an opcode, which decision 3 does not allow.
It stays reserved. When it is designed it needs its own exception. The localization spec already narrows it: a
filter is a pure function of its input and cannot write VM state.

## 8. The engine API

### 8.1 `maid.vm` (capability `vm`)

```lua
local vm = maid.vm

vm.g[0x62ccb]            -- integer global: read or write, signed 32-bit
vm.gf[addr]              -- float global
vm.gs[addr]              -- string global
vm.l[1]                  -- integer local of the frame the hook fired in; also vm.lf, vm.ls
vm.g:range(addr, count)  -- many cells in one call, as a table

vm.frame                 -- { script = "ADDEXP.BIN", site = 0x9a, depth = 3 }
vm.frames()              -- the call stack, outermost first
vm.script_id("ADDEXP")   -- catalog name to packed id
vm.call("CALCDMG")       -- run a script now (§10)
```

This is raw access, and it is all the `vm` capability gives: cells by address, the frame's locals and operands,
and script calls. Access by name belongs to the accessors (§9.3), which need no capability.

It binds to two owners, as the engine layering spec foresaw (its §8):

| Part | Owner |
|---|---|
| `vm.g`, `vm.gf`, `vm.gs` and the pointer cells | the script memory native |
| `vm.l`, `vm.lf`, `vm.ls`, `vm.frame`, `vm.frames()`, `ctx.args`, `vm.call` | the interpreter, through an interface it implements (§14) |
| `vm.script_id` | the script catalog |

- **Integers** must be whole and fit in 32 bits; anything else is an error, not a truncation. A read gives the
  signed low 32 bits of the cell, and a write stores that form. The port's cells are 64-bit and can hold either
  extension of a value (backlog, "Integer cells are not normalised to 32 bits when written").
- **Floats.** A float cell holds a 32-bit float. A read gives its value; a write rounds to single precision.
- **Strings** must be encodable in CP932. A numbered save writes string cells in CP932 and would store any other
  character as `?` (`NativeNumberedSaveCodec`).
- **Pointer cells** are readable as addresses and not writable in version 1.
- **Locals** exist only while a hook with a frame is running. In an engine event they are an error. A console
  chunk at a wait reads the waiting frame's (§13).
- **Positions.** `site` is an original offset, so it exists only for an instruction of the base script. For code
  that a patch inserted, `site` is absent. The frame then gives its offset in the linked script and the mods that
  patched the script, which the link records (`link.json`). The trace (§12) and an error report (§5.5) give the
  same. Naming the one mod that inserted an instruction needs the link cache to keep each inserted run's owner.
  The patch applier knows it (`OmsPatchProvenance.Plan`); the cache keeps only where the base instructions went.
- **No dictionaries, no Godot objects, no .NET objects** ever reach Lua. Every value is copied or wrapped by the
  facade.

### 8.2 `maid.events`

`maid.events.emit(name, fields)` raises an event that was declared with `raised_by = "lua"` (§9.2). Subscribers
run before it returns, and the table comes back with any fields they changed. Subscribing is done in the manifest
only.

### 8.3 `maid.state`

A table per mod, restored at session build and saved with each numbered save (§11). It holds booleans, finite
numbers, strings and nested tables with string or integer keys. A function, a cycle, a number that is not
finite or an oversized table is an error at save time, reported against the mod. The game's save is still
written, with the mod's last good state beside it (§11).

### 8.4 `maid.log`, `maid.mod`, `maid.session` and `maid.names`

- `maid.log.info`, `.warn`, `.error` write to `diagnostics/lua.log` with the mod's id.
- `maid.mod` holds the mod's id, version, and the version that wrote the loaded state.
- `maid.session.reason` is the reason the session was built (§5.7). `maid.session.restoring` is true while a load
  restores its frames (§11). Every mod has both, with or without `vm`.
- `maid.names` exists only in a package that carries a layout, which is a base mod. It holds the accessors over
  that layout, `maid.names.g` and `maid.names.table`, for the base mod's own code (§9.3). The name is a
  suggestion.

### 8.5 What the roadmap named and this version leaves out

- **`maid.hooks`.** Registration is in the manifest, so there is nothing to call.
- **`maid.game`.** Game access is what a base mod exports (§9). The `maid` name stays engine-only, so a reader can
  tell from the name which side a call belongs to. The engine builds the accessors, and a mod still gets them
  from the base mod's `require`, because the names are the base mod's.
- **`maid.assets`, `maid.audio`, `maid.ui`.** Engine services are future work.

## 9. The game layer: base mods

### 9.1 What a base mod is

One first-party package per game, `<profile>-base`, built from three things:

| Part | Source | Written by |
|---|---|---|
| Layout: every registry global's name, bank, address and shape | `vm-map/globals[-<profile>].toml` | generated |
| Events: named occurrences with typed fields | `events/*.toml` in the base mod | hand, as data |
| Library: helpers that need judgment | `lua/` in the base mod | hand, as Lua |

The accessors are not a fourth part. The engine builds them from the layout (§9.3).

Most of the base mod is generated or declarative, and only behaviour is Lua. That keeps it in step with the
registries and lets the loader validate it.

- **Location.** `mods/<profile>-base/`, a new top-level folder. It needs a row in the documents map
  and in `PROJECT-STRUCTURE.md`. The layout file joins the generated-files table.
- **Patch points keep one source,** `vm-map/patch-points-<profile>.toml`, and the base mod carries a generated
  copy, as it carries its layout. An export does not carry the registry today (backlog, "Toolchain packaging for
  exported ports"), so without the copy an event that names a point would resolve only in a repository build.
  The mod loader spec said the base mod would own the points, and that the registry would live in `vm-map/`
  only until then (its §5). Decided 2026-11-14: the registry stays, and the copy is how the base mod carries the
  points. This replaces that sentence.
- **Exported names are an API.** The registry's names are reverse-engineering labels, and they are renamed as
  understanding improves. A rename must not break the mods that use the name:
  - the base mod checks in a generated list of the names it exports;
  - a rename keeps the old name as an alias, or raises the base mod's major version;
  - `validate.py` fails a rename that does neither.

  `globals_build.py` has no aliases today; the layout generator would add them. A mod's write list and an
  event's fields use the same exported names, so the rule covers them too. An alias and the name it stands for
  are one name.
- **Mounted on demand.** The engine ships the base mods. One joins the stack only when an enabled mod requires
  it, so an unmodded game still runs with no Lua.
- **Capability.** The base mod declares `vm`. A mod that depends on it needs no capability: it reads by name,
  and writes the names it lists (§9.3).

### 9.2 Declaring an event

```toml
# mods/himegari-base/events/experience.toml
[[event]]
name = "himegari.experience.award"
point = "himegari.experience.after_calculation"
description = "One experience award, after the multipliers and before it is applied."

[[event.field]]
name = "amount"
at = "L[0x1]"                    # the frame's integer local 1
type = "int"
access = "rw"

[[event.field]]
name = "unit"
at = "current_unit_id"           # a registry global, by name
type = "int"
access = "r"
```

- **A field is a live view of a cell.** Reading `event.amount` reads the local; writing it writes the local. The
  next handler sees the change.
- **Locations** are a global by registry name or address, a local, an operand of the instruction at the point, or
  a table element indexed by another location (`unit_current_levels[current_unit_id]`).
- **Types** are `int`, `float`, `string` and `bool`. Writing a field declared `r` is an error.
- **Everything is checked at link:** the point exists, the names resolve, and every subscriber's event exists.
- **An event that needs computation** is declared with `raised_by = "lua"` and no point. The base mod's own
  handler builds the fields and calls `maid.events.emit`.

### 9.3 Accessors, declared writes and the library

What a dependent mod sees:

```lua
local game = require("himegari-base")

game.g.current_unit_id                         -- read a named global
game.g.stage_turn_limit = 30                   -- write one
game.table("unit_current_levels")[unit] = 5    -- write an element of a named table
```

```toml
# manifest.toml
[lua]
writes = ["stage_turn_limit", "unit_current_levels"]
```

**The engine builds the accessors.**

- It builds `g` and `table` from the base mod's layout, once for each mod that requires the base mod, and puts
  them in that mod's view of the base mod (§5.4).
- The check on a write has to know which mod is writing, and it has to sit where that mod cannot go around it.
  Lua code in the base mod can do neither. An event's fields are built the same way, from data (§9.2).
- There is no proxy library in Lua. One would take the base mod's `maid.vm` as an argument, which is the
  hand-over of `maid.vm` that §5.2 guards against.

**Reads need no declaration.** A mod can read any name the base mod exports.

**A mod lists the names it writes,** under `[lua] writes` (decided by the user, 2026-11-14).

- A table's name covers all of its elements.
- Writing a name that the list lacks is an error (§5.5). The message gives the mod, the name, and the list the
  name is missing from.
- A listed name that the base mod does not export is a link error.
- A write to an event's `rw` field needs no entry. The subscription declares it, and the link report takes it
  from the event's declaration.
- The list is also for a mod with `vm`. Such a mod can write a cell by address without it; what it writes by
  name it lists like any other mod.

**Why a list.** The two other answers:

- **Read-only accessors.** A plain mod could then write only the event fields that the base mod's author
  foresaw. The first base mod has no helpers, so most writes would need `vm`.
- **Accessors that write freely.** The link report could then not say which mod changes what, and its "opaque"
  mark would name only the mods that use raw addresses.

A list keeps a write as direct as the second answer and as visible as the first. It also follows how this design
already works: a hook that may skip says `skip = true`, a mod lists the scripts it calls, and the link reports
both.

**What the link report does with it** (§12). It lists each mod's writes. Two mods that write one name are an
ordered interaction, in load order:

```text
easy-mode   writes stage_turn_limit, unit_current_levels
hard-mode   writes stage_turn_limit
  stage_turn_limit: written by easy-mode, then hard-mode (ordered interaction)
```

**Keeping the list from becoming a chore.** The typing is small: one entry for each distinct name, and a table
counts once. The real cost is a mod that fails on a rare path because a name was left out. Two things deal with
that:

- **`omp check` finds the writes it can read.** It reports a write whose name is written out in the source and
  missing from the list: `game.g.<name> = …` and `game.table("<name>")[…] = …`. It prints the list as it should
  read. `omp build` refuses to pack a mod with such a write, because the write would fail when it ran. The scan
  reads the source only: a write through a computed name, or through a local that stands for `game.g`, is
  checked when it runs.
- **`--lua-dev` reports instead of refusing** (§13). An unpacked mod's undeclared write goes through, and
  `diagnostics/lua.log` names it once. The author plays, then copies the names into the list. The link report
  marks a mod that runs this way. A packed mod's list is always enforced.

A third idea is not built: one entry that stands for a group of names (§16).

**Helpers.** Helpers that carry invariants are hand-written for each game.

- **A helper's writes are the base mod's.** A helper is the base mod's code, and it uses the base mod's own
  accessors (`maid.names`, §8.4), which have no write list: the base mod owns the names. So a mod does not list
  what a helper writes, and does not have to know which cells a helper touches.
- **The trace names the mod that called the helper** (§12).
- **The link report does not show a helper's writes.** A mod that changes a value through a helper and a mod
  that writes the same cell directly show no interaction there. The trace is where it shows.
- **Where the game has a script for a change, the helper calls it** (§10) instead of writing cells, as the
  roadmap asks. Those helpers arrive with the script calls (milestone 5). The first base mod has accessors
  only.

### 9.4 The worked example

The game guide already shows doubling experience as a patch on `himegari.experience.after_calculation`. The same
mod in Lua:

```toml
# manifest.toml
[[requires]]
id = "himegari-base"
version = ">=1.0, <2"

[[hooks]]
event = "himegari.experience.award"
handler = "double_exp.lua:on_award"
```

```lua
-- lua/double_exp.lua
function on_award(event)
    event.amount = event.amount * 2
end
```

The mod names no address, no script and no fingerprint. Those live in the base mod and the registries.

## 10. Calling game scripts

`maid.vm.call(script)` runs a script as a nested frame and returns when it exits. AGE scripts take their inputs
and give their results in globals, so a helper sets cells, calls, and reads cells. The interpreter runs it the
way `call-script` runs a script, on the same stack.

**The call cannot wait, save, load or continue a restore.** If the script reaches a wait, or op `0x19e`, `0x19f`
or `0x1a1`, or op `0xae` while a restore is pending, the call fails with a Lua error naming the opcode.

- **Why no wait.** At a text wait the player can open a menu, and a menu can save. That save would record the
  called script's frame, and nothing could restore it: a load re-enters saved scripts from their entries (§11),
  and the Lua function that was waiting for the call is not in the save. The other waits cannot lead to a save,
  but each would hold a handler on the stack for as long as it lasts. Decision 4 rules them all out.
- **Why no save or load.** A save taken inside the call has the same problem without the wait. A saved frame
  records which entry of its script's call table it was in (`CaptureSavedFrames`). The frame that fired the hook
  is at the hooked instruction, which is no such entry, so a load would resume it in the wrong place. A load
  inside the call would replace the banks under a handler that is still running.
- **Why no restore.** Op `0xae` continues a pending restore for whichever frame runs it: its handler asks only
  whether a restore is pending, not which frame is running (`OpContinueSaveLoadStackRestore`). A restore is
  pending from the moment the restored root is entered until the last saved frame is restored, and
  `maid.session.restoring` is true throughout (§11). Every script that can be on a saved stack has an op
  `0xae`. If a hook in the prologue called one, the restore would go on inside the Lua call, and the refusal of
  its first wait would leave it half done. Outside a restore the op does nothing, and a called script may run it.
- **What counts as a wait.** Every wait is a member of `IInputAndWaits` and blocks in
  `InputAndWaits.Blocking.cs`. They are the text waits, the sleeps, the timed-callback wait, the effect and
  screen transitions, the text reveal, the modal movie, the hidden-window wait and the dialogs. The list is the
  interface's. The engine layering spec's table of waits (its §5.3) sorts them by what they do to time, and
  has no row for the hidden-window wait.
- **What blocks and is not a wait.** Three things. None of them runs a script or takes a save:
  - the port's wait for the display at a present (ops `0x222` and `0x20c`; native presents without waiting);
  - fast-forward's pacing sleep;
  - the front end's modal hold. The frame turn after each instruction blocks while the options panel, the 終了
    dialog or a menu is open (`FrameYield`). No tick runs meanwhile. A stop or a return to title asked for
    there is taken at the next boundary, as below. This one can hold a handler on the stack for as long as the
    player leaves the panel open. That is accepted, because nothing in the game moves meanwhile.
- **A script error inside the call** would open AGE's error box, and the dialogs are waits. It is refused as one:
  the call fails with a Lua error that carries the script error's message.
- **Checked at run time.** The interpreter refuses before it calls the waiting member. So a headless run, whose
  stand-in for the waits returns at once (`HeadlessWaits`), fails the same way as the port. A test lists every
  member of `IInputAndWaits` as a wait or not, and fails on a member that is on neither list. `FrameYield` is
  on the second list.
- **Checked ahead.** A mod lists the scripts it calls under `[lua] calls`. `omp check` walks each one's static
  call graph. It fails on an opcode that always waits, saves or loads, and warns on one that may wait. This needs
  the two opcode facts of §7.1 in `vm-map/opcodes.toml`, which is where opcode facts live. The registry does not
  have them yet: which opcodes reach each wait has to be read off the opcode handlers once. The walk also
  warns in two places it cannot decide:
  - a `call-script` whose script id comes from a cell, which it cannot follow;
  - an op `0xae`, which fails the call only during a restore.
- **The list is enforced.** A call to a script that `calls` does not list is an error.

**When the script ends the run or the scene.** A called script can stop the run (a halt, or op `0x1`'s exit
request) or return to title (op `0x9`). The front end can ask for either at any boundary inside it
(`StopAndReload`). In those cases the call does not return. The handler is abandoned with every Lua frame above
it, and the engine goes on with that outcome, as it does after a `call-script` that ended the same way. Each of
these outcomes ends the session, so no Lua runs in it again.

**How it unwinds.** A return to title, a stop and a halt come back from the called frame as a return value
(`FrameOutcome`), not as an exception. So the unwind has to be made:

1. the `vm.call` binding turns the value into an exception and throws it through the Lua frames above;
2. the hook dispatch catches it and turns it back into the outcome;
3. the instruction loop returns that outcome, as it would from `call-script`.

The exceptions the engine throws itself (§2) pass through the same frames and are not caught. Both are the throw
that `pcall` must not catch (§5.1).

- **While it runs:**
  - hooks inside the called script fire as usual, and its frame has the cause `mod` (§7.2);
  - a step budget bounds it, and the nesting cap of §5.5 applies;
  - a queued callback script (`CALLBACK_SETTING.BIN`, `CALLBACK_WINDOW.BIN`) stays queued, as it does while a
    statement block is open (`ServiceInstructionBoundary`);
  - a console chunk or a reload that arrives is held until no Lua is on the stack (§13).
- **A failed call leaves what the script already did to the cells.** The static check exists so this does not
  happen in a released mod.
- **A failed call puts the statement block back.** A block is scene state of the tick, a flag and a depth
  (`Natives/EngineTick.cs`), and scripts hold one open across waits; ADDEXP and MES.BIN do. So a call that is
  refused at a wait is likely to be inside one. Left open, the block would withhold presentation and the queued
  callback scripts until the next return to title. The interpreter notes the block's state when the call
  starts and restores it when the call fails. The implementation plan lists what other scene state a script can
  leave half set; the block is the one found so far.
- **Where it can be used.** In a handler the interpreter invoked at an instruction: a hook, a watch or an event.
  It cannot be used:
  - in a console chunk (§13);
  - in `init.lua` or an `ome.session.start` handler. A build runs between frames, where no instruction is under
    way. The game's tables may not be filled yet, and after a load a restore is pending (§5.7).

For anything interactive, a mod ships a script and a patch that calls it. The loader already does that, and a save
inside it works.

## 11. Saves and state

- **File.** `SAVEnn.modstate.json` beside `SAVEnn.DAT`. It holds, per mod id, the mod's version and its
  `maid.state`. JSON and a Lua table differ in four ways, which the format settles:
  - an object's keys are strings, so the format marks each key as a string or an integer. The keys `1` and
    `"1"` are then two keys;
  - a table is written as an array only when its keys are 1 to n with no hole;
  - a number that is not finite has no JSON form. It is refused at the capture (§8.3);
  - keys are written in sorted order, so the same state gives the same file.
- **Owner.** Mod state is the scripting native's record (§14; engine layering spec §5.1, rule 6). The native save
  file has no room for it, so the save store keeps it beside the save, as it keeps the stack stamp
  (`SAVEnn.mods.toml`).
- **The store.** `SidecarNativeDatStore` writes a sidecar after each save, and deletes and copies it with the
  save. It takes one fixed text when it is built, which suits the stamp. Mod state differs per save, so the store
  must take the text at the moment of the save. That is the one change to it.
- **Not a transaction.** The store has no transaction: the stamp is written after `SAVEnn.DAT`, each file by its
  own replace, and the state file is written the same way. If the process dies in between, the slot holds a new
  save with an old state file, or with none. To detect that, the state file records the save's timestamp and
  play time, which the store has at the write and the save's header repeats. A load reports a state file that
  does not match its save and uses empty state.
- **Writing.** At op `0x19e`, `ome.save.writing` fires first (§7.7). Then the save native captures the records,
  the scripting native's among them, and the store writes the save and its sidecars. A save that fails writes no
  state.
  - **The state is serialized at the capture,** before the store is called. The store only writes text it was
    given. In `SaveNumbered` the capture comes before the store's call, and only I/O and data errors are caught
    there: an error from a mod's state would otherwise fail the whole save, or leave the opcode handler
    uncaught.
  - **A mod's bad state never fails the game's save.** The scripting native serializes each mod's state on its
    own. If one cannot be serialized (§8.3), the file gets that mod's last good block: the one it loaded, or
    the one it last saved. The error is reported against the mod, and `--lua-errors halt` ends the run.
- **Loading.** Ops `0x19f` and `0x1a1` replace the banks from the save. The store hands the slot's state text
  over as it reads the file, before the save is decoded; the scripting native keeps it and uses it only if the
  load succeeds. The session is rebuilt (§5.7) with each mod's state restored. A save with no state file gives
  empty state; that is a save made without Lua mods, or by native AGE, or one whose state file was lost. The
  stamp beside the save (`SAVEnn.mods.toml`) tells the last case apart: if it names a mod that ships Lua in the
  current stack and there is no state file, the load reports it. Neither game uses op `0x19f`: in both, only
  `SAVE.BIN` loads, and it uses op `0x1a1`.
- **Removed mods keep their state.** A block for a mod that is not in the stack is carried into the next save
  unchanged, so reinstalling the mod restores it. The loader treats selectors the same way. This must also hold
  when the last Lua mod is removed. So the scripting native exists in every run of the port and carries the
  blocks of the state file it last loaded. Only the Lua runtime waits for a mod that needs it.
- **The stamp.** The loader marks a package that changes no script or asset as presentation-only in the stamp
  (`ModLayout.ChangesNoContent`; localization spec §8). A package with `lua/` or `events/` is never marked so.
- **The native save is unchanged** by any of this. A mod that writes cells through `maid.vm` changes the save the
  same way a patch would.

**What a full load runs** (`VirtualMachine.Run`, `CreateRestoredFrame`, op `0xae`'s handler). After op `0x1a1`
replaces the banks, the engine:

1. unwinds every frame;
2. runs `CALLBACK_LOAD.BIN`, if the game has one;
3. re-enters the saved root script at its entry and runs it forward. For SYSTEM4 that is its whole prologue: the
   configuration scripts, INIT2 and the `*INIT` data tables;
4. at the root's op `0xae`, enters the next saved script the same way, and so on down the saved stack;
5. at the last script's op `0xae`, jumps to the saved position. That is the frame's read-message marker, which
   is the start of the page that was on screen. A frame that had passed no marker when it was saved goes on
   from the instruction after its op `0xae`. Neither is the instruction the save was made at.

Native does the same. A load therefore executes real instructions before the game resumes, and they are largely
the ones a boot executes. In both games the root is SYSTEM4, and its prologue calls LOADCONFIG, INITCONFIG and
INIT2 before its op `0xae`; Kamidori's also calls CHECKCONFIG. The rules that follow:

- **Hooks, watches and events fire during a restore as they do for any other execution.** A mod that edits a
  data table from a hook needs this: a load zeroes the cells past the saved prefix, and the prologue fills them
  again.
- **They fire in the new session,** which is built before step 2 with the save's state. `ome.session.start`, with
  the reason `load`, comes before any of them.
- **A restore is marked.** `maid.session.restoring` is true from the session's build until step 5's jump.
  Without it, a handler that keeps a running total in `maid.state` would add the prologue's writes to the saved
  total at every load. The frame's cause cannot tell it: the prologue's scripts are ordinary calls, and a watch
  handler is given no frame (§7.2).
- **The end of a restore is an event.** `ome.session.resumed` fires after step 5's jump, with the flag already
  clear (§7.7).
- **Part of the last script runs again after that.** From the saved position to where the save was made, the
  resumed frame executes instructions it already executed before the save. Hooks, events and watches there
  fire a second time, after `ome.session.resumed` and with the flag clear.
  - A handler in a script the player can save in must therefore be safe to run twice. One that adds to a total
    in `maid.state` adds twice: once before the save, which is in the saved state, and once after each load.
  - Nothing marks where the repeated range ends. A save records the marker, not the position.
  - This is the game's own behaviour, and native's. The game's instructions in that range run twice as well,
    and so does a bytecode patch placed there. The guide says it beside `maid.session.restoring`.
- **A load that is abandoned** (`CALLBACK_LOAD.BIN` returns to title) ends the session with the flag set. The
  next session starts with it clear.

## 12. Conflicts and attribution

- **Link report** (`diagnostics/mod-link.txt`) gains: every hook with its handlers in order, every event with its
  owner and subscribers, each mod's capabilities, and the names each mod writes (§9.3).
- **Classes** follow the roadmap (§3.5):
  - several handlers on one hook are an ordered interaction;
  - two mods that write one name are an ordered interaction, in load order;
  - a mod with `vm` is opaque, and the report says so. So is a mod that runs with undeclared writes allowed
    (`--lua-dev`).
- **What the report cannot show:** a write that a base mod's helper makes for a mod (§9.3), and what a handler
  does with the names it reads. The trace shows both.
- **Hard conflicts:** a hook on a site a patch removed, an undeclared event or point, a fingerprint mismatch, a
  missing dependency for an event's owner, a name in a write list that the base mod does not export.
- **Trace.** `--lua-trace` writes one line per handler call to `diagnostics/lua-trace.jsonl`: mod, handler, hook,
  script and offset, duration, and each declared field before and after. A write through `maid.vm` or through an
  accessor is logged with the address, the name if it has one, and both values. This is the roadmap's
  `base 50 -> level-gap mod 25 -> double-exp mod 50` chain.
- **Whose write it is.** The trace, and a watch record (§7.3), name the mod whose handler is running, the
  innermost one first. A base mod's helper runs for the mod that called it, although it writes through the base
  mod's own access. A subscriber of an event raised from Lua is the innermost handler while it runs.

## 13. Tools

| Tool | Effect |
|---|---|
| `omp build`, `omp check` | parse every Lua file, validate hooks, handlers, events, `calls` and `writes`, and report. No mod code runs (§14.2) |
| `omp lua-stubs --output DIR` | write editor definitions for `maid` and for each mounted mod's events and layout |
| `--lua-errors halt` | stop on a handler error (§5.5) |
| `--lua-trace` | the attribution trace (§12) |
| `--lua-reload` | rebuild the session when a file under an unpacked mod's `lua/` changes, keeping `maid.state` |
| `--lua-dev` | for an unpacked mod: a write to a name its list lacks goes through and is reported (§9.3) |
| driver step `lua [MOD] CODE` | run a chunk with the `vm` capability and return its result as text; port only |

- **The console step and the reload reach the engine thread as requests.** The front end sends them through
  `InputAndWaits.Invoke`, as the driver's `state` and `rand-seed` do. The engine thread runs a request at its
  next instruction boundary, or inside the wait it is blocked in: a request wakes a waiting engine at once.
- **A request only hands the work over.** `Invoke` blocks the front end's thread until the request has run. And
  requests also run at the boundaries inside a script that a handler called, with Lua on the stack. So the
  request gives the chunk, or the reload, to the scripting native and returns. It never waits for a handler to
  finish, and it never queues itself again.
- **The console chunk** runs on the engine thread the next time the scripting native is serviced with no Lua on
  the stack: at an instruction boundary, or when a wait wakes. So it also runs while the game sits at a text
  wait. The driver step polls for the result once a frame, as its timed operations do (`DriveDispatcher`), and
  completes when the chunk has run.
  - **Its environment.** The console has an environment of its own, with `maid.vm` and with accessors that have
    no write list. The accessors come from the layout of the profile's base mod, which the engine reads for the
    console even when no mod has mounted that base mod. The environment lasts for the session, so a chunk can
    use what an earlier chunk defined. Given a mod's id, the step runs the chunk in that mod's environment
    instead, with that mod's globals, its `maid.state` and its write list. That is how a mod is debugged.
  - **With no Lua mod enabled** the step creates the runtime, and a session for the console alone, on its first
    use. Exploring an unmodded game is what the console is for (§1.1). A run that never uses the step still
    creates no runtime. That build runs no mod code, so it may happen inside a wait, where the chunk runs (§5.7).
  - **Frames.** At a wait the waiting frame is there. The chunk reads `vm.frame`, `vm.frames()` and that
    frame's locals, as the driver's `state` step reads the frame stack.
  - It reads and writes cells, and the state of the environment it runs in.
  - It cannot call a script: what a front end asks for reads or sets state and never runs a script (engine
    layering spec §5.4).
  - Its write to a watched cell is delivered with the next instruction's records (§7.3), for the same reason.
- **The reload** is taken at the next instruction boundary with no Lua on the stack, and never inside a wait.
  The build runs mod code (`init.lua`, the session start event), which must not run inside an opcode handler.
  At a text wait, that boundary is the first instruction after the click; no handler can fire before it. A
  reload whose build fails keeps the session that was running and reports the error (§5.5).
- **The driver step is the port's alone,** like `language` and `reference-diff`: a capability the native target
  does not report.
- Changing a manifest or an event file needs a restart, because it changes the link.
- The guide for mod authors goes under `docs/modding/`, in the same change that ships the feature.

## 14. Engine changes

### 14.1 Where Lua sits in the layers

| Piece | Layer and project | Why there |
|---|---|---|
| Hook dispatch: an instruction's hooks, the context, `skip`, script entry and exit, delivering watch records, the synchronous call with its refusals | the interpreter, in `Ome.Engine.Vm` | it is part of the instruction loop, which is not a native |
| A scripting native (`Natives/Scripting*.cs`; the name is a suggestion). It owns the session, each mod's state and its save record, handler order, budgets and the error policy, the log, the trace and the notices. It exists in every run of the port; it creates the Lua runtime only when a mod needs one (§11) | `Ome.Engine` | it is state that a save records and a reset rebuilds |
| Watch recording | `ScriptMemory`'s setters, in `Ome.Engine` | script memory owns the banks |
| The hook plan, the event declarations, each base mod's layout and each mod's write list, as data | types in `Ome.Engine`; `Ome.Mods` builds them at link | the linker and the interpreter both read them, and `Ome.Mods` references `Ome.Engine`, not `Ome.Engine.Vm` |
| The checks on a mod's access: its capability, its write list, its `calls` list | the facade `IScriptAccess`, in `Ome.Engine`, with one face for each mod | the rules are the engine's, so a runtime cannot loosen them |
| The interpreter's side of `maid.vm`: frames, locals, operands, the synchronous call | an interface that `Ome.Engine` defines and `VirtualMachine` implements | the pattern of `IInterpreterInput` (engine layering spec §5.4, rule 2), so references still point one way |
| The runtime seam: `IModRuntime`, `IModSession` | `Ome.Engine` | it is what the scripting native calls, and what the link calls to check a file |
| The Lua library, the sandbox, the environments and the read-only views, the module loader over packages, the `maid` bindings, the accessors' Lua side, the state serializer | new `Ome.Scripting.Lua`, which references `Ome.Engine` only | the only project that names the library |
| Building all of it | composition: `godot/Main.cs`, `Ome.Cli` | they build every native and hand it to the VM |

How this meets the layering rules:

- **Lua is not a device.** A device is what a front end or an operating system supplies (engine layering spec
  §3.1). Every front end runs the same Lua. So the library sits behind a seam of `Ome.Engine`, as the script
  provider and the save store do. `ProjectBoundaryTests` lists those seams, each with its reason; `IModRuntime`
  and `IModSession` join the list.
- **A native never runs a script** (rule 9). The scripting native starts no script. `maid.vm.call` reaches the
  interpreter only inside a handler that the interpreter itself invoked, through the interface the interpreter
  implements. The waits native runs the interpreter's callbacks in the same way (the service callback of
  `WaitForInput`).
- **Engine thread only** (rule 4). Lua handlers, the native and the bindings run on the engine thread and take
  no lock. The front end reaches them by request (§13), or reads what the native publishes for it: the notices.
- **Scene state** (rule 5). The session and the mods' state are the native's `Scene`, which op `0x9`'s reset
  replaces. `SceneReloadTests` then covers the native with the other owners. A load replaces it too, from the
  native's own record.
- **Save records** (rule 6). The native writes and reads the mods' state; the save native decides the order
  (§11).
- **No revision names.** Nothing here reads an engine build's name (engine layering spec §7.6). A base mod is
  chosen by profile, in a package's manifest.
- **A VM given no scripting native has no hooks,** as a VM given no audio native is silent. Tests and tools that
  run a bare table are unchanged.

### 14.2 The seam

```csharp
// Ome.Engine: what the scripting native calls. Ome.Scripting.Lua implements it.
public interface IModRuntime : IDisposable
{
    // Parse only, and run nothing: syntax errors, the file's top-level functions, the names it writes.
    ModSourceCheck Check(ModSource source);
    IModSession BuildSession(ModRuntimeStack stack, IScriptAccess access, ModState state, SessionReason reason);
}

public interface IModSession : IDisposable
{
    void Invoke(HandlerId handler, HookContext context);   // engine thread only
    ModState CaptureState();
}
```

`IScriptAccess` is the facade of §8.1 and §9.3. It wraps the script memory native for the cells, and the
interface the interpreter implements for frames, locals, operands and calls. It is the only way a runtime
touches engine state.

- **One face for each mod.** A face carries the mod's capability, its write list and its `calls` list, and
  refuses what they do not allow. The runtime binds `maid.vm` and the accessors to the mod's face. So the
  checks are the engine's code, and a mod's Lua cannot reach past them.
- **`Check` is what the link uses.** `Ome.Mods` cannot name the Lua library. The composition hands it the
  runtime, and the link and `omp check` call `Check` for each file: the handlers of §6.2, the write scan of
  §9.3 and the syntax errors of §5.5 all come from it.
- **`CaptureState` serializes each mod's state on its own** and reports the mods whose state failed (§11).

### 14.3 The other changes

| Part | Change |
|---|---|
| `Ome.Mods` | `lua/` and `events/` in the package layout; `[lua]` and `[[hooks]]` in the manifest, with its next format version; link validation through the runtime's check; the hook plan, the base mods' layouts and the write lists in the link cache; base-mod mounting; the `omp` commands, with the write scan; a package with Lua is not presentation-only |
| Persistence | the save store takes a sidecar text per save (§11) |
| Front end (`Ome.Engine.Frontend`, `godot/`) | flags, the error notice, the driver's `lua` step, the reload watcher |
| Tools | the base mods' layout and patch-point generators; the exported-names check in `validate.py` (§9.1); the two opcode facts in `opcodes.toml` (§7.1) |
| Tests | `ProjectBoundaryTests` (the two seams; only `Ome.Scripting.Lua` references the library), `SceneReloadTests` (the new owner), the list of waits (§10) |

## 15. Untrusted code

Packages are untrusted (mod loader spec §11). For Lua:

- source only: no bytecode, no native modules, no .NET access;
- nothing reaches outside the game: no files, network, processes or clock;
- bounded: file count and size, instructions per call, nesting depth, string length, state size;
- not bounded by the instruction budget: a library function that loops inside one call, such as a pattern that
  backtracks. The spike reports what the chosen library can bound (§5.1). What it cannot bound can stall the
  game;
- a managed interpreter has no hard memory cap, so a mod can still exhaust memory;
- no mod can change what another mod sees: the library tables, the string metatable, or what another mod
  exports (§5.2). So a mod cannot take a capability it did not declare;
- a mod with `vm` can corrupt game state and the saves made afterwards. A mod without it can still write the
  names it lists. The link report shows both, so a player can see what each mod may change.

The roadmap's wording stands: this is a capability boundary, not a perfect security sandbox.

## 16. Future work

- **Lua that waits.** Scenes, dialogue and choices written in Lua need a continuation a save can store, or a rule
  that saving is unavailable while one runs.
- **Queued script calls.** Letting a handler ask for an interactive script to run after it returns. The engine
  has the mechanism now: a native records a request, and the interpreter takes it at a boundary or inside a wait
  (engine layering spec §5.4), as it takes the callback scripts. The call must still look to a save exactly like
  the script's own `call-script`.
- **Calling Lua from bytecode.** An extension opcode (OMS §9.4) so an `extended` script can call a function.
- **Engine services:** `maid.ui`, `maid.audio`, `maid.assets`. Each would call the native that owns the
  behaviour, as the options panel and the driver do.
- **Profile state and mod settings:** state that is not per save, and a settings file with a schema.
- **Write groups.** One entry in a write list that stands for many names, for a mod that rebalances a whole
  area. The registries' categories are too coarse for it today: `data-table` covers 308 of Himegari's 459
  entries. A group's name would also become part of the base mod's API, to be kept stable as the names are
  (§9.1).
- **Pattern selectors.** Hooks resolved from an instruction pattern to a set of sites at link (roadmap §3.4).
- **Hooks on another mod's code,** through exported site identities.
- **A debugger:** breakpoints and stepping over the driver channel, and read watches.
- **Outside capabilities:** file or network access, only with the user's approval.

## 17. Validation gates

Every gate except the driven ones runs in `Ome.Engine.Tests`, without Godot, as the natives' own tests do.

| Gate | Passes when |
|---|---|
| 1. Off means off | with no Lua mod enabled and the console unused, no runtime is created, every existing gate passes, and a corpus sweep is no slower within noise |
| 2. Runtime | the chosen library meets the eleven criteria in §5.1 |
| 3. Link | a syntax error, a missing handler, a handler that is not a top-level function of its file, an undeclared event, a wrong fingerprint, a hook on a patched-away site, and a hook that declares `skip` on a jump, a wait or a save each stop the load with a report naming the mods; no mod code has run by then |
| 4. Point | a Lua handler on `himegari.experience.after_calculation` and the guide's OMS patch produce the same decoded save (`Ome.Cli save-diff`) |
| 5. Events | two mods on one event run in the reported order, and the trace shows the value after each; an event, a point hook and a site hook on one instruction run in the one order the report gives |
| 6. Watches | a watch fires once per write, after the instruction and its `after` hooks; an inner instruction's delivery leaves the outer instruction's records for the outer one's return; a load's replacement of the banks and a return to title's zeroing fire none; a console write is delivered with the next instruction's records; a watch on a name works in a mod without `vm`, and a watch on an address is a link error there |
| 7. State | state survives save and load; a removed and re-added mod gets its state back, also when it was the last Lua mod and a save was made without it; `SAVEnn.DAT` is identical with and without a mod that only uses `maid.state`; a state file that does not match its save is reported; a missing state file beside a save whose stamp names a Lua mod is reported; a mod with a function in its state does not fail the save: the save is written, the file keeps that mod's last good block, and the error names the mod; the keys `1` and `"1"` come back as two keys |
| 8. Session | a value in a Lua local is gone after a load and after a return to title; at the start of a run, after a return to title and after a load, the order is the build, `ome.session.start`, the first frame's `enter`, then the first instruction's `before` hooks; a restored frame fires `enter` with the cause `restore`; `exit` fires for a return, and not for a frame that a return to title ends; `maid.session.restoring` is set through the prologue and clear at `ome.session.resumed`; a hook inside the repeated range fires again after `ome.session.resumed`; a watch that honours the flag and keeps a total from a cell written outside the repeated range has the same total after save, load and resume, and one on a cell written inside it counts that write twice; a synthetic script shows the rebuild after op `0x19f`, which neither game uses, and that the op's own `after` hook does not fire; hooks around a wait behave as §7.1 says |
| 9. Calls | a wait-free script called from a handler works, and its frame has the cause `mod`; one that waits, saves or loads fails with the opcode named, in a headless run and in the port, and `omp check` reports it first; a script outside `calls` is refused; a script error inside a call fails it with the error's message; a call refused inside a statement block leaves the block as it was when the call started; an op `0xae` in a script called during a restore fails the call, and the restore completes; a call from `init.lua` or from an `ome.session.start` handler is refused; a return to title, a stop and an exit request inside a call each abandon the handler, with a `pcall` around the call, and reach their outcome |
| 10. Errors | a runaway handler is stopped; unbounded recursion ends as an error; an error names mod, handler, script and offset; a mod whose `init.lua` fails at a load is off for that session, and its state block is in the next save unchanged; `--lua-errors halt` ends the run |
| 11. Randomness | a driven scenario with a mod that uses `math.random` under `rand-seed`, inside a `pairs` loop too, ends in the same state twice; without a seed the draws come from the system's generator, so two loads of one save need not draw alike; `math.randomseed` is absent |
| 12. Capability | `maid.vm` is absent in a mod that did not declare it, and a point or site hook from such a mod is a link error; a mod cannot change a library function for another mod, nor the string methods, nor a table another mod exports, with `rawset` either; a console chunk runs in the console's environment and, given a mod's id, in that mod's |
| 13. Second game | the Kamidori base mod builds from its registry and one event works, with no engine change; a registry rename with no alias and no new major version fails `validate.py` |
| 14. Layers | `Ome.Engine` and `Ome.Engine.Vm` reference no Lua library; `ProjectBoundaryTests` and `SceneReloadTests` pass with the scripting native in them |
| 15. Declared writes | a mod without `vm` reads any exported name; it writes a name it lists, and a write to a name it does not list is an error that names the list; a listed name that the base mod does not export is a link error; an event's `rw` field needs no entry; a write that a base mod's helper makes needs no entry in the calling mod's list, and the trace names the calling mod; the link report lists each mod's writes, and the ordered interaction of two mods on one name; `omp check` reports a written-out write that the list lacks, and `omp build` refuses to pack it; under `--lua-dev` an unpacked mod's undeclared write goes through and is reported |

## 18. Milestones

0. **Spike.** Choose the library against §5.1 and measure dispatch cost. All eleven criteria are checked here,
   those for script calls too, because the library is chosen once.
1. **Core.** The scripting native and the runtime seam in their layers (§14), with the seam's check; session
   life with `ome.session.start`; the sandbox, with each mod's own library tables and read-only exports; `lua/`,
   `[lua]`, point hooks, `maid.vm` without `call`, `maid.log`, errors and budgets, the driver's `lua` step. The
   step has `maid.vm` only until milestone 3 brings the layout its accessors need.
   Gates 1–4, 10, 12, 14.
2. **State.** `maid.state`, the sidecar and the store's change, session rebuild on load with
   `maid.session.restoring`, `ome.session.resumed` and `ome.save.writing`. Gates 7, 8.
3. **Events and the first base mod.** `events/`, typed fields, `maid.events.emit`, the generated layout and
   points, the list of exported names, the accessors with the write lists, the write scan and `--lua-dev`, the
   trace, editor stubs. Gates 5, 13, 15.

**The first release is milestones 0 to 3** (decided 2026-11-14), with the guide for mod authors.

4. **Wider hooks.** Raw sites with `after`, `skip`, script entry and exit, watches, and the two opcode facts
   (§7.1). Gate 6, and the held cases below.
5. **Script calls.** The synchronous call, the static check, and the base mods' helpers that call scripts.
   Gate 9.
6. **Authoring.** Reload and the seeded random streams. Gate 11.

Four of the first release's gates have cases that name a later hook form. Those cases wait for milestone 4:

| Gate | Held for milestone 4 |
|---|---|
| 3 | a hook that declares `skip` on a jump, a wait or a save |
| 5 | the site hook in the merged order |
| 8 | `enter` for a restored frame; `exit`; the two cases with a watch; hooks around a wait |
| 12 | the site hook |

Until a form is built, gate 3 has the opposite case for it: the link refuses the form by name (§6.3).

One case of gate 10 waits inside the first release: a build that fails at a load needs milestone 2.

## 19. Decisions

No question is open. Each decision names the alternative it rejected. These five were decided 2026-11-14:

1. **The library is a managed one,** and the spike picks which (§5.1). The alternative was native Lua, for Lua
   5.4, with its extra packaging work. If no managed candidate passes the spike, this decision is open again.
2. **The session is rebuilt on a load** (decision 6). The alternative kept one Lua state for the whole process
   and trusted mods to reset themselves. The engine rebuilds every owner's scene state on a return to title,
   which is the same rule.
3. **Base mods live in `mods/<profile>-base/`,** with their events. Patch points keep `vm-map/` as their one
   source, and the base mod carries a generated copy, which also makes it work in an exported port (§9.1). The
   alternative moved the points into the base mod, as the loader spec anticipated.
4. **A handler error is reported and the game continues** for players; the driver and tests halt (§5.5). The
   alternative halted for everyone.
5. **A mod without `vm` may not hook a point** (§6.3). A point needs the capability, which any mod may declare,
   and the link report then marks that mod opaque. So every point useful to ordinary mods becomes an event in
   the base mod: more work for the base mods, and a steadier surface for mods.

**Also decided 2026-11-14:**

- **A mod lists the names it writes** (§9.3). Decided by the user. The alternatives were read-only accessors,
  and accessors that write freely.
- **The rest:**
  - **A handler is a top-level function, and a build that fails switches the mod off** (§5.5, §6.2). The
    alternative kept the lookup in the table a file returns and checked it at the build.
  - **The session is built between frames** (§5.7).
  - **Mods share nothing one of them can change** (decision 11, §5.2).
  - **The engine builds the accessors** (§9.3). The alternative was a base mod that exports a constructor,
    which the runtime calls once for each requiring mod.
  - **`omp check` and `--lua-dev` keep the write list light** (§9.3). A group of names is future work (§16).
  - **A helper's writes are the base mod's** (§9.3).
  - **A watch on a name needs no `vm`** (§6.3). Points stay as decision 5 says.
  - **Op `0xae` is refused in a call during a restore** (§10). The simpler alternative refused every call while
    a restore is under way.
  - **A mod's bad state never fails the game's save** (§11).
  - **Smaller choices:** a write to a watched cell from the console or from the session's build is delivered
    with the next instruction's records (§7.3); watch records are delivered after the instruction's `after` hooks
    (§7.4); a hook declares in its manifest entry that it may `skip` (§7.1); and `after` fires on a jump as on
    any instruction that stays in its frame (§7.1).

**Settled earlier:**

- **The first release holds milestones 0 to 3** (§1.2, §18). Decided 2026-11-14. The alternative shipped all of
  version 1 at once.
- **Manifest version 3** (§6.2). Decided 2026-11-14. The alternative kept Lua's tables in version 2.
- **`enter` fires for a restored frame** (§7.2). Decided 2026-11-14. The alternative hid those entries from mods.
- **Shared library code.** Decided 2026-11-10: a package may target no profile. The localization design built the
  rule, and §6.4 now says what such a package may hold for Lua.
- **The site table** hooks need exists (§7.6).
- **What a load runs** (§11), **where the engine waits** (§10), **how a request reaches the engine during a
  wait** (§13) and **how a save stores a string cell** (§8.1) are read from the code.

# Lua scripting — implementation plan

**Date:** 2026-11-14
**Spec:** `docs/superpowers/specs/2026-11-10-lua-scripting-design.md` (cited as "spec §n"). The spec stays the
design's home. This plan keeps what each milestone measured and what that settles for the later ones.
**Status:** milestones 0 and 1 are done (2026-11-14). Milestone 0, the spike, chose **Lua-CSharp 0.5.8**
(`LuaCSharp` on NuGet); milestone 1 built the core (§4). Milestones 2 to 6 are not built. The backlog's "Runtime
scripting (Lua)" item lists them.

Paths are relative to `age-reimpl/`.

## 1. Milestone 0: the spike

`engine/Ome.Scripting.Lua.Spike/` is a console program, not in `OmeEngine.sln` and not shipped. It holds one case
or more per criterion of spec §5.1 for each library:

- `LuaCSharpCases.cs` and `MoonSharpCases.cs` hold the cases. Each one builds what the spec describes: a mod
  environment with its own library copies, a locked string metatable, read-only views of an export as userdata, and
  accessors over a base mod's layout. A case returns a verdict and its measurements.
- `Spike.cs` runs each case in its own child process, so a .NET stack overflow or a hang is a failed case and does
  not end the run. A case runs on a thread with a 1 MiB stack. `SPIKE_STACK_KB` changes that size, and
  `SPIKE_THREAD=pool` runs the case on a thread-pool thread instead, as the port's engine thread is.
- `SpikeLayout.cs` reads Kamidori's generated registry (`build/games/kamidori/globals.json`) as a stand-in for a base
  mod's layout, and each package's nuspec for its licence.

Run it from `age-reimpl/`:

```
dotnet build -c Release engine/Ome.Scripting.Lua.Spike
dotnet engine/Ome.Scripting.Lua.Spike/bin/Release/net8.0/Ome.Scripting.Lua.Spike.dll run [--lib luacsharp|moonsharp] [--case PREFIX] [--out build/lua-spike/results.json]
```

`list` names the cases. `exceptions` and `limits` print what Lua-CSharp's API offers for criteria 2, 3 and spec
§15. A full run takes about four minutes; one MoonSharp case hangs until the runner kills it at 120 s.

The candidates were the newest releases on NuGet on 2026-11-14: Lua-CSharp 0.5.8 and MoonSharp 2.0.0. MoonSharp's
3.0.0-beta.1 was not tested. The project would not ship a pre-release, and Lua-CSharp passed.

## 2. Results

Measured 2026-11-14 on the development machine (Windows 10, .NET 8, Release build). Times are steady state: the
timer warms each body for 500 ms, past tiered compilation, and keeps the best of several rounds. The first
calls in a process run several times slower.

| § 5.1 | Lua-CSharp 0.5.8 | MoonSharp 2.0.0 |
|---|---|---|
| 1. re-entry | pass: Lua → C# → Lua twice; 4,288 B of C# stack per level | pass; 1,008 B per level |
| 2. unwind | **pass when the callback cancels the call's token** (§3.1): every scenario ends with `LuaCanceledException`, and no Lua runs after the callback. A thrown exception does not pass: `pcall`, `xpcall`, coroutines, `__index`, `table.sort`, `__tostring` and `gsub` all catch it. A thrown subclass of `OperationCanceledException` passes all but `xpcall` | fail: `coroutine.resume` turns it into `InvalidOperationException`, `table.sort` into `IndexOutOfRangeException`, and `__tostring` catches it |
| 3. budget | **pass when the hook cancels the call's token**: all 11 scenarios stop (a loop, inside `pcall`, `xpcall`, a coroutine, `coroutine.resume`, `table.sort`'s comparison, `__index`, `gsub`'s callback, and Lua called back from C#), within 4–80 ms of a 2,000,000-instruction budget. The state then runs plain calls, its `pcall` still catches an ordinary error, and the next budget stops the handler again. A hook that throws stops a plain loop only | fail: the auto-yield counter stops loops and `pcall`, not a coroutine inside the handler |
| 4. sandbox | pass: the removed globals are gone, `require` resolves only what the engine gives, once per name, and `print` is the engine's | pass |
| 5. isolation | pass: library copies, a locked string metatable (`getmetatable("")` cannot be changed), and views as userdata. None of six writes into a view succeeds, `rawset` included; reads, `pairs`, `ipairs` and `#` work through it | pass |
| 6. dispatch | pass: a trivial handler 46 ns, 0 B, on the stack API (87 ns with argument arrays); `on_award(e)` reading and writing a userdata field 150 ns, 0 B. A live token and the count hook add nothing measurable (46 ns; 153 ns) | pass: 42 ns, 256 B; 119 ns, 656 B |
| 7. synchronous | pass: in 100,000 calls none completed asynchronously and no callback ran on another thread; a Lua → C# call costs 30 ns | pass: the API is synchronous; 69 ns |
| 8. session build | pass: a base mod and five dependents (environments, library copies, views, accessors over Kamidori's 622 names, each running an `init.lua`): 26.3 ms the first time in a process, then 0.24 ms (median of 40; max 0.63 ms). Reading the layout once took 52 ms | pass: 25.0 ms, then 0.48 ms |
| 9. licence | pass: MIT. Its dependencies are the same authors' `LuaCSharp.Annotations` (MIT) and `LuaCSharp.SourceGenerator` (MIT, build time only) | partial: the package gives only a licence URL |
| 10. recursion | pass: plain recursion, `pcall`, `__index`, `table.sort`, coroutines, `gsub`, `__tostring` and Lua → C# → Lua each end as `LuaRuntimeException` "stack overflow", and the state works after | fail: three forms throw `IndexOutOfRangeException` from the library, `__index` hangs, and four end the process with a .NET stack overflow (0xC00000FD) |
| 11. pairs order | pass: the same order in two processes whose .NET string hashes differed | pass |

Further reports:

- **A library function that loops inside one call.** A backtracking `string.find` (28 `a*` against 28 a's) ran past
  10 s in Lua-CSharp. Neither the hook nor a token cancelled after 1 s stopped it. MoonSharp ended it in 5 ms with
  an error.
- **What else Lua-CSharp can bound.** Its API has `MaxCallDepth`, the hook and the token, and nothing for memory or
  string length.
- **What the hook costs.** Over three runs, a loop of 10,000,000 iterations took 48–50 ms with no hook, 50–55 ms
  with a count hook every 1,000 instructions, and 55–65 ms with one every 100. The library counted 110 million
  instructions for it, 2.5–2.8 ns each. A body with a table, a call and a concatenation costs 7 ns an instruction.
- **The check.** Lua-CSharp's parser is public (`Lua.CodeAnalysis.Syntax.LuaSyntaxTree.Parse`). It found a file's
  top-level global functions and the writes written out as `game.g.NAME = …` and `game.table("NAME")[…] = …`,
  without running the file. The first parse in a process took 11.6 ms, and a syntax error comes with its line
  and column. MoonSharp has no public syntax tree.
- **Strings.** Lua-CSharp's strings are .NET strings, counted in UTF-16 code units: `#"日本語"` is 3 (9 in
  reference Lua), `string.byte("日")` is 26085, and `("日本"):sub(1, 1)` is `"日"`. A VM string reaches Lua
  unchanged.
- **Stack.** Lua-CSharp's recursion guard measures the stack that is left, so the depth it allows follows the
  thread: Lua → C# → Lua reached 31 levels on 256 KiB, 220 on 1 MiB and 1,984 on 8 MiB, at 4,160 B a level. On a
  thread-pool thread under `dotnet.exe`, whose executable reserves 1.5 MiB a thread, it reached 345: a pool thread
  takes the host executable's size. Godot's Windows executables reserve 8 MiB.

## 3. The choice: Lua-CSharp

MoonSharp fails criterion 10 in every form: a mod's unbounded recursion can end the process or hang it, and nothing
the engine does around the library prevents it. It also fails parts of 2 and 3.

Lua-CSharp passes all eleven criteria. It passes criteria 2 and 3 only one way: an unwind and a budget stop must
cancel the token of the call, not throw. The library then raises `LuaCanceledException` at once, and no Lua frame
catches it. These points bind milestone 1.

### 3.1 How the runtime calls Lua

1. **Every call into Lua carries a token.** The dispatch gives the outermost call the session's
   `CancellationTokenSource` token. A source that was cancelled is replaced; one that was not is reused
   (`TryReset`). A callback that calls Lua again passes the token it received.
2. **An engine unwind cancels the token.** Every engine callback runs inside one wrapper. When the engine throws
   inside it (a return to title, a stop, a halt), the wrapper keeps the exception, cancels the token and returns.
   The outermost dispatch catches the `LuaCanceledException` and rethrows the kept exception with its stack
   (`ExceptionDispatchInfo`).
3. **A budget stop cancels the token too.** The count hook runs every 1,000 instructions, adds to the call's count,
   and past the budget marks the stop as the budget's and cancels the token. The dispatch reports a budget error,
   and the state keeps working (spec §5.5).
4. **A callback throws only Lua errors.** `LuaRuntimeException` is a Lua error, which `pcall` may catch, for a bad
   argument and the like. The wrapper carries anything else as in point 2.
5. **Every call completes synchronously.** The library's API returns `ValueTask`s. With synchronous callbacks they
   complete in place; the runtime treats one that does not as a fault.

### 3.2 Budgets to start from

The spec sets the budgets from these measurements (spec §5.5, §7.5). Milestone 1 starts from:

- **The hook** every 1,000 instructions: at most 15 % on a tight loop, and a stop lands within 1,000 instructions
  of the budget.
- **A handler call**: 1,000,000 instructions, 3–7 ms at the measured speed. **A mod's start-up code**: 10,000,000.
- **Dispatch**: about 50 ns for a trivial handler and 150 ns for one that reads and writes an event field, with no
  allocation. Those are the recorded per-call budgets of spec §7.5; milestone 1 measures them again through the
  seam.
- **The session build**: under 1 ms once the library is warm. The first build in a process costs about 25 ms of JIT,
  and reading Kamidori's layout costs 52 ms, so the runtime reads each layout once for the run, not at each build.

### 3.3 Nesting depth

The library ends recursion inside Lua as an error at any stack size, so the cap of spec §5.5 guards only the
engine's own frames between two levels. In the first release a level is a handler that raises an event (spec §8.2):
Lua → C# → Lua, 4,160 B of the library's. Milestone 1 starts from a cap of 32 levels, about 130 KiB, far below a
1.5 MiB stack. Milestone 5 measures what a script call adds and sets the cap again.

The engine thread is a thread-pool thread (`godot/Main.cs`, `Task.Run`), so its stack is whatever the host
executable gives: 8 MiB under Godot on Windows, and not measured on Linux. Milestone 1 gives the engine thread an
explicit stack size, so the cap does not depend on the host.

### 3.4 What the spec's other milestones take from it

- **The check** (spec §5.5, §14.2) parses with the library's own parser; no parser of our own is needed.
  Milestone 1 found that parser refuses valid Lua, so the check compiles and scans instead (§4.2).
- **Notices.** The spike ships nothing. Milestone 1's `Ome.Scripting.Lua` ships `LuaCSharp` and
  `LuaCSharp.Annotations`, so it adds them to `THIRD_PARTY_NOTICES.md`. The packaging check
  (`tools/package_notices.py`) fails until it does.
- **Strings count UTF-16 code units.** The guide for mod authors says so beside "Lua 5.2" (`docs/modding/lua.md`,
  begun in milestone 1).
- **What the budget cannot bound** (spec §15): a backtracking pattern in `string.find`, `match`, `gmatch` or `gsub`
  stalls the game, and nothing in the library stops it. Memory and string length have no limit in the library
  either. **Accepted 2026-11-14** (user): the runtime does not replace the pattern functions with a counted
  matcher. At worst a player finds the mod by switching mods off and back on.

## 4. Milestone 1: the core

Built 2026-11-14 on `feature/lua-scripting`. What it holds, by layer (spec §14.1):

- **The seam** (`engine/Ome.Engine/Modding/`): `IModRuntime`, `IModSession`, the stack and its sources, the hook
  plan and each script's table, the hook context, and `IScriptAccess`, one checked face per mod.
- **The scripting native** (`engine/Ome.Engine/Natives/Scripting*.cs`): the session in its `Scene`, the handler
  calls, the nesting cap, the error policy, `diagnostics/lua.log`, the notices and the console queue.
- **The interpreter's side** (`engine/Ome.Engine.Vm/VirtualMachine.Scripting.cs`): the frames, locals and operands.
  The loop tests the frame's table, one reference, per instruction.
- **The runtime** (`engine/Ome.Scripting.Lua/`): the check, the session, the sandbox, the module loader, `maid`, the
  calling rules of §3.1 and the budgets of §3.2.
- **The link** (`engine/Ome.Mods/`): manifest format version 3 (`[lua]`, `[[hooks]]`, the `extended` target),
  `lua/` in the layout, `ModLuaLinker` (each file checked, each hook bound, the plan in the merged order), and the
  sources and plan in the link cache.
- **The front end:** the port's `--lua-errors`, its `[lua]` startup line and notices in the status line, the
  drive protocol's `lua` op and the driver's `lua` step. The engine thread is now a `Thread` with an 8 MiB stack
  (§3.3), not a pool thread.

Modders' guide: `docs/modding/lua.md`. The example `oms/examples/mods/example.double-exp-lua` is gate 4's mod.

### 4.1 Hexadecimal numerals

Lua-CSharp 0.5.8's lexer refuses a hexadecimal integer numeral that an operator, `]` or `;` follows directly:
`t[0x100]`, `0x10+1`, `0x10==16`, `0x10;` and `0x10--c` fail with "malformed number". It reads `)`, `}`, `,`, white
space, the end of the chunk, decimal numerals and hex numerals with a fraction or an exponent as Lua does. Every
chunk therefore passes through `LuaSource.Prepare`, which puts a space after such a numeral, outside strings,
comments and names. It puts none before a letter, digit, `_` or `.`, because Lua reads those as one malformed
numeral. Lines do not move, so an error's line is the mod's.

### 4.2 The syntax tree is not used

`LuaSyntaxTree.Parse`, the public parser the spike found for the check (§3.4), is a separate implementation from the
compiler that loads chunks. In 0.5.8 it refuses valid Lua that the compiler accepts:

| Source | Syntax tree | Compiler |
|---|---|---|
| any comment, `-- x`, `--[[ x ]]`, first in a file or after code | "unexpected symbol near '-'" or a wrong error on the next line | accepts |
| `'a\z` and white space, `b'` | "Unterminated string" | accepts |
| a bare `return` at the end of a chunk | "syntax error near <eof>" | accepts |
| `"\u{48}"` | accepts | refuses (Lua 5.2 has no `\u`) |

The spike's cases had no comments, so it did not see this. The first file with a comment, the example's, failed to
link. The check (`LuaModRuntime.Check`) now compiles the file with the compiler a session uses, which runs nothing,
and reports its errors with their line. The compiler gives no column, as reference Lua does not. The handlers and
the written names come from `LuaSourceScan`, a token scan of our own. It finds `function NAME(` at block depth 0,
not after `local`, and the write forms of spec §9.3 as assignment targets. It reads only text the compiler accepted,
so it needs Lua 5.2's tokens and block keywords and nothing more. This replaces §3.4's "no parser of our own is
needed".

### 4.3 Error text

The library's exception messages begin with "Lua-CSharp: ". Its error object holds the value passed to `error`,
without the position that Lua 5.2's `error` adds to a string or a number; the message has it. The runtime reports
the message without the library's name, so `error('boom')` in the console reads `[string "console"]:1: boom` and
then the traceback. A table thrown as an error reads "(error object is a table value)". The check's errors also
drop the compiler's `[string "CHUNK"]:LINE:`, because the report gives the file and the line.

### 4.4 The seam as built

Spec §14.2 gives the seam's shape. As built (`engine/Ome.Engine/Modding/ModRuntime.cs`):

- `IModRuntime.Identity` names the library and the runtime build, for the link cache key; a stack without Lua keeps
  its old key. `BuildSession` takes `ModLimits` (the budgets and the nesting cap) besides the spec's parameters.
- `IModSession` adds `Failures` (the mods the build switched off), `IsActive(handler)` (the handler's mod is on and
  its function exists once its file ran) and `RunChunk` (the console).
- `LazyModRuntime` creates the runtime at its first use, so gate 1 holds: the composition hands one to the link and
  the scripting native, and a run without Lua and without the console creates none.
- `maid.vm.gp` and `maid.vm.gsp` read the pointer cells, which spec §8.1 says are readable and not writable.
- `vm.frames()`, `vm.script_id()` and `vm.call()` are built once per view and bound to it, so the dot form the spec
  writes works, and the colon form too.

### 4.5 The driver step

Spec §13 writes `lua [MOD] CODE`. As built it is `lua [mod=ID] [timeout=S] CODE`: a bare first word cannot be told
from code (`lua x = 1`). The step takes the rest of the line as the chunk, `#` included. The request only hands the
chunk over (`InputAndWaits.Invoke`); the scripting native runs it at the next instruction boundary, or inside the
wait the engine sits in (`InputAndWaits.ServiceAfterRequests`), and the dispatcher polls for the result once a frame.

### 4.6 Gates

| Gate | How it passes |
|---|---|
| 1. Off means off | `OffMeansOff_NoLuaModCreatesNoRuntime`; the port without Lua mods prints no `[lua]` line and creates no runtime until the console's first use; the suite and core validation pass; the corpus sweep (`Ome.Cli sweep`, Release, 10 runs of each build alternating) took a median 1.627 s before the milestone and 1.611 s after, with the same results |
| 2. Runtime | milestone 0 (§2) |
| 3. Link | `ModLuaLinkTests`: a syntax error, a missing handler, four forms that are not a top-level function, a point that is not a patch point, a wrong fingerprint, a hook on an instruction a patch replaced, and a script another mod replaced each stop the load naming the mods, with no mod code run. Each form not built yet is refused by name, as spec §6.3 says. The `skip` case waits for milestone 4 |
| 4. Point | `ALuaHandlerOnTheExperiencePointDoesWhatTheGuidesPatchDoes`: `ADDEXP.BIN` awards 7 headlessly, once with the guide's OMS patch and once with the example's handler, and the sessions' banks are equal (progress 14; 7 unmodded). A headless run writes no save, so the banks, which a numbered save's global sections are written from, stand in for `Ome.Cli save-diff` |
| 10. Errors | runaway handler, unbounded recursion, an error naming mod, handler, script and offset, `--lua-errors halt`, three failures switching a handler off, a failing `init.lua` switching its mod and the mods requiring it off. A build that fails at a load waits for milestone 2, as spec §18 says |
| 12. Capability | `maid.vm` absent without `vm`, and a point hook a link error there; library tables, the string metatable and exports unchangeable for another mod, `rawset` too; the console's environment and a named mod's |
| 14. Layers | `OnlyTheLuaProjectNamesTheLuaLibrary`; `ProjectBoundaryTests` lists `IModRuntime` and `IModSession` as seams; `SceneReloadTests` covers the scripting native |

Driven in the port (2026-11-14) with a mod holding an `ome.session.start` handler and a point hook: the session
started at boot and the handler logged to `lua.log`; at the title and on SC0000's first pages the console read cells,
locals and the frame stack, wrote a cell, kept a console global between chunks, ran in the mod's environment by
`mod=`, and reported an error with its traceback; a return to title rebuilt the session (the console's global gone,
the mod's counter back to 1, a second `ome.session.start`). Without mods the console started the runtime on its
first use. `omp check` passed the mod and reported a broken one's syntax error and unknown point together; `omp
export` refused it.

The engine suite (2143 tests) and core validation pass.

### 4.7 Measured through the seam

Dispatch on the development machine (Release; 200,000 instructions, each one hooked, best of 30 runs):

| Case | Per instruction |
|---|---|
| no scripting native | 21.5–22.9 ns, 0 B |
| a Lua mod, the instruction not hooked | 22.0–25.9 ns, 0 B |
| a trivial handler | about 85 ns more, 136 B |
| a handler that reads and writes `maid.vm.g[addr]` | about 460 ns more, 136 B |

- The trivial handler's 136 B are the context and its view. They are new for each call, so a `ctx` that a handler
  keeps is refused once its call returns (`AContextIsLiveOnlyDuringItsCall`).
- The spike measured 46 ns for the bare call. The rest is the engine's own work around it: the context, the
  handler's switch-off check, the nesting count and the budget's reset.
- On the way, three costs were removed from every dispatch: a formatted "where" string that only an error needs, a
  closure and delegate per call, and an enumerator per hooked instruction. Together they were 248 B.
- The cell handler makes six metamethod calls (`maid.vm.g` twice, the read, the write). A handler that keeps
  `local g = maid.vm.g` makes two.
- A script's table holds one entry per hooked instruction and is built when a frame first enters the script.

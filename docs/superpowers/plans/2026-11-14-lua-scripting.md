# Lua scripting — implementation plan

**Date:** 2026-11-14
**Spec:** `docs/superpowers/specs/2026-11-10-lua-scripting-design.md` (cited as "spec §n"). The spec stays the
design's home. This plan keeps what each milestone measured and what that settles for the later ones.
**Status:** milestones 0 to 2 are done (2026-11-14). Milestone 0, the spike, chose **Lua-CSharp 0.5.8**
(`LuaCSharp` on NuGet); milestone 1 built the core (§4). §5 measures the library against the official Lua 5.2
test suite. Milestone 2 built mod state and the session's life across loads (§6). Milestone 3 is being built (§7);
milestones 4 to 6 are not.
The backlog's "Runtime scripting (Lua)" item lists them.

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

## 5. Library conformance

Milestone 1 found two library bugs by accident (§4.1, §4.2). The spike's criteria test what the engine needs from a
library, not the Lua language, so this measures the language. Run 2026-11-14, after milestone 1.

### 5.1 How it was run

- **The suite.** The official Lua 5.2 test suite, `lua-5.2.2-tests.tar.gz` from https://www.lua.org/tests/ (SHA-256
  `07c1071c66d9cb4dc80200ed430fc21ebbbccf1f55148b87eb0b9522932e4eb6`), the newest one for 5.2. It is unpacked into
  the disposable `build/lua-conformance/official/` and not committed.
- **The runner.** The spike's `conformance` command runs each file unmodified, read as Latin-1 (one character per
  byte, as reference Lua reads it). Each file gets the full standard library, not a mod's sandbox, with `_port` and
  `_soft` set as `all.lua` allows. Each run is a child process on an 8 MiB thread with a 30 s limit. A file that
  hangs runs again with a count hook that reports the line it is on. Three modes:
  - **strict:** the file as written; the first failed assert ends it;
  - **soft:** `assert` records a failure and returns, so a run lists every failed assert up to the first other
    error;
  - **past:** soft, plus shims and line patches that step over a stop once it is recorded, so the rest of the file
    runs (`Conformance.PastShims` and `PastPatches`, each with its reason). None hides a failed assert.

  A file that `LuaSource.Prepare` changes runs raw and prepared.
- **The probe.** The `conformance-probe` command runs what the suite leaves out:
  - 146 chunks with the result Lua 5.2 gives: numerals, escapes, comments, precedence, number text, `tonumber`,
    `string.format`, tables, assignment order, control flow, `bit32`, and what 5.2 does not have;
  - 234 numerals followed directly by an operator or a bracket, each compared with the same expression spaced out.

  Each chunk runs on its own, so one failure hides nothing.
- **The libraries.**
  - Lua-CSharp 0.5.8 and MoonSharp 2.0.0, as the spike tested them.
  - Lua-CSharp's `main` at `0fb05ff` (2026-11-12), built from source for net8.0 with SDK 8 (its `global.json` asks
    for SDK 10). It ran with `LuaPlatform.NumberFormat = Lua`, an option added after 0.5.8.
- **Upstream's own run.** Upstream runs the same suite in its tests, on a copy with ten files edited. `math.lua` is
  reformatted with spaces around its operators, which hides §4.1's bug (`math.lua:129` is `0xE+1`). Failing asserts
  are commented out. So upstream's passing run did not show conformance.

From `age-reimpl/`, after unpacking the suite:

```
dotnet build -c Release engine/Ome.Scripting.Lua.Spike
dotnet engine/Ome.Scripting.Lua.Spike/bin/Release/net8.0/Ome.Scripting.Lua.Spike.dll conformance [--lib luacsharp|moonsharp] [--file NAME] [--timeout S] [--out PATH]
dotnet engine/Ome.Scripting.Lua.Spike/bin/Release/net8.0/Ome.Scripting.Lua.Spike.dll conformance-probe [--lib L]
dotnet engine/Ome.Scripting.Lua.Spike/bin/Release/net8.0/Ome.Scripting.Lua.Spike.dll conformance-eval CODE
```

- `--tests DIR` defaults to `build/lua-conformance/official/lua-5.2.2-tests`, and `--out` to
  `build/lua-conformance/results.json`.
- `CONFORMANCE_NUMBERS=lua` sets `NumberFormat` by reflection, on a library that has it.
- To test another build of the library, copy the output folder and replace `Lua.dll` and `Lua.Annotations.dll`.

### 5.2 Results

Nineteen of the suite's files test what a mod's sandbox has. The other three (`gc.lua`, `db.lua`, `files.lua`) test
`collectgarbage`, `debug` and `io`, which a mod does not get.

| Files | Lua-CSharp 0.5.8 | `main` | MoonSharp 2.0.0 |
|---|---|---|---|
| `constructs`, `literals`, `locals`, `goto`, `vararg`, `events`, `sort`, `pm`, `bitwise`, `big`, `verybig` | pass | pass | 3 of 11 pass (`locals`, `big`, `verybig`) |
| `calls` | stops at 217: `load` with a reader function is not built | pass | stops at 217 |
| `nextvar` | stops at 233: `package.loaded` lacks `package`; past mode passes | pass | an assert at 34 |
| `strings` | asserts at 122 (`%q` of `"\0"`), 151 and 152 (`%x` of a fraction raises); stops at 187 (`%a`) | pass | an assert at 6 |
| `math` | does not compile (§4.1); prepared, asserts at 60, 85, 99, 100, 137 (`tonumber`) | pass | stops at 45 |
| `attrib` | stops at 385 and 395: `i, a[i], a = …` indexes the new `a` | pass | stops at 385 |
| `closure` | hangs at 27, waiting for a weak entry to clear; past, asserts at 45 and 53 (closure identity) and 219-242 (`debug.upvalueid`) | the same, without the `debug` ones | does not compile: a valid `goto` refused |
| `coroutine` | an assert at 209: a weak entry not cleared | the same | stops at 134 |
| `errors` | asserts at 153 (`io.stdin`'s `__gc`) and 180 ("stack overflow" for "C stack overflow") | the same | an assert at 36 |

The probe:

| Library | Chunks (146) | Numerals (234) |
|---|---|---|
| Lua-CSharp 0.5.8, raw | 128 | 186 |
| Lua-CSharp 0.5.8, through `LuaSource.Prepare` (what a mod runs) | 130 | 234 |
| Lua-CSharp `main`, `NumberFormat = Lua` | 146 | 234 |
| Lua-CSharp `main`, default number format | 134 | 234 |
| MoonSharp 2.0.0 | 121 | 213 |

MoonSharp's probe failures include `7 % -3` giving 1 (Lua gives -2), `math.modf(-3.5)` giving -4, `"Z" < "a"`
false, `tonumber("0x10")` nil, and `1e1+2` refused.

### 5.3 What differs for a mod

| Difference | 0.5.8 | `main` | Reaches a mod |
|---|---|---|---|
| A hex integer numeral followed by an operator, `]` or `;` does not compile | yes; `LuaSource.Prepare` works around it (§4.1) | fixed | no |
| **Number text.** `tostring`, `..`, `print`, `%s` and `table.concat` write .NET's shortest round-trip form, not `%.14g`: `1/3` reads `0.3333333333333333`, `0.1 + 0.2` reads `0.30000000000000004`, `1e15` reads `1000000000000000`, `1/0` reads `Infinity`. `%g` writes `1E+20`, and `%e` writes the number unformatted | yes | Lua's form with `NumberFormat = Lua`; .NET's stays the default | text only (§5.5) |
| **`string.format("%d", 2.5)` raises** "number has no integer representation" (Lua 5.3's rule; 5.2 truncates); `%x` the same | yes | fixed | **yes** |
| `%q` writes a NUL character as it is; `%a` is not built | yes | fixed | rarely |
| A multiple assignment that assigns a table variable and indexes it in one statement indexes the new value | yes | fixed | rarely |
| `tonumber` takes an embedded NUL, `0x5p+-2` and `"Infinity"`, and misreads a 150-digit hex numeral | yes | fixed | rarely |
| Weak tables (`__mode`) are never cleared, and `__gc` never runs | yes | yes; upstream documents it: the .NET collector owns lifetimes | rarely: a weak cache keeps its entries, and a mod has no `collectgarbage` |
| Two closures that nothing tells apart are not equal (5.2 reuses one; the manual leaves it open) | yes | yes | no |
| Error wording, such as "stack overflow" for "C stack overflow" | yes | yes | no |
| Outside the sandbox: `load` with a reader, `string.dump`, `package.loaded`, `collectgarbage`'s options, `debug`, `io` | gaps | most fixed | no |

### 5.4 What follows

- **The choice stands (§3).** Lua-CSharp's core passes the suite's language files. Upstream's `main` fixes every
  0.5.8 failure a mod can reach except weak tables. MoonSharp fails more, besides criterion 10.
- **Number text does not hold Lua back** (decided 2026-11-14 by the user, after §5.5). Lua ships on 0.5.8. The
  runtime moves to the release after it when one comes, sets `NumberFormat = Lua`, drops `LuaSource.Prepare` once its
  test passes raw, and repeats this run. The backlog keeps the item.
- **Until then** the guide gives the number rule and weak tables, and the troubleshooting page gives the number text
  and `%d`.

### 5.5 Number text, measured

The question was whether 0.5.8's number text is more than a display difference. Each value below went through
0.5.8's `tostring` and was compared with C's `"%.14g"`, the format Lua 5.2 uses.

| Values | Differ |
|---|---|
| Integers up to 14 digits, so every VM cell (32-bit) | none |
| Integer-valued results of float arithmetic (`10 / 2`, `2^10`, `math.floor(x * 1.1)`) | none |
| Decimals as a mod writes them (`0.1`, `1.5`, `12.34`, `0.001`) | none |
| `x * 1.5`, `x * 0.5`, `x * 1.25` for x in 1..1000 | none: exact in binary |
| `x * 1.1`, `x * 0.9`, `x * 0.3`, `x * 0.01` for x in 1..1000 | 53 %, 20 %, 24 %, 13 %: `3 * 1.1` reads `3.3000000000000003`, where Lua reads `3.3` |
| `a / b` for a, b in 1..100; `x * 100 / y` | 78 %; 85 %: `1/3` reads `0.3333333333333333`, where Lua reads `0.33333333333333` |
| Integers from 10^14; magnitudes below 10^-4 or above 10^15 | all: no exponent below 10^16, an upper-case `E` |
| `inf`, `-inf`, `nan` | all: `Infinity`, `-Infinity`, `NaN` |

What it touches:

- **Only text.** A cell write takes the number, not its text. A table key that is a number stays a number, and a
  key built from an integer (`"slot" .. id`) is the same string. Read back with `tonumber`, .NET's form gives the
  same number for all of 90,000 fractions tested, where Lua's 14 digits give a different one for 92 % of them.
- **Not the machine's locale.** The probe gives the same results under de-DE, fr-FR, ru-RU and ar-SA. MoonSharp does
  not: under de-DE, `"" .. 2.5` reads `2,5`.
- **Not what the engine writes.** `print` and `maid.log` format numbers in our code (`LuaModSession.Show`, .NET's
  round-trip form with the invariant culture), so they agree with `tostring`; the move past 0.5.8 changes it with
  them. The mod state of milestone 2 will be written by our own serializer, which must keep each number exactly.
- **Explicit formatting is the same everywhere.** `string.format("%.Nf", x)` gives C's text on 0.5.8, ties rounded
  to even as C does (`%.0f` of 2.5 reads `2`, `%.2f` of 0.125 reads `0.12`). Only `%e` and `%g` differ in 0.5.8.
- **No workaround inside the engine.** A replaced `tostring` or `print` would not reach `..`, `table.concat` or the
  string functions' coercions, which the library does inside its interpreter, and `tostring(x)` and `"" .. x` would
  then disagree.
- **The move changes noise, not values.** After it, `3 * 1.1` reads `3.3` instead of `3.3000000000000003`. A mod
  that formats explicitly sees no change; one that compares number text with a string literal would, and a number a
  mod keeps as text no longer reads back exactly, as in Lua 5.2.

## 6. Milestone 2: state

Built 2026-11-14 on `feature/lua-scripting`: `maid.state` beside each numbered save, the session a load rebuilds,
`maid.session.restoring`, `ome.session.resumed` and `ome.save.writing` (spec §5.7, §7.7, §8.3, §11). What it holds,
by layer:

- **The seam** (`engine/Ome.Engine/Modding/`): `ModState` holds a `ModStateBlock` per mod (its version and its state
  as JSON text); `IModSession.CaptureState()` returns the blocks and a failure per mod whose state did not serialize.
  `ModLimits` gains the state's bounds: 1,048,576 characters per mod and 64 levels of nesting. The plan's handlers
  carry the engine event they answer (`PlannedHandler.EngineEvent`), so one can sit in an instruction's list.
- **The scripting native** (`engine/Ome.Engine/Natives/Scripting.State.cs`): the save record, the load's report, the
  restore flag, the two events and the last good blocks. Every field is in its `Scene`, so `SceneReloadTests` holds:
  a return to title drops a load not yet taken, the flag and the pending event with the session.
- **The save native** (`SavesAndSettings.Numbered.cs`): `SaveNumbered` takes the time and play time, then the
  scripting native's record, then calls the store; `LoadNumbered` hands the texts read beside the save to the
  scripting native once the load has succeeded.
- **The interpreter** (`engine/Ome.Engine.Vm/`): op `0x19e` ends the run after its save when a state failed under
  `--lua-errors halt`; op `0x19f` ends the session on success; the terminal op `0xae` of a restore ends the restore.
  The loop asks the native for a pending build or event at each boundary (below).
- **The runtime** (`engine/Ome.Scripting.Lua/`): `LuaStateCodec` turns a mod's `maid.state` into the file's form and
  back; the session decodes each mod's block before its `init.lua` and sets `maid.mod.state_version`.
- **The link** (`ModLuaLinker`): `ome.session.resumed` and `ome.save.writing` bind like `ome.session.start`. Only
  `ome.text.present` is still refused by name.
- **The front end** (`godot/Main.Mods.cs`): the port's save store is the stamp store wrapped in a second sidecar
  store for `SAVEnn.modstate.json`, in every run, with or without mods, so a removed last Lua mod's block reaches the
  next save.

Modders' guide: `docs/modding/lua.md`, "State" and "Loads"; the messages are in `docs/modding/troubleshooting.md`.

### 6.1 The store's change

`SidecarNativeDatStore` took one fixed text when it was built, which suits the stamp. It now also takes a text per
save: `INativeDatStore.SaveNumberedFile` has an overload with the sidecars by suffix, where a null text deletes the
file and a missing one leaves the store's fixed text. A load returns each sidecar's text in
`NativeNumberedSaveFile.Sidecars`, read before the save is decoded. A store the port did not wrap ignores the
sidecars, so a headless run without the wrapper writes no state file.

The state's text is made before the store is called (spec §11, "Writing"), so the store's I/O catch never sees a
mod's error. Delete and copy move the file with the save, as they did the stamp.

### 6.2 The file

```json
{
  "modstate": 1,
  "save": { "timestamp": "2026-11-14T21:03:55.120", "play_seconds": 312 },
  "mods": {
    "lua-state-check": { "version": "1.0.0", "state": {"s:last_slot":0,"s:saves":1} }
  }
}
```

- Mods are in ordinal id order, and the state is written as the runtime gave it.
- The state's form (`LuaStateCodec`): an object's keys are marked `s:` or `i:`; a table is an array only when its keys
  are exactly 1 to n, n at least 1, and the empty table is `{}`; integer keys come first in ascending order, then
  string keys in ordinal order; a whole number below 2^53 is written as an integer, any other number, and -0, in
  .NET's round-trip form, so every number reads back exactly.
- Refused, with the path of the value: functions, userdata, threads, cycles, numbers that are not finite, keys that
  are neither strings nor integers, strings with a lone surrogate, nesting past 64 and text past 1,048,576 characters.
  Metatables are not consulted or saved, and a table reached twice is written twice.
- **Matching the save.** The file repeats the save's header time, to the millisecond, and its play time. A load
  compares both with the save. A file that differs, or that cannot be read, is reported and gives every mod empty
  state. With no file, a load reports only when the stamp beside the save names a mod that ships Lua in this stack.

### 6.3 The last good block

The native keeps a block per mod: the ones the session's load gave, replaced by each successful capture. A mod
whose state does not serialize keeps its last good block in the file, the error goes to `lua.log` and a notice, and
under `halt` op `0x19e` ends the run after the save is written. A mod that is off is not captured, and a mod not in
the stack has no session at all, so both blocks are carried unchanged. The native exists in every port run, so this
holds when no mod ships Lua and no runtime is created.

### 6.4 Where the session and the events run

- **A full load** (op `0x1a1`, then `VirtualMachine.Run`): the session is built between frames, before
  `CALLBACK_LOAD.BIN`, with the save's state and `restoring` set. The terminal op `0xae` clears the flag before its
  jump and queues `ome.session.resumed`.
- **A data-only load** (op `0x19f`): the op ends the session when it succeeds, so nothing of the old session runs
  after the banks are replaced, and the next boundary builds the new one with `restoring` clear and no resumed event,
  since no frame is restored.
- **The boundary.** The loop tests one property, `BoundaryPending`, before `ServiceInstructionBoundary()`. When it is
  set, the native builds the pending session and then fires the pending `ome.session.resumed`, so both come before
  the boundary's services and before the hooks of the instruction the game resumes at (decision 3: Lua runs at a
  boundary). The order after a load is then `ome.session.start`, the restore's hooks, `ome.session.resumed`, the
  first resumed instruction's hooks.
- **`ome.save.writing`** is not a separate call. The link's handlers for it are merged into the `before` list of
  each numbered-save instruction (opcode label `save-numbered-slot`, asked through `IInterpreterAccess.IsNumberedSave`)
  by priority and then load order, with the point hooks there. It gets an engine event's context with `slot` read
  from the instruction's slot operand, and no frame access.
- **An abandoned load** (`CALLBACK_LOAD.BIN` returns to title): the reset ends the session with the flag set and
  drops what was pending; the title's session starts clear and empty.

### 6.5 Gates

| Gate | How it passes |
|---|---|
| 7. State | `LuaStateTests`, over real saves in a temporary directory through the store the port composes. State survives save and load (`AFullLoadRebuildsTheSessionWithTheSavedStateAndMarksTheRestore`). A removed and re-added mod gets its state back, as the last Lua mod and beside another (`ARemovedModGetsItsStateBackAfterASaveMadeWithoutIt`). `SAVEnn.DAT`'s payload and history tail are equal with and without a mod that only uses `maid.state`; the header's time differs between any two saves (`TheSaveIsTheSameWithAndWithoutAModThatOnlyUsesState`). A mismatched file is reported (`AStateFileThatDoesNotMatchItsSaveIsReportedAndGivesEmptyState`). A missing file is reported when the stamp names a Lua mod, and not when it names a mod without Lua (`AMissingStateFileIsReportedWhenTheStampNamesAModThatShipsLua`). A function in the state does not fail the save: it is written, the file keeps the last good block, the error names the mod, and `halt` ends the run (`AStateThatCannotBeSavedKeepsTheLastGoodBlockAndTheSaveIsStillWritten`). The keys `1` and `"1"` come back as two keys (the first test and `TheStateFormKeepsKeysTypesAndOrderAndReadsBackWhatItWrote`) |
| 8. Session | A Lua global is gone after a load (the first test) and after a return to title (milestone 1, and `AnAbandonedLoadEndsWithTheFlagSetAndTheTitleSessionStartsClear`). The order is the build, `ome.session.start`, the first instruction's hooks. `maid.session.restoring` is set through `CALLBACK_LOAD` and the prologue and clear at `ome.session.resumed`; a hook in the repeated range fires again after it, and a total that honours the flag is the same after save, load and resume while one in the repeated range counts it twice (the first test, with point hooks where the gate names watches). Op `0x19f`'s rebuild on a synthetic script (`ADataOnlyLoadRebuildsTheSessionAtTheNextBoundary`). `ome.save.writing` in the save instruction's list by priority (`SaveWritingTakesItsPlaceInTheSaveInstructionsListByPriority`) |
| 10. Errors, the load case | `AModWhoseInitFailsAtALoadIsOffAndItsStateBlockIsCarriedUnchanged` |
| 14. Layers | `SceneReloadTests` with the new fields; the codec is in `Ome.Scripting.Lua` |

Held for milestone 4, as spec §18 says: `enter` for a restored frame, `exit`, the watch cases and hooks around a wait.
One case more waits there: that op `0x19f`'s own `after` hook does not fire. There are no `after` hooks yet; the
session ends inside the op, so none could.

### 6.6 Driven in the port

On 2026-11-14, sandbox `luastate`, with a scratch mod (`ome.session.start`, `ome.session.resumed` and
`ome.save.writing` handlers counting saves in `maid.state`) and `--lua-errors halt`:

- a save in slot 0 and its load logged `start boot`, `writing slot 0 saves=1`, `start load restoring=true saves=1
  from=1.0.0`, `resumed slot 0 restoring=false`; `SAVE00.modstate.json` held `{"s:last_slot":0,"s:saves":1}`
  beside the stamp;
- restarted without the mod, slot 0 loaded and slot 1 saved: `SAVE01.modstate.json` carried the block, and slot 1 has
  no stamp;
- restarted with the mod, slot 1 loaded: `saves=1 from=1.0.0` came back;
- with `SAVE00.modstate.json` deleted, slot 0's load reported the missing file in the engine log and a notice, and the
  state started empty.

### 6.7 Measured

Dispatch, as §4.7, against the commit before the milestone (`92c6a70d`) in a second worktree: Release, 200,000
instructions, each one hooked in the hooked case; each case the best of 30 runs, less a one-instruction run of the
same set-up; three passes per process, the first dropped as warm-up; each build run twice, alternating.

| Case | Before | Milestone 2 |
|---|---|---|
| no scripting native | 21.5 ns, 0 B | 21.6–21.7 ns, 0 B |
| a scripting native, no Lua mod (the port's run without Lua mods) | 21.6–21.8 ns, 0 B | 21.8–22.0 ns, 0 B |
| a Lua mod, the instruction not hooked | 21.6–21.8 ns, 0 B | 21.9 ns, 0 B |
| a trivial handler | 101.9–103.3 ns, 175 B | 105.1–109.2 ns, 175 B |

The boundary test adds a fraction of a nanosecond where nothing is hooked. A hooked call costs 2 to 7 ns more; the
cause was not isolated (the dispatch now tests each handler for an engine event). This harness counts allocation differently from §4.7's, so its 175 B
compare only with each other.

The engine suite (2156 tests) and core validation pass.

## 7. Milestone 3: events and base mods

Being built on `feature/lua-scripting`, in seven pieces:

1. the base mod package (§7.1);
2. the accessors `game.g` and `game.table`, `[lua] writes`, `maid.names`, and the base mod in the runtime stack;
3. `events/` with typed fields, `maid.events.emit`, event hooks, and a point linked through the base mod's copy;
4. the write scan and `--lua-dev`;
5. the link report's additions and `--lua-trace`;
6. `omp lua-stubs` (lua-language-server `---@class` annotations);
7. the reverse-engineered Kamidori point `kamidori.experience.after_calculation`, each base mod's experience award
   event (its only event at first), gate 13 and the guide.

### 7.1 The base mod package

Built 2026-11-14 (spec §9.1):

- **The folders.** `mods/himegari-base/` and `mods/kamidori-base/` each hold a hand-written `manifest.toml` (omp 3,
  the game's `[game]`, `[lua] capabilities = ["vm"]`, version 1.0.0) and `README.md`. `layout/` is written by
  `tools/base_mod_build.py --build` from the repository's sources alone, so a base mod builds without an install.
- **`layout/globals.json`** (format 1) has one entry per named global, one per line, ordered by name: its bank (`G`,
  `GF`, `GS`), address, dimensions, columns and aliases. Himegari has 428 globals (93 with columns, 9 with an open
  first dimension), Kamidori 624 (69 and 4).
  - Only the first dimension may be open (`int[][C]`, `int[unit][C]`): the registry does not know its row count.
  - Columns name indices of the last dimension: a 1-D array's elements or a 2-D array's columns. They are Lua names
    and may hold capitals, as Kamidori's asset-named columns do.
- **`layout/points.json`** copies the patch points in `patch-points.json`'s form, validated without the install
  (Himegari has one point, Kamidori none yet). Piece 3 links a point through it when
  `build/games/<profile>/patch-points.json` is absent.
- **`layout/exports.txt`** holds the major version and every exported name; an alias reads `old = new`. The
  checked-in file is the released list:
  - `--check`, a core `validate.py` gate (`base-mods-check`), refuses a name that leaves the list while the
    manifest's major version stays the same, and a major version lower than the file's;
  - a raised major version starts a new list.
- **Aliases** (`globals_build.py`) are `aliases = [...]` on a registry global, as lowercase identifiers.
  - The linter refuses an alias that is another global's name or alias.
  - The reference doc writes them as "formerly".
  - A name and an alias are one name in the layout (`GameLayout.TryGet`).
- **Read at open** (`ModPackage`). A package with `layout/` needs:
  - omp 3, a `[game]` of its own and the capability `vm`;
  - a `layout/globals.json` that parses as that game's layout (`GameLayout.Parse`,
    `engine/Ome.Engine/Modding/GameLayout.cs`), and a `points.json` that parses as patch points.

  Any other file in `layout/` is refused.
- **Shipped** (`ShippedPackages`, `engine/Ome.Mods/Packages/`). `mods/**` is embedded in `Ome.Mods`, so an exported
  build carries the base mods.
  - Discovery (`ModLaunch.Discover`) and `omp build` add each shipped package an enabled mod requires, and those it
    requires in turn, with a note in the link report.
  - A disabled mod's requirement mounts nothing.
  - An installed package of the same id is used instead, with a note.
  - A shipped package's location reads `<engine>/mods/<id>`.

Tests: `BaseModTests` (17; the shipped bytes must equal the repository's folders, so a stale build fails) and the core
`tools/test_base_mod_build.py`. A base mod has no `lua/` yet, so it does not join the runtime's stack; piece 2 adds it.

### 7.2 The accessors and the write lists

Built 2026-11-15 (spec §5.4, §8.4, §9.3, §13):

- **The face** (`engine/Ome.Engine/Modding/ScriptAccess.cs`). `IModAccess.Names(baseModId)` gives an
  `INamedAccess`, the base mod's accessors for that mod, and every rule is the engine's:
  - names and aliases resolve through the layout; a name the base mod does not export is refused;
  - indices pass `LayoutCells`: whole numbers from 0, within the rows, and within the columns of a 2-D array; the
    face checks them again when it reads or writes;
  - values pass `CellRules`, as `maid.vm`'s do, and a string goes only to a string global;
  - a write to a global the mod's list lacks is refused, with the mod, the name and the list.

  A mod that requires the base mod gets its own list. The base mod's own face and the console's have none, and any
  other mod gets null.
- **An open first dimension ends at the next global.** `int[][C]` and `int[unit][C]` have no row count in the
  registry. `GameLayout` bounds each one by the next named global of the same bank, so a mod without `vm` reaches only
  the table's cells. In both games every open table ends exactly at such a global (Himegari's nine at 900 to 52,000
  rows, Kamidori's four at 50 to 226). An open table that nothing follows would be bounded by the last address alone;
  none does.
- **The Lua side** (`engine/Ome.Scripting.Lua/NameViews.cs`) has userdata views only:
  - `require("<profile>-base")` gives `g` and `table(name)` beside a read-only view of what the base mod's
    `init.lua` returned. A base mod whose export uses either name is off for the session, and so are the mods that
    require it.
  - `table(name)` gives elements of a 1-D array, or rows of a 2-D array, by index or column name; `#` gives rows or
    columns.
  - `maid.names` exists in a base mod and in the console.
- **The console** reads the base mod the stack mounts, else the profile's shipped one
  (`ScriptingSetup.ConsoleLayout`, `ShippedPackages.BaseLayoutOf`). It reads it at the console's first use, so a run
  that never uses the console reads no layout. `require("<profile>-base")` works there too.
- **The link** (`ModLuaLinker`) puts a base mod in the stack with its layout and no sources. `[lua] writes` becomes
  the globals its entries name, an alias as its global. A name no required base mod exports is a link error, and so
  is a list in a mod that requires none. The link cache keeps each layout (`layout/<mod>/globals.json`) and each list;
  a missing layout file makes the entry incomplete.

Tests: `LuaNamesTests` (25) over a synthetic layout, and three link tests in `ModLuaLinkTests` (the shipped
`himegari-base` among them). Driven in the port on 2026-11-15 (sandbox `luanames`):
- without mods, the console read `maid.names.g.current_stage_turn` and `#maid.names.table("unit_current_levels")`
  (100), wrote `stage_turn_limit` through `require("himegari-base")`, and refused index 100;
- with a scratch mod that requires `himegari-base` and lists `stage_turn_limit`, the engine mounted the shipped base
  mod with its note. The mod's `ome.session.start` handler wrote the limit, and its write to `current_stage_turn` was
  refused with the list. Until the write scan (piece 4), the link still prints its old note for a written-out name.

### 7.3 Events

Built 2026-11-15 (spec §7.4, §8.2, §9.2):

- **The declarations** (`engine/Ome.Mods/Manifest/ModEvents.cs`). `ManifestReader.ParseEvents` reads an `events/*.toml`
  file: `[[event]]` tables with a dotted name, exactly one of `point` and `raised_by = "lua"`, a description, and
  `[[event.field]]` entries (name, type, `access` with `r` the default, `at`). Unknown keys are errors.
- **The rules** (`engine/Ome.Engine/Modding/GameEvents.cs`) are the engine's, so a runtime cannot loosen them:
  - `FieldLocation.Parse` reads `at`: `L[n]`, `LF[n]`, `LS[n]`, `G[n]`, `GF[n]`, `GS[n]`, `arg[n]`, an exported
    name, and `table[loc]`, `table[loc][loc]` or `table[loc].column`, with numbers or other locations as indices. A
    fixed index is checked against the table's shape when it is read.
  - `GameEvent.BuildField` checks a field: a Lua name other than `event`, its type against its cell's kind, a
    location for an event at a point and none for one raised from Lua.
  - `EventFieldRules` holds the value rules (a `bool` is an integer cell, 1 or 0). `EventValues` holds an emit's
    values and refuses a missing or an undeclared field.
- **The link** (`ModLuaLinker`) reads every declaration before the hooks:
  - Names resolve through the declaring mod's layout, or the first base mod it requires.
  - An event at a point is bound as a point hook is, and its declaring mod needs `vm`.
  - An event declared twice is a hard conflict naming both mods. A broken declaration is an error naming its file,
    and its subscribers report nothing more.
  - An event hook names a declared event, and its mod requires the declaring mod or is it.
  - A point event's subscribers join the instruction's list with the point hooks, each naming its event
    (`PlannedHandler.GameEvent`). A Lua event's subscribers go to `HookPlan.LuaEvents`.
  - The ordered interaction lists the merged order, a subscriber as "… on EVENT".
  - The patch points are the profile's, else the copy a base mod ships (plan §7.1). `link.json` keeps the events, as
    written, and `lua_events`. A due handler keeps its `event`, and the cache reads the fields again over the same
    layout.
- **The run** (`engine/Ome.Engine/Natives/Scripting.Events.cs`):
  - A subscriber at a point gets a `HookContext` of kind `GameEvent` whose fields are live views (`PointFields`):
    locals through the interpreter, globals and table elements through script memory, operands of the point's
    instruction with the write rules of `ctx.args`.
  - `maid.events.emit` runs the subscribers inside the call. The engine checks that the event is declared and raised
    from Lua, that the caller is its owner, and that a session is running.
  - Under `--lua-errors halt`, a subscriber's failure is kept and returned by the outermost call (`_nestedHalt`).
- **A call inside a call** (`LuaModSession.Guarded`). A subscriber inside an emit is a call of its own: its own token
  and budget, and its error converted for its own handler. The outer call's token and count resume after it.
  Before, the inner call shared the raiser's budget, and a subscriber's Lua error became an error of the raiser's.
  The nesting cap (32) bounds an event that raises itself.
- **Lua** (`LuaViews.cs`, `ModEnvironment.cs`). `ctx.event` is the event's name and `ctx.<field>` its value: a
  boolean for `bool`, a number or a string otherwise. A write takes only its type's Lua value, so a number for a
  `bool` field is an error. `maid.events.emit(name, fields)` returns its table with the values the subscribers left.
- **The first event.** `mods/himegari-base/events/experience.toml` declares `himegari.experience.award` at
  `himegari.experience.after_calculation`: `amount` (`L[0x1]`, rw), `unit` (`current_unit_id`) and `level`
  (`unit_current_levels[current_unit_id]`). The example `oms/examples/mods/example.double-exp-event` is spec §9.4's
  worked example: it has no `vm` and names no address.

Gate 5, as far as this piece reaches:
- **Order** (`LuaEventsTests`, 53 tests). Two subscribers and a point hook between them on one instruction run in
  priority order, and each sees the cells the one before left. An event raised from Lua runs its subscribers in their
  own order inside the emit. A failing subscriber is logged against itself and the emit goes on; under halt the run
  ends with its reason. A runaway subscriber is stopped by its own budget while its raiser completes.
- **The link** (`ModLuaLinkTests`, 12 tests). It reports the merged order (gate 3's undeclared event among them), and
  the cache keeps the events. On the install, `example.double-exp-event` on `ADDEXP.BIN` leaves the same session as
  the guide's OMS patch.
- **Still to come:** the trace that shows the value after each handler is `--lua-trace` (piece 5), and the site hook
  in the gate's one order is milestone 4's.

Driven in the port on 2026-11-15 (sandbox `luaevents`): with `--mod oms/examples/mods/example.double-exp-event` the
engine mounted the shipped `himegari-base`, linked the subscriber into `ADDEXP.BIN`'s list at the point (`link.json`
keeps `"event": "himegari.experience.award"`), and the console found `on_award` in the mod without `maid.vm`. An
award in play needs a battle; the install test above runs the same native on `ADDEXP.BIN`.

### 7.4 The write scan and `--lua-dev`

Built 2026-11-15 (spec §9.3, §13):

- **The scan** (`ModLuaLinker.ScanWrites`). The source check already collects the names a mod writes where it
  assigns: `x.g.NAME = …` and `x.table("NAME")[…] = …` (`ModSourceCheck.WrittenNames`, milestone 1's own token scan).
  The link resolves each through the base mods the mod requires, aliases included:
  - a name the list lacks gives one note for the mod with every such name and its file, and the list as it should
    read; the link records it (`LinkedLua.Undeclared`, a `ModUndeclaredWrite` per name);
  - a name no required base mod exports gives a note: that write fails when it runs.
  - Base mods and mods that require none are not scanned. A write through a local alias, or a name built at run
    time, is left to the runtime check.
- **`omp build`** refuses a mod with an undeclared write, naming each and pointing at the note's list. `omp check`
  and the startup report only note it, as a run fails the write itself.
- **`--lua-dev`** (`ModLaunchOptions.LuaDev`, copied to `ModLinkBase.LuaDev`). The link marks each mod that is a
  folder and requires a base mod (`ModRuntimeMod.LuaDev`) and notes it; a packed mod gets a note that its list is
  enforced. At run time `Scripting.NamedFace` lets a marked mod's unlisted write through and writes one warning per
  mod and name to `diagnostics/lua.log` (`_devWrites`, kept across a scene reload). The other cell rules hold. The
  link cache keeps the mark, and the flag is part of its key when the stack ships Lua. The port's `[lua]` line names
  the mods it applies to.
- **Tests.** `ModLuaLinkTests` (3): the note and its list with an alias already listed, the unexported note, the quiet
  base and plain mods; `omp build` refusing and then building once the list is complete; `--lua-dev` marking the
  folder and not the packed `.omp`, the cache mark and key. `LuaNamesTests` (1): an unlisted write goes through under
  `--lua-dev`, the log names it once, and a fractional write to an integer cell still fails.

Checked on the install on 2026-11-15 with a scratch folder mod whose list names `adv_hide_window_enabled` and whose
source writes `adv_hover_history`: `omp check` gave the note with `writes = ["adv_hide_window_enabled",
"adv_hover_history"]`. In the port (sandbox `luadev`, `--port-arg=--lua-dev`) the `[lua]` line ended `; --lua-dev for
scratch.devwrite`, two writes through the console in the mod's environment went through with one `lua.log` warning,
and a write of `1.5` to the integer cell still failed.

### 7.5 The link report's Lua section and `--lua-trace`

Built 2026-11-15 (spec §12, §13; gate 5's trace, gate 15's remaining cases):

- **The report** (`ModLuaLinker.Summary`, `ModLinkReport.Lua`, rendered after the findings as `lua:`):
  - `mod`: each mod in the runtime stack in load order, with a base mod's global count, `library`, its capabilities
    (`vm (opaque)`), and its writes: the list's global names and the base mods they go through, `and any cell by
    address` with `vm`, and the `--lua-dev` addition (opaque);
  - `hook`: each planned instruction and engine event, its handlers in the merged order;
  - `event`: each declared event with its owner, where it is raised, its fields and its subscribers.
  - Two mods whose lists name one global are an ordered interaction in load order. The `vm` note and the
    `--lua-dev` note say "opaque". The cache keeps the section, as it keeps the rendered report.
- **The trace** (`Natives/Scripting.Trace.cs`, `ScriptingSetup.Trace`, the port's `--lua-trace`):
  - `Call` opens a record before the handler (the fields read through the context, the time) and writes the line
    after it, before the context goes dead, with `result` from the error kind. Nested calls keep a stack; a line
    names the calls it ran inside (`within`, innermost first) and is written before theirs.
  - Writes are recorded where they are made: `NamedFace.Write` (the accessor, with `name[row][column]`),
    `VmFace` (globals with the layout's name, `GameLayout.NameAt`, and locals), and `HookContext.WriteOperand`
    through `OperandWritten`, which only a traced point call sets. Each write is the innermost call's, with `through`
    when the face's mod is not the call's: a base mod's helper writing for its caller.
  - Without the flag nothing is read or allocated per write: the faces test `Tracing` (a traced call is running).
  - The file is opened at the run's first line with `FileMode.Create` and closed by `Dispose`; the last 256 lines
    are kept in memory (`RecentTrace`). A write error logs one warning and stops the file.
- **The port's `[lua]` line** now counts the mods that ship Lua apart from the base mods in the stack for their
  layout (piece 2 counted those as shipping Lua), and says when the trace is on.
- **Tests.** `LuaTraceTests` (5): two subscribers' amounts in order (gate 5's trace), each write named for its handler
  with a base mod's helper's write `through` the base mod and needing no list entry (gate 15), a nested call's
  `within` and a failure's `result`, the file only under the flag and started anew, and `NameAt`.
  `ModLuaLinkTests` (2): the section's lines, the write interaction, opaque under `vm` and `--lua-dev`, no section
  without Lua; the startup report keeps the section (below).
- **Gates.** Gate 15 has every case now. Gate 5 has all but the site hook in the merged order, held for
  milestone 4.

Driven in the port on 2026-11-15 (sandbox `luatrace`, `--port-arg=--lua-trace`) with a scratch mod whose
`ome.session.start` handler writes `adv_hover_history` through `himegari-base`: the `[lua]` line read `1 mods ship Lua
(scratch.trace), with the layout of himegari-base; 1 handlers; errors continue; tracing to
diagnostics/lua-trace.jsonl`, and the trace held one line, the handler with `reason` before and after and the write
as `G[0x6c9]` named `adv_hover_history`. The first run showed that the launcher's merged report dropped the section
(`ModLaunch.Run` copies the order and the dropped mods into a new report); it now copies the section too, and
`ModLuaLinkTests.TheStartupReportKeepsTheLuaSectionWhenItLinksAndWhenItReusesTheCache` covers it. The rerun's
`mod-link.txt` ends with the base mod, the scratch mod, its hook and the experience award with no subscribers.

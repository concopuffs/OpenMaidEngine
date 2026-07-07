# call-script execution in the C# VM — design (2026-07-07)

## Goal

Make `call-script <id>` (opcode 0x03) **actually execute** in the C# engine (`Age.Engine`): load the
target `.BIN` by id, run it as a nested subroutine that shares the global bank, and return to the
caller. This unlocks the ~297 subroutine scripts the corpus calls (MES, ADDITEM, the CALC* family,
SHOWGROW, …) which are currently stubbed — the first functional step past "single script runs" toward
a driven playthrough.

Enabled by the native-RE finding that `call-script <id>` is a direct raw index into the SYS4INI file
table (see `docs/engine-re.md`, "op 0x03 (call-script)"; `docs/name-resolution.md §1`).

## Scope

**In scope:** subroutine execution — nested load + run + return, sharing globals, with per-call local
frames. Validated headless + in the existing hosts.

**Out of scope (this slice):**
- Scene chaining / decision→scene (the SCJUMP decision-value → scene-id native hop is still open).
- Input model (`0x90`) and any new interactive behavior.
- The graphics geometry/blend drift (a separate, state-divergence problem).
- Reimplementing call-script in `vm0.py` (see Validation — vm0.py is retired from oracle duty).

## Background — current architecture

`VirtualMachine` (`engine/Age.Engine/Vm/VirtualMachine.cs`) runs **one** `Script` with **one** `Frame`
and a single intra-script `_callstack` (for op 0x8f `call`). `call-script` today is a stub:
`_host.CallScript(id); return pc + 1;` — the target never runs.

Scripts are loaded by **name** from `Paths.Scripts()` (the override-aware corpus map: root loose-file
overrides shadow `extracted/DATA1/*.BIN`) via `Sys4Loader.Load`. `GameSession` carries the flat global
bank across scenes; local frames correctly do not persist. Seam rule: `Vm` references only `Model` +
`Hosting`, never `Sys4`.

Corpus facts grounding the design: called scripts terminate with `exit` (ADDITEM, MES, SHOWGROW) or
`ret` (BUNKI); native call depth is bounded (≤ 0x26 = 38); all 297 distinct call-script ids resolve to
a DATA1 `.BIN` (0 out-of-range, 0 alternate-pack).

## Design

### 1. Script-provider seam

A small interface in `Hosting` (which may reference `Model`), injected into the VM so `Vm` never
references `Sys4`:

```csharp
public interface IScriptProvider { Script? GetById(long id); }
```

- `Sys4` implements `Sys4ScriptProvider`: `id → build/callscript-names.json → name →
  Paths.Scripts()[name] → Sys4Loader.Load`, **cached by id** (MES is called 59×; parse once). Reusing
  `Paths.Scripts()` gives the native "loose override first" behavior for free (it already prefers root
  overrides over the archive copy).
- The VM takes the provider as an **optional** constructor dependency. Product paths (CLI `play`,
  Godot) always supply one, so execution is effectively always-on. The provider is a genuine
  dependency, not a feature flag — you cannot run a script you cannot load. A VM constructed without a
  provider that then hits `call-script` halts with a clear reason (it never happens on product paths).

### 2. Recursive frame execution

Per-script state moves out of instance fields into an `ExecFrame`:

- `Script` being run, the local `Frame` (I/F/S/P slots), the intra-script `call`/`ret` stack, the `pc`,
  and the **emit-seen** loop-guard map.

The run loop becomes `RunFrame(ExecFrame)`. `Run(entry)` builds the top frame and calls it.
`call-script id` → `provider.GetById(Read(a[0]))` → build a child `ExecFrame` (fresh locals, entry
pc 0) → `RunFrame(child)` → on return, the caller continues at `pc + 1`. Recursion (not an explicit
stack list) is chosen because depth is bounded (≤ 38), so there is no overflow risk and
`exit`-returns-to-caller falls out as a plain return from `RunFrame`.

Shared VM-level state stays on the instance: `Globals`, `GlobalStrings`, `Emitted`, `Steps`, `_host`,
`_provider`.

### 3. Semantics

- **Shared globals = the return channel.** A callee returns results by writing globals the caller reads
  (the native shared-global model; no explicit return value). Local frames are per-call and discarded
  on return (matches `GameSession`).
- **`exit` / `exit-script`:** pop the current frame. Empty stack (top-level) → HALT; otherwise return
  to the caller at its `call-script` + 1. Only the outermost `exit` halts.
- **`ret` (op 0x5):** returns from an intra-script `call` (0x8f) via the frame's `call`-stack. When a
  script's top-level flow reaches `ret` with an empty `call`-stack (e.g. BUNKI), it returns from the
  **script frame** (same as `exit`). **This empty-stack `ret` behavior is confirmed against the native
  op 0x5 handler (`ctx[0x26c93+5]` = `LAB_00417ad0`) during implementation**, not guessed.
- **Emit-seen is per-frame.** The loop guard keys on string offset, and offsets are script-local
  (MES `0x100` ≠ SC0000 `0x100`); a shared map would collide and falsely trip the cap on hot
  subroutines. It lives in `ExecFrame`, reset per call.
- **Depth cap:** cap recursion at ~38 frames; exceeding halts with a distinct reason (the native throws
  there). Guards runaway / mutual recursion.
- **Steps:** one monotonic counter across all frames (subroutine steps count toward it).
- **`Emitted`:** aggregates all frames' text in execution order; each entry carries the source script's
  identity so mixed output is disambiguable in golden traces.
- **Dynamic ids:** the operand is usually immediate but may be g-int/l-ptr; `Read(a[0])` handles all
  types, so a computed target resolves through the same provider.
- **`IHost.CallScript(id)`** is kept as a fire-on-entry notification (diagnostics/logging); control flow
  is now VM-owned. Existing host implementations are unaffected.

## Validation

**vm0.py is retired from oracle duty.** It served its purpose (prototyping the execution model and
proving the C# port byte-identical across 297 scenes); it stays in the repo as a frozen historical
reference and Python-side experiment tool but is no longer maintained in lockstep. Reimplementing
call-script in it would be double work for a shrinking payoff (the C# engine now also has real-game
ground truth vm0.py never had).

The C# engine **owns its correctness fixtures**:

- **Golden traces regenerated from the C# engine** and checked into the repo as the source of truth
  (replacing "match whatever vm0.py emits"). Scenes that call no subroutines are unchanged (and, for a
  cheap belt-and-suspenders check, still happen to match vm0.py — an optional narrowed parity test).
- **New on-path checks:**
  - the corpus sweep (`Age.Cli sweep`) still **terminates cleanly** with execution on (no new hangs /
    depth-cap trips across the corpus);
  - **known subroutines execute and return** — e.g. a scene calling MES/ADDITEM shows the subroutine's
    effects (emitted text / global writes) and control resumes after the call;
  - the empty-stack `ret` and `exit`-returns-to-caller branches have unit tests with hand-built frames;
  - the new SC0000 opening trace (subroutines now executed) is validated by inspection against the
    real-game ground truth we hold (Frida load order, screenshots, by-ear audio).

## Testing

- **Unit (xUnit, `Age.Engine.Tests`):**
  - a fake in-memory `IScriptProvider` returning hand-built `Script`s: caller → callee → return to
    caller at `pc+1`; callee `exit` returns (does not halt caller); nested depth; shared-global
    write-visible-to-caller; per-frame locals don't leak; depth-cap halt; missing-id halt.
  - empty-stack `ret` returns from the frame (after the native-handler confirmation).
- **Integration:** `Sys4ScriptProvider` resolves real ids (0x1ab→ADDITEM, 0x2ae7→MES) and caches;
  a real scene that calls MES executes it.
- **Corpus:** `sweep` terminates with execution on; golden-trace fixtures regenerated + committed.
- **Regression:** existing non-call-script scenes produce unchanged golden traces.

## Risks / open items

- **Empty-stack `ret` semantics** — resolved by decompiling `LAB_00417ad0` before relying on it.
- **Headless zero-state subroutines** — with no seeded state some callees may take odd branches; the
  sweep-terminates check is the guard. Not a correctness bug in call-script itself (state divergence).
- **Golden-trace churn** — one-time regeneration; the diff is reviewed, not blindly accepted.

## Files touched (anticipated)

- `Age.Engine/Hosting/IScriptProvider.cs` (new), `IHost.cs` (unchanged; `CallScript` kept as notify).
- `Age.Engine/Vm/VirtualMachine.cs` (ExecFrame refactor + call-script execution), `Vm/Frame.cs`
  (possibly `ExecFrame`), `Vm/VmOptions.cs` (depth cap constant).
- `Age.Engine/Sys4/Sys4ScriptProvider.cs` (new), `Sys4/Paths.cs` (callscript-names.json path if needed).
- `Age.Cli/Program.cs` (wire the provider into `play`/`run`/`sweep`), Godot `GodotAdvHost`/`Main`
  (supply the provider).
- `Age.Engine.Tests/*` (new call-script tests), regenerated golden fixtures.

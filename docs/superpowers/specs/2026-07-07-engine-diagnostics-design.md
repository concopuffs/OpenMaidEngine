# engine diagnostics / trace facility — design (2026-07-07)

## Goal

Give `Age.Engine` a **first-class, extensible diagnostics seam it owns** — a typed event stream the VM
emits and any frontend, the CLI, or a test consumes. The engine should surface what *it* handles
(script/scene execution, call-script dispatch, opcode flow, halts, step counts); a frontend should log
only *its* realm (graphics, sound, input). Today that boundary is backwards: the VM has no logging seam,
so "a script is being run" — an engine fact — is surfaced by routing through `IHost` + a thread-safe
queue + a main-thread `GD.Print` in Godot, and every CLI diagnostic (`audio`, `gfx`) is a full bespoke
`IHost` reimplementation.

The point of this slice is **the seam, not any one diagnostic feature**. v1 stays small (console/file
text; a modest event vocabulary) but is built so the *next* time we want visibility into something, we
add to this facility rather than writing another bespoke host.

## Scope

**In scope:**
- A typed, injectable trace sink in `Age.Engine` (`ITraceSink` + `TraceEvent` + `NullTraceSink` +
  `TextTraceSink`).
- The VM emits a v1 event vocabulary (Step, FrameEnter, FrameExit, CallScript, Stub, Halt).
- **Boundary correction:** remove the two diagnostic-only methods (`CallScript`, `OnStub`) from `IHost`;
  relocate them to trace events. Migrate the 4 non-test implementers + 3 real consumers.
- CLI `--trace` wiring on `run`/`play`/`sweep`; Godot dispatch visibility moved onto a `GodotTraceSink`.
- Parity: default `NullTraceSink` ⇒ existing behavior byte-identical.

**Out of scope (this slice):**
- Serilog / `Microsoft.Extensions.Logging` / `EventSource` in the core (see *Alternatives*). These may
  later be added as **edge sink adapters**, never as the seam.
- A rich event vocabulary beyond the six kinds (GlobalWrite, BranchTaken, etc. — add lazily when a
  consumer needs them).
- Any Godot debug-overlay UI. Godot's change is limited to retiring the dispatch-queue hack via a sink.
- Composite/fan-out sink, JSON-artifact sink, divergence differ (all future consumers the seam enables,
  none built now).
- Migrating the `gfx`/`audio` **resolving** hosts to the sink — they do genuine host-side resId→file
  work, not pure diagnostics; they only shed the two dead `IHost` methods.

## Background — current architecture

`VirtualMachine` (`engine/Age.Engine/Vm/VirtualMachine.cs`) runs scripts as nested `ExecFrame`s
(`RunFrame`), sharing a flat global bank; summary facts already live as VM properties (`Steps`,
`HaltReason`, `Emitted`). Diagnostic notifications are smuggled through `IHost`:

- `IHost.CallScript(long id)` — fired at the `call-script` op site (`VirtualMachine.cs:173`), purely to
  notify observers. Real consumers: `CaptureHost.CallScriptCount` (the CLI `run` summary line),
  `GodotAdvHost.Dispatched` (the thread-queue hack `Main` prints at scene end), and 3 tests. Everyone
  else no-ops it.
- `IHost.OnStub(int opcode)` — fired at the `default:` arm (`VirtualMachine.cs:215`). Its only consumer,
  `CaptureHost.Stubs`, is **written but never read** — dead output.

Seam rule (unchanged): `Vm` references only `Model` + `Hosting`, never `Sys4`. This design adds
`Diagnostics` as a third allowed `Vm` dependency — it is version-neutral and references no `Sys4`.

`IHost` implementers today (10): `CaptureHost` (engine); `GodotAdvHost` (godot); `AudioTraceHost`,
`GfxTraceHost` (CLI); and 6 test hosts (`VoiceCountHost`, `RecHost`, `FakeSizeHost`, `RecordingHost`,
and two `NullHost`s). Each drops exactly two lines in the migration.

## Design

### 1. The seam — `ITraceSink`

New namespace `Age.Engine.Diagnostics`. One hot method plus one cheap gate:

```csharp
public interface ITraceSink
{
    bool TracingSteps { get; }        // cheap gate: the VM skips constructing per-instruction Step
                                       // events when no consumer wants them (keeps the hot path free)
    void Emit(in TraceEvent e);       // `in` = readonly-struct by ref, no copy
}
```

- Injected into the VM as an **optional** constructor dependency (exactly like `IHost`); `null` →
  `NullTraceSink.Instance`.
- **Invariant — observe-only:** a sink never reads or writes VM state and never influences control
  flow. This is what protects the byte-identical selftest and lets us guarantee parity.

```csharp
public VirtualMachine(Script s, OpcodeTable t, IHost host,
                      VmOptions? o = null, IScriptProvider? provider = null,
                      ITraceSink? sink = null)   // null → NullTraceSink.Instance
```

`GameSession.RunScene` threads the sink through so cross-scene runs trace uniformly.

### 2. The event — `TraceEvent`

A `readonly struct` with a `Kind` discriminator and a small shared field set, so events cost **no heap
allocation** (a corpus sweep is ~1.46M instructions; a record hierarchy would allocate per event).
Constructed via static factories so call sites read clean:

```csharp
public enum TraceEventKind { Step, FrameEnter, FrameExit, CallScript, Stub, Halt }

public readonly struct TraceEvent
{
    public TraceEventKind Kind { get; }
    public int    Pc     { get; }
    public int    Opcode { get; }
    public int    Depth  { get; }
    public long   Id     { get; }   // call-script id
    public long   Steps  { get; }
    public string? Name  { get; }   // script/scene name; resolved call-script name
    public string? Text  { get; }   // halt reason; frame outcome
    public Instruction? Ins { get; } // Step carries the instruction (args) by ref — never copied

    public static TraceEvent Step(int pc, Instruction ins, int depth);
    public static TraceEvent FrameEnter(string name, int depth, FrameCause cause, long id = 0);
    public static TraceEvent FrameExit(string name, int depth, string outcome);
    public static TraceEvent CallScript(long id, string? name, bool resolved);
    public static TraceEvent Stub(int opcode, int pc);
    public static TraceEvent Halt(string reason, long steps);
}

public enum FrameCause { TopScene, CallScript }
```

Extensibility contract: a new kind = one enum member + one factory + one emit call. Consumers that
don't recognize a kind ignore it.

### 3. Event vocabulary (v1) — where each is emitted

| Kind | Emitted at | Carries | Replaces |
|---|---|---|---|
| `Step` | `RunFrame` loop, per instruction — **gated by `TracingSteps`** | pc, opcode, `Ins`, depth | *(new)* |
| `FrameEnter` | top of `RunFrame` | name, depth, cause (`TopScene`/`CallScript`), id | *(new)* |
| `FrameExit` | bottom of `RunFrame` | name, depth, outcome (`Returned`/`Halted`/`RanOff`) | *(new)* |
| `CallScript` | `case "call-script"` op site | id, resolved name, resolved? | `IHost.CallScript` |
| `Stub` | `default:` arm | opcode, pc | `IHost.OnStub` |
| `Halt` | end of `Run()` | reason, total steps | reconstructed from `HaltReason` |

`CallScript` (op-site) and `FrameEnter(cause: CallScript)` (child-frame) are complementary, not
redundant: `CallScript` fires even in provider-less stub mode (no child pushed); `FrameEnter`/`FrameExit`
bracket the real nested execution when a provider runs the child. Together they reproduce the
"SC0240 dispatched 29 call-scripts as nested frames" view — now as first-class engine events instead of
the Godot host-queue hack.

### 4. Boundary correction & migration

- **`IHost` loses `CallScript(long)` and `OnStub(int)`.** All 11 implementers drop those two lines.
  `OnStub`'s consumer was dead, so nothing migrates — the `Stub` event replaces it outright.
- **Call-script count becomes a VM property.** Add `VirtualMachine.CallScriptDispatches` (incremented at
  the same site that emits the `CallScript` event), mirroring `Steps`/`HaltReason`. Summary stats live on
  the VM; the detailed stream lives in events. The CLI `run` line reads the property — no sink required
  for the common case.
- **The 3 real `CallScript` consumers migrate:**
  - CLI `run` → `vm.CallScriptDispatches`.
  - Tests (`CallScriptIntegrationTests`, `CallScriptTests`, `RecordingHost`) → a test-side
    `RecordingTraceSink` (captures the event list, incl. ids for `CallScriptTests`), or the property for
    pure counts.
  - Godot `Dispatched` queue → a `GodotTraceSink` that records `CallScript` events thread-safely; `Main`
    reads them at scene end exactly as it read `Dispatched`. This retires the motivating hack.

### 5. Sinks shipped in v1 (two)

- `NullTraceSink` — singleton; `TracingSteps => false`; empty `Emit`. The default → total parity.
- `TextTraceSink(TextWriter writer, bool includeSteps = false)` — the one built-in formatter; writes
  deterministic text to `Console.Out` or a file stream. `includeSteps` off by default (Step volume);
  when on, `TracingSteps => true`. Formats each kind to a stable line (e.g.
  `» enter SC0000` / `call-script 0x1ab =ADDITEM (resolved)` / `halt: exit @ 27994 steps`).

Future sinks — **noted, not built:** `CompositeTraceSink` (fan-out to console + file), a JSON-artifact
sink (run diffing / regression), a divergence differ, and framework **adapters** at the edge —
`SerilogTraceSink` (rolling files / Seq) and `EventSourceTraceSink` (out-of-proc `dotnet-trace` /
PerfView profiling). Each is a plain `ITraceSink`; none binds the core.

### 6. CLI surface

`run` / `play` / `sweep` gain:

```
--trace                 attach a TextTraceSink to Console.Out
--trace-file <path>     …to a file instead
--trace-steps           include per-instruction Step events (verbose)
```

Absent ⇒ `NullTraceSink`. `gfx`/`audio` behavior is unchanged (their resolving hosts only shed the two
dead `IHost` methods).

## Alternatives considered

- **Serilog / `Microsoft.Extensions.Logging` / NLog / ZLogger (logging frameworks).** All are
  output-oriented and stringly-typed for programmatic consumers, and want a dependency in the core. A
  future state-divergence differ wants `e.Kind == Step && e.Opcode == …`, not `Properties["Op"]` fished
  from a bag and re-parsed; and per-`Step` logging through a `LogEvent` allocates at 1.46M-instruction
  scale. MEL is the only one worth singling out (standard abstraction, DI-native) but buys little in a
  classlib + CLI + Godot app while adding a core dependency. **Rejected for the core.**
- **BCL built-in tracing (`DiagnosticSource`, `EventSource`).** Philosophically identical to `ITraceSink`
  (in-proc, typed-ish, multi-subscriber, `IsEnabled` gate) and dependency-free. But payloads are
  `object`/primitive-only — to stay allocation-free and typed at Step granularity you fight the API, and
  the subscriber ergonomics (`IObserver<KeyValuePair<string,object>>`) are heavier than `Emit(in e)`.
  **Kept as a future edge sink** (`EventSourceTraceSink` gives free `dotnet-trace`/PerfView consumption),
  not the seam.
- **Decision:** hand-roll the ~5-line typed `ITraceSink`. Our constraints (in-proc, single producer,
  zero-alloc at Step granularity, *typed* payload for the differ, zero core deps, clean Godot/Mono build)
  all cut against what general frameworks optimize for; each would cost a dependency and an impedance
  mismatch to save five lines.

## Testing

- **Parity is the invariant.** Default `NullTraceSink` ⇒ existing engine tests (25/25) and the Godot
  `--selftest` stay byte-identical. Migrated call-script tests assert the same facts via
  `CallScriptDispatches` / `RecordingTraceSink`.
- **Hot-path guard.** With `NullTraceSink`, the loop adds only `if (_sink.TracingSteps) …` (short-circuit)
  plus empty `Emit` for the rare events — confirm a `sweep` timing is unaffected.
- **New tests** (via `ScriptAssembler`; synthesize-don't-disable — never disable a feature to keep a
  golden matching):
  - A synthetic scene with a nested call-script through a `RecordingTraceSink`; assert the event sequence
    (`FrameEnter(TopScene)` → `Step…` → `CallScript(resolved)` → child `FrameEnter(CallScript)` /
    `FrameExit` → `Halt`).
  - The `TracingSteps` gate: off ⇒ zero `Step` events; on ⇒ exactly one per executed instruction.
  - `TextTraceSink` deterministic formatting for each kind.

## Files touched

- **New:** `engine/Age.Engine/Diagnostics/{ITraceSink,TraceEvent,NullTraceSink,TextTraceSink}.cs`;
  `engine/Age.Engine.Tests/TraceSinkTests.cs`; a test `RecordingTraceSink` helper.
- **Edited:** `Vm/VirtualMachine.cs` (sink field + emit calls + `CallScriptDispatches`);
  `Vm/GameSession.cs` (thread the sink); `Hosting/IHost.cs` + `Hosting/CaptureHost.cs` (drop 2 methods);
  `Age.Cli/Program.cs` (`--trace` wiring; drop 2 methods from `AudioTraceHost`/`GfxTraceHost`; `run`
  count from property); `godot/GodotAdvHost.cs` + `godot/Main.cs` (drop 2 methods; `GodotTraceSink`);
  the 6 test hosts (drop 2 methods); migrated call-script tests.
- **Docs on completion:** `docs/tools-reference.md` (the `--trace` flag + sinks); status memory +
  `MEMORY.md` line.

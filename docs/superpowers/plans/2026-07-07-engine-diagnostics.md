# Engine Diagnostics / Trace Facility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `Age.Engine` a typed, injectable diagnostics seam (`ITraceSink`) that the VM emits engine facts to, and relocate the two diagnostic-only methods (`CallScript`, `OnStub`) off `IHost` onto it.

**Architecture:** The VM emits allocation-free `TraceEvent` structs (Step, FrameEnter, FrameExit, CallScript, Stub, Halt) to an `ITraceSink` supplied at construction; the default `NullTraceSink` makes every existing path byte-identical. One built-in `TextTraceSink` formats events to console/file. Consumers (CLI, Godot, tests) subscribe by supplying a sink instead of reimplementing `IHost`.

**Tech Stack:** C# / .NET 8, xUnit. Solution `engine/AgeEngine.sln`; core `engine/Age.Engine`; CLI `engine/Age.Cli`; tests `engine/Age.Engine.Tests`; Godot project `godot/Himegari.csproj` (.NET, referenced separately).

## Global Constraints

- **Working directory for all commands:** `S:/Game Hacking/Eushully/Himegari/age-reimpl` (the repo root). Branch: `feat/engine-diagnostics` (already created).
- **Parity is the invariant:** with the default `NullTraceSink`, VM behavior is byte-identical. Existing engine tests (25) and the Godot `--selftest` must stay green. A sink is **observe-only** — it never reads/writes VM state or influences control flow.
- **Seam rule (unchanged):** `Vm` references only `Model` + `Hosting` + the new `Diagnostics` namespace — never `Sys4`. `Diagnostics` is version-neutral and references no `Sys4`.
- **No new third-party dependencies.** `Age.Engine` stays dependency-free (Serilog / `EventSource` are deferred to optional future edge sinks, not built here).
- **Test data is synthesized, never crippled:** build synthetic scenes with `ScriptAssembler`; never disable a feature to keep a golden matching (project principle — see status memory).
- **Build:** `dotnet build engine/AgeEngine.sln`. **Test:** `dotnet test engine/Age.Engine.Tests/Age.Engine.Tests.csproj`. Filter a class with `--filter FullyQualifiedName~ClassName`.
- Spec: `docs/superpowers/specs/2026-07-07-engine-diagnostics-design.md`.

---

## File Structure

**New (all in `engine/Age.Engine/Diagnostics/`):**
- `ITraceSink.cs` — the seam interface (`TracingSteps` gate + `Emit(in TraceEvent)`).
- `TraceEvent.cs` — `readonly struct` event + `TraceEventKind`/`FrameCause` enums + static factories.
- `NullTraceSink.cs` — inert default singleton.
- `TextTraceSink.cs` — the one built-in formatter (console/file).

**New tests:**
- `engine/Age.Engine.Tests/TraceSinkTests.cs` — type + formatting + VM-emission + gate tests.
- `RecordingTraceSink` test double — added to `engine/Age.Engine.Tests/TestSupport.cs`.

**New Godot:**
- `godot/GodotTraceSink.cs` — frontend consumer that queues `CallScript` events for the main thread.

**Modified:**
- `engine/Age.Engine/Vm/VirtualMachine.cs` — sink field, event emission, `CallScriptDispatches` property.
- `engine/Age.Engine/Vm/GameSession.cs` — thread the sink through `RunScene`.
- `engine/Age.Engine/Hosting/IHost.cs`, `Hosting/CaptureHost.cs` — drop `CallScript`/`OnStub`.
- `engine/Age.Cli/Program.cs` — `--trace` wiring; `run` count from `CallScriptDispatches`; drop 2 methods from `AudioTraceHost`/`GfxTraceHost`.
- `godot/GodotAdvHost.cs`, `godot/Main.cs` — drop 2 methods + `Dispatched`; wire `GodotTraceSink`.
- 6 test hosts (`CaptureHost`? no — test hosts: `RecordingHost`, `VoiceCountHost`, `RecHost`, `FakeSizeHost`, and the two `NullHost`s in call-script tests) — drop 2 methods; migrate the 2 call-script assertions.
- Docs: `docs/tools-reference.md`; status memory + `MEMORY.md`.

---

## Task 1: Diagnostics seam types

Pure data types — no VM changes. Self-contained and fully testable in isolation.

**Files:**
- Create: `engine/Age.Engine/Diagnostics/ITraceSink.cs`
- Create: `engine/Age.Engine/Diagnostics/TraceEvent.cs`
- Create: `engine/Age.Engine/Diagnostics/NullTraceSink.cs`
- Create: `engine/Age.Engine/Diagnostics/TextTraceSink.cs`
- Test: `engine/Age.Engine.Tests/TraceSinkTests.cs`

**Interfaces:**
- Produces:
  - `interface ITraceSink { bool TracingSteps { get; } void Emit(in TraceEvent e); }`
  - `enum TraceEventKind { Step, FrameEnter, FrameExit, CallScript, Stub, Halt }`
  - `enum FrameCause { TopScene, CallScript }`
  - `readonly struct TraceEvent` with fields `Kind, Pc, Opcode, Depth, Id, Steps, Cause, Name?, Text?, Ins?` and factories `Step(int pc, Instruction ins, int depth)`, `FrameEnter(string name, int depth, FrameCause cause, long id = 0)`, `FrameExit(string name, int depth, string outcome)`, `CallScript(long id, string? name)`, `Stub(int opcode, int pc)`, `Halt(string reason, long steps)`. **`resolved` is implied by `Name != null`.**
  - `sealed class NullTraceSink { static readonly NullTraceSink Instance; }`
  - `sealed class TextTraceSink(TextWriter writer, OpcodeTable? table = null, bool includeSteps = false)`

- [ ] **Step 1: Write the seam interface**

Create `engine/Age.Engine/Diagnostics/ITraceSink.cs`:

```csharp
namespace Age.Engine.Diagnostics;

/// <summary>The engine's diagnostics seam. The VM emits typed <see cref="TraceEvent"/>s here; any
/// consumer (CLI, Godot, tests) supplies a sink instead of reimplementing IHost. Observe-only:
/// a sink never reads/writes VM state or influences control flow (that guarantees trace parity).</summary>
public interface ITraceSink
{
    /// <summary>Cheap gate: when false the VM skips constructing per-instruction Step events, keeping
    /// the hot path (a corpus sweep is ~1.46M instructions) free. Rare events emit regardless.</summary>
    bool TracingSteps { get; }
    void Emit(in TraceEvent e);
}
```

- [ ] **Step 2: Write the event struct**

Create `engine/Age.Engine/Diagnostics/TraceEvent.cs`:

```csharp
using Age.Engine.Model;
namespace Age.Engine.Diagnostics;

public enum TraceEventKind { Step, FrameEnter, FrameExit, CallScript, Stub, Halt }
public enum FrameCause { TopScene, CallScript }

/// <summary>An engine diagnostic fact. A <c>readonly struct</c> with a Kind discriminator and a shared
/// field set — no per-event heap allocation. Only the fields relevant to a Kind are populated; the
/// static factories are the only intended constructors.</summary>
public readonly struct TraceEvent
{
    public TraceEventKind Kind { get; private init; }
    public int    Pc     { get; private init; }
    public int    Opcode { get; private init; }
    public int    Depth  { get; private init; }
    public long   Id     { get; private init; }   // call-script id
    public long   Steps  { get; private init; }   // total steps at Halt
    public FrameCause Cause { get; private init; }
    public string? Name  { get; private init; }    // script/scene name; resolved call-script name (null => unresolved/stub)
    public string? Text  { get; private init; }    // halt reason; frame outcome
    public Instruction? Ins { get; private init; } // Step: the instruction (args) by ref, never copied

    public static TraceEvent Step(int pc, Instruction ins, int depth) => new()
        { Kind = TraceEventKind.Step, Pc = pc, Opcode = ins.Opcode, Ins = ins, Depth = depth };
    public static TraceEvent FrameEnter(string name, int depth, FrameCause cause, long id = 0) => new()
        { Kind = TraceEventKind.FrameEnter, Name = name, Depth = depth, Cause = cause, Id = id };
    public static TraceEvent FrameExit(string name, int depth, string outcome) => new()
        { Kind = TraceEventKind.FrameExit, Name = name, Depth = depth, Text = outcome };
    public static TraceEvent CallScript(long id, string? name) => new()
        { Kind = TraceEventKind.CallScript, Id = id, Name = name };
    public static TraceEvent Stub(int opcode, int pc) => new()
        { Kind = TraceEventKind.Stub, Opcode = opcode, Pc = pc };
    public static TraceEvent Halt(string reason, long steps) => new()
        { Kind = TraceEventKind.Halt, Text = reason, Steps = steps };
}
```

- [ ] **Step 3: Write the null and text sinks**

Create `engine/Age.Engine/Diagnostics/NullTraceSink.cs`:

```csharp
namespace Age.Engine.Diagnostics;

/// <summary>The inert default: no step tracing, empty Emit. Supplying this (or null) to the VM
/// guarantees byte-identical behavior.</summary>
public sealed class NullTraceSink : ITraceSink
{
    public static readonly NullTraceSink Instance = new();
    private NullTraceSink() { }
    public bool TracingSteps => false;
    public void Emit(in TraceEvent e) { }
}
```

Create `engine/Age.Engine/Diagnostics/TextTraceSink.cs`:

```csharp
using Age.Engine.Model;
namespace Age.Engine.Diagnostics;

/// <summary>The one built-in formatter: writes each event as a deterministic text line to a
/// TextWriter (Console.Out or a file). Indents by frame depth. If an OpcodeTable is supplied, Step
/// lines show the mnemonic; otherwise the raw opcode. Step lines only appear when includeSteps is set.</summary>
public sealed class TextTraceSink : ITraceSink
{
    private readonly TextWriter _w;
    private readonly OpcodeTable? _table;
    private readonly bool _steps;

    public TextTraceSink(TextWriter writer, OpcodeTable? table = null, bool includeSteps = false)
    { _w = writer; _table = table; _steps = includeSteps; }

    public bool TracingSteps => _steps;

    public void Emit(in TraceEvent e)
    {
        string indent = new string(' ', Math.Max(0, e.Depth - 1) * 2);
        switch (e.Kind)
        {
            case TraceEventKind.FrameEnter:
                _w.WriteLine($"{indent}» {e.Name} (enter, {e.Cause})"); break;
            case TraceEventKind.FrameExit:
                _w.WriteLine($"{indent}« {e.Name} ({e.Text})"); break;
            case TraceEventKind.Step:
                _w.WriteLine($"{indent}  {e.Pc:x4} {Mnemonic(e.Opcode)} {Args(e.Ins)}"); break;
            case TraceEventKind.CallScript:
                _w.WriteLine($"{indent}  call-script 0x{e.Id:x} ={e.Name ?? "?"} " +
                             $"({(e.Name != null ? "resolved" : "stub/unresolved")})"); break;
            case TraceEventKind.Stub:
                _w.WriteLine($"{indent}  {e.Pc:x4} STUB op=0x{e.Opcode:x}"); break;
            case TraceEventKind.Halt:
                _w.WriteLine($"halt: {e.Text} @ {e.Steps} steps"); break;
        }
    }

    private string Mnemonic(int op) => _table?.Label(op) is { Length: > 0 } l ? l : $"0x{op:x}";
    private static string Args(Instruction? ins) =>
        ins == null ? "" : string.Join(" ", ins.Args.Select(o => $"{o.Type}:{o.Value}"));
}
```

- [ ] **Step 4: Write the failing tests**

Create `engine/Age.Engine.Tests/TraceSinkTests.cs`:

```csharp
using System.IO;
using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Xunit;

public class TraceSinkTests
{
    [Fact]
    public void FactoriesSetKindAndFields()
    {
        var ins = new Instruction(0x40, 0x55, new[] { new Operand(3, 0x10), new Operand(0, 7) });
        var step = TraceEvent.Step(0x40, ins, 2);
        Assert.Equal(TraceEventKind.Step, step.Kind);
        Assert.Equal(0x55, step.Opcode);
        Assert.Same(ins, step.Ins);
        Assert.Equal(2, step.Depth);

        var cs = TraceEvent.CallScript(0x1ab, "ADDITEM");
        Assert.Equal(TraceEventKind.CallScript, cs.Kind);
        Assert.Equal(0x1abL, cs.Id);
        Assert.Equal("ADDITEM", cs.Name);
    }

    [Fact]
    public void NullSinkIsInertAndNotTracingSteps()
    {
        Assert.False(NullTraceSink.Instance.TracingSteps);
        NullTraceSink.Instance.Emit(TraceEvent.Halt("x", 1));   // must not throw
    }

    [Fact]
    public void TextSinkFormatsEachKind()
    {
        var sw = new StringWriter();
        var sink = new TextTraceSink(sw, table: null, includeSteps: true);
        sink.Emit(TraceEvent.FrameEnter("SC0000", 1, FrameCause.TopScene));
        sink.Emit(TraceEvent.CallScript(0x1ab, "ADDITEM"));
        sink.Emit(TraceEvent.Halt("exit", 27994));
        var outp = sw.ToString();
        Assert.Contains("» SC0000 (enter, TopScene)", outp);
        Assert.Contains("call-script 0x1ab =ADDITEM (resolved)", outp);
        Assert.Contains("halt: exit @ 27994 steps", outp);
    }
}
```

- [ ] **Step 5: Run the tests — verify they pass**

Run: `dotnet test engine/Age.Engine.Tests/Age.Engine.Tests.csproj --filter FullyQualifiedName~TraceSinkTests`
Expected: 3 passed. (Types compile; no VM touched yet.)

- [ ] **Step 6: Commit**

```bash
git add engine/Age.Engine/Diagnostics engine/Age.Engine.Tests/TraceSinkTests.cs
git commit -m "feat(diagnostics): ITraceSink seam + TraceEvent + Null/Text sinks

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Emit trace events from the VM

Wire the sink into the VM, emit the six event kinds, add the `CallScriptDispatches` stat, thread the sink through `GameSession.RunScene`. Migrate the two call-script tests that asserted on the (now sink-fed) `CallScript` notification. Parity holds because the default is `NullTraceSink` and the sink is observe-only.

**Files:**
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs`
- Modify: `engine/Age.Engine/Vm/GameSession.cs:27-41`
- Modify: `engine/Age.Engine.Tests/TestSupport.cs` (add `RecordingTraceSink`)
- Modify: `engine/Age.Engine.Tests/CallScriptTests.cs:60-79`
- Modify: `engine/Age.Engine.Tests/CallScriptIntegrationTests.cs:31-35`
- Test: `engine/Age.Engine.Tests/TraceSinkTests.cs` (add VM-emission + gate tests)

**Interfaces:**
- Consumes: `ITraceSink`, `TraceEvent`, `NullTraceSink`, `TraceEventKind`, `FrameCause` (Task 1).
- Produces:
  - VM constructor gains a 6th optional param `ITraceSink? sink = null`.
  - `VirtualMachine.CallScriptDispatches` (`long`, get) — count of executed `call-script` opcodes.
  - `GameSession.RunScene(..., ITraceSink? sink = null)` — 6th optional param, forwarded to the VM.
  - `RecordingTraceSink` test double: `{ bool TracingSteps init; List<TraceEvent> Events; List<long> CallScriptIds; }`.

- [ ] **Step 1: Write the failing VM-emission and gate tests**

Add to `engine/Age.Engine.Tests/TraceSinkTests.cs` (inside the class; add `using System;`, `using System.Collections.Generic;`, `using System.Linq;`, `using Age.Engine.Sys4;`, `using Age.Engine.Vm;` at the top):

```csharp
    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);

    [Fact]
    public void VmEmitsFrameCallScriptAndHaltEvents()
    {
        var t = Table();
        // callee: exit.  caller: call-script 5 ; exit.
        var callee = ScriptAssembler.Assemble(t, "CALLEE",
            new List<(int, Operand[])> { (0x2, Array.Empty<Operand>()) }, Array.Empty<string>());
        var caller = ScriptAssembler.Assemble(t, "CALLER",
            new List<(int, Operand[])> { (0x3, new[] { new Operand(0, 5) }), (0x2, Array.Empty<Operand>()) },
            Array.Empty<string>());
        var sink = new RecordingTraceSink();
        var vm = new VirtualMachine(caller, t, new RecordingHost(), null,
                                    new MapProvider(new() { [5] = callee }), sink);
        vm.Run();

        var kinds = sink.Events.Select(e => e.Kind).ToList();
        Assert.Equal(TraceEventKind.FrameEnter, kinds[0]);                 // caller enters first
        Assert.Equal(TraceEventKind.Halt, kinds[^1]);                     // halt is last
        Assert.Equal(2, sink.Events.Count(e => e.Kind == TraceEventKind.FrameEnter));  // caller + callee
        Assert.Equal(2, sink.Events.Count(e => e.Kind == TraceEventKind.FrameExit));
        Assert.Contains(5L, sink.CallScriptIds);
        Assert.Equal(1, vm.CallScriptDispatches);
    }

    [Fact]
    public void StepEventsGatedByTracingSteps()
    {
        var t = Table();
        // mov g[0x10]=7 ; exit  => 2 executed instructions.
        var body = new List<(int, Operand[])>
        {
            (0x55, new[] { new Operand(3, 0x10), new Operand(0, 7) }),
            (0x2, Array.Empty<Operand>()),
        };
        var s = ScriptAssembler.Assemble(t, "S", body, Array.Empty<string>());

        var off = new RecordingTraceSink { TracingSteps = false };
        new VirtualMachine(s, t, new RecordingHost(), null, null, off).Run();
        Assert.Empty(off.Events.Where(e => e.Kind == TraceEventKind.Step));

        var on = new RecordingTraceSink { TracingSteps = true };
        new VirtualMachine(s, t, new RecordingHost(), null, null, on).Run();
        Assert.Equal(2, on.Events.Count(e => e.Kind == TraceEventKind.Step));
    }
```

Add the `RecordingTraceSink` double to `engine/Age.Engine.Tests/TestSupport.cs` (append; add `using System.Linq;` and `using Age.Engine.Diagnostics;` at the top):

```csharp
/// <summary>Captures every trace event for assertions; TracingSteps is settable so a test can
/// exercise the Step gate both ways.</summary>
internal sealed class RecordingTraceSink : ITraceSink
{
    public bool TracingSteps { get; init; }
    public readonly List<TraceEvent> Events = new();
    public void Emit(in TraceEvent e) => Events.Add(e);
    public List<long> CallScriptIds =>
        Events.Where(e => e.Kind == TraceEventKind.CallScript).Select(e => e.Id).ToList();
}
```

- [ ] **Step 2: Run the new tests — verify they fail to compile**

Run: `dotnet test engine/Age.Engine.Tests/Age.Engine.Tests.csproj --filter FullyQualifiedName~TraceSinkTests`
Expected: BUILD FAIL — `VirtualMachine` has no 6-arg constructor; `CallScriptDispatches` undefined.

- [ ] **Step 3: Add the sink + stat to the VM and emit events**

In `engine/Age.Engine/Vm/VirtualMachine.cs`:

Add `using Age.Engine.Diagnostics;` at the top (with the other usings).

Add the field + property near the other fields (after `private int _depth;`, line 19):

```csharp
    private readonly ITraceSink _sink;
    public long CallScriptDispatches { get; private set; }
```

Change the constructor (line 27-28) to accept the sink:

```csharp
    public VirtualMachine(Script s, OpcodeTable t, IHost host, VmOptions? o = null,
                          IScriptProvider? provider = null, ITraceSink? sink = null)
    { _s = s; _t = t; _host = host; _o = o ?? new VmOptions(); _provider = provider;
      _sink = sink ?? NullTraceSink.Instance; }
```

Change `Run` (line 97-104) to pass the frame cause and emit the terminal Halt:

```csharp
    public void Run(int entryOffset = 0)
    {
        var top = new ExecFrame(_s, _s.IndexByOffset.TryGetValue(entryOffset, out var idx) ? idx : 0);
        var outcome = RunFrame(top, FrameCause.TopScene);
        if (outcome == FrameOutcome.RanOff) HaltReason ??= "pc-out-of-range";
        else if (outcome == FrameOutcome.Returned) HaltReason ??= "exit";
        // Halted: HaltReason already set by the halting op.
        _sink.Emit(TraceEvent.Halt(HaltReason ?? "unknown", Steps));
    }
```

Change `RunFrame` (line 106-122) to take the cause, emit FrameEnter/FrameExit, and emit gated Step events:

```csharp
    private FrameOutcome RunFrame(ExecFrame frame, FrameCause cause, long callId = 0)
    {
        var prev = _cur; _cur = frame; _depth++;
        _sink.Emit(TraceEvent.FrameEnter(frame.Script.Name, _depth, cause, callId));
        var outcome = FrameOutcome.RanOff;
        int pc = frame.Pc;
        while (pc >= 0 && pc < frame.Script.Instructions.Count)
        {
            if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; outcome = FrameOutcome.Halted; break; }
            Steps++;
            if (_sink.TracingSteps) _sink.Emit(TraceEvent.Step(pc, frame.Script.Instructions[pc], _depth));
            int next = Step(frame.Script.Instructions[pc], pc);
            if (next == FRAME_RETURN) { outcome = FrameOutcome.Returned; break; }
            if (next == HALT) { outcome = FrameOutcome.Halted; break; }
            pc = next;
        }
        _sink.Emit(TraceEvent.FrameExit(frame.Script.Name, _depth, outcome.ToString()));
        _cur = prev; _depth--;
        return outcome;
    }
```

Change the `call-script` case (line 170-182) to count, emit the CallScript event, and pass the cause to the child frame (replaces `_host.CallScript(id)`):

```csharp
            case "call-script":
            {
                long id = a.Count > 0 ? Read(a[0]) : 0;
                CallScriptDispatches++;
                if (_provider == null)
                {
                    _sink.Emit(TraceEvent.CallScript(id, null));   // stub mode: notify only, no child pushed
                    return pc + 1;
                }
                if (_depth >= _o.CallDepthCap) { HaltReason ??= "call-depth-exceeded"; return HALT; }
                var child = _provider.GetById(id);
                _sink.Emit(TraceEvent.CallScript(id, child?.Name));
                if (child == null) { HaltReason ??= $"callscript-unresolved:0x{id:x}"; return HALT; }
                var entry = child.IndexByOffset.TryGetValue(0, out var ci) ? ci : 0;
                var outcome = RunFrame(new ExecFrame(child, entry), FrameCause.CallScript, id);
                if (outcome == FrameOutcome.Halted) return HALT;   // propagate whole-VM halt up
                return pc + 1;                                      // Returned / RanOff: resume caller
            }
```

Change the `default` stub arm (line 214-215) to emit a Stub event (replaces `_host.OnStub(op)`):

```csharp
            default:
                _sink.Emit(TraceEvent.Stub(op, pc)); return pc + 1;
```

- [ ] **Step 4: Thread the sink through GameSession**

In `engine/Age.Engine/Vm/GameSession.cs`, add `using Age.Engine.Diagnostics;` at the top, then change `RunScene` (line 27-41) to accept and forward the sink:

```csharp
    public SceneResult RunScene(Script script, OpcodeTable table, IHost host,
                                VmOptions? options = null, IScriptProvider? provider = null,
                                ITraceSink? sink = null)
    {
        var vm = new VirtualMachine(script, table, host, options, provider, sink);
        foreach (var kv in Globals) vm.Globals[kv.Key] = kv.Value;
        foreach (var kv in GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;

        vm.Run();

        // Globals are one flat space; last write wins — the engine's single global bank.
        foreach (var kv in vm.Globals) Globals[kv.Key] = kv.Value;
        foreach (var kv in vm.GlobalStrings) GlobalStrings[kv.Key] = kv.Value;

        return new SceneResult(vm.Emitted.ToList(), vm.HaltReason, vm.Steps);
    }
```

- [ ] **Step 5: Migrate the two call-script assertions off the host notification**

The VM no longer calls `IHost.CallScript`, so tests that asserted on it must read the sink / property instead.

In `engine/Age.Engine.Tests/CallScriptTests.cs`, `CalleeRunsAndControlResumesAfterTheCall` (line 60-67) — add a recording sink and assert on it:

```csharp
        var host = new NullHost();
        var sink = new RecordingTraceSink();
        var vm = new VirtualMachine(caller, t, host, null, new MapProvider(new() { [5] = callee }), sink);
        vm.Run();
        Assert.Equal(7, vm.Globals[0x10]);           // callee wrote a shared global
        Assert.Equal(7, vm.Globals[0x11]);           // caller read it AFTER the call returned
        Assert.Equal("exit", vm.HaltReason);         // top-level exit
        Assert.Contains(5L, sink.CallScriptIds);     // dispatch observed via the trace sink
```

And `MissingProviderFallsBackToStub` (line 69-79):

```csharp
        var host = new NullHost();
        var sink = new RecordingTraceSink();
        var vm = new VirtualMachine(caller, t, host, null, null, sink);   // no provider
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);        // did not halt on the call; stub + continue
        Assert.Contains(5L, sink.CallScriptIds);
```

In `engine/Age.Engine.Tests/CallScriptIntegrationTests.cs`, `RealScriptExecutesRealSubroutinesAndReturns` (line 31-35) — read the VM stat:

```csharp
        var host = new NullHost();
        var vm = new VirtualMachine(script, t, host, null, provider);
        vm.Run();
        Assert.Equal(2, vm.CallScriptDispatches); // ADDILLSUB + CALCREVISE both dispatched
        Assert.Equal("exit", vm.HaltReason);      // subroutines returned; ADDILL reached its own exit
```

(The `NullHost.CallScript`/`OnStub` methods in these files stay for now — they're removed in Task 3.)

- [ ] **Step 6: Run the full suite — verify green and parity held**

Run: `dotnet test engine/Age.Engine.Tests/Age.Engine.Tests.csproj`
Expected: all pass (existing 25 + the new TraceSink tests). The parity-critical tests (`RecoverTests`, `SyntheticSceneTests`, `WaitForInputTests`) use the default null sink and are unaffected.

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine/Vm/GameSession.cs \
        engine/Age.Engine.Tests/TestSupport.cs engine/Age.Engine.Tests/TraceSinkTests.cs \
        engine/Age.Engine.Tests/CallScriptTests.cs engine/Age.Engine.Tests/CallScriptIntegrationTests.cs
git commit -m "feat(diagnostics): VM emits trace events + CallScriptDispatches stat

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Slim IHost — drop CallScript/OnStub

Remove the two diagnostic-only methods from `IHost` and all engine/CLI/test implementers. The VM no longer calls them (Task 2), so this is a pure deletion — except `CaptureHost.CallScriptCount`, whose one consumer (CLI `run`) moves to `vm.CallScriptDispatches`. Godot is handled separately in Task 4.

**Files:**
- Modify: `engine/Age.Engine/Hosting/IHost.cs:5-6`
- Modify: `engine/Age.Engine/Hosting/CaptureHost.cs`
- Modify: `engine/Age.Cli/Program.cs:20` (count via property), `:216-217`, `:266-267` (drop 2 methods each)
- Modify: `engine/Age.Engine.Tests/TestSupport.cs` (RecordingHost), `GameSessionTests.cs`, `TextureOpsTests.cs`, `TextureGeometryTests.cs`, `CallScriptTests.cs`, `CallScriptIntegrationTests.cs` (drop 2 methods each)

**Interfaces:**
- Consumes: `VirtualMachine.CallScriptDispatches` (Task 2).
- Produces: `IHost` with 9 methods (no `CallScript`, no `OnStub`).

- [ ] **Step 1: Remove the two methods from IHost**

Replace `engine/Age.Engine/Hosting/IHost.cs` with:

```csharp
namespace Age.Engine.Hosting;
public interface IHost
{
    void ShowText(int offset, string text);
    void WaitForInput();
    void CreateTexture(int slot, int width, int height);
    void SetTexture(long resourceId, int slot);
    void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY);
    (int Width, int Height) GetTextureSize(int slot);
    void PlayBgm(long id);
    void PlayVoice(long id);
}
```

- [ ] **Step 2: Run the build — confirm current state (it still compiles)**

Run: `dotnet build engine/AgeEngine.sln`
Expected: BUILD SUCCEEDS. Removing the two methods from `IHost` does **not** break compilation — the implementers keep them as ordinary (no-longer-interface) public methods, and `CaptureHost.CallScriptCount`/`.Stubs` still exist, so `Program.cs:20` still resolves. This step just confirms the baseline; Steps 3–5 delete the now-dead members (they are dead because the VM stopped calling them in Task 2). This task is a no-behavior-change refactor — the suite is already green from Task 2 and must stay green.

- [ ] **Step 3: Slim CaptureHost**

Replace `engine/Age.Engine/Hosting/CaptureHost.cs` with:

```csharp
namespace Age.Engine.Hosting;
public sealed class CaptureHost : IHost
{
    public List<(int Offset, string Text)> Emitted { get; } = new();
    public void ShowText(int offset, string text) => Emitted.Add((offset, text));
    public void WaitForInput() { }
    public void CreateTexture(int slot, int width, int height) { }
    public void SetTexture(long resourceId, int slot) { }
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
    public void PlayBgm(long id) { }
    public void PlayVoice(long id) { }
}
```

- [ ] **Step 4: Fix the CLI `run` count and drop the trace hosts' dead methods**

In `engine/Age.Cli/Program.cs` line 20, change `runHost.CallScriptCount` to `vm.CallScriptDispatches`:

```csharp
    Console.WriteLine($"{Path.GetFileName(args[1])}: {vm.Steps} steps, {vm.Emitted.Count} show-text, {vm.CallScriptDispatches} call-scripts (halt: {vm.HaltReason})");
```

Delete these two lines from `AudioTraceHost` (was 216-217):

```csharp
    public void CallScript(long id) { }
    public void OnStub(int opcode) { }
```

Delete the identical two lines from `GfxTraceHost` (was 266-267).

- [ ] **Step 5: Drop the two methods from every test host**

Delete the `CallScript(...)` and `OnStub(...)` lines from each of these hosts:
- `engine/Age.Engine.Tests/TestSupport.cs` `RecordingHost` (lines 12-13) — also delete the now-unused `CallScripts` field from line 9 (`public int Waits, CallScripts;` → `public int Waits;`). `Waits` is still asserted; `CallScripts` is not.
- `engine/Age.Engine.Tests/GameSessionTests.cs` `VoiceCountHost` (lines 31-32).
- `engine/Age.Engine.Tests/TextureOpsTests.cs` `RecHost` (lines 15-16).
- `engine/Age.Engine.Tests/TextureGeometryTests.cs` `FakeSizeHost` (lines 13-14).
- `engine/Age.Engine.Tests/CallScriptTests.cs` `NullHost` — delete `CallScript`/`OnStub` (lines 18-19) and the `Calls` field (line 17: `public List<long> Calls = new();`), now that assertions read the sink.
- `engine/Age.Engine.Tests/CallScriptIntegrationTests.cs` `NullHost` — delete `CallScript`/`OnStub` (lines 12-13) and the `CallScripts` field (line 10: `public int CallScripts;`), now that the assertion reads `vm.CallScriptDispatches`.

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet test engine/Age.Engine.Tests/Age.Engine.Tests.csproj`
Expected: build clean, all tests pass. `IHost` now carries only render/audio/input methods.

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Hosting/IHost.cs engine/Age.Engine/Hosting/CaptureHost.cs \
        engine/Age.Cli/Program.cs engine/Age.Engine.Tests
git commit -m "refactor(hosting): drop CallScript/OnStub from IHost (now trace events)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Retire the Godot dispatch hack onto a sink

Replace `GodotAdvHost`'s `Dispatched` queue (the thread-hack the memory calls out) with a `GodotTraceSink`. `Main` reports subroutines from the sink; `GodotAdvHost` sheds the two dead `IHost` methods. Verified by compiling the Godot project (it isn't in the xUnit suite).

**Files:**
- Create: `godot/GodotTraceSink.cs`
- Modify: `godot/GodotAdvHost.cs:45-49` (drop `Dispatched` + 2 methods)
- Modify: `godot/Main.cs:104-105` (wire sink), `:206` (read sink)

**Interfaces:**
- Consumes: `ITraceSink`, `TraceEvent`, `TraceEventKind` (Task 1); the VM's 6-arg constructor (Task 2).
- Produces: `GodotTraceSink { ConcurrentQueue<long> CallScripts }`.

- [ ] **Step 1: Create the Godot trace sink**

Create `godot/GodotTraceSink.cs`:

```csharp
using System.Collections.Concurrent;
using Age.Engine.Diagnostics;

// Frontend-side trace consumer. Runs on the VM background thread, so it just queues the dispatched
// call-script ids; the main thread drains them (Godot drops GD.Print from background threads). This
// replaces the old IHost.CallScript -> GodotAdvHost.Dispatched hack: subroutine visibility is now an
// engine fact delivered over the trace seam.
public sealed class GodotTraceSink : ITraceSink
{
    public bool TracingSteps => false;
    public readonly ConcurrentQueue<long> CallScripts = new();
    public void Emit(in TraceEvent e)
    {
        if (e.Kind == TraceEventKind.CallScript) CallScripts.Enqueue(e.Id);
    }
}
```

- [ ] **Step 2: Drop the hack from GodotAdvHost**

In `godot/GodotAdvHost.cs`, delete lines 45-49 (the comment block, `Dispatched` field, `CallScript`, `OnStub`):

```csharp
    // Records each call-script the VM dispatches (runs on the VM thread, so collect thread-safely and
    // let the main thread report it — Godot drops GD.Print from background threads).
    public readonly System.Collections.Concurrent.ConcurrentQueue<long> Dispatched = new();
    public void CallScript(long id) => Dispatched.Enqueue(id);
    public void OnStub(int opcode) { }
```

- [ ] **Step 3: Wire the sink in Main**

In `godot/Main.cs`, add a field near the other VM fields (e.g. beside `_host`):

```csharp
    private GodotTraceSink _trace = null!;
```

Change the VM construction (lines 104-105) to create and pass the sink:

```csharp
        _host = new GodotAdvHost(this, ResourceMap.Load(), scene);
        _trace = new GodotTraceSink();
        _vm = new VirtualMachine(script, table, _host, null, provider, _trace);
```

Change `ReportSubroutines` (line 206) to drain the sink instead of the host:

```csharp
        while (_trace.CallScripts.TryDequeue(out var id)) ids.Add(id);
```

- [ ] **Step 4: Build the Godot project — verify it compiles**

Run: `dotnet build godot/Himegari.csproj`
Expected: build succeeds. (`RunSelfTest`'s headless VM at Main.cs:217 uses a `CaptureHost` with no sink — unaffected; parity of the selftest is preserved.)

- [ ] **Step 5: Commit**

```bash
git add godot/GodotTraceSink.cs godot/GodotAdvHost.cs godot/Main.cs
git commit -m "refactor(godot): report subroutines via GodotTraceSink, not the IHost queue

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: CLI `--trace` wiring

Let `run`/`play`/`sweep` attach a `TextTraceSink` to console or file. Absent flag ⇒ `NullTraceSink`.

**Files:**
- Modify: `engine/Age.Cli/Program.cs` (add `BuildSink` local function; pass sink in `run`/`play`/`sweep`)
- Modify: `docs/tools-reference.md` (document the flag)

**Interfaces:**
- Consumes: `TextTraceSink`, `NullTraceSink`, `ITraceSink` (Task 1); `GameSession.RunScene(..., sink)` (Task 2).

- [ ] **Step 1: Add the sink-builder helper**

In `engine/Age.Cli/Program.cs`, add `using Age.Engine.Diagnostics;` at the top, then add this local function just after the `provider` line (line 10):

```csharp
// --trace [--trace-file <path>] [--trace-steps] → a TextTraceSink to console or file; else inert.
static ITraceSink BuildSink(string[] a, OpcodeTable tbl)
{
    if (!a.Contains("--trace")) return NullTraceSink.Instance;
    bool steps = a.Contains("--trace-steps");
    int fi = Array.IndexOf(a, "--trace-file");
    TextWriter w = (fi >= 0 && fi + 1 < a.Length)
        ? new StreamWriter(a[fi + 1]) { AutoFlush = true }
        : Console.Out;
    return new TextTraceSink(w, tbl, steps);
}
```

- [ ] **Step 2: Pass the sink in `run`**

In the `run` block, change the VM construction (line 18-19) to:

```csharp
    var runHost = new CaptureHost();
    var vm = new VirtualMachine(script, table, runHost, null, provider, BuildSink(args, table));
```

- [ ] **Step 3: Pass the sink in `play` and `sweep`**

In `play`, change the scene run (line 103) to pass the sink:

```csharp
        var r = session.RunScene(script, table, new CaptureHost(), null, provider, BuildSink(args, table));
```

In `sweep`, the non-seeded path (line 168) similarly:

```csharp
        var r = session.RunScene(Sys4Loader.Load(scripts[name], table), table, new CaptureHost(), null, provider, BuildSink(args, table));
```

(Leave the seeded story-explorer path and the `--boot` baseline runs on the default null sink — they run each scene twice for diffing and aren't a tracing target.)

- [ ] **Step 4: Build and smoke-test the flag**

Run: `dotnet build engine/AgeEngine.sln`
Expected: build succeeds.

Run: `dotnet run --project engine/Age.Cli -- run SC0000.BIN --trace`
Expected: the normal summary line, preceded by trace lines — a `» SC0000 (enter, TopScene)`, `call-script 0x… =… (resolved)` lines for dispatched subroutines, and a final `halt: … @ … steps`. (Without `--trace-steps`, no per-instruction lines.)

Run: `dotnet run --project engine/Age.Cli -- run SC0000.BIN` (no flag)
Expected: identical summary line, no trace lines (null sink).

- [ ] **Step 5: Document the flag**

In `docs/tools-reference.md`, under the `Age.Cli` entry, add a line documenting the shared flag:

```
- `--trace [--trace-file <path>] [--trace-steps]` (on `run`/`play`/`sweep`): stream the engine's
  diagnostic events (scene enter/exit, call-script dispatch, halts; per-instruction opcodes with
  `--trace-steps`) to console or a file via the Age.Engine ITraceSink facility. Absent ⇒ no tracing.
```

- [ ] **Step 6: Commit**

```bash
git add engine/Age.Cli/Program.cs docs/tools-reference.md
git commit -m "feat(cli): --trace flag streams engine diagnostics (run/play/sweep)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Final verification + status memory

**Files:**
- Modify: status memory `himegari-port-status.md` + `MEMORY.md` (in `~/.claude/…/memory/`).

- [ ] **Step 1: Full clean build + test**

Run: `dotnet build engine/AgeEngine.sln && dotnet test engine/Age.Engine.Tests/Age.Engine.Tests.csproj`
Expected: build clean; all tests pass (25 existing + new TraceSink tests). Confirm the count grew only by the added tests and none regressed.

- [ ] **Step 2: Confirm Godot selftest parity (if a Godot runtime is available)**

Run: `dotnet build godot/Himegari.csproj`
Expected: compiles. If a Godot binary is on hand, `godot --headless --path godot -- --selftest` should still print `SELFTEST OK` (the selftest VM uses the default null sink — parity preserved). If no Godot runtime is available, note that and rely on the compile check.

- [ ] **Step 3: Update the status memory**

Append a completion note to `himegari-port-status.md` (the "ENGINE-LEVEL DIAGNOSTICS" section) and its `MEMORY.md` one-liner: the diagnostics facility is landed — typed `ITraceSink`/`TraceEvent` in `Age.Engine.Diagnostics`, six v1 events, `CallScript`/`OnStub` relocated off `IHost`, `Null`+`Text` sinks, CLI `--trace`, Godot hack retired onto `GodotTraceSink`, parity held. Note the deferred edge-sink adapters (`SerilogTraceSink`, `EventSourceTraceSink`) and future consumers (JSON artifact, divergence differ). Use absolute dates.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: record engine diagnostics facility landed

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Notes for the implementer

- **Line numbers** cite the files as they stand at plan time; if a prior task shifted them, match on the surrounding code shown in each step rather than the number.
- **`ScriptAssembler.Assemble(table, name, List<(int opcode, Operand[] args)>, string[] strings)`** builds a `Script` through the real loader — use it for synthetic scenes (opcodes: exit=0x2, call-script=0x3 argc1, mov=0x55 argc2; operand types: imm=0, global-int=3, local-int=9).
- **Why `CallScript`'s `resolved` isn't a field:** it is exactly `Name != null` (a resolved child always has a non-null `Script.Name`; stub-mode and unresolved both leave it null). The `Halt` event still distinguishes the unresolved case via its `callscript-unresolved:` reason.
- **Parity guard:** if any existing test's offsets/steps/halt change, a sink is doing more than observing — revert to observe-only. The default-null-sink paths must never differ from pre-change behavior.

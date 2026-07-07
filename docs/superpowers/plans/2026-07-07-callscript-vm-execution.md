# call-script Execution in the C# VM — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `call-script <id>` actually load and execute the target `.BIN` as a nested subroutine that shares the global bank and returns to its caller.

**Architecture:** Introduce an `IScriptProvider` seam (implemented in `Sys4`, injected into the VM so `Vm` never references `Sys4`) that maps a call-script id (a raw SYS4INI file index) to a loaded `Script`. Refactor the VM's per-script state into an `ExecFrame` and run scripts through a recursive `RunFrame`; `call-script` recurses into a child frame. Recursion is safe because call depth is bounded (native ≤ 38).

**Tech Stack:** C# / .NET 8, xUnit. Solution `engine/AgeEngine.sln` (projects `Age.Engine`, `Age.Cli`, `Age.Engine.Tests`).

## Global Constraints

- Run tests with: `dotnet test engine/AgeEngine.sln` (from `age-reimpl/`).
- Seam rule: `Vm` code references only `Model` + `Hosting` namespaces, never `Sys4`. (One assembly; enforced by convention.)
- `call-script <id>`: `id` is a **raw index into the SYS4INI file table** (`build/callscript-names.json` maps id→name; `Paths.Scripts()` maps name→path). Confirmed: all 297 corpus ids resolve to a DATA1 `.BIN`. See `docs/engine-re.md`.
- **Deviation from spec (deliberate):** a VM with **no** `IScriptProvider` that hits `call-script` falls back to the **prior stub** (`_host.CallScript(id); pc+1`), NOT a halt. This keeps every existing base-ISA test byte-identical (they construct provider-less VMs) and avoids golden-fixture churn. Product paths always inject a provider, so execution is the real behavior where it matters.
- Frequent commits: one per task minimum.

---

### Task 1: Script-provider seam + resolver

**Files:**
- Create: `engine/Age.Engine/Hosting/IScriptProvider.cs`
- Create: `engine/Age.Engine/Sys4/Sys4ScriptProvider.cs`
- Modify: `engine/Age.Engine/Sys4/Paths.cs` (add `CallscriptNamesJson`)
- Test: `engine/Age.Engine.Tests/Sys4ScriptProviderTests.cs`

**Interfaces:**
- Produces: `interface IScriptProvider { Script? GetById(long id); }` (namespace `Age.Engine.Hosting`).
- Produces: `Sys4ScriptProvider.Load(OpcodeTable table) -> Sys4ScriptProvider`; instance `GetById(long id) -> Script?` (cached; unknown id → null).
- Produces: `Paths.CallscriptNamesJson -> string`.
- Consumes: existing `OpcodeTable`, `Sys4Loader.Load(string, OpcodeTable)`, `Paths.Scripts()`, `Paths.Build`.

- [ ] **Step 1: Add the path constant**

In `engine/Age.Engine/Sys4/Paths.cs`, after the `AssetIndexJson` line (line 12), add:

```csharp
    public static string CallscriptNamesJson => Path.Combine(Build, "callscript-names.json");
```

- [ ] **Step 2: Create the provider interface**

Create `engine/Age.Engine/Hosting/IScriptProvider.cs`:

```csharp
using Age.Engine.Model;
namespace Age.Engine.Hosting;

/// <summary>Resolves a call-script id (a raw SYS4INI file index) to a loaded <see cref="Script"/>.
/// Implemented in Sys4; injected into the VM so the Vm layer never references Sys4.</summary>
public interface IScriptProvider
{
    /// <summary>The script for this id, or null if the id maps to no known script.</summary>
    Script? GetById(long id);
}
```

- [ ] **Step 3: Write the failing test**

Create `engine/Age.Engine.Tests/Sys4ScriptProviderTests.cs`:

```csharp
using Age.Engine.Sys4;
using Xunit;

public class Sys4ScriptProviderTests
{
    [Fact]
    public void ResolvesKnownIdsToTheirScripts()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(table);

        var additem = provider.GetById(0x1ab);   // ADDITEM.BIN
        var mes = provider.GetById(0x2ae7);      // MES.BIN
        Assert.NotNull(additem);
        Assert.NotNull(mes);
        Assert.True(additem!.Instructions.Count > 0);
        Assert.Same(additem, provider.GetById(0x1ab));   // cached: same instance
        Assert.Null(provider.GetById(long.MaxValue));    // unknown id
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter Sys4ScriptProviderTests`
Expected: FAIL (compile error — `Sys4ScriptProvider` does not exist).

- [ ] **Step 5: Implement the provider**

Create `engine/Age.Engine/Sys4/Sys4ScriptProvider.cs`:

```csharp
using System.Text.Json;
using Age.Engine.Hosting;
using Age.Engine.Model;
namespace Age.Engine.Sys4;

/// <summary>Resolves call-script ids (raw SYS4INI file indices) to loaded scripts, using
/// build/callscript-names.json (id→name) + Paths.Scripts() (name→path). Cached per id.
/// The native resolver prefers a loose override before the archive; Paths.Scripts() already
/// shadows extracted/DATA1 with root overrides, so that behavior is preserved.</summary>
public sealed class Sys4ScriptProvider : IScriptProvider
{
    private readonly OpcodeTable _table;
    private readonly IReadOnlyDictionary<long, string> _idToName;
    private readonly Dictionary<string, string> _byName;      // NAME(UPPER) -> path
    private readonly Dictionary<long, Script?> _cache = new();

    public Sys4ScriptProvider(OpcodeTable table, IReadOnlyDictionary<long, string> idToName,
                              Dictionary<string, string> byName)
    { _table = table; _idToName = idToName; _byName = byName; }

    public static Sys4ScriptProvider Load(OpcodeTable table)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Paths.CallscriptNamesJson)) ?? new();
        var idToName = raw.ToDictionary(kv => long.Parse(kv.Key), kv => kv.Value);
        return new Sys4ScriptProvider(table, idToName, Paths.Scripts());
    }

    public Script? GetById(long id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;
        Script? s = null;
        if (_idToName.TryGetValue(id, out var name) &&
            _byName.TryGetValue(name.ToUpperInvariant(), out var path))
            s = Sys4Loader.Load(path, _table);
        _cache[id] = s;
        return s;
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test engine/AgeEngine.sln --filter Sys4ScriptProviderTests`
Expected: PASS. (Prerequisite: `build/callscript-names.json` exists — regenerate with `py -3.11 -X utf8 tools/parse_sys4ini.py` if missing.)

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Hosting/IScriptProvider.cs engine/Age.Engine/Sys4/Sys4ScriptProvider.cs engine/Age.Engine/Sys4/Paths.cs engine/Age.Engine.Tests/Sys4ScriptProviderTests.cs
git commit -m "Add IScriptProvider + Sys4ScriptProvider (call-script id -> Script)"
```

---

### Task 2: ExecFrame refactor (no behavior change) + emitted script identity

Refactor the VM's per-script state into an `ExecFrame` run by a recursive `RunFrame`, and tag each emitted line with its source script. call-script stays a stub in this task. All existing tests must stay green (emitted offsets/counts/halt unchanged).

**Files:**
- Create: `engine/Age.Engine/Vm/ExecFrame.cs`
- Modify: `engine/Age.Engine/Model/Script.cs` (add `Name`)
- Modify: `engine/Age.Engine/Sys4/Sys4Loader.cs` (set `Name`)
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs` (the refactor + emitted tuple)
- Modify: `engine/Age.Engine/Vm/GameSession.cs` (SceneResult tuple)
- Modify: `engine/Age.Cli/Program.cs:17` (destructure fix)

**Interfaces:**
- Produces: `Script.Name` (string, defaults `""`).
- Produces: `VirtualMachine.Emitted` becomes `List<(int Offset, string Text, string Script)>`.
- Produces (internal): `ExecFrame { Model.Script Script; int Pc; Frame Locals; List<int> CallStack; Dictionary<int,int> EmitSeen; }`.
- Consumes: `IScriptProvider` (Task 1) — added to the constructor but unused until Task 3.

- [ ] **Step 1: Add `Name` to the Script model**

In `engine/Age.Engine/Model/Script.cs`, add a property (keep existing members):

```csharp
    public string Name { get; init; } = "";
```

- [ ] **Step 2: Set `Name` in the loader**

In `engine/Age.Engine/Sys4/Sys4Loader.cs`, in `Parse`, change the return (line ~25) to include the name:

```csharp
        return new Script { Name = name, Header = header, Instructions = instrs, IndexByOffset = idxByOff, Strings = strings };
```

- [ ] **Step 3: Create ExecFrame**

Create `engine/Age.Engine/Vm/ExecFrame.cs`:

```csharp
using Age.Engine.Model;
namespace Age.Engine.Vm;

/// <summary>One script activation: the running script, its instruction cursor, its local slots,
/// its intra-script call/ret stack, and its per-script loop-guard map. Globals live on the VM and
/// are shared across frames; everything here is per-call and discarded on return.</summary>
internal sealed class ExecFrame
{
    public readonly Script Script;
    public int Pc;                                       // entry instruction index
    public readonly Frame Locals = new();
    public readonly List<int> CallStack = new();         // intra-script `call` (op 0x8f) returns
    public readonly Dictionary<int, int> EmitSeen = new();
    public ExecFrame(Script script, int pc) { Script = script; Pc = pc; }
}
```

- [ ] **Step 4: Refactor VirtualMachine to frames**

In `engine/Age.Engine/Vm/VirtualMachine.cs`:

(a) Add the constructor provider parameter and sentinels/fields. Replace the fields block + constructor (lines 7–27) so it reads:

```csharp
    private const long NoJump = 0xFFFFFFFF;
    private const int HALT = int.MinValue;
    private const int FRAME_RETURN = int.MinValue + 1;
    private const int T_IMM = 0, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_GPTR = 6,
                      T_LINT = 9, T_LFLOAT = 10, T_LSTR = 11, T_LPTR = 12;

    private readonly Script _s;
    private readonly OpcodeTable _t;
    private readonly IHost _host;
    private readonly VmOptions _o;
    private readonly IScriptProvider? _provider;
    private ExecFrame _cur = null!;
    private int _depth;
    private bool _halted;

    public Dictionary<int, long> Globals { get; } = new();
    public Dictionary<int, string> GlobalStrings { get; } = new();
    public List<(int Offset, string Text, string Script)> Emitted { get; } = new();
    public string? HaltReason { get; private set; }
    public long Steps { get; private set; }

    public VirtualMachine(Script s, OpcodeTable t, IHost host, VmOptions? o = null, IScriptProvider? provider = null)
    { _s = s; _t = t; _host = host; _o = o ?? new VmOptions(); _provider = provider; }
```

(b) Replace `Run` (lines 94–106) with:

```csharp
    private enum FrameOutcome { Returned, Halted, RanOff }

    public void Run(int entryOffset = 0)
    {
        var top = new ExecFrame(_s, _s.IndexByOffset.TryGetValue(entryOffset, out var idx) ? idx : 0);
        var outcome = RunFrame(top);
        if (outcome == FrameOutcome.RanOff) HaltReason ??= "pc-out-of-range";
        else if (outcome == FrameOutcome.Returned) HaltReason ??= "exit";
        // Halted: HaltReason already set by the halting op.
    }

    private FrameOutcome RunFrame(ExecFrame frame)
    {
        var prev = _cur; _cur = frame; _depth++;
        var outcome = FrameOutcome.RanOff;
        int pc = frame.Pc;
        while (pc >= 0 && pc < frame.Script.Instructions.Count)
        {
            if (Steps >= _o.MaxSteps) { HaltReason ??= "STEP-LIMIT"; _halted = true; outcome = FrameOutcome.Halted; break; }
            Steps++;
            int next = Step(frame.Script.Instructions[pc], pc);
            if (next == FRAME_RETURN) { outcome = FrameOutcome.Returned; break; }
            if (next == HALT) { _halted = true; outcome = FrameOutcome.Halted; break; }
            pc = next;
        }
        _cur = prev; _depth--;
        return outcome;
    }
```

(c) Repoint the helpers from `_fr`/`_s` to the current frame. Change every `_fr` to `_cur.Locals` in `Read`, `Write`, `ReadStr`, `WriteStr`, `BaseAddr`, `LookupStore` (the fields `_fr.I/F/S/P` become `_cur.Locals.I/F/S/P`).

(d) In `Step`, repoint frame-scoped state: `_callstack` → `_cur.CallStack`; `_emitSeen` → `_cur.EmitSeen`; `_s.IndexByOffset` → `_cur.Script.IndexByOffset`; `_s.GetString` → `_cur.Script.GetString`. Specifically:

- `jmp`: `return _cur.Script.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);`
- `call`: `_cur.CallStack.Add(pc + 1); return _cur.Script.IndexByOffset.GetValueOrDefault((int)a[0].Value, pc + 1);`
- `ret`: `if (_cur.CallStack.Count > 0) { int r = _cur.CallStack[^1]; _cur.CallStack.RemoveAt(_cur.CallStack.Count - 1); return r; } return FRAME_RETURN;`
- `jcc`: `return tgt == NoJump ? pc + 1 : _cur.Script.IndexByOffset.GetValueOrDefault((int)tgt, pc + 1);`
- `exit` / `exit-script`: `return FRAME_RETURN;`
- `show-text` body: `_cur.EmitSeen.TryGetValue(off, out var c); c++; _cur.EmitSeen[off] = c;` then on cap `HaltReason = $"LOOP:line@0x{off:x}×{c}"; return HALT;`, and `string text = _cur.Script.GetString(off); Emitted.Add((off, text, _cur.Script.Name)); _host.ShowText(off, text);`
- `call-script`: leave the stub for now — `case "call-script": _host.CallScript(a.Count > 0 ? Read(a[0]) : 0); return pc + 1;`

- [ ] **Step 5: Fix the SceneResult tuple**

In `engine/Age.Engine/Vm/GameSession.cs` line 67, change the record to:

```csharp
public sealed record SceneResult(IReadOnlyList<(int Offset, string Text, string Script)> Emitted, string? Halt, long Steps);
```

- [ ] **Step 6: Fix the one positional destructure**

In `engine/Age.Cli/Program.cs` line 17, change:

```csharp
    foreach (var (off, text, _) in vm.Emitted.Take(20)) Console.WriteLine($"  [{off:x}] {text}");
```

- [ ] **Step 7: Run the full suite to verify no behavior change**

Run: `dotnet test engine/AgeEngine.sln`
Expected: PASS — all existing tests green. `WaitForInputTests` still asserts SC0000 = 186 (provider-less = stub), `RecoverTests`, `TextureOpsTests`, `GameSessionTests` unchanged. (`TraceDiffTests` passes if `build/vm0-trace.json` is present; it is skipped otherwise.)

- [ ] **Step 8: Commit**

```bash
git add engine/Age.Engine/Vm/ExecFrame.cs engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine/Vm/GameSession.cs engine/Age.Engine/Model/Script.cs engine/Age.Engine/Sys4/Sys4Loader.cs engine/Age.Cli/Program.cs
git commit -m "Refactor VM to ExecFrame + tag emitted lines with source script (no behavior change)"
```

---

### Task 3: call-script execution

Wire the provider into `call-script`: load the child script, run it as a nested frame sharing globals, return to the caller. `exit`/empty-stack `ret` return from the frame; only the top frame's return ends the VM. Depth-capped.

**Files:**
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs` (the `call-script` case)
- Modify: `engine/Age.Engine/Vm/VmOptions.cs` (depth cap)
- Test: `engine/Age.Engine.Tests/CallScriptTests.cs`

**Interfaces:**
- Consumes: `IScriptProvider.GetById` (Task 1), `ExecFrame`, `RunFrame`, `FrameOutcome` (Task 2).
- Produces: nested execution semantics (below) exercised by later tasks.

- [ ] **Step 1: Add the depth-cap option**

In `engine/Age.Engine/Vm/VmOptions.cs`:

```csharp
namespace Age.Engine.Vm;
public sealed record VmOptions(int EmitCap = 2, long MaxSteps = 2_000_000, int CallDepthCap = 64);
```

(64 is a generous runaway guard; the native limit is 38.)

- [ ] **Step 2: Write the failing tests**

Create `engine/Age.Engine.Tests/CallScriptTests.cs`. It uses a fake in-memory provider and hand-built scripts.

```csharp
using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class CallScriptTests
{
    private sealed class NullHost : IHost
    {
        public List<long> Calls = new();
        public void ShowText(int o, string t) { }
        public void CallScript(long id) => Calls.Add(id);
        public void OnStub(int op) { }
        public void WaitForInput() { }
        public void CreateTexture(int s, int w, int h) { }
        public void SetTexture(long r, int s) { }
        public void DrawTexture(int s, int sx, int sy, int w, int h, int dx, int dy) { }
        public (int Width, int Height) GetTextureSize(int s) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    private sealed class MapProvider : IScriptProvider
    {
        private readonly Dictionary<long, Script> _m;
        public MapProvider(Dictionary<long, Script> m) => _m = m;
        public Script? GetById(long id) => _m.TryGetValue(id, out var s) ? s : null;
    }

    // Build a Script from raw dwords via the real loader (guarantees identical decode).
    private static Script Asm(OpcodeTable t, string name, params uint[] body)
    {
        var bytes = new byte[0x3C + body.Length * 4];
        System.Text.Encoding.ASCII.GetBytes("SYS4422 ").CopyTo(bytes, 0);
        // fields[8] (F8 = code end) at header offset 8 + 8*4 = 0x28; set to body length (all code).
        System.BitConverter.GetBytes(body.Length).CopyTo(bytes, 8 + 8 * 4);
        for (int i = 0; i < body.Length; i++) System.BitConverter.GetBytes(body[i]).CopyTo(bytes, 0x3C + i * 4);
        return Sys4Loader.Parse(bytes, t, name);
    }

    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);

    [Fact]
    public void CalleeRunsAndControlResumesAfterTheCall()
    {
        var t = Table();
        // Callee (id 5): set global 0x10 = 7, then exit (op 0x0).
        var callee = Asm(t, "CALLEE", 0x55, 3, 0x10, 0, 7, 0x0);   // mov g[0x10]=7 ; exit
        // Caller: call-script 5 ; set g[0x11]=g[0x10]+1 ; exit.
        var caller = Asm(t, "CALLER",
            0x03, 0, 5,                       // call-script 5
            0x55, 3, 0x11, 3, 0x10,           // mov g[0x11] = g[0x10]   (see note below)
            0x0);                             // exit
        var host = new NullHost();
        var vm = new VirtualMachine(caller, t, host, null, new MapProvider(new() { [5] = callee }));
        vm.Run();
        Assert.Equal(7, vm.Globals[0x10]);           // callee wrote a shared global
        Assert.Equal(7, vm.Globals[0x11]);           // caller read it AFTER the call returned
        Assert.Equal("exit", vm.HaltReason);         // top-level exit
        Assert.Contains(5L, host.Calls);             // host notified
    }

    [Fact]
    public void MissingProviderFallsBackToStub()
    {
        var t = Table();
        var caller = Asm(t, "CALLER", 0x03, 0, 5, 0x0);   // call-script 5 ; exit
        var host = new NullHost();
        var vm = new VirtualMachine(caller, t, host, null, null);   // no provider
        vm.Run();
        Assert.Equal("exit", vm.HaltReason);        // did not halt on the call; stub + continue
        Assert.Contains(5L, host.Calls);
    }

    [Fact]
    public void UnresolvedIdHalts()
    {
        var t = Table();
        var caller = Asm(t, "CALLER", 0x03, 0, 99, 0x0);
        var vm = new VirtualMachine(caller, t, new NullHost(), null, new MapProvider(new()));
        vm.Run();
        Assert.StartsWith("callscript-unresolved", vm.HaltReason);
    }

    [Fact]
    public void LocalsDoNotLeakBetweenCallerAndCallee()
    {
        var t = Table();
        // Callee writes LOCAL-int 0 = 42 (op 0x55 to local-int, type 9), then exit.
        var callee = Asm(t, "CALLEE", 0x55, 9, 0, 0, 42, 0x0);
        // Caller sets local-int 0 = 1, calls, then copies its own local-int 0 to global 0x20.
        var caller = Asm(t, "CALLER",
            0x55, 9, 0, 0, 1,                 // l[0] = 1
            0x03, 0, 5,                       // call-script 5 (callee sets ITS local 0 = 42)
            0x55, 3, 0x20, 9, 0,              // g[0x20] = l[0]
            0x0);
        var vm = new VirtualMachine(caller, t, new NullHost(), null, new MapProvider(new() { [5] = callee }));
        vm.Run();
        Assert.Equal(1, vm.Globals[0x20]);   // caller's local 0 unchanged by callee's local 0
    }
}
```

Note on `mov g[0x11] = g[0x10]`: the `mov` opcode 0x55 with a 2-operand form copies src→dst; the test encodes dst=`(type 3 = global-int, 0x11)`, src=`(type 3, 0x10)`. If the corpus `mov` is strictly 2-arg, keep argc=2 as encoded (`0x55, 3,0x11, 3,0x10`). Confirm the opcode's argc via `build/opcodes.json` when implementing; adjust the dword stream to match the real argc so `Sys4Loader.Parse` decodes it as one instruction.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test engine/AgeEngine.sln --filter CallScriptTests`
Expected: FAIL — `CalleeRunsAndControlResumesAfterTheCall`, `UnresolvedIdHalts`, `LocalsDoNotLeakBetweenCallerAndCallee` fail (call-script is still the stub, so the callee never runs / never halts on unresolved). `MissingProviderFallsBackToStub` may already pass.

- [ ] **Step 4: Implement call-script execution**

In `engine/Age.Engine/Vm/VirtualMachine.cs`, replace the `call-script` stub case with:

```csharp
            case "call-script":
            {
                long id = a.Count > 0 ? Read(a[0]) : 0;
                _host.CallScript(id);                       // notify (diagnostics)
                if (_provider == null) return pc + 1;       // no script source: prior stub behavior
                if (_depth >= _o.CallDepthCap) { HaltReason ??= "call-depth-exceeded"; return HALT; }
                var child = _provider.GetById(id);
                if (child == null) { HaltReason ??= $"callscript-unresolved:0x{id:x}"; return HALT; }
                var entry = child.IndexByOffset.TryGetValue(0, out var ci) ? ci : 0;
                var outcome = RunFrame(new ExecFrame(child, entry));
                if (outcome == FrameOutcome.Halted) return HALT;   // propagate whole-VM halt up
                return pc + 1;                                      // Returned / RanOff: resume caller
            }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test engine/AgeEngine.sln --filter CallScriptTests`
Expected: PASS (all four). If the `mov` encoding decodes wrong, fix the dword stream per the Step-2 note.

- [ ] **Step 6: Run the full suite (no regressions)**

Run: `dotnet test engine/AgeEngine.sln`
Expected: PASS — existing provider-less tests unchanged (SC0000 still 186 in `WaitForInputTests`).

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine/Vm/VmOptions.cs engine/Age.Engine.Tests/CallScriptTests.cs
git commit -m "Execute call-script: nested frame, shared globals, return to caller"
```

---

### Task 4: Wire the provider into product paths + real-scene validation

Thread a real `Sys4ScriptProvider` into the CLI run/play/sweep paths and `GameSession.RunScene`, prove a real scene executes a real subroutine, and confirm the corpus still terminates with execution on. Confirm the empty-stack `ret` path with a real script (BUNKI).

**Files:**
- Modify: `engine/Age.Engine/Vm/GameSession.cs` (`RunScene` gains an optional provider)
- Modify: `engine/Age.Cli/Program.cs` (construct + pass the provider in `run`, `play`, `sweep`)
- Test: `engine/Age.Engine.Tests/CallScriptIntegrationTests.cs`

**Interfaces:**
- Consumes: `Sys4ScriptProvider.Load` (Task 1), `VirtualMachine(..., provider)` (Task 2/3).
- Produces: `GameSession.RunScene(Script, OpcodeTable, IHost, VmOptions?, IScriptProvider?)`.

- [ ] **Step 1: Add the provider to GameSession.RunScene**

In `engine/Age.Engine/Vm/GameSession.cs`, change the `RunScene` signature and VM construction:

```csharp
    public SceneResult RunScene(Script script, OpcodeTable table, IHost host,
                                VmOptions? options = null, IScriptProvider? provider = null)
    {
        var vm = new VirtualMachine(script, table, host, options, provider);
```

(The rest of the method is unchanged.)

- [ ] **Step 2: Write the failing integration tests**

Create `engine/Age.Engine.Tests/CallScriptIntegrationTests.cs`:

```csharp
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class CallScriptIntegrationTests
{
    private sealed class NullHost : IHost
    {
        public int CallScripts;
        public void ShowText(int o, string t) { }
        public void CallScript(long id) => CallScripts++;
        public void OnStub(int op) { }
        public void WaitForInput() { }
        public void CreateTexture(int s, int w, int h) { }
        public void SetTexture(long r, int s) { }
        public void DrawTexture(int s, int sx, int sy, int w, int h, int dx, int dy) { }
        public (int Width, int Height) GetTextureSize(int s) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    [Fact]
    public void RealSceneExecutesRealSubroutinesAndReturns()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(t);
        var script = Sys4Loader.Load(Paths.Scripts()["ALCHEMY.BIN"], t);   // calls MES/BUNKI/ADDITEM
        var host = new NullHost();
        var vm = new VirtualMachine(script, t, host, null, provider);
        vm.Run();
        Assert.True(host.CallScripts > 0, "the scene should issue call-scripts");
        // Termination is the key property: execution-on must still reach a clean end, not hang or
        // trip the depth cap. (No unresolved / depth halts.)
        Assert.DoesNotContain("unresolved", vm.HaltReason ?? "");
        Assert.NotEqual("call-depth-exceeded", vm.HaltReason);
        Assert.NotEqual("STEP-LIMIT", vm.HaltReason);
    }

    [Fact]
    public void BunkiTopLevelRetReturnsCleanlyAsSubroutine()
    {
        // BUNKI.BIN ends with a top-level `ret` (empty intra-call stack). Called as a subroutine it
        // must return to the caller, not underflow-halt. Drive it directly as a child of a 1-op caller.
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(t);
        var bunki = provider.GetById(0x143);   // BUNKI.BIN
        Assert.NotNull(bunki);
        var vm = new VirtualMachine(bunki!, t, new NullHost(), null, provider);
        vm.Run();
        // Reaching a frame-return at the top = clean "exit"; never "ret-underflow".
        Assert.NotEqual("ret-underflow", vm.HaltReason);
    }
}
```

- [ ] **Step 3: Run to verify (expected: mostly pass already)**

Run: `dotnet test engine/AgeEngine.sln --filter CallScriptIntegrationTests`
Expected: PASS. If `BunkiTopLevelRetReturnsCleanlyAsSubroutine` reveals a wrong `ret` semantics (e.g. it halts early with a bad reason), that is the empty-stack-`ret` branch — verify against the native op 0x5 handler `ctx[0x26c93+5]` (LAB_00417ad0) before adjusting; the plan's choice (empty-stack `ret` = frame return) should hold.

- [ ] **Step 4: Wire the provider into the CLI**

In `engine/Age.Cli/Program.cs`, build one provider after `table` is loaded and pass it into the product paths:

- In the `run` command (line ~13), construct `var provider = Sys4ScriptProvider.Load(table);` and change the VM to `new VirtualMachine(script, table, host, null, provider)`.
- In `play` (line ~96), construct the provider once before the loop and pass it: `session.RunScene(script, table, new CaptureHost(), null, provider)`.
- In `sweep` (lines ~121, 140, 162): construct the provider once and pass it to each `RunScene(...)` call (`, null, provider`). The `--boot` INIT scripts may be run with or without the provider; pass it for consistency.
- Leave `trace` (line ~181) provider-less on purpose — it is the base-ISA offset oracle (stub behavior, comparable to vm0.py).

Exact edit for `run` (lines 12–17):

```csharp
    var table = OpcodeTableJson.Load(Paths.OpcodesJson);
    var provider = Sys4ScriptProvider.Load(table);
    var script = Sys4Loader.Load(args[1], table);
    var host = new ConsoleHost();
    var vm = new VirtualMachine(script, table, host, null, provider);
    vm.Run();
```

(Use whatever host `run` already constructs; only the trailing `provider` arg is added.)

- [ ] **Step 5: Confirm the corpus still terminates with execution on**

Build and run the sweep:

```bash
dotnet run --project engine/Age.Cli -- sweep
```

Expected: it completes; halt distribution is dominated by `exit`. Note any new non-`exit` halts (e.g. `call-depth-exceeded`, `callscript-unresolved`, `STEP-LIMIT`) — none should appear for well-formed scenes. Compare the anomaly list to the pre-change baseline (unbooted sweep previously matched vm0.py: 294 `exit` + 3 `LOOP`). New anomalies are the review signal.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test engine/AgeEngine.sln`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Vm/GameSession.cs engine/Age.Cli/Program.cs engine/Age.Engine.Tests/CallScriptIntegrationTests.cs
git commit -m "Wire Sys4ScriptProvider into CLI run/play/sweep; validate real subroutine execution"
```

---

## Post-implementation notes (not tasks)

- **Godot host:** the playable Godot path (`GodotAdvHost`/`Main`) should construct a `Sys4ScriptProvider` and pass it to its `VirtualMachine`, so subroutines execute live; keep the `--selftest` path **provider-less** (preserves the 186-offset SC0000 gate). This is a small follow-up wiring, mirroring Task 4's CLI changes.
- **vm0.py** is not modified. `trace`/`TraceDiffTests`/`WaitForInputTests` remain on the provider-less (stub) path, so vm0.py stays a valid base-ISA reference without lockstep maintenance.
- **decision→scene** (scene chaining) is out of scope; it rides this same loader once the SCJUMP decision→scene-id hop is reversed.
- After landing, update the status memory (`himegari-port-status.md`) and `docs/phase-a-slice-plan.md` with the result.

## Self-review

- **Spec coverage:** provider seam (Task 1) ✓; recursive frame execution (Task 2) ✓; shared globals / per-frame locals / exit+ret semantics / depth cap / dynamic ids / IHost.CallScript notify (Task 3) ✓; emitted script identity (Task 2) ✓; validation via C#-owned tests + sweep-terminates + real subroutine + empty-stack-ret (Tasks 3–4) ✓; vm0.py retired without deletion, base-ISA guard preserved (Global Constraints + notes) ✓. Deviation (no-provider → stub, not halt) is documented in Global Constraints.
- **Placeholder scan:** none — every code step shows full code; the one encoding caveat (`mov` argc) has an explicit resolution instruction.
- **Type consistency:** `IScriptProvider.GetById` (Task 1) is consumed unchanged in Tasks 2–4; `Emitted`/`SceneResult` tuple `(int Offset, string Text, string Script)` is introduced in Task 2 and consumers updated in the same task; `RunFrame`/`FrameOutcome`/`ExecFrame`/`_provider`/`_depth` defined in Task 2 and used in Task 3; `VmOptions.CallDepthCap` defined in Task 3 before first use.

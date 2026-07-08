# Frame-stepped VM Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the Godot port's VM from outrunning real time (the SC0000 opening "speeds through") by throttling interpreter execution to a bounded wall-clock op rate, so visible state changes over real time like the native engine.

**Architecture:** Keep the VM on its background thread (unchanged). Add ONE host call per executed opcode — `IHost.FrameYield()`. Headless hosts no-op it (byte-identical parity). The Godot host throttles: it counts ops and, once a per-frame budget is reached, blocks the VM thread until the main-thread `_Process` advances a host-owned `FrameClock`. That same clock drives `Sleep` and the anim tween, so a future single `Speed` multiplier (Ctrl fast-forward, NOT built now) scales everything coherently.

**Tech Stack:** C# / .NET 8 (`engine/AgeEngine.sln`), xUnit tests, Godot 4.7 mono (`godot/Himegari.csproj`).

**Spec:** `docs/superpowers/specs/2026-07-08-frame-stepped-vm-design.md` (read it — it has the RE evidence: the native interpreter runs ~1,788 ops/sec normal, ~4× under Ctrl; rendering is uncapped D3D9, so the pacing quantity is a wall-clock op rate, not a display-frame budget).

## Global Constraints

- **Parity is sacred.** All non-Godot hosts must keep headless output byte-identical. Regression gates that MUST stay green unchanged: the engine test suite (`dotnet test engine/AgeEngine.sln`), `Age.Cli sweep` (reports `exit=284, STEP-LIMIT=13`), and the Godot `--selftest` (`SELFTEST OK`). `FrameYield`/`Sleep` are no-ops in every non-Godot host, which is what guarantees this.
- **Seam rule:** `Age.Engine/Vm` may reference only `Model`, `Hosting`, `Diagnostics` — never `Sys4`. `FrameClock` lives in `Age.Engine/Hosting` (used by hosts, not by `Vm`).
- **No Ctrl wiring.** `FrameClock.Speed` stays `1.0` (a field, the future hook). Do NOT add a key handler or change Speed. When eventually wired it must be ADV-mode-scoped — do not bake in a global-speed assumption.
- **Build/run C#:** `dotnet build engine/AgeEngine.sln -c Debug`; `dotnet test engine/AgeEngine.sln`. Godot: `dotnet build godot/Himegari.csproj -c Debug` then the console exe at `S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe` (or `pwsh run-godot.ps1`).
- **Python (if needed):** `py -3.11 -X utf8 tools/<name>.py`.
- TDD, one deliverable per task, commit at the end of each task. End every commit message with:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

---

## Task 0: Branch

- [ ] **Step 1: Create the feature branch** (repo root = `age-reimpl/`, git repo; default branch is `main`)

```bash
cd "S:/Game Hacking/Eushully/Himegari/age-reimpl"
git checkout -b feat/frame-stepped-vm
```

---

## File Structure

- `engine/Age.Engine/Hosting/FrameClock.cs` — **new**. Pure virtual clock: `NowMs`, `Speed`, `OpsPerFrame`, `Advance()`, `EffectiveBudget`. No threading. Task 1.
- `engine/Age.Engine/Hosting/IHost.cs` — **modify**. Add `void FrameYield()`. Task 2.
- `engine/Age.Engine/Vm/VirtualMachine.cs` — **modify**. Call `_host.FrameYield()` once per executed opcode. Task 2.
- `engine/Age.Engine/Hosting/CaptureHost.cs` + every other non-Godot `IHost` implementer — **modify**. Add empty `FrameYield()`. Task 2.
- `godot/GodotAdvHost.cs` — **modify**. Real `FrameYield()` throttle + `Sleep()` on the clock. Task 3.
- `godot/Main.cs` — **modify**. Own a `FrameClock`, advance + pulse it each `_Process`, tween reads it. Task 3.
- `engine/Age.Engine.Tests/FrameClockTests.cs` — **new**. Task 1.
- `engine/Age.Engine.Tests/FrameYieldTests.cs` — **new**. Task 2.

---

## Task 1: `FrameClock` — pure virtual clock + op budget

**Files:**
- Create: `engine/Age.Engine/Hosting/FrameClock.cs`
- Test: `engine/Age.Engine.Tests/FrameClockTests.cs`

**Interfaces:**
- Produces: `Age.Engine.Hosting.FrameClock` with `long NowMs { get; }`, `double Speed` (field, default 1.0), `int OpsPerFrame` (field, default 30), `void Advance(double realDeltaSeconds)`, `int EffectiveBudget { get; }`.

- [ ] **Step 1: Write the failing test**

Create `engine/Age.Engine.Tests/FrameClockTests.cs`:

```csharp
using Age.Engine.Hosting;
using Xunit;

public class FrameClockTests
{
    [Fact]
    public void Advance_AtSpeed1_AddsRealMilliseconds()
    {
        var c = new FrameClock();               // Speed defaults to 1.0
        c.Advance(0.016);                        // one ~60fps frame
        Assert.Equal(16, c.NowMs);
    }

    [Fact]
    public void Advance_ScalesBySpeed()
    {
        var c = new FrameClock { Speed = 4.0 };
        c.Advance(0.016);
        Assert.Equal(64, c.NowMs);               // 4x virtual time
    }

    [Fact]
    public void EffectiveBudget_ScalesBySpeed_AndFloorsAtOne()
    {
        Assert.Equal(30,  new FrameClock { OpsPerFrame = 30, Speed = 1.0 }.EffectiveBudget);
        Assert.Equal(120, new FrameClock { OpsPerFrame = 30, Speed = 4.0 }.EffectiveBudget);
        Assert.Equal(1,   new FrameClock { OpsPerFrame = 0,  Speed = 1.0 }.EffectiveBudget);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter FrameClockTests`
Expected: FAIL — `FrameClock` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `engine/Age.Engine/Hosting/FrameClock.cs`:

```csharp
namespace Age.Engine.Hosting;

/// <summary>Host-owned virtual clock + per-frame op budget. Pure (no threading): the Godot host
/// advances it once per rendered frame and consults it to pace the VM. The one <see cref="Speed"/>
/// factor is the future (unwired) Ctrl fast-forward multiplier — scaling it scales the throttle
/// budget, sleeps, and the anim tween together. See docs/superpowers/specs/2026-07-08-frame-stepped-vm-design.md.</summary>
public sealed class FrameClock
{
    /// <summary>Monotonic virtual time in milliseconds (scaled by Speed).</summary>
    public long NowMs { get; private set; }

    /// <summary>Speed multiplier. 1.0 = normal. The future Ctrl hook (ADV-scoped); leave at 1.0 for now.</summary>
    public double Speed = 1.0;

    /// <summary>Base per-frame interpreter op budget (tunable by eye; ~30 ≈ 1,800 ops/sec at 60fps).</summary>
    public int OpsPerFrame = 30;

    /// <summary>Advance the clock by one rendered frame's real delta (seconds), scaled by Speed.</summary>
    public void Advance(double realDeltaSeconds) => NowMs += (long)(realDeltaSeconds * 1000.0 * Speed);

    /// <summary>Ops the VM may run before yielding a frame, scaled by Speed (min 1).</summary>
    public int EffectiveBudget => System.Math.Max(1, (int)System.Math.Round(OpsPerFrame * Speed));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test engine/AgeEngine.sln --filter FrameClockTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add engine/Age.Engine/Hosting/FrameClock.cs engine/Age.Engine.Tests/FrameClockTests.cs
git commit -m "feat: add FrameClock (virtual clock + per-frame op budget)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `IHost.FrameYield()` — per-op hook + headless no-ops + parity

**Files:**
- Modify: `engine/Age.Engine/Hosting/IHost.cs`
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs` (the `RunFrame` loop, ~lines 119-128)
- Modify: `engine/Age.Engine/Hosting/CaptureHost.cs` and every other non-Godot `IHost` implementer
- Test: `engine/Age.Engine.Tests/FrameYieldTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 yet (the Godot host uses `FrameClock` in Task 3).
- Produces: `IHost.FrameYield()` (called by the VM exactly once per executed opcode, including the halting/returning op). Headless implementers make it a no-op.

- [ ] **Step 1: Find every `IHost` implementer** (so the build won't break)

Run: `py -3.11 -X utf8 -c "import subprocess"` is not needed — just grep:
Use the Grep tool for `: IHost` and `IHost` across `engine/` — expected implementers to edit in this task:
`engine/Age.Engine/Hosting/CaptureHost.cs`, the test hosts in `engine/Age.Engine.Tests/` (e.g. recording/counting hosts used by existing tests), and the CLI hosts in `engine/Age.Cli/Program.cs` (`AudioTraceHost`, `GfxTraceHost`). **`godot/GodotAdvHost.cs` is handled in Task 3** (separate project, not in `AgeEngine.sln`).

- [ ] **Step 2: Write the failing test**

Create `engine/Age.Engine.Tests/FrameYieldTests.cs`. This asserts the VM calls `FrameYield` exactly once per step (`Steps`), proving the per-op wiring, using a tiny counting host. It loads a real boot script that halts cleanly.

```csharp
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class FrameYieldTests
{
    private sealed class CountingHost : IHost
    {
        public long Yields;
        public void FrameYield() => Yields++;
        public void ShowText(int offset, string text) { }
        public void WaitForInput() { }
        public void Sleep(long duration) { }
        public void CreateTexture(int slot, int width, int height) { }
        public void SetTexture(long resourceId, int slot) { }
        public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy) { }
        public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
    }

    [Fact]
    public void FrameYield_CalledOncePerStep()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["INITCONFIG.BIN"], table);
        var host = new CountingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Run();
        Assert.True(vm.Steps > 0);
        Assert.Equal(vm.Steps, host.Yields);   // exactly one FrameYield per executed opcode
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter FrameYieldTests`
Expected: FAIL — `IHost` has no `FrameYield` (compile error in the host), and the VM does not call it.

- [ ] **Step 4: Add `FrameYield` to the interface**

In `engine/Age.Engine/Hosting/IHost.cs`, add the method (place it next to `WaitForInput`/`Sleep`):

```csharp
    void FrameYield();
```

- [ ] **Step 5: Call it once per opcode in the VM**

In `engine/Age.Engine/Vm/VirtualMachine.cs`, the `RunFrame` loop currently reads (around lines 119-128):

```csharp
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
```

Add `_host.FrameYield();` immediately after the `Step(...)` call:

```csharp
            int next = Step(frame.Script.Instructions[pc], pc);
            _host.FrameYield();
            if (next == FRAME_RETURN) { outcome = FrameOutcome.Returned; break; }
```

(`_host` is the existing `IHost` field the VM already dispatches `ShowText`/`Sleep`/etc. through.)

- [ ] **Step 6: Add empty `FrameYield()` to every non-Godot host**

In each implementer found in Step 1, add:

```csharp
    public void FrameYield() { }
```

Concretely: `engine/Age.Engine/Hosting/CaptureHost.cs`; each test host class in `engine/Age.Engine.Tests/`; and `AudioTraceHost` + `GfxTraceHost` in `engine/Age.Cli/Program.cs`. (The `CountingHost` in Step 2 already has its own.)

- [ ] **Step 7: Run the new test — verify it passes**

Run: `dotnet test engine/AgeEngine.sln --filter FrameYieldTests`
Expected: PASS.

- [ ] **Step 8: Run the FULL suite — verify parity (nothing else changed)**

Run: `dotnet test engine/AgeEngine.sln`
Expected: PASS — all pre-existing tests still green (FrameYield is a no-op everywhere headless, so traces/steps/emitted are byte-identical).

- [ ] **Step 9: Verify the corpus oracle is unchanged**

Run: `dotnet run --project engine/Age.Cli -c Debug -- sweep`
Expected: halt distribution includes `exit=284` and `STEP-LIMIT=13` (unchanged from before this task).

- [ ] **Step 10: Commit**

```bash
git add engine/Age.Engine/Hosting/IHost.cs engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine/Hosting/CaptureHost.cs engine/Age.Cli/Program.cs engine/Age.Engine.Tests/FrameYieldTests.cs
# also add any test-host files you edited under engine/Age.Engine.Tests/
git commit -m "feat: add IHost.FrameYield per-opcode hook (no-op headless, parity held)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Godot host throttle + Sleep/tween on the clock

**Files:**
- Modify: `godot/GodotAdvHost.cs`
- Modify: `godot/Main.cs`

**Interfaces:**
- Consumes: `Age.Engine.Hosting.FrameClock` (Task 1); `IHost.FrameYield` (Task 2).
- Produces: `GodotAdvHost` throttled to `FrameClock.EffectiveBudget` ops per rendered frame; `GodotAdvHost.PulseFrame()` (called by `Main._Process` each frame).

This task changes only the Godot project, which builds separately (`godot/Himegari.csproj`) and is verified by the Godot `--selftest` + a manual windowed run (not the xUnit suite).

- [ ] **Step 1: Add the FrameClock + throttle to `GodotAdvHost`**

In `godot/GodotAdvHost.cs`:

(a) Add fields (near the existing `_gate`):

```csharp
    private readonly Age.Engine.Hosting.FrameClock _clock;
    private readonly System.Threading.AutoResetEvent _frameSignal = new(false);
    private int _opsSinceYield;
```

(b) Change the constructor to accept the clock:

```csharp
    public GodotAdvHost(Main main, ResourceMap res, string scene, Age.Engine.Hosting.FrameClock clock)
    {
        _main = main; _res = res; _scene = scene; _clock = clock;
    }
```

(c) Add the per-frame pulse (called from `Main._Process`) and the throttle. Add these methods:

```csharp
    // Main thread, once per rendered frame: releases a VM thread parked in FrameYield/Sleep.
    public void PulseFrame() => _frameSignal.Set();

    // Called once per executed opcode (IHost.FrameYield). After a frame's worth of ops (the clock's
    // budget), block the VM background thread until Main._Process advances the clock — throttling the
    // interpreter to ~budget ops per rendered frame (the native engine's rate-limited cadence).
    public void FrameYield()
    {
        if (++_opsSinceYield < _clock.EffectiveBudget) return;
        _opsSinceYield = 0;
        long start = _clock.NowMs;
        while (_clock.NowMs == start)                 // wait until a real _Process advanced the clock
            if (!_frameSignal.WaitOne(50)) break;     // 50ms safety cap: never hang if _Process stalls
    }
```

(d) Replace the existing `Sleep` body so it waits on the clock (unified timebase) instead of `Thread.Sleep`:

```csharp
    public double SleepScale = 1.0;   // --sleep-scale <f>: debug multiplier (kept)
    public void Sleep(long duration)
    {
        long ms = (long)System.Math.Clamp(duration * SleepScale, 0, 60_000);
        long deadline = _clock.NowMs + ms;
        while (_clock.NowMs < deadline)
            if (!_frameSignal.WaitOne(2000)) break;   // safety cap
    }
```

- [ ] **Step 2: Own + drive the FrameClock in `Main`**

In `godot/Main.cs`:

(a) Add a field (near the other Main fields, ~line 23):

```csharp
    private readonly Age.Engine.Hosting.FrameClock _clock = new();
```

(b) Pass it when constructing the host. The current line (~122) reads:

```csharp
        _host = new GodotAdvHost(this, ResourceMap.Load(), scene) { SleepScale = sleepScale };
```

Change to:

```csharp
        _host = new GodotAdvHost(this, ResourceMap.Load(), scene, _clock) { SleepScale = sleepScale };
```

(c) In `_Process(double delta)`, right after the existing `_lastDelta = delta;` (~line 158), advance the clock and pulse the frame:

```csharp
        _lastDelta = delta;
        _clock.Advance(delta);
        _host?.PulseFrame();
```

(d) Make the anim tween use the clock's (Speed-scaled) time so a future Speed multiplier scales it too. In `AlphaFor` (~line 281), change:

```csharp
        tw.Elapsed += _lastDelta;
```

to:

```csharp
        tw.Elapsed += _lastDelta * _clock.Speed;   // Speed==1 now => identical; future Ctrl scales the tween
```

- [ ] **Step 3: Build the Godot project**

Run: `dotnet build godot/Himegari.csproj -c Debug --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Verify parity via the headless self-test**

Run: `& "S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --headless --path godot -- --selftest`
Expected: output ends with `SELFTEST OK` (the synthetic scene still produces identical lines; the throttle changes timing, not output).

- [ ] **Step 5: Manual visual check (acceptance — user-facing)**

Run windowed with boot state:
`& "S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --path godot -- --boot`
Expected: the SC0000 opening now advances at a readable pace (no instant speed-through). If it's too slow or too fast, tune `FrameClock.OpsPerFrame` (default 30; higher = faster). This is the eyeball calibration step — the user validates and picks the final `OpsPerFrame`.

- [ ] **Step 6: Commit**

```bash
git add godot/GodotAdvHost.cs godot/Main.cs
git commit -m "feat: throttle Godot VM to a per-frame op budget on FrameClock (fixes opening speed-through)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Notes for the executor

- **If per-op `FrameYield()` overhead shows up** in a slow `dotnet test`/`sweep`, batch it: keep a counter in the VM and call `_host.FrameYield()` every K ops (e.g. 4). Parity is unaffected (still no-op headless); only the Godot throttle granularity coarsens. Not expected to be necessary.
- **Do NOT** wire Ctrl / change `Speed`. That's a separate future slice (needs the ADV-scope RE); the seam is ready for it.
- **Stretch (only if the opening still looks wrong after tuning):** confirm the 2D-composite assumption by hooking `IDirect3DDevice9::StretchRect`/`Clear` (extend `tools/frida/probe_present.py`). Not required for this plan.

## Self-review (done while writing)

- **Spec coverage:** driving model A → Task 3 (thread kept); `IHost.FrameYield` → Task 2; op-budget throttle on host clock → Tasks 1+3; `Sleep` on clock + tween on clock → Task 3; single `Speed` hook (no Ctrl wiring) → FrameClock (Task 1) + tween (Task 3); parity via headless no-op → Task 2 Steps 8-9; present stays no-op → unchanged (no task touches `0x20c`, correct). All covered.
- **Placeholder scan:** all code blocks are complete; the only "find these files" step (Task 2 Step 1) is a grep with the expected file list given.
- **Type consistency:** `FrameClock.NowMs/Speed/OpsPerFrame/Advance/EffectiveBudget` used identically in Tasks 1 and 3; `FrameYield()` signature identical in IHost (Task 2) and GodotAdvHost (Task 3); `PulseFrame()` defined in Task 3 Step 1c and called in Step 2c.

# Frame-paced `sleep` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `sleep` (`0xc8`) real timing so the SC0000 opening's `sleep`-paced retained-object burst animates on screen, using the compositor and alpha-tween subsystem that already exist.

**Architecture:** Add a `void Sleep(long duration)` seam to `IHost`; the VM dispatches `sleep` to it. All headless/CLI hosts no-op it (parity preserved); only `GodotAdvHost` blocks the VM background thread, letting the existing per-frame compositor (`Main.Recomposite`) catch each intermediate `GfxState`. Close the `GfxState` mutation race that becomes real once the VM runs concurrently for seconds, and add a frame-sequence capture tool to verify the animation.

**Tech Stack:** C# / .NET 8 (`Age.Engine`, `Age.Cli`), Godot 4.7 .NET (`godot/`), xUnit, Ghidra MCP (native RE), Python tooling (`opcodes_build.py`, `scene_opcode_coverage.py`).

## Global Constraints

- Run engine tests: `dotnet test engine/AgeEngine.sln` — must stay green (currently 50).
- **Headless/CLI parity is non-negotiable:** every non-Godot `IHost` implements `Sleep` as a no-op; `sweep` stays 284 exit / 13 STEP-LIMIT; emitted offsets + step counts byte-identical; `--selftest` unchanged.
- Synthesize test data; never disable a feature to keep a real scene matching a frozen number.
- Opcode source of truth is `vm-map/opcodes.toml`; regenerate with `py -3.11 -X utf8 tools/opcodes_build.py --build`. Never hand-edit generated files.
- Python is always `py -3.11 -X utf8 tools/<name>.py`.
- Annotate native RE in Ghidra as you go (rename `FUN_...`, plate comment, `save_program`), not just in docs (CLAUDE.md rule).
- Branch: continue `feat/gfx-command-buffer`. Commit after every task.

---

### Task 1: RE the `sleep` (`0xc8`) handler + `0x20c` disposition

Determine the operand's time unit (ms vs frames vs ticks) and confirm `sleep` is a simple blocking pause. This gates correct pacing in Task 4. Also settle whether `0x20c` is a bare present (→ `safe-noop`) or gates buffering.

**Files:**
- Modify: `vm-map/opcodes.toml` (ops `0xc8`, `0x20c`)
- Modify: `docs/engine-re.md` (new `sleep` decode section)
- Ghidra: annotate the resolved handlers (rename + plate comment + `save_program`)

**Interfaces:**
- Produces: the documented conversion `operand → milliseconds` (recorded in `opcodes.toml` op `0xc8` `details`), consumed by Task 4. Default expectation: operand is **milliseconds** (identity).

- [ ] **Step 1: Resolve the `0xc8` handler address via the dispatch table.**

The master dispatch is `handler(op) = ctx[0x26c93+op] = *(ctx+0x9b24c+op*4)`, registered in `FUN_00413860` (per `docs/engine-re.md`). Read `FUN_00413860`'s decompile and find the store whose computed opcode index equals `0xc8`. Use Ghidra MCP:

```
mcp__ghidra__decompile_function name="FUN_00413860"   # find the [ESI+0x9b24c+0xc8*4] store -> handler VA
```

Record the handler VA for `0xc8`. Repeat for `0x20c` (its handler is already named `gfx_op_0x20c_present_frame` per docs — confirm).

- [ ] **Step 2: Decompile the `sleep` handler and identify the timing primitive.**

```
mcp__ghidra__decompile_function address="<0xc8 handler VA>"
```

Look for the pacing mechanism and unit. Decide among:
- `Sleep(ms)` / `GetTickCount` / `timeGetTime` loop → operand is **milliseconds** (identity conversion).
- A frame counter / `present`-count loop → operand is **frames**; conversion = `ms = operand * 1000 / 60`.
- `QueryPerformanceCounter` scaled by a frequency constant → derive the unit from the constant.

Confirm it is a plain blocking pause (message-pump/idle loop), not a conditional/clock-relative wait. Note whether it pumps the window message loop (engine keeps rendering during the pause — matches our host-side model).

- [ ] **Step 3: Annotate the handler in Ghidra.**

```
mcp__ghidra__rename_function_by_address function_address="<0xc8 handler VA>" new_name="sleep_op_0xc8"
mcp__ghidra__set_plate_comment function_address="<0xc8 handler VA>" comment="op 0xc8 sleep(argc1): blocking pause for <operand> <unit>. <decode>. Frame-pacing keystone — see docs/engine-re.md."
mcp__ghidra__save_program
```

- [ ] **Step 4: Record the finding in `opcodes.toml`.**

Edit `vm-map/opcodes.toml` op `0xc8`: set `name`/`summary`, `source = "investigation"`, `confidence` per how clean the decode is, and put the exact unit + conversion in `details` (e.g. `details = "operand is milliseconds; blocking pause; handler sleep_op_0xc8 @0x...; host converts operand->ms 1:1"`). For `0x20c`: if it is a bare present with no VM-visible state, add `noop_headless = true` and note "compositor presents continuously host-side"; otherwise document what it gates and leave it a GAP.

- [ ] **Step 5: Rebuild the opcode artifacts and verify no lint regressions.**

Run: `py -3.11 -X utf8 tools/opcodes_build.py --build`
Then: `py -3.11 -X utf8 tools/opcodes_build.py --lint`
Expected: build succeeds; lint clean (no dangling refs / confidence-ceiling violations).

- [ ] **Step 6: Update `docs/engine-re.md`.**

Add a short `sleep (0xc8) — frame pacing` section: handler VA + name, the timing primitive, the unit + conversion, and the `0x20c` disposition. Cross-link the spec.

- [ ] **Step 7: Commit.**

```bash
git add vm-map/opcodes.toml tools/age_opcodes_himegari.py build/opcodes.json docs/opcode-reference.md build/opcode-coverage.md docs/engine-re.md
git commit -m "re(gfx): decode sleep 0xc8 timing unit + 0x20c present disposition"
```

---

### Task 2: `IHost.Sleep` seam + VM dispatch + host no-op stubs

Add the seam and wire the VM. Every non-Godot host no-ops it, keeping headless parity. TDD via a synthesized scene.

**Files:**
- Modify: `engine/Age.Engine/Hosting/IHost.cs` (add method)
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs:281` (add `case "sleep"` before `default:`)
- Modify (no-op `Sleep`): `engine/Age.Engine/Hosting/CaptureHost.cs`, `engine/Age.Cli/Program.cs` (`AudioTraceHost`, `GfxTraceHost`), `engine/Age.Engine.Tests/CallScriptIntegrationTests.cs` (`NullHost`), `engine/Age.Engine.Tests/CallScriptTests.cs` (`NullHost`), `engine/Age.Engine.Tests/GameSessionTests.cs` (`VoiceCountHost`), `engine/Age.Engine.Tests/TextureGeometryTests.cs` (`FakeSizeHost`), `engine/Age.Engine.Tests/TextureOpsTests.cs` (`RecHost`)
- Modify (record `Sleep`): `engine/Age.Engine.Tests/TestSupport.cs` (`RecordingHost`)
- Test: `engine/Age.Engine.Tests/SleepDispatchTests.cs` (create)

**Interfaces:**
- Produces: `IHost.Sleep(long duration)` — VM calls it with the raw `sleep` operand (`Read(a[0])`); the host interprets the unit. `RecordingHost.SleptDurations` (a `List<long>`) records the raw values for tests.
- Consumes: `ScriptAssembler.Assemble(table, name, code, strings)` (existing), opcode label `"sleep"` for `0xc8` (from `opcodes.toml`).

- [ ] **Step 1: Add a `Sleep` recorder to `RecordingHost` and write the failing test.**

In `engine/Age.Engine.Tests/TestSupport.cs`, add to `RecordingHost`:

```csharp
public readonly List<long> SleptDurations = new();
public void Sleep(long duration) => SleptDurations.Add(duration);
```

Create `engine/Age.Engine.Tests/SleepDispatchTests.cs`:

```csharp
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class SleepDispatchTests
{
    [Fact]
    public void SleepOp_ForwardsRawOperand_ToHost_AndAdvancesLikeNoop()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        (int, Operand[]) Sleep(long ms) => (0xc8, new[] { new Operand(0, ms) });
        (int, Operand[]) Show(int s) => (0x6e, new[] { new Operand(2, s), new Operand(0, 0) });
        (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

        var script = ScriptAssembler.Assemble(table, "SLEEPTEST",
            new List<(int, Operand[])> { Sleep(200), Show(0), Sleep(1000), Exit() }, new[] { "hi" });

        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Run();

        Assert.Equal(new List<long> { 200, 1000 }, host.SleptDurations);
        Assert.Single(host.Lines);                        // the show-text between the sleeps still ran
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile (no `Sleep` on `IHost`).**

Run: `dotnet test engine/AgeEngine.sln --filter SleepDispatchTests`
Expected: BUILD FAILS — `IHost` has no `Sleep`; the other host doubles don't implement it.

- [ ] **Step 3: Add `Sleep` to the interface.**

In `engine/Age.Engine/Hosting/IHost.cs`, add after `WaitForInput`:

```csharp
    void Sleep(long duration);
```

- [ ] **Step 4: Add the VM dispatch case.**

In `engine/Age.Engine/Vm/VirtualMachine.cs`, immediately before `default:` (line ~281):

```csharp
            case "sleep":   // 0xc8 (duration) — pause the host; headless hosts no-op (parity). Frame pacing.
                _host.Sleep(Read(a[0])); return pc + 1;
```

- [ ] **Step 5: Add no-op `Sleep` to every non-Godot host.**

`CaptureHost.cs`, and the test doubles `NullHost` (×2), `VoiceCountHost`, `FakeSizeHost`, `RecHost`, plus `AudioTraceHost` and `GfxTraceHost` in `Program.cs` — add to each:

```csharp
    public void Sleep(long duration) { }
```

(`RecordingHost` already got its recording impl in Step 1.)

- [ ] **Step 6: Run the new test and the full suite.**

Run: `dotnet test engine/AgeEngine.sln --filter SleepDispatchTests`
Expected: PASS.
Run: `dotnet test engine/AgeEngine.sln`
Expected: all green (was 50, now 51).

- [ ] **Step 7: Verify headless parity — sweep unchanged.**

Run: `dotnet run --project engine/Age.Cli -- sweep`
Expected: `284` exit / `13` STEP-LIMIT (unchanged from before this slice).

- [ ] **Step 8: Commit.**

```bash
git add engine/Age.Engine/Hosting/IHost.cs engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine/Hosting/CaptureHost.cs engine/Age.Cli/Program.cs engine/Age.Engine.Tests/
git commit -m "feat(vm): IHost.Sleep seam + dispatch 0xc8 (headless hosts no-op; parity held)"
```

---

### Task 3: Close the `GfxState` mutation race

Once the VM runs concurrently for seconds (Task 4), the main-thread compositor's `SnapshotVisibleObjects()` truly overlaps VM-thread `_objects`/`_registry` writes. `GetOrCreate`, `Register`, and `Release` currently mutate **without** the lock. Serialize them on the existing `_lock` (re-entrant — safe for callers like `BindDraw`/`EraseRange` that already hold it).

**Files:**
- Modify: `engine/Age.Engine/Model/GfxState.cs` (`GetOrCreate`, `Register`, `Release`)
- Test: `engine/Age.Engine.Tests/GfxStateConcurrencyTests.cs` (create)

**Interfaces:**
- Consumes: `GfxState.GetOrCreate/Register/Release/BindDraw/SnapshotVisibleObjects` (existing signatures unchanged).
- Produces: no API change — behavior identical single-threaded; safe under concurrent snapshot + mutation.

- [ ] **Step 1: Write the failing concurrency test.**

Create `engine/Age.Engine.Tests/GfxStateConcurrencyTests.cs`:

```csharp
using System.Threading.Tasks;
using Age.Engine.Model;
using Xunit;

public class GfxStateConcurrencyTests
{
    [Fact]
    public void Snapshot_DoesNotThrow_WhileObjectsMutate()
    {
        var gfx = new GfxState();
        var stop = false;
        var writer = Task.Run(() =>
        {
            long h = 0;
            while (!stop)
            {
                h = (h + 1) % 64;
                gfx.BindDraw(h, 0, 0, 0, 10, 10, 0, 0);   // GetOrCreate + visible
                gfx.GetOrCreate(h + 100);                  // bare create
                if (h % 8 == 0) gfx.Release(h + 100);      // remove
            }
        });
        // Hammer the reader concurrently; a dictionary mutated during enumeration would throw here.
        for (int i = 0; i < 20000; i++) { var _ = gfx.SnapshotVisibleObjects(); }
        stop = true;
        writer.Wait();
    }
}
```

- [ ] **Step 2: Run it to observe the race (flaky failure).**

Run: `dotnet test engine/AgeEngine.sln --filter GfxStateConcurrencyTests`
Expected: FAIL (often) with `InvalidOperationException: Collection was modified` from `SnapshotVisibleObjects` enumerating `_objects` while `GetOrCreate` writes it. (If it passes by luck, rerun; the fix makes it deterministic.)

- [ ] **Step 3: Lock the three unguarded mutators.**

In `engine/Age.Engine/Model/GfxState.cs`:

```csharp
    public GfxObject GetOrCreate(long handle)
    {
        lock (_lock)
        {
            if (!_objects.TryGetValue(handle, out var o)) { o = new GfxObject(); _objects[handle] = o; }
            CurrentObject = handle;
            return o;
        }
    }

    public void Register(long handle) { lock (_lock) { _registry.Add(handle); } }

    public void Release(long handle)
    {
        lock (_lock)
        {
            _objects.Remove(handle);
            _registry.Remove(handle);
        }
    }
```

(The `lock` field is declared just below `Register` in the current file. Move the `private readonly object _lock = new();` declaration up above `GetOrCreate` so it is in scope for all three — keep the single declaration, do not add a second.)

- [ ] **Step 4: Run the concurrency test + full suite.**

Run: `dotnet test engine/AgeEngine.sln --filter GfxStateConcurrencyTests`
Expected: PASS deterministically.
Run: `dotnet test engine/AgeEngine.sln`
Expected: all green (52).

- [ ] **Step 5: Commit.**

```bash
git add engine/Age.Engine/Model/GfxState.cs engine/Age.Engine.Tests/GfxStateConcurrencyTests.cs
git commit -m "fix(gfx): serialize GetOrCreate/Register/Release on _lock (compositor race)"
```

---

### Task 4: `GodotAdvHost.Sleep` — real pause on the VM thread

Block the VM background thread for the RE'd duration; the main thread keeps compositing.

**Files:**
- Modify: `godot/GodotAdvHost.cs` (implement `Sleep`)

**Interfaces:**
- Consumes: `IHost.Sleep(long duration)`; the unit conversion documented in Task 1 (`opcodes.toml` op `0xc8` `details`).
- Produces: a blocking pause on the calling (VM background) thread.

- [ ] **Step 1: Implement `Sleep` in `GodotAdvHost`.**

Add to `godot/GodotAdvHost.cs` (after `WaitForInput`). Use the Task-1 conversion; the code below assumes **milliseconds** (identity) — if Task 1 found frames, replace the conversion line as noted:

```csharp
    // op 0xc8: block the VM background thread so the main-thread compositor (Main.Recomposite) presents the
    // current retained GfxState — this is what makes the sleep-paced opening burst animate. Time-based sibling
    // of WaitForInput's suspend. Unit per docs/engine-re.md + opcodes.toml 0xc8 (operand = milliseconds).
    public void Sleep(long duration)
    {
        int ms = (int)System.Math.Clamp(duration, 0, 10_000);   // operand is ms; cap so a bad script can't hang
        // If Task 1 found FRAMES instead: int ms = (int)System.Math.Clamp(duration * 1000 / 60, 0, 10_000);
        if (ms > 0) Thread.Sleep(ms);
    }
```

(`using System.Threading;` is already imported for `SemaphoreSlim`.)

- [ ] **Step 2: Build the Godot project.**

Run: `dotnet build godot/Himegari.csproj`
Expected: build succeeds, no errors.

- [ ] **Step 3: Verify the selftest still passes (plumbing parity).**

Run: `godot --headless --path godot -- --selftest`
Expected: `SELFTEST OK: threaded host matches headless (...)`. (The synthetic selftest scene has no `sleep`, so this is unaffected — a guard that the seam addition didn't break the thread/semaphore plumbing.)

- [ ] **Step 4: Commit.**

```bash
git add godot/GodotAdvHost.cs
git commit -m "feat(godot): Sleep blocks the VM thread so the compositor presents paced frames"
```

---

### Task 5: `--shot-sequence` frame capture + visual verification

Add a dev flag that saves one PNG per `_Process` frame across the opening, so the `AE*` burst can be verified as distinct frames. Then actually run it and inspect.

**Files:**
- Modify: `godot/Main.cs` (parse `--shot-sequence <dir>` / `--frames <n>`; capture in `_Process`)
- Modify: `docs/tools-reference.md` (document the flag)

**Interfaces:**
- Consumes: existing `_Process` compositor + `GetViewport().GetTexture().GetImage()` capture (as in `--shot`).
- Produces: `--shot-sequence <dir> [--frames N]` writing `frame_0000.png…` (default N=180 ≈ 3s @60fps), then quits.

- [ ] **Step 1: Add the fields and arg parsing.**

In `godot/Main.cs`, add fields near `_shotPath` (line ~26):

```csharp
    private string? _seqDir;                    // --shot-sequence <dir>: dump one PNG per frame
    private int _seqFrames = 180;               // --frames <n>: how many frames to dump (default ~3s @60fps)
    private int _seqIdx;
```

In the arg loop (near line ~88), add:

```csharp
            if (userArgs[i] == "--shot-sequence" && i + 1 < userArgs.Length) _seqDir = userArgs[i + 1];
            if (userArgs[i] == "--frames" && i + 1 < userArgs.Length) int.TryParse(userArgs[i + 1], out _seqFrames);
```

- [ ] **Step 2: Auto-advance past input waits during a sequence capture, and capture per frame.**

In `_Ready`, alongside the `--shot` auto-advancer (line ~129), add so the opening isn't blocked waiting for a click:

```csharp
        if (_seqDir != null)
            _ = Task.Run(async () => { while (!_done) { if (_host.IsWaiting) _host.SignalInput(); await Task.Delay(1); } });
```

In `_Process`, at the top of the method after `Recomposite()` (so the composited frame is captured), add:

```csharp
        if (_seqDir != null && _seqIdx < _seqFrames && !_done)
        {
            System.IO.Directory.CreateDirectory(_seqDir);
            var fimg = GetViewport().GetTexture().GetImage();
            fimg.SavePng($"{_seqDir}/frame_{_seqIdx:0000}.png");
            _seqIdx++;
            if (_seqIdx >= _seqFrames) { GD.Print($"SEQ saved {_seqIdx} frames -> {_seqDir}"); GetTree().Quit(0); }
            return;
        }
```

- [ ] **Step 3: Build.**

Run: `dotnet build godot/Himegari.csproj`
Expected: build succeeds.

- [ ] **Step 4: Capture the opening with boot state.**

Run: `godot --path godot -- --boot --shot-sequence "../../../scratchpad/seq" --frames 240`
(Use the scratchpad dir from the session; run windowed — texture ops don't render under `--headless`.)
Expected: 240 PNGs written; console `SEQ saved 240 frames`.

- [ ] **Step 5: Inspect the frames for the animated burst.**

Read a spread of frames (e.g. every ~20th) and confirm the opening `AE001D → AE002B → AE003B` surface swaps appear as **distinct** frames — the explosion stepping — not a single static final CG. Read them with the Read tool (PNG visual):

```
Read frame_0000.png, frame_0040.png, frame_0080.png, frame_0120.png, frame_0160.png
```

Success = visibly different intermediate frames across the burst. If all frames are identical, STOP and debug (systematic-debugging): likely the unit conversion (Task 1/4) or the sleeps aren't on the captured path.

- [ ] **Step 6: Document the flag and commit.**

Add a `--shot-sequence <dir> [--frames N]` row to the Godot frontend section of `docs/tools-reference.md` (per-frame PNG dump for verifying time-based effects).

```bash
git add godot/Main.cs docs/tools-reference.md
git commit -m "feat(godot): --shot-sequence per-frame capture for verifying paced animation"
```

---

### Task 6: Coverage gauge, docs, and memory

Record the GAP shrink and update the canonical docs + status memory.

**Files:**
- Modify: `docs/phase-a-slice-plan.md` (A2b frame-pacing slice result)
- Modify: `~/.claude/…/memory/himegari-port-status.md` + `MEMORY.md` (status line)
- (Regenerated) `build/scene-opcode-coverage/SC0000.md`

**Interfaces:**
- Consumes: everything above (VM now handles `sleep`; `0x20c` disposition from Task 1).

- [ ] **Step 1: Regenerate the SC0000 completeness gauge.**

Run: `py -3.11 -X utf8 tools/scene_opcode_coverage.py SC0000`
Expected: `0xc8` moves GAP→impl; if Task 1 marked `0x20c` `noop_headless`, it moves GAP→safe-noop. Note the new handled/GAP counts (was 65/129 handled, 64 GAP).

- [ ] **Step 2: Update `docs/phase-a-slice-plan.md`.**

Add an "A2b — frame-paced sleep" result subsection: the root cause (compositor already presents; `sleep` was instant), what shipped (seam + Godot block + race fix + `--shot-sequence`), the visual verification result (the opening burst now animates — or the honest state if partial), the RE'd `sleep` unit, and the GAP delta.

- [ ] **Step 3: Update the status memory.**

In `himegari-port-status.md`, append a `✅ FRAME-PACED SLEEP` entry: `sleep 0xc8` now paces the VM thread → SC0000 opening `AE*` burst animates (screenshot-sequence verified); `IHost.Sleep` seam (headless no-op → parity held, sweep 284/13 unchanged); `GfxState` compositor race closed; `--shot-sequence` tool; `0x20c` disposition; GAP delta. Note the scene-coroutine framework (`0x7b/0x7c/0x140`) is still the deferred next chunk. Update the one-line entry in `MEMORY.md`. Convert any relative dates to absolute (2026-07-08).

- [ ] **Step 4: Commit.**

```bash
git add docs/phase-a-slice-plan.md build/scene-opcode-coverage/SC0000.md
git commit -m "docs(gfx): record the frame-paced sleep slice + SC0000 GAP shrink"
```

(The memory files live outside the repo; they are written, not committed here.)

---

## Notes for the executor

- **Task ordering matters:** Task 1 (RE) feeds Task 4's unit conversion. If Task 1 conclusively finds milliseconds, Task 4's code is used as-written; if frames, swap the one conversion line.
- **Parity is the tripwire:** if `dotnet test` drops a test or `sweep` deviates from 284/13, stop — the seam should be behavior-neutral headless.
- **The verification is visual** (Task 5): a green test suite does NOT prove the opening animates; the frame sequence does. Treat an all-identical sequence as a real failure, not a pass.
- The scene-coroutine framework, ADV native text, and audio/SFX clusters are explicitly out of scope (see spec Non-goals).

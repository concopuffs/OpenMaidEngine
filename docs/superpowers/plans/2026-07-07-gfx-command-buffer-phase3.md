# GFX Command-Buffer — Phase 3 Implementation Plan (TDD)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute the 13 gfx command-buffer ops against a VM-owned `GfxState` so `0x215` returns distinct per-object slots and `0x218`/`0x21a` return stored geometry vectors — fixing the background/sprite render drift.

**Architecture:** `GfxState` is version-neutral VM execution state (like the global bank); the ops are new `case`s in `VirtualMachine.Step` that mutate/query it. QUERY ops `Write` results into operands exactly like `get-texture-size`. No new `IHost` methods for pure-data ops. Design: `docs/superpowers/specs/2026-07-07-gfx-command-buffer-design.md`; op contract: `docs/engine-re.md` (gfx op-contract table).

**Tech Stack:** C# / .NET 8 (`engine/`, `AgeEngine.sln`, xUnit). Opcode build: `py -3.11 -X utf8 tools/opcodes_build.py --build`. Godot 4.7 .NET (`godot/`).

## Global Constraints

- **Dispatch keys on the `label` field, not `name`.** `OpcodeTableJson.Load` reads `opcodes.json` `label`; `VirtualMachine.Step` switches on `_t.Label(op)`. My Phase-1 edits set `name` only, so **Task 3.2 must set each gfx op's `label` to its dispatch string** (the `0x208`/`0x1fb` precedent) and rebuild — otherwise the new `case`s never match.
- **Run Python as** `py -3.11 -X utf8 tools/opcodes_build.py --build`; never hand-edit generated files; `--lint` must be `0 errors`.
- **Operand indexing:** `a[i]` is 0-based; contract operand *n* = `a[n-1]`. Read inputs with `Read(a[i])`, write outputs with `Write(a[i], val)`.
- **Parity:** each gfx op is one step, returns `pc+1` (same as the old `OnStub`). Base-ISA tests and the Godot `--selftest` run synthetic/non-gfx scenes → must stay green and byte-identical. Verify with `dotnet test engine/AgeEngine.sln`.
- **Seam:** `GfxState` lives in `Age.Engine/Model`; `Vm` may reference `Model`. No `Sys4`/Godot references from the VM.
- **Testing principle:** synthesize scenes with `ScriptAssembler` (see `SyntheticSceneTests`); never disable a feature to keep a real scene matching a frozen number.

## Confirmed constants (from Ghidra, Phase 1 + this pass)

| op | label (dispatch) | argc | GfxState action |
|---|---|---|---|
| `0x1a2` | `gfx-cmd-register` | 1 | `GetOrCreate(a0)` (register) |
| `0x1f7` | `gfx-elem-create` | 2 | `GetOrCreate(a0)` (assign slot if new) |
| `0x1fa` | `gfx-elem-release` | 1 | `Release(a0)` |
| `0x212` | `set-gfx-field64` | 2 | `GetOrCreate(a0).Field64 = a1` |
| `0x213` | `set-gfx-xy` | 3 | `o=GetOrCreate(a0); o.Field68=a1; o.Field6c=a2` |
| `0x215` | `query-gfx-object?` | 2 | `Write(a0, QuerySlot(a1))` |
| `0x216` | `query-gfx-field?` | 2 | `Write(a0, QueryField(a1))` |
| `0x217` | `set-gfx-geom3` | 4 | `GetOrCreate(a0).V18 = (a1,a2,a3)` |
| `0x218` | `get-gfx-geom3?` | 4 | `v=TryGet(a0)?.V18; Write(a1,v.X);Write(a2,v.Y);Write(a3,v.Z)` |
| `0x219` | `set-gfx-geom3-b` | 4 | `GetOrCreate(a0).V24 = (a1,a2,a3)` |
| `0x21a` | `get-gfx-geom3-b?` | 4 | `v=TryGet(a0)?.V24; Write(a1..a3)` |
| `0x1ff` | `set-gfx-geom3-c` | 4 | `GetOrCreate(a0).V16c = (a1,a2,a3)` |
| `0x202` | `gfx-blit-color` | 5 | `GetOrCreate(a0).Color = PackColor(a3,a4)` (blend deferred) |
| `0x203` | `gfx-draw-color` | 4 | `GetOrCreate(a0).Color = PackColor(a2,a3)` (blend deferred) |

**Vector pairing (native obj fields):** V18 = `+0x18/1c/20` (0x217 set ↔ 0x218 get, anchor); V24 = `+0x24/28/2c` (0x219 set ↔ 0x21a get, position); V16c = `+0x16c` (0x1ff set). All handles resolve in the one `ctx+0x408` registry; getters return `(0,0,0)` on miss. **Simplifications (not drift-critical; validate in Phase 4 if a scene needs them):** `0x212/0x213` index a distinct native table (`ctx+0x14d54`) — modelled on the same object map keyed by their idx; `0x216` reads `ctx+0x46d14`, which no op in the family writes → `QueryField` returns 0.

## File structure

| File | Responsibility | Task |
|---|---|---|
| `engine/Age.Engine/Model/GfxState.cs` | **New.** Handle→object registry, slot allocator, PackColor | 3.1 |
| `engine/Age.Engine.Tests/GfxStateTests.cs` | **New.** Pure-data unit tests | 3.1 |
| `vm-map/opcodes.toml` | Set `label` = dispatch string for the 13 ops | 3.2 |
| `engine/Age.Engine/Vm/VirtualMachine.cs` | `Gfx` property + the 13 `case`s | 3.3, 3.4, 3.5 |
| `engine/Age.Engine.Tests/GfxCommandBufferTests.cs` | **New.** Synthetic-scene op tests + drift regression | 3.3–3.6 |
| `engine/Age.Cli/Program.cs` (`GfxTraceHost`) | Oracle reads real `vm.Gfx` slots | 3.7 |
| `godot/Main.cs` | Guarded alpha log; confirm per-slot compositing | 3.7 |

---

### Task 3.1 — `GfxState` model (pure data, host-free)

**Files:** Create `engine/Age.Engine/Model/GfxState.cs`; Test `engine/Age.Engine.Tests/GfxStateTests.cs`.

**Interfaces produced:** `GfxState` with `GfxObject GetOrCreate(long handle)`, `GfxObject? TryGet(long)`, `int QuerySlot(long)`, `long QueryField(long)`, `void Release(long)`, `static long PackColor(long alpha, long color)`; `GfxObject { int Slot; (long X,long Y,long Z) V18, V24, V16c; long Field64, Field68, Field6c; long Color; }`.

- [ ] **Step 1: Write the failing tests**

Create `engine/Age.Engine.Tests/GfxStateTests.cs`:
```csharp
using Age.Engine.Model;
using Xunit;

public class GfxStateTests
{
    [Fact]
    public void DistinctHandlesGetDistinctSlots()
    {
        var g = new GfxState();
        int s1 = g.GetOrCreate(0x1000).Slot;
        int s2 = g.GetOrCreate(0x2000).Slot;
        Assert.NotEqual(s1, s2);
        Assert.Equal(s1, g.QuerySlot(0x1000));   // stable
        Assert.Equal(-1, g.QuerySlot(0x9999));    // unknown -> -1 (matches native 0xffffffff)
    }

    [Fact]
    public void VectorsRoundTripPerObject()
    {
        var g = new GfxState();
        g.GetOrCreate(0x1000).V18 = (10, 20, 30);
        g.GetOrCreate(0x1000).V24 = (40, 50, 60);
        Assert.Equal((10L, 20L, 30L), g.TryGet(0x1000)!.V18);
        Assert.Equal((40L, 50L, 60L), g.TryGet(0x1000)!.V24);
        Assert.Null(g.TryGet(0x2000));            // untouched handle absent
    }

    [Fact]
    public void ReleaseFreesTheSlotForReuse()
    {
        var g = new GfxState();
        int s1 = g.GetOrCreate(0x1000).Slot;
        g.Release(0x1000);
        Assert.Equal(-1, g.QuerySlot(0x1000));
        Assert.Equal(s1, g.GetOrCreate(0x2000).Slot);  // freed slot reused
    }

    [Fact]
    public void PackColorPacksArgb()
        => Assert.Equal(0x80_112233L, GfxState.PackColor(0x80, 0x112233));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~GfxStateTests`
Expected: FAIL — `GfxState` does not exist (compile error).

- [ ] **Step 3: Implement `GfxState`**

Create `engine/Age.Engine/Model/GfxState.cs`:
```csharp
namespace Age.Engine.Model;

/// <summary>Host-agnostic model of the AGE native gfx command-buffer (reversed in
/// docs/engine-re.md, gfx op-contract table). One registry maps an object handle to a GfxObject — the
/// native ctx+0x408 map that op 0x215 queries and the geometry get/set ops share. Each object carries a
/// slot (returned by 0x215) and three 3-vectors: V18 (set 0x217 / get 0x218, anchor), V24 (set 0x219 /
/// get 0x21a, position), V16c (set 0x1ff). The native DirectDraw workers are NOT modelled — only the data
/// the query ops read back, which is all the bytecode geometry math needs.</summary>
public sealed class GfxState
{
    public sealed class GfxObject
    {
        public int Slot = -1;
        public (long X, long Y, long Z) V18, V24, V16c;
        public long Field64, Field68, Field6c;
        public long Color;
    }

    private readonly Dictionary<long, GfxObject> _objects = new();
    private readonly SortedSet<int> _free = new();
    private int _nextSlot = 4;                       // observed native slot range is 4..13
    private readonly Dictionary<long, long> _fieldTable = new();   // ctx+0x46d14 (0x216); no family writer -> default 0
    public long CurrentObject { get; private set; }

    private int AcquireSlot()
    {
        if (_free.Count > 0) { int s = _free.Min; _free.Remove(s); return s; }
        return _nextSlot++;
    }

    public GfxObject GetOrCreate(long handle)
    {
        if (!_objects.TryGetValue(handle, out var o))
        {
            o = new GfxObject { Slot = AcquireSlot() };
            _objects[handle] = o;
        }
        CurrentObject = handle;
        return o;
    }

    public GfxObject? TryGet(long handle) => _objects.TryGetValue(handle, out var o) ? o : null;
    public int QuerySlot(long handle) => _objects.TryGetValue(handle, out var o) ? o.Slot : -1;
    public long QueryField(long idx) => _fieldTable.TryGetValue(idx, out var v) ? v : 0;

    public void Release(long handle)
    {
        if (_objects.TryGetValue(handle, out var o)) { if (o.Slot >= 0) _free.Add(o.Slot); _objects.Remove(handle); }
    }

    /// <summary>Pack (alpha, rgb) → 0xAARRGGBB, matching op 0x202/0x203's handler bit-manipulation for the
    /// common (non-negative-sentinel) case. The alpha&lt;0 / color&lt;0 native-fetch path is deferred.</summary>
    public static long PackColor(long alpha, long color)
        => ((alpha & 0xff) << 24) | (color & 0xffffff);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~GfxStateTests`
Expected: PASS (all 4 facts).

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Engine/Model/GfxState.cs engine/Age.Engine.Tests/GfxStateTests.cs
git commit -m "feat(gfx): GfxState host-agnostic command-buffer model

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3.2 — Set opcode `label`s to the dispatch strings + rebuild

The VM switches on `_t.Label(op)` = the `opcodes.toml` **`label`** field. Set each gfx op's `label` to its dispatch string so the Task-3.3/3.4/3.5 `case`s match.

**Files:** Modify `vm-map/opcodes.toml` (13 ops); then rebuild generated files.

- [ ] **Step 1: Set the `label` line for each gfx op**

For each op below, change its `label = "u00…"` line to the dispatch string (the block starts at `op = <op>`):

`0x1a2`→`gfx-cmd-register`, `0x1f7`→`gfx-elem-create`, `0x1fa`→`gfx-elem-release`, `0x1ff`→`set-gfx-geom3-c`, `0x202`→`gfx-blit-color`, `0x203`→`gfx-draw-color`, `0x212`→`set-gfx-field64`, `0x213`→`set-gfx-xy`, `0x215`→`query-gfx-object?`, `0x216`→`query-gfx-field?`, `0x217`→`set-gfx-geom3`, `0x218`→`get-gfx-geom3?`, `0x219`→`set-gfx-geom3-b`, `0x21a`→`get-gfx-geom3-b?`.

Example (op `0x215`): change `label = "u00421160"` to `label = "query-gfx-object?"`.

- [ ] **Step 2: Rebuild + lint + confirm**

Run: `py -3.11 -X utf8 tools/opcodes_build.py --build`
Run: `py -3.11 -X utf8 tools/opcodes_build.py --lint`
Expected: build writes 4 files; lint `0 errors, 0 warnings`.
Run: `grep -c '"label": "query-gfx-object?"' build/opcodes.json`
Expected: `1`.

- [ ] **Step 3: Confirm base-ISA parity (labels didn't touch handled ops)**

Run: `dotnet test engine/AgeEngine.sln`
Expected: all green (these ops are still stubbed at this point; only their label strings changed — disasm/VM unaffected until Task 3.3).

- [ ] **Step 4: Commit**
```bash
git add vm-map/opcodes.toml tools/age_opcodes_himegari.py build/opcodes.json docs/opcode-reference.md build/opcode-coverage.md
git commit -m "chore(gfx): set dispatch labels for the gfx command-buffer ops

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3.3 — Wire `Gfx` into the VM + implement the QUERY ops (the drift drivers)

**Files:** Modify `engine/Age.Engine/Vm/VirtualMachine.cs`; Test `engine/Age.Engine.Tests/GfxCommandBufferTests.cs` (new).

**Interfaces:** Consumes `GfxState` (Task 3.1). Produces `public GfxState Gfx { get; }` on `VirtualMachine`, and dispatch for `query-gfx-object?`/`query-gfx-field?`/`get-gfx-geom3?`/`get-gfx-geom3-b?`.

- [ ] **Step 1: Write the failing test**

Create `engine/Age.Engine.Tests/GfxCommandBufferTests.cs`:
```csharp
using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class GfxCommandBufferTests
{
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int addr) => new(3, addr);   // global-int
    private static Operand I(long v) => new(0, v);        // immediate

    // op ctor helpers (label→op: 0x217 set-geom3, 0x218 get-geom3, 0x215 query, 0x2 exit)
    private static (int, Operand[]) SetGeom3(int handle, int a, int b, int c) => (0x217, new[] { G(handle), G(a), G(b), G(c) });
    private static (int, Operand[]) GetGeom3(int handle, int a, int b, int c) => (0x218, new[] { G(handle), G(a), G(b), G(c) });
    private static (int, Operand[]) Query(int outAddr, int handle) => (0x215, new[] { G(outAddr), G(handle) });
    private static (int, Operand[]) MovGI(int dst, long v) => (0x55, new[] { G(dst), I(v) });
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void SetThenGetGeom3RoundTripsThroughTheObject()
    {
        var t = T();
        // g[1]=handle; set V18 from g[2,3,4]=(10,20,30); read V18 back into g[5,6,7]; then exit.
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 10), MovGI(3, 20), MovGI(4, 30),
            SetGeom3(1, 2, 3, 4),
            GetGeom3(1, 5, 6, 7),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(10, vm.Globals[5]);
        Assert.Equal(20, vm.Globals[6]);
        Assert.Equal(30, vm.Globals[7]);
    }

    [Fact]
    public void QueryReturnsDistinctSlotsPerHandle_NotZero()
    {
        var t = T();
        // create two objects via set-geom, then query each into g[10], g[11].
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 0x2000), MovGI(3, 0),
            SetGeom3(1, 3, 3, 3), SetGeom3(2, 3, 3, 3),
            Query(10, 1), Query(11, 2), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.NotEqual(0, vm.Globals[10]);            // not collapsed to slot 0
        Assert.NotEqual(vm.Globals[10], vm.Globals[11]); // distinct slots => no collapse
    }
}
```
(The op numbers `0x217/0x218/0x215` here bypass label lookup at assemble time — the VM still dispatches by `_t.Label`, which Task 3.2 made match.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~GfxCommandBufferTests`
Expected: FAIL — ops still hit `default`/stub, so `g[5..7]`/`g[10..11]` are never written (assert fails / `KeyNotFoundException`).

- [ ] **Step 3: Add the `Gfx` property**

In `engine/Age.Engine/Vm/VirtualMachine.cs`, after the `Globals`/`GlobalStrings` properties (~line 25):
```csharp
    public GfxState Gfx { get; } = new();
```

- [ ] **Step 4: Add the QUERY `case`s**

In `Step`, before the `default:` case (~line 228):
```csharp
            case "query-gfx-object?":   // 0x215 (out)(handle) -> slot | -1
                Write(a[0], Gfx.QuerySlot(Read(a[1]))); return pc + 1;
            case "query-gfx-field?":    // 0x216 (out)(idx)
                Write(a[0], Gfx.QueryField(Read(a[1]))); return pc + 1;
            case "get-gfx-geom3?":      // 0x218 (handle)(outA)(outB)(outC) <- V18
            {
                var v = Gfx.TryGet(Read(a[0]))?.V18 ?? default;
                Write(a[1], v.X); Write(a[2], v.Y); Write(a[3], v.Z); return pc + 1;
            }
            case "get-gfx-geom3-b?":    // 0x21a (handle)(outA)(outB)(outC) <- V24
            {
                var v = Gfx.TryGet(Read(a[0]))?.V24 ?? default;
                Write(a[1], v.X); Write(a[2], v.Y); Write(a[3], v.Z); return pc + 1;
            }
```
(These reference `set-gfx-geom3` which Task 3.4 adds; the round-trip test needs both, so run the suite after Task 3.4. To keep Task 3.3 self-checking, temporarily add the `set-gfx-geom3` case now too — or run Step 5 after 3.4. Recommended: implement Task 3.4's SET case in the same edit; the split is documentation-only.)

- [ ] **Step 5: (with Task 3.4) run to verify pass** — see Task 3.4 Step 3.

---

### Task 3.4 — Implement the SET + lifecycle ops

**Files:** Modify `engine/Age.Engine/Vm/VirtualMachine.cs`.

- [ ] **Step 1: Add the SET/lifecycle `case`s** (alongside Task 3.3's block)
```csharp
            case "set-gfx-geom3":       // 0x217 (handle)(a)(b)(c) -> V18
                Gfx.GetOrCreate(Read(a[0])).V18 = (Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            case "set-gfx-geom3-b":     // 0x219 (handle)(a)(b)(c) -> V24
                Gfx.GetOrCreate(Read(a[0])).V24 = (Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            case "set-gfx-geom3-c":     // 0x1ff (handle)(a)(b)(c) -> V16c
                Gfx.GetOrCreate(Read(a[0])).V16c = (Read(a[1]), Read(a[2]), Read(a[3])); return pc + 1;
            case "set-gfx-field64":     // 0x212 (idx)(val)
                Gfx.GetOrCreate(Read(a[0])).Field64 = Read(a[1]); return pc + 1;
            case "set-gfx-xy":          // 0x213 (idx)(x)(y)
            {
                var o = Gfx.GetOrCreate(Read(a[0])); o.Field68 = Read(a[1]); o.Field6c = Read(a[2]); return pc + 1;
            }
            case "gfx-cmd-register":    // 0x1a2 (val)  — register/select
            case "gfx-elem-create":     // 0x1f7 (handle)(count) — ensure object + slot
                Gfx.GetOrCreate(Read(a[0])); return pc + 1;
            case "gfx-elem-release":    // 0x1fa (handle)
                Gfx.Release(Read(a[0])); return pc + 1;
```

- [ ] **Step 2: Add the round-trip + slot facts** (already in Task 3.3's test file)

- [ ] **Step 3: Run the gfx suite**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~GfxCommandBufferTests`
Expected: PASS (`SetThenGetGeom3RoundTrips`, `QueryReturnsDistinctSlots`).

- [ ] **Step 4: Run the full suite (parity)**

Run: `dotnet test engine/AgeEngine.sln`
Expected: all green — base-ISA/synthetic tests unaffected (gfx ops absent from them; still one step / `pc+1`).

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/GfxCommandBufferTests.cs
git commit -m "feat(gfx): execute gfx command-buffer query/set/lifecycle ops in the VM

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3.5 — Colored draws (`0x202`/`0x203`) with alpha deferred + guarded

**Files:** Modify `engine/Age.Engine/Vm/VirtualMachine.cs`; Test in `GfxCommandBufferTests.cs`.

- [ ] **Step 1: Write the color-packing test**
```csharp
    private static (int, Operand[]) BlitColor(int h, int x, int y, int alpha, int color)
        => (0x202, new[] { G(h), G(x), G(y), G(alpha), G(color) });

    [Fact]
    public void BlitColorStoresPackedArgbOnTheObject()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 0), MovGI(3, 0), MovGI(4, 0x80), MovGI(5, 0x112233),
            BlitColor(1, 2, 3, 4, 5), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(0x80_112233L, vm.Gfx.TryGet(0x1000)!.Color);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~BlitColorStores`
Expected: FAIL (op stubbed → object absent → `TryGet` null-deref).

- [ ] **Step 3: Add the colored-draw `case`s (state now; blend deferred)**
```csharp
            case "gfx-blit-color":      // 0x202 (handle)(x)(y)(alpha)(color) — blend deferred
                Gfx.GetOrCreate(Read(a[0])).Color = GfxState.PackColor(Read(a[3]), Read(a[4]));
                WarnAlphaDeferredOnce(); return pc + 1;
            case "gfx-draw-color":      // 0x203 (handle)(v)(alpha)(color) — blend deferred
                Gfx.GetOrCreate(Read(a[0])).Color = GfxState.PackColor(Read(a[2]), Read(a[3]));
                WarnAlphaDeferredOnce(); return pc + 1;
```
Add a one-time guard near the bottom of the class (routes through the trace sink so it's visible, not silent):
```csharp
    private bool _warnedAlpha;
    private void WarnAlphaDeferredOnce()
    {
        if (_warnedAlpha) return; _warnedAlpha = true;
        _sink.Emit(TraceEvent.Stub(0x202, -1));   // surfaces "gfx alpha/blend deferred" in --trace
    }
```
(If `TraceEvent.Stub` is unsuitable as a general marker, add a dedicated `TraceEvent.Note(string)`; keep it observe-only so parity holds.)

- [ ] **Step 4: Run to verify pass + full suite**

Run: `dotnet test engine/AgeEngine.sln`
Expected: `BlitColorStores…` PASS; all others green (the guard is observe-only → step/trace parity holds).

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/GfxCommandBufferTests.cs
git commit -m "feat(gfx): colored-draw ops store packed ARGB; alpha blend deferred + guarded

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3.6 — Drift regression test (two different-sized backgrounds don't drift)

The machine-checkable heart of the fix: reproduce the anchor-preserve idiom on two different-sized objects and assert each keeps its own geometry (pre-fix they'd share slot 0 and cross-contaminate).

**Files:** Test in `engine/Age.Engine.Tests/GfxCommandBufferTests.cs`.

- [ ] **Step 1: Write the regression test**
```csharp
    [Fact]
    public void TwoObjectsKeepIndependentGeometry_NoDrift()
    {
        var t = T();
        // obj A(0x1000): V24=(100,500,0). obj B(0x2000): V24=(300,100,0).
        // Read each back; A's values must be unaffected by B's write.
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 0x2000),
            MovGI(3, 100), MovGI(4, 500), MovGI(5, 0),
            (0x219, new[]{ G(1), G(3), G(4), G(5) }),           // set B-vector on A
            MovGI(3, 300), MovGI(4, 100),
            (0x219, new[]{ G(2), G(3), G(4), G(5) }),           // set B-vector on B
            (0x21a, new[]{ G(1), G(10), G(11), G(12) }),        // read A back
            (0x21a, new[]{ G(2), G(20), G(21), G(22) }),        // read B back
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal((100L, 500L), (vm.Globals[10], vm.Globals[11]));   // A intact
        Assert.Equal((300L, 100L), (vm.Globals[20], vm.Globals[21]));   // B intact, no cross-contamination
    }
```

- [ ] **Step 2: Run**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~TwoObjectsKeep`
Expected: PASS (objects are independent by construction of `GfxState`).

- [ ] **Step 3: Commit**
```bash
git add engine/Age.Engine.Tests/GfxCommandBufferTests.cs
git commit -m "test(gfx): drift regression — independent per-object geometry

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3.7 — Host side: CLI oracle reads real slots; Godot guard

**Files:** Modify `engine/Age.Cli/Program.cs` (`GfxTraceHost`), `godot/Main.cs`.

- [ ] **Step 1: CLI gfx oracle — report real slots**

`Age.Cli gfx` builds a VM; after `vm.Run()`, the executed `set-texture`/`draw-texture` already log per the existing `GfxTraceHost`. Add, after the run, a dump of `vm.Gfx` object→slot assignments so the oracle shows distinct slots (not all 0). Concretely, extend the summary print in the `gfx` command block:
```csharp
Console.WriteLine($"  gfx objects: {string.Join(", ", /* iterate vm.Gfx slots */ )}");
```
(Expose a read-only enumeration on `GfxState`, e.g. `IEnumerable<(long Handle, int Slot)> Objects`, if not already; add a matching `GfxStateTests` fact.)

- [ ] **Step 2: Godot compositor — confirm per-slot + guard log**

In `godot/Main.cs`/`GodotAdvHost.cs`, confirm `BlitSlot`/`_slotBmp` already key on the slot the VM supplies (they do). Add a one-time `GD.Print("[gfx] alpha/blend deferred")` when a colored-draw path is hit, mirroring the VM guard, so the deferral is visible in the Godot log too.

- [ ] **Step 3: Build both**

Run: `dotnet build engine/AgeEngine.sln`
Run: `godot --headless --path godot --import && dotnet build godot/Himegari.csproj`
Expected: both succeed.

- [ ] **Step 4: Godot selftest parity**

Run: `godot --headless --path godot -- --selftest`
Expected: `SELFTEST OK …` (synthetic scene, no gfx ops → unaffected).

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Cli/Program.cs godot/Main.cs godot/GodotAdvHost.cs
git commit -m "feat(gfx): CLI oracle reports gfx slots; Godot alpha-deferred guard

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-review

- **Spec coverage:** GfxState model → 3.1; dispatch enablement (the `label` gotcha) → 3.2; all 13 ops (query/set/lifecycle/colored) → 3.3–3.5; drift regression → 3.6; oracle + compositor guard → 3.7. Deferral (alpha blend) implemented as state-now/guard → 3.5/3.7. ✓
- **Discovery gate:** the one open risk (vector pairing) was resolved before planning (V18/V24/V16c constants baked in); remaining simplifications (`0x212/0x213` idx table, `0x216` field table) are flagged non-drift-critical and validated in Phase 4. ✓
- **Placeholder scan:** every code step has complete code; the only prose-only step is 3.7 Step 1–2 (host glue that depends on the existing `GfxTraceHost`/`Main` shapes) — bounded and explicit. ✓
- **Type consistency:** `GfxState.GetOrCreate/TryGet/QuerySlot/QueryField/Release/PackColor` and `GfxObject.{Slot,V18,V24,V16c,Field64/68/6c,Color}` are used identically in Model, VM cases, and tests; VM dispatch strings match the Task-3.2 `label`s and the confirmed-constants table. ✓
- **Parity:** every op returns `pc+1`, one step; guard is observe-only; base-ISA/synthetic/selftest reaffirmed green in 3.4/3.5/3.7. ✓

**Next after Phase 3:** Phase 4 (validate against the real drift scene — `Age.Cli gfx` numbers + screenshot), per `docs/superpowers/plans/2026-07-07-gfx-command-buffer.md`.

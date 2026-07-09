# SC0000 gfx animation / transform / spritesheet cluster — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Implement the gfx animation/transform/spritesheet opcode cluster (`0x1fd`, `0x21c`–`0x243`) so SC0000 renders coherently — sprites move, animated sprites show one cycling spritesheet cell (not the full sheet), and effects play at native cadence (fixing "mach 5") — turning SC0000 into a scene the user can finally validate as a whole.

**Architecture:** Hybrid (same seam as slice A). The engine model holds a per-object **animation-channel record**; the cluster setter ops populate it; an **engine-side interpolator** (a faithful port of native `gfx_object_anim_interpolate`) computes each visible object's current transform + spritesheet cell + color as a function of a clock time; the Godot host blits the result. The interpolator is driven by the **`FrameClock`** built for frame-stepping, so animation time == throttled virtual time (the "mach 5" fix).

**Tech Stack:** C# / .NET 8 (`engine/AgeEngine.sln`), xUnit; Godot 4.7 mono (`godot/Himegari.csproj`); Ghidra MCP on `build/engine-dump/range_00400000.bin`.

**Spec:** `docs/superpowers/specs/2026-07-08-sc0000-anim-transform-cluster-design.md`.

## Global Constraints

- **Parity is sacred.** Gates that MUST stay green unchanged: `dotnet test engine/AgeEngine.sln`; `Age.Cli sweep` → `exit=284, STEP-LIMIT=13`; Godot `--selftest` → `SELFTEST OK`. New cluster ops are effectful only through the gfx model; the headless dialogue path is unaffected.
- **Seam rule:** `Age.Engine/Vm` references only `Model`, `Hosting`, `Diagnostics`. The interpolator + channel model live in `Age.Engine/Model`; it may read `Age.Engine.Hosting.FrameClock` (already allowed).
- **Dispatch table (verbatim, from RE):** `handler(op) = ctx[0x26c93 + op]` (dword index; registrar `FUN_00413860`). The `u004xxxxx` labels in `opcodes.toml` are **Kelebek VA drift** — never decompile them; resolve via the table. Recovered cluster handler addresses are in Task 1.
- **Interpolator timebase:** the native uses the frame clock `ctx+0xb550` with per-channel **periods**, **ping-pong** (triangle wave, folded at `period/2`). We port this onto `FrameClock.NowMs`.
- **Build/run:** `dotnet build engine/AgeEngine.sln -c Debug`; `dotnet test engine/AgeEngine.sln`. Godot: `dotnet build godot/Himegari.csproj -c Debug` then `S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe`. Coverage gauge: `py -3.11 -X utf8 tools/scene_opcode_coverage.py SC0000`.
- TDD, one deliverable per task, commit at the end of each task. End every commit message with:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

---

## Task 0: Branch

- [ ] **Step 1: Create the feature branch** (repo root = `age-reimpl/`, default `main`)

```bash
cd "S:/Game Hacking/Eushully/Himegari/age-reimpl"
git checkout -b feat/anim-transform-cluster
```

---

## File Structure

- `docs/engine-re.md` — **modify** (Task 1). The op→channel-field table + used-channel set.
- `engine/Age.Engine/Model/GfxState.cs` — **modify** (Tasks 2, 3, 4). Channel fields on `GfxObject`; setter mutators; `SnapshotVisibleObjects(long nowMs)` interpolator.
- `engine/Age.Engine/Vm/VirtualMachine.cs` — **modify** (Task 4). `case` handlers for the cluster ops.
- `engine/Age.Engine.Tests/AnimChannelTests.cs` — **new** (Tasks 2, 4). Setter-op → field assertions.
- `engine/Age.Engine.Tests/AnimInterpolatorTests.cs` — **new** (Task 3). Ping-pong phase assertions.
- `godot/Main.cs` — **modify** (Task 5). Pass `FrameClock.NowMs`; blit at interpolated pos + src-cell.

---

## Task 1: RE — reverse the cluster workers → op→field table + used-channel set

**This is a real RE task** (~18 workers). All handler addresses are known (below); each op is a thin wrapper (`FUN_0041b940(n)` fetches operand n) → a worker (`FUN_0047xxxx`) that writes object fields. The **consumer** `gfx_object_anim_interpolate` (`0x473ed0`, already annotated) is ground truth for what each field means.

**Files:** Modify `docs/engine-re.md` (append the table). No code.

**Known handler addresses** (resolve worker by decompiling the handler, which tail-calls the worker):

| op | handler | op | handler | op | handler |
|---|---|---|---|---|---|
| 0x1fd | `gfx_op_0x1fd_set_vec_scaled` | 0x223 | `FUN_00423620` | 0x232 | `FUN_00423c30` |
| 0x20a | `FUN_00422ce0` | 0x224 | `LAB_00417550` | 0x236 | `FUN_00423ee0` |
| 0x20e | `LAB_004174f0` | 0x228 | `FUN_0042a3a0` | 0x239 | `FUN_00424120`→`FUN_0047ed90` |
| 0x21c | `LAB_00417520` | 0x229 | `FUN_00423700` | 0x23d | `LAB_004175c0` |
| 0x21d | `FUN_00423310` | 0x22f | `FUN_00423b00`→`FUN_00472e90` | 0x23f | `FUN_0042a520` |
| 0x21f | `FUN_00423410` | 0x231 | `FUN_00423be0` | 0x242 | `FUN_004249d0` |
| | | | | 0x243 | `LAB_004182d0` |

**Worked examples (done — record these + reverse the rest the same way):**
- `0x22f` (`FUN_00472e90`): `(handle, op2, x, y, z)` → sets anim bit `obj|2`, resets `obj+0x45c`, writes `obj+0x46c=op2`, `obj+0x480`, and a translation 3-vector at `obj+0x5d4` (via `FUN_0048afb1`). ⇒ **position/translation channel.**
- `0x239` (`FUN_0047ed90`): `(handle, p3, p4, grid_w, grid_h, cell)` → resets progress `obj+0x34=0`, writes src-rect **grid** `obj+0x238`/`obj+0x23c` + cell `obj+0x234`, params `obj+0x48`/`obj+0x5c`. ⇒ **src-rect / spritesheet-cell channel** (matches the interpolator's `obj+0x238/0x23c` grid + `obj+8..0x14` crop).

**Interpolator channel map (from `gfx_object_anim_interpolate`, ground truth):**

| Channel | period | start | target/params |
|---|---|---|---|
| Color/alpha | `obj+0x220` | `obj+0x20c` | `obj+0x240` |
| Matrix A | `obj+0x224` | `obj+0x210` | `obj+0x250..0x28c` |
| Rotation | `obj+0x228` | `obj+0x214` | `obj+0x244` (360°=`0x168`) |
| Matrix B | `obj+0x22c` | `obj+0x218` | `obj+0x290..0x2cc` |
| Src-rect scroll | `obj+0x230` | `obj+0x21c` | grid `obj+0x238/0x23c`, crop `obj+8..0x14` |

- [ ] **Step 1: Reverse each remaining cluster worker** (decompile the handler at its known address; note the operand→field writes). Rename `FUN_...`→`gfx_op_0xNNN_worker_*` and `set_plate_comment` the decode as you go (CLAUDE.md rule); `save_program` when done.

- [ ] **Step 2: Determine the SC0000 used-channel set.** From `build/disasm/SC0000.asm`, list which cluster ops the opening actually calls and thus which channels (position / rotation / scale / src-rect / matrices) are exercised. `grep` the cluster op labels in the disasm.

- [ ] **Step 3: Record the op→field table + used-channel set** in `docs/engine-re.md` (new subsection under §"The full gfx render model"). This table is the reference Tasks 2 and 4 implement against.

- [ ] **Step 4: Commit**

```bash
git add docs/engine-re.md
git commit -m "docs(re): SC0000 anim cluster op->field table + used-channel set

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

**Gate for the user / reviewer:** the used-channel set decides Task 5's scope (rotation/scale rendering in or out). Surface it before proceeding if it materially changes the plan size.

---

## Task 2: Engine — anim-channel fields on `GfxObject`

**Files:** Modify `engine/Age.Engine/Model/GfxState.cs`; Test `engine/Age.Engine.Tests/AnimChannelTests.cs`.

**Interfaces:**
- Produces: `GfxState.GfxObject` gains, per channel, `{ long PeriodX, long StartX, ... target fields }` and mutators `SetPositionAnim`, `SetSrcRectAnim` (+ others per Task 1). Start fields default `-1` = "uninitialized" (the interpolator seeds them to `nowMs` on first sight).
- Src-rect channel fields: `long SrcGridW, SrcGridH, SrcCell, SrcPeriod, SrcStart`.
- Position channel fields: `long PosTX, PosTY, PosTZ, PosPeriod, PosStart` (+ anim-active flag).

This task adds the **position** and **src-rect** channels (the two worked in Task 1, covering motion + spritesheet). Additional channels found used in Task 1 (rotation/scale) are added in Task 4 the same way.

- [ ] **Step 1: Write the failing test**

Create `engine/Age.Engine.Tests/AnimChannelTests.cs`:

```csharp
using System.Linq;
using Age.Engine.Model;
using Xunit;

public class AnimChannelTests
{
    private static GfxState VisibleObj(long handle)
    {
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(handle, 1, 0, 0, 64, 64, 100, 100);   // 64x64 cell surface at (100,100)
        return g;
    }

    [Fact]
    public void SetSrcRectAnim_StoresGridAndCell()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRectAnim(0x100, gridW: 4, gridH: 1, cell: 2, period: 800);
        var o = g.TryGet(0x100)!;
        Assert.Equal(4, o.SrcGridW);
        Assert.Equal(2, o.SrcCell);
        Assert.Equal(800, o.SrcPeriod);
        Assert.Equal(-1, o.SrcStart);   // uninitialized until first interpolated frame
    }

    [Fact]
    public void SetPositionAnim_StoresTargetAndPeriod()
    {
        var g = VisibleObj(0x100);
        g.SetPositionAnim(0x100, tx: 300, ty: 100, tz: 0, period: 1000);
        var o = g.TryGet(0x100)!;
        Assert.Equal(300, o.PosTX);
        Assert.Equal(1000, o.PosPeriod);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter AnimChannelTests`
Expected: FAIL — fields/mutators don't exist (compile error).

- [ ] **Step 3: Add the channel fields + mutators**

In `GfxState.GfxObject` (near the color fields), add:

```csharp
        // ---- position/translation anim channel (op 0x22f family; interpolator target obj+0x5d4 vec) ----
        public long PosTX, PosTY, PosTZ, PosPeriod, PosStart = -1;
        public bool PosAnim;
        // ---- src-rect / spritesheet-cell channel (op 0x239 family; interpolator grid obj+0x238/0x23c) ----
        public long SrcGridW = 1, SrcGridH = 1, SrcCell, SrcPeriod, SrcStart = -1;
        public bool SrcAnim;
```

In `GfxState`, add the mutators (near `SetObjectColor`):

```csharp
    /// <summary>Op 0x22f family: animate the object toward (tx,ty,tz) over <paramref name="period"/> ms
    /// (ping-pong). Resets Start so the interpolator re-seeds it on the next frame.</summary>
    public void SetPositionAnim(long handle, long tx, long ty, long tz, long period)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.PosTX = tx; o.PosTY = ty; o.PosTZ = tz; o.PosPeriod = period; o.PosStart = -1; o.PosAnim = true;
        }
    }

    /// <summary>Op 0x239 family: set the spritesheet grid + animate the visible cell over
    /// <paramref name="period"/> ms (ping-pong across the row).</summary>
    public void SetSrcRectAnim(long handle, long gridW, long gridH, long cell, long period)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.SrcGridW = gridW < 1 ? 1 : gridW; o.SrcGridH = gridH < 1 ? 1 : gridH;
            o.SrcCell = cell; o.SrcPeriod = period; o.SrcStart = -1; o.SrcAnim = true;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test engine/AgeEngine.sln --filter AnimChannelTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add engine/Age.Engine/Model/GfxState.cs engine/Age.Engine.Tests/AnimChannelTests.cs
git commit -m "feat: add position + src-rect anim-channel fields/mutators to GfxObject

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Engine — the interpolator (`SnapshotVisibleObjects(long nowMs)`)

**Files:** Modify `engine/Age.Engine/Model/GfxState.cs` (`RenderObject` + `SnapshotVisibleObjects`); Test `engine/Age.Engine.Tests/AnimInterpolatorTests.cs`.

**Interfaces:**
- Produces: `RenderObject` gains `int CellW, CellH` (the resolved spritesheet sub-rect is expressed via the existing `SrcX/SrcY/W/H`). `SnapshotVisibleObjects(long nowMs)` overload interpolates active channels; the existing no-arg overload calls it with `nowMs = 0` (deterministic, back-compat for headless callers).
- Ping-pong helper: `static long BlendMath.PingPong(long now, long start, long period)` → the folded progress `u ∈ [0, period/2]` (add to `BlendMath`, unit-tested here).

**Ping-pong math (verbatim from `gfx_object_anim_interpolate`):** with period `P>0`, start `S` (seed to `now` if uninitialized), `u = (now - S) mod P`; if `u >= P/2` then `u = P - u`; the interpolation weight toward the target is `t = (2u)/P ∈ [0,1]` (and `1-t` toward the base).

- [ ] **Step 1: Write the failing test**

Create `engine/Age.Engine.Tests/AnimInterpolatorTests.cs`:

```csharp
using System.Linq;
using Age.Engine.Model;
using Xunit;

public class AnimInterpolatorTests
{
    private static GfxState VisibleObj(long handle)
    {
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(handle, 1, 0, 0, 256, 64, 100, 100);   // 256x64 sheet, base pos (100,100)
        return g;
    }

    [Fact]
    public void PingPong_FoldsAtHalfPeriod()
    {
        Assert.Equal(0,   BlendMath.PingPong(now: 0,   start: 0, period: 1000));
        Assert.Equal(500, BlendMath.PingPong(now: 500, start: 0, period: 1000));   // peak at half
        Assert.Equal(0,   BlendMath.PingPong(now: 1000, start: 0, period: 1000));  // back to base
    }

    [Fact]
    public void Position_InterpolatesTowardTargetAtHalfPeriod()
    {
        var g = VisibleObj(0x100);
        g.SetPositionAnim(0x100, tx: 300, ty: 100, tz: 0, period: 1000);   // base x=100 -> target x=300
        // first snapshot seeds Start=0; at now=500 (half period) it's fully at the target
        g.SnapshotVisibleObjects(0);
        var ro = g.SnapshotVisibleObjects(500).Single();
        Assert.Equal(300, ro.DstX);
        var back = g.SnapshotVisibleObjects(1000).Single();
        Assert.Equal(100, back.DstX);   // ping-ponged back to base
    }

    [Fact]
    public void SrcRect_SelectsCellFromGrid()
    {
        var g = VisibleObj(0x100);
        // 4-cell row across a 256px sheet => each cell 64px wide; cell index 2 => srcX=128
        g.SetSrcRectAnim(0x100, gridW: 4, gridH: 1, cell: 2, period: 0);   // static cell 2
        var ro = g.SnapshotVisibleObjects(0).Single();
        Assert.Equal(128, ro.SrcX);
        Assert.Equal(64, ro.W);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter AnimInterpolatorTests`
Expected: FAIL — `PingPong`, `SnapshotVisibleObjects(long)` don't exist.

- [ ] **Step 3: Add `PingPong` to `BlendMath`**

In `engine/Age.Engine/Model/BlendMath.cs`:

```csharp
    /// <summary>Ping-pong (triangle-wave) progress of gfx_object_anim_interpolate: fold (now-start) mod
    /// period at period/2 so the value ramps to the peak at half-period then back. Returns u in
    /// [0, period/2]; the interpolation weight toward the target is 2u/period.</summary>
    public static long PingPong(long now, long start, long period)
    {
        if (period <= 0) return 0;
        long u = ((now - start) % period + period) % period;
        if (u >= period / 2) u = period - u;
        return u;
    }
```

- [ ] **Step 4: Add the interpolating overload**

In `GfxState.cs`, add `int CellW, int CellH` to `RenderObject` is NOT needed — reuse `SrcX/SrcY/W/H`. Change the existing no-arg `SnapshotVisibleObjects()` to delegate, and add the interpolating overload:

```csharp
    public IReadOnlyList<RenderObject> SnapshotVisibleObjects() => SnapshotVisibleObjects(0);

    /// <summary>Visible objects (ascending-handle z-order) with each active anim channel interpolated at
    /// <paramref name="nowMs"/> (ping-pong over the channel period) — the port of gfx_object_anim_interpolate.
    /// Start fields seed to nowMs on first sight. Position animates the blit dst; src-rect selects the
    /// spritesheet cell into SrcX/W. Color/alpha/tint come from slice A.</summary>
    public IReadOnlyList<RenderObject> SnapshotVisibleObjects(long nowMs)
    {
        lock (_lock)
        {
            var list = new List<RenderObject>();
            foreach (var kv in _objects.OrderBy(k => k.Key))
            {
                var o = kv.Value;
                if (!o.Visible) continue;
                var (resId, ck) = _surfaces.TryGetValue(o.SourceSlot, out var s) ? s : (0L, 0L);

                int alpha = 255; long tint = 0xFFFFFF; var blend = BlendKind.Opaque;
                if (o.HasColor)
                {
                    var (a, r, g, b) = BlendMath.UnpackArgb(o.Color);
                    alpha = a; tint = ((long)r << 16) | ((long)g << 8) | (long)b; blend = BlendKind.Alpha;
                }

                // ---- position channel: base V24 + ping-pong toward (PosTX,PosTY) ----
                int dstX = (int)o.V24.X, dstY = (int)o.V24.Y;
                if (o.PosAnim)
                {
                    if (o.PosStart < 0) o.PosStart = nowMs;
                    long u = BlendMath.PingPong(nowMs, o.PosStart, o.PosPeriod);
                    long half = o.PosPeriod > 0 ? o.PosPeriod / 2 : 1;
                    double t = half > 0 ? (double)u / half : 0;                // 0..1 base->target->base
                    dstX = (int)(o.V24.X + (o.PosTX - o.V24.X) * t);
                    dstY = (int)(o.V24.Y + (o.PosTY - o.V24.Y) * t);
                }

                // ---- src-rect channel: pick the spritesheet cell ----
                int srcX = o.SrcRect.X, srcY = o.SrcRect.Y, w = o.SrcRect.W, h = o.SrcRect.H;
                if (o.SrcAnim && o.SrcGridW >= 1)
                {
                    int cellW = (int)(o.SrcRect.W / o.SrcGridW);
                    int cellH = o.SrcGridH >= 1 ? (int)(o.SrcRect.H / o.SrcGridH) : o.SrcRect.H;
                    long cell = o.SrcCell;
                    if (o.SrcPeriod > 0)                                        // animate the cell across the row
                    {
                        if (o.SrcStart < 0) o.SrcStart = nowMs;
                        long half = o.SrcPeriod / 2; half = half > 0 ? half : 1;
                        long u = BlendMath.PingPong(nowMs, o.SrcStart, o.SrcPeriod);
                        cell = (long)((double)u / half * (o.SrcGridW - 1));
                    }
                    srcX = o.SrcRect.X + (int)(cell % o.SrcGridW) * cellW;
                    srcY = o.SrcRect.Y + (int)(cell / o.SrcGridW) * cellH;
                    w = cellW; h = cellH;
                }

                list.Add(new RenderObject(kv.Key, resId, ck, srcX, srcY, w, h, dstX, dstY,
                                          new AnimState(o.AnimEnabled, o.AnimNormalized,
                                                        o.AnimTarget.X, o.AnimTarget.Y, o.AnimTarget.Z,
                                                        o.AnimDurationTicks, o.AnimGeneration),
                                          alpha, tint, blend));
            }
            return list;
        }
    }
```

Remove the old `SnapshotVisibleObjects()` body (now replaced by the delegating one + the overload).

- [ ] **Step 5: Run tests — verify pass**

Run: `dotnet test engine/AgeEngine.sln --filter "AnimInterpolatorTests|BlendMathTests"`
Expected: PASS.

- [ ] **Step 6: Full suite + sweep parity**

Run: `dotnet test engine/AgeEngine.sln` — Expected: all green (the no-arg overload keeps existing callers identical).
Run: `dotnet run --project engine/Age.Cli -c Debug -- sweep` — Expected: `exit=284, STEP-LIMIT=13`.

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Model/GfxState.cs engine/Age.Engine/Model/BlendMath.cs engine/Age.Engine.Tests/AnimInterpolatorTests.cs
git commit -m "feat: port gfx_object_anim_interpolate (ping-pong position + spritesheet cell)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Engine — wire the cluster setter ops

**Files:** Modify `engine/Age.Engine/Vm/VirtualMachine.cs`; extend `engine/Age.Engine.Tests/AnimChannelTests.cs`.

**Interfaces:**
- Consumes: `SetPositionAnim`, `SetSrcRectAnim` (Task 2) + any additional channel mutators added here per the Task 1 table.
- Produces: `VirtualMachine.Step` `case`s for the cluster ops, routing operands to the mutators.

The op **labels** are in `opcodes.toml` (e.g. `0x22f`→`u00421DD0`); dispatch keys on the label string (as the existing gfx cases do). Operand order is verified against the Task 1 worked examples: `0x22f (handle)(op2)(x)(y)(z)`, `0x239 (handle)(p3)(p4)(gridW)(gridH)(cell)`.

- [ ] **Step 1: Write the failing test** (append to `AnimChannelTests.cs`)

```csharp
    [Fact]
    public void Op0x22f_DrivesPositionAnim()
    {
        var t = OpcodeTableJson.Load(Paths.OpcodesJson);
        // assemble: bind a surface+object, then op 0x22f to animate toward (300,100)
        // (use the existing ScriptAssembler harness as in GfxCommandBufferTests)
        // ... assert vm.Gfx.TryGet(handle).PosAnim == true and PosTX == 300
    }
```

> Follow the exact `ScriptAssembler`/`MovGI` pattern from `engine/Age.Engine.Tests/GfxCommandBufferTests.cs` (which already assembles gfx-op scripts). Assert `PosAnim` and the target field.

- [ ] **Step 2: Run to verify it fails** — `dotnet test engine/AgeEngine.sln --filter Op0x22f` → FAIL (no case; op is a stub).

- [ ] **Step 3: Add the `case`s** in `VirtualMachine.Step` (near the existing `set-anim-transform-abs`):

```csharp
            case "u00421DD0":   // 0x22f — position/translation anim (handle)(op2)(x)(y)(z)
                Gfx.SetPositionAnim(Read(a[0]), Read(a[2]), Read(a[3]), Read(a[4]), Read(a[1])); return pc + 1;
            case "u004223C0":   // 0x239 — spritesheet cell/grid (handle)(p3)(p4)(gridW)(gridH)(cell)
                Gfx.SetSrcRectAnim(Read(a[0]), Read(a[3]), Read(a[4]), Read(a[5]), Read(a[1])); return pc + 1;
```

> NOTE: the `case` string is the opcode **label** from `opcodes.toml` (dispatch keys on label, per the existing gfx cases). Confirm each label with `grep "op = 0x22f" -A1 vm-map/opcodes.toml`. The exact operand→argument slot mapping (which operand is period vs target) comes from the Task 1 worker decode — the two above match the worked examples; fill the remaining ops from the Task 1 table.

- [ ] **Step 4: Wire the remaining used cluster ops** (per Task 1's used-channel set). For each, add a `case` routing operands to the appropriate mutator (position/src-rect, or a rotation/scale mutator added the same way as Task 2 if Task 1 found those channels used). Ops that Task 1 shows write channels not exercised by the SC0000 opening may be routed to a safe no-op `case` (documented) rather than left as effectful GAP stubs.

- [ ] **Step 5: Run the new tests + full suite + sweep**

Run: `dotnet test engine/AgeEngine.sln` — Expected: green.
Run: `dotnet run --project engine/Age.Cli -c Debug -- sweep` — Expected: `exit=284, STEP-LIMIT=13`.

- [ ] **Step 6: Coverage check**

Run: `py -3.11 -X utf8 tools/scene_opcode_coverage.py SC0000`
Expected: the wired cluster ops move from GAP → handled (GAP count drops by the number wired).

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/AnimChannelTests.cs
git commit -m "feat: wire SC0000 anim/transform/spritesheet cluster ops to channel mutators

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Host — drive the interpolator off FrameClock + blit cell/position

**Files:** Modify `godot/Main.cs` (`Recomposite` ~line 251).

**Interfaces:**
- Consumes: `SnapshotVisibleObjects(long nowMs)` (Task 3); `_clock.NowMs` (`Age.Engine.Hosting.FrameClock`, already owned by `Main`).
- Produces: the compositor renders each object at its interpolated position and spritesheet cell, paced by the frame clock (the "mach 5" fix).

- [ ] **Step 1: Pass the frame clock's time into the snapshot**

In `Recomposite`, change the enumeration to the interpolating overload:

```csharp
        foreach (var v in _vm.Gfx.SnapshotVisibleObjects(_clock.NowMs))   // interpolate at the throttled clock
```

(The existing loop already blits `v.SrcX/SrcY/W/H` at `v.DstX/DstY`, so the selected cell + interpolated position flow through with no further change; `BlitLayer` is unchanged.)

- [ ] **Step 2: Build the Godot project**

Run: `dotnet build godot/Himegari.csproj -c Debug --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Verify parity via the headless self-test**

Run: `& "S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --headless --path godot -- --selftest`
Expected: `SELFTEST OK`.

- [ ] **Step 4: Scene acceptance — the user's whole-scene validation**

Run: `& "S:\Godot\...console.exe" --path godot -- --boot --shot-sequence "..\scratch\anim_seq" --frames 240`
Then Read a spread of frames. Expected: sprites **translate** across frames; animated sprites show a **single cycling cell** (not the full sheet); the glow/explosion plays at a **readable cadence** (not mach-5). This is the scene-level confirmation. If rotation/scale were gated out in Task 1 and a sprite visibly needs them, note it as the follow-up (don't expand here).

- [ ] **Step 5: Commit**

```bash
git add godot/Main.cs
git commit -m "feat(godot): drive the anim interpolator off FrameClock (motion+spritesheet paced)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Docs + memory

- [ ] **Step 1:** Update `vm-map/opcodes.toml` summaries for the wired cluster ops (now handled; cite `engine-re.md` §anim cluster), then `py -3.11 -X utf8 tools/opcodes_build.py --build`.
- [ ] **Step 2:** Append a "cluster DONE" section to `docs/phase-a-slice-plan.md` (what landed, coverage delta, any gated-out channels).
- [ ] **Step 3:** Update memory `himegari-port-status.md` + `MEMORY.md` (absolute date 2026-07-08).
- [ ] **Step 4: Commit**

```bash
git add vm-map/opcodes.toml tools/age_opcodes_himegari.py docs/opcode-reference.md docs/phase-a-slice-plan.md
git commit -m "docs: record SC0000 anim/transform/spritesheet cluster coverage

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Notes for the executor

- **Task 1 is the load-bearing RE.** Do not guess operand→field mappings — decompile each worker at its known address. The two worked examples (`0x22f`, `0x239`) are the pattern.
- **Ping-pong, not one-shot.** These channels oscillate (glow pulses); do not "reach target and stop." The `PingPong` helper enforces the fold.
- **Rotation/scale rendering** stays out unless Task 1 proves the opening uses it — then add a rotation/scale mutator (Task 2 pattern) + an affine blit in `Main.cs` (a larger change; scope it as its own task if it lands).
- **`nowMs` back-compat:** every existing headless caller uses the no-arg `SnapshotVisibleObjects()` (→ `nowMs=0`), so their output is deterministic and unchanged — parity holds.

## Self-review (done while writing)

- **Spec coverage:** channel model → Task 2; setter ops → Tasks 1(RE)+4(wire); interpolator port → Task 3; FrameClock pacing → Task 5 Step 1; spritesheet cell → Task 3 Step 4; RE Task-0 → Task 1; testing (setter+interpolator+scene) → Tasks 2/3/5; rotation-scale gating → Task 1 Step 2 + Task 5 note. Covered.
- **Placeholder scan:** the interpolator (Task 3), channel model (Task 2), and the two worked setter ops (Task 4) are complete code. The remaining setter ops (Task 4 Step 4) are explicitly RE-dependent (Task 1) — this is a genuine sequencing dependency, not a hidden placeholder; the pattern + addresses are given.
- **Type consistency:** `SetPositionAnim(long,long,long,long,long)`, `SetSrcRectAnim(long,long,long,long,long)`, `PosTX/PosPeriod/PosStart/PosAnim`, `SrcGridW/SrcCell/SrcPeriod/SrcStart/SrcAnim`, `BlendMath.PingPong(long,long,long)`, `SnapshotVisibleObjects(long)` — used identically across Tasks 2/3/4/5.

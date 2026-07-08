# Sprite Transform/Animation Subsystem (Opening Slice) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make SC0000's opening `AE*` fade actually animate — implement the sprite transform/animation ops the booted opening executes, tweened over wall-clock time in the Godot compositor with alpha.

**Architecture:** The VM records animation-channel data on gfx objects (passive `GfxState`, no time) exactly like the existing gfx command-buffer ops, so headless trace parity is untouched. The Godot compositor owns a per-handle tween table, interpolates the recorded target over its frame delta, and applies alpha in the blit. Native DirectDraw workers are not modeled.

**Tech Stack:** C# / .NET 8 (`Age.Engine`, xUnit), Godot 4.7 .NET (`godot/`), Python 3.11 tooling (`opcodes_build.py`, `scene_opcode_coverage.py`), Ghidra+MCP for the RE gate.

## Global Constraints

- **Run Python as** `py -3.11 -X utf8 tools/<name>.py …` (cp932 needs utf8 mode on Windows).
- **Never hand-edit generated files.** Edit `vm-map/opcodes.toml`, then `py -3.11 -X utf8 tools/opcodes_build.py --build` regenerates `build/opcodes.json` + `tools/age_opcodes_himegari.py` + `docs/opcode-reference.md`.
- **VM dispatches on the opcode's top-level `label`** in `opcodes.toml` (the `0x208`/`0x215` precedent). Setting a `label` to a dispatch name is how an op becomes handled.
- **Headless parity is sacred:** the VM must only *record* gfx state; non-Godot hosts (`CaptureHost`, test `RecordingHost`/`CountHost`) read none of it. Existing `--selftest`, `sweep`, and engine tests must stay byte-identical.
- **`GfxState` stays a passive data model** — no time/`Tick`; it exposes recorded data only.
- **Ghidra: annotate as you RE** — rename `FUN_...` → `gfx_op_0x<op>_<role>`, add a plate comment with the decode, `save_program`. The Ghidra program is `range_00400000.bin` (Raw x86:LE:32:default @0x400000); handler for op N = the value stored to `param_1[0x26c93 + N]` in `FUN_00413860`.
- **Test commands:** all engine tests `dotnet test engine/AgeEngine.sln --nologo`; one class `dotnet test engine/AgeEngine.sln --nologo --filter "FullyQualifiedName~<ClassName>"`.
- **Branch:** continue on `feat/gfx-command-buffer`. Commit after each task.

---

## File Structure

- `docs/engine-re.md` — RE addendum: `anim_start`/`set_anim_clock` operand contract + confirmed opening op subset (Task 1).
- `engine/Age.Engine/Model/GfxState.cs` — animation-channel fields + `SetAnimTransform`/`StartAnim`/`SetAnimClock`; `RenderObject` gains an `AnimState` (Tasks 2, 5).
- `vm-map/opcodes.toml` — labels for `0x21e`/`0x220`/`0x234`/`0x238` (Task 3).
- `engine/Age.Engine/Vm/VirtualMachine.cs` — `case` arms for the four ops (Tasks 3, 4).
- `engine/Age.Engine.Tests/GfxAnimationTests.cs` — unit tests (Tasks 2–5).
- `godot/Main.cs` — tween table + alpha-aware blit in `Recomposite`/`BlitLayer` (Task 6).
- `docs/phase-a-slice-plan.md`, status memory — results (Task 7).

---

## Task 1: RE gate — decode `anim_start`/`set_anim_clock` + confirm the opening subset

**Files:**
- Modify: `docs/engine-re.md` (append to the "0x21c–0x243 sprite transform / ANIMATION cluster" section)
- Ghidra project `range_00400000.bin` (annotations only)

**Interfaces:**
- Produces: **the operand contract** for `anim-start` (op `0x234`) and `set-anim-clock` (op `0x238`) — for each: `argc`, and the role of each operand (handle? duration/ticks? channel index?). Also: **the confirmed list of cluster ops SC0000's `--boot` opening actually executes**, and **which quantity the fade animates** (the `0x21e/0x220` transform vec3 at `obj+0xac`, vs the packed alpha from `0x202/0x203`). Tasks 3–6 consume this.

- [ ] **Step 1: Resolve the two handler addresses.** In Ghidra, open `FUN_00413860` (`decompile_function` at `0x00413860`). The dispatch slot for op N is `param_1[0x26c93 + N]`. Read the value at index `0x26ec7` (= `0x234`, `gfx_op_0x234_anim_start`) and `0x26ecb` (= `0x238`, `gfx_op_0x238_set_anim_clock`). They sit between `LAB_00423cf0` (op `0x233`) and `FUN_00423e40` (op `0x235`), and between `FUN_004240a0` (op `0x237`) and `FUN_00424120` (op `0x239`) respectively.

- [ ] **Step 2: Decompile both handlers.** `decompile_function` at each address. Read the cmd-type they write (`*(ctx + 0x53d88 + ctx[0x53d14]*0x78) = <cmd>`), the operand fetches (`FUN_0041b940(i)`, 1-based), and the worker they call. Compare to the decoded `0x220`/`0x21e` (both write cmd-type `0xd`, call a transform worker). Determine: does `anim_start` take a handle + start the channel (`obj+0x68`/a "playing" flag)? Does `set_anim_clock` set a duration/tick count (and on which object/scope)?

- [ ] **Step 3: Annotate in Ghidra.** `rename_function_by_address` each to `gfx_op_0x234_anim_start` / `gfx_op_0x238_set_anim_clock` (if not already), `set_plate_comment` with the decode (argc, operand roles, cmd-type, worker), then `save_program`.

- [ ] **Step 4: Confirm the opening's executed op subset + animated quantity.** Statically inspect the opening path in `build/disasm/SC0000.asm` around the `AE*` CG loads (search the `set-texture`/`draw-texture` sites near the fade), and cross-check dynamically:

Run: `py -3.11 -X utf8 tools/sys4load.py "$(py -3.11 -X utf8 -c "import sys;sys.path.insert(0,'tools');import paths;print(paths.scripts()['SC0000.BIN'])")" --json > build/sc0000.json` is optional; the reliable read is the disasm. Grep the disasm for the cluster ops near the fade:

Run: `py -3.11 -X utf8 -c "import re;lines=open('build/disasm/SC0000.asm',encoding='utf8').read().splitlines();[print(l) for l in lines if re.search(r'op 0x(21e|220|234|238|202|203|204)\b', l)][:60]"`
Expected: shows whether the fade region uses `0x21e/0x220` (transform) and/or `0x202/0x203` (color/alpha), and whether `0x234`/`0x238` appear. Record the finding.

- [ ] **Step 5: Write the addendum + decision.** Append to `docs/engine-re.md`: the two operand contracts, the confirmed opening op subset, and the **scope decision** — if the fade's alpha is `0x202/0x203`-driven (not the transform ops), note that Task 6 must apply that packed alpha and the transform ops become a secondary concern; otherwise confirm the transform-tween scope. This decision is the input to Tasks 3/4/6.

- [ ] **Step 6: Commit**

```bash
git add docs/engine-re.md
git commit -m "docs(gfx): decode anim_start/set_anim_clock + confirm SC0000 opening anim subset"
```

---

## Task 2: `GfxState` animation-channel data model

**Files:**
- Modify: `engine/Age.Engine/Model/GfxState.cs`
- Test: `engine/Age.Engine.Tests/GfxAnimationTests.cs` (create)

**Interfaces:**
- Consumes: nothing (pure model).
- Produces: on `GfxState.GfxObject` — fields `(long X,long Y,long Z) AnimTarget`, `long AnimParam1`, `long AnimParam2`, `bool AnimNormalized`, `bool AnimEnabled`, `long AnimDurationTicks`, `long AnimGeneration`. Methods `void SetAnimTransform(long handle, long p1, long p2, (long X,long Y,long Z) target, bool normalized)`, `void StartAnim(long handle)`, `void SetAnimClock(long handle, long durationTicks)`. Consumed by Tasks 3, 4, 5.

- [ ] **Step 1: Write the failing tests** — create `engine/Age.Engine.Tests/GfxAnimationTests.cs`:

```csharp
using Age.Engine.Model;
using Xunit;

public class GfxAnimationTests
{
    [Fact]
    public void SetAnimTransform_RecordsTargetParamsAndEnables()
    {
        var g = new GfxState();
        g.SetAnimTransform(0x1000, p1: 7, p2: 9, target: (100, 100, 100), normalized: true);
        var o = g.TryGet(0x1000)!;
        Assert.Equal((100L, 100L, 100L), o.AnimTarget);
        Assert.Equal(7, o.AnimParam1);
        Assert.Equal(9, o.AnimParam2);
        Assert.True(o.AnimNormalized);
        Assert.True(o.AnimEnabled);
    }

    [Fact]
    public void StartAnim_BumpsGenerationEachCall()
    {
        var g = new GfxState();
        g.SetAnimTransform(0x1000, 0, 0, (0, 0, 0), false);
        var gen0 = g.TryGet(0x1000)!.AnimGeneration;
        g.StartAnim(0x1000);
        g.StartAnim(0x1000);
        Assert.Equal(gen0 + 2, g.TryGet(0x1000)!.AnimGeneration);
    }

    [Fact]
    public void SetAnimClock_SetsDurationTicks()
    {
        var g = new GfxState();
        g.SetAnimClock(0x1000, 30);
        Assert.Equal(30, g.TryGet(0x1000)!.AnimDurationTicks);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test engine/AgeEngine.sln --nologo --filter "FullyQualifiedName~GfxAnimationTests"`
Expected: FAIL — `GfxObject` has no `AnimTarget` etc. (compile error).

- [ ] **Step 3: Add the fields to `GfxObject`.** In `GfxState.cs`, inside `public sealed class GfxObject`, after the existing `public bool Visible;` line add:

```csharp
        // ---- animation channel (0x21e/0x220 transform-set, 0x234 anim-start, 0x238 set-anim-clock;
        // native worker gfx_anim_set_channel@0x47eaa0 writes obj+0xac target / +0x3c,+0x50 params / +0x68 enable).
        // Passive: recorded here, interpolated by the Godot compositor over wall-clock. See
        // docs/superpowers/specs/2026-07-07-gfx-animation-subsystem-design.md. ----
        public (long X, long Y, long Z) AnimTarget;
        public long AnimParam1, AnimParam2;
        public bool AnimNormalized;         // 0x21e (operand/_DAT_00571c28, ~percent) vs 0x220 (absolute)
        public bool AnimEnabled;            // obj+0x68
        public long AnimDurationTicks;      // from set-anim-clock (0x238)
        public long AnimGeneration;         // bumped by anim-start (0x234); the compositor's re-trigger
```

- [ ] **Step 4: Add the methods.** In `GfxState.cs`, after the `BindDraw` method, add:

```csharp
    /// <summary>Op 0x21e/0x220 (set-anim-transform): record the transform target + params on the object and
    /// enable its animation channel. normalized = 0x21e (operands are ~percent, /_DAT_00571c28); absolute = 0x220.
    /// Native: gfx_anim_set_channel@0x47eaa0 sets obj+0x3c=p1, +0x50=p2, +0xac=target, +0x68=1.</summary>
    public void SetAnimTransform(long handle, long p1, long p2, (long X, long Y, long Z) target, bool normalized)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.AnimParam1 = p1; o.AnimParam2 = p2; o.AnimTarget = target;
            o.AnimNormalized = normalized; o.AnimEnabled = true;
        }
    }

    /// <summary>Op 0x234 (anim-start): begin the object's animation. Bumps AnimGeneration — the compositor
    /// (re)starts a wall-clock tween whenever this changes.</summary>
    public void StartAnim(long handle)
    {
        lock (_lock) { var o = GetOrCreate(handle); o.AnimEnabled = true; o.AnimGeneration++; }
    }

    /// <summary>Op 0x238 (set-anim-clock): set the animation duration (in game ticks/frames) for the object.</summary>
    public void SetAnimClock(long handle, long durationTicks)
    {
        lock (_lock) { GetOrCreate(handle).AnimDurationTicks = durationTicks; }
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test engine/AgeEngine.sln --nologo --filter "FullyQualifiedName~GfxAnimationTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Run the full engine suite for parity**

Run: `dotnet test engine/AgeEngine.sln --nologo`
Expected: PASS, all prior tests still green (adding fields/methods touches nothing existing).

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Model/GfxState.cs engine/Age.Engine.Tests/GfxAnimationTests.cs
git commit -m "feat(gfx): GfxState animation-channel model (transform target + clock + generation)"
```

---

## Task 3: Label + dispatch the transform-set ops `0x21e`/`0x220`

**Files:**
- Modify: `vm-map/opcodes.toml` (ops `0x21e`, `0x220`, `0x234`, `0x238`)
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs` (add `case` arms)
- Test: `engine/Age.Engine.Tests/GfxAnimationTests.cs` (add)

**Interfaces:**
- Consumes: `GfxState.SetAnimTransform` (Task 2).
- Produces: opcode labels `set-anim-transform-norm` (0x21e), `set-anim-transform-abs` (0x220), `anim-start` (0x234), `set-anim-clock` (0x238) in the rebuilt `build/opcodes.json`; VM `case` arms for the two transform ops. Consumed by Tasks 4, 5.

- [ ] **Step 1: Set the labels in `opcodes.toml`.** For each of the four ops, set the top-level `label` and the `[opcode.semantics]` `name`/`category`/`summary`/`source`/`confidence`. Op `0x220` block:

```toml
op = 0x220
label = "set-anim-transform-abs"
argc = 6
```
and its `[opcode.semantics]`:
```toml
name = "set-anim-transform-abs"
category = "graphics"
summary = "(handle)(p1)(p2)(x)(y)(z) — set sprite transform/anim channel, absolute; cmd-type 0xd, worker 0x47ecc0. Cluster 0x21c-0x243."
noop_headless = false
source = "investigation"
confidence = "high"
```
Do the same for `0x21e` (`label = "set-anim-transform-norm"`, summary notes `/_DAT_00571c28` normalization, worker `gfx_anim_set_channel@0x47eaa0`), `0x234` (`label = "anim-start"`, category `graphics`), `0x238` (`label = "set-anim-clock"`, category `graphics`). Keep each op's existing `argc` unless Task 1 found otherwise for `0x234`/`0x238`.

- [ ] **Step 2: Rebuild the generated opcode files**

Run: `py -3.11 -X utf8 tools/opcodes_build.py --build`
Expected: `build: wrote build/opcodes.json, …`. Then `py -3.11 -X utf8 tools/opcodes_build.py --lint` → `lint: 0 errors, 0 warnings`.

- [ ] **Step 3: Write the failing dispatch test.** First, at the **top of `GfxAnimationTests.cs`** (with the existing `using Age.Engine.Model; using Xunit;`), add the usings this and later tasks need:

```csharp
using System.Collections.Generic;
using Age.Engine.Sys4;
using Age.Engine.Vm;
```

Then add these helpers + test **inside the `GfxAnimationTests` class**:

```csharp
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int a) => new(3, a);
    private static Operand I(long v) => new(0, v);
    private static (int, Operand[]) MovGI(int d, long v) => (0x55, new[] { G(d), I(v) });
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void SetAnimTransformAbs_DispatchRecordsChannel()
    {
        var t = T();
        // handle g[1]=0x1000; p1 g[2]=7; p2 g[3]=9; target g[4,5,6]=(800,500,0)
        var scene = ScriptAssembler.Assemble(t, "ANIM", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 7), MovGI(3, 9), MovGI(4, 800), MovGI(5, 500), MovGI(6, 0),
            (0x220, new[] { G(1), G(2), G(3), G(4), G(5), G(6) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.Equal((800L, 500L, 0L), o.AnimTarget);
        Assert.False(o.AnimNormalized);
        Assert.True(o.AnimEnabled);
    }
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --nologo --filter "FullyQualifiedName~SetAnimTransformAbs_DispatchRecordsChannel"`
Expected: FAIL — op `0x220` hits the `default` stub, so `AnimTarget` stays `(0,0,0)`.

- [ ] **Step 5: Add the `case` arms.** In `VirtualMachine.Step`, alongside the other gfx ops (before `default:`), add:

```csharp
            case "set-anim-transform-abs":   // 0x220 (handle)(p1)(p2)(x)(y)(z)
                Gfx.SetAnimTransform(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5])), normalized: false); return pc + 1;
            case "set-anim-transform-norm":  // 0x21e — same, operands are ~percent (/_DAT_00571c28)
                Gfx.SetAnimTransform(Read(a[0]), Read(a[1]), Read(a[2]),
                    (Read(a[3]), Read(a[4]), Read(a[5])), normalized: true); return pc + 1;
```

- [ ] **Step 6: Run to verify it passes + full suite parity**

Run: `dotnet test engine/AgeEngine.sln --nologo`
Expected: PASS, all green (audio/gfx CLI + selftest unaffected — new ops only write GfxState, which `RecordingHost` ignores).

- [ ] **Step 7: Commit**

```bash
git add vm-map/opcodes.toml build/opcodes.json tools/age_opcodes_himegari.py docs/opcode-reference.md engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/GfxAnimationTests.cs
git commit -m "feat(gfx): dispatch set-anim-transform 0x21e/0x220 into GfxState"
```

---

## Task 4: Dispatch `anim-start` (0x234) + `set-anim-clock` (0x238)

**Files:**
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs`
- Test: `engine/Age.Engine.Tests/GfxAnimationTests.cs` (add)

**Interfaces:**
- Consumes: Task 1's operand contract for `0x234`/`0x238`; `GfxState.StartAnim`/`SetAnimClock` (Task 2); labels from Task 3.
- Produces: `case "anim-start"` / `case "set-anim-clock"` arms.

> **Operand mapping — reconcile with Task 1's `engine-re.md` addendum before coding.** The code below assumes `anim-start` = `(handle)` and `set-anim-clock` = `(handle)(durationTicks)`. If Task 1 found `set-anim-clock` is argc-1 (duration only, applied to the current object), use `Gfx.SetAnimClock(Gfx.CurrentObject, Read(a[0]))` instead. Adjust the indices to match the documented contract; the test asserts whatever mapping Task 1 confirmed.

- [ ] **Step 1: Write the failing test** — add to `GfxAnimationTests.cs` (using the mapping Task 1 confirmed; shown here for `(handle)` / `(handle)(ticks)`):

```csharp
    [Fact]
    public void AnimStartAndClock_DispatchBumpGenerationAndSetDuration()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "ANIM", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 30),
            (0x238, new[] { G(1), G(2) }),       // set-anim-clock(handle=0x1000, ticks=30)
            (0x234, new[] { G(1) }),             // anim-start(handle=0x1000)
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.Equal(30, o.AnimDurationTicks);
        Assert.Equal(1, o.AnimGeneration);
        Assert.True(o.AnimEnabled);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --nologo --filter "FullyQualifiedName~AnimStartAndClock"`
Expected: FAIL — both ops hit `default`; `AnimDurationTicks`/`AnimGeneration` stay 0.

- [ ] **Step 3: Add the `case` arms** (matching Task 1's contract):

```csharp
            case "anim-start":      // 0x234 (handle) — begin the object's animation
                Gfx.StartAnim(Read(a[0])); return pc + 1;
            case "set-anim-clock":  // 0x238 (handle)(durationTicks)
                Gfx.SetAnimClock(Read(a[0]), Read(a[1])); return pc + 1;
```

- [ ] **Step 4: Run to verify it passes + full suite**

Run: `dotnet test engine/AgeEngine.sln --nologo`
Expected: PASS, all green.

- [ ] **Step 5: Commit**

```bash
git add engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/GfxAnimationTests.cs
git commit -m "feat(gfx): dispatch anim-start 0x234 + set-anim-clock 0x238"
```

---

## Task 5: Surface the anim channel to the compositor (`RenderObject.AnimState`)

**Files:**
- Modify: `engine/Age.Engine/Model/GfxState.cs` (`RenderObject`, `AnimState`, `SnapshotVisibleObjects`)
- Test: `engine/Age.Engine.Tests/GfxAnimationTests.cs` (add)

**Interfaces:**
- Consumes: the `GfxObject` anim fields (Task 2).
- Produces: `public readonly record struct AnimState(bool Enabled, bool Normalized, long TX, long TY, long TZ, long DurationTicks, long Generation);` and `RenderObject` gains a trailing `AnimState Anim` member. Consumed by Task 6.

- [ ] **Step 1: Write the failing test** — add to `GfxAnimationTests.cs`:

```csharp
    [Fact]
    public void SnapshotCarriesAnimStateForVisibleObject()
    {
        var t = T();
        // make object 0xA visible via set/draw-texture, then arm an anim channel + clock + start.
        var scene = ScriptAssembler.Assemble(t, "ANIM", new List<(int, Operand[])>
        {
            MovGI(1, 0xA), MovGI(2, 4), MovGI(7, 0x25), MovGI(3, 800), MovGI(4, 600), MovGI(5, 0), MovGI(6, 0),
            (0x1f9, new[] { G(7), G(2), I(0) }),                       // set-texture resId 0x25 -> slot 4
            (0x1fb, new[] { G(1), G(2), I(0), I(0), G(3), G(4), G(5), G(6) }),  // draw-texture: object 0xA visible
            MovGI(8, 0), MovGI(9, 30),
            (0x220, new[] { G(1), G(5), G(6), G(3), G(4), G(8) }),     // anim target (800,600,0)
            (0x238, new[] { G(1), G(9) }),                            // clock 30
            (0x234, new[] { G(1) }),                                  // start
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var vis = vm.Gfx.SnapshotVisibleObjects();
        Assert.Single(vis);
        Assert.True(vis[0].Anim.Enabled);
        Assert.Equal((800L, 600L, 0L), (vis[0].Anim.TX, vis[0].Anim.TY, vis[0].Anim.TZ));
        Assert.Equal(30, vis[0].Anim.DurationTicks);
        Assert.Equal(1, vis[0].Anim.Generation);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --nologo --filter "FullyQualifiedName~SnapshotCarriesAnimState"`
Expected: FAIL — `RenderObject` has no `Anim` member (compile error).

- [ ] **Step 3: Add `AnimState` + extend `RenderObject`.** In `GfxState.cs`, replace the `RenderObject` record declaration with:

```csharp
public readonly record struct AnimState(bool Enabled, bool Normalized, long TX, long TY, long TZ,
                                        long DurationTicks, long Generation);

/// <summary>A renderable view of one visible gfx object … (existing summary).</summary>
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY,
                                           AnimState Anim);
```

- [ ] **Step 4: Populate it in `SnapshotVisibleObjects`.** In that method's loop, change the `list.Add(...)` to include the anim state:

```csharp
                list.Add(new RenderObject(kv.Key, resId, ck, o.SrcRect.X, o.SrcRect.Y, o.SrcRect.W, o.SrcRect.H,
                                          (int)o.V24.X, (int)o.V24.Y,
                                          new AnimState(o.AnimEnabled, o.AnimNormalized,
                                                        o.AnimTarget.X, o.AnimTarget.Y, o.AnimTarget.Z,
                                                        o.AnimDurationTicks, o.AnimGeneration)));
```

- [ ] **Step 5: Run to verify it passes + full suite.** The existing `SetThenDrawTextureMakesAVisibleObjectFromTheSurface` test in `GfxCommandBufferTests` constructs `RenderObject` positionally only via the snapshot (not `new`), so it stays valid.

Run: `dotnet test engine/AgeEngine.sln --nologo`
Expected: PASS, all green.

- [ ] **Step 6: Commit**

```bash
git add engine/Age.Engine/Model/GfxState.cs engine/Age.Engine.Tests/GfxAnimationTests.cs
git commit -m "feat(gfx): surface anim channel to the compositor via RenderObject.AnimState"
```

---

## Task 6: Godot compositor — wall-clock tween + alpha blit

**Files:**
- Modify: `godot/Main.cs` (`Recomposite`, `BlitLayer`; add a tween table + `TweenState`)

**Interfaces:**
- Consumes: `RenderObject.Anim` (Task 5).
- Produces: animated, alpha-composited rendering. Verified visually (Godot; no unit test).

> **Scope from Task 1:** if Task 1 found the opening fade's alpha comes from `0x202/0x203` (packed color on the object) rather than the transform vec3, drive the blit alpha from `RenderObject.ColorKey`/the object color instead of (or in addition to) the tweened target. The tween mechanics below are unchanged; only the *source of the alpha value* differs. Wire whichever Task 1 confirmed.

- [ ] **Step 1: Add the tween state + table.** In `Main.cs`, add fields near the other compositor fields:

```csharp
    // Per-handle wall-clock tween of the animation channel. Re-armed whenever an object's AnimGeneration or
    // target changes (the VM records those; the compositor owns time). See the gfx-animation-subsystem spec.
    private sealed class TweenState
    {
        public long Generation = long.MinValue;
        public double Elapsed, Duration;
        public (double X, double Y, double Z) Start, Target;
    }
    private readonly System.Collections.Generic.Dictionary<long, TweenState> _tweens = new();
    private const double GameTickSeconds = 1.0 / 60.0;   // duration ticks -> seconds (confirm FPS in Task 1)
```

- [ ] **Step 2: Advance tweens + compute the current alpha in `Recomposite`.** Replace the body of the `foreach (var v in _vm.Gfx.SnapshotVisibleObjects())` loop so each object computes an alpha (1.0 when not animating) and passes it to the blit:

```csharp
        foreach (var v in _vm.Gfx.SnapshotVisibleObjects())   // ascending handle = z-order
        {
            if (v.SurfaceResId == 0) continue;                 // render-target/blank surface
            var bmp = _host.ResolveResIdTexture(v.SurfaceResId);
            if (bmp == null) continue;

            float alpha = 1f;
            if (v.Anim.Enabled)
            {
                var tw = _tweens.TryGetValue(v.Handle, out var t) ? t : (_tweens[v.Handle] = new TweenState());
                if (tw.Generation != v.Anim.Generation)        // (re)arm the tween
                {
                    double norm = v.Anim.Normalized ? 100.0 : 1.0;   // 0x21e ~percent; confirm divisor in Task 1
                    tw.Generation = v.Anim.Generation;
                    tw.Elapsed = 0;
                    tw.Duration = System.Math.Max(1, v.Anim.DurationTicks) * GameTickSeconds;
                    tw.Target = (v.Anim.TX / norm, v.Anim.TY / norm, v.Anim.TZ / norm);
                    tw.Start = (1, 1, 1);                       // fade-from-full default; refine per Task 1
                }
                tw.Elapsed += _lastDelta;
                double p = System.Math.Clamp(tw.Elapsed / tw.Duration, 0, 1);
                // First interpretation: the target's Z (or X) channel is opacity. Task 1 pins which channel.
                double a = tw.Start.Z + (tw.Target.Z - tw.Start.Z) * p;
                alpha = (float)System.Math.Clamp(a, 0, 1);
            }
            BlitLayer(bmp, v.SrcX, v.SrcY, v.W, v.H, v.DstX, v.DstY, alpha);
        }
```

Add a `private double _lastDelta;` field and set it at the top of `_Process`: `_lastDelta = delta;`.

- [ ] **Step 3: Make `BlitLayer` alpha-aware.** Change its signature to `BlitLayer(string bmpPath, int srcX, int srcY, int w, int h, int dstX, int dstY, float alpha = 1f)`. Keep the fast opaque path when `alpha >= 0.999f` (existing `BlitRect`). Otherwise blend over the raw RGBA byte buffer:

```csharp
        if (alpha >= 0.999f) { _screen.BlitRect(src, new Rect2I(srcX, srcY, sw, sh), new Vector2I(dstX, dstY)); return; }
        byte[] dst = _screen.GetData(); byte[] ss = src.GetData();
        int dw = _screen.GetWidth(), sfw = src.GetWidth();
        int ia = (int)(alpha * 255);
        for (int y = 0; y < sh; y++)
        for (int x = 0; x < sw; x++)
        {
            int di = ((dstY + y) * dw + (dstX + x)) * 4;
            int si = ((srcY + y) * sfw + (srcX + x)) * 4;
            if (di < 0 || di + 3 >= dst.Length) continue;
            int sa = ss[si + 3] * ia / 255;                    // source alpha * object alpha
            for (int c = 0; c < 3; c++) dst[di + c] = (byte)((ss[si + c] * sa + dst[di + c] * (255 - sa)) / 255);
            dst[di + 3] = (byte)System.Math.Min(255, dst[di + 3] + sa);
        }
        _screen.SetData(dw, _screen.GetHeight(), false, _screen.GetFormat(), dst);
```

- [ ] **Step 4: Build + import Godot**

Run: `dotnet build godot/Himegari.csproj -v q` then `godot --headless --path godot --import`
Expected: build succeeds, import completes.

- [ ] **Step 5: Capture the opening fade across frames.** Grab the fade page at a couple of time offsets and eyeball that it changes (fades) rather than staying opaque:

Run: `godot --path godot -- --boot --shot build/shot-anim-p2.png --shot-page 2`
Expected: a PNG is written; opening CG present. Compare against a pre-change capture (`git stash` the Godot change, capture, unstash) — the animated effect should differ across the tween where before it was static/opaque. Record the observation.

- [ ] **Step 6: Commit**

```bash
git add godot/Main.cs
git commit -m "feat(gfx): Godot wall-clock tween + alpha-aware blit for the anim channel"
```

---

## Task 7: Verify the slice + update the living docs

**Files:**
- Modify: `docs/phase-a-slice-plan.md`, `~/.claude/…/memory/himegari-port-status.md`, `~/.claude/…/memory/MEMORY.md`

**Interfaces:** none (verification + docs).

- [ ] **Step 1: Confirm the tracker GAP shrank.**

Run: `py -3.11 -X utf8 tools/scene_opcode_coverage.py SC0000`
Expected: the implemented ops (`0x21e`,`0x220`,`0x234`,`0x238`) now report **impl**, not GAP; the GAP count dropped by that many (from 68). Record the new number.

- [ ] **Step 2: Confirm full headless parity.**

Run: `dotnet test engine/AgeEngine.sln --nologo` → all green.
Run: `godot --headless --path godot -- --selftest` → `SELFTEST OK`.
Run: `dotnet run --project engine/Age.Cli -- sweep` → same 294 exit / 3 LOOP as before (anim ops are record-only).
Expected: no regressions.

- [ ] **Step 3: Update `docs/phase-a-slice-plan.md`.** In the A2b section, add a short subsection recording: the ops implemented, the tween/alpha approach, the tracker GAP delta, the screenshot result, and any Task-1 corrections (operand contracts, animated channel, divisor/FPS).

- [ ] **Step 4: Update the status memory.** In `himegari-port-status.md`, append a milestone entry (ops done, GAP shrink, what's still deferred — the rest of the `0x21c–0x243` cluster + scene-coroutine timing) and refresh its one-line `MEMORY.md` index entry. Convert relative dates to absolute.

- [ ] **Step 5: Commit**

```bash
git add docs/phase-a-slice-plan.md
git commit -m "docs(gfx): record the anim-subsystem opening slice results"
```

---

## Self-review notes (for the executor)

- **Task 1 is the gate.** Do not skip it — Tasks 4 and 6 explicitly reconcile against its findings (operand contract for `0x234`/`0x238`; whether the fade alpha is transform- or `0x202/0x203`-driven; the normalization divisor and game FPS). The plan's default assumptions are the current best hypotheses, not confirmed facts.
- **Parity guard**: after every C# task, `dotnet test engine/AgeEngine.sln --nologo` must stay fully green — the anim ops only write `GfxState`, which non-Godot hosts never read.
- **YAGNI**: only the four opening ops are wired; the rest of `0x21c–0x243` stays on the `default` stub and keeps showing as GAP in the tracker (intentional, measurable remaining work).

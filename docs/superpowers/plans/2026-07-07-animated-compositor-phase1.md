# Animated Compositor — Phase 1: Retained Compositor Implementation Plan (TDD)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Replace the immediate-mode permanent canvas with a **retained-object compositor** — `draw-texture` records/updates a persistent layer keyed by its object handle; the Godot host clears + re-composites all active layers each frame; `0x1f7` erase removes them. This is the foundation for alpha (Phase 2) and time-animation (Phase 3).

**Architecture:** The retained layer set lives in **`GfxState`** (VM-owned, version-neutral, thread-safe): the VM's `draw-texture` updates it (additive — engine parity preserved), `EraseRange` removes from it. The Godot host stops immediate-blitting; `Main._Process` snapshots the layers each frame and composites them (in execution order = current correct paint order) using the host's slot→image map. Design: `docs/superpowers/specs/2026-07-07-animated-compositor-design.md`.

**Tech Stack:** C# / .NET 8 (`engine/`, xUnit); Godot 4.7 .NET (`godot/`).

## Global Constraints

- **Composite order = execution order, handle-keyed update-in-place** (matches the current, correct immediate-mode render — the guardrail is "booted SC0000 CGs must not regress"). Erase removes by handle. This ordering policy is the Phase-1 design choice; validate by screenshot no-regression.
- **Engine parity:** `draw-texture` gains a `GfxState` update but stays one step / `pc+1`; non-Godot hosts are unaffected (they read nothing new). `dotnet test engine/AgeEngine.sln` must stay green; Godot `--selftest` (synthetic, no gfx) must stay green.
- **Thread-safety:** `GfxState` layers are mutated on the VM thread and read on the Godot main thread → all layer access is `lock`-guarded; the host reads via an immutable `SnapshotLayers()`.
- **No auto-screenshot:** per the user, do NOT screenshot-and-quit for validation; the user drives the live window. Provide the numeric `Age.Cli gfx` oracle for headless checks.
- **Seam:** `GfxState` is `Age.Engine/Model`; the VM references `Model`. `DrawLayer` lives with `GfxState`.

## File structure

| File | Responsibility | Task |
|---|---|---|
| `engine/Age.Engine/Model/GfxState.cs` | `DrawLayer` + retained layer list (add/update, remove, snapshot; lock-guarded); `EraseRange` also drops layers | 1.1 |
| `engine/Age.Engine.Tests/GfxStateTests.cs` | Layer add/update-in-place/order/erase unit tests | 1.1 |
| `engine/Age.Engine/Vm/VirtualMachine.cs` | `draw-texture` records a `DrawLayer` in `GfxState` | 1.2 |
| `engine/Age.Engine.Tests/GfxCommandBufferTests.cs` | Synthetic scene: draws → layer list; erase → removed | 1.2 |
| `engine/Age.Cli/Program.cs` (`gfx` cmd) | Dump the retained layer list (headless oracle) | 1.3 |
| `godot/GodotAdvHost.cs` | `DrawTexture` no longer blits (retained loop composites); keep slot→BMP map | 1.4 |
| `godot/Main.cs` | `_Process` snapshots `GfxState` layers → clear + composite each frame | 1.4 |

---

### Task 1.1 — `DrawLayer` + retained layer list in `GfxState` (pure data)

**Files:** Modify `engine/Age.Engine/Model/GfxState.cs`; Test `engine/Age.Engine.Tests/GfxStateTests.cs`.

**Interfaces produced:** `readonly record struct DrawLayer(long Handle, int Slot, int SrcX, int SrcY, int W, int H, int DstX, int DstY)`; on `GfxState`: `void AddOrUpdateLayer(DrawLayer l)` (update-in-place by `Handle`, else append — preserving order), `void RemoveLayers(long handle)`, `IReadOnlyList<DrawLayer> SnapshotLayers()`. `EraseRange` also calls `RemoveLayers` per erased handle.

- [ ] **Step 1: Write the failing tests**
```csharp
    [Fact]
    public void LayersAppendInOrderAndUpdateInPlace()
    {
        var g = new GfxState();
        g.AddOrUpdateLayer(new DrawLayer(0xA, 4, 0, 0, 800, 600, 0, 0));
        g.AddOrUpdateLayer(new DrawLayer(0xB, 5, 0, 0, 200, 200, 100, 100));
        g.AddOrUpdateLayer(new DrawLayer(0xA, 4, 0, 0, 800, 600, 0, 50));   // re-draw A -> update in place
        var s = g.SnapshotLayers();
        Assert.Equal(2, s.Count);
        Assert.Equal(0xA, s[0].Handle);           // order preserved (A still first)
        Assert.Equal(50, s[0].DstY);              // updated
        Assert.Equal(0xB, s[1].Handle);
    }

    [Fact]
    public void EraseRangeAlsoDropsLayers()
    {
        var g = new GfxState();
        g.AddOrUpdateLayer(new DrawLayer(0x10, 4, 0, 0, 10, 10, 0, 0));
        g.AddOrUpdateLayer(new DrawLayer(0x20, 5, 0, 0, 10, 10, 0, 0));
        g.EraseRange(0x10, 1);
        var s = g.SnapshotLayers();
        Assert.Single(s);
        Assert.Equal(0x20, s[0].Handle);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~GfxStateTests`
Expected: FAIL — `DrawLayer`/`AddOrUpdateLayer` don't exist (compile error).

- [ ] **Step 3: Implement**

In `engine/Age.Engine/Model/GfxState.cs`, add the record (top of namespace) and the layer members:
```csharp
public readonly record struct DrawLayer(long Handle, int Slot, int SrcX, int SrcY, int W, int H, int DstX, int DstY);
```
Inside `GfxState`:
```csharp
    private readonly List<DrawLayer> _layers = new();
    private readonly object _lock = new();

    public void AddOrUpdateLayer(DrawLayer l)
    {
        lock (_lock)
        {
            for (int i = 0; i < _layers.Count; i++)
                if (_layers[i].Handle == l.Handle) { _layers[i] = l; return; }
            _layers.Add(l);
        }
    }

    public void RemoveLayers(long handle)
    {
        lock (_lock) { _layers.RemoveAll(l => l.Handle == handle); }
    }

    public IReadOnlyList<DrawLayer> SnapshotLayers()
    {
        lock (_lock) { return _layers.ToArray(); }
    }
```
Extend `EraseRange` to also drop layers — change its body to:
```csharp
    public void EraseRange(long handle, long count)
    {
        if (count > 1) for (long i = handle; i < handle + count; i++) { Release(i); RemoveLayers(i); }
        else { Release(handle); RemoveLayers(handle); }
    }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~GfxStateTests`
Expected: PASS (all facts, including the pre-existing ones).

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Engine/Model/GfxState.cs engine/Age.Engine.Tests/GfxStateTests.cs
git commit -m "feat(gfx): retained DrawLayer list in GfxState (thread-safe)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 1.2 — `draw-texture` records a retained layer

**Files:** Modify `engine/Age.Engine/Vm/VirtualMachine.cs`; Test `engine/Age.Engine.Tests/GfxCommandBufferTests.cs`.

**Interfaces:** Consumes `GfxState.AddOrUpdateLayer`. The `draw-texture` case builds a `DrawLayer` from `a[0]`(handle),`a[1]`(slot),`a[2..7]` and records it; it **keeps** the existing `_host.DrawTexture(...)` call (the CLI oracle logs it; the Godot host will no-op-blit in Task 1.4).

- [ ] **Step 1: Write the failing test** (in `GfxCommandBufferTests.cs`)
```csharp
    private static (int, Operand[]) DrawTex(int handle, int slot, int w, int h, int dx, int dy)
        => (0x1fb, new[] { G(handle), G(slot), I(0), I(0), G(w), G(h), G(dx), G(dy) });

    [Fact]
    public void DrawTextureRecordsARetainedLayer()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "GFX", new List<(int, Operand[])>
        {
            MovGI(1, 0xA), MovGI(2, 4), MovGI(3, 800), MovGI(4, 600), MovGI(5, 0), MovGI(6, 0),
            DrawTex(1, 2, 3, 4, 5, 6), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var layers = vm.Gfx.SnapshotLayers();
        Assert.Single(layers);
        Assert.Equal(0xA, layers[0].Handle);
        Assert.Equal((800, 600, 0, 0), (layers[0].W, layers[0].H, layers[0].DstX, layers[0].DstY));
    }
```
(Note: `draw-texture` reads slot from `a[1]`, dst from `a[6]/a[7]`, w/h from `a[4]/a[5]` — mirror the existing case's operand indices exactly.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~DrawTextureRecords`
Expected: FAIL — no layer recorded (`Empty` snapshot).

- [ ] **Step 3: Implement** — in `VirtualMachine.Step`, extend the `draw-texture` case:
```csharp
            case "draw-texture":   // (handle, slot, srcX, srcY, w, h, dstX, dstY)
                Gfx.AddOrUpdateLayer(new DrawLayer(Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]),
                                     (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])));
                _host.DrawTexture((int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]),
                                  (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7])); return pc + 1;
```

- [ ] **Step 4: Run to verify pass + full suite (parity)**

Run: `dotnet test engine/AgeEngine.sln`
Expected: all green — `draw-texture` still one step; non-Godot hosts unaffected; new test passes.

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/GfxCommandBufferTests.cs
git commit -m "feat(gfx): draw-texture records a retained layer in GfxState

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 1.3 — CLI `gfx` oracle dumps the retained layer list

**Files:** Modify `engine/Age.Cli/Program.cs` (the `gfx` command, after the run).

- [ ] **Step 1: Add the layer dump** — after the existing `gfx objects` print:
```csharp
    var layers = vm.Gfx.SnapshotLayers();
    Console.WriteLine($"  layers ({layers.Count}, composite order):");
    foreach (var l in layers)
        Console.WriteLine($"    h=0x{l.Handle:x} slot={l.Slot} src=({l.SrcX},{l.SrcY} {l.W}x{l.H}) dst=({l.DstX},{l.DstY})");
```

- [ ] **Step 2: Build + eyeball on the drift scene**

Run: `dotnet run --project engine/Age.Cli -- gfx --boot SC0000.BIN`
Expected: prints a retained layer list. Sanity: the full-screen CGs appear as layers at `(0,0) 800x600`; erased handles are absent (fewer layers than raw draw-texture calls). This is the headless proof the retained set is built correctly.

- [ ] **Step 3: Commit**
```bash
git add engine/Age.Cli/Program.cs
git commit -m "feat(gfx): gfx oracle dumps the retained layer list

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 1.4 — Godot: per-frame clear + recomposite from the retained layers

**Files:** Modify `godot/GodotAdvHost.cs`, `godot/Main.cs`.

**Interfaces:** Consumes `_vm.Gfx.SnapshotLayers()` + the host's slot→BMP map. Produces a per-frame composite in `Main._Process`.

- [ ] **Step 1: Godot host — stop immediate-blitting; keep slot→BMP**

In `godot/GodotAdvHost.cs`, make `DrawTexture` a no-op for compositing (the retained loop handles it) but keep the slot→BMP map current (still populated by `SetTexture`). Expose the slot→BMP map (e.g. `public string? SlotBmp(int slot)`), since `Main._Process` needs it to composite a layer's slot.

- [ ] **Step 2: Main — per-frame recomposite in `_Process`**

In `godot/Main.cs`, give `Main` access to the VM (`_vm` already a field) and the host. In `_Process`, each frame:
```csharp
    // Retained recomposite: clear, then blit each layer (execution order) from its slot's BMP.
    var layers = _vm.Gfx.SnapshotLayers();
    _screen.Fill(new Color(0, 0, 0, 0));                 // clear (transparent; the frame is rebuilt)
    foreach (var l in layers)
    {
        var bmp = _host.SlotBmp(l.Slot);
        if (bmp == null) continue;
        BlitLayer(bmp, l.SrcX, l.SrcY, l.W, l.H, l.DstX, l.DstY);   // same clamp logic as the old BlitSlot
    }
    _screenTex.Update(_screen);
```
`BlitLayer` is the old `BlitSlot` body (load BMP, clamp src rect, `BlitRect`) minus the per-call `_screenTex.Update` (do one update after the loop). Cache loaded `Image`s by path to avoid re-reading every frame (a `Dictionary<string, Image>`), since `_Process` runs every frame.

**Guardrail:** the booted opening CGs must render identically to before (same execution-order composite, now rebuilt each frame). If a CG flickers/disappears, the layer set or order is wrong — check the `gfx --boot` oracle layer dump against the expected draws.

- [ ] **Step 3: Build**

Run: `godot --headless --path godot --import && dotnet build godot/Himegari.csproj`
Expected: builds clean.

- [ ] **Step 4: Selftest parity**

Run: `godot --headless --path godot -- --selftest`
Expected: `SELFTEST OK …` (synthetic scene, no gfx layers → unaffected).

- [ ] **Step 5: Live check (user-driven, NOT auto-screenshot)**

Launch for the user in the background: `godot --path godot -- --boot`. Ask them to confirm: (a) the opening CGs still render correctly (no regression), and (b) the stuck bottom overlay no longer persists indefinitely across pages (it's now rebuilt per frame from the active layer set; it clears when its handle is erased or no longer drawn). *A still-opaque, non-fading glow while its handle is active is EXPECTED at Phase 1 — alpha is Phase 2, fading is Phase 3.*

- [ ] **Step 6: Commit**
```bash
git add godot/GodotAdvHost.cs godot/Main.cs
git commit -m "feat(gfx): retained per-frame compositor in Godot (clear + recomposite)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-review

- **Spec coverage (Phase 1):** retained layer model in `GfxState` → 1.1; `draw-texture` retained → 1.2; per-frame clear + recomposite → 1.4; headless oracle → 1.3. Alpha/animation explicitly NOT here (Phases 2/3). ✓
- **Parity:** `draw-texture` stays one step + keeps `_host.DrawTexture`; non-Godot hosts read nothing new; engine suite + selftest re-asserted (1.2 Step 4, 1.4 Step 4). ✓
- **Threading:** all `_layers` access lock-guarded; host reads via immutable `SnapshotLayers()`; the Image cache prevents per-frame disk reads. ✓
- **Placeholder scan:** 1.1–1.3 have complete code; 1.4 is host glue (concrete approach + key code, validated live) — the honest boundary (no unit oracle for pixels). ✓
- **Guardrail explicit:** "booted CGs must not regress" stated at 1.4; the Phase-1 layer-order/reset policy (execution-order, handle-keyed, erase-removes) is called out for screenshot validation. ✓

**Next:** Phase 2 (alpha/blend) then Phase 3 (time-animation), per the design spec.

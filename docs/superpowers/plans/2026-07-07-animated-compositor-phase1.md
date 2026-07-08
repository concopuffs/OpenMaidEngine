# Animated Compositor — Phase 1 (Surfaces + Objects + Composite) — TDD Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use `- [ ]` checkboxes.

**Goal:** Implement the RE-confirmed render model — a **SurfaceStore** + per-object **source slot / rect /
position / visible** in `GfxState`, and a Godot host that **clears and composites the visible objects in
ascending-handle order** from their live surface each frame. No alpha/colorkey/animation yet (opaque). This
replaces the reverted flat-layer attempt and must render the booted CGs correctly (the guardrail).

**Model (see `docs/engine-re.md` "The full gfx render model" + the design spec):** surfaces are image buffers
per slot (create/set-texture); objects reference a surface by slot (live) + a rect + a position (V24) + a
visible flag (draw-texture); the render loop iterates objects by ascending handle (= z-order) and blits each
visible object's surface-rect at its position.

**Tech:** C#/.NET 8 (`engine/`, xUnit), Godot 4.7 .NET.

## Global constraints

- **Model-first, no guessing.** If an assumption isn't in the RE, RE it or pin it via the `gfx` oracle before coding on it.
- **Guardrail:** `godot -- --boot` opening CGs must render correctly at Phase 1 (the reverted attempt failed this).
- **Parity:** VM ops stay one step / `pc+1`; non-Godot hosts unaffected; `dotnet test` + Godot `--selftest` stay green.
- **Threading:** `GfxState` mutated on the VM thread, read on the Godot main thread → `lock`-guarded; host reads one immutable snapshot per frame.
- **Seam:** `GfxState` stores `ResId`/`ColorKey`/geometry (version-neutral); the host resolves `ResId → BMP` via `ResourceMap`.
- **No auto-screenshot** for validation — user drives the live window.

## File structure

| File | Responsibility | Task |
|---|---|---|
| `engine/Age.Engine/Model/GfxState.cs` | `SurfaceStore` (slot→{resId,colorkey}); object `SourceSlot`/`SrcRect`/`Visible`; `RenderObject` snapshot (visible, ascending-handle) | 1.1 |
| `engine/Age.Engine.Tests/GfxStateTests.cs` | surface set/get; object bind; ascending-handle visible snapshot | 1.1 |
| `engine/Age.Engine/Vm/VirtualMachine.cs` | `set/create-texture` → surface; `draw-texture` → object bind (slot/rect/pos/visible) | 1.2 |
| `engine/Age.Engine.Tests/GfxCommandBufferTests.cs` | synthetic scene: set-texture + draw-texture → a visible render object | 1.2 |
| `engine/Age.Cli/Program.cs` (`gfx`) | dump visible render objects (handle order) with resolved surface | 1.3 |
| `godot/GodotAdvHost.cs`, `godot/Main.cs` | per-frame clear + composite from `SnapshotVisibleObjects()` | 1.4 |

---

### Task 1.1 — `SurfaceStore` + object fields + `RenderObject` snapshot (pure data)

**Interfaces produced:** on `GfxState`: `void SetSurface(int slot, long resId, long colorKey)`;
`void ClearSurface(int slot)` (create-texture blank); object mutators `void BindDraw(long handle, int slot,
int sx, int sy, int w, int h, int dstX, int dstY)` (sets SourceSlot/SrcRect/Position(V24)/Visible=true);
`IReadOnlyList<RenderObject> SnapshotVisibleObjects()` (visible objects, ascending handle, with resolved
surface resId+colorkey + rect + position). `readonly record struct RenderObject(long Handle, long SurfaceResId,
long ColorKey, int SrcX, int SrcY, int W, int H, int DstX, int DstY)`. `GfxObject` gains `int SourceSlot=-1`,
`(int X,int Y,int W,int H) SrcRect`, `bool Visible`.

- [ ] **Step 1: failing tests** (append to `GfxStateTests.cs`)
```csharp
    [Fact]
    public void BindDrawMakesAVisibleRenderObjectFromItsSurface()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x25, 0);                        // load resId 0x25 into surface slot 4
        g.BindDraw(0xcb2a, 4, 0, 0, 800, 600, 0, 0);     // object 0xcb2a draws surface 4 at (0,0)
        var vis = g.SnapshotVisibleObjects();
        Assert.Single(vis);
        Assert.Equal(0xcb2a, vis[0].Handle);
        Assert.Equal(0x25, vis[0].SurfaceResId);          // resolved from the object's source slot
        Assert.Equal((800, 600, 0, 0), (vis[0].W, vis[0].H, vis[0].DstX, vis[0].DstY));
    }

    [Fact]
    public void VisibleObjectsComeInAscendingHandleOrder()   // ascending handle == z-order
    {
        var g = new GfxState();
        g.SetSurface(4, 0x1, 0); g.SetSurface(5, 0x2, 0);
        g.BindDraw(0xcf08, 5, 0, 0, 10, 10, 0, 0);        // higher handle
        g.BindDraw(0xcb20, 4, 0, 0, 10, 10, 0, 0);        // lower handle
        var vis = g.SnapshotVisibleObjects();
        Assert.Equal(new long[] { 0xcb20, 0xcf08 }, vis.Select(v => v.Handle).ToArray());
    }
```

- [ ] **Step 2:** run → FAIL (missing members). `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~GfxStateTests`

- [ ] **Step 3: implement** in `GfxState.cs`
```csharp
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY);
```
On `GfxObject` add: `public int SourceSlot = -1; public (int X, int Y, int W, int H) SrcRect; public bool Visible;`
On `GfxState` (all `_surfaces`/object access under the existing `_lock`):
```csharp
    private readonly Dictionary<int, (long ResId, long ColorKey)> _surfaces = new();
    public void SetSurface(int slot, long resId, long colorKey) { lock (_lock) { _surfaces[slot] = (resId, colorKey); } }
    public void ClearSurface(int slot) { lock (_lock) { _surfaces[slot] = (0, 0); } }

    public void BindDraw(long handle, int slot, int sx, int sy, int w, int h, int dstX, int dstY)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.SourceSlot = slot; o.SrcRect = (sx, sy, w, h); o.V24 = (dstX, dstY, 0); o.Visible = true;
        }
    }

    public IReadOnlyList<RenderObject> SnapshotVisibleObjects()
    {
        lock (_lock)
        {
            var list = new List<RenderObject>();
            foreach (var kv in _objects.OrderBy(k => k.Key))
            {
                var o = kv.Value;
                if (!o.Visible) continue;
                var (resId, ck) = _surfaces.TryGetValue(o.SourceSlot, out var s) ? s : (0L, 0L);
                list.Add(new RenderObject(kv.Key, resId, ck, o.SrcRect.X, o.SrcRect.Y, o.SrcRect.W, o.SrcRect.H, (int)o.V24.X, (int)o.V24.Y));
            }
            return list;
        }
    }
```
(Add `using System.Linq;` if needed. `GetOrCreate` already exists; `_lock` already exists from the prior layer work — reuse it. Remove the old `DrawLayer`/`_layers`/`AddOrUpdateLayer`/`SnapshotLayers`/`RemoveLayers` from the reverted flat model if still present, and the `EraseRange`→`RemoveLayers` call.)

- [ ] **Step 4:** run → PASS. **Step 5:** commit (`feat(gfx): SurfaceStore + object bind + ascending-handle render snapshot`).

---

### Task 1.2 — VM: `set/create-texture` → surface; `draw-texture` → object bind

- [ ] **Step 1: failing test** (`GfxCommandBufferTests.cs`): a synthetic scene that `set-texture(0x25, slot 4)` then `draw-texture(handle 0xcb2a, slot 4, 800x600 @ 0,0)` yields one visible `RenderObject` with `SurfaceResId==0x25`. (Use op `0x1f9` set-texture args `(resId, slot, colorkey)`, op `0x1fb` draw-texture args `(handle, slot, sx, sy, w, h, dx, dy)`.)

- [ ] **Step 2:** run → FAIL.

- [ ] **Step 3: implement** — in `VirtualMachine.Step`:
  - `set-texture` (`0x1f9`): `Gfx.SetSurface((int)Read(a[1]), Read(a[0]), a.Count > 2 ? Read(a[2]) : 0);` then keep `_host.SetTexture(...)` (host still loads dims for `get-texture-size`).
  - `create-texture` (`0x1f8`): `Gfx.ClearSurface((int)Read(a[0]));` then keep `_host.CreateTexture(...)`.
  - `draw-texture` (`0x1fb`): `Gfx.BindDraw(Read(a[0]), (int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]), (int)Read(a[5]), (int)Read(a[6]), (int)Read(a[7]));` — and **drop the `_host.DrawTexture` call** (the retained compositor renders now; the Godot host's `DrawTexture` becomes a no-op in 1.4).

- [ ] **Step 4:** run full suite → green (parity). **Step 5:** commit.

---

### Task 1.3 — CLI `gfx` oracle: dump visible render objects (handle order)

- [ ] **Step 1:** after the run, replace/add the layer dump with:
```csharp
    var vis = vm.Gfx.SnapshotVisibleObjects();
    Console.WriteLine($"  visible objects ({vis.Count}, ascending-handle = z-order):");
    foreach (var v in vis)
        Console.WriteLine($"    h=0x{v.Handle:x} surf=0x{v.SurfaceResId:x} ({res.Resolve(sceneKey, v.SurfaceResId)?.Name ?? "?"}) src=({v.SrcX},{v.SrcY} {v.W}x{v.H}) dst=({v.DstX},{v.DstY})");
```
- [ ] **Step 2:** `dotnet run --project engine/Age.Cli -- gfx --boot SC0000.BIN` — sanity: the CGs appear as visible objects with real surfaces + plausible positions, in ascending-handle order. **Step 3:** commit.

---

### Task 1.4 — Godot: per-frame clear + composite from visible objects

- [ ] **Step 1:** `GodotAdvHost.cs`: make `DrawTexture` a no-op (retained compositor renders); keep `SetTexture` populating dims. Add `public string? ResolveResIdTexture(long resId)` (`_res.Resolve(_scene, resId) → TexturePath`).
- [ ] **Step 2:** `Main.cs`: add `Recomposite()` called from `_Process` (guard `!_selftest && _vm != null`); replace `BlitSlot` with a cached `BlitLayer`. Recomposite:
```csharp
    _screen.Fill(new Color(0, 0, 0, 0));
    foreach (var v in _vm.Gfx.SnapshotVisibleObjects())   // already ascending-handle = z-order
    {
        if (v.SurfaceResId == 0) continue;
        var bmp = _host.ResolveResIdTexture(v.SurfaceResId);
        if (bmp != null) BlitLayer(bmp, v.SrcX, v.SrcY, v.W, v.H, v.DstX, v.DstY);
    }
    _screenTex.Update(_screen);
```
`BlitLayer` = the old `BlitSlot` body, caching `Image`s by path (`Dictionary<string, Image?>`), one `_screenTex.Update` after the loop. (Removed: the immediate `CallDeferred("BlitSlot")` in `DrawTexture`.)
- [ ] **Step 3:** build Godot (`--import` → `dotnet build godot/Himegari.csproj`); **Step 4:** `--selftest` green.
- [ ] **Step 5: live, user-driven:** launch `godot -- --boot` in the background; ask the user to confirm the CGs render correctly across pages (no alternating grey, no vanished content). Expected caveats (Phases 2/3): glow opaque, no fade, possible green boxes on sprites, occasional off-position CG (cold-anchor residual). **Step 6:** commit.

## Self-review

- Model-first: every field comes from the RE (`obj+4` slot, `obj+8..0x14` rect, `V24` position, visible bit 0, ascending-handle z-order). No flat-layer/snapshot logic. ✓
- Parity + threading + seam constraints restated per task. ✓
- Guardrail (booted CGs render) is the Phase-1 live check. ✓
- Open items (scale math, colorkey bits, visible-flag clear) are explicitly deferred to later phases / to be RE'd, not guessed. ✓

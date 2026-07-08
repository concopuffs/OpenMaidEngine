# Blend & Transparency (Slice A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Godot compositor honor per-surface **colorkey** transparency and per-object **alpha/tint**, so keyed sprites stop rendering as opaque boxes and the SC0000 opening's fade layer stops covering the scene as an opaque grey.

**Architecture:** Hybrid (approach 3). The engine resolves each visible object into a fully-specified `RenderObject` blend plan (`Alpha`, `Tint`, `Blend`, existing `ColorKey`); the Godot host executes a colorkey/alpha/tint blit. A small pure `BlendMath` helper in the engine holds the colorkey/unpack math so it is unit-tested; the per-pixel loop stays in the host where Godot's `Image` lives.

**Tech Stack:** C# / .NET 8 (`engine/AgeEngine.sln`), xUnit; Godot 4.7 mono (`godot/Himegari.csproj`).

**Spec:** `docs/superpowers/specs/2026-07-08-blend-transparency-design.md`. Native RE (colorkey format, `0x202`/`0x203` color workers, blit blend modes) is done and recorded in `docs/engine-re.md` §"Blend & transparency" — read it; the Ghidra image is annotated (`gfx_op_0x1f9_load_surface`, `gfx_op_0x202_worker_set_color_anim`, `gfx_op_0x203_worker_set_color`, `gfx_object_blit_d3d9`).

## Global Constraints

- **Parity is sacred.** Non-Godot output stays byte-identical. Gates that MUST stay green: `dotnet test engine/AgeEngine.sln`; `Age.Cli sweep` → `exit=284, STEP-LIMIT=13`; Godot `--selftest` → `SELFTEST OK`. This slice adds model fields + host rendering only; the headless dialogue path is untouched.
- **Seam rule:** `Age.Engine/Vm` may reference only `Model`, `Hosting`, `Diagnostics` — never `Sys4`. `BlendMath`/`BlendKind` live in `Age.Engine/Model`.
- **Colorkey semantics (from RE, verbatim):** op arg 3 `< 0` → **no colorkey**; else the operand is **`0xRRGGBB`** and texels whose `(R,G,B)` equal it become transparent (operand `0` = key **black**).
- **Packed color (existing `GfxState.PackColor(alpha,color)`):** `0xAARRGGBB` = `(alpha<<24) | (color & 0xFFFFFF)`.
- **In scope:** colorkey transparency, per-object static alpha + RGB tint (modulate), surfaceless color-fill quads. **Deferred (documented, not built):** smooth color-animation interpolation over the `0x238` clock, and additive/glow blend (`local_2c` mode 2/3). Leave the `BlendKind.Additive` enum member as an unused seam.
- **Screen size:** the primary surface is `800x600` (see `GodotAdvHost._slotDims[0]`).
- **Build/run:** `dotnet build engine/AgeEngine.sln -c Debug`; `dotnet test engine/AgeEngine.sln`. Godot: `dotnet build godot/Himegari.csproj -c Debug` then `S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe`.
- TDD, one deliverable per task, commit at the end of each task. End every commit message with:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

---

## Task 0: Branch

- [ ] **Step 1: Create the feature branch** (repo root = `age-reimpl/`, default branch `main`)

```bash
cd "S:/Game Hacking/Eushully/Himegari/age-reimpl"
git checkout -b feat/blend-transparency
```

---

## File Structure

- `engine/Age.Engine/Model/BlendMath.cs` — **new**. Pure colorkey/ARGB math. Task 1.
- `engine/Age.Engine/Model/BlendKind.cs` — **new**. `enum BlendKind { Opaque, Alpha, Additive }`. Task 2.
- `engine/Age.Engine/Model/GfxState.cs` — **modify**. `RenderObject` blend fields; `GfxObject.HasColor`; resolution in `SnapshotVisibleObjects`. Task 2.
- `engine/Age.Engine/Vm/VirtualMachine.cs` — **modify**. `0x202`/`0x203` set `HasColor`. Task 2.
- `engine/Age.Engine.Tests/BlendMathTests.cs` — **new**. Task 1.
- `engine/Age.Engine.Tests/RenderObjectBlendTests.cs` — **new**. Task 2.
- `godot/Main.cs` — **modify**. Colorkey-baked image cache; `BlitLayer` alpha/tint; surfaceless fill. Tasks 3–4.

---

## Task 1: `BlendMath` — pure colorkey + ARGB helpers

**Files:**
- Create: `engine/Age.Engine/Model/BlendMath.cs`
- Test: `engine/Age.Engine.Tests/BlendMathTests.cs`

**Interfaces:**
- Produces:
  - `static bool Age.Engine.Model.BlendMath.HasColorKey(long colorKey)` — `colorKey >= 0`.
  - `static bool BlendMath.ColorKeyMatches(byte r, byte g, byte b, long colorKey)` — false when `!HasColorKey`; else true when `(r,g,b)` equals the key's `0xRRGGBB` bytes.
  - `static (int A, int R, int G, int B) BlendMath.UnpackArgb(long packed)` — from `0xAARRGGBB`.

- [ ] **Step 1: Write the failing test**

Create `engine/Age.Engine.Tests/BlendMathTests.cs`:

```csharp
using Age.Engine.Model;
using Xunit;

public class BlendMathTests
{
    [Fact]
    public void NegativeColorKey_IsNoKey()
    {
        Assert.False(BlendMath.HasColorKey(-1));
        Assert.False(BlendMath.ColorKeyMatches(0, 0, 0, -1));   // never matches when no key
    }

    [Fact]
    public void ZeroColorKey_KeysBlack()
    {
        Assert.True(BlendMath.HasColorKey(0));
        Assert.True(BlendMath.ColorKeyMatches(0, 0, 0, 0));      // (0,0,0) == key black
        Assert.False(BlendMath.ColorKeyMatches(1, 0, 0, 0));    // near-black is NOT keyed (exact match)
    }

    [Fact]
    public void ColorKey_MatchesRgb888Bytes()
    {
        long key = 0xFF00FF;                                    // magenta 0xRRGGBB
        Assert.True(BlendMath.ColorKeyMatches(0xFF, 0x00, 0xFF, key));
        Assert.False(BlendMath.ColorKeyMatches(0xFE, 0x00, 0xFF, key));
    }

    [Fact]
    public void UnpackArgb_SplitsBytes()
    {
        Assert.Equal((0x80, 0x12, 0x34, 0x56), BlendMath.UnpackArgb(0x80123456));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter BlendMathTests`
Expected: FAIL — `BlendMath` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `engine/Age.Engine/Model/BlendMath.cs`:

```csharp
namespace Age.Engine.Model;

/// <summary>Pure blend helpers shared by the engine resolution and the host blit. Colorkey semantics come
/// from the native loader (docs/engine-re.md §"Blend & transparency"): op arg 3 &lt; 0 = no key; otherwise
/// the operand is 0xRRGGBB and matching (R,G,B) texels are transparent (operand 0 = key black).</summary>
public static class BlendMath
{
    /// <summary>A colorkey operand &gt;= 0 is an active key; a negative operand means "no colorkey".</summary>
    public static bool HasColorKey(long colorKey) => colorKey >= 0;

    /// <summary>True when (r,g,b) exactly equals the key's 0xRRGGBB bytes. Always false when there is no key.</summary>
    public static bool ColorKeyMatches(byte r, byte g, byte b, long colorKey)
    {
        if (!HasColorKey(colorKey)) return false;
        return r == ((colorKey >> 16) & 0xff) && g == ((colorKey >> 8) & 0xff) && b == (colorKey & 0xff);
    }

    /// <summary>Split a 0xAARRGGBB packed color (see GfxState.PackColor) into its byte channels.</summary>
    public static (int A, int R, int G, int B) UnpackArgb(long packed)
        => ((int)((packed >> 24) & 0xff), (int)((packed >> 16) & 0xff),
            (int)((packed >> 8) & 0xff), (int)(packed & 0xff));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test engine/AgeEngine.sln --filter BlendMathTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add engine/Age.Engine/Model/BlendMath.cs engine/Age.Engine.Tests/BlendMathTests.cs
git commit -m "feat: add BlendMath (colorkey match + ARGB unpack)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `RenderObject` blend fields + resolution

**Files:**
- Create: `engine/Age.Engine/Model/BlendKind.cs`
- Modify: `engine/Age.Engine/Model/GfxState.cs` (`RenderObject` record ~lines 17-19; `GfxObject` ~lines 29-49; `SnapshotVisibleObjects` ~lines 181-199)
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs` (`gfx-blit-color` / `gfx-draw-color` handlers ~lines 273-277)
- Test: `engine/Age.Engine.Tests/RenderObjectBlendTests.cs`

**Interfaces:**
- Consumes: `BlendMath.UnpackArgb` (Task 1).
- Produces:
  - `enum Age.Engine.Model.BlendKind { Opaque, Alpha, Additive }`.
  - `RenderObject` gains: `int Alpha` (0-255), `long Tint` (0xRRGGBB), `BlendKind Blend`. (Existing `long ColorKey` is retained.)
  - `GfxState.GfxObject.HasColor` (bool) — set true by ops `0x202`/`0x203`.
  - Resolution: an object with `HasColor` resolves to `Alpha`/`Tint` from its packed `Color` and `Blend = Alpha`; otherwise `Alpha = 255`, `Tint = 0xFFFFFF`, `Blend = Opaque`.

- [ ] **Step 1: Write the failing test**

Create `engine/Age.Engine.Tests/RenderObjectBlendTests.cs`:

```csharp
using System.Linq;
using Age.Engine.Model;
using Xunit;

public class RenderObjectBlendTests
{
    // Make a visible textured object bound to a slot that has a surface, so it appears in the snapshot.
    private static GfxState WithVisibleObject(long handle, long resId, long colorKey)
    {
        var g = new GfxState();
        g.SetSurface(1, resId, colorKey);
        g.BindDraw(handle, 1, 0, 0, 10, 10, 0, 0);   // draw-texture: slot 1, 10x10 at (0,0), visible
        return g;
    }

    [Fact]
    public void ObjectWithoutColor_ResolvesOpaqueWhiteTint()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, ro.Alpha);
        Assert.Equal(0xFFFFFF, ro.Tint);
        Assert.Equal(BlendKind.Opaque, ro.Blend);
    }

    [Fact]
    public void DrawColor_0x203_SetsAlphaTintAndBlend()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        // emulate op 0x203: pack (alpha=0x80, color=0x102030) and mark HasColor
        g.SetObjectColor(0x100, GfxState.PackColor(0x80, 0x102030));
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(0x80, ro.Alpha);
        Assert.Equal(0x102030, ro.Tint);
        Assert.Equal(BlendKind.Alpha, ro.Blend);
    }

    [Fact]
    public void ColorKey_IsCarriedThrough()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: 0x000000);   // key black
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(0x000000, ro.ColorKey);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter RenderObjectBlendTests`
Expected: FAIL — `BlendKind`, `RenderObject.Alpha/Tint/Blend`, and `GfxState.SetObjectColor` do not exist (compile errors).

- [ ] **Step 3: Add the `BlendKind` enum**

Create `engine/Age.Engine/Model/BlendKind.cs`:

```csharp
namespace Age.Engine.Model;

/// <summary>How an object's surface composites onto the canvas. Opaque = straight copy; Alpha = source-alpha
/// blend (fades). Additive (glow/flash, native blit mode 2/3) is a documented seam — NOT implemented in the
/// blend/transparency slice; see docs/superpowers/specs/2026-07-08-blend-transparency-design.md.</summary>
public enum BlendKind { Opaque, Alpha, Additive }
```

- [ ] **Step 4: Add `Alpha`/`Tint`/`Blend` to `RenderObject`**

In `engine/Age.Engine/Model/GfxState.cs`, change the `RenderObject` record (currently):

```csharp
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY,
                                           AnimState Anim);
```

to (append the three blend fields after `Anim`):

```csharp
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY,
                                           AnimState Anim, int Alpha, long Tint, BlendKind Blend);
```

- [ ] **Step 5: Add `HasColor` + `SetObjectColor`, and set `HasColor` in `BindDraw`-adjacent color path**

In `GfxState.GfxObject` (near the `Color` field ~line 33), add:

```csharp
        public bool HasColor;   // true once op 0x202/0x203 set a color/alpha modulation on this object
```

Add a method on `GfxState` (near `SetSurface`, so tests and the VM share one entry point):

```csharp
    /// <summary>Ops 0x202/0x203: record a packed 0xAARRGGBB color/alpha modulation on the object and mark it
    /// HasColor so the compositor applies alpha+tint (vs the opaque default).</summary>
    public void SetObjectColor(long handle, long packed)
    {
        lock (_lock) { var o = GetOrCreate(handle); o.Color = packed; o.HasColor = true; }
    }
```

- [ ] **Step 6: Resolve the blend fields in `SnapshotVisibleObjects`**

In `SnapshotVisibleObjects` (~lines 186-196), replace the loop body's `list.Add(...)` with a version that
computes the blend plan first:

```csharp
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

                list.Add(new RenderObject(kv.Key, resId, ck, o.SrcRect.X, o.SrcRect.Y, o.SrcRect.W, o.SrcRect.H,
                                          (int)o.V24.X, (int)o.V24.Y,
                                          new AnimState(o.AnimEnabled, o.AnimNormalized,
                                                        o.AnimTarget.X, o.AnimTarget.Y, o.AnimTarget.Z,
                                                        o.AnimDurationTicks, o.AnimGeneration),
                                          alpha, tint, blend));
            }
```

- [ ] **Step 7: Route the VM color ops through `SetObjectColor`**

In `engine/Age.Engine/Vm/VirtualMachine.cs`, the `gfx-blit-color` / `gfx-draw-color` handlers currently read:

```csharp
            case "gfx-blit-color":      // 0x202 (handle)(x)(y)(alpha)(color) — blend deferred
                Gfx.GetOrCreate(Read(a[0])).Color = GfxState.PackColor(Read(a[3]), Read(a[4]));
```
```csharp
            case "gfx-draw-color":      // 0x203 (handle)(v)(alpha)(color) — blend deferred
                Gfx.GetOrCreate(Read(a[0])).Color = GfxState.PackColor(Read(a[2]), Read(a[3]));
```

Change each assignment to `SetObjectColor` (so `HasColor` is set). Keep the surrounding `return pc + 1;` and any trace/stub lines unchanged:

```csharp
            case "gfx-blit-color":      // 0x202 (handle)(x)(y)(alpha)(color) — static alpha/tint (anim interp deferred)
                Gfx.SetObjectColor(Read(a[0]), GfxState.PackColor(Read(a[3]), Read(a[4])));
```
```csharp
            case "gfx-draw-color":      // 0x203 (handle)(v)(alpha)(color) — static alpha/tint
                Gfx.SetObjectColor(Read(a[0]), GfxState.PackColor(Read(a[2]), Read(a[3])));
```

> NOTE: keep the exact operand indices shown above (`a[3]/a[4]` for `0x202`, `a[2]/a[3]` for `0x203`) — they match the existing code.

- [ ] **Step 8: Run the new test — verify it passes**

Run: `dotnet test engine/AgeEngine.sln --filter RenderObjectBlendTests`
Expected: PASS (3 tests).

- [ ] **Step 9: Run the FULL suite — verify parity**

Run: `dotnet test engine/AgeEngine.sln`
Expected: PASS — all pre-existing tests still green (existing `RenderObject` consumers get the new fields; the VM change only adds `HasColor`, which no headless host reads).

- [ ] **Step 10: Verify the corpus oracle is unchanged**

Run: `dotnet run --project engine/Age.Cli -c Debug -- sweep`
Expected: `exit=284, STEP-LIMIT=13` (unchanged).

- [ ] **Step 11: Commit**

```bash
git add engine/Age.Engine/Model/BlendKind.cs engine/Age.Engine/Model/GfxState.cs engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/RenderObjectBlendTests.cs
git commit -m "feat: resolve per-object alpha/tint/blend into RenderObject (colorkey retained)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Host — colorkey bake + alpha/tint blit

**Files:**
- Modify: `godot/Main.cs` (`_imgCache` ~line 234; `Recomposite` ~lines 251-266; `BlitLayer` ~lines 290-328)

**Interfaces:**
- Consumes: `RenderObject.{ColorKey, Alpha, Tint, Blend}` (Task 2); `BlendMath.ColorKeyMatches`, `BlendMath.HasColorKey` (Task 1).
- Produces: a compositor that (a) bakes colorkey → transparent on load, keyed by `(path, colorKey)`, and (b) blits each object with its `Alpha` and `Tint`.

This task changes only the Godot project (built separately; verified by `--selftest` + `--shot`, not xUnit).

- [ ] **Step 1: Key the image cache by colorkey and bake transparency**

In `godot/Main.cs`, change the image cache key from `string` to a `(string, long)` tuple. Replace the field
(~line 234):

```csharp
    private readonly System.Collections.Generic.Dictionary<string, Image?> _imgCache = new();
```

with:

```csharp
    private readonly System.Collections.Generic.Dictionary<(string Path, long Key), Image?> _imgCache = new();
```

- [ ] **Step 2: Thread ColorKey/Alpha/Tint through `Recomposite` into `BlitLayer`**

In `Recomposite` (~line 263), the current call is:

```csharp
            BlitLayer(bmp, v.SrcX, v.SrcY, v.W, v.H, v.DstX, v.DstY, AlphaFor(v, clockReset, clockDur));
```

Change it to pass the blend plan:

```csharp
            float a = AlphaFor(v, clockReset, clockDur) * (v.Alpha / 255f);
            BlitLayer(bmp, v.ColorKey, v.Tint, v.SrcX, v.SrcY, v.W, v.H, v.DstX, v.DstY, a);
```

- [ ] **Step 3: Rewrite `BlitLayer` to bake colorkey + apply tint/alpha**

Replace the whole `BlitLayer` method (~lines 290-328) with:

```csharp
    // Blit one object's surface rect. The source Image is cached per (path, colorKey): on first load, texels
    // matching the surface colorkey are made transparent (native bakes the key at load — engine-re.md §Blend).
    // tint (0xRRGGBB) modulates the texel RGB (fade-to-black uses tint=black); alpha is the object's opacity.
    private void BlitLayer(string bmpPath, long colorKey, long tint, int srcX, int srcY, int w, int h,
                           int dstX, int dstY, float alpha = 1f)
    {
        var cacheKey = (bmpPath, colorKey);
        if (!_imgCache.TryGetValue(cacheKey, out var src))
        {
            src = new Image();
            if (src.LoadBmpFromBuffer(System.IO.File.ReadAllBytes(bmpPath)) != Error.Ok)
            { GD.Print($"BMP load failed {bmpPath}"); src = null; }
            else
            {
                if (src.GetFormat() != Image.Format.Rgba8) src.Convert(Image.Format.Rgba8);
                if (Age.Engine.Model.BlendMath.HasColorKey(colorKey)) BakeColorKey(src, colorKey);
            }
            _imgCache[cacheKey] = src;
        }
        if (src == null) return;

        int sw = w > 0 ? w : src.GetWidth();
        int sh = h > 0 ? h : src.GetHeight();
        sw = System.Math.Min(sw, src.GetWidth() - srcX);
        sh = System.Math.Min(sh, src.GetHeight() - srcY);
        if (sw <= 0 || sh <= 0) return;

        bool plainOpaque = alpha >= 0.999f && tint == 0xFFFFFF && !Age.Engine.Model.BlendMath.HasColorKey(colorKey);
        if (plainOpaque)   // fast path: unchanged behaviour for opaque, un-keyed, un-tinted layers
        {
            _screen.BlitRect(src, new Rect2I(srcX, srcY, sw, sh), new Vector2I(dstX, dstY));
            return;
        }

        int tr = (int)((tint >> 16) & 0xff), tg = (int)((tint >> 8) & 0xff), tb = (int)(tint & 0xff);
        byte[] dst = _screen.GetData(); byte[] ss = src.GetData();
        int dw = _screen.GetWidth(), dh = _screen.GetHeight(), sfw = src.GetWidth();
        int ia = (int)(System.Math.Clamp(alpha, 0f, 1f) * 255);
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int dxp = dstX + x, dyp = dstY + y;
                if (dxp < 0 || dyp < 0 || dxp >= dw || dyp >= dh) continue;
                int di = (dyp * dw + dxp) * 4;
                int si = ((srcY + y) * sfw + (srcX + x)) * 4;
                int sa = ss[si + 3] * ia / 255;                 // texel alpha (colorkey already 0) × object alpha
                if (sa == 0) continue;
                int sr = ss[si] * tr / 255, sg = ss[si + 1] * tg / 255, sb = ss[si + 2] * tb / 255;   // tint modulate
                dst[di]     = (byte)((sr * sa + dst[di]     * (255 - sa)) / 255);
                dst[di + 1] = (byte)((sg * sa + dst[di + 1] * (255 - sa)) / 255);
                dst[di + 2] = (byte)((sb * sa + dst[di + 2] * (255 - sa)) / 255);
                dst[di + 3] = (byte)System.Math.Min(255, dst[di + 3] + sa);
            }
        _screen.SetData(dw, dh, false, _screen.GetFormat(), dst);
    }

    // Make colorkey-matching texels transparent (native colorkey is baked at surface load).
    private static void BakeColorKey(Image img, long colorKey)
    {
        byte[] px = img.GetData();
        int w = img.GetWidth(), h = img.GetHeight();
        for (int i = 0; i < px.Length; i += 4)
            if (Age.Engine.Model.BlendMath.ColorKeyMatches(px[i], px[i + 1], px[i + 2], colorKey))
                px[i + 3] = 0;
        img.SetData(w, h, false, img.GetFormat(), px);
    }
```

- [ ] **Step 4: Build the Godot project**

Run: `dotnet build godot/Himegari.csproj -c Debug --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Verify parity via the headless self-test**

Run: `& "S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --headless --path godot -- --selftest`
Expected: ends with `SELFTEST OK`.

- [ ] **Step 6: Visual check — keyed sprites + alpha**

Run: `& "S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --path godot -- --boot --shot "..\scratch\blend_p1.png" --shot-page 1`
Then Read the PNG. Expected: the event-CG composites without opaque boxes around keyed layers (compare against the pre-change opening). Full validation of the fade is Task 4.

- [ ] **Step 7: Commit**

```bash
git add godot/Main.cs
git commit -m "feat(godot): colorkey bake + per-object alpha/tint blit

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Host — surfaceless color-fill (fades) + acceptance

**Files:**
- Modify: `godot/Main.cs` (`Recomposite` ~lines 258-264)

**Interfaces:**
- Consumes: `RenderObject.{SurfaceResId, Alpha, Tint, W, H, DstX, DstY, Blend}` (Task 2).
- Produces: `Recomposite` fills a `Tint`×`Alpha` quad for a **colored, surfaceless** object (a fade/flash with no bound image) instead of skipping it.

- [ ] **Step 1: Fill surfaceless colored objects instead of skipping them**

In `Recomposite` (~lines 258-264), the loop currently skips any object with no surface:

```csharp
        foreach (var v in _vm.Gfx.SnapshotVisibleObjects())   // already ascending-handle = z-order
        {
            if (v.SurfaceResId == 0) continue;                 // render-target/blank surface (no file) — later phase
            var bmp = _host.ResolveResIdTexture(v.SurfaceResId);
            if (bmp == null) continue;
            float a = AlphaFor(v, clockReset, clockDur) * (v.Alpha / 255f);
            BlitLayer(bmp, v.ColorKey, v.Tint, v.SrcX, v.SrcY, v.W, v.H, v.DstX, v.DstY, a);
        }
```

Replace it with a version that fills colored surfaceless objects:

```csharp
        foreach (var v in _vm.Gfx.SnapshotVisibleObjects())   // already ascending-handle = z-order
        {
            float a = AlphaFor(v, clockReset, clockDur) * (v.Alpha / 255f);
            if (v.SurfaceResId == 0)
            {
                // A colored object with no bound surface = a fade/flash fill (e.g. fade-to-black). Fill its
                // rect (full-screen when it has no size, the opening's case) with the tint at alpha. Uncolored
                // surfaceless objects are render targets — still skipped (slice C).
                if (v.Blend != Age.Engine.Model.BlendKind.Opaque)
                {
                    int fw = v.W > 0 ? v.W : 800, fh = v.H > 0 ? v.H : 600;
                    FillQuad(v.DstX, v.DstY, fw, fh, v.Tint, a);
                }
                continue;
            }
            var bmp = _host.ResolveResIdTexture(v.SurfaceResId);
            if (bmp == null) continue;
            BlitLayer(bmp, v.ColorKey, v.Tint, v.SrcX, v.SrcY, v.W, v.H, v.DstX, v.DstY, a);
        }
```

- [ ] **Step 2: Add the `FillQuad` helper**

Add next to `BlitLayer` in `godot/Main.cs`:

```csharp
    // Alpha-blend a solid tint (0xRRGGBB) rectangle over the screen — the surfaceless fade/flash fill.
    private void FillQuad(int dstX, int dstY, int w, int h, long tint, float alpha)
    {
        int ia = (int)(System.Math.Clamp(alpha, 0f, 1f) * 255);
        if (ia == 0) return;
        int tr = (int)((tint >> 16) & 0xff), tg = (int)((tint >> 8) & 0xff), tb = (int)(tint & 0xff);
        byte[] dst = _screen.GetData();
        int dw = _screen.GetWidth(), dh = _screen.GetHeight();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int dxp = dstX + x, dyp = dstY + y;
                if (dxp < 0 || dyp < 0 || dxp >= dw || dyp >= dh) continue;
                int di = (dyp * dw + dxp) * 4;
                dst[di]     = (byte)((tr * ia + dst[di]     * (255 - ia)) / 255);
                dst[di + 1] = (byte)((tg * ia + dst[di + 1] * (255 - ia)) / 255);
                dst[di + 2] = (byte)((tb * ia + dst[di + 2] * (255 - ia)) / 255);
                dst[di + 3] = (byte)System.Math.Min(255, dst[di + 3] + ia);
            }
        _screen.SetData(dw, dh, false, _screen.GetFormat(), dst);
    }
```

- [ ] **Step 3: Build the Godot project**

Run: `dotnet build godot/Himegari.csproj -c Debug --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Verify parity via the headless self-test**

Run: `& "S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --headless --path godot -- --selftest`
Expected: ends with `SELFTEST OK`.

- [ ] **Step 5: Acceptance — the opening fade no longer covers the scene**

Run a windowed shot-sequence with boot state:
`& "S:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --path godot -- --boot --shot-sequence "..\scratch\blend_seq" --frames 120`
Then Read several frames (e.g. `frame_0015.png`, `frame_0045.png`, `frame_0060.png`). Expected: the event-CG stays visible and the fade renders as a translucent/settling layer instead of a full-screen opaque grey that eats the scene. This is the eyeball acceptance the user validates; if a fade still looks wrong, note it as the deferred **smooth color-anim interpolation** follow-up (do not expand this slice).

- [ ] **Step 6: Commit**

```bash
git add godot/Main.cs
git commit -m "feat(godot): surfaceless color-fill quads for AE* fades (opening no longer opaque-grey)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Docs + memory

**Files:**
- Modify: `docs/phase-a-slice-plan.md` (append a slice-A-done section)
- Modify: `vm-map/opcodes.toml` (`0x202`/`0x203` summaries: now resolved to per-object color/alpha, not "blend deferred"), then rebuild
- Modify: memory `himegari-port-status.md` + `MEMORY.md`

- [ ] **Step 1: Refresh the opcode notes for `0x202`/`0x203`**

Edit `vm-map/opcodes.toml` — in the `0x202` and `0x203` entries, update the `summary`/`notes` to record that the VM now sets a per-object color/alpha modulation consumed by the compositor (static alpha/tint), citing `docs/engine-re.md` §"Blend & transparency". Do NOT hand-edit generated files.

- [ ] **Step 2: Rebuild the opcode artifacts**

Run: `py -3.11 -X utf8 tools/opcodes_build.py --build`
Expected: regenerates `tools/age_opcodes_himegari.py`, `build/opcodes.json`, `docs/opcode-reference.md`, `build/opcode-coverage.md` with no errors.

- [ ] **Step 3: Record the slice in `docs/phase-a-slice-plan.md`**

Append a short "### A2b — Blend & transparency (slice A) DONE" section: what landed (colorkey bake, per-object alpha/tint, surfaceless fade fill), the acceptance result, and the two explicit deferrals (smooth color-anim interpolation; additive/glow). Cross-link the spec.

- [ ] **Step 4: Update the status memory**

Update `himegari-port-status.md` (and its one-line entry in `MEMORY.md`) with the slice-A result and the deferrals. Convert relative dates to absolute (2026-07-08).

- [ ] **Step 5: Commit**

```bash
git add vm-map/opcodes.toml tools/age_opcodes_himegari.py build/opcodes.json docs/opcode-reference.md build/opcode-coverage.md docs/phase-a-slice-plan.md
git commit -m "docs: record blend/transparency slice A (colorkey + alpha/tint + fade fill)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
(The memory files live outside the repo; they do not need to be committed.)

---

## Notes for the executor

- **Do NOT** build smooth color-animation interpolation or additive/glow blend — both are deferred by design (see Global Constraints). If the opening fade snaps instead of ramping, that is the expected limit of this slice, not a bug.
- **Colorkey is baked once per `(path, colorKey)`** — if the same BMP is used both keyed and un-keyed, the cache correctly holds two entries.
- **`FillQuad` full-screen default (800×600)** is correct for the opening's full-screen fades; per-object fill geometry is refined in slice B (geometry/anchors).

## Self-review (done while writing)

- **Spec coverage:** Unit 1 (RenderObject blend fields + resolution) → Task 2; Unit 2 (colorkey bake + alpha/tint blit) → Task 3; Unit 3 (alpha unification) → Task 3 Step 2 (`AlphaFor(...) * v.Alpha/255`); `BlendMath` helper → Task 1; surfaceless fill → Task 4; RE Task-0 → done pre-plan (recorded in engine-re.md), so the plan starts at branch. Deferrals (smooth interp, additive) are stated in the spec's Non-goals and this plan's Global Constraints. All covered.
- **Placeholder scan:** every code step shows complete code; the only prose-only steps are Task 5 doc edits (inherently prose) with exact files + rebuild command.
- **Type consistency:** `BlendKind{Opaque,Alpha,Additive}`, `RenderObject.{Alpha:int, Tint:long, Blend:BlendKind}`, `GfxState.SetObjectColor(long,long)`, `GfxObject.HasColor:bool`, `BlendMath.{HasColorKey(long):bool, ColorKeyMatches(byte,byte,byte,long):bool, UnpackArgb(long):(int,int,int,int)}`, `BlitLayer(string,long,long,int,int,int,int,int,int,float)`, `FillQuad(int,int,int,int,long,float)` — used identically across tasks.

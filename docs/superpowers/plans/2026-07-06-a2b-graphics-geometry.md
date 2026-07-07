# A2b Graphics Geometry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement opcode `0x208` (`get-texture-size`) so the bytecode's own geometry math produces correct sprite/background positions, and replace the Godot host's TextureRect-per-slot approximation with a faithful 800×600 immediate-mode blit compositor.

**Architecture:** The CG-load subroutine (`SC0000.asm` `label_12649`) computes all geometry in bytecode from anchor globals + the texture's width/height; the only missing native primitive is `0x208 = get-texture-size(slot) → (out_w, out_h)`. We add `IHost.GetTextureSize`, make `0x208` a real VM op that writes the two output globals, and give the Godot host a screen-backbuffer blit compositor whose `draw-texture` blits a source slot's image (src rect → dst) into one shared canvas in execution order. Texture dims are read from the pre-converted BMP header on the VM thread (synchronous); pixel blits happen deferred on the Godot main thread.

**Tech Stack:** C# / .NET 8 (`engine/`, solution `AgeEngine.sln`), Godot 4.7 .NET (`godot/Himegari.csproj`), Python 3.11 for the opcode build (`tools/opcodes_build.py`), xUnit for engine tests.

## Global Constraints

- **Run Python as** `py -3.11 -X utf8 tools/<name>.py …` (utf8 mode mandatory on Windows).
- **Never hand-edit generated files.** `tools/age_opcodes_himegari.py`, `build/opcodes.json`, `docs/opcode-reference.md`, `build/opcode-coverage.md` are generated from `vm-map/opcodes.toml` via `py -3.11 -X utf8 tools/opcodes_build.py --build`.
- **Trace/selftest parity is a guardrail.** After any VM change, the C# trace must stay byte-identical to `build/vm0-trace.json` (offsets + halt + **step count**). Non-Godot hosts (`CaptureHost`, test/CLI `RecHost`s) must no-op the new op (return `(0,0)`).
- **Seam rule:** `Age.Engine/Vm` references only `Model` + `Hosting` (never `Sys4`). `IHost` lives in `Age.Engine/Hosting`.
- **Opcode `category` vocabulary** is fixed: `marker/structural/control/adv/draw/audio/input/compute/unknown`. Use `draw` for `0x208`.
- **Valid opcode `confidence`:** use `med` for `0x208` (inference from disassembly, not Frida-confirmed).
- Build engine + tests: `dotnet test engine/AgeEngine.sln`. Build Godot: `godot --headless --path godot --import` then `dotnet build godot/Himegari.csproj`. Godot selftest: `godot --headless --path godot -- --selftest`.

---

## File structure

| File | Responsibility | Task |
|---|---|---|
| `vm-map/opcodes.toml` | Rename `0x208` → `get-texture-size` (source edit) | 1 |
| `engine/Age.Engine/Hosting/IHost.cs` | Add `GetTextureSize` to the host contract | 1 |
| `engine/Age.Engine/Hosting/CaptureHost.cs` | No-op impl → `(0,0)` (parity) | 1 |
| `engine/Age.Engine/Vm/VirtualMachine.cs` | `case "get-texture-size"` handler (writes 2 globals) | 1 |
| `engine/Age.Cli/Program.cs` | Add impl to `AudioTraceHost`; add `gfx` command + `GfxTraceHost` | 1, 2 |
| `engine/Age.Engine.Tests/TextureOpsTests.cs` | Add `GetTextureSize` to test `RecHost` | 1 |
| `engine/Age.Engine.Tests/TextureGeometryTests.cs` | New: `0x208` writes host dims into output globals | 1 |
| `engine/Age.Engine/Sys4/BmpHeader.cs` | New: `ReadDims(path) → (w,h)` from BMP header | 2 |
| `engine/Age.Engine.Tests/BmpHeaderTests.cs` | New: `ReadDims` on a known BMP | 2 |
| `godot/GodotAdvHost.cs` | Per-slot BMP path + dims; real `GetTextureSize`; `DrawTexture` → blit | 1 (stub), 3 |
| `godot/Main.cs` | Screen `Image` backbuffer + one `TextureRect`; `BlitSlot` | 3 |

---

### Task 1: `0x208 get-texture-size` — real VM op + host contract

Make `0x208` a VM op that writes the loaded texture's width/height into its two output globals, backed by `IHost.GetTextureSize`. All non-Godot hosts return `(0,0)` so trace/selftest parity holds. The Godot host gets a temporary `(0,0)` stub here (real impl in Task 3) so `Himegari.csproj` keeps compiling.

**Files:**
- Modify: `vm-map/opcodes.toml` (op `0x208` block, ~line 4980)
- Modify: `engine/Age.Engine/Hosting/IHost.cs:10` (add method)
- Modify: `engine/Age.Engine/Hosting/CaptureHost.cs`
- Modify: `engine/Age.Engine/Vm/VirtualMachine.cs` (add case near the other texture ops, ~line 175)
- Modify: `engine/Age.Cli/Program.cs` (`AudioTraceHost`)
- Modify: `engine/Age.Engine.Tests/TextureOpsTests.cs` (`RecHost`)
- Modify: `godot/GodotAdvHost.cs` (temporary stub)
- Test: `engine/Age.Engine.Tests/TextureGeometryTests.cs` (new)

**Interfaces:**
- Produces: `IHost.GetTextureSize(int slot) → (int Width, int Height)`. VM handler for label `"get-texture-size"`: reads operand 0 as the slot, writes `Width` to operand 1 and `Height` to operand 2 (both via `Write`).
- Consumes: existing `Read`/`Write` operand helpers; `OpcodeTable.Label(op)` dispatch (dispatches on the `label` field, confirmed: `set-texture`/`draw-texture` carry friendly labels in `opcodes.toml`).

- [ ] **Step 1: Rename the opcode in the source-of-truth and rebuild**

Edit `vm-map/opcodes.toml`, the `op = 0x208` block. Change the `label` line and the `[opcode.semantics]` fields (leave `argc = 3` and the three `[[opcode.semantics.args]]` blocks; optionally set their `role`s):

```toml
[[opcode]]
op = 0x208
label = "get-texture-size"
argc = 3
abi_source = "kelebek+decode-validated"

[opcode.semantics]
name = "get-texture-size"
category = "draw"
summary = "0x208 (slot)(out_w)(out_h) — writes the loaded texture's width/height into two output globals; keystone for bytecode-computed sprite/bg geometry (SC0000 label_12649)"
noop_headless = false
source = "inference"
confidence = "med"
depends_on = []
evidence = "SC0000 label_12649: set-texture(resId,slot) then 0x208(slot)->w,h feeds w/2 horizontal-center + foot-anchor subtraction into draw-texture dst; stubbing yields 0x0 sizes / off-center draws"

[[opcode.semantics.args]]
i = 1
role = "slot"
observed_types = ["imm", "g-int", "l-int"]

[[opcode.semantics.args]]
i = 2
role = "out_width"
observed_types = ["g-int", "l-int"]

[[opcode.semantics.args]]
i = 3
role = "out_height"
observed_types = ["g-int", "l-int"]
```

Then rebuild the generated files:

Run: `py -3.11 -X utf8 tools/opcodes_build.py --build`
Expected: succeeds, writes the 4 generated files.

Verify the label propagated:

Run: `grep -n '"label": "get-texture-size"' build/opcodes.json`
Expected: one match.

- [ ] **Step 2: Add `GetTextureSize` to `IHost` and all existing implementors (compile green)**

In `engine/Age.Engine/Hosting/IHost.cs`, add after `DrawTexture` (line 10):

```csharp
    (int Width, int Height) GetTextureSize(int slot);
```

In `engine/Age.Engine/Hosting/CaptureHost.cs`, add:

```csharp
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
```

In `engine/Age.Cli/Program.cs`, add to `AudioTraceHost` (with the other no-op IHost members):

```csharp
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
```

In `engine/Age.Engine.Tests/TextureOpsTests.cs`, add to `RecHost`:

```csharp
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
```

In `godot/GodotAdvHost.cs`, add a temporary stub (real impl in Task 3), next to the texture ops:

```csharp
    public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
```

Run: `dotnet build engine/AgeEngine.sln`
Expected: build succeeds (no `IHost` member missing).

- [ ] **Step 3: Write the failing test**

Create `engine/Age.Engine.Tests/TextureGeometryTests.cs`:

```csharp
using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class TextureGeometryTests
{
    private sealed class FakeSizeHost : IHost
    {
        public void ShowText(int o, string t) { }
        public void CallScript(long id) { }
        public void OnStub(int op) { }
        public void WaitForInput() { }
        public void CreateTexture(int slot, int w, int h) { }
        public void SetTexture(long resId, int slot) { }
        public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy) { }
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) { }
        public (int Width, int Height) GetTextureSize(int slot) => (0x140, 0xC8);
    }

    [Fact]
    public void GetTextureSizeWritesHostDimsIntoOutputGlobals()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // 0x208 (global-int 50)(global-int 60)(global-int 61): slot=50, out_w=G[60], out_h=G[61]
        const int T_GINT = 3;
        var ins = new Instruction(0, 0x208, new[]
        {
            new Operand(T_GINT, 50), new Operand(T_GINT, 60), new Operand(T_GINT, 61),
        });
        var script = new Script
        {
            Header = new ScriptHeader(0, 0, 0, 0, 0, 0),
            Instructions = new[] { ins },
            IndexByOffset = new Dictionary<int, int> { { 0, 0 } },
            Strings = new Dictionary<int, string>(),
        };
        var vm = new VirtualMachine(script, table, new FakeSizeHost());
        vm.Run();
        Assert.Equal(0x140, vm.Globals[60]);
        Assert.Equal(0xC8, vm.Globals[61]);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~TextureGeometryTests`
Expected: FAIL — `0x208` currently dispatches to `OnStub`, so `G[60]`/`G[61]` are never written and `vm.Globals[60]` throws `KeyNotFoundException` (or the asserts fail).

- [ ] **Step 5: Add the VM handler**

In `engine/Age.Engine/Vm/VirtualMachine.cs`, add a case alongside the other texture ops (after the `draw-texture` case, ~line 177):

```csharp
            case "get-texture-size":   // 0x208 (slot) (out_w) (out_h)
            {
                var (gw, gh) = _host.GetTextureSize((int)Read(a[0]));
                Write(a[1], gw); Write(a[2], gh);
                return pc + 1;
            }
```

- [ ] **Step 6: Run the new test to verify it passes**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~TextureGeometryTests`
Expected: PASS.

- [ ] **Step 7: Run the full engine suite (parity guardrail)**

Run: `dotnet test engine/AgeEngine.sln`
Expected: all tests PASS — including `TraceDiffTests` (C# trace byte-identical to `build/vm0-trace.json`). Rationale: with `CaptureHost.GetTextureSize` returning `(0,0)`, the handler writes `0`/`0`, which equals the prior default; the op is still one step, so offsets/halt/steps are unchanged.

- [ ] **Step 8: Confirm the Godot project still compiles**

Run: `dotnet build godot/Himegari.csproj`
Expected: build succeeds (the temporary `GetTextureSize` stub satisfies `IHost`).

- [ ] **Step 9: Commit**

```bash
git add vm-map/opcodes.toml tools/age_opcodes_himegari.py build/opcodes.json docs/opcode-reference.md build/opcode-coverage.md \
        engine/Age.Engine/Hosting/IHost.cs engine/Age.Engine/Hosting/CaptureHost.cs \
        engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Cli/Program.cs \
        engine/Age.Engine.Tests/TextureOpsTests.cs engine/Age.Engine.Tests/TextureGeometryTests.cs \
        godot/GodotAdvHost.cs
git commit -m "feat(a2b): implement 0x208 get-texture-size (geometry keystone)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `BmpHeader.ReadDims` + `gfx` diagnostic (headless numeric oracle)

Add a reusable BMP-dimension reader and an `Age.Cli gfx <SCENE>` command that runs a scene and dumps each `set-texture` / `get-texture-size` / `draw-texture` with resolved file + computed geometry — so we can validate geometry numerically without Godot (mirrors the `audio` command).

**Files:**
- Create: `engine/Age.Engine/Sys4/BmpHeader.cs`
- Test: `engine/Age.Engine.Tests/BmpHeaderTests.cs` (new)
- Modify: `engine/Age.Cli/Program.cs` (add `gfx` command + `GfxTraceHost`)

**Interfaces:**
- Produces: `BmpHeader.ReadDims(string path) → (int Width, int Height)` (0,0 on failure/missing); `Age.Cli gfx <SCENE.BIN> [0xADDR=VAL ...]`.
- Consumes: `ResourceMap.Resolve` / `ResourceMap.TexturePath` (Task-independent, existing); the `0x208` handler from Task 1.

- [ ] **Step 1: Write the failing test for `ReadDims`**

Create `engine/Age.Engine.Tests/BmpHeaderTests.cs`:

```csharp
using System.IO;
using Age.Engine.Sys4;
using Xunit;

public class BmpHeaderTests
{
    [Fact]
    public void ReadDimsReadsWidthAndHeightFromBmpHeader()
    {
        // Minimal 54-byte BMP header (BITMAPFILEHEADER 14 + BITMAPINFOHEADER 40); width=4, height=3.
        var b = new byte[54];
        b[0] = (byte)'B'; b[1] = (byte)'M';
        System.BitConverter.GetBytes(40).CopyTo(b, 14);   // header size
        System.BitConverter.GetBytes(4).CopyTo(b, 18);    // width
        System.BitConverter.GetBytes(3).CopyTo(b, 22);    // height
        var tmp = Path.Combine(Path.GetTempPath(), "agehdr_test.bmp");
        File.WriteAllBytes(tmp, b);
        try
        {
            var (w, h) = BmpHeader.ReadDims(tmp);
            Assert.Equal(4, w);
            Assert.Equal(3, h);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ReadDimsReturnsZeroForMissingFile()
    {
        var (w, h) = BmpHeader.ReadDims(Path.Combine(Path.GetTempPath(), "does_not_exist_agehdr.bmp"));
        Assert.Equal((0, 0), (w, h));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~BmpHeaderTests`
Expected: FAIL — `BmpHeader` does not exist (compile error).

- [ ] **Step 3: Implement `BmpHeader`**

Create `engine/Age.Engine/Sys4/BmpHeader.cs`:

```csharp
namespace Age.Engine.Sys4;

/// <summary>
/// Reads pixel dimensions from a BMP file header (BITMAPINFOHEADER: width at byte 18, height at byte 22,
/// both little-endian int32; height may be negative for top-down bitmaps). Used to give the VM the
/// texture size that opcode 0x208 (get-texture-size) needs, without decoding pixels. Our textures are
/// pre-converted BMPs (tools/convert_agf.py).
/// </summary>
public static class BmpHeader
{
    public static (int Width, int Height) ReadDims(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (0, 0);
        try
        {
            var b = new byte[26];
            using var fs = File.OpenRead(path);
            if (fs.Read(b, 0, 26) < 26 || b[0] != (byte)'B' || b[1] != (byte)'M') return (0, 0);
            int w = System.BitConverter.ToInt32(b, 18);
            int h = System.BitConverter.ToInt32(b, 22);
            return (System.Math.Abs(w), System.Math.Abs(h));
        }
        catch { return (0, 0); }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test engine/AgeEngine.sln --filter FullyQualifiedName~BmpHeaderTests`
Expected: PASS (both facts).

- [ ] **Step 5: Add the `gfx` command and `GfxTraceHost` to the CLI**

In `engine/Age.Cli/Program.cs`, add a new command block after the `audio` block (before the `trace` block):

```csharp
if (args[0] == "gfx")
{
    // gfx <SCENE.BIN> [0xADDR=VAL ...] — run the scene and dump executed texture ops in order with
    // resolved file + computed geometry (set-texture / get-texture-size / draw-texture). Diagnostic only.
    var sceneName = args[1];
    var sceneKey = Path.GetFileNameWithoutExtension(sceneName).ToUpperInvariant();
    var res = ResourceMap.Load();
    var host = new GfxTraceHost(res, sceneKey);
    var vm = new VirtualMachine(Sys4Loader.Load(Paths.Scripts()[sceneName.ToUpperInvariant()], table), table, host);
    foreach (var s in args.Skip(2))
    {
        var kv = s.Split('=');
        int k = kv[0].StartsWith("0x") ? Convert.ToInt32(kv[0], 16) : int.Parse(kv[0]);
        long v = kv[1].StartsWith("0x") ? Convert.ToInt64(kv[1], 16) : long.Parse(kv[1]);
        vm.Globals[k] = v;
    }
    vm.Run();
    Console.WriteLine($"{sceneName}: {host.Events.Count} texture ops (halt: {vm.HaltReason})");
    foreach (var line in host.Events) Console.WriteLine("  " + line);
    return 0;
}
```

Add the `GfxTraceHost` class next to `AudioTraceHost` at the bottom of `Program.cs`:

```csharp
sealed class GfxTraceHost : IHost
{
    private readonly ResourceMap _res;
    private readonly string _scene;
    private readonly Dictionary<int, string?> _slotBmp = new();   // slot -> resolved BMP path (or null)
    public List<string> Events { get; } = new();
    public GfxTraceHost(ResourceMap res, string scene) { _res = res; _scene = scene; }

    public (int Width, int Height) GetTextureSize(int slot)
    {
        _slotBmp.TryGetValue(slot, out var bmp);
        var (w, h) = BmpHeader.ReadDims(bmp);
        Events.Add($"get-tex-size slot={slot} -> {w}x{h}");
        return (w, h);
    }

    public void SetTexture(long resId, int slot)
    {
        var e = _res.Resolve(_scene, resId);
        var bmp = e != null ? ResourceMap.TexturePath(e) : null;
        _slotBmp[slot] = bmp;
        Events.Add($"set-texture slot={slot} res=0x{resId:x} -> {(e?.Name ?? "<unresolved>")}"
                   + (bmp == null ? " [NO BMP]" : ""));
    }

    public void DrawTexture(int slot, int sx, int sy, int w, int h, int dx, int dy)
    {
        _slotBmp.TryGetValue(slot, out var bmp);
        Events.Add($"draw-texture slot={slot} src=({sx},{sy} {w}x{h}) dst=({dx},{dy}) "
                   + $"file={(bmp != null ? System.IO.Path.GetFileName(bmp) : "<none>")}");
    }

    public void CreateTexture(int slot, int width, int height) => Events.Add($"create-texture slot={slot} {width}x{height}");
    public void ShowText(int offset, string text) { }
    public void CallScript(long id) { }
    public void OnStub(int opcode) { }
    public void WaitForInput() { }
    public void PlayBgm(long id) { }
    public void PlayVoice(long id) { }
}
```

- [ ] **Step 6: Run the diagnostic and inspect the geometry**

Run: `dotnet run --project engine/Age.Cli -- gfx SC0000.BIN`
Expected: prints an ordered list of texture ops. Sanity checks:
- The full-screen background/event-CG draws show `src=(0,0 800x600)` (`0x320`×`0x258`) `dst=(0,0)` — i.e. `800x600` / `800x500` (bg is natively 800×500) at the origin.
- Any sprite `draw-texture` shows **non-zero** `w`×`h` and a plausible on-screen `dst` (not `0x0`, not wildly off-canvas). Before Task 1 these were `0×0`; the `get-tex-size` lines now report real dims from the BMP header.

(No unit test for the command itself — it's a diagnostic, like `audio`. The `ReadDims` unit test covers the load-bearing logic.)

- [ ] **Step 7: Commit**

```bash
git add engine/Age.Engine/Sys4/BmpHeader.cs engine/Age.Engine.Tests/BmpHeaderTests.cs engine/Age.Cli/Program.cs
git commit -m "feat(a2b): BmpHeader.ReadDims + gfx diagnostic (headless geometry oracle)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Godot screen-backbuffer blit compositor

Replace the TextureRect-per-slot approximation with one 800×600 screen `Image` that `draw-texture` blits into (src rect → dst) in execution order, displayed by a single `TextureRect`. The Godot host reports real texture dims (from the BMP header, on the VM thread) via `GetTextureSize`, replacing the Task-1 stub. This is the pass that makes sprites and backgrounds actually appear at correct positions/sizes on screen.

**Files:**
- Modify: `godot/GodotAdvHost.cs` (per-slot path + dims; real `GetTextureSize`; `DrawTexture` → deferred blit)
- Modify: `godot/Main.cs` (screen `Image` backbuffer + one `TextureRect`; `BlitSlot`; remove per-slot `TextureRect`s)

**Interfaces:**
- Consumes: `BmpHeader.ReadDims` (Task 2); `ResourceMap.Resolve` / `TexturePath` (existing); `IHost.GetTextureSize` (Task 1).
- Produces: `Main.BlitSlot(string bmpPath, int srcX, int srcY, int w, int h, int dstX, int dstY)` (main-thread, `CallDeferred`-invoked); a single displayed screen texture.

- [ ] **Step 1: Rework `GodotAdvHost` — cache path + dims per slot, real `GetTextureSize`, blit on draw**

In `godot/GodotAdvHost.cs`, replace the texture-ops region (the `_slotBmp` field stays; add a dims cache) so it reads:

```csharp
    private readonly Dictionary<int, string?> _slotBmp = new();          // slot -> pre-converted BMP path
    private readonly Dictionary<int, (int W, int H)> _slotDims = new();  // slot -> texture dims (BMP header)
```

```csharp
    // ---- texture ops (run on the VM thread; marshal Godot node work to the main thread) ----
    public void CreateTexture(int slot, int width, int height) { _slotBmp[slot] = null; _slotDims[slot] = (0, 0); }

    public void SetTexture(long resourceId, int slot)
    {
        var asset = _res.Resolve(_scene, resourceId);
        var bmp = asset != null ? ResourceMap.TexturePath(asset) : null;
        _slotBmp[slot] = bmp;
        _slotDims[slot] = BmpHeader.ReadDims(bmp);   // synchronous: dims from the header, no Godot Image
    }

    // Dims are read from the BMP header on the VM thread so the bytecode's geometry math (which calls this
    // synchronously right after set-texture) sees the real size. Pixels are blitted later on the main thread.
    public (int Width, int Height) GetTextureSize(int slot)
        => _slotDims.TryGetValue(slot, out var d) ? (d.W, d.H) : (0, 0);

    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY)
    {
        if (_slotBmp.TryGetValue(slot, out var bmp) && bmp != null)
            _main.CallDeferred("BlitSlot", bmp, srcX, srcY, width, height, dstX, dstY);
    }
```

Add the `BmpHeader` namespace use — `GodotAdvHost.cs` already has `using Age.Engine.Sys4;`, so `BmpHeader.ReadDims` resolves. Remove the temporary `GetTextureSize` stub added in Task 1 (it is replaced by the real one above).

- [ ] **Step 2: Rework `Main` — one screen backbuffer, blit into it, drop per-slot TextureRects**

In `godot/Main.cs`, replace the slot fields and `_Ready` stage setup so the stage holds **one** `TextureRect` showing a shared screen `Image`. Replace:

```csharp
    private Control _stage = null!;                       // texture layer (behind the text)
    private readonly Dictionary<int, TextureRect> _slots = new();
```

with:

```csharp
    private TextureRect _screenView = null!;              // shows the composited screen backbuffer
    private Image _screen = null!;                        // 800x600 immediate-mode canvas
    private ImageTexture _screenTex = null!;
```

In `_Ready`, replace the `_stage` block with:

```csharp
        // Screen backbuffer: one 800x600 canvas that draw-texture blits into, shown behind the dialogue.
        _screen = Image.CreateEmpty(800, 600, false, Image.Format.Rgba8);
        _screenTex = ImageTexture.CreateFromImage(_screen);
        _screenView = new TextureRect
        {
            Texture = _screenTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _screenView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_screenView);   // added first -> draws behind the text/status labels
```

Replace the `DrawSlot` method with `BlitSlot`:

```csharp
    // Blit a source BMP (src rect) onto the screen backbuffer at (dstX,dstY), then refresh the display
    // texture. Execution order == paint order, so later draws (sprites) land over earlier ones (bg).
    public void BlitSlot(string bmpPath, int srcX, int srcY, int w, int h, int dstX, int dstY)
    {
        var src = new Image();
        if (src.LoadBmpFromBuffer(System.IO.File.ReadAllBytes(bmpPath)) != Error.Ok)
        { GD.Print($"BMP load failed {bmpPath}"); return; }
        if (src.GetFormat() != Image.Format.Rgba8) src.Convert(Image.Format.Rgba8);

        // Clamp the source rect to the image; a zero/negative size falls back to the full image.
        int sw = w > 0 ? w : src.GetWidth();
        int sh = h > 0 ? h : src.GetHeight();
        sw = System.Math.Min(sw, src.GetWidth() - srcX);
        sh = System.Math.Min(sh, src.GetHeight() - srcY);
        if (sw <= 0 || sh <= 0) return;

        _screen.BlitRect(src, new Rect2I(srcX, srcY, sw, sh), new Vector2I(dstX, dstY));
        _screenTex.Update(_screen);
    }
```

- [ ] **Step 3: Build the Godot project**

Run: `godot --headless --path godot --import`
Then: `dotnet build godot/Himegari.csproj`
Expected: both succeed (no references to the removed `_stage`/`_slots`/`DrawSlot`).

- [ ] **Step 4: Run the headless selftest (VM-behavior guardrail)**

Run: `godot --headless --path godot -- --selftest`
Expected: `SELFTEST OK: 186 lines match vm0 trace`, exit 0. Dialogue flow does not branch on pixel geometry, so returning real dims from `GetTextureSize` must not change the emitted show-text offsets. **If it FAILS,** do not disable the check — investigate which geometry global leaked into control flow (a genuine finding); use `Age.Cli gfx SC0000.BIN` to see the computed values and trace the divergence.

- [ ] **Step 5: Eyeball the opening (human oracle)**

Run: `godot --path godot`
Expected: SC0000 opening renders with the background placed correctly (filling the frame / at the origin, not off-center) and any sprites at plausible positions and non-zero sizes. Known-and-expected caveats (out of scope this pass, do not treat as regressions): sprites may show an opaque background box (no chromakey yet) and `AE*` fades draw opaque/instant (no alpha yet).

- [ ] **Step 6: Commit**

```bash
git add godot/GodotAdvHost.cs godot/Main.cs
git commit -m "feat(a2b): screen-backbuffer blit compositor + real GetTextureSize

Correct sprite/background geometry: get-texture-size feeds the bytecode's own
centering/anchor math; draw-texture blits src rect -> dst into one 800x600
canvas in execution order. Selftest byte-parity preserved.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage:**
- `0x208` real op + `IHost.GetTextureSize` → Task 1. ✓
- Screen backbuffer blit compositor, source images per slot, dest-handle collapse → Task 3. ✓
- `Age.Cli gfx` diagnostic → Task 2 (Step 5–6). ✓
- Trace/selftest parity preserved → Task 1 Step 7 (engine `TraceDiffTests`) + Task 3 Step 4 (Godot selftest). ✓
- Opcode rename in `opcodes.toml` + rebuild → Task 1 Step 1. ✓
- Empty-slot / missing-BMP / off-canvas / dims-mismatch edge cases → `BmpHeader.ReadDims` returns `(0,0)`; `BlitSlot` clamps src rect and returns on empty; `GetTextureSize` returns `(0,0)` for empty slots. ✓
- Deferred (out of scope): fades/alpha, chromakey, true multi-surface, animation ops → not implemented; noted in Task 3 Step 5 caveats. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; commands have expected output. ✓

**Type consistency:** `GetTextureSize(int slot) → (int Width, int Height)` identical across `IHost`, `CaptureHost`, `AudioTraceHost`, test `RecHost`, `FakeSizeHost`, `GfxTraceHost`, `GodotAdvHost`. `BmpHeader.ReadDims(string?) → (int Width, int Height)` consistent in Tasks 2 and 3. `Main.BlitSlot(string,int,int,int,int,int,int)` matches the `CallDeferred("BlitSlot", bmp, srcX, srcY, width, height, dstX, dstY)` argument order in `GodotAdvHost.DrawTexture`. VM dispatch label `"get-texture-size"` matches the `label` set in `opcodes.toml`. ✓

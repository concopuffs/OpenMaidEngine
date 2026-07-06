# A2b-Background — Engine-Driven Background Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render SC0000's locally-loaded full-screen background (`set-texture 0x21` → slot `0xe` → `draw-texture` 800×600) behind the dialogue, driven by the bytecode executing the real texture ops and resolving the resource id through a data map.

**Architecture:** Promote `create/set/draw-texture` from VM stubs to typed `IHost` methods; an investigation resolves resource id `0x21` → an AGF filename into a tracked `vm-map/resources.json`; `AGF2BMP2AGF.exe` converts it to a BMP; the Godot backend loads the mapped image into a slot and composites the full-screen slot into a background node behind the dialogue.

**Tech Stack:** C# / .NET 8, Godot 4.7 mono, xUnit, Python 3.11, `AGF2BMP2AGF.exe`, (optionally Frida).

## Global Constraints

- **Work on a feature branch** (e.g. `feat/a2b-background`); do not commit to `main`.
- **Concrete target = resource id `0x21`** — SC0000's self-loaded full-screen image (`set-texture 0x21 0xe`, drawn 800×600 at instr `0x5e8`). The machinery is resource-agnostic; only Task 2's *lookup* is `0x21`-specific.
- **Confirmed opcode ABI** (from `build/opcodes.json`): `create-texture 0x1f8` argc 4 = `(slot, w, h, ?)`; `set-texture 0x1f9` argc 3 = `(resId, slot, ?)`; `draw-texture 0x1fb` argc 8 = `(fade, slot, x, y, w, h, ?, ?)`. VM dispatches on the Kelebek labels.
- **Trace parity is sacred:** new `IHost` methods must not change `Steps`/`Emitted`. `CaptureHost` no-ops them → A1 trace-diff + `WaitForInputTests` + A2a `--selftest` stay green. Verified in Task 1.
- **Engine-driven, not pinned:** the background renders only because `set-texture`/`draw-texture` execute and the resource id resolves through `vm-map/resources.json`. No image is drawn without the bytecode driving it.
- `vm-map/resources.json` (the id→AGF map) is **tracked**; `build/textures/*.BMP` (converted images) are **gitignored/regenerable** via `tools/convert_agf.py`.
- Godot toolchain (verified A2a): `GODOT="S:/Godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64_console.exe"`; `dotnet build godot/Himegari.csproj`; `"$GODOT" --headless --path godot [-- --selftest]`.
- **Contingency (Task 2 is a gate):** this plan assumes SC0000's presentable full-screen background is the *locally-loaded* res `0x21`. If Task 2 finds res `0x21` is not a sensible background (e.g. it's an overlay and the real bg is the cross-context slot-3 image), **pause and revise Tasks 3–5** to target the correct resource — the machinery is unchanged, only the mapped id/AGF differs.

---

## File Structure

```
engine/Age.Engine/Hosting/IHost.cs        (modify: +CreateTexture/SetTexture/DrawTexture)
engine/Age.Engine/Hosting/CaptureHost.cs  (modify: +3 no-ops)
engine/Age.Engine/Vm/VirtualMachine.cs    (modify: 3 texture-op handlers)
engine/Age.Engine/Sys4/Paths.cs           (modify: +ResourcesJson, +TexturesDir, +VmMap)
engine/Age.Engine/Sys4/ResourceMap.cs     (create: id→AGF resolver)
engine/Age.Engine.Tests/TextureOpsTests.cs        (create: Task 1 recording test)
engine/Age.Engine.Tests/BackgroundResolveTests.cs (create: Task 3 resolve-oracle)
tools/convert_agf.py                       (create: AGF→BMP via AGF2BMP2AGF.exe)
vm-map/resources.json                      (create: {"0x21": "<AGF>"} — Task 2)
godot/GodotAdvHost.cs                       (modify: texture ops + slot dict)
godot/Main.cs                               (modify: background TextureRect)
docs/phase-a-slice-plan.md                  (modify: mark A2b-bg done)
```

---

## Task 1: Engine texture-op wiring

**Files:** Modify `IHost.cs`, `CaptureHost.cs`, `VirtualMachine.cs`; Create `Age.Engine.Tests/TextureOpsTests.cs`.

**Interfaces — Produces:** `IHost.CreateTexture(int slot,int width,int height)`, `IHost.SetTexture(long resourceId,int slot)`, `IHost.DrawTexture(int slot,int x,int y,int width,int height)`; `CaptureHost` no-ops them.

- [ ] **Step 1: Write the failing test** — `engine/Age.Engine.Tests/TextureOpsTests.cs`:

```csharp
using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class TextureOpsTests
{
    private sealed class RecHost : IHost
    {
        public List<(long resId, int slot)> Sets = new();
        public List<(int slot, int w, int h)> Draws = new();
        public int Creates;
        public void ShowText(int o, string t) { }
        public void CallScript(long id) { }
        public void OnStub(int op) { }
        public void WaitForInput() { }
        public void CreateTexture(int slot, int w, int h) => Creates++;
        public void SetTexture(long resId, int slot) => Sets.Add((resId, slot));
        public void DrawTexture(int slot, int x, int y, int w, int h) => Draws.Add((slot, w, h));
    }

    [Fact]
    public void SC0000FiresTextureOpsIncludingRes0x21FullScreen()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new RecHost();
        new VirtualMachine(script, table, host).Run();
        Assert.True(host.Creates > 0, "create-texture should fire");
        Assert.Contains(host.Sets, s => s.resId == 0x21);                 // the local bg load
        Assert.Contains(host.Draws, d => d.w == 0x320 && d.h == 0x258);   // a full-screen (800x600) draw
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`IHost` lacks the methods → `RecHost` won't compile against it)
```bash
dotnet test engine/Age.Engine.Tests --filter FullyQualifiedName~TextureOpsTests
```

- [ ] **Step 3: Implement**

`engine/Age.Engine/Hosting/IHost.cs` — add three methods:
```csharp
    void CreateTexture(int slot, int width, int height);
    void SetTexture(long resourceId, int slot);
    void DrawTexture(int slot, int x, int y, int width, int height);
```
`engine/Age.Engine/Hosting/CaptureHost.cs` — add no-ops (after `WaitForInput`):
```csharp
    public void CreateTexture(int slot, int width, int height) { }
    public void SetTexture(long resourceId, int slot) { }
    public void DrawTexture(int slot, int x, int y, int width, int height) { }
```
`engine/Age.Engine/Vm/VirtualMachine.cs` — add handlers before the `default:` in `Step`'s switch:
```csharp
            case "create-texture":
                _host.CreateTexture((int)Read(a[0]), (int)Read(a[1]), (int)Read(a[2])); return pc + 1;
            case "set-texture":
                _host.SetTexture(Read(a[0]), (int)Read(a[1])); return pc + 1;
            case "draw-texture":
                _host.DrawTexture((int)Read(a[1]), (int)Read(a[2]), (int)Read(a[3]), (int)Read(a[4]), (int)Read(a[5])); return pc + 1;
```

- [ ] **Step 4: Run — expect PASS + full regression**
```bash
dotnet test engine/AgeEngine.sln
```
Expected: 8 passed (new test + the 7 existing; RECOVER/trace-diff/WaitForInput unchanged — `CaptureHost` no-ops keep `Steps`/`Emitted` identical).

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Engine/Hosting engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/TextureOpsTests.cs
git commit -m "feat(a2b): promote create/set/draw-texture to IHost methods (trace parity kept)"
```

---

## Task 2: Investigation — resolve res 0x21 → AGF + convert + seed map

**Files:** Create `tools/convert_agf.py`, `vm-map/resources.json`; produce `build/textures/<AGF>.BMP`.

**Interfaces — Produces:** `vm-map/resources.json` = `{"0x21": "<AGF filename>"}`; `build/textures/<stem>.BMP` for the mapped AGF; `tools/convert_agf.py` (CLI: `<AGF-name> [<AGF-name> ...]`).

This is the investigation **gate**. Output (the specific AGF) is discovered; the steps are concrete.

- [ ] **Step 1: Write the AGF converter** — `tools/convert_agf.py`:
```python
#!/usr/bin/env python3
"""Convert named AGF files (in extracted/DATA2 or DATA5) to BMP via AGF2BMP2AGF.exe,
into build/textures/. Run: py -3.11 -X utf8 tools/convert_agf.py EV001AA.AGF ..."""
import os, sys, shutil, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths

EXE = paths.EXTRACTED / "DATA1" / "AGF2BMP2AGF.exe"  # tool lives in extracted/DATA1
SRC_DIRS = [paths.EXTRACTED / "DATA2", paths.EXTRACTED / "DATA5"]
OUT = paths.BUILD / "textures"

def find(name):
    for d in SRC_DIRS:
        p = d / name
        if p.exists():
            return p
    return None

def main(argv):
    OUT.mkdir(parents=True, exist_ok=True)
    for name in argv:
        src = find(name)
        if not src:
            print(f"NOT FOUND: {name}"); continue
        tmp = OUT / name
        shutil.copy(src, tmp)
        subprocess.run([str(EXE), name], cwd=str(OUT), check=True)
        tmp.unlink(missing_ok=True)  # drop the copied .AGF, keep the .BMP
        print(f"converted {name} -> {OUT / (os.path.splitext(name)[0] + '.BMP')}")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
```
Sanity-check it converts a known file:
```bash
py -3.11 -X utf8 tools/convert_agf.py EV001CG.AGF
ls -la build/textures/EV001CG.BMP   # ~1.44 MB (800x600x24)
```
Expected: `build/textures/EV001CG.BMP` exists.

- [ ] **Step 2: Resolve res 0x21 → AGF (static first).** Probe the id→name path — `SYS4INI.BIN` is the ALF filename index; check whether id `0x21`/`33` (or the value in a nearby resource table) maps to an AGF name. Inspect:
```bash
py -3.11 -X utf8 tools/sys4load.py "S:/Game Hacking/Eushully/Himegari/extracted/DATA1/SYS4INI.BIN" --strings 2>&1 | grep -iE '\.AGF|EV[0-9]|BG[0-9]' | head -30
```
and cross-reference SC0000's texture context (what `create-texture`/`set-texture` values surround `0x21`) via `build/disasm/SC0000.asm` around instr `0x5e1`. Goal: a candidate AGF name for res `0x21`.

- [ ] **Step 3: If static is inconclusive — Frida capture (ground truth).** Check availability: `py -3.11 -c "import frida; print(frida.__version__)"` (or `frida --version`). If present, attach to the running translated game, hook the graphic-load call, reach SC0000, and record the `id → AGF` for the res-`0x21` load. If Frida is unavailable/impractical, **fallback:** convert the handful of plausible full-screen intro AGFs from DATA2 (`py -3.11 -X utf8 tools/convert_agf.py <candidates>`), open the BMPs, and pick the one that is SC0000's opening background by eye. Record which AGF corresponds to res `0x21`.

- [ ] **Step 4: Seed the map + convert the winner.** Write `vm-map/resources.json`:
```json
{ "0x21": "REPLACE_WITH_RESOLVED.AGF" }
```
(the resolved AGF name from Step 2/3), then:
```bash
py -3.11 -X utf8 tools/convert_agf.py <resolved AGF name>
ls build/textures/   # the resolved .BMP is present
```
Confirm by eye that `build/textures/<resolved>.BMP` is a plausible SC0000 background. **If it clearly isn't a background, invoke the Global-Constraints contingency** (retarget to the correct resource) before proceeding.

- [ ] **Step 5: Commit**
```bash
git add tools/convert_agf.py vm-map/resources.json
git commit -m "feat(a2b): resolve SC0000 bg res 0x21 -> AGF; convert_agf tool + resources.json"
```

---

## Task 3: Resolve-oracle (headless) + `ResourceMap` + `Paths`

**Files:** Modify `Age.Engine/Sys4/Paths.cs`; Create `Age.Engine/Sys4/ResourceMap.cs`, `Age.Engine.Tests/BackgroundResolveTests.cs`.

**Interfaces — Produces:** `Paths.ResourcesJson`, `Paths.TexturesDir`; `ResourceMap { static ResourceMap Load(string path); string? Resolve(long resourceId); }`.

- [ ] **Step 1: Write the failing test** — `engine/Age.Engine.Tests/BackgroundResolveTests.cs`:
```csharp
using System.IO;
using System.Linq;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class BackgroundResolveTests
{
    private sealed class RecHost : IHost
    {
        public System.Collections.Generic.List<(long resId, int slot)> Sets = new();
        public System.Collections.Generic.List<(int slot, int w, int h)> Draws = new();
        public void ShowText(int o, string t) { }
        public void CallScript(long id) { }
        public void OnStub(int op) { }
        public void WaitForInput() { }
        public void CreateTexture(int slot, int w, int h) { }
        public void SetTexture(long resId, int slot) => Sets.Add((resId, slot));
        public void DrawTexture(int slot, int x, int y, int w, int h) => Draws.Add((slot, w, h));
    }

    [Fact]
    public void SC0000BackgroundResolvesToAConvertedImage()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new RecHost();
        new VirtualMachine(script, table, host).Run();

        // the bg: res 0x21 loaded into a slot that is then drawn full-screen
        var set = host.Sets.First(s => s.resId == 0x21);
        Assert.Contains(host.Draws, d => d.slot == set.slot && d.w == 0x320 && d.h == 0x258);

        var map = ResourceMap.Load(Paths.ResourcesJson);
        string? agf = map.Resolve(0x21);
        Assert.False(string.IsNullOrEmpty(agf), "res 0x21 must resolve in vm-map/resources.json");
        string bmp = Path.Combine(Paths.TexturesDir, Path.GetFileNameWithoutExtension(agf) + ".BMP");
        Assert.True(File.Exists(bmp), $"converted background missing: {bmp}");
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`Paths.ResourcesJson`/`ResourceMap` missing)
```bash
dotnet test engine/Age.Engine.Tests --filter FullyQualifiedName~BackgroundResolveTests
```

- [ ] **Step 3: Implement** — add to `engine/Age.Engine/Sys4/Paths.cs` (after `OpcodesJson`):
```csharp
    public static string VmMap => Path.Combine(Repo, "vm-map");
    public static string ResourcesJson => Path.Combine(VmMap, "resources.json");
    public static string TexturesDir => Path.Combine(Build, "textures");
```
Create `engine/Age.Engine/Sys4/ResourceMap.cs`:
```csharp
using System.Text.Json;
namespace Age.Engine.Sys4;
public sealed class ResourceMap
{
    private readonly IReadOnlyDictionary<long, string> _m;
    private ResourceMap(IReadOnlyDictionary<long, string> m) => _m = m;

    public static ResourceMap Load(string path)
    {
        var m = new Dictionary<long, string>();
        if (File.Exists(path))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in doc.RootElement.EnumerateObject())
                m[System.Convert.ToInt64(p.Name, 16)] = p.Value.GetString() ?? "";
        }
        return new ResourceMap(m);
    }

    public string? Resolve(long resourceId) => _m.TryGetValue(resourceId, out var v) ? v : null;
}
```

- [ ] **Step 4: Run — expect PASS + full regression**
```bash
dotnet test engine/AgeEngine.sln
```
Expected: 9 passed. (This is the engine-driven-resolution oracle: SC0000's execution routes res 0x21 to a full-screen draw, and the map+conversion yield a real file — no pixels needed.)

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Engine/Sys4/Paths.cs engine/Age.Engine/Sys4/ResourceMap.cs engine/Age.Engine.Tests/BackgroundResolveTests.cs
git commit -m "feat(a2b): ResourceMap + Paths + headless bg resolve-oracle (res 0x21 -> converted image)"
```

---

## Task 4: Godot backend — texture ops + background compositing

**Files:** Modify `godot/GodotAdvHost.cs`, `godot/Main.cs`.

**Interfaces — Consumes:** `ResourceMap`, `Paths.ResourcesJson`, `Paths.TexturesDir`. **Produces:** the background rendered behind the dialogue when the bytecode draws the resolved full-screen slot.

- [ ] **Step 1: Extend `godot/GodotAdvHost.cs`** — add the texture-op state + methods (add fields near the top, methods after `SignalInput`):
```csharp
    // texture state (main-thread-facing values captured on the worker, applied via CallDeferred)
    private readonly Dictionary<int, (int W, int H)> _slotDims = new();
    private readonly ResourceMap _resources = ResourceMap.Load(Paths.ResourcesJson);

    public void CreateTexture(int slot, int width, int height) { _slotDims[slot] = (width, height); }

    public void SetTexture(long resourceId, int slot)
    {
        string? agf = _resources.Resolve(resourceId);
        if (string.IsNullOrEmpty(agf)) return;                        // unmapped -> slot stays empty
        string bmp = System.IO.Path.Combine(Paths.TexturesDir,
                        System.IO.Path.GetFileNameWithoutExtension(agf) + ".BMP");
        if (System.IO.File.Exists(bmp)) _main.CallDeferred("LoadSlotTexture", slot, bmp);
    }

    public void DrawTexture(int slot, int x, int y, int width, int height)
    {
        if (width == 0x320 && height == 0x258)                        // full-screen -> background layer
            _main.CallDeferred("ShowBackground", slot);
    }
```
Add `using Age.Engine.Sys4;` at the top of the file.

- [ ] **Step 2: Extend `godot/Main.cs`** — add the background node + the two deferred UI methods, and load `ResourceMap`'s images. Add a field and create the node in `_Ready` **before** the `_text` label (so it's z-behind); add the methods near the other UI methods:
```csharp
    private TextureRect _bg = null!;
    private readonly System.Collections.Generic.Dictionary<int, Texture2D> _slotTex = new();
```
In `_Ready`, as the FIRST child added (before `_text`):
```csharp
        _bg = new TextureRect { StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
        _bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_bg);
```
New public methods (invoked via `CallDeferred` on the main thread):
```csharp
    public void LoadSlotTexture(int slot, string bmpPath)
    {
        var img = Image.LoadFromFile(bmpPath);
        if (img != null) _slotTex[slot] = ImageTexture.CreateFromImage(img);
    }

    public void ShowBackground(int slot)
    {
        if (_slotTex.TryGetValue(slot, out var tex)) _bg.Texture = tex;
    }
```

- [ ] **Step 3: Build + regression (headless selftest still green)**
```bash
GODOT="S:/Godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64_console.exe"
dotnet build godot/Himegari.csproj 2>&1 | grep -iE 'Build succeeded|error CS'
"$GODOT" --headless --path godot -- --selftest 2>&1 | grep SELFTEST
```
Expected: `Build succeeded`; `SELFTEST OK: 186 lines match vm0 trace` (dialogue path unaffected by the new texture handling).

- [ ] **Step 4: Commit**
```bash
git add godot/GodotAdvHost.cs godot/Main.cs
git commit -m "feat(a2b): Godot backend renders the resolved full-screen background behind dialogue"
```

---

## Task 5: Manual visual + docs

**Files:** Modify `docs/phase-a-slice-plan.md`.

- [ ] **Step 1: Manual visual check (human)** — run windowed:
```bash
"S:/Godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64.exe" --path godot
```
Confirm by eye: SC0000's dialogue now appears **over a background image** (the converted AGF), and the dialogue still advances on click/Enter. (If the bg is wrong/absent, revisit Task 2's resolution.)

- [ ] **Step 2: Update `docs/phase-a-slice-plan.md`** — under the A2a subsection add:
```
### A2b-Background — engine-driven background layer ✅ DONE (2026-07-06)
Promoted create/set/draw-texture (0x1f8/0x1f9/0x1fb) to IHost methods (CaptureHost no-ops → trace parity
kept). SC0000's local full-screen res 0x21 resolves via vm-map/resources.json to an AGF, converted by
tools/convert_agf.py (AGF2BMP2AGF → build/textures/*.BMP), rendered behind the dialogue by GodotAdvHost +
a background TextureRect. Headless resolve-oracle asserts res 0x21 -> full-screen draw -> converted image;
engine tests 9/9; A2a selftest still OK. Only the bg LAYER (other ~239 draws still stub); resolution map
grows over time. Next A2b: voice/BGM, choices, general resolution.
Spec/plan: docs/superpowers/{specs,plans}/2026-07-06-a2b-background*.md.
```

- [ ] **Step 3: Commit**
```bash
git add docs/phase-a-slice-plan.md
git commit -m "docs(a2b): mark engine-driven background layer done"
```

---

## Self-Review

**Spec coverage:** promote create/set/draw-texture to typed IHost + CaptureHost no-op + trace parity (Task 1) ✓ · resolution map as tracked profile data + AGF pipeline (Task 2) ✓ · investigation static-then-Frida with eyeball fallback + gate (Task 2 Steps 2–4) ✓ · ResourceMap resolver + Godot backend rendering only resolved full-screen slots + compositing behind dialogue (Tasks 3–4) ✓ · headless resolve-oracle (no pixels), A1/A2a regression, manual visual (Tasks 3,4,5) ✓ · engine-driven-not-pinned (map lookup gated by executed ops) ✓ · out-of-scope (239 draws, general resolution, voice/choices) absent ✓.

**Placeholder scan:** the only discovered value is Task 2's AGF name (`REPLACE_WITH_RESOLVED.AGF`) — that is the *output* of an investigation task, with concrete steps to find it (static probe → Frida → eyeball fallback), not an unfilled plan gap. All code steps are complete.

**Type consistency:** `IHost.CreateTexture(int,int,int)`/`SetTexture(long,int)`/`DrawTexture(int,int,int,int,int)` defined in Task 1 and implemented by `CaptureHost` (Task 1), `RecHost` (Tasks 1,3), and `GodotAdvHost` (Task 4). `ResourceMap.Load(string)`/`Resolve(long)→string?` defined Task 3, used Tasks 3–4. `Paths.ResourcesJson`/`TexturesDir` defined Task 3, used Tasks 3–4. `Main.LoadSlotTexture(int,string)`/`ShowBackground(int)` defined Task 4 and called via `CallDeferred` from `GodotAdvHost` (Task 4). The full-screen sentinel `0x320`×`0x258` (800×600) is used identically in Tasks 1, 3, 4.

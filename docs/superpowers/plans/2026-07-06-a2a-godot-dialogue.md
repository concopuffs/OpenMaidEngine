# A2a — Interactive Dialogue Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Godot 4.7 (.NET) project that references `Age.Engine` in-process and plays SC0000's dialogue as a message window that pauses at `wait-for-input` and resumes on a click, proven by a headless self-test.

**Architecture:** Add one method to the VM's `IHost` seam (`WaitForInput`); the VM core is otherwise unchanged. A Godot project in `godot/` runs `vm.Run()` on a worker thread; the host blocks that thread at `wait-for-input` on a `SemaphoreSlim`, marshals text to the UI via `CallDeferred`, and resumes on click. A `--selftest` mode auto-drives the gate headlessly and asserts the emitted text equals the A1 `vm0-trace.json`.

**Tech Stack:** C# / .NET 8, Godot 4.7-stable **mono** (`S:/Godot/Godot_v4.7-stable_mono_win64/`), xUnit, `System.Text.Json`.

## Global Constraints

- **Work on a feature branch** (e.g. `feat/a2a-godot-dialogue`); do not commit to `main`.
- **Verified Godot toolchain** (spiked 2026-07-06). Let `GODOT="S:/Godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64_console.exe"`:
  - Import: `"$GODOT" --headless --path godot --import`
  - Build C#: `dotnet build godot/Himegari.csproj` (Godot.NET.Sdk restore is cached; no network)
  - Run headless: `"$GODOT" --headless --path godot [-- --selftest]` (`OS.GetCmdlineUserArgs()` gets args after `--`)
- The Godot game project targets **`net8.0`** with **`Godot.NET.Sdk/4.7.0`** and `<EnableDynamicLoading>true</EnableDynamicLoading>`, referencing `../engine/Age.Engine/Age.Engine.csproj`.
- **Seam rule preserved:** the only VM-core change is `IHost.WaitForInput()`; `CaptureHost.WaitForInput()` is a no-op so **A1's RECOVER + trace-diff stay green** (nothing the trace observes changes).
- Data via `Age.Engine.Sys4.Paths` (resolves up to `age-reimpl/`; `godot/` is inside it). Prereqs: `build/opcodes.json`, `build/vm0-trace.json`, `../extracted/DATA1/SC0000.BIN` (all present from A1).
- Target scene: **SC0000.BIN** (CLEAN, 186 lines, halt `exit`).
- `.NET`/`Godot` build output stays gitignored (`.godot/`, `engine/**/bin|obj`, `[Bb]in/obj`, `[Oo]bj`); track only source (`godot/*.cs`, `godot/*.tscn`, `godot/project.godot`, `godot/Himegari.csproj`).

---

## File Structure

```
engine/Age.Engine/Hosting/IHost.cs        (modify: + WaitForInput)
engine/Age.Engine/Hosting/CaptureHost.cs  (modify: + WaitForInput no-op)
engine/Age.Engine/Vm/VirtualMachine.cs    (modify: wait-for-input calls host)
engine/Age.Engine.Tests/WaitForInputTests.cs  (create)
godot/project.godot          (create)  Godot project, main scene = Main.tscn
godot/Himegari.csproj        (create)  Godot.NET.Sdk + Age.Engine reference
godot/Main.tscn              (create)  root Control + script (UI built in code)
godot/Main.cs                (create)  worker thread, UI methods, input, selftest
godot/GodotAdvHost.cs        (create)  IHost: capture + CallDeferred + blocking gate
docs/phase-a-slice-plan.md   (modify)  mark A2a done
```

---

## Task 1: VM-core `IHost.WaitForInput` hook

**Files:** Modify `engine/Age.Engine/Hosting/IHost.cs`, `engine/Age.Engine/Hosting/CaptureHost.cs`, `engine/Age.Engine/Vm/VirtualMachine.cs`; Create `engine/Age.Engine.Tests/WaitForInputTests.cs`.

**Interfaces — Produces:** `IHost.WaitForInput()` called once per `wait-for-input` (0x72) instruction; `CaptureHost.WaitForInput()` no-op.

- [ ] **Step 1: Write the failing test** — `engine/Age.Engine.Tests/WaitForInputTests.cs`:

```csharp
using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class WaitForInputTests
{
    private sealed class CountHost : IHost
    {
        public int Waits;
        public List<int> Emitted = new();
        public void ShowText(int offset, string text) => Emitted.Add(offset);
        public void CallScript(long id) { }
        public void OnStub(int opcode) { }
        public void WaitForInput() => Waits++;
    }

    [Fact]
    public void WaitForInputFiresAndEmittedIsStable()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new CountHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Run();
        Assert.True(host.Waits > 0, "wait-for-input (0x72) should fire at least once");
        Assert.Equal(186, host.Emitted.Count);   // unchanged vs the A1 SC0000 trace
        Assert.Equal("exit", vm.HaltReason);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`IHost` has no `WaitForInput`)
```bash
dotnet test engine/Age.Engine.Tests --filter FullyQualifiedName~WaitForInputTests
```
Expected: build error — `CountHost` does not implement `IHost` (missing `WaitForInput`) — i.e. the interface lacks it.

- [ ] **Step 3: Implement**

`engine/Age.Engine/Hosting/IHost.cs` — add the method:
```csharp
namespace Age.Engine.Hosting;
public interface IHost
{
    void ShowText(int offset, string text);
    void CallScript(long id);
    void OnStub(int opcode);
    void WaitForInput();
}
```
`engine/Age.Engine/Hosting/CaptureHost.cs` — add the no-op (append after `OnStub`):
```csharp
    public void WaitForInput() { }
```
`engine/Age.Engine/Vm/VirtualMachine.cs` — split `wait-for-input` out of the no-op group. Replace:
```csharp
            case "end-text-line": case "wait-for-input": case "set-font":
            case "comment": case "display-furigana": case "dev_ukn":
                return pc + 1;
```
with:
```csharp
            case "wait-for-input": _host.WaitForInput(); return pc + 1;
            case "end-text-line": case "set-font":
            case "comment": case "display-furigana": case "dev_ukn":
                return pc + 1;
```

- [ ] **Step 4: Run — expect PASS (new test + full regression)**
```bash
dotnet test engine/AgeEngine.sln
```
Expected: 7 passed (the new test + the existing 6; RECOVER and TraceDiff unchanged because `CaptureHost.WaitForInput` is a no-op).

- [ ] **Step 5: Commit**
```bash
git add engine/Age.Engine/Hosting engine/Age.Engine/Vm/VirtualMachine.cs engine/Age.Engine.Tests/WaitForInputTests.cs
git commit -m "feat(a2a): add IHost.WaitForInput hook (wait-for-input 0x72); trace parity preserved"
```

---

## Task 2: Godot project scaffold + Age.Engine reference (headless smoke)

**Files:** Create `godot/project.godot`, `godot/Himegari.csproj`, `godot/Main.tscn`, `godot/Main.cs`.

**Interfaces — Produces:** a buildable Godot .NET project whose `Main._Ready` loads SC0000 via `Age.Engine` and prints a load summary, proving `Paths` + the engine work inside Godot.

- [ ] **Step 1: Create the project files**

`godot/project.godot`:
```
config_version=5

[application]
config/name="Himegari (age-reimpl A2a)"
run/main_scene="res://Main.tscn"

[dotnet]
project/assembly_name="Himegari"
```
`godot/Himegari.csproj`:
```xml
<Project Sdk="Godot.NET.Sdk/4.7.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\engine\Age.Engine\Age.Engine.csproj" />
  </ItemGroup>
</Project>
```
`godot/Main.tscn` (UI is built in code, so the scene is just the scripted root):
```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://Main.cs" id="1"]

[node name="Main" type="Control"]
layout_mode = 3
anchors_preset = 15
script = ExtResource("1")
```
`godot/Main.cs` (minimal smoke version — replaced in Task 3):
```csharp
using Godot;
using Age.Engine.Sys4;

public partial class Main : Godot.Control
{
    public override void _Ready()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        GD.Print($"SMOKE repo={Paths.Repo}");
        GD.Print($"SMOKE SC0000 instrs={script.Instructions.Count} strings={script.Strings.Count}");
        GetTree().Quit(0);
    }
}
```

- [ ] **Step 2: Import, build, run headless**
```bash
GODOT="S:/Godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64_console.exe"
"$GODOT" --headless --path godot --import
dotnet build godot/Himegari.csproj
"$GODOT" --headless --path godot 2>&1 | grep -E 'SMOKE|error|Exception'
```
Expected: `SMOKE repo=…\age-reimpl`, and `SMOKE SC0000 instrs=<n> strings=<m>` with n>0 (proves `Paths` resolves inside Godot and `Age.Engine` parses SC0000 in-process). If `Paths.Repo` is wrong or missing, `Paths.FindRepo` walks up from the assembly dir for `age-reimpl` — confirm the project is under `age-reimpl/godot/`.

- [ ] **Step 3: Commit**
```bash
git add godot/project.godot godot/Himegari.csproj godot/Main.tscn godot/Main.cs
git commit -m "feat(a2a): Godot 4.7 .NET project scaffold referencing Age.Engine (headless smoke)"
```

---

## Task 3: Interactive host + worker thread + headless self-test

**Files:** Create `godot/GodotAdvHost.cs`; replace `godot/Main.cs`.

**Interfaces — Consumes:** `IHost`, `VirtualMachine`, `Sys4Loader`, `OpcodeTableJson`, `Paths`. **Produces:** `GodotAdvHost` (blocking gate + capture + `CallDeferred` UI); `Main` (worker thread + selftest). Self-test gate: `--selftest` prints `SELFTEST OK` and exits 0 when the emitted offset sequence equals `build/vm0-trace.json["SC0000.BIN"]`.

- [ ] **Step 1: Create `godot/GodotAdvHost.cs`**
```csharp
using System.Collections.Generic;
using System.Threading;
using Age.Engine.Hosting;

public sealed class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly SemaphoreSlim _gate = new(0, 1);
    public volatile bool IsWaiting;
    public readonly List<(int Offset, string Text)> Captured = new();

    public GodotAdvHost(Main main) => _main = main;

    public void ShowText(int offset, string text)
    {
        Captured.Add((offset, text));
        _main.CallDeferred("AppendLine", text);
    }

    public void WaitForInput()
    {
        _main.CallDeferred("PageBreak");
        IsWaiting = true;
        _gate.Wait();
        IsWaiting = false;
        _main.CallDeferred("ClearPage");
    }

    // called from the main thread (click) or the selftest auto-clicker
    public void SignalInput() { if (_gate.CurrentCount == 0) _gate.Release(); }

    public void CallScript(long id) { }
    public void OnStub(int opcode) { }
}
```

- [ ] **Step 2: Replace `godot/Main.cs`**
```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Age.Engine.Sys4;
using Age.Engine.Vm;

public partial class Main : Godot.Control
{
    private Label _text = null!;
    private Label _status = null!;
    private VirtualMachine _vm = null!;
    private GodotAdvHost _host = null!;
    private volatile bool _done;
    private bool _ended;
    private bool _selftest;

    public override void _Ready()
    {
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _text.OffsetLeft = 40; _text.OffsetTop = 40; _text.OffsetRight = -40; _text.OffsetBottom = -80;
        AddChild(_text);
        _status = new Label();
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _status.OffsetLeft = 40; _status.OffsetTop = -60;
        AddChild(_status);

        _selftest = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--selftest") >= 0;

        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        _host = new GodotAdvHost(this);
        _vm = new VirtualMachine(script, table, _host);
        _ = Task.Run(() => { _vm.Run(); _done = true; });

        if (_selftest)
            _ = Task.Run(async () => { while (!_done) { if (_host.IsWaiting) _host.SignalInput(); await Task.Delay(1); } });
    }

    public override void _Process(double delta)
    {
        if (_done && !_ended)
        {
            _ended = true;
            ShowEnd();
            if (_selftest) RunSelfTest();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_selftest) return;
        if (e.IsActionPressed("ui_accept") ||
            (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left))
            _host.SignalInput();
    }

    public override void _ExitTree() { _host?.SignalInput(); }

    // ---- UI methods invoked on the main thread via CallDeferred ----
    public void AppendLine(string text) => _text.Text += text + "\n";
    public void PageBreak() => _status.Text = "▼ click / Enter";
    public void ClearPage() { _text.Text = ""; _status.Text = ""; }
    public void ShowEnd() => _status.Text = "— end —";

    private void RunSelfTest()
    {
        var expected = LoadExpected("SC0000.BIN");
        var actual = _host.Captured.ConvertAll(c => c.Offset);
        bool ok = expected != null && actual.Count == expected.Count;
        for (int i = 0; ok && i < actual.Count; i++) ok = actual[i] == expected![i];
        if (ok) GD.Print($"SELFTEST OK: {actual.Count} lines match vm0 trace");
        else GD.Print($"SELFTEST FAIL: cs={actual.Count} expected={(expected?.Count.ToString() ?? "n/a")}");
        GetTree().Quit(ok ? 0 : 1);
    }

    private static List<int>? LoadExpected(string scene)
    {
        string p = System.IO.Path.Combine(Paths.Build, "vm0-trace.json");
        if (!System.IO.File.Exists(p)) return null;
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(p));
        if (!doc.RootElement.TryGetProperty(scene, out var e)) return null;
        var list = new List<int>();
        foreach (var x in e.GetProperty("offsets").EnumerateArray()) list.Add(x.GetInt32());
        return list;
    }
}
```

- [ ] **Step 3: Build and run the self-test — expect FAIL first if anything is off, then PASS**
```bash
GODOT="S:/Godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64_console.exe"
dotnet build godot/Himegari.csproj
"$GODOT" --headless --path godot -- --selftest 2>&1 | grep -E 'SELFTEST|error|Exception'
echo "exit: ${PIPESTATUS[0]}"
```
Expected: `SELFTEST OK: 186 lines match vm0 trace`, exit 0. This exercises the worker thread, the real blocking `SemaphoreSlim` gate (driven by the auto-clicker), `CallDeferred` marshalling, and asserts the emitted text equals the trusted `vm0-trace.json`. If it hangs, the gate isn't being released (check `IsWaiting`/`SignalInput`); if the count differs, the VM/host emitted wrong text (compare with `py -3.11 -X utf8 tools/vm0.py --scene SC0000`).

- [ ] **Step 4: Regression — A1 engine tests still green**
```bash
dotnet test engine/AgeEngine.sln 2>&1 | grep -E 'Passed!|Failed!'
```
Expected: 7 passed.

- [ ] **Step 5: Commit**
```bash
git add godot/GodotAdvHost.cs godot/Main.cs
git commit -m "feat(a2a): worker-thread ADV host + blocking wait-for-input + headless self-test (SELFTEST OK)"
```

---

## Task 4: Manual visual verification + CJK font + docs

**Files:** Modify `godot/Main.cs` (best-effort CJK font); Modify `docs/phase-a-slice-plan.md`.

**Interfaces:** none (visual + docs).

- [ ] **Step 1: Add a best-effort CJK font so the manual visual isn't tofu** — in `Main._Ready`, after `AddChild(_status);`, insert:
```csharp
        foreach (var fp in new[] { "C:/Windows/Fonts/YuGothR.ttc", "C:/Windows/Fonts/msgothic.ttc", "C:/Windows/Fonts/meiryo.ttc" })
        {
            if (!System.IO.File.Exists(fp)) continue;
            try
            {
                var ff = new FontFile();
                ff.LoadDynamicFont(fp);
                _text.AddThemeFontOverride("font", ff);
                _status.AddThemeFontOverride("font", ff);
                _text.AddThemeFontSizeOverride("font_size", 22);
                break;
            }
            catch { /* fall back to default font */ }
        }
```
(If `FontFile.LoadDynamicFont` doesn't exist in 4.7, the editor will flag it at build; substitute `ResourceLoader.Load<FontFile>(fp)` — but the self-test does not depend on this, so it never blocks the gate.)

- [ ] **Step 2: Rebuild + self-test still green (font is headless-safe)**
```bash
GODOT="S:/Godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64_console.exe"
dotnet build godot/Himegari.csproj
"$GODOT" --headless --path godot -- --selftest 2>&1 | grep SELFTEST
```
Expected: `SELFTEST OK: 186 lines match vm0 trace`.

- [ ] **Step 3: Manual visual check (human)** — run windowed and click through:
```bash
"S:/Godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64.exe" --path godot
```
Confirm by eye: the opening narration appears page by page in Japanese, `▼ click / Enter` shows at each `wait-for-input`, a click/Enter advances, and it ends with `— end —`. (This is the one part only a human verifies.)

- [ ] **Step 4: Update `docs/phase-a-slice-plan.md`** — under the A2 section add:
```
### A2a — Interactive dialogue loop ✅ DONE (2026-07-06)
Godot 4.7 (.NET) project in `godot/` referencing `Age.Engine` in-process. VM gained one hook
(`IHost.WaitForInput`, 0x72); suspend/resume via a worker thread + blocking SemaphoreSlim in
`GodotAdvHost`, UI marshalled by `CallDeferred`. Plays SC0000 page-by-page, pauses at wait-for-input,
resumes on click. Headless self-test (`--selftest`) asserts the emitted 186-line sequence == vm0-trace;
A1 engine tests stay 7/7. Next: A2b (background/AGF, voice, choices, call-script/state).
Spec/plan: `docs/superpowers/{specs,plans}/2026-07-06-a2a-godot-dialogue*.md`.
```

- [ ] **Step 5: Commit**
```bash
git add godot/Main.cs docs/phase-a-slice-plan.md
git commit -m "feat(a2a): CJK font for the message window; mark A2a done in the slice plan"
```

---

## Self-Review

**Spec coverage:** Godot 4.7 .NET project + Age.Engine in-process (Task 2) ✓ · one VM-core change `IHost.WaitForInput`, CaptureHost no-op, trace parity (Task 1) ✓ · background-thread host + blocking `SemaphoreSlim` + `CallDeferred` marshalling (Task 3) ✓ · SC0000 page-by-page, pause at wait-for-input, resume on click (Tasks 3–4) ✓ · headless self-test asserting vs `vm0-trace.json` (Task 3) ✓ · A1 regression 7/7 (Tasks 1,3) ✓ · `Paths` resolves in Godot (Task 2) ✓ · cross-thread visibility via `volatile _done`, `Captured` read only after `_done` (Task 3) ✓ · `_ExitTree` releases the gate (Task 3) ✓ · CJK font for the manual visual (Task 4) ✓. Out-of-scope (bg/voice/choice/call-script) correctly absent.

**Placeholder scan:** none — every code step is complete. The only non-mechanical step is Task 4 Step 3 (human visual check), which is inherently manual and fully specified.

**Type consistency:** `IHost.WaitForInput()` (Task 1) is implemented by `CaptureHost` (Task 1), the test `CountHost` (Task 1), and `GodotAdvHost` (Task 3). `GodotAdvHost` calls `_main.CallDeferred("AppendLine"|"PageBreak"|"ClearPage")` — all `public` methods on `Main` (Task 3). `Main` uses `Paths.OpcodesJson`/`Paths.Scripts()`/`Paths.Build`/`Paths.Repo`, `OpcodeTableJson.Load`, `Sys4Loader.Load`, `VirtualMachine(script,table,host)` + `.Run()` — all matching the A1 engine signatures. `_host.Captured`/`IsWaiting`/`SignalInput()` used by `Main` match `GodotAdvHost`.

# Design: A2a — Interactive Dialogue Loop (Godot + Age.Engine)

Status: **approved (design)** · Date: 2026-07-06
Related: `docs/phase-a-slice-plan.md` (A2), `docs/remake-architecture-and-roadmap.md`, the A1 engine
(`engine/Age.Engine`), `docs/superpowers/specs/2026-07-06-a1-csharp-vm-design.md`.

## Problem / goal

A1 delivered a headless C# VM byte-identical to `vm0.py`. A2a is the first *presentation* slice: stand up
a **Godot 4.7 (.NET)** project that references `Age.Engine` in-process, make the VM **suspendable**, and
play one CLEAN scene (**SC0000**, 186 lines) as a message window whose text **pauses at `wait-for-input`
(0x72) and resumes on a click**. It proves the two load-bearing A2 unknowns — (1) Godot .NET can host and
run our engine in-process, and (2) the suspend/resume interactive loop — with the validated VM core left
essentially unchanged. Background art, voice, choices, and `call-script`/state are **A2b**.

## Non-goals (→ A2b / later)

Background/AGF pipeline, `play-voice`/`play-bgm`, choices (buttons → VM), `call-script`/state seeding to
enter richer scenes, `set-font`, remaining draw/audio effectful ops, exported-build data packaging, UX
polish (text speed, backlog, skip). A2a is the interactive VM↔Godot loop only.

## Prerequisites (verified 2026-07-06)

- Godot **4.7-stable mono** at `S:/Godot/Godot_v4.7-stable_mono_win64/` (`GodotSharp/` present;
  `--version` → `4.7.stable.mono.official`). Console exe used for headless runs.
- `build/opcodes.json` and `build/vm0-trace.json` exist (from A1); `../extracted/DATA1/SC0000.BIN` present.
- `Age.Engine` targets `net8.0` (Godot 4.7 .NET also targets .NET 8) → compatible.

## Architecture

```
godot/                              Godot 4.7 .NET project (config_version=5)
  project.godot                     main scene = Main.tscn; dotnet/project config
  Himegari.csproj                   Godot.NET.Sdk; net8.0; ProjectReference ../engine/Age.Engine
  Main.tscn                         Control(root) > RichTextLabel + Label(status)
  Main.cs                           owns the VM worker thread + UI methods
  GodotAdvHost.cs : Age.Engine.Hosting.IHost   interactive backend
```

- **In-process, no IPC:** the Godot game assembly references `engine/Age.Engine/Age.Engine.csproj`
  directly. Data is resolved by the existing `Age.Engine.Sys4.Paths` (walks up to `age-reimpl/`; the
  `godot/` project is inside it, so `Paths.OpcodesJson` / `Paths.Scripts()` work in the editor and in
  headless dev runs). *(Exported-build data packaging is A2b/C.)*

### One VM-core change (kept minimal to preserve trace parity)

Add to `IHost`: `void WaitForInput();`. In `VirtualMachine.Step`, split `wait-for-input` out of the
no-op group:
```csharp
case "wait-for-input": _host.WaitForInput(); return pc + 1;
```
`CaptureHost.WaitForInput()` is a **no-op**, so `Steps`/`Emitted` are unchanged and **A1's trace-diff and
RECOVER stay green** (calling an empty method changes nothing the trace observes). This is the entire core
change; all suspend/resume logic lives in `GodotAdvHost`.

### Threading model (background thread + blocking host)

`Main.cs` runs `vm.Run()` on a `Task` (worker thread). Ownership is clean: the worker owns VM state, the
main thread owns UI, and `WaitForInput` is the only rendezvous.

`GodotAdvHost` (constructed with the `Main` node and an `autoAdvance` flag):
- `ShowText(off, text)` → `main.CallDeferred(Main.MethodName.AppendLine, text)` (append on main thread);
  also record `(off, text)` into a `Captured` list (for the self-test).
- `WaitForInput()` → if `autoAdvance` return immediately; else `main.CallDeferred(Main.MethodName.PageBreak)`
  then `_gate.Wait()` (a `SemaphoreSlim(0,1)`); on resume, `main.CallDeferred(Main.MethodName.ClearPage)`.
- `SignalInput()` → `_gate.Release()` (called from the main thread on click).
- `CallScript`/`OnStub` → record/ignore (A2a stubs them like A1).

`Main.cs`:
- `_Ready()`: parse `OS.GetCmdlineUserArgs()` for `--selftest`; load `OpcodeTable` + SC0000 `Script` via
  `Paths`; build `GodotAdvHost(this, autoAdvance: selftest)`; start `_task = Task.Run(() => { _vm.Run(); _done = true; })`.
- `_UnhandledInput(e)`: on `ui_accept` or a left mouse click, `_host.SignalInput()`.
- UI methods (called via `CallDeferred`): `AppendLine(string)` appends to the `RichTextLabel`;
  `PageBreak()` shows a "▼ click" indicator; `ClearPage()` clears the label; `ShowEnd()` shows "— end —".
- `_Process()`: when `_done` flips true, call `ShowEnd()` once; in **selftest** mode, compare
  `_host.Captured` offsets to the expected SC0000 sequence and `GetTree().Quit(exitCode)`.

## Data flow (one page)

worker `vm.Run()` → N× `ShowText` (each `CallDeferred(AppendLine)`) → `wait-for-input` →
`WaitForInput()` shows ▼ and blocks the worker → user clicks → `_UnhandledInput` → `SignalInput()`
releases → worker clears the page and continues → … → `exit` → `_done` → `ShowEnd()`.

## Validation

- **Headless self-test (auto-verifiable, the A2a gate):** run
  `Godot_v4.7-stable_mono_win64_console.exe --headless --path godot -- --selftest`. `Main` runs SC0000 on
  the worker thread with `autoAdvance` (WaitForInput returns immediately), still marshalling `AppendLine`
  via `CallDeferred` (so the thread + deferred path is exercised headlessly), waits for `_done`, then
  asserts `_host.Captured` offsets equal `build/vm0-trace.json["SC0000.BIN"]["offsets"]` (186 lines) and
  quits `0`/`1`, printing `SELFTEST OK`/`SELFTEST FAIL: …`. This proves Godot .NET hosts Age.Engine, the
  worker-thread + `CallDeferred` marshalling works, and the emitted text matches the trusted oracle.
- **A1 regression:** `dotnet test engine/AgeEngine.sln` → still 6/6 (WaitForInput no-op in CaptureHost).
- **Manual visual (human):** open `godot/` in the Godot 4.7 .NET editor (or run non-headless) and click
  through SC0000 — text appears page by page, pauses at ▼, advances on click, ends cleanly. The
  suspend/resume timing under a real window is the part only a human can confirm.

## Risks

- **Godot .NET project scaffolding by hand** — `project.godot` + `.csproj` must be correct for
  `godot --headless` to build the C# assembly. Mitigation: generate via the Godot editor's C# setup if the
  hand-authored files fail to build; the plan verifies with a headless build before wiring logic.
- **`CallDeferred` method binding** — deferred calls target `Main` methods by name; they must be
  `public` (or `[Signal]`/source-gen `MethodName`) on the `Node`. Verified by the self-test.
- **Headless drivers** — use `--headless` (no audio/video driver needed); the self-test avoids real input.
- **Thread vs Godot lifetime** — if the scene exits while the worker blocks in `WaitForInput`, release the
  gate in `_ExitTree()` and guard `CallDeferred` after tree exit. Covered in the plan.
- **Cross-thread visibility** — `_done` is `volatile` (worker writes, `_Process` reads); `_host.Captured`
  is only read after `_done` is observed true, so the completed write is safely published. No lock needed
  beyond that ordering.
- **`Paths` in an exported build** — dev-only for A2a (resolves via the repo tree); packaging is deferred.

## Out of scope (A2b and beyond)

Background via `AGF2BMP2AGF.exe` → texture, `play-voice`/`play-bgm` (OGG), choices → UI buttons feeding VM
globals, `call-script`/state seeding to unlock richer/EMPTY scenes, `set-font`, remaining draw ops,
export packaging, and ADV UX (text speed, backlog, auto/skip).

# Design: Phase B — Cross-Scene State (persistent globals + a state-carrying runner)

Status: **approved-by-delegation (owner asleep; autonomous Phase B mandate)** · Date: 2026-07-07
Related: `docs/remake-architecture-and-roadmap.md` (Phase B), `docs/phase-a-slice-plan.md`
(A2b-Geometry — the state-divergence finding that motivates this), `engine/Age.Engine`
(`Vm/VirtualMachine.cs`), `engine/Age.Cli/Program.cs`.

## Problem / goal

Every VM run today is a **single scene in isolation** with an empty global bank. But the session's
biggest finding is that most divergence from the real game — the bg/sprite geometry drift, the 12
state-gated EMPTY scenes, Lily's form-gated voices — is **state divergence**: the real game carries a
persistent global store across scenes (boot init → scene → scene …), so scenes take branches our
fresh-state VM never reaches.

The foundational Frida-free step is therefore **cross-scene state**: a persistent global bank that
survives scene boundaries, seedable up front, so we can (a) run a boot script then a scene with its
state, (b) seed known flags (chapter, form) and observe scenes branch correctly, and (c) later drive a
real multi-scene sequence. This is pure bytecode execution — no Frida, no packer, no unsolved
call-script registry (the scene *sequence* is supplied; auto-chaining via SCJUMP is a later slice).

**Goal:** a reusable `GameSession` in `Age.Engine` that owns a persistent global store and runs scenes
into it, plus an `Age.Cli play` mode that runs a supplied scene sequence carrying state (with optional
seeds) and dumps the combined dialogue. Headless, deterministic, unit-tested.

## Scope

**In:**
- `Age.Engine/Vm/GameSession.cs` — persistent `Globals`/`GlobalStrings`; `RunScene(script, host)` seeds
  a fresh `VirtualMachine` from session state, runs it, and merges the final state back. Returns a small
  result (emitted lines, halt reason, steps).
- Seeding API: `Seed(int addr, long value)` / `SeedString(int addr, string)`.
- `Age.Cli play <SCENE...> [0xADDR=VAL ...]` — run a sequence carrying state; print per-scene dialogue
  counts + halt, and the combined emitted line count. Reuses `CaptureHost`.
- Unit tests: state persists A→B; SC0000 via `GameSession` is byte-identical to a single run (186 lines);
  a seeded global is visible to the scene and changes an observable (branch/emit).

**Out (later slices):** SCJUMP-driven auto-sequencing, `call-script` id→code resolution, the input/
hotspot model (interactive/EMPTY scenes), save-file (de)serialization of the store, Godot wiring of
`GameSession`. This slice is the state *substrate*; those consume it.

## Architecture

### `Age.Engine/Vm/GameSession.cs` (new)
```csharp
public sealed class GameSession
{
    public Dictionary<int, long>    Globals       { get; } = new();
    public Dictionary<int, string>  GlobalStrings { get; } = new();

    public void Seed(int addr, long value)        => Globals[addr] = value;
    public void SeedString(int addr, string value)=> GlobalStrings[addr] = value;

    public SceneResult RunScene(Script script, OpcodeTable table, IHost host, VmOptions? o = null)
    {
        var vm = new VirtualMachine(script, table, host, o);
        foreach (var kv in Globals)       vm.Globals[kv.Key] = kv.Value;
        foreach (var kv in GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;
        vm.Run();
        // merge final state back (globals are one flat space; last write wins — matches the engine)
        foreach (var kv in vm.Globals)       Globals[kv.Key] = kv.Value;
        foreach (var kv in vm.GlobalStrings) GlobalStrings[kv.Key] = kv.Value;
        return new SceneResult(vm.Emitted.ToList(), vm.HaltReason, vm.Steps);
    }
}
public sealed record SceneResult(IReadOnlyList<(int Offset, string Text)> Emitted, string? Halt, long Steps);
```
Seam rule preserved: `GameSession` lives in `Vm`, references only `Model`/`Hosting` (never `Sys4`). The
VM itself is unchanged (its `Globals`/`GlobalStrings` are already public and pre-seedable — the `audio`/
`gfx` CLI modes already write them before `Run`), so **trace/selftest parity is untouched**.

### `Age.Cli play` (Program.cs)
`play SC0000.BIN SC0030.BIN 0xa57=1` → build a `GameSession`, apply seeds, `RunScene` each in order
through a `CaptureHost`, print `scene: N lines (halt …)` per scene and the carried global count. Mirrors
the existing `audio`/`gfx` seed-arg parsing.

## Data flow
```
seeds ──► GameSession.Globals ──►┐
                                 ├─ RunScene(A): VM seeded from session → runs → merge back ─► session grows
scene A ──────────────────────►─┘
scene B ──► RunScene(B): VM seeded from session (now includes A's writes) → runs → merge back
```

## Testing / verification
1. **Persistence (synthetic):** script A = `mov (global-int 0x5000) 0x2a; exit`; script B = `show-text`
   guarded by `jcc (global-int 0x5000)`… — simplest: A sets `G[0x5000]=42`, then assert
   `session.Globals[0x5000]==42` after `RunScene(A)`, and that `RunScene(B)` sees it. Build scripts
   in-memory (as `TextureGeometryTests` does) so it's deterministic and needs no corpus.
2. **Parity:** `GameSession.RunScene(SC0000)` emits exactly the 186 offsets of the single-run trace
   (`build/vm0-trace.json`) — proves the session wrapper doesn't perturb execution.
3. **Seeding changes behavior:** seed `G[0xa57]=1` (Lily form A) before SC0000 and assert the emitted
   set differs from the unseeded run (the form-gated lines now execute) — the headless analogue of the
   audio finding, and the first concrete demonstration that state seeding works end-to-end.
4. **CLI smoke:** `Age.Cli play SC0000.BIN` runs and prints a sane summary; engine suite stays green.

## Risks / open questions
- **Merge semantics:** globals are one flat space; merging the VM's full final map back into the session
  (last-write-wins) matches the engine's single global bank. Local frames are per-call and correctly do
  NOT persist (they live in the VM, not the session). ✓
- **Which globals are "state" vs scratch:** we carry *all* of them (the engine does). Transient scratch
  registers (e.g. `G[0x62424]`) get overwritten next use — harmless. No filtering needed.
- **Scene sequence source:** supplied by the caller here; SCJUMP-driven sequencing is a later slice and
  does not block this substrate.
- **No divergence risk to A1/A2:** the VM is untouched; parity test (2) is the guardrail.

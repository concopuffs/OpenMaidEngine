# Design: A1 — C# VM Core (headless, differential-tested against vm0.py)

Status: **approved (design)** · Date: 2026-07-06
Related: `docs/phase-a-slice-plan.md` (A1), `docs/remake-architecture-and-roadmap.md`, `tools/vm0.py`,
`tools/sys4load.py`, `vm-map/opcodes.toml` → `build/opcodes.json`.

## Problem / goal

A0 validated the AGE/SYS4 execution model in Python (`vm0.py`): RECOVER unit test passes, and 282/294
ADV scenes emit a clean in-order subsequence of `dialogue.jsonl` with zero garbage. A1 ports that
*validated* model to **C#** — the runtime language chosen for the hot 1.5M-instruction fetch/execute
loop (GDScript is too slow; see roadmap). A1 is **headless**: it reproduces A0's behavior exactly,
proven by a per-scene differential trace against `vm0.py`. No rendering, input, audio, or Godot — those
are A2.

## Goals

1. A C# VM that reproduces `vm0.py` **exactly** on every SC/SP scene (per-scene trace-identical) and
   passes the RECOVER pointer/array/control-flow unit test.
2. Establish the roadmap's seams — **VM core** (version/game-agnostic) / **SYS4 front-end**
   (parser + codec + opcode table) / **backend (`IHost`)** — with a **version-neutral `Script`
   contract** as an explicit constraint, so retooling to a plugin architecture later is additive.
3. A standalone `.NET 8` solution that builds and tests without Godot.

## Non-goals (explicitly deferred to A2 / Phase B)

Rendering, message window, input, audio, real `call-script` resolution, Godot integration, per-scene
state seeding / unlocking the 12 EMPTY scenes, a version-front-end plugin system or manifest loader,
and embedding `opcodes.json` for a shipped build. Also not porting the non-VM Python tools
(`extract_*`, `global_map`, `opcodes_build`) — those stay in Python (toolchain vs runtime).

## Architecture

New `.NET 8` solution under `engine/` (`engine/AgeEngine.sln`). Three projects:

```
engine/
  AgeEngine.sln
  Age.Engine/                     class library — the reusable engine
    Model/                        version-NEUTRAL contract (the load-bearing seam)
    Vm/                           execution core (depends ONLY on Model + Hosting)
    Sys4/                         SYS4 front-end (the ONLY SYS4/encoding-specific code)
    Hosting/                      IHost seam + CaptureHost (headless)
  Age.Cli/                        console runner: recover | run <f> | sweep | trace <out.json>
  Age.Engine.Tests/               xUnit: RECOVER, parser, trace-diff
```

**Seam rule (enforced, reviewed):** `Age.Engine.Vm` references only `Age.Engine.Model` and
`Age.Engine.Hosting`. It must not reference `Age.Engine.Sys4` or do file I/O. This keeps the core
version-neutral; extracting `IVersionFrontend` later = naming the interface `Sys4Loader` already
satisfies + adding a manifest loader (pure addition, no core edits).

### `Age.Engine.Model` — the neutral contract

```csharp
public readonly record struct Operand(int Type, long Value);           // type tags match vm0 (0=imm,2=str,3=g-int,9=l-int,…)
public sealed record Instruction(int Offset, int Opcode, IReadOnlyList<Operand> Args);
public sealed record ScriptHeader(int LocalInt1, int LocalFloats, int LocalStrings1,
                                  int LocalInt2, int Unknown, int LocalStrings2);  // F0–F5
public sealed class Script {
    public required ScriptHeader Header { get; init; }
    public required IReadOnlyList<Instruction> Instructions { get; init; }
    public required IReadOnlyDictionary<int,int> IndexByOffset { get; init; }     // dword offset -> instruction index
    public required IReadOnlyDictionary<int,string> Strings { get; init; }        // resolved type-2 strings (offset -> decoded)
    public string GetString(int offset) => Strings.TryGetValue(offset, out var s) ? s : "";
}
public sealed class OpcodeTable {                                                 // from build/opcodes.json
    public bool TryGet(int opcode, out string label, out int argc);
    public string Label(int opcode);                                             // dispatch key (Kelebek label)
    public int Argc(int opcode);                                                 // decode width
}
```

The VM consumes `Script` + `OpcodeTable` + `IHost`. It never sees a byte, a file path, or the codec.

### `Age.Engine.Vm` — faithful port of `vm0.py`

Stateful `VirtualMachine` mirroring vm0's `VM` (so RECOVER can pre-seed and inspect state):

```csharp
public sealed class VirtualMachine {
    public Dictionary<int,long> Globals { get; } = new();                        // flat global-int bank
    public Dictionary<int,string> GlobalStrings { get; } = new();
    public List<(int Offset, string Text)> Emitted { get; } = new();             // captured show-text
    public string? HaltReason { get; private set; }
    public long Steps { get; private set; }
    public VirtualMachine(Script script, OpcodeTable table, IHost host, VmOptions? options = null);
    public void Run(int entryOffset = 0);
}
public sealed record VmOptions(int EmitCap = 2, long MaxSteps = 2_000_000);
```

Ported element-for-element from `vm0.py` (same names, same order):
- **Operand resolution** with pointer/lvalue semantics: `Read`, `Write`, `BaseAddr`, `LookupStore`
  (ptr dst takes a *reference* = the global address; reading a ptr dereferences; write-through-ptr).
- **Handlers:** ALU (`add sub mul div mod and or sar shl`), compares (`eq ne lt lte gr gre`), `mov`,
  `set-string`, `lookup-array`, `lookup-array-2d`, `bit-set`, `bit-reset`, `check-bit`,
  `copy-to-global`; control `jmp`, `call`, `ret`, `jcc` (`0xFFFFFFFF` = fallthrough),
  `exit`/`exit-script`; `call-script` → `host.CallScript(id)` (stub); `show-text` → capture via host
  with the **emit-cap loop-guard** (halt `LOOP:line@…` on the `(EmitCap+1)`-th emit of an offset);
  `end-text-line`/`wait-for-input`/`set-font`/`comment`/`display-furigana`/`dev_ukn` → no-op; anything
  else → `host.OnStub(opcode)` + fall through. Halt reasons: `exit`, `LOOP:…`, `STEP-LIMIT`,
  `ret-underflow`, `pc-out-of-range` (identical strings to vm0).
- **Dispatch** on `table.Label(opcode)` (the Kelebek label) — the same switch `vm0.step()` does.

**Integer-semantics parity (critical):** vm0 uses Python ints. Two ops differ from C# defaults and MUST
be implemented to match Python, or the trace will diverge on negative operands:
- `div` → **floor division** (C# `/` truncates toward zero; Python `//` floors). Pure-integer helper
  (no `double` — precision-safe for full `long` range):
  `PyDiv(a,b){ if(b==0) return 0; long q=a/b, r=a%b; if(r!=0 && (r<0)!=(b<0)) q--; return q; }`
- `mod` → result takes the **divisor's sign** (Python `%`; C# `%` takes the dividend's sign):
  `PyMod(a,b){ if(b==0) return 0; long r=a%b; if(r!=0 && (r<0)!=(b<0)) r+=b; return r; }`
- Use `long` for all values (vm0's ints are unbounded; `long` matches for the magnitudes these scenes
  reach). `sar`/`shl` use `a >> (b & 31)` / `a << (b & 31)` (arithmetic shift on signed `long`, matching
  Python on the tested range). The trace-diff is the backstop that proves these choices.

### `Age.Engine.Sys4` — the front-end (only SYS4-specific code)

- `Sys4Loader.Load(string path) / Parse(byte[])` → neutral `Script`. Ports `sys4load`'s container parse:
  header (magic `SYS44xx`, 13×u32, F0–F12 = var-bank counts), sections (code `[0..F8)`, T1/T2/T3), the
  `len = 1 + 2*argc` code walk with the **`data_array_end` shrink** (stop code at the lowest referenced
  string/array offset), `IndexByOffset`, and resolve every type-2 operand into `Strings` via the codec.
- `Sys4StringCodec.Decode(body, dwordOffset)` → XOR-`0xFF` then cp932. Register
  `System.Text.Encoding.CodePages` (`CodePagesEncodingProvider.Instance`) for code page 932.
- Opcode table via `OpcodeTableJson.Load(Paths.OpcodesJson)` reading `build/opcodes.json` (opcode →
  `label`, `argc`). Argc drives the decode walk; label drives VM dispatch.
- `Paths` — a small resolver mirroring `tools/paths.py`: from the assembly location walk up to the
  `age-reimpl/` root, expose `Data1`, `Extracted`, `Build`, `OpcodesJson`, and the game dir.

### `Age.Engine.Hosting` — the backend seam

```csharp
public interface IHost {
    void ShowText(int offset, string text);   // A1: capture. A2: message window.
    void CallScript(long id);                  // A1: stub (log). Later: dispatch.
    void OnStub(int opcode);                    // effectful draw/audio/input/unknown — A1: log/no-op.
}
public sealed class CaptureHost : IHost { /* records ShowText into a list; counts stubs; no-ops rest */ }
```

A2 = implement `IHost` for Godot. The VM is unchanged.

## Data flow

`Sys4Loader.Load(.BIN)` → `Script` ; `OpcodeTableJson.Load` → `OpcodeTable` ;
`new VirtualMachine(script, table, captureHost).Run()` → `Emitted` + `HaltReason` + `Globals`. The CLI
sweeps every SC/SP scene and, for `trace`, writes `{scene: {offsets, halt, steps}}`.

## Validation — per-scene differential trace

Shared trace format (both sides emit exactly this):
```json
{ "SC0000.BIN": { "offsets": [2457, 2464, …], "halt": "exit", "steps": 12345 }, … }
```
- **Python side:** add `tools/vm0.py --trace <out.json>` — iterate the same SC/SP set as `--sweep`,
  writing each scene's emitted string-offset list + halt reason + steps. (Reuses `run_scene`;
  `offsets = [off for off,_ in vm.text]`.)
- **C# side:** `Age.Cli trace <out.json>` produces the identical structure over the same scenes.
- **Diff:** load both; assert per-scene equality of `offsets` (ordered) + `halt` + `steps`; on mismatch
  report the scene and the first differing index. Trace-identical across all 294 = behavioral parity.

xUnit tests:
- `RecoverTests` — port `vm0.run_test` (seed the RECOVER globals, run, assert the 7 checks).
- `Sys4LoaderTests` — parse `MENU.BIN` + `SC0030.BIN`: assert header fields, section bounds, instruction
  count, and that decoded `Strings` for a sample match `sys4load`'s output (**codec parity** — the
  offset-only trace can't catch a decode bug, so this test guards it).
- `TraceDiffTests` — read `build/vm0-trace.json` (skip with a clear message if absent), run the C# sweep,
  assert per-scene equality. This is the headline A1 gate.

**A1 done when:** RECOVER passes in C#; the C# trace is byte-identical to `vm0.py --trace` on all 294
scenes (so 282/294 dialogue-valid falls out); parser + codec-parity tests green.

## Opcode data

The engine reads `build/opcodes.json` (generated by `opcodes_build.py --build` from the canonical
`vm-map/opcodes.toml`) via `Paths.OpcodesJson`. It is a runtime data input, like the `.BIN` scripts —
the solution *builds* without Python; *running* requires `--build` to have produced the JSON.
Embedding it as an assembly resource for a shipped engine is deferred (A2/C).

## Risks

- **Integer semantics** (div/mod/shift sign behavior) — addressed above with `PyDiv`/`PyMod`; the
  trace-diff is the definitive check.
- **Codec parity not covered by the offset-only trace** — covered by `Sys4LoaderTests` string-equality
  against `sys4load`.
- **`data_array_end` shrink subtlety** — the trickiest parser detail; port carefully and validate via
  `Sys4LoaderTests` instruction counts against `sys4load --summary`.
- **Toolchain/runtime drift** — the C# parser and `sys4load` must agree; the parser tests + trace-diff
  pin them together. If they diverge later, the format spec in `docs/sys4-format-notes.md` is the
  arbiter.
- **`build/opcodes.json` prerequisite** — CLI/tests fail fast with a clear message if it's missing
  (run `opcodes_build.py --build`).

## Out of scope (future)

Godot backend + real effectful ops (A2), input + state seeding to unlock EMPTY scenes (A2/B),
`call-script` registry (deferred), version-front-end plugin + manifest (Phase C), shipped-engine
resource embedding.

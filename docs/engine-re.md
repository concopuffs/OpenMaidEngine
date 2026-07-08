# Native-engine reverse engineering (Ghidra + MCP)

Static RE of the **unpacked** `AGE.EXE` engine image, driving Ghidra 12.1.2 via the
bethington/ghidra-mcp bridge. This is the home for decompiled native-op findings — the class of logic
the scripts call but that lives compiled in the engine (decision→scene, call-script dispatch, op 0x60,
the gfx command-buffer). Opcode semantics recovered here also flow into `vm-map/opcodes.toml`.

Related: `docs/scjump-progression.md` (the SCJUMP decoder that hit this wall), `name-resolution.md §1`
(call-script), `vm-mapping-plan.md` appendix (why the exe is packed + the runtime-dump route).

---

## Runbook — the Ghidra + MCP loop

**One-time setup (done 2026-07-07):**
- **MCP server:** bethington/ghidra-mcp, cloned to `S:\Game Hacking\ghidra-mcp`. We used the **prebuilt
  extension** `GhidraMCP-5.14.2.zip` (installed in Ghidra via File > Install Extensions) — this skips
  the Maven/Java-21 build. The Python **bridge** runs from a venv (`.venv`, Python 3.11, `pip install .`);
  no `uv` needed. Registered in Claude Code via `.mcp.json` at the workspace root:
  `{"mcpServers":{"ghidra":{"command":"S:\\Game Hacking\\ghidra-mcp\\.venv\\Scripts\\bridge-mcp-ghidra.exe","args":["--transport","stdio"]}}}`.
- **In Ghidra:** enable the GhidraMCP plugin (File > Configure) and **Tools > GhidraMCP > Start MCP
  Server** (serves `http://127.0.0.1:8089/`). The bridge talks to that; Claude reaches the bridge over stdio.

**Loading the engine image (IMPORTANT — the language gotcha):**
- Import `age-reimpl/build/engine-dump/range_00400000.bin` (the module dump: 2,490,368 bytes, the full
  0x400000 module image; VA→file offset = `VA − 0x400000`).
- **Format = Raw Binary, Language = `x86:LE:32:default`, Image Base = `0x400000`.** Ghidra's language
  picker offers `x86:LE:32:System Management Mode` as the "closest" match — **do NOT use it.** SMM is a
  16-bit *segmented* (segment:offset) variant for BIOS/SMRAM; it mis-decodes flat 32-bit code (it loaded
  with addresses like `0000:0000`/`0025:ffff` and produced **0 functions**). The plain `default` variant
  is correct and yielded **2,721 functions**.
- We drove the (re)import over MCP: `import_file(language="x86:LE:32:default", compiler_spec="windows",
  auto_analyze=false)` → `set_image_base(0x400000)` **before** analysis (so absolute-address refs resolve)
  → `run_analysis`.
- **Load sanity check (AGF-decoder landmark):** at VA `0x474f23`, `CMP word ptr [ESI + 0x4], 0x4d42`
  (the `BM`/BMP-magic check) confirms the image is correctly based + decoded.
- Escalation (unused so far): `bin/pe-sieve32.exe /pid <PID> /imp 3 /dmode 3 /dir <out>` (run from
  **PowerShell**, not Git Bash — it mangles `/flags`) rebuilds the IAT into a clean PE. Only needed if
  raw-dump analysis is inadequate; it was fine for reading logic, so we stayed on the raw dump.

---

## Master key — the opcode→handler dispatch table (2026-07-07, anchored)

The interpreter dispatches each op via a per-context handler table, **fully anchored**:

> **`handler(op) = ctx[0x26c93 + op]`**  (word index) **= `*(ctx + 0x9b24c + op*4)`**
> — `ctx` = the engine context (`esi` in handlers, thiscall; `param_1` in the decompile of the
> registration routine).

The registration routine **`FUN_00413860`** first fills `0x400` (1024) slots starting at
`ctx[0x26c93]` with a **default handler `FUN_004162b0`** (op 0's slot), then overrides specific
opcodes: `ctx[0x26c93 + op] = <handler_va>`. So **opcode = (word_index − 0x26c93)**. Cross-check:
`ctx[0x26e3f] = 0x427fb0` (byte offset `0x9b8fc`) → op `0x26e3f − 0x26c93 = 0x1ac`.

**Why this matters:** the Kelebek `u00XXXXXX` opcode names encode handler VAs from *Kelebek's* build,
which **drift** in ours. This table resolves the *real* handler for any opcode in our image — the
general fix for VA drift project-wide. To find op `N`'s handler: read `ctx[0x26c93 + N]` from the
`FUN_00413860` decompile (or `*(ctx + 0x9b24c + N*4)` at runtime).

**Other confirmed engine-context offsets** (`ctx`/`esi`): `+0x53d14` = current gfx-object index;
`+0x53d88` = per-object cmd-type table (stride `0x78` = 120 bytes); operand-fetch helper = `call
0x41b940` (thiscall, `ecx=ctx`, arg = operand index → returns the operand value); `FUN_00415f30(i)` =
a companion operand accessor.

---

## Findings

### op `0x1a2` (`u00428010`) is a GRAPHICS command-buffer op — NOT save, NOT decision→scene (2026-07-07)

The SCJUMP slice assumed `u00428010` resolved a decision value to a scene. **That premise is wrong**,
and pinning the *real* handler via the dispatch table above corrects two layers of confusion:

- **VA-drift trap:** Kelebek's `u00428010` = op `0x1a2`. But Kelebek's raw VA `0x428010`, in *our*
  build, sits inside a *different* handler `0x427fb0`, which is **op `0x1ac`** (per the table:
  `ctx[0x26e3f]=0x427fb0`). Op `0x1ac` is a **save-path** op — its handler formats
  `%s\SAVE%2.2d.DAT` (format string `0x571e70`) and is multi-operand. Reading the raw VA gave the
  wrong opcode.
- **Op `0x1a2`'s real handler = `FUN_0042d360`** (`= ctx[0x26c93+0x1a2] = ctx[0x26e35]`), argc 1. It:
  sets the **current gfx-object cmd-type to 3** (`*(ctx+0x53d88 + ctx[0x53d14]*0x78) = 3`), fetches
  operand 1, formats a key with `"%c%8.8x"` (format string `0x5714e0`) of `(3, operand)`, and calls
  `FUN_0042cf70(key, &operand)`. This is a **graphics command-buffer registration op**, not save and
  not scene-load.
- **Consequence — the decision→scene premise is discredited.** The FIELD snippet
  `lookup(0x5f0ed, 0x62ccf); mov(ptr,1); lookup(0x5f0ed, 0x62ccf); u00428010(ptr)` (next op `0x21b`,
  also gfx-family) is a **graphics/UI operation**, not scene sequencing. So `u00428010` does **not**
  resolve decision→scene. **The real decision→scene mechanism is unidentified** — it belongs with the
  call-script / script-load dispatch (`name-resolution.md §1`), the next target for this loop (now
  armed with the dispatch table to resolve the call-script handler directly).

**Lesson:** never analyze a native op by its Kelebek `u00XXXXXX` VA directly — always resolve the real
handler through the dispatch table (`ctx[0x26c93 + op]`). The raw VA is off by whole functions.

---

### op `0x03` (`call-script`) is a raw index into the SYS4INI file table — SOLVED (2026-07-07)

The long-deferred `call-script <id>` registry (`name-resolution.md §1`) is cracked. Resolved through
the dispatch table (op `0x03` → `ctx[0x26c93+3]` = **`FUN_0041bc90`**), then the loader/resolver chain:

- **`FUN_0041bc90`** (handler): fetches operand 1 (the id), bounds-checks call depth (≤ 0x26), pushes
  a script frame, and calls the loader.
- **`FUN_0040e980`** (loader): opens the resource by id, reads the **0x20-byte SYS4 header**, checks
  magic, allocates per-frame code/local buffers from the header var-counts, reads the bytecode body,
  and pushes a script frame (**stride 0x1e = 30 dwords**, indexed by `ctx[0x14f45]`). Returns to the
  caller when the callee ends.
- **`FUN_0044f390`** (resolver — the key): `record = [ctx+0x414] + id*0x50`. The record is exactly the
  **SYS4INI 80-byte layout** `{name[64], arc_id@0x40, file_number@0x44, offset@0x48, size@0x4c}`
  (count = `[ctx+0x40c]`, archive-name table = `[ctx+0x410]`). It tries a **loose override first**
  (`CreateFileA` on `record.name` → the mod/patch hook point), else opens archive
  `[record.arc_id*0x100 + ctx+0x410]`, `SetFilePointer` to `record.offset`, size = `record.size`.
  High-byte-tagged ids (`id & 0xff000000`) select an alternate pack via `[ctx+0x3028]` — **unused by
  the corpus** (0/297 ids carry a high byte).

**So `call-script <id>` = a direct RAW index into the SYS4INI global file table** — the same table
`parse_sys4ini.py` reads, but indexed *without* skipping `@` placeholders (13208 records, 2
placeholders). There is **no separate on-disk id→code registry**; SYS4INI *is* the registry, and we
already had it. **Statically confirmed:** all **297/297** distinct corpus `call-script` ids resolve to
a `.BIN` script with a semantically-exact name (`0x1ab→ADDITEM`, `0x2ae7→MES`, `0x143→BUNKI`,
`0x329d→CALCREVISE`, `0x2add→CALCBTPARAM`), 0 out-of-range, 0 pack-branch. Tooling:
`parse_sys4ini.py` emits `build/callscript-names.json` (id→name); `sys4load` annotates
`call-script 0x1ab =ADDITEM.BIN`; the whole `build/disasm/*.asm` call graph now reads by name. See
`name-resolution.md §1`.

**Companion — op `0x8f` (`call`) is INTRA-script, not cross-script.** Its handler **`FUN_0041fba0`**
sets `[frame PC @+0x53d2c] = [frame codebase @+0x53d28] + operand*4` and pushes a return address on
the per-frame return stack (`[ctx+0x552e8]`/`[ctx+0x55248]`). The operand is a **code offset within
the current script** (matches header table **T3, tag 0x8F** = local call targets). So `0x8f` is a
local JSR; only `0x03` loads another script.

**Follow-up (functional):** the C# VM still *stubs* `call-script`. With the id→resource mapping now
known, it can be implemented for real (load the target `.BIN` from the archive via the SYS4INI record,
push a frame, run, return) — the unlock for subroutine-using scripts and, via the same path,
decision→scene (scenes are just `SCxxxx.BIN` records loaded by their SYS4INI index).

---

### op `0x215` (`query-gfx-object?`) is a native command-buffer op — settles the render drift as (b) (2026-07-07)

**This is the canonical account of the background/sprite "drift" bug** (background pinned off-centre /
bottom-right, rest grey — `Screenshot 2026-07-06 211353.png`). It supersedes the earlier "drift =
state-divergence, seed state and it's fixed" conclusion in `docs/phase-a-slice-plan.md` and the status
memory, which are corrected to point here.

Resolved via the dispatch table (`ctx[0x26c93 + 0x215]`): the registration routine `FUN_00413860` stores
`[ESI + 0x9baa0] = 0x42a0b0`, so op `0x215`'s **real handler is `FUN_0042a0b0`**. (Kelebek's `0x421160` is
VA-drift — it lands inside the unrelated `FUN_00421090`. Same lesson as `0x1a2`: never trust a Kelebek raw VA.)

`FUN_0042a0b0(ctx)` does exactly two things:
1. **`*(ctx + 0x53d88 + ctx[0x53d14]*0x78) = 5`** — writes **cmd-type 5** into the *current* gfx-object
   record. A **command-buffer registration** side-effect, directly parallel to op `0x1a2` (`FUN_0042d360`)
   writing cmd-type 3. So `0x215` is part of the gfx command-buffer subsystem, not a pure query.
2. **`out = FUN_0047f280(FUN_0041b940(2))`** — `FUN_0041b940(2)` fetches operand 2 (the bytecode handle
   key); `FUN_0047f280` is a **`std::map::find`** over an engine-internal associative registry, returning
   the mapped value or **`0xffffffff` (not-found)**; `FUN_00425fb0(1, out)` writes it to operand 1. That
   registry is **populated by sibling gfx ops** — op `0x1a2`'s handler builds a `"%c%8.8x"` key and calls
   `FUN_0042cf70`, an open-addressing hash **insert** into the same kind of store.

**(a) vs (b) — the verdict is (b).** The value `0x215` returns is **native command-buffer state**: "has a
gfx object already been registered under this handle?" (`≥0` = existing → use its slot; `-1` = new). That
state lives in the engine's own registry, maintained by the gfx ops, **not in the VM global bank**. So
**seeding story-state globals cannot reproduce it** — the drift is *not* the Phase-B state-divergence
problem. Stubbing `0x215` returns a constant → `label_12649`'s slot-select always takes one branch → every
draw collapses onto slot 0 → the anchor-preserve math measures foreign-sized textures → cumulative drift.

**Why the prior "state-divergence" conclusion was wrong.** It was grounded in `capture_gfx_objects.py`,
which polled the object-*record* array (`[esi+0x53d64]`) at ~2/s and saw only 3 persistent UI objects, "0
CG objects." But (i) the branch is driven by the **map lookup** (a different structure the poll never
observed), and (ii) command-buffer records are **transient** — a 2/s poll can't prove CG records weren't
used. Absence in that capture ≠ absence of the native path.

**The fix is tractable and Frida-free.** (b) does *not* mean an opaque native state machine. The subsystem
is a **modelable data structure**: an object-record array (slot / geometry / cmd-type per object) plus a
handle→object registry (a hash map). The gfx ops are inserts/queries/writes against these, and the inserts
are **bytecode-driven** — so a faithful host-side model, with the gfx ops (`0x1a2`, `0x215`, and the
`0x212–0x21a` family) *executed* instead of stubbed, rebuilds the state from the same scripts. The opcode-
level summary lives in `vm-map/opcodes.toml` op `0x215`.

#### gfx command-buffer — op contract table (2026-07-07, full family reversed)

Every gfx op shares one shape: **write a `cmd-type` into the current object record** (`*(ctx + 0x53d88 +
ctx[0x53d14]*0x78) = <cmd>`), fetch operands via `FUN_0041b940(i)` (1-based; `docs` = the `0x1a2` variant
uses `FUN_00415f30`), then either **SET** object fields (call a native worker `FUN_0047xxxx`) or **QUERY**
object fields (write results back to output operands via `FUN_00425fb0(i, val)`). Handlers resolved through
the dispatch table (`ctx[0x26c93+op]`); all renamed in the Ghidra project `gfx_op_0x<op>_<role>`.

| op | handler | cmd | dir | argc | contract |
|---|---|---|---|---|---|
| `0x1a2` | `0x42d360` | 3 | set | 1 | registry **insert**: key `"%c%8.8x"(3, operand-desc)` → `FUN_0042cf70` |
| `0x1f7` | `0x422270` | 5 | erase | 2 | registry **erase** (teardown, NOT create): `op2>1` → `gfx_registry_erase_range(op1,op2)` erases `[op1,op1+op2)`, else `gfx_registry_erase(op1)`. Objects are created lazily by the geometry SET ops. |
| `0x1fa` | `0x4224a0` | 3 | set | 1 | release element `[ctx+0x52bd4 + op1*4]` (vtbl free) + `FUN_00474e40(op1)` |
| `0x1ff` | `0x4227b0` | 9 | set | 4 | 3 int→float params on obj op1 → `FUN_0047e800(op1,f2,f3,f4)` |
| `0x202` | `0x4228d0` | 0xb | set | 5 | blit obj op1 with (op2,op3) + **packed ARGB** from op4(alpha)/op5(color) → `FUN_0047ea00` |
| `0x203` | `0x4229a0` | 9 | set | 4 | draw obj op1 with op2 + packed color(op3/op4) → `FUN_0047e9b0` |
| `0x212` | `0x4230c0` | 5 | set | 2 | `obj[ctx+0x14d54 + op1*4] -> +0x64 = op2` |
| `0x213` | `0x423110` | 7 | set | 3 | `obj[0x14d54+op1*4] -> +0x68 = op2 ; +0x6c = op3` (an (x,y) pair) |
| `0x215` | `0x42a0b0` | 5 | **query** | 2 | registry **find**(op2 handle) → op1 (value / `0xffffffff`). **Drives slot-select.** |
| `0x216` | `0x42a0f0` | 5 | **query** | 2 | read `[ctx+0x46d14 + op2*0x14]` → op1 |
| `0x217` | `0x4231b0` | 9 | set | 4 | 3 int→float on obj op1 → `FUN_0047e960` (SETS a geom 3-vector) |
| `0x218` | `0x42a130` | 9 | **query** | 4 | `FUN_0047f360(obj op1)` → op2,op3,op4 (GETS a geom 3-vector) |
| `0x219` | `0x423240` | 9 | set | 4 | 3 int→float on obj op1 → `FUN_0047e910` (SETS a geom 3-vector) |
| `0x21a` | `0x42a1b0` | 9 | **query** | 4 | `FUN_0047f2e0(obj op1)` → op2,op3,op4 (GETS a geom 3-vector) |

**`label_12649` correlation (the drift chain, confirmed).** The recurring idiom is:
```
query-gfx-object? (G 0x62452) (G 0x6245X)   ; 0x215: handle G[0x6245X] -> working slot G[0x62452]
ui-elem?          (G 0x6245X) 0xa           ; 0x1f7: select that element
ui-clear?         (G 0x62452)               ; 0x1fa: clear the slot
```
`G[0x62452]` is the **working slot**; `G[0x6245X]` are per-object **handles** (the `0x62455[idx]` family:
`0x62456/7/8/a/b/c`). The geometry ops move two per-object 3-vectors between object records and globals:
- **`0x217` SET** anchor-vector `G[0x6249b/c/d]` **into** the object; **`0x218` GET** it back **out**.
- **`0x21a` GET** position-vector into `G[0x62498/9/a]`.
These get-vectors are exactly the inputs to the anchor-preserve math (`docs/superpowers/specs/2026-07-06-a2b-graphics-geometry-design.md`:
`G[0x62498] = G[0x6249b] − w/2`, foot-anchor at `G[0x6249c]`). **So the drift has two stubbed drivers, not
one:** `0x215` (wrong slot → collapse to slot 0) **and** `0x218`/`0x21a` (stale geometry vectors → the
anchor math reads garbage). Both read object state the SET ops (`0x217`/`0x219`/`0x212`/`0x213`) wrote — all
bytecode-driven, all host-modelable.

**Model implication for the host-side reimplementation (Phase 2 input).** The subsystem is a set of
per-object records keyed by handle, carrying: a **slot** (from the `0x215` registry), a **position 3-vector**
(`0x21a` get / a matching set), and an **anchor 3-vector** (`0x218` get / `0x217` set), plus color/blit
params (`0x202`/`0x203`). The native workers (`FUN_0047xxxx` = the DirectDraw/surface layer) need **not** be
modelled — only the object-record data model, so the QUERY ops return what the SET ops stored. That makes
`0x215`/`0x216`/`0x218`/`0x21a` return correct values and the existing bytecode geometry math produces
correct `dst`/`w`/`h`. Ancillary per-object tables observed: `ctx+0x14d54` (obj pointers, fields `+0x64/
+0x68/+0x6c`), `ctx+0x46d14` (stride `0x14`), `ctx+0x52bd4` (element pointers), plus the `0x408` registry.

Worker functions decoded + annotated in the Ghidra project (2026-07-07): `gfx_registry_erase`(`0x47d850`),
`gfx_registry_erase_range`(`0x47d8b0`), `gfx_object_get_or_create`(`0x47ddb0`, inserts a zeroed default via
`gfx_object_init_default`@`0x472810`), the setters `gfx_set_vec18/24/16c`(`0x47e960/e910/e800`), the getters
`gfx_get_vec18/24`(`0x47f360/f2e0`).

### The render drift's SECOND half: missing system-boot state (2026-07-07, resolved)

Implementing the gfx ops (above) was necessary but not sufficient — a cold single-scene run of SC0000 still
drifted. Runtime tracing found **the CG handle array `G[0x62455..0x6245c]` was all zeros**, so every CG
collapsed onto object `0` and its geometry accumulated. Those handles are set by the **boot script `INIT2`**
(mov `0x62455=0xcb20 … 0x6245c=0xcbc0`), which is call-scripted by the real entrypoint **`SYSTEM4.BIN`**
(`LOADCONFIG → INITCONFIG → INIT2 → LOGO → OP → INIT → TITLE → …`). Our harness teleports straight into
SC0000, skipping that boot. **Fix:** run the system-boot state prefix (`INITCONFIG/INIT2/INIT`, skipping the
UI scripts) before the scene — `Age.Cli gfx --boot` and Godot `--boot` (both via `GameSession`). With boot,
the CGs **de-collapse and render correctly** (screenshot-confirmed). **This is the synthesis of the old
(a)-vs-(b) debate: the drift needed BOTH the native gfx ops (b) AND boot state (a) — specifically INIT2's
handle array, never before identified (it is not a story flag).** Note two distinct boots: our Phase-B
`--boot` runs the *data* `*INIT` scripts (skills/items/…); this is the *system* boot (`SYSTEM4` prefix) — a
"full boot" should run both. **Residual:** the `AE*` fade/flash effects still draw opaque (alpha/blend
deferred — Phase 2 scope), and some object-slot CGs start with a zero anchor (cold gfx objects vs the real
game's warm ones; default object geometry is confirmed `(0,0)` in `gfx_object_init_default`, so it is not a
missing-default bug). See `docs/phase-a-slice-plan.md` A2b-Geometry.

### The gfx animation/effects subsystem — the `AE*` fades (2026-07-07)

The `AE*` flash/glow effects (and sprite motion) are a **native time-animated retained render loop**, not
per-frame bytecode. Reversed + annotated in Ghidra:
- **Retained objects carry animation state:** an *active* flag (obj`+0` bit 4), a **progress** counter
  (obj`+0x214`, starts 0), a **duration** (obj`+0x228`), and a **target vector** (obj`+0x244/248/24c`).
- **`gfx_anim_start`** (`0x47f060`, worker for op **`0x234`**) configures a per-object animation: sets the
  flag, resets progress, stores duration + target. Op **`0x1fd`** (`gfx_op_0x1fd_set_vec_scaled`) sets a
  scaled 3-vector.
- **Op `0x238`** (`gfx_op_0x238_set_anim_clock`) sets a **global animation clock**, **non-blocking**:
  `ctx+0x51b78 = 0` (elapsed), `ctx+0x51b7c = duration` (the max per-object duration; SC0000 `label_1235a`
  maxes a table to compute it). It does **not** loop/wait.
- **Frame model:** the bytecode does `configure anims (0x234/0x1fd) → set clock (0x238) → show-text →
  wait-for-input` and **continues**; the native render loop advances the clock + per-object progress each
  frame, interpolates, composites, presents. Render/present family nearby: `0x243/0x20c/0x21c/0x224`
  (`u004162xx`, not yet fully RE'd). **⇒ the port can drive animation in the HOST per-frame loop while the
  VM is parked at wait-for-input — no blocking present op, no VM/host frame-lockstep** (the answer to the
  "frame loop" question).

Consequence: reproducing the fades needs a **retained per-frame animated compositor** — the current
immediate-mode permanent canvas can neither fade nor clear. Design: `docs/superpowers/specs/2026-07-07-
animated-compositor-design.md`.

### The full gfx render model — surfaces + objects + composite (2026-07-07)

Reversed the create/set/draw-texture handlers + the render loop (all annotated in Ghidra). **This is the
canonical model** (an earlier flat "draw layers to one screen" attempt was WRONG — it had no surface concept
and snapshotted textures at draw time; symptoms: alternating grey, glow over backgrounds, vanishing sprites).

**Two distinct stores:**
- **Surfaces** — image buffers at `ctx+0x52bd4[slot]`, indexed by slot. `gfx_op_0x1f8_create_surface`
  (`0x4222d0`) allocates a blank one (releasing any old); `gfx_op_0x1f9_load_surface` (`0x422360`, op `0x1f9`
  set-texture) resolves `resId` via the SYS4INI resolver (`FUN_0044f390`) and loads the file into the slot's
  surface **with a colorkey/chromakey** (op arg 3 — never modelled before), also releasing the old surface.
  A surface persists at its slot until the next set-texture overwrites it.
- **Objects** — the `ctx+0x408` registry, keyed by handle (a `std::map`). `gfx_op_0x1fb_draw_bind` (`0x422510`,
  op `0x1fb` draw-texture) → `gfx_object_bind_draw` (`0x47e870`): sets the object's **source slot** (`obj+4`),
  **source rect** (`obj+8..0x14` = left,top,right,bottom), **position** (`obj+0x24/28/2c` = V24), and the
  **visible** flag (bit 0). The object references its surface **by slot index, live** (re-resolved each frame),
  NOT a snapshot. Objects also carry anchor V18 (`obj+0x18`), animation (flag bit 2 + progress `obj+0x214` /
  duration `obj+0x228` / target `obj+0x244..`), and color/alpha (`0x202/0x203`).

**Render frame** — `gfx_render_frame` (`0x4820b0`), driven by op `0x20c` present (`gfx_op_0x20c_present_frame`
`0x4174a0`, which also updates the frame timer `ctx+0x51b64/68`): iterate the object registry **in ascending
handle order — that IS the z-order** (lower handle behind, higher on top; `std::map` key order). For each
object with visible bit 0, `gfx_object_composite` (`0x47f650`) computes its transform from geometry, **applies
the animation interpolation if bit 2 is set**, and blits `surface[obj.slot]` with alpha/colorkey. Then swap
buffers (present). **Slot 0 is NOT special** — a normal slot; several objects may share one surface.

**⇒ Faithful port:** a `SurfaceStore` (`slot → {image, colorkey}`, from create/set-texture) + an `ObjectStore`
(`handle → {slot, srcRect, position, anchor, scale, anim, alpha, visible}`, from draw-texture + the gfx ops) +
a host per-frame compositor that draws visible objects **in ascending-handle order** from their live surface,
interpolating animations by elapsed time. No VM/host lockstep (op `0x238` clock is non-blocking; animations
play during the wait-for-input park). Open detail for implementation: the exact scale/transform math in
`gfx_object_composite` (`FUN_00472f00`/`FUN_00473ed0`) and the colorkey format.

---

## Native walls backlog (targets for this loop)

- ~~**call-script dispatch**~~ — **SOLVED** (above): `call-script <id>` = raw SYS4INI file index.
- **decision→scene** — how `0x62ccf`/the decision selects the next `SCxxxx`. Now narrower: scenes load
  via `call-script`/the same SYS4INI-index loader, so the open question is only where the decision
  value is turned into a scene *id* (a caller of SCJUMP; re-aimed away from `u00428010`).
- **op `0x60`** (`u0041A270`) — the rand-like value gating 1732/1755 SCJUMP decisions.
- ~~**gfx command-buffer**~~ — **DONE** (the `0x212–0x21a` positioned-object subsystem = the rendering
  drift): all 14 ops reversed + implemented against a host-side `GfxState`, and the missing INIT2 boot
  state supplied via `--boot`. CGs render (screenshot-confirmed). See the op `0x215` finding + "The render
  drift's SECOND half" above. Remaining: `AE*` alpha/blend (deferred) and cold-object anchors.

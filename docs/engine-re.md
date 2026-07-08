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

#### The query registry is SEPARATE from the geometry object store (2026-07-07) — the retained-mode "2nd CG off-screen" fix

Modelling the gfx ops (above) exposed a subtle but decisive point that the first retained-mode
implementation got wrong. There are **two distinct native structures**, and they must stay distinct:

1. **The op-`0x215` query registry** — a `std::map<handle,value>` **populated ONLY by op `0x1a2`**
   (`FUN_0042cf70` hash insert; native stores `map[handle] = handle`). `0x215` does `map.find(handle)`
   → the found value (which equals the handle, and for small system/UI handles doubles as their surface
   slot) or `0xffffffff` = **-1**.
2. **The geometry object store** — per-handle V18/V24/V16c/color/draw-bind, touched lazily by the
   geometry SET ops and `draw-texture` (`gfx_object_get_or_create`). This feeds the compositor.

The first `GfxState` conflated them: `GetOrCreate` (called by *every* geometry/draw op) also assigned a
fabricated per-object slot via an `AcquireSlot()` allocator, and `QuerySlot` (op `0x215`) returned it.
That is a fiction with **no basis in the engine** — the native `0x215` never allocates a slot.

Consequence, traced end-to-end in `SC0000` `label_12649` (the CG-load subroutine): a CG handle
(`0xcb2a` = `INIT2`'s `G[0x62456]`, idx 1) is **never `0x1a2`-registered**. Real engine → `0x215` returns
`-1` → the **fresh branch** runs → anchor comes from the `INIT2` arrays (`G[0x62469+idx]=400`,
`G[0x6247d+idx]=600`) → `dst = anchor − (w/2, h) = (0,0)`. Correct. But with the fabricated allocator the
*second* pass over the same handle found it "existing" (slot `4`) → the **existing branch** ran
`get-texture-size(4)` on a slot whose surface was never loaded (the bytecode's own slot table
`rec[s3]`/`G[0x3239]` gave slot `0`) → size `0` → `anchor = pos(0,0) + 0` → `dst = (0−400, 0−600) =
(−400,−600)` — the CG rendered off-screen. This is the bug that had been mis-attributed to "geometry
accumulation / drift" several times.

**Fix (branch `feat/gfx-command-buffer`):** `GfxState` keeps a separate `_registry` (a `HashSet<long>`)
populated only by `Register(handle)` (op `0x1a2`); `QuerySlot` returns `handle` if registered else `-1`,
and no longer consults the geometry store or invents slots. Verified: `Age.Cli gfx --boot SC0000.BIN`
→ all event CGs `dst=(0,0)`, zero `(−400,−600)` draws; Godot `--boot --shot` pages 1/2/4 render the
opening event CGs full-screen; engine 44/44; sweep parity 284 exit / 13 STEP-LIMIT unchanged.

Note a **second, still-latent** gap this uncovered: `label_125bd` (which fills `rec[s3]`/`G[0x3239]` with
the per-object slots 4..13, called at `SC0000` `0x50f`) does **not** execute in a cold single-scene run —
the scene coroutine framework (ops `0x7b`/`0x140` + the `G[0xaba5c]==1` re-entry gate) routes cold flow
past it, so every fresh CG is assigned slot `0`. It doesn't break the *opening* (one full-screen CG shown
at a time, so sharing slot 0 is harmless and the fresh-branch geometry is correct regardless), but a scene
with several simultaneous distinct-slot objects would need the setup to run. Tracked as the scene-coroutine
work, separate from this fix.

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

#### The `0x21c–0x243` sprite transform / ANIMATION cluster (2026-07-07, recon — implementation pending)

The scene-completeness tracker (`tools/scene_opcode_coverage.py`) flagged a dense band of GAP ops in
`0x21c–0x243` (+ `0x2bd/0x2bf`) — the **largest remaining rendering unknown** in SC0000 (e.g. `0x220`×66,
`0x22f`×34, `0x228`×33, `0x21e`×25 static sites). Resolving every one through the dispatch table
(`ctx[0x26c93+op]`, read from `FUN_00413860`) shows it is **one coherent subsystem: sprite transform +
animation/tween** — and two members were already named in prior RE (`0x234 gfx_op_0x234_anim_start`,
`0x238 gfx_op_0x238_set_anim_clock`). Kelebek VAs drift here as everywhere (op `0x220` real handler is
`0x4234e0`, not Kelebek's `0x4215D0`). **Op → real handler map:**

| op | handler | op | handler | op | handler |
|---|---|---|---|---|---|
| `0x21c` | `0x417520` (417xxx trivial) | `0x229` | `0x423700` | `0x236` | `0x423ee0` |
| `0x21d` | `0x423310` | `0x22a` | `0x4237b0` | `0x237` | `0x4240a0` |
| `0x21e` | `0x423350` **✎ set_transform3_norm** | `0x22b` | `0x423850` | `0x238` | **anim_start's clock ✎** |
| `0x21f` | `0x423410` | `0x22c` | `0x423900` | `0x239` | `0x424120` |
| `0x220` | `0x4234e0` **✎ set_transform3_abs** | `0x22d` | `0x423990` | `0x23a` | `0x42a440` |
| `0x221` | `0x423590` | `0x22e` | `0x423a40` | `0x23b` | `0x424190` |
| `0x222` | `0x4235e0` | `0x22f` | `0x423b00` | `0x23c` | `0x417580` (417xxx) |
| `0x223` | `0x423620` | `0x230` | `0x423ba0` | `0x23d` | `0x4175c0` (417xxx) |
| `0x224` | `0x417550` (417xxx) | `0x231` | `0x423be0` | `0x23e` | `0x42a4a0` |
| `0x225` | `0x4236a0` | `0x232` | `0x423c30` | `0x23f` | `0x42a520` |
| `0x226` | `0x42a230` | `0x233` | `0x423cf0` | `0x240` | `0x4245f0` |
| `0x227` | `0x42a2e0` | `0x234` | **anim_start ✎** | `0x241` | `0x4247e0` |
| `0x228` | `0x42a3a0` | `0x235` | `0x423e40` | `0x242` | `0x4249d0` |
|  |  |  |  | `0x243` | `0x4182d0` (417xxx) |

(`0x2bd`→`0x4251c0`, `0x2bf`→`0x425240`. The handful of `0x417xxx` handlers are trivial/marker-shaped — the
default-handler neighbourhood — and are almost certainly no-ops or arg-poppers; triage before modelling.)

**Contract (decoded, representative ops `0x220`/`0x21e`, both `argc 6`, annotated in Ghidra):** same shape as
the geometry family — write **cmd-type `0xd`** into the current object record, fetch operands 1..6, call a
transform worker with `(int op1=handle, int op2, int op3, float op4, float op5, float op6)`. `0x220` uses raw
floats (worker `0x47ecc0`); **`0x21e` normalizes the 3 floats by `/_DAT_00571c28`** (runtime-init divisor,
static 0) so operand `0x64`=100 → a fraction → **scale/percentage** (worker `gfx_anim_set_channel`@`0x47eaa0`).
The worker calls the SAME `gfx_object_get_or_create` our `GfxState` already models, then arms an animation
channel on the object record: `obj+0x3c = op2`, `obj+0x50 = op3`, `obj+0x68 = 1` (enable), `obj+0xac =
vec3(op4,op5,op6)` (the transform target), and raises global dirty flags `ctx+0xb558/+0xb560`. Corpus idiom:
`0x220 (handle=0xcb20+k) 800 500 0 0 0` (size a CG object), `0x21e (handle) (val) 100 100 100 100` (scale/color
channels). **`0x234 anim_start` + `0x238 set_anim_clock` imply a per-frame clock that interpolates these
targets over time** — i.e. this is what makes `AE*` fades/effects *animate* rather than snap.

**Model implication (Phase-2 input, mirrors the geometry family):** the DirectDraw workers need NOT be
modelled — extend the host `GfxState` object with the transform/anim fields (a transform `vec3` target + the
two scalar params + enable + an animation clock), have the SET ops (`0x21e/0x220/0x234/0x238/…`) write them and
the compositor apply the transform per-frame, stepping the clock on `anim_start`/`set_anim_clock`. This is a
spec/plan-worthy chunk (~18 effectful handlers + workers `0x47eaa0/0x47ecc0` + the per-frame stepping); the op
map above is the de-risked starting point. `tools/scene_opcode_coverage.py SC0000` measures the GAP shrink as
each lands.

##### `anim_start`/`set_anim_clock` decoded + opening confirmed (2026-07-07, animation-slice Task 1)

Decoding the two already-named clock/start ops (dispatch table → `0x234`@`0x00423da0`, `0x238`@`0x004240e0`;
both annotated) and grepping the SC0000 opening settles the animation model and confirms the opening exercises it:

- **`0x238 set_anim_clock` (argc 1, cmd-type 3):** `ctx+0x51b78 = 0` (elapsed), `ctx+0x51b7c = operand1`
  (total duration). **A GLOBAL, NON-BLOCKING clock** — not per-object. The op only *configures* the clock; it
  does **not** loop/wait. The native render loop advances this clock each frame and interpolates *all* animating
  objects. Its own plate comment states the payoff: "our port can drive animation in the host's per-frame loop
  while the VM is parked at wait-for-input; no VM/host frame-lockstep." → **validates the wall-clock-tween
  architecture directly.** SC0000: `set-anim-clock(G[0x624bb])` @`0x123bd`, `set-anim-clock(0x190=400)` @`0x13858`.
- **`0x234 anim_start` (argc 5, cmd-type 0xb):** `gfx_anim_start(op1=handle, op2, (float)op3, (float)op4,
  (float)op5)`. `op2` = *this object's* animation duration (`label_1235a` maxes the per-object durations into the
  global clock); `(op3,op4,op5)` = the target transform vec3 the object animates **toward**. SC0000 opening
  @`0x00daf`: `anim_start(G[0x62457], 0x2328, 0, 0, local2)` on the INIT2 CG handles, then `call label_1235a`.
- **The opening path uses the whole subsystem, early.** `0x21e`/`0x220` transform-sets fire from `0x00f73`
  onward (`0x21e (G[0x6245b]) 0 0x12c l0 l1 0x64`, `0x220 (G[0x62457]) 0x96 0x3e8 l1 l3 0`), on the same INIT2 CG
  handles (`G[0x62457]`,`0x6245b`,`0x6245c`) — this is the opening, **not** battle/debug. So the slice's ops are
  real and verifiable on screen.

**Corrected host model (supersedes the "per-object clock" wording above):**
- **Global clock** (from `0x238`): one `AnimClockDurationTicks` + a generation/reset marker the host watches to
  reset its wall-clock `elapsed` to 0. The host tweens all armed objects over this duration.
- **Per-object** (from `0x21e`/`0x220` = set transform directly; `0x234` = animate toward a target): the object's
  transform target vec3 + the two scalar params + enable + a per-object generation (bumped by `anim_start`).
- **Residual (empirical, Task 6):** *which* vec3 component is opacity vs scale vs position lives in the DirectDraw
  draw-worker we deliberately don't model. Determine it empirically from the animating channel + screenshot, not
  by RE'ing the surface layer.

##### The opening render path is RETAINED, not immediate-mode (2026-07-08, ground-truth correction)

A working note in the animation slice mis-called the SC0000 opening a set of "immediate-mode slot-0 blits." That
was **wrong**, and it came from trusting our own `Age.Cli gfx` oracle (which executes our VM and mis-labeled the
CG draws as "slot 0"). Verified against native code + the raw bytecode:

- **`draw-texture` (op `0x1fb`, handler `gfx_op_0x1fb_draw_bind`@`0x422510`) is a RETAINED bind, not a blit.** It
  writes cmd-type `0x11` and calls **`gfx_object_bind_draw`@`0x47e870`**, which on the object keyed by `handle`
  (operand 1) sets: `flag|=1` (visible), `obj+4 = source SLOT index`, `obj+8..0x14 = source rect`,
  `obj+0x24/28/2c = position`. Its plate comment (prior RE) already states the key fact: the object stores the
  **slot INDEX — a live ref to `surface[slot]`, resolved each frame at render — NOT a texture snapshot.** Objects
  persist and are composited each frame; this is exactly the surfaces+objects model in "The full gfx render
  model" above.
- **The SC0000 opening is a retained scene of distinct objects, `sleep`-paced.** Raw bytecode: fixed-handle UI
  objects (`0xcf08` slot 3 full-screen, `0xc350` slot 0xe, `0xe678` slot 0xd — a 400×30 element re-bound 20+
  times), an animated sprite (`draw-texture (G[0x62457]) (G[0x62452]) … (G[0x62498]) (G[0x62499])`, computed
  position), and the CG loader (`SC0000` @`0x126e1`/`0x12970`): `set-texture G[0x62424] → slot G[0x62452]`,
  `get-texture-size`, centre it, then `draw-texture (handle = CG_array[G[0x62450]] = INIT2 array G[0x62455..])
  slot G[0x62452] …`. `sleep 0x64/0x3e8/0x2ee` sits between steps. So different draws use **different handles and
  per-object working slots** — not one slot-0 canvas.
- **Why our port still doesn't animate the opening (conclusion unchanged, mechanism corrected):** we execute the
  whole load/draw/`sleep` sequence **instantly** — no `sleep` timing, no per-frame present — so we only ever see
  the *final* retained state; the intermediate `AE*` frames (`AE001D→AE002B→AE003B`, surface swaps on the working
  slot between paced frames) never get a frame to display. The fix is **frame-pacing** (scene-coroutine / `sleep
  0xc8`), a separate subsystem from the transform/alpha channel. **Lesson: never characterise the engine's render
  mechanism from our own VM's oracle output — use native code + raw bytecode.**

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

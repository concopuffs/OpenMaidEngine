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
- IAT reconstruction — **tried, DOESN'T WORK on this binary (2026-07-09):** `bin/pe-sieve32.exe /pid
  <PID> /imp 3 /dmode 3 /dir build/pe-sieve` (run from **PowerShell**, not Git Bash — it mangles
  `/flags`) ran fine but the game is packed with a **zeroed IAT** resolved via `GetProcAddress` at load,
  so there is no conventional import table to rebuild. Of 363 "imports" it emitted, only ~17 are genuine
  (`in_main:1`): the packer bootstrap (`LoadLibraryA`/`GetProcAddress`/`GetModuleHandleA`/`VirtualAlloc`/
  `VirtualFree`) + a one-per-DLL seed block at RVA `0x202bfc` (`d3d9.Direct3DCreate9`,
  `user32.RegisterClassExA`, `gdi32.GetStockObject`, `winmm.timeSetEvent`, `advapi32.RegOpenKeyA`,
  `shell32.SHGetSpecialFolderPathA`, `oleaut32.Variant*`, `kernel32.RaiseException`, …). The other 300+
  are stray pointer-shaped DWORDs mis-resolved to "first export at module base" (e.g. `msvcrt._wstrtime_s`
  30×, `in_main:0`, non-terminated). ⇒ **do not graft pe-sieve output** — grafting the noise would inject
  wrong import names. The game's hot APIs (`ReadFile`/`CreateFileA`/`timeGetTime`/d3d9 device methods) are
  `GetProcAddress`-resolved into private pointer tables, invisible to a static IAT scan. Report/dump left
  at `build/pe-sieve/process_<pid>/` (disposable).

  **→ The Frida import-map approach — ✅ DONE 2026-07-09 (replaced pe-sieve).** Named the
  dynamically-resolved APIs at their call sites via the LIVE process. `tools/frida/map_imports.py`
  (read-only, plain-JS): (1) Frida-reads all loaded modules' export tables → `{runtime_addr → dll!Func}`
  (23,342 exports); (2) scans the `0x400000` module for aligned DWORDs holding those addresses → `RVA →
  name` (ASLR-stable: RVAs into the fixed main module transfer to the dump even though the DLL targets
  relocate); (3) a `run_script_inline` pass labels the `/v2` image `imp_<dll>_<func>`. **Result: the packer's
  rebuilt core IAT lives at RVA `0x16f000` (VA `0x56f000`) — 248 imports labeled** (kernel32 129, user32 57,
  winmm 20, gdi32 17, advapi32/ole/oleaut/version/ntdll), 0 clobbers. **Validated:** `FUN_0044f390` now reads
  `(*imp_kernel32_CreateFileA)` / `(*imp_kernel32_SetFilePointer)` at its resolver I/O; `sleep_timer_arm` reads
  `(*imp_winmm_timeGetTime)()` — pinning the long-standing `DAT_0056f3d4` = **timeGetTime**. Tool:
  `tools/frida/map_imports.py [--recon]` → `build/import-map.json`; plan
  `docs/superpowers/plans/2026-07-09-frida-import-map.md`.
  **Known limit (by design):** only the module-resident IAT is labelable. `d3d9`/`shell32`/`dsound`/CRT are
  `GetProcAddress`-resolved into HEAP (not in the `0x400000` dump), so they aren't labeled — and D3D9 is used
  via COM vtables (`Present` = device vtable slot 17, see `probe_present.py`), not an import thunk, so this
  costs us nothing on the render path. 29 isolated singleton matches were set aside (`build/import-map-singletons.json`),
  not auto-applied.

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

### Materialized + applied image-wide (2026-07-09)

The table is no longer resolved op-by-op by hand — it is **extracted once and applied to the whole
image**. `tools/ghidra_handler_map.py` parses the override stores in `FUN_00413860` (dump at
`build/engine-dump/FUN_00413860.disasm.txt`) → **`build/op-handler-map.json`** (`{op → handler VA}`,
**420 overrides**). Regenerate: `py -3.11 -X utf8 tools/ghidra_handler_map.py
build/engine-dump/FUN_00413860.disasm.txt --check`. The `--check` diffs the derived handlers against the
handler VAs mentioned in `vm-map/opcodes.toml` prose and found **0 real drift** — the only 7 flags are
ops whose toml text records the *worker* VA, not the handler (`0x20c→0x4174a0`, and the `0x21c–0x243`
cluster entries), each already matching the recon tables below.

A one-shot Ghidra script (via `run_script_inline`; needs `GHIDRA_MCP_ALLOW_SCRIPTS=1`) then labeled the
image from that map: **281 raw `FUN_`/`LAB_` handlers renamed `op_0xNN_handler`, 107 bare handler VAs
turned into functions, 31 hand-named handlers preserved** (source `USER_DEFINED` is never renamed), and
a plate comment `opcode 0xNN dispatch handler; ctx[0x26c93+op] in FUN_00413860` set on every one
(appended to existing decode comments, never clobbering). The one shared handler `0x416650` (ops
`0xaf`/`0x1a8`) is `op_0xaf_0x1a8_handler`. ⇒ every dispatch handler in the image now self-identifies its
opcode; a bare `op_0xNN_handler` is a handler not yet role-RE'd. Enrich with a descriptive name +
decode when you reverse one (the generic name is a floor, not a final).

> **⚠ Two-program gotcha (cost time 2026-07-09).** The Ghidra project holds **two** imports named
> `range_00400000.bin`: the GOOD one at project path **`/v2/range_00400000.bin`** (`x86:LE:32:default`,
> image base `0x400000`, 4308 functions — all our annotations live here) and a BROKEN early import at
> **`/range_00400000.bin`** (the `x86:LE:32:System Management Mode` mis-import: base `0000:0000`, **0
> functions**; see the language gotcha in the runbook). After a Ghidra restart the broken one can become
> active. **Always confirm `get_current_program_info` shows base `0x400000` / 4308 functions (or
> `switch_program /v2/range_00400000.bin`) before doing anything** — `run_script_inline` runs against the
> GUI's active program, so a wrong-program script would mutate/measure garbage.

**Other confirmed engine-context offsets** (`ctx`/`esi`): `+0x53d14` = current gfx-object index;
`+0x53d88` = per-object cmd-type table (stride `0x78` = 120 bytes); operand-fetch helper =
**`vm_operand_fetch`@`0x41b940`** (thiscall, `ecx=ctx`, arg = operand index → returns the operand value);
**`vm_operand_write`@`0x425fb0`** = the counterpart store; **`vm_operand_lvalue`@`0x415f30`** = the
companion index/pointer accessor.

**Hot-helper naming pass (2026-07-09, lever #2).** Ghidra's Function ID analyzer names **0** functions on
this image (the bundled FidDbs don't cover the VC9/VS2008 static runtime; ~3,660 of 4,428 funcs stay
`FUN_`), and the library workers we actually touch were already hand-named (`gfx_object_query_source_slot`,
etc.). So "STL/CRT auto-naming" had little to add — but the recon (rank unnamed funcs by call-count)
surfaced the real win: ubiquitous **documented-but-unnamed helpers**. Named the top 5 (~2,400 call
sites): `vm_operand_fetch`@`0x41b940` (1021 refs), `vm_operand_write`@`0x425fb0` (188),
`vm_operand_lvalue`@`0x415f30`, plus two CRT primitives identified by behavior —
`__security_check_cookie`@`0x54f981` (692; compares `__security_cookie`=`DAT_005c28c0`) and
`operator_new`@`0x5502be` (533; `_malloc`+`__callnewh`+throw `bad_alloc`). ⇒ every handler now reads
e.g. `vm_operand_fetch(2)` not `FUN_0041b940(2)`. No FidDb generation (out of scope, low ROI). Rename
hot unnamed funcs by call-count when de-noising further; there is no registry file for these — the
Ghidra name is the record.

**These `ctx` offsets are now a typed struct (2026-07-09).** The canonical field map is
`vm-map/engine-ctx.toml` → generated `docs/engine-ctx-reference.md`; a `run_script_inline` pass created
an `EngineCtx` Ghidra struct and retyped **all 419 dispatch handlers' `this` to `EngineCtx *`**, so they
decompile `ctx->cur_ctx_index` / `ctx->cmd_type_table` / `ctx->run_state_flags` instead of `param_1 + 0x…`
(verified: `sleep_op_0xc8`, `gfx_op_0x215_query_source_slot`). Add a field: edit `engine-ctx.toml`, run
`engine_ctx_build.py --build`, re-apply the struct. (The VM global bank `G[…]` is separate — `globals.toml`.)

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
handle→object registry (a `std::map`). Geometry and draw workers lazily populate that retained-object map;
query and erase workers read/remove the same entries. Op `0x1a2` also maintains an operand-descriptor hash,
but that is a separate structure and is not what op `0x215` queries. The opcode-level source of truth is
`vm-map/opcodes.toml`.

#### Op `0x215` queries the retained gfx-object's source slot (corrected 2026-07-09)

The decisive caller/callee detail is the owner pointer. Op `0x215`'s handler passes
`ECX = ctx+0x46614` to `gfx_object_query_source_slot` (`0x47f280`); that worker searches
`ECX+0x408`. `draw-texture` passes the same owner to `gfx_object_bind_draw` (`0x47e870`), whose
`gfx_object_get_or_create` uses the same `owner+0x408` map and writes the source surface slot to
`obj+4`. Therefore:

- absent handle → `0x215` returns `0xffffffff` (-1);
- geometry-only/unbound object → its default source slot is -1;
- draw-bound object → `0x215` returns the live source slot from `obj+4`.

The earlier “op `0x215` reads a registry populated only by `0x1a2`” conclusion was wrong: it conflated
the retained-object `std::map` with `0x1a2`'s open-addressing operand-descriptor hash. The useful part of
the earlier fix remains: `GetOrCreate` must not fabricate a slot. A fresh object stays unbound (-1) until
`draw-texture` supplies its real source slot.

This also explains the reported magic-circle retention end-to-end. `AE001H.AGF` (resource `0x37`) is
bound to the ritual object's surface slot. At the post-effect cleanup (SC0000 `0x3321`), the script queries that object with
`0x215`, sign-tests the returned slot, then executes `0x1f7(handle, 10)` followed by
`0x1fa(returned_slot)`. Native `0x1f7` removes the retained object group from this same map; `0x1fa`
releases the surface slot. The port's old separate-registry model returned -1, skipped the guarded cleanup,
and left the circle compositing. `GfxState.QuerySlot` now returns `GfxObject.SourceSlot`; VM op `0x1fa`
clears that surface slot. The booted SC0000 regression ends with no visible resource `0x37`; live
clicked-path validation confirmed the corrected disappearance on 2026-07-10.

Note a **second, still-latent** gap this uncovered: `label_125bd` (which fills `rec[s3]`/`G[0x3239]` with
the eight per-object slots 4..11, called at `SC0000` `0x50f`) does **not** execute in a cold single-scene run —
the scene coroutine framework (ops `0x7b`/`0x140` + the `G[0xaba5c]==1` re-entry gate) routes cold flow
past it, so every fresh CG is assigned slot `0`. It doesn't break the *opening* (one full-screen CG shown
at a time, so sharing slot 0 is harmless and the fresh-branch geometry is correct regardless), but a scene
with several simultaneous distinct-slot objects would need the setup to run. Tracked as the scene-coroutine
work, separate from this fix.

**⇒ Scene-coroutine framework — INVESTIGATION COMPLETE (2026-07-09).** The mechanism behind the slot-0
collapse is fully understood; the native finding and the implemented host-model disposition follow:

**The gate `G[0xaba5c]` is NATIVE scene-entry state — no script sets it to 1.** Across the whole corpus
(429 references in 150 files) *every* `aba5c` reference is a read or a write of `0`; nothing anywhere writes
`1`. So `aba5c==1` is set by the engine's scene loader/scheduler on entry — the **same class as the INIT2
handle array** (native entry-state a cold single-scene harness skips), NOT a story flag. Cold, it reads `0`.

**Corrected roles of the two branches** (the earlier head-start had them inverted). At SC0000 `0x450`
`eq local0 = (aba5c==1)`; `0x457 jcc local0 label_462 <fallthrough>`:
- **`aba5c==1` → `label_462` "ループ開始" (loop start)** = the scene's **intro/setup LOOP**. Its body
  `label_491` runs `call label_125bd` (@`0x50f`, the slot-table fill `G[0x3239..0x324e]=4..11`) plus ADV
  state init, UI-slot clears (a loop over `G[0x3239]`), intro draw — then `jmp label_462` (@`0x711`). A real
  loop, exited only when its iterator makes `G[0x6be]==exit-PC` (→ `mov aba5c 0`, `jmp label_45e`).
- **`aba5c!=1` → `label_45e → call label_71b`** = the **scene CONTENT**: `label_71b` is a `switch(G[0x62ccf])`
  on the SCJUMP decision → "序章 / プロローグ", `play-bgm`, `call label_12649` (CG loads that *read* the slot
  table). So the intended lifecycle is **enter `aba5c=1` → intro loop fills slots → `aba5c→0` → content uses
  slots.** Cold we skip straight to content with an empty slot table → all layers collapse to slot 0 → grey.

**The loop iterator `op 0x140` is a native video-service call — not statically reproducible.** Handler =
**`0x4299c0`** (dispatch `ctx[0x9b74c]=0x4299c0`; created+typed `EngineCtx*`+annotated; Kelebek `u0041F9C0` is
VA-drift). It writes gfx cmd-type 9, copies operand-2/3 strings (`"LABEL"`, `"J"`) + operand-4 int, calls
**`(*DAT_005c6018)(8, ctx[0x54fe8], &{str,str,int})`**, and writes the returned PC-like value back to operand 1
(SC0000: `G[0x6be]`). `DAT_005c6018` is a **runtime-resolved function pointer** (all 6 xrefs are READs, no
static writer) — the engine's **native video / transition / timing service**: `FUN_00405740` (a screen-
transition/fade routine full of DirectDraw-layer calls) calls the *same* pointer with `cmd_id=3` and branches
on its return (`1`/`2` = transition progress). It is the **same class as the DirectDraw workers this project
deliberately does not model.** ⇒ faithfully emulating `0x140` = emulating the native video service = out of
static scope, permanently.

**The two companion ops (confirmed):**
- **`0x7b` (`FUN_0041ebf0`)** — *save yield handlers*: writes op1→`ctx[0x6da88+idx*4]`, op2→`ctx[0x6db28+idx*4]`
  (idx=`ctx[0x53d14]`). SC0000 `0x79`: `op 0x7b label_3c9 label_41e` — registers the per-frame ADV handlers.
- **`0x7c` (`FUN_00417cb0`)** — *resume*: requires run-state bit `0x2000000` (`ctx[0x6dbc8]`) — **throws
  (`__CxxThrowException`) if unset**, so it is only ever reached on a scheduler-driven re-entry, never cold;
  restores PC=`ctx[0x53d28]+ctx[0x6dbcc]*4`, clears the bit, resets input/line state.
- `label_3c9`/`label_41e` (the `0x7b`-saved handlers) are per-frame *render → poll (`call label_8c`) → yield
  (`0x7c`)* routines — i.e. **this coroutine machinery IS the ADV per-frame loop**, not just intro setup.

**⇒ DESIGN (host-model, not emulate).** To make cold single-scene runs correct: (1) supply `aba5c=1` as
scene-entry state (native, seedable, like INIT2); (2) give `0x140` a **host-modeled bounded "labeled yield"**
that runs the intro body at least once (so `label_125bd` fills the slot table + ADV init runs) then reports
completion so content plays — we reproduce the *observable effect*, not the video service. The idiom is
byte-identical across all ~136 ADV scenes, so it generalizes with zero per-scene work. **Payoff beyond the
slot fix:** the same `0x7b`/`0x7c` + handler machinery is the ADV frame loop, so a clean host model becomes
the seam for the interactive-ADV backlog (`0x90` hotspots / EMPTY scenes) and likely fixes the stuck magic
circle (scene-phase cleanup). **Permanently out of static scope:** the real intro-transition *timing/pacing*
inside the loop (it lives in the native video service) — we approximate it host-side, as everywhere else.
**Host model implemented (2026-07-09).** `VirtualMachine` recognizes only the ADV `"LABEL","J"` form
(138 corpus scripts; all have the same terminal-check shape), synthesizes `G[0xaba5c]=1` on top-level
scene entry, forces one setup-body pass even if `G[0x6be]` is stale, then returns the terminal immediate
discovered from the following `mov`/`eq` pair. Thus no SC0000 offset is hardcoded. Op `0x7b` retains the
saved handler PCs as frame metadata; op `0x7c` is a host-scheduler marker because the host already owns
service-boundary suspension and retained presentation. `TITLE.BIN`'s unrelated `"BIN","SC????.BIN"` service remains
stubbed. The real video-service timing remains intentionally unmodeled.

**Magic-circle retention fixed in the host model (2026-07-09).** The effect is `AE001H.AGF`
(resource `0x37`). SC0000 already contains the correct teardown, but the port's wrong `0x215` query
returned -1 and skipped it. The corrected source-slot query now reaches `0x1f7(handle,10)` object erasure
and `0x1fa(slot)` surface release; the booted regression ends with no visible `0x37` object.

#### gfx command-buffer — op contract table (2026-07-07, full family reversed)

Every gfx op shares one shape: **write a `cmd-type` into the current object record** (`*(ctx + 0x53d88 +
ctx[0x53d14]*0x78) = <cmd>`), fetch operands via `FUN_0041b940(i)` (1-based; `docs` = the `0x1a2` variant
uses `FUN_00415f30`), then either **SET** object fields (call a native worker `FUN_0047xxxx`) or **QUERY**
object fields (write results back to output operands via `FUN_00425fb0(i, val)`). Handlers resolved through
the dispatch table (`ctx[0x26c93+op]`); all renamed in the Ghidra project `gfx_op_0x<op>_<role>`.

| op | handler | cmd | dir | argc | contract |
|---|---|---|---|---|---|
| `0x1a2` | `0x42d360` | 3 | set | 1 | operand-descriptor hash insert: key `"%c%8.8x"(3, operand-desc)` → `FUN_0042cf70`; separate from the retained object map |
| `0x1f7` | `0x422270` | 5 | erase | 2 | retained-object erase: `op2>1` → `gfx_object_erase_range(op1,op2)` erases `[op1,op1+op2)`, else `gfx_object_erase(op1)` |
| `0x1fa` | `0x4224a0` | 3 | set | 1 | release **surface slot** `ctx+0x52bd4[op1]` (vtbl free) + `FUN_00474e40(op1)` |
| `0x1ff` | `0x4227b0` | 9 | set | 4 | 3 int→float params on obj op1 → `FUN_0047e800(op1,f2,f3,f4)` |
| `0x202` | `0x4228d0` | 0xb | set | 5 | blit obj op1 with (op2,op3) + **packed ARGB** from op4(alpha)/op5(color) → `FUN_0047ea00` |
| `0x203` | `0x4229a0` | 9 | set | 4 | draw obj op1 with op2 + packed color(op3/op4) → `FUN_0047e9b0` |
| `0x212` | `0x4230c0` | 5 | set | 2 | `obj[ctx+0x14d54 + op1*4] -> +0x64 = op2` |
| `0x213` | `0x423110` | 7 | set | 3 | `obj[0x14d54+op1*4] -> +0x68 = op2 ; +0x6c = op3` (an (x,y) pair) |
| `0x215` | `0x42a0b0` | 5 | **query** | 2 | retained-object **find**(op2 handle) → op1 = obj+4 source slot / `0xffffffff`. **Drives setup and teardown.** |
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
per-object records keyed by handle, carrying: a live source **slot** (written by draw-texture, queried by `0x215`), a **position 3-vector**
(`0x21a` get / a matching set), and an **anchor 3-vector** (`0x218` get / `0x217` set), plus color/blit
params (`0x202`/`0x203`). The native workers (`FUN_0047xxxx` = the DirectDraw/surface layer) need **not** be
modelled — only the object-record data model, so the QUERY ops return what the SET ops stored. That makes
`0x215`/`0x216`/`0x218`/`0x21a` return correct values and the existing bytecode geometry math produces
correct `dst`/`w`/`h`. Ancillary per-object tables observed: `ctx+0x14d54` (obj pointers, fields `+0x64/
+0x68/+0x6c`), `ctx+0x46d14` (stride `0x14`), `ctx+0x52bd4` (element pointers), plus the `0x408` registry.

Worker functions decoded + annotated in the Ghidra project (updated 2026-07-09): `gfx_object_erase`(`0x47d850`),
`gfx_object_erase_range`(`0x47d8b0`), `gfx_object_query_source_slot`(`0x47f280`),
`gfx_object_get_or_create`(`0x47ddb0`, inserts a zeroed default via
`gfx_object_init_default`@`0x472810`; critically, source slot `obj+4` defaults to **0**, while only an absent
map entry queries as `-1`), the setters `gfx_set_vec18/24/16c`(`0x47e960/e910/e800`), the getters
`gfx_get_vec18/24`(`0x47f360/f2e0`).

**Page-58 lifecycle correction (2026-07-11).** Locator `SC0000 P058` resolves to `wait@0x6545`; the loader at
`0x6470..0x6478` resolves resource `0x7a` to `EV050EA.AGF`. The port loaded and bound it correctly, but handle
`0xcb2a` inherited an earlier translation target `(-100,0)`, rotation `-90 degrees`, and alpha endpoint zero.
The preceding query-gated cleanup had skipped this created-but-unbound object because the port initialized
`SourceSlot=-1`. Native `gfx_object_init_default` zeroes `obj+4`, so op `0x215` returns slot 0, the cleanup
erases the object, and the later bind recreates identity state. Matching that default leaves EV050EA centered,
unrotated, and opaque in the synchronized page-58 compositor trace. The adjacent op `0x242(handle,0)` is not
a reset: `op_0x242_set_object_field2d0@0x4249d0` calls `gfx_object_set_field2d0@0x47f1a0`, which only writes
the supplied value at `obj+0x2d0`.

**Page-89 surface-lifetime correction (2026-07-11).** Locator `SC0000 P089` resolves to
`wait@0x89e9`; the loader at `0x8976..0x898a` resolves resource `0x6a` to `BG004D.AGF`. The background
was bound but composited at `(400,650)`. The remaining 550 pixels were intentional retained motion from
BG001A; the incorrect `(400,600)` component came from the host keeping slot 0's boot-time 800x600 dimensions
after op `0x1fa` released that surface. Native op `0x1fa` frees and nulls `ctx+0x52bd4[slot]`, so a subsequent
op `0x208` size query of the released slot yields no surface dimensions. Clearing the host slot resource and
dimensions on release makes the script's existing-object geometry path compute base `(0,-500)`; after the
retained `(0,550)` translation, BG004D lands at `(0,50)`. A synchronized run at the exact wait confirms the
background and character layers together. This path also established that op `0x1ff` directly replaces the
current translation matrix at `obj+0x16c`; it is now modeled rather than stored in an inert side vector.

#### The `0x21c–0x243` sprite transform / ANIMATION cluster (2026-07-10, partial implementation)

The scene-completeness tracker (`tools/scene_opcode_coverage.py`) flagged a dense band of GAP ops in
`0x21c–0x243` (+ `0x2bd/0x2bf`) — at that point the **largest remaining rendering unknown** in SC0000
(e.g. `0x220`×66,
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
| `0x227` | `0x42a2e0` | `0x234` | **set_rotation_cycle ✎** | `0x241` | `0x4247e0` |
| `0x228` | `0x42a3a0` | `0x235` | `0x423e40` | `0x242` | `0x4249d0` |
|  |  |  |  | `0x243` | `0x4182d0` (417xxx) |

(`0x2bd`→`0x4251c0`, `0x2bf`→`0x425240`. The handful of `0x417xxx` handlers are trivial/marker-shaped — the
default-handler neighbourhood — and are almost certainly no-ops or arg-poppers; triage before modelling.)

**Contract (completed 2026-07-10, representative ops `0x21e`/`0x220`, both `argc 6`):** these are
independent matrix channels, not two encodings of one vec3 property.

- `0x21e` normalizes operands 4–6, then `gfx_object_set_scale_channel` (`0x47eaa0`) stores timing at
  `obj+0x3c/+0x50` and calls `0x48af1d`, which writes the three values onto a 4×4 matrix diagonal at
  `obj+0xac`: a **scale matrix**.
- `0x220` passes raw operands 4–6 to `gfx_object_set_translation_channel` (`0x47ecc0`), stores timing at
  `obj+0x44/+0x58`, and calls `0x48afb1`, which writes them into matrix entries 12–14 at
  `obj+0x1ac`: a **translation matrix**.
- `0x21f` converts operands 4–7 to floats and calls `gfx_object_set_rotation_channel` (`0x47eb70`). It
  stores delay/duration at `obj+0x40/+0x54`, target axis at `obj+0x1f8..0x200`, target angle (degrees)
  at `obj+0x208`, and the target axis-angle matrix at `obj+0x12c`. Current axis/angle are
  `obj+0x1ec..0x1f4/+0x204`, with current matrix `obj+0xec`.
- `gfx_object_apply_transform_channels` (`0x472f00`) supplies the timing contract. All three channels use
  shared start timestamp `obj+0x34` and global frame-time `ctx+0xb550`, but have independent delay/duration:
  scale `obj+0x3c/+0x50`, rotation `obj+0x40/+0x54`, translation `obj+0x44/+0x58`. Each holds current through the
  delay, linearly interpolates current→target for its duration, then commits the target and clears its own timing.
  Neither third component is opacity.

**Exact composition and 2D reduction (live-validated 2026-07-10).** The one-shot consumer starts from identity and
right-multiplies `T(-V18) → scale-current → rotation-current → translation-current → T(+V18)`;
`matrix4_multiply` at `0x4ee2a4` computes `out = left * right`. AGE uses row vectors. With no
rotation/perspective, the screen projection is therefore exactly
`V18.xy + (point.xy - V18.xy) * scale.xy + translation.xy`. The captured SC0000 handle `0xcbc0`
has base `(0,600)`, anchor `(400,1000)`, and final scale `(5,5)`; native matrix translation
terms are `(-1600,-4000)`, projecting the base point to `(-1600,-1000)`. The port's focused
projection test and transform-aware gfx log reproduce those values.

`gfx_object_composite` then right-multiplies `gfx_object_anim_interpolate`'s separately anchored product,
which contains op `0x234`'s cyclic rotation. With the other oscillating matrices at identity, adjacent anchors
cancel and the full order is
`T(-V18) * scale * one-shot-rotation * translation * cyclic-rotation * T(+V18)`. Thus cyclic rotation
also rotates the translation vector. The cyclic angle is integer degrees
`floor(((frameTime-start) % period) * 360 / period)`; it wraps to zero without ping-pong. Positive Z produces
`m01=+sin, m10=-sin`, clockwise on the Y-down screen.

Native matrix oracle: handle `0xcb8e`, anchor `(700,600)`, scale current `0.9`, op `0x21f` target axis
`(0,0,1)`/30° after 500 ms for 390 ms, sampled 11 ms into the ramp as
`[0.9055,0.0134;-0.0134,0.9055]` with translation `(74.1449,47.3127)`. The port focused test matches
those terms. In the windowed port capture, the two SC0000 `0x234` sites (periods 9000/13000 ms, Z axes
`+1/-1`) advanced after 563 ms to integer angles `22/15`, exactly the native formula, and produced distinct
affine PNG frames. Nearest-neighbour inverse mapping is the deliberate software raster sampling policy;
native D3D9 subpixel filtering remains a possible pixel-level difference, not an uncertain matrix approximation.

**Port result (2026-07-10):** `GfxState` retains scale, one-shot rotation, translation, and cyclic rotation
with their native clocks/order. `Transform2DMath` composes the full row-vector 4×4 transform before 2D
projection. Godot uses an inverse-mapped affine RGBA8 rasterizer for textured objects and solid fills,
preserving colorkey/tint/opacity behavior and never deriving opacity from transform Z.

##### `anim_start`/`set_anim_clock` decoded + opening confirmed (2026-07-07, animation-slice Task 1)

Decoding the two already-named clock/start ops (dispatch table → `0x234`@`0x00423da0`, `0x238`@`0x004240e0`;
both annotated) and grepping the SC0000 opening settles the animation model and confirms the opening exercises it:

- **`0x238 set_anim_clock` (argc 1, cmd-type 3):** `ctx+0x51b78 = 0` (elapsed), `ctx+0x51b7c = operand1`
  (total duration). **A GLOBAL, NON-BLOCKING clock** — not per-object. The op only *configures* the clock; it
  does **not** loop/wait. The native render loop advances this clock each frame and interpolates *all* animating
  objects. Its own plate comment states the payoff: "our port can drive animation in the host's per-frame loop
  while the VM is parked at wait-for-input; no VM/host frame-lockstep." → **validates the wall-clock-tween
  architecture directly.** SC0000: `set-anim-clock(G[0x624bb])` @`0x123bd`, `set-anim-clock(0x190=400)` @`0x13858`.
- **`0x234 anim_start` (legacy mnemonic; argc 5, cmd-type 0xb):** following worker
  `gfx_object_set_rotation_cycle` (`0x47f060`) into `gfx_object_anim_interpolate` (`0x473ed0`)
  corrects its ABI to `(handle)(period_ms)(axis_x)(axis_y)(axis_z)`. Period is `obj+0x228`, axis is
  `obj+0x244..0x24c`, and the frame-clock consumer applies
  `360*((now-start)%period)/period` degrees. This is a cyclic **rotation** channel, not a target vec3
  for scale/translation and not opacity.
- **The opening path uses the whole subsystem, early.** `0x21e`/`0x220` transform-sets fire from `0x00f73`
  onward (`0x21e (G[0x6245b]) 0 0x12c l0 l1 0x64`, `0x220 (G[0x62457]) 0x96 0x3e8 l1 l3 0`), on the same INIT2 CG
  handles (`G[0x62457]`,`0x6245b`,`0x6245c`) — this is the opening, **not** battle/debug. So the slice's ops are
  real and verifiable on screen.

**Corrected host model:** `0x21e` scale and `0x220` translation run directly from frame-time
`ctx+0xb550` using their own delay/duration; they do not use op `0x238` as their duration.
Op `0x234` is the independent rotation cycle above. Op `0x238` still configures the separate
`ctx+0x51b78/+0x51b7c` animation service used by its own family.

##### `label_1235a` animation-section boundary (2026-07-10)

The section helper computes the maximum configured duration and arms it with `0x238`, then reads
message-skip through `0x1c7` and ADV read/click-skip state through `0x1cc`. The raw `jcc` order matters:
normal playback (both zero) branches to `0x21c` (set run-state bit `0x400`), while a nonzero skip/read
state executes `0x243` (reset the separate global animation-service clock) followed by `0x20c` present.
Both branches finish with
`0x224`, which clears the native gfx command queue at `ctx+0x418`. These handlers are now named,
commented, and saved in the Ghidra image.

The matching native presentation trace corrects the earlier cadence model. Ordinary opcode execution is
**burst-fast between presentation services**, while `0x21c` is the render/wait boundary: it parks the
interpreter and `gfx_render_frame` repeatedly samples visible finite one-shot channels and queued surface
commands until dirty presentation state clears; `0x224` then clears the command queue. `0x20c` is a single
explicit publication on the skip branch.

At the opening AE001D passage, native `0x125a6` rendered the preceding state, then both object binds plus
mode-1 `0x203` and target `0x202` writes (`0xd5a/0xd63/0xd73/0xd8a`) completed in about 5 ms with **no render
between them**. Their first composition was the following repeated `gfx_render_frame` loop at `0x21c`.
Likewise, the explicitly presented mode-0 white CG at `0x125a6` survived only about 10 ms before that next
boundary. The port's former 200-completed-op/s throttle stretched the same burst across many display frames;
that average had folded service waits into execution time and was not an opcode scheduler rate.

The Godot host therefore leaves ordinary `FrameYield` non-blocking and publishes retained mutations only at
`0x20c`, `0x21c`, sleep, and stable input waits. `0x21c` waits for visible finite color/matrix channels plus
`0x223`; ambient cyclic/spritesheet pulses do not block, and click forcing remains restricted to `0x223`.
This is a native-evidenced scheduler correction, not a guessed duration sleep.

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

### `sleep` (op `0xc8`) — the frame-pacing primitive (2026-07-08, decoded)

Handler resolved via the dispatch table (`ctx[0x26c93+0xc8]` = `param_1[0x26d5b]` in `FUN_00413860`) →
**`sleep_op_0xc8`@`0x420ec0`** (was `LAB_00420ec0`; created + annotated). It is **NON-BLOCKING**:

- It **arms a timer** — `sleep_timer_arm`@`0x44cff0` on the object at `ctx+0x5f304`: `+8 = 1` (active),
  `+0x14 = (*DAT_0056f3d4)()` (start tick — an **ms** source, `timeGetTime`/`GetTickCount` class, same
  `DAT_0056f3d4` the boot uses to seed `srand` via `time/100`), `+0x18 = duration` (operand, min 1). The engine's
  main loop polls `elapsed ≥ duration` and resumes the script — rendering continues in the meantime. This is the
  native confirmation that the engine paces animation in its per-frame loop, not by blocking.
- **Operand unit = MILLISECONDS.** `duration < 10` fast-paths through import `[0x56f0b8]`; every real scene sleep
  (`100`/`750`/`1000` in SC0000) is `≥ 10` → the timer-arm path.
- The handler also writes gfx **cmd-type 3** into the current object record (`ctx+0x53d88+curidx*0x78`) and runs
  two **anti-tamper** checks (call `[ctx+0x5512c]`; a rotate-checksum compare of `ctx+0x55120/0x55124`;
  `__CxxThrowException` on mismatch — integrity work piggybacked on a hot op). Neither is needed by our model.

**Port equivalent (implemented):** our VM runs on a background thread (like `wait-for-input`), so blocking that
thread for `duration` ms while the main-thread compositor (`Main.Recomposite` in `_Process`) keeps presenting is
behaviorally equivalent to the native non-blocking timer. This correctly reproduces the **explicit one-shot
sleeps** (the dramatic 1000/750/200 ms holds). Headless/CLI hosts no-op `Sleep` (parity). `IHost.Sleep(long)` +
VM `case "sleep"`; see `vm-map/opcodes.toml` 0xc8. **It does not pace ordinary opcode bursts.** The later
native presentation trace proved that back-to-back retained mutations execute within milliseconds and are
first published at the following `0x20c`/`0x21c`, sleep, or stable input boundary. An earlier claim that the
opening was generally sleep-paced was inherited without execution evidence and is superseded.

**Related — `present-frame` (op `0x20c`):** dispatch `param_1[0x26e9f] = gfx_op_0x20c_present_frame` →
`gfx_render_frame`@`0x4820b0` (buffer flip). The port treats `0x20c` as an explicit retained-state publication
boundary in the interactive host while keeping it `noop_headless=true`; the Kelebek label `u00416200` was
VA-drift. This supersedes the earlier host-implicit/no-op presentation model.

### Frame cadence — the interpreter tick, and why our port "speeds through" (2026-07-08)

**Historical reconstruction, corrected by the synchronized presentation trace below.** This pass correctly
identified the one-op interpreter tick and ADV Ctrl state, but incorrectly inferred a constant engine-level
opcode cadence. The outer native service invokes that tick in bursts and publishes retained state only at
specific service boundaries.

**Confirmed from the engine image (annotated in Ghidra):**

- **The interpreter is a cooperative one-op-per-tick step, not a run-to-completion loop.**
  `adv_interpreter_tick`@`0x410fb0` (renamed from `FUN_00410fb0`) executes **exactly one opcode** per call:
  `op = **(ctx+0x53d2c + curCtx*0x78)`; if `0 ≤ op ≤ 0x3ff` it dispatches `(*(ctx+0x9b24c+op*4))()` (the
  handler table = `ctx[0x26c93+op]`) then advances `PC += *(ctx+0x53d88+curCtx*0x78) * 4` (decoded cmd size),
  else the default handler `FUN_004162b0`. It also runs the **message-skip / click / auto-advance** logic each
  tick (`s_set_CancelMesSkipOnClick`, `s_message_ReadTextSkip`, skip bit `ctx+0xa0ce4 & 0x8000000`) — i.e. the
  **Ctrl fast-forward governor lives at the per-op level**, and a click can reposition the PC (skip-to-next).
- **Script contexts are coroutine records.** `curCtx = *(ctx+0x53d14)` indexes `0x78`-byte records at
  `ctx+0x53d60`/`ctx+0x53d2c` (PC, codebase, cmd-size). The engine multiplexes script "threads." Init/reset =
  `scene_context_init_reset`@`0x40b3b0` (zeroes `0x53d14` + `0xa0ce4`, allocs surfaces `ctx+0x52bd4[1000]`).
- **Advancement is gated by an interpreter run-state flags word `ctx+0xa0ce4`** (bit1 = sleeping, plus wait/
  skip/etc.), read+written by ~40 state functions. `sleep_op_0xc8` sets bit1 + arms the ms timer and returns —
  it does not block. So the outer loop consults `0xa0ce4` to decide whether to step the script this frame.
- **Effects are frame-stepped.** Screen transitions `FUN_0043cdb0` (12 wipe/slide modes) render **one frame per
  step** and take a step-count parameter (the natural place a speed multiplier applies); `present-frame` (0x20c)
  and the anim clock (0x238) advance per frame. A CG transition therefore spreads over many real frames.
- **Timing source** = the ms-clock function pointer `*DAT_0056f3d4` (`timeGetTime`-class), used throughout.

**Model proposed at this stage (partially superseded):** single-threaded frame loop; each service pass steps
opcodes until the context **yields**
(`sleep` armed / `wait-for-input` 0x72 / active frame-stepped transition/anim / present), renders
(`gfx_render_frame`), waits on the clock, continues. Back-to-back draws inside one page compose into a single
frame (fine); the opening's CG-to-CG advances are gated by frame-stepped transitions + sleeps, which spread
them over real time.

**⚠ Not statically resolvable (honest boundary):** the **outer frame loop itself** is not readable from this
dump. `adv_interpreter_tick` is invoked through a **runtime-set mode function pointer** (heap/vtable slot) — it
has zero static xrefs, and its address bytes (`b0 10 41 00`) appear nowhere in `range_00400000` (0x400000–
0x65ffff). The functions touching the scheduler state (`0x53d14`, `0xa0ce4`) are init/reset, save
(`context_state_serialize`@`0x40d320`), and op-handlers — never the loop. The "run-until-yield then render"
statement above is a **reconstruction** from those pieces, not a line read from the loop; pinning the actual
loop + its exact per-frame step budget / vsync wait needs a **live-debugger break** (attach + break in the
frame loop), or a wider memory dump that includes the mode object.

**Historical port conclusion (superseded):** this pass prescribed a bounded wall-clock opcode rate and led to
the frame-stepped implementation documented in the historical spec. The 2026-07-10 native trace disproved that
rate model: ordinary opcode work must remain burst-fast, while `0x20c`, `0x21c`, sleep, and stable input waits
own publication/pacing. Commit `85fc07d` implements the corrected boundary model.

### Frame cadence — live measurement (2026-07-08, Frida read-only)

The static pass couldn't reach the outer loop, so we measured the running game. **Read-only / import-only
only** (`tools/frida/probe_frame_cadence.py`, `probe_present.py`): a plain-JS hook on the proven operand-fetch
`0x41b940` (grab ctx + count exec rate) + system-DLL hooks; no engine-code patching. **Lesson learned the hard
way:** a first attempt with a **CModule** hook on the hyper-hot `adv_interpreter_tick` crashed the game
instantly (bad native callback into the hottest path — *not* anti-tamper; our other scripts hook engine code
via plain JS and survive). Use plain-JS hooks on proven addresses + memory polling.

Findings, corrected by the later synchronized presentation trace:
- Normal active capture measured about **1,788 operand fetches/sec** (peak ~5,796). This is neither a
  completed-opcode count nor a scheduler budget; it mixes burst execution with native-service parking.
- **Fast-forward (Ctrl)** raised operand-fetch activity about 4× (≈7,738/sec avg, peak ~15,572), gated by
  **`ctx+0xa0ce4 & 0x8000000`**. This remains useful evidence that Ctrl is ADV-scoped, but it does not define
  a constant opcode cadence.
- Rendering is Direct3D 9 and uncapped. The candidate D3D9 `Present` hook fired around 1,908/sec in the
  original probe; there is no fixed display refresh boundary to copy into the port.
- **Final scheduler implication (2026-07-10):** ordinary opcode work is burst-fast between explicit native
  service boundaries. AE001D bind + mode + color-target writes completed within ~5 ms with no
  `gfx_render_frame`; rendering began only at `0x21c`. The old 200-completed-op/s calibration folded service
  waits into script time and is discarded. Godot `FrameYield` is non-blocking; `0x20c`, `0x21c`, sleep, and
  input own publication/pacing.

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
"full boot" should run both. **Historical residual (subsequently resolved):** this capture still had opaque
`AE*` fade/flash effects and zero-anchor object-slot CGs. The later blend, geometry, animation, and retained-
presentation subsections below supersede that state; default object geometry remains confirmed `(0,0)` in
`gfx_object_init_default`.

### The gfx animation/effects subsystem — the `AE*` fades (2026-07-07)

The `AE*` flash/glow effects (and sprite motion) are a **native time-animated retained render loop**, not
per-frame bytecode. Reversed + annotated in Ghidra:
- **Retained objects carry cyclic rotation state:** flag value `4` at `obj+0`, start timestamp
  `obj+0x214`, period `obj+0x228`, and axis `obj+0x244/248/24c`.
- **`gfx_object_set_rotation_cycle`** (`0x47f060`, worker for legacy op **`0x234`**)
  configures that channel. `gfx_object_anim_interpolate` consumes it from frame-time `ctx+0xb550`
  as a repeating 0..360-degree axis rotation. Op **`0x1fd`**
  (`gfx_op_0x1fd_set_vec_scaled@0x422650` → `gfx_object_set_scale_current@0x47e6b0`) is an immediate
  current-scale setter: it divides integer X/Y/Z percentages by 100, marks scale state at `obj+0x68`, and
  writes `matrix4_make_scale` at `obj+0x6c`.
- **Op `0x238`** (`gfx_op_0x238_set_anim_clock`) sets a **global animation clock**, **non-blocking**:
  `ctx+0x51b78 = 0` (elapsed), `ctx+0x51b7c = duration` (the max per-object duration; SC0000 `label_1235a`
  maxes a table to compute it). It does **not** loop/wait.
- **Frame model:** the bytecode does `configure anims (0x234/0x1fd) → set clock (0x238) → show-text →
  wait-for-input` and **continues**; the native render loop advances the clock + per-object progress each
  frame, interpolates, composites, presents. Render/present family nearby: `0x243/0x20c/0x21c/0x224`
  (`u004162xx`, not yet fully RE'd). **⇒ the port can drive animation in the HOST per-frame loop while the
  VM is parked at wait-for-input — no blocking present op, no VM/host frame-lockstep** (the answer to the
  "frame loop" question).

Consequence: animation needs a retained per-frame compositor. That architecture is live; scale,
one-shot rotation/translation, and cyclic rotation now rasterize through the affine software path.

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
  NOT a snapshot. Objects also carry anchor V18 (`obj+0x18`), independent scale and translation
  matrices/timing, cyclic rotation state, and color/alpha (`0x202/0x203`).

**Render frame** — `gfx_render_frame` (`0x4820b0`), driven by op `0x20c` present (`gfx_op_0x20c_present_frame`
`0x4174a0`, which also updates the frame timer `ctx+0x51b64/68`): iterate the object registry **in ascending
handle order — that IS the z-order** (lower handle behind, higher on top; `std::map` key order). For each
object with visible bit 0, `gfx_object_composite` (`0x47f650`) computes its transform from geometry, **applies
the animation interpolation if bit 2 is set**, and blits `surface[obj.slot]` with alpha/colorkey. Then swap
buffers (present). **Slot 0 is NOT special** — a normal slot; several objects may share one surface.

**⇒ Faithful port:** a `SurfaceStore` (`slot → {image, colorkey}`, from create/set-texture) + an `ObjectStore`
(`handle → {slot, srcRect, position, anchor, scale, anim, alpha, visible}`, from draw-texture + the gfx ops) +
a host per-frame compositor that draws visible objects **in ascending-handle order** from their live surface,
interpolating animations by elapsed time. No VM/host lockstep: animations play during the wait-for-input park.
Separate scale/rotation/translation state and timing are implemented. Anchor semantics, multiplication order,
cyclic wrapping, 2D projection, and affine raster coverage have focused native-oracle tests. Native D3D9 filtering
and render-target command execution remain separate fidelity work.

### Blend & transparency — colorkey + `0x202`/`0x203` color/alpha (2026-07-08)

Reversed for graphics slice A (spec `docs/superpowers/specs/2026-07-08-blend-transparency-design.md`;
Ghidra functions renamed + plate-commented, saved).

- **Colorkey format** (`gfx_op_0x1f9_load_surface` `0x422360`): read op arg 3; if `(int)key < 0` → **no
  colorkey** (opaque); else the operand is **`0xRRGGBB`**, converted to `0xFFRRGGBB` and passed to the
  surface creator `FUN_00477c40` as the transparent key (so operand `0` = key **black**). Colorkey is baked
  **at surface-load** (matching texels → transparent), NOT compared per-blit. *Port:* interpret arg 3 as
  RGB888; `<0` = none; else texels whose `(R,G,B)` equal the key become transparent when the surface image
  is loaded/cached.
- **`0x202` (`gfx_op_0x202_worker_set_color_anim` `0x47ea00`)**: sets an **animated** color/alpha target
  `obj+0x64 = packedARGB`, the color-anim active bit, and resets shared start `obj+0x34=0`. Operands 2/3
  are delay/duration at `obj+0x38/+0x4c`; sampling uses frame clock `ctx+0xb550`, not op `0x238`.
- **`0x203` (`gfx_op_0x203_worker_set_color` `0x47e9b0`)**: sets a **static** color/alpha `obj+0x60`, no anim
  bit. Immediate per-object modulation.
- **Blit** (`gfx_object_blit_d3d9` `0x4774c0`): selects a **blend mode** (`local_2c`: 0 opaque, 1 alpha
  `SRCALPHA/INVSRCALPHA`, 2/3 additive/special for glow/flash) and passes a modulation color/alpha to the
  device draw. Slice A ports the **alpha** path (fades); additive (glow) is deferred (its `local_2c` source
  field is `obj+0x30`, the value written by op `0x203`. Mode 1 uses packed ARGB alpha as opacity and RGB
  as multiplicative D3D modulation. For a **textured mode-0** object, preserved `0xffffffff` is opaque
  identity, not a request to replace the texture with white; the packed alpha byte is therefore not a
  generic tint-strength control. Modes 2/3 remain separately scoped beyond the completed mode-1 path, and
  surfaceless mode-0 fills remain a distinct consumer case.

**SC0000 third-CG white-screen and missing-glow fix (2026-07-11).** The page containing
`大役を担ったのは…` reaches the intended EV052DA image and both animated AE001D layers. A synchronized
Godot capture proved AE001D is correctly alpha-bearing and drawn in mode 1 at only 6-8% object opacity;
it was not the white wall. During the preceding `0x223` EV052CA→EV052DA crossfade, base handle `0xcb2a`
renders EV052DA in mode 2 with identity modulation. When the transition ends, `0x203@0x12478` restores that
same textured object to mode 0 with negative color operands, preserving `0xffffffff`. Native keeps EV052DA
visible; the old port resolved the state as `tintStr=1.00` and lerped every source texel to white. Textured
mode 0 now resolves as opaque multiplicative RGB, making white identity, while surfaceless mode-0 fill
strength remains separate.

The first corrected capture exposed a second independent gap: AE001D was submitted and rotating but had no
visible pixel contribution. Its 800×800 circle was based at `(0,550)` around anchor `(400,950)` and remained
at the port's default 100% scale, almost wholly below the viewport. SC0000 executes `0x1fd` with
`210/210/100` and `240/240/100`; implementing the native immediate current-scale setter expands the circles
into the viewport (the first top edge reaches y=110). The user positively confirmed both the restored CG and
visible glow on the ordinary interactive path. This glow is a retained scaled/rotating texture, not an
`0x231` spritesheet-cell animation; later missing spritesheet effects remain a separate issue.

**Interpolation RE pass (2026-07-08, stalled → both deferrals confirmed).** Attempted to pin how a `0x202`
fade animates so smooth ramping could join slice A. Findings (Ghidra `gfx_object_anim_interpolate`
`0x473ed0`, annotated+saved): the bit-2 anim family (op `0x234`) interpolates **5 independent sub-channels**
(color `obj+0x240`/period `obj+0x220`, two matrices, rotation `0x168`=360°, src-rect scroll), each on the
**global frame clock `ctx+0xb550`** (advanced per present, NOT the op-`0x238` clock `ctx+0x51b7c`), and each
**ping-pongs** (triangle wave, folded at period/2) — i.e. these are *oscillating/pulsing* effects, not
one-shot fades. The `0x202`/`0x203` color (`obj+0x60` static / `obj+0x64` animated, sets
`ctx+0xb558`/`0xb560`) is a **separate channel whose blit consumer was not located** in this pass. So a
one-shot fade's exact source→target→easing is still unresolved and would take a dedicated dig (find the
`obj+0x64` consumer + the color→`obj+0x240` path + the clock advance). ⇒ **smooth color-anim interpolation
stays deferred**; slice A ships the static end-state (which reaches the correct final alpha/tint and fixes
the stuck-opaque bug), with interpolation as a scoped follow-up.

**Resolution (2026-07-10 — supersedes the deferral above).** The missing consumer was the bit-1 branch in
`gfx_object_apply_transform_channels` (`0x472f00`), before its matrix channels. It seeds shared start
`obj+0x34` from `ctx+0xb550`; holds current packed ARGB `obj+0x60` through delay `+0x38`; then performs an
integer, bytewise linear interpolation to target `+0x64` for duration `+0x4c`. At natural completion—or
when `ctx+0xb55c == 1` requests forced completion—the target commits to current, delay/duration clear,
target becomes `0xffffffff`, and the one-shot active bit clears when no color/matrix/src-rect sibling remains.
The object-local override bit at `+0x2d0` suppresses the global force. Negative alpha/RGB target operands
independently preserve their bytes from current `+0x60`.

The port now carries current and target separately and samples them from the unified `FrameClock`; an op
`0x203` static write after `0x202` therefore becomes the ramp's current value rather than overwriting its
target. Mode 0 retains the established CG/tint/fill behavior; mode 1 now uses native alpha opacity plus RGB
modulation. `draw-string 0x204`/`0x7a` remains a separate dependency.

**ADV chrome correction (2026-07-11).** Mode 0 cannot be classified from the final packed color alone.
Static `0x203(mode=0, alpha=0, rgb=white)` remains the established opaque/no-tint CG initializer, but a
mode-0 object whose current/target channel was armed by `0x202` consumes that sampled ARGB as opacity plus
multiplicative RGB modulation, including after target commit. SC0000 proves the distinction with SYSTEM4's
SO001 surface: backing object `0xd2f0` ramps `0x00000000 <-> 0xff000000`, while control-strip object
`0xd2f1` ramps `0x00ffffff <-> 0xffffffff`. Treating alpha as tint strength made the visible controls solid
white and the hidden backing expose SO001's raw white crop. Preserving one-shot provenance makes white an
identity modulation for the yellow controls and alpha zero fully transparent. A matching windowed capture
and user manual check confirmed both endpoints.

### SC0000 anim/transform/spritesheet cluster — op→field map (2026-07-08)

Reversed for the animation cluster slice (spec `docs/superpowers/specs/2026-07-08-sc0000-anim-transform-cluster-design.md`).
Every cluster handler resolved via the dispatch table `handler(op)=ctx[0x26c93+op]` (the `opcodes.toml`
`u004xxxx` labels are Kelebek VA drift — do not use them). Each op is a thin wrapper (`FUN_0041b940(n)`
fetches operand n) → a worker that writes object fields; the interpolator `gfx_object_anim_interpolate`
(`0x473ed0`) is the consumer. **The cluster is heterogeneous** — setters, queries, and a movie op. Renamed +
annotated in Ghidra, saved.

**In scope (built this slice):**
| op | handler / worker | semantics |
|---|---|---|
| `0x22f` | `gfx_op_0x22f_set_position_anim` → `gfx_worker_set_translation` | set object **position** (translation vec `obj+0x5d4`); base transform, not a ping-pong channel |
| `0x229` | `gfx_op_0x229_set_position` (`FUN_00472bb0`+`FUN_00472be0`) | set object **position/geometry** immediately (`obj+0x420/0x424` + vec `obj+0x440..0x448`) |
| `0x239` | `gfx_op_0x239_set_srcrect_cell` → `gfx_worker_set_srcrect_cell` | one-shot **spritesheet-cell** channel: delay/duration `obj+0x48/+0x5c`, total frames/columns `obj+0x238/+0x23c`, target frame `obj+0x234` |
| `0x231` | `gfx_op_0x231_anim_srcrect` → `gfx_worker_anim_srcrect` | looping **spritesheet-cell** channel: milliseconds per frame `obj+0x230`, total frames `obj+0x238`, columns `obj+0x23c`; row-major and wraps, not ping-pong |
| `0x232` | `gfx_op_0x232_anim_color` → `gfx_worker_anim_color` | **animate color**: bit2 active, period `obj+0x220`, target `obj+0x240` → interpolator COLOR channel (ping-pong). Negative alpha/RGB preserve corresponding bytes from static color `obj+0x60`; alpha >255 clamps. Distinct from one-shot `0x202`/static `0x203` |
| `0x228` | `gfx_op_0x228_query_position` → `gfx_object_query_translation_target` (`0x47cdd0`) | **query** the decomposed target-translation matrix (x,y,z), `obj+0x1ac/+0x1b0/+0x1b4`, → operand slots 3/4/5; success is 0 and missing is 1 |
| `0x23f` | `gfx_op_0x23f_query_object` (`FUN_0042a520`) | **query** an object status/value → operand slot 1 |

**Follow-up resolution (2026-07-10):** `0x21f` is the one-shot axis-angle channel and is implemented with
affine rasterization. `0x223` is **not affine**: `gfx_queue_surface_alpha_transition` (`0x47f440`) inserts
a type-0 command-map record keyed by arg 1: start `+4`, delay/duration `+8/+0xc`, target surface slot `+0x10`,
and two object handle ranges at `+0x14/+0x1c` and `+0x18/+0x20`. `gfx_render_frame` composites those ranges
into the target and ramps alpha 0→1. Its SC0000 site `0x129e7` passes `(handle+2, transition slot,
handle+1,1,handle,1,G[0x6249f],G[0x624a0])`. It remains a render-target/transition slice dependency rather
than being approximated in the affine object compositor.

#### ADV foreground surface-transition lifecycle (2026-07-10)

The completed native chain is `0x21d` snapshot -> `0x223` queue -> `label_1235a` skip queries ->
`0x21c` normal yield or `0x20c` skip endpoint-present:

- `gfx_object_clone` (`0x47e4f0`, op `0x21d` handler `0x423310`) copies the complete retained-object
  record: exactly `0xb5` dwords / `0x2d4` bytes. SC0000 `0x128fc` clones the current CG handle to
  `handle+1`; the loader then rebinds the source handle to the new CG, and `0x223 @ 0x129e7` uses
  `handle+1` as old range A, the updated handle as new range B, and `handle+2` as the target presenter.
- `0x1c7` is the `run_state_flags & 0x08000000` message-skip query. `0x1cc` reads
  `ctx+0x6dbd4`, now named `adv_read_skip_state`; `adv_refresh_read_skip_state` (`0x406cd0`) and the
  text/label/wait handlers maintain it from `message_ReadTextSkip` plus per-PC read history. It is not
  surface-transition progress.
- Zero OR-state is normal playback and reaches `0x21c`; run-state bit `0x400` parks interpreter
  progression while the type-0 surface command advances from the frame clock. Nonzero skip/read state
  reaches `0x243 + 0x20c`, exposing the completed endpoint without the normal wait.
- Op `0x203` stores operand 2 at object `+0x30`. Transition sources use mode 2; their common
  `0xffffffff` color is opaque identity modulation, not a solid-white tint. Negative alpha/RGB operands
  separately preserve the corresponding byte(s) of current static color via the `FUN_0047f3e0` lookup.

The port mirrors this with an explicit pending/start/progress/natural-or-forced-complete state. A click
while `0x21c` is parked completes only the active foreground surface transition and is consumed; it does
not pre-arm the following `wait-for-input`, and it does not complete independent retained rotation,
matrix, spritesheet, or color-animation channels.

**Source-rectangle correction (2026-07-11):** `gfx_object_anim_interpolate` preserves the source rectangle
set by `draw-texture`; `0x231` does not divide that rectangle by its operands. It computes
`frame = floor(elapsed / frame_period) % frame_count`, then offsets both X bounds by
`rect_width * (frame % columns)` and both Y bounds by `rect_height * (frame / columns)`. SC0000 passes
`(100, 8, 4)` for AE001H: eight 200x200 cells in an 800x400, 4x2 sheet. Treating 8 and 4 as grid width
and height shrank the crop to 25x50 and made the intended cave spirit effectively disappear.

**AE001H travel-path correction (2026-07-11):** the three post-movie movement legs exposed a separate
port bug from the source-rectangle correction. Native `0x228` copies the full retained-object record and
decomposes the target matrix beginning at `obj+0x17c`; the returned translation comes from
`obj+0x1ac/+0x1b0/+0x1b4`. It does **not** return the draw/base position at `obj+0x24` (`V24`). SC0000 uses
that query before each `0x220` to derive the next translation target. AE001H's draw base is `(360,20)` and
its initial translation target is `(0,0)`, so the first native leg targets `(40,-20)`. The current C#
`0x228` implementation instead returns `(360,20)`, producing `(400,0)`; the compositor then adds that
translation to the unchanged draw base, placing the object near `(760,20)` before its cyclic rotation.
That explained the observed immediate off-screen movement and why later legs never visibly returned. The
port now queries `TranslationTarget` independently of `V24`, returns the native status convention, and
leaves output operands untouched when the object is absent. A bytecode-level regression reproduces the
three SC0000 targets `(40,-20)`, `(50,-80)`, and `(130,-100)` while retaining base `V24=(360,20)`.

**AE001H white-pulse verification (2026-07-11):** the eight source frames contain only the expected
purple artwork; the white wash is introduced by the port's `0x232` path. SC0000 `0x1a0e` explicitly arms
`0x232(handle,1200,224,-1)`. Native `gfx_op_0x232_anim_color` treats negative RGB as a sentinel and fetches
the object's current static packed color before constructing the target. The C# dispatch instead calls
`PackColor(224,-1)`, whose RGB mask becomes `0xffffff`, and `SnapshotVisibleObjects` maps animated alpha
to mode-0 tint strength. Existing retained-state evidence shows `tintStr` cycling `0 → 0.82 → 0` on
AE001H, exactly matching the reported white pulse. Follow-up native dataflow closes the remaining question:
fresh objects initialize static color `obj+0x60` to `0xffffffff`; the interpolator samples that temporary
color toward target `obj+0x240`, then `gfx_object_composite` passes the sample and the unchanged blend
selector `obj+0x30` to `gfx_object_blit_d3d9`. Mode 0 enables no alpha blending and uses RGB only as vertex
modulation, while mode 1 enables SRCALPHA/INVSRCALPHA. AE001H therefore cycles
`0xffffffff ↔ 0xe0ffffff`: identity RGB throughout, with alpha intentionally inert in mode 0, so native has
no visible pulse. The faithful fix is now fully bounded: initialize/resolve static color correctly, sample
packed ARGB, and consume it through the existing mode-specific blend path instead of converting animated
alpha into tint strength. The port now initializes static color to native identity `0xffffffff`, resolves
negative operands in `SetColorAnimResolved`, samples packed ARGB before blend selection, and feeds it through
the existing mode-specific path. Exact AE001H, mode-0 RGB-modulation, and mode-1 alpha regressions cover the
contract; the white pulse is removed without suppressing the scripted channel.

**Resolved 2026-07-11:** `0x236` is the movie-to-retained-surface path described below. `0x242` is now
classified as the retained-object `+0x2d0` field setter described above; `0x23d/0x20a/0x20e` remain GAP.

### Movie-to-surface opcode `0x236` (2026-07-11)

The exact ABI is `play-movie-to-surface(resource_id, surface_slot, movie_flags, sync_mask)`. Handler
`op_0x236_play_movie_to_surface@0x423ee0` is command type 9 and requires the destination texture to exist.
It allocates/reuses a 0x478-byte `CMovieToTexture` object, binds the D3D device/backing texture, opens
operand 1 through `asset_open_indexed_entry`, constructs a DirectShow FilterGraph, and starts it. The graph
queries `IGraphBuilder`, `IMediaControl`, `IMediaPosition`, `IMediaEvent`, and `IBasicAudio`; its custom
`CMovieTextureRenderer` accepts RGB samples and copies the bottom-up frame into the retained texture.

Operand 3 is retained movie mode plus sound-route policy. Bits `0x10000/0x20000/0x40000/0x80000` force
sound route 0/1/2/3; without an override, native setting `set:DependMovieSound` supplies the route. This
slice deliberately does not implement the audio branch. Operand 4 is stored at movie object `+0x42c` as
the sync/device mask; it is not a duration or loop count. Replacing or releasing the owning surface stops
the graph and detaches the renderer.

Graph construction/open is synchronous, but playback and sample delivery are asynchronous. The handler
returns normally and the interpreter advances one instruction: at SC0000 `0x13c8`, the native operand
capture evaluates `(0x33, 0, 2, 0)` and the next executed bytecode is `0x13d1`. The movie therefore does
not itself block the VM. SC0000 prepares additional static layers, then reaches `0x21c` through
`label_1235a@0x1574`. That opcode sets run-state bit `0x400` and yields the interpreter; the presentation
service continues sampling the retained movie until DirectShow EOF, after which the following script
cleanup releases it. The static preparation before `0x21c` is not a movie teardown boundary.

**Manual-test corrections (2026-07-11):** the initial port incorrectly treated pre-yield static loads as
surface replacement, producing start/first-frame/stop all in render frame 0. The bounded host now retains
the movie through `0x21c` until DirectShow completion. A follow-up compositor trace proved the movie object
was present at the correct z-position, but the static AGF cache reused the first decoded sample forever
because every sample has the same `(assetId,colorKey)`. An unconditional current-sample backbuffer copy did
not help because normal retained composition immediately covered it with that cached first sample. Movie
surfaces now bypass the static cache and publish only through the retained object. A windowed run records
first frame 101 and stop frame 190; the user manually confirmed visible playback. The separate lower white
textbox-area object remains outside this finding, as does the deliberately unrendered movie audio stream.

The `/v2` image names/comments the handler; movie ctor/interface/open/play/volume/release workers; sound
route helpers; renderer media-type/sample workers; and stop/detach/destructor lifecycle. The image was
saved after annotation.

### Grey-background root cause — slot collision + tint-strength (2026-07-08, gfx-log)

Diagnosed with the new `--gfx-log` compositor/op trace (docs/tools-reference.md). The grey background has
**two distinct causes**, both now proven:

1. **CORRECTION to the blend section above — op `0x202`/`0x203` "alpha" is a TINT STRENGTH, not object
   opacity.** Evidence: the primary CG is drawn with `0x203 (alpha=0, color=white)` = `0x00ffffff`. That
   means "blend the tint (white) into the texel by strength 0" = **no tint, fully opaque CG** — but slice-A
   treated the alpha byte as the object's opacity → the CG rendered fully transparent → grey. Fix (commit
   5e4fdda): `RenderObject.TintStrength` split from `Alpha` (opacity); textured objects stay opaque and the
   tint LERPs the RGB by strength (0=keep texel, 1=full tint). Surfaceless fills use the strength as fill
   opacity. Verified: the opening event CGs render again (shot-confirmed).

2. **Effect pages: everything collapses into slot 0.** `set-texture` is dominantly
   `set-texture (GLOBAL resId)(GLOBAL slot)(local colorkey)` (543× across the corpus); the **slot is a
   global**. In our run every such global resolves to **0**, so the background (`BG030A`), event CGs, and
   the effect spritesheet (`AE001H`, an 800×400 4×2 grid of blob frames) **all set-texture into slot 0**.
   Objects live-reference their slot, so loading the effect **evicts** the BG → grey; and the effect is
   drawn full-screen from slot 0 (its object `src=(0,0 800x600)`) → the whole sheet (blob grid) covers the
   screen. ⇒ The layering failure is a **slot-assignment** problem. **CONFIRMED CAUSE (2026-07-08, live
   `AGE_DIAG_SETTEX` trace):** every `set-texture` slot = `G[0x62452]`, written by `query-gfx-object?`
   (`0x215`) which returns **-1** for the (correctly-unregistered) CG/effect handles → the fallback at
   SC0000 `label_12649` does `G[0x62452] = lookup-array-2d(rec[s3]=G[0x3239], G[0x62450], 3, 0)` = **0**
   because the slot table `rec[s3]`/`G[0x3239]` is **empty**. That table is filled by `call label_125bd`
   (SC0000 `0x50f`, slots 4..11), which is reached **only through the scene-coroutine framework** — the
   `G[0xaba5c]` gate (`0x450`) + op `0x140` (`u0041F9C0`, coroutine LABEL/yield `"LABEL" "J"` @ `0x46d`).
   **Fixed by the bounded scene-coroutine host model:** `0x140` runs the setup body once, `label_125bd`
   fills the eight slot records, and SC0000 resource `0x23` loads into assigned slot 5 instead of slot 0.
   This was not a compositor/z-order/blend bug. Diagnostics: `AGE_DIAG_SETTEX=1` env → VM logs each `set-texture`
   slot operand + `query-gfx-object?` result.

### Differential offset-path oracle — engine-vs-VM control-flow diff (2026-07-09)

**Method (lever #3 of the RE-front-loading program).** Run the same scene in the real engine and our C#
VM and diff the **executed script-offset path**. Both run the same bytecode, so the opcode at each offset
is static (from disasm); the first place the two offset sequences differ is exactly the branch/opcode/state
we modeled wrong — cheaper and higher-signal than diffing effects, and precisely where the render-drift
walk-backs lived. Tools: `tools/frida/trace_engine_ops.py` (engine capture) · `Age.Cli trace <SCENE>
--boot --trace-json` (VM capture) · `tools/diff_optrace.py` (align + first-divergence). Spec/plan:
`docs/superpowers/{specs,plans}/2026-07-09-differential-oracle*`.

**Capture method that WORKS = the operand hook `vm_operand_fetch@0x41b940`** (thiscall `ecx=ctx`; per op,
`offset=(pc−codebase)/4` from `cur_ctx_index@0x53d14` / `frame_pc@0x53d2c` / `frame_codebase@0x53d28`,
per-context stride `0x78`). Validation: **100% of captured offsets land on valid SC0000 instruction
starts** — proves both the ctx-field offsets and the `(pc−codebase)/4` math. **The tick hook
`adv_interpreter_tick@0x410fb0` does NOT work** — plain-JS `Interceptor.attach` there sees `ecx≠ctx`
(0 entries), so the recon gate's *tick* path is closed too, not just the CModule path noted under Frame
cadence. Two capture caveats, both handled:
- **Frida must hook BEFORE the scene loads.** The scene-entry setup (decl preamble + first CG load at
  `0x802 mov G[0x62424]=0x23; call label_12649`) runs in a µs burst at load; a capture started mid-scene
  floors at ~`0x80d` and misses it. `trace_engine_ops.py` writes `build/tracer-live.flag` once the hook is
  installed → launch it in the background and gate the New-Game trigger on that flag.
- **Operand mode skips zero-operand ops** (stmt-begin/end markers, script-entry `0x259`) — they never
  trigger an operand fetch. `diff_optrace.py` filters the VM trace to argc≥1 ops (same subsequence);
  control flow is preserved (markers don't branch). `--full` disables it for a hypothetical tick capture.

**Codebase identification.** The engine trace tags each op with its script's `codebase` pointer (a per-run
heap address). `pick_scene_codebase` picks the codebase whose offset sequence shares the longest common
prefix with the VM trace. From a boot→opening capture (7 codebases / 265k ops), SC0000 = `0x09c1afe8`
(13252 ops, 100% valid, first offset `1` = offset 0 filtered).

**FIRST DIVERGENCE FOUND (the tool's first catch + self-test).** On the SC0000 opening the VM and engine
agree for **27 ops** (including the coroutine op `0x7b` @ `0x79`, which matched), then **diverge at offset
`0x8d` = op `0xa0` (jcc) on `global-int G[0x6c1]`:** the engine falls through to `0x94` (the op-`0x90`
hotspot-chrome registration block) ⇒ `G[0x6c1]≠0`; the VM jumps to `label_df` (`0xdf`) ⇒ `G[0x6c1]==0`.
Because both ran `1..0x8d` identically, `G[0x6c1]` is set **before** SC0000 — by pre-scene *system* boot
the VM's cold `--boot` (INITCONFIG/INIT2/INIT) doesn't replicate (the **two-boot gap**). `G[0x6c1]` is an
unlabeled but heavily-used scalar (766 uses) in the **same cluster as the op-`0x90` hotspot flags
`G[0x6c9..0x6cd]`** = ADV-chrome/input state. Headlessly benign (no input) but a genuine VM-side state gap
— and a clean demonstration that the oracle localizes a mis-modeled branch to a single instruction. It is
NOT the predicted coroutine yield (`0x140`/`~0x50f`); the oracle reports whatever diverges *first*, and it
surfaced an earlier state hole. **This is now the repeatable way to localize a mis-modeled op/state.**
Phase-2 extension (deferred): effects-diff (global-bank / gfx-registry writes) for branchy scenes.

### ADV control-strip buttons and native hotspot callbacks (2026-07-18)

The five controls at the lower right of ordinary ADV scenes are script-driven retained UI, backed by
`SO001.AGF` in system surface slot 17. They are not Godot-style widgets and opcode `0x90` is not an
immediate hit-test branch. The shared ADV routine copied into all 301 ADV scripts registers five rectangles
at `(684|706|728|750|772,572)`, nominally `20x20`, plus three keyboard/pad records. Native
`op_0x90_handler@0x41fc80` stores `(x,y,x+w,y+h)` and three callback PCs through
`input_hotspot_register_rect_callbacks@0x403d70`; `op 0x94` then arms the input service. The bounds are
compared inclusively.

`input_hotspot_update_cursor_hit@0x403e90` publishes the first matching record index.
`input_hotspot_poll_hover_callback@0x4040b0` maps the stored PCs exactly: operand 5 on pointer entry and
operand 6 on pointer exit. A direct move from one hotspot to another dispatches the old record's exit first
and the new record's entry on the next poll. `input_hotspot_take_click_callback@0x404330` resets the
registry and returns operand 7 on activation. The interpreter temporarily redirects the current script PC
to these callbacks; registration itself returns normally. Companion op `0x97` finds an identical registered
rectangle and binds its fifth operand as an input-bit index; SC0000 uses bits 0, 8, and 7 for its three
`1x1` keyed records.

The five entry callbacks set `G[0x6c9..0x6cd]` one at a time and call the common redraw routine at SC0000
`label_11ffa`; exit clears the corresponding flag and redraws. The redraw takes three pieces from SO001:

- the always-visible base strip from source `(0,254,114,25)` to screen `(681,570)`;
- one 132x27 textual tooltip from source y=227; and
- the same generic 20x20 hover overlay from source `(114,254)` over the selected icon, rendered through
  mode-1 `0x203` at alpha `0x80`.

This gives the exact left-to-right behavior:

| x | Hover flag | SO001 tooltip source x | Label/action on activation |
|---:|---|---:|---|
| 684 | `G[0x6c9]` | 396 | **History** — cancel hotspot wait, call `HISTORY.BIN`, then rebuild chrome |
| 706 | `G[0x6ca]` | 264 | **Auto message** — `0x1b6` read, toggle, `0x1b7` write |
| 728 | `G[0x6cb]` | 0 | **Message skip** — `0x88(1)` enables all-message skip |
| 750 | `G[0x6cc]` | 132 | **Read-message skip** — `0x1cb` read, toggle, `0x1ca` write `message:ReadTextSkip` |
| 772 | `G[0x6cd]` | 528 | **Hide window** — `0x199` yields into the ADV/HIDEWIN coroutine flow |

The redraw also overlays persistent active-state cells for Auto, Message skip, and Read-message skip from
SO001 source x `154/174/194` at y=254. After the four state/coroutine actions, the script waits 100 ms,
reads the virtual cursor with `0x109`, alternates its y by one pixel, writes it with `0x10a`, and resets
transient skip/input state through `0x101`. That deliberate cursor jiggle re-arms entry/exit processing.

**No hover sound.** Manual correction on 2026-07-18 confirms these five ADV controls are silent on hover.
This matches the static evidence: none of the entry/exit callbacks executes `0xb4/0xb5` or another script
audio opcode, and the decoded native registration/hover-dispatch chain contains no audio call. The port
should not invent a sound asset or host audio event for this interaction.

The `/v2` Ghidra image names/comments the opcode handlers, registry/hit-test/dispatch helpers, message-skip,
auto-message, read-skip, cursor, and coroutine operations described above; saved 2026-07-18.

**Port implementation (2026-07-18).** The C# VM now models the per-frame registry and dispatches its three
local callbacks on the VM thread through a callback-only host wake channel, leaving the surrounding ADV input
wait parked. Godot feeds scaled native-screen pointer coordinates and consumes activation before ordinary
page advance. The blocking-host model retains the registered definitions across a normal action callback to
represent the native scheduler's subsequent shared-registration pass; explicit op `0x93` still clears them,
and callbacks such as History rebuild through their script path. The existing bytecode therefore owns the
SO001 hover/active redraw rather than a parallel widget layer; callback completion publishes one retained
frame even though the enclosing wait remains static. `0x1b6`/`0x1b7` are implemented as VM service
state for the first Auto action bridge; timed automatic page advance remains separate follow-up work.

**Port correction from manual validation (2026-07-18).** Merely implementing op `0x90` was insufficient in
the cold single-scene harness. The visible SO001 strip is drawn independently, while `jcc@0x8d` skips its five
rectangles when inherited `G[0x6c1]` is zero; the existing trace did exactly that. Even with the native value
one, `cancel-hotspot-wait@0x622` clears the early pass before page-one `wait-for-input@0x83c`, and the native
ADV coroutine later republishes it. Godot now seeds `adv_chrome_enabled=1` as part of the same bounded
SYSTEM4 chrome bootstrap as SO000/SO001. The blocking VM retains canceled definitions only as inactive
coroutine templates, replaces them if script registration runs first, and otherwise re-arms them at a stable
message wait. A full SC0000 regression proves the real History enter callback and retained-frame publication,
not just a synthetic registry path.

### ADV Auto-message timing and voice completion (2026-07-18)

Auto advance is a native input-service policy, not a script sleep or a fixed synthetic click. The native
state is split between `ctx+0x55104` (`auto_message_enabled`) and `ctx+0x6dbe4`
(`adv_auto_voice_pending`). `op_0xc4_handler@0x420610` sets the latter when it queues voice playback, while
`op_0x1bc_handler@0x416c20` clears it at the next message boundary.

`op_0x72_handler@0x41e690` arms `message:AutoMessageTime1` when Auto is enabled and the current message has
no queued voice. For a voiced message, `adv_input_service_poll@0x411230` instead waits until the native voice
service reports playback complete, clears `adv_auto_voice_pending`, and then arms
`message:AutoMessageTime0`. Expiry follows the same wait-release path as ordinary ADV input. Both branches
substitute 100 ms when their configuration getter returns zero.

The two settings are script-visible rather than constants embedded in the wait handler:

- op `0x1b8` reads selector 0 = post-voice `AutoMessageTime0`, selector 1 = unvoiced
  `AutoMessageTime1`;
- op `0x1b9` writes the same selectors;
- `CONFIG.BIN` initializes them to 500 ms and 2000 ms respectively, and its UI adjusts either setting in
  500-ms steps over 500..9500 ms.

**Port implementation.** The VM retains the enable bit, both configured delays, and per-message voice flag,
then supplies their live state to the blocking host wait. `GodotAdvHost` polls a small deterministic timer
from the existing monotonic frame clock. Unvoiced pages wait Time1; voiced pages remain parked through actual
`AudioStreamPlayer` playback and then wait Time0. A queued/started/completed generation counter closes the
deferred-call race between the VM thread queuing a voice and Godot beginning playback. Turning Auto off
cancels an armed deadline, and turning it back on starts a fresh one. This keeps timing in the host service
boundary and configuration in the VM, without scene offsets, wall-clock sleeps, or Auto-specific input
injection. Timer and VM regressions cover both paths, exact deadlines, disable/re-enable, the native zero
fallback, `0x1b8`/`0x1b9`, and the `0xc4`/`0x1bc` voice-state lifecycle. The `/v2` handlers and input poller
are named/commented and saved.

### ADV all-message Skip service (2026-07-18)

The x=728 control enables a persistent interpreter service; it is not a one-page advance and its active
overlay is not the transient run-state bit. `op_0x88_set_message_skip@0x41f130` writes the requested value to
`ctx+0x13dc` (`message_skip_enabled`) and `ctx+0x550fc` (`message_skip_display_enabled`). While the former is
nonzero, `adv_interpreter_tick@0x410fb0` injects input bit `0x40` on every interpreter tick. The input service
turns that into `run_state_flags & 0x08000000`, completing text/input waits and selecting the already-reversed
skip endpoints for retained transitions.

The state queries are deliberately different:

- op `0x19a` returns persistent `message_skip_display_enabled` for SO001's active x=728 overlay;
- op `0x1c7` returns the transient skip run-state bit, which may be driven by persistent op-`0x88` state or
  a physical fast-forward input such as Ctrl;
- op `0x101` clears transient input/run-state fields after an ADV chrome action, but does not touch either
  persistent op-`0x88` field. The following interpreter tick therefore re-arms Skip.

All 301 ordinary ADV button callbacks call `0x88(1)`. `CALLBACK_LOAD.BIN` contains the corpus's only
`0x88(0)` reset. The engine supports an optional click-cancel state machine, but
`engine_settings_register_defaults@0x46be30` registers `set:CancelMesSkipOnClick=0`; a nonzero loaded engine
configuration is required to enable its press/release cancellation path. The port preserves that default
instead of unconditionally inventing click-to-cancel.

Voice playback also has a native Skip queue. `op_0xc4_handler@0x420610` plays immediately while the skip bit
is clear. While it is set, the handler replaces `ctx+0x6dbf4/+0x6dbf8` with the latest requested voice instead
of starting it. When Skip/read-skip input ends, `adv_interpreter_tick` starts that latest voice and clears the
queue; skipped voices do not accumulate.

**Port implementation.** The VM owns persistent op-`0x88` state and keeps op `0x19a` separate from the
combined op-`0x1c7` persistent/host-input query. `GodotAdvHost` completes text reveal and stable message waits
while Skip is active; the existing `0x1c7` transition branches continue to publish their completed endpoints.
Voice requests replace one host-side deferred payload during Skip and the latest payload starts when
`0x88(0)` arrives. Enabling Skip through the real SC0000 hotspot callback wakes the parked wait without
creating a synthetic pointer click. Focused tests cover `0x88` enable/disable, `0x19a`, `0x1c7`, transient
`0x101`, retained host state, and the actual x=728 callback. The new EngineCtx fields are applied to `/v2`;
the affected handlers, interpreter tick, and settings-default initializer are named/commented and saved.

**Manual pacing correction.** The first port build released skipped text/waits correctly but then let the
background VM free-run to the next non-skipped service boundary. That produced whole-scene bursts separated
by explicit sleeps: visibly an immediate jump, a slow point, then another immediate jump. Native
`adv_interpreter_tick` still dispatches exactly one opcode per engine tick while persistent Skip removes the
ordinary waits. `GodotAdvHost.FrameYield` now consumes one rendered-frame pulse per opcode only while
message Skip is active. Normal opcode bursts retain the existing run-to-service-boundary model; Skip gains
the missing native governor and remains fast without teleporting between blocking points. A regression proves
op `0x88` state reaches the host before the following cadence yields. Validation is engine 168/168,
zero-warning Godot build, and threaded `SELFTEST OK`.

### ADV read-message Skip and shared `RT.DAT` history (2026-07-18)

Read-message Skip is backed by an engine-owned `ReadTextDB`, not a VM-global flag and not ordinary numbered
slot data. Each script frame supplies its raw packed SYS4/AAI resource id, a table of message code offsets,
and its count. `script_frame_load_resource@0x40e980` stores the same id used by
`asset_open_indexed_entry` at frame field `+0x04` (`EngineCtx+0x53d64`); base Himegari scripts therefore use
their SYS4INI file index, while append resources retain the high-byte pack selector.
`read_text_db_find_message_index@0x468f50` maps the current code dword offset through that table to a
per-script message index. `read_text_db_is_message_read@0x469930` formats the script id as an eight-digit
lowercase hexadecimal key, looks up that script's record, bounds-checks the index, and returns the stored
dword flag. `adv_refresh_read_skip_state@0x406cd0` and ops `0x6e/0x71/0x72` combine that result with
`message:ReadTextSkip`; a read page sets `run_state_flags & 0x08000000` and
`ctx->adv_read_skip_state`, which op `0x1cc` exposes to the scripts.

The write side records completion, rather than merely displaying text. Ordinary click/wheel advance and
Auto expiry queue `{script_id, message_index, message_count}` through
`read_text_db_queue_message@0x469340`; an op-`0x72` wait already being passed by Skip queues the same tuple
directly. Opcode `0x71` resets a text layout at the structural sites targeted by T1; it is not a pure runtime no-op:
`op_0x71_handler@0x41e540` snapshots the current code position and calls
`read_text_db_commit_pending@0x46ae20`, which grows or creates the per-script flag array and sets the queued
indices to one. This queued/commit seam lets the port reproduce native read eligibility without scene
offset lists or synthetic VM globals.

Persistence is shared across numbered save slots. `shared_profile_save@0x40c950` atomically rewrites
`SAVE.DAT`, then serializes `ReadTextDB` through `$$RT.DAT` to `RT.DAT`, with `RT.BAK` handling.
`shared_profile_load@0x40ccd0` loads `SAVE.DAT` (falling back to `SAVE.BAK`) and then independently loads
`RT.DAT` when present. Numbered saves use the separate `SAVE%2.2d.DAT` pattern. A successful context/slot
save calls the shared-profile writer, and shutdown also calls it unless `set:NoSaveDat` suppresses shared
data writes.

The `RT.DAT` header is `0x114` bytes: magic `0x54523353` (bytes `S3RT`), a compatibility id, a 256-byte
game id, version pair `1,0`, and script-record count. It is followed by 12-byte script records containing
`{script_id, message_count, pointer_placeholder}` and the corresponding `message_count` dword flag arrays.
The loader validates the header compatibility fields, allocates fresh arrays, and rebuilds the in-memory
hashtable. The port should own an equivalent profile-level model; matching the original raw pointer-bearing
file layout is optional compatibility work, not a prerequisite for native runtime semantics.

The `/v2` Ghidra image now names/comments the lookup, queue, commit, mark, file read/write, and shared-profile
save/load chain and corrects the relevant function prototypes; saved 2026-07-18.

### Remaining ADV control-strip actions and implementation cost (2026-07-18)

The five standard controls are now fully inventoried. Auto message, all-message Skip, and Hide Window have
working native-path services; Read-message Skip is the profile-wide `RT.DAT`/ReadTextDB slice above.
History remains a distinct retained-text subsystem rather than another variation of Skip.

| Control | Native action | Current port boundary | Relative cost |
|---|---|---|---|
| History (`x=684`) | Cancel the ADV hotspot wait and run `HISTORY.BIN` over the text manager's retained record stream | Input/callback infrastructure works; a bounded retained-history model, its read/write opcodes, and supporting text/presentation ops remain | Medium-high, bounded |
| Auto (`x=706`) | Toggle the Auto service | Implemented, including timed wait completion | Done |
| Message Skip (`x=728`) | Enable persistent all-message fast-forward | Implemented; pacing discrepancies remain a later fidelity adjustment | Done |
| Read-message Skip (`x=750`) | Toggle `message:ReadTextSkip`; gate advancement through shared ReadTextDB state | Native persistence and queue/commit/query flow investigated; service not implemented | Medium-high, bounded |
| Hide Window (`x=772`) | Op `0x199` enters the saved ADV coroutine handler, removes chrome, and runs `HIDEWIN.BIN` | Implemented through the native script path, including coroutine re-entry, per-frame callbacks, mouse/joy state, and `.CUR` resources | Done |

`HIDEWIN.BIN` is primarily an input/scheduler slice, not a new renderer. Its former eight effectful gaps were
cursor selection (`0x86/0x87`), mouse callback registration/dispatch (`0xcc/0xcd`), mouse-button state
(`0x108`), and joy callback registration/poll/dispatch (`0xfb/0xff/0x100`). The implementation below adds
those services plus real op-`0x199` frame redirection; the existing retained renderer supplies the visual
state while the script saves translations, hides the ADV chrome, permits view/pan input, and restores state.

`HISTORY.BIN` remains a larger subsystem, but the formerly ambiguous gaps are now bounded. Static runtime
coverage is still 47/78 distinct opcodes (781/854 instructions): this investigation refined semantics, not
the C# handler count. The 15 op-`0x64` sites decode count-prefixed inline integer arrays used for row
rectangles and coordinates. Ops `0xa1/0xa2/0xa3` are a generic value switch (`begin`, `add case`, `dispatch`),
not a History-specific menu/input service; HISTORY maps already-produced action values to local branches.
Op `0x12e` scans those rectangle/offset arrays for pointer hover. The Hide Window callback/input layer is
therefore already sufficient.

#### Retained History record model and lifetime (2026-07-18)

The native text manager at `ctx+0x14940` owns two vectors:

- manager `+0xd24/+0xd28`: 0x48-byte retained records;
- manager `+0xd34/+0xd38`: 8-byte logical entries `{layout_slot, first_record_index}`.

A normal record holds the layout slot at `+0x00`, geometry at `+0x04..+0x10`, value/metadata fields at
`+0x14/+0x18`, font/color/baseline state at `+0x1c..+0x24`, flags at `+0x28`, and an inline-or-heap string
object at `+0x2c` (length `+0x40`, capacity `+0x44`). Flag bit 0 begins a logical group; bit 1 is filtered by
History's navigation mode; `0x20000000` denotes typed metadata and `0x40000000` denotes a voice pair.

The write path is part of ordinary ADV execution:

- op `0x70` defines a text layout and op `0x71` resets one. Unless recording is suppressed, each appends
  `{layout_slot,current_record_count}` and arms the next record's group-start bit. The op-`0x71` operand is a
  layout slot—not a T1 anchor id—although T1 entries structurally target these reset sites. Its ReadTextDB
  snapshot/commit work remains a second responsibility.
- op `0x6e`'s glyph builder appends normal text chunks with the active geometry/font/color state.
- voice op `0xc4` appends a `0x40000000` record containing its stored pair.
- op `0x1d2` appends a `0x20000000` record with operand 1 as the value and operand 2 as the metadata type.
  It was previously misclassified as a safe statement marker; 17,323 corpus uses make this a foundational
  correction.
- op `0x1bb(0)` writes suppression bit `0x80000000` at `ctx+0x55110`; op `0x1bb(1)` clears it. HISTORY uses
  that pair at entry/exit so its own UI text is not added to the backlog.
- op `0x85` clears both vectors. Its 286 corpus uses are two sites in each of 143 ordinary ADV scripts,
  generally bounding History to the current ADV block rather than an unbounded profile log.

The read side is the previously identified op-`0x1d0..0x1d4` family. Op `0x1d0` returns a logical entry's
layout slot and first record index; op `0x1d1` renders records until the next group boundary; op `0x1d3`
finds typed metadata; op `0x1d4` finds the voice pair; op `0x1bd` replays its voice id.

History is independent of `RT.DAT`, but native full save fidelity does serialize the live backlog.
`text_history_serialize@0x451d00` writes the index and packed records/strings after context/numbered-save
serialization, and `text_history_deserialize@0x456130` restores them on the matching load path. This is a
future numbered-save integration seam, not a reason to choose a shared-profile backend now: an in-memory
History button can be complete first, while save/load restoration stays deferred with the wider storage
architecture decision.

The remaining supporting gaps are ordinary presentation operations: primary/ruby font sizes (`0x75/0x197`),
font weight (`0x2bd`), colors/effect mode/offset (`0x76/0x77/0x78/0x1a4`), layout origin (`0x198`), surface
rectangle fill (`0x20b`), message-window alpha (`0x131`), and retained-object presentation (`0x222`). The
`0xd3/0xd4/0xd5` callback-sequence family drives the smooth scrollbar interpolation and is the main
remaining scheduler detail; omitting it would affect motion fidelity, not the backlog data model.

The first port slice now implements an engine/session-owned `AdvTextHistory` service. `GameSession` carries
one instance across VM scene runs, while a standalone VM owns its instance for the full top-level and nested
script lifetime. The model retains the logical index, typed text/metadata/voice records, group-start flags,
layout/cursor snapshots, and font/color/effect state. Ops `0x70`, `0x71`, `0x6e`, `0xc4`, `0x1d2`, `0x1bb`,
and `0x85` feed it directly; Godot remains only a presentation host and does not own the canonical backlog.

The model intentionally has no JSON or disk serialization. `GameSession.ToJson()` continues to snapshot
only the pre-existing global banks, so landing the live backlog does not silently choose a save/profile
backend. Native numbered-save restoration remains the explicit future integration seam described above.
The second port slice implements generic inline-array copying (`0x64`), formatted value dispatch
(`0xa1/0xa2/0xa3`), cumulative navigation (`0x1d0`), and metadata/voice queries (`0x1d3/0x1d4`). The C#
loader retains the original body dwords so `0x64` copies the file's plain count-prefixed values; AGE's
rotate/XOR work belongs to its native in-memory representation, not the SYS4 file format. Navigation mirrors
manager `+0xd6c`: layout define/reset updates the latest-entry anchor, while each `0x1d0` delta is cumulative
and does not mutate it. Query scans stop at the next group-start record; their third operands are unused by
the native helpers. History's remaining work is therefore presentation and interaction rather than backlog
data access.

The original dependency order was **Hide Window first** to establish reusable callback/coroutine input, then
Read-message Skip, then History after both the input layer and message-completion seam exist. Hide Window is
now complete. Read-message Skip's semantics remain understood, but its implementation is deferred until the
port can choose storage for numbered saves and shared profile/global data as one architecture rather than
selecting an isolated ReadTextDB backend. History is therefore the sole remaining control-strip action under
active consideration; it can already reuse Hide Window's input layer, while any useful sharing with the
deferred ReadTextDB work should remain a seam rather than a storage dependency. The `/v2` image
names/comments the cursor, callback dispatch, retained-history navigation/render/metadata, and history-voice
opcode paths.

### ADV Hide Window implementation (2026-07-18)

The x=772 callback now follows the original control flow rather than a Godot-only visibility shortcut.
Opcode `0x199` saves the instruction after the yield, enters the handler-A PC registered by `0x7b`, and,
when HIDEWIN calls `0x199` again, enters handler B. Opcode `0x7c` then restores the saved ADV PC. This keeps
chrome removal/restoration, nested `call-script 0x20`, retained drawing, and the 100-ms cursor re-arm under
the shipped SC0000 bytecode.

The reusable input layer implements the eight previously effectful HIDEWIN gaps: `0x86/0x87` select and
clear indexed cursor resources; `0xcc/0xcd` register and dispatch the timed mouse callback; `0xfb`, `0xff`,
and `0x100` maintain and dispatch the frame-local 32-entry joy/input callback table; and `0x108` returns the
live mouse-button mask. Existing cursor coordinate ops `0x109/0x10a` are now effectful in the VM as well.
Godot supplies virtual-screen pointer coordinates, left/right mouse bits (`0x1`/`0x2`), and the script's
directional input indices (down/left/up/right = 0/1/2/3; accept/cancel = 4/5). The common index-10 release
callback is queued on action release.

Raw ids `0x3318..0x331f` resolve through SYS4INI to the game's 32x32 monochrome Windows `.CUR` assets. The
runtime decodes their DIB XOR/AND masks and hotspots to RGBA textures and installs them through Godot's
custom-cursor API. This is asset-backed behavior; no replacement cursor art is authored by the port.

Every ordinary ADV script gates the handler-A call to HIDEWIN on `G[0x62425]`. No script writes that global,
and the complete boot-to-SC0000 VM-write capture does not contain it, so it is native scheduler-owned
inherited state rather than numbered save data or the data-only `--boot` prefix. The Godot scene bootstrap
mirrors the original enabled value as `adv_hide_window_enabled=1`, next to the already documented
`adv_chrome_enabled` state. A real-script regression activates SC0000's x=772 record, enters HIDEWIN.BIN,
services multiple timed input iterations, closes through the native right-button bit, and returns to the
parked ADV wait. Synthetic regressions separately cover both coroutine handlers, callback dispatch, live
pointer/button reads, cursor host forwarding, and real CUR decoding.
Validation is engine 175/175, opcode/global generator tests and lints clean, zero-warning Godot build, and
threaded `SELFTEST OK`.

**Manual-validation correction (2026-07-18).** Native `bit-set`/`bit-reset` operands are bit indices, not
literal masks: HIDEWIN sets index 1 at `0x13d`, tests mask `0x2` at `0x146`, and clears index 1 at `0x154`.
The old VM interpretation wrote mask `0x1`, so the right-button release edge could never reach the restore
path. `/v2` confirms the generic semantics in `op_0x135_handler@0x4296c0` and
`op_0x136_handler@0x429730`: `value |= 1 << index` and `value &= ~(1 << index)`, with indices 0..31 valid.
The raw op-`0x108` channel therefore supplies `0x1` for `VK_LBUTTON` and `0x2` for `VK_RBUTTON`, making a
right-button release one direct restore route. That is not the complete input model, however. Native
`input_poll_mouse_action_bits@0x460240` also maps the physical left/right buttons through configurable
logical actions (defaults 0/1) to callback indices 4/5, and HIDEWIN registers both indices to its restore
callback. A completed ordinary left click therefore restores the textbox as observed in the original game.
The raw left-button edge branch only supports moving an oversized retained display object while held; it is
not evidence that dungeon gameplay camera panning is enabled during an ordinary 800x600 VN scene.

The manual run also exposed that ADV text and the op-`0x72` wait indicator are currently Godot presentation
overlays rather than retained texture objects. The shipped handler correctly faded the textbox/chrome, but
those overlays ignored the op-`0x199` coroutine lifecycle and remained above the scene. The host now suspends
both when the first `0x199` saves/yields the active ADV page and restores them only when op `0x7c` restores
that saved page PC. The enclosing input wait remains parked throughout, so HIDEWIN continues to own mouse and
mapped action input instead of an overlay click accidentally advancing dialogue.
The Godot input adapter now queues the native primary-action callback while the ADV page is suspended and
does not release the enclosing dialogue wait; otherwise the restoring click would also advance the page.
The `/v2` bit and mouse-input helpers are annotated and saved. Validation: engine 177/177, opcode tests/lints
and vm0 RECOVER clean, zero-warning Godot build, and threaded `SELFTEST OK`.

### ADV retained text — ops `0x7a` / `0x204` and show-text publication (2026-07-10)

The SC0000 textbox uses two related native paths under the text manager at `ctx+0x14940`:

- **Op `0x7a` (`op_0x7a_handler@0x41eba0`) is `set-adv-text-cursor(layoutSlot,x,y)`.**
  `adv_text_set_cursor@0x4530f0` treats slot 0 as the current slot at manager `+0x4c8`, resolves
  `manager+0x414[slot]`, and `text_layout_set_cursor@0x452530` writes x/y to `+4/+8` of that
  layout's last 20-byte record. SC0000 `0x9d3` and `0xbbf` set slot 1 to `(75,47)` for voiced
  pages. The reset narration record is `(100,47,720,147,0)`.
- **Op `0x204` (`op_0x204_handler@0x422a60`) is immediate `draw-string(surface,x,y,string)`.**
  `draw_string_to_surface@0x450150` locks the numbered D3D surface, selects the uncached or cached/effect
  raster worker, consumes CP932 characters through GDI `GetGlyphOutlineA`, blends the bitmap in
  `text_blit_glyph_bitmap@0x458c80`, and unlocks. Font/color/effect state is retained around manager
  `+0x450/+0x458/+0x4d0/+0x544/+0x558`; the observed ADV glyph is white with a one-pixel `0x606060`
  outline and advances 25 pixels. At SC0000 `0x9b2`, surface 13 is 400x30 and receives `"魔王"` at
  `(1,1)`; the following `0x1fb` binds it to retained object `0xe678` at `(74,444)`, so the name begins
  at screen `(75,445)`.

Show-text (`0x6e`) is the timed companion, not an immediate Label write. `adv_text_build_glyph_records`
`@0x4576c0` measures and rasterizes the complete CP932 line into one 20-byte record per glyph. Layout slot
1 has origin `(0,430)`, bounds `(720,147)`, reveal handle base `55000` (`0xd6d8`), and source surface
`slot+0x14 = 21`. `adv_text_publish_next_glyph@0x451220` advances the reveal index and publishes each
record with `gfx_object_bind_draw`. Native SC0000 records `ctx+0x14e9c = 50 ms`: page 1 builds 13 records
at `0x834`, then publishes them one at a time before reaching wait `0x83c`. A click during reveal completes
the remaining records and is consumed; the next click releases the stable wait.

The port retains the SC0000-visible contract without exposing thousands of individual host glyph objects:
surface strings remain associated with blank surface slots for later retained-object binding, while ADV
lines retain cursor/origin, start time, visible-glyph count, and completion state. The compositor renders
the surface-13 name at the bound `0xe678` transform and the dialogue at origin+cursor. `ShowText` parks the
VM until the 50 ms/glyph service completes or a click forces completion, so the completion click cannot
pre-arm the following `wait-for-input`. Local/global string-pointer operand tags (8/14) are now resolved by
the VM, which is required for SC0000's `lookup-array local-string-ptr -> draw-string` name path.

**Deliberate Phase-A fidelity gap — Label rendering instead of native glyph objects.** The native engine
GDI-rasterizes CP932 glyph bitmaps and publishes retained 20-byte records one glyph at a time; the port
collapses that representation into Godot `Label` nodes for the ADV body and surface-bound speaker name.
Coordinates, layout origin/cursor, reveal timing, click consumption, and retained surface/object placement
are native-backed, but the final glyph rasterizer and object granularity are not. Consequences can include
small differences in glyph shape, advance/kerning, baseline, wrapping, clipping, and outline pixels, plus
missing behavior if a later scene depends on per-glyph color/effects, transforms, z-order, lifetime, or
furigana interaction. Treat any such discrepancy as debt in the text renderer, not evidence that `0x7a`,
`0x204`, or the ADV scheduler coordinates are inherently wrong. A future fidelity pass can replace the
Label backend with decoded glyph surfaces/retained glyph records behind the existing VM/host contract.

Matching evidence: `build/native-adv-text-trace.jsonl` and Godot timeline captures at `0x834`, `0x9b2`,
`0x9d3`, and `0xa0d`. Windowed page-1 pixels place glyphs at x=100 and native y=477; voiced page 7 has
non-overlapping name/dialogue bands at y=447–468, 478–500, and 507–530. A manual run progressed 14 pages:
11 clicks completed active reveals and 14 later clicks released 14 distinct waits through `0xe0c`.

#### ADV wait indicator -- ops `0x73` / `0x72` (2026-07-11)

The small bat marker is a configured ADV-layout sprite, not a glyph or part of SO001. `SYSTEM4.BIN`
loads universal raw asset `0x337c` (`SO000.AGF`) into surface slot 12. SO000 is a 390x27 strip of thirteen
30x27 bat frames. It then executes op `0x73` with
`(layout=1, x=385, y=140, surface=12, src=0,0, cell=30x27, terminalFrame=12, period=48ms)`.
Layout 1 begins at screen y=430, so the marker lands at `(385,570)`, matching the original screenshot.

Native `op_0x73_configure_wait_indicator@0x41e900` writes this descriptor through
`adv_text_configure_wait_indicator@0x44ff60` to layout fields `+0x3c..+0x60`; operand 10 configures the
animation period through `adv_indicator_set_frame_period@0x44d060`. `op_0x72_handler@0x41e690` later calls
the layout renderer with frame `-1` and arms the input-wait flags, which makes the configured animation
visible only after show-text has naturally or forcibly completed. The completion click remains consumed;
the following click releases the wait.

**Port implementation:** the single-scene harness now injects SYSTEM4's exact SO000 surface-12 and op-`0x73`
configuration alongside its existing SO001 shortcut. The VM forwards all ten operands through the host ABI;
`GodotAdvHost` retains configurations by layout and resolves slot 0 to the active Phase-A layout. A separate
Godot atlas overlay advances the inclusive frames `0..12` on the 48 ms host clock only while `IsWaiting`,
then hides immediately when input releases the wait. Keeping this 30x27 overlay outside the software
backbuffer preserves the static-wait compositor optimization. Full SYSTEM4 replay remains Phase-B work;
the shortcut carries the same state in the meantime.

### SC0000 native SFX / BGM-fade family — `0xb4`/`0xb5`/`0xb6`/`0xc2`/`0xd9` (2026-07-11)

The three SFX opcodes are a retained channel lifecycle, not immediate fire-and-forget calls. Handler
resolution and the saved `/v2` names are:

- `0xb4` `op_0xb4_sfx_load@0x4201d0` -> `sfx_channel_load@0x482500`: `(resource_id, channel)` opens the
  scene-local SYS4 entry and replaces the channel decoder/buffer without starting it. The manager supports
  13 slots (`0..12`); SC0000 deliberately resets and uses the `0..9` subset.
- `0xb5` `op_0xb5_sfx_start_once@0x420210` -> `sfx_channel_start@0x4825d0`: starts the loaded channel with
  logical loop mode 0. Adjacent op `0xba`, not this slice, passes mode 1.
- `0xb6` `op_0xb6_sfx_release@0x420250` -> `sfx_channel_release@0x482600` ->
  `sound_buffer_destroy@0x4831a0`: stop/release and clear the retained resource/decoder; empty release is
  safe. SC0000's `0x62b..0x646` and `0x120d..0x1228` are ten-channel reset sweeps.

`sound_decode_channel@0x483360` selects the decoder by file signature, constructs a DirectSound buffer, and
installs four quarter-buffer notifications. `sound_buffer_start@0x484270` primes the ring and synchronously
calls `IDirectSoundBuffer::Play(0,0,DSBPLAY_LOOPING)` before returning. That flag loops the streaming ring,
not the logical clip: `sound_stream_fill_quarter@0x483b70` rewinds the decoder only for logical mode 1;
otherwise it pads after EOF and `sound_buffer_stop@0x483aa0` stops playback. This family carries no volume or
pan operands. It inherits configured SFX volume and centered pan: the first-pair capture applies DirectSound
attenuation `-2377` to both channel loads, and the shared audio service later records centered `SetPan(0)`.
`sound_buffer_set_volume@0x483f80` computes that inherited attenuation; neither value is supplied by these
five handlers. The bounded port does not yet import native audio preferences, so its extracted-WAV bootstrap
uses unity gain and centered pan rather than hard-coding the captured user's setting.

The native trace in `build/native-sfx-trace.jsonl` captures SC0000's first pair: `0xb4@0xc29` resolves
resource `0x28` to `E0808.WAV`, loads channel 0, and `0xb5@0xc2e` starts it in the same millisecond. The next
`0xb4@0xc31` preloads the same WAV into engine-owned secondary channel 4 for a later service start. The
scratch global `G[0x6242d]` is maintained outside script-visible writes; the SC0000 port profile exposes it
as an external value of 4 rather than pretending the script assigned it.

Normal-speed windowed validation reached `wait-for-input@0x1a58` after 45.6 seconds without an audio stall;
the user confirmed the opening effects were audible and sounded good.

`0xc2` is BGM rather than SFX: `op_0xc2_bgm_fade@0x4204c0` sets run-state `0x200`, arms the service timer,
and calls `bgm_fade_arm@0x464830`. `bgm_fade_tick@0x464960` linearly interpolates current to target percent;
durations at least 1000 ms take 100 steps, shorter durations take 10, and target zero releases the source.
The VM is parked for the requested duration. `0xd9` is adjacent startup control, not audio data: it clears
run/service bit `0x1000` in the primary and, when active, secondary context and has no VM-visible result.

### Scene-entry state snapshot — auto-seeding single-scene runs (2026-07-09)

**Problem the oracle surfaced:** single-scene VM runs diverge from the engine because they lack the
pre-scene global state the engine accumulates over `SYSTEM4 → … → TITLE → New Game`. `--boot` reproduces
only the data `*INIT` scripts; flags like `G[0x6c1]` (ADV-chrome enable) are set later and missed.

**Solution — `capture_global_writes.py`** hooks `vm_operand_write@0x425fb0` and logs every global-int
write as `(codebase, index, PLAINTEXT value)`. **Key: the helper receives the plaintext value** before the
engine encodes it into the obfuscated global store (rotate+XOR with the per-session cookie `ctx+0x55120`)
— which is exactly why the shelved flat-int32 scans (`global-memory-re.md`) found nothing, and why hooking
the WRITER is clean (no de-obfuscation). ABI: thiscall `ecx=ctx`, `[esp+4]`=operand index, `[esp+8]`=value;
the global index/type come from the instruction's operand slot (`framePc + idx*8`; `type=*(opnd-4)`,
`index=*opnd`), type 3 = global-int.

**Packer gotcha (solved):** AGE.EXE unpacks in-place at `0x400000`, so a `--spawn`-time hook hits packed
bytes → Frida "unable to intercept function at 00425FB0". Fix: poll `0x425fb0` until the real prologue
(`6aff 6836a85600 64a1…`) appears (unpack done), THEN attach. `--spawn` is required for completeness
(attach misses pre-attach boot writes); the tool also kills the spawned pid on setup failure so a JS error
can't leave a suspended windowless orphan. (First bug hit: `SIG` hex without `0x` → JS `create_script`
SyntaxError → resume never ran → orphaned suspended game.)

**Validated:** a real boot→New-Game→SC0000 capture (34,008 globals incl. `G[0x6c1]=1`, `G[0x62424]=0x23`
the resId) loaded via `Age.Cli trace SC0000 --state <snap> --trace-json` seeds the VM to match the engine's
**entire opening** (542 ops, no non-realignable fork) with ZERO manual seeding — confirming the
pre-scene-state theory and giving a general auto-seed for single-scene fidelity. **Residual:** a 2-op color
detour (`0x202/0x203` @ `0x122d0`, writing `G[0x62451]`) the full state does NOT fix = a real branch/op
difference to chase (not state). **Caveats:** snapshot is playthrough-specific (best for canonical entry
points — new-game opening, chapter starts); v1 captures global-INTS only (type 3; strings/floats TODO);
includes the scene's own early writes (can exclude by codebase for a pure pre-scene boundary).

---

### S4AC append catalogs and packed resource ids (2026-07-11)

`asset_mount_append_catalogs@0x44f120` scans `*.AAI`, constructs `AAIFileDB` objects, and calls
`aai_catalog_load@0x401110`. Installed `S4AC422` uses selector `1` at header offset `0x108`, expanded size
at `0x110`, packed size at `0x114`, and an LZSS directory stream at `0x118`. The expanded directory is the
same archive-count / 256-byte archive-name / file-count / 80-byte-record layout as SYS4INI. Its 81 names are
literal `$1$...` basenames in `APPEND01.ALF`.

After a successful load, the scan writes the catalog pointer to `FileDB+0x3028 + selector*4`; a later
successful discovery of the same selector overwrites that slot. `asset_open_indexed_entry@0x44f390` splits
nonzero-high-byte ids into `mounted_aai[id >> 24]` and record index `id & 0xffffff`, while high-byte-zero ids
stay in the base SYS4 table. `aai_open_indexed_entry@0x401630` then applies the same exact loose basename
before indexed ALF fallback as the base path. Thus append selection is explicit pack selection, not filename
replacement. The selector extraction is an arithmetic `SAR 24`, so ids whose high byte has its sign bit set
index before the mount table rather than slots `0x80..0xff`; the port rejects those selectors instead of
inventing unsigned behavior. `/v2` names/comments this mount/load/open chain and is saved.

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
  drift's SECOND half" above. The then-remaining `AE*` blend and cold-anchor work is resolved by the later
  blend, geometry, animation, and retained-presentation sections.

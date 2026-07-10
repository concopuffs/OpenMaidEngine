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
saved handler PCs as frame metadata; op `0x7c` is a host-scheduler marker because `IHost.FrameYield` and
`FrameClock` already own per-frame pacing. `TITLE.BIN`'s unrelated `"BIN","SC????.BIN"` service remains
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
`gfx_object_init_default`@`0x472810`), the setters `gfx_set_vec18/24/16c`(`0x47e960/e910/e800`), the getters
`gfx_get_vec18/24`(`0x47f360/f2e0`).

#### The `0x21c–0x243` sprite transform / ANIMATION cluster (2026-07-10, partial implementation)

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

None of `0x21c/0x224/0x243` waits for the per-object transform duration. The normal-path boundary is
the engine's rate-limited one-op interpreter cadence plus continuous retained compositing. This matters because
the earlier Frida probe hooked `vm_operand_fetch`: its ~1,788/s result counts **operand reads**, commonly
several per completed opcode. Feeding that number to the port's per-completed-opcode `FrameYield` made
the `0xcbc0` section reach only scale 1.44 in 226 ms before teardown.

The corrected host limiter is refresh-independent and runs at 200 completed opcodes/s. It resets accumulated
credit after sleep/input parking, and clicks are accepted only while actually waiting, so clicks during a
visible animation cannot pre-arm the next wait. A normal-clock replay retained `0xcbc0` for 1,798 ms at
the intermediate 215/s calibration; the final 200/s replay kept it alive for 2,014 virtual ms, beyond the native
1,890 ms endpoint. `--speed` scales VM, sleep, and animation clocks together for comparison without
changing these virtual-time relationships or auto-advancing waits.

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
VM `case "sleep"`; see `vm-map/opcodes.toml` 0xc8. **⚠ It does NOT make the rapid opening `AE*` burst animate**
— execution trace shows the back-to-back `set-texture→draw-texture` swaps have no `sleep`/`wait`/`present`/
coroutine between them, so bare `sleep` was never their pacer; what advances that burst is still unknown (an
earlier claim that "the opening is sleep-paced" was inherited from this doc and never execution-verified —
corrected). Profile the real Godot run (`--trace-histogram`) to find it.

**Related — `present-frame` (op `0x20c`):** dispatch `param_1[0x26e9f] = gfx_op_0x20c_present_frame` →
`gfx_render_frame`@`0x4820b0` (buffer flip). Our compositor presents every frame regardless, so `0x20c` is a VM
no-op (`noop_headless=true`); the Kelebek label `u00416200` was VA-drift. This corrects the earlier open item
("no per-frame present") above — present is host-implicit; only `sleep` timing was missing.

### Frame cadence — the interpreter tick, and why our port "speeds through" (2026-07-08)

Answers the open question the `sleep` section above left ("what advances the rapid opening burst is still
unknown"). The pace is an **engine-level execution cadence**, not any bytecode primitive. Corroborated in-game
by Ctrl fast-forwarding ADV (a speed governor). Motivated by the user's observation that our port visibly
speeds through the opening — which contradicted, and correctly overturned, an earlier same-day overclaim that
"there is no missing pacer" (that was inferred from headless op-counts, which cannot render).

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

**Model:** single-threaded, vsync-timed frame loop; each frame it steps opcodes until the context **yields**
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

**Port relevance (the speed-through root cause).** Our Godot VM runs on a **free-running background thread**
(`Task.Run(() => vm.Run())` in `Main.cs`) with no frame binding — it executes an entire page's ops in
microseconds; only `WaitForInput` and `Sleep` pause it, and the compositor merely samples `GfxState` at 60fps.
So every no-`sleep` CG/state advance collapses to its end state → the speed-through. **Fix shape:** throttle
the VM to a bounded wall-clock op rate (see the measured numbers below) via a per-opcode host yield; retire the
free-running thread. Spec: `docs/superpowers/specs/2026-07-08-frame-stepped-vm-design.md`.

### Frame cadence — live measurement (2026-07-08, Frida read-only)

The static pass couldn't reach the outer loop, so we measured the running game. **Read-only / import-only
only** (`tools/frida/probe_frame_cadence.py`, `probe_present.py`): a plain-JS hook on the proven operand-fetch
`0x41b940` (grab ctx + count exec rate) + system-DLL hooks; no engine-code patching. **Lesson learned the hard
way:** a first attempt with a **CModule** hook on the hyper-hot `adv_interpreter_tick` crashed the game
instantly (bad native callback into the hottest path — *not* anti-tamper; our other scripts hook engine code
via plain JS and survive). Use plain-JS hooks on proven addresses + memory polling.

Findings:
- **Execution is rate-limited, not free-running.** Normal active rate ≈ **1,788 operand-fetches/sec** (peak
  ~5,796) — far below an unthrottled interpreter (millions/sec), so the engine paces itself. Execution is
  bursty (parked at `wait-for-input` prompts, then a bounded burst), confirming per-iteration op-budgeting.
- **Fast-forward (Ctrl) scales the rate ~4×** (≈7,738/sec avg, peak ~15,572), gated by the engine skip bit
  **`ctx+0xa0ce4 & 0x8000000`** (set only while fast-forwarding). It runs *more ops per unit time* — it does
  not skip content. (Ctrl is **ADV-scoped**; it does not speed up gameplay/menus.)
- **Rendering = Direct3D 9, UNCAPPED.** `ddraw.dll` is not loaded; the game uses `d3d9.dll` (+ `nvd3dum.dll`).
  `IDirect3DDevice9::Present` (device vtable slot 17, found by scanning ctx for a d3d9-vtable object with a
  full ~119-method table) fires ~**1,908/sec** with **no vsync**; `BeginScene`/`EndScene` never fire → a **2D
  StretchRect-style compositor**, not a 3D scene. So there is **no fixed display-frame rate**; `Present` rate
  ≈ op rate (~1 op per present). ⇒ the pacing quantity is the **wall-clock op rate**, not a per-frame budget.
- **Corrected implication for the port (2026-07-10):** 1,788/s is an operand-fetch rate, not an opcode rate.
  `IHost.FrameYield` runs once per completed opcode, so matching those quantities directly overclocked the
  port by roughly ninefold. The native SC0000 transform lifetime pins the corresponding completed-op cadence
  at about **200/s**. The limiter must be wall-clock based (not a per-render callback budget), with a future
  ADV Ctrl multiplier applied to the same unified clock.

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
- **Retained objects carry cyclic rotation state:** flag value `4` at `obj+0`, start timestamp
  `obj+0x214`, period `obj+0x228`, and axis `obj+0x244/248/24c`.
- **`gfx_object_set_rotation_cycle`** (`0x47f060`, worker for legacy op **`0x234`**)
  configures that channel. `gfx_object_anim_interpolate` consumes it from frame-time `ctx+0xb550`
  as a repeating 0..360-degree axis rotation. Op **`0x1fd`** is a separate scaled 3-vector setter.
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
  `obj+0x64 = packedARGB`, the color-anim active bit, resets progress `obj+0x34=0`. Animates over the global
  clock (`0x238`).
- **`0x203` (`gfx_op_0x203_worker_set_color` `0x47e9b0`)**: sets a **static** color/alpha `obj+0x60`, no anim
  bit. Immediate per-object modulation.
- **Blit** (`gfx_object_blit_d3d9` `0x4774c0`): selects a **blend mode** (`local_2c`: 0 opaque, 1 alpha
  `SRCALPHA/INVSRCALPHA`, 2/3 additive/special for glow/flash) and passes a modulation color/alpha to the
  device draw. Slice A ports the **alpha** path (fades); additive (glow) is deferred (its `local_2c` source
  field is not yet pinned).

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
| `0x239` | `gfx_op_0x239_set_srcrect_cell` → `gfx_worker_set_srcrect_cell` | set **spritesheet** grid `obj+0x238/0x23c` + static cell `obj+0x234` |
| `0x231` | `gfx_op_0x231_anim_srcrect` → `gfx_worker_anim_srcrect` | **animate spritesheet cell**: bit2 active, period `obj+0x230`, grid `obj+0x238/0x23c` → interpolator SRC-RECT SCROLL channel (ping-pong across the row) |
| `0x232` | `gfx_op_0x232_anim_color` → `gfx_worker_anim_color` | **animate color** (pulsing GLOW): bit2 active, period `obj+0x220`, target `obj+0x240` → interpolator COLOR channel (ping-pong). Distinct from static `0x202`/`0x203` (`obj+0x60/0x64`) |
| `0x228` | `gfx_op_0x228_query_position` (`FUN_0047cdd0`) | **query** current computed (x,y,z) → operand slots 3/4/5 (script logic, not render) |
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

**Still deferred:** `0x236` (`gfx_op_0x236` @`0x423ee0`) a
**timed/animated-surface (movie-like) op**; plus the unclassified `0x242/0x23d/0x20a/0x20e`
tail (2-arg flags / inline). These stay GAP until a follow-up slice or are safe-noop'd if the opening tolerates it.

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

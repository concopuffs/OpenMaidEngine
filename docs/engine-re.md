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

**Other confirmed engine-context offsets** (`ctx`/`esi`): `+0x53d14` = current script-context index;
`+0x53d88 + index*0x78` = current decoded instruction length in dwords (the interpreter advances PC by
that value times four); operand-fetch helper =
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
decompile `ctx->cur_ctx_index` / `ctx->frame_instruction_word_count` / `ctx->run_state_flags` instead of `param_1 + 0x…`
(verified: `sleep_op_0xc8`, `gfx_op_0x215_query_source_slot`). Add a field: edit `engine-ctx.toml`, run
`engine_ctx_build.py --build`, re-apply the struct. (The VM global bank `G[…]` is separate — `globals.toml`.)

---

## Findings

### Logical screen dimensions come from the SYS4INI settings trailer (resolved 2026-07-24)

AGE has a compiled fallback, but the shipped game's logical resolution is data-driven. Engine construction
at `0x413860` initializes `EngineCtx.screen_w/screen_h` to `640x480`, and
`engine_settings_register_defaults@0x46be30` independently registers `set:WinX=640` and
`set:WinY=480`. During `engine_load_sys4ini_and_mount_append_catalogs@0x4099f0`, the loader advances past
the archive/file directory and VM-bank metadata into the remaining SYS4INI data, then calls
`engine_apply_sys4ini_settings@0x4056d0`. That routine loads the serialized settings through the engine
settings service and copies `set:WinX`/`set:WinY` into the two context fields before surfaces and the main
window are created.

The decompressed Himegari SYS4INI trailer explicitly contains `SCREENX=800` and `SCREENY=600`. As an
independent cross-game check, Kamidori's trailer contains `1024x576`. Thus the answer for the logical game
canvas is **SYS4INI per-game data overriding a generic EXE fallback**, not a separately compiled AGE
binary for every resolution. `display:ScreenMode`, `display:FullScreenWidth/Height`, aspect mode, and
related registered settings govern presentation/fullscreen selection separately; they do not redefine the
authored logical canvas. The serialized trailer location is recorded in `sys4-format-notes.md`.

### SYS4INI startup-settings catalog (resolved 2026-07-28)

The settings trailer is a real per-game startup profile, not a miscellaneous string appendix. The complete
36-pair serialized inventory and physical offsets are canonical in `sys4-format-notes.md`; this section owns
their native behavior and current port relevance.

The startup order is:

1. `engine_settings_register_defaults@0x46be30` creates AGE's generic registry.
2. `engine_load_sys4ini_and_mount_append_catalogs@0x4099f0` parses the asset directory and VM-bank metadata,
   then passes the remaining length-delimited settings record to
   `EngineCtx::engine_apply_sys4ini_settings@0x4056d0`.
3. `engine_settings_import_sys4ini_pairs@0x46da80` reads the pair count followed by NUL-terminated CP932
   key/value strings, matches external names case-insensitively, and inserts their canonical typed keys.
   Unknown external names take the loader error path.
4. `engine_apply_sys4ini_settings` copies `set:WinX`, `set:WinY`, and `set:AntiFontVersion` into context
   fields. `engine_initialize_subsystems_from_settings@0x415390` then initializes text, graphics, and audio
   from the completed registry before script execution.

Most entries are direct integer/string mappings, but three transforms matter:

- `SAVEVERSION=310` becomes `set:SaveVersion1=3` and `set:SaveVersion2=10` by quotient/remainder division
  by 100. It is the source of Himegari's persistence layout and append-catalog compatibility version.
- `NOSETMUSIC=3` stores `set:NoSetMusic=3` and also seeds `sound:Music=2`
  (`NOSETMUSIC - 1`). `sound_route_is_enabled@0x405420` treats nonnegative music states as enabled;
  `sound_set_music_route_enabled@0x407f80` moves the state between enabled/disabled bands separated by
  three. The `set:NoSetMusic` copy itself has no later direct reader in this image.
- `WHEELKEYUP`/`WHEELKEYDOWN` each populate both `set:WheelKey*` and
  `message:WheelKey*OnTW`. A nonempty `VERREGPOS` also invokes
  `registration_read_class00_version@0x46ae80`; its Windows `CLASS00` registry result replaces
  `set:GameVersion`.

The meaningful consumer catalog is:

| Setting group | Native Himegari effect | Classification |
|---|---|---|
| `CREATEOBJECT`, `DRAWMODE` | `CreateObject=2` selects only the retained D3D/object backend; `DrawMode=1` selects its matching draw/text path. Startup verifies that bit `1 << DrawMode` exists in the backend mask. | Active renderer selection |
| `SCREENX`, `SCREENY` | Replaces the generic `640x480` fallback with the `800x600` logical canvas before window/surface creation. The port parses and applies these dimensions to its logical canvas and default windowed client size; optional host boot arguments can vary only the physical client. | Active core profile; implemented |
| `FONT`, `ENABLEANTIFONT`, `ANTIFONTVERSION` | Seeds MS Mincho, enables `message:UseAntiFont`, selects the grayscale glyph-outline path, and records antialias version 3. | Active text profile |
| `DEPENDMOVIESOUND` | Supplies the default movie-audio dependency/routing policy when movie opcode flags do not force another route. | Active media policy |
| `FULLSCREENBIT`, `ALWAYSBACKUPSURFACE` | These are read only inside the legacy `CreateObject & 1` DirectDraw branch of `engine_initialize_graphics_from_settings@0x406480`. Himegari selects only bit 2, so its 32-bit/zero values do not drive the active renderer. | Valid compatibility settings, inert for this profile |
| `SCREENWARNING` | Imported as `set:ScreenWarning`; no reader beyond default registration/import was found in this EXE image. | Loaded, no observed consumer |
| `NOSETMUSIC` | Seeds the live music-route state as described above; the route starts enabled. | Active audio initialization plus otherwise-unused policy copy |
| all seven `MENU_*` keys | Imported as `set:Menu_*`. No direct non-loader reader of these exact registry keys was found; they appear to be shared AGE native-menu capability metadata rather than Himegari script-menu state. `MENU_USEANTIFONT` does **not** disable the independently enabled `message:UseAntiFont`. | Loaded compatibility/menu metadata |
| `ENABLEMEMFLIP` | Gates `op_0x6d`, which swaps VM-bank pointer pairs only when enabled. Himegari disables it and its corpus does not use that opcode. | Compatibility capability gate |
| `CLICKONUP` | Imported as `set:ClickOnUp`, but no direct runtime reader was found in this image. | Loaded, no observed consumer |
| `CANCELMESSKIPONCLICK` | Himegari overrides the zero fallback with 2, enabling `adv_interpreter_tick`'s press/release click-cancel state machine for persistent message Skip. | Active ADV input behavior; currently missing from port |
| `CONTROLDISIBLECURSOR` | The spelling is native. Value 1 suppresses AGE's ordinary cursor-restore call when a bound ADV hotspot action or hover callback is selected. | Active ADV cursor behavior; currently missing from port |
| `COEXISTMESSKIP` | Value 1 lets Auto and all-message Skip remain simultaneously enabled. With zero, `adv_toggle_auto_mode@0x406b70` clears Skip and `adv_toggle_skip_mode@0x406c20` clears Auto. | Active ADV toggle behavior; currently missing from port |
| `REDRAWTEXTONKEY` | Value 0 disables the wheel/key path that traverses and republishes the current text-history view through `CALLBACK_TEXT.BIN`. | Active ADV history-input policy; port uses its script callback path instead |
| `WHEELKEYUP`, `WHEELKEYDOWN` | Rebinds the registry action bits from generic defaults 3/1 to 8/9. `adv_input_service_poll@0x411230` uses them for ADV wheel-key/history handling; raw `WM_MOUSEWHEEL` accumulation for op `0x10d` remains a separate channel. | Active ADV input binding; port does not source these values |
| `USEAPPDATAFOLDER`, `SAVEPATH` | Selects `%LOCALAPPDATA%\Eushully\姫狩りダンジョンマイスター\SAVE` as the native save root. | Active native path policy; the port redirects it through the shared profile-root policy |
| `REGFILEPATH` | Supplies the Eushully/product-relative directory for native `SYS4REG.INI`. With Himegari's `USEAPPDATAFOLDER=1`, AGE resolves `%LOCALAPPDATA%\Eushully\姫狩りダンジョンマイスター\SYS4REG.INI`. | Active native engine-options path; redirected with saves while retaining the native format |
| `SAVEVERSION` | Selects numbered-save layout 3.10 and enables the matching append-catalog persistence structures. | Active persistence ABI |
| `GAMEVERSION`, `VERREGPOS` | Seeds display/registration version `1.00`, then permits the GUID-selected Windows registry `CLASS00` value to replace it. | Windows version/registration metadata |
| `REGKEY` | A nonzero value activates `registration_validate_key_file@0x46fc80`, which opens `SYS4RK.BIN`, validates its header/transformed payload and both CRC variants, and publishes the registration result. | Native registration/key validation |
| `COPYRIGHT`, `RCVERSION` | Both are imported; no later direct consumer of the exact registry keys was found in this image. | Loaded product metadata |

This is intentionally a native-behavior catalog, not a mandate to reproduce every Win32-era switch.
`Sys4AssetCatalog` now parses the ordered trailer once, retaining unknown pairs for diagnostics, and projects
`SCREENX`/`SCREENY` into one validated engine-owned logical canvas. Godot uses that value for its content-scale
base, default windowed client, backbuffer/compositor bounds, primary surface, movie/layout fallbacks, and input
coordinates. `--window-width`/`--window-height` may vary that client independently while the logical canvas
and all AGE coordinates remain unchanged. AGE's independent `640x480` defaults apply for missing or invalid
dimensions. The port still
preserves requested Mincho/Gothic faces, save version 3.10, and movie/audio behavior through their existing
paths; those keys have not yet been migrated to the parsed registry.

The remaining generic-profile gap is semantic application of further portable settings. In particular,
click-cancel, Auto/Skip coexistence, cursor policy, redraw policy, and wheel action ids must be decided from
this evidence instead of AGE's compiled defaults. The port deliberately redirects the native profile to
Godot `user://`: save payloads remain under `user://SAVE`, while engine options retain AGE's native
`SYS4REG.INI` filename and format at `user://SYS4REG.INI`.
It should not reproduce legacy DirectDraw selection or native registration/key validation.

### ops `0x1a2`/`0x1a3` store and restore shared `SAVE.DAT` integer cells (resolved 2026-07-20)

The SCJUMP slice assumed `u00428010` resolved a decision value to a scene. **That premise is wrong**,
and pinning the real handler plus its paired reader resolves the service:

- **VA-drift trap:** Kelebek's `u00428010` = op `0x1a2`. But Kelebek's raw VA `0x428010`, in *our*
  build, sits inside a *different* handler `0x427fb0`, which is **op `0x1ac`** (per the table:
  `ctx[0x26e3f]=0x427fb0`). Op `0x1ac` is a **save-path** op — its handler formats
  `%s\SAVE%2.2d.DAT` (format string `0x571e70`) and is multi-operand. Reading the raw VA gave the
  wrong opcode.
- **`0x1a2` = store.** `op_0x1a2_store_shared_profile_int@0x42d360` records the generic three-dword
  instruction length, reads operand 1's current raw 32-bit value, and resolves its actual cell index with
  `vm_operand_lvalue`. That accessor accepts a direct global integer (type 3), global pointer (type 6), or
  local pointer (type `0xc`); pointer forms resolve to an index relative to the global integer bank. The
  handler formats the key `3%08x` and insert-or-assigns the value in the table at `ctx+0x5190`.
- **`0x1a3` = load.** `op_0x1a3_load_shared_profile_int@0x427e90` resolves the identical cell index,
  calls `shared_profile_int_lookup@0x4199d0`, and writes the result back through `vm_operand_write`.
  A missing key returns **zero**. The old `string-lookup-set` label described neither its type nor effect.
- **The consumer is shared `SAVE.DAT`.** `shared_profile_payload_write@0x430a20` enumerates this table
  and writes its entry count followed by one 12-byte key and one 32-bit value per entry. It is invoked by
  `shared_profile_save@0x40c950`. `shared_profile_payload_read@0x431070`, called by
  `shared_profile_load@0x40ccd0`, reconstructs the same table with insert-or-assign. Numbered
  `SAVE##.DAT` paths use the surrounding state object only for container metadata/timing; they do not
  enumerate this table. `RT.DAT` independently stores `ReadTextDB`.
- **The generic hash helper caused the earlier conflation.** `hash_table_insert_or_assign@0x42cf70` and
  `hash_table_find_value_ptr@0x419290` operate on whichever table ECX selects. `0x1a2` selects
  `ctx+0x5190`; value-switch ops `0xa2`/`0xa3` select the distinct temporary table at `ctx+0x5f6c0`;
  engine settings and text caches use still other instances.
- **Corpus shape matches profile persistence.** The corpus has 17,585 `0x1a2` calls in 315 scripts:
  17,539 operate on a local pointer immediately resolved by `lookup-array`, while 46 name a global cell
  directly. The paired `0x1a3` appears 73 times in 12 scripts. `LOADCONFIG.BIN` restores configuration
  globals with consecutive loads; `SYSTEM4.BIN` stores its initialized-config flag; gameplay and ADV
  scripts store selected array cells rather than the whole VM global bank.

**Port verdict (implemented 2026-07-24):** `SharedProfile` now owns the selected
`global-cell-index → raw-int32` map across VM/script lifetimes. Opcode `0x1a2` upserts and `0x1a3`
loads-or-zero for direct globals and local pointers resolved into the global bank. The profile is shared by
fresh scene VMs through `GameSession`, while `GameSession` JSON remains intentionally unchanged and does not
silently alias this native profile domain. The paired string service and native `SAVE.DAT` payload/store
lifecycle are implemented with it; see the persistence-family section below.

The FIELD snippet `lookup(0x5f0ed, 0x62ccf); mov(ptr,1); lookup(...); 0x1a2(ptr)` therefore persists that
selected global array cell to the shared profile. It does not resolve decision→scene; scene dispatch remains
the separate call-script/progression path documented in `name-resolution.md` and `scjump-progression.md`.

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
- **`asset_open_indexed_entry@0x44f390`** (resolver — the key): its `this` is the embedded FileDB at
  `EngineCtx+0x9c24c`, not EngineCtx itself. A base `record = [FileDB+0x414] + id*0x50`. The record is exactly the
  **SYS4INI 80-byte layout** `{name[64], arc_id@0x40, file_number@0x44, offset@0x48, size@0x4c}`
  (count = `[FileDB+0x40c]`, archive-name table = `[FileDB+0x410]`; absolute EngineCtx fields
  `+0x9c658/+0x9c65c/+0x9c660`). It tries a **loose override first**
  (`CreateFileA` on `record.name` → the mod/patch hook point), else opens archive
  `[record.arc_id*0x100 + FileDB+0x410]`, `SetFilePointer` to `record.offset`, size = `record.size`.
  High-byte-tagged ids select `[FileDB+0x3028 + signed_selector*4]` and use the low 24 bits as the
  selected AAI record. The base corpus has no explicit high-byte call-script operand; INIT2 op `0x143`
  supplies mounted record-zero ids dynamically.

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

**Port status:** the C# VM resolves both immediate and computed resource ids through `IScriptProvider`,
pushes an `ExecFrame`, runs the child, and resumes its caller. This is the same mechanism needed for the
natural root described below; an out-of-band scene-name registry is not required.

#### Natural boot and New Game control spine (B0, 2026-07-20)

SYSTEM4 is the long-lived script root and the game's actual scene coordinator. Its initial path establishes
the nine ADV layouts and system surfaces, chooses `LOADCONFIG.BIN` or `INITCONFIG.BIN`, calls `INIT2.BIN`,
optionally calls `LOGO.BIN` and `OP.BIN`, calls the one-op `INIT.BIN`, and enters `TITLE.BIN`. `INIT2` is
not a thin handle seed: it calls 23 data initializers in order (`EBINIT`, `CNINIT`, `ITINIT`, `SKINIT`,
`ILINIT`, `AFINIT`, `TRINIT`, `MAINIT`, `ALINIT`, `CDINIT2`, `MPINIT`, `LAINIT`, `OBINIT`, `STINIT2`,
`RTINIT`, `CGINIT`, `SPINIT`, `CTINIT`, `CVINIT`, `CIINIT`, `VIINIT`, `SCINIT`, `BTANINIT2`) and then
`TUNE.BIN`. The CLI `play --boot` nine-script list is therefore only a partial diagnostic approximation.
Godot now runs SYSTEM4 itself by default, so this complete sequence and its host-visible side effects execute
in one VM; Godot `--boot` remains only for an explicit direct-scene diagnostic such as `--scene SC0000`.

**Startup-video gate and modal player (2026-07-20).** The apparently optional `LOGO`/`OP` calls are
deterministic first-process-run behavior, not a profile/save decision. `SYSTEM4@0x29a` calls op `0x130` and
executes `LOGO.BIN@0x2a4` then `OP.BIN@0x2a7` when its result is nonzero. Native
`op_0x130_get_initial_root_run@0x4295b0` returns `EngineCtx+0x54ff0`; context construction initializes that
field to one, while `op_0x9_reset_scene_and_reload_root` is its only later writer and clears it before
resetting engine state and reloading root script id zero. The port now owns the same flag in the persistent
VM: it begins at one,
`0x130` writes it, and op `0x9` clears it as part of the whole-stack root reload described below. It is not
a boot seed or profile value.

**Whole-stack root reload (`0x9`, 2026-07-20).** Native
`op_0x9_reset_scene_and_reload_root@0x418f50` is not an ordinary child-script return. It clears the
initial-root flag, calls `script_frame_dispose@0x40e610` across all 40 interpreter slots, cancels timed and
input-callback state, invokes `scene_context_init_reset@0x40b3b0`, resets hotspot/input services, and finally
loads raw script resource zero with `script_frame_load_resource(..., 0)`. Raw SYS4INI index zero is
`SYSTEM4.BIN` in Himegari.

The scene reset owns interpreter run state, ADV input/skip/auto state, retained gfx objects and command
queues, text/render buffers, and—on the normal fresh-session path—the 1000 ordinary surface/movie slots.
It does not clear the global VM bank or engine configuration. The port now mirrors that boundary: an `0x9`
request propagates through every nested `call-script` frame without executing any caller continuation,
clears scene-owned VM/Godot presentation and input state, cancels deferred SFX starts without unloading or
stopping active channels/BGM, preserves global/external-global banks and process-owned configuration/caches,
then begins raw script zero at offset zero in the same VM session.
The retained ADV history backlog remains intact for now because its lifetime across this reset has not yet
been proven; only recording suppression is reset. Focused tests cover a three-frame unwind and raw-zero
resolution to `SYSTEM4.BIN`.

**Frontend exit request is not a root reload (`0x1`, 2026-07-20).** Native
`op_0x1_throw_exit_request@0x4162e0` constructs a four-byte value-one payload and throws it with
`ThrowInfo@0x5a9710`. The catchable-type metadata resolves that object to the named RTTI type
`Command_Exit_Exception` (`TypeDescriptor@0x5b13e8`), rather than an undifferentiated integer exception.
Its only two corpus sites establish the intent: TITLE executes it after the fifth main-menu action's sound
and delay, while SYSTEM4 executes it after reporting an invalid execution mode.

The catcher is the outer native message/scheduler loop now recovered as
`engine_main_tick_with_exception_policy@0x411840`. Its MSVC `FuncInfo@0x5a9750` has a typed catch entry for
`Command_Exit_Exception` at `catch_CommandExitException_set_exit_result@0x412648`. That funclet forces the
enclosing result to one and returns continuation `0x412961`, which performs loop teardown and returns to the
frontend. It never changes `frame_pc`. This matters because the dispatcher itself advances `frame_pc` only
after an opcode handler returns; `0x1` throws instead, so neither the dispatcher nor the catcher advances
past it. A distinct generic error-dialog policy at `0x412689` proves that fall-through is explicit: result
two retries the same instruction, result four adds the decoded instruction length before restarting the
loop, and other results exit. `Command_Exit_Exception` bypasses that policy entirely.

The Windows/frontend policy that follows the returned exit request—full exit versus returning to
title—is outside this opcode handler and is not implemented in the Godot frontend yet.

TITLE happens to contain a developer menu immediately after its `0x1`, including a `call-script` to
`DEBUG.BIN`; that code is unreachable in the native flow because the handler never returns. The port's
former unknown-op fallback did expose that menu when selecting TITLE's fifth action, providing a useful
visual confirmation of the static mapping but not a legitimate retail route. Opcode `0x1` now propagates a
process-exit request through hotspot callbacks and nested script frames and ends the VM session without
executing the following bytecode. An explicit Godot `--native-debug-menu` diagnostic can deliberately
restore the old fall-through for developer archaeology; it maps to `VmOptions.IgnoreExitRequests`, defaults
off, and is not part of the native compatibility path. Because it changes the opcode globally, it also
suppresses SYSTEM4's invalid-execution-mode exit site during that run. End-to-end visual validation of the native
`SYSTEM4 -> TITLE -> child -> 0x9 -> SYSTEM4 -> TITLE` history remains deferred until the frontend
exit/return-to-title boundary or a natural game-over/completion route exists.

The unreachable developer menu nevertheless records the game's intended debug-scene handoff. Its two ADV
viewer choices write `G[0]=1`, `G[0xaba5c]=-1`, `G[0x62ccf]=0`, and a raw script id into `G[0x699]`, then
return TITLE with local result one. TITLE's outer loop performs its normal ADV input/skip exit pair and
returns to SYSTEM4. SYSTEM4 resumes at `0x2b0`; the nonzero `G[0xaba5c]` suppresses SCJUMP remapping, so the
coordinator keeps the requested `G[0x699]`, performs its normal scene-entry setup, and calls that script at
`0x477`. Other developer choices directly call utility scripts such as `DEBUG.BIN` from TITLE instead.

The complete post-`0x1` menu accounts for all eight base-catalog `DEBUG*.BIN` records; no other fixed-id
caller was found in the corpus. `ADVデバッグ` writes packed id `0x325f` (`DEBUGADV.BIN`) to `G[0x699]`, and
`ADVデバッグ（PG用）` similarly writes `0x3294` (`DEBUGADV2.BIN`); these are the two coordinator-return paths.
The remaining choices are direct nested TITLE calls: `迷宮` calls `0x338c`/`DEBUGMAP.BIN`; both the base and
PG-oriented dungeon/base choices use `0x338d`/`DEBUGMAP2.BIN` with different surrounding mode writes;
`迷宮（敵確認用）` calls `0x3390`/`DEBUGMAP3.BIN`; `戦闘` calls `0x338e`/`DEBUGBTL.BIN` then `BTL.BIN`;
`戦闘エフェクト` calls `DEBUGBTL.BIN`, `0x338f`/`DEBUGANIME.BIN`, then `BTL.BIN`; and `システムテスト`
calls `0x3391`/`DEBUG.BIN`. Thus every authored path is behind the same non-returning exit request in this
retail executable. The generic SYSTEM4 computed call can technically accept any packed id, but no separate
normal-game writer of these eight fixed ids appears in the static corpus.

This also confirms three distinct cleanup owners around a debug launch: the selected script's own terminal
subroutines, SYSTEM4's ordinary post-child cleanup (including all ten SFX channels and retained scene
objects), and op `0x9`'s whole-stack scene reset when that opcode is actually executed. An arbitrary VM
script replacement would bypass the first two and is not equivalent to native scene dispatch.

Both child scripts create/draw 800x600 surface 42 and call op `0x20f`; their resource ids are universal raw
SYS4INI indexes `0x335f`/`LOGO.AGF` and `0x3364`/`OP.AGF`. `ED.BIN` is the only other corpus user, with
`0x3324`/`ED.AGF`. All three payloads begin with MPEG program-stream pack code `00 00 01 BA`.
`op_0x20f_play_modal_movie_to_surface@0x422e50` shares the movie allocation/open/audio setup used by
non-modal scene-movie op `0x236`, then starts playback and sets run-state bit `0x2000`; the engine main loop
and window procedure treat that state as the modal whole-movie service. This parks the script at the opcode
until EOF or input cancellation, after which the script's following instructions release the object/surface.
The port implements `0x20f` through a typed packed-movie resolver and a distinct modal host call. The existing
asynchronous decoder publishes frames through the retained surface while only the VM thread is parked;
EOF, mouse click, Accept, or Cancel resumes the wrapper so its scripted cleanup releases surface 42.
`0x236` retains its non-modal contract; native passes its packed resource operand through the same universal
catalog opener as `0x20f`. Both paths now decode the MPEG audio pin through FFmpeg into timestamped stereo
float PCM. Godot owns one `AudioStreamGenerator` per playback instance, applies the native flag-selected
mute/music/SE/voice or default movie route, and uses the sound-hardware position as the presentation clock.

**Native first-frame preroll and stream origin (2026-07-28):** AGE does not seek or manually decode an
opening frame. `movie_to_texture_open_asset_graph@0x463e20` finishes with the DirectShow graph stopped.
`movie_start_modal_playback@0x463280` then calls `IMediaControl::Run` at vtable slot `+0x1c`; there is no
preceding `Pause` or `IMediaPosition` mutation in `0x20f`. DirectShow's stopped-to-running transition
necessarily passes through paused/preroll. For a file source, the graph manager waits for each renderer to
receive a sample; a video renderer displays that held first sample as a poster image before the graph enters
running state. See Microsoft's [Filter States](https://learn.microsoft.com/en-us/windows/win32/directshow/filter-states)
contract.

The custom `movie_texture_renderer_receive_sample@0x4628d0` copies that preroll sample into the D3D texture
and sets renderer `+0x5b4`. `movie_consume_renderer_new_frame_flag@0x405120` clears the flag and makes the
outer engine tick render the retained surface. Audio is cued during the same graph transition but begins only
with the running reference clock. Native observation adds the decisive cadence constraint: OP's image advances
immediately with its audio; it does not hold the preroll image for roughly 600 ms. AGE contains no later
timestamp correction, so the DirectShow splitter/graph is necessarily presenting the video stream relative to
its first sample rather than exposing FFmpeg's shared program-stream timestamp origin. This last mapping is an
inference about DirectShow internals, but the visible native behavior is the parity oracle.

The installed FFmpeg probe rules out dropped or transparent source frames. LOGO and OP first report video PTS
600/601 ms on the shim's shared mux timeline, then deliver every tested opening frame at consecutive 33/34 ms
steps; pixels change within the first 20 frames. Every decoded alpha byte in those frames is 255. MPEG-1 has no
alpha plane. Native `movie_texture_renderer_set_media_type@0x463750` accepts RGB24/RGB32, and the RGB24 copy
path promotes pixels to alpha `0xff` before comparing the complete packed ARGB value with its renderer key.
Opcode `0x20f` attaches that renderer to the existing D3D target and never reloads the retained surface or
assigns it an RGB color key. The former port did both `Gfx.SetSurface(..., colorKey: 0)` and RGB-only keying,
which incorrectly made exact-black movie texels transparent and explained grey-background MPEG speckles.

The corrected port publishes frame zero before starting audio, then schedules all video frames from
`source_video_pts - first_video_pts`; audio retains its own complete stream from timestamp zero. No video or
audio samples are discarded. Signal-bearing PCM in OP's first 600 ms proves only that the audio data is real,
not that the MPEG mux's cross-stream start offset is an intended 600 ms visible lead.

An existing native operand trace identifies every observed heap codebase by a 100% match against its static
instruction-offset set. The captured New Game route is:

`TITLE → GAMESTART → UNITECH → CALCARR → GAMESTART → TUNE → GAMESTART → TITLE → SYSTEM4 → SC0000`.

The transition sites make the ownership explicit. `TITLE@0x31c` calls `GAMESTART`. The selected New Game
path calls `UNITECH@0xd47` (which calls `CALCARR@0x4ca`), later calls `TUNE@0x1338`, writes flow result
`G[0]=1` and SCJUMP decision `G[0x62ccf]=0`, and returns. SYSTEM4 resumes at `0x2b0`, prepares the ADV
scene boundary, resolves `G[0x87a57][G[0x62ccf]]` into next-script resource `G[0x699]`, falls back to raw
SYS4INI id `0x22` (`SC0000.BIN`) when the mapping is zero, and executes computed `call-script@0x477`.
Thus normal scenes remain nested script frames under SYSTEM4 and return to it; the port should keep one
VM/host session rooted at SYSTEM4 rather than replace top-level VMs based on a host-invented scene result.

**Port landing (2026-07-20).** The no-argument Godot path is rooted at SYSTEM4 and renders TITLE without
manual inherited-layout or surface injection. A real-script integration test drives the same input callback
lifecycle through TITLE and GAMESTART and observes SYSTEM4 enter SC0000 with `G[0]=1`, `G[0x699]=0x22`,
and script-produced `G[0x6c1]=1`. Direct `--scene` launches retain the old bootstrap strictly as a diagnostic.

`tools/frida/capture_script_loads.py` hooks `script_frame_load_resource@0x40e980` and reads its third stack
argument (the raw packed resource id) for direct name resolution. It is attach-only: attempting to gate the
installed executable at process start with this loader hook produced Protection Error 45 and no records.
No protection bypass or executable patch is part of the investigation.

---

### op `0x215` (`query-gfx-object?`) is a retained gfx-object query — settles the render drift as (b) (corrected 2026-07-20)

**This is the canonical account of the background/sprite "drift" bug** (background pinned off-centre /
bottom-right, rest grey — `Screenshot 2026-07-06 211353.png`). It supersedes the earlier "drift =
state-divergence, seed state and it's fixed" conclusion in `docs/phase-a-slice-plan.md` and the status
memory, which are corrected to point here.

Resolved via the dispatch table (`ctx[0x26c93 + 0x215]`): the registration routine `FUN_00413860` stores
`[ESI + 0x9baa0] = 0x42a0b0`, so op `0x215`'s **real handler is `FUN_0042a0b0`**. (Kelebek's `0x421160` is
VA-drift — it lands inside the unrelated `FUN_00421090`. Same lesson as `0x1a2`: never trust a Kelebek raw VA.)

`FUN_0042a0b0(ctx)` does exactly two things:
1. **`*(ctx + 0x53d88 + ctx[0x53d14]*0x78) = 5`** — records this opcode's generic encoded length
   (one opcode dword plus two dwords per operand). This is interpreter bookkeeping, not a gfx side effect.
2. **`out = FUN_0047f280(FUN_0041b940(2))`** — `FUN_0041b940(2)` fetches operand 2 (the bytecode handle
   key); `FUN_0047f280` is a **`std::map::find`** over an engine-internal associative registry, returning
   the mapped value or **`0xffffffff` (not-found)**; `FUN_00425fb0(1, out)` writes it to operand 1. That
   registry is populated by the retained-object draw/geometry workers. Op `0x1a2`'s shared-profile integer
   table is separate and does not populate this map.

**(a) vs (b) — the verdict is (b).** The value `0x215` returns is **native retained-object state**: "has a
gfx object already been created under this handle, and what is its source slot?" (`≥0` = existing → use its slot;
`-1` = absent). That
state lives in the engine's own registry, maintained by the gfx ops, **not in the VM global bank**. So
**seeding story-state globals cannot reproduce it** — the drift is *not* the Phase-B state-divergence
problem. Stubbing `0x215` returns a constant → `label_12649`'s slot-select always takes one branch → every
draw collapses onto slot 0 → the anchor-preserve math measures foreign-sized textures → cumulative drift.

**Why the prior "state-divergence" conclusion was wrong.** It was grounded in `capture_gfx_objects.py`,
which polled `[esi+0x53d64]` at ~2/s and mistook the engine's `0x78`-byte **script-context records** for gfx
objects. The branch is driven by the retained-object map at `ctx+0x46614+0x408`, a different structure the
probe never observed. Absence in that capture therefore says nothing about the native gfx-object path.

**The fix is tractable and Frida-free.** (b) does *not* mean an opaque native state machine. The subsystem
is a **modelable data structure**: retained object records (slot / geometry / draw state) behind a
handle→object registry (a `std::map`). Geometry and draw workers lazily populate that retained-object map;
query and erase workers read/remove the same entries. Op `0x1a2` also maintains a shared-profile integer table,
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
the retained-object `std::map` with `0x1a2`'s open-addressing shared-profile integer table. The useful part of
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

#### ADV layer surface-slot registry (`G[0x3239]`, closed 2026-07-23)

The former auto label `rec[s3]` is the eight-row `adv_layer_surface_slots` registry. Each scene's
setup helper writes the same three banks: column zero `primary_surface_slot` = 4..11, column one
`alternate_surface_slot` = 43..50, and column two `transition_surface_slot` = 51..58. The shape is
pervasive rather than SC0000-specific: 309 scripts reference the base, with 2,657 table-base
accesses; 143 scripts contain the initializer.

The common CG loader proves the column roles. `adv_gfx_layer_index` (`G[0x62450]`, always assigned
0..7) selects both this row and `adv_gfx_object_handles` (`G[0x62455]`). For a fresh retained object,
the loader uses the primary slot. For an already-bound object, it compares the live slot returned
by op `0x215` in `adv_gfx_surface_slot_work` (`G[0x62452]`) and chooses the other member of the
primary/alternate pair before rebinding. Column two is released and reused by the transition path.
The resource passed to `set-texture` is the transient `adv_gfx_resource_id` (`G[0x62424]`).

INIT2 seeds the first nine cells of the 20-cell retained-object handle array with `0xcb20`,
`0xcb2a`, `0xcb8e`..`0xcbc0`, and `0xcf08`. The eight-row layer table governs indices zero through
seven; the ninth handle is a fixed extra object outside that row-indexed layer set. These names now
live canonically in `vm-map/globals.toml`.

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
VA-drift). It records the generic 9-dword instruction length, copies operand-2/3 strings (`"LABEL"`, `"J"`) + operand-4 int, calls
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

#### gfx opcode contract table (corrected 2026-07-20; full family reversed)

Every handler first writes its encoded instruction length in dwords to the current **script-frame** record
(`*(ctx + 0x53d88 + ctx[0x53d14]*0x78) = 1 + 2*argc`), then fetches operands via
`FUN_0041b940(i)` (1-based). Gfx handlers then either **SET** retained-object fields (call a native worker
`FUN_0047xxxx`) or **QUERY** them (write results back via `FUN_00425fb0(i, val)`). The length write is generic
interpreter bookkeeping and is not part of the gfx contract. Handlers resolved through
the dispatch table (`ctx[0x26c93+op]`). The later ADV-text correction below removes `0x212/0x213`
from this family: their shared pointer table was initially mistaken for retained gfx objects.

| op | handler | words | dir | argc | contract |
|---|---|---|---|---|---|
| `0x1f7` | `0x422270` | 5 | erase | 2 | retained-object erase: `op2>1` → `gfx_object_erase_range(op1,op2)` erases `[op1,op1+op2)`, else `gfx_object_erase(op1)` |
| `0x1fa` | `0x4224a0` | 3 | set | 1 | release **surface slot** `ctx+0x52bd4[op1]` (vtbl free) + `FUN_00474e40(op1)` |
| `0x1ff` | `0x4227b0` | 9 | set | 4 | 3 int→float params on obj op1 → `FUN_0047e800(op1,f2,f3,f4)` |
| `0x202` | `0x4228d0` | 0xb | set | 5 | blit obj op1 with (op2,op3) + **packed ARGB** from op4(alpha)/op5(color) → `FUN_0047ea00` |
| `0x203` | `0x4229a0` | 9 | set | 4 | draw obj op1 with op2 + packed color(op3/op4) → `FUN_0047e9b0` |
| `0x212` | `0x4230c0` | 5 | ADV | 2 | layout `op1` retained wait-indicator handle `+0x64 = op2` |
| `0x213` | `0x423110` | 7 | ADV | 3 | layout `op1` retained glyph interval `+0x68 = op2` (first handle), `+0x6c = op3` (capacity) |
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
anchor math reads garbage). Both read object state the SET ops (`0x217`/`0x219`/`0x1ff`) wrote — all
bytecode-driven, all host-modelable.

**Model implication for the host-side reimplementation (Phase 2 input).** The subsystem is a set of
per-object records keyed by handle, carrying: a live source **slot** (written by draw-texture, queried by `0x215`), a **position 3-vector**
(`0x21a` get / a matching set), and an **anchor 3-vector** (`0x218` get / `0x217` set), plus color/blit
params (`0x202`/`0x203`). The native workers (`FUN_0047xxxx` = the DirectDraw/surface layer) need **not** be
modelled — only the object-record data model, so the QUERY ops return what the SET ops stored. That makes
`0x215`/`0x216`/`0x218`/`0x21a` return correct values and the existing bytecode geometry math produces
correct `dst`/`w`/`h`. Ancillary per-object tables observed: `ctx+0x46d14` (stride `0x14`),
`ctx+0x52bd4` (element pointers), plus the `0x408` registry.

**ADV pointer-table correction (2026-07-27).** `ctx+0x14d54` is not a gfx-object table. It is
text-manager `ctx+0x14940` plus the ADV layout pointer table at manager `+0x414`. Opcode `0x212` writes
layout `+0x64`, consumed by `adv_text_publish_wait_indicator_frame@0x453120` as the retained indicator
handle. Opcode `0x213` writes layout `+0x68/+0x6c`, consumed by
`adv_text_publish_next_glyph@0x451220` as first glyph handle plus capacity and erased by layout reset/
publication. SYSTEM4 is the complete corpus: one `0x212(1,0xd674)` and nine `0x213` layout ranges, with
layout 1 assigned `0xd6d8..0xd8cb`.

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
a reset: it clears the object's detached-animation control word after binding. Its consumer and complete
lifecycle are documented under “Detached finite object animation” below.

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
- `0x1fe(handle, axis_x, axis_y, axis_z, angle_degrees)` is the immediate-current rotation setter.
  `op_0x1fe_set_rotation_current` (`0x422700`) converts operands 2–5 to floats and calls
  `gfx_object_set_rotation_current` (`0x47e720`). The worker stores current axis at
  `obj+0x1ec..0x1f4`, current angle in degrees at `obj+0x204`, converts the angle to radians, writes the
  current axis-angle matrix at `obj+0xec`, marks transform state at `obj+0x68`, and raises retained-gfx
  redraw dirty at manager `+0xb558` (`EngineCtx+0x51b6c`). It is the direct-current companion to
  `0x21f`, not another timed channel. Of 187 corpus calls, 186 use Z axis `(0,0,1)`; the lone DEBUG call
  uses Y axis `(0,1,0)`. SC0010 uses immediate `5`, `-5`, and `0` degree Z rotations in a wobble setup.
- `0x21f` converts operands 4–7 to floats and calls `gfx_object_set_rotation_channel` (`0x47eb70`). It
  stores delay/duration at `obj+0x40/+0x54`, target axis at `obj+0x1f8..0x200`, target angle (degrees)
  at `obj+0x208`, and the target axis-angle matrix at `obj+0x12c`. Current axis/angle are
  `obj+0x1ec..0x1f4/+0x204`, with current matrix `obj+0xec`.
- `gfx_object_apply_transform_channels` (`0x472f00`) supplies the timing contract. All three channels use
  shared start timestamp `obj+0x34` and retained-gfx frame-time `owner+0xb550`
  (`EngineCtx+0x51b64`), but have independent delay/duration:
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

- **`0x238 set_anim_clock` (argc 1, 3-dword instruction):** `ctx+0x51b78 = 0` (elapsed), `ctx+0x51b7c = operand1`
  (total duration). **A GLOBAL, NON-BLOCKING clock** — not per-object. The op only *configures* the clock; it
  does **not** loop/wait. The native render loop advances this clock each frame and interpolates *all* animating
  objects. Its own plate comment states the payoff: "our port can drive animation in the host's per-frame loop
  while the VM is parked at wait-for-input; no VM/host frame-lockstep." → **validates the wall-clock-tween
  architecture directly.** SC0000: `set-anim-clock(G[0x624bb])` @`0x123bd`, `set-anim-clock(0x190=400)` @`0x13858`.
- **`0x234 anim_start` (legacy mnemonic; argc 5, 11-dword instruction):** following worker
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
`retained-gfx owner+0xb550` (`EngineCtx+0x51b64`) using their own delay/duration; they do not use op
`0x238` as their duration.
Op `0x234` is the independent rotation cycle above. Op `0x238` still configures the separate
`ctx+0x51b78/+0x51b7c` animation service used by its own family.

##### Shared frame clock and cyclic source-cell cadence (2026-07-22)

The native scheduler confirms the shared-clock recollection, with one important distinction between a
shared **timebase** and universal phase. `engine_main_tick_with_exception_policy@0x411840` samples
`timeGetTime()` once per active outer presentation tick, first shifting retained-gfx manager current time
`+0xb550` (`EngineCtx+0x51b64`) into previous time `+0xb554` (`EngineCtx+0x51b68`). Opcode `0x23c`
explicitly performs the same shift/sample; it does not create another timer or impose a fixed frame rate.
Every cyclic object channel reads that shared pair, but each channel owns a start timestamp and period in
its retained object record. `gfx_animation_service_poll@0x407640` and op `0x238`'s
manager `+0xb564/+0xb568` start/duration window are separate finite-animation service state, not the cyclic
spritesheet clock.

Op `0x231` is already natively cell-change-driven. `gfx_worker_anim_srcrect@0x47eec0` resets object start
`+0x21c` and stores period `+0x230`, frame count `+0x238`, and columns `+0x23c`.
`gfx_object_anim_interpolate@0x473ed0` seeds that start from the shared current timestamp on the first
sample and selects `floor((current-start)/period) % frame_count`. It performs the same calculation with the
shared previous timestamp and raises manager redraw-dirty `+0xb558` only when the selected cells differ.
Other retained mutations raise dirty directly; continuously varying cyclic channels keep it raised. The
outer loop renders only for retained dirty state or a new movie frame. Merely completing an opcode `0xc8`
sleep/poll interval is not itself a native graphics-dirty request.

FIELD's DEBUGMAP unit family is more tightly synchronized than the general model permits. At
`FIELD@0x924a`, the script configures prototype handle `0x9c40` as `(period=200 ms, frames=4, columns=2)`.
The following loop calls `DRAWCH`, whose `0x21d` clones that complete retained record from `0x9c40` to
`0x9c40+entity_index`. Ordinary bytecode executes burst-fast and there is no render between prototype setup
and those clones, so every copied start remains zero and their first render seeds the same shared timestamp.
Those unit sprites are therefore phase-locked at one cell change every 200 ms (5 Hz), even though the
engine generally permits different object-local starts and periods.

FIELD's per-tile movement animation uses two retained objects. At `0x45c9`/`0x4686` and again at
`0x51fd`, it clones the bound idle handle `0x9c40+entity_index` into temporary moving handle
`0x9c40+0x76c`. The following mode-0 `0x203` write sets the idle handle's alpha to zero, while translation
`0x220` and source-cell endpoint `0x239` animate only the temporary handle. After the presentation boundary,
`DRAWCH@0x51ec` rebinds the idle handle at the new tile; a multi-tile route immediately repeats the
clone/suppress cycle. Treating every static mode-0 alpha byte on a loaded texture as inert therefore leaves
the old idle sprite composited beneath the moving clone until each segment ends. The port records the
post-bind static-alpha consumption as a transient opacity latch, cleared by the next `draw-texture` bind.
This preserves ADV's distinct pre-bind/static alpha-zero CG initialization.

The same ordering rule applies when an ADV callback rebuilds the message window after a modal menu.
`CALLBACK_SETTING` erases `0xd2f0..0xd2f6`, binds a fresh `SO001` crop to backing handle `0xd2f0`, reads
`message:MesWinAlpha`, and only then applies `(16-value)<<4` through mode-0 `0x203`. The erase discards the
backing's earlier one-shot-color provenance, so classifying only animated or looping objects made every
rebuilt message window opaque. Post-bind static alpha now controls this fresh backing as well; repeated
CONFIG changes therefore take effect immediately while the enclosing ADV wait remains active.

The same route exposed an independent publication-atomicity requirement. Each tile step reaches
`FIELD@0x4eb2 -> 0x9225`, which calls `DRAWMAP`, `DRAWOBJ`, and `DRAWMINIMAP`. `DRAWMAP` first erases its
old terrain handle ranges and then rebuilds them; `DRAWMINIMAP` recreates mutable surfaces `0x42..0x44`
from transparent pixels before filling the terrain and copying object/unit markers. Those are ordinary
VM-side construction bursts, not a series of front-buffer presents. Letting Godot's render thread sample
between their individual erase/fill/copy instructions displayed a partially blank minimap at every tile
and could display the erased terrain state for one frame when a complete route returned. The interactive
host now holds one writer-side presentation barrier for the whole script burst. Explicit presentation,
transition, sleep, text, movie, and input services temporarily expose the completed state; callbacks
serviced inside a parked input wait reacquire the writer side for their own atomic burst.

**Port implication:** replace the current “any spritesheet is active” redraw predicate with native-style
shared current/previous sampling and request a composition only when at least one visible sampled cell
changes. The FIELD `sleep(1)` input-poll loop must also stop requesting a redraw when no retained mutation
occurred. This is a fidelity correction as well as an optimization; it preserves phase-locking for cloned
records and does not invent 51 independent timers.

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
  records its 17-dword instruction length and calls **`gfx_object_bind_draw`@`0x47e870`**, which on the object keyed by `handle`
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
- The handler also records its generic **3-dword instruction length** in the current script-frame record
  (`ctx+0x53d88+curidx*0x78`) and runs
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

The native timer arm clamps every duration to at least one millisecond. This is semantically important for
menu scripts: ROOM's steady input loop uses `sleep 0` as a one-engine-tick yield. The Godot host formerly
allowed a zero deadline, turning that loop into a free-running burst that exhausted the VM step guard and
showed `-end-`; it now applies the native minimum after the debug speed multiplier.

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
  configures that channel. `gfx_object_anim_interpolate` consumes it from retained-gfx frame-time
  `owner+0xb550` (`EngineCtx+0x51b64`)
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
- **Objects** — the retained-gfx `owner+0x408` registry (`EngineCtx+0x46a1c`), keyed by handle (a
  `std::map`). `gfx_op_0x1fb_draw_bind` (`0x422510`,
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

#### Raw mode-1 surface load — opcode `0x249` (2026-07-21)

`op_0x249_load_raw_texture_surface@0x424b20` has ABI
`(universal_packed_catalog_id, surface_slot, RGB_colorkey)`. Its release, movie detach, indexed-asset open,
colorkey conversion, failure exception, and stream cleanup are the same as `0x1f9`. The only native loader
difference is the last argument to `gfx_surface_load_asset@0x477c40`: `0x1f9` passes mode 0, while `0x249`
passes mode 1. `gfx_surface_decode_and_create@0x474e90` selects a base 0x450-byte texture object for mode 0
and a derived 0x460-byte texture object for mode 1. A successful mode-0 load records the resource id in its
device-reload record; mode 1 records `-1`.

The derived class is specifically a **tiled large-image surface**, not an alternate pixel format,
spritesheet interpretation, or blend mode. `gfx_tiled_surface_create@0x432ff0` divides the logical image into
`ceil(width / DAT_005b15b0) × ceil(height / DAT_005b15b0)` ordinary mode-0 child surfaces.
`gfx_tiled_surface_upload_agf@0x431a10` decodes indexed 1/4/8-bpp and 24/32-bpp input and uploads each tile;
`gfx_tiled_surface_blit@0x4316b0` divides any requested logical source rectangle across the intersecting
children and adjusts their destinations. A CPU compositor can therefore keep one contiguous decoded RGBA
image without losing the mode-1 behavior relevant to Himegari.

The corpus makes the addressing and first gameplay consequence concrete. FIELD calls `0x249` with raw ids
`0x32da..0x32dd`, which are SYS4INI entries `SO005`, `SO007`, `SO008A`, and `SO007A`, into slots
`0x3e..0x41`. `DRAWMAP.BIN` then creates the dungeon tile objects almost entirely from slots `0x3e` and
`0x3f`. A skipped `0x249` therefore leaves the surrounding UI operational but the central map black. After
implementing it, the first DEBUGMAP retest exposed a separate host blit error: FIELD intentionally binds a
zero-width/zero-height SO005 prototype object, while the port expanded zero dimensions to the full texture.
Native `gfx_object_blit_d3d9@0x4774c0` clips the explicit source rectangle and returns when
`right<=left || bottom<=top`; mode-1's tiled blit likewise visits no children for an empty rectangle. The
port now preserves that empty draw rather than leaking the complete SO005 sheet.

#### Selected retained-object range transform — opcodes `0x229`/`0x22a`/`0x22c`/`0x22d` (2026-07-21)

`0x229` was formerly misclassified as a second per-object position setter. Native
`op_0x229_set_gfx_range_transform@0x423700` instead resets an embedded gfx-object record at retained-gfx
owner `+0x428`, writes `(first_handle,count)` to owner `+0x420/+0x424`, and writes operands 3..5 as that
embedded object's anchor at owner `+0x440..+0x448`. The actual per-object direct-position opcode remains
`0x22f`.

On every render frame, `gfx_range_transform_sample_frame@0x476df0` samples the embedded object's ordinary
scale/rotation/translation channels into owner matrix `+0xb5b4`. `gfx_object_composite@0x47f650`
post-multiplies an object's normal matrix by this shared matrix only when its handle is in
`[first_handle, first_handle+count)`. The sibling setters are:

- `0x22a`: current scale, three integer percentages divided by 100;
- `0x22b`: current axis-angle rotation (present in the native dispatch table, zero Himegari corpus calls);
- `0x22c`: current translation in pixels;
- `0x22d`: delayed/duration scale target, using the embedded object's ordinary one-shot scale channel;
- `0x22e`: delayed/duration axis-angle target (native-dispatch-only, zero Himegari corpus calls).

FIELD's camera helper selects handles `[1,50000)`, anchors the transform at the current camera world
coordinate `(G[0x767e],G[0x767f])`, sets translation to `(400-camera_x,300-camera_y,0)`, and applies the
zoom percentage from `G[0xccc09]`. Thus the map layer is centered/scaled while handles `>=50000`—the dungeon
UI—remain screen-fixed. FIELD's sole `0x22d` call animates a zoom over 300 ms. LOOK reuses the immediate
camera helper. Across the corpus, `0x229` occurs 693 times in 309 scripts: 590 all-zero disables, 101
identity-range selections, and the two FIELD/LOOK camera selections. Correcting the contract therefore
removes spurious object-zero mutations without changing established ADV output.

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
  are delay/duration at `obj+0x38/+0x4c`; sampling uses retained-gfx frame clock `owner+0xb550`
  (`EngineCtx+0x51b64`), not op `0x238`.
- **`0x203` (`gfx_op_0x203_worker_set_color` `0x47e9b0`)**: sets a **static** color/alpha `obj+0x60`, no anim
  bit. Immediate per-object modulation.
- **Blit** (`gfx_object_blit_d3d9` `0x4774c0`): selects a **blend mode** from `obj+0x30`, the value written
  by op `0x203`, and passes a modulation color/alpha to the device draw. Correcting the D3D9 constants:
  mode 1 writes `SRCBLEND=SRCALPHA` (5) and `DESTBLEND=ONE` (2), so it is additive glow—not ordinary
  `SRCALPHA/INVSRCALPHA`. Mode 2 conditionally writes `ONE/ZERO` for a selected render target; mode 3 adds
  the subtract blend operation to the mode-1 factors. The port implements mode-1 additive composition,
  where packed alpha scales source contribution and packed RGB multiplicatively modulates it. For a
  **textured mode-0** object, preserved `0xffffffff` is opaque
  identity, not a request to replace the texture with white; the packed alpha byte is therefore not a
  generic tint-strength control. Mode 3 remains separately scoped beyond the completed mode-1 path, and
  surfaceless mode-0 fills remain a distinct consumer case.

Created/render-target surfaces are a third mode-0 consumer, distinct from both loaded textures and an object
with no surface. SYSTEM4 creates and fills surface 3; BUNKI draws it with alpha `0xd0`, while FIELD draws it
beneath the minimap with alpha `0x40`. Original screenshots show the former as a translucent tinted panel and
the latter allowing the sidebar paper through. Therefore created surfaces consume packed alpha as object
opacity and packed RGB as multiplicative modulation. The port retains created-surface identity separately
even though these surfaces have resource id zero; their no-colorkey sentinel is `-1`, not active key-black
zero. Loaded mode-0 textures keep the established opaque/alpha-inert behavior above.

**TITLE SO022 additive proof (2026-07-20).** TITLE loads type-1 8-bpp `SO022.AGF` with no alpha plane and
no color key, binds two 140×140 spritesheet objects, and calls `0x203(handle,1,255,0xffffff)` for both.
Ordinary alpha composition therefore produces opaque black squares around the blue flames. Native mode-1
`SRCALPHA/ONE` makes black contribute zero; the implemented additive raster path removes the rectangles
while preserving the animated glow, verified across a windowed SYSTEM4/TITLE capture.

**SC0000 third-CG white-screen and missing-glow fix (2026-07-11).** The page containing
`大役を担ったのは…` reaches the intended EV052DA image and both animated AE001D layers. A synchronized
Godot capture proved AE001D is correctly alpha-bearing and drawn in mode 1 at only a 6-8% additive source scale;
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
**global frame clock `retained-gfx owner+0xb550` (`EngineCtx+0x51b64`)** (sampled by the active outer engine tick, NOT the
op-`0x238` clock `ctx+0x51b7c`), and each
**ping-pongs** (triangle wave, folded at period/2) — i.e. these are *oscillating/pulsing* effects, not
one-shot fades. The `0x202`/`0x203` color (`obj+0x60` static / `obj+0x64` animated, sets
`owner+0xb558`/`0xb560` = `EngineCtx+0x51b6c`/`0x51b74`) is a **separate channel whose blit consumer was
not located** in this pass. So a
one-shot fade's exact source→target→easing is still unresolved and would take a dedicated dig (find the
`obj+0x64` consumer + the color→`obj+0x240` path + the clock advance). ⇒ **smooth color-anim interpolation
stays deferred**; slice A ships the static end-state (which reaches the correct final alpha/tint and fixes
the stuck-opaque bug), with interpolation as a scoped follow-up.

**Resolution (2026-07-10 — supersedes the deferral above).** The missing consumer was the bit-1 branch in
`gfx_object_apply_transform_channels` (`0x472f00`), before its matrix channels. It seeds shared start
`obj+0x34` from retained-gfx `owner+0xb550` (`EngineCtx+0x51b64`); holds current packed ARGB `obj+0x60`
through delay `+0x38`; then performs an
integer, bytewise linear interpolation to target `+0x64` for duration `+0x4c`. At natural completion—or
when retained-gfx `owner+0xb55c == 1` (`EngineCtx+0x51b70`) requests forced completion—the target commits
to current, delay/duration clear,
target becomes `0xffffffff`, and the one-shot active bit clears when no color/matrix/src-rect sibling remains.
The object-local bit at `+0x2d0` suppresses the global force and keeps that finite group from raising the
blocking-presentation dirty flag; it remains redraw-active and therefore animates asynchronously. Negative
alpha/RGB target operands independently preserve their bytes from current `+0x60`.

The port now carries current and target separately and samples them from the unified `FrameClock`; an op
`0x203` static write after `0x202` therefore becomes the ramp's current value rather than overwriting its
target. Mode 0 retains the established CG/tint/fill behavior; mode 1 uses native additive composition with
ARGB alpha as the source scale plus RGB modulation. `draw-string 0x204`/`0x7a` remains a separate dependency.

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
| `0x22f` | `gfx_op_0x22f_set_position_anim` → `gfx_worker_set_translation` | set object **position** (translation vec `obj+0x5d4`); base transform, not a ping-pong channel. Operand 2 is also retained as channel control state; the port currently ignores that field. |
| `0x229` | `op_0x229_set_gfx_range_transform` → `gfx_range_transform_reset` / `select_handles` / `set_anchor` | reset/select the shared **retained-object range transform**; not a per-object position setter (superseded finding above) |
| `0x239` | `gfx_op_0x239_set_srcrect_cell` → `gfx_worker_set_srcrect_cell` | one-shot **spritesheet-cell** channel: delay/duration `obj+0x48/+0x5c`, total frames/columns `obj+0x238/+0x23c`, target frame `obj+0x234` |
| `0x231` | `gfx_op_0x231_anim_srcrect` → `gfx_worker_anim_srcrect` | looping **spritesheet-cell** channel: milliseconds per frame `obj+0x230`, total frames `obj+0x238`, columns `obj+0x23c`; row-major and wraps, not ping-pong |
| `0x232` | `gfx_op_0x232_anim_color` → `gfx_worker_anim_color` | **animate color**: bit2 active, period `obj+0x220`, target `obj+0x240` → interpolator COLOR channel (ping-pong). Negative alpha/RGB preserve corresponding bytes from static color `obj+0x60`; alpha >255 clamps. Distinct from one-shot `0x202`/static `0x203` |
| `0x228` | `gfx_op_0x228_query_position` → `gfx_object_query_translation_target` (`0x47cdd0`) | **query** the decomposed target-translation matrix (x,y,z), `obj+0x1ac/+0x1b0/+0x1b4`, → operand slots 3/4/5; success is 0 and missing is 1 |
| `0x23f` | `op_0x23f_query_surface_stop_time_ms` (`0x42a520`) | **query loaded-surface DirectShow stop position in integer milliseconds** → operand slot 1, or -1 for an empty movie surface. Implemented with an explicit warning/-1 safety path when host timing metadata is unavailable. |

**Opcode-hardening follow-up (2026-07-28):** `0x233` is the missing cyclic-scale sibling between
`0x232` color and `0x234` rotation. `op_0x233_set_scale_cycle@0x423cf0` accepts
`(handle, period_ms, scale_x_percent, scale_y_percent, scale_z_percent)`, divides the three scale operands
by 100, and calls `gfx_object_set_scale_cycle@0x47efd0`. The worker clears start timestamp `obj+0x210`,
stores period `obj+0x224`, builds the target scale matrix at `obj+0x250`, and raises retained redraw dirty.

`gfx_object_anim_interpolate@0x473ed0` samples
`phase = 2 * min(elapsed % period, period - elapsed % period) / period` and linearly blends the identity
matrix to the target. The first half-cycle therefore grows/shrinks identity to target and the second returns
target to identity. This matrix lives in the separately anchored cyclic-animation product, independent of
the immediate/current and delayed one-shot scale matrices. Himegari has 31 sites in nine scripts: nineteen
in DEBUGADV and twelve across eight ordinary ADV scenes. The port now models this as its own retained
channel, samples the exact triangular phase on the shared frame clock, and composes
`T(-anchor) * one-shot-scale * one-shot-rotation * translation * cyclic-scale * cyclic-rotation *
T(anchor)`. It remains frame-driven while visible and survives native-style object cloning independently
of the immediate and delayed one-shot scale fields.

**Cyclic-channel reset follow-up (2026-07-29):** opcode `0x230` is the shared reset for this looping
animation family. `op_0x230_reset_gfx_cyclic_animations@0x423ba0` takes one retained-object handle and
calls `gfx_object_reset_cyclic_animation_channels@0x47ee60`. The worker gets or creates the object, clears
active flag bit 2, and zeroes five start/period pairs:

| channel field | start | period |
|---|---:|---:|
| looping color | `obj+0x20c` | `obj+0x220` |
| cyclic scale | `obj+0x210` | `obj+0x224` |
| cyclic rotation | `obj+0x214` | `obj+0x228` |
| second cyclic matrix | `obj+0x218` | `obj+0x22c` |
| looping source rectangle | `obj+0x21c` | `obj+0x230` |

It does not change the object's current/base transforms. Five DEBUGADV calls reset an effect object before
demonstrating a new looping asset; FIELD's sole ordinary call resets the temporary moving-unit handle
between its one-shot translation setup and `0x239` source-cell animation. This is a bounded retained-state
operation. The port now exposes the same get-or-create reset on `GfxState`: it disables cyclic color,
scale, rotation, and source-rectangle sampling; clears all ten native timing dwords in the preserved
numbered-save record, including the unmodeled second cyclic matrix; and leaves base/current transforms,
cyclic targets, and finite one-shot channels intact.

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
modulation, while mode 1 enables SRCALPHA/ONE additive composition. AE001H therefore cycles
`0xffffffff ↔ 0xe0ffffff`: identity RGB throughout, with alpha intentionally inert in mode 0, so native has
no visible pulse. The faithful fix is now fully bounded: initialize/resolve static color correctly, sample
packed ARGB, and consume it through the existing mode-specific blend path instead of converting animated
alpha into tint strength. The port now initializes static color to native identity `0xffffffff`, resolves
negative operands in `SetColorAnimResolved`, samples packed ARGB before blend selection, and feeds it through
the existing mode-specific path. Exact AE001H, mode-0 RGB-modulation, and mode-1 additive regressions cover the
contract; the white pulse is removed without suppressing the scripted channel.

**Resolved 2026-07-20:** `0x236` is the movie-to-retained-surface path described below. `0x242` is the
detached finite-animation control described below; `0x23d`, `0x20a`, and `0x20e` are also implemented in
their later lifecycle/presentation slices.

### Movie-to-surface opcode `0x236` (2026-07-11)

The exact ABI is `play-movie-to-surface(resource_id, surface_slot, movie_flags, start_delay_ms)`. Handler
`op_0x236_play_movie_to_surface@0x423ee0` records a 9-dword instruction length and requires the destination texture to exist.
It allocates/reuses a 0x478-byte `CMovieToTexture` object, binds the D3D device/backing texture, opens
operand 1 through `asset_open_indexed_entry`, constructs a stopped DirectShow FilterGraph, and configures its
pending start. The graph
queries `IGraphBuilder`, `IMediaControl`, `IMediaPosition`, `IMediaEvent`, and `IBasicAudio`; its custom
`CMovieTextureRenderer` accepts RGB samples and copies the bottom-up frame into the retained texture.

Operand 3 is retained movie mode plus sound-route policy. Bits `0x10000/0x20000/0x40000/0x80000` force
mute/music/SE/voice routing; without an override, native setting `set:DependMovieSound` supplies the normal
movie route. The port applies those categories through Godot audio buses. Operand 4 is stored at movie object
`+0x42c` as a millisecond start delay: `movie_start_pending_after_sync_delay` records `timeGetTime()` on the
first service tick and starts the graph once `current - start >= operand4`. It is not a device mask or movie
duration. Replacing or releasing the owning surface stops the graph and detaches the renderer. The port still
threads this value through a legacy `syncMask`-named host parameter but does not yet delay FFmpeg start for
nonzero values. The corpus has 25 sites: 24 pass literal zero, while BTL supplies a dynamic local-pointer value.

Graph construction/open is synchronous, but playback and sample delivery are asynchronous. The handler
returns normally and the interpreter advances one instruction: at SC0000 `0x13c8`, the native operand
capture evaluates `(0x33, 0, 2, 0)` and the next executed bytecode is `0x13d1`. The movie therefore does
not itself block the VM. SC0000 prepares additional static layers, then reaches `0x21c` through
`label_1235a@0x1574`. That opcode sets run-state bit `0x400` and yields the interpreter; the presentation
service continues sampling the retained movie until DirectShow EOF, after which the following script
cleanup releases it. The static preparation before `0x21c` is not a movie teardown boundary.

Non-modal start is deferred through `movie_play_configure@0x4625e0`: the outer tick's
`movie_start_pending_after_sync_delay@0x4633b0` records the first service time and calls the same shared
`IMediaControl::Run` worker once operand 4's sync delay has elapsed. A zero mask starts on the first service
tick. It therefore receives the same stopped-to-paused preroll and first-frame poster behavior as modal `0x20f`.

**Manual-test corrections (2026-07-11):** the initial port incorrectly treated pre-yield static loads as
surface replacement, producing start/first-frame/stop all in render frame 0. The bounded host now retains
the movie through `0x21c` until DirectShow completion. A follow-up compositor trace proved the movie object
was present at the correct z-position, but the static AGF cache reused the first decoded sample forever
because every sample has the same `(assetId,colorKey)`. An unconditional current-sample backbuffer copy did
not help because normal retained composition immediately covered it with that cached first sample. Movie
surfaces now bypass the static cache and publish only through the retained object. A windowed run records
first frame 101 and stop frame 190; the user manually confirmed visible playback. The separate lower white
textbox-area object remains outside this finding. Embedded audio was added later through the portable
FFmpeg/audio-clock path described in `docs/platform-portability.md`.

The `/v2` image names/comments the handler; movie ctor/interface/open/play/volume/release workers; sound
route helpers; renderer media-type/sample workers; and stop/detach/destructor lifecycle. The image was
saved after annotation.

### Movie-surface stop-time query `0x23f` (2026-07-21)

The exact ABI is `query-surface-stop-time-ms(out_stop_time_ms, surface_slot)`. Handler
`op_0x23f_query_surface_stop_time_ms@0x42a520` records a 5-dword instruction length, fetches operand 2,
and directly indexes `EngineCtx+0x52bd4[surface_slot]`. A null surface writes `-1` to operand 1. A non-null
surface dereferences the `CMovieToTexture+0x414` interface pointer and calls vtable slot `+0x28` with a
stack `double` output parameter.

That interface is conclusively `IMediaPosition`: graph initialization queries IID
`{56A868B2-0AD4-11CE-B03A-0020AF0BA770}`, and the inherited `IUnknown` + `IDispatch` layout places
`get_Duration` at `+0x1c`, `put_CurrentPosition` at `+0x20`, `get_CurrentPosition` at `+0x24`, and
`get_StopTime` at `+0x28`. Neighboring op `0x23e` calls `+0x24`, while movie op `0x245` calls `+0x20`,
which independently confirms the slot mapping. Thus `0x23f` does **not** call `get_Duration`; it asks for
the configured playback stop position. With an ordinary freshly opened graph that position normally
equals the media duration, explaining why every observed consumer uses it as a lifetime.

Native multiplies the returned `REFTIME` seconds by the double `1000.0` at `0x5713e8`, passes the x87
value to the compiler helper at `0x550850`, and writes the low 32-bit integer to operand 1. The helper's
SSE2 branch uses `CVTTSD2SI`; its x87 fallback corrects the current rounding result to the same behavior,
so conversion is truncation toward zero. The COM `HRESULT` is ignored and the local output is not
preinitialized: the handler assumes any non-null movie surface has a usable `IMediaPosition`. It is a
pure query—no run-state bit, service boundary, seek, or playback mutation occurs.

Himegari has 23 calls in 17 scripts: BTL, DEBUGADV2, FIELD (2), SC0000/10/20/40/50/60/70/80/90,
SC0100 (2), SC0110 (2), SC0120 (2), SC0130 (3), and USEMAGIC. Every site is associated with a preceding
`0x236` load of the queried surface; the repeated ADV form has five table writes between load and query.
FIELD supplies the strongest unit evidence: one path computes `stop_time_ms / 16 + 1` for a 16 ms callback
schedule, and another clamps it to 600 ms before calling DRAWVOL. This corroborates the native decode and
rules out the former port behavior, `retained gfx object exists ? 0 : -1`.

**Port implementation (2026-07-21):** `DirectShowMovieDecoder` now queries
`IMediaPosition::get_StopTime` after the graph reaches its running state and before synchronous `0x236`
initialization returns. It applies native seconds-to-milliseconds truncation and hands the value through
`IHost.PlayMovieToSurface` into a movie-surface record owned by `GfxState`. Decoder construction moved from
the deferred Godot callback to the VM-side synchronous open boundary; the ready decoder is staged in a
thread-safe pending registry and adopted by the main thread before frame sampling, preserving asynchronous
presentation. `0x23f` silently returns -1 for an empty movie slot. Native ignores the HRESULT for a non-null
movie's stop-time query and has no meaningful contract for the port-only case where the installed host
decoder cannot construct a graph for a shipped asset. The port therefore models backend failure as an
explicitly completed movie with stop time 0. This gives BTL an immediate-effect duration rather than feeding
its timeline -1. Started decoders also have a duration-based completion watchdog (minimum five seconds,
reported stop time plus two seconds, maximum five minutes; 30 seconds without timing) so missing EOF cannot
hold the shared `0x21c` wait indefinitely. A normal Game Start through SC0000 was manually validated with
ordinary positive timing.

**Portable replacement closeout (2026-07-25):** the synchronous metadata, asynchronous presentation,
failure-as-completed, and watchdog contracts above remain, but `FfmpegMovieDecoder` is now the sole backend.
After the 213/213 video/audio corpus gate and clean audible LOGO/OP/CHAPTER acceptance, the port deleted
`DirectShowMovieDecoder`, its COM/temp-file adapter, compatibility test, and managed Windows annotations.
Native AGE's DirectShow behavior remains relevant evidence for opcode semantics; it is no longer port code.

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
`engine_settings_register_defaults@0x46be30` registers `set:CancelMesSkipOnClick=0` only as the generic
fallback. Himegari's SYS4INI trailer overrides it with `CANCELMESSKIPONCLICK=2`, enabling the
press/release cancellation path. The port currently preserves the fallback instead because it does not yet
apply the trailer's ADV policy settings; that is now a cataloged parity gap rather than evidence that
Himegari intended click-cancel to remain disabled.

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

**ADV entry/exit lifecycle.** The script comments `savemesskip` and `loadmesskip` describe a temporary
suspension boundary, not a second saved preference. `op_0x19b_suspend_adv_skip_service@0x416560` clears
active `ctx+0x13dc`, run-state bit `0x08000000`, and `ctx+0x55100` (`adv_skip_service_enabled`) while
deliberately preserving `ctx+0x550fc`, the all-message Skip toggle returned by op `0x19a`.
`op_0x19c_resume_adv_skip_service@0x4165a0` sets the lifecycle gate again and reconstructs active
fast-forward from persistent all-message Skip or `adv_read_skip_state`; the separate startup guard at
`ctx+0x6f86c` can suppress that reactivation. SC0000 brackets ADV teardown/setup with this pair, as do
branch transitions and `CALLBACK_LOAD.BIN`.

The port therefore keeps persistent Skip separate from currently active fast-forward. Opcode `0x19b`
deactivates the host service without changing the control-strip toggle, and `0x19c` recomputes the host
service from the persistent toggle plus the host's live read-skip channel. This also makes op `0x1c7`
correctly report inactive during the suspended interval. Focused regressions cover preservation across the
pair and read-skip-only reactivation. Implementing the `message:ReadTextSkip` preference itself remains
deferred with ops `0x1ca`/`0x1cb` and the profile-owned `ReadTextDB`; the lifecycle implementation does not
invent a storage backend.

### Opcode `0x1ad` marks the numbered-save resume-frame boundary (2026-07-20)

Opcode `0x1ad` is a zero-operand persistence marker, not an input reset or modal-UI synchronization call.
Its real dispatch handler is `op_0x1ad_mark_save_resume_frame@0x416b70`. Aside from recording the generic
one-dword instruction length at `ctx+0x53d88 + curCtx*0x78`, it performs one semantic write:
`ctx->save_frame_boundary_index = ctx->cur_ctx_index` (`ctx+0x9928c = ctx+0x53d14`).

`context_state_serialize@0x40d320` consumes the mark for numbered-save layouts 2 and 3. A negative mark
falls back to the current frame; otherwise the serializer copies script frames `0..mark` inclusive and
forces the marked frame's saved return entry to `-1`, making that frame the top/terminal activation after
load. `op_0x2_exit_or_return_frame@0x417940` clears the mark when normal return unwinds below it. Scene/context reset also
initializes it to `-1`. Thus the mark selects both the highest saved activation and the frame at which a
loaded game resumes; the opcode itself neither reads nor writes a file.

Corpus placement agrees with the native dataflow: 1,928 executions appear across 304 scripts. Roots such as
`CAMP`, `FIELD`, and `FORT` mark their main frame near entry; SC0000's six sites are its startup path and the
return paths from `HISTORY`, `MENU`, `HIDEWIN`, and `INPUTNAME`. Those calls re-establish the enclosing ADV
frame as the safe numbered-save resume point after modal/nested scripts finish.

**Port implication:** the port-owned JSON session snapshot still persists only global integer/string banks,
but the VM now tracks `0x1ad` as an identity reference to the active `ExecFrame`. The marker survives nested
calls and clears when that same frame unwinds, exposing the inclusive zero-based cutoff for the native
serializer. The following numbered-payload slice must consume this boundary while serializing the frame
chain; no script seed or offset-specific shortcut is involved.

### Native persistence opcode family and file layouts (resolved 2026-07-24)

The remaining `SAVE.BIN` family is now mapped. It separates three storage domains rather than exposing one
generic save operation:

| Opcode | Native operation |
|---|---|
| `0x19e(status,slot)` | write `SAVE%02d.DAT`; truncate/replace after any incompatible-save prompt |
| `0x19f(status,slot)` | load numbered data only, with runtime-frame and history restoration disabled; unused by Himegari's shipped corpus |
| `0x1a0(status,slot,...metadata)` | validate only the fixed header and return date/time plus accumulated playtime |
| `0x1a1(status,slot)` | full numbered load, including active frames and history, then resume through `CALLBACK_LOAD`/`0xae` |
| `0x1a9(cell)` / `0x1aa(cell)` | store/restore a selected string cell in shared `SAVE.DAT` |
| `0x1ab(status,slot)` | delete the numbered `.DAT` and `.STH` pair |
| `0x1ac(status,src,dst)` | copy the numbered `.DAT` and `.STH` pair, replacing destination files |
| `0x1ad()` | select the highest frame included in a numbered save; no file I/O (documented above) |
| `0x1ae(status,slot,surface)` | write `SAVE%02d.STH` from a surface |
| `0x1af(status,slot,surface)` | load `SAVE%02d.STH` into a surface |

Opcode `0x19d`, adjacent in number and used by CGMODE/ED/HMODE/MMODE, queries the shared-profile catalog
unlock database:

- `op_0x19d_is_catalog_resource_unlocked@0x427810` fetches `(out, packed_resource_id)`. Packed append
  ids return zero when the configured save version is older than 3.10; otherwise it calls
  `asset_catalog_is_resource_unlocked@0x415920` on `EngineCtx+0x9c24c`.
- Base ids use the marker table at asset-catalog `+0x41c`. A nonzero high byte selects the append
  pointer at `+0x3844 + selector*4`; the low 24 bits are the table index.
- The marker is valid exactly when its low word equals
  `low16(index * 0x053d6f99 + 0xb0b0b0b0)`. `asset_catalog_mark_resource_opened@0x44e410`, called only
  after `asset_open_indexed_entry` succeeds, writes the plain marker and its modular-exponent encrypted
  SAVE.DAT counterpart.
- `shared_profile_load` decrypts base and flattened append marker arrays through
  `modular_exponent_u32@0x471e60`, then
  `asset_catalog_restore_resource_unlock_marker@0x404ca0` installs and re-encrypts them under the
  current session key. The two leading table DWORDs are private-exponent XOR `0x87912345` and modulus.

CGMODE passes each CGINIT full-image AGF id; HMODE passes each SPINIT scene-script id. The copied port
profile validates all 851 gallery images and all 118 H-scene scripts. The former all-locked presentation
was therefore an opcode/runtime integration defect, not missing save data. The port now decodes and
queries the native markers, marks successful VFS opens, and writes a native-decodable encrypted table.
The actual numbered/selected-cell persistence cluster remains the adjacent `0x19e` through `0x1af`
family described above.

**Numbered operation status contracts.** Save/load open failure is `1`; metadata uses `0=valid`,
`1=absent/open failure`, and `2=invalid/incompatible`. Delete/copy attempt both members of the pair and use
`0=both succeeded`, `1=DAT failed but STH succeeded`, `2=STH failed` (the STH result takes precedence).
Thumbnail I/O uses `0=success`, `1=open/create failure`, and `2=codec failure`. Full-load opcode `0x1a1`
expects its caller to pre-seed status zero: on success it starts the resume sequence without rewriting that
operand, while a missing file writes one.

#### Save-root resolution

Save paths are native engine policy, not script-provided strings. Every numbered/shared handler first calls
`save_root_resolve@0x40b880`, then formats one of the engine-owned names (`SAVE%2.2d.DAT`,
`SAVE%2.2d.STH`, `SAVE.DAT`, `SAVE.BAK`, `RT.DAT`, and their `$$` temporary names) under that root.
The script operands select the operation and numbered slot only.

The port preserves that ownership boundary while intercepting the enclosing profile root. For Himegari,
`Sys4PersistencePaths` validates that `SAVEPATH` is beneath `REGFILEPATH`, maps their relative `SAVE` tail
under Godot `user://`, and supplies `user://SAVE` to `DirectoryNativeDatStore`. This isolates authored port
saves from the original installation while retaining compatible file structure; another profile root can
be injected without changing script semantics.

The resolver reads two per-game settings from the SYS4INI-backed settings registry:
`set:UseAppDataFolder` and `set:SavePath`. When `UseAppDataFolder == 1`, the modern-Windows branch
loads `SHGetFolderPathA` and requests `CSIDL_LOCAL_APPDATA | CSIDL_FLAG_CREATE` (`0x801c`); it then
appends `SavePath`. Otherwise the configured `SavePath` is used directly. It probes
`<root>\SAVE.DAT`; if that file does not exist, it walks the root and creates missing directory
components before returning it.

Himegari's SYS4INI trailer supplies:

```text
USEAPPDATAFOLDER=1
SAVEPATH=Eushully\姫狩りダンジョンマイスター\SAVE
```

Thus its normal modern-Windows root is
`%LOCALAPPDATA%\Eushully\姫狩りダンジョンマイスター\SAVE`. The complete filenames and backup
lifecycle are fixed by AGE after that root has been selected; no persistence opcode accepts an arbitrary
path or filename.

#### Shared `SAVE.DAT`

`shared_profile_save@0x40c950` writes `$$SAVE.DAT`, replaces `SAVE.DAT`, and keeps `SAVE.BAK`; it performs
the analogous `$$RT.DAT` → `RT.DAT` / `RT.BAK` update for ReadTextDB. `shared_profile_load@0x40ccd0`
falls back from `SAVE.DAT` to `SAVE.BAK` and loads `RT.DAT` independently. Numbered `.DAT` writes do not
use this backup transaction, but a successful numbered serialization flushes the shared profile too.

The complete shared-payload byte structure, installed-file counts, and its separation from `RT.DAT` and
numbered slot state are canonical in `sys4-format-notes.md` under "Native persistence files." The
selected-cell address meanings are canonical in `vm-map/globals.toml`. Native writer/reader behavior
establishes that the fixed integer/string maps are profile-wide services rather than whole-bank snapshots;
their opaque catalog and extended sections are preserved by the port.

`SAVE.BIN` provides the game-level consumer proof for the menu split: it reads timestamp/playtime from
the numbered header, loads the `.STH` screenshot, restores the remaining row fields with selected-cell
ops, and scans the restored cleared-ending mask to draw the NG+ badges. Save and stage-select paths
snapshot the same preview cells before persistence.

#### Common `.DAT` container

`save_container_read_and_validate_header@0x4306f0` is the metadata-only path used by `0x1a0`.
The full writer/reader are `save_container_encode_and_write@0x42fac0` and
`save_container_read_and_decode@0x42ff80`. The canonical header, codec-frame, CRC, LZSS, and reversible
DWORD-transform specification is in `sys4-format-notes.md`. The mapped helpers are
`lzss_4k_compress@0x42ed30`,
`lzss_4k_decompress@0x42f050`, `save_payload_expand_multiply_transform@0x42f400`,
`save_payload_inverse_multiply_transform@0x42f4a0`, `crc32_msb_first@0x42f360`, and
`crc32_reflected@0x42f300`.

#### Numbered logical state and `.STH`

`context_state_serialize@0x40d320` chooses numbered logical layout 1, 2, or 3 from `SaveVersion1`;
layout 1 retains legacy `SaveVersion2` sublayouts 10 and 20. Layouts 2 and 3 serialize script contexts
`0..save_frame_boundary_index` inclusive (falling back to the current context), clear the terminal frame's
return target, and append `text_history_serialize`. The matching
`save_data_deserialize_and_begin_restore@0x40fd10` reconstructs the banks, resources, retained state,
history, and—when requested by `0x1a1`—the active frame chain consumed by `0xae`.

The complete Himegari layout-3 body, frame record, six-bank sequence, retained-gfx region, appended
text-history tail, and `.STH` BMP format are canonical in `sys4-format-notes.md`. Native thumbnail
helpers are `gfx_surface_write_bmp24_to_handle@0x434bf0` and
`gfx_surface_write_bmp24_to_path@0x475420`; the load side uses the renderer image decoder.

**1.0 implementation boundary:** reproduce these native binary domains and lifecycle first: shared
`SAVE.DAT`/`SAVE.BAK`, `RT.DAT`/`RT.BAK`, numbered `.DAT`, and paired BMP `.STH`. Keep the ownership behind
a profile/save service so extended mode can later add JSON inspection/export, namespaced mod state, migrations,
or a friendlier editor without changing compatibility-mode opcode semantics or the native import/export path.

**Port correspondence (2026-07-24):**
`Age.Engine.Persistence.NativeSaveContainerCodec` now reads and writes the common header, Shift-JIS game id,
SYSTEMTIME/playtime/version metadata, both CRC layers, version-2 compression wrapper, and exact reversible
DWORD transform. `Sys4.LzssEncoder` emits the same 4 KiB-ring token dialect already consumed by
`LzssDecoder`, falling back to native verbatim storage when compression does not shrink. The
`INativeDatStore` boundary and `DirectoryNativeDatStore` own shared `$$SAVE.DAT` → `SAVE.DAT` /
`SAVE.BAK` replacement/fallback and direct numbered `SAVE##.DAT` writes.

`SharedProfilePayloadCodec` owns the typed shared logical layout, including native CP932 strings and
lossless opaque-section preservation. `SharedProfile` owns selected integer/string maps and explicit
load/save lifecycle; `GameSession` injects it into every fresh VM. Opcodes `0x1a2`/`0x1a3` and
`0x1a9`/`0x1aa` now implement native upsert and missing-value defaults for direct cells and resolved
global pointers. `RT.DAT` is now implemented as the separate S3RT layer described below. The numbered store
validates its distinct compatibility id, queries metadata without decoding payloads, and performs exact
paired `.DAT`/`.STH` copy/delete status layering. `NumberedThumbnailCodec` reads and writes the native BMP
dialect through host surface capture/replacement, and ops `0x1a0`, `0x1ab`–`0x1af` (including `0x1ad`'s
frame marker) are wired. Godot's shared profile-path resolver injects the store at `user://SAVE`.
`NativeNumberedSaveCodec`, `NativeTextHistoryCodec`, and `NativeGfxPersistenceCodec` own the complete
layout-3 numbered body, global/frame/gfx state, and appended history tail. The VM wires `0x19e`,
data-only `0x19f`, full load
`0x1a1`, and the active branch of `0xae`; the existing `GameSession` JSON snapshot remains a separate
diagnostic/extended-mode surface.

#### Layout-3 restore mechanics and port correspondence (2026-07-24)

The port tracks the native global banks separately at runtime, captures the marked frame chain, serializes
live surfaces and retained objects, and applies the 20-byte surface records' explicit reload flags. Full
load first replaces each serialized mutable bank prefix while preserving initialization-authored cells
beyond its count, restores history/gfx and retained audio, unwinds the obsolete managed call chain, runs
`CALLBACK_LOAD.BIN` when
the mounted script provider resolves it, loads each saved script at its ordinary entry so its frame-local
prologue runs, lets that script reach its own `0xae` rendezvous, recursively reconstructs child frames,
resumes parents after their saved T2 call sites,
and finally resumes the terminal frame at its T1 boundary. Successful `0x19e` also flushes shared
`SAVE.DAT`/`RT.DAT`, matching `context_state_serialize`.

The prefix boundary is observable in the installed file: integer count `0x6241b` stops before the
initialization-authored unit/stage definition tables, and string count `0x315` stops exactly before
`unit_story_display_names`. Native `save_data_deserialize_and_begin_restore@0x40fd10` zeroes only those
counted regions; the port's former whole-dictionary clear erased the definitions needed by CHMENU,
unit-management, and SELSTAGE after load.

The fixed audio fields are also resolved. Payload `+0x008` is
`EngineCtx.current_bgm_track_id` (`ctx+0xa0b84`), maintained by
`bgm_play_track@0x464750` and queried by op `0xc0`; the installed FORT save contains `0x18`, matching
FORT's BGM024 command immediately before its T1 resume boundary. Payload `+0x00c..+0x033` copies
`EngineCtx.sfx_channel_resource_ids[10]` (`ctx+0x144e0`), and
`sfx_reload_saved_channels@0x482640` reopens each positive packed id after deserialization. The port now
tracks both lifecycles through `0xbf`/`0xc2` and `0xb4`/`0xb6`, serializes them, and restores the active
track plus saved SFX resources. The new EngineCtx fields and function annotations are applied to the
saved `/v2` image.

The surface records have a related preservation boundary. Resource id is at `+0x00`, packed color key at
`+0x04`, `+0x08` is a reload flag rather than a general presence bit, `+0x0c` remains unknown, and
`+0x10` marks a blank/mutable created surface. `gfx_surface_record_tables_init@0x472700` zeroes both
1,000-record tables and initializes every resource id to `-1`.
`gfx_surface_create_blank@0x477370` writes resource `-1` and created flag one;
`gfx_surface_load_asset@0x477c40` writes resource/color and clears the created flag. Neither operation
changes the reload flag. The layout-3 serializer copies all 20,000 live record bytes verbatim, rather
than deriving reloadability from the presence of an asset.

The restore loop calls `gfx_surface_load_asset` only when the saved reload flag is one and resource id is
nonnegative. Otherwise the already-initialized surface survives. The optional all-surface release is
separately gated by both `set:CreateObject` and `set:AutoFreeTex`; the registered defaults at
`engine_settings_register_defaults@0x46be30` are one and zero respectively, matching Himegari's active
path. Opcode `0x259` is also a real lifecycle operation:
`op_0x259_script_entry_clear_surface_persistence_flags@0x417660` clears record `+0x08` and `+0x0c`
across both native tables at every script entry.
Opcode `0x258` then declares the exceptions:
`op_0x258_set_surface_persistence_flags@0x4250a0` calls
`gfx_surface_set_persistence_flags@0x4159c0`, which replaces record `+0x08` from flags bit 0 and
record `+0x0c` from bit 1 in both tables. The port models bit 0 because layout-3 restoration consumes
that reload policy; the adjacent bit-1 consumer remains unnamed.
Installed slot 15 records resource `0x3383` with reload flag zero. SYSTEM4 loads that atlas before the
save UI, and BUNKI cuts its reusable choice-box corners, borders, and winged top ornament from it. The
port formerly released all 1,000 host surfaces unconditionally, so post-load BUNKI retained its black
backing but lost the atlas decoration. Restoration now preserves live surfaces and overlays only flagged
reload records by default, while `VmOptions.CreateObject && VmOptions.AutoFreeTextures` reproduces the
native all-release branch. Port-authored saves emit the exact resource/reload/created fields, including
`-1` for unused records, and opcode `0x259` clears the modeled reload policy without releasing textures.
The corrected native functions are renamed/commented in the saved `/v2` image.

#### Port-authored round trips: restored frame boundary and retained-object encoding (2026-07-24)

An immediate `installed SAVE00 -> port SAVE03` round trip isolated two independent writer defects.
The source file contains two frames (`SYSTEM4.BIN -> FORT.BIN`), while the first port rewrite contained
four: the same gameplay pair plus `SAVE.BIN` and its nested helper. Native opcode `0x1ad` selects the
terminal gameplay frame before opening modal helpers. Full restore through `0xae` reinstates that saved
terminal context as the effective boundary. The managed restore cleared its temporary restore state but
did not restore `_saveResumeFrame`, so the next `0x19e` fell back to the currently deepest frame. The port
now marks the terminal restored `ExecFrame` before resuming at T1; a regression loads two frames, saves
from a nested helper, and proves the rewrite still contains only the original two.

The first fresh rewrite (`SAVE04`) then exposed the other half of that managed/native representation gap.
It had two frames, but its restored SYSTEM4 ancestor changed from native `resume=8, call=8` to
`resume=-1, call=-1`. Native `op_0xae_continue_save_load_stack_restore@0x416790` restores the parent
context's real T1/T2/T3 coordinates before activating its child. The managed port instead leaves the
parent physically parked at the synthetic `0xae` rendezvous while `RunFrame` recursively runs FORT.
Recomputing a save record from that synthetic PC cannot find a T1 or T2 table entry. FORT itself loads
and runs, but when stage launch unwinds through SYSTEM4, the missing T2 continuation enters ordinary
Eushully boot.

Each restored `ExecFrame` now shadows its original `NativeSavedScriptFrame` while a restored descendant
is active. An intervening `0x19e` serializes those original T1/T2/T3 indices; T3 also reconstructs the
live local-return stack, and the saved T1 coordinate seeds the frame's read-message state. The shadow is
discarded only when that child genuinely returns and the parent resumes at its restored T2 continuation.
Restored root-reload and process-exit outcomes also propagate through the synthetic recursion. A focused
load -> nested save -> reload regression proves the parent indices survive and the child is not invoked
again through ordinary `CallScript`.

Manual slot 005 acceptance then proved that an immediate base rewrite could load and later launch a
stage. Slot 006, authored immediately after entering that stage, still restored a black dungeon with
visible but noninteractive UI. Its serialized FIELD range, 704 retained objects, map textures, and
native-looking 0.8 transform were intact. A software replay reproduced the black result because FIELD
had changed every restored map object's scale to `(0,0,1)`: global zoom selector `G[0x7682]` was 3, but
the frame-local lookup table that should yield 80 was still zero.

Ghidra resolves the ordering precisely. `script_frame_restore_saved_layout@0x40f2d0` calls
`script_frame_load_resource@0x40e980`; that loader allocates and zeroes locals, loads the script body,
and initializes its PC to the script codebase, not to `0xae`. The restored script therefore executes its
ordinary entry prologue before reaching the rendezvous. FIELD's prologue constructs the inline zoom
table `[40, 50, 64, 80, 100, 124, 156]`; the port's direct jump to `0xae` skipped those copies and let
FIELD apply zero scale after the gfx record restore.

Restored managed frames now likewise begin at script offset zero while retaining their saved T1/T2/T3
coordinates for `0xae`. Synthetic root and child regressions prove both prologues execute. Replaying the
unchanged real slot 006 now selects zoom 80, leaves the map at 0.8 scale, produces a nonblack dungeon
raster, and reaches the gameplay poll. No slot rewrite is required for this correction.

The same comparison exposed native retained-gfx pointer arithmetic. `context_state_serialize@0x40d320`
writes a handle and copies the meaningful `0xb5` DWORD / `0x2d4`-byte record, then advances its DWORD
cursor by `0x2d5`; `save_data_deserialize_and_begin_restore@0x40fd10` mirrors that stride. Early port
output packed entries contiguously. In addition, `gfx_object_init_default@0x472810` seeds six identity
matrices and three `0xffffffff` color defaults before the loader overwrites the complete record; the old
writer began with zero bytes and only filled modeled fields. It also wrote translation vectors over the
first row of the native 4x4 matrices instead of entries 12..14.

`NativeNumberedSaveCodec` now emits the native `0xb54`-byte entry stride and recognizes the old tight
experimental layout for compatibility. `NativeGfxPersistenceCodec` begins new records from the exact
native defaults, writes row-vector translation matrices correctly, and retains the original raw record
behind the semantic object so still-unnamed fields survive native load/save cycles. The `/v2` serializer
and deserializer comments record the corrected stride and overwrite behavior.

### Opcode `0xae` continues numbered-save stack restoration (2026-07-20)

Opcode `0xae` is the load-side rendezvous paired with serialized script-frame state. Its handler,
`op_0xae_continue_save_load_stack_restore@0x416790`, normally returns after recording its one-dword length.
It only becomes effectful while `ctx+0x53d24` (`save_load_stack_restore_active`) is set by
`save_data_deserialize_and_begin_restore@0x40fd10`. That deserializer restores the selected save layout,
loads `CALLBACK_LOAD.BIN` or the saved entry script, and resets the current context so ordinary opcode
`0xae` sites can rebuild the saved stack.

On an active restore, the handler reads `set:SaveVersion1`/`set:SaveVersion2`, selects the matching saved
frame layout, replaces the current frame PC with its saved resume or call target, and advances through the
serialized contexts. `script_frame_restore_saved_layout@0x40f2d0` loads each packed script and expands its
T1/T2/T3 indices back into live PC/call/return offsets. At the saved terminal context the handler clears
the restore flag and reinstates the saved context/return state. The corpus placement supports that control-flow role: 305 calls overwhelmingly follow
coroutine-resume or call boundaries, including SC0000's main-loop resume sequence.

The load order is significant: `script_frame_restore_saved_layout` delegates script creation to
`script_frame_load_resource@0x40e980`, which initializes the new frame PC to the script codebase.
Consequently the script runs its normal frame-local initialization and reaches `0xae` itself. Starting
a restored frame directly at `0xae` is not native behavior; in FIELD it leaves the local zoom table
zeroed and collapses the restored dungeon to a black point.

The port now implements both branches: an ordinary one-instruction no-op outside restoration, and the
T1/T2/T3-driven managed-frame reconstruction described above while a full numbered load is active.

The first interactive installed-save continuation exposed a dispatch-label integration error rather than
a native semantic error. Although `0xae` had this mapped semantic record and a VM case, its opcode-table
label still used upstream placeholder `u00415130`; the VM switches on the label, so the active case was
unreachable. The saved root entered `SYSTEM4.BIN` with restore state intact, but `0xae` fell through and
SYSTEM4 ran its ordinary `LOGO.BIN → OP.BIN → INIT.BIN → TITLE.BIN` boot path. The source label is now
`continue-save-load-stack-restore`. A real installed-save gate executes beyond the rendezvous and proves
`SYSTEM4.BIN → FORT.BIN → CHMENU.BIN` restoration to the gameplay poll. The synthetic two-frame gate
also asserts that its child enters with `FrameCause.SaveRestore`, preventing its ordinary call site from
masking another inactive-branch regression.

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
save calls the shared-profile writer directly. On accepted `WM_CLOSE`, `age_main_window_proc@0x486320`
queries `set:NoSaveDat` and calls the same writer only when that value is zero; forced and confirmed close
share that post-acceptance path. `engine_settings_register_defaults@0x46be30` registers `NoSaveDat=0`.
Thus the switch suppresses only the shutdown write, not the shared flush following a successful numbered
save.

The selected integer-cell portion of shared `SAVE.DAT` is the `0x1a2` store / `0x1a3` restore service
documented above. It is independent of the `RT.DAT` read-message database even though the shared-profile
writer updates both files in one lifecycle.

The `RT.DAT` header is `0x114` bytes: magic `0x54523353` (bytes `S3RT`), a compatibility id, a 256-byte
game id, version pair `1,0`, and script-record count. It is followed by all 12-byte script records containing
`{script_id, message_count, serialized_flags_pointer}` and then, in the same record order, the corresponding
`message_count` DWORD flag arrays. The native writer copies its live heap pointer into the third record word.
The loader ignores that address, allocates a fresh array, overwrites the word, and rebuilds the in-memory
hashtable; a portable structurally compatible writer can therefore emit zero without inventing an address.
The installed 76,752-byte native file validates the formula exactly: 192 records and 18,543 flag DWORDs.
Its SC0000 record (`script_id=0x22`) has 320 messages, exactly matching SC0000's F7/T1 count.

**Port implementation (2026-07-24):** `ReadTextDatabaseCodec` imports and emits the exact S3RT header,
record table, and ordered flag arrays, validates compatibility/game/version identity, accepts native nonzero
pointer residue, and writes zero in that ignored field. `DirectoryNativeDatStore` owns
`$$RT.DAT` → `RT.DAT` / `RT.BAK` replacement; load follows native behavior and reads `RT.DAT` directly
rather than treating `RT.BAK` as a fallback. `SharedProfile.ReadText` owns records and the pending queue
across fresh VMs, and shared-profile Save/Load updates SAVE.DAT and RT.DAT as separate native domains.
Godot now requests a VM stop, releases the blocking host, waits for the VM worker to leave its opcode
boundary, and performs one shared-profile flush before frontend teardown. Repeated exit notifications do
not rotate backups twice; expected I/O failures are reported without crashing teardown; self-test uses
`NoSaveDat`; and the loaded shared header's accumulated-playtime value seeds the new process baseline.
Focused restart coverage proves selected integer/string cells and committed read flags survive a clean
exit without any numbered slot write. `NoSaveDat` suppresses that exit write while a successful `0x19e`
still writes both shared files.

Scripts now retain their packed resource id and decoded F7/T1 table. Opcode `0x71` commits pending tuples
and snapshots its T1 coordinate; `0x6e`/`0x71`/`0x72` refresh read eligibility; wait completion queues
`{script_id,message_index,message_count}`. Opcodes `0x1ca`/`0x1cb` share the profile-lifetime
`message:ReadTextSkip` setting, while `0x1cc` reports the current message state. This implements native
read-message behavior without scene-offset special cases and leaves JSON/export tooling as extended mode.

The `/v2` Ghidra image now names/comments the lookup, queue, commit, mark, file read/write, shared-profile
save/load chain, exact `WM_CLOSE` gate, and `NoSaveDat=0` default; saved/refined through 2026-07-24.

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
- voice op `0xc4` appends a `0x40000000` record containing `{voice_id,0}`; History replay op `0x1bd`
  uses the same writer with `{voice_id,1}` when recording is enabled.
- op `0x1d2` appends a `0x20000000` record with operand 1 as the metadata type and operand 2 as its value.
  It was previously misclassified as a safe statement marker; 17,323 corpus uses make this a foundational
  correction.
- op `0x1bb(0)` writes suppression bit `0x80000000` at `ctx+0x55110`; op `0x1bb(1)` clears it. HISTORY uses
  that pair at entry/exit so its own UI text is not added to the backlog.
- op `0x85` clears both vectors. Its 286 corpus uses are two sites in each of 143 ordinary ADV scripts,
  generally bounding History to the current ADV block rather than an unbounded profile log.

The read side is the previously identified op-`0x1d0..0x1d4` family. Op `0x1d0` returns a logical entry's
layout slot and first record index; op `0x1d1` renders records until the next group boundary; op `0x1d3`
finds typed metadata; op `0x1d4` finds the voice pair. `HISTORY.BIN` dispatches pair variant 1 through op
`0x1bd` and variant 0 through ordinary op `0xc4`.

History is independent of `RT.DAT`, but native full save fidelity does serialize the live backlog.
`text_history_serialize@0x451d00` writes the index and packed records/strings after context/numbered-save
serialization, and `text_history_deserialize@0x456130` restores them on the matching load path. The port's
`NativeTextHistoryCodec` now implements that appended tail directly against the same live
`AdvTextHistory` model; it remains independent of shared profile state and `RT.DAT`.

History's display support consists of the ordinary presentation operations: primary/ruby font sizes
(`0x75/0x197`), font weight (`0x2bd`), colors/effect mode/offset (`0x76/0x77/0x78/0x1a4`), layout origin
(`0x198`), surface rectangle fill (`0x20b`), message-window alpha (`0x131`), and retained-object presentation
(`0x222`). The `0xd3/0xd4/0xd5` callback-sequence family drives the smooth scrollbar interpolation; omitting
it affects motion fidelity, not the backlog data model.

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

The third port slice implements visible History presentation without moving canonical records into Godot.
Op `0x1d1` asks `AdvTextHistory` to select one retained group, skip metadata/voice records, and build a
host-facing render batch using the target layout configured by `0x70/0x71`, positioned by `0x198`, and
seeded by `0x7a`. `HISTORY.BIN` passes zero for flags and both color overrides, so this is its exact ordinary
binding path; the native flag-4 raster-without-binding and flag-8 side-effect modes remain outside the live
game call site. Godot owns only replaceable labels keyed by target layout, applies the retained font/color/
effect snapshot, and clears stale labels whenever `0x71` resets that layout.

The same slice implements the support calls at their natural seams. Op `0x131` reads a host configuration
property (currently defaulting to zero; choosing a profile/config persistence backend remains deferred),
`0x20b` replaces every clipped RGBA pixel in the addressed mutable-surface rectangle and clears modeled text
draws anchored inside it, and `0x222` requests one retained recomposition boundary. This pixel write matters
outside HISTORY: SYSTEM4 fills created surface 3 opaque white, then BUNKI applies mode-0 tint plus `0xd0`
opacity to produce its translucent menu interior. FIELD reuses the surface at `0x40` opacity under its
minimap. Surface text is now a list rather than one draw per slot: HISTORY's five 600x30
source rows therefore retain five independent speaker names, and compositor source-rectangle clipping maps
each one into its bound object. A real-script regression retains SC0000 page one, runs unmodified
`HISTORY.BIN`, observes a non-empty rendered row, and reaches its `(0,60000)` presentation call.

The fourth port slice implements History's basic pointer interaction and clean return. Op `0x12e` treats its
addressable operands as arrays in their actual VM domain (HISTORY's are local integers), scans from
`incoming_index+1`, and tests inclusive intersection. Rectangle fields are `[left,right,top,bottom]`; each
candidate's x/y offset is subtracted from the pointer before comparison with the reference rectangle.
HISTORY's decoded arrays contain fourteen candidates: scrollbar/control regions, bottom-right close region
8, and five 650x130 text rows at indices 9..13. Moving the pointer updates the selected index and executes the
script's existing redraw, while left-button release on region 8 sets the script's exit state and runs its
normal surface/layout/recording cleanup.

The host input bridge now distinguishes an enclosing ADV page wait from a script-owned timed raw-input loop.
Registration through op `0xcc` marks the current frame as the raw-input owner until that frame returns;
Godot routes physical button state and configured callback indices to it without signaling the parked ADV
page. This is frame-scoped callback state, not a HISTORY name/offset special case, and also matches the
existing HIDEWIN scheduler family. A real regression activates x=684 in SC0000, runs unmodified HISTORY,
selects/closes region 8, observes retained text, and returns to the same single page wait.

Manual comparison exposed a separate typed-address bug in the first display build. `HISTORY.BIN` copies its
button x/y tables into local integer cells `0x4` and `0x68`, then op `0x61` takes local pointers to selected
elements for `draw-texture`. A local base operand names that local cell; it is not a local value containing a
global address. The VM now retains the local/global domain in pointer values, so reads and writes through a
local pointer reach the correct bank. This restores the six controls at x=768/y=121..411, the close control
at x=768/y=549, and the hovered-row highlight while preserving RECOVER's global-array pointer behavior.
The Python A0 oracle uses the same typed-address model.

The History text batches already carry the native x origin 65 and cursor x 45. Their Godot labels were first
created with a full-rect anchor preset before being parented, which discarded the intended absolute placement
and caused a Godot parent/layout diagnostic. Dynamically composited ADV labels now use top-left anchors, and
all root Controls apply their presets only after parenting.

A second manual check separated the apparent bottom row from the real History batches: it was the ordinary
ADV dialogue Label leaking above HISTORY's full-screen retained surface. The real batches contained several
non-empty retained groups, but their target layouts had width/height zero and Godot clipped each to one pixel.
`HISTORY.BIN` deliberately only resets and repositions layouts 2..6; `SYSTEM4.BIN@0x7..0x82` defines and
resets all nine shared layouts before scene dispatch. In particular, slots 2..6 are 650x150 at x=65 and
y=0/150/300/450/600. The Phase-A single-scene bootstrap now carries forward that exact nine-layout prefix,
in the same category as its inherited SO000/SO001 state, until full SYSTEM4 replay replaces the bridge.
The multi-message regression proves that at least two non-empty rows retain 650x150 geometry.

While a nested timed raw-input frame owns the screen, Godot now hides the enclosing page's ordinary dialogue
Label and independently animated wait marker. Native rendering gets this layering naturally because both are
part of retained composition; the port must state it explicitly because those two elements are separate
Godot overlays. They reappear with the same parked page after the modal frame returns.

History's target-layout render batches are likewise presentation bindings, not part of the retained backlog.
`HISTORY.BIN@0x1100` re-enables recording with op `0x1bb(1)` after erasing its object range and releasing
surfaces `0xc0/0xc1`; that existing exit boundary now tells the host to discard all bound History batches.
The semantic text/index records remain untouched, so reopening History rebuilds fresh rows while exiting
cannot leave the old labels above the resumed ADV page.

Stored voice replay now follows the native split. Ordinary op `0xc4` and History op `0x1bd` share the
packed-resource resolver, Skip replacement queue, and Auto voice-pending state, but carry variants 0 and 1
respectively through `IHost.PlayVoice`. Both append their pair to the backlog when recording is enabled;
History's surrounding `0x1bb(0)` suppression prevents the replay from recording itself. Native
`voice_play_indexed_asset@0x488330` stores that variant in the channel-12 sound-buffer state before starting
playback. Its precise audible meaning remains unproven, so the Godot host retains it through its queue and
timeline rather than inventing different playback behavior.

A live replay trace exposed a corrected prerequisite on that path. The clicked SC0000 row found voice pair
`{0x24,0}` but returned at `HISTORY.BIN@0x9ee` because its required type-2 metadata lookup failed. Native
`op_0x1d2_append_text_history_metadata@0x41f9c0` fetches operand 2 and then operand 1 before passing them as
`(type,value)` to `text_history_append_typed_metadata@0x455f00`; the helper stores value at record `+0x14`
and type at `+0x18`. Thus SC0000's `0x1d2(2,0x11)` means type 2, character/value `0x11`, not the reverse.
The port now writes that pair in the native order, allowing HISTORY's type-2 gate to reach voice playback.

#### Timed local-callback scheduler and History smooth scrolling (2026-07-19)

Ops `0xd3/0xd4/0xd5` are a generic frame-local timed callback sequence, not a History-specific animation
primitive. The native state is a vector of 16-byte entries at `ctx+0x5f694`, with begin/end/capacity at
`+0x5f698/+0x5f69c/+0x5f6a0`; each entry is `{deadline_ms,aux,primary_pc,catchup_pc}`. The playback cursor is
`ctx+0x5f6a8`, the last appended index is `ctx+0x5f6a4`, and op `0xd5` uses the elapsed timer rooted at
`ctx+0x5f3ac`. These names are canonical in `vm-map/engine-ctx.toml` and applied to the `/v2` `EngineCtx`.

- op `0xd3` clears the vector, resets the cursor to zero, and initializes the last index and optional abort
  PC to `-1`;
- op `0xd4(interval,count,primary,catchup)` appends `count` entries, accumulating each deadline relative to
  the preceding one and storing the two local callback PCs;
- op `0xd5(abort_pc)` retains the active script identity, starts the timer, sorts the entries, and enables
  scheduler run-state bit `0x40` while `cursor < last_index`. The final entry is therefore a non-dispatched
  look-ahead sentinel, not another callback.

`timed_callback_sequence_tick@0x408170` sleeps until an entry is due. If the following entry's deadline is
already behind elapsed time, it dispatches the current entry's catch-up PC; otherwise it dispatches the
primary PC. It validates that the active frame still owns the recorded script, redirects that frame's PC,
and advances the cursor. The optional abort signal at `ctx+0xa0ce8` redirects to the op-`0xd5` fallback PC;
HISTORY passes `0xffffffff`, so that branch is outside its live route.

HISTORY builds deadlines 10, 20, 30, 40, 50, 51, and 52 ms. The first five advance a five-step scrollbar
interpolation; the catch-up form omits the expensive redraw/present call when the engine is late. The 51 ms
entry is a no-op callback and the 52 ms entry is its look-ahead sentinel. The port keeps the sequence on the
script frame, blocks only at these scheduler service boundaries using the host clock, and resumes op `0xd5`
after each local callback returns. Focused tests cover exact relative deadlines and the late catch-up choice.

Smooth-scroll scheduling is now implemented without changing History backlog ownership or choosing a
save/profile backend.

#### Mouse-wheel accumulation and History navigation (2026-07-19)

Opcode `0x10d` is the read half of AGE's generic Win32 mouse-wheel service. The newly recovered
`age_main_window_proc@0x486320` handles `WM_MOUSEWHEEL` (`0x20a`), sign-extends the high word of `wParam`,
and normally adds that value to `ctx+0x1c34`. In special run modes it instead maps the sign through the
configurable `set:WheelKeyUp`/`set:WheelKeyDown` actions, so the accumulator is specifically the ordinary
raw-wheel channel used by scripts such as HISTORY.

`op_0x10d_consume_mouse_wheel_delta@0x428cf0` writes the accumulated signed value to operand 1 and clears
the field immediately. HISTORY polls it twice: once while establishing the callback loop to discard stale
input, then once per timed mouse callback. The script only tests zero and sign, converting a positive or
negative native delta into one call of its existing smooth-scroll path.

The port follows that ownership directly: Godot converts wheel-up/down events to signed 120-unit native
deltas, the VM atomically accumulates them, and `0x10d` atomically reads and clears the total. This state is
runtime input only; it is neither a script seed nor profile/save data. A real SC0000-to-HISTORY regression
opens after eight retained messages and proves wheel-up selects older retained rows without releasing or
re-entering the enclosing ADV wait.

HISTORY is now 74/78 distinct opcodes and 849/854 instructions handled or safe-noop. Its remaining static
gaps are four supporting opcodes across five instructions: text-style setter `0x8b` occurs twice,
`0x1ce`/`0x20a` form a sprite-animation service pair, and `0x1cb` reads the deliberately deferred
Read-message Skip profile setting.

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

Raw ids `0x3318..0x331f` resolve through SYS4INI to eight 32x32 monochrome Windows `.CUR` assets. The
runtime decodes their DIB XOR/AND masks and hotspots to RGBA textures and installs them through Godot's
custom-cursor API. FIELD separately selects raw id `0x32ce` (`CURSOR09.CUR`) while drag-panning; that asset
is an uncompressed 4-bpp, 16-color 32x32 cursor with hotspot `(16,14)`, not an EXE-embedded cursor. The
port decoder accepts both installed paletted formats, using their independent DWORD-aligned XOR/color and
1-bpp AND-mask strides. No replacement cursor art is authored by the port.

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

The 2026-07-27 retained-layout polish closes the non-dialogue half of this contract. `STUDY.BIN` selects
layout 7, changes its origin for each research row, and calls `MAMES.BIN`; the helper saves the current
glyph delay with op `0x7f`, sets zero with op `0x1b5`, emits two `0x6e/0x6f` lines, and restores the saved
delay. Native therefore publishes all six descriptions immediately and retains them until op `0x71`
resets layout 7, even though op `0x1bb(0)` suppresses History-backlog recording for the menu. The port now
keeps that live presentation stream separate from the persisted History backlog, captures layout/style
and execution-stack ownership per run, implements the delay pair and the font-height-plus-leading line
advance, and draws every retained run rather than replacing a singleton dialogue Label. Raw-input menus
retain runs owned by their own script stack while suppressing unrelated enclosing ADV runs, so STUDY's
MAMES descriptions remain visible without leaking the parent page above HISTORY. Consecutive same-line
literal/global-string runs are joined for presentation, preserving the common
`"literal" + dynamic name + "literal"` ADV idiom.
The same generic path covers `ITMES.BIN`, `SKMES.BIN`, and `INFOMES.BIN`; no STUDY-specific coordinates or
strings live in the runtime.

Detached labels must still follow the ordinary ADV page's retained visibility lifecycle. At SC0000
`show-text@0xb16 -> wait@0xb1e`, the first CG transition calls `0x127d1`: its chrome rebuild helper erases
and rebinds handles `0xd2f0..0xd2f6`, applies the configured backing alpha, then arms `d2f0`'s one-shot
color toward transparent. Before the animation wait begins, `label_1226b -> label_12350 -> label_12637`
executes `gfx_object_erase_range(0xd6d8,0x1f4)` and erases speaker-name handle `0xe678`. AGE therefore
removes the old glyphs **immediately as the window begins fading**; it does not fade glyph alpha with
`d2f0`, nor wait for the later op-`0x71` layout reset.

The port now retains SYSTEM4's op-`0x213` handle interval per ADV layout. A full op-`0x1f7` erase covering
that interval clears the corresponding detached Godot live runs at the same bytecode point. Partial object
erases do not discard the collapsed layout. This follows native lifetime without coupling text to the
configured message-window opacity or affecting callback-owned layouts.

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

#### Native font face, weight, antialiasing, and effects (2026-07-28)

The earlier retained-text work established *where* glyphs appear but deliberately left their final raster
appearance to Godot `Label`. Static reconstruction now identifies the complete native style selected by
Himegari and the specific reasons the port's text looks only approximate.

Himegari's decompressed SYS4INI settings trailer supplies `FONT=ＭＳ 明朝`, `DRAWMODE=1`,
`ENABLEANTIFONT=1`, and `ANTIFONTVERSION=3`. `engine_initialize_subsystems_from_settings@0x415390`
passes the face to `adv_text_manager_initialize@0x456800`, stores draw mode 1 at `ctx+0x99290`, applies
the enabled `message:UseAntiFont` default of 1 through `text_set_antialias_enabled@0x4137e0`, and stores
AntiFont version 3 at text-manager `+0x548`. The ordinary primary `LOGFONTA` begins as:

- `lfHeight=-24`, `lfWidth=-12`, `lfWeight=0`;
- no escapement/orientation, italic, underline, or strikeout;
- `DEFAULT_CHARSET`, default output/clip precision, default quality, and default pitch/family;
- `lfFaceName="ＭＳ 明朝"`.

Size opcode `0x75` keeps the negative pixel height and a negative half-width in both primary LOGFONT
states. It rounds ordinary odd requested heights down by one and special-cases 32/33 to 31, although every
Himegari call uses an even size from 8 through 24. Bold opcode `0x2bd` sets `lfWeight` to exactly 700, not
an arbitrary stroke expansion. Face opcode `0x1a5` is also effectful:
`op_0x1a5_set_font_face@0x42bbf0` calls `text_set_primary_font_face@0x42b1d0`, which copies the requested
face and constructs an `@%s` vertical-writing companion before
`text_rebuild_primary_font_resources@0x44f7f0` recreates the `CreateFontIndirectA` handles, metrics, and
glyph buffers. The 207 corpus calls use only `ＭＳ 明朝` (168) and `ＭＳ ゴシック` (39).

With Himegari's DrawMode 1 and AntiFont enabled, `GetGlyphOutlineA` requests
`GGO_GRAY4_BITMAP` with an identity `MAT2`. `text_raster_string_cached@0x45b600` caches the returned
0..16 coverage bitmap and composites it into the locked 16- or 32-bit destination with ordinary
source-over color/alpha interpolation. Layout measurement separately uses `GetTextExtentPoint32A` against
the selected GDI font, so face, explicit `lfWidth`, and weight affect advances and wrapping as well as
pixels.

The shipped effect profiles use only modes 1 and 3:

- mode 1 draws one effect-color copy at `(x+effect_x,y+effect_y)`, then the primary glyph; all 43
  Himegari sites pair it with `(0,0)`;
- mode 3 samples an ellipse around the glyph. `text_compute_outline_ellipse_offset@0x459860` computes
  `cos(angle)*effect_x` / `sin(angle)*effect_y`; the caller steps by
  `360 / (sqrt(effect_x²+effect_y²)*8)`, blits the effect-colored glyph at every rounded offset, then
  draws the primary glyph. All 164 paired sites use `(1,1)`, producing the normal one-pixel outline.

The ordinary ADV preset is therefore 24-pixel `ＭＳ 明朝`, weight 700, white fill, mode-3 `(1,1)` outline
in `0x606060`, with 8 pixels of line leading. The small menu-description preset is 16-pixel
`ＭＳ ゴシック`, weight 0, white fill, mode 1 with zero displacement, and 9 pixels of leading.

The port now carries opcode `0x1a5` through `AdvTextStyle` into live, retained, History, and surface-text
presentation. `godot/Main.cs` recognizes Himegari's two requested families and loads
`C:/Windows/Fonts/msmincho.ttc` or `msgothic.ttc` when present. Missing or unknown requested faces fall
back to the existing best-effort Japanese font probe, and regular/bold variants are cached per resolved
face so a runtime face switch cannot reuse the preceding family's synthetic-bold font. This restores the
important Mincho/Gothic distinction on a normal Windows installation without treating the proprietary
fonts as game assets.

Godot presentation now distinguishes the two shipped effect profiles. Mode 1 uses the Label shadow
layer as one effect-color glyph at the configured `(x,y)` displacement behind the primary glyph; this
also preserves Himegari's zero-offset double-composite profile instead of inventing an outline. Mode 3
uses a symmetric outline whose size is the maximum absolute configured radius. That is a good
backend-native approximation for every shipped mode-3 call because Himegari always requests equal
`(1,1)` radii. The same style applicator covers live ADV, retained History, and surface text. Mode 2
remains unimplemented and is unused by this game.

A 2026-07-28 Windows calibration compared exact-profile GDI
`CreateFontIndirectW`/`GetTextExtentPoint32W`/`GetGlyphOutlineW(GGO_GRAY4_BITMAP)` results with Godot's
loaded TTCs. Six mixed kanji, kana, punctuation, full-width digit, and Latin samples found exact
horizontal agreement for regular 24px Mincho and regular 16px Gothic. GDI also produced identical
measurements with the native negative half-height `lfWidth` and `lfWidth=0`; no horizontal scaling is
needed for those profiles on this installation.

The first numeric pass selected `VariationEmbolden=0.53` plus one pixel of glyph spacing because its raw
Godot atlas sum was close to GDI's raw 4-bit coverage sum and it bounded the six complete-string errors
to 0..3 pixels. A subsequent direct native/port SC0000 screenshot comparison proved that cross-API sum
was not a visually comparable quantity. For `――かつて、戦いがあった。`, both screenshots have the
same viewport-relative top (`y=478`) and bright horizontal bounds (`x=101..410`, 310px), confirming
placement and effective advance. Native nevertheless has 1,656 bright neutral pixels versus the port's
1,145 (+45%), 1,296 near-white pixels versus 713 (+82%), a 23px rather than 22px bright box, and roughly
twice the gray edge population. The port is materially underweight and softer despite requesting the
correct Mincho face.

The discrepancy is not an unknown value to tune. The native and port pipelines are behaviorally
different:

1. `adv_text_manager_construct@0x456570` creates an information DC with
   `CreateICA("DISPLAY",NULL,NULL,NULL)`. AGE selects the `CreateFontIndirectA` result for its complete
   `LOGFONTA` into this display IC. For SC0000 that state is height `-24`, width `-12`, weight `700`,
   `DEFAULT_CHARSET`, default precision/quality/pitch, and `ＭＳ 明朝`. Windows GDI therefore owns font
   mapping, grid fitting, and weight-700 synthesis.
2. `text_raster_string_cached@0x45b600` calls `GetGlyphOutlineA` with identity `MAT2` and
   `GGO_GRAY4_BITMAP`. It retains GDI's `GLYPHMETRICS`, uses `gmCellIncX` as the per-glyph advance, and
   consumes the returned 17-level coverage mask (`0..16`) directly.
3. `text_blit_glyph_bitmap@0x458c80` converts coverage `c` to integer alpha `c*255/16`. On a 32-bit
   surface it takes `max(destination alpha, glyph alpha)` and interpolates each RGB channel as
   `(dst*(255-alpha)+src*alpha)/255`.
4. Mode 3 does not invoke a font-renderer outline. Its step is
   `360/(sqrt(effect_x²+effect_y²)*8)`, with nearest-integer ellipse coordinates. Radius `(1,1)` makes
   twelve calls covering the eight neighboring offsets: each cardinal offset is visited twice and each
   diagonal once, followed by one primary-color glyph blit at the unshifted position.

The port does none of those raster operations. It loads the same TTC into Godot/FreeType with grayscale
antialiasing, light hinting, automatic subpixel positioning, embedded bitmaps disabled, and otherwise
default `FontFile` policy. Bold is the unrelated `FontVariation.VariationEmbolden=0.53` operation plus
one pixel of artificial glyph spacing; mode 3 is Godot `Label`'s `outline_size=1`. Thus the family,
nominal size, placement, and total width can agree while stems, serifs, white-core coverage, and edge
pixels differ. The screenshot is useful evidence that the difference is visible, but it is not the
behavioral source.

The fidelity correction is consequently a decoded glyph-mask backend, not screenshot-driven embolden
calibration. A Windows reference implementation can call the same GDI APIs with the decoded `LOGFONTA`
and reproduce AGE's integer compositor. A portable backend must expose the same mask/metrics/compositing
contract while explicitly defining its font-substitution and rasterizer policy; FreeType output should
be treated as that backend's result, not claimed to be GDI-equivalent. Godot's one-pixel line-box
compensation remains independently supported by the matching viewport placement. Portable configurable
face substitutions/defaults remain deferred to the broader runtime configuration design. Implementation
is explicitly backlogged until gameplay settles; the scoped architecture and acceptance gates live in
`docs/remake-architecture-and-roadmap.md` under “AGE-exact glyph-mask text renderer.”

#### ADV wait indicator -- ops `0x73` / `0x72` (2026-07-11)

The small bat marker is a configured ADV-layout sprite, not a glyph or part of SO001. `SYSTEM4.BIN`
loads universal raw asset `0x337c` (`SO000.AGF`) into surface slot 12. SO000 decodes to a 360x27 strip of
twelve 30x27 bat frames. It then executes op `0x73` with
`(layout=1, x=385, y=140, surface=12, src=0,0, cell=30x27, terminalFrame=12, period=48ms)`.
The terminal value is exclusive/a frame count: native publishes frames `0..11` and wraps before frame
`12`.
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
Godot atlas overlay advances on the 48 ms host clock only while `IsWaiting`, then hides immediately when
input releases the wait. A 2026-07-27 polish fix removed the port's erroneous `TerminalFrame + 1`
interpretation, which had selected nonexistent frame `12` once per cycle and made Godot render one empty
atlas cell. Frame selection now treats the terminal value as the exclusive frame count and loops `0..11`.
Keeping this 30x27 overlay outside the software backbuffer preserves the static-wait compositor optimization.
Full SYSTEM4 replay remains Phase-B work; the shortcut carries the same state in the meantime.

### SC0000 native SFX / BGM-fade family — `0xb4`/`0xb5`/`0xb6`/`0xba`/`0xc2`/`0xd9` (2026-07-11)

The four SFX opcodes are a retained channel lifecycle, not immediate fire-and-forget calls. Handler
resolution and the saved `/v2` names are:

- `0xb4` `op_0xb4_sfx_load@0x4201d0` -> `sfx_channel_load@0x482500`: `(packed_raw_resource_id, channel)`
  opens the universal SYS4INI entry directly when the high byte is zero, or the selected AAI append entry
  when it is nonzero, then replaces the channel decoder/buffer without starting it. The manager supports
  13 slots (`0..12`); SC0000 deliberately resets and uses the `0..9` subset.
- `0xb5` `op_0xb5_sfx_start_once@0x420210` -> `sfx_channel_start@0x4825d0`: starts the loaded channel with
  logical loop mode 0.
- `0xb6` `op_0xb6_sfx_release@0x420250` -> `sfx_channel_release@0x482600` ->
  `sound_buffer_destroy@0x4831a0`: stop/release and clear the retained resource/decoder; empty release is
  safe. SC0000's `0x62b..0x646` and `0x120d..0x1228` are ten-channel reset sweeps.
- `0xba` `op_0xba_sfx_start_loop@0x420290` -> the same `sfx_channel_start@0x4825d0`: starts the loaded
  channel with logical loop mode 1. `sound_stream_fill_quarter` rewinds the decoder at EOF until a later
  load or release replaces the channel. It is not an alias of the one-shot `0xb5`.

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
raw catalog id `0x28` to `E0808.WAV`, loads channel 0, and `0xb5@0xc2e` starts it in the same millisecond. The next
`0xb4@0xc31` preloads the same WAV into engine-owned secondary channel 4 for a later service start. The
scratch global `G[0x6242d]` is maintained outside script-visible writes; the SC0000 port profile exposes it
as an external value of 4 rather than pretending the script assigned it.

**TITLE SFX packed-raw correction (2026-07-20).** TITLE preloads raw id `0x2aea` (`SE020.WAV`) into channel 2
and executes `0xb5(2)` whenever mouse/joy selection changes. Each activation path loads raw id `0x3321`
(`SE015.WAV`) into channel 1 and immediately starts it; GAMESTART additionally uses raw `0x2aeb`
(`SE013.WAV`). A synchronized Godot trace captured repeated hover starts and the Game Start activation, but
each preceding load recorded `file:null`: `GodotAdvHost.LoadSoundEffect` applied the active script's
scene-local manifest, and TITLE has only 14 local entries. `ResourceMap.ResolveSoundEffect` now uses the
native packed lookup and filters the selected record to OGG/WAV before the existing byte/player path.
The corrected synchronized trace resolves and starts `SE020`, `SE015`, and `SE013` through
TITLE→GAMESTART→TITLE, and the user confirmed hover, activation, and cancel sounds are audible. BGM remains
independent through its direct-name resolver; no callback, mixer, or channel workaround was needed.

That later service start is opcode `0x2bf`, now identified as the facade's `SetDelay` operation.
`op_0x2bf_schedule_sfx_start@0x425240` passes `(channel,start_mode,delay_ms)` to
`sfx_set_delay@0x482720` on the inline sound facade at `ctx+0x14024`. The worker accepts channels `0..9`
and stores five pieces of transient state: active at facade `+0x418`, elapsed/progress at `+0x440`, delay
at `+0x468`, start mode at `+0x490`, and an aggregate-active marker at `+0x4b8`. Its native error text is
literally `Function: SetDelay ... invalid Sound number`. The existing trace supplies the consumer proof:
after SC0000 executes `(channel 4, mode 0, 100 ms)`, the ordinary `sfx_channel_start` worker enters for
channel 4 about 109 ms later while the interpreter is parked at its `0x21c` presentation boundary. There
is no intervening `0xb5`. The port schedules the same already-loaded channel and invalidates the pending
callback if that channel is loaded again or released before its deadline.

Normal-speed windowed validation reached `wait-for-input@0x1a58` after 45.6 seconds without an audio stall;
the user confirmed the opening effects were audible and sounded good.

FIELD deliberately reasserts `play-bgm(stage_bgm_id)` from its common post-action/event cleanup at
`0x884a`; ordinary unit movement reaches that cleanup once per acting unit. The BGM facade already retains
the selected direct-name track id for op `0xc0` and numbered-save state. An identical `0xbf` request must
therefore preserve the current stream and playback position rather than reopen the OGG. The former host path
decoded and assigned a fresh Godot stream on every request, resetting the stage music after every player
move and repeatedly throughout enemy turns. The VM now treats a current-track reassertion as idempotent.
A different id still starts a new track, and `0xc2` target zero clears the retained id so a later request
can start that track normally.

`0xc2` is BGM rather than SFX: `op_0xc2_bgm_fade@0x4204c0` sets run-state `0x200`, arms the service timer,
and calls `bgm_fade_arm@0x464830`. `bgm_fade_tick@0x464960` linearly interpolates current to target percent;
durations at least 1000 ms take 100 steps, shorter durations take 10, and target zero releases the source.
The VM is parked for the requested duration while the native main loop continues rendering. The Godot host
mirrors that ownership split: its VM worker waits on the fade clock only after publishing the completed
pre-fade graphics burst and releasing the script presentation barrier. This is required at
`SC0000@0x7c1`, where the three-second title-BGM fade begins after the New Game scene has already cleared
to black; retaining presentation ownership during the wait incorrectly froze the last menu frame until the
opening CG transition began. `0xd9` is adjacent startup control, not audio data: it clears
run/service bit `0x1000` in the primary and, when active, secondary context and has no VM-visible result.

Opcode `0x1cf` belongs to the voice/BGM envelope rather than SFX. Handler
`op_0x1cf_set_voice_bgm_duck_control@0x4209f0` replaces the transient mask at
`EngineCtx.voice_bgm_duck_control_flags` (`ctx+0x6dbf0`). Before ordinary or History voice playback,
`voice_bgm_duck_begin@0x406de0` checks that BGM is active, `MusicFadeOnVoicePlaying` is enabled, no explicit
`0xc2` fade is active, and mask bit 0 is clear. It then saves the current BGM volume at
`ctx+0x6dbe8` and applies `MusicFadeOnVoicePlayingVol`. The native settings registry supplies defaults of
enabled=`1` and target=`50` percent; SC0000 writes only control masks `0` and `1`.

The port therefore retains `0x1cf` as script-owned runtime state, applies the native registered default
attenuation when an unsuppressed voice begins, and restores the saved BGM level when that voice completes
or is stopped by message Skip. Godot also gives an explicit `0xc2` fade exclusive ownership of the BGM
envelope: voice start does not duck during that fade, a new fade cancels its predecessor, and starting a
replacement BGM cancels any still-live prior fade before restoring normal gain. The last rule closes a
main-thread scheduling race at the TITLE-to-SC0000 boundary. The VM's blocking fade deadline begins before
the deferred Godot tween is created, so `BGM005` could start during the tween's final frame and then be
overwritten to `-80 dB`; a later voice temporarily wrote the audible 50-percent duck level and restored
the erroneous silent level afterward. This introduces no profile record, boot seed, or persistence backend.
A future unified settings backend can replace the registered defaults without changing the opcode/host seam.

**Loop-start closeout (2026-07-28).** A complete Himegari-corpus scan found `0xba` at 334 sites in 100
scripts, and every site immediately follows `0xb4` with the same literal channel 9 for persistent UI/ambient
sound. The port now forwards the native start mode through its host seam and sets
Godot's WAV loop mode before playback; `0xb5` explicitly supplies mode 0 and `0xba` supplies mode 1. The
focused VM regression covers both modes, and the renamed/commented `/v2` handler is saved.

**CONFIG mixer ABI implemented (mapped 2026-07-28; landed 2026-07-29).** Four opcodes form one
internally dependent settings surface:

- `0xc5 get-audio-volume(category, out)` reads `sound:Volume0..Volume4`; selectors `0..4` mean
  master/music/SFX/voice/movie and values are basis points (`0..10000`).
- `0xc6 set-audio-volume(category, basis_points)` writes the same registry cell and immediately reapplies
  the live master/music/SFX/voice/movie volume. Master changes propagate through active movies and sound
  buffers; category changes reach active BGM, ten SFX channels, voice channel 12, or the movie basis.
- `0x1ba set-audio-route-enabled(category, enabled)` accepts selectors `1..4` for music/SFX/voice/movie,
  updates the live route, stops active playback when the route-specific worker requires it, and persists
  `sound:Music/SE/Voice/Movie`.
- `0xc7 get-audio-route-enabled(category, out)` queries those four settings and writes boolean `0/1`.

CONFIG contains all 37 sites: nine volume reads, twelve volume writes, fifteen route writes, and one
route-query loop. Skipping `0xc5` leaves its output operand stale, so the visual slider calculation can be
wrong even before the user changes anything; skipping the setters makes its controls inert.

The native default registry initializes `sound:Volume0..Volume4` to `-1`. During audio startup, each
nonnegative entry is applied and `-1` deliberately leaves the hardware-default gain unchanged. Himegari's
CONFIG reset action later writes `10000/6500/5000/8000` for master/music/SFX/voice. Route defaults are
enabled after `NOSETMUSIC=3` transforms music to enabled state 2 and the generic SE/Voice/Movie flags begin
at one. The saved `/v2` image now records these defaults plus the exact route side effects:
`sound_set_music_route_enabled@0x407f80` rebuilds the current music route, disabling SFX calls its
active-channel stop helper, disabling voice stops channel 12, and movie enablement changes routing without
stopping the movie.

The port now owns one `AudioMixerSettings` instance across fresh scene VMs. `0xc5`/`0xc7` query it and
`0xc6`/`0x1ba` update it before calling the host's live-application seam. Godot maps selectors 0..4 to its
Master/Music/SFX/Voice/Movie buses; the parent Master bus and category bus multiply naturally, and route
changes mute immediately while preserving the native stop/restart distinctions above.

Persistence uses AGE's CP932 `SYS4REG.INI`, not a port-owned format.
`engine_resolve_sys4reg_ini_path@0x46b150` selects the fixed filename under the SYS4INI
`REGFILEPATH`; Himegari therefore uses
`%LOCALAPPDATA%\Eushully\姫狩りダンジョンマイスター\SYS4REG.INI`.
`engine_settings_load_sys4reg_ini@0x46b6e0` registers and imports the `[display]`, `[sound]`,
`[message]`, and `[system]` projections, including all nine audio keys. `Sys4RegIniStore` reads and
updates only `Music`, `SE`, `Voice`, `Movie`, and `Volume0..4`, preserving all unrelated lines, sections,
key spelling/order, CP932 text, and newline style. Missing keys use native defaults. Music retains AGE's
signed value band: Himegari's enabled state `2` disables to `-1` and re-enables to `2`, rather than being
silently normalized to `1`. Writes use a sibling temporary file followed by replacement.

AGE resolves save and settings locations independently, but Himegari makes `SAVEPATH` the `SAVE`
descendant of `REGFILEPATH`. `Sys4PersistencePaths` models both native resolutions together. Godot's
current profile override replaces the resolved `REGFILEPATH` directory with `user://`, preserving the
relative `SAVE` tail and yielding `user://SYS4REG.INI` plus `user://SAVE`. If a future profile supplies
unrelated paths, a single-root override is rejected instead of guessing; native resolution remains
available as the exact `USEAPPDATAFOLDER` plus independent-path policy.

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

**Universal packed resource addressing confirmed (2026-07-21).** `asset_catalog_parse_base_tables@0x44e7e0`
parses one entry count and copies one flat array of 0x50-byte SYS4INI records in serialized order.
`asset_open_indexed_entry@0x44f390` directly bounds-checks a high-byte-zero operand against that count and
indexes `entries[id]`; a nonzero high byte takes the AAI branch described above. It receives neither
`EngineCtx` nor a script-frame/scene identifier and contains no section-base calculation or fallback.

The callers pass their operands unchanged: `script_frame_load_resource@0x40e980`,
`gfx_op_0x1f9_load_surface@0x422360`, `op_0x249_load_raw_texture_surface@0x424b20`,
`voice_play_indexed_asset@0x488330`, SFX/cursor services, and
`movie_to_texture_open_asset_graph@0x463e20`. For movies, `op_0x236_play_movie_to_surface@0x423ee0`
fetches operand 1 and immediately forwards it to that helper. Thus scene-local and raw-fallback are not
native modes: ordinary resource operands are already universal packed SYS4INI/AAI ids.

**Append execution boundary resolved (2026-07-24).** Append startup is exposed to bytecode through
`op_0x143_run_mounted_append_autoruns@0x4172f0`, not through an explicit
`call-script 0x01000000`. The handler scans the `FileDB+0x3028` mount-pointer table as
`EngineCtx.mounted_aai_catalogs[1..255]` (`ctx+0x9f278` onward). For each non-null selector it enqueues
`selector << 24`: the packed id for that catalog's record zero. It does not search for an AUTORUN
basename. The selector loop batches all ids under `script_launch_dispatch_active`, advances the caller
past the opcode, then dispatches the queue.

`script_launch_queue_enqueue@0x40f820` appends to the embedded FIFO at `ctx+0x6f89c`.
`script_launch_queue_dispatch_next@0x40f6e0` suspends the current frame, records return sentinel `-10`,
and loads positive packed ids into reserved interpreter frame 37. When that script reaches
`op_0x2_exit_or_return_frame@0x417940`, the sentinel path launches the next queued id if present;
otherwise it restores the suspended caller. Mounted record-zero scripts therefore execute serially in
ascending selector order.

The sole corpus use is `INIT2@0x17f`. SYSTEM4 has already performed its UI, configuration, input-map,
and preload setup before calling INIT2. INIT2 then calls its 23 base data initializers—EBINIT through
SCINIT plus BTANINIT2—performs its base global/array registrations, executes op `0x143`, and calls
TUNE only after the queued append scripts return. Installed selector 1 record zero is
`$1$AUTORUN.BIN` (`0x01000000`), so the exact shipped order is base definitions, append deltas and
registrations, then TUNE and the remainder of SYSTEM4 boot. Catalog mounting itself still occurs earlier,
when the native SYS4INI/FileDB loader calls `asset_mount_append_catalogs`; mount only establishes
addressability, while op `0x143` is the explicit patch-application boundary.

The port implements the same observable boundary without reproducing the reserved native frame or sentinel.
`IScriptProvider` exposes mounted selector identity; op `0x143` snapshots, deduplicates, and sorts it,
constructs `selector << 24`, and executes each resolved record-zero script synchronously through the normal
nested-frame path. That preserves batching, serial order, caller suspension, global-bank sharing, and
whole-stack halt/reload propagation. Focused VM tests cover two selectors and failure resolution, while the
natural SYSTEM4 integration path proves `BTANINIT2 -> $1$AUTORUN -> $1$EBINIT -> TUNE`.

SC0010 supplies a clean corpus proof outside SC0000's base-zero coincidence. Its `set-texture 0x21` must
open raw entry `0x21` (`SO013A.AGF`); adding SC0010's catalog position `0x11e` instead selects unrelated
`COL0023.OGG`. Its `play-voice 0x120/0x121/0x122` operands directly select
`LILA1414/LILB0053/LILC0054.OGG`, whose SC0010-local file numbers are 2/3/4. The catalog grouping relation
is therefore `absolute id = group start + file_number`; the compiler has already performed that addition.
The port's former scene-first compatibility resolver inverted this relationship. On 2026-07-21 it was
replaced by typed `ResolvePacked` lookup for texture, voice, and both movie consumers; the VM now preserves
the operand unchanged and Godot cache/movie identities retain the complete packed selector. SC0010 low-id
and installed append-pack regressions cover the two failure modes. Ghidra `/v2` renames the catalog parser
and entry-name helper, corrects the opener/caller comments, and is saved.

---

### ADV text line spacing -- opcode `0x8b` (2026-07-19)

`op_0x8b_set_text_line_spacing@0x41f270` stores its single operand at ADV text-manager offset
`+0x560`, or `EngineCtx.text_line_spacing` (`ctx+0x14ea0`). This is pixel leading, not a font face,
weight, or effect selector. `adv_text_manager_initialize@0x456800` gives it a native default of 6.

The horizontal line-break path (`adv_text_append_line_break_horizontal@0x456fd0`) and retained History
renderer (`text_history_render_records@0x452970`) pass `manager+0x560 - manager+0x4cc` as the line
advance. Offset `+0x4cc` is the primary `LOGFONT.lfHeight`, which AGE stores as a negative pixel height,
so the effective pitch is `font pixel height + text_line_spacing`. The corresponding vertical-writing
path uses the same pitch to move to the next column. Corpus values support that interpretation: scripts
pair 8 pixels with 22/24-pixel Mincho text and 9 pixels with 16-pixel Gothic text.

History records do not snapshot the `+0x560` field. `text_history_append_text_record@0x456000` retains
font, color, effect, geometry, flags, and string state, while `text_history_render_records` reads the
manager's current line spacing when it encounters a retained line-break marker. The port therefore keeps
line spacing in the current ADV style manager and overrides a History render batch with that current value;
the individual record continues to supply its retained font/color/effect fields.

The `/v2` image names/comments the handler, manager initializer, horizontal/vertical line-break paths, and
their low-level cursor-advance helpers. The regenerated 58-field `EngineCtx` is applied and the program is
saved.

---

### ADV text publication and wait-indicator service -- opcodes `0x1ce` / `0x20a` (2026-07-20)

The earlier working label "sprite-animation service" was too broad. These operations belong specifically
to ADV text publication and the animated input-wait indicator (the bouncing marker shown after a page
finishes typing). They do not expose a generic retained-object animation channel.

`op_0x1ce_set_adv_wait_indicator_enabled@0x41f8c0` stores its operand at
`EngineCtx.adv_wait_indicator_enabled_value` (`ctx+0x5f734`). A nonzero value sets run-state bit
`0x40000000`, resets `adv_wait_indicator_frame` (`ctx+0x5f72c`) to zero, and starts
`adv_wait_indicator_timer` (`ctx+0x5f64c`). Zero erases the indicator belonging to
`adv_wait_indicator_layout_slot` (`ctx+0x6da84`) with the worker's frame `-2` mode and clears the run bit.
Every Himegari corpus use is `0x1ce(0)`, normally at entry to a modal script; `HISTORY.BIN@0x3` is the
concrete route relevant here.

`op_0x20a_publish_adv_text_layout@0x422ce0` first calls
`adv_text_publish_layout@0x450c80`. Slot zero selects the text manager's current layout; a nonzero operand
selects that indexed layout. The worker erases the layout's old retained-object range, walks its 20-byte
text records, and rebinds their draw objects. When run-state bit `0x40000000` is active, the opcode also
calls `adv_text_publish_wait_indicator_frame@0x453120` with the current indicator frame. The latter worker
uses frame `-1` to return/capture the terminal frame, `-2` to erase the indicator object, and a
nonnegative frame to choose and bind the corresponding atlas cell. All corpus uses are `0x20a(1)`;
`HISTORY.BIN@0x13ab` reaches it through the shared ADV redraw callback.

Opcode `0x72` owns the normal implicit start of the same service: it records the waiting layout, captures
the indicator's terminal frame, resets the active frame to zero, starts the timer, and raises run-state bit
`0x40000000`. Completing the input wait erases the object and clears the bit. In the original interpreter,
return from a nested modal script flows through that shared redraw/wait path and re-arms the parent marker.
The port's host blocks inside the enclosing wait while servicing the nested HISTORY callback, so it restores
the previously enabled parent marker at the callback-return boundary. This is lifecycle equivalence, not a
script-specific seed or global-state workaround.

The port exposes the two explicit operations as host services: `0x1ce` controls marker eligibility and
`0x20a` requests retained ADV publication. Godot's existing indicator clock/configuration remains the
animation implementation. The real SC0000-to-HISTORY regression proves HISTORY disables the marker on
entry, executes layout-slot-1 publication on return, clears transient History rows, and leaves the enclosing
ADV page wait in place. The expanded 63-field `EngineCtx` and semantic function annotations are applied to
the saved `/v2` image.

---

### Retained graphics lifecycle and render targets -- opcodes `0x1f6` / `0x23d` / `0x20d` / `0x20e` (2026-07-20)

The initially suspected `0x1f6` / `0x20e` pair is actually two independent lifecycle pairs. Opcode
`0x1f6` clears retained object records, while `0x23d` releases transient surface resources. Separately,
`0x20d` selects a Direct3D render target and `0x20e` clears the selected target to black. Their frequent
adjacency comes from scene setup and teardown, not from one shared service.

`op_0x1f6_clear_retained_gfx_objects@0x417430` passes the embedded retained-gfx manager at
`EngineCtx+0x46614` to `retained_gfx_objects_clear@0x47cab0`. The worker destroys the complete
handle-to-object map at manager `+0x408` (`EngineCtx+0x46a1c`), resets its sentinel/count and transient
dirty/force-completion/animation-clock state, but leaves the surface table and queued surface commands
intact. The port consequently clears `GfxState` objects without releasing their source slots, allowing a
later bind to recreate an object from an existing surface.

`op_0x23d_release_transient_surfaces@0x4175c0` loops over slots 42 through 999. For each slot it stops and
releases any movie-to-texture object at `EngineCtx+0x52bd4[slot]`, then calls
`retained_gfx_release_surface@0x474e40`. Slots 0 through 41 are deliberately preserved. The native release
worker can also retain a slot protected by its per-slot ownership guard; the port has no corresponding
external owner and releases the complete transient range. In the corpus, 146 of 149 `0x23d` calls directly
follow `0x1f6`, forming the full object-plus-resource reset.

`op_0x20d_select_render_target@0x422e10` forwards its operand to
`retained_gfx_select_render_target@0x479660`. Values below 1000 resolve a retained texture wrapper, acquire
texture level zero, and call `IDirect3DDevice9::SetRenderTarget(0, surface)`. Values at or above 1000 acquire
backbuffer zero instead and record current target `-1` at manager `+0xb530` (`EngineCtx+0x51b44`). Opcode
`0x20e` then calls `d3d_clear_render_target_black@0x471460`, which invokes `IDirect3DDevice9::Clear` with no
rectangles, `D3DCLEAR_TARGET | D3DCLEAR_ZBUFFER`, color zero, depth 1.0, and stencil zero. The port tracks
the selected target and forwards the clear to the host; the retained compositor reconstructs its main
backbuffer from black at publication boundaries, while offscreen clears discard both modeled pixels and
text draws.

Opcode `0x222` publishes its retained handle range into whichever target `0x20d` selected; it is not
merely an onscreen repaint request. SAVE.BIN provides a compact end-to-end example. It creates slot 2 at
800x600, selects and clears it, then publishes handles `[0,0x130b0)` to capture the underlying gameplay
without the save-menu objects. It next creates/selects/clears slot 192 at 112x84, binds slot 2 through
handle `0x15f90`, scales that object to 14%, publishes the two-handle range, and passes slot 192 to
`0x1ae`. Dungeon scripts use the same selected-target publication to construct map surfaces.

The port originally retained only an object-list snapshot for transitions and requested an ordinary
backbuffer repaint from `PresentObjectRange`; the selected surface's pixels remained its cleared black
allocation. That produced completely black port-authored `.STH` files and caused restored dungeon map
surfaces to turn black when their rebuild published offscreen. Godot now uses the platform-neutral
software affine compositor for selected-target `0x222`/`0x20c` publication, including source surfaces,
scaling, transforms, tint/opacity/blend, handle-range filtering, and real pixel clears. Backbuffer
publication remains on the existing renderer. `gfx_present_object_range@0x482230` records the refined
target ownership in the saved `/v2` image.

This trace also corrects an important base-pointer assumption in earlier graphics notes. Offsets `+0x408`
and `+0xb550` are relative to the retained-gfx manager at `EngineCtx+0x46614`, not to `EngineCtx` itself.
Their absolute locations are therefore `EngineCtx+0x46a1c` (object registry) and `EngineCtx+0x51b64`
(frame timer). The same correction moves the dirty/force-completion fields to `EngineCtx+0x51b6c` through
`+0x51b74`. This changes native field provenance and the Ghidra structure, but not the already implemented
host-side object/animation behavior, which was based on worker semantics and runtime traces rather than
directly reading those native addresses.

Corpus totals are 293 `0x1f6` calls in 146 scripts, 149 `0x23d` calls in 146 scripts, 113 `0x20d` calls in
24 scripts, and 345 `0x20e` calls in 167 scripts. In SC0000, the full reset appears as `0x1f6` then `0x23d`
at `0x4ed`; other paths use `0x1f6` then `0x20e` to discard retained objects and clear the already selected
backbuffer. Fresh offscreen surfaces commonly use `0x20d` then `0x20e` before drawing.

---

### Detached finite object animation -- opcodes `0x242` / `0x243` (2026-07-20)

`op_0x242_set_object_animation_detached@0x4249d0` fetches `(handle, flags)` and calls
`gfx_object_set_animation_control@0x47f1a0`, which get-or-creates the retained object and replaces its
32-bit control word at `obj+0x2d0`. Only bit 0 has a located consumer. It does not reset geometry, color,
or matrix state.

`gfx_object_apply_transform_channels@0x472f00` gives bit 0 two connected effects on the finite one-shot
group (packed color, scale, rotation, translation, and timed source rectangle):

- Manager `+0xb55c` value 1 normally forces every finite channel to its endpoint. Bit 0 masks that request
  for this object.
- An unfinished ordinary object raises manager `+0xb560`, keeping blocking presentation active. A bit-0
  object does not raise that flag, but still raises redraw-dirty `+0xb558`, so it continues animating while
  script execution proceeds.

When no finite channel remains, the consumer clears bit 0 and the shared start timestamp. The control is
therefore a per-animation detachment flag, not a persistent object mode. Object initialization sets the
word to zero, and native clone copies it with the rest of the complete object record.

`op_0x243_force_complete_and_reset_anim_clock@0x4182d0` supplies the paired global operation. When service
flags manager `+0xb56c` bit 1 is clear, it sets force-complete `+0xb55c` (`EngineCtx+0x51b70`) to 1 and
zeros the separate animation-service elapsed/duration fields at `+0xb564/+0xb568`. The next composition
commits every ordinary finite group; detached objects ignore the request. The port performs that observable
commit directly when executing `0x243`, excludes detached groups from its blocking wait predicate, and
continues including them in its visual recomposition predicate until natural completion.

Corpus evidence is unusually sharp: all 303 calls pass an immediate flag, with zero used 302 times and one
used once. SC0000's two CG-loader sites (`0x12723`, `0x13310`) write zero. `BTL.BIN@0x2b4d` writes one on an
animated battle object after its texture/movie or sprite-cell setup, matching the nonblocking background
animation contract. No boot seed, script-offset branch, or persistence state is involved.

---

### ROOM character selection, profile voice defaults, and legacy screen crossfade — `0x60` / `0x6c` / `0x25` (2026-07-21)

ROOM's apparently connected missing greeting and instant menu presentation were three separate native
contracts. Opcode `0x60` is the random selector, `0x6c` establishes the default voice-setting array, and
`0x25` owns the blocking transition between two complete offscreen frames.

`op_0x60_handler@0x426970` calls the imported CRT `rand`, fetches operand 2, and writes
`rand() % bound` to operand 1. Bound zero first writes zero and then raises script error `0x10005`.
`ROOM.BIN@0x5` passes bound 4; the result selects four character resource sets. Results 0, 1, and 2 also
select six voice ids each (`0x3365..0x3376`), including their greeting/farewell pair. Result 3 deliberately
leaves those locals zero in the release script, so that presentation variant is silent by script design.

The voice-enabled default was being defeated earlier in boot. `op_0x6c_handler@0x426d90` resolves operand
1 as a writable integer address and fills operand-2 consecutive dwords with `EngineCtx::anti_tamper_b`, the
native encoded representation of logical zero. It is a zero-range operation, not the scalar
`copy-to-global` suggested by Kelebek's label. `INITCONFIG@0x30` therefore clears the 13-cell character
voice-setting table `G[0x2e49..0x2e55]`, after which `0x1a2` registers each cell with the shared profile
service. CONFIG indexes the same table for voice preview and its 0/1 enable/suppress choices. The old port
instead wrote the scalar count 13 into `G[0x2e49]`; ROOM treats a nonzero value in cell 0 as voice suppression and
skipped all greeting/farewell ids. Correct zero-range execution fixes the boot default without implementing
or choosing a persistence backend for `0x1a2`/`0x1a3`.

`op_0x25_handler@0x41ce00` starts the legacy transition manager at `EngineCtx+0x1c38`, sets run-state bit
8, and keeps the script parked until the transition completes. Its three operands are source surface,
target surface, and a timing argument. For arguments up to 64, the handler arms that many milliseconds per
tick and advances the 8-bit alpha by 16. Larger arguments use `argument/16` milliseconds and alpha step
1. The branch is expressed in decompilation as `((arg < 65) - 1 & 0xfffffff1) + 0x10`; evaluating both
outcomes is significant (`true -> 16`, `false -> 1`). `interval_timer_poll_elapsed_steps@0x44d080` returns any missed timer intervals, and
`screen_transition_tick@0x43a7a0` mode 4 composites source first and target over it at the accumulated alpha;
at `0x100` it commits the target. `screen_transition_begin@0x439da0` and
`screen_transition_finalize@0x4399c0` own the endpoints. Thus an argument 10 lasts about 160 ms, while 30
lasts about 480 ms; input can force the endpoint through the same transition-abort service. Manual ROOM
validation caught the initially inverted alpha-step branch.

ROOM explicitly constructs those full frames. It selects/clears surface 1 and calls `0x20c`, mutates the
retained room presentation, captures surface 2 through another `0x20c`, restores the backbuffer, and calls
`0x25(1,2,10)`. The same form fades its menu away at `0x856`; after the farewell and one-second hold,
`0x25(1,2,30)@0x8dc` fades to the frame prepared for TITLE return. The interactive port now snapshots
retained objects when `0x20c` targets an offscreen surface, composites the captured target over the captured
source on the shared frame clock, blocks the VM through the endpoint, and only then permits the following
surface release/root reload. Headless hosts retain their non-rendering no-op policy.

#### Single-surface black/white fade family — `0x21`–`0x24` (2026-07-29)

The widest remaining opcode `0x22` is not a new renderer. It and `0x21` are the single-surface siblings of
the already implemented mode-4 `0x25` transition. Dispatch-table resolution gives:

| op | handler | transition mode | endpoint |
|---|---|---:|---|
| `0x21` | `op_0x21_fade_surface_in_from_black@0x41cb00` | 0 | captured surface |
| `0x22` | `op_0x22_fade_surface_out_to_black@0x41cbc0` | 1 | black |
| `0x23` | `op_0x23_fade_surface_in_from_white@0x41cc80` | 2 | captured surface |
| `0x24` | `op_0x24_fade_surface_out_to_white@0x41cd40` | 3 | white |

All four take `(surface_slot, timing_argument)`, set run-state bit 8, and use the exact `0x25` timing
conversion: arguments up to 64 use `argument` milliseconds per tick with alpha step 16; larger arguments
use `argument/16` milliseconds with step 1. Modes 0/2 start with an opaque solid scratch surface over the
captured frame and lower its alpha to zero. Modes 1/3 keep the captured frame opaque and raise the
black/white scratch alpha to 255. Re-entry finalizes the named endpoint before script execution resumes.

Himegari uses only the black pair: three `0x21` calls and eight `0x22` calls, all `(1,30)` and therefore
about 480 ms. The scripts first select surface 1, clear it, publish a complete retained frame through
`0x20c`, restore the backbuffer, then fade. `0x21` is the menu-entry reveal; `0x22` is the corresponding
menu/scene exit to black.

The port dispatches both opcodes through the existing blocking `LegacyScreenTransition` clock and terminal
publication path. Its compositor already clears the whole frame to opaque black, so an empty source list
models black-to-captured and an empty target list models captured-to-black without overloading the mode-4
missing-surface fallback (which intentionally snapshots the live retained frame). This closes 11 formerly
skipped instructions across seven distinct scripts and reduces the effectful-gap inventory from 18 to 16
distinct opcodes / 28 instructions.

The same manual pass exposed a resource-addressing issue after `0x6c` was fixed: ROOM did execute
`play-voice`, but `GodotAdvHost` attempted only inferred SC-section resolution. ROOM's voice operands
`0x3365..0x3376` are universal packed SYS4INI indices (for example `0x3365` is `EUA0016.OGG`). The first
compatibility fix tried an SC section and then a type-checked base-catalog fallback. Subsequent native RE
proved there is no first stage: ROOM and SC scripts alike pass an already absolute packed id directly to
the catalog opener. SC0000's `0x24 -> MAN999.OGG` works because that scene's inferred group begins at zero.

---

### Startup string predicate and unit-data block copy — `0x194` / `0x1b0` (2026-07-21)

The natural Game Start capture reached two previously unnamed effectful handlers. They are independent
contracts even though both appeared in the same startup fallback inventory.

`op_0x194_string_equals@0x426e20` resolves operands 2 and 3 as SYS4 strings, passes their data pointers and
explicit byte lengths to the native comparison helper, and writes `comparison == 0` to integer operand 1.
It is therefore an equality predicate, not a string assignment. The release corpus uses the result in
conditional branches; `GAMESTART@0x134c` compares `INPUTNAME` with `"?"`, while INIT2 also compares
`INPUTNAME` with an empty string during default-name initialization.

The port now executes `0x194` through the common string resolver and ordinal equality, so every operand
form accepted by that resolver shares one contract: inline literal, global string, local string, global
string pointer, and local string pointer. Focused tests cover equality and inequality across those forms
plus the release GAMESTART compare-then-`jcc` shape. The full traced SYSTEM4-to-SC0000 regression reaches
GAMESTART without emitting a `0x194` fallback.

`op_0x63_take_address@0x426ac0` is the companion address operation. It passes operand 2 and unsubscripted
indices `-1/-1` to `vm_operand_resolve_address@0x425a50`, then stores the returned address in operand 1
through `vm_pointer_operand_write@0x416090`. The resolver returns the backing-cell address for direct
global/local integer or string operands; for pointer operands it returns the target already held by the
pointer, not the address of the pointer slot. Thus `0x63(dst_ptr, source)` is typed address aliasing. All 92
release-corpus sites use a local integer-pointer destination; sources are local pointers 81 times, local
integers seven times, and global integers four times.

`op_0x1b0_copy_dwords@0x427060` fetches operand 3 as a cell count, resolves addressable operands 1 and 2 as
source and destination, and calls `memcpy(destination, source, count * 4)`. The corpus has 65 calls: direct
global/local spans as well as local pointers, with 43 immediately preceded by `0x63`. The boot capture
reached the pair in `UNITECH`/`CALCCC`; those scripts use it to copy record-shaped arrays between per-entity
tables and working buffers.

The port now maps both operations onto its domain-preserving `VmAddress` model. Direct local/global cells
retain their bank, aliasing an existing pointer retains its target bank, and `0x1b0` copies consecutive
32-bit integer cells through the resolved endpoints. Focused tests cover local-to-global, global-to-local,
an alias of a `lookup-array` result, direct spans, and the native string-pointer destination form of
`0x63`. The step-traced SYSTEM4-to-SC0000 regression reaches both operations without fallback. Static
coverage is consequently 31/31 handled for UNITECH and 14/15 for CALCCC; CALCCC's only remaining gap is
the deliberately deferred shared-profile write `0x1a2`.

---

### ADV layout reset cursor and overflow bounds — `0x79` / `0x1c1` (2026-07-21)

SYSTEM4 uses these two three-operand operations as a pair while constructing its nine ADV layouts. They
configure persistent layout properties used by later resets; neither operation draws text or moves the
current cursor immediately.

`op_0x79_set_adv_text_reset_cursor@0x41eb50` reads `(layout_slot, x, y)` and calls
`adv_text_layout_set_reset_cursor@0x44fed0`. Slot zero resolves through the current-layout selector at
manager `+0x4c8`; otherwise the helper selects `manager+0x414[slot]`. It writes `x/y` to layout
`+0x1c/+0x20`. This is distinct from `0x7a`, whose `adv_text_set_cursor@0x4530f0` path modifies the live
cursor record immediately.

`op_0x1c1_set_adv_text_bounds@0x41f6c0` reads `(layout_slot, right, bottom)` and calls
`adv_text_layout_set_bounds@0x44ff00`, which writes the pair to layout `+0x24/+0x28`. They are absolute
layout-local overflow coordinates. `adv_text_layout_check_overflow@0x45efc0` compares glyph end x/y
against those fields and returns horizontal/vertical overflow bits. When a layout resets,
`adv_text_layout_reset_cursor_and_bounds_record@0x455070` replaces its retained state with a five-dword
record `{0, reset_x, reset_y, right, bottom}` copied from all four configured fields.

The corpus supplies a complete consistency check: all nine sites for each opcode are in SYSTEM4 and
alternate after layouts 1–9 are defined and initially reset. Layout 1 configures cursor `(100,47)` and
bounds `(720,147)`; layouts 2–6 use `(45,42)` and `(645,135)`; layout 7 uses `(53,10)` and `(495,180)`;
layout 8 uses `(53,10)` and `(495,60)`; layout 9 uses `(10,10)` and `(250,368)`. Layout 1 computes its
bounds as `100+620` and `47+100`, further excluding width/height-delta semantics.

The port now keeps configured reset cursor and right/bottom bounds in its engine-owned layout state.
`0x70` initializes bounds from width/height, `0x79` changes only the deferred reset cursor, `0x1c1`
changes the bounds, and `0x71` restores the configured cursor while notifying the host of the concrete
selected slot. Retained snapshots carry the boundaries, and ordinary/history Godot labels are positioned
from the layout origin and sized from `right-cursor_x` / `bottom-cursor_y`; the former hardcoded slot-1
rectangle is gone. The ordinary overlay remains bound to the parent input wait's captured layout while a
nested callback such as HISTORY selects and renders into layouts 2–6; closing the callback therefore
reveals the retained parent text at its original geometry rather than consulting the callback's last
current-layout selection. The direct-scene SYSTEM4 bootstrap also recognizes all nine configuration pairs and
evaluates the two constant `add` expressions used for slot 1 without entering SYSTEM4's control flow. This
is script-owned state, with no boot seed or game-specific coordinates in the runtime.

---

### Logical input-action configuration — `0xfe` / `0x107` / `0x10b` / `0x10c` (2026-07-21)

SYSTEM4's adjacent input setup block defines the logical action namespace later consumed by callback
poll/dispatch ops `0xff` and `0x100`. These calls are effectful engine configuration, not declarations:

- `op_0xfe_set_input_action_count@0x421390` stores an unsigned count below 32 at EngineCtx `+0x814` and
  throws script error `0x10005` for an invalid count. SYSTEM4 sets 10, so callback dispatch scans actions
  0 through 9. If the polled mask is empty, `op_0x100_dispatch_joy_callbacks@0x416f00` instead invokes
  callback slot 10 and resumes after itself; that slot is the native no-input/release path, not action 10.
- `op_0x107_map_joystick_button@0x421550` writes physical joystick button numbers into the 32-entry
  EngineCtx `+0x89c` table. Joystick axes directly emit actions 0=up, 1=right, 2=down, 3=left; table slot
  N emits action N+4. SYSTEM4 maps physical buttons 0,3,2,1,6,7 to actions 4 through 9.
- `op_0x10b_map_mouse_button@0x4216f0` writes a logical button slot into the table at EngineCtx `+0x135c`,
  indexed by physical mouse button. Polling adds four to the stored slot. Initialization zeroes this map,
  making left mouse action 4 by default; SYSTEM4 `(3,1)` maps right mouse to action 7.
- `op_0x10c_map_keyboard_scancode@0x421730` translates a DirectInput DIK scan code through EngineCtx
  `+0x1828` to a Win32 virtual key, then writes the action into the 256-entry table at `+0x1428`.
  Initialization sets count 7 and prebinds 0=up, 1=right, 2=down, 3=left, 4=Enter, 5=Space, and
  6=Backspace. SYSTEM4 raises the count to 10 and adds Z to action 4, C/LeftCtrl to action 6, X to action
  7, PageUp to action 8, and PageDown to action 9; the earlier Enter/Space/Backspace mappings remain.

`input_poll_action_mask@0x4608b0` combines
`input_poll_keyboard_action_bits@0x4601a0`, `input_poll_mouse_action_bits@0x460240`, and
`input_poll_joystick_action_bits@0x460380`. The first three logical actions are therefore not a generic
Godot UI ordering; they are the native engine ABI established by the input manager and refined by scripts.

The same mask is also consumed directly by `adv_interpreter_tick@0x410fb0`. Logical action 6 is bit
`0x40`, the exact bit used by the transient ADV fast-forward/run-state path. There is no Ctrl-specific
branch: SYSTEM4's C and LeftCtrl mappings, the retained native Backspace default, and any joystick mapping
that emits action 6 all reach the same hold-to-fast-forward mechanism. Persistent op-`0x88` Skip injects
that same bit independently each tick.

The port now models this as one process-owned `InputBindings` service. It starts with the native seven
keyboard defaults, the four configuration handlers mutate it, and `0xff` combines live keyboard-VK,
left/right mouse, joystick-axis, and joystick-button state with the narrow logical injection used by tests.
`0x100` scans only actions below the configured count, resumes on itself for simultaneous held actions, and
uses callback slot `count` only for an empty mask. Godot translates layout-independent physical keys to the
native Win32 VK namespace and sends physical mouse/standard joy events through the service; it no longer
assigns Godot's `ui_*` actions directly to AGE indices. Direct-scene diagnostics replay the same 16 immediate
SYSTEM4 configuration calls through `InputBindingBootstrap`, while natural boot executes the real opcodes.
The live action-6 state now also feeds the ADV fast-forward host channel while the ADV lifecycle service is
enabled. That transient channel is kept separate from persistent op-`0x88` Skip, so releasing the held key
cannot clear the user's toggle; both channels share the existing text completion, wait advance, voice
deferral, and skip cadence. This needs no profile storage, boot seed, or game-specific conditional.

### ADV right-click/X system-menu path (2026-07-21)

Right-click during ADV is not a frontend-owned menu shortcut. It is the same script-owned keyed-hotspot
mechanism already partially modeled for SC0000. SYSTEM4 maps physical right mouse and keyboard X to logical
action 7. Each of the 136 SC-family scripts registers a 1x1 dummy rectangle whose activation callback is
the local branch that cancels the ADV hotspot wait, calls raw script id `0x1f` (`MENU.BIN`), then rebuilds
the parent ADV controls and redraws the retained page. Opcode `0x97` binds action 7 to that rectangle.

Native `adv_input_service_poll@0x411230` polls the configured logical-action mask first, then calls
`input_hotspot_poll_bound_action_callback@0x403fb0`. The helper scans armed records in registration order;
for each nonnegative op-`0x97` action index it tests `mask & (1 << action)` and returns the record's ordinary
activation callback PC. This identified the former port seam: `HotspotRegistry.BindKey` retained the action
on the matching record, but no code consumed `Entry.InputBit`; Godot routed only pointer-left activation and
action-4/5 page advance. Right mouse therefore reached `InputBindings` as action 7 without queuing MENU.

The implemented bridge is engine-generic. `HotspotRegistry.ActivateBoundActions` scans armed records in
registration order, consumes the first matching logical-action binding through the ordinary activation
target, and shares pointer activation's disarm/rearm lifecycle. Godot routes pressed keyboard, mouse, and
joystick logical-action masks through the VM before ordinary page advance and wakes the existing callback
service. There is no MENU-specific branch in Godot or the VM. A real SC0000 regression proves action 7
enters the release `MENU.BIN` and restores the parent ADV hotspot registry after controlled return.

The reached script path is promising but should be validated incrementally. `MENU.BIN` is 48/49 opcodes
handled and its only static gap is op `0x80`; `INFO.BIN`, which selects character/enemy/voice/affinity/item
information pages, is 30/30 handled. The large `CHMENU` and five `INFO*` detail screens are roughly
91.5–95% handled and may expose secondary visual/data gaps after the shell opens. SAVE and CONFIG are only
about 80% handled and remain separate storage/audio-settings work, not prerequisites for opening or closing
the system menu.

The sole `MENU.BIN` gap is now decoded: `op_0x80_set_default_gfx_object_slot@0x41ed40` stores operand 1 at
EngineCtx `+0x14e08`; `op_0x1d9_handler@0x420a30` substitutes that selected slot only when its explicit
object-slot operand is zero. MENU-family scripts select slots 7/8/9 on entry and restore slot 1 during
teardown. The port now retains this engine-owned selector in `GfxState.DefaultObjectSlot`; MENU's entry path
selects slot 8 in the regression. The surveyed MENU/INFO scripts do not themselves call op `0x1d9`, so this
state was not the cause of the former missing launch.

### Indexed two-key sort — `0x12f` (2026-07-21)

`op_0x12f_sort_indices_by_key_sum@0x429360` takes `(out_indices, key_a, key_b, count)`. It resolves the
first three operands as integer-array bases, seeds output index zero, and performs a stable insertion sort
over source indices `0..count-1`. The comparison is signed
`unchecked(key_a[index] + key_b[index])`; a new index moves left only when its sum is strictly smaller, so
equal sums preserve source order. The native handler's final loop does not change that result: it encodes
the directly written output cells back into AGE's protected integer-bank representation.

This is the cause of the first observed character-menu discrepancy. Natural New Game correctly runs
UNITECH and enters SC0000 with party slot 2 carrying flags `0x13` and character id 2. CHMENU constructs
100 party-slot sort keys, calls `0x12f` at `0x1c9b`, and reads the populated tail of the resulting index
permutation. The port's fallback no-op left that permutation zero-filled, so CHMENU selected slot zero and
rendered an apparently empty party even though the authoritative unit state already existed. The port now
implements the generic sort with addressed local/global arrays, native signed 32-bit addition and overflow,
stable equal-key ordering, repeated count reads, and the native unconditional output-index-zero write.
Focused tests cover stability, overflow, and zero count. A real-script regression carries natural
SYSTEM4-to-SC0000 state into CHMENU and stops after `0x1db8`, proving selected slot 2 survives the first
release roster sort without a fallback. No startup seed or menu-specific roster injection is involved.

### Numeric glyph styles/draws and half-byte string length — `0x13a` / `0x23b` / `0x1a6` (2026-07-21)

The missing values in the first working `DEBUGMAP` field HUD are not missing globals or a failed texture
load. They are generated by a dedicated decimal-glyph subsystem whose two opcodes were still skipped by
the port:

- `op_0x13a_register_numeric_glyph_style@0x421ab0` takes
  `(style_index, surface_slot, atlas_x, atlas_y, digit_width, digit_height)`. It accepts style indices
  `0..10` and stores the remaining five operands in the 20-byte record at
  `EngineCtx+0x55180+style_index*0x14`; an invalid index raises the standard script error. The corpus has
  74 registrations in 24 scripts, including eight in `DRAWCHP.BIN`.
- `op_0x23b_draw_decimal_glyphs@0x424190` takes
  `(base_handle, style_index, value, x, y, digit_capacity, flags)`. It first erases the destination handle
  range, splits the signed integer through division/remainder by ten, and binds one retained object per
  displayed digit using horizontally adjacent cells from the selected style record. Flag bit `0x1`
  includes leading zeroes, bit `0x2` centers the used digits, and bit `0x4` left-aligns them; without an
  alignment flag the value is right-aligned in the requested capacity. An invalid or unregistered style
  raises the script error. The corpus has 147 draws in the same 24 scripts; `DRAWCHP.BIN` has 22 and uses
  them for the field HUD's turn/control/mana/level/HP/SP/FS values.

This path creates ordinary retained graphics objects, so the existing atlas decode and compositor are
the correct backend; it is not an immediate `GodotAdvHost.DrawTexture` raster operation. The port now
models all 11 EngineCtx style records in `GfxState` and implements both dispatches. Each `0x23b` call erases
its full destination-handle capacity and then uses the ordinary
`BindDraw` path for every displayed digit, preserving surface replacement, z-order, and compositor effects.

The absent unit and weapon names are a separate layout-compute gap. `DRAWCHP.BIN` does populate both
strings and calls `draw-string` at `0x9c6`/`0x9f5`, but first centers each one with opcode `0x1a6`.
`op_0x1a6_half_byte_strlen@0x427020` resolves the NUL-terminated engine byte string and writes
`strlen(bytes) >> 1`. The script multiplies that result by 21 and subtracts it from x=257 on a 263-pixel
scratch surface. With the opcode skipped, x remains 257 and Godot correctly clips nearly all of the text.
The port reproduces the encoded byte count rather than using the .NET UTF-16 character count. The VM's
native-string code page is configurable for other container frontends and defaults to SYS4's CP932; the
calculation also stops at an embedded NUL before shifting. Focused tests cover CP932 mixed-width strings,
all three numeric layout flags, zero padding, retained-object replacement, and invalid/unregistered styles.

### BUNKI popup sizing and placement — opcodes `0x2c5` and `0x195` (2026-07-21)

Field confirmation popups and TITLE's shipped developer menu share `BUNKI.BIN`; neither uses the ordinary
VN text-layout overlay. BUNKI creates surface 200, draws its strings into that surface through opcode
`0x204`, decorates it with the common menu frame, and binds the completed surface as a retained object.

`op_0x2c5_byte_strlen@0x42a690` takes `(destination, string)`, resolves operand 2 as an engine byte string,
scans to its NUL terminator, and writes the raw byte count. This differs from a Unicode character count but
matches BUNKI's later conversion: it finds the longest choice/title byte length, adds four bytes of padding,
multiplies by 21, and divides by two to obtain the full-width glyph-space estimate used for panel width and
the shared left edge of the primary labels.

Before implementation, the port skipped `0x2c5`, leaving BUNKI's maximum-length local at zero. For FIELD's
`待機`/`帰還`/`キャンセル` popup, the panel still hits the same 240-pixel minimum but the computed label
origin moves from surface x=67 to x=120, a 53-pixel right shift which makes `キャンセル` touch/spill beyond
the frame. TITLE's longer developer choices should expand the surface beyond 240 pixels; the skipped result
instead keeps the minimum panel and makes the overflow much larger.

The corrected native/port comparison also exposes an independent 30-pixel vertical error. Dispatch slot
`ctx[0x26c93+0x195]` resolves to `op_0x195_string_not_equals@0x426f20`, the inverse of sibling opcode
`0x194`: it compares two complete engine strings and writes one when their byte ranges differ. BUNKI invokes
it three times to test whether optional title global string `0x7da` is nonempty. The skipped opcode does not
write zero for an empty title. At the final test (`BUNKI@0x905`), destination local `0x99` still contains a
nonzero graphics handle from frame construction, so the following branch falsely draws/reserves the title
row and advances the choice y cursor by `0x1e` even though the empty title has no visible glyphs. Implementing
`0x195` therefore removes the exact one-row downward shift; this is not a Godot font-baseline discrepancy or
an inherited VN cursor indent.

Both operations are now implemented as shared VM semantics. `0x2c5` uses the same configurable native-string
encoding helper as `0x1a6` (CP932 for SYS4), stops at an embedded NUL, and writes the unshifted byte count.
`0x195` is ordinal inequality through the existing literal/global/local/string-pointer resolver and always
writes zero or one, so stale destinations cannot leak into the branch. Focused tests cover mixed-width CP932,
embedded-NUL termination, all observed comparison operand classes, and the exact BUNKI empty-title stale-handle
case. The full 281-test engine suite, zero-warning Godot build, and threaded frontend selftest pass; manual
DEBUGMAP and developer-menu visual rechecks remain.

#### BUNKI popup publication and BUNKIMOVE lifecycle (2026-07-27)

BUNKI's opening/closing effect is an offscreen capture, not an independent decoration behind a stable
popup. BUNKI builds the complete panel under handles `60000..60099`, including surface-200 text, and calls
`BUNKIMOVE.BIN`. BUNKIMOVE selects 800x600 surface 2, publishes that retained range into it, restores the
backbuffer, binds the capture at handle `59999`, and scales it horizontally and then vertically through
timed callbacks. Each transition frame publishes `[0,60000)`, deliberately including the animated capture
while excluding BUNKI's stable objects. On open, BUNKI publishes the stable menu only after BUNKIMOVE
returns; on close, it removes the stable range after the reverse animation.

The port previously treated a backbuffer `0x222` as a generic repaint request and reconstructed every
visible retained object. It therefore drew handles `60000+` at full size above handle `59999`, making the
real popup appear immediately while its opening animation ran behind it; closing had the symmetric error.
Godot now retains zero-based backbuffer publication ranges and filters reconstruction to the active range.
Nonzero ranges remain incremental overlays over a full reconstruction because the port does not preserve
native D3D backbuffer pixels between publications.

Surface text also remains modeled metadata rather than pixels. Selected-target publication now projects
that metadata into the destination surface, and the final Godot labels inherit the retained object's
projected scale and rotation. Consequently BUNKIMOVE's captured labels follow the same transform as its
panel instead of escaping as full-size Control overlays.

### Formatted integers on text surfaces — opcode `0x205` (2026-07-21)

The deployment picker opened from an empty DEBUGMAP deployment slot uses a third numeric path. Its red
unit-information card is built by `DRAWENP.BIN` on temporary surface `0x51`: ordinary `0x204` calls draw
labels and separators such as `LV` and `/`, while 16 calls to previously skipped opcode `0x205` draw the
level, current/maximum HP/SP/FS, and the two stat columns. This explains why the values alone were absent
even after the separate `0x13a`/`0x23b` retained-glyph HUD path was restored.

`op_0x205_handler@0x422ab0` takes
`(surface_slot, x, y, value, field_width, flags)`. It records the 13-dword instruction length, calls
`format_integer_for_surface@0x407190`, and sends the returned string and adjusted x coordinate to the same
`draw_string_to_surface@0x450150` worker used by opcode `0x204`. The field width includes an optional sign.
Flag bit `0x1` zero-pads, `0x2` centers omitted leading cells, and `0x4` left-aligns; the default keeps the
field's right edge fixed by moving x right for omitted cells. Bits `0x8`/`0x10` request a plus sign for a
positive/zero value, while bit `0x20` gives zero a minus sign. Bit `0x10000`, used by every `DRAWENP` site,
keeps half-width ASCII and uses half the current font-cell advance; without it,
`ascii_to_fullwidth_cp932_inplace@0x417800` converts the formatted field to full-width CP932.

The VM now routes the formatted result through the existing styled surface-text host path, preserving the
font, color, and effect changes surrounding each call. Focused tests reproduce DRAWENP's exact level-80
call and cover half-width right alignment, zero padding, and the observed full-width left-aligned variant.
DRAWENP is now 36/36 opcodes and 611/611 instructions handled; all 284 engine tests, the zero-warning Godot
build, and threaded frontend selftest pass.

### DEBUGMAP-to-battle frontier opcode cluster (2026-07-21)

A static call-graph/coverage pass from `FIELD.BIN` through selection, movement, combat, damage, growth, and
status helpers shows that the next gameplay risk is presentation plumbing rather than battle arithmetic.
The following native handlers were decoded while bounding that slice:

- `op_0x191_absolute_value@0x426de0` writes
  `(value ^ (value >> 31)) - (value >> 31)` through operand 1. This is signed 32-bit absolute value; all
  five corpus sites are in `SELACT.BIN`, normalizing a signed preview delta before display.
- `op_0xd0_get_monotonic_time_ms@0x428860` writes `timeGetTime()` to operand 1. `BTL.BIN` samples it around
  its timed callback/HP presentation, and `MVRTN.BIN` contains the other two calls. The port's host-owned
  `FrameClock.NowMs` is the matching monotonic, speed-scaled service timebase.
- `op_0x23c_sample_frame_time@0x417580` shifts `EngineCtx+0x51b64` to `+0x51b68`, then stores
  `timeGetTime()` as the new current timestamp. BTL callback frames, FIELD/USEMAGIC movie loops, ADDEXP,
  and SHOWGROW place it next to presentation boundaries.
- `op_0x23a_query_movie_surface_active@0x42a440` writes zero for an empty surface slot; otherwise it writes
  whether the movie-surface field at `+0x42c` is nonzero. All four corpus sites use that result as a movie
  completion-loop predicate: BTL scans its active combat surfaces, while FIELD and USEMAGIC poll slot 42.
- `op_0x24e_set_gfx_animation_service_flags@0x425070` copies its operand directly to
  `EngineCtx.gfx_animation_service_flags` (`+0x51b80`). BTL brackets combat presentation with 1/0 and
  GAMECLEAR uses 3/0. Bit 1 is independently consumed by opcode `0x243` to suppress a force-complete and
  animation-clock-reset request.
- `op_0x207_copy_surface_rect@0x422b50` builds source/destination rectangles from
  `(source_surface, destination_surface, source_x, source_y, width, height, destination_x, destination_y)`
  and calls `gfx_copy_surface_rect@0x477da0`. The worker validates both slots, clips both rectangles while
  preserving their correspondence, treats an empty intersection as success, marks the destination dirty,
  and copies through locked D3D surfaces. Its 15 corpus sites are isolated to DRAWMINIMAP (8), STATUS (3),
  READICON (2), and DRAWTIP (2).
- `op_0x2c0_schedule_voice_playback@0x425290` forwards
  `(voice_id, playback_variant, delay_ms)` to `voice_schedule_delayed_playback@0x488480` on the service at
  `EngineCtx+0x14508`. The setter marks one pending request active and clears its start timestamp. The main
  engine tick calls `voice_tick_delayed_playback@0x4884d0`, which captures the first tick then, after
  unsigned elapsed time reaches the delay, clears the request and invokes
  `voice_play_indexed_asset(voice_id, playback_variant)`. BTL's sole call schedules a randomized combat
  voice with variant 0 and an entity-specific delay; it is presentation-only, not battle state.

These handlers and their newly understood workers are renamed/commented in Ghidra `/v2`; the program was saved.
The exact source metadata lives in `vm-map/opcodes.toml`. The port now implements this cluster against the
shared Godot frame clock, retained graphics state, movie decoder state, mutable RGBA surfaces, and delayed
voice service. `0x207` copies colorkey-baked source pixels into immutable published snapshots so compositor
reads cannot race VM-side mutations. The first complete player attack remains the manual acceptance test.
BTL's two `0x1a2` shared-profile writes were initially deferred because they do not feed same-exchange
combat state; the unified profile service now handles them through the ordinary opcode dispatch.

### Movement/attack flood-fill FIFO -- opcodes `0x132`-`0x134` (2026-07-21)

The DEBUGMAP symptom "selected unit can wait on its origin, but has no blue reachable tiles and cannot
move" is caused by the only three effectful gaps in `MVSEEK.BIN`, not by `CALCSCOPE` or FIELD input. AGE
provides 11 context-owned integer FIFO slots:

- `op_0x132_reset_int_queue@0x4217d0` validates `queue_id <= 10`, destroys any existing object in the
  selected slot, and allocates a fresh 0x1c-byte FIFO. `int_queue_construct@0x4074c0` allocates 0x100
  dwords, uses another 0x100 dwords as its growth quantum, and zeros the read/end/high-water indices.
- `op_0x133_enqueue_int@0x4218d0` validates the slot and calls `int_queue_enqueue@0x408930`. The helper
  appends at the end, first compacting consumed entries when possible or growing storage when necessary.
- `op_0x134_try_dequeue_int@0x429620` writes `(success=1, value)` and advances the read index when the FIFO
  is nonempty; otherwise it writes `success=0`. Its value output is not meaningful on failure, and both
  shipped callers branch on success before inspecting it.

Only `MVSEEK.BIN` and `ATSEEK.BIN` use this cluster. Both reset queue 0, pack map coordinates as
`(x << 16) + y`, enqueue the origin, and repeatedly dequeue a tile and enqueue accepted neighbors.
`MVSEEK` writes the origin's movement cost before invoking the queue ops. The former generic stubs left the
origin valid but left `0x134`'s zero-initialized success local unchanged, so the flood fill exited on its
first loop test. This exactly explained why clicking the occupied tile still reached Wait while neither
reachable overlays nor movement targets existed; `ATSEEK` was blocked identically.

The port now retains 11 VM-lifetime integer FIFO slots and implements reset/enqueue/try-dequeue with native
signed-dword behavior. It diagnoses invalid or never-reset slots; shipped scripts always reset queue 0
first. On empty dequeue it writes `success=0` and retains the value destination rather than reproducing the
native handler's unusable implementation-pointer value. Focused tests cover independent slots, FIFO order,
signed values, empty reads, and reset replacement. Real-script tests seed a bounded passable grid and prove
that shipped `MVSEEK.BIN` populates all four neighboring movement costs while `ATSEEK.BIN` populates attack
distance 1 around the origin. Both scripts are now 100% handled. The handlers and queue helpers are
renamed/commented in Ghidra `/v2`; the program was saved.

The first live recheck after that FIFO implementation still showed no movement, exposing the caller layer
that the direct real-script tests had bypassed. SYSTEM4 does not leave these workers to ordinary
`call-script`: its only three opcode `0x06` sites preload `ATSEEK.BIN` (`0x337f`) into EngineCtx frame slot
`0x1d`, `SETROUTE.BIN` (`0x3380`) into slot `0x1e`, and `MVSEEK.BIN` (`0x3381`) into slot `0x1f`. FIELD and
the route helpers then contain 64 total opcode `0x08` calls targeting only those three slots. Both opcodes
were still generic effectful stubs, so live FIELD never entered MVSEEK despite the now-correct worker.

Native `op_0x6_preload_script_slot@0x41bdb0` temporarily selects a caller-specified context index (valid
range 0..39), calls the ordinary script resource loader there, then restores the current index without
executing the loaded script. `op_0x8_call_preloaded_script_slot@0x41bf00` switches to the selected loaded
context, records the caller context as its return target, resets its PC to codebase, and begins execution.
`op_0x2_exit_or_return_frame@0x417940` disposes only an adjacent child (`parent+1 == current`); the
non-adjacent service slots therefore survive return with their local banks allocated.

The VM now models those persistent preloaded frames, clears them on root-scene reset, and emits normal
call-script trace events when invoked. A focused worker proves repeated `0x08` calls restart code while
retaining locals. A second regression uses the exact SYSTEM4 ABI—`0x06(0x3381,0x1f)` followed by
`0x08(0x1f)`—to run the shipped MVSEEK and populate all four neighboring movement costs. FIELD's five
formerly skipped `0x08` instructions are handled, as are all indirect MVSEEK/ATSEEK/SETROUTE consumers.
The two native handlers are renamed/commented in Ghidra `/v2`; the program was saved.
Manual DEBUGMAP acceptance confirms that reachable-tile overlays and movement now work through this live
route and that player combat is reachable. Any remaining work at this frontier should start from the
concrete combat discrepancies observed in that run rather than from movement search or dispatch.

---

## Native walls backlog (targets for this loop)

- ~~**call-script dispatch**~~ — **SOLVED** (above): `call-script <id>` = raw SYS4INI file index.
- **decision→scene** — how `0x62ccf`/the decision selects the next `SCxxxx`. Now narrower: scenes load
  via `call-script`/the same SYS4INI-index loader, so the open question is only where the decision
  value is turned into a scene *id* (a caller of SCJUMP; re-aimed away from `u00428010`).
- ~~**op `0x60`**~~ — **SOLVED**: CRT `rand() % bound`, including the zero-bound script-error path.
- ~~**gfx command-buffer**~~ — **DONE** (the `0x212–0x21a` positioned-object subsystem = the rendering
  drift): all 14 ops reversed + implemented against a host-side `GfxState`, and the missing INIT2 boot
  state supplied via `--boot`. CGs render (screenshot-confirmed). See the op `0x215` finding + "The render
  drift's SECOND half" above. The then-remaining `AE*` blend and cold-anchor work is resolved by the later
  blend, geometry, animation, and retained-presentation sections.

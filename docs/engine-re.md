# Native-engine reverse engineering (Ghidra + MCP)

> **Executable scope:** every address, `EngineCtx` offset, and `/v2` annotation in this document belongs
> to Himegari `S4IC422` / `SYS4422` unless a finding explicitly names broader evidence. The installed
> packed `AGE.EXE` is 1,105,920 bytes with SHA-256
> `5c80a3a8c5b2e755647247c2195446385c5f1920db422bcb35cfd41cd77b5ee6`; the unpacked Ghidra program
> `/v2/range_00400000.bin` is 2,490,368 bytes with SHA-256
> `3d7543ba97e0fd3d59db05d387096dd3afa912b8ec8bcb3c916d9dd313496476`. Do not apply these offsets
> to Kamidori or another AGE executable without separately matching that executable and image. The program's
> listing restores the packer's API sites (Runbook, "Packer API sites"), so its bytes differ from the dump file
> at those 228 places.

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
- IAT reconstruction — **tried, DOESN'T WORK on this binary (2026-07-09):** a local PE-sieve 0.4.1.1 `/pid
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
  at `build/pe-sieve/process_<pid>/` (disposable). The executable was removed from the repository and purged
  from reachable history on 2026-08-03; it is obsolete and is not needed to reproduce the accepted workflow.

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
  `tools/frida/map_imports.py [--recon]` → `build/games/himegari/import-map.json`; plan
  `docs/superpowers/plans/2026-07-09-frida-import-map.md`.
  **Known limit (by design):** only the module-resident IAT is labelable. `d3d9`/`shell32`/`dsound`/CRT are
  `GetProcAddress`-resolved into HEAP (not in the `0x400000` dump), so they aren't labeled — and D3D9 is used
  via COM vtables (`Present` = device vtable slot 17, see `probe_present.py`), not an import thunk, so this
  costs us nothing on the render path. 29 isolated singleton matches were set aside (`build/games/himegari/import-map-singletons.json`),
  not auto-applied.

  **Reading the labels.** They name the export each slot held at run time, so a forwarded API carries its
  target's name: kernel32's critical-section calls are `imp_ntdll_Rtl*CriticalSection`, HeapReAlloc is
  `imp_ntdll_RtlReAllocateHeap`, DefWindowProcA is `imp_ntdll_NtdllDefWindowProc_A`, and COM calls are
  `imp_combase_*`. Two exports at one address get one name: `imp_user32_RegisterClipboardFormatA` is the
  RegisterWindowMessageA slot. The protector hooks GetProcAddress, so its slot `0x56f0c4` held a protector address
  and was named from its callers, as was `0x56f294` (SHGetSpecialFolderPathA, the one shell32 import).

**Packer API sites (restored 2026-11-09).** The protector rewrote API calls in both packed builds, Himegari's
`AGE.EXE` and Kamidori's 4.33G. Such a site is a `call rel32` into the protector's stub area (`/v2`: 198 `call
0x2750000`, 29 calls to a stub page `+4`) and one junk byte. The stub calls the API and returns to the site + 6. At
some sites the protector also took the next instruction into its stub (a `jmp short`, `mov reg, eax` or `jmp
rel32` of length L): the junk is then 1 + L bytes and the code resumes at the site + 6 + L. It did so at only some
of the sites that have such an instruction (4.33G: 9 of the 13 calls followed by a `jmp short`). A tail call `jmp
[IAT]` takes the same form (`/v2` `0x4063c6` in `engine_message_box`). `/v2`'s `__EH_prolog` was moved out whole,
leaving `jmp 0x2790000` and 26 junk bytes at `0x554810`. A linear decoder loses sync at the junk.

The Ghidra programs restore each site in their listing; the dump files are unchanged. The site's bytes become
`call [slot]` (`ff15`) or `jmp [slot]` (`ff25`) plus any stolen instruction. Its EOL comment, `Packer restore …`,
keeps the original bytes and the evidence, and a `Packer restore` bookmark names the API.

- `/kamidori/4.33G` (224 sites) was restored from the translation's unpacked 4.33F, which has the same code with
  `call [IAT]` and the same IAT layout.
- `/v2` (227 sites and `__EH_prolog`) has no unpacked twin, so every API there is inferred; treat it as such. 128
  come from matching the 16 instructions around the site against 4.33G's `call [API]` sites. A match was accepted
  at 12 of 16 with a margin of 2, at 11 with a margin of 4, or at 8 with a margin of 4 when `/v2` never calls the
  slot directly. Tried on `/v2`'s own direct calls, the first two rules chose no wrong API and the third, without
  the slot condition, chose 1 wrong in 759. The other 99, including the 13 sites whose stolen instruction was
  restored, were read by hand from their arguments and strings, the matching 4.33G routine, or the VC2005 CRT's
  source. Each comment says which.

**Typed imports (2026-11-09).** In `/v2`, `/kamidori/4.33G` and `/kamidori/SYS4433` every `imp_` slot is a pointer
to the API's function definition from Ghidra's `windows_vs12_32.gdt` (forwarder names folded as above;
Direct3DCreate9, absent from the archive, defined by hand). The decompiler therefore shows API arguments, and an
API's stack cleanup no longer depends on analysis guesses.

**Script gotchas (ghidra-mcp).** `run_ghidra_script` takes a full script path, copies the script into
`~/ghidra_scripts` and splits its arguments on spaces with no quoting, so pass paths without spaces. Its result
carries the compiler output for every script in that folder; print the script's own lines with a marker and
filter for it. `run_script_inline` wraps its body in a generated class, so code that needs imports or declares a
class must go in a file. A script that throws keeps the changes it made before the exception.

---

## Kamidori engine builds: 4.33F and 4.33G (2026-11-08)

Kamidori has two engine builds in the workspace, and they are different code, not one build with and without the
translation's hook:

| install | `AGE.EXE` | version | live module | dump |
|---|---|---|---|---|
| `Kamidori/`, clean, official updates applied | packed, 1,204,736 bytes, SHA-256 `52151686545215f350d2453febab91259450cf3fa79bfb218142765ce3f8842e` | 4.33G (4.33.7.0) | `0x400000`, 2,990,080 bytes | `build/games/kamidori/engine-dump/` (SHA-256 `eed00b04279566d1cb990208c144f470da4587d7e42256d1aa02298d59ffbc14`, captured from an unhooked AGE) |
| `Kamidori_Translated/`, English fan translation | unpacked plus a `.patch` section, 2,183,936 bytes, SHA-256 `977923a70f4170a5bb5847cef2a1f05ef1e79b45699c1204af29243be6ee84f4` | 4.33F (4.33.6.0) | `0x400000`, 2,294,528 bytes | `build/games/kamidori/engine-dump-translated/` |

Both builds place their sections at the same addresses (`.text` `0x401000..0x574000`, then `.rdata`, `.data`,
`.rsrc`), but the code differs. Comparing the two dumps:

- Unchanged: `g_pEngineCtx` `0x5c9bc8`, the interpreter at `0x41121e` and its frame offsets,
  `input_set_polling_enabled` `0x465a60`, and `_rand` `0x558368`.
- Moved: op `0x60`'s handler (4.33F `0x4276f0`, 4.33G `0x427650`), the WM_ACTIVATE polling call's return
  (`0x48e061` → `0x48e041`), and many call targets (by `0xb0` near `0x44d8xx`). `0x41ac80`, where 4.33F's op `0x23`
  handler starts, lies inside another function in 4.33G.
- The opcode table moved by 4 bytes: 4.33G's constructor at `0x413e70` (4.33F `0x413e50`) fills it at
  `ctx+0x9f704`, and the interpreter dispatches through `mov edx, [esi+eax*4+0x9f704]` at `0x41123f` (4.33F
  `+0x9f700`). Read at that base, both builds register the same 437 opcodes, and 4.33G's op `0x60` is the rand
  handler at `0x427650`. Read at 4.33F's base, 4.33G's table looks shifted by one opcode.
- Other context offsets moved too: the function at `0x404f30` reads `ctx+0xa5198` in 4.33F and `ctx+0xa519c` in
  4.33G.
- The operand accessors moved, for example fetch-value `0x4198d0` → `0x419900`, write `0x426750` → `0x4266b0`,
  address `0x4261a0` → `0x426100`, and bind `0x416900` → `0x416920`. All 14 accessors map one to one when
  same-opcode handlers of the two builds are aligned. `tools/operand_access.py` pins 4.33G's, and the operand
  contracts it derives from 4.33G equal those from 4.33F for all 437 handlers.
- The drive step `rand-seed` works on 4.33G with the probe at `0x42768f`. A 魔弾 battle drew op `0x60` 20 times
  at `COMMITBTL@0x928` once its reward screen closed, and the next turn end drew 1,000 times at `FIELD@0x940a`.
  This is the same pattern 4.33F showed.
- 4.33G's packer leaves its API calls as `call rel32` into a stub area plus one junk byte, where the translation's
  unpacked 4.33F has `call [IAT]`. Himegari's dump has the same pattern (198 such calls, for example `call
  0x2750000` at `0x429eb1`). A linear decoder loses sync at the junk byte; `operand_access.py` resumes past it,
  which also recovered Himegari op `0x14a`'s operand-1 write at `0x429ebc`.

**What this means.** Native Kamidori runs use the clean install's 4.33G since 2026-11-08, and
`tools/drive/kamidori.toml` holds 4.33G values. Behavior compared against native before then was 4.33F's.

**The 4.33G image (2026-11-09).** `/kamidori/4.33G/range_00400000.bin` is the clean build's dump
(`build/games/kamidori/engine-dump/`), imported like `/v2`. Its packer sites are restored (Runbook, "Packer API
sites") and its imports typed. The 4.33F image's names, plates and EOL comments were ported to it through a
function alignment of the two builds, with their 4.33F addresses translated; each ported plate ends with a
`[Ported from …]` note. Names were then borrowed from `/v2` where the code is identical or at least 90 % alike
(174, each plate noting `[Name borrowed from Himegari /v2 …]`), and 107 opcode handlers that kept a placeholder
name were named from the opcode registry's labels. `/kamidori/SYS4433` stays as the 4.33F reference.

Kamidori addresses this document and `vm-map/opcodes.toml` give without a build are 4.33G's since 2026-11-09,
except in the next section, which describes the 4.33F image. They were converted through the same alignment and
checked against the function names at both ends. `build/games/kamidori/f2g-address-conversion.tsv` lists every
converted value. The context struct moved too: 4.33G adds a dword before the opcode table, so Kamidori `ctx`
offsets from `0x9f700` up are 4 higher in 4.33G, and lower offsets are the same in both builds. That dword defers
a screen-mode present during a save restore ("Comparing engine builds" below).

## Comparing engine builds (2026-11-09)

`tools/revision_diff.py` compares the native code behind every opcode two engine dumps share (usage and report
format: `docs/tools-reference.md`, "Opcode revision diff"). Its purpose is a high-level list of what a new game's
engine changed, read before any playthrough, so a new game needs no full native-vs-port sweep to find its
revision changes; a sweep then checks the rows that matter. Four builds have dumps and pinned facts in
`tools/operand_access.py` `ENGINES`:

| Engine id | Game | AGE.EXE | ctx constructor | Opcode table | Handlers | C runtime starts |
|---|---|---|---|---|---:|---|
| `himegari` | Himegari (EU10) | 4.22 | `0x413860` | `ctx+0x9b24c` | 420 | `0x54f3de` |
| `kamidori` | Kamidori (EU12), clean install | 4.33G | `0x413e70` | `ctx+0x9f704` | 437 | `0x556878` |
| `kamidori-433f` | Kamidori, the English translation | 4.33F | `0x413e50` | `ctx+0x9f700` | 437 | `0x556878` |
| `arterial` | 創刻のアテリアル (EU13) | 4.38D | `0x414e10` | `ctx+0xa4b9c` | 473 | `0x583dd8` |

**Arterial.** `Arterial/創刻のアテリアル/` is a reference install with no port profile (`paths.REFERENCE_INSTALLS`),
kept only for engine comparison. Its packed `AGE.EXE` (1,294,848 bytes, SHA-256
`c6d4b8d03c834350c996832479ec1faf48b5f7c0ec763a101ec5cd601f76d421`, catalog `S4IC438`) was dumped from a
`native_scratch.py --reference arterial` copy by `dump_engine.py --reference arterial --spawn`, with the shell
folders redirected into the dump sandbox. The install checked unchanged afterwards, and the real saves under
`%LOCALAPPDATA%` were not touched. The module is based at `0x400000` and spans 3,227,648 bytes
(`build/engines/arterial/engine-dump/`). The dispatch-table finder (`lea edi,[reg+table]; mov ecx,0x400;
rep stosd`) located the constructor, and `operand_access.py --engine arterial --align-accessors --align-to
kamidori` aligned all 14 accessors unanimously. The Ghidra image is `/arterial/4.38D/range_00400000.bin`.
Function ID names start at `0x566214` (the NFM compression library). The DirectShow base classes follow, and the C
runtime begins at the `operator new[]` thunk `0x583dd8` (150 callers; `__security_check_cookie` at `0x5843fe`).
Unlike 4.22 and 4.33, most of 4.38D's engine code from about `0x4a0000` on is not 16-aligned, so alignment does
not mark where the runtime starts.

**Method.** Same-opcode handlers pair exactly. Aligning their instruction streams, and then their callees', votes
on which functions, context fields and globals correspond. Each function becomes a multiset of behavior facts, and
an opcode's facts are those of its handler and three callee levels. Compiler and layout idioms are weak facts:
object-field offsets, container element counts and `max_size` checks, record copy sizes, shift counts, pointer
strides, `std::string`'s small-buffer checks, the C++ library's exception messages and unidentified Windows API
calls. Register reuse, a constant changing role, code inlined just past the depth limit or into a shared helper,
and a call to a function the other build inlined do not count either. The tool's docstring lists every rule.
Known limits:
- Object layouts move per struct, which one offset map cannot follow. A change that only reads another object field
  is therefore reported as `minor`.
- API calls are not identified.
- Code no opcode reaches (frame loop, renderer, input) is compared only where a shared helper covers it.

**Noise floor: 4.33F against 4.33G.** 431 opcodes are `same`, 5 `minor`, 1 `differs`, and one shared helper
differs. Each one is a real code change:
- 4.33G's new dword `ctx+0x9f700` is set to 1 by `save_data_deserialize_and_begin_restore@0x40faa0` (reached from
  the numbered save and load ops `0xab`, `0x190`, `0x19f` and `0x1a1`, the `minor` ones). While it is set,
  `display_toggle_screen_mode_and_menu_bar@0x4130f0` (op `0x2cf`) only sets bit 1 when the mode is 2 and does not
  present. `op_0x20c_present_frame` (also `minor`) presents and then clears the dword. 4.33F always presents and
  tail-jumps into the present worker `0x489c40`.
- The helper `0x44ea00` (4.33F `0x44eab0`, used by 34 opcodes) reports `%s を開くことが出来ません．…ERROR CODE = %d`
  through the Windows API when a file does not open. 4.33F has no such path.

Before the tool learned tail jumps, import thunks, jump tables behind a no-return call, pointer-shaped strings and
context offsets computed by `add reg, imm`, the same pair showed 36 differing opcodes.

**Himegari against Kamidori (tuning pair).** 193 `same`, 102 `minor`, 125 `differs`; helpers 44 / 31 / 26, and 8
unpaired. The differences include:
- op `0x215`, scored 4 for its one `+bit 1`: the SYS4433 bit-0 test found by hand earlier ("SYS4433 reads a
  draw-unbound object as absent");
- the float operands of about 22 gfx opcodes (`operand N read` against `read-float`, with `fconst 0.5` rounding);
- new settings keys: `display:VirtualFullScreenType` (`0x10a`), `set:BlankExtentMode` (`0x221`, `0x8a`),
  `display:AspectMode` and `set:UseProportionalFont` (`0x9`);
- effect-parameter names (`Alpha`, `Angle`, `CenterU`, `SlideBlur`, …) loaded in the screen-mode switch;
- Himegari's inline Shift-JIS layout code in the text opcodes, which Kamidori restructured.

Op `0x202`'s alpha clamp to `0..255` is in both builds (`minor`). These rows are not triaged yet.

**Held out: Arterial.** Arterial was compared without retuning. Kamidori against Arterial gives 358 `same`, 43
`minor` and 36 `differs`; Himegari against Arterial gives 194 / 103 / 123. The verdicts agree across the three
pairs: no opcode is `same` in Himegari–Kamidori and in Kamidori–Arterial but `differs` in Himegari–Arterial. Nine
of 420 break the pattern another way, and each of those rests on one context field or one helper call (scores up
to 6), the noise class the report flags. Kamidori–Arterial's largest differences are new:
- `AGE Extend` and window-position keys (`set:SaveWinPos`, `set:WinPosLeft`, `set:WinPosTop`) in the
  screen-mode switch;
- vertex-buffer render paths (`CalcDiffuse`, `RenderPolygon`, `CopyScene`) under `0x20c`, `0x222` and `0x21d`;
- `message:AutoMessage*` settings in op `0x72`;
- op `0x2c7`'s substring, which now writes a string operand and warns when it cuts a full-width character.

## Kamidori SYS4433 comparison image and K6 frontier (2026-08-19)

This image is the English fan translation's 4.33F build (see "Kamidori engine builds" above), and the addresses
and `ctx` offsets in this section are 4.33F's. `/kamidori/4.33G` has the same names at its own addresses.
Kamidori was captured independently rather than treating Himegari's `/v2` image as a SYS4 baseline.
`tools/frida/dump_engine.py` recorded the installed 2,183,936-byte `AGE.EXE` (not packed: its `.text` equals the
live dump's, checked 2026-11-08) with SHA-256
`977923a70f4170a5bb5847cef2a1f05ef1e79b45699c1204af29243be6ee84f4`; the live module is based at
`0x400000`, spans 2,294,528 bytes, and its gap-free dump has SHA-256
`a071ca0fcc3f5955492d070c02e79d8f4ea8c54530d0a2bd1164811e07865a87`. The separate Ghidra program is
`/kamidori/SYS4433/range_00400000.bin`, imported as flat `x86:LE:32:default` at `0x400000`. Kamidori has
no borrowed Himegari landmark; the schema-v2 manifest under
`build/games/kamidori/engine-dump/manifest.json` is the capture authority.

`engine_ctx_construct_SYS4433@0x413e50` initializes Kamidori's opcode table at
`ctx+0x9f700`, so its dispatch rule is `*(ctx + 0x9f700 + opcode*4)`. This differs from Himegari's
`ctx+0x9b24c` table and is not an `EngineCtx` layout transfer. The K6 natural-boot frontier established and
annotated these handlers in the SYS4433 image:

| Opcode | SYS4433 handler | Finding reached naturally |
|---|---|---|
| `0x2bc` | `op_0x2bc_configure_wait_indicator_grid@0x4231e0` | `SYSTEM4@0xa0`; configures the wait-indicator atlas/grid and period. |
| `0x1b1` | `op_0x1b1_set_wait_indicator_follow_last_glyph@0x41d5f0` | `SYSTEM4@0xb7`; stores the follow-last-glyph mode at SYS4433 text-manager `+0x570`. |
| `0x2db` | `op_0x2db_set_text_aspect_mode@0x4235c0` | `SYSTEM4@0xbd`; mode 1 changes glyph metrics/font orientation and rebuilds primary font resources. |
| `0x2dc` | `op_0x2dc_get_font_family_count@0x42ba80` | `SELFONT@0xa5`; returns the installed-family vector size, or `-1` when empty. |
| `0x2dd` | `op_0x2dd_get_font_family_name@0x42d880` | `SELFONT@0x958/0xb86`; copies an indexed installed family, or empty when invalid. |
| `0x2de` | `op_0x2de_find_font_family_index@0x42bac0` | `CHECKCONFIG@0x0`; strips one leading `@`, then returns the installed-family index or `-1`. |
| `0x25a` | `op_0x25a_set_display_background_color@0x423120` | `SYSTEM4@0x1f3`; persists mode 1/RGB and recreates a logical-canvas filled surface. |
| `0x1be` | `op_0x1be_sound_channel_is_playing@0x429400` | `TITLE@0xd7`; validates one of thirteen channels and returns its normalized live-playing flag. |
| `0x24` | `op_0x24_fade_surface_out_to_white@0x41ad80` | `GAMESTART@0x19dc`; runs blocking screen-transition mode 3 from captured surface 1 to white with timing 80. |
| `0x2da` | `op_0x2da_register_extended_numeric_glyph_style@0x4234e0` | `DRAWCHP@0x626`; registers a seven-field decimal-glyph atlas style with independent source/destination spacing. |
| `0x251` | `op_0x251_queue_radial_blur_range_transition@0x422ff0` | `AIM@0xae`; queues a delayed radial-blur post-effect over the dungeon condition-object range. |

Comparative decompilation confirmed equivalent SYS4422 behavior for `0x1b1`, `0x1be`, `0x2bc`, `0x25a`,
and `0x24`; that supports shared runtime handlers without claiming a true base SYS4 dialect. Opcodes `0x2da`,
`0x2db`, `0x2dc`, `0x2dd`, and `0x2de` are installed only by the inspected SYS4433 constructor. Exact operand contracts and
structured profile/revision provenance remain canonical in `vm-map/opcodes.toml`.

The SYS4433 font-family cache is a contiguous vector of 0x20-byte strings at text-manager
`+0x313c0/+0x313c4` (`EngineCtx+0x45cf8/+0x45cfc`). Opcode `0x2dc` divides its byte span by 0x20 and
writes that count, except an empty vector produces `-1`. Opcode `0x2dd` bounds-checks a signed index, copies
that record through `vm_operand_write_string@0x42c600`, and returns an empty string for negative or
out-of-range indices. Opcode `0x2de` searches the same records by face name. This is revision-local
text-manager layout evidence, not a field transfer to the SYS4422 `EngineCtx` struct applied to `/v2`.

The first post-K6 Dungeon route reached `DRAWCHP.BIN@0x626` after `SYSTEM4 → FIELD → DRAWCHP` and established
the SYS4433 numeric-glyph extension. `op_0x2da_register_extended_numeric_glyph_style@0x4234e0` enforces
`style_index < 11`, then stores operands 2..8 in a seven-dword record at
`EngineCtx+0x5940c+style_index*0x1c`: surface slot, atlas x/y, digit width/height, source-cell spacing, and
destination-cell spacing. The legacy SYS4433 `op_0x13a_register_numeric_glyph_style@0x41fae0` writes the same
first five fields and clears both spacing fields. Consumer `op_0x23b_draw_decimal_glyphs@0x422000` advances
source atlas cells by `digit_width + source_spacing` and destination objects by
`digit_width + destination_spacing`, using the latter stride in right-, center-, and left-aligned placement.
The complete installed Kamidori corpus has 66 `0x2da` calls. All three functions are named/commented in the
SYS4433 Ghidra image; SYS4422 retains its independent five-dword style layout and has no installed `0x2da`
dispatch entry.

The next strict Dungeon frontier, after the `SC2310` ADV portion and condition display, was
`AIM.BIN@0xae`, opcode `0x251`. `op_0x251_queue_radial_blur_range_transition@0x422ff0` fetches twelve
operands and calls `gfx_queue_radial_blur_range_transition@0x486b90`. That worker creates retained special
command type 3/mode 1 at `command_handle`, with `target_surface`, `range_start`, and `range_count`. It stores
the first effect triple at record `+0x40/+0x44/+0x48`, the second at `+0x50/+0x54/+0x58`, and the start
delay/duration at `+0x8/+0xc`.

`gfx_present_retained_effect_commands@0x486d50` proves the field meanings. It linearly interpolates the
triples after the delay, binds the first sampled component as shader `Length`, divides the other two by the
target width/height for `CenterU` and `CenterV`, marks the retained handle interval for offscreen rendering,
and clears the command's active state at completion. The exact AIM call is
`(90000,14,70010,23,100,512,204,0,512,204,100,500)`: radial length contracts from 100 to 0 around
`(512,204)` after 100 ms over 500 ms. The adjacent `0x250` producer creates type 3/mode 0 and supplies
the sibling directional effect. `op_0x250_queue_directional_blur_range_transition@0x422f60` forwards ten
operands to `gfx_queue_directional_blur_range_transition@0x4869e0`: command handle, target surface, retained
range start/count, start Length/Angle, end Length/Angle, delay, and duration. The producer stores Length at
record `+0x40/+0x50` and Angle at `+0x4c/+0x5c`; the unused mode-1 center fields are zero. The shared
consumer samples those pairs linearly, passes Angle and Length directly to the mode-0 effect, and obtains
shader Width/Height from the target surface. The complete Kamidori corpus has 34 calls in 11 scripts. Story
code establishes degrees as the authored angle convention (`+90/-90` are common, while one SC0050 effect
interpolates through 1800 degrees for multiple turns). The port now retains the exact command lifecycle,
range selection, endpoint interpolation, and timing. Godot approximates the missing D3D9 directional shader
with six alpha-weighted translated samples, collapsing exactly to one unshifted sample at Length zero.
Kamidori's complete catalog contains 64 `0x251` calls in 28 scripts. The SYS4433 constructor installs the
handler at `ctx+0xa0044`; the inspected SYS4422 constructor has no corresponding dispatch-slot store, so the
semantic provenance is SYS4433-only rather than an inferred SYS4 baseline. All three SYS4433 functions are
named and commented in the Ghidra image; the `0x250` handler and producer are annotated there as well.

The first complete-corpus follow-up selected opcode `0x2d8` because it is used by combat calculation and
setup rather than only a peripheral menu. The SYS4433 dispatcher installs
`op_0x2d8_fill_int_array@0x42b9c0`. It resolves operand 1 as an integer-cell address, evaluates operand 2
once and converts it to the engine's stored integer representation, then writes that value into operand 3
consecutive dwords. A nonpositive count performs no writes. This covers direct global/local spans and
integer-pointer destinations through the ordinary address resolver. The complete official and translated
Kamidori catalogs agree on 23 calls: CALCDMG (15), SETEN (4), SETSTUDIOCH (2), SELORN (1), and TOWN (1).
The inspected Himegari SYS4422 corpus does not observe `0x2d8`, so the finding remains SYS4433-scoped rather
than being promoted to a presumed base SYS4 contract. The handler is named/commented in the SYS4433 image.

The next static pair, opcodes `0x27` and `0x28`, belongs to the older patterned full-frame surface service,
not retained gameplay objects. SYS4433 handlers `op_0x27_reveal_surface_striped@0x41b070` and
`op_0x28_reveal_surface_staggered_strips@0x41b150` both accept
`(target_surface, interval_ms, divisions, selector)`, enter blocking VM state 9, arm the interval timer, and
call `pattern_surface_transition_begin@0x43ce80`. `0x27` maps selectors 0..4 to native modes 4..8;
`0x28` maps them to modes 8..12. The begin worker implements modes 4..11 and reports mode 12 as invalid.

`pattern_surface_transition_tick@0x43d6b0` proves the geometry. Modes 4/5 divide the active width and reveal
all vertical strips simultaneously from the left/right; modes 6/7 do the same across equal-height strips
from the top/bottom. Modes 8..11 use the same four orientations but initialize per-strip counters one timer
step apart. Thus simultaneous modes last `strip_thickness * interval`, while staggered modes last
`(division_count - 1 + strip_thickness) * interval`. ADV fast-forward or a nonpositive division count
publishes the full target immediately. Kamidori's sole `0x27` call is
`SC1050@0x8cf5 (2,40,64,3)`, a 360 ms bottom-to-top horizontal-strip reveal. Its 192 `0x28` calls across 47
scripts all use surface 2, 64 divisions, a 10 or 15 ms interval, and selectors 0..3, exercising all four
stagger directions. The port preserves the exact blocking/timing/endpoint contract and currently represents
the unavailable legacy strip compositor as a host-level crossfade approximation. Both handlers and both
workers are named/commented in the saved SYS4433 image.

After these implementations, strict natural execution reaches the stable `TITLE.BIN` loop, native-style
mouse/callback input selects Game Start, and `GAMESTART.BIN` enters `SC0000.BIN` through its first completed
ADV page wait with retained visuals and scene-local audio requests. A windowed 1024×576 capture also confirms
the rendered title. Kamidori's startup MPEG records use literal `.MPG` catalog names; that cross-game asset
contract and its runtime support are documented in `docs/asset-resolution-re.md`. That reference also records
the later finding that the installed English translation injects a `patch\\<basename>` base-asset overlay ahead
of normal root/archive resolution. K6's original capture used the official root/archive layer; the port now offers
that translation behavior explicitly through ordered overlay roots plus its separate BMP-as-AGF codec option.

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
`build/games/himegari/engine-dump/FUN_00413860.disasm.txt`) → **`build/games/himegari/op-handler-map.json`** (`{op → handler VA}`,
**420 overrides**). Regenerate: `py -3.11 -X utf8 tools/ghidra_handler_map.py
build/games/himegari/engine-dump/FUN_00413860.disasm.txt --check`. The `--check` diffs the derived handlers against the
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
the archive/file directory and its 0x90-byte metadata block into the remaining SYS4INI data, then calls
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

The settings trailer is a real per-game startup profile, not a miscellaneous string appendix. The serialized
inventories (Himegari's 36 pairs, Kamidori's 44) and physical offsets are canonical in `sys4-format-notes.md`; this
section owns their native behavior and current port relevance.

The startup order is:

1. `engine_settings_register_defaults@0x46be30` creates AGE's generic registry.
2. `engine_load_sys4ini_and_mount_append_catalogs@0x4099f0` reads the 300-byte file header into
   `ctx+0xa0bb8` (the global bank dimensions, see `sys4-format-notes.md`), parses the asset directory and
   its 0x90-byte metadata block, then passes the remaining length-delimited settings record to
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
  three. AGE.EXE has no later reader of the `set:NoSetMusic` copy; AGERC's 設定 dialog reads it to choose its
  ｻｳﾝﾄﾞ page ("Native menu bar").
- `WHEELKEYUP`/`WHEELKEYDOWN` each populate both `set:WheelKey*` and
  `message:WheelKey*OnTW`. A nonempty `VERREGPOS` also invokes
  `registration_read_install_display_version@0x46ae80`, which replaces `set:GameVersion` with the installer's
  `DisplayVersion` ("AGERC dialogs" under "Native menu bar").

The meaningful consumer catalog is:

| Setting group | Native effect (Himegari unless noted) | Classification |
|---|---|---|
| `CREATEOBJECT`, `DRAWMODE` | `CreateObject` is a mask of the renderer backends AGE creates, bit n for DrawMode n (registered default 1). Both games set 2, the retained D3D/object backend, and `DrawMode=1` selects its draw/text path. Startup replaces a mask that lacks DrawMode's bit with 3 (SYS4433 `sys4_read_startup_settings@0x415a40`). | Active renderer selection; the port reads both (`Sys4StartupSettings.CreateObject`/`.DrawMode`) |
| `SCREENX`, `SCREENY` | Replaces the generic `640x480` fallback with the `800x600` logical canvas before window/surface creation. The port parses and applies these dimensions to its logical canvas and default windowed client size; optional host boot arguments can vary only the physical client. | Active core profile; implemented |
| `FONT`, `ENABLEANTIFONT`, `ANTIFONTVERSION` | Seeds MS Mincho, enables `message:UseAntiFont`, selects the grayscale glyph-outline path, and records antialias version 3. | Active text profile |
| `DEPENDMOVIESOUND` | The mixer route of a movie whose flags force none: 0 mute, 1 music, 2 SFX, 3 voice, 4 movie, any other value silent ("Movie-to-surface opcode 0x236"). Himegari sets 1, Kamidori 2; Kamidori's LOGO and OP pass flag `0x20000`, which forces music. | Active media policy; port implemented (`MovieSoundRoute`, 2026-11-07) |
| `FULLSCREENBIT`, `ALWAYSBACKUPSURFACE` | These are read only inside the legacy `CreateObject & 1` DirectDraw branch of `engine_initialize_graphics_from_settings@0x406480`. Both games' `CreateObject=2` creates only the object backend, so their 32-bit/zero values do not drive the active renderer. | Valid compatibility settings, inert for both games |
| `AUTOFREETEX` (Kamidori) | A mask, registered default 0, that acts only under `CreateObject` bit 1: bit 1 releases every surface before a numbered load restores its records, bit 0 at op `0x9`'s root reload ("AutoFreeTex"). Kamidori sets 3. | Port implemented for the numbered load and op `0x9` (`VmOptions.AutoFreeTex`, 2026-11-07) |
| `OUTERFRAMEMODE` (Kamidori) | Registered default 1 in SYS4433 and 0 in SYS4422; Kamidori also sets 1. When nonzero, `engine_initialize_graphics_from_settings_sys4433@0x4063e0` gives the display op `0x25a`'s black background at device creation (`display_set_background_color_mode1@0x404c20`); `sys4_read_startup_settings` does the same for a fullscreen start when it is 1 (`ctx_set_display_background_color_mode1@0x405260`). `scene_context_init_reset_sys4433@0x40c2b0` and `engine_create_main_window@0x48ec20` copy it to `ctx+0x55eb8`, which nothing reads. | Inert: `SYSTEM4@0x1f3` sets the same black background with op `0x25a` before anything draws (2026-11-07) |
| `BLANKEXTENTMODE` (Kamidori) | Read only by SYS4433's aspect-mode glyph-string draws (`text_draw_glyph_string_plain@0x45e4e0` at `0x45e77f`/`0x45ea13`, `text_draw_glyph_string_effect@0x460250`, and the draws at `0x45ec60` and `0x460d50`). With 1, a blank (`0x20` or `0x8140`) advances by its text extent in the reference font (`0x404da0`); otherwise by the reference cell `+0x313e8`, halved for a single byte. The ADV builder (`0x45b710`) lays out its own records and never reads it. Kamidori sets 1. | Not modelled: the port's immediate surface text has none of aspect mode's rules (`docs/backlog.md`) |
| `USEPROPORTIONALFONT`, `VALIDFONTCHECKER` (Kamidori) | Filter SYS4433's installed-font list, which ops `0x2dc`-`0x2de` read ("Font family list"). With `set:UseProportionalFont` 0 (the registered default) the list drops IPA Pゴシック, IPA P明朝 and every family that is not fixed-pitch; Kamidori sets 1. `VALIDFONTCHECKER` repeats, one character per pair (Kamidori: 悪, ｱ, 洒), and a family lacking any of them is dropped. | Port implemented (`FontFamilyCatalog`, `WindowsGdiFontFamilySource`, 2026-11-07); the port's list equals native's 37 families, in order, on this machine |
| `RUBYSHIFTMODE` (Kamidori) | Seeds `message:RubyShiftMode`. SYS4433's `SYS4REG.INI` loader (`0x472530`) reads a value there over it but never writes one. Nonzero removes the live furigana's one-pixel drop and its render-mode-3 raise ("Furigana", SYS4433). Kamidori sets 1. | Port implemented (`RubyLayoutRules`, `Sys4RegIniStore.RubyShiftMode`, 2026-11-07) |
| `SCREENWARNING` | Imported as `set:ScreenWarning`; no reader beyond default registration/import was found in this EXE image. | Loaded, no observed consumer |
| `NOSETMUSIC` | Seeds the live music-route state as described above; the route starts enabled. AGERC's 設定 dialog shows ｻｳﾝﾄﾞ page 102, without `sound:Music`'s radio buttons, when it is nonzero (both games have 3). | Active audio initialization; the port picks the 設定 page the same way (`Sys4StartupSettings.NoSetMusic`) |
| all seven `MENU_*` keys | Imported as `set:Menu_*`. AGE.EXE does not read them. AGERC.DLL does: `agerc_prune_menu_by_settings@0x10002ca0` removes items from the native menu bar when it is attached (see "Native menu bar" below). It reads `Menu_MesWinA` twice and never reads `Menu_MesSpeed`. They are not Himegari script-menu state. `MENU_USEANTIFONT` prunes only the menu's 文字表示 entries; it does **not** disable the independently enabled `message:UseAntiFont` (corrected 2026-11-05). | Native menu-bar pruning |
| `ENABLEMEMFLIP` | Gates `op_0x6d`, which swaps VM-bank pointer pairs only when enabled. Himegari disables it and its corpus does not use that opcode. | Compatibility capability gate |
| `CLICKONUP` | Copied at `0x488080` into `g_dwClickOnUp@0x5c6030`. When nonzero, `age_main_window_proc` latches mouse clicks (`input_latch_mouse_click@0x485e20`) on `WM_LBUTTONUP`/`WM_RBUTTONUP` instead of the down messages. That latch is the trigger for ADV page advance, hotspot clicks, bound actions (right-click → `MENU.BIN`) and movie/transition skips; see *Driving native AGE* below. Himegari has 1. | Active input timing; port implemented (`Sys4StartupSettings.ClickOnUp`) |
| `CANCELMESSKIPONCLICK` | Himegari overrides the zero fallback with 2, enabling `adv_interpreter_tick`'s press/release click-cancel state machine for active fast-forward; value 2 also launches `CALLBACK_SETTING.BIN`. See "Click cancels Skip" below. | Active ADV input behavior; port implemented (`Sys4StartupSettings.CancelMessageSkipOnClick`) |
| `CONTROLDISIBLECURSOR` | The spelling is native. The "cursor" is the page-wait indicator. With value 0, `adv_input_service_poll` calls `adv_wait_indicator_stop@0x4051c0` (erase the indicator, clear run-state `0x40000000`) before it runs a bound-action or hover callback. Value 1 skips that call, so the indicator keeps running under the callback. | Both games set 1, which is the port's behavior (callbacks leave the indicator alone); value 0 is not modelled (2026-11-04) |
| `COEXISTMESSKIP` | Value 1 lets Auto and all-message Skip remain simultaneously enabled. With zero, `adv_toggle_auto_mode@0x406b70` clears Skip and `adv_toggle_skip_mode@0x406c20` clears Auto. Their only callers are IAGEService `+0x84`/`+0x88` (vtable `0x578034`, global object `0x5c5f10`), which the menu bar's ｵｰﾄﾒｯｾｰｼﾞ and ﾒｯｾｰｼﾞｽｷｯﾌﾟ call ("Native menu bar"); no opcode reaches them, and the script menus' ops `0x88`/`0x1b7` ignore the setting. | Modelled for the menu toggles (`VmOptions.CoexistMessageSkip`); inert on the script path (both shipped games have 1) |
| `EffectSkipOnClick` (`SYS4REG.INI` `[system]`) | The registered default is 0, which a new profile writes; the 設定 dialog's その他 page sets it ("Native menu bar"). When it is 1, the effect services (run-state bits `0x8`, `0x10`, `0x80` and `0x100`, and `0x400` unless service-flags bit 0 is set) poll the input latch; logical action 4 forces the effect to its terminal frame (delta `0x10000000`, or the force-complete worker for `0x400`). With 0 nothing polls during the effect, and the click stays latched for the next service ("Effect skipping and the input latch"). | Active visual-effect input policy; the port reads it live from `SYS4REG.INI` and matches native on both settings |
| `REDRAWTEXTONKEY` | Value 1 lets a wheel action at an ADV text wait step the text view (`FUN_00453520` ±1, serviced by `adv_text_view_service_tick@0x410d30`) and launch `CALLBACK_TEXT.BIN` under run-state `0x100000`. | Inert: both games set 0, and the port does not model value 1 (2026-11-04) |
| `WHEELKEYUP`, `WHEELKEYDOWN` | Rebinds the registry action bits from generic defaults 3/1 to 8/9. While an ADV text wait polls, a wheel turn raises that action instead of feeding op `0x10d`; every scene binds action 8 to History ("Mouse wheel at an ADV text wait"). The `message:WheelKey*OnTW` copies have no reader. | Active ADV input binding; port implemented (`Sys4StartupSettings.WheelKeyUpAction`, 2026-11-04) |
| `USEAPPDATAFOLDER`, `SAVEPATH` | Selects `%LOCALAPPDATA%\Eushully\姫狩りダンジョンマイスター\SAVE` as the native save root. | Active native path policy; the port redirects it through the shared profile-root policy |
| `REGFILEPATH` | Supplies the Eushully/product-relative directory for native `SYS4REG.INI`. With Himegari's `USEAPPDATAFOLDER=1`, AGE resolves `%LOCALAPPDATA%\Eushully\姫狩りダンジョンマイスター\SYS4REG.INI`. | Active native engine-options path; redirected with saves while retaining the native format |
| `SAVEVERSION` | Selects numbered-save layout 3.10 and enables the matching append-catalog persistence structures. | Active persistence ABI |
| `GAMEVERSION`, `VERREGPOS` | Seeds `set:GameVersion` with `1.00`. A nonempty `VERREGPOS` replaces it with the `DisplayVersion` of the GUID's InstallShield uninstall key, or `1.00.0000` when that value is missing. ﾊﾞｰｼﾞｮﾝ情報 shows the result. | Version dialog text; port implemented (2026-11-05; since engine shell M3, the options panel's 情報, `Main.OptionsPanel.cs`) |
| `REGKEY` | A nonzero value activates `registration_validate_key_file@0x46fc80`, which opens `SYS4RK.BIN`, validates its header/transformed payload and both CRC variants, and publishes the registration result. | Native registration/key validation |
| `COPYRIGHT`, `RCVERSION` | Both are imported. AGERC's ﾊﾞｰｼﾞｮﾝ情報 shows `set:Copyright`; no consumer of `RCVERSION` was found. | Version dialog text; port implemented for `COPYRIGHT` |

This is intentionally a native-behavior catalog, not a mandate to reproduce every Win32-era switch.
`Sys4AssetCatalog` now parses the ordered trailer once, retaining unknown pairs for diagnostics, and projects
`SCREENX`/`SCREENY` into one validated engine-owned logical canvas. Godot uses that value for its content-scale
base, default windowed client, backbuffer/compositor bounds, primary surface, movie/layout fallbacks, and input
coordinates. `--window-width`/`--window-height` may vary that client independently while the logical canvas
and all AGE coordinates remain unchanged. AGE's independent `640x480` defaults apply for missing or invalid
dimensions. The port still preserves requested Mincho/Gothic faces and movie/audio behavior through their
existing paths. Persistence now reads and validates the selected profile's packed `SAVEVERSION` from this
registry; the other keys have not yet been migrated to semantic consumers.

The port now applies click-cancel (`CANCELMESSKIPONCLICK`) and the wheel action ids (`WHEELKEYUP`/`WHEELKEYDOWN`)
from the trailer instead of AGE's compiled defaults. Auto/Skip coexistence, the wait-indicator policy
(`CONTROLDISIBLECURSOR`) and the text-redraw policy (`REDRAWTEXTONKEY`) are inert or already matched for both games
(rows above). Kamidori's six extra keys (eight pairs) are applied or shown inert (rows above, 2026-11-07). The port
deliberately redirects each native profile beneath
Godot `user://games/<profile-id>/`: save payloads remain under its `SAVE` child, while engine options retain
AGE's native `SYS4REG.INI` filename and format at the profile root.
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
  (`CreateFileA` on `record.name` → the mod/patch hook point). Any failed loose `CreateFileA` result—not only
  file-not-found—takes the archive branch; a successfully opened but malformed loose payload remains authoritative.
  Both loose and archive opens use read access, read sharing, `OPEN_EXISTING`, and the sequential-scan hint. The
  archive branch opens
  `[record.arc_id*0x100 + FileDB+0x410]`, `SetFilePointer` to `record.offset`, size = `record.size`.
  High-byte-tagged ids select `[FileDB+0x3028 + signed_selector*4]` and use the low 24 bits as the
  selected AAI record. The base corpus has no explicit high-byte call-script operand; INIT2 op `0x143`
  supplies mounted record-zero ids dynamically.
- **`asset_register_open_handle_range@0x44edf0`** registers the selected handle in FileDB's 256-slot stream
  table, retaining its current file position and the loose-file length or catalog-record length supplied by the
  opener. A full table closes the new handle and throws. Base out-of-range ids, missing signed append selectors,
  append out-of-range ids, and entries for which neither loose nor archive open succeeds likewise throw rather
  than returning an unresolved sentinel.

**So `call-script <id>` = a direct RAW index into the SYS4INI global file table** — the same table
`parse_sys4ini.py` reads, but indexed *without* skipping `@` placeholders (13208 records, 2
placeholders). There is **no separate on-disk id→code registry**; SYS4INI *is* the registry, and we
already had it. **Statically confirmed:** all **297/297** distinct corpus `call-script` ids resolve to
a `.BIN` script with a semantically-exact name (`0x1ab→ADDITEM`, `0x2ae7→MES`, `0x143→BUNKI`,
`0x329d→CALCREVISE`, `0x2add→CALCBTPARAM`), 0 out-of-range, 0 pack-branch. Tooling:
`parse_sys4ini.py` emits `build/games/himegari/callscript-names.json` (id→name); `sys4load` annotates
`call-script 0x1ab =ADDITEM.BIN`; the whole `build/games/himegari/disasm/*.asm` call graph now reads by name. See
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
in one VM. Godot direct-scene runs (`--scene SC0000`) are hosted by SYSTEM4 as well, so Godot has no `--boot`
(2026-11-05; "Native ADV hosting and Himegari's developer entry").

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
queues, text/render buffers, and the surface records and movies of the 1000 slots (their textures survive unless
`AutoFreeTex` asks; "AutoFreeTex" below).

**Op `0x9` zeroes the global banks (2026-11-07).** Between disposing the frames and `scene_context_init_reset`, the
handler clears every global bank, once per bank set (two when `set:EnableMemFlip` is on):

- every integer global (`bank_size_int_globals + 1` of them) becomes `EngineCtx.anti_tamper_b`, the encoded zero;
- the float bank is zeroed and every global string erased;
- the integer-pointer, string-pointer and local-pointer-scratch banks are zeroed.

SYS4433's `op_0x9_reset_scene_and_reload_root_sys4433@0x423b50` does the same. Engine configuration and the shared
profile are untouched; SYSTEM4 re-authors what it needs when it runs again. A Kamidori game over shows the effect.
FIELD sets `G[0x428c]=1`, SC5300 ends in op `0x9`, and `INIT2@0x274` draws the map objects and unit sprites
(DRAWOBJ/DRAWCHALL) whenever `G[0x428c]` is nonzero. A Frida read of native at TITLE after a turn-limit game over
found `G[0x428c]` and SC5300's `G[0x19c1]`/`G[0xfd435]` back at zero, and the title clean.

The port mirrors that boundary: an `0x9` request propagates through every nested `call-script` frame without
executing any caller continuation, zeroes the global banks, clears scene-owned VM/Godot presentation and input
state, cancels deferred SFX starts without unloading or stopping active channels/BGM, preserves process-owned
configuration/caches, then begins raw script zero at offset zero in the same VM session. The 終了 dialog's
タイトルに戻る runs the same reset.
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

**Close policy: forced and unforced WM_CLOSE (2026-11-04).** The catch continuation at `0x412961` sends
`WM_CLOSE` with `wParam` 1 to the main window (`ctx+0x54fe8`), so `0x1` is a *forced* close. The window
procedure `age_main_window_proc@0x486320` closes at once on `wParam == 1`. Any other `WM_CLOSE` is
*unforced* and asks `wm_close_confirm_exit_or_title@0x405740` first. Three things post an unforced close:

- the window's close button, or the taskbar's Close;
- ESC (`WM_KEYDOWN 0x1b` posts `WM_CLOSE(0)` and still forwards the key to the engine);
- menu command `0x9c41`, the menu bar's ｹﾞｰﾑ>終了 (labelled ESC; see "Native menu bar" below).

Two things post the forced form:

- `WM_USER+1`, unless its hook stub returns true;
- menu command `0x9c6b`. Only AGERC's debug menu 124 holds it (ﾌﾟﾛｸﾞﾗﾑの終了, labelled F12). No code loads that
  menu, since `LoadMenuA`'s only callers pass 110. `WM_KEYDOWN` tests only ESC, and neither image posts the id. This
  build cannot send it (2026-11-05).

The decision function returns 1 at once, before it dims or asks, when run-state bit `0x1000000` is set. The bit
means the game is minimized or suspended:

- `WM_SYSCOMMAND SC_MINIMIZE` sets it, windowed or with `display:VirtualFullScreen`, and removes `SC_MAXIMIZE` from
  the system menu. `SC_RESTORE` clears it.
- In fullscreen, `display_suspend_on_deactivate_set_minimized_flag@0x45f9b0` sets it on `WM_ACTIVATEAPP(0)` and on
  message `0x1401`.

A native drive confirmed it (`tools/drive/scenarios/himegari-minimized-close.steps`):

- A close while minimized exits with code 0, shows no dialog and writes `SAVE.DAT`.
- After a restore the dialog asks again (2026-11-05).

Otherwise:

1. It dims the presented frame with a black fill at alpha `0x80`.
2. It calls `g_pfnAgercHostCallback(3, hwnd, 0)`. In AGERC.DLL that is `agerc_dispatch_host_command` case 3,
   which runs `DialogBoxParamA` on dialog resource 3 with `agerc_exit_dialog_proc@0x10002040`.
   - The dialog is titled 終了 and reads 次の方法で終了しますか？.
   - It has radio `0x3ea` プログラムを終了する(&E), checked at init, and radio `0x3eb` タイトルに戻る(&T).
   - Its buttons are OK and ｷｬﾝｾﾙ.
   - Its icon is `IDI_ASTERISK`.
   - `agerc_center_dialog_on_parent` centres it on AGE's *outer* window, caption and menu bar included. With the
     800×600 client (outer frame 3/46/3/3 px) its window rect starts at client (234,194).
3. It acts on the answer:

| Answer | Native action |
|---|---|
| 1 (exit) | The screen fills black. The window procedure saves `SAVE.DAT` (unless `set:NoSaveDat`), tears down and destroys the window. |
| 2 (title) | It undims, presents, sleeps 250 ms and calls `op_0x9_reset_scene_and_reload_root` from inside the window procedure. That reset clears the initial-root-run flag, so SYSTEM4's `0x130` skips LOGO/OP. |
| 0 (cancel) | It undims and play resumes. |

The dialog is modal, so native's main loop is frozen while it is open.

A native drive confirmed this (`tools/drive/scenarios/himegari-exit-dialog.steps`):

- Cancel on the title resumes.
- タイトルに戻る from SC0000 page 1 is back at TITLE 0.42 s later.
- TITLE's fifth action (`0x1`) then exits with code 0 and no dialog.

**Port.** The port has the same boundary:

- **Op `0x1`:** the VM ends with `exit-request` and the frontend quits at once (`Main.QuitAfterExitRequest`). As in
  native, it writes `SAVE.DAT` unless `NoSaveDat` is set.
- **Unforced closes:** the window's close request and ESC go to `Main.RequestUnforcedClose`. ESC is also still
  forwarded to the VM. That method:
  1. holds the VM thread at its next frame yield;
  2. dims to 128/255;
  3. freezes the presented frame (see "Port appearance" below);
  4. opens `ExitConfirmationDialog`, the shell's own 終了 dialog (since 2026-11-09, engine shell M2): dialog 3's
     layout in client pixels, with its texts from the shell's string table (`ShellExitDialog`,
     `engine/Ome.Engine.Frontend/Shell`) in the player's language, not read from the DLL. Every game root gets it,
     whatever its AGERC.DLL holds. Natively the templates differ only in text: the translated Kamidori install has an
     English `agerc.dll` (Quit, `Exit(&E)`, `Return to title(&T)`, Cancel) laid out as Himegari's Japanese one (driven
     2026-11-07), and AGERC's code strings (the 設定 page formats, the folder prompt) stay Japanese in both DLLs.
- **Minimized:** `RequestUnforcedClose` first checks the window mode. A minimized window quits at once with exit
  code 0, and the shutdown flush writes `SAVE.DAT`, as native does under run-state `0x1000000`. The port reads
  the window's state instead of keeping the bit.
- **タイトルに戻る:** after 250 ms, `VirtualMachine.RequestRootReload` runs. It wakes any blocking host wait through
  `ILifecycleHost.InterruptForRootReload`. The current frame stops at its next opcode boundary (a hotspot callback
  unwinds too), and op `0x9`'s reset and root reload follow.

The port's drive of the same scenario passes. Its return to TITLE takes 0.28 s, with no logos. The minimized-close
scenario passes on the port too.

The port's drawn menu bar sends `0x9c41` from ｹﾞｰﾑ>終了 as native's does: the asking close (see "Native menu bar" below).

**Port appearance (2026-11-04).** Both modal dialogs (this one and the interpreter error box) are drawn to look like
AGE's on Windows 10. The reference is PrintWindow captures of the native dialogs with their layout read through
`GetWindowRect`, `GetClientRect`, `WM_GETFONT` and GDI text metrics.

- **Frame.** AGE has no common-controls manifest, so its dialogs are classic Win32 controls inside a Windows 10
  DWM frame. The frame is a 1 px `#191919` border and a white 25 px caption with a 10 px ✕; the client is
  `COLOR_BTNFACE`. The window rect also has a 2 px invisible border left, right and bottom.
- **Controls.**
  - Push buttons have a two-pixel bevel and a 1 px `#646464` frame on the default button. Radios use GDI's
    12×12 glyph. Focus rectangles are dotted.
  - `godot/Win32Dialog.cs` and its control classes reproduce these pixel for pixel: the 終了 dialog's frame,
    bevels, radios, focus rectangles and ＭＳ Ｐゴシック text match the capture exactly.
  - The icons (`IDI_ASTERISK`, `IDI_HAND`) are vector look-alikes.
- **Fonts.** The 終了 dialog uses ＭＳ Ｐゴシック 12 px from its bitmap strike (GDI tmAscent 10). Its extents are the
  sums of the character advances: Godot's shaped string size can differ by a pixel.
  - The message box and the caption use Segoe UI −11/−12. GDI links their Japanese to Meiryo UI at 13 px
    (extents match), and the message box's line pitch is 13 px.
  - `SystemFont` loads a collection's first face (MS Gothic, not MS PGothic), so faces are picked from the TTC by
    name. Without the Windows fonts, the dialogs fall back to portable Japanese fonts.
- **Freeze.** Native's modal loops freeze its main loop, so the window keeps the last presented frame.
  `Main.FreezePresentedFrame` shows a snapshot of that frame under the dim until the dialog closes. タイトルに戻る
  keeps it through the 250 ms sleep.
- **Placement.** The 終了 dialog sits where native puts it. Windows centres the message box on the monitor, but the
  port centres it on the game.
- **Known differences.**
  - Grey antialiasing stands in for ClearType.
  - Godot's unhinted Segoe UI advances are about 1% narrower than GDI's, so the error box is up to 10 px narrower.
  - The error box shows Japanese Windows' button labels (native shows the Windows UI language's, `&Retry` here).

TITLE happens to contain a developer menu immediately after its `0x1`, including a `call-script` to
`DEBUG.BIN`; that code is unreachable in the native flow because the handler never returns. The port's
former unknown-op fallback did expose that menu when selecting TITLE's fifth action, providing a useful
visual confirmation of the static mapping but not a legitimate retail route. Opcode `0x1` now propagates a
process-exit request through hotspot callbacks and nested script frames and ends the VM session without
executing the following bytecode. An explicit Godot `--native-debug-menu` diagnostic can deliberately
restore the old fall-through for developer archaeology; it maps to `VmOptions.IgnoreExitRequests`, defaults
off, and is not part of the native compatibility path. Because it changes the opcode globally, it also
suppresses SYSTEM4's invalid-execution-mode exit site during that run. The 終了 dialog's タイトルに戻る now exercises
`SC0000 -> 0x9 reset -> SYSTEM4 -> TITLE` end to end on both targets (close policy above).

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
with the running reference clock. A read-only `IMediaSample::GetTime` trace on 2026-08-11 replaced the earlier
visual inference with direct evidence: native LOGO's video samples reach the inherited DirectShow base renderer
at exactly 0, 33.3667, 66.7333 ms and continue at that cadence. A follow-up hook installed while `quartz.dll`
loaded, before graph `Run`, proved that native audio also begins at graph time 0 with contiguous 26.1224 ms
samples; its first non-silent PCM block begins at 2,351 ms. Native video-content fingerprints show real changing
frames throughout 0--600 ms and match FFmpeg-decoded fingerprints at the same timestamps from 1,501 ms onward.
AGE therefore preserves the common program timeline; it does not drop the opening video interval.

The earlier installed FFmpeg probe proved that its returned frames were consecutive and opaque, but not that
decode began at the first packet: its explicit seek had already skipped to PTS 600/601 ms. After the corrected
common-origin rewind, LOGO and OP begin at PTS 0; their first 20 frames remain consecutive 33/34 ms samples and
change within that window. Every decoded alpha byte is 255. MPEG-1 has no alpha plane. Native
`movie_texture_renderer_set_media_type@0x463750` accepts RGB24/RGB32, and the RGB24 copy
path promotes pixels to alpha `0xff` before comparing the complete packed ARGB value with its renderer key.
Opcode `0x20f` attaches that renderer to the existing D3D target and never reloads the retained surface or
assigns it an RGB color key. The former port did both `Gfx.SetSurface(..., colorKey: 0)` and RGB-only keying,
which incorrectly made exact-black movie texels transparent and explained grey-background MPEG speckles.

The port's error was below managed pacing. `rewind_for_decode` sought the video demuxer to
`AVStream.start_time` (600/601 ms for LOGO/OP), skipping valid earlier packets/decoded frames. Managed pacing then
treated the first surviving frame as presentation time zero while audio correctly retained the common origin.
For LOGO this decoded only 200 frames and moved picture about 600 ms ahead of sound. The native shim now rewinds
video to the format's common `timeline_origin_ms`; LOGO produces 218 consecutive frames beginning at PTS 0. The
asset-level cue confirms the correction: audio signal begins around 2,380 ms and the first `E` appears at video
PTS 2,502 ms, preserving the authored roughly 122 ms sound lead instead of the old roughly 478 ms lag.

**Audio-bearing graph completion (2026-08-11):** DirectShow remains active through the common graph endpoint.
The port had completed once final PCM was *submitted* to Godot, not when it became audible, and LOGO could still
have roughly 375 ms queued. Audio-bearing playback holds the terminal image until the sound-hardware clock reaches
the full source endpoint; audio decode EOF and PCM submission remain additional requirements. With the recovered
opening video frames, LOGO's video and audio now naturally share the approximately 7,288 ms graph lifetime. A live
Windows F6 series showed zero generator underruns and a stable output-latency-compensated clock, ruling out decode
starvation and startup buffer loss.

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
- geometry-only/unbound object → slot 0 on SYS4422, whose `gfx_object_init_default` zeroes `obj+4` (page-58
  correction below), and -1 on SYS4433 (next subsection);
- draw-bound object → `0x215` returns the live source slot from `obj+4`.

The earlier “op `0x215` reads a registry populated only by `0x1a2`” conclusion was wrong: it conflated
the retained-object `std::map` with `0x1a2`'s open-addressing shared-profile integer table. The useful part of
the earlier fix remains: `GetOrCreate` must not fabricate a slot. A fresh object keeps the default slot until
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

#### SYS4433 reads a draw-unbound object as absent (2026-11-09)

**Native.** Kamidori's op `0x215` handler (4.33G `0x42af40`) passes owner `ctx+0x4a884` to worker `0x486120`
(`gfx_object_query_bound_source_slot`). After the map lookup the worker tests bit 0 of the record's first dword and
returns `obj+4` only when it is set; otherwise it returns -1, as for an absent handle. Bit 0 is the draw-bound bit
that `draw-texture` sets (the port's `GfxObject.Visible`). 4.33F has the same test at the same address. SYS4422's
`gfx_object_query_source_slot@0x47f280` has no such test.

**Where it shows.** SC1250 (Kamidori's tavern event) shows EV021AA twice. Between them `label_fcb3` (called at
`0x97a1`) erases layer 1's object `0xcb2a` (`G[0xfd43c]`) with `gfx-elem-erase fd43c 0xa` at `0xfe93` and releases
surfaces 5/44/52. Then `0x98aa` runs `gfx-blit-color (fd43c) 0 0x37a 0 -1`. Op `0x202`'s worker (`0x485740`) calls
`gfx_object_get_or_create` (`0x484b40`), which inserts an unbound default record, `obj+4 = 0`. The next CG call
reaches `label_f63f`, which queries the layer object with op `0x215`:

- `< 0` → the fresh-object branch (`0xf3ec`): a new object anchored at (512,576) that fades in. Native takes it.
- `>= 0` → the crossfade branch (`0xf681`): it reads the old texture's size with op `0x208` on the returned slot and
  positions the new CG from it. The port took it with slot 0. Surface 0 was released, so op `0x208` (worker
  `0x47ba80`, the size of the surface at manager `+0xa5a8[slot]`, 0,0 when empty) gave 0x0, and EV021AA was drawn
  at (-512,-576): a black screen through pages 13-16.

**Port.** `GfxState.QuerySlot(handle, unboundReadsAbsent)` returns -1 for an object whose `Visible` is clear when
asked as SYS4433 does. `VirtualMachine` asks so on SYS4433 (`EngineRevisions.IsSys4433`). With it the SC1250 route's
states 37-72 match native and the shots differ only in the animated wait indicator. Tests:
`GfxStateTests.QueryReadsAnUnboundObjectAsAbsentWhenAskedAsSys4433Does`,
`GfxCommandBufferTests.QueryOfAnObjectAColorFadeCreated_FollowsTheEngineRevision`. The 4.33G workers are renamed and
commented in Ghidra (`/kamidori/4.33G`).

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

> **Correction (2026-11-04).** The engine does not set the gate; it is `adv_developer_entry_mode`.
> - The scan above missed the one write of 1, at TITLE `0x5fa` in TITLE's developer menu, which retail never
>   reaches. In retail the gate stays 0.
> - The loop below is a developer label picker, not the source of the slot table. SYSTEM4 fills the table before it
>   calls any scene.
> - A native New Game trace (`tools/frida/trace_engine_ops.py`) passes `0x450 → 0x457 → 0x45e → label_71b`.
>
> The lifecycle inferred next ("enter `aba5c=1` → intro loop fills slots") describes no native path. See "Native
> ADV hosting and Himegari's developer entry" below.

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

**The loop iterator `op 0x140` is a native AGERC service call.** Handler =
**`0x4299c0`** (dispatch `ctx[0x9b74c]=0x4299c0`; created+typed `EngineCtx*`+annotated; Kelebek `u0041F9C0` is
VA-drift). It records the generic 9-dword instruction length, copies operand-2/3 strings (`"LABEL"`, `"J"`) + operand-4 int, calls
**`(*g_pfnAgercHostCallback)(8, ctx[0x54fe8], &{str,str,int})`**, and writes the returned PC-like value back
to operand 1 (SC0000: `G[0x6be]`). The callback has no static writer in the unpacked AGE image because the
loaded resource DLL supplies it. A read-only live probe now resolves it to `AGERC.DLL+0x1050`; the imported
`/support/AGERC.DLL` image names that thunk `agerc_host_callback_export` and its worker
`agerc_dispatch_host_command@0x100025f0`. Command 8 opens AGERC dialog resource 8. The same callback's
command 3 services `FUN_00405740` screen transitions. This closes the callback's ownership and makes a
future `0x140` dive statically possible, but does not by itself identify command 8's LABEL iterator result.

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
circle (scene-phase cleanup). The real intro-transition *timing/pacing* inside AGERC's command-8 service
remains intentionally unmodeled; the port approximates it host-side, as it does other native transitions.
**Host model implemented (2026-07-09).** `VirtualMachine` recognizes only the ADV `"LABEL","J"` form
(138 corpus scripts; all have the same terminal-check shape), synthesizes `G[0xaba5c]=1` on top-level
scene entry, forces one setup-body pass even if `G[0x6be]` is stale, then returns the terminal immediate
discovered from the following `mov`/`eq` pair. Thus no SC0000 offset is hardcoded. Op `0x7b` retains the
saved handler PCs as frame metadata; op `0x7c` is a host-scheduler marker because the host already owns
service-boundary suspension and retained presentation. `TITLE.BIN`'s unrelated `"BIN","SC????.BIN"` service remains
stubbed. The real video-service timing remains intentionally unmodeled.

**The terminal is the scene loop head (2026-10-07).** The terminal immediate is the code offset of the scene loop head:
the `call` (SCJUMP dispatch) and `exit` pair that the entry gate jumps over. That holds in all 144 Himegari and 269
Kamidori scenes. The host model
now finds the loop head itself, as the nearest `call` followed by `exit` before the yield, and returns that. The
script's immediate must name the same instruction; otherwise the VM halts with `coroutine-yield-terminal@…`. Echoing
the immediate would hide a script whose code moved without relocating it, which native AGE would not survive. The Open
Maid Script decoder relabels this immediate as a code position (`G[0x6c3] = &label`, registry
`value = "code-address"`), so recompiled scenes keep it correct.

**Comments in the pattern (2026-11-01).** A fragment (OMS spec §10) may put code anywhere around the loop. The gate-7
probe puts inert comments (op 0x1a7, whose native handler only advances) before the loop head, between its `call` and
`exit`, and between the yield, the `mov` and the `eq`. A transfer to the head then lands on the first comment in front
of it, so the relocated immediate names that comment. The host model skips comments when it matches the pattern. It
accepts an immediate that names the head itself or a run of comments leading into it, and returns that immediate. Any
other instruction between the named one and the head still halts with `coroutine-yield-terminal@…`.

The same relocation work exposed a port bug in the setup's `call *G[0x6be]`. The port's `call` took the operand's
cell number (`0x6be`) as the target instead of reading the cell. That was a silent no-op wherever `0x6be` is not an
instruction offset, and a jump into unrelated code wherever it is. `call *v` now calls the offset the variable holds.
A value that is no instruction's offset calls nothing and pushes no return address. The first labeled-yield visit
returns -1, so the setup's indirect call stays the no-op the validated model relied on.

**AGERC command 8 is a developer jump picker (2026-10-07).** `agerc_label_dialog_proc@0x10002270` (AGERC.DLL, created
and annotated in `/support/AGERC.DLL`) runs modal dialog resource 8. It does not compute a PC.

- **`"LABEL"` (exact string compare):** label mode. The second string pre-fills a prefix filter (`"J"`), and the list
  holds the script's labels from the engine service (`IAGEService` vtable `+0x60`/`+0x64`). OK resolves the chosen
  name through `+0x5c` and ends the dialog with that value; Cancel ends it with 0.
- **Any other name:** entry mode. The list holds service entries (`+0x94`/`+0x98`) whose last three characters equal
  the name, filtered by the second string with `?` wildcards; OK resolves through `+0x90`. Kamidori TITLE's
  `"BIN","SC????.BIN"` is this scene-script picker.

So a scene's labeled-yield loop is the developer path "start a scene, then pick a label to jump to"; the returned PC
is what `call *G[0x6be]` runs. It only runs when the scene-entry gate is set. Kamidori TITLE sets
`G[0x1372d9] = 1` only in case 0 of its developer menu (アドベンチャー, 全体マップ, 迷宮, 戦闘, …), right before the
script picker; retail play never sets it, so scenes skip the loop, natively and in the port. The port's Himegari
gate synthesis is the same developer entry, used for direct single-scene launches.

The Kamidori English fan translation's 32 replacement append scenes (`$1$SC2600` … `$3$SC6920`) dropped the first
character of every string the translators did not rewrite: `"LABEL","J"` became `"ABEL",""`, and choice text such
as `"シーン回想終了"` became `"ーン回想終了"`. In retail play that only shows as broken choice text. Through the
developer entry, native AGE would open the entry picker (no `ABEL` entries), while the port stubs the call and loops
until its step limit. Neither path is reachable without the developer menu.

**Native ADV hosting and Himegari's developer entry (static and Frida-traced, 2026-11-04).** This replaces the
2026-07-09 reading that the engine sets `G[0xaba5c]` (`adv_developer_entry_mode`) on scene entry.

- **SYSTEM4 hosts every ADV scene.** Its `system_flow_request==1` branch (`0x2b7`) calls the scene with
  `call-script G[0x699]` at `0x477`. The call sits between the "advset" block (from `0x3d6`) and the "advend" block
  (from `0x47f`; its `0x93` is at `0x483`). While `G[0x3275]` is 0, the branch first runs the ADV setup:
  - `call label_5bf` at `0x33e` fills `adv_layer_surface_slots` (`G[0x3239]`) with 4/43/51 … 11/50/58;
  - `0x341` sets `adv_chrome_enabled`;
  - the font and text state follow.

  So the slot table is filled before any scene runs. The SC0000 page-3 saves of native and the port hold the same
  table.
- **The scene loop is a developer copy of that wrapper.** Each scene's `0x462..0x711` loop repeats:
  1. advset;
  2. `call G[0x6be]`, an indirect call to the label that AGERC command 8 returned;
  3. advend, with its `0x93` at `0x622`.

  The loop's `label_125bd` is a copy of `label_5bf`.
- **Only TITLE's developer menu sets the gate.** The menu starts at TITLE `0x394`, right after the main menu's
  forced quit (`throw-exit-request@0x393`). No label points into it, so retail never runs it (op `0x1` record).
  - Case 0, ＡＤＶパート (`0x5f5`), writes `G[0]=1`, `G[0xaba5c]=1` and `G[0x62ccf]=0`. It then picks a scene with
    op `0x140` `"BIN","SC????.BIN"`.
  - Cases 1 and 2, ＡＤＶデバッグ and ＡＤＶデバッグ（ＰＧ用）, write -1 and name DEBUGADV and DEBUGADV2.
  - Every other script write is 0, and AGE.EXE holds no `0xaba5c` immediate.

  SYSTEM4 reads the gate twice. Only at 0 does it mark the SCJUMP decision seen (`0x2c6`) and take `G[0x699]` from
  `scjump_scene_script_resource_ids` (`0x44a`). At 1 or -1 it runs TITLE's choice. In retail the gate is 0, and
  native New Game passes `0x450 → 0x457 → 0x45e → label_71b` (`tools/frida/trace_engine_ops.py`).
- **A save load resumes before the gate.** A load from TITLE's Data Load re-enters SC0000 from its start and
  re-registers the control strip at `0x7e`. Then `continue-save-load-stack-restore` at `0x447` jumps to the saved
  position (`0x877` for a page-3 save), so the gate at `0x450` is never reached (Frida-traced).
- **No retail text wait follows an orphan `0x93`.** Native `0x93` empties the frame's registry
  (`input_hotspot_reset`). A linear scan finds that 1,623 of the corpus's 1,766 `0x93` sites re-register before
  any text wait. SC0000's sites show the pattern: `0x93`, a call into History, Menu, HIDEWIN or INPUTNAME, then
  `call label_8c`. The other 143 are advend blocks:
  - one in each of the five hosts (SYSTEM4, CAMP, FIELD, FORT, HMODE), whose own frames never wait for text or
    register hotspots;
  - each scene's developer-loop copy at `0x622`.

  On the developer path, native therefore reaches the content after the loop with an empty registry.
- **Port routes (2026-11-05).**
  - The natural boot matches native: SYSTEM4 hosts SC0000 with the gate at 0.
  - The F4 launcher takes TITLE's case 1/2 route, with the gate at -1 and SYSTEM4 hosting (`tools-reference.md`).
    Unlike case 1/2, it names the chosen scene itself rather than DEBUGADV.
  - Godot direct-scene runs (`--scene SC0000`) enter the same way (`VirtualMachine.HostScene`).
    - SYSTEM4 starts as after a return to title (`get-initial-root-run` answers 0, as after op `0x9`), so it skips
      LOGO and OP and runs LOADCONFIG/INITCONFIG, INIT2 and INIT.
    - TITLE returns before its first instruction with the launcher's writes and the scene's packed id.
    - SYSTEM4 then runs its ADV setup and hosts the scene, and the run ends when the scene returns.
    - Because the gate is -1, the scene skips its loop (`0x450`), and nothing cancels the control strip that
      `0x7e` registers.
    - The profile's direct-scene stand-ins are gone: the forced gate and loop pass, the SYSTEM4 layout, binding
      and inline-array replays, the inherited SO000/SO001 surfaces, the wait indicator, and the
      `adv_chrome_enabled` seed.
    - TITLE's own setup (title textures, BGM `0x17`, its input callbacks) does not run, so a scene with no
      `play-bgm` of its own starts silent, where the F4 launcher would still play the title music.
    - Driven 2026-11-05: `--scene SC0000` reaches page 1 at `SYSTEM4@0x477 > SC0000@0x83f`, the frames native New
      Game shows. Hovering the History control shows its tooltip. Against the port's own New Game, the page-3 shot
      is pixel-identical and page 1 differs only in the wait indicator's animation frame. Against native, both
      port runs differ only in the usual glyph-edge antialiasing and that indicator.
  - The CLI's headless scene tools (`trace`, `gfx`, `play`, `sweep`, `oms-rebuild`) run the scene as the root
    with zeroed banks. The gate stays 0, so they take the retail content path. Only `--boot` stands in for
    SYSTEM4's state prefix there.
  - The port's op `0x93` empties the table as `input_hotspot_reset@0x404130` does (record count `+0x408` = 0).
    The retained-template re-arm that served the old forced loop pass is gone.

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
| `0x1fa` | `0x4224a0` | 3 | set | 1 | release **surface slot** op1: delete its movie object `ctx+0x52bd4[op1]` (vtbl free), then its texture (`retained_gfx_release_surface@0x474e40(op1)`) |
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
`ctx+0x52bd4` (per-slot `CMovieToTexture` objects), plus the `0x408` registry.

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
after op `0x1fa` released that surface. Native op `0x1fa` releases the slot's texture, so a subsequent
op `0x208` size query of the released slot yields no surface dimensions. Clearing the host slot resource and
dimensions on release makes the script's existing-object geometry path compute base `(0,-500)`; after the
retained `(0,550)` translation, BG004D lands at `(0,50)`. A synchronized run at the exact wait confirms the
background and character layers together. This path also established that op `0x1ff` directly replaces the
current translation matrix at `obj+0x16c`; it is now modeled rather than stored in an inert side vector.

**SYSTEM4 P102 / SC0000 `wait@0x85de` current-translation correction (2026-08-16).** The missing
`BG001A.AGF` was not an asset or compositor failure. The retained log showed the new background animate
from translation zero to `+1200`, then settle at `+1150`; with base Y `-500`, it remained below the
600-pixel viewport. Native `0x228` had been misidentified as a query of the delayed target matrix.
`gfx_object_query_translation_current@0x47cdd0` copies the complete object record to stack local
`-0x2e0` and passes `local_174`—exactly `+0x16c` within that copy—to
`matrix4_decompose_affine@0x48d7c8`. It therefore queries the **current** translation matrix, not the
target at `obj+0x1ac` and not base position V24. SC0000 makes the consequence explicit: `0x1ff@0x84e8`
sets current translation to zero, `0x228@0x84fa` reads that zero, and `0x220@0x8521` adds the intended
`+600`; the following leg reads the completed current `+600` and subtracts 50 to settle at `+550`
(screen Y 50). The C# VM now queries `TranslationCurrent`; its focused handle-reuse regression preserves a
previous `+600` target, resets current to zero, and verifies the next SC0000 leg targets exactly `+600`.

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
  delay, linearly interpolates current→target for its duration, then commits the target, clears its own timing and
  resets the target matrix to identity (see "Finished channels reset their targets", 2026-11-08). Neither third
  component is opacity.

**Exact composition and 2D reduction (live-validated 2026-07-10).** The one-shot consumer starts from identity and
right-multiplies `T(-V18) → scale-current → rotation-current → translation-current → T(+V18)`;
`matrix4_multiply` at `0x4ee2a4` computes `out = left * right`. AGE uses row vectors. With no
rotation/perspective, the screen projection is therefore exactly
`V18.xy + (point.xy - V18.xy) * scale.xy + translation.xy`. The captured SC0000 handle `0xcbc0`
has base `(0,600)`, anchor `(400,1000)`, and final scale `(5,5)`; native matrix translation
terms are `(-1600,-4000)`, projecting the base point to `(-1600,-1000)`. The port's focused
projection test and transform-aware gfx log reproduce those values.

`gfx_object_composite` then right-multiplies `gfx_object_anim_interpolate`'s separately anchored product,
which contains op `0x233`'s cyclic scale, `0x234`'s cyclic rotation, and `0x235`'s cyclic translation.
Adjacent anchors cancel and the full order is
`T(-V18) * scale * one-shot-rotation * translation * cyclic-scale * cyclic-rotation * cyclic-translation * T(+V18)`.
Thus cyclic rotation also rotates the one-shot translation vector, while cyclic translation is applied to
the already-rotated coordinate result. The cyclic angle is integer degrees
`floor(((frameTime-start) % period) * 360 / period)`; it wraps to zero without ping-pong. Positive Z produces
`m01=+sin, m10=-sin`, clockwise on the Y-down screen.

Native matrix oracle: handle `0xcb8e`, anchor `(700,600)`, scale current `0.9`, op `0x21f` target axis
`(0,0,1)`/30° after 500 ms for 390 ms, sampled 11 ms into the ramp as
`[0.9055,0.0134;-0.0134,0.9055]` with translation `(74.1449,47.3127)`. The port focused test matches
those terms. In the windowed port capture, the two SC0000 `0x234` sites (periods 9000/13000 ms, Z axes
`+1/-1`) advanced after 563 ms to integer angles `22/15`, exactly the native formula, and produced distinct
affine PNG frames. Objects are filtered bilinearly, as native's D3D9 sprite filters them ("Object sampling" below).

**Port result (updated 2026-08-19):** `GfxState` retains scale, one-shot rotation, translation, and the
cyclic scale/rotation/translation siblings
with their native clocks/order. `Transform2DMath` composes the full row-vector 4×4 transform before 2D
projection. Godot uses an inverse-mapped affine RGBA8 rasterizer for textured objects and solid fills,
preserving colorkey/tint/opacity behavior and never deriving opacity from transform Z.

##### Object sampling (2026-11-07)

Both engines draw every retained object through a statically linked D3DX sprite (`ID3DXSprite`; SYS4433 creates it in
`gfx_create_sprite@0x47d910`, at renderer `+0xa5a4`). The sprite's `Begin` sets `D3DTADDRESS_CLAMP` and filters derived
from the device caps. Then `gfx_object_blit_d3d9@0x4774c0` (SYS4422) and `gfx_draw_surface_through_sprite@0x47dac0`
(SYS4433) set sampler 0 themselves: `MINFILTER` and `MAGFILTER` `LINEAR`, `MIPFILTER` `NONE`. So every object is
filtered bilinearly:

- D3DX's screen projection puts pixel centres on texel centres, so a 1:1 draw at an integer position copies texels
  exactly.
- A scaled, rotated or subpixel-placed draw blends neighbouring texels.
- The clamp is to the texture, not to the source rectangle, so a scaled region takes some colour from the texels just
  outside it.

Kamidori's backgrounds show the difference. On the prologue page (`kamidori-furigana.steps`) nearest sampling left
the band above the text window up to 78 levels off native, with 12168 pixels more than 8 off. Bilinear sampling brings it
within 1 of native on both the GPU and the software path.

**Port.**

- `SoftwareAffineRasterizer` covers a destination pixel when its centre `inv(x + 0.5, y + 0.5)` lies inside the source
  rectangle. It blends the four texels around `p - 0.5` in source-image coordinates with weights `round(frac * 256)`,
  clamps coordinates to the image, and rounds with `(sum + 32768) >> 16`. Integer translations keep the exact copy
  path.
- `GpuRetainedRenderer` uses Godot's linear filter for any transform other than an integer translation. It turns
  region clipping off so that Godot also reads past the region, as the clamp does.
- Tests: `SoftwareAffineRasterizerTests`, against an independent per-pixel bilinear oracle.

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
`0x220` and the one-shot source-cell channel `0x239` animate only the temporary handle. After the presentation boundary,
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

### Native menu bar — AGERC menu 110 (2026-11-05)

A windowed AGE has a Win32 menu bar. `display_toggle_screen_mode_and_menu_bar@0x412df0` switches the screen mode
("Screen mode" below):

- **To a window,** it sets style `0x90cb0000`, sizes the frame for the caption and the menu, and attaches the menu:
  `LoadMenuA(110)`, `SetMenu`, AGERC `_OperateMenu` op 1, `DrawMenuBar`.
- **To fullscreen,** it sets style `0x90000000` and detaches the menu with `SetMenu(NULL)`. In fullscreen the same
  commands come from AGERC's top-edge strip ("Screen mode" below).

Menu resource 110 is in AGERC.DLL. Its labels, without their mnemonics, are:

| Popup | Items (command id) |
|---|---|
| ｹﾞｰﾑ | ｾｰﾌﾞ `0x9c43`, ﾛｰﾄﾞ `0x9c44`, a separator, 終了 `0x9c41` (labelled ESC) |
| 画像の保存 | 表示画面を保存 `0x9c46`, 画像の保存場所 `0x9c47` |
| ﾒｯｾｰｼﾞ | ﾒｯｾｰｼﾞｽｷｯﾌﾟ `0x9c63`, 既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ `0x9c6f`, ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを消す `0x9c64`, ｵｰﾄﾒｯｾｰｼﾞ `0x9c65`, a separator, ﾒｯｾｰｼﾞの設定 `0x9c6c` |
| 設定 | 画面表示 (ﾌﾙｽｸﾘｰﾝ `0x9c5a`, ｳｲﾝﾄﾞｳ `0x9c5b`); ﾏｳｽの右ｸﾘｯｸ時の動作 (`0x9c49`–`0x9c4b`); 文字表示 (`0x9c5d`, `0x9c5e`); ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ (`0x9c4d`–`0x9c50`); ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過 (なし `0x9c55`, 25–75% `0x9c52`–`0x9c54`); a separator; BGMの設定 (`0x9c56`, `0x9c57`); SEの設定 (`0x9c58`, `0x9c59`); ｻｳﾝﾄﾞ設定 `0x9c68`; その他の設定 `0x9c6e` |
| ﾍﾙﾌﾟ | このｺﾝﾋﾟｭｰﾀの動作環境 `0x9c6d`, a separator, ﾊﾞｰｼﾞｮﾝ情報 `0x9c45` |

AGERC.DLL also holds a debug menu, 124. Its items are F2–F12 commands such as BINﾌｧｲﾙの再読込, ﾀｲﾄﾙに戻る `0x9c6a` and
ﾌﾟﾛｸﾞﾗﾑの終了 `0x9c6b`. No code loads that menu ("Close policy" above).

**Pruning.** `_OperateMenu` op 1 is `agerc_prune_menu_by_settings@0x10002ca0`. A `set:Menu_*` key of 0 removes:

- `Menu_Save`: ｾｰﾌﾞ, ﾛｰﾄﾞ and their separator;
- `Menu_RClick`: ﾏｳｽの右ｸﾘｯｸ時の動作;
- `Menu_UseAntiFont`: 文字表示;
- `Menu_MesWinA`: both ﾒｯｾｰｼﾞｽﾋﾟｰﾄﾞ and ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳの透過. The second test reads `Menu_MesWinA` again, so
  `Menu_MesSpeed` is never read.
- `Menu_SoundONOFF`: BGMの設定 and SEの設定;
- `Menu_Message`: the whole ﾒｯｾｰｼﾞ popup.

Himegari and Kamidori both set every key to 0 except `MENU_MESSAGE`, which is 1 (Kamidori's trailer read
2026-11-06). Their live bar is therefore:

- ｹﾞｰﾑ, holding only 終了;
- 画像の保存;
- ﾒｯｾｰｼﾞ;
- 設定, holding 画面表示, a separator, ｻｳﾝﾄﾞ設定 and その他の設定;
- ﾍﾙﾌﾟ.

The windowed game's outer window is 806×649, and its client starts at (3,46).

**Out of scope** (2026-11-06). The port leaves out three parts no shipped game can reach. It draws only the pruned
bar, so the six radio groups (`0x9c49`–`0x9c59`, `0x9c5d`, `0x9c5e`) never appear, and their command handlers
are not decoded. It opens no right-click ﾒｯｾｰｼﾞ popup ("Right-click popup" below). It does not draw 設定 page 101,
which only `NOSETMUSIC=0` selects ("AGERC dialogs" below). A profile that ships a `MENU_*` key at 1, or
`NOSETMUSIC=0`, reopens the matching part.

**Commands.** `age_main_window_proc@0x486320` handles `WM_COMMAND`:

- `0x9c41` posts `WM_CLOSE(0)`, and `0x9c6b` posts `WM_CLOSE(1)` ("Close policy" above).
- Every other id goes to AGERC `_OperateMenu` op 2, together with the menu that `WM_INITMENU` saved. `WM_INITMENU`
  also runs op 0, the refresh, and `DrawMenuBar`.
- Opcode `0x142` below grays the settings and save items while CONFIG runs.

**Right-click popup.** On `WM_RBUTTONDOWN`, AGE checks two things: any of run-state bits `0x94800000` must be set,
and `message:RMouseEvent` must be 2. When both hold, AGE loads a fresh copy of menu 110. Op 1 never prunes
that copy. AGE then opens its third popup, ﾒｯｾｰｼﾞ, at the cursor with `TrackPopupMenu`. The default in
`SYS4REG.INI` is `RMouseEvent=0`, and both games prune ﾏｳｽの右ｸﾘｯｸ時の動作 from the bar. The 設定 dialog's pages
do not write the key either, so in both games only a hand-edited `SYS4REG.INI` opens the popup.

**`_OperateMenu@16`.** AGE resolves AGERC's export `_OperateMenu@16` (`agerc_operate_menu_export@0x10001070`, ordinal
6) into `g_pfnAgercOperateMenu` (`0x5c601c`). Its first argument selects the operation: 0 refreshes the menu
(`agerc_refresh_system_menu_state@0x10002e10`), 1 prunes it (above), 2 runs a command (`agerc_menu_command@0x100032f0`);
anything else returns -1. Earlier revisions called these "host commands 0/1/2"; the AGERC host commands 4, -1 and 10
named elsewhere in this document go through a different export. Each call first resolves `AGE:reg` and
`AGE:IAGEService` (vtable `0x578034`) through the host callback.

**Refresh.** On every `WM_INITMENU` the refresh rebuilds the check marks, labels and enabled states:

- ﾌﾙｽｸﾘｰﾝ/ｳｲﾝﾄﾞｳ from IAGEService `+0x08`; ﾒｯｾｰｼﾞｽｷｯﾌﾟ from `+0x04` (persistent Skip, `ctx+0x550fc`); ｵｰﾄﾒｯｾｰｼﾞ
  from `+0x0c` (`ctx+0x55104`); 既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ from `message:ReadTextSkip`.
- `0x9c64` reads ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを表示する(&O) and is enabled while the hidden-window wait runs (`+0xb8`, run-state
  `0x4000000`). Otherwise it reads ﾒｯｾｰｼﾞｳｲﾝﾄﾞｳを消す(&H), enabled only at a polling text wait (`+0xbc`, run-state
  `0x80000000`) or the op-`0x95` choice wait (`+0xc0`, `0x10000000`) whose running frame has an op-`0x7b` handler A
  (`+0x80`).
- The 設定 radio groups from `AGE:reg`: `message:RMouseEvent`, `display:UseAntiFont`, `message:MessageSpeed`
  (0 / 2–10 / 11–20 / above 20), `message:MesWinAlpha` (0/4/8/12), `sound:SE` and `sound:Music` (-1 is off).
- Op `0x142`'s field grays the settings and message items (below).

**Command handler.** `agerc_menu_command` runs the ﾒｯｾｰｼﾞ items through IAGEService, and after each of the three toggles
it calls `+0x20` `launch_script_by_name("CALLBACK_SETTING.BIN")`, which the launch queue runs from the ADV tick:

| Id | Service | Native function | Effect |
|---|---|---|---|
| `0x9c63` ﾒｯｾｰｼﾞｽｷｯﾌﾟ | `+0x88` | `adv_toggle_skip_mode@0x406c20` | Flips the persistent Skip; enabling clears Auto unless `set:CoexistMesSkip`. With the skip service on (`ctx+0x55100`, set by scene reset and op `0x19c`, cleared by op `0x19b`), enabling starts fast-forward at once. Disabling leaves run-state `0x08000000` for `adv_interpreter_tick@0x410fb0` to clear: each tick clears it while action 6 is up (no longer injected) and `adv_read_skip_state` is 0 (corrected 2026-11-09; this row named op `0xfa`, which Himegari never runs). |
| `0x9c65` ｵｰﾄﾒｯｾｰｼﾞ | `+0x84` | `adv_toggle_auto_mode@0x406b70` | Enabling sets `0x55104`, clears Skip unless `CoexistMesSkip`, and arms the sleep timer with `AutoMessageTime1` (100 if unset) unless a voice is pending. With the skip service on it clears run-state `0x80000000`: the polling text wait ends at once, so the page advances immediately. Disabling only clears `0x55104`. |
| `0x9c6f` 既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ | `+0xd8` (thunk `0x45f8b0`) | `adv_toggle_read_skip_and_refresh@0x406cd0` | Toggles `message:ReadTextSkip` first. At a text wait, turning it off clears `adv_read_skip_state` (`0x6dbd4`); turning it on checks ReadTextDB: a read message starts fast-forward (run-state `0x08000000`, `0x6dbd4` = 1, indicator stopped); an unread one clears `0x6dbd4` only if Skip is off. |
| `0x9c64` | `+0x8c` | `iage_service_request_hide_window@0x45f700` | ORs `0x80000000` into the trigger mask while hidden (any trigger ends that wait), else bit `0x20` (action 5), which a polling text wait takes as Space's hide ("ADV hide window" below). |

`CALLBACK_SETTING.BIN` redraws the control strip at `0xd2f0`: the base strip, then an ON cell for each of Auto
(`+4`), Skip (`+2`) and read skip (`+3`). While G[`0x62428`] is set it only sets G[`0x62429`]; the scene's next
`0x62428`-clearing epilogue then redraws. SC0000's right-click handler (`0x381`) sets `0x62428` before
`call-script MENU.BIN` and clears it only when MENU returns, so a load from MENU leaves it set. Native's load still
reads it as 0 because layout 3 clears every bank in full ("Layout-3 restore mechanics" below; native-read
2026-11-05).

**Menu loop.** While a menu is open, Windows runs its modal menu loop on AGE's thread, so the game stops: no
presents, no script progress, no input reaches it. A focus loss closes the menu. The keyboard stops on a grayed item
too, and Windows draws it with a gray highlight: `0xe6e6e6`, with `0xe4e4e4` over the check-mark column. That was
measured on the window menu's grayed Restore (below); in the shipped games menu 110 has no grayed item that a
keyboard run reached. With a popup open, hovering another bar item switches to it, and pressing the open item's
title closes it.

**Keyboard mode** (driven natively 2026-11-06, windowed and on the fullscreen strip). AGE's window procedure leaves
`WM_SYSKEYUP`, `WM_SYSCHAR` and `SC_KEYMENU` (other than Alt+Enter's `lParam` `0xd`) to `DefWindowProc`, so the
keyboard reaches the bar as in any Windows program:

- **Entry.** Alt pressed and released with nothing between, or F10 the same way, starts the menu loop with the first
  bar item, ｹﾞｰﾑ, selected and no dropdown. The item has the pushed look (border `0x99d1ff`, fill `0xcce8ff`, bottom
  row `0x91c9f7`), every bar label shows its access-key underline, and the caption looks active. Bit `0x200` of op
  `0x10f`'s mask keeps F10 out ("Op `0x10f`" below). The game holds as for any menu loop.
- **Moving.** Left and Right cycle ｹﾞｰﾑ, 画像の保存, ﾒｯｾｰｼﾞ, 設定, ﾍﾙﾌﾟ and then a sixth position, the window menu,
  where nothing is drawn. Underlines stay on throughout.
- **Opening.** Down, Up or Enter opens the selected item's dropdown with its first row selected. An access key
  (`m`) opens its own item's dropdown the same way. So does Alt plus an access key from outside the loop
  (`SC_KEYMENU`). At the window-menu position, Down, Up or Enter opens the window menu; Alt+Space opens it from
  outside the loop.
- **With a dropdown open.** Left and Right pass through the same six positions, opening each dropdown with its first
  row selected. At the window-menu position they open the window menu.
- **The window menu** opens at window (3,25), one row above the bar, 178×145. It shows Restore, Move, Size, Minimize,
  Maximize, a separator, and Close (bold, the default item, with Alt+F4). The labels follow the OS display language
  (English on the capture machine), and Restore, Minimize, Maximize and Close carry Windows' glyphs in the check-mark
  column. A window has Restore and Size grayed. The fullscreen strip's has Size and Maximize grayed. Down on it
  selects Restore even when grayed.
- **Leaving.** Alt or F10 ends the loop, with or without a dropdown. A click on the game ends it, and the click is
  eaten: the next one advances. The bar then returns to plain labels. Escape cannot be driven natively (the driver
  refuses it); by Windows' rule it closes the innermost dropdown and then the loop.
- **Fullscreen.** The Alt press that shows the strip goes to AGE's window procedure, which shows the strip and returns
  without calling `DefWindowProc`, so its release starts nothing. The next Alt tap, or F10, reaches the strip, whose
  dialog procedure leaves it to `DefWindowProc`: its menu enters keyboard mode as the bar's does, with the same six
  positions. With the strip hidden, F10 has no visible effect (Down after it opens no window menu).
- **Activation.** The loop starts only in the active window of AGE's thread, which the window a user types into
  always is. A thread with no active window ignores Alt, F10 and Alt+M. Measured with `GetGUIThreadInfo`: a minimize
  of the foreground AGE leaves its thread with none, and a restore with `SW_SHOWNOACTIVATE` does not give it one. The
  window does not need the foreground: once activated inside AGE's thread, with another process in the foreground, a
  driven Alt tap entered the loop (menu-mode flag `0x4`, menu owner AGE's window). The driver activates it that way
  before such a key ("Driving native AGE", The foreground).
- **Not driven.** Pointer moves during keyboard mode. The driver's posted `WM_MOUSEMOVE` does not move the selection,
  but Windows' menu loop follows the real cursor, so that run decides nothing.

**Screenshots** (native-read and driver-checked 2026-11-05). F1 and 表示画面を保存 (`0x9c46`) both reach
`engine_save_screenshot_bmp@0x407ce0`:

- **Callers.** `age_main_window_proc`'s `WM_KEYDOWN` F1 passes surface 0. It fires on auto-repeat too, unless bit 0
  of `ctx+0x6da7c` (`system_key_disable_mask`, op `0x10f` below) is set. `agerc_menu_command` sends `0x9c46` to
  IAGEService `+0xa4` (`iage_service_save_screenshot_bmp@0x45f780`), which passes surface `0x1e`.
- **Name.** A process-global counter (`0x5c4544`, from 0) numbers `CG%6.6d.BMP`. Each probe opens the name with
  `OPEN_EXISTING` and advances the counter, so the first missing name is written and the next save goes on from
  there. At 1,000,000 the function gives up.
- **Folder.** `system:SaveBMPPath` when it is set, else the Desktop (`SHGetFolderPathA(0x8010)`).
- **Pixels.** In draw mode 1 (`ctx+0x99290`, Himegari) `gfx_backend1_save_backbuffer_bmp24@0x479800` reads the D3D
  back buffer and ignores the surface argument. `gfx_surface_write_bmp24_to_path@0x475420` writes it as the save
  thumbnails' BMP: bottom-up, 24-bit, with a `bfSize` that leaves out the 14-byte file header. An 800×600 frame
  makes 1,440,054 bytes with `bfSize` 1,440,040. The game frame is saved without the menu bar. While the menu loop
  runs nothing is presented, so the menu command saves the frame from when the menu opened.
- **Feedback.** None: no message and no sound, on success or failure.

**Op `0x10f`.** `op_0x10f_set_system_key_disable_mask@0x4217a0` stores its operand at `ctx+0x6da7c`. Bit 0 blocks
F1. Bit `0x200` makes `age_main_window_proc` swallow `WM_SYSKEYDOWN` F10, the key that selects the menu bar. The
constructor and `scene_context_init_reset@0x40b3b0` clear the field. Neither Himegari's nor Kamidori's scripts use the
opcode.

**画像の保存場所** (`0x9c47`). `agerc_choose_screenshot_folder@0x10002bc0` opens `SHBrowseForFolderA`, owned by the
game window, rooted at the desktop, with `BIF_RETURNONLYFSDIRS` and the prompt 画像を保存するフォルダを選択してください.
Its callback, `agerc_screenshot_folder_browse_callback@0x10002a70`, preselects where the next screenshot would go:

- `SaveBMPPath`;
- when that is empty, the game's directory (`agerc_game_executable_directory@0x10002980`);
- or the Desktop instead when `set:UseAppDataFolder` is 1, as Himegari's is.

OK stores the folder through `AGE:reg` as `system:SaveBMPPath`; Cancel changes nothing. AGE writes `SYS4REG.INI`
when it exits, so the file shows the value only then. The dialog's modal loop runs on AGE's thread, so the game stops
while it is open.

**Native check.** `tools/drive/scenarios/himegari-screenshot.steps` runs on both targets in a sandbox, whose Desktop
the driver redirects. It saves `CG000000`–`CG000003.BMP` there. The BMP headers are identical on both targets. Each
BMP equals the shot taken before it, except the animated wait indicator (about x 387..412, y 570..597). Native and port
BMPs differ only where their shots do, in glyph-edge antialiasing. After OK, both targets store the sandbox Desktop as
`SaveBMPPath`, and the next BMP goes there.

**AGERC dialogs** (native-read and driver-checked 2026-11-05). The other dialog commands go through
`agerc_dispatch_host_command@0x100025f0`. In fullscreen only (IAGEService `+0x08`,
`iage_service_query_display_windowed`, returns 0), `agerc_menu_command` first hides the top-edge strip and brackets
the dialog with two IAGEService calls:

- `+0x14` is `engine_suspend_presentation_for_dialog@0x405e10`. It sets run-state `0x200000`, and in draw mode 1 it
  also sets `ctx+0x9b240`.
- `+0x18` is `engine_resume_presentation_after_dialog@0x405fc0`, which undoes both.

A windowed game skips both calls. Either way the dialog's modal loop runs on AGE's thread, so the game stops and its
last frame stays on screen. Unlike the 終了 dialog, nothing dims it.

- **Commands.** Command 2 opens dialog resource 2, ﾊﾞｰｼﾞｮﾝ情報. Command 9 opens dialog 9, 設定, through proc
  `0x10006400`: a tab control `0x417` holding the pages 100–105, with OK, ｷｬﾝｾﾙ, ﾊﾞｰｼﾞｮﾝ情報 and ﾃﾞﾊﾞｯｸﾞ設定 buttons.
  The starting tab comes from `0x100a0b6c`: `0x64` selects ﾒｯｾｰｼﾞ (tab 0), `0x66` ｻｳﾝﾄﾞ (1), `0x68` その他 (2) and
  `0x69` 動作環境 (3). Page 105, 動作環境, fills bordered read-only edits with the OS, DirectX version, CPU, memory and
  VRAM, and drive space, and has a ｸﾘｯﾌﾟﾎﾞｰﾄﾞにｺﾋﾟｰ button `0x419`. このｺﾝﾋﾟｭｰﾀの動作環境 (`0x9c6d`) is therefore
  dialog 9 opened at tab 3.
- **Layout.** AGERC's templates use ＭＳ Ｐゴシック 9 pt, whose base units are 7×12. `CreateDialogIndirect` converts each
  control's x, y, cx and cy on its own: `MulDiv(x, 7, 4)`, `MulDiv(y, 12, 8)`, rounding half away from zero. A width is
  therefore not the difference of two converted edges. This rule reproduces every control rectangle of the 終了
  dialog and of all 14 controls of ﾊﾞｰｼﾞｮﾝ情報 (client 376×278).
- **ﾊﾞｰｼﾞｮﾝ情報.** The procedure is `agerc_version_dialog_proc@0x10001e70`. On `WM_INITDIALOG` it centres the dialog on
  AGE's window and fills its controls:
  - `0x400` is "Version " plus the running EXE's `\StringFileInfo\041103a4\FileVersion`
    (`agerc_exe_file_version_string@0x10001c90`; AGE.EXE 4.24J);
  - `0x404` gets `STM_SETICON` with AGERC's `IDI_ICON1`;
  - `0x3ee` is `set:GameName`, the SYS4INI catalog title;
  - `0x407` is `set:Copyright`;
  - `0x3ef` is "Version " plus `set:GameVersion`;
  - `0x3f0` is IAGEService `+0xcc` (`iage_service_product_id_string@0x470090`), the copy protector's
    machine-specific product ID.

  The OK button's id is 2 (IDCANCEL), so OK, Enter, Escape and the ✕ all close the dialog, and IDOK does nothing.
  OK with Ctrl held opens registration dialog 134 (キーの入力), but only when `set:IsReggist` exists and the
  registration check fails. Himegari never sets `set:IsReggist`.
- **`set:GameVersion`.** With `VERREGPOS` set, `registration_read_install_display_version@0x46ae80` opens
  `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\InstallShield_{<VERREGPOS>}`. It calls
  `RegCreateKeyExA` with class `CLASS00` and `KEY_ALL_ACCESS` through the protector stub. It then reads
  `DisplayVersion`, or uses `1.00.0000` when that value is missing. AGE is 32-bit, so this is the WOW6432Node
  view. The capture machine's installer (patch 1.03) gives 1.03.0008, while SYS4INI's `GAMEVERSION` is 1.00.
- **Look.** The focused OK gains `BS_DEFPUSHBUTTON` and its 1 px frame. A dialog opened from the menu shows no
  focus rectangle or mnemonic underline until a navigation key or Alt is pressed (Windows' UI state). Read-only
  borderless edits draw on COLOR_BTNFACE, 1 px in from the left and at the top, and the `ES_CENTER` one centres its
  text. The separator is a 2 px `SS_BLACKFRAME | SS_SUNKEN` static: its top row and left column are shadow, its
  bottom row and right column are highlight. The lines of a multi-line static are 12 px apart (tmHeight). The icon
  is 32×32, so the `SS_ICON` static takes that size.
- **Frame.** Every captured AGE dialog draws its caption in ClearType. The port draws it from 4 px inside the
  frame's left edge, which lines its glyphs up with the captures. The ✕ is antialiased with one coverage mask: an end pixel is full, a diagonal pixel 250/255, and its side neighbours 51/255.
  The mask is drawn black, white over red when hovered, and grey `0xcccccc` when disabled.

**設定** (dialog 9; native-read and driver-checked 2026-11-05). `agerc_settings_dialog_proc@0x10006400` runs it.

- **Init.** On `WM_INITDIALOG` it centres the dialog on AGE's window and inserts the tabs ﾒｯｾｰｼﾞ, ｻｳﾝﾄﾞ, その他 and
  動作環境 into `0x417`. It then creates the DS_CONTROL pages with `CreateDialogParamA`, in this order:
  - 105 (動作環境);
  - 104 (その他);
  - the ｻｳﾝﾄﾞ page: 101, or 102 when `set:NoSetMusic` is nonzero (Himegari's is 3);
  - 100 (ﾒｯｾｰｼﾞ).

  Each page sits at its template's x and y and keeps its template's size. Page 103 is never created. The
  procedure shows only the start tab's page and hides ﾃﾞﾊﾞｯｸﾞ設定 `0x410`. A tab change hides the old page on
  `TCN_SELCHANGING` and shows the new one on `TCN_SELCHANGE`.
- **Buttons.** ﾊﾞｰｼﾞｮﾝ情報 `0x3ff` is host command 2 with 設定 as the owner. `agerc_center_dialog_on_parent`
  centres it on its owner, 設定, which is itself centred on AGE's window. The version dialog therefore opens where
  the menu opens it: (212,171)–(594,478), inside 設定's (160,131)–(647,517). OK runs `agerc_settings_apply@0x10005c20`, launches
  `CALLBACK_SETTING.BIN` through IAGEService `+0x20` and ends with 1. ｷｬﾝｾﾙ undoes the ｻｳﾝﾄﾞ page's live changes
  (below) and ends with 0. `0x410` would open dialog 6.
- **ﾒｯｾｰｼﾞ** (`agerc_settings_message_page_proc@0x10004cc0`). The page has four `TBS_BOTTOM` trackbars. `0x414`
  (0..100) is set to 100 − `message:MessageSpeed`, and `0x415` (0..16) to 16 − `MesWinAlpha`. `0x416` and `0x417`
  (0..100) are set to `AutoMessageTime0` and `AutoMessageTime1` divided by 100. Moving a trackbar refreshes its edit
  only:
  - `0x3ee` reads ﾉｰｳｴｲﾄ at 100, else ｳｴｲﾄ%d (100 − pos);
  - `0x3ef` reads `%d%%` (pos × 100 / 16);
  - `0x3f0` and `0x3f1` read `%d.%d秒`.

  Check 1000 is `message:UseAntiFont`, and `0x3e9` is `AdvanceMesOnWheel`.
- **ｻｳﾝﾄﾞ** (`agerc_settings_sound_page_proc@0x10005080`). Its four vertical `TBS_BOTH` trackbars show the master,
  BGM, SE and voice volumes at 100 − basis points / 100. The values come from IAGEService `+0x40`/`+0x48`/`+0x50`/`+0x58`;
  `+0x40` reads `ctx+0x4b68`, the sound object's `+0x530`, which `FUN_004829f0` initialises to 10000. An unconfigured
  volume reads 10000 and shows at the top. (The driver's quiet seed `sound.Volume0=0` puts the master at the bottom.)
  The page acts at once, and ｷｬﾝｾﾙ restores the saved values:
  - a volume trackbar calls `+0x3c`/`+0x44`/`+0x4c`/`+0x54` with (100 − pos) × 100;
  - the BGM (1000), 効果音 (`0x3e9`) and 音声 (`0x3ed`) checks call the route enables `+0x24`/`+0x28`/`+0x2c`.

  `0x3ea` (`MusicFadeOnVoicePlaying`) and the `TBS_BOTH` trackbar `0x418` (`MusicFadeOnVoicePlayingVolume`, edit
  `0x3ee` `%d%%`) take effect only on OK. Page 101 adds radio buttons for `sound:Music`: 1 checks `0x40f`, 2 checks
  `0x3ec`, and 0 or -1 checks `0x3eb`.
- **その他** (`agerc_settings_other_page_proc@0x10005600`). Check 1000 is `system:EffectSkipOnClick`.
- **動作環境** (`agerc_settings_system_info_page_proc@0x10005870`). `WM_INITDIALOG` starts the clock measurement and
  a 100 ms timer, then fills the edits:
  - `0x3ee`: `agerc_os_version_string`, from `GetVersionExA`. Unmanifested, Windows 8 and later report 6.2.9200, so
    the edit reads " Windows Vista 以降  (6.2.9200)".
  - `0x3f2`: `agerc_directx_version_string`. It reads the 32-bit view of `HKLM\Software\Microsoft\DirectX`: the top
    bytes of `InstalledVersion`'s two dwords, and `Version`. The result is " DirectX 9.0 (4.09.00.0904)".
  - `0x3f3` and `0x3f4`: `agerc_format_drive_space` for the current directory's drive and the Windows drive. Each
    shows " %c: ", then the total and the free space in MiB as " %dMByte" up to 1000, " %d.%2.2dGByte" above,
    joined by " /".

  The 11th tick fills `0x3ef` (" %s / %dMHz") and `0x3f1` (" %dMByte / %dMByte"), then kills the timer:
  - **CPU.** The brand comes from CPUID `0x80000002..4`, from its first non-space (trailing spaces stay).
    `agerc_cpu_clock_measure_mhz` divides the RDTSC ticks since init by 1000 and by the `timeGetTime`
    milliseconds. Repeat opens read 4686–4694 MHz against the registry's `~MHz` of 4691. The first open in each of
    two processes read 0 and 14862 MHz instead, which was not explained.
  - **Memory.** `GlobalMemoryStatus`'s `dwTotalPhys` (at most 2 GiB − 1 for this 32-bit, non-large-address-aware
    AGE) / 2^20 + 1.
  - **VRAM.** IAGEService `+0x78` / 1,000,000. `+0x78` multiplies DxDiag's display memory in MB by 1,000,000 as a
    32-bit int, so 4095 MB reads −199. The first call in a process takes about 2.3 s (2283 ms, Frida-timed
    2026-11-06; later opens 0 ms), so the first 設定 holds AGE's thread from about 1.2 s to 3.5 s after it opens.
    A click in that window waits in the queue and acts afterwards.

  ｸﾘｯﾌﾟﾎﾞｰﾄﾞにｺﾋﾟｰ (`0x419`) copies `0x3ee`, `0x3f2`, `0x3ef`, `0x3f1`, `0x3f3` and `0x3f4` as CF_TEXT lines.
- **OK** (`agerc_settings_apply`) writes through `AGE:reg` in this order:
  - `message:MessageSpeed`, also IAGEService `+0x10`;
  - `MesWinAlpha`;
  - `AutoMessageTime0/1` = pos × 100;
  - UseAntiFont through `+0x68`;
  - `AdvanceMesOnWheel`;
  - the three route enables;
  - `MusicFadeOnVoicePlaying`;
  - `sound:Volume0..3` = (100 − pos) × 100;
  - `MusicFadeOnVoicePlayingVolume`;
  - `system:EffectSkipOnClick`;
  - `mk:mksh` through `+0x14`, from a control page 104 lacks, so always 0. It is not persisted.

  AGE writes `SYS4REG.INI` only when it quits. The port's store writes most keys at once, and after
  `himegari-settings-dialog.steps` both targets' files are identical. The `[message]` values the VM holds while it
  runs (MessageSpeed, MesWinAlpha, AutoMessageTime0/1 and, since 2026-11-09, ReadTextSkip) are written at quit, as
  AGE writes them.
- **Classic controls.** The pages draw unthemed comctl32 controls: check boxes, group boxes, trackbars with a
  22 px thumb, the tab control and `WS_EX_CLIENTEDGE` edits. The port drew them with measured pixel geometry until
  engine shell M3 (2026-11-09) replaced 設定 with its own panel; the classes that recorded it
  (`godot/Win32ClassicCheckBox.cs`, `Win32GroupBox.cs`, `Win32Trackbar.cs`, `Win32TabControl.cs`, `Win32Edit.cs`,
  `Win32Edges.cs`) remain in the history before that change.

**Measured look** (native captures, 2026-11-05):

- **Bar.** 20 px tall. Each item is its text width plus 14, and the text starts 7 px in.
- **Popups.** A popup opens at its bar item's x and client y 19. Rows are 22 px, separators 7 px, and labels start
  at x 35. A submenu opens at its parent's right edge minus 6. Native popups may extend past the game window; the
  ﾍﾙﾌﾟ popup spans 744..989.
- **Font.** The menu font is Segoe UI at -12 (9 pt at 96 dpi). GDI font linking draws the Japanese labels in
  Meiryo UI at 13 px, so a halfwidth katakana advances 7 px (6.5 hinted), using GDI's integer per-glyph advances
  with no kerning.

**Port** (2026-11-05). `godot/Main.MenuBar.cs` draws menu 110 above the game:

- **Data and geometry.** Since engine shell M2 (2026-11-09) the bar comes from the shell's model, not the DLL:
  `ShellMenu` (`engine/Ome.Engine.Frontend/Shell`) holds menu 110's tree, labelled from the shell's string table,
  and models op 1 (`Prune`) and op 0 (`Refresh`). `godot/NativeMenuBar.cs` draws the bar and the popups with the
  measured geometry. The window grows by the bar's height; shots, the cursor mapping and the dialogs place the game
  below it.
- **Commands.** `ShellCommands` names each command id and runs it through `IShellHost` and `IVmMenuServices`. 終了
  sends the asking close. The four ﾒｯｾｰｼﾞ items call the VM's menu services (`RequestMenuMessageSkipToggle`,
  `…AutoMessageToggle`, `…ReadMessageSkipToggle`, `RequestMenuHideWindow`). The VM applies them at its next
  instruction boundary or text-wait service, then runs `CALLBACK_SETTING.BIN`. Ending a text wait from Auto goes
  through `IInputHost.EndInputWait`. 設定's section commands, 動作環境 and ﾊﾞｰｼﾞｮﾝ情報 open the shell's options panel
  (below), so every install has them; the port reads nothing from AGERC.DLL.
- **Screenshots.** `godot/Main.Screenshot.cs` saves through `ScreenshotBmpStore` (`engine/Ome.Engine/Persistence`).
  表示画面を保存 saves the frame frozen when the menu opened; F1 saves the current frame unless
  `VirtualMachine.SystemKeyDisableMask` (op `0x10f`) has bit 0, or a dialog is open. `--desktop-dir` replaces the
  Desktop. Bit `0x200` keeps F10 from entering the bar's keyboard mode, as natively.
- **Image folder.** 画像の保存場所 holds the VM, freezes the frame and opens the same shell dialog
  (`godot/ScreenshotFolderDialog.cs`). The dialog runs on a thread of its own so that Godot keeps drawing, and the
  driver's `dialogs` and `answer` steps reach it as they reach native's. Other platforms use their own folder
  dialog. The port writes `SaveBMPPath` to `SYS4REG.INI` at once.
- **Options panel** (engine shell M3, 2026-11-09; `docs/superpowers/specs/2026-11-09-engine-shell-design.md`).
  設定, ﾊﾞｰｼﾞｮﾝ情報 and 動作環境 are one panel of the port's own (`godot/OptionsPanel.cs`, opened and committed by
  `godot/Main.OptionsPanel.cs`), drawn in Noto Sans JP over the frozen frame while the VM holds. Its sections are
  ﾒｯｾｰｼﾞ, ｻｳﾝﾄﾞ and その他 (設定's pages 100, 102 and 104, with their wording and every value their controls can
  write), 画面 (the screen mode and the window size), OME (the port options in `OME.INI`) and 情報 (about and
  diagnostics). It keeps 設定's commit model in `ShellOptionsDraft` (`engine/Ome.Engine.Frontend/Shell`): the ｻｳﾝﾄﾞ
  volumes and routes apply live and ｷｬﾝｾﾙ restores them (routes, then volumes); 決定 applies the draft in
  `agerc_settings_apply`'s order above and hands the message settings to `VirtualMachine.RequestMessageSettings`,
  which applies them at the next safe point and runs `CALLBACK_SETTING.BIN`. 設定's OK applies even when nothing
  changed and ﾊﾞｰｼﾞｮﾝ情報's never does, so the panel opened from ﾊﾞｰｼﾞｮﾝ情報 applies only an edit. While op `0x142`'s
  gate is 0 (Kamidori's CONFIG), when native 設定 cannot open, the ﾒｯｾｰｼﾞ, ｻｳﾝﾄﾞ and その他 sections are read-only and
  nothing of theirs commits. The driver's `dialogs` lists the panel by its Japanese texts (title 設定, the section
  shown, its check boxes, the sections, 決定 and ｷｬﾝｾﾙ), and `answer` presses them; a read-only check box refuses.
- **情報 names the port.** The about rows fill the game half as AGE fills ﾊﾞｰｼﾞｮﾝ情報's (`set:GameName`,
  `set:GameVersion` by the `VERREGPOS` rule, read from the registry on Windows, and `set:Copyright`). The engine half
  is `EngineIdentity` (`engine/Ome.Engine.Frontend/Shell`) with the OME icon: Open Maid Engine for ARCGameEngine,
  "Released under the MIT License" for ARC Software Laboratory's copyright, the FFmpeg notice FFmpeg's license
  checklist asks for (FFmpeg, its LGPLv2.1, and `FFmpeg-SOURCE.txt` for its source; `platform-portability.md`, item 10)
  for AGE's Ogg Vorbis notice, and "Third-party licenses: `THIRD_PARTY_NOTICES.md`" for Xiphophorus' copyright. The
  version is the engine's, which the build takes from `git describe --tags --match "v[0-9]*" --dirty=+ --always`
  (`godot/OME.csproj`, target `OmeVersion`; `-p:OmeVersion=` overrides it): the tag's "v" dropped, "dev-" before a
  bare commit (a clone without tags), "unknown" when the build stored none. The プロダクトID row is left out: the
  port has no copy protector. 動作環境's diagnostics give the OS, the Godot version, the renderer and GPU adapter, the
  audio output, the engine, the profile and the data paths, with a copy button whose text uses English labels.
  Native's DirectX version, CPU clock and VRAM figures are not carried.
- **Unimplemented commands.** The refresh grays every command the port cannot run yet.
- **Menu loop.** While a menu is open, the port holds the VM and freezes the presented frame. The hold nests, so the
  終了 dialog can open from it.
- **Keyboard.** `NativeMenuBar` implements keyboard mode as described above, on the bar and on the strip.
  `godot/WindowSystemMenu.cs` supplies the window menu: native's states per mode, the labels of the port window's
  own window menu (`GetMenuString`, so they follow the display language; English off Windows), and the four glyphs
  captured from native. A selected grayed item draws gray. The default item uses the bold menu font
  (`Win32DialogStyle.MenuBoldFont`, with GDI's bold advances). The window menu's commands run as AGE's
  `WM_SYSCOMMAND` does: Minimize, Maximize (fullscreen, unless `ForceScreen`), Close (the 終了 dialog) and Move
  (Windows' keyboard move loop on the port's window). On the strip they act as its caption buttons do; its Move does
  nothing. A key that starts the loop counts as the window being active, since only the focused window gets keys; the
  driver's keys reach an unfocused port as its posted messages reach native AGE. Pointer moves in keyboard mode
  without a dropdown move the selection without opening it, which is Windows' rule but not driven natively.
- **Text.** Labels are drawn glyph by glyph, with GDI's advances (`GdiTextMetrics`, Windows only), the font's own
  coverage check (`Win32Font.PrimaryHasChar`; Godot's `Font.HasChar` also counts fallbacks), and LCD antialiasing.
  The 終了 and script-error dialogs share this text path.
- **Opting out.** `--no-menu-bar` restores the plain window.
- **Checks.** `tools/drive/scenarios/himegari-menu-bar.steps` checks read skip, hide, Auto and 終了 on both
  targets, `himegari-screenshot.steps` checks the screenshots and the image folder,
  `himegari-version-dialog.steps` checks ﾊﾞｰｼﾞｮﾝ情報, and `himegari-settings-dialog.steps` checks 設定 through the
  driver's `option` steps, which set AGERC's controls by id natively and the panel's draft on the port; both
  targets' `SYS4REG.INI` files come out identical. The driver's `menu` step chooses an item as a user does. Natively it sends `WM_INITMENU`, reads the item
  state, then posts `WM_COMMAND`. Both targets refuse a grayed item. `himegari-menu-keyboard.steps` drives keyboard
  mode on SC0000. Both targets eat the same click and move the same one page through ｵｰﾄﾒｯｾｰｼﾞ on and off. Both
  open the 終了 dialog from the window menu's Close. Full-window captures of the six positions and the window menu
  match native apart from ClearType fringes and the window menu's row (below).
- **Remaining gaps.** `docs/backlog.md`, "Engine shell menu bar", lists the optional look differences, including native's
  off-window popups and the window menu's row: it opens one row above the bar natively and at the bar's top in the
  port. The ﾒｯｾｰｼﾞ page's UseAntiFont check is applied on OK ("AntiFont off" in the text sections).

### Screen mode — ﾌﾙｽｸﾘｰﾝ, ｳｲﾝﾄﾞｳ and the top-edge strip (2026-11-06)

**The switch.** `display_toggle_screen_mode_and_menu_bar@0x412df0` takes the target mode: 1 for a window, 0 for
fullscreen.

- When the target equals `display_windowed` (`ctx+0x9a298`), it returns at once and runs no callback.
- With `display:ForceScreen` set to 1, it refuses to leave a window.
- Otherwise it restyles the window and attaches or detaches the menu ("Native menu bar" above). It then flips
  `display_windowed`, stores `display:ScreenMode` (written to `SYS4REG.INI` at exit) and stamps `ctx+0x55118` with
  `timeGetTime`. For one second after that stamp, AGE ignores message `0x1401`, the fullscreen suspend on losing the
  foreground. Last, it calls `script_launch_by_name("CALLBACK_WINDOW.BIN")`. The launch queue runs that script from
  the ADV tick, as it runs `CALLBACK_SETTING.BIN`.

Four callers reach the switch:

| Caller | Target |
|---|---|
| `WM_SYSCOMMAND` `SC_MAXIMIZE` (the maximize box) | 0 |
| `SC_KEYMENU` with `lParam` `0xd` (Alt+Enter) | the other mode |
| op `0x2cf` | its operand, when 0 or 1 (no Himegari or Kamidori script calls it) |
| IAGEService `+0x1c` (`iage_service_toggle_screen_mode@0x45f4f0`) | AGERC's ﾌﾙｽｸﾘｰﾝ `0x9c5a` passes 0 and ｳｲﾝﾄﾞｳ `0x9c5b` passes 1; the strip's restore passes 1 and its Alt+Enter the other mode |

The window procedure ignores the first two while `display:ForceScreen` is 1.

**Startup.** `engine_initialize_graphics_from_settings@0x406480` starts windowed when `display:ScreenMode` is
nonzero. The registered default is 0, so a new profile starts fullscreen. A SYS4INI `FORCESCREEN` pair overrides
`display:ForceScreen`'s default (the trailer import at `0x46e5f5`). With `display:VirtualFullScreen` 0 (the default),
fullscreen changes the display mode. With 1, a window covers the desktop instead, and the driver's sandboxes set 1
(`tools/drive/himegari.toml`). In that mode, the driver's client capture is desktop-sized, not a canvas image.

**The strip.** Fullscreen has no menu bar. Instead, AGERC shows a strip across the top of the screen: host command 4
(`agerc_dispatch_host_command@0x100025f0`). The first time, the host command creates it with `CreateDialogParam(4,
agerc_system_menu_strip_dialog_proc@0x10003c20)`; later it calls `ShowWindow(SW_SHOW)`. It also sets active-host-UI bit
`0x10`.

`WM_INITDIALOG` builds the strip:

- It copies AGE's title and sets AGERC's `IDI_ICON1` (`WM_SETICON`).
- It sets style `0x91cb0000`, a maximized popup with a caption, system menu and minimize/maximize boxes.
- It calls `SetWindowPos` to (0,0), `SM_CXSCREEN` wide and `SM_CYCAPTION + 2·SM_CYFIXEDFRAME + SM_CYMENU` tall
  (49 rows here).
- It refreshes and prunes its own copy of menu 110, as the bar's are.

A capture of the strip on Windows 10 at 96 dpi (`PrintWindow`, inactive look, 3440×49) gives this layout:

- **Rows.** Row 0 is frame. Rows 1–25 are the white caption and rows 26–45 the menu bar; its bottom lines are
  rows 44 (`f2`) and 45 (`f0`). Row 46 is frame. Rows 47–48 are transparent.
- **Columns.** Columns 0–1 and the last two are transparent; the frame is at column 2 and at W−3.
- **Caption.** The 16×16 icon is at (5,5) and the title strokes start at x 27. The caption glyphs are 10 px wide:
  the minimize line at W−122 (row 13), the restore glyph at W−77 (rows 8–17; two 8 px squares) and the ✕ at W−31.
- **Menu.** The menu text starts at x 10. ﾍﾙﾌﾟ is right-justified and ends at W−11.
- **Colours.** The inactive look has frame `0x2b2b2b` and caption text `0x999999`.

`agerc_system_menu_strip_hide@0x10002a40` hides the strip in three steps:

1. `ShowWindow(SW_HIDE)`.
2. IAGEService `+0x30` (`iage_service_system_menu_strip_hidden@0x45f540` → `system_menu_strip_hidden_resume@0x407c70`):
   this clears run-state bit `0x400000`, resumes presentation and turns input polling back on.
3. It clears bit `0x10`.

The dialog procedure handles these messages:

- `WM_ACTIVATE` inactive hides the strip while it shows (IAGEService `+0xc4`, run-state `0x400000`).
- `SC_MINIMIZE` hides it, calls IAGEService `+0x9c(1, 0)` (`iage_service_suspend_on_minimize@0x45fa90`), then forwards
  `SC_MINIMIZE` to AGE.
- `SC_RESTORE` (the restore button or a double click on the caption) hides it, then calls `+0x1c(1)`.
- `SC_KEYMENU` `0xd` hides it, then switches to the other mode.
- `IDCANCEL` (Escape, the close button, Alt+F4) and `0x9c41` post `WM_CLOSE(0)` to AGE, then hide it. `0x9c6b` hides
  it, then posts `WM_CLOSE(1)`. Every other command goes to `agerc_menu_command`.
- `WM_ENTERMENULOOP` refreshes the menu and redraws it. `WM_MOVING` keeps the strip in place, and
  `WM_GETMINMAXINFO` keeps its height.

In fullscreen (IAGEService `+0x08` returns 0), `agerc_menu_command` hides the strip before its command for these ids:
`0x9c43`–`0x9c45`, `0x9c47`, `0x9c52`–`0x9c55`, `0x9c5a`, `0x9c5b`, `0x9c5d`, `0x9c5e`, `0x9c62`–`0x9c65`,
`0x9c68`, `0x9c6a` and `0x9c6c`–`0x9c6f`. It brackets its dialogs with `+0x14`/`+0x18`. `0x9c46` saves the
screenshot first, then hides the strip.

**Showing the strip.** `system_menu_strip_show@0x407bd0` has two callers:

- The top-edge dwell ("System-menu top-edge reveal delay" below).
- `WM_SYSKEYDOWN` `VK_MENU` in fullscreen. This shows the strip at once, turns input polling off, clears the dwell
  and kills timer 1.

The function does four things:

1. It freezes the presented frame (`engine_suspend_presentation_for_dialog`).
2. It sets run-state bit `0x400000`.
3. It calls host command 4.
4. With draw mode 1 and `display:PresentInterval` nonzero, it saves the system's selection-fade setting and turns
   it off (`SPI_SETSELECTIONFADE`). The hide restores it.

The following was observed natively on 2026-11-06. While the strip shows, the VM keeps running and the screen keeps
its last frame. Input polling is off. A click on the game deactivates the strip, so the strip hides, and the click
then reaches the game. While the strip's menu loop runs, the game holds, as it does for the bar's.

**Port** (`godot/Main.ScreenMode.cs`, `godot/SystemMenuStrip.cs`, `VirtualMachine.ScreenMode.cs`):

- **Switches.** ﾌﾙｽｸﾘｰﾝ, ｳｲﾝﾄﾞｳ, Alt+Enter, the maximize box, the strip's restore button and op `0x2cf` all switch.
  Op `0x2cf` asks the host (`ILifecycleHost.RequestScreenMode`), and the frontend switches at its next frame. A
  switch to the current mode does nothing, and `display:ForceScreen` 1 keeps the window. Each switch queues
  `CALLBACK_WINDOW.BIN` (`VirtualMachine.RequestWindowCallback`). It runs where `CALLBACK_SETTING.BIN` would: at the
  next instruction boundary, or inside a polling text wait.
- **Startup.** The game starts in the mode `display:ScreenMode` names. A run without `SYS4REG.INI` (self-test,
  read-only persistence) or with `--windowed` starts windowed.
- **Fullscreen.** The port uses a modern fullscreen rather than native's exclusive mode: a borderless window over
  the monitor, with the canvas scaled to fit, its aspect kept and centred. It ignores `VirtualFullScreen`,
  `AspectMode`, `FullScreenWidth`/`Height` and `FullScreenBit`, and never changes the display mode.
- **The strip's drawing.** The port draws the strip in game pixels over the top of the canvas, as native's default
  800×600 fullscreen shows it (`SM_CXSCREEN` is then the canvas width). It uses the active look: frame `0x191919`
  and black text. Its geometry matches the capture above. The text differs from native only in ClearType fringes,
  as the windowed bar's does. The caption's icon is left blank until the shell has its own (engine shell spec §5.5),
  since the game's is inside AGERC.DLL.
- **The strip's behaviour.** It shows from the top-edge dwell (the screen's top two rows, 100 ms ticks) or from
  Alt. It freezes the presented frame without holding the VM. It takes the keyboard. A later Alt or F10 tap starts
  its keyboard mode ("Native menu bar", Keyboard mode). A click elsewhere hides it and reaches the game. Its menu loop
  holds the VM as the bar's does. A command chosen from it keeps the frame frozen across the hide, so the strip is
  not in the frame 終了 dims.
- **Persistence.** `display:ScreenMode` is written at each switch. Native writes it at exit, and the final file is
  the same.
- **Not modelled.** The `ctx+0x55118` stamp is not modelled. The selection-fade setting is never touched, because it
  is a system-wide setting. The hover colours of the minimize and restore buttons (`0xe5e5e5`, pressed `0xcccccc`)
  are Windows 10's defaults, not captured from native.
- **Driver.** In fullscreen, the `menu` step uses the strip's menu, which must be showing. Natively the driver
  refreshes it with `WM_ENTERMENULOOP` and posts `WM_COMMAND` to the strip.
- **Scenario.** `tools/drive/scenarios/himegari-fullscreen.steps` passes on both targets: ﾌﾙｽｸﾘｰﾝ, the dwell, then
  ｳｲﾝﾄﾞｳ from the strip. Both `SYS4REG.INI` files end with `ScreenMode=1`.

### Native system-menu reentrancy guard — opcode `0x142` (2026-07-29)

`op_0x142_set_system_menu_actions_enabled@0x421fe0` is a direct one-operand setter for
`EngineCtx.system_menu_actions_enabled` (`ctx+0xa0d10`). `scene_context_init_reset@0x40b3b0` initializes
the field to one. CONFIG.BIN contains both shipped calls: `0x142(0)` is its first instruction, before any
settings data or UI is built, and `0x142(1)` runs after its last optional callback and immediately before
retained-object/surface/SFX teardown and exit.

The field is not consumed by the interpreter, renderer, gameplay logic, or persistence. AGE exposes it
through `AGE:IAGEService` virtual method `iage_service_query_system_menu_enabled@0x45f7c0` (vtable offset
`+0xb4`), which returns whether the field is nonzero. The shipped AGERC.DLL obtains that interface through
`GetClassObject("AGE:IAGEService")`. Its `agerc_refresh_system_menu_state@0x10002e10` calls the virtual
getter after refreshing the native menu's settings. A zero result applies `MF_GRAYED` through
`EnableMenuItem` to command ids `0x9c63`, `0x9c64`, `0x9c65`, `0x9c68`, `0x9c6c`, `0x9c6e`, and
`0x9c6f`; those entries launch settings/save-related callbacks and modal services that could otherwise
reenter configuration while CONFIG already owns the scripted settings UI.

The opcode is therefore a host-menu availability guard, not a configuration-persistence switch or input
lock for the game itself. A portable frontend without AGE's native AGERC menu can retain the flag for
lifetime/state parity and gate any future equivalent menu, with no immediate visual or headless effect.
It is an isolated implementation slice and has no dependency on the remaining opcode gaps. The handler,
IAGEService getter, AGERC menu refresher, and typed EngineCtx field are annotated in the saved Ghidra
programs.

The port retains the complete signed dword on `VirtualMachine`, initializes it to one, replaces it on each
`0x142` call, and restores one during opcode-`0x9` root scene-context reset. It calls no `IHost` service: treating
the flag as a game-input or persistence switch would broaden its native effect. The Godot frontend's menu refresh
reads it (`VmMenuState.SystemMenuEnabled`) and grays the same items, on the bar and on the fullscreen strip.

### System-menu top-edge reveal delay — opcodes `0x148` / `0x149` (2026-07-29)

`op_0x149_set_system_menu_show_delay@0x422050` replaces the complete dword at
`EngineCtx.system_menu_show_delay_ms` (`ctx+0x5511c`); the paired
`op_0x148_get_system_menu_show_delay@0x429c70` writes it to its output operand. Scene reset initializes
the field to zero. Himegari never calls the getter and has one setter site: SYSTEM4 writes 1000
milliseconds immediately before the unrelated `0x248(128)` tiled-surface configuration.

The only non-opcode consumer is `system_menu_top_edge_dwell_timer_tick@0x486280`, reached from the main
Win32 window procedure's `WM_TIMER` id 1 and repeated top-edge `WM_MOUSEMOVE` path. A pointer at client
y=0 or y=1 starts a 100-millisecond polling timer and captures `timeGetTime`; leaving those two rows
clears the timestamp and kills the timer. The tick reveals the menu only when:

- unsigned elapsed time is strictly greater than `system_menu_show_delay_ms`;
- neither the left nor right mouse button is held;
- the game is fullscreen (`display_windowed` is 0). A windowed game has the menu bar instead ("Native menu bar"
  above). An earlier revision said "windowed"; corrected 2026-11-05.
- AGERC host command `-1` reports no active host UI.

The unsigned comparison preserves the setter's complete dword: a negative script value becomes a very
long effective delay rather than being clamped. The independent Alt-key path calls the same reveal worker
immediately and does not consult this field.

`system_menu_strip_show@0x407bd0` freezes presentation, sets run-state bit `0x400000` (`TIMER_SHOWMENU`) and
calls AGERC host command 4, which shows AGERC's top-edge strip: a maximized captioned window as wide as the
screen, holding the title, the caption buttons and menu 110 ("Screen mode" above has its layout and messages).
`agerc_system_menu_strip_hide@0x10002a40` hides it and clears AGERC bit `0x10`, so a later dwell can reveal it
again. The handlers, timer/reveal workers, EngineCtx field, AGERC dispatcher/dialog/hide workers, and key globals
are named/commented in the saved Ghidra programs.

The VM retains the field as a `uint`, sign-extends the getter's dword into the existing script integer-cell
representation, and restores zero in the opcode-`0x9` scene reset. It makes no `IHost` call: the Godot frontend
reads `SystemMenuShowDelayMilliseconds` for its own dwell (`godot/Main.ScreenMode.cs`). Focused coverage includes
SYSTEM4's 1000-millisecond setting/getter pair, a high-bit dword round trip, and root-reload lifetime.

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
`0x223`; ambient cyclic/spritesheet pulses do not block. Click forcing applies to that complete finite
presentation set, not only `0x223`: the outer tick's run-state-`0x400` branch reads
`system:EffectSkipOnClick` and, when it is 1, consumes logical action 4, calls the same force-complete worker
as `0x243`, and renders the terminal retained frame ("Effect skipping and the input latch"). Ordinary object color/alpha and matrix fades therefore jump to their
endpoints together with type-0 surface commands. Movie-mask commands and `0x242`-detached or ambient channels
remain outside that completion set.
This is a native-evidenced scheduler correction, not a guessed duration sleep.

##### The opening render path is RETAINED, not immediate-mode (2026-07-08, ground-truth correction)

A working note in the animation slice mis-called the SC0000 opening a set of "immediate-mode slot-0 blits." That
was **wrong**, and it came from trusting our own `Ome.Cli gfx` oracle (which executes our VM and mis-labeled the
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

An operand under 10 never reaches the timer: the handler calls `kernel32!Sleep` with it and returns, so ROOM's
steady input loop's `sleep 0` gives up the time slice and the main loop goes on to its next iteration. The Godot host
formerly allowed a zero deadline, turning that loop into a free-running burst that exhausted the VM step guard and
showed `-end-`; it now waits at least one millisecond after the debug speed multiplier.

#### Short sleeps do not wait for the display (2026-11-07)

Native's main loop (`engine_main_tick_with_exception_policy@0x411840`) dispatches one op per iteration and calls
`gfx_render_frame` at the top of the next one when the frame is dirty. It never waits for the display. The engine
calls `timeBeginPeriod`, so `kernel32!Sleep(1)` lasts about a millisecond. A poll loop such as Kamidori FIELD's
(`get-input-type`, `sleep 1`, `jmp`) therefore wakes about once a millisecond. Op `0xcd`
(`op_0xcd_dispatch_mouse_callback@0x417e10`) fires the op-`0xcc` callback once `timeGetTime()` minus the last firing
reaches the interval, and re-arms from that moment, so a 50 ms callback runs about every 50 ms.

The port's `GodotAdvHost.Sleep` used to request a synchronized presentation on every sleep and wait for the
compositor to take it, which happens once per display frame. Each `sleep 1` then lasted up to a frame (16.7 ms at
60 Hz), and the 50 ms callback fired after three or four frames: every 50 or 67 ms. FIELD's callback
(`label_1498`) counts its calls in local `0x2f53` while nothing is selected, and draws the help overlay (texture
`0x41` at object `0xc350 + 0x7cf`) once the count passes 100 (`label_c4c6`). Native showed it about 5 s after the
last input; the port showed it about half a second later.

A sleep under 10 ms now requests presentation without waiting for it, and only waits its own real time. The
compositor still publishes whenever it finds the script parked in the sleep. Longer sleeps still synchronize as
before. Measured on Kamidori FIELD after a wheel input, sampling every 0.5 s: both targets showed the overlay in the
same sample, in two runs. The Himegari scenarios effect-waits, save-load, hover-after-click and latch-sleep still
pass on the port. Idle in FIELD, the port uses 9.2% of one core and native 4.7%.

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
  `scene_context_init_reset`@`0x40b3b0` (zeroes `0x53d14` + `0xa0ce4`, deletes the movie objects `ctx+0x52bd4[1000]`).
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
frame loop), or a wider memory dump that includes the mode object. *(Superseded: the loop is
`engine_main_tick_with_exception_policy@0x411840` in the /v2 image; see "Fast-forward pace".)*

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
UI scripts) before the scene — `Ome.Cli gfx --boot` and, until 2026-11-05, Godot `--boot` (both via
`GameSession`; Godot direct-scene runs are now hosted by SYSTEM4 itself). With boot,
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
  maxes a table to compute it). It does **not** loop/wait itself, but its window holds the next op-`0x21c` wait
  ("The anim-clock window holds the effect wait").
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
- **Surfaces** — textures in the gfx manager (`+0xa590[slot]` of `ctx+0x46614`), indexed by slot. A slot's movie
  object lives separately at `ctx+0x52bd4[slot]`. `gfx_op_0x1f8_create_surface` (`0x4222d0`) deletes that movie
  object and allocates a blank surface (`gfx_surface_create_blank`). `gfx_op_0x1f9_load_surface` (`0x422360`, op
  `0x1f9` set-texture) also deletes it, resolves `resId` via the SYS4INI resolver (`FUN_0044f390`) and loads the file
  into the slot's surface **with a colorkey/chromakey** (op arg 3 — never modelled before).
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

#### What a texture load costs (2026-11-06)

Op `0x1f9` loads its AGF synchronously, inside the op, in these steps:

1. `asset_open_indexed_entry@0x44f390` opens the catalog entry.
2. `asset_handle_seek_and_lock@0x44ef40` enters the entry's critical section and seeks to its offset.
3. `gfx_surface_load_asset@0x477c40` calls `gfx_surface_decode_and_create@0x474e90`. That reads the header, then
   inflates the information section (with the palette) and the pixel section through `lzss_4k_decompress@0x42f050`.
   A type-2 AGF also reads its alpha plane (`agf_alpha_read_header@0x4018b0`, `agf_alpha_read_plane@0x401960`).
   It then constructs the surface and uploads the pixels through the surface's virtual `+0x1c`.
4. `asset_handle_close@0x44f610` unlocks the entry (`asset_handle_unlock@0x44f000`) and closes the handle.

Native keeps no decoded image, so loading the same AGF again costs as much as the first time.

A Frida probe timed each op with `QueryPerformanceCounter`, hooking AGE.EXE functions only. It ran on the
`himegari-effect-waits.steps` run from boot to SC0000's page 14. The port times are `GodotAdvHost.SetTexture`'s
decode in the same run:

| Load (resource) | Native op | of which LZSS | Port, optimized | Port, Debug build |
|---|---|---|---|---|
| SO021 (`0x3323`, TITLE) | 14.38 ms | 7.64 ms | 15.98 ms | 50.99 ms |
| SO013A (`0x21`) | 2.48 ms | 0.96 ms | 2.34 ms | 7.89 ms |
| EV049AA (`0x23`) | 4.91 ms | 2.56 ms | 4.78 ms | 11.28 ms |
| EV052CA (`0x25`) | 4.70 ms | 2.39 ms | 5.37 ms | 10.80 ms |
| EV052DA (`0x27`, `SC0000@0x12970`) | 4.93 ms | 2.63 ms | 4.38 ms | 10.56 ms |
| AE001D (`0x29`, `SC0000@0x126e1`) | 3.52 ms | 1.72 ms | 3.95 ms | 13.07 ms |
| AE001D again | 3.60 ms | 1.77 ms | 0.00 ms (cached) | 0.00 ms (cached) |

The port decodes an AGF on its first load (`AgfDecoder`) and caches the RGBA image by packed id
(`GodotAdvHost.Decode`). With the JIT optimizing, each load is within about 1.6 ms of native's, and repeat loads cost
nothing. `drive.py port` runs Godot's Debug configuration, where the JIT optimizer is disabled unless it is started
with `--optimize` (`tools-reference.md`, "Port timing"). There the same decodes take two to three times as long. A standalone benchmark of `AgfDecoder` gives the same split: EV052DA takes
3.94 ms optimized and 9.20 ms in Debug.

#### Selected retained-object range transform — opcodes `0x229`/`0x22a`/`0x22c`/`0x22d`/`0x22f` (2026-07-21; corrected 2026-08-16)

`0x229` was formerly misclassified as a second per-object position setter. Native
`op_0x229_set_gfx_range_transform@0x423700` instead resets an embedded gfx-object record at retained-gfx
owner `+0x428`, writes `(first_handle,count)` to owner `+0x420/+0x424`, and writes operands 3..5 as that
embedded object's anchor at owner `+0x440..+0x448`.

On every render frame, `gfx_range_transform_sample_frame@0x476df0` samples the embedded object's ordinary
scale/rotation/translation channels into owner matrix `+0xb5b4`. `gfx_object_composite@0x47f650`
post-multiplies an object's normal matrix by this shared matrix only when its handle is in
`[first_handle, first_handle+count)`. The sibling setters are:

- `0x22a`: current scale, three integer percentages divided by 100;
- `0x22b`: current axis-angle rotation (present in the native dispatch table, zero Himegari corpus calls);
- `0x22c`: current translation in pixels;
- `0x22d`: delayed/duration scale target, using the embedded object's ordinary one-shot scale channel;
- `0x22e`: delayed/duration axis-angle target. SYS4422 handler
  `op_0x22e_set_gfx_range_rotation_target@0x423a40` uses integer operand fetches, while SYS4433
  `@0x421970` uses its float-aware reader for axis/angle; both call the matching
  `gfx_range_transform_set_rotation_target` worker and arm the embedded object's shared one-shot clock;
- `0x22f`: delayed/duration translation target. Its handler loads ECX with retained-gfx owner
  `ctx+0x46614`, and worker `gfx_range_transform_set_translation_target@0x472e90` writes the embedded
  object's start/delay/duration fields and target matrix. Operand 1 is delay, not an object handle.

FIELD's camera helper selects handles `[1,50000)`, anchors the transform at the current camera world
coordinate `(G[0x767e],G[0x767f])`, sets translation to `(400-camera_x,300-camera_y,0)`, and applies the
zoom percentage from `G[0xccc09]`. Thus the map layer is centered/scaled while handles `>=50000`—the dungeon
UI—remain screen-fixed. FIELD's sole `0x22d` call animates a zoom over 300 ms. LOOK reuses the immediate
camera helper. Across the corpus, `0x229` occurs 693 times in 309 scripts: 590 all-zero disables, 101
identity-range selections, and the two FIELD/LOOK camera selections. Correcting the contract therefore
removes spurious object-zero mutations without changing established ADV output.

Kamidori adds one shipped use of the previously dispatch-only range rotation setter:
`SC6400.BIN@0x4c33` supplies `(delay=0, duration=700, axis=(0,0,1), angle=0)`. Although that particular
target is visually neutral, executing it still preserves the native channel timing and range dirty state.

FIELD's minimap-click helper at `FIELD@0xb3fe` changes the camera world coordinate, then issues two
synchronized one-shot translations: `0x22f` pans the selected world range and `0x220` moves the ordinary
minimap viewport-marker object. The former was previously mistaken for a direct position write to ordinary
object handle 0, so the marker moved immediately while the world stayed fixed until the next camera-helper
rebuild snapped it to the new coordinate. Routing `0x22f` to the embedded range translation channel restores
the shared pan duration and keeps both representations aligned. The user confirmed the natural FIELD minimap
path now pans the world and viewport marker together on 2026-08-16.

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
| `0x22f` | `op_0x22f_set_gfx_range_translation_target` → `gfx_range_transform_set_translation_target` | set the shared selected-range **translation target** with independent delay/duration (embedded object target matrix `owner+0x5d4` = `obj+0x1ac`); FIELD pairs it with the minimap marker's ordinary-object `0x220` tween. |
| `0x229` | `op_0x229_set_gfx_range_transform` → `gfx_range_transform_reset` / `select_handles` / `set_anchor` | reset/select the shared **retained-object range transform**; not a per-object position setter (superseded finding above) |
| `0x239` | `gfx_op_0x239_set_srcrect_cell` → `gfx_worker_set_srcrect_cell` | finite one-shot **spritesheet-cell** channel (the fifth one-shot; op `0x21c` waits for it): delay/duration `obj+0x48/+0x5c`, total frames/columns `obj+0x238/+0x23c`, flags `obj+0x234`. After the delay, cell = elapsed × frames / duration, row-major. Flag bit 0 keeps the last cell at the end, otherwise the source rect collapses to empty; flag bit 1 draws the base cell during the delay, otherwise nothing. BTL's sword-clash intro (`BTL@0x539`, gated on `G[0xaba5d]==0`, so the debug menu's battle skips it) is `(0, 1000, 28, 7, 0)`; FIELD's unit walk (`0x50d2`) uses flags 3 |
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
`T(-anchor) * one-shot-scale * one-shot-rotation * translation * cyclic-scale * cyclic-rotation * cyclic-translation *
T(anchor)`. It remains frame-driven while visible and survives native-style object cloning independently
of the immediate and delayed one-shot scale fields.

**Cyclic-translation follow-up (2026-08-19):** opcode `0x235` is the fifth cyclic channel, not an
unknown SYS4433 addition. The independently inspected handlers have the same five-operand contract in
Himegari SYS4422 (`op_0x235_handler@0x423e40`) and Kamidori SYS4433
(`op_0x235_set_translation_cycle@0x421d10`): `(handle, period_ms, x, y, z)`. Their workers
(`gfx_object_set_translation_cycle@0x47f110` and `@0x485f80`) clear lazy start `obj+0x218`, store period
at `obj+0x22c`, and build a target translation matrix at `obj+0x290`. SYS4433
`gfx_object_compose_animated_transform@0x47a400` uses the same exact triangular blend as cyclic scale,
then right-multiplies the sampled matrix after cyclic rotation. The coordinates are raw translation units,
not percentages. Kamidori contains 28 calls across 12 story/presentation scripts. The port now retains,
clones, resets, persists, and samples this channel independently and passes it through both compositors.

**Cyclic-channel reset follow-up (2026-07-29):** opcode `0x230` is the shared reset for this looping
animation family. `op_0x230_reset_gfx_cyclic_animations@0x423ba0` takes one retained-object handle and
calls `gfx_object_reset_cyclic_animation_channels@0x47ee60`. The worker gets or creates the object, clears
active flag bit 2, and zeroes five start/period pairs:

| channel field | start | period |
|---|---:|---:|
| looping color | `obj+0x20c` | `obj+0x220` |
| cyclic scale | `obj+0x210` | `obj+0x224` |
| cyclic rotation | `obj+0x214` | `obj+0x228` |
| cyclic translation (`0x235`) | `obj+0x218` | `obj+0x22c` |
| looping source rectangle | `obj+0x21c` | `obj+0x230` |

It does not change the object's current/base transforms. Five DEBUGADV calls reset an effect object before
demonstrating a new looping asset; FIELD's sole ordinary call resets the temporary moving-unit handle
between its one-shot translation setup and `0x239` source-cell animation. This is a bounded retained-state
operation. The port now exposes the same get-or-create reset on `GfxState`: it disables cyclic color,
scale, rotation, translation, and source-rectangle sampling; clears all ten native timing dwords in the preserved
numbered-save record; and leaves base/current transforms,
cyclic targets, and finite one-shot channels intact.

#### Diagnostic accumulator opcodes `0x1b2`–`0x1b4` (2026-07-29)

The three remaining diagnostic-looking instructions are one native family. The real dispatch registrations
are `ctx+0x9b914 = 0x41f4b0`, `ctx+0x9b918 = 0x41a400`, and `ctx+0x9b91c = 0x419240`; the legacy Kelebek
labels do not identify these handlers correctly.

- `op_0x1b2_append_diagnostic_value@0x41f4b0` formats operand 1 through
  `vm_operand_format_as_string@0x41af90`. Integer operand forms become signed decimal text, while string
  forms return their byte strings; the helper also covers AGE's other operand families. The handler
  appends the exact returned bytes to the embedded 28-byte MSVC string at `ctx+0x6f880`, now
  `EngineCtx::diagnostic_text_buffer`.
- `op_0x1b3_append_diagnostic_newline@0x41a400` appends the two bytes at `0x57082c`, `0d 0a`, to that
  same accumulator. It neither presents nor clears anything.
- `op_0x1b4_show_and_clear_diagnostic@0x419240` passes owner HWND
  `EngineCtx::owner_window_handle` (`ctx+0x54fe8`), the accumulator's current byte pointer, and flags
  `0x10004` through `engine_host_show_message@0x403820`. The thunk loads
  `EngineCtx::host_ui_interface` from `ctx+4` and dispatches virtual slot `+4`; `ctx+0x54fe8` is an
  explicit argument, not the interface object.
- The installed default vslot, `default_host_show_message@0x431520`, writes the raw diagnostic between
  `"\r\n致命的エラー[\r\n"` / `"]\r\n"` trace markers, then calls
  `age_show_message_box@0x409370`. Flag `0x10000` appends the current interpreter coordinate using
  `"\n\nデバック情報：\nFILE=%s ADDRESS=%X LINE=%d COMMAND=%s(%d) DEPTH=%d\n"`. Low mode `4`
  formats body `%s%s`, selects caption `エラーが発生しました`, and invokes USER32 `MessageBoxA`
  synchronously with style `0x40` (`MB_OK | MB_ICONINFORMATION`) and the supplied owner HWND. The
  opcode ignores the `IDOK` result and only then erases the whole string with
  `msvc_string_erase(buffer, 0, 0xffffffff)`.
- The optional command-metadata table at `ctx+0x6ddac` and per-frame source-line maps at
  `ctx+0x6f798` are both zeroed by the constructor. Exhaustive immediate-offset xrefs show reads plus
  that initialization only—no release-image writer—so Himegari deterministically emits
  `LINE=-1 COMMAND=-(436)` for `0x1b4`. `FILE`, dword `ADDRESS`, and zero-based frame `DEPTH` remain
  live values.

Himegari contains three `0x1b2` sites. FIELD appends the literal invalid-mutual-destruction-state text and
then CRLF. SYSTEM4 appends the literal invalid-execution-mode prefix, appends global
`system_flow_request` in decimal, presents and clears the assembled diagnostic, then appends CRLF to the
now-empty buffer. The trio therefore owns three effectful gaps totaling six instructions and is a bounded
next implementation slice; native ordering, including SYSTEM4's apparently odd post-clear newline, should
be preserved. A compatible host seam therefore needs synchronous modal presentation on the UI thread and
the current script coordinate; it is not a general shell-command interface.

The port implements the trio with a `GameSession`-shared `DiagnosticOutputState`. Godot marshals the
completed prompt to the main thread through `OS.Alert` and parks the VM until dismissal; headless
`CaptureHost` records the same effect. The accumulator clears after the synchronous call returns, so
FIELD's unpresented line and SYSTEM4's unusual post-clear CRLF retain their native lifetime and ordering.

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
  `ctx+0x6dbd4`, now named `adv_read_skip_state`; the text/label/wait handlers maintain it from
  `message_ReadTextSkip` plus per-PC read history, and the menu bar's 既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ toggle
  (`adv_toggle_read_skip_and_refresh@0x406cd0`) re-checks it. It is not
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
mute/music/SE/voice routing. Without one, `set:DependMovieSound` (SYS4INI `DEPENDMOVIESOUND`, registered default 1)
picks the route by the same numbering, with 4 the movie category; any value outside 1..4 is silent
(`movie_sound_route_from_flags@0x405490`; SYS4433 `0x405200`). The movie then plays at master × that category's
volume (`sound_route_volume@0x407950`) and is silent while the category's route is off
(`sound_route_is_enabled@0x405420`). Himegari's 1 is music and Kamidori's 2 is SFX; Kamidori's LOGO and OP pass
`0x20004`, so they follow music. The port resolves the same category (`MovieSoundRoute`) and plays the movie on that
category's Godot bus; until 2026-11-07 it put every unflagged movie on the Movie category. Operand 4 is stored at movie object
`+0x42c` as a millisecond start delay: `movie_start_pending_after_sync_delay` records `timeGetTime()` on the
first service tick and starts the graph once `current - start >= operand4`. It is not a device mask or movie
duration. Replacing or releasing the owning surface stops the graph and detaches the renderer. The corpus has
25 sites: 24 pass literal zero, while BTL supplies `battle_animation_effect_start_delays_ms` for the effect
(`BTL@0x2b21`, the same delay its sprite-sheet effects give op `0x239`). Op `0x241` takes the same delay. The
port passes it as the host's start delay (2026-11-04): the decoder opens at once, so op `0x23f` still reads the
stop time immediately, but no frame is taken, and so no audio or presentation clock starts, until the delay has
passed. As natively, the surface keeps its created contents until then, since a stopped graph prerolls nothing.

Graph construction/open is synchronous, but playback and sample delivery are asynchronous. The handler
returns normally and the interpreter advances one instruction: at SC0000 `0x13c8`, the native operand
capture evaluates `(0x33, 0, 2, 0)` and the next executed bytecode is `0x13d1`. (On a New Game from the title the
surface is 5, not 0: see "SC0000's chapter movie plays on surface 5", 2026-11-07.) The movie therefore does
not itself block the VM. SC0000 prepares additional static layers, then reaches `0x21c` through
`label_1235a@0x1574`. That opcode sets run-state bit `0x400` and yields the interpreter; the presentation
service continues sampling the retained movie until DirectShow EOF, after which the following script
cleanup releases it. The static preparation before `0x21c` is not a movie teardown boundary.

**What the open costs (2026-11-06).** Nearly all of op `0x236`'s time is `IGraphBuilder::Render`, called at `0x46426c`
on the async reader's output pin. That cost belongs to the DirectShow filters installed on the machine, not to AGE. A
Frida probe hooked combase and quartz only and timed SC0000's movie at `0x13c8` (resource `0x33`):

- On this machine LAV Filters is installed. Render tries LAV Splitter first (`{171252A0-8820-4AFE-9DF8-5C92B2D66B04}`),
  whose FFmpeg stream probe ran for about 5.15 s. The graph then fell back to the Microsoft MPEG-1 Splitter, the MPEG
  video and audio decoders and the DirectSound renderer in about 20 ms. The VM stayed at `0x13c8` for 4.66 s.
- With LAV Filters' CLSIDs refused (`CoCreateInstance` answering `REGDB_E_CLASSNOTREG` in the game's process only),
  Render took 3 ms. The VM left `0x13c8` after 10 ms and reached the 750 ms sleep at `0x16cb` 14.03 s after the
  page-17 advance. The port reaches it at 14.07 s.
- The boot movie's Render took 34 ms with LAV and 8 ms without it.

The port opens the movie at once and does not model a codec pack's probe. Native timings on a machine with
third-party DirectShow filters include their cost at every movie start.

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
FFmpeg/audio-clock path described in `docs/platform-portability.md`. *Superseded 2026-11-07:* the port no longer keeps
a movie bound past a surface release, and the movie plays on surface 5, not 0. See "A surface release deletes its
movie" and "SC0000's chapter movie plays on surface 5" under "Movie flags decide the effect wait".

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

### Movie playback-rate opcode `0x246` (2026-08-19)

The exact ABI is `set-movie-playback-rate(surface_slot, rate_percent)`. Independent dispatch-table recovery
finds matching handlers in Himegari SYS4422 (`0x424a80`) and Kamidori SYS4433 (`0x4228d0`). Each records a
five-dword instruction, fetches the surface slot, and returns without effect if the corresponding
`EngineCtx` surface pointer is null. Otherwise it fetches operand 2, divides the signed integer by the
double constant `100.0`, dereferences `CMovieToTexture+0x414`, and calls vtable slot `+0x38` with the
resulting double.

The interface is the same `IMediaPosition` proven above. After inherited `IUnknown` and `IDispatch`, its
slots run from `get_Duration@+0x1c` through `put_StopTime@+0x2c`, `get/put_PrerollTime@+0x30/+0x34`, and
`put_Rate@+0x38`. Opcode `0x246` therefore sets the live graph's playback-rate multiplier; it is not a
battle-state mutation. The DirectShow `HRESULT` is ignored. Kamidori's complete corpus has one call in
`BTL.BIN`: after creating 512x200 surface 44 and starting a selected battle-effect MPEG on it, the script
calls `(44, 200)` before beginning the effect's timed callback sequence.

The portable host resolves the surface to its instance-keyed movie playback and applies the 2.0 multiplier
before the first frame leaves preroll. Video-only FFmpeg streams divide frame and completion deadlines by
the positive forward rate. Audio-bearing streams set Godot's `AudioStreamPlayer.PitchScale`, so the sound
device consumes PCM and advances the decoder's external video clock at the same rate. Missing movie bindings
remain no-ops. Focused tests cover exact percent conversion, missing-graph behavior, and the halved video
frame/completion deadlines.

### Current rotation query opcode `0x227` (2026-08-19)

Opcode `0x227` is the retained-object rotation counterpart to current-scale query `0x226` and
current-translation query `0x228`. Its six-operand contract is
`(out_failure, handle, out_axis_x, out_axis_y, out_axis_z, out_angle_degrees)`. On a found object it
writes failure zero and returns the stored **current** axis-angle fields at object
`+0x1ec/+0x1f0/+0x1f4/+0x204`; it does not read delayed target axis `+0x1f8..+0x200` or target angle
`+0x208`. A missing handle writes failure one and leaves all four value outputs untouched.

Himegari SYS4422 installs `op_0x227_query_rotation_current@0x42a2e0`, which calls
`gfx_object_query_rotation_current@0x47cd20`, explicitly converts each returned float toward zero with
`__ftol2_sse`, and passes the integer to the ordinary operand writer. Kamidori SYS4433 installs the same
logical handler/worker at `0x42b1a0`/`0x484510`, but writes the values through the revision's separate
`vm_operand_write_float@0x426c00`. That helper preserves a float for float destinations and performs the
conversion when the destination is an integer. This is a concrete SYS subrevision implementation/ABI
detail even though it does not alter any observed game result: all twelve Kamidori sites use global-integer
destinations, while Himegari has no decoded call site.

Kamidori uses six parallel calls in each of `SC0600.BIN` and `SC2060.BIN`, querying presentation-object
handles before deriving their next one-shot rotation targets. The portable VM exposes the current retained
axis-angle independently of its target, truncates toward zero for these integer outputs, and preserves the
missing-object outputs. Focused tests cover current-versus-target separation, positive and negative
fractional truncation, and the missing-object contract.

### Retained graphics-object swap opcode `0x214` (2026-08-19)

Opcode `0x214` takes two retained graphics-object handles and exchanges their complete records. Matching
handlers are `op_0x214_swap_gfx_objects@0x423170` in Himegari SYS4422 and `@0x4211c0` in Kamidori
SYS4433. Their workers (`gfx_object_swap_records@0x47e310` and `@0x485000`) implement the same four
presence cases:

- both found: copy the complete second record over the first and the saved first record over the second;
- only the first found: insert its complete record at the second handle, then erase the first;
- only the second found: insert its complete record at the first handle, then erase the second;
- neither found: leave both absent.

Equal handles are unchanged, and every path marks retained graphics dirty. The record size itself reflects
the SYS subrevision: SYS4422 copies `0xb5` dwords (`0x2d4` bytes), while SYS4433 copies its expanded
`0xb9`-dword (`0x2e4`-byte) record. The logical opcode contract does not otherwise change.

Kamidori has ten sites, one each in `SC0000`, `SC0710`, `SC0800`, `SC1050`, `SC1360`, `SC3030`,
`SC3510`, `SC5550`, `SC6400`, and `SP6604`. Every site runs two swaps over parallel object-handle ranges,
then immediately swaps the same two rows in the script's presentation metadata table. That corroborates a
complete presentation-object reorder rather than a surface-resource swap. The portable retained model swaps
or moves every semantic field plus preserved native persistence bytes, maintains its handle-sorted compositor
index, and invalidates presentation in all four cases. Focused tests cover both-found full-record exchange,
one-found movement, and two-missing behavior.

### Patterned retained-range transition opcode `0x24f` (2026-08-19)

Opcode `0x24f` is the type-2 patterned sibling of type-0 alpha transition `0x223` and type-1 movie-mask
transition `0x24d`. Its ten operands are `(command_key, target_slot, range_a_start, range_a_count,
range_b_start, range_b_count, mode, pattern_thickness, delay_ms, duration_ms)`. Matching native producers
are `op_0x24f_queue_patterned_surface_transition@0x425460` ->
`gfx_queue_patterned_surface_transition@0x47f7e0` in Himegari SYS4422 and `@0x422ed0` -> `@0x486800`
in Kamidori SYS4433. Both write the same 0x40-byte command prefix: type 2 at `+0`, delay/duration at
`+8/+0xc`, target surface at `+0x10`, the two ranges at `+0x14/+0x1c` and `+0x18/+0x20`, mode at
`+0x34`, pattern thickness at `+0x38`, and the command key at `+0x3c`.

The shared Himegari consumer `retained_gfx_surface_command_composite@0x47fbc0` establishes the visual
contract. It renders both retained ranges into scratch surfaces `0x24` and `0x25`, finds the ordinary
retained object at `command_key` to obtain the clipped effect rectangle, clears `target_slot`, and combines
the two captures according to the sampled delay/duration progress. Modes 0..3 are quantized wipes from
left, right, top, and bottom. Modes 4..7 reveal equal vertical or horizontal strips in parallel, and modes
8..11 stagger the same strips in the corresponding four directions. A positive thickness with a mode
outside 0..11 logs an error and returns without creating the command. A nonpositive thickness logs an error
but creates an immediate mode -1/thickness-1 fallback.

Kamidori has ten byte-identical copies of one story-portrait helper in `SC0010`, `SC0050`, `SC0100`,
`SC0110`, `SC0150`, `SC0200`, `SC0250`, `SC0300`, `SC0350`, and `SC0400`. Each transitions between two
single-object portrait ranges into a newly created surface, then binds that surface to the command object.
The helper starts from a nominal value 64 and derives a pixel thickness from the current clipped 1024x576
extent before dispatch. The portable model retains both ranges, mode, thickness, timing, validation,
force-completion, and command-object lifetime. Godot currently presents the same timed endpoints through a
crossfade approximation; exact clip-mask geometry is isolated to a future host-compositor refinement.
Focused tests cover timeline sampling, command attachment/removal, exact SYS4433 dispatch, and native
validation fallbacks.

### Integer LIFO family `0x137`-`0x139` and positioned movie playback `0x241` (2026-07-29)

`op_0x137_reset_int_stack@0x421940` is the LIFO counterpart to the implemented integer FIFO family
`0x132`-`0x134`. It destroys the pointer selected from `EngineCtx+0x55158`, allocates a 0x14-byte object,
and calls `int_stack_construct@0x407410`. The object starts with a 0x100-dword buffer, a 0x100-dword
growth quantum, and top index -1. The unobserved paired operations are
`op_0x138_push_int_stack@0x421a40`, whose `int_stack_push@0x408860` grows and appends, and
`op_0x139_try_pop_int_stack@0x4297a0`, which removes `data[top]` and writes a success flag.
On an empty pop the release handler still writes the reused incoming `EngineCtx *` local to `out_value`;
that output is implementation garbage unless success is nonzero.

The storage has a native bounds bug worth preserving as documentation, not as portable memory corruption.
Construction, scene reset, and destruction iterate exactly ten queue pointers at `+0x55130` followed by ten
stack pointers at `+0x55158`. All six queue/stack handlers admit unsigned ids through 10. Queue id 10
therefore aliases stack slot 0, while stack id 10 aliases the first dword of the numeric-glyph-style table
at `+0x55180`. Himegari's queue and stack sites use only id 0. Its sole `0x137` is
`CALLBACK_LOAD@0xf2`, immediately before the helper that rebuilds the eight retained ADV layers; no
Himegari script calls `0x138` or `0x139`.

The portable VM implements the complete family as eleven isolated logical stacks. All slots exist at
scene entry, root reload reconstructs them empty, and `0x137` replaces only the selected slot. This keeps
the handler-admitted id-10 ABI without allowing it to overwrite numeric-glyph state. Empty `0x139`
preserves `out_value` while writing success zero, because reproducing the native leaked `EngineCtx *`
would expose meaningless host-dependent data rather than a script-observable engine value.

`op_0x241_play_movie_to_surface_at_position@0x4247e0` is an exact five-operand extension of `0x236`:

`(movie_resource, surface_slot, movie_and_sound_flags, start_delay_ms, position_ms)`.

It reuses the same movie-to-texture allocation, existing-render-target requirement, packed asset open,
sound routing/volume setup, and nonblocking `movie_play_configure` lifecycle. Between graph open and
configuration it calls `IMediaPosition::put_CurrentPosition(position_ms / 1000.0)`. The start-delay
operand remains distinct and is forwarded unchanged after the seek.

The sole call, `CALLBACK_LOAD@0x189`, clarifies its purpose. Ordinary ADV movie setup writes opcode
`0x23f`'s stop position into the eight-cell global `adv_layer_movie_stop_time_ms` (`0x329e`) immediately
after `0x236`; the load callback passes that value minus one to `0x241`. This is terminal-frame
reconstruction after a numbered load, not restoration of a separately sampled live playback cursor.

The portable implementation delegates graph/surface ownership to the existing `0x236` lifecycle and adds
one synchronous initial-position boundary before decoder workers start. FFmpeg ABI v3 seeks the independent
video and audio demuxers to their preceding indexed packet. Managed video preroll retains the latest frame
at or before the requested position and queues the first later frame, which is essential for
`stop_time_ms-1` to restore the terminal image rather than run off EOF. Audio preroll drops complete earlier
blocks, trims a straddling block at sample precision, and rebases its remaining timestamps with video
pacing/completion to zero. A real archived `CHAPTER.AGF` regression proves both demuxers seek near 11 seconds;
synthetic regressions pin active-frame selection, terminal-frame retention, audio trimming, and pacing.

### Movie-mask transition `0x24d` and tiled-surface edge `0x248` (2026-07-29)

The adjacent opcode numbers do not represent one subsystem. `op_0x248_set_tiled_surface_edge_length`
(`0x424af0`) only copies its operand into `g_nTiledSurfaceEdgeLength` (`0x5b15b0`). The global's 30
references are confined to `gfx_tiled_surface_create`, `gfx_tiled_surface_blit`,
`gfx_tiled_surface_rect_op`, and `gfx_tiled_surface_upload_agf`. Those workers partition a logical mode-1
surface into `ceil(width / edge)` by `ceil(height / edge)` ordinary texture tiles and clip logical
operations across them. SYSTEM4's sole call sets the edge to 128 immediately after opcode `0x149`.
This is legacy texture-allocation configuration for `0x249`; a backend with contiguous RGBA surfaces may
retain the value for state parity without physically reproducing the tiling.

`op_0x24d_play_movie_mask_transition` (`0x424db0`) is instead a 12-operand presentation operation:

1. It allocates or reuses the scratch movie-to-texture object in operand 2 and opens operand 10 as a
   universal packed resource. It routes sound with native flag value 6 and configures operand 11 as the
   movie's start delay.
2. It reads `IMediaPosition::get_StopTime` and sets `put_Rate` to
   `(native stop time in milliseconds) / operand 12`, retiming the complete movie to the requested
   transition duration.
3. `movie_texture_allocate_transition_mask@0x415d90` allocates an operand-7 by operand-8 byte mask and
   initializes it to zero when operand 9 is one, or 255 otherwise. While mask mode is active,
   `movie_texture_renderer_receive_sample@0x4628d0` does not upload ordinary color pixels: for every
   bottom-up RGB24 pixel it copies byte `+1`, the decoded **green channel**, directly into the mask.
4. `gfx_movie_mask_transition_register@0x47f560` records a type-1 retained command keyed by operand 1.
   The record identifies the old/source range (operands 3 and 4), scratch movie surface, mask mode,
   start delay, and destination rectangle `{x=operand 5, y=operand 6, width=operand 7,
   height=operand 8}`.

`retained_gfx_surface_command_composite@0x47fbc0` captures the old and new object sets for type 1.
`gfx_apply_movie_transition_mask@0x476e50` preserves source RGB while applying each mask byte to packed
pixel alpha inside the requested rectangle. The exact 32-bit operation leaves `source_argb & 0x00ffffff`
unchanged and sets the output alpha byte to the high byte of
`((source_argb >> 8) * mask_byte)`; this includes the native packed-integer carry from the high color bytes
and should not be rounded into a generic `alpha * luminance / 255` formula. The 16-bit path performs the
analogous 4-bit-alpha operation, and pixels outside the rectangle copy unchanged. The compositor keeps its
blocking/redraw state set while the scratch movie exists. At completion it flips the mask to the terminal
fill (mode 1: 255; other modes: 0), clears the active command, and releases the blocking lifetime. Operand 1
is the immediately preceding new `draw-texture` range key; operand 3 is the old range that the script erases
after its `0x21c`/`0x224` presentation boundary.

Both shipped calls are in DEBUG.BIN. They use scratch slots 45/46, old ranges 10/20, new keys 11/21,
mode zero, zero delay, and a 1000 ms duration. Resource `0x325e` is `TEST.AGF`; its `00 00 01 BA`
header identifies an MPEG program stream despite the extension. One call uses a signed x coordinate of
-184, confirming that the position operands are not unsigned dimensions. Retail TITLE cannot enter this
post-exit developer menu, although the port deliberately exposes DEBUG through its F4 diagnostic route.

The implementation boundary is consequently larger than ordinary movie playback or a scalar crossfade,
but no mask heuristic remains: the source is specifically the decoded green byte. It requires
movie-frame-to-mask publication, retained-range capture, per-pixel alpha composition, and
movie-completion-driven blocking cleanup. The handlers, sample worker, compositor, and exact helpers are
named and commented in the saved `/v2` Ghidra image.

**Port implementation (2026-07-29):** `IHost.PlayMovieMaskTransition` carries the complete
twelve-operand request. At dispatch, Godot snapshots the old/source retained range through
`RetainedSurfaceRasterizer` into the script-created scratch surface; the immediately preceding
`draw-texture` command key therefore displays that captured range without a second compositor model.
Mask playback remains instance-keyed in the ordinary movie registry, but frame publication is redirected:
`MovieMaskSurface.ExtractGreen` copies logical top-down RGBA green bytes into the requested mask dimensions,
and `MovieMaskSurface.Apply` republishes an immutable dynamic-surface snapshot with the exact packed-ARGB
multiplication above. Signed rectangles clip mask and destination coordinates together, while pixels outside
the rectangle remain the captured source.

The initial mask is published before decoder adoption. `MovieRuntime` defers its first-frame take for the
requested start delay, so decoder pacing cannot run ahead during that interval. Its optional presentation
duration scales frame deadlines by `requested_duration/native_stop_time` and holds the terminal frame until
the requested endpoint; this is the managed equivalent of `IMediaPosition::put_Rate`. Completion publishes
the mode-specific terminal fill, marks the type-1 command complete in `GfxState`, and only then releases
opcode `0x21c`'s blocking lifetime. The existing click/skip completion path deliberately affects only type 0.
Because the result is an ordinary dynamic RGBA scratch surface, both the software oracle and accepted Godot
GPU renderer consume the same pixels; no backend-specific mask shader or approximation is involved.

**`0x248` port implementation (2026-07-29):** `GfxState` retains the complete signed dword with native
zero initialization. The setter neither publishes a retained mutation nor rebuilds surfaces that already
exist, and scene-context reset leaves the process-global-equivalent setting intact. Opcode `0x249`
continues to create one contiguous portable RGBA surface; only AGE's obsolete physical D3D partitioning is
omitted. Focused tests cover SYSTEM4's value 128, mode-1 texture loading, signed replacement, and reset
lifetime.

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
   `OME_DIAG_SETTEX` trace):** every `set-texture` slot = `G[0x62452]`, written by `query-gfx-object?`
   (`0x215`) which returns **-1** for the (correctly-unregistered) CG/effect handles → the fallback at
   SC0000 `label_12649` does `G[0x62452] = lookup-array-2d(rec[s3]=G[0x3239], G[0x62450], 3, 0)` = **0**
   because the slot table `rec[s3]`/`G[0x3239]` is **empty**. That table is filled by `call label_125bd`
   (SC0000 `0x50f`, slots 4..11), which is reached **only through the scene-coroutine framework** — the
   `G[0xaba5c]` gate (`0x450`) + op `0x140` (`u0041F9C0`, coroutine LABEL/yield `"LABEL" "J"` @ `0x46d`).
   **Fixed by the bounded scene-coroutine host model:** `0x140` runs the setup body once, `label_125bd`
   fills the eight slot records, and SC0000 resource `0x23` loads into assigned slot 5 instead of slot 0.
   This was not a compositor/z-order/blend bug. Diagnostics: `OME_DIAG_SETTEX=1` env → VM logs each `set-texture`
   slot operand + `query-gfx-object?` result.

### Differential offset-path oracle — engine-vs-VM control-flow diff (2026-07-09)

**Method (lever #3 of the RE-front-loading program).** Run the same scene in the real engine and our C#
VM and diff the **executed script-offset path**. Both run the same bytecode, so the opcode at each offset
is static (from disasm); the first place the two offset sequences differ is exactly the branch/opcode/state
we modeled wrong — cheaper and higher-signal than diffing effects, and precisely where the render-drift
walk-backs lived. Tools: `tools/frida/trace_engine_ops.py` (engine capture) · `Ome.Cli trace <SCENE>
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
and the new record's entry on the next poll. At the ADV text wait (`adv_input_service_poll@0x411230`,
run-state `0x80000000`) a click takes operand 7 through `input_hotspot_current_click_callback@0x404f70`, which
leaves the registry and its hovered record as they are. Every dispatch from that wait first pushes the waiting
op's offset (`vm_return_stack_push_pc(-3)`), so the callback's `ret` runs the wait again. The interpreter
temporarily redirects the current script PC to these callbacks; registration itself returns normally. The
registry-resetting `input_hotspot_take_click_callback@0x404330` belongs only to the modal waits (see "Modal
hotspot waits" below). Companion op `0x97` finds an identical registered
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
frame even though the enclosing wait remains static. (Correction, 2026-11-04: no native registration pass
follows an action callback. The ADV text wait never clears the registry on a click, so retaining the
definitions is the native behaviour itself; see the next paragraph.)

**Hover after an activation (native-driven 2026-11-04, `tools/drive/scenarios/himegari-hover-after-click.steps`).**
A clicked control stays hovered until the pointer leaves it. After a click on read-skip, its tooltip stays hidden
while the pointer rests on the control or moves within it. A direct move to the neighbouring control shows that
control's tooltip, and moving back shows read-skip's again. The port had cleared the hovered record on activation,
so the re-arm after the callback ran the enter callback again under a still pointer and redrew the tooltip at once.
`HotspotRegistry` now keeps the hovered record across an activation.

The mechanism is simple: nothing is reset. A Frida probe on native AGE (2026-11-04, SC0000 page 1) logged the
registry at ctx+0x5248 through the same scenario:
- **Hover.** Read-skip's enter (`label_295`) is dispatched from `0x4114ed` with a return push of -3.
- **Click.** It goes through `input_hotspot_current_click_callback@0x404f70` (push -3 from `0x4115ae`) and returns
  `label_2ab`. The record count stays 8 and the hovered record `+0xefc` stays 3. `input_hotspot_take_click_callback`,
  `input_hotspot_reset` and `input_hotspot_register_rect_callbacks` are not called.
- **After the callback.** Its `ret` re-runs `wait-for-input@0x83c`. The callback's one-pixel cursor jiggle keeps
  the hit on record 3, so `input_hotspot_poll_hover_callback` sees no transition.
- **Neighbour.** The move to message skip dispatches read-skip's leave (`label_2a0`), then message skip's enter
  (`label_247`) on the next poll.

**The hit and the hovered record (native-traced 2026-11-05).** The registry keeps two indices, and they change at
different times.
- **The current hit** (`+0x74b0`, ctx+`0xc6f8`) is the record under the pointer as of the last hit test.
  `input_hotspot_update_cursor_hit` runs from only two places:
  - **`WM_MOUSEMOVE`.** `age_main_window_proc` calls `input_mouse_move_update_hotspot_hits@0x485db0` (call at
    `0x48690f`) with the message's client point, and only when that point differs from the last move's. The hit test
    runs whether or not a wait is polling, so it also covers a running callback.
  - **The first arm after a registration.** `input_hotspot_arm_cursor_tracking@0x404230` hit-tests `GetCursorPos`.

  Nothing else re-tests. A button press hit-tests nothing, and the click callback is the current hit's
  (`input_hotspot_current_click_callback@0x404f70`). With no hit, the click advances the page.
- **The hit is cleared (−1) by:**
  - op `0x90` (`op_0x90_handler@0x41fc80`, which also clears the pending enter `+0x74a8`);
  - op `0x7c` (`op_0x7c_handler@0x417cb0`, for this registry and the op-`0x95` choice table at ctx+`0xc72c`);
  - the bound-action and Auto click dispatches in `adv_input_service_poll`.
- **The hovered record** (`+0xefc`) follows the hit only when a wait polls (`input_hotspot_poll_hover_callback`). A
  difference dispatches the old record's leave, then the new record's enter on the next poll.
- **Op `0x7c` and a still pointer.** Op `0x7c` ends both coroutine handlers of the hide window. After it, a pointer
  that has not moved hits nothing: the wait's next poll runs the hovered control's leave, which clears its tooltip,
  and a click on the same spot advances the page until the pointer moves.
  - Native-driven on SC0000 page 4 (`himegari-hide-window.steps`): Space hides and restores under a pointer resting
    on the hide control, the tooltip goes, and a click there advanced to `0x913`.
  - From the control itself, the callback continues at `0x30d` with its one-pixel cursor nudge. Op `0x10a` calls
    `SetCursorPos`, and Windows answers with a `WM_MOUSEMOVE` at the new point, which hits the control again.
  - `engine_main_tick_with_exception_policy` pumps messages every third loop iteration (counter ctx+`0xa0cec` against
    mask ctx+`0x54ff4` = 4). Run-state 0 dispatches one op per iteration, so the move is handled before the callback's
    `ret` re-runs the wait. That wait finds hit = hovered, and no tooltip returns.
- **Port.** `HotspotRegistry` keeps both indices:
  - `MovePointer` is the `WM_MOUSEMOVE` path, and `VirtualMachine.UpdatePointer` skips an unchanged point.
  - The first `Arm` after a registration hit-tests.
  - `InvalidateHit` is op `0x7c`.
  - `Activate` takes the current hit.
  - Tests: `HotspotInputTests.CoroutineResume_ClearsTheHitSoAStillPointerLeavesAndItsClickIsNotConsumed` and the
    real-SC0000 hide-window theory.
- **Driver.** The native driver's `SetCursorPos` replacement now posts that `WM_MOUSEMOVE` too
  (`tools/drive/native-agent.js`). Without it, driven native never re-hit-tested after the nudge, and a move inside the
  control brought the tooltip back.

`0x1b6`/`0x1b7` are implemented as VM service state for the first Auto action bridge; timed automatic page
advance remains separate follow-up work.

**Modal hotspot waits (ops `0x91`, `0x92`, `0x95`, `0x96`; static, 2026-11-04).** These waits reset the registry on
a click. Neither game's scripts use them, and the port stops on them as unsupported opcodes.
- **`0x91`/`0x92` (main registry).** They keep the PC on the op under run-state `0x800000`.
  `input_hotspot_wait_poll@0x408710` serves that bit:
  - A hover transition pushes the op's own offset, so its callback returns into the wait.
  - A click goes through `input_hotspot_take_click_callback@0x404330`, which resets the registry, and jumps
    without a return push. The click callback replaces the wait, and its `ret` pops the enclosing call.
  - Operand 1 sets the keyboard row stride (`+0xf00`) used by `input_hotspot_keyboard_navigate@0x404010`. Op `0x94`
    passes 10000.
  - Op `0x92` adds a callback (operand 2, table `+0x74ac`) that runs on each poll with no hover transition or click.
  - Op `0x93` run without an op-`0x94` arm sets a pending cancel (`ctx+0xc6e0`), and the next `0x91`/`0x92` falls
    through.
- **`0x95`/`0x96` (choice table).** They are the same pair over a second, engine-drawn table at ctx+0xc72c with
  the same layout:
  - `input_hotspot_arm_highlight_table@0x4042b0` lays out the records' texts in ADV text slot operand 1
    (`adv_text_layout_choice_table@0x457e20`).
  - `adv_input_dispatch_callbacks@0x408530` (run-state `0x10000000`) moves a `0x60`-alpha highlight over the hit
    (`hotspot_highlight_move@0x450070`).
  - The click and poll-callback paths match `0x91`/`0x92`.
  - Op `0x96` is the table's `0x93`.

The opcode records are in `vm-map/opcodes.toml`.

**Port correction from manual validation (2026-07-18).** Merely implementing op `0x90` was insufficient in
the cold single-scene harness. The visible SO001 strip is drawn independently, while `jcc@0x8d` skips its five
rectangles when inherited `G[0x6c1]` is zero; the existing trace did exactly that. Even with the native value
one, `cancel-hotspot-wait@0x622` clears the early pass before page-one `wait-for-input@0x83c`, and the native
ADV coroutine later republishes it. Godot now seeds `adv_chrome_enabled=1` as part of the same bounded
SYSTEM4 chrome bootstrap as SO000/SO001. The blocking VM retains canceled definitions only as inactive
coroutine templates, replaces them if script registration runs first, and otherwise re-arms them at a stable
message wait. A full SC0000 regression proves the real History enter callback and retained-frame publication,
not just a synthetic registry path.

**Correction (native-traced 2026-11-04).** Native New Game does not run `cancel-hotspot-wait@0x622` before page 1,
so there is nothing for an ADV coroutine to republish:
- The Frida probe saw the registry written once, by `label_8c` called from `0x7e` (8 records, armed at `0x19d`),
  and never reset before `wait-for-input@0x83c`.
- The executed-offset trace (`tools/frida/trace_engine_ops.py`) takes `0x450 → 0x457 → 0x45e → label_71b`. So
  `G[0xaba5c]` is not 1 there, and the loop body `0x462..0x711` that holds `0x622` does not run.

That loop is TITLE's developer entry ("Native ADV hosting and Himegari's developer entry" above). Retail never runs
it, and no retail text wait follows a `0x93` without a re-registration in between. On its developer path, native
reaches page 1 with the registry empty. The port once forced that body for a cold run whose root was the ADV scene
(`VirtualMachine.Run` set `G[0xaba5c]=1`) and re-armed the canceled definitions as a template. Since 2026-11-05
direct-scene runs are hosted by SYSTEM4 with the gate at -1, so they skip the loop, and op `0x93` empties the table
as native does.

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
press/release cancellation path described next.

**Click cancels Skip (2026-11-03).** `adv_interpreter_tick` runs only while run-state `0x08000000` (active
fast-forward from persistent Skip, read-skip or held action 6) is set. With a nonzero `CancelMesSkipOnClick` it
runs a state machine at `ctx+0x6da80` on action-4 bit `0x10` (polled held mask plus click triggers) before the
dispatch:

- bit set: the bit is removed from the tick's mask; state 1 becomes 2;
- bit clear in state 2: state 0, `input_flush_action_triggers`, run-state `0x08000000`, `ctx+0x13dc` and
  `ctx+0x550fc` cleared, `message:ReadTextSkip=0`; with value 2 `script_launch_by_name@0x410d10`
  (`"CALLBACK_SETTING.BIN"`) enqueues the redraw callback through `script_launch_queue_enqueue@0x40f820`, and the
  tick returns without dispatching;
- bit clear otherwise: state 1.

Op `0x101` also zeroes `ctx+0x6da80`, so the Skip button's own callback leaves the machine idle until a tick
with action 4 clear. With a nonzero setting the tick's hotspot hover/click dispatch is withheld during
fast-forward. A held physical action 6 restarts fast-forward at the next reveal, unless op `0x101` has suppressed
that ("Physical fast-forward" below). `CALLBACK_SETTING.BIN` redraws the
message window and nameplate when `system_flow_request == 1` and `G[0x6bf]` is set.

The port implements this in `VirtualMachine.SkipClickCancel.cs`: the machine runs at each instruction boundary
while fast-forward is active, the frontend latches an action-4 click to it instead of hotspots or page advance,
and the mode-2 callback runs nested at the first boundary outside an open statement block (the native launch
queue suspends the frame and runs the script in reserved frame 37; its statement-block close dispatches a
deferred launch). A native run on 2026-11-03 confirmed the behaviour: after the SC0000 page-1 Skip button, one
held click stops Skip and the scene continues at normal speed to the next input wait, as in the port. The port
reaches a later stop page because it fast-forwards faster than native (0.4 s after enabling Skip: native at
`SC0000@0x13c8`, port at `0x126e8`). That pace gap is now closed (see "Fast-forward pace" above). The same session showed that a scene called with `run` from SC0000
crashes native under fast-forward ("このコマンドはサポートされていません", the caller's pc in the callee),
consistent with the documented rule that a `run`-called script cannot be a scene.

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

**Skip pacing regression corrected (2026-07-29).** A later port change incorrectly treated
`adv_interpreter_tick`'s one-op dispatch as one rendered frame per opcode only while Skip was active.
That contradicted the synchronized native trace above: the outer service calls the dispatcher in fast bursts,
and Ctrl measured about 7,738 operand fetches/sec rather than one per display frame. The artificial
`GodotAdvHost.FrameYield` wait made the setup/cleanup bytecode between skipped fades take seconds even though
the fades themselves correctly jumped to their endpoints. `FrameYield` is nonblocking again; only explicit
presentation, sleep, and input services own wall-clock time.

**Fast-forward pace (2026-11-03).** The main loop `engine_main_tick_with_exception_policy@0x411840` is now
readable, and it settles the fast-forward rate. While run-state `0x08000000` is set it dispatches exactly one op
per iteration through `adv_interpreter_tick`. Each iteration then calls `gfx_request_force_complete_and_reset_anim_clock`,
`sfx_clear_scheduled_starts`, `FUN_004884c0`, a 1000-slot movie-surface pass and `Sleep(0)` before the next
`PeekMessage`. It also runs `gfx_render_frame` at the top of the iteration when tick presentation is on, the frame
is dirty and no statement block is open. The fast-forward branch is tested before the sleep bit (run-state `1`),
and `sleep_op_0xc8@0x420ec0` arms nothing while `0x08000000` is set, so sleeps cost nothing under fast-forward.
Native fast-forward is therefore paced by a per-op cost, not by sleeps or a display rate (`Present` is
uncapped).

Measured with `tools/drive` on 2026-11-03: SC0000 read-skip from page 1 to the first unread page `0x1b6e`
(13,512 ops in the port's trace) took 5.06 s on native AGE, about 0.374 ms per op. The port took 2.72 s: its ops
were nearly free, and its time went to three `0xc8` sleeps it still honoured (about 1.0 s, including a 1000 ms
sleep at `SC0000@0x1342`) and 61 synchronized `0x20c` presentations (about 0.6 s).

The port now matches this. `0xc8` returns at once while fast-forward is active, and the Godot host ends an
in-progress sleep when fast-forward starts. `VirtualMachine.FastForwardPace.cs` paces fast-forward ops against a
wall-clock deadline of `VmOptions.FastForwardOpMicroseconds` per op, with
`VmOptions.NativeFastForwardOpMicroseconds = 374` in the Godot frontend and pacing off in headless tools and
tests. Time already spent blocked in host services counts toward the budget, up to 100 ms of credit. A run that
leaves fast-forward and returns starts a new schedule. Pacing reads its time and sleeps through the VM's
`IPaceClock` (a Stopwatch and `Thread.Sleep` by default). `FastForwardPaceTests` passes a virtual clock that moves
only when pacing sleeps or a simulated service blocks, so it checks the schedule, the 1 ms sleep granularity, the
credit cap and the restart exactly, without measuring wall time. The same measurement then took 5.08 s. That 374 µs
constant was superseded on 2026-11-07 by a cost per op plus a cost per page, timed on both games ("Fast-forward through
effect waits, its pace, and physical Ctrl"). Native's per-iteration cost is CPU-bound, so it varies by machine.

**ADV entry/exit lifecycle.** The script comments `savemesskip` and `loadmesskip` describe a temporary
suspension boundary, not a second saved preference. `op_0x19b_suspend_adv_skip_service@0x416560` clears
active `ctx+0x13dc`, run-state bit `0x08000000`, and `ctx+0x55100` (`adv_skip_service_enabled`) while
deliberately preserving `ctx+0x550fc`, the all-message Skip toggle returned by op `0x19a`.
`op_0x19c_resume_adv_skip_service@0x4165a0` sets the lifecycle gate again and reconstructs active
fast-forward from persistent all-message Skip or `adv_read_skip_state`; the separate startup guard at
`ctx+0x6f86c` can suppress that reactivation. SC0000 brackets ADV teardown/setup with this pair, as do
branch transitions and `CALLBACK_LOAD.BIN`. Both ops clear run-state `0x08000000` however it started, so a held Ctrl
does not survive them ("Physical fast-forward").

The port therefore keeps persistent Skip separate from currently active fast-forward. Opcode `0x19b`
deactivates the host service without changing the control-strip toggle, and `0x19c` recomputes the host
service from the persistent toggle plus the host's live read-skip channel. This also makes op `0x1c7`
correctly report inactive during the suspended interval. Focused regressions cover preservation across the
pair and read-skip-only reactivation. The `message:ReadTextSkip` preference and `ReadTextDB` were implemented
later (2026-07-24; see "ADV read-message Skip and shared `RT.DAT` history").

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
under Godot `user://games/himegari/`, and supplies `user://games/himegari/SAVE` to
`DirectoryNativeDatStore`. This isolates authored port saves from the original installation and other game
profiles while retaining compatible file structure. Pre-K1 files at the unnamespaced `user://SAVE` and
`user://SYS4REG.INI` locations are left untouched and are not imported automatically; migration must be an
explicit copy into the Himegari namespace so startup cannot guess which profile owns legacy data.

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

#### Playtime (2026-11-07)

Every container header stores a playtime in whole seconds at `+0x118` (`sys4-format-notes.md`). Shared `SAVE.DAT`
and each numbered `SAVE##.DAT` get it from one clock, kept in the save container object at `ctx+0x4d7c`. It has
three fields: base `+0x40c`, start `+0x408` (in `timeGetTime()/1000` seconds) and pending `+0x410`. The same model
holds in SYS4422 (`/v2`) and SYS4433:

| Event | Native | Effect |
|---|---|---|
| write (`0x19e`, the shared-profile flush) | `save_container_encode_and_write@0x42fac0` | stores `timeGetTime()/1000 + base - start`; the two clock readings are each truncated to whole seconds |
| read (boot `SAVE.DAT`, `0x19f`, `0x1a1`) | `save_container_read_and_decode@0x42ff80` | pending = the header's playtime; base and start are untouched |
| reset (container construction, op `0x9`) | `save_container_playtime_reset@0x42fa80` (SYS4433 `0x430430`) | base = 0, pending = 0, start = now |
| commit (each frame a full load restores, op `0xad`) | `save_container_playtime_commit_pending@0x4306c0` (SYS4433 `0x430fe0`) | base = pending, start = now |

`script_frame_restore_saved_layout@0x40f2d0` (SYS4433 `0x40f0c0`) commits for layouts 2 and 3, once per restored
frame. The deserializer calls it only when `restore_runtime` is set (`0x1a1`, not `0x19f`), and op `0xae` and op
`0x2` call it for the frames that follow. A full load therefore counts on from the slot's playtime, starting at its
last frame restore, while a data-only load leaves the clock running. Booting reads `SAVE.DAT` but restores no
frame, so a fresh run counts from zero. A native Kamidori run (2026-11-07) confirmed this: after 31 s at the title
it wrote 31 to `SAVE.DAT`, which had held 3585 before. Op `0xad` (`op_0xad_commit_pending_playtime@0x416760`,
SYS4433 `0x416f00`) commits on demand; no shipped script uses it.

**Port:** `VirtualMachine` keeps the same three values. Its clock is a `TimeProvider`: `TimeProvider.System`'s
timestamp counts from Windows start like `timeGetTime`, and tests pass a manual clock. `ResetPlayTime` runs at
construction and in op `0x9`'s scene reset; `CommitLoadedPlayTime` runs for each frame a numbered load restores
and for op `0xad`. `0x19e` and the shutdown flush both store `AccumulatedPlaySeconds()`. `NumberedSaveVmTests`
covers the fresh run, full load, data-only load, op `0xad` and return-to-title cases;
`SharedProfileLifecycleTests` covers the shutdown flush.

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
`Ome.Engine.Persistence.NativeSaveContainerCodec` now reads and writes the common header, Shift-JIS game id,
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
validates magic, game id and versions (since 2026-11-03 not the compatibility id, like AGE; see "Save identity and
native read checks" below), queries metadata without decoding payloads, and performs exact
paired `.DAT`/`.STH` copy/delete status layering. `NumberedThumbnailCodec` reads and writes the native BMP
dialect through host surface capture/replacement, and ops `0x1a0`, `0x1ab`–`0x1af` (including `0x1ad`'s
frame marker) are wired. Godot's shared profile-path resolver injects the Himegari store at
`user://games/himegari/SAVE`.
`NativeNumberedSaveCodec`, `NativeTextHistoryCodec`, and `NativeGfxPersistenceCodec` own the complete
layout-3 numbered body, global/frame/gfx state, and appended history tail. The VM wires `0x19e`,
data-only `0x19f`, full load
`0x1a1`, and the active branch of `0xae`; the existing `GameSession` JSON snapshot remains a separate
diagnostic/extended-mode surface.

#### Layout-3 restore mechanics and port correspondence (2026-07-24)

The port tracks the native global banks separately at runtime, captures the marked frame chain, serializes
live surfaces and retained objects, and applies the 20-byte surface records' explicit reload flags. Full
load first clears each global bank and copies the serialized prefix into it, restores history/gfx and retained
audio, unwinds the obsolete managed call chain, runs
`CALLBACK_LOAD.BIN` when
the mounted script provider resolves it, loads each saved script at its ordinary entry so its frame-local
prologue runs, lets that script reach its own `0xae` rendezvous, recursively reconstructs child frames,
resumes parents after their saved T2 call sites,
and finally resumes the terminal frame at its T1 boundary. Successful `0x19e` also flushes shared
`SAVE.DAT`/`RT.DAT`, matching `context_state_serialize`.

The prefix boundary is observable in the installed file: integer count `0x6241b` stops before the
initialization-authored unit/stage definition tables, and string count `0x315` stops exactly before
`unit_story_display_names`.

**A load zeroes each bank in full** (corrected 2026-11-05; an earlier revision of this section said only the
counted regions). Before copying the prefixes, layouts 2 and 3 of
`save_data_deserialize_and_begin_restore@0x40fd10` memset every bank to its live size:

- integers `ctx+0x53c7c` (Himegari 1,419,442 cells);
- floats `+0x53c80`;
- strings `+0x53c84` (18,180, each erased);
- the integer-pointer and string-pointer banks `+0x53c88` and `+0x53c8c`, and the local pointer scratch `+0x53c90`.

The definitions past the prefix come back because every restored frame re-runs its prologue from its entry to
`0xae`. SYSTEM4's `0xae` is at `0x235`, after LOADCONFIG or INITCONFIG, the op-`0x64` table copy at `0x1ca`, and
`call-script INIT2` at `0x232`. INIT2 runs EBINIT, CNINIT and the other `*INIT` tables. Kamidori's SYSTEM4 calls
INIT2 at `0x23c` and reaches `0xae` at `0x23f`.

A cell past the prefix that no prologue writes therefore reads 0 after any load. Native measurements after a load
from MENU: `G[0x62428]` = 0, `G[0x6242d]` = 4 (the `0x1ca` copy) and `G[0x66381]` = SO026A.AGF's id (INIT2).
`G[0x62428]` is SC0000's MENU-busy flag, which the right-click handler sets before `call-script MENU.BIN` and a load
from MENU never clears ("Native menu bar"). The port matches: `ApplyNumberedState` clears each bank before applying
the prefix, and `InstalledSave00ContinuesToRestoredGameplayPollWhenPresent` checks INIT2's tables after a load. It
had preserved the cells past the prefix until 2026-11-05, which left `G[0x62428]` set after a load from MENU and
deferred every `CALLBACK_SETTING.BIN` redraw.

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
changes the reload flag. The layout-3 serializer copies 20,000 record bytes verbatim, rather than deriving
reloadability from the presence of an asset. Those bytes are the op-`0x71` page snapshot at `ctx+0x4bb34`,
not the live table at `ctx+0x46d14` (2026-11-03). `op_0x71_reset_adv_text_layout@0x41e540` copies live to
snapshot, and also copies the resource records from `ctx+0x3cac` to `ctx+0x415c`. Releases (`0x1fa`,
`0x23d`) write only the resource id `-1`; the key and created flag stay. Every worker that draws into a
surface sets created. Which workers those are, and how the key is packed, is listed in
[sys4-format-notes.md](sys4-format-notes.md) under "Surface-record bookkeeping".

The restore loop calls `gfx_surface_load_asset` only when the saved reload flag is one and resource id is
nonnegative. Otherwise the already-initialized surface survives. The optional all-surface release is
gated by `set:CreateObject` and `set:AutoFreeTex` ("AutoFreeTex" below). Opcode `0x259` is also a real lifecycle operation:
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
reload records, unless `VmOptions.AutoFreeTex` selects native's all-release branch (below). Port-authored saves emit the exact resource/reload/created fields, including
`-1` for unused records, and opcode `0x259` clears the modeled reload policy without releasing textures.
The corrected native functions are renamed/commented in the saved `/v2` image.

#### AutoFreeTex (2026-11-07)

`set:AutoFreeTex` (SYS4INI `AUTOFREETEX`; registered default 0 at `engine_settings_register_defaults@0x46be30`) is a
mask. Both bits act only under `set:CreateObject` bit 1, the object backend both games create:

- **Bit 1.** `save_data_deserialize_and_begin_restore` releases all 1,000 surfaces before a numbered load restores its
  records, so only the records flagged for reload come back.
- **Bit 0.** Op `0x9`'s root reload (`op_0x9_reset_scene_and_reload_root_sys4433@0x423b50`) releases them as well.
- The release worker (`retained_gfx_release_surface@0x47b6b0`) skips a slot whose guard `gfx+0xb638[slot]`
  (`ctx+0x55ebc`) is set, but nothing writes that table, so every slot is released.

The code is not a revision difference. SYS4422 registers the same default 0, and its handler
(`op_0x9_reset_scene_and_reload_root@0x418f50`, at `0x4191de`) tests the same `(CreateObject & 2) && (AutoFreeTex & 1)`
gate as SYS4433's. Only the setting differs between the games.
Himegari's trailer has no `AUTOFREETEX`, so its initialized system textures survive a numbered load. Kamidori sets 3.

The scene reset that op `0x9` runs first (SYS4422 `scene_context_init_reset@0x40b3b0`) releases no texture. It
reinitializes both record tables and deletes each slot's `CMovieToTexture` (`ctx+0x52bd4`), but the texture wrappers
(SYS4422 `gfx+0xa590`) stay. A native read across a Himegari return to title (2026-11-07) agrees:

- Slots 5 (`0x23`) and 14 (`0x21`), loaded by SC0000, still hold textures at TITLE.
- Their records read resource id -1, so op `0x216` returns -1 for them.
- Every other texture live on SC0000's first page is still live at TITLE.

The port gates both releases on `VmOptions.CreateObject`/`.AutoFreeTex`:

- **Numbered load:** bit 1.
- **Op `0x9`:** bit 0, after `GfxState.ResetSceneContext` and the host's reset
  (`VirtualMachine.ResetSceneContextForRootReload`). The reset itself keeps the surfaces and clears only the records,
  the reload flags and the movies. Op `0x216` reads the live record (`GfxState.QueryField`).

Driving the port the same way, its reset keeps every surface it holds (3, 5, 12, 14, 15, 17, 21). Its title after the
return matches its boot title exactly. The port host never holds native's other kept slots (22-29, and the boot
scratch surfaces 36/37) as surfaces, so the reset cannot drop them.

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

### Save identity and native read checks (2026-11-03)

Found while making a port-written slot load in AGE and the reverse (game-driving acceptance 3; scenarios
`tools/drive/scenarios/himegari-save-page3.steps` / `himegari-load-page3.steps`). Format detail lives in
[sys4-format-notes.md](sys4-format-notes.md#common-s3sd--s4sd-dat-container--confirmed).

- **The compatibility id is the engine version.** `engine_ctx_construct_SYS4422` copies it into `ctx+0x53b74`
  (`engine_version`, four characters). The /v2 dump shows the placeholder `"000 "` at its source `0x571218` (the exe
  is packed); the running Himegari exe holds `"424J"`, as every file it writes shows. `engine_initialize_subsystems_from_settings` prints it as
  `ARCGameEngine Ver %c.%c%c%c`. `shared_profile_save@0x40c950` passes it with the SYS4INI game id at `ctx+0xa0bc0`
  to both `shared_profile_payload_write` and `read_text_db_write_file`; `op_0x19e_save_numbered_slot` passes it too.
  The 2019 executable wrote `"422B"`, which is why the installed numbered saves differ from SAVE.DAT. It was never a
  per-file-kind id. `engine_load_sys4ini_and_mount_append_catalogs` compares its first three characters with
  SYS4INI header `+0x04` (`ctx+0xa0bbc`).
- **No reader checks it.** `save_container_read_and_validate_header@0x4306f0` compares the magic (`S4SD` when the
  SYS4INI header copy at `ctx+0xa0bb8` begins `S4`) and the NUL-terminated game id, then copies the header.
  `read_text_db_read_file@0x46aad0` compares `S3RT`, the game id and version 1.0; its `compat_id` parameter is
  unused. The port now behaves the same, so slots from other engine builds list and load.
- **Header `+0xf0` is the checked logical length** (payload plus both CRC DWORDs). `shared_profile_payload_read`
  and the numbered loader behind op `0x1a1` allocate `(len / 4) + 1` DWORDs from it before decoding. A port slot
  with zero there made AGE report "メモリが不足しました" and crash.
- **Restore-side defaults the port must write:** the 100 resource-reload records are `(-1, 0, 0)` when empty (the
  loop at `0x410576` reloads only when the flag DWORD is one), and the numbered string bank is
  `(unpadded / 4) + 1` DWORDs, the same rule as SAVE.DAT.
- **Native SAVE.DAT string padding is not zero.** The writer leaves heap residue (`0x3f` observed) after the last
  string; the reader ignores it. The port had rejected it and failed to boot on a native-written profile.

The Ghidra plates of the functions named here and the `EngineCtx` fields `engine_version`, `sys4ini_magic`,
`sys4ini_required_engine_version` and `sys4ini_game_id` carry the same findings (`vm-map/engine-ctx.toml`).

### Kamidori numbered-save graphics extension (2026-09-21)

The SYS4433 image at `/kamidori/SYS4433/range_00400000.bin` now has the following persistence functions
named and annotated:

| Function | Address | Evidence |
|---|---|---|
| `context_state_serialize` | `0x40d1e0` | Layout-3 graphics size, sparse write stride, range record, and allocation term |
| `save_data_deserialize_and_begin_restore` | `0x40faa0` | Reads declared byte size, initializes each record, copies it, then advances a DWORD cursor by the byte-size value |
| `gfx_object_init_default` | `0x478a40` | Common-prefix defaults and initialization of the appended rotation cache |
| `gfx_object_evaluate_one_shot` | `0x479170` | Produces the cached evaluated axis/radian angle and consumes it in a display-mode-specific rotation rebuild |

The initializer and evaluator establish that the larger record is a tail extension, not a wholesale
shift of the existing fields. During one-shot rotation evaluation, the native code writes the evaluated
axis and converted radian angle into that tail. When `gfx_owner+0xb61c == 0`, display mode is 2, and the
source slot is 20 through 29, it rebuilds the matrix from those cached values. Consequently, merely
accepting the larger byte count and truncating or blindly zero-filling the extension is not a complete
implementation. The loader's initialization before copying does not rescue a malformed full-size record:
the copy overwrites the initialized bytes.

The exact byte layout, implementation status, installed-save roundtrips, and acceptance limits live in
[SYS4 format notes](sys4-format-notes.md#kamidori-320-layout). The native findings are implemented in the
shared codecs, with profile-selected framing rather than separate game-specific save opcode handlers.

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
dword flag. Ops `0x6e/0x71/0x72`, and the menu bar's toggle `adv_toggle_read_skip_and_refresh@0x406cd0`,
combine that result with `message:ReadTextSkip`; a read page sets `run_state_flags & 0x08000000` and
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
record table, and ordered flag arrays, validates magic/game/version identity (not the compatibility id), accepts native nonzero
pointer residue, and writes zero in that ignored field. `DirectoryNativeDatStore` owns
`$$RT.DAT` → `RT.DAT` / `RT.BAK` replacement; load follows native behavior and reads `RT.DAT` directly
rather than treating `RT.BAK` as a fallback. `SharedProfile.ReadText` owns records and the pending queue
across fresh VMs, and shared-profile Save/Load updates SAVE.DAT and RT.DAT as separate native domains.
Godot now requests a VM stop, releases the blocking host, waits for the VM worker to leave its opcode
boundary, and performs one shared-profile flush before frontend teardown. Repeated exit notifications do
not rotate backups twice; expected I/O failures are reported without crashing teardown; self-test uses
`NoSaveDat`. (The 2026-07-24 note that the loaded shared header's playtime seeded the process baseline was wrong:
reading `SAVE.DAT` only sets the pending value, so a run's flush stores its own run time; see "Playtime" above.)
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

**Native comparison (2026-11-03).** `tools/drive/scenarios/himegari-read-skip.steps` saves on SC0000 page 1,
reads six pages, reloads the save and turns on Read-message Skip. Native AGE and the port both fast-forward over
the read pages and stop at `SC0000@0xa18`, the page shown but not yet advanced past, with the control showing ON.
The port's refresh is simpler than the check in `adv_toggle_read_skip_and_refresh@0x406cd0`, whose only caller is
the menu bar's 既読ﾒｯｾｰｼﾞ ｽｷｯﾌﾟ toggle (2026-11-05). That check evaluates only while run-state
`0x80000000` is set (an ADV wait's input poll: op `0x72`, or op `0xfa` when fast-forward is inactive), clears that gate when a read page starts
fast-forward, and on an unread page clears `adv_read_skip_state` only while the all-message toggle `ctx+0x550fc`
is off. The port recomputes at each text/wait boundary. No visible difference followed from this in the run.

### Remaining ADV control-strip actions and implementation cost (2026-07-18)

The five standard controls are now fully inventoried. Auto message, all-message Skip, and Hide Window have
working native-path services; Read-message Skip is the profile-wide `RT.DAT`/ReadTextDB slice above.
History remains a distinct retained-text subsystem rather than another variation of Skip.

| Control | Native action | Current port boundary | Relative cost |
|---|---|---|---|
| History (`x=684`) | Cancel the ADV hotspot wait and run `HISTORY.BIN` over the text manager's retained record stream | Input/callback infrastructure works; a bounded retained-history model, its read/write opcodes, and supporting text/presentation ops remain | Medium-high, bounded |
| Auto (`x=706`) | Toggle the Auto service | Implemented, including timed wait completion | Done |
| Message Skip (`x=728`) | Enable persistent all-message fast-forward | Implemented; pacing discrepancies remain a later fidelity adjustment | Done |
| Read-message Skip (`x=750`) | Toggle `message:ReadTextSkip`; gate advancement through shared ReadTextDB state | Implemented 2026-07-24; native-confirmed 2026-11-03 (both targets stop at the first unread page) | Done |
| Hide Window (`x=772`) | Op `0x199` enters the saved ADV coroutine handler, which fades the text box out. Retail Himegari then waits in the engine until any trigger. `HIDEWIN.BIN` runs only when `G[0x62425]` is set, which retail never does; Kamidori always runs it | Implemented 2026-11-05 on both paths: the engine's hidden-window wait, and the `HIDEWIN.BIN` script path (coroutine re-entry, per-frame callbacks, mouse/joy state, `.CUR` resources). See "ADV hide window: the engine's hidden-window wait" | Done |

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

A text record holds the layout slot at `+0x00`, the drawn row's rectangle edges at `+0x04..+0x10`, BGR
primary/effect colors at `+0x14/+0x18` (metadata and voice records hold their value pair there instead),
text manager `+0x554` at `+0x1c`, render mode at `+0x20`, primary pixel height at `+0x24`, flags at `+0x28`,
and an inline-or-heap string object at `+0x2c` (length `+0x40`, capacity `+0x44`). The per-field table is in
[sys4-format-notes.md](sys4-format-notes.md) under "Appended text-history tail" (2026-11-03). Flag bit 0 begins a logical group; bit 1 is filtered by
History's navigation mode; `0x20000000` denotes typed metadata and `0x40000000` denotes a voice pair.

The write path is part of ordinary ADV execution:

- op `0x70` defines a text layout and op `0x71` resets one. Unless recording is suppressed, each appends
  `{layout_slot,current_record_count}` and arms the next record's group-start bit. The op-`0x71` operand is a
  layout slot—not a T1 anchor id—although T1 entries structurally target these reset sites. Its ReadTextDB
  snapshot/commit work remains a second responsibility.
- op `0x6e`'s glyph builder (`adv_text_build_glyph_records@0x4576c0`) appends one record per drawn row.
  Each horizontal wrap adds a flag-`8` line-break record before the continuation row. Op `0x6f` appends the
  same line-break record (`adv_text_append_line_break_horizontal@0x456fd0`).
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

That tail is specifically the History backlog, not the live ADV layout presentation.
`text_history_deserialize` calls `text_history_clear`, reconstructs only the logical index and 0x48-byte
History record/string vectors, and does not recreate or replace live layout definitions, layout glyph
records, source-surface pixels, selected layout, wait-indicator handles, or glyph-object ranges. The main
layout-3 container likewise reloads only flagged asset surfaces; created ADV layout surfaces have no
serialized pixels. Full restoration runs the saved scripts from their ordinary entry prologues through
`CALLBACK_LOAD.BIN`/`0xae`, so the current page is rebuilt and republished by script control flow.
`AdvTextHistory.RestorePersistenceSnapshot` consequently replaces only backlog data and preserves any
initialized live layout configuration/bindings. A focused native-tail regression pins that separation.

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

The ordinary ADV redraw pair `0x83` -> `0x82` uses the same retained index and 0x48-byte record vectors but
is distinct from HISTORY's reflow path. In SYS4433, `op_0x83_query_text_redraw_state@0x427e10` writes the
input service's `-1/0/+1` redraw direction from `ctx+0x72078`, then
`text_query_redraw_cursor@0x450340` returns the 8-byte entry selected by mutable text-manager cursor `+0xd70`.
SYS4422's matching handler at `0x427140` instead calls `text_query_redraw_anchor@0x450bb0`, which reads the
fixed latest-entry anchor at `+0xd6c`; that is a real revision distinction rather than a Kelebek-label
difference. `0x82` then draws the retained group again at its records' rectangles ("Op 0x82 redraw" below). The
portable `0x83` returns a neutral direction plus its newest retained entry, which is all Kamidori's CONFIG sites use.
Direct key-driven history walking remains an input service feature, not an opcode compatibility stub.

The third port slice implements visible History presentation without moving canonical records into Godot.
Op `0x1d1` asks `AdvTextHistory` to select one retained group, skip metadata/voice records, and build a
host-facing render batch using the target layout configured by `0x70/0x71`, positioned by `0x198`, and
seeded by `0x7a`. `HISTORY.BIN` passes zero for flags and both color overrides, so this is its exact ordinary
binding path; the native flag-4 raster-without-binding and flag-8 side-effect modes remain outside the live
game call site. The initial port used replaceable labels keyed by target layout. The retained-glyph migration
now passes the target's transient surface/handle binding with the batch, rasterizes its text through the
shared glyph-mask service, and publishes one ordinary object per edge record. Exact rows therefore inherit
native z-order, alpha/tint/fade, clipping, transforms, capture, and range erasure. `0x71` resets the row's
surface and records; HISTORY's broad exit erase plus recording re-enable remove all transient row bindings
without touching the persisted backlog. The layout-keyed Label remains only as the explicit
unavailable-rasterizer fallback.

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

#### Op `0x82` redraw (2026-11-07)

**Where it runs.** Only Kamidori runs op `0x82`, and not when CONFIG returns. Its six sites are CONFIG's setting
handlers, so the ADV page behind the open CONFIG screen shows a change at once:

- CONFIG1's 初期設定 (`label_fb6`, after `INITCONFIG`, `INITCONFIG2` and `CHECKCONFIG`);
- every CONFIG2 change (`label_1d47`);
- the matching CONFIG3, CONFIG4 (two sites) and CONFIG5 handlers.

Each first sets the text style from the settings: the face `GS[0x279]`, bold, ruby size 8, line spacing 10, render
mode `G[0xfd499]` and effect offset `G[0xfd49a]`. It then calls `0x83` -> `0x82` only when `G[0]` is 1 (ADV mode,
set at `SC0000@0x660`), `G[0x4f9]` is 1 (the window is shown; MENU leaves it alone) and `G[0x306e]` is set (every line
sets it). The call is `redraw-retained-text-group (layout) (record) 0x2 0xffffff G[0xfd498]`: flag 2, white text and
the configured outline color.

**Native.** SYS4422 `op_0x82_redraw_retained_text_group@0x41edd0` -> `text_redraw_retained_group@0x451530`; SYS4433
`@0x41cef0` -> `@0x4553c0`, decoded below.

1. On the D3D path it releases the layout's objects (`gfx_release_object_range@0x4843c0` over `layout+0x68`/`+0x6c`)
   and clears the layout's surface. The DrawMode-1 path without D3D fills the rectangle (`0x4374e0`).
2. Flag 2 takes the text and effect colors from operands 4 and 5, R and B swapped.
3. It walks the records from the requested one:
   - a navigation-filtered record (`+0x28` bit 1) ends the walk unless flag 1 is set;
   - the next group start (`+0x70` bit 0) ends it;
   - metadata records (`0x20000000`) are skipped;
   - voice records (`0x40000000`) are replayed only under flag 8.
4. For each text record:
   - It restores the record's text and effect colors (`+0x14`/`+0x18`, unless flag 2), the unidentified `+0x1c`
     (into manager `+0x558`) and the pixel height (`+0x24`). Face, weight and effect offset stay the manager's current
     ones.
   - It draws in the render mode the manager had on entry, not the record's `+0x20`.
   - Each character is drawn from the record's rectangle top-left. It advances by its measured extent, or by the
     reference cell `+0x313e8` in aspect mode (half for a single byte).
   - A ruby record (`+0x28` bit 2) draws in the ruby font, mode 0, advancing by `+0x355d8`.
   - It writes the drawn right/bottom back to the record.
   - Unless flag 4 is set, it binds one object per record over the drawn rectangle
     (`gfx_bind_object_to_surface_rect@0x4855e0`, handles counting up from `layout+0x68`).
5. The aspect in-cell placement flag (`+0x35598`) is 1 during the walk. The style is restored at the end, and the
   live glyph vector and the text cursor are left alone, so later text continues the page.

**Port.**

- `AdvTextHistory.TryBuildRedrawBatch` walks the group as native does and gives each record its draw style: the
  current `AdvTextStyle` with the record's colors (or flag 2's) and pixel height, and mode 0 for ruby.
- `IAdvHost.RedrawRetainedTextGroup` receives it. `GodotAdvHost` draws every record at its rectangle into a cleared
  layout surface (`RetainedGlyphLayoutEngine.RenderRedraw`; ruby through `RubyTextLayout.DrawAt`).
- Unless flag 4 is set, it binds the published glyphs again over the new pixels. Native binds one object per record,
  which covers the same cells.
- Flag 8's voice replay is not modelled (`docs/backlog.md`).
- It is not a History presentation, which History's exit would erase.

The port used to lay the whole group out again as one reflowed string through the History path. On return from
CONFIG that duplicated the text, then left the page blank.

**Check.** `tools/drive/scenarios/kamidori-config-redraw.steps` (2026-11-07) reaches the furigana page
`SC0000@0xfa2`. It then makes two CONFIG changes, 初期設定 on 基本設定 and ADV設定's 効果 縁なし, and shoots the page
after each. Inside the text window no pixel differs from native by more than 30 after either change, the ruby
included. Before the changes, only the wait indicator's animation frame and 4 pixels of the window's top edge
differ. Outside the window the background's edges differ, as on every Kamidori page compared so far
(`docs/backlog.md`). Synthesized tests: `TextRedrawOpsTests` and
`RubyTextLayoutTests.ARedrawDrawsThePageAgainAtItsRecords`; the latter redraws a wrapped page with ruby and gets the
live page's pixels back.

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
`0x1ce`/`0x20a` form a sprite-animation service pair, and `0x1cb` reads the Read-message Skip profile setting
(implemented 2026-07-24).

The original dependency order was **Hide Window first** to establish reusable callback/coroutine input, then
Read-message Skip, then History after both the input layer and message-completion seam exist. Hide Window is
now complete. Read-message Skip was implemented on 2026-07-24 with native shared-profile `RT.DAT` storage (see
"ADV read-message Skip and shared `RT.DAT` history"). History is therefore the sole remaining control-strip action under
active consideration; it can already reuse Hide Window's input layer, while any useful sharing with the
deferred ReadTextDB work should remain a seam rather than a storage dependency. The `/v2` image
names/comments the cursor, callback dispatch, retained-history navigation/render/metadata, and history-voice
opcode paths.

#### Mouse wheel at an ADV text wait (static and native-driven, 2026-11-04)

The "special run modes" above are the ADV waits. `age_main_window_proc@0x486320` sends a wheel turn to the
accumulator only while run-state has none of `0x90100000`:
- `0x80000000`, an ADV text wait polling;
- `0x10000000`, the op-`0x95` choice table (no game uses it);
- `0x100000`, text-redraw mode.

Otherwise the sign selects `set:WheelKeyUp`/`set:WheelKeyDown` (SYS4INI `WHEELKEYUP=8`/`WHEELKEYDOWN=9` in both
games, registered defaults 3/1). The window procedure ORs that logical action's bit into
`service_action_trigger_mask` (ctx+`0xa0ce8`), and the delta never reaches op `0x10d`. `adv_input_service_poll@0x411230`
then handles the bit in this order:
1. A hotspot record bound to the action with op `0x97` runs its activation callback. Every Himegari scene binds
   actions 0, 7 and 8 (301 sites each), and Kamidori binds 0–3, 7 and 8 (322 each). SC0000's action-8 record calls
   `label_339`, which opens HISTORY (`call-script` at `0x35a`).
2. Otherwise, with no click or fast-forward bit, a `WheelKeyDown` bit advances like a click when
   `message:AdvanceMesOnWheel` is 1 and text-redraw mode is off.
3. With `set:ReDrawTextOnKey` 1, either wheel bit steps the text view and launches `CALLBACK_TEXT.BIN`. Both games
   set `REDRAWTEXTONKEY=0`.
4. An unconsumed bit has no effect; the next text reveal clears the mask.

No script binds action 9, and neither game changes `AdvanceMesOnWheel`: its op pair `0x2cc`/`0x2cd` is unused, so it
comes only from `SYS4REG.INI`, default 0. In retail, wheel-up at a text wait opens History and wheel-down does
nothing. Dispatching a callback clears `0x80000000`, so inside HISTORY the wheel feeds op `0x10d` again and scrolls.

The text-reveal service `adv_text_reveal_service_tick@0x406fa0` (run-state `0x20000000`, outside the mask) has a
matching rule. With `AdvanceMesOnWheel` 1 it reads and clears the accumulator each tick: a negative total finishes
the reveal, and any other total is discarded.

**Native driving.** `tools/drive/scenarios/himegari-wheel-history.steps` covers the behavior at the SC0000 page-7
wait (`SC0000@0xa18`): wheel-down changes nothing, and wheel-up opens HISTORY as `SYSTEM4 > SC0000@0x35a > HISTORY`.
Inside, one wheel-up scrolls a row older. The close button returns to the wait, where wheel-up reopens HISTORY.
Before the port change, its wheel-up stayed on the page.

**Port.**
- `VirtualMachine.ApplyMouseWheel` implements the routing. Its wait-polling flag follows `0x80000000`: the VM sets
  it around the host's text wait and clears it while a hotspot callback runs.
- The ids come from `Sys4StartupSettings.WheelKeyUpAction`/`WheelKeyDownAction`.
- Godot reads `AdvanceMesOnWheel` from `SYS4REG.INI` on each event.

The native reveal tick also consumes a negative delta queued before the reveal began. The port consumes the
accumulator only when a wheel event arrives during the reveal. Text-redraw mode is not modelled.

### Menu default cursor auto-move (2026-08-16)

The menu option stored in `G[0x3238]` is `menu_cursor_auto_move_enabled`. `INITCONFIG.BIN` defaults it to
one and `CONFIG.BIN` writes the paired enabled/disabled values. At `BUNKI.BIN@0x2b6` and
`SBUNKI.BIN@0x289`, an enabled value makes the script compute a point in the default choice row and invoke
`set-cursor-virtual` (`0x10a`); the disabled path instead samples the existing physical position through
`get-cursor-virtual` (`0x109`). `BUNKIMOVE` and `SBUNKIMOVE` use the same toggle while animating the menu.
The highlighted default row and the physical pointer are therefore two consequences of the same script
path, not one frontend-owned selection operation.

The `/v2` handler `op_0x10a_set_cursor_virtual@0x421590` converts the client origin to screen space,
applies active VirtualFullScreen scale/letterbox geometry, and calls USER32 `SetCursorPos`. The port now
preserves its immediate virtual-pointer update and also forwards a cursor-warp request to the interactive
host. Godot marshals that request onto its main thread, inversely maps the 800x600 AGE coordinate into the
current viewport, and calls `Viewport.WarpMouse`. Godot supports that operation on the port's Windows,
macOS, and Linux desktop targets; it is documented as a no-op on Android, iOS, and Web, where the VM's
logical position still remains correct. A synthetic opcode regression covers both the VM position and host
request. The Ghidra handler annotation was updated and the `/v2` program saved on 2026-08-16.

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

> **Correction (2026-11-04, native-driven).** Retail native does not enter HIDEWIN.BIN on SC0000 page 3. After the
> x=772 control its frames stay `SYSTEM4 > SC0000@0x30d` with the text box hidden, and a left click restores it.
> Handler A takes the `G[0x62425]` = 0 branch to op `0x7c`, and the restored run-state bit `0x4000000` is an engine
> hidden-window wait that ends in handler B ("The other guards", coroutine yield). The seeded value 1 therefore
> does not mirror native. Resolved 2026-11-05: the port no longer seeds it and models the wait (next section).

### ADV hide window: the engine's hidden-window wait (2026-11-05)

**`adv_hide_window_enabled` (`G[0x62425]`) is 0 in retail, and nothing writes it.**

- `engine_load_sys4ini_and_mount_append_catalogs@0x4099f0` allocates the integer bank with `VirtualAlloc`, which
  zero-fills it.
- The cell lies past the saved prefix (`0x6241b`), so no save restores it, and a load zeroes it with the rest of
  the bank ("Layout-3 restore mechanics").
- No native instruction addresses its byte offset (`0x189094`).
- All 301 scripts that mention it only branch on it.

So every Himegari scene's handler A skips `HIDEWIN.BIN`, which is unreachable in retail Himegari. Kamidori's handler
A calls `HIDEWIN.BIN` unconditionally, then yields again into handler B.

**Two entries hide the window.** Both enter the frame's op-`0x7b` handler A with the yield saved.

- **The control** (`x=772`) runs op `0x199` at `SC0000@0x30c`. The saved run state is the callback's (0), and the
  resume is `SC0000@0x30d`.
- **Space** at a text wait. Space is logical action 5 in both games' SYSTEM4 key maps.
  `adv_input_service_poll@0x411230` takes this entry when the poll has no click or fast-forward bit and no
  op-`0x97` binding takes the action.
  - It saves the run state, which holds the text wait's `0x80000000`, and the wait itself as the resume. Native
    pushes no return pc.
  - Both games bind only actions 0, 7 and 8, so Space is free.
  - `adv_input_dispatch_callbacks@0x408530` has the same entry for op `0x95`'s choice table, which no game uses.

**The wait.**

- Handler A (`SC0000@0x3c9`) fades the text box out through `label_1226b`/`label_1235a`. Its `G[0x62425]` branch
  then jumps to op `0x7c` at `0x41d`.
- Op `0x7c` restores `coroutine_runstate & ~0x2000000`, which still holds op `0x199`'s `0x4000000`.
- `engine_main_tick_with_exception_policy` tests that bit before every other run-state service except the dialog,
  close and BGM-fade bits.
- Each tick it calls `input_poll_triggered_action_mask@0x460880`, which takes:
  - the keyboard latch (a non-repeat `WM_KEYDOWN` of a mapped key);
  - the mouse latch (button-up under CLICKONUP; left → action 4, right → action 7);
  - mapped joystick buttons and axes;
  - action 6 whenever the persistent Skip state (`ctx+0x13dc`, the input manager's `+0xfd4`) is set.
- `age_main_window_proc@0x486320` turns `WM_MOUSEWHEEL` into the action of set:WheelKeyUp/Down only while run-state
  `0x90100000` is set.
  - The Space entry keeps `0x80000000`, so there the wheel ends the wait.
  - From the control, the run state is `0x4000000` alone, so the wheel delta accumulates for op `0x10d`.
- The first trigger clears `0x4000000` and enters handler B. The new yield gets:
  - `coroutine_runstate` = the run state | `0x2000000`;
  - the resume = the frame pc (`frame_pc_word_offset@0x405200`);
  - the owner = the running script.
- Handler B (`0x41e`) fades the text box back in, sets `G[0x3233]` = 1 and ends at op `0x7c` (`0x443`). From the
  control, that returns to `0x30d`, where the
  callback sleeps 100 ms, nudges the cursor and returns to its wait. From Space, it returns to the parked text
  wait with its `0x80000000`.
- The wait consumes the trigger. A restoring click never advances the page, and a restoring right click never opens
  the menu.
- Both ops `0x7c` also clear the hotspot hit. After a Space restore under a resting pointer, the control the pointer
  rests on loses its tooltip, and a click there advances the page until the pointer moves. From the control, the
  cursor nudge at `0x30d` hits it again ("The hit and the hovered record").

**Native-driven 2026-11-05 (`tools/drive/scenarios/himegari-hide-window.steps`).**

- The control leaves the frames at `SYSTEM4 > SC0000@0x30d`. The wheel and A (no action) keep the window hidden.
  A left click restores it.
- Space leaves the frames on the wait (`SC0000@0x8a9`). The wheel, Space, a right click or a left click restores
  it.
- Enter still advances.
- On page 4, Space hides and restores under a pointer resting on the hide control: the tooltip goes, and a click on
  the same spot advances to `0x913`.
- The port matches every step. Its hidden frames are pixel-identical to native. Its restored frames differ only as
  any port page does: 68 px of glyph edges and the animated wait indicator. The control strip in the tooltip shots
  is pixel-identical.

**Port.**

- `VirtualMachine.ControlFlow` keeps the yield as engine state:
  - entered (`0x4000000`);
  - the resume pc (`0x2000000`);
  - the owner;
  - whether the resume is a text wait.
- Op `0x7c` with the yield still entered calls `WaitInHiddenWindow`.
  - That sets the frame location to the resume and blocks in `IInputHost.WaitForServiceTrigger`. The wait ends on a
    latched trigger, the Skip service, a stop or a root reload.
  - It then enters handler B, or returns to the resume when the frame has none.
- `TryRequestAdvWaitCoroutineYield` is the Space entry. `ServiceHotspotCallback` runs it before queued hotspot
  callbacks, with hotspot dispatch suspended as during any callback. Its op `0x7c` returns `HOTSPOT_RETURN` to the
  parked wait.
- `TryLatchAdvCoroutineYieldTrigger` holds Godot's trigger edges in the input latch while a yield is under way
  ("Effect skipping and the input latch").
  - It applies only when no nested script's raw input callbacks own input, so `HIDEWIN.BIN` keeps its own input.
  - The latch consumes the input.
  - Neither op `0x199` nor the Space entry clears the latch. Op `0x199` clears only the service trigger mask
    `ctx+0xa0ce8`, so a click an effect held before the yield still ends the hidden wait.
- `ApplyMouseWheel` latches the WheelKey action only in the Space entry's wait.
- Godot (`Main.Input`) no longer treats action 5 as advance. Native advances, finishes a reveal, skips an effect
  and cancels a movie on action 4 only.
- Op `0x199` with no yield entered and no handler A now does nothing, as native does. The port used to jump to
  handler B.
- The Himegari profile no longer seeds `G[0x62425]`.
- Tests:
  - `HiddenWindowWaitTests` (synthesized): both entries, the triggers, wheel routing, Skip and missing handlers;
  - `HotspotInputTests.Sc0000HideWindow_RetailWaitsInTheEngineAndAClickRestoresThePage` (real SC0000, both
    entries).

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

> **Correction (2026-11-05, native-driven).** A left click never closes Himegari's `HIDEWIN.BIN`. The click observed
> in the original game ended the engine's hidden-window wait, which retail runs instead of `HIDEWIN.BIN` (next
> section). Native was forced onto the `HIDEWIN.BIN` path by a test hook that makes every read of `G[0x62425]` return 1
> (`vm_operand_fetch@0x41b940` stores the operand slot at `0x5c55c0`; type at slot−4, value at slot).
> - **Driven on SC0000 page 3.** A left click, short or held 300 ms, left the text box hidden. Enter, Space, a right
>   click and X restored it; A did nothing. Enter restored the page without advancing it. The port with
>   `--seed 0x62425=1` behaved identically.
> - **Why the left button never restores.** Op `0xff` polls button levels (`input_poll_action_mask@0x4608b0` →
>   `input_poll_mouse_action_bits@0x460240`, `GetAsyncKeyState`), not click edges. At `HIDEWIN@0x3ad` a held left
>   button takes the pan branch, which polls nothing. Once the button is up, the poll no longer sees action 4.
> - **The restore inputs.** The restore callbacks run on joy actions 4–7: Enter 4, Space 5, X 7, and mapped joystick
>   inputs. They restore only after the idle callback (slot 10, `label_47f`) has cleared local `0xd`. The
>   `mouse_callback` at `label_120` also restores on a raw right-button release.
> - **Kamidori.** Its `HIDEWIN.BIN` restores on a left-button release only in its no-pan mode, while `G[0xfd41c]` is
>   nonzero (`label_4b5` tracks a raw left edge). No script writes `G[0xfd41c]` directly. With it 0, a left click does
>   not restore, as in Himegari.

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

The record layout is `{publication-chain flag, left, top, right, bottom}` as five dwords. These are native
rectangle edges, not width/height: `gfx_object_bind_draw@0x47e870` copies them directly into its source-edge
record. The publisher binds handle `layout+0x68 + reveal_index` from source surface `layout_slot+0x14` and
places it at `(layout_origin_x+left, layout_origin_y+top)`. A first dword equal to one makes the service
publish the following record in the same tick; otherwise the configured glyph delay controls the next
publication. This gives the port a direct path to ordinary `GfxState` objects: rasterize the complete layout
surface first, then materialize these exact bounded records in handle order rather than adding a second
overlay compositor.

`adv_text_layout_check_overflow@0x45efc0` sets bit 1 only when `right_bound < glyph_right` and bit 2 only
when `bottom_bound < glyph_bottom`; equality fits. Vertical overflow terminates the builder. With horizontal
wrapping enabled, right overflow inserts a line break before the current glyph except for CP932 `0x8141`,
`0x8142`, and `0x8176` (`、`, `。`, `」`), which remain attached to the preceding line even beyond the
bound. The line pitch is the positive primary font pixel height plus configured leading.

Timed `adv_text_publish_next_glyph@0x451220` binds only while the reveal index is below layout `+0x6c`,
the configured retained-object capacity; reaching the capacity ends the timed service even if more records
exist. SYSTEM4 configures all nine Himegari layouts with `0x1f4` (500) handles. The decoded static dialogue
corpus contains 30,057 rows, has a maximum individual show-text string length of 68, and contains no
500-character string, so shipped ordinary text does not approach that limit. This is a corpus safety
baseline, not permission for a mod-facing renderer to overrun the interval.

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

Matching evidence: `build/games/himegari/native-adv-text-trace.jsonl` and Godot timeline captures at `0x834`, `0x9b2`,
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
  `cos(a)*effect_x` / `sin(a)*effect_y`, with `a = angle*180/pi`. The caller steps the angle by
  `360 / (sqrt(effect_x+effect_y)*8)`, blits the effect-colored glyph at every rounded offset, then draws the
  primary glyph. All 164 paired sites use `(1,1)`, which gives a one-pixel outline over six of the eight neighbours
  ("AntiFont off" below, which corrects the first reading).

**Measured cell versus raster ink (2026-07-30).** `adv_text_build_glyph_records@0x4576c0` stores
the `GetTextExtentPoint32A` measured cell edges in each retained record, and
`adv_text_publish_next_glyph@0x451220` uses those edges when it binds the native object.
`text_raster_string_cached@0x45b600`, however, composites the complete `GetGlyphOutlineA` black-box
bitmap and effect copies into the shared layout surface. Direct 24-pixel bold Mincho probes show several
ordinary Japanese glyphs reaching below the 24-pixel measured cell even before the mode-3 `(1,1)` outline.
Thus the record is a layout/publication cell, not a guaranteed ink bounding box.

**Correction: the baseline is `tmAscent` (2026-11-03).** The overflow above came from a wrong baseline,
not from GDI ink. Every DrawMode-1 raster caller (`text_draw_string_primary_or_ruby@0x44fac0`,
`draw_string_to_surface@0x450150`) passes text manager `+0x458` as the cell-top-to-baseline offset.
`text_rebuild_primary_font_resources` fills a `TEXTMETRICA` at `+0x454` with `GetTextMetricsA`, so `+0x458` is
the primary font's `tmAscent` (ruby: `+0x490`). For 24-pixel bold Mincho that is 21, not 24. The port had used
the pixel height, which drew every glyph 3 pixels low and pushed ink below the cell. The port then compensated
with a vertically widened publication crop. With the baseline at `cellTop + tmAscent`, native and port page 3
of SC0000 agree to 20 white pixels in 4,444 with no offset. Native's own glyph objects use exactly the
measured cell as their source rect (every SC0000 page-3 object ends at `0x47`/`0x67`), and so does the port
now. `GlyphMask.Ascent` carries `tmAscent` from the GDI backend and `FontGetAscent` from the portable one.

At the script/AGE request boundary, the ordinary ADV preset is therefore 24-pixel `ＭＳ 明朝`, weight
700, white fill, mode-3 `(1,1)` outline in `0x606060`, with 8 pixels of line leading. The small
menu-description preset is 16-pixel `ＭＳ ゴシック`, weight 0, white fill, mode 1 with zero displacement,
and 9 pixels of leading. This describes AGE's logical `LOGFONTA`; the reference launcher's realized font
is a separate boundary qualified below.

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
authored Mincho face. The live qualification below shows that the wrapper-launched reference realizes that
request as Gothic, so this is an input-family mismatch rather than a Mincho weight mismatch.

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
   surface, an opaque glyph or a destination with alpha zero receives the source RGB and glyph alpha
   directly. Otherwise destination alpha becomes `max(destination alpha, glyph alpha)` and each RGB
   channel becomes `(dst*(255-alpha)+src*alpha)/255`, with integer truncation.
4. Mode 3 does not invoke a font-renderer outline. Its step is `360/(sqrt(effect_x+effect_y)*8)`, with
   nearest-integer ellipse coordinates at angles converted by `180/pi`. Radius `(1,1)` makes twelve calls covering
   six neighbouring offsets, each twice, followed by one primary-color glyph blit at the unshifted position. (The
   first reading, eight neighbours, is corrected under "AntiFont off" below.)

Mode 2 uses the same primary color for a second unshifted pass after the ordinary primary glyph, but shifts
every cached coverage value right by two first. On a transparent destination this lower-coverage duplicate
does not change the already-written source RGB/max alpha; on an existing colored pixel it performs a second
integer interpolation toward the primary color. Himegari does not select mode 2, but the shared compositor
still reproduces it.

The port does none of those raster operations. It loads the same TTC into Godot/FreeType with grayscale
antialiasing, light hinting, automatic subpixel positioning, embedded bitmaps disabled, and otherwise
default `FontFile` policy. Bold is the unrelated `FontVariation.VariationEmbolden=0.53` operation plus
one pixel of artificial glyph spacing; mode 3 is Godot `Label`'s `outline_size=1`. Thus the family,
nominal size, placement, and total width can agree while stems, serifs, white-core coverage, and edge
pixels differ. This explains why the statically reconstructed Mincho path differs from Godot, but the live
qualification below supersedes it as the explanation for the user's native comparison: the patch-wrapper
reference selects Gothic before rasterization.

The fidelity correction is consequently a decoded glyph-mask backend, not screenshot-driven embolden
calibration. The Windows implementation calls the same GDI APIs with the decoded `LOGFONTA` and reproduces
AGE's integer compositor. A portable backend must expose the same mask/metrics/compositing contract while
explicitly defining its font-substitution and rasterizer policy; FreeType output should be treated as that
backend's result, not claimed to be GDI-equivalent. Godot's one-pixel line-box compensation remains
independently supported by the matching viewport placement. Portable configurable face
substitutions/defaults remain deferred to the broader runtime configuration design. The scoped architecture
and acceptance gates live in `docs/remake-architecture-and-roadmap.md` under “AGE-exact glyph-mask text
renderer.”

The platform-neutral half of that correction landed on 2026-07-30 under `Ome.Engine.Text`.
`GlyphRasterRequest` carries face, positive pixel height, native requested width, weight, Unicode scalar,
optional original CP932 code, and an explicit native/portable policy. `GlyphMask` owns normalized 0..16
coverage plus stride, GDI-style origin, cell extent, and advance. `AgeGlyphMaskCompositor` implements the
32-bit branch above without calling an OS or Godot API; its synthetic oracle includes transparent and
existing-alpha pixels, negative bearings/clipping, all four effect modes, and overlapping mode-3 masks.

`RetainedGlyphLayoutEngine` consumes any `IGlyphMaskRasterizer`, writes those masks into an `RgbaImage`,
and returns the five-dword edge records plus final cursor, observed overflow, wrap count, and vertical-stop
state. It preserves native horizontal precedence: vertical-only overflow stops before compositing, while a
simultaneous horizontal overflow follows the wrap/kinsoku path. A reusable bounded LRU provides eviction
callbacks for future native font handles; `CachedGlyphMaskRasterizer` applies the same bound to masks. This
core now drives immediate surface strings, live ADV, and retained History.

The independent Windows reference backend landed alongside, but outside, the neutral engine as
`Ome.Engine.Text.Windows.WindowsGdiGlyphMaskRasterizer`. It holds one `CreateICA("DISPLAY")` information
context and selects bounded, safely disposed `CreateFontIndirectA` objects carrying the requested negative
height/width, weight, `DEFAULT_CHARSET`, and raw CP932 face bytes. Each request preserves the original one-
or two-byte CP932 code for `GetGlyphOutlineA(GGO_GRAY4_BITMAP)` and `GetTextExtentPoint32A`; returned masks
flow through the same normalized `GlyphMask` contract without another pixel conversion.

`GetGlyphOutline`'s queried allocation size is not always identical to the rows described by
`GLYPHMETRICS`. On the reference Windows installation, 24-pixel bold MS Mincho `p` reports a 12x15 box with a
12-byte row stride but requests 192 bytes rather than 180; a live SC0000 run likewise produced 672 bytes for a
25x23 box with 28-byte rows. The adapter therefore allocates and reads the complete queried size, as the API
requires, while exposing only `stride * gmBlackBoxY` row bytes to the normalized mask/compositor. Extra storage
is outside the drawable metric box and can contain unspecified data. Zero-byte spacing glyphs retain their
separate transparent-mask handling; nonzero undersized buffers still fail.

This exact ANSI reference requires Windows system ACP 932. `TryGetAvailability` reports the active-platform/
code-page incompatibility and the constructor refuses to masquerade as exact when unavailable.
`GlyphRasterizerBackendInfo` identifies the backend as `windows-gdi-gray4`, native-CP932, and pixel-exact.
On the reference installation, three test requests—24px regular Mincho `あ`, 24px weight-700 Mincho `姫`,
and 16px weight-700 Gothic `ア`—match an independently created Unicode GDI font/DC call byte-for-byte in
coverage and exactly in black box, aligned stride, glyph origin, cell extent, and advance. The backend is
now selected by Godot for immediate surface strings when `TryGetAvailability` confirms Windows ACP 932.

**Live-output qualification (2026-07-30).** A same-line comparison at `SC0000@0x869`
(`その地には、魔王と呼ばれる者がいた。`) first disproved the stronger claim that matching the
reconstructed request already proves visible native parity. The native capture has 2,391 pixels at
luminance 240+ inside the text crop; the forced `windows-gdi-gray4` port capture has 1,347, with nearly the
same bounds. The user's port capture and a fresh forced-GDI capture have identical threshold counts at every
measured level. Forced portable is substantially narrower, while forced-GDI GPU and software captures are
effectively identical, ruling out portable substitution and retained GPU sampling. An independent
offscreen `CreateICA`/24px `ＭＳ 明朝`/width -12/weight-700 Gray-4 request plus the decoded compositor
reproduces the port capture with luminance MSE 2.68.

A focused live Frida capture then established the missing boundary. At all 16 intercepted per-glyph raster
entries for `SC0000@0x869`, the manager still carries height `-24`, width `-12`, weight `700`, AntiFont 1,
version 3, and the UTF-8 bytes of the authored `ＭＳ 明朝` face. The `HFONT` actually selected in AGE's
display IC is different: `GetObjectA` reports face `MS Gothic`, `SHIFTJIS_CHARSET` 128, height `-24`, width
`-12`, and weight `700`; `GetTextMetricsA` reports height 24, ascent 21, descent 3, average width 13, and
maximum width 48. The intercepted `GetGlyphOutlineA` format is 5 (`GGO_GRAY4_BITMAP`) with identity `MAT2`
and 25-pixel Japanese cell increments.

Thirteen uncached glyph masks from that line were saved at the call boundary. Replaying their CP932 codes
through an independent `MS Gothic` request matches all 13 in black box, origin, cell increment, stride, and
every 0..16 coverage byte. Both `MS Gothic` and `ＭＳ ゴシック` produce 13/13; `MS Mincho` and
`ＭＳ 明朝` produce 0/13. This is the visible flourish/stroke-weight cause. It is not a compositor,
coverage scaling, GPU, or weight problem.

The distinction comes from the reference launch path, not the script preset. `AGE Patch.exe` loads
`jprun.dll`; that DLL imports/detours `CreateFontIndirectA` (and related font APIs) and contains repeated
hard-coded `MS Gothic` family strings. The comparison screenshots therefore show the patch-wrapper
realization of AGE's Mincho request as bold MS Gothic, not true unwrapped native rendering.
`windows-gdi-gray4` remains the correct native-default path because it reproduces AGE's authored Mincho
request directly. Wrapper-specific realized-face substitution is outside the native-AGE compatibility target;
no launch/profile override is planned for it.

`ImmediateSurfaceTextRenderer` builds the complete CP932 request list before touching the destination,
making an unsupported character an atomic fallback rather than a partially rasterized string. It applies
the decoded LOGFONT rebuild rules (default 24px Mincho, odd heights rounded down, 32/33 mapped to 31,
negative half-width, and weight 700 only for bold), then advances from GDI's returned cell metrics while
the shared compositor writes into a cloned `RgbaImage`. `GodotAdvHost.DrawStringToSurface` publishes that
snapshot for ops `0x204` and `0x205`; successful exact draws add no `SurfaceTextDraw` metadata. Text on a
bound surface is therefore ordinary texture data in both software and GPU retained renderers: later handles
can occlude it, and the owning object's alpha/tint, animated fade, affine transform, source rectangle,
offscreen capture, fill/copy, and preservation behavior apply without a detached overlay. A bounded
2,048-mask cache sits above the already bounded GDI font cache.

If the exact backend is unavailable, string conversion fails, or the target surface cannot be resolved,
the host records one explicit diagnostic and retains the old metadata/Label projection for that draw. The
fallback is constructed before surface publication, so it cannot mix a partial new raster with the Label.
This fallback now covers immediate, live, and History text; the non-glyph wait indicator is always an
ordinary retained atlas object when its script binding is valid.

Live ADV now uses the same exact surface pixels through ordinary retained objects.
`AdvTextLayoutPresentationBinding` is transient engine state: it carries the layout surface (`slot+0x14`),
first object handle, capacity, wait-indicator handle, and reset cursor without changing the persisted
History record layout. The VM passes that binding and its `GfxState` to the host for each `show-text` run,
then writes the returned GDI-measured cursor into the canonical live layout before processing another
literal/dynamic run.

`RetainedAdvTextLayoutPresentation` stores each five-dword record together with the layout origin active
when that run was built. Publication binds `first_handle+index` from the record's source edges to
`run_origin+(left,top)`, so MAMES-style origin changes do not move earlier glyphs. The complete run is
rasterized once into the layout RGBA surface; timed reveal only adds another `GfxState` binding and never
rerasterizes or reuploads the complete line. The configured capacity bounds timed publication. Click or
Skip sets the existing force-complete flag, binds every remaining publishable record, synchronizes that
endpoint, and returns without releasing the subsequent stable input wait.

Because these are normal handles, a partial `0x1f7` removes only the selected glyph. Op `0x20a` walks the
retained records through the current reveal count and recreates missing bindings. HIDEWIN suspension erases
the active page's published range and restore republishes it; layout reset erases the configured interval,
drops transient records, and zeroes the source surface. Whole scene reset/release drops the transient layout
state. Numbered History serialization still owns none of these pixels or records: the saved-frame script
path reconstructs them, and a reconstruction test proves identical handle/source/destination bindings.
Exact runs are omitted from `SnapshotLiveAdvText`, eliminating the detached always-on-top Label while the
unavailable-backend fallback continues to use that snapshot.

#### AntiFont off: `message:UseAntiFont` 0 (2026-11-06)

Text manager `+0x544` (`text_antialias_enabled`) picks the glyph format.

- **Startup.** `adv_text_manager_construct@0x456570` sets the flag to 0. At `0x41550b`, AGE applies `message:UseAntiFont`
  through `text_set_antialias_enabled@0x4137e0` only when `set:EnableAntiFont` is nonzero. Himegari and Kamidori both set
  `ENABLEANTIFONT=1` and keep the default of 1.
- **The 設定 dialog.** The ﾒｯｾｰｼﾞ page's 文字の縁をなめらかにする (check 1000) is applied on OK through IAGEService `+0x68`.
  That service writes the key, then calls the setter.
- **On a change.** The setter rebuilds the font resources, which do not depend on the flag, and empties both glyph caches.
  It does not redraw: a native capture after OK shows page 1 unchanged.

With the flag clear:

- **Glyph format.** `text_raster_string_cached@0x45b600` and `text_raster_string_uncached@0x459d90` call
  `GetGlyphOutlineA` with format `(flag != 0)*4 + 1`, which is `GGO_BITMAP`. They step rows by `(w >> 5)*4 + 4` bytes.
  That is GDI's DWORD stride, except at widths that are multiples of 32: there AGE reads one DWORD more per row than GDI
  wrote, so the rows skew.
- **Compositing.** `text_blit_glyph_bitmap@0x458c80` reads the bits MSB first and blits each set bit at alpha 255. An
  aliased glyph pixel therefore replaces the destination.
- **Route.** With the flag set, three callers reach the raster workers directly, through
  `text_draw_antialiased_effect_string@0x45d700` and `text_draw_antialiased_plain_string@0x45a110`:
  - the ADV glyph-record builder (`adv_text_build_glyph_records@0x4576c0`);
  - the retained redraw;
  - History (`text_history_render_records@0x4526c0`).

  With the flag clear, they call `text_draw_string_primary_or_ruby@0x44fac0` instead. For a mode 1–3 string, that
  function starts the cached rasterizer at `(x + effect_x, y + effect_y)`, and the rasterizer then adds its own effect
  offsets. The records keep the measured cell. Himegari's mode-3 `(1,1)` text therefore lands one pixel right and down
  of its antialiased position. Two kinds of text do not move: mode-0 text, and op `0x204`
  (`draw_string_to_surface@0x450150`), which calls the workers directly.
- **Mode 2.** Its second-pass buffer is `(signed char)byte >> 2` over the cached bytes. For packed bits, that moves each
  byte's eight pixels two to the right and repeats the byte's first pixel into its first two. The pixels still draw
  opaque. Neither game uses mode 2.

**Mode-3 outline samples (correction).** The earlier outline description was wrong in two places. The AntiFont-off
capture settled it, since its outline pixels are exactly `0x606060`.

- **Step.** The step is `360 / ((float)sqrt((float)(effect_y + effect_x)) * 8)`: the square root of the sum, not of the
  sum of squares. The angle is a float stepped from 0 while it stays below 360, so there is always at least one sample.
- **Angle conversion.** `text_compute_outline_ellipse_offset@0x459860` converts the angle with `angle * 180 / pi`
  (constants `0x5779f8` = 180.0 and `0x577fc0` = π), the inverse of degrees to radians. The samples therefore fall at
  scattered directions.
- **Result.** For `(1,1)` the twelve samples are (1,0), (1,1), (0,1), (−1,0), (−1,−1), (0,−1), twice over. That covers
  six neighbours, never (+1,−1) or (−1,+1). The smallest rounding margin is 0.026, at sample 1, so x87 precision cannot
  change the set.

The port had used degrees to radians and all eight neighbours in both modes. With AntiFont on, SC0000 page 1's text
then differed from native in 494 pixels, 54 of them by more than 24 levels; it now matches exactly.

**Port.**

- **Setting.** `GlyphRasterRequest.Antialias` carries the flag, so the mask cache keeps the two formats apart; AGE
  empties its cache instead. `GodotAdvHost` holds the flag:
  - `Sys4RegIniStore.TextAntialiasEnabled` seeds it at startup, from `EnableAntiFont` and `UseAntiFont`;
  - the 設定 dialog's OK sets it through `IAgercSettingsHost.SetTextAntialias`.
- **Rasterizers.** `WindowsGdiGlyphMaskRasterizer` reads `GGO_BITMAP` with AGE's row stride (`UnpackNativeBitmapRows`)
  into a `Monochrome` mask of 0/16 coverage. The portable backend renders with `FontAntialiasing.None` and thresholds
  the result.
- **Layout.** `RetainedGlyphLayoutEngine` applies the effect-offset start; it lays out live ADV text and History.
- **Compositor.** `AgeGlyphMaskCompositor.ModeTwoBitmapPass` builds the bitmap mode-2 pass, and `GetMode3OutlineOffsets`
  follows the native samples.
- **Check.** `tools/drive/scenarios/himegari-antifont-off.steps` turns the setting off on page 1:
  - page 2 and History then match native pixel for pixel;
  - page 1 is identical before and after OK on both targets;
  - apart from the wait indicator's phase, nothing differs anywhere.

  A profile started with `UseAntiFont=0` draws page 1 aliased on both targets. With AntiFont on, page 1 now matches
  native pixel for pixel too.

#### Furigana: op `0x196` (2026-11-06)

`op_0x196_display_furigana@0x41f2a0` takes (layout slot, base text, ruby). It is op `0x6e` with the ruby passed as
`adv_text_build_glyph_records@0x4576c0`'s third argument, with two differences: it does not re-read
`message:ReadTextSkip`, and it does not call `sleep_timer_arm`. Himegari's scripts run it 279 times and Kamidori's
3,054.

**The ruby font.** `adv_text_manager_initialize@0x456800` copies the initial primary LOGFONT into the ruby one
(`+0x508`), then sets lfHeight -8, lfWidth -4 and the face ＭＳ ゴシック (`0x577f60`). Op `0x1a5` changes only the primary
face, so ruby text is always MS Gothic at the default weight. Op `0x197` (`text_set_ruby_font_size@0x415c40`) sets the
size with the primary font's adjustment (odd sizes and 32 lose a pixel, 33 becomes 31); every Himegari script sets 8.

**Layout.** The builder draws the base text as ordinary glyphs. After each row's History record it hands the row's
share of the ruby to `adv_text_draw_ruby_over_row@0x456b40`, with the row's width (last glyph right minus first glyph
left) and its first glyph's cell.

- A row before a wrap takes `((rubyLeft * rowBytes) / baseLeft / 2) * 2` CP932 bytes of the ruby. `rubyLeft` and
  `baseLeft` count the bytes still undrawn from the row's start. The last row takes the rest.
- `difference = row width - ruby width`. When it is positive, the gap `difference / (n + 1)` goes before, between and
  after the n characters (n is the CP932 length over two). Otherwise the ruby is centred and overhangs both sides by
  `-(difference / 2)`.
- The first cell starts at `(first.left - overhang + gap, first.top + lfHeight + 1)`. A render-mode-3 effect raises
  it by the effect's y offset. Himegari's page-18 ruby かがりび over 篝火 (48 px) sits 8 px above the base with 3 px gaps.
- Each two-byte character is drawn plain, with no effect, in the text color. With AntiFont on that goes through
  `text_draw_antialiased_plain_string` (ruby lfWidth and tmAscent, `+0x50c`/`+0x490`); with it off, through
  `text_draw_string_primary_or_ruby` in mode 0 with the ruby flag.
- Each character's record keeps its own extent, and the cursor returns to the end of the base text.

**Publication.** The builders always draw into the layout's last (pending) record and then push the next one. The base
builder pushes chain flag 0, `adv_text_draw_ruby_over_row` chain flag 1, and a layout reset
(`adv_text_layout_reset_cursor_and_bounds_record@0x455070`) starts over with 0. Each tick of
`adv_text_publish_next_glyph@0x451220` publishes one record and keeps going while the record it just published has chain
flag 1.

- 篝火 + かがりび therefore publishes 篝 | 火 | か | がりび: four ticks.
- The record after the last ruby character keeps chain flag 1. The next run's first glyph therefore shares a tick with
  its second: の如 | く | ….

Native SC0000 page 18 at `MessageSpeed=100` (2026-11-06) took 0.40 s in op `0x196` and 2.30 s for the following 24
glyphs: 4 and 23 ticks.

**History.** Each ruby character gets its own record: flags | 4, the character's rectangle, size `-lfHeight` (8),
render mode 0. The records follow their row's record and come before the row's line break.
`text_history_render_records@0x4526c0` gathers a run of flag-4 records into one string and spreads it over the record
drawn before it with the same rule, using the current ruby font. That record's width is measured in History's current
font.

**Port.**

- `VirtualMachine` runs op `0x196` through `ShowTextRun`, as op `0x6e` does, with `AdvLiveTextRun.Ruby`.
- `Ome.Engine.Text.RubyTextLayout` holds the font, the row split and the layout. `RetainedGlyphLayoutEngine.RenderRuby`
  draws a run's ruby and `RenderRubyOver` History's.
- `GodotAdvHost` appends each row's base glyphs and then its ruby, in AGE's order.
- `RetainedAdvTextLayoutPresentation.AppendRun` applies the chain rule across runs. The reveal counts ticks through
  `PublicationTarget`.
- `AdvTextHistory` keeps the flag-4 records (`AdvTextHistoryRecordFlags.Ruby`), and `TryBuildHistoryRenderBatch` turns
  them into `AdvTextRubyAnnotation`s. Op `0x82` draws each ruby record at its own rectangle ("Op 0x82 redraw").
- Numbered saves keep the records in native's record layout (not yet compared with a native save of a furigana page).
- Tests: `RubyTextLayoutTests` (synthesized).
- Check: `tools/drive/scenarios/himegari-furigana.steps`. Page 18 and its History match native pixel for pixel in
  every text row, the ruby included.
- Kamidori's engine differs: "Furigana in SYS4433" below.

#### Furigana in SYS4433 (Kamidori, 2026-11-07)

Kamidori's engine has neither of Himegari's two ruby code sequences. Its live ruby is
`adv_text_draw_ruby_over_row_sys4433@0x4590d0` and its History renderer is `@0x456490`; op `0x196`'s handler is
`0x41d3c0`. The ruby font (8 px ＭＳ ゴシック), the row split and the record layout are Himegari's. The differences:

- **Spacing.** The gap divides by `(bytes + 3) >> 1` instead of `bytes / 2 + 1`. A single-byte ruby character is
  followed by half the gap, and a control character draws as a space.
- **Vertical placement.** The live ruby starts one pixel lower only when `set:DrawMode` is not 1 and
  `message:RubyShiftMode` is 0. History's ruby never adds that pixel. A render-mode-3 effect raises the ruby by its y
  offset only when `RubyShiftMode` is 0. Kamidori has DrawMode 1 and `RUBYSHIFTMODE=1`, so its ruby sits at
  `base top + lfHeight` with no raise. With `RubyShiftMode` 0 and DrawMode 1, History's sprite publication also
  samples its source one pixel lower. The port does not model that, since no shipped SYS4433 game runs that way.
- **Publication.** Every ruby record gets chain flag 1, and the ruby builder resets the pending record to 0. A run's
  whole ruby therefore publishes in one tick with the base glyph before it, and the next run starts its own tick.
- **The glyph timer.** Op `0x196` re-arms the glyph timer as op `0x6e` does, which SYS4422's handler does not.

`RUBYSHIFTMODE` comes from the SYS4INI trailer. SYS4433's `SYS4REG.INI` loader also reads `message:RubyShiftMode`
over it, but never writes it ([sys4-format-notes.md](sys4-format-notes.md), "Native SYS4REG.INI engine options").

**Port.** `RubyLayoutRules` holds each revision's rules: `Sys4422`, or `Sys4433(rubyShiftMode, drawMode)` from the
profile's engine ABI, its SYS4INI and the loaded `SYS4REG.INI` (`RubyLayoutRules.For`). `RubyTextLayout`,
`RetainedAdvTextLayoutPresentation.AppendRun` and the glyph timer read them.

**Check.** `tools/drive/scenarios/kamidori-furigana.steps` (2026-11-07) shows the prologue page `SC0000@0xfa2`, with
はんらん over 叛乱, and then its History. The translation blanks every ruby, so it runs on the Japanese base (the port
without `--overlay-root`, native with the scratch install's `patch/` renamed). History matches native in every
near-white text pixel (none of 46,144 differs by more than 30). Inside the page's text window only the wait
indicator and 4 pixels of the window's top edge differ. Kamidori's base text first needed its own font realization and placement ("Aspect-mode font
calibration", "Aspect-mode glyph placement").

#### The reveal clock (2026-11-06)

A timed reveal publishes on a period timer, not on elapsed time since the run began.

**The timer.** `engine_ctx_construct` builds 32 timer objects of 0x1c bytes at `ctx+0x5f304`. Their constructor,
`period_timer_construct@0x44cfb0`, leaves period 0 and next index 1. The reveal uses timer 29, `ctx+0x5f630`
(`adv_glyph_reveal_timer`).

- Op `0x6e` arms it with `sleep_timer_arm` after the builder, with the glyph delay (`ctx+0x14e9c`) as the period. It
  does so only on the timed path: not with delay 0, not under read skip (`0x8000000`), and not when the builder fails.
- Op `0x196` never arms it, and no other code does.

**A tick.** `adv_text_reveal_service_tick@0x406fa0` checks input first (click, fast-forward, wheel). Then, in draw mode
1 (`ctx+0x99290`, Himegari), it polls `period_timer_poll_elapsed_periods@0x44d0f0`. While the poll answers -1 the tick
returns; otherwise it publishes one tick (`adv_text_publish_next_glyph`). Draw mode 0 instead calls `Sleep` with the
delay and then publishes.

- The poll succeeds once the time since the arm exceeds index × period, and then sets the index to elapsed / period +
  1.
- So the first tick waits a full delay, and a run of n ticks ends after n delays.
- A late poll publishes one tick and drops the boundaries it missed; the reveal never catches up.

**Op `0x196` keeps the previous phase.**

- Right after a show-text the index is one past that run's last tick, so the furigana's first tick waits one more
  delay.
- Long after the last arm, its first poll succeeds at once, and the later ticks follow the old timer's phase.
- Before any op `0x6e` in a session the period is 0, so the reveal waits for a click (decoded, not observed).

**Native trace.** SC0000 page 18 at `MessageSpeed=100` (2026-11-06; time from each op to the next):

| Run | Ticks | Native | Port before | Port now |
|---|---|---|---|---|
| op `0x6e` 今にも消えそうな (`0x1a49`) | 8 | 0.802 s | 0.72 s | 0.799 s |
| op `0x196` 篝火 + かがりび (`0x1a50`) | 4 | 0.399 s | (not drawn) | 0.400 s |
| op `0x6e` の如く、… (`0x1a55`) | 23 | 2.301 s | 2.32 s | 2.302 s |

The 0.72 s is the old port's (n - 1) delays, which showed the first tick at once.

**Port.**

- `Ome.Engine.Hosting.PeriodTimer` implements the arm and the poll.
- `GodotAdvHost` keeps one timer. It arms it when a run with `AdvLiveTextRun.ArmsGlyphTimer` (op `0x6e`; the VM sets
  false for op `0x196`) takes the timed path.
- The reveal loop polls on the engine clock and wakes at the timer's next due millisecond. It requests a
  presentation for each tick and does not wait for it, since a frame wait would make polls late and drop ticks.
- Tests: `PeriodTimerTests`.
- Check: `tools/drive/scenarios/himegari-reveal-clock.steps` (page 18 at 300 ms).

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
`GodotAdvHost` retains configurations by layout and pairs them with op `0x212`'s script-owned handle and
layout origin. `RetainedAdvWaitIndicatorPresentation` binds the current atlas cell as an ordinary
`GfxState` object, mutating it only when the 48 ms frame boundary changes. Op `0x20a` recreates an erased
current frame; HIDEWIN suspension and wait-service shutdown erase the handle. The former Godot
`TextureRect` overlay and raw-callback visibility exception are removed, so normal handle order, source
color key, capture, and surface/scene composition apply. A real Vulkan SC0000 page-one capture places the
retained bat at `(385,570)`.

A 2026-07-27 polish fix removed the port's erroneous `TerminalFrame + 1`
interpretation, which had selected nonexistent frame `12` once per cycle and made Godot render one empty
atlas cell. Frame selection now treats the terminal value as the exclusive frame count and loops `0..11`.
The retained presenter requests a new frame only when that source cell changes, preserving the static-wait
compositor optimization. Full SYSTEM4 replay remains Phase-B work; the shortcut carries the same state in
the meantime.

**SYS4433 follow mode (2026-11-07).** Kamidori configures its indicator with op `0x2bc` and turns on follow mode with
op `0x1b1` (`SYSTEM4@0xb7`, text manager `+0x570`); Himegari never does. In follow mode
`adv_wait_indicator_draw_frame_sys4433@0x4526d0` places the indicator at the layout origin plus the configured offset
(`+0x50`) plus the left/top of the layout's last 20-byte glyph record (`*(layout+0x30) - 0x10`). The builders always
draw into the last record and then push the next one at the cursor, so that record is the pending one just past the
last glyph. Op `0x6f`'s line end, op `0x7a` and a layout reset move it. The port used to anchor on the last glyph's own
left, one cell left of native. `GodotAdvHost` now keeps each layout's cursor (`_layoutCursors`), set by every drawn run
and by `IAdvHost.SetAdvTextCursor`, which op `0x6f` now calls too. On `SC0000@0xfa2` the indicator now lands where
native draws it.

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
`sound_buffer_set_volume@0x483f80` applies that inherited attenuation; neither value is supplied by these
five handlers. The attenuation comes from `sound_channel_apply_volume@0x4830d0` and AGE's shared volume curve
(see "Native volume curve" below); the port reaches the same gain through its mixer buses, and pan stays centered.

**Native RIFF boundary contract (2026-07-29).** The RIFF branch constructs
`wav_decoder_ctor@0x4885b0`; `wav_decoder_open@0x488790` then opens the packed-entry handle with WinMM
`mmioOpenA`, descends into the first `RIFF/WAVE` with `MMIO_FINDRIFF`, and searches that RIFF's children for
`fmt ` and `data` with `MMIO_FINDCHUNK`. At `0x488976`, the returned `data` `MMCKINFO.cksize` is copied into
both remaining-byte and total-byte fields. `wav_decoder_fill_buffer@0x4889b0` reads
`min(remaining_bytes, 0x2000)`, decrements that counter, and closes the decoder when it reaches zero.
Native playback therefore cannot consume bytes after the first `data` chunk's declared extent.

This normal RIFF behavior, rather than an asset-specific exception, makes malformed `A1215.WAV` playable.
Its packed entry is 323,009 bytes, but the first RIFF declares an extent of 157,940 bytes and its first
`data` chunk is 157,896 bytes. A multipart-upload header and a complete second RIFF begin after that declared
boundary; AGE ignores both. The whole-buffer Godot importer instead sees the appended `Cont` bytes as another
RIFF chunk and repeatedly seeks beyond the buffer. The port now reproduces the native boundary in its
transient Godot decoder copy, leaving the archive/VFS payload untouched. The saved `/v2` image names/comments
the WAV decoder constructor, open/close, buffer-fill, rewind, and query methods, and records this boundary
contract on the dispatcher and parser.

The native trace in `build/games/himegari/native-sfx-trace.jsonl` captures SC0000's first pair: `0xb4@0xc29` resolves
raw catalog id `0x28` to `E0808.WAV`, loads channel 0, and `0xb5@0xc2e` starts it in the same millisecond. The next
`0xb4@0xc31` preloads the same WAV into engine-owned secondary channel 4 for a later service start. The
channel comes from `G[0x6242d]`.

> **Correction (2026-11-05).** `G[0x6242d]` is script-written after all. `SYSTEM4@0x1ca` (op `0x64`,
> copy-inline-int-array) copies the SFX channel table `[0,4,5,6,9]` to `G[0x6242c..0x62430]` on every boot. The
> Frida write capture (`capture_global_writes.py`) missed it because op `0x64` writes the bank directly, not
> through `vm_operand_write`. The port's natural boot holds 4 there, as native does. The profile's SC0000
> "external" seed and the VM's external-global overlay are gone. Godot direct-scene runs are hosted by SYSTEM4,
> which makes the copy itself ("Native ADV hosting and Himegari's developer entry").

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

**Forced BGM lifecycle siblings (mapped 2026-07-29).** The remaining `0xb7`/`0xb8`/`0xb9` gap is one
coherent BGM tranche:

- `op_0xb7_force_play_bgm_loop@0x4202d0` calls `bgm_force_play_track@0x464a90` with start mode one.
  A nonzero operand replaces and starts that track; zero restarts the retained current track. Unlike
  ordinary `0xbf`, the force worker has no same-track early return. CALLBACK_LOAD uses zero after a
  numbered-load callback, and CONFIG uses BGM029 as its music-preview sample.
- `op_0xb8_stop_bgm@0x416ba0` calls `bgm_stop_release@0x464690`, clearing the retained track and stopping
  the active backend. CONFIG uses it when leaving the preview path; MMODE uses it before constructing the
  music-room screen.
- `op_0xb9_force_play_bgm_once@0x420330` calls the same force worker with start mode zero. Its sole
  Himegari site starts BGM025 as STAGECLEAR's one-shot fanfare.

All three first complete and cancel an active `0xc2` fade by clearing run-state bit `0x200` and calling
`bgm_fade_tick(...,100)`. Both start variants store their mode at
`EngineCtx.current_bgm_start_mode` (`ctx+0xa0b8c`) before invoking the backend. Himegari's OGG backend
forwards that value through `FUN_00482f00` to `sound_buffer_start`; the already-decoded
`sound_stream_fill_quarter@0x483b70` rewinds at EOF only when the mode is nonzero. Thus the distinction is
logical loop versus one-shot, not a mixer flag. A compatible implementation must extend the existing BGM
host seam with explicit restart and loop ownership, preserve `0xbf`'s current idempotence, treat operand
zero through the VM's retained track, and make `0xb8` clear that retained state.

The port now implements that split directly. `IHost.RestartBgm(track,startMode)` is distinct from ordinary
`PlayBgm`, so `0xbf` remains same-track-idempotent while `0xb7`/`0xb9` always replace the stream. The VM
resolves operand zero through `_currentBgmTrackId`; zero with no retained track calls the same idempotent
stop path as native. `StopBgm` clears the VM track and makes Godot cancel voice ducking and any deferred
fade, stop the player, release its stream, and restore neutral gain. Godot sets `AudioStreamOggVorbis.Loop`
from the native start mode. A `0xc2` fade that reaches target zero releases the source the same way (since
2026-11-07 the host does so when the background fade ends), replacing the former silent-but-still-bound Godot
stream with native release behavior.
A focused regression covers same-track restart, zero aliasing, loop/one-shot selection, track replacement,
stop/clear, fade-to-zero release, and restart after stop; the threaded self-test exercises both loop modes
and real stop/release.

**INPUTNAME native editor and CP932 string helpers (mapped 2026-07-29).** The three remaining
INPUTNAME-only gaps are one implementation slice, but have two ownership layers:

- `op_0x144_edit_fullwidth_string_dialog@0x42bdd0` is the engine wrapper for AGERC host command 10.
  Its ABI is `(result_inout, initial_text)`. AGE resolves both operands into separate 1024-byte buffers,
  suspends normal display ownership, calls `g_pfnAgercHostCallback(10,owner_hwnd,&{result,initial,0})`,
  writes both returned buffers through their string lvalues, and restores display ownership.
- A read-only Frida probe of the pristine running game resolved `g_pfnAgercHostCallback`
  (`AGE+0x1c6018`) to `AGERC.DLL+0x1050`. The companion DLL is now imported and annotated at
  `/support/AGERC.DLL`: `agerc_host_callback_export@0x10001050` forwards to
  `agerc_dispatch_host_command@0x100025f0`, whose command-10 branch opens dialog resource 10 with
  `agerc_fullwidth_text_dialog_proc@0x10002140`.
- The dialog initializes edit control 1006 from the second buffer. Accept reads no more than 255 bytes,
  rejects a result longer than 16 CP932 bytes with `字数オーバーです`, and walks it in two-byte cells through
  `agerc_reject_non_dbcs_character@0x10001b50`; any half-width or invalid cell produces
  `半角文字は使用できません`. A valid accept replaces the first buffer. Cancel closes without changing it.
  The dispatcher copies both buffers back, so operand 1 is the result/inout value and operand 2 is the
  initial edit text.
- `op_0x2c6_cp932_character_length@0x42a6d0` sets the C locale to `japanese`, calls MSVC `_mbstrlen` on
  operand 2, and writes the multibyte-character count to operand 1.
- `op_0x2c8_cp932_substring@0x42c420` copies its source into a 256-byte local buffer, counts Japanese
  multibyte characters, and selects `[start,start+count)` while walking `_mbbtype` lead/trail pairs
  together. Native clamping replaces the requested end with the full character length when
  `start+count < 1` or exceeds that length.

The release script's flow is now fully grounded. INPUTNAME's keyboard button assembles its current
character cells, copies that text into both `0x144` operands, then calls `0x2c6` and loops
`0x2c8(name,index,1)` to repopulate its eight cells. The pure helpers can use the VM's existing
`NativeStringCodePage` encoder. The modal op can follow the existing diagnostic-dialog ownership pattern:
the VM worker blocks while Godot's main thread presents a LineEdit dialog, validates the CP932 byte limit
and full-width-only rule, and signals the worker on accept or cancel. No save/profile format is involved.

**Port implementation (2026-07-29).** `Cp932Text` now owns the byte-level character count, substring,
and AGERC acceptance rules. The VM dispatches all three handlers and exposes the modal as an explicit
accept/cancel `IHost` exchange, so cancellation cannot overwrite operand 1. Godot uses a main-thread
`FullwidthTextEditorDialog` while its VM worker waits; over-16-byte and non-double-byte submissions retain
focus and show AGERC's exact Japanese errors. The helper operates on the C-string prefix and encodes through
the configured native code page, avoiding both UTF-16 indexing and accidental splitting of CP932 pairs.

`0xc2` is BGM rather than SFX. During ordinary execution, `op_0xc2_bgm_fade@0x4204c0` sets run-state `0x200`,
arms the service timer, and calls `bgm_fade_arm@0x464830`. `bgm_fade_tick@0x464960` linearly interpolates
current to target percent; durations at least 1000 ms take 100 steps, shorter durations take 10, and target
zero releases the source. Run-state `0x200` does not park the interpreter: the fade runs in the background
while ops keep dispatching ("BGM fades run in the background", below; the earlier reading that the VM is parked
for the duration was wrong). Active ADV fast-forward (`run_state_flags & 0x08000000`) takes a separate
opcode-local branch: it arms the requested target, applies the terminal fade tick immediately, and never sets
state `0x200`.
A normal advance click does not take that branch. `adv_input_service_poll@0x411230` uses action bit `0x10` to
release the preceding text wait, while only fast-forward bit `0x40` establishes `0x08000000`; once `0x200` is
active, `engine_main_tick_with_exception_policy@0x411e8a` services only elapsed timer steps and does not poll
click input for the BGM fade. (Until 2026-11-07 the Godot host parked its VM worker on the fade clock, after
publishing the pre-fade graphics burst and releasing the script presentation barrier so that `SC0000@0x7c1`'s
black frame showed during the wait; the port no longer waits.) `0xd9` is adjacent startup control, not audio data: it clears
run/service bit `0x1000` in the primary and, when active, secondary context and has no VM-visible result.

**Skip endpoint correction (2026-08-16).** The port initially sent every `0xc2` request through that blocking
host clock, so an already-active all-message or held-Ctrl Skip appeared to stop on SC0000 pages immediately
followed by a BGM fade. `SYSTEM4 P046` (`wait SC0000@0x4180`) is followed by a 3000 ms target-zero fade at
`SC0000@0x418d`; the captured run also contains the same pattern at P015 (3000 ms) and P090 (1500 ms). VM audio
dispatch now sends zero duration only for the same combined active ADV-skip predicate used by native endpoint
branches. Non-skip dispatch retains the authored duration, preserving native's deliberately unskippable-click
behavior. A focused regression covers both outcomes.

**BGM fades run in the background (2026-11-07).** `engine_main_tick_with_exception_policy` services run-state
`0x200` at `0x411e65`. It polls the fade's own period timer `ctx+0x5f400` (only op `0xc2`, the voice-duck release at
`0x411e48` and this service use it), calls `bgm_fade_tick` with the elapsed periods and clears `0x200` when the tick
returns 0. It then falls through to the other service tests. None of them matches `0x200` alone, so the tick reaches
the op dispatch at `0x4125eb`: the script runs on while the fade plays. At 100 % `bgm_fade_tick` commits the level to
`+0x420` (intermediate steps apply a level without committing it) and, for target 0, calls `bgm_stop_release`, which
also clears the retained track. Ops `0xb7`/`0xb8`/`0xb9`/`0xbf`/`0xc3` clear `0x200` and call `bgm_fade_tick(...,100)`
before they act. Every shipped fade site (547 in Himegari, 1497 in Kamidori) is followed by the same `0x1cf`
duck-control choice; what comes next varies. SC0000@0x7c1 plays the next track six ops later, which cuts the
title-music fade short at once. SC0000@0x126c goes on into the scene's presentation and text, which proceed while
the music fades. SYS4433 is identical (`op_0xc2_bgm_fade_sys4433@0x41e580`, service at `0x4120bb`).

Measured with `tools/drive.py` from the click on 始める (New Game) on a fresh sandbox:

| | Native | Port, parked VM | Port, background fade |
|---|---|---|---|
| Position when the click returns | `SC0000@0x7fa` (`play-bgm 5`) | `SC0000@0x7c6`, `waiting=sleep` | `SC0000@0x123df` 0.2 s later |
| First text page (`0x83f`) | 1.81 s | 5.02 s | 1.88 s |

**Port.** The VM keeps the pending target (`VirtualMachine.Audio.cs`, `_bgmFadeTarget`, the analogue of `0x200`). The
host runs the envelope and releases the source itself when it reaches a zero target. The play, force-play and stop
ops and a numbered load first end a running fade through `IAudioHost.CompleteBgmFade`. Op `0xc0` and a numbered save
settle a fade that has run its course (`IsBgmFadeActive` false), so a fade to zero clears the retained track when it
ends. Godot's tween applies each step through `ApplyBgmVolume` and commits only the endpoint, so a re-armed fade starts
from the committed level, as native's does. The 設定 dialog's music-route toggle no longer cancels a running fade:
in native only the tick and those BGM ops clear `0x200` (every `AND …, 0xfffffdff` in the image), so the fade goes on
whatever the route. The SYS4422 and SYS4433 profiles share this code.

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
main-thread scheduling race at the TITLE-to-SC0000 boundary. The VM's fade deadline begins before
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

**Native volume curve (2026-11-04).** Every native audio path converts a basis-point level with one expression:
level 0 is −10000 mB (silence), otherwise `trunc((ln(level) × 14.427 − 132.88) × 100)` mB, which is
`1000 × log2(level / 10000)` mB, or 10 dB per halving. The sites are:

- `sound_channel_apply_volume@0x4830d0` for SFX channels 0..9 and voice channel 12, with
  `level = master × channel / 10000` (`sound_set_channel_volume@0x483890`,
  `sound_set_master_volume_reapply_channels@0x483850`);
- `bgm_dshow_apply_volume_level@0x4610d0` for BGM (`IBasicAudio::put_Volume`), fed by
  `bgm_dshow_set_volume_level@0x4615a0` with `music × master / 10000`;
- `movie_audio_apply_volume@0x462610` for movies.

Before this the port scaled amplitude linearly, so a 50% setting played at −6 dB instead of −10 dB, and the
CONFIG reset values (music 6500, SFX 5000, voice 8000) were all too loud. `Ome.Engine.Model.NativeAudioVolume`
now holds the curve. Godot sets every bus with it; because the curve is logarithmic, the Master and category
buses' decibels add exactly where native multiplies the levels (up to native's integer truncation). Levels 1..9
would compute below −10000 mB, which DirectSound and DirectShow reject (the previous volume stays); the port
clamps them to silence.

BGM fades and the voice duck work in the same domain. `bgm_fade_tick@0x464960` interpolates
`((100 − p) × current + target × p) / 100` in basis points and the backend applies the curve, so a fade is linear
in level, not amplitude. Op `0xc2`'s target is a level in basis points. CONFIG and CALLBACK_LOAD (which restores
the BGM level after a load) pass `G[0x3236] × 100`, TITLE passes `0 × 100`, and every other site passes 0. The
port read it as a percent, so any nonzero restored level (above 1%) played at full volume. Godot now keeps the BGM envelope as a basis-point level (native `+0x420`), steps `0xc2` fades
100 or 10 times as native does, sets the duck to `MusicFadeOnVoicePlayingVol × 100` (0 reads as 100) and runs the
duck release from that level to 10000 in 100 steps.

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
current profile override replaces the resolved `REGFILEPATH` directory with
`user://games/<profile-id>/`, preserving the relative `SAVE` tail and yielding
`user://games/himegari/SYS4REG.INI` plus `user://games/himegari/SAVE` for Himegari. If a future profile
supplies unrelated paths, a single-root override is rejected instead of guessing; native resolution remains
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
the resId) loaded via `Ome.Cli trace SC0000 --state <snap> --trace-json` seeds the VM to match the engine's
**entire opening** (542 ops, no non-realignable fork) with ZERO manual seeding — confirming the
pre-scene-state theory and giving a general auto-seed for single-scene fidelity. **Residual:** a 2-op color
detour (`0x202/0x203` @ `0x122d0`, writing `G[0x62451]`) the full state does NOT fix = a real branch/op
difference to chase (not state). **Caveats:** snapshot is playthrough-specific (best for canonical entry
points — new-game opening, chapter starts); v1 captures global-INTS only (type 3; strings/floats TODO);
includes the scene's own early writes (can exclude by codebase for a pure pre-scene boundary).

**Gap (2026-11-05).** The capture sees only writes through `vm_operand_write`. Op `0x64` (copy-inline-int-array)
writes the bank directly, so `SYSTEM4@0x1ca`'s SFX channel table (`G[0x6242c..0x62430]`) is absent from it. Before
treating a global as engine-owned because the capture lacks it, check the scripts' bulk writes (op `0x64`, `0x1b0`
copy-dwords, `0x2d8` fill-int-array, indexed `lookup-array` stores), or watch the port's natural boot.

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
renderer (`text_history_render_records@0x4526c0`) pass `manager+0x560 - manager+0x4cc` as the line
advance. Offset `+0x4cc` is the primary `LOGFONT.lfHeight`, which AGE stores as a negative pixel height,
so the effective pitch is `font pixel height + text_line_spacing`. The corresponding vertical-writing
path uses the same pitch to move to the next column. Corpus values support that interpretation: scripts
pair 8 pixels with 22/24-pixel Mincho text and 9 pixels with 16-pixel Gothic text.

History records do not snapshot the `+0x560` field. `text_history_append_text_record@0x456000` retains
the row rectangle, text color, effect color, `+0x554`, render mode, pixel height, flags and string, but
op `0x1d1`'s History renderer reads only the string and flags:

> **Correction (2026-11-04).** This section formerly said the History record supplies its retained
> font/color/effect fields. The decompile disproves it, and native shots confirm it.
> `text_history_render_records@0x4526c0` measures each string with `GetTextExtentPoint32A` on the
> manager DC (the current font). It draws with the current `+0x54c` text color, `+0x550` effect color
> and `+0x558` render mode. It never reads record `+0x14..+0x24`. So History draws in HISTORY.BIN's own
> style, set at `0xf78`: 22 px ＭＳ 明朝 bold, line spacing 8, mode 3, effect `0x333e52`. It does not use
> the 24 px / `0x606060` style the ADV window recorded.
>
> Rows flow from the target layout's cursor: x advances by the measured extent. A line-break (flags-8)
> record resets x to the layout's reset x (`layout+0x1c`) and adds the current pixel height plus line
> spacing. There is no wrap and no bounds check, so the rows break exactly where the ADV window wrapped.
> Flag 2 swaps the operand colors in for the call.
>
> Op `0x82`'s `text_redraw_retained_group@0x451530` is different: it restores each record's colors and
> pixel height and draws at the record rectangle ("Op 0x82 redraw").
>
> The port builds an op-`0x1d1` batch in the VM's current `AdvTextStyle`, carrying the glyph indices of
> the group's line-break records (`AdvTextHistory.TryBuildHistoryRenderBatch`). The Godot host lays
> that batch out unbounded and breaks only there (`GlyphTextLayoutOptions.LineBreaksBefore`). Driven
> with `himegari-wheel-history.steps`, the port's History shots align with native's pixel for pixel;
> what remains is antialiasing (no channel differs by more than 160).

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
text records, and rebinds their draw objects. It snapshots global reveal index manager `+0x570`, clamps it
only to the layout's record count, and binds records zero through that index inclusive; unlike timed reveal,
this path does not clamp to layout `+0x6c` capacity. Himegari's 500-handle ranges keep the shipped path within
the configured interval. When run-state bit `0x40000000` is active, the opcode also calls
`adv_text_publish_wait_indicator_frame@0x453120` with the current indicator frame. The latter worker
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

The main loop also calls this worker, now named
`gfx_request_force_complete_and_reset_anim_clock@0x4076c0`, from the active run-state-`0x400` service.
At `0x412408..0x41245c` it reads `system:EffectSkipOnClick` (nothing below runs unless it is 1), rejects
service-flags bit 0, polls the triggered logical-action mask (taking the whole input latch), consumes
action-4 bit `0x10`, clears run-state `0x400`, and requests terminal sampling before `gfx_render_frame`. This is the generalized native CG-fade click path. It covers the ordinary finite
color/alpha and matrix channels serviced by `0x21c`, as well as queued type-0 surface commands; it is separate
from the legacy run-state-bit-8 fade service.

The ordering is a lifecycle contract, not incidental: native clears run-state `0x400` before calling
`gfx_request_force_complete_and_reset_anim_clock`. Service-flags bit 1 can make that worker a no-op, and
detached or movie-backed presentation can remain active, but none of those states re-park the interpreter.
The port must therefore track “the `0x400` wait was bypassed” separately from “all timed presentation is
inactive.” Deriving the wait solely from the latter produces an invisible-duration stall: ordinary fade
channels reach their endpoint, while an excluded movie/protected channel keeps the VM blocked.

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
8, and keeps ordinary script execution parked until the transition completes. Its three operands are source surface,
target surface, and a timing argument. For arguments up to 64, the handler arms that many milliseconds per
tick and advances the 8-bit alpha by 16. Larger arguments use `argument/16` milliseconds and alpha step
1. The branch is expressed in decompilation as `((arg < 65) - 1 & 0xfffffff1) + 0x10`; evaluating both
outcomes is significant (`true -> 16`, `false -> 1`). `interval_timer_poll_elapsed_steps@0x44d080` returns any missed timer intervals, and
`screen_transition_tick@0x43a7a0` mode 4 composites source first and target over it at the accumulated alpha;
at `0x100` it commits the target. `screen_transition_begin@0x439da0` and
`screen_transition_finalize@0x4399c0` own the endpoints. Thus an argument 10 lasts about 160 ms, while 30
lasts about 480 ms. The outer service's generalized skip paths are:

- If ADV fast-forward bit `run_state_flags & 0x08000000` is already set when `0x25` dispatches, the
  handler does not create a transition. It calls `screen_transition_finalize` for the target endpoint
  and advances normally.
- Otherwise the handler starts service bit 8 and calls `input_flush_action_triggers@0x405380`, preventing
  the input that entered the effect from also canceling it. With `system:EffectSkipOnClick` enabled,
  `engine_main_tick_with_exception_policy@0x411840` polls
  `input_poll_triggered_action_mask@0x460880`; logical action 4 (`0x10`) is consumed and
  `screen_transition_tick(0x10000000)` forces the active service to its endpoint. With it disabled nothing
  polls, and a click during the fade stays latched ("Effect skipping and the input latch").

The PC advances when the service begins. The handler's finalize branch is therefore the skip-at-dispatch
path, not a re-entry after natural completion; natural and click-forced progress lives wholly in the outer
service. Manual ROOM
validation caught the initially inverted alpha-step branch.

ROOM explicitly constructs those full frames. It selects/clears surface 1 and calls `0x20c`, mutates the
retained room presentation, captures surface 2 through another `0x20c`, restores the backbuffer, and calls
`0x25(1,2,10)`. The same form fades its menu away at `0x856`; after the farewell and one-second hold,
`0x25(1,2,30)@0x8dc` fades to the frame prepared for TITLE return. The interactive port now snapshots
retained objects when `0x20c` targets an offscreen surface, composites the captured target over the captured
source on the shared frame clock, blocks the VM through the endpoint, and only then permits the following
surface release/root reload. Headless hosts retain their non-rendering no-op policy.

**Port fix (2026-11-04): flatten the target before blending.** Mode 4 blends two complete surfaces. The
port's snapshots are retained-object lists, and it drew the target as separate objects at the progress alpha over a
black fill at that alpha. Mid-transition that dimmed the whole frame, background included, and let the
target's layers show through each other. This was seen when ROOM's menu faded in or out and on its sub-menu
entries.

`Main.CompositeFlattenedTransitionTarget` now draws the target opaquely over its clear (black, or the overlay
colour for the white fade) into the frame buffer. It then blends that over the flattened source with
`LegacyScreenTransitionComposite.BlendTargetOverSource`, so pixels the two surfaces share stay steady. A
driven ROOM entry's mid-transition frame is a plain linear mix of its two endpoints (35%, mean error 0.3
grey levels).

#### Single-surface black/white fade family — `0x21`–`0x24` (2026-07-29)

The widest remaining opcode `0x22` is not a new renderer. It and `0x21` are the single-surface siblings of
the already implemented mode-4 `0x25` transition. Dispatch-table resolution gives:

| op | handler | transition mode | endpoint |
|---|---|---:|---|
| `0x21` | `op_0x21_fade_surface_in_from_black@0x41cb00` | 0 | captured surface |
| `0x22` | `op_0x22_fade_surface_out_to_black@0x41cbc0` | 1 | black |
| `0x23` | `op_0x23_fade_surface_in_from_white@0x41cc80` | 2 | captured surface |
| `0x24` | `op_0x24_fade_surface_out_to_white@0x41cd40` | 3 | white |

All four take `(surface_slot, timing_argument)` and use the exact `0x25` timing
conversion: arguments up to 64 use `argument` milliseconds per tick with alpha step 16; larger arguments
use `argument/16` milliseconds with step 1. Modes 0/2 start with an opaque solid scratch surface over the
captured frame and lower its alpha to zero. Modes 1/3 keep the captured frame opaque and raise the
black/white scratch alpha to 255. With fast-forward inactive, they set service bit 8 and use the same
natural/action-4 forced-completion path described above. With fast-forward already active at dispatch,
they publish the named endpoint without starting the service.

Himegari uses only the black pair: three `0x21` calls and eight `0x22` calls, all `(1,30)` and therefore
about 480 ms. The scripts first select surface 1, clear it, publish a complete retained frame through
`0x20c`, restore the backbuffer, then fade. `0x21` is the menu-entry reveal; `0x22` is the corresponding
menu/scene exit to black.

The port dispatches both opcodes through the existing blocking `LegacyScreenTransition` clock and terminal
publication path. Its compositor already clears the whole frame to opaque black, so an empty source list
models black-to-captured and an empty target list models captured-to-black without overloading the mode-4
missing-surface fallback (which intentionally snapshots the live retained frame). Active ADV Skip/Ctrl now
marks the transition forced at dispatch, while action 4 is routed to the transition before retained ADV
hotspots can consume it. This closes 11 formerly
skipped instructions across seven distinct scripts and reduces the effectful-gap inventory from 18 to 16
distinct opcodes / 28 instructions.

The same manual pass exposed a resource-addressing issue after `0x6c` was fixed: ROOM did execute
`play-voice`, but `GodotAdvHost` attempted only inferred SC-section resolution. ROOM's voice operands
`0x3365..0x3376` are universal packed SYS4INI indices (for example `0x3365` is `EUA0016.OGG`). The first
compatibility fix tried an SC section and then a type-checked base-catalog fallback. Subsequent native RE
proved there is no first stage: ROOM and SC scripts alike pass an already absolute packed id directly to
the catalog opener. SC0000's `0x24 -> MAN999.OGG` works because that scene's inferred group begins at zero.

#### Legacy transitions and scratch surface `0x20` (2026-11-08)

Kamidori saves made on the port recorded surface `0x20` as created where native's had it clear (slots 01, 05,
09, 200 and 204 of the sweep, compared with `Ome.Cli save-diff`). The port marked the slot for every legacy
transition. Native writes it only for some modes, and Kamidori's scripts mostly use the two that never do: the
crossfade `0x25` (65 sites) and the strip reveal `0x28` (192 sites).

Under `DRAWMODE=1`, which both games run, `screen_transition_begin` (SYS4422 `0x439da0`, SYS4433 `0x43a6d0`)
treats the scratch slot by mode. A blank create or a fill sets the record's created flag (see
[sys4-format-notes.md](sys4-format-notes.md), "Surface-record bookkeeping").

| mode | op (SYS4433 handler) | scratch `0x20` |
|---|---|---|
| 0, 2 | `0x21` (`0x41aaa0`), `0x23` (`0x41acb0`) | created, filled black (0) or white (2) and drawn over the frame |
| 1, 3 | `0x22` (`0x41abb0`), `0x24` (`0x41adb0`) | the captured surface is drawn, then `0x20` is created and filled black (1) or white (3) |
| 4 | `0x25` (`0x41aeb0`) | untouched: source and target are composited directly |
| 5–7 | `0x2a` (`0x41b260`): mode `5 + style`, style 0–2 | created and filled black |
| 8 | `0x2e` (`0x41b8c0`): an asset mask | untouched in draw mode 1 (draw mode 0 builds the mask object) |
| 9 | `0x36` (`0x41c100`) | created blank; the two surfaces are copied to `0x24` and `0x25` |

The strip reveals `0x27`/`0x28` do not use this manager. Their begin and tick (SYS4433 `0x43cdd0` and
`0x43d600`) never write `0x20`.

Every handler takes `screen_transition_finalize` (SYS4422 `0x4399c0`, SYS4433 `0x43a320`) when fast-forward
(run-state `0x8000000`) is set at dispatch. Finalize publishes its surface operand directly and fills `0x20`
only when that operand is negative, which is the black or white endpoint:

| op | fast-forward off | fast-forward on |
|---|---|---|
| `0x21` | begin mode 0: `0x20` written | `finalize(surface, 0)`: written only for a negative surface |
| `0x22` | begin mode 1: written | `finalize(-1, 0)`: written |
| `0x24` | begin mode 3: written | `finalize(-1, 0xffffff)`: written |
| `0x25` | begin mode 4: not written | `finalize(target, 0)`: written only for a negative target |
| `0x27`/`0x28` | strip reveal: not written | `finalize(surface, 0)`, also when the division count is ≤ 0: written only for a negative surface |

The port follows these tables in `VirtualMachine.Presentation.cs` (`MarkLegacyTransitionScratchWritten`,
`MarkScratchIfFinalizeFills`). Ops `0x23`, `0x2a`, `0x2e` and `0x36` appear in neither corpus, so the port
still traces and skips them. Test: `SurfaceRecordTableTests.LegacyTransitionsMarkScratch0x20OnlyWhereNativeFillsIt`.
Live check: the port loaded slot 008, entered 木漏れ日の深緑地 (SC0500 and the SC2000 tutorial) and saved. The save
records `0x20` with created 0, as native's slot 009 does; before the fix the port recorded 1.

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
against those fields with strict `bound < glyph_edge` tests and returns horizontal/vertical overflow bits.
When a layout resets,
`adv_text_layout_reset_cursor_and_bounds_record@0x455070` replaces its retained state with a five-dword
record `{0, reset_x, reset_y, right, bottom}` copied from all four configured fields.

`adv_text_layout_reset@0x455210` then zeroes every pixel in surface `layout_slot+0x14` and erases the
complete configured glyph-object interval at layout `+0x68/+0x6c`. History recording is a separate tail:
unless suppression bit 31 is set, reset appends the logical boundary and arms the next History record's
group-start flag.

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
current-layout selection. The SYSTEM4 layout bootstrap (`AdvTextLayoutBootstrap`, now only a test fixture: Godot
direct-scene runs execute SYSTEM4 since 2026-11-05) also recognizes all nine configuration pairs and
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
The live action-6 state now also feeds the ADV fast-forward host channel independently of the
op-`0x19b`/`0x19c` lifecycle gate. Ghidra shows `adv_interpreter_tick` polling the physical action mask before
dispatch on every tick and testing bit `0x40` without consulting `adv_skip_service_enabled`; `0x19b` clears
persistent/read-driven active Skip but cannot release a physically held key. The transient physical channel
remains separate from persistent op-`0x88` Skip, so releasing the key cannot clear the user's toggle. Both
channels share text completion, wait advance, and voice deferral.

### Effect skipping and the input latch (2026-11-05)

**The latch.** A mapped click (`input_latch_mouse_click@0x485e20`, `ctx+0x13e0`; on release under `CLICKONUP`) or a
non-repeat mapped key (the key latch) stays latched until a service polls it.
`input_poll_triggered_action_mask@0x460880` takes the whole latch at once: keys, clicks, mapped joystick inputs, and
action 6 while Skip is on ("ADV hide window: the engine's hidden-window wait" lists the sources). Nothing ages a
trigger, so a click made while nothing polls is still there for the next poll.

**`system:EffectSkipOnClick` decides whether an effect polls.**

- With 1, `engine_main_tick_with_exception_policy@0x411840` polls while an effect service runs: the legacy run-state
  bits `0x8`, `0x10`, `0x80` and `0x100`, and `0x400` unless service-flags bit 0 is set. Action 4 forces the effect
  to its endpoint (`screen_transition_tick(0x10000000)`, or `gfx_request_force_complete_and_reset_anim_clock@0x4076c0`
  for `0x400`). The poll took every other trigger too, and they are lost.
- With 0, a new profile's default, nothing polls while the effect runs. The effect runs out, and the click waits in
  the latch.

**The services that take the latch.** Each acts on what it finds and drops the rest.

- `adv_text_reveal_service_tick@0x406fa0` polls on every tick, so a reveal's first tick takes what the latch holds.
  Action 4 finishes the reveal and sets `ctx+0x55108`. Until op `0x72` clears it, the page's later reveals publish
  at once without polling, and the latch keeps what it holds. Under fast-forward (or with action 6 held) the reveal
  does not poll.
- The hidden-window wait (`0x411ee6`): any trigger ends it.
- The modal movie service, run-state `0x2000` (`0x41205d`): action 4 stops the movie. It does not read
  `EffectSkipOnClick`.
- `adv_input_service_poll@0x411230` at a text wait. Op `0x72`'s entry normally discards the latch first (below), so
  the poll sees held triggers only at a wait that resumed without op `0x72`: the wait that op `0x7c` returns to after a
  Space hide. There the triggers count as fresh input: an op-`0x97` binding, then action 5 (hide again), then action
  4 (activate the hit, or advance the page).
- `adv_text_view_service_tick@0x410d30`, the `REDRAWTEXTONKEY` text view (run-state `0x100000`): actions 3 and 1
  (bits tested directly, the generic wheel defaults) step the view, and action 4 activates the hit or leaves the
  view. Both games set `REDRAWTEXTONKEY` 0, so it never runs.
- `timed_callback_sequence_tick@0x408170`, the op-`0xd5` sequence (run-state `0x40`), polls on every tick. Any
  trigger moves the frame to the abort pc (op `0xd5`'s operand, recorded at cursor 0) and clears `0x40`; the cursor
  stays. With no abort pc (-1) the trigger is dropped. 16 of Himegari's 17 sequences pass -1 (SHOWGROW passes
  `label_9db`), and 14 of Kamidori's 15 (one passes `0x87e`).
- Not modelled by the port, being unreachable: `input_hotspot_wait_poll` (run-state `0x800000`) and
  `adv_input_dispatch_callbacks` (`0x10000000`). Their ops (`0x91`, `0x92`, `0x95`, `0x96`) occur in neither game's
  scripts (both catalogs' opcode coverage, checked 2026-11-08), and the port stops on them as unsupported opcodes
  ("Modal hotspot waits").

Nothing else polls. A script sleep (op `0xc8`, run-state 1) only tests `interval_timer_poll_elapsed_steps`, and
`input_latch_mouse_click` tests no run state, so a click made during a sleep, while ops run, or during an effect with
`EffectSkipOnClick` 0 waits in the latch for the next service above.

**The points that discard it.**

- Unless run-state `0x8000000` (fast-forward) is set:
  - op `0x72`'s entry (`op_0x72_handler@0x41e690`, which also clears `ctx+0x55108`);
  - op `0xfa` (`op_0xfa_refresh_adv_skip_input_state@0x416de0`);
  - a hotspot callback's `ret`, which re-runs the waiting op `0x72`.
- Always:
  - op `0x101`;
  - `scene_context_init_reset` (op `0x9`, a root reload);
  - `engine_reload_current_script_and_reset_input@0x42e930`, the debug BIN reload (`IAGEService` `+0x34`,
    `iage_service_reload_current_script@0x45f550`);
  - `input_flush_action_triggers@0x405380`. Its callers are the legacy fades `0x21`–`0x25`, `0x2a`, `0x2e` and `0x36` as
    they start (not under fast-forward), op `0xd5` at cursor 0, `adv_input_service_poll` before a hotspot dispatch
    (`0x411294`), the text view's hit activation, and the skip-click cancel.
- Op `0x199` clears only the service trigger mask `ctx+0xa0ce8`, not the latch.

**Native-driven 2026-11-05** (slow reveals: `[message] MessageSpeed=100`):

| Click during | `EffectSkipOnClick` 0 | `EffectSkipOnClick` 1 |
|---|---|---|
| SC0000's page 13→14 fades | the fades run out; the click finishes page 14's reveal | the click skips the fade; page 14 reveals normally |
| the hide control's fade-out (handler A) | the window comes back after the fade | the window stays hidden |
| the fade-in after a Space hide (handler B) | the page advances (`0x8a9` → `0x8de`) | the page stays |

`tools/drive/scenarios/himegari-effect-skip-on-click.steps` checks the last two rows at both settings, switching the
setting in 設定's その他 page. The port matches every step. Its shots match native's apart from glyph edges (at most
50 levels) and the wait indicator's animation frame.

**Native-driven 2026-11-06: clicks during a sleep** (`MessageSpeed=100`). SC0000 page 17 (だが──) advances into a
1000 ms sleep at `0x1342`, a movie, about 12 s of effects, a 750 ms sleep at `0x16c8`, and 4.2 s more effects before
page 18 reveals its 35 glyphs in two runs around the furigana かがりび.

- A left click during either sleep finishes page 18's reveal at its first tick: both runs (`0x1a44`, `0x1a50`) and
  the furigana publish at once. Page 17 to page 18's wait took 22.97 s with the click and 26.5 s without.
- A right click during the sleep opens no MENU: no op-`0x97` binding polls, and the reveal's poll takes action 1 and
  drops it.

`tools/drive/scenarios/himegari-latch-sleep.steps` checks both. The port matched only after `RouteUnpolledTrigger`
(below); before, a sleep click reached the still-armed hotspots and bindings, so the right click opened MENU.

**Port.**

- `VirtualMachine.InputLatch` holds the triggers that arrive while nothing polls (`HoldTriggers`), and the consumers
  and discard points above take or drop them. The ADV coroutine yield holds its triggers in the same latch.
- Godot (`Main.Input`) routes a trigger that arrives during an effect wait to
  `GodotAdvHost.TryPollEffectTriggers`. That reads `EffectSkipOnClick` from `SYS4REG.INI` at each trigger, and
  polls when it is 1 and the effect is a screen transition or a `0x400` wait without service-flags bit 0. Otherwise
  the trigger goes to the latch.
- Only a polling text wait (`VirtualMachine.IsAdvTextWaitPolling`, native run-state `0x80000000`) dispatches hotspots
  and op-`0x97` bindings. Any other mapped click, key or joystick trigger goes through `Main.RouteUnpolledTrigger`: a
  running reveal's poll takes action 4 and drops the rest, and otherwise the trigger is held in the latch.
- A held action 4 reaches the host as `IAdvHost.FinishTextRevealsUntilWait` (kept until the next text wait) or
  `IMovieHost.CancelNextModalMovie`.
- Op `0xd5` polls the latch before and after each wait for the next entry (`TryAbortTimedCallbackSequence`; the
  literal entry intervals in both games are 33 ms at most), and flushes it at cursor 0.
- Tests: `InputLatchTests` and `TimedCallbackOpsTests` (synthesized).

### Service waits end on the engine clock (2026-11-05)

**Native.** Run-state `0x400`, the effect wait that op `0x21c` enters, is serviced at `0x4123d1` in
`engine_main_tick_with_exception_policy@0x411840`. Each tick does the following:

1. It samples `timeGetTime` into the retained-gfx frame time `+0xb550`.
2. It renders when the scene is dirty.
3. It calls `gfx_animation_service_poll@0x407640` on the manager at `ctx+0x46614`. The poll returns 1 while op
   `0x238`'s anim-clock window runs (start `+0xb564`, duration `+0xb568`; the force at `+0xb55c` ends it). After
   that it returns `+0xb560`, which records whether the last render found an effect still running (when that is 0
   and service-flags bit `0x10000` is set, it first clears the flags word):
   - the channel setters (color `0x47ea8a`, scale `0x47eb5f`, rotation `0x47eca0`, translation `0x47ed7f`, source
     rect `0x47ee4d`, the movie mask and the range transform) set it to 1;
   - `gfx_object_apply_transform_channels@0x473d80` and the surface-command composite set it during a render while
     something still runs;
   - `gfx_render_frame@0x4820b0` clears it first.
4. When the poll returns 0 and no movie surface holds the wait, it clears `0x400`. The movie flag is `[ebp-0x24]`, set
   at `0x411b4f` when `surface_movie_is_playing@0x405150` holds for any surface in `ctx+0x52bd4`. Otherwise it runs the
   `EffectSkipOnClick` check ("Effect skipping and the input latch"). Despite its name, `surface_movie_is_playing`
   does not ask DirectShow: it answers `((+0x424 ? +0x430 : +0x440) & 5) == 4`, and both words hold the movie op's
   `movie_flags` operand (`movie_play_configure@0x4625e0` stores `+0x430`, `movie_start_modal_playback@0x463280`
   stores `+0x440`). So only flags with bit 2 set and bit 0 clear hold the wait, and they hold it for the movie object's
   lifetime, past the last frame until its surface is released (see "Movie flags decide the effect wait", below).
5. Either way it calls `Sleep(0)`.

A tick takes about 0.17 ms here (5457 polls in an 890 ms wait), so the wait ends on the first tick after its last
effect ends.

**Measured (Frida, 2026-11-05).** The click that ends SC0000's page 13 runs `label_1235a` four times. Each run ends in
op `0x21c` at `0x123de`.

| Measurement | Native |
|---|---|
| The four waits, on frame time `+0xb550` | 150, 890, 150 and 300 ms |
| Gaps between the waits | 4, 0 and 8 ms |
| Click to page 14's show-text (`0xe04`) | 1505 ms |
| Show-text to end-text-line and op `0x72` | 156 ms more |

The reveal holds the script, as in the port: while run-state `0x20000000` is set the main tick runs
`adv_text_reveal_service_tick@0x406fa0` instead of dispatching an op, and the tick clears the bit only after
`adv_text_publish_next_glyph@0x451220` reports the last glyph. Page 14's 31 glyphs publish about 5 ms apart at the
default `MessageSpeed`, and end-text-line and op `0x72` follow the last one at once. The port's show-text runs 160 ms.

**Port.** Each wait used to lose about three frames, because of three behaviours:

- `FrameClock` moved only in `Main._Process`, one step per rendered frame.
- The waits woke on the frame signal.
- A one-shot channel was seeded only by the next frame's snapshot.

At 100 Hz the four waits took about 170, 910, 160 and 320 ms, and the reveal began 1620 ms after the click. Now:

- `FrameClock.StartRealTime` (the Godot host's clock) reads the high-resolution timer on every `NowMs`, scaled by
  `Speed` and monotonic. `Advance` does nothing on it. Tests keep the frame-stepped `new FrameClock()`.
- `WaitForForegroundTransition` (op `0x21c`) seeds the one-shot channels at its start
  (`GfxState.SeedOneShotChannels`), as native's first tick renders them. It then polls every millisecond.
  `HasActiveTimedPresentation` counts a seeded channel as finished once the clock passes its end; the next render
  commits it.
- These waits wake at their deadline (`FrameClock.RealMillisecondsUntil`) instead of at the next frame:
  - script sleeps;
  - the timed-callback deadline;
  - BGM fades (until 2026-11-07, when the port stopped waiting for them: "BGM fades run in the background");
  - legacy screen transitions.

The port's four waits now end 150, 890, 150 and 300 ms after they start.

**The end of the wait (2026-11-06).** Native's last tick renders the final sample before its poll ends the wait, and
that render commits every channel it samples past its end. The port used to wait for one synchronized compositor
presentation of that sample before the script went on: the next frame, then that frame's render.

Now `WaitForForegroundTransition` samples once on the VM thread when the wait ends (`GfxState.SnapshotVisibleObjects`).
That commits the same endpoints, or the forced ones. It then asks for a presentation without waiting for it, so the
next frame shows the endpoints or the next burst's result.

The VM can still wait for a render that has already started, because the presentation barrier keeps a burst atomic.
On the GPU backend a render took 0.03 ms (median; at most 1.2 ms) during these waits. `--timeline-log` switches to
the software rasterizer, which took 11–50 ms a frame here, so timeline runs still show waits of 1–17 ms at the end.

With the timeline log on, the time from the click to page 14's text wait fell from 1829 ms to 1704 ms; native takes
1661 ms. Most of the remaining 43 ms is two things:

- The software renders at the wait ends.
- Op `0x1f9` (set-texture) decoding an AGF for the first time at `0x12970` and `0x126e1`. These took 9.4 and
  10.5 ms in the driver's Debug build. Optimized, they take 4.4 and 4.0 ms, against native's 4.9 and 3.5 ms
  ("What a texture load costs").

`tools/drive/scenarios/himegari-effect-waits.steps` times the sequence through the driver, from the click step to
page 14's text wait. Native takes 1.81 s. The port took 1.78–1.81 s when it waited for the final presentation and
1.76–1.78 s now (four runs each; the driver rounds each step to 10 ms). Tests: `FrameClockTests`,
`GfxAnimationTests.SeedOneShotChannels_StampsArmedVisibleChannelsOnce`,
`GfxAnimationTests.TerminalSample_CommitsChannelEndpointsAtTheWaitEnd`.

#### Movie flags decide the effect wait (2026-11-07)

**Native.** `surface_movie_is_playing@0x405150` (SYS4433 `0x404ec0`) answers `(flags & 5) == 4` on the flags the movie op
stored, as step 4 above describes. Only flags with bit 2 set and bit 0 clear hold op `0x21c`'s wait, and they hold it
for the movie object's lifetime. At a movie's end the tick's surface-movie pass (`0x411b4f` onward) does one of two
things:

- With bit 0 set (`+0x43c`), it restarts the movie: the loop flag.
- Otherwise it detaches the movie (`movie_stop_and_detach_surface`). Unless `(flags & 3) == 2` it then clears the
  surface (`gfx_fill_surface_rect_worker@0x4790e0`), and it frees the object.

The shipped flags hold no wait through a movie itself:

- Kamidori's ADDEXP, SHOWGROW and BTL movie ops all pass 0, so the script continues while the level-up and battle
  movies play.
- Himegari SC0000's chapter movie passes 2. Its `0x21c` still waits for the movie because op `0x238` sets the anim
  clock to the latest layer stop time from op `0x23f` (`0x32b2`).

**Port.** `MovieSurfaceRegistry.HoldsServiceWait` applies the same predicate to the flags each binding keeps from its
movie op, until the surface is released. `GodotAdvHost.HasActiveMoviePresentation` reads it for `0x21c` and the
foreground-transition waits. Before this, any unfinished movie held the wait. Kamidori's first-battle SHOWGROW then
sat about 9.3 s after Engage, against native's 6.5 s; it now sits 6.3 s. SC0000's chapter wait is unchanged:
native 1.63 s, port 1.68 s. Test: `MovieSurfaceRegistryTests.OnlyFlagsWithBit2SetAndBit0ClearHoldTheServiceWait`.
*Correction (2026-11-07):* that 1.63 s wait is not the movie's. The wait that should cover the movie fell to 5 s on the
port once the movie stopped holding it, because op `0x23f` read the wrong surface. See "SC0000's chapter movie plays
on surface 5", below.

#### A surface release deletes its movie (2026-11-07)

**Native.** Ops `0x1f8` create-texture, `0x1f9` set-texture, `0x249` and `0x1fa` gfx-elem-release delete the slot's
movie object (`ctx+0x52bd4[slot]`) before they touch its texture. Op `0x23d` does the same for slots 42–999. None of
them asks whether the movie has reached its end.

Kamidori depends on this when a unit opens a chest or gathers. FIELD's `label_bbf8` (`0xbbf8`):

1. creates surface `0x2a` at 120×120 and plays the effect movie into it (local `0x542a`);
2. sets the anim clock to the movie's stop time (op `0x23f`) and waits it out with op `0x21c`;
3. erases the object and releases `0x2a` with op `0x1fa`.

MES.BIN then creates `0x2a` again at the text's size, draws the pickup text into it with draw-string, and shows
handle `0x13880+0x32` centered on the field.

**Port.** Since 2026-07-11 the port had kept a movie bound when a release came before its decoder reported the end
(`ReleaseIfCompleted`). At the chest the anim clock runs out first, so MES's text box showed the movie's last
frame, a black 120×120 square, instead of 「【消費】勇壮の水・小を２個手に入れた！」. `MovieSurfaceRegistry.Release` now
drops the binding whatever its state, and the timeline's `movie-stop` event records `completed`. Verified on
平穏なる採取地 (the chest at tile 2,3): both targets show the text. Test:
`MovieSurfaceRegistryTests.ReleasingASurfaceRemovesAMovieThatIsStillPlaying`.

#### SC0000's chapter movie plays on surface 5 (2026-11-07)

**Native.** SYSTEM4@`0x5bf` fills `adv_layer_surface_slots` (`G[0x3239]`, three slots per layer). Layer 1's first
slot is 5. SC0000's `label_1326e` copies it to `G[0x62452]`, so op `0x236` at `0x13c8` plays CHAPTER.AGF (`0x33`, PTS
0.39–12.37 s, stop time 12016 ms) into surface 5. A Frida probe on `op_0x236_play_movie_to_surface@0x423ee0` saw
`ctx+0x52bd4` go from empty to `[5]`. Op `0x23f` at `0x13f0` reads the same `G[0x62452]`. It stores the stop time in
`0x32b2[1]`, and `label_1235a` sets the anim clock to the largest such entry. The wait at `0x1574` therefore lasts the
whole movie. The release at `0x165f` comes after the movie's end.

**Port.** A 2026-07-11 special case moved this movie to surface 0. It came from a bounded bootstrap in which neither
SYSTEM4 nor SC0000's `label_125bd` had filled the table, so the operand read 0. The special case left `G[0x62452]` at 5.
Op `0x23f` therefore found no movie on 5 and returned -1, and the anim clock fell to the layer fades' 5000 ms.
Until the movie flags decided the effect wait (above), the unfinished movie held the wait and hid this. After that
change the port released the movie at `0x165f`, 5.9 s into its 12 s.

With the special case removed, the movie stays on 5. The port reaches the 750 ms sleep at `0x16cb` 13.5 s after the
page-17 advance and the next page at about 18 s. Native reaches the next page at 24 s, about 6 s of which LAV
Filters' codec probe spends at `0x13c8`. The movie frames line up once that probe time is subtracted. Test:
`MovieOpcodeTests.Sc0000ResumesImmediatelyAfterMovieOpcodeAtBytecodeOffset13d1` seeds the table as SYSTEM4 does,
then checks surface 5 and a 12016 ms anim clock.

#### Op 0x216 reads past the live surface table (2026-11-07)

**Native.** `op_0x216_query_surface_resource` (SYS4422 `0x42a0f0`, SYS4433 `0x42af80`) returns the word at gfx
manager `+0x700 + slot*0x14` without checking the slot. Kamidori's world map and save screen pass it -1:

1. ALLMAP@`0x4fa` and SAVE@`0xbce` ask op `0x215` for object `0xcb2a`'s source slot (ADV layer 1, `G[0xfd43c]`)
   into `G[0xfd437]`. With no such object that is -1.
2. The `jcc` that follows skips only on zero, so op `0x216` queries slot -1 into `G[0x320a]`.
3. A nonzero `G[0x320a]` would scan table `G[0xfec76]` for a matching background and draw its thumbnail into the save
   image (`0xc1`/`0xc3`).

Slot -1 reads manager `+0x6ec`. Neither image addresses that word (no `0x6ec` displacement, nor `ctx+0x46d00` or
`ctx+0x4af88`), and a Frida read of Kamidori native at ALLMAP found it 0, with `G[0xfd437]=-1` and `G[0x320a]=0`. Native
saves carry that 0. The page snapshot table follows the live one directly (manager `+0x5520`; SYS4433
`ctx+0x4af9c+0x4e20`), so slots 1000–1999 read snapshot records.

**Port.** `GfxState.QueryField` returned -1 for any slot outside 0–999, so every port save since the first world
map held `G[0x320a]=-1`. The port then ran the thumbnail scan, which native skips on 0. No entry matched -1, so
nothing drew. `QueryField` now returns 0 for slot -1 and snapshot ids for 1000–1999; other slots
still return -1. After both targets loaded the same save and made the same world-map round trip, SAVE07's payloads
differ only in native's pre-load surface-record history (below) and op `0xd0` clock samples. Test:
`GfxStateTests.SurfaceResourceQueryIndexesPastTheLiveTableAsNativeDoes`.

**Save comparison.** The decoded payloads (`NativeSaveContainerCodec.Decode`) are what to compare: the LZSS bytes
differ by encoder choice. Two known differences remain:

- A load does not copy the saved surface table back ([sys4-format-notes.md](sys4-format-notes.md),
  "Surface-record bookkeeping"). Color keys and created flags from before the load therefore reach the next
  op-`0x71` snapshot. A port restarted before loading lacks them.
- Op `0xd0` samples kept in globals are `timeGetTime()` on native and the frame clock on the port. Scripts use them
  only in pairs.

#### A movie surface shows its created texture until the first sample (2026-11-08)

**Symptom.** In Kamidori battles the port drew a white box where an effect movie was about to play, for as long as the
effect's start delay lasted. Native draws nothing there.

**Native.** BTL@`0x4311`–`0x436d` plays each effect movie into a fresh texture:

1. `create-texture (l 0xb) w h 0` creates a zeroed surface.
2. `play-movie-to-surface` (op `0x236`) binds the movie to that surface with the effect's start delay.
3. `gfx-draw-color` puts the object in mode `0x3b6e37`, an additive blend.

Op `0x236` requires the destination texture to exist, and `CMovieTextureRenderer` copies each sample into it. Until
the first sample, through the whole start delay, the object therefore shows the zeroed texture, which the additive
blend leaves invisible.

**Port.** `GodotAdvHost.StartMovie` dropped the slot's created image when it bound the movie. Before the first frame
the object had neither a texture nor a surface resource, and `Main.Compositor` drew it as a surfaceless fill in the
object's white tint. `MovieSurfaceRegistry.Begin` now takes the surface's contents at bind time, and
`TryResolveSurface` serves them until `PublishFrame` supplies a sample. A movie mask keeps its own path. Test:
`MovieSurfaceRegistryTests.SurfaceShowsItsCreatedContentsUntilTheFirstSample`. A 0.25 s capture of a 魔弾 attack
on 木漏れ日の深緑地 (slot 009, `rand-seed 13`) shows no white box.

**Battle timing.** The same battle, with the VM position sampled every 0.1 s on both targets, reaches ADDEXP at 4.67 s
on the port and 4.70 s on native, and REWARD at 5.20 s and 5.10 s. The roughly 1 s lead seen in earlier sweeps is gone.
Native is caught once inside op `0x236` itself, opening the effect movie; the port opens it at once (see "What the open
costs" under "Movie-to-surface opcode `0x236`").

#### Finished channels reset their targets; the transform mark `obj+0x68` (2026-11-08)

**Channel end.** When a finite matrix channel ends, `gfx_object_apply_transform_channels@0x472f00` copies its target
matrix into the current one, zeroes the channel's delay and duration, and resets the target matrix to identity. A
rotation channel also zeroes the target axis (`+0x1f8..0x200`) and angle (`+0x208`). Forced completion (owner
`+0xb55c == 1`, op `0x243`) ends channels the same way. The port copied the target into current but kept the target,
delay and duration. Saves taken after a motion therefore differed. Kamidori's FIELD minimap indicator `0xc382` moves
by `set-anim-transform-abs` (FIELD@`0xa55f`, 400 ms) and saved `+0x58 = 400` and a `(45, 15)` translation target on
the port, against 0 and identity on native. `SampleMatrixChannel`, `SampleRotationChannel` and
`CommitOneShotChannels` now reset as native does. Test:
`GfxAnimationTests.FinishedMatrixChannels_ResetTheirTargetsAndTimingAsNativeDoes`.

**Transform mark.** These setters write `obj+0x68 = 1`:

- the immediate setters `0x1fd`/`0x1fe`/`0x1ff`, which check no visibility;
- the channel setters `0x21e`/`0x21f`/`0x220`, inside their visible-bit gate;
- the six range-transform setters, as owner `+0x490` (the embedded record's `+0x68`).

`gfx_object_init_default` writes 0, and so does op `0x1fc` (`gfx_object_reset_transform_current@0x47e5b0`), which
also rebuilds the three current matrices as identity. No gfx path reads the mark, but saves carry it. The port wrote 0
for every object it created. `GfxObject.TransformMarked` now follows native, and `NativeGfxPersistenceCodec` reads
and writes it. Neither corpus calls op `0x1fc`, so the port still skips it.

**Parity.** Both targets loaded slot 009, fought the same battle and saved to 007. Every gfx record then matched
except the start stamps (`+0x20c..0x21c`), which hold absolute clock values. The payloads still differ in three ways:

- `G[0x4f2]` is the last slot saved, a shared-profile int that SAVE writes after each save. The sandboxes' histories
  differed.
- Two globals and one string are the save list's cache of the slot being overwritten. The two slot-007 saves held
  different contents.
- Op `0xd0` clock samples, as above.

### Fast-forward through effect waits, its pace, and physical Ctrl (2026-11-07)

Kamidori's prologue under held Ctrl showed four gaps against native. Each is closed below. With a 1.5 s Ctrl from
SC0000's first page, native and the port now both pass FIELD's setup and stop at `SC2000@0xaf5`.

**Effect waits under fast-forward.** The main tick (`engine_main_tick_with_exception_policy@0x411840`; SYS4433
`engine_main_tick_sys4433@0x411a40`) services run-state `0x400`, the op-`0x21c` effect wait, at `0x4123d1`. That is
ahead of the `0x40` and `0x08000000` (fast-forward) branch at `0x4124ba`. The fast-forward branch runs one op through
`adv_interpreter_tick` (SYS4422 `0x410fb0`, SYS4433 `0x4110f0`). It then calls
`gfx_request_force_complete_and_reset_anim_clock` (SYS4422 `0x4076c0` with the flags at manager `+0xb56c`, SYS4433
`0x407a30` with `+0xb588`) on every iteration, which only service-flags bit 1 suppresses. So under fast-forward:

- an op-`0x21c` wait finds its effects already forced to their endpoints. Its first poll finds nothing running, and
  the wait ends at once. With bit 1 set, the effects run at normal speed;
- enabling fast-forward during a `0x400` wait does not end that wait, because the force runs only in the branch the
  wait keeps the tick from reaching.

The port forces at the start of `WaitForForegroundTransition` while fast-forward is active
(`GfxState.ForceCompleteTimedPresentationForFastForward`, which yields to bit 1, leaves detached channels and movie
masks alone, and ends the anim-clock window). It no longer forces a wait already in progress when Skip starts
(`SetMessageSkipChannel`).

**The anim-clock window holds the effect wait.** `gfx_animation_service_poll@0x407640` (SYS4433 `0x4079b0`) is called
only from the `0x400` service. While op `0x238`'s window runs, it returns 1. The window's start is seeded at the first
poll and its duration is `+0xb568`. So `set-anim-clock N` followed by `0x21c` holds the script for N ms even with no
channel running. Kamidori's per-page effect helper `SC0000@label_1fdd1` relies on this: it sets the clock to the
longest fade, then waits with `0x21c` (under Skip it uses `0x243` and `0x20c` instead). The port's `GfxState.PollEffectService` matches.

**Service-flags bit 16 is one-shot.** After the window, the poll clears the whole flags word
(`gfx_animation_service_flags`, ctx `+0x51b80`) if bit `0x10000` is set and the last render found nothing running
(`+0xb560`). `retained_gfx_objects_clear@0x47cab0` (op `0x1f6` and `scene_context_init_reset`; SYS4433 `0x484330`) ends
the window, clears the force and dirty state, and clears the flags only under bit 16. The constructor
(`0x47fa90`, 0), op `0x24e`, the poll and that clear are the only writers. Kamidori's GAMESTART, MES, ALLMAP and
GAMECLEAR set `0x10003` or `0x10001`, so the setting lasts only until the first effect wait that finds nothing
running. Himegari uses 1/0 and 3/0 without bit 16. The port does the same in `PollEffectService` and
`ClearAnimationServiceForObjectClear`. Without the one-shot clear, GAMESTART's `0x10003` would stay set through the
prologue, and bit 1 would keep the fast-forward force off.

**Pace.** The pace below was measured with Frida on 2026-11-07, timing every op-`0x72` handler entry under held Ctrl.
It fits a cost per op plus a cost per text page:

| Run | Ops | Pages | Time | Per op | Per page |
|---|---|---|---|---|---|
| Kamidori SC0000 | 34,851 | 140 | 865 ms | 15.1 µs | 2.42 ms |
| Himegari opening pages | | | | 12.3 µs | 2.75 ms |

The 2026-11-03 figure of 374 µs per op ("Fast-forward pace" above) included stalls that are not op costs, such as
op `0x236`'s movie graph build at Himegari `SC0000@0x13c8`. `VmOptions.NativeFastForwardOpMicroseconds` is now 15
and `NativeFastForwardPageMicroseconds` is 2400. `VirtualMachine.FastForwardPace.cs` charges the page cost for each
text wait that fast-forward passes.

The port's synchronized presentation (`0x20c`, `RequestSynchronizedPresentation`) waits for the main thread's next
frame, which native's cheap render does not. Under fast-forward it now presents at most once per 50 ms and returns
at once otherwise, so the screen still updates and the op pace holds. Tests: `ForegroundTransitionTests` (the force,
the window, bit 16, the object clear) and `FastForwardPaceTests`.

**Physical fast-forward.** Logical action 6 (Ctrl, C and Backspace in both games) sets run-state `0x08000000` in two
places only:

- `adv_input_service_poll@0x411230`, at `0x411635`: a polling text wait whose triggered mask has `0x40`.
  `input_poll_triggered_action_mask@0x460880` returns the non-repeat `WM_KEYDOWN` latch, plus `0x40` while
  `ctx+0x13dc` (persistent Skip) is set. A key held since before the wait does not count, and op `0x72`'s entry
  discards anything latched earlier.
- `adv_text_reveal_service_tick@0x406fa0`: a reveal tick whose held mask has `0x40` while `ctx+0x6da74` is 0. The tick
  sets the bit and publishes every glyph. Only op `0x101` sets `ctx+0x6da74` (`0x4170df`). Only a reveal tick with
  action 6 up and no click clears it (`0x407101`). So after an op `0x101` (TITLE, FIELD and every control-strip
  callback run one), a held key starts nothing until it is released during a reveal.

The other setters are op `0x19c` (persistent Skip or read-skip), the ReadTextDB read-skip sets in ops `0x6e`, `0x71`
and `0x72`, and `engine_reload_current_script_and_reset_input@0x42ea3e`. The bit is cleared by:

- `adv_interpreter_tick` (`0x411039`, `0x41109f`), op `0x72` (`0x41e793`) and op `0xfa`, when action 6 is up and
  read-skip is off;
- op `0x19b`, op `0x19c` (which restores only persistent Skip and read-skip, or keeps the bit under the startup guard
  `ctx+0x6f86c`), op `0x101` (`0x4170ce`) and op `0x88(0)` (`0x41f170`), however the bit started;
- the click cancel, and `scene_context_init_reset` (`0x40b40e` zeroes the run state).

Held Ctrl therefore does not carry fast-forward across ADV setup. Native with Ctrl held 4 s from SC0000's first page
stops at `SC2000@0xaf5`:

- TITLE's op `0x101` sets the reveal suppression, and SC0000's first page clears it again;
- FIELD's op `0x101` sets it again during the fast-forward;
- SC2000's op `0x19b` at `0x85e` ends the fast-forward;
- the reveal of 「ここが最終試験の会場となる迷宮だ」 finds the suppression set, and its text wait sees no new key-down.

The port used to follow the held state, so it skipped on into AIM. `VirtualMachine.PhysicalFastForward.cs` now
implements both start paths, the suppression and every clear. Tests: `PhysicalFastForwardTests` and
`InputLatchTests.AFastForwardKeyHeldThroughHandlerBStartsFastForwardAtTheWaitSpaceParked`. A latched key-down that
op `0x7c` returns to the parked wait counts as a fresh trigger.

### Read skip and ctx+0x13dc (2026-11-07)

Native keeps persistent Skip in two fields, and fast-forward itself in a third:

- `ctx+0x550fc` (`message_skip_display_enabled`) is the toggle the menu bar and CONFIG show.
- `ctx+0x13dc` (`message_skip_enabled`) is the Skip a text wait acts on. While it is set, `adv_interpreter_tick` and
  `input_poll_triggered_action_mask` inject action 6 (`0x40`), so a polling text wait starts fast-forward at
  `0x411635`. `input_poll_action_mask@0x4608b0` (the held mask) injects nothing.
- Run-state `0x08000000` is fast-forward. `ctx+0x6dbd4` (`adv_read_skip_state`) records that the current message
  was read.

The ops and the menu treat them as follows (/v2 addresses):

| Writer | Effect |
|---|---|
| op `0x88` (`0x41f130`) | writes `0x13dc` and `0x550fc` alike. `0x88(1)` sets `message_skip_activation_guard` (`ctx+0x6da78`) and no run-state bit; `0x88(0)` clears the run-state bit |
| op `0x101` (`0x4170ce`) | clears the run-state bit however it started; keeps `0x13dc` and `0x550fc` |
| op `0x19b` (`0x416560`) | clears the run-state bit, `0x13dc`, the service flag `0x55100` and the click-cancel phase; keeps `0x550fc` |
| op `0x19c` (`0x4165a0`) | sets `0x55100`. With `0x550fc` set: `0x13dc = 1` and the run-state bit set. Else with the message read: the run-state bit set. Else, unless the guard `ctx+0x6f86c` is set: clears `0x13dc` and the run-state bit |
| op `0x72`'s entry, again when a hotspot callback's `ret` re-runs the waiting op | the read-skip block below, then the clear below |
| read-skip block of ops `0x6e` (`0x41e3b1`), `0x71` (`0x41e540`, `0x41e63a`), `0x72` (`0x41e73b`) and the menu's read-skip toggle (`0x406cd0`) | with `message:ReadTextSkip` off, read = 0. With it on and the message read: the run-state bit set, read = 1. Else read = 0 only while `0x550fc` is clear. It never clears the run-state bit and never starts it from the toggle |
| op `0x72` | after that block, clears the run-state bit when read is 0 and the held mask lacks `0x40` (`0x41e786`); its own wait then starts it again from `0x13dc` |
| op `0xfa` (`0x416de0`) | clears the run-state bit when the held mask lacks `0x40`, even on a read message. With it then clear, the op's input poll starts it again at once from `0x13dc` (opcode `0xfa` section) |
| op `0x1ca` (`0x41f880`) | writes `message:ReadTextSkip` only |
| menu Skip toggle (`adv_toggle_skip_mode@0x406c20`) | while `0x55100` is set, `0x13dc = 0x550fc` |
| menu Auto toggle (`adv_toggle_auto_mode@0x406b70`) | when it enables Auto with `0x55100` set, `0x13dc = 0x550fc` |

Op `0x88(1)` therefore starts nothing by itself. SC0000's strip Skip button (`0x25d`) runs it, sleeps its 100 ms
without fast-forward and returns into op `0x72`, whose wait starts fast-forward from the injected action 6. A strip
callback's op `0x101` ends a running fast-forward the same way, and the re-run op `0x72` restarts it from persistent
Skip or a read message.

The guard `ctx+0x6f86c` (`debug_reload_resume_active`) is unreachable in release images. Only the debug menu's BIN
reload (`engine_reload_current_script_and_reset_input`, Himegari `0x42e930`, Kamidori `0x42f350`) sets it, with a
resume line 10 before the current source line. That line comes from `script_source_line_table`, which the constructor
zeroes and nothing writes. So it is always -1, the resume line clamps to 0, and the reload clears the guard again.

So a page shown outside ADV, after op `0x19b`, runs without fast-forward even with the toggle on, and the toggle comes
back at the next op `0x19c`. Kamidori's SYSTEM4 runs op `0x19b` before FIELD, and FIELD resets layout 1 with op
`0x71` at `0x30a`. The port's read-skip refresh used to restart fast-forward from the toggle there. With fast-forward
running on the field, the next click hit Kamidori's `CancelMesSkipOnClick = 2` cancel: it turned Skip off and queued
CALLBACK_SETTING, so the port's SC2500 stopped on pages native skipped. With Skip on from the prologue, both targets
now reach SC2500's BUNKI choice.

**Port.** `VirtualMachine`: `_messageSkipEnabled` (`0x550fc`), `_messageSkipInjected` (`0x13dc`),
`_messageSkipServiceActive` (the run-state bit), `ApplyReadTextSkip` (the shared block) and `SetScriptFastForward`.
Ops `0x88`, `0x101`, `0x19b`, `0x19c`, `0x72`, `0xfa` and `0x1ca`, the menu toggles and the click cancel follow the
table (op `0xfa` since 2026-11-09; before then the port had it end fast-forward with no poll after it). `EnterTextWait` is op `0x72`'s entry, and `ServiceHotspotCallback` runs it again after a callback returns. The
hidden-window wait ends on `_messageSkipInjected`, the injected action 6, rather than on fast-forward. Until
2026-11-08, op `0x88(1)` started fast-forward at once, which skipped the Skip button's 100 ms sleep, and op `0x101`
left a script-started fast-forward running. No sweep had reported either. The guard is not modelled, being
unreachable. Tests:
- `HotspotInputTests.Op0x88EnablesSkipWithoutStartingFastForwardUntilATextWait`
- `HotspotInputTests.MessageSkipToggleSurvivesOp0x101_WhichEndsFastForwardUntilTheNextTextWait`
- `HotspotInputTests.Sc0000MessageSkipButton_EnablesPersistentServiceState` (the 100 ms sleep runs)
- `HotspotInputTests.RefreshAdvSkipInputState_EndsFastForwardAndPersistentSkipRestartsItAtTheNextTextWait`
- `HotspotInputTests.APageOutsideAdvLeavesPersistentSkipSuspendedUntilOp0x19c`

### Op 0x60 randomness (2026-11-07)

Op `0x60` (`op_0x60_handler@0x426970`) writes `rand() % bound`. `_rand@0x550fe4` is the MSVC LCG
(`seed = seed * 0x343fd + 0x269ec3`, per-thread data `+0x14`). `_srand@0x550fd7` seeds it from the tick count at
`0x413b7f` and `0x413c8d` (the context constructor) and `0x44e5d4`. The same generator is also drawn by:

- the main tick, at entry (`0x4118af`) and, while its registration check runs, at each lapse of that timer
  (`0x4119d1`);
- screen transitions (`FUN_00440530`), the save encoder and the catalog parse.

A native run's draws therefore depend on when it started and what ran before, and the port draws from
`System.Random.Shared`. Kamidori's enemy scripts (MVRTN, RTN_M0xx, STAGEROUND, STAGEREADY, SETEN, ADDEN) and battle
scripts (BTL, CALCDMG, COMMITBTL) use op `0x60`, as do FIELD, ROOM, TOWN, USEITEM and ALCHEMY. From the first enemy
phase on, native and the port place enemies differently, and the same click can lead
into a battle by a different path. In the 2026-11-07 sweep, native's slime attacked Wilfred, through FIELD's turn
processing (`FIELD@0x8cec`, "Taking Action..."). The port's slime did not, and the click on the 戦闘 room took FIELD's
direct attack (`0x3205`, cell flag `0x1` clear and `0x20100` set, then SELACT from `0x32ac`). So the 防御 row in
SELACT is live on native, where Wilfred defends, and grayed in the port, where he attacks.

Unseeded, compare battle presentation, not outcomes. The SELACT Engage button, the BTL HUD's centre stats and the
REWARD item panel matched native to within the translucency over the different backdrops.

#### Deterministic mode (2026-11-07)

The drive step `rand-seed SEED` (`tools-reference.md`, "Game driver") makes both targets draw op `0x60` from the same
streams; `rand-seed off` gives each its own generator back. Every call site has its own stream: the running frame's
script, packed id `P` (native: the frame's resource id), and the op's dword offset `O`. A stream starts at
`seed ^ (P * 0x9E3779B1 + O * 0x85EBCA77)`, mod 2^32, and steps as MSVC `rand()`:
`state = state * 0x343FD + 0x269EC3`, value `(state >> 16) & 0x7FFF`. The op then writes `value % bound`. The k-th
draw at a site is the same on both targets, whatever else ran. A site that one target draws more often, such as one
in a timed loop, changes only its own later values.

- Native: the agent (`tools/drive/native-agent.js`) puts a Frida probe on the instruction after the handler's
  `rand()` call, which returns to `0x4269af` in `/v2` and `0x42768f` in SYS4433 (`op_0x60_handler@0x427650`,
  `_rand@0x558368`), and replaces EAX. The handler calls `rand()` before it fetches an operand, so the frame's pc
  still holds the op's own offset. The driver profiles name the site (`[native.rand]`), and the agent checks the
  bytes there before it hooks. The hook must be a probe, a bare function. An `onEnter` object makes Frida take the
  site for a function entry and swap `[esp]`, here the handler's saved ESI, for its leave trampoline. AGE then
  faulted in the main tick reading `[esi+0xa51a0]`.
- Port: `VirtualMachine.DeterministicRandom.cs`. Like native, the port now draws before it reads the bound, so a
  zero bound spends a draw too.
- The step's result counts the draws per site since the previous `rand-seed`. Equal counts mean equal values.

Verified on Kamidori, from one FIELD save on both targets (turn 12, two slimes):

| Run | Draws per site, both targets | Outcome, both targets |
|---|---|---|
| seed 1, end the turn | FIELD@0x940a ×1000, MVRTN@0x24e ×5, RTN_M002@0x5e ×3, RTN_M005@0x24a ×6 | turn 13, the slimes stay |
| seed 2: click Wilfred, then the portal room (the turn ended); then Wilfred, the 戦闘 room and Engage | FIELD@0x940a ×2000, MVRTN@0x24e ×12, RTN_M002@0x5e ×9, RTN_M005@0x24a ×14 | turn 14, the slime moves into the 戦闘 room; then the same entry through `FIELD@0x8cec` into SELACT, and the slime at 0/12, Wilfred at 16/18, 24 EXP, 緑の液 ×1 |

The frames differ only in animation phases. Himegari's site is checked statically and by the agent's byte check;
no committed Himegari scenario reaches an op `0x60` site yet.

### Driving native AGE (2026-11-01)

These were found while driving native AGE from a script for gate 7 and for the game driver, and they are built into
`tools/drive_native.py` (`tools-reference.md`, "Game driver").

**Where native input comes from.**

- **Buttons and keys** are read with `GetAsyncKeyState`, which reports the global hardware state. The joystick goes
  through winmm `joyGetPosEx`/`joyGetDevCapsA` (`input_poll_joystick_action_bits@0x460380`). On Windows 10,
  `joyGetNumDevs` is the constant `push 0x10; pop eax; ret` (16 slots, not devices).
- **The cursor** comes from `input_poll_cursor_client@0x4602a0`: `GetCursorPos`, then `ScreenToClient`. Menus that
  poll it follow the real cursor. The op-`0x90` hotspot registry is the exception: it hit-tests the `WM_MOUSEMOVE`
  lParam, and `GetCursorPos` only on its first arm after a registration ("The hit and the hovered record"; corrected
  2026-11-05).
- **Cursor warps.** AGE also calls `SetCursorPos`: a confirmation such as セーブしますか？ glides the cursor onto its
  default button (はい) in about 20 steps, and the ADV control callbacks nudge it by a pixel (op `0x10a`). Windows
  answers a moved cursor with a `WM_MOUSEMOVE`, the only message that hit-tests hotspots ("The hit and the hovered
  record"), so the driver's replacement posts one for a new point inside the client area (2026-11-05).
- **The top edge.** A `WM_MOUSEMOVE` with y < 2 starts the system-menu dwell timer for the top-edge strip.
- **The polling gate** is input manager `+0x1820` (`ctx+0x1c28`, `input_polling_enabled`), written by
  `input_set_polling_enabled@0x460180` as `disable == 0`. While it is 0, every poller skips `GetAsyncKeyState` and
  `GetCursorPos`, so polled input (menus such as TITLE's `mouse_callback`) is ignored. The message latches below still
  fire, so movie skips still work.
  - On `WM_ACTIVATE`, `age_main_window_proc` passes `disable = display_windowed && wParam == WA_INACTIVE` (call at
    `0x4865ee`; `display_windowed` is `ctx+0x9a298`, nonzero = windowed). A **windowed** AGE therefore stops polling
    while its window is inactive and resumes on activation; fullscreen never disables it there. The gate also closes
    on Alt (the system-menu strip) and `WM_ENTERMENULOOP`, and reopens on `WM_EXITMENULOOP`.
  - *Corrected 2026-11-03:* the first reading took `+0x9a298` for fullscreen and concluded that a background AGE
    keeps polling. A driven AGE runs inactive at the bottom of the z-order, and the 2026-11-01 runs passed only
    because AGE happened to be active.
  - The driver's agent therefore turns that one call (return address `0x4865f3`) into an enable and writes 1 to the
    gate once the window exists (profile `[native.keep_polling]`). It installs then, not at spawn, because the packed
    AGE.EXE unpacks at run time.
- **The click latch.** `WM_LBUTTONDOWN`/`WM_RBUTTONDOWN` set a click latch (`ctx+0x13e0` bit 0/1, via
  `input_latch_mouse_click@0x485e20`), unless `+0x1c30` suppresses it. With `g_dwClickOnUp@0x5c6030` (SYS4INI
  `CLICKONUP`, loaded at `0x488080`) set, the latch is set on `WM_LBUTTONUP`/`WM_RBUTTONUP` instead; Himegari has
  `CLICKONUP=1`. Menus consume the polled state; everything else that reacts to a click consumes the latch.
  `adv_input_service_poll@0x411230` calls `input_poll_triggered_action_mask@0x460880` on the input manager
  (`ctx+0x408`), and its `input_poll_mouse_click_triggers@0x460320` turns each latched button into its logical action
  (slot + 4: left → 4, right → 7) at `input+0xfd8` (= `ctx+0x13e0`). Page advance, hotspot clicks, bound actions
  (right-click → `MENU.BIN`) and the movie and transition skips therefore fire on **release** in Himegari.
  Which service takes a latched click, and which flush it, is in "Effect skipping and the input latch".
  - This matters for CHMENU. It closes on a right press it saw followed by the release (local `0x3f5` latch over op
    `0x108`). A menu opened on the press would see the button still held and close as soon as it is let go.
  - The port used to trigger on press. It opened MENU, but every open after the first (once assets were cached)
    closed at once, and a press that skipped the opening movie went on to activate the title's ゲーム開始 on release.
    It now follows `Sys4StartupSettings.ClickOnUp` (`godot/Main.Input.cs`).
- **Keys.** A non-repeat `WM_KEYDOWN` also reaches `FUN_00485e50`, the key latch, which
  `input_poll_key_triggers@0x460220` consumes, so keys trigger on press. ESC posts `WM_CLOSE`. F1 calls
  `engine_save_screenshot_bmp@0x407ce0`, auto-repeat included, unless op `0x10f` set bit 0 of `ctx+0x6da7c`
  ("Native menu bar", Screenshots).

A driver can therefore play AGE without touching the desktop's input:
- replace those user32/winmm calls inside the process;
- post the button and key messages for the latches;
- capture the client area with `PrintWindow`.

**The foreground** (measured 2026-11-06). AGE never calls `SetForegroundWindow`. It creates its main window visible and
shows it, and its `WM_SYSCOMMAND` handler only adds to `SC_RESTORE`: it clears run-state `0x1000000` and resets the
window menu (`GetSystemMenu(hwnd, TRUE)`), then calls `DefWindowProcA` (`age_main_window_proc`, `0x486b83`). Still,
in every driven session it took the foreground at start, or with its first dialog once the start was kept in the
background. These Windows rules explain it; the driver's agent applies them (`tools-reference.md`, "What the native
hooks do inside AGE"):
- **The permission.** A process started by one that may set the foreground may set it too. It keeps that permission
  until it activates a window or the user clicks or types: a driven AGE kept it through 90 s of user idle time, and a
  zero-distance mouse move did not cancel it. While a process has it, any window it activates becomes the foreground
  window.
- **Reading it without stealing.** `LockSetForegroundWindow(LSFW_LOCK)` succeeds only for a process with the
  permission. A probe calls `LSFW_UNLOCK` right after.
- **The lock keeps activations in-thread.** While AGE holds `LSFW_LOCK`, its activations stay inside its thread: a
  dialog, or `SetActiveWindow` from a thread joined to AGE's input queue (`AttachThreadInput`). Such an activation
  also spends the permission. Afterwards `LSFW_LOCK` fails, AGE's window is active in its thread, and the foreground
  is unchanged.
- **A restore can take the foreground without the permission.** Windows makes a restored minimized window the
  foreground one when it is still the active window of its thread, even through `ShowWindow(SW_SHOWNOACTIVATE)` and
  with the window disabled. Minimizing a window that is active in a background thread leaves it active (focus 0);
  minimizing the foreground window leaves its thread with none. A restore from that inactive state does not take the
  foreground.
- **Deactivating.** `SetActiveWindow(NULL)` does not deactivate a window, called from a thread joined to AGE's queue.
  Activating another window of the same thread does, even a hidden one.

**Frame positions.** Each script frame's pc is a byte pointer into its code. In the innermost frame it is the resume
point: at a text wait it is already past the wait (SC0000 page 3 waits at `0x8a6` and reads `0x8a9`). A caller frame
keeps its pc **on** its `call-script` instruction (`SYSTEM4@0x477 > SC0000@0x3a2` with MENU open, not the following
`0x47a`/`0x3a5`), also right after a load. The port's control channel reports frames the same way
(`DriveFrame.FromVm`); a restored caller parked on the port's synthetic `0xae` reports its saved call site.

**The engine context pointer.** `g_pEngineCtx@0x5c6020` holds the live `EngineCtx*`. `age_main_window_proc` reads
every ctx field through it, and the host-callback thunks at `0x45f780`.. load `ECX` from it.

The VM position is a frame stack:
- `cur_ctx_index` (`+0x53d14`) selects the running record;
- each 0x78-byte record has `frame_codebase` (`+0x53d28`), `frame_pc` (`+0x53d2c`) and `frame_script_resource_id`
  (`+0x53d64`);
- the instruction offset is `(pc − codebase) >> 2`, the same formula `age_show_message_box@0x409370` uses for its
  FILE/ADDRESS diagnostics;
- `asset_catalog_get_entry_name@0x44ea40` names the resource id: a zero high byte indexes the SYS4INI table, and a
  nonzero one selects an append mount.

At a text wait the frame pc has already moved **past** the wait instruction. On native SC0000 page 3 the frame reads
`SC0000@0x8a9`; the port's page map has that page waiting at `0x8a6`, and `0x8a9` is where page 4 starts. Pages 1, 2, 3
and 5 all follow this rule (page 5 reads `0x913`; its port wait is `0x910`).

While the right-click menu is open the stack reads `SYSTEM4@0x477 > SC0000@0x3a2 > MENU@… > CHMENU@…`. Once the menu
closes, the SC0000 frame is back on the page offset.

**Per-user folders.** AGE loads `SHGetFolderPathA` from `Shell32.dll` at each use. Every caller:

| Caller | CSIDL | Used for |
|---|---|---|
| `save_root_resolve@0x40b880` | `0x801c` LocalAppData | the save root |
| `FUN_0040bd80` | `0x801c` | same shape as `save_root_resolve` |
| `engine_resolve_sys4reg_ini_path@0x46b150` | `0x801c` | `SYS4REG.INI` |
| `engine_save_screenshot_bmp@0x407ce0` | `0x8010` Desktop | F1 and 表示画面を保存 screenshots `CG%06d.BMP`, when `system:SaveBMPPath` is empty |
| `engine_write_debug_report@0x42d4d0` | `0x8010` Desktop | debug report (below) |

The debug report writes `REPORT\NN\` with surface BMPs and names `VARIABLES0INT.TXT`, `VARIABLES2STRING.TXT` and
`FILES.TXT`. It is reached only from the host-callback thunk at `0x45f7a0`. `WM_USER+1` (0x401) goes to
`wm_user1_hook_stub_true@0x42d4c0`, which returns 1 and does nothing. The variable dump is a lead for native global
observation (`global-memory-re.md`).

Redirecting `SHGetFolderPathA` therefore moves saves, settings, screenshots and reports together. A driven run that
quits rewrites the shared profile even without a save: `WM_CLOSE` calls `shared_profile_save`, which writes
`SYS4REG.INI`, `SAVE.DAT` and `RT.DAT`/`RT.BAK`. The attach test showed exactly those four files changed.

**A new profile starts fullscreen.** Without `SYS4REG.INI`, AGE writes its native defaults, including
`display:ScreenMode=0` (fullscreen; windowed is 1), `sound:Volume0..4=-1`, `MessageSpeed=5`, `MesWinAlpha=8` and
`EffectSkipOnClick=0`. Fullscreen takes over the display, and `PrintWindow` then captures black, so a sandboxed run
must write `ScreenMode=1` first.

**Message boxes.** `age_show_message_box@0x409370` formats the text and calls `MessageBoxA` through a packer import
thunk. Mode 5 is style `0x14` (`MB_YESNO | MB_ICONHAND`) with the caption エラーが発生しました. Returning `IDYES`
from a `MessageBoxA` replacement continues exactly as pressing the button does.

The agent's `MessageBoxA` replacement is C (a CModule), not a JS `NativeCallback`. Before 2026-11-04 it was JS, and
AGE died with `0xC000041D` (an exception in a user-mode callback) as soon as a shown box painted. The box's modal
loop ran inside the JS frame. Policy-answered boxes never entered that loop, so the bug stayed hidden until the
interpreter error box was left open. Input posts also skip a disabled window now, as hardware input does while a
modal box owns it.

**The "data is corrupted" box is a registration check.** `engine_main_tick_with_exception_policy`
(`0x4119ca`..`0x411a1d`) checks whether `set:IsReggist` exists and is 0. If so, every 60–180 s (60 s plus
`rand() % 120` s) it shows a mode-5 box. Its full text:

```text
データが壊れています．
再インストールしてください．



プログラムを続行しますか？

はい：このままプログラムを続行します．
      (ただし正常に動作しない場合があります)
いいえ：プログラムを終了します．
```

Yes continues; No throws the exit exception. Nothing is corrupted. The box appears when AGE runs from a
`native_scratch.py` copy, and while it is open it blocks the main loop, so no clicks reach the game until it is
answered.

**Kamidori (SYS4433, 2026-11-07; 4.33G values since 2026-11-08).** Kamidori's engine lays out its context
differently, so `engine-ctx.toml`'s offsets (Himegari's `/v2` image) do not apply. The profile
`tools/drive/kamidori.toml` names its own, for the clean install's 4.33G build ("Kamidori engine builds"):

- `g_pEngineCtx` is `0x5c9bc8`.
- The interpreter (`0x41121e`) reads `cur_ctx_index` at `ctx+0x57fa0` and the current op through `ctx+0x57fb8 +
  curCtx * 0x78`. Each 0x78-byte frame record keeps Himegari's internal layout: codebase `+0x14`, pc `+0x18` and
  resource id `+0x50`, so `0x57fb4`, `0x57fb8` and `0x57ff0` from the context.
- `input_set_polling_enabled` is `0x465a60` (input manager `+0x1820`, as in Himegari, so the gate is `ctx+0x1c28`).
  `age_main_window_proc`'s `WM_ACTIVATE` case calls it with `windowed && wParam == WA_INACTIVE` on the input manager
  at `ctx+0x408`, and the call returns to `0x48e041` (4.33F: windowed flag `ctx+0x9e740`, return `0x48e061`).
- Op `0x60`'s handler (`0x427650`) calls `_rand@0x558368`, which returns to `0x42768f` (4.33F: `0x4276f0`,
  `0x42772f`); the drive step `rand-seed` replaces the draw there.
- A copied install raises the same データが壊れています． registration box, answered Yes.

The driver uses those values in place of `engine-ctx.toml` ([tools-reference.md](tools-reference.md), "Game driver").

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
  `EngineCtx+0x55180+style_index*0x14`; an invalid index raises the standard script error. Scene reset
  clears only 200 bytes here, exactly ten records. Handler-admitted record 10 starts at `+0x55248` and
  aliases the return-stack table. All literal Himegari registrations use indices 0..9. The corpus has
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
the correct backend; it is not an immediate `GodotAdvHost.DrawTexture` raster operation. The port models
all eleven handler-addressable indices as safe independent `GfxState` records rather than reproducing
native index 10's adjacent-memory overwrite. Each `0x23b` call erases its full destination-handle capacity
and then uses the ordinary
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

The port originally treated a backbuffer `0x222` as a generic repaint request and reconstructed every
visible retained object. It therefore drew handles `60000+` at full size above handle `59999`, making the
real popup appear immediately while its opening animation ran behind it; closing had the symmetric error.
The first correction retained zero-based publication ranges and filtered reconstruction to the active
range, which removed the stable-popup overlay but still rebuilt each selected range from black rather than
preserving native D3D backbuffer pixels.

The native lifecycle was refined on 2026-07-30 while diagnosing two remaining endpoint flashes.
`gfx_present_object_range@0x482230` never clears the selected target: it begins a D3D scene through
`d3d_begin_scene_nested@0x4709d0`, draws the selected retained objects, ends the scene through
`d3d_end_scene_nested@0x470a10`, and, for target `-1`, immediately reaches
`d3d_present_backbuffer@0x4713b0` through its default wrapper. `IDirect3DDevice9::Present` is therefore
part of each backbuffer `0x222`, while black clearing remains the separate explicit opcode `0x20e`.
Backbuffer ranges are incremental draws over preserved native pixels rather than complete scenes rebuilt
from black.

Timed-callback pacing is not an implicit publication boundary in native AGE. Op `0xd5` enables run-state
bit `0x40`; that bit bypasses the outer loop's automatic `gfx_render_frame` path, and
`timed_callback_sequence_tick@0x408170` only sleeps and redirects the script PC. BUNKIMOVE's first visible
frame is consequently the first callback's scale mutation followed by explicit `0x222`. At the other
endpoint, close sets global `0x2` to one and BUNKIMOVE explicitly publishes `[0,59999)`, excluding capture
handle `59999`, before cleanup and BUNKI's final `0x20c`.

The port now gives op `0xd5` a dedicated non-presenting deadline wait which keeps the script presentation
barrier held, while backbuffer `0x222` suspends that barrier and synchronously snapshots the exact callback
state before returning. Incremental range publications preserve the preceding backbuffer: the software
backend skips its black clear and the GPU backend appends the new draw layers over its retained stage.
An explicit backbuffer `0x20e` disables preservation for the following publication. Native
`gfx_render_frame@0x4820b0`, used by ordinary `0x20c`, likewise has no implicit clear: it draws all visible
objects over the selected target and leaves clearing to `0x20e`. The software backend can apply that rule
literally to its pixel buffer. Godot GPU nodes require a separate retention policy: partial ranges append
over the retained stage, nonempty full publications replace/reuse the existing Sprite2D pool, and empty
full publications leave the preceding stage untouched. Treating every full publication as an append caused
unbounded draw-node accumulation and rapid slowdown. This split removes the pre-animation full-popup frame
and prevents BUNKI's post-close empty `0x20c`—issued after releasing both the capture and stable menu
objects—from reconstructing an empty selected set over black, without stacking complete scenes.

Surface text also remains modeled metadata rather than pixels. Selected-target publication now projects
that metadata into the destination surface, and the final Godot labels inherit the retained object's
projected scale and rotation. Consequently BUNKIMOVE's captured labels follow the same transform as its
panel instead of escaping as full-size Control overlays. Because those labels are live metadata overlays
rather than pixels in the preserved backbuffer, each incremental publication replaces the preceding label
projection from index zero. Retaining old labels would accumulate every intermediate transform as visible
text trails even though preserving the underlying software pixels or GPU draw layers is correct.

Focused timed-callback boundary coverage passes with the complete 532-test engine suite. The Godot build
is warning-free and the Himegari-targeted threaded frontend selftest passes. User testing confirmed the
premature opening frame and accumulated text trails are gone. The close-only black frame was traced to the
port's incorrect implicit clear on an empty full `0x20c`. The first preservation correction over-retained
GPU draw nodes; the bounded full/partial/empty publication policy passes the frontend selftest. User
retesting accepted both the restored performance and close behavior on 2026-07-30.

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
  GAMECLEAR uses 3/0. Bit 0 disables the run-state-`0x400` `EffectSkipOnClick` branch; bit 1 is independently
  consumed by opcode `0x243` and the shared worker to suppress a force-complete and animation-clock-reset
  request, including the per-iteration force under fast-forward. Bit `0x10000` makes the setting one-shot: the effect
  poll clears the word when it finds nothing running (Kamidori's `0x10003`/`0x10001`; "Service-flags bit 16 is
  one-shot").
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

### Scaled surface rectangle copy -- opcode `0x32` (2026-08-19)

Opcode `0x32` is the scaled sibling of `0x207`, with independent source and destination extents:
`(source_surface, destination_surface, source_x, source_y, source_width, source_height, destination_x,
destination_y, destination_width, destination_height)`. The SYS4422 handler
`op_0x32_scale_copy_surface_rect@0x41db20` calls `gfx_scale_copy_surface_rect@0x47c6c0`; SYS4433 uses the
matching pair at `0x41bbf0` and `0x483410`. Both workers validate the two surface slots, proportionally clip
the paired rectangles, mark the destination dirty, and select a direct copy when the extents match. When
both axes shrink they use an area-weighted sampler; when both grow they use the enlargement sampler; mixed
directions are handled in two passes through scratch surface `0x20`.

Kamidori has only two calls: `ALLMAP@0x4e2` copies surface 2 to 193 and `SAVE@0xbb6` copies surface 2 to 195.
Both are wholly in-bounds immediate-operand downscales from 1024x576 to 384x216. The portable host therefore
implements the observed path with an area-weighted RGBA sampler and leaves equal-sized requests on the
existing exact-copy route. This is a shared SYS4 graphics operation rather than a Kamidori compatibility
branch. Both revision handlers, workers, downsamplers, and enlargement helpers are renamed and commented in
their Ghidra programs; both programs were saved.

### Alternate-fill point-in-polygon -- opcode `0x147` (2026-08-19)

SYS4422 `op_0x147_point_in_polygon@0x429b10` and SYS4433's matching handler at `0x42a960` implement
`(out_inside, point_x, point_y, vertex_x_array, vertex_y_array, vertex_count)`. Each resolves the two
parallel signed-dword arrays, materializes Win32 `POINT` records, calls `CreatePolygonRgn` with fill mode
`ALTERNATE`, queries `PtInRegion`, normalizes the output to 0/1, and destroys the temporary region. There is
no game-state mutation or retained host object.

A direct GDI behavior probe establishes the raster boundary convention needed by the portable crossing
test: for a square from `(0,0)` through `(10,10)`, the top and left edges are inside while the bottom and
right edges are outside. The standard half-open even-odd crossing rule reproduces that behavior without a
Windows dependency. Kamidori's only calls are `ARRANGE@0x4c6` and `STUDIO@0x525`; both use four vertices
through local pointers. Both revision handlers are renamed/commented in their Ghidra programs and saved.

### ADV skip/input-state refresh -- opcode `0xfa` (2026-08-19; corrected 2026-11-09)

The zero-operand SYS4422 handler `op_0xfa_refresh_adv_skip_input_state@0x416de0` and SYS4433 equivalent
at `0x417580` end fast-forward and then wait for input. Step by step:

1. They set the frame's instruction length to 1 and clear `ctx+0x5510c` (4.33G `0x59398`). Op `0xae` sets that
   field after a load, scene reset clears it, and no instruction reads it at a 32-bit displacement.
2. They poll the held action mask, which carries no `ctx+0x13dc` injection. When logical action 6 (`0x40`) is
   absent they clear fast-forward (run-state `0x08000000`), even on a read message, and play/clear the latest voice
   queued during message skip.
3. If fast-forward is then off, they poll and discard the input latch and set run-state `0x80000000`.

Run-state `0x80000000` is the wait op `0x72` enters. `adv_interpreter_tick@0x410fb0` advances the pc by the length
the op set, and the engine then runs `adv_input_service_poll@0x411230` instead of the interpreter until the poll
clears the bit. So the script holds after op `0xfa` until one of these:

- a click;
- persistent Skip's injected action 6, which ends the poll at once and starts fast-forward again;
- Auto;
- a hotspot callback.

Unlike op `0x72`, op `0xfa` publishes no wait indicator and arms no Auto timer: Auto polls the shared timer
(`ctx+0x5f304`, also op `0xc8`'s) that an earlier page or sleep armed. That timer has normally expired, so an
unvoiced Auto ends the poll at its first tick. No page is shown either. The menu bar's
`adv_toggle_read_skip_and_refresh@0x406cd0` also reads the bit, as at a text wait.

Kamidori's only call is `SHOWALLMAP@0x16a`, the world-map reveal after a new area opens. The map stays on screen,
pc at `0x16b`, until the player clicks; the reveal's crossfade back and SC0010 then follow. This section said until
2026-11-09 that the op re-armed nothing and that the map had no special behavior. The Kamidori sweep found native
holding the map for more than 6.5 s while the port ran on to SC0010's next page (2026-11-09). With the port fixed,
the same route replayed from a save (k1clean slot 011, turn 25 of 木漏れ日の深緑地) holds both targets at `0x16b`
with identical shots until one click takes both through `SC0010@0xb424` to `SC0010@0xd08`.

**Port.** `VirtualMachine` runs op `0xfa`'s entry (`RefreshAdvSkipInputState`). With fast-forward off it enters the
same poll as op `0x72` (`WaitAtAdvInputPoll`) through `IInputHost.WaitForAdvInputPoll`, starting fast-forward first
when persistent Skip is injected. The Godot host runs the text wait's loop for it without a page break, locator page,
wait indicator or text clear. Its Auto timer is due at once unless a voice is pending (`AdvAutoAdvanceTimer`'s
`unvoicedDeadlineDue`). A hotspot callback's `ret` after op `0xfa`'s poll would land 3 dwords back, inside the
previous instruction. Only hotspots that SHOWALLMAP registered itself could reach that, and it registers none, so the
port runs op `0xfa`'s entry again instead. A headless run with `HaltAtWaitForInput` halts there as at op `0x72`. Tests:
`HotspotInputTests.RefreshAdvSkipInputState_*`, `PhysicalFastForwardTests.Op0xfaKeepsFastForwardAndPollsNothingWhileTheKeyIsHeld`,
`AdvAutoAdvanceTimerTests.AWaitWithNoTimerOfItsOwn_IsDueAtOnceUnlessAVoiceIsPending`. Both handlers are
renamed/commented in Ghidra (`/v2`, `/kamidori/SYS4433`, `/kamidori/4.33G`).

### Joystick physical-button capability -- opcode `0x106` (2026-08-20)

SYS4422 `op_0x106_get_joystick_button_count@0x428b30` and SYS4433's equivalent at `0x4299a0` directly
write the input manager's active-device physical button count (`EngineCtx+0x898`) to operand 1. Kamidori's
only call is `CONFIG@0x12`, storing the result in global `0xfd49b` for controller configuration. The
portable contract exposes this as an optional input-host capability; headless and hosts without device
enumeration return zero. Both handlers are renamed/commented in their saved Ghidra programs.

### Movement/attack flood-fill FIFO -- opcodes `0x132`-`0x134` (2026-07-21)

The DEBUGMAP symptom "selected unit can wait on its origin, but has no blue reachable tiles and cannot
move" is caused by the only three effectful gaps in `MVSEEK.BIN`, not by `CALCSCOPE` or FIELD input. AGE
admits queue ids `0..10`, but scene reset physically constructs ten context-owned FIFO pointers:

- `op_0x132_reset_int_queue@0x4217d0` validates `queue_id <= 10`, destroys any existing object in the
  selected slot, and allocates a fresh 0x1c-byte FIFO. `int_queue_construct@0x4074c0` allocates 0x100
  dwords, uses another 0x100 dwords as its growth quantum, and zeros the read/end/high-water indices.
  The ten-entry initialization loop covers `EngineCtx+0x55130..+0x55157`; admitted id 10 therefore
  aliases integer-stack slot 0 at `+0x55158`.
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

The port retains all eleven handler-addressable queue ids as safe independent VM-lifetime slots rather than
reproducing id 10's adjacent stack overwrite, and implements reset/enqueue/try-dequeue with native
signed-dword behavior. It diagnoses ids above 10 or never-reset slots; shipped scripts always reset queue 0
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

### VM operand accessors and per-opcode operand access (2026-10-06)

Handlers never read operand dwords directly. Each asks one of a small accessor family for "operand N"
of the current instruction. The family reads `frame_pc` (SYS4422 `ctx+0x53d2c`, SYS4433 `ctx+0x57fb8`)
and indexes the instruction's `(type, value)` pairs by its operand-index argument. Every accessor below is
named and plate-commented in `/v2`, `/kamidori/SYS4433` and `/kamidori/4.33G`; the SYS4433 column gives 4.33G addresses:

| Access kind | SYS4422 | SYS4433 | Index arg | Behavior |
|---|---|---|---|---|
| read | `vm_operand_fetch@0x41b940` | `0x419900` | 1 | int32 value of any operand form (see value semantics below) |
| read-float | — | `vm_operand_fetch_float@0x419c60` | 1 | float value; integer forms convert and strings use `atof` |
| read-string | `vm_operand_read_string_buffer@0x418860`, `vm_operand_read_std_string@0x41b480`, `vm_operand_format_as_string@0x41af90` | `0x419020`, `0x425640`, `0x419390` | 1 (`std_string`: 2, after the hidden return) | text; numbers become full-width CP932 digits |
| write | `vm_operand_write@0x425fb0` | `0x4266b0` | 1 | int32 store |
| write-string | `vm_operand_write_string@0x42b5b0` | `0x42c570` | 1 | string store (types 5/8/0xb/0xe) |
| write-float | — | `vm_operand_write_float@0x426c00` | 1 | float store, converted for integer destinations |
| bind | `vm_pointer_operand_bind_element@0x416180`, `vm_pointer_operand_write@0x416090` | `0x416920`, `0x416830` | 1 | rebinds a reference variable (types 6/8/0xc/0xe) |
| address | `vm_operand_resolve_address@0x425a50` | `0x426100` | 1 | backing-cell address; a reference yields its target |
| string-cell | `vm_operand_global_string_cell@0x415fe0` | `0x416780` | 1 | global string cell index of a global string or string reference; local strings throw |
| ref-slot | `vm_pointer_operand_slot@0x415eb0` | `0x416650` | 1 | address of a reference variable's own slot (types 6/0xc only) |
| cell | `vm_operand_lvalue@0x415f30` | `0x4166d0` | 1 | global-integer cell index (types 3/6/0xc) |

`tools/operand_access.py` derives every opcode's operand contract from this family without Ghidra. It reads
the dispatch table from the constructor's immediate stores. For SYS4422 this reproduces `build/games/himegari/op-handler-map.json`
exactly, 420 of 420 handlers; SYS4433 has 437. The tool then walks each handler and two callee levels, and records
every accessor call with its constant operand index. MSVC hoists argument pushes above conditional branches, so the
backward argument scan follows branch predecessors. SYS4433's accessors were mapped by aligning same-opcode
handlers with SYS4422; every alignment was unanimous (808/808 `fetch` calls, for example). Decompiling both images
then confirmed each classification; it corrected one early guess, `0x415fe0`, from a string storage address to a
global string cell index (the string twin of `vm_operand_lvalue`, used by `0x1a9`/`0x1aa`).

Results, recorded in `vm-map/opcodes.toml` as `access`/`access_by_abi`:

- **677 operand contracts for 234 observed opcodes.**
- **No literal store operands:** no immediate or inline string ever occupies a write, bind, address, or cell
  position in either corpus.
- **One operand without native access:** `0x1a7` (`comment`). Its handlers (SYS4422 `op_0x1a7_comment@0x416630`,
  SYS4433 `0x416dd0`) only set the instruction length. The operand is an editor comment compiled into the bytecode
  (2,086 Himegari and 3,672 Kamidori string-literal uses).
- **Value-dependent calls:** three fetches use a value-dependent index. In `0xc6` and `0x1ba`, operand 1 is compared
  with 2 and that value is then passed as the index. Unobserved `0x14c` reads operand 3 although the Kelebek argc is
  2, so its upstream framing is suspect.
- **One revision difference, value type only.** Every operand has the same direction (read, write, bind, address) in
  both revisions. SYS4433 reads and writes floats for 67 operands of the transform family (`0x1fd`–`0x1ff`,
  `0x217`–`0x21a`, `0x21e`–`0x220`, `0x227`–`0x22f`, `0x233`–`0x235`), where SYS4422 uses integers.
- **Missing code targets.** Three code-target operands were absent from the upstream code-target table:
  - value-switch case and default targets (`0xa2` operand 2, `0xa3` operand 2), which left 80+12 Himegari and 95+11
    Kamidori targets unlabeled and unrelocatable;
  - the timed-callback abort PC (`0xd5` operand 1). `timed_callback_sequence_tick` jumps to `codebase + abort_pc*4`,
    but every corpus site passes the `-1` sentinel.

  A scan of all read operands for values that always land on instruction starts found no other missing targets.

### Statement blocks -- opcodes `0x1f4` / `0x1f5` (2026-10-06)

`op_0x1f4_statement_block_begin@0x417390` opens an atomic statement block. The outermost open sets
`statement_block_active` (`ctx+0x5f2fc`). It also restarts the presentation-timer baseline:
`frame_timer_current` moves to `frame_timer_previous` and receives a fresh `timeGetTime`. A nested open only
increments `statement_block_depth` (`ctx+0x5f300`).

`op_0x1f5_statement_block_end@0x4173e0` decrements the depth first. The outermost close clears the flag and,
unless `script_launch_dispatch_active` is set, tail-calls `script_launch_queue_dispatch_next@0x40f6e0`. That function
itself returns immediately while a block is open.

While a block is open, `engine_main_tick_with_exception_policy@0x411840` skips five services. The first three
still run when run-state bit `0x400` is set (parked on an animation/surface transition):

- `sfx_tick_delayed_starts@0x4827e0` on the sound facade at `ctx+0x14024`, and
  `voice_tick_delayed_playback@0x4884d0` (check at `0x411a42`)
- pending per-surface movie starts (`movie_start_pending_after_sync_delay@0x4633b0`, over `ctx+0x52bd4[0..999]`;
  `0x411b05`)
- the tick's own `gfx_render_frame`, including the forced-redraw path at `ctx+0x51b6c` (`0x411cb3`)
- the run-state `0x1000` timer (`0x411e9d`). Op `0xd8` arms it, and on expiry it sets `0x800` and enqueues script
  `-ctx[0x5f718]` with `script_launch_queue_enqueue@0x40f820`. Op `0xd8` is unused in Himegari.
- the run-state `0x2000` modal service at `ctx+0x52bd0`, i.e. the scene movie of op `0x236` (`0x412027`)

The block does not touch sleep (`0xc8`), input waits or the explicit present. `gfx_op_0x20c_present_frame@0x4174a0`
skips only its frame-timer baseline update while a block is open, then renders as usual.

A block therefore makes one compiled command atomic. Nothing it draws is presented, and none of its audio, movie or
queued-script starts take effect, until the outermost block closes.

The corpus structure confirms that these are compiler-emitted brackets:

- **Balanced:** blocks are balanced in all 481 base scripts and nest at most two deep.
- **Never crossed by a jump:** no `jmp`, `jcc`, switch, coroutine or timer edge crosses a block boundary (0 of
  roughly 160,000 edges). Only `call` targets and callback registrations, which are code references rather than
  transfers, point across.
- **They wrap macro expansions.** Almost all blocks are parts of the ADV line template:
  - the 5-instruction voice-state reset
  - the 15-instruction speaker nameplate (`0x1d2`, nameplate draw, `0x7a`)
  - the 2-instruction `call` + `0x1bf`

Three neighbors inside that template are ordinary audio/presentation operations, not compiler markers. Their
historical labels were structural guesses:

- `op_0x1bf_arm_voice_duck_release@0x416c70` (formerly "call-end") advances the one-shot stage of
  `voice_bgm_duck_control_flags`. Unless bit 0 suppresses ducking, it arms `voice_bgm_duck_release_armed`
  (`ctx+0x6dbec`), so the tick restores ducked BGM once the voice ends. It mirrors the tick's own post-delayed-voice
  code.
- `op_0x1d5_mute_next_voice@0x416cb0` (formerly "cond-block") sets `voice_mute_next_flags` (`ctx+0x1493c`, voice
  facade `+0x434`).
  - The next `0xc4` promotes the flag to `0x10001`, and `voice_play_indexed_asset@0x4883a0` plays that voice on
    channel 12 at volume 0. The muted voice keeps its timing for Auto.
  - The following voice clears the flag.
  - The template emits it only when there is no speaker or the character's voice is disabled (`G[0x2e49]`).
- `op_0x21b_set_tick_presentation@0x4232d0` (formerly "line-id?") stores `operand != 0` in
  `tick_presentation_enabled` (`ctx+0x99294`). Under draw mode 1, the tick advances the presentation timer and may
  render only while it is set or run-state `0x2400` is active. Op `0x247` queries it.

`op_0x1bc_handler@0x416c20` (`reset-message-voice-state`), the template's voice-state reset, does more than clear
`adv_auto_voice_pending`. When the sound manager (`ctx+0x14910`) exists it first stops voice channel 12 through
`sound_manager_stop_channel@0x482fa0`. It then clears `voice_mute_next_flags` and the message-Skip queued voice
id/arg. The template runs it only while `G[0x2e47]` is nonzero, which is true at almost every Himegari line, so
advancing cuts a voice that is still playing.

**Blocks that span waits (resolved 2026-11-03).** Not every block is a template part. Some scripts keep one
block open across waits:

- **ADDEXP** wraps its whole body (`0x0..0x7da`), including a `MES.BIN` call and `sleep` / `present-frame` loops.
- **MES.BIN** draws its message, calls `present-frame`, then polls input in a `sleep 1` / `present-frame` loop.
- **SC0000's ADV control-strip buttons** (Auto `0x203`, Skip `0x25d`, read-skip `0x2ab`, `0x303`) redraw the strip
  and `sleep 0x64` inside a block, with no present.

These work because a block withholds only the tick's own render: sleeps and input waits still run, and explicit
`present-frame` still draws. So inside a block, AGE shows a frame only on `present-frame` or while parked at
run-state `0x400`. A strip button's new icon appears when the block closes, after its 100 ms sleep.

**Port status (2026-11-03).**

- **`0x1d5`.** `VirtualMachine` keeps `voice_mute_next_flags` with the native `0xc4` / `0x1bd` / `0x1bc`
  transitions and passes `muted` to `IAudioHost.PlayVoice(id, variant, muted)`. Godot plays a muted voice at
  -80 dB, so completion and Auto timing are unchanged. Live: with "その他" voices off, every `MAN999.OGG` line in
  SC0000 played muted, while the `LILA*.OGG` lines stayed audible.
- **`0x1bc`.** It now also stops the voice (`IAudioHost.StopVoice`) and drops a Skip-queued voice.
- **`0x1bf`.** It reaches `IAudioHost.ArmVoiceBgmDuckRelease`, which applies the `0x1cf` one-shot stage. Godot now
  restores ducked BGM only once the release is armed and the voice is idle, with no `0xc2` fade and bit 0 clear.
  It no longer restores at voice completion alone. A delayed voice start arms it too.
- **Duck release fade (2026-11-03).** The release at `engine_main_tick` `0x411e12` swaps run-state `0x4000`
  for `0x200`. It arms the 10 ms period timer at `ctx+0x5f400` and calls `bgm_fade_arm(10000, 1)`. Each tick
  then feeds `period_timer_poll_elapsed_periods@0x44d0f0` to `bgm_fade_tick@0x464960`, which adds one point per
  elapsed period up to 100 and interpolates the level linearly in basis points (the backend then applies the
  native volume curve; "linear in amplitude" here was corrected 2026-11-04). The result is a one-second fade from
  the ducked level to full volume (target 10000, not the level saved at `ctx+0x6dbe8`). The duck
  itself stays instant: `voice_bgm_duck_begin` arms step 10 and ticks 100 periods at once. While the release
  fade holds `0x200`, a new voice does not duck. The port matches: `Main.FadeBackFromVoiceBgmDuck` owns the
  BGM envelope as an explicit fade does, in 100 basis-point-level steps over one second.
- **Statement blocks.** `VirtualMachine` tracks the flag and depth and reports the outermost open and close to
  `IAudioHost.SetStatementBlockActive`. Godot withholds delayed voice service (`0x2c0`) and delayed SFX starts
  (`0x2bf`) until the close.
  - **Render withholding.** `ConsumePresentationReasons` reports nothing while a block is open, unless an explicit
    present (`HostRequest`) is pending or a transition is active (`IsTransitionWaiting` / screen transition, the
    `0x400` equivalent). Retained changes stay pending for the next present.
  - **Live check.** The timeline event `statement-block-presentation` brackets each withheld stretch. Each Auto
    click on SC0000 withholds about 90 ms at `0x21b`. The save/load scenario passes.
  - **Sleeps (2026-11-08).** `sleep_op_0xc8@0x420ec0` never sets run-state `0x400`: under 10 ms it calls kernel32
    `Sleep` inline, and from 10 ms it sets run-state bit 1 and arms the timer. So native renders nothing while a
    sleep inside a block runs. `GodotAdvHost.Sleep` used to request a presentation on every sleep, which the render
    withholding lets through, so a strip button's new icon showed during its 100 ms sleep instead of at the close.
    It now requests none while a block is open (`GodotAdvHost.SleepPresentationFor`, checked by the Godot
    self-test). A scan of both games' disassembly (2026-11-08) finds sleeps lexically inside a block only in
    Himegari: the four strip buttons (`sleep 0x64`, no present) in 301 scenes, and one `sleep 0x10` in ADDEXP that a
    `present-frame` follows. Kamidori has none. Blocks that span a call (ADDEXP into MES.BIN) are tracked at run
    time, so their sleeps follow the same rule.
  - **Movie starts (2026-11-08).** The same scan finds a movie start inside a block only in `DEBUG.BIN` (`0x139`,
    `0x1ab`, both `play-movie-mask-transition`), and Kamidori has none. Each is followed directly by op `0x21c`,
    which parks the interpreter at run-state `0x400`, so native's tick starts the movie and renders as it would
    outside a block. The port does not withhold movie starts and lets presentation through while it parks, so it
    matches. Op `0x236`, whose `0x2000` modal service a block would withhold, is in no block in either game.
  - **Not modelled.** The unused `0xd8` timer. Script launches run synchronously in the port.
- **`0x21b`.** The value is stored, and op `0x247` reads it back. The port has no draw-mode-1 tick render gate.

### VM value, array and call semantics relevant to compilation (2026-10-06)

These facts define the core of a language that compiles to AGE bytecode (roadmap §3.3, "Decided language
architecture"). They come from the `/v2` decompilation; the SYS4433 float accessors above are the only observed
departure.

**Values.**

- **Generic reads.** `vm_operand_fetch` always yields an int32. Float forms (types 1/4/0xa) truncate toward zero
  (`__ftol2_sse`). Inline and variable strings (types 2/5/0xb) parse with `atol`. String references (8, 0xe), float
  references (0xd) and type 7 throw.
- **Generic writes.** `vm_operand_write` stores int32 into integer cells and integer references, and `(float)value`
  into float cells. String destinations throw.
- **Strings.** Numbers read as strings become full-width CP932 (`ascii_to_fullwidth_cp932_inplace`). Integers
  format with `_itoa_s` base 10 and floats with `%lf`; then digits and letters widen, `-` becomes `－` and anything
  else `？`, so a float's `.` too. Op `0x1b2`'s diagnostic accessor and op `0x1c8` keep ASCII. The value-switch
  keys (`0xa2`/`0xa3`) read the same way. The port matches since 2026-11-07 (`VirtualMachine.ReadStr`); before, it
  kept ASCII, so Kamidori's gathering message read 「素材5個」 where native shows 「素材５個」. The only string
  operations are `set-string`, `concat` and string (in)equality (`0x192`–`0x195`).
- **SYS5-style vector types.** The types `0x8003`/`0x8009`/`0x8005`/`0x800b` are handled by the accessors but
  never occur in either corpus.

**Operators.**

- **Arithmetic:** `add`, `sub` and `mul` wrap at 32 bits. `div` and `mod` truncate toward zero with no zero-divisor
  guard.
- **Bitwise and shifts:** `and` and `or` are bitwise. `sar` and `shl` shift by `n & 31`.
- **Comparisons:** `eq`, `ne`, `lt`, `lte`, `gr` and `gre` are signed and produce 0 or 1.
- **No other operators.** There is no xor, not, negate or short-circuit operator; compiled code synthesizes them.
- **Negative constants are never encoded as immediates.** The original compiler emits `sub tmp 0 n`: 19,899
  Himegari and 36,623 Kamidori sites, mostly `-1` and `-1000`. The only negative immediates are `0xd5`'s `-1`
  abort sentinel.

**References and arrays.**

- `lookup-array` (`0x61`) binds `dst_ref` to `base[index]`.
- `lookup-array-negative` (`0x62`, installed but unused) binds `dst_ref` to `base[-index]`.
- `lookup-array-2d` (`0x12c`) binds `dst_ref` to `base[row*stride + column]`, with operands
  `(dst_ref)(base)(row)(stride)(column)`.
- Elements are 4-byte cells, or 0x1c-byte string records. No bounds are checked.
- `take-address` (`0x63`) aliases a reference to a variable's storage, or to another reference's target.

**Control and calls.**

- `jcc` (`0xa0`) transfers to operand 2 when the condition is non-zero and to operand 3 when it is zero; `-1`
  falls through.
- `call` (`0x8f`) pushes the return offset on the frame's 256-entry local return stack and jumps. `call -1` is a
  no-op.
- `ret` (`0x5`) pops that stack and resumes there unless the entry is `-1`. It also restores a pending run-state
  snapshot from `ctx+0x5f2f4`/`0x5f2f8`.
- **Callback bodies are local subroutines.** Every native callback dispatcher calls `vm_return_stack_push_pc@0x405330`
  before redirecting `frame_pc`: the timed scheduler, `adv_input_dispatch_callbacks`, `adv_interpreter_tick`,
  `adv_input_service_poll`, and two helpers. The callback's `ret` therefore resumes the waiting opcode. The corpus
  agrees: every hotspot, mouse, joystick and timed callback target reaches `ret`, as do 72,792 of 72,794 local-call
  targets.
- **Coroutine handlers are the exception.** Handlers registered by `0x7b` instead end at `coroutine-resume` (`0x7c`)
  in all 1,924 Himegari registrations.
- **Callbacks belong to the script that registered them (traced 2026-11-03).** Native keeps one hotspot table
  (`ctx+0x5248`, at most 100 records) rather than one per frame. Each register (`input_hotspot_register_rect_callbacks`
  for op `0x90`, `input_hotspot_register_named_callback@0x403e10`) records the registering frame's packed script id at
  table `+0x7494`, i.e. `EngineCtx::hotspot_owner_script_id` (`ctx+0xc6dc`). While op `0x94` keeps
  `hotspot_wait_armed` (`ctx+0xc6e4`) set, *every* ADV wait polls that table, including a wait in a script the scene
  called. Before redirecting to any hotspot callback (op-`0x97` bound action, hover, click; from
  `adv_input_service_poll`, `adv_input_dispatch_callbacks`, `adv_interpreter_tick` and two helpers),
  `hotspot_check_owner_script@0x4079b0` compares the owner with the running frame's `frame_script_resource_id`. On
  a mismatch it throws flags `0x10007` with `Depth が不正です <running> != <owner>` (`0x570424`). The same guard,
  with the same message, protects the op-`0xcc` mouse callback (`mouse_callback_owner_script_id`, `ctx+0x5f6ac`,
  checked by op `0xcd`), coroutine resume (`coroutine_yield_owner_script_id`, `ctx+0x5f6bc`, checked by op `0x7c`),
  op `0xcf` and `timed_callback_sequence_tick`.
  - The main loop's catch (`engine_main_tick_with_exception_policy`) shows the error through
    `age_show_message_box` mode 7: caption `エラーが発生しました`, body = message + the `デバック情報` coordinate +
    the "プログラムを続行しますか？" abort/retry/ignore explanation (`0x570558`), MessageBoxA style `0x112`
    (`MB_ABORTRETRYIGNORE | MB_ICONHAND | MB_DEFBUTTON2`).
  - **The answers (driven 2026-11-04, `tools/drive/scenarios/himegari-script-error*.steps`).** The packer thunk
    in place of `MessageBoxA` translates the button before the policy at `0x412689` sees it. EAX was measured at
    `0x412679`:

    | Button | `MessageBoxA` returns | Policy sees | Native effect |
    |---|---|---|---|
    | 再試行 | `IDRETRY` 4 | 2 | Logs `0x570f34` and restarts the loop with the pc unchanged. Nothing is re-executed, so the error comes back only if its cause does: a held button re-raises it on every tick. |
    | 無視 | `IDIGNORE` 5 | 4 | Logs `0x570d24` and adds 4 × the frame's *last decoded* instruction length (`ctx+0x53d88` per frame) to `frame_pc`, then restarts. |
    | 中止 (and any other code) | `IDABORT` 3 | 1 | Continuation `0x412961`, the forced `WM_CLOSE`: exit code 0, and `SYS4REG.INI` and the saves are written. |

    For 無視 after an input-poll error, the last decoded instruction is the wait. A waiting frame's pc is already
    past the wait, so its successor is skipped by the wait's length without running, and the frame keeps waiting.
    `example.depth-error`'s DETOUR shows this: it waits at `0x2f` (3 dwords), and 無視 moves the pc from `0x32`
    (`end-text-line`) to `0x35` (`exit`).

    Repeated 無視 under a held button kept adding 3 dwords even where the instruction at the pc was 1 dword long,
    so the step is the last decoded length, not the current instruction's. Its pc then left the script, and a later
    Depth error surfaced as a plain mode-1 box (style `0x10`, no debug block) that closed the game.
  - Raised with `oms/examples/mods/example.depth-error` (SC0000 `run`s a mod script before page 3; a right click at
    its text wait): "Depth が不正です $2$DETOUR.BIN != SC0000.BIN … FILE=$2$DETOUR.BIN ADDRESS=32 LINE=-1
    COMMAND=-(111) DEPTH=2". A Frida hook on the six throw sites confirmed the guard is `hotspot_check_owner_script`
    called from `adv_input_service_poll` (the bound right-click action). `ADDRESS`/`COMMAND` are the instruction
    after the wait, because a waiting frame's pc is already past it.

    The gate-4 record that "the game closes" was the driver's own crash, not 中止 (see "Driving native AGE",
    message boxes).
  - **The other guards (static, 2026-11-04).** Their state lives in `EngineCtx`, not in the frame. A registration
    from any frame therefore replaces the earlier one, and a frame other than the registrant can reach the dispatch.
    - **Mouse callback.**
      - Op `0xcc` (`op_0xcc_register_mouse_callback@0x420f70`) stores the target (`+0x5f684`) and the running
        script (`mouse_callback_owner_script_id`, `+0x5f6ac`). It then starts the interval timer at `+0x5f320` with
        operand 1.
      - Op `0xcd` (`@0x417e10`, length 1) does nothing until the interval has passed. Fast-forward (run-state
        `0x8000000`) makes it due at once.
      - Once it is due, op `0xcd` restarts the timer and pushes the return pc; a target of -1 pops it again.
      - If the running script is not the owner, op `0xcd` throws Depth with the return pc still pushed. Otherwise it
        jumps to the target in the running frame.
      - After 再試行 the dispatch is not due again until the interval passes. Either answer leaves the stale return
        pc for the successor's `ret`.
    - **Coroutine yield.**
      - Op `0x7b`'s handlers are per depth (`+0x6da88`/`+0x6db28`). The yield itself is global: `coroutine_runstate`
        (`+0x6dbc8`), `coroutine_resume_off` (`+0x6dbcc`) and `coroutine_yield_owner_script_id` (`+0x5f6bc`).
      - Op `0x199` (`@0x416440`) clears the service-action trigger mask.
      - When no yield is entered (bit `0x4000000` clear) and handler A is set, op `0x199` saves
        `run_state | 0x6000000` and clears the run state. It saves pc+1 and the running script as owner, then enters
        handler A.
      - Otherwise, when handler B is set, op `0x199` clears `0x4000000`, sets `0x2000000` and enters handler B.
      - Op `0x7c` (`op_0x7c_handler@0x417cb0`) throws 「不正な位置でEndHWLが呼び出されました．」 (`0x571498`,
        flags `0x10005`) when `0x2000000` is clear. Native calls the op EndHWL.
      - Op `0x7c` throws Depth when the owner is not the running script, before it changes anything. It records no
        instruction length first, so 無視 skips by the previous handler's length.
      - Otherwise op `0x7c` restores the pc and the run state (`coroutine_runstate & ~0x2000000`), and clears the
        yield. The restored run state keeps op `0x199`'s bit `0x4000000`: unless handler B ran in between, the frame
        then sits in the engine's hidden-window wait at the resume pc.
      - The engine arms the same yield itself:
        - `adv_input_dispatch_callbacks` (`0x4085a5`) and `adv_input_service_poll` (`0x411338`) enter handler A on
          trigger bit `0x20` (logical action 5);
        - `engine_main_tick_with_exception_policy` (`0x411ef5`) ends the `0x4000000` wait on any service trigger and
          enters handler B.

        Each of the three sets the owner to the running frame.
      - Retail hide window uses that wait (native-driven 2026-11-04). On SC0000 page 3, handler A skips HIDEWIN.BIN
        and resumes at `SC0000@0x30d` with the text box hidden, and a left click enters handler B. The port models
        the wait and the text-wait entry ("ADV hide window: the engine's hidden-window wait").
    - **Timed sequence.**
      - Ops `0xd3`/`0xd4` build one engine-wide schedule (entries `+0x5f694`).
      - Op `0xd5` (`@0x421090`, length 0) starts the schedule when the cursor is 0. It records the abort pc and the
        running script in `timed_callback_script_resource_id`/`_guard` (`+0x5f6b4`/`+0x5f6b8`).
      - While the cursor is below the last index, op `0xd5` sets run-state `0x40`.
      - `timed_callback_sequence_tick@0x408170` then waits for the entry's deadline and picks the on-time or catch-up
        target. It throws Depth when a target is set and the running script is not the guard.
      - A frame reaches this guard when it runs op `0xd5` while another script's sequence is under way. The pc is
        still on op `0xd5`, with length 0, so 再試行 and 無視 both raise the error again until 中止.
      - A nested op `0xd3` instead resets the shared schedule, so the outer sequence ends at its next op `0xd5`.
    - **Op `0xcf`.**
      - `op_0xce_register_timed_catchup_callback@0x420fd0` registers an on-time target (`+0x5f688`), a catch-up
        target (`+0x5f68c`) and owner `+0x5f6b0`, and starts its interval timer (`+0x5f33c`).
      - `op_0xcf_dispatch_timed_catchup_callback@0x417f80` dispatches it with the same check. Its on-time path
        formats the raw ids instead: 「Depth が不正です %d != %d」 (`0x5714c0`).
      - Neither game uses ops `0xce`/`0xcf`.
    - **Resets.**
      - Construction and op `0x9`'s root reload call `scene_context_init_reset@0x40b3b0`. It sets every owner to -1
        and clears `coroutine_runstate` and the timed schedule, but keeps the `0xcc` target.
      - No other code writes the mouse or timed fields, so they survive a save load.
  - **Port (fixed 2026-11-03; answers 2026-11-04):**
    - **Owner check.** The port's hotspot registry is the owning frame (`_interactiveFrame`). It used to dequeue the
      owner's callback offset and look it up in the *running* script, so the click was silently dropped, or, had the
      offset existed there, ran unrelated code. `VirtualMachine.ServiceHotspotCallback` now raises the native error
      when the owner is not the running frame. The text matches native's byte for byte.
    - **The box.** The error goes through `IDiagnosticHost.ShowScriptError`, which returns a `ScriptErrorChoice`.
      Godot shows `ScriptErrorDialog`: native's caption and text, 中止(&A)/再試行(&R)/無視(&I), 再試行 as the default
      button, no cancel and a disabled ✕. It has the native box's MessageBox layout, over a frozen frame (see "Port
      appearance" under the close policy).
    - **中止** ends the run (`HaltReason = "script-error: …"`, `VirtualMachine.ScriptError`), and the frontend quits
      like the forced close.
    - **再試行** keeps waiting, with the owner's bindings re-armed.
    - **無視** adds the wait's length to `ExecFrame.IgnoredDwords`. The frame resumes that many dwords past the
      wait's successor, and the drive position shows the skip while the frame still waits.
    - Both targets pass the same scenarios.
    - **The other guards (2026-11-04).**
      - The mouse callback, the coroutine yield and the timed sequence are VM state, kept in
        `VirtualMachine.Input`, `.ControlFlow` and `.Timing`. Only op `0x7b`'s handlers stay on `ExecFrame`.
      - Each owner is the registrant's `Script`. Owners are compared by packed id, as native compares resource ids,
        so two activations of one script are the same owner.
      - Each guard raises the native error through `RaiseInstructionScriptError`, with the dispatching instruction
        as the coordinate. 再試行 runs that instruction again. 無視 skips it, except at op `0xd5`, where it runs again.
      - A root reload clears all three, and the `0xcc` target with them.
      - A save restore clears the yield, and restored scenes take op `0x7c`'s cold path.
      - The port models two of the engine-armed yields (2026-11-05): the text wait's action-5 entry and the
        hidden-window wait's handler B. The third, op `0x95`'s choice table, is unused.
      - Op `0xcf` stays a stub.
      - `CallbackOwnershipTests` has a synthesized test for each path.
  - Consequence for content: a new scene that must support the menu and saving is a scene of its own, entered by
    SYSTEM4 through the SCJUMP registry with its own `0x7b` handlers, hotspots and `0x1ad` save-resume mark, as
    the shipped append scenes are (`oms/examples/mods/example.prologue`). A script reached by `run` must not wait
    for input while its caller's menu bindings are armed.

**Persistent global state.**

- **Only a prefix of each bank is saved.** Numbered saves serialize a mutable prefix of each global bank (Himegari:
  402,459 integer cells, i.e. below `0x6241b`, and 789 strings; Kamidori: 1,037,327 and 802). Initialization tables
  above the prefix are rebuilt at boot and never saved.
- **Consequence for native mods.** State that must survive a save under native AGE therefore has to occupy unused
  cells inside the prefix.
- **Sizes come from the SYS4INI file header.** The loader reads the first 300 header bytes into `ctx+0xa0bb8`.
  The saved-prefix counts are header `+0xf0`, mirrored at `ctx+0xa0ca8`. The full bank sizes are header `+0x114`,
  mirrored at `ctx+0xa0ccc` and copied to `bank_size_*` for allocation. See `sys4-format-notes.md`,
  "SYS4INI file header". The port's per-profile `bankDimensions` equal the saved-prefix counts, and a workspace
  test now checks this. The persistent/non-persistent split is therefore a toolchain declaration. A native mod
  needing more persistent cells could in principle raise the header prefix, since a load copies exactly the
  counted prefix into banks it has first zeroed in full, but that is untested.

**New scripts under native AGE.** Mounted `*.AAI` append catalogs (see "S4AC append catalogs") already provide the
mechanism:

- a catalog with an unused selector addresses its scripts as `selector << 24 | index`;
- loose basenames take precedence over the archive;
- op `0x143` runs each mounted catalog's record 0 during INIT2.

Generating such a catalog is the native route for mod-added scripts. It has not yet been exercised with a synthetic
catalog.

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


### SYS4433 live ADV aspect-mode layout metrics (2026-09-21)

`adv_text_append_horizontal_glyph_records@0x45b710` branches on text-manager
`+0x313d0` (opcode `0x2db`). For nonzero mode it replaces the ordinary GDI text
extent overflow check with cursor X plus reference `gmCellIncX / (3-byteLength)`
and cursor Y plus reference `gmCellIncY`. The signed shorts live at `+0x313e8`
and `+0x313ea`, within baseline GLYPHMETRICS at `+0x313d8`. Record right and next
cursor X use the same half/full reference advance. Record bottom is cursor Y plus
requested font size, plus effect Y when render mode is nonzero. Thus overflow
height and the published glyph rectangle height are distinct.

`text_rebuild_reference_gothic_metrics@0x44fd30` and
`text_measure_reference_gothic_glyph@0x44fe90` measure CP932 `0x8c83` (激) using
ＭＳ ゴシック, negative requested size, negative half-width, and current weight.
The port now uses that reference for nonzero-mode retained layout instead of
selected-font glyph advances/extents. Mode-zero behavior stays unchanged.

Font realization is a separate native layer: `text_create_calibrated_single_byte_font@0x451aa0`
selects `text_calibrate_mode1_glyph_font@0x451270` or
`text_calibrate_mode2_glyph_font@0x451840`; these consult natural font metrics and
reference glyph geometry. The 2026-09-21 change implemented only the layout rule. The calibration and the in-cell
placement and the surface-text draws followed on 2026-11-07 (the sections below). Display-aspect scaling is still
not modelled. The named
functions and findings are annotated and saved in the SYS4433 Ghidra image. Opcode evidence lives
in `vm-map/opcodes.toml`; regression/results are in `phase-a-slice-plan.md`.

#### Font family list (2026-11-07)

SYS4433 keeps a list of installed font families that ops `0x2dc` (count, or `-1` when empty), `0x2dd` (name) and
`0x2de` (find) read. Kamidori's SELFONT offers it. The startup reader (`0x415a40`) builds the list once with
`text_build_font_family_list@0x458040`:

1. `EnumFontFamiliesExA` over `SHIFTJIS_CHARSET` (lfOutPrecision 8, empty face) on a display IC, in GDI's order. The
   callback (`0x455010`) keeps a family when its FontType is not exactly `RASTER_FONTTYPE`, its `lfCharSet` is
   `SHIFTJIS_CHARSET` and its name does not start with `@`.
2. Every value name under `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink` is removed,
   except ten Japanese families (`0x470bc0`'s table at `0x57ef7c`..`0x57eff4`). The removed ones are Latin faces with
   linked Japanese fallbacks.
3. With `set:UseProportionalFont` 0, IPA Pゴシック, IPA P明朝 (`0x57d124`, `0x57d118`) and every family whose pitch is
   not `FIXED_PITCH` are removed.
4. A family that lacks any `VALIDFONTCHECKER` character is removed. "Lacks" means `GetGlyphOutlineA(GGO_METRICS)` gives
   a black box under 2x2 pixels in the face at lfHeight -64, lfWidth -32 and `DEFAULT_CHARSET`. The code asked for comes
   from `text_set_valid_font_checkers@0x40b060`:
   - a one-byte value is its byte;
   - otherwise the first two CP932 bytes combine as `b0 * 256 + (signed)b1` in 16 bits. A trail byte of `0x80` or more
     therefore lowers the lead byte by one: 悪 (`0x88AB`) is asked for as `0x87AB` and 洒 (`0x9FAD`) as `0x9EAD`.

Removal keeps the order of the rest, and each family keeps its `lfPitchAndFamily` for the calibration below.

**Port.** `FontFamilyCatalog` applies the rules over an `IFontFamilySource`. `WindowsGdiFontFamilySource` makes the
same ANSI GDI calls on a DISPLAY information context and reads the 32-bit registry view. On this machine the port's
list equals the one dumped from native AGE: 37 families in the same order. Tests: `FontFamilyCatalogTests`
(synthesized font system).

#### Aspect-mode font calibration (2026-11-07)

`text_rebuild_primary_font_resources@0x4522c0` rebuilds the primary font whenever its face, size, weight or the
aspect mode changes. `text_measure_glyph_with_aspect_mode@0x452020` picks its `lfWidth`:

- With the mode off, the width is `-height / 2`, as for any AGE font. The same holds for a face missing from the font
  family list, and for a fixed-pitch one other than IPA Pゴシック or IPA P明朝.
- Otherwise mode 1 calibrates it from 激 (`text_calibrate_mode1_glyph_font@0x451270`). `ref` is the reference glyph:
  ＭＳ ゴシック at the size, half width, and the weight. `nat` is the face's glyph at its natural width (lfWidth 0).
  - When the reference black box is no wider, the width is `ref.gmBlackBoxX / nat.gmCellIncX * -nat.tmAveCharWidth`.
  - Otherwise it is `nat.gmBlackBoxX / ref.gmCellIncX * -nat.tmAveCharWidth`.
  - Both are truncated. Kamidori's メイリオ bold comes out at -18 for both 20 and 22 pixels.

In aspect mode, outside ruby, a single-byte glyph gets a font of its own
(`text_create_calibrated_single_byte_font@0x451aa0`). The same function's single-byte branch calibrates that font
against that very character:

- The size is the font size `+0x313d4` for surface text, or the primary `lfHeight` (`-+0x35590`) for the ADV layout.
  The two are the same unless a display scale applies. The probe fonts take the primary weight (`+0x35594`, written
  together with `+0x4e0`).
- `font2` is the face at character height `trunc(size / nat.tmHeight * size)`, at its natural width: a cell about
  `size` tall.
- The width is `-font2.tmAveCharWidth`. When the character's black box in `font2` is wider than half of the stored
  reference 激's (`+0x313d8 >> 1`), it is instead `trunc((float)(ref.gmBlackBoxX * 0.5) / font2.gmBlackBoxX *
  -font2.tmAveCharWidth)`.
- The font keeps `lfHeight = -size`. Kamidori's メイリオ digits at 14 pixels come out at -9, as native GDI captures
  show.

The same faces as the primary font's are calibrated; any other gets `-size / 2`.

Not modelled: mode 2 (`text_calibrate_mode2_glyph_font@0x451840`), which no shipped game selects; and the
`display:AspectMode` setting, which calibrates every face when nonzero.

**Port.** `AspectFontRealization` holds the rules (`RequestedWidth`, `SingleByteWidth`). Its measurements come from an
`IFontMetricsProbe`, which is GDI in the Windows text backend. ADV text, History, op `0x82` and immediate surface text
use it. Tests: `AspectFontRealizationTests` (synthesized metrics).

#### Aspect-mode glyph placement (2026-11-07)

In aspect mode a proportional face's glyph is placed inside its fixed reference-width cell.
`text_draw_glyph_string_effect@0x460250` (at `0x460918`) and `text_draw_glyph_string_plain@0x45e4e0` (at `0x45e815`)
move the glyph right of the pen when all of these hold:

- the aspect mode is on;
- the text manager's aspect-layout flag `+0x35598` is set (the ADV append, History and op `0x82` set it; immediate
  surface text clears it);
- the glyph is not ruby;
- the primary face is in the font family list with its fixed-pitch bit clear (the IPA proportional faces get no
  exception here).

`text_classify_aspect_glyph@0x45c560` looks a double-byte glyph's lead and trail bytes up, with `strstr`, in two class
strings that the text-manager constructor (`0x4588d0`) copies to `+0x31484` and `+0x31504`. In mode 1:

- Class 0 is the closing brackets and 、。，．. The glyph keeps the pen.
- Class 1 is the opening brackets. The black box's right edge goes to the reference 激's black-box width:
  `trunc(+0x313d8 * scale + 0.5) - gmptGlyphOrigin.x - gmBlackBoxX` (`0x4609ab`). `+0x313d8` is the `gmBlackBoxX`
  that `text_rebuild_reference_gothic_metrics@0x44fd30` (through `text_measure_reference_gothic_glyph@0x44fe90`)
  measures with `GetGlyphOutline` format 0 (`GGO_METRICS`) on ＭＳ ゴシック at `lfHeight = -size`,
  `lfWidth = -(size / 2)`, the primary weight, `DEFAULT_QUALITY` and `DEFAULT_CHARSET`. At small sizes that is the
  embedded bitmap's box: 19 pixels for bold 22 px, where a `GGO_GRAY4_BITMAP` raster of the same font is 21 wide.
  メイリオ has no embedded bitmaps, so its metrics do not depend on the format.
- Every other glyph, and every single-byte one, is centred:
  `trunc(size / 2 + 0.5) - trunc(gmBlackBoxX * 0.5 + 0.5) - gmptGlyphOrigin.x`, with a quarter of the size for a single
  byte.

Any other nonzero mode always centres. AGE also multiplies the size and the reference width by a display scale
(`+0x40c->+0x4e0`, 1.0 unless `+0x488` is clear), which the port does not have.

The ADV builder also raises a glyph above its cell (`adv_text_append_horizontal_glyph_records@0x45b710` at `0x45be16`,
and History's renderer `@0x456490`). Let `d = trunc(tmAscent - ref.tmAscent - 0.5)` against the reference ＭＳ ゴシック,
and `e = trunc(tmAscent - 激.gmptGlyphOrigin.y - 1)` for the realized font's 激 (`+0x31430`). The raise is 0 when `d`
is 0, and otherwise the larger of `d` and `e`. A face as tall as the reference is not raised; Kamidori's メイリオ bold
at 22 pixels is raised 3. The record keeps the cell.

**Port.** `AspectGlyphPlacement.OffsetX` and `RetainedGlyphLayoutEngine.AspectGlyphRaise`. The class-1 width comes
from `AspectFontRealization.ReferenceBlackBoxWidth`, which measures 激 with `GGO_METRICS` as native does, not from
the reference raster. Tests: `AspectGlyphPlacementTests` and
`WindowsGdiGlyphMaskRasterizerTests.TheAspectReferenceBlackBoxIsTheEmbeddedBitmapsNotTheAntialiasedRasters`. Kamidori's
「 now lands on native's pixels.

#### SYS4433 glyph coverage alpha (2026-11-07)

`text_blit_glyph_bitmap_sys4433@0x45cff0` turns a gray-4 coverage value `c` (0..16) into alpha `c * 255 / 17`, so
full coverage is 240 and text never fully hides what lies under it. SYS4422 uses `c * 255 >> 4`. Its GGO_GRAY8
branch, `c * 255 / 65`, needs text manager `+0x35584` = 1, which nothing sets. An aliased glyph's set pixels are opaque
under both. The port picks the blit by engine ABI (`AgeGlyphMaskCompositor.CoverageAlphaFor`).

#### Aspect-mode surface text (2026-11-07)

Immediate surface text (ops `0x204` and `0x205`, `text_draw_string_to_surface_sys4433@0x44f7d0`) clears the
aspect-layout flag `+0x35598`, so no glyph is placed inside its cell. In aspect mode 1 it moves y by the reference
`tmAscent` (`+0x313f0`) minus the primary font's, so every glyph's baseline is `y + ref.tmAscent`; other modes keep the
primary font's baseline.

The text goes through one of two glyph-string draws:

- `text_draw_glyph_string_effect@0x460250` when an effect is on (render mode `+0x55c`);
- `text_draw_glyph_string_plain@0x45e4e0` otherwise.

In both, a double-byte glyph uses the calibrated primary font, and a single-byte one gets its own font ("Aspect-mode
font calibration"). Each glyph draws at the pen. The two draws advance differently:

| Draw | Glyph | Blank (0x20, 0x8140, control byte) |
|---|---|---|
| plain | its own `gmCellIncX` | `BLANKEXTENTMODE` 1: its extent. Otherwise the reference 激 cell `+0x313e8`, halved for one byte |
| effect (modes 1, 2) | `trunc(+0x313e8 / (one byte ? 2.0 : 1.0) + 0.5)`, whatever its own cell | `BLANKEXTENTMODE` 1 in mode 1: its extent. Otherwise the font size `+0x313d4`, halved for one byte |

A blank's extent is `text_measure_blank_extent@0x404da0`: `GetTextExtentPoint32A` of the character in the reference
ＭＳ ゴシック. Kamidori sets `BLANKEXTENTMODE` 1. The effect draw also keeps a glyph cache (`0x423ef0`), so a glyph it
has drawn once is not asked of GDI again.

Op `0x205`'s formatter, `text_format_integer_for_draw@0x407260`, does not draw padding. It returns only the digits and
moves x right by the skipped positions times a blank width.

Kamidori's CONFIG2 page (ADV設定) draws its values in 14 px メイリオ with the outline on. Native GDI captures show each
digit drawn at lfWidth -9 with `gmCellIncX` 6, yet each advances 7: half the reference cell of 14. The port's CONFIG2
shot matches native's within 1 (`kamidori-config-redraw.steps`).

**Port.** `ImmediateSurfaceTextRenderer.RenderAspect`, with `BLANKEXTENTMODE` from `Sys4StartupSettings.BlankExtentMode`.
Native also measures a control byte's extent; the port gives a control byte the fallback advance instead. Tests:
`ImmediateSurfaceAspectTextTests`.


### Surface resource query 0x216 and Kamidori battle transition (2026-09-22)

The legacy mnemonic `query-gfx-field?` actually queries a surface's resource id.
`op_0x216_query_surface_resource@0x42a0f0` (SYS4422) reads the inline record word at
ctx+0x46d14+slot*0x14; SYS4433's corresponding handler is 0x42af80, with base
ctx+0x4af9c. This supersedes the earlier per-object/independent-table interpretation.
The existing native surface-record lifetime findings above identify its producers.

Installed loose Kamidori BTL.BIN@0x30e0 stores the old resource from slot 0xc2+side
in local0xfda. After replacing that surface at 0x3188, 0x319d loads the saved id
into slot 0x2b+side, queries its dimensions, and uses it in the transition.
The port's never-populated independent query table returned zero, incorrectly
selecting SYSTEM4.BIN. QueryField now reads the existing surface state under its
lock, returning -1 for missing/created slots. No asset warning is suppressed.
Mode-1 raw-load bookkeeping and failed host decode state remain separate existing
surface-model limitations; this change addresses the ordinary 0x1f9 battle path.

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

## Native walls backlog (targets for this loop)

- ~~**call-script dispatch**~~ — **SOLVED** (above): `call-script <id>` = raw SYS4INI file index.
- **decision→scene** — how `0x62ccf`/the decision selects the next `SCxxxx`. Now narrower: scenes load
  via `call-script`/the same SYS4INI-index loader, so the open question is only where the decision
  value is turned into a scene *id* (a caller of SCJUMP; re-aimed away from `u00428010`).
- **op `0x60`** (`u0041A270`) — the rand-like value gating 1732/1755 SCJUMP decisions.
- **gfx command-buffer** — the `0x212–0x21a` positioned-object subsystem (`scjump`-unrelated; the
  rendering drift).

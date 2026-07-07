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
- Escalation (unused so far): `tools/pe-sieve32.exe /pid <PID> /imp 3 /dmode 3 /dir <out>` (run from
  **PowerShell**, not Git Bash — it mangles `/flags`) rebuilds the IAT into a clean PE. Only needed if
  raw-dump analysis is inadequate; it was fine for reading logic, so we stayed on the raw dump.

---

## Master key — the opcode→handler dispatch table (2026-07-07)

The interpreter dispatches each op via a per-context handler table:

> **`handler(op) = *(ctx + 0x9b8f4 + op*4)`**  — `ctx` = the engine context (`esi` in handlers, thiscall).

The table is populated by the registration routine **`FUN_00413860`** as a long run of
`MOV dword ptr [ESI + 0x9b8f4 + op*4], <handler_va>`. Sample (from ~`0x414756`):

| slot offset | handler VA |
|---|---|
| `0x9b8f4` | `0x42bd90` |
| `0x9b8f8` | `0x427ed0` |
| `0x9b8fc` | `0x427fb0` (the save handler below) |
| `0x9b900` | `0x416b70` |
| `0x9b904` | `0x428100` |
| `0x9b908` | `0x428240` |

**Why this matters:** the Kelebek `u00XXXXXX` opcode names encode handler VAs from *Kelebek's* build,
which **drift** in our build (confirmed below). This table resolves the *real* handler for any opcode
in our image — the general fix for VA drift across the whole project. (Anchoring the slot↔opcode index
is the immediate next step: read the dispatch site's exact base, then `slot = base + op*4`.)

**Other confirmed engine-context offsets** (`ctx`/`esi`): `+0x53d14` = current gfx-object index;
`+0x53d88` = per-object cmd-type table (stride `0x78` = 120 bytes); operand-fetch helper = `call
0x41b940` (thiscall, `ecx=ctx`, arg = operand index → returns the operand value).

---

## Findings

### op `0x1a2` (`u00428010`) is a SAVE/resource-file op — NOT decision→scene (2026-07-07)

The SCJUMP slice assumed `u00428010` resolved a decision value to a scene. **That premise is wrong:**

- Kelebek's `u00428010` = **opcode `0x1a2`, argc 1**. In *our* build, VA `0x428010` is **inside a
  different function** (`0x427fb0`) — Kelebek-VA drift.
- `0x427fb0` is a genuine interpreter handler (uses `ctx+0x53d14`, the `ctx+0x53d88` cmd-type table,
  and the `0x41b940` operand-fetch). It builds file paths from the format string at `0x571e70` =
  **`%s\SAVE%2.2d.DAT`** (and a second at `0x571e84`) via an sprintf-style `0x407770`. It is
  **multi-operand** (fetches operands 2 and 3) — inconsistent with the argc-1 op `0x1a2`, reconfirming
  the drift.
- **Conclusion:** op `0x1a2`/`u00428010` is a **save/resource-file** op (matches its earlier tentative
  "resource" tag). So the SCJUMP consumer pattern `lookup(0x5f0ed, decision); mov(ptr,1);
  u00428010(ptr)` is **persisting the "visited-decision" flag into the save data**, *not* loading a
  scene. The real decision→scene resolution is a **different** mechanism — most likely the same native
  boundary as call-script/script-load (next investigation).

*(To pin op `0x1a2`'s exact handler in our build: index the dispatch table above by `0x1a2`.)*

---

## Native walls backlog (targets for this loop)

- **decision→scene** — how `0x62ccf`/the decision actually selects the next `SCxxxx` (re-aimed away
  from `u00428010`; likely call-script-adjacent).
- **call-script dispatch** — `call-script <id>` → engine entry point (`name-resolution.md §1`).
- **op `0x60`** (`u0041A270`) — the rand-like value gating 1732/1755 SCJUMP decisions.
- **gfx command-buffer** — the `0x212–0x21a` positioned-object subsystem (`scjump`-unrelated; the
  rendering drift).

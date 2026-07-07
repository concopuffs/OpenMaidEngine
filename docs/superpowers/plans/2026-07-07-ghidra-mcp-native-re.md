# Ghidra + MCP Native-RE Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to run this plan with review checkpoints. This is a **runbook**, not code-TDD — steps alternate between **[you]** (setup / Ghidra GUI / launching the game) and **[me]** (MCP-driven analysis). Each task ends at a concrete gate. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Stand up a "Claude drives Ghidra over MCP" loop and prove it by reversing `u00428010` (SCJUMP's native decision→scene resolver) to a concrete verdict — recover the `0x62ccf → SCxxxx.BIN` mapping, or pinpoint exactly why it's runtime-only.

**Architecture:** Load our existing unpacked engine dump (`build/engine-dump/range_00400000.bin`) into Ghidra 12.1.2; connect the bethington/ghidra-mcp bridge so I can call `decompile_function`/`get_xrefs_*`/`read_memory`; drive the analysis of `u00428010` and the `0x5f0ed` array; capture findings in `docs/engine-re.md`.

**Tech Stack:** Ghidra 12.1.2 + bethington/ghidra-mcp (Java 21, Maven 3.9+, Python 3.10+, `uv`); pe-sieve v0.4.1.1 (escalation only); the game (JP locale) for a fresh dump if escalating.

## Global Constraints

- **Analysis only — no redistribution.** The engine dump / any reconstructed PE stay local.
- **Substitute your paths** where marked: `<GHIDRA_DIR>` = your Ghidra 12.1.2 install (README example `C:\ghidra_12.1.2_PUBLIC`); `<MCP_DIR>` = your `ghidra-mcp` clone; `<PID>` = the running game's process id.
- **Run pe-sieve from PowerShell, not the Bash tool** — Git Bash mangles `/pid` `/imp` `/dir` into paths. (`& .\tools\pe-sieve32.exe /pid <PID> /imp 3 /dir <out>`.)
- Findings live in `docs/engine-re.md` (new canonical doc); resolved op semantics also go to `vm-map/opcodes.toml` (rebuild with `opcodes_build.py --build`). One fact, one home.
- Git repo root is `age-reimpl/`; commit docs from there. Commit trailer: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

**Reference facts (verified 2026-07-07):**
- Engine dump: `build/engine-dump/range_00400000.bin` (2,490,368 bytes) is the **full module image at base 0x400000** (memory-laid-out). VA→file offset = `VA − 0x400000`, so `u00428010`'s handler region is at file offset `0x28010`, and the AGF landmark VA `0x474f1f` is at `0x74f1f`.
- Target: consumers run `lookup-array(ptr, 0x5f0ed, 0x62ccf)` then `u00428010(ptr)`. Kelebek names the op by its handler VA `0x428010` (may drift in our build → resolve via the interpreter op-dispatch table `[esi+idx*120+0x53d88]` or xrefs from the operand-fetch `call 0x41b940`). Interpreter runs from the module @0x400000 (stable base).
- `0x62ccf` decision values: 847 distinct, range 0..1693 (from `build/scjump-decisions.json`). Scene files are `SC0000..SC1690` (see `paths.scripts()` / `build/asset-sections.json`).
- pe-sieve: v0.4.1.1 (x86) at `tools/pe-sieve32.exe`; `/imp 3` = rebuild ImportTable from scratch.

---

### Task 1: Bring the environment up — image loaded + MCP connected

Deliverable: I can call a bethington MCP tool against the loaded engine image and get a real result.

- [ ] **Step 1 [you]: Build + deploy the Ghidra MCP plugin**

```bash
git clone https://github.com/bethington/ghidra-mcp.git <MCP_DIR>
cd <MCP_DIR>
python -m tools.setup ensure-prereqs --ghidra-path "<GHIDRA_DIR>"
python -m tools.setup build
python -m tools.setup deploy --ghidra-path "<GHIDRA_DIR>"
```

Expected: build succeeds; the extension is deployed into `<GHIDRA_DIR>`.

- [ ] **Step 2 [you]: Load the engine image in Ghidra**

1. Start Ghidra → new project → **File > Import File** → select
   `S:\Game Hacking\Eushully\Himegari\age-reimpl\build\engine-dump\range_00400000.bin`.
2. In the import dialog: **Format = Raw Binary**, **Language = `x86:LE:32:default`** (compiler: `windows`).
3. Open it in CodeBrowser. Set the image base: **Window > Memory Map** → set the block's start to
   `0x00400000` (or set `Options > Image Base = 0x400000` at import).
4. Run **Analysis > Auto Analyze** (accept defaults; ensure Decompiler + Function ID are on).

Expected: auto-analysis completes; the Listing shows disassembled functions.

- [ ] **Step 3 [you]: Enable + start the MCP server in Ghidra, then the bridge**

1. **File > Configure > Configure All Plugins > GhidraMCP** → check the box.
2. **Tools > GhidraMCP > Start MCP Server** (serves `http://127.0.0.1:8089/`).
3. Verify HTTP: `curl http://127.0.0.1:8089/check_connection` → expect a success/OK response.
4. Launch the stdio bridge:

```bash
cd <MCP_DIR>
uv run bridge-mcp-ghidra
```

- [ ] **Step 4 [you]: Register the server in Claude Code**

Add to the project MCP config (`.mcp.json` at the workspace root, or `claude mcp add-json ghidra …`):

```json
{
  "mcpServers": {
    "ghidra": {
      "command": "uv",
      "args": ["run", "--directory", "<MCP_DIR>", "bridge-mcp-ghidra"]
    }
  }
}
```

Then reconnect the session so the `ghidra` MCP tools register (they arrive as deferred tools I fetch via ToolSearch). Tell me when it's connected.

- [ ] **Step 5 [me]: Connectivity gate**

I `ToolSearch` for the ghidra tools (e.g. `select:list_functions,decompile_function,get_function_by_address`), then call `list_functions` (or `check_connection`).
Gate: a non-empty function list comes back → the loop works, proceed. If it errors → we debug the bridge/Ghidra-server/config before continuing (fallback: you paste decompiler output; degraded but unblocked).

---

### Task 2: Confirm image quality at the target; escalate to pe-sieve only if needed

Deliverable: a confirmed-legible decompilation at the `u00428010` region (raw dump, or a pe-sieve-rebuilt PE).

- [ ] **Step 1 [me]: Sanity-check the load + assess the target region**

- Validate the dump: `get_function_by_address(0x474f1f)` / `read_memory` near it — confirm it reads as real code around the `BM` (`0x4D42`) check (the recorded AGF-decoder landmark).
- Navigate to `0x428010`: `get_function_by_address(0x428010)` + `decompile_function`. Assess whether the decompiled output is coherent (recognizable control flow, memory refs) or garbage/truncated.

Gate: coherent → skip to Task 3. Garbage/incomplete (bad section boundaries, unresolved refs blocking reading) → escalate (Step 2).

- [ ] **Step 2 [you, only if escalating]: pe-sieve a clean, import-rebuilt PE**

With the game running (JP locale), from **PowerShell**:

```powershell
# NOTE: do not name this $pid — that is a PowerShell automatic variable (the shell's own PID).
$agePid = (Get-Process AGE*).Id         # confirm this returns exactly one id
& "S:\Game Hacking\Eushully\Himegari\age-reimpl\tools\pe-sieve32.exe" /pid $agePid /imp 3 /dmode 3 /dir "S:\Game Hacking\Eushully\Himegari\age-reimpl\build\engine-pe"
```

Expected: an output folder under `build/engine-pe\process_<PID>\` containing the rebuilt AGE module PE (named by base, e.g. `400000.AGE.EXE`). Validate: it contains plaintext `SYS4422` / `.BIN` / `DATA1` strings (recorded dump-sanity check) — e.g. `Select-String -Path <dumped.exe> -Pattern 'DATA1' -Encoding ascii`.

- [ ] **Step 3 [you, only if escalating]: Re-import the rebuilt PE in Ghidra**

Import the pe-sieve module as **Format = Portable Executable (PE)** (base 0x400000 is intact), auto-analyze, keep the MCP server pointed at this program. Tell me when ready; I re-run Step 1's assessment.

Gate: coherent decompilation at `0x428010` → proceed to Task 3.

---

### Task 3: Reverse `u00428010` → decision→scene verdict

Deliverable: an evidence-backed answer to "what scene does decision N load?", or a precise statement of the runtime dependency + the Frida hook that would resolve it. **[me]**, MCP-driven.

- [ ] **Step 1: Locate the real handler**

`get_function_by_address(0x428010)`. If it lands mid-function or on non-handler code (Kelebek build drift): find the true handler via xrefs — `get_xrefs_to` around the op-dispatch table `[esi+idx*120+0x53d88]`, or from the operand-fetch path `0x41b940` — and via `list_functions` near 0x428010.

- [ ] **Step 2: Decompile + read the handler**

`decompile_function` on the handler. Read: how it consumes its pointer argument (the `&0x5f0ed[decision]` reference — begin/element), what struct fields it reads, what it returns, and what it calls (esp. anything that looks like a file/scene load or a table index).

- [ ] **Step 3: Chase the data**

Follow references from the handler (and from `0x5f0ed`) to any static table in `.data`: `get_xrefs_to(0x5f0ed)`, `list_data_items` around it, `read_memory` on the referenced table(s). Determine whether the decision index maps — directly or via `0x5f0ed` — to a scene id / filename / SYS4INI section.

- [ ] **Step 4: Cross-check against the corpus**

For candidate scene ids/indices the table yields, cross-check against real `SC####.BIN` names (`paths.scripts()`) and `build/asset-sections.json`. If a decision value resolves to an existing scene, that's the mapping. Spot-check 2–3 known decisions end-to-end.

- [ ] **Step 5: Verdict**

Write the conclusion:
- **(a) Static mapping recovered** — the `decision → scene` table/formula, with the cross-checked evidence. (This would finish SCJUMP's open boundary as a *static* win.)
- **(b) Runtime-dependent** — exactly what live state the resolution needs that the table doesn't encode, and the precise Frida hook (address + what to log) that would capture it.

Gate: one of (a)/(b) with concrete evidence. Report it to you before writing docs.

---

### Task 4: Capture findings + updates

Deliverable: committed docs recording the result and the repeatable workflow.

- [ ] **Step 1 [me]: Write `docs/engine-re.md`**

Create the canonical native-RE doc with two parts:
1. **Runbook** — "How to run the Ghidra + MCP loop": the bethington setup (build/deploy, enable plugin, start server:8089, bridge, Claude Code registration), loading `build/engine-dump/range_00400000.bin` (raw, base 0x400000, `x86:LE:32`), the AGF landmark sanity check (VA 0x474f1f), and the pe-sieve escalation recipe (`/imp 3 /dmode 3`, PowerShell). Cross-link `vm-mapping-plan.md` appendix rather than duplicating the packing detail.
2. **Findings — `u00428010` (decision→scene)** — handler address (as-found), decompiled behavior, the `0x5f0ed` + table structure, and the Task 3 verdict (mapping or runtime hook). Leave a "native walls backlog" list (call-script dispatch, op 0x60, gfx command-buffer) as future sections.

- [ ] **Step 2 [me]: Propagate the result**

- `docs/scjump-progression.md` — update the "native decision→scene boundary" section with the verdict (mapping recovered → link the table; or the identified runtime hook).
- `vm-map/opcodes.toml` — if `u00428010` (or any op identified en route) earned real semantics, add/enrich its entry; run `py -3.11 -X utf8 tools/opcodes_build.py --build`.
- `CLAUDE.md` canonical-documents map — add: `Native-engine RE (decompiled AGE.EXE) → docs/engine-re.md` (workspace root; edit on disk, not in the repo).
- `docs/tools-reference.md` — add `pe-sieve32.exe` (dump-prep) if it was used; note its home (`tools/` or move to `bin/` per PROJECT-STRUCTURE).
- `docs/PROJECT-STRUCTURE.md` — add `docs/engine-re.md` and (if kept) `pe-sieve32.exe`.

- [ ] **Step 3 [me]: Commit docs + update status memory**

```bash
git add docs/engine-re.md docs/scjump-progression.md docs/tools-reference.md docs/PROJECT-STRUCTURE.md vm-map/opcodes.toml docs/opcode-reference.md
git commit -m "docs: native-engine RE — Ghidra+MCP workflow + u00428010 decision->scene findings

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

Update `~/.claude/…/memory/himegari-port-status.md` + `MEMORY.md`: record that the Ghidra+MCP native-RE loop is stood up (bethington/ghidra-mcp on the 0x400000 module dump) and the `u00428010` verdict; note it's the reusable path for the remaining native walls. Convert 2026-07-07 to the absolute date. (Memory is outside the repo — no commit.)

---

## Notes for the implementer

- **This is a runbook with a hard human/tool dependency** (Ghidra GUI + MCP bridge + possibly the running game). Stop at each gate; don't fabricate analysis results — every finding in Task 3/4 must come from an actual MCP call.
- **A "runtime-dependent" verdict is still success** — it validates the workflow and converts an open wall into a defined Frida task.
- **VA drift**: never trust the raw Kelebek VA blindly; confirm the handler via the dispatch table / xrefs (Task 3 Step 1).
- **If the MCP bridge can't be made to work**, fall back to pasted decompiler output for `u00428010` — slower (no automated xrefs) but the analysis and docs still land.
- The exact bethington commands/config are from the repo README (2026-07-07); if the repo has since changed, follow its current README for build/bridge/registration and keep the rest of this plan.

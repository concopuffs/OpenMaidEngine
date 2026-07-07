# Ghidra + MCP native-RE workflow (proven on `u00428010`) — design

**Date:** 2026-07-07
**Status:** approved (brainstorming) → ready for implementation plan
**Slice:** native-engine RE enablement; first target = SCJUMP's decision→scene resolver.

---

## 1. Problem & goal

We keep hitting the same wall: logic compiled into `AGE.EXE` that scripts call as native ops —
**decision→scene (`u00428010`), call-script dispatch, op 0x60 (the rand-like SCJUMP gate), the gfx
command-buffer.** Frida (dynamic observation) has taken us far but stalls where the native logic is a
complex state machine that must be *read*, not just watched. The blocker that previously ruled out
static analysis (packed exe) is gone — we have the in-place-unpacked engine dumped
(`build/engine-dump/`, module @0x400000, validated at the AGF landmark 0x474f1f).

**Goal:** stand up a repeatable "Claude drives Ghidra over MCP" loop and **prove it end-to-end on one
native function — `u00428010`, the SCJUMP decision→scene resolver** — to a concrete result: either
recover the `0x62ccf → SCxxxx.BIN` mapping (finishing the SCJUMP slice), or establish precisely what
makes it runtime-only. Success de-risks the whole approach and leaves a documented, reusable process
for the other native walls.

## 2. Non-goals (this slice)

- **Not** reversing call-script dispatch, op 0x60, or the gfx command-buffer — those are follow-on
  slices that reuse the proven loop. One target only.
- **Not** building custom RE tooling — we use an off-the-shelf Ghidra MCP server; we don't write our
  own bridge.
- **No redistribution** of engine binaries — the dump/PE stay local, for analysis only.
- Not committing to reimplementing `u00428010` in the VM this slice; the deliverable is the
  *understanding* (the mapping/table or a clear runtime verdict). Reimplementation is a later step.

## 3. Environment (confirmed 2026-07-07)

- **Have:** Ghidra 12.1.2 installed; the game runs locally (JP locale); Frida 17.15.3;
  `build/engine-dump/` (raw runtime dump: `manifest.json` + `range_<base>.bin`, module base 0x400000).
- **Need (this slice sets up):** the bethington/ghidra-mcp server (plugin + bridge) registered in
  Claude Code; possibly pe-sieve.exe (only if raw-dump analysis is inadequate).
- **Target facts:** consumers do `lookup-array(ptr, 0x5f0ed, 0x62ccf)` then `u00428010(ptr)`. Kelebek
  names the op by its handler VA `0x428010` (may drift in our build → resolve via the dispatch table
  `[esi+idx*120+0x53d88]` / proximity). The interpreter executes from the **module @0x400000**
  (stable base) — not the per-run heap (an earlier worry, since corrected).

## 4. The MCP server — bethington/ghidra-mcp

Chosen over LaurieWired/GhidraMCP because it explicitly exposes the primitives this analysis needs:
`decompile_function`, `get_xrefs_to`/`get_xrefs_from`, `get_function_by_address`, `read_memory`,
`list_data_items`, `search_byte_patterns`, struct/enum tools. Actively maintained (v5.14.2, Jun 2026),
stdio-MCP transport designed for Claude Code. Requires Ghidra 12.1.2 (matches ours); prereqs Java 21,
Maven 3.9+, Python 3.10+. The 256-tool surface is a non-issue for Claude Code — tools are deferred/
searchable, so only the handful used are loaded via ToolSearch.

**Setup (user, one-time):** build + deploy the Ghidra extension (`python -m tools.setup ...`), start
the HTTP-serving plugin inside Ghidra (127.0.0.1:8089), run the MCP bridge, and register the server in
Claude Code's MCP config. A connectivity check (I call `list_functions` and get a non-empty result)
gates progress to analysis.

**Fallback:** if the bridge proves painful, I can still work from decompiler output the user pastes,
or a Ghidra headless export — degraded (no automated xrefs) but not blocked.

## 5. Analyzable image — raw-dump-first, pe-sieve as escalation

For reading one function's logic, a perfect IAT is often unnecessary (unnamed API calls are fine when
the function is a table lookup). So:

1. **Stage A — try the existing raw dump** (`build/engine-dump/range_<0x400000>.bin`), imported into
   Ghidra as a raw 32-bit x86 binary at base 0x400000, auto-analyzed. Zero new setup. If the decompiler
   renders `u00428010`'s region legibly, prep is done.
2. **Stage B — escalate to pe-sieve only if Stage A is inadequate:** with the game running,
   `pe-sieve.exe /pid <PID> /imp 3` rebuilds the IAT into a clean PE (recorded in
   `vm-mapping-plan.md` appendix; pe-sieve is a single portable exe, Bash-drivable while the user has
   the game up). Validate the dump by confirming plaintext `SYS4422`/`.BIN`/`DATA1` strings appear.

The escalation is triggered by observation (illegible/incomplete decompilation), not assumed up front.

## 6. Ghidra load parameters

32-bit x86 (`x86:LE:32:default`), image base `0x400000`. For the raw dump: import as "Raw Binary",
set the language + base, run auto-analysis (decompiler, function ID, data references). For a pe-sieve
PE: standard PE import. Confirm the AGF landmark (VA 0x474f1f contains the `BM`/0x4D42 check) reads as
real code — the recorded dump-sanity anchor.

## 7. Analysis plan for `u00428010`

Driven by me over MCP:
1. **Locate the real handler.** Try `get_function_by_address(0x428010)`; if it lands mid-function or on
   non-handler code (Kelebek build drift), find it via the interpreter's op dispatch table
   (`[esi+idx*120+0x53d88]`) or by xrefs from the operand-fetch path (`call 0x41b940`).
2. **Decompile it** and read how it consumes its pointer argument (the `&0x5f0ed[decision]` reference)
   — what fields it reads, what it returns, what it calls.
3. **Chase the data.** Follow xrefs/reads to any static table in `.data`: does the decision index map,
   directly or via `0x5f0ed`, to a scene id / filename / SYS4INI section? `list_data_items` +
   `read_memory` on the referenced table; cross-check candidate scene ids against the real corpus
   (SC#### filenames) and `build/asset-sections.json`.
4. **Verdict.** Either (a) recover the `decision → scene` mapping (ideally a static table we can export
   → a *static* win finishing SCJUMP), or (b) determine it's computed at runtime (e.g. from live state
   the table doesn't encode) and document exactly what's needed to resolve it (a Frida hook point).

**Success criterion:** a concrete, evidence-backed answer to "what scene does decision N load?", or a
precise statement of why it can't be answered statically + the identified runtime hook.

## 8. Findings capture

- **New canonical doc `docs/engine-re.md`** — "native-engine reverse engineering": the home for
  decompiled native-op findings. This slice writes the `u00428010` section (handler address, decompiled
  behavior, the `0x5f0ed`/table structure, the decision→scene verdict). Future native walls (call-script,
  op 0x60, gfx buffer) get sections here. Includes a short "how to run the Ghidra+MCP loop" runbook
  (setup + connect) so it's repeatable.
- **`vm-map/opcodes.toml`** — if `u00428010` (and any native op identified along the way) gets real
  semantics, record them there (its existing home); rebuild via `opcodes_build.py --build`.
- **`docs/scjump-progression.md`** — update the "native decision→scene boundary" section with the
  result (mapping recovered, or the precise runtime dependency).
- **CLAUDE.md canonical-documents map** — add a row: `Native-engine RE (decompiled AGE.EXE) →
  docs/engine-re.md`.
- If pe-sieve is used, note the dump-prep recipe in `docs/engine-re.md` (and reference the existing
  `vm-mapping-plan.md` appendix rather than duplicating).

## 9. Risks & fallbacks

- **MCP setup friction / version mismatch** → fallback to pasted decompiler output or headless export
  (degraded, not blocked). A connectivity gate (§4) catches this before analysis.
- **Anti-debug / protector** → attach post-title (already works for our Frida dump); pe-sieve is a
  read-only scan (low risk). Not expected to affect Ghidra static analysis of an already-captured image.
- **VA drift (Kelebek build ≠ ours)** → resolve the handler via the dispatch table, not the raw VA.
- **The answer is genuinely runtime** → that is still a *successful* outcome for this slice: it
  validates the workflow and pinpoints the exact Frida hook, converting an open wall into a defined task.

## 10. Definition of done

1. bethington/ghidra-mcp connected in Claude Code; I can `list_functions` / `decompile_function` on the
   loaded engine image.
2. `u00428010` located and decompiled; its use of `0x5f0ed` and any static table understood.
3. A verdict on decision→scene: recovered mapping (with evidence) **or** a precise runtime-hook
   statement.
4. `docs/engine-re.md` written (findings + repeatable runbook); `scjump-progression.md` and (if
   applicable) `opcodes.toml` + CLAUDE.md updated; status memory updated.

## 11. Implementation order (for the plan)

1. Set up bethington/ghidra-mcp (user) + register in Claude Code; connectivity gate.
2. Load the raw engine dump in Ghidra (32-bit, base 0x400000); confirm the AGF landmark; assess decomp
   quality. Escalate to pe-sieve `/imp 3` only if inadequate.
3. Locate + decompile `u00428010`; chase `0x5f0ed` + referenced tables; reach the decision→scene verdict.
4. Write `docs/engine-re.md` (findings + runbook); update `scjump-progression.md`, `opcodes.toml` (if
   applicable), CLAUDE.md map, status memory.
